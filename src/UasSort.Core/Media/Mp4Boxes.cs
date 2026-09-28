using System.Buffers.Binary;
using System.Text;

namespace UasSort.Core.Media;

/// <summary>One ISO-BMFF box header (Ref §6.3 step 1). <see cref="Truncated"/> = the box claimed more bytes than remain.</summary>
public readonly record struct Mp4Box(string Type, long Offset, long Size, int HeaderSize, bool Truncated)
{
    public long Body => Offset + HeaderSize;
    public long End => Offset + Size;
}

/// <summary>Lazy box walker: reads headers only and seeks over payloads, so <c>mdat</c> is never read.</summary>
public static class Mp4Boxes
{
    /// <summary>
    /// Walks the boxes in [start, end). Size 1 = a 64-bit size follows; size 0 = the box runs to <paramref name="end"/>;
    /// <c>uuid</c> adds 16 header bytes. A size smaller than its header ends the walk; a box that claims more than
    /// remains is returned once with <c>Truncated = true</c> (clamped to <paramref name="end"/>) and ends the walk.
    /// </summary>
    public static IEnumerable<Mp4Box> Walk(BlockCache cache, long start, long end)
    {
        ArgumentNullException.ThrowIfNull(cache);
        return WalkCore(cache, start, Math.Min(end, cache.Length));
    }

    /// <summary>The first direct child of <paramref name="parent"/> with the given type, or null.</summary>
    public static Mp4Box? Child(BlockCache cache, Mp4Box parent, string type)
    {
        foreach (Mp4Box box in Walk(cache, parent.Body, parent.End))
            if (box.Type == type) return box;
        return null;
    }

    private static IEnumerable<Mp4Box> WalkCore(BlockCache cache, long start, long end)
    {
        long pos = start;
        while (pos + 8 <= end)
        {
            byte[] h = cache.ReadAt(pos, (int)Math.Min(16, end - pos));
            if (h.Length < 8) yield break;
            long size = BinaryPrimitives.ReadUInt32BigEndian(h);
            string type = Encoding.Latin1.GetString(h, 4, 4);
            int header = 8;
            if (size == 1)
            {
                if (h.Length < 16) yield break;
                ulong large = BinaryPrimitives.ReadUInt64BigEndian(h.AsSpan(8));
                if (large > long.MaxValue) yield break;
                size = (long)large;
                header = 16;
            }
            else if (size == 0)
            {
                size = end - pos;
            }
            if (type == "uuid") header += 16;
            if (size < header) yield break;
            if (size > end - pos)
            {
                if (end - pos >= header) yield return new Mp4Box(type, pos, end - pos, header, Truncated: true);
                yield break;
            }
            yield return new Mp4Box(type, pos, size, header, Truncated: false);
            pos += size;
        }
    }
}
