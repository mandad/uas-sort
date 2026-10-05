// src/UasSort.App/Services/CardThumbnails.cs — the App's one IThumbnailSource; swaps the per-scan ThumbnailReader (Ref §9.5)
namespace UasSort.App.Services;

/// <summary>Stable facade over the current card's ThumbnailReader. Pause is honoured across a swap: while any pause is held
/// GetAsync returns empty bytes without touching the card (Commit and Cleanup pause thumbnails, Ref §10.3, §10.6).</summary>
public sealed partial class CardThumbnails : IThumbnailSource
{
    private readonly Lock _gate = new();
    private ThumbnailReader? _current;
    private int _pauses;

    public void Use(ICardReader reader, IEnumerable<RawItem> items)
    {
        var next = new ThumbnailReader(reader, items);
        ThumbnailReader? old;
        lock (_gate)
        {
            old = _current;
            _current = next;
        }
        old?.Dispose();
    }

    /// <summary>Picture Offload cleanup's thumbnails (spec 2026-10-04 §2): "photo-root:" keys go here, every other key to the card.</summary>
    public PhotoRootThumbnails? PhotoRoot { get; set; }

    public ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct)
    {
        if (PhotoRootThumbnails.IsKey(id)) return PhotoRoot?.GetAsync(id, ct) ?? ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
        ThumbnailReader? reader;
        lock (_gate) reader = _pauses > 0 ? null : _current;
        return reader?.GetAsync(id, ct) ?? ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
    }

    public IDisposable Pause()
    {
        IDisposable? inner;
        lock (_gate)
        {
            _pauses++;
            inner = _current?.Pause();
        }
        return new Resume(this, inner);
    }

    private sealed partial class Resume(CardThumbnails owner, IDisposable? inner) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            inner?.Dispose();
            lock (owner._gate) owner._pauses--;
        }
    }
}
