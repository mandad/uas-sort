// src/UasSort.Review/Review/Footer.cs
namespace UasSort.Review;

/// <summary>Footer totals per destination drive against free space (Ref §9.10). The totals are what Commit will copy: they come from
/// Core's pure OffloadCompiler (Ref §10.1), so a skipped group, an already imported item, the JPG twin and a resumed set's missing
/// members count exactly as the offload will.</summary>
public static class Footer
{
    public static string Text(Plan plan, IFreeSpace space)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(space);
        var settings = plan.Base.Scan.Settings;
        var (videos, photos) = Totals(plan);
        var shownDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();

        string Segment(int n, string one, string many, long bytes, string root)
        {
            var drive = Fmt.Drive(root);
            var free = shownDrives.Add(drive) && space.FreeBytes(root) is { } f ? $" ({Fmt.Size(f)} free)" : "";
            return $"{Fmt.Count(n, one, many)} · {Fmt.Size(bytes)} → {drive}{free}";
        }

        if (videos.Count > 0) parts.Add(Segment(videos.Count, "video", "videos", videos.Bytes, settings.VideoRoot));
        if (photos.Count > 0) parts.Add(Segment(photos.Count, "photo", "photos", photos.Bytes, settings.PhotoRoot));
        return parts.Count == 0 ? "Nothing to copy" : string.Join(" · ", parts);
    }

    /// <summary>Items and bytes per destination root. Every video job lands under the video root and every photo or set job under
    /// the photo root (Append targets are always inside the video root). Without a pinned card identity the compiler can't run;
    /// the footer then falls back to the ticked items rather than throw.</summary>
    private static ((int Count, long Bytes) Videos, (int Count, long Bytes) Photos) Totals(Plan plan)
    {
        if (plan.Base.Scan.Inventory.Source.Identity is null)
        {
            var included = plan.Base.Items.Where(i => plan.Included.Contains(i.Raw.Unit.Id)).ToList();
            var v = included.Where(i => i.Raw.Kind == ItemKind.Video).ToList();
            var p = included.Where(i => i.Raw.Kind != ItemKind.Video).ToList();
            return ((v.Count, v.Sum(i => i.Raw.Bytes)), (p.Count, p.Sum(i => i.Raw.Bytes)));
        }
        var jobs = OffloadCompiler.Compile(plan, "footer").Jobs;
        (int, long) Of(DestRoot root)
        {
            var mine = jobs.Where(j => j.Root == root).ToList();
            return (mine.Select(j => j.Item).Distinct().Count(), mine.Sum(j => j.Size));
        }
        return (Of(DestRoot.Video), Of(DestRoot.Photo));
    }
}
