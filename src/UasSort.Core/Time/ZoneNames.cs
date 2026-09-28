// src/UasSort.Core/Time/ZoneNames.cs
using System.Collections.Frozen;
using System.Globalization;

namespace UasSort.Core.Time;

/// <summary>Display names for zones: the short US table of Ref §9.2, abbreviations for Ref §9.13, and UTC offsets.</summary>
public static class ZoneNames
{
    static readonly FrozenDictionary<string, string> Regions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["America/New_York"] = "Eastern", ["America/Detroit"] = "Eastern", ["America/Indiana/Indianapolis"] = "Eastern",
        ["America/Kentucky/Louisville"] = "Eastern",
        ["America/Chicago"] = "Central", ["America/Indiana/Knox"] = "Central", ["America/Menominee"] = "Central",
        ["America/North_Dakota/Center"] = "Central",
        ["America/Denver"] = "Mountain", ["America/Boise"] = "Mountain",
        ["America/Phoenix"] = "Arizona",
        ["America/Los_Angeles"] = "Pacific",
        ["America/Anchorage"] = "Alaska", ["America/Juneau"] = "Alaska", ["America/Nome"] = "Alaska", ["America/Sitka"] = "Alaska",
        ["America/Yakutat"] = "Alaska", ["America/Metlakatla"] = "Alaska",
        ["Pacific/Honolulu"] = "Hawaii",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // the seven US clock zones of Ref §6.1 (the same ids as DroneClock.UsZones, Task 04.6; Phoenix, Anchorage and Honolulu are among them)
    static readonly FrozenSet<string> UsIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "America/New_York", "America/Chicago", "America/Denver", "America/Phoenix", "America/Los_Angeles",
        "America/Anchorage", "Pacific/Honolulu",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static string Region(string ianaId)
    {
        ArgumentNullException.ThrowIfNull(ianaId);
        return Regions.TryGetValue(ianaId, out var region) ? region : ianaId;
    }

    public static string ClockName(string ianaId)
    {
        ArgumentNullException.ThrowIfNull(ianaId);
        if (!Regions.TryGetValue(ianaId, out var region)) return ianaId;
        return region is "Eastern" or "Central" or "Mountain" or "Pacific" ? "US " + region : region;
    }

    public static string Abbreviation(string ianaId, DateTime utc)
    {
        ArgumentNullException.ThrowIfNull(ianaId);
        var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        if (!Zones.TryFind(ianaId, out var zone)) return "UTC";
        var dst = zone.IsDaylightSavingTime(u);
        return Region(ianaId) switch
        {
            "Eastern" => dst ? "EDT" : "EST",
            "Central" => dst ? "CDT" : "CST",
            "Mountain" => dst ? "MDT" : "MST",
            "Arizona" => "MST",
            "Pacific" => dst ? "PDT" : "PST",
            "Alaska" => dst ? "AKDT" : "AKST",
            "Hawaii" => "HST",
            _ => FormatOffset(zone.GetUtcOffset(u)),
        };
    }

    public static string FormatOffset(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero) return "UTC";
        var sign = offset < TimeSpan.Zero ? "\u2212" : "+";
        var abs = offset.Duration();
        var hours = (int)abs.TotalHours;
        return abs.Minutes == 0
            ? string.Create(CultureInfo.InvariantCulture, $"UTC{sign}{hours}")
            : string.Create(CultureInfo.InvariantCulture, $"UTC{sign}{hours}:{abs.Minutes:00}");
    }

    public static string FormatLocal(DateTime utc, string? ianaId)
    {
        var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        if (ianaId is not null && Zones.TryFind(ianaId, out var zone))
            return TimeZoneInfo.ConvertTimeFromUtc(u, zone).ToString("MMM d HH:mm", CultureInfo.InvariantCulture)
                   + " " + Abbreviation(ianaId, u);
        return u.ToString("MMM d HH:mm", CultureInfo.InvariantCulture) + " UTC";
    }

    public static TimeSpan OffsetAt(string ianaId, DateTime utc)
    {
        ArgumentNullException.ThrowIfNull(ianaId);
        return Zones.TryFind(ianaId, out var zone)
            ? zone.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc))
            : TimeSpan.Zero;   // unknown id → UTC
    }

    public static bool IsUs(string ianaId)
    {
        ArgumentNullException.ThrowIfNull(ianaId);
        return UsIds.Contains(ianaId);
    }
}
