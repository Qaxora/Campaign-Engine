using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using CampaignEngine.Infrastructure.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Api.Security;

/// <summary>One calling system (POS, web shop, back office, …).</summary>
public sealed class ApiClient
{
    /// <summary>Recorded in the redemption ledger, e.g. <c>pos-istanbul</c>, <c>akinon</c>.</summary>
    public string Name { get; set; } = "";

    public string Key { get; set; } = "";

    /// <summary>Slug of the organization (tenant) the key belongs to.</summary>
    public string Organization { get; set; } = "";

    /// <summary><see cref="Roles.Admin"/> and/or <see cref="Roles.Channel"/>.</summary>
    public List<string> Roles { get; set; } = [];
}

public sealed class AuthOptions
{
    public List<ApiClient> ApiKeys { get; set; } = [];
}

public static class Roles
{
    /// <summary>Manage campaigns, product lists and webhooks.</summary>
    public const string Admin = "admin";

    /// <summary>Evaluate carts, redeem, read the snapshot.</summary>
    public const string Channel = "channel";
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
}

/// <summary>Authenticates requests by the <c>X-Api-Key</c> header and binds them to the key's organization.</summary>
public sealed class ApiKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<AuthOptions> auth,
    OrganizationService organizations)
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

        var presented = Hash(values.ToString());
        var client = auth.CurrentValue.ApiKeys.FirstOrDefault(c =>
            !string.IsNullOrEmpty(c.Key) && CryptographicOperations.FixedTimeEquals(Hash(c.Key), presented));
        if (client is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var organization = await organizations.FindBySlugAsync(client.Organization, Context.RequestAborted);
        if (organization is null)
        {
            return AuthenticateResult.Fail("The API key is not bound to an existing organization.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, client.Name),
            new(PlatformClaims.TenantId, organization.Id.ToString()),
        };
        claims.AddRange(client.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    // Hashing first makes the comparison constant-time regardless of key length.
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}

public static class ApiKeyExtensions
{
    public static IServiceCollection AddApiKeyAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection("Auth"));
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

    /// <summary>Creates the organizations referenced by configured API keys (bootstrap before DB-stored keys exist).</summary>
    public static async Task EnsureConfiguredOrganizationsAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;
        var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
        foreach (var slug in options.ApiKeys.Select(k => k.Organization).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())
        {
            await organizations.EnsureAsync(slug, slug);
        }
    }

    /// <summary>Name of the calling client, for the ledger.</summary>
    public static string ClientName(this ClaimsPrincipal user) => user.Identity?.Name ?? "unknown";
}
