using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace UasSort.Platform.Io;

internal sealed record VolumeInformation(uint Serial, string? Label, string FileSystem, uint Flags);

/// <summary>Volume facts read from Win32 without opening any file or any data handle (Ref §4.1 VolumeInfo, §10.6).</summary>
internal static unsafe class VolumeQuery
{
    private static readonly string[] BusNames =
        ["Unknown", "Scsi", "Atapi", "Ata", "1394", "Ssa", "Fibre", "Usb", "Raid", "iScsi", "Sas", "Sata", "Sd", "Mmc",
         "Virtual", "FileBackedVirtual", "Spaces", "Nvme", "Scm", "Ufs"];

    public static string BusName(uint storageBusType) => storageBusType < BusNames.Length ? BusNames[storageBusType] : "Unknown";

    public static VolumeInformation? Information(string volumeRoot)
    {
        var name = stackalloc char[261];
        var fs = stackalloc char[261];
        if (!Kernel32.GetVolumeInformation(WithSeparator(volumeRoot), name, 261, out var serial, out _, out var flags, fs, 261)) return null;
        var label = new string(name);
        return new VolumeInformation(serial, label.Length == 0 ? null : label, new string(fs), flags);
    }

    public static CardIdentity? Identity(string volumeRoot)
        => Information(volumeRoot) is { } i && Space(volumeRoot) is { } s ? new CardIdentity(i.Serial, i.Label, i.FileSystem, s.Total) : null;

    public static string? VolumePathName(string path)
    {
        var buffer = stackalloc char[1024];
        return Kernel32.GetVolumePathName(Path.GetFullPath(path), buffer, 1024) ? LongPath.Strip(new string(buffer)) : null;
    }

    public static (long Free, long Total)? Space(string existingDir)
        => Kernel32.GetDiskFreeSpaceEx(WithSeparator(existingDir), out var free, out var total, out _) ? ((long)free, (long)total) : null;

    public static int ClusterBytes(string volumeRoot)
        => Kernel32.GetDiskFreeSpace(WithSeparator(volumeRoot), out var spc, out var bps, out _, out _)
            ? checked((int)(spc * bps))
            : throw new IOException($"GetDiskFreeSpaceW({volumeRoot}) failed: {Marshal.GetLastPInvokeError()}");

    public static (string BusType, bool RemovableMedia)? Bus(string volumeRoot)
    {
        if (!IsLetterRoot(volumeRoot)) return null;
        using var device = Kernel32.CreateFile(@"\\.\" + volumeRoot[..2], 0 /* no data access */,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE, 0, Kernel32.OPEN_EXISTING, 0, 0);
        if (device.IsInvalid) return null;
        var query = new StoragePropertyQuery();
        var output = stackalloc byte[1024];
        if (!Kernel32.DeviceIoControl(device, Kernel32.IOCTL_STORAGE_QUERY_PROPERTY, &query, (uint)sizeof(StoragePropertyQuery),
                                      output, 1024, out var returned, 0) || returned < 32) return null;
        // STORAGE_DEVICE_DESCRIPTOR: RemovableMedia at byte 10, BusType (STORAGE_BUS_TYPE) at byte 28
        return (BusName(*(uint*)(output + 28)), output[10] != 0);
    }

    public static bool IsSystemBootOrPaging(string volumeRoot)
    {
        var root = WithSeparator(volumeRoot);
        if (Same(root, KnownFolders.SystemVolumeRoot())) return true;
        if (Same(root, Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "")) return true;
        foreach (var name in new[] { "pagefile.sys", "swapfile.sys", "hiberfil.sys" })
        {
            var a = Kernel32.TryGetAttributes(Path.Join(root, name), out var error);
            if (a is not null || error == Kernel32.ERROR_SHARING_VIOLATION) return true;
        }
        using var mm = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
        foreach (var entry in mm?.GetValue("PagingFiles") as string[] ?? [])
            if (entry.Length >= 3 && Same(entry[..3], root)) return true;
        return false;
    }

    private static bool IsLetterRoot(string root) => root.Length is 2 or 3 && char.IsAsciiLetter(root[0]) && root[1] == ':';
    private static string WithSeparator(string root) => root.EndsWith('\\') ? root : root + '\\';
    private static bool Same(string a, string b) => string.Equals(WithSeparator(a), WithSeparator(b), StringComparison.OrdinalIgnoreCase);
}
