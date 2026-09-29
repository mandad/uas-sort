using UasSort.Core.Config;
using UasSort.Core.Ledger;
using UasSort.Core;
using UasSort.Core.Tests.Ledger;
using UasSort.Core.Tests.Library;
using static UasSort.Core.Tests.Ledger.LedgerLines;

namespace UasSort.Core.Tests.Config;

public sealed class RootChangeTests
{
    private static readonly Settings Defaults = SettingsDefaults.Derive(@"C:\Users\u\Pictures");
    private static readonly string OldPhoto = Defaults.PhotoRoot;
    private static readonly string[] PhotoAndP = [@"D:\Photos", @"E:\P"];
    private const string NewVideo = @"D:\UAS Videos";
    private const string NewZrel = NewVideo + @"\2026\2026-09\2026-09-27 Zachar Bay";

    [Fact]
    public void RootChange_PhotoRootChange_AppendsTheOldRoot()
    {
        Settings s1 = SettingsEdits.ChangePhotoRoot(Defaults, @"D:\Photos");
        Assert.Equal(@"D:\Photos", s1.PhotoRoot);
        Assert.Equal(new[] { OldPhoto }, s1.PreviousPhotoRoots);

        Settings s2 = SettingsEdits.ChangePhotoRoot(s1, @"E:\P");
        Assert.Equal(new[] { OldPhoto, @"D:\Photos" }, s2.PreviousPhotoRoots);

        Settings s3 = SettingsEdits.ChangePhotoRoot(s2, OldPhoto.ToUpperInvariant() + @"\");   // back to the old root
        Assert.Equal(PhotoAndP, s3.PreviousPhotoRoots);

        Assert.Same(s3, SettingsEdits.ChangePhotoRoot(s3, s3.PhotoRoot.ToUpperInvariant() + @"\"));
    }

    private static LedgerFolderStatus NewRootStatus(LedgerFolderState state)
        => new(LedgerPaths.For(NewVideo), state, state != LedgerFolderState.Missing, true, false, true, [], [], []);

    [Fact]
    public void RootChange_HistoryPrompt_OnlyWhenTheNewRootHasNoLedgerAndWeHaveRecords()
    {
        LedgerSnapshot current = TestLedger.Snapshot(Seen("s1", "X.DNG", 1, Utc(2026, 10, 4)));
        Assert.True(VideoRootChange.NeedsHistoryPrompt(NewRootStatus(LedgerFolderState.Missing), current));
        Assert.True(VideoRootChange.NeedsHistoryPrompt(NewRootStatus(LedgerFolderState.Empty), current));
        Assert.False(VideoRootChange.NeedsHistoryPrompt(NewRootStatus(LedgerFolderState.Ok), current));        // new root already has ledger files
        Assert.False(VideoRootChange.NeedsHistoryPrompt(NewRootStatus(LedgerFolderState.Missing), TestLedger.Empty()));
        Assert.False(VideoRootChange.HasRecords(TestLedger.Empty()));
    }

    [Fact]
    public void RootChange_StartEmpty_CountsAppCopiedVideosAndTheWatermarkMoves()
    {
        DateTime T = LibraryFixture.T;
        var listing = new ListingResult([
            new FsEntry(NewZrel + @"\DJI_20260927142416_0148_D.MP4", "", false, 77_000_000, LibraryFixture.Utc(2026, 9, 27, 18, 25, 46), T, T, 0x20),
            new FsEntry(NewZrel + @"\DJI_20260927160000_0160_D.MP4", "", false, 60_000_000, LibraryFixture.Utc(2026, 9, 27, 20, 1, 30), T, T, 0x20),
            new FsEntry(NewVideo + @"\Picture Offload\DJI_20260927161000_0161_D.DNG", "", false, 27_000_000, T, T, T, 0x20),
            new FsEntry(NewVideo + @"\.uas-sort\DJI_20260927170000_0170_D.MP4", "", false, 5_000, T, T, T, 0x20)], []);
        LedgerSnapshot current = TestLedger.Snapshot(
            FileRec("f160", "DJI_20260927160000_0160_D.MP4", 60_000_000, @"C:\Old\UAS Videos\x\DJI_20260927160000_0160_D.MP4"),
            FileRec("f161", "DJI_20260927161000_0161_D.DNG", 27_000_000, @"C:\Old\Picture Offload\DJI_20260927161000_0161_D.DNG", root: "photo", kind: "photo"),
            FileRec("f170", "DJI_20260927170000_0170_D.MP4", 5_000, @"C:\Old\UAS Videos\y\DJI_20260927170000_0170_D.MP4"));

        Assert.Equal(1, VideoRootChange.AppCopiedVideos(listing, NewVideo, current));   // 0160 only: photos and .uas-sort don't count

        // the documented cost of [Start empty]: without the history the app copy 0160 moves the watermark later
        var clock = new ClockModel(ClockMode.Zone, "America/New_York", [], null, StoredClockMode.Zone, "America/New_York");
        var root = new RootListing(NewVideo, DestRoot.Video, false, true, listing);
        var photo = new RootListing(NewVideo + @"\Picture Offload", DestRoot.Photo, false, true, new ListingResult([], []));
        var listings = new LibraryListings(root, photo, []);
        Assert.Equal(LibraryFixture.Utc(2026, 9, 27, 18, 24, 16), LibraryIndex.Build(listings, current, clock).WatermarkUtc);
        Assert.Equal(LibraryFixture.Utc(2026, 9, 27, 20, 0, 0), LibraryIndex.Build(listings, TestLedger.Empty(), clock).WatermarkUtc);
    }
}
