// src/UasSort.Core/Planning/DaySplitFinder.cs
using System.Collections.Immutable;

namespace UasSort.Core.Planning;

/// <summary>Day-split suggestions inside a group (Ref §8.3).</summary>
public static class DaySplitFinder
{
    public static ImmutableArray<DaySplit> Find(IReadOnlyList<Item> groupVideos)
    {
        var v = groupVideos.ToList();
        v.Sort(Clusterer.Order);
        var dayCentroids = v.Where(i => i.Gps is not null)
                            .GroupBy(i => i.Time.LocalDate)
                            .ToDictionary(g => g.Key, g => PlanningGeo.Centroid(g.Select(i => i.Gps!.Point)));
        var result = ImmutableArray.CreateBuilder<DaySplit>();
        for (var i = 1; i < v.Count; i++)
        {
            DateOnly from = v[i - 1].Time.LocalDate, to = v[i].Time.LocalDate;
            if (from == to) continue;
            Distance? apart = dayCentroids.GetValueOrDefault(from) is { } a && dayCentroids.GetValueOrDefault(to) is { } b
                ? PlanningGeo.Haversine(a, b) : null;
            result.Add(new DaySplit(v[i].Raw.Unit.Id, from, to, apart, v[i].Time.CaptureUtc - v[i - 1].Time.CaptureUtc,
                                    apart is { } d && d.Miles >= PlanningGeo.NearMiles));
        }
        return result.ToImmutable();
    }
}
