using CampaignEngine.Client;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Carts;
using CampaignEngine.Core.Products;
using CampaignEngine.Core.Rules.Rewards;

namespace CampaignEngine.Api.Tests;

public sealed class ClientTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Cart Cart(string category) => new()
    {
        Channel = "web",
        Lines =
        [
            new CartLine { LineId = "1", Sku = $"{category}-1", Quantity = 2, UnitPrice = 250, Categories = [category] },
            new CartLine { LineId = "2", Sku = $"{category}-2", Quantity = 1, UnitPrice = 99.90m, Categories = [category] },
        ],
    };

    [Fact]
    public async Task Typed_client_manages_campaigns_and_redeems()
    {
        var admin = new CampaignEngineClient(factory.Admin());
        var pos = new CampaignEngineClient(factory.Pos());

        await admin.CreateProductListAsync("CLIENT-LIST", "Client list", skus: ["client-1", "client-2"]);
        var campaign = await admin.CreateCampaignAsync(new Campaign
        {
            Code = "CLIENT-15",
            Name = "15% on the client list",
            Target = new ProductSelector { ProductLists = ["CLIENT-LIST"] },
            Reward = new PercentageDiscountReward { Percent = 15 },
        });
        await admin.ActivateCampaignAsync(campaign.Id);

        var result = await pos.EvaluateAsync(Cart("client"));
        Assert.Equal(89.99m, result.TotalDiscount); // 15% of 599.90 = 89.985 → 89.99

        var redemption = await pos.RedeemAsync("CLIENT-T1", Cart("client"));
        Assert.Equal(89.99m, redemption.TotalDiscount);
        Assert.Equal("reversed", (await pos.ReverseAsync("CLIENT-T1", "customer returned")).Status);

        var error = await Assert.ThrowsAsync<CampaignEngineApiException>(() =>
            admin.CreateCampaignAsync(new Campaign { Code = "CLIENT-15", Name = "dupe", Reward = new AmountDiscountReward { Amount = 1 } }));
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task Local_engine_matches_the_server_and_uses_etags()
    {
        var admin = new CampaignEngineClient(factory.Admin());
        var campaign = await admin.CreateCampaignAsync(new Campaign
        {
            Code = "LOCAL-3X2",
            Name = "3 for 2 local",
            Target = new ProductSelector { Categories = ["local"] },
            Reward = new BuyXGetYReward { BuyQuantity = 2, GetQuantity = 1 },
        });
        await admin.ActivateCampaignAsync(campaign.Id);

        var pos = new CampaignEngineClient(factory.Pos());
        var local = new LocalCampaignEngine(pos);
        Assert.True(await local.RefreshAsync());
        Assert.False(await local.RefreshAsync()); // 304

        var offline = local.Evaluate(Cart("local"));
        var online = await pos.EvaluateAsync(Cart("local"));
        Assert.Equal(online.TotalDiscount, offline.TotalDiscount);
        Assert.Equal(99.90m, offline.TotalDiscount);

        var imported = await pos.ImportOfflineAsync(OfflineSale.From("LOCAL-T1", Cart("local"), offline));
        Assert.True(imported.Offline);
        Assert.Equal("LOCAL-3X2", imported.Campaigns.Single().CampaignCode);
    }
}
