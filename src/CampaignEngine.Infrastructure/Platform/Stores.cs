using System.Text.RegularExpressions;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Platform;

/// <summary>A physical store, warehouse or web site of the organization. Its code is what carts send as <c>storeId</c>.</summary>
public sealed class StoreRecord : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    /// <summary>Default sales channel of the store, e.g. <c>store</c> or <c>web</c>.</summary>
    public string? Channel { get; set; }

    public string? City { get; set; }

    public bool Active { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public sealed record Store(Guid Id, string Code, string Name, string? Channel, string? City, bool Active, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record StoreInput(string Code, string Name, string? Channel, string? City, bool Active = true);

public sealed partial class StoreService(CampaignDbContext db, TimeProvider time)
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$")]
    private static partial Regex CodePattern();

    public async Task<IReadOnlyList<Store>> ListAsync(bool? active = null, CancellationToken cancellationToken = default) =>
        (await db.Stores.AsNoTracking()
            .Where(s => active == null || s.Active == active)
            .OrderBy(s => s.Code)
            .ToListAsync(cancellationToken))
        .Select(ToModel)
        .ToList();

    public async Task<Store?> GetAsync(string code, CancellationToken cancellationToken = default) =>
        await FindAsync(code, tracking: false, cancellationToken) is { } record ? ToModel(record) : null;

    public async Task<Store> CreateAsync(StoreInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        if (await FindAsync(input.Code, tracking: false, cancellationToken) is not null)
        {
            throw new ConflictException($"A store with code '{input.Code}' already exists.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var record = new StoreRecord
        {
            Id = Guid.NewGuid(),
            Code = input.Code.Trim(),
            Name = input.Name.Trim(),
            Channel = Blank(input.Channel),
            City = Blank(input.City),
            Active = input.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Stores.Add(record);
        await SaveAsync(cancellationToken);
        return ToModel(record);
    }

    /// <summary>Updates a store. The code identifies the store in carts and cannot change.</summary>
    public async Task<Store> UpdateAsync(string code, StoreInput input, CancellationToken cancellationToken = default)
    {
        Validate(input with { Code = code });
        var record = await FindAsync(code, tracking: true, cancellationToken)
                     ?? throw new NotFoundException($"Store '{code}' was not found.");
        record.Name = input.Name.Trim();
        record.Channel = Blank(input.Channel);
        record.City = Blank(input.City);
        record.Active = input.Active;
        record.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await SaveAsync(cancellationToken);
        return ToModel(record);
    }

    public async Task DeleteAsync(string code, CancellationToken cancellationToken = default)
    {
        var record = await FindAsync(code, tracking: true, cancellationToken)
                     ?? throw new NotFoundException($"Store '{code}' was not found.");
        db.Stores.Remove(record);
        await SaveAsync(cancellationToken);
    }

    /// <summary>
    /// Store codes a campaign refers to that the organization has not registered. A warning, not an
    /// error: POS systems may start sending a store before it is registered here.
    /// </summary>
    public async Task<IReadOnlyList<string>> UnknownStoreWarningsAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        var referenced = campaign.Stores.Include.Concat(campaign.Stores.Exclude)
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();
        if (referenced.Count == 0)
        {
            return [];
        }

        var known = await db.Stores.AsNoTracking()
            .Where(s => referenced.Contains(s.Code.ToUpper()))
            .Select(s => s.Code.ToUpper())
            .ToListAsync(cancellationToken);
        return campaign.Stores.Include.Concat(campaign.Stores.Exclude)
            .Where(s => !known.Contains(s.Trim().ToUpperInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => $"Store '{s}' is not registered in this organization.")
            .ToList();
    }

    private Task<StoreRecord?> FindAsync(string code, bool tracking, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var query = tracking ? db.Stores : db.Stores.AsNoTracking();
        return query.FirstOrDefaultAsync(s => s.Code.ToUpper() == normalized, cancellationToken);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CampaignService.IsUniqueViolation(ex))
        {
            throw new ConflictException("A store with this code already exists.");
        }
    }

    private static void Validate(StoreInput input)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Code) || !CodePattern().IsMatch(input.Code.Trim()))
        {
            errors.Add("code must be 1-64 characters: letters, digits, '-', '_' or '.', starting with a letter or digit.");
        }

        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 128)
        {
            errors.Add("name is required (max 128 characters).");
        }

        if (input.Channel?.Length > 64 || input.City?.Length > 128)
        {
            errors.Add("channel (max 64) or city (max 128) is too long.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Store ToModel(StoreRecord r) => new(
        r.Id, r.Code, r.Name, r.Channel, r.City, r.Active,
        new DateTimeOffset(r.CreatedAt, TimeSpan.Zero), new DateTimeOffset(r.UpdatedAt, TimeSpan.Zero));
}
