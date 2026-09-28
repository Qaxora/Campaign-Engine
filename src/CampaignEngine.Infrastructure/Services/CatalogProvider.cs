using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Evaluation;
using CampaignEngine.Core.Products;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Services;

/// <summary>Live campaigns and the product lists they need, loaded together.</summary>
public sealed class CatalogData
{
    public required IReadOnlyList<Campaign> Campaigns { get; init; }

    public required IReadOnlyList<ProductList> ProductLists { get; init; }

    /// <summary>Hash of every campaign and list version: changes whenever the catalog changes.</summary>
    public required string Version { get; init; }

    public required DateTimeOffset LoadedAt { get; init; }

    public required CatalogSnapshot Snapshot { get; init; }
}

/// <summary>
/// Caches each tenant's catalog in memory. Local writes invalidate it immediately; other instances
/// pick up changes after <see cref="CatalogOptions.CacheSeconds"/>.
/// </summary>
public sealed class CatalogProvider(IServiceScopeFactory scopes, IOptions<CatalogOptions> options, TimeProvider time) : IDisposable
{
    private readonly ConcurrentDictionary<Guid, CatalogCacheEntry> _entries = new();

    public async Task<CatalogData> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var entry = _entries.GetOrAdd(tenantId, _ => new CatalogCacheEntry());
        var current = Volatile.Read(ref entry.Data);
        if (current is not null && !IsExpired(current))
        {
            return current;
        }

        await entry.Lock.WaitAsync(cancellationToken);
        try
        {
            current = entry.Data;
            if (current is not null && !IsExpired(current))
            {
                return current;
            }

            var generation = Interlocked.Read(ref entry.Generation);
            var loaded = await LoadAsync(tenantId, cancellationToken);

            // Do not cache a catalog that was invalidated while it was being loaded.
            if (generation == Interlocked.Read(ref entry.Generation))
            {
                Volatile.Write(ref entry.Data, loaded);
            }

            return loaded;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    public void Invalidate(Guid tenantId)
    {
        var entry = _entries.GetOrAdd(tenantId, _ => new CatalogCacheEntry());
        Interlocked.Increment(ref entry.Generation);
        Volatile.Write(ref entry.Data, null);
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Dispose();
        }
    }

    private bool IsExpired(CatalogData data) =>
        time.GetUtcNow() - data.LoadedAt > TimeSpan.FromSeconds(options.Value.CacheSeconds);

    private async Task<CatalogData> LoadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
        var now = time.GetUtcNow();
        var nowUtc = now.UtcDateTime;

        var records = await db.Campaigns.AsNoTracking()
            .Where(c => (c.Status == CampaignStatus.Active || c.Status == CampaignStatus.Paused)
                        && (c.EndsAt == null || c.EndsAt > nowUtc))
            .ToListAsync(cancellationToken);
        var campaigns = records.Select(r => r.ToDomain()).OrderBy(c => c.Code, StringComparer.Ordinal).ToList();

        var referenced = campaigns.SelectMany(c => c.ReferencedProductLists()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var listRecords = await db.ProductLists.AsNoTracking()
            .Include(l => l.Items)
            .Where(l => l.Kind == ProductListKind.GlobalExclusion || referenced.Contains(l.Code))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var lists = listRecords.Select(l => l.ToDomain()).OrderBy(l => l.Code, StringComparer.Ordinal).ToList();

        var version = ComputeVersion(campaigns, lists);
        return new CatalogData
        {
            Campaigns = campaigns,
            ProductLists = lists,
            Version = version,
            LoadedAt = now,
            Snapshot = new CatalogSnapshot(campaigns, new ProductListIndex(lists), version),
        };
    }

    private static string ComputeVersion(List<Campaign> campaigns, List<ProductList> lists)
    {
        var text = new StringBuilder();
        foreach (var c in campaigns)
        {
            text.Append('c').Append(c.Id).Append(':').Append(c.Version).Append(':').Append(c.Status).Append(';');
        }

        foreach (var l in lists)
        {
            text.Append('l').Append(l.Id).Append(':').Append(l.Version).Append(';');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }
}

internal sealed class CatalogCacheEntry : IDisposable
{
    public readonly SemaphoreSlim Lock = new(1, 1);
#pragma warning disable CA1051 // fields are needed for Volatile / Interlocked
    public CatalogData? Data;
    public long Generation;
#pragma warning restore CA1051

    public void Dispose() => Lock.Dispose();
}

public sealed class CatalogOptions
{
    /// <summary>How long a loaded catalog is reused. Matters only when several API instances share a database.</summary>
    public int CacheSeconds { get; set; } = 30;
}
