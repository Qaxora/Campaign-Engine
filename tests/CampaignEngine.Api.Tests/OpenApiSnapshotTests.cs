using System.Text.Json;
using System.Text.Json.Nodes;

namespace CampaignEngine.Api.Tests;

/// <summary>
/// The web app's typed client is generated from <c>web/openapi.json</c>. This test fails when the API
/// contract changed without refreshing that file. Refresh with:
/// <c>UPDATE_OPENAPI=1 dotnet test --filter OpenApiSnapshot</c>, then <c>pnpm gen:api</c> in <c>web/</c>.
/// </summary>
public sealed class OpenApiSnapshotTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public async Task Web_snapshot_matches_the_api_document()
    {
        var json = await factory.CreateClient().GetStringAsync("/openapi/v1.json");
        var current = Normalize(json);
        var path = Path.Combine(RepositoryRoot(), "web", "openapi.json");

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            await File.WriteAllTextAsync(path, current);
            return;
        }

        Assert.True(File.Exists(path), $"{path} is missing. Run: UPDATE_OPENAPI=1 dotnet test --filter OpenApiSnapshot");
        var snapshot = Normalize(await File.ReadAllTextAsync(path));
        Assert.True(snapshot == current,
            "web/openapi.json is out of date. Run: UPDATE_OPENAPI=1 dotnet test --filter OpenApiSnapshot, then `pnpm gen:api` in web/.");
    }

    /// <summary>Drops the server list (it contains the test host URL) and fixes formatting and line endings.</summary>
    private static string Normalize(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node.Remove("servers");
        return node.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CampaignEngine.slnx"))
                                     && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
