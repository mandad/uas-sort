using UasSort.Core.Media;
using UasSort.Testing;

namespace UasSort.Core.Tests.Media;

public sealed class Mp4ThumbTests
{
    private static (BlockCache Cache, Mp4Box Moov) Open(SyntheticMp4 mp4)
    {
        var cache = new BlockCache(new MemoryStream(mp4.Bytes));
        return (cache, Mp4Boxes.Walk(cache, 0, cache.Length).Single(b => b.Type == "moov"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_ReturnsTheTnalJpegRange(bool moovFirst)
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { MoovFirst = moovFirst }.BuildFile();
        (BlockCache cache, Mp4Box moov) = Open(mp4);

        Assert.Equal(mp4.Thumb, Mp4Thumb.Find(cache, moov));
        Assert.Equal(SyntheticMp4Builder.TinyJpeg.Length, mp4.Thumb!.Value.Length);
    }

    [Fact]
    public void Find_NoUdta_ReturnsNull()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { Thumbnail = [] }.BuildFile();
        (BlockCache cache, Mp4Box moov) = Open(mp4);

        Assert.Null(Mp4Thumb.Find(cache, moov));
    }
}
