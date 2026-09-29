// src/UasSort.Core/Planning/Clusterer.cs
using System.Collections.Immutable;

namespace UasSort.Core.Planning;

/// <summary>Ref §8.2 auto clustering + §8.9 structural edits + caused boundaries. Pure.</summary>
public static class Clusterer
{
    public static readonly IComparer<Item> Order = Comparer<Item>.Create((a, b) =>
    {
        var c = a.Time.CaptureUtc.CompareTo(b.Time.CaptureUtc);
        return c != 0 ? c : string.CompareOrdinal(a.Raw.Unit.Id.CardRelPath, b.Raw.Unit.Id.CardRelPath);
    });

    public static ClusterResult Cluster(IReadOnlyList<Item> videos, Tuning t, IReadOnlyList<PlanEdit> structuralEdits, out int missingEdits)
    {
        var ordered = videos.ToList();
        ordered.Sort(Order);
        var parts = AutoPartition(ordered, t);
        var userSplit = new HashSet<ItemId>();
        missingEdits = ApplyEdits(parts, ordered, structuralEdits, userSplit);
        return Build(parts, userSplit, t);
    }

    internal static LibraryFolderRef? FolderOf(Item i) => i.Newness is Imported { Folder: { } f } ? f : null;

    internal static bool SameFolder(LibraryFolderRef a, LibraryFolderRef b) =>
        string.Equals(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);

    internal static bool TimeSplit(Item prev, Item x, Tuning t) =>
        x.Time.LocalDate.DayNumber - prev.Time.LocalDate.DayNumber > t.GapDays
        && x.Time.CaptureUtc - prev.Time.CaptureUtc > PlanningGeo.HoursGuard;

    internal static LibraryFolderRef? WallOf(IEnumerable<Item> items)
    {
        foreach (var i in items) if (FolderOf(i) is { } f) return f;
        return null;
    }

    internal static GeoPoint? CentroidOf(IEnumerable<Item> items) =>
        PlanningGeo.Centroid(items.Where(i => i.Gps is not null).Select(i => i.Gps!.Point));

    private static bool SameSession(Item a, Item b) => a.Session is { } sa && b.Session is { } sb && sa.SameSession(sb);

    private sealed class Acc
    {
        private readonly List<GeoPoint> _points = [];
        private GeoPoint? _cached;
        public void Add(Item i) { if (i.Gps is { } g) { _points.Add(g.Point); _cached = null; } }
        public GeoPoint? Get() => _cached ??= PlanningGeo.Centroid(_points);
    }

    private static List<List<Item>> AutoPartition(List<Item> ordered, Tuning t)
    {
        var radiusM = Distance.FromMiles(t.RadiusMiles).Meters;
        var groups = new List<List<Item>>();
        List<Item>? cur = null;
        var acc = new Acc();
        var tail = new List<Item>();                 // no-GPS items after the group's last GPS item
        Item? lastGps = null;

        void Start(Item x)
        {
            cur = [x];
            groups.Add(cur);
            acc = new Acc();
            acc.Add(x);
            tail = x.Gps is null ? [x] : [];
            lastGps = x.Gps is null ? null : x;
        }

        foreach (var x in ordered)
        {
            if (cur is null) { Start(x); continue; }
            var prev = cur[^1];
            if (TimeSplit(prev, x, t)) { Start(x); continue; }
            if (FolderOf(x) is { } fx && cur.Any(i => FolderOf(i) is { } f && !SameFolder(f, fx))) { Start(x); continue; }
            if (x.Gps is not null && acc.Get() is { } c
                && !cur.Any(m => m.Gps is not null && SameSession(m, x))
                && PlanningGeo.Haversine(x.Gps.Point, c).Meters > radiusM)
            {
                var moved = new List<Item>();
                foreach (var ti in tail)
                {
                    bool move;
                    if (SameSession(ti, x)) move = true;
                    else if (lastGps is not null && SameSession(ti, lastGps)) move = false;
                    else move = lastGps is null || x.Time.CaptureUtc - ti.Time.CaptureUtc < ti.Time.CaptureUtc - lastGps.Time.CaptureUtc;
                    if (move) moved.Add(ti);
                }
                foreach (var m in moved) cur.Remove(m);
                if (cur.Count == 0) groups.Remove(cur);
                var next = new List<Item>(moved) { x };
                next.Sort(Order);
                cur = next;
                groups.Add(cur);
                acc = new Acc();
                acc.Add(x);                          // moved items have no GPS
                tail = [];
                lastGps = x;
                continue;
            }
            cur.Add(x);
            acc.Add(x);
            if (x.Gps is not null) { tail = []; lastGps = x; }
            else tail.Add(x);
        }
        return groups;
    }

    private static int ApplyEdits(List<List<Item>> parts, List<Item> ordered, IReadOnlyList<PlanEdit> edits, HashSet<ItemId> userSplit)
    {
        var byId = ordered.ToDictionary(i => i.Raw.Unit.Id);
        var missing = 0;
        int Find(ItemId id) => parts.FindIndex(p => p.Exists(i => i.Raw.Unit.Id == id));

        foreach (var e in edits)
        {
            if (!PlanEditRefs.IsStructural(e)) continue;
            if (PlanEditRefs.Referenced(e).Any(r => !byId.ContainsKey(r))) { missing++; continue; }
            switch (e)
            {
                case SplitBefore s:
                {
                    var x = byId[s.First];
                    var g = parts[Find(s.First)];
                    var after = g.Where(i => Order.Compare(i, x) >= 0).ToList();
                    if (after.Count < g.Count)
                    {
                        g.RemoveAll(after.Contains);
                        parts.Add(after);
                    }
                    userSplit.Add(s.First);
                    break;
                }
                case Merge m:
                {
                    int a = Find(m.InA), b = Find(m.InB);
                    if (a == b) break;
                    int lo = Math.Min(a, b), hi = Math.Max(a, b);
                    var range = parts.GetRange(lo, hi - lo + 1);
                    var walls = range.Select(WallOf).Where(w => w is not null)
                                     .Select(w => w!.FullPath.ToUpperInvariant()).Distinct().Count();
                    if (walls > 1) break;                                  // inactive: MergeAcrossLibraryFolders
                    foreach (var p in range.Skip(1)) userSplit.Remove(p[0].Raw.Unit.Id);
                    var merged = range.SelectMany(p => p).ToList();
                    parts.RemoveRange(lo, hi - lo + 1);
                    parts.Insert(lo, merged);
                    break;
                }
                case MoveToNewGroup mv:
                {
                    var items = mv.Items.Distinct().Select(id => byId[id]).ToList();
                    if (items.Count == 0 || items.Exists(i => i.Newness is Imported)) break;
                    foreach (var p in parts) p.RemoveAll(items.Contains);
                    parts.Add(items);
                    break;
                }
                case MoveToGroup mt:
                {
                    if (mt.Items.Contains(mt.InTarget)) break;
                    var items = mt.Items.Distinct().Select(id => byId[id]).ToList();
                    if (items.Count == 0 || items.Exists(i => i.Newness is Imported)) break;
                    var target = parts[Find(mt.InTarget)];
                    foreach (var p in parts) p.RemoveAll(items.Contains);
                    target.AddRange(items);
                    break;
                }
            }
            Normalize(parts);
        }
        return missing;
    }

    private static void Normalize(List<List<Item>> parts)
    {
        parts.RemoveAll(p => p.Count == 0);
        foreach (var p in parts) p.Sort(Order);
        parts.Sort((a, b) => Order.Compare(a[0], b[0]));
    }

    private static ClusterResult Build(List<List<Item>> parts, HashSet<ItemId> userSplit, Tuning t)
    {
        Normalize(parts);
        var radiusM = Distance.FromMiles(t.RadiusMiles).Meters;
        var drafts = ImmutableArray.CreateBuilder<GroupDraft>(parts.Count);
        var bounds = ImmutableArray.CreateBuilder<Boundary>(Math.Max(0, parts.Count - 1));
        GeoPoint? prevC = null;
        for (var i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            var c = CentroidOf(p);
            var id = new GroupId(p[0].Raw.Unit.Id);
            GroupId? neighbour = null;
            if (i > 0)
            {
                var l = parts[i - 1];
                var cause = CauseOf(l, p, prevC, userSplit, t, radiusM);
                Distance? jump = prevC is { } a && c is { } b ? PlanningGeo.Haversine(a, b) : null;
                bounds.Add(new Boundary(drafts[i - 1].Id, id, cause, jump, p[0].Time.CaptureUtc - l[^1].Time.CaptureUtc,
                                        p[0].Time.LocalDate.DayNumber - l[^1].Time.LocalDate.DayNumber));
                if (cause == BoundaryCause.UserSplit) neighbour = drafts[i - 1].Id;
            }
            drafts.Add(new GroupDraft(id, [.. p], c, p.Min(x => x.Time.LocalDate), p.Max(x => x.Time.LocalDate), WallOf(p), neighbour));
            prevC = c;
        }
        return new ClusterResult(drafts.MoveToImmutable(), bounds.ToImmutable());
    }

    private static BoundaryCause CauseOf(List<Item> l, List<Item> r, GeoPoint? lc, HashSet<ItemId> userSplit, Tuning t, double radiusM)
    {
        if (userSplit.Contains(r[0].Raw.Unit.Id)) return BoundaryCause.UserSplit;
        if (WallOf(l) is { } wl && WallOf(r) is { } wr && !SameFolder(wl, wr)) return BoundaryCause.LibraryFolder;
        if (TimeSplit(l[^1], r[0], t)) return BoundaryCause.DayGap;
        if (r.Find(i => i.Gps is not null) is { } g && lc is { } c && PlanningGeo.Haversine(g.Gps!.Point, c).Meters > radiusM)
            return BoundaryCause.Distance;
        return BoundaryCause.UserSplit;
    }
}
