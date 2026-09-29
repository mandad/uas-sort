// tests/UasSort.Testing/Planning/BenchmarkScenario.cs
using System.Globalization;

namespace UasSort.Testing.Planning;

public static class BenchmarkScenario
{
    public static PlanScenario Build(int videos = 400, int groups = 12, int photos = 100)
    {
        var items = new List<RawItem>(videos + photos);
        var start = new DateTime(2026, 6, 1, 18, 0, 0);
        for (var i = 0; i < videos; i++)
        {
            var g = i % groups;
            var site = new GeoPoint(60 + (i % 7) * 0.001, -150 + 4 * g);          // 4° of longitude ≈ 138 mi apart, all on land
            var stamp = start.AddDays(3 * g).AddMinutes(2 * (i / groups));
            items.Add(Clip.Vid(stamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), i + 1, site));
        }
        for (var j = 0; j < photos; j++)
        {
            var g = j % groups;
            var stamp = start.AddDays(3 * g).AddMinutes(1 + 2 * (j / groups));
            items.Add(Clip.Dng(stamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), 5000 + j, new GeoPoint(60, -150 + 4 * g)));
        }
        return new PlanScenario().Card([.. items]);
    }
}
