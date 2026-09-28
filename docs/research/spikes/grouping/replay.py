#!/usr/bin/env python3
"""Replay the real video library as if every clip were on one card, and check that the
clustering reproduces the user's own folders.  Uses ONLY names/sizes/mtimes (listing) plus
GPS already extracted by djmd/calibration.json and more_gps.json (Kodiak = cloud-only = no GPS)."""
import json, os, sys
from datetime import datetime, timedelta, timezone
from zoneinfo import ZoneInfo
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import grouping as G

ROOT = '/mnt/c/Users/damia/OneDrive/Pictures/UAS Videos'
UTC = timezone.utc
AK = ZoneInfo('America/Anchorage')

gps = {}
for r in json.load(open(os.path.join(os.path.dirname(__file__), '../djmd/calibration.json'))) + \
         json.load(open(os.path.join(os.path.dirname(__file__), 'more_gps.json'))):
    if r.get('lat') is not None:
        gps[r['name']] = (r['lat'], r['lon'], r.get('mvhd_creation_utc'))

lib_files, raws, truth = [], [], {}
for dp, dn, fn in os.walk(ROOT):
    for f in fn:
        full = os.path.join(dp, f)
        st = os.stat(full)                       # metadata only; never opens content
        rel = os.path.relpath(full, ROOT).replace(os.sep, '/')
        mt = datetime.fromtimestamp(st.st_mtime, UTC)
        lib_files.append(G.LibFile(rel, st.st_size, mt, 'photo' if rel.startswith('Picture Offload/') else 'video'))
        if rel.startswith('Picture Offload/') or not f.upper().endswith('.MP4'):
            continue
        folder = rel.rsplit('/', 1)[0]
        truth[f] = folder
        if f.startswith('DJI_'):
            dc = G.drone_clock_from_name(f)
            la, lo, mv = gps.get(f, (None, None, None))
            mvhd = datetime.fromisoformat(mv) if mv else None
            if mvhd is None and f not in ('DJI_20260727002013_0014_D.MP4', 'DJI_20260727005240_0024_D.MP4'):
                mvhd = (dc + timedelta(hours=4)).replace(tzinfo=UTC)   # not read: simulate moov (Kodiak)
            raws.append(G.RawItem(f, 'video', f, st.st_size, 'dji', mt, mvhd_utc=mvhd, lat=la, lon=lo))
        else:   # Autel: creation_time = wall clock mislabelled Z; mtime (FAT naive) shows same wall clock in AKDT
            wall = mt.astimezone(AK).replace(tzinfo=UTC)
            raws.append(G.RawItem(f, 'video', f, st.st_size, 'autel', mt, mvhd_utc=wall))

def partition(groups):
    return sorted(sorted(i.id for i in g.items) for g in groups)

want = {}
for n, fo in truth.items():
    want.setdefault(fo, []).append(n)
want_part = sorted(sorted(v) for v in want.values())

def check(p, verbose=False):
    items = G.normalize(raws, p, 'America/Anchorage')
    groups = G.cluster([i for i in items if i.kind == 'video'], p)
    ok = partition(groups) == want_part
    if verbose:
        for g in groups:
            fo = sorted({truth[i.id] for i in g.items})
            c = g.centroid
            print(f"  {g.start}..{g.end}  n={len(g.items):2d}  centroid={'%.4f,%.4f' % c if c else 'n/a':>20s}  "
                  f"tz={g.items[0].tz:18s} -> {fo}")
    return ok, len(groups)

print('Default Params:', G.Params())
ok, n = check(G.Params(), verbose=True)
print(f'reproduces the {len(want)} user folders exactly: {ok} ({n} groups)\n')

print('Sweep R (km) with G=1:')
good_r = [r for r in (1, 2, 5, 10, 12, 12.5, 13, 15, 20, 25, 30, 40, 50, 53, 54, 60, 100) if check(G.Params(radius_km=r))[0]]
print('  R values reproducing all folders:', good_r)
print('Sweep G (days) with R=25:')
good_g = [g for g in range(0, 25) if check(G.Params(gap_days=g))[0]]
print('  G values reproducing all folders:', good_g)
for g in (0, 12, 19):
    print(f'  G={g}:', check(G.Params(gap_days=g))[1], 'groups')

# ---- decisions against the real library index (names/sizes only)
print('\nDecision replay A: card == whole library (everything already imported)')
vg, pg, items, lib = G.run(raws, lib_files, 'Picture Offload/')
print('  actions:', sorted({(g.action, g.target) for g in vg}))
print('  statuses:', sorted({i.status for i in items}))
print('  watermark:', lib.watermark(timedelta(hours=-4)))

print('\nDecision replay B: library without Zachar Bay folder -> Zachar clips are new')
lf = [f for f in lib_files if 'Zachar Bay' not in f.rel]
vg, pg, items, lib = G.run(raws, lf, 'Picture Offload/')
for g in vg:
    if g.action != 'AlreadyImported':
        print('  ', g.action, g.target, g.confidence, len(g.items))

print('\nDecision replay C: library has Zachar 0123..0137 only; card has all 13 (append to today)')
lf = [f for f in lib_files if not ('Zachar Bay' in f.rel and int(f.name[-10:-6]) > 137)]
vg, pg, items, lib = G.run(raws, lf, 'Picture Offload/')
for g in vg:
    if g.action != 'AlreadyImported':
        print('  ', g.action, g.target, g.confidence, [i.status for i in g.items].count('New'), 'new')

print('\nDecision replay D: library lacks Anvil Mountain; Council Road leftovers on card (consecutive day, 53 km)')
lf = [f for f in lib_files if 'Anvil Mountain' not in f.rel]
vg, pg, items, lib = G.run(raws, lf, 'Picture Offload/')
for g in vg:
    if g.action != 'AlreadyImported':
        print('  ', g.action, g.target, g.confidence, len(g.items))
print('  Council Road location learned from:', lib.folders['2026/2026-07/2026-07-25 Council Road'].loc_source)

print('\nDecision replay E: as D but card has ONLY Anvil clips (no leftovers, no ledger)')
anvil = [r for r in raws if truth[r.id].endswith('Anvil Mountain')]
vg, pg, items, lib = G.run(anvil, lf, 'Picture Offload/')
for g in vg:
    print('  ', g.action, g.target, g.confidence, len(g.items))
