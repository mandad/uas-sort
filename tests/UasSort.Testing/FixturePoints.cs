// tests/UasSort.Testing/FixturePoints.cs
namespace UasSort.Testing;

/// <summary>The spike fixture's sites (Ref §13, from docs/research/spikes/grouping/test_grouping.py) plus two for the 15-min case.</summary>
public static class FixturePoints
{
    public static readonly GeoPoint Anvil = new(64.5627, -165.3696);
    public static readonly GeoPoint Council = new(64.6935, -164.2657);
    public static readonly GeoPoint NomeA = new(64.6932, -165.7665);
    public static readonly GeoPoint NomeB = new(64.5925, -165.6731);
    public static readonly GeoPoint Zachar = new(57.5368, -153.7484);
    public static readonly GeoPoint KodiakTown = new(57.7996, -152.3902);
    public static readonly GeoPoint NewportAm = new(41.5155, -71.2967);
    public static readonly GeoPoint NewportPm = new(41.4762, -71.3237);
    public static readonly GeoPoint Makaha = new(21.47, -158.21);
    public static readonly GeoPoint Kathmandu = new(27.7172, 85.3240);
    public static readonly GeoPoint Kolkata = new(22.5726, 88.3639);
}
