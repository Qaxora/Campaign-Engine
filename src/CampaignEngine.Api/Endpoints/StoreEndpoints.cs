using CampaignEngine.Api.Security;
using CampaignEngine.Infrastructure.Platform;

namespace CampaignEngine.Api.Endpoints;

public static class StoreEndpoints
{
    public sealed record StoreRequest(string Code, string Name, string? Channel, string? City, bool? Active);

    public static IEndpointRouteBuilder MapStoreEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/stores")
            .WithTags("Stores")
            .RequireAuthorization(Policies.Read);

        group.MapGet("/", async (bool? active, StoreService service, CancellationToken ct) => await service.ListAsync(active, ct))
            .WithSummary("List stores");

        group.MapGet("/{code}", async Task<IResult> (string code, StoreService service, CancellationToken ct) =>
                await service.GetAsync(code, ct) is { } store ? Results.Ok(store) : Results.NotFound())
            .WithSummary("Get a store by code")
            .Produces<Store>();

        group.MapPost("/", async (StoreRequest request, StoreService service, CancellationToken ct) =>
            {
                var store = await service.CreateAsync(new StoreInput(request.Code, request.Name, request.Channel, request.City, request.Active ?? true), ct);
                return Results.Created($"/api/v1/stores/{Uri.EscapeDataString(store.Code)}", store);
            })
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Register a store")
            .WithDescription("The code is what POS carts send as `storeId` and what campaigns list under `stores`.")
            .Produces<Store>(StatusCodes.Status201Created);

        group.MapPut("/{code}", async (string code, StoreRequest request, StoreService service, CancellationToken ct) =>
                await service.UpdateAsync(code, new StoreInput(code, request.Name, request.Channel, request.City, request.Active ?? true), ct))
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Update a store (the code cannot change)");

        group.MapDelete("/{code}", async (string code, StoreService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(code, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Delete a store")
            .WithDescription("Campaigns that list the code keep it; consider deactivating instead.");

        return app;
    }
}
