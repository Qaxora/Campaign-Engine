using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Products;

namespace CampaignEngine.Infrastructure.Services;

/// <summary>
/// Called by the services right before they save a change, so an implementation can enlist
/// notifications in the same database transaction (outbox pattern).
/// </summary>
public interface IChangeNotifier
{
    void CampaignChanged(string eventType, Campaign campaign);

    void ProductListChanged(string eventType, ProductList list);
}

public sealed class NullChangeNotifier : IChangeNotifier
{
    public void CampaignChanged(string eventType, Campaign campaign)
    {
    }

    public void ProductListChanged(string eventType, ProductList list)
    {
    }
}

public static class ChangeEvents
{
    public const string CampaignCreated = "campaign.created";
    public const string CampaignUpdated = "campaign.updated";
    public const string CampaignActivated = "campaign.activated";
    public const string CampaignPaused = "campaign.paused";
    public const string CampaignArchived = "campaign.archived";
    public const string CampaignDeleted = "campaign.deleted";
    public const string ProductListCreated = "productList.created";
    public const string ProductListUpdated = "productList.updated";
    public const string ProductListDeleted = "productList.deleted";

    public static IReadOnlyList<string> All { get; } =
    [
        CampaignCreated, CampaignUpdated, CampaignActivated, CampaignPaused, CampaignArchived, CampaignDeleted,
        ProductListCreated, ProductListUpdated, ProductListDeleted,
    ];
}
