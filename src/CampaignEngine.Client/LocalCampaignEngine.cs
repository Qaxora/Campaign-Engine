using CampaignEngine.Core.Carts;
using CampaignEngine.Core.Evaluation;

namespace CampaignEngine.Client;

/// <summary>
/// Local ("offline") mode for .NET channels: keeps the latest snapshot in memory and evaluates carts
/// in-process with the same engine the server uses. Call <see cref="RefreshAsync"/> periodically or
/// when a webhook arrives; it costs one 304 response while nothing changed.
/// </summary>
/// <remarks>
/// Usage limits and budgets cannot be enforced offline. Report each sale with
/// <see cref="CampaignEngineClient.ImportOfflineAsync"/> (see <see cref="OfflineSale.From"/>) once back online.
/// </remarks>
public sealed class LocalCampaignEngine(CampaignEngineClient client, EngineOptions? options = null)
{
    private readonly PromotionEvaluator _evaluator = new(options);
    private volatile State? _state;

    public string? Version => _state?.ETag;

    public DateTimeOffset? LastRefresh { get; private set; }

    /// <summary>Loads a snapshot saved earlier (e.g. from disk at POS start-up, before the network is up).</summary>
    public void Load(SnapshotDocument snapshot, string? etag = null) =>
        _state = new State(etag ?? $"\"{snapshot.Version}\"", snapshot, snapshot.ToCatalog());

    /// <summary>Fetches the snapshot if it changed. Returns true when a new snapshot was loaded.</summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.GetSnapshotAsync(_state?.ETag, cancellationToken);
        LastRefresh = DateTimeOffset.UtcNow;
        if (response.NotModified || response.Snapshot is null)
        {
            return false;
        }

        Load(response.Snapshot, response.ETag);
        return true;
    }

    /// <summary>The current snapshot, e.g. to persist it to disk.</summary>
    public SnapshotDocument? Snapshot => _state?.Document;

    public EvaluationResult Evaluate(Cart cart, bool explain = false)
    {
        var state = _state ?? throw new InvalidOperationException("No snapshot loaded. Call RefreshAsync or Load first.");
        return _evaluator.Evaluate(cart, state.Catalog, UsageSnapshot.Empty, explain);
    }

    private sealed record State(string? ETag, SnapshotDocument Document, CatalogSnapshot Catalog);
}
