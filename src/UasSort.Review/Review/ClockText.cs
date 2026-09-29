// src/UasSort.Review/Review/ClockText.cs
namespace UasSort.Review;

/// <summary>Clock banner, clock-mismatch InfoBar, time cells and tooltips (Ref §6.1, §6.5, §9.2, §9.4, §9.5).</summary>
public static class ClockText
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public const string WhyText =
        "The drone stamps file names with the RC 2's clock, which follows the RC's time-zone setting, not GPS. uas-sort uses the true UTC time in each video and the time zone of where it was shot.";

    private const string FixText =
        "To fix the drone clock: RC 2 → Settings → System → Date & time → turn off the network-provided time zone and set the zone for where you are flying. Network time zones can be wrong on ship or hotel Wi-Fi, and the RC keeps the last one it saw until it reconnects.";

    public static string SourceText(TimeSource s) => s switch
    {
        TimeSource.Mvhd => "from the video (mvhd)",
        TimeSource.ExifWithOffset => "from the photo (EXIF with offset)",
        TimeSource.DroneClockSiteLocal => "estimated from the drone clock (local time at the site)",
        TimeSource.DroneClockZone => "estimated from the drone clock (its time zone)",
        TimeSource.DroneClockSample => "estimated from the drone clock (nearest video)",
        TimeSource.DroneClockSetting => "estimated from the drone clock (last learned setting)",
        TimeSource.Mtime => "from the card file's modified time",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "unknown TimeSource"),
    };

    public static string SourceGlyph(TimeSource s) => s switch
    {
        TimeSource.Mvhd => "",
        TimeSource.ExifWithOffset => "",
        TimeSource.DroneClockSiteLocal or TimeSource.DroneClockZone or TimeSource.DroneClockSample or TimeSource.DroneClockSetting => "",
        TimeSource.Mtime => "",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "unknown TimeSource"),
    };

    public static bool IsEstimated(TimeSource s)
        => s is TimeSource.DroneClockSiteLocal or TimeSource.DroneClockZone or TimeSource.DroneClockSample or TimeSource.DroneClockSetting;

    public static string TimeCell(Item item)
        => (IsEstimated(item.Time.Source) ? "~" : "") + Fmt.Clock(item.Time.LocalTime) + " " + ZoneNames.Abbreviation(item.Time.TzId, item.Time.CaptureUtc);

    /// <summary>The clock banner is Core's text (<see cref="ClockSummary.Headline"/>, from DroneClock.Summarize); Review only shows it.</summary>
    public static string Banner(ClockSummary s, ClockModel model, IReadOnlyList<Item> items)
    {
        ArgumentNullException.ThrowIfNull(s);
        _ = model;   // the registry signature keeps the clock model and the items; Core already summarised both into Headline
        _ = items;
        return s.Headline;
    }

    public static string TimeTooltip(Item item)
    {
        var utc = item.Time.CaptureUtc;
        var parts = new List<string> { utc.ToString("HH:mm:ss", C) + " UTC" };
        if (item.Raw.DroneStamp is { } stamp)
            parts.Add($"drone clock {stamp.ToString("HH:mm:ss", C)} ({Fmt.Offset(DroneOffset(item)!.Value)})");
        parts.Add($"{item.Time.LocalTime.ToString("HH:mm:ss", C)} {ZoneNames.Abbreviation(item.Time.TzId, utc)} ({Fmt.Offset(ZoneNames.OffsetAt(item.Time.TzId, utc))})");
        parts.Add(SourceText(item.Time.Source));
        if (item.Flags.HasFlag(ItemFlags.ClockMismatch)) parts.Add("Drone clock ≠ local time");
        return string.Join(" · ", parts);
    }

    public static string ChipTooltip(Item item)
    {
        var clock = DroneOffset(item) ?? TimeSpan.Zero;
        var site = ZoneNames.OffsetAt(item.Time.TzId, item.Time.CaptureUtc);
        return $"The drone clock ({Fmt.Offset(clock)}) doesn't match local time here ({Fmt.Offset(site)}). Dates use local time.";
    }

    public static string? MismatchInfoBar(ClockSummary s, ClockModel model, IReadOnlyList<Item> items)
    {
        if (s.MismatchItems == 0) return null;
        var flagged = items.Where(i => i.Flags.HasFlag(ItemFlags.ClockMismatch)).ToList();
        var clock = flagged.Select(DroneOffset).FirstOrDefault(o => o is not null);
        var zone = ClockZone(model);
        var clockText = (clock is { } c ? Fmt.Offset(c) : "a different time") + (zone is null ? "" : $" ({zone})");
        return $"Drone clock is set to {clockText}, but footage on this card was shot in {Sites(s, flagged)}. Dates here use local time at each site. {FixText}";
    }

    internal static TimeSpan? DroneOffset(Item item)
        => item.Raw.DroneStamp is { } stamp ? DroneClock.Round15(stamp - item.Time.CaptureUtc) : null;

    private static string? ClockZone(ClockModel m) => m.Mode switch
    {
        ClockMode.Zone => m.ZoneId,
        ClockMode.Setting when m.SettingMode == StoredClockMode.Zone => m.SettingZoneId,
        _ => null,
    };

    private static string Sites(ClockSummary s, IReadOnlyList<Item> flagged)
        => string.Join(", ", s.MismatchSiteZones.Select(z =>
        {
            var at = flagged.FirstOrDefault(i => string.Equals(i.Time.TzId, z, StringComparison.Ordinal))?.Time.CaptureUtc;
            return at is { } u ? $"{ZoneNames.Region(z)} ({Fmt.Offset(ZoneNames.OffsetAt(z, u))})" : ZoneNames.Region(z);
        }));
}
