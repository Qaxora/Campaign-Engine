namespace CampaignEngine.Core.Rules.Conditions;

/// <summary>Only for the customer's first order (as reported by the channel).</summary>
public sealed class FirstOrderCondition : Condition
{
    public override ConditionResult Evaluate(ConditionContext context) =>
        context.Cart.Customer?.IsFirstOrder == true
            ? ConditionResult.Satisfied
            : ConditionResult.Failed("Not the customer's first order.");
}

/// <summary>
/// Payment-based campaigns: payment method, issuing bank, card BIN prefixes, installment count.
/// Every non-empty criterion must match.
/// </summary>
public sealed class PaymentCondition : Condition
{
    public List<string> Methods { get; set; } = [];

    public List<string> BankCodes { get; set; } = [];

    /// <summary>BIN prefixes; a card matches when its BIN starts with one of them.</summary>
    public List<string> CardBins { get; set; } = [];

    public int? MinInstallments { get; set; }

    public int? MaxInstallments { get; set; }

    public override ConditionResult Evaluate(ConditionContext context)
    {
        var payment = context.Cart.Payment;
        if (payment is null)
        {
            return ConditionResult.Failed("No payment information in the cart.");
        }

        if (Methods.Count > 0 && !Methods.Contains(payment.Method ?? "", StringComparer.OrdinalIgnoreCase))
        {
            return ConditionResult.Failed($"Payment method '{payment.Method}' is not eligible.");
        }

        if (BankCodes.Count > 0 && !BankCodes.Contains(payment.BankCode ?? "", StringComparer.OrdinalIgnoreCase))
        {
            return ConditionResult.Failed($"Bank '{payment.BankCode}' is not eligible.");
        }

        if (CardBins.Count > 0 &&
            (payment.CardBin is null || !CardBins.Exists(bin => payment.CardBin.StartsWith(bin, StringComparison.Ordinal))))
        {
            return ConditionResult.Failed("Card BIN is not eligible.");
        }

        var installments = payment.Installments ?? 1;
        if (installments < (MinInstallments ?? int.MinValue) || installments > (MaxInstallments ?? int.MaxValue))
        {
            return ConditionResult.Failed($"{installments} installments are not eligible.");
        }

        return ConditionResult.Satisfied;
    }

    public override IEnumerable<string> Validate(string path)
    {
        if (MinInstallments is { } min && MaxInstallments is { } max && min > max)
        {
            yield return $"{path}.minInstallments cannot be greater than maxInstallments.";
        }
    }
}

/// <summary>A free-form cart attribute must be present (and, if values are given, equal one of them).</summary>
public sealed class CartAttributeCondition : Condition
{
    public string Key { get; set; } = "";

    public List<string> Values { get; set; } = [];

    public override ConditionResult Evaluate(ConditionContext context)
    {
        if (!context.Cart.Attributes.TryGetValue(Key, out var value))
        {
            return ConditionResult.Failed($"Cart attribute '{Key}' is missing.");
        }

        return Values.Count == 0 || Values.Contains(value, StringComparer.OrdinalIgnoreCase)
            ? ConditionResult.Satisfied
            : ConditionResult.Failed($"Cart attribute '{Key}' = '{value}' is not eligible.");
    }

    public override IEnumerable<string> Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            yield return $"{path}.key is required.";
        }
    }
}
