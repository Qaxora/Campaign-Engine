using CampaignEngine.Core.Carts;
using CampaignEngine.Core.Evaluation;
using CampaignEngine.Core.Serialization;
using CampaignEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Services;

public sealed class RedemptionResult
{
    public required string TransactionId { get; init; }

    public RedemptionStatus Status { get; init; }

    public bool Offline { get; init; }

    /// <summary>True when the transaction had already been recorded and the stored result is returned.</summary>
    public bool Replayed { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReversedAt { get; init; }

    public decimal TotalDiscount { get; init; }

    public List<RedeemedCampaign> Campaigns { get; init; } = [];

    /// <summary>The full evaluation result for online redemptions.</summary>
    public EvaluationResult? Evaluation { get; init; }
}

public sealed record RedeemedCampaign(Guid CampaignId, string CampaignCode, decimal Discount, string? CouponCode);

/// <summary>A sale that was priced locally (snapshot) while the channel was offline.</summary>
public sealed class OfflineRedemption
{
    public required string TransactionId { get; set; }

    public DateTimeOffset? Timestamp { get; set; }

    public string Currency { get; set; } = "TRY";

    public string Channel { get; set; } = "";

    public string? StoreId { get; set; }

    public string? CustomerId { get; set; }

    public List<OfflineCampaign> Campaigns { get; set; } = [];
}

public sealed class OfflineCampaign
{
    public required string CampaignCode { get; set; }

    public decimal Discount { get; set; }

    public string? CouponCode { get; set; }
}

/// <summary>
/// The redemption ledger. Confirms sales idempotently, enforces limits under concurrency and
/// releases usage on reversal.
/// </summary>
public sealed class RedemptionService(CampaignDbContext db, CatalogProvider catalog, PromotionEvaluator evaluator, TimeProvider time)
{
    private const int MaxAttempts = 5;

    /// <summary>
    /// Re-evaluates <paramref name="cart"/> and records the applied campaigns under
    /// <paramref name="transactionId"/>. Calling it again with the same id returns the stored result.
    /// </summary>
    public async Task<RedemptionResult> RedeemAsync(string transactionId, Cart cart, string client, CancellationToken cancellationToken = default)
    {
        ValidateTransactionId(transactionId);
        for (var attempt = 1; ; attempt++)
        {
            if (await FindAsync(transactionId, cancellationToken) is { } existing)
            {
                return existing;
            }

            try
            {
                var data = await catalog.GetAsync(cancellationToken);
                var usage = await UsageReader.ReadAsync(db, data.Campaigns, cart, cancellationToken);
                var result = evaluator.Evaluate(cart, data.Snapshot, usage);
                var applied = result.AppliedCampaigns
                    .Select(a => new RedeemedCampaign(a.CampaignId, a.Code, a.Discount, a.CouponCode))
                    .ToList();

                var transaction = NewTransaction(transactionId, client, offline: false, cart.Customer?.Id, cart.Channel, cart.StoreId,
                    cart.Currency, result.TotalDiscount, CampaignJson.Serialize(result), applied);
                await SaveWithCountersAsync(transaction, applied, cancellationToken);
                return ToResult(transaction, replayed: false);
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // A concurrent redemption changed a counter (limits must be re-checked) or recorded the
                // same transaction id (the next loop returns it). Start over with fresh data.
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Records a sale that was priced offline. Limits are not enforced: the sale already happened.</summary>
    public async Task<RedemptionResult> ImportOfflineAsync(OfflineRedemption sale, string client, CancellationToken cancellationToken = default)
    {
        ValidateTransactionId(sale.TransactionId);
        var codes = sale.Campaigns.Select(c => c.CampaignCode.ToUpperInvariant()).Distinct().ToList();
        var known = await db.Campaigns.AsNoTracking()
            .Where(c => codes.Contains(c.Code.ToUpper()))
            .ToDictionaryAsync(c => c.Code.ToUpperInvariant(), c => c.Id, cancellationToken);

        var errors = sale.Campaigns
            .Where(c => !known.ContainsKey(c.CampaignCode.ToUpperInvariant()))
            .Select(c => $"Campaign '{c.CampaignCode}' does not exist.")
            .Concat(sale.Campaigns.Where(c => c.Discount < 0).Select(c => $"Discount of '{c.CampaignCode}' cannot be negative."))
            .ToList();
        if (codes.Count != sale.Campaigns.Count)
        {
            errors.Add("Each campaign may appear only once per transaction.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        for (var attempt = 1; ; attempt++)
        {
            if (await FindAsync(sale.TransactionId, cancellationToken) is { } existing)
            {
                return existing;
            }

            try
            {
                var applied = sale.Campaigns
                    .Select(c => new RedeemedCampaign(known[c.CampaignCode.ToUpperInvariant()], c.CampaignCode, c.Discount, c.CouponCode))
                    .ToList();
                var transaction = NewTransaction(sale.TransactionId, client, offline: true, sale.CustomerId, sale.Channel, sale.StoreId,
                    sale.Currency, applied.Sum(a => a.Discount), CampaignJson.Serialize(sale), applied);
                if (sale.Timestamp is { } timestamp)
                {
                    transaction.CreatedAt = timestamp.UtcDateTime;
                }

                await SaveWithCountersAsync(transaction, applied, cancellationToken);
                return ToResult(transaction, replayed: false);
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Cancels a transaction (void, full return) and gives its usage and budget back.</summary>
    public async Task<RedemptionResult> ReverseAsync(string transactionId, string? reason, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var transaction = await db.Transactions.Include(t => t.Redemptions)
                                  .FirstOrDefaultAsync(t => t.TransactionId == transactionId, cancellationToken)
                              ?? throw new NotFoundException($"Transaction '{transactionId}' was not found.");
            if (transaction.Status == RedemptionStatus.Reversed)
            {
                return ToResult(transaction, replayed: true);
            }

            try
            {
                var now = time.GetUtcNow().UtcDateTime;
                transaction.Status = RedemptionStatus.Reversed;
                transaction.ReversedAt = now;
                transaction.ReverseReason = reason;
                foreach (var redemption in transaction.Redemptions)
                {
                    redemption.Status = RedemptionStatus.Reversed;
                }

                await UpdateCountersAsync(transaction.Redemptions.Select(r => (r.CampaignId, -1, -r.Discount)), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                return ToResult(transaction, replayed: false);
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<RedemptionResult?> FindAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await db.Transactions.AsNoTracking().Include(t => t.Redemptions)
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, cancellationToken);
        return transaction is null ? null : ToResult(transaction, replayed: true);
    }

    private TransactionRecord NewTransaction(
        string transactionId, string client, bool offline, string? customerId, string channel, string? storeId,
        string currency, decimal totalDiscount, string resultJson, List<RedeemedCampaign> applied)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var transaction = new TransactionRecord
        {
            TransactionId = transactionId,
            Client = client,
            Status = RedemptionStatus.Confirmed,
            Offline = offline,
            CustomerId = customerId,
            Channel = channel,
            StoreId = storeId,
            Currency = currency,
            TotalDiscount = totalDiscount,
            ResultJson = resultJson,
            CreatedAt = now,
        };
        transaction.Redemptions.AddRange(applied.Select(a => new RedemptionRecord
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            CampaignId = a.CampaignId,
            CampaignCode = a.CampaignCode,
            CustomerId = customerId,
            CouponCode = a.CouponCode is null ? null : UsageReader.NormalizeCoupon(a.CouponCode),
            Discount = a.Discount,
            Status = RedemptionStatus.Confirmed,
            CreatedAt = now,
        }));
        return transaction;
    }

    private async Task SaveWithCountersAsync(TransactionRecord transaction, List<RedeemedCampaign> applied, CancellationToken cancellationToken)
    {
        db.Transactions.Add(transaction);
        await UpdateCountersAsync(applied.Select(a => (a.CampaignId, 1, a.Discount)), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Changes the per-campaign counters. Each counter row carries a concurrency stamp, so two
    /// instances redeeming the same campaign at the same time cannot both pass a limit check:
    /// one of them fails on save and retries with fresh usage.
    /// </summary>
    private async Task UpdateCountersAsync(IEnumerable<(Guid CampaignId, int Redemptions, decimal Discount)> changes, CancellationToken cancellationToken)
    {
        foreach (var change in changes)
        {
            var counter = await db.CampaignUsage.FirstOrDefaultAsync(u => u.CampaignId == change.CampaignId, cancellationToken);
            if (counter is null)
            {
                counter = new CampaignUsageRecord { CampaignId = change.CampaignId };
                db.CampaignUsage.Add(counter);
            }

            counter.Redemptions = Math.Max(0, counter.Redemptions + change.Redemptions);
            counter.DiscountTotal = Math.Max(0, counter.DiscountTotal + change.Discount);
            counter.ConcurrencyStamp = Guid.NewGuid();
        }
    }

    private static RedemptionResult ToResult(TransactionRecord transaction, bool replayed) => new()
    {
        TransactionId = transaction.TransactionId,
        Status = transaction.Status,
        Offline = transaction.Offline,
        Replayed = replayed,
        CreatedAt = new DateTimeOffset(transaction.CreatedAt, TimeSpan.Zero),
        ReversedAt = transaction.ReversedAt is { } reversed ? new DateTimeOffset(reversed, TimeSpan.Zero) : null,
        TotalDiscount = transaction.TotalDiscount,
        Campaigns = transaction.Redemptions
            .OrderBy(r => r.CampaignCode, StringComparer.Ordinal)
            .Select(r => new RedeemedCampaign(r.CampaignId, r.CampaignCode, r.Discount, r.CouponCode))
            .ToList(),
        Evaluation = transaction.Offline ? null : CampaignJson.Deserialize<EvaluationResult>(transaction.ResultJson),
    };

    private static void ValidateTransactionId(string transactionId)
    {
        if (string.IsNullOrWhiteSpace(transactionId) || transactionId.Length > 128)
        {
            throw new ValidationException(["transactionId is required (max 128 characters)."]);
        }
    }
}
