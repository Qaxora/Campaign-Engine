using CampaignEngine.Api.Security;
using CampaignEngine.Core.Evaluation;
using CampaignEngine.Infrastructure.Services;
using Microsoft.Net.Http.Headers;

namespace CampaignEngine.Api.Endpoints;

public static class SnapshotEndpoints
{
    public static IEndpointRouteBuilder MapSnapshotEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/snapshot", async (HttpContext http, CatalogProvider catalog, CancellationToken ct) =>
            {
                var data = await catalog.GetAsync(ct);
                var etag = new EntityTagHeaderValue($"\"{data.Version}\"");
                http.Response.Headers.ETag = etag.ToString();
                http.Response.Headers.CacheControl = "no-cache";

                var ifNoneMatch = http.Request.GetTypedHeaders().IfNoneMatch;
                if (ifNoneMatch.Any(tag => tag.Compare(etag, useStrongComparison: false) || tag.Equals(EntityTagHeaderValue.Any)))
                {
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                }

                return Results.Ok(new SnapshotDocument
                {
                    Version = data.Version,
                    GeneratedAt = data.LoadedAt,
                    Campaigns = [.. data.Campaigns],
                    ProductLists = [.. data.ProductLists],
                });
            })
            .RequireAuthorization(Policies.Channel)
            .WithTags("Snapshot")
            .WithSummary("All live campaigns and the product lists they use")
            .WithDescription("For local/offline evaluation. Poll with If-None-Match: <etag>; the response is 304 while nothing changed. " +
                             "Contains active and paused campaigns that have not ended; paused ones never apply.")
            .Produces<SnapshotDocument>()
            .Produces(StatusCodes.Status304NotModified);

        return app;
    }
}
