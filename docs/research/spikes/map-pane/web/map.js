// Spike: uas-sort map pane (MapLibre GL JS 6, ESM, all assets local).
// Host bridge: WebView2 (window.chrome.webview) or a test shim in a plain browser.
import * as maplibregl from './lib/maplibre-gl.mjs';

const PROTOCOL = 1;
const EARTH_R_M = 6371008.8;          // mean Earth radius, same constant as the C# haversine
const M_PER_MI = 1609.344;
const LOCAL_GLYPHS = location.href.replace(/[^/]*$/, '') + 'fonts/{fontstack}/{range}.pbf'; // braces must stay unescaped

// ---------- host bridge ----------
const outbox = (window.__outbox = []);
const bridge = window.chrome?.webview ?? {
  postMessage: (m) => outbox.push(m),
  addEventListener: (_t, fn) => { window.__hostSend = (data) => fn({ data }); },
};
const post = (type, body = {}) => bridge.postMessage({ v: PROTOCOL, type, ...body });

// ---------- base maps (registry is data: host can override URLs without an app update) ----------
const BASES = {
  none: { label: 'No base map', build: async () => ({ sources: {}, layers: [], attribution: '' }) },
  streets: {
    label: 'Streets (OpenFreeMap)',
    build: async (cfg) => {
      const s = await (await fetch(cfg.streetsStyleUrl)).json();
      return { sources: s.sources, layers: s.layers, glyphs: s.glyphs, sprite: s.sprite, attribution: '' };
    },
  },
  satellite: {
    label: 'Satellite (Esri World Imagery)',
    build: async (cfg) => ({
      sources: { base: { type: 'raster', tiles: [cfg.esriImageryUrl], tileSize: 256, maxzoom: 19,
        attribution: 'Imagery: Esri, Vantor, Earthstar Geographics, and the GIS User Community | Powered by Esri' } },
      layers: [{ id: 'base', type: 'raster', source: 'base' }],
    }),
  },
  usgs: {
    label: 'Satellite (USGS, US only)',
    build: async (cfg) => ({
      sources: { base: { type: 'raster', tiles: [cfg.usgsImageryUrl], tileSize: 256, maxzoom: 16,
        attribution: 'Imagery: USGS The National Map / USDA NAIP' } },
      layers: [{ id: 'base', type: 'raster', source: 'base' }],
    }),
  },
};

// ---------- state ----------
let cfg = null;
let data = { items: [], groups: [], jumps: [] };
let selection = { groupId: null, itemIds: [] };
let radiusMiles = 50;
let places = [];
let baseKey = 'none';
let baseErrors = 0;
const jumpMarkers = [];

const empty = () => ({ type: 'FeatureCollection', features: [] });

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
    properties: { id: it.id, g: it.groupId, color: color[it.groupId] ?? '#888', kind: it.kind },
    geometry: { type: 'Point', coordinates: [it.lon, it.lat] } })) };
}
function radiusFC() {
  const g = data.groups.find((x) => x.id === selection.groupId);
  return g ? { type: 'FeatureCollection', features: [ring(g.center[0], g.center[1], radiusMiles * M_PER_MI)] } : empty();
}
function jumpsFC() {
  return { type: 'FeatureCollection', features: data.jumps.map((j) => ({
    type: 'Feature', properties: { label: j.label },
    geometry: { type: 'LineString', coordinates: [j.from, j.to] } })) };
}
function placesFC() {
  return { type: 'FeatureCollection', features: places.map((p) => ({
    type: 'Feature', properties: { name: p.name, rank: p.rank ?? 0 },
    geometry: { type: 'Point', coordinates: [p.lon, p.lat] } })) };
}

// Overlay layers always sit on top of whatever base is active.
function overlay(glyphFont) {
  return {
    sources: {
      radius: { type: 'geojson', data: radiusFC() },
      jumps: { type: 'geojson', data: jumpsFC() },
      items: { type: 'geojson', data: itemsFC(), promoteId: 'id' },
      places: { type: 'geojson', data: placesFC() },
    },
    layers: [
      { id: 'places', type: 'symbol', source: 'places',
        layout: { 'text-field': ['get', 'name'], 'text-font': [glyphFont], 'text-size': 11, 'symbol-sort-key': ['-', ['get', 'rank']] },
        paint: { 'text-color': '#ddd', 'text-halo-color': '#000', 'text-halo-width': 1.2 } },
      { id: 'radius-fill', type: 'fill', source: 'radius', paint: { 'fill-color': '#4aa3ff', 'fill-opacity': 0.08 } },
      { id: 'radius-line', type: 'line', source: 'radius', paint: { 'line-color': '#4aa3ff', 'line-width': 2, 'line-dasharray': [3, 2] } },
      { id: 'jumps', type: 'line', source: 'jumps', paint: { 'line-color': '#ffcc33', 'line-width': 1.5, 'line-dasharray': [2, 2] } },
      { id: 'items', type: 'circle', source: 'items',
        paint: { 'circle-color': ['get', 'color'],
                 'circle-radius': ['case', ['boolean', ['feature-state', 'sel'], false], 7, 5],
                 'circle-stroke-color': '#fff',
                 'circle-stroke-width': ['case', ['boolean', ['feature-state', 'sel'], false], 2.5, 1] } },
    ],
  };
}

async function buildStyle(key) {
  let base;
  try { base = await BASES[key].build(cfg); }
  catch (e) { post('baseUnavailable', { base: key, reason: String(e) }); key = 'none'; base = await BASES.none.build(cfg); }
  baseKey = key; baseErrors = 0;
  const glyphs = base.glyphs ?? LOCAL_GLYPHS;
  const ov = overlay('Noto Sans Bold');  // exists both locally and on OpenFreeMap's glyph server
  const style = {
    version: 8, glyphs, ...(base.sprite ? { sprite: base.sprite } : {}),
    sources: { ...base.sources, ...ov.sources },
    // Keep the base style's own background (OpenFreeMap 'land' is its background); add ours only for raster/none.
    layers: [...(base.layers.some((l) => l.type === 'background') ? [] : [{ id: 'bg', type: 'background', paint: { 'background-color': '#2b3036' } }]),
             ...base.layers, ...ov.layers],
  };
  return style;
}

function refreshSources() {
  // Not isStyleLoaded(): that is false while any tile is still loading. We only need our sources to exist.
  if (!map.getSource('items')) return;
  map.getSource('items')?.setData(itemsFC());
  map.getSource('radius')?.setData(radiusFC());
  map.getSource('jumps')?.setData(jumpsFC());
  map.getSource('places')?.setData(placesFC());
  applySelectionState();
  // Jump labels as DOM markers: independent of any glyph server, few in number.
  jumpMarkers.splice(0).forEach((m) => m.remove());
  for (const j of data.jumps) {
    const el = document.createElement('div'); el.className = 'jump-label'; el.textContent = j.label;
    jumpMarkers.push(new maplibregl.Marker({ element: el }).setLngLat([(j.from[0] + j.to[0]) / 2, (j.from[1] + j.to[1]) / 2]).addTo(map));
  }
}
let lastSel = [];
function applySelectionState() {
  for (const id of lastSel) map.setFeatureState({ source: 'items', id }, { sel: false });
  lastSel = selection.itemIds.slice();
  for (const id of lastSel) map.setFeatureState({ source: 'items', id }, { sel: true });
}
const setBadge = (t) => { const b = document.getElementById('badge'); b.textContent = t; b.style.display = t ? 'block' : 'none'; };

// ---------- map ----------
const map = new maplibregl.Map({
  container: 'map', style: { version: 8, glyphs: LOCAL_GLYPHS, sources: {}, layers: [] },
  center: [-165.4, 64.5], zoom: 7, attributionControl: { compact: false }, dragRotate: false, pitchWithRotate: false,
});
window.__map = map;
map.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-right');
map.addControl(new maplibregl.ScaleControl({ unit: 'imperial', maxWidth: 120 }), 'bottom-left');
map.on('style.load', refreshSources);

map.on('click', 'items', (e) => {
  const ids = [...new Set(e.features.map((f) => f.properties.id))];
  post('click', { itemIds: ids, groupId: e.features[0].properties.g, ctrl: e.originalEvent.ctrlKey, shift: e.originalEvent.shiftKey });
});
map.on('contextmenu', 'items', (e) => {
  post('contextMenu', { itemIds: [...new Set(e.features.map((f) => f.properties.id))], x: e.point.x, y: e.point.y });
});
map.on('mouseenter', 'items', () => (map.getCanvas().style.cursor = 'pointer'));
map.on('mouseleave', 'items', () => (map.getCanvas().style.cursor = ''));
let vpTimer;
map.on('moveend', () => {
  clearTimeout(vpTimer);
  vpTimer = setTimeout(() => { const b = map.getBounds(); post('viewport', { bbox: [b.getWest(), b.getSouth(), b.getEast(), b.getNorth()], zoom: map.getZoom() }); }, 250);
});
map.on('error', (e) => {
  if (e.sourceId === 'base' || e.sourceId === 'openmaptiles' || e.sourceId === 'ne2_shaded') {
    if (++baseErrors === 4) { post('tileError', { base: baseKey, message: String(e.error?.message ?? e.error) }); setBadge('Offline: base map unavailable'); }
  } else post('error', { message: String(e.error?.message ?? e.error) });
});

// ---------- host -> map ----------
bridge.addEventListener('message', async (ev) => {
  const m = ev.data;
  if (!m || m.v !== PROTOCOL) return post('error', { message: 'bad protocol' });
  switch (m.type) {
    case 'init': cfg = m.config; radiusMiles = m.radiusMiles; map.setStyle(await buildStyle(m.base), { diff: false }); setBadge(m.online ? '' : 'Offline'); break;
    case 'setBase': setBadge(''); map.setStyle(await buildStyle(m.base), { diff: false }); break;
    case 'setData': data = m; refreshSources(); break;
    case 'select': selection = { groupId: m.groupId, itemIds: m.itemIds ?? [] };
      refreshSources();
      if (m.fit && m.bbox) map.fitBounds([[m.bbox[0], m.bbox[1]], [m.bbox[2], m.bbox[3]]], { padding: 40, maxZoom: 14, duration: 300 });
      break;
    case 'setRadius': radiusMiles = m.miles; map.getSource('radius')?.setData(radiusFC()); break;
    case 'places': places = m.places; map.getSource('places')?.setData(placesFC()); break;
    case 'fit': map.fitBounds([[m.bbox[0], m.bbox[1]], [m.bbox[2], m.bbox[3]]], { padding: 40, maxZoom: 14 }); break;
  }
});

map.once('load', () => post('ready', { maplibre: maplibregl.getVersion?.() ?? 'unknown', webgl2: !!map.painter?.context?.gl }));
