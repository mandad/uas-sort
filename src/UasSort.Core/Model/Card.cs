using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace UasSort.Core;

public enum EntryClass { Video, Photo, PhotoTwin, SetMember, Skip, Unknown }

/// <summary>A classified card file. RelPath is card-relative with '/' separators.</summary>
public sealed record CardEntry(string RelPath, long Size, DateTime MtimeUtc, DateTime CreationUtc, DateTime LastAccessUtc,
                               uint RawAttributes, EntryClass Class, string? Rule);

public sealed record CardIdentity(uint VolumeSerial, string? Label, string FileSystem, long TotalBytes);

public closed record class MediaUnit(ItemId Id);
public sealed record class VideoUnit(ItemId Id, CardEntry Mp4, bool HasTrinf) : MediaUnit(Id);
public sealed record class PhotoUnit(ItemId Id, CardEntry Primary, CardEntry? JpgTwin) : MediaUnit(Id);
public sealed record class SetUnit(ItemId Id, SetKind Kind, string SetName, ImmutableArray<CardEntry> Members) : MediaUnit(Id);

public sealed record CardSource(string Root /* canonical, anchored at the folder holding DCIM */, CardIdentity? Identity,
                                bool IsBrowsedFolder /* set only by CardSourceValidator: detected == null (Ref §4.1) */,
                                bool IsWriteProtected /* detected?.IsReadOnlyVolume */)   // UI hints only: the eraser factory
                                                                                          // re-derives every fact from Win32
{
    public string DraftKey => Identity is { } i
        ? string.Create(CultureInfo.InvariantCulture, $"vol-{i.VolumeSerial:X8}")
        : "dir-" + HashRoot(Root);

    private static string HashRoot(string root)
    {
#pragma warning disable CA1308 // Ref §3: the draft key hashes the lowercase root
        var bytes = Encoding.UTF8.GetBytes(root.ToLowerInvariant());
#pragma warning restore CA1308
        return XxHash64.HashToUInt64(bytes).ToString("x16", CultureInfo.InvariantCulture);
    }
}

public sealed record CardInventory(CardSource Source, DateTime ListedUtc, string InventoryHash, ImmutableArray<CardEntry> Entries,
                                   ImmutableArray<MediaUnit> Units, string? CameraModel, ImmutableArray<ScanWarning> Warnings);

public sealed record ScanWarning(string Code, string Message, string? RelPath, bool ForcesNotSafe);

// ── card detection and source validation (Ref §3 Supporting types (2))
public sealed record CardCandidate(VolumeInfo Volume, bool IsDjiCard, string? NotCardReason /* "not a DJI card" */, int MediaCount);
public sealed record SourceOk(CardSource Source);
public sealed record SourceRefused(string Reason);
public union CardSourceCheck(SourceOk, SourceRefused);
