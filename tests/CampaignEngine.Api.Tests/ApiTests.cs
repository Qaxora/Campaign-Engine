using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Conflicts;
using CampaignEngine.Core.Evaluation;
using CampaignEngine.Infrastructure.Services;
using CampaignEngine.Infrastructure.Webhooks;
using Microsoft.Extensions.DependencyInjection;

namespace CampaignEngine.Api.Tests;

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Cart(string skuPrefix, int quantity = 3, decimal price = 100, string? coupon = null, string? customer = null) => $$"""
        {
          "channel": "store",
          "storeId": "IST-001",
          "customer": {{(customer is null ? "null" : $$"""{ "id": "{{customer}}" }""")}},
          "couponCodes": [{{(coupon is null ? "" : $"\"{coupon}\"")}}],
          "lines": [
            { "lineId": "1", "sku": "{{skuPrefix}}-1", "quantity": {{quantity}}, "unitPrice": {{price}}, "categories": ["{{skuPrefix}}"] },
            { "lineId": "2", "sku": "TOBACCO-1", "quantity": 1, "unitPrice": 50, "categories": ["{{skuPrefix}}"] }
          ]
        }
        """;

    private async Task<Campaign> CreateActiveAsync(string json, bool force = false)
    {
        var admin = factory.Admin();
        var campaign = await (await admin.PostRawAsync("/api/v1/campaigns", json)).ReadAsync<Campaign>();
        var change = await (await admin.PostAsync($"/api/v1/campaigns/{campaign.Id}/activate?force={force}", null)).ReadAsync<CampaignChange>();
        Assert.Equal(CampaignStatus.Active, change.Campaign.Status);
        return change.Campaign;
    }

    private async Task EnsureTobaccoExcludedAsync()
    {
        var response = await factory.Admin().PostRawAsync("/api/v1/product-lists",
            """{ "code": "REGULATED", "name": "Never discount", "kind": "globalExclusion", "skus": ["TOBACCO-1"] }""");
        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Requests_need_a_valid_key_and_role()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/campaigns")).StatusCode);

        var wrongKey = factory.CreateClient();
        wrongKey.DefaultRequestHeaders.Add("X-Api-Key", "nope");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrongKey.GetAsync("/api/v1/campaigns")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Pos().GetAsync("/api/v1/campaigns")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Admin().GetAsync("/api/v1/campaigns")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task End_to_end_evaluate_redeem_replay_and_reverse()
    {
        await EnsureTobaccoExcludedAsync();
        await CreateActiveAsync("""
            {
              "code": "E2E-3X2", "name": "3 for 2",
              "target": { "categories": ["e2e"] },
              "reward": { "type": "buyXGetY", "buyQuantity": 2, "getQuantity": 1 },
              "limits": { "maxRedemptions": 1 },
              "metadata": { "erpCampaignNo": "2026-0042" }
            }
            """);
        var pos = factory.Pos();

        var evaluation = await (await pos.PostRawAsync("/api/v1/evaluate?explain=true", Cart("e2e"))).ReadAsync<EvaluationResult>();
        Assert.Equal(100m, evaluation.LineDiscount);
        Assert.Equal(0m, evaluation.Lines.Single(l => l.Sku == "TOBACCO-1").Discount);
        Assert.Equal("2026-0042", evaluation.AppliedCampaigns.Single().Metadata["erpCampaignNo"]);

        var redeem = await pos.PostRawAsync("/api/v1/redemptions", $$"""{ "transactionId": "E2E-T1", "cart": {{Cart("e2e")}} }""");
        Assert.Equal(HttpStatusCode.Created, redeem.StatusCode);
        Assert.Equal(100m, (await redeem.ReadAsync<RedemptionResult>()).TotalDiscount);

        var replay = await pos.PostRawAsync("/api/v1/redemptions", $$"""{ "transactionId": "E2E-T1", "cart": {{Cart("e2e")}} }""");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.ReadAsync<RedemptionResult>()).Replayed);

        // maxRedemptions = 1 is used up.
        var second = await (await pos.PostRawAsync("/api/v1/redemptions", $$"""{ "transactionId": "E2E-T2", "cart": {{Cart("e2e")}} }""")).ReadAsync<RedemptionResult>();
        Assert.Equal(0m, second.TotalDiscount);

        var reversed = await (await pos.PostAsync("/api/v1/redemptions/E2E-T1/reverse", null)).ReadAsync<RedemptionResult>();
        Assert.Equal(Infrastructure.Persistence.RedemptionStatus.Reversed, reversed.Status);

        var third = await (await pos.PostRawAsync("/api/v1/redemptions", $$"""{ "transactionId": "E2E-T3", "cart": {{Cart("e2e")}} }""")).ReadAsync<RedemptionResult>();
        Assert.Equal(100m, third.TotalDiscount);

        var stored = await (await pos.GetAsync("/api/v1/redemptions/E2E-T3")).ReadAsync<RedemptionResult>();
        Assert.Equal("E2E-3X2", stored.Campaigns.Single().CampaignCode);
    }

    [Fact]
    public async Task Concurrent_redemptions_never_exceed_the_limit()
    {
        await CreateActiveAsync("""
            {
              "code": "RACE-10", "name": "10% for the first five",
              "target": { "categories": ["race"] },
              "reward": { "type": "percentageDiscount", "percent": 10 },
              "limits": { "maxRedemptions": 5 }
            }
            """);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            var response = await factory.Pos().PostRawAsync("/api/v1/redemptions", $$"""{ "transactionId": "RACE-{{i}}", "cart": {{Cart("race", quantity: 1)}} }""");
            return await response.ReadAsync<RedemptionResult>();
        }));

        Assert.Equal(5, results.Count(r => r.TotalDiscount > 0));
    }

    [Fact]
    public async Task Duplicate_coupon_blocks_activation_unless_forced()
    {
        await CreateActiveAsync("""
            { "code": "COUPON-A", "name": "A", "coupon": { "codes": ["SAMECODE"] }, "reward": { "type": "amountDiscount", "amount": 10 } }
            """);
        var admin = factory.Admin();
        var second = await (await admin.PostRawAsync("/api/v1/campaigns", """
            { "code": "COUPON-B", "name": "B", "coupon": { "codes": ["samecode"] }, "reward": { "type": "amountDiscount", "amount": 20 } }
            """)).ReadAsync<Campaign>();

        var blocked = await admin.PostAsync($"/api/v1/campaigns/{second.Id}/activate", null);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var problem = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("duplicateCoupon", problem.GetProperty("conflicts")[0].GetProperty("kind").GetString());

        var forced = await (await admin.PostAsync($"/api/v1/campaigns/{second.Id}/activate?force=true", null)).ReadAsync<CampaignChange>();
        Assert.Contains(forced.Conflicts, c => c.Kind == ConflictKind.DuplicateCoupon);
    }

    [Fact]
    public async Task Stale_version_is_rejected()
    {
        var admin = factory.Admin();
        var created = await (await admin.PostRawAsync("/api/v1/campaigns",
            """{ "code": "VERSIONED", "name": "v1", "reward": { "type": "percentageDiscount", "percent": 5 } }""")).ReadAsync<Campaign>();

        created.Name = "v2";
        var ok = await admin.PutAsJsonAsync($"/api/v1/campaigns/{created.Id}", created, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        created.Name = "stale";
        var stale = await admin.PutAsJsonAsync($"/api/v1/campaigns/{created.Id}", created, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Invalid_definitions_return_all_errors()
    {
        var response = await factory.Admin().PostRawAsync("/api/v1/campaigns",
            """{ "code": "bad code", "name": "", "reward": { "type": "percentageDiscount", "percent": 150 }, "target": { "productLists": ["MISSING"] } }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.Equal(4, errors.GetArrayLength());
    }

    [Fact]
    public async Task Malformed_json_and_unknown_types_are_bad_requests()
    {
        var admin = factory.Admin();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRawAsync("/api/v1/campaigns", "{ not json")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRawAsync("/api/v1/campaigns",
            """{ "code": "X", "name": "X", "reward": { "type": "teleport" } }""")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.Pos().PostRawAsync("/api/v1/evaluate",
            """{ "lines": [ { "lineId": "1", "sku": "A", "quantity": 0, "unitPrice": 10 } ] }""")).StatusCode);
    }

    [Fact]
    public async Task Snapshot_supports_conditional_requests()
    {
        await CreateActiveAsync("""{ "code": "SNAP-1", "name": "s", "target": { "categories": ["snap"] }, "reward": { "type": "percentageDiscount", "percent": 1 } }""");
        var pos = factory.Pos();

        var first = await pos.GetAsync("/api/v1/snapshot");
        var snapshot = await first.ReadAsync<SnapshotDocument>();
        Assert.Contains(snapshot.Campaigns, c => c.Code == "SNAP-1");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/snapshot");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await pos.SendAsync(request)).StatusCode);

        // A local evaluation from the snapshot gives the same answer as the server.
        var cart = System.Text.Json.JsonSerializer.Deserialize<Core.Carts.Cart>(Cart("snap"), ApiFactory.Json)!;
        var local = new PromotionEvaluator().Evaluate(cart, snapshot.ToCatalog());
        var remote = await (await pos.PostRawAsync("/api/v1/evaluate", Cart("snap"))).ReadAsync<EvaluationResult>();
        Assert.Equal(remote.TotalDiscount, local.TotalDiscount);
    }

    [Fact]
    public async Task Product_lists_import_csv_and_refuse_deleting_lists_in_use()
    {
        var admin = factory.Admin();
        await (await admin.PostRawAsync("/api/v1/product-lists", """{ "code": "CSV-LIST", "name": "csv" }""")).ReadAsync<object>();

        var csv = new StringContent("sku,name\nA-1,one\n\"A-2\",two\n\nA-1,dupe\n");
        csv.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var imported = await (await admin.PutAsync("/api/v1/product-lists/csv-list/skus/import", csv)).ReadAsync<Core.Products.ProductList>();
        Assert.Equal(["A-1", "A-2"], imported.Skus.Order());

        await CreateActiveAsync("""
            { "code": "USES-CSV", "name": "u", "target": { "productLists": ["CSV-LIST"] }, "reward": { "type": "percentageDiscount", "percent": 5 } }
            """);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync("/api/v1/product-lists/CSV-LIST")).StatusCode);
    }

    [Fact]
    public async Task Offline_sales_are_imported_once()
    {
        await CreateActiveAsync("""{ "code": "OFFLINE-5", "name": "o", "target": { "categories": ["offline"] }, "reward": { "type": "percentageDiscount", "percent": 5 } }""");
        var pos = factory.Pos();
        const string sale = """
            { "transactionId": "OFF-1", "channel": "store", "timestamp": "2026-06-01T10:00:00Z",
              "campaigns": [ { "campaignCode": "offline-5", "discount": 12.5 } ] }
            """;

        var first = await pos.PostRawAsync("/api/v1/redemptions/offline", sale);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var again = await pos.PostRawAsync("/api/v1/redemptions/offline", sale);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        var unknown = await pos.PostRawAsync("/api/v1/redemptions/offline",
            """{ "transactionId": "OFF-2", "campaigns": [ { "campaignCode": "NOPE", "discount": 1 } ] }""");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Webhooks_are_delivered_with_a_valid_signature()
    {
        var admin = factory.Admin();
        var subscription = await (await admin.PostRawAsync("/api/v1/webhooks",
            """{ "url": "https://erp.example.com/hooks/campaigns", "events": ["campaign.*"], "secret": "0123456789abcdef-secret" }""")).ReadAsync<CreatedWebhookSubscription>();

        await admin.PostRawAsync("/api/v1/campaigns", """{ "code": "HOOKED", "name": "h", "reward": { "type": "percentageDiscount", "percent": 5 } }""");
        await admin.PostRawAsync("/api/v1/product-lists", """{ "code": "NOT-SUBSCRIBED", "name": "n" }""");

        await factory.Services.GetRequiredService<WebhookDispatcher>().DispatchBatchAsync(CancellationToken.None);

        var (request, body) = Assert.Single(factory.Webhooks.Requests, r => r.Body.Contains("HOOKED", StringComparison.Ordinal));
        Assert.Equal("campaign.created", request.Headers.GetValues("X-Campaign-Event").Single());
        var timestamp = long.Parse(request.Headers.GetValues("X-Campaign-Timestamp").Single(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(WebhookSigner.Verify(subscription.Secret, timestamp, body, request.Headers.GetValues("X-Campaign-Signature").Single()));
        Assert.DoesNotContain(factory.Webhooks.Requests, r => r.Body.Contains("NOT-SUBSCRIBED", StringComparison.Ordinal));

        var deliveries = await (await admin.GetAsync($"/api/v1/webhooks/{subscription.Id}/deliveries")).ReadAsync<List<WebhookDelivery>>();
        Assert.All(deliveries, d => Assert.NotNull(d.DeliveredAt));
    }

    [Fact]
    public async Task OpenApi_document_is_generated()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.True(document.GetProperty("paths").TryGetProperty("/api/v1/evaluate", out _));
    }
}
