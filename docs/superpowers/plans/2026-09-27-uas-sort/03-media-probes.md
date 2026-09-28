# Part 03 — Media probes

**Goal.** Read everything the planner needs from card media without ever reading `mdat` or a whole `moov`: the MP4 `mvhd` time and clip length, the first djmd GPS fix (per-model table, generic search, multi-sample search within a 16-read budget, the truncated-clip `mdat` fallback), session and serial, the `tnal` thumbnail range; the DNG/JPG DTO from the EXIF directory that actually has it, offset, GPS, model and IFD0 thumbnail range; then harvest a whole card sequentially and serve thumbnails as bytes. The proven Python spikes are ported one to one: `docs/research/spikes/djmd/djmd_gps.py` (reader), `docs/research/spikes/djmd/synth_test.py` (synthetic MP4s, here `SyntheticMp4Builder`), `docs/research/spikes/review-ux/mp4thumb.py` (thumbnail ranges); calibration facts from `docs/research/02-djmd-gps-calibration.md`.

**Ref sections implemented:** §6.3 (MP4 steps 1–9 and Stills), the §4.2 rows `Mp4Probe`, `StillProbe`, `MetadataHarvester`, `ThumbnailReader`, the §3 probe types as consumed, the §13 "Synthetic MP4s", "Synthetic DNG" and "Real-file checks" groups, and Ref §14 step 3.

**Depends on:** Part 01 (solution, `UasSort.Core` with MetadataExtractor 2.9.3, `UasSort.Testing`, `UasSort.Core.Tests` with the csproj `<Using Include="Xunit" />`), Part 02 (every shared type in namespace `UasSort.Core`: `ItemId`, `GeoPoint`, `ByteRange`, `ItemKind`, `SetKind`, `EntryClass`, `CardEntry`, `CardIdentity`, `CardSource`, `CardInventory`, `MediaUnit`/`VideoUnit`/`PhotoUnit`/`SetUnit`, `GpsSource`, `GpsFix`, `NoFixReason`, `NoFix`, the `GpsProbe` union, `Mp4Info`, `StillInfo`, `RawItem`, `ScanPhase`, `ScanProgress`, `FsEntry`, `ListingResult`, `CardSpace`, the ports `ICardReader` and `IThumbnailSource`, `UnsafeIoException`; Part 02 Task 02.1 also created `src/UasSort.Core/Namespaces.cs`, which already anchors namespace `UasSort.Core.Media`, the fixed `GlobalUsings.Core.cs` in every project that references Core (global `System.Collections.Immutable`, `UasSort.Core`, `UasSort.Core.Media` and the other Core folder namespaces), `tests/UasSort.Testing/GlobalUsings.cs` and `tests/UasSort.Core.Tests/GlobalUsings.cs` (global `Microsoft.Extensions.Time.Testing`, `UasSort.Testing`); see `00-interfaces.md` "Namespaces and GlobalUsings").

**Conventions used here.**
- Units of this part live in namespace `UasSort.Core.Media` (folder `src/UasSort.Core/Media/`); the test helpers of this part live in flat namespace `UasSort.Testing` (folder `tests/UasSort.Testing/`); the tests live in `UasSort.Core.Tests.Media`. This part adopts the fixed `GlobalUsings.Core.cs` (registry `00-interfaces.md`): `System.Collections.Immutable`, `UasSort.Core` and `UasSort.Core.Media` are global in `UasSort.Core`, `UasSort.Testing` and `UasSort.Core.Tests`, and `UasSort.Testing` is global in `UasSort.Core.Tests`, so no file of this part needs a `using` for them. The explicit `using UasSort.Core.Media;` / `using UasSort.Testing;` lines kept in the test files are redundant with those global usings and harmless. This part creates no `GlobalUsings.cs` and no namespace other than these.
- Card-relative paths use `/` (Part 02 convention). `FsEntry.RelPath` is native `\`.
- Probes take a `Stream` (from `ICardReader.OpenRandom`) and wrap it in a 4 KB `BlockCache`; nothing here opens a path. MetadataExtractor is called through its **Stream** overloads only (the path overloads are banned, Ref §2.4).
- `GpsProbe` is a C# 15 union: tests unwrap it with `ProbeAssert.Fix`/`ProbeAssert.NoFix` (Task 03.9); production code assigns `GpsFix` or `NoFix` to it by implicit conversion and never boxes it.
- Commands run in `C:\dev\uas-sort` on Windows; from WSL run the same command through `tools/r.sh` (`tools/r.sh dotnet test …`). Every task ends with the whole suite green and one commit.
- While this part was written, the code of every task was compiled and run stage by stage in task order (143 tests, 2 golden skips) with the .NET 10 SDK against stand-ins of the Part 02 types (C# 14, so the `GpsProbe` union and the `closed` `MediaUnit` were abstract records there), with the analyzers at `latest-recommended` and warnings as errors. Under C# 15 the switches over `MediaUnit` need no default arm.

**`NoFixReason` for MP4s (not fixed by the Ref; defined here):** `NoDjmdTrack` = a `moov` without a djmd track (e.g. an Autel clip); `NotDji` = no usable `moov` and no `.proto` protocol at the `mdat` head; `Unparseable` = the djmd track exists but its tables are malformed or no probed sample decodes; `AllProbedSamplesZero` = samples decode but every probed fix is 0/0. `NoGpsTag` is the still-photo case; `GenericHitImplausible` is set later by `GpsPlausibility` (Part 04).

### Task 03.1: Block cache and counting stream

Every probe read goes through a 4 KB aligned read-through cache (Ref §6.3 step 9); `CountingStream` records the underlying reads so later tasks can assert the read budget.

**Files:**
- Create: `src/UasSort.Core/Media/BlockCache.cs`
- Create: `tests/UasSort.Testing/CountingStream.cs`
- Test: `tests/UasSort.Core.Tests/Media/BlockCacheTests.cs`

**Interfaces:**

Consumes:

```csharp
// System only (Stream).
```

Produces:

```csharp
namespace UasSort.Core.Media;
public sealed class BlockCache                                   // (defined here)
{
    public const int DefaultBlockSize = 4096;
    public BlockCache(Stream stream, int blockSize = DefaultBlockSize);   // readable + seekable, blockSize >= 512
    public long Length { get; }
    public int BlockReads { get; }
    public byte[] ReadAt(long offset, int count);                 // clamped at end of stream
}
namespace UasSort.Testing;
public sealed class CountingStream(Stream inner) : Stream        // (defined here) read-only wrapper
{
    public IReadOnlyList<(long Offset, int Count)> Reads { get; }
    public int ReadCalls { get; }
    public int ReadsWithin(long start, long end);                 // reads whose start offset is in [start, end)
}
```

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/Media/BlockCacheTests.cs`:

```csharp
using UasSort.Core.Media;
using UasSort.Testing;

namespace UasSort.Core.Tests.Media;

public sealed class BlockCacheTests
{
    private static byte[] Pattern(int n) => [.. Enumerable.Range(0, n).Select(i => (byte)(i % 251))];

    [Fact]
    public void ReadAt_ReturnsExactBytesAcrossABlockBoundary()
    {
        byte[] data = Pattern(10_000);
        var cache = new BlockCache(new MemoryStream(data));

        byte[] got = cache.ReadAt(4090, 20);

        Assert.Equal(data[4090..4110], got);
        Assert.Equal(2, cache.BlockReads);
    }

    [Fact]
    public void ReadAt_ReadsEachAlignedBlockOnce()
    {
        var counting = new CountingStream(new MemoryStream(Pattern(10_000)));
        var cache = new BlockCache(counting);

        cache.ReadAt(100, 8);
        cache.ReadAt(200, 16);
        cache.ReadAt(4090, 20);
        cache.ReadAt(5000, 4);

        (long, int)[] expected = [(0, 4096), (4096, 4096)];
        Assert.Equal(expected, counting.Reads);
    }

    [Fact]
    public void ReadAt_ClampsAtEndOfStream()
    {
        byte[] data = Pattern(5_000);
        var cache = new BlockCache(new MemoryStream(data));

        Assert.Equal(data[4990..], cache.ReadAt(4990, 100));
        Assert.Empty(cache.ReadAt(6000, 4));
        Assert.Equal(5_000, cache.Length);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*BlockCacheTests*"
```

Expected: the build fails with CS0246 (`The type or namespace name 'BlockCache' could not be found`) and CS0246 for `CountingStream` (namespace `UasSort.Core.Media` itself already exists through Part 02's `Namespaces.cs`).

- [ ] **Step 3: Implement**

`src/UasSort.Core/Media/BlockCache.cs`:

```csharp
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
```

`tests/UasSort.Testing/CountingStream.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*BlockCacheTests*"
dotnet test --solution uas-sort.slnx
```

Expected: 3 tests pass; the full suite passes with no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Media/BlockCacheTests.cs src/UasSort.Core/Media/BlockCache.cs tests/UasSort.Testing/CountingStream.cs
git commit -F- <<'EOF'
feat: add 4 KB block cache and counting stream for media probes

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

### Task 03.2: MP4 box walker

Lazy ISO-BMFF box walk (Ref §6.3 step 1): 8-byte headers, size 1 = 64-bit size, size 0 = to the end, `uuid` + 16 header bytes; a size smaller than its header ends the walk; a box claiming more than remains is returned once as `Truncated` and ends the walk (the Ref says "reject"; the flag lets the caller treat a truncated `moov` as missing while still using a truncated `mdat` head for the fallback).

**Files:**
- Create: `src/UasSort.Core/Media/Mp4Boxes.cs`
- Test: `tests/UasSort.Core.Tests/Media/Mp4BoxesTests.cs`

**Interfaces:**

Consumes:

```csharp
BlockCache.ReadAt(long offset, int count); BlockCache.Length;   // Task 03.1
```

Produces:

```csharp
namespace UasSort.Core.Media;
public readonly record struct Mp4Box(string Type, long Offset, long Size, int HeaderSize, bool Truncated)   // (defined here)
{ public long Body { get; } public long End { get; } }
public static class Mp4Boxes                                     // (defined here)
{
    public static IEnumerable<Mp4Box> Walk(BlockCache cache, long start, long end);
    public static Mp4Box? Child(BlockCache cache, Mp4Box parent, string type);
}
```

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/Media/Mp4BoxesTests.cs`:

```csharp
using System.Buffers.Binary;
using System.Text;
using UasSort.Core.Media;

namespace UasSort.Core.Tests.Media;

public sealed class Mp4BoxesTests
{
    private static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); return b; }
    private static byte[] U64(ulong v) { var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, v); return b; }
    private static byte[] Box(string type, int payload) => [.. U32((uint)(8 + payload)), .. Encoding.ASCII.GetBytes(type), .. new byte[payload]];
    private static BlockCache Cache(byte[] bytes) => new(new MemoryStream(bytes));

    [Fact]
    public void Walk_ReturnsTopLevelBoxesInOrder()
    {
        byte[] file = [.. Box("ftyp", 12), .. Box("free", 0), .. Box("mdat", 100)];

        List<Mp4Box> boxes = [.. Mp4Boxes.Walk(Cache(file), 0, file.Length)];

        Assert.Equal(["ftyp", "free", "mdat"], boxes.Select(b => b.Type));
        Assert.Equal([0L, 20L, 28L], boxes.Select(b => b.Offset));
        Assert.Equal(108, boxes[2].Size);
        Assert.All(boxes, b => Assert.False(b.Truncated));
    }

    [Fact]
    public void Walk_Size1_ReadsA64BitSize()
    {
        byte[] file = [.. Box("ftyp", 12), .. U32(1), .. "mdat"u8.ToArray(), .. U64(16 + 50), .. new byte[50]];

        Mp4Box mdat = Mp4Boxes.Walk(Cache(file), 0, file.Length).Last();

        Assert.Equal("mdat", mdat.Type);
        Assert.Equal(16, mdat.HeaderSize);
        Assert.Equal(66, mdat.Size);
        Assert.Equal(20 + 16, mdat.Body);
    }

    [Fact]
    public void Walk_Size0_RunsToTheEnd()
    {
        byte[] file = [.. Box("ftyp", 12), .. U32(0), .. "moov"u8.ToArray(), .. new byte[40]];

        Mp4Box moov = Mp4Boxes.Walk(Cache(file), 0, file.Length).Last();

        Assert.Equal(48, moov.Size);
        Assert.False(moov.Truncated);
    }

    [Fact]
    public void Walk_Uuid_HasSixteenMoreHeaderBytes()
    {
        byte[] file = [.. U32(8 + 16 + 4), .. "uuid"u8.ToArray(), .. new byte[16], .. new byte[4], .. Box("free", 0)];

        List<Mp4Box> boxes = [.. Mp4Boxes.Walk(Cache(file), 0, file.Length)];

        Assert.Equal(24, boxes[0].HeaderSize);
        Assert.Equal(24, boxes[0].Body);
        Assert.Equal("free", boxes[1].Type);
    }

    [Fact]
    public void Walk_SizeSmallerThanHeader_StopsTheWalk()
    {
        byte[] file = [.. Box("ftyp", 12), .. U32(3), .. "free"u8.ToArray(), .. Box("mdat", 10)];

        Assert.Equal(["ftyp"], Mp4Boxes.Walk(Cache(file), 0, file.Length).Select(b => b.Type));
    }

    [Fact]
    public void Walk_SizeLargerThanRemaining_ReturnsTruncatedBoxAndStops()
    {
        byte[] file = [.. Box("ftyp", 12), .. U32(1000), .. "moov"u8.ToArray(), .. new byte[50]];

        Mp4Box moov = Mp4Boxes.Walk(Cache(file), 0, file.Length).Last();

        Assert.Equal("moov", moov.Type);
        Assert.True(moov.Truncated);
        Assert.Equal(58, moov.Size);
    }

    [Fact]
    public void Child_FindsTheFirstDirectChild()
    {
        byte[] moov = [.. U32(8 + 16 + 8), .. "moov"u8.ToArray(), .. Box("mvhd", 8), .. Box("trak", 0)];
        BlockCache cache = Cache(moov);
        Mp4Box root = Mp4Boxes.Walk(cache, 0, moov.Length).Single();

        Mp4Box? trak = Mp4Boxes.Child(cache, root, "trak");

        Assert.Equal(24, trak?.Offset);
        Assert.Null(Mp4Boxes.Child(cache, root, "udta"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*Mp4BoxesTests*"
```

Expected: the build fails with CS0103 (`The name 'Mp4Boxes' does not exist in the current context`) and CS0246 for `Mp4Box`.

- [ ] **Step 3: Implement**

`src/UasSort.Core/Media/Mp4Boxes.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*Mp4BoxesTests*"
dotnet test --solution uas-sort.slnx
```

Expected: 7 tests pass; the full suite passes with no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Media/Mp4BoxesTests.cs src/UasSort.Core/Media/Mp4Boxes.cs
git commit -F- <<'EOF'
feat: add lazy MP4 box walker

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

### Task 03.3: Generic protobuf decoder

Schema-less decoding of djmd samples (Ref §6.3 step 3): varints as `ulong`, fixed64, length-delimited, fixed32; a length-delimited value becomes a nested message only when it is not printable text and parses; first-occurrence path lookup; the `.proto` protocol search.

**Files:**
- Create: `src/UasSort.Core/Media/Protobuf.cs`
- Test: `tests/UasSort.Core.Tests/Media/ProtobufTests.cs`

**Interfaces:**

Consumes:

```csharp
// System only.
```

Produces:

```csharp
namespace UasSort.Core.Media;
public sealed record PbField(int Number, int WireType, ulong Value, ReadOnlyMemory<byte> Raw, IReadOnlyList<PbField>? Message);   // (defined here)
public static class Protobuf                                     // (defined here)
{
    public const int MaxDepth = 12;
    public static bool TryReadVarint(ReadOnlySpan<byte> buffer, ref int position, out ulong value);
    public static IReadOnlyList<PbField>? Parse(ReadOnlyMemory<byte> buffer);
    public static IReadOnlyList<PbField>? DecodeTree(ReadOnlyMemory<byte> buffer);
    public static PbField? GetPath(IReadOnlyList<PbField>? tree, ReadOnlySpan<int> path);
    public static string? FindProtocol(IReadOnlyList<PbField>? tree);
    public static bool LooksLikeText(ReadOnlySpan<byte> bytes);
}
```

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/Media/ProtobufTests.cs`:

```csharp
using System.Buffers.Binary;
using UasSort.Core.Media;

namespace UasSort.Core.Tests.Media;

public sealed class ProtobufTests
{
    [Fact]
    public void TryReadVarint_DecodesMultiByteValuesAsUlong()
    {
        int p = 0;
        Assert.True(Protobuf.TryReadVarint([0xAC, 0x02], ref p, out ulong v300));
        Assert.Equal(300UL, v300);
        Assert.Equal(2, p);

        p = 0;
        Assert.True(Protobuf.TryReadVarint([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01], ref p, out ulong max));
        Assert.Equal(ulong.MaxValue, max);
    }

    [Fact]
    public void TryReadVarint_FailsOnTruncation()
    {
        int p = 0;
        Assert.False(Protobuf.TryReadVarint([0x80], ref p, out _));
    }

    [Fact]
    public void Parse_ReadsTheFourWireTypes()
    {
        var d = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(d, 1.5);
        byte[] msg = [0x08, 0x96, 0x01, 0x11, .. d, 0x1A, 0x02, (byte)'h', (byte)'i', 0x25, 1, 2, 3, 4];

        IReadOnlyList<PbField> fields = Protobuf.Parse(msg)!;

        Assert.Equal([1, 2, 3, 4], fields.Select(f => f.Number));
        Assert.Equal([0, 1, 2, 5], fields.Select(f => f.WireType));
        Assert.Equal(150UL, fields[0].Value);
        Assert.Equal(1.5, BinaryPrimitives.ReadDoubleLittleEndian(fields[1].Raw.Span));
        Assert.Equal("hi"u8.ToArray(), fields[2].Raw.ToArray());
        Assert.Equal(4, fields[3].Raw.Length);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x01 })]
    [InlineData(new byte[] { 0x0B })]
    [InlineData(new byte[] { 0x0A, 0x05, 0x01 })]
    [InlineData(new byte[] { 0x11, 0x01, 0x02 })]
    public void Parse_ReturnsNullForInvalidMessages(byte[] bytes) => Assert.Null(Protobuf.Parse(bytes));

    [Fact]
    public void DecodeTree_NestsBinaryValuesButKeepsTextAsBytes()
    {
        byte[] protocol = "dvtm_Air3s.proto"u8.ToArray();
        byte[] msg = [0x0A, 0x02, 0x08, 0x07, 0x12, (byte)protocol.Length, .. protocol];

        IReadOnlyList<PbField> tree = Protobuf.DecodeTree(msg)!;

        Assert.Equal(7UL, Assert.Single(tree[0].Message!).Value);
        Assert.Null(tree[1].Message);
        Assert.Equal(protocol, tree[1].Raw.ToArray());
    }

    [Fact]
    public void GetPath_TakesTheFirstOccurrenceAtEachLevel()
    {
        byte[] msg = [0x0A, 0x02, 0x10, 0x05, 0x0A, 0x02, 0x10, 0x09];

        IReadOnlyList<PbField> tree = Protobuf.DecodeTree(msg)!;

        Assert.Equal(5UL, Protobuf.GetPath(tree, [1, 2])!.Value);
        Assert.Null(Protobuf.GetPath(tree, [1, 3]));
        Assert.Null(Protobuf.GetPath(tree, [2]));
    }

    [Fact]
    public void FindProtocol_SearchesDepthFirstForADotProtoString()
    {
        byte[] protocol = "dvtm_Air3s.proto"u8.ToArray();
        byte[] inner = [0x0A, (byte)protocol.Length, .. protocol];
        byte[] msg = [0x0A, (byte)inner.Length, .. inner, 0x18, 0x01];

        Assert.Equal("dvtm_Air3s.proto", Protobuf.FindProtocol(Protobuf.DecodeTree(msg)));
        byte[] noProtocol = [0x18, 0x01];
        Assert.Null(Protobuf.FindProtocol(Protobuf.DecodeTree(noProtocol)));
    }

    [Fact]
    public void LooksLikeText_AcceptsPrintableAsciiAndWhitespaceOnly()
    {
        Assert.True(Protobuf.LooksLikeText("DJI Air3s\r\n\t"u8));
        Assert.False(Protobuf.LooksLikeText([0x0A, 0x10]));
        Assert.False(Protobuf.LooksLikeText([]));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*ProtobufTests*"
```

Expected: the build fails with CS0103 (`The name 'Protobuf' does not exist in the current context`) and CS0246 for `PbField`.

- [ ] **Step 3: Implement**

`src/UasSort.Core/Media/Protobuf.cs`:

```csharp
using System.Text;

namespace UasSort.Core.Media;

/// <summary>
/// One decoded protobuf field. <see cref="Value"/> holds a varint (wire type 0); <see cref="Raw"/> holds the bytes of
/// wire types 1, 2 and 5; <see cref="Message"/> is set when a length-delimited value decoded as a nested message.
/// </summary>
public sealed record PbField(int Number, int WireType, ulong Value, ReadOnlyMemory<byte> Raw, IReadOnlyList<PbField>? Message);

/// <summary>Schema-less protobuf decoding for djmd samples (Ref §6.3 step 3).</summary>
public static class Protobuf
{
    public const int MaxDepth = 12;

    /// <summary>Reads a base-128 varint as <see cref="ulong"/>; false on truncation or more than 64 bits.</summary>
    public static bool TryReadVarint(ReadOnlySpan<byte> buffer, ref int position, out ulong value)
    {
        value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (position >= buffer.Length) return false;
            byte b = buffer[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
        }
        return false;
    }

    /// <summary>Parses one message level; null if the bytes are not a valid message (wire types 0, 1, 2, 5 only).</summary>
    public static IReadOnlyList<PbField>? Parse(ReadOnlyMemory<byte> buffer)
    {
        ReadOnlySpan<byte> span = buffer.Span;
        var fields = new List<PbField>();
        int p = 0;
        while (p < span.Length)
        {
            if (!TryReadVarint(span, ref p, out ulong key)) return null;
            ulong number = key >> 3;
            int wire = (int)(key & 7);
            if (number == 0 || number > int.MaxValue) return null;
            switch (wire)
            {
                case 0:
                    if (!TryReadVarint(span, ref p, out ulong v)) return null;
                    fields.Add(new PbField((int)number, 0, v, ReadOnlyMemory<byte>.Empty, null));
                    break;
                case 1:
                    if (span.Length - p < 8) return null;
                    fields.Add(new PbField((int)number, 1, 0, buffer.Slice(p, 8), null));
                    p += 8;
                    break;
                case 2:
                    if (!TryReadVarint(span, ref p, out ulong length) || length > (ulong)(span.Length - p)) return null;
                    fields.Add(new PbField((int)number, 2, 0, buffer.Slice(p, (int)length), null));
                    p += (int)length;
                    break;
                case 5:
                    if (span.Length - p < 4) return null;
                    fields.Add(new PbField((int)number, 5, 0, buffer.Slice(p, 4), null));
                    p += 4;
                    break;
                default:
                    return null;
            }
        }
        return fields;
    }

    /// <summary>
    /// Decodes the whole tree: a length-delimited value becomes a nested message only if it is not printable text
    /// and parses as a message (depth limit <see cref="MaxDepth"/>).
    /// </summary>
    public static IReadOnlyList<PbField>? DecodeTree(ReadOnlyMemory<byte> buffer) => DecodeTree(buffer, 0);

    /// <summary>The field at a dotted path (first occurrence at each level), e.g. [3, 3, 4, 1]; null if absent.</summary>
    public static PbField? GetPath(IReadOnlyList<PbField>? tree, ReadOnlySpan<int> path)
    {
        IReadOnlyList<PbField>? node = tree;
        PbField? found = null;
        foreach (int number in path)
        {
            if (node is null) return null;
            found = null;
            foreach (PbField f in node)
            {
                if (f.Number == number)
                {
                    found = f;
                    break;
                }
            }
            if (found is null) return null;
            node = found.Message;
        }
        return found;
    }

    /// <summary>The first length-delimited text value ending in ".proto" (depth-first), e.g. "dvtm_Air3s.proto".</summary>
    public static string? FindProtocol(IReadOnlyList<PbField>? tree)
    {
        if (tree is null) return null;
        foreach (PbField f in tree)
        {
            if (f.WireType == 2 && f.Message is null && f.Raw.Span.EndsWith(".proto"u8))
                return Encoding.Latin1.GetString(f.Raw.Span);
            if (FindProtocol(f.Message) is { } nested) return nested;
        }
        return null;
    }

    public static bool LooksLikeText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return false;
        foreach (byte b in bytes)
            if (b is not ((>= 0x20 and < 0x7F) or 9 or 10 or 13)) return false;
        return true;
    }

    private static List<PbField>? DecodeTree(ReadOnlyMemory<byte> buffer, int depth)
    {
        IReadOnlyList<PbField>? flat = Parse(buffer);
        if (flat is null) return null;
        var result = new List<PbField>(flat.Count);
        foreach (PbField f in flat)
        {
            bool nested = f.WireType == 2 && depth < MaxDepth && !f.Raw.IsEmpty && !LooksLikeText(f.Raw.Span);
            result.Add(nested ? f with { Message = DecodeTree(f.Raw, depth + 1) } : f);
        }
        return result;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*ProtobufTests*"
dotnet test --solution uas-sort.slnx
```

Expected: 11 tests pass; the full suite passes with no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Media/ProtobufTests.cs src/UasSort.Core/Media/Protobuf.cs
git commit -F- <<'EOF'
feat: add schema-less protobuf decoder for djmd samples

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

### Task 03.4: SyntheticMp4Builder

The C# port of `synth_test.py`, without any user data: the spike copied real samples from a library clip; this builder writes synthetic djmd protobuf samples in the same shape (sample 0: 1-1-1 protocol, 1-1-5 serial, 1-1-9 uptime; every sample: 3-1-2 uptime and the GPSInfo message at the model path, altitude at its sibling 2). It covers `moov` first or last, 64-bit or 32-bit `mdat`, `stco`/`co64`, 1/3/5 samples per chunk, `djmd` as the second `stsd` entry, GPS zeroed before sample N, degrees/radians/units field, unknown protocols with any GPS path, no `moov` (payload start or offset 512, walk-stopping padding box), box sizes 0 and oversize, a top-level `uuid`, 4 KB sample alignment for read counting, `mvhd` version 0/1 with any timescale, a `tnal` thumbnail, and an Autel-style clip without a djmd track. Its fluent `With…` methods and `Build()` returning bytes are the contract Part 11 (when it extends Part 01's `tools/fixtures/make-selftest-assets.cs`; this part never creates that file) and Part 12 use. Note: the spike's `ct = 3875977587` is not 2026-09-27T18:06:27Z (that is 3873377187 s after 1904-01-01); the builder computes the value from `CreationUtc`.

**Files:**
- Create: `tests/UasSort.Testing/SyntheticMp4Builder.cs`
- Test: `tests/UasSort.Core.Tests/Media/SyntheticMp4BuilderTests.cs`

**Interfaces:**

Consumes:

```csharp
// Part 02 (namespace UasSort.Core)
public readonly record struct GeoPoint(double Lat, double Lon);
public readonly record struct ByteRange(long Offset, int Length);
// Tasks 03.2–03.3 (tests only): Mp4Boxes.Walk, Protobuf.DecodeTree/GetPath/FindProtocol
```

Produces:

```csharp
namespace UasSort.Testing;
public sealed record SyntheticMp4(byte[] Bytes, long MdatPayloadStart, long MdatPayloadEnd, long? MoovOffset,
                                  ImmutableArray<long> SampleOffsets, ByteRange? Thumb);          // (defined here)
public sealed record class SyntheticMp4Builder                   // (defined here)
{
    public static readonly DateTime ZacharCreationUtc;            // 2026-09-27T18:06:27Z
    public static readonly GeoPoint Zachar0128;                   // 57.5504420579265, -153.738972972093
    public static readonly ImmutableArray<byte> TinyJpeg;         // 22-byte SOI…EOI stand-in
    // init properties: SampleCount (40), SamplesPerChunk (1), MoovFirst, IncludeMoov (true), WithDjmdTrack (true),
    // DjmdSecondStsdEntry, Co64, LargeMdat (true), Protocol ("dvtm_Air3s.proto"), GpsPath ([3,3,4,1]), Gps (Zachar0128),
    // AltM (298.394), WriteDegrees, CoordinateUnits (int?), ZeroGpsBefore, Serial ("1581F6ZSYNTH0001"), UptimeUs (179_000_000),
    // CreationUtc (ZacharCreationUtc), MvhdVersion (0), Timescale (1000), Duration (222_000), Thumbnail (TinyJpeg),
    // SampleAlignment, MdatPayloadAt512, CorruptPaddingBox, UuidBox, LastBoxSizeZero, LastBoxOverclaim
    public SyntheticMp4Builder WithMvhdUtc(DateTime utc);
    public SyntheticMp4Builder WithDuration(TimeSpan duration);
    public SyntheticMp4Builder WithDjmdGps(string protocol, GeoPoint first);
    public SyntheticMp4Builder WithThumbnail(byte[] jpeg);
    public byte[] Build();
    public SyntheticMp4 BuildFile();
    public byte[] BuildSample(int k);
}
```

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/Media/SyntheticMp4BuilderTests.cs`:

```csharp
using UasSort.Core.Media;
using UasSort.Testing;

namespace UasSort.Core.Tests.Media;

public sealed class SyntheticMp4BuilderTests
{
    private static List<Mp4Box> TopLevel(SyntheticMp4 mp4)
    {
        var cache = new BlockCache(new MemoryStream(mp4.Bytes));
        return [.. Mp4Boxes.Walk(cache, 0, cache.Length)];
    }

    [Fact]
    public void Build_Default_IsFtypMdatMoovWithA64BitMdat()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder().BuildFile();
        List<Mp4Box> top = TopLevel(mp4);

        Assert.Equal(["ftyp", "mdat", "moov"], top.Select(b => b.Type));
        Assert.Equal(16, top[1].HeaderSize);
        Assert.Equal(top[1].Body, mp4.MdatPayloadStart);
        Assert.Equal(top[2].Offset, mp4.MoovOffset);
        Assert.Equal(40, mp4.SampleOffsets.Length);
    }

    [Fact]
    public void Build_MoovFirst_PutsMoovBeforeMdat()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { MoovFirst = true, LargeMdat = false }.BuildFile();

        List<Mp4Box> top = TopLevel(mp4);

        Assert.Equal(["ftyp", "moov", "mdat"], top.Select(b => b.Type));
        Assert.Equal(8, top[2].HeaderSize);
    }

    [Fact]
    public void Build_WritesEachSampleAtItsRecordedOffset()
    {
        var builder = new SyntheticMp4Builder { SamplesPerChunk = 3, Co64 = true, MoovFirst = true };
        SyntheticMp4 mp4 = builder.BuildFile();

        foreach (int k in new[] { 0, 1, 7, 39 })
        {
            byte[] sample = builder.BuildSample(k);
            Assert.Equal(sample, mp4.Bytes.AsSpan((int)mp4.SampleOffsets[k], sample.Length).ToArray());
        }
    }

    [Fact]
    public void BuildSample_Zero_CarriesProtocolSerialUptimeAndGpsInfo()
    {
        IReadOnlyList<PbField> tree = Protobuf.DecodeTree(new SyntheticMp4Builder().BuildSample(0))!;

        Assert.Equal("dvtm_Air3s.proto", Protobuf.FindProtocol(tree));
        Assert.Equal("1581F6ZSYNTH0001"u8.ToArray(), Protobuf.GetPath(tree, [1, 1, 5])!.Raw.ToArray());
        Assert.Equal(179_000_000UL, Protobuf.GetPath(tree, [3, 1, 2])!.Value);
        Assert.Equal([2, 3], Protobuf.GetPath(tree, [3, 3, 4, 1])!.Message!.Select(f => f.Number));
        Assert.Equal(298_394UL, Protobuf.GetPath(tree, [3, 3, 4, 2])!.Value);
    }

    [Fact]
    public void BuildSample_Later_HasNoProtocol()
    {
        IReadOnlyList<PbField> tree = Protobuf.DecodeTree(new SyntheticMp4Builder().BuildSample(5))!;

        Assert.Null(Protobuf.FindProtocol(tree));
        Assert.Equal(3, Assert.Single(tree).Number);
    }

    [Fact]
    public void Build_MdatPayloadAt512_StartsThePayloadAtOffset512()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { IncludeMoov = false, MdatPayloadAt512 = true }.BuildFile();

        Assert.Equal(512, mp4.MdatPayloadStart);
        Assert.Equal(512, mp4.SampleOffsets[0]);
        Assert.Null(mp4.MoovOffset);
    }

    [Fact]
    public void Build_SampleAlignment_AlignsChunksAndTheMdatEnd()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { SampleAlignment = 4096 }.BuildFile();

        Assert.All(mp4.SampleOffsets, o => Assert.Equal(0, o % 4096));
        Assert.Equal(0, mp4.MdatPayloadEnd % 4096);
    }

    [Fact]
    public void Build_ThumbRangeHoldsTheJpeg()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder().BuildFile();

        Assert.NotNull(mp4.Thumb);
        byte[] jpeg = mp4.Bytes.AsSpan((int)mp4.Thumb.Value.Offset, mp4.Thumb.Value.Length).ToArray();
        Assert.Equal(SyntheticMp4Builder.TinyJpeg, jpeg);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*SyntheticMp4BuilderTests*"
```

Expected: the build fails with CS0246 (`The type or namespace name 'SyntheticMp4' could not be found`).

- [ ] **Step 3: Implement**

`tests/UasSort.Testing/SyntheticMp4Builder.cs`:

```csharp
using System.Buffers.Binary;
using System.Text;

namespace UasSort.Testing;

/// <summary>A built synthetic MP4 plus the layout facts tests assert on.</summary>
public sealed record SyntheticMp4(byte[] Bytes, long MdatPayloadStart, long MdatPayloadEnd, long? MoovOffset,
                                  ImmutableArray<long> SampleOffsets, ByteRange? Thumb);

/// <summary>
/// Builds DJI-like MP4s with a djmd track (C# port of docs/research/spikes/djmd/synth_test.py; no user data).
/// Samples are synthetic protobuf: sample 0 carries field 1-1 (1 = protocol, 5 = serial, 9 = uptime µs,
/// 10 = model text); every sample carries field 3 (3-1 = frame number and uptime µs at 3-1-2, and the GPSInfo
/// message at <see cref="GpsPath"/> with the altitude in mm at its sibling field 2). Configure with init properties
/// or the fluent <c>With…</c> methods; <see cref="Build"/> returns the bytes, <see cref="BuildFile"/> the layout too.
/// </summary>
public sealed record class SyntheticMp4Builder
{
    public static readonly DateTime ZacharCreationUtc = new(2026, 9, 27, 18, 6, 27, DateTimeKind.Utc);
    public static readonly GeoPoint Zachar0128 = new(57.5504420579265, -153.738972972093);
    public static readonly ImmutableArray<byte> TinyJpeg =
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9];

    private static readonly DateTime QtEpoch = new(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public int SampleCount { get; init; } = 40;
    public int SamplesPerChunk { get; init; } = 1;
    public bool MoovFirst { get; init; }
    public bool IncludeMoov { get; init; } = true;
    public bool WithDjmdTrack { get; init; } = true;
    /// <summary>Two <c>stsd</c> entries ("mett", then "djmd"); every chunk points at entry 2.</summary>
    public bool DjmdSecondStsdEntry { get; init; }
    public bool Co64 { get; init; }
    /// <summary>64-bit <c>mdat</c> size (box size field 1).</summary>
    public bool LargeMdat { get; init; } = true;
    public string Protocol { get; init; } = "dvtm_Air3s.proto";
    public ImmutableArray<int> GpsPath { get; init; } = [3, 3, 4, 1];
    public GeoPoint Gps { get; init; } = Zachar0128;
    public double AltM { get; init; } = 298.394;
    /// <summary>True = lat/lon written in degrees; false = radians.</summary>
    public bool WriteDegrees { get; init; }
    /// <summary>GPSInfo field 1 (0 radians, 1 degrees); null = absent.</summary>
    public int? CoordinateUnits { get; init; }
    /// <summary>Samples [0, n) carry lat = lon = 0.0 (no fix).</summary>
    public int ZeroGpsBefore { get; init; }
    public string Serial { get; init; } = "1581F6ZSYNTH0001";
    public ulong UptimeUs { get; init; } = 179_000_000;
    public DateTime CreationUtc { get; init; } = ZacharCreationUtc;
    public int MvhdVersion { get; init; }
    public uint Timescale { get; init; } = 1000;
    public ulong Duration { get; init; } = 222_000;
    /// <summary>Stored in <c>udta/meta/ilst/tnal/data</c>; empty = no <c>udta</c>.</summary>
    public ImmutableArray<byte> Thumbnail { get; init; } = TinyJpeg;
    /// <summary>&gt; 0: every chunk starts on this absolute alignment and <c>mdat</c> ends on it.</summary>
    public int SampleAlignment { get; init; }
    /// <summary>Pads the top level with a <c>free</c> box so the <c>mdat</c> payload starts at file offset 512.</summary>
    public bool MdatPayloadAt512 { get; init; }
    /// <summary>Writes size 3 into that padding box's header, so a box walk stops before <c>mdat</c>.</summary>
    public bool CorruptPaddingBox { get; init; }
    /// <summary>A top-level <c>uuid</c> box right after <c>ftyp</c>.</summary>
    public bool UuidBox { get; init; }
    /// <summary>The last top-level box gets size 0 ("to end of file"); needs a 32-bit header (moov, or LargeMdat = false).</summary>
    public bool LastBoxSizeZero { get; init; }
    /// <summary>The last top-level box claims this many bytes more than the file holds.</summary>
    public int LastBoxOverclaim { get; init; }

    public SyntheticMp4Builder WithMvhdUtc(DateTime utc) => this with { CreationUtc = utc };

    public SyntheticMp4Builder WithDuration(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        return this with { Duration = (ulong)((UInt128)(ulong)duration.Ticks * Timescale / (ulong)TimeSpan.TicksPerSecond) };
    }

    /// <summary>Protocol and first fix; a protocol of the Ref §6.3 model table also sets its GPS path and units rule.</summary>
    public SyntheticMp4Builder WithDjmdGps(string protocol, GeoPoint first)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return TableRow(protocol) is var (path, alwaysDegrees)
            ? this with { Protocol = protocol, Gps = first, GpsPath = path, WriteDegrees = alwaysDegrees || WriteDegrees }
            : this with { Protocol = protocol, Gps = first };
    }

    public SyntheticMp4Builder WithThumbnail(byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        return this with { Thumbnail = [.. jpeg] };
    }

    /// <summary>The MP4 bytes.</summary>
    public byte[] Build() => BuildFile().Bytes;

    /// <summary>The MP4 bytes plus the layout facts tests assert on.</summary>
    public SyntheticMp4 BuildFile()
    {
        byte[][] samples = [.. Enumerable.Range(0, SampleCount).Select(BuildSample)];
        int chunkCount = SampleCount == 0 ? 0 : (SampleCount + SamplesPerChunk - 1) / SamplesPerChunk;
        int mdatHeader = LargeMdat ? 16 : 8;

        byte[] ftyp = Box("ftyp", Concat(Ascii("isom"), U32(0), Ascii("isom")));
        var top = new List<byte[]> { ftyp };
        if (UuidBox) top.Add(Box("uuid", Concat(new byte[16], new byte[8])));
        long preLength = top.Sum(b => b.Length);
        if (MdatPayloadAt512)
        {
            int pad = (int)(512 - preLength - mdatHeader);
            byte[] free = Box("free", new byte[pad - 8]);
            if (CorruptPaddingBox) BinaryPrimitives.WriteUInt32BigEndian(free, 3);
            top.Add(free);
            preLength = 512 - mdatHeader;
        }

        long moovLength = IncludeMoov ? BuildMoov(new long[chunkCount], samples).Length : 0;
        long mdatStart = IncludeMoov && MoovFirst ? preLength + moovLength : preLength;
        long payloadStart = mdatStart + mdatHeader;

        var payload = new MemoryStream();
        var chunkOffsets = new long[chunkCount];
        var sampleOffsets = ImmutableArray.CreateBuilder<long>(SampleCount);
        for (int c = 0; c < chunkCount; c++)
        {
            PadTo(payload, payloadStart, SampleAlignment);
            chunkOffsets[c] = payloadStart + payload.Length;
            for (int k = c * SamplesPerChunk; k < Math.Min(SampleCount, (c + 1) * SamplesPerChunk); k++)
            {
                sampleOffsets.Add(payloadStart + payload.Length);
                payload.Write(samples[k]);
            }
            payload.Write(Enumerable.Repeat((byte)0xAA, 100).ToArray());
        }
        if (SampleCount == 0) payload.Write(new byte[4096]);
        PadTo(payload, payloadStart, SampleAlignment);
        byte[] body = payload.ToArray();

        byte[] mdat = LargeMdat
            ? Concat(U32(1), Ascii("mdat"), U64((ulong)(16 + body.Length)), body)
            : Concat(U32((uint)(8 + body.Length)), Ascii("mdat"), body);
        byte[]? moov = IncludeMoov ? BuildMoov(chunkOffsets, samples) : null;

        var file = new List<byte[]>(top);
        long? moovOffset = null;
        if (moov is not null && MoovFirst)
        {
            moovOffset = preLength;
            file.Add(moov);
            file.Add(mdat);
        }
        else
        {
            file.Add(mdat);
            if (moov is not null)
            {
                moovOffset = preLength + mdat.Length;
                file.Add(moov);
            }
        }
        byte[] last = file[^1];
        if (LastBoxSizeZero)
        {
            if (last == mdat && LargeMdat) throw new InvalidOperationException("Size 0 needs a 32-bit header: set LargeMdat = false.");
            BinaryPrimitives.WriteUInt32BigEndian(last, 0);
        }
        if (LastBoxOverclaim > 0)
        {
            if (last == mdat && LargeMdat)
                BinaryPrimitives.WriteUInt64BigEndian(last.AsSpan(8), (ulong)(last.Length + LastBoxOverclaim));
            else
                BinaryPrimitives.WriteUInt32BigEndian(last, (uint)(last.Length + LastBoxOverclaim));
        }

        ByteRange? thumb = moov is not null && !Thumbnail.IsDefaultOrEmpty
            ? new ByteRange(moovOffset!.Value + moov.Length - Thumbnail.Length, Thumbnail.Length) : null;
        return new SyntheticMp4(Concat([.. file]), payloadStart, payloadStart + body.Length, moovOffset,
                                sampleOffsets.ToImmutable(), thumb);
    }

    /// <summary>The protobuf bytes of djmd sample <paramref name="k"/>.</summary>
    public byte[] BuildSample(int k)
    {
        bool zero = k < ZeroGpsBefore;
        double scale = WriteDegrees ? 1.0 : Math.PI / 180.0;
        double lat = zero ? 0.0 : Gps.Lat * scale;
        double lon = zero ? 0.0 : Gps.Lon * scale;
        byte[] gpsInfo = Concat(CoordinateUnits is int u ? PbVarint(1, (ulong)u) : [], PbDouble(2, lat), PbDouble(3, lon));
        byte[] inner = Concat(PbLen(GpsPath[^1], gpsInfo), PbVarint(2, unchecked((ulong)(long)Math.Round(AltM * 1000))));
        for (int i = GpsPath.Length - 2; i >= 1; i--) inner = PbLen(GpsPath[i], inner);
        byte[] timing = Concat(PbVarint(1, (ulong)k), PbVarint(2, UptimeUs + (ulong)k * 33_367));
        byte[] field3 = PbLen(GpsPath[0], Concat(PbLen(1, timing), inner));
        if (k != 0) return field3;
        byte[] header = PbLen(1, PbLen(1, Concat(PbText(1, Protocol), PbText(5, Serial), PbVarint(9, UptimeUs), PbText(10, "DJI Air3s"))));
        return Concat(header, field3);
    }

    private byte[] BuildMoov(long[] chunkOffsets, byte[][] samples)
    {
        ulong created = (ulong)((CreationUtc - QtEpoch).Ticks / TimeSpan.TicksPerSecond);
        byte[] mvhd = MvhdVersion == 1
            ? FullBox("mvhd", 1, Concat(U64(created), U64(created), U32(Timescale), U64(Duration), new byte[80]))
            : FullBox("mvhd", 0, Concat(U32((uint)created), U32((uint)created), U32(Timescale), U32((uint)Duration), new byte[80]));
        var children = new List<byte[]>
        {
            mvhd,
            Trak(WithDjmdTrack ? ["hvc1"] : ["avc1"], "vide", "HAL video", [], [], [], (uint)created),
        };
        if (WithDjmdTrack)
        {
            string[] formats = DjmdSecondStsdEntry ? ["mett", "djmd"] : ["djmd"];
            uint description = (uint)formats.Length;
            var stsc = new List<(uint, uint, uint)>();
            if (SampleCount > 0)
            {
                stsc.Add((1, (uint)SamplesPerChunk, description));
                int lastChunkSamples = SampleCount - (chunkOffsets.Length - 1) * SamplesPerChunk;
                if (lastChunkSamples != SamplesPerChunk) stsc.Add(((uint)chunkOffsets.Length, (uint)lastChunkSamples, description));
            }
            children.Add(Trak(formats, "meta", "HAL meta", stsc, [.. samples.Select(s => (uint)s.Length)], chunkOffsets, (uint)created));
        }
        if (!Thumbnail.IsDefaultOrEmpty)
        {
            byte[] hdlr = FullBox("hdlr", 0, Concat(U32(0), Ascii("mdir"), new byte[12], [0]));
            byte[] data = Box("data", Concat(U32(13), U32(0), [.. Thumbnail]));
            byte[] ilst = Box("ilst", Box("tnal", data));
            children.Add(Box("udta", FullBox("meta", 0, Concat(hdlr, ilst))));
        }
        return Box("moov", Concat([.. children]));
    }

    private byte[] Trak(string[] formats, string handler, string handlerName, List<(uint First, uint PerChunk, uint Desc)> stsc,
                        uint[] sizes, long[] chunkOffsets, uint created)
    {
        byte[] stsd = FullBox("stsd", 0, Concat([U32((uint)formats.Length), .. formats.Select(f => Box(f, new byte[8]))]));
        byte[] stts = FullBox("stts", 0, sizes.Length == 0 ? U32(0) : Concat(U32(1), U32((uint)sizes.Length), U32(1001)));
        byte[] stscBox = FullBox("stsc", 0, Concat([U32((uint)stsc.Count), .. stsc.Select(e => Concat(U32(e.First), U32(e.PerChunk), U32(e.Desc)))]));
        byte[] stsz = FullBox("stsz", 0, Concat([U32(0), U32((uint)sizes.Length), .. sizes.Select(U32)]));
        byte[] co = Co64
            ? FullBox("co64", 0, Concat([U32((uint)chunkOffsets.Length), .. chunkOffsets.Select(o => U64((ulong)o))]))
            : FullBox("stco", 0, Concat([U32((uint)chunkOffsets.Length), .. chunkOffsets.Select(o => U32((uint)o))]));
        byte[] stbl = Box("stbl", Concat(stsd, stts, stscBox, stsz, co));
        byte[] mdhd = FullBox("mdhd", 0, Concat(U32(created), U32(created), U32(30000), U32((uint)sizes.Length * 1001), U32(0)));
        byte[] hdlr = FullBox("hdlr", 0, Concat(U32(0), Ascii(handler), new byte[12], Ascii(handlerName + "\0")));
        byte[] tkhd = FullBox("tkhd", 0, new byte[80]);
        return Box("trak", Concat(tkhd, Box("mdia", Concat(mdhd, hdlr, Box("minf", stbl)))));
    }

    /// <summary>The builder's own copy of the Ref §6.3 table (an independent oracle for the probe); null = unknown protocol.</summary>
    private static (ImmutableArray<int> Path, bool AlwaysDegrees)? TableRow(string protocol) => protocol switch
    {
        "dvtm_Air3s.proto" or "dvtm_Air3.proto" or "dvtm_Mini4_Pro.proto" or "dvtm_wm265e.proto" or "dvtm_pm320.proto"
            or "dvtm_wm261.proto" or "dvtm_wa345e.proto" => (ImmutableArray.Create(3, 3, 4, 1), false),
        "dvtm_Mavic4.proto" or "dvtm_Mini5Pro.proto" => (ImmutableArray.Create(3, 3, 4, 1), true),
        "dvtm_AVATA2.proto" or "dvtm_dji_neo.proto" => (ImmutableArray.Create(3, 4, 4, 1), false),
        "dvtm_ac203.proto" or "dvtm_ac204.proto" or "dvtm_ac206.proto" or "dvtm_oq101.proto" => (ImmutableArray.Create(3, 4, 2, 1), false),
        _ => null,
    };

    private static void PadTo(MemoryStream payload, long payloadStart, int alignment)
    {
        if (alignment <= 0) return;
        long absolute = payloadStart + payload.Length;
        long pad = (alignment - absolute % alignment) % alignment;
        payload.Write(new byte[pad]);
    }

    private static byte[] Box(string type, byte[] payload) => Concat(U32((uint)(8 + payload.Length)), Ascii(type), payload);
    private static byte[] FullBox(string type, byte version, byte[] payload) => Box(type, Concat([version, 0, 0, 0], payload));
    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    private static byte[] U32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        return b;
    }

    private static byte[] U64(ulong v)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(b, v);
        return b;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        int at = 0;
        foreach (byte[] p in parts)
        {
            p.CopyTo(result, at);
            at += p.Length;
        }
        return result;
    }

    private static byte[] Varint(ulong v)
    {
        var bytes = new List<byte>();
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            bytes.Add(v != 0 ? (byte)(b | 0x80) : b);
        }
        while (v != 0);
        return [.. bytes];
    }

    private static byte[] Tag(int field, int wire) => Varint(((ulong)field << 3) | (uint)wire);
    private static byte[] PbVarint(int field, ulong v) => Concat(Tag(field, 0), Varint(v));
    private static byte[] PbLen(int field, byte[] content) => Concat(Tag(field, 2), Varint((ulong)content.Length), content);
    private static byte[] PbText(int field, string text) => PbLen(field, Encoding.UTF8.GetBytes(text));

    private static byte[] PbDouble(int field, double v)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(b, v);
        return Concat(Tag(field, 1), b);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*SyntheticMp4BuilderTests*"
dotnet test --solution uas-sort.slnx
```

Expected: 8 tests pass; the full suite passes with no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Media/SyntheticMp4BuilderTests.cs tests/UasSort.Testing/SyntheticMp4Builder.cs
git commit -F- <<'EOF'
test: add SyntheticMp4Builder (port of synth_test.py)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

### Task 03.5: djmd sample decoder and model table

One sample in, one reading out (Ref §6.3 steps 4 and 6): the protocol from the `.proto` string (sample 0) or the caller (later samples); the per-model GPS path with its units rule (field 1: 0 or absent = radians, 1 = degrees; Mavic4 and Mini5Pro always degrees) and altitude in mm at the sibling field 2 (a plain varint cast to `long`); for an unknown protocol the generic search for the first sub-message whose fields 2 and 3 are in-range doubles, in radians or degrees (without a units field, values within ±π/2, ±π are taken as radians, otherwise degrees), recorded with its field path; uptime from 3-1-2 else 1-1-9 and the serial from 1-1-5.

**Files:**
- Create: `src/UasSort.Core/Media/DjmdDecoder.cs`
- Test: `tests/UasSort.Core.Tests/Media/DjmdDecoderTests.cs`

**Interfaces:**

Consumes:

```csharp
// Part 02: GeoPoint.   Task 03.3: Protobuf, PbField.   Task 03.4 (tests): SyntheticMp4Builder.BuildSample
```

Produces:

```csharp
namespace UasSort.Core.Media;
public sealed record DjmdGpsPath(ImmutableArray<int> Path, bool AlwaysDegrees) { public string FieldPath { get; } }   // (defined here)
public sealed record DjmdReading(string? Protocol, GeoPoint? Point, double? AltM, string? FieldPath, bool Generic,
                                 ulong? UptimeUs, string? DroneSerial);                        // (defined here)
public static class DjmdDecoder                                  // (defined here)
{
    public static ImmutableDictionary<string, DjmdGpsPath> ModelTable { get; }   // 15 protocols, key without ".proto"
    public static bool IsFix(GeoPoint p);                         // |lat| or |lon| >= 1e-6
    public static DjmdReading? Decode(ReadOnlyMemory<byte> sample, string? knownProtocol);
    public static GeoPoint? DecodeAt(ReadOnlyMemory<byte> sample, string fieldPath);   // "3-3-4-1"
}
```

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/Media/DjmdDecoderTests.cs`:

```csharp
using System.Globalization;
using UasSort.Core.Media;
using UasSort.Testing;

namespace UasSort.Core.Tests.Media;

public sealed class DjmdDecoderTests
{
    private static readonly GeoPoint Zachar = SyntheticMp4Builder.Zachar0128;

    private static void Near(GeoPoint expected, GeoPoint? actual, double tolerance = 1e-9)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Lat, actual.Value.Lat, tolerance);
        Assert.Equal(expected.Lon, actual.Value.Lon, tolerance);
    }

    private static ImmutableArray<int> Path(string dashed)
        => [.. dashed.Split('-').Select(s => int.Parse(s, CultureInfo.InvariantCulture))];

    [Fact]
    public void Decode_Air3sSampleZero_UsesTheModelTableAndConvertsRadians()
    {
        DjmdReading r = DjmdDecoder.Decode(new SyntheticMp4Builder().BuildSample(0), null)!;

        Assert.Equal("dvtm_Air3s.proto", r.Protocol);
        Near(Zachar, r.Point);
        Assert.Equal(298.394, r.AltM!.Value, 1e-9);
        Assert.Equal("3-3-4-1", r.FieldPath);
        Assert.False(r.Generic);
        Assert.Equal(179_000_000UL, r.UptimeUs);
        Assert.Equal("1581F6ZSYNTH0001", r.DroneSerial);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Decode_CoordinateUnitsField_SelectsDegreesOrRadians(bool writeDegrees, int units)
    {
        var b = new SyntheticMp4Builder { WriteDegrees = writeDegrees, CoordinateUnits = units };

        Near(Zachar, DjmdDecoder.Decode(b.BuildSample(0), null)!.Point);
    }

    [Theory]
    [InlineData("dvtm_Mavic4.proto")]
    [InlineData("dvtm_Mini5Pro.proto")]
    public void Decode_Mavic4AndMini5Pro_AreAlwaysDegrees(string protocol)
    {
        var b = new SyntheticMp4Builder { Protocol = protocol, WriteDegrees = true };

        DjmdReading r = DjmdDecoder.Decode(b.BuildSample(0), null)!;

        Near(Zachar, r.Point);
        Assert.False(r.Generic);
    }

    [Theory]
    [InlineData("dvtm_Air3.proto", "3-3-4-1")]
    [InlineData("dvtm_Mini4_Pro.proto", "3-3-4-1")]
    [InlineData("dvtm_wm265e.proto", "3-3-4-1")]
    [InlineData("dvtm_pm320.proto", "3-3-4-1")]
    [InlineData("dvtm_wm261.proto", "3-3-4-1")]
    [InlineData("dvtm_wa345e.proto", "3-3-4-1")]
    [InlineData("dvtm_AVATA2.proto", "3-4-4-1")]
    [InlineData("dvtm_dji_neo.proto", "3-4-4-1")]
    [InlineData("dvtm_ac203.proto", "3-4-2-1")]
    [InlineData("dvtm_ac204.proto", "3-4-2-1")]
    [InlineData("dvtm_ac206.proto", "3-4-2-1")]
    [InlineData("dvtm_oq101.proto", "3-4-2-1")]
    public void Decode_EachModelReadsItsTablePath(string protocol, string path)
    {
        var b = new SyntheticMp4Builder { Protocol = protocol, GpsPath = Path(path) };

        DjmdReading r = DjmdDecoder.Decode(b.BuildSample(0), null)!;

        Near(Zachar, r.Point);
        Assert.Equal(path, r.FieldPath);
        Assert.False(r.Generic);
        Assert.Equal(298.394, r.AltM!.Value, 1e-9);
    }

    [Fact]
    public void ModelTable_HasTheFifteenProtocolsOfRef63()
    {
        Assert.Equal(15, DjmdDecoder.ModelTable.Count);
        Assert.True(DjmdDecoder.ModelTable["dvtm_Mavic4"].AlwaysDegrees);
        Assert.False(DjmdDecoder.ModelTable["dvtm_Air3s"].AlwaysDegrees);
    }

    [Fact]
    public void Decode_LaterSample_UsesTheKnownProtocol()
    {
        byte[] sample5 = new SyntheticMp4Builder().BuildSample(5);

        DjmdReading known = DjmdDecoder.Decode(sample5, "dvtm_Air3s.proto")!;
        DjmdReading unknown = DjmdDecoder.Decode(sample5, null)!;

        Assert.False(known.Generic);
        Assert.Equal("dvtm_Air3s.proto", known.Protocol);
        Assert.True(unknown.Generic);
        Near(Zachar, known.Point);
        Near(Zachar, unknown.Point);
    }

    [Fact]
    public void Decode_UnknownProtocol_GenericSearchRecordsTheFieldPath()
    {
        var b = new SyntheticMp4Builder { Protocol = "dvtm_Future9.proto", GpsPath = [3, 5, 7, 1] };

        DjmdReading r = DjmdDecoder.Decode(b.BuildSample(0), null)!;

        Assert.True(r.Generic);
        Assert.Equal("3-5-7-1", r.FieldPath);
        Near(Zachar, r.Point);
        Assert.Null(r.AltM);
    }

    [Fact]
    public void Decode_UnknownProtocolInDegreesWithoutUnits_IsRecognisedAsDegrees()
    {
        var b = new SyntheticMp4Builder { Protocol = "dvtm_Future9.proto", GpsPath = [3, 5, 7, 1], WriteDegrees = true };

        Near(Zachar, DjmdDecoder.Decode(b.BuildSample(0), null)!.Point);
    }

    [Fact]
    public void Decode_ZeroedGps_IsAPointButNotAFix()
    {
        DjmdReading r = DjmdDecoder.Decode(new SyntheticMp4Builder { ZeroGpsBefore = 1 }.BuildSample(0), null)!;

        Assert.Equal(new GeoPoint(0, 0), r.Point);
        Assert.False(DjmdDecoder.IsFix(r.Point!.Value));
    }

    [Fact]
    public void Decode_UnknownProtocolZeroed_HasNoGenericHit()
    {
        var b = new SyntheticMp4Builder { Protocol = "dvtm_Future9.proto", GpsPath = [3, 5, 7, 1], ZeroGpsBefore = 1 };

        DjmdReading r = DjmdDecoder.Decode(b.BuildSample(0), null)!;

        Assert.Null(r.Point);
        Assert.Null(r.FieldPath);
    }

    [Fact]
    public void Decode_NotProtobuf_ReturnsNull()
    {
        byte[] junk = [0x0B, 0x00];
        Assert.Null(DjmdDecoder.Decode(junk, null));
    }

    [Fact]
    public void DecodeAt_ReadsTheRecordedPath()
    {
        var b = new SyntheticMp4Builder { Protocol = "dvtm_Future9.proto", GpsPath = [3, 5, 7, 1] };

        Near(Zachar, DjmdDecoder.DecodeAt(b.BuildSample(39), "3-5-7-1"));
        Assert.Null(DjmdDecoder.DecodeAt(b.BuildSample(39), "3-3-4-1"));
    }

    [Fact]
    public void IsFix_NeedsLatOrLonAtLeast1e6()
    {
        Assert.False(DjmdDecoder.IsFix(new GeoPoint(5e-7, -5e-7)));
        Assert.True(DjmdDecoder.IsFix(new GeoPoint(0, 2e-6)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*DjmdDecoderTests*"
```

Expected: the build fails with CS0103 (`The name 'DjmdDecoder' does not exist in the current context`) and CS0246 for `DjmdReading`.

- [ ] **Step 3: Implement**

`src/UasSort.Core/Media/DjmdDecoder.cs`:

```csharp
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace UasSort.Core.Media;

/// <summary>A row of the per-model GPS table (Ref §6.3 step 4). <see cref="Path"/> is the GPSInfo message path.</summary>
public sealed record DjmdGpsPath(ImmutableArray<int> Path, bool AlwaysDegrees)
{
    public string FieldPath => string.Join('-', Path);
}

/// <summary>What one djmd sample says. <see cref="Point"/> may be (0, 0) = no fix; <see cref="Generic"/> = found by the generic search.</summary>
public sealed record DjmdReading(string? Protocol, GeoPoint? Point, double? AltM, string? FieldPath, bool Generic,
                                 ulong? UptimeUs, string? DroneSerial);

/// <summary>Decodes one djmd sample: model table first, generic search for unknown protocols (Ref §6.3 steps 3–4, 6).</summary>
public static class DjmdDecoder
{
    private const double RadToDeg = 180.0 / Math.PI;
    private static readonly ImmutableArray<int> P3341 = [3, 3, 4, 1];
    private static readonly ImmutableArray<int> P3441 = [3, 4, 4, 1];
    private static readonly ImmutableArray<int> P3421 = [3, 4, 2, 1];
    private static readonly ImmutableArray<int> UptimePrimary = [3, 1, 2];
    private static readonly ImmutableArray<int> UptimeSecondary = [1, 1, 9];
    private static readonly ImmutableArray<int> SerialPath = [1, 1, 5];

    /// <summary>Protocol name without ".proto" → GPS path and units rule.</summary>
    public static ImmutableDictionary<string, DjmdGpsPath> ModelTable { get; } = new Dictionary<string, DjmdGpsPath>
    {
        ["dvtm_Air3s"] = new(P3341, false),
        ["dvtm_Air3"] = new(P3341, false),
        ["dvtm_Mini4_Pro"] = new(P3341, false),
        ["dvtm_wm265e"] = new(P3341, false),
        ["dvtm_pm320"] = new(P3341, false),
        ["dvtm_wm261"] = new(P3341, false),
        ["dvtm_wa345e"] = new(P3341, false),
        ["dvtm_Mavic4"] = new(P3341, true),
        ["dvtm_Mini5Pro"] = new(P3341, true),
        ["dvtm_AVATA2"] = new(P3441, false),
        ["dvtm_dji_neo"] = new(P3441, false),
        ["dvtm_ac203"] = new(P3421, false),
        ["dvtm_ac204"] = new(P3421, false),
        ["dvtm_ac206"] = new(P3421, false),
        ["dvtm_oq101"] = new(P3421, false),
    }.ToImmutableDictionary(StringComparer.Ordinal);

    private enum Units { FromField, Degrees, Guess }

    /// <summary>|lat| and |lon| both below 1e-6 = no fix (Ref §6.3 step 5).</summary>
    public static bool IsFix(GeoPoint p) => Math.Abs(p.Lat) >= 1e-6 || Math.Abs(p.Lon) >= 1e-6;

    /// <summary>
    /// Decodes a sample. <paramref name="knownProtocol"/> is sample 0's protocol, used for later samples that carry none.
    /// Null when the bytes are not a protobuf message.
    /// </summary>
    public static DjmdReading? Decode(ReadOnlyMemory<byte> sample, string? knownProtocol)
    {
        IReadOnlyList<PbField>? tree = Protobuf.DecodeTree(sample);
        if (tree is null) return null;
        string? protocol = Protobuf.FindProtocol(tree) ?? knownProtocol;
        ulong? uptime = VarintAt(tree, UptimePrimary) ?? VarintAt(tree, UptimeSecondary);
        string? serial = Protobuf.GetPath(tree, SerialPath.AsSpan()) is { WireType: 2, Message: null } s
            ? Encoding.UTF8.GetString(s.Raw.Span) : null;

        if (protocol is not null && ModelTable.TryGetValue(StripSuffix(protocol), out DjmdGpsPath? model))
        {
            GeoPoint? point = GpsInfo(Protobuf.GetPath(tree, model.Path.AsSpan())?.Message,
                                      model.AlwaysDegrees ? Units.Degrees : Units.FromField);
            double? alt = VarintAt(tree, model.Path.SetItem(model.Path.Length - 1, 2)) is { } mm
                ? unchecked((long)mm) / 1000.0 : null;
            return new DjmdReading(protocol, point, alt, model.FieldPath, Generic: false, uptime, serial);
        }

        var path = new List<int>();
        return TryGeneric(tree, path, out GeoPoint hit)
            ? new DjmdReading(protocol, hit, null, string.Join('-', path), Generic: true, uptime, serial)
            : new DjmdReading(protocol, null, null, null, Generic: true, uptime, serial);
    }

    /// <summary>Decodes the GPSInfo message at a recorded field path such as "3-3-4-1" (for <c>LastSameField</c>).</summary>
    public static GeoPoint? DecodeAt(ReadOnlyMemory<byte> sample, string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(fieldPath);
        IReadOnlyList<PbField>? tree = Protobuf.DecodeTree(sample);
        if (tree is null) return null;
        string[] parts = fieldPath.Split('-');
        var path = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out path[i])) return null;
        return GpsInfo(Protobuf.GetPath(tree, path)?.Message, Units.Guess);
    }

    private static string StripSuffix(string protocol)
        => protocol.EndsWith(".proto", StringComparison.Ordinal) ? protocol[..^".proto".Length] : protocol;

    private static ulong? VarintAt(IReadOnlyList<PbField> tree, ImmutableArray<int> path)
        => Protobuf.GetPath(tree, path.AsSpan()) is { WireType: 0 } f ? f.Value : null;

    private static bool TryGeneric(IReadOnlyList<PbField> tree, List<int> path, out GeoPoint hit)
    {
        foreach (PbField f in tree)
        {
            if (f.Message is null) continue;
            path.Add(f.Number);
            if (GpsInfo(f.Message, Units.Guess) is { } p && InRange(p) && (p.Lat != 0 || p.Lon != 0))
            {
                hit = p;
                return true;
            }
            if (TryGeneric(f.Message, path, out hit)) return true;
            path.RemoveAt(path.Count - 1);
        }
        hit = default;
        return false;
    }

    private static bool InRange(GeoPoint p) => Math.Abs(p.Lat) <= 90 && Math.Abs(p.Lon) <= 180;

    private static GeoPoint? GpsInfo(IReadOnlyList<PbField>? message, Units mode)
    {
        if (message is null) return null;
        ulong? units = null;
        double? lat = null, lon = null;
        foreach (PbField f in message)
        {
            if (f.Number == 1 && f.WireType == 0) units = f.Value;
            else if (f.Number == 2 && f.WireType == 1) lat = BinaryPrimitives.ReadDoubleLittleEndian(f.Raw.Span);
            else if (f.Number == 3 && f.WireType == 1) lon = BinaryPrimitives.ReadDoubleLittleEndian(f.Raw.Span);
        }
        if (lat is null && lon is null) return null;
        double la = lat ?? 0, lo = lon ?? 0;
        if (double.IsNaN(la) || double.IsNaN(lo)) return null;
        bool radians = mode switch
        {
            Units.Degrees => false,
            Units.FromField => units is null or 0,
            _ => units is { } u ? u == 0 : Math.Abs(la) <= Math.PI / 2 && Math.Abs(lo) <= Math.PI,
        };
        return radians ? new GeoPoint(la * RadToDeg, lo * RadToDeg) : new GeoPoint(la, lo);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*DjmdDecoderTests*"
dotnet test --solution uas-sort.slnx
```

Expected: 26 tests pass; the full suite passes with no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Media/DjmdDecoderTests.cs src/UasSort.Core/Media/DjmdDecoder.cs
git commit -F- <<'EOF'
feat: add djmd sample decoder with per-model GPS table and generic search

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

### Task 03.6: Sample tables

Lazy access to one track's tables (Ref §6.3 step 2): `stsd` entry formats read header by header, `stsc` kept in memory, `stsz`/`stz2` and `stco`/`co64` entries read on demand through the block cache; `Locate(k)` maps a 0-based sample through the `stsc` runs and returns its description index, so the caller can check it points at the `djmd` entry.

**Files:**
- Create: `src/UasSort.Core/Media/Mp4SampleTable.cs`
- Test: `tests/UasSort.Core.Tests/Media/Mp4SampleTableTests.cs`

**Interfaces:**

Consumes:

```csharp
// Tasks 03.1–03.2: BlockCache, Mp4Box, Mp4Boxes.   Task 03.4 (tests): SyntheticMp4Builder
```

Produces:

```csharp
namespace UasSort.Core.Media;
public readonly record struct SampleLocation(long Offset, int Size, int DescriptionIndex);   // (defined here)
public sealed class Mp4SampleTable                               // (defined here)
{
    public ImmutableArray<string> Formats { get; }
    public int SampleCount { get; }
    public int DjmdDescription { get; }                           // 1-based; 0 = none
    public static ImmutableArray<string> ReadFormats(BlockCache cache, Mp4Box stsd);
    public static Mp4SampleTable? Open(BlockCache cache, Mp4Box stbl);
    public SampleLocation? Locate(int k);
}
```

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/Media/Mp4SampleTableTests.cs`:

```csharp
using UasSort.Core.Media;
using UasSort.Testing;

namespace UasSort.Core.Tests.Media;

public sealed class Mp4SampleTableTests
{
    private static List<Mp4Box> Stbls(BlockCache cache)
    {
        Mp4Box moov = Mp4Boxes.Walk(cache, 0, cache.Length).Single(b => b.Type == "moov");
        var result = new List<Mp4Box>();
        foreach (Mp4Box trak in Mp4Boxes.Walk(cache, moov.Body, moov.End).Where(b => b.Type == "trak"))
        {
            Mp4Box mdia = Mp4Boxes.Child(cache, trak, "mdia")!.Value;
            Mp4Box minf = Mp4Boxes.Child(cache, mdia, "minf")!.Value;
            result.Add(Mp4Boxes.Child(cache, minf, "stbl")!.Value);
        }
        return result;
    }

    [Fact]
    public void Open_TwoStsdEntries_FindsDjmdAsEntry2()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { DjmdSecondStsdEntry = true }.BuildFile();
        var cache = new BlockCache(new MemoryStream(mp4.Bytes));

        Mp4SampleTable table = Mp4SampleTable.Open(cache, Stbls(cache)[1])!;

        Assert.Equal(["mett", "djmd"], table.Formats);
        Assert.Equal(2, table.DjmdDescription);
        Assert.Equal(40, table.SampleCount);
    }

    [Fact]
    public void ReadFormats_VideoTrack_HasNoDjmd()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder().BuildFile();
        var cache = new BlockCache(new MemoryStream(mp4.Bytes));

        Mp4Box stsd = Mp4Boxes.Child(cache, Stbls(cache)[0], "stsd")!.Value;

        Assert.Equal(["hvc1"], Mp4SampleTable.ReadFormats(cache, stsd));
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(3, true, true)]
    [InlineData(5, false, true)]
    [InlineData(5, true, false)]
    public void Locate_MatchesTheBuilderLayout(int perChunk, bool co64, bool moovFirst)
    {
        var builder = new SyntheticMp4Builder { SamplesPerChunk = perChunk, Co64 = co64, MoovFirst = moovFirst, DjmdSecondStsdEntry = true };
        SyntheticMp4 mp4 = builder.BuildFile();
        var cache = new BlockCache(new MemoryStream(mp4.Bytes));
        Mp4SampleTable table = Mp4SampleTable.Open(cache, Stbls(cache)[1])!;

        for (int k = 0; k < 40; k++)
        {
            SampleLocation loc = table.Locate(k)!.Value;
            Assert.Equal(mp4.SampleOffsets[k], loc.Offset);
            Assert.Equal(builder.BuildSample(k).Length, loc.Size);
            Assert.Equal(2, loc.DescriptionIndex);
        }
    }

    [Fact]
    public void Locate_OutOfRange_ReturnsNull()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder().BuildFile();
        var cache = new BlockCache(new MemoryStream(mp4.Bytes));
        Mp4SampleTable table = Mp4SampleTable.Open(cache, Stbls(cache)[1])!;

        Assert.Null(table.Locate(40));
        Assert.Null(table.Locate(-1));
    }

    [Fact]
    public void Open_WithoutStsc_ReturnsNull()
    {
        byte[] stsd = [0, 0, 0, 24, .. "stsd"u8.ToArray(), 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 8, .. "djmd"u8.ToArray()];
        byte[] stbl = [0, 0, 0, (byte)(8 + stsd.Length), .. "stbl"u8.ToArray(), .. stsd];
        var cache = new BlockCache(new MemoryStream(stbl));
        Mp4Box box = Mp4Boxes.Walk(cache, 0, cache.Length).Single();

        Assert.Equal(["djmd"], Mp4SampleTable.ReadFormats(cache, Mp4Boxes.Child(cache, box, "stsd")!.Value));
        Assert.Null(Mp4SampleTable.Open(cache, box));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*Mp4SampleTableTests*"
```

Expected: the build fails with CS0103/CS0246 (`Mp4SampleTable` not found).

- [ ] **Step 3: Implement**

`src/UasSort.Core/Media/Mp4SampleTable.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*Mp4SampleTableTests*"
dotnet test --solution uas-sort.slnx
```

Expected: 8 tests pass; the full suite passes with no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Media/Mp4SampleTableTests.cs src/UasSort.Core/Media/Mp4SampleTable.cs
git commit -F- <<'EOF'
feat: add lazy MP4 sample tables (stsc, stsz/stz2, stco/co64)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

### Task 03.7: tnal thumbnail range

Port of `mp4thumb.py` for `tnal` only (Ref §6.3 step 8): walk `moov/udta/meta/ilst/tnal/data` headers and return the range of the 160×90 JPEG after the `data` box's type and locale words. `meta` is a full box in DJI files; a QuickTime-style `meta` (children start with `hdlr`) is recognised too. `covr` is deferred (Ref).

**Files:**
- Create: `src/UasSort.Core/Media/Mp4Thumb.cs`
- Test: `tests/UasSort.Core.Tests/Media/Mp4ThumbTests.cs`

**Interfaces:**

Consumes:

```csharp
// Part 02: ByteRange.   Tasks 03.1–03.2: BlockCache, Mp4Box, Mp4Boxes.
```

Produces:

```csharp
namespace UasSort.Core.Media;
public static class Mp4Thumb { public static ByteRange? Find(BlockCache cache, Mp4Box moov); }   // (defined here)
```

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/Media/Mp4ThumbTests.cs`:

```csharp
using UasSort.Core.Media;
using UasSort.Testing;

namespace UasSort.Core.Tests.Media;

public sealed class Mp4ThumbTests
{
    private static (BlockCache Cache, Mp4Box Moov) Open(SyntheticMp4 mp4)
    {
        var cache = new BlockCache(new MemoryStream(mp4.Bytes));
        return (cache, Mp4Boxes.Walk(cache, 0, cache.Length).Single(b => b.Type == "moov"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_ReturnsTheTnalJpegRange(bool moovFirst)
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { MoovFirst = moovFirst }.BuildFile();
        (BlockCache cache, Mp4Box moov) = Open(mp4);

        Assert.Equal(mp4.Thumb, Mp4Thumb.Find(cache, moov));
        Assert.Equal(SyntheticMp4Builder.TinyJpeg.Length, mp4.Thumb!.Value.Length);
    }

    [Fact]
    public void Find_NoUdta_ReturnsNull()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { Thumbnail = [] }.BuildFile();
        (BlockCache cache, Mp4Box moov) = Open(mp4);

        Assert.Null(Mp4Thumb.Find(cache, moov));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*Mp4ThumbTests*"
```

Expected: the build fails with CS0103 (`The name 'Mp4Thumb' does not exist in the current context`).

- [ ] **Step 3: Implement**

`src/UasSort.Core/Media/Mp4Thumb.cs`:

```csharp

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
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*Mp4ThumbTests*"
dotnet test --solution uas-sort.slnx
```

Expected: 3 tests pass; the full suite passes with no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Media/Mp4ThumbTests.cs src/UasSort.Core/Media/Mp4Thumb.cs
git commit -F- <<'EOF'
feat: find the MP4 tnal thumbnail range

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

### Task 03.8: mdat-head fallback for clips without moov

Truncated recordings (Anvil 0014 and 0024 in the calibration) have no `moov`; DJI writes djmd sample 0 first in `mdat` (Ref §6.3 step 7). Try the `mdat` payload start, then file offset 512; read 4 KB, keep the leading length-delimited top-level fields 1–3, decode, and accept only a protocol ending in `.proto`.

**Files:**
- Create: `src/UasSort.Core/Media/Mp4MdatHead.cs`
- Test: `tests/UasSort.Core.Tests/Media/Mp4MdatHeadTests.cs`

**Interfaces:**

Consumes:

```csharp
// Tasks 03.1–03.5: BlockCache, Mp4Box, Mp4Boxes, Protobuf.TryReadVarint, DjmdDecoder, DjmdReading
```

Produces:

```csharp
namespace UasSort.Core.Media;
public static class Mp4MdatHead                                  // (defined here)
{
    public const long FixedOffset = 512;
    public const int HeadBytes = 4096;
    public static DjmdReading? TryRead(BlockCache cache, Mp4Box? mdat);
}
```

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/Media/Mp4MdatHeadTests.cs`:

```csharp
using UasSort.Core.Media;
using UasSort.Testing;

namespace UasSort.Core.Tests.Media;

public sealed class Mp4MdatHeadTests
{
    private static (BlockCache Cache, Mp4Box? Mdat) Open(byte[] bytes)
    {
        var cache = new BlockCache(new MemoryStream(bytes));
        Mp4Box? mdat = null;
        foreach (Mp4Box b in Mp4Boxes.Walk(cache, 0, cache.Length))
            if (b.Type == "mdat") mdat = b;
        return (cache, mdat);
    }

    [Fact]
    public void TryRead_UsesTheMdatPayloadStart()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { IncludeMoov = false }.BuildFile();
        (BlockCache cache, Mp4Box? mdat) = Open(mp4.Bytes);

        DjmdReading r = Mp4MdatHead.TryRead(cache, mdat)!;

        Assert.NotEqual(512, mp4.MdatPayloadStart);
        Assert.Equal("dvtm_Air3s.proto", r.Protocol);
        Assert.Equal(SyntheticMp4Builder.Zachar0128.Lat, r.Point!.Value.Lat, 1e-9);
        Assert.Equal(SyntheticMp4Builder.Zachar0128.Lon, r.Point!.Value.Lon, 1e-9);
    }

    [Fact]
    public void TryRead_FallsBackToOffset512WhenTheWalkNeverReachesMdat()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { IncludeMoov = false, MdatPayloadAt512 = true, CorruptPaddingBox = true }.BuildFile();
        (BlockCache cache, Mp4Box? mdat) = Open(mp4.Bytes);

        DjmdReading r = Mp4MdatHead.TryRead(cache, mdat)!;

        Assert.Null(mdat);
        Assert.True(DjmdDecoder.IsFix(r.Point!.Value));
    }

    [Fact]
    public void TryRead_ZeroedGps_ReturnsTheReadingWithoutAFix()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { IncludeMoov = false, ZeroGpsBefore = 40 }.BuildFile();
        (BlockCache cache, Mp4Box? mdat) = Open(mp4.Bytes);

        DjmdReading r = Mp4MdatHead.TryRead(cache, mdat)!;

        Assert.Equal(new GeoPoint(0, 0), r.Point);
    }

    [Fact]
    public void TryRead_NoProtocol_ReturnsNull()
    {
        byte[] zeros = new byte[8192];
        (BlockCache cache, Mp4Box? mdat) = Open(zeros);

        Assert.Null(Mp4MdatHead.TryRead(cache, mdat));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*Mp4MdatHeadTests*"
```

Expected: the build fails with CS0103 (`The name 'Mp4MdatHead' does not exist in the current context`).

- [ ] **Step 3: Implement**

`src/UasSort.Core/Media/Mp4MdatHead.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*Mp4MdatHeadTests*"
dotnet test --solution uas-sort.slnx
```

Expected: 4 tests pass; the full suite passes with no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Media/Mp4MdatHeadTests.cs src/UasSort.Core/Media/Mp4MdatHead.cs
git commit -F- <<'EOF'
feat: recover djmd GPS from the mdat head of clips without moov

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

### Task 03.9: Mp4Probe (sample 0)

`Mp4Probe.Read` assembles the pieces (Ref §6.3 steps 1–4, 6–8 and the `mvhd` duration): top-level walk that stops at the first complete `moov`; `mvhd` creation time (seconds since 1904, 32-bit in version 0, 64-bit in version 1, `SpecifyKind` UTC) and `Duration = duration / timescale` (exact, in ticks; null without `moov` or with timescale 0); the first track whose `stsd` holds `djmd`; the first fix; `LastSameField` for generic hits (the last sample at the same field path); `SessionUtc = mvhd − uptime µs` from sample 0; the serial; the `tnal` range. In this task the fix comes from **sample 0 only**; Task 03.10 widens it to the full search order and adds the read budget. This task also adds the test helper `ProbeAssert` that unwraps the `GpsProbe` union.

**Files:**
- Create: `src/UasSort.Core/Media/Mp4Probe.cs`
- Test: `tests/UasSort.Core.Tests/Media/ProbeAssert.cs`
- Test: `tests/UasSort.Core.Tests/Media/Mp4ProbeTests.cs`

**Interfaces:**

Consumes:

```csharp
// Part 02 (namespace UasSort.Core)
public enum GpsSource { DjmdModelTable, DjmdGenericSearch, MdatHeadFallback, Exif }
public sealed record GpsFix(GeoPoint Point, double? AltM, int Sample, GpsSource Source, string? FieldPath);
public enum NoFixReason { NotDji, NoDjmdTrack, AllProbedSamplesZero, Unparseable, NoGpsTag, GenericHitImplausible }
public sealed record NoFix(NoFixReason Reason);
public union GpsProbe(GpsFix, NoFix);
public sealed record Mp4Info(DateTime? MvhdUtc, bool HasMoov, GpsProbe First, GpsFix? LastSameField, string? Protocol,
                             DateTime? SessionUtc, string? DroneSerial, ByteRange? Thumb, TimeSpan? Duration);
// Tasks 03.1–03.8: BlockCache, Mp4Boxes, DjmdDecoder, Mp4SampleTable, Mp4Thumb, Mp4MdatHead.  Task 03.4 (tests): SyntheticMp4Builder
```

Produces:

```csharp
namespace UasSort.Core.Media;
public static class Mp4Probe                                     // Ref §4.2
{
    public const int MaxSampleBytes = 1 << 20;
    public static Mp4Info Read(Stream s);                         // never throws on malformed content; stream errors propagate
}
namespace UasSort.Core.Tests.Media;
internal static class ProbeAssert                                // (defined here, test helper)
{ GpsFix Fix(GpsProbe probe); NoFix NoFix(GpsProbe probe); void Near(GeoPoint expected, GeoPoint actual, double tolerance = 1e-9); }
```

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/Media/ProbeAssert.cs`:

```csharp
using Xunit.Sdk;

namespace UasSort.Core.Tests.Media;

/// <summary>Unwraps the <see cref="GpsProbe"/> union in assertions.</summary>
internal static class ProbeAssert
{
    public static GpsFix Fix(GpsProbe probe) => probe is GpsFix fix ? fix : throw new XunitException("Expected a GpsFix.");

    public static NoFix NoFix(GpsProbe probe) => probe is NoFix none ? none : throw new XunitException("Expected a NoFix.");

    public static void Near(GeoPoint expected, GeoPoint actual, double tolerance = 1e-9)
    {
        Assert.Equal(expected.Lat, actual.Lat, tolerance);
        Assert.Equal(expected.Lon, actual.Lon, tolerance);
    }
}
```

`tests/UasSort.Core.Tests/Media/Mp4ProbeTests.cs`:

```csharp
using UasSort.Core.Media;
using UasSort.Testing;
using static UasSort.Core.Tests.Media.ProbeAssert;

namespace UasSort.Core.Tests.Media;

public sealed class Mp4ProbeTests
{
    private static readonly DateTime Mvhd0128 = new(2026, 9, 27, 18, 6, 27, DateTimeKind.Utc);

    private static Mp4Info Probe(SyntheticMp4Builder b) => Mp4Probe.Read(new MemoryStream(b.Build()));

    [Fact]
    public void Read_SyntheticZachar0128_ReadsTimeGpsSessionSerialThumbAndLength()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder().BuildFile();

        Mp4Info info = Mp4Probe.Read(new MemoryStream(mp4.Bytes));

        Assert.True(info.HasMoov);
        Assert.Equal(Mvhd0128, info.MvhdUtc);
        Assert.Equal(DateTimeKind.Utc, info.MvhdUtc!.Value.Kind);
        GpsFix fix = Fix(info.First);
        Near(SyntheticMp4Builder.Zachar0128, fix.Point);
        Assert.Equal(298.394, fix.AltM!.Value, 1e-9);
        Assert.Equal(0, fix.Sample);
        Assert.Equal(GpsSource.DjmdModelTable, fix.Source);
        Assert.Equal("3-3-4-1", fix.FieldPath);
        Assert.Null(info.LastSameField);
        Assert.Equal("dvtm_Air3s.proto", info.Protocol);
        Assert.Equal("1581F6ZSYNTH0001", info.DroneSerial);
        Assert.Equal(new DateTime(2026, 9, 27, 18, 3, 28, DateTimeKind.Utc), info.SessionUtc);
        Assert.Equal(mp4.Thumb, info.Thumb);
        Assert.Equal(TimeSpan.FromSeconds(222), info.Duration);
    }

    [Theory]
    [InlineData(true, true, true, 3, true)]
    [InlineData(false, false, false, 1, false)]
    [InlineData(false, true, true, 5, true)]
    [InlineData(true, false, true, 1, false)]
    public void Read_BoxLayouts_AllFindTheFirstFix(bool moovFirst, bool co64, bool largeMdat, int perChunk, bool secondStsd)
    {
        var b = new SyntheticMp4Builder
        {
            MoovFirst = moovFirst, Co64 = co64, LargeMdat = largeMdat, SamplesPerChunk = perChunk, DjmdSecondStsdEntry = secondStsd,
        };

        GpsFix fix = Fix(Probe(b).First);

        Near(SyntheticMp4Builder.Zachar0128, fix.Point);
        Assert.Equal(0, fix.Sample);
    }

    [Theory]
    [InlineData(0, 1000u, 222_000ul, 222.0)]
    [InlineData(1, 1000u, 222_000ul, 222.0)]
    [InlineData(0, 90000u, 5_535_000ul, 61.5)]
    [InlineData(1, 90000u, 5_535_000ul, 61.5)]
    public void Read_MvhdVersionAndTimescale_GiveDuration(int version, uint timescale, ulong duration, double seconds)
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { MvhdVersion = version, Timescale = timescale, Duration = duration });

        Assert.Equal(TimeSpan.FromSeconds(seconds), info.Duration);
        Assert.Equal(Mvhd0128, info.MvhdUtc);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void Read_DegreesOrRadians_GiveTheSamePoint(bool writeDegrees, int? units)
    {
        GpsFix fix = Fix(Probe(new SyntheticMp4Builder { WriteDegrees = writeDegrees, CoordinateUnits = units }).First);

        Near(SyntheticMp4Builder.Zachar0128, fix.Point);
    }

    [Fact]
    public void Read_AutelStyleClipWithoutDjmd_IsNoDjmdTrack()
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { WithDjmdTrack = false });

        Assert.Equal(NoFixReason.NoDjmdTrack, NoFix(info.First).Reason);
        Assert.True(info.HasMoov);
        Assert.Equal(Mvhd0128, info.MvhdUtc);
        Assert.Equal(TimeSpan.FromSeconds(222), info.Duration);
        Assert.Null(info.Protocol);
    }

    [Fact]
    public void Read_UnknownProtocol_GenericHitWithLastSameField()
    {
        var b = new SyntheticMp4Builder { Protocol = "dvtm_Future9.proto", GpsPath = [3, 5, 7, 1] };

        Mp4Info info = Probe(b);

        GpsFix fix = Fix(info.First);
        Assert.Equal(GpsSource.DjmdGenericSearch, fix.Source);
        Assert.Equal("3-5-7-1", fix.FieldPath);
        Assert.Equal("dvtm_Future9.proto", info.Protocol);
        GpsFix last = info.LastSameField!;
        Assert.Equal(39, last.Sample);
        Assert.Equal("3-5-7-1", last.FieldPath);
        Assert.Equal(GpsSource.DjmdGenericSearch, last.Source);
        Near(SyntheticMp4Builder.Zachar0128, last.Point);
    }

    [Fact]
    public void Read_NoMoov_UsesTheMdatPayloadStart()
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { IncludeMoov = false });

        Assert.False(info.HasMoov);
        Assert.Null(info.MvhdUtc);
        Assert.Null(info.Duration);
        Assert.Null(info.SessionUtc);
        Assert.Null(info.Thumb);
        GpsFix fix = Fix(info.First);
        Assert.Equal(GpsSource.MdatHeadFallback, fix.Source);
        Assert.Equal(0, fix.Sample);
        Near(SyntheticMp4Builder.Zachar0128, fix.Point);
        Assert.Equal("dvtm_Air3s.proto", info.Protocol);
        Assert.Equal("1581F6ZSYNTH0001", info.DroneSerial);
    }

    [Fact]
    public void Read_NoMoov_FallsBackToOffset512()
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { IncludeMoov = false, MdatPayloadAt512 = true, CorruptPaddingBox = true });

        Assert.Equal(GpsSource.MdatHeadFallback, Fix(info.First).Source);
    }

    [Fact]
    public void Read_NoMoovAndNothingDjiLike_IsNotDji()
    {
        Mp4Info info = Mp4Probe.Read(new MemoryStream(new byte[8192]));

        Assert.Equal(NoFixReason.NotDji, NoFix(info.First).Reason);
        Assert.False(info.HasMoov);
    }

    [Fact]
    public void Read_NoMoovWithZeroedGps_IsAllProbedSamplesZero()
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { IncludeMoov = false, ZeroGpsBefore = 40 });

        Assert.Equal(NoFixReason.AllProbedSamplesZero, NoFix(info.First).Reason);
        Assert.Equal("dvtm_Air3s.proto", info.Protocol);
    }

    [Fact]
    public void Read_MoovWithSize0_RunsToEndOfFile()
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { LastBoxSizeZero = true });

        Assert.True(info.HasMoov);
        Assert.Equal(0, Fix(info.First).Sample);
    }

    [Fact]
    public void Read_MoovClaimingMoreThanTheFile_IsTreatedAsMissing()
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { LastBoxOverclaim = 100 });

        Assert.False(info.HasMoov);
        Assert.Null(info.Duration);
        Assert.Equal(GpsSource.MdatHeadFallback, Fix(info.First).Source);
    }

    [Fact]
    public void Read_TruncatedMdatWithoutMoov_StillReadsItsHead()
    {
        Mp4Info info = Probe(new SyntheticMp4Builder { IncludeMoov = false, LastBoxOverclaim = 1_000_000 });

        Assert.Equal(GpsSource.MdatHeadFallback, Fix(info.First).Source);
    }

    [Fact]
    public void Read_UuidBoxAtTopLevel_IsSkipped()
    {
        Assert.Equal(GpsSource.DjmdModelTable, Fix(Probe(new SyntheticMp4Builder { UuidBox = true }).First).Source);
    }

    [Fact]
    public void Read_FluentBuilderClip_RoundTrips()
    {
        var anvil = new GeoPoint(64.5627, -165.3696);
        byte[] jpeg = [0xFF, 0xD8, 0x01, 0x02, 0xFF, 0xD9];
        byte[] clip = new SyntheticMp4Builder()
            .WithMvhdUtc(new DateTime(2026, 7, 26, 7, 50, 0, DateTimeKind.Utc))
            .WithDuration(TimeSpan.FromSeconds(95))
            .WithDjmdGps("dvtm_AVATA2.proto", anvil)
            .WithThumbnail(jpeg)
            .Build();

        Mp4Info info = Mp4Probe.Read(new MemoryStream(clip));

        Assert.Equal(new DateTime(2026, 7, 26, 7, 50, 0, DateTimeKind.Utc), info.MvhdUtc);
        Assert.Equal(TimeSpan.FromSeconds(95), info.Duration);
        GpsFix fix = Fix(info.First);
        Near(anvil, fix.Point);
        Assert.Equal("3-4-4-1", fix.FieldPath);
        Assert.Equal(jpeg.Length, info.Thumb!.Value.Length);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*Mp4ProbeTests*"
```

Expected: the build fails with CS0103 (`The name 'Mp4Probe' does not exist in the current context`).

- [ ] **Step 3: Implement**

`src/UasSort.Core/Media/Mp4Probe.cs`:

```csharp
using System.Buffers.Binary;

namespace UasSort.Core.Media;

/// <summary>
/// Reads <c>mvhd</c> time and length, first djmd GPS fix, session, serial and thumbnail range from a DJI MP4
/// (Ref §6.3; port of docs/research/spikes/djmd/djmd_gps.py). Never reads <c>mdat</c> or the whole <c>moov</c>.
/// Malformed content gives a <see cref="NoFix"/>; only stream errors throw.
/// </summary>
public static class Mp4Probe
{
    public const int MaxSampleBytes = 1 << 20;

    private static readonly DateTime QtEpoch = new(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ulong MaxQtSeconds = (ulong)((DateTime.MaxValue - QtEpoch).Ticks / TimeSpan.TicksPerSecond);
    private static readonly int[] FirstSampleOnly = [0];   // sample 0 only; Task 03.10 widens this to ProbeIndices
    private static readonly ulong MaxUptimeMicroseconds = (ulong)(TimeSpan.FromDays(3650).Ticks / 10);

    public static Mp4Info Read(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var cache = new BlockCache(s);
        Mp4Box? moov = null, mdat = null;
        foreach (Mp4Box b in Mp4Boxes.Walk(cache, 0, cache.Length))
        {
            if (b.Type == "moov" && !b.Truncated)
            {
                moov = b;
                break;
            }
            if (b.Type == "mdat") mdat ??= b;
        }
        return moov is { } m ? ReadMoov(cache, m) : ReadWithoutMoov(cache, mdat);
    }

    private static Mp4Info ReadWithoutMoov(BlockCache cache, Mp4Box? mdat)
    {
        DjmdReading? r = Mp4MdatHead.TryRead(cache, mdat);
        GpsProbe first;
        if (r is null) first = new NoFix(NoFixReason.NotDji);
        else if (r.Point is { } p && DjmdDecoder.IsFix(p)) first = new GpsFix(p, r.AltM, 0, GpsSource.MdatHeadFallback, r.FieldPath);
        else first = new NoFix(NoFixReason.AllProbedSamplesZero);
        return new Mp4Info(MvhdUtc: null, HasMoov: false, First: first, LastSameField: null, Protocol: r?.Protocol,
                           SessionUtc: null, DroneSerial: r?.DroneSerial, Thumb: null, Duration: null);
    }

    private static Mp4Info ReadMoov(BlockCache cache, Mp4Box moov)
    {
        DateTime? created = null;
        TimeSpan? duration = null;
        bool sawMvhd = false;
        Mp4Box? djmdStbl = null;
        foreach (Mp4Box b in Mp4Boxes.Walk(cache, moov.Body, moov.End))
        {
            if (b.Type == "mvhd" && !sawMvhd)
            {
                sawMvhd = true;
                (created, duration) = ParseMvhd(cache.ReadAt(b.Body, (int)Math.Min(32, b.End - b.Body)));
            }
            else if (b.Type == "trak" && djmdStbl is null && DjmdStbl(cache, b) is { } stbl)
            {
                djmdStbl = stbl;
            }
        }
        ByteRange? thumb = Mp4Thumb.Find(cache, moov);
        if (djmdStbl is not { } found)
            return new Mp4Info(created, true, new NoFix(NoFixReason.NoDjmdTrack), null, null, null, null, thumb, duration);
        if (Mp4SampleTable.Open(cache, found) is not { DjmdDescription: > 0 } table)
            return new Mp4Info(created, true, new NoFix(NoFixReason.Unparseable), null, null, null, null, thumb, duration);

        SampleSearch search = FindFirstFix(cache, table);
        GpsFix? last = search.First is { Source: GpsSource.DjmdGenericSearch } generic ? LastSameField(cache, table, generic) : null;
        DateTime? session = created is { } c && search.UptimeUs is { } up && up <= MaxUptimeMicroseconds
            ? c - TimeSpan.FromTicks((long)up * 10) : null;
        GpsProbe probe;
        if (search.First is { } fix) probe = fix;
        else probe = new NoFix(search.AnyDecoded ? NoFixReason.AllProbedSamplesZero : NoFixReason.Unparseable);
        return new Mp4Info(created, true, probe, last, search.Protocol, session, search.Serial, thumb, duration);
    }

    private static Mp4Box? DjmdStbl(BlockCache cache, Mp4Box trak)
    {
        if (Mp4Boxes.Child(cache, trak, "mdia") is not { } mdia) return null;
        if (Mp4Boxes.Child(cache, mdia, "minf") is not { } minf) return null;
        if (Mp4Boxes.Child(cache, minf, "stbl") is not { } stbl) return null;
        if (Mp4Boxes.Child(cache, stbl, "stsd") is not { } stsd) return null;
        return Mp4SampleTable.ReadFormats(cache, stsd).Contains("djmd") ? stbl : null;
    }

    private static (DateTime? Created, TimeSpan? Duration) ParseMvhd(ReadOnlySpan<byte> b)
    {
        if (b.Length < 20) return (null, null);
        ulong created, duration;
        uint timescale;
        if (b[0] == 1)
        {
            if (b.Length < 32) return (null, null);
            created = BinaryPrimitives.ReadUInt64BigEndian(b[4..]);
            timescale = BinaryPrimitives.ReadUInt32BigEndian(b[20..]);
            duration = BinaryPrimitives.ReadUInt64BigEndian(b[24..]);
        }
        else
        {
            created = BinaryPrimitives.ReadUInt32BigEndian(b[4..]);
            timescale = BinaryPrimitives.ReadUInt32BigEndian(b[12..]);
            duration = BinaryPrimitives.ReadUInt32BigEndian(b[16..]);
        }
        DateTime? utc = created == 0 || created > MaxQtSeconds ? null : QtEpoch.AddTicks((long)created * TimeSpan.TicksPerSecond);
        TimeSpan? length = null;
        if (timescale != 0)
        {
            UInt128 ticks = (UInt128)duration * (ulong)TimeSpan.TicksPerSecond / timescale;
            if (ticks <= long.MaxValue) length = TimeSpan.FromTicks((long)ticks);
        }
        return (utc, length);
    }

    private sealed record SampleSearch(GpsFix? First, string? Protocol, ulong? UptimeUs, string? Serial, bool AnyDecoded);

    private static SampleSearch FindFirstFix(BlockCache cache, Mp4SampleTable table)
    {
        string? protocol = null, serial = null;
        ulong? uptime = null;
        bool anyDecoded = false;
        foreach (int k in FirstSampleOnly)
        {
            if (ReadSample(cache, table, k) is not { } bytes) continue;
            if (DjmdDecoder.Decode(bytes, protocol) is not { } r) continue;
            anyDecoded = true;
            protocol ??= r.Protocol;
            if (k == 0)
            {
                uptime = r.UptimeUs;
                serial = r.DroneSerial;
            }
            if (r.Point is { } p && DjmdDecoder.IsFix(p))
            {
                var fix = new GpsFix(p, r.AltM, k, r.Generic ? GpsSource.DjmdGenericSearch : GpsSource.DjmdModelTable, r.FieldPath);
                return new SampleSearch(fix, protocol, uptime, serial, true);
            }
        }
        return new SampleSearch(null, protocol, uptime, serial, anyDecoded);
    }

    private static GpsFix? LastSameField(BlockCache cache, Mp4SampleTable table, GpsFix first)
    {
        int last = table.SampleCount - 1;
        if (first.Sample == last) return first;
        if (first.FieldPath is not { } path || ReadSample(cache, table, last) is not { } bytes) return null;
        return DjmdDecoder.DecodeAt(bytes, path) is { } p && DjmdDecoder.IsFix(p)
            ? new GpsFix(p, null, last, GpsSource.DjmdGenericSearch, path) : null;
    }

    private static byte[]? ReadSample(BlockCache cache, Mp4SampleTable table, int k)
    {
        if (table.Locate(k) is not { } loc) return null;
        if (loc.DescriptionIndex != table.DjmdDescription || loc.Size <= 0 || loc.Size > MaxSampleBytes) return null;
        byte[] bytes = cache.ReadAt(loc.Offset, loc.Size);
        return bytes.Length == loc.Size ? bytes : null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*Mp4ProbeTests*"
dotnet test --solution uas-sort.slnx
```

Expected: 23 tests pass; the full suite passes with no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Media/ProbeAssert.cs tests/UasSort.Core.Tests/Media/Mp4ProbeTests.cs src/UasSort.Core/Media/Mp4Probe.cs
git commit -F- <<'EOF'
feat: add Mp4Probe (mvhd time and duration, djmd first fix, session, tnal)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

### Task 03.10: Multi-sample search and the 16-read budget

Ref §6.3 step 5: sample 0 is always read; without a fix, probe samples 1–9, then 16, 32, 64, … below n, then n − 1 — at most 1 + 9 + |{2^k ≥ 16, 2^k &lt; n}| + 1 sample reads, 16 for n = 300. The budget test builds 300 samples each on its own 4 KB block (so one sample read = one underlying block read inside `mdat`) and counts the underlying reads that land in `mdat`: exactly 16 when GPS never appears, 12 when it first appears at sample 32, 1 when sample 0 has it; a clip with the fix in sample 0 costs at most 7 underlying reads in total (Ref: about 6–7 per file). GPS zeroed until sample 7, until sample 32, and everywhere are covered with different chunk layouts.

**Files:**
- Modify: `src/UasSort.Core/Media/Mp4Probe.cs`
- Test: `tests/UasSort.Core.Tests/Media/Mp4ProbeSearchTests.cs`

**Interfaces:**

Consumes:

```csharp
// Task 03.9: Mp4Probe.Read.  Task 03.1 (tests): CountingStream.ReadsWithin.  Task 03.4 (tests): SyntheticMp4Builder
```

Produces:

```csharp
namespace UasSort.Core.Media;
public static class Mp4Probe { public static IReadOnlyList<int> ProbeIndices(int sampleCount); }   // (added here)
```

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/Media/Mp4ProbeSearchTests.cs`:

```csharp
using UasSort.Core.Media;
using UasSort.Testing;
using static UasSort.Core.Tests.Media.ProbeAssert;

namespace UasSort.Core.Tests.Media;

public sealed class Mp4ProbeSearchTests
{
    [Fact]
    public void ProbeIndices_300Samples_Are16()
    {
        int[] expected = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 16, 32, 64, 128, 256, 299];

        Assert.Equal(expected, Mp4Probe.ProbeIndices(300));
    }

    [Fact]
    public void ProbeIndices_SmallCounts()
    {
        Assert.Empty(Mp4Probe.ProbeIndices(0));
        Assert.Equal([0], Mp4Probe.ProbeIndices(1));
        Assert.Equal(Enumerable.Range(0, 10), Mp4Probe.ProbeIndices(10));
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 16], Mp4Probe.ProbeIndices(17));
    }

    [Fact]
    public void Read_GpsZeroedUntilSample7_FixFromSample7()
    {
        var b = new SyntheticMp4Builder { ZeroGpsBefore = 7, SamplesPerChunk = 4, Co64 = true, DjmdSecondStsdEntry = true };

        GpsFix fix = Fix(Mp4Probe.Read(new MemoryStream(b.Build())).First);

        Assert.Equal(7, fix.Sample);
        Near(SyntheticMp4Builder.Zachar0128, fix.Point);
    }

    [Fact]
    public void Read_GpsZeroedUntilSample32_FixFromSample32()
    {
        var b = new SyntheticMp4Builder { ZeroGpsBefore = 32, SamplesPerChunk = 5, MoovFirst = true };

        Assert.Equal(32, Fix(Mp4Probe.Read(new MemoryStream(b.Build())).First).Sample);
    }

    [Fact]
    public void Read_GpsZeroedEverywhere_IsAllProbedSamplesZero()
    {
        Mp4Info info = Mp4Probe.Read(new MemoryStream(new SyntheticMp4Builder { ZeroGpsBefore = 40 }.Build()));

        Assert.Equal(NoFixReason.AllProbedSamplesZero, NoFix(info.First).Reason);
        Assert.Equal("dvtm_Air3s.proto", info.Protocol);
        Assert.NotNull(info.SessionUtc);
    }

    [Theory]
    [InlineData(300, 16)]
    [InlineData(32, 12)]
    [InlineData(0, 1)]
    public void Read_SampleReadBudget_AtMost16ForTheFirstFix(int zeroBefore, int expectedSampleReads)
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { SampleCount = 300, ZeroGpsBefore = zeroBefore, SampleAlignment = 4096 }.BuildFile();
        var counting = new CountingStream(new MemoryStream(mp4.Bytes));

        Mp4Probe.Read(counting);

        int sampleReads = counting.ReadsWithin(mp4.MdatPayloadStart, mp4.MdatPayloadEnd);
        Assert.Equal(expectedSampleReads, sampleReads);
        Assert.True(sampleReads <= 16);
    }

    [Fact]
    public void Read_FixInSample0_CostsAtMost7UnderlyingReads()
    {
        SyntheticMp4 mp4 = new SyntheticMp4Builder { SampleCount = 300, SampleAlignment = 4096 }.BuildFile();
        var counting = new CountingStream(new MemoryStream(mp4.Bytes));

        Mp4Probe.Read(counting);

        Assert.InRange(counting.ReadCalls, 1, 7);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*Mp4ProbeSearchTests*"
```

Expected: the build fails with CS0117 (`'Mp4Probe' does not contain a definition for 'ProbeIndices'`).

- [ ] **Step 3: Implement**

Edit `src/UasSort.Core/Media/Mp4Probe.cs` in three places.

1. Delete the field:

```csharp
    private static readonly int[] FirstSampleOnly = [0];   // sample 0 only; Task 03.10 widens this to ProbeIndices
```

2. Insert this method directly after `Read(Stream s)`:

```csharp
    /// <summary>0-based samples probed for the first fix: 0–9, then 16, 32, 64, … below n, then n − 1 (Ref §6.3 step 5).</summary>
    public static IReadOnlyList<int> ProbeIndices(int sampleCount)
    {
        var indices = new List<int>();
        for (int i = 0; i < Math.Min(10, sampleCount); i++) indices.Add(i);
        for (long k = 16; k < sampleCount; k *= 2) indices.Add((int)k);
        if (sampleCount > 0 && !indices.Contains(sampleCount - 1)) indices.Add(sampleCount - 1);
        return indices;
    }
```

3. In `FindFirstFix`, replace

```csharp
        foreach (int k in FirstSampleOnly)
```

with

```csharp
        foreach (int k in ProbeIndices(table.SampleCount))
```

The whole file after the edit, for reference:

`src/UasSort.Core/Media/Mp4Probe.cs`:

```csharp
using System.Buffers.Binary;

namespace UasSort.Core.Media;

/// <summary>
/// Reads <c>mvhd</c> time and length, first djmd GPS fix, session, serial and thumbnail range from a DJI MP4
/// (Ref §6.3; port of docs/research/spikes/djmd/djmd_gps.py). Never reads <c>mdat</c> or the whole <c>moov</c>.
/// Malformed content gives a <see cref="NoFix"/>; only stream errors throw.
/// </summary>
public static class Mp4Probe
{
    public const int MaxSampleBytes = 1 << 20;

    private static readonly DateTime QtEpoch = new(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ulong MaxQtSeconds = (ulong)((DateTime.MaxValue - QtEpoch).Ticks / TimeSpan.TicksPerSecond);
    private static readonly ulong MaxUptimeMicroseconds = (ulong)(TimeSpan.FromDays(3650).Ticks / 10);

    public static Mp4Info Read(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var cache = new BlockCache(s);
        Mp4Box? moov = null, mdat = null;
        foreach (Mp4Box b in Mp4Boxes.Walk(cache, 0, cache.Length))
        {
            if (b.Type == "moov" && !b.Truncated)
            {
                moov = b;
                break;
            }
            if (b.Type == "mdat") mdat ??= b;
        }
        return moov is { } m ? ReadMoov(cache, m) : ReadWithoutMoov(cache, mdat);
    }

    /// <summary>0-based samples probed for the first fix: 0–9, then 16, 32, 64, … below n, then n − 1 (Ref §6.3 step 5).</summary>
    public static IReadOnlyList<int> ProbeIndices(int sampleCount)
    {
        var indices = new List<int>();
        for (int i = 0; i < Math.Min(10, sampleCount); i++) indices.Add(i);
        for (long k = 16; k < sampleCount; k *= 2) indices.Add((int)k);
        if (sampleCount > 0 && !indices.Contains(sampleCount - 1)) indices.Add(sampleCount - 1);
        return indices;
    }

    private static Mp4Info ReadWithoutMoov(BlockCache cache, Mp4Box? mdat)
    {
        DjmdReading? r = Mp4MdatHead.TryRead(cache, mdat);
        GpsProbe first;
        if (r is null) first = new NoFix(NoFixReason.NotDji);
        else if (r.Point is { } p && DjmdDecoder.IsFix(p)) first = new GpsFix(p, r.AltM, 0, GpsSource.MdatHeadFallback, r.FieldPath);
        else first = new NoFix(NoFixReason.AllProbedSamplesZero);
        return new Mp4Info(MvhdUtc: null, HasMoov: false, First: first, LastSameField: null, Protocol: r?.Protocol,
                           SessionUtc: null, DroneSerial: r?.DroneSerial, Thumb: null, Duration: null);
    }

    private static Mp4Info ReadMoov(BlockCache cache, Mp4Box moov)
    {
        DateTime? created = null;
        TimeSpan? duration = null;
        bool sawMvhd = false;
        Mp4Box? djmdStbl = null;
        foreach (Mp4Box b in Mp4Boxes.Walk(cache, moov.Body, moov.End))
        {
            if (b.Type == "mvhd" && !sawMvhd)
            {
                sawMvhd = true;
                (created, duration) = ParseMvhd(cache.ReadAt(b.Body, (int)Math.Min(32, b.End - b.Body)));
            }
            else if (b.Type == "trak" && djmdStbl is null && DjmdStbl(cache, b) is { } stbl)
            {
                djmdStbl = stbl;
            }
        }
        ByteRange? thumb = Mp4Thumb.Find(cache, moov);
        if (djmdStbl is not { } found)
            return new Mp4Info(created, true, new NoFix(NoFixReason.NoDjmdTrack), null, null, null, null, thumb, duration);
        if (Mp4SampleTable.Open(cache, found) is not { DjmdDescription: > 0 } table)
            return new Mp4Info(created, true, new NoFix(NoFixReason.Unparseable), null, null, null, null, thumb, duration);

        SampleSearch search = FindFirstFix(cache, table);
        GpsFix? last = search.First is { Source: GpsSource.DjmdGenericSearch } generic ? LastSameField(cache, table, generic) : null;
        DateTime? session = created is { } c && search.UptimeUs is { } up && up <= MaxUptimeMicroseconds
            ? c - TimeSpan.FromTicks((long)up * 10) : null;
        GpsProbe probe;
        if (search.First is { } fix) probe = fix;
        else probe = new NoFix(search.AnyDecoded ? NoFixReason.AllProbedSamplesZero : NoFixReason.Unparseable);
        return new Mp4Info(created, true, probe, last, search.Protocol, session, search.Serial, thumb, duration);
    }

    private static Mp4Box? DjmdStbl(BlockCache cache, Mp4Box trak)
    {
        if (Mp4Boxes.Child(cache, trak, "mdia") is not { } mdia) return null;
        if (Mp4Boxes.Child(cache, mdia, "minf") is not { } minf) return null;
        if (Mp4Boxes.Child(cache, minf, "stbl") is not { } stbl) return null;
        if (Mp4Boxes.Child(cache, stbl, "stsd") is not { } stsd) return null;
        return Mp4SampleTable.ReadFormats(cache, stsd).Contains("djmd") ? stbl : null;
    }

    private static (DateTime? Created, TimeSpan? Duration) ParseMvhd(ReadOnlySpan<byte> b)
    {
        if (b.Length < 20) return (null, null);
        ulong created, duration;
        uint timescale;
        if (b[0] == 1)
        {
            if (b.Length < 32) return (null, null);
            created = BinaryPrimitives.ReadUInt64BigEndian(b[4..]);
            timescale = BinaryPrimitives.ReadUInt32BigEndian(b[20..]);
            duration = BinaryPrimitives.ReadUInt64BigEndian(b[24..]);
        }
        else
        {
            created = BinaryPrimitives.ReadUInt32BigEndian(b[4..]);
            timescale = BinaryPrimitives.ReadUInt32BigEndian(b[12..]);
            duration = BinaryPrimitives.ReadUInt32BigEndian(b[16..]);
        }
        DateTime? utc = created == 0 || created > MaxQtSeconds ? null : QtEpoch.AddTicks((long)created * TimeSpan.TicksPerSecond);
        TimeSpan? length = null;
        if (timescale != 0)
        {
            UInt128 ticks = (UInt128)duration * (ulong)TimeSpan.TicksPerSecond / timescale;
            if (ticks <= long.MaxValue) length = TimeSpan.FromTicks((long)ticks);
        }
        return (utc, length);
    }

    private sealed record SampleSearch(GpsFix? First, string? Protocol, ulong? UptimeUs, string? Serial, bool AnyDecoded);

    private static SampleSearch FindFirstFix(BlockCache cache, Mp4SampleTable table)
    {
        string? protocol = null, serial = null;
        ulong? uptime = null;
        bool anyDecoded = false;
        foreach (int k in ProbeIndices(table.SampleCount))
        {
            if (ReadSample(cache, table, k) is not { } bytes) continue;
            if (DjmdDecoder.Decode(bytes, protocol) is not { } r) continue;
            anyDecoded = true;
            protocol ??= r.Protocol;
            if (k == 0)
            {
                uptime = r.UptimeUs;
                serial = r.DroneSerial;
            }
            if (r.Point is { } p && DjmdDecoder.IsFix(p))
            {
                var fix = new GpsFix(p, r.AltM, k, r.Generic ? GpsSource.DjmdGenericSearch : GpsSource.DjmdModelTable, r.FieldPath);
                return new SampleSearch(fix, protocol, uptime, serial, true);
            }
        }
        return new SampleSearch(null, protocol, uptime, serial, anyDecoded);
    }

    private static GpsFix? LastSameField(BlockCache cache, Mp4SampleTable table, GpsFix first)
    {
        int last = table.SampleCount - 1;
        if (first.Sample == last) return first;
        if (first.FieldPath is not { } path || ReadSample(cache, table, last) is not { } bytes) return null;
        return DjmdDecoder.DecodeAt(bytes, path) is { } p && DjmdDecoder.IsFix(p)
            ? new GpsFix(p, null, last, GpsSource.DjmdGenericSearch, path) : null;
    }

    private static byte[]? ReadSample(BlockCache cache, Mp4SampleTable table, int k)
    {
        if (table.Locate(k) is not { } loc) return null;
        if (loc.DescriptionIndex != table.DjmdDescription || loc.Size <= 0 || loc.Size > MaxSampleBytes) return null;
        byte[] bytes = cache.ReadAt(loc.Offset, loc.Size);
        return bytes.Length == loc.Size ? bytes : null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*Mp4ProbeSearchTests*"
dotnet test --solution uas-sort.slnx
```

Expected: 9 tests pass; the full suite passes with no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Media/Mp4ProbeSearchTests.cs src/UasSort.Core/Media/Mp4Probe.cs
git commit -F- <<'EOF'
feat: probe djmd samples 0-9, powers of two and the last within 16 reads

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

### Task 03.11: SyntheticDngBuilder and StillProbe

A minimal little-endian TIFF/DNG with the traps of a real DJI DNG (Ref §13 Synthetic DNG): IFD0 carries `DateTime` (0x132, not the DTO), the 160×120 JPEG strip, Make and Model; a raw sub-IFD (0x14A) comes before the EXIF IFD and has no DTO; the EXIF IFD holds the DTO and optional `OffsetTimeOriginal`; a GPS IFD. `StillProbe` (Ref §6.3 Stills) reads it through `ImageMetadataReader.ReadMetadata(Stream)`: DTO from the first `ExifDirectoryBase` that contains `TagDateTimeOriginal` (Ref §6.3), offset from the same directory, GPS from `GpsDirectory.TryGetGeoLocation` (0/0 or missing = `NoFix(NoGpsTag)`), model from IFD0, thumbnail from IFD0 (compression 6/7 with a single strip, or JPEGInterchangeFormat) only for TIFF-based files; an unreadable file throws `ImageProcessingException` (the harvester turns it into `ProbeError`). The builder's fluent API is the Part 11 contract.

**Files:**
- Create: `tests/UasSort.Testing/SyntheticDngBuilder.cs`
- Create: `src/UasSort.Core/Media/StillProbe.cs`
- Test: `tests/UasSort.Core.Tests/Media/StillProbeTests.cs`

**Interfaces:**

Consumes:

```csharp
// Part 01: MetadataExtractor 2.9.3 PackageReference in src/UasSort.Core/UasSort.Core.csproj (Stream overloads only).
// Part 02 (namespace UasSort.Core)
public sealed record StillInfo(DateTime? DtoNaive, TimeSpan? OffsetTime, GpsProbe Gps, string? Model, ByteRange? Thumb);
// Task 03.5: DjmdDecoder.IsFix.  Task 03.4: SyntheticMp4Builder.TinyJpeg.  Task 03.9 (tests): ProbeAssert
```

Produces:

```csharp
namespace UasSort.Core.Media;
public static class StillProbe { public static StillInfo Read(Stream s); }   // Ref §4.2
namespace UasSort.Testing;
public sealed record SyntheticDng(byte[] Bytes, ByteRange? Thumb);           // (defined here)
public sealed record class SyntheticDngBuilder                   // (defined here)
{
    public static readonly DateTime Pano0001Dto;                  // 2026-05-25 09:30:28 (naive)
    public static readonly GeoPoint Pano0001Gps;                  // 57.799648, -152.390180
    // init properties: Dto, Ifd0DateTime, OffsetTimeOriginal, Gps, AltM (41.5), Make ("DJI"), Model ("FC9113"),
    // IncludeRawSubIfd (true), Thumbnail (SyntheticMp4Builder.TinyJpeg)
    public SyntheticDngBuilder WithDateTimeOriginal(DateTime naive);
    public SyntheticDngBuilder WithGps(GeoPoint point);
    public SyntheticDngBuilder WithModel(string model);
    public SyntheticDngBuilder WithThumbnail(byte[] jpeg);
    public byte[] Build();
    public SyntheticDng BuildFile();
}
```

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/Media/StillProbeTests.cs`:

```csharp
using MetadataExtractor;
using UasSort.Core.Media;
using UasSort.Testing;
using static UasSort.Core.Tests.Media.ProbeAssert;

namespace UasSort.Core.Tests.Media;

public sealed class StillProbeTests
{
    private static StillInfo Probe(SyntheticDngBuilder b) => StillProbe.Read(new MemoryStream(b.Build()));

    [Fact]
    public void Read_Dto_ComesFromTheExifIfdNotIfd0OrTheRawSubIfd()
    {
        StillInfo info = Probe(new SyntheticDngBuilder());

        Assert.Equal(new DateTime(2026, 5, 25, 9, 30, 28), info.DtoNaive);
        Assert.Equal(DateTimeKind.Unspecified, info.DtoNaive!.Value.Kind);
        Assert.Null(info.OffsetTime);
    }

    [Fact]
    public void Read_Gps_FromTheGpsIfd()
    {
        GpsFix fix = Fix(Probe(new SyntheticDngBuilder()).Gps);

        Near(SyntheticDngBuilder.Pano0001Gps, fix.Point, 1e-6);
        Assert.Equal(41.5, fix.AltM!.Value, 1e-9);
        Assert.Equal(GpsSource.Exif, fix.Source);
        Assert.Equal(0, fix.Sample);
        Assert.Null(fix.FieldPath);
    }

    [Fact]
    public void Read_ModelAndThumbnail_FromIfd0()
    {
        SyntheticDng dng = new SyntheticDngBuilder().BuildFile();

        StillInfo info = StillProbe.Read(new MemoryStream(dng.Bytes));

        Assert.Equal("FC9113", info.Model);
        Assert.Equal(dng.Thumb, info.Thumb);
        Assert.Equal(SyntheticMp4Builder.TinyJpeg, dng.Bytes.AsSpan((int)info.Thumb!.Value.Offset, info.Thumb.Value.Length).ToArray());
    }

    [Theory]
    [InlineData("-08:00", -8, 0)]
    [InlineData("+05:45", 5, 45)]
    public void Read_OffsetTimeOriginal_IsParsed(string text, int hours, int minutes)
    {
        StillInfo info = Probe(new SyntheticDngBuilder { OffsetTimeOriginal = text });

        TimeSpan expected = new TimeSpan(Math.Abs(hours), minutes, 0) * Math.Sign(hours);
        Assert.Equal(expected, info.OffsetTime);
    }

    [Fact]
    public void Read_NoGpsIfd_IsNoGpsTag()
    {
        Assert.Equal(NoFixReason.NoGpsTag, NoFix(Probe(new SyntheticDngBuilder { Gps = null }).Gps).Reason);
    }

    [Fact]
    public void Read_NoDto_IsNull()
    {
        Assert.Null(Probe(new SyntheticDngBuilder { Dto = null }).DtoNaive);
    }

    [Fact]
    public void Read_NoThumbnail_IsNull()
    {
        Assert.Null(Probe(new SyntheticDngBuilder { Thumbnail = [] }).Thumb);
    }

    [Fact]
    public void Read_NotAnImage_Throws()
    {
        byte[] junk = [.. "not an image at all"u8.ToArray(), .. new byte[64]];

        Assert.Throws<ImageProcessingException>(() => StillProbe.Read(new MemoryStream(junk)));
    }

    [Fact]
    public void Read_FluentBuilderDng_RoundTrips()
    {
        var zachar = new GeoPoint(57.5368, -153.7484);
        byte[] dng = new SyntheticDngBuilder()
            .WithDateTimeOriginal(new DateTime(2026, 9, 27, 14, 5, 0))
            .WithGps(zachar)
            .WithModel("FC9113")
            .WithThumbnail([0xFF, 0xD8, 0xFF, 0xD9])
            .Build();

        StillInfo info = StillProbe.Read(new MemoryStream(dng));

        Assert.Equal(new DateTime(2026, 9, 27, 14, 5, 0), info.DtoNaive);
        Near(zachar, Fix(info.Gps).Point, 1e-6);
        Assert.Equal("FC9113", info.Model);
        Assert.Equal(4, info.Thumb!.Value.Length);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*StillProbeTests*"
```

Expected: the build fails with CS0246 (`The type or namespace name 'SyntheticDngBuilder' could not be found`) and CS0103 for `StillProbe`.

- [ ] **Step 3: Implement**

Before writing `StillProbe.cs`, confirm `src/UasSort.Core/UasSort.Core.csproj` references the MetadataExtractor package (Part 01; version 2.9.3 pinned in `Directory.Packages.props`). Part 02 lists it as a Part 01 product; if it is missing, add the `PackageReference` without a version.

`tests/UasSort.Testing/SyntheticDngBuilder.cs`:

```csharp
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace UasSort.Testing;

/// <summary>A built synthetic DNG and where its IFD0 thumbnail sits.</summary>
public sealed record SyntheticDng(byte[] Bytes, ByteRange? Thumb);

/// <summary>
/// Minimal little-endian TIFF/DNG (Ref §13): IFD0 (NewSubFileType 1, 160×120 JPEG thumbnail strip, Make, Model,
/// an IFD0 DateTime distractor, DNGVersion), a raw sub-IFD without DTO (tag 0x14A), an EXIF IFD holding the DTO and
/// optional OffsetTimeOriginal, and a GPS IFD. No user data. <see cref="Build"/> returns the bytes, <see cref="BuildFile"/> the thumbnail range too.
/// </summary>
public sealed record class SyntheticDngBuilder
{
    public static readonly DateTime Pano0001Dto = new(2026, 5, 25, 9, 30, 28);
    public static readonly GeoPoint Pano0001Gps = new(57.799648, -152.390180);

    public DateTime? Dto { get; init; } = Pano0001Dto;
    /// <summary>IFD0 DateTime (0x132), which is not the DTO; null = absent.</summary>
    public DateTime? Ifd0DateTime { get; init; } = new DateTime(2026, 5, 25, 9, 31, 0);
    /// <summary>EXIF OffsetTimeOriginal (0x9011), e.g. "-08:00"; null = absent.</summary>
    public string? OffsetTimeOriginal { get; init; }
    public GeoPoint? Gps { get; init; } = Pano0001Gps;
    public double? AltM { get; init; } = 41.5;
    public string Make { get; init; } = "DJI";
    public string Model { get; init; } = "FC9113";
    public bool IncludeRawSubIfd { get; init; } = true;
    /// <summary>IFD0 JPEG strip; empty = no thumbnail tags.</summary>
    public ImmutableArray<byte> Thumbnail { get; init; } = SyntheticMp4Builder.TinyJpeg;

    private const ushort TypeByte = 1, TypeAscii = 2, TypeShort = 3, TypeLong = 4, TypeRational = 5, TypeUndefined = 7;

    private sealed record Entry(ushort Tag, ushort Type, uint Count, byte[] Value);

    public SyntheticDngBuilder WithDateTimeOriginal(DateTime naive) => this with { Dto = naive };
    public SyntheticDngBuilder WithGps(GeoPoint point) => this with { Gps = point };

    public SyntheticDngBuilder WithModel(string model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return this with { Model = model };
    }

    public SyntheticDngBuilder WithThumbnail(byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        return this with { Thumbnail = [.. jpeg] };
    }

    /// <summary>The DNG bytes.</summary>
    public byte[] Build() => BuildFile().Bytes;

    /// <summary>The DNG bytes and the thumbnail range.</summary>
    public SyntheticDng BuildFile()
    {
        byte[] thumb = [.. Thumbnail.IsDefault ? [] : Thumbnail];
        byte[] rawData = new byte[32];

        // Pass 1 with zero pointers to size the IFDs; pointer values are inline LONGs, so sizes don't change.
        (List<Entry> ifd0, List<Entry> raw, List<Entry> exif, List<Entry>? gps) = Ifds(0, 0, 0, 0, 0, thumb.Length);
        uint ifd0Offset = 8;
        uint rawOffset = ifd0Offset + (uint)IfdSize(ifd0);
        uint exifOffset = rawOffset + (IncludeRawSubIfd ? (uint)IfdSize(raw) : 0);
        uint gpsOffset = exifOffset + (uint)IfdSize(exif);
        uint thumbOffset = gpsOffset + (gps is null ? 0 : (uint)IfdSize(gps));
        uint rawDataOffset = thumbOffset + (uint)thumb.Length + (uint)(thumb.Length % 2);

        (ifd0, raw, exif, gps) = Ifds(rawOffset, exifOffset, gpsOffset, thumbOffset, rawDataOffset, thumb.Length);
        var file = new MemoryStream();
        file.Write("II*\0"u8);
        file.Write(U32(ifd0Offset));
        file.Write(Serialize(ifd0, ifd0Offset));
        if (IncludeRawSubIfd) file.Write(Serialize(raw, rawOffset));
        file.Write(Serialize(exif, exifOffset));
        if (gps is not null) file.Write(Serialize(gps, gpsOffset));
        file.Write(thumb);
        if (thumb.Length % 2 == 1) file.WriteByte(0);
        file.Write(rawData);
        return new SyntheticDng(file.ToArray(), thumb.Length == 0 ? null : new ByteRange(thumbOffset, thumb.Length));
    }

    private (List<Entry> Ifd0, List<Entry> Raw, List<Entry> Exif, List<Entry>? Gps) Ifds(
        uint rawOffset, uint exifOffset, uint gpsOffset, uint thumbOffset, uint rawDataOffset, int thumbLength)
    {
        var ifd0 = new List<Entry>
        {
            LongEntry(0x00FE, 1), LongEntry(0x0100, 160), LongEntry(0x0101, 120),
            Ascii(0x010F, Make), Ascii(0x0110, Model),
            ShortEntry(0x0115, 3), LongEntry(0x0116, 120), LongEntry(0x8769, exifOffset),
            new(0xC612, TypeByte, 4, [1, 4, 0, 0]),
        };
        if (thumbLength > 0)
        {
            ifd0.Add(ShortEntry(0x0103, 7));
            ifd0.Add(LongEntry(0x0111, thumbOffset));
            ifd0.Add(LongEntry(0x0117, (uint)thumbLength));
        }
        if (Ifd0DateTime is { } modified) ifd0.Add(Ascii(0x0132, modified.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture)));
        if (IncludeRawSubIfd) ifd0.Add(LongEntry(0x014A, rawOffset));
        if (Gps is not null) ifd0.Add(LongEntry(0x8825, gpsOffset));

        var raw = new List<Entry>
        {
            LongEntry(0x00FE, 0), LongEntry(0x0100, 4), LongEntry(0x0101, 4), ShortEntry(0x0102, 16), ShortEntry(0x0103, 1),
            LongEntry(0x0111, rawDataOffset), LongEntry(0x0117, 32),
        };

        var exif = new List<Entry> { new(0x9000, TypeUndefined, 4, Encoding.ASCII.GetBytes("0231")) };
        if (Dto is { } taken) exif.Add(Ascii(0x9003, taken.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture)));
        if (OffsetTimeOriginal is { } offset) exif.Add(Ascii(0x9011, offset));

        List<Entry>? gps = null;
        if (Gps is { } p)
        {
            gps =
            [
                new(0x0000, TypeByte, 4, [2, 3, 0, 0]),
                Ascii(0x0001, p.Lat >= 0 ? "N" : "S"), Dms(0x0002, p.Lat),
                Ascii(0x0003, p.Lon >= 0 ? "E" : "W"), Dms(0x0004, p.Lon),
            ];
            if (AltM is { } alt)
            {
                gps.Add(new Entry(0x0005, TypeByte, 1, [alt < 0 ? (byte)1 : (byte)0]));
                gps.Add(new Entry(0x0006, TypeRational, 1, Concat(U32((uint)Math.Round(Math.Abs(alt) * 1000)), U32(1000))));
            }
        }
        return (ifd0, raw, exif, gps);
    }

    private static Entry LongEntry(ushort tag, uint v) => new(tag, TypeLong, 1, U32(v));

    private static Entry ShortEntry(ushort tag, ushort v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        return new Entry(tag, TypeShort, 1, b);
    }

    private static Entry Ascii(ushort tag, string text)
    {
        byte[] b = Encoding.ASCII.GetBytes(text + "\0");
        return new Entry(tag, TypeAscii, (uint)b.Length, b);
    }

    private static Entry Dms(ushort tag, double degrees)
    {
        double a = Math.Abs(degrees);
        uint d = (uint)Math.Floor(a);
        double minutes = (a - d) * 60;
        uint m = (uint)Math.Floor(minutes);
        uint s = (uint)Math.Round((minutes - m) * 60 * 1_000_000);
        return new Entry(tag, TypeRational, 3, Concat(U32(d), U32(1), U32(m), U32(1), U32(s), U32(1_000_000)));
    }

    private static int IfdSize(List<Entry> entries)
        => 2 + 12 * entries.Count + 4 + entries.Where(e => e.Value.Length > 4).Sum(e => e.Value.Length + e.Value.Length % 2);

    private static byte[] Serialize(List<Entry> entries, uint at)
    {
        List<Entry> sorted = [.. entries.OrderBy(e => e.Tag)];
        var head = new MemoryStream();
        var overflow = new MemoryStream();
        uint overflowStart = at + 2 + 12 * (uint)sorted.Count + 4;
        var count = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(count, (ushort)sorted.Count);
        head.Write(count);
        foreach (Entry e in sorted)
        {
            var entry = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(entry, e.Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(2), e.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), e.Count);
            if (e.Value.Length <= 4)
            {
                e.Value.CopyTo(entry, 8);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(8), overflowStart + (uint)overflow.Length);
                overflow.Write(e.Value);
                if (e.Value.Length % 2 == 1) overflow.WriteByte(0);
            }
            head.Write(entry);
        }
        head.Write(U32(0));
        head.Write(overflow.ToArray());
        return head.ToArray();
    }

    private static byte[] U32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];
}
```

`src/UasSort.Core/Media/StillProbe.cs`:

```csharp
using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace UasSort.Core.Media;

/// <summary>
/// DNG/JPG metadata through MetadataExtractor <b>Stream</b> overloads only (Ref §6.3 Stills): DTO and offset from the
/// EXIF directory that actually has the DTO, GPS, IFD0 model and the IFD0 JPEG thumbnail range. Throws
/// <see cref="ImageProcessingException"/> on an unreadable file (the harvester turns it into ProbeError).
/// </summary>
public static class StillProbe
{
    private const int TagOffsetTimeOriginal = 0x9011;
    private const int TagCompression = 0x0103;
    private const int TagStripOffsets = 0x0111;
    private const int TagStripByteCounts = 0x0117;
    private const int TagJpegOffset = 0x0201;
    private const int TagJpegLength = 0x0202;

    public static StillInfo Read(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);
        s.Position = 0;
        Span<byte> magic = stackalloc byte[4];
        bool isTiff = s.Length >= 4 && s.Read(magic) == 4
                      && (magic.SequenceEqual("II*\0"u8) || magic.SequenceEqual("MM\0*"u8));
        s.Position = 0;
        IReadOnlyList<MetadataExtractor.Directory> dirs = ImageMetadataReader.ReadMetadata(s);

        ExifDirectoryBase? dtoDir = dirs.OfType<ExifDirectoryBase>()
                                        .FirstOrDefault(d => d.ContainsTag(ExifDirectoryBase.TagDateTimeOriginal));
        DateTime? dto = null;
        if (dtoDir is not null && dtoDir.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime taken))
            dto = DateTime.SpecifyKind(taken, DateTimeKind.Unspecified);
        TimeSpan? offset = dtoDir?.GetString(TagOffsetTimeOriginal) is { } text && TryParseOffset(text, out TimeSpan o) ? o : null;

        ExifIfd0Directory? ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
        string? model = ifd0?.GetString(ExifDirectoryBase.TagModel)?.Trim();
        ByteRange? thumb = isTiff && ifd0 is not null ? ThumbRange(ifd0, s.Length) : null;
        return new StillInfo(dto, offset, Gps(dirs), string.IsNullOrEmpty(model) ? null : model, thumb);
    }

    private static GpsProbe Gps(IReadOnlyList<MetadataExtractor.Directory> dirs)
    {
        GpsDirectory? gps = dirs.OfType<GpsDirectory>().FirstOrDefault();
        if (gps is null || !gps.TryGetGeoLocation(out GeoLocation location)) return new NoFix(NoFixReason.NoGpsTag);
        var point = new GeoPoint(location.Latitude, location.Longitude);
        if (double.IsNaN(point.Lat) || double.IsNaN(point.Lon) || !DjmdDecoder.IsFix(point)) return new NoFix(NoFixReason.NoGpsTag);
        double? alt = null;
        if (gps.TryGetRational(GpsDirectory.TagAltitude, out Rational r) && r.Denominator != 0)
        {
            alt = r.ToDouble();
            if (gps.TryGetInt32(GpsDirectory.TagAltitudeRef, out int below) && below == 1) alt = -alt;
        }
        return new GpsFix(point, alt, 0, GpsSource.Exif, null);
    }

    private static ByteRange? ThumbRange(ExifIfd0Directory ifd0, long streamLength)
    {
        long offset, length;
        if (TryInteger(ifd0.GetObject(TagCompression), out long compression) && compression is 6 or 7
            && TryInteger(ifd0.GetObject(TagStripOffsets), out offset) && TryInteger(ifd0.GetObject(TagStripByteCounts), out length))
            return Checked(offset, length, streamLength);
        if (TryInteger(ifd0.GetObject(TagJpegOffset), out offset) && TryInteger(ifd0.GetObject(TagJpegLength), out length))
            return Checked(offset, length, streamLength);
        return null;
    }

    private static ByteRange? Checked(long offset, long length, long streamLength)
        => offset > 0 && length is > 0 and <= int.MaxValue && offset + length <= streamLength
            ? new ByteRange(offset, (int)length) : null;

    /// <summary>A single integer value, or a one-element array of one (a multi-strip thumbnail has no single range).</summary>
    private static bool TryInteger(object? value, out long result)
    {
        switch (value)
        {
            case null or string:
                result = 0;
                return false;
            case Array { Length: 1 } one:
                return TryInteger(one.GetValue(0), out result);
            case Array:
                result = 0;
                return false;
            case IConvertible c:
                result = c.ToInt64(CultureInfo.InvariantCulture);
                return true;
            default:
                result = 0;
                return false;
        }
    }

    /// <summary>Parses EXIF OffsetTime text such as "+08:00" or "-04:00".</summary>
    private static bool TryParseOffset(string text, out TimeSpan offset)
    {
        offset = default;
        string t = text.Trim().TrimEnd('\0');
        if (t.Length != 6 || (t[0] != '+' && t[0] != '-') || t[3] != ':') return false;
        if (!int.TryParse(t.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int h)
            || !int.TryParse(t.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int m)) return false;
        if (h > 14 || m > 59) return false;
        offset = new TimeSpan(h, m, 0);
        if (t[0] == '-') offset = -offset;
        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*StillProbeTests*"
dotnet test --solution uas-sort.slnx
```

Expected: 10 tests pass; the full suite passes with no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Media/StillProbeTests.cs tests/UasSort.Testing/SyntheticDngBuilder.cs src/UasSort.Core/Media/StillProbe.cs
git commit -F- <<'EOF'
feat: add StillProbe and SyntheticDngBuilder

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

### Task 03.12: MetadataHarvester

Harvest a card sequentially (Ref §4.2): videos first, then photos, then the first frame of each set (lowest member file name), one `OpenRandom` at a time, each stream disposed before the next. Every unit becomes a `RawItem`: `Name` = file name (set: `SetName`), `Bytes` = all files of the unit (photo: DNG + JPG twin; set: every member), `CardMtimeUtc` = the primary file (set: first frame), `DroneStamp` = EXIF DTO, else the file-name stamp. Any probe exception except cancellation and `UnsafeIoException` becomes `ProbeError` ("TypeName: message") with `Mp4`/`Still` null. Progress is `ScanPhase.ReadingMetadata` with Done 0..n and the current path, then a final report with no path. `MetadataHarvester` is a **static** class (the Ref does not say; an instance method would trip CA1822): Part 06 calls `MetadataHarvester.HarvestAsync(...)` without `new`, as its own reconcile note allows. `MemoryCardReader` is the in-memory `ICardReader` these and the thumbnail tests use.

**Files:**
- Create: `src/UasSort.Core/Media/DroneStampParser.cs`
- Create: `src/UasSort.Core/Media/MetadataHarvester.cs`
- Create: `tests/UasSort.Testing/MemoryCardReader.cs`
- Test: `tests/UasSort.Core.Tests/Media/MetadataHarvesterTests.cs`

**Interfaces:**

Consumes:

```csharp
// Part 02 (namespace UasSort.Core)
public sealed record CardEntry(string RelPath, long Size, DateTime MtimeUtc, DateTime CreationUtc, DateTime LastAccessUtc,
                               uint RawAttributes, EntryClass Class, string? Rule);
public closed record class MediaUnit(ItemId Id);   // VideoUnit(Id, Mp4, HasTrinf) | PhotoUnit(Id, Primary, JpgTwin) | SetUnit(Id, Kind, SetName, Members)
public sealed record CardInventory(CardSource Source, DateTime ListedUtc, string InventoryHash, ImmutableArray<CardEntry> Entries,
                                   ImmutableArray<MediaUnit> Units, string? CameraModel, ImmutableArray<ScanWarning> Warnings);
public sealed record RawItem(MediaUnit Unit, ItemKind Kind, string Name, long Bytes, DateTime CardMtimeUtc,
                             DateTime? DroneStamp, Mp4Info? Mp4, StillInfo? Still, string? ProbeError);
public sealed record ScanProgress(ScanPhase Phase, int Done, int Total, string? Current);
public interface ICardReader { CardIdentity CurrentIdentity(); Stream OpenRandom(string cardRelPath); Stream OpenSequential(string cardRelPath);
                               FsEntry Stat(string cardRelPath); ListingResult Relist(); CardSpace Space(); }
public class UnsafeIoException : Exception;
// Tasks 03.9–03.11: Mp4Probe.Read, StillProbe.Read
```

Produces:

```csharp
namespace UasSort.Core.Media;
public static partial class DroneStampParser                     // (defined here)
{
    public static DateTime? FromFileName(string nameOrRelPath);   // DJI_yyyyMMddHHmmss_nnnn_… → naive stamp
    public static string FileName(string nameOrRelPath);          // last segment, '/' or '\'
}
public static class MetadataHarvester                            // Ref §4.2 (static: see the task text)
{
    public static IAsyncEnumerable<RawItem> HarvestAsync(CardInventory inventory, ICardReader reader,
                                                         IProgress<ScanProgress> progress, CancellationToken ct);
    public static RawItem Harvest(MediaUnit unit, ICardReader reader);
    public static CardEntry FirstFrame(SetUnit set);
}
namespace UasSort.Testing;
public sealed class MemoryCardReader(CardIdentity identity) : ICardReader   // (defined here)
{
    public static readonly DateTime DefaultMtimeUtc;
    public CardIdentity Identity { get; set; }
    public IReadOnlyList<string> OpenLog { get; }
    public int OpenHandles { get; }
    public MemoryCardReader Add(string relPath, byte[] content);
    public MemoryCardReader FailOpen(string relPath);
}
```

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/Media/MetadataHarvesterTests.cs`:

```csharp
using UasSort.Core.Media;
using UasSort.Testing;
using static UasSort.Core.Tests.Media.ProbeAssert;

namespace UasSort.Core.Tests.Media;

public sealed class MetadataHarvesterTests
{
    private static readonly DateTime T = new(2026, 9, 27, 18, 8, 0, DateTimeKind.Utc);
    private const string VideoPath = "DCIM/DJI_001/DJI_20260927140627_0128_D.MP4";
    private const string DngPath = "DCIM/DJI_001/DJI_20260927141000_0129_D.DNG";
    private const string JpgPath = "DCIM/DJI_001/DJI_20260927141000_0129_D.JPG";
    private const string Pano1 = "DCIM/PANORAMA/001_0087/PANO_0001.DNG";
    private const string Pano2 = "DCIM/PANORAMA/001_0087/PANO_0002.DNG";

    private sealed class ScanProgressLog : IProgress<ScanProgress>
    {
        public List<ScanProgress> Reports { get; } = [];
        public void Report(ScanProgress value) => Reports.Add(value);
    }

    private static CardEntry Entry(string rel, long size, EntryClass cls) => new(rel, size, T, T, T, 0x20, cls, null);

    internal static (CardInventory Inventory, MemoryCardReader Reader) Card()
    {
        byte[] mp4 = new SyntheticMp4Builder().Build();
        byte[] dng = new SyntheticDngBuilder { Dto = new DateTime(2026, 9, 27, 14, 10, 0) }.Build();
        byte[] pano1 = new SyntheticDngBuilder().Build();
        byte[] pano2 = new SyntheticDngBuilder { Dto = new DateTime(2026, 5, 25, 9, 30, 31) }.Build();
        var reader = new MemoryCardReader(new CardIdentity(0x1234ABCD, "SD", "exFAT", 128_000_000_000))
            .Add(VideoPath, mp4).Add(DngPath, dng).Add(JpgPath, new byte[500]).Add(Pano1, pano1).Add(Pano2, pano2);
        var video = new VideoUnit(new ItemId(VideoPath), Entry(VideoPath, mp4.Length, EntryClass.Video), false);
        var photo = new PhotoUnit(new ItemId(DngPath), Entry(DngPath, dng.Length, EntryClass.Photo), Entry(JpgPath, 500, EntryClass.PhotoTwin));
        var set = new SetUnit(new ItemId("DCIM/PANORAMA/001_0087"), SetKind.Panorama, "001_0087",
                              [Entry(Pano2, pano2.Length, EntryClass.SetMember), Entry(Pano1, pano1.Length, EntryClass.SetMember)]);
        var inventory = new CardInventory(new CardSource("E:\\", null, true, false), T, "0000000000000000", [],
                                          [photo, set, video], null, []);
        return (inventory, reader);
    }

    private static async Task<List<RawItem>> Harvest(CardInventory inventory, MemoryCardReader reader, ScanProgressLog progress)
    {
        var items = new List<RawItem>();
        await foreach (RawItem item in MetadataHarvester.HarvestAsync(inventory, reader, progress, TestContext.Current.CancellationToken))
            items.Add(item);
        return items;
    }

    [Theory]
    [InlineData("DJI_20260927140627_0128_D.MP4")]
    [InlineData("DCIM/DJI_001/DJI_20260927140627_0128_D.MP4")]
    [InlineData("DCIM\\DJI_001\\DJI_20260927140627_0128_D.MP4")]
    public void DroneStampParser_ReadsTheFileNameStamp(string name)
    {
        DateTime? stamp = DroneStampParser.FromFileName(name);

        Assert.Equal(new DateTime(2026, 9, 27, 14, 6, 27), stamp);
        Assert.Equal(DateTimeKind.Unspecified, stamp!.Value.Kind);
    }

    [Theory]
    [InlineData("PANO_0001.DNG")]
    [InlineData("DJI_20261399999999_0001_D.MP4")]
    [InlineData("MAX_0061.MP4")]
    public void DroneStampParser_NoStamp_IsNull(string name) => Assert.Null(DroneStampParser.FromFileName(name));

    [Fact]
    public async Task HarvestAsync_ReadsVideosThenPhotosThenSets()
    {
        (CardInventory inventory, MemoryCardReader reader) = Card();
        var progress = new ScanProgressLog();

        List<RawItem> items = await Harvest(inventory, reader, progress);

        Assert.Equal([ItemKind.Video, ItemKind.Photo, ItemKind.Set], items.Select(i => i.Kind));
        Assert.Equal([VideoPath, DngPath, Pano1], reader.OpenLog);
        Assert.Equal(0, reader.OpenHandles);
        Assert.All(progress.Reports, r => Assert.Equal(ScanPhase.ReadingMetadata, r.Phase));
        Assert.Equal([0, 1, 2, 3], progress.Reports.Select(r => r.Done));
        Assert.Equal(VideoPath, progress.Reports[0].Current);
        Assert.Null(progress.Reports[^1].Current);
    }

    [Fact]
    public async Task HarvestAsync_FillsEachRawItem()
    {
        (CardInventory inventory, MemoryCardReader reader) = Card();

        List<RawItem> items = await Harvest(inventory, reader, new ScanProgressLog());

        RawItem video = items[0];
        Assert.Equal("DJI_20260927140627_0128_D.MP4", video.Name);
        Assert.Equal(new DateTime(2026, 9, 27, 14, 6, 27), video.DroneStamp);
        Assert.Equal(T, video.CardMtimeUtc);
        Assert.Equal(GpsSource.DjmdModelTable, Fix(video.Mp4!.First).Source);
        Assert.Null(video.Still);
        Assert.Null(video.ProbeError);

        RawItem photo = items[1];
        Assert.Equal("DJI_20260927141000_0129_D.DNG", photo.Name);
        Assert.Equal(new DateTime(2026, 9, 27, 14, 10, 0), photo.DroneStamp);
        Assert.Equal(((PhotoUnit)photo.Unit).Primary.Size + 500, photo.Bytes);
        Assert.NotNull(photo.Still);

        RawItem set = items[2];
        Assert.Equal("001_0087", set.Name);
        Assert.Equal(new DateTime(2026, 5, 25, 9, 30, 28), set.DroneStamp);
        Assert.Equal(((SetUnit)set.Unit).Members.Sum(m => m.Size), set.Bytes);
    }

    [Fact]
    public async Task HarvestAsync_ProbeFailuresBecomeProbeError()
    {
        (CardInventory inventory, MemoryCardReader reader) = Card();
        reader.FailOpen(VideoPath).Add(DngPath, [.. "not an image"u8.ToArray(), .. new byte[64]]);

        List<RawItem> items = await Harvest(inventory, reader, new ScanProgressLog());

        Assert.StartsWith("IOException:", items[0].ProbeError);
        Assert.Null(items[0].Mp4);
        Assert.Equal(new DateTime(2026, 9, 27, 14, 6, 27), items[0].DroneStamp);
        Assert.StartsWith("ImageProcessingException:", items[1].ProbeError);
        Assert.Null(items[1].Still);
        Assert.Equal(new DateTime(2026, 9, 27, 14, 10, 0), items[1].DroneStamp);
        Assert.Null(items[2].ProbeError);
        Assert.Equal(0, reader.OpenHandles);
    }

    [Fact]
    public async Task HarvestAsync_Cancelled_Throws()
    {
        (CardInventory inventory, MemoryCardReader reader) = Card();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (RawItem _ in MetadataHarvester.HarvestAsync(inventory, reader, new ScanProgressLog(), cts.Token)) { }
        });
    }

    [Fact]
    public void FirstFrame_IsTheLowestFileName()
    {
        (CardInventory inventory, _) = Card();
        SetUnit set = inventory.Units.OfType<SetUnit>().Single();

        Assert.Equal(Pano1, MetadataHarvester.FirstFrame(set).RelPath);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*MetadataHarvesterTests*"
```

Expected: the build fails with CS0246 (`The type or namespace name 'MemoryCardReader' could not be found`) and CS0103 for `MetadataHarvester` and `DroneStampParser`.

- [ ] **Step 3: Implement**

`src/UasSort.Core/Media/DroneStampParser.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace UasSort.Core.Media;

/// <summary>The drone-clock stamp in a DJI file name: <c>DJI_20260927140627_0128_D.MP4</c> → 2026-09-27 14:06:27 (naive).</summary>
public static partial class DroneStampParser
{
    /// <summary>Stamp of a file name or card-relative path ('/' or '\' separators); null when the name has none.</summary>
    public static DateTime? FromFileName(string nameOrRelPath)
    {
        ArgumentNullException.ThrowIfNull(nameOrRelPath);
        Match m = Stamp().Match(FileName(nameOrRelPath));
        return m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                                                   DateTimeStyles.None, out DateTime stamp)
            ? stamp : null;
    }

    /// <summary>The last path segment, splitting on both '/' and '\'.</summary>
    public static string FileName(string nameOrRelPath)
    {
        ArgumentNullException.ThrowIfNull(nameOrRelPath);
        int cut = nameOrRelPath.LastIndexOfAny(['/', '\\']);
        return nameOrRelPath[(cut + 1)..];
    }

    [GeneratedRegex(@"^DJI_(\d{14})_\d{4}_", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Stamp();
}
```

`src/UasSort.Core/Media/MetadataHarvester.cs`:

```csharp
using System.Runtime.CompilerServices;

namespace UasSort.Core.Media;

/// <summary>
/// Reads card metadata sequentially: videos, then photos, then the first frame of each set (Ref §4.2). A probe failure
/// becomes <see cref="RawItem.ProbeError"/>, never an exception; cancellation and <see cref="UnsafeIoException"/> propagate.
/// </summary>
public static class MetadataHarvester
{
    public static async IAsyncEnumerable<RawItem> HarvestAsync(CardInventory inventory, ICardReader reader,
                                                                IProgress<ScanProgress> progress,
                                                                [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(progress);
        List<MediaUnit> order = [.. inventory.Units.Where(u => u is VideoUnit),
                                 .. inventory.Units.Where(u => u is PhotoUnit),
                                 .. inventory.Units.Where(u => u is SetUnit)];
        for (int i = 0; i < order.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            progress.Report(new ScanProgress(ScanPhase.ReadingMetadata, i, order.Count, PrimaryPath(order[i])));
            yield return Harvest(order[i], reader);
        }
        progress.Report(new ScanProgress(ScanPhase.ReadingMetadata, order.Count, order.Count, null));
    }

    /// <summary>Probes one unit; failures become <see cref="RawItem.ProbeError"/>.</summary>
    public static RawItem Harvest(MediaUnit unit, ICardReader reader)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(reader);
        return unit switch
        {
            VideoUnit v => Video(v, reader),
            PhotoUnit p => Photo(p, reader),
            SetUnit s => Set(s, reader),
        };
    }

    /// <summary>A set's first frame: the member with the lowest file name (ordinal, case-insensitive).</summary>
    public static CardEntry FirstFrame(SetUnit set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return set.Members.OrderBy(m => DroneStampParser.FileName(m.RelPath), StringComparer.OrdinalIgnoreCase)
                          .ThenBy(m => m.RelPath, StringComparer.Ordinal)
                          .First();
    }

    private static string PrimaryPath(MediaUnit unit) => unit switch
    {
        VideoUnit v => v.Mp4.RelPath,
        PhotoUnit p => p.Primary.RelPath,
        SetUnit s => s.Members.IsDefaultOrEmpty ? s.Id.CardRelPath : FirstFrame(s).RelPath,
    };

    private static RawItem Video(VideoUnit v, ICardReader reader)
    {
        string name = DroneStampParser.FileName(v.Mp4.RelPath);
        Mp4Info? info = null;
        string? error = null;
        try
        {
            using Stream s = reader.OpenRandom(v.Mp4.RelPath);
            info = Mp4Probe.Read(s);
        }
#pragma warning disable CA1031 // a probe failure becomes ProbeError, not an exception (Ref §4.2)
        catch (Exception ex) when (ex is not OperationCanceledException and not UnsafeIoException)
#pragma warning restore CA1031
        {
            error = Describe(ex);
        }
        return new RawItem(v, ItemKind.Video, name, v.Mp4.Size, v.Mp4.MtimeUtc, DroneStampParser.FromFileName(name), info, null, error);
    }

    private static RawItem Photo(PhotoUnit p, ICardReader reader)
    {
        string name = DroneStampParser.FileName(p.Primary.RelPath);
        (StillInfo? still, string? error) = ReadStill(p.Primary.RelPath, reader);
        long bytes = p.Primary.Size + (p.JpgTwin?.Size ?? 0);
        DateTime? stamp = still?.DtoNaive ?? DroneStampParser.FromFileName(name);
        return new RawItem(p, ItemKind.Photo, name, bytes, p.Primary.MtimeUtc, stamp, null, still, error);
    }

    private static RawItem Set(SetUnit s, ICardReader reader)
    {
        if (s.Members.IsDefaultOrEmpty)
            return new RawItem(s, ItemKind.Set, s.SetName, 0, default, null, null, null, "empty set");
        CardEntry first = FirstFrame(s);
        (StillInfo? still, string? error) = ReadStill(first.RelPath, reader);
        long bytes = s.Members.Sum(m => m.Size);
        DateTime? stamp = still?.DtoNaive ?? DroneStampParser.FromFileName(first.RelPath);
        return new RawItem(s, ItemKind.Set, s.SetName, bytes, first.MtimeUtc, stamp, null, still, error);
    }

    private static (StillInfo? Still, string? Error) ReadStill(string relPath, ICardReader reader)
    {
        try
        {
            using Stream s = reader.OpenRandom(relPath);
            return (StillProbe.Read(s), null);
        }
#pragma warning disable CA1031 // a probe failure becomes ProbeError, not an exception (Ref §4.2)
        catch (Exception ex) when (ex is not OperationCanceledException and not UnsafeIoException)
#pragma warning restore CA1031
        {
            return (null, Describe(ex));
        }
    }

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";
}
```

`tests/UasSort.Testing/MemoryCardReader.cs`:

```csharp
namespace UasSort.Testing;

/// <summary>
/// In-memory <see cref="ICardReader"/> for probe, harvester and thumbnail tests: read-only streams over byte arrays,
/// with open counting, live-handle tracking and per-path open failures. Paths compare case-insensitively.
/// </summary>
public sealed class MemoryCardReader(CardIdentity identity) : ICardReader
{
    public static readonly DateTime DefaultMtimeUtc = new(2026, 9, 27, 18, 8, 0, DateTimeKind.Utc);

    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failing = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TrackedStream> _streams = [];
    private readonly Lock _lock = new();

    public CardIdentity Identity { get; set; } = identity;

    /// <summary>Every OpenRandom/OpenSequential call, in order.</summary>
    public IReadOnlyList<string> OpenLog { get { lock (_lock) return [.. _streams.Select(s => s.Path)]; } }

    /// <summary>Streams opened and not yet disposed.</summary>
    public int OpenHandles { get { lock (_lock) return _streams.Count(s => !s.IsDisposed); } }

    public MemoryCardReader Add(string relPath, byte[] content)
    {
        lock (_lock) _files[relPath] = content;
        return this;
    }

    /// <summary>Makes every later open of <paramref name="relPath"/> throw <see cref="IOException"/>.</summary>
    public MemoryCardReader FailOpen(string relPath)
    {
        lock (_lock) _failing.Add(relPath);
        return this;
    }

    public CardIdentity CurrentIdentity() => Identity;
    public Stream OpenRandom(string cardRelPath) => Open(cardRelPath);
    public Stream OpenSequential(string cardRelPath) => Open(cardRelPath);

    public FsEntry Stat(string cardRelPath)
    {
        lock (_lock)
        {
            if (!_files.TryGetValue(cardRelPath, out byte[]? content)) throw new FileNotFoundException("Not on the fake card.", cardRelPath);
            return Entry(cardRelPath, content.Length);
        }
    }

    public ListingResult Relist()
    {
        lock (_lock) return new ListingResult([.. _files.Select(kv => Entry(kv.Key, kv.Value.Length))], []);
    }

    public CardSpace Space() => new(FreeBytes: 32_000_000_000, TotalBytes: 128_000_000_000, ClusterBytes: 131_072);

    private static FsEntry Entry(string relPath, long size)
    {
        string native = relPath.Replace('/', '\\');      // FsEntry.RelPath is native (Part 02)
        return new FsEntry(@"E:\" + native, native, false, size, DefaultMtimeUtc, DefaultMtimeUtc, DefaultMtimeUtc, 0x20);
    }

    private TrackedStream Open(string relPath)
    {
        lock (_lock)
        {
            if (_failing.Contains(relPath)) throw new IOException($"Simulated read error: {relPath}");
            if (!_files.TryGetValue(relPath, out byte[]? content)) throw new FileNotFoundException("Not on the fake card.", relPath);
            var stream = new TrackedStream(relPath, content);
            _streams.Add(stream);
            return stream;
        }
    }

    private sealed class TrackedStream(string path, byte[] content) : MemoryStream(content, writable: false)
    {
        public string Path { get; } = path;
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*MetadataHarvesterTests*"
dotnet test --solution uas-sort.slnx
```

Expected: 11 tests pass; the full suite passes with no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Media/MetadataHarvesterTests.cs src/UasSort.Core/Media/DroneStampParser.cs src/UasSort.Core/Media/MetadataHarvester.cs tests/UasSort.Testing/MemoryCardReader.cs
git commit -F- <<'EOF'
feat: add MetadataHarvester, DroneStampParser and MemoryCardReader

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

### Task 03.13: ThumbnailReader

`IThumbnailSource` over the card (Ref §4.2, §4.1): bytes, not images — the MP4 `tnal` range, the DNG IFD0 range, a set's first frame. One card read at a time (a `SemaphoreSlim`); the last card handle stays open for the next request; `Pause()` closes it and makes `GetAsync` return empty until every pause token is disposed (Commit §10.3 and Card cleanup §10.6 hold one); read errors and unknown items give empty bytes (the UI shows a placeholder). Constructed per scan from the harvested items: `new ThumbnailReader(reader, scan.Raw)` (in the App, Part 10's `ShellDeps.CreateReview` builds it from `PlanBase.Scan.Raw` and Part 11's `CardThumbnails` facade swaps it in, decision 34).

**Files:**
- Create: `src/UasSort.Core/Media/ThumbnailReader.cs`
- Test: `tests/UasSort.Core.Tests/Media/ThumbnailReaderTests.cs`

**Interfaces:**

Consumes:

```csharp
// Part 02 (namespace UasSort.Core)
public interface IThumbnailSource { ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct); IDisposable Pause(); }
// Task 03.12: MetadataHarvester.Harvest/FirstFrame, MemoryCardReader (tests)
```

Produces:

```csharp
namespace UasSort.Core.Media;
public readonly record struct ThumbLocation(string CardRelPath, ByteRange Range);            // (defined here)
public sealed class ThumbnailReader : IThumbnailSource, IDisposable   // Ref §4.2
{
    public ThumbnailReader(ICardReader reader, IEnumerable<RawItem> items);
    public static ThumbLocation? Locate(RawItem item);
    public ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct);
    public IDisposable Pause();
    public void Dispose();
}
```

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/Media/ThumbnailReaderTests.cs`:

```csharp
using UasSort.Core.Media;
using UasSort.Testing;

namespace UasSort.Core.Tests.Media;

public sealed class ThumbnailReaderTests
{
    private static (ThumbnailReader Thumbs, MemoryCardReader Reader, List<RawItem> Items) Setup()
    {
        (CardInventory inventory, MemoryCardReader reader) = MetadataHarvesterTests.Card();
        List<RawItem> items = [.. inventory.Units.Select(u => MetadataHarvester.Harvest(u, reader))];
        return (new ThumbnailReader(reader, items), reader, items);
    }

    private static ItemId Id(List<RawItem> items, ItemKind kind) => items.Single(i => i.Kind == kind).Unit.Id;

    [Theory]
    [InlineData(ItemKind.Video)]
    [InlineData(ItemKind.Photo)]
    [InlineData(ItemKind.Set)]
    public async Task GetAsync_ReturnsTheStoredJpeg(ItemKind kind)
    {
        (ThumbnailReader thumbs, _, List<RawItem> items) = Setup();
        using (thumbs)
        {
            ReadOnlyMemory<byte> jpeg = await thumbs.GetAsync(Id(items, kind), TestContext.Current.CancellationToken);

            Assert.Equal(SyntheticMp4Builder.TinyJpeg, jpeg.ToArray());
        }
    }

    [Fact]
    public async Task GetAsync_SetReadsItsFirstFrame()
    {
        (ThumbnailReader thumbs, MemoryCardReader reader, List<RawItem> items) = Setup();
        using (thumbs)
        {
            await thumbs.GetAsync(Id(items, ItemKind.Set), TestContext.Current.CancellationToken);

            Assert.EndsWith("PANO_0001.DNG", reader.OpenLog[^1]);
        }
    }

    [Fact]
    public async Task GetAsync_UnknownItem_IsEmpty()
    {
        (ThumbnailReader thumbs, _, _) = Setup();
        using (thumbs)
        {
            Assert.True((await thumbs.GetAsync(new ItemId("DCIM/DJI_001/nope.MP4"), TestContext.Current.CancellationToken)).IsEmpty);
        }
    }

    [Fact]
    public async Task GetAsync_SameFileTwice_OpensItOnce()
    {
        (ThumbnailReader thumbs, MemoryCardReader reader, List<RawItem> items) = Setup();
        using (thumbs)
        {
            int before = reader.OpenLog.Count;

            await thumbs.GetAsync(Id(items, ItemKind.Video), TestContext.Current.CancellationToken);
            await thumbs.GetAsync(Id(items, ItemKind.Video), TestContext.Current.CancellationToken);

            Assert.Equal(before + 1, reader.OpenLog.Count);
            Assert.Equal(1, reader.OpenHandles);
        }
    }

    [Fact]
    public async Task Pause_ClosesTheHandleAndReturnsEmptyUntilEveryPauseIsDisposed()
    {
        (ThumbnailReader thumbs, MemoryCardReader reader, List<RawItem> items) = Setup();
        using (thumbs)
        {
            ItemId video = Id(items, ItemKind.Video);
            await thumbs.GetAsync(video, TestContext.Current.CancellationToken);

            IDisposable first = thumbs.Pause();
            IDisposable second = thumbs.Pause();
            Assert.Equal(0, reader.OpenHandles);
            int opens = reader.OpenLog.Count;

            Assert.True((await thumbs.GetAsync(video, TestContext.Current.CancellationToken)).IsEmpty);
            first.Dispose();
            first.Dispose();
            Assert.True((await thumbs.GetAsync(video, TestContext.Current.CancellationToken)).IsEmpty);
            Assert.Equal(opens, reader.OpenLog.Count);

            second.Dispose();
            Assert.Equal(SyntheticMp4Builder.TinyJpeg, (await thumbs.GetAsync(video, TestContext.Current.CancellationToken)).ToArray());
        }
    }

    [Fact]
    public async Task GetAsync_ReadError_IsEmpty()
    {
        (ThumbnailReader thumbs, MemoryCardReader reader, List<RawItem> items) = Setup();
        using (thumbs)
        {
            reader.FailOpen("DCIM/DJI_001/DJI_20260927140627_0128_D.MP4");

            Assert.True((await thumbs.GetAsync(Id(items, ItemKind.Video), TestContext.Current.CancellationToken)).IsEmpty);
        }
    }

    [Fact]
    public async Task Dispose_ClosesTheCachedHandle()
    {
        (ThumbnailReader thumbs, MemoryCardReader reader, List<RawItem> items) = Setup();

        await thumbs.GetAsync(Id(items, ItemKind.Photo), TestContext.Current.CancellationToken);
        thumbs.Dispose();

        Assert.Equal(0, reader.OpenHandles);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*ThumbnailReaderTests*"
```

Expected: the build fails with CS0246 (`The type or namespace name 'ThumbnailReader' could not be found`).

- [ ] **Step 3: Implement**

`src/UasSort.Core/Media/ThumbnailReader.cs`:

```csharp
namespace UasSort.Core.Media;

/// <summary>Where an item's stored thumbnail lives on the card.</summary>
public readonly record struct ThumbLocation(string CardRelPath, ByteRange Range);

/// <summary>
/// <see cref="IThumbnailSource"/> over the card (Ref §4.2): returns the stored JPEG bytes (MP4 <c>tnal</c> 160×90, DNG IFD0
/// 160×120, a set's first frame). One card read at a time; the last card handle stays open for the next request.
/// <see cref="Pause"/> closes it and makes <see cref="GetAsync"/> return empty until every pause is disposed.
/// </summary>
public sealed class ThumbnailReader : IThumbnailSource, IDisposable
{
    private readonly ICardReader _reader;
    private readonly ImmutableDictionary<ItemId, ThumbLocation> _locations;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _openPath;
    private Stream? _open;
    private int _pauses;

    public ThumbnailReader(ICardReader reader, IEnumerable<RawItem> items)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(items);
        _reader = reader;
        var builder = ImmutableDictionary.CreateBuilder<ItemId, ThumbLocation>();
        foreach (RawItem item in items)
            if (Locate(item) is { } location) builder[item.Unit.Id] = location;
        _locations = builder.ToImmutable();
    }

    /// <summary>The stored thumbnail of a harvested item, or null when it has none (a placeholder shows).</summary>
    public static ThumbLocation? Locate(RawItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Unit switch
        {
            VideoUnit v => item.Mp4?.Thumb is { } r ? new ThumbLocation(v.Mp4.RelPath, r) : null,
            PhotoUnit p => item.Still?.Thumb is { } r ? new ThumbLocation(p.Primary.RelPath, r) : null,
            SetUnit s => item.Still?.Thumb is { } r && !s.Members.IsDefaultOrEmpty
                ? new ThumbLocation(MetadataHarvester.FirstFrame(s).RelPath, r) : null,
        };
    }

    public async ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct)
    {
        if (!_locations.TryGetValue(id, out ThumbLocation location)) return ReadOnlyMemory<byte>.Empty;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_pauses > 0) return ReadOnlyMemory<byte>.Empty;
            Stream s = Handle(location.CardRelPath);
            if (location.Range.Offset + location.Range.Length > s.Length) return ReadOnlyMemory<byte>.Empty;
            var buffer = new byte[location.Range.Length];
            s.Position = location.Range.Offset;
            await s.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
            return buffer;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CloseHandle();
            return ReadOnlyMemory<byte>.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IDisposable Pause()
    {
        _gate.Wait();
        try
        {
            _pauses++;
            CloseHandle();
        }
        finally
        {
            _gate.Release();
        }
        return new PauseToken(this);
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            CloseHandle();
        }
        finally
        {
            _gate.Release();
        }
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private Stream Handle(string cardRelPath)
    {
        if (_open is not null && _openPath == cardRelPath) return _open;
        CloseHandle();
        _open = _reader.OpenRandom(cardRelPath);
        _openPath = cardRelPath;
        return _open;
    }

    private void CloseHandle()
    {
        _open?.Dispose();
        _open = null;
        _openPath = null;
    }

    private void EndPause()
    {
        _gate.Wait();
        try
        {
            _pauses--;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class PauseToken(ThumbnailReader owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.EndPause();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*ThumbnailReaderTests*"
dotnet test --solution uas-sort.slnx
```

Expected: 9 tests pass; the full suite passes with no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Media/ThumbnailReaderTests.cs src/UasSort.Core/Media/ThumbnailReader.cs
git commit -F- <<'EOF'
feat: add ThumbnailReader with pause support

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

### Task 03.14: Golden real-file checks (UASSORT_GOLDEN)

Optional real-file checks (Ref §13): skipped unless `UASSORT_GOLDEN` names a folder of **copies** from a card. `GoldenFolder` refuses volume roots (a real card), anything inside a OneDrive, "UAS Videos" or "Picture Offload" folder, the Pictures folder (the default library roots), the OneDrive roots from the environment and `%LOCALAPPDATA%\uas-sort`, and throws (not skips) on a refused folder. Expected values: Zachar 0128 → 57.5504420579265, −153.738972972093 (tolerance 1e-12), altitude 298.394 m, `mvhd` 2026-09-27T18:06:27Z, protocol `dvtm_Air3s.proto`, a `tnal` range starting FF D8; PANO_0001 → DTO 2026-05-25 09:30:28 and 57.799648, −152.390180 (tolerance 1e-6). The files are opened read-only with `File.OpenRead` in the test project (tests are outside the BannedSymbols scope, Part 01).

**Files:**
- Create: `tests/UasSort.Testing/GoldenFolder.cs`
- Test: `tests/UasSort.Core.Tests/Media/GoldenMediaTests.cs`

**Interfaces:**

Consumes:

```csharp
// Tasks 03.9–03.11: Mp4Probe.Read, StillProbe.Read; ProbeAssert (tests)
```

Produces:

```csharp
namespace UasSort.Testing;
public static partial class GoldenFolder                          // (defined here)
{
    public const string Variable = "UASSORT_GOLDEN";
    public static string? Refusal(string fullPath, IReadOnlyList<string> forbiddenRoots);
    public static IReadOnlyList<string> DefaultForbiddenRoots();
    public static string? File(string name);                      // null = variable unset or file absent; throws when refused
}
```

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/Media/GoldenMediaTests.cs`:

```csharp
#pragma warning disable RS0030 // Golden checks only: read-only opens of COPIES under UASSORT_GOLDEN (Ref §13)
using UasSort.Core.Media;
using UasSort.Testing;
using static UasSort.Core.Tests.Media.ProbeAssert;

namespace UasSort.Core.Tests.Media;

public sealed class GoldenMediaTests
{
    [Theory]
    [InlineData(@"E:\")]
    [InlineData(@"E:")]
    [InlineData(@"\\nas\share\")]
    [InlineData(@"C:\Users\pilot\OneDrive\Pictures\UAS Videos\2026")]
    [InlineData(@"C:\Users\pilot\OneDrive - Contoso\golden")]
    [InlineData(@"D:\Picture Offload\001_0087")]
    [InlineData(@"C:\Users\pilot\Pictures\golden")]
    public void GoldenFolder_RefusesUserDataLocations(string path)
        => Assert.NotNull(GoldenFolder.Refusal(path, [@"C:\Users\pilot\Pictures"]));

    [Theory]
    [InlineData(@"C:\Temp\uas-golden")]
    [InlineData(@"C:\Users\pilot\Pictures2\golden")]
    public void GoldenFolder_AllowsAFolderOfCopies(string path)
        => Assert.Null(GoldenFolder.Refusal(path, [@"C:\Users\pilot\Pictures"]));

    [Fact]
    public void Golden_Zachar0128_MatchesExiftool()
    {
        string? path = GoldenFolder.File("DJI_20260927140627_0128_D.MP4");
        if (path is null)
        {
            Assert.Skip("UASSORT_GOLDEN is not set or holds no DJI_20260927140627_0128_D.MP4");
            return;
        }
        using Stream s = File.OpenRead(path);

        Mp4Info info = Mp4Probe.Read(s);

        GpsFix fix = Fix(info.First);
        Assert.Equal(57.5504420579265, fix.Point.Lat, 1e-12);
        Assert.Equal(-153.738972972093, fix.Point.Lon, 1e-12);
        Assert.Equal(298.394, fix.AltM!.Value, 1e-6);
        Assert.Equal(0, fix.Sample);
        Assert.Equal(GpsSource.DjmdModelTable, fix.Source);
        Assert.Equal(new DateTime(2026, 9, 27, 18, 6, 27, DateTimeKind.Utc), info.MvhdUtc);
        Assert.Equal("dvtm_Air3s.proto", info.Protocol);
        Assert.True(info.Duration > TimeSpan.Zero);
        ByteRange thumb = info.Thumb!.Value;
        var soi = new byte[2];
        s.Position = thumb.Offset;
        s.ReadExactly(soi);
        Assert.Equal([0xFF, 0xD8], soi);
    }

    [Fact]
    public void Golden_Pano0001_DtoAndGps()
    {
        string? path = GoldenFolder.File("PANO_0001.DNG");
        if (path is null)
        {
            Assert.Skip("UASSORT_GOLDEN is not set or holds no PANO_0001.DNG");
            return;
        }
        using Stream s = File.OpenRead(path);

        StillInfo info = StillProbe.Read(s);

        Assert.Equal(new DateTime(2026, 5, 25, 9, 30, 28), info.DtoNaive);
        GpsFix fix = Fix(info.Gps);
        Assert.Equal(57.799648, fix.Point.Lat, 1e-6);
        Assert.Equal(-152.390180, fix.Point.Lon, 1e-6);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*GoldenMediaTests*"
```

Expected: the build fails with CS0103 (`The name 'GoldenFolder' does not exist in the current context`).

- [ ] **Step 3: Implement**

Run the golden checks by hand, pointing at a folder that holds copies of `DJI_20260927140627_0128_D.MP4` and `PANO_0001.DNG` (never the card or the library): `$env:UASSORT_GOLDEN = "C:\Temp\uas-golden"` before `dotnet test`.

`tests/UasSort.Testing/GoldenFolder.cs`:

```csharp
using System.Text.RegularExpressions;

namespace UasSort.Testing;

/// <summary>
/// The optional real-file checks (Ref §13): <c>UASSORT_GOLDEN</c> names a folder of COPIES from a card. The folder is
/// refused if it could be user data: a volume root (a real card), anything inside a OneDrive, "UAS Videos" or
/// "Picture Offload" folder, the Pictures folder (the default library roots), the OneDrive roots, or
/// %LOCALAPPDATA%\uas-sort.
/// </summary>
public static partial class GoldenFolder
{
    public const string Variable = "UASSORT_GOLDEN";

    private static readonly string[] ForbiddenSegments = ["OneDrive", "UAS Videos", "Picture Offload"];

    /// <summary>Why <paramref name="fullPath"/> is refused, or null when it may be read.</summary>
    public static string? Refusal(string fullPath, IReadOnlyList<string> forbiddenRoots)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        ArgumentNullException.ThrowIfNull(forbiddenRoots);
        string p = fullPath.Replace('/', '\\').TrimEnd('\\');
        if (VolumeRoot().IsMatch(p + "\\")) return "a volume root (a card?); point it at a folder of copies";
        foreach (string segment in p.Split('\\'))
        {
            foreach (string bad in ForbiddenSegments)
            {
                if (segment.Equals(bad, StringComparison.OrdinalIgnoreCase)
                    || segment.StartsWith(bad + " - ", StringComparison.OrdinalIgnoreCase))
                    return $"inside a '{bad}' folder";
            }
        }
        foreach (string root in forbiddenRoots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            string r = root.Replace('/', '\\').TrimEnd('\\');
            if (p.Equals(r, StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase))
                return $"inside {r}";
        }
        return null;
    }

    /// <summary>The Pictures folder, the OneDrive roots from the environment, and %LOCALAPPDATA%\uas-sort.</summary>
    public static IReadOnlyList<string> DefaultForbiddenRoots() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Environment.GetEnvironmentVariable("OneDrive") ?? "",
        Environment.GetEnvironmentVariable("OneDriveConsumer") ?? "",
        Environment.GetEnvironmentVariable("OneDriveCommercial") ?? "",
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uas-sort"),
    ];

    /// <summary>
    /// Full path of <paramref name="name"/> in the golden folder; null when the variable is unset or the file is absent.
    /// Throws when the folder is refused, so a misconfigured run fails loudly instead of reading user data.
    /// </summary>
    public static string? File(string name)
    {
        string? dir = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(dir)) return null;
        string full = Path.GetFullPath(dir);
        if (Refusal(full, DefaultForbiddenRoots()) is { } why)
            throw new InvalidOperationException($"{Variable} refused ({why}): {full}");
        string path = Path.Join(full, name);
#pragma warning disable RS0030 // Golden checks only: existence test of a copy under UASSORT_GOLDEN (Ref §13)
        return System.IO.File.Exists(path) ? path : null;
#pragma warning restore RS0030
    }

    [GeneratedRegex(@"^([A-Za-z]:\\|\\\\[^\\]+\\[^\\]+\\)$")]
    private static partial Regex VolumeRoot();
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*GoldenMediaTests*"
dotnet test --solution uas-sort.slnx
```

Expected: 9 tests pass and the 2 golden tests are skipped (with `UASSORT_GOLDEN` set to a folder of copies: 11 pass); the full suite passes with no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Media/GoldenMediaTests.cs tests/UasSort.Testing/GoldenFolder.cs
git commit -F- <<'EOF'
test: add UASSORT_GOLDEN real-file checks for Mp4Probe and StillProbe

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

## Part 03 — Produces (summary)

```csharp
// Namespaces per 00-interfaces.md; every file relies on Part 02's GlobalUsings.Core.cs (no GlobalUsings.cs created here).
// src/UasSort.Core/Media  (namespace UasSort.Core.Media)
public sealed class BlockCache { const int DefaultBlockSize = 4096; BlockCache(Stream stream, int blockSize = 4096);
                                 long Length; int BlockReads; byte[] ReadAt(long offset, int count); }
public readonly record struct Mp4Box(string Type, long Offset, long Size, int HeaderSize, bool Truncated) { long Body; long End; }
public static class Mp4Boxes { IEnumerable<Mp4Box> Walk(BlockCache c, long start, long end); Mp4Box? Child(BlockCache c, Mp4Box parent, string type); }
public sealed record PbField(int Number, int WireType, ulong Value, ReadOnlyMemory<byte> Raw, IReadOnlyList<PbField>? Message);
public static class Protobuf { const int MaxDepth = 12; bool TryReadVarint(ReadOnlySpan<byte>, ref int, out ulong);
    IReadOnlyList<PbField>? Parse(ReadOnlyMemory<byte>); IReadOnlyList<PbField>? DecodeTree(ReadOnlyMemory<byte>);
    PbField? GetPath(IReadOnlyList<PbField>?, ReadOnlySpan<int>); string? FindProtocol(IReadOnlyList<PbField>?); bool LooksLikeText(ReadOnlySpan<byte>); }
public sealed record DjmdGpsPath(ImmutableArray<int> Path, bool AlwaysDegrees) { string FieldPath; }
public sealed record DjmdReading(string? Protocol, GeoPoint? Point, double? AltM, string? FieldPath, bool Generic, ulong? UptimeUs, string? DroneSerial);
public static class DjmdDecoder { ImmutableDictionary<string, DjmdGpsPath> ModelTable; bool IsFix(GeoPoint);
    DjmdReading? Decode(ReadOnlyMemory<byte> sample, string? knownProtocol); GeoPoint? DecodeAt(ReadOnlyMemory<byte> sample, string fieldPath); }
public readonly record struct SampleLocation(long Offset, int Size, int DescriptionIndex);
public sealed class Mp4SampleTable { ImmutableArray<string> Formats; int SampleCount; int DjmdDescription;
    static ImmutableArray<string> ReadFormats(BlockCache, Mp4Box stsd); static Mp4SampleTable? Open(BlockCache, Mp4Box stbl); SampleLocation? Locate(int k); }
public static class Mp4Thumb { ByteRange? Find(BlockCache cache, Mp4Box moov); }
public static class Mp4MdatHead { const long FixedOffset = 512; const int HeadBytes = 4096; DjmdReading? TryRead(BlockCache cache, Mp4Box? mdat); }
public static class Mp4Probe { const int MaxSampleBytes = 1 << 20; Mp4Info Read(Stream s); IReadOnlyList<int> ProbeIndices(int sampleCount); }
public static class StillProbe { StillInfo Read(Stream s); }
public static partial class DroneStampParser { DateTime? FromFileName(string nameOrRelPath); string FileName(string nameOrRelPath); }
public static class MetadataHarvester { IAsyncEnumerable<RawItem> HarvestAsync(CardInventory, ICardReader, IProgress<ScanProgress>, CancellationToken);
                                        RawItem Harvest(MediaUnit unit, ICardReader reader); CardEntry FirstFrame(SetUnit set); }
public readonly record struct ThumbLocation(string CardRelPath, ByteRange Range);
public sealed class ThumbnailReader : IThumbnailSource, IDisposable { ThumbnailReader(ICardReader reader, IEnumerable<RawItem> items);
    static ThumbLocation? Locate(RawItem item); ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId, CancellationToken); IDisposable Pause(); void Dispose(); }

// tests/UasSort.Testing  (namespace UasSort.Testing)
public sealed class CountingStream(Stream inner) : Stream { IReadOnlyList<(long Offset, int Count)> Reads; int ReadCalls; int ReadsWithin(long start, long end); }
public sealed record SyntheticMp4(byte[] Bytes, long MdatPayloadStart, long MdatPayloadEnd, long? MoovOffset, ImmutableArray<long> SampleOffsets, ByteRange? Thumb);
public sealed record class SyntheticMp4Builder { /* init properties, Task 03.4 */ WithMvhdUtc(DateTime); WithDuration(TimeSpan);
    WithDjmdGps(string protocol, GeoPoint first); WithThumbnail(byte[]); byte[] Build(); SyntheticMp4 BuildFile(); byte[] BuildSample(int k);
    static ZacharCreationUtc; static Zachar0128; static ImmutableArray<byte> TinyJpeg; }
public sealed record SyntheticDng(byte[] Bytes, ByteRange? Thumb);
public sealed record class SyntheticDngBuilder { /* init properties, Task 03.11 */ WithDateTimeOriginal(DateTime); WithGps(GeoPoint);
    WithModel(string); WithThumbnail(byte[]); byte[] Build(); SyntheticDng BuildFile(); static Pano0001Dto; static Pano0001Gps; }
public sealed class MemoryCardReader(CardIdentity identity) : ICardReader { Identity; OpenLog; OpenHandles; Add(string, byte[]); FailOpen(string); static DefaultMtimeUtc; }
public static partial class GoldenFolder { const string Variable = "UASSORT_GOLDEN"; string? Refusal(string, IReadOnlyList<string>);
    IReadOnlyList<string> DefaultForbiddenRoots(); string? File(string name); }

// tests/UasSort.Core.Tests/Media: ProbeAssert (internal) + 14 test classes, 143 tests (2 golden tests skip without UASSORT_GOLDEN)
```

**Notes for later parts.**
- Part 04: `GpsPlausibility` gates fixes whose `Source` is `DjmdGenericSearch` using `Mp4Info.LastSameField`; `DroneClock` samples use `Mp4Info.MvhdUtc` and `RawItem.DroneStamp` (the file-name stamp for videos).
- Part 06: call `MetadataHarvester.HarvestAsync(inventory, reader, progress, ct)` statically (no `new MetadataHarvester()`).
- Parts 10–11: the per-scan `ThumbnailReader` (`new ThumbnailReader(reader, scan.Raw)`, built in Part 10's `ShellDeps.CreateReview` from `PlanBase.Scan.Raw`) sits behind Part 11's `CardThumbnails : IThumbnailSource` facade, which swaps it per scan and is the one `IThumbnailSource` given to `ReviewServices`, `CommitEnvironment` and `CleanupEnvironment` (decision 34); Commit and Card cleanup hold `Pause()` for their whole run.
- Part 11: Part 11 extends Part 01's `tools/fixtures/make-selftest-assets.cs` (adds `selftest.dng`, `ledger-v1.jsonl` and the MP4s `selftest-0001.mp4`, `selftest-0002.mp4`, `selftest-0003.mp4`); this part never creates that file. The extension uses the fluent `SyntheticMp4Builder`/`SyntheticDngBuilder` API exactly as its contract lists; `selftest.dng` probes with `StillProbe.Read`.
- Card cleanup (Part 08) shows `Mp4Info.Duration`; null means no `moov` ("unfinished").
