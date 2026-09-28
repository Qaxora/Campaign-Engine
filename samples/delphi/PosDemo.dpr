program PosDemo;

{ Console demo: price a basket, print the receipt lines, confirm the sale. }

{$APPTYPE CONSOLE}

uses
  System.SysUtils, System.JSON,
  CampaignEngine.Client in 'CampaignEngine.Client.pas';

var
  Client: TCampaignEngineClient;
  Cart: TCartBuilder;
  Priced, Redemption: TJSONObject;
  Line, Applied: TJSONValue;
begin
  Client := TCampaignEngineClient.Create('http://localhost:5080', 'dev-pos-key');
  Cart := TCartBuilder.Create('store', 'IST-001');
  try
    Cart
      .AddLine('1', 'TS-001-M', 3, 299.90, ['apparel', 'apparel-tshirt'])
      .AddLine('2', 'TOBACCO-001', 1, 95.00, [])
      .SetCustomer('C-1001', ['gold'])
      .SetPayment('creditCard', '45436012', 3);

    Priced := Client.Evaluate(Cart.ToJson, True);
    try
      for Line in Priced.GetValue<TJSONArray>('lines') do
        Writeln(Format('%-12s %8s  -%8s',
          [Line.GetValue<string>('sku'),
           CurrToStr(JsonToMoney(Line.GetValue<TJSONValue>('gross'))),
           CurrToStr(JsonToMoney(Line.GetValue<TJSONValue>('discount')))]));

      for Applied in Priced.GetValue<TJSONArray>('appliedCampaigns') do
        Writeln('  Campaign: ', Applied.GetValue<string>('name'),
          '  -', CurrToStr(JsonToMoney(Applied.GetValue<TJSONValue>('discount'))));

      Writeln('TOTAL: ', CurrToStr(JsonToMoney(Priced.GetValue<TJSONValue>('total'))));
    finally
      Priced.Free;
    end;

    // Payment succeeded: record the sale. Safe to call again after a timeout.
    Redemption := Client.Redeem('IST-001-' + FormatDateTime('yyyymmddhhnnss', Now), Cart.ToJson);
    try
      Writeln('Recorded, discount: ', CurrToStr(JsonToMoney(Redemption.GetValue<TJSONValue>('totalDiscount'))));
    finally
      Redemption.Free;
    end;
  finally
    Cart.Free;
    Client.Free;
  end;
  Readln;
end.
