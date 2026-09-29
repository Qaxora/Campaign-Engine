using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CampaignEngine.Api.Tests;

/// <summary>
/// `dotnet run` uses appsettings.Development.json with the documented dev keys. This starts the API
/// with exactly that configuration (only the database file differs) so a seed that no longer passes
/// validation is caught here, not by the next person who clones the repository.
/// </summary>
public sealed class DevelopmentStartupTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"campaign-dev-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public DevelopmentStartupTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:ConnectionString", $"Data Source={_databasePath}");
            builder.UseSetting("Webhooks:Enabled", "false");
        });
    }

    [Theory]
    [InlineData("dev-admin-key", "/api/v1/campaigns")]
    [InlineData("dev-pos-key", "/api/v1/snapshot")]
    public async Task Documented_development_keys_work(string key, string path)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }
}
