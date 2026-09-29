using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Core.Tests.Offload.CommitTailRecordsTests;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class CardAuditTests
{
    private const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private const string Name = "E: · DJI Air 3S · serial 1A2B-3C4D";

    internal static ListingResult Unchanged(CardInventory inventory)
        => new([.. inventory.Entries.Select(e => new FsEntry(PathRules.Join(CardRoot, e.RelPath), e.RelPath.Replace('/', '\\'), false,
                                                               e.Size, e.MtimeUtc, e.CreationUtc, e.LastAccessUtc, e.RawAttributes))], []);

    private static CopyOutcome Ok(CopyJob j) => new Verified(j, UInt128.One, VerifyMode.Unbuffered);

    private static FormatVerdict Audit(Plan plan, OffloadResult? result, ListingResult? relisted = null, CardIdentity? now = null, bool gone = false)
        => CardAudit.Audit(plan.Base.Scan.Inventory, relisted ?? Unchanged(plan.Base.Scan.Inventory), gone ? null : now ?? OffloadPlanBuilder.Card,
                           plan, result, plan.Base.Scan.Ledger, OffloadPlanBuilder.Card);

    private static (Plan Plan, OffloadResult Result) TwoVideosAndAListedPhoto(Func<CopyJob, CopyOutcome>? outcome = null)
    {
        var b = new OffloadPlanBuilder();
        var v1 = b.Video("DJI_20260927140000_0001_D.MP4", 100, T0);
        var v2 = b.Video("DJI_20260927140100_0002_D.MP4", 200, T0.AddMinutes(1));
        b.Group(new NewFolder(ZRel), Zachar, v1, v2);
        b.LibraryPhoto("DJI_20260927140200_0003_D.DNG", 50);
        b.Photo("DJI_20260927140200_0003_D.DNG", 50, T0.AddMinutes(2), new Imported(Evidence.LibraryNameSize, null, "listed"), included: false);
        var plan = b.Build();
        return (plan, Result(OffloadCompiler.Compile(plan, "run-1"), outcome ?? Ok));
    }

    [Fact]
    public void Safe_NamesTheCard_AndSplitsTheEvidence()
    {
        var (plan, result) = TwoVideosAndAListedPhoto();

        var v = Audit(plan, result);

        Assert.Equal(VerdictLevel.Safe, v.Level);
        Assert.Equal($"{Name}: Safe to format: 2 verified, 1 matched by name+size only", v.Headline);
        Assert.Equal((2, 1, 1, 0), (v.Counts[AuditCategory.VerifiedThisRun], v.Counts[AuditCategory.NameSizeMatch], v.NameSizeOnly, v.CachedVerifies));
        Assert.Equal(OffloadPlanBuilder.Card, v.Card);
        Assert.Empty(v.CardChanges);
        Assert.Null(v.SafeRemovalNote);
    }

    [Fact]
    public void BufferedVerifies_AreCountedInTheHeadline()
    {
        var (plan, result) = TwoVideosAndAListedPhoto(j => j.CardRelPath.EndsWith("0001_D.MP4", StringComparison.Ordinal)
                                                              ? new Verified(j, UInt128.One, VerifyMode.Cached) : Ok(j));

        var v = Audit(plan, result);

        Assert.Equal($"{Name}: Safe to format: 2 verified (1 with buffered read-back), 1 matched by name+size only", v.Headline);
        Assert.Equal(1, v.CachedVerifies);
    }

    [Fact]
    public void ProbablyImportedPhotos_GiveSafeWithAssumptions()
    {
        var b = new OffloadPlanBuilder();
        var v1 = b.Video("DJI_20260927140000_0001_D.MP4", 100, T0);
        b.Group(new NewFolder(ZRel), Zachar, v1);
        b.Photo("DJI_20260927100000_0001_D.DNG", 50, T0.AddHours(-4), new ProbablyImported("videos from this day are already in the library"), included: false);
        b.Photo("DJI_20260927100100_0002_D.DNG", 50, T0.AddHours(-4), new ProbablyImported("videos from this day are already in the library"), included: false);
        var plan = b.Build();

        var v = Audit(plan, Result(OffloadCompiler.Compile(plan, "run-1"), Ok));

        Assert.Equal(VerdictLevel.SafeWithAssumptions, v.Level);
        Assert.Equal($"{Name}: Safe, with assumptions: 2 photos were assumed already imported; 1 verified", v.Headline);
    }

    [Fact]
    public void ATruncatedClipCopiedAsIs_GivesSafeWithAssumptions()
    {
        var b = new OffloadPlanBuilder();
        var v1 = b.Video("DJI_20260727002013_0014_D.MP4", 100, T0, flags: ItemFlags.Truncated, hasTrinf: true);
        b.Group(new NewFolder(ZRel), Zachar, v1);
        var plan = b.Build();

        var v = Audit(plan, Result(OffloadCompiler.Compile(plan, "run-1"), Ok));

        Assert.Equal($"{Name}: Safe, with assumptions: 1 unfinished recording copied as-is; the drone may still be able to repair it; 1 verified", v.Headline);
    }

    [Fact]
    public void AcceptedLedgerParseIssues_CapTheVerdict()
    {
        var b = new OffloadPlanBuilder();
        var v1 = b.Video("DJI_20260927140000_0001_D.MP4", 100, T0);
        b.Group(new NewFolder(ZRel), Zachar, v1);
        b.Issue(new Issue(IssueSeverity.Warning, IssueCode.LedgerParseIssue, "Ledger line ledger-B.jsonl:12 can't be read: bad JSON", null, [], true));
        var plan = b.Build();

        var v = Audit(plan, Result(OffloadCompiler.Compile(plan, "run-1"), Ok));

        Assert.Equal($"{Name}: Safe, with assumptions: unreadable history lines were accepted; 1 verified", v.Headline);
    }

    [Fact]
    public void FailuresAndUnfinishedRecordingsLeftOnTheCard_AreNotSafe()
    {
        var b = new OffloadPlanBuilder();
        var ok = b.Video("DJI_20260927140000_0001_D.MP4", 100, T0);
        var bad = b.Video("DJI_20260927140100_0002_D.MP4", 100, T0.AddMinutes(1));
        var u1 = b.Video("DJI_20260927140200_0003_D.MP4", 7, T0.AddMinutes(2), flags: ItemFlags.Truncated, included: false, hasTrinf: true);
        var u2 = b.Video("DJI_20260927140300_0004_D.MP4", 7, T0.AddMinutes(3), flags: ItemFlags.Truncated, included: false, hasTrinf: true);
        b.Group(new NewFolder(ZRel), Zachar, ok, bad, u1, u2);
        var plan = b.Build();
        var result = Result(OffloadCompiler.Compile(plan, "run-1"), j => j.Item == bad ? new Failed(j, CopyPhase.Verify, "mismatch twice") : Ok(j));

        var v = Audit(plan, result);

        Assert.Equal(VerdictLevel.NotSafe, v.Level);
        Assert.Equal($"{Name}: Don't format yet: 1 file failed, 2 unfinished recordings not copied", v.Headline);
    }

    [Fact]
    public void ADifferentOrMissingCard_IsNotSafe()
    {
        var (plan, result) = TwoVideosAndAListedPhoto();

        var swapped = Audit(plan, result, now: OffloadPlanBuilder.Card with { VolumeSerial = 0xDEADBEEF });
        var gone = Audit(plan, result, gone: true);

        Assert.Equal(VerdictLevel.NotSafe, swapped.Level);
        Assert.Equal($"{Name}: Don't format yet: The card in E: is not the one that was offloaded", swapped.Headline);
        Assert.Equal(VerdictLevel.NotSafe, gone.Level);
    }

    [Fact]
    public void AnEnumerationErrorWarning_ForcesNotSafe()
    {
        var b = new OffloadPlanBuilder().Warning(new ScanWarning("EnumerationError", "Access denied: DCIM/DJI_002", "DCIM/DJI_002", true));
        var v1 = b.Video("DJI_20260927140000_0001_D.MP4", 100, T0);
        b.Group(new NewFolder(ZRel), Zachar, v1);
        var plan = b.Build();

        var v = Audit(plan, Result(OffloadCompiler.Compile(plan, "run-1"), Ok));

        Assert.Equal($"{Name}: Don't format yet: 1 part of the card couldn't be read", v.Headline);
    }

    [Fact]
    public void AFileAddedToTheCardAfterTheScan_IsNotSafe_AndListed()
    {
        var (plan, result) = TwoVideosAndAListedPhoto();
        var relisted = Unchanged(plan.Base.Scan.Inventory);
        relisted = relisted with { Entries = relisted.Entries.Add(new FsEntry(@"E:\DCIM\DJI_001\late.MP4", @"DCIM\DJI_001\late.MP4", false, 9, T0, T0, T0, 0x20)) };

        var v = Audit(plan, result, relisted);

        Assert.Equal(VerdictLevel.NotSafe, v.Level);
        Assert.Equal($"{Name}: Don't format yet: 1 file changed since the scan", v.Headline);
        Assert.Equal(["added: DCIM/DJI_001/late.MP4"], v.CardChanges.ToArray());
        Assert.Equal(["DCIM/DJI_001/late.MP4"], CardAudit.ChangedPaths(v).ToArray());
    }

    [Fact]
    public void LastAccessOnlyDifferences_AreReportedNotCounted()
    {
        var (plan, result) = TwoVideosAndAListedPhoto();
        var relisted = Unchanged(plan.Base.Scan.Inventory);
        relisted = relisted with { Entries = [.. relisted.Entries.Select(e => e with { LastAccessUtc = e.LastAccessUtc.AddDays(1) })] };

        var v = Audit(plan, result, relisted);

        Assert.Equal(VerdictLevel.Safe, v.Level);
        Assert.Equal(["OS updated last-access times on 3 files (not counted)"], v.CardChanges.ToArray());
        Assert.Empty(CardAudit.ChangedPaths(v));
    }

    [Fact]
    public void ANonNtfsDestination_AddsTheSafeRemovalNote()
    {
        var (plan, result) = TwoVideosAndAListedPhoto();

        var v = Audit(plan, result with { VolumesNeedingSafeRemoval = [@"D:\"] });

        Assert.Equal("Safely remove D: before formatting the card", v.SafeRemovalNote);
    }

    [Fact]
    public void SafeIsImpossible_WhileAnythingIsUnaccountedOrAssumed()
    {
        string[] names = ["verified", "verifiedCached", "alreadyThere", "failed", "changedOnCard", "cardSwapped", "cancelled", "notStarted",
                          "conflictAtRename", "newUnticked", "unfinishedUnticked", "probeErrorUnticked", "conflictUnticked", "ledgerVerified",
                          "ledgerNameSize", "libraryListed", "dismissedVideo", "probablyImportedPhoto", "twinCopyOff", "twinAssumed",
                          "dngVerifiedTwinFailed", "setImportedByFolder", "resumeMixed", "setOneFailed", "skip", "unknown", "unknownDismissed"];
        foreach (var name in names)
        {
            var f = AuditCategorizerTests.Case(name);
            var v = Audit(f.Plan, f.Result);
            if (v.Units.Any(u => u.Worst == AuditCategory.Unaccounted)) Assert.True(v.Level == VerdictLevel.NotSafe, name);
            else if (v.Units.Any(u => u.Worst == AuditCategory.AssumedByRule)) Assert.True(v.Level == VerdictLevel.SafeWithAssumptions, name);
            else Assert.True(v.Level == VerdictLevel.Safe, name);
        }
    }
}
