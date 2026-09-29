namespace UasSort.Platform.Io;

public sealed class WindowsVolumeProvider : IVolumeProvider
{
    public IReadOnlyList<VolumeInfo> GetVolumes()
    {
        var list = new List<VolumeInfo>();
#pragma warning disable RS0030 // IO layer: the volume provider is the one place that enumerates drives
        foreach (var drive in DriveInfo.GetDrives())
        {
            var root = drive.Name;
            var type = drive.DriveType.ToString();
            var ready = drive.IsReady;
#pragma warning restore RS0030
            var probe = ProbesVolume(ready, type);
            var info = ready ? VolumeQuery.Information(root) : null;
            var space = ready ? VolumeQuery.Space(root) : null;
            var bus = probe ? VolumeQuery.Bus(root) : null;
            var busType = bus?.BusType ?? "Unknown";
            list.Add(new VolumeInfo(
                Root: root,
                Identity: new CardIdentity(info?.Serial ?? 0, info?.Label, info?.FileSystem ?? "", space?.Total ?? 0),
                DriveType: type,
                IsReady: ready && info is not null,
                IsReadOnlyVolume: info is not null && (info.Flags & Kernel32.FILE_READ_ONLY_VOLUME) != 0,
                IsNtfs: info?.FileSystem == "NTFS",
                IsRemovableBus: busType is "Usb" or "Sd" or "Mmc" or "1394",
                FreeBytes: space?.Free ?? 0,
                BusType: busType,
                RemovableMedia: bus?.RemovableMedia ?? false,
                IsSystemBootOrPaging: probe && VolumeQuery.IsSystemBootOrPaging(root)));
        }
        return list;
    }

    /// <summary>
    /// Whether the bus and system/boot/paging probes may touch this drive: never an unready drive (an empty card-reader
    /// slot could raise "There is no disk in the drive") nor a Network/CDRom/NoRootDirectory one (SMB reconnect stalls).
    /// A system, boot or paging volume is always a ready local drive, so skipping the rest loses nothing.
    /// </summary>
    internal static bool ProbesVolume(bool ready, string driveType)
        => ready && driveType is not ("Network" or "CDRom" or "NoRootDirectory");
}
