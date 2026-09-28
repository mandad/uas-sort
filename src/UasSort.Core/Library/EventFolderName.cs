using System.Globalization;
using System.Text.RegularExpressions;

namespace UasSort.Core.Library;

/// <summary>An event folder's name (Ref §7.1): YYYY-MM-DD, optionally followed by whitespace and a description.</summary>
public static partial class EventFolderName
{
    [GeneratedRegex(@"^([0-9]{4})-([0-9]{2})-([0-9]{2})(?:\s+(.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool TryParse(string folderName, out DateOnly date, out string description)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        date = default;
        description = "";
        Match m = Pattern().Match(folderName);
        if (!m.Success) return false;
        int y = int.Parse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        int mo = int.Parse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        int d = int.Parse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        if (y < 1 || mo is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo)) return false;
        date = new DateOnly(y, mo, d);
        description = m.Groups[4].Success ? m.Groups[4].Value.Trim() : "";
        return true;
    }
}
