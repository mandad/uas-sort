using GeoTimeZone;

namespace UasSort.Core.Geo;

/// <summary>GeoTimeZone lookup with the <c>Etc/*</c> (open sea) results flagged (Ref §4.2, §6.4).</summary>
public sealed class GeoTimeZoneResolver : ITimeZoneResolver
{
    public TzLookup Resolve(GeoPoint p)
    {
        var result = TimeZoneLookup.GetTimeZone(p.Lat, p.Lon);
        return new TzLookup(result.Result, [.. result.AlternativeResults], IsEtc(result.Result));
    }

    public static bool IsEtc(string ianaId)
    {
        ArgumentNullException.ThrowIfNull(ianaId);
        return ianaId.StartsWith("Etc/", StringComparison.Ordinal);
    }
}
