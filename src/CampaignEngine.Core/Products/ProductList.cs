using System.Text.Json.Serialization;

namespace CampaignEngine.Core.Products;

/// <summary>
/// A named, reusable set of SKUs maintained through the API instead of SQL scripts.
/// </summary>
public sealed class ProductList
{
    public Guid Id { get; set; }

    /// <summary>Stable code campaigns refer to, e.g. <c>NO-DISCOUNT</c>, <c>SUMMER-26-TSHIRTS</c>.</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public ProductListKind Kind { get; set; } = ProductListKind.Standard;

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)] // keep the case-insensitive comparer
    public HashSet<string> Skus { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public enum ProductListKind
{
    /// <summary>Referenced explicitly by campaign selectors.</summary>
    Standard,

    /// <summary>
    /// Products that no campaign may discount (tobacco, gold, gift cards, regulated items).
    /// Applied to every campaign unless the campaign sets <c>ignoreGlobalExclusions</c>.
    /// </summary>
    GlobalExclusion,
}
