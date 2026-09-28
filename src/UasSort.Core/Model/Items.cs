namespace UasSort.Core;

public enum TimeSource { Mvhd, ExifWithOffset, DroneClockSiteLocal, DroneClockZone, DroneClockSample, DroneClockSetting, Mtime }
public enum TzSource { Gps, SameSession, NearestGpsWithin12h, NearestLandGpsOnCard, GeoNamesTz, PcZone }

#pragma warning disable CA1711 // Ref §3 mandates the name ItemFlags
[Flags]
public enum ItemFlags
{
    None = 0, NoGps = 1, Truncated = 2, ClockNotSet = 4, CheckDate = 8, TzFallback = 16,
    ProbeFailed = 32, GpsGuessed = 64, ClockFromSetting = 128, ClockMismatch = 256,
}
#pragma warning restore CA1711

public sealed record ItemTime(DateTime CaptureUtc, TimeSource Source, string TzId, TzSource TzSource, DateOnly LocalDate, DateTime LocalTime);

public enum NewReason { NoMatch, SeenNotCopied, AfterWatermark, NearWatermark, DayHasNewVideos }
public enum Evidence { LibraryNameSize, LedgerVerified, LedgerNameSize }
public enum DecisionKind { AssumedImported, Dismissed }

public sealed record LibraryFolderRef(string FullPath, DateOnly NameDate, string Description);

public closed record class Newness;
#pragma warning disable CA1711 // Ref §3 mandates the name IsNew
public sealed record class IsNew(NewReason Why, DateTime? SeenUtc) : Newness;
#pragma warning restore CA1711
public sealed record class Imported(Evidence By, LibraryFolderRef? Folder, string Why) : Newness;
public sealed record class Decided(DecisionKind Kind, DateTime AtUtc, string Machine) : Newness;      // ConfirmedByYou; undoable
public sealed record class ProbablyImported(string Why) : Newness;
public sealed record class Conflict(string ExistingPath, long ExistingSize) : Newness;

public sealed record Item(RawItem Raw, ItemTime Time, GpsFix? Gps, SessionKey? Session, ItemFlags Flags, Newness Newness);
