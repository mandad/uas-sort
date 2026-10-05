// tests/UasSort.Core.Tests/Offload/PreflightAckScopeTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Offload;

/// <summary>Task U7 (user report): Planner.Derive → OffloadCompiler → Preflight.Check → PreflightAcks.Required, end to end. A group
/// that copies nothing (already imported, skipped or fully unticked) never asks for an acknowledgement on the preflight sheet.</summary>
public class PreflightAckScopeTests
{
    private const string CouncilRel = @"2026\2026-07\2026-07-25 Council Road";
    private static readonly RawItem C117 = Clip.Vid("20260725232655", 117, Sites.Council);
    private static readonly RawItem C118 = Clip.Vid("20260726022937", 118, Sites.Council);
    private static readonly RawItem A1 = Clip.Vid("20260726235645", 1, Sites.Anvil);
    private static readonly RawItem A2 = Clip.Vid("20260727000012", 2, Sites.Anvil);
    private static readonly RawItem NewC = Clip.Vid("20250725232655", 17, Sites.Council);
    private static readonly RawItem NewA = Clip.Vid("20250726235645", 18, Sites.Anvil);

    private static PreflightReport Preflighted(Plan plan)
    {
        var fs = FakeLayout.NewFileSystem();
        var batch = OffloadCompiler.Compile(plan, "run-1", FakeLayout.CardId);
        return Preflight.Check(batch, plan, new FakeFileOps(fs, OffloadCompiler.NewFolderDirs(batch, FakeLayout.VideoRoot)), fs,
                               new FakeCardReader(fs, FakeLayout.CardRoot, FakeLayout.CardId),
                               new FakeLedgerStore(null, FakeLayout.VideoRoot, FakeLayout.Machine, plan.Base.Scan.Ledger),
                               new FakeOffloadLock(), plan.Base.Scan.Settings);
    }

    [Fact]
    public void AnAlreadyImportedDaySplit_IsNotAnAck_ButTheNewGroupsSplitIs()
    {
        var b = new PlanScenario { Root = FakeLayout.VideoRoot }.Library(CouncilRel, C117, C118, A1, A2)
                                                              .Card(C117, C118, A1, A2, NewC, NewA).Prepare();
        var plan = PlanScenario.Derive(b);
        Assert.IsType<AlreadyImported>(Assert.Single(plan.Groups, g => g.Videos.Contains(C117.Id())).Target);
        Assert.Contains(Assert.Single(plan.Groups, g => g.Videos.Contains(C117.Id())).DaySplits, d => d.Emphasised);

        var acks = PreflightAcks.Required(Preflighted(plan));

        var ack = Assert.Single(acks);
        Assert.Equal((IssueCode.EmphasisedDaySplit, (ItemId?)NewA.Id()), (ack.Code, ack.Anchor));
    }

    [Fact]
    public void ASkippedOrUntickedGroup_NeedsNoAck()
    {
        var b = new PlanScenario { Root = FakeLayout.VideoRoot }.Card(C117, C118, A1, A2).Prepare();
        var members = new[] { C117, C118, A1, A2 }.Select(x => x.Id()).ToArray();
        Assert.Single(PreflightAcks.Required(Preflighted(PlanScenario.Derive(b))));

        var skipped = PlanScenario.Derive(b, edits: [new Retarget(C117.Id(), new SkipTarget(), false, [.. members])]);
        Assert.Empty(PreflightAcks.Required(Preflighted(skipped)));
        var unticked = PlanScenario.Derive(b, edits: [new SetIncluded([.. members], false)]);
        Assert.Empty(PreflightAcks.Required(Preflighted(unticked)));
    }
}
