# Map pane for the uas-sort WinUI 3 review screen (as of 2026-09-27)

## Recommendation

Use the WinUI 3 `WebView2` control running **MapLibre GL JS 6.11.2**, with every file bundled locally and served through virtual host mapping.

- **Streets map:** OpenFreeMap. It needs no key and has no request limits.
- **Satellite toggle:** Esri World Imagery as the default, which works worldwide and has no key on its older endpoint. USGS imagery is the public-domain US option, and it covers Alaska in high resolution.
- **Offline:** a plain canvas showing the group-coloured dots, the radius circle, labelled jump lines, GeoNames place labels using bundled fonts, and a scale bar in miles. Coastlines from Natural Earth are optional.
- **Not viable:**
  - The Windows App SDK MapControl is ruled out. Its pins can't be coloured or labelled, and it can't draw lines or circles.
  - Mapsui.WinUI is a weaker fallback. It shows raster tiles only in practice, and nothing confirms it works with .NET 10/11 or Windows App SDK 2.x.
  - Leaflet is out of date: the last stable release was 1.9.4 in 2023, and 2.0 is still alpha.

## What I checked in this session

- **Browser spike** (`.../scratchpad/spikes/map-pane/web/`: `index.html`, `map.js`, `test.js`, plus `shot_*.png`). I ran MapLibre 6.11.2 from a local folder in Chromium through Playwright, served over http.
  - These worked: the ES module and its worker, the OpenFreeMap Liberty style, Esri raster tiles, USGS raster tiles, dots coloured by group, the dashed 50-mile circle, dashed jump lines with labels, place labels using local fonts, the imperial scale bar, and Ctrl-click sending `{type:"click", itemIds, groupId, ctrl:true}`.
  - With the network blocked, the page fell back to the offline canvas and sent `baseUnavailable` or `tileError` to the host.
  - One trap I hit: `map.isStyleLoaded()` returns false while tiles are still loading. Check `map.getSource('items')` instead before updating the overlays.
- **This was not the real WebView2.** Nothing was built as WinUI, because this machine has no .NET 10 SDK and no WinUI build. The WebView2 wiring below is UNVERIFIED on this machine. The risk is low, since WebView2 uses the same Chromium engine.
- **Imagery samples.** I fetched one tile at a time, around Nome, Council and Anvil Mountain at zoom 15–16.
  - Esri and USGS both show sub-metre detail, including rural Council.
  - USGS says its Alaska data is NAIP 2020; its older "10 m SPOT for Alaska" description is out of date.
  - USGS looks cloudy at low zoom.
  - EOX Sentinel-2 is 10 m, which is too blurry here.
- **Versions** from the npm and NuGet registries:

| Package | Version and date |
|---|---|
| maplibre-gl | 6.11.2, 2026-09-24. v6.0.0 came out 2026-07-22 and is ESM-only (no UMD build); its worker loads from a real URL. |
| leaflet | 1.9.4 stable (2023); 2.0.0-alpha.1 |
| Microsoft.WindowsAppSDK | 2.5.1, 2026-09-16 |
| Microsoft.Web.WebView2 | 1.0.4191.47 |
| Mapsui.WinUI | 5.1.0, 2026-05-27 |
| pmtiles | 4.5.0 |

- **Housekeeping.**
  - The Playwright tool wrote a `.playwright-mcp` folder into `C:\dev\uas-sort`. I deleted it, and the repo folder is empty again.
  - The local http server is stopped.
  - I created no Windows temp folders. `uas-sort-spike-modernstack` and `uas-sort-spike-winui` exist under `%TEMP%`; they belong to other agents, so I left them alone.

## 1. Map options compared

| | Windows App SDK MapControl | **WebView2 + MapLibre GL JS 6** | WebView2 + Leaflet | Mapsui.WinUI |
|---|---|---|---|---|
| Version (2026-09) | In Windows App SDK 1.5–2.x; API docs updated 2026-07-28 | 6.11.2 (BSD-3); WebView2 is built into WinUI 3 | 1.9.4 (2023); 2.0 is alpha | 5.1.0 (MIT) |
| Key or account | **An Azure Maps account and key are required**, and the key ships inside the app. Free tier: 5,000 base-map and 1,000 imagery transactions a month, 15 tiles each. Gen1 pricing retired 2026-09-15. | None with OpenFreeMap, Esri (older endpoint) or USGS | Same as MapLibre | None |
| Coloured dots per group | **No.** `MapIcon` has only `Location`: no colour, title or z-order. | Yes, a circle layer driven by data (verified) | Yes | Yes |
| Radius circle and jump lines | **No.** `MapElement` has only `MapIcon`, and `MapLayer` only `MapElementsLayer`. | Yes (verified) | Yes | Yes |
| Click and multi-select sync | Only a `MapElementClick` event | Yes, via click, contextmenu and feature-state (verified) | Yes | Yes, via its MapInfo API |
| Satellite | Only through Azure's built-in style picker; there is no API to switch styles | Any tile source (verified with Esri and USGS) | Raster tiles | Raster tiles |
| Vector street map with no key | No | Yes, OpenFreeMap | Needs an extra plugin | Only through a third-party package (VexTile) |
| Offline fallback | No | Yes: blank canvas with overlays and local fonts (verified) | Yes | Yes |
| .NET 10/11 and Windows App SDK 2.5 | Built in | WebView2 comes with Windows App SDK | Same | NuGet lists net8/net9-windows10.0.19041, depends on Windows App SDK 1.6 and SkiaSharp 3.119. The main branch still targets net9 and Windows App SDK 1.6. **net10 and Windows App SDK 2.x are UNVERIFIED.** |
| Verdict | **Ruled out** (feature gaps plus Azure billing) | **Recommended** | Outdated; no reason to pick it over MapLibre | Fallback only |

## 2. Tile and imagery sources for a personal desktop app

| Source | Key | Terms and limits | Attribution | Fit |
|---|---|---|---|---|
| **OpenFreeMap** (vector; styles liberty, bright, positron, dark, fiord) | None | Free with no request limits, and commercial use is allowed. Runs on donations with no SLA ("may discontinue at any time"). The terms forbid automated data collection, so no prefetching. | "OpenFreeMap © OpenMapTiles Data from OpenStreetMap"; MapLibre adds it automatically | **Default streets map.** Use `dark` when the app is in dark mode. |
| OpenStreetMap standard raster tiles | None | Strict policy: a user agent that identifies the app, no library-default user agents, cache for 7 days or more, and no bulk download, prefetch or offline use. Access can be blocked without notice. | "© OpenStreetMap contributors", always visible | Not needed. Only an optional URL in settings; it adds nothing over OpenFreeMap. |
| **Esri World Imagery**, older endpoint (`server.arcgisonline.com/.../World_Imagery/MapServer/tile/{z}/{y}/{x}`) | None; worked in this session. Cache header is 1 day. | Licensed under the Esri Master License Agreement. The layer is "not intended to be used to export tiles for offline". Esri's web-services terms allow non-commercial use; whether use outside ArcGIS apps needs an ArcGIS account is **UNVERIFIED** (Esri's blog returned 403). | "Esri, Vantor, Earthstar Geographics, and the GIS User Community" plus "Powered by Esri" (exact wording **UNVERIFIED**) | **Default satellite.** Worldwide, and the best at every zoom level. |
| Esri Static Basemap Tiles (`arcgis/imagery`) | **A key is required.** I got 401 "Token Required" without one. | Free ArcGIS Location Platform account: 2 million tiles a month free | Same as above | Optional upgrade if the user adds a key |
| **USGS National Map imagery** (`basemap.nationalmap.gov/.../USGSImageryOnly/MapServer/tile/{z}/{y}/{x}`) | None | USDA/USGS public domain. US only. **Alaska is covered by NAIP 2020** and looked sharp at zoom 16 near Council. Low zoom shows a cloudy mosaic. There is also a `USGSImageryTopo` hybrid. | "USGS The National Map / USDA NAIP" | Second satellite choice, with no terms questions |
| MapTiler free plan | Key required | Non-commercial only; 100,000 requests and 5,000 sessions a month; service stops until next month when exceeded; logo required. Satellite included. | Logo and attribution | Not needed |
| Stadia free plan | Key required | Non-commercial; 200,000 credits a month; **no satellite** (Standard plan or higher) | Required | Not needed |
| EOX Sentinel-2 cloudless | None | 10 m resolution; licence for recent years is CC BY-NC-SA (**UNVERIFIED**) | Required | Too blurry for drone sites |
| Protomaps PMTiles regional extract | None | OpenStreetMap-derived data (ODbL); free for non-commercial use | "© OpenStreetMap" | Optional later add-on for a real offline street map |

**Defaults:** Streets = OpenFreeMap (liberty or dark, following the app theme). Satellite = Esri, with USGS in the same dropdown. Base-map URLs live in `settings.json` rather than in code, as the OpenStreetMap policy recommends, so a source that dies or starts needing a token can be swapped without an update.

## 3. How it fits together

**Files**
- `UasSort.App/MapAssets/` holds `index.html`, `map.js`, `lib/maplibre-gl.{mjs,css}`, `lib/maplibre-gl-shared.mjs`, `lib/maplibre-gl-worker.mjs` (about 1.2 MB in total) and `fonts/Noto Sans {Regular,Bold}/{0-255,256-511}.pbf` (about 420 KB).
- Optionally add `ne_50m_land.geojson` (1.6 MB) and `ne_50m_lakes.geojson` (0.9 MB) from Natural Earth, which are public domain.
- Copy these as Content files. They must exist as real files on disk; a single-file publish would have to extract them to `%LOCALAPPDATA%\uas-sort\map\<version>\` first.
- Pin the MapLibre version and keep a copy of the files in the repo. Never load them from a CDN.
- The fonts use the same names as OpenFreeMap's font server, so the overlay labels render whether the streets map is on or not.

**C# host setup (UNVERIFIED on this machine)**
```csharp
const string Host = "map.uas-sort.example";           // reserved .example name, can never clash with a real site
var udf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uas-sort", "WebView2");
var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, udf, new CoreWebView2EnvironmentOptions());
await MapView.EnsureCoreWebView2Async(env);          // unpackaged apps default to a data folder next to the exe (bad under Program Files)
var core = MapView.CoreWebView2; var s = core.Settings;
s.UserAgent += $" uas-sort/{AppInfo.Version}";       // identify the app to tile servers
s.AreDefaultContextMenusEnabled = false;              // right-click goes to JS, then a WinUI MenuFlyout
s.AreBrowserAcceleratorKeysEnabled = false; s.IsZoomControlEnabled = false; s.IsStatusBarEnabled = false;
s.AreDevToolsEnabled = Debugger.IsAttached;
core.Profile.PreferredColorScheme = isDark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "MapAssets"), CoreWebView2HostResourceAccessKind.DenyCors);
core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith($"https://{Host}/")) e.Cancel = true; };
core.NewWindowRequested += (_, e) => { e.Handled = true; if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme is "https") _ = Launcher.LaunchUriAsync(u); }; // attribution links open in the browser
core.WebMessageReceived += (_, e) => Dispatch(JsonSerializer.Deserialize(e.WebMessageAsJson, MapJson.Default.MapToHost)!);
core.Navigate($"https://{Host}/index.html");
void Send(HostToMap m) => core.PostWebMessageAsJson(JsonSerializer.Serialize(m, MapJson.Default.HostToMap));
```

**Message format.**
- Every message carries `v:1` and `type`. The JS side puts `type` first (or set `AllowOutOfOrderMetadataProperties = true`).
- In C#, use source-generated System.Text.Json with `[JsonPolymorphic(TypeDiscriminatorPropertyName="type")]` and `[JsonDerivedType]`.
- Coordinates are always `[lon, lat]` (GeoJSON order). The C# records name the fields `Lon` and `Lat` to prevent swaps.
- C# works out every distance, label and colour, so the grid chips and the map always match. JS only draws the circle outline, from the centre and `radiusMiles`, using the same Earth radius of 6,371,008.8 m.

| Direction | type | Payload | Purpose |
|---|---|---|---|
| Host → map | `init` | `config{streetsStyleUrl, esriImageryUrl, usgsImageryUrl, …}, base, radiusMiles, online, theme` | Choose the base map; with no network, fall back to `none` |
| | `setData` | `rev, items[{id, groupId, lon, lat, kind}], groups[{id, color, center[lon,lat], label}], jumps[{from, to, label:"33 mi · 21 h"}]` | Initial scan, every edit, and live R/G slider regrouping (throttle to about 10 per second) |
| | `select` | `groupId, itemIds[], fit, bbox[w,s,e,n]` | Grid selection → radius circle and highlighted dots |
| | `setRadius` | `miles` | Slider preview of the circle only |
| | `setBase` | `base: "streets" \| "satellite" \| "usgs" \| "none"` | Toolbar toggle |
| | `places` | `places[{name, lon, lat, rank}]` | GeoNames labels for the current view (`PlaceIndex.InBox`, top N) |
| | `fit` / `setTheme` | `bbox` / `theme` | |
| Map → host | `ready` | `maplibre, webgl2` | Show the pane; if this never arrives within about 5 s, show "Map unavailable" and an "Open in browser" button |
| | `click` | `itemIds[], groupId, ctrl, shift` | Select in the grid (Ctrl adds to the selection) (verified) |
| | `clickEmpty` / `boxSelect` | `itemIds[]` | Box select is still to build: turn off MapLibre's box zoom and add a Shift-drag handler |
| | `contextMenu` | `itemIds[], x, y` | WinUI `MenuFlyout` at that point, e.g. "Move selected to new group" |
| | `viewport` | `bbox, zoom` (debounced 250 ms) | Host replies with `places` |
| | `tileError` / `baseUnavailable` | `base, message` | Offline badge; host may switch to `none` |
| | `error` | `message` | Log it |

## 4. Offline behaviour (no internet on a trip)

- **Detecting it.**
  - The host watches `NetworkInformation.NetworkStatusChanged` and the internet connectivity level.
  - JS counts base-map tile errors; after 4 it sends `tileError` and shows "Offline: base map unavailable". Verified in the spike.
  - If the streets style can't be fetched, JS falls back to `none` and sends `baseUnavailable`. Also verified.
- **What the offline canvas shows** (verified): a dark background, the group dots and selection, the 50-mile circle, jump lines with labels, GeoNames labels using the bundled fonts, and the imperial scale bar.
- **Recommended addition:** Natural Earth 50m land and lake outlines as a local GeoJSON layer, for coastline context. Public domain, about 2.5 MB. Not yet rendered in the spike.
- **Satellite cannot work offline.** Esri doesn't allow exporting tiles for offline use, and OpenStreetMap forbids prefetching. WebView2's normal browser cache may still show recently viewed tiles; don't rely on it.
- **Optional later:** a Protomaps `.pmtiles` extract (for example Alaska plus home, max zoom about 12) for a real offline street map. It needs HTTP range requests, and whether WebView2 virtual host mapping supports them is **UNVERIFIED**. The fallback is serving tiles from a `WebResourceRequested` handler.

## 5. Risks

1. **The WebView2 host code is unbuilt.** The JS side works in Chromium, but the WinUI part (virtual host, messaging, user agent, data folder) was not run here. Make it the first task of the map milestone. Mitigation: it is standard WebView2 API and the engine is the same.
2. **Esri terms.** The keyless older endpoint works today, but whether use outside ArcGIS apps needs an account is UNVERIFIED, and Esri could start requiring a token. Mitigation: URLs in settings, USGS as the backup, and an optional ArcGIS key field that switches to the 2-million-free-tiles service.
3. **OpenFreeMap has no SLA.** Mitigation: the URL is in settings, and the same map could be self-hosted or replaced by a PMTiles file.
4. **MapLibre changes fast.** It went from 6.0 to 6.11 in 9 weeks, and v6 broke compatibility (ESM only; `setData` lost its second argument; the old `styleimagemissing` callback became notify-only). Mitigation: pin and keep a copy of the files; upgrade on purpose.
5. **WebGL may be missing** (for example over RDP or in a VM). Mitigation: time out waiting for `ready`, then show a fallback panel.
6. **Keyboard focus.** WebView2 takes keyboard focus, so app shortcuts like Ctrl+Z need forwarding from JS (a `key` message) or focus management. Details UNVERIFIED.
7. **Footprint.** WebView2 adds Edge helper processes; memory use and start-up time are UNVERIFIED. It is only created when the review screen opens.
8. **USGS quirks.** Its service description is out of date and the low-zoom mosaic is cloudy. Its uptime has not been measured (UNVERIFIED).
9. **Change from the earlier plan.** The earlier verdict chose WPF with Mapsui. With WinUI 3 now chosen, Mapsui.WinUI is the weakest part of that plan, so the recommendation moves to WebView2 with MapLibre.

## Sources
- [WinUI MapControl guide](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/map-control), [MapControl API](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.mapcontrol), [MapElement API](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.mapelement?view=windows-app-sdk-2.0), [Nick's .NET Travels on MapControl](https://nicksnettravels.builttoroam.com/winui-mapcontrol/), [Azure Maps pricing](https://azure.microsoft.com/en-us/pricing/details/azure-maps/)
- [WebView2 local content and virtual host mapping](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/working-with-local-content), [WebView2 user data folders](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder), [WebView2 in WinUI 3](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/webview2), [Microsoft.Web.WebView2 on NuGet](https://www.nuget.org/packages/microsoft.web.webview2)
- [MapLibre GL JS releases](https://github.com/maplibre/maplibre-gl-js/releases), [maplibre-gl on npm](https://www.npmjs.com/package/maplibre-gl), [MaplibreNative.NET](https://github.com/tdcosta100/MaplibreNative.NET)
- [Mapsui.WinUI on NuGet](https://www.nuget.org/packages/Mapsui.WinUI/), [Mapsui on GitHub](https://github.com/Mapsui/Mapsui)
- [OSM tile usage policy](https://operations.osmfoundation.org/policies/tiles/), [OpenFreeMap](https://openfreemap.org/), [OpenFreeMap quick start](https://openfreemap.org/quick_start/), [OpenFreeMap terms](https://openfreemap.org/tos/)
- [Esri World Imagery service](https://services.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer), [ArcGIS Static Basemap Tiles](https://developers.arcgis.com/rest/static-basemap-tiles/), [Esri attribution](https://developers.arcgis.com/documentation/esri-and-data-attribution/), [Esri web services terms](https://www.esri.com/en-us/legal/terms/web-site-service)
- [USGSImageryOnly service](https://basemap.nationalmap.gov/arcgis/rest/services/USGSImageryOnly/MapServer)
- [MapTiler pricing](https://www.maptiler.com/cloud/pricing/), [Stadia pricing](https://stadiamaps.com/pricing/)
- [Protomaps basemap downloads](https://docs.protomaps.com/basemaps/downloads), [Natural Earth GeoJSON](https://github.com/nvkelso/natural-earth-vector)