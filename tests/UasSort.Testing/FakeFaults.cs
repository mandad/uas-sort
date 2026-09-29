namespace UasSort.Testing;

/// <summary>Fault hooks. Full paths are normalized Windows paths (PathRules.Normalize); card paths are '/'-relative.
/// All lookups are case-insensitive.</summary>
public sealed class FakeFaults
{
    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    // ── card reader (Part 07 fault injection, Part 08 executor)
    /// <summary>Card paths whose first Read throws IOException once; the re-read succeeds.</summary>
    public HashSet<string> TransientCardReadError { get; } = new(Cmp);
    /// <summary>Card paths whose every Read throws IOException while the card stays present.</summary>
    public HashSet<string> PersistentCardReadError { get; } = new(Cmp);
    /// <summary>Card path → bytes readable before the card disappears (CardRemoved becomes true).</summary>
    public Dictionary<string, long> CardVanishesAfterBytes { get; } = new(Cmp);
    /// <summary>The card is gone: identity, stat, reads and deletes fail with ERROR_NOT_READY (21).</summary>
    public bool CardRemoved { get; set; }
    /// <summary>Called with the 1-based count of CurrentIdentity() calls; a non-null result is returned instead of the volume's identity.</summary>
    public Func<int, CardIdentity?>? IdentityOnCall { get; set; }

    // ── destination (FakeFileOps, Part 07)
    /// <summary>Total bytes all temp writes may take before IOException "disk full" (HResult 0x80070070).</summary>
    public long? DiskFullAfterBytes { get; set; }
    /// <summary>Final path → number of VerifyHash calls that see a flipped bit.</summary>
    public Dictionary<string, int> CorruptVerify { get; } = new(Cmp);
    /// <summary>Final paths whose verify falls back to VerifyMode.Cached.</summary>
    public HashSet<string> UnbufferedUnsupported { get; } = new(Cmp);
    /// <summary>Final paths where another file appears just before RenameNoReplace.</summary>
    public HashSet<string> TargetAppearsBeforeRename { get; } = new(Cmp);
    /// <summary>Final path → the size ConfirmFinal sees after the rename.</summary>
    public Dictionary<string, long> SizeAfterRename { get; } = new(Cmp);
    /// <summary>Full paths whose append stream throws IOException on write (ledger append failure).</summary>
    public HashSet<string> AppendFails { get; } = new(Cmp);
    /// <summary>Roots that vanished: every IFileOps call under one throws DirectoryNotFoundException.</summary>
    public HashSet<string> LostRoots { get; } = new(Cmp);

    // ── lister and eraser
    /// <summary>Directory full path → Win32 error the lister records instead of entering it.</summary>
    public Dictionary<string, int> EnumerationErrors { get; } = new(Cmp);
    /// <summary>Card path → Win32 error DeleteFile returns (19 write-protect, 5 access denied, 32 sharing, 21 not ready).</summary>
    public Dictionary<string, int> DeleteErrors { get; } = new(Cmp);
    /// <summary>Card paths whose delete succeeds but whose entry stays listed (another program holds it open).</summary>
    public HashSet<string> DeletePending { get; } = new(Cmp);
    /// <summary>Called by FakeCardEraser.DeleteFile with the '/' card path once the guard has allowed the delete and before
    /// DeleteErrors or the delete itself apply (Part 08: swap, remove or cancel between two deletes).</summary>
    public Action<string>? OnCardDelete { get; set; }

    internal bool IsLost(string fullPath)
    {
        foreach (var root in LostRoots)
            if (PathRules.IsSameOrUnder(fullPath, root)) return true;
        return false;
    }
}
