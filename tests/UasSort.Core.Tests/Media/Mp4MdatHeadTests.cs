using UasSort.Core.Media;
using UasSort.Testing;

namespace UasSort.Core.Tests.Media;

public sealed class Mp4MdatHeadTests
{
    private static (BlockCache Cache, Mp4Box? Mdat) Open(byte[] bytes)
    {
        var cache = new BlockCache(new MemoryStream(bytes));
        Mp4Box? mdat = null;
        foreach (Mp4Box b in Mp4Boxes.Walk(cache, 0, cache.Length))
            if (b.Type == "mdat") mdat = b;
        return (cache, mdat);
    }

    [Fact]
    public void TryRead_UsesTheMdatPayloadStart()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { IncludeMoov = false }.BuildFile();
        (BlockCache cache, Mp4Box? mdat) = Open(mp4.Bytes);

        DjmdReading r = Mp4MdatHead.TryRead(cache, mdat)!;

        Assert.NotEqual(512, mp4.MdatPayloadStart);
        Assert.Equal("dvtm_Air3s.proto", r.Protocol);
        Assert.Equal(SyntheticMp4Builder.Zachar0128.Lat, r.Point!.Value.Lat, 1e-9);
        Assert.Equal(SyntheticMp4Builder.Zachar0128.Lon, r.Point!.Value.Lon, 1e-9);
    }

    [Fact]
    public void TryRead_FallsBackToOffset512WhenTheWalkNeverReachesMdat()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { IncludeMoov = false, MdatPayloadAt512 = true, CorruptPaddingBox = true }.BuildFile();
        (BlockCache cache, Mp4Box? mdat) = Open(mp4.Bytes);

        DjmdReading r = Mp4MdatHead.TryRead(cache, mdat)!;

        Assert.Null(mdat);
        Assert.True(DjmdDecoder.IsFix(r.Point!.Value));
    }

    [Fact]
    public void TryRead_ZeroedGps_ReturnsTheReadingWithoutAFix()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { IncludeMoov = false, ZeroGpsBefore = 40 }.BuildFile();
        (BlockCache cache, Mp4Box? mdat) = Open(mp4.Bytes);

        DjmdReading r = Mp4MdatHead.TryRead(cache, mdat)!;

        Assert.Equal(new GeoPoint(0, 0), r.Point);
    }

    [Fact]
    public void TryRead_NoProtocol_ReturnsNull()
    {
        byte[] zeros = new byte[8192];
        (BlockCache cache, Mp4Box? mdat) = Open(zeros);

        Assert.Null(Mp4MdatHead.TryRead(cache, mdat));
    }
}
