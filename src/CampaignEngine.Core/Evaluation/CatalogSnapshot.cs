using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Products;

namespace CampaignEngine.Core.Evaluation;

/// <summary>
/// Everything the engine needs to know about campaigns and product lists, frozen at one point in time.
/// </summary>
public sealed class CatalogSnapshot(IReadOnlyList<Campaign> campaigns, IProductListLookup lists, string? version = null)
{
    public static CatalogSnapshot Empty { get; } = new([], ProductListIndex.Empty);

    public IReadOnlyList<Campaign> Campaigns { get; } = campaigns;

    public IProductListLookup Lists { get; } = lists;

    /// <summary>Opaque version (e.g. an ETag) echoed in results for traceability.</summary>
    public string? Version { get; } = version;
}

/// <summary>How much of each campaign's limits has been used, as seen by the ledger.</summary>
public sealed class UsageSnapshot
{
    private readonly Dictionary<Guid, CampaignUsage> _usage;

    public UsageSnapshot(Dictionary<Guid, CampaignUsage>? usage = null) => _usage = usage ?? [];

    public static UsageSnapshot Empty { get; } = new();

    public CampaignUsage For(Guid campaignId) => _usage.GetValueOrDefault(campaignId) ?? CampaignUsage.None;
}

public sealed class CampaignUsage
{
    public static CampaignUsage None { get; } = new();

    public int Redemptions { get; init; }

    public decimal DiscountTotal { get; init; }

    /// <summary>Redemptions by the customer of the cart being evaluated.</summary>
    public int CustomerRedemptions { get; init; }

    public IReadOnlyDictionary<string, int> CouponRedemptions { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}
