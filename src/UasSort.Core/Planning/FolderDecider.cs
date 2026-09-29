// src/UasSort.Core/Planning/FolderDecider.cs
using System.Collections.Immutable;
using UasSort.Core.Library;

namespace UasSort.Core.Planning;

/// <summary>Target choice per group (Ref §8.5) with the two-pass UserSplit rule (Ref §8.9 step 4). One instance per derive (caches).</summary>
public sealed class FolderDecider
{
    private static readonly StringComparer PathCmp = StringComparer.OrdinalIgnoreCase;
    private readonly LibraryIndex _lib;
    private readonly IReadOnlyList<GroupDraft> _all;
    private readonly Tuning _t;
    private readonly Dictionary<string, LibraryFolder> _byPath;
    private readonly Dictionary<(string, string), ImmutableHashSet<DateOnly>> _days = [];
    private readonly Dictionary<string, GeoPoint?> _centroids = new(PathCmp);

    public FolderDecider(LibraryIndex lib, IReadOnlyList<GroupDraft> all, Tuning t)
    {
        _lib = lib;
        _all = all;
        _t = t;
        _byPath = lib.Folders.GroupBy(f => f.Ref.FullPath, PathCmp).ToDictionary(g => g.Key, g => g.First(), PathCmp);
    }

    public static GroupTarget Decide(GroupDraft g, IReadOnlyList<GroupDraft> all, IReadOnlySet<ItemId> included, LibraryIndex lib,
                                     Tuning t, IReadOnlyDictionary<GroupId, GroupTarget>? pass1)
        => new FolderDecider(lib, all, t).Decide(g, included, pass1);

    public static IReadOnlyList<Item> Judged(GroupDraft g, IReadOnlySet<ItemId> included) =>
        g.Videos.Where(v => included.Contains(v.Raw.Unit.Id) && v.Newness is IsNew or Conflict).ToList();

    public static LibraryFolderRef? TargetFolder(GroupTarget t) => t switch
    {
        Append a => a.Folder,
        AlreadyImported ai => ai.Folder,
        _ => null,
    };

    public bool BordersUserSplit(GroupDraft g) => g.UserSplitNeighbour is not null || _all.Any(r => r.UserSplitNeighbour == g.Id);

    public GroupTarget Decide(GroupDraft g, IReadOnlySet<ItemId> included, IReadOnlyDictionary<GroupId, GroupTarget>? pass1)
    {
        var judged = Judged(g, included);
        if (judged.Count == 0)
            return g.Wall is { } w ? new AlreadyImported(w) : new NothingToCopy(NothingToCopySummary(g, included));

        var tz = ZoneOf(g);
        if (g.Wall is { } wall)
        {
            var days = FolderDays(wall, tz);
            if (judged[0].Time.LocalDate < wall.NameDate) return new NewFolder("");
            if (judged.Any(v => days.Contains(v.Time.LocalDate)))
                return new Append(wall, Confidence.High, "same day as clips already in this folder", null);
            var newC = Clusterer.CentroidOf(judged);
            var fc = FolderCentroid(wall);
            Distance? d = newC is { } a && fc is { } b ? PlanningGeo.Haversine(a, b) : null;
            var why = d is { } dd ? $"different day, {PlanText.Miles(dd)} from {wall.Description}" : "different day, location unknown";
            var sp = SplitPoint(g, wall, included);
            var hint = sp is { } p
                ? new CrossDayHint($"Different day from '{wall.Description}' ({PlanText.ShortDate(days.Min())})"
                                   + (d is { } x ? $" · {PlanText.Miles(x)}" : ""), [new SplitBefore(p)])
                : null;
            return new Append(wall, Confidence.Medium, why, hint);
        }

        var excluded = Excluded(g, pass1);
        var best = Candidates(g, judged, tz, excluded).OrderBy(c => c.DateGap).ThenBy(c => c.Dist).FirstOrDefault();
        return best.Target ?? new NewFolder("");
    }

    public IReadOnlyList<LibraryFolder> AppendCandidates(GroupDraft g, int max)
    {
        var tz = ZoneOf(g);
        var list = new List<(LibraryFolder F, int Gap, double Dist)>();
        foreach (var f in _lib.Folders)
        {
            var isWall = g.Wall is { } w && PathCmp.Equals(w.FullPath, f.Ref.FullPath);
            var days = FolderDays(f.Ref, tz);
            var fEnd = days.Max();
            if (!isWall && !(f.Ref.NameDate <= g.Start && g.Start <= fEnd.AddDays(_t.GapDays))) continue;
            var fc = FolderCentroid(f.Ref);
            var dist = g.Centroid is { } a && fc is { } b ? PlanningGeo.Haversine(a, b).Meters : double.PositiveInfinity;
            list.Add((f, isWall ? -1 : Math.Max(0, g.Start.DayNumber - fEnd.DayNumber), dist));
        }
        return list.OrderBy(x => x.Gap).ThenBy(x => x.Dist).Select(x => x.F).Take(max).ToList();
    }

    public ItemId? SplitPoint(GroupDraft g, LibraryFolderRef f, IReadOnlySet<ItemId> included)
    {
        var days = FolderDays(f, ZoneOf(g));
        var first = Judged(g, included).FirstOrDefault(v => !days.Contains(v.Time.LocalDate));
        if (first is null) return null;
        ItemId? point = first.Raw.Unit.Id == g.Id.Anchor
            ? g.Videos.FirstOrDefault(v => Clusterer.FolderOf(v) is { } x && Clusterer.SameFolder(x, f))?.Raw.Unit.Id
            : first.Raw.Unit.Id;
        return point is { } p && p != g.Id.Anchor ? p : null;
    }

    public ItemId? NewBeforeWallSplitPoint(GroupDraft g, IReadOnlySet<ItemId> included)
    {
        var judged = Judged(g, included);
        return g.Wall is { } w && judged.Count > 0 && judged[0].Time.LocalDate < w.NameDate ? SplitPoint(g, w, included) : null;
    }

    private IEnumerable<(GroupTarget? Target, int DateGap, double Dist)> Candidates(GroupDraft g, IReadOnlyList<Item> judged, string tz,
                                                                                  HashSet<string> excluded)
    {
        var gs = judged.Min(v => v.Time.LocalDate);
        var newDays = judged.Select(v => v.Time.LocalDate).ToHashSet();
        var gc = Clusterer.CentroidOf(judged) ?? g.Centroid;
        var radiusM = Distance.FromMiles(_t.RadiusMiles).Meters;
        var members = g.Videos.Select(v => v.Raw.Unit.Id).ToImmutableArray();
        foreach (var f in _lib.Folders)
        {
            if (excluded.Contains(f.Ref.FullPath)) continue;
            var days = FolderDays(f.Ref, tz);
            var fEnd = days.Max();
            if (!(f.Ref.NameDate <= gs && gs <= fEnd.AddDays(_t.GapDays))) continue;   // never auto-append earlier clips
            var overlap = newDays.Overlaps(days);
            var gap = Math.Max(0, gs.DayNumber - fEnd.DayNumber);
            if (gc is { } a && FolderCentroid(f.Ref) is { } b)
            {
                var d = PlanningGeo.Haversine(a, b);
                if (d.Meters > radiusM) continue;
                if (overlap)
                    yield return (new Append(f.Ref, Confidence.High, $"same dates, {PlanText.Miles(d)}", null), 0, d.Meters);
                else if (d.Miles < PlanningGeo.NearMiles)
                    yield return (new Append(f.Ref, Confidence.High, $"next day, {PlanText.Miles(d)}", null), gap, d.Meters);
                else
                    yield return (new Append(f.Ref, Confidence.Medium, $"different day, {PlanText.Miles(d)}",
                        new CrossDayHint($"Different day from '{f.Ref.Description}' ({PlanText.ShortDate(fEnd)}) · {PlanText.Miles(d)}",
                                         [new Retarget(g.Id.Anchor, new NewFolderTarget(), false, members)])), gap, d.Meters);
            }
            else if (overlap)
                yield return (new Append(f.Ref, Confidence.Medium, "same dates, location unknown", null), 0, double.PositiveInfinity);
        }
    }

    /// <summary>Folders the UserSplit rule excludes for <paramref name="g"/> in pass 2 (Ref §8.9 step 4); empty when <paramref name="pass1"/> is null.</summary>
    internal HashSet<string> Excluded(GroupDraft g, IReadOnlyDictionary<GroupId, GroupTarget>? pass1)
    {
        var set = new HashSet<string>(PathCmp);
        if (pass1 is null) return set;
        if (g.UserSplitNeighbour is { } left && _all.FirstOrDefault(x => x.Id == left) is { } ld)
        {
            if (ld.Wall is { } w) set.Add(w.FullPath);
            if (pass1.TryGetValue(left, out var lt) && TargetFolder(lt) is { } f) set.Add(f.FullPath);
        }
        foreach (var r in _all)
            if (r.UserSplitNeighbour == g.Id && r.Wall is { } w2) set.Add(w2.FullPath);
        if (g.Wall is { } own) set.Remove(own.FullPath);
        return set;
    }

    private static string NothingToCopySummary(GroupDraft g, IReadOnlySet<ItemId> included)
    {
        bool Inc(Item v) => included.Contains(v.Raw.Unit.Id);
        var parts = new List<string>();
        var conflicts = g.Videos.Count(v => v.Newness is Conflict && !Inc(v));
        var dismissed = g.Videos.Count(v => v.Newness is Decided);
        var unticked = g.Videos.Count(v => v.Newness is IsNew && !Inc(v));
        var imported = g.Videos.Count(v => v.Newness is Imported);
        if (conflicts > 0) parts.Add(PlanText.Count(conflicts, "conflict", "conflicts"));
        if (dismissed > 0) parts.Add(PlanText.Count(dismissed, "dismissed", "dismissed"));
        if (unticked > 0) parts.Add(PlanText.Count(unticked, "unticked", "unticked"));
        if (imported > 0) parts.Add(PlanText.Count(imported, "already imported", "already imported"));
        return "nothing to copy: " + string.Join(", ", parts);
    }

    private static string ZoneOf(GroupDraft g) =>
        g.Videos.FirstOrDefault(v => v.Time.TzSource == TzSource.Gps)?.Time.TzId ?? g.Videos[0].Time.TzId;

    private ImmutableHashSet<DateOnly> FolderDays(LibraryFolderRef r, string tz)
    {
        var key = (r.FullPath.ToUpperInvariant(), tz);
        if (_days.TryGetValue(key, out var d)) return d;
        d = _byPath.TryGetValue(r.FullPath, out var f) ? f.DaysIn(tz).Add(r.NameDate) : [r.NameDate];
        _days[key] = d;
        return d;
    }

    private GeoPoint? FolderCentroid(LibraryFolderRef r)
    {
        if (_centroids.TryGetValue(r.FullPath, out var c)) return c;
        c = _byPath.TryGetValue(r.FullPath, out var f) && f.Centroid is { } lc
            ? lc
            : Clusterer.CentroidOf(_all.SelectMany(g => g.Videos).Where(v => Clusterer.FolderOf(v) is { } x && Clusterer.SameFolder(x, r)));
        _centroids[r.FullPath] = c;
        return c;
    }
}
