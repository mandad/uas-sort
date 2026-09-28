namespace UasSpike.Core;

public readonly record struct GeoPoint(double Lat, double Lon);

public static class Geo
{
    public const double MetresPerMile = 1609.344;
    private const double EarthRadiusMetres = 6_371_008.8; // IUGG mean radius

    /// <summary>Great-circle distance (haversine), metres.</summary>
    public static double DistanceMetres(GeoPoint a, GeoPoint b)
    {
        double φ1 = double.DegreesToRadians(a.Lat), φ2 = double.DegreesToRadians(b.Lat);
        double dφ = φ2 - φ1, dλ = double.DegreesToRadians(b.Lon - a.Lon);
        double h = Math.Sin(dφ / 2) * Math.Sin(dφ / 2)
                 + Math.Cos(φ1) * Math.Cos(φ2) * Math.Sin(dλ / 2) * Math.Sin(dλ / 2);
        return 2 * EarthRadiusMetres * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}

/// <summary>C# 14 extension members (new syntax) - proves the latest language version compiles here.</summary>
public static class GeoPointExtensions
{
    extension(GeoPoint p)
    {
        public double MilesTo(GeoPoint other) => Geo.DistanceMetres(p, other) / Geo.MetresPerMile;
        public bool IsWithinMiles(GeoPoint other, double miles) => p.MilesTo(other) <= miles;
    }
}
