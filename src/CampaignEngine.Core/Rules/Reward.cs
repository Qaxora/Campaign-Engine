namespace CampaignEngine.Core.Rules;

/// <summary>
/// What the customer gets when a campaign applies. Serialized with a <c>"type"</c> discriminator.
/// </summary>
public abstract class Reward
{
    /// <summary>Returns validation errors, prefixed with <paramref name="path"/>.</summary>
    public virtual IEnumerable<string> Validate(string path) => [];

    public virtual IEnumerable<string> ReferencedProductLists() => [];
}
