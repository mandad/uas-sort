// src/UasSort.Core/Time/DroneClock.cs
namespace UasSort.Core.Time;

/// <summary>Learns the drone clock: SiteLocal first, then a zone, else NearestSample; Setting when no video has a moov (Ref §6.1).</summary>
public static partial class DroneClock
{
    public static readonly ImmutableArray<string> UsZones =
    [
        "America/New_York", "America/Chicago", "America/Denver", "America/Phoenix", "America/Los_Angeles",
        "America/Anchorage", "Pacific/Honolulu",
    ];

    static readonly TimeSpan Quarter = TimeSpan.FromMinutes(15);

    public static ClockModel Learn(IEnumerable<RawItem> items, StoredClockMode settingMode, string settingZoneId,
                                   ITimeZoneResolver tz, TimeZoneInfo pc)
    {
        ArgumentNullException.ThrowIfNull(settingZoneId);
        ArgumentNullException.ThrowIfNull(pc);
        var samples = Samples(items, tz);
        if (samples.IsEmpty)
            return new ClockModel(ClockMode.Setting, settingMode == StoredClockMode.Zone ? settingZoneId : null,
                                  samples, null, settingMode, settingZoneId);

        var modal = Modal(samples);
        if (SiteLocalFits(samples))
            return new ClockModel(ClockMode.SiteLocal, null, samples, modal, settingMode, settingZoneId);

        foreach (var id in Candidates(settingZoneId, pc))
            if (Zones.TryFind(id, out var zone) && samples.All(s => ZoneFits(zone, s)))
                return new ClockModel(ClockMode.Zone, id, samples, modal, settingMode, settingZoneId);

        return new ClockModel(ClockMode.NearestSample, null, samples, modal, settingMode, settingZoneId);
    }

    public static ImmutableArray<ClockSample> Samples(IEnumerable<RawItem> items, ITimeZoneResolver tz)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(tz);
        var list = new List<ClockSample>();
        foreach (var r in items)
        {
            if (r.Kind != ItemKind.Video || r.DroneStamp is not { } stamp) continue;
            if (r.Mp4 is not { HasMoov: true, MvhdUtc: { } mvhd } mp4) continue;
            var utc = DateTime.SpecifyKind(mvhd, DateTimeKind.Utc);
            var local = DateTime.SpecifyKind(stamp, DateTimeKind.Unspecified);
            list.Add(new ClockSample(local, utc, Round15(local - utc), SiteZone(mp4, tz)));
        }
        return [.. list.OrderBy(s => s.MvhdUtc).ThenBy(s => s.DroneStamp)];
    }

    public static TimeSpan Round15(TimeSpan offset)
        => TimeSpan.FromTicks((long)Math.Round(offset.Ticks / (double)Quarter.Ticks, MidpointRounding.AwayFromZero) * Quarter.Ticks);

    static string? SiteZone(Mp4Info mp4, ITimeZoneResolver tz)
    {
        if (mp4.First is not GpsFix fix || fix.Source != GpsSource.DjmdModelTable) return null;
        var lookup = tz.Resolve(fix.Point);
        return !lookup.IsEtc && Zones.TryFind(lookup.IanaId, out _) ? lookup.IanaId : null;
    }

    static bool SiteLocalFits(ImmutableArray<ClockSample> samples)
    {
        var any = false;
        foreach (var s in samples)
        {
            if (s.SiteZoneId is null) continue;
            if (!Zones.TryFind(s.SiteZoneId, out var zone) || zone.GetUtcOffset(s.MvhdUtc) != s.Offset) return false;
            any = true;
        }
        return any;
    }

    static bool ZoneFits(TimeZoneInfo zone, ClockSample s)
    {
        var local = DateTime.SpecifyKind(s.DroneStamp, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) return false;
        if (zone.IsAmbiguousTime(local)) return Array.IndexOf(zone.GetAmbiguousTimeOffsets(local), s.Offset) >= 0;
        return zone.GetUtcOffset(local) == s.Offset;
    }

    static IEnumerable<string> Candidates(string settingZoneId, TimeZoneInfo pc)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<string> all = [settingZoneId, .. UsZones, Zones.IanaId(pc)];
        foreach (var id in all)
            if (seen.Add(id)) yield return id;
    }

    static TimeSpan Modal(ImmutableArray<ClockSample> samples)
        => samples.GroupBy(s => s.Offset)
                  .OrderByDescending(g => g.Count())
                  .ThenBy(g => g.Min(s => s.MvhdUtc))
                  .First().Key;
}
