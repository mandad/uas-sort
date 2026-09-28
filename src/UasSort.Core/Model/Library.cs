namespace UasSort.Core;

public enum LocationSource { Ledger, CardLeftovers, Unknown }

/// <summary>An event folder of the library. DaysIn(tzId) is added by Part 05 (partial).</summary>
public sealed partial record LibraryFolder(LibraryFolderRef Ref, ImmutableArray<DateTime> MemberStartsUtc, string? TzId,
                                           GeoPoint? Centroid, LocationSource Loc);

public sealed record RootListing(string Root, DestRoot Kind, bool IsPrevious, bool Available, ListingResult Listing);
public sealed record LibraryListings(RootListing Video, RootListing Photo, ImmutableArray<RootListing> PreviousPhoto);
public sealed record LibraryFile(string FullPath, FileKey Key, DateTime MtimeUtc, uint RawAttributes, LibraryFolderRef? EventFolder);
public sealed record SetFolderListing(string FullPath, string Name, ImmutableArray<(string Member, long Size, DateTime MtimeUtc)> Members);

/// <summary>Built from listings + ledger only (Ref §7.1). Build, Match, SameNameOtherSize, Folders, SetFolder,
/// WatermarkUtc and UnavailableRoots are added by Part 05 in Library/LibraryIndex.cs (partial).</summary>
public sealed partial class LibraryIndex
{
}
