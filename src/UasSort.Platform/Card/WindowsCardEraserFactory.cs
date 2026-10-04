using System.IO.Enumeration;
using UasSort.Platform.Io;

namespace UasSort.Platform.Card;

/// <summary>Re-derives the cleanup volume check from Win32 (Ref §10.6); never trusts CardSource flags or a caller's context.</summary>
public sealed class WindowsCardEraserFactory : ICardEraserFactory
{
    private static readonly IReadOnlySet<string> NoExclusions = new HashSet<string>();
    private readonly Settings _settings;
    private readonly string _appDataDir;
    private readonly string _machine;
    private readonly IPathFacts _facts;
    private readonly IDirectoryLister _lister;
    private readonly IVolumeFacts _volumes;
    private readonly string _systemVolumeRoot;

    public WindowsCardEraserFactory(Settings settings, string appDataDir, string machine, IPathFacts facts, IDirectoryLister lister)
        : this(settings, appDataDir, machine, facts, lister, new WindowsVolumeFacts(), KnownFolders.SystemVolumeRoot()) { }

    internal WindowsCardEraserFactory(Settings settings, string appDataDir, string machine, IPathFacts facts, IDirectoryLister lister,
                                      IVolumeFacts volumeFacts, string systemVolumeRoot)
    {
        _settings = settings;
        _appDataDir = appDataDir;
        _machine = machine;
        _facts = facts;
        _lister = lister;
        _volumes = volumeFacts;
        _systemVolumeRoot = systemVolumeRoot;
    }

    public ICardEraser Open(CardSource source, CardIdentity pinned, ConfirmedCleanupPlan plan)
    {
        if (source.IsBrowsedFolder) throw Refuse("the source is a browsed folder, not a detected card");
        if (source.IsWriteProtected) throw Refuse("the card is write-protected");
        var root = _facts.Canonical(source.Root);
        if (_volumes.VolumePathName(root) is not { } volumeRoot || !Same(volumeRoot, root)) throw Refuse($"{root} is not a volume root");
        var v = _volumes.Describe(Sep(root)) ?? throw Refuse($"the facts of volume {root} can't be read");
        if (v.Identity.FileSystem is not ("exFAT" or "FAT32")) throw Refuse($"file system {v.Identity.FileSystem} is not exFAT or FAT32");
        if (v.Identity != pinned) throw Refuse("the volume's identity differs from the scanned card");
        if (v.IsReadOnlyVolume) throw Refuse("the volume is write-protected");
        if (BusRefusal(v.BusType, v.RemovableMedia) is { } bus) throw Refuse(bus);
        if (v.IsSystemBootOrPaging || Same(root, _systemVolumeRoot)) throw Refuse("it is the system, boot or paging volume");
        foreach (var configured in new[] { _settings.VideoRoot, _settings.PhotoRoot, _appDataDir }.Concat(_settings.PreviousPhotoRoots))
            if (_volumes.VolumePathName(_facts.Canonical(configured)) is { } on && Same(on, root))
                throw Refuse($"{configured} lies on this volume");
        var misc = _lister.Enumerate(Path.Join(root, "MISC"), recurse: false, NoExclusions).Entries;
        if (!misc.Any(e => e.IsDirectory ? string.Equals(Path.GetFileName(e.FullPath), "IDX", StringComparison.OrdinalIgnoreCase)
                                         : FileSystemName.MatchesSimpleExpression("FC*.db", Path.GetFileName(e.FullPath), ignoreCase: true)))
            throw Refuse(@"no drone index (MISC\FC*.db or MISC\IDX\)");
        if (!Same(_facts.Canonical(plan.Plan.CardRoot), root)) throw Refuse("the confirmed plan names another card root");
        if (plan.Plan.Card != pinned) throw Refuse("the confirmed plan names another card");

        var ctx = GuardContexts.For(_settings, _appDataDir, _machine, _facts, root) with
        {
            SystemVolumeRoot = _systemVolumeRoot, CardIsVerifiedCardVolume = true, Cleanup = plan,
        };
        return new WindowsCardEraser(ctx);
    }

    /// <summary>Rule 4 of the cleanup volume check (Ref §10.6, user decision 2026-10-04): removable media on any bus (an SD/MMC slot,
    /// a USB reader, or a PCIe card reader that reports Scsi or Unknown), or an Sd/Mmc bus. Fixed media (a USB or SATA disk, NVMe:
    /// RemovableMedia false) is refused. Null when it passes, else why.</summary>
    internal static string? BusRefusal(string busType, bool removableMedia)
        => removableMedia || busType is "Sd" or "Mmc"
            ? null
            : $"rule 4: bus {(busType.Length == 0 ? "(none)" : busType)}, removable media false: fixed media, not a card";

    private static UnsafeIoException Refuse(string why) => new($"Card cleanup refused: {why}");
    private static string Sep(string p) => p.EndsWith('\\') ? p : p + '\\';
    private static bool Same(string a, string b) => string.Equals(Sep(a), Sep(b), StringComparison.OrdinalIgnoreCase);
}
