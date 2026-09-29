using System.Security.Claims;
using System.Text.Encodings.Web;
using CampaignEngine.Infrastructure.Audit;
using CampaignEngine.Infrastructure.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Api.Security;

/// <summary>
/// Role claims. Users carry <c>owner</c> / <c>admin</c> / <c>member</c>; API keys carry their scopes
/// <c>admin</c> / <c>channel</c>. The shared name <c>admin</c> means "may manage" for both.
/// </summary>
public static class Roles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Member = "member";
    public const string Channel = ApiKeyScopes.Channel;

    public static string Of(MemberRole role) => role switch
    {
        MemberRole.Owner => Owner,
        MemberRole.Admin => Admin,
        _ => Member,
    };
}

/// <summary>Authorization policies shared by people and integrations (docs/saas-architecture.md).</summary>
public static class Policies
{
    /// <summary>See the organization's data: any member, or an admin-scoped key.</summary>
    public const string Read = "read";

    /// <summary>Change configuration: owners and admins, or an admin-scoped key.</summary>
    public const string Manage = "manage";

    /// <summary>Price and redeem carts: channel/admin keys, or any member (simulator in the web app).</summary>
    public const string Channel = "channel";

    /// <summary>Organization-level decisions: owners only.</summary>
    public const string Owner = "owner";

    /// <summary>Signed-in person, with or without an organization.</summary>
    public const string User = "user";
}

public static class PlatformClaims
{
    /// <summary>The organization the caller acts for.</summary>
    public const string TenantId = "tenant_id";

    /// <summary><c>apiKey</c> or <c>user</c>.</summary>
    public const string ActorType = "actor_type";

    /// <summary>Id of the API key or user.</summary>
    public const string ActorId = "actor_id";

    /// <summary>Id of the web session (users only).</summary>
    public const string SessionId = "session_id";
}

/// <summary>Authenticates integrations by the <c>X-Api-Key</c> header against the organization's stored keys.</summary>
public sealed class ApiKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyService keys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || string.IsNullOrEmpty(values.ToString()))
        {
            return AuthenticateResult.NoResult();
        }

        var identity = await keys.AuthenticateAsync(values.ToString(), Context.RequestAborted);
        if (identity is null)
        {
            return AuthenticateResult.Fail("Invalid or revoked API key.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, identity.Name),
            new(PlatformClaims.TenantId, identity.TenantId.ToString()),
            new(PlatformClaims.ActorType, "apiKey"),
            new(PlatformClaims.ActorId, identity.KeyId.ToString()),
        };
        claims.AddRange(identity.Scopes.Select(scope => new Claim(ClaimTypes.Role, scope)));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }
}

/// <summary>Authenticates people by the opaque session token in <c>Authorization: Bearer</c> (ADR 0007).</summary>
public sealed class SessionHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IdentityService identities)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var identity = await identities.AuthenticateAsync(header["Bearer ".Length..].Trim(), Context.RequestAborted);
        if (identity is null)
        {
            return AuthenticateResult.Fail("Session is invalid or expired.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, identity.User.Name),
            new(ClaimTypes.Email, identity.User.Email),
            new(PlatformClaims.ActorType, "user"),
            new(PlatformClaims.ActorId, identity.User.Id.ToString()),
            new(PlatformClaims.SessionId, identity.SessionId.ToString()),
        };
        if (identity is { TenantId: { } tenantId, Role: { } role })
        {
            claims.Add(new Claim(PlatformClaims.TenantId, tenantId.ToString()));
            claims.Add(new Claim(ClaimTypes.Role, Roles.Of(role)));
        }

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }
}

public static class AuthenticationSetup
{
    private const string SmartScheme = "ApiKeyOrSession";

    public static IServiceCollection AddPlatformAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(SmartScheme)
            .AddPolicyScheme(SmartScheme, "API key or session", o => o.ForwardDefaultSelector = context =>
                context.Request.Headers.ContainsKey(ApiKeyHandler.HeaderName) ? ApiKeyHandler.SchemeName : SessionHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyHandler>(ApiKeyHandler.SchemeName, null)
            .AddScheme<AuthenticationSchemeOptions, SessionHandler>(SessionHandler.SchemeName, null);

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Read, p => p.RequireClaim(PlatformClaims.TenantId).RequireRole(Roles.Owner, Roles.Admin, Roles.Member))
            .AddPolicy(Policies.Manage, p => p.RequireClaim(PlatformClaims.TenantId).RequireRole(Roles.Owner, Roles.Admin))
            .AddPolicy(Policies.Channel, p => p.RequireClaim(PlatformClaims.TenantId).RequireRole(Roles.Channel, Roles.Owner, Roles.Admin, Roles.Member))
            .AddPolicy(Policies.Owner, p => p.RequireClaim(PlatformClaims.TenantId).RequireRole(Roles.Owner))
            .AddPolicy(Policies.User, p => p.RequireClaim(PlatformClaims.ActorType, "user"));
        return services;
    }

    /// <summary>Sets the scoped <see cref="TenantContext"/> (ADR 0005) and the audit actor from the authenticated caller.</summary>
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.User.FindFirstValue(PlatformClaims.ActorType) is { } actorType)
            {
                context.RequestServices.GetRequiredService<ActorContext>()
                    .Set(actorType, context.User.FindFirstValue(PlatformClaims.ActorId), context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.ClientName());
            }

            if (Guid.TryParse(context.User.FindFirstValue(PlatformClaims.TenantId), out var tenantId))
            {
                context.RequestServices.GetRequiredService<TenantContext>().Set(tenantId);
            }

            await next(context);
        });

    /// <summary>Name of the caller, recorded in the ledger (API key name or user name).</summary>
    public static string ClientName(this ClaimsPrincipal user) => user.Identity?.Name ?? "unknown";

    public static Guid SessionId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(PlatformClaims.SessionId)!);

    public static Guid ActorId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(PlatformClaims.ActorId)!);

    /// <summary>The member role of a signed-in person; API keys with the admin scope count as admins.</summary>
    public static MemberRole MemberRole(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.Owner) ? Infrastructure.Platform.MemberRole.Owner
        : user.IsInRole(Roles.Admin) ? Infrastructure.Platform.MemberRole.Admin
        : Infrastructure.Platform.MemberRole.Member;
}
