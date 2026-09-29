using CampaignEngine.Api.Security;
using CampaignEngine.Infrastructure.Audit;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/audit", async (string? entityType, string? entityId, string? action, string? actorId, int? page, int? pageSize, AuditLog audit, CancellationToken ct) =>
                await audit.ListAsync(new AuditQuery(entityType, entityId, action, actorId, page ?? 1, pageSize ?? 50), ct))
            .WithTags("Audit")
            .RequireAuthorization(Policies.Read)
            .WithSummary("Who changed what, newest first")
            .WithDescription("Filter by `entityType` (campaign, productList, store, apiKey, webhook, member, organization) and `entityId` " +
                             "(campaign / list / key / webhook id, store code, user id), `action` (e.g. campaign.activated) or `actorId`.")
            .Produces<PagedResult<AuditEntry>>();
        return app;
    }
}
