using CampaignEngine.Api.Security;
using CampaignEngine.Infrastructure.Platform;

namespace CampaignEngine.Api.Endpoints;

public static class ApiKeyEndpoints
{
    public sealed record CreateApiKeyRequest(string Name, List<string> Scopes);

    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/api-keys")
            .WithTags("API keys")
            .RequireAuthorization(Policies.Manage);

        group.MapGet("/", async (ApiKeyService service, CancellationToken ct) => await service.ListAsync(ct))
            .WithSummary("List the organization's API keys (secrets are never returned)");

        group.MapPost("/", async (CreateApiKeyRequest request, ApiKeyService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request.Name, request.Scopes ?? [], ct);
                return Results.Created($"/api/v1/api-keys/{created.Id}", created);
            })
            .WithSummary("Create an API key")
            .WithDescription("Scopes: `channel` (evaluate, redeem, snapshot) and/or `admin` (management). " +
                             "The `key` field is only returned in this response — store it in the integration's secret store.")
            .Produces<CreatedApiKey>(StatusCodes.Status201Created);

        group.MapPost("/{id:guid}/revoke", async (Guid id, ApiKeyService service, CancellationToken ct) =>
                await service.RevokeAsync(id, ct))
            .WithSummary("Revoke an API key immediately");

        return app;
    }
}
