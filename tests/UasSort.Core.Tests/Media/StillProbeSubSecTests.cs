// tests/UasSort.Core.Tests/Media/StillProbeSubSecTests.cs
namespace UasSort.Core.Tests.Media;

public sealed class StillProbeSubSecTests
{
    [Fact]
    public void SubSecTimeOriginal_IsReadNextToTheDto()
    {
        var info = StillProbe.Read(new MemoryStream(new SyntheticDngBuilder().WithSubSec("045").Build()));
        Assert.Equal(SyntheticDngBuilder.Pano0001Dto, info.DtoNaive);
        Assert.Equal("045", info.SubSec);
    }

    [Fact]
    public void NoSubSecTag_IsNull()
        => Assert.Null(StillProbe.Read(new MemoryStream(new SyntheticDngBuilder().Build())).SubSec);

    [Fact]
    public void PixelDimensions_AreReadFromTheExifPixelTags_AndNullWithoutThem()
    {
        var info = StillProbe.Read(new MemoryStream(new SyntheticDngBuilder { ExifPixels = (8192, 4096) }.Build()));
        Assert.Equal(((int?)8192, (int?)4096), (info.PixelWidth, info.PixelHeight));
        var none = StillProbe.Read(new MemoryStream(new SyntheticDngBuilder().Build()));
        Assert.Equal(((int?)null, (int?)null), (none.PixelWidth, none.PixelHeight));
    }

    [Theory]
    [InlineData("045", "045")]
    [InlineData(" 12 ", "12")]
    [InlineData("5\0", "5")]
    [InlineData("  ", null)]
    [InlineData("x1", null)]
    public void SubSecDigits_KeepsTheLeadingDigits(string raw, string? expected)
        => Assert.Equal(expected, StillProbe.SubSecDigits(raw));
}
