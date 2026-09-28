using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Carts;
using CampaignEngine.Core.Products;
using CampaignEngine.Core.Rules;

namespace CampaignEngine.Core.Evaluation;

/// <summary>
/// Prices a cart against a catalog of campaigns. Pure and thread-safe: no I/O, no shared state,
/// the same input always gives the same output.
/// </summary>
/// <remarks>See <c>docs/architecture.md</c> §6 for the algorithm.</remarks>
public sealed class PromotionEvaluator(EngineOptions? options = null)
{
    private readonly EngineOptions _options = options ?? new EngineOptions();

    public EvaluationResult Evaluate(Cart cart, CatalogSnapshot catalog, UsageSnapshot? usage = null, bool explain = false)
    {
        var errors = cart.Validate();
        if (errors.Count > 0)
        {
            throw new CartValidationException(errors);
        }

        usage ??= UsageSnapshot.Empty;
        var now = cart.Timestamp ?? _options.TimeProvider.GetUtcNow();
        var rejections = new List<RejectedCampaign>();
        var hints = new List<CampaignHint>();

        var candidates = new List<Candidate>();
        foreach (var campaign in catalog.Campaigns.OrderByDescending(c => c.Priority).ThenBy(c => c.Code, StringComparer.Ordinal))
        {
            var candidate = CheckEligibility(campaign, cart, catalog.Lists, usage.For(campaign.Id), now, out var rejection, out var hint);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
            else
            {
                rejections.Add(rejection!);
                if (hint is not null)
                {
                    hints.Add(ToHint(campaign, hint));
                }
            }
        }

        candidates = ResolveExclusivityGroups(candidates, cart, catalog.Lists, rejections);

        var plans = new List<List<Candidate>> { candidates.Where(c => c.Campaign.Stacking == StackingMode.Stackable).ToList() };
        plans.AddRange(candidates.Where(c => c.Campaign.Stacking == StackingMode.Exclusive).Select(c => new List<Candidate> { c }));

        var outcomes = plans.Where(p => p.Count > 0).Select(p => ApplyPlan(p, cart, catalog.Lists)).ToList();
        var winner = SelectPlan(outcomes);

        foreach (var candidate in candidates)
        {
            if (winner is not null && winner.Plan.Contains(candidate))
            {
                if (!winner.Applications.Any(a => a.Candidate == candidate))
                {
                    rejections.Add(new(candidate.Campaign.Id, candidate.Campaign.Code, RejectionReason.NoDiscount,
                        "The campaign applied but produced no discount (floors, caps or no free units)."));
                }
            }
            else
            {
                rejections.Add(new(candidate.Campaign.Id, candidate.Campaign.Code, RejectionReason.NotSelected,
                    "Another combination of campaigns gives a better result."));
            }
        }

        if (winner is not null)
        {
            hints.AddRange(winner.Applications
                .Where(a => a.Context.Hint is not null)
                .Select(a => ToHint(a.Candidate.Campaign, a.Context.Hint!)));
        }

        return BuildResult(cart, catalog, now, winner, hints, explain ? rejections : null);
    }

    private Candidate? CheckEligibility(
        Campaign campaign, Cart cart, IProductListLookup lists, CampaignUsage usage, DateTimeOffset now,
        out RejectedCampaign? rejection, out ConditionHint? hint)
    {
        hint = null;
        RejectedCampaign Reject(RejectionReason reason, string? detail = null) => new(campaign.Id, campaign.Code, reason, detail);

        if (campaign.Status != CampaignStatus.Active)
        {
            rejection = Reject(RejectionReason.NotActive, $"Status is {campaign.Status}.");
            return null;
        }

        if (campaign.Currency is not null && !string.Equals(campaign.Currency, cart.Currency, StringComparison.OrdinalIgnoreCase))
        {
            rejection = Reject(RejectionReason.CurrencyMismatch);
            return null;
        }

        if (!campaign.Schedule.TryResolveTimeZone(out _))
        {
            rejection = Reject(RejectionReason.InvalidDefinition, $"Unknown time zone '{campaign.Schedule.TimeZone}'.");
            return null;
        }

        if (!campaign.Schedule.IsActiveAt(now))
        {
            rejection = Reject(RejectionReason.OutsideSchedule);
            return null;
        }

        if (campaign.Channels.Count > 0 && !campaign.Channels.Contains(cart.Channel, StringComparer.OrdinalIgnoreCase))
        {
            rejection = Reject(RejectionReason.ChannelMismatch);
            return null;
        }

        if (!campaign.Stores.Matches(cart.StoreId))
        {
            rejection = Reject(RejectionReason.StoreMismatch);
            return null;
        }

        if (campaign.CustomerSegments.Count > 0 &&
            !(cart.Customer?.Segments.Exists(s => campaign.CustomerSegments.Contains(s, StringComparer.OrdinalIgnoreCase)) ?? false))
        {
            rejection = Reject(RejectionReason.SegmentMismatch);
            return null;
        }

        string? coupon = null;
        if (campaign.Coupon is not null)
        {
            coupon = campaign.Coupon.Match(cart.CouponCodes);
            if (coupon is null)
            {
                rejection = Reject(RejectionReason.CouponMissing);
                return null;
            }

            if (campaign.Coupon.MaxUsesPerCode is { } maxPerCode &&
                usage.CouponRedemptions.GetValueOrDefault(coupon) >= maxPerCode)
            {
                rejection = Reject(RejectionReason.CouponExhausted, $"Coupon '{coupon}' has been used up.");
                return null;
            }
        }

        var limits = campaign.Limits;
        if (limits.MaxRedemptions is { } maxRedemptions && usage.Redemptions >= maxRedemptions)
        {
            rejection = Reject(RejectionReason.UsageLimitReached);
            return null;
        }

        if (limits.MaxRedemptionsPerCustomer is { } maxPerCustomer && cart.Customer?.Id is not null &&
            usage.CustomerRedemptions >= maxPerCustomer)
        {
            rejection = Reject(RejectionReason.CustomerLimitReached);
            return null;
        }

        decimal? cap = limits.MaxDiscountPerOrder;
        if (limits.Budget is { } budget)
        {
            var remaining = budget - usage.DiscountTotal;
            if (remaining <= 0)
            {
                rejection = Reject(RejectionReason.BudgetExhausted);
                return null;
            }

            cap = cap is null ? remaining : Math.Min(cap.Value, remaining);
        }

        var qualifying = cart.Lines
            .Where(l => l.Discountable
                        && (campaign.IgnoreGlobalExclusions || !lists.IsGloballyExcluded(l.Sku))
                        && campaign.Target.Matches(l, lists))
            .ToList();
        if (qualifying.Count == 0)
        {
            rejection = Reject(RejectionReason.NoEligibleItems);
            return null;
        }

        var context = new ConditionContext { Cart = cart, Lists = lists, QualifyingLines = qualifying, Decimals = _options.Decimals };
        foreach (var condition in campaign.Conditions)
        {
            var result = condition.Evaluate(context);
            if (!result.IsSatisfied)
            {
                rejection = Reject(RejectionReason.ConditionNotMet, result.Reason);
                hint = result.Hint;
                return null;
            }
        }

        rejection = null;
        return new Candidate(campaign, coupon, cap);
    }

    /// <summary>Keeps only the best campaign (largest stand-alone discount) of each exclusivity group.</summary>
    private List<Candidate> ResolveExclusivityGroups(List<Candidate> candidates, Cart cart, IProductListLookup lists, List<RejectedCampaign> rejections)
    {
        var losers = new HashSet<Candidate>();
        foreach (var group in candidates.Where(c => c.Campaign.ExclusivityGroup is not null)
                     .GroupBy(c => c.Campaign.ExclusivityGroup!, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            // Candidates are already ordered by priority, so ties keep the higher-priority campaign.
            var best = group
                .Select(c => (Candidate: c, Discount: ApplyPlan([c], cart, lists).TotalDiscount))
                .Aggregate((a, b) => b.Discount > a.Discount ? b : a)
                .Candidate;
            foreach (var loser in group.Where(c => c != best))
            {
                losers.Add(loser);
                rejections.Add(new(loser.Campaign.Id, loser.Campaign.Code, RejectionReason.LostInExclusivityGroup,
                    $"'{best.Campaign.Code}' gives a better discount in group '{group.Key}'."));
            }
        }

        return candidates.Where(c => !losers.Contains(c)).ToList();
    }

    private PlanOutcome ApplyPlan(List<Candidate> plan, Cart cart, IProductListLookup lists)
    {
        var state = new PricingState(cart, _options.Decimals);
        var applications = new List<Application>();
        foreach (var candidate in plan)
        {
            var context = new RewardContext(candidate.Campaign, state, lists, candidate.Cap, _options.Decimals);
            candidate.Campaign.Reward.Apply(context);
            if (context.TotalDiscount > 0 || context.Gifts.Count > 0)
            {
                applications.Add(new Application(candidate, context));
            }
        }

        return new PlanOutcome(plan, state, applications);
    }

    private PlanOutcome? SelectPlan(List<PlanOutcome> outcomes)
    {
        var withEffect = outcomes.Where(o => o.Applications.Count > 0).ToList();
        if (withEffect.Count == 0)
        {
            return outcomes.FirstOrDefault();
        }

        // Earlier plans (stackable first, then exclusives by priority) win ties.
        return _options.Selection switch
        {
            PlanSelection.HighestPriority => withEffect
                .Aggregate((a, b) => b.TopPriority > a.TopPriority ? b : a),
            _ => withEffect
                .Aggregate((a, b) => b.TotalDiscount > a.TotalDiscount ||
                                     (b.TotalDiscount == a.TotalDiscount && b.TopPriority > a.TopPriority) ? b : a),
        };
    }

    private EvaluationResult BuildResult(
        Cart cart, CatalogSnapshot catalog, DateTimeOffset now, PlanOutcome? winner,
        List<CampaignHint> hints, List<RejectedCampaign>? rejections)
    {
        var state = winner?.State ?? new PricingState(cart, _options.Decimals);
        var applications = winner?.Applications ?? [];

        var applied = applications.Select(a => new AppliedCampaign
        {
            CampaignId = a.Candidate.Campaign.Id,
            Code = a.Candidate.Campaign.Code,
            Name = a.Candidate.Campaign.Name,
            DisplayMessage = a.Candidate.Campaign.DisplayMessage,
            RewardType = RuleNames.Of(a.Candidate.Campaign.Reward),
            CouponCode = a.Candidate.Coupon,
            LineDiscount = a.Context.LineDiscounts.Values.Sum(),
            ShippingDiscount = a.Context.ShippingDiscount,
            Lines = a.Context.LineDiscounts
                .OrderBy(p => p.Key.Index)
                .Select(p => new AppliedLine(p.Key.Line.LineId, p.Value))
                .ToList(),
            Gifts = a.Context.Gifts.Select(g => new GiftItem(g.Sku, g.Quantity, a.Candidate.Campaign.Code)).ToList(),
            Metadata = new Dictionary<string, string>(a.Candidate.Campaign.Metadata),
        }).ToList();

        var lines = state.Lines.Select(l => new LineResult
        {
            LineId = l.Line.LineId,
            Sku = l.Line.Sku,
            Quantity = l.Line.Quantity,
            UnitPrice = l.Line.UnitPrice,
            Gross = l.Gross,
            Discount = l.Discount,
            Discounts = applications
                .Where(a => a.Context.LineDiscounts.ContainsKey(l))
                .Select(a => new LineDiscount(a.Candidate.Campaign.Code, a.Context.LineDiscounts[l]))
                .ToList(),
        }).ToList();

        var usedCoupons = applied.Where(a => a.CouponCode is not null).Select(a => a.CouponCode!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new EvaluationResult
        {
            CartId = cart.Id,
            Currency = cart.Currency,
            EvaluatedAt = now,
            CatalogVersion = catalog.Version,
            Subtotal = lines.Sum(l => l.Gross),
            LineDiscount = lines.Sum(l => l.Discount),
            ShippingAmount = Money.Round(cart.ShippingAmount, _options.Decimals),
            ShippingDiscount = applied.Sum(a => a.ShippingDiscount),
            AppliedCampaigns = applied,
            Lines = lines,
            Gifts = applied.SelectMany(a => a.Gifts).ToList(),
            Hints = hints,
            UnusedCouponCodes = cart.CouponCodes.Where(c => !usedCoupons.Contains(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Rejections = rejections,
        };
    }

    private static CampaignHint ToHint(Campaign campaign, ConditionHint hint) => new()
    {
        CampaignId = campaign.Id,
        Code = campaign.Code,
        Name = campaign.Name,
        DisplayMessage = campaign.DisplayMessage,
        MissingAmount = hint.MissingAmount,
        MissingQuantity = hint.MissingQuantity,
    };

    private sealed record Candidate(Campaign Campaign, string? Coupon, decimal? Cap);

    private sealed record Application(Candidate Candidate, RewardContext Context);

    private sealed record PlanOutcome(List<Candidate> Plan, PricingState State, List<Application> Applications)
    {
        public decimal TotalDiscount => Applications.Sum(a => a.Context.TotalDiscount);

        public int TopPriority => Plan.Count == 0 ? int.MinValue : Plan.Max(c => c.Campaign.Priority);
    }
}
