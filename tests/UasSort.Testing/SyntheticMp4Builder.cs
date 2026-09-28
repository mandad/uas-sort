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
