using CampaignEngine.Core.Evaluation;
using CampaignEngine.Core.Products;
using CampaignEngine.Core.Rules.Conditions;
using CampaignEngine.Core.Rules.Rewards;
using static CampaignEngine.Core.Tests.TestData;

namespace CampaignEngine.Core.Tests;

public class RewardTests
{
    [Fact]
    public void Percentage_discount_applies_to_target_lines_only()
    {
        var cart = Cart(Line("1", 100, category: "tshirt"), Line("2", 200, category: "jeans"));
        var campaign = Campaign("TSHIRT20", new PercentageDiscountReward { Percent = 20 },
            c => c.Target = new ProductSelector { Categories = ["tshirt"] });

        var result = Evaluate(cart, campaign);

        Assert.Equal(20m, result.DiscountOf("1"));
        Assert.Equal(0m, result.DiscountOf("2"));
        Assert.Equal(280m, result.Total);
    }

    [Fact]
    public void Percentage_discount_respects_its_own_cap()
    {
        var cart = Cart(Line("1", 1000));
        var campaign = Campaign("P", new PercentageDiscountReward { Percent = 50, MaxDiscount = 100 });

        Assert.Equal(100m, Evaluate(cart, campaign).LineDiscount);
    }

    [Fact]
    public void Amount_discount_is_allocated_to_the_cent()
    {
        var cart = Cart(Line("1", 10), Line("2", 10), Line("3", 10));
        var campaign = Campaign("A", new AmountDiscountReward { Amount = 10 });

        var result = Evaluate(cart, campaign);

        Assert.Equal(10m, result.LineDiscount);
        Assert.Equal([3.34m, 3.33m, 3.33m], result.Lines.Select(l => l.Discount));
        Assert.Equal(10m, result.AppliedCampaigns.Single().Lines.Sum(l => l.Discount));
    }

    [Fact]
    public void Amount_discount_never_exceeds_the_eligible_amount()
    {
        var cart = Cart(Line("1", 30));
        var campaign = Campaign("A", new AmountDiscountReward { Amount = 50 });

        Assert.Equal(30m, Evaluate(cart, campaign).LineDiscount);
    }

    [Fact]
    public void Per_unit_amount_discount_multiplies_by_quantity()
    {
        var cart = Cart(Line("1", 50, quantity: 3));
        var campaign = Campaign("A", new AmountDiscountReward { Amount = 5, PerUnit = true });

        Assert.Equal(15m, Evaluate(cart, campaign).LineDiscount);
    }

    [Fact]
    public void Three_for_two_gives_the_cheapest_unit_free()
    {
        var cart = Cart(Line("1", 100), Line("2", 80), Line("3", 60));
        var campaign = Campaign("3X2", new BuyXGetYReward { BuyQuantity = 2, GetQuantity = 1 });

        var result = Evaluate(cart, campaign);

        Assert.Equal(60m, result.LineDiscount);
        Assert.Equal(60m, result.DiscountOf("3"));
    }

    [Fact]
    public void Three_for_two_groups_units_from_most_to_least_expensive()
    {
        var cart = Cart(Line("1", 100, quantity: 3), Line("2", 50, quantity: 3));
        var campaign = Campaign("3X2", new BuyXGetYReward { BuyQuantity = 2, GetQuantity = 1 });

        // sets: (100,100,100) → 100 free, (50,50,50) → 50 free
        Assert.Equal(150m, Evaluate(cart, campaign).LineDiscount);
    }

    [Fact]
    public void Buy_x_get_y_respects_max_applications()
    {
        var cart = Cart(Line("1", 10, quantity: 9));
        var campaign = Campaign("3X2", new BuyXGetYReward { BuyQuantity = 2, GetQuantity = 1, MaxApplications = 2 });

        Assert.Equal(20m, Evaluate(cart, campaign).LineDiscount);
    }

    [Fact]
    public void Second_item_half_price()
    {
        var cart = Cart(Line("1", 200, quantity: 2));
        var campaign = Campaign("2ND50", new BuyXGetYReward { BuyQuantity = 1, GetQuantity = 1, DiscountPercent = 50 });

        Assert.Equal(100m, Evaluate(cart, campaign).LineDiscount);
    }

    [Fact]
    public void Buy_shoe_get_cheapest_socks_free()
    {
        var cart = Cart(
            Line("shoe", 1500, category: "shoes"),
            Line("sock-a", 90, category: "socks"),
            Line("sock-b", 60, category: "socks"));
        var campaign = Campaign("SHOE-SOCK", new BuyXGetYReward
        {
            BuyQuantity = 1,
            GetQuantity = 1,
            GetProducts = new ProductSelector { Categories = ["socks"] },
        }, c => c.Target = new ProductSelector { Categories = ["shoes"] });

        var result = Evaluate(cart, campaign);

        Assert.Equal(60m, result.LineDiscount);
        Assert.Equal(60m, result.DiscountOf("sock-b"));
    }

    [Fact]
    public void Buy_x_get_y_does_not_count_the_same_unit_twice_when_pools_overlap()
    {
        // One product in both pools: buy 1 get 1 must need two physical units.
        var cart = Cart(Line("1", 100));
        var campaign = Campaign("B1G1", new BuyXGetYReward
        {
            BuyQuantity = 1,
            GetQuantity = 1,
            GetProducts = ProductSelector.All,
        });

        var result = Evaluate(cart, campaign);

        Assert.Equal(0m, result.LineDiscount);
        Assert.Equal(RejectionReason.NoDiscount, result.ReasonFor("B1G1"));
    }

    [Fact]
    public void Weighed_items_are_not_used_by_unit_offers()
    {
        var cart = Cart(Line("1", 40, quantity: 2.5m));
        var campaign = Campaign("3X2", new BuyXGetYReward { BuyQuantity = 1, GetQuantity = 1 });

        Assert.Equal(0m, Evaluate(cart, campaign).LineDiscount);
    }

    [Fact]
    public void Bundle_price_uses_most_expensive_units_first()
    {
        var cart = Cart(Line("1", 50), Line("2", 40), Line("3", 30), Line("4", 20));
        var campaign = Campaign("ANY3-100", new BundlePriceReward { Quantity = 3, Price = 100 });

        var result = Evaluate(cart, campaign);

        // (50 + 40 + 30) - 100 = 20, spread over the three units in the set
        Assert.Equal(20m, result.LineDiscount);
        Assert.Equal(0m, result.DiscountOf("4"));
        Assert.Equal(100m, result.Lines.Where(l => l.LineId != "4").Sum(l => l.Net));
    }

    [Fact]
    public void Bundle_is_not_applied_when_it_would_not_be_cheaper()
    {
        var cart = Cart(Line("1", 20, quantity: 3));
        var campaign = Campaign("ANY3-100", new BundlePriceReward { Quantity = 3, Price = 100 });

        Assert.Equal(0m, Evaluate(cart, campaign).LineDiscount);
    }

    [Fact]
    public void Fixed_unit_price_discounts_down_to_the_price()
    {
        var cart = Cart(Line("1", 149.90m, quantity: 2), Line("2", 79.90m));
        var campaign = Campaign("ALL-99", new FixedUnitPriceReward { Price = 99.90m });

        var result = Evaluate(cart, campaign);

        Assert.Equal(100m, result.DiscountOf("1"));
        Assert.Equal(0m, result.DiscountOf("2"));
    }

    [Fact]
    public void Tiered_discount_applies_highest_reached_tier_and_hints_the_next()
    {
        var cart = Cart(Line("1", 300), Line("2", 300));
        var campaign = Campaign("SPEND", new TieredDiscountReward
        {
            Tiers =
            [
                new Tier { Threshold = 500, Amount = 50 },
                new Tier { Threshold = 1000, Amount = 150 },
            ],
        });

        var result = Evaluate(cart, campaign);

        Assert.Equal(50m, result.LineDiscount);
        var hint = Assert.Single(result.Hints);
        Assert.Equal(400m, hint.MissingAmount);
    }

    [Fact]
    public void Tiered_discount_by_quantity_with_percentages()
    {
        var cart = Cart(Line("1", 100, quantity: 3));
        var campaign = Campaign("MORE", new TieredDiscountReward
        {
            Basis = TierBasis.Quantity,
            Tiers =
            [
                new Tier { Threshold = 2, Percent = 10 },
                new Tier { Threshold = 3, Percent = 20 },
            ],
        });

        Assert.Equal(60m, Evaluate(cart, campaign).LineDiscount);
    }

    [Fact]
    public void Free_shipping_with_threshold_and_upsell_hint()
    {
        var campaign = Campaign("SHIP", new FreeShippingReward(), c =>
        {
            c.Conditions = [new MinSubtotalCondition { Amount = 500 }];
            c.DisplayMessage = "Free shipping over 500";
        });

        var below = Cart(Line("1", 450));
        below.ShippingAmount = 39.90m;
        var belowResult = Evaluate(below, campaign);
        Assert.Equal(0m, belowResult.ShippingDiscount);
        Assert.Equal(50m, Assert.Single(belowResult.Hints).MissingAmount);

        var above = Cart(Line("1", 550));
        above.ShippingAmount = 39.90m;
        var aboveResult = Evaluate(above, campaign);
        Assert.Equal(39.90m, aboveResult.ShippingDiscount);
        Assert.Equal(550m, aboveResult.Total);
    }

    [Fact]
    public void Gift_product_is_reported()
    {
        var cart = Cart(Line("1", 1000));
        var campaign = Campaign("GIFT", new GiftProductReward { Sku = "TOTE-BAG", Quantity = 1 });

        var result = Evaluate(cart, campaign);

        var gift = Assert.Single(result.Gifts);
        Assert.Equal("TOTE-BAG", gift.Sku);
        Assert.Equal("GIFT", Assert.Single(result.AppliedCampaigns).Code);
    }
}
