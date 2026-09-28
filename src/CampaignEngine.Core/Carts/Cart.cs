namespace CampaignEngine.Core.Carts;

/// <summary>
/// The basket a channel (POS, web, mobile, …) asks the engine to price.
/// All amounts are in <see cref="Currency"/> and include tax the same way the channel's prices do.
/// </summary>
public sealed class Cart
{
    /// <summary>Optional id of the basket in the calling system; echoed back for tracing.</summary>
    public string? Id { get; set; }

    /// <summary>ISO 4217 code. Campaigns restricted to another currency are ignored.</summary>
    public string Currency { get; set; } = "TRY";

    /// <summary>Sales channel, free text agreed between systems, e.g. <c>store</c>, <c>web</c>, <c>mobile</c>.</summary>
    public string Channel { get; set; } = "";

    /// <summary>Store / warehouse / web-site code, if the channel has one.</summary>
    public string? StoreId { get; set; }

    /// <summary>Moment of sale. Defaults to "now"; offline POS replays send the original time.</summary>
    public DateTimeOffset? Timestamp { get; set; }

    public CustomerInfo? Customer { get; set; }

    public List<CartLine> Lines { get; set; } = [];

    public List<string> CouponCodes { get; set; } = [];

    public PaymentInfo? Payment { get; set; }

    /// <summary>Shipping fee before discounts. Only free-shipping rewards touch it.</summary>
    public decimal ShippingAmount { get; set; }

    /// <summary>Free-form key/values for <c>cartAttribute</c> conditions (e.g. <c>deliveryType=clickAndCollect</c>).</summary>
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns human-readable problems; an empty list means the cart can be evaluated.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Currency))
        {
            errors.Add("currency is required.");
        }

        if (ShippingAmount < 0)
        {
            errors.Add("shippingAmount cannot be negative.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            var path = $"lines[{i}]";
            if (string.IsNullOrWhiteSpace(line.LineId))
            {
                errors.Add($"{path}.lineId is required.");
            }
            else if (!seen.Add(line.LineId))
            {
                errors.Add($"{path}.lineId '{line.LineId}' is not unique.");
            }

            if (string.IsNullOrWhiteSpace(line.Sku))
            {
                errors.Add($"{path}.sku is required.");
            }

            if (line.Quantity <= 0)
            {
                errors.Add($"{path}.quantity must be greater than zero.");
            }

            if (line.UnitPrice < 0)
            {
                errors.Add($"{path}.unitPrice cannot be negative.");
            }

            if (line.MinimumUnitPrice is < 0)
            {
                errors.Add($"{path}.minimumUnitPrice cannot be negative.");
            }
        }

        return errors;
    }
}

public sealed class CartLine
{
    /// <summary>Id of the line in the calling system; unique within the cart.</summary>
    public required string LineId { get; set; }

    public required string Sku { get; set; }

    /// <summary>Units or a weight (e.g. 0.750 kg). Unit-based offers only use whole units.</summary>
    public decimal Quantity { get; set; } = 1;

    /// <summary>Price of one unit before campaign discounts.</summary>
    public decimal UnitPrice { get; set; }

    public string? Brand { get; set; }

    /// <summary>
    /// Category codes the product belongs to. Send the whole path
    /// (e.g. <c>["apparel", "apparel-men", "apparel-men-tshirt"]</c>) so campaigns can target any level.
    /// </summary>
    public List<string> Categories { get; set; } = [];

    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Price floor: no campaign may bring one unit below this price.</summary>
    public decimal? MinimumUnitPrice { get; set; }

    /// <summary>Set to false for items that must never be discounted (e.g. gift cards, deposits).</summary>
    public bool Discountable { get; set; } = true;
}

public sealed class CustomerInfo
{
    public string? Id { get; set; }

    /// <summary>Segments / tiers the customer belongs to, e.g. <c>gold</c>, <c>employee</c>.</summary>
    public List<string> Segments { get; set; } = [];

    public bool IsFirstOrder { get; set; }
}

public sealed class PaymentInfo
{
    /// <summary>e.g. <c>creditCard</c>, <c>cash</c>, <c>wallet</c>, <c>giftCard</c>.</summary>
    public string? Method { get; set; }

    /// <summary>Issuing bank code, for bank-sponsored campaigns.</summary>
    public string? BankCode { get; set; }

    /// <summary>First 6–8 digits of the card number.</summary>
    public string? CardBin { get; set; }

    public int? Installments { get; set; }
}
