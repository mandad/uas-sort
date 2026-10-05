using System.Reflection;

namespace UasSort.Platform;

/// <summary>
/// Every Platform implementation the App composes (Ref §4.1; registry decision 21, owner Part 11). Creating it writes
/// nothing: the stores, the ledger and the log touch the disk only when used, so the selftest's placeholderVisibility check
/// can create one over the real app-data folder. Inside this record the property `Settings` (the store) hides the Core type
/// of the same name, so the type is written fully qualified.
/// </summary>
public sealed record PlatformServices(
    TimeProvider Clock, string AppDataDir, string Machine,
    IVolumeProvider Volumes, IDirectoryLister Lister, IPathFacts PathFacts, ICardSourceValidator Validator,
    ICardReaderFactory Readers, ICardEraserFactory Erasers, ISettingsStore Settings, IDraftStore Drafts, IReportStore Reports,
    IAppAssets Assets, IPowerRequest Power, IOffloadLock OffloadLock, IDeviceEject Eject, IShellLauncher Shell,
    Func<string, ILedgerStore> LedgerFor, Func<UasSort.Core.Settings, IReadOnlySet<string>, IFileOps> FileOpsFor, FileLog Log,
    IPhotoRootRecyclerFactory PhotoRecyclers, Func<UasSort.Core.Settings, IPhotoFileReader> PhotoReaderFor)
{
    public static PlatformServices Create(string appDataDir, TimeProvider clock)
        => Create(appDataDir, clock, KnownFolders.Pictures());

    /// <summary>The same composition over a given Pictures folder (the base of the settings defaults). Tests pass a synthetic
    /// one, so nothing under the real Pictures folder (which follows OneDrive) is queried during the build.</summary>
    internal static PlatformServices Create(string appDataDir, TimeProvider clock, string picturesFolder)
    {
        var machine = Environment.MachineName;                  // decision 32: passed explicitly from here
        var facts = new PathFacts();
        var lister = new WindowsDirectoryLister();
        var settings = new SettingsStore(appDataDir, picturesFolder, machine, facts, lister, clock);

        // The guarded Platform classes take Settings at construction. These read the saved settings (read-only, never
        // writing) each time they are used, so a root changed in Setup or Settings is what the next card read, card erase
        // or ledger open is guarded with.
        UasSort.Core.Settings Current() => settings.Load(readOnly: true).Settings;

        return new PlatformServices(
            clock, appDataDir, machine,
            new WindowsVolumeProvider(), lister, facts, new CardSourceValidator(),
            new CurrentReaders(() => new WindowsCardReaderFactory(Current(), appDataDir, machine, facts, lister)),
            new CurrentErasers(() => new WindowsCardEraserFactory(Current(), appDataDir, machine, facts, lister)),
            settings,
            new DraftStore(appDataDir, machine, facts),
            new ReportStore(appDataDir, machine, facts, clock),
            new AppAssets(AppContext.BaseDirectory, Assembly.GetEntryAssembly() ?? typeof(PlatformServices).Assembly),
            new PowerRequest(),
            new OffloadLock(),
            new DeviceEject(),
            new ShellLauncher(),
            videoRoot => new LedgerStore(Current() with { VideoRoot = videoRoot }, appDataDir, machine, facts, lister, clock),
            (s, newFolderDirs) => new GuardedFileOps(s, appDataDir, machine, facts, newFolderDirs),
            new FileLog(appDataDir, machine, facts, clock),
            new WindowsPhotoRootRecyclerFactory(Current, appDataDir, machine, facts),     // re-reads the saved photo root at every Open
            s => new GuardedPhotoFileReader(s, appDataDir, machine, facts));
    }

    private sealed class CurrentReaders(Func<ICardReaderFactory> create) : ICardReaderFactory
    {
        public ICardReader Open(CardSource source, CardIdentity identity) => create().Open(source, identity);
    }

    private sealed class CurrentErasers(Func<ICardEraserFactory> create) : ICardEraserFactory
    {
        public ICardEraser Open(CardSource source, CardIdentity pinned, ConfirmedCleanupPlan plan) => create().Open(source, pinned, plan);
    }
}
