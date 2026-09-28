namespace UasSort.Core;

public sealed record TzLookup(string IanaId, ImmutableArray<string> Alternatives, bool IsEtc);
public enum PlaceClass : byte { Populated, Feature }
public sealed record PlaceHit(string Name, GeoPoint Point, PlaceClass Class, string FeatureCode, int Population, string TzId, Distance Away);
public sealed record ResolvedItem(RawItem Raw, ItemTime Time, ItemFlags Flags, GpsFix? Gps, SessionKey? Session);
