namespace UasSort.Core.Media;

/// <summary>
/// Read-through cache of aligned blocks over a readable, seekable stream (Ref §6.3 step 9).
/// Every probe read goes through it, so a DJI MP4 costs about 6–7 underlying reads.
/// </summary>
public sealed class BlockCache
{
    public const int DefaultBlockSize = 4096;

    private readonly Stream _stream;
    private readonly int _blockSize;
    private readonly Dictionary<long, byte[]> _blocks = [];

    public BlockCache(Stream stream, int blockSize = DefaultBlockSize)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("The stream must be readable and seekable.", nameof(stream));
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 512);
        _stream = stream;
        _blockSize = blockSize;
        Length = stream.Length;
    }

    /// <summary>Length of the underlying stream when the cache was created.</summary>
    public long Length { get; }

    /// <summary>Number of blocks read from the underlying stream (one read per block).</summary>
    public int BlockReads { get; private set; }

    /// <summary>Returns the bytes in [offset, offset + count), clamped at the end of the stream.</summary>
    public byte[] ReadAt(long offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        long end = Math.Min(offset + count, Length);
        if (end <= offset) return [];
        var result = new byte[end - offset];
        long pos = offset;
        while (pos < end)
        {
            long index = pos / _blockSize;
            byte[] block = Block(index);
            int inBlock = (int)(pos - index * _blockSize);
            int take = (int)Math.Min(block.Length - inBlock, end - pos);
            if (take <= 0) break;
            Buffer.BlockCopy(block, inBlock, result, (int)(pos - offset), take);
            pos += take;
        }
        return pos == end ? result : result[..(int)(pos - offset)];
    }

    private byte[] Block(long index)
    {
        if (_blocks.TryGetValue(index, out byte[]? cached)) return cached;
        long start = index * _blockSize;
        var block = new byte[(int)Math.Min(_blockSize, Length - start)];
        _stream.Position = start;
        _stream.ReadExactly(block);
        BlockReads++;
        _blocks[index] = block;
        return block;
    }
}
