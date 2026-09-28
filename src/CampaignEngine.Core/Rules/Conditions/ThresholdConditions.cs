using CampaignEngine.Core.Products;

namespace CampaignEngine.Core.Rules.Conditions;

/// <summary>"Spend at least X" — e.g. 500 TRY and above.</summary>
public sealed class MinSubtotalCondition : Condition
{
    public decimal Amount { get; set; }

    /// <summary>Which lines count. Defaults to the campaign's qualifying lines.</summary>
    public ProductSelector? Products { get; set; }

    public override ConditionResult Evaluate(ConditionContext context)
    {
        var subtotal = context.Lines(Products).Sum(context.Gross);
        return subtotal >= Amount
            ? ConditionResult.Satisfied
            : ConditionResult.Failed(
                $"Subtotal {subtotal} is below {Amount}.",
                new ConditionHint(MissingAmount: Amount - subtotal));
    }

    public override IEnumerable<string> Validate(string path)
    {
        if (Amount <= 0)
        {
            yield return $"{path}.amount must be greater than zero.";
        }
    }

    public override IEnumerable<string> ReferencedProductLists() => Products?.ReferencedProductLists() ?? [];
}

/// <summary>"Buy at least N items".</summary>
public sealed class MinQuantityCondition : Condition
{
    public decimal Quantity { get; set; }

    /// <summary>Which lines count. Defaults to the campaign's qualifying lines.</summary>
    public ProductSelector? Products { get; set; }

    public override ConditionResult Evaluate(ConditionContext context)
    {
        var quantity = context.Lines(Products).Sum(l => l.Quantity);
        return quantity >= Quantity
            ? ConditionResult.Satisfied
            : ConditionResult.Failed(
                $"Quantity {quantity} is below {Quantity}.",
                new ConditionHint(MissingQuantity: Quantity - quantity));
    }

    public override IEnumerable<string> Validate(string path)
    {
        if (Quantity <= 0)
        {
            yield return $"{path}.quantity must be greater than zero.";
        }
    }

    public override IEnumerable<string> ReferencedProductLists() => Products?.ReferencedProductLists() ?? [];
}
