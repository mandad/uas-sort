using MetadataExtractor;
using UasSort.Core.Media;
using UasSort.Testing;
using static UasSort.Core.Tests.Media.ProbeAssert;

namespace UasSort.Core.Tests.Media;

public sealed class StillProbeTests
{
    private static StillInfo Probe(SyntheticDngBuilder b) => StillProbe.Read(new MemoryStream(b.Build()));

    [Fact]
    public void Read_Dto_ComesFromTheExifIfdNotIfd0OrTheRawSubIfd()
    {
        StillInfo info = Probe(new SyntheticDngBuilder());

        Assert.Equal(new DateTime(2026, 5, 25, 9, 30, 28), info.DtoNaive);
        Assert.Equal(DateTimeKind.Unspecified, info.DtoNaive!.Value.Kind);
        Assert.Null(info.OffsetTime);
    }

    [Fact]
    public void Read_Gps_FromTheGpsIfd()
    {
        GpsFix fix = Fix(Probe(new SyntheticDngBuilder()).Gps);

        Near(SyntheticDngBuilder.Pano0001Gps, fix.Point, 1e-6);
        Assert.Equal(41.5, fix.AltM!.Value, 1e-9);
        Assert.Equal(GpsSource.Exif, fix.Source);
        Assert.Equal(0, fix.Sample);
        Assert.Null(fix.FieldPath);
    }

    [Fact]
    public void Read_ModelAndThumbnail_FromIfd0()
    {
        SyntheticDng dng = new SyntheticDngBuilder().BuildFile();

        StillInfo info = StillProbe.Read(new MemoryStream(dng.Bytes));

        Assert.Equal("FC9113", info.Model);
        Assert.Equal(dng.Thumb, info.Thumb);
        Assert.Equal(SyntheticMp4Builder.TinyJpeg, dng.Bytes.AsSpan((int)info.Thumb!.Value.Offset, info.Thumb.Value.Length).ToArray());
    }

    [Theory]
    [InlineData("-08:00", -8, 0)]
    [InlineData("+05:45", 5, 45)]
    public void Read_OffsetTimeOriginal_IsParsed(string text, int hours, int minutes)
    {
        StillInfo info = Probe(new SyntheticDngBuilder { OffsetTimeOriginal = text });

        TimeSpan expected = new TimeSpan(Math.Abs(hours), minutes, 0) * Math.Sign(hours);
        Assert.Equal(expected, info.OffsetTime);
    }

    [Fact]
    public void Read_NoGpsIfd_IsNoGpsTag()
    {
        Assert.Equal(NoFixReason.NoGpsTag, NoFix(Probe(new SyntheticDngBuilder { Gps = null }).Gps).Reason);
    }

    [Fact]
    public void Read_NoDto_IsNull()
    {
        Assert.Null(Probe(new SyntheticDngBuilder { Dto = null }).DtoNaive);
    }

    [Fact]
    public void Read_NoThumbnail_IsNull()
    {
        Assert.Null(Probe(new SyntheticDngBuilder { Thumbnail = [] }).Thumb);
    }

    [Fact]
    public void Read_NotAnImage_Throws()
    {
        byte[] junk = [.. "not an image at all"u8.ToArray(), .. new byte[64]];

        Assert.Throws<ImageProcessingException>(() => StillProbe.Read(new MemoryStream(junk)));
    }

    [Fact]
    public void Read_FluentBuilderDng_RoundTrips()
    {
        var zachar = new GeoPoint(57.5368, -153.7484);
        byte[] dng = new SyntheticDngBuilder()
            .WithDateTimeOriginal(new DateTime(2026, 9, 27, 14, 5, 0))
            .WithGps(zachar)
            .WithModel("FC9113")
            .WithThumbnail([0xFF, 0xD8, 0xFF, 0xD9])
            .Build();

        StillInfo info = StillProbe.Read(new MemoryStream(dng));

        Assert.Equal(new DateTime(2026, 9, 27, 14, 5, 0), info.DtoNaive);
        Near(zachar, Fix(info.Gps).Point, 1e-6);
        Assert.Equal("FC9113", info.Model);
        Assert.Equal(4, info.Thumb!.Value.Length);
    }
}
