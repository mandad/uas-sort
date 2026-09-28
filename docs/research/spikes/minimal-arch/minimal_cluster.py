"""Check: does the MINIMAL clusterer (time split + centroid radius only; no power-on-session
rule, no re-homing of trailing no-GPS items) still reproduce the user's folders on the replay?
Library access = os.walk/stat only (metadata), same as grouping/replay.py."""
import sys, os
from datetime import timedelta
sys.path.insert(0, '../grouping')
src = open('../grouping/replay.py').read().split('def partition')[0]
__file__ = os.path.abspath('../grouping/replay.py'); exec(src)          # builds raws, truth, want (metadata-only listing + GPS from calibration jsons)
import grouping as G

def minimal_cluster(items, p):
    groups, cur = [], None
    for x in sorted(items, key=lambda i: (i.utc, i.id)):
        if cur is None or G.time_split(cur.items[-1], x, p) or (
                x.has_gps and cur.centroid is not None and G.haversine_km(x.loc, cur.centroid) > p.radius_km):
            cur = G.Group(); groups.append(cur)
        cur.add(x)
    return groups

def partition(groups): return sorted(sorted(i.id for i in g.items) for g in groups)
want_part = sorted(sorted(v) for v in {fo: [n for n, f in truth.items() if f == fo] for fo in set(truth.values())}.values())

def check(p):
    items = G.normalize(raws, p, 'America/Anchorage')
    gs = minimal_cluster([i for i in items if i.kind == 'video'], p)
    return partition(gs) == want_part, len(gs)

print('folders:', len(want_part), ' default:', check(G.Params()))
print('R ok:', [r for r in (5, 10, 12, 12.5, 13, 15, 20, 25, 30, 40, 50, 53, 54, 60) if check(G.Params(radius_km=r))[0]])
print('G ok:', [g for g in range(0, 20) if check(G.Params(gap_days=g))[0]])
nogps = [r.id for r in raws if r.lat is None]
print('clips without GPS in replay:', len(nogps), sorted({truth[n].split('/')[-1] for n in nogps}))
