using UasSort.Core.Media;
using UasSort.Testing;

namespace UasSort.Core.Tests.Media;

public sealed class SyntheticMp4BuilderTests
{
    private static List<Mp4Box> TopLevel(SyntheticMp4 mp4)
    {
        var cache = new BlockCache(new MemoryStream(mp4.Bytes));
        return [.. Mp4Boxes.Walk(cache, 0, cache.Length)];
    }

    [Fact]
    public void Build_Default_IsFtypMdatMoovWithA64BitMdat()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder().BuildFile();
        List<Mp4Box> top = TopLevel(mp4);

        Assert.Equal(["ftyp", "mdat", "moov"], top.Select(b => b.Type));
        Assert.Equal(16, top[1].HeaderSize);
        Assert.Equal(top[1].Body, mp4.MdatPayloadStart);
        Assert.Equal(top[2].Offset, mp4.MoovOffset);
        Assert.Equal(40, mp4.SampleOffsets.Length);
    }

    [Fact]
    public void Build_MoovFirst_PutsMoovBeforeMdat()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { MoovFirst = true, LargeMdat = false }.BuildFile();

        List<Mp4Box> top = TopLevel(mp4);

        Assert.Equal(["ftyp", "moov", "mdat"], top.Select(b => b.Type));
        Assert.Equal(8, top[2].HeaderSize);
    }

    [Fact]
    public void Build_WritesEachSampleAtItsRecordedOffset()
    {
        var builder = new SyntheticMp4Builder { SamplesPerChunk = 3, Co64 = true, MoovFirst = true };
        SyntheticMp4 mp4 = builder.BuildFile();

        foreach (int k in new[] { 0, 1, 7, 39 })
        {
            byte[] sample = builder.BuildSample(k);
            Assert.Equal(sample, mp4.Bytes.AsSpan((int)mp4.SampleOffsets[k], sample.Length).ToArray());
        }
    }

    [Fact]
    public void BuildSample_Zero_CarriesProtocolSerialUptimeAndGpsInfo()
    {
        IReadOnlyList<PbField> tree = Protobuf.DecodeTree(new SyntheticMp4Builder().BuildSample(0))!;

        Assert.Equal("dvtm_Air3s.proto", Protobuf.FindProtocol(tree));
        Assert.Equal("1581F6ZSYNTH0001"u8.ToArray(), Protobuf.GetPath(tree, [1, 1, 5])!.Raw.ToArray());
        Assert.Equal(179_000_000UL, Protobuf.GetPath(tree, [3, 1, 2])!.Value);
        Assert.Equal([2, 3], Protobuf.GetPath(tree, [3, 3, 4, 1])!.Message!.Select(f => f.Number));
        Assert.Equal(298_394UL, Protobuf.GetPath(tree, [3, 3, 4, 2])!.Value);
    }

    [Fact]
    public void BuildSample_Later_HasNoProtocol()
    {
        IReadOnlyList<PbField> tree = Protobuf.DecodeTree(new SyntheticMp4Builder().BuildSample(5))!;

        Assert.Null(Protobuf.FindProtocol(tree));
        Assert.Equal(3, Assert.Single(tree).Number);
    }

    [Fact]
    public void Build_MdatPayloadAt512_StartsThePayloadAtOffset512()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { IncludeMoov = false, MdatPayloadAt512 = true }.BuildFile();

        Assert.Equal(512, mp4.MdatPayloadStart);
        Assert.Equal(512, mp4.SampleOffsets[0]);
        Assert.Null(mp4.MoovOffset);
    }

    [Fact]
    public void Build_SampleAlignment_AlignsChunksAndTheMdatEnd()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { SampleAlignment = 4096 }.BuildFile();

        Assert.All(mp4.SampleOffsets, o => Assert.Equal(0, o % 4096));
        Assert.Equal(0, mp4.MdatPayloadEnd % 4096);
    }

    [Fact]
    public void Build_ThumbRangeHoldsTheJpeg()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder().BuildFile();

        Assert.NotNull(mp4.Thumb);
        byte[] jpeg = mp4.Bytes.AsSpan((int)mp4.Thumb.Value.Offset, mp4.Thumb.Value.Length).ToArray();
        Assert.Equal(SyntheticMp4Builder.TinyJpeg, jpeg);
    }
}
