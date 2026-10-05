// tests/UasSort.Core.Tests/Testing/PhotoCleanupFakesTests.cs
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Testing;

public sealed class PhotoCleanupFakesTests
{
    private static readonly DateOnly D = new(2026, 6, 1);

    [Fact]
    public void FakeRecycler_MovesOnlyThroughTheGuard()
    {
        var fs = FakeLayout.NewFileSystem();
        var dng = PhotoRoot + @"\A.DNG";
        var other = PhotoRoot + @"\B.DNG";
        fs.AddFile(dng, 10, Mtime);
        fs.AddFile(other, 10, Mtime);
        var factory = new FakePhotoRootRecyclerFactory(fs, FakeLayout.Context(cardRoot: null));
        using var recycler = factory.Open(Confirmed(new FakeTimeProvider(), PhotoCleanupMode.BeforeDate, [Row(Photo("A.DNG", D, size: 10))]));
        Assert.Equal(new PhotoItemStat(10, Mtime, 0x20, false), recycler.Stat(dng));
        Assert.True(recycler.Recycle(dng) is RecycleOk);
        Assert.False(fs.Exists(dng));
        Assert.Throws<UnsafeIoException>(() => recycler.Recycle(other));
        Assert.True(fs.Exists(other));
        Assert.Equal([PathRules.Normalize(dng)], factory.Recycled);
        Assert.Null(recycler.Stat(dng));
    }

    [Fact]
    public void FakeReader_RefusesACloudOnlyFile_AsAHydrationViolation()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(PhotoRoot + @"\A.DNG", new SyntheticDngBuilder().Build(), Mtime, FakeFileSystem.CloudOnlyPlaceholder);
        var reader = new FakePhotoFileReader(fs, FakeLayout.Context(cardRoot: null));
        Assert.Throws<HydrationViolation>(() => reader.OpenRead(PhotoRoot + @"\A.DNG"));
        Assert.Single(fs.HydrationViolations);
        Assert.Empty(reader.Opened);
    }
}
