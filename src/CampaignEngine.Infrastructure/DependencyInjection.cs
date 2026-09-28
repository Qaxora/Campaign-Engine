using CampaignEngine.Core.Conflicts;
using CampaignEngine.Core.Evaluation;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Platform;
using CampaignEngine.Infrastructure.Services;
using CampaignEngine.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CampaignEngine.Infrastructure;

public sealed class DatabaseOptions
{
    /// <summary><c>Sqlite</c> (default), <c>SqlServer</c> or <c>PostgreSql</c>.</summary>
    public string Provider { get; set; } = "Sqlite";

    public string ConnectionString { get; set; } = "Data Source=campaigns.db";

    /// <summary>Create the schema on startup if the database is empty.</summary>
    public bool AutoCreate { get; set; } = true;
}

public static class DependencyInjection
{
    public static IServiceCollection AddCampaignEngineInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var database = configuration.GetSection("Database").Get<DatabaseOptions>() ?? new DatabaseOptions();
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));
        services.Configure<CatalogOptions>(configuration.GetSection("Catalog"));
        services.Configure<EngineOptions>(configuration.GetSection("Engine"));

        services.AddDbContext<CampaignDbContext>(options => _ = database.Provider.ToUpperInvariant() switch
        {
            "SQLITE" => options.UseSqlite(database.ConnectionString),
            "SQLSERVER" => options.UseSqlServer(database.ConnectionString),
            "POSTGRESQL" or "POSTGRES" or "NPGSQL" => options.UseNpgsql(database.ConnectionString),
            _ => throw new InvalidOperationException($"Unknown database provider '{database.Provider}'. Use Sqlite, SqlServer or PostgreSql."),
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<OrganizationService>();
        services.AddScoped<ApiKeyService>();
        services.AddScoped<IdentityService>();
        services.AddScoped<MemberService>();
        services.AddScoped<StoreService>();
        services.Configure<AccountOptions>(configuration.GetSection("Accounts"));
        services.Configure<SeedOptions>(configuration.GetSection("Seed"));
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EngineOptions>>().Value;
            options.TimeProvider = sp.GetRequiredService<TimeProvider>();
            return new PromotionEvaluator(options);
        });
        services.AddSingleton(new ConflictAnalyzer());
        services.AddSingleton<CatalogProvider>();
        services.AddScoped<CampaignService>();
        services.AddScoped<ProductListService>();
        services.AddScoped<EvaluationService>();
        services.AddScoped<RedemptionService>();

        services.Configure<WebhookOptions>(configuration.GetSection("Webhooks"));
        services.AddScoped<OutboxChangeNotifier>();
        services.AddScoped<IChangeNotifier>(sp => sp.GetRequiredService<OutboxChangeNotifier>());
        services.AddScoped<WebhookService>();
        services.AddHttpClient(WebhookDispatcher.HttpClientName);
        services.AddSingleton<WebhookDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<WebhookDispatcher>());
        return services;
    }

    /// <summary>Creates the schema when <see cref="DatabaseOptions.AutoCreate"/> is on.</summary>
    public static async Task InitializeCampaignDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
        if (options.AutoCreate)
        {
            await scope.ServiceProvider.GetRequiredService<CampaignDbContext>().Database.EnsureCreatedAsync(cancellationToken);
        }
    }
}
