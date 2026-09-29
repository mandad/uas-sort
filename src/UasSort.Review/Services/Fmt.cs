// src/UasSort.Review/Services/Fmt.cs
namespace UasSort.Review;

/// <summary>Ref §9.13 units and formatting; every VM text goes through here.</summary>
public static class Fmt
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static string Miles(Distance d)
    {
        var mi = d.Miles;
        if (mi < 0.1) return "<0.1 mi";
        if (mi < 10) return string.Create(C, $"{mi:0.0} mi");
        return string.Create(C, $"{Math.Round(mi, MidpointRounding.AwayFromZero):0} mi");
    }

    public static string Size(long bytes) => bytes switch
    {
        >= 100_000_000 => string.Create(C, $"{bytes / 1e9:0.#} GB"),
        >= 1_000_000 => string.Create(C, $"{Math.Round(bytes / 1e6, MidpointRounding.AwayFromZero):0} MB"),
        >= 1_000 => string.Create(C, $"{Math.Round(bytes / 1e3, MidpointRounding.AwayFromZero):0} KB"),
        _ => string.Create(C, $"{bytes} B"),
    };

    public static string ClipLength(TimeSpan t)
    {
        var total = (long)Math.Floor(t.TotalSeconds);
        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return h > 0 ? string.Create(C, $"{h}:{m:00}:{s:00}") : string.Create(C, $"{m}:{s:00}");
    }

    public static string Day(DateOnly d) => d.ToString("MMM d", C);
    public static string DayWithWeekday(DateOnly d) => d.ToString("MMM d (ddd)", C);
    public static string DayYear(DateOnly d) => d.ToString("MMM d, yyyy", C);

    public static string DateRange(DateOnly a, DateOnly b)
    {
        if (a == b) return Day(a);
        if (a.Year != b.Year) return $"{DayYear(a)} – {DayYear(b)}";
        if (a.Month == b.Month) return string.Create(C, $"{Day(a)}–{b.Day}");
        return $"{Day(a)} – {Day(b)}";
    }

    public static string Gap(TimeSpan t)
    {
        if (t < TimeSpan.FromHours(1)) return string.Create(C, $"{Math.Max(0, Math.Round(t.TotalMinutes)):0} min");
        if (t < TimeSpan.FromHours(48)) return string.Create(C, $"{Math.Round(t.TotalHours):0} h");
        return Count((int)Math.Round(t.TotalDays), "day", "days");
    }

    public static string Offset(TimeSpan o)
    {
        var sign = o < TimeSpan.Zero ? "−" : "+";
        var a = o.Duration();
        return a.Minutes == 0
            ? string.Create(C, $"UTC{sign}{(int)a.TotalHours}")
            : string.Create(C, $"UTC{sign}{(int)a.TotalHours}:{a.Minutes:00}");
    }

    public static string Clock(DateTime local) => local.ToString("HH:mm", C);

    public static string Count(int n, string one, string many) => string.Create(C, $"{n} {(n == 1 ? one : many)}");

    public static string Serial(uint serial)
    {
        var hex = serial.ToString("X8", C);
        return $"{hex[..4]}-{hex[4..]}";
    }

    public static string ModelName(string? model) => model switch
    {
        null => "DJI drone",
        _ when model.StartsWith("FC9113", StringComparison.OrdinalIgnoreCase) => "DJI Air 3S",
        _ => model,
    };

    public static string CardChip(string root, CardIdentity id, string? model, int files, long bytes)
        => $"{root} · {ModelName(model)} · serial {Serial(id.VolumeSerial)} · {Count(files, "file", "files")} · {Size(bytes)}";

    public static string Drive(string path) => (Path.GetPathRoot(path) ?? path).TrimEnd('\\');
}
