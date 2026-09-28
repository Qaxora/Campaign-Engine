using CampaignEngine.Api.Security;
using CampaignEngine.Infrastructure.Webhooks;

namespace CampaignEngine.Api.Endpoints;

public static class WebhookEndpoints
{
    public sealed record CreateWebhookRequest(string Url, List<string>? Events, string? Description, string? Secret);

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/webhooks")
            .WithTags("Webhooks")
            .RequireAuthorization(Policies.Manage);

        group.MapGet("/", async (WebhookService service, CancellationToken ct) => await service.ListAsync(ct))
            .WithSummary("List webhook subscriptions");

        group.MapPost("/", async (CreateWebhookRequest request, WebhookService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request.Url, request.Events, request.Description, request.Secret, ct);
                return Results.Created($"/api/v1/webhooks/{created.Id}", created);
            })
            .WithSummary("Subscribe a URL to change events")
            .WithDescription("Events: campaign.created|updated|activated|paused|archived|deleted, productList.created|updated|deleted, " +
                             "or wildcards '*' and 'campaign.*'. The signing secret is returned only in this response.")
            .Produces<CreatedWebhookSubscription>(StatusCodes.Status201Created);

        group.MapDelete("/{id:guid}", async (Guid id, WebhookService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithSummary("Delete a subscription");

        group.MapPost("/{id:guid}/ping", async (Guid id, WebhookService service, CancellationToken ct) =>
            {
                await service.PingAsync(id, ct);
                return Results.Accepted();
            })
            .WithSummary("Queue a test 'ping' event");

        group.MapGet("/{id:guid}/deliveries", async (Guid id, int? take, WebhookService service, CancellationToken ct) =>
                await service.DeliveriesAsync(id, take ?? 50, ct))
            .WithSummary("Recent deliveries and their status");

        return app;
    }
}
