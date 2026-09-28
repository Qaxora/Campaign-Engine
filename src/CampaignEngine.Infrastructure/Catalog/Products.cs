using System.Text.Json;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Platform;
using CampaignEngine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Catalog;

/// <summary>
/// Reference data about a product. The engine never reads it while pricing — carts carry their own
/// categories and prices — but the builder and the AI assistant use it to offer real SKUs, categories
/// and brands.
/// </summary>
public sealed class ProductRecord : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Sku { get; set; }

    public required string Name { get; set; }

    public string? Brand { get; set; }

    /// <summary>Attributes as a JSON object of strings.</summary>
    public string? AttributesJson { get; set; }

    public bool Active { get; set; } = true;

    public DateTime UpdatedAt { get; set; }

    public List<ProductCategoryRecord> Categories { get; set; } = [];
}

/// <summary>One category code of a product (a product usually has its whole category path).</summary>
public sealed class ProductCategoryRecord : ITenantOwned
{
    public Guid ProductId { get; set; }

    public Guid TenantId { get; set; }

    public required string Category { get; set; }
}

public sealed record Product(
    string Sku, string Name, string? Brand, IReadOnlyList<string> Categories, IReadOnlyDictionary<string, string> Attributes, bool Active, DateTimeOffset UpdatedAt);

public sealed record ProductInput(string Sku, string Name, string? Brand, IReadOnlyList<string>? Categories, IReadOnlyDictionary<string, string>? Attributes, bool Active = true);

public sealed record ProductQuery(string? Search = null, string? Category = null, string? Brand = null, bool? Active = null, int Page = 1, int PageSize = 50);

public sealed record FacetValue(string Value, int Count);

public sealed record CatalogFacets(IReadOnlyList<FacetValue> Categories, IReadOnlyList<FacetValue> Brands, int ProductCount);

public sealed record ImportResult(int Created, int Updated, int Total);

public sealed class ProductService(CampaignDbContext db, TimeProvider time)
{
    public const int MaxProductsPerRequest = 20_000;

    public async Task<PagedResult<Product>> SearchAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        var q = db.Products.AsNoTracking().Include(p => p.Categories).AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToUpperInvariant();
            q = q.Where(p => p.Sku.ToUpper().Contains(term) || p.Name.ToUpper().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim().ToUpperInvariant();
            q = q.Where(p => p.Categories.Any(c => c.Category.ToUpper() == category));
        }

        if (!string.IsNullOrWhiteSpace(query.Brand))
        {
            var brand = query.Brand.Trim().ToUpperInvariant();
            q = q.Where(p => p.Brand != null && p.Brand.ToUpper() == brand);
        }

        if (query.Active is { } active)
        {
            q = q.Where(p => p.Active == active);
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 500);
        var total = await q.CountAsync(cancellationToken);
        var items = await q.OrderBy(p => p.Sku).Skip((page - 1) * size).Take(size).AsSplitQuery().ToListAsync(cancellationToken);
        return new PagedResult<Product>(items.Select(ToModel).ToList(), page, size, total);
    }

    public async Task<Product?> GetAsync(string sku, CancellationToken cancellationToken = default)
    {
        var normalized = sku.Trim().ToUpperInvariant();
        var record = await db.Products.AsNoTracking().Include(p => p.Categories)
            .FirstOrDefaultAsync(p => p.Sku.ToUpper() == normalized, cancellationToken);
        return record is null ? null : ToModel(record);
    }

    /// <summary>Creates or updates products by SKU. Existing products are replaced field by field.</summary>
    public async Task<ImportResult> UpsertAsync(IReadOnlyList<ProductInput> products, CancellationToken cancellationToken = default)
    {
        if (products.Count > MaxProductsPerRequest)
        {
            throw new ValidationException([$"At most {MaxProductsPerRequest} products per request."]);
        }

        var errors = products.SelectMany((p, i) => Validate(p, $"products[{i}]")).ToList();
        var duplicates = products.GroupBy(p => p.Sku.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            errors.Add($"Duplicate SKUs in the request: {string.Join(", ", duplicates.Take(10))}.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors.Take(50).ToList());
        }

        var now = time.GetUtcNow().UtcDateTime;
        int created = 0, updated = 0;
        foreach (var chunk in products.Chunk(500))
        {
            var skus = chunk.Select(p => p.Sku.Trim().ToUpperInvariant()).ToList();
            var existing = await db.Products.Include(p => p.Categories)
                .Where(p => skus.Contains(p.Sku.ToUpper()))
                .ToDictionaryAsync(p => p.Sku.ToUpperInvariant(), cancellationToken);
            foreach (var input in chunk)
            {
                if (!existing.TryGetValue(input.Sku.Trim().ToUpperInvariant(), out var record))
                {
                    record = new ProductRecord { Id = Guid.NewGuid(), Sku = input.Sku.Trim(), Name = "" };
                    db.Products.Add(record);
                    created++;
                }
                else
                {
                    updated++;
                }

                Apply(record, input, now);
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        return new ImportResult(created, updated, products.Count);
    }

    /// <summary>
    /// Imports a CSV with a header row. Columns: <c>sku</c>, <c>name</c> (required), <c>brand</c>,
    /// <c>categories</c> (separated by <c>|</c>), <c>active</c>, and any other column as an attribute.
    /// </summary>
    public async Task<ImportResult> ImportCsvAsync(TextReader reader, CancellationToken cancellationToken = default)
    {
        var rows = await Csv.ReadAsync(reader, MaxProductsPerRequest, cancellationToken);
        if (rows.Count == 0)
        {
            return new ImportResult(0, 0, 0);
        }

        var header = rows[0].Select(h => h.ToLowerInvariant()).ToArray();
        var sku = Array.IndexOf(header, "sku");
        var name = Array.IndexOf(header, "name");
        if (sku < 0 || name < 0)
        {
            throw new ValidationException(["The CSV needs a header row with at least 'sku' and 'name' columns."]);
        }

        var brand = Array.IndexOf(header, "brand");
        var categories = Array.IndexOf(header, "categories");
        var active = Array.IndexOf(header, "active");
        var known = new[] { sku, name, brand, categories, active };
        var attributeColumns = Enumerable.Range(0, header.Length).Where(i => !known.Contains(i) && header[i].Length > 0).ToList();

        string? Cell(string[] row, int index) => index >= 0 && index < row.Length && row[index].Length > 0 ? row[index] : null;

        var inputs = rows.Skip(1).Select(row => new ProductInput(
            Cell(row, sku) ?? "",
            Cell(row, name) ?? "",
            Cell(row, brand),
            Cell(row, categories)?.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            attributeColumns.Where(i => Cell(row, i) is not null).ToDictionary(i => rows[0][i], i => Cell(row, i)!),
            Cell(row, active) is not { } flag || !(flag.Equals("false", StringComparison.OrdinalIgnoreCase) || flag == "0" || flag.Equals("no", StringComparison.OrdinalIgnoreCase))))
            .ToList();
        return await UpsertAsync(inputs, cancellationToken);
    }

    public async Task DeleteAsync(string sku, CancellationToken cancellationToken = default)
    {
        var normalized = sku.Trim().ToUpperInvariant();
        var record = await db.Products.FirstOrDefaultAsync(p => p.Sku.ToUpper() == normalized, cancellationToken)
                     ?? throw new NotFoundException($"Product '{sku}' was not found.");
        db.Products.Remove(record);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Categories and brands with product counts, for pickers and the AI assistant.</summary>
    public async Task<CatalogFacets> FacetsAsync(CancellationToken cancellationToken = default)
    {
        var categories = await db.ProductCategories.AsNoTracking()
            .GroupBy(c => c.Category)
            .Select(g => new FacetValue(g.Key, g.Count()))
            .ToListAsync(cancellationToken);
        var brands = await db.Products.AsNoTracking()
            .Where(p => p.Brand != null)
            .GroupBy(p => p.Brand!)
            .Select(g => new FacetValue(g.Key, g.Count()))
            .ToListAsync(cancellationToken);
        var count = await db.Products.CountAsync(cancellationToken);
        return new CatalogFacets(
            categories.OrderByDescending(f => f.Count).ThenBy(f => f.Value, StringComparer.Ordinal).ToList(),
            brands.OrderByDescending(f => f.Count).ThenBy(f => f.Value, StringComparer.Ordinal).ToList(),
            count);
    }

    private static void Apply(ProductRecord record, ProductInput input, DateTime now)
    {
        record.Name = input.Name.Trim();
        record.Brand = string.IsNullOrWhiteSpace(input.Brand) ? null : input.Brand.Trim();
        record.Active = input.Active;
        record.AttributesJson = input.Attributes is { Count: > 0 } attributes ? JsonSerializer.Serialize(attributes) : null;
        record.UpdatedAt = now;

        var wanted = (input.Categories ?? []).Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        record.Categories.RemoveAll(c => !wanted.Contains(c.Category, StringComparer.OrdinalIgnoreCase));
        foreach (var category in wanted.Where(c => !record.Categories.Exists(x => string.Equals(x.Category, c, StringComparison.OrdinalIgnoreCase))))
        {
            record.Categories.Add(new ProductCategoryRecord { ProductId = record.Id, Category = category });
        }
    }

    private static IEnumerable<string> Validate(ProductInput input, string path)
    {
        if (string.IsNullOrWhiteSpace(input.Sku) || input.Sku.Trim().Length > 128)
        {
            yield return $"{path}.sku is required (max 128 characters).";
        }

        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 256)
        {
            yield return $"{path}.name is required (max 256 characters).";
        }

        if (input.Brand?.Length > 128 || (input.Categories ?? []).Any(c => c.Length > 128))
        {
            yield return $"{path}: brand and categories are limited to 128 characters.";
        }
    }

    private static Product ToModel(ProductRecord r) => new(
        r.Sku,
        r.Name,
        r.Brand,
        r.Categories.Select(c => c.Category).Order(StringComparer.Ordinal).ToList(),
        r.AttributesJson is null ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(r.AttributesJson)!,
        r.Active,
        new DateTimeOffset(r.UpdatedAt, TimeSpan.Zero));
}
