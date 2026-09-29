using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Conflicts;
using CampaignEngine.Core.Products;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Services;

public sealed record CampaignQuery(
    CampaignStatus? Status = null,
    string? Channel = null,
    string? Search = null,
    string? Tag = null,
    DateTimeOffset? ActiveFrom = null,
    DateTimeOffset? ActiveTo = null,
    CampaignSort Sort = CampaignSort.Updated,
    bool Descending = true,
    int Page = 1,
    int PageSize = 50);

public enum CampaignSort
{
    Updated,
    Priority,
    Name,
    Code,
    StartsAt,
    EndsAt,
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>A campaign after a change, with the conflicts it has with other live campaigns.</summary>
/// <param name="Warnings">Non-blocking remarks, e.g. store codes that are not registered.</param>
public sealed record CampaignChange(Campaign Campaign, IReadOnlyList<CampaignConflict> Conflicts, IReadOnlyList<string> Warnings);

/// <summary>Campaign management: CRUD, lifecycle and conflict analysis.</summary>
public sealed class CampaignService(
    CampaignDbContext db,
    ITenantContext tenant,
    CatalogProvider catalog,
    ConflictAnalyzer analyzer,
    StoreService stores,
    IChangeNotifier notifier,
    TimeProvider time)
{
    public async Task<PagedResult<Campaign>> ListAsync(CampaignQuery query, CancellationToken cancellationToken = default)
    {
        var q = db.Campaigns.AsNoTracking();
        if (query.Status is { } status)
        {
            q = q.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToUpperInvariant();
            q = q.Where(c => c.Code.ToUpper().Contains(term) || c.Name.ToUpper().Contains(term));
        }

        // A campaign runs in [from, to) when its schedule window overlaps it (open ends overlap everything).
        if (query.ActiveTo is { } activeTo)
        {
            var toUtc = activeTo.UtcDateTime;
            q = q.Where(c => c.StartsAt == null || c.StartsAt < toUtc);
        }

        if (query.ActiveFrom is { } activeFrom)
        {
            var fromUtc = activeFrom.UtcDateTime;
            q = q.Where(c => c.EndsAt == null || c.EndsAt > fromUtc);
        }

        // Channel and tag live inside the JSON definition, so they are filtered — and the page sorted — after loading.
        var records = await q.ToListAsync(cancellationToken);
        var campaigns = Sort(
                records.Select(r => r.ToDomain())
                    .Where(c => query.Channel is null || c.Channels.Count == 0 || c.Channels.Contains(query.Channel, StringComparer.OrdinalIgnoreCase))
                    .Where(c => query.Tag is null || c.Tags.Contains(query.Tag, StringComparer.OrdinalIgnoreCase)),
                query.Sort,
                query.Descending)
            .ToList();

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 500);
        return new PagedResult<Campaign>(campaigns.Skip((page - 1) * size).Take(size).ToList(), page, size, campaigns.Count);
    }

    /// <summary>Sorts by the chosen key; campaigns without a date sort last either way; code breaks ties.</summary>
    private static IEnumerable<Campaign> Sort(IEnumerable<Campaign> campaigns, CampaignSort sort, bool descending)
    {
        var ordered = sort switch
        {
            CampaignSort.Priority => descending ? campaigns.OrderByDescending(c => c.Priority) : campaigns.OrderBy(c => c.Priority),
            CampaignSort.Name => descending ? campaigns.OrderByDescending(c => c.Name, StringComparer.OrdinalIgnoreCase) : campaigns.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase),
            CampaignSort.Code => descending ? campaigns.OrderByDescending(c => c.Code, StringComparer.OrdinalIgnoreCase) : campaigns.OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase),
            CampaignSort.StartsAt => ByDate(campaigns, c => c.Schedule.StartsAt, descending),
            CampaignSort.EndsAt => ByDate(campaigns, c => c.Schedule.EndsAt, descending),
            _ => descending ? campaigns.OrderByDescending(c => c.UpdatedAt) : campaigns.OrderBy(c => c.UpdatedAt),
        };
        return ordered.ThenBy(c => c.Code, StringComparer.Ordinal);

        static IOrderedEnumerable<Campaign> ByDate(IEnumerable<Campaign> source, Func<Campaign, DateTimeOffset?> key, bool desc)
        {
            var withDate = source.OrderBy(c => key(c) is null ? 1 : 0);
            return desc ? withDate.ThenByDescending(key) : withDate.ThenBy(key);
        }
    }

    /// <summary>Finds a campaign by id or by code.</summary>
    public async Task<Campaign?> FindAsync(string idOrCode, CancellationToken cancellationToken = default) =>
        (await FindRecordAsync(idOrCode, tracking: false, cancellationToken))?.ToDomain();

    /// <summary>Creates a campaign. New campaigns are always drafts; use <see cref="ActivateAsync"/> to go live.</summary>
    public async Task<Campaign> CreateAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(campaign, cancellationToken);
        if (await CodeExistsAsync(campaign.Code, exceptId: null, cancellationToken))
        {
            throw new ConflictException($"A campaign with code '{campaign.Code}' already exists.");
        }

        var now = time.GetUtcNow();
        campaign.Id = campaign.Id == Guid.Empty ? Guid.NewGuid() : campaign.Id;
        campaign.Status = CampaignStatus.Draft;
        campaign.Version = 1;
        campaign.CreatedAt = now;
        campaign.UpdatedAt = now;

        var record = new CampaignRecord { Id = campaign.Id, Code = campaign.Code, Name = campaign.Name, Definition = "" };
        record.Apply(campaign);
        record.Version = 1;
        record.CreatedAt = record.UpdatedAt = now.UtcDateTime;
        db.Campaigns.Add(record);
        await notifier.CampaignChangedAsync(ChangeEvents.CampaignCreated, campaign, cancellationToken);
        await SaveAsync(cancellationToken);
        return campaign;
    }

    /// <summary>
    /// Replaces the definition. <paramref name="expectedVersion"/> must match the stored version.
    /// The status is not changed here; live campaigns are re-checked for blocking conflicts.
    /// </summary>
    public async Task<CampaignChange> UpdateAsync(Guid id, Campaign campaign, int expectedVersion, bool force, CancellationToken cancellationToken = default)
    {
        var record = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                     ?? throw new NotFoundException($"Campaign '{id}' was not found.");
        if (record.Version != expectedVersion)
        {
            throw new ConflictException($"Campaign was changed by someone else (version {record.Version}, you sent {expectedVersion}). Reload and retry.");
        }

        if (record.Status == CampaignStatus.Archived)
        {
            throw new ConflictException("Archived campaigns cannot be changed.");
        }

        await ValidateAsync(campaign, cancellationToken);
        if (await CodeExistsAsync(campaign.Code, exceptId: id, cancellationToken))
        {
            throw new ConflictException($"A campaign with code '{campaign.Code}' already exists.");
        }

        campaign.Id = id;
        campaign.Status = record.Status;
        var conflicts = record.Status is CampaignStatus.Active or CampaignStatus.Paused
            ? await CheckConflictsAsync(campaign, force, cancellationToken)
            : [];

        var saved = await SaveDefinitionAsync(record, campaign, ChangeEvents.CampaignUpdated, cancellationToken);
        return new CampaignChange(saved, conflicts, await stores.UnknownStoreWarningsAsync(saved, cancellationToken));
    }

    /// <summary>Makes a draft or paused campaign live. Refused on error-level conflicts unless forced.</summary>
    public async Task<CampaignChange> ActivateAsync(Guid id, bool force, CancellationToken cancellationToken = default)
    {
        var (record, campaign) = await LoadForTransitionAsync(id, cancellationToken);
        if (record.Status is not (CampaignStatus.Draft or CampaignStatus.Paused))
        {
            throw new ConflictException($"Only draft or paused campaigns can be activated (status is {record.Status}).");
        }

        var conflicts = await CheckConflictsAsync(campaign, force, cancellationToken);
        campaign.Status = CampaignStatus.Active;
        var activated = await SaveDefinitionAsync(record, campaign, ChangeEvents.CampaignActivated, cancellationToken);
        return new CampaignChange(activated, conflicts, await stores.UnknownStoreWarningsAsync(activated, cancellationToken));
    }

    public async Task<Campaign> PauseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var (record, campaign) = await LoadForTransitionAsync(id, cancellationToken);
        if (record.Status != CampaignStatus.Active)
        {
            throw new ConflictException($"Only active campaigns can be paused (status is {record.Status}).");
        }

        campaign.Status = CampaignStatus.Paused;
        return await SaveDefinitionAsync(record, campaign, ChangeEvents.CampaignPaused, cancellationToken);
    }

    public async Task<Campaign> ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var (record, campaign) = await LoadForTransitionAsync(id, cancellationToken);
        if (record.Status == CampaignStatus.Archived)
        {
            return campaign;
        }

        campaign.Status = CampaignStatus.Archived;
        return await SaveDefinitionAsync(record, campaign, ChangeEvents.CampaignArchived, cancellationToken);
    }

    /// <summary>Deletes a draft. Campaigns that were ever live are archived instead, to keep the ledger meaningful.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var (record, campaign) = await LoadForTransitionAsync(id, cancellationToken);
        if (record.Status != CampaignStatus.Draft)
        {
            throw new ConflictException("Only draft campaigns can be deleted; archive the campaign instead.");
        }

        db.Campaigns.Remove(record);
        await notifier.CampaignChangedAsync(ChangeEvents.CampaignDeleted, campaign, cancellationToken);
        await SaveAsync(cancellationToken);
    }

    /// <summary>Conflicts of a (possibly unsaved) campaign against the live catalog.</summary>
    public async Task<IReadOnlyList<CampaignConflict>> AnalyzeAsync(Campaign candidate, CancellationToken cancellationToken = default)
    {
        var data = await catalog.GetAsync(tenant.RequiredTenantId, cancellationToken);
        return analyzer.Analyze(candidate, data.Campaigns, await ListLookupAsync(candidate, data, cancellationToken));
    }

    /// <summary>All conflicts among live campaigns.</summary>
    public async Task<IReadOnlyList<CampaignConflict>> AnalyzeAllAsync(CancellationToken cancellationToken = default)
    {
        var data = await catalog.GetAsync(tenant.RequiredTenantId, cancellationToken);
        return analyzer.AnalyzeAll(data.Campaigns, data.Snapshot.Lists);
    }

    /// <summary>All conflicts among live campaigns, each with both campaigns described.</summary>
    public async Task<ConflictReport> ConflictReportAsync(CancellationToken cancellationToken = default)
    {
        var data = await catalog.GetAsync(tenant.RequiredTenantId, cancellationToken);
        return ConflictReportBuilder.Build(data.Campaigns, analyzer.AnalyzeAll(data.Campaigns, data.Snapshot.Lists), data.Version);
    }

    private async Task<IReadOnlyList<CampaignConflict>> CheckConflictsAsync(Campaign campaign, bool force, CancellationToken cancellationToken)
    {
        var conflicts = await AnalyzeAsync(campaign, cancellationToken);
        if (!force && conflicts.Any(c => c.Severity == ConflictSeverity.Error))
        {
            throw new ActivationBlockedException(conflicts);
        }

        return conflicts;
    }

    /// <summary>The catalog's lists plus any list the candidate references that is not live yet.</summary>
    private async Task<IProductListLookup> ListLookupAsync(Campaign candidate, CatalogData data, CancellationToken cancellationToken)
    {
        var known = data.ProductLists.Select(l => l.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = candidate.ReferencedProductLists().Where(code => !known.Contains(code)).ToList();
        if (missing.Count == 0)
        {
            return data.Snapshot.Lists;
        }

        var extra = await db.ProductLists.AsNoTracking().Include(l => l.Items)
            .Where(l => missing.Contains(l.Code))
            .ToListAsync(cancellationToken);
        return new ProductListIndex(data.ProductLists.Concat(extra.Select(l => l.ToDomain())));
    }

    private async Task ValidateAsync(Campaign campaign, CancellationToken cancellationToken)
    {
        var errors = CampaignValidator.Validate(campaign).ToList();
        var referenced = campaign.Reward is null ? [] : campaign.ReferencedProductLists().ToList();
        if (referenced.Count > 0)
        {
            var existing = await db.ProductLists.AsNoTracking()
                .Where(l => referenced.Contains(l.Code))
                .Select(l => l.Code)
                .ToListAsync(cancellationToken);
            errors.AddRange(referenced
                .Where(code => !existing.Contains(code, StringComparer.OrdinalIgnoreCase))
                .Select(code => $"Product list '{code}' does not exist."));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private async Task<(CampaignRecord Record, Campaign Campaign)> LoadForTransitionAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                     ?? throw new NotFoundException($"Campaign '{id}' was not found.");
        return (record, record.ToDomain());
    }

    private async Task<Campaign> SaveDefinitionAsync(CampaignRecord record, Campaign campaign, string eventType, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        campaign.Version = record.Version + 1;
        campaign.CreatedAt = new DateTimeOffset(record.CreatedAt, TimeSpan.Zero);
        campaign.UpdatedAt = now;
        record.Apply(campaign);
        record.Version = campaign.Version;
        record.UpdatedAt = now.UtcDateTime;
        await notifier.CampaignChangedAsync(eventType, campaign, cancellationToken);
        await SaveAsync(cancellationToken);
        return campaign;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The campaign was changed concurrently. Reload and retry.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ConflictException("A campaign with this code already exists.");
        }
        finally
        {
            catalog.Invalidate(tenant.RequiredTenantId);
        }
    }

    private async Task<CampaignRecord?> FindRecordAsync(string idOrCode, bool tracking, CancellationToken cancellationToken)
    {
        var q = tracking ? db.Campaigns : db.Campaigns.AsNoTracking();
        return Guid.TryParse(idOrCode, out var id)
            ? await q.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            : await q.FirstOrDefaultAsync(c => c.Code.ToUpper() == idOrCode.ToUpperInvariant(), cancellationToken);
    }

    /// <summary>Campaign codes are unique case-insensitively ("summer-10" and "SUMMER-10" would confuse cashiers).</summary>
    private Task<bool> CodeExistsAsync(string code, Guid? exceptId, CancellationToken cancellationToken)
    {
        var normalized = code.ToUpperInvariant();
        return db.Campaigns.AnyAsync(c => c.Code.ToUpper() == normalized && (exceptId == null || c.Id != exceptId.Value), cancellationToken);
    }

    internal static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException?.Message is { } message &&
        (message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
         || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
         || message.Contains("23505", StringComparison.Ordinal));
}
