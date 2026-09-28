using CampaignEngine.Api.Security;
using CampaignEngine.Infrastructure.Catalog;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Endpoints;

public static class ProductEndpoints
{
    public sealed record ProductRequest(string? Name, string? Brand, List<string>? Categories, Dictionary<string, string>? Attributes, bool? Active);

    public sealed record BulkProduct(string Sku, string Name, string? Brand, List<string>? Categories, Dictionary<string, string>? Attributes, bool? Active);

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/products")
            .WithTags("Products")
            .RequireAuthorization(Policies.Read);

        group.MapGet("/", async (string? search, string? category, string? brand, bool? active, int? page, int? pageSize, ProductService service, CancellationToken ct) =>
                await service.SearchAsync(new ProductQuery(search, category, brand, active, page ?? 1, pageSize ?? 50), ct))
            .WithSummary("Search the product catalog")
            .Produces<PagedResult<Product>>();

        group.MapGet("/facets", async (ProductService service, CancellationToken ct) => await service.FacetsAsync(ct))
            .WithSummary("Categories and brands with product counts");

        group.MapGet("/{sku}", async Task<IResult> (string sku, ProductService service, CancellationToken ct) =>
                await service.GetAsync(sku, ct) is { } product ? Results.Ok(product) : Results.NotFound())
            .WithSummary("Get a product by SKU")
            .Produces<Product>();

        group.MapPut("/{sku}", async (string sku, ProductRequest request, ProductService service, CancellationToken ct) =>
            {
                await service.UpsertAsync([new ProductInput(sku, request.Name ?? "", request.Brand, request.Categories, request.Attributes, request.Active ?? true)], ct);
                return (await service.GetAsync(sku, ct))!;
            })
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Create or replace a product");

        group.MapPost("/bulk", async (List<BulkProduct> products, ProductService service, CancellationToken ct) =>
                await service.UpsertAsync(products.Select(p => new ProductInput(p.Sku, p.Name, p.Brand, p.Categories, p.Attributes, p.Active ?? true)).ToList(), ct))
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Create or replace up to 20 000 products")
            .Produces<ImportResult>();

        group.MapPut("/import", async (HttpRequest http, ProductService service, CancellationToken ct) =>
            {
                using var reader = new StreamReader(http.Body);
                return await service.ImportCsvAsync(reader, ct);
            })
            .RequireAuthorization(Policies.Manage)
            .Accepts<string>("text/csv", "text/plain")
            .WithSummary("Import products from CSV")
            .WithDescription("Header row required. Columns: sku, name (required), brand, categories (separated by |), active; " +
                             "any other column becomes an attribute. `,` `;` and tab delimiters are detected. Existing SKUs are updated.")
            .Produces<ImportResult>();

        group.MapDelete("/{sku}", async (string sku, ProductService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(sku, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.Manage)
            .WithSummary("Delete a product");

        return app;
    }
}
