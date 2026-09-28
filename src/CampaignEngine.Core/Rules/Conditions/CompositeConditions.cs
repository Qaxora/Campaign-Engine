namespace CampaignEngine.Core.Rules.Conditions;

/// <summary>All nested conditions must hold.</summary>
public sealed class AllOfCondition : Condition
{
    public List<Condition> Conditions { get; set; } = [];

    public override ConditionResult Evaluate(ConditionContext context)
    {
        foreach (var condition in Conditions)
        {
            var result = condition.Evaluate(context);
            if (!result.IsSatisfied)
            {
                return result;
            }
        }

        return ConditionResult.Satisfied;
    }

    public override IEnumerable<string> Validate(string path) => Composite.Validate(Conditions, path);

    public override IEnumerable<string> ReferencedProductLists() => Conditions.SelectMany(c => c.ReferencedProductLists());
}

/// <summary>At least one nested condition must hold.</summary>
public sealed class AnyOfCondition : Condition
{
    public List<Condition> Conditions { get; set; } = [];

    public override ConditionResult Evaluate(ConditionContext context)
    {
        var failures = new List<ConditionResult>();
        foreach (var condition in Conditions)
        {
            var result = condition.Evaluate(context);
            if (result.IsSatisfied)
            {
                return result;
            }

            failures.Add(result);
        }

        // Surface the hint of the branch that is closest to being met, so upsell messages still work.
        var closest = failures
            .Select(f => f.Hint)
            .OfType<ConditionHint>()
            .MinBy(h => h.MissingAmount ?? decimal.MaxValue);
        return ConditionResult.Failed(
            "None of: " + string.Join(" / ", failures.Select(f => f.Reason)),
            closest);
    }

    public override IEnumerable<string> Validate(string path) => Composite.Validate(Conditions, path);

    public override IEnumerable<string> ReferencedProductLists() => Conditions.SelectMany(c => c.ReferencedProductLists());
}

/// <summary>The nested condition must not hold.</summary>
public sealed class NotCondition : Condition
{
    public Condition? Condition { get; set; }

    public override ConditionResult Evaluate(ConditionContext context) =>
        Condition is null || Condition.Evaluate(context).IsSatisfied
            ? ConditionResult.Failed("Negated condition holds.")
            : ConditionResult.Satisfied;

    public override IEnumerable<string> Validate(string path) =>
        Condition is null ? [$"{path}.condition is required."] : Condition.Validate($"{path}.condition");

    public override IEnumerable<string> ReferencedProductLists() => Condition?.ReferencedProductLists() ?? [];
}

internal static class Composite
{
    public static IEnumerable<string> Validate(List<Condition> conditions, string path) =>
        conditions.Count == 0
            ? [$"{path}.conditions must not be empty."]
            : conditions.SelectMany((c, i) => c.Validate($"{path}.conditions[{i}]"));
}
