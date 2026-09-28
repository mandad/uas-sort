// src/UasSort.Core/Time/Zones.cs
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace UasSort.Core.Time;

/// <summary>Cached IANA zone lookups through the built-in ICU-backed <see cref="TimeZoneInfo"/> (Ref §2.1).</summary>
public static class Zones
{
    static readonly ConcurrentDictionary<string, TimeZoneInfo?> Cache = new(StringComparer.Ordinal);

    public static bool TryFind(string? id, [NotNullWhen(true)] out TimeZoneInfo? zone)
    {
        zone = null;
        if (string.IsNullOrEmpty(id)) return false;
        zone = Cache.GetOrAdd(id, static key =>
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(key); }
            catch (TimeZoneNotFoundException) { return null; }
            catch (InvalidTimeZoneException) { return null; }
        });
        return zone is not null;
    }

    public static TimeZoneInfo Find(string id)
        => TryFind(id, out var zone) ? zone : throw new TimeZoneNotFoundException($"Unknown time zone '{id}'.");

    public static string IanaId(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (zone.HasIanaId) return zone.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;
    }
}
