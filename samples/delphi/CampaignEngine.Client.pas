unit CampaignEngine.Client;

{
  Campaign Engine client for Delphi (XE8 or newer: System.Net.HttpClient + System.JSON).

  Older Delphi versions (e.g. Delphi 7 / 2007) can use Indy's TIdHTTP with the same JSON bodies:
  set Request.CustomHeaders.Values['X-Api-Key'] and Request.ContentType := 'application/json',
  then POST a TStringStream. Nothing else is required by the API.

  Money is Currency (4 fixed decimals, no floating point surprises) and is always written with
  '.' as decimal separator, whatever the Windows locale is (Turkish Windows uses ',').
}

interface

uses
  System.SysUtils, System.Classes, System.JSON, System.NetEncoding, System.Net.HttpClient, System.Net.URLClient;

type
  ECampaignEngineError = class(Exception)
  private
    FStatusCode: Integer;
    FBody: string;
  public
    constructor Create(AStatusCode: Integer; const ABody: string);
    property StatusCode: Integer read FStatusCode;
    property Body: string read FBody;
  end;

  /// Builds the cart JSON the API expects.
  TCartBuilder = class
  private
    FCart: TJSONObject;
    FLines: TJSONArray;
    FCoupons: TJSONArray;
  public
    constructor Create(const AChannel, AStoreId: string; const ACurrency: string = 'TRY');
    destructor Destroy; override;
    function AddLine(const ALineId, ASku: string; AQuantity, AUnitPrice: Currency;
      const ACategories: array of string; const ABrand: string = ''): TCartBuilder;
    function AddCoupon(const ACode: string): TCartBuilder;
    function SetCustomer(const ACustomerId: string; const ASegments: array of string): TCartBuilder;
    function SetPayment(const AMethod, ACardBin: string; AInstallments: Integer): TCartBuilder;
    function ToJson: string;
  end;

  TCampaignEngineClient = class
  private
    FHttp: THTTPClient;
    FBaseUrl: string;
    FApiKey: string;
    function Send(const AMethod, APath, ABody: string): TJSONValue;
  public
    constructor Create(const ABaseUrl, AApiKey: string);
    destructor Destroy; override;

    /// POST /api/v1/evaluate. Caller frees the result.
    function Evaluate(const ACartJson: string; AExplain: Boolean = False): TJSONObject;
    /// POST /api/v1/redemptions. Idempotent: retry with the same transaction id after a timeout.
    function Redeem(const ATransactionId, ACartJson: string): TJSONObject;
    /// POST /api/v1/redemptions/{id}/reverse (void / full return).
    function Reverse(const ATransactionId, AReason: string): TJSONObject;
  end;

function MoneyToJson(AValue: Currency): string;
function JsonToMoney(AValue: TJSONValue): Currency;

implementation

var
  InvariantFormat: TFormatSettings;

function MoneyToJson(AValue: Currency): string;
begin
  Result := CurrToStr(AValue, InvariantFormat);
end;

function JsonToMoney(AValue: TJSONValue): Currency;
begin
  // Numbers arrive as JSON numbers; parse the text to avoid a Double round trip.
  Result := StrToCurr(AValue.Value, InvariantFormat);
end;

function JsonStringArray(const AValues: array of string): TJSONArray;
var
  Value: string;
begin
  Result := TJSONArray.Create;
  for Value in AValues do
    Result.Add(Value);
end;

{ ECampaignEngineError }

constructor ECampaignEngineError.Create(AStatusCode: Integer; const ABody: string);
begin
  inherited CreateFmt('Campaign Engine returned HTTP %d: %s', [AStatusCode, ABody]);
  FStatusCode := AStatusCode;
  FBody := ABody;
end;

{ TCartBuilder }

constructor TCartBuilder.Create(const AChannel, AStoreId, ACurrency: string);
begin
  inherited Create;
  FCart := TJSONObject.Create;
  FCart.AddPair('channel', AChannel);
  FCart.AddPair('storeId', AStoreId);
  FCart.AddPair('currency', ACurrency);
  FLines := TJSONArray.Create;
  FCoupons := TJSONArray.Create;
  FCart.AddPair('lines', FLines);
  FCart.AddPair('couponCodes', FCoupons);
end;

destructor TCartBuilder.Destroy;
begin
  FCart.Free; // owns FLines and FCoupons
  inherited;
end;

function TCartBuilder.AddLine(const ALineId, ASku: string; AQuantity, AUnitPrice: Currency;
  const ACategories: array of string; const ABrand: string): TCartBuilder;
var
  Line: TJSONObject;
begin
  Line := TJSONObject.Create;
  Line.AddPair('lineId', ALineId);
  Line.AddPair('sku', ASku);
  // Sent as strings: the API accepts numbers as strings and this keeps the exact decimal value.
  Line.AddPair('quantity', MoneyToJson(AQuantity));
  Line.AddPair('unitPrice', MoneyToJson(AUnitPrice));
  Line.AddPair('categories', JsonStringArray(ACategories));
  if ABrand <> '' then
    Line.AddPair('brand', ABrand);
  FLines.AddElement(Line);
  Result := Self;
end;

function TCartBuilder.AddCoupon(const ACode: string): TCartBuilder;
begin
  FCoupons.Add(ACode);
  Result := Self;
end;

function TCartBuilder.SetCustomer(const ACustomerId: string; const ASegments: array of string): TCartBuilder;
var
  Customer: TJSONObject;
begin
  Customer := TJSONObject.Create;
  Customer.AddPair('id', ACustomerId);
  Customer.AddPair('segments', JsonStringArray(ASegments));
  FCart.AddPair('customer', Customer);
  Result := Self;
end;

function TCartBuilder.SetPayment(const AMethod, ACardBin: string; AInstallments: Integer): TCartBuilder;
var
  Payment: TJSONObject;
begin
  Payment := TJSONObject.Create;
  Payment.AddPair('method', AMethod);
  if ACardBin <> '' then
    Payment.AddPair('cardBin', ACardBin);
  Payment.AddPair('installments', TJSONNumber.Create(AInstallments));
  FCart.AddPair('payment', Payment);
  Result := Self;
end;

function TCartBuilder.ToJson: string;
begin
  Result := FCart.ToJSON;
end;

{ TCampaignEngineClient }

constructor TCampaignEngineClient.Create(const ABaseUrl, AApiKey: string);
begin
  inherited Create;
  FBaseUrl := ABaseUrl.TrimRight(['/']) + '/api/v1';
  FApiKey := AApiKey;
  FHttp := THTTPClient.Create;
  FHttp.ConnectionTimeout := 5000;
  FHttp.ResponseTimeout := 10000;
end;

destructor TCampaignEngineClient.Destroy;
begin
  FHttp.Free;
  inherited;
end;

function TCampaignEngineClient.Send(const AMethod, APath, ABody: string): TJSONValue;
var
  Content: TStringStream;
  Response: IHTTPResponse;
  Headers: TNetHeaders;
begin
  Headers := [TNameValuePair.Create('X-Api-Key', FApiKey),
              TNameValuePair.Create('Content-Type', 'application/json'),
              TNameValuePair.Create('Accept', 'application/json')];
  Content := TStringStream.Create(ABody, TEncoding.UTF8);
  try
    if AMethod = 'POST' then
      Response := FHttp.Post(FBaseUrl + APath, Content, nil, Headers)
    else
      Response := FHttp.Get(FBaseUrl + APath, nil, Headers);
  finally
    Content.Free;
  end;

  if (Response.StatusCode < 200) or (Response.StatusCode > 299) then
    raise ECampaignEngineError.Create(Response.StatusCode, Response.ContentAsString(TEncoding.UTF8));
  Result := TJSONObject.ParseJSONValue(Response.ContentAsString(TEncoding.UTF8));
end;

function TCampaignEngineClient.Evaluate(const ACartJson: string; AExplain: Boolean): TJSONObject;
const
  ExplainParam: array[Boolean] of string = ('false', 'true');
begin
  Result := Send('POST', '/evaluate?explain=' + ExplainParam[AExplain], ACartJson) as TJSONObject;
end;

function TCampaignEngineClient.Redeem(const ATransactionId, ACartJson: string): TJSONObject;
var
  Body: TJSONObject;
begin
  Body := TJSONObject.Create;
  try
    Body.AddPair('transactionId', ATransactionId);
    Body.AddPair('cart', TJSONObject.ParseJSONValue(ACartJson));
    Result := Send('POST', '/redemptions', Body.ToJSON) as TJSONObject;
  finally
    Body.Free;
  end;
end;

function TCampaignEngineClient.Reverse(const ATransactionId, AReason: string): TJSONObject;
var
  Body: TJSONObject;
begin
  Body := TJSONObject.Create;
  try
    Body.AddPair('reason', AReason);
    Result := Send('POST', '/redemptions/' + TNetEncoding.URL.Encode(ATransactionId) + '/reverse', Body.ToJSON) as TJSONObject;
  finally
    Body.Free;
  end;
end;

initialization
  InvariantFormat := TFormatSettings.Create;
  InvariantFormat.DecimalSeparator := '.';
  InvariantFormat.ThousandSeparator := #0;

end.
