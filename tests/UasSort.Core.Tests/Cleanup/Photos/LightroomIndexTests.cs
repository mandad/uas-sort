// tests/UasSort.Core.Tests/Cleanup/Photos/LightroomIndexTests.cs
namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class LightroomIndexTests
{
    private const string Lr = @"X:\Lightroom";
    private static readonly DateTime Shot = new(2026, 6, 1, 12, 10, 0);
    private static readonly DateTime ImportedAt = new(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);

    private sealed class CountingLister(IDirectoryLister inner) : IDirectoryLister
    {
        public List<string> Listed { get; } = [];

        public ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames)
        {
            Listed.Add(PathRules.Normalize(root));
            return inner.Enumerate(root, recurse, excludeDirNames);
        }
    }

    private sealed class Rig
    {
        public Rig()
        {
            Lister = new CountingLister(Fs);
            Reader = new FakePhotoFileReader(Fs, FakeLayout.Context(cardRoot: null) with { LightroomFolder = Lr });
            Exif = new PhotoExifCache(Reader);
            Fs.AddDirectory(Lr);
        }

        public FakeFileSystem Fs { get; } = FakeLayout.NewFileSystem();
        public CountingLister Lister { get; }
        public FakePhotoFileReader Reader { get; }
        public PhotoExifCache Exif { get; }

        public void Dng(string rel, DateTime dto, string? sub = null, DateTime? mtime = null, uint attributes = 0x20)
            => Fs.AddFile(Lr + @"\" + rel, new SyntheticDngBuilder { SubSecTimeOriginal = sub }.WithDateTimeOriginal(dto).Build(), mtime ?? ImportedAt,
                          attributes);

        public LightroomIndex Build()
            => LightroomIndex.Build(Lr, Lister, Exif, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, CancellationToken.None);
    }

    [Fact]
    public void Build_IndexesShotsInTheRange_BySecondAndModel()
    {
        var rig = new Rig();
        rig.Dng(@"2026\2026-06-01\Damian_20260601_001.dng", Shot, "045");
        rig.Dng(@"2026\2026-06-01\Damian_20260601_002.dng", Shot.AddSeconds(5));
        rig.Dng(@"2026\2026-06-01\Damian_20260601_003.jpg", Shot.AddSeconds(9));
        rig.Dng(@"2026\2026-08-10\Damian_20260810_001.dng", new DateTime(2026, 8, 10, 9, 0, 0));

        var index = rig.Build();

        Assert.Equal(3, index.Count);
        var hit = Assert.Single(index.SameSecond(new ExifStamp(Shot, null, "fc9113")));
        Assert.Equal(("045", Lr + @"\2026\2026-06-01\Damian_20260601_001.dng", true), (hit.Stamp.SubSec, hit.FullPath, hit.IsDng));
        Assert.False(Assert.Single(index.SameSecond(new ExifStamp(Shot.AddSeconds(9), null, "FC9113"))).IsDng);   // kept for stitched panoramas only
        Assert.DoesNotContain(rig.Lister.Listed, p => p.EndsWith("2026-08-10", StringComparison.Ordinal));
    }

    [Fact] // [Review Focus 1]
    public void Build_ACloudOnlyLibraryFile_IsNeverOpened()
    {
        var rig = new Rig();
        rig.Dng(@"2026\2026-06-01\Damian_20260601_001.dng", Shot, attributes: FakeFileSystem.CloudOnlyPlaceholder);
        var index = rig.Build();
        Assert.Equal((0, 0), (index.Count, index.Opened));
        Assert.Empty(rig.Reader.Opened);
        Assert.Empty(rig.Fs.HydrationViolations);
        Assert.Equal([Lr + @"\2026\2026-06-01\Damian_20260601_001.dng"], index.Unreadable);
    }

    [Fact]
    public void Build_NeverListsIntoOrOpensTheCatalog()
    {
        var rig = new Rig();
        rig.Dng(@"2026\2026-06-01\Damian_20260601_001.dng", Shot);
        rig.Dng(@"LR_Catalog\Lightroom Catalog.lrcat", Shot);
        rig.Dng(@"Lightroom Catalog Previews.lrdata\0\A\x.dng", Shot);
        rig.Dng(@"Lightroom Catalog Smart Previews.lrdata\x.dng", Shot);
        rig.Dng(@"Lightroom Catalog.lrcat-data\x.dng", Shot);
        rig.Dng("Lightroom Catalog.lrcat", Shot);

        var index = rig.Build();

        Assert.Equal(1, index.Count);
        Assert.DoesNotContain(rig.Lister.Listed, LightroomRules.IsCatalogPath);
        Assert.DoesNotContain(rig.Reader.Opened, LightroomRules.IsCatalogPath);
        Assert.DoesNotContain(rig.Fs.GuardLog, g => LightroomRules.IsCatalogPath(g.Path));
    }

    [Fact]
    public void Build_SkipsFilesWrittenBeforeTheRange_WithoutOpeningThem()
    {
        var rig = new Rig();
        rig.Dng(@"Old\x.dng", Shot, mtime: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(0, rig.Build().Count);
        Assert.Empty(rig.Reader.Opened);
    }

    [Theory]
    [InlineData("2026", false)]
    [InlineData("2025", true)]
    [InlineData("2026-06", false)]
    [InlineData("2026-05", false)]
    [InlineData("2026-08", true)]
    [InlineData("2026-06-01 Juneau", false)]
    [InlineData("2026-07-05", true)]
    [InlineData("Imports", false)]
    public void FolderOutside_PrunesDatedFoldersOnly(string name, bool outside)
        => Assert.Equal(outside, LightroomIndex.FolderOutside(name, new DateOnly(2026, 5, 31), new DateOnly(2026, 7, 1)));
}
