using System.Net;
using System.Text.Json;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Infrastructure.Platform;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Tests;

public sealed class StoreTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Stores_can_be_registered_updated_and_deleted()
    {
        var admin = factory.Admin();
        var created = await (await admin.PostRawAsync("/api/v1/stores",
            """{ "code": "IST-KADIKOY", "name": "Kadıköy", "channel": "store", "city": "İstanbul" }""")).ReadAsync<Store>();
        Assert.True(created.Active);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostRawAsync("/api/v1/stores", """{ "code": "ist-kadikoy", "name": "dupe" }""")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRawAsync("/api/v1/stores", """{ "code": "bad code", "name": "" }""")).StatusCode);

        var updated = await (await admin.PutAsync("/api/v1/stores/ist-kadikoy", new StringContent(
            """{ "code": "ignored", "name": "Kadıköy Moda", "active": false }""", System.Text.Encoding.UTF8, "application/json"))).ReadAsync<Store>();
        Assert.Equal("IST-KADIKOY", updated.Code);
        Assert.Equal("Kadıköy Moda", updated.Name);
        Assert.False(updated.Active);

        var inactive = JsonSerializer.Deserialize<List<Store>>(await (await admin.GetAsync("/api/v1/stores?active=false")).Content.ReadAsStringAsync(), ApiFactory.Json)!;
        Assert.Contains(inactive, s => s.Code == "IST-KADIKOY");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/v1/stores/IST-KADIKOY")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/stores/IST-KADIKOY")).StatusCode);
    }

    [Fact]
    public async Task Stores_are_tenant_scoped_and_channel_keys_cannot_manage_them()
    {
        await (await factory.Admin().PostRawAsync("/api/v1/stores", """{ "code": "ANK-001", "name": "Ankara" }""")).ReadAsync<Store>();

        Assert.Equal(HttpStatusCode.NotFound, (await factory.OtherTenant().GetAsync("/api/v1/stores/ANK-001")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await factory.OtherTenant().PostRawAsync("/api/v1/stores", """{ "code": "ANK-001", "name": "Other org" }""")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Pos().PostRawAsync("/api/v1/stores", """{ "code": "X-1", "name": "x" }""")).StatusCode);
    }

    [Fact]
    public async Task Campaigns_warn_about_unregistered_store_codes()
    {
        var admin = factory.Admin();
        await (await admin.PostRawAsync("/api/v1/stores", """{ "code": "IZM-001", "name": "İzmir" }""")).ReadAsync<Store>();
        const string definition = """
            { "code": "STORE-WARN", "name": "w", "stores": { "include": ["IZM-001", "IZM-999"] },
              "reward": { "type": "percentageDiscount", "percent": 5 } }
            """;

        var validation = JsonDocument.Parse(await (await admin.PostRawAsync("/api/v1/campaigns/validate", definition)).Content.ReadAsStringAsync()).RootElement;
        Assert.True(validation.GetProperty("valid").GetBoolean());
        Assert.Equal("Store 'IZM-999' is not registered in this organization.", Assert.Single(validation.GetProperty("warnings").EnumerateArray()).GetString());

        var campaign = await (await admin.PostRawAsync("/api/v1/campaigns", definition)).ReadAsync<Campaign>();
        var activated = await (await admin.PostAsync($"/api/v1/campaigns/{campaign.Id}/activate", null)).ReadAsync<CampaignChange>();
        Assert.Single(activated.Warnings);
    }
}
