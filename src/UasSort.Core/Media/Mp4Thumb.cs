
namespace UasSort.Core.Media;

/// <summary>Finds the <c>udta/meta/ilst/tnal</c> 160×90 JPEG (Ref §6.3 step 8) by walking headers only.</summary>
public static class Mp4Thumb
{
    /// <summary>The byte range of the JPEG inside <c>tnal</c>'s <c>data</c> box, or null.</summary>
    public static ByteRange? Find(BlockCache cache, Mp4Box moov)
    {
        ArgumentNullException.ThrowIfNull(cache);
        if (Mp4Boxes.Child(cache, moov, "udta") is not { Truncated: false } udta) return null;
        if (Mp4Boxes.Child(cache, udta, "meta") is not { Truncated: false } meta) return null;
        // `meta` is a full box (version + flags) in DJI files; a QuickTime-style `meta` starts with `hdlr` directly.
        byte[] probe = cache.ReadAt(meta.Body + 4, 4);
        long childStart = probe.AsSpan().SequenceEqual("hdlr"u8) ? meta.Body : meta.Body + 4;
        Mp4Box? ilst = null;
        foreach (Mp4Box b in Mp4Boxes.Walk(cache, childStart, meta.End))
        {
            if (b.Type == "ilst")
            {
                ilst = b;
                break;
            }
        }
        if (ilst is not { Truncated: false } list) return null;
        if (Mp4Boxes.Child(cache, list, "tnal") is not { Truncated: false } tnal) return null;
        long start, length;
        if (Mp4Boxes.Child(cache, tnal, "data") is { Truncated: false } data)
        {
            start = data.Body + 8;            // data payload: type (4) + locale (4), then the image
            length = data.End - start;
        }
        else
        {
            start = tnal.Body;
            length = tnal.End - tnal.Body;
        }
        return length is > 0 and <= int.MaxValue ? new ByteRange(start, (int)length) : null;
    }
}
