import numpy as np, sys, time, csv
def load(fn, classes=None):
    names=[];lat=[];lon=[];fc=[];fcode=[];pop=[];tz=[]
    with open(fn, encoding='utf-8') as f:
        for line in f:
            c=line.rstrip('\n').split('\t')
            if classes and c[6] not in classes: continue
            names.append(c[1]); lat.append(float(c[4])); lon.append(float(c[5])); fc.append(c[6]); fcode.append(c[7]); pop.append(int(c[14] or 0)); tz.append(c[17])
    return names, np.radians(np.array(lat)), np.radians(np.array(lon)), fc, fcode, pop, tz
def near(db, la, lo, k=4, maxkm=None):
    names, LA, LO, fc, fcode, pop, tz = db
    la, lo = np.radians(la), np.radians(lo)
    d = 2*6371*np.arcsin(np.sqrt(np.sin((LA-la)/2)**2 + np.cos(la)*np.cos(LA)*np.sin((LO-lo)/2)**2))
    idx = np.argsort(d)[:k]
    return [(names[i], fc[i]+'.'+fcode[i], round(float(d[i]),1), pop[i], tz[i]) for i in idx if maxkm is None or d[i]<=maxkm]
t=time.time(); cities=load('cities500.txt'); t1=time.time()-t
t=time.time(); us=load('US.txt', {'T','H','L','S','P','V'}); t2=time.time()-t
print(f"cities500 rows={len(cities[0])} load {t1:.1f}s ; US.txt T/H/L/S/P/V rows={len(us[0])} load {t2:.1f}s")
pts=[l.split('|') for l in sys.argv[1:]]
for name,la,lo in pts:
    la=float(la); lo=float(lo)
    print(f"\n### {name} ({la:.5f},{lo:.5f})")
    print("  cities500 nearest:", near(cities, la, lo, 3))
    for cls in ['P','T','H','S','L']:
        db=us; mask=[i for i,c in enumerate(db[3]) if c==cls]
        sub=(list(np.array(db[0],dtype=object)[mask]), db[1][mask], db[2][mask], [cls]*len(mask), list(np.array(db[4],dtype=object)[mask]), list(np.array(db[5])[mask]), list(np.array(db[6],dtype=object)[mask]))
        print(f"  US {cls} nearest:", near(sub, la, lo, 3))
