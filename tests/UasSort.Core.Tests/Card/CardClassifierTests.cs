// tests/UasSort.Core.Tests/Card/CardClassifierTests.cs
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace UasSort.Core.Tests.Card;

public class CardClassifierTests
{
    private static readonly DateTime T = new(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 20, 0, 0, TimeSpan.Zero);
    private static readonly CardSource Source = new(@"E:\", FakeLayout.CardId, false, false);

    private static readonly (string Rel, EntryClass Class, string? Rule)[] Rows =
    [
        ("DCIM/DJI_001/DJI_20260927140627_0128_D.MP4", EntryClass.Video, null),
        ("DCIM/DJI_001/DJI_20260927140627_0128_D.LRF", EntryClass.Skip, "proxy"),
        ("DCIM/DJI_001/DJI_20260927140627_0128_D.SRT", EntryClass.Skip, "captions"),
        ("DCIM/DJI_001/DJI_20260927141000_0129_D.DNG", EntryClass.Photo, null),
        ("DCIM/DJI_001/DJI_20260927141000_0129_D.JPG", EntryClass.PhotoTwin, null),
        ("DCIM/DJI_001/DJI_20260927141100_0130_D.JPG", EntryClass.Photo, null),
        ("DCIM/DJI_001/DJI_20260927140700_0131_D.MP4", EntryClass.Video, null),
        ("DCIM/DJI_001/DJI_20260927140700_0131_D.JPG", EntryClass.Unknown, "possible video cover"),
        ("DCIM/DJI_001/DJI_20260727002013_0014_D.MP4", EntryClass.Video, null),
        ("DCIM/DJI_001/.DJI_20260727002013_0014_D.MP4.trinf", EntryClass.Skip, "system/recovery"),
        ("DCIM/DJI_001/.DJI_20260727002013_0014_D.avc1", EntryClass.Skip, "system/recovery"),
        ("DCIM/DJI_001/._DJI_20260927140627_0128_D.MP4", EntryClass.Skip, "system/recovery"),
        ("DCIM/DJI_001/.foo.MP4", EntryClass.Unknown, null),
        ("DCIM/DJI_001/DJI_20260927150000_0140_D.HEIC", EntryClass.Unknown, null),
        ("DCIM/DJI_002_A01/DJI_20260928100000_0001_D.MP4", EntryClass.Video, null),
        ("DCIM/DJI_A001/x.MP4", EntryClass.Unknown, null),
        ("DCIM/DJI_001/nested/DJI_20260927140000_0001_D.MP4", EntryClass.Unknown, null),
        ("DCIM/PANORAMA/001_0087/PANO_0001.DNG", EntryClass.SetMember, null),
        ("DCIM/PANORAMA/001_0087/PANO_0002.DNG", EntryClass.SetMember, null),
        ("DCIM/PANORAMA/001_0087/notes.txt", EntryClass.Unknown, null),
        ("DCIM/HYPERLAPSE/001_0090/HYPERLAPSE_0001.JPG", EntryClass.SetMember, null),
        ("x.MP4", EntryClass.Unknown, null),
        ("readme.txt", EntryClass.Skip, "outside DCIM, not media"),
        ("MISC/FC9113.db", EntryClass.Skip, "DJI: no backup needed"),
        ("MISC/THM/DJI_0001.JPG", EntryClass.Skip, "DJI: no backup needed"),
        ("LOST.DIR/1", EntryClass.Skip, "DJI: no backup needed"),
        ("System Volume Information/IndexerVolumeGuid", EntryClass.Skip, "system"),
        (".Trashes/501/DJI_20260927140627_0128_D.MP4", EntryClass.Skip, "system/recovery"),
    ];

    public static TheoryData<string, EntryClass, string?> Expectations
    {
        get
        {
            var data = new TheoryData<string, EntryClass, string?>();
            foreach (var (rel, cls, rule) in Rows) data.Add(rel, cls, rule);
            return data;
        }
    }

    private static CardInventory Classify(Action<FakeFileSystem>? more = null)
    {
        var fs = FakeLayout.NewFileSystem();
        foreach (var row in Rows) fs.AddFile(@"E:\" + row.Rel, 1_000, T);
        more?.Invoke(fs);
        var listing = fs.Enumerate(@"E:\", recurse: true, ImmutableHashSet<string>.Empty);
        return new CardClassifier(new FakeTimeProvider(Now)).Classify(Source, listing);
    }

    [Theory]
    [MemberData(nameof(Expectations))]
    public void EveryFile_GetsItsFailSafeClass(string relPath, EntryClass expected, string? rule)
    {
        var e = Assert.Single(Classify().Entries, x => x.RelPath == relPath);
        Assert.Equal(expected, e.Class);
        Assert.Equal(rule, e.Rule);
    }

    [Fact]
    public void OnlyFilesBecomeEntries()
    {
        var inv = Classify();
        Assert.Equal(Rows.Length, inv.Entries.Length);
        Assert.DoesNotContain(inv.Entries, e => e.RelPath is "DCIM" or "DCIM/DJI_001");
    }

    [Fact]
    public void Units_ArePairsSetsAndVideosWithTrinfFlags()
    {
        var inv = Classify();
        var videos = inv.Units.OfType<VideoUnit>().ToDictionary(v => v.Id.CardRelPath);
        Assert.Equal(4, videos.Count);
        Assert.True(videos["DCIM/DJI_001/DJI_20260727002013_0014_D.MP4"].HasTrinf);
        Assert.False(videos["DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"].HasTrinf);

        var photos = inv.Units.OfType<PhotoUnit>().ToDictionary(p => p.Id.CardRelPath);
        Assert.Equal(2, photos.Count);
        Assert.Equal("DCIM/DJI_001/DJI_20260927141000_0129_D.JPG", photos["DCIM/DJI_001/DJI_20260927141000_0129_D.DNG"].JpgTwin!.RelPath);
        Assert.Null(photos["DCIM/DJI_001/DJI_20260927141100_0130_D.JPG"].JpgTwin);

        var sets = inv.Units.OfType<SetUnit>().ToDictionary(s => s.Id.CardRelPath);
        Assert.Equal(2, sets.Count);
        var pano = sets["DCIM/PANORAMA/001_0087"];
        Assert.Equal(SetKind.Panorama, pano.Kind);
        Assert.Equal("001_0087", pano.SetName);
        string[] members = ["DCIM/PANORAMA/001_0087/PANO_0001.DNG", "DCIM/PANORAMA/001_0087/PANO_0002.DNG"];
        Assert.Equal(members, pano.Members.Select(m => m.RelPath));
        Assert.Equal(SetKind.Hyperlapse, sets["DCIM/HYPERLAPSE/001_0090"].Kind);
        Assert.Equal(inv.Units.Select(u => u.Id.CardRelPath).Order(StringComparer.Ordinal), inv.Units.Select(u => u.Id.CardRelPath));
    }

    [Fact]
    public void CameraModel_ComesFromTheMiscDatabase_AndListedUtcFromTheClock()
    {
        var inv = Classify();
        Assert.Equal("FC9113", inv.CameraModel);
        Assert.Equal(Now.UtcDateTime, inv.ListedUtc);
        Assert.Same(Source, inv.Source);
    }

    [Fact]
    public void EnumerationErrors_BecomeForcesNotSafeWarnings()
    {
        var inv = Classify(fs =>
        {
            fs.AddFile(@"E:\DCIM\DJI_003\DJI_20260929100000_0300_D.MP4", 10, T);
            fs.Faults.EnumerationErrors[@"E:\DCIM\DJI_003"] = 5;
        });
        var w = Assert.Single(inv.Warnings);
        Assert.Equal("EnumerationError", w.Code);
        Assert.Equal("DCIM/DJI_003", w.RelPath);
        Assert.True(w.ForcesNotSafe);
    }

    [Fact]
    public void InventoryHash_SortsByLowercasePath_AndHashesSizeAndMtimeTicks()
    {
        var t1 = new DateTime(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc);
        var a = new CardEntry("DCIM/a.MP4", 1, t1, t1, t1, 0x20, EntryClass.Unknown, null);
        var b = new CardEntry("DCIM/B.MP4", 2, t1, t1, t1, 0x20, EntryClass.Unknown, null);
        var text = string.Create(CultureInfo.InvariantCulture, $"DCIM/a.MP4|1|{t1.Ticks}\nDCIM/B.MP4|2|{t1.Ticks}\n");
        var expected = XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(text)).ToString("x16", CultureInfo.InvariantCulture);
        Assert.Equal(expected, CardClassifier.ComputeInventoryHash([b, a]));
        Assert.NotEqual(expected, CardClassifier.ComputeInventoryHash([a, b with { Size = 3 }]));
        Assert.Matches("^[0-9a-f]{16}$", Classify().InventoryHash);
        Assert.Equal(Classify().InventoryHash, Classify().InventoryHash);
    }

    [Fact]
    public void ACardWithMediaOnlyInUnrecognisedLocations_HasNoUnitsAndOnlyUnknownMedia()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(@"E:\DCIM\DJI_A001\x.MP4", 10, T);
        fs.AddFile(@"E:\Videos\DJI_20260927140627_0128_D.MP4", 10, T);
        var inv = new CardClassifier(new FakeTimeProvider(Now)).Classify(Source, fs.Enumerate(@"E:\", true, ImmutableHashSet<string>.Empty));
        Assert.Empty(inv.Units);
        Assert.All(inv.Entries, e => Assert.Equal(EntryClass.Unknown, e.Class));
    }
}
