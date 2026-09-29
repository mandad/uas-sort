// tests/UasSort.Core.Tests/Planning/VideoNewnessTests.cs
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class VideoNewnessTests
{
    private const string Zrel = @"2026\2026-09\2026-09-27 Zachar Bay";

    private static Newness Of(ScanResult s, RawItem r) => NewnessRules.Video((VideoUnit)r.Unit, s.Library, s.Ledger);

    // Ported test #36 (test_video_conflict_duplicate_removed → VideoNewness_ConflictNoMatchLedger)
    [Fact]
    public void VideoNewness_ConflictNoMatchLedger()
    {
        var a = Clip.Vid("20260927140127", 123, Sites.Zachar, size: 105_764_094);
        var conflict = Clip.Vid("20260927140127", 123, Sites.Zachar, size: 7_340_032);
        var dup = Clip.Vid("20260927150000", 130, Sites.Zachar, size: 555_555_555);
        var gone = Clip.Vid("20260927151000", 131, Sites.Zachar);
        var s = new PlanScenario()
            .Card(conflict, dup, gone)
            .Library(Zrel, a)
            .LibraryFile($@"{Zrel}\best shot.MP4", 555_555_555, new DateTime(2026, 9, 27, 19, 0, 0, DateTimeKind.Utc))
            .LedgerFile(gone, Zrel, Sites.Zachar, "America/Anchorage")
            .Build();

        var c = Assert.IsType<Conflict>(Of(s, conflict));
        Assert.Equal($@"{PlanScenario.VideoRoot}\{Zrel}\DJI_20260927140127_0123_D.MP4", c.ExistingPath);
        Assert.Equal(105_764_094, c.ExistingSize);
        Assert.Equal(new IsNew(NewReason.NoMatch, null), Of(s, dup));
        var imp = Assert.IsType<Imported>(Of(s, gone));
        Assert.Equal(Evidence.LedgerVerified, imp.By);
        Assert.Null(imp.Folder);                                   // culled from the library: no wall
    }

    [Fact]
    public void Listed_IsImportedWithItsEventFolder()
    {
        var z = Clip.Vid("20260927140127", 123, Sites.Zachar);
        var s = new PlanScenario().Card(z).Library(Zrel, z).Build();
        var imp = Assert.IsType<Imported>(Of(s, z));
        Assert.Equal(Evidence.LibraryNameSize, imp.By);
        Assert.Equal("Zachar Bay", imp.Folder!.Description);
        Assert.Equal(new DateOnly(2026, 9, 27), imp.Folder.NameDate);
    }

    [Fact] // " (n)" removal for matching (Ref §7.1)
    public void CounterSuffixedLibraryCopy_StillMatches()
    {
        var z = Clip.Vid("20260927140127", 123, Sites.Zachar);
        var s = new PlanScenario().Card(z)
            .LibraryFile($@"{Zrel}\DJI_20260927140127_0123_D (2).MP4", z.Bytes, z.CardMtimeUtc).Build();
        Assert.IsType<Imported>(Of(s, z));
    }

    [Fact]
    public void ListedAndInLedger_TakesLedgerEvidenceAndListedFolder()
    {
        var z = Clip.Vid("20260927140127", 123, Sites.Zachar);
        var s = new PlanScenario().Card(z).Library(Zrel, z).LedgerFile(z, Zrel, verify: VerifyKind.NameSize).Build();
        var imp = Assert.IsType<Imported>(Of(s, z));
        Assert.Equal(Evidence.LedgerNameSize, imp.By);
        Assert.Equal("Zachar Bay", imp.Folder!.Description);
    }

    [Fact]
    public void Dismissed_IsDecided()
    {
        var z = Clip.Vid("20260927140127", 123, Sites.Zachar);
        var s = new PlanScenario().Card(z).LedgerDecision(z, DecisionKind.Dismissed).Build();
        var d = Assert.IsType<Decided>(Of(s, z));
        Assert.Equal(DecisionKind.Dismissed, d.Kind);
        Assert.Equal(PlanScenario.OldMachine, d.Machine);
    }

    [Fact]
    public void LedgerOnlyOtherSize_IsConflictWithLedgerDest()
    {
        var old = Clip.Vid("20260927140127", 123, Sites.Zachar, size: 1_000);
        var card = Clip.Vid("20260927140127", 123, Sites.Zachar, size: 2_000);
        var s = new PlanScenario().Card(card).LedgerFile(old, Zrel).Build();
        var c = Assert.IsType<Conflict>(Of(s, card));
        Assert.Equal(1_000, c.ExistingSize);
        Assert.EndsWith(@"Zachar Bay\DJI_20260927140127_0123_D.MP4", c.ExistingPath);
    }
}
