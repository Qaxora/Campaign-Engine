using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using CampaignEngine.Infrastructure.Persistence;

namespace CampaignEngine.Infrastructure.Platform;

/// <summary>
/// Organizations and API keys created on start-up — for local development, demos and tests.
/// Production organizations are created through registration.
/// </summary>
public sealed class SeedOptions
{
    public List<SeedOrganization> Organizations { get; set; } = [];
}

public sealed class SeedOrganization
{
    public string Slug { get; set; } = "";

    public string Name { get; set; } = "";

    public List<SeedApiKey> ApiKeys { get; set; } = [];
}

public sealed class SeedApiKey
{
    public string Name { get; set; } = "";

    /// <summary>The plaintext key; only its hash is stored.</summary>
    public string Key { get; set; } = "";

    public List<string> Scopes { get; set; } = [];
}

public static class Seeding
{
    /// <summary>Creates the configured organizations and keys that do not exist yet. Idempotent.</summary>
    public static async Task SeedPlatformAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<IOptions<SeedOptions>>().Value;
        foreach (var seed in options.Organizations.Where(o => !string.IsNullOrWhiteSpace(o.Slug)))
        {
            await using var scope = services.CreateAsyncScope();
            var organization = await scope.ServiceProvider.GetRequiredService<OrganizationService>()
                .EnsureAsync(seed.Slug, string.IsNullOrWhiteSpace(seed.Name) ? seed.Slug : seed.Name, cancellationToken);
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(organization.Id);
            var db = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
            var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
            foreach (var key in seed.ApiKeys.Where(k => !string.IsNullOrWhiteSpace(k.Key)))
            {
                var hash = ApiKeyService.Hash(key.Key);
                if (!await db.ApiKeys.IgnoreQueryFilters().AnyAsync(k => k.KeyHash == hash, cancellationToken))
                {
                    await keys.CreateAsync(key.Name, key.Scopes, key.Key, cancellationToken);
                }
            }
        }
    }
}
