namespace UasSort.Core.Planning;

/// <summary>Distances and centroids for grouping (Ref §8.1–8.2). Distances come from Part 04's GeoMath (Earth radius shared with map.js).</summary>
public static class PlanningGeo
{
    public const double EarthRadiusM = GeoMath.EarthRadiusMeters;
    public const double NearMiles = 10;                                // day-split emphasis and next-day append confidence
    public static readonly TimeSpan HoursGuard = TimeSpan.FromHours(3); // H

    public static Distance Haversine(GeoPoint a, GeoPoint b) => GeoMath.Haversine(a, b);

    public static GeoPoint? Centroid(IEnumerable<GeoPoint> points)
    {
        double x = 0, y = 0, z = 0; var n = 0;
        foreach (var p in points)
        {
            double la = Rad(p.Lat), lo = Rad(p.Lon);
            x += Math.Cos(la) * Math.Cos(lo); y += Math.Cos(la) * Math.Sin(lo); z += Math.Sin(la); n++;
        }
        if (n == 0) return null;
        var hyp = Math.Sqrt(x * x + y * y);
        return new GeoPoint(Deg(Math.Atan2(z, hyp)), Deg(Math.Atan2(y, x)));
    }

    public static Distance MaxPairwise(IReadOnlyList<GeoPoint> points)
    {
        double best = 0;
        for (var i = 0; i < points.Count; i++)
            for (var j = i + 1; j < points.Count; j++)
                best = Math.Max(best, Haversine(points[i], points[j]).Meters);
        return new Distance(best);
    }

    private static double Rad(double d) => d * Math.PI / 180;
    private static double Deg(double r) => r * 180 / Math.PI;
}
