using UasSort.Core.Media;
using UasSort.Testing;

namespace UasSort.Core.Tests.Media;

public sealed class Mp4SampleTableTests
{
    private static List<Mp4Box> Stbls(BlockCache cache)
    {
        Mp4Box moov = Mp4Boxes.Walk(cache, 0, cache.Length).Single(b => b.Type == "moov");
        var result = new List<Mp4Box>();
        foreach (Mp4Box trak in Mp4Boxes.Walk(cache, moov.Body, moov.End).Where(b => b.Type == "trak"))
        {
            Mp4Box mdia = Mp4Boxes.Child(cache, trak, "mdia")!.Value;
            Mp4Box minf = Mp4Boxes.Child(cache, mdia, "minf")!.Value;
            result.Add(Mp4Boxes.Child(cache, minf, "stbl")!.Value);
        }
        return result;
    }

    [Fact]
    public void Open_TwoStsdEntries_FindsDjmdAsEntry2()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { DjmdSecondStsdEntry = true }.BuildFile();
        var cache = new BlockCache(new MemoryStream(mp4.Bytes));

        Mp4SampleTable table = Mp4SampleTable.Open(cache, Stbls(cache)[1])!;

        Assert.Equal(["mett", "djmd"], table.Formats);
        Assert.Equal(2, table.DjmdDescription);
        Assert.Equal(40, table.SampleCount);
    }

    [Fact]
    public void ReadFormats_VideoTrack_HasNoDjmd()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder().BuildFile();
        var cache = new BlockCache(new MemoryStream(mp4.Bytes));

        Mp4Box stsd = Mp4Boxes.Child(cache, Stbls(cache)[0], "stsd")!.Value;

        Assert.Equal(["hvc1"], Mp4SampleTable.ReadFormats(cache, stsd));
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(3, true, true)]
    [InlineData(5, false, true)]
    [InlineData(5, true, false)]
    public void Locate_MatchesTheBuilderLayout(int perChunk, bool co64, bool moovFirst)
    {
        var builder = new SyntheticMp4Builder { SamplesPerChunk = perChunk, Co64 = co64, MoovFirst = moovFirst, DjmdSecondStsdEntry = true };
        SyntheticMp4 mp4 = builder.BuildFile();
        var cache = new BlockCache(new MemoryStream(mp4.Bytes));
        Mp4SampleTable table = Mp4SampleTable.Open(cache, Stbls(cache)[1])!;

        for (int k = 0; k < 40; k++)
        {
            SampleLocation loc = table.Locate(k)!.Value;
            Assert.Equal(mp4.SampleOffsets[k], loc.Offset);
            Assert.Equal(builder.BuildSample(k).Length, loc.Size);
            Assert.Equal(2, loc.DescriptionIndex);
        }
    }

    [Fact]
    public void Locate_OutOfRange_ReturnsNull()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder().BuildFile();
        var cache = new BlockCache(new MemoryStream(mp4.Bytes));
        Mp4SampleTable table = Mp4SampleTable.Open(cache, Stbls(cache)[1])!;

        Assert.Null(table.Locate(40));
        Assert.Null(table.Locate(-1));
    }

    [Fact]
    public void Open_WithoutStsc_ReturnsNull()
    {
        byte[] stsd = [0, 0, 0, 24, .. "stsd"u8.ToArray(), 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 8, .. "djmd"u8.ToArray()];
        byte[] stbl = [0, 0, 0, (byte)(8 + stsd.Length), .. "stbl"u8.ToArray(), .. stsd];
        var cache = new BlockCache(new MemoryStream(stbl));
        Mp4Box box = Mp4Boxes.Walk(cache, 0, cache.Length).Single();

        Assert.Equal(["djmd"], Mp4SampleTable.ReadFormats(cache, Mp4Boxes.Child(cache, box, "stsd")!.Value));
        Assert.Null(Mp4SampleTable.Open(cache, box));
    }
}
