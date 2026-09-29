// src/UasSort.Core/Cleanup/CleanupFormat.cs
using System.Globalization;

namespace UasSort.Core.Cleanup;

/// <summary>Formatting used by cleanup reasons and texts (Ref §9.13; defined here). Zones come from Part 04's Zones and the
/// abbreviations from ZoneNames, so cleanup shows the same zone text as every other screen.</summary>
public static class CleanupFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Gb(long bytes) => (bytes / 1e9).ToString("0.0", Inv) + " GB";

    public static string Miles(Distance d)
    {
        var mi = d.Miles;
        if (mi < 0.1) return "<0.1 mi";
        if (mi < 10) return mi.ToString("0.0", Inv) + " mi";
        return Math.Round(mi).ToString("0", Inv) + " mi";
    }

    public static DateTime Local(DateTime utc, string tzId)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zones.Find(tzId));

    public static string MonthDay(DateOnly d) => d.ToString("MMM d", Inv);
    public static string MonthDayYear(DateOnly d) => d.ToString("MMM d, yyyy", Inv);
    public static string DayOf(DateTime utc, string tzId) => Local(utc, tzId).ToString("MMM d", Inv);

    /// <summary>"AKDT", "HST", "UTC+5:30" … (Part 04 ZoneNames.Abbreviation).</summary>
    public static string Abbrev(string tzId, DateTime utc) => ZoneNames.Abbreviation(tzId, utc);
}
