using CampaignEngine.Api.Security;
using CampaignEngine.Infrastructure.Analytics;

namespace CampaignEngine.Api.Endpoints;

public static class AnalyticsEndpoints
{
    private const string PeriodDescription =
        "`from` (inclusive) and `to` (exclusive) are ISO 8601; the default is the last 30 days, the maximum 366 days. " +
        "Reversed transactions are excluded.";

    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/analytics")
            .WithTags("Analytics")
            .RequireAuthorization(Policies.Read);

        group.MapGet("/overview", async (DateTimeOffset? from, DateTimeOffset? to, AnalyticsService service, CancellationToken ct) =>
                await service.OverviewAsync(service.ResolvePeriod(from, to), ct))
            .WithSummary("Campaign counts and redemption KPIs, with the previous period of the same length")
            .WithDescription(PeriodDescription)
            .Produces<AnalyticsOverview>();

        group.MapGet("/campaigns", async (DateTimeOffset? from, DateTimeOffset? to, AnalyticsService service, CancellationToken ct) =>
                await service.CampaignPerformanceAsync(service.ResolvePeriod(from, to), cancellationToken: ct))
            .WithSummary("Performance of every redeemed or active campaign, best first")
            .WithDescription(PeriodDescription)
            .Produces<IReadOnlyList<CampaignPerformance>>();

        group.MapGet("/top-campaigns", async (DateTimeOffset? from, DateTimeOffset? to, int? limit, AnalyticsService service, CancellationToken ct) =>
                await service.CampaignPerformanceAsync(service.ResolvePeriod(from, to), limit ?? 5, ct))
            .WithSummary("The campaigns that gave the most discount")
            .WithDescription(PeriodDescription)
            .Produces<IReadOnlyList<CampaignPerformance>>();

        group.MapGet("/trends", async (DateTimeOffset? from, DateTimeOffset? to, string? timeZone, AnalyticsService service, CancellationToken ct) =>
                await service.TrendsAsync(service.ResolvePeriod(from, to), timeZone, ct))
            .WithSummary("Daily transactions, redemptions and discount (empty days included)")
            .WithDescription(PeriodDescription + " Days are cut in `timeZone` (IANA or Windows id, default UTC), e.g. `Europe/Istanbul`.")
            .Produces<AnalyticsTrend>();

        return app;
    }
}
