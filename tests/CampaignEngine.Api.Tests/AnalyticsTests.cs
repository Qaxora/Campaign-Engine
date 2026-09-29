using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Infrastructure.Analytics;
using CampaignEngine.Infrastructure.Platform;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Tests;

/// <summary>Each test registers a fresh organization so the ledger it aggregates is exactly known.</summary>
public sealed class AnalyticsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Day1 = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Midnight1 = new(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);

    private async Task<HttpClient> FreshOrganizationAsync()
    {
        var body = JsonSerializer.Serialize(new { email = $"analytics-{Guid.NewGuid():N}@example.com", password = "correct-horse-battery", name = "A", organizationName = "Analytics Co" }, ApiFactory.Json);
        var signIn = await (await factory.CreateClient().PostRawAsync("/api/v1/auth/register", body)).ReadAsync<SignInResult>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signIn.Token);
        return client;
    }

    private static async Task<Campaign> CampaignAsync(HttpClient client, string code, bool activate = true, string limits = "{}")
    {
        var campaign = await (await client.PostRawAsync("/api/v1/campaigns", $$"""
            { "code": "{{code}}", "name": "{{code}}", "reward": { "type": "percentageDiscount", "percent": 10 }, "limits": {{limits}} }
            """)).ReadAsync<Campaign>();
        if (activate)
        {
            await (await client.PostAsync($"/api/v1/campaigns/{campaign.Id}/activate", null)).ReadAsync<CampaignChange>();
        }

        return campaign;
    }

    /// <summary>Records a sale at a known time through the offline import.</summary>
    private static async Task SaleAsync(HttpClient client, string id, DateTimeOffset at, params (string Code, decimal Discount)[] campaigns)
    {
        var sale = new
        {
            transactionId = id,
            timestamp = at,
            currency = "TRY",
            channel = "store",
            campaigns = campaigns.Select(c => new { campaignCode = c.Code, discount = c.Discount }),
        };
        await (await client.PostRawAsync("/api/v1/redemptions/offline", JsonSerializer.Serialize(sale, ApiFactory.Json))).ReadAsync<RedemptionResult>();
    }

    private static string Range(DateTimeOffset from, DateTimeOffset to) =>
        $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";

    [Fact]
    public async Task Overview_counts_campaigns_and_compares_with_the_previous_period()
    {
        var client = await FreshOrganizationAsync();
        await CampaignAsync(client, "A");
        await CampaignAsync(client, "B");
        await CampaignAsync(client, "DRAFT", activate: false);

        await SaleAsync(client, "P-1", Day1.AddDays(-5), ("A", 10m));                // previous period
        await SaleAsync(client, "C-1", Day1, ("A", 20m), ("B", 10m));
        await SaleAsync(client, "C-2", Day1.AddDays(1), ("A", 30m));
        await SaleAsync(client, "C-3", Day1.AddDays(2), ("B", 50m));
        await client.PostAsync("/api/v1/redemptions/C-3/reverse", null);             // excluded
        await SaleAsync(client, "LATER", Day1.AddDays(20), ("A", 99m));              // outside

        var overview = await (await client.GetAsync($"/api/v1/analytics/overview?{Range(Day1, Day1.AddDays(7))}")).ReadAsync<AnalyticsOverview>();

        Assert.Equal(3, overview.TotalCampaigns);
        Assert.Equal(2, overview.ActiveCampaigns);
        Assert.Equal(1, overview.DraftCampaigns);
        Assert.Equal(new OverviewMetrics(2, 3, 60m, 30m, 2), overview.Current);
        Assert.Equal(new OverviewMetrics(1, 1, 10m, 10m, 1), overview.Previous);
    }

    [Fact]
    public async Task Campaign_performance_ranks_by_discount_with_share_and_budget_usage()
    {
        var client = await FreshOrganizationAsync();
        await CampaignAsync(client, "SMALL");
        await CampaignAsync(client, "BIG", limits: """{ "budget": 200, "maxRedemptions": 50 }""");
        await CampaignAsync(client, "IDLE");
        await SaleAsync(client, "T-1", Day1, ("BIG", 60m), ("SMALL", 20m));
        await SaleAsync(client, "T-2", Day1.AddHours(2), ("BIG", 20m));
        await SaleAsync(client, "OLD", Day1.AddDays(-40), ("BIG", 20m)); // counts for budget, not for the period

        var rows = JsonSerializer.Deserialize<List<CampaignPerformance>>(
            await (await client.GetAsync($"/api/v1/analytics/campaigns?{Range(Day1, Day1.AddDays(1))}")).Content.ReadAsStringAsync(), ApiFactory.Json)!;

        Assert.Equal(["BIG", "SMALL", "IDLE"], rows.Select(r => r.Code));
        var big = rows[0];
        Assert.Equal(2, big.Redemptions);
        Assert.Equal(80m, big.Discount);
        Assert.Equal(40m, big.AverageDiscount);
        Assert.Equal(0.8m, big.DiscountShare);
        Assert.Equal(0.5m, big.BudgetUsed); // (60 + 20 + 20) / 200
        Assert.Equal(0, rows[2].Redemptions);

        var top = JsonSerializer.Deserialize<List<CampaignPerformance>>(
            await (await client.GetAsync($"/api/v1/analytics/top-campaigns?{Range(Day1, Day1.AddDays(1))}&limit=1")).Content.ReadAsStringAsync(), ApiFactory.Json)!;
        Assert.Equal("BIG", Assert.Single(top).Code);
    }

    [Fact]
    public async Task Trends_are_daily_gap_filled_and_follow_the_time_zone()
    {
        var client = await FreshOrganizationAsync();
        await CampaignAsync(client, "T");
        await SaleAsync(client, "D-1", Day1, ("T", 10m));
        await SaleAsync(client, "D-2", Day1.AddHours(1), ("T", 5m));
        await SaleAsync(client, "D-3", new DateTimeOffset(2026, 3, 12, 22, 30, 0, TimeSpan.Zero), ("T", 7m)); // 13 March in Istanbul (UTC+3)

        var utc = await (await client.GetAsync($"/api/v1/analytics/trends?{Range(Midnight1, Midnight1.AddDays(4))}")).ReadAsync<AnalyticsTrend>();
        Assert.Equal(4, utc.Points.Count);
        Assert.Equal(new TrendPoint(new DateOnly(2026, 3, 10), 2, 2, 15m), utc.Points[0]);
        Assert.Equal(new TrendPoint(new DateOnly(2026, 3, 11), 0, 0, 0m), utc.Points[1]);
        Assert.Equal(7m, utc.Points[2].Discount);

        var istanbul = await (await client.GetAsync($"/api/v1/analytics/trends?{Range(Midnight1, Midnight1.AddDays(4))}&timeZone=Europe/Istanbul")).ReadAsync<AnalyticsTrend>();
        Assert.Equal(7m, istanbul.Points.Single(p => p.Date == new DateOnly(2026, 3, 13)).Discount);
        Assert.Equal(0m, istanbul.Points.Single(p => p.Date == new DateOnly(2026, 3, 12)).Discount);
    }

    [Fact]
    public async Task Invalid_periods_are_rejected_and_data_is_tenant_scoped()
    {
        var client = await FreshOrganizationAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/analytics/overview?{Range(Day1, Day1.AddDays(-1))}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/analytics/overview?{Range(Day1, Day1.AddDays(400))}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/analytics/trends?timeZone=Mars/Olympus")).StatusCode);

        await CampaignAsync(client, "PRIVATE");
        await SaleAsync(client, "PRIV-1", Day1, ("PRIVATE", 25m));

        var other = await FreshOrganizationAsync();
        var overview = await (await other.GetAsync($"/api/v1/analytics/overview?{Range(Day1, Day1.AddDays(1))}")).ReadAsync<AnalyticsOverview>();
        Assert.Equal(0, overview.TotalCampaigns);
        Assert.Equal(0m, overview.Current.DiscountTotal);

        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Pos().GetAsync("/api/v1/analytics/overview")).StatusCode);
    }
}
