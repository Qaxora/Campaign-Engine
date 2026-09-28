namespace CampaignEngine.Core.Rules.Rewards;

/// <summary>"20% off" the target products.</summary>
public sealed class PercentageDiscountReward : Reward
{
    public decimal Percent { get; set; }

    /// <summary>Optional cap for this reward, e.g. "20% off, at most 200".</summary>
    public decimal? MaxDiscount { get; set; }

    public override void Apply(RewardContext context) =>
        ApplyPercentage(context, context.EligibleLines, Percent, MaxDiscount);

    internal static void ApplyPercentage(RewardContext context, IReadOnlyList<LineState> lines, decimal percent, decimal? max)
    {
        var amount = context.Round(lines.Sum(l => l.Net) * percent / 100m);
        if (max is { } cap)
        {
            amount = Math.Min(amount, cap);
        }

        context.DistributeDiscount(lines, amount, redistribute: false);
    }

    public override IEnumerable<string> Validate(string path)
    {
        if (Percent is <= 0 or > 100)
        {
            yield return $"{path}.percent must be in (0, 100].";
        }

        if (MaxDiscount is <= 0)
        {
            yield return $"{path}.maxDiscount must be greater than zero.";
        }
    }

    public override decimal? MaxRateEstimate => Percent / 100m;
}

/// <summary>"100 off the basket" or, with <see cref="PerUnit"/>, "10 off every unit".</summary>
public sealed class AmountDiscountReward : Reward
{
    public decimal Amount { get; set; }

    public bool PerUnit { get; set; }

    public override void Apply(RewardContext context)
    {
        if (!PerUnit)
        {
            context.DistributeDiscount(context.EligibleLines, Amount, redistribute: true);
            return;
        }

        foreach (var line in context.EligibleLines)
        {
            context.DiscountLine(line, Amount * line.Line.Quantity);
        }
    }

    public override IEnumerable<string> Validate(string path)
    {
        if (Amount <= 0)
        {
            yield return $"{path}.amount must be greater than zero.";
        }
    }
}

/// <summary>"Selected products for 99.90 each". Units already used by other unit offers are skipped.</summary>
public sealed class FixedUnitPriceReward : Reward
{
    public decimal Price { get; set; }

    public override void Apply(RewardContext context)
    {
        foreach (var line in context.EligibleLines)
        {
            var units = line.FreeQuantity;
            var unitNet = line.FreeUnitNet;
            if (units > 0 && unitNet > Price)
            {
                context.DiscountUnits(line, units, (unitNet - Price) * units);
            }
        }
    }

    public override IEnumerable<string> Validate(string path)
    {
        if (Price < 0)
        {
            yield return $"{path}.price cannot be negative.";
        }
    }
}

/// <summary>Free or discounted shipping.</summary>
public sealed class FreeShippingReward : Reward
{
    /// <summary>Optional cap; null means the whole shipping fee.</summary>
    public decimal? MaxAmount { get; set; }

    public override void Apply(RewardContext context) =>
        context.DiscountShipping(Math.Min(context.RemainingShipping, MaxAmount ?? decimal.MaxValue));

    public override IEnumerable<string> Validate(string path)
    {
        if (MaxAmount is <= 0)
        {
            yield return $"{path}.maxAmount must be greater than zero.";
        }
    }
}

/// <summary>A free product added to the order (the channel adds the line; the engine reports it).</summary>
public sealed class GiftProductReward : Reward
{
    public string Sku { get; set; } = "";

    public decimal Quantity { get; set; } = 1;

    public override void Apply(RewardContext context) => context.AddGift(Sku, Quantity);

    public override IEnumerable<string> Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(Sku))
        {
            yield return $"{path}.sku is required.";
        }

        if (Quantity <= 0)
        {
            yield return $"{path}.quantity must be greater than zero.";
        }
    }
}
