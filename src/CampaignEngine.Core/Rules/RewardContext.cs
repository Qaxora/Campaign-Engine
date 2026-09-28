using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Carts;
using CampaignEngine.Core.Products;

namespace CampaignEngine.Core.Rules;

/// <summary>
/// What a <see cref="Reward"/> sees and may change while it is applied. Every discount goes through
/// this class, which enforces price floors, the campaign's per-order cap / remaining budget and
/// rounding — so rewards cannot break those rules by accident.
/// </summary>
public sealed class RewardContext
{
    private readonly PricingState _state;
    private readonly Dictionary<LineState, decimal> _lineDiscounts = [];
    private readonly List<(string Sku, decimal Quantity)> _gifts = [];
    private decimal? _capRemaining;

    internal RewardContext(Campaign campaign, PricingState state, IProductListLookup lists, decimal? cap, int decimals)
    {
        Campaign = campaign;
        _state = state;
        Lists = lists;
        _capRemaining = cap;
        Decimals = decimals;
        EligibleLines = state.Lines.Where(l => IsEligible(l) && Campaign.Target.Matches(l.Line, lists)).ToList();
    }

    public Campaign Campaign { get; }

    public Cart Cart => _state.Cart;

    public IProductListLookup Lists { get; }

    public int Decimals { get; }

    /// <summary>Lines matching the campaign target that can still be discounted.</summary>
    public IReadOnlyList<LineState> EligibleLines { get; }

    public decimal RemainingShipping => _state.RemainingShipping;

    /// <summary>Total discount given by this reward so far (lines + shipping).</summary>
    public decimal TotalDiscount => _lineDiscounts.Values.Sum() + ShippingDiscount;

    public decimal ShippingDiscount { get; private set; }

    /// <summary>Optional upsell hint set by the reward, e.g. the next tier of a tiered discount.</summary>
    public ConditionHint? Hint { get; set; }

    internal IReadOnlyDictionary<LineState, decimal> LineDiscounts => _lineDiscounts;

    internal IReadOnlyList<(string Sku, decimal Quantity)> Gifts => _gifts;

    /// <summary>Lines matching <paramref name="selector"/> that can be discounted (ignores the campaign target).</summary>
    public IEnumerable<LineState> LinesMatching(ProductSelector selector) =>
        _state.Lines.Where(l => IsEligible(l) && selector.Matches(l.Line, Lists));

    public decimal Round(decimal amount) => Money.Round(amount, Decimals);

    /// <summary>Discounts a whole line. Returns the amount actually applied after floor and cap.</summary>
    public decimal DiscountLine(LineState line, decimal amount)
    {
        var applied = Clamp(line, amount);
        if (applied > 0)
        {
            line.ApplyDiscount(applied);
            Record(line, applied);
        }

        return applied;
    }

    /// <summary>Discounts and locks free units of a line (unit-based offers).</summary>
    public decimal DiscountUnits(LineState line, decimal units, decimal amount)
    {
        if (units <= 0 || units > line.FreeQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(units), units, "Units must be free units of the line.");
        }

        var applied = Clamp(line, amount);
        line.ApplyUnitDiscount(units, applied);
        Record(line, applied);
        return applied;
    }

    /// <summary>
    /// Spreads <paramref name="amount"/> across <paramref name="lines"/> in proportion to their net amounts.
    /// With <paramref name="redistribute"/>, the part a line cannot take (floor) moves to the other lines;
    /// otherwise it is simply not given (right for percentages, where each line gets "its" share).
    /// </summary>
    public decimal DistributeDiscount(IReadOnlyList<LineState> lines, decimal amount, bool redistribute)
    {
        var remaining = Round(amount);
        var applied = 0m;
        var pool = lines.Where(l => l.Headroom > 0).ToList();
        while (remaining > 0 && pool.Count > 0 && _capRemaining is not <= 0)
        {
            var toAllocate = _capRemaining is { } cap ? Math.Min(remaining, cap) : remaining;
            var parts = Money.Allocate(toAllocate, pool.Select(l => l.Net).ToList(), Decimals);
            var round = 0m;
            for (var i = 0; i < pool.Count; i++)
            {
                round += DiscountLine(pool[i], parts[i]);
            }

            applied += round;
            remaining -= round;
            if (!redistribute || round == 0)
            {
                break;
            }

            pool = pool.Where(l => l.Headroom > 0).ToList();
        }

        return applied;
    }

    public decimal DiscountShipping(decimal amount)
    {
        var applied = Math.Min(Round(amount), _state.RemainingShipping);
        if (_capRemaining is { } cap)
        {
            applied = Math.Min(applied, cap);
            _capRemaining = cap - Math.Max(0, applied);
        }

        if (applied <= 0)
        {
            return 0;
        }

        _state.RemainingShipping -= applied;
        ShippingDiscount += applied;
        return applied;
    }

    public void AddGift(string sku, decimal quantity) => _gifts.Add((sku, quantity));

    private bool IsEligible(LineState line) =>
        line.Line.Discountable
        && line.Gross > 0
        && (Campaign.IgnoreGlobalExclusions || !Lists.IsGloballyExcluded(line.Line.Sku));

    private decimal Clamp(LineState line, decimal amount)
    {
        var applied = Math.Min(Round(amount), line.Headroom);
        if (_capRemaining is { } cap)
        {
            applied = Math.Min(applied, cap);
        }

        applied = Math.Max(0, applied);
        if (_capRemaining is not null)
        {
            _capRemaining -= applied;
        }

        return applied;
    }

    private void Record(LineState line, decimal amount)
    {
        if (amount <= 0)
        {
            return;
        }

        _lineDiscounts[line] = _lineDiscounts.GetValueOrDefault(line) + amount;
    }
}

/// <summary>The cart being priced by one plan.</summary>
internal sealed class PricingState
{
    public PricingState(Cart cart, int decimals)
    {
        Cart = cart;
        Lines = cart.Lines.Select((line, i) => new LineState(line, i, decimals)).ToList();
        RemainingShipping = Money.Round(cart.ShippingAmount, decimals);
    }

    public Cart Cart { get; }

    public IReadOnlyList<LineState> Lines { get; }

    public decimal RemainingShipping { get; set; }
}
