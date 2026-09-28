namespace UasSort.Core;

public sealed record Settings(int Schema, string VideoRoot, string PhotoRoot, ImmutableArray<string> PreviousPhotoRoots,
                              double RadiusMiles, int GapDays, StoredClockMode DroneClockMode /* last learned; default Zone */,
                              string DroneClockZone /* last learned zone; default America/New_York */, bool CopyJpgTwin,
                              MapSettings Map, LayoutSettings Layout, bool RootsConfirmed);
                              // deliberately NO ledger-folder property: STJ would serialise a computed getter as "ledgerDir"

public sealed record MapSettings(string Base /* streets|satellite|none */, string StreetsStyleUrl, string StreetsDarkStyleUrl,
                                 string SatelliteUrl, ImmutableDictionary<string, string> SatellitePresets);
public sealed record LayoutSettings(double TimelineWidth, double MapHeightRatio);
public sealed record SettingsLoad(Settings Settings, bool Recovered, string? CorruptCopyPath, RunRoots? RootsFromLastRun);

public sealed record Draft(int V, string CardKey, string InventoryHash, DateTime SavedUtc, Tuning Tuning, ImmutableArray<PlanEdit> Edits);
