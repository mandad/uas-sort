// tests/UasSort.Core.Tests/Cleanup/Photos/LightroomRulesTests.cs
namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class LightroomRulesTests
{
    private static readonly Settings S = FakeLayout.Settings();   // video C:\Users\u\OneDrive\Pictures\UAS Videos, photo …\Picture Offload

    [Theory]
    [InlineData(@"D:\LR_Catalog", true)]
    [InlineData(@"d:\lr_catalog\Lightroom Catalog.lrcat", true)]
    [InlineData(@"X:\Photos\LR_Catalog\x.dng", true)]
    [InlineData(@"X:\Photos\Lightroom Catalog.lrcat-data\a", true)]
    [InlineData(@"X:\Photos\Lightroom Catalog.lrcat.lock", true)]
    [InlineData(@"X:\Photos\Lightroom Catalog Previews.lrdata\0\x.lrprev", true)]
    [InlineData(@"X:\Photos\Lightroom Catalog Smart Previews.lrdata", true)]
    [InlineData(@"X:\Photos\2026\2026-09-27\Damian_20260927_001.dng", false)]
    [InlineData(@"D:\Lightroom Library\2026\x.dng", false)]
    public void IsCatalogPath_CoversTheCatalogItsFilesAndPreviews(string path, bool expected)
        => Assert.Equal(expected, LightroomRules.IsCatalogPath(path));

    [Theory]
    [InlineData(@"X:\Photos\Lightroom", null)]
    [InlineData(@"D:\Lightroom Library", null)]
    [InlineData("", "Pick a folder")]
    [InlineData(@"relative\folder", "Pick a folder on a drive")]
    [InlineData(@"D:\LR_Catalog", "That is Lightroom's catalog folder; pick the folder that holds the imported photos")]
    [InlineData(@"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload", "That folder is inside the photo folder (Picture Offload)")]
    [InlineData(@"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload\LR", "That folder is inside the photo folder (Picture Offload)")]
    [InlineData(@"C:\Users\u\OneDrive\Pictures", "That folder contains the photo folder (Picture Offload)")]
    [InlineData(@"C:\Users\u\OneDrive\Pictures\UAS Videos\2026", "That folder is inside the video folder")]
    public void FolderRefusal_FollowsTheSettingsRule(string folder, string? expected)
        => Assert.Equal(expected, LightroomRules.FolderRefusal(folder, S));

    [Fact]
    public void FolderRefusal_APreviousPhotoRoot_IsRefused()
        => Assert.Equal("That folder overlaps a previous photo folder",
                        LightroomRules.FolderRefusal(@"X:\Old Offload\sub", S with { PreviousPhotoRoots = [@"X:\Old Offload"] }));
}
