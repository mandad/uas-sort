// tests/UasSort.Core.Tests/Cleanup/CleanupBuildFreeSpaceTests.cs
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public class CleanupBuildFreeSpaceTests
{
    private const long Unit5 = 5_000_003_584;     // 38,147 clusters of 128 KiB exactly

    private static (CleanupScenario S, ItemId[] Ids) SixEvidenceUnits()
    {
        var s = new CleanupScenario();
        var ids = Enumerable.Range(3, 6).Select(d => s.AddVideo(Utc(2026, 7, d, 20, 0), size: Unit5)).ToArray();
        return (s, ids);
    }

    [Fact]
    public void Have_free_targets_the_amount_and_takes_the_minimal_prefix()
    {
        var (s, ids) = SixEvidenceUnits();
        var plan = s.Build(HaveFree(20_000_000_000));
        Assert.Equal(new[] { ids[0], ids[1] }, plan.Delete.Select(c => c.Unit));     // 1 unit → 17.4 GB falls short
        Assert.Equal(22_400_007_168, plan.ExpectedFreeAfter);
        Assert.Null(plan.Shortfall);
        Assert.Equal(new DateTime(2026, 7, 4, 12, 0, 0), plan.Cutoff.LastLocalTime);
        Assert.Null(plan.Cutoff.BeforeDate);
    }

    [Fact]
    public void Free_up_targets_free_plus_the_amount()
    {
        var (s, ids) = SixEvidenceUnits();
        Assert.Equal(32_400_000_000, new FreeSpaceGoal(FreeSpaceKind.FreeUp, 20_000_000_000).TargetFreeBytes(s.Space));
        Assert.Equal(20_000_000_000, new FreeSpaceGoal(FreeSpaceKind.HaveFree, 20_000_000_000).TargetFreeBytes(s.Space));
        var plan = s.Build(FreeUp(20_000_000_000));
        Assert.Equal(ids[..4], plan.Delete.Select(c => c.Unit));                   // 3 units free up only 15.0 GB
    }

    [Fact]
    public void Never_units_are_passed_over_without_ending_the_walk()
    {
        var s = new CleanupScenario();
        var u1 = s.AddVideo(Utc(2026, 7, 3, 20, 0), size: Unit5);
        var never = s.AddVideo(Utc(2026, 7, 4, 20, 0), size: Unit5, probeError: "bad moov");
        var u3 = s.AddVideo(Utc(2026, 7, 5, 20, 0), size: Unit5);
        s.AddVideo(Utc(2026, 7, 6, 20, 0), size: Unit5);
        var plan = s.Build(HaveFree(20_000_000_000));
        Assert.Equal(new[] { u1, u3 }, plan.Delete.Select(c => c.Unit));
        Assert.Equal("its metadata couldn't be read", plan.NotDeletable.Single(k => k.Unit == never).Reason);
    }

    [Fact]
    public void Target_already_met_gives_an_empty_plan()
    {
        var (s, _) = SixEvidenceUnits();
        var plan = s.Build(HaveFree(10_000_000_000));
        Assert.Empty(plan.Delete);
        Assert.Null(plan.Shortfall);
        Assert.Null(plan.Cutoff.LastCaptureUtc);
        Assert.Equal(12_400_000_000, plan.ExpectedFreeAfter);
    }

    [Fact]
    public void Unreachable_target_deletes_everything_deletable_and_reports_the_shortfall()
    {
        var s = new CleanupScenario();
        var e1 = s.AddVideo(Utc(2026, 7, 3, 20, 0), size: Unit5);
        var nil = s.AddVideo(Utc(2026, 7, 4, 20, 0), AuditCategory.Unaccounted, listed: false, size: Unit5);
        s.AddVideo(Utc(2026, 7, 5, 20, 0), size: Unit5, probeError: "bad moov");
        var e2 = s.AddVideo(Utc(2026, 7, 6, 20, 0), size: Unit5);
        s.AddLoose("MISC/FC9113.db", EntryClass.Skip, 2_000_000);                  // 16 clusters = 2,097,152

        var off = s.Build(HaveFree(200_000_000_000));
        Assert.Equal(new[] { e1, e2 }, off.Delete.Select(c => c.Unit));
        Assert.Equal(new FreeSpaceShortfall(10_000_007_168, Unit5, Unit5 + 2_097_152), off.Shortfall);

        var on = s.Build(HaveFree(200_000_000_000, include: true));
        Assert.Equal(new[] { e1, nil, e2 }, on.Delete.Select(c => c.Unit));
        Assert.Equal(new FreeSpaceShortfall(15_000_010_752, 0, Unit5 + 2_097_152), on.Shortfall);
    }

    [Fact]
    public void Cluster_rounding_counts_toward_the_target()
    {
        var s = new CleanupScenario { Space = new CardSpace(1_000_000, 256_060_514_304, 131_072) };
        var one = s.AddVideo(Utc(2026, 7, 3, 20, 0), size: 1);
        var met = s.Build(HaveFree(1_131_072));
        Assert.Equal(new[] { one }, met.Delete.Select(c => c.Unit));
        Assert.Null(met.Shortfall);
        Assert.NotNull(s.Build(HaveFree(1_131_073)).Shortfall);
    }

    [Fact]
    public void Cutoff_counts_the_files_of_the_cutoff_day()
    {
        var s = new CleanupScenario { Space = new CardSpace(1_000_000_000, 256_060_514_304, 131_072) };
        s.AddVideo(Utc(2026, 7, 3, 20, 0), size: Unit5, companions: Comp.Lrf | Comp.Srt);
        var cut = s.AddVideo(Utc(2026, 8, 30, 22, 22), size: Unit5, companions: Comp.Lrf | Comp.Srt);   // Aug 30 14:22 AKDT
        s.AddVideo(Utc(2026, 8, 30, 23, 0), size: Unit5, companions: Comp.Lrf | Comp.Srt);
        s.AddVideo(Utc(2026, 8, 31, 1, 0), size: Unit5, companions: Comp.Lrf | Comp.Srt);              // Aug 30 17:00 AKDT
        var plan = s.Build(HaveFree(10_000_000_000));
        Assert.Equal(cut, plan.Delete[^1].Unit);
        Assert.Equal(6, plan.FileCount);
        Assert.Equal(10_040_377_344, plan.AllocatedBytes);
        Assert.Equal(new DateTime(2026, 8, 30, 14, 22, 0), plan.Cutoff.LastLocalTime);
        Assert.Equal(3, plan.Cutoff.FilesDeletedOnCutoffDay);
        Assert.Equal(9, plan.Cutoff.FilesOnCutoffDay);
    }

    [Fact]
    public void Free_space_without_a_goal_is_a_caller_bug()
    {
        var (s, _) = SixEvidenceUnits();
        Assert.Throws<ArgumentException>(() => s.Build(new CleanupRequest(CleanupMode.FreeSpace, null, null, false)));
    }
}
