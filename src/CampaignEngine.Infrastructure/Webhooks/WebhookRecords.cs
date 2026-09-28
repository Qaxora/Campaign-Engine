namespace CampaignEngine.Infrastructure.Webhooks;

public sealed class WebhookSubscriptionRecord
{
    public Guid Id { get; set; }

    public required string Url { get; set; }

    /// <summary>HMAC-SHA256 key for the <c>X-Campaign-Signature</c> header.</summary>
    public required string Secret { get; set; }

    /// <summary>Comma-separated event types, or <c>*</c> for all.</summary>
    public string Events { get; set; } = "*";

    public string? Description { get; set; }

    public bool Active { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}

/// <summary>A notification waiting to be delivered to one subscription.</summary>
public sealed class OutboxMessageRecord
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public required string EventType { get; set; }

    public required string Payload { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime NextAttemptAt { get; set; }

    public int Attempts { get; set; }

    public DateTime? DeliveredAt { get; set; }

    /// <summary>Set after the last allowed attempt failed; the message is not retried anymore.</summary>
    public bool Failed { get; set; }

    public string? LastError { get; set; }
}
