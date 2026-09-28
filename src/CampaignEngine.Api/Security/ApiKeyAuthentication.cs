using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Api.Security;

/// <summary>One calling system (POS, web shop, back office, …).</summary>
public sealed class ApiClient
{
    /// <summary>Recorded in the redemption ledger, e.g. <c>pos-istanbul</c>, <c>akinon</c>.</summary>
    public string Name { get; set; } = "";

    public string Key { get; set; } = "";

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

/// <summary>Authenticates requests by the <c>X-Api-Key</c> header.</summary>
public sealed class ApiKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<AuthOptions> auth)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || string.IsNullOrEmpty(values.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var presented = Hash(values.ToString());
        var client = auth.CurrentValue.ApiKeys.FirstOrDefault(c =>
            !string.IsNullOrEmpty(c.Key) && CryptographicOperations.FixedTimeEquals(Hash(c.Key), presented));
        if (client is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, client.Name) };
        claims.AddRange(client.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
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
            .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin))
            .AddPolicy(Policies.Channel, p => p.RequireRole(Roles.Channel, Roles.Admin));
        return services;
    }

    /// <summary>Name of the calling client, for the ledger.</summary>
    public static string ClientName(this ClaimsPrincipal user) => user.Identity?.Name ?? "unknown";
}
