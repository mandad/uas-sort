using GeoTimeZone;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using UasSort.Testing;

namespace UasSort.Core.Tests.StackProof;

/// <summary>Ref §13 / §14 step 1: the checked-in EXIF fixture read through MetadataExtractor's Stream API, and GeoTimeZone.</summary>
public sealed class StackExifFixtureTests
{
    private static readonly string FixturePath = RepoPaths.Of("src/UasSort.App/SelfTest/stack-exif.jpg");

    [Fact]
    public void Fixture_IsASmallJpegContainer()
    {
        var bytes = File.ReadAllBytes(FixturePath);
        Assert.InRange(bytes.Length, 100, 2048);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);   // SOI
        Assert.Equal(0xFF, bytes[^2]);
        Assert.Equal(0xD9, bytes[^1]);  // EOI
    }

    [Fact]
    public void StreamRead_FindsDateTimeOriginal()
    {
        using var stream = File.OpenRead(FixturePath);
        var directories = JpegMetadataReader.ReadMetadata(stream);
        var exif = directories.OfType<ExifSubIfdDirectory>().Single();
        Assert.Equal("2026:09:27 14:01:27", exif.GetString(ExifDirectoryBase.TagDateTimeOriginal));
    }

    [Fact]
    public void StreamRead_FindsZacharBayGps()
    {
        using var stream = File.OpenRead(FixturePath);
        var gps = JpegMetadataReader.ReadMetadata(stream).OfType<GpsDirectory>().Single();
        Assert.True(gps.TryGetGeoLocation(out var location));
        Assert.Equal(57.5368, location.Latitude, 6);
        Assert.Equal(-153.7484, location.Longitude, 6);
    }

    [Fact]
    public void GeoTimeZone_ZacharBay_IsAnchorage_AndIcuKnowsTheZone()
    {
        var tz = TimeZoneLookup.GetTimeZone(57.5368, -153.7484).Result;
        Assert.Equal("America/Anchorage", tz);
        Assert.Equal(TimeSpan.FromHours(-9), TimeZoneInfo.FindSystemTimeZoneById(tz).BaseUtcOffset);
    }
}
