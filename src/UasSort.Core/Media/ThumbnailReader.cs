namespace UasSort.Core.Media;

/// <summary>Where an item's stored thumbnail lives on the card.</summary>
public readonly record struct ThumbLocation(string CardRelPath, ByteRange Range);

/// <summary>
/// <see cref="IThumbnailSource"/> over the card (Ref §4.2): returns the stored JPEG bytes (MP4 <c>tnal</c> 160×90, DNG IFD0
/// 160×120, a set's first frame). One card read at a time; the last card handle stays open for the next request.
/// <see cref="Pause"/> closes it and makes <see cref="GetAsync"/> return empty until every pause is disposed.
/// </summary>
public sealed class ThumbnailReader : IThumbnailSource, IDisposable
{
    private readonly ICardReader _reader;
    private readonly ImmutableDictionary<ItemId, ThumbLocation> _locations;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _openPath;
    private Stream? _open;
    private int _pauses;
    private bool _disposed;   // under _gate; the gate itself is never disposed, so queued loads always get it and return empty

    public ThumbnailReader(ICardReader reader, IEnumerable<RawItem> items)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(items);
        _reader = reader;
        var builder = ImmutableDictionary.CreateBuilder<ItemId, ThumbLocation>();
        foreach (RawItem item in items)
            if (Locate(item) is { } location) builder[item.Unit.Id] = location;
        _locations = builder.ToImmutable();
    }

    /// <summary>The stored thumbnail of a harvested item, or null when it has none (a placeholder shows).</summary>
    public static ThumbLocation? Locate(RawItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Unit switch
        {
            VideoUnit v => item.Mp4?.Thumb is { } r ? new ThumbLocation(v.Mp4.RelPath, r) : null,
            PhotoUnit p => item.Still?.Thumb is { } r ? new ThumbLocation(p.Primary.RelPath, r) : null,
            SetUnit s => item.Still?.Thumb is { } r && !s.Members.IsDefaultOrEmpty
                ? new ThumbLocation(MetadataHarvester.FirstFrame(s).RelPath, r) : null,
        };
    }

    public async ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct)
    {
        if (!_locations.TryGetValue(id, out ThumbLocation location)) return ReadOnlyMemory<byte>.Empty;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed || _pauses > 0) return ReadOnlyMemory<byte>.Empty;
            Stream s = Handle(location.CardRelPath);
            if (location.Range.Offset + location.Range.Length > s.Length) return ReadOnlyMemory<byte>.Empty;
            var buffer = new byte[location.Range.Length];
            s.Position = location.Range.Offset;
            await s.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
            return buffer;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CloseHandle();
            return ReadOnlyMemory<byte>.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IDisposable Pause()
    {
        _gate.Wait();
        try
        {
            if (_disposed) return NoPause.Instance;
            _pauses++;
            CloseHandle();
        }
        finally
        {
            _gate.Release();
        }
        return new PauseToken(this);
    }

    /// <summary>Closes the card handle once any load in progress finishes. Loads queued behind it (a rescan while thumbnails load)
    /// then return empty instead of opening a new handle; the gate is not disposed, so none of them hangs or throws.</summary>
    public void Dispose()
    {
        _gate.Wait();
        try
        {
            _disposed = true;
            CloseHandle();
        }
        finally
        {
            _gate.Release();
        }
        GC.SuppressFinalize(this);
    }

    private Stream Handle(string cardRelPath)
    {
        if (_open is not null && _openPath == cardRelPath) return _open;
        CloseHandle();
        _open = _reader.OpenRandom(cardRelPath);
        _openPath = cardRelPath;
        return _open;
    }

    private void CloseHandle()
    {
        _open?.Dispose();
        _open = null;
        _openPath = null;
    }

    private void EndPause()
    {
        _gate.Wait();
        try
        {
            if (!_disposed) _pauses--;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class NoPause : IDisposable
    {
        public static readonly NoPause Instance = new();
        public void Dispose() { }
    }

    private sealed class PauseToken(ThumbnailReader owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.EndPause();
        }
    }
}
