// tests/UasSort.Core.Tests/Geo/PlaceIndexTests.cs
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace UasSort.Core.Tests.Geo;

public sealed class PlaceIndexTests
{
    static string Line(long id, string name, double lat, double lon, string cls, string code, long pop, string tz)
        => string.Join('\t',
            id.ToString(CultureInfo.InvariantCulture), name, name, "",
            lat.ToString("R", CultureInfo.InvariantCulture), lon.ToString("R", CultureInfo.InvariantCulture),
            cls, code, "US", "", "AK", "180", "", "", pop.ToString(CultureInfo.InvariantCulture), "", "10", tz, "2026-09-27");

    static readonly List<string> UsLines =
    [
        Line(5861187, "Anvil Mountain", 64.5639, -165.3703, "T", "MT", 0, "America/Nome"),
        Line(5870133, "Nome", 64.5011, -165.4064, "P", "PPLA2", 3699, "America/Nome"),
        Line(5877811, "Zachar Bay", 57.5500, -153.7450, "P", "PPL", 0, "America/Anchorage"),
        Line(5877812, "Zachar Bay", 57.5600, -153.7000, "H", "BAY", 0, "America/Anchorage"),
        Line(9000001, "Dateline Cape", 51.9, 179.95, "T", "CAPE", 0, "America/Adak"),
        Line(5000001, "Nome-Council Road", 64.69, -164.27, "R", "RD", 0, "America/Nome"),     // class R: not kept
        Line(5000002, "East Fork", 64.6, -165.0, "H", "STM", 0, "America/Nome"),              // stream: not kept
        Line(5000003, "", 64.6, -165.1, "T", "MT", 0, "America/Nome"),                        // no name: not kept
        "not\ta\tgeonames\tline",
    ];

    static readonly List<string> CityLines =
    [
        Line(5870133, "Nome", 64.5011, -165.4064, "P", "PPLA2", 3699, "America/Nome"),        // duplicate id: skipped
        Line(2179537, "Wellington", -41.28664, 174.77557, "P", "PPLC", 381900, "Pacific/Auckland"),
        Line(9000002, "Cities Mountain", 10.0, 10.0, "T", "MT", 0, "Africa/Lagos"),           // cities list: populated only
    ];

    static byte[] BuildBytes(IReadOnlyList<PlaceRecord> records)
    {
        using var ms = new MemoryStream();
        PlacesFormat.Write(ms, records);
        return ms.ToArray();
    }

    static PlaceIndex Index()
    {
        using var ms = new MemoryStream(BuildBytes(PlacesFormat.Build(UsLines, CityLines)));
        return PlaceIndex.Load(ms);
    }

    static byte[] Gunzip(byte[] gz)
    {
        using var input = new MemoryStream(gz);
        using var z = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    static byte[] Gzip(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var z = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw);
        return output.ToArray();
    }

    [Fact]
    public void Places_Build_KeepsPopulatedAndListedFeatures_DedupesById()
    {
        var records = PlacesFormat.Build(UsLines, CityLines);
        string[] expected = ["Anvil Mountain", "Nome", "Zachar Bay", "Zachar Bay", "Dateline Cape", "Wellington"];
        Assert.Equal(expected, records.Select(r => r.Name).ToArray());
        Assert.Equal("MT", records[0].FeatureCode);
        Assert.Equal(PlaceClass.Populated, records[1].Class);
        Assert.Null(records[1].FeatureCode);
        Assert.Equal(3699, records[1].Population);
    }

    [Fact]
    public void Places_Write_HeaderIsUplcVersion1()
    {
        var raw = Gunzip(BuildBytes(PlacesFormat.Build(UsLines, CityLines)));
        Assert.Equal("UPLC"u8.ToArray(), raw[..4]);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(4)));
        Assert.Equal(6u, BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(6)));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(10)));   // Nome, Anchorage, Adak, Auckland
    }

    [Fact]
    public void Places_RoundTrip_NearestFeatureAndPopulatedPlace()
    {
        var index = Index();
        Assert.Equal(6, index.Count);
        var clip = new GeoPoint(64.56267, -165.36964);
        var feature = Assert.Single(index.Near(clip, Distance.FromMiles(1.5), PlaceClass.Feature, 6));
        Assert.Equal("Anvil Mountain", feature.Name);
        Assert.Equal("MT", feature.FeatureCode);
        Assert.Equal("America/Nome", feature.TzId);
        Assert.Equal(new GeoPoint(64.5639, -165.3703), feature.Point);
        Assert.Equal(GeoMath.Haversine(clip, feature.Point).Meters, feature.Away.Meters, 1e-6);

        var town = Assert.Single(index.Near(clip, Distance.FromMiles(30), PlaceClass.Populated, 6));
        Assert.Equal("Nome", town.Name);
        Assert.Equal("P", town.FeatureCode);
        Assert.Equal(3699, town.Population);
    }

    [Fact]
    public void Places_Near_RespectsRadiusClassAndMaxAndSortsNearestFirst()
    {
        var index = Index();
        var zachar = new GeoPoint(57.5504, -153.7390);
        Assert.Empty(index.Near(zachar, Distance.FromMiles(0.01), PlaceClass.Feature, 6));
        Assert.Empty(index.Near(zachar, Distance.FromMiles(5), PlaceClass.Feature, 0));
        var both = index.Near(zachar, Distance.FromMiles(5), PlaceClass.Populated, 6).Concat(index.Near(zachar, Distance.FromMiles(5), PlaceClass.Feature, 6)).ToList();
        Assert.Equal(2, both.Count);
        Assert.All(both, h => Assert.Equal("Zachar Bay", h.Name));
        var one = index.Near(new GeoPoint(64.55, -165.38), Distance.FromMiles(30), PlaceClass.Feature, 1);
        Assert.Equal("Anvil Mountain", Assert.Single(one).Name);
        var towns = index.Near(zachar, Distance.FromMiles(700), PlaceClass.Populated, 6);
        string[] nearestFirst = ["Zachar Bay", "Nome"];
        Assert.Equal(nearestFirst, towns.Select(h => h.Name).ToArray());
        Assert.True(towns[0].Away.Meters < towns[1].Away.Meters);
        Assert.Equal("Zachar Bay", Assert.Single(index.Near(zachar, Distance.FromMiles(700), PlaceClass.Populated, 1)).Name);
    }

    [Fact]
    public void Places_Near_CrossesAntimeridian()
    {
        var hit = Assert.Single(Index().Near(new GeoPoint(51.9, -179.95), Distance.FromMiles(5), PlaceClass.Feature, 6));
        Assert.Equal("Dateline Cape", hit.Name);
        Assert.Equal(4.263, hit.Away.Miles, 0.01);
    }

    [Fact]
    public void Places_Near_WorksInTheSouthernHemisphere()
        => Assert.Equal("Wellington", Assert.Single(Index().Near(new GeoPoint(-41.29, 174.78), Distance.FromMiles(3), PlaceClass.Populated, 6)).Name);

    [Fact]
    public void Places_LongName_IsCutAtAUtf8Boundary()
    {
        var name = new string('é', 200);   // 400 UTF-8 bytes
        List<PlaceRecord> records = [new PlaceRecord(1, name, 10, 10, PlaceClass.Populated, null, 1, "UTC")];
        using var ms = new MemoryStream(BuildBytes(records));
        var hit = Assert.Single(PlaceIndex.Load(ms).Near(new GeoPoint(10, 10), Distance.FromMiles(1), PlaceClass.Populated, 1));
        Assert.Equal(new string('é', 127), hit.Name);
    }

    [Fact]
    public void Places_Load_RejectsBadMagic()
    {
        using var ms = new MemoryStream(Gzip(Encoding.ASCII.GetBytes("NOPE\u0001\u0000")));
        Assert.Throws<InvalidDataException>(() => PlaceIndex.Load(ms));
    }

    [Fact]
    public void Places_Load_RejectsTruncatedFile()
    {
        var raw = Gunzip(BuildBytes(PlacesFormat.Build(UsLines, CityLines)));
        using var ms = new MemoryStream(Gzip(raw[..^3]));
        Assert.Throws<InvalidDataException>(() => PlaceIndex.Load(ms));
    }

    sealed class BytesAssets(byte[] places) : IAppAssets
    {
        public Stream OpenPlaces() => new MemoryStream(places, writable: false);
        public Stream OpenSelfTest(string name) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Places_LoadAsync_ReadsThroughAppAssets()
    {
        var index = await PlaceIndex.LoadAsync(new BytesAssets(BuildBytes(PlacesFormat.Build(UsLines, CityLines))), TestContext.Current.CancellationToken);
        Assert.Equal(6, index.Count);
    }
}
