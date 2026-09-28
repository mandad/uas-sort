namespace UasSort.Core;

public sealed record VolumeInfo(string Root, CardIdentity Identity, string DriveType, bool IsReady,
                                bool IsReadOnlyVolume /* FILE_READ_ONLY_VOLUME */,
                                bool IsNtfs, bool IsRemovableBus, long FreeBytes,
                                string BusType /* IOCTL_STORAGE_QUERY_PROPERTY: "Sd", "Mmc", "Usb", "Nvme", … */,
                                bool RemovableMedia /* STORAGE_DEVICE_DESCRIPTOR.RemovableMedia */,
                                bool IsSystemBootOrPaging);   // the last three feed the cleanup volume check (Ref §10.6)

/// <summary>One listed entry. RelPath is relative to the listed root with the native '\' separator.</summary>
public sealed record FsEntry(string FullPath, string RelPath, bool IsDirectory, long Size, DateTime MtimeUtc, DateTime CreationUtc,
                             DateTime LastAccessUtc, uint RawAttributes);

public sealed record ListingResult(ImmutableArray<FsEntry> Entries, ImmutableArray<(string Path, int Win32Error)> Errors);
