using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Core.Tests.Offload.CommitTailRecordsTests;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class AuditCategorizerTests
{
    private const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private const string V = "DJI_20260927140000_0001_D.MP4";
    private const string P = "DJI_20260927140000_0002_D.DNG";
    private static readonly CardDiffResult NoChange = new([], [], [], 0);

    internal sealed record Fixture(Plan Plan, OffloadResult? Result, CardDiffResult Diff, ItemId Unit)
    {
        public AuditUnits Run() => AuditCategorizer.Categorize(Plan.Base.Scan.Inventory, Plan, Result, Plan.Base.Scan.Ledger, Diff);
        public UnitAudit Unit_ => Run().Units.Single(u => u.Unit == Unit);
    }

    private static LibraryFolderRef ZFolder(OffloadPlanBuilder b) => new(b.NewFolderPath(ZRel), new DateOnly(2026, 9, 27), "Zachar Bay");

    private static ItemId NewVideo(OffloadPlanBuilder b, bool included = true, ItemFlags flags = ItemFlags.None, string? probeError = null,
                                   Newness? newness = null, bool hasTrinf = false)
    {
        var v = b.Video(V, 100, T0, newness, flags, Zachar, included, hasTrinf, probeError);
        b.Group(new NewFolder(ZRel), Zachar, v);
        return v;
    }

    private static Fixture Build(OffloadPlanBuilder b, ItemId unit, Func<CopyJob, CopyOutcome>? outcome = null, CardDiffResult? diff = null)
    {
        var plan = b.Build();
        var result = outcome is null ? null : Result(OffloadCompiler.Compile(plan, "run-1"), outcome);
        return new Fixture(plan, result, diff ?? NoChange, unit);
    }

    private static CopyOutcome Ok(CopyJob j) => new Verified(j, UInt128.One, VerifyMode.Unbuffered);

    internal static Fixture Case(string name)
    {
        var b = new OffloadPlanBuilder();
        switch (name)
        {
            case "verified": return Build(b, NewVideo(b), Ok);
            case "verifiedCached": return Build(b, NewVideo(b), j => new Verified(j, UInt128.One, VerifyMode.Cached));
            case "alreadyThere": return Build(b, NewVideo(b), j => new AlreadyThere(j));
            case "failed": return Build(b, NewVideo(b), j => new Failed(j, CopyPhase.Copy, "Data error"));
            case "changedOnCard": return Build(b, NewVideo(b), j => new ChangedOnCard(j, 101, T0));
            case "cardSwapped": return Build(b, NewVideo(b), j => new CardSwapped(j, OffloadPlanBuilder.Card with { VolumeSerial = 1 }));
            case "cancelled": return Build(b, NewVideo(b), j => new Cancelled(j));
            case "notStarted": return Build(b, NewVideo(b), j => new NotStarted(j));
            case "conflictAtRename": return Build(b, NewVideo(b), j => new ConflictAtRename(j));
            case "newUnticked": return Build(b, NewVideo(b, included: false));
            case "unfinishedUnticked": return Build(b, NewVideo(b, included: false, flags: ItemFlags.Truncated, hasTrinf: true));
            case "probeErrorUnticked": return Build(b, NewVideo(b, included: false, flags: ItemFlags.ProbeFailed, probeError: "bad box"));
            case "conflictUnticked":
                return Build(b, NewVideo(b, included: false, newness: new Conflict(ZFolder(b).FullPath + "\\" + V, 555)));
            case "ledgerVerified":
            {
                b.LedgerFile(V, 100, VerifyKind.Unbuffered);
                var v = b.Video(V, 100, T0, new Imported(Evidence.LedgerVerified, null, "ledger"));
                b.Group(new AlreadyImported(ZFolder(b)), null, v);
                return Build(b, v);
            }
            case "ledgerNameSize":
            {
                b.LedgerFile(V, 100, VerifyKind.NameSize);
                var v = b.Video(V, 100, T0, new Imported(Evidence.LedgerNameSize, null, "ledger"));
                b.Group(new AlreadyImported(ZFolder(b)), null, v);
                return Build(b, v);
            }
            case "libraryListed":
            {
                b.LibraryVideo(ZRel + "\\" + V, 100);
                var v = b.Video(V, 100, T0, new Imported(Evidence.LibraryNameSize, ZFolder(b), "listed"));
                b.Group(new AlreadyImported(ZFolder(b)), null, v);
                return Build(b, v);
            }
            case "dismissedVideo":
            {
                b.LedgerDecision(V, 100, DecisionKind.Dismissed);
                var v = b.Video(V, 100, T0, new Decided(DecisionKind.Dismissed, T0, "DESKTOP-A"), included: false);
                b.Group(new NothingToCopy("nothing to copy: 1 dismissed"), null, v);
                return Build(b, v);
            }
            case "probablyImportedPhoto":
                return Build(b, b.Photo(P, 50, T0, new ProbablyImported("videos from this day are already in the library"), included: false));
            case "twinCopyOff":
            {
                b.CopyJpgTwin(false);
                return Build(b, b.Photo(P, 50, T0, twinSize: 5), Ok);
            }
            case "twinAssumed":
            {
                b.LedgerFile(P, 50, VerifyKind.Unbuffered);
                return Build(b, b.Photo(P, 50, T0, new Imported(Evidence.LedgerVerified, null, "ledger"), twinSize: 5));
            }
            case "dngVerifiedTwinFailed":
                return Build(b, b.Photo(P, 50, T0, twinSize: 5),
                             j => j.CardRelPath.EndsWith(".JPG", StringComparison.Ordinal) ? new Failed(j, CopyPhase.Verify, "mismatch twice") : Ok(j));
            case "setImportedByFolder":
                return Build(b, b.Set("001_0087", [("PANO_0001.DNG", 5), ("PANO_0002.DNG", 6)], T0, SetResolution.Imported,
                                      newness: new Imported(Evidence.LibraryNameSize, null, "set folder")));
            case "resumeMixed":
                return Build(b, b.Set("001_0087", [("PANO_0001.DNG", 5), ("PANO_0002.DNG", 6)], T0, SetResolution.Resume,
                                      membersToCopy: ["PANO_0002.DNG"]), Ok);
            case "setOneFailed":
                return Build(b, b.Set("001_0087", [("PANO_0001.DNG", 5), ("PANO_0002.DNG", 6), ("PANO_0003.DNG", 7)], T0, SetResolution.Plain),
                             j => j.CardRelPath.EndsWith("PANO_0002.DNG", StringComparison.Ordinal) ? new Failed(j, CopyPhase.Copy, "Data error") : Ok(j));
            case "skip":
                return Build(b, new ItemId(b.Skip("DCIM/DJI_001/DJI_20260927140000_0001_D.LRF", 5, "proxy").RelPath));
            case "unknown":
                return Build(b, new ItemId(b.Unknown("DCIM/DJI_A001/x.MP4", 5).RelPath));
            case "unknownDismissed":
            {
                b.LedgerDecision("x.MP4", 5, DecisionKind.Dismissed);
                return Build(b, new ItemId(b.Unknown("DCIM/DJI_A001/x.MP4", 5).RelPath));
            }
            case "changedSinceScan":
            {
                var v = NewVideo(b);
                return Build(b, v, Ok, new CardDiffResult([], [], [new CardDiffEntry(v.CardRelPath, 101)], 0));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
    }

    [Theory]
    [InlineData("verified", AuditCategory.VerifiedThisRun)]
    [InlineData("verifiedCached", AuditCategory.VerifiedThisRun)]
    [InlineData("alreadyThere", AuditCategory.NameSizeMatch)]
    [InlineData("failed", AuditCategory.Unaccounted)]
    [InlineData("changedOnCard", AuditCategory.Unaccounted)]
    [InlineData("cardSwapped", AuditCategory.Unaccounted)]
    [InlineData("cancelled", AuditCategory.Unaccounted)]
    [InlineData("notStarted", AuditCategory.Unaccounted)]
    [InlineData("conflictAtRename", AuditCategory.Unaccounted)]
    [InlineData("newUnticked", AuditCategory.Unaccounted)]
    [InlineData("unfinishedUnticked", AuditCategory.Unaccounted)]
    [InlineData("probeErrorUnticked", AuditCategory.Unaccounted)]
    [InlineData("conflictUnticked", AuditCategory.Unaccounted)]
    [InlineData("ledgerVerified", AuditCategory.InLedger)]
    [InlineData("ledgerNameSize", AuditCategory.NameSizeMatch)]
    [InlineData("libraryListed", AuditCategory.NameSizeMatch)]
    [InlineData("dismissedVideo", AuditCategory.ConfirmedByYou)]
    [InlineData("probablyImportedPhoto", AuditCategory.AssumedByRule)]
    [InlineData("twinCopyOff", AuditCategory.SkippedByRule)]
    [InlineData("twinAssumed", AuditCategory.AssumedByRule)]
    [InlineData("dngVerifiedTwinFailed", AuditCategory.Unaccounted)]
    [InlineData("setImportedByFolder", AuditCategory.NameSizeMatch)]
    [InlineData("resumeMixed", AuditCategory.NameSizeMatch)]
    [InlineData("setOneFailed", AuditCategory.Unaccounted)]
    [InlineData("skip", AuditCategory.SkippedByRule)]
    [InlineData("unknown", AuditCategory.Unaccounted)]
    [InlineData("unknownDismissed", AuditCategory.ConfirmedByYou)]
    [InlineData("changedSinceScan", AuditCategory.Unaccounted)]
    public void UnitTakesTheWorstCategoryOfItsFiles(string name, AuditCategory expected)
        => Assert.Equal(expected, Case(name).Unit_.Worst);

    [Fact]
    public void NameSizeLedgerRecords_AreNeverInLedger()
    {
        var line = Assert.Single(Case("ledgerNameSize").Unit_.Lines);
        Assert.Equal(AuditCategory.NameSizeMatch, line.Category);
        Assert.Equal("in the history, matched by name and size", line.Detail);
    }

    [Fact]
    public void TwinLines_SayWhy()
    {
        Assert.Equal("JPG twin: copying disabled in Settings", Case("twinCopyOff").Unit_.Lines[1].Detail);
        var assumed = Case("twinAssumed").Unit_;
        Assert.Equal([AuditCategory.InLedger, AuditCategory.AssumedByRule], assumed.Lines.Select(l => l.Category).ToArray());
        Assert.Equal("JPG twin assumed imported with its DNG", assumed.Lines[1].Detail);
        Assert.Equal([AuditCategory.NameSizeMatch, AuditCategory.VerifiedThisRun],
                     Case("resumeMixed").Unit_.Lines.Select(l => l.Category).ToArray());
    }

    [Theory]
    [InlineData("failed", UnaccountedKind.Failed)]
    [InlineData("newUnticked", UnaccountedKind.NotCopied)]
    [InlineData("notStarted", UnaccountedKind.NotCopied)]
    [InlineData("unfinishedUnticked", UnaccountedKind.UnfinishedNotCopied)]
    [InlineData("unknown", UnaccountedKind.Unrecognised)]
    [InlineData("changedSinceScan", UnaccountedKind.ChangedSinceScan)]
    [InlineData("changedOnCard", UnaccountedKind.ChangedSinceScan)]
    public void UnaccountedUnits_CarryTheirMostUrgentReason(string name, UnaccountedKind expected)
    {
        var f = Case(name);
        Assert.Equal(expected, f.Run().Unaccounted[f.Unit]);
    }

    [Fact]
    public void ChangedSinceScan_DetailStartsWithTheSharedPrefix()
        => Assert.StartsWith(CardDiffResult.ChangedDetail, Assert.Single(Case("changedSinceScan").Unit_.Lines).Detail, StringComparison.Ordinal);

    [Fact]
    public void TruncatedClipCopiedOrMatched_IsCounted_AndCachedVerifiesAreCounted()
    {
        var b = new OffloadPlanBuilder();
        var copied = NewVideo(b, flags: ItemFlags.Truncated, hasTrinf: true);
        var f = Build(b, copied, j => new Verified(j, UInt128.One, VerifyMode.Cached));
        var audit = f.Run();
        Assert.Equal((1, 1), (audit.TruncatedAssumed, audit.CachedVerifies));

        var m = new OffloadPlanBuilder();
        m.LibraryVideo(ZRel + "\\" + V, 100);
        var listed = m.Video(V, 100, T0, new Imported(Evidence.LibraryNameSize, ZFolder(m), "listed"), ItemFlags.Truncated, hasTrinf: true);
        m.Group(new AlreadyImported(ZFolder(m)), null, listed);
        Assert.Equal(1, Build(m, listed).Run().TruncatedAssumed);
        Assert.Equal(0, Case("unfinishedUnticked").Run().TruncatedAssumed);
    }

    [Fact]
    public void EveryCardFile_GetsExactlyOneLine_AndAddedFilesBecomeUnits()
    {
        var b = new OffloadPlanBuilder();
        var v = NewVideo(b);
        b.Skip("DCIM/DJI_001/DJI_20260927140000_0001_D.LRF", 5, "proxy");
        b.Unknown("DCIM/DJI_A001/x.MP4", 5);
        b.Photo(P, 50, T0, twinSize: 5);
        var f = Build(b, v, Ok, new CardDiffResult([new CardDiffEntry("DCIM/DJI_001/late.MP4", 9)], [], [], 0));

        var audit = f.Run();

        var lines = audit.Units.SelectMany(u => u.Lines).Select(l => l.CardRelPath).ToList();
        Assert.Equal(f.Plan.Base.Scan.Inventory.Entries.Length + 1, lines.Count);
        Assert.Equal(lines.Count, lines.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var late = audit.Units.Single(u => u.Unit == new ItemId("DCIM/DJI_001/late.MP4"));
        Assert.Equal((AuditCategory.Unaccounted, CardDiffResult.AddedDetail), (late.Worst, late.Lines[0].Detail));
    }
}
