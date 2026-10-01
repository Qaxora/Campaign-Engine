using CampaignEngine.Core.Campaigns;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Ledger;

public sealed record RedemptionQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    RedemptionStatus? Status = null,
    string? CampaignCode = null,
    string? CouponCode = null,
    string? CustomerId = null,
    int Page = 1,
    int PageSize = 50);

public sealed record RedemptionRow(
    string TransactionId,
    Guid CampaignId,
    string CampaignCode,
    string? CampaignName,
    string? CouponCode,
    string? CustomerId,
    string Channel,
    string? StoreId,
    string Currency,
    bool Offline,
    decimal Discount,
    RedemptionStatus Status,
    DateTimeOffset SoldAt);

public sealed record TransactionQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    RedemptionStatus? Status = null,
    string? Channel = null,
    string? StoreId = null,
    string? CustomerId = null,
    string? CampaignCode = null,
    bool? Offline = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50);

public sealed record LedgerTransaction(
    string TransactionId,
    RedemptionStatus Status,
    bool Offline,
    string Client,
    string Channel,
    string? StoreId,
    string? CustomerId,
    string Currency,
    decimal TotalDiscount,
    int CampaignCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReversedAt);

public sealed record LedgerRedemption(
    Guid CampaignId, string CampaignCode, string? CampaignName, string? CouponCode, decimal Discount, RedemptionStatus Status, DateTimeOffset CreatedAt);

public sealed record LedgerTransactionDetail(LedgerTransaction Transaction, string? ReverseReason, IReadOnlyList<LedgerRedemption> Redemptions);

/// <summary>Consumption of a campaign against its limits. Reversed transactions are not counted.</summary>
public sealed record CampaignUsage(
    Guid CampaignId,
    string Code,
    string Name,
    CampaignStatus Status,
    int Redemptions,
    decimal DiscountTotal,
    int? MaxRedemptions,
    decimal? Budget,
    decimal? BudgetRemaining,
    int ReversedRedemptions,
    DateTimeOffset? LastRedeemedAt);

public sealed record CustomerCampaignUsage(string CampaignCode, string? CampaignName, int Redemptions, decimal Discount, int? MaxRedemptionsPerCustomer, DateTimeOffset LastRedeemedAt);

public sealed record CustomerUsage(string CustomerId, int Transactions, decimal TotalDiscount, IReadOnlyList<CustomerCampaignUsage> Campaigns);

/// <summary>
/// Read side of the redemption ledger (transactions, redemptions, usage). Writes stay in
/// <see cref="RedemptionService"/>, which owns idempotency and concurrency. Every query is
/// tenant-filtered by the DbContext.
/// </summary>
public sealed class LedgerService(CampaignDbContext db)
{
    public async Task<PagedResult<LedgerTransaction>> ListTransactionsAsync(TransactionQuery query, CancellationToken cancellationToken = default)
    {
        var q = db.Transactions.AsNoTracking().AsQueryable();
        if (query.From is { } from)
        {
            var fromUtc = from.UtcDateTime;
            q = q.Where(t => t.CreatedAt >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.UtcDateTime;
            q = q.Where(t => t.CreatedAt < toUtc);
        }

        if (query.Status is { } status)
        {
            q = q.Where(t => t.Status == status);
        }

        if (query.Offline is { } offline)
        {
            q = q.Where(t => t.Offline == offline);
        }

        if (!string.IsNullOrWhiteSpace(query.Channel))
        {
            var channel = query.Channel.Trim().ToUpperInvariant();
            q = q.Where(t => t.Channel.ToUpper() == channel);
        }

        if (!string.IsNullOrWhiteSpace(query.StoreId))
        {
            var store = query.StoreId.Trim().ToUpperInvariant();
            q = q.Where(t => t.StoreId != null && t.StoreId.ToUpper() == store);
        }

        if (!string.IsNullOrWhiteSpace(query.CustomerId))
        {
            var customer = query.CustomerId.Trim();
            q = q.Where(t => t.CustomerId == customer);
        }

        if (!string.IsNullOrWhiteSpace(query.CampaignCode))
        {
            var code = query.CampaignCode.Trim().ToUpperInvariant();
            q = q.Where(t => t.Redemptions.Any(r => r.CampaignCode.ToUpper() == code));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToUpperInvariant();
            q = q.Where(t => t.TransactionId.ToUpper().Contains(term));
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        var total = await q.CountAsync(cancellationToken);
        var items = await q.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.TransactionId)
            .Skip((page - 1) * size).Take(size)
            .Select(t => new { Record = t, Count = t.Redemptions.Count })
            .ToListAsync(cancellationToken);
        return new PagedResult<LedgerTransaction>(items.Select(x => ToModel(x.Record, x.Count)).ToList(), page, size, total);
    }

    /// <summary>One row per campaign applied in a sale, newest first, dated by the sale (offline imports included).</summary>
    public async Task<PagedResult<RedemptionRow>> ListRedemptionsAsync(RedemptionQuery query, CancellationToken cancellationToken = default)
    {
        var q = db.Redemptions.AsNoTracking()
            .Join(db.Transactions, r => r.TransactionRecordId, t => t.Id, (r, t) => new { r, t });
        if (query.From is { } from)
        {
            var fromUtc = from.UtcDateTime;
            q = q.Where(x => x.t.CreatedAt >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.UtcDateTime;
            q = q.Where(x => x.t.CreatedAt < toUtc);
        }

        if (query.Status is { } status)
        {
            q = q.Where(x => x.r.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.CampaignCode))
        {
            var code = query.CampaignCode.Trim().ToUpperInvariant();
            q = q.Where(x => x.r.CampaignCode.ToUpper() == code);
        }

        if (!string.IsNullOrWhiteSpace(query.CouponCode))
        {
            var coupon = query.CouponCode.Trim().ToUpperInvariant();
            q = q.Where(x => x.r.CouponCode == coupon);
        }

        if (!string.IsNullOrWhiteSpace(query.CustomerId))
        {
            var customer = query.CustomerId.Trim();
            q = q.Where(x => x.r.CustomerId == customer);
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        var total = await q.CountAsync(cancellationToken);
        var rows = await q.OrderByDescending(x => x.t.CreatedAt).ThenBy(x => x.r.CampaignCode)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new { x.r, x.t.Channel, x.t.StoreId, x.t.Currency, x.t.Offline, SoldAt = x.t.CreatedAt })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(x => x.r.CampaignId).Distinct().ToList();
        var names = await db.Campaigns.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        return new PagedResult<RedemptionRow>(
            rows.Select(x => new RedemptionRow(
                x.r.TransactionId, x.r.CampaignId, x.r.CampaignCode, names.GetValueOrDefault(x.r.CampaignId), x.r.CouponCode, x.r.CustomerId,
                x.Channel, x.StoreId, x.Currency, x.Offline, x.r.Discount, x.r.Status, Utc(x.SoldAt))).ToList(),
            page, size, total);
    }

    public async Task<LedgerTransactionDetail?> GetTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        var record = await db.Transactions.AsNoTracking().Include(t => t.Redemptions)
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var campaignIds = record.Redemptions.Select(r => r.CampaignId).Distinct().ToList();
        var names = await db.Campaigns.AsNoTracking().Where(c => campaignIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var redemptions = record.Redemptions
            .OrderBy(r => r.CampaignCode, StringComparer.Ordinal)
            .Select(r => new LedgerRedemption(r.CampaignId, r.CampaignCode, names.GetValueOrDefault(r.CampaignId), r.CouponCode, r.Discount, r.Status, Utc(r.CreatedAt)))
            .ToList();
        return new LedgerTransactionDetail(ToModel(record, record.Redemptions.Count), record.ReverseReason, redemptions);
    }

    /// <summary>Usage per campaign, including campaigns that were never redeemed.</summary>
    public async Task<IReadOnlyList<CampaignUsage>> CampaignUsageAsync(CampaignStatus? status = null, CancellationToken cancellationToken = default) =>
        await CampaignUsageAsync(status, campaignId: null, cancellationToken);

    public async Task<CampaignUsage?> CampaignUsageAsync(Guid campaignId, CancellationToken cancellationToken = default) =>
        (await CampaignUsageAsync(null, campaignId, cancellationToken)).SingleOrDefault();

    private async Task<IReadOnlyList<CampaignUsage>> CampaignUsageAsync(CampaignStatus? status, Guid? campaignId, CancellationToken cancellationToken)
    {
        var campaigns = await db.Campaigns.AsNoTracking()
            .Where(c => (status == null || c.Status == status) && (campaignId == null || c.Id == campaignId))
            .ToListAsync(cancellationToken);
        var ids = campaigns.Select(c => c.Id).ToList();

        // Aggregated in memory: SQLite cannot sum or order decimals server-side, and per-tenant
        // redemption counts are modest. Totals come from the ledger, not from the usage counters,
        // so the view stays correct even if a counter drifted.
        var redemptions = await db.Redemptions.AsNoTracking()
            .Where(r => ids.Contains(r.CampaignId))
            .Select(r => new { r.CampaignId, r.Status, r.Discount, r.CreatedAt })
            .ToListAsync(cancellationToken);
        var byCampaign = redemptions.ToLookup(r => r.CampaignId);

        return campaigns
            .Select(record =>
            {
                var limits = record.ToDomain().Limits;
                var confirmed = byCampaign[record.Id].Where(r => r.Status == RedemptionStatus.Confirmed).ToList();
                var discount = confirmed.Sum(r => r.Discount);
                return new CampaignUsage(
                    record.Id,
                    record.Code,
                    record.Name,
                    record.Status,
                    confirmed.Count,
                    discount,
                    limits.MaxRedemptions,
                    limits.Budget,
                    limits.Budget is { } budget ? Math.Max(0, budget - discount) : null,
                    byCampaign[record.Id].Count(r => r.Status == RedemptionStatus.Reversed),
                    confirmed.Count == 0 ? null : Utc(confirmed.Max(r => r.CreatedAt)));
            })
            .OrderByDescending(u => u.DiscountTotal)
            .ThenBy(u => u.Code, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<CustomerUsage> CustomerUsageAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var redemptions = await db.Redemptions.AsNoTracking()
            .Where(r => r.CustomerId == customerId && r.Status == RedemptionStatus.Confirmed)
            .Select(r => new { r.CampaignId, r.CampaignCode, r.TransactionRecordId, r.Discount, r.CreatedAt })
            .ToListAsync(cancellationToken);
        var ids = redemptions.Select(r => r.CampaignId).Distinct().ToList();
        var campaigns = (await db.Campaigns.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken))
            .ToDictionary(c => c.Id);

        var perCampaign = redemptions
            .GroupBy(r => r.CampaignId)
            .Select(g =>
            {
                var campaign = campaigns.GetValueOrDefault(g.Key);
                return new CustomerCampaignUsage(
                    g.First().CampaignCode,
                    campaign?.Name,
                    g.Count(),
                    g.Sum(r => r.Discount),
                    campaign?.ToDomain().Limits.MaxRedemptionsPerCustomer,
                    Utc(g.Max(r => r.CreatedAt)));
            })
            .OrderByDescending(u => u.LastRedeemedAt)
            .ToList();
        return new CustomerUsage(customerId, redemptions.Select(r => r.TransactionRecordId).Distinct().Count(), redemptions.Sum(r => r.Discount), perCampaign);
    }

    private static LedgerTransaction ToModel(TransactionRecord t, int campaignCount) => new(
        t.TransactionId, t.Status, t.Offline, t.Client, t.Channel, t.StoreId, t.CustomerId, t.Currency, t.TotalDiscount, campaignCount,
        Utc(t.CreatedAt), t.ReversedAt is { } reversed ? Utc(reversed) : null);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
}
