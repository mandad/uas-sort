// src/UasSort.Review/Map/MapBridge.Data.cs
namespace UasSort.Review;

/// <summary>C# computes every distance, label and colour (Ref §9.6); JS only draws.</summary>
public static class MapProjection
{
    public const string ImportedGrey = "#9E9E9E";

    public static ImmutableArray<string> Palette { get; } =
        ["#1F77B4", "#FF7F0E", "#2CA02C", "#D62728", "#9467BD", "#8C564B", "#E377C2", "#7F7F7F", "#BCBD22", "#17BECF"];

    public static string Color(VideoGroup g)
        => g.Target is AlreadyImported || g.ColorIndex < 0 ? ImportedGrey : Palette[g.ColorIndex % Palette.Length];

    public static MapInit Init(MapSettings s, double radiusMiles, bool online, bool dark)
        => new(new MapConfig(s.StreetsStyleUrl, s.StreetsDarkStyleUrl, s.SatelliteUrl), online ? s.Base : "none", radiusMiles, online, dark ? "dark" : "light");

    public static MapSetData SetData(Plan plan, int rev)
    {
        var byId = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var items = ImmutableArray.CreateBuilder<MapItem>();
        var groups = ImmutableArray.CreateBuilder<MapGroup>();
        foreach (var g in plan.Groups)
        {
            var gid = g.Id.Anchor.CardRelPath;
            foreach (var id in g.Videos)
            {
                if (!byId.TryGetValue(id, out var item)) continue;
                if (item.Gps is { } fix) items.Add(new MapItem(id.CardRelPath, gid, fix.Point.Lon, fix.Point.Lat, "video"));
                else if (g.Centroid is { } c) items.Add(new MapItem(id.CardRelPath, gid, c.Lon, c.Lat, "videoNoGps"));
            }
            if (g.Centroid is { } centre)
                groups.Add(new MapGroup(gid, Color(g), centre, $"{Fmt.DateRange(g.Start, g.End)} · {Fmt.Count(g.Videos.Length, "clip", "clips")}"));
        }
        var centres = plan.Groups.ToDictionary(g => g.Id, g => g.Centroid);
        var jumps = ImmutableArray.CreateBuilder<MapJump>();
        foreach (var b in plan.Boundaries)
        {
            if (b.Jump is not { } jump || centres[b.Left] is not { } from || centres[b.Right] is not { } to) continue;
            jumps.Add(new MapJump(from, to, $"{Fmt.Miles(jump)} · {Fmt.Gap(b.Gap)}"));
        }
        return new MapSetData(rev, items.ToImmutable(), groups.ToImmutable(), jumps.ToImmutable());
    }

    public static MapSelect Select(Plan plan, GroupId group, IReadOnlyList<ItemId> itemIds, bool fit)
    {
        var g = plan.Groups.FirstOrDefault(x => x.Id == group);
        HashSet<ItemId> ids = g is null ? [] : g.Videos.ToHashSet();
        var points = plan.Base.Items.Where(i => ids.Contains(i.Raw.Unit.Id) && i.Gps is not null).Select(i => i.Gps!.Point).ToList();
        if (points.Count == 0 && g?.Centroid is { } c) points.Add(c);
        return new MapSelect(group.Anchor.CardRelPath, [.. itemIds.Select(i => i.CardRelPath)], fit, Bbox(points));
    }

    public static ImmutableArray<double> Bbox(IReadOnlyList<GeoPoint> points)
    {
        if (points.Count == 0) return [];
        const double pad = 0.005;
        return [points.Min(p => p.Lon) - pad, points.Min(p => p.Lat) - pad, points.Max(p => p.Lon) + pad, points.Max(p => p.Lat) + pad];
    }
}

public sealed partial class MapBridge
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(100);
    private readonly Lock _throttleLock = new();
    private ITimer? _trailing;
    private Plan? _pending;
    private DateTimeOffset _lastSent = DateTimeOffset.MinValue;

    /// <summary>Revision counter of the setData messages sent so far.</summary>
    public int Rev { get; private set; }

    /// <summary>Sends setData for this plan, at most 10 per second; a burst ends with the latest plan (Ref §9.6).</summary>
    public void SendData(Plan plan)
    {
        lock (_throttleLock)
        {
            var now = _time.GetUtcNow();
            if (_trailing is null && now - _lastSent >= MinInterval)
            {
                SendNow(plan, now);
                return;
            }
            _pending = plan;
            if (_trailing is null)
            {
                var due = MinInterval - (now - _lastSent);
                _trailing = _time.CreateTimer(_ => FlushPending(), null, due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void FlushPending()
    {
        lock (_throttleLock)
        {
            _trailing?.Dispose();
            _trailing = null;
            if (_pending is { } p)
            {
                _pending = null;
                SendNow(p, _time.GetUtcNow());
            }
        }
    }

    private void SendNow(Plan plan, DateTimeOffset now)
    {
        _lastSent = now;
        Rev++;
        Send(MapProjection.SetData(plan, Rev));
    }

    private void DisposeThrottle()
    {
        lock (_throttleLock)
        {
            _trailing?.Dispose();
            _trailing = null;
        }
    }
}
