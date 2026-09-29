// tests/UasSort.Testing/PlanFingerprint.cs
using System.Globalization;
using System.Text;

namespace UasSort.Testing;

/// <summary>A revision-independent text form of a plan, for "undo then redo gives the same plan" and draft replay.</summary>
public static class PlanFingerprint
{
    public static string Of(Plan p)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"T:{p.Tuning.RadiusMiles}/{p.Tuning.GapDays}\n");
        foreach (var g in p.Groups)
            sb.Append(string.Join(",", g.Videos.Select(v => v.CardRelPath))).Append(" | ").Append(Target(g.Target))
              .Append(" | ").Append(g.Description).Append(" | ").Append(g.Foldable).Append('\n');
        foreach (var b in p.Boundaries) sb.Append(b.Cause).Append(';');
        sb.Append("\nI:").Append(string.Join(",", p.Included.Select(i => i.CardRelPath).Order(StringComparer.Ordinal)));
        sb.Append("\nX:").Append(string.Join(",", p.Issues.Select(i => $"{i.Code}/{i.Severity}@{i.Anchor?.CardRelPath}").Order(StringComparer.Ordinal)));
        return sb.ToString();
    }

    private static string Target(GroupTarget t) => t switch
    {
        NewFolder n => "N:" + n.RelPath,
        Append a => $"A:{a.Folder.FullPath}:{a.Confidence}:{a.Why}",
        AlreadyImported ai => "I:" + ai.Folder.FullPath,
        NothingToCopy n => "0:" + n.Summary,
        SkipGroup => "S",
    };
}
