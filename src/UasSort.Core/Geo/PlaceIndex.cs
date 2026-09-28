// src/UasSort.Core/Geo/PlaceIndex.cs
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace UasSort.Core.Geo;

/// <summary>The offline GeoNames extract with a 0.1° grid built at load time (Ref §4.2, §8.7).</summary>
public sealed class PlaceIndex : IPlaceIndex
{
    const int LonCells = 3600;
    const int MinRecordBytes = 17;   // u8 + i32 + i32 + u8 + u8 + u32 + u16

    readonly int[] _lat, _lon;
    readonly byte[] _class, _code;
    readonly uint[] _population;
    readonly ushort[] _tz;
    readonly int[] _nameStart;
    readonly byte[] _names;
    readonly string[] _zones;
    readonly int[] _order;
    readonly Dictionary<int, (int Start, int Count)> _cells = [];

    PlaceIndex(int[] lat, int[] lon, byte[] cls, byte[] code, uint[] population, ushort[] tz, int[] nameStart, byte[] names, string[] zones)
    {
        (_lat, _lon, _class, _code, _population, _tz, _nameStart, _names, _zones) = (lat, lon, cls, code, population, tz, nameStart, names, zones);
        var keys = new int[lat.Length];
        _order = new int[lat.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = Key(LatCell(lat[i] / 1e6), LonCell(lon[i] / 1e6));
            _order[i] = i;
        }
        Array.Sort(keys, _order);
        for (var k = 0; k < keys.Length;)
        {
            var start = k;
            while (k < keys.Length && keys[k] == keys[start]) k++;
            _cells[keys[start]] = (start, k - start);
        }
    }

    public int Count => _lat.Length;

    public static PlaceIndex Load(Stream gz)
    {
        ArgumentNullException.ThrowIfNull(gz);
        using var raw = new MemoryStream();
        using (var z = new GZipStream(gz, CompressionMode.Decompress, leaveOpen: true))
            z.CopyTo(raw);   // a corrupt gzip stream throws InvalidDataException itself
        return Parse(raw.GetBuffer().AsSpan(0, checked((int)raw.Length)));
    }

    public static Task<PlaceIndex> LoadAsync(IAppAssets assets, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        return Task.Run(() =>
        {
            using var s = assets.OpenPlaces();
            return Load(s);
        }, ct);
    }

    public IReadOnlyList<PlaceHit> Near(GeoPoint p, Distance r, PlaceClass cls, int max)
    {
        if (max <= 0 || Count == 0) return [];
        var dLat = r.Meters / GeoMath.MetersPerDegree;
        var latLo = Math.Clamp((int)Math.Floor((p.Lat - dLat) * 10), -900, 899);
        var latHi = Math.Clamp((int)Math.Floor((p.Lat + dLat) * 10), -900, 899);
        var edge = Math.Min(89.9, Math.Abs(p.Lat) + dLat);
        var dLon = dLat / Math.Cos(edge * Math.PI / 180);
        int lonLo, lonHi;
        if (dLon >= 180) { lonLo = -1800; lonHi = 1799; }
        else { lonLo = (int)Math.Floor((p.Lon - dLon) * 10); lonHi = (int)Math.Floor((p.Lon + dLon) * 10); }

        var visited = new HashSet<int>();
        var hits = new List<(double Meters, int Index)>();
        for (var la = latLo; la <= latHi; la++)
            for (var lo = lonLo; lo <= lonHi; lo++)
            {
                var key = Key(la, Wrap(lo));
                if (!visited.Add(key) || !_cells.TryGetValue(key, out var cell)) continue;
                for (var k = cell.Start; k < cell.Start + cell.Count; k++)
                {
                    var i = _order[k];
                    if (_class[i] != (byte)cls) continue;
                    var meters = GeoMath.Haversine(p, PointOf(i)).Meters;
                    if (meters <= r.Meters) hits.Add((meters, i));
                }
            }
        hits.Sort((a, b) => a.Meters != b.Meters ? a.Meters.CompareTo(b.Meters) : string.CompareOrdinal(NameOf(a.Index), NameOf(b.Index)));
        return hits.Take(max).Select(h => Hit(h.Index, h.Meters)).ToList();
    }

    PlaceHit Hit(int i, double meters) => new(
        NameOf(i), PointOf(i), (PlaceClass)_class[i],
        _code[i] == PlacesFormat.PopulatedCode ? "P" : PlacesFormat.FeatureCodes[_code[i]],
        (int)Math.Min(_population[i], int.MaxValue), _zones[_tz[i]], new Distance(meters));

    GeoPoint PointOf(int i) => new(_lat[i] / 1e6, _lon[i] / 1e6);
    string NameOf(int i) => Encoding.UTF8.GetString(_names, _nameStart[i], _nameStart[i + 1] - _nameStart[i]);

    static int LatCell(double lat) => Math.Clamp((int)Math.Floor(lat * 10), -900, 899);
    static int LonCell(double lon) => Wrap((int)Math.Floor(lon * 10));
    static int Wrap(int lonCell) => ((lonCell + 1800) % LonCells + LonCells) % LonCells - 1800;
    static int Key(int latCell, int lonCell) => (latCell + 900) * LonCells + (lonCell + 1800);

    static PlaceIndex Parse(ReadOnlySpan<byte> data)
    {
        var c = new Cursor(data);
        if (!c.Take(4).SequenceEqual(PlacesFormat.Magic)) throw new InvalidDataException("places.bin: bad magic.");
        if (c.U16() != PlacesFormat.Version) throw new InvalidDataException("places.bin: unsupported version.");
        var count = c.U32();
        if (count > (uint)(data.Length / MinRecordBytes)) throw new InvalidDataException("places.bin: record count exceeds the data.");
        var n = (int)count;
        var zones = new string[c.U16()];
        for (var z = 0; z < zones.Length; z++) zones[z] = Encoding.UTF8.GetString(c.Take(c.U8()));

        var lat = new int[n];
        var lon = new int[n];
        var cls = new byte[n];
        var code = new byte[n];
        var population = new uint[n];
        var tz = new ushort[n];
        var nameStart = new int[n + 1];
        using var names = new MemoryStream();
        for (var i = 0; i < n; i++)
        {
            nameStart[i] = (int)names.Length;
            names.Write(c.Take(c.U8()));
            lat[i] = c.I32();
            lon[i] = c.I32();
            cls[i] = c.U8();
            code[i] = c.U8();
            population[i] = c.U32();
            tz[i] = c.U16();
            var codeOk = cls[i] == (byte)PlaceClass.Populated ? code[i] == PlacesFormat.PopulatedCode : code[i] < PlacesFormat.FeatureCodes.Length;
            if (cls[i] > (byte)PlaceClass.Feature || !codeOk || tz[i] >= zones.Length)
                throw new InvalidDataException("places.bin: invalid record.");
        }
        nameStart[n] = (int)names.Length;
        if (!c.AtEnd) throw new InvalidDataException("places.bin: trailing bytes.");
        return new PlaceIndex(lat, lon, cls, code, population, tz, nameStart, names.ToArray(), zones);
    }

    ref struct Cursor
    {
        readonly ReadOnlySpan<byte> _data;
        int _pos;

        public Cursor(ReadOnlySpan<byte> data) => _data = data;

        public readonly bool AtEnd => _pos == _data.Length;

        public ReadOnlySpan<byte> Take(int length)
        {
            if (length < 0 || _pos + length > _data.Length) throw new InvalidDataException("places.bin: truncated.");
            var s = _data.Slice(_pos, length);
            _pos += length;
            return s;
        }

        public byte U8() => Take(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    }
}
