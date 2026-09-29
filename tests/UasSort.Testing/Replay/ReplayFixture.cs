// tests/UasSort.Testing/Replay/ReplayFixture.cs
using System.Globalization;
using System.Text.Json;

namespace UasSort.Testing.Planning;

public sealed record ReplayEntry(string RelPath, long Size, DateTime MtimeUtc, uint Attributes);
public sealed record ReplayClip(double? Lat, double? Lon, DateTime? MvhdUtc, bool HasMoov, bool MvhdSimulated);

/// <summary>The checked-in golden-replay fixture (Ref §13). Read from an embedded resource; no file IO.</summary>
public sealed class ReplayFixture
{
    public required DateTime SnapshotUtc { get; init; }
    public required string PcZone { get; init; }
    public required IReadOnlyList<ReplayEntry> Entries { get; init; }
    public required IReadOnlyDictionary<string, ReplayClip> Clips { get; init; }

    public IReadOnlyList<ReplayEntry> Mp4s => Entries
        .Where(e => e.RelPath.EndsWith(".MP4", StringComparison.OrdinalIgnoreCase)
                    && !e.RelPath.StartsWith("Picture Offload/", StringComparison.OrdinalIgnoreCase))
        .ToList();

    public static string FolderOf(ReplayEntry e) => e.RelPath[..e.RelPath.LastIndexOf('/')];
    public static string NameOf(ReplayEntry e) => e.RelPath[(e.RelPath.LastIndexOf('/') + 1)..];

    public static ReplayFixture Load()
    {
        using var s = typeof(ReplayFixture).Assembly.GetManifestResourceStream("UasSort.Testing.Replay.library-listing.json")
                      ?? throw new InvalidOperationException("Replay fixture not embedded; copy docs/research/fixtures/library-listing.json to tests/UasSort.Testing/Replay/.");
        using var doc = JsonDocument.Parse(s);
        var root = doc.RootElement;
        static DateTime Utc(string text) =>
            DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        var entries = root.GetProperty("entries").EnumerateArray().Select(e => new ReplayEntry(
            e.GetProperty("relPath").GetString()!, e.GetProperty("size").GetInt64(),
            Utc(e.GetProperty("mtimeUtc").GetString()!), (uint)e.GetProperty("attributes").GetInt32())).ToList();
        var clips = new Dictionary<string, ReplayClip>(StringComparer.Ordinal);
        foreach (var p in root.GetProperty("clips").EnumerateObject())
        {
            var c = p.Value;
            double? Num(string n) => c.GetProperty(n).ValueKind == JsonValueKind.Number ? c.GetProperty(n).GetDouble() : null;
            DateTime? Time(string n) => c.GetProperty(n).ValueKind == JsonValueKind.String ? Utc(c.GetProperty(n).GetString()!) : null;
            clips[p.Name] = new ReplayClip(Num("lat"), Num("lon"), Time("mvhdUtc"), c.GetProperty("hasMoov").GetBoolean(),
                                           c.GetProperty("mvhdSimulated").GetBoolean());
        }
        return new ReplayFixture
        {
            SnapshotUtc = Utc(root.GetProperty("snapshotUtc").GetString()!),
            PcZone = root.GetProperty("pcZone").GetString()!,
            Entries = entries,
            Clips = clips,
        };
    }

    /// <summary>A card copy of a library MP4: DJI clips carry the fixture's mvhd and GPS; Autel clips are timed from mtime.</summary>
    public RawItem CardItem(ReplayEntry e)
    {
        var name = NameOf(e);
        var stamp = UasSort.Core.Planning.PlanKeys.DjiStamp(name);
        if (stamp is null) return Clip.Autel(name, e.Size, e.MtimeUtc);
        var clip = Clips[name];
        var rel = $"DCIM/DJI_001/{name}";
        var entry = new CardEntry(rel, e.Size, e.MtimeUtc, e.MtimeUtc, e.MtimeUtc, 0x20, EntryClass.Video, null);
        GpsProbe first;
        if (clip.Lat is { } lat && clip.Lon is { } lon)
            first = new GpsFix(new GeoPoint(lat, lon), null, 0, clip.HasMoov ? GpsSource.DjmdModelTable : GpsSource.MdatHeadFallback,
                               clip.HasMoov ? "3-3-4-1" : null);
        else first = new NoFix(NoFixReason.AllProbedSamplesZero);
        var info = new Mp4Info(clip.HasMoov ? clip.MvhdUtc : (DateTime?)null, clip.HasMoov, first, null, "dvtm_Air3s.proto", null, null, null,
                               clip.HasMoov ? TimeSpan.FromSeconds(60) : (TimeSpan?)null);
        return new RawItem(new VideoUnit(new ItemId(rel), entry, false), ItemKind.Video, name, e.Size, e.MtimeUtc, stamp, info, null, null);
    }

    /// <summary>A scenario whose card holds the given MP4s and whose library listing holds every entry passing `listed`.</summary>
    public PlanScenario Scenario(IEnumerable<ReplayEntry> card, Func<ReplayEntry, bool> listed)
    {
        var s = new PlanScenario();
        foreach (var e in Entries.Where(listed)) s.LibraryFile(e.RelPath.Replace('/', '\\'), e.Size, e.MtimeUtc, e.Attributes);
        return s.Card([.. card.Select(CardItem)]);
    }
}
