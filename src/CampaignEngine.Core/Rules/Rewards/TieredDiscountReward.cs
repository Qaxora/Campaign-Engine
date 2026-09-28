namespace CampaignEngine.Core.Rules.Rewards;

/// <summary>
/// "Spend 500 get 50 off, spend 1000 get 150 off" or "buy 2 get 10%, buy 3 get 20%".
/// The highest reached tier applies. The basis is measured on the target lines at list price.
/// </summary>
public sealed class TieredDiscountReward : Reward
{
    public TierBasis Basis { get; set; } = TierBasis.Subtotal;

    public List<Tier> Tiers { get; set; } = [];

    public override void Apply(RewardContext context)
    {
        var lines = context.EligibleLines;
        var value = Basis == TierBasis.Subtotal ? lines.Sum(l => l.Gross) : lines.Sum(l => l.Line.Quantity);
        var ordered = Tiers.OrderBy(t => t.Threshold).ToList();
        var reached = ordered.LastOrDefault(t => t.Threshold <= value);
        var next = ordered.FirstOrDefault(t => t.Threshold > value);
        if (next is not null)
        {
            var missing = next.Threshold - value;
            context.Hint = Basis == TierBasis.Subtotal
                ? new ConditionHint(MissingAmount: missing)
                : new ConditionHint(MissingQuantity: missing);
        }

        if (reached is null)
        {
            return;
        }

        if (reached.Percent is { } percent)
        {
            PercentageDiscountReward.ApplyPercentage(context, lines, percent, reached.MaxDiscount);
        }
        else if (reached.Amount is { } amount)
        {
            context.DistributeDiscount(lines, amount, redistribute: true);
        }
    }

    public override IEnumerable<string> Validate(string path)
    {
        if (Tiers.Count == 0)
        {
            yield return $"{path}.tiers must not be empty.";
        }

        for (var i = 0; i < Tiers.Count; i++)
        {
            var tier = Tiers[i];
            if (tier.Threshold <= 0)
            {
                yield return $"{path}.tiers[{i}].threshold must be greater than zero.";
            }

            if ((tier.Percent is null) == (tier.Amount is null))
            {
                yield return $"{path}.tiers[{i}] must define exactly one of percent or amount.";
            }

            if (tier.Percent is <= 0 or > 100)
            {
                yield return $"{path}.tiers[{i}].percent must be in (0, 100].";
            }

            if (tier.Amount is <= 0)
            {
                yield return $"{path}.tiers[{i}].amount must be greater than zero.";
            }
        }

        if (Tiers.Select(t => t.Threshold).Distinct().Count() != Tiers.Count)
        {
            yield return $"{path}.tiers must have distinct thresholds.";
        }
    }

    public override decimal? MaxRateEstimate =>
        Tiers.Count > 0 && Tiers.TrueForAll(t => t.Percent is not null) ? Tiers.Max(t => t.Percent!.Value) / 100m : null;
}

public enum TierBasis
{
    Subtotal,
    Quantity,
}

public sealed class Tier
{
    public decimal Threshold { get; set; }

    public decimal? Percent { get; set; }

    public decimal? Amount { get; set; }

    /// <summary>Cap for percentage tiers.</summary>
    public decimal? MaxDiscount { get; set; }
}
