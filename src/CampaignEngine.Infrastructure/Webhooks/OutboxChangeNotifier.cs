using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Products;
using CampaignEngine.Core.Serialization;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Webhooks;

/// <summary>The body POSTed to webhook subscribers.</summary>
public sealed record WebhookEvent(Guid Id, string Type, DateTimeOffset OccurredAt, object Data);

/// <summary>Product list notifications carry metadata only; lists can be large. Fetch the list or the snapshot for SKUs.</summary>
public sealed record ProductListEventData(Guid Id, string Code, string Name, ProductListKind Kind, int SkuCount, int Version);

/// <summary>
/// Adds one outbox row per interested subscription to the current unit of work. The rows are saved
/// by the caller's <c>SaveChangesAsync</c>, so a notification exists if and only if the change does.
/// </summary>
public sealed class OutboxChangeNotifier(CampaignDbContext db, TimeProvider time) : IChangeNotifier
{
    public Task CampaignChangedAsync(string eventType, Campaign campaign, CancellationToken cancellationToken) =>
        EnqueueAsync(eventType, campaign, cancellationToken);

    public Task ProductListChangedAsync(string eventType, ProductList list, CancellationToken cancellationToken) =>
        EnqueueAsync(eventType, new ProductListEventData(list.Id, list.Code, list.Name, list.Kind, list.Skus.Count, list.Version), cancellationToken);

    public async Task EnqueueAsync(string eventType, object data, CancellationToken cancellationToken, Guid? onlySubscription = null)
    {
        var subscriptions = await db.WebhookSubscriptions.AsNoTracking()
            .Where(s => s.Active && (onlySubscription == null || s.Id == onlySubscription))
            .ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        foreach (var subscription in subscriptions.Where(s => Wants(s, eventType)))
        {
            var id = Guid.NewGuid();
            db.OutboxMessages.Add(new OutboxMessageRecord
            {
                Id = id,
                SubscriptionId = subscription.Id,
                EventType = eventType,
                Payload = CampaignJson.Serialize(new WebhookEvent(id, eventType, now, data)),
                CreatedAt = now.UtcDateTime,
                NextAttemptAt = now.UtcDateTime,
            });
        }
    }

    private static bool Wants(WebhookSubscriptionRecord subscription, string eventType) =>
        eventType == ChangeEvents.Ping
        || subscription.Events == "*"
        || subscription.Events.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(e => string.Equals(e, eventType, StringComparison.OrdinalIgnoreCase)
                      || (e.EndsWith(".*", StringComparison.Ordinal) && eventType.StartsWith(e[..^1], StringComparison.OrdinalIgnoreCase)));
}
