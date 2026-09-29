using UasSort.Core.Card;
using UasSort.Core.Ledger;
using UasSort.Core.Media;
using UasSort.Core.Time;

namespace UasSort.Core.Planning;

/// <summary>The Plan stage's scan (Ref §4.2, §4.4 step 4): card and roots listed in parallel, ledger after Check(), sequential harvest.</summary>
public sealed class ScanService
{
    public static readonly IReadOnlySet<string> LibraryExcludes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { LedgerPaths.FolderName };
    private static readonly IReadOnlySet<string> NoExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly Settings _settings;
    private readonly IDirectoryLister _lister;
    private readonly ILedgerStore _ledger;
    private readonly IVolumeProvider _volumes;
    private readonly ITimeZoneResolver _tz;
    private readonly TimeZoneInfo _pc;
    private readonly TimeProvider _clock;

    public ScanService(Settings settings, IDirectoryLister lister, ILedgerStore ledger, IVolumeProvider volumes,
                       ITimeZoneResolver tz, TimeZoneInfo pcZone, TimeProvider clock)
    {
        _settings = settings;
        _lister = lister;
        _ledger = ledger;
        _volumes = volumes;
        _tz = tz;
        _pc = pcZone;
        _clock = clock;
    }

    public async Task<ScanResult> ScanAsync(CardSource source, ICardReaderFactory readers, IProgress<ScanProgress> progress, CancellationToken ct)
    {
        var identity = CardVolumes.IdentityOf(_volumes, source)
                       ?? throw new InvalidOperationException($"No volume found for {source.Root}");
        var reader = readers.Open(source, identity);

        progress.Report(new ScanProgress(ScanPhase.ListingCard, 0, 1, source.Root));
        var cardTask = Task.Run(() => _lister.Enumerate(source.Root, true, NoExcludes), ct);
        var roots = new List<(string Root, DestRoot Kind, bool Previous)>
        {
            (_settings.VideoRoot, DestRoot.Video, false),
            (_settings.PhotoRoot, DestRoot.Photo, false),
        };
        roots.AddRange(_settings.PreviousPhotoRoots.Select(r => (r, DestRoot.Photo, true)));
        progress.Report(new ScanProgress(ScanPhase.ListingLibrary, 0, roots.Count, null));
        var rootTasks = roots.Select(r => Task.Run(() => ListRoot(r.Root, r.Kind, r.Previous), ct)).ToList();
        var cardListing = await cardTask.ConfigureAwait(false);
        var rootListings = await Task.WhenAll(rootTasks).ConfigureAwait(false);
        var inventory = new CardClassifier(_clock).Classify(source, cardListing);
        ct.ThrowIfCancellationRequested();

        progress.Report(new ScanProgress(ScanPhase.ReadingLedger, 0, 1, null));
        var status = _ledger.Check();                                           // attributes only, before any open
        var ledger = status.State is LedgerFolderState.CloudOnly or LedgerFolderState.Missing
                                  or LedgerFolderState.Empty or LedgerFolderState.VideoRootMissing or LedgerFolderState.Unlistable
            ? LedgerSnapshots.Empty(status)
            : _ledger.Load();
        ct.ThrowIfCancellationRequested();

        var raw = new List<RawItem>(inventory.Units.Length);
        await foreach (var r in MetadataHarvester.HarvestAsync(inventory, reader, progress, ct).ConfigureAwait(false))
            raw.Add(r);

        var clock = DroneClock.Learn(raw, _settings.DroneClockMode, _settings.DroneClockZone, _tz, _pc);
        var listings = new LibraryListings(rootListings[0], rootListings[1], [.. rootListings.Skip(2)]);
        var lib = LibraryIndex.Build(listings, ledger, clock);
        var warnings = inventory.Warnings.AddRange(rootListings
            .Where(l => l.Available)
            .SelectMany(l => l.Listing.Errors.Select(e =>
                new ScanWarning("LibraryListingError", $"Can't list {e.Path} (error {e.Win32Error})", null, false))));
        progress.Report(new ScanProgress(ScanPhase.BuildingPlan, 0, 1, null));
        return new ScanResult(inventory, [.. raw], lib, ledger, clock, warnings, _settings);
    }

    private RootListing ListRoot(string root, DestRoot kind, bool previous)
    {
        var listing = _lister.Enumerate(root, true, LibraryExcludes);
        var missing = listing.Errors.Any(e => SamePath(e.Path, root) && e.Win32Error is 2 or 3);   // file / path not found
        return new RootListing(root, kind, previous, !missing, listing);
    }

    private static bool SamePath(string a, string b) => PathRules.Equal(a, b);
}
