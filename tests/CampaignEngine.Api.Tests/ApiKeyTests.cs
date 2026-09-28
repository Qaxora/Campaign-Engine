using System.Net;
using System.Text.Json;
using CampaignEngine.Infrastructure.Platform;

namespace CampaignEngine.Api.Tests;

public sealed class ApiKeyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient WithKey(string key)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    [Fact]
    public async Task Created_key_works_once_shown_and_stops_after_revocation()
    {
        var created = await (await factory.Admin().PostRawAsync("/api/v1/api-keys", """{ "name": "pos-izmir", "scopes": ["channel"] }""")).ReadAsync<CreatedApiKey>();
        Assert.StartsWith("qxc_", created.Key, StringComparison.Ordinal);
        Assert.StartsWith(created.Prefix, created.Key, StringComparison.Ordinal);

        var pos = WithKey(created.Key);
        Assert.Equal(HttpStatusCode.OK, (await pos.GetAsync("/api/v1/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pos.GetAsync("/api/v1/campaigns")).StatusCode); // channel scope only

        // The secret is never listed again.
        var list = await (await factory.Admin().GetAsync("/api/v1/api-keys")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(created.Key, list, StringComparison.Ordinal);
        var listed = JsonSerializer.Deserialize<List<ApiKey>>(list, ApiFactory.Json)!.Single(k => k.Id == created.Id);
        Assert.NotNull(listed.LastUsedAt);

        await (await factory.Admin().PostAsync($"/api/v1/api-keys/{created.Id}/revoke", null)).ReadAsync<ApiKey>();
        Assert.Equal(HttpStatusCode.Unauthorized, (await pos.GetAsync("/api/v1/snapshot")).StatusCode);
    }

    [Fact]
    public async Task Keys_are_tenant_scoped()
    {
        var created = await (await factory.Admin().PostRawAsync("/api/v1/api-keys", """{ "name": "a-only", "scopes": ["admin"] }""")).ReadAsync<CreatedApiKey>();

        var otherList = JsonSerializer.Deserialize<List<ApiKey>>(await (await factory.OtherTenant().GetAsync("/api/v1/api-keys")).Content.ReadAsStringAsync(), ApiFactory.Json)!;
        Assert.DoesNotContain(otherList, k => k.Id == created.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.OtherTenant().PostAsync($"/api/v1/api-keys/{created.Id}/revoke", null)).StatusCode);

        // The new key acts in tenant A: it sees A's keys, not B's.
        var ownList = JsonSerializer.Deserialize<List<ApiKey>>(await (await WithKey(created.Key).GetAsync("/api/v1/api-keys")).Content.ReadAsStringAsync(), ApiFactory.Json)!;
        Assert.Contains(ownList, k => k.Name == "back-office");
        Assert.DoesNotContain(ownList, k => k.Name == "other-back-office");
    }

    [Fact]
    public async Task Invalid_scopes_are_rejected()
    {
        var response = await factory.Admin().PostRawAsync("/api/v1/api-keys", """{ "name": "x", "scopes": ["root"] }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Channel_keys_cannot_manage_keys()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Pos().PostRawAsync("/api/v1/api-keys", """{ "name": "x", "scopes": ["admin"] }""")).StatusCode);
    }
}
