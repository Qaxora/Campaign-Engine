using CampaignEngine.Core.Carts;

namespace CampaignEngine.Core.Products;

/// <summary>
/// Describes a set of products. A line matches when it matches <em>any</em> include criterion
/// (or there are no include criteria at all) and <em>no</em> exclude criterion.
/// All comparisons are case-insensitive.
/// </summary>
public sealed class ProductSelector
{
    public List<string> Skus { get; set; } = [];

    public List<string> Categories { get; set; } = [];

    public List<string> Brands { get; set; } = [];

    /// <summary>Codes of <see cref="ProductList"/>s.</summary>
    public List<string> ProductLists { get; set; } = [];

    /// <summary>Attribute name → accepted values, e.g. <c>{"season": ["SS26"]}</c>.</summary>
    public Dictionary<string, List<string>> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> ExcludeSkus { get; set; } = [];

    public List<string> ExcludeCategories { get; set; } = [];

    public List<string> ExcludeBrands { get; set; } = [];

    public List<string> ExcludeProductLists { get; set; } = [];

    public bool HasIncludeCriteria =>
        Skus.Count > 0 || Categories.Count > 0 || Brands.Count > 0 || ProductLists.Count > 0 || Attributes.Count > 0;

    public bool HasExcludeCriteria =>
        ExcludeSkus.Count > 0 || ExcludeCategories.Count > 0 || ExcludeBrands.Count > 0 || ExcludeProductLists.Count > 0;

    /// <summary>A selector without any criteria: matches every product.</summary>
    public static ProductSelector All => new();

    public bool Matches(CartLine line, IProductListLookup lists) =>
        IsIncluded(line, lists) && !IsExcluded(line, lists);

    private bool IsIncluded(CartLine line, IProductListLookup lists)
    {
        if (!HasIncludeCriteria)
        {
            return true;
        }

        return Contains(Skus, line.Sku)
            || (line.Brand is not null && Contains(Brands, line.Brand))
            || line.Categories.Exists(c => Contains(Categories, c))
            || ProductLists.Exists(list => lists.Contains(list, line.Sku))
            || Attributes.Any(pair => line.Attributes.TryGetValue(pair.Key, out var value) && Contains(pair.Value, value));
    }

    private bool IsExcluded(CartLine line, IProductListLookup lists) =>
        Contains(ExcludeSkus, line.Sku)
        || (line.Brand is not null && Contains(ExcludeBrands, line.Brand))
        || line.Categories.Exists(c => Contains(ExcludeCategories, c))
        || ExcludeProductLists.Exists(list => lists.Contains(list, line.Sku));

    private static bool Contains(List<string> values, string value) =>
        values.Exists(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));

    /// <summary>All product list codes referenced by this selector.</summary>
    public IEnumerable<string> ReferencedProductLists() => ProductLists.Concat(ExcludeProductLists);
}
