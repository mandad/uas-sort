// src/UasSort.Core/Naming/FolderNamer.cs
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UasSort.Core.Naming;

/// <summary>Folder paths and description cleaning (Ref §8.6), conflict names (Ref §7.4).</summary>
public static partial class FolderNamer
{
    public const int MaxDescription = 80;
    public const int MaxTempPath = 400;
    public const string TempSuffix = ".uas-sort.tmp";
    public const string FolderExistsWhy = "Folder exists; appending";

    private const string Reserved = "<>:\"/\\|?*";

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    public static string Clean(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw) sb.Append(ch < 0x20 || Reserved.Contains(ch) ? ' ' : ch);
        var s = Whitespace().Replace(sb.ToString(), " ").Trim().TrimEnd('.', ' ');
        if (s.Length <= MaxDescription) return s;
        var cut = new StringBuilder(MaxDescription);
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext())
        {
            var el = e.GetTextElement();
            if (cut.Length + el.Length > MaxDescription) break;
            cut.Append(el);
        }
        return cut.ToString().TrimEnd('.', ' ');
    }

    public static string NewFolderRel(DateOnly start, string description)
    {
        var d = Clean(description);
        var leaf = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + (d.Length > 0 ? " " + d : "");
        return $@"{start.ToString("yyyy", CultureInfo.InvariantCulture)}\{start.ToString("yyyy-MM", CultureInfo.InvariantCulture)}\{leaf}";
    }

    public static string TempPath(string finalPath) => finalPath + TempSuffix;

    public static string ConflictName(string fileName, Func<string, bool> taken)
    {
        var dot = fileName.LastIndexOf('.');
        var (stem, ext) = dot <= 0 ? (fileName, "") : (fileName[..dot], fileName[dot..]);
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem} ({n.ToString(CultureInfo.InvariantCulture)}){ext}";
            if (!taken(candidate)) return candidate;
        }
    }

    /// <summary>The JPG twin follows its DNG's (n) name: "X (2).DNG" + "X.JPG" → "X (2).JPG" (Ref §7.3 rule 2b).</summary>
    public static string TwinName(string conflictPrimaryName, string twinName)
    {
        var pd = conflictPrimaryName.LastIndexOf('.');
        var td = twinName.LastIndexOf('.');
        return (pd <= 0 ? conflictPrimaryName : conflictPrimaryName[..pd]) + (td < 0 ? "" : twinName[td..]);
    }
}
