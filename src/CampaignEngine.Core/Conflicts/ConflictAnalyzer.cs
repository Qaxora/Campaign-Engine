using System.Globalization;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Products;

namespace CampaignEngine.Core.Conflicts;

/// <summary>
/// Finds campaigns that can hit the same cart and explains how they will interact —
/// before a customer finds out at the till.
/// </summary>
/// <remarks>
/// The analysis is static (no cart involved) and conservative: when it cannot prove two scopes are
/// disjoint it reports a <see cref="OverlapCertainty.Possible"/> overlap instead of staying silent.
/// </remarks>
public sealed class ConflictAnalyzer(ConflictAnalyzerOptions? options = null)
{
    private readonly ConflictAnalyzerOptions _options = options ?? new ConflictAnalyzerOptions();

    /// <summary>Conflicts between <paramref name="subject"/> and each of <paramref name="others"/>.</summary>
    public IReadOnlyList<CampaignConflict> Analyze(Campaign subject, IEnumerable<Campaign> others, IProductListLookup? lists = null)
    {
        lists ??= ProductListIndex.Empty;
        return others
            .Where(o => o.Id != subject.Id && !string.Equals(o.Code, subject.Code, StringComparison.OrdinalIgnoreCase))
            .SelectMany(o => Compare(subject, o, lists))
            .OrderByDescending(c => c.Severity)
            .ThenBy(c => c.OtherCode, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>All pairwise conflicts among <paramref name="campaigns"/>.</summary>
    public IReadOnlyList<CampaignConflict> AnalyzeAll(IReadOnlyList<Campaign> campaigns, IProductListLookup? lists = null)
    {
        lists ??= ProductListIndex.Empty;
        var conflicts = new List<CampaignConflict>();
        for (var i = 0; i < campaigns.Count; i++)
        {
            for (var j = i + 1; j < campaigns.Count; j++)
            {
                conflicts.AddRange(Compare(campaigns[i], campaigns[j], lists));
            }
        }

        return conflicts.OrderByDescending(c => c.Severity).ThenBy(c => c.CampaignCode, StringComparer.Ordinal).ToList();
    }

    private IEnumerable<CampaignConflict> Compare(Campaign a, Campaign b, IProductListLookup lists)
    {
        if (!AudienceOverlaps(a, b) || !Overlap.Schedules(a.Schedule, b.Schedule))
        {
            yield break;
        }

        CampaignConflict Conflict(ConflictKind kind, ConflictSeverity severity, OverlapCertainty certainty, string message, decimal? rate = null) =>
            new(a.Id, a.Code, b.Id, b.Code, kind, severity, certainty, message, rate);

        // Same coupon code in two live campaigns is always a mistake, whatever the products.
        var sharedCoupons = a.Coupon is not null && b.Coupon is not null
            ? a.Coupon.Codes.Intersect(b.Coupon.Codes, StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        if (sharedCoupons.Count > 0)
        {
            yield return Conflict(ConflictKind.DuplicateCoupon, ConflictSeverity.Error, OverlapCertainty.Certain,
                $"Coupon code(s) {string.Join(", ", sharedCoupons)} are used by both campaigns.");
        }

        var scope = Overlap.Products(a.Target, b.Target, lists);
        if (scope == OverlapCertainty.None)
        {
            yield break;
        }

        var maybe = scope == OverlapCertainty.Possible ? "may " : "";
        if (a.ExclusivityGroup is not null && string.Equals(a.ExclusivityGroup, b.ExclusivityGroup, StringComparison.OrdinalIgnoreCase))
        {
            yield return Conflict(ConflictKind.SameGroup, ConflictSeverity.Info, scope,
                $"Both are in exclusivity group '{a.ExclusivityGroup}'; only the better one applies to a cart.");
            yield break;
        }

        if (a.Stacking == StackingMode.Exclusive || b.Stacking == StackingMode.Exclusive)
        {
            yield return Conflict(ConflictKind.ExclusiveOverlap, ConflictSeverity.Info, scope,
                $"The campaigns {maybe}target the same products; the exclusive one competes with the other instead of combining.");
            if (a.Priority == b.Priority)
            {
                yield return Conflict(ConflictKind.SamePriority, ConflictSeverity.Warning, scope,
                    $"Both have priority {a.Priority}; when they give the same discount the winner is decided by code order.");
            }

            yield break;
        }

        var rate = CombinedRate(a, b);
        var severity = rate >= _options.StackedRateWarningThreshold ? ConflictSeverity.Warning : ConflictSeverity.Info;
        var estimate = rate is null ? "" : string.Create(CultureInfo.InvariantCulture, $" Combined discount can reach about {rate.Value:P0}.");
        yield return Conflict(ConflictKind.StackedDiscount, severity, scope,
            $"Both are stackable and {maybe}discount the same products.{estimate}", rate);
    }

    private static bool AudienceOverlaps(Campaign a, Campaign b) =>
        (a.Currency is null || b.Currency is null || string.Equals(a.Currency, b.Currency, StringComparison.OrdinalIgnoreCase))
        && Overlap.Lists(a.Channels, b.Channels)
        && Overlap.Lists(a.CustomerSegments, b.CustomerSegments)
        && Overlap.Stores(a.Stores, b.Stores);

    private static decimal? CombinedRate(Campaign a, Campaign b) =>
        a.Reward.MaxRateEstimate is { } ra && b.Reward.MaxRateEstimate is { } rb
            ? 1 - ((1 - ra) * (1 - rb))
            : null;
}

public sealed class ConflictAnalyzerOptions
{
    /// <summary>Stacked campaigns whose combined rate reaches this value are reported as warnings.</summary>
    public decimal StackedRateWarningThreshold { get; set; } = 0.5m;
}

public sealed record CampaignConflict(
    Guid CampaignId,
    string CampaignCode,
    Guid OtherId,
    string OtherCode,
    ConflictKind Kind,
    ConflictSeverity Severity,
    OverlapCertainty Certainty,
    string Message,
    decimal? CombinedRateEstimate = null);

public enum ConflictKind
{
    DuplicateCoupon,
    StackedDiscount,
    ExclusiveOverlap,
    SamePriority,
    SameGroup,
}

public enum ConflictSeverity
{
    Info,
    Warning,
    Error,
}

public enum OverlapCertainty
{
    None,
    Possible,
    Certain,
}
