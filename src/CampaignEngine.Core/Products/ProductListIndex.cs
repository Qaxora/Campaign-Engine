namespace CampaignEngine.Core.Products;

/// <summary>Answers "is this SKU in that list?" during evaluation.</summary>
public interface IProductListLookup
{
    bool Contains(string listCode, string sku);

    /// <summary>True when the SKU is in any <see cref="ProductListKind.GlobalExclusion"/> list.</summary>
    bool IsGloballyExcluded(string sku);
}

/// <summary>In-memory, read-only index over product lists. Unknown list codes behave as empty lists.</summary>
public sealed class ProductListIndex : IProductListLookup
{
    private readonly Dictionary<string, HashSet<string>> _lists = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _globallyExcluded = new(StringComparer.OrdinalIgnoreCase);

    public static ProductListIndex Empty { get; } = new([]);

    public ProductListIndex(IEnumerable<ProductList> lists)
    {
        foreach (var list in lists)
        {
            _lists[list.Code] = new HashSet<string>(list.Skus, StringComparer.OrdinalIgnoreCase);
            if (list.Kind == ProductListKind.GlobalExclusion)
            {
                _globallyExcluded.UnionWith(list.Skus);
            }
        }
    }

    public bool Contains(string listCode, string sku) =>
        _lists.TryGetValue(listCode, out var skus) && skus.Contains(sku);

    public bool IsGloballyExcluded(string sku) => _globallyExcluded.Contains(sku);

    public bool HasList(string listCode) => _lists.ContainsKey(listCode);
}
