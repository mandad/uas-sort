using System.Buffers.Binary;
using System.Text;

namespace UasSort.Core.Media;

/// <summary>Where sample k lives: file offset, size and its 1-based <c>stsd</c> description index.</summary>
public readonly record struct SampleLocation(long Offset, int Size, int DescriptionIndex);

/// <summary>
/// Lazy random access into one track's sample tables (Ref §6.3 step 2): <c>stsc</c> is kept in memory;
/// <c>stsz</c>/<c>stz2</c> and <c>stco</c>/<c>co64</c> entries are read on demand through the block cache.
/// </summary>
public sealed class Mp4SampleTable
{
    private const int MaxStscEntries = 1 << 16;
    private const int MaxStsdEntries = 64;

    private readonly BlockCache _cache;
    private readonly (long FirstChunk, long PerChunk, int Description)[] _stsc;
    private readonly long _constantSize;
    private readonly long _sizeEntries;
    private readonly int _stz2Bits;
    private readonly long _chunkEntries;
    private readonly int _chunkWidth;
    private readonly long _chunkCount;

    private Mp4SampleTable(BlockCache cache, ImmutableArray<string> formats, (long, long, int)[] stsc, long constantSize,
                           int sampleCount, long sizeEntries, int stz2Bits, long chunkEntries, int chunkWidth, long chunkCount)
    {
        _cache = cache;
        Formats = formats;
        _stsc = stsc;
        _constantSize = constantSize;
        SampleCount = sampleCount;
        _sizeEntries = sizeEntries;
        _stz2Bits = stz2Bits;
        _chunkEntries = chunkEntries;
        _chunkWidth = chunkWidth;
        _chunkCount = chunkCount;
    }

    /// <summary>The <c>stsd</c> entry formats in order, e.g. ["mett", "djmd"].</summary>
    public ImmutableArray<string> Formats { get; }

    public int SampleCount { get; }

    /// <summary>1-based index of the <c>djmd</c> entry; 0 when there is none.</summary>
    public int DjmdDescription => Formats.IndexOf("djmd") + 1;

    /// <summary>Reads the entry formats of an <c>stsd</c> box, one 8-byte entry header at a time.</summary>
    public static ImmutableArray<string> ReadFormats(BlockCache cache, Mp4Box stsd)
    {
        ArgumentNullException.ThrowIfNull(cache);
        byte[] head = cache.ReadAt(stsd.Body, 8);
        if (head.Length < 8) return [];
        uint count = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(4));
        var formats = ImmutableArray.CreateBuilder<string>();
        long p = stsd.Body + 8;
        for (uint i = 0; i < Math.Min(count, MaxStsdEntries) && p + 8 <= stsd.End; i++)
        {
            byte[] entry = cache.ReadAt(p, 8);
            if (entry.Length < 8) break;
            uint size = BinaryPrimitives.ReadUInt32BigEndian(entry);
            formats.Add(Encoding.Latin1.GetString(entry, 4, 4));
            if (size < 8) break;
            p += size;
        }
        return formats.ToImmutable();
    }

    /// <summary>Opens the tables under <paramref name="stbl"/>; null when a required box is missing or malformed.</summary>
    public static Mp4SampleTable? Open(BlockCache cache, Mp4Box stbl)
    {
        ArgumentNullException.ThrowIfNull(cache);
        Mp4Box? stsd = null, stsc = null, stsz = null, stz2 = null, stco = null, co64 = null;
        foreach (Mp4Box b in Mp4Boxes.Walk(cache, stbl.Body, stbl.End))
        {
            switch (b.Type)
            {
                case "stsd": stsd ??= b; break;
                case "stsc": stsc ??= b; break;
                case "stsz": stsz ??= b; break;
                case "stz2": stz2 ??= b; break;
                case "stco": stco ??= b; break;
                case "co64": co64 ??= b; break;
            }
        }
        if (stsd is not { } sd || stsc is not { } sc) return null;
        if (stsz is null && stz2 is null) return null;
        if (stco is null && co64 is null) return null;

        byte[] scHead = cache.ReadAt(sc.Body, 8);
        if (scHead.Length < 8) return null;
        uint runs = BinaryPrimitives.ReadUInt32BigEndian(scHead.AsSpan(4));
        if (runs > MaxStscEntries || 8 + 12L * runs > sc.End - sc.Body) return null;
        byte[] scBody = cache.ReadAt(sc.Body + 8, (int)(12 * runs));
        if (scBody.Length < 12 * runs) return null;
        var table = new (long, long, int)[runs];
        for (int i = 0; i < runs; i++)
        {
            ReadOnlySpan<byte> e = scBody.AsSpan(12 * i, 12);
            table[i] = (BinaryPrimitives.ReadUInt32BigEndian(e), BinaryPrimitives.ReadUInt32BigEndian(e[4..]),
                        (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(e[8..]), int.MaxValue));
        }

        long constant = 0, sizeEntries;
        int bits = 0;
        uint count;
        if (stsz is { } z)
        {
            byte[] h = cache.ReadAt(z.Body, 12);
            if (h.Length < 12) return null;
            constant = BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(4));
            count = BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(8));
            sizeEntries = z.Body + 12;
        }
        else
        {
            Mp4Box z2 = stz2.GetValueOrDefault();
            byte[] h = cache.ReadAt(z2.Body, 12);
            if (h.Length < 12) return null;
            bits = h[7];
            if (bits is not (4 or 8 or 16)) return null;
            count = BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(8));
            sizeEntries = z2.Body + 12;
        }
        if (count > int.MaxValue) return null;

        Mp4Box co = co64 is { } c64 ? c64 : stco.GetValueOrDefault();
        byte[] coHead = cache.ReadAt(co.Body + 4, 4);
        if (coHead.Length < 4) return null;
        long chunks = BinaryPrimitives.ReadUInt32BigEndian(coHead);
        return new Mp4SampleTable(cache, ReadFormats(cache, sd), table, constant, (int)count, sizeEntries, bits,
                                  co.Body + 8, co64 is null ? 4 : 8, chunks);
    }

    /// <summary>Maps a 0-based sample index to its location through the <c>stsc</c> runs; null if out of range or malformed.</summary>
    public SampleLocation? Locate(int k)
    {
        if (k < 0 || k >= SampleCount) return null;
        long before = 0;
        for (int i = 0; i < _stsc.Length; i++)
        {
            (long first, long perChunk, int description) = _stsc[i];
            long nextFirst = i + 1 < _stsc.Length ? _stsc[i + 1].FirstChunk : _chunkCount + 1;
            if (first < 1 || perChunk < 1 || nextFirst < first) return null;
            long runSamples = (nextFirst - first) * perChunk;
            if (k < before + runSamples)
            {
                long rel = k - before;
                long chunk = first - 1 + rel / perChunk;
                long firstInChunk = k - rel % perChunk;
                if (ChunkOffset(chunk) is not { } offset) return null;
                if (SumSizes(firstInChunk, (int)(k - firstInChunk)) is not { } skip) return null;
                if (SampleSize(k) is not { } size || size > int.MaxValue) return null;
                return new SampleLocation(offset + skip, (int)size, description);
            }
            before += runSamples;
        }
        return null;
    }

    private long? ChunkOffset(long chunk)
    {
        if (chunk < 0 || chunk >= _chunkCount) return null;
        byte[] raw = _cache.ReadAt(_chunkEntries + _chunkWidth * chunk, _chunkWidth);
        if (raw.Length < _chunkWidth) return null;
        if (_chunkWidth == 4) return BinaryPrimitives.ReadUInt32BigEndian(raw);
        ulong wide = BinaryPrimitives.ReadUInt64BigEndian(raw);
        return wide > long.MaxValue ? null : (long)wide;
    }

    private long? SampleSize(long k)
    {
        if (_constantSize != 0) return _constantSize;
        switch (_stz2Bits)
        {
            case 16:
                byte[] w = _cache.ReadAt(_sizeEntries + 2 * k, 2);
                return w.Length < 2 ? null : BinaryPrimitives.ReadUInt16BigEndian(w);
            case 8:
                byte[] b = _cache.ReadAt(_sizeEntries + k, 1);
                return b.Length < 1 ? null : b[0];
            case 4:
                byte[] n = _cache.ReadAt(_sizeEntries + k / 2, 1);
                return n.Length < 1 ? null : k % 2 == 0 ? n[0] >> 4 : n[0] & 0xF;
            default:
                byte[] d = _cache.ReadAt(_sizeEntries + 4 * k, 4);
                return d.Length < 4 ? null : BinaryPrimitives.ReadUInt32BigEndian(d);
        }
    }

    private long? SumSizes(long first, int count)
    {
        if (count == 0) return 0;
        if (_constantSize != 0) return _constantSize * count;
        if (_stz2Bits == 0)
        {
            byte[] raw = _cache.ReadAt(_sizeEntries + 4 * first, 4 * count);
            if (raw.Length < 4 * count) return null;
            long sum = 0;
            for (int i = 0; i < count; i++) sum += BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(4 * i));
            return sum;
        }
        long total = 0;
        for (int i = 0; i < count; i++)
        {
            if (SampleSize(first + i) is not { } s) return null;
            total += s;
        }
        return total;
    }
}
