using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampaignEngine.Api.Tests;

public sealed class AccountTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _counter;

    private static string UniqueEmail(string name) => $"{name}-{Interlocked.Increment(ref _counter)}-{Guid.NewGuid():N}@example.com";

    private async Task<SignInResult> RegisterAsync(string email, string? organization = "Acme Retail")
    {
        var body = JsonSerializer.Serialize(new { email, password = "correct-horse-battery", name = "Test User", organizationName = organization }, ApiFactory.Json);
        return await (await factory.CreateClient().PostRawAsync("/api/v1/auth/register", body)).ReadAsync<SignInResult>();
    }

    private HttpClient WithSession(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Register_login_me_and_logout()
    {
        var email = UniqueEmail("owner");
        var registered = await RegisterAsync(email, "Acme Perakende A.Ş.");
        Assert.StartsWith("qxs_", registered.Token, StringComparison.Ordinal);
        Assert.Equal(MemberRole.Owner, registered.Session.Current!.Role);
        Assert.StartsWith("acme-perakende-a-s", registered.Session.Current.OrganizationSlug, StringComparison.Ordinal);

        var login = await (await factory.CreateClient().PostRawAsync("/api/v1/auth/login",
            $$"""{ "email": "{{email.ToUpperInvariant()}}", "password": "correct-horse-battery" }""")).ReadAsync<SignInResult>();
        var session = WithSession(login.Token);
        var me = await (await session.GetAsync("/api/v1/auth/me")).ReadAsync<SessionInfo>();
        Assert.Equal(email, me.User.Email);

        // A signed-in owner manages campaigns of their organization.
        Assert.Equal(HttpStatusCode.Created, (await session.PostRawAsync("/api/v1/campaigns",
            """{ "code": "WEB-USER-1", "name": "w", "reward": { "type": "percentageDiscount", "percent": 5 } }""")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await session.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_duplicate_email_are_rejected()
    {
        var email = UniqueEmail("dupe");
        await RegisterAsync(email);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostRawAsync("/api/v1/auth/login",
            $$"""{ "email": "{{email}}", "password": "wrong-password" }""")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await factory.CreateClient().PostRawAsync("/api/v1/auth/register",
            $$"""{ "email": "{{email}}", "password": "another-password", "name": "x" }""")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().PostRawAsync("/api/v1/auth/register",
            """{ "email": "not-an-email", "password": "short", "name": "" }""")).StatusCode);
    }

    [Fact]
    public async Task Expired_sessions_are_rejected()
    {
        var registered = await RegisterAsync(UniqueEmail("expired"));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
            var hash = ApiKeyService.Hash(registered.Token);
            await db.Sessions.Where(s => s.TokenHash == hash).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await WithSession(registered.Token).GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Members_can_read_but_not_manage()
    {
        var owner = await RegisterAsync(UniqueEmail("owner"));
        var memberEmail = UniqueEmail("member");
        var member = await RegisterAsync(memberEmail, organization: null);
        Assert.Null(member.Session.Current);
        Assert.Equal(HttpStatusCode.Forbidden, (await WithSession(member.Token).GetAsync("/api/v1/campaigns")).StatusCode);

        var ownerClient = WithSession(owner.Token);
        Assert.Equal(HttpStatusCode.Created, (await ownerClient.PostRawAsync("/api/v1/members", $$"""{ "email": "{{memberEmail}}", "role": "member" }""")).StatusCode);

        var memberLogin = await (await factory.CreateClient().PostRawAsync("/api/v1/auth/login",
            $$"""{ "email": "{{memberEmail}}", "password": "correct-horse-battery" }""")).ReadAsync<SignInResult>();
        var memberClient = WithSession(memberLogin.Token);
        Assert.Equal(MemberRole.Member, memberLogin.Session.Current!.Role);
        Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync("/api/v1/campaigns")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostRawAsync("/api/v1/campaigns",
            """{ "code": "NOPE", "name": "n", "reward": { "type": "percentageDiscount", "percent": 5 } }""")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostRawAsync("/api/v1/api-keys", """{ "name": "x", "scopes": ["channel"] }""")).StatusCode);

        // Members can use the cart simulator.
        Assert.Equal(HttpStatusCode.OK, (await memberClient.PostRawAsync("/api/v1/evaluate",
            """{ "channel": "web", "lines": [ { "lineId": "1", "sku": "A", "quantity": 1, "unitPrice": 10 } ] }""")).StatusCode);
    }

    [Fact]
    public async Task Ownership_rules_protect_the_organization()
    {
        var owner = await RegisterAsync(UniqueEmail("owner"));
        var adminEmail = UniqueEmail("admin");
        var admin = await RegisterAsync(adminEmail, organization: null);
        var ownerClient = WithSession(owner.Token);
        var added = await (await ownerClient.PostRawAsync("/api/v1/members", $$"""{ "email": "{{adminEmail}}", "role": "admin" }""")).ReadAsync<Member>();

        var adminLogin = await (await factory.CreateClient().PostRawAsync("/api/v1/auth/login",
            $$"""{ "email": "{{adminEmail}}", "password": "correct-horse-battery" }""")).ReadAsync<SignInResult>();
        var adminClient = WithSession(adminLogin.Token);

        // An admin cannot make themselves owner or remove the owner.
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.PutAsync($"/api/v1/members/{added.UserId}",
            new StringContent("""{ "role": "owner" }""", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.DeleteAsync($"/api/v1/members/{owner.Session.User.Id}")).StatusCode);

        // The last owner cannot step down.
        Assert.Equal(HttpStatusCode.Conflict, (await ownerClient.PutAsync($"/api/v1/members/{owner.Session.User.Id}",
            new StringContent("""{ "role": "member" }""", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Fact]
    public async Task Users_only_reach_organizations_they_belong_to()
    {
        var alice = await RegisterAsync(UniqueEmail("alice"), "Alice Co");
        var bob = await RegisterAsync(UniqueEmail("bob"), "Bob Co");
        var aliceClient = WithSession(alice.Token);
        var campaign = await (await aliceClient.PostRawAsync("/api/v1/campaigns",
            """{ "code": "ALICE-ONLY", "name": "a", "reward": { "type": "percentageDiscount", "percent": 5 } }""")).ReadAsync<Campaign>();

        var bobClient = WithSession(bob.Token);
        Assert.Equal(HttpStatusCode.NotFound, (await bobClient.GetAsync($"/api/v1/campaigns/{campaign.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bobClient.PostRawAsync("/api/v1/auth/switch-organization",
            $$"""{ "organizationId": "{{alice.Session.Current!.OrganizationId}}" }""")).StatusCode);

        // A second organization of Alice's is separate from the first one.
        var second = await (await aliceClient.PostRawAsync("/api/v1/auth/organizations", """{ "name": "Alice Outlet" }""")).ReadAsync<SessionInfo>();
        Assert.Equal(2, second.Memberships.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await aliceClient.GetAsync($"/api/v1/campaigns/{campaign.Id}")).StatusCode);

        var back = await (await aliceClient.PostRawAsync("/api/v1/auth/switch-organization",
            $$"""{ "organizationId": "{{alice.Session.Current!.OrganizationId}}" }""")).ReadAsync<SessionInfo>();
        Assert.Equal("Alice Co", back.Current!.OrganizationName);
        Assert.Equal(HttpStatusCode.OK, (await aliceClient.GetAsync($"/api/v1/campaigns/{campaign.Id}")).StatusCode);
    }
}
