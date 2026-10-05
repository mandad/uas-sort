// tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupBuildTests.cs
using UasSort.Core.Tests.Ledger;
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class PhotoCleanupBuildTests
{
    private static readonly DateOnly Jun1 = new(2026, 6, 1), Jun30 = new(2026, 6, 30), Jul1 = new(2026, 7, 1);

    private static PhotoSurvey Survey(params PhotoItem[] items) => new(PhotoRoot, [.. items], ["notes.txt"], TestLedger.Empty());

    private static PhotoExifCache NoExif() => new(new FakePhotoFileReader(FakeLayout.NewFileSystem(), FakeLayout.Context(cardRoot: null)));

    private static PhotoCleanupPlan Build(PhotoSurvey s, PhotoCleanupMode mode = PhotoCleanupMode.BeforeDate, LightroomIndex? index = null,
                                          PhotoExifCache? exif = null)
        => PhotoCleanupPlanner.Build(s, new PhotoCleanupRequest(mode, Jun30, mode == PhotoCleanupMode.Verify ? @"X:\Lightroom" : null),
                                     index, exif ?? NoExif(), null, CancellationToken.None);

    [Fact]
    public void Build_APairWithAnUndatedOrLaterTwin_IsNotEligible()
    {
        var undated = new PhotoItem("A.DNG", PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
            [Member("A.DNG", Jun1), new PhotoMember("A.JPG", 8_000_000, Mtime, FakeFileSystem.CloudOnlyPlaceholder, null, null, CaptureSource.None,
                                                    null, PhotoCleanupRules.DateUnknownCloudOnly, null)]);
        var later = new PhotoItem("B.DNG", PhotoItemKind.Photo, PhotoSetKind.Unknown, null, [Member("B.DNG", Jun30), Member("B.JPG", Jul1, 8_000_000)]);
        var plan = Build(Survey(undated, later));
        Assert.Empty(plan.Rows);
        Assert.Equal([("A.DNG", PhotoEligibility.DateUnknown, "date unknown (cloud-only)"),
                      ("B.DNG", PhotoEligibility.StraddlesCutoff, "one of its files was shot after Jun 30")],
                     plan.NotEligible.Select(r => (r.Key, r.Eligibility, r.Why)).OrderBy(t => t.Key, StringComparer.Ordinal));
    }

    [Fact] // [Review Focus 1]
    public void VerifyMode_ACloudOnlyLedgerDatedPhoto_IsNeverOpened_AndSaysCantCheck()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(PhotoRoot + @"\A.DNG", new SyntheticDngBuilder().Build(), Mtime, FakeFileSystem.CloudOnlyPlaceholder);
        var reader = new FakePhotoFileReader(fs, FakeLayout.Context(cardRoot: null));
        var cloud = new PhotoItem("A.DNG", PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
                                  [Member("A.DNG", Jun1, attributes: FakeFileSystem.CloudOnlyPlaceholder)]);     // dated from the ledger
        var plan = Build(Survey(cloud), PhotoCleanupMode.Verify, LightroomIndex.From(@"X:\Lightroom", []), new PhotoExifCache(reader));
        var row = Assert.Single(plan.Rows);
        Assert.Equal((false, PhotoCleanupRules.CloudOnlyCantCheck), (row.Verification.Verified, row.Verification.Text));
        Assert.Empty(plan.DefaultDelete());
        Assert.Empty(reader.Opened);
        Assert.Empty(fs.HydrationViolations);
    }

    [Fact]
    public void Build_DateMode_TheCutoffDayIsIncluded_EveryRowStartsDelete_LabelledDateMode()
    {
        var plan = Build(Survey(Photo("A.DNG", Jun30), Photo("B.DNG", Jun1), Photo("C.DNG", Jul1)));
        Assert.Equal(["B.DNG", "A.DNG"], plan.Rows.Select(r => r.Key));
        Assert.All(plan.Rows, r => Assert.Equal(PhotoVerification.DateMode, r.Verification));
        Assert.Equal(2, plan.DefaultDelete().Count);
        Assert.Empty(plan.NotEligible);
        Assert.Equal(["notes.txt"], plan.NotTouched);
    }

    [Fact] // [Review Focus 2]
    public void Build_ASetStraddlingTheCutoff_IsNotEligible()
    {
        var straddling = new PhotoItem("001_0042", PhotoItemKind.Set, PhotoSetKind.Hyperlapse, "001_0042",
            [Member(@"001_0042\HYPERLAPSE_0001.DNG", Jun30), Member(@"001_0042\HYPERLAPSE_0002.DNG", Jul1)]);
        var inside = Set("001_0087", Jun30, PhotoSetKind.Panorama, "PANO_0001.DNG", "PANO_0002.DNG");
        var plan = Build(Survey(straddling, inside));
        Assert.Equal(["001_0087"], plan.Rows.Select(r => r.Key));
        var row = Assert.Single(plan.NotEligible);
        Assert.Equal((PhotoEligibility.StraddlesCutoff, "part of the set was shot after Jun 30"), (row.Eligibility, row.Why));
    }

    [Fact]
    public void Build_DateUnknown_IsListedNotEligible_WithItsReason()
    {
        var cloud = new PhotoItem("A.DNG", PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
            [new PhotoMember("A.DNG", 10, Mtime, FakeFileSystem.CloudOnlyPlaceholder, null, null, CaptureSource.None, null,
                             PhotoCleanupRules.DateUnknownCloudOnly, null)]);
        var plan = Build(Survey(cloud));
        Assert.Empty(plan.Rows);
        Assert.Equal((PhotoEligibility.DateUnknown, "date unknown (cloud-only)"), (plan.NotEligible[0].Eligibility, plan.NotEligible[0].Why));
    }

    [Fact]
    public void Build_VerifyMode_VerifiedRowsStartDelete_UnverifiedKeep_AndNeedsAnIndex()
    {
        var stamp = new ExifStamp(new DateTime(2026, 6, 1, 12, 10, 0), null, "FC9113");
        var inLr = Photo("A.DNG", Jun1, stamp: stamp);
        var notInLr = Photo("B.DNG", Jun1, stamp: stamp with { Second = stamp.Second.AddSeconds(30) });
        var index = LightroomIndex.From(@"X:\Lightroom", [new LightroomPhoto(@"X:\Lightroom\a.dng", stamp)]);
        var plan = Build(Survey(inLr, notInLr), PhotoCleanupMode.Verify, index);
        Assert.Equal(["A.DNG"], plan.DefaultDelete());
        Assert.Equal("not found in Lightroom", plan.Rows.Single(r => r.Key == "B.DNG").Verification.Text);
        Assert.Throws<ArgumentException>(() => Build(Survey(inLr), PhotoCleanupMode.Verify));
    }

    [Fact]
    public void VerifyRange_IsTheFirstEligibleDateThroughTheCutoff()
    {
        Assert.Equal<(DateOnly From, DateOnly To)?>((Jun1, Jun30), PhotoCleanupPlanner.VerifyRange(Survey(Photo("A.DNG", Jun1), Photo("C.DNG", Jul1)), Jun30));
        Assert.Null(PhotoCleanupPlanner.VerifyRange(Survey(Photo("C.DNG", Jul1)), Jun30));
    }
}
