// tests/UasSort.Core.Tests/Offload/CommitTailRecordsTests.cs
using System.Collections.Immutable;
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class CommitTailRecordsTests
{
    internal static OffloadResult Result(OffloadBatch batch, Func<CopyJob, CopyOutcome> outcome, StopReason? stop = null)
        => new(batch.RunId, [.. batch.Jobs.Select(outcome)], stop, T0, T0.AddMinutes(5), []);

    [Fact]
    public void Seen_OnePerUncopiedPhoto_AndPerUncopiedSetMember()
    {
        var b = new OffloadPlanBuilder();
        var unticked = b.Photo("DJI_20260927140000_0001_D.DNG", 10, T0, included: false);
        var notStarted = b.Photo("DJI_20260927140100_0002_D.DNG", 20, T0.AddMinutes(1));
        var copied = b.Photo("DJI_20260927140200_0003_D.DNG", 30, T0.AddMinutes(2), twinSize: 3);
        var conflict = b.Photo("DJI_20260927140300_0004_D.DNG", 40, T0.AddMinutes(3), new Conflict(@"C:\x\DJI_20260927140300_0004_D.DNG", 99), included: false);
        b.Set("001_0087", [("PANO_0001.DNG", 5), ("PANO_0002.DNG", 6)], T0.AddMinutes(4), SetResolution.Plain);
        var plan = b.Build();
        var batch = OffloadCompiler.Compile(plan, "run-1");
        var result = Result(batch, j => j.Item == copied ? new Verified(j, UInt128.One, VerifyMode.Unbuffered) : new NotStarted(j), StopReason.Cancelled);

        var seen = CommitTailRecords.Seen(batch, plan, result, "DESKTOP-A", T0.AddMinutes(6));

        Assert.Equal(
            [("DJI_20260927140000_0001_D.DNG", "New", "unticked", (string?)null),
             ("DJI_20260927140100_0002_D.DNG", "New", "notStarted", null),
             ("DJI_20260927140300_0004_D.DNG", "Conflict", "unticked", null),
             ("PANO_0001.DNG", "New", "notStarted", "001_0087"),
             ("PANO_0002.DNG", "New", "notStarted", "001_0087")],
            seen.Select(x => (x.Name, x.Status, x.Why, x.Set)).ToArray());
        var first = seen[0];
        Assert.Equal((1, "DESKTOP-A", "run-1", T0.AddMinutes(6), 10L, "DCIM/DJI_001/DJI_20260927140000_0001_D.DNG", (DateTime?)T0),
                     (first.V, first.Machine, first.Run, first.At, first.Size, first.Src, first.CaptureUtc));
        Assert.DoesNotContain(seen, x => x.Name.Contains("0003", StringComparison.Ordinal));
        Assert.Equal(unticked, new ItemId(first.Src));
        Assert.Equal(notStarted, new ItemId(seen[1].Src));
        Assert.Equal(conflict, new ItemId(seen[2].Src));
    }

    private static (OffloadBatch Batch, Plan Plan) ResumeSet()
    {
        var b = new OffloadPlanBuilder();
        b.Set("001_0087", [("PANO_0001.DNG", 5), ("PANO_0002.DNG", 6), ("PANO_0003.DNG", 7)], T0, SetResolution.Resume,
              membersToCopy: ["PANO_0002.DNG", "PANO_0003.DNG"]);
        var plan = b.Build();
        return (OffloadCompiler.Compile(plan, "run-1"), plan);
    }

    [Fact]
    public void Seen_ForAResumeSet_WhoseMissingMembersWereAllVerified_IsEmpty()
    {
        var (batch, plan) = ResumeSet();
        var result = Result(batch, j => new Verified(j, UInt128.One, VerifyMode.Unbuffered));

        Assert.Empty(CommitTailRecords.Seen(batch, plan, result, "DESKTOP-A", T0.AddMinutes(6)));
    }

    [Fact]
    public void Seen_ForAResumeSet_CoversOnlyTheMissingMembersNotCopied()
    {
        var (batch, plan) = ResumeSet();
        var result = Result(batch, j => j.CardRelPath.EndsWith("PANO_0003.DNG", StringComparison.Ordinal)
                                        ? new NotStarted(j) : new Verified(j, UInt128.One, VerifyMode.Unbuffered), StopReason.Cancelled);

        var seen = Assert.Single(CommitTailRecords.Seen(batch, plan, result, "DESKTOP-A", T0.AddMinutes(6)));

        Assert.Equal(("PANO_0003.DNG", "notStarted", "001_0087"), (seen.Name, seen.Why, seen.Set));
    }

    [Fact]
    public void CardLeftovers_AreTheFolderPlansWithoutJobs()
    {
        var b = new OffloadPlanBuilder();
        var zFolder = new LibraryFolderRef(b.NewFolderPath(@"2026\2026-09\2026-09-27 Zachar Bay"), new DateOnly(2026, 9, 27), "Zachar Bay");
        var old = b.Video("DJI_20260927140000_0123_D.MP4", 10, T0, new Imported(Evidence.LibraryNameSize, zFolder, "listed"));
        var fresh = b.Video("DJI_20260928140000_0170_D.MP4", 10, T0.AddDays(1));
        b.Group(new AlreadyImported(zFolder), Zachar, old).Group(new NewFolder(@"2026\2026-09\2026-09-28 Next"), Zachar, fresh);
        var batch = OffloadCompiler.Compile(b.Build(), "run-1");

        var record = Assert.Single(CommitTailRecords.CardLeftovers(batch, "DESKTOP-A"));

        Assert.Equal((zFolder.FullPath, "Zachar Bay", "cardLeftovers", "run-1"), (record.Path, record.Desc, record.Source, record.Run));
        Assert.Equal((Zachar.Lat, Zachar.Lon), (record.Lat!.Value, record.Lon!.Value));
    }

    [Fact]
    public void Run_CarriesCardRootsVerdictAndNonZeroCounts()
    {
        var b = new OffloadPlanBuilder();
        b.Photo("DJI_20260927140000_0001_D.DNG", 10, T0);
        var plan = b.Build();
        var batch = OffloadCompiler.Compile(plan, "run-1");
        var result = Result(batch, j => new Verified(j, UInt128.One, VerifyMode.Unbuffered));
        var counts = Enum.GetValues<AuditCategory>().ToImmutableDictionary(c => c, c => c == AuditCategory.VerifiedThisRun ? 17 : c == AuditCategory.AssumedByRule ? 40 : 0);
        var verdict = new FormatVerdict(VerdictLevel.SafeWithAssumptions, OffloadPlanBuilder.Card, "h", counts, 0, 0, [], [], null);

        var run = CommitTailRecords.Run(batch, plan, result, verdict, "DESKTOP-A", "0.1.0");

        Assert.Equal(("run-1", T0, T0.AddMinutes(5), "0.1.0", "SafeWithAssumptions"), (run.Run, run.Start, run.End, run.App, run.Verdict));
        Assert.Equal(new RunCard("1A2B3C4D", null, "exFAT", "FC9113", "0123456789abcdef"), run.Card);
        Assert.Equal(new RunRoots(b.VideoRoot, b.PhotoRoot), run.Roots);
        Assert.Equal(2, run.Counts.Count);
        Assert.Equal((17, 40), (run.Counts["VerifiedThisRun"], run.Counts["AssumedByRule"]));
    }
}
