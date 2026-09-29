// LruCache is written fully qualified: the App's GlobalUsings.cs arrives in Task 11.4
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace UasSort.App.Controls;

/// <summary>Ref §9.5: bytes from IThumbnailSource, decoded at 96 px, LRU of 400, one load per key in flight.
/// Empty bytes (IThumbnailSource.Pause during Commit/Cleanup, or no thumbnail) are never cached, so the placeholder shows
/// and the next realisation retries.</summary>
public sealed class ThumbnailCache(IThumbnailSource source, DispatcherQueue ui)
{
    public const int Capacity = 400;
    public const int DecodeWidth = 96;

    private readonly UasSort.Review.LruCache<ItemId, BitmapImage> _cache = new(Capacity);
    private readonly Dictionary<ItemId, Task<ImageSource?>> _inFlight = [];

    public int LoadsStarted { get; private set; }

    /// <summary>Call on the UI thread.</summary>
    public Task<ImageSource?> GetAsync(ItemId id)
    {
        if (_cache.TryGet(id, out var hit)) return Task.FromResult<ImageSource?>(hit);
        if (_inFlight.TryGetValue(id, out var running)) return running;
        var task = LoadAsync(id);
        _inFlight[id] = task;
        return task;
    }

    public void Clear() => _cache.Clear();

    /// <summary>Selftest teardown: pauses the source, which closes the card file it holds open (Ref §9.5) so the sandbox
    /// can be deleted. Later loads return the placeholder until the returned token is disposed.</summary>
    internal IDisposable PauseSource() => source.Pause();

    private async Task<ImageSource?> LoadAsync(ItemId id)
    {
        LoadsStarted++;
        try
        {
            var bytes = await Task.Run(async () => await source.GetAsync(id, CancellationToken.None)).ConfigureAwait(true);
            if (bytes.IsEmpty) return null;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes.ToArray());
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var image = new BitmapImage { DecodePixelWidth = DecodeWidth, DecodePixelType = DecodePixelType.Logical };
            await image.SetSourceAsync(stream);
            _cache.Set(id, image);
            return image;
        }
#pragma warning disable CA1031 // a bad thumbnail (card gone, corrupt JPEG, decoder error) shows the placeholder; never fatal
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
        finally
        {
            _inFlight.Remove(id);
            System.Diagnostics.Debug.Assert(ui.HasThreadAccess);
        }
    }
}
