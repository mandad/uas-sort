// tests/UasSort.Review.Tests/CrossAssemblyTests.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UasSort.Review.Tests;

[JsonSerializable(typeof(PlanEdit))]
[JsonSerializable(typeof(TargetChoice))]
[JsonSerializable(typeof(ImmutableArray<PlanEdit>))]
internal sealed partial class ReviewTestJsonContext : JsonSerializerContext;

public class CrossAssemblyTests
{
    private static readonly CopyJob Job = new(new ItemId("DCIM/DJI_001/a.MP4"), "DCIM/DJI_001/a.MP4", 10, DateTime.UnixEpoch,
                                              DateTime.UnixEpoch, @"C:\x\a.MP4", DestRoot.Video, null, false);

    private static string Name(CopyOutcome o) => o switch
    {
        Verified => "verified",
        AlreadyThere => "alreadyThere",
        ConflictAtRename => "conflict",
        ChangedOnCard => "changed",
        CardSwapped => "swapped",
        Failed => "failed",
        Cancelled => "cancelled",
        NotStarted => "notStarted",
    };

    private static string Name(CleanupOutcome o) => o switch
    {
        Deleted => "deleted",
        SkippedChanged => "skippedChanged",
        SkippedEvidenceGone => "evidenceGone",
        PartiallyDeleted => "partial",
        CleanupFailed => "failed",
        CleanupNotStarted => "notStarted",
        CleanupCardSwapped => "swapped",
    };

    private static string Name(GpsProbe p) => p switch
    {
        GpsFix f => "fix " + f.Sample.ToString(CultureInfo.InvariantCulture),
        NoFix n => "none " + n.Reason,
    };

    private static string Name(EraseResult r) => r switch
    {
        EraseOk => "ok",
        EraseError e => "error " + e.Win32Error.ToString(CultureInfo.InvariantCulture),
    };

    [Fact]
    public void CrossAssembly_ExhaustiveSwitches_CompileWithoutDefaultArm()
    {
        Assert.Equal("verified", Name(new Verified(Job, UInt128.One, VerifyMode.Unbuffered)));
        Assert.Equal("notStarted", Name(new NotStarted(Job)));
        Assert.Equal("partial", Name(new PartiallyDeleted(Job.Item, ["a"], ["b"], "stopped")));
        Assert.Equal("none NoGpsTag", Name(new NoFix(NoFixReason.NoGpsTag)));
        Assert.Equal("error 19", Name(new EraseError(19, "write protected")));
    }

    [Fact]
    public void CrossAssembly_PlanEditsRoundTripThroughSourceGeneratedJson()
    {
        ImmutableArray<PlanEdit> edits =
        [
            new Merge(new ItemId("a"), new ItemId("b")),
            new SplitBefore(new ItemId("c")),
            new Rename(new ItemId("a"), "Council Road", [new ItemId("a")]),
            new Retarget(new ItemId("a"), new AppendTo(@"C:\Lib\UAS Videos\2026\2026-07\2026-07-25 Council Road"), true, [new ItemId("a")]),
            new Retarget(new ItemId("a"), new SkipTarget(), false, []),
            new SetDayIncluded(new DateOnly(2026, 7, 25), false),
        ];

        var json = JsonSerializer.Serialize(edits, ReviewTestJsonContext.Default.ImmutableArrayPlanEdit);
        var back = JsonSerializer.Deserialize(json, ReviewTestJsonContext.Default.ImmutableArrayPlanEdit);

        Assert.Contains("\"t\":", json, StringComparison.Ordinal);
        Assert.Equal(edits.Length, back.Length);
        Assert.IsType<Merge>(back[0]);
        Assert.Equal("Council Road", Assert.IsType<Rename>(back[2]).Description);
        Assert.IsType<AppendTo>(Assert.IsType<Retarget>(back[3]).Choice);
        Assert.IsType<SkipTarget>(Assert.IsType<Retarget>(back[4]).Choice);
        Assert.Equal(new DateOnly(2026, 7, 25), Assert.IsType<SetDayIncluded>(back[5]).Day);
    }
}
