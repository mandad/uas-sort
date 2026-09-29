// tests/UasSort.Core.Tests/Cleanup/CleanupRowsTests.cs
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public class CleanupRowsTests
{
    private const long Unit5 = 5_000_003_584;

    [Fact]
    public void Keep_inside_the_free_space_prefix_extends_it_and_moves_the_cutoff()
    {
        var s = new CleanupScenario();
        var e1 = s.AddVideo(Utc(2026, 7, 3, 20, 0), size: Unit5);
        var n1 = s.AddVideo(Utc(2026, 7, 4, 20, 0), AuditCategory.Unaccounted, listed: false, size: Unit5);
        var e2 = s.AddVideo(Utc(2026, 7, 5, 20, 0), size: Unit5);
        var first = s.Build(HaveFree(20_000_000_000, include: true));
        Assert.Equal(new[] { e1, n1 }, first.Delete.Select(c => c.Unit));

        var kept = s.Build(HaveFree(20_000_000_000, include: true), CleanupRowOps.Set(NoRows, n1, delete: false));
        Assert.Equal(new[] { e1, e2 }, kept.Delete.Select(c => c.Unit));
        Assert.True(kept.Cutoff.LastCaptureUtc > first.Cutoff.LastCaptureUtc);
        Assert.Equal("kept by you: new: not in your library", kept.NotDeletable.Single(k => k.Unit == n1).Reason);
        Assert.NotEqual(first.Fingerprint, kept.Fingerprint);
    }

    [Fact]
    public void A_row_that_joins_later_is_undecided_counts_for_the_walk_and_is_never_deleted()
    {
        var s = new CleanupScenario();
        var e1 = s.AddVideo(Utc(2026, 7, 3, 20, 0), size: Unit5);
        var n1 = s.AddVideo(Utc(2026, 7, 4, 20, 0), AuditCategory.Unaccounted, listed: false, size: Unit5);
        var n2 = s.AddVideo(Utc(2026, 7, 5, 20, 0), AuditCategory.Unaccounted, listed: false, size: Unit5);
        var e2 = s.AddVideo(Utc(2026, 7, 6, 20, 0), size: Unit5);
        var shown = new HashSet<ItemId> { e1, n1, e2 };                          // n2 was out of range when first shown
        var req = HaveFree(20_000_000_000, include: true);

        var first = s.Build(req, NoRows, shown);
        Assert.Equal(new[] { e1, n1 }, first.Delete.Select(c => c.Unit));
        Assert.Empty(first.Undecided);

        var keepN1 = CleanupRowOps.Set(NoRows, n1, delete: false);
        var plan = s.Build(req, keepN1, shown);
        Assert.Equal(new[] { e1 }, plan.Delete.Select(c => c.Unit));             // n2 ends the walk but is not deleted
        Assert.Equal(new[] { n2 }, plan.Undecided);
        Assert.Equal(new[] { n1, n2 }, plan.NotInLibraryInScope.Select(c => c.Unit));
        Assert.Equal("new in range, not decided yet: new: not in your library", plan.NotDeletable.Single(k => k.Unit == n2).Reason);

        var decided = s.Build(req, CleanupRowOps.Set(keepN1, n2, delete: true), shown);
        Assert.Equal(new[] { e1, n2 }, decided.Delete.Select(c => c.Unit));
        Assert.Empty(decided.Undecided);

        var all = s.Build(req, CleanupRowOps.DeleteAll(plan), shown);
        Assert.Empty(all.Undecided);
        Assert.Equal(new[] { e1, n1 }, all.Delete.Select(c => c.Unit));           // [Delete all] also sets n1 back to Delete

        Assert.Empty(s.Build(HaveFree(20_000_000_000, include: false), keepN1, shown).Undecided);
    }

    [Fact]
    public void Before_date_keep_drops_only_that_unit_and_a_later_date_brings_undecided_rows()
    {
        var s = new CleanupScenario();
        var n1 = s.AddVideo(Utc(2026, 7, 20, 20, 0), AuditCategory.Unaccounted, listed: false);
        var e = s.AddVideo(Utc(2026, 7, 21, 20, 0));
        var n3 = s.AddVideo(Utc(2026, 7, 22, 20, 0), AuditCategory.Unaccounted, listed: false);
        var n2 = s.AddVideo(Utc(2026, 7, 27, 20, 0), AuditCategory.Unaccounted, listed: false);
        var shown = new HashSet<ItemId> { n1, e, n3 };
        var keepN1 = CleanupRowOps.Set(NoRows, n1, delete: false);

        var jul26 = s.Build(Before(2026, 7, 26, include: true), keepN1, shown);
        Assert.Equal(new[] { e, n3 }, jul26.Delete.Select(c => c.Unit));

        var jul28 = s.Build(Before(2026, 7, 28, include: true), keepN1, shown);
        Assert.Equal(new[] { e, n3 }, jul28.Delete.Select(c => c.Unit));
        Assert.Equal(new[] { n2 }, jul28.Undecided);
    }

    [Fact]
    public void Ticked_for_offload_rows_start_on_keep_and_delete_all_leaves_them()
    {
        var s = new CleanupScenario();
        var ticked = Enumerable.Range(0, 20)
            .Select(i => s.AddVideo(Utc(2026, 7, 10, 0, 0).AddHours(i), AuditCategory.Unaccounted, listed: false)).ToArray();
        foreach (var id in ticked) s.Included.Add(id);
        var req = Before(2026, 7, 26, include: true);

        var plan = s.Build(req);
        Assert.Empty(plan.Delete);
        Assert.Equal(20, plan.NotInLibraryInScope.Length);
        Assert.All(plan.NotInLibraryInScope, c => Assert.True(c.TickedForOffload));
        Assert.All(plan.NotDeletable.Where(k => k.Unit is not null), k => Assert.StartsWith("ticked for offload: ", k.Reason, StringComparison.Ordinal));

        var afterDeleteAll = s.Build(req, CleanupRowOps.DeleteAll(plan));
        Assert.Empty(afterDeleteAll.Delete);

        var one = s.Build(req, CleanupRowOps.Set(CleanupRowOps.DeleteAll(plan), ticked[7], delete: true));
        Assert.Equal(new[] { ticked[7] }, one.Delete.Select(c => c.Unit));
    }

    [Fact]
    public void Keep_all_keeps_every_review_row()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 20, 20, 0), AuditCategory.Unaccounted, listed: false);
        var e = s.AddVideo(Utc(2026, 7, 21, 20, 0));
        s.AddPhoto(Utc(2026, 7, 22, 20, 0), AuditCategory.AssumedByRule, listed: false);
        var req = Before(2026, 7, 26, include: true);
        var plan = s.Build(req);
        Assert.Equal(3, plan.Delete.Length);
        Assert.Equal(new[] { e }, s.Build(req, CleanupRowOps.KeepAll(plan)).Delete.Select(c => c.Unit));
    }
}
