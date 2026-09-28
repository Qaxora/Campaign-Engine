using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Products;

namespace CampaignEngine.Core.Conflicts;

/// <summary>Static intersection tests between parts of two campaign definitions.</summary>
internal static class Overlap
{
    private const int MinutesPerDay = 24 * 60;

    /// <summary>Two filter lists where "empty" means "everything".</summary>
    public static bool Lists(List<string> a, List<string> b) =>
        a.Count == 0 || b.Count == 0 || a.Intersect(b, StringComparer.OrdinalIgnoreCase).Any();

    public static bool Stores(StoreScope a, StoreScope b)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        bool Allowed(string store) => !a.Exclude.Contains(store, comparer) && !b.Exclude.Contains(store, comparer);

        return (a.Include.Count, b.Include.Count) switch
        {
            (0, 0) => true,
            (_, 0) => a.Include.Any(Allowed),
            (0, _) => b.Include.Any(Allowed),
            _ => a.Include.Intersect(b.Include, comparer).Any(Allowed),
        };
    }

    public static bool Schedules(Schedule a, Schedule b)
    {
        var aStart = a.StartsAt ?? DateTimeOffset.MinValue;
        var bStart = b.StartsAt ?? DateTimeOffset.MinValue;
        var aEnd = a.EndsAt ?? DateTimeOffset.MaxValue;
        var bEnd = b.EndsAt ?? DateTimeOffset.MaxValue;
        if (aStart >= bEnd || bStart >= aEnd)
        {
            return false;
        }

        // Weekly/daily windows can only be compared reliably in the same time zone.
        if (!string.Equals(a.TimeZone, b.TimeZone, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (a.DaysOfWeek.Count > 0 && b.DaysOfWeek.Count > 0 && !a.DaysOfWeek.Intersect(b.DaysOfWeek).Any())
        {
            return false;
        }

        return Windows(a).Any(wa => Windows(b).Any(wb => wa.Start < wb.End && wb.Start < wa.End));
    }

    private static IEnumerable<(int Start, int End)> Windows(Schedule schedule)
    {
        var start = Minutes(schedule.DailyStart) ?? 0;
        var end = Minutes(schedule.DailyEnd) ?? MinutesPerDay;
        if (start < end)
        {
            yield return (start, end);
        }
        else
        {
            yield return (start, MinutesPerDay);
            yield return (0, end);
        }
    }

    private static int? Minutes(TimeOnly? time) => time is { } t ? (t.Hour * 60) + t.Minute : null;

    /// <summary>Can a single product be matched by both selectors?</summary>
    public static OverlapCertainty Products(ProductSelector a, ProductSelector b, IProductListLookup lists)
    {
        if (IsSkuOnly(a))
        {
            return a.Skus.Select(sku => SkuMatch(sku, b, lists)).DefaultIfEmpty(OverlapCertainty.None).Max();
        }

        if (IsSkuOnly(b))
        {
            return b.Skus.Select(sku => SkuMatch(sku, a, lists)).DefaultIfEmpty(OverlapCertainty.None).Max();
        }

        if (!a.HasIncludeCriteria || !b.HasIncludeCriteria)
        {
            return a.HasExcludeCriteria || b.HasExcludeCriteria ? OverlapCertainty.Possible : OverlapCertainty.Certain;
        }

        var comparer = StringComparer.OrdinalIgnoreCase;
        var shared = a.Categories.Intersect(b.Categories, comparer).Any()
                     || a.Brands.Intersect(b.Brands, comparer).Any()
                     || a.ProductLists.Intersect(b.ProductLists, comparer).Any()
                     || a.Attributes.Any(pair => b.Attributes.TryGetValue(pair.Key, out var values) && pair.Value.Intersect(values, comparer).Any());
        if (shared)
        {
            return OverlapCertainty.Certain;
        }

        // A product has exactly one brand: two brand-only selectors without a common brand cannot meet.
        if (IsBrandOnly(a) && IsBrandOnly(b))
        {
            return OverlapCertainty.None;
        }

        return OverlapCertainty.Possible;
    }

    private static OverlapCertainty SkuMatch(string sku, ProductSelector selector, IProductListLookup lists)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        if (selector.ExcludeSkus.Contains(sku, comparer) || selector.ExcludeProductLists.Exists(l => lists.Contains(l, sku)))
        {
            return OverlapCertainty.None;
        }

        if (selector.Skus.Contains(sku, comparer) || selector.ProductLists.Exists(l => lists.Contains(l, sku)))
        {
            return selector.HasExcludeCriteria ? OverlapCertainty.Possible : OverlapCertainty.Certain;
        }

        if (!selector.HasIncludeCriteria)
        {
            return selector.HasExcludeCriteria ? OverlapCertainty.Possible : OverlapCertainty.Certain;
        }

        // Only SKUs / lists in the other selector and none matched: disjoint. Otherwise we cannot know.
        return selector.Categories.Count == 0 && selector.Brands.Count == 0 && selector.Attributes.Count == 0
            ? OverlapCertainty.None
            : OverlapCertainty.Possible;
    }

    private static bool IsSkuOnly(ProductSelector s) =>
        s.Skus.Count > 0 && s.Categories.Count == 0 && s.Brands.Count == 0 && s.ProductLists.Count == 0 && s.Attributes.Count == 0;

    private static bool IsBrandOnly(ProductSelector s) =>
        s.Brands.Count > 0 && s.Skus.Count == 0 && s.Categories.Count == 0 && s.ProductLists.Count == 0 && s.Attributes.Count == 0;
}
