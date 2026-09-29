// Writes the checked-in selftest assets into src/UasSort.App/SelfTest/ (Ref §2.3, §13). No user data.
// usage (from the repo root, on Windows): dotnet run tools/fixtures/make-selftest-assets.cs -- .
#:property TargetFramework=net11.0-windows10.0.26100.0
#:project ../../tests/UasSort.Testing/UasSort.Testing.csproj
using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using UasSort.Core;
using UasSort.Testing;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

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

    private static readonly GeoPoint Anvil = new(64.5627, -165.3696);
    private static readonly GeoPoint Zachar = new(57.5368, -153.7484);

    /// <summary>Every asset this tool writes: Part 01's stack-exif.jpg, then Part 11's synthetic card and test ledger.</summary>
    public static IEnumerable<(string Name, byte[] Bytes)> All()
    {
        yield return ("stack-exif.jpg", StackExifJpeg());

        // Part 11 (Task 11.9): the synthetic card. Drone clock US Eastern (mvhd = stamp + 4 h), as the RC 2 of the fixture.
        var thumb = Jpeg160x90();
        yield return ("selftest-0001.mp4", new SyntheticMp4Builder().WithMvhdUtc(new DateTime(2026, 7, 26, 7, 50, 0, DateTimeKind.Utc))
            .WithDuration(TimeSpan.FromSeconds(222)).WithDjmdGps("dvtm_Air3s.proto", Anvil).WithThumbnail(thumb).Build());    // Jul 25 23:50 AKDT
        yield return ("selftest-0002.mp4", new SyntheticMp4Builder().WithMvhdUtc(new DateTime(2026, 7, 26, 8, 10, 0, DateTimeKind.Utc))
            .WithDuration(TimeSpan.FromSeconds(95)).WithDjmdGps("dvtm_Air3s.proto", Anvil).WithThumbnail(thumb).Build());     // Jul 26 00:10 AKDT
        yield return ("selftest-0003.mp4", new SyntheticMp4Builder().WithMvhdUtc(new DateTime(2026, 9, 27, 18, 1, 27, DateTimeKind.Utc))
            .WithDuration(TimeSpan.FromSeconds(3725)).WithDjmdGps("dvtm_Air3s.proto", Zachar).WithThumbnail(thumb).Build());  // Sep 27 10:01 AKDT
        yield return ("selftest.dng", new SyntheticDngBuilder().WithDateTimeOriginal(new DateTime(2026, 9, 27, 14, 5, 0))
            .WithGps(Zachar).WithModel("FC9113").WithThumbnail(thumb).Build());
        yield return ("ledger-v1.jsonl", Encoding.UTF8.GetBytes(string.Join("\n", LedgerSamples.AllRecordKindsV1()) + "\n"));
    }

    /// <summary>A real, decodable 160×90 JPEG (like a tnal thumbnail): a blue→orange gradient, encoded by WinRT.</summary>
    public static byte[] Jpeg160x90() => Jpeg160x90Async().GetAwaiter().GetResult();

    private static async Task<byte[]> Jpeg160x90Async()
    {
        const int w = 160, h = 90;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                px[i] = (byte)(255 - x * 255 / w); px[i + 1] = (byte)(96 + y); px[i + 2] = (byte)(x * 255 / w); px[i + 3] = 255;   // BGRA
            }
        using var stream = new InMemoryRandomAccessStream();
        var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, w, h, 96, 96, px);
        await enc.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
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
