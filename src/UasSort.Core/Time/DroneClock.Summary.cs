// src/UasSort.Core/Time/DroneClock.Summary.cs
using System.Globalization;
using System.Text;

namespace UasSort.Core.Time;

public static partial class DroneClock
{
    /// <summary>The clock banner data (Ref §6.1 "Header text", §9.2): headline, clock changes, and the ClockMismatch zones.</summary>
    public static ClockSummary Summarize(ClockModel clock, IReadOnlyList<ResolvedItem> items)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(items);
        var mismatched = items.Where(i => i.Flags.HasFlag(ItemFlags.ClockMismatch)).OrderBy(i => i.Time.CaptureUtc).ToList();
        var zones = mismatched.Select(i => i.Time.TzId).Distinct(StringComparer.Ordinal).ToImmutableArray();
        var runs = clock.Mode == ClockMode.NearestSample ? ChangeRuns(clock.Samples) : [];
        var headline = Headline(clock, runs) + MismatchLine(zones);
        return new ClockSummary(clock.Mode, clock.ZoneId, clock.Samples.Length, headline, mismatched.Count, zones,
                                [.. runs.Select(r => r.Change)]);
    }

    public static ImmutableArray<ClockChange> Changes(ImmutableArray<ClockSample> samples) => [.. ChangeRuns(samples).Select(r => r.Change)];

    /// <summary>After a successful run (Ref §6.1): SiteLocal saves the mode only; a fitted zone saves mode and zone; nothing else saves.</summary>
    public static Settings ApplyLearned(Settings current, ClockModel clock)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(clock);
        return clock.Mode switch
        {
            ClockMode.SiteLocal => current with { DroneClockMode = StoredClockMode.SiteLocal },
            ClockMode.Zone when clock.ZoneId is { } zone => current with { DroneClockMode = StoredClockMode.Zone, DroneClockZone = zone },
            _ => current,
        };
    }

    static List<(ClockChange Change, string? LaterZone)> ChangeRuns(ImmutableArray<ClockSample> samples)
    {
        var sorted = samples.OrderBy(s => s.MvhdUtc).ThenBy(s => s.DroneStamp).ToList();
        var runs = new List<(ClockChange, string?)>();
        for (var k = 1; k < sorted.Count; k++)
        {
            var (prev, next) = (sorted[k - 1], sorted[k]);
            if (prev.Offset == next.Offset) continue;
            var at = prev.MvhdUtc + TimeSpan.FromTicks((next.MvhdUtc - prev.MvhdUtc).Ticks / 2);
            runs.Add((new ClockChange(DateTime.SpecifyKind(at, DateTimeKind.Utc), prev.Offset, next.Offset), next.SiteZoneId));
        }
        return runs;
    }

    static string Headline(ClockModel c, List<(ClockChange Change, string? LaterZone)> runs)
    {
        var n = c.Samples.Length;
        var videos = n == 1 ? "1 video" : string.Create(CultureInfo.InvariantCulture, $"{n} videos");
        return c.Mode switch
        {
            ClockMode.Zone => $"Drone clock: {ZoneNames.ClockName(c.ZoneId ?? c.SettingZoneId)} ({c.ZoneId ?? c.SettingZoneId}), "
                              + $"learned from {videos}. Folder dates use local time at each site.",
            ClockMode.SiteLocal => $"Drone clock: follows local time at each site, learned from {videos}.",
            ClockMode.NearestSample => "Drone clock: no single time zone fits these videos, so each item uses the offset of the nearest video in time."
                                       + ChangeLine(runs),
            ClockMode.Setting => c.SettingMode == StoredClockMode.SiteLocal
                ? "Drone clock: no videos on this card: using the last learned clock, local time at each site."
                : $"Drone clock: no videos on this card: using the last learned clock, {ZoneNames.ClockName(c.SettingZoneId)} ({c.SettingZoneId}).",
            _ => throw new ArgumentOutOfRangeException(nameof(c), c.Mode, "Unknown clock mode."),
        };
    }

    static string ChangeLine(List<(ClockChange Change, string? LaterZone)> runs)
    {
        if (runs.Count == 0) return "";
        var sb = new StringBuilder(" Drone clock changed during this card: ").Append(ZoneNames.FormatOffset(runs[0].Change.From));
        foreach (var (change, laterZone) in runs)
            sb.Append(" until ").Append(ZoneNames.FormatLocal(change.AtUtc, laterZone))
              .Append(", then ").Append(ZoneNames.FormatOffset(change.To));
        return sb.Append('.').ToString();
    }

    static string MismatchLine(ImmutableArray<string> zones)
    {
        if (zones.IsEmpty) return "";
        var regions = zones.Select(ZoneNames.Region).Distinct(StringComparer.Ordinal);
        return $" It doesn't match local time where this card was shot ({string.Join(", ", regions)}).";
    }
}
