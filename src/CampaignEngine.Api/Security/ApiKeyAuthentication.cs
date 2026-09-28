using System.Security.Claims;
using System.Text.Encodings.Web;
using CampaignEngine.Infrastructure.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Api.Security;

public static class Roles
{
    /// <summary>Manage campaigns, product lists and webhooks.</summary>
    public const string Admin = ApiKeyScopes.Admin;

    /// <summary>Evaluate carts, redeem, read the snapshot.</summary>
    public const string Channel = ApiKeyScopes.Channel;
}

public static class Policies
{
    public const string Admin = "admin";
    public const string Channel = "channel";
}

public static class PlatformClaims
{
    /// <summary>The organization the caller acts for.</summary>
    public const string TenantId = "tenant_id";

    /// <summary><c>apiKey</c> or <c>user</c>.</summary>
    public const string ActorType = "actor_type";

    /// <summary>Id of the API key or user.</summary>
    public const string ActorId = "actor_id";
}

/// <summary>Authenticates requests by the <c>X-Api-Key</c> header against the organization's stored keys.</summary>
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
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}

public static class ApiKeyExtensions
{
    public static IServiceCollection AddApiKeyAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(ApiKeyHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyHandler>(ApiKeyHandler.SchemeName, null);
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin).RequireClaim(PlatformClaims.TenantId))
            .AddPolicy(Policies.Channel, p => p.RequireRole(Roles.Channel, Roles.Admin).RequireClaim(PlatformClaims.TenantId));
        return services;
    }

    /// <summary>Sets the scoped <see cref="TenantContext"/> from the authenticated caller (ADR 0005).</summary>
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (Guid.TryParse(context.User.FindFirstValue(PlatformClaims.TenantId), out var tenantId))
            {
                context.RequestServices.GetRequiredService<TenantContext>().Set(tenantId);
            }

            await next(context);
        });

    /// <summary>Name of the calling client, for the ledger.</summary>
    public static string ClientName(this ClaimsPrincipal user) => user.Identity?.Name ?? "unknown";
}
