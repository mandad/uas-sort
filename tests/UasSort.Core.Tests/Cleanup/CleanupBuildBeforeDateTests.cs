using static UasSort.Core.Tests.Cleanup.CleanupScenario;
#pragma warning disable CA1861 // expected path lists read best inline next to their scenario; each test runs once

namespace UasSort.Core.Tests.Cleanup;

public class CleanupBuildBeforeDateTests
{
    private static readonly DateTime Oct4 = Utc(2026, 10, 4, 20, 0);

    [Fact]
    public void Deletes_units_strictly_before_the_site_local_day_and_keeps_the_day_itself()
    {
        var s = new CleanupScenario();
        var jul24 = s.AddVideo(Utc(2026, 7, 24, 20, 0));
        var jul25 = s.AddVideo(Utc(2026, 7, 26, 7, 50));          // Jul 25 23:50 AKDT
        var jul26 = s.AddVideo(Utc(2026, 7, 26, 8, 10));          // Jul 26 00:10 AKDT
        var jul27 = s.AddVideo(Utc(2026, 7, 27, 20, 0));
        var plan = s.Build(Before(2026, 7, 26));
        Assert.Equal(new[] { jul24, jul25 }, plan.Delete.Select(c => c.Unit));
        Assert.DoesNotContain(plan.Delete, c => c.Unit == jul26 || c.Unit == jul27);
        Assert.Equal(new DateOnly(2026, 7, 26), plan.Cutoff.BeforeDate);
        Assert.Equal(2, plan.FileCount);
        Assert.Equal(2_000_158_720, plan.AllocatedBytes);
        Assert.Equal(14_400_158_720, plan.ExpectedFreeAfter);
        Assert.Empty(plan.NotDeletable.Where(k => k.Unit is not null));
        Assert.Null(plan.Shortfall);
        Assert.Equal(CardRoot, plan.CardRoot);
        Assert.Equal(Identity, plan.Card);
        Assert.Equal("9f3c0a6d12e4b7a1", plan.InventoryHash);
        Assert.Equal("FC9113", plan.CameraModel);
    }

    [Fact]
    public void A_flight_across_local_midnight_is_split_and_reported()
    {
        var s = new CleanupScenario();
        var session = new SessionKey("1581F6Z", Utc(2026, 7, 26, 7, 40));
        var before = s.AddVideo(Utc(2026, 7, 26, 7, 50), session: session);   // 20260725235000 local
        var after = s.AddVideo(Utc(2026, 7, 26, 8, 10), session: session);
        var plan = s.Build(Before(2026, 7, 26));
        Assert.Equal(new[] { before }, plan.Delete.Select(c => c.Unit));
        Assert.DoesNotContain(plan.Delete, c => c.Unit == after);
        Assert.Equal(new DateTime(2026, 7, 26, 0, 10, 0), plan.Cutoff.FlightContinuesLocal);
        Assert.Equal(new DateTime(2026, 7, 25, 23, 50, 0), plan.Cutoff.LastLocalTime);
        Assert.Equal(Ak, plan.Cutoff.TzId);
    }

    [Fact]
    public void Site_local_date_decides_not_the_pc_zone()
    {
        var s = new CleanupScenario();
        var makaha = s.AddVideo(Utc(2026, 3, 1, 9, 30), tz: "Pacific/Honolulu");   // Feb 28 23:30 HST; Mar 1 in Alaska
        Assert.Equal(new[] { makaha }, s.Build(Before(2026, 3, 1)).Delete.Select(c => c.Unit));
        Assert.Empty(s.Build(Before(2026, 2, 28)).Delete);
    }

    [Fact]
    public void Not_in_library_units_need_the_switch_and_decisions_are_not_consent()
    {
        var s = new CleanupScenario();
        var fresh = s.AddVideo(Utc(2026, 7, 20, 20, 0), AuditCategory.Unaccounted, listed: false);
        var dismissed = s.AddVideo(Utc(2026, 7, 21, 20, 0), listed: false);
        s.Decide(dismissed, DecisionKind.Dismissed, Oct4);
        var assumed = s.AddPhoto(Utc(2026, 7, 22, 20, 0), listed: false);
        s.Decide(assumed, DecisionKind.AssumedImported, Oct4);
        var evidence = s.AddVideo(Utc(2026, 7, 23, 20, 0));

        var off = s.Build(Before(2026, 7, 26));
        Assert.Equal(new[] { evidence }, off.Delete.Select(c => c.Unit));
        Assert.Equal(new[] { fresh, dismissed, assumed }, off.NotInLibraryInScope.Select(c => c.Unit));
        Assert.Equal("not in your library: you marked it not needed on Oct 4",
                     off.NotDeletable.Single(k => k.Unit == dismissed).Reason);
        Assert.Empty(off.Undecided);
        Assert.Equal(NotInLibraryReason.RecordedAsImported, off.NotInLibraryInScope.Single(c => c.Unit == assumed).NotInLibrary);
        Assert.Equal("you recorded it as imported on Oct 4; not verified", off.NotInLibraryInScope.Single(c => c.Unit == assumed).Reason);

        var on = s.Build(Before(2026, 7, 26, include: true));
        Assert.Equal(new[] { fresh, dismissed, assumed, evidence }, on.Delete.Select(c => c.Unit));
    }

    [Fact]
    public void Never_units_in_range_are_kept_with_their_reason_and_loose_files_are_listed_apart()
    {
        var s = new CleanupScenario();
        var probe = s.AddVideo(Utc(2026, 7, 20, 20, 0), probeError: "bad moov");
        var laterNever = s.AddVideo(Utc(2026, 7, 28, 20, 0), probeError: "bad moov");
        s.AddLoose("MISC/FC9113.db", EntryClass.Skip, 2_000_000);
        s.AddDir("DCIM");
        var plan = s.Build(Before(2026, 7, 26));
        Assert.Empty(plan.Delete);
        var kept = plan.NotDeletable.Single(k => k.Unit == probe);
        Assert.Equal("its metadata couldn't be read", kept.Reason);
        Assert.Equal(new[] { "DCIM/DJI_001/DJI_20260720120000_0101_D.MP4" }, kept.CardRelPaths);
        Assert.DoesNotContain(plan.NotDeletable, k => k.Unit == laterNever);
        var misc = Assert.Single(plan.NotDeletable, k => k.Unit is null);
        Assert.Equal(new[] { "MISC/FC9113.db" }, misc.CardRelPaths);
        Assert.Equal("DJI system file", misc.Reason);
    }

    [Fact]
    public void Fingerprint_is_stable_for_the_same_content_and_changes_with_it()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 24, 20, 0));
        s.AddVideo(Utc(2026, 7, 27, 20, 0));
        var a = s.Build(Before(2026, 7, 26));
        var b = s.Build(Before(2026, 7, 26));
        Assert.NotEqual(a.PlanId, b.PlanId);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Matches("^[0-9a-f]{16}$", a.Fingerprint);
        Assert.NotEqual(a.Fingerprint, s.Build(Before(2026, 7, 25)).Fingerprint);   // same Delete, other request
        Assert.NotEqual(a.Fingerprint, s.Build(Before(2026, 7, 28)).Fingerprint);   // other Delete
        Assert.Equal(a.Fingerprint, CleanupFingerprint.Compute(a.Request, a.SpaceBefore, a.Delete.Reverse()));
    }

    [Fact]
    public void Before_date_without_a_date_is_a_caller_bug()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 24, 20, 0));
        Assert.Throws<ArgumentException>(() => s.Build(new CleanupRequest(CleanupMode.BeforeDate, null, null, false)));
    }
}
