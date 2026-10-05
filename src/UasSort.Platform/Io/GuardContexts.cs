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

    /// <summary>Picture Offload cleanup (spec 2026-10-04 §5): For(...) plus the canonical Lightroom folder and, for the recycler only, the plan.</summary>
    public static GuardContext ForPhotoCleanup(Settings s, string appDataDir, string machine, IPathFacts facts, ConfirmedPhotoCleanupPlan? plan)
        => For(s, appDataDir, machine, facts) with
        {
            LightroomFolder = s.LightroomFolder is { } lightroom ? facts.Canonical(lightroom) : null,
            PhotoCleanup = plan,
        };

    /// <summary>For Platform's own stores: the store folder is the only place the policy allows (rule 5).</summary>
    public static GuardContext ForAppData(string appDataDir, string machine, IPathFacts facts)
    {
        var root = facts.Canonical(appDataDir);
        var noLibrary = Path.Join(root, ".no-library");     // never created; keeps rules 3–4 away from the store
        return new(noLibrary, noLibrary, [], null, root, machine, None, None, None, KnownFolders.SystemVolumeRoot(), false, null);
    }
}
