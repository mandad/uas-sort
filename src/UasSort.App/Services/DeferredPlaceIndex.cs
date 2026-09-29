// src/UasSort.App/Services/DeferredPlaceIndex.cs — PlaceIndex loads on a background thread (Ref §4.2, PlaceIndex.LoadAsync)
namespace UasSort.App.Services;

public sealed class DeferredPlaceIndex(Task<PlaceIndex> loading) : IPlaceIndex
{
    public IReadOnlyList<PlaceHit> Near(GeoPoint p, Distance r, PlaceClass cls, int max)
    {
        PlaceIndex index;
        try { index = loading.GetAwaiter().GetResult(); }            // called from scan/plan threads, never the UI thread
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return [];                                               // a missing or damaged places.bin.gz only loses suggestions
        }
        return index.Near(p, r, cls, max);
    }
}
