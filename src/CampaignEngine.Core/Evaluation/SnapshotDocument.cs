using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Products;

namespace CampaignEngine.Core.Evaluation;

/// <summary>
/// The wire format of <c>GET /api/v1/snapshot</c>: every live campaign plus the product lists they
/// need. A channel can store it and evaluate carts locally (e.g. a POS while offline).
/// </summary>
public sealed class SnapshotDocument
{
    public required string Version { get; init; }

    public DateTimeOffset GeneratedAt { get; init; }

    public List<Campaign> Campaigns { get; init; } = [];

    public List<ProductList> ProductLists { get; init; } = [];

    /// <summary>Builds the in-memory catalog for <see cref="PromotionEvaluator"/>.</summary>
    public CatalogSnapshot ToCatalog() => new(Campaigns, new ProductListIndex(ProductLists), Version);
}
