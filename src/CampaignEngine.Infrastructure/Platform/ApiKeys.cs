using System.Security.Cryptography;
using System.Text;
using CampaignEngine.Infrastructure.Audit;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Platform;

/// <summary>What an API key may do.</summary>
public static class ApiKeyScopes
{
    /// <summary>Manage campaigns, product lists, stores and webhooks.</summary>
    public const string Admin = "admin";

    /// <summary>Evaluate carts, redeem, read the snapshot (POS, e-commerce).</summary>
    public const string Channel = "channel";

    public static IReadOnlyList<string> All { get; } = [Admin, Channel];
}

/// <summary>A credential of one integration. Only the SHA-256 hash of the secret is stored.</summary>
public sealed class ApiKeyRecord : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Name { get; set; }

    /// <summary>First characters of the key, safe to display (e.g. <c>qxc_Ab3dE</c>).</summary>
    public required string Prefix { get; set; }

    public required string KeyHash { get; set; }

    /// <summary>Comma-separated <see cref="ApiKeyScopes"/>.</summary>
    public required string Scopes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}

public sealed record ApiKey(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt);

/// <summary>Returned once, on creation: the only time the secret exists outside the caller.</summary>
public sealed record CreatedApiKey(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, string Key);

/// <summary>The identity behind a presented key.</summary>
public sealed record ApiKeyIdentity(Guid KeyId, Guid TenantId, string Name, IReadOnlyList<string> Scopes);

public sealed class ApiKeyService(CampaignDbContext db, ITenantContext tenant, AuditLog audit, TimeProvider time)
{
    private const string KeyPrefix = "qxc_";

    public async Task<IReadOnlyList<ApiKey>> ListAsync(CancellationToken cancellationToken = default) =>
        (await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync(cancellationToken))
        .Select(ToModel)
        .ToList();

    public Task<CreatedApiKey> CreateAsync(string name, IReadOnlyList<string> scopes, CancellationToken cancellationToken = default) =>
        CreateAsync(name, scopes, secret: null, cancellationToken);

    /// <summary>Creates a key. <paramref name="secret"/> is only for seeding well-known development keys.</summary>
    public async Task<CreatedApiKey> CreateAsync(string name, IReadOnlyList<string> scopes, string? secret, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 128)
        {
            errors.Add("name is required (max 128 characters).");
        }

        var normalized = scopes.Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList();
        if (normalized.Count == 0 || normalized.Exists(s => !ApiKeyScopes.All.Contains(s)))
        {
            errors.Add($"scopes must be one or more of: {string.Join(", ", ApiKeyScopes.All)}.");
        }

        if (secret is { Length: < 12 })
        {
            errors.Add("A fixed secret must be at least 12 characters.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var key = secret ?? KeyPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var record = new ApiKeyRecord
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Prefix = key[..Math.Min(12, key.Length)],
            KeyHash = Hash(key),
            Scopes = string.Join(',', normalized),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.ApiKeys.Add(record);
        audit.Stage("apiKey.created", AuditEntities.ApiKey, record.Id.ToString(), record.Name, $"API key {record.Name} ({record.Prefix}…) created with scopes {record.Scopes}.");
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedApiKey(record.Id, record.Name, record.Prefix, normalized, key);
    }

    public async Task<ApiKey> RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken)
                     ?? throw new NotFoundException($"API key '{id}' was not found.");
        if (record.RevokedAt is null)
        {
            record.RevokedAt = time.GetUtcNow().UtcDateTime;
            audit.Stage("apiKey.revoked", AuditEntities.ApiKey, record.Id.ToString(), record.Name, $"API key {record.Name} ({record.Prefix}…) revoked.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToModel(record);
    }

    /// <summary>
    /// Resolves a presented key. Runs before the tenant is known, so it is one of the few places that
    /// reads across tenants — by hash only, never listing keys.
    /// </summary>
    public async Task<ApiKeyIdentity?> AuthenticateAsync(string presentedKey, CancellationToken cancellationToken = default)
    {
        var hash = Hash(presentedKey);
        var record = await db.ApiKeys.IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.RevokedAt == null, cancellationToken);
        if (record is null)
        {
            return null;
        }

        // Record usage at most once a minute to avoid a write per request.
        var now = time.GetUtcNow().UtcDateTime;
        if (record.LastUsedAt is null || now - record.LastUsedAt > TimeSpan.FromMinutes(1))
        {
            if (tenant is TenantContext context)
            {
                context.Set(record.TenantId);
                record.LastUsedAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return new ApiKeyIdentity(record.Id, record.TenantId, record.Name, record.Scopes.Split(','));
    }

    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ApiKey ToModel(ApiKeyRecord r) => new(
        r.Id, r.Name, r.Prefix, r.Scopes.Split(','),
        new DateTimeOffset(r.CreatedAt, TimeSpan.Zero),
        r.LastUsedAt is { } used ? new DateTimeOffset(used, TimeSpan.Zero) : null,
        r.RevokedAt is { } revoked ? new DateTimeOffset(revoked, TimeSpan.Zero) : null);
}
