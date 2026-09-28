// src/UasSort.Core/Time/TimeFlags.cs
namespace UasSort.Core.Time;

/// <summary>The time-window and clock flags of Ref §6.5. Info only: none of them changes a date.</summary>
public static class TimeFlags
{
    public static readonly TimeSpan MismatchThreshold = TimeSpan.FromMinutes(15);
    public static readonly DateTime EarliestPlausibleUtc = new(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public const double AlternativesWindowMinutes = 60;
    public const double ClockWindowMinutes = 75;

    public static bool IsClockMismatch(TimeSpan droneOffset, TimeSpan siteOffset)
        => (droneOffset - siteOffset).Duration() >= MismatchThreshold;

    public static double MinutesFromMidnight(DateTime local)
    {
        var minutes = local.TimeOfDay.TotalMinutes;
        return Math.Min(minutes, 1440 - minutes);
    }

    public static bool IsClockSource(TimeSource s) => s is TimeSource.Mvhd or TimeSource.DroneClockSiteLocal
        or TimeSource.DroneClockZone or TimeSource.DroneClockSample or TimeSource.DroneClockSetting;

    public static ItemFlags For(RawItem raw, ItemTime time, TzLookup? ownLookup, ClockModel clock, TimeZoneInfo zone, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(zone);
        var f = ItemFlags.None;

        if (time.CaptureUtc < EarliestPlausibleUtc || time.CaptureUtc > nowUtc.AddDays(1)) f |= ItemFlags.ClockNotSet;

        var fromMidnight = MinutesFromMidnight(time.LocalTime);
        var hasAlternatives = time.TzSource == TzSource.Gps && ownLookup is { } l && !l.Alternatives.IsDefaultOrEmpty;
        if ((hasAlternatives && fromMidnight <= AlternativesWindowMinutes) || (IsClockSource(time.Source) && fromMidnight <= ClockWindowMinutes))
            f |= ItemFlags.CheckDate;

        if (time.Source == TimeSource.DroneClockSetting) f |= ItemFlags.ClockFromSetting;

        if (raw.DroneStamp is { } stamp && time.TzSource != TzSource.PcZone && time.Source != TimeSource.Mtime
            && IsClockMismatch(clock.OffsetAt(stamp, time.TzId), zone.GetUtcOffset(DateTime.SpecifyKind(time.CaptureUtc, DateTimeKind.Utc))))
            f |= ItemFlags.ClockMismatch;

        return f;
    }
}
