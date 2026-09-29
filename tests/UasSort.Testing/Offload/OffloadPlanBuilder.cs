using System.Collections.Immutable;
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing;

namespace UasSort.Testing.Offload;

/// <summary>Builds a Plan directly from the Ref §3 records, for the offload and audit tests (no probes, no Planner).</summary>
public sealed class OffloadPlanBuilder
{
    public const string CardRoot = @"E:\";
    public const string Tz = "America/Anchorage";
    public static readonly CardIdentity Card = FakeLayout.CardId;          // (0x1A2B3C4D, null, "exFAT", 256_060_514_304)
    public static readonly DateTime T0 = new(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc);
    public static readonly GeoPoint Zachar = new(57.5368, -153.7484);

    private readonly List<Item> _items = [];
    private readonly List<CardEntry> _entries = [];
    private readonly List<MediaUnit> _units = [];
    private readonly HashSet<ItemId> _included = [];
    private readonly List<(GroupTarget Target, GeoPoint? Centroid, ItemId[] Videos)> _groups = [];
    private readonly Dictionary<ItemId, SetPlacement> _sets = [];
    private readonly List<FsEntry> _videoListing = [];
    private readonly List<FsEntry> _photoListing = [];
    private readonly Dictionary<FileKey, LedgerFile> _ledgerFiles = [];
    private readonly Dictionary<FileKey, LedgerDecision> _decisions = [];
    private readonly Dictionary<string, LedgerFolder> _ledgerFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ScanWarning> _warnings = [];
    private readonly List<Issue> _issues = [];
    private LedgerFolderState _ledgerState = LedgerFolderState.Ok;
    private bool _copyTwin = true;
    private bool _rootsConfirmed = true;
    private bool _browsed;

    public string VideoRoot { get; } = FakeLayout.VideoRoot;
    public string PhotoRoot { get; private set; } = FakeLayout.PhotoRoot;
    public IReadOnlyList<CardEntry> Entries => _entries;

    public OffloadPlanBuilder WithPhotoRoot(string root) { PhotoRoot = root; return this; }
    public OffloadPlanBuilder CopyJpgTwin(bool on) { _copyTwin = on; return this; }
    public OffloadPlanBuilder RootsConfirmed(bool on) { _rootsConfirmed = on; return this; }
    /// <summary>The source came from Browse to folder: no identity in the CardSource (CardSourceValidator, Ref §4.1).</summary>
    public OffloadPlanBuilder Browsed() { _browsed = true; return this; }
    public OffloadPlanBuilder LedgerState(LedgerFolderState state) { _ledgerState = state; return this; }
    public OffloadPlanBuilder Warning(ScanWarning w) { _warnings.Add(w); return this; }
    public OffloadPlanBuilder Issue(Issue i) { _issues.Add(i); return this; }

    public string NewFolderPath(string relUnderVideoRoot) => OffloadPaths.Join(VideoRoot, relUnderVideoRoot);

    public CardEntry Entry(string relPath, long size, EntryClass cls, string? rule = null, uint attributes = 0x20, DateTime? mtimeUtc = null)
    {
        var m = mtimeUtc ?? T0;
        var e = new CardEntry(relPath, size, m, m, m, attributes, cls, rule);   // creation = last access = mtime, as FakeFileSystem lists it
        _entries.Add(e);
        return e;
    }

    public ItemId Video(string name, long size, DateTime captureUtc, Newness? newness = null, ItemFlags flags = ItemFlags.None,
                        GeoPoint? gps = null, bool included = true, bool hasTrinf = false, string? probeError = null)
    {
        var e = Entry($"DCIM/DJI_001/{name}", size, EntryClass.Video, mtimeUtc: captureUtc.AddSeconds(90));
        var id = new ItemId(e.RelPath);
        Add(new VideoUnit(id, e, hasTrinf), ItemKind.Video, name, size, captureUtc, newness, flags, gps, included, probeError);
        return id;
    }

    public ItemId Photo(string name, long size, DateTime captureUtc, Newness? newness = null, long? twinSize = null, bool included = true)
    {
        var p = Entry($"DCIM/DJI_001/{name}", size, EntryClass.Photo, mtimeUtc: captureUtc);
        CardEntry? twin = twinSize is long ts
            ? Entry($"DCIM/DJI_001/{Path.GetFileNameWithoutExtension(name)}.JPG", ts, EntryClass.PhotoTwin, mtimeUtc: captureUtc)
            : null;
        var id = new ItemId(p.RelPath);
        Add(new PhotoUnit(id, p, twin), ItemKind.Photo, name, size + (twinSize ?? 0), captureUtc, newness, ItemFlags.None, null, included, null);
        return id;
    }

    public ItemId Set(string setName, (string Member, long Size)[] members, DateTime captureUtc, SetResolution resolution,
                      string? folderName = null, string[]? membersToCopy = null, Newness? newness = null, bool included = true)
    {
        var entries = members
            .Select(m => Entry($"DCIM/PANORAMA/{setName}/{m.Member}", m.Size, EntryClass.SetMember, mtimeUtc: captureUtc))
            .ToImmutableArray();
        var id = new ItemId($"DCIM/PANORAMA/{setName}");
        Add(new SetUnit(id, SetKind.Panorama, setName, entries), ItemKind.Set, setName, members.Sum(m => m.Size), captureUtc,
            newness, ItemFlags.None, null, included, null);
        _sets[id] = new SetPlacement(id, folderName ?? setName, resolution,
                                     [.. membersToCopy ?? members.Select(m => m.Member).ToArray()]);
        return id;
    }

    public CardEntry Unknown(string relPath, long size) => Entry(relPath, size, EntryClass.Unknown);
    public CardEntry Skip(string relPath, long size, string rule) => Entry(relPath, size, EntryClass.Skip, rule);

    public OffloadPlanBuilder Group(GroupTarget target, GeoPoint? centroid, params ItemId[] videos)
    {
        _groups.Add((target, centroid, videos));
        return this;
    }

    public OffloadPlanBuilder LibraryVideo(string relUnderVideoRoot, long size, uint attributes = 0x20)
    {
        _videoListing.Add(new FsEntry(OffloadPaths.Join(VideoRoot, relUnderVideoRoot), relUnderVideoRoot.Replace('/', '\\'),
                                      false, size, T0.AddDays(-2), T0.AddDays(-2), T0.AddDays(-2), attributes));
        return this;
    }

    public OffloadPlanBuilder LibraryPhoto(string relUnderPhotoRoot, long size, uint attributes = 0x20)
    {
        _photoListing.Add(new FsEntry(OffloadPaths.Join(PhotoRoot, relUnderPhotoRoot), relUnderPhotoRoot.Replace('/', '\\'),
                                      false, size, T0.AddDays(-2), T0.AddDays(-2), T0.AddDays(-2), attributes));
        return this;
    }

    public OffloadPlanBuilder LedgerFile(string name, long size, VerifyKind verify, string? set = null)
    {
        var key = OffloadPaths.Key(name, size);
        _ledgerFiles[key] = new LedgerFile(key, $"DCIM/DJI_001/{name}", DestRoot.Video, OffloadPaths.Join(VideoRoot, name), null,
            verify, T0.AddDays(-1), null, null, null, null, null, set, "DESKTOP-A", "run-0");
        return this;
    }

    public OffloadPlanBuilder LedgerDecision(string name, long size, DecisionKind kind, string? set = null)
    {
        var key = OffloadPaths.Key(name, size);
        _decisions[key] = new LedgerDecision($"dec-{name}", key, kind, T0.AddDays(-1), "DESKTOP-A", set, "test");
        return this;
    }

    public OffloadPlanBuilder LedgerFolder(string fullPath)
    {
        var day = DateOnly.FromDateTime(T0);
        _ledgerFolders[fullPath] = new LedgerFolder(fullPath, OffloadPaths.FileName(fullPath), FolderSource.Created, null, day, day, Tz);
        return this;
    }

    private void Add(MediaUnit unit, ItemKind kind, string name, long bytes, DateTime captureUtc, Newness? newness,
                     ItemFlags flags, GeoPoint? gps, bool included, string? probeError)
    {
        var local = captureUtc.AddHours(-8);
        var raw = new RawItem(unit, kind, name, bytes, captureUtc.AddSeconds(90), null, null, null, probeError);
        var time = new ItemTime(captureUtc, kind == ItemKind.Video ? TimeSource.Mvhd : TimeSource.DroneClockZone, Tz, TzSource.Gps,
                                DateOnly.FromDateTime(local), local);
        GpsFix? fix = gps is GeoPoint g ? new GpsFix(g, null, 0, GpsSource.Exif, null) : null;
        _items.Add(new Item(raw, time, fix, null, flags, newness ?? new IsNew(NewReason.NoMatch, null)));
        _units.Add(unit);
        if (included) _included.Add(unit.Id);
    }

    public Plan Build()
    {
        var settings = new Settings(1, VideoRoot, PhotoRoot, [], 50, 1, StoredClockMode.Zone, "America/New_York", _copyTwin,
            new MapSettings("streets", "https://tiles.example/liberty", "https://tiles.example/dark", "https://imagery.example/{z}/{y}/{x}",
                            ImmutableDictionary<string, string>.Empty),
            new LayoutSettings(380, 0.45), _rootsConfirmed);
        var clock = new ClockModel(ClockMode.Zone, "America/New_York", [], TimeSpan.FromHours(-4), StoredClockMode.Zone, "America/New_York");
        var status = new LedgerFolderStatus(LedgerPaths.For(VideoRoot), _ledgerState, _ledgerState != LedgerFolderState.Missing,
            false, true, _ledgerState != LedgerFolderState.Unwritable, [], [], []);
        var ledger = new LedgerSnapshot(_ledgerFiles.ToImmutableDictionary(), ImmutableDictionary<string, ImmutableArray<LedgerSet>>.Empty,
            _decisions.ToImmutableDictionary(), ImmutableDictionary<FileKey, DateTime>.Empty,
            _ledgerFolders.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase), [], [], [], [], status);
        var listings = new LibraryListings(
            new RootListing(VideoRoot, DestRoot.Video, false, true, new ListingResult([.. _videoListing], [])),
            new RootListing(PhotoRoot, DestRoot.Photo, false, true, new ListingResult([.. _photoListing], [])),
            []);
        var library = LibraryIndex.Build(listings, ledger, clock);
        var inventory = new CardInventory((_browsed ? new CardSource(CardRoot, null, true, false) : new CardSource(CardRoot, Card, false, false)), T0, "0123456789abcdef",
                                          [.. _entries], [.. _units], "FC9113", [.. _warnings]);
        var scan = new ScanResult(inventory, [.. _items.Select(i => i.Raw)], library, ledger, clock, [.. _warnings], settings);
        var summary = new ClockSummary(ClockMode.Zone, "America/New_York", 0, "Drone clock: America/New_York", 0, [], []);
        var planBase = new PlanBase(scan, [.. _items], [], _sets.ToImmutableDictionary(), summary, null);

        var byId = _items.ToDictionary(i => i.Raw.Unit.Id);
        var groups = _groups.Select((g, n) =>
        {
            var videos = g.Videos.OrderBy(v => byId[v].Time.CaptureUtc).ThenBy(v => v.CardRelPath, StringComparer.Ordinal).ToImmutableArray();
            var start = videos.Min(v => byId[v].Time.LocalDate);
            var end = videos.Max(v => byId[v].Time.LocalDate);
            LibraryFolderRef? wall = g.Target switch { Append a => a.Folder, AlreadyImported ai => ai.Folder, _ => null };
            var description = g.Target switch
            {
                NewFolder nf => OffloadPaths.FileName(nf.RelPath) is var leaf && leaf.Length > 11 ? leaf[11..] : "",
                _ => wall?.Description ?? "",
            };
            return new VideoGroup(new GroupId(videos[0]), videos, g.Centroid, new Distance(0), start, end, wall, g.Target, null,
                description, DescSource.User, g.Target is NewFolder, null, [], [], [], false, n);
        }).ToImmutableArray();

        return new Plan(1, planBase, new Tuning(), groups, [], [.. _included], [.. _issues]);
    }
}
