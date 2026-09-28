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
