namespace UasSort.Core.Media;

/// <summary>
/// GPS from a clip without <c>moov</c> (Ref §6.3 step 7): DJI writes djmd sample 0 first in <c>mdat</c>. Tries the
/// <c>mdat</c> payload start, then file offset 512; reads 4 KB and decodes top-level fields 1–3.
/// </summary>
public static class Mp4MdatHead
{
    public const long FixedOffset = 512;
    public const int HeadBytes = 4096;

    /// <summary>
    /// The first reading whose protocol ends in ".proto" and has a fix; else the first such reading without a fix;
    /// else null (nothing DJI-like at either place).
    /// </summary>
    public static DjmdReading? TryRead(BlockCache cache, Mp4Box? mdat)
    {
        ArgumentNullException.ThrowIfNull(cache);
        DjmdReading? withoutFix = null;
        long[] starts = mdat is { } m && m.Body != FixedOffset ? [m.Body, FixedOffset] : [FixedOffset];
        foreach (long start in starts)
        {
            if (start >= cache.Length) continue;
            byte[] head = cache.ReadAt(start, HeadBytes);
            int length = TopFieldsLength(head);
            if (length == 0) continue;
            DjmdReading? r = DjmdDecoder.Decode(head.AsMemory(0, length), null);
            if (r?.Protocol is not { } protocol || !protocol.EndsWith(".proto", StringComparison.Ordinal)) continue;
            if (r.Point is { } p && DjmdDecoder.IsFix(p)) return r;
            withoutFix ??= r;
        }
        return withoutFix;
    }

    /// <summary>Length of the leading run of length-delimited top-level fields 1–3, stopping after field 3.</summary>
    private static int TopFieldsLength(ReadOnlySpan<byte> head)
    {
        int p = 0, good = 0;
        while (p < head.Length)
        {
            int q = p;
            if (!Protobuf.TryReadVarint(head, ref q, out ulong key)) break;
            ulong number = key >> 3;
            if ((key & 7) != 2 || number is < 1 or > 3) break;
            if (!Protobuf.TryReadVarint(head, ref q, out ulong length) || length > (ulong)(head.Length - q)) break;
            p = q + (int)length;
            good = p;
            if (number == 3) break;
        }
        return good;
    }
}
