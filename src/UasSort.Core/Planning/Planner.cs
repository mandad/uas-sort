// src/UasSort.Core/Planning/Planner.cs
namespace UasSort.Core.Planning;

/// <summary>Prepare = tuning-independent steps (Ref §4.2); Derive (Planner.Derive.cs) = the derive order of Ref §8.9.</summary>
public sealed partial class Planner
{
    private readonly ITimeZoneResolver _tz;
    private readonly IPlaceIndex? _places;
    private readonly TimeZoneInfo _pc;
    private readonly TimeProvider _clock;

    public Planner(ITimeZoneResolver tz, IPlaceIndex? places, TimeZoneInfo pcZone, TimeProvider clock)
    {
        _tz = tz;
        _places = places;
        _pc = pcZone;
        _clock = clock;
    }

    public PlanBase Prepare(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var resolved = TimeResolver.Resolve(scan.Raw, scan.Clock, _tz, _places, _pc, _clock.GetUtcNow().UtcDateTime);
        var lib = scan.Library;
        var ledger = scan.Ledger;

        var videos = resolved.Where(r => r.Raw.Unit is VideoUnit)
            .Select(r => new Item(r.Raw, r.Time, r.Gps, r.Session, Flags(r), NewnessRules.Video((VideoUnit)r.Raw.Unit, lib, ledger)))
            .ToList();
        var newVideoDays = videos.Where(v => v.Newness is IsNew).Select(v => v.Time.LocalDate).ToHashSet();
        var watermark = lib.WatermarkUtc;

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sets = ImmutableDictionary.CreateBuilder<ItemId, SetPlacement>();
        var photos = new List<Item>();
        foreach (var r in resolved.Where(r => r.Raw.Unit is not VideoUnit)
                                  .OrderBy(r => r.Time.CaptureUtc).ThenBy(r => r.Raw.Unit.Id.CardRelPath, StringComparer.Ordinal))
        {
            SetPlacement? placement = null;
            if (r.Raw.Unit is SetUnit su)
            {
                var provisional = new Item(r.Raw, r.Time, r.Gps, r.Session, Flags(r), new IsNew(NewReason.NoMatch, null));
                placement = SetFolderNamer.Resolve(su, provisional, lib, ledger, taken);
                sets[su.Id] = placement;
            }
            photos.Add(new Item(r.Raw, r.Time, r.Gps, r.Session, Flags(r),
                                NewnessRules.Photo(r.Raw.Unit, r.Time, lib, ledger, placement, newVideoDays, watermark)));
        }

        var items = videos.Concat(photos).ToList();
        items.Sort(Clusterer.Order);
        return new PlanBase(scan, [.. items], PhotoDays(photos), sets.ToImmutable(), DroneClock.Summarize(scan.Clock, resolved), watermark);
    }

    private static ItemFlags Flags(ResolvedItem r)
    {
        var f = r.Flags;
        if (r.Raw.Unit is VideoUnit v && (v.HasTrinf || r.Raw.Mp4 is { HasMoov: false })) f |= ItemFlags.Truncated;
        if (r.Raw.ProbeError is not null) f |= ItemFlags.ProbeFailed;
        return f;
    }

    private static ImmutableArray<PhotoDay> PhotoDays(List<Item> photos) =>
    [
        .. photos.GroupBy(p => p.Time.LocalDate).OrderBy(g => g.Key)
                 .Select(g => new PhotoDay(g.Key, g.First().Time.TzId, [.. g.Select(p => p.Raw.Unit.Id)], DayReason(g))),
    ];

    private static string DayReason(IEnumerable<Item> day)
    {
        var list = day.ToList();
        if (list.Select(p => p.Newness).OfType<ProbablyImported>().FirstOrDefault() is { } pi) return pi.Why;
        return list.All(p => p.Newness is Imported or Decided) ? "already in library" : "";
    }
}
