// tests/UasSort.Review.Tests/Fixtures/TestPlans.cs
namespace UasSort.Review.Tests;

/// <summary>One card video in a fixture (named PlanClip so it never clashes with UasSort.Testing.Planning.Clip). Times are true UTC;
/// the local date comes from TzId.</summary>
internal sealed record PlanClip(string Name, DateTime CaptureUtc, string TzId, double? Lat, double? Lon,
                           Newness? Newness = null, ItemFlags Flags = ItemFlags.None, long Bytes = 1_200_000_000,
                           DateTime? DroneStamp = null, TimeSource Source = TimeSource.Mvhd, TimeSpan? Duration = null);

internal static class TestPlans
{
    public const string VideoRoot = @"C:\Lib\UAS Videos";
    public const string PhotoRoot = @"C:\Lib\UAS Videos\Picture Offload";
    public const string Anchorage = "America/Anchorage";
    public static readonly CardIdentity Card = new(0x1A2B3C4D, "DJI_CARD", "exFAT", 256_060_514_304);
    public static readonly CardSource Source = new(@"E:\", Card, IsBrowsedFolder: false, IsWriteProtected: false);

    public static ItemId Id(string name) => new($"DCIM/DJI_001/{name}");
    public static DateTime Utc(int y, int mo, int d, int h, int mi, int s = 0) => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    /// <summary>Council Road (2 clips, Jul 25 AKDT) and Anvil Mountain (2 clips, Jul 26 AKDT), 33.9 mi apart; all New.</summary>
    public static IReadOnlyList<PlanClip> CouncilAnvil() =>
    [
        new("DJI_20260725192655_0117_D.MP4", Utc(2026, 7, 26, 3, 26, 55), Anchorage, 64.6935, -164.2657),
        new("DJI_20260725194000_0118_D.MP4", Utc(2026, 7, 26, 3, 40, 0), Anchorage, 64.6940, -164.2650),
        new("DJI_20260726195645_0001_D.MP4", Utc(2026, 7, 27, 3, 56, 45), Anchorage, 64.5627, -165.3696),
        new("DJI_20260726202000_0002_D.MP4", Utc(2026, 7, 27, 4, 20, 0), Anchorage, 64.5630, -165.3700),
    ];

    /// <summary>Zachar Bay: 3 clips on Sep 27 AKDT shot with an Eastern-set drone clock (ClockMismatch on each).</summary>
    public static IReadOnlyList<PlanClip> Zachar() =>
    [
        new("DJI_20260927140127_0123_D.MP4", Utc(2026, 9, 27, 18, 1, 27), Anchorage, 57.5415, -153.7409,
            Flags: ItemFlags.ClockMismatch, DroneStamp: new DateTime(2026, 9, 27, 14, 1, 27)),
        new("DJI_20260927140627_0128_D.MP4", Utc(2026, 9, 27, 18, 6, 27), Anchorage, 57.550442, -153.738973,
            Flags: ItemFlags.ClockMismatch, DroneStamp: new DateTime(2026, 9, 27, 14, 6, 27)),
        new("DJI_20260927142416_0148_D.MP4", Utc(2026, 9, 27, 18, 24, 16), Anchorage, 57.5420, -153.7400,
            Flags: ItemFlags.ClockMismatch, DroneStamp: new DateTime(2026, 9, 27, 14, 24, 16)),
    ];

    public static Settings Settings(bool rootsConfirmed = true) =>
        new(1, VideoRoot, PhotoRoot, [], 50, 1, StoredClockMode.Zone, "America/New_York", true,
            new MapSettings("streets", "https://tiles.openfreemap.org/styles/liberty", "https://tiles.openfreemap.org/styles/dark",
                            "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                            ImmutableDictionary<string, string>.Empty),
            new LayoutSettings(420, 0.6), rootsConfirmed);

    public static LedgerSnapshot Ledger(LedgerFolderState state = LedgerFolderState.Ok,
                                        ImmutableArray<LedgerParseIssue> parseIssues = default,
                                        ImmutableArray<string> sourceFiles = default) =>
        new(ImmutableDictionary<FileKey, LedgerFile>.Empty,
            ImmutableDictionary<string, ImmutableArray<LedgerSet>>.Empty,
            ImmutableDictionary<FileKey, LedgerDecision>.Empty,
            ImmutableDictionary<FileKey, DateTime>.Empty,
            ImmutableDictionary<string, LedgerFolder>.Empty,
            [], [], parseIssues.IsDefault ? [] : parseIssues,
            sourceFiles.IsDefault ? [@"C:\Lib\UAS Videos\.uas-sort\ledger-PC1.jsonl"] : sourceFiles,
            new LedgerFolderStatus(VideoRoot + @"\.uas-sort", state, state != LedgerFolderState.Missing, true, true,
                                   state != LedgerFolderState.Unwritable, [], [], []));

    public static ClockModel ZoneClock() => new(ClockMode.Zone, "America/New_York", [], TimeSpan.FromHours(-4), StoredClockMode.Zone, "America/New_York");

    public const string Headline = "Drone clock: US Eastern (America/New_York), learned from 13 videos. Folder dates use local time at each site.";

    /// <summary>A ClockSummary as Planner.Prepare builds it through DroneClock.Summarize; the headline text is Core's (Part 04).</summary>
    public static ClockSummary Summary(ClockMode mode = ClockMode.Zone, int mismatchItems = 0, ImmutableArray<string> siteZones = default,
                                       ImmutableArray<ClockChange> changes = default, string headline = Headline) =>
        new(mode, mode == ClockMode.Zone ? "America/New_York" : null, 13, headline, mismatchItems,
            siteZones.IsDefault ? [] : siteZones, changes.IsDefault ? [] : changes);

    public static ListingResult EmptyListing() => new([], []);

    public static LibraryListings Listings(ImmutableArray<FsEntry> videoEntries = default) =>
        new(new RootListing(VideoRoot, DestRoot.Video, false, true, new ListingResult(videoEntries.IsDefault ? [] : videoEntries, [])),
            new RootListing(PhotoRoot, DestRoot.Photo, false, true, EmptyListing()), []);

    public static Item ItemOf(PlanClip c)
    {
        var id = Id(c.Name);
        var entry = new CardEntry(id.CardRelPath, c.Bytes, c.CaptureUtc, c.CaptureUtc, c.CaptureUtc, 0x20, EntryClass.Video, null);
        var mp4 = new Mp4Info(c.CaptureUtc, !c.Flags.HasFlag(ItemFlags.Truncated), new NoFix(NoFixReason.NotDji), null, "dvtm_Air3s.proto",
                              null, "1581F", null, c.Duration ?? TimeSpan.FromSeconds(222));
        var raw = new RawItem(new VideoUnit(id, entry, false), ItemKind.Video, c.Name, c.Bytes, c.CaptureUtc, c.DroneStamp, mp4, null, null);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(c.TzId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(c.CaptureUtc, tz);
        var time = new ItemTime(c.CaptureUtc, c.Source, c.TzId, TzSource.Gps, DateOnly.FromDateTime(local), local);
        GpsFix? gps = c.Lat is { } lat && c.Lon is { } lon ? new GpsFix(new GeoPoint(lat, lon), null, 0, GpsSource.DjmdModelTable, null) : null;
        var flags = c.Flags | (gps is null ? ItemFlags.NoGps : ItemFlags.None);
        return new Item(raw, time, gps, new SessionKey("1581F", c.CaptureUtc), flags, c.Newness ?? new IsNew(NewReason.NoMatch, null));
    }

    public static PlanBase Base(IReadOnlyList<PlanClip> clips, ClockModel? clock = null, ClockSummary? summary = null,
                                LedgerSnapshot? ledger = null, IReadOnlyList<Item>? extraItems = null,
                                ImmutableArray<PhotoDay> photoDays = default, LibraryListings? listings = null,
                                CardSource? source = null, DateTime? watermarkUtc = null, ImmutableArray<CardEntry> extraEntries = default,
                                ImmutableArray<ScanWarning> warnings = default, bool rootsConfirmed = true)
    {
        var items = clips.Select(ItemOf).Concat(extraItems ?? []).ToImmutableArray();
        var clockModel = clock ?? ZoneClock();
        var led = ledger ?? Ledger();
        var entries = items.SelectMany(EntriesOf).Concat(extraEntries.IsDefault ? [] : extraEntries).ToImmutableArray();
        var src = source ?? Source;
        var inventory = new CardInventory(src, Utc(2026, 9, 28, 2, 0), "9f3c0a6d12e4b7a1", entries,
                                          [.. items.Select(i => i.Raw.Unit)], "FC9113", warnings.IsDefault ? [] : warnings);
        var library = LibraryIndex.Build(listings ?? Listings(), led, clockModel);
        var scan = new ScanResult(inventory, [.. items.Select(i => i.Raw)], library, led, clockModel,
                                  warnings.IsDefault ? [] : warnings, Settings(rootsConfirmed));
        return new PlanBase(scan, items, photoDays.IsDefault ? [] : photoDays,
                            ImmutableDictionary<ItemId, SetPlacement>.Empty, summary ?? Summary(), watermarkUtc);
    }

    private static IEnumerable<CardEntry> EntriesOf(Item i) => i.Raw.Unit switch
    {
        VideoUnit v => [v.Mp4],
        PhotoUnit p => p.JpgTwin is { } t ? [p.Primary, t] : [p.Primary],
        SetUnit s => s.Members,
    };
}
