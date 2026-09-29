namespace UasSort.Core.Config;

/// <summary>Derived defaults (Ref §11 settings.json, §4.5 CLI defaults). No literal user paths.</summary>
public static class SettingsDefaults
{
    public const string StreetsStyleUrl = "https://tiles.openfreemap.org/styles/liberty";
    public const string StreetsDarkStyleUrl = "https://tiles.openfreemap.org/styles/dark";
    public const string EsriUrl = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}";
    public const string UsgsUrl = "https://basemap.nationalmap.gov/arcgis/rest/services/USGSImageryOnly/MapServer/tile/{z}/{y}/{x}";

    public static MapSettings Map()
        => new("streets", StreetsStyleUrl, StreetsDarkStyleUrl, EsriUrl,
               ImmutableDictionary<string, string>.Empty.Add("Esri", EsriUrl).Add("USGS", UsgsUrl));

    public static LayoutSettings Layout() => new(380, 0.45);

    public static Settings Derive(string picturesFolder)
    {
        string videoRoot = PathRules.Join(picturesFolder, "UAS Videos");
        return new Settings(SettingsCodec.Schema, videoRoot, PathRules.Join(videoRoot, "Picture Offload"), [], 50, 1,
                            StoredClockMode.Zone, "America/New_York", CopyJpgTwin: true, Map(), Layout(), RootsConfirmed: false);
    }
}
