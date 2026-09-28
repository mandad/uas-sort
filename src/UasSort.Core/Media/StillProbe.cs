using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace UasSort.Core.Media;

/// <summary>
/// DNG/JPG metadata through MetadataExtractor <b>Stream</b> overloads only (Ref §6.3 Stills): DTO and offset from the
/// EXIF directory that actually has the DTO, GPS, IFD0 model and the IFD0 JPEG thumbnail range. Throws
/// <see cref="ImageProcessingException"/> on an unreadable file (the harvester turns it into ProbeError).
/// </summary>
public static class StillProbe
{
    private const int TagOffsetTimeOriginal = 0x9011;
    private const int TagCompression = 0x0103;
    private const int TagStripOffsets = 0x0111;
    private const int TagStripByteCounts = 0x0117;
    private const int TagJpegOffset = 0x0201;
    private const int TagJpegLength = 0x0202;

    public static StillInfo Read(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);
        s.Position = 0;
        Span<byte> magic = stackalloc byte[4];
        bool isTiff = s.Length >= 4 && s.Read(magic) == 4
                      && (magic.SequenceEqual("II*\0"u8) || magic.SequenceEqual("MM\0*"u8));
        s.Position = 0;
        IReadOnlyList<MetadataExtractor.Directory> dirs = ImageMetadataReader.ReadMetadata(s);

        ExifDirectoryBase? dtoDir = dirs.OfType<ExifDirectoryBase>()
                                        .FirstOrDefault(d => d.ContainsTag(ExifDirectoryBase.TagDateTimeOriginal));
        DateTime? dto = null;
        if (dtoDir is not null && dtoDir.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime taken))
            dto = DateTime.SpecifyKind(taken, DateTimeKind.Unspecified);
        TimeSpan? offset = dtoDir?.GetString(TagOffsetTimeOriginal) is { } text && TryParseOffset(text, out TimeSpan o) ? o : null;

        ExifIfd0Directory? ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
        string? model = ifd0?.GetString(ExifDirectoryBase.TagModel)?.Trim();
        ByteRange? thumb = isTiff && ifd0 is not null ? ThumbRange(ifd0, s.Length) : null;
        return new StillInfo(dto, offset, Gps(dirs), string.IsNullOrEmpty(model) ? null : model, thumb);
    }

    private static GpsProbe Gps(IReadOnlyList<MetadataExtractor.Directory> dirs)
    {
        GpsDirectory? gps = dirs.OfType<GpsDirectory>().FirstOrDefault();
        if (gps is null || !gps.TryGetGeoLocation(out GeoLocation location)) return new NoFix(NoFixReason.NoGpsTag);
        var point = new GeoPoint(location.Latitude, location.Longitude);
        if (double.IsNaN(point.Lat) || double.IsNaN(point.Lon) || !DjmdDecoder.IsFix(point)) return new NoFix(NoFixReason.NoGpsTag);
        double? alt = null;
        if (gps.TryGetRational(GpsDirectory.TagAltitude, out Rational r) && r.Denominator != 0)
        {
            alt = r.ToDouble();
            if (gps.TryGetInt32(GpsDirectory.TagAltitudeRef, out int below) && below == 1) alt = -alt;
        }
        return new GpsFix(point, alt, 0, GpsSource.Exif, null);
    }

    private static ByteRange? ThumbRange(ExifIfd0Directory ifd0, long streamLength)
    {
        long offset, length;
        if (TryInteger(ifd0.GetObject(TagCompression), out long compression) && compression is 6 or 7
            && TryInteger(ifd0.GetObject(TagStripOffsets), out offset) && TryInteger(ifd0.GetObject(TagStripByteCounts), out length))
            return Checked(offset, length, streamLength);
        if (TryInteger(ifd0.GetObject(TagJpegOffset), out offset) && TryInteger(ifd0.GetObject(TagJpegLength), out length))
            return Checked(offset, length, streamLength);
        return null;
    }

    private static ByteRange? Checked(long offset, long length, long streamLength)
        => offset > 0 && length is > 0 and <= int.MaxValue && offset + length <= streamLength
            ? new ByteRange(offset, (int)length) : null;

    /// <summary>A single integer value, or a one-element array of one (a multi-strip thumbnail has no single range).</summary>
    private static bool TryInteger(object? value, out long result)
    {
        switch (value)
        {
            case null or string:
                result = 0;
                return false;
            case Array { Length: 1 } one:
                return TryInteger(one.GetValue(0), out result);
            case Array:
                result = 0;
                return false;
            case IConvertible c:
                result = c.ToInt64(CultureInfo.InvariantCulture);
                return true;
            default:
                result = 0;
                return false;
        }
    }

    /// <summary>Parses EXIF OffsetTime text such as "+08:00" or "-04:00".</summary>
    private static bool TryParseOffset(string text, out TimeSpan offset)
    {
        offset = default;
        string t = text.Trim().TrimEnd('\0');
        if (t.Length != 6 || (t[0] != '+' && t[0] != '-') || t[3] != ':') return false;
        if (!int.TryParse(t.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int h)
            || !int.TryParse(t.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int m)) return false;
        if (h > 14 || m > 59) return false;
        offset = new TimeSpan(h, m, 0);
        if (t[0] == '-') offset = -offset;
        return true;
    }
}
