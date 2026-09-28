namespace UasSort.Core.Tests.Geo;

public sealed class GeoMathTests
{
    static readonly GeoPoint Anvil = new(64.5627, -165.3696);
    static readonly GeoPoint Council = new(64.6935, -164.2657);
    static readonly GeoPoint Zachar = new(57.5368, -153.7484);
    static readonly GeoPoint KodiakTown = new(57.7996, -152.3902);

    [Fact]
    public void GeoMath_Haversine_CouncilToAnvil_Is33_9Miles()
        => Assert.Equal(33.908, GeoMath.Haversine(Council, Anvil).Miles, 0.01);

    [Fact]
    public void GeoMath_Haversine_ZacharToKodiakTown_Is53_4Miles()
        => Assert.Equal(53.372, GeoMath.Haversine(Zachar, KodiakTown).Miles, 0.01);

    [Fact]
    public void GeoMath_Haversine_AcrossAntimeridian_IsShort()
        => Assert.Equal(4.263, GeoMath.Haversine(new GeoPoint(51.9, 179.95), new GeoPoint(51.9, -179.95)).Miles, 0.01);

    [Fact]
    public void GeoMath_Haversine_SamePoint_IsZero()
        => Assert.Equal(0.0, GeoMath.Haversine(Zachar, Zachar).Meters, 1e-9);

    [Fact]
    public void GeoMath_MetersPerDegree_UsesSharedEarthRadius()
        => Assert.Equal(111_195.08, GeoMath.MetersPerDegree, 0.01);

    [Fact]
    public void GeoMath_Median_TakesComponentMedians()
    {
        List<GeoPoint> odd = [new(1, 10), new(3, 30), new(2, 20)];
        Assert.Equal(new GeoPoint(2, 20), GeoMath.Median(odd));
        List<GeoPoint> even = [new(1, 10), new(2, 40), new(4, 20), new(3, 30)];
        Assert.Equal(new GeoPoint(2.5, 25), GeoMath.Median(even));
    }

    [Fact]
    public void GeoMath_Median_EmptyThrows()
        => Assert.Throws<ArgumentException>(() => GeoMath.Median(new List<GeoPoint>()));
}
