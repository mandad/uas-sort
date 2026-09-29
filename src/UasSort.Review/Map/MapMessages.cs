// src/UasSort.Review/Map/MapMessages.cs
using System.Text.Json.Serialization;

namespace UasSort.Review;

#pragma warning disable CA1056 // wire format (Ref §9.6): map.js reads URLs as plain strings

/// <summary>Host → map messages (Ref §9.6). Every message carries v:1 and a "type" discriminator.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MapInit), "init")]
[JsonDerivedType(typeof(MapSetData), "setData")]
[JsonDerivedType(typeof(MapSelect), "select")]
[JsonDerivedType(typeof(MapSetRadius), "setRadius")]
[JsonDerivedType(typeof(MapSetBase), "setBase")]
[JsonDerivedType(typeof(MapSetTheme), "setTheme")]
[JsonDerivedType(typeof(MapFit), "fit")]
[JsonDerivedType(typeof(MapPing), "ping")]
public closed record class HostToMap
{
    [JsonPropertyOrder(-1)] public int V { get; init; } = 1;
}

public sealed record MapConfig(string StreetsStyleUrl, string StreetsDarkStyleUrl, string SatelliteUrl);
public sealed record MapItem(string Id, string GroupId, double Lon, double Lat, string Kind);
public sealed record MapGroup(string Id, string Color, GeoPoint Center, string Label);
public sealed record MapJump(GeoPoint From, GeoPoint To, string Label);

public sealed record class MapInit(MapConfig Config, string Base, double RadiusMiles, bool Online, string Theme) : HostToMap;
public sealed record class MapSetData(int Rev, ImmutableArray<MapItem> Items, ImmutableArray<MapGroup> Groups, ImmutableArray<MapJump> Jumps) : HostToMap;
public sealed record class MapSelect(string GroupId, ImmutableArray<string> ItemIds, bool Fit, ImmutableArray<double> Bbox) : HostToMap;
public sealed record class MapSetRadius(double RadiusMiles) : HostToMap;
public sealed record class MapSetBase(string Base) : HostToMap;
public sealed record class MapSetTheme(string Theme) : HostToMap;
public sealed record class MapFit(ImmutableArray<double> Bbox) : HostToMap;
public sealed record class MapPing(int N) : HostToMap;

/// <summary>Map → host messages (Ref §9.6). map.js sends "v" before "type".</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MapReady), "ready")]
[JsonDerivedType(typeof(MapPong), "pong")]
[JsonDerivedType(typeof(MapClick), "click")]
[JsonDerivedType(typeof(MapClickEmpty), "clickEmpty")]
[JsonDerivedType(typeof(MapContextMenu), "contextMenu")]
[JsonDerivedType(typeof(MapTileError), "tileError")]
[JsonDerivedType(typeof(MapBaseUnavailable), "baseUnavailable")]
[JsonDerivedType(typeof(MapError), "error")]
public closed record class MapToHost
{
    [JsonPropertyOrder(-1)] public int V { get; init; } = 1;
}

public sealed record class MapReady(string Maplibre, bool Webgl2) : MapToHost;
public sealed record class MapPong(int N) : MapToHost;
public sealed record class MapClick(ImmutableArray<string> ItemIds, string? GroupId, bool Ctrl, bool Shift) : MapToHost;
public sealed record class MapClickEmpty(ImmutableArray<string> ItemIds, string? GroupId, bool Ctrl, bool Shift) : MapToHost;
public sealed record class MapContextMenu(ImmutableArray<string> ItemIds, double X, double Y) : MapToHost;
public sealed record class MapTileError(string? Base, string Message) : MapToHost;
public sealed record class MapBaseUnavailable(string? Base, string Message) : MapToHost;
public sealed record class MapError(string? Base, string Message) : MapToHost;

/// <summary>GeoPoint on the wire is [lon,lat] (Ref §3, §9.6) through Core's GeoPointJsonConverter (Part 02).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
                             AllowOutOfOrderMetadataProperties = true,
                             Converters = [typeof(GeoPointJsonConverter)])]
[JsonSerializable(typeof(HostToMap))]
[JsonSerializable(typeof(MapToHost))]
internal sealed partial class MapJsonContext : JsonSerializerContext;
