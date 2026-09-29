namespace UasSort.Platform.Tests;

public sealed class LongPathTests
{
    [Theory]
    [InlineData(@"C:\a\b.MP4", @"\\?\C:\a\b.MP4")]
    [InlineData(@"E:\", @"\\?\E:\")]
    [InlineData(@"\\server\share\x", @"\\?\UNC\server\share\x")]
    [InlineData(@"\\?\C:\already", @"\\?\C:\already")]
    public void Prefix_AddsExtendedPrefix(string input, string expected) => Assert.Equal(expected, LongPath.Prefix(input));

    [Theory]
    [InlineData(@"\\?\C:\a\b", @"C:\a\b")]
    [InlineData(@"\\?\UNC\server\share\x", @"\\server\share\x")]
    [InlineData(@"C:\plain", @"C:\plain")]
    public void Strip_RemovesExtendedPrefix(string input, string expected) => Assert.Equal(expected, LongPath.Strip(input));

    [Fact]
    public void Prefix_RejectsRelativePath() => Assert.Throws<ArgumentException>(() => LongPath.Prefix(@"a\b"));

    [Fact]
    public void TempDir_IsUnderTempAndDeleted()
    {
        string p;
        using (var t = new TempDir())
        {
            p = t.Path;
            t.File(@"x\y.bin", attributes: FileAttributes.ReadOnly | FileAttributes.Hidden);
            Assert.StartsWith(Path.GetTempPath(), p, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("uas-sort-test-", p, StringComparison.Ordinal);
        }
        Assert.False(Directory.Exists(p));
    }
}
