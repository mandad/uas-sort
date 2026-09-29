namespace UasSort.Core.Card;

/// <summary>The identity of the volume holding a card source: a detected card's own, or for a Browse source (no identity in its
/// CardSource, Ref §4.1) the volume of its path root. ScanService opens the reader with it and CommitSession.Begin pins it.</summary>
internal static class CardVolumes
{
    public static CardIdentity? IdentityOf(IVolumeProvider volumes, CardSource source)
    {
        if (source.Identity is { } pinned) return pinned;
        var volumeRoot = Path.GetPathRoot(source.Root);
        return string.IsNullOrEmpty(volumeRoot)
            ? null
            : volumes.GetVolumes().FirstOrDefault(v => PathRules.Equal(v.Root, volumeRoot))?.Identity;
    }
}
