using UasSort.Core.Media;
using UasSort.Testing;
using static UasSort.Core.Tests.Media.ProbeAssert;

namespace UasSort.Core.Tests.Media;

public sealed class Mp4ProbeSearchTests
{
    [Fact]
    public void ProbeIndices_300Samples_Are16()
    {
        int[] expected = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 16, 32, 64, 128, 256, 299];

        Assert.Equal(expected, Mp4Probe.ProbeIndices(300));
    }

    [Fact]
    public void ProbeIndices_SmallCounts()
    {
        Assert.Empty(Mp4Probe.ProbeIndices(0));
        Assert.Equal([0], Mp4Probe.ProbeIndices(1));
        Assert.Equal(Enumerable.Range(0, 10), Mp4Probe.ProbeIndices(10));
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 16], Mp4Probe.ProbeIndices(17));
    }

    [Fact]
    public void Read_GpsZeroedUntilSample7_FixFromSample7()
    {
        var b = new SyntheticMp4Builder { ZeroGpsBefore = 7, SamplesPerChunk = 4, Co64 = true, DjmdSecondStsdEntry = true };

        GpsFix fix = Fix(Mp4Probe.Read(new MemoryStream(b.Build())).First);

        Assert.Equal(7, fix.Sample);
        Near(SyntheticMp4Builder.Zachar0128, fix.Point);
    }

    [Fact]
    public void Read_GpsZeroedUntilSample32_FixFromSample32()
    {
        var b = new SyntheticMp4Builder { ZeroGpsBefore = 32, SamplesPerChunk = 5, MoovFirst = true };

        Assert.Equal(32, Fix(Mp4Probe.Read(new MemoryStream(b.Build())).First).Sample);
    }

    [Fact]
    public void Read_GpsZeroedEverywhere_IsAllProbedSamplesZero()
    {
        Mp4Info info = Mp4Probe.Read(new MemoryStream(new SyntheticMp4Builder { ZeroGpsBefore = 40 }.Build()));

        Assert.Equal(NoFixReason.AllProbedSamplesZero, NoFix(info.First).Reason);
        Assert.Equal("dvtm_Air3s.proto", info.Protocol);
        Assert.NotNull(info.SessionUtc);
    }

    [Theory]
    [InlineData(300, 16)]
    [InlineData(32, 12)]
    [InlineData(0, 1)]
    public void Read_SampleReadBudget_AtMost16ForTheFirstFix(int zeroBefore, int expectedSampleReads)
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { SampleCount = 300, ZeroGpsBefore = zeroBefore, SampleAlignment = 4096 }.BuildFile();
        var counting = new CountingStream(new MemoryStream(mp4.Bytes));

        Mp4Probe.Read(counting);

        int sampleReads = counting.ReadsWithin(mp4.MdatPayloadStart, mp4.MdatPayloadEnd);
        Assert.Equal(expectedSampleReads, sampleReads);
        Assert.True(sampleReads <= 16);
    }

    [Fact]
    public void Read_FixInSample0_CostsAtMost7UnderlyingReads()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { SampleCount = 300, SampleAlignment = 4096 }.BuildFile();
        var counting = new CountingStream(new MemoryStream(mp4.Bytes));

        Mp4Probe.Read(counting);

        Assert.InRange(counting.ReadCalls, 1, 7);
    }
}
