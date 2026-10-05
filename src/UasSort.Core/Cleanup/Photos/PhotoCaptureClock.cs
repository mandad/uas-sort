// src/UasSort.Core/Cleanup/Photos/PhotoCaptureClock.cs
namespace UasSort.Core.Cleanup;

/// <summary>A Picture Offload still's capture time and local date from its EXIF, resolved as the scan resolves a still (spec 2026-10-04
/// §3(2), main spec §5.2, TimeResolver.Capture/ZoneFor): DateTimeOriginal − OffsetTimeOriginal when present, else the saved drone clock
/// (no videos to learn from: DroneClock.Learn gives ClockMode.Setting) with the photo's own GPS zone as the site zone; the local date in
/// the photo's own GPS zone, else the PC zone. Only when the clock zone is unknown is the naive calendar date used.</summary>
public sealed class PhotoCaptureClock
{
    private readonly ClockModel _model;
    private readonly ITimeZoneResolver _tz;
    private readonly TimeZoneInfo _pc;

    public PhotoCaptureClock(StoredClockMode mode, string zoneId, ITimeZoneResolver tz, TimeZoneInfo pc)
    {
        ArgumentNullException.ThrowIfNull(zoneId);
        ArgumentNullException.ThrowIfNull(tz);
        ArgumentNullException.ThrowIfNull(pc);
        _model = DroneClock.Learn([], mode, zoneId, tz, pc);
        _tz = tz;
        _pc = pc;
    }

    public static PhotoCaptureClock For(Settings s, ITimeZoneResolver tz, TimeZoneInfo pc)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new PhotoCaptureClock(s.DroneClockMode, s.DroneClockZone, tz, pc);
    }

    public (DateTime? Utc, DateOnly LocalDate) Resolve(StillInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var dto = DateTime.SpecifyKind(info.DtoNaive ?? throw new ArgumentException("no DateTimeOriginal", nameof(info)), DateTimeKind.Unspecified);
        string? own = info.Gps is GpsFix fix && _tz.Resolve(fix.Point) is { IsEtc: false } l && Zones.TryFind(l.IanaId, out _) ? l.IanaId : null;
        DateTime? utc = info.OffsetTime is { } offset ? DateTime.SpecifyKind(dto - offset, DateTimeKind.Utc)
                      : ClockConversion.ToUtc(_model, dto, own)?.Utc;
        if (utc is not { } u) return (null, DateOnly.FromDateTime(dto));
        var zone = own is not null && Zones.TryFind(own, out var z) ? z : _pc;
        return (u, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(u, zone)));
    }
}
