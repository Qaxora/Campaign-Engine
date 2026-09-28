using System.Net;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Evaluation;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Platform;
using CampaignEngine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampaignEngine.Api.Tests;

/// <summary>Tenant A cannot access Tenant B data (ADR 0005).</summary>
public sealed class TenantIsolationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Cart = """
        { "channel": "web", "lines": [ { "lineId": "1", "sku": "ISO-1", "quantity": 1, "unitPrice": 100, "categories": ["iso"] } ] }
        """;

    private const string SharedCart = """
        { "channel": "web", "lines": [ { "lineId": "1", "sku": "ISO-2", "quantity": 1, "unitPrice": 100, "categories": ["iso-shared"] } ] }
        """;

    private static string Campaign(string code, decimal percent, string category = "iso") => $$"""
        { "code": "{{code}}", "name": "{{code}}", "target": { "categories": ["{{category}}"] },
          "reward": { "type": "percentageDiscount", "percent": {{percent}} }, "limits": { "maxRedemptions": 1 } }
        """;

    private static async Task<Campaign> CreateActiveAsync(HttpClient admin, string json)
    {
        var campaign = await (await admin.PostRawAsync("/api/v1/campaigns", json)).ReadAsync<Campaign>();
        await (await admin.PostAsync($"/api/v1/campaigns/{campaign.Id}/activate", null)).ReadAsync<CampaignChange>();
        return campaign;
    }

    [Fact]
    public async Task Campaigns_of_another_tenant_are_invisible_and_untouchable()
    {
        var a = factory.Admin();
        var b = factory.OtherTenant();
        var campaign = await CreateActiveAsync(a, Campaign("ISO-A-ONLY", 10));

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/v1/campaigns/{campaign.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/v1/campaigns/ISO-A-ONLY")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/v1/campaigns/{campaign.Id}/pause", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/v1/campaigns/{campaign.Id}")).StatusCode);

        var list = await (await b.GetAsync("/api/v1/campaigns?search=ISO-A-ONLY")).ReadAsync<PagedResult<Campaign>>();
        Assert.Empty(list.Items);

        var snapshot = await (await b.GetAsync("/api/v1/snapshot")).ReadAsync<SnapshotDocument>();
        Assert.DoesNotContain(snapshot.Campaigns, c => c.Code == "ISO-A-ONLY");

        // Tenant B's carts are not discounted by tenant A's campaigns.
        var evaluation = await (await b.PostRawAsync("/api/v1/evaluate", Cart)).ReadAsync<EvaluationResult>();
        Assert.DoesNotContain(evaluation.AppliedCampaigns, c => c.Code == "ISO-A-ONLY");
    }

    [Fact]
    public async Task Same_codes_and_transaction_ids_are_independent_per_tenant()
    {
        var a = factory.Admin();
        var b = factory.OtherTenant();
        await CreateActiveAsync(a, Campaign("ISO-SHARED", 10, "iso-shared"));
        await CreateActiveAsync(b, Campaign("ISO-SHARED", 30, "iso-shared"));

        var redeemA = await (await factory.Pos().PostRawAsync("/api/v1/redemptions", $$"""{ "transactionId": "ISO-T1", "cart": {{SharedCart}} }""")).ReadAsync<RedemptionResult>();
        var redeemB = await (await b.PostRawAsync("/api/v1/redemptions", $$"""{ "transactionId": "ISO-T1", "cart": {{SharedCart}} }""")).ReadAsync<RedemptionResult>();

        Assert.False(redeemB.Replayed);
        Assert.Equal(10m, redeemA.Campaigns.Single(c => c.CampaignCode == "ISO-SHARED").Discount);
        Assert.Equal(30m, redeemB.Campaigns.Single(c => c.CampaignCode == "ISO-SHARED").Discount);

        // maxRedemptions = 1 is counted per tenant: A's usage does not exhaust B's campaign and vice versa.
        var reverse = await b.PostAsync("/api/v1/redemptions/ISO-T1/reverse", null);
        Assert.Equal(HttpStatusCode.OK, reverse.StatusCode);
        var stillConfirmedInA = await (await factory.Pos().GetAsync("/api/v1/redemptions/ISO-T1")).ReadAsync<RedemptionResult>();
        Assert.Equal(RedemptionStatus.Confirmed, stillConfirmedInA.Status);
    }

    [Fact]
    public async Task Ledger_product_lists_and_webhooks_are_isolated()
    {
        var a = factory.Admin();
        var b = factory.OtherTenant();
        await (await factory.Pos().PostRawAsync("/api/v1/redemptions", $$"""{ "transactionId": "ISO-PRIVATE", "cart": {{Cart}} }""")).ReadAsync<RedemptionResult>();
        await (await a.PostRawAsync("/api/v1/product-lists", """{ "code": "ISO-LIST", "name": "a", "skus": ["X"] }""")).ReadAsync<object>();
        await (await a.PostRawAsync("/api/v1/webhooks", """{ "url": "https://a.example.com/hook" }""")).ReadAsync<object>();

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/v1/redemptions/ISO-PRIVATE")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync("/api/v1/redemptions/ISO-PRIVATE/reverse", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/v1/product-lists/ISO-LIST")).StatusCode);
        Assert.Equal("[]", await (await b.GetAsync("/api/v1/webhooks")).Content.ReadAsStringAsync());

        // Tenant B may create a list with the same code.
        Assert.Equal(HttpStatusCode.Created, (await b.PostRawAsync("/api/v1/product-lists", """{ "code": "ISO-LIST", "name": "b" }""")).StatusCode);
    }

    [Fact]
    public async Task Writing_tenant_owned_data_without_a_tenant_fails()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
        db.Campaigns.Add(new CampaignRecord { Id = Guid.NewGuid(), Code = "NO-TENANT", Name = "x", Definition = "{}" });

        await Assert.ThrowsAsync<TenantRequiredException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Queries_without_a_tenant_see_nothing()
    {
        await CreateActiveAsync(factory.Admin(), Campaign("ISO-HIDDEN", 5));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();

        Assert.Empty(await db.Campaigns.ToListAsync());
        Assert.NotEmpty(await db.Campaigns.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Writing_into_another_tenant_is_refused()
    {
        var tenantB = await Organizations().FindBySlugAsync("tenant-b");
        await using var scope = factory.Services.CreateAsyncScope();
        var tenantA = await scope.ServiceProvider.GetRequiredService<OrganizationService>().FindBySlugAsync("tenant-a");
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantA!.Id);
        var db = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
        db.Campaigns.Add(new CampaignRecord { Id = Guid.NewGuid(), TenantId = tenantB!.Id, Code = "SMUGGLED", Name = "x", Definition = "{}" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private OrganizationService Organizations() =>
        factory.Services.CreateScope().ServiceProvider.GetRequiredService<OrganizationService>();
}
