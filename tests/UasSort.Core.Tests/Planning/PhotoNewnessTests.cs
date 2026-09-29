// tests/UasSort.Core.Tests/Planning/PhotoNewnessTests.cs
using System.Globalization;
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class PhotoNewnessTests
{
    private const string Zrel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private static readonly DateTime Watermark = new(2026, 9, 27, 18, 24, 16, DateTimeKind.Utc);
    private static readonly HashSet<DateOnly> NoNewDays = [];

    private static ItemTime T(DateTime utc, TimeSource src = TimeSource.DroneClockZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("America/Anchorage"));
        return new ItemTime(utc, src, "America/Anchorage", TzSource.Gps, DateOnly.FromDateTime(local), local);
    }

    private static Newness Of(ScanResult s, RawItem r, DateTime? watermark, IReadOnlySet<DateOnly>? newDays = null,
                              SetPlacement? placement = null, TimeSource src = TimeSource.DroneClockZone) =>
        NewnessRules.Photo(r.Unit, T(Clip.Utc(r.DroneStamp!.Value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)), src), s.Library, s.Ledger,
                           placement, newDays ?? NoNewDays, watermark);

    [Fact]
    public void Rule1_LedgerFileBeatsEverything()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var s = new PlanScenario().Card(d).LedgerFile(d, null).LedgerSeen(d, PlanScenario.NowUtc).Build();
        Assert.Equal(Evidence.LedgerVerified, Assert.IsType<Imported>(Of(s, d, Watermark)).By);
    }

    [Fact]
    public void Rule1_DecisionBeatsHeuristic_RevokeRestoresIt()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var decided = new PlanScenario().Card(d).LedgerDecision(d, DecisionKind.AssumedImported).Build();
        Assert.Equal(DecisionKind.AssumedImported, Assert.IsType<Decided>(Of(decided, d, Watermark)).Kind);
        var revoked = new PlanScenario().Card(d).Build();   // a revoked decision is absent from LedgerSnapshot.Decisions (Part 05)
        Assert.IsType<ProbablyImported>(Of(revoked, d, Watermark));
    }

    [Fact]
    public void Rule2_ListedInPhotoRoot_OrPreviousPhotoRoot_IsImported()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var s = new PlanScenario().Card(d).LibraryFile($@"Picture Offload\{d.Name}", d.Bytes, d.CardMtimeUtc).Build();
        Assert.Equal(Evidence.LibraryNameSize, Assert.IsType<Imported>(Of(s, d, Watermark)).By);
        var moved = new PlanScenario().Card(d).PreviousPhotoRootFile(@"D:\Old Offload", d.Name, d.Bytes, d.CardMtimeUtc).Build();
        Assert.IsType<Imported>(Of(moved, d, Watermark));      // photo root moved to D: (Ref §13 Newness)
    }

    [Fact]
    public void Rule2b_SameNameOtherSize_IsConflict()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil, withJpgTwin: true);
        var s = new PlanScenario().Card(d).LibraryFile($@"Picture Offload\{d.Name}", 1_234, d.CardMtimeUtc).Build();
        var c = Assert.IsType<Conflict>(Of(s, d, Watermark));
        Assert.Equal(1_234, c.ExistingSize);
    }

    [Fact] // two-run seen scenario: an unticked photo stays New across runs
    public void Rule3_SeenWithoutFile_IsNewNotCopied()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var seenAt = new DateTime(2026, 10, 4, 18, 0, 0, DateTimeKind.Utc);
        var s = new PlanScenario().Card(d).LedgerSeen(d, seenAt).Build();
        Assert.Equal(new IsNew(NewReason.SeenNotCopied, seenAt), Of(s, d, Watermark));
    }

    [Fact]
    public void Rule4_AfterWatermark_AndNoWatermark()
    {
        var d = Clip.Dng("20260930200000", 200, Sites.Zachar);
        var s = new PlanScenario().Card(d).Build();
        Assert.Equal(new IsNew(NewReason.AfterWatermark, null), Of(s, d, Watermark));
        var early = Clip.Dng("20260101120000", 1, Sites.Zachar);
        Assert.Equal(new IsNew(NewReason.AfterWatermark, null), Of(new PlanScenario().Card(early).Build(), early, null));
    }

    [Fact]
    public void Rule5_DroneClockWithin75MinBefore_IsNear_MtimeIsNot()
    {
        var d = Clip.Dng("20260927131000", 5, Sites.Zachar);        // 17:10Z, 74 min 16 s before the watermark
        var s = new PlanScenario().Card(d).Build();
        Assert.Equal(new IsNew(NewReason.NearWatermark, null), Of(s, d, Watermark));
        Assert.IsType<ProbablyImported>(Of(s, d, Watermark, src: TimeSource.Mtime));
        var far = Clip.Dng("20260927130800", 6, Sites.Zachar);      // 76 min 16 s before
        Assert.IsType<ProbablyImported>(Of(new PlanScenario().Card(far).Build(), far, Watermark));
    }

    [Fact]
    public void Rule6_DayHasNewVideos()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil);
        Assert.Equal(new IsNew(NewReason.DayHasNewVideos, null),
            Of(new PlanScenario().Card(d).Build(), d, Watermark, new HashSet<DateOnly> { new(2026, 8, 15) }));
    }

    [Fact]
    public void Rule7_ProbablyImported_BothReasons()
    {
        var z = Clip.Vid("20260927140127", 123, Sites.Zachar);
        var photoOnly = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var s = new PlanScenario().Card(photoOnly).Library(Zrel, z).Build();
        Assert.Equal("photo-only day before the last imported video (Sep 27)",
            Assert.IsType<ProbablyImported>(Of(s, photoOnly, Watermark)).Why);
        var sameDay = Clip.Dng("20260927100000", 7, Sites.Zachar);  // 14:00Z, Sep 27 AKDT, a day with library videos
        var s2 = new PlanScenario().Card(sameDay).Library(Zrel, z).Build();
        Assert.Equal("videos from this day are already in the library",
            Assert.IsType<ProbablyImported>(Of(s2, sameDay, Watermark)).Why);
    }

    [Fact] // sets: every member needs a record; a partial set falls through
    public void Sets_PartialDecisionOrSeen_FallsThrough_FullIsDecided()
    {
        var members = Enumerable.Range(1, 33)
            .Select(i => ($"PANO_{i:0000}.DNG", 12_000_000L + i, new DateTime(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc)))
            .ToArray();
        var pano = Clip.Set("001_0087", "2026-05-25 09:30:28", Sites.KodiakTown, members);
        var placement = new SetPlacement(pano.Id(), "001_0087", SetResolution.Plain, []);
        var t = T(new DateTime(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc));
        Newness Run(PlanScenario sc)
        {
            var scan = sc.Card(pano).Build();
            return NewnessRules.Photo(pano.Unit, t, scan.Library, scan.Ledger, placement, NoNewDays, null);
        }
        Assert.IsType<IsNew>(Run(new PlanScenario().LedgerDecision(pano, DecisionKind.AssumedImported, members: 5)));
        Assert.IsType<Decided>(Run(new PlanScenario().LedgerDecision(pano, DecisionKind.AssumedImported)));
        Assert.Equal(NewReason.AfterWatermark, Assert.IsType<IsNew>(Run(new PlanScenario().LedgerSeen(pano, PlanScenario.NowUtc, members: 5))).Why);
        Assert.Equal(NewReason.SeenNotCopied, Assert.IsType<IsNew>(Run(new PlanScenario().LedgerSeen(pano, PlanScenario.NowUtc))).Why);
    }

    [Fact]
    public void Sets_ImportedPlacement_IsImported()
    {
        var pano = Clip.Set("001_0087", "2026-05-25 09:30:28", Sites.KodiakTown,
            ("PANO_0001.DNG", 13_751_808, new DateTime(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc)));
        var scan = new PlanScenario().Card(pano).Build();
        var n = NewnessRules.Photo(pano.Unit, T(new DateTime(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc)), scan.Library, scan.Ledger,
            new SetPlacement(pano.Id(), "001_0087", SetResolution.Imported, []), NoNewDays, null);
        Assert.IsType<Imported>(n);
    }
}
