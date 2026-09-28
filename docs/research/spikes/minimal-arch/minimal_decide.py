"""Minimal decide: library-folder boundaries become hard cluster boundaries (replaces AppendSplit);
then Append(the one folder) / date+location match / NewFolder.  Replays scenarios B-E of replay.py."""
import sys, os
from datetime import timedelta
sys.path.insert(0, '../grouping')
src = open('../grouping/replay.py').read().split('def partition')[0]
__file__ = os.path.abspath('../grouping/replay.py'); exec(src)
import grouping as G
P = G.Params(); OFF = timedelta(hours=-4)

def cluster(items, p):
    groups, cur = [], None
    for x in sorted(items, key=lambda i: (i.utc, i.id)):
        cur_folders = {i.lib_folder for i in cur.items if i.lib_folder} if cur else set()
        if (cur is None or G.time_split(cur.items[-1], x, p)
            or (x.has_gps and cur.centroid is not None and G.haversine_km(x.loc, cur.centroid) > p.radius_km)
            or (x.lib_folder and cur_folders and x.lib_folder not in cur_folders)):
            cur = G.Group(); groups.append(cur)
        cur.add(x)
    return groups

def decide(g, lib):
    if not any(v.status == 'New' for v in g.items): return ('AlreadyImported', None)
    fs = {v.lib_folder for v in g.items if v.lib_folder}
    if fs: return ('Append', fs.pop())
    new = [v for v in g.items if v.status == 'New']
    gs = min(v.local_date for v in new); tz = next((v.tz for v in new if v.tz), 'America/Anchorage')
    best = None
    for ef in lib.folders.values():
        f0, f1 = lib.folder_range(ef, tz, OFF)
        if not (f0 <= gs <= f1 + timedelta(days=P.gap_days)): continue
        c = g.centroid
        if ef.centroid and c:
            if G.haversine_km(ef.centroid, c) > P.radius_km: continue
        elif gs > f1: continue
        k = (gs - f1).days if gs > f1 else 0
        if best is None or k < best[0]: best = (k, ef.rel)
    return ('Append', best[1]) if best else ('NewFolder', G.proposed_rel(gs, ''))

def run(card, lf, label):
    items = G.normalize(card, P, 'America/Anchorage')
    lib = G.Library(lf, 'Picture Offload/')
    vids = [i for i in items if i.kind == 'video']
    for v in vids: G.classify_video(v, lib, P)
    lib.learn_locations_from_card(vids)
    out = [(decide(g, lib), len(g.items), sum(v.status == 'New' for v in g.items)) for g in cluster(vids, P)]
    print(label); [print('   ', d, 'n=%d new=%d' % (n, k)) for d, n, k in out if d[0] != 'AlreadyImported']
    print('    groups:', len(out))

run(raws, lib_files, 'A: card == library')
run(raws, [f for f in lib_files if 'Zachar Bay' not in f.rel], 'B: no Zachar folder')
run(raws, [f for f in lib_files if not ('Zachar Bay' in f.rel and int(f.name[-10:-6]) > 137)], 'C: Zachar partly imported')
lf = [f for f in lib_files if 'Anvil Mountain' not in f.rel]
run(raws, lf, 'D: no Anvil folder, Council leftovers on card')
run([r for r in raws if truth[r.id].endswith('Anvil Mountain')], lf, 'E: only Anvil clips, no leftovers')
