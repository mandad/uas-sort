using System.Globalization;

namespace UasSort.Cli;

/// <summary>The human-readable plan (Ref §4.5): one block per group, then photo days, sets, other files and issues.</summary>
internal static class PlanTextRenderer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Render(PlanDocument doc, TextWriter w)
    {
        w.WriteLine(string.Create(Inv, $"CARD {doc.Card.Root} · {doc.Card.Model ?? "unknown model"} · {doc.Card.Files} files · inventory {doc.Card.InventoryHash}"));
        w.WriteLine(string.Create(Inv, $"ROOTS video {doc.Settings.VideoRoot} · photo {doc.Settings.PhotoRoot} · R {doc.Settings.RadiusMiles} mi · G {doc.Settings.GapDays}"));
        w.WriteLine(string.Create(Inv, $"CLOCK {doc.Clock.Mode} {doc.Clock.Zone ?? "-"} · {doc.Clock.Samples} samples · mismatch {doc.Clock.Mismatch.Items} ({string.Join(", ", doc.Clock.Mismatch.SiteZones)})"));
        w.WriteLine();

        foreach (var g in doc.Groups)
        {
            w.WriteLine(GroupLine(g));
            if (g.BoundaryBefore is { } b)
                w.WriteLine(string.Create(Inv, $"  boundary before: {b.Cause}{(b.JumpMiles is { } j ? " · " + j.ToString("0.0", Inv) + " mi" : "")} · {b.GapHours:0.#} h · {b.DayGap} days"));
            foreach (var v in g.Videos)
            {
                string flags = v.Flags.Count == 0 ? "" : " [" + string.Join(", ", v.Flags) + "]";
                w.WriteLine(string.Create(Inv, $"  {(v.Included ? "x" : " ")} {FileName(v.Id)}  {v.Status}  {v.LocalDate:yyyy-MM-dd}  {v.TimeSource}{flags}"));
            }
            foreach (var s in g.DaySplits)
                w.WriteLine(string.Create(Inv, $"  -- day change {s.From:yyyy-MM-dd} → {s.To:yyyy-MM-dd} before {FileName(s.FirstOfDay)}{(s.ApartMiles is { } a ? " · " + a.ToString("0.0", Inv) + " mi apart" : "")}{(s.Emphasised ? " (emphasised)" : "")}"));
            w.WriteLine();
        }

        w.WriteLine("PHOTO DAYS");
        if (doc.PhotoDays.Count == 0) w.WriteLine("  (none)");
        foreach (var d in doc.PhotoDays)
            w.WriteLine(string.Create(Inv, $"  {d.Date:yyyy-MM-dd} {d.Tz} · {d.Units} units · {d.New} new · {d.ProbablyImported} probably imported · {d.Reason}"));

        w.WriteLine("SETS");
        if (doc.Sets.Count == 0) w.WriteLine("  (none)");
        foreach (var s in doc.Sets)
            w.WriteLine(string.Create(Inv, $"  {s.Id} → {s.Folder} · {s.Resolution} · {s.Members} members"));

        w.WriteLine("OTHER FILES");
        if (doc.Other.Count == 0) w.WriteLine("  (none)");
        foreach (var o in doc.Other)
            w.WriteLine(string.Create(Inv, $"  {o.RelPath} · {o.Class}{(o.Rule is { } r ? $" ({r})" : "")}"));

        w.WriteLine("ISSUES");
        if (doc.Issues.Count == 0) w.WriteLine("  (none)");
        foreach (var i in doc.Issues)
            w.WriteLine(string.Create(Inv, $"  {i.Severity} {i.Code} · {i.Message}{(i.Anchor is { } a ? " · " + a : "")}{(i.RequiresAck ? " · needs acknowledgement at preflight" : "")}"));
    }

    private static string GroupLine(GroupJson g)
    {
        string label = g.Target switch
        {
            GroupTargetKind.NewFolder => "NEW FOLDER",
            GroupTargetKind.Append => string.Create(Inv, $"APPEND ({g.Confidence})"),
            GroupTargetKind.AlreadyImported => "ALREADY IMPORTED",
            GroupTargetKind.NothingToCopy => "NOTHING TO COPY",
            GroupTargetKind.SkipGroup => "SKIP",
            _ => throw new ArgumentOutOfRangeException(nameof(g), g.Target, "unknown target kind"),
        };
        string head = g.RelPath is { } p ? $"{label} {p}" : label;
        string line = string.Create(Inv, $"{head} · {g.Videos.Count} clips · {FormatDays(g.Start, g.End)}");
        if (g.Issues.Count > 0) line += " · issues: " + string.Join(", ", g.Issues);
        if (g.Why is { } why) line += Environment.NewLine + "  " + why;
        return line;
    }

    public static string FormatDays(DateOnly start, DateOnly end)
    {
        if (start == end) return start.ToString("MMM d", Inv);
        if (start.Year != end.Year) return start.ToString("MMM d, yyyy", Inv) + "–" + end.ToString("MMM d, yyyy", Inv);
        if (start.Month != end.Month) return start.ToString("MMM d", Inv) + "–" + end.ToString("MMM d", Inv);
        return start.ToString("MMM d", Inv) + "–" + end.ToString("%d", Inv);
    }

    private static string FileName(string cardRelPath) => cardRelPath[(cardRelPath.LastIndexOf('/') + 1)..];
}
