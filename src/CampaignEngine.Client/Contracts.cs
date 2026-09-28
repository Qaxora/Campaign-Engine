using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Conflicts;
using CampaignEngine.Core.Evaluation;

namespace CampaignEngine.Client;

// Response shapes of the HTTP API that are not part of CampaignEngine.Core.

public sealed class RedemptionResponse
{
    public required string TransactionId { get; init; }

    /// <summary><c>confirmed</c> or <c>reversed</c>.</summary>
    public string Status { get; init; } = "";

    public bool Offline { get; init; }

    public bool Replayed { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReversedAt { get; init; }

    public decimal TotalDiscount { get; init; }

    public List<RedeemedCampaign> Campaigns { get; init; } = [];

    public EvaluationResult? Evaluation { get; init; }
}

public sealed record RedeemedCampaign(Guid CampaignId, string CampaignCode, decimal Discount, string? CouponCode);

public sealed class OfflineSale
{
    public required string TransactionId { get; set; }

    public DateTimeOffset? Timestamp { get; set; }

    public string Currency { get; set; } = "TRY";

    public string Channel { get; set; } = "";

    public string? StoreId { get; set; }

    public string? CustomerId { get; set; }

    public List<OfflineSaleCampaign> Campaigns { get; set; } = [];

    /// <summary>Builds the import payload from a locally computed result.</summary>
    public static OfflineSale From(string transactionId, Core.Carts.Cart cart, EvaluationResult result) => new()
    {
        TransactionId = transactionId,
        Timestamp = result.EvaluatedAt,
        Currency = cart.Currency,
        Channel = cart.Channel,
        StoreId = cart.StoreId,
        CustomerId = cart.Customer?.Id,
        Campaigns = result.AppliedCampaigns
            .Select(a => new OfflineSaleCampaign { CampaignCode = a.Code, Discount = a.Discount, CouponCode = a.CouponCode })
            .ToList(),
    };
}

public sealed class OfflineSaleCampaign
{
    public required string CampaignCode { get; set; }

    public decimal Discount { get; set; }

    public string? CouponCode { get; set; }
}

public sealed record CampaignChangeResponse(Campaign Campaign, IReadOnlyList<CampaignConflict> Conflicts, IReadOnlyList<string>? Warnings = null);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Result of a conditional snapshot request.</summary>
public sealed record SnapshotResponse(bool NotModified, string? ETag, SnapshotDocument? Snapshot);

/// <summary>Non-success response from the API, with the problem-details body.</summary>
public sealed class CampaignEngineApiException(int statusCode, string? title, string? detail, IReadOnlyList<string> errors, string body)
    : Exception($"{statusCode} {title}: {detail}")
{
    public int StatusCode { get; } = statusCode;

    public string? Title { get; } = title;

    public string? Detail { get; } = detail;

    public IReadOnlyList<string> Errors { get; } = errors;

    public string Body { get; } = body;
}
