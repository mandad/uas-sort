using UasSort.Platform.Io;

namespace UasSort.Platform.Card;

internal sealed record VolumeFacts(CardIdentity Identity, bool IsReadOnlyVolume, string BusType, bool RemovableMedia, bool IsSystemBootOrPaging);

/// <summary>The factory's seam (Ref §4.1 ICardEraserFactory): production reads Win32; only Platform.Tests substitutes it.</summary>
internal interface IVolumeFacts
{
    string? VolumePathName(string path);
    VolumeFacts? Describe(string volumeRoot);
}

internal sealed class WindowsVolumeFacts : IVolumeFacts
{
    public string? VolumePathName(string path) => VolumeQuery.VolumePathName(path);

    public VolumeFacts? Describe(string volumeRoot)
    {
        var info = VolumeQuery.Information(volumeRoot);
        var identity = VolumeQuery.Identity(volumeRoot);
        var bus = VolumeQuery.Bus(volumeRoot);
        if (info is null || identity is null || bus is null) return null;
        return new VolumeFacts(identity, (info.Flags & Kernel32.FILE_READ_ONLY_VOLUME) != 0, bus.Value.BusType, bus.Value.RemovableMedia,
                               VolumeQuery.IsSystemBootOrPaging(volumeRoot));
    }
}
