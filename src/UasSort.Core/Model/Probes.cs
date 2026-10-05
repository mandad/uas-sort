namespace UasSort.Core;

public enum GpsSource { DjmdModelTable, DjmdGenericSearch, MdatHeadFallback, Exif }
public sealed record GpsFix(GeoPoint Point, double? AltM, int Sample, GpsSource Source, string? FieldPath);
public enum NoFixReason { NotDji, NoDjmdTrack, AllProbedSamplesZero, Unparseable, NoGpsTag, GenericHitImplausible }
public sealed record NoFix(NoFixReason Reason);
public union GpsProbe(GpsFix, NoFix);

public sealed record Mp4Info(DateTime? MvhdUtc, bool HasMoov, GpsProbe First, GpsFix? LastSameField /* generic hits only */,
                             string? Protocol, DateTime? SessionUtc, string? DroneSerial, ByteRange? Thumb,
                             TimeSpan? Duration /* mvhd duration / timescale; null when there is no moov (Ref §6.3) */);

public readonly record struct SessionKey(string? DroneSerial, DateTime SessionUtc)   // SessionUtc = mvhd − uptime µs (Ref §6.3 step 6)
{
    public bool SameSession(SessionKey o) => DroneSerial == o.DroneSerial
                                             && Math.Abs((SessionUtc - o.SessionUtc).TotalSeconds) <= 2;   // never compare with ==
}

public sealed record StillInfo(DateTime? DtoNaive, TimeSpan? OffsetTime, GpsProbe Gps, string? Model, ByteRange? Thumb,
                               string? SubSec = null /* EXIF SubSecTimeOriginal digits; Picture Offload cleanup's Lightroom match */,
                               int? PixelWidth = null, int? PixelHeight = null /* the stitched-panorama shape test of Picture Offload cleanup */);

public sealed record RawItem(MediaUnit Unit, ItemKind Kind, string Name, long Bytes, DateTime CardMtimeUtc,
                             DateTime? DroneStamp /* filename or EXIF DTO, naive */, Mp4Info? Mp4, StillInfo? Still, string? ProbeError);
