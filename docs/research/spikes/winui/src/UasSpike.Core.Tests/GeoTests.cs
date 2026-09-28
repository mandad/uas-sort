using UasSpike.Core;

namespace UasSpike.Core.Tests;

public class GeoTests
{
    private static readonly GeoPoint ZacharBay = new(57.5504, -153.7390);
    private static readonly GeoPoint Kodiak = new(57.7996, -152.3902);

    [Fact]
    public void ZacharBay_to_Kodiak_is_about_52_miles()
    {
        double miles = ZacharBay.MilesTo(Kodiak);
        Assert.InRange(miles, 51.0, 54.0);
    }

    [Theory]
    [InlineData(50.0, false)]
    [InlineData(60.0, true)]
    public void Radius_membership(double radiusMiles, bool expected)
        => Assert.Equal(expected, ZacharBay.IsWithinMiles(Kodiak, radiusMiles));

    [Fact]
    public void Zero_distance_to_self() => Assert.Equal(0, Geo.DistanceMetres(Kodiak, Kodiak), 6);
}
