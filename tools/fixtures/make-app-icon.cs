// Writes the checked-in app icon src/UasSort.App/Assets/AppIcon.ico (ruling P11-C2): a multi-size ICO (16, 32, 48, 256)
// whose frames are PNGs encoded by Windows.Graphics.Imaging.BitmapEncoder. Drawn procedurally; no user data.
// usage (from the repo root): dotnet run tools/fixtures/make-app-icon.cs -- .
#:property TargetFramework=net11.0-windows10.0.26100.0
using System.Buffers.Binary;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

var repoRoot = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var assetsDir = Path.Join(repoRoot, "src", "UasSort.App", "Assets");
Directory.CreateDirectory(assetsDir);

int[] sizes = [16, 32, 48, 256];
var frames = new List<byte[]>();
foreach (var size in sizes)
    frames.Add(await AppIcon.EncodePngAsync(size, AppIcon.Render(size)));

var ico = AppIcon.Pack(sizes, frames);
var path = Path.Join(assetsDir, "AppIcon.ico");
File.WriteAllBytes(path, ico);
Console.WriteLine($"{path} ({ico.Length} bytes; frames {string.Join(", ", sizes)})");

internal static class AppIcon
{
    private const int Supersample = 4;

    // Fluent-style blue tile with a white quadcopter seen from above.
    private static readonly (byte B, byte G, byte R) Background = (0xBD, 0x6C, 0x0F);   // #0F6CBD
    private static readonly (byte B, byte G, byte R) Foreground = (0xFF, 0xFF, 0xFF);

    /// <summary>BGRA8, straight alpha, top-down rows; each pixel averages Supersample x Supersample samples.</summary>
    public static byte[] Render(int size)
    {
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                double bgCover = 0, fgCover = 0;
                for (var sy = 0; sy < Supersample; sy++)
                {
                    for (var sx = 0; sx < Supersample; sx++)
                    {
                        var u = (x + (sx + 0.5) / Supersample) / size;
                        var v = (y + (sy + 0.5) / Supersample) / size;
                        if (!InTile(u, v)) continue;
                        if (InDrone(u, v)) fgCover++;
                        else bgCover++;
                    }
                }

                const double samples = Supersample * Supersample;
                var alpha = (bgCover + fgCover) / samples;
                var i = (y * size + x) * 4;
                if (alpha <= 0) continue;
                var f = fgCover / (bgCover + fgCover);
                pixels[i] = Mix(Background.B, Foreground.B, f);
                pixels[i + 1] = Mix(Background.G, Foreground.G, f);
                pixels[i + 2] = Mix(Background.R, Foreground.R, f);
                pixels[i + 3] = (byte)Math.Round(alpha * 255);
            }
        }

        return pixels;
    }

    public static async Task<byte[]> EncodePngAsync(int size, byte[] bgra)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)size, (uint)size, 96, 96, bgra);
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    /// <summary>ICONDIR + one ICONDIRENTRY per frame + the PNG frames (Vista+ PNG-in-ICO).</summary>
    public static byte[] Pack(IReadOnlyList<int> sizes, IReadOnlyList<byte[]> pngs)
    {
        const int HeaderSize = 6, EntrySize = 16;
        var offset = HeaderSize + EntrySize * sizes.Count;
        var ico = new byte[offset + pngs.Sum(p => p.Length)];
        var s = ico.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(s[2..], 1);                 // type: icon
        BinaryPrimitives.WriteUInt16LittleEndian(s[4..], (ushort)sizes.Count);
        for (var i = 0; i < sizes.Count; i++)
        {
            var e = s[(HeaderSize + i * EntrySize)..];
            e[0] = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);                    // 0 means 256
            e[1] = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(e[4..], 1);              // planes
            BinaryPrimitives.WriteUInt16LittleEndian(e[6..], 32);             // bits per pixel
            BinaryPrimitives.WriteUInt32LittleEndian(e[8..], (uint)pngs[i].Length);
            BinaryPrimitives.WriteUInt32LittleEndian(e[12..], (uint)offset);
            pngs[i].CopyTo(s[offset..]);
            offset += pngs[i].Length;
        }

        return ico;
    }

    private static byte Mix(byte a, byte b, double f) => (byte)Math.Round(a + (b - a) * f);

    /// <summary>A rounded square filling the icon, 3 % margin, corner radius 20 %.</summary>
    private static bool InTile(double u, double v)
    {
        const double Margin = 0.03, Radius = 0.2;
        var lo = Margin + Radius;
        var hi = 1 - Margin - Radius;
        var dx = Math.Max(0, Math.Max(lo - u, u - hi));
        var dy = Math.Max(0, Math.Max(lo - v, v - hi));
        return u >= Margin && u <= 1 - Margin && v >= Margin && v <= 1 - Margin && dx * dx + dy * dy <= Radius * Radius;
    }

    /// <summary>Body, four diagonal arms, four rotor rings with hubs.</summary>
    private static bool InDrone(double u, double v)
    {
        const double Rotor = 0.27, RingOuter = 0.155, RingInner = 0.105, Hub = 0.04, Body = 0.1, Arm = 0.035;
        if (Distance(u, v, 0.5, 0.5) <= Body) return true;
        foreach (var (cx, cy) in new[] { (Rotor, Rotor), (1 - Rotor, Rotor), (Rotor, 1 - Rotor), (1 - Rotor, 1 - Rotor) })
        {
            var d = Distance(u, v, cx, cy);
            if (d <= Hub || (d >= RingInner && d <= RingOuter)) return true;
            if (d > RingInner && SegmentDistance(u, v, 0.5, 0.5, cx, cy) <= Arm) return true;     // arm stops at the ring
        }

        return false;
    }

    private static double Distance(double x0, double y0, double x1, double y1) => Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));

    private static double SegmentDistance(double px, double py, double ax, double ay, double bx, double by)
    {
        var (dx, dy) = (bx - ax, by - ay);
        var t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0, 1);
        return Distance(px, py, ax + t * dx, ay + t * dy);
    }
}
