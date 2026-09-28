#!/usr/bin/env python3
"""Calibration run: first-fix GPS + creation time for LOCAL library MP4s only.
Files whose Windows attributes include ReparsePoint(0x400, OneDrive placeholder),
Offline(0x1000) or RecallOnDataAccess(0x400000) are skipped without being opened."""
import itertools
import json
import math
import re
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from zoneinfo import ZoneInfo

sys.path.insert(0, __file__.rsplit('/', 1)[0])
from djmd_gps import read_mp4  # noqa: E402

WIN_ROOT = r'C:\Users\damia\OneDrive\Pictures\UAS Videos'
WSL_ROOT = '/mnt/c/Users/damia/OneDrive/Pictures/UAS Videos'
AK = ZoneInfo('America/Anchorage')
ET = ZoneInfo('America/New_York')
SKIP_MASK = 0x1000 | 0x400000   # Offline | RecallOnDataAccess (0x420 = hydrated placeholder is OK)

FOLDERS = [
    ('2026-07-03 Nome Roads', r'2026\2026-07\2026-07-03 Nome Roads', ['DJI_20260704014132_0110_D.MP4']),
    ('2026-07-25 Council Road', r'2026\2026-07\2026-07-25 Council Road', ['DJI_20260726022937_0118_D.MP4']),
    ('2026-07-26 Anvil Mountain', r'2026\2026-07\2026-07-26 Anvil Mountain', None),   # None = all
    ('2026-09-27 Zachar Bay', r'2026\2026-09\2026-09-27 Zachar Bay', None),
    ('2022-03-27 Makaha Valley', r'2022\2022-03-27 Makaha Valley', ['MAX_0065.MP4']),
]


def win_attrs(win_dir):
    ps = ("Get-ChildItem -LiteralPath '%s' -File -Force | ForEach-Object { '{0}|{1}' -f $_.Name,"
          "[int][System.IO.File]::GetAttributes($_.FullName) }" % win_dir)
    out = subprocess.run(['powershell.exe', '-NoProfile', '-Command', ps], capture_output=True,
                         text=True, cwd='/mnt/c', timeout=120).stdout
    res = {}
    for line in out.splitlines():
        line = line.strip()
        if '|' in line:
            n, a = line.rsplit('|', 1)
            res[n] = int(a)
    return res


def hav_km(a, b):
    R = 6371.0088
    la1, lo1, la2, lo2 = map(math.radians, (a[0], a[1], b[0], b[1]))
    h = math.sin((la2 - la1) / 2) ** 2 + math.cos(la1) * math.cos(la2) * math.sin((lo2 - lo1) / 2) ** 2
    return 2 * R * math.asin(math.sqrt(h))


def fname_clock(name):
    m = re.match(r'DJI_(\d{14})_', name)
    return datetime.strptime(m.group(1), '%Y%m%d%H%M%S') if m else None


rows = []
skipped = []
for label, wdir, only in FOLDERS:
    attrs = win_attrs(WIN_ROOT + '\\' + wdir)
    for name in sorted(attrs):
        if not name.upper().endswith('.MP4'):
            continue
        if only and name not in only:
            continue
        a = attrs[name]
        if a & SKIP_MASK:
            skipped.append((label, name, hex(a)))
            continue
        path = f"{WSL_ROOT}/{wdir.replace(chr(92), '/')}/{name}"
        r = read_mp4(path, last=True)
        r['folder'], r['name'], r['attr'] = label, name, hex(a)
        rows.append(r)

print('SKIPPED (placeholder/cloud attrs, not opened):')
for s in skipped:
    print('  ', *s)
print()
hdr = ('folder', 'file', 'creation_utc', 'local_AK', 'fname_minus_utc_h', 'dur_s', 'lat', 'lon',
       'alt_m', 'clip_travel_km', 'ms', 'bytes')
print('| ' + ' | '.join(hdr) + ' |')
print('|' + '---|' * len(hdr))
for r in rows:
    ct = datetime.fromisoformat(r['mvhd_creation_utc']) if r.get('mvhd_creation_utc') else None
    loc = ct.astimezone(AK).strftime('%Y-%m-%d %H:%M:%S') if ct else ''
    fc = fname_clock(r['name'])
    off = round((fc - ct.replace(tzinfo=None)).total_seconds() / 3600, 2) if (fc and ct) else ''
    travel = ''
    if 'last_lat' in r:
        travel = f"{hav_km((r['lat'], r['lon']), (r['last_lat'], r['last_lon'])):.2f}"
    print('| ' + ' | '.join(str(x) for x in (
        r['folder'], r['name'], ct.strftime('%Y-%m-%d %H:%M:%SZ') if ct else '', loc, off,
        r.get('duration_s', ''), f"{r['lat']:.5f}" if 'lat' in r else (r.get('error') or ''),
        f"{r['lon']:.5f}" if 'lon' in r else '', r.get('abs_alt_m', ''), travel,
        r['elapsed_ms'], r['bytes_read'])) + ' |')

json.dump(rows, open(__file__.rsplit('/', 1)[0] + '/calibration.json', 'w'), indent=1, default=str)

# ---- per-folder stats
print('\nPER-FOLDER (first fixes):')
cents = {}
spans = {}
for label, grp in itertools.groupby([r for r in rows if 'lat' in r], key=lambda r: r['folder']):
    g = list(grp)
    pts = [(r['lat'], r['lon']) for r in g] + [(r['last_lat'], r['last_lon']) for r in g if 'last_lat' in r]
    firsts = [(r['lat'], r['lon']) for r in g]
    maxd_first = max((hav_km(a, b) for a, b in itertools.combinations(firsts, 2)), default=0.0)
    maxd_all = max((hav_km(a, b) for a, b in itertools.combinations(pts, 2)), default=0.0)
    gt = [r for r in g if r.get('mvhd_creation_utc')]   # truncated files have GPS but no mvhd
    starts = [datetime.fromisoformat(r['mvhd_creation_utc']) for r in gt]
    ends = [s + timedelta(seconds=r['duration_s']) for s, r in zip(starts, gt)]
    c = (sum(p[0] for p in firsts) / len(firsts), sum(p[1] for p in firsts) / len(firsts))
    cents[label] = c
    spans[label] = (min(starts), max(ends))
    print(f'  {label}: n={len(g)} centroid=({c[0]:.5f},{c[1]:.5f}) max pairwise first-fix={maxd_first:.3f} km, '
          f'incl. last-fixes={maxd_all:.3f} km, span {min(starts).astimezone(AK):%m-%d %H:%M}..'
          f'{max(ends).astimezone(AK):%m-%d %H:%M} AKDT = {(max(ends) - min(starts)).total_seconds() / 60:.1f} min')

# ---- inter-folder
# Kodiak proxy: PANO_0001.DNG 57.7996N 152.3902W, DateTimeOriginal 2026:05:25 09:30:28 drone clock (EDT)
kod_t = datetime(2026, 5, 25, 9, 30, 28, tzinfo=ET).astimezone(timezone.utc)
cents = {'2026-05-22 Kodiak (PANO proxy)': (57.7996, -152.3902), **cents}
spans = {'2026-05-22 Kodiak (PANO proxy)': (kod_t, kod_t), **spans}
order = ['2026-05-22 Kodiak (PANO proxy)', '2026-07-03 Nome Roads', '2026-07-25 Council Road',
         '2026-07-26 Anvil Mountain', '2026-09-27 Zachar Bay']
order = [o for o in order if o in cents]
print('\nINTER-FOLDER centroid distance matrix (km):')
print('| | ' + ' | '.join(o.split(' ', 1)[1] for o in order) + ' |')
print('|---|' + '---|' * len(order))
for a in order:
    print(f"| {a.split(' ', 1)[1]} | " + ' | '.join(f'{hav_km(cents[a], cents[b]):.1f}' for b in order) + ' |')
print('\nCONSECUTIVE (by time): distance, gap (end of prev sample set -> start of next)')
for a, b in zip(order, order[1:]):
    gap = spans[b][0] - spans[a][1]
    print(f'  {a} -> {b}: {hav_km(cents[a], cents[b]):.1f} km, gap {gap.total_seconds() / 3600:.1f} h '
          f'({gap.total_seconds() / 86400:.2f} d)')
