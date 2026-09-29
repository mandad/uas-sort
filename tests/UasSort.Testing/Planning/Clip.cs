// tests/UasSort.Testing/Planning/Clip.cs
using System.Collections.Immutable;
using System.Globalization;

namespace UasSort.Testing.Planning;

/// <summary>Port of the spike's vid()/dng() helpers: mvhd = stamp + 4 h as UTC, mtime = mvhd + 90 s.</summary>
public static class Clip
{
    public const string Serial = "TESTSERIAL";

    public static DateTime Stamp(string stamp) =>
        DateTime.SpecifyKind(DateTime.ParseExact(stamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture), DateTimeKind.Unspecified);

    public static DateTime Utc(string stamp, int clockMinusUtcHours = -4) =>
        DateTime.SpecifyKind(Stamp(stamp).AddHours(-clockMinusUtcHours), DateTimeKind.Utc);

    public static ItemId Id(this RawItem r) => r.Unit.Id;

    public static RawItem Vid(string stamp, int n, GeoPoint? loc = null, long? size = null, bool moov = true,
                              DateTime? session = null, int clockMinusUtcHours = -4)
    {
        var dc = Stamp(stamp);
        var utc = Utc(stamp, clockMinusUtcHours);
        var name = $"DJI_{stamp}_{n:0000}_D.MP4";
        var rel = $"DCIM/DJI_001/{name}";
        var bytes = size ?? 100_000_000L + n;
        var mtime = utc.AddSeconds(90);
        var entry = new CardEntry(rel, bytes, mtime, mtime, mtime, 0x20, EntryClass.Video, null);
        GpsProbe first;
        if (loc is { } p) first = new GpsFix(p, 100, 0, moov ? GpsSource.DjmdModelTable : GpsSource.MdatHeadFallback, moov ? "3-3-4-1" : null);
        else first = new NoFix(NoFixReason.AllProbedSamplesZero);
        var info = new Mp4Info(moov ? utc : (DateTime?)null, moov, first, null, "dvtm_Air3s.proto",
                               session, session is null ? null : Serial, null, moov ? TimeSpan.FromSeconds(60) : (TimeSpan?)null);
        return new RawItem(new VideoUnit(new ItemId(rel), entry, false), ItemKind.Video, name, bytes, mtime, dc, info, null, null);
    }

    public static RawItem Dng(string stamp, int n, GeoPoint? loc = null, long? size = null, bool withJpgTwin = false)
    {
        var dc = Stamp(stamp);
        var name = $"DJI_{stamp}_{n:0000}_D.DNG";
        var rel = $"DCIM/DJI_001/{name}";
        var bytes = size ?? 25_000_000L + n;
        var mtime = Utc(stamp);
        var entry = new CardEntry(rel, bytes, mtime, mtime, mtime, 0x20, EntryClass.Photo, null);
        CardEntry? twin = withJpgTwin
            ? new CardEntry($"DCIM/DJI_001/DJI_{stamp}_{n:0000}_D.JPG", 8_000_000L + n, mtime, mtime, mtime, 0x20, EntryClass.PhotoTwin, null)
            : null;
        GpsProbe gps;
        if (loc is { } p) gps = new GpsFix(p, null, 0, GpsSource.Exif, null);
        else gps = new NoFix(NoFixReason.NoGpsTag);
        var still = new StillInfo(dc, null, gps, "FC9113", null);
        return new RawItem(new PhotoUnit(new ItemId(rel), entry, twin), ItemKind.Photo, name, bytes, mtime, dc, null, still, null);
    }

    public static RawItem Set(string setName, string firstDto, GeoPoint? loc, params (string Name, long Size, DateTime MtimeUtc)[] members)
    {
        var dto = DateTime.SpecifyKind(DateTime.ParseExact(firstDto, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), DateTimeKind.Unspecified);
        var dir = $"DCIM/PANORAMA/{setName}";
        var entries = members.Select(m => new CardEntry($"{dir}/{m.Name}", m.Size, m.MtimeUtc, m.MtimeUtc, m.MtimeUtc, 0x20,
                                                        EntryClass.SetMember, null)).ToImmutableArray();
        GpsProbe gps;
        if (loc is { } p) gps = new GpsFix(p, null, 0, GpsSource.Exif, null);
        else gps = new NoFix(NoFixReason.NoGpsTag);
        var still = new StillInfo(dto, null, gps, "FC9113", null);
        return new RawItem(new SetUnit(new ItemId(dir), SetKind.Panorama, setName, entries), ItemKind.Set, setName,
                           members.Sum(m => m.Size), members[0].MtimeUtc, dto, null, still, null);
    }

    /// <summary>A non-DJI clip (Autel MAX_####): no mvhd, no stamp, so TimeResolver times it from mtime.</summary>
    public static RawItem Autel(string name, long size, DateTime mtimeUtc)
    {
        var rel = $"DCIM/100MEDIA/{name}";
        var entry = new CardEntry(rel, size, mtimeUtc, mtimeUtc, mtimeUtc, 0x20, EntryClass.Video, null);
        return new RawItem(new VideoUnit(new ItemId(rel), entry, false), ItemKind.Video, name, size, mtimeUtc, null, null, null, null);
    }
}
