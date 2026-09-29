// tests/UasSort.Core.Tests/Library/LibraryIndexTests.cs
using UasSort.Core;
using UasSort.Core.Tests.Ledger;
using static UasSort.Core.Tests.Ledger.LedgerLines;
using static UasSort.Core.Tests.Library.LibraryFixture;

namespace UasSort.Core.Tests.Library;

public sealed class LibraryIndexTests
{
    private const string V = VideoRoot;
    private const string Zrel = V + @"\2026\2026-09\2026-09-27 Zachar Bay";
    private const string Council = V + @"\2026\2026-07\2026-07-25 Council Road";
    private const string Anvil = V + @"\2026\2026-07\2026-07-26 Anvil Mountain";

    private static readonly ClockModel EasternZone =
        new(ClockMode.Zone, "America/New_York", [], null, StoredClockMode.Zone, "America/New_York");
    private static readonly ClockModel SiteLocalClock =
        new(ClockMode.SiteLocal, null, [], null, StoredClockMode.SiteLocal, "America/New_York");

    private static LibraryFolder FolderAt(LibraryIndex lib, string path)
        => lib.Folders.Single(f => string.Equals(f.Ref.FullPath, path, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void LibraryIndex_ReviewFocus_NonEventFoldersAndLooseFiles()
    {
        var listings = new LibraryFixture()
            .WithFile(V + @"\desktop.ini", 282, LibraryFixture.Utc(2026, 1, 1))
            .WithFile(PhotoRoot + @"\DJI_20260815200000_0119_D.DNG", 27_000_000, LibraryFixture.Utc(2026, 8, 16))
            .WithFile(PhotoRoot + @"\desktop.ini", 282, LibraryFixture.Utc(2026, 1, 1))
            .WithFile(PhotoRoot + @"\2026-05-25 Pano\PANO_0001.DNG", 13_751_808, LibraryFixture.Utc(2026, 5, 25, 13, 31))
            .WithFile(V + @"\Exports\Council edit.mp4", 500_000_000, LibraryFixture.Utc(2026, 7, 30, 12))
            .WithFile(V + @"\2026\DJI_20260801120000_0001_D.MP4", 1_000_000, LibraryFixture.Utc(2026, 8, 1, 16, 1, 30))
            .WithFile(Council + @"\DJI_20260725232655_0117_D.MP4", 1_234_567_890, LibraryFixture.Utc(2026, 7, 26, 3, 28, 25))
            .WithFile(Council + @"\DJI_20260726022937_0118_D.MP4", 2_000_000_000, LibraryFixture.Utc(2026, 7, 26, 6, 31, 7))
            .WithFile(Council + @"\desktop.ini", 100, LibraryFixture.Utc(2026, 7, 27))
            .Build();
        LibraryIndex lib = LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone);

        // only the YYYY-MM-DD folder is an event folder; Picture Offload, Exports, 2026, 2026-07 and the dated folder in the photo root are not
        LibraryFolder council = Assert.Single(lib.Folders);
        Assert.Equal(Council, council.Ref.FullPath);
        Assert.Equal(new DateOnly(2026, 7, 25), council.Ref.NameDate);
        Assert.Equal("Council Road", council.Ref.Description);
        Assert.Equal(new[] { LibraryFixture.Utc(2026, 7, 26, 3, 26, 55), LibraryFixture.Utc(2026, 7, 26, 6, 29, 37) }, council.MemberStartsUtc);
        Assert.Equal(3, lib.FilesIn(council.Ref).Length);       // incl. its desktop.ini, which is no member start

        // name+size matching works everywhere; loose files never get a (pseudo) event folder
        Assert.Null(Assert.Single(lib.Match(FileKey.Of("DJI_20260801120000_0001_D.MP4", 1_000_000))).EventFolder);
        Assert.Null(Assert.Single(lib.Match(FileKey.Of("Council edit.mp4", 500_000_000))).EventFolder);
        Assert.Null(Assert.Single(lib.Match(FileKey.Of("DJI_20260815200000_0119_D.DNG", 27_000_000))).EventFolder);
        Assert.Null(Assert.Single(lib.Match(FileKey.Of("PANO_0001.DNG", 13_751_808))).EventFolder);
        Assert.Equal(2, lib.Match(FileKey.Of("desktop.ini", 282)).Length);
        Assert.All(lib.Match(FileKey.Of("desktop.ini", 282)), f => Assert.Null(f.EventFolder));
        Assert.Equal(Council, Assert.Single(lib.Match(FileKey.Of("desktop.ini", 100))).EventFolder?.FullPath);
        Assert.Equal(Council, Assert.Single(lib.Match(FileKey.Of("DJI_20260725232655_0117_D.MP4", 1_234_567_890))).EventFolder?.FullPath);

        // decision 4: only event-folder videos count for the watermark; the Exports clip (mtime Jul 30) and the stray 0001 at 2026\
        // (Aug 1 16:00Z) are later but leave it at Council 0118 (02:29:37 EDT = 06:29:37Z)
        Assert.Equal(LibraryFixture.Utc(2026, 7, 26, 6, 29, 37), lib.WatermarkUtc);
        Assert.Contains(V + @"\Exports\Council edit.mp4", lib.StartsFromMtime);
        Assert.Empty(lib.UnavailableRoots);
    }

    [Fact]
    public void LibraryIndex_Watermark_IgnoresVideosOutsideEventFolders()
    {
        // decision 4 (user): only videos inside event folders count; an Exports\ clip and a loose 2026\x.MP4 with later mtimes
        // leave the watermark at Zachar Bay 0148 (stamp 14:24:16 EDT = 18:24:16Z)
        var listings = new LibraryFixture()
            .WithFile(Zrel + @"\DJI_20260927142416_0148_D.MP4", 77_000_000, LibraryFixture.Utc(2026, 9, 27, 18, 25, 46))
            .WithFile(V + @"\Exports\Zachar edit.mp4", 900_000_000, LibraryFixture.Utc(2026, 10, 1))
            .WithFile(V + @"\2026\x.MP4", 800_000_000, LibraryFixture.Utc(2026, 10, 2, 12))
            .Build();
        Assert.Equal(LibraryFixture.Utc(2026, 9, 27, 18, 24, 16), LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone).WatermarkUtc);

        // without an event-folder video there is no watermark at all
        var outsideOnly = new LibraryFixture()
            .WithFile(V + @"\Exports\Zachar edit.mp4", 900_000_000, LibraryFixture.Utc(2026, 10, 1))
            .WithFile(V + @"\2026\x.MP4", 800_000_000, LibraryFixture.Utc(2026, 10, 2, 12))
            .Build();
        Assert.Null(LibraryIndex.Build(outsideOnly, TestLedger.Empty(), EasternZone).WatermarkUtc);
    }

    [Fact]
    public void LibraryIndex_LedgerFolderIsNotIndexed()
    {
        var listings = new LibraryFixture()
            .WithFile(Zrel + @"\DJI_20260927142416_0148_D.MP4", 77_000_000, LibraryFixture.Utc(2026, 9, 27, 18, 25, 46))
            .WithFile(V + @"\.uas-sort\ledger-A.jsonl", 2_500, LibraryFixture.Utc(2026, 10, 1))
            .WithFile(V + @"\.uas-sort\ledger-A-LAPTOP-B.jsonl", 2_400, LibraryFixture.Utc(2026, 10, 1))
            .WithFile(V + @"\.uas-sort\DJI_20261001120000_0200_D.MP4", 5_000, LibraryFixture.Utc(2026, 10, 1, 16, 1, 30))
            .WithFile(V + @"\.uas-sort\DJI_20261001120500_0201_D.DNG", 6_000, LibraryFixture.Utc(2026, 10, 1, 16, 6))
            .WithFile(V + @"\.uas-sort\2026-10-01 Stray\DJI_20261001130000_0202_D.MP4", 7_000, LibraryFixture.Utc(2026, 10, 1, 17, 1))
            .Build();
        LibraryIndex lib = LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone);
        Assert.Empty(lib.Match(FileKey.Of("DJI_20261001120000_0200_D.MP4", 5_000)));
        Assert.Empty(lib.Match(FileKey.Of("DJI_20261001120500_0201_D.DNG", 6_000)));
        Assert.Empty(lib.Match(FileKey.Of("DJI_20261001130000_0202_D.MP4", 7_000)));
        Assert.Empty(lib.Match(FileKey.Of("ledger-A.jsonl", 2_500)));
        Assert.Empty(lib.SameNameOtherSize("dji_20261001120000_0200_d.mp4", 1));
        Assert.Equal(Zrel, Assert.Single(lib.Folders).Ref.FullPath);
        Assert.Empty(lib.SetFolder(".uas-sort"));
        Assert.Empty(lib.SetFolder("2026-10-01 Stray"));
        Assert.Equal(LibraryFixture.Utc(2026, 9, 27, 18, 24, 16), lib.WatermarkUtc);
        Assert.DoesNotContain(lib.Files, f => f.FullPath.Contains(".uas-sort", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LibraryIndex_AppCopiesNeverMoveTheWatermark()
    {
        var listings = new LibraryFixture()
            .WithFile(Zrel + @"\DJI_20260927140127_0123_D.MP4", 105_764_094, LibraryFixture.Utc(2026, 9, 27, 18, 2, 57))
            .WithFile(Zrel + @"\DJI_20260927140144_0124_D.MP4", 98_000_000, LibraryFixture.Utc(2026, 9, 27, 18, 3, 14))
            .WithFile(Zrel + @"\DJI_20260927142416_0148_D.MP4", 77_000_000, LibraryFixture.Utc(2026, 9, 27, 18, 25, 46))
            .WithFile(Zrel + @"\DJI_20260927160000_0160_D.MP4", 60_000_000, LibraryFixture.Utc(2026, 9, 27, 20, 1, 30))
            .Build();
        LedgerSnapshot ledger = TestLedger.Snapshot(
            FileRec("f160", "DJI_20260927160000_0160_D.MP4", 60_000_000, Zrel + @"\DJI_20260927160000_0160_D.MP4"));
        LibraryIndex lib = LibraryIndex.Build(listings, ledger, EasternZone);
        Assert.Equal(LibraryFixture.Utc(2026, 9, 27, 18, 24, 16), lib.WatermarkUtc);    // 10:24:16 AKDT, Zachar Bay 0148
        Assert.Equal(4, FolderAt(lib, Zrel).MemberStartsUtc.Length);                   // the app copy is still a member
    }

    [Fact]
    public void LibraryIndex_NoWatermark_WhenEveryVideoHasALedgerRecordOrThereAreNoVideos()
    {
        var withRecord = new LibraryFixture()
            .WithFile(Zrel + @"\DJI_20260927160000_0160_D.MP4", 60_000_000, LibraryFixture.Utc(2026, 9, 27, 20, 1, 30)).Build();
        LedgerSnapshot ledger = TestLedger.Snapshot(
            FileRec("f160", "DJI_20260927160000_0160_D.MP4", 60_000_000, Zrel + @"\DJI_20260927160000_0160_D.MP4"));
        Assert.Null(LibraryIndex.Build(withRecord, ledger, EasternZone).WatermarkUtc);

        var photosOnly = new LibraryFixture()
            .WithFile(PhotoRoot + @"\DJI_20260815200000_0119_D.DNG", 27_000_000, LibraryFixture.Utc(2026, 8, 16)).Build();
        Assert.Null(LibraryIndex.Build(photosOnly, TestLedger.Empty(), EasternZone).WatermarkUtc);
        Assert.Null(LibraryIndex.Build(new LibraryFixture().Build(), TestLedger.Empty(), EasternZone).WatermarkUtc);
    }

    [Fact]
    public void LibraryIndex_FolderZoneAndCentroid_FromTheLedger()
    {
        var listings = new LibraryFixture()
            .WithFile(Zrel + @"\DJI_20260927142416_0148_D.MP4", 77_000_000, LibraryFixture.Utc(2026, 9, 27, 18, 25, 46))
            .WithFile(Anvil + @"\DJI_20260726235645_0001_D.MP4", 100, LibraryFixture.Utc(2026, 7, 27, 3, 58, 15))
            .WithFile(Anvil + @"\DJI_20260727000012_0002_D.MP4", 200, LibraryFixture.Utc(2026, 7, 27, 4, 1, 42))
            .WithFile(Council + @"\DJI_20260725232655_0117_D.MP4", 1_234_567_890, LibraryFixture.Utc(2026, 7, 26, 3, 28, 25))
            .Build();
        LedgerSnapshot ledger = TestLedger.Snapshot(
            FolderRec("fo1", Zrel, "Zachar Bay", new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), "America/Anchorage", 57.5415, -153.7409),
            FileRec("a1", "DJI_20260726235645_0001_D.MP4", 100, Anvil + @"\DJI_20260726235645_0001_D.MP4", lat: 64.5627, lon: -165.3696),
            FileRec("a2", "DJI_20260727000012_0002_D.MP4", 200, Anvil + @"\DJI_20260727000012_0002_D.MP4", lat: 64.5627, lon: -165.3696));
        LibraryIndex lib = LibraryIndex.Build(listings, ledger, EasternZone);

        LibraryFolder z = FolderAt(lib, Zrel);
        Assert.Equal("America/Anchorage", z.TzId);
        Assert.Equal(new GeoPoint(57.5415, -153.7409), z.Centroid);
        Assert.Equal(LocationSource.Ledger, z.Loc);

        LibraryFolder a = FolderAt(lib, Anvil);
        Assert.Null(a.TzId);
        GeoPoint ac = Assert.NotNull(a.Centroid);
        Assert.Equal(64.5627, ac.Lat, 9);
        Assert.Equal(-165.3696, ac.Lon, 9);
        Assert.Equal(LocationSource.Ledger, a.Loc);

        LibraryFolder c = FolderAt(lib, Council);
        Assert.Null(c.Centroid);
        Assert.Equal(LocationSource.Unknown, c.Loc);
        Assert.Equal(3, lib.Folders.Length);
    }

    [Fact]
    public void LibraryIndex_SiteLocalMemberStarts_UseTheFolderLedgerZoneElseTheStoredZone()
    {
        const string other = V + @"\2026\2026-09\2026-09-28 Other";
        var listings = new LibraryFixture()
            .WithFile(Zrel + @"\DJI_20260927100127_0123_D.MP4", 105_764_094, LibraryFixture.Utc(2026, 9, 27, 18, 2, 57))
            .WithFile(other + @"\DJI_20260928100000_0001_D.MP4", 1, LibraryFixture.Utc(2026, 9, 28, 14, 1, 30))
            .Build();
        LedgerSnapshot ledger = TestLedger.Snapshot(
            FolderRec("fo1", Zrel, "Zachar Bay", new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), "America/Anchorage"));
        LibraryIndex lib = LibraryIndex.Build(listings, ledger, SiteLocalClock);
        Assert.Equal(new[] { LibraryFixture.Utc(2026, 9, 27, 18, 1, 27) }, FolderAt(lib, Zrel).MemberStartsUtc);
        Assert.Equal(new[] { LibraryFixture.Utc(2026, 9, 28, 14, 0, 0) }, FolderAt(lib, other).MemberStartsUtc);
        Assert.Empty(lib.StartsFromMtime);
    }

    [Fact]
    public void LibraryIndex_PhotoRootMovedToD_OldRootsStillMatch()
    {
        var listings = new LibraryFixture()
            .WithPhotoRoot(@"D:\Photos")
            .WithPrevious(PhotoRoot)                  // the old Picture Offload inside the video root
            .WithPrevious(@"D:\Old Offload")
            .WithFile(PhotoRoot + @"\DJI_20260815200000_0119_D.DNG", 27_000_000, LibraryFixture.Utc(2026, 8, 16))
            .WithFile(PhotoRoot + @"\2026-05-25 Pano\PANO_0001.DNG", 13_751_808, LibraryFixture.Utc(2026, 5, 25))
            .WithFile(@"D:\Old Offload\DJI_20250101120000_0001_D.DNG", 25_000_000, LibraryFixture.Utc(2025, 1, 1, 17))
            .WithFile(@"D:\Photos\DJI_20260901120000_0002_D.DNG", 26_000_000, LibraryFixture.Utc(2026, 9, 1, 16))
            .Build();
        LibraryIndex lib = LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone);
        Assert.Single(lib.Match(FileKey.Of("DJI_20260815200000_0119_D.DNG", 27_000_000)));
        Assert.Single(lib.Match(FileKey.Of("PANO_0001.DNG", 13_751_808)));
        Assert.Single(lib.Match(FileKey.Of("DJI_20250101120000_0001_D.DNG", 25_000_000)));
        Assert.Single(lib.Match(FileKey.Of("DJI_20260901120000_0002_D.DNG", 26_000_000)));
        Assert.Empty(lib.Folders);                   // a dated folder in a previous photo root is not an event folder
    }

    [Fact]
    public void LibraryIndex_SetFolders_InAnyListedRoot()
    {
        var listings = new LibraryFixture()
            .WithPrevious(@"D:\Old Offload")
            .WithFile(PhotoRoot + @"\001_0087\PANO_0002.DNG", 12_882_432, LibraryFixture.Utc(2026, 5, 25, 13, 30, 30))
            .WithFile(PhotoRoot + @"\001_0087\PANO_0001.DNG", 13_751_808, LibraryFixture.Utc(2026, 5, 25, 13, 30, 28))
            .WithDir(PhotoRoot + @"\001_0112")
            .WithFile(@"D:\Old Offload\001_0087\PANO_0001.DNG", 1, LibraryFixture.Utc(2025, 1, 1))
            .Build();
        LibraryIndex lib = LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone);

        var sets = lib.SetFolder("001_0087");
        Assert.Equal(2, sets.Length);
        SetFolderListing inPhotoRoot = sets.Single(s => s.FullPath.StartsWith(PhotoRoot, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("001_0087", inPhotoRoot.Name);
        Assert.Equal(new[] { ("PANO_0001.DNG", 13_751_808L, LibraryFixture.Utc(2026, 5, 25, 13, 30, 28)),
                             ("PANO_0002.DNG", 12_882_432L, LibraryFixture.Utc(2026, 5, 25, 13, 30, 30)) },
                     inPhotoRoot.Members.Select(m => (m.Member, m.Size, m.MtimeUtc)));
        Assert.Empty(Assert.Single(lib.SetFolder("001_0112")).Members);        // an empty folder is listed; Part 06 decides it doesn't count
        Assert.Empty(lib.SetFolder("001_9999"));
    }

    [Fact]
    public void LibraryIndex_CopySuffixMatchesAndConflictEvidence()
    {
        var listings = new LibraryFixture()
            .WithFile(Zrel + @"\DJI_20260927140127_0123_D (2).MP4", 105_764_094, LibraryFixture.Utc(2026, 9, 27, 18, 2, 57))
            .WithFile(Zrel + @"\best shot.MP4", 555_555_555, LibraryFixture.Utc(2026, 9, 27, 19))
            .Build();
        LibraryIndex lib = LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone);
        Assert.Single(lib.Match(FileKey.Of("DJI_20260927140127_0123_D.MP4", 105_764_094)));
        LibraryFile other = Assert.Single(lib.SameNameOtherSize("dji_20260927140127_0123_d.mp4", 7_340_032));
        Assert.Equal(105_764_094, other.Key.Size);
        Assert.Empty(lib.SameNameOtherSize("dji_20260927140127_0123_d.mp4", 105_764_094));
        Assert.Empty(lib.Match(FileKey.Of("DJI_20260927140130_0130_D.MP4", 555_555_555)));   // same size, other name: no match
    }

    [Fact]
    public void LibraryIndex_LegacyDepthAutelMembers_StartFromMtime()
    {
        const string makaha = V + @"\2022\2022-03-27 Makaha Valley";
        var listings = new LibraryFixture()
            .WithFile(makaha + @"\MAX_0061.MP4", 61_000_000, LibraryFixture.Utc(2022, 3, 27, 15, 1))
            .WithFile(makaha + @"\MAX_0062.MP4", 62_000_000, LibraryFixture.Utc(2022, 3, 27, 15, 2))
            .WithFile(makaha + @"\MAX_0064.MP4", 64_000_000, LibraryFixture.Utc(2022, 3, 27, 15, 4))
            .Build();
        LibraryIndex lib = LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone);
        LibraryFolder f = Assert.Single(lib.Folders);
        Assert.Equal((new DateOnly(2022, 3, 27), "Makaha Valley"), (f.Ref.NameDate, f.Ref.Description));
        Assert.Equal(new[] { LibraryFixture.Utc(2022, 3, 27, 15, 1), LibraryFixture.Utc(2022, 3, 27, 15, 2), LibraryFixture.Utc(2022, 3, 27, 15, 4) },
                     f.MemberStartsUtc);
        Assert.Equal(3, lib.StartsFromMtime.Count);
    }

    [Fact]
    public void LibraryIndex_UnavailableRootsAndListingErrors_AreCarried()
    {
        var listings = new LibraryFixture()
            .WithPrevious(@"E:\Gone").WithUnavailable(@"E:\Gone")
            .WithDir(V + @"\2026\locked").WithError(V + @"\2026\locked", 5)
            .Build();
        LibraryIndex lib = LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone);
        Assert.Equal(@"E:\Gone", Assert.Single(lib.UnavailableRoots));
        Assert.Equal((V + @"\2026\locked", 5), Assert.Single(lib.ListingErrors));
    }
}
