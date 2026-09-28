namespace CampaignEngine.Core.Evaluation;

/// <summary>The priced cart: what applied, how much, to which lines — and optionally why not.</summary>
public sealed class EvaluationResult
{
    public string? CartId { get; init; }

    public required string Currency { get; init; }

    public DateTimeOffset EvaluatedAt { get; init; }

    public string? CatalogVersion { get; init; }

    /// <summary>Sum of line gross amounts (quantity × unit price).</summary>
    public decimal Subtotal { get; init; }

    public decimal LineDiscount { get; init; }

    public decimal ShippingAmount { get; init; }

    public decimal ShippingDiscount { get; init; }

    public decimal TotalDiscount => LineDiscount + ShippingDiscount;

    /// <summary>Amount to pay: subtotal − discounts + shipping.</summary>
    public decimal Total => Subtotal - LineDiscount + ShippingAmount - ShippingDiscount;

    public List<AppliedCampaign> AppliedCampaigns { get; init; } = [];

    public List<LineResult> Lines { get; init; } = [];

    public List<GiftItem> Gifts { get; init; } = [];

    /// <summary>Campaigns the customer is close to (e.g. "add 50 more for free shipping").</summary>
    public List<CampaignHint> Hints { get; init; } = [];

    /// <summary>Coupon codes in the cart that did not produce a discount.</summary>
    public List<string> UnusedCouponCodes { get; init; } = [];

    /// <summary>Only filled when an explanation was requested.</summary>
    public List<RejectedCampaign>? Rejections { get; init; }
}

public sealed class AppliedCampaign
{
    public Guid CampaignId { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public string? DisplayMessage { get; init; }

    public required string RewardType { get; init; }

    public string? CouponCode { get; init; }

    public decimal Discount => LineDiscount + ShippingDiscount;

    public decimal LineDiscount { get; init; }

    public decimal ShippingDiscount { get; init; }

    public List<AppliedLine> Lines { get; init; } = [];

    public List<GiftItem> Gifts { get; init; } = [];

    /// <summary>Copied from the campaign so channels can map it to their own ids.</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
}

public sealed record AppliedLine(string LineId, decimal Discount);

public sealed record GiftItem(string Sku, decimal Quantity, string CampaignCode);

public sealed class LineResult
{
    public required string LineId { get; init; }

    public required string Sku { get; init; }

    public decimal Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal Gross { get; init; }

    public decimal Discount { get; init; }

    public decimal Net => Gross - Discount;

    public List<LineDiscount> Discounts { get; init; } = [];
}

public sealed record LineDiscount(string CampaignCode, decimal Amount);

public sealed class CampaignHint
{
    public Guid CampaignId { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public string? DisplayMessage { get; init; }

    public decimal? MissingAmount { get; init; }

    public decimal? MissingQuantity { get; init; }
}

public sealed record RejectedCampaign(Guid CampaignId, string Code, RejectionReason Reason, string? Detail = null);

public enum RejectionReason
{
    NotActive,
    CurrencyMismatch,
    OutsideSchedule,
    ChannelMismatch,
    StoreMismatch,
    SegmentMismatch,
    CouponMissing,
    CouponExhausted,
    UsageLimitReached,
    CustomerLimitReached,
    BudgetExhausted,
    NoEligibleItems,
    ConditionNotMet,
    LostInExclusivityGroup,
    NotSelected,
    NoDiscount,
    InvalidDefinition,
}
