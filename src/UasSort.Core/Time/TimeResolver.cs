// src/UasSort.Core/Time/TimeResolver.cs
namespace UasSort.Core.Time;

/// <summary>Capture time, site zone, local date, session and flags per item (Ref §6.2–6.5). Independent of R and G.</summary>
public static class TimeResolver
{
    public static readonly TimeSpan NearbyWindow = TimeSpan.FromHours(12);
    public static readonly Distance GeoNamesTzRadius = Distance.FromMiles(60);
    static readonly PlaceClass[] PlaceClasses = [PlaceClass.Populated, PlaceClass.Feature];

    public static ImmutableArray<ResolvedItem> Resolve(IReadOnlyList<RawItem> raw, ClockModel clock, ITimeZoneResolver tz,
                                                       IPlaceIndex? places, TimeZoneInfo pc, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(tz);
        ArgumentNullException.ThrowIfNull(pc);
        var n = raw.Count;
        var lookups = new Dictionary<GeoPoint, TzLookup>();
        TzLookup Lookup(GeoPoint p)
        {
            if (!lookups.TryGetValue(p, out var l)) { l = tz.Resolve(p); lookups[p] = l; }
            return l;
        }

        // 1. GPS: trusted fixes, then generic hits through the plausibility gate (§6.3 step 5b)
        var gps = new GpsFix?[n];
        var guessed = new bool[n];
        var trusted = new List<GeoPoint>();
        for (var i = 0; i < n; i++)
            if (FirstFix(raw[i]) is { } f && f.Source != GpsSource.DjmdGenericSearch)
            {
                gps[i] = f;
                trusted.Add(f.Point);
            }
        for (var i = 0; i < n; i++)
            if (FirstFix(raw[i]) is { Source: GpsSource.DjmdGenericSearch } g
                && GpsPlausibility.Check(g, raw[i].Mp4?.LastSameField, Lookup(g.Point), trusted) is GpsFix accepted)
            {
                gps[i] = accepted;
                guessed[i] = true;
            }

        // 2. each GPS item's own land zone (§6.4 step 1)
        var own = new string?[n];
        for (var i = 0; i < n; i++)
            if (gps[i] is { } fix && Lookup(fix.Point) is { IsEtc: false } l && Zones.TryFind(l.IanaId, out _))
                own[i] = l.IanaId;

        // 3. power-on sessions (§6.3 step 6)
        var sessions = new SessionKey?[n];
        for (var i = 0; i < n; i++)
            if (raw[i].Mp4 is { SessionUtc: { } su } m)
                sessions[i] = new SessionKey(m.DroneSerial, DateTime.SpecifyKind(su, DateTimeKind.Utc));

        // 4. capture time (§6.2); SiteLocal converts through the own zone, else the nearest GPS item ≤ 12 h by drone stamp (§6.1)
        var utc = new DateTime[n];
        var src = new TimeSource[n];
        for (var i = 0; i < n; i++)
            (utc[i], src[i]) = Capture(raw[i], clock, own[i] ?? NearestOwnByStamp(raw, own, i));

        // 5. zone, local date and flags (§6.4, §6.5)
        var pcId = Zones.IanaId(pc);
        var result = ImmutableArray.CreateBuilder<ResolvedItem>(n);
        for (var i = 0; i < n; i++)
        {
            var (tzId, tzSource, fallback) = ZoneFor(i, gps, own, sessions, utc, places, pcId);
            if (!Zones.TryFind(tzId, out var zone))
            {
                (tzId, tzSource, fallback, zone) = (pcId, TzSource.PcZone, true, pc);
            }
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc[i], zone);
            var time = new ItemTime(utc[i], src[i], tzId, tzSource, DateOnly.FromDateTime(local), local);
            var flags = BaseFlags(raw[i], gps[i] is null, guessed[i], fallback);
            result.Add(new ResolvedItem(raw[i], time, flags, gps[i], sessions[i]));
        }
        return result.MoveToImmutable();
    }

    static GpsFix? FirstFix(RawItem r)
    {
        if (r.Mp4 is { } m && m.First is GpsFix video) return video;
        if (r.Still is { } s && s.Gps is GpsFix photo) return photo;
        return null;
    }

    static (DateTime Utc, TimeSource Source) Capture(RawItem r, ClockModel clock, string? clockZone)
    {
        if (r.Kind == ItemKind.Video && r.Mp4 is { HasMoov: true, MvhdUtc: { } mvhd })
            return (DateTime.SpecifyKind(mvhd, DateTimeKind.Utc), TimeSource.Mvhd);
        if (r.Still is { DtoNaive: { } dto, OffsetTime: { } offset })
            return (DateTime.SpecifyKind(dto - offset, DateTimeKind.Utc), TimeSource.ExifWithOffset);
        if (r.DroneStamp is { } stamp && clock.ToUtc(stamp, clockZone) is { } converted)
            return (converted.Utc, converted.Src);
        return (DateTime.SpecifyKind(r.CardMtimeUtc, DateTimeKind.Utc), TimeSource.Mtime);
    }

    static string? NearestOwnByStamp(IReadOnlyList<RawItem> raw, string?[] own, int i)
    {
        if (raw[i].DroneStamp is not { } stamp) return null;
        string? best = null;
        var bestGap = TimeSpan.MaxValue;
        for (var j = 0; j < raw.Count; j++)
        {
            if (j == i || own[j] is null || raw[j].DroneStamp is not { } other) continue;
            var gap = (other - stamp).Duration();
            if (gap <= NearbyWindow && gap < bestGap) { best = own[j]; bestGap = gap; }
        }
        return best;
    }

    static (string Id, TzSource Source, bool Fallback) ZoneFor(int i, GpsFix?[] gps, string?[] own, SessionKey?[] sessions,
                                                               DateTime[] utc, IPlaceIndex? places, string pcId)
    {
        if (own[i] is { } mine) return (mine, TzSource.Gps, false);
        if (gps[i] is { } fix)
        {   // GPS over the sea: an Etc/* zone (§6.4 step 2)
            if (NearestOwn(own, utc, i, static _ => true, within12h: true) is { } land) return (land, TzSource.NearestLandGpsOnCard, true);
            if (NearestPlaceZone(places, fix.Point) is { } placeZone) return (placeZone, TzSource.GeoNamesTz, true);
            return (pcId, TzSource.PcZone, true);
        }
        // no GPS (§6.4 step 3)
        if (sessions[i] is { } s
            && NearestOwn(own, utc, i, j => sessions[j] is { } sj && sj.SameSession(s), within12h: false) is { } sessionZone)
            return (sessionZone, TzSource.SameSession, false);
        if (NearestOwn(own, utc, i, static _ => true, within12h: true) is { } nearby) return (nearby, TzSource.NearestGpsWithin12h, false);
        return (pcId, TzSource.PcZone, true);
    }

    static string? NearestOwn(string?[] own, DateTime[] utc, int i, Func<int, bool> eligible, bool within12h)
    {
        string? best = null;
        var bestGap = TimeSpan.MaxValue;
        for (var j = 0; j < own.Length; j++)
        {
            if (j == i || own[j] is null || !eligible(j)) continue;
            var gap = (utc[j] - utc[i]).Duration();
            if (within12h && gap > NearbyWindow) continue;
            if (gap < bestGap) { best = own[j]; bestGap = gap; }
        }
        return best;
    }

    static string? NearestPlaceZone(IPlaceIndex? places, GeoPoint p)
    {
        if (places is null) return null;
        PlaceHit? best = null;
        foreach (var cls in PlaceClasses)
            foreach (var hit in places.Near(p, GeoNamesTzRadius, cls, 1))
                if (Zones.TryFind(hit.TzId, out _) && (best is null || hit.Away.Meters < best.Away.Meters))
                    best = hit;
        return best?.TzId;
    }

    static ItemFlags BaseFlags(RawItem r, bool noGps, bool guessed, bool tzFallback)
    {
        var f = ItemFlags.None;
        if (noGps) f |= ItemFlags.NoGps;
        if (guessed) f |= ItemFlags.GpsGuessed;
        if (tzFallback) f |= ItemFlags.TzFallback;
        if (r.ProbeError is not null) f |= ItemFlags.ProbeFailed;
        if (r.Kind == ItemKind.Video && (r.Mp4 is { HasMoov: false } || r.Unit is VideoUnit { HasTrinf: true }))
            f |= ItemFlags.Truncated;
        return f;
    }
}
