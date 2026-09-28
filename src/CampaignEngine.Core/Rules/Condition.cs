namespace CampaignEngine.Core.Rules;

/// <summary>
/// A predicate over the cart that must hold for a campaign to apply.
/// Serialized with a <c>"type"</c> discriminator.
/// </summary>
public abstract class Condition
{
    /// <summary>Returns validation errors, prefixed with <paramref name="path"/>.</summary>
    public virtual IEnumerable<string> Validate(string path) => [];

    public virtual IEnumerable<string> ReferencedProductLists() => [];
}
