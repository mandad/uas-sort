// tests/UasSort.Review.Tests/MapMessageTests.cs
using System.Text.Json.Nodes;

namespace UasSort.Review.Tests;

public class MapMessageTests
{
    // Ref §9.6 golden examples, verbatim.
    private const string Init = """{"v":1,"type":"init","config":{"streetsStyleUrl":"https://tiles.openfreemap.org/styles/liberty","streetsDarkStyleUrl":"https://tiles.openfreemap.org/styles/dark","satelliteUrl":"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"},"base":"streets","radiusMiles":50,"online":true,"theme":"light"}""";
    private const string SetData = """{"v":1,"type":"setData","rev":7,"items":[{"id":"DCIM/DJI_001/DJI_20260927140627_0128_D.MP4","groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","lon":-153.738973,"lat":57.550442,"kind":"video"}],"groups":[{"id":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","color":"#1F77B4","center":[-153.7409,57.5415],"label":"Sep 27 · 13 clips"}],"jumps":[{"from":[-164.2657,64.6935],"to":[-165.3696,64.5627],"label":"34 mi · 21 h"}]}""";
    private const string Select = """{"v":1,"type":"select","groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","itemIds":[],"fit":true,"bbox":[-153.76,57.53,-153.72,57.56]}""";
    private const string SetRadius = """{"v":1,"type":"setRadius","radiusMiles":25}""";
    private const string SetBase = """{"v":1,"type":"setBase","base":"satellite"}""";
    private const string SetTheme = """{"v":1,"type":"setTheme","theme":"dark"}""";
    private const string Fit = """{"v":1,"type":"fit","bbox":[-165.40,64.55,-164.25,64.70]}""";
    private const string Ping = """{"v":1,"type":"ping","n":1}""";
    private const string Ready = """{"v":1,"type":"ready","maplibre":"6.11.2","webgl2":true}""";
    private const string Pong = """{"v":1,"type":"pong","n":1}""";
    private const string Click = """{"v":1,"type":"click","itemIds":["DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"],"groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","ctrl":false,"shift":false}""";
    private const string ClickEmpty = """{"v":1,"type":"clickEmpty","itemIds":[],"groupId":null,"ctrl":false,"shift":false}""";
    private const string ContextMenu = """{"v":1,"type":"contextMenu","itemIds":["DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"],"x":412,"y":288}""";
    private const string TileError = """{"v":1,"type":"tileError","base":"satellite","message":"HTTP 503"}""";
    private const string BaseUnavailable = """{"v":1,"type":"baseUnavailable","base":"satellite","message":"4 tile errors"}""";
    private const string Error = """{"v":1,"type":"error","base":null,"message":"Uncaught TypeError: …"}""";

    private const string Anchor = "DCIM/DJI_001/DJI_20260927140127_0123_D.MP4";
    private const string Clip128 = "DCIM/DJI_001/DJI_20260927140627_0128_D.MP4";

    private static void AssertSameJson(string golden, HostToMap message)
        => Assert.True(JsonNode.DeepEquals(JsonNode.Parse(golden), JsonNode.Parse(MapBridge.Serialize(message))),
                       $"expected {golden}\nactual   {MapBridge.Serialize(message)}");

    [Fact]
    public void MapMessages_HostToMap_SerialiseToTheGoldenObjects()
    {
        AssertSameJson(Init, new MapInit(new MapConfig("https://tiles.openfreemap.org/styles/liberty", "https://tiles.openfreemap.org/styles/dark",
            "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"), "streets", 50, true, "light"));
        AssertSameJson(SetData, new MapSetData(7,
            [new MapItem(Clip128, Anchor, -153.738973, 57.550442, "video")],
            [new MapGroup(Anchor, "#1F77B4", new GeoPoint(57.5415, -153.7409), "Sep 27 · 13 clips")],
            [new MapJump(new GeoPoint(64.6935, -164.2657), new GeoPoint(64.5627, -165.3696), "34 mi · 21 h")]));
        AssertSameJson(Select, new MapSelect(Anchor, [], true, [-153.76, 57.53, -153.72, 57.56]));
        AssertSameJson(SetRadius, new MapSetRadius(25));
        AssertSameJson(SetBase, new MapSetBase("satellite"));
        AssertSameJson(SetTheme, new MapSetTheme("dark"));
        AssertSameJson(Fit, new MapFit([-165.40, 64.55, -164.25, 64.70]));
        AssertSameJson(Ping, new MapPing(1));
    }

    [Fact]
    public void MapMessages_HostToMapGolden_DeserialiseWithVBeforeType()
    {
        var setData = Assert.IsType<MapSetData>(MapBridge.ParseHostMessage(SetData));
        Assert.Equal(new GeoPoint(57.5415, -153.7409), setData.Groups[0].Center);
        Assert.Equal(new GeoPoint(64.5627, -165.3696), setData.Jumps[0].To);
        Assert.Equal(1, Assert.IsType<MapPing>(MapBridge.ParseHostMessage(Ping)).N);
        Assert.IsType<MapInit>(MapBridge.ParseHostMessage(Init));
    }

    [Fact]
    public void MapMessages_MapToHostGolden_DeserialiseWithVBeforeType()
    {
        var ready = Assert.IsType<MapReady>(MapBridge.Parse(Ready));
        Assert.Equal("6.11.2", ready.Maplibre);
        Assert.True(ready.Webgl2);
        Assert.Equal(1, ready.V);
        Assert.Equal(1, Assert.IsType<MapPong>(MapBridge.Parse(Pong)).N);
        var click = Assert.IsType<MapClick>(MapBridge.Parse(Click));
        Assert.Equal(Clip128, Assert.Single(click.ItemIds));
        Assert.Equal(Anchor, click.GroupId);
        var empty = Assert.IsType<MapClickEmpty>(MapBridge.Parse(ClickEmpty));
        Assert.Null(empty.GroupId);
        Assert.Empty(empty.ItemIds);
        var menu = Assert.IsType<MapContextMenu>(MapBridge.Parse(ContextMenu));
        Assert.Equal(412, menu.X);
        Assert.Equal(288, menu.Y);
        Assert.Equal("HTTP 503", Assert.IsType<MapTileError>(MapBridge.Parse(TileError)).Message);
        Assert.Equal("satellite", Assert.IsType<MapBaseUnavailable>(MapBridge.Parse(BaseUnavailable)).Base);
        Assert.Null(Assert.IsType<MapError>(MapBridge.Parse(Error)).Base);
    }

    [Fact]
    public void MapBridge_Dispatch_RaisesReceivedAndLogsUnknownTypes()
    {
        var log = new ListLog();
        var sent = new List<string>();
        using var bridge = new MapBridge(sent.Add, log, new FakeTimeProvider());
        var got = new List<MapToHost>();
        bridge.Received += got.Add;

        bridge.Dispatch(Pong);
        bridge.Dispatch("""{"v":1,"type":"hover","itemIds":[]}""");
        bridge.Dispatch("not json");
        bridge.Send(new MapPing(2));

        Assert.IsType<MapPong>(Assert.Single(got));
        Assert.Equal(2, log.Warnings.Count);
        Assert.Contains("\"n\":2", Assert.Single(sent), StringComparison.Ordinal);
    }
}
