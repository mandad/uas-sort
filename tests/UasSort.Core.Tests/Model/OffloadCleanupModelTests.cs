using System.Reflection;
using UasSort.Core.Tests.Support;

namespace UasSort.Core.Tests.Model;

public class OffloadCleanupModelTests
{
    private static readonly CardSpace Space = new(12_400_000_000, 256_060_514_304, 131_072);

    [Theory]
    [InlineData(FreeSpaceKind.FreeUp, 20_000_000_000L, 32_400_000_000L)]
    [InlineData(FreeSpaceKind.HaveFree, 20_000_000_000L, 20_000_000_000L)]
    [InlineData(FreeSpaceKind.HaveFree, 300_000_000_000L, 256_060_514_304L)]
    public void FreeSpaceGoal_TargetsBothReadings(FreeSpaceKind kind, long bytes, long expected)
        => Assert.Equal(expected, new FreeSpaceGoal(kind, bytes).TargetFreeBytes(Space));

    [Fact]
    public void PreflightReport_CanStartOnlyWithoutBlockingIssues()
    {
        static Issue I(IssueSeverity s) => new(s, IssueCode.StaleTempFiles, "x", null, [], false);
        Assert.True(new PreflightReport([I(IssueSeverity.Warning), I(IssueSeverity.Info)], [], [], [], [], []).CanStart);
        Assert.False(new PreflightReport([I(IssueSeverity.Blocking)], [], [], [], [], []).CanStart);
    }

    [Fact]
    public void ConfirmedCleanupPlan_DerivesCanonicalCaseInsensitiveSets()
    {
        var video = TestCleanupPlans.Candidate("DCIM/DJI_001/DJI_20260726035000_0001_D.MP4",
            ["DCIM/DJI_001/DJI_20260726035000_0001_D.LRF", "DCIM/DJI_001/DJI_20260726035000_0001_D.MP4"]);
        var set = TestCleanupPlans.Candidate("DCIM/PANORAMA/001_0087",
            ["DCIM/PANORAMA/001_0087/PANO_0001.DNG", "DCIM/PANORAMA/001_0087/PANO_0002.DNG"], setFolder: "DCIM/PANORAMA/001_0087");
        var nil = TestCleanupPlans.Candidate("DCIM/DJI_001/DJI_20260726001000_0002_D.MP4",
            ["DCIM/DJI_001/DJI_20260726001000_0002_D.MP4"], CleanupEligibility.NotInLibrary);

        var c = TestCleanupPlans.Confirmed(@"E:\", video, set, nil);

        Assert.Equal(5, c.FilePaths.Count);
        Assert.True(c.FilePaths.Contains(@"e:\dcim\dji_001\DJI_20260726035000_0001_d.lrf"));
        Assert.True(c.FilePaths.Contains(@"E:\DCIM\PANORAMA\001_0087\PANO_0002.DNG"));
        Assert.Equal(@"E:\DCIM\PANORAMA\001_0087", Assert.Single(c.SetFolders));
        Assert.Equal(nil.Unit, Assert.Single(c.NotInLibraryConfirmed));
        Assert.Equal(@"E:\", c.Plan.CardRoot);
        Assert.Equal(TestCleanupPlans.InventoryHash, c.Plan.InventoryHash);
        Assert.Equal(TestCleanupPlans.CameraModel, c.Plan.CameraModel);
    }

    [Fact]
    public void CleanupPlan_And_ConfirmedCleanupPlan_HaveNoPublicConstructor()
    {
        Assert.Empty(typeof(CleanupPlan).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(typeof(ConfirmedCleanupPlan).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void Outcome_And_Result_Unions_AreExhaustive()
    {
        var job = new CopyJob(new ItemId("DCIM/DJI_001/a.MP4"), "DCIM/DJI_001/a.MP4", 1, TestCleanupPlans.T, TestCleanupPlans.T,
            @"C:\V\a.MP4", DestRoot.Video, null, false);
        CopyOutcome[] outcomes =
        [
            new Verified(job, 1, VerifyMode.Unbuffered), new AlreadyThere(job), new ConflictAtRename(job),
            new ChangedOnCard(job, 2, TestCleanupPlans.T), new CardSwapped(job, TestCleanupPlans.Card),
            new Failed(job, CopyPhase.Verify, "hash mismatch"), new Cancelled(job), new NotStarted(job),
        ];
        Assert.Equal(8, outcomes.Select(o => o switch
        {
            Verified => 1, AlreadyThere => 2, ConflictAtRename => 3, ChangedOnCard => 4, CardSwapped => 5,
            Failed => 6, Cancelled => 7, NotStarted => 8,
        }).Distinct().Count());

        var u = new ItemId("DCIM/DJI_001/a.MP4");
        CleanupOutcome[] cleanup =
        [
            new Deleted(u, 1, 1, false), new SkippedChanged(u, u.CardRelPath, null, null),
            new SkippedEvidenceGone(u, u.CardRelPath, "no longer listed"), new PartiallyDeleted(u, [], [], "error"),
            new CleanupFailed(u, u.CardRelPath, 5, "Access is denied."), new CleanupNotStarted(u),
            new CleanupCardSwapped(u, TestCleanupPlans.Card),
        ];
        Assert.Equal(7, cleanup.Select(o => o switch
        {
            Deleted => 1, SkippedChanged => 2, SkippedEvidenceGone => 3, PartiallyDeleted => 4, CleanupFailed => 5,
            CleanupNotStarted => 6, CleanupCardSwapped => 7,
        }).Distinct().Count());

        EraseResult erase = new EraseError(19, "The media is write protected.");
        VerifyResult verify = new HashMismatch(7, VerifyMode.Cached);
        RenameResult rename = new TargetExists();
        EjectResult eject = new EjectRefused(@"D:\", "in use");
        Assert.Equal("err", erase switch { EraseOk => "ok", EraseError => "err" });
        Assert.Equal("mismatch", verify switch { HashMatch => "match", HashMismatch => "mismatch" });
        Assert.Equal("exists", rename switch { Renamed => "renamed", TargetExists => "exists" });
        Assert.Equal("refused", eject switch { Ejected => "ejected", EjectRefused => "refused" });
    }

    [Fact]
    public void UnsafeIoException_IsNotAnIOException()
    {
        var e = new UnsafeIoException("ReadData of a library file");
        Assert.IsNotAssignableFrom<IOException>(e);
        Assert.Equal("ReadData of a library file", e.Message);
    }
}
