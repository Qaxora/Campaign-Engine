using CampaignEngine.Core.Campaigns;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Analytics;

/// <summary>A half-open time range <c>[From, To)</c>.</summary>
public sealed record Period(DateTimeOffset From, DateTimeOffset To)
{
    public Period PreviousPeriod() => new(From - (To - From), From);

}

public sealed record OverviewMetrics(
    int Transactions,
    int Redemptions,
    decimal DiscountTotal,
    decimal AverageDiscountPerTransaction,
    int CampaignsRedeemed);

public sealed record AnalyticsOverview(
    Period Period,
    int TotalCampaigns,
    int ActiveCampaigns,
    int DraftCampaigns,
    OverviewMetrics Current,
    OverviewMetrics Previous);

public sealed record CampaignPerformance(
    Guid CampaignId,
    string Code,
    string Name,
    CampaignStatus Status,
    int Redemptions,
    decimal Discount,
    decimal AverageDiscount,
    /// <summary>Share of the period's total discount, 0–1.</summary>
    decimal DiscountShare,
    decimal? Budget,
    /// <summary>Lifetime budget consumption, 0–1 (not limited to the period).</summary>
    decimal? BudgetUsed,
    int? MaxRedemptions);

public sealed record TrendPoint(DateOnly Date, int Transactions, int Redemptions, decimal Discount);

public sealed record AnalyticsTrend(Period Period, string TimeZone, IReadOnlyList<TrendPoint> Points);

/// <summary>
/// Campaign analytics computed from the campaign table and the redemption ledger — no separate event
/// store. Reversed transactions are excluded; sales are dated by their transaction time (for offline
/// imports, the time of the sale). Every query is tenant-filtered by the DbContext.
/// </summary>
public sealed class AnalyticsService(CampaignDbContext db, TimeProvider time)
{
    public const int MaxPeriodDays = 366;

    /// <summary>Defaults to the last 30 days; validates explicit ranges.</summary>
    public Period ResolvePeriod(DateTimeOffset? from, DateTimeOffset? to)
    {
        var end = to ?? time.GetUtcNow();
        var start = from ?? end.AddDays(-30);
        if (start >= end)
        {
            throw new ValidationException(["from must be earlier than to."]);
        }

        if (end - start > TimeSpan.FromDays(MaxPeriodDays))
        {
            throw new ValidationException([$"The period can be at most {MaxPeriodDays} days."]);
        }

        return new Period(start, end);
    }

    public async Task<AnalyticsOverview> OverviewAsync(Period period, CancellationToken cancellationToken = default)
    {
        var statuses = await db.Campaigns.AsNoTracking()
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int Count(CampaignStatus status) => statuses.Where(s => s.Status == status).Sum(s => s.Count);

        var sales = await SalesAsync(period.PreviousPeriod().From, period.To, cancellationToken);
        return new AnalyticsOverview(
            period,
            statuses.Sum(s => s.Count),
            Count(CampaignStatus.Active),
            Count(CampaignStatus.Draft),
            Metrics(sales.Where(s => s.At >= period.From.UtcDateTime)),
            Metrics(sales.Where(s => s.At < period.From.UtcDateTime)));
    }

    /// <summary>Every campaign that was redeemed in the period (plus active ones without redemptions), best first.</summary>
    public async Task<IReadOnlyList<CampaignPerformance>> CampaignPerformanceAsync(Period period, int? limit = null, CancellationToken cancellationToken = default)
    {
        var sales = await SalesAsync(period.From, period.To, cancellationToken);
        var redemptions = sales.SelectMany(s => s.Redemptions).ToList();
        var total = redemptions.Sum(r => r.Discount);
        var byCampaign = redemptions.ToLookup(r => r.CampaignId);

        var redeemedIds = byCampaign.Select(g => g.Key).ToList();
        var campaigns = await db.Campaigns.AsNoTracking()
            .Where(c => redeemedIds.Contains(c.Id) || c.Status == CampaignStatus.Active)
            .ToListAsync(cancellationToken);
        var campaignIds = campaigns.Select(c => c.Id).ToList();

        // Lifetime consumption for budget usage.
        var lifetime = (await db.Redemptions.AsNoTracking()
                .Where(r => campaignIds.Contains(r.CampaignId) && r.Status == RedemptionStatus.Confirmed)
                .Select(r => new { r.CampaignId, r.Discount })
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.CampaignId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Discount));

        var rows = campaigns
            .Select(c =>
            {
                var limits = c.ToDomain().Limits;
                var own = byCampaign[c.Id].ToList();
                var discount = own.Sum(r => r.Discount);
                return new CampaignPerformance(
                    c.Id,
                    c.Code,
                    c.Name,
                    c.Status,
                    own.Count,
                    discount,
                    own.Count == 0 ? 0 : Math.Round(discount / own.Count, 2),
                    total == 0 ? 0 : Math.Round(discount / total, 4),
                    limits.Budget,
                    limits.Budget is > 0 and { } budget ? Math.Round(lifetime.GetValueOrDefault(c.Id) / budget, 4) : null,
                    limits.MaxRedemptions);
            })
            .OrderByDescending(r => r.Discount)
            .ThenByDescending(r => r.Redemptions)
            .ThenBy(r => r.Code, StringComparer.Ordinal);
        return (limit is { } n ? rows.Take(Math.Clamp(n, 1, 100)) : rows).ToList();
    }

    /// <summary>Daily totals in <paramref name="timeZoneId"/> (default UTC), with empty days filled in.</summary>
    public async Task<AnalyticsTrend> TrendsAsync(Period period, string? timeZoneId, CancellationToken cancellationToken = default)
    {
        var zone = ResolveTimeZone(timeZoneId);
        var sales = await SalesAsync(period.From, period.To, cancellationToken);
        DateOnly Day(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));

        var byDay = sales.GroupBy(s => Day(s.At)).ToDictionary(g => g.Key);
        var first = Day(period.From.UtcDateTime);
        var last = Day(period.To.UtcDateTime.AddTicks(-1));
        var points = new List<TrendPoint>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            points.Add(byDay.TryGetValue(day, out var group)
                ? new TrendPoint(day, group.Count(), group.Sum(s => s.Redemptions.Count), group.Sum(s => s.Discount))
                : new TrendPoint(day, 0, 0, 0));
        }

        return new AnalyticsTrend(period, zone.Id, points);
    }

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ValidationException([$"Unknown time zone '{id}'."]);
        }
    }

    private static OverviewMetrics Metrics(IEnumerable<Sale> sales)
    {
        var list = sales.ToList();
        var discount = list.Sum(s => s.Discount);
        return new OverviewMetrics(
            list.Count,
            list.Sum(s => s.Redemptions.Count),
            discount,
            list.Count == 0 ? 0 : Math.Round(discount / list.Count, 2),
            list.SelectMany(s => s.Redemptions).Select(r => r.CampaignId).Distinct().Count());
    }

    /// <summary>
    /// Confirmed transactions in <c>[from, to)</c> with their redemptions. Aggregated in memory
    /// because SQLite cannot sum decimals server-side; the columns loaded are narrow.
    /// </summary>
    private async Task<List<Sale>> SalesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var fromUtc = from.UtcDateTime;
        var toUtc = to.UtcDateTime;
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Status == RedemptionStatus.Confirmed && t.CreatedAt >= fromUtc && t.CreatedAt < toUtc)
            .Select(t => new
            {
                t.CreatedAt,
                t.TotalDiscount,
                Redemptions = t.Redemptions.Select(r => new SaleRedemption(r.CampaignId, r.Discount)).ToList(),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new Sale(r.CreatedAt, r.TotalDiscount, r.Redemptions)).ToList();
    }

    private sealed record Sale(DateTime At, decimal Discount, List<SaleRedemption> Redemptions);

    private sealed record SaleRedemption(Guid CampaignId, decimal Discount);
}
