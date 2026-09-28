using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Carts;
using CampaignEngine.Core.Conflicts;
using CampaignEngine.Core.Evaluation;
using CampaignEngine.Core.Products;
using CampaignEngine.Core.Serialization;

namespace CampaignEngine.Client;

/// <summary>
/// Typed client for the Campaign Engine API. Register with
/// <see cref="ServiceCollectionExtensions.AddCampaignEngineClient"/> or construct with an
/// <see cref="HttpClient"/> whose <c>BaseAddress</c> and <c>X-Api-Key</c> header are set.
/// </summary>
public sealed class CampaignEngineClient(HttpClient http)
{
    private static JsonSerializerOptions Json => CampaignJson.Options;

    // Channel operations

    public Task<EvaluationResult> EvaluateAsync(Cart cart, bool explain = false, CancellationToken cancellationToken = default) =>
        SendAsync<EvaluationResult>(HttpMethod.Post, $"api/v1/evaluate?explain={(explain ? "true" : "false")}", cart, cancellationToken);

    /// <summary>Confirms a completed sale. Safe to retry with the same <paramref name="transactionId"/>.</summary>
    public Task<RedemptionResponse> RedeemAsync(string transactionId, Cart cart, CancellationToken cancellationToken = default) =>
        SendAsync<RedemptionResponse>(HttpMethod.Post, "api/v1/redemptions", new { transactionId, cart }, cancellationToken);

    public Task<RedemptionResponse> ReverseAsync(string transactionId, string? reason = null, CancellationToken cancellationToken = default) =>
        SendAsync<RedemptionResponse>(HttpMethod.Post, $"api/v1/redemptions/{Uri.EscapeDataString(transactionId)}/reverse", new { reason }, cancellationToken);

    public Task<RedemptionResponse> ImportOfflineAsync(OfflineSale sale, CancellationToken cancellationToken = default) =>
        SendAsync<RedemptionResponse>(HttpMethod.Post, "api/v1/redemptions/offline", sale, cancellationToken);

    public async Task<RedemptionResponse?> GetRedemptionAsync(string transactionId, CancellationToken cancellationToken = default) =>
        await GetOrNullAsync<RedemptionResponse>($"api/v1/redemptions/{Uri.EscapeDataString(transactionId)}", cancellationToken);

    /// <summary>Downloads the snapshot unless <paramref name="etag"/> is still current.</summary>
    public async Task<SnapshotResponse> GetSnapshotAsync(string? etag = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/snapshot");
        if (etag is not null)
        {
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));
        }

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new SnapshotResponse(true, etag, null);
        }

        var snapshot = await ReadAsync<SnapshotDocument>(response, cancellationToken);
        return new SnapshotResponse(false, response.Headers.ETag?.ToString(), snapshot);
    }

    // Campaign management

    public Task<PagedResponse<Campaign>> ListCampaignsAsync(string? status = null, string? channel = null, string? search = null,
        int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var query = $"api/v1/campaigns?page={page}&pageSize={pageSize}"
                    + (status is null ? "" : $"&status={Uri.EscapeDataString(status)}")
                    + (channel is null ? "" : $"&channel={Uri.EscapeDataString(channel)}")
                    + (search is null ? "" : $"&search={Uri.EscapeDataString(search)}");
        return SendAsync<PagedResponse<Campaign>>(HttpMethod.Get, query, null, cancellationToken);
    }

    public Task<Campaign?> GetCampaignAsync(string idOrCode, CancellationToken cancellationToken = default) =>
        GetOrNullAsync<Campaign>($"api/v1/campaigns/{Uri.EscapeDataString(idOrCode)}", cancellationToken);

    public Task<Campaign> CreateCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default) =>
        SendAsync<Campaign>(HttpMethod.Post, "api/v1/campaigns", campaign, cancellationToken);

    /// <summary>Replaces a campaign; <see cref="Campaign.Version"/> must be the version you loaded.</summary>
    public Task<CampaignChangeResponse> UpdateCampaignAsync(Campaign campaign, bool force = false, CancellationToken cancellationToken = default) =>
        SendAsync<CampaignChangeResponse>(HttpMethod.Put, $"api/v1/campaigns/{campaign.Id}?force={(force ? "true" : "false")}", campaign, cancellationToken);

    public Task<CampaignChangeResponse> ActivateCampaignAsync(Guid id, bool force = false, CancellationToken cancellationToken = default) =>
        SendAsync<CampaignChangeResponse>(HttpMethod.Post, $"api/v1/campaigns/{id}/activate?force={(force ? "true" : "false")}", null, cancellationToken);

    public Task<Campaign> PauseCampaignAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<Campaign>(HttpMethod.Post, $"api/v1/campaigns/{id}/pause", null, cancellationToken);

    public Task<Campaign> ArchiveCampaignAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<Campaign>(HttpMethod.Post, $"api/v1/campaigns/{id}/archive", null, cancellationToken);

    public Task<IReadOnlyList<CampaignConflict>> AnalyzeConflictsAsync(Campaign candidate, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<CampaignConflict>>(HttpMethod.Post, "api/v1/campaigns/conflicts", candidate, cancellationToken);

    // Product lists

    public Task<ProductList?> GetProductListAsync(string code, CancellationToken cancellationToken = default) =>
        GetOrNullAsync<ProductList>($"api/v1/product-lists/{Uri.EscapeDataString(code)}", cancellationToken);

    public Task<ProductList> CreateProductListAsync(string code, string name, ProductListKind kind = ProductListKind.Standard,
        IEnumerable<string>? skus = null, string? description = null, CancellationToken cancellationToken = default) =>
        SendAsync<ProductList>(HttpMethod.Post, "api/v1/product-lists", new { code, name, description, kind, skus = skus?.ToList() }, cancellationToken);

    public Task<ProductList> AddSkusAsync(string code, IEnumerable<string> skus, CancellationToken cancellationToken = default) =>
        SendAsync<ProductList>(HttpMethod.Post, $"api/v1/product-lists/{Uri.EscapeDataString(code)}/skus", new { skus }, cancellationToken);

    public Task<ProductList> RemoveSkusAsync(string code, IEnumerable<string> skus, CancellationToken cancellationToken = default) =>
        SendAsync<ProductList>(HttpMethod.Post, $"api/v1/product-lists/{Uri.EscapeDataString(code)}/skus/remove", new { skus }, cancellationToken);

    public Task<ProductList> ReplaceSkusAsync(string code, IEnumerable<string> skus, CancellationToken cancellationToken = default) =>
        SendAsync<ProductList>(HttpMethod.Put, $"api/v1/product-lists/{Uri.EscapeDataString(code)}/skus", new { skus }, cancellationToken);

    // Plumbing

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<T?> GetOrNullAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await http.GetAsync(url, cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ReadAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToException(response.StatusCode, body);
        }

        return JsonSerializer.Deserialize<T>(body, Json)
               ?? throw new CampaignEngineApiException((int)response.StatusCode, "Empty response", null, [], body);
    }

    private static CampaignEngineApiException ToException(HttpStatusCode status, string body)
    {
        string? title = null, detail = null;
        var errors = new List<string>();
        try
        {
            using var problem = JsonDocument.Parse(body);
            var root = problem.RootElement;
            title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
            detail = root.TryGetProperty("detail", out var d) ? d.GetString() : null;
            if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Array)
            {
                errors.AddRange(e.EnumerateArray().Select(x => x.GetString() ?? ""));
            }
        }
        catch (JsonException)
        {
            // Not a problem-details body.
        }

        return new CampaignEngineApiException((int)status, title ?? status.ToString(), detail, errors, body);
    }
}
