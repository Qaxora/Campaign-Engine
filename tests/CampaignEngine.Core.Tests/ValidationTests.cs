using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Rules;
using CampaignEngine.Core.Rules.Conditions;
using CampaignEngine.Core.Rules.Rewards;
using static CampaignEngine.Core.Tests.TestData;

namespace CampaignEngine.Core.Tests;

public class ValidationTests
{
    [Fact]
    public void Valid_campaign_has_no_errors()
    {
        var campaign = Campaign("OK-1", new PercentageDiscountReward { Percent = 10 });

        Assert.Empty(CampaignValidator.Validate(campaign));
    }

    [Fact]
    public void Structural_errors_are_reported_with_paths()
    {
        var campaign = Campaign("bad code!", new BuyXGetYReward { BuyQuantity = 0, GetQuantity = 1 }, c =>
        {
            c.Name = "";
            c.Schedule = new Schedule { StartsAt = Now, EndsAt = Now, TimeZone = "Mars/Olympus" };
            c.Limits.Budget = 0;
            c.Coupon = new CouponRule { Codes = ["A", "a"] };
            c.Conditions = [new AnyOfCondition(), new MinSubtotalCondition { Amount = -1 }];
        });

        var errors = CampaignValidator.Validate(campaign);

        Assert.Contains(errors, e => e.StartsWith("code", StringComparison.Ordinal));
        Assert.Contains("name is required.", errors);
        Assert.Contains("schedule.endsAt must be after schedule.startsAt.", errors);
        Assert.Contains(errors, e => e.StartsWith("schedule.timeZone", StringComparison.Ordinal));
        Assert.Contains("limits.budget must be greater than zero.", errors);
        Assert.Contains("coupon.codes must be unique (case-insensitive).", errors);
        Assert.Contains("reward.buyQuantity must be at least 1.", errors);
        Assert.Contains("conditions[0].conditions must not be empty.", errors);
        Assert.Contains("conditions[1].amount must be greater than zero.", errors);
    }

    [Theory]
    [MemberData(nameof(InvalidRewards))]
    public void Invalid_rewards_are_rejected(Reward reward) =>
        Assert.NotEmpty(CampaignValidator.Validate(Campaign("R", reward)));

    public static TheoryData<Reward> InvalidRewards() =>
    [
        new PercentageDiscountReward { Percent = 0 },
        new PercentageDiscountReward { Percent = 120 },
        new AmountDiscountReward { Amount = 0 },
        new FixedUnitPriceReward { Price = -1 },
        new BundlePriceReward { Quantity = 1, Price = 10 },
        new TieredDiscountReward(),
        new TieredDiscountReward { Tiers = [new Tier { Threshold = 100, Percent = 10, Amount = 5 }] },
        new GiftProductReward(),
        new FreeShippingReward { MaxAmount = 0 },
    ];
}
