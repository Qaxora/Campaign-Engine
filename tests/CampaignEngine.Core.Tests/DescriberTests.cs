using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Products;
using CampaignEngine.Core.Rules;
using CampaignEngine.Core.Rules.Conditions;
using CampaignEngine.Core.Rules.Rewards;
using static CampaignEngine.Core.Tests.TestData;

namespace CampaignEngine.Core.Tests;

public sealed class DescriberTests
{
    [Fact]
    public void Describes_a_typical_campaign_in_plain_english()
    {
        var campaign = Campaign("OCT-20", new PercentageDiscountReward { Percent = 20, MaxDiscount = 500 }, c =>
        {
            c.Currency = "TRY";
            c.Target = new ProductSelector { ExcludeCategories = ["electronics"] };
            c.Conditions = [new MinSubtotalCondition { Amount = 1000 }];
            c.Channels = ["store", "web"];
            c.Schedule = new Schedule
            {
                StartsAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(3)),
                EndsAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(3)),
                TimeZone = "Europe/Istanbul",
                DaysOfWeek = [DayOfWeek.Sunday, DayOfWeek.Saturday],
            };
            c.Limits = new CampaignLimits { MaxRedemptionsPerCustomer = 1, Budget = 50_000 };
        });

        var description = CampaignDescriber.Describe(campaign);

        Assert.Equal("20% off (at most 500 TRY) when the qualifying items total at least 1,000 TRY.", description.Summary);
        Assert.Equal(["All products", "Except categories electronics", "Never discounts products on global exclusion lists"], description.Products);
        Assert.Equal(["Channels: store, web", "All stores", "Every customer", "Only for carts in TRY"], description.Audience);
        Assert.Equal(["Oct 1, 2026 00:00 – Nov 1, 2026 00:00", "On Saturday, Sunday", "Time zone: Europe/Istanbul"], description.Schedule);
        Assert.Equal(["At most once per customer", "Budget 50,000 TRY"], description.Limits);
        Assert.StartsWith("Stackable", description.Combination, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Rewards))]
    public void Every_reward_type_has_a_description(Reward reward, string expected)
    {
        Assert.Equal(expected, CampaignDescriber.DescribeReward(reward, a => a.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
    }

    public static TheoryData<Reward, string> Rewards() => new()
    {
        { new AmountDiscountReward { Amount = 100 }, "100 off the order" },
        { new AmountDiscountReward { Amount = 5, PerUnit = true }, "5 off each unit" },
        { new FixedUnitPriceReward { Price = 99.9m }, "every unit for 99.9" },
        { new BuyXGetYReward { BuyQuantity = 2, GetQuantity = 1 }, "3 for 2" },
        { new BuyXGetYReward { BuyQuantity = 1, GetQuantity = 1, DiscountPercent = 50, MaxApplications = 2 }, "buy 1, get 1 50% off, up to twice per order" },
        { new BuyXGetYReward { BuyQuantity = 1, GetQuantity = 1, GetProducts = new ProductSelector { Categories = ["socks"] } }, "buy 1, get 1 free from categories socks" },
        { new BundlePriceReward { Quantity = 3, Price = 100 }, "any 3 for 100" },
        { new TieredDiscountReward { Tiers = [new Tier { Threshold = 1000, Percent = 10 }, new Tier { Threshold = 500, Amount = 25 }] }, "tiered discount: 500+ → 25 off; 1000+ → 10% off" },
        { new FreeShippingReward(), "free shipping" },
        { new GiftProductReward { Sku = "TOTE-1" }, "a free gift (1 × TOTE-1)" },
    };

    [Fact]
    public void Describes_nested_and_context_conditions()
    {
        Condition condition = new AnyOfCondition
        {
            Conditions =
            [
                new FirstOrderCondition(),
                new AllOfCondition
                {
                    Conditions =
                    [
                        new PaymentCondition { BankCodes = ["0062", "0046"], MinInstallments = 3 },
                        new NotCondition { Condition = new CartAttributeCondition { Key = "deliveryType", Values = ["clickAndCollect"] } },
                    ],
                },
            ],
        };

        Assert.Equal(
            "any of: it is the customer's first order; all of: the order is with a card of 0062 or 0046, in at least 3 installments; not (deliveryType is clickAndCollect)",
            CampaignDescriber.DescribeCondition(condition, a => $"{a}"));
    }

    [Fact]
    public void Describes_audience_coupons_and_exclusive_groups()
    {
        var campaign = Campaign("VIP", new PercentageDiscountReward { Percent = 15 }, c =>
        {
            c.Target = new ProductSelector { Brands = ["Acme"], Categories = ["shoes"] };
            c.Stores = new StoreScope { Include = ["IST-1"], Exclude = ["IST-9"] };
            c.CustomerSegments = ["vip"];
            c.Coupon = new CouponRule { Codes = ["VIP15"], MaxUsesPerCode = 1 };
            c.Stacking = StackingMode.Exclusive;
            c.ExclusivityGroup = "seasonal";
            c.Priority = 10;
        });

        var description = CampaignDescriber.Describe(campaign);

        Assert.Equal("15% off on categories shoes, or brands Acme with a coupon.", description.Summary);
        Assert.Contains("Coupon required: VIP15 (each usable once)", description.Audience);
        Assert.Contains("Except stores: IST-9", description.Audience);
        Assert.Contains("Customer segments: vip", description.Audience);
        Assert.Equal("Exclusive: never combined with other campaigns; the better offer for the customer wins; only one campaign of group 'seasonal' applies. Priority 10.", description.Combination);
    }
}
