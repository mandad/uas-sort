// tests/UasSort.Core.Tests/Card/CardDetectorTests.cs
namespace UasSort.Core.Tests.Card;

public class CardDetectorTests
{
    private static readonly DateTime T = new(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc);

    private static VolumeInfo Vol(string root, uint serial, string driveType = "Removable", bool ready = true,
                                  string fs = "exFAT", string? label = null)
        => new(root, new CardIdentity(serial, label, fs, 256_060_514_304), driveType, ready, false, fs == "NTFS",
               driveType == "Removable", 12_400_000_000, "Sd", true, false);

    private sealed class CountingLister(IDirectoryLister inner) : IDirectoryLister
    {
        public List<string> Roots { get; } = [];
        public ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames)
        {
            Roots.Add(root);
            return inner.Enumerate(root, recurse, excludeDirNames);
        }
    }

    private static void AddDjiCard(FakeFileSystem fs, string root)
    {
        fs.AddFile(root + @"DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", 100, T);
        fs.AddFile(root + @"DCIM\DJI_001\DJI_20260927140627_0128_D.LRF", 10, T);
        fs.AddFile(root + @"DCIM\DJI_001\DJI_20260927141000_0129_D.DNG", 50, T);
        fs.AddFile(root + @"MISC\FC9113.db", 4, T);
    }

    [Fact]
    public void Detect_FlagsDjiCardsAndListsOtherVolumesAsNotACard()
    {
        var fs = FakeLayout.NewFileSystem();
        AddDjiCard(fs, @"E:\");
        fs.AddFile(@"F:\DCIM\100MEDIA\MAX_0061.MP4", 100, T);                                    // Autel
        fs.AddFile(@"G:\Android\data\dji.go.v5\files\MediaCaches\x.jpg", 10, T);                 // RC 2 storage
        fs.AddFile(@"K:\DCIM\PANORAMA\001_0087\PANO_0001.DNG", 10, T);                          // no DJI-named file
        AddDjiCard(fs, @"D:\");                                                                  // Fixed exFAT copy: DriveType is not trusted
        var volumes = new[] { Vol(@"E:\", 1), Vol(@"F:\", 2), Vol(@"G:\", 3), Vol(@"K:\", 4), Vol(@"D:\", 5, "Fixed") };

        var found = CardDetector.Detect(volumes, fs, FakeLayout.Settings());

        string[] roots = [@"D:\", @"E:\", @"F:\", @"G:\", @"K:\"];
        Assert.Equal(roots, found.Select(c => c.Volume.Root));
        var e = found.Single(c => c.Volume.Root == @"E:\");
        Assert.True(e.IsDjiCard);
        Assert.Null(e.NotCardReason);
        Assert.Equal(2, e.MediaCount);
        Assert.True(found.Single(c => c.Volume.Root == @"D:\").IsDjiCard);
        foreach (var root in new[] { @"F:\", @"G:\", @"K:\" })
        {
            var c = found.Single(x => x.Volume.Root == root);
            Assert.False(c.IsDjiCard);
            Assert.Equal(CardDetector.NotACard, c.NotCardReason);
        }
    }

    [Fact]
    public void Detect_SkipsUnreadyNetworkOpticalRootlessAndRootHoldingVolumes_WithoutListingThem()
    {
        var fs = FakeLayout.NewFileSystem();
        foreach (var r in new[] { @"H:\", @"I:\", @"M:\", @"N:\", @"O:\" }) AddDjiCard(fs, r);
        AddDjiCard(fs, @"C:\");
        var lister = new CountingLister(fs);
        var volumes = new[]
        {
            Vol(@"H:\", 1, "Network"), Vol(@"I:\", 2, ready: false), Vol(@"M:\", 3, "CDRom"), Vol(@"N:\", 4, "NoRootDirectory"),
            Vol(@"C:\", 5, "Fixed", fs: "NTFS"),   // holds the video root
            Vol(@"O:\", 6, "Fixed"),               // holds a previous photo root
        };
        var settings = FakeLayout.Settings(previousPhotoRoots: [@"O:\Old Photos"]);

        Assert.Empty(CardDetector.Detect(volumes, lister, settings));
        Assert.Empty(lister.Roots);
    }

    [Fact] // [Review Focus] item 5
    public void DroneOverUsb_TwoDjiVolumes_AreBothListed_AndNeverAutoPicked()
    {
        var fs = FakeLayout.NewFileSystem();
        AddDjiCard(fs, @"J:\");   // aircraft internal storage
        AddDjiCard(fs, @"L:\");   // the SD card in the aircraft
        var volumes = new[] { Vol(@"L:\", 0x5D000002, label: "SD_card"), Vol(@"J:\", 0x1A000001, label: "InternalStorage") };

        var found = CardDetector.Detect(volumes, fs, FakeLayout.Settings());

        Assert.Equal(2, found.Count);
        Assert.All(found, c => Assert.True(c.IsDjiCard));
        Assert.Null(CardDetector.SingleDjiCard(found));
        var keys = found.Select(c => new CardSource(c.Volume.Root, c.Volume.Identity, false, false).DraftKey).ToList();
        string[] expectedKeys = ["vol-1A000001", "vol-5D000002"];
        Assert.Equal(expectedKeys, keys);
    }

    [Fact]
    public void SingleDjiCard_PicksTheOnlyDjiCard_IgnoringNonCards()
    {
        var fs = FakeLayout.NewFileSystem();
        AddDjiCard(fs, @"E:\");
        fs.AddFile(@"F:\DCIM\100MEDIA\MAX_0061.MP4", 100, T);
        var found = CardDetector.Detect([Vol(@"E:\", 1), Vol(@"F:\", 2)], fs, FakeLayout.Settings());
        Assert.Equal(@"E:\", CardDetector.SingleDjiCard(found)!.Volume.Root);
        Assert.Null(CardDetector.SingleDjiCard([]));
    }
}
