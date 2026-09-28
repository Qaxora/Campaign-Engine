namespace CampaignEngine.Core;

/// <summary>
/// Money helpers. Amounts are <see cref="decimal"/> in the cart currency; there is no floating point
/// anywhere in the engine.
/// </summary>
public static class Money
{
    /// <summary>Rounds half away from zero, which is what receipts and invoices expect.</summary>
    public static decimal Round(decimal value, int decimals) =>
        Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Splits <paramref name="total"/> across <paramref name="weights"/> proportionally so that the
    /// parts add up to the rounded total exactly (largest remainder method). Negative weights count as zero.
    /// </summary>
    public static decimal[] Allocate(decimal total, IReadOnlyList<decimal> weights, int decimals)
    {
        var result = new decimal[weights.Count];
        total = Round(total, decimals);
        var weightSum = weights.Sum(w => Math.Max(0, w));
        if (total <= 0 || weightSum <= 0)
        {
            return result;
        }

        var unit = Unit(decimals);
        var remainders = new decimal[weights.Count];
        var allocated = 0m;
        for (var i = 0; i < weights.Count; i++)
        {
            var raw = total * Math.Max(0, weights[i]) / weightSum;
            var floored = Math.Floor(raw / unit) * unit;
            result[i] = floored;
            remainders[i] = raw - floored;
            allocated += floored;
        }

        // Hand out the leftover cents to the largest remainders; ties go to the earlier index.
        var leftoverUnits = (int)Math.Round((total - allocated) / unit);
        foreach (var index in Enumerable.Range(0, weights.Count)
                     .Where(i => weights[i] > 0)
                     .OrderByDescending(i => remainders[i])
                     .ThenBy(i => i)
                     .Take(leftoverUnits))
        {
            result[index] += unit;
        }

        return result;
    }

    /// <summary>The smallest amount for the given number of decimals, e.g. 0.01 for 2.</summary>
    public static decimal Unit(int decimals)
    {
        var unit = 1m;
        for (var i = 0; i < decimals; i++)
        {
            unit /= 10m;
        }

        return unit;
    }
}
