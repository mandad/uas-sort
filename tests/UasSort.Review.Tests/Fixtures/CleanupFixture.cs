// tests/UasSort.Review.Tests/Fixtures/CleanupFixture.cs  (Task 10.21 part; Task 10.22 appends the inputs builder)
namespace UasSort.Review.Tests;

internal static partial class CleanupFixture
{
    public static CleanupCandidate Candidate(string relPath, ItemKind kind, long size, TimeSpan? duration, DateTime localTime, DateTime captureUtc,
                                             NotInLibraryReason? reason, string reasonText, string? place = null, GeoPoint? location = null,
                                             bool ticked = false)
    {
        var entry = new CardEntry(relPath, size, captureUtc, captureUtc, captureUtc, 0x20, kind == ItemKind.Video ? EntryClass.Video : EntryClass.Photo, null);
        return new CleanupCandidate(new ItemId(relPath), [entry], size, captureUtc, DateOnly.FromDateTime(localTime), TestPlans.Anchorage,
            reason is null ? CleanupEligibility.Evidence : CleanupEligibility.NotInLibrary, AuditCategory.Unaccounted, reasonText, duration,
            location, place, kind, localTime, reason, null, null, [], null, ticked, []);
    }

    /// <summary>A CleanupResult over a real ConfirmedCleanupPlan from Part 08's CleanupPlanFixtures (built through Core's internal
    /// constructors), covering the files the outcomes name.</summary>
    public static CleanupResult Result(ImmutableArray<CleanupOutcome> outcomes, CleanupStop? stop, long freeAfter, ImmutableArray<string> stillListed,
                                       string? closingReadError = null)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero));
        string[] files = [.. outcomes.Select(o => o.Unit.CardRelPath).Where(p => p.EndsWith(".MP4", StringComparison.OrdinalIgnoreCase))];
        string[] setFolders = [.. outcomes.Select(o => o.Unit.CardRelPath).Where(p => !p.EndsWith(".MP4", StringComparison.OrdinalIgnoreCase))];
        var confirmed = CleanupPlanFixtures.Confirmed(@"E:\", TestPlans.Card, files, setFolders, clock);
        return new("cleanup01", confirmed, outcomes, stop, new CardSpace(freeAfter, 256_060_514_304, 131_072), stillListed,
                   TestPlans.Utc(2026, 9, 28, 3, 0), TestPlans.Utc(2026, 9, 28, 3, 2), closingReadError);
    }

    /// <summary>One clip = 9,155 clusters of 128 KiB, so allocated size equals file size.</summary>
    public const long S = 1_199_964_160;
    public static readonly VolumeInfo Volume = new(@"E:\", TestPlans.Card, "Removable", true, false, false, true, 12_400_000_000, "Sd", true, false);
    public static readonly CardSpace Space = new(12_400_000_000, 256_060_514_304, 131_072);

    private static readonly LibraryFolderRef Nome = new(TestPlans.VideoRoot + @"\2026\2026-07\2026-07-03 Nome Roads", new(2026, 7, 3), "Nome Roads");
    private static readonly LibraryFolderRef Teller = new(TestPlans.VideoRoot + @"\2026\2026-07\2026-07-28 Teller", new(2026, 7, 28), "Teller");

    public static readonly string[] Old = ["DJI_20260703190000_0001_D.MP4", "DJI_20260703191000_0002_D.MP4", "DJI_20260703192000_0003_D.MP4", "DJI_20260703193000_0004_D.MP4"];
    public const string NewA = "DJI_20260720190000_0050_D.MP4";   // New, ticked for offload
    public const string NewB = "DJI_20260720191000_0051_D.MP4";   // New, unticked
    public const string NewC = "DJI_20260727190000_0060_D.MP4";   // New, unticked, after Jul 26
    public static readonly string[] Late = ["DJI_20260728190000_0070_D.MP4", "DJI_20260728191000_0071_D.MP4"];

    public static CleanupInputs Inputs()
    {
        PlanClip C(string name, DateTime utc, Newness n) => new(name, utc, TestPlans.Anchorage, 64.5, -165.4, Newness: n, Bytes: S);
        var nome = new Imported(Evidence.LibraryNameSize, Nome, "same name and size");
        var teller = new Imported(Evidence.LibraryNameSize, Teller, "same name and size");
        var isNew = new IsNew(NewReason.NoMatch, null);
        List<PlanClip> clips =
        [
            .. Old.Select((n, i) => C(n, TestPlans.Utc(2026, 7, 4, 3, 10 * i), nome)),
            C(NewA, TestPlans.Utc(2026, 7, 21, 3, 0), isNew),
            C(NewB, TestPlans.Utc(2026, 7, 21, 3, 10), isNew),
            C(NewC, TestPlans.Utc(2026, 7, 28, 3, 0), isNew),
            .. Late.Select((n, i) => C(n, TestPlans.Utc(2026, 7, 29, 3, 10 * i), teller)),
        ];
        var t = TestPlans.Utc(2026, 7, 30, 0, 0);
        ImmutableArray<FsEntry> library =
        [
            .. Old.Select(n => new FsEntry(Nome.FullPath + "\\" + n, @"2026\2026-07\2026-07-03 Nome Roads\" + n, false, S, t, t, t, 0x20)),
            .. Late.Select(n => new FsEntry(Teller.FullPath + "\\" + n, @"2026\2026-07\2026-07-28 Teller\" + n, false, S, t, t, t, 0x20)),
        ];
        var listings = TestPlans.Listings(library);
        var b = TestPlans.Base(clips, listings: listings);
        var plan = new ScriptedDeriver().Derive(b, new Tuning(), [new SetIncluded([TestPlans.Id(NewB), TestPlans.Id(NewC)], false)],
                                                new SessionFlags(false), 1, CancellationToken.None);
        var units = b.Items.Select(i =>
        {
            var cat = i.Newness is Imported ? AuditCategory.NameSizeMatch : AuditCategory.Unaccounted;
            return new UnitAudit(i.Raw.Unit.Id, cat, [new AuditLine(i.Raw.Unit.Id.CardRelPath, S, cat, cat == AuditCategory.NameSizeMatch ? "same name and size" : "new: not copied")]);
        }).ToImmutableArray();
        var audit = new FormatVerdict(VerdictLevel.NotSafe, TestPlans.Card, "E: · DJI Air 3S · serial 1A2B-3C4D: Don't format yet",
            ImmutableDictionary<AuditCategory, int>.Empty.Add(AuditCategory.NameSizeMatch, 6).Add(AuditCategory.Unaccounted, 3), 6, 0, units, [], null);
        return new CleanupInputs(b.Scan.Inventory, plan, audit, null, Space, listings, TestPlans.Ledger(), Volume, null, TestPlans.Settings());
    }
}
