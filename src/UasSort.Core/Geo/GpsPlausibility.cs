namespace UasSort.Core.Geo;

/// <summary>Gates generic-search djmd GPS hits (Ref §6.3 step 5b). Model-table, EXIF and mdat-fallback fixes never come here.</summary>
public static class GpsPlausibility
{
    public static readonly Distance MaxFirstToLast = Distance.FromMiles(3);
    public static readonly Distance MaxFromCard = Distance.FromMiles(500);

    public static GpsProbe Check(GpsFix first, GpsFix? last, TzLookup zone, IReadOnlyList<GeoPoint> cardPoints)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(cardPoints);

        // No last sample at the same field path means the hit can't be confirmed: fail safe (no GPS rather than a wrong one).
        if (last is null || GeoMath.Haversine(first.Point, last.Point).Meters > MaxFirstToLast.Meters)
            return new NoFix(NoFixReason.GenericHitImplausible);
        if (zone.IsEtc)
            return new NoFix(NoFixReason.GenericHitImplausible);
        if (cardPoints.Count > 0 && GeoMath.Haversine(first.Point, GeoMath.Median(cardPoints)).Meters > MaxFromCard.Meters)
            return new NoFix(NoFixReason.GenericHitImplausible);
        return first;
    }
}
