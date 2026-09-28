using System.Text.Json.Serialization;
using CampaignEngine.Core.Rules.Conditions;

namespace CampaignEngine.Core.Rules;

/// <summary>
/// A predicate over the cart that must hold for a campaign to apply.
/// Serialized with a <c>"type"</c> discriminator. Conditions always look at the cart as sent by the
/// channel (list prices), never at discounts given by other campaigns.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MinSubtotalCondition), "minSubtotal")]
[JsonDerivedType(typeof(MinQuantityCondition), "minQuantity")]
[JsonDerivedType(typeof(FirstOrderCondition), "firstOrder")]
[JsonDerivedType(typeof(PaymentCondition), "payment")]
[JsonDerivedType(typeof(CartAttributeCondition), "cartAttribute")]
[JsonDerivedType(typeof(AllOfCondition), "allOf")]
[JsonDerivedType(typeof(AnyOfCondition), "anyOf")]
[JsonDerivedType(typeof(NotCondition), "not")]
public abstract class Condition
{
    public abstract ConditionResult Evaluate(ConditionContext context);

    /// <summary>Returns validation errors, prefixed with <paramref name="path"/>.</summary>
    public virtual IEnumerable<string> Validate(string path) => [];

    public virtual IEnumerable<string> ReferencedProductLists() => [];
}

/// <param name="IsSatisfied">Whether the condition holds.</param>
/// <param name="Reason">Why it does not hold (for explanations).</param>
/// <param name="Hint">How close the cart is, used for upsell messages.</param>
public readonly record struct ConditionResult(bool IsSatisfied, string? Reason = null, ConditionHint? Hint = null)
{
    public static ConditionResult Satisfied { get; } = new(true);

    public static ConditionResult Failed(string reason, ConditionHint? hint = null) => new(false, reason, hint);
}

/// <summary>What is missing to satisfy a threshold condition.</summary>
public sealed record ConditionHint(decimal? MissingAmount = null, decimal? MissingQuantity = null);
