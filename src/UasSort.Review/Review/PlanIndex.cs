// src/UasSort.Review/Review/PlanIndex.cs
namespace UasSort.Review;

/// <summary>Lookups computed once per derived plan and shared by every card and row.</summary>
public sealed class PlanIndex
{
    private readonly Dictionary<GroupId, Boundary> _before = [];

    public PlanIndex(Plan plan)
    {
        Plan = plan;
        Items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        foreach (var b in plan.Boundaries) _before[b.Right] = b;
    }

    public Plan Plan { get; }
    public IReadOnlyDictionary<ItemId, Item> Items { get; }
    public string VideoRoot => Plan.Base.Scan.Settings.VideoRoot;

    public Boundary? Before(GroupId g) => _before.GetValueOrDefault(g);

    /// <summary>Append candidates for the retarget flyout: exactly the folders Planner.AppendCandidates returns for this group, in its
    /// order (Ref §9.4 item 4; the Planner's own ranking, never a second one here). Only the label and the detail line are Review's.</summary>
    public IReadOnlyList<RetargetOptionVm> Candidates(VideoGroup g)
        => [.. Planner.AppendCandidates(Plan, g.Id)
                .Select(f =>
                {
                    Distance? away = f.Centroid is { } fc && g.Centroid is { } gc ? GeoMath.Haversine(fc, gc) : null;
                    return new RetargetOptionVm(RetargetKind.Append,
                        f.Ref.Description.Length > 0 ? f.Ref.Description : Path.GetFileName(f.Ref.FullPath),
                        $"{Fmt.Day(f.Ref.NameDate)} · {(away is { } d ? Fmt.Miles(d) : "location unknown")}",
                        f.Ref.FullPath, f.Ref.NameDate);
                })];

    public string PhotoCounts(VideoGroup g)
    {
        var days = Plan.Base.PhotoDays.Where(d => d.Date >= g.Start && d.Date <= g.End).SelectMany(d => d.Items).ToList();
        if (days.Count == 0) return "";
        var fresh = days.Count(id => Items.TryGetValue(id, out var i) && i.Newness is IsNew);
        var probably = days.Count(id => Items.TryGetValue(id, out var i) && i.Newness is ProbablyImported);
        return string.Create(CultureInfo.InvariantCulture, $"Photos that day: {fresh} new, {probably} probably imported");
    }
}
