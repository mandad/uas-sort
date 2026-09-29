// src/UasSort.Core/Naming/DescriptionSuggester.cs
using UasSort.Core.Library;
using UasSort.Core.Planning;

namespace UasSort.Core.Naming;

/// <summary>Description suggestions at each local day's centroid (Ref §8.7).</summary>
public static class DescriptionSuggester
{
    public const int Max = 6;

    public static bool Prefills(DescSource s) => s is DescSource.ExistingFolder or DescSource.Ledger or DescSource.Feature or DescSource.Place;

    public static IReadOnlyList<Suggestion> Suggest(GroupDraft g, LibraryIndex lib, IPlaceIndex? places)
    {
        var list = new List<Suggestion>();
        if (g.Wall is { Description.Length: > 0 } w) list.Add(new Suggestion(w.Description, DescSource.ExistingFolder, null, null));

        var ledgerFolders = lib.Folders.Where(f => f.Loc == LocationSource.Ledger && f.Centroid is not null && f.Ref.Description.Length > 0).ToList();
        foreach (var day in g.Videos.Where(v => v.Gps is not null).GroupBy(v => v.Time.LocalDate).OrderBy(d => d.Key))
        {
            var c = PlanningGeo.Centroid(day.Select(v => v.Gps!.Point))!.Value;
            foreach (var (f, d) in ledgerFolders.Select(f => (f, PlanningGeo.Haversine(c, f.Centroid!.Value)))
                                                .Where(x => x.Item2.Miles <= 3).OrderBy(x => x.Item2.Meters))
                list.Add(new Suggestion(f.Ref.Description, DescSource.Ledger, d, day.Key));
            if (places is null) continue;
            foreach (var p in places.Near(c, Distance.FromMiles(1.5), PlaceClass.Feature, 3))
                list.Add(new Suggestion(p.Name, DescSource.Feature, p.Away, day.Key));
            foreach (var p in places.Near(c, Distance.FromMiles(3), PlaceClass.Populated, 3))
                list.Add(new Suggestion(p.Name, DescSource.Place, p.Away, day.Key));
            var town = places.Near(c, Distance.FromMiles(30), PlaceClass.Populated, 50)
                             .Where(p => p.Population >= 1000).OrderBy(p => p.Away.Meters).FirstOrDefault();
            if (town is not null) list.Add(new Suggestion($"near {town.Name}", DescSource.Town, town.Away, day.Key));
        }
        return list.Where(s => s.Text.Length > 0).DistinctBy(s => s.Text, StringComparer.OrdinalIgnoreCase).Take(Max).ToList();
    }
}
