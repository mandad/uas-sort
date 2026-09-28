namespace UasSort.Core.Card;

/// <summary>Card source policy (Ref §4.3): anchor at the folder holding DCIM, canonicalise through IPathFacts, refuse any
/// overlap with the library, the ledger folder, app data or a cloud sync root. Listings only; opens nothing.</summary>
public sealed class CardSourceValidator : ICardSourceValidator
{
    public const string PickTopFolder = "Pick the card's top folder (the one containing DCIM)";
    public const string NoDcim = "No DCIM folder here";
    public const string PartOfLibrary = "This is part of your library (or a synced folder); uas-sort only offloads from cards.";

    private static readonly IReadOnlySet<string> NoExclusions = ImmutableHashSet<string>.Empty;

    public CardSourceCheck Validate(string chosenPath, VolumeInfo? detected, Settings s, IDirectoryLister lister,
                                    IPathFacts facts, string appDataDir)
    {
        ArgumentNullException.ThrowIfNull(chosenPath);
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(appDataDir);

        // 1. Anchor.
        var chosen = PathRules.Normalize(chosenPath);
        string? anchor = null;
        for (var dir = chosen; dir is not null; dir = PathRules.Parent(dir))
        {
            if (HoldsDcim(lister.Enumerate(dir, recurse: false, NoExclusions)))
            {
                anchor = dir;
                break;
            }
        }
        if (anchor is null)
        {
            var here = lister.Enumerate(chosen, recurse: false, NoExclusions);
            var hasMedia = here.Entries.Any(e => !e.IsDirectory && CardClassifier.MediaExtensions.Contains(Extension(e.FullPath)));
            return new SourceRefused(hasMedia ? PickTopFolder : NoDcim);
        }

        // 2. Canonicalise.
        var root = PathRules.Normalize(facts.Canonical(anchor));

        // 3. Refuse overlaps.
        var guarded = new List<string> { s.VideoRoot, s.PhotoRoot, LedgerPaths.For(s.VideoRoot), appDataDir };
        guarded.AddRange(s.PreviousPhotoRoots);
        foreach (var g in guarded)
            if (PathRules.Overlaps(root, CanonicalOrAsWritten(facts, g))) return new SourceRefused(PartOfLibrary);
        if (facts.InSyncRoot(root)) return new SourceRefused(PartOfLibrary);
        foreach (var syncRoot in facts.SyncRoots())
            if (PathRules.Overlaps(root, CanonicalOrAsWritten(facts, syncRoot))) return new SourceRefused(PartOfLibrary);

        return new SourceOk(new CardSource(root, detected?.Identity, IsBrowsedFolder: detected is null,
                                           IsWriteProtected: detected?.IsReadOnlyVolume ?? false));
    }

    private static bool HoldsDcim(ListingResult listing)
        => listing.Entries.Any(e => e.IsDirectory && string.Equals(PathRules.FileName(e.FullPath), "DCIM", StringComparison.OrdinalIgnoreCase));

    private static string CanonicalOrAsWritten(IPathFacts facts, string path)
    {
        try
        {
            return PathRules.Normalize(facts.Canonical(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return PathRules.Normalize(path);   // e.g. D: unplugged: compare the configured path as written
        }
    }

    private static string Extension(string path)
    {
        var name = PathRules.FileName(path);
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[(dot + 1)..];
    }
}
