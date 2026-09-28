namespace UasSort.Platform.Win32;

/// <summary>
/// An existence-based named-mutex lock: whoever creates the named mutex owns the name until it disposes its handle
/// (or the process exits). Ownership is never taken, so the lock is not thread-affine and any thread may dispose it.
/// Used for the single-instance fallback (Local\uas-sort) and, from Part 09, the offload lock (Local\uas-sort-offload).
/// </summary>
public sealed partial class NamedMutexLock : IDisposable
{
    private readonly Mutex _mutex;

    private NamedMutexLock(Mutex mutex, string name)
    {
        _mutex = mutex;
        Name = name;
    }

    public string Name { get; }

    public static NamedMutexLock? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        if (createdNew)
        {
            return new NamedMutexLock(mutex, name);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose() => _mutex.Dispose();
}
