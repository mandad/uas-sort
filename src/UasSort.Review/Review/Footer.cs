// src/UasSort.Review/Review/Footer.cs
namespace UasSort.Review;

/// <summary>Footer totals per destination drive against free space (Ref §9.10).</summary>
public static class Footer
{
    public static string Text(Plan plan, IFreeSpace space)
    {
        var settings = plan.Base.Scan.Settings;
        var included = plan.Base.Items.Where(i => plan.Included.Contains(i.Raw.Unit.Id)).ToList();
        var videos = included.Where(i => i.Raw.Kind == ItemKind.Video).ToList();
        var photos = included.Where(i => i.Raw.Kind != ItemKind.Video).ToList();
        var shownDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();

        string Segment(int n, string one, string many, long bytes, string root)
        {
            var drive = Fmt.Drive(root);
            var free = shownDrives.Add(drive) && space.FreeBytes(root) is { } f ? $" ({Fmt.Size(f)} free)" : "";
            return $"{Fmt.Count(n, one, many)} · {Fmt.Size(bytes)} → {drive}{free}";
        }

        if (videos.Count > 0) parts.Add(Segment(videos.Count, "video", "videos", videos.Sum(i => i.Raw.Bytes), settings.VideoRoot));
        if (photos.Count > 0) parts.Add(Segment(photos.Count, "photo", "photos", photos.Sum(i => i.Raw.Bytes), settings.PhotoRoot));
        return parts.Count == 0 ? "Nothing to copy" : string.Join(" · ", parts);
    }
}
