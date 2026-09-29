using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Infrastructure.Audit;
using CampaignEngine.Infrastructure.Platform;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Tests;

public sealed class AuditTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Client, SignInResult SignIn)> OwnerAsync()
    {
        var body = JsonSerializer.Serialize(new { email = $"audit-{Guid.NewGuid():N}@example.com", password = "correct-horse-battery", name = "Auditor", organizationName = "Audit Co" }, ApiFactory.Json);
        var signIn = await (await factory.CreateClient().PostRawAsync("/api/v1/auth/register", body)).ReadAsync<SignInResult>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signIn.Token);
        return (client, signIn);
    }

    [Fact]
    public async Task Campaign_lifecycle_is_recorded_with_the_signed_in_user()
    {
        var (owner, signIn) = await OwnerAsync();
        var campaign = await (await owner.PostRawAsync("/api/v1/campaigns",
            """{ "code": "AUD-1", "name": "Audited", "reward": { "type": "percentageDiscount", "percent": 5 } }""")).ReadAsync<Campaign>();
        await (await owner.PostAsync($"/api/v1/campaigns/{campaign.Id}/activate", null)).ReadAsync<CampaignChange>();
        await (await owner.PostAsync($"/api/v1/campaigns/{campaign.Id}/pause", null)).ReadAsync<Campaign>();

        var trail = await (await owner.GetAsync($"/api/v1/audit?entityType=campaign&entityId={campaign.Id}")).ReadAsync<PagedResult<AuditEntry>>();

        Assert.Equal(["campaign.paused", "campaign.activated", "campaign.created"], trail.Items.Select(e => e.Action));
        Assert.All(trail.Items, e =>
        {
            Assert.Equal("user", e.ActorType);
            Assert.Equal(signIn.Session.User.Id.ToString(), e.ActorId);
            Assert.Equal(signIn.Session.User.Email, e.ActorName);
            Assert.Equal("AUD-1", e.EntityName);
        });
        Assert.Contains("status paused", trail.Items[0].Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configuration_changes_by_api_keys_are_recorded()
    {
        var admin = factory.Admin();
        await (await admin.PostRawAsync("/api/v1/stores", """{ "code": "AUD-STORE", "name": "Audit store" }""")).ReadAsync<Store>();
        var key = await (await admin.PostRawAsync("/api/v1/api-keys", """{ "name": "audit-pos", "scopes": ["channel"] }""")).ReadAsync<CreatedApiKey>();
        await (await admin.PostAsync($"/api/v1/api-keys/{key.Id}/revoke", null)).ReadAsync<ApiKey>();
        await admin.PostRawAsync("/api/v1/product-lists", """{ "code": "AUD-LIST", "name": "Audit list", "skus": ["A", "B"] }""");

        var store = await (await admin.GetAsync("/api/v1/audit?entityType=store&entityId=AUD-STORE")).ReadAsync<PagedResult<AuditEntry>>();
        var entry = Assert.Single(store.Items);
        Assert.Equal("store.created", entry.Action);
        Assert.Equal("apiKey", entry.ActorType);
        Assert.Equal("back-office", entry.ActorName);

        var keyTrail = await (await admin.GetAsync($"/api/v1/audit?entityType=apiKey&entityId={key.Id}")).ReadAsync<PagedResult<AuditEntry>>();
        Assert.Equal(["apiKey.revoked", "apiKey.created"], keyTrail.Items.Select(e => e.Action));
        Assert.DoesNotContain(keyTrail.Items, e => e.Summary.Contains(key.Key, StringComparison.Ordinal)); // never the secret

        var list = await (await admin.GetAsync("/api/v1/audit?action=productList.created")).ReadAsync<PagedResult<AuditEntry>>();
        Assert.Contains(list.Items, e => e.EntityName == "AUD-LIST" && e.Summary.Contains("2 SKUs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Member_and_organization_changes_are_recorded()
    {
        var (owner, _) = await OwnerAsync();
        var otherEmail = $"audit-member-{Guid.NewGuid():N}@example.com";
        await factory.CreateClient().PostRawAsync("/api/v1/auth/register",
            JsonSerializer.Serialize(new { email = otherEmail, password = "correct-horse-battery", name = "M" }, ApiFactory.Json));
        var member = await (await owner.PostRawAsync("/api/v1/members", $$"""{ "email": "{{otherEmail}}", "role": "member" }""")).ReadAsync<Member>();
        await owner.PutAsync($"/api/v1/members/{member.UserId}", new StringContent("""{ "role": "admin" }""", Encoding.UTF8, "application/json"));
        await owner.PutAsync("/api/v1/organization", new StringContent("""{ "name": "Audit Co Renamed" }""", Encoding.UTF8, "application/json"));

        var trail = await (await owner.GetAsync("/api/v1/audit")).ReadAsync<PagedResult<AuditEntry>>();
        Assert.Equal(["organization.renamed", "member.roleChanged", "member.added"], trail.Items.Select(e => e.Action));
        Assert.Contains("from member to admin", trail.Items[1].Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Audit_log_is_tenant_scoped_and_not_visible_to_channel_keys()
    {
        var (owner, _) = await OwnerAsync();
        await owner.PostRawAsync("/api/v1/stores", """{ "code": "SECRET-STORE", "name": "s" }""");

        var other = await factory.OtherTenant().GetAsync("/api/v1/audit?entityType=store&entityId=SECRET-STORE");
        Assert.Equal(0, (await other.ReadAsync<PagedResult<AuditEntry>>()).TotalCount);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Pos().GetAsync("/api/v1/audit")).StatusCode);
    }
}
