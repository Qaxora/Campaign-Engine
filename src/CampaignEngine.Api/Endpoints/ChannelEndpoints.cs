using System.Security.Claims;
using CampaignEngine.Api.Security;
using CampaignEngine.Core.Carts;
using CampaignEngine.Core.Evaluation;
using CampaignEngine.Infrastructure.Services;

namespace CampaignEngine.Api.Endpoints;

/// <summary>Endpoints used by sales channels (POS, web shop, mobile app, …).</summary>
public static class ChannelEndpoints
{
    public sealed record RedeemRequest(string TransactionId, Cart Cart);

    public sealed record ReverseRequest(string? Reason);

    public static IEndpointRouteBuilder MapChannelEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization(Policies.Channel);

        api.MapPost("/evaluate", async (Cart cart, bool? explain, EvaluationService service, CancellationToken ct) =>
                await service.EvaluateAsync(cart, explain ?? false, ct))
            .WithTags("Evaluation")
            .WithSummary("Price a cart")
            .WithDescription("Stateless: call it on every basket change. With explain=true the response lists every campaign " +
                             "that did not apply and why. Nothing is recorded; call POST /redemptions when the sale completes.")
            .Produces<EvaluationResult>();

        var redemptions = api.MapGroup("/redemptions").WithTags("Redemptions");

        redemptions.MapPost("/", async (RedeemRequest request, ClaimsPrincipal user, RedemptionService service, CancellationToken ct) =>
            {
                var result = await service.RedeemAsync(request.TransactionId, request.Cart, user.ClientName(), ct);
                return result.Replayed
                    ? Results.Ok(result)
                    : Results.Created($"/api/v1/redemptions/{Uri.EscapeDataString(result.TransactionId)}", result);
            })
            .WithSummary("Confirm a completed sale")
            .WithDescription("Re-evaluates the final cart and records the applied campaigns against usage limits and budgets. " +
                             "Idempotent on transactionId: retries return the stored result with 200 and replayed=true.")
            .Produces<RedemptionResult>(StatusCodes.Status201Created)
            .Produces<RedemptionResult>();

        redemptions.MapGet("/{transactionId}", async Task<IResult> (string transactionId, RedemptionService service, CancellationToken ct) =>
                await service.FindAsync(transactionId, ct) is { } result ? Results.Ok(result) : Results.NotFound())
            .WithSummary("Get a recorded transaction")
            .Produces<RedemptionResult>();

        redemptions.MapPost("/{transactionId}/reverse", async (string transactionId, ReverseRequest? request, RedemptionService service, CancellationToken ct) =>
                await service.ReverseAsync(transactionId, request?.Reason, ct))
            .WithSummary("Reverse a transaction (void / full return)")
            .WithDescription("Releases usage and budget. Idempotent.");

        redemptions.MapPost("/offline", async (OfflineRedemption sale, ClaimsPrincipal user, RedemptionService service, CancellationToken ct) =>
            {
                var result = await service.ImportOfflineAsync(sale, user.ClientName(), ct);
                return result.Replayed ? Results.Ok(result) : Results.Created($"/api/v1/redemptions/{Uri.EscapeDataString(result.TransactionId)}", result);
            })
            .WithSummary("Import a sale priced offline from a snapshot")
            .WithDescription("The sale already happened, so limits are not enforced; usage and budget are still counted. Idempotent on transactionId.")
            .Produces<RedemptionResult>(StatusCodes.Status201Created);

        return app;
    }
}
