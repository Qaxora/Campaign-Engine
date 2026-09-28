namespace CampaignEngine.Infrastructure.Platform;

/// <summary>Marks a row that belongs to one organization. See ADR 0005.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

/// <summary>The organization the current request (or background job) acts for.</summary>
public interface ITenantContext
{
    /// <summary>Null when no tenant has been established (e.g. before authentication).</summary>
    Guid? TenantId { get; }

    /// <summary>
    /// True only for platform jobs that deliberately work across tenants (webhook delivery, seeding).
    /// Such code must also use <c>IgnoreQueryFilters()</c> explicitly.
    /// </summary>
    bool IsSystem { get; }

    /// <summary>The tenant id, or an exception when none is set.</summary>
    Guid RequiredTenantId => TenantId ?? throw new TenantRequiredException();
}

/// <summary>Scoped, mutable implementation set by authentication.</summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    public bool IsSystem { get; private set; }

    public void Set(Guid tenantId)
    {
        if (TenantId is { } current && current != tenantId)
        {
            throw new InvalidOperationException("The tenant of a scope cannot change once set.");
        }

        TenantId = tenantId;
    }

    public void RunAsSystem() => IsSystem = true;
}

public sealed class TenantRequiredException()
    : InvalidOperationException("This operation needs a tenant (organization) context.");

public enum Plan
{
    Free,
    Growth,
    Enterprise,
}

/// <summary>A customer of the platform. It is the tenant itself, so it is not tenant-owned.</summary>
public sealed class OrganizationRecord
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>URL-friendly unique name, e.g. <c>acme-retail</c>.</summary>
    public required string Slug { get; set; }

    public Plan Plan { get; set; } = Plan.Free;

    public DateTime CreatedAt { get; set; }
}
