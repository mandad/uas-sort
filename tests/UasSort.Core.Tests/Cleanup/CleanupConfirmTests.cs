// tests/UasSort.Core.Tests/Cleanup/CleanupConfirmTests.cs
#pragma warning disable CA1861 // expected path lists read best inline next to their scenario; each test runs once
using System.Reflection;
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public class CleanupConfirmTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 12, 19, 30, 0, TimeSpan.Zero));

    private static CleanupAck Ack(CleanupPlan p, bool cantBeRecovered = true, bool? includes = null, ImmutableHashSet<ItemId>? nil = null)
    {
        var units = nil ?? p.Delete.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary).Select(c => c.Unit).ToImmutableHashSet();
        return new CleanupAck(p.Fingerprint, cantBeRecovered, includes ?? units.Count > 0, units);
    }

    private static (CleanupScenario S, ItemId Evidence, ItemId Nil, ItemId Set) Card()
    {
        var s = new CleanupScenario();
        var e = s.AddVideo(Utc(2026, 7, 20, 20, 0), companions: Comp.Lrf);
        var n = s.AddVideo(Utc(2026, 7, 21, 20, 0), AuditCategory.Unaccounted, listed: false);
        var set = s.AddSet("001_0087", Utc(2026, 7, 22, 20, 0));
        return (s, e, n, set);
    }

    /// <summary>The same plan with another Delete list, through Part 02's 18-argument internal constructor.</summary>
    private static CleanupPlan Rebuild(CleanupPlan p, ImmutableArray<CleanupCandidate> delete, string? fingerprint = null)
    {
        var allocated = delete.Sum(c => c.AllocatedBytes);
        return new(p.PlanId, p.Card, p.CardRoot, p.InventoryHash, p.CameraModel, p.Request, p.SpaceBefore, delete,
                   p.NotInLibraryInScope, p.Rows, p.Undecided, p.NotDeletable, p.Cutoff, delete.Sum(c => c.Files.Length), allocated,
                   Math.Min(p.SpaceBefore.TotalBytes, p.SpaceBefore.FreeBytes + allocated), p.Shortfall,
                   fingerprint ?? CleanupFingerprint.Compute(p.Request, p.SpaceBefore, delete));
    }

    [Fact]
    public void Confirm_returns_canonical_paths_set_folders_and_per_unit_tokens()
    {
        var (s, _, n, _) = Card();
        var plan = s.Build(Before(2026, 7, 26, include: true));
        var confirmed = plan.Confirm(Ack(plan), Clock);
        Assert.Same(plan, confirmed.Plan);
        Assert.NotEqual(Guid.Empty, confirmed.Token);
        Assert.Equal(new DateTime(2026, 10, 12, 19, 30, 0, DateTimeKind.Utc), confirmed.ConfirmedUtc);
        Assert.Equal(6, confirmed.FilePaths.Count);                              // LRF + MP4, MP4, 3 members
        Assert.True(confirmed.FilePaths.Contains(@"E:\DCIM\DJI_001\DJI_20260720120000_0101_D.MP4"));
        Assert.True(confirmed.FilePaths.Contains(@"e:\dcim\dji_001\dji_20260720120000_0101_d.lrf"));   // case-insensitive
        Assert.Equal(new[] { @"E:\DCIM\PANORAMA\001_0087" }, confirmed.SetFolders);
        Assert.Equal(new[] { n }, confirmed.NotInLibraryConfirmed);
        Assert.NotEqual(plan.Confirm(Ack(plan), Clock).Token, confirmed.Token);
    }

    [Fact]
    public void Confirm_refuses_a_missing_or_wrong_acknowledgement()
    {
        var (s, _, n, _) = Card();
        var plan = s.Build(Before(2026, 7, 26, include: true));
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan) with { PlanFingerprint = "0000000000000000" }, Clock));
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan, cantBeRecovered: false), Clock));
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan, nil: []), Clock));                    // row decision missing
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan, includes: false), Clock));            // box unticked
        var evidenceOnly = s.Build(Before(2026, 7, 26));
        Assert.Throws<InvalidOperationException>(() => evidenceOnly.Confirm(Ack(evidenceOnly, includes: true, nil: []), Clock));
        Assert.Throws<InvalidOperationException>(() => evidenceOnly.Confirm(Ack(evidenceOnly, nil: [n]), Clock));
    }

    [Fact]
    public void Confirm_refuses_an_undecided_row_and_an_empty_plan()
    {
        var (s, e, n, set) = Card();
        var undecided = s.Build(Before(2026, 7, 26, include: true), NoRows, new HashSet<ItemId> { e, set });
        Assert.Equal(new[] { n }, undecided.Undecided);
        Assert.Throws<InvalidOperationException>(() => undecided.Confirm(Ack(undecided), Clock));
        var empty = s.Build(Before(2026, 7, 1));
        Assert.Empty(empty.Delete);
        Assert.Throws<InvalidOperationException>(() => empty.Confirm(Ack(empty), Clock));
    }

    [Fact]
    public void Confirm_recomputes_the_fingerprint_and_never_trusts_the_stored_one()
    {
        var (s, _, _, _) = Card();
        var plan = s.Build(Before(2026, 7, 26));
        var tampered = Rebuild(plan, plan.Delete.RemoveAt(0), plan.Fingerprint);
        Assert.Throws<InvalidOperationException>(() => tampered.Confirm(Ack(plan), Clock));
    }

    [Fact]
    public void Confirm_refuses_never_candidates_paths_outside_dcim_and_non_set_folders()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 20, 20, 0));
        s.AddVideo(Utc(2026, 7, 21, 20, 0), probeError: "bad moov");
        var plan = s.Build(Before(2026, 7, 26));
        var never = s.Candidates().Single(c => c.Eligibility == CleanupEligibility.Never);
        var ok = plan.Delete[0];

        var withNever = Rebuild(plan, [ok, never]);
        Assert.Throws<InvalidOperationException>(() => withNever.Confirm(Ack(withNever), Clock));

        var misc = ok with { Files = [ok.Files[0] with { RelPath = "MISC/FC9113.db" }] };
        var outside = Rebuild(plan, [misc]);
        Assert.Throws<InvalidOperationException>(() => outside.Confirm(Ack(outside), Clock));

        var dots = ok with { Files = [ok.Files[0] with { RelPath = "DCIM/../Windows/explorer.exe" }] };
        var escaping = Rebuild(plan, [dots]);
        Assert.Throws<InvalidOperationException>(() => escaping.Confirm(Ack(escaping), Clock));

        var notSet = ok with { SetFolder = "DCIM/DJI_001" };
        var badFolder = Rebuild(plan, [notSet]);
        Assert.Throws<InvalidOperationException>(() => badFolder.Confirm(Ack(badFolder), Clock));
    }

    [Theory]
    [InlineData(typeof(CleanupPlan))]
    [InlineData(typeof(ConfirmedCleanupPlan))]
    public void Plans_are_sealed_classes_without_public_constructors_or_with(Type t)
    {
        Assert.True(t.IsClass && t.IsSealed);
        Assert.Empty(t.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.All(t.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance), c => Assert.True(c.IsAssembly));
        Assert.Null(t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
    }
}
