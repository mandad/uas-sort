// tests/UasSort.Core.Tests/Cleanup/Photos/CorruptPhotoReader.cs
namespace UasSort.Core.Tests.Cleanup.Photos;

/// <summary>An IPhotoFileReader whose named files open fine but whose bytes make the EXIF parser throw (the way a corrupt DNG or JPG
/// surfaces from MetadataExtractor); every other file goes to the inner reader.</summary>
internal sealed class CorruptPhotoReader(IPhotoFileReader inner) : IPhotoFileReader
{
    public Dictionary<string, Exception> Corrupt { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Stream OpenRead(string fullPath)
    {
        var s = inner.OpenRead(fullPath);
        if (!Corrupt.TryGetValue(PathRules.Normalize(fullPath), out var e)) return s;
        s.Dispose();
        return new ThrowingStream(e);
    }

    /// <summary>The exception types a parser can throw on untrusted bytes (review P.7 Important 1, Task PCfix).</summary>
#pragma warning disable CA2201 // a parser can throw runtime-reserved exceptions on corrupt bytes; this fake reproduces them
    public static Exception Make(string type) => type switch
    {
        nameof(ArgumentException) => new ArgumentException("corrupt IFD"),
        nameof(ArgumentOutOfRangeException) => new ArgumentOutOfRangeException(nameof(type), "corrupt IFD"),
        nameof(IndexOutOfRangeException) => new IndexOutOfRangeException("corrupt IFD"),
        nameof(OverflowException) => new OverflowException("corrupt IFD"),
        nameof(NotSupportedException) => new NotSupportedException("corrupt IFD"),
        nameof(InvalidOperationException) => new InvalidOperationException("corrupt IFD"),
        nameof(EndOfStreamException) => new EndOfStreamException("corrupt IFD"),
        nameof(NullReferenceException) => new NullReferenceException("corrupt IFD"),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
#pragma warning restore CA2201

    private sealed class ThrowingStream(Exception e) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => 4096;
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count) => throw e;
        public override int Read(Span<byte> buffer) => throw e;
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            _ => Length + offset,
        };
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
