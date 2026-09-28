// src/UasSort.Core/Guard/IoGuardPolicy.cs
namespace UasSort.Core;

/// <summary>The one set of IO rules (Ref §4.3). Pure; Platform and FakeFileSystem both call it before every open, create,
/// attribute change, delete or rename. Listings and attribute reads are not checked.</summary>
public static class IoGuardPolicy
{
    public const uint FileAttributeReadOnly = 0x1;
    public const uint FileAttributeHidden = 0x2;
    public const uint FileAttributeDirectory = 0x10;
    public const uint FileAttributeArchive = 0x20;
    public const uint FileAttributeOffline = 0x1000;
    public const uint FileAttributeNotContentIndexed = 0x2000;
    public const uint FileAttributeRecallOnOpen = 0x40000;
    public const uint FileAttributePinned = 0x80000;
    public const uint FileAttributeUnpinned = 0x100000;
    public const uint FileAttributeRecallOnDataAccess = 0x400000;
    public const string TempSuffix = ".uas-sort.tmp";

    private const uint PlaceholderBits = FileAttributeOffline | FileAttributeRecallOnOpen | FileAttributeRecallOnDataAccess;

    public static GuardDecision Check(IoOp op, string canonicalPath, uint? attributes, GuardContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var path = PathRules.Normalize(canonicalPath);
        var ledgerDir = LedgerPaths.For(ctx.VideoRoot);

        // Rule 1: attributes first. Null = the target doesn't exist, accepted only by the creating ops.
        if (attributes is null && op is not (IoOp.CreateNew or IoOp.CreateDir or IoOp.AppendOwnLedger))
            return Unsafe(op, path, "the target doesn't exist or its attributes couldn't be read");
        if (attributes is uint a && (a & PlaceholderBits) != 0 && op != IoOp.SetPinned)
            return IsTopLevelLedgerFile(path, ledgerDir) ? new GuardCloudOnly(path) : new GuardHydration(path, a);

        // Rule 2: the card root. CardDelete has its own rules (Task 02.7).
        if (op == IoOp.CardDelete) return Unsafe(op, path, "card deletes need a confirmed cleanup plan");
        if (ctx.CardRoot is { } card && PathRules.IsSameOrUnder(path, card))
            return op == IoOp.ReadData ? Allow() : Unsafe(op, path, "nothing on the card is ever created, written, renamed or changed");

        // Rule 3: the ledger folder exemption.
        if (PathRules.IsSameOrUnder(path, ledgerDir)) return CheckLedgerFolder(op, path, attributes, ledgerDir, ctx);

        // Rule 4: library roots.
        if (IsUnderLibraryRoot(path, ctx)) return CheckLibrary(op, path, ctx);

        // Rule 5: Platform's own stores.
        if (PathRules.IsSameOrUnder(path, ctx.AppDataDir)) return Allow();

        // Rule 6.
        return Unsafe(op, path, "outside every configured root");
    }

    private static GuardDecision CheckLedgerFolder(IoOp op, string path, uint? attributes, string ledgerDir, GuardContext ctx)
    {
        var parent = PathRules.Parent(path);
        var topLevel = parent is not null && PathRules.Equal(parent, ledgerDir);
        var isDirectory = attributes is uint a && (a & FileAttributeDirectory) != 0;
        var allowed = op switch
        {
            IoOp.ReadData => topLevel && !isDirectory && LedgerPaths.IsLedgerFileName(PathRules.FileName(path)),
            IoOp.AppendOwnLedger => PathRules.Equal(path, LedgerPaths.OwnFile(ctx.VideoRoot, ctx.Machine)),
            IoOp.CreateDir or IoOp.SetPinned => PathRules.Equal(path, ledgerDir),
            _ => false,
        };
        return allowed ? Allow() : Unsafe(op, path, "the ledger folder allows only reading ledger*.jsonl, appending the own file, creating and pinning the folder");
    }

    private static GuardDecision CheckLibrary(IoOp op, string path, GuardContext ctx)
    {
        var isTemp = PathRules.FileName(path).EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase);
        var ownTemp = isTemp && PathRules.SetContains(ctx.OwnTempsThisRun, path);
        var allowed = op switch
        {
            IoOp.CreateNew => isTemp,
            IoOp.ReadData or IoOp.SetAttributesOrTimes or IoOp.Rename => ownTemp,
            IoOp.Delete => isTemp,
            IoOp.OpenForFlush => PathRules.SetContains(ctx.RenamedThisRun, path)
                                 || PathRules.SetContains(ctx.NewFolderDirs, path)
                                 || HoldsRenamedFile(path, ctx),
            IoOp.CreateDir => PathRules.SetContains(ctx.NewFolderDirs, path),
            _ => false,
        };
        return allowed ? Allow() : Unsafe(op, path, "pre-existing library content is never opened, changed or created");
    }

    private static bool HoldsRenamedFile(string dir, GuardContext ctx)
    {
        foreach (var r in ctx.RenamedThisRun)
            if (PathRules.Parent(r) is { } parent && PathRules.Equal(parent, dir)) return true;
        return false;
    }

    private static bool IsUnderLibraryRoot(string path, GuardContext ctx)
    {
        if (PathRules.IsSameOrUnder(path, ctx.VideoRoot) || PathRules.IsSameOrUnder(path, ctx.PhotoRoot)) return true;
        foreach (var p in ctx.PreviousPhotoRoots)
            if (PathRules.IsSameOrUnder(path, p)) return true;
        return false;
    }

    private static bool IsTopLevelLedgerFile(string path, string ledgerDir)
        => PathRules.Parent(path) is { } parent && PathRules.Equal(parent, ledgerDir)
           && LedgerPaths.IsLedgerFileName(PathRules.FileName(path));

    private static GuardDecision Allow() => new GuardAllow();

    private static GuardDecision Unsafe(IoOp op, string path, string why) => new GuardUnsafe($"{op} {path}: {why}");
}
