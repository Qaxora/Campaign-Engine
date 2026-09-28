using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Carts;
using CampaignEngine.Core.Evaluation;
using CampaignEngine.Core.Products;
using CampaignEngine.Core.Rules.Conditions;
using CampaignEngine.Core.Rules.Rewards;
using static CampaignEngine.Core.Tests.TestData;

namespace CampaignEngine.Core.Tests;

public class EvaluatorTests
{
    private static PercentageDiscountReward Percent(decimal value) => new() { Percent = value };

    [Fact]
    public void Stackable_percentages_compound_on_what_is_left()
    {
        var cart = Cart(Line("1", 100));
        var result = Evaluate(cart,
            Campaign("A", Percent(10), c => c.Priority = 2),
            Campaign("B", Percent(20), c => c.Priority = 1));

        // 10% of 100 = 10, then 20% of 90 = 18
        Assert.Equal(28m, result.LineDiscount);
        Assert.Equal(["A", "B"], result.AppliedCampaigns.Select(a => a.Code));
        Assert.Equal([new LineDiscount("A", 10m), new LineDiscount("B", 18m)], result.Lines[0].Discounts);
    }

    [Fact]
    public void Exclusive_campaign_wins_when_it_is_better_for_the_customer()
    {
        var cart = Cart(Line("1", 100));
        var result = Evaluate(cart,
            Campaign("STACK-10", Percent(10)),
            Campaign("STACK-5", Percent(5)),
            Campaign("EXCL-40", Percent(40), c => c.Stacking = StackingMode.Exclusive));

        Assert.Equal(40m, result.LineDiscount);
        Assert.Equal("EXCL-40", Assert.Single(result.AppliedCampaigns).Code);
        Assert.Equal(RejectionReason.NotSelected, result.ReasonFor("STACK-10"));
    }

    [Fact]
    public void Stackable_combination_wins_when_it_is_better()
    {
        var cart = Cart(Line("1", 100));
        var result = Evaluate(cart,
            Campaign("STACK-30", Percent(30)),
            Campaign("STACK-20", Percent(20)),
            Campaign("EXCL-40", Percent(40), c => c.Stacking = StackingMode.Exclusive));

        Assert.Equal(44m, result.LineDiscount); // 30 + 20% of 70
        Assert.Equal(RejectionReason.NotSelected, result.ReasonFor("EXCL-40"));
    }

    [Fact]
    public void Highest_priority_selection_prefers_the_top_campaign_even_if_smaller()
    {
        var cart = Cart(Line("1", 100));
        var result = Evaluate(cart,
            [
                Campaign("STACK-30", Percent(30), c => c.Priority = 1),
                Campaign("EXCL-10", Percent(10), c => { c.Stacking = StackingMode.Exclusive; c.Priority = 9; }),
            ],
            options: new EngineOptions { Selection = PlanSelection.HighestPriority });

        Assert.Equal("EXCL-10", Assert.Single(result.AppliedCampaigns).Code);
    }

    [Fact]
    public void Only_the_best_campaign_of_an_exclusivity_group_applies()
    {
        var cart = Cart(Line("1", 100));
        var result = Evaluate(cart,
            Campaign("BANK-A", Percent(10), c => c.ExclusivityGroup = "bank"),
            Campaign("BANK-B", Percent(15), c => c.ExclusivityGroup = "bank"),
            Campaign("OTHER", new AmountDiscountReward { Amount = 5 }));

        Assert.Equal(["BANK-B", "OTHER"], result.AppliedCampaigns.Select(a => a.Code).Order());
        Assert.Equal(RejectionReason.LostInExclusivityGroup, result.ReasonFor("BANK-A"));
    }

    [Fact]
    public void Unit_offers_never_reuse_the_same_units()
    {
        var cart = Cart(Line("1", 100, quantity: 3));
        var result = Evaluate(cart,
            Campaign("3X2", new BuyXGetYReward { BuyQuantity = 2, GetQuantity = 1 }, c => c.Priority = 2),
            Campaign("2-FOR-150", new BundlePriceReward { Quantity = 2, Price = 150 }, c => c.Priority = 1));

        Assert.Equal(100m, result.LineDiscount);
        Assert.Equal(RejectionReason.NoDiscount, result.ReasonFor("2-FOR-150"));
    }

    [Fact]
    public void Percentage_after_unit_offer_applies_to_remaining_net()
    {
        var cart = Cart(Line("1", 100, quantity: 3));
        var result = Evaluate(cart,
            Campaign("3X2", new BuyXGetYReward { BuyQuantity = 2, GetQuantity = 1 }, c => c.Priority = 2),
            Campaign("TEN", Percent(10), c => c.Priority = 1));

        Assert.Equal(120m, result.LineDiscount); // 100 free + 10% of 200
    }

    [Fact]
    public void Price_floor_is_never_crossed()
    {
        var line = Line("1", 100);
        line.MinimumUnitPrice = 85;
        var result = Evaluate(Cart(line), Campaign("P", Percent(50)));

        Assert.Equal(15m, result.LineDiscount);
    }

    [Fact]
    public void Amount_discount_moves_to_other_lines_when_one_hits_its_floor()
    {
        var floored = Line("1", 100);
        floored.MinimumUnitPrice = 100;
        var result = Evaluate(Cart(floored, Line("2", 100)), Campaign("A", new AmountDiscountReward { Amount = 30 }));

        Assert.Equal(0m, result.DiscountOf("1"));
        Assert.Equal(30m, result.DiscountOf("2"));
    }

    [Fact]
    public void Globally_excluded_and_non_discountable_products_are_never_discounted()
    {
        var giftCard = Line("gc", 500, sku: "GIFTCARD");
        giftCard.Discountable = false;
        var cart = Cart(Line("1", 100), Line("tobacco", 100, sku: "TOBACCO-1"), giftCard);
        var lists = new[]
        {
            new ProductList { Code = "LEGAL-NO-DISCOUNT", Name = "Regulated", Kind = ProductListKind.GlobalExclusion, Skus = ["TOBACCO-1"] },
        };

        var result = Evaluate(cart, [Campaign("ALL10", Percent(10))], lists);

        Assert.Equal(10m, result.DiscountOf("1"));
        Assert.Equal(0m, result.DiscountOf("tobacco"));
        Assert.Equal(0m, result.DiscountOf("gc"));
    }

    [Fact]
    public void Global_exclusions_can_be_ignored_explicitly()
    {
        var cart = Cart(Line("1", 100, sku: "GOLD-1"));
        var lists = new[] { new ProductList { Code = "X", Name = "X", Kind = ProductListKind.GlobalExclusion, Skus = ["GOLD-1"] } };

        var result = Evaluate(cart, [Campaign("C", Percent(10), c => c.IgnoreGlobalExclusions = true)], lists);

        Assert.Equal(10m, result.LineDiscount);
    }

    [Fact]
    public void Product_lists_can_include_and_exclude()
    {
        var cart = Cart(Line("1", 100, sku: "A"), Line("2", 100, sku: "B"), Line("3", 100, sku: "C"));
        var lists = new[]
        {
            new ProductList { Code = "SUMMER", Name = "Summer", Skus = ["A", "B"] },
            new ProductList { Code = "NEW-IN", Name = "New in", Skus = ["B"] },
        };
        var campaign = Campaign("SUMMER10", Percent(10), c => c.Target = new ProductSelector
        {
            ProductLists = ["SUMMER"],
            ExcludeProductLists = ["NEW-IN"],
        });

        var result = Evaluate(cart, [campaign], lists);

        Assert.Equal([10m, 0m, 0m], result.Lines.Select(l => l.Discount));
    }

    [Fact]
    public void Campaign_outside_its_date_range_is_rejected()
    {
        var campaign = Campaign("OLD", Percent(10), c => c.Schedule = new Schedule { EndsAt = Now.AddDays(-1) });

        var result = Evaluate(Cart(Line("1", 100)), campaign);

        Assert.Empty(result.AppliedCampaigns);
        Assert.Equal(RejectionReason.OutsideSchedule, result.ReasonFor("OLD"));
    }

    [Theory]
    [InlineData(6, 30, true)]   // 09:30 Istanbul (UTC+3)
    [InlineData(5, 30, false)]  // 08:30 Istanbul
    [InlineData(21, 30, false)] // 00:30 Istanbul next day (Tuesday)
    public void Happy_hour_is_evaluated_in_the_campaign_time_zone(int utcHour, int utcMinute, bool expected)
    {
        var campaign = Campaign("HAPPY", Percent(10), c => c.Schedule = new Schedule
        {
            TimeZone = "Europe/Istanbul",
            DaysOfWeek = [DayOfWeek.Monday],
            DailyStart = new TimeOnly(9, 0),
            DailyEnd = new TimeOnly(12, 0),
        });
        var cart = Cart(Line("1", 100));
        cart.Timestamp = new DateTimeOffset(2026, 6, 15, utcHour, utcMinute, 0, TimeSpan.Zero);

        Assert.Equal(expected, Evaluate(cart, campaign).AppliedCampaigns.Count == 1);
    }

    [Fact]
    public void Daily_window_may_cross_midnight()
    {
        var schedule = new Schedule { DailyStart = new TimeOnly(22, 0), DailyEnd = new TimeOnly(2, 0) };

        Assert.True(schedule.IsActiveAt(new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.Zero)));
        Assert.True(schedule.IsActiveAt(new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero)));
        Assert.False(schedule.IsActiveAt(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Audience_filters_channel_store_and_segment()
    {
        var cart = Cart(Line("1", 100));
        cart.Customer = new CustomerInfo { Id = "C1", Segments = ["gold"] };

        var result = Evaluate(cart,
            Campaign("WEB", Percent(10), c => c.Channels = ["web"]),
            Campaign("NOT-IST", Percent(10), c => c.Stores.Exclude = ["IST-001"]),
            Campaign("STAFF", Percent(10), c => c.CustomerSegments = ["employee"]),
            Campaign("GOLD", Percent(10), c => c.CustomerSegments = ["gold"]));

        Assert.Equal("GOLD", Assert.Single(result.AppliedCampaigns).Code);
        Assert.Equal(RejectionReason.ChannelMismatch, result.ReasonFor("WEB"));
        Assert.Equal(RejectionReason.StoreMismatch, result.ReasonFor("NOT-IST"));
        Assert.Equal(RejectionReason.SegmentMismatch, result.ReasonFor("STAFF"));
    }

    [Fact]
    public void Coupon_campaigns_need_a_matching_code()
    {
        var campaign = Campaign("COUPON", Percent(10), c => c.Coupon = new CouponRule { Codes = ["WELCOME10"] });

        var without = Evaluate(Cart(Line("1", 100)), campaign);
        Assert.Equal(RejectionReason.CouponMissing, without.ReasonFor("COUPON"));

        var cart = Cart(Line("1", 100));
        cart.CouponCodes = ["welcome10", "BOGUS"];
        var with = Evaluate(cart, campaign);
        Assert.Equal("welcome10", Assert.Single(with.AppliedCampaigns).CouponCode);
        Assert.Equal(["BOGUS"], with.UnusedCouponCodes);
    }

    [Fact]
    public void Single_use_coupon_is_rejected_once_used()
    {
        var campaign = Campaign("ONCE", Percent(10), c => c.Coupon = new CouponRule { Codes = ["X1"], MaxUsesPerCode = 1 });
        var cart = Cart(Line("1", 100));
        cart.CouponCodes = ["X1"];
        var usage = new UsageSnapshot(new()
        {
            [campaign.Id] = new CampaignUsage { CouponRedemptions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["x1"] = 1 } },
        });

        var result = Evaluate(cart, [campaign], usage: usage);

        Assert.Equal(RejectionReason.CouponExhausted, result.ReasonFor("ONCE"));
    }

    [Fact]
    public void Usage_limits_are_enforced()
    {
        var total = Campaign("TOTAL", Percent(10), c => c.Limits.MaxRedemptions = 100);
        var perCustomer = Campaign("PER-CUSTOMER", Percent(10), c => c.Limits.MaxRedemptionsPerCustomer = 1);
        var cart = Cart(Line("1", 100));
        cart.Customer = new CustomerInfo { Id = "C1" };
        var usage = new UsageSnapshot(new()
        {
            [total.Id] = new CampaignUsage { Redemptions = 100 },
            [perCustomer.Id] = new CampaignUsage { Redemptions = 5, CustomerRedemptions = 1 },
        });

        var result = Evaluate(cart, [total, perCustomer], usage: usage);

        Assert.Equal(RejectionReason.UsageLimitReached, result.ReasonFor("TOTAL"));
        Assert.Equal(RejectionReason.CustomerLimitReached, result.ReasonFor("PER-CUSTOMER"));
    }

    [Fact]
    public void Remaining_budget_caps_the_discount()
    {
        var campaign = Campaign("BUDGET", Percent(50), c => c.Limits.Budget = 1000);
        var usage = new UsageSnapshot(new() { [campaign.Id] = new CampaignUsage { DiscountTotal = 970 } });

        var result = Evaluate(Cart(Line("1", 100)), [campaign], usage: usage);

        Assert.Equal(30m, result.LineDiscount);
    }

    [Fact]
    public void Exhausted_budget_rejects_the_campaign()
    {
        var campaign = Campaign("BUDGET", Percent(50), c => c.Limits.Budget = 1000);
        var usage = new UsageSnapshot(new() { [campaign.Id] = new CampaignUsage { DiscountTotal = 1000 } });

        var result = Evaluate(Cart(Line("1", 100)), [campaign], usage: usage);

        Assert.Equal(RejectionReason.BudgetExhausted, result.ReasonFor("BUDGET"));
    }

    [Fact]
    public void Max_discount_per_order_caps_the_discount()
    {
        var campaign = Campaign("CAP", Percent(50), c => c.Limits.MaxDiscountPerOrder = 25);

        Assert.Equal(25m, Evaluate(Cart(Line("1", 100), Line("2", 100)), campaign).LineDiscount);
    }

    [Fact]
    public void Bank_card_campaign_matches_bin_prefix_and_installments()
    {
        var campaign = Campaign("BANK", Percent(10), c => c.Conditions =
        [
            new PaymentCondition { CardBins = ["454360"], MaxInstallments = 3 },
        ]);
        var cart = Cart(Line("1", 100));
        cart.Payment = new PaymentInfo { Method = "creditCard", CardBin = "45436012", Installments = 3 };

        Assert.Single(Evaluate(cart, campaign).AppliedCampaigns);

        cart.Payment.Installments = 6;
        Assert.Equal(RejectionReason.ConditionNotMet, Evaluate(cart, campaign).ReasonFor("BANK"));
    }

    [Fact]
    public void Composite_conditions_combine()
    {
        var campaign = Campaign("MIX", Percent(10), c => c.Conditions =
        [
            new AnyOfCondition
            {
                Conditions =
                [
                    new MinSubtotalCondition { Amount = 1000 },
                    new CartAttributeCondition { Key = "deliveryType", Values = ["clickAndCollect"] },
                ],
            },
            new NotCondition { Condition = new FirstOrderCondition() },
        ]);
        var cart = Cart(Line("1", 100));
        cart.Attributes["deliveryType"] = "clickAndCollect";

        Assert.Single(Evaluate(cart, campaign).AppliedCampaigns);

        cart.Customer = new CustomerInfo { IsFirstOrder = true };
        Assert.Empty(Evaluate(cart, campaign).AppliedCampaigns);
    }

    [Fact]
    public void Threshold_counts_only_qualifying_lines_by_default()
    {
        var campaign = Campaign("TSHIRT-500", Percent(10), c =>
        {
            c.Target = new ProductSelector { Categories = ["tshirt"] };
            c.Conditions = [new MinSubtotalCondition { Amount = 500 }];
        });
        var cart = Cart(Line("1", 300, category: "tshirt"), Line("2", 400, category: "jeans"));

        var result = Evaluate(cart, campaign);

        Assert.Equal(RejectionReason.ConditionNotMet, result.ReasonFor("TSHIRT-500"));
        Assert.Equal(200m, Assert.Single(result.Hints).MissingAmount);
    }

    [Fact]
    public void Draft_and_paused_campaigns_do_not_apply()
    {
        var result = Evaluate(Cart(Line("1", 100)),
            Campaign("DRAFT", Percent(10), c => c.Status = CampaignStatus.Draft),
            Campaign("PAUSED", Percent(10), c => c.Status = CampaignStatus.Paused));

        Assert.Empty(result.AppliedCampaigns);
        Assert.All(result.Rejections!, r => Assert.Equal(RejectionReason.NotActive, r.Reason));
    }

    [Fact]
    public void Currency_restricted_campaign_ignores_other_currencies()
    {
        var cart = Cart(Line("1", 100));
        cart.Currency = "EUR";

        var result = Evaluate(cart, Campaign("TRY-ONLY", Percent(10), c => c.Currency = "TRY"));

        Assert.Equal(RejectionReason.CurrencyMismatch, result.ReasonFor("TRY-ONLY"));
    }

    [Fact]
    public void Invalid_cart_is_rejected_with_all_errors()
    {
        var cart = Cart(Line("1", -5, quantity: 0), Line("1", 10));

        var ex = Assert.Throws<CartValidationException>(() => Evaluate(cart));

        Assert.Equal(3, ex.Errors.Count);
    }

    [Fact]
    public void Evaluation_is_deterministic()
    {
        var campaigns = new[]
        {
            Campaign("A", Percent(7)),
            Campaign("B", new BuyXGetYReward { BuyQuantity = 2, GetQuantity = 1 }),
            Campaign("C", new AmountDiscountReward { Amount = 13 }),
        };
        var cart = Cart(Line("1", 33.33m, quantity: 4), Line("2", 19.99m, quantity: 2), Line("3", 7.49m));

        var first = Evaluate(cart, campaigns);
        var second = Evaluate(cart, campaigns.Reverse());

        Assert.Equal(first.Lines.Select(l => l.Discount), second.Lines.Select(l => l.Discount));
        Assert.Equal(first.LineDiscount, first.AppliedCampaigns.Sum(a => a.LineDiscount));
    }

    [Fact]
    public void Line_allocations_always_add_up_to_campaign_totals()
    {
        var cart = Cart(Line("1", 12.99m, quantity: 3), Line("2", 7.45m, quantity: 7), Line("3", 101.10m));
        var result = Evaluate(cart,
            Campaign("P", Percent(13)),
            Campaign("A", new AmountDiscountReward { Amount = 17.77m }));

        foreach (var applied in result.AppliedCampaigns)
        {
            Assert.Equal(applied.LineDiscount, applied.Lines.Sum(l => l.Discount));
        }

        Assert.Equal(result.LineDiscount, result.Lines.Sum(l => l.Discount));
        Assert.All(result.Lines, l => Assert.Equal(l.Discount, l.Discounts.Sum(d => d.Amount)));
    }
}
