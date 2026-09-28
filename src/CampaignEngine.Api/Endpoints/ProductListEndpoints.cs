using CampaignEngine.Api.Http;
using CampaignEngine.Api.Security;
using CampaignEngine.Core.Products;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Endpoints;

public static class ProductListEndpoints
{
    public sealed record CreateProductListRequest(string Code, string Name, string? Description, ProductListKind Kind, List<string>? Skus);

    public sealed record UpdateProductListRequest(string Name, string? Description, ProductListKind Kind);

    public sealed record SkusRequest(List<string> Skus);

    public enum ImportMode
    {
        Replace,
        Append,
    }

    public static IEndpointRouteBuilder MapProductListEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/product-lists")
            .WithTags("Product lists")
            .RequireAuthorization(Policies.Read);

        group.MapGet("/", async (ProductListService service, CancellationToken ct) => await service.ListAsync(ct))
            .WithSummary("List product lists (without SKUs)");

        group.MapGet("/{code}", async Task<IResult> (string code, ProductListService service, CancellationToken ct) =>
                await service.GetAsync(code, ct) is { } list ? Results.Ok(list) : Results.NotFound())
            .WithSummary("Get a product list with its SKUs")
            .Produces<ProductList>();

        group.MapPost("/", async (CreateProductListRequest request, ProductListService service, CancellationToken ct) =>
            {
                var list = await service.CreateAsync(new ProductList
                {
                    Code = request.Code,
                    Name = request.Name,
                    Description = request.Description,
                    Kind = request.Kind,
                    Skus = new HashSet<string>(request.Skus ?? [], StringComparer.OrdinalIgnoreCase),
                }, ct);
                return Results.Created($"/api/v1/product-lists/{list.Code}", list);
            })
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Create a product list")
            .WithDescription("Use kind `globalExclusion` for products no campaign may ever discount (tobacco, gold, gift cards, …).")
            .Produces<ProductList>(StatusCodes.Status201Created);

        group.MapPut("/{code}", async (string code, UpdateProductListRequest request, ProductListService service, CancellationToken ct) =>
                await service.UpdateAsync(code, request.Name, request.Description, request.Kind, ct))
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Update name, description and kind");

        group.MapDelete("/{code}", async (string code, ProductListService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(code, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Delete a product list that no campaign uses");

        group.MapPost("/{code}/skus", async (string code, SkusRequest request, ProductListService service, CancellationToken ct) =>
                await service.AddSkusAsync(code, request.Skus, ct))
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Add SKUs");

        // POST instead of DELETE-with-body: several HTTP clients (e.g. Indy in older Delphi versions) cannot send a DELETE body.
        group.MapPost("/{code}/skus/remove", async (string code, SkusRequest request, ProductListService service, CancellationToken ct) =>
                await service.RemoveSkusAsync(code, request.Skus, ct))
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Remove SKUs");

        group.MapPut("/{code}/skus", async (string code, SkusRequest request, ProductListService service, CancellationToken ct) =>
                await service.ReplaceSkusAsync(code, request.Skus, ct))
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Replace all SKUs");

        group.MapPut("/{code}/skus/import", async (string code, string? mode, HttpRequest http, ProductListService service, CancellationToken ct) =>
            {
                var skus = await ReadSkusAsync(http, ct);
                return QueryEnum.Parse<ImportMode>(mode, "mode") == ImportMode.Append
                    ? await service.AddSkusAsync(code, skus, ct)
                    : await service.ReplaceSkusAsync(code, skus, ct);
            })
            .Accepts<string>("text/csv", "text/plain")
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Import SKUs from a CSV or text file")
            .WithDescription("One SKU per line; for CSV the first column is used and a header row named `sku` is skipped. " +
                             "mode=replace (default) or append. Example: `curl -X PUT --data-binary @skus.csv -H \"Content-Type: text/csv\" …`");

        return app;
    }

    private static async Task<List<string>> ReadSkusAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body);
        var skus = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var sku = line.Split([',', ';', '\t'], 2)[0].Trim().Trim('"');
            if (sku.Length == 0 || (skus.Count == 0 && sku.Equals("sku", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            skus.Add(sku);
            if (skus.Count > ProductListService.MaxSkusPerRequest)
            {
                throw new ValidationException([$"At most {ProductListService.MaxSkusPerRequest} SKUs per request."]);
            }
        }

        return skus;
    }
}
