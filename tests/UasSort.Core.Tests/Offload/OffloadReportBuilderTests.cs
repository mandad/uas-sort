using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Core.Tests.Offload.CardAuditTests;
using static UasSort.Core.Tests.Offload.CommitTailRecordsTests;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class OffloadReportBuilderTests
{
    [Fact]
    public void Report_HasOneLinePerJob_EveryAuditLine_AndTheVerdict()
    {
        var b = new OffloadPlanBuilder();
        var ok = b.Video("DJI_20260927140000_0001_D.MP4", 4_000_000, T0);
        var bad = b.Video("DJI_20260927140100_0002_D.MP4", 3_000_000, T0.AddMinutes(1));
        b.Group(new NewFolder(@"2026\2026-09\2026-09-27 Zachar Bay"), Zachar, ok, bad);
        var photo = b.Photo("DJI_20260927140200_0003_D.DNG", 2_000_000, T0.AddMinutes(2));
        b.Set("001_0087", [("PANO_0001.DNG", 500_000), ("PANO_0002.DNG", 500_000)], T0.AddMinutes(3), SetResolution.Plain);
        b.Skip("DCIM/DJI_001/DJI_20260927140000_0001_D.LRF", 10, "proxy");
        var plan = b.Build();
        var batch = OffloadCompiler.Compile(plan, "run-1");
        var hash = (UInt128)0xABCDEF;
        var result = Result(batch, j => j.Item == bad ? new Failed(j, CopyPhase.Verify, "The copy didn't match the card twice")
                                       : j.Item == photo ? new AlreadyThere(j)
                                       : new Verified(j, hash, VerifyMode.Cached), StopReason.Cancelled);
        var verdict = CardAudit.Audit(plan.Base.Scan.Inventory, Unchanged(plan.Base.Scan.Inventory), OffloadPlanBuilder.Card, plan, result,
                                      plan.Base.Scan.Ledger, OffloadPlanBuilder.Card);

        var report = OffloadReportBuilder.Build(plan, batch, result, verdict);

        Assert.Equal((1, "run-1", StopReason.Cancelled, verdict.Level, verdict.Headline),
                     (report.V, report.RunId, report.Stop, report.Verdict, report.Headline));
        Assert.Equal(plan.Base.Scan.Settings, report.SettingsSnapshot);
        Assert.Equal("1 folder · 2 videos · 1 photo · 1 set · 0.01 GB", report.PlanSummary);
        Assert.Equal(batch.Jobs.Length, report.Files.Length);
        var first = report.Files[0];
        Assert.Equal((batch.Jobs[0].CardRelPath, (string?)batch.Jobs[0].DestPath, "Verified", (CopyPhase?)null, (string?)null,
                      (string?)"00000000000000000000000000abcdef", (string?)"cached"),
                     (first.CardRelPath, first.Dest, first.Outcome, first.Phase, first.Error, first.Xxh128, first.Verify));
        var failed = report.Files[1];
        Assert.Equal(("Failed", (CopyPhase?)CopyPhase.Verify, (string?)"The copy didn't match the card twice"), (failed.Outcome, failed.Phase, failed.Error));
        Assert.Equal(("AlreadyThere", (string?)"nameSize"), (report.Files[2].Outcome, report.Files[2].Verify));
        Assert.Equal(plan.Base.Scan.Inventory.Entries.Length, report.Audit.Length);
        Assert.Equal(verdict.CardChanges, report.CardChanges);
    }

    [Fact]
    public void Lines_DescribeCardChangesAndSwaps()
    {
        var job = new CopyJob(new ItemId("DCIM/DJI_001/a.MP4"), "DCIM/DJI_001/a.MP4", 10, T0, T0, @"C:\V\a.MP4", DestRoot.Video, null, true);

        Assert.Equal("now 11 bytes, modified 2026-09-27 18:00:02Z",
                     OffloadReportBuilder.Line(new ChangedOnCard(job, 11, T0.AddSeconds(2))).Error);
        Assert.Equal("a different card is in the reader (serial DEAD-BEEF)",
                     OffloadReportBuilder.Line(new CardSwapped(job, OffloadPlanBuilder.Card with { VolumeSerial = 0xDEADBEEF })).Error);
        Assert.Equal(("NotStarted", (string?)null), (OffloadReportBuilder.Line(new NotStarted(job)).Outcome, OffloadReportBuilder.Line(new NotStarted(job)).Verify));
    }
}
