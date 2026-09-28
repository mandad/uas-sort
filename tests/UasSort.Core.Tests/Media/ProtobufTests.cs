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
