using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Conflicts;

namespace CampaignEngine.Infrastructure.Services;

/// <summary>One side of a conflict, described by the engine so clients never interpret rules.</summary>
public sealed record ConflictSide(
    Guid Id,
    string Code,
    string Name,
    CampaignStatus Status,
    int Priority,
    StackingMode Stacking,
    string Summary,
    IReadOnlyList<string> Audience,
    IReadOnlyList<string> Schedule,
    IReadOnlyList<string> Products);

public sealed record ConflictReportItem(CampaignConflict Conflict, ConflictSide Campaign, ConflictSide Other);

public sealed record ConflictReport(
    int LiveCampaigns,
    int Errors,
    int Warnings,
    int Infos,
    string CatalogVersion,
    IReadOnlyList<ConflictReportItem> Items);

public static class ConflictReportBuilder
{
    /// <summary>Pairs every conflict of the deterministic analyzer with both campaigns' descriptions.</summary>
    public static ConflictReport Build(IReadOnlyList<Campaign> campaigns, IReadOnlyList<CampaignConflict> conflicts, string catalogVersion)
    {
        var sides = campaigns.ToDictionary(c => c.Id, Side);
        var items = conflicts
            .Where(c => sides.ContainsKey(c.CampaignId) && sides.ContainsKey(c.OtherId))
            .OrderByDescending(c => c.Severity)
            .ThenBy(c => c.Kind)
            .ThenBy(c => c.CampaignCode, StringComparer.Ordinal)
            .ThenBy(c => c.OtherCode, StringComparer.Ordinal)
            .Select(c => new ConflictReportItem(c, sides[c.CampaignId], sides[c.OtherId]))
            .ToList();

        return new ConflictReport(
            campaigns.Count,
            items.Count(i => i.Conflict.Severity == ConflictSeverity.Error),
            items.Count(i => i.Conflict.Severity == ConflictSeverity.Warning),
            items.Count(i => i.Conflict.Severity == ConflictSeverity.Info),
            catalogVersion,
            items);
    }

    private static ConflictSide Side(Campaign campaign)
    {
        var d = CampaignDescriber.Describe(campaign);
        return new ConflictSide(campaign.Id, campaign.Code, campaign.Name, campaign.Status, campaign.Priority, campaign.Stacking,
            d.Summary, d.Audience, d.Schedule, d.Products);
    }
}
