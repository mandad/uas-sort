namespace UasSort.Core.Tests.Config;

public sealed class SettingsCodecTests
{
    private const string Pictures = @"C:\Users\u\Pictures";
    private static readonly string[] PresetNames = ["Esri", "USGS"];

    [Fact]
    public void Settings_Derive_DefaultsComeFromThePicturesFolder()
    {
        Settings d = SettingsDefaults.Derive(Pictures);
        Assert.Equal(@"C:\Users\u\Pictures\UAS Videos", d.VideoRoot);
        Assert.Equal(@"C:\Users\u\Pictures\UAS Videos\Picture Offload", d.PhotoRoot);
        Assert.Empty(d.PreviousPhotoRoots);
        Assert.Equal((1, 50.0, 1), (d.Schema, d.RadiusMiles, d.GapDays));
        Assert.Equal((StoredClockMode.Zone, "America/New_York"), (d.DroneClockMode, d.DroneClockZone));
        Assert.True(d.CopyJpgTwin);
        Assert.False(d.RootsConfirmed);
        Assert.Equal("streets", d.Map.Base);
        Assert.Equal(SettingsDefaults.EsriUrl, d.Map.SatelliteUrl);
        Assert.Equal(PresetNames, d.Map.SatellitePresets.Keys.Order(StringComparer.Ordinal));
        Assert.Equal((380.0, 0.45), (d.Layout.TimelineWidth, d.Layout.MapHeightRatio));
        Assert.Equal(@"C:\Users\u\Pictures\UAS Videos\.uas-sort", LedgerPaths.For(d.VideoRoot));
    }

    [Fact]
    public void Settings_Serialize_HasNoLedgerDirKey()
    {
        string json = SettingsCodec.Serialize(SettingsDefaults.Derive(Pictures) with { RootsConfirmed = true });
        Assert.DoesNotContain("ledger", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"schema\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"droneClockMode\": \"Zone\"", json, StringComparison.Ordinal);
        Assert.Contains("\"rootsConfirmed\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"previousPhotoRoots\": []", json, StringComparison.Ordinal);
        Assert.Contains("\"USGS\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        Settings s = SettingsDefaults.Derive(Pictures) with
        {
            PreviousPhotoRoots = [@"D:\Old Offload"], DroneClockMode = StoredClockMode.SiteLocal, RadiusMiles = 33, GapDays = 0,
            RootsConfirmed = true,
        };
        string json = SettingsCodec.Serialize(s);
        SettingsParse back = SettingsCodec.Parse(json);
        Assert.Null(back.Error);
        Assert.NotNull(back.Settings);
        Assert.Equal(json, SettingsCodec.Serialize(back.Settings));
    }

    [Fact]
    public void Settings_Parse_TheReferenceFile()
    {
        const string json = """
            { "schema": 1, "rootsConfirmed": true,
              "videoRoot": "C:\\Users\\u\\OneDrive\\Pictures\\UAS Videos",
              "photoRoot": "C:\\Users\\u\\OneDrive\\Pictures\\UAS Videos\\Picture Offload",
              "previousPhotoRoots": [],
              "radiusMiles": 50, "gapDays": 1, "droneClockMode": "Zone", "droneClockZone": "America/New_York", "copyJpgTwin": true,
              "map": { "base": "streets", "streetsStyleUrl": "https://tiles.openfreemap.org/styles/liberty",
                       "streetsDarkStyleUrl": "https://tiles.openfreemap.org/styles/dark",
                       "satelliteUrl": "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                       "satellitePresets": { "Esri": "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                                             "USGS": "https://basemap.nationalmap.gov/arcgis/rest/services/USGSImageryOnly/MapServer/tile/{z}/{y}/{x}" } },
              "layout": { "timelineWidth": 380, "mapHeightRatio": 0.45 } }
            """;
        SettingsParse p = SettingsCodec.Parse(json);
        Assert.Null(p.Error);
        Settings s = Assert.IsType<Settings>(p.Settings);
        Assert.Equal(@"C:\Users\u\OneDrive\Pictures\UAS Videos", s.VideoRoot);
        Assert.True(s.RootsConfirmed);
        Assert.Equal(StoredClockMode.Zone, s.DroneClockMode);
        Assert.Equal(SettingsDefaults.UsgsUrl, s.Map.SatellitePresets["USGS"]);
    }

    [Fact]
    public void Settings_Parse_MissingOptionalBlocks_GetDefaults()
    {
        SettingsParse p = SettingsCodec.Parse("""
            { "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\V\\Picture Offload", "radiusMiles": 50, "gapDays": 1,
              "droneClockMode": "SiteLocal", "droneClockZone": "America/New_York", "copyJpgTwin": false, "rootsConfirmed": true }
            """);
        Settings s = Assert.IsType<Settings>(p.Settings);
        Assert.Empty(s.PreviousPhotoRoots);
        Assert.Equal(SettingsDefaults.Map().SatelliteUrl, s.Map.SatelliteUrl);
        Assert.Equal(380.0, s.Layout.TimelineWidth);
        Assert.Equal(StoredClockMode.SiteLocal, s.DroneClockMode);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{ "schema": 2, "videoRoot": "C:\\V", "photoRoot": "C:\\P", "radiusMiles": 50, "gapDays": 1, "droneClockMode": "Zone", "droneClockZone": "America/New_York" }""")]
    [InlineData("""{ "schema": 1, "photoRoot": "C:\\P", "radiusMiles": 50, "gapDays": 1, "droneClockMode": "Zone", "droneClockZone": "America/New_York" }""")]
    [InlineData("""{ "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\P", "radiusMiles": 500, "gapDays": 1, "droneClockMode": "Zone", "droneClockZone": "America/New_York" }""")]
    [InlineData("""{ "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\P", "radiusMiles": 50, "gapDays": 9, "droneClockMode": "Zone", "droneClockZone": "America/New_York" }""")]
    [InlineData("""{ "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\P", "radiusMiles": 50, "gapDays": 1, "droneClockMode": "Bogus", "droneClockZone": "America/New_York" }""")]
    [InlineData("""{ "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\P", "radiusMiles": 50, "gapDays": 1, "droneClockMode": 7, "droneClockZone": "America/New_York" }""")]
    [InlineData("""{ "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\P", "radiusMiles": 50, "gapDays": 1, "droneClockMode": "Zone" }""")]
    public void Settings_Parse_UnreadableFiles_GiveAnError(string json)
    {
        SettingsParse p = SettingsCodec.Parse(json);
        Assert.Null(p.Settings);
        Assert.False(string.IsNullOrWhiteSpace(p.Error));
    }
}
