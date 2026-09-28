using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Http;

/// <summary>
/// Case-insensitive enum query parameters (<c>?status=active</c>), matching the camelCase enums in JSON bodies.
/// Minimal APIs bind enums case-sensitively.
/// </summary>
internal static class QueryEnum
{
    public static T? Parse<T>(string? value, string name)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse<T>(value, ignoreCase: true, out var result) && Enum.IsDefined(result)
            ? result
            : throw new ValidationException([$"{name} must be one of: {string.Join(", ", Enum.GetNames<T>().Select(ToCamel))}."]);
    }

    private static string ToCamel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
