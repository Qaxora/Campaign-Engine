using CampaignEngine.Infrastructure.Audit;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Webhooks;

public sealed record WebhookSubscription(Guid Id, string Url, IReadOnlyList<string> Events, string? Description, bool Active, DateTimeOffset CreatedAt);

/// <summary>Returned once, on creation: the only time the secret is shown.</summary>
public sealed record CreatedWebhookSubscription(Guid Id, string Url, IReadOnlyList<string> Events, string? Description, string Secret);

public sealed record WebhookDelivery(
    Guid Id, string EventType, DateTimeOffset CreatedAt, int Attempts, DateTimeOffset? DeliveredAt, bool Failed, string? LastError);

public sealed class WebhookService(CampaignDbContext db, OutboxChangeNotifier outbox, AuditLog audit, TimeProvider time)
{
    public async Task<IReadOnlyList<WebhookSubscription>> ListAsync(CancellationToken cancellationToken = default) =>
        (await db.WebhookSubscriptions.AsNoTracking().OrderBy(s => s.CreatedAt).ToListAsync(cancellationToken))
        .Select(ToModel)
        .ToList();

    public async Task<CreatedWebhookSubscription> CreateAsync(
        string url, IReadOnlyList<string>? events, string? description, string? secret, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add("url must be an absolute http(s) URL.");
        }

        var eventList = events is { Count: > 0 } ? events.Select(e => e.Trim()).ToList() : ["*"];
        errors.AddRange(eventList
            .Where(e => e != "*" && !e.EndsWith(".*", StringComparison.Ordinal) && !ChangeEvents.All.Contains(e, StringComparer.OrdinalIgnoreCase))
            .Select(e => $"Unknown event '{e}'. Known: {string.Join(", ", ChangeEvents.All)}, or '*' / 'campaign.*'."));
        if (secret is { Length: < 16 })
        {
            errors.Add("secret must be at least 16 characters (or omit it to generate one).");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var record = new WebhookSubscriptionRecord
        {
            Id = Guid.NewGuid(),
            Url = url,
            Secret = secret ?? WebhookSigner.NewSecret(),
            Events = string.Join(',', eventList),
            Description = description,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.WebhookSubscriptions.Add(record);
        audit.Stage("webhook.created", AuditEntities.Webhook, record.Id.ToString(), record.Url, $"Webhook to {record.Url} created for {record.Events}.");
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedWebhookSubscription(record.Id, record.Url, eventList, record.Description, record.Secret);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                     ?? throw new NotFoundException($"Webhook subscription '{id}' was not found.");
        db.WebhookSubscriptions.Remove(record);
        audit.Stage("webhook.deleted", AuditEntities.Webhook, record.Id.ToString(), record.Url, $"Webhook to {record.Url} deleted.");
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Queues a <c>ping</c> event for one subscription, to test the receiver.</summary>
    public async Task PingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.WebhookSubscriptions.AnyAsync(s => s.Id == id && s.Active, cancellationToken))
        {
            throw new NotFoundException($"Active webhook subscription '{id}' was not found.");
        }

        await outbox.EnqueueAsync(ChangeEvents.Ping, new { message = "pong" }, cancellationToken, onlySubscription: id);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookDelivery>> DeliveriesAsync(Guid id, int take, CancellationToken cancellationToken = default) =>
        (await db.OutboxMessages.AsNoTracking()
            .Where(m => m.SubscriptionId == id)
            .OrderByDescending(m => m.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken))
        .Select(m => new WebhookDelivery(
            m.Id,
            m.EventType,
            new DateTimeOffset(m.CreatedAt, TimeSpan.Zero),
            m.Attempts,
            m.DeliveredAt is { } delivered ? new DateTimeOffset(delivered, TimeSpan.Zero) : null,
            m.Failed,
            m.LastError))
        .ToList();

    private static WebhookSubscription ToModel(WebhookSubscriptionRecord r) =>
        new(r.Id, r.Url, r.Events.Split(',', StringSplitOptions.RemoveEmptyEntries), r.Description, r.Active, new DateTimeOffset(r.CreatedAt, TimeSpan.Zero));
}
