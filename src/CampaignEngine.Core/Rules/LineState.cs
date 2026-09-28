using CampaignEngine.Core.Carts;

namespace CampaignEngine.Core.Rules;

/// <summary>
/// Pricing state of one cart line while campaigns are being applied.
/// </summary>
/// <remarks>
/// Unit-based rewards (buy X get Y, bundles, fixed unit price) <em>lock</em> the units they use so
/// that a unit is never part of two such offers. The engine remembers how much of the line's net
/// amount belongs to locked units, so later unit-based offers price the remaining units correctly.
/// Proportional discounts (percentage, amount) apply to the whole line, locked units included.
/// </remarks>
public sealed class LineState
{
    private decimal _lockedNet;

    internal LineState(CartLine line, int index, int decimals)
    {
        Line = line;
        Index = index;
        Gross = Money.Round(line.Quantity * line.UnitPrice, decimals);
        Floor = Math.Min(Gross, Money.Round(line.Quantity * (line.MinimumUnitPrice ?? 0), decimals));
        IsWholeUnits = line.Quantity == decimal.Truncate(line.Quantity);
    }

    public CartLine Line { get; }

    /// <summary>Position of the line in the cart; used for deterministic ordering.</summary>
    public int Index { get; }

    /// <summary>Quantity × unit price, rounded.</summary>
    public decimal Gross { get; }

    /// <summary>The lowest net amount the line may reach (price floor × quantity).</summary>
    public decimal Floor { get; }

    public decimal Discount { get; private set; }

    public decimal Net => Gross - Discount;

    /// <summary>How much more discount the line can take before hitting its floor.</summary>
    public decimal Headroom => Math.Max(0, Net - Floor);

    public decimal LockedQuantity { get; private set; }

    public decimal FreeQuantity => Line.Quantity - LockedQuantity;

    /// <summary>False for weighed items (e.g. 0.75 kg); unit-based offers skip them.</summary>
    public bool IsWholeUnits { get; }

    /// <summary>Whole units not yet used by a unit-based offer.</summary>
    public int FreeWholeUnits => IsWholeUnits ? (int)FreeQuantity : 0;

    /// <summary>Current net price of one unit that is not locked.</summary>
    public decimal FreeUnitNet => FreeQuantity > 0 ? (Net - _lockedNet) / FreeQuantity : 0;

    /// <summary>Discounts the whole line (locked and free units proportionally).</summary>
    internal void ApplyDiscount(decimal amount)
    {
        if (Net > 0 && _lockedNet > 0)
        {
            _lockedNet -= amount * _lockedNet / Net;
        }

        Discount += amount;
    }

    /// <summary>Discounts <paramref name="units"/> free units by <paramref name="amount"/> in total and locks them.</summary>
    internal void ApplyUnitDiscount(decimal units, decimal amount)
    {
        _lockedNet += units * FreeUnitNet - amount;
        LockedQuantity += units;
        Discount += amount;
    }
}
