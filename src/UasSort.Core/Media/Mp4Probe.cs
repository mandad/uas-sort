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
