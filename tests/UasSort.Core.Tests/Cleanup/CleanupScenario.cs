// tests/UasSort.Core.Tests/Cleanup/CleanupScenario.cs
namespace UasSort.Core.Tests.Cleanup;

[Flags]
internal enum Comp { None = 0, Lrf = 1, Srt = 2, Trinf = 4, Avc1 = 8, CoverSkip = 16, CoverUnknown = 32 }

/// <summary>Builds CleanupInputs (and, in Task 08.10, the fake PC) from a few declarations. The card, roots, identity and
/// space are FakeLayout's: card E:\, 12.4 GB free, cluster 128 KiB.</summary>
internal sealed partial class CleanupScenario
{
    public const string CardRoot = FakeLayout.CardRoot;
    public const string VideoRoot = FakeLayout.VideoRoot;
    public const string PhotoRoot = FakeLayout.PhotoRoot;
    public const string Ak = "America/Anchorage";
    public const string CouncilDir = @"2026\2026-07\2026-07-25 Council Road";
    public static readonly CardIdentity Identity = FakeLayout.CardId;
    public static readonly DateTime ScanUtc = new(2026, 10, 12, 19, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime LedgerAt = new(2026, 9, 27, 21, 7, 2, DateTimeKind.Utc);

    public CardSpace Space { get; set; } = FakeLayout.CardSpace;
    public bool CopyJpgTwin { get; set; } = true;
    public IPlaceIndex? Places { get; set; }
    public List<CardEntry> Entries { get; } = [];
    public List<MediaUnit> Units { get; } = [];
    public List<Item> Items { get; } = [];
    public Dictionary<ItemId, List<AuditLine>> Audit { get; } = [];
    public HashSet<ItemId> Included { get; } = [];
    public List<CopyOutcome> Outcomes { get; } = [];
    public List<FsEntry> VideoListing { get; } = [];
    public List<FsEntry> PhotoListing { get; } = [];
    public Dictionary<FileKey, LedgerFile> LedgerFiles { get; } = [];
    public Dictionary<FileKey, LedgerDecision> Decisions { get; } = [];
    public List<ScanWarning> Warnings { get; } = [];
    private int _n = 100;

    public static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    private static string Stamp(DateTime utc, string tz) => CleanupFormat.Local(utc, tz).ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);

    private static CardEntry Entry(string rel, long size, EntryClass cls, DateTime mtime, uint attrs = 0x20, string? rule = null)
        => new(rel, size, mtime, mtime, mtime, attrs, cls, rule);

    private static FsEntry Lib(string root, string rel, long size)
        => new(root + "\\" + rel, rel, false, size, LedgerAt, LedgerAt, LedgerAt, 0x20);

    private static LedgerFile Record(string rel, long size, VerifyKind verify, DestRoot root, DateTime captureUtc, string tz, string? set = null)
        => new(CleanupKeys.Key(rel, size), rel, root, (root == DestRoot.Video ? VideoRoot : PhotoRoot) + "\\" + CleanupPaths.Name(rel),
               null, verify, LedgerAt, captureUtc, null, tz, DateOnly.FromDateTime(CleanupFormat.Local(captureUtc, tz)), null, set,
               "DESKTOP-A", "run-1");

    private static Newness DefaultNewness(AuditCategory c) => c switch
    {
        AuditCategory.Unaccounted => new IsNew(NewReason.NoMatch, null),
        AuditCategory.AssumedByRule => new ProbablyImported("videos from this day are already in the library"),
        _ => new Imported(Evidence.LibraryNameSize, null, "in the library"),
    };

    private void AddItem(MediaUnit unit, ItemKind kind, string name, long bytes, DateTime utc, string tz, Newness newness,
                         ItemFlags flags, string? probeError, SessionKey? session, GeoPoint? gps, Mp4Info? mp4)
    {
        var local = CleanupFormat.Local(utc, tz);
        var raw = new RawItem(unit, kind, name, bytes, utc.AddSeconds(90), local, mp4, null, probeError);
        var time = new ItemTime(utc, TimeSource.Mvhd, tz, TzSource.Gps, DateOnly.FromDateTime(local), local);
        var fix = gps is { } p ? new GpsFix(p, null, 0, GpsSource.DjmdModelTable, null) : null;
        Items.Add(new Item(raw, time, fix, session, flags, newness));
    }

    public ItemId AddVideo(DateTime captureUtc, AuditCategory category = AuditCategory.NameSizeMatch, bool listed = true,
        string tz = Ak, Newness? newness = null, ItemFlags flags = ItemFlags.None, long size = 1_000_000_000,
        string? probeError = null, VerifyKind? ledger = null, SessionKey? session = null, TimeSpan? duration = null,
        GeoPoint? gps = null, Comp companions = Comp.None, uint attributes = 0x20)
    {
        var name = $"DJI_{Stamp(captureUtc, tz)}_{++_n:0000}_D.MP4";
        var stem = name[..^4];
        var rel = "DCIM/DJI_001/" + name;
        var mtime = captureUtc.AddSeconds(90);
        var mp4 = Entry(rel, size, EntryClass.Video, mtime, attributes);
        Entries.Add(mp4);
        var comps = new List<CardEntry>();
        if (companions.HasFlag(Comp.Lrf)) comps.Add(Entry($"DCIM/DJI_001/{stem}.LRF", 20_000_000, EntryClass.Skip, mtime, rule: "proxy"));
        if (companions.HasFlag(Comp.Srt)) comps.Add(Entry($"DCIM/DJI_001/{stem}.SRT", 2_000, EntryClass.Skip, mtime, rule: "captions"));
        if (companions.HasFlag(Comp.Trinf)) comps.Add(Entry($"DCIM/DJI_001/.{name}.trinf", 512, EntryClass.Skip, mtime, 0x22, "system/recovery"));
        if (companions.HasFlag(Comp.Avc1)) comps.Add(Entry($"DCIM/DJI_001/.{name}.avc1", 4_096, EntryClass.Skip, mtime, 0x22, "system/recovery"));
        if (companions.HasFlag(Comp.CoverSkip)) comps.Add(Entry($"DCIM/DJI_001/{stem}.JPG", 300_000, EntryClass.Skip, mtime, rule: "video cover"));
        if (companions.HasFlag(Comp.CoverUnknown)) comps.Add(Entry($"DCIM/DJI_001/{stem}.JPG", 300_000, EntryClass.Unknown, mtime));
        Entries.AddRange(comps);
        var unit = new VideoUnit(new ItemId(rel), mp4, companions.HasFlag(Comp.Trinf));
        Units.Add(unit);
        var info = duration is null ? null
            : new Mp4Info(captureUtc, true, new NoFix(NoFixReason.NoDjmdTrack), null, null, null, null, null, duration);
        AddItem(unit, ItemKind.Video, name, size, captureUtc, tz, newness ?? DefaultNewness(category), flags, probeError, session, gps, info);
        var lines = new List<AuditLine> { new(rel, size, category, "") };
        foreach (var c in comps.Where(c => c.Class == EntryClass.Skip))
            lines.Add(new(c.RelPath, c.Size, AuditCategory.SkippedByRule, c.Rule ?? ""));
        Audit[unit.Id] = lines;
        if (listed) VideoListing.Add(Lib(VideoRoot, CouncilDir + "\\" + name, size));
        if (ledger is { } v) LedgerFiles[CleanupKeys.Key(rel, size)] = Record(rel, size, v, DestRoot.Video, captureUtc, tz);
        return unit.Id;
    }

    public ItemId AddPhoto(DateTime captureUtc, AuditCategory category = AuditCategory.NameSizeMatch, bool listed = true,
        bool twin = false, AuditCategory? twinCategory = null, bool? twinListed = null, VerifyKind? ledger = null,
        string tz = Ak, Newness? newness = null, long size = 27_000_000)
    {
        var name = $"DJI_{Stamp(captureUtc, tz)}_{++_n:0000}_D.DNG";
        var rel = "DCIM/DJI_001/" + name;
        var mtime = captureUtc.AddSeconds(1);
        var primary = Entry(rel, size, EntryClass.Photo, mtime);
        Entries.Add(primary);
        CardEntry? jpg = twin ? Entry($"DCIM/DJI_001/{name[..^4]}.JPG", 9_000_000, EntryClass.PhotoTwin, mtime) : null;
        if (jpg is not null) Entries.Add(jpg);
        var unit = new PhotoUnit(new ItemId(rel), primary, jpg);
        Units.Add(unit);
        AddItem(unit, ItemKind.Photo, name, size, captureUtc, tz, newness ?? DefaultNewness(category), ItemFlags.None, null, null, null, null);
        var lines = new List<AuditLine> { new(rel, size, category, "") };
        if (jpg is not null) lines.Add(new(jpg.RelPath, jpg.Size, twinCategory ?? category, ""));
        Audit[unit.Id] = lines;
        if (listed) PhotoListing.Add(Lib(PhotoRoot, name, size));
        if (jpg is not null && (twinListed ?? listed)) PhotoListing.Add(Lib(PhotoRoot, CleanupPaths.Name(jpg.RelPath), jpg.Size));
        if (ledger is { } v)
        {
            LedgerFiles[CleanupKeys.Key(rel, size)] = Record(rel, size, v, DestRoot.Photo, captureUtc, tz);
            if (jpg is not null) LedgerFiles[CleanupKeys.Key(jpg.RelPath, jpg.Size)] = Record(jpg.RelPath, jpg.Size, v, DestRoot.Photo, captureUtc, tz);
        }
        return unit.Id;
    }

    public ItemId AddSet(string setName, DateTime firstUtc, int members = 3, AuditCategory category = AuditCategory.NameSizeMatch,
        bool listed = true, VerifyKind? ledger = null, string tz = Ak)
    {
        var dir = "DCIM/PANORAMA/" + setName;
        var mtime = firstUtc.AddSeconds(1);
        // added in reverse so the planner's ordinal sort is what puts them in order
        var files = Enumerable.Range(1, members).Reverse()
            .Select(i => Entry($"{dir}/PANO_{i:0000}.DNG", 13_751_808, EntryClass.SetMember, mtime)).ToImmutableArray();
        Entries.AddRange(files);
        var unit = new SetUnit(new ItemId(dir), SetKind.Panorama, setName, files);
        Units.Add(unit);
        AddItem(unit, ItemKind.Set, setName, files.Sum(f => f.Size), firstUtc, tz, DefaultNewness(category), ItemFlags.None, null, null, null, null);
        Audit[unit.Id] = [.. files.Select(f => new AuditLine(f.RelPath, f.Size, category, ""))];
        foreach (var f in files)
        {
            if (listed) PhotoListing.Add(Lib(PhotoRoot, setName + "\\" + CleanupPaths.Name(f.RelPath), f.Size));
            if (ledger is { } v) LedgerFiles[CleanupKeys.Key(f.RelPath, f.Size)] = Record(f.RelPath, f.Size, v, DestRoot.Photo, firstUtc, tz, setName);
        }
        return unit.Id;
    }

    public void AddLoose(string rel, EntryClass cls, long size = 1_000, uint attributes = 0x20)
        => Entries.Add(Entry(rel, size, cls, ScanUtc.AddDays(-30), attributes));

    public void AddDir(string rel) => Entries.Add(Entry(rel, 0, EntryClass.Skip, ScanUtc.AddDays(-30), 0x10));

    public MediaUnit Unit(ItemId id) => Units.Single(u => u.Id == id);

    public CardEntry Primary(ItemId id) => Unit(id) switch
    {
        VideoUnit v => v.Mp4,
        PhotoUnit p => p.Primary,
        SetUnit s => s.Members.OrderBy(m => m.RelPath, StringComparer.Ordinal).First(),
    };

    public void SetCategory(string rel, AuditCategory category, string detail = "")
    {
        foreach (var lines in Audit.Values)
            for (var i = 0; i < lines.Count; i++)
                if (CleanupPaths.Rel(lines[i].CardRelPath) == CleanupPaths.Rel(rel)) lines[i] = lines[i] with { Category = category, Detail = detail };
    }

    public void MarkChanged(string rel) => SetCategory(rel, AuditCategory.Unaccounted, CleanupRules.ChangedSinceScanDetail);

    public void Decide(ItemId id, DecisionKind kind, DateTime atUtc)
    {
        CardEntry[] files = Unit(id) switch
        {
            VideoUnit v => [v.Mp4],
            PhotoUnit p => p.JpgTwin is null ? [p.Primary] : [p.Primary, p.JpgTwin],
            SetUnit s => [.. s.Members],
        };
        foreach (var f in files)
        {
            var key = CleanupKeys.Key(f.RelPath, f.Size);
            Decisions[key] = new LedgerDecision($"dec-{Decisions.Count + 1}", key, kind, atUtc, "DESKTOP-A", null, "by you");
            SetCategory(f.RelPath, AuditCategory.ConfirmedByYou);
        }
        var i = Items.FindIndex(x => x.Raw.Unit.Id == id);
        Items[i] = Items[i] with { Newness = new Decided(kind, atUtc, "DESKTOP-A") };
    }

    public void AddEnumerationError(string relDir) => Warnings.Add(new ScanWarning("EnumerationError", "Access is denied.", relDir, true));

    private CopyJob Job(ItemId id)
    {
        var p = Primary(id);
        return new CopyJob(id, p.RelPath, p.Size, p.MtimeUtc, p.CreationUtc, VideoRoot + "\\" + CleanupPaths.Name(p.RelPath),
                           DestRoot.Video, null, false);
    }

    public bool HasOffload { get; private set; }
    public void OffloadVerified(ItemId id) { HasOffload = true; Outcomes.Add(new Verified(Job(id), UInt128.One, VerifyMode.Unbuffered)); }
    public void OffloadFailed(ItemId id) { HasOffload = true; Outcomes.Add(new Failed(Job(id), CopyPhase.Copy, "read error")); }
    public void OffloadNotStarted(ItemId id) { HasOffload = true; Outcomes.Add(new NotStarted(Job(id))); }
    public void OffloadChanged(ItemId id) { HasOffload = true; var j = Job(id); Outcomes.Add(new ChangedOnCard(j, j.Size + 1, j.CardMtimeUtc.AddMinutes(1))); }

    public Settings Settings() => FakeLayout.Settings() with { CopyJpgTwin = CopyJpgTwin };

    public LedgerSnapshot Ledger() => new(LedgerFiles.ToImmutableDictionary(), ImmutableDictionary<string, ImmutableArray<LedgerSet>>.Empty,
        Decisions.ToImmutableDictionary(), ImmutableDictionary<FileKey, DateTime>.Empty, ImmutableDictionary<string, LedgerFolder>.Empty,
        [], [], [], [], new LedgerFolderStatus(LedgerPaths.For(VideoRoot), LedgerFolderState.Ok, true, false, true, true, [], [], []));

    public LibraryListings Listings() => new(
        new RootListing(VideoRoot, DestRoot.Video, false, true, new ListingResult([.. VideoListing], [])),
        new RootListing(PhotoRoot, DestRoot.Photo, false, true, new ListingResult([.. PhotoListing], [])), []);

    public CardInventory Inventory() => new(new CardSource(CardRoot, Identity, false, false), ScanUtc, "9f3c0a6d12e4b7a1",
        [.. Entries], [.. Units], "FC9113", [.. Warnings]);

    public CleanupInputs Inputs()
    {
        var settings = Settings();
        var clock = new ClockModel(ClockMode.Zone, "America/New_York", [], null, StoredClockMode.Zone, "America/New_York");
        var ledger = Ledger();
        var listings = Listings();
        var inv = Inventory();
        var scan = new ScanResult(inv, [.. Items.Select(i => i.Raw)], LibraryIndex.Build(listings, ledger, clock), ledger, clock, [], settings);
        var summary = new ClockSummary(ClockMode.Zone, "America/New_York", 0, "Drone clock: America/New_York", 0, [], []);
        var pb = new PlanBase(scan, [.. Items], [], ImmutableDictionary<ItemId, SetPlacement>.Empty, summary, null);
        var plan = new Plan(1, pb, new Tuning(), [], [], [.. Included], []);
        var audits = Audit.Select(kv => new UnitAudit(kv.Key, kv.Value.Max(l => l.Category), [.. kv.Value])).ToImmutableArray();
        var verdict = new FormatVerdict(VerdictLevel.Safe, Identity, "E: · DJI Air 3S · serial 1A2B-3C4D",
            ImmutableDictionary<AuditCategory, int>.Empty, 0, 0, audits, [], null);
        OffloadResult? offload = HasOffload ? new OffloadResult("run-offload", [.. Outcomes], null, ScanUtc, ScanUtc, []) : null;
        var volume = new VolumeInfo(CardRoot, Identity, "Removable", true, false, false, true, Space.FreeBytes, "Sd", true, false);
        return new CleanupInputs(inv, plan, verdict, offload, Space, listings, ledger, volume, Places, settings);
    }

    public ImmutableArray<CleanupCandidate> Candidates() => CleanupPlanner.Candidates(Inputs());
    public CleanupCandidate Candidate(ItemId id) => Candidates().Single(c => c.Unit == id);
}
