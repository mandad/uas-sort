// tests/UasSort.Core.Tests/Cleanup/CleanupConfirmFixturesTests.cs
#pragma warning disable CA1861 // expected path lists read best inline next to their scenario; each test runs once
namespace UasSort.Core.Tests.Cleanup;

public class CleanupConfirmFixturesTests
{
    private const string Proxy = "DCIM/DJI_001/DJI_20260726035000_0001_D.LRF";
    private const string Clip = "DCIM/DJI_001/DJI_20260726035000_0001_D.MP4";
    private const string Set = "DCIM/PANORAMA/001_0087";

    [Fact]
    public void Fixture_plan_names_exactly_the_canonical_paths_the_guard_checks()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 19, 30, 0, TimeSpan.Zero));
        var c = CleanupPlanFixtures.Confirmed(FakeLayout.CardRoot, FakeLayout.CardId,
            [Proxy, Clip, $"{Set}/PANO_0001.DNG", $"{Set}/PANO_0002.DNG"], [Set], clock);

        Assert.Equal(new[]
        {
            @"E:\DCIM\DJI_001\DJI_20260726035000_0001_D.LRF", @"E:\DCIM\DJI_001\DJI_20260726035000_0001_D.MP4",
            @"E:\DCIM\PANORAMA\001_0087\PANO_0001.DNG", @"E:\DCIM\PANORAMA\001_0087\PANO_0002.DNG",
        }, c.FilePaths.Order(StringComparer.Ordinal));
        Assert.True(c.FilePaths.Contains(@"e:\dcim\dji_001\dji_20260726035000_0001_d.lrf"));       // case-insensitive
        Assert.Equal(@"E:\DCIM\PANORAMA\001_0087", Assert.Single(c.SetFolders));
        Assert.Empty(c.NotInLibraryConfirmed);
        Assert.Equal(new DateTime(2026, 10, 12, 19, 30, 0, DateTimeKind.Utc), c.ConfirmedUtc);
        Assert.Equal((FakeLayout.CardRoot, FakeLayout.CardId), (c.Plan.CardRoot, c.Plan.Card));
        Assert.Equal(3, c.Plan.Delete.Length);                                  // the set, then the LRF and the MP4
        Assert.Equal(4, c.Plan.FileCount);
        Assert.Equal(CleanupFingerprint.Compute(c.Plan.Request, c.Plan.SpaceBefore, c.Plan.Delete), c.Plan.Fingerprint);

        var again = c.Plan.Confirm(new CleanupAck(c.Plan.Fingerprint, true, false, []), clock);   // Confirm accepts a fixture plan
        Assert.True(again.FilePaths.SetEquals(c.FilePaths));
        Assert.True(again.SetFolders.SetEquals(c.SetFolders));
    }

    [Fact]
    public void Plan_fixture_with_no_set_folder_makes_one_unit_per_file()
    {
        var plan = CleanupPlanFixtures.Plan(@"F:\", FakeLayout.CardId, [Clip], []);
        var unit = Assert.Single(plan.Delete);
        Assert.Equal(Clip, unit.Unit.CardRelPath);
        Assert.Null(unit.SetFolder);
        Assert.Equal(CleanupEligibility.Evidence, unit.Eligibility);
        Assert.Equal(@"F:\", plan.CardRoot);
    }
}
