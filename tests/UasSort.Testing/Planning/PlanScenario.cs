// tests/UasSort.Testing/Planning/PlanScenario.cs
using System.Collections.Immutable;
using UasSort.Core.Geo;
using UasSort.Core.Library;
using UasSort.Core.Planning;
using UasSort.Core.Time;

namespace UasSort.Testing.Planning;

/// <summary>Builds a ScanResult the way ScanService would, without any disk: card raws, library listings, ledger.</summary>
public sealed partial class PlanScenario
{
    public const string VideoRoot = @"C:\lib\UAS Videos";
    public const string PcZoneId = "America/Anchorage";
    public const string OldMachine = "PC-OLD";
    public static readonly DateTime NowUtc = new(2026, 10, 4, 20, 0, 0, DateTimeKind.Utc);
    public static readonly CardSource Source = new(@"E:\", new CardIdentity(0x1A2B3C4D, "DJI", "exFAT", 256_060_514_304), false, false);
    public static TimeZoneInfo PcZone => TimeZoneInfo.FindSystemTimeZoneById(PcZoneId);

    private const string PhotoPrefix = @"Picture Offload\";

    private readonly List<RawItem> _card = [];
    private readonly List<FsEntry> _video = [];
    private readonly List<FsEntry> _photo = [];
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FsEntry>> _previous = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FileKey, LedgerFile> _files = [];
    private readonly Dictionary<FileKey, LedgerDecision> _decisions = [];
    private readonly Dictionary<FileKey, DateTime> _seen = [];
    private readonly Dictionary<string, List<LedgerSet>> _sets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LedgerParseIssue> _parse = [];

    public string Root { get; init; } = VideoRoot;
    public string PhotoRoot => Root + @"\Picture Offload";
    public StoredClockMode ClockMode { get; set; } = StoredClockMode.Zone;
    public string ClockZone { get; set; } = "America/New_York";
    public bool RootsConfirmed { get; set; } = true;
    public bool VideoRootAvailable { get; set; } = true;
    public bool PhotoRootAvailable { get; set; } = true;
    public LedgerFolderState LedgerState { get; set; } = LedgerFolderState.Ok;

    public PlanScenario Card(params RawItem[] items) { _card.AddRange(items); return this; }

    /// <summary>Lists every file of the given units under folderRel (relative to the video root, backslashes).</summary>
    public PlanScenario Library(string folderRel, params RawItem[] items)
    {
        foreach (var r in items)
            foreach (var f in Files(r))
                LibraryFile($@"{folderRel}\{(r.Unit is SetUnit s ? s.SetName + @"\" : "")}{f.Name}", f.Size, f.MtimeUtc);
        return this;
    }

    /// <summary>Lists one file and every ancestor directory of it (ruling P06-C1), as a real listing would.</summary>
    public PlanScenario LibraryFile(string relPath, long size, DateTime mtimeUtc, uint attributes = 0x20)
    {
        for (var i = relPath.IndexOf('\\', StringComparison.Ordinal); i > 0; i = relPath.IndexOf('\\', i + 1))
            AddDirOnce(relPath[..i]);
        var full = Root + @"\" + relPath;
        _video.Add(new FsEntry(full, relPath, false, size, mtimeUtc, mtimeUtc, mtimeUtc, attributes));
        if (relPath.StartsWith(PhotoPrefix, StringComparison.OrdinalIgnoreCase))
            _photo.Add(new FsEntry(full, relPath[PhotoPrefix.Length..], false, size, mtimeUtc, mtimeUtc, mtimeUtc, attributes));
        return this;
    }

    public PlanScenario LibraryDir(string relPath)
    {
        AddDirOnce(relPath);
        return this;
    }

    private void AddDirOnce(string relPath)
    {
        if (!_dirs.Add(relPath)) return;
        var full = Root + @"\" + relPath;
        var t = NowUtc.AddDays(-30);
        _video.Add(new FsEntry(full, relPath, true, 0, t, t, t, 0x10));
        if (relPath.StartsWith(PhotoPrefix, StringComparison.OrdinalIgnoreCase))
            _photo.Add(new FsEntry(full, relPath[PhotoPrefix.Length..], true, 0, t, t, t, 0x10));
    }

    public PlanScenario PreviousPhotoRootFile(string root, string relPath, long size, DateTime mtimeUtc)
    {
        if (!_previous.TryGetValue(root, out var list)) _previous[root] = list = [];
        list.Add(new FsEntry(root + @"\" + relPath, relPath, false, size, mtimeUtc, mtimeUtc, mtimeUtc, 0x20));
        return this;
    }

    /// <summary>One ledger `file` record per file of the unit; folderRel null = the photo root.</summary>
    public PlanScenario LedgerFile(RawItem r, string? folderRel, GeoPoint? point = null, string? tz = null,
                                   VerifyKind verify = VerifyKind.Unbuffered)
    {
        foreach (var f in Files(r))
        {
            var key = PlanKeys.Key(f.Name, f.Size);
            var dest = folderRel is null ? $@"{PhotoRoot}\{f.Name}" : $@"{Root}\{folderRel}\{f.Name}";
            _files[key] = new LedgerFile(key, f.CardRel, folderRel is null ? DestRoot.Photo : DestRoot.Video, dest, null, verify,
                NowUtc.AddDays(-7), r.Mp4?.MvhdUtc, point, tz, null, null, r.Unit is SetUnit s ? s.SetName : null, OldMachine, "run-1");
        }
        return this;
    }

    public PlanScenario LedgerDecision(RawItem r, DecisionKind kind, int members = int.MaxValue)
    {
        foreach (var f in Files(r).Take(members))
        {
            var key = PlanKeys.Key(f.Name, f.Size);
            _decisions[key] = new LedgerDecision(Guid.NewGuid().ToString(), key, kind, NowUtc.AddDays(-3), OldMachine,
                                                 r.Unit is SetUnit s ? s.SetName : null, "test");
        }
        return this;
    }

    public PlanScenario LedgerSeen(RawItem r, DateTime atUtc, int members = int.MaxValue)
    {
        foreach (var f in Files(r).Take(members)) _seen[PlanKeys.Key(f.Name, f.Size)] = atUtc;
        return this;
    }

    public PlanScenario LedgerSet(string setName, DateTime firstFrameUtc, params (string Member, long Size)[] members)
    {
        if (!_sets.TryGetValue(setName, out var list)) _sets[setName] = list = [];
        list.Add(new LedgerSet(setName, firstFrameUtc, [.. members]));
        return this;
    }

    public PlanScenario LedgerParseIssue(string file, int line, string reason)
    {
        _parse.Add(new LedgerParseIssue(file, line, reason));
        return this;
    }

    public ScanResult Build()
    {
        var raw = _card.ToImmutableArray();
        var clock = DroneClock.Learn(raw, ClockMode, ClockZone, new GeoTimeZoneResolver(), PcZone);
        var settings = new Settings(1, Root, PhotoRoot, [.. _previous.Keys], 50, 1, ClockMode, ClockZone, true,
            new MapSettings("streets", "https://tiles.openfreemap.org/styles/liberty", "https://tiles.openfreemap.org/styles/dark",
                            "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                            ImmutableDictionary<string, string>.Empty),
            new LayoutSettings(420, 0.55), RootsConfirmed);
        var listings = new LibraryListings(
            new RootListing(Root, DestRoot.Video, false, VideoRootAvailable,
                            new ListingResult(VideoRootAvailable ? [.. _video] : [], [])),
            new RootListing(PhotoRoot, DestRoot.Photo, false, PhotoRootAvailable,
                            new ListingResult(PhotoRootAvailable ? [.. _photo] : [], [])),
            [.. _previous.Select(kv => new RootListing(kv.Key, DestRoot.Photo, true, true, new ListingResult([.. kv.Value], [])))]);
        var ledger = BuildLedger();
        var lib = LibraryIndex.Build(listings, ledger, clock);
        var entries = raw.SelectMany(r => Entries(r)).ToImmutableArray();
        var inv = new CardInventory(Source, NowUtc, "0123456789abcdef", entries, [.. raw.Select(r => r.Unit)], "FC9113", []);
        return new ScanResult(inv, raw, lib, ledger, clock, [], settings);
    }

    private LedgerSnapshot BuildLedger()
    {
        var folder = Root + @"\.uas-sort";
        var exists = LedgerState is not (LedgerFolderState.Missing or LedgerFolderState.VideoRootMissing);
        var status = new LedgerFolderStatus(folder, LedgerState, exists, false, LedgerState != LedgerFolderState.NotPinned,
            LedgerState != LedgerFolderState.Unwritable, exists ? [folder + @"\ledger-PC-OLD.jsonl"] : [],
            LedgerState == LedgerFolderState.CloudOnly ? [folder + @"\ledger-B.jsonl"] : [], []);
        return new LedgerSnapshot(
            _files.ToImmutableDictionary(),
            _sets.ToImmutableDictionary(kv => kv.Key, kv => kv.Value.ToImmutableArray(), StringComparer.OrdinalIgnoreCase),
            _decisions.ToImmutableDictionary(),
            _seen.ToImmutableDictionary(),
            ImmutableDictionary.Create<string, LedgerFolder>(StringComparer.OrdinalIgnoreCase),
            [], [], [.. _parse], [], status);
    }

    private static ImmutableArray<CardEntry> Entries(RawItem r) => r.Unit switch
    {
        VideoUnit v => [v.Mp4],
        PhotoUnit p => p.JpgTwin is { } t ? [p.Primary, t] : [p.Primary],
        SetUnit s => s.Members,
    };

    private static IEnumerable<(string Name, long Size, DateTime MtimeUtc, string CardRel)> Files(RawItem r) =>
        Entries(r).Select(e => (PlanKeys.FileName(e.RelPath), e.Size, e.MtimeUtc, e.RelPath));
}
