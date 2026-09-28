using System.Runtime.CompilerServices;

namespace UasSort.Core.Media;

/// <summary>
/// Reads card metadata sequentially: videos, then photos, then the first frame of each set (Ref §4.2). A probe failure
/// becomes <see cref="RawItem.ProbeError"/>, never an exception; cancellation and <see cref="UnsafeIoException"/> propagate.
/// </summary>
public static class MetadataHarvester
{
    public static async IAsyncEnumerable<RawItem> HarvestAsync(CardInventory inventory, ICardReader reader,
                                                                IProgress<ScanProgress> progress,
                                                                [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(progress);
        List<MediaUnit> order = [.. inventory.Units.Where(u => u is VideoUnit),
                                 .. inventory.Units.Where(u => u is PhotoUnit),
                                 .. inventory.Units.Where(u => u is SetUnit)];
        for (int i = 0; i < order.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            progress.Report(new ScanProgress(ScanPhase.ReadingMetadata, i, order.Count, PrimaryPath(order[i])));
            yield return Harvest(order[i], reader);
        }
        progress.Report(new ScanProgress(ScanPhase.ReadingMetadata, order.Count, order.Count, null));
    }

    /// <summary>Probes one unit; failures become <see cref="RawItem.ProbeError"/>.</summary>
    public static RawItem Harvest(MediaUnit unit, ICardReader reader)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(reader);
        return unit switch
        {
            VideoUnit v => Video(v, reader),
            PhotoUnit p => Photo(p, reader),
            SetUnit s => Set(s, reader),
        };
    }

    /// <summary>A set's first frame: the member with the lowest file name (ordinal, case-insensitive).</summary>
    public static CardEntry FirstFrame(SetUnit set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return set.Members.OrderBy(m => DroneStampParser.FileName(m.RelPath), StringComparer.OrdinalIgnoreCase)
                          .ThenBy(m => m.RelPath, StringComparer.Ordinal)
                          .First();
    }

    private static string PrimaryPath(MediaUnit unit) => unit switch
    {
        VideoUnit v => v.Mp4.RelPath,
        PhotoUnit p => p.Primary.RelPath,
        SetUnit s => s.Members.IsDefaultOrEmpty ? s.Id.CardRelPath : FirstFrame(s).RelPath,
    };

    private static RawItem Video(VideoUnit v, ICardReader reader)
    {
        string name = DroneStampParser.FileName(v.Mp4.RelPath);
        Mp4Info? info = null;
        string? error = null;
        try
        {
            using Stream s = reader.OpenRandom(v.Mp4.RelPath);
            info = Mp4Probe.Read(s);
        }
#pragma warning disable CA1031 // a probe failure becomes ProbeError, not an exception (Ref §4.2)
        catch (Exception ex) when (ex is not OperationCanceledException and not UnsafeIoException)
#pragma warning restore CA1031
        {
            error = Describe(ex);
        }
        return new RawItem(v, ItemKind.Video, name, v.Mp4.Size, v.Mp4.MtimeUtc, DroneStampParser.FromFileName(name), info, null, error);
    }

    private static RawItem Photo(PhotoUnit p, ICardReader reader)
    {
        string name = DroneStampParser.FileName(p.Primary.RelPath);
        (StillInfo? still, string? error) = ReadStill(p.Primary.RelPath, reader);
        long bytes = p.Primary.Size + (p.JpgTwin?.Size ?? 0);
        DateTime? stamp = still?.DtoNaive ?? DroneStampParser.FromFileName(name);
        return new RawItem(p, ItemKind.Photo, name, bytes, p.Primary.MtimeUtc, stamp, null, still, error);
    }

    private static RawItem Set(SetUnit s, ICardReader reader)
    {
        if (s.Members.IsDefaultOrEmpty)
            return new RawItem(s, ItemKind.Set, s.SetName, 0, default, null, null, null, "empty set");
        CardEntry first = FirstFrame(s);
        (StillInfo? still, string? error) = ReadStill(first.RelPath, reader);
        long bytes = s.Members.Sum(m => m.Size);
        DateTime? stamp = still?.DtoNaive ?? DroneStampParser.FromFileName(first.RelPath);
        return new RawItem(s, ItemKind.Set, s.SetName, bytes, first.MtimeUtc, stamp, null, still, error);
    }

    private static (StillInfo? Still, string? Error) ReadStill(string relPath, ICardReader reader)
    {
        try
        {
            using Stream s = reader.OpenRandom(relPath);
            return (StillProbe.Read(s), null);
        }
#pragma warning disable CA1031 // a probe failure becomes ProbeError, not an exception (Ref §4.2)
        catch (Exception ex) when (ex is not OperationCanceledException and not UnsafeIoException)
#pragma warning restore CA1031
        {
            return (null, Describe(ex));
        }
    }

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";
}
