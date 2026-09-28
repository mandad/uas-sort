using System.Globalization;
using System.Text.RegularExpressions;

namespace UasSort.Core.Media;

/// <summary>The drone-clock stamp in a DJI file name: <c>DJI_20260927140627_0128_D.MP4</c> → 2026-09-27 14:06:27 (naive).</summary>
public static partial class DroneStampParser
{
    /// <summary>Stamp of a file name or card-relative path ('/' or '\' separators); null when the name has none.</summary>
    public static DateTime? FromFileName(string nameOrRelPath)
    {
        ArgumentNullException.ThrowIfNull(nameOrRelPath);
        Match m = Stamp().Match(FileName(nameOrRelPath));
        return m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                                                   DateTimeStyles.None, out DateTime stamp)
            ? stamp : null;
    }

    /// <summary>The last path segment, splitting on both '/' and '\'.</summary>
    public static string FileName(string nameOrRelPath)
    {
        ArgumentNullException.ThrowIfNull(nameOrRelPath);
        int cut = nameOrRelPath.LastIndexOfAny(['/', '\\']);
        return nameOrRelPath[(cut + 1)..];
    }

    [GeneratedRegex(@"^DJI_(\d{14})_\d{4}_", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Stamp();
}
