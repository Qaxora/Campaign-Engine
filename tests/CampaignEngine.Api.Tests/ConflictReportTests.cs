using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Conflicts;
using CampaignEngine.Infrastructure.Platform;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Tests;

public sealed class ConflictReportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> FreshOrganizationAsync()
    {
        var body = JsonSerializer.Serialize(new { email = $"conflicts-{Guid.NewGuid():N}@example.com", password = "correct-horse-battery", name = "C", organizationName = "Conflict Co" }, ApiFactory.Json);
        var signIn = await (await factory.CreateClient().PostRawAsync("/api/v1/auth/register", body)).ReadAsync<SignInResult>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signIn.Token);
        return client;
    }

    private static async Task<Campaign> LiveAsync(HttpClient client, string json)
    {
        var campaign = await (await client.PostRawAsync("/api/v1/campaigns", json)).ReadAsync<Campaign>();
        await (await client.PostAsync($"/api/v1/campaigns/{campaign.Id}/activate?force=true", null)).ReadAsync<CampaignChange>();
        return campaign;
    }

    [Fact]
    public async Task Report_pairs_every_live_conflict_with_both_campaigns_most_severe_first()
    {
        var client = await FreshOrganizationAsync();
        await LiveAsync(client, """
            { "code": "WKND-20", "name": "Weekend 20%", "channels": ["store", "web"], "coupon": { "codes": ["SAVE"] },
              "schedule": { "daysOfWeek": ["saturday", "sunday"], "timeZone": "Europe/Istanbul" },
              "reward": { "type": "percentageDiscount", "percent": 20 } }
            """);
        await LiveAsync(client, """
            { "code": "OVER-500", "name": "500 TRY → 100 TRY", "currency": "TRY", "coupon": { "codes": ["SAVE"] },
              "conditions": [ { "type": "minSubtotal", "amount": 500 } ], "reward": { "type": "amountDiscount", "amount": 100 } }
            """);
        // A draft never takes part in the live analysis.
        await client.PostRawAsync("/api/v1/campaigns", """{ "code": "DRAFT", "name": "d", "coupon": { "codes": ["SAVE"] }, "reward": { "type": "percentageDiscount", "percent": 5 } }""");

        var report = await (await client.GetAsync("/api/v1/campaigns/conflicts/report")).ReadAsync<ConflictReport>();

        Assert.Equal(2, report.LiveCampaigns);
        Assert.Equal(1, report.Errors);
        Assert.Equal(report.Items.Count, report.Errors + report.Warnings + report.Infos);
        var first = report.Items[0];
        Assert.Equal(ConflictKind.DuplicateCoupon, first.Conflict.Kind);
        Assert.Equal(ConflictSeverity.Error, first.Conflict.Severity);
        Assert.Contains(report.Items, i => i.Conflict.Kind == ConflictKind.StackedDiscount);
        Assert.DoesNotContain(report.Items, i => i.Campaign.Code == "DRAFT" || i.Other.Code == "DRAFT");

        var sides = new[] { first.Campaign, first.Other }.ToDictionary(s => s.Code);
        Assert.Equal("Weekend 20%", sides["WKND-20"].Name);
        Assert.Contains("Channels: store, web", sides["WKND-20"].Audience);
        Assert.Contains("On Saturday, Sunday", sides["WKND-20"].Schedule);
        Assert.Equal("100 TRY off the order when the qualifying items total at least 500 TRY with a coupon.", sides["OVER-500"].Summary);
        Assert.Equal(CampaignStatus.Active, sides["OVER-500"].Status);
    }

    [Fact]
    public async Task Report_is_empty_without_conflicts_and_tenant_scoped()
    {
        var client = await FreshOrganizationAsync();
        await LiveAsync(client, """{ "code": "SOLO", "name": "solo", "reward": { "type": "percentageDiscount", "percent": 5 } }""");

        var report = await (await client.GetAsync("/api/v1/campaigns/conflicts/report")).ReadAsync<ConflictReport>();
        Assert.Equal(1, report.LiveCampaigns);
        Assert.Empty(report.Items);

        var other = await (await (await FreshOrganizationAsync()).GetAsync("/api/v1/campaigns/conflicts/report")).ReadAsync<ConflictReport>();
        Assert.Equal(0, other.LiveCampaigns);

        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Pos().GetAsync("/api/v1/campaigns/conflicts/report")).StatusCode);
    }
}
