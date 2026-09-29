using System.Text;
using UasSort.Platform.Io;

namespace UasSort.Platform.Ledger;

/// <summary>The ledger folder videoRoot\.uas-sort: the one exemption from "never open library files" (Ref §4.3, §11).
/// Platform gathers facts and opens files; Core's LedgerFolderStatusBuilder and LedgerLoader decide (decision 37).</summary>
public sealed partial class LedgerStore
{
    private static readonly IReadOnlySet<string> NoExclusions = new HashSet<string>();
    private readonly IPathFacts _facts;
    private readonly IDirectoryLister _lister;
    private readonly GuardContext _ctx;
    private readonly string _videoRoot;

    public LedgerStore(Settings settings, string appDataDir, string machine, IPathFacts facts, IDirectoryLister lister, TimeProvider clock)
    {
        CurrentSettings = settings;
        AppDataDir = appDataDir;
        Machine = machine;
        Clock = clock;
        _facts = facts;
        _lister = lister;
        _videoRoot = facts.Canonical(settings.VideoRoot);
        Folder = LedgerPaths.For(_videoRoot);                 // the same path LedgerFolderStatusBuilder and IoGuardPolicy derive
        OwnFile = LedgerPaths.OwnFile(_videoRoot, machine);
        BackupDir = LedgerPaths.BackupDir(appDataDir, _videoRoot);
        _ctx = GuardContexts.For(settings, appDataDir, machine, facts);
    }

    public string Folder { get; }
    public string OwnFile { get; }
    public string BackupDir { get; }

    // Used by the write half (Task 09.9): CopyInto builds a store for the new root, SnapshotToBackup names and copies.
    private Settings CurrentSettings { get; }
    private string AppDataDir { get; }
    private string Machine { get; }
    private TimeProvider Clock { get; }
    private ImmutableArray<string>? LoadedFiles { get; set; }   // the SourceFiles of the last Load(); null until one ran

    /// <summary>Attributes, one top-level listing, the sync-root test and the security-descriptor write check; no ledger file is opened.</summary>
    public LedgerFolderStatus Check() => LedgerFolderStatusBuilder.Build(_videoRoot, Machine, GatherFacts());

    /// <summary>The union of the listed local ledger files; LedgerLoader opens nothing for Missing, VideoRootMissing or CloudOnly.</summary>
    public LedgerSnapshot Load()
    {
        var snapshot = LedgerLoader.Load(Check(), OpenLedgerRead);
        LoadedFiles = snapshot.SourceFiles;
        return snapshot;
    }

    private LedgerFolderFacts GatherFacts()
    {
        if (!IsDirectory(Kernel32.TryGetAttributes(_videoRoot, out _)))
            return new LedgerFolderFacts(VideoRootExists: false, Folder: null, TopLevel: null, InSyncRoot: false, Writable: false);

        FsEntry? folder = Kernel32.TryGetAttributeData(Folder, out var d, out _)
            ? new FsEntry(Folder, LedgerPaths.FolderName, (d.FileAttributes & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0, d.Size,
                          d.LastWriteUtc, d.CreationUtc, d.LastAccessUtc, d.FileAttributes)
            : null;
        var exists = folder is { IsDirectory: true };
        var topLevel = exists ? _lister.Enumerate(Folder, recurse: false, NoExclusions) : null;
        var inSyncRoot = _facts.InSyncRoot(exists ? Folder : _videoRoot);

        var ownAttributes = Kernel32.TryGetAttributes(OwnFile, out _);
        var writable = exists
            ? AccessProbe.Has(Folder, AccessProbe.FILE_ADD_FILE)
              && (ownAttributes is not uint own
                  || ((own & Kernel32.FILE_ATTRIBUTE_READONLY) == 0 && AccessProbe.Has(OwnFile, AccessProbe.FILE_APPEND_DATA)))
            : AccessProbe.Has(_videoRoot, AccessProbe.FILE_ADD_FILE);

        return new LedgerFolderFacts(VideoRootExists: true, folder, topLevel, inSyncRoot, writable);
    }

    /// <summary>LedgerLoader's opener: the guard first (a cloud-only file throws CloudOnlyFileException before any open).</summary>
    private Stream OpenLedgerRead(string path)
    {
        var canonical = _facts.Canonical(path);
        IoGate.Require(IoOp.ReadData, canonical, _ctx);
        return OpenShared(canonical);
    }

    private static FileStream OpenShared(string path)
    {
#pragma warning disable RS0030 // IO layer: ledger exemption read, FileShare.ReadWrite (Ref §4.3)
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite, Options = FileOptions.SequentialScan,
        });
#pragma warning restore RS0030
    }

    /// <summary>Whole text of a file the caller has already cleared with IoGate (backup load, [Copy]).</summary>
    private static string ReadAllText(string path)
    {
        using var stream = OpenShared(path);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    private static bool IsDirectory(uint? attributes) => attributes is uint a && (a & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0;
}
