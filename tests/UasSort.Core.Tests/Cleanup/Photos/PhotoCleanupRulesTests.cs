// tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupRulesTests.cs
namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class PhotoCleanupRulesTests
{
    [Theory]
    [InlineData("001_0087", true, "001_0087")]
    [InlineData("001_0087 2026-09-27", true, "001_0087")]
    [InlineData("001_0087 2026-09-27 (2)", true, "001_0087")]
    [InlineData("001_0087 (3)", true, "001_0087")]
    [InlineData("Exports", false, "Exports")]
    [InlineData("2026-09-27 Kodiak", false, "2026-09-27 Kodiak")]
    public void SetFolderNames_FollowRefSection88(string folder, bool looksLikeSet, string setName)
    {
        Assert.Equal(looksLikeSet, PhotoCleanupRules.LooksLikeSetFolder(folder));
        Assert.Equal(setName, PhotoCleanupRules.SetNameOf(folder));
    }

    [Theory]
    [InlineData("A.DNG", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("b.HEIC", true)]
    [InlineData("c.tif", true)]
    [InlineData("v.MP4", false)]
    [InlineData("desktop.ini", false)]
    public void PhotoNames(string name, bool photo) => Assert.Equal(photo, PhotoCleanupRules.IsPhotoName(name));

    [Fact]
    public void CloudOnly_IsAnyPlaceholderBit()
    {
        Assert.True(PhotoCleanupRules.IsCloudOnly(FakeFileSystem.CloudOnlyPlaceholder));
        Assert.True(PhotoCleanupRules.IsCloudOnly(IoGuardPolicy.FileAttributeOffline));
        Assert.False(PhotoCleanupRules.IsCloudOnly(0x20 | IoGuardPolicy.FileAttributePinned));
    }

    [Theory]
    [InlineData(8192, 4096, true)]      // DJI sphere: 2:1
    [InlineData(12000, 3000, true)]     // 180°
    [InlineData(3000, 9000, true)]      // vertical
    [InlineData(4032, 3024, false)]     // an ordinary 4:3 still
    [InlineData(5280, 2970, false)]     // an ordinary 16:9 still
    [InlineData(0, 100, false)]
    public void LooksLikePanorama_NeedsTheLongerSideAtLeastTwiceTheShorter(int w, int h, bool pano)
        => Assert.Equal(pano, PhotoCleanupRules.LooksLikePanorama((w, h)));

    [Fact]
    public void LooksLikePanorama_UnknownSize_IsNo() => Assert.False(PhotoCleanupRules.LooksLikePanorama(null));
}
