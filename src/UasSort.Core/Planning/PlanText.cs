using System.Globalization;

namespace UasSort.Core.Planning;

/// <summary>Why/issue text formatting per Ref §9.13 (miles, dates); zone names, abbreviations and offsets come from Part 04's ZoneNames.</summary>
public static class PlanText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static TimeZoneInfo Zone(string tzId) => Zones.Find(tzId);

    public static string Miles(Distance d)
    {
        var mi = d.Miles;
        if (mi < 0.1) return "<0.1 mi";
        if (mi < 10) return mi.ToString("0.0", Inv) + " mi";
        return Math.Round(mi, MidpointRounding.AwayFromZero).ToString("0", Inv) + " mi";
    }

    public static string ShortDate(DateOnly d) => d.ToString("MMM d", Inv);

    public static string DateRange(DateOnly a, DateOnly b)
    {
        if (a == b) return ShortDate(a);
        return a.Month == b.Month && a.Year == b.Year
            ? $"{ShortDate(a)}–{b.Day.ToString(Inv)}"
            : $"{ShortDate(a)}–{ShortDate(b)}";
    }

    public static string Offset(TimeSpan o) => ZoneNames.FormatOffset(o);

    public static string ZoneAbbrev(string tzId, DateTime utc) => ZoneNames.Abbreviation(tzId, utc);

    public static string LocalTime(DateTime utc, string tzId) => ZoneNames.FormatLocal(utc, tzId);

    /// <summary>Site-zone names for the clock-mismatch InfoBar (Ref §9.2 short table), else the IANA ID.</summary>
    public static string ZoneName(string tzId) => ZoneNames.Region(tzId);

    public static string ClockZoneName(string tzId) => ZoneNames.ClockName(tzId);

    public static string Count(int n, string one, string many) => $"{n.ToString(Inv)} {(n == 1 ? one : many)}";
}
