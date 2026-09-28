# Does a tighter radius for day-to-day links (R_day) still reproduce the user's 8 folders?
import sys, os
sys.path.insert(0, '../grouping')
src = open('../grouping/replay.py').read().split('def partition')[0]
__file__ = os.path.abspath('../grouping/replay.py'); exec(src)
import grouping as G
def cl(items, p, rday):
    groups, cur = [], None
    for x in sorted(items, key=lambda i: (i.utc, i.id)):
        prev = cur.items[-1] if cur else None
        r = p.radius_km if (prev is None or prev.local_date == x.local_date) else rday
        if cur is None or G.time_split(prev, x, p) or (x.has_gps and cur.centroid is not None and G.haversine_km(x.loc, cur.centroid) > r):
            cur = G.Group(); groups.append(cur)
        cur.add(x)
    return groups
part = lambda gs: sorted(sorted(i.id for i in g.items) for g in gs)
want = sorted(sorted(v) for v in {fo: [n for n, f in truth.items() if f == fo] for fo in set(truth.values())}.values())
items = [i for i in G.normalize(raws, G.Params(), 'America/Anchorage') if i.kind == 'video']
for rday in (2, 5, 10, 14, 25):
    print('R_day', rday, part(cl(items, G.Params(), rday)) == want)
# multi-day folders with GPS on both days?
from collections import defaultdict
d = defaultdict(set)
for i in items:
    if i.has_gps: d[truth[i.id]].add(i.local_date)
print({k.split('/')[-1]: sorted(str(x) for x in v) for k, v in d.items()})
