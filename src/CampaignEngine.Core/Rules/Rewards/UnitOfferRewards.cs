using System.Text.Json.Serialization;
using CampaignEngine.Core.Products;

namespace CampaignEngine.Core.Rules.Rewards;

/// <summary>
/// "Buy X get Y": "3 for 2" is <c>buyQuantity: 2, getQuantity: 1</c>; "second item 50% off" is
/// <c>buyQuantity: 1, getQuantity: 1, discountPercent: 50</c>.
/// </summary>
/// <remarks>
/// Without <see cref="GetProducts"/>, units of the target are sorted from most to least expensive and
/// grouped in sets of X+Y; the cheapest Y of each set are discounted. With <see cref="GetProducts"/>
/// ("buy a shoe, get socks free"), the X units come from the target and the Y cheapest units from
/// <see cref="GetProducts"/>. Every unit used is locked for other unit-based offers.
/// </remarks>
public sealed class BuyXGetYReward : Reward
{
    public int BuyQuantity { get; set; }

    public int GetQuantity { get; set; }

    /// <summary>Discount on the "get" units; 100 means free.</summary>
    public decimal DiscountPercent { get; set; } = 100;

    /// <summary>Where the "get" units come from. Null means the same products as the target.</summary>
    public ProductSelector? GetProducts { get; set; }

    /// <summary>How many sets per cart at most. Null means unlimited.</summary>
    public int? MaxApplications { get; set; }

    public override void Apply(RewardContext context)
    {
        var allocation = GetProducts is null ? SamePool(context) : SeparatePools(context);
        allocation.Commit(context);
    }

    private UnitAllocation SamePool(RewardContext context)
    {
        var units = UnitPool.From(context.EligibleLines).OrderByDescending(u => u.UnitNet).ThenBy(u => u.Line.Index).ToList();
        var setSize = BuyQuantity + GetQuantity;
        var sets = Math.Min(units.Count / setSize, MaxApplications ?? int.MaxValue);

        var allocation = new UnitAllocation();
        for (var set = 0; set < sets; set++)
        {
            var members = units.Skip(set * setSize).Take(setSize).ToList();
            foreach (var paid in members.Take(BuyQuantity))
            {
                allocation.Add(paid.Line, 0);
            }

            foreach (var discounted in members.Skip(BuyQuantity))
            {
                allocation.Add(discounted.Line, discounted.UnitNet * DiscountPercent / 100m);
            }
        }

        return allocation;
    }

    private UnitAllocation SeparatePools(RewardContext context)
    {
        var buyUnits = UnitPool.From(context.EligibleLines).ToList();
        var getUnits = UnitPool.From(context.LinesMatching(GetProducts!)).OrderBy(u => u.UnitNet).ThenBy(u => u.Line.Index).ToList();
        var maxSets = Math.Min(Math.Min(buyUnits.Count / BuyQuantity, getUnits.Count / GetQuantity), MaxApplications ?? int.MaxValue);

        // A unit may match both pools; find the largest number of sets that fits without reusing units.
        for (var sets = maxSets; sets > 0; sets--)
        {
            var chosenGet = getUnits.Take(sets * GetQuantity).ToList();
            var usedPerLine = chosenGet.GroupBy(u => u.Line).ToDictionary(g => g.Key, g => g.Count());
            var chosenBuy = new List<UnitPool.Unit>();
            foreach (var unit in buyUnits.OrderByDescending(u => u.UnitNet).ThenBy(u => u.Line.Index))
            {
                if (chosenBuy.Count == sets * BuyQuantity)
                {
                    break;
                }

                if (usedPerLine.TryGetValue(unit.Line, out var used) && used > 0)
                {
                    usedPerLine[unit.Line] = used - 1; // this physical unit is already a "get" unit
                    continue;
                }

                chosenBuy.Add(unit);
            }

            if (chosenBuy.Count < sets * BuyQuantity)
            {
                continue;
            }

            var allocation = new UnitAllocation();
            chosenBuy.ForEach(u => allocation.Add(u.Line, 0));
            chosenGet.ForEach(u => allocation.Add(u.Line, u.UnitNet * DiscountPercent / 100m));
            return allocation;
        }

        return new UnitAllocation();
    }

    public override IEnumerable<string> Validate(string path)
    {
        if (BuyQuantity < 1)
        {
            yield return $"{path}.buyQuantity must be at least 1.";
        }

        if (GetQuantity < 1)
        {
            yield return $"{path}.getQuantity must be at least 1.";
        }

        if (DiscountPercent is <= 0 or > 100)
        {
            yield return $"{path}.discountPercent must be in (0, 100].";
        }

        if (MaxApplications is < 1)
        {
            yield return $"{path}.maxApplications must be at least 1.";
        }
    }

    public override IEnumerable<string> ReferencedProductLists() => GetProducts?.ReferencedProductLists() ?? [];

    [JsonIgnore]
    public override decimal? MaxRateEstimate =>
        BuyQuantity > 0 ? DiscountPercent / 100m * GetQuantity / (BuyQuantity + GetQuantity) : null;
}

/// <summary>"Any 3 for 100". Sets are built from the most expensive units first.</summary>
public sealed class BundlePriceReward : Reward
{
    public int Quantity { get; set; }

    public decimal Price { get; set; }

    public int? MaxApplications { get; set; }

    public override void Apply(RewardContext context)
    {
        var units = UnitPool.From(context.EligibleLines).OrderByDescending(u => u.UnitNet).ThenBy(u => u.Line.Index).ToList();
        var sets = Math.Min(units.Count / Quantity, MaxApplications ?? int.MaxValue);
        var allocation = new UnitAllocation();
        for (var set = 0; set < sets; set++)
        {
            var members = units.Skip(set * Quantity).Take(Quantity).ToList();
            var setNet = members.Sum(u => u.UnitNet);
            if (setNet <= Price)
            {
                break; // sets only get cheaper from here
            }

            var shares = Money.Allocate(setNet - Price, members.Select(u => u.UnitNet).ToList(), context.Decimals);
            for (var i = 0; i < members.Count; i++)
            {
                allocation.Add(members[i].Line, shares[i]);
            }
        }

        allocation.Commit(context);
    }

    public override IEnumerable<string> Validate(string path)
    {
        if (Quantity < 2)
        {
            yield return $"{path}.quantity must be at least 2.";
        }

        if (Price < 0)
        {
            yield return $"{path}.price cannot be negative.";
        }

        if (MaxApplications is < 1)
        {
            yield return $"{path}.maxApplications must be at least 1.";
        }
    }
}

/// <summary>Expands lines into individual free whole units.</summary>
internal static class UnitPool
{
    public sealed record Unit(LineState Line, decimal UnitNet);

    public static IEnumerable<Unit> From(IEnumerable<LineState> lines) =>
        lines.SelectMany(line => Enumerable.Repeat(new Unit(line, line.FreeUnitNet), line.FreeWholeUnits));
}

/// <summary>Collects unit discounts per line and applies them in one go (so unit prices stay stable while choosing).</summary>
internal sealed class UnitAllocation
{
    private readonly Dictionary<LineState, (int Units, decimal Discount)> _perLine = [];

    public void Add(LineState line, decimal discount)
    {
        var current = _perLine.GetValueOrDefault(line);
        _perLine[line] = (current.Units + 1, current.Discount + discount);
    }

    public void Commit(RewardContext context)
    {
        foreach (var (line, (units, discount)) in _perLine.OrderBy(p => p.Key.Index))
        {
            context.DiscountUnits(line, units, discount);
        }
    }
}
