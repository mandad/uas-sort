namespace UasSort.Core.Tests.Geo;

public sealed class GeoTimeZoneResolverTests
{
    static readonly GeoTimeZoneResolver Resolver = new();

    [Theory]
    [InlineData(57.5368, -153.7484, "America/Anchorage")]   // Zachar Bay
    [InlineData(64.5627, -165.3696, "America/Nome")]        // Anvil Mountain
    [InlineData(41.5155, -71.2967, "America/New_York")]     // Newport RI
    [InlineData(21.47, -158.21, "Pacific/Honolulu")]        // Makaha
    [InlineData(27.7172, 85.3240, "Asia/Kathmandu")]
    [InlineData(22.5726, 88.3639, "Asia/Kolkata")]
    public void GeoTz_LandPoints_ResolveToIanaZones(double lat, double lon, string expected)
    {
        var lookup = Resolver.Resolve(new GeoPoint(lat, lon));
        Assert.Equal(expected, lookup.IanaId);
        Assert.False(lookup.IsEtc);
        Assert.False(lookup.Alternatives.IsDefault);
        Assert.Equal(expected, TimeZoneInfo.FindSystemTimeZoneById(lookup.IanaId).Id);   // ICU lookup works (Ref §2.1)
    }

    [Fact]
    public void GeoTz_OpenOcean_IsEtc()
    {
        var lookup = Resolver.Resolve(new GeoPoint(56.0, -148.0));   // Gulf of Alaska, far offshore
        Assert.True(lookup.IsEtc);
        Assert.StartsWith("Etc/", lookup.IanaId, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Etc/GMT+10", true)]
    [InlineData("America/Anchorage", false)]
    [InlineData("etc/gmt+10", false)]
    public void GeoTz_IsEtc_IsOrdinalPrefix(string id, bool expected) => Assert.Equal(expected, GeoTimeZoneResolver.IsEtc(id));
}
