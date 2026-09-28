using UasSort.Core.Media;
using UasSort.Testing;
using static UasSort.Core.Tests.Media.ProbeAssert;

namespace UasSort.Core.Tests.Media;

public sealed class Mp4ProbeTests
{
    private static readonly DateTime Mvhd0128 = new(2026, 9, 27, 18, 6, 27, DateTimeKind.Utc);

    private static Mp4Info Probe(SyntheticMp4Builder b) => Mp4Probe.Read(new MemoryStream(b.Build()));

    [Fact]
    public void Read_SyntheticZachar0128_ReadsTimeGpsSessionSerialThumbAndLength()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder().BuildFile();

        Mp4Info info = Mp4Probe.Read(new MemoryStream(mp4.Bytes));

        Assert.True(info.HasMoov);
        Assert.Equal(Mvhd0128, info.MvhdUtc);
        Assert.Equal(DateTimeKind.Utc, info.MvhdUtc!.Value.Kind);
        GpsFix fix = Fix(info.First);
        Near(SyntheticMp4Builder.Zachar0128, fix.Point);
        Assert.Equal(298.394, fix.AltM!.Value, 1e-9);
        Assert.Equal(0, fix.Sample);
        Assert.Equal(GpsSource.DjmdModelTable, fix.Source);
        Assert.Equal("3-3-4-1", fix.FieldPath);
        Assert.Null(info.LastSameField);
        Assert.Equal("dvtm_Air3s.proto", info.Protocol);
        Assert.Equal("1581F6ZSYNTH0001", info.DroneSerial);
        Assert.Equal(new DateTime(2026, 9, 27, 18, 3, 28, DateTimeKind.Utc), info.SessionUtc);
        Assert.Equal(mp4.Thumb, info.Thumb);
        Assert.Equal(TimeSpan.FromSeconds(222), info.Duration);
    }

    [Theory]
    [InlineData(true, true, true, 3, true)]
    [InlineData(false, false, false, 1, false)]
    [InlineData(false, true, true, 5, true)]
    [InlineData(true, false, true, 1, false)]
    public void Read_BoxLayouts_AllFindTheFirstFix(bool moovFirst, bool co64, bool largeMdat, int perChunk, bool secondStsd)
    {
        var b = new SyntheticMp4Builder
        {
            MoovFirst = moovFirst, Co64 = co64, LargeMdat = largeMdat, SamplesPerChunk = perChunk, DjmdSecondStsdEntry = secondStsd,
        };

        GpsFix fix = Fix(Probe(b).First);

        Near(SyntheticMp4Builder.Zachar0128, fix.Point);
        Assert.Equal(0, fix.Sample);
    }

    [Theory]
    [InlineData(0, 1000u, 222_000ul, 222.0)]
    [InlineData(1, 1000u, 222_000ul, 222.0)]
    [InlineData(0, 90000u, 5_535_000ul, 61.5)]
    [InlineData(1, 90000u, 5_535_000ul, 61.5)]
    public void Read_MvhdVersionAndTimescale_GiveDuration(int version, uint timescale, ulong duration, double seconds)
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { MvhdVersion = version, Timescale = timescale, Duration = duration });

        Assert.Equal(TimeSpan.FromSeconds(seconds), info.Duration);
        Assert.Equal(Mvhd0128, info.MvhdUtc);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void Read_DegreesOrRadians_GiveTheSamePoint(bool writeDegrees, int? units)
    {
        GpsFix fix = Fix(Probe(new SyntheticMp4Builder { WriteDegrees = writeDegrees, CoordinateUnits = units }).First);

        Near(SyntheticMp4Builder.Zachar0128, fix.Point);
    }

    [Fact]
    public void Read_AutelStyleClipWithoutDjmd_IsNoDjmdTrack()
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { WithDjmdTrack = false });

        Assert.Equal(NoFixReason.NoDjmdTrack, NoFix(info.First).Reason);
        Assert.True(info.HasMoov);
        Assert.Equal(Mvhd0128, info.MvhdUtc);
        Assert.Equal(TimeSpan.FromSeconds(222), info.Duration);
        Assert.Null(info.Protocol);
    }

    [Fact]
    public void Read_UnknownProtocol_GenericHitWithLastSameField()
    {
        var b = new SyntheticMp4Builder { Protocol = "dvtm_Future9.proto", GpsPath = [3, 5, 7, 1] };

        Mp4Info info = Probe(b);

        GpsFix fix = Fix(info.First);
        Assert.Equal(GpsSource.DjmdGenericSearch, fix.Source);
        Assert.Equal("3-5-7-1", fix.FieldPath);
        Assert.Equal("dvtm_Future9.proto", info.Protocol);
        GpsFix last = info.LastSameField!;
        Assert.Equal(39, last.Sample);
        Assert.Equal("3-5-7-1", last.FieldPath);
        Assert.Equal(GpsSource.DjmdGenericSearch, last.Source);
        Near(SyntheticMp4Builder.Zachar0128, last.Point);
    }

    [Fact]
    public void Read_NoMoov_UsesTheMdatPayloadStart()
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { IncludeMoov = false });

        Assert.False(info.HasMoov);
        Assert.Null(info.MvhdUtc);
        Assert.Null(info.Duration);
        Assert.Null(info.SessionUtc);
        Assert.Null(info.Thumb);
        GpsFix fix = Fix(info.First);
        Assert.Equal(GpsSource.MdatHeadFallback, fix.Source);
        Assert.Equal(0, fix.Sample);
        Near(SyntheticMp4Builder.Zachar0128, fix.Point);
        Assert.Equal("dvtm_Air3s.proto", info.Protocol);
        Assert.Equal("1581F6ZSYNTH0001", info.DroneSerial);
    }

    [Fact]
    public void Read_NoMoov_FallsBackToOffset512()
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { IncludeMoov = false, MdatPayloadAt512 = true, CorruptPaddingBox = true });

        Assert.Equal(GpsSource.MdatHeadFallback, Fix(info.First).Source);
    }

    [Fact]
    public void Read_NoMoovAndNothingDjiLike_IsNotDji()
    {
        Mp4Info info = Mp4Probe.Read(new MemoryStream(new byte[8192]));

        Assert.Equal(NoFixReason.NotDji, NoFix(info.First).Reason);
        Assert.False(info.HasMoov);
    }

    [Fact]
    public void Read_NoMoovWithZeroedGps_IsAllProbedSamplesZero()
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { IncludeMoov = false, ZeroGpsBefore = 40 });

        Assert.Equal(NoFixReason.AllProbedSamplesZero, NoFix(info.First).Reason);
        Assert.Equal("dvtm_Air3s.proto", info.Protocol);
    }

    [Fact]
    public void Read_MoovWithSize0_RunsToEndOfFile()
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { LastBoxSizeZero = true });

        Assert.True(info.HasMoov);
        Assert.Equal(0, Fix(info.First).Sample);
    }

    [Fact]
    public void Read_MoovClaimingMoreThanTheFile_IsTreatedAsMissing()
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { LastBoxOverclaim = 100 });

        Assert.False(info.HasMoov);
        Assert.Null(info.Duration);
        Assert.Equal(GpsSource.MdatHeadFallback, Fix(info.First).Source);
    }

    [Fact]
    public void Read_TruncatedMdatWithoutMoov_StillReadsItsHead()
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { IncludeMoov = false, LastBoxOverclaim = 1_000_000 });

        Assert.Equal(GpsSource.MdatHeadFallback, Fix(info.First).Source);
    }

    [Fact]
    public void Read_UuidBoxAtTopLevel_IsSkipped()
    {
        Assert.Equal(GpsSource.DjmdModelTable, Fix(Probe(new SyntheticMp4Builder { UuidBox = true }).First).Source);
    }

    [Fact]
    public void Read_FluentBuilderClip_RoundTrips()
    {
        var anvil = new GeoPoint(64.5627, -165.3696);
        byte[] jpeg = [0xFF, 0xD8, 0x01, 0x02, 0xFF, 0xD9];
        byte[] clip = new SyntheticMp4Builder()
            .WithMvhdUtc(new DateTime(2026, 7, 26, 7, 50, 0, DateTimeKind.Utc))
            .WithDuration(TimeSpan.FromSeconds(95))
            .WithDjmdGps("dvtm_AVATA2.proto", anvil)
            .WithThumbnail(jpeg)
            .Build();

        Mp4Info info = Mp4Probe.Read(new MemoryStream(clip));

        Assert.Equal(new DateTime(2026, 7, 26, 7, 50, 0, DateTimeKind.Utc), info.MvhdUtc);
        Assert.Equal(TimeSpan.FromSeconds(95), info.Duration);
        GpsFix fix = Fix(info.First);
        Near(anvil, fix.Point);
        Assert.Equal("3-4-4-1", fix.FieldPath);
        Assert.Equal(jpeg.Length, info.Thumb!.Value.Length);
    }
}
