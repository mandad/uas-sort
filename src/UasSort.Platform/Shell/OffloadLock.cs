namespace UasSort.Platform.Shell;

/// <summary>Local\uas-sort-offload, held for a whole Commit or Card cleanup (Ref §4.1, §10.6 step 1).
/// Wraps Part 01's NamedMutexLock (existence-based, not thread-affine: any thread may dispose it).</summary>
public sealed class OffloadLock : IOffloadLock
{
    public const string MutexName = @"Local\uas-sort-offload";
    private readonly string _name;

    public OffloadLock() : this(MutexName) { }
    internal OffloadLock(string name) => _name = name;

    /// <returns>The held lock, or null only when another holder (another process, or a still-held lock here) has the name.</returns>
    public IDisposable? TryAcquire() => NamedMutexLock.TryAcquire(_name);
}
