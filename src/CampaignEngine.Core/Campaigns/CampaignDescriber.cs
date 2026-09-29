using System.Globalization;
using CampaignEngine.Core.Products;
using CampaignEngine.Core.Rules;
using CampaignEngine.Core.Rules.Conditions;
using CampaignEngine.Core.Rules.Rewards;

namespace CampaignEngine.Core.Campaigns;

/// <summary>A campaign definition in plain English, section by section.</summary>
public sealed record CampaignDescription(
    string Summary,
    string Reward,
    IReadOnlyList<string> Conditions,
    IReadOnlyList<string> Products,
    IReadOnlyList<string> Audience,
    IReadOnlyList<string> Schedule,
    IReadOnlyList<string> Limits,
    string Combination);

/// <summary>
/// Deterministic, template-based description of a campaign. It lives next to the rules so that every
/// client (web app, AI assistant fallback, e-mails) shows the same wording and none of them has to
/// know what a rule means.
/// </summary>
public static class CampaignDescriber
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static CampaignDescription Describe(Campaign campaign)
    {
        var money = MoneyFormatter(campaign.Currency);
        var reward = DescribeReward(campaign.Reward, money);
        var conditions = campaign.Conditions.Select(c => DescribeCondition(c, money)).ToList();
        var products = DescribeProducts(campaign.Target, campaign.IgnoreGlobalExclusions);
        var audience = DescribeAudience(campaign);
        var schedule = DescribeSchedule(campaign.Schedule);
        var limits = DescribeLimits(campaign.Limits, money);

        var summary = new List<string> { Capitalize(reward) };
        if (products.Count > 0 && !products[0].StartsWith("All products", StringComparison.Ordinal))
        {
            summary.Add("on " + Uncapitalize(products[0]));
        }

        if (conditions.Count > 0)
        {
            summary.Add("when " + string.Join(" and ", conditions.Select(Uncapitalize)));
        }

        if (campaign.Coupon is { Codes.Count: > 0 })
        {
            summary.Add("with a coupon");
        }

        return new CampaignDescription(
            string.Join(' ', summary) + ".",
            Capitalize(reward) + ".",
            conditions,
            products,
            audience,
            schedule,
            limits,
            DescribeCombination(campaign));
    }

    public static string DescribeReward(Reward reward, Func<decimal, string> money) => reward switch
    {
        PercentageDiscountReward r => $"{Number(r.Percent)}% off" + (r.MaxDiscount is { } max ? $" (at most {money(max)})" : ""),
        AmountDiscountReward { PerUnit: true } r => $"{money(r.Amount)} off each unit",
        AmountDiscountReward r => $"{money(r.Amount)} off the order",
        FixedUnitPriceReward r => $"every unit for {money(r.Price)}",
        BuyXGetYReward r => DescribeBuyXGetY(r),
        BundlePriceReward r => $"any {r.Quantity} for {money(r.Price)}" + Applications(r.MaxApplications),
        TieredDiscountReward r => "tiered discount: " + string.Join("; ", r.Tiers.OrderBy(t => t.Threshold).Select(t =>
            $"{(r.Basis == TierBasis.Quantity ? $"{Number(t.Threshold)}+ items" : $"{money(t.Threshold)}+")} → " +
            (t.Percent is { } p ? $"{Number(p)}% off" : $"{money(t.Amount ?? 0)} off"))),
        FreeShippingReward r => "free shipping" + (r.MaxAmount is { } max ? $" (up to {money(max)})" : ""),
        GiftProductReward r => $"a free gift ({Number(r.Quantity)} × {r.Sku})",
        _ => reward.GetType().Name,
    };

    public static string DescribeCondition(Condition condition, Func<decimal, string> money) => condition switch
    {
        MinSubtotalCondition c => $"the {(c.Products is null ? "qualifying items" : "selected items")} total at least {money(c.Amount)}" + Scope(c.Products),
        MinQuantityCondition c => $"at least {Number(c.Quantity)} {(c.Products is null ? "qualifying items are" : "selected items are")} in the cart" + Scope(c.Products),
        FirstOrderCondition => "it is the customer's first order",
        PaymentCondition c => DescribePayment(c),
        CartAttributeCondition c => $"{c.Key} is {OneOf(c.Values)}",
        AllOfCondition c => "all of: " + string.Join("; ", c.Conditions.Select(x => DescribeCondition(x, money))),
        AnyOfCondition c => "any of: " + string.Join("; ", c.Conditions.Select(x => DescribeCondition(x, money))),
        NotCondition { Condition: { } inner } => "not (" + DescribeCondition(inner, money) + ")",
        _ => condition.GetType().Name,
    };

    private static string DescribeBuyXGetY(BuyXGetYReward r)
    {
        var free = r.DiscountPercent >= 100 ? "free" : $"{Number(r.DiscountPercent)}% off";
        var what = r.GetProducts is null ? "" : " from " + Uncapitalize(DescribeProducts(r.GetProducts, false)[0]);
        if (r.GetProducts is null && r.DiscountPercent >= 100)
        {
            return $"{r.BuyQuantity + r.GetQuantity} for {r.BuyQuantity}" + Applications(r.MaxApplications);
        }

        return $"buy {r.BuyQuantity}, get {r.GetQuantity} {free}{what}" + Applications(r.MaxApplications);
    }

    private static string DescribePayment(PaymentCondition c)
    {
        var parts = new List<string>();
        if (c.Methods.Count > 0)
        {
            parts.Add($"paid by {OneOf(c.Methods)}");
        }

        if (c.BankCodes.Count > 0)
        {
            parts.Add($"with a card of {OneOf(c.BankCodes)}");
        }

        if (c.CardBins.Count > 0)
        {
            parts.Add($"with a card starting {OneOf(c.CardBins)}");
        }

        if (c.MinInstallments is { } min && c.MaxInstallments is { } max)
        {
            parts.Add($"in {min}–{max} installments");
        }
        else if (c.MinInstallments is { } minOnly)
        {
            parts.Add($"in at least {minOnly} installments");
        }
        else if (c.MaxInstallments is { } maxOnly)
        {
            parts.Add($"in at most {maxOnly} installments");
        }

        return parts.Count == 0 ? "any payment" : "the order is " + string.Join(", ", parts);
    }

    public static IReadOnlyList<string> DescribeProducts(ProductSelector selector, bool ignoreGlobalExclusions)
    {
        var include = new List<string>();
        Add(include, selector.Categories, "categories");
        Add(include, selector.Brands, "brands");
        Add(include, selector.Skus, "SKUs");
        Add(include, selector.ProductLists, "product lists");
        include.AddRange(selector.Attributes.Select(a => $"{a.Key} {OneOf(a.Value)}"));

        var lines = new List<string> { include.Count == 0 ? "All products" : Capitalize(string.Join(", or ", include)) };
        var exclude = new List<string>();
        Add(exclude, selector.ExcludeCategories, "categories");
        Add(exclude, selector.ExcludeBrands, "brands");
        Add(exclude, selector.ExcludeSkus, "SKUs");
        Add(exclude, selector.ExcludeProductLists, "product lists");
        if (exclude.Count > 0)
        {
            lines.Add("Except " + string.Join(", ", exclude));
        }

        lines.Add(ignoreGlobalExclusions
            ? "Also discounts products on global exclusion lists"
            : "Never discounts products on global exclusion lists");
        return lines;

        static void Add(List<string> target, List<string> values, string noun)
        {
            if (values.Count > 0)
            {
                target.Add($"{noun} {string.Join(", ", values)}");
            }
        }
    }

    private static List<string> DescribeAudience(Campaign campaign)
    {
        var lines = new List<string>
        {
            campaign.Channels.Count == 0 ? "All channels" : $"Channels: {string.Join(", ", campaign.Channels)}",
            campaign.Stores.Include.Count == 0 ? "All stores" : $"Stores: {string.Join(", ", campaign.Stores.Include)}",
        };
        if (campaign.Stores.Exclude.Count > 0)
        {
            lines.Add($"Except stores: {string.Join(", ", campaign.Stores.Exclude)}");
        }

        lines.Add(campaign.CustomerSegments.Count == 0 ? "Every customer" : $"Customer segments: {string.Join(", ", campaign.CustomerSegments)}");
        if (campaign.Coupon is { Codes.Count: > 0 } coupon)
        {
            var codes = coupon.Codes.Count <= 5 ? string.Join(", ", coupon.Codes) : $"{coupon.Codes.Count} codes";
            lines.Add($"Coupon required: {codes}" + (coupon.MaxUsesPerCode is { } uses ? $" (each usable {Times(uses)})" : ""));
        }

        if (campaign.Currency is { } currency)
        {
            lines.Add($"Only for carts in {currency}");
        }

        return lines;
    }

    private static List<string> DescribeSchedule(Schedule schedule)
    {
        var zone = schedule.TimeZone;
        var lines = new List<string>
        {
            (schedule.StartsAt, schedule.EndsAt) switch
            {
                (null, null) => "No start or end date",
                ({ } s, null) => $"From {Date(s)}, no end date",
                (null, { } e) => $"Until {Date(e)}",
                ({ } s, { } e) => $"{Date(s)} – {Date(e)}",
            },
        };
        if (schedule.DaysOfWeek.Count is > 0 and < 7)
        {
            lines.Add("On " + string.Join(", ", schedule.DaysOfWeek.OrderBy(d => ((int)d + 6) % 7).Select(d => d.ToString())));
        }

        if (schedule.DailyStart is { } start && schedule.DailyEnd is { } end)
        {
            lines.Add($"Daily {start:HH\\:mm}–{end:HH\\:mm}" + (end < start ? " (overnight)" : ""));
        }

        lines.Add($"Time zone: {zone}");
        return lines;

        static string Date(DateTimeOffset value) => value.ToString("MMM d, yyyy HH:mm", Invariant);
    }

    private static List<string> DescribeLimits(CampaignLimits limits, Func<decimal, string> money)
    {
        var lines = new List<string>();
        if (limits.MaxRedemptions is { } total)
        {
            lines.Add($"At most {Times(total)} in total");
        }

        if (limits.MaxRedemptionsPerCustomer is { } perCustomer)
        {
            lines.Add($"At most {Times(perCustomer)} per customer");
        }

        if (limits.Budget is { } budget)
        {
            lines.Add($"Budget {money(budget)}");
        }

        if (limits.MaxDiscountPerOrder is { } perOrder)
        {
            lines.Add($"At most {money(perOrder)} off per order");
        }

        return lines.Count == 0 ? ["No usage limits"] : lines;
    }

    private static string DescribeCombination(Campaign campaign)
    {
        var text = campaign.Stacking == StackingMode.Exclusive
            ? "Exclusive: never combined with other campaigns; the better offer for the customer wins"
            : "Stackable: combines with other stackable campaigns";
        if (campaign.ExclusivityGroup is { } group)
        {
            text += $"; only one campaign of group '{group}' applies";
        }

        return text + $". Priority {campaign.Priority}.";
    }

    private static Func<decimal, string> MoneyFormatter(string? currency) =>
        amount => currency is null ? Number(amount) : $"{Number(amount)} {currency}";

    private static string Number(decimal value) => value.ToString("#,0.##", Invariant);

    private static string Applications(int? max) => max is { } n ? $", up to {Times(n)} per order" : "";

    private static string Times(int n) => n == 1 ? "once" : n == 2 ? "twice" : $"{n} times";

    private static string OneOf(List<string> values) =>
        values.Count <= 1 ? string.Join("", values) : string.Join(", ", values.Take(values.Count - 1)) + " or " + values[^1];

    private static string Scope(ProductSelector? products) =>
        products is null ? "" : $" ({Uncapitalize(DescribeProducts(products, false)[0])})";

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Uncapitalize(string text) =>
        text.Length == 0 || text.StartsWith("SKU", StringComparison.Ordinal) ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
