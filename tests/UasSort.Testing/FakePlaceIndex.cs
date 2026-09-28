// tests/UasSort.Testing/FakePlaceIndex.cs
namespace UasSort.Testing;

/// <summary>An in-memory <see cref="IPlaceIndex"/> over given hits; <c>Away</c> is recomputed for each query.
/// <c>params</c> collection: <c>new FakePlaceIndex([hit1, hit2])</c>, <c>new FakePlaceIndex(list)</c> and <c>new FakePlaceIndex(hit1, hit2)</c> all work.</summary>
public sealed class FakePlaceIndex(params IReadOnlyList<PlaceHit> places) : IPlaceIndex
{
    public IReadOnlyList<PlaceHit> Near(GeoPoint p, Distance r, PlaceClass cls, int max)
        => places.Where(h => h.Class == cls)
                 .Select(h => h with { Away = GeoMath.Haversine(p, h.Point) })
                 .Where(h => h.Away.Meters <= r.Meters)
                 .OrderBy(h => h.Away.Meters)
                 .Take(max)
                 .ToList();
}
