using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Products;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Infrastructure.Audit;

/// <summary>
/// Campaign and product list services already announce every change through
/// <see cref="IChangeNotifier"/> before saving. This decorator records those changes in the audit log
/// in the same unit of work and forwards them to the webhook outbox.
/// </summary>
public sealed class AuditingChangeNotifier(IChangeNotifier inner, AuditLog audit) : IChangeNotifier
{
    public Task CampaignChangedAsync(string eventType, Campaign campaign, CancellationToken cancellationToken)
    {
        var verb = eventType[(eventType.IndexOf('.', StringComparison.Ordinal) + 1)..];
        audit.Stage(eventType, AuditEntities.Campaign, campaign.Id.ToString(), campaign.Code,
            $"Campaign '{campaign.Name}' ({campaign.Code}) {verb}; status {campaign.Status.ToString().ToLowerInvariant()}, version {campaign.Version}.");
        return inner.CampaignChangedAsync(eventType, campaign, cancellationToken);
    }

    public Task ProductListChangedAsync(string eventType, ProductList list, CancellationToken cancellationToken)
    {
        var verb = eventType[(eventType.IndexOf('.', StringComparison.Ordinal) + 1)..];
        audit.Stage(eventType, AuditEntities.ProductList, list.Id.ToString(), list.Code,
            $"Product list '{list.Name}' ({list.Code}) {verb}; {list.Skus.Count} SKUs.");
        return inner.ProductListChangedAsync(eventType, list, cancellationToken);
    }
}
