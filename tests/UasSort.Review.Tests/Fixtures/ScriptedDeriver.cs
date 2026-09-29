// tests/UasSort.Review.Tests/Fixtures/ScriptedDeriver.cs
namespace UasSort.Review.Tests;

/// <summary>
/// A small, deterministic IPlanDeriver for VM tests: consecutive-clip clustering by G, R and walls, the four structural
/// edits, inclusion, pins, day splits, and the issues the VMs react to. It is not the Planner; it only has to produce
/// realistic Plans so the real PlanSession (Part 06) can validate edits against them.
/// </summary>
internal sealed class ScriptedDeriver(ImmutableArray<Suggestion> suggestions = default) : IPlanDeriver
{
    public Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var videos = b.Items.Where(i => i.Raw.Kind == ItemKind.Video)
                            .OrderBy(i => i.Time.CaptureUtc).ThenBy(i => i.Raw.Unit.Id.CardRelPath, StringComparer.Ordinal).ToList();
        var groups = new List<List<Item>>();
        foreach (var x in videos)
        {
            if (groups.Count == 0) { groups.Add([x]); continue; }
            var cur = groups[^1];
            var prev = cur[^1];
            var dayGap = x.Time.LocalDate.DayNumber - prev.Time.LocalDate.DayNumber > t.GapDays
                         && x.Time.CaptureUtc - prev.Time.CaptureUtc > TimeSpan.FromHours(3);
            var far = x.Gps is { } gx && Centroid(cur) is { } c && GeoMath.Haversine(gx.Point, c).Miles > t.RadiusMiles;
            var wall = Folder(x) is { } fx && cur.Select(Folder).FirstOrDefault(f => f is not null) is { } fc
                       && !string.Equals(fx.FullPath, fc.FullPath, StringComparison.OrdinalIgnoreCase);
            if (dayGap || far || wall) groups.Add([x]); else cur.Add(x);
        }

        var userSplits = new HashSet<ItemId>();
        foreach (var e in edits)
        {
            switch (e)
            {
                case SplitBefore s:
                    var gi = groups.FindIndex(g => g.Exists(i => i.Raw.Unit.Id == s.First));
                    if (gi < 0) break;
                    var at = groups[gi].FindIndex(i => i.Raw.Unit.Id == s.First);
                    if (at > 0) { groups.Insert(gi + 1, groups[gi][at..]); groups[gi] = groups[gi][..at]; }
                    userSplits.Add(s.First);
                    break;
                case Merge m:
                    var a = groups.FindIndex(g => g.Exists(i => i.Raw.Unit.Id == m.InA));
                    var z = groups.FindIndex(g => g.Exists(i => i.Raw.Unit.Id == m.InB));
                    if (a < 0 || z < 0 || a == z) break;
                    var (lo, hi) = a < z ? (a, z) : (z, a);
                    var merged = groups.Skip(lo).Take(hi - lo + 1).SelectMany(g => g).ToList();
                    groups.RemoveRange(lo, hi - lo + 1);
                    groups.Insert(lo, merged);
                    foreach (var i in merged) userSplits.Remove(i.Raw.Unit.Id);
                    break;
                case MoveToNewGroup mv:
                    var moving = groups.SelectMany(g => g).Where(i => mv.Items.Contains(i.Raw.Unit.Id)).ToList();
                    foreach (var g in groups) g.RemoveAll(i => mv.Items.Contains(i.Raw.Unit.Id));
                    groups.Add(moving);
                    if (moving.Count > 0) userSplits.Add(moving.OrderBy(i => i.Time.CaptureUtc).First().Raw.Unit.Id);
                    break;
                case MoveToGroup mt:
                    var target = groups.Find(g => g.Exists(i => i.Raw.Unit.Id == mt.InTarget));
                    if (target is null || mt.Items.Contains(mt.InTarget)) break;
                    var mov = groups.SelectMany(g => g).Where(i => mt.Items.Contains(i.Raw.Unit.Id)).ToList();
                    foreach (var g in groups) g.RemoveAll(i => mt.Items.Contains(i.Raw.Unit.Id));
                    target.AddRange(mov);
                    break;
            }
        }
        groups.RemoveAll(g => g.Count == 0);
        foreach (var g in groups) g.Sort((p, q) => p.Time.CaptureUtc.CompareTo(q.Time.CaptureUtc));
        groups.Sort((p, q) => p[0].Time.CaptureUtc.CompareTo(q[0].Time.CaptureUtc));

        var included = b.Items.Where(i => i.Newness is IsNew && !i.Flags.HasFlag(ItemFlags.Truncated))
                              .Select(i => i.Raw.Unit.Id).ToHashSet();
        foreach (var e in edits)
        {
            if (e is SetIncluded si) foreach (var id in si.Items) { if (si.Included) included.Add(id); else included.Remove(id); }
            if (e is SetDayIncluded sd)
                foreach (var i in b.Items.Where(i => i.Raw.Kind != ItemKind.Video && i.Time.LocalDate == sd.Day))
                    if (sd.Included) included.Add(i.Raw.Unit.Id); else included.Remove(i.Raw.Unit.Id);
        }

        var issues = new List<Issue>();
        var result = new List<VideoGroup>();
        var color = 0;
        foreach (var g in groups)
        {
            var anchor = g[0].Raw.Unit.Id;
            var ids = g.Select(i => i.Raw.Unit.Id).ToImmutableArray();
            var rename = edits.OfType<Rename>().LastOrDefault(r => ids.Contains(r.InGroup));
            var retarget = edits.OfType<Retarget>().LastOrDefault(r => ids.Contains(r.InGroup));
            var pinned = retarget?.PinnedMembers ?? rename?.PinnedMembers;
            if (pinned is { } pm && !pm.ToHashSet().SetEquals(ids))
                issues.Add(new Issue(IssueSeverity.Warning, IssueCode.PinMembershipChanged,
                    string.Create(CultureInfo.InvariantCulture, $"'{rename?.Description ?? "target"}' chosen for {pm.Length} clips; group now has {ids.Length}"),
                    anchor, [], true));
            var wall = g.Select(Folder).FirstOrDefault(f => f is not null);
            var anyIncluded = ids.Any(included.Contains);
            var (desc, src) = rename?.Description is { } d ? (d, DescSource.User)
                            : suggestions.IsDefaultOrEmpty ? ("", DescSource.None) : (suggestions[0].Text, DescSource.Feature);
            var start = g.Min(i => i.Time.LocalDate);
            var end = g.Max(i => i.Time.LocalDate);
            GroupTarget target = retarget?.Choice switch
            {
                SkipTarget => new SkipGroup(),
                AppendTo ap => new Append(new LibraryFolderRef(ap.FolderFullPath, start, Path.GetFileName(ap.FolderFullPath)), Confidence.High, "chosen by you", null),
                _ when anyIncluded => new NewFolder(NewRel(start, desc)),
                _ when wall is not null => new AlreadyImported(wall),
                _ => new NothingToCopy("nothing to copy"),
            };
            var foldable = target is AlreadyImported && !g.Exists(i => i.Newness is Conflict || i.Flags.HasFlag(ItemFlags.Truncated));
            if (target is NewFolder && desc.Length == 0)
                issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.EmptyFolderName, "Name this folder", anchor, [new QuickFix("Name it", [])], false));
            var splits = DaySplits(g);
            foreach (var s in splits.Where(s => s.Emphasised))
                issues.Add(new Issue(IssueSeverity.Warning, IssueCode.EmphasisedDaySplit, "likely separate outing", s.FirstOfDay,
                                     [new QuickFix("Split here", [new SplitBefore(s.FirstOfDay)])], true));
            var c = Centroid(g);
            result.Add(new VideoGroup(new GroupId(anchor), ids, c,
                new Distance(c is { } cc ? g.Where(i => i.Gps is not null).Max(i => GeoMath.Haversine(i.Gps!.Point, cc).Meters) : 0),
                start, end, wall, target, null, target is AlreadyImported or Append ? (target is Append ap2 ? ap2.Folder.Description : wall!.Description) : desc,
                target is AlreadyImported or Append ? DescSource.ExistingFolder : src, target is NewFolder, null,
                suggestions.IsDefault ? [] : suggestions, splits, [], foldable, target is AlreadyImported ? -1 : color++ % 10));
        }

        var boundaries = new List<Boundary>();
        for (var k = 1; k < result.Count; k++)
        {
            var (l, r) = (result[k - 1], result[k]);
            var lastL = groups[k - 1][^1];
            var firstR = groups[k][0];
            var cause = userSplits.Contains(r.Id.Anchor) ? BoundaryCause.UserSplit
                      : l.Wall is not null && r.Wall is not null && !string.Equals(l.Wall.FullPath, r.Wall.FullPath, StringComparison.OrdinalIgnoreCase) ? BoundaryCause.LibraryFolder
                      : firstR.Time.LocalDate.DayNumber - lastL.Time.LocalDate.DayNumber > t.GapDays ? BoundaryCause.DayGap
                      : BoundaryCause.Distance;
            Distance? jump = l.Centroid is { } lc && r.Centroid is { } rc ? GeoMath.Haversine(lc, rc) : null;
            boundaries.Add(new Boundary(l.Id, r.Id, cause, jump, firstR.Time.CaptureUtc - lastL.Time.CaptureUtc,
                                        r.Start.DayNumber - l.End.DayNumber));
        }

        if (b.Items.Any(i => i.Flags.HasFlag(ItemFlags.ClockMismatch)))
            issues.Add(new Issue(IssueSeverity.Info, IssueCode.ClockMismatch, "Drone clock differs from local time", null, [], false));
        foreach (var p in b.Scan.Ledger.ParseIssues)
            issues.Add(flags.LedgerIssuesAccepted
                ? new Issue(IssueSeverity.Warning, IssueCode.LedgerParseIssue, $"Ledger line {p.File}:{p.Line} can't be read: {p.Reason}", null, [], true)
                : new Issue(IssueSeverity.Blocking, IssueCode.LedgerParseIssue, $"Ledger line {p.File}:{p.Line} can't be read: {p.Reason}", null,
                            [new QuickFix("Accept and continue", [])], false));

        return new Plan(revision, b, t, [.. result], [.. boundaries], [.. included], [.. issues]);
    }

    private static string NewRel(DateOnly d, string desc)
        => string.Create(CultureInfo.InvariantCulture, $@"{d:yyyy}\{d:yyyy-MM}\{d:yyyy-MM-dd}") + (desc.Length == 0 ? "" : " " + desc);

    private static LibraryFolderRef? Folder(Item i) => i.Newness is Imported im ? im.Folder : null;

    private static GeoPoint? Centroid(IEnumerable<Item> items)
    {
        var pts = items.Where(i => i.Gps is not null).Select(i => i.Gps!.Point).ToList();
        return pts.Count == 0 ? null : new GeoPoint(pts.Average(p => p.Lat), pts.Average(p => p.Lon));
    }

    private static ImmutableArray<DaySplit> DaySplits(List<Item> g)
    {
        var list = new List<DaySplit>();
        for (var k = 1; k < g.Count; k++)
        {
            if (g[k].Time.LocalDate == g[k - 1].Time.LocalDate) continue;
            var from = g[k - 1].Time.LocalDate;
            var to = g[k].Time.LocalDate;
            var a = Centroid(g.Where(i => i.Time.LocalDate == from));
            var z = Centroid(g.Where(i => i.Time.LocalDate == to));
            Distance? apart = a is { } pa && z is { } pz ? GeoMath.Haversine(pa, pz) : null;
            list.Add(new DaySplit(g[k].Raw.Unit.Id, from, to, apart, g[k].Time.CaptureUtc - g[k - 1].Time.CaptureUtc, apart is { Miles: >= 10 }));
        }
        return [.. list];
    }
}
