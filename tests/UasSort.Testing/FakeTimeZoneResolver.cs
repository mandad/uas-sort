// tests/UasSort.Testing/FakeTimeZoneResolver.cs
namespace UasSort.Testing;

/// <summary>Answers chosen points from a table (e.g. an <c>Etc/*</c> ocean point, a border point with alternatives), others from a real resolver.</summary>
public sealed class FakeTimeZoneResolver(ITimeZoneResolver fallback) : ITimeZoneResolver
{
    readonly Dictionary<GeoPoint, TzLookup> _table = [];

    public FakeTimeZoneResolver With(GeoPoint p, TzLookup lookup)
    {
        _table[p] = lookup;
        return this;
    }

    public TzLookup Resolve(GeoPoint p) => _table.TryGetValue(p, out var lookup) ? lookup : fallback.Resolve(p);
}
