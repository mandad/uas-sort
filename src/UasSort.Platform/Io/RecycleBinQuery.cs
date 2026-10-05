using System.Globalization;
using System.Security;
using Microsoft.Win32;

namespace UasSort.Platform.Io;

/// <summary>The Recycle Bin of the volume holding a path (branch-2 ruling), read-only: MaxCapacity (MB) and NukeOnDelete from
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\{volume-guid} — when absent, Windows' documented defaults
/// (5 % of the volume; recycle) — and the bin's current size from SHQueryRecycleBinW. Opens no file.</summary>
internal static unsafe class RecycleBinQuery
{
    private const string BitBucketVolumes = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\";
    private const long Megabyte = 1024 * 1024;
    private const int DefaultPercent = 5;

    /// <exception cref="IOException">The volume, its Recycle Bin or its settings couldn't be read.</exception>
    public static RecycleBinCapacity Read(string path)
    {
        var full = Path.GetFullPath(path);
        var volumeRoot = VolumeQuery.VolumePathName(full) ?? throw new IOException($"The volume of {full} couldn't be found");
        var root = volumeRoot.EndsWith('\\') ? volumeRoot : volumeRoot + '\\';
        var volume = root.TrimEnd('\\');
        var total = VolumeQuery.Space(root)?.Total ?? throw new IOException($"The size of {volume} couldn't be read");
        var info = new ShQueryRbInfo { Size = (uint)sizeof(ShQueryRbInfo) };
        var hr = Shell32.SHQueryRecycleBin(root, ref info);
        if (hr < 0) throw new IOException(string.Create(CultureInfo.InvariantCulture, $"SHQueryRecycleBinW({volume}) failed (HRESULT 0x{hr:X8})"), hr);
        var guid = VolumeGuidOf(VolumeName(root)) ?? throw new IOException($"The volume id of {volume} couldn't be read");
        int? maxMb, nuke;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(BitBucketVolumes + guid);
            maxMb = key?.GetValue("MaxCapacity") as int?;
            nuke = key?.GetValue("NukeOnDelete") as int?;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException)
        {
            throw new IOException($"The Recycle Bin settings of {volume} couldn't be read: {e.Message}", e);
        }
        return FromSettings(volume, total, info.BinBytes, maxMb, nuke);
    }

    /// <summary>The registry values, or the documented defaults when absent: MaxCapacity 5 % of the volume, NukeOnDelete 0.</summary>
    internal static RecycleBinCapacity FromSettings(string volume, long volumeBytes, long usedBytes, int? maxCapacityMb, int? nukeOnDelete)
        => new(volume, maxCapacityMb is { } mb ? mb * Megabyte : volumeBytes / 100 * DefaultPercent, usedBytes, nukeOnDelete is { } n && n != 0);

    /// <summary>"{guid}" from "\\?\Volume{guid}\"; null for anything else.</summary>
    internal static string? VolumeGuidOf(string? volumeName)
    {
        if (volumeName is null) return null;
        var open = volumeName.IndexOf("Volume{", StringComparison.OrdinalIgnoreCase);
        var close = open < 0 ? -1 : volumeName.IndexOf('}', open);
        return close < 0 ? null : volumeName[(open + "Volume".Length)..(close + 1)];
    }

    private static string? VolumeName(string volumeRoot)
    {
        var buffer = stackalloc char[64];
        return Kernel32.GetVolumeNameForVolumeMountPoint(volumeRoot, buffer, 64) ? new string(buffer) : null;
    }
}

