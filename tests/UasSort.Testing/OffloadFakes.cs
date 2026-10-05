// tests/UasSort.Testing/OffloadFakes.cs
using UasSort.Core;

namespace UasSort.Testing;

/// <summary>Runs its action on the first Dispose only.</summary>
internal sealed class ReleaseOnce(Action onDispose) : IDisposable
{
    private bool _done;
    public void Dispose()
    {
        if (_done) return;
        _done = true;
        onDispose();
    }
}

public sealed class FakeOffloadLock : IOffloadLock
{
    public bool HeldElsewhere { get; set; }
    public int Holds { get; private set; }
    public IDisposable? TryAcquire()
    {
        if (HeldElsewhere) return null;
        Holds++;
        return new ReleaseOnce(() => Holds--);
    }
}

public sealed class FakePowerRequest : IPowerRequest
{
    public int Active { get; private set; }
    public List<string> Reasons { get; } = [];
    public IDisposable KeepSystemAwake(string reason)
    {
        Reasons.Add(reason);
        Active++;
        return new ReleaseOnce(() => Active--);
    }
}

/// <summary>IThumbnailSource that serves empty bytes and counts pauses: Pauses = Pause() calls so far, ActivePauses = not yet released.</summary>
public sealed class FakeThumbnails : IThumbnailSource
{
    public int Pauses { get; private set; }
    public int ActivePauses { get; private set; }
    /// <summary>Same count as ActivePauses (the name the cleanup tests read).</summary>
    public int Paused => ActivePauses;
    public ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct) => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
    public IDisposable Pause()
    {
        Pauses++;
        ActivePauses++;
        return new ReleaseOnce(() => ActivePauses--);
    }
}

public sealed class MemReportStore : IReportStore
{
    public List<OffloadReport> Offload { get; } = [];
    public List<CleanupReport> Cleanup { get; } = [];
    public List<PhotoCleanupReport> Photos { get; } = [];
    public bool Throws { get; set; }

    public string Save(OffloadReport r)
    {
        if (Throws) throw new IOException("The report couldn't be written.");
        Offload.Add(r);
        return $@"{FakeLayout.AppDataDir}\reports\{r.RunId}.json";
    }

    public string Save(CleanupReport r)
    {
        if (Throws) throw new IOException("The report couldn't be written.");
        Cleanup.Add(r);
        return $@"{FakeLayout.AppDataDir}\reports\{r.RunId}-cleanup.json";
    }

    public string Save(PhotoCleanupReport r)
    {
        if (Throws) throw new IOException("The report couldn't be written.");
        Photos.Add(r);
        return $@"{FakeLayout.AppDataDir}\reports\{r.RunId}-photos.json";
    }
}

public sealed class FakeVolumeProvider(IReadOnlyList<VolumeInfo> volumes) : IVolumeProvider
{
    public IReadOnlyList<VolumeInfo> GetVolumes() => volumes;
}

public sealed class ListProgress<T> : IProgress<T>
{
    private readonly Lock _gate = new();
    private readonly List<T> _items = [];
    public List<T> Items { get { lock (_gate) { return [.. _items]; } } }
    public void Report(T value) { lock (_gate) { _items.Add(value); } }
}
