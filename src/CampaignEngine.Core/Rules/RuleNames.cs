using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;

namespace CampaignEngine.Core.Rules;

/// <summary>Maps rule classes to their JSON discriminator (e.g. <c>BuyXGetYReward</c> → <c>buyXGetY</c>).</summary>
public static class RuleNames
{
    private static readonly ConcurrentDictionary<Type, string> Cache = new();

    public static string Of(Reward reward) => Of(reward.GetType(), typeof(Reward));

    public static string Of(Condition condition) => Of(condition.GetType(), typeof(Condition));

    private static string Of(Type type, Type baseType) =>
        Cache.GetOrAdd(type, t => baseType
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .FirstOrDefault(a => a.DerivedType == t)?.TypeDiscriminator as string ?? t.Name);
}
