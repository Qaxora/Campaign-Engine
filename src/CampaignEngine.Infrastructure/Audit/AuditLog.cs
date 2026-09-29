using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Platform;
using CampaignEngine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Audit;

/// <summary>One recorded change: who did what to which entity.</summary>
public sealed class AuditEntryRecord : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary><c>user</c>, <c>apiKey</c> or <c>system</c>.</summary>
    public required string ActorType { get; set; }

    public string? ActorId { get; set; }

    public required string ActorName { get; set; }

    /// <summary>Dotted verb, e.g. <c>campaign.activated</c>, <c>store.created</c>, <c>apiKey.revoked</c>.</summary>
    public required string Action { get; set; }

    /// <summary><c>campaign</c>, <c>productList</c>, <c>store</c>, <c>apiKey</c>, <c>webhook</c>, <c>member</c>, <c>organization</c>.</summary>
    public required string EntityType { get; set; }

    public required string EntityId { get; set; }

    /// <summary>Human-readable identifier (campaign code, store code, email …).</summary>
    public string? EntityName { get; set; }

    public required string Summary { get; set; }

    public DateTime CreatedAt { get; set; }
}

public static class AuditEntities
{
    public const string Campaign = "campaign";
    public const string ProductList = "productList";
    public const string Store = "store";
    public const string ApiKey = "apiKey";
    public const string Webhook = "webhook";
    public const string Member = "member";
    public const string Organization = "organization";
}

/// <summary>Who is acting in the current scope. Set by the API from the authenticated caller.</summary>
public sealed class ActorContext
{
    public string Type { get; private set; } = "system";

    public string? Id { get; private set; }

    public string Name { get; private set; } = "system";

    public void Set(string type, string? id, string name)
    {
        Type = type;
        Id = id;
        Name = name;
    }
}

public sealed record AuditEntry(
    Guid Id, string ActorType, string? ActorId, string ActorName, string Action, string EntityType, string EntityId, string? EntityName, string Summary, DateTimeOffset CreatedAt);

public sealed record AuditQuery(string? EntityType = null, string? EntityId = null, string? Action = null, string? ActorId = null, int Page = 1, int PageSize = 50);

/// <summary>
/// Stages audit entries in the caller's unit of work, so an entry exists if and only if the change is
/// saved. Tenant stamping and filtering come from the DbContext like any other tenant-owned row.
/// </summary>
public sealed class AuditLog(CampaignDbContext db, ActorContext actor, TimeProvider time)
{
    public void Stage(string action, string entityType, string entityId, string? entityName, string summary) =>
        db.AuditEntries.Add(new AuditEntryRecord
        {
            Id = Guid.NewGuid(),
            ActorType = actor.Type,
            ActorId = actor.Id,
            ActorName = Truncate(actor.Name, 256)!,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            EntityName = Truncate(entityName, 256),
            Summary = Truncate(summary, 1024)!,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        });

    public async Task<PagedResult<AuditEntry>> ListAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var q = db.AuditEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            q = q.Where(e => e.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            q = q.Where(e => e.EntityId == query.EntityId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            q = q.Where(e => e.Action == query.Action);
        }

        if (!string.IsNullOrWhiteSpace(query.ActorId))
        {
            q = q.Where(e => e.ActorId == query.ActorId);
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        var total = await q.CountAsync(cancellationToken);
        var items = await q.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditEntry>(
            items.Select(e => new AuditEntry(e.Id, e.ActorType, e.ActorId, e.ActorName, e.Action, e.EntityType, e.EntityId, e.EntityName, e.Summary,
                new DateTimeOffset(e.CreatedAt, TimeSpan.Zero))).ToList(),
            page, size, total);
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
