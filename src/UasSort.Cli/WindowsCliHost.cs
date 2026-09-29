namespace UasSort.Cli;

/// <summary>
/// The CLI's composition root: the same validator, lister, path facts, card reader, ledger store (Check + Load only),
/// guard and planner as the app. Nothing here writes: settings are loaded read-only, the ledger is never opened for
/// append, no folder is ensured or pinned, no snapshot is taken, and no log file is used (logs go to stderr).
/// </summary>
internal sealed class WindowsCliHost : ICliHost
{
    private static readonly string Machine = Environment.MachineName;   // passed explicitly as `machine` (registry decision 32)
    private readonly WindowsDirectoryLister _lister = new();
    private readonly PathFacts _facts = new();
    private readonly WindowsVolumeProvider _volumes = new();
    private readonly GeoTimeZoneResolver _tz = new();
    private readonly Lazy<IPlaceIndex?> _places = new(LoadPlaces);

    public string AppDataDir { get; } = KnownFolders.AppDataDir();

    public SettingsLoad LoadSettings(string? settingsPath)
    {
        string pictures = KnownFolders.Pictures();
        var store = settingsPath is { } p
            ? SettingsStore.ForFile(Path.GetFullPath(p), AppDataDir, pictures, Machine, _facts, _lister, TimeProvider.System)
            : new SettingsStore(AppDataDir, pictures, Machine, _facts, _lister, TimeProvider.System);
        return store.Load(readOnly: true);
    }

    public CardSourceCheck Validate(string cardPath, Settings settings) =>
        new CardSourceValidator().Validate(Path.GetFullPath(cardPath), detected: null, settings, _lister, _facts, AppDataDir);

    public CardIdentity? IdentityFor(string cardRoot)
    {
        string canonical = _facts.Canonical(cardRoot);
        return _volumes.GetVolumes()
            .Where(v => canonical.StartsWith(v.Root, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.Root.Length)
            .Select(v => v.Identity)
            .FirstOrDefault();
    }

    public Task<ScanResult> ScanAsync(CardSource source, Settings settings, IProgress<ScanProgress> progress, CancellationToken ct)
    {
        var ledger = new LedgerStore(settings, AppDataDir, Machine, _facts, _lister, TimeProvider.System);   // Check() + Load() only
        var scan = new ScanService(settings, _lister, ledger, _volumes, _tz, TimeZoneInfo.Local, TimeProvider.System);
        var readers = new WindowsCardReaderFactory(settings, AppDataDir, Machine, _facts, _lister);
        return scan.ScanAsync(source, readers, progress, ct);
    }

    public Plan Plan(ScanResult scan, Tuning tuning, CancellationToken ct)
    {
        var planner = new Planner(_tz, _places.Value, TimeZoneInfo.Local, TimeProvider.System);
        PlanBase b = planner.Prepare(scan);
        return planner.Derive(b, tuning, [], new SessionFlags(LedgerIssuesAccepted: false), revision: 1, ct);
    }

    public string ReadExpectFile(string path) => ReadOnlyTextFile.Read(path, ExpectedFile.MaxBytes);

    private static PlaceIndex? LoadPlaces()
    {
        try
        {
            using Stream gz = new AppAssets(AppContext.BaseDirectory, typeof(WindowsCliHost).Assembly).OpenPlaces();
            return PlaceIndex.Load(gz);
        }
        catch (FileNotFoundException)
        {
            return null;   // no places.bin.gz beside the exe: descriptions come only from the library and the ledger
        }
    }
}
