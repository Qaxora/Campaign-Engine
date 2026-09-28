using Microsoft.Extensions.DependencyInjection;

namespace CampaignEngine.Client;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CampaignEngineClient"/> as a typed HTTP client and
    /// <see cref="LocalCampaignEngine"/> as a singleton.
    /// </summary>
    public static IHttpClientBuilder AddCampaignEngineClient(this IServiceCollection services, Uri baseAddress, string apiKey)
    {
        services.AddSingleton(sp => new LocalCampaignEngine(sp.GetRequiredService<CampaignEngineClient>()));
        return services.AddHttpClient<CampaignEngineClient>(http =>
        {
            http.BaseAddress = baseAddress;
            http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        });
    }
}
