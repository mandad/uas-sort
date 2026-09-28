namespace UasSort.Testing;

/// <summary>Read-only stream wrapper that records every Read call as (position, requested count).</summary>
public sealed class CountingStream(Stream inner) : Stream
{
    private readonly List<(long Offset, int Count)> _reads = [];

    public IReadOnlyList<(long Offset, int Count)> Reads => _reads;
    public int ReadCalls => _reads.Count;

    /// <summary>Reads whose start offset lies in [start, end).</summary>
    public int ReadsWithin(long start, long end) => _reads.Count(r => r.Offset >= start && r.Offset < end);

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        _reads.Add((inner.Position, count));
        return inner.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        _reads.Add((inner.Position, buffer.Length));
        return inner.Read(buffer);
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
