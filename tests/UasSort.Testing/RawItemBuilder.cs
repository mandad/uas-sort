// tests/UasSort.Testing/RawItemBuilder.cs
using System.Globalization;

namespace UasSort.Testing;

/// <summary>Builds harvested <see cref="RawItem"/>s directly (no card, no probes) for time, clock and planning tests (Ref §13 fixture).</summary>
public static class RawItemBuilder
{
    public static readonly TimeSpan Eastern = TimeSpan.FromHours(-4);
    public const string Serial = "1581F6Z8C23";

    public static DateTime Stamp(string yyyyMMddHHmmss)
        => DateTime.ParseExact(yyyyMMddHHmmss, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None);

    public static DateTime Utc(int y, int mo, int d, int h, int mi, int s = 0) => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    public static RawItem Vid(string stamp, int n, GeoPoint? loc, TimeSpan? clockOffset = null, bool moov = true,
                              SessionKey? session = null, GpsSource gpsSource = GpsSource.DjmdModelTable, GeoPoint? last = null,
                              DateTime? mvhdUtc = null)
    {
        var s = Stamp(stamp);
        var start = mvhdUtc ?? DateTime.SpecifyKind(s - (clockOffset ?? Eastern), DateTimeKind.Utc);
        var name = string.Create(CultureInfo.InvariantCulture, $"DJI_{stamp}_{n:0000}_D.MP4");
        var rel = "DCIM/DJI_001/" + name;
        var mtime = start.AddSeconds(90);
        var entry = new CardEntry(rel, 100_000_000L + n, mtime, start, mtime, 0x20, EntryClass.Video, null);
        var source = moov ? gpsSource : GpsSource.MdatHeadFallback;
        var path = source == GpsSource.DjmdGenericSearch ? "3-9-1" : null;
        GpsProbe first = loc is { } p
            ? new GpsFix(p, 100, 0, source, path)
            : new NoFix(moov ? NoFixReason.AllProbedSamplesZero : NoFixReason.Unparseable);
        GpsFix? lastFix = last is { } lp ? new GpsFix(lp, 100, 299, source, path) : null;
        var mp4 = new Mp4Info(moov ? start : null, moov, first, lastFix, "dvtm_Air3s.proto",
                              session?.SessionUtc, session?.DroneSerial, null, moov ? TimeSpan.FromSeconds(90) : null);
        return new RawItem(new VideoUnit(new ItemId(rel), entry, false), ItemKind.Video, name, entry.Size, mtime, s, mp4, null, null);
    }

    public static RawItem Dng(string stamp, int n, GeoPoint? loc, TimeSpan? offsetTime = null)
    {
        var s = Stamp(stamp);
        var name = string.Create(CultureInfo.InvariantCulture, $"DJI_{stamp}_{n:0000}_D.DNG");
        var rel = "DCIM/DJI_001/" + name;
        var mtime = DateTime.SpecifyKind(s - Eastern, DateTimeKind.Utc);
        var entry = new CardEntry(rel, 25_000_000L + n, mtime, mtime, mtime, 0x20, EntryClass.Photo, null);
        GpsProbe gps = loc is { } p ? new GpsFix(p, 100, 0, GpsSource.Exif, null) : new NoFix(NoFixReason.NoGpsTag);
        var still = new StillInfo(s, offsetTime, gps, "FC9113", null);
        return new RawItem(new PhotoUnit(new ItemId(rel), entry, null), ItemKind.Photo, name, entry.Size, mtime, s, null, still, null);
    }

    public static RawItem Other(string name, DateTime mtimeUtc, GeoPoint? loc)
    {
        var rel = "DCIM/DJI_001/" + name;
        var mtime = DateTime.SpecifyKind(mtimeUtc, DateTimeKind.Utc);
        var entry = new CardEntry(rel, 3_000_000L, mtime, mtime, mtime, 0x20, EntryClass.Photo, null);
        GpsProbe gps = loc is { } p ? new GpsFix(p, null, 0, GpsSource.Exif, null) : new NoFix(NoFixReason.NoGpsTag);
        var still = new StillInfo(null, null, gps, null, null);
        return new RawItem(new PhotoUnit(new ItemId(rel), entry, null), ItemKind.Photo, name, entry.Size, mtime, null, null, still, null);
    }
}
