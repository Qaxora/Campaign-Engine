using CampaignEngine.Core.Carts;
using CampaignEngine.Core.Products;

namespace CampaignEngine.Core.Rules;

public sealed class ConditionContext
{
    public required Cart Cart { get; init; }

    public required IProductListLookup Lists { get; init; }

    /// <summary>
    /// Lines the campaign's reward could apply to: matching the target, discountable and not globally
    /// excluded. Threshold conditions without their own product selector count these lines.
    /// </summary>
    public required IReadOnlyList<CartLine> QualifyingLines { get; init; }

    public int Decimals { get; init; } = 2;

    /// <summary>The qualifying lines, or — when a selector is given — exactly the lines it matches.</summary>
    public IEnumerable<CartLine> Lines(ProductSelector? selector) =>
        selector is null ? QualifyingLines : Cart.Lines.Where(l => selector.Matches(l, Lists));

    public decimal Gross(CartLine line) => Money.Round(line.Quantity * line.UnitPrice, Decimals);
}
