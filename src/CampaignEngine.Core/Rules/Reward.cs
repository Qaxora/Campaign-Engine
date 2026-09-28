using System.Text.Json.Serialization;
using CampaignEngine.Core.Rules.Rewards;

namespace CampaignEngine.Core.Rules;

/// <summary>
/// What the customer gets when a campaign applies. Serialized with a <c>"type"</c> discriminator.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PercentageDiscountReward), "percentageDiscount")]
[JsonDerivedType(typeof(AmountDiscountReward), "amountDiscount")]
[JsonDerivedType(typeof(FixedUnitPriceReward), "fixedUnitPrice")]
[JsonDerivedType(typeof(BuyXGetYReward), "buyXGetY")]
[JsonDerivedType(typeof(BundlePriceReward), "bundlePrice")]
[JsonDerivedType(typeof(TieredDiscountReward), "tieredDiscount")]
[JsonDerivedType(typeof(FreeShippingReward), "freeShipping")]
[JsonDerivedType(typeof(GiftProductReward), "giftProduct")]
public abstract class Reward
{
    /// <summary>Applies the reward to the cart through <paramref name="context"/>.</summary>
    public abstract void Apply(RewardContext context);

    /// <summary>Returns validation errors, prefixed with <paramref name="path"/>.</summary>
    public virtual IEnumerable<string> Validate(string path) => [];

    public virtual IEnumerable<string> ReferencedProductLists() => [];

    /// <summary>
    /// Best-effort upper bound of the discount rate on a single product, used by conflict analysis
    /// to estimate how deep two stacked campaigns can go. Null when it cannot be estimated.
    /// </summary>
    public virtual decimal? MaxRateEstimate => null;
}
