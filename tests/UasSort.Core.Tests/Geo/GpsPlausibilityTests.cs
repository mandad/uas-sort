namespace UasSort.Core.Tests.Geo;

public sealed class GpsPlausibilityTests
{
    static readonly GeoPoint Zachar = new(57.5368, -153.7484);
    static readonly TzLookup Anchorage = new("America/Anchorage", [], false);
    static readonly TzLookup Ocean = new("Etc/GMT+10", [], true);
    static readonly List<GeoPoint> KodiakCard = [new(57.7996, -152.3902), new(57.55, -153.74)];
    static readonly List<GeoPoint> NewportCard = [new(41.5155, -71.2967), new(41.4762, -71.3237)];
    static readonly List<GeoPoint> NoCard = [];

    static GpsFix Generic(GeoPoint p, int sample) => new(p, 50, sample, GpsSource.DjmdGenericSearch, "3-9-1");
    static GeoPoint North(GeoPoint p, double miles) => new(p.Lat + miles * 1609.344 / GeoMath.MetersPerDegree, p.Lon);

    static void AssertRejected(GpsProbe probe)
        => Assert.True(probe is NoFix n && n.Reason == NoFixReason.GenericHitImplausible, "expected NoFix(GenericHitImplausible)");

    [Fact]
    public void GpsGate_ConsistentHit_IsAccepted()
    {
        var first = Generic(Zachar, 0);
        var probe = GpsPlausibility.Check(first, Generic(North(Zachar, 0.5), 299), Anchorage, KodiakCard);
        Assert.True(probe is GpsFix f && f == first);
    }

    [Fact]
    public void GpsGate_FirstAndLast2_99MiApart_IsAccepted()
        => Assert.True(GpsPlausibility.Check(Generic(Zachar, 0), Generic(North(Zachar, 2.99), 299), Anchorage, KodiakCard) is GpsFix);

    [Fact]
    public void GpsGate_FirstAndLast3_2MiApart_IsRejected()
        => AssertRejected(GpsPlausibility.Check(Generic(Zachar, 0), Generic(North(Zachar, 3.2), 299), Anchorage, KodiakCard));

    [Fact]
    public void GpsGate_EtcZone_IsRejected()
        => AssertRejected(GpsPlausibility.Check(Generic(Zachar, 0), Generic(Zachar, 299), Ocean, KodiakCard));

    [Fact]
    public void GpsGate_MoreThan500MiFromCardMedian_IsRejected()
        => AssertRejected(GpsPlausibility.Check(Generic(Zachar, 0), Generic(Zachar, 299), Anchorage, NewportCard));

    [Fact]
    public void GpsGate_NoOtherGpsOnCard_SkipsDistanceTest()
        => Assert.True(GpsPlausibility.Check(Generic(Zachar, 0), Generic(Zachar, 299), Anchorage, NoCard) is GpsFix);

    [Fact]
    public void GpsGate_NoLastSample_IsRejected()
        => AssertRejected(GpsPlausibility.Check(Generic(Zachar, 0), null, Anchorage, KodiakCard));
}
