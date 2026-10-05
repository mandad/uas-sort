// tests/UasSort.Core.Tests/Cleanup/Photos/LightroomIndexTests.cs
namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class LightroomIndexTests
{
    private const string Lr = @"X:\Lightroom";
    private const string PhotoRoot = FakeLayout.PhotoRoot;
    private static readonly DateTime Shot = new(2026, 6, 1, 12, 10, 0);
    private static readonly DateTime ImportedAt = new(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Protected = [FakeLayout.PhotoRoot, FakeLayout.VideoRoot, FakeLayout.AppDataDir];
    private const uint LinkFolder = 0x410;              // FILE_ATTRIBUTE_DIRECTORY | REPARSE_POINT: a junction, mount point or symlink
    private const uint LinkFile = 0x420;                // FILE_ATTRIBUTE_ARCHIVE | REPARSE_POINT: a file symlink
    private const uint CloudFolder = 0x400410;          // + RECALL_ON_DATA_ACCESS: a cloud placeholder folder (entered)

    private sealed class CountingLister(IDirectoryLister inner) : IDirectoryLister
    {
        public List<string> Listed { get; } = [];

        public ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames)
        {
            Listed.Add(PathRules.Normalize(root));
            return inner.Enumerate(root, recurse, excludeDirNames);
        }
    }

    /// <summary>A lister that, like the OS following a link, lists <paramref name="target"/>'s entries (with their own paths) when asked
    /// for <paramref name="mirror"/>: the walk must never index them even when the listing doesn't reveal the link.</summary>
    private sealed class ResolvingLister(FakeFileSystem fs, string mirror, string target) : IDirectoryLister
    {
        public ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames)
            => fs.Enumerate(PathRules.Equal(root, mirror) ? target : root, recurse, excludeDirNames);
    }

    /// <summary>A link cycle the listing doesn't reveal: every folder holds a plain sub-folder "loop".</summary>
    private sealed class CycleLister : IDirectoryLister
    {
        public ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames)
        {
            var path = PathRules.Join(root, "loop");
            if (path.Length > 4000) throw new InvalidOperationException("the walk didn't stop");
            return new ListingResult([new FsEntry(path, "loop", true, 0, ImportedAt, ImportedAt, ImportedAt, 0x10)], []);
        }
    }

    private sealed class Rig
    {
        public Rig(Func<FakeFileSystem, IDirectoryLister>? lister = null)
        {
            Lister = new CountingLister(lister?.Invoke(Fs) ?? Fs);
            Reader = new FakePhotoFileReader(Fs, FakeLayout.Context(cardRoot: null) with { LightroomFolder = Lr });
            Exif = new PhotoExifCache(Reader);
            Fs.AddDirectory(Lr);
        }

        public FakeFileSystem Fs { get; } = FakeLayout.NewFileSystem();
        public CountingLister Lister { get; }
        public FakePhotoFileReader Reader { get; set; }
        public PhotoExifCache Exif { get; set; }

        public void Dng(string rel, DateTime dto, string? sub = null, DateTime? mtime = null, uint attributes = 0x20)
            => Fs.AddFile(Lr + @"\" + rel, new SyntheticDngBuilder { SubSecTimeOriginal = sub }.WithDateTimeOriginal(dto).Build(), mtime ?? ImportedAt,
                          attributes);

        public LightroomIndex Build(string folder = Lr)
            => LightroomIndex.Build(folder, Lister, Exif, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), Protected, null, CancellationToken.None);
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

    [Fact] // Task PCfix 1: a junction, mount point or symlink under the library is listed but never entered or read through
    public void Build_NeverFollowsALink_SoAPhotoItPointsAtStaysUnverified()
    {
        var rig = new Rig();
        rig.Fs.AddDirectory(Lr + @"\Linked", LinkFolder);                    // → Picture Offload: the OS would show its files here
        rig.Dng(@"Linked\DJI_0001.DNG", Shot);
        rig.Dng(@"2026\2026-06-01\link.dng", Shot.AddSeconds(5), attributes: LinkFile);
        rig.Dng(@"2026\2026-06-01\Damian_20260601_001.dng", Shot.AddSeconds(40));

        var index = rig.Build();

        Assert.Equal(1, index.Count);
        Assert.Empty(index.SameSecond(new ExifStamp(Shot, null, "FC9113")));
        Assert.Empty(index.SameSecond(new ExifStamp(Shot.AddSeconds(5), null, "FC9113")));
        Assert.DoesNotContain(rig.Lister.Listed, p => p.EndsWith(@"\Linked", StringComparison.OrdinalIgnoreCase));
        Assert.Equal([Lr + @"\2026\2026-06-01\Damian_20260601_001.dng"], rig.Reader.Opened);
    }

    [Fact]
    public void Build_EntersACloudPlaceholderFolder()
    {
        var rig = new Rig();
        rig.Fs.AddDirectory(Lr + @"\2026", CloudFolder);
        rig.Dng(@"2026\2026-06-01\Damian_20260601_001.dng", Shot);
        Assert.Equal(1, rig.Build().Count);
    }

    [Fact] // Task PCfix 1, defence in depth: a file under the photo root, the video root or app data is never indexed as a Lightroom file
    public void Build_NeverIndexesAFileUnderAProtectedRoot_EvenWhenTheListingFollowsALink()
    {
        var rig = new Rig(fs => new ResolvingLister(fs, Lr + @"\Mirror", PhotoRoot));
        rig.Fs.AddDirectory(Lr + @"\Mirror");
        rig.Fs.AddFile(PhotoRoot + @"\DJI_0001.DNG", new SyntheticDngBuilder().WithDateTimeOriginal(Shot).Build(), ImportedAt);
        rig.Fs.AddFile(PhotoRoot + @"\001_0042\DJI_0002.DNG", new SyntheticDngBuilder().WithDateTimeOriginal(Shot).Build(), ImportedAt);

        var index = rig.Build();

        Assert.Equal(0, index.Count);
        Assert.Empty(rig.Reader.Opened);
        Assert.DoesNotContain(rig.Lister.Listed, p => PathRules.IsStrictlyUnder(p, PhotoRoot));
    }

    [Fact] // a Lightroom folder that contains the photo root (or the video root) never indexes their photos
    public void Build_ALibraryAroundThePhotoRoot_SkipsTheProtectedFolders()
    {
        const string pictures = @"C:\Users\u\OneDrive\Pictures";
        var rig = new Rig();
        rig.Fs.AddFile(PhotoRoot + @"\DJI_0001.DNG", new SyntheticDngBuilder().WithDateTimeOriginal(Shot).Build(), ImportedAt);
        rig.Fs.AddFile(pictures + @"\Lightroom\2026-06-01\Damian_20260601_001.dng",
                       new SyntheticDngBuilder().WithDateTimeOriginal(Shot.AddSeconds(40)).Build(), ImportedAt);
        rig.Reader = new FakePhotoFileReader(rig.Fs, FakeLayout.Context(cardRoot: null) with { LightroomFolder = pictures });
        rig.Exif = new PhotoExifCache(rig.Reader);

        var index = rig.Build(pictures);

        Assert.Equal(1, index.Count);
        Assert.Empty(index.SameSecond(new ExifStamp(Shot, null, "FC9113")));
        Assert.DoesNotContain(rig.Lister.Listed, p => PathRules.IsSameOrUnder(p, FakeLayout.VideoRoot));
    }

    [Fact] // Task PCfix 1: a cycle the listing doesn't reveal ends at the depth limit with a listing error, never a hang
    public void Build_ACycle_StopsAtTheDepthLimit_WithAnError()
    {
        var rig = new Rig(_ => new CycleLister());
        var index = rig.Build();
        Assert.Equal(LightroomIndex.MaxDepth + 1, rig.Lister.Listed.Count);
        var (path, code) = Assert.Single(index.Errors);
        Assert.Equal((LightroomIndex.TooDeepError, LightroomIndex.MaxDepth + 1), (code, path.Split(@"\loop").Length - 1));
        Assert.Equal(0, index.Count);
    }

    [Theory] // Task PCfix 2: a corrupt library file is skipped and counted, never an exception that aborts the index
    [InlineData(nameof(ArgumentException))]
    [InlineData(nameof(ArgumentOutOfRangeException))]
    [InlineData(nameof(IndexOutOfRangeException))]
    [InlineData(nameof(OverflowException))]
    [InlineData(nameof(NotSupportedException))]
    [InlineData(nameof(InvalidOperationException))]
    [InlineData(nameof(EndOfStreamException))]
    [InlineData(nameof(NullReferenceException))]
    public void Build_ACorruptLibraryFile_IsSkippedAndCounted(string exception)
    {
        var rig = new Rig();
        var corrupt = new CorruptPhotoReader(rig.Reader);
        rig.Exif = new PhotoExifCache(corrupt);
        rig.Dng(@"2026\2026-06-01\Damian_20260601_001.dng", Shot);
        rig.Dng(@"2026\2026-06-01\Damian_20260601_002.dng", Shot.AddSeconds(5));
        corrupt.Corrupt[Lr + @"\2026\2026-06-01\Damian_20260601_002.dng"] = CorruptPhotoReader.Make(exception);

        var index = rig.Build();

        Assert.Equal(1, index.Count);
        Assert.Equal([Lr + @"\2026\2026-06-01\Damian_20260601_002.dng"], index.Unreadable);
    }

    [Fact]
    public void Build_ACancelDuringARead_StillCancels()
    {
        var rig = new Rig();
        var corrupt = new CorruptPhotoReader(rig.Reader);
        rig.Exif = new PhotoExifCache(corrupt);
        rig.Dng(@"2026\2026-06-01\Damian_20260601_001.dng", Shot);
        corrupt.Corrupt[Lr + @"\2026\2026-06-01\Damian_20260601_001.dng"] = new OperationCanceledException();
        Assert.Throws<OperationCanceledException>(() => rig.Build());
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
