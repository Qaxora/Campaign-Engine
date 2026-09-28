using CampaignEngine.Core.Carts;
using CampaignEngine.Core.Evaluation;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Platform;

namespace CampaignEngine.Infrastructure.Services;

/// <summary>Stateless pricing of a cart against the live catalog and current usage.</summary>
public sealed class EvaluationService(CampaignDbContext db, ITenantContext tenant, CatalogProvider catalog, PromotionEvaluator evaluator)
{
    public async Task<EvaluationResult> EvaluateAsync(Cart cart, bool explain, CancellationToken cancellationToken = default)
    {
        var data = await catalog.GetAsync(tenant.RequiredTenantId, cancellationToken);
        var usage = await UsageReader.ReadAsync(db, data.Campaigns, cart, cancellationToken);
        return evaluator.Evaluate(cart, data.Snapshot, usage, explain);
    }
}
