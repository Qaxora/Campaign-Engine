using System.Text.RegularExpressions;

namespace CampaignEngine.Core.Campaigns;

/// <summary>Structural validation of a campaign definition. Returns messages keyed by JSON path.</summary>
public static partial class CampaignValidator
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[A-Za-z]{3}$")]
    private static partial Regex CurrencyPattern();

    public static IReadOnlyList<string> Validate(Campaign campaign)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(campaign.Code) || !CodePattern().IsMatch(campaign.Code))
        {
            errors.Add("code must be 1-64 characters: letters, digits, '-', '_' or '.', starting with a letter or digit.");
        }

        if (string.IsNullOrWhiteSpace(campaign.Name))
        {
            errors.Add("name is required.");
        }

        if (campaign.Currency is not null && !CurrencyPattern().IsMatch(campaign.Currency))
        {
            errors.Add("currency must be a 3-letter ISO 4217 code.");
        }

        if (campaign.ExclusivityGroup is not null && string.IsNullOrWhiteSpace(campaign.ExclusivityGroup))
        {
            errors.Add("exclusivityGroup cannot be blank; omit it instead.");
        }

        ValidateSchedule(campaign.Schedule, errors);
        ValidateLimits(campaign.Limits, errors);

        if (campaign.Coupon is { } coupon)
        {
            if (coupon.Codes.Count == 0 || coupon.Codes.Exists(string.IsNullOrWhiteSpace))
            {
                errors.Add("coupon.codes must contain at least one non-empty code.");
            }
            else if (coupon.Codes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != coupon.Codes.Count)
            {
                errors.Add("coupon.codes must be unique (case-insensitive).");
            }

            if (coupon.MaxUsesPerCode is < 1)
            {
                errors.Add("coupon.maxUsesPerCode must be at least 1.");
            }
        }

        if (campaign.Reward is null)
        {
            errors.Add("reward is required.");
        }
        else
        {
            errors.AddRange(campaign.Reward.Validate("reward"));
        }

        for (var i = 0; i < campaign.Conditions.Count; i++)
        {
            if (campaign.Conditions[i] is null)
            {
                errors.Add($"conditions[{i}] is null.");
                continue;
            }

            errors.AddRange(campaign.Conditions[i].Validate($"conditions[{i}]"));
        }

        return errors;
    }

    private static void ValidateSchedule(Schedule schedule, List<string> errors)
    {
        if (schedule.StartsAt is { } start && schedule.EndsAt is { } end && start >= end)
        {
            errors.Add("schedule.endsAt must be after schedule.startsAt.");
        }

        if (!schedule.TryResolveTimeZone(out _))
        {
            errors.Add($"schedule.timeZone '{schedule.TimeZone}' is not a known time zone.");
        }

        if (schedule.DailyStart is { } from && schedule.DailyEnd is { } to && from == to)
        {
            errors.Add("schedule.dailyStart and schedule.dailyEnd cannot be equal.");
        }
    }

    private static void ValidateLimits(CampaignLimits limits, List<string> errors)
    {
        if (limits.MaxRedemptions is < 1)
        {
            errors.Add("limits.maxRedemptions must be at least 1.");
        }

        if (limits.MaxRedemptionsPerCustomer is < 1)
        {
            errors.Add("limits.maxRedemptionsPerCustomer must be at least 1.");
        }

        if (limits.Budget is <= 0)
        {
            errors.Add("limits.budget must be greater than zero.");
        }

        if (limits.MaxDiscountPerOrder is <= 0)
        {
            errors.Add("limits.maxDiscountPerOrder must be greater than zero.");
        }
    }
}
