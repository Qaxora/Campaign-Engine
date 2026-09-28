using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Products;
using CampaignEngine.Infrastructure.Platform;

namespace CampaignEngine.Infrastructure.Persistence;

// Persistence models. The domain model stays free of EF concerns; see ADR 0001 for why a campaign
// is one row with a JSON definition and ADR 0005 for TenantId.

public sealed class CampaignRecord : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public CampaignStatus Status { get; set; }

    public int Priority { get; set; }

    public DateTime? StartsAt { get; set; }

    public DateTime? EndsAt { get; set; }

    /// <summary>Full definition as produced by <c>CampaignJson</c>.</summary>
    public required string Definition { get; set; }

    public int Version { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public sealed class ProductListRecord : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public ProductListKind Kind { get; set; }

    public int Version { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<ProductListItemRecord> Items { get; set; } = [];
}

public sealed class ProductListItemRecord : ITenantOwned
{
    public Guid ListId { get; set; }

    public Guid TenantId { get; set; }

    public required string Sku { get; set; }
}

/// <summary>One completed sale (or an imported offline sale) that used campaigns.</summary>
public sealed class TransactionRecord : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>The caller's receipt / order number; unique per tenant.</summary>
    public required string TransactionId { get; set; }

    public required string Client { get; set; }

    public RedemptionStatus Status { get; set; }

    public bool Offline { get; set; }

    public string? CustomerId { get; set; }

    public string Channel { get; set; } = "";

    public string? StoreId { get; set; }

    public required string Currency { get; set; }

    public decimal TotalDiscount { get; set; }

    /// <summary>The evaluation result (or the imported payload) returned on idempotent replays.</summary>
    public required string ResultJson { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ReversedAt { get; set; }

    public string? ReverseReason { get; set; }

    public List<RedemptionRecord> Redemptions { get; set; } = [];
}

/// <summary>One campaign applied in one transaction.</summary>
public sealed class RedemptionRecord : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid TransactionRecordId { get; set; }

    /// <summary>Copy of the caller's transaction id, for queries and display.</summary>
    public required string TransactionId { get; set; }

    public Guid CampaignId { get; set; }

    public required string CampaignCode { get; set; }

    public string? CustomerId { get; set; }

    /// <summary>Upper-cased so that limits are counted case-insensitively.</summary>
    public string? CouponCode { get; set; }

    public decimal Discount { get; set; }

    public RedemptionStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>Running totals per campaign. Doubles as the optimistic-concurrency guard for limits.</summary>
public sealed class CampaignUsageRecord : ITenantOwned
{
    public Guid CampaignId { get; set; }

    public Guid TenantId { get; set; }

    public int Redemptions { get; set; }

    public decimal DiscountTotal { get; set; }

    public Guid ConcurrencyStamp { get; set; }
}

public enum RedemptionStatus
{
    Confirmed,
    Reversed,
}
