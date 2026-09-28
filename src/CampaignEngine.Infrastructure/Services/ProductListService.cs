using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Products;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Services;

public sealed record ProductListSummary(
    Guid Id, string Code, string Name, string? Description, ProductListKind Kind, int SkuCount, int Version, DateTimeOffset UpdatedAt);

/// <summary>
/// Product lists replace the "INSERT INTO campaign_products …" scripts: include lists, exclude lists
/// and global never-discount lists are all maintained here.
/// </summary>
public sealed class ProductListService(CampaignDbContext db, ITenantContext tenant, CatalogProvider catalog, IChangeNotifier notifier, TimeProvider time)
{
    public const int MaxSkusPerRequest = 100_000;

    public async Task<IReadOnlyList<ProductListSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.ProductLists.AsNoTracking()
            .OrderBy(l => l.Code)
            .Select(l => new ProductListSummary(
                l.Id, l.Code, l.Name, l.Description, l.Kind, l.Items.Count, l.Version, new DateTimeOffset(l.UpdatedAt, TimeSpan.Zero)))
            .ToListAsync(cancellationToken);

    public async Task<ProductList?> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        var record = await db.ProductLists.AsNoTracking().Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Code.ToUpper() == code.ToUpperInvariant(), cancellationToken);
        return record?.ToDomain();
    }

    public async Task<ProductList> CreateAsync(ProductList list, CancellationToken cancellationToken = default)
    {
        Validate(list.Code, list.Name, list.Skus);
        var normalized = list.Code.ToUpperInvariant();
        if (await db.ProductLists.AnyAsync(l => l.Code.ToUpper() == normalized, cancellationToken))
        {
            throw new ConflictException($"A product list with code '{list.Code}' already exists.");
        }

        var record = new ProductListRecord
        {
            Id = Guid.NewGuid(),
            Code = list.Code,
            Name = list.Name,
            Description = list.Description,
            Kind = list.Kind,
            Version = 1,
            UpdatedAt = time.GetUtcNow().UtcDateTime,
        };
        record.Items.AddRange(Clean(list.Skus).Select(sku => new ProductListItemRecord { ListId = record.Id, Sku = sku }));
        db.ProductLists.Add(record);
        return await SaveAsync(record, ChangeEvents.ProductListCreated, cancellationToken);
    }

    /// <summary>Updates name, description and kind. SKUs are changed with the item operations.</summary>
    public async Task<ProductList> UpdateAsync(string code, string name, string? description, ProductListKind kind, CancellationToken cancellationToken = default)
    {
        Validate(code, name, []);
        var record = await LoadAsync(code, cancellationToken);
        record.Name = name;
        record.Description = description;
        record.Kind = kind;
        return await SaveAsync(record, ChangeEvents.ProductListUpdated, cancellationToken);
    }

    public async Task<ProductList> AddSkusAsync(string code, IEnumerable<string> skus, CancellationToken cancellationToken = default)
    {
        var record = await LoadAsync(code, cancellationToken);
        var existing = record.Items.Select(i => i.Sku).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var sku in Clean(skus).Where(existing.Add))
        {
            record.Items.Add(new ProductListItemRecord { ListId = record.Id, Sku = sku });
        }

        return await SaveAsync(record, ChangeEvents.ProductListUpdated, cancellationToken);
    }

    public async Task<ProductList> RemoveSkusAsync(string code, IEnumerable<string> skus, CancellationToken cancellationToken = default)
    {
        var record = await LoadAsync(code, cancellationToken);
        var remove = Clean(skus).ToHashSet(StringComparer.OrdinalIgnoreCase);
        record.Items.RemoveAll(i => remove.Contains(i.Sku));
        return await SaveAsync(record, ChangeEvents.ProductListUpdated, cancellationToken);
    }

    /// <summary>Replaces all SKUs (e.g. a nightly export from the ERP).</summary>
    public async Task<ProductList> ReplaceSkusAsync(string code, IEnumerable<string> skus, CancellationToken cancellationToken = default)
    {
        var record = await LoadAsync(code, cancellationToken);
        var wanted = Clean(skus).ToHashSet(StringComparer.OrdinalIgnoreCase);
        record.Items.RemoveAll(i => !wanted.Contains(i.Sku));
        var existing = record.Items.Select(i => i.Sku).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var sku in wanted.Where(existing.Add))
        {
            record.Items.Add(new ProductListItemRecord { ListId = record.Id, Sku = sku });
        }

        return await SaveAsync(record, ChangeEvents.ProductListUpdated, cancellationToken);
    }

    /// <summary>Deletes a list unless a non-archived campaign still references it.</summary>
    public async Task DeleteAsync(string code, CancellationToken cancellationToken = default)
    {
        var record = await LoadAsync(code, cancellationToken);
        var users = (await db.Campaigns.AsNoTracking().Where(c => c.Status != CampaignStatus.Archived).ToListAsync(cancellationToken))
            .Select(c => c.ToDomain())
            .Where(c => c.ReferencedProductLists().Contains(record.Code, StringComparer.OrdinalIgnoreCase))
            .Select(c => c.Code)
            .ToList();
        if (users.Count > 0)
        {
            throw new ConflictException($"Product list '{record.Code}' is used by: {string.Join(", ", users)}.");
        }

        var list = record.ToDomain(includeSkus: false);
        db.ProductLists.Remove(record);
        await notifier.ProductListChangedAsync(ChangeEvents.ProductListDeleted, list, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        catalog.Invalidate(tenant.RequiredTenantId);
    }

    private async Task<ProductListRecord> LoadAsync(string code, CancellationToken cancellationToken) =>
        await db.ProductLists.Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Code.ToUpper() == code.ToUpperInvariant(), cancellationToken)
        ?? throw new NotFoundException($"Product list '{code}' was not found.");

    private async Task<ProductList> SaveAsync(ProductListRecord record, string eventType, CancellationToken cancellationToken)
    {
        if (db.Entry(record).State != EntityState.Added)
        {
            record.Version++;
            record.UpdatedAt = time.GetUtcNow().UtcDateTime;
        }

        var list = record.ToDomain();
        await notifier.ProductListChangedAsync(eventType, list, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException($"Product list '{record.Code}' was changed concurrently. Retry.");
        }
        finally
        {
            catalog.Invalidate(tenant.RequiredTenantId);
        }

        return list;
    }

    private static void Validate(string code, string name, IEnumerable<string> skus)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64)
        {
            errors.Add("code is required (max 64 characters).");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("name is required.");
        }

        if (skus.Count() > MaxSkusPerRequest)
        {
            errors.Add($"At most {MaxSkusPerRequest} SKUs per request.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static List<string> Clean(IEnumerable<string> skus)
    {
        var list = skus.Select(s => s?.Trim() ?? "").Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count > MaxSkusPerRequest)
        {
            throw new ValidationException([$"At most {MaxSkusPerRequest} SKUs per request."]);
        }

        var tooLong = list.FirstOrDefault(s => s.Length > 128);
        return tooLong is not null
            ? throw new ValidationException([$"SKU '{tooLong[..20]}…' is longer than 128 characters."])
            : list;
    }
}
