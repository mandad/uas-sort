window.__runScenario = async (base, opts = {}) => {
  const cfg = {
    streetsStyleUrl: 'https://tiles.openfreemap.org/styles/liberty',
    esriImageryUrl: 'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',
    usgsImageryUrl: 'https://basemap.nationalmap.gov/arcgis/rest/services/USGSImageryOnly/MapServer/tile/{z}/{y}/{x}'
  };
  window.__hostSend({ v: 1, type: 'init', config: cfg, base, radiusMiles: 50, online: !opts.offline });
  const items = [];
  let seed = 1; const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
  const add = (g, lon, lat, n) => { for (let i = 0; i < n; i++) items.push({ id: `${g}-${i}`, groupId: g, lon: lon + (rnd() - 0.5) * 0.08, lat: lat + (rnd() - 0.5) * 0.03, kind: 'mp4' }); };
  add('g1', -164.95, 64.62, 12); add('g2', -165.4411, 64.5556, 8); add('g3', -162.6, 64.95, 5);
  const groups = [
    { id: 'g1', color: '#e6194b', center: [-164.95, 64.62] },
    { id: 'g2', color: '#3cb44b', center: [-165.4411, 64.5556] },
    { id: 'g3', color: '#4363d8', center: [-162.6, 64.95] }];
  const jumps = [{ from: [-164.95, 64.62], to: [-165.4411, 64.5556], label: '15 mi · 21 h' }, { from: [-165.4411, 64.5556], to: [-162.6, 64.95], label: '87 mi · 2 d' }];
  await new Promise(r => setTimeout(r, 2500));
  window.__hostSend({ v: 1, type: 'setData', items, groups, jumps });
  window.__hostSend({ v: 1, type: 'select', groupId: 'g1', itemIds: items.filter(i => i.groupId === 'g1').map(i => i.id), fit: true, bbox: opts.bbox ?? [-167.2, 63.7, -162.2, 65.5] });
  window.__hostSend({ v: 1, type: 'places', places: [{ name: 'Nome', lon: -165.4064, lat: 64.5011, rank: 3 }, { name: 'Council', lon: -163.6745, lat: 64.8953, rank: 1 }, { name: 'Solomon', lon: -164.4406, lat: 64.5608, rank: 1 }] });
  await new Promise(r => setTimeout(r, opts.wait ?? 4000));
  const m = window.__map;
  const rd = await m.getSource('radius').getData();
  return { outbox: window.__outbox.map(x => x.type + (x.message ? ': ' + x.message : '')), radiusFeatures: rd.features.length, zoom: +m.getZoom().toFixed(2), layers: m.getStyle().layers.length };
};
