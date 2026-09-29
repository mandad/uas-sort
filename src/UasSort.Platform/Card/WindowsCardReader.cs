using UasSort.Platform.Io;

namespace UasSort.Platform.Card;

/// <summary>Read-only card access bound to one anchored root and one identity (Ref §4.1, §4.3 Card).</summary>
public sealed class WindowsCardReader : ICardReader
{
    private static readonly IReadOnlySet<string> NoExclusions = new HashSet<string>();
    private readonly GuardContext _ctx;
    private readonly IDirectoryLister _lister;
    private readonly string _volumeRoot;

    internal WindowsCardReader(CardSource source, CardIdentity identity, GuardContext ctx, IDirectoryLister lister)
    {
        _ctx = ctx;
        _lister = lister;
        Root = ctx.CardRoot ?? throw new ArgumentException("the card GuardContext has no CardRoot", nameof(ctx));
        BoundIdentity = identity;
        _volumeRoot = VolumeQuery.VolumePathName(Root) ?? Path.GetPathRoot(Root)!;
        Source = source;
    }

    public CardSource Source { get; }
    public string Root { get; }
    public CardIdentity BoundIdentity { get; }

    public CardIdentity CurrentIdentity()
        => VolumeQuery.Identity(_volumeRoot) ?? throw new IOException($"The card volume {_volumeRoot} is not available");

    public Stream OpenRandom(string cardRelPath) => Open(cardRelPath, FileOptions.RandomAccess);
    public Stream OpenSequential(string cardRelPath) => Open(cardRelPath, FileOptions.SequentialScan);

    public FsEntry Stat(string cardRelPath)
    {
        var full = Resolve(cardRelPath);
        if (!Kernel32.TryGetAttributeData(full, out var d, out var error))
            throw Kernel32.IsNotFound(error) ? new FileNotFoundException("Not on the card", full)
                                             : new IOException($"Stat {full} failed (Win32 error {error})", error);
        return new FsEntry(full, Path.GetRelativePath(Root, full), (d.FileAttributes & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0, d.Size,
                           d.LastWriteUtc, d.CreationUtc, d.LastAccessUtc, d.FileAttributes);
    }

    public ListingResult Relist() => _lister.Enumerate(Root, recurse: true, NoExclusions);

    public CardSpace Space()
    {
        var (free, total) = VolumeQuery.Space(Root) ?? throw new IOException($"The card volume {_volumeRoot} is not available");
        return new CardSpace(free, total, VolumeQuery.ClusterBytes(_volumeRoot));
    }

    private FileStream Open(string cardRelPath, FileOptions options)
    {
        var full = Resolve(cardRelPath);
        IoGate.Require(IoOp.ReadData, full, _ctx);
#pragma warning disable RS0030 // IO layer: the card reader; FileAccess.Read only, never a write right (Ref §4.3)
        return new FileStream(full, new FileStreamOptions
        {
            Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite, Options = options, BufferSize = 0,
        });
#pragma warning restore RS0030
    }

    private string Resolve(string cardRelPath) => Path.GetFullPath(Path.Join(Root, cardRelPath.Replace('/', '\\')));
}
