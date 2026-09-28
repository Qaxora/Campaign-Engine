using System.Globalization;
using System.Text;
using CampaignEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Webhooks;

public sealed class WebhookOptions
{
    public bool Enabled { get; set; } = true;

    public int PollSeconds { get; set; } = 5;

    public int BatchSize { get; set; } = 50;

    public int MaxAttempts { get; set; } = 10;

    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>Delivers outbox messages with retries and exponential backoff (10 s, 20 s, 40 s … capped at 1 h).</summary>
public sealed class WebhookDispatcher(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    IOptions<WebhookOptions> options,
    TimeProvider time,
    ILogger<WebhookDispatcher> logger) : BackgroundService
{
    public const string HttpClientName = "webhooks";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds)), time);
        do
        {
            try
            {
                while (await DispatchBatchAsync(stoppingToken) == options.Value.BatchSize)
                {
                    // A full batch means there may be more waiting.
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Webhook dispatch failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Sends due messages once. Returns how many were attempted.</summary>
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
        var now = time.GetUtcNow().UtcDateTime;

        var due = await db.OutboxMessages
            .Where(m => m.DeliveredAt == null && !m.Failed && m.NextAttemptAt <= now)
            .OrderBy(m => m.CreatedAt)
            .Take(options.Value.BatchSize)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var subscriptionIds = due.Select(m => m.SubscriptionId).Distinct().ToList();
        var subscriptions = await db.WebhookSubscriptions.AsNoTracking()
            .Where(s => subscriptionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        foreach (var message in due)
        {
            if (!subscriptions.TryGetValue(message.SubscriptionId, out var subscription) || !subscription.Active)
            {
                message.Failed = true;
                message.LastError = "Subscription is inactive.";
                continue;
            }

            await DeliverAsync(message, subscription, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return due.Count;
    }

    private async Task DeliverAsync(OutboxMessageRecord message, WebhookSubscriptionRecord subscription, CancellationToken cancellationToken)
    {
        message.Attempts++;
        try
        {
            var timestamp = time.GetUtcNow().ToUnixTimeSeconds();
            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
            {
                Content = new StringContent(message.Payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add(WebhookSigner.EventHeader, message.EventType);
            request.Headers.Add(WebhookSigner.DeliveryHeader, message.Id.ToString());
            request.Headers.Add(WebhookSigner.TimestampHeader, timestamp.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add(WebhookSigner.SignatureHeader, WebhookSigner.Sign(subscription.Secret, timestamp, message.Payload));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                message.DeliveredAt = time.GetUtcNow().UtcDateTime;
                message.LastError = null;
                return;
            }

            Fail(message, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Fail(message, ex.Message);
        }
    }

    private void Fail(OutboxMessageRecord message, string error)
    {
        message.LastError = error.Length > 1000 ? error[..1000] : error;
        if (message.Attempts >= options.Value.MaxAttempts)
        {
            message.Failed = true;
            logger.LogWarning("Webhook {MessageId} gave up after {Attempts} attempts: {Error}", message.Id, message.Attempts, error);
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Min(3600, 10 * Math.Pow(2, message.Attempts - 1)));
        message.NextAttemptAt = time.GetUtcNow().UtcDateTime + delay;
    }
}
