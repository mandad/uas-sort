using System.Globalization;

namespace UasSort.Testing;

/// <summary>
/// Materialises a DJI card folder tree on real disk for the Windows tests and the CLI run (Ref §2.3), only under
/// %TEMP%\uas-sort-test-*. Videos are SyntheticMp4Builder clips DJI_{stamp}_{n}_D.MP4 whose mvhd is the drone stamp
/// + 4 h as UTC (an Eastern-set drone clock in summer, the Ref §13 fixture convention) and whose mtime is mvhd + 90 s.
/// </summary>
public sealed class FakeCardWriter
{
    public static readonly DateTime DefaultMtimeUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly List<(string RelPath, byte[] Bytes, DateTime MtimeUtc)> _files = [];

    public FakeCardWriter(string cardRoot)
    {
        string full = Path.GetFullPath(cardRoot);
        string allowed = Path.Join(Path.GetFullPath(Path.GetTempPath()), "uas-sort-test-");
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"FakeCardWriter writes only under %TEMP%\\uas-sort-test-*; refused '{full}'", nameof(cardRoot));
        Root = full;
    }

    public string Root { get; }

    public IReadOnlyList<string> RelPaths => _files.Select(f => f.RelPath).ToList();

    public FakeCardWriter AddDjiVideo(string mediaFolder, DateTime droneStamp, int number, GeoPoint? gps)
    {
        var mvhdUtc = DateTime.SpecifyKind(droneStamp.AddHours(4), DateTimeKind.Utc);
        var builder = gps is { } at
            ? new SyntheticMp4Builder().WithMvhdUtc(mvhdUtc).WithDjmdGps("dvtm_Air3s.proto", at)
            : new SyntheticMp4Builder { WithDjmdTrack = false }.WithMvhdUtc(mvhdUtc);
        string name = string.Create(CultureInfo.InvariantCulture, $"DJI_{droneStamp:yyyyMMddHHmmss}_{number:0000}_D.MP4");
        _files.Add(($"DCIM/{mediaFolder}/{name}", builder.Build(), mvhdUtc.AddSeconds(90)));
        return this;
    }

    public FakeCardWriter AddFile(string cardRelPath, byte[] content, DateTime? mtimeUtc = null)
    {
        _files.Add((cardRelPath, content, mtimeUtc ?? DefaultMtimeUtc));
        return this;
    }

    public void Write()
    {
#pragma warning disable RS0030 // test fixture: materialises a fake card under %TEMP%\uas-sort-test-* only
        Directory.CreateDirectory(Root);
        foreach (var (rel, bytes, mtime) in _files)
        {
            string path = Path.Join(Root, rel.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, mtime);
        }
#pragma warning restore RS0030
    }
}
