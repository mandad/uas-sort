#!/usr/bin/env python3
"""Read first/last djmd GPS for library folders NOT covered by djmd/calibration.json.
Skips any file whose Windows attributes have Offline(0x1000) or RecallOnDataAccess(0x400000)."""
import json, subprocess, sys, os
sys.path.insert(0, '/tmp/claude-1000/-mnt-c-dev-uas-sort/187b6b83-e3e1-4971-8c9e-c13c3990d115/scratchpad/spikes/djmd')
from djmd_gps import read_mp4
WIN_ROOT = r'C:\Users\damia\OneDrive\Pictures\UAS Videos'
WSL_ROOT = '/mnt/c/Users/damia/OneDrive/Pictures/UAS Videos'
SKIP = 0x1000 | 0x400000
FOLDERS = [r'2026\2026-05\2026-05-10 Newport, RI', r'2026\2026-06\2026-06-12 Safety Roadhouse',
           r'2026\2026-07\2026-07-03 Nome Roads', r'2026\2026-07\2026-07-25 Council Road',
           r'2026\2026-05\2026-05-22 Kodiak']
out = []
for f in FOLDERS:
    ps = ("Get-ChildItem -LiteralPath '%s\\%s' -File -Force | ForEach-Object { '{0}|{1}' -f $_.Name,"
          "[int][System.IO.File]::GetAttributes($_.FullName) }" % (WIN_ROOT, f))
    res = subprocess.run(['powershell.exe', '-NoProfile', '-Command', ps], capture_output=True, text=True, cwd='/mnt/c').stdout
    for line in res.splitlines():
        line = line.strip()
        if '|' not in line: continue
        name, attr = line.rsplit('|', 1); attr = int(attr)
        rec = {'folder': f.split('\\')[-1], 'name': name, 'attr': hex(attr)}
        if attr & SKIP or not name.upper().endswith('.MP4'):
            rec['skipped'] = True
        else:
            p = os.path.join(WSL_ROOT, *f.split('\\'), name)
            r = read_mp4(p, last=True)
            for k in ('mvhd_creation_utc', 'duration_s', 'lat', 'lon', 'last_lat', 'last_lon', 'bytes_read', 'elapsed_ms', 'error'):
                if k in r: rec[k] = r[k]
        out.append(rec)
json.dump(out, open('more_gps.json', 'w'), indent=1)
for r in out:
    print(r['folder'][:22].ljust(22), r['name'], r['attr'], 'SKIP' if r.get('skipped') else (r.get('mvhd_creation_utc'), r.get('lat'), r.get('lon'), r.get('last_lat'), r.get('last_lon'), r.get('error')))
