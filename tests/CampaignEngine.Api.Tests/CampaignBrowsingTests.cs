using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Infrastructure.Ledger;
using CampaignEngine.Infrastructure.Platform;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Tests;

/// <summary>What the campaign list and detail screens need from the API.</summary>
public sealed class CampaignBrowsingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> FreshOrganizationAsync()
    {
        var body = JsonSerializer.Serialize(new { email = $"browse-{Guid.NewGuid():N}@example.com", password = "correct-horse-battery", name = "B", organizationName = "Browse Co" }, ApiFactory.Json);
        var signIn = await (await factory.CreateClient().PostRawAsync("/api/v1/auth/register", body)).ReadAsync<SignInResult>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signIn.Token);
        return client;
    }

    private static async Task<Campaign> CreateAsync(HttpClient client, string code, int priority, string? startsAt, string? endsAt)
    {
        var schedule = JsonSerializer.Serialize(new { startsAt, endsAt });
        return await (await client.PostRawAsync("/api/v1/campaigns", $$"""
            { "code": "{{code}}", "name": "{{code}} name", "priority": {{priority}}, "schedule": {{schedule}},
              "reward": { "type": "percentageDiscount", "percent": 10 } }
            """)).ReadAsync<Campaign>();
    }

    [Fact]
    public async Task List_sorts_and_filters_by_schedule_window()
    {
        var client = await FreshOrganizationAsync();
        await CreateAsync(client, "JUNE", 5, "2026-06-01T00:00:00Z", "2026-07-01T00:00:00Z");
        await CreateAsync(client, "SUMMER", 20, "2026-06-15T00:00:00Z", "2026-09-01T00:00:00Z");
        await CreateAsync(client, "ALWAYS", 1, null, null);
        await CreateAsync(client, "OCTOBER", 10, "2026-10-01T00:00:00Z", "2026-11-01T00:00:00Z");

        async Task<string[]> Codes(string query) =>
            (await (await client.GetAsync($"/api/v1/campaigns?{query}")).ReadAsync<PagedResult<Campaign>>()).Items.Select(c => c.Code).ToArray();

        Assert.Equal(["SUMMER", "OCTOBER", "JUNE", "ALWAYS"], await Codes("sort=priority"));
        Assert.Equal(["ALWAYS", "JUNE", "OCTOBER", "SUMMER"], await Codes("sort=code&order=asc"));
        Assert.Equal(["JUNE", "SUMMER", "OCTOBER", "ALWAYS"], await Codes("sort=startsAt&order=asc")); // undated last
        Assert.Equal(["ALWAYS", "SUMMER"], await Codes("activeFrom=2026-08-01T00:00:00Z&activeTo=2026-08-02T00:00:00Z&sort=code&order=asc"));
        Assert.Equal(["ALWAYS", "JUNE", "SUMMER"], await Codes("activeFrom=2026-06-20T00:00:00Z&activeTo=2026-06-21T00:00:00Z&sort=code&order=asc"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/campaigns?sort=random")).StatusCode);
    }

    [Fact]
    public async Task Description_is_served_for_saved_and_unsaved_campaigns()
    {
        var client = await FreshOrganizationAsync();
        var campaign = await (await client.PostRawAsync("/api/v1/campaigns", """
            { "code": "WKND", "name": "Weekend", "currency": "TRY",
              "conditions": [ { "type": "minSubtotal", "amount": 500 } ],
              "reward": { "type": "amountDiscount", "amount": 100 } }
            """)).ReadAsync<Campaign>();

        var byCode = await (await client.GetAsync("/api/v1/campaigns/wknd/description")).ReadAsync<CampaignDescription>();
        Assert.Equal("100 TRY off the order when the qualifying items total at least 500 TRY.", byCode.Summary);
        var byId = await (await client.GetAsync($"/api/v1/campaigns/{campaign.Id}/description")).ReadAsync<CampaignDescription>();
        Assert.Equal(JsonSerializer.Serialize(byCode), JsonSerializer.Serialize(byId));

        var draft = await (await client.PostRawAsync("/api/v1/campaigns/describe",
            """{ "code": "X", "name": "x", "reward": { "type": "buyXGetY", "buyQuantity": 2, "getQuantity": 1 } }""")).ReadAsync<CampaignDescription>();
        Assert.Equal("3 for 2.", draft.Summary);

        Assert.Equal(HttpStatusCode.NotFound, (await factory.OtherTenant().GetAsync($"/api/v1/campaigns/{campaign.Id}/description")).StatusCode);
    }

    [Fact]
    public async Task Usage_of_one_campaign()
    {
        var client = await FreshOrganizationAsync();
        var campaign = await CreateAsync(client, "USE-ONE", 0, null, null);
        await (await client.PostAsync($"/api/v1/campaigns/{campaign.Id}/activate", null)).ReadAsync<CampaignChange>();
        await client.PostRawAsync("/api/v1/redemptions/offline",
            """{ "transactionId": "U-1", "currency": "TRY", "campaigns": [ { "campaignCode": "USE-ONE", "discount": 12.5 } ] }""");

        var usage = await (await client.GetAsync($"/api/v1/ledger/usage/campaigns/{campaign.Id}")).ReadAsync<CampaignUsage>();
        Assert.Equal(1, usage.Redemptions);
        Assert.Equal(12.5m, usage.DiscountTotal);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/ledger/usage/campaigns/{Guid.NewGuid()}")).StatusCode);
    }
}
