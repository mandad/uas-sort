using Microsoft.Extensions.Time.Testing;
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Core.Tests.Offload.CardAuditTests;
using static UasSort.Core.Tests.Offload.CommitTailRecordsTests;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class VerdictDecisionsTests
{
    private const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(T0.AddHours(3)));

    private sealed record Fixture(Plan Plan, OffloadResult Result, FormatVerdict Verdict, ItemId Copied, ItemId NewVideo, ItemId Truncated,
                                  ItemId Photo1, ItemId Photo2, ItemId Set, ItemId Unknown);

    private static Fixture Build()
    {
        var b = new OffloadPlanBuilder();
        var copied = b.Video("DJI_20260927140000_0001_D.MP4", 1_200_000_000, T0);
        var fresh = b.Video("DJI_20260927140100_0002_D.MP4", 1_200_000_000, T0.AddMinutes(1), included: false);
        var truncated = b.Video("DJI_20260927140200_0003_D.MP4", 7_000_000, T0.AddMinutes(2), flags: ItemFlags.Truncated, included: false, hasTrinf: true);
        b.Group(new NewFolder(ZRel), Zachar, copied, fresh, truncated);
        var why = "videos from this day are already in the library";
        var p1 = b.Photo("DJI_20260927100000_0010_D.DNG", 27_000_000, T0.AddHours(-4), new ProbablyImported(why), twinSize: 3_000_000, included: false);
        var p2 = b.Photo("DJI_20260927100100_0011_D.DNG", 27_000_000, T0.AddHours(-4).AddMinutes(1), new ProbablyImported(why), included: false);
        var set = b.Set("001_0087", [("PANO_0001.DNG", 13_000_000), ("PANO_0002.DNG", 12_000_000)], T0.AddHours(-3), SetResolution.Plain,
                        newness: new ProbablyImported(why), included: false);
        var unknown = new ItemId(b.Unknown("DCIM/DJI_A001/x.MP4", 5_000).RelPath);
        b.Skip("DCIM/DJI_001/DJI_20260927140000_0001_D.LRF", 10, "proxy");
        var plan = b.Build();
        var result = Result(OffloadCompiler.Compile(plan, "run-1"), j => new Verified(j, UInt128.One, VerifyMode.Unbuffered));
        var verdict = CardAudit.Audit(plan.Base.Scan.Inventory, Unchanged(plan.Base.Scan.Inventory), OffloadPlanBuilder.Card, plan, result,
                                      plan.Base.Scan.Ledger, OffloadPlanBuilder.Card);
        return new Fixture(plan, result, verdict, copied, fresh, truncated, p1, p2, set, unknown);
    }

    private static NotCopiedRow Row(Fixture f, ItemId id) => VerdictDecisions.NotCopied(f.Verdict, f.Plan).Single(r => r.Unit == id);

    [Fact]
    public void NotCopied_ListsUnaccountedAndAssumedUnits_WithKindDayAndSize()
    {
        var f = Build();

        var rows = VerdictDecisions.NotCopied(f.Verdict, f.Plan);

        Assert.Equal([f.NewVideo, f.Truncated, f.Photo1, f.Photo2, f.Set, f.Unknown], rows.Select(r => r.Unit).ToArray());
        Assert.Equal([NotCopiedKind.Video, NotCopiedKind.Video, NotCopiedKind.Photo, NotCopiedKind.Photo, NotCopiedKind.Set, NotCopiedKind.Unknown],
                     rows.Select(r => r.Kind).ToArray());
        Assert.True(Row(f, f.Truncated).Truncated);
        Assert.Equal((2, 30_000_000L, (DateOnly?)new DateOnly(2026, 9, 27)), (Row(f, f.Photo1).Files, Row(f, f.Photo1).Bytes, Row(f, f.Photo1).LocalDate));
        Assert.Null(Row(f, f.Unknown).LocalDate);
        Assert.Equal(AuditCategory.AssumedByRule, Row(f, f.Set).Category);
    }

    [Fact]
    public void Day_SelectsOnlyPhotosAndSets()
    {
        var f = Build();

        var day = VerdictDecisions.Day(VerdictDecisions.NotCopied(f.Verdict, f.Plan), new DateOnly(2026, 9, 27));

        Assert.Equal([f.Photo1, f.Photo2, f.Set], day.Select(r => r.Unit).ToArray());
    }

    [Fact]
    public void Check_EnforcesWhatMayBeSelectedTogether()
    {
        var f = Build();
        NotCopiedRow R(ItemId id) => Row(f, id);

        Assert.False(VerdictDecisions.Check(DecisionKind.Dismissed, []).Ok);
        Assert.Equal("Only photos and sets can be recorded as imported",
                     VerdictDecisions.Check(DecisionKind.AssumedImported, [R(f.Photo1), R(f.NewVideo)]).Refusal);
        Assert.Equal("Videos and unknown files are marked one at a time",
                     VerdictDecisions.Check(DecisionKind.Dismissed, [R(f.NewVideo), R(f.Truncated)]).Refusal);
        Assert.False(VerdictDecisions.Check(DecisionKind.Dismissed, [R(f.Unknown), R(f.Photo1)]).Ok);
        Assert.True(VerdictDecisions.Check(DecisionKind.Dismissed, [R(f.Truncated)]).Ok);
        Assert.True(VerdictDecisions.Check(DecisionKind.Dismissed, [R(f.Unknown)]).Ok);
        Assert.True(VerdictDecisions.Check(DecisionKind.AssumedImported, [R(f.Photo1), R(f.Photo2), R(f.Set)]).Ok);
        Assert.True(VerdictDecisions.Check(DecisionKind.Dismissed, [R(f.Photo1), R(f.Photo2), R(f.Set)]).Ok);
    }

    [Fact]
    public void Confirmation_NamesCountsByKindAndGigabytes()
    {
        var f = Build();

        Assert.Equal("Record 2 photos, 1 set (0.08 GB) as already imported?",
                     VerdictDecisions.Confirmation(DecisionKind.AssumedImported, [Row(f, f.Photo1), Row(f, f.Photo2), Row(f, f.Set)]));
        Assert.Equal("Mark 1 video (1.20 GB) as not needed?", VerdictDecisions.Confirmation(DecisionKind.Dismissed, [Row(f, f.NewVideo)]));
    }

    [Fact]
    public void Records_OnePerFile_SetMembersCarryTheSetName()
    {
        var f = Build();

        var records = VerdictDecisions.Records(DecisionKind.AssumedImported, [Row(f, f.Photo1), Row(f, f.Set)], f.Plan, "run-1", "DESKTOP-A", Clock);

        Assert.Equal(
            [("DJI_20260927100000_0010_D.DNG", (string?)null), ("DJI_20260927100000_0010_D.JPG", null), ("PANO_0001.DNG", "001_0087"), ("PANO_0002.DNG", "001_0087")],
            records.Select(r => (r.Name, r.Set)).ToArray());
        Assert.All(records, r =>
        {
            Assert.Equal(("assumedImported", "run-1", "DESKTOP-A", T0.AddHours(3), 1), (r.Kind, r.Run, r.Machine, r.At, r.V));
            Assert.Equal("videos from this day are already in the library", r.Why);
        });
        Assert.Throws<InvalidOperationException>(() =>
            VerdictDecisions.Records(DecisionKind.AssumedImported, [Row(f, f.NewVideo)], f.Plan, "run-1", "DESKTOP-A", Clock));
    }

    [Fact]
    public void DismissingTheLastUnaccountedFiles_MakesTheCardSafe_AndUndoRevokesIt()
    {
        var b = new OffloadPlanBuilder();
        var copied = b.Video("DJI_20260927140000_0001_D.MP4", 100, T0);
        var fresh = b.Video("DJI_20260927140100_0002_D.MP4", 100, T0.AddMinutes(1), included: false);
        b.Group(new NewFolder(ZRel), Zachar, copied, fresh);
        var plan = b.Build();
        var result = Result(OffloadCompiler.Compile(plan, "run-1"), j => new Verified(j, UInt128.One, VerifyMode.Unbuffered));
        var relisted = Unchanged(plan.Base.Scan.Inventory);
        FormatVerdict Audit(LedgerSnapshot l)
            => CardAudit.Audit(plan.Base.Scan.Inventory, relisted, OffloadPlanBuilder.Card, plan, result, l, OffloadPlanBuilder.Card);
        var before = Audit(plan.Base.Scan.Ledger);
        Assert.Equal(VerdictLevel.NotSafe, before.Level);

        var made = VerdictDecisions.Records(DecisionKind.Dismissed, [VerdictDecisions.NotCopied(before, plan).Single()], plan, "run-1", "DESKTOP-A", Clock);
        var after = Audit(VerdictDecisions.Apply(plan.Base.Scan.Ledger, made, []));

        Assert.Equal("not needed", Assert.Single(made).Why);
        Assert.Equal(VerdictLevel.Safe, after.Level);
        Assert.Equal("E: · DJI Air 3S · serial 1A2B-3C4D: Safe to format: 1 verified, 1 confirmed by you", after.Headline);

        var revokes = VerdictDecisions.Revokes(made.Select(m => m.Id), "DESKTOP-A", Clock);
        Assert.Equal(made[0].Id, Assert.Single(revokes).Decision);
        var undone = Audit(VerdictDecisions.Apply(plan.Base.Scan.Ledger, made, revokes));
        Assert.Equal(VerdictLevel.NotSafe, undone.Level);
    }
}
