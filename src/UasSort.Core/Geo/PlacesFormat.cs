// src/UasSort.Core/Geo/PlacesFormat.cs
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace UasSort.Core.Geo;

public sealed record PlaceRecord(long GeonameId, string Name, double Lat, double Lon, PlaceClass Class, string? FeatureCode,
                                 long Population, string TzId);

/// <summary>The places.bin.gz format (Ref §8.7) and the GeoNames dump filter used by tools/places/build-places.cs.</summary>
public static class PlacesFormat
{
    public static ReadOnlySpan<byte> Magic => "UPLC"u8;
    public const ushort Version = 1;
    public const byte PopulatedCode = 255;

    public static readonly ImmutableArray<string> FeatureCodes =
    [
        "MT", "PK", "HLL", "VAL", "PASS", "CAPE", "ISL", "PEN", "PT", "BAY", "LK", "GLCR", "FJD", "COVE", "LGN", "INLT", "SD",
        "STRT", "HBR", "FLLS", "PRK",
    ];

    /// <summary>One line of a GeoNames "geoname" table dump (tab-separated, 19 columns).</summary>
    public static bool TryParseGeoNamesLine(string line, bool populatedOnly, [NotNullWhen(true)] out PlaceRecord? record)
    {
        ArgumentNullException.ThrowIfNull(line);
        record = null;
        var c = line.Split('\t');
        if (c.Length < 18 || c[1].Length == 0 || c[17].Length == 0) return false;
        if (!long.TryParse(c[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return false;
        if (!double.TryParse(c[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(c[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return false;

        PlaceClass cls;
        string? code;
        if (c[6] == "P") { cls = PlaceClass.Populated; code = null; }
        else if (!populatedOnly && FeatureCodes.Contains(c[7])) { cls = PlaceClass.Feature; code = c[7]; }
        else return false;

        _ = long.TryParse(c[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out var population);
        record = new PlaceRecord(id, c[1], lat, lon, cls, code, Math.Max(0, population), c[17]);
        return true;
    }

    /// <summary>US.txt (populated places and the listed features) then cities5000.txt (populated only), deduplicated by geonameid.</summary>
    public static ImmutableArray<PlaceRecord> Build(IEnumerable<string> usLines, IEnumerable<string> citiesLines)
    {
        ArgumentNullException.ThrowIfNull(usLines);
        ArgumentNullException.ThrowIfNull(citiesLines);
        var seen = new HashSet<long>();
        var result = ImmutableArray.CreateBuilder<PlaceRecord>();
        foreach (var line in usLines)
            if (TryParseGeoNamesLine(line, populatedOnly: false, out var r) && seen.Add(r.GeonameId)) result.Add(r);
        foreach (var line in citiesLines)
            if (TryParseGeoNamesLine(line, populatedOnly: true, out var r) && seen.Add(r.GeonameId)) result.Add(r);
        return result.ToImmutable();
    }

    public static void Write(Stream destination, IReadOnlyList<PlaceRecord> records)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(records);
        var zones = new List<string>();
        var zoneIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in records)
            if (zoneIndex.TryAdd(r.TzId, zones.Count)) zones.Add(r.TzId);
        if (zones.Count > ushort.MaxValue) throw new InvalidOperationException("Too many time zones for a u16 index.");

        using var gz = new GZipStream(destination, CompressionLevel.SmallestSize, leaveOpen: true);
        using var w = new BinaryWriter(gz, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write((uint)records.Count);
        w.Write((ushort)zones.Count);
        foreach (var zone in zones) WriteShortString(w, zone);
        foreach (var r in records)
        {
            WriteShortString(w, r.Name);
            w.Write((int)Math.Round(r.Lat * 1e6));
            w.Write((int)Math.Round(r.Lon * 1e6));
            w.Write((byte)r.Class);
            w.Write(r.Class == PlaceClass.Populated ? PopulatedCode : FeatureIndex(r.FeatureCode));
            w.Write((uint)Math.Clamp(r.Population, 0L, uint.MaxValue));
            w.Write((ushort)zoneIndex[r.TzId]);
        }
    }

    static byte FeatureIndex(string? code)
    {
        var i = code is null ? -1 : FeatureCodes.IndexOf(code);
        return i >= 0 ? (byte)i : throw new InvalidOperationException($"Feature code '{code}' is not in the kept list.");
    }

    static void WriteShortString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var length = Math.Min(bytes.Length, 255);
        while (length > 0 && length < bytes.Length && (bytes[length] & 0xC0) == 0x80) length--;   // never split a UTF-8 sequence
        w.Write((byte)length);
        w.Write(bytes, 0, length);
    }
}
