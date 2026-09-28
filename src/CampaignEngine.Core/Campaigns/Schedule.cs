using System.Collections.Concurrent;

namespace CampaignEngine.Core.Campaigns;

/// <summary>
/// When a campaign is valid. Date boundaries are absolute instants; the weekly/daily window is
/// interpreted in <see cref="TimeZone"/> so that "every Saturday 10:00–14:00" means local store time.
/// </summary>
public sealed class Schedule
{
    private static readonly ConcurrentDictionary<string, TimeZoneInfo?> TimeZones = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Inclusive start. Null means "since forever".</summary>
    public DateTimeOffset? StartsAt { get; set; }

    /// <summary>Exclusive end. Null means "until stopped".</summary>
    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>IANA (<c>Europe/Istanbul</c>) or Windows time zone id.</summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>If not empty, only on these days (local time).</summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];

    /// <summary>Daily window start (local time, inclusive). A window may cross midnight, e.g. 22:00–02:00.</summary>
    public TimeOnly? DailyStart { get; set; }

    /// <summary>Daily window end (local time, exclusive).</summary>
    public TimeOnly? DailyEnd { get; set; }

    public bool IsActiveAt(DateTimeOffset instant)
    {
        if (StartsAt is { } start && instant < start)
        {
            return false;
        }

        if (EndsAt is { } end && instant >= end)
        {
            return false;
        }

        if (DaysOfWeek.Count == 0 && DailyStart is null && DailyEnd is null)
        {
            return true;
        }

        var local = TimeZoneInfo.ConvertTime(instant, ResolveTimeZone());
        if (DaysOfWeek.Count > 0 && !DaysOfWeek.Contains(local.DayOfWeek))
        {
            return false;
        }

        return IsInDailyWindow(TimeOnly.FromDateTime(local.DateTime));
    }

    public bool HasEnded(DateTimeOffset now) => EndsAt is { } end && now >= end;

    private bool IsInDailyWindow(TimeOnly time)
    {
        var from = DailyStart ?? TimeOnly.MinValue;
        var to = DailyEnd;
        if (to is null)
        {
            return time >= from;
        }

        return from <= to
            ? time >= from && time < to
            : time >= from || time < to; // window crosses midnight
    }

    public bool TryResolveTimeZone(out TimeZoneInfo zone)
    {
        zone = TimeZones.GetOrAdd(TimeZone, static id =>
            TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) ? found : null)!;
        return zone is not null;
    }

    private TimeZoneInfo ResolveTimeZone() =>
        TryResolveTimeZone(out var zone)
            ? zone
            : throw new InvalidOperationException($"Unknown time zone '{TimeZone}'.");
}
