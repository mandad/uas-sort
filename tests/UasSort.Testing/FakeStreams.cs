namespace UasSort.Testing;

/// <summary>Read-only deterministic content of a given length (no allocation).</summary>
internal sealed class PatternStream(long length, uint seed) : Stream
{
    private long _position;

    public static byte ByteAt(uint seed, long i) => unchecked((byte)((((ulong)i + seed) * 0x9E3779B97F4A7C15UL) >> 56));

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => _position; set => _position = value; }
    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var n = (int)Math.Max(0, Math.Min(count, length - _position));
        for (var i = 0; i < n; i++) buffer[offset + i] = ByteAt(seed, _position + i);
        _position += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };
        return _position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Write-only stream that appends to a fake node under the file system's lock.</summary>
internal sealed class FakeAppendStream(FakeFileSystem fs, FakeNode node, bool fails) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => node.Size;
    public override long Position { get => node.Size; set => throw new NotSupportedException(); }

    public override void Flush()
    {
        if (fails) throw new IOException("The device is not ready.");
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (fails) throw new IOException("The device is not ready.");
        fs.AppendBytes(node, buffer.AsSpan(offset, count));
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>Card read stream with the FakeFaults card hooks.</summary>
internal sealed class FaultyCardStream(Stream inner, FakeFaults faults, string cardRelPath) : Stream
{
    private bool _transientFired;

    public override bool CanRead => true;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (faults.CardRemoved) throw FakeCardReader.NotReady();
        if (faults.PersistentCardReadError.Contains(cardRelPath))
            throw new IOException("Data error (cyclic redundancy check).", unchecked((int)0x80070017));
        if (!_transientFired && faults.TransientCardReadError.Contains(cardRelPath))
        {
            _transientFired = true;
            throw new IOException("The request could not be performed because of an I/O device error.", unchecked((int)0x8007045D));
        }
        if (faults.CardVanishesAfterBytes.TryGetValue(cardRelPath, out var limit))
        {
            if (inner.Position >= limit)
            {
                faults.CardRemoved = true;
                throw FakeCardReader.NotReady();
            }
            count = (int)Math.Min(count, limit - inner.Position);
        }
        return inner.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
