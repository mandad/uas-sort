import sys, os
sys.path.insert(0, '../grouping')
# reuse replay.py data loading without its prints: exec top portion
src = open('../grouping/replay.py').read().split("print('Default Params:'")[0]
g = {'__file__': os.path.abspath('../grouping/replay.py')}
exec(src, g)
G = g['G']; raws = g['raws']; lib_files = g['lib_files']; truth = g['truth']
R50 = 50*1.609344
p = G.Params(radius_km=R50)
print('Scenario D at 50 mi: library lacks Anvil; Council leftovers on card')
lf = [f for f in lib_files if 'Anvil Mountain' not in f.rel]
vg, pg, items, lib = G.run(raws, lf, 'Picture Offload/', p=p)
for grp in vg:
    if grp.action != 'AlreadyImported':
        st = {}
        for i in grp.items: st[(i.status, str(i.local_date))] = st.get((i.status, str(i.local_date)),0)+1
        print('  ', grp.action, grp.target, grp.confidence, len(grp.items), st)
print('Scenario C2 at 50 mi: library lacks Anvil AND Council; card has both (new)')
lf2 = [f for f in lib_files if 'Anvil Mountain' not in f.rel and 'Council Road' not in f.rel]
vg, pg, items, lib = G.run(raws, lf2, 'Picture Offload/', p=p)
for grp in vg:
    if grp.action != 'AlreadyImported':
        print('  ', grp.action, grp.target, grp.confidence, len(grp.items), grp.start, grp.end)
print('watermark', lib.watermark(G.timedelta(hours=-4)))
