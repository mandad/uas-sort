// Writes the checked-in selftest assets into src/UasSort.App/SelfTest/ (Ref §2.3, §13). No user data.
// usage (from the repo root): dotnet run tools/fixtures/make-selftest-assets.cs -- .
using System.Buffers.Binary;
using System.Text;

var repoRoot = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var selfTestDir = Path.Join(repoRoot, "src", "UasSort.App", "SelfTest");
Directory.CreateDirectory(selfTestDir);
foreach (var (name, bytes) in SelfTestAssets.All())
{
    var path = Path.Join(selfTestDir, name);
    File.WriteAllBytes(path, bytes);
    Console.WriteLine($"{path} ({bytes.Length} bytes)");
}

internal static class SelfTestAssets
{
    private const ushort TypeByte = 1;
    private const ushort TypeAscii = 2;
    private const ushort TypeLong = 4;
    private const ushort TypeRational = 5;

    /// <summary>
    /// Every asset this tool writes. Part 11 (Task 11.9) extends this list in place with selftest.dng, ledger-v1.jsonl
    /// and selftest-0001..0003.mp4.
    /// </summary>
    public static IEnumerable<(string Name, byte[] Bytes)> All()
    {
        yield return ("stack-exif.jpg", StackExifJpeg());
    }

    /// <summary>
    /// SOI, APP1 "Exif" (little-endian TIFF: IFD0 -> Exif IFD with DateTimeOriginal, GPS IFD with Zachar Bay
    /// 57.5368 N 153.7484 W), EOI. A metadata carrier, not a decodable image; MetadataExtractor stops at EOI.
    /// </summary>
    public static byte[] StackExifJpeg()
    {
        const int Ifd0 = 8, ExifIfd = 38, Dto = 56, GpsIfd = 76, Lat = 142, Lon = 166, TiffLength = 190;
        var tiff = new byte[TiffLength];
        var s = tiff.AsSpan();
        s[0] = (byte)'I';
        s[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(s[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..], Ifd0);

        // IFD0 (2 entries; next-IFD offset at Ifd0 + 26 stays 0)
        BinaryPrimitives.WriteUInt16LittleEndian(s[Ifd0..], 2);
        Entry(s, Ifd0 + 2, 0x8769, TypeLong, 1, ExifIfd);
        Entry(s, Ifd0 + 14, 0x8825, TypeLong, 1, GpsIfd);

        // Exif IFD: DateTimeOriginal
        BinaryPrimitives.WriteUInt16LittleEndian(s[ExifIfd..], 1);
        Entry(s, ExifIfd + 2, 0x9003, TypeAscii, 20, Dto);
        Encoding.ASCII.GetBytes("2026:09:27 14:01:27\0").CopyTo(s[Dto..]);

        // GPS IFD
        BinaryPrimitives.WriteUInt16LittleEndian(s[GpsIfd..], 5);
        Entry(s, GpsIfd + 2, 0x0000, TypeByte, 4, 0x0000_0302); // GPSVersionID 2.3.0.0
        Entry(s, GpsIfd + 14, 0x0001, TypeAscii, 2, 'N');       // "N\0" inline
        Entry(s, GpsIfd + 26, 0x0002, TypeRational, 3, Lat);
        Entry(s, GpsIfd + 38, 0x0003, TypeAscii, 2, 'W');       // "W\0" inline
        Entry(s, GpsIfd + 50, 0x0004, TypeRational, 3, Lon);
        Rationals(s[Lat..], (57u, 1u), (32u, 1u), (1248u, 100u));     // 57° 32' 12.48" = 57.5368
        Rationals(s[Lon..], (153u, 1u), (44u, 1u), (5424u, 100u));    // 153° 44' 54.24" = 153.7484

        var header = "Exif\0\0"u8;
        var app1Length = 2 + header.Length + tiff.Length;       // the APP1 length field counts itself
        var jpg = new byte[2 + 2 + app1Length + 2];
        jpg[0] = 0xFF;
        jpg[1] = 0xD8;                                          // SOI
        jpg[2] = 0xFF;
        jpg[3] = 0xE1;                                          // APP1
        BinaryPrimitives.WriteUInt16BigEndian(jpg.AsSpan(4), (ushort)app1Length);
        header.CopyTo(jpg.AsSpan(6));
        tiff.CopyTo(jpg.AsSpan(6 + header.Length));
        jpg[^2] = 0xFF;
        jpg[^1] = 0xD9;                                         // EOI
        return jpg;
    }

    private static void Entry(Span<byte> s, int at, ushort tag, ushort type, uint count, uint valueOrOffset)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(s[at..], tag);
        BinaryPrimitives.WriteUInt16LittleEndian(s[(at + 2)..], type);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(at + 4)..], count);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(at + 8)..], valueOrOffset);
    }

    private static void Rationals(Span<byte> s, params ReadOnlySpan<(uint Numerator, uint Denominator)> values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(s[(i * 8)..], values[i].Numerator);
            BinaryPrimitives.WriteUInt32LittleEndian(s[(i * 8 + 4)..], values[i].Denominator);
        }
    }
}
