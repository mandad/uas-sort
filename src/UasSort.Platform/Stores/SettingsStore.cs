using UasSort.Platform.Io;

namespace UasSort.Platform.Stores;

/// <summary>settings.json with File.Replace + .bak (Ref §11). Parsing, validation and the recovery decision are Core's
/// SettingsCodec, SettingsLoadPolicy and SettingsRecovery (decision 37); this class only reads, renames and writes.</summary>
public sealed class SettingsStore : ISettingsStore
{
    private static readonly IReadOnlySet<string> NoExclusions = new HashSet<string>();
    private readonly string _appDataDir;
    private readonly string _path;
    private readonly string _picturesFolder;
    private readonly string _machine;
    private readonly IPathFacts _facts;
    private readonly IDirectoryLister _lister;
    private readonly TimeProvider _clock;
    private readonly GuardContext _ctx;

    public SettingsStore(string appDataDir, string picturesFolder, string machine, IPathFacts facts, IDirectoryLister lister, TimeProvider clock)
        : this(appDataDir, Path.Join(appDataDir, "settings.json"), picturesFolder, machine, facts, lister, clock) { }

    private SettingsStore(string appDataDir, string path, string picturesFolder, string machine, IPathFacts facts,
                          IDirectoryLister lister, TimeProvider clock)
    {
        _appDataDir = appDataDir;
        _path = facts.Canonical(path);
        _picturesFolder = picturesFolder;
        _machine = machine;
        _facts = facts;
        _lister = lister;
        _clock = clock;
        _ctx = GuardContexts.ForAppData(Path.GetDirectoryName(_path)!, machine, facts);
    }

    /// <summary>The CLI's --settings file: the store is rooted at that file's folder; backups still come from appDataDir.</summary>
    public static SettingsStore ForFile(string settingsFile, string appDataDir, string picturesFolder, string machine, IPathFacts facts,
                                        IDirectoryLister lister, TimeProvider clock)
        => new(appDataDir, settingsFile, picturesFolder, machine, facts, lister, clock);

    public SettingsLoad Load(bool readOnly = false)
    {
        var text = StoreFiles.ReadText(_path, _ctx);                    // null = missing; a placeholder throws in IoGate
        var decision = SettingsLoadPolicy.Decide(_path, text, readOnly, SettingsDefaults.Derive(_picturesFolder),
                                                 _clock.GetUtcNow().UtcDateTime,
                                                 () => SettingsRecovery.RootsFromLastRun(BackupMirrors())
                                                       ?? SettingsRecovery.RootsFromLastRun(DerivedRootLedgers()));
        if (!readOnly && decision.MoveCorruptTo is { } corrupt) StoreFiles.Rename(_path, corrupt, _ctx);
        return decision.Load;
    }

    public void Save(Settings s) => StoreFiles.WriteReplace(_path, SettingsCodec.Serialize(s), _ctx, _path + ".bak");

    /// <summary>The local ledger mirrors, appDataDir\ledger-backup\{root key}\ledger*.jsonl (top level of each key folder;
    /// snapshots are not mirrors). Read only when Core asks for them (an unreadable, non-read-only load).</summary>
    private List<LedgerFileText> BackupMirrors()
    {
        var appCtx = GuardContexts.ForAppData(_appDataDir, _machine, _facts);
        var mirrors = new List<LedgerFileText>();
        var backupRoot = Path.Join(_appDataDir, "ledger-backup");
        foreach (var rootKey in _lister.Enumerate(backupRoot, recurse: false, NoExclusions).Entries.Where(e => e.IsDirectory))
            foreach (var e in _lister.Enumerate(rootKey.FullPath, recurse: false, NoExclusions).Entries)
                if (!e.IsDirectory && LedgerPaths.IsLedgerFileName(Path.GetFileName(e.FullPath)))
                    mirrors.Add(new LedgerFileText(e.FullPath, StoreFiles.ReadText(_facts.Canonical(e.FullPath), appCtx) ?? ""));
        return mirrors;
    }

    /// <summary>The fallback when no mirror has a run (Ref §11): the ledger*.jsonl files directly under
    /// LedgerPaths.For(SettingsDefaults.Derive(picturesFolder).VideoRoot); a cloud-only file is skipped, never hydrated.</summary>
    private List<LedgerFileText> DerivedRootLedgers()
    {
        var d = SettingsDefaults.Derive(_picturesFolder);
        var ctx = GuardContexts.For(d, _appDataDir, _machine, _facts);
        var dir = LedgerPaths.For(d.VideoRoot);
        var ledgers = new List<LedgerFileText>();
        foreach (var e in _lister.Enumerate(dir, recurse: false, NoExclusions).Entries)
        {
            if (e.IsDirectory || !LedgerPaths.IsLedgerFileName(Path.GetFileName(e.FullPath))) continue;
            try { ledgers.Add(new LedgerFileText(e.FullPath, StoreFiles.ReadText(_facts.Canonical(e.FullPath), ctx) ?? "")); }
            catch (CloudOnlyFileException) { }
        }
        return ledgers;
    }
}
