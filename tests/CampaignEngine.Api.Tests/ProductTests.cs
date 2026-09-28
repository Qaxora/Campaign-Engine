using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CampaignEngine.Infrastructure.Catalog;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Tests;

public sealed class ProductTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static StringContent CsvContent(string csv)
    {
        var content = new StringContent(csv, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return content;
    }

    [Fact]
    public async Task Csv_import_creates_updates_and_parses_quotes_and_attributes()
    {
        var admin = factory.Admin();
        const string first = "sku;name;brand;categories;active;season\n" +
                             "TV-55;\"Televizyon 55\"\" 4K\";Vestel;electronics|electronics-tv;true;AW26\n" +
                             "TS-01;\"T-shirt; basic\";Acme;apparel|apparel-tshirt;;SS26\n";
        var imported = await (await admin.PutAsync("/api/v1/products/import", CsvContent(first))).ReadAsync<ImportResult>();
        Assert.Equal(new ImportResult(2, 0, 2), imported);

        var tv = await (await admin.GetAsync("/api/v1/products/tv-55")).ReadAsync<Product>();
        Assert.Equal("Televizyon 55\" 4K", tv.Name);
        Assert.Equal(["electronics", "electronics-tv"], tv.Categories);
        Assert.Equal("AW26", tv.Attributes["season"]);

        const string second = "sku,name,categories,active\nTV-55,Televizyon 55,electronics,false\nMUG-1,Kupa,home,\n";
        var again = await (await admin.PutAsync("/api/v1/products/import", CsvContent(second))).ReadAsync<ImportResult>();
        Assert.Equal(new ImportResult(1, 1, 2), again);

        var updated = await (await admin.GetAsync("/api/v1/products/TV-55")).ReadAsync<Product>();
        Assert.False(updated.Active);
        Assert.Equal(["electronics"], updated.Categories);
        Assert.Null(updated.Brand);
    }

    [Fact]
    public async Task Search_and_facets()
    {
        var admin = factory.Admin();
        await (await admin.PostRawAsync("/api/v1/products/bulk", """
            [
              { "sku": "F-1", "name": "Laptop Pro", "brand": "Globex", "categories": ["facet-electronics", "facet-computers"] },
              { "sku": "F-2", "name": "Laptop Air", "brand": "Globex", "categories": ["facet-electronics", "facet-computers"] },
              { "sku": "F-3", "name": "Kettle", "brand": "Initech", "categories": ["facet-home"] }
            ]
            """)).ReadAsync<ImportResult>();

        var laptops = await (await admin.GetAsync("/api/v1/products?search=laptop&pageSize=1")).ReadAsync<PagedResult<Product>>();
        Assert.Equal(2, laptops.TotalCount);
        Assert.Single(laptops.Items);

        var computers = await (await admin.GetAsync("/api/v1/products?category=FACET-COMPUTERS")).ReadAsync<PagedResult<Product>>();
        Assert.Equal(["F-1", "F-2"], computers.Items.Select(p => p.Sku));

        var facets = await (await admin.GetAsync("/api/v1/products/facets")).ReadAsync<CatalogFacets>();
        Assert.Contains(new FacetValue("facet-electronics", 2), facets.Categories);
        Assert.Contains(new FacetValue("Globex", 2), facets.Brands);
    }

    [Fact]
    public async Task Invalid_input_is_rejected()
    {
        var admin = factory.Admin();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync("/api/v1/products/import", CsvContent("code,title\nA,B\n"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRawAsync("/api/v1/products/bulk",
            """[ { "sku": "D-1", "name": "a" }, { "sku": "d-1", "name": "b" } ]""")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRawAsync("/api/v1/products/bulk", """[ { "sku": "", "name": "" } ]""")).StatusCode);
    }

    [Fact]
    public async Task Catalog_is_tenant_scoped_and_read_only_for_channels()
    {
        await (await factory.Admin().PutAsync("/api/v1/products/PRIVATE-1", new StringContent(
            """{ "name": "Private", "categories": ["private"] }""", Encoding.UTF8, "application/json"))).ReadAsync<Product>();

        Assert.Equal(HttpStatusCode.NotFound, (await factory.OtherTenant().GetAsync("/api/v1/products/PRIVATE-1")).StatusCode);
        var otherFacets = await (await factory.OtherTenant().GetAsync("/api/v1/products/facets")).ReadAsync<CatalogFacets>();
        Assert.DoesNotContain(otherFacets.Categories, f => f.Value == "private");
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Pos().DeleteAsync("/api/v1/products/PRIVATE-1")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await factory.Admin().DeleteAsync("/api/v1/products/PRIVATE-1")).StatusCode);
    }
}
