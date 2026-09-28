namespace UasSort.Core;

public enum ClockMode { SiteLocal, Zone, NearestSample, Setting }    // Ref §6.1
public enum StoredClockMode { SiteLocal, Zone }                       // what a successful run saves (Settings.DroneClockMode)

public sealed record ClockSample(DateTime DroneStamp, DateTime MvhdUtc, TimeSpan Offset,
                                 string? SiteZoneId /* sample's own GPS; null = no GPS or Etc/* */);

/// <summary>The learned drone clock. ToUtc and OffsetAt are added by Part 04 in Time/ClockModel.Conversion.cs.</summary>
public sealed partial record ClockModel(ClockMode Mode, string? ZoneId, ImmutableArray<ClockSample> Samples, TimeSpan? Modal,
                                        StoredClockMode SettingMode, string SettingZoneId);

public sealed record ClockSummary(ClockMode Mode, string? ZoneId, int SampleCount, string Headline,
                                  int MismatchItems, ImmutableArray<string> MismatchSiteZones /* IANA IDs, for the InfoBar */,
                                  ImmutableArray<ClockChange> Changes /* NearestSample only */);

public sealed record ClockChange(DateTime AtUtc, TimeSpan From, TimeSpan To);
