namespace UasSort.Core.Geo;

/// <summary>Great-circle distance and simple point statistics on the shared Earth radius (Ref §3 <c>Distance</c>).</summary>
public static class GeoMath
{
    public const double EarthRadiusMeters = 6_371_008.8;
    const double Rad = Math.PI / 180;

    public static double MetersPerDegree => EarthRadiusMeters * Rad;

    public static Distance Haversine(GeoPoint a, GeoPoint b)
    {
        var dLat = (b.Lat - a.Lat) * Rad;
        var dLon = (b.Lon - a.Lon) * Rad;
        var sinLat = Math.Sin(dLat / 2);
        var sinLon = Math.Sin(dLon / 2);
        var h = sinLat * sinLat + Math.Cos(a.Lat * Rad) * Math.Cos(b.Lat * Rad) * sinLon * sinLon;
        return new Distance(2 * EarthRadiusMeters * Math.Asin(Math.Min(1.0, Math.Sqrt(h))));
    }

    public static GeoPoint Median(IReadOnlyList<GeoPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0) throw new ArgumentException("At least one point is required.", nameof(points));
        return new GeoPoint(MedianOf(points.Select(p => p.Lat)), MedianOf(points.Select(p => p.Lon)));
    }

    static double MedianOf(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
