// src/UasSort.App/Services/PlatformAdapters.cs — small App-side adapters of Platform ports to Review seams
namespace UasSort.App.Services;

/// <summary>IFreeSpace for the Setup/Settings free-space lines: the free bytes of the volume holding the path.</summary>
public sealed class VolumeFreeSpace(IVolumeProvider volumes) : IFreeSpace
{
    public long? FreeBytes(string anyPathOnVolume)
    {
        var root = Path.GetPathRoot(anyPathOnVolume);
        if (string.IsNullOrEmpty(root)) return null;
        var volume = volumes.GetVolumes().FirstOrDefault(v => PathRules.Equal(v.Root, root));
        return volume is { IsReady: true } ? volume.FreeBytes : null;
    }
}

/// <summary>IReviewLog over Part 09's FileLog.</summary>
public sealed class FileReviewLog(FileLog log) : IReviewLog
{
    public void Warn(string message) => log.Warn(message);
    public void Info(string message) => log.Info(message);
}

/// <summary>The selftest's Card stage volume list: always empty (Ref §13 Isolation).</summary>
public sealed class NoVolumes : IVolumeProvider
{
    public static readonly NoVolumes Instance = new();

    public IReadOnlyList<VolumeInfo> GetVolumes() => [];
}
