// tests/UasSort.Core.Tests/Geo/PlaceIndexGoldenTests.cs
namespace UasSort.Core.Tests.Geo;

/// <summary>build-places → PlaceIndex.Load on the real extract (Ref §8.7, §14 step 4): the names the Python spike found.</summary>
public sealed class PlaceIndexGoldenTests
{
    static readonly Lazy<PlaceIndex> Index = new(() =>
    {
        using var s = typeof(PlaceIndexGoldenTests).Assembly.GetManifestResourceStream("places.bin.gz")
                      ?? throw new InvalidOperationException("places.bin.gz is not embedded");
        return PlaceIndex.Load(s);
    });

    // first GPS fixes of the real clips (docs/research/spikes/djmd/calibration.json), rounded to 6 decimals
    static readonly ImmutableArray<GeoPoint> AnvilClips =
    [
        new(64.562676, -165.369638), new(64.563839, -165.368771), new(64.564500, -165.370056), new(64.564337, -165.369335),
        new(64.564249, -165.370239), new(64.563745, -165.369407), new(64.562642, -165.373221), new(64.562640, -165.373229),
        new(64.562636, -165.373232), new(64.563534, -165.371245), new(64.562896, -165.374300), new(64.563509, -165.371165),
        new(64.562750, -165.374623), new(64.562744, -165.372460), new(64.562304, -165.373683), new(64.555694, -165.360136),
        new(64.555346, -165.361350), new(64.553791, -165.370900), new(64.554418, -165.375791), new(64.560376, -165.373349),
        new(64.560349, -165.373351),
    ];

    static readonly ImmutableArray<GeoPoint> ZacharClips =
    [
        new(57.536828, -153.748385), new(57.536964, -153.747251), new(57.544347, -153.740997), new(57.546764, -153.739543),
        new(57.550442, -153.738973), new(57.549947, -153.739812), new(57.542624, -153.743735), new(57.542751, -153.743272),
        new(57.544364, -153.744304), new(57.535034, -153.736347), new(57.538145, -153.728609), new(57.536230, -153.732694),
        new(57.534836, -153.748082),
    ];

    /// <summary>Over all clips: the smallest distance at which the nearest place (of the given classes) is the named one.</summary>
    static double NearestNamedMiles(ImmutableArray<GeoPoint> clips, string name, params PlaceClass[] classes)
    {
        var best = double.MaxValue;
        foreach (var p in clips)
        {
            var nearest = classes.SelectMany(c => Index.Value.Near(p, Distance.FromMiles(5), c, 1))
                                 .OrderBy(h => h.Away.Meters)
                                 .FirstOrDefault();
            if (nearest is not null && nearest.Name == name) best = Math.Min(best, nearest.Away.Miles);
        }
        return best;
    }

    [Fact]
    public void PlacesGolden_Extract_HasTheExpectedShape()
    {
        Assert.InRange(Index.Value.Count, 400_000, 900_000);
        var nome = Index.Value.Near(new GeoPoint(64.5011, -165.4064), Distance.FromMiles(3), PlaceClass.Populated, 1);
        Assert.Equal("Nome", Assert.Single(nome).Name);
        Assert.Equal("America/Nome", nome[0].TzId);
    }

    [Fact]
    public void PlacesGolden_AnvilMountain_IsNearestFeature_Within0_2Mi()
        => Assert.InRange(NearestNamedMiles(AnvilClips, "Anvil Mountain", PlaceClass.Feature), 0.0, 0.2);

    // 1.5 mi = spec §8.7 rule 3's feature radius (user decision 2026-09-28; GeoNames puts the bay point ~1.0 mi from the flights)
    [Fact]
    public void PlacesGolden_ZacharBay_IsNearestFeature_Within1_5Mi()
        => Assert.InRange(NearestNamedMiles(ZacharClips, "Zachar Bay", PlaceClass.Feature), 0.0, 1.5);
}
