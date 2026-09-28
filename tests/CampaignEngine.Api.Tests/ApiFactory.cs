using System.Net.Http.Json;
using System.Text.Json;
using CampaignEngine.Core.Serialization;
using CampaignEngine.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CampaignEngine.Api.Tests;

/// <summary>Runs the real API against a fresh SQLite file per test class.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminKey = "test-admin-key";
    public const string PosKey = "test-pos-key";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"campaign-tests-{Guid.NewGuid():N}.db");

    public CapturingHandler Webhooks { get; } = new();

    public static JsonSerializerOptions Json => CampaignJson.Options;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Database:ConnectionString", $"Data Source={_databasePath}");
        builder.UseSetting("Catalog:CacheSeconds", "0");
        builder.UseSetting("Webhooks:Enabled", "false"); // tests drive the dispatcher explicitly
        builder.UseSetting("Auth:ApiKeys:0:Name", "back-office");
        builder.UseSetting("Auth:ApiKeys:0:Key", AdminKey);
        builder.UseSetting("Auth:ApiKeys:0:Roles:0", "admin");
        builder.UseSetting("Auth:ApiKeys:1:Name", "pos-ist-001");
        builder.UseSetting("Auth:ApiKeys:1:Key", PosKey);
        builder.UseSetting("Auth:ApiKeys:1:Roles:0", "channel");

        builder.ConfigureTestServices(services =>
            services.AddHttpClient(WebhookDispatcher.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Webhooks));
    }

    public HttpClient Admin() => ClientWithKey(AdminKey);

    public HttpClient Pos() => ClientWithKey(PosKey);

    private HttpClient ClientWithKey(string key)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_databasePath) + "*"))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }
}

/// <summary>Records webhook requests instead of sending them.</summary>
public sealed class CapturingHandler : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests)
        {
            Requests.Add((request, body));
        }

        return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
    }
}

internal static class HttpExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, ApiFactory.Json)!;
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, ApiFactory.Json);

    public static Task<HttpResponseMessage> PostRawAsync(this HttpClient client, string url, string json) =>
        client.PostAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
}
