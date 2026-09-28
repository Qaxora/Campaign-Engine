using CampaignEngine.Api.Http;
using CampaignEngine.Api.Security;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Conflicts;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Endpoints;

public static class CampaignEndpoints
{
    public sealed record ValidationResponse(bool Valid, IReadOnlyList<string> Errors);

    public static IEndpointRouteBuilder MapCampaignEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/campaigns")
            .WithTags("Campaigns")
            .RequireAuthorization(Policies.Read);

        group.MapGet("/", async (
                CampaignService service,
                string? status,
                string? channel,
                string? search,
                string? tag,
                int? page,
                int? pageSize,
                CancellationToken ct) =>
            await service.ListAsync(new CampaignQuery(QueryEnum.Parse<CampaignStatus>(status, "status"), channel, search, tag, page ?? 1, pageSize ?? 50), ct))
            .WithSummary("List campaigns")
            .WithDescription("Filters by status, channel (campaigns without channels match every channel), tag and a free-text search on code and name.");

        group.MapGet("/{idOrCode}", async Task<IResult> (string idOrCode, CampaignService service, CancellationToken ct) =>
                await service.FindAsync(idOrCode, ct) is { } campaign ? Results.Ok(campaign) : Results.NotFound())
            .WithSummary("Get a campaign by id or code")
            .Produces<Campaign>();

        group.MapPost("/", async (Campaign campaign, CampaignService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(campaign, ct);
                return Results.Created($"/api/v1/campaigns/{created.Id}", created);
            })
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Create a campaign (always as draft)")
            .Produces<Campaign>(StatusCodes.Status201Created);

        group.MapPut("/{id:guid}", async (Guid id, Campaign campaign, bool? force, CampaignService service, CancellationToken ct) =>
                await service.UpdateAsync(id, campaign, campaign.Version, force ?? false, ct))
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Replace a campaign definition")
            .WithDescription("Send the `version` you loaded; a different stored version returns 409. Live campaigns are re-checked for conflicts.");

        group.MapPost("/{id:guid}/activate", async (Guid id, bool? force, CampaignService service, CancellationToken ct) =>
                await service.ActivateAsync(id, force ?? false, ct))
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Activate a draft or paused campaign")
            .WithDescription("Returns the conflicts with other live campaigns. Error-level conflicts (e.g. duplicate coupon codes) block activation unless force=true.");

        group.MapPost("/{id:guid}/pause", async (Guid id, CampaignService service, CancellationToken ct) =>
                await service.PauseAsync(id, ct))
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Pause an active campaign");

        group.MapPost("/{id:guid}/archive", async (Guid id, CampaignService service, CancellationToken ct) =>
                await service.ArchiveAsync(id, ct))
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Archive a campaign (terminal)");

        group.MapDelete("/{id:guid}", async (Guid id, CampaignService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Delete a draft campaign");

        group.MapPost("/validate", (Campaign campaign) =>
            {
                var errors = CampaignValidator.Validate(campaign);
                return new ValidationResponse(errors.Count == 0, errors);
            })
            .WithSummary("Validate a definition without saving it");

        group.MapPost("/conflicts", async (Campaign campaign, CampaignService service, CancellationToken ct) =>
                await service.AnalyzeAsync(campaign, ct))
            .WithSummary("Conflicts of a (possibly unsaved) campaign with live campaigns")
            .Produces<IReadOnlyList<CampaignConflict>>();

        group.MapGet("/conflicts", async (CampaignService service, CancellationToken ct) =>
                await service.AnalyzeAllAsync(ct))
            .WithSummary("All conflicts among live campaigns")
            .Produces<IReadOnlyList<CampaignConflict>>();

        return app;
    }
}
