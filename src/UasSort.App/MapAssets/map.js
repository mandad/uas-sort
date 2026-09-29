// src/UasSort.App/MapAssets/map.js — bootstrap for the MIME check (Task 11.8 replaces this file with the full map)
const bridge = window.chrome?.webview;
const post = (type, body = {}) => bridge?.postMessage({ v: 1, type, ...body });
const manifest = await (await fetch('lib/manifest.json')).json();
const mod = await import('./lib/' + manifest.entry);
const ml = mod.Map ? mod : mod.default;
if (manifest.worker) ml.setWorkerUrl(manifest.worker);
const webgl2 = !!document.createElement('canvas').getContext('webgl2');
bridge?.addEventListener('message', (ev) => { if (ev.data?.type === 'ping') post('pong', { n: ev.data.n }); });
if (!webgl2) post('ready', { maplibre: manifest.version, webgl2: false });
else {
  const map = new ml.Map({ container: 'map', style: { version: 8, sources: {}, layers: [] }, center: [-165.4, 64.5], zoom: 6 });
  map.once('load', () => post('ready', { maplibre: ml.getVersion?.() ?? manifest.version, webgl2: true }));
}
