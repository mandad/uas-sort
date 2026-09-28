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
