using System.Text.Json.Serialization;
using CampaignEngine.Core.Products;
using CampaignEngine.Core.Rules;

namespace CampaignEngine.Core.Campaigns;

/// <summary>
/// A campaign definition: who gets (audience), what (target + reward), when (schedule),
/// under which circumstances (conditions) and how often (limits).
/// </summary>
public sealed class Campaign
{
    public Guid Id { get; set; }

    /// <summary>Unique, human-friendly code. Printed on receipts and used by other systems.</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;

    /// <summary>Higher values are applied first and win ties.</summary>
    public int Priority { get; set; }

    public StackingMode Stacking { get; set; } = StackingMode.Stackable;

    /// <summary>At most one campaign per group applies to a cart: the one giving the largest discount.</summary>
    public string? ExclusivityGroup { get; set; }

    /// <summary>If set, the campaign only applies to carts in this currency.</summary>
    public string? Currency { get; set; }

    public Schedule Schedule { get; set; } = new();

    /// <summary>Channels the campaign runs on. Empty means every channel.</summary>
    public List<string> Channels { get; set; } = [];

    public StoreScope Stores { get; set; } = new();

    /// <summary>Customer segments the campaign is for. Empty means everyone, including anonymous customers.</summary>
    public List<string> CustomerSegments { get; set; } = [];

    /// <summary>When set, one of the coupon codes must be present in the cart.</summary>
    public CouponRule? Coupon { get; set; }

    /// <summary>Products the reward applies to (and that count toward conditions by default).</summary>
    public ProductSelector Target { get; set; } = new();

    /// <summary>All conditions must hold. Use <c>anyOf</c> / <c>not</c> for other logic.</summary>
    public List<Condition> Conditions { get; set; } = [];

    public required Reward Reward { get; set; }

    public CampaignLimits Limits { get; set; } = new();

    /// <summary>Allows discounting products in global exclusion lists. Use with care.</summary>
    public bool IgnoreGlobalExclusions { get; set; }

    /// <summary>Text shown to the customer / cashier when the campaign applies.</summary>
    public string? DisplayMessage { get; set; }

    public List<string> Tags { get; set; } = [];

    /// <summary>Free-form references to other systems, e.g. <c>{"erpCampaignNo": "2026-0042"}</c>.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)] // keep the case-insensitive comparer
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Incremented on every change; used for optimistic concurrency.</summary>
    public int Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>All product list codes this campaign depends on.</summary>
    public IEnumerable<string> ReferencedProductLists() =>
        Target.ReferencedProductLists()
            .Concat(Conditions.SelectMany(c => c.ReferencedProductLists()))
            .Concat(Reward.ReferencedProductLists())
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True if the campaign could still apply at or after <paramref name="now"/>
    /// (active or paused, and not ended). Used for snapshots and conflict analysis.
    /// </summary>
    public bool IsLive(DateTimeOffset now) =>
        Status is CampaignStatus.Active or CampaignStatus.Paused && !Schedule.HasEnded(now);
}

public enum CampaignStatus
{
    Draft,
    Active,
    Paused,
    Archived,
}

public enum StackingMode
{
    /// <summary>Combines with other stackable campaigns.</summary>
    Stackable,

    /// <summary>Never combined with anything; competes against the combination of all stackable campaigns.</summary>
    Exclusive,
}

public sealed class StoreScope
{
    /// <summary>If not empty, only these stores.</summary>
    public List<string> Include { get; set; } = [];

    public List<string> Exclude { get; set; } = [];

    public bool Matches(string? storeId)
    {
        if (Include.Count > 0 && (storeId is null || !Include.Contains(storeId, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        return storeId is null || !Exclude.Contains(storeId, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class CouponRule
{
    public List<string> Codes { get; set; } = [];

    /// <summary>How many times each code may be redeemed in total (e.g. 1 for single-use codes).</summary>
    public int? MaxUsesPerCode { get; set; }

    /// <summary>Returns the first cart coupon that belongs to this campaign.</summary>
    public string? Match(IEnumerable<string> cartCoupons) =>
        cartCoupons.FirstOrDefault(c => Codes.Contains(c, StringComparer.OrdinalIgnoreCase));
}

public sealed class CampaignLimits
{
    /// <summary>Total number of transactions the campaign may be redeemed in.</summary>
    public int? MaxRedemptions { get; set; }

    /// <summary>Per identified customer. Anonymous carts are not limited by this.</summary>
    public int? MaxRedemptionsPerCustomer { get; set; }

    /// <summary>Total discount the campaign may give away over its lifetime.</summary>
    public decimal? Budget { get; set; }

    /// <summary>Cap on the discount given to a single cart.</summary>
    public decimal? MaxDiscountPerOrder { get; set; }
}
