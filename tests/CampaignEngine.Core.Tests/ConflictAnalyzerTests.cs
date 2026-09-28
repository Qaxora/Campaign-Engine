using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Conflicts;
using CampaignEngine.Core.Products;
using CampaignEngine.Core.Rules.Rewards;
using static CampaignEngine.Core.Tests.TestData;

namespace CampaignEngine.Core.Tests;

public class ConflictAnalyzerTests
{
    private static readonly ConflictAnalyzer Analyzer = new();

    private static Campaign Percent(string code, decimal percent, Action<Campaign>? configure = null) =>
        Campaign(code, new PercentageDiscountReward { Percent = percent }, configure);

    [Fact]
    public void Store_and_web_campaigns_on_the_same_products_are_reported()
    {
        // The classic case: the store team and the e-commerce team both run a campaign on t-shirts.
        var store = Percent("STORE-TSHIRT", 30, c => c.Target.Categories = ["tshirt"]);
        var web = Percent("WEB-TSHIRT", 40, c => c.Target.Categories = ["tshirt"]);

        var conflict = Assert.Single(Analyzer.Analyze(store, [web]));

        Assert.Equal(ConflictKind.StackedDiscount, conflict.Kind);
        Assert.Equal(ConflictSeverity.Warning, conflict.Severity);
        Assert.Equal(OverlapCertainty.Certain, conflict.Certainty);
        Assert.Equal(0.58m, conflict.CombinedRateEstimate);
    }

    [Fact]
    public void Separate_channels_do_not_conflict()
    {
        var store = Percent("STORE", 30, c => c.Channels = ["store"]);
        var web = Percent("WEB", 40, c => c.Channels = ["web", "mobile"]);

        Assert.Empty(Analyzer.Analyze(store, [web]));
    }

    [Fact]
    public void Non_overlapping_dates_do_not_conflict()
    {
        var june = Percent("JUNE", 30, c => c.Schedule = new Schedule { StartsAt = Now, EndsAt = Now.AddDays(30) });
        var july = Percent("JULY", 30, c => c.Schedule = new Schedule { StartsAt = Now.AddDays(30), EndsAt = Now.AddDays(60) });

        Assert.Empty(Analyzer.Analyze(june, [july]));
    }

    [Fact]
    public void Disjoint_daily_windows_do_not_conflict()
    {
        var morning = Percent("MORNING", 30, c => c.Schedule = new Schedule { DailyStart = new(8, 0), DailyEnd = new(12, 0) });
        var night = Percent("NIGHT", 30, c => c.Schedule = new Schedule { DailyStart = new(22, 0), DailyEnd = new(2, 0) });
        var lateMorning = Percent("LATE", 30, c => c.Schedule = new Schedule { DailyStart = new(11, 0), DailyEnd = new(13, 0) });

        Assert.Empty(Analyzer.Analyze(morning, [night]));
        Assert.Single(Analyzer.Analyze(morning, [lateMorning]));
    }

    [Fact]
    public void Different_weekdays_do_not_conflict()
    {
        var weekend = Percent("WEEKEND", 30, c => c.Schedule.DaysOfWeek = [DayOfWeek.Saturday, DayOfWeek.Sunday]);
        var monday = Percent("MONDAY", 30, c => c.Schedule.DaysOfWeek = [DayOfWeek.Monday]);

        Assert.Empty(Analyzer.Analyze(weekend, [monday]));
    }

    [Fact]
    public void Disjoint_sku_scopes_and_exclusions_do_not_conflict()
    {
        var a = Percent("A", 10, c => c.Target.Skus = ["S1", "S2"]);
        var b = Percent("B", 10, c => c.Target.Skus = ["S3"]);
        var everythingButS1S2 = Percent("C", 10, c => c.Target.ExcludeSkus = ["S1", "S2"]);

        Assert.Empty(Analyzer.Analyze(a, [b, everythingButS1S2]));
    }

    [Fact]
    public void Sku_in_referenced_product_list_is_a_certain_overlap()
    {
        var lists = new ProductListIndex([new ProductList { Code = "SUMMER", Name = "Summer", Skus = ["S1"] }]);
        var a = Percent("A", 10, c => c.Target.Skus = ["S1"]);
        var b = Percent("B", 10, c => c.Target.ProductLists = ["SUMMER"]);

        Assert.Equal(OverlapCertainty.Certain, Assert.Single(Analyzer.Analyze(a, [b], lists)).Certainty);
    }

    [Fact]
    public void Different_dimensions_are_a_possible_overlap()
    {
        var byBrand = Percent("BRAND", 10, c => c.Target.Brands = ["acme"]);
        var byCategory = Percent("CATEGORY", 10, c => c.Target.Categories = ["shoes"]);
        var otherBrand = Percent("OTHER-BRAND", 10, c => c.Target.Brands = ["globex"]);

        Assert.Equal(OverlapCertainty.Possible, Assert.Single(Analyzer.Analyze(byBrand, [byCategory])).Certainty);
        Assert.Empty(Analyzer.Analyze(byBrand, [otherBrand]));
    }

    [Fact]
    public void Duplicate_coupon_codes_are_errors()
    {
        var a = Percent("A", 10, c => c.Coupon = new CouponRule { Codes = ["WELCOME"] });
        var b = Percent("B", 10, c =>
        {
            c.Coupon = new CouponRule { Codes = ["welcome"] };
            c.Target.Skus = ["UNRELATED"];
        });

        Assert.Contains(Analyzer.Analyze(a, [b]), x => x.Kind == ConflictKind.DuplicateCoupon && x.Severity == ConflictSeverity.Error);
    }

    [Fact]
    public void Exclusive_overlap_with_equal_priority_warns()
    {
        var a = Percent("A", 10, c => c.Stacking = StackingMode.Exclusive);
        var b = Percent("B", 20);

        var conflicts = Analyzer.Analyze(a, [b]);

        Assert.Contains(conflicts, c => c.Kind == ConflictKind.ExclusiveOverlap);
        Assert.Contains(conflicts, c => c.Kind == ConflictKind.SamePriority && c.Severity == ConflictSeverity.Warning);
    }

    [Fact]
    public void Same_exclusivity_group_is_informational()
    {
        var a = Percent("A", 10, c => c.ExclusivityGroup = "bank");
        var b = Percent("B", 20, c => c.ExclusivityGroup = "BANK");

        var conflict = Assert.Single(Analyzer.Analyze(a, [b]));

        Assert.Equal(ConflictKind.SameGroup, conflict.Kind);
        Assert.Equal(ConflictSeverity.Info, conflict.Severity);
    }

    [Fact]
    public void Store_scopes_are_compared()
    {
        var istanbul = Percent("IST", 10, c => c.Stores.Include = ["IST-001", "IST-002"]);
        var ankara = Percent("ANK", 10, c => c.Stores.Include = ["ANK-001"]);
        var allButIstanbul = Percent("NOT-IST", 10, c => c.Stores.Exclude = ["IST-001", "IST-002"]);

        Assert.Empty(Analyzer.Analyze(istanbul, [ankara, allButIstanbul]));
    }

    [Fact]
    public void Analyze_all_reports_each_pair_once()
    {
        var campaigns = new[] { Percent("A", 10), Percent("B", 10), Percent("C", 10) };

        Assert.Equal(3, Analyzer.AnalyzeAll(campaigns).Count);
    }
}
