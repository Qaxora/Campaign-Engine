using System.Net;
using System.Text.Json;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Infrastructure.Ledger;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Tests;

public sealed class LedgerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Cart(string category, string? customer = null, string channel = "store", string? store = "IST-1") => $$"""
        { "channel": "{{channel}}", "storeId": {{(store is null ? "null" : $"\"{store}\"")}}, "customer": {{(customer is null ? "null" : $"{{ \"id\": \"{customer}\" }}")}},
          "lines": [ { "lineId": "1", "sku": "L-1", "quantity": 1, "unitPrice": 200, "categories": ["{{category}}"] } ] }
        """;

    private async Task<Campaign> ActiveCampaignAsync(string code, string category, string limits = "{}")
    {
        var admin = factory.Admin();
        var campaign = await (await admin.PostRawAsync("/api/v1/campaigns", $$"""
            { "code": "{{code}}", "name": "{{code}} name", "target": { "categories": ["{{category}}"] },
              "reward": { "type": "percentageDiscount", "percent": 10 }, "limits": {{limits}} }
            """)).ReadAsync<Campaign>();
        await (await admin.PostAsync($"/api/v1/campaigns/{campaign.Id}/activate", null)).ReadAsync<CampaignChange>();
        return campaign;
    }

    private async Task RedeemAsync(string transactionId, string cart, HttpClient? client = null) =>
        await (await (client ?? factory.Pos()).PostRawAsync("/api/v1/redemptions", $$"""{ "transactionId": "{{transactionId}}", "cart": {{cart}} }""")).ReadAsync<RedemptionResult>();

    [Fact]
    public async Task Transactions_can_be_filtered_paged_and_inspected()
    {
        await ActiveCampaignAsync("LEDGER-A", "ledger-a");
        await RedeemAsync("LG-1", Cart("ledger-a", "C-1"));
        await RedeemAsync("LG-2", Cart("ledger-a", "C-2", channel: "web", store: null));
        await RedeemAsync("LG-3", Cart("ledger-a", "C-1", store: "ANK-1"));
        await factory.Pos().PostAsync("/api/v1/redemptions/LG-3/reverse", null);

        var admin = factory.Admin();
        var all = await (await admin.GetAsync("/api/v1/ledger/transactions?campaign=ledger-a&pageSize=2")).ReadAsync<PagedResult<LedgerTransaction>>();
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(["LG-3", "LG-2"], all.Items.Select(t => t.TransactionId)); // newest first

        var web = await (await admin.GetAsync("/api/v1/ledger/transactions?campaign=LEDGER-A&channel=WEB")).ReadAsync<PagedResult<LedgerTransaction>>();
        Assert.Equal("LG-2", Assert.Single(web.Items).TransactionId);

        var reversed = await (await admin.GetAsync("/api/v1/ledger/transactions?campaign=LEDGER-A&status=reversed")).ReadAsync<PagedResult<LedgerTransaction>>();
        Assert.Equal("LG-3", Assert.Single(reversed.Items).TransactionId);

        var store = await (await admin.GetAsync("/api/v1/ledger/transactions?campaign=LEDGER-A&storeId=ist-1&customerId=C-1")).ReadAsync<PagedResult<LedgerTransaction>>();
        Assert.Equal("LG-1", Assert.Single(store.Items).TransactionId);

        var future = await (await admin.GetAsync($"/api/v1/ledger/transactions?campaign=LEDGER-A&from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"))}")).ReadAsync<PagedResult<LedgerTransaction>>();
        Assert.Equal(0, future.TotalCount);

        var detail = await (await admin.GetAsync("/api/v1/ledger/transactions/LG-1")).ReadAsync<LedgerTransactionDetail>();
        var redemption = Assert.Single(detail.Redemptions);
        Assert.Equal("LEDGER-A name", redemption.CampaignName);
        Assert.Equal(20m, redemption.Discount);
        Assert.Equal("pos-ist-001", detail.Transaction.Client);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/ledger/transactions?status=nope")).StatusCode);
    }

    [Fact]
    public async Task Campaign_usage_reflects_limits_budget_and_reversals()
    {
        var campaign = await ActiveCampaignAsync("LEDGER-B", "ledger-b", """{ "maxRedemptions": 10, "budget": 100 }""");
        await RedeemAsync("LB-1", Cart("ledger-b"));
        await RedeemAsync("LB-2", Cart("ledger-b"));
        await factory.Pos().PostAsync("/api/v1/redemptions/LB-2/reverse", null);

        var usage = JsonSerializer.Deserialize<List<CampaignUsage>>(
            await (await factory.Admin().GetAsync("/api/v1/ledger/usage/campaigns?status=active")).Content.ReadAsStringAsync(), ApiFactory.Json)!;
        var row = usage.Single(u => u.CampaignId == campaign.Id);
        Assert.Equal(1, row.Redemptions);
        Assert.Equal(20m, row.DiscountTotal);
        Assert.Equal(80m, row.BudgetRemaining);
        Assert.Equal(10, row.MaxRedemptions);
        Assert.Equal(1, row.ReversedRedemptions);
        Assert.NotNull(row.LastRedeemedAt);
    }

    [Fact]
    public async Task Customer_usage_counts_confirmed_redemptions_per_campaign()
    {
        await ActiveCampaignAsync("LEDGER-C", "ledger-c", """{ "maxRedemptionsPerCustomer": 3 }""");
        await RedeemAsync("LC-1", Cart("ledger-c", "CUST-9"));
        await RedeemAsync("LC-2", Cart("ledger-c", "CUST-9"));
        await RedeemAsync("LC-3", Cart("ledger-c", "CUST-9"));
        await factory.Pos().PostAsync("/api/v1/redemptions/LC-3/reverse", null);

        var usage = await (await factory.Admin().GetAsync("/api/v1/ledger/usage/customers/CUST-9")).ReadAsync<CustomerUsage>();
        Assert.Equal(2, usage.Transactions);
        Assert.Equal(40m, usage.TotalDiscount);
        var row = Assert.Single(usage.Campaigns);
        Assert.Equal(2, row.Redemptions);
        Assert.Equal(3, row.MaxRedemptionsPerCustomer);
    }

    [Fact]
    public async Task Redemptions_are_listed_per_campaign_with_sale_context()
    {
        await ActiveCampaignAsync("LEDGER-E", "ledger-e");
        await ActiveCampaignAsync("LEDGER-F", "ledger-f");
        const string twoCampaigns = """
            { "channel": "web", "storeId": "WEB", "customer": { "id": "CUST-E" },
              "lines": [ { "lineId": "1", "sku": "E-1", "quantity": 1, "unitPrice": 100, "categories": ["ledger-e", "ledger-f"] } ] }
            """;
        await RedeemAsync("LE-1", twoCampaigns);
        await RedeemAsync("LE-2", Cart("ledger-e", "CUST-E"));
        await factory.Pos().PostAsync("/api/v1/redemptions/LE-2/reverse", null);

        var admin = factory.Admin();
        var e = await (await admin.GetAsync("/api/v1/ledger/redemptions?campaign=ledger-e")).ReadAsync<PagedResult<RedemptionRow>>();
        Assert.Equal(["LE-2", "LE-1"], e.Items.Select(r => r.TransactionId));
        Assert.Equal("LEDGER-E name", e.Items[0].CampaignName);
        Assert.Equal(RedemptionStatus.Reversed, e.Items[0].Status);
        Assert.Equal("web", e.Items[1].Channel);
        Assert.Equal("WEB", e.Items[1].StoreId);

        var confirmed = await (await admin.GetAsync("/api/v1/ledger/redemptions?customerId=CUST-E&status=confirmed")).ReadAsync<PagedResult<RedemptionRow>>();
        Assert.Equal(["LEDGER-E", "LEDGER-F"], confirmed.Items.Select(r => r.CampaignCode).Order());
        Assert.All(confirmed.Items, r => Assert.Equal("LE-1", r.TransactionId));

        var other = await (await factory.OtherTenant().GetAsync("/api/v1/ledger/redemptions?customerId=CUST-E")).ReadAsync<PagedResult<RedemptionRow>>();
        Assert.Equal(0, other.TotalCount);
    }

    [Fact]
    public async Task Ledger_is_tenant_scoped_and_needs_read_access()
    {
        await ActiveCampaignAsync("LEDGER-D", "ledger-d");
        await RedeemAsync("LD-PRIVATE", Cart("ledger-d", "CUST-D"));

        var other = factory.OtherTenant();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/api/v1/ledger/transactions/LD-PRIVATE")).StatusCode);
        var otherList = await (await other.GetAsync("/api/v1/ledger/transactions?search=LD-PRIVATE")).ReadAsync<PagedResult<LedgerTransaction>>();
        Assert.Equal(0, otherList.TotalCount);
        var otherCustomer = await (await other.GetAsync("/api/v1/ledger/usage/customers/CUST-D")).ReadAsync<CustomerUsage>();
        Assert.Empty(otherCustomer.Campaigns);

        // Channel keys price and redeem; they do not browse the ledger.
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Pos().GetAsync("/api/v1/ledger/transactions")).StatusCode);
    }
}
