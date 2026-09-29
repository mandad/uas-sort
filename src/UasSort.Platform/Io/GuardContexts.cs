namespace UasSort.Platform.Io;

public static class GuardContexts
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static GuardContext For(Settings s, string appDataDir, string machine, IPathFacts facts, string? cardRoot = null,
                                   IEnumerable<string>? newFolderDirs = null, IReadOnlySet<string>? ownTemps = null,
                                   IReadOnlySet<string>? renamed = null)
        => new(VideoRoot: facts.Canonical(s.VideoRoot),
               PhotoRoot: facts.Canonical(s.PhotoRoot),
               PreviousPhotoRoots: [.. s.PreviousPhotoRoots.Select(facts.Canonical)],
               CardRoot: cardRoot is null ? null : facts.Canonical(cardRoot),
               AppDataDir: facts.Canonical(appDataDir),
               Machine: machine,
               NewFolderDirs: newFolderDirs is null ? None
                   : new HashSet<string>(newFolderDirs.Select(facts.Canonical), StringComparer.OrdinalIgnoreCase),
               OwnTempsThisRun: ownTemps ?? None,
               RenamedThisRun: renamed ?? None,
               SystemVolumeRoot: KnownFolders.SystemVolumeRoot(),
               CardIsVerifiedCardVolume: false,
               Cleanup: null);
}
