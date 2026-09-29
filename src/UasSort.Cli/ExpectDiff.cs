using System.Globalization;

namespace UasSort.Cli;

/// <summary>tests/acceptance/first-card-expected.json (schema: tests/acceptance/first-card-expected.schema.json).</summary>
internal sealed record ExpectedFileJson(IReadOnlyList<ExpectedFolderJson> Folders);

/// <summary>One folder the user expects: its path relative to the video root, its clip file names, optionally the target kind.</summary>
internal sealed record ExpectedFolderJson(string RelPath, IReadOnlyList<string> Clips, GroupTargetKind? Target);

internal enum ExpectDifferenceKind { Merge, SplitOrMove, RenameOrRetarget, NotOnCard, ListedTwice }

internal sealed record ExpectDifference(ExpectDifferenceKind Kind, string Subject, string Detail, int Edits);

internal sealed record ExpectReport(int Edits, IReadOnlyList<ExpectDifference> Differences)
{
    /// <summary>Acceptance step 2 passes at this many edits or fewer (Ref §4.5, §13).</summary>
    public const int PassAt = 2;
    public bool Passes => Edits <= PassAt;
}

/// <summary>The edit count between the dry-run plan and the user's expected folder list (Ref §4.5).</summary>
internal static class ExpectDiff
{
    public static ExpectReport Compare(ExpectedFileJson expected, IReadOnlyList<GroupJson> groups)
    {
        var diffs = new List<ExpectDifference>();
        int edits = 0;

        var groupOfClip = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int gi = 0; gi < groups.Count; gi++)
            foreach (var v in groups[gi].Videos)
                groupOfClip.TryAdd(FileName(v.Id), gi);

        var folderOfClip = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int fi = 0; fi < expected.Folders.Count; fi++)
            foreach (var clip in expected.Folders[fi].Clips)
                if (!folderOfClip.TryAdd(clip, fi))
                    diffs.Add(new(ExpectDifferenceKind.ListedTwice, clip,
                        $"listed in {expected.Folders[folderOfClip[clip]].RelPath} and in {expected.Folders[fi].RelPath}; the first counts", 0));

        for (int fi = 0; fi < expected.Folders.Count; fi++)
        {
            var folder = expected.Folders[fi];
            var present = new List<string>();
            foreach (var clip in folder.Clips.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (groupOfClip.ContainsKey(clip)) present.Add(clip);
                else diffs.Add(new(ExpectDifferenceKind.NotOnCard, clip, $"expected in {folder.RelPath}, not on the card", 0));
            }
            if (present.Count == 0) continue;

            var groupIndexes = present.Select(c => groupOfClip[c]).Distinct().Order().ToList();
            if (groupIndexes.Count > 1)
            {
                int merges = groupIndexes.Count - 1;
                edits += merges;
                diffs.Add(new(ExpectDifferenceKind.Merge, folder.RelPath,
                    $"its clips are in {groupIndexes.Count} plan groups ({string.Join(", ", groupIndexes.Select(i => groups[i].Id))})", merges));
            }

            int matched = groupIndexes.OrderByDescending(gi => present.Count(c => groupOfClip[c] == gi)).ThenBy(gi => gi).First();
            var g = groups[matched];
            bool pathDiffers = !string.Equals(NormalizePath(g.RelPath), NormalizePath(folder.RelPath), StringComparison.OrdinalIgnoreCase);
            bool kindDiffers = folder.Target is { } kind && kind != g.Target;
            if (pathDiffers || kindDiffers)
            {
                edits += 1;
                diffs.Add(new(ExpectDifferenceKind.RenameOrRetarget, folder.RelPath,
                    $"the plan proposes {g.Target} {g.RelPath ?? "(no folder)"}", 1));
            }
        }

        foreach (var g in groups)
        {
            var folders = g.Videos.Select(v => FileName(v.Id)).Where(folderOfClip.ContainsKey)
                                  .Select(c => folderOfClip[c]).Distinct().Order().ToList();
            if (folders.Count > 1)
            {
                int splits = folders.Count - 1;
                edits += splits;
                diffs.Add(new(ExpectDifferenceKind.SplitOrMove, g.Id,
                    $"plan group {g.RelPath ?? g.Id} holds clips of {folders.Count} expected folders ({string.Join(", ", folders.Select(i => expected.Folders[i].RelPath))})", splits));
            }
        }

        return new ExpectReport(edits, diffs);
    }

    public static void Write(ExpectReport report, TextWriter w)
    {
        foreach (var d in report.Differences)
        {
            string label = d.Kind switch
            {
                ExpectDifferenceKind.Merge => "merge",
                ExpectDifferenceKind.SplitOrMove => "split/move",
                ExpectDifferenceKind.RenameOrRetarget => "rename/retarget",
                ExpectDifferenceKind.NotOnCard => "not on card",
                ExpectDifferenceKind.ListedTwice => "listed twice",
                _ => throw new ArgumentOutOfRangeException(nameof(report), d.Kind, "unknown difference"),
            };
            string edits = d.Edits == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $" ({d.Edits} edit{(d.Edits == 1 ? "" : "s")})");
            w.WriteLine($"EXPECT {label}: {d.Subject} — {d.Detail}{edits}");
        }
        w.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"EXPECT edits: {report.Edits} (passes at <= {ExpectReport.PassAt}): {(report.Passes ? "PASS" : "FAIL")}"));
    }

    private static string FileName(string cardRelPath) => cardRelPath[(cardRelPath.LastIndexOf('/') + 1)..];

    private static string? NormalizePath(string? p) => p?.Replace('/', '\\').Trim().TrimEnd('\\');
}
