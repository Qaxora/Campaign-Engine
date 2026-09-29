using CampaignEngine.Api.Http;
using CampaignEngine.Api.Security;
using CampaignEngine.Core.Campaigns;
using CampaignEngine.Infrastructure.Ledger;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Endpoints;

/// <summary>Read access to the redemption ledger for the web app, finance and reporting.</summary>
public static class LedgerEndpoints
{
    public static IEndpointRouteBuilder MapLedgerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ledger")
            .WithTags("Ledger")
            .RequireAuthorization(Policies.Read);

        group.MapGet("/transactions", async (
                    DateTimeOffset? from, DateTimeOffset? to, string? status, string? channel, string? storeId, string? customerId,
                    string? campaign, bool? offline, string? search, int? page, int? pageSize, LedgerService service, CancellationToken ct) =>
                await service.ListTransactionsAsync(
                    new TransactionQuery(from, to, QueryEnum.Parse<RedemptionStatus>(status, "status"), channel, storeId, customerId, campaign, offline, search, page ?? 1, pageSize ?? 50),
                    ct))
            .WithSummary("Search transactions, newest first")
            .WithDescription("`from` is inclusive and `to` exclusive (ISO 8601). `campaign` filters by campaign code; `search` matches the transaction id.")
            .Produces<PagedResult<LedgerTransaction>>();

        group.MapGet("/transactions/{transactionId}", async Task<IResult> (string transactionId, LedgerService service, CancellationToken ct) =>
                await service.GetTransactionAsync(transactionId, ct) is { } detail ? Results.Ok(detail) : Results.NotFound())
            .WithSummary("A transaction with the discount of every campaign it used")
            .Produces<LedgerTransactionDetail>();

        group.MapGet("/usage/campaigns", async (string? status, LedgerService service, CancellationToken ct) =>
                await service.CampaignUsageAsync(QueryEnum.Parse<CampaignStatus>(status, "status"), ct))
            .WithSummary("Redemptions, discount and limit / budget consumption per campaign")
            .Produces<IReadOnlyList<CampaignUsage>>();

        group.MapGet("/usage/campaigns/{campaignId:guid}", async Task<IResult> (Guid campaignId, LedgerService service, CancellationToken ct) =>
                await service.CampaignUsageAsync(campaignId, ct) is { } usage ? Results.Ok(usage) : Results.NotFound())
            .WithSummary("Usage of one campaign")
            .Produces<CampaignUsage>();

        group.MapGet("/usage/customers/{customerId}", async (string customerId, LedgerService service, CancellationToken ct) =>
                await service.CustomerUsageAsync(customerId, ct))
            .WithSummary("What one customer has redeemed, per campaign")
            .Produces<CustomerUsage>();

        return app;
    }
}
