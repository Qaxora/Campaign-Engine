using CampaignEngine.Core.Conflicts;
using CampaignEngine.Core.Evaluation;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Services;
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
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EngineOptions>>().Value;
            options.TimeProvider = sp.GetRequiredService<TimeProvider>();
            return new PromotionEvaluator(options);
        });
        services.AddSingleton(new ConflictAnalyzer());
        services.AddSingleton<CatalogProvider>();
        services.TryAddScoped<IChangeNotifier, NullChangeNotifier>();
        services.AddScoped<CampaignService>();
        services.AddScoped<ProductListService>();
        services.AddScoped<EvaluationService>();
        services.AddScoped<RedemptionService>();
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
