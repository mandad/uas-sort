using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace UasSort.Testing;

/// <summary>A built synthetic DNG and where its IFD0 thumbnail sits.</summary>
public sealed record SyntheticDng(byte[] Bytes, ByteRange? Thumb);

/// <summary>
/// Minimal little-endian TIFF/DNG (Ref §13): IFD0 (NewSubFileType 1, 160×120 JPEG thumbnail strip, Make, Model,
/// an IFD0 DateTime distractor, DNGVersion), a raw sub-IFD without DTO (tag 0x14A), an EXIF IFD holding the DTO and
/// optional OffsetTimeOriginal, and a GPS IFD. No user data. <see cref="Build"/> returns the bytes, <see cref="BuildFile"/> the thumbnail range too.
/// </summary>
public sealed record class SyntheticDngBuilder
{
    public static readonly DateTime Pano0001Dto = new(2026, 5, 25, 9, 30, 28);
    public static readonly GeoPoint Pano0001Gps = new(57.799648, -152.390180);

    public DateTime? Dto { get; init; } = Pano0001Dto;
    /// <summary>IFD0 DateTime (0x132), which is not the DTO; null = absent.</summary>
    public DateTime? Ifd0DateTime { get; init; } = new DateTime(2026, 5, 25, 9, 31, 0);
    /// <summary>EXIF OffsetTimeOriginal (0x9011), e.g. "-08:00"; null = absent.</summary>
    public string? OffsetTimeOriginal { get; init; }
    /// <summary>EXIF SubSecTimeOriginal (0x9291), e.g. "045"; null = absent.</summary>
    public string? SubSecTimeOriginal { get; init; }
    /// <summary>EXIF PixelXDimension/PixelYDimension (0xA002/0xA003); null = absent.</summary>
    public (int Width, int Height)? ExifPixels { get; init; }
    public GeoPoint? Gps { get; init; } = Pano0001Gps;
    public double? AltM { get; init; } = 41.5;
    public string Make { get; init; } = "DJI";
    public string Model { get; init; } = "FC9113";
    public bool IncludeRawSubIfd { get; init; } = true;
    /// <summary>IFD0 JPEG strip; empty = no thumbnail tags.</summary>
    public ImmutableArray<byte> Thumbnail { get; init; } = SyntheticMp4Builder.TinyJpeg;

    private const ushort TypeByte = 1, TypeAscii = 2, TypeShort = 3, TypeLong = 4, TypeRational = 5, TypeUndefined = 7;

    private sealed record Entry(ushort Tag, ushort Type, uint Count, byte[] Value);

    public SyntheticDngBuilder WithDateTimeOriginal(DateTime naive) => this with { Dto = naive };
    public SyntheticDngBuilder WithSubSec(string digits) => this with { SubSecTimeOriginal = digits };
    public SyntheticDngBuilder WithGps(GeoPoint point) => this with { Gps = point };

    public SyntheticDngBuilder WithModel(string model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return this with { Model = model };
    }

    public SyntheticDngBuilder WithThumbnail(byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        return this with { Thumbnail = [.. jpeg] };
    }

    /// <summary>The DNG bytes.</summary>
    public byte[] Build() => BuildFile().Bytes;

    /// <summary>The DNG bytes and the thumbnail range.</summary>
    public SyntheticDng BuildFile()
    {
        byte[] thumb = [.. Thumbnail.IsDefault ? [] : Thumbnail];
        byte[] rawData = new byte[32];

        // Pass 1 with zero pointers to size the IFDs; pointer values are inline LONGs, so sizes don't change.
        (List<Entry> ifd0, List<Entry> raw, List<Entry> exif, List<Entry>? gps) = Ifds(0, 0, 0, 0, 0, thumb.Length);
        uint ifd0Offset = 8;
        uint rawOffset = ifd0Offset + (uint)IfdSize(ifd0);
        uint exifOffset = rawOffset + (IncludeRawSubIfd ? (uint)IfdSize(raw) : 0);
        uint gpsOffset = exifOffset + (uint)IfdSize(exif);
        uint thumbOffset = gpsOffset + (gps is null ? 0 : (uint)IfdSize(gps));
        uint rawDataOffset = thumbOffset + (uint)thumb.Length + (uint)(thumb.Length % 2);

        (ifd0, raw, exif, gps) = Ifds(rawOffset, exifOffset, gpsOffset, thumbOffset, rawDataOffset, thumb.Length);
        var file = new MemoryStream();
        file.Write("II*\0"u8);
        file.Write(U32(ifd0Offset));
        file.Write(Serialize(ifd0, ifd0Offset));
        if (IncludeRawSubIfd) file.Write(Serialize(raw, rawOffset));
        file.Write(Serialize(exif, exifOffset));
        if (gps is not null) file.Write(Serialize(gps, gpsOffset));
        file.Write(thumb);
        if (thumb.Length % 2 == 1) file.WriteByte(0);
        file.Write(rawData);
        return new SyntheticDng(file.ToArray(), thumb.Length == 0 ? null : new ByteRange(thumbOffset, thumb.Length));
    }

    private (List<Entry> Ifd0, List<Entry> Raw, List<Entry> Exif, List<Entry>? Gps) Ifds(
        uint rawOffset, uint exifOffset, uint gpsOffset, uint thumbOffset, uint rawDataOffset, int thumbLength)
    {
        var ifd0 = new List<Entry>
        {
            LongEntry(0x00FE, 1), LongEntry(0x0100, 160), LongEntry(0x0101, 120),
            Ascii(0x010F, Make), Ascii(0x0110, Model),
            ShortEntry(0x0115, 3), LongEntry(0x0116, 120), LongEntry(0x8769, exifOffset),
            new(0xC612, TypeByte, 4, [1, 4, 0, 0]),
        };
        if (thumbLength > 0)
        {
            ifd0.Add(ShortEntry(0x0103, 7));
            ifd0.Add(LongEntry(0x0111, thumbOffset));
            ifd0.Add(LongEntry(0x0117, (uint)thumbLength));
        }
        if (Ifd0DateTime is { } modified) ifd0.Add(Ascii(0x0132, modified.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture)));
        if (IncludeRawSubIfd) ifd0.Add(LongEntry(0x014A, rawOffset));
        if (Gps is not null) ifd0.Add(LongEntry(0x8825, gpsOffset));

        var raw = new List<Entry>
        {
            LongEntry(0x00FE, 0), LongEntry(0x0100, 4), LongEntry(0x0101, 4), ShortEntry(0x0102, 16), ShortEntry(0x0103, 1),
            LongEntry(0x0111, rawDataOffset), LongEntry(0x0117, 32),
        };

        var exif = new List<Entry> { new(0x9000, TypeUndefined, 4, Encoding.ASCII.GetBytes("0231")) };
        if (Dto is { } taken) exif.Add(Ascii(0x9003, taken.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture)));
        if (OffsetTimeOriginal is { } offset) exif.Add(Ascii(0x9011, offset));
        if (SubSecTimeOriginal is { } subSec) exif.Add(Ascii(0x9291, subSec));
        if (ExifPixels is { } px)                                            // after 0x9291: IFD entries stay sorted by tag
        {
            exif.Add(LongEntry(0xA002, (uint)px.Width));
            exif.Add(LongEntry(0xA003, (uint)px.Height));
        }

        List<Entry>? gps = null;
        if (Gps is { } p)
        {
            gps =
            [
                new(0x0000, TypeByte, 4, [2, 3, 0, 0]),
                Ascii(0x0001, p.Lat >= 0 ? "N" : "S"), Dms(0x0002, p.Lat),
                Ascii(0x0003, p.Lon >= 0 ? "E" : "W"), Dms(0x0004, p.Lon),
            ];
            if (AltM is { } alt)
            {
                gps.Add(new Entry(0x0005, TypeByte, 1, [alt < 0 ? (byte)1 : (byte)0]));
                gps.Add(new Entry(0x0006, TypeRational, 1, Concat(U32((uint)Math.Round(Math.Abs(alt) * 1000)), U32(1000))));
            }
        }
        return (ifd0, raw, exif, gps);
    }

    private static Entry LongEntry(ushort tag, uint v) => new(tag, TypeLong, 1, U32(v));

    private static Entry ShortEntry(ushort tag, ushort v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        return new Entry(tag, TypeShort, 1, b);
    }

    private static Entry Ascii(ushort tag, string text)
    {
        byte[] b = Encoding.ASCII.GetBytes(text + "\0");
        return new Entry(tag, TypeAscii, (uint)b.Length, b);
    }

    private static Entry Dms(ushort tag, double degrees)
    {
        double a = Math.Abs(degrees);
        uint d = (uint)Math.Floor(a);
        double minutes = (a - d) * 60;
        uint m = (uint)Math.Floor(minutes);
        uint s = (uint)Math.Round((minutes - m) * 60 * 1_000_000);
        return new Entry(tag, TypeRational, 3, Concat(U32(d), U32(1), U32(m), U32(1), U32(s), U32(1_000_000)));
    }

    private static int IfdSize(List<Entry> entries)
        => 2 + 12 * entries.Count + 4 + entries.Where(e => e.Value.Length > 4).Sum(e => e.Value.Length + e.Value.Length % 2);

    private static byte[] Serialize(List<Entry> entries, uint at)
    {
        List<Entry> sorted = [.. entries.OrderBy(e => e.Tag)];
        var head = new MemoryStream();
        var overflow = new MemoryStream();
        uint overflowStart = at + 2 + 12 * (uint)sorted.Count + 4;
        var count = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(count, (ushort)sorted.Count);
        head.Write(count);
        foreach (Entry e in sorted)
        {
            var entry = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(entry, e.Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(2), e.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), e.Count);
            if (e.Value.Length <= 4)
            {
                e.Value.CopyTo(entry, 8);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(8), overflowStart + (uint)overflow.Length);
                overflow.Write(e.Value);
                if (e.Value.Length % 2 == 1) overflow.WriteByte(0);
            }
            head.Write(entry);
        }
        head.Write(U32(0));
        head.Write(overflow.ToArray());
        return head.ToArray();
    }

    private static byte[] U32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];
}
