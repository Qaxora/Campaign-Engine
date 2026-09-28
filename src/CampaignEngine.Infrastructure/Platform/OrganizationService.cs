using System.Text;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Platform;

public sealed record Organization(Guid Id, string Name, string Slug, Plan Plan, DateTimeOffset CreatedAt);

public sealed class OrganizationService(CampaignDbContext db, TimeProvider time)
{
    public async Task<Organization?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ToModel(await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken));

    public async Task<Organization?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        return ToModel(await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Slug == normalized, cancellationToken));
    }

    /// <summary>Creates an organization with a unique slug derived from the name (or the one given).</summary>
    public async Task<Organization> CreateAsync(string name, string? slug = null, Plan plan = Plan.Free, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 128)
        {
            throw new ValidationException(["organization name is required (max 128 characters)."]);
        }

        var baseSlug = Slugify(slug ?? name);
        var candidate = baseSlug;
        for (var i = 2; await db.Organizations.AnyAsync(o => o.Slug == candidate, cancellationToken); i++)
        {
            candidate = $"{baseSlug}-{i}";
        }

        var record = new OrganizationRecord
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = candidate,
            Plan = plan,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.Organizations.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return ToModel(record)!;
    }

    /// <summary>Returns the organization with this slug, creating it if needed (bootstrap / seeding).</summary>
    public async Task<Organization> EnsureAsync(string slug, string name, CancellationToken cancellationToken = default) =>
        await FindBySlugAsync(Slugify(slug), cancellationToken) ?? await CreateAsync(name, slug, cancellationToken: cancellationToken);

    public async Task<Organization> RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 128)
        {
            throw new ValidationException(["organization name is required (max 128 characters)."]);
        }

        var record = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
                     ?? throw new NotFoundException("Organization was not found.");
        record.Name = name.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return ToModel(record)!;
    }

    /// <summary>"Acme Perakende A.Ş." → "acme-perakende-a-s".</summary>
    public static string Slugify(string value)
    {
        var builder = new StringBuilder();
        foreach (var c in value.Trim().ToLowerInvariant())
        {
            var mapped = c switch
            {
                'ı' => 'i', 'ğ' => 'g', 'ü' => 'u', 'ş' => 's', 'ö' => 'o', 'ç' => 'c',
                _ => c,
            };
            builder.Append(mapped is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? mapped : '-');
        }

        var slug = string.Join('-', builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length > 48)
        {
            slug = slug[..48].TrimEnd('-');
        }

        return slug.Length == 0 ? "org" : slug;
    }

    private static Organization? ToModel(OrganizationRecord? r) =>
        r is null ? null : new Organization(r.Id, r.Name, r.Slug, r.Plan, new DateTimeOffset(r.CreatedAt, TimeSpan.Zero));
}
