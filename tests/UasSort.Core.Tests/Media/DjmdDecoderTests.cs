using System.Globalization;
using UasSort.Core.Media;
using UasSort.Testing;

namespace UasSort.Core.Tests.Media;

public sealed class DjmdDecoderTests
{
    private static readonly GeoPoint Zachar = SyntheticMp4Builder.Zachar0128;

    private static void Near(GeoPoint expected, GeoPoint? actual, double tolerance = 1e-9)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Lat, actual.Value.Lat, tolerance);
        Assert.Equal(expected.Lon, actual.Value.Lon, tolerance);
    }

    private static ImmutableArray<int> Path(string dashed)
        => [.. dashed.Split('-').Select(s => int.Parse(s, CultureInfo.InvariantCulture))];

    [Fact]
    public void Decode_Air3sSampleZero_UsesTheModelTableAndConvertsRadians()
    {
        DjmdReading r = DjmdDecoder.Decode(new SyntheticMp4Builder().BuildSample(0), null)!;

        Assert.Equal("dvtm_Air3s.proto", r.Protocol);
        Near(Zachar, r.Point);
        Assert.Equal(298.394, r.AltM!.Value, 1e-9);
        Assert.Equal("3-3-4-1", r.FieldPath);
        Assert.False(r.Generic);
        Assert.Equal(179_000_000UL, r.UptimeUs);
        Assert.Equal("1581F6ZSYNTH0001", r.DroneSerial);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Decode_CoordinateUnitsField_SelectsDegreesOrRadians(bool writeDegrees, int units)
    {
        var b = new SyntheticMp4Builder { WriteDegrees = writeDegrees, CoordinateUnits = units };

        Near(Zachar, DjmdDecoder.Decode(b.BuildSample(0), null)!.Point);
    }

    [Theory]
    [InlineData("dvtm_Mavic4.proto")]
    [InlineData("dvtm_Mini5Pro.proto")]
    public void Decode_Mavic4AndMini5Pro_AreAlwaysDegrees(string protocol)
    {
        var b = new SyntheticMp4Builder { Protocol = protocol, WriteDegrees = true };

        DjmdReading r = DjmdDecoder.Decode(b.BuildSample(0), null)!;

        Near(Zachar, r.Point);
        Assert.False(r.Generic);
    }

    [Theory]
    [InlineData("dvtm_Air3.proto", "3-3-4-1")]
    [InlineData("dvtm_Mini4_Pro.proto", "3-3-4-1")]
    [InlineData("dvtm_wm265e.proto", "3-3-4-1")]
    [InlineData("dvtm_pm320.proto", "3-3-4-1")]
    [InlineData("dvtm_wm261.proto", "3-3-4-1")]
    [InlineData("dvtm_wa345e.proto", "3-3-4-1")]
    [InlineData("dvtm_AVATA2.proto", "3-4-4-1")]
    [InlineData("dvtm_dji_neo.proto", "3-4-4-1")]
    [InlineData("dvtm_ac203.proto", "3-4-2-1")]
    [InlineData("dvtm_ac204.proto", "3-4-2-1")]
    [InlineData("dvtm_ac206.proto", "3-4-2-1")]
    [InlineData("dvtm_oq101.proto", "3-4-2-1")]
    public void Decode_EachModelReadsItsTablePath(string protocol, string path)
    {
        var b = new SyntheticMp4Builder { Protocol = protocol, GpsPath = Path(path) };

        DjmdReading r = DjmdDecoder.Decode(b.BuildSample(0), null)!;

        Near(Zachar, r.Point);
        Assert.Equal(path, r.FieldPath);
        Assert.False(r.Generic);
        Assert.Equal(298.394, r.AltM!.Value, 1e-9);
    }

    [Fact]
    public void ModelTable_HasTheFifteenProtocolsOfRef63()
    {
        Assert.Equal(15, DjmdDecoder.ModelTable.Count);
        Assert.True(DjmdDecoder.ModelTable["dvtm_Mavic4"].AlwaysDegrees);
        Assert.False(DjmdDecoder.ModelTable["dvtm_Air3s"].AlwaysDegrees);
    }

    [Fact]
    public void Decode_LaterSample_UsesTheKnownProtocol()
    {
        byte[] sample5 = new SyntheticMp4Builder().BuildSample(5);

        DjmdReading known = DjmdDecoder.Decode(sample5, "dvtm_Air3s.proto")!;
        DjmdReading unknown = DjmdDecoder.Decode(sample5, null)!;

        Assert.False(known.Generic);
        Assert.Equal("dvtm_Air3s.proto", known.Protocol);
        Assert.True(unknown.Generic);
        Near(Zachar, known.Point);
        Near(Zachar, unknown.Point);
    }

    [Fact]
    public void Decode_UnknownProtocol_GenericSearchRecordsTheFieldPath()
    {
        var b = new SyntheticMp4Builder { Protocol = "dvtm_Future9.proto", GpsPath = [3, 5, 7, 1] };

        DjmdReading r = DjmdDecoder.Decode(b.BuildSample(0), null)!;

        Assert.True(r.Generic);
        Assert.Equal("3-5-7-1", r.FieldPath);
        Near(Zachar, r.Point);
        Assert.Null(r.AltM);
    }

    [Fact]
    public void Decode_UnknownProtocolInDegreesWithoutUnits_IsRecognisedAsDegrees()
    {
        var b = new SyntheticMp4Builder { Protocol = "dvtm_Future9.proto", GpsPath = [3, 5, 7, 1], WriteDegrees = true };

        Near(Zachar, DjmdDecoder.Decode(b.BuildSample(0), null)!.Point);
    }

    [Fact]
    public void Decode_ZeroedGps_IsAPointButNotAFix()
    {
        DjmdReading r = DjmdDecoder.Decode(new SyntheticMp4Builder { ZeroGpsBefore = 1 }.BuildSample(0), null)!;

        Assert.Equal(new GeoPoint(0, 0), r.Point);
        Assert.False(DjmdDecoder.IsFix(r.Point!.Value));
    }

    [Fact]
    public void Decode_UnknownProtocolZeroed_HasNoGenericHit()
    {
        var b = new SyntheticMp4Builder { Protocol = "dvtm_Future9.proto", GpsPath = [3, 5, 7, 1], ZeroGpsBefore = 1 };

        DjmdReading r = DjmdDecoder.Decode(b.BuildSample(0), null)!;

        Assert.Null(r.Point);
        Assert.Null(r.FieldPath);
    }

    [Fact]
    public void Decode_NotProtobuf_ReturnsNull()
    {
        byte[] junk = [0x0B, 0x00];
        Assert.Null(DjmdDecoder.Decode(junk, null));
    }

    [Fact]
    public void DecodeAt_ReadsTheRecordedPath()
    {
        var b = new SyntheticMp4Builder { Protocol = "dvtm_Future9.proto", GpsPath = [3, 5, 7, 1] };

        Near(Zachar, DjmdDecoder.DecodeAt(b.BuildSample(39), "3-5-7-1"));
        Assert.Null(DjmdDecoder.DecodeAt(b.BuildSample(39), "3-3-4-1"));
    }

    private static byte[] Len(int field, params byte[][] content)
    {
        byte[] body = [.. content.SelectMany(c => c)];
        return [(byte)((field << 3) | 2), (byte)body.Length, .. body];
    }

    private static byte[] Dbl(int field, double v)
        => [(byte)((field << 3) | 1), .. BitConverter.GetBytes(v)];

    private static DjmdReading DecodeUnknown(params byte[][] subMessages)
        => DjmdDecoder.Decode(Len(3, subMessages), null)!;

    [Fact]
    public void Decode_GenericSearch_SkipsADecoyWithOnlyLatitude()
    {
        DjmdReading r = DecodeUnknown(Len(1, Dbl(2, 0.5)), Len(2, Dbl(2, 61.2), Dbl(3, -149.9)));

        Assert.Equal("3-2", r.FieldPath);
        Near(new GeoPoint(61.2, -149.9), r.Point);
    }

    [Fact]
    public void Decode_GenericSearch_RejectsALongitudeOnlyMessage()
    {
        DjmdReading r = DecodeUnknown(Len(1, Dbl(3, 100.0)));

        Assert.Null(r.Point);
        Assert.Null(r.FieldPath);
    }

    [Theory]
    [InlineData(double.PositiveInfinity, -149.9)]
    [InlineData(61.2, double.PositiveInfinity)]
    public void Decode_GenericSearch_RejectsInfiniteCoordinates(double lat, double lon)
    {
        DjmdReading r = DecodeUnknown(Len(1, Dbl(2, lat), Dbl(3, lon)));

        Assert.Null(r.Point);
    }

    [Fact]
    public void IsFix_NeedsLatOrLonAtLeast1e6()
    {
        Assert.False(DjmdDecoder.IsFix(new GeoPoint(5e-7, -5e-7)));
        Assert.True(DjmdDecoder.IsFix(new GeoPoint(0, 2e-6)));
    }
}
