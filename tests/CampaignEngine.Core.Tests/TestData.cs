using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Carts;
using CampaignEngine.Core.Evaluation;
using CampaignEngine.Core.Products;
using CampaignEngine.Core.Rules;

namespace CampaignEngine.Core.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero); // a Monday

    public static Campaign Campaign(string code, Reward reward, Action<Campaign>? configure = null)
    {
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            Status = CampaignStatus.Active,
            Reward = reward,
        };
        configure?.Invoke(campaign);
        return campaign;
    }

    public static CartLine Line(string id, decimal unitPrice, decimal quantity = 1, string? category = null, string? sku = null, string? brand = null) => new()
    {
        LineId = id,
        Sku = sku ?? $"SKU-{id}",
        UnitPrice = unitPrice,
        Quantity = quantity,
        Brand = brand,
        Categories = category is null ? [] : [category],
    };

    public static Cart Cart(params CartLine[] lines) => new()
    {
        Currency = "TRY",
        Channel = "store",
        StoreId = "IST-001",
        Timestamp = Now,
        Lines = [.. lines],
    };

    public static EvaluationResult Evaluate(
        Cart cart,
        IEnumerable<Campaign> campaigns,
        IEnumerable<ProductList>? lists = null,
        UsageSnapshot? usage = null,
        EngineOptions? options = null) =>
        new PromotionEvaluator(options).Evaluate(
            cart,
            new CatalogSnapshot([.. campaigns], new ProductListIndex(lists ?? [])),
            usage,
            explain: true);

    public static EvaluationResult Evaluate(Cart cart, params Campaign[] campaigns) => Evaluate(cart, campaigns, null);

    public static decimal DiscountOf(this EvaluationResult result, string lineId) =>
        result.Lines.Single(l => l.LineId == lineId).Discount;

    public static RejectionReason ReasonFor(this EvaluationResult result, string code) =>
        result.Rejections!.First(r => r.Code == code).Reason;
}
