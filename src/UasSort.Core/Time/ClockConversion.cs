// src/UasSort.Core/Time/ClockConversion.cs
namespace UasSort.Core.Time;

/// <summary>Drone-clock stamp → UTC through a learned <see cref="ClockModel"/> (Ref §6.1 Modes).</summary>
public static class ClockConversion
{
    public static readonly TimeSpan SampleWindow = TimeSpan.FromDays(60);

    public static (DateTime Utc, TimeSource Src)? ToUtc(ClockModel model, DateTime droneStamp, string? siteZoneId)
    {
        ArgumentNullException.ThrowIfNull(model);
        var stamp = DateTime.SpecifyKind(droneStamp, DateTimeKind.Unspecified);
        if (Offset(model, stamp, siteZoneId) is not { } offset) return null;
        return (DateTime.SpecifyKind(stamp - offset, DateTimeKind.Utc), SourceFor(model.Mode));
    }

    public static TimeSpan OffsetAt(ClockModel model, DateTime droneStamp, string? siteZoneId)
    {
        ArgumentNullException.ThrowIfNull(model);
        var stamp = DateTime.SpecifyKind(droneStamp, DateTimeKind.Unspecified);
        return Offset(model, stamp, siteZoneId) ?? model.Modal ?? TimeSpan.Zero;
    }

    public static TimeSource SourceFor(ClockMode mode) => mode switch
    {
        ClockMode.SiteLocal => TimeSource.DroneClockSiteLocal,
        ClockMode.Zone => TimeSource.DroneClockZone,
        ClockMode.NearestSample => TimeSource.DroneClockSample,
        ClockMode.Setting => TimeSource.DroneClockSetting,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown clock mode."),
    };

    public static ClockSample? Nearest(ImmutableArray<ClockSample> samples, DateTime droneStamp, TimeSpan[]? allowedOffsets)
    {
        ClockSample? best = null;
        var bestGap = TimeSpan.MaxValue;
        foreach (var s in samples)
        {
            if (allowedOffsets is not null && Array.IndexOf(allowedOffsets, s.Offset) < 0) continue;
            var gap = (s.DroneStamp - droneStamp).Duration();
            if (gap > SampleWindow) continue;
            if (best is null || gap < bestGap || (gap == bestGap && s.MvhdUtc < best.MvhdUtc))
            {
                best = s;
                bestGap = gap;
            }
        }
        return best;
    }

    static TimeSpan? Offset(ClockModel m, DateTime stamp, string? siteZoneId) => m.Mode switch
    {
        ClockMode.SiteLocal => ZoneOffset(siteZoneId ?? m.SettingZoneId, stamp, m.Samples),
        ClockMode.Zone => ZoneOffset(m.ZoneId ?? m.SettingZoneId, stamp, m.Samples),
        ClockMode.NearestSample => Nearest(m.Samples, stamp, null)?.Offset ?? m.Modal,
        ClockMode.Setting => ZoneOffset(m.SettingMode == StoredClockMode.SiteLocal ? siteZoneId ?? m.SettingZoneId : m.SettingZoneId,
                                        stamp, m.Samples),
        _ => null,
    };

    static TimeSpan? ZoneOffset(string zoneId, DateTime stamp, ImmutableArray<ClockSample> samples)
    {
        if (!Zones.TryFind(zoneId, out var zone)) return null;
        if (zone.IsAmbiguousTime(stamp))
        {
            // the repeated hour at DST end: the nearest video tells which pass this was (Review Focus 4)
            var valid = zone.GetAmbiguousTimeOffsets(stamp);
            return Nearest(samples, stamp, valid)?.Offset ?? zone.GetUtcOffset(stamp);
        }
        return zone.GetUtcOffset(stamp);   // for a skipped (invalid) time this is the standard offset
    }
}
