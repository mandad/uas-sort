// tests/UasSort.Core.Tests/Cleanup/Photos/PhotoRootThumbnailsTests.cs
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class PhotoRootThumbnailsTests
{
    private static readonly DateOnly D = new(2026, 6, 1);

    [Fact]
    public async Task Thumbnails_ADngGivesItsEmbeddedJpeg_UnderAPhotoRootKey()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(PhotoRoot + @"\A.DNG", new SyntheticDngBuilder().Build(), Mtime);
        var thumbs = new PhotoRootThumbnails(new FakePhotoFileReader(fs, FakeLayout.Context(cardRoot: null)));
        var item = Photo("A.DNG", D);
        thumbs.Use(PhotoRoot, [item]);
        var bytes = await thumbs.GetAsync(PhotoRootThumbnails.KeyOf(item), CancellationToken.None);
        Assert.Equal(SyntheticMp4Builder.TinyJpeg.ToArray(), bytes.ToArray());
        Assert.True(PhotoRootThumbnails.IsKey(PhotoRootThumbnails.KeyOf(item)));
        Assert.False(PhotoRootThumbnails.IsKey(new ItemId("DCIM/DJI_001/A.DNG")));
    }

    [Fact] // [Review Focus 1]
    public async Task Thumbnails_CloudOnlyPhoto_IsNeverOpened()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(PhotoRoot + @"\A.DNG", new SyntheticDngBuilder().Build(), Mtime, FakeFileSystem.CloudOnlyPlaceholder);
        var reader = new FakePhotoFileReader(fs, FakeLayout.Context(cardRoot: null));
        var thumbs = new PhotoRootThumbnails(reader);
        var item = new PhotoItem("A.DNG", PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
                                 [Member("A.DNG", D, attributes: FakeFileSystem.CloudOnlyPlaceholder)]);
        thumbs.Use(PhotoRoot, [item]);
        Assert.True((await thumbs.GetAsync(PhotoRootThumbnails.KeyOf(item), CancellationToken.None)).IsEmpty);
        Assert.Empty(reader.Opened);
        Assert.Empty(fs.HydrationViolations);
    }
}
