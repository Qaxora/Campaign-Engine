using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Carts;
using CampaignEngine.Core.Evaluation;
using CampaignEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Services;

/// <summary>Reads the ledger into a <see cref="UsageSnapshot"/> for the campaigns that have limits.</summary>
public static class UsageReader
{
    public static async Task<UsageSnapshot> ReadAsync(
        CampaignDbContext db, IEnumerable<Campaign> campaigns, Cart cart, CancellationToken cancellationToken)
    {
        var limited = campaigns
            .Where(c => c.Limits.MaxRedemptions is not null
                        || c.Limits.Budget is not null
                        || c.Limits.MaxRedemptionsPerCustomer is not null
                        || c.Coupon?.MaxUsesPerCode is not null)
            .ToList();
        if (limited.Count == 0)
        {
            return UsageSnapshot.Empty;
        }

        var ids = limited.Select(c => c.Id).ToList();
        var totals = await db.CampaignUsage.AsNoTracking()
            .Where(u => ids.Contains(u.CampaignId))
            .ToDictionaryAsync(u => u.CampaignId, cancellationToken);

        var customerId = cart.Customer?.Id;
        var perCustomer = new Dictionary<Guid, int>();
        var customerIds = limited.Where(c => c.Limits.MaxRedemptionsPerCustomer is not null).Select(c => c.Id).ToList();
        if (customerId is not null && customerIds.Count > 0)
        {
            perCustomer = await db.Redemptions.AsNoTracking()
                .Where(r => customerIds.Contains(r.CampaignId) && r.CustomerId == customerId && r.Status == RedemptionStatus.Confirmed)
                .GroupBy(r => r.CampaignId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        }

        var coupons = cart.CouponCodes.Select(NormalizeCoupon).Distinct().ToList();
        var couponIds = limited.Where(c => c.Coupon?.MaxUsesPerCode is not null).Select(c => c.Id).ToList();
        var perCoupon = new Dictionary<Guid, Dictionary<string, int>>();
        if (coupons.Count > 0 && couponIds.Count > 0)
        {
            var rows = await db.Redemptions.AsNoTracking()
                .Where(r => couponIds.Contains(r.CampaignId) && r.CouponCode != null && coupons.Contains(r.CouponCode) &&
                            r.Status == RedemptionStatus.Confirmed)
                .GroupBy(r => new { r.CampaignId, r.CouponCode })
                .Select(g => new { g.Key.CampaignId, g.Key.CouponCode, Count = g.Count() })
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                if (!perCoupon.TryGetValue(row.CampaignId, out var map))
                {
                    perCoupon[row.CampaignId] = map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                }

                map[row.CouponCode!] = row.Count;
            }
        }

        var usage = new Dictionary<Guid, CampaignUsage>();
        foreach (var id in ids)
        {
            var total = totals.GetValueOrDefault(id);
            usage[id] = new CampaignUsage
            {
                Redemptions = total?.Redemptions ?? 0,
                DiscountTotal = total?.DiscountTotal ?? 0,
                CustomerRedemptions = perCustomer.GetValueOrDefault(id),
                CouponRedemptions = perCoupon.GetValueOrDefault(id) ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            };
        }

        return new UsageSnapshot(usage);
    }

    public static string NormalizeCoupon(string code) => code.Trim().ToUpperInvariant();
}
