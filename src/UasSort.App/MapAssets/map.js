// src/UasSort.App/MapAssets/map.js — uas-sort map pane: MapLibre GL JS 6.11.2 (vendored ESM), Ref §9.6 protocol v1.
// Port of docs/research/spikes/map-pane/web/map.js. C# computes every distance, label and colour; JS only draws
// (the radius circle uses the same Earth radius as the C# haversine).
const PROTOCOL = 1;
const EARTH_R_M = 6371008.8;
const M_PER_MI = 1609.344;
const LOCAL_GLYPHS = location.href.replace(/[^/]*$/, '') + 'fonts/{fontstack}/{range}.pbf';
const OVERLAY_SOURCES = new Set(['radius', 'jumps', 'items']);

// ---------- host bridge (WebView2, or a shim in a plain browser for debugging) ----------
const outbox = (window.__outbox = []);
const bridge = window.chrome?.webview ?? {
  postMessage: (m) => outbox.push(m),
  addEventListener: (_t, fn) => { window.__hostSend = (data) => fn({ data }); },
};
const post = (type, body = {}) => bridge.postMessage({ v: PROTOCOL, type, ...body });

// ---------- engine (entry and worker from lib/manifest.json, written by tools/vendor-maplibre.ps1) ----------
const manifest = await (await fetch('lib/manifest.json')).json();
const mod = await import('./lib/' + manifest.entry);
const ml = mod.Map ? mod : mod.default;
if (manifest.worker) ml.setWorkerUrl(manifest.worker);

// ---------- state ----------
let cfg = null;
let theme = 'light';
let online = true;
let requestedBase = 'none';
let baseKey = 'none';
let baseErrors = 0;
let tileErrorSent = false;
let radiusMiles = 50;
let data = { rev: 0, items: [], groups: [], jumps: [] };
let selection = { groupId: null, itemIds: [] };
let badgeText = '';
const jumpMarkers = [];
const empty = () => ({ type: 'FeatureCollection', features: [] });

function setBadge(t) {
  badgeText = t;
  const b = document.getElementById('badge');
  b.textContent = t;
  b.style.display = t ? 'block' : 'none';
}

function ring(lon, lat, meters, n = 128) {
  const d = meters / EARTH_R_M, la1 = lat * Math.PI / 180, lo1 = lon * Math.PI / 180, pts = [];
  for (let i = 0; i <= n; i++) {
    const b = 2 * Math.PI * i / n;
    const la2 = Math.asin(Math.sin(la1) * Math.cos(d) + Math.cos(la1) * Math.sin(d) * Math.cos(b));
    const lo2 = lo1 + Math.atan2(Math.sin(b) * Math.sin(d) * Math.cos(la1), Math.cos(d) - Math.sin(la1) * Math.sin(la2));
    pts.push([lo2 * 180 / Math.PI, la2 * 180 / Math.PI]);
  }
  return { type: 'Feature', properties: {}, geometry: { type: 'Polygon', coordinates: [pts] } };
}

function itemsFC() {
  const color = Object.fromEntries(data.groups.map((g) => [g.id, g.color]));
  return { type: 'FeatureCollection', features: data.items.map((it) => ({
    type: 'Feature', id: it.id,
    properties: { id: it.id, g: it.groupId, color: color[it.groupId] ?? '#888888', kind: it.kind },
    geometry: { type: 'Point', coordinates: [it.lon, it.lat] } })) };
}
function radiusFC() {
  const g = data.groups.find((x) => x.id === selection.groupId);
  return g && g.center ? { type: 'FeatureCollection', features: [ring(g.center[0], g.center[1], radiusMiles * M_PER_MI)] } : empty();
}
function jumpsFC() {
  return { type: 'FeatureCollection', features: data.jumps.map((j) => ({
    type: 'Feature', properties: { label: j.label }, geometry: { type: 'LineString', coordinates: [j.from, j.to] } })) };
}

function overlay() {
  const noGps = ['==', ['get', 'kind'], 'videoNoGps'];
  const sel = ['boolean', ['feature-state', 'sel'], false];
  return {
    sources: {
      radius: { type: 'geojson', data: radiusFC() },
      jumps: { type: 'geojson', data: jumpsFC() },
      items: { type: 'geojson', data: itemsFC(), promoteId: 'id' },
    },
    layers: [
      { id: 'radius-fill', type: 'fill', source: 'radius', paint: { 'fill-color': '#4aa3ff', 'fill-opacity': 0.08 } },
      { id: 'radius-line', type: 'line', source: 'radius', paint: { 'line-color': '#4aa3ff', 'line-width': 2, 'line-dasharray': [3, 2] } },
      { id: 'jumps', type: 'line', source: 'jumps', paint: { 'line-color': '#ffcc33', 'line-width': 1.5, 'line-dasharray': [2, 2] } },
      { id: 'items', type: 'circle', source: 'items',
        paint: {
          'circle-color': ['case', noGps, 'rgba(0,0,0,0)', ['get', 'color']],
          'circle-radius': ['case', sel, 7, 5],
          'circle-stroke-color': ['case', noGps, ['get', 'color'], '#ffffff'],
          'circle-stroke-width': ['case', sel, 2.5, ['case', noGps, 2, 1]],
        } },
    ],
  };
}

function satelliteAttribution(url) {
  return url.includes('nationalmap.gov')
    ? 'Imagery: USGS The National Map / USDA NAIP'
    : 'Imagery: Esri, Vantor, Earthstar Geographics, and the GIS User Community | Powered by Esri';
}

async function baseParts(key) {
  if (key === 'streets') {
    const url = theme === 'dark' ? cfg.streetsDarkStyleUrl : cfg.streetsStyleUrl;
    const res = await fetch(url);
    if (!res.ok) throw new Error('style HTTP ' + res.status);
    const s = await res.json();
    return { sources: s.sources, layers: s.layers, glyphs: s.glyphs, sprite: s.sprite };
  }
  if (key === 'satellite') {
    return { sources: { base: { type: 'raster', tiles: [cfg.satelliteUrl], tileSize: 256, maxzoom: 19,
                                attribution: satelliteAttribution(cfg.satelliteUrl) } },
             layers: [{ id: 'base', type: 'raster', source: 'base' }] };
  }
  return { sources: {}, layers: [] };
}

async function buildStyle(key) {
  let base;
  try { base = await baseParts(key); }
  catch (e) {
    post('baseUnavailable', { base: key, message: String(e?.message ?? e) });
    setBadge('Offline: base map unavailable');
    key = 'none';
    base = await baseParts('none');
  }
  baseKey = key; baseErrors = 0; tileErrorSent = false;
  const ov = overlay();
  const hasBackground = base.layers.some((l) => l.type === 'background');
  const bg = { id: 'bg', type: 'background', paint: { 'background-color': theme === 'dark' ? '#2b3036' : '#e9e6df' } };
  return {
    version: 8,
    glyphs: base.glyphs ?? LOCAL_GLYPHS,
    ...(base.sprite ? { sprite: base.sprite } : {}),
    sources: { ...base.sources, ...ov.sources },
    layers: [...(hasBackground ? [] : [bg]), ...base.layers, ...ov.layers],
  };
}

async function applyBase(key) {
  requestedBase = key;
  map.setStyle(await buildStyle(online ? key : 'none'), { diff: false });
}

let lastSel = [];
function applySelectionState() {
  for (const id of lastSel) map.setFeatureState({ source: 'items', id }, { sel: false });
  lastSel = selection.itemIds.slice();
  for (const id of lastSel) map.setFeatureState({ source: 'items', id }, { sel: true });
}

function refreshSources() {
  if (!map.getSource('items')) return;                 // not isStyleLoaded(): false while tiles load (Ref §9.6)
  map.getSource('items').setData(itemsFC());
  map.getSource('radius').setData(radiusFC());
  map.getSource('jumps').setData(jumpsFC());
  applySelectionState();
  jumpMarkers.splice(0).forEach((m) => m.remove());
  for (const j of data.jumps) {
    const el = document.createElement('div');
    el.className = 'jump-label';
    el.textContent = j.label;
    jumpMarkers.push(new ml.Marker({ element: el }).setLngLat([(j.from[0] + j.to[0]) / 2, (j.from[1] + j.to[1]) / 2]).addTo(map));
  }
}

const fit = (bbox, animate) => map.fitBounds([[bbox[0], bbox[1]], [bbox[2], bbox[3]]], { padding: 40, maxZoom: 14, duration: animate ? 300 : 0 });

// ---------- WebGL 2 gate ----------
const webgl2 = !!document.createElement('canvas').getContext('webgl2');
if (!webgl2) {
  post('ready', { maplibre: manifest.version, webgl2: false });
  throw new Error('no WebGL 2');                        // host shows "Map unavailable"
}

// ---------- map ----------
const map = new ml.Map({
  container: 'map', style: { version: 8, glyphs: LOCAL_GLYPHS, sources: {}, layers: [] },
  center: [-165.4, 64.5], zoom: 6, attributionControl: { compact: false }, dragRotate: false, pitchWithRotate: false,
});
map.touchZoomRotate.disableRotation();
map.addControl(new ml.NavigationControl({ showCompass: false }), 'top-right');
map.addControl(new ml.ScaleControl({ unit: 'imperial', maxWidth: 120 }), 'bottom-left');
map.on('style.load', refreshSources);

const featuresAt = (point) => (map.getLayer('items') ? map.queryRenderedFeatures(point, { layers: ['items'] }) : []);
map.on('click', (e) => {
  const f = featuresAt(e.point);
  const mods = { ctrl: !!e.originalEvent.ctrlKey, shift: !!e.originalEvent.shiftKey };
  if (f.length) post('click', { itemIds: [...new Set(f.map((x) => x.properties.id))], groupId: f[0].properties.g, ...mods });
  else post('clickEmpty', { itemIds: [], groupId: null, ...mods });
});
map.on('contextmenu', (e) => {
  const f = featuresAt(e.point);
  if (f.length) post('contextMenu', { itemIds: [...new Set(f.map((x) => x.properties.id))], x: Math.round(e.point.x), y: Math.round(e.point.y) });
});
map.on('mouseenter', 'items', () => (map.getCanvas().style.cursor = 'pointer'));
map.on('mouseleave', 'items', () => (map.getCanvas().style.cursor = ''));
map.on('error', (e) => {
  const message = String(e.error?.message ?? e.error ?? 'map error');
  if (e.sourceId && !OVERLAY_SOURCES.has(e.sourceId)) {
    baseErrors++;
    if (!tileErrorSent) { tileErrorSent = true; post('tileError', { base: baseKey, message }); }
    if (baseErrors === 4) {                            // Ref §9.6: 4 tile errors = offline → overlays without a base
      post('baseUnavailable', { base: baseKey, message: '4 tile errors' });
      setBadge('Offline: base map unavailable');
      map.setStyle(buildStyleSync('none'), { diff: false });
      baseKey = 'none';
    }
  } else {
    post('error', { base: null, message });
  }
});

function buildStyleSync(key) {                         // 'none' needs no fetch
  const ov = overlay();
  return { version: 8, glyphs: LOCAL_GLYPHS, sources: { ...ov.sources },
           layers: [{ id: 'bg', type: 'background', paint: { 'background-color': theme === 'dark' ? '#2b3036' : '#e9e6df' } }, ...ov.layers] };
}

// ---------- host → map ----------
bridge.addEventListener('message', async (ev) => {
  const m = ev.data;
  if (!m || m.v !== PROTOCOL) { post('error', { base: null, message: 'bad protocol version' }); return; }
  switch (m.type) {
    case 'init':                                       // a new session (a new MapBridge: rescan, new card, or re-init)
      data = { rev: 0, items: [], groups: [], jumps: [] }; // before the await: the next bridge's setData starts again at rev 1
      selection = { groupId: null, itemIds: [] };
      refreshSources();
      cfg = m.config; radiusMiles = m.radiusMiles; theme = m.theme; online = m.online;
      document.body.dataset.theme = theme;
      setBadge(online || m.base === 'none' ? '' : 'Offline: base map unavailable');
      await applyBase(m.base);
      break;
    case 'setData':
      if ((m.rev ?? 0) < (data.rev ?? 0)) break;       // an older derive never overwrites a newer one
      data = { rev: m.rev ?? 0, items: m.items ?? [], groups: m.groups ?? [], jumps: m.jumps ?? [] };
      refreshSources();
      break;
    case 'select':
      selection = { groupId: m.groupId ?? null, itemIds: m.itemIds ?? [] };
      refreshSources();
      if (m.fit && m.bbox) fit(m.bbox, true);
      break;
    case 'setRadius':
      radiusMiles = m.radiusMiles;
      map.getSource('radius')?.setData(radiusFC());
      break;
    case 'setBase':
      if (!online && m.base !== 'none') { setBadge('Offline: base map unavailable'); requestedBase = m.base; break; }
      setBadge('');
      await applyBase(m.base);
      break;
    case 'setTheme':
      theme = m.theme;
      document.body.dataset.theme = theme;
      if (baseKey === 'streets') await applyBase('streets');
      else if (map.getLayer('bg')) map.setPaintProperty('bg', 'background-color', theme === 'dark' ? '#2b3036' : '#e9e6df');
      break;
    case 'fit':
      if (m.bbox) fit(m.bbox, true);
      break;
    case 'ping':
      post('pong', { n: m.n });
      break;
    default:
      post('error', { base: null, message: 'unknown message type ' + m.type });
  }
});

window.__uas = {
  stats: () => ({ items: data.items.length, groups: data.groups.length, jumps: data.jumps.length,
                  radius: radiusFC().features.length, base: baseKey, rev: data.rev ?? 0,
                  selected: selection.itemIds.length, badge: badgeText }),
};

map.once('load', () => post('ready', { maplibre: ml.getVersion?.() ?? manifest.version, webgl2: true }));
