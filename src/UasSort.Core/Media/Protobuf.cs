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
