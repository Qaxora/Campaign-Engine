using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Products;
using CampaignEngine.Core.Serialization;

namespace CampaignEngine.Infrastructure.Persistence;

internal static class Mapping
{
    /// <summary>Columns are the source of truth for identity, status, version and timestamps.</summary>
    public static Campaign ToDomain(this CampaignRecord record)
    {
        var campaign = CampaignJson.Deserialize<Campaign>(record.Definition);
        campaign.Id = record.Id;
        campaign.Code = record.Code;
        campaign.Status = record.Status;
        campaign.Version = record.Version;
        campaign.CreatedAt = new DateTimeOffset(record.CreatedAt, TimeSpan.Zero);
        campaign.UpdatedAt = new DateTimeOffset(record.UpdatedAt, TimeSpan.Zero);
        return campaign;
    }

    /// <summary>Copies the definition into the record (identity and audit columns are set by the caller).</summary>
    public static void Apply(this CampaignRecord record, Campaign campaign)
    {
        record.Code = campaign.Code;
        record.Name = campaign.Name;
        record.Status = campaign.Status;
        record.Priority = campaign.Priority;
        record.StartsAt = campaign.Schedule.StartsAt?.UtcDateTime;
        record.EndsAt = campaign.Schedule.EndsAt?.UtcDateTime;
        record.Definition = CampaignJson.Serialize(campaign);
    }

    public static ProductList ToDomain(this ProductListRecord record, bool includeSkus = true) => new()
    {
        Id = record.Id,
        Code = record.Code,
        Name = record.Name,
        Description = record.Description,
        Kind = record.Kind,
        Version = record.Version,
        UpdatedAt = new DateTimeOffset(record.UpdatedAt, TimeSpan.Zero),
        Skus = includeSkus
            ? new HashSet<string>(record.Items.Select(i => i.Sku), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
    };
}
