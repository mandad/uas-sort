#!/usr/bin/env python3
"""uas-sort: reference implementation of the grouping + newness design (throwaway spike).

Pure functions only: no file I/O here.  Metadata readers (djmd GPS, EXIF, directory
listings) produce RawItem / LibFile records; everything below is deterministic and
unit-testable.  Mirrors the C# design 1:1 so the tests can be ported.
"""
from __future__ import annotations

import math
import re
from dataclasses import dataclass, field
from datetime import date, datetime, timedelta, timezone
from typing import Optional
from zoneinfo import ZoneInfo

UTC = timezone.utc


# ----------------------------------------------------------------------------- params
@dataclass
class Params:
    gap_days: int = 1             # G: join if local-date gap <= G (1 => consecutive days join)
    radius_km: float = 25.0       # R: split if a GPS item is > R km from the group's centroid
    min_split_hours: float = 3.0  # H: a date-gap split also needs > H hours since previous item
    photo_attach_days: int = 1    # photos attach to a video group within +-this many local days
    offset_max_age_days: int = 60 # a learned drone-clock offset sample is used only within this
    dup_size_min: int = 1 << 20   # size-only "possible duplicate" check only for files >= 1 MiB


# ----------------------------------------------------------------------------- geo
def haversine_km(a, b) -> float:
    la1, lo1, la2, lo2 = map(math.radians, (a[0], a[1], b[0], b[1]))
    h = math.sin((la2 - la1) / 2) ** 2 + math.cos(la1) * math.cos(la2) * math.sin((lo2 - lo1) / 2) ** 2
    return 2 * 6371.0088 * math.asin(min(1.0, math.sqrt(h)))


class Centroid:
    """Mean of unit vectors (safe across the antimeridian / poles)."""
    def __init__(self):
        self.x = self.y = self.z = 0.0
        self.n = 0

    def add(self, lat, lon):
        la, lo = math.radians(lat), math.radians(lon)
        self.x += math.cos(la) * math.cos(lo); self.y += math.cos(la) * math.sin(lo); self.z += math.sin(la)
        self.n += 1

    def get(self):
        if not self.n:
            return None
        hyp = math.hypot(self.x, self.y)
        return (math.degrees(math.atan2(self.z, hyp)), math.degrees(math.atan2(self.y, self.x)))


def tz_lookup_stub(lat, lon) -> Optional[str]:
    """Stand-in for GeoTimeZone.TimeZoneLookup.GetTimeZone(lat, lon).Result (IANA id)."""
    if lat is None:
        return None
    if 18 < lat < 23 and -161 < lon < -154:
        return 'Pacific/Honolulu'
    if lat > 51 and lon < -130:
        return 'America/Anchorage'
    if lon > -85:
        return 'America/New_York'
    return None


# ----------------------------------------------------------------------------- raw + item models
DJI_NAME = re.compile(r'^DJI_(\d{14})_(\d{4})_[A-Z]\.(MP4|MOV|DNG|JPG)$', re.I)


def drone_clock_from_name(name: str) -> Optional[datetime]:
    m = DJI_NAME.match(name)
    return datetime.strptime(m.group(1), '%Y%m%d%H%M%S') if m else None


@dataclass
class RawItem:
    """What the metadata readers hand to the pure core (card item)."""
    id: str                               # card-relative path, unique on the card
    kind: str                             # 'video' | 'photo' | 'set' (pano/hyperlapse set = one unit)
    name: str                             # file name (for a set: set folder name, e.g. 001_0087)
    size: int                             # bytes (set: sum)
    maker: str                            # 'dji' | 'autel' | 'other'
    mtime_utc: datetime
    mvhd_utc: Optional[datetime] = None   # MP4 creation_time as written (DJI: true UTC; Autel: wall clock)
    exif_dto: Optional[datetime] = None   # naive DateTimeOriginal (DJI: drone clock)
    exif_offset: Optional[timedelta] = None  # OffsetTimeOriginal when present
    gps_utc: Optional[datetime] = None    # real GPS time (EXIF GPSDateStamp or djmd 3-x-x-6-1) if any
    lat: Optional[float] = None
    lon: Optional[float] = None
    session_utc: Optional[datetime] = None  # power-on instant = creation - uptime (DJI djmd)
    members: tuple = ()                   # for kind == 'set': ((name, size), ...)


@dataclass
class Item:
    raw: RawItem
    utc: datetime                         # CaptureUtc (floating items: best estimate)
    time_source: str
    floating: Optional[datetime] = None   # wall clock for items with no zone info (Autel)
    tz: Optional[str] = None
    tz_source: str = ''
    local_date: Optional[date] = None
    status: str = ''                      # see newness()
    reason: str = ''
    lib_folder: Optional[str] = None      # event folder rel path when already in library

    @property
    def id(self): return self.raw.id
    @property
    def kind(self): return self.raw.kind
    @property
    def has_gps(self): return self.raw.lat is not None
    @property
    def loc(self): return (self.raw.lat, self.raw.lon)


# ----------------------------------------------------------------------------- 1. normalisation
def round_offset(td: timedelta) -> timedelta:
    q = 15 * 60
    return timedelta(seconds=round(td.total_seconds() / q) * q)


def learn_offsets(raws):
    """Drone-clock offset samples: (drone_clock_start, offset) from DJI MP4s with a moov."""
    out = []
    for r in raws:
        dc = drone_clock_from_name(r.name)
        if r.kind == 'video' and r.maker == 'dji' and r.mvhd_utc and dc:
            out.append((dc, round_offset(dc - r.mvhd_utc.replace(tzinfo=None))))
    return sorted(out)


def offset_for(dc: datetime, samples, setting: Optional[timedelta], p: Params):
    if samples:
        best = min(samples, key=lambda s: abs(s[0] - dc))
        if abs(best[0] - dc) <= timedelta(days=p.offset_max_age_days):
            return best[1], 'learned'
        # all samples far away: still prefer the card's mode over the stored setting
        offs = [s[1] for s in samples]
        return max(set(offs), key=offs.count), 'learned-mode'
    if setting is not None:
        return setting, 'setting'
    return None, None


def normalize(raws, p: Params, pc_tz: str, clock_setting: Optional[timedelta] = None,
              tz_lookup=tz_lookup_stub):
    samples = learn_offsets(raws)
    items = []
    for r in raws:
        floating = None
        dc = r.exif_dto if r.exif_dto else drone_clock_from_name(r.name)
        if r.kind == 'video' and r.maker == 'dji' and r.mvhd_utc:
            utc, src = r.mvhd_utc, 'mvhd'
        elif r.gps_utc:
            utc, src = r.gps_utc, 'gps'
        elif r.exif_dto and r.exif_offset is not None:
            utc, src = (r.exif_dto - r.exif_offset).replace(tzinfo=UTC), 'exif+OffsetTime'
        elif r.maker == 'dji' and dc and offset_for(dc, samples, clock_setting, p)[0] is not None:
            off, how = offset_for(dc, samples, clock_setting, p)
            utc, src = (dc - off).replace(tzinfo=UTC), f'droneclock+{how}'
        elif r.maker == 'autel' and r.mvhd_utc:
            floating, src = r.mvhd_utc.replace(tzinfo=None), 'autel-mvhd-floating'
            utc = None
        elif r.maker == 'autel' or r.maker == 'other':
            # FAT/exFAT without zone: Windows turned the naive stamp into UTC with the PC zone
            floating, src = r.mtime_utc.astimezone(ZoneInfo(pc_tz)).replace(tzinfo=None), 'mtime-floating'
            utc = None
        else:
            utc, src = r.mtime_utc, 'mtime'
        it = Item(raw=r, utc=utc, time_source=src, floating=floating)
        if r.lat is not None:
            it.tz, it.tz_source = tz_lookup(r.lat, r.lon), 'gps'
        items.append(it)

    # zone inheritance for items without GPS: same session first, then nearest GPS item within 12 h
    gps_items = [i for i in items if i.tz]
    for it in items:
        if it.tz:
            continue
        cand = [g for g in gps_items if it.raw.session_utc and g.raw.session_utc == it.raw.session_utc]
        if not cand and it.utc:
            near = [g for g in gps_items if g.utc and abs(g.utc - it.utc) <= timedelta(hours=12)]
            cand = sorted(near, key=lambda g: abs(g.utc - it.utc))[:1]
        if cand:
            it.tz, it.tz_source = cand[0].tz, 'inherited'
        else:
            it.tz, it.tz_source = pc_tz, 'pc'

    for it in items:
        z = ZoneInfo(it.tz)
        if it.floating is not None:
            it.local_date = it.floating.date()
            it.utc = it.floating.replace(tzinfo=z).astimezone(UTC)  # estimate, for ordering only
        else:
            it.local_date = it.utc.astimezone(z).date()
    return items


# ----------------------------------------------------------------------------- 2. clustering
@dataclass
class Group:
    items: list = field(default_factory=list)
    cen: Centroid = field(default_factory=Centroid)
    action: str = ''
    target: Optional[str] = None
    confidence: str = ''
    photos: list = field(default_factory=list)

    def add(self, it):
        self.items.append(it)
        if it.has_gps:
            self.cen.add(*it.loc)

    def remove(self, it):
        self.items.remove(it)
        if it.has_gps:
            self._rebuild()

    def _rebuild(self):
        self.cen = Centroid()
        for i in self.items:
            if i.has_gps:
                self.cen.add(*i.loc)

    @property
    def centroid(self): return self.cen.get()
    @property
    def start(self): return min(i.local_date for i in self.items)
    @property
    def end(self): return max(i.local_date for i in self.items)
    @property
    def t0(self): return min(i.utc for i in self.items)
    @property
    def t1(self): return max(i.utc for i in self.items)
    @property
    def gps_sessions(self): return {i.raw.session_utc for i in self.items if i.raw.session_utc and i.has_gps}


def time_split(prev: Item, x: Item, p: Params) -> bool:
    return ((x.local_date - prev.local_date).days > p.gap_days
            and (x.utc - prev.utc) > timedelta(hours=p.min_split_hours))


def cluster(items, p: Params):
    """Sequential clustering in CaptureUtc order.  New group when
       (a) local-date gap to the previous item > G (and > H hours), or
       (b) x has GPS, the group has a centroid, x is not in a power-on session already in
           the group, and distance(x, centroid) > R.
       No-GPS items never trigger (b); trailing no-GPS items are re-homed to the nearer
       side (session match first, then time) when (b) fires."""
    groups, cur, tail = [], None, []   # tail = no-GPS items after cur's last GPS item
    last_gps = None
    for x in sorted(items, key=lambda i: (i.utc, i.id)):
        if cur is None:
            cur = Group(); cur.add(x); groups.append(cur)
            tail, last_gps = ([] if x.has_gps else [x]), (x if x.has_gps else None)
            continue
        prev = cur.items[-1]
        if time_split(prev, x, p):
            cur = Group(); cur.add(x); groups.append(cur)
            tail, last_gps = ([] if x.has_gps else [x]), (x if x.has_gps else None)
            continue
        if (x.has_gps and cur.centroid is not None
                and not (x.raw.session_utc and x.raw.session_utc in cur.gps_sessions)
                and haversine_km(x.loc, cur.centroid) > p.radius_km):
            new = Group()
            for t in tail:   # re-home trailing no-GPS items
                if t.raw.session_utc and t.raw.session_utc == x.raw.session_utc:
                    move = True
                elif t.raw.session_utc and last_gps and t.raw.session_utc == last_gps.raw.session_utc:
                    move = False
                else:
                    move = last_gps is None or (x.utc - t.utc) < (t.utc - last_gps.utc)
                if move:
                    cur.remove(t); new.add(t)
            if not cur.items:
                groups.remove(cur)
            new.add(x); new.items.sort(key=lambda i: (i.utc, i.id))
            cur = new; groups.append(cur); tail, last_gps = [], x
            continue
        cur.add(x)
        if x.has_gps:
            tail, last_gps = [], x
        else:
            tail.append(x)
    return groups


def attach_photos(video_groups, photos, p: Params):
    """Photos never move video-group boundaries.  A photo joins the video group with the
       smallest time distance whose local-date window (start-A .. end+A) contains it and
       whose centroid is within R (or unknown / photo has no GPS).  The rest form
       photo-only groups using the same cluster() rules."""
    left = []
    for ph in photos:
        best, bestkey = None, None
        for g in video_groups:
            if not (g.start - timedelta(days=p.photo_attach_days) <= ph.local_date
                    <= g.end + timedelta(days=p.photo_attach_days)):
                continue
            c = g.centroid
            d = haversine_km(ph.loc, c) if (ph.has_gps and c) else 0.0
            if d > p.radius_km:
                continue
            dt = max(timedelta(0), g.t0 - ph.utc, ph.utc - g.t1)
            key = (dt, d)
            if bestkey is None or key < bestkey:
                best, bestkey = g, key
        if best:
            best.photos.append(ph)
        else:
            left.append(ph)
    return cluster(left, p)


# ----------------------------------------------------------------------------- 3. library index
EVENT_DIR = re.compile(r'^(\d{4})-(\d{2})-(\d{2})(?:\s+(.*))?$')


@dataclass
class LibFile:
    rel: str            # path relative to its root, '/'-separated
    size: int
    mtime_utc: datetime
    root: str = 'video' # 'video' | 'photo'

    @property
    def name(self): return self.rel.rsplit('/', 1)[-1]


@dataclass
class EventFolder:
    rel: str
    name_date: date
    desc: str
    files: list = field(default_factory=list)
    centroid: Optional[tuple] = None
    loc_source: str = ''


@dataclass
class LedgerRec:
    name: str
    size: int
    utc: datetime
    kind: str
    folder: Optional[str]           # event folder rel (videos) / photo-root rel dir
    lat: Optional[float] = None
    lon: Optional[float] = None
    tz: Optional[str] = None


class Library:
    def __init__(self, files, photo_root_prefix: Optional[str], ledger=()):
        self.files = files
        self.ledger = list(ledger)
        self.by_key, self.by_name, self.by_size = {}, {}, {}
        self.folders = {}
        for f in files:
            k = f.name.lower()
            self.by_key.setdefault((k, f.size), []).append(f)
            self.by_name.setdefault(k, []).append(f)
            self.by_size.setdefault(f.size, []).append(f)
            if f.root != 'video' or (photo_root_prefix and f.rel.startswith(photo_root_prefix)):
                continue
            parts = f.rel.split('/')[:-1]
            for i in range(len(parts) - 1, -1, -1):     # nearest dated ancestor, any depth
                m = EVENT_DIR.match(parts[i])
                if m:
                    rel = '/'.join(parts[:i + 1])
                    ef = self.folders.get(rel)
                    if ef is None:
                        ef = self.folders[rel] = EventFolder(
                            rel, date(int(m[1]), int(m[2]), int(m[3])), (m[4] or '').strip())
                    ef.files.append(f)
                    break
        self.ledger_key = {(r.name.lower(), r.size): r for r in self.ledger}
        for rel, ef in self.folders.items():
            c = Centroid()
            for r in self.ledger:
                if r.folder == rel and r.lat is not None:
                    c.add(r.lat, r.lon)
            if c.n:
                ef.centroid, ef.loc_source = c.get(), 'ledger'

    def folder_of(self, f: LibFile) -> Optional[str]:
        for rel, ef in self.folders.items():
            if f in ef.files:
                return rel
        return None

    def learn_locations_from_card(self, items):
        """Card copies of already-imported videos tell us where a library folder was shot."""
        acc = {}
        for it in items:
            if it.lib_folder and it.has_gps:
                acc.setdefault(it.lib_folder, Centroid()).add(*it.loc)
        for rel, c in acc.items():
            ef = self.folders.get(rel)
            if ef and ef.centroid is None:
                ef.centroid, ef.loc_source = c.get(), 'card-leftovers'

    def folder_range(self, ef: EventFolder, tz: str, offset: timedelta):
        ds = self.folder_dates(ef, tz, offset)
        return min(ds), max(ds)

    def folder_dates(self, ef: EventFolder, tz: str, offset: timedelta):
        """Local dates of a library folder, from NAMES (drone clock - offset) + ledger only."""
        z = ZoneInfo(tz)
        ds = [ef.name_date]
        for f in ef.files:
            dc = drone_clock_from_name(f.name)
            if dc:
                ds.append((dc - offset).replace(tzinfo=UTC).astimezone(z).date())
        for r in self.ledger:
            if r.folder == ef.rel:
                ds.append(r.utc.astimezone(ZoneInfo(r.tz or tz)).date())
        return set(ds)

    def watermark(self, offset: timedelta) -> Optional[datetime]:
        """Latest start instant of any already-imported video (library names or ledger)."""
        ts = []
        for f in self.files:
            dc = drone_clock_from_name(f.name)
            if dc and f.name.upper().endswith(('.MP4', '.MOV')):
                ts.append((dc - offset).replace(tzinfo=UTC))
        ts += [r.utc for r in self.ledger]
        return max(ts) if ts else None


# ----------------------------------------------------------------------------- 5. newness
def classify_video(it: Item, lib: Library, p: Params):
    k = (it.raw.name.lower(), it.raw.size)
    if k in lib.by_key:
        f = next((f for f in lib.by_key[k] if f.root == 'video' and lib.folder_of(f)), lib.by_key[k][0])
        it.status, it.reason, it.lib_folder = 'Imported', 'name+size in library', lib.folder_of(f)
    elif k in lib.ledger_key:
        it.status, it.reason = 'RemovedFromLibrary', 'ledger says offloaded; not in library now'
        it.lib_folder = lib.ledger_key[k].folder
    elif it.raw.name.lower() in lib.by_name:
        it.status, it.reason = 'Conflict', 'same name, different size in library'
    elif it.raw.size >= p.dup_size_min and it.raw.size in lib.by_size:
        it.status, it.reason = 'PossibleDuplicate', 'same size as ' + lib.by_size[it.raw.size][0].rel
    else:
        it.status, it.reason = 'New', ''


def classify_photo(it: Item, lib: Library, photo_root_prefix: str, new_video_dates, watermark,
                   imported_video_dates=frozenset()):
    r = it.raw
    names = [(m[0], m[1]) for m in r.members] if r.kind == 'set' else [(r.name, r.size)]
    in_led = all((n.lower(), s) in lib.ledger_key for n, s in names)
    in_root = all(any(f.root == 'photo' or f.rel.startswith(photo_root_prefix)
                      for f in lib.by_key.get((n.lower(), s), [])) for n, s in names)
    if r.kind == 'set':   # PANO_000N repeats across sets: require the set folder too
        in_root = in_root and all(any(('/' + r.name + '/') in ('/' + f.rel)
                                      for f in lib.by_key.get((n.lower(), s), [])) for n, s in names)
    if in_led:
        it.status, it.reason = 'Imported', 'ledger'
    elif in_root:
        it.status, it.reason = 'Imported', 'present in photo root'
    elif watermark is not None and it.utc > watermark:
        it.status, it.reason = 'New', 'after last imported video'
    elif it.local_date in new_video_dates:
        it.status, it.reason = 'New', 'same local date as new videos'
    else:
        it.status = 'ProbablyImported'
        it.reason = ('no record; imported videos exist that day' if it.local_date in imported_video_dates
                     else 'no record; no videos that day and before the watermark (photo-only day)')


# ----------------------------------------------------------------------------- 3/4. decisions
INVALID = re.compile(r'[<>:"/\\|?*\x00-\x1f]')


def sanitize_desc(desc: str) -> str:
    s = INVALID.sub(' ', desc)
    s = re.sub(r'\s+', ' ', s).strip().rstrip('. ').strip()
    return s[:80].rstrip('. ')


def proposed_rel(start: date, desc: str) -> str:
    d = sanitize_desc(desc)
    leaf = f'{start:%Y-%m-%d}' + (f' {d}' if d else '')
    return f'{start:%Y}/{start:%Y-%m}/{leaf}'


def decide(groups, lib: Library, p: Params, offset: timedelta):
    for g in groups:
        vids = g.items
        new = [v for v in vids if v.status in ('New', 'Conflict', 'PossibleDuplicate')]
        if not any(v.status == 'New' for v in vids):
            g.action = 'AlreadyImported' if not new else 'Review'
            g.target = next((v.lib_folder for v in vids if v.lib_folder), None)
            continue
        folders = sorted({v.lib_folder for v in vids if v.status == 'Imported' and v.lib_folder})
        if len(folders) == 1:
            g.action, g.target, g.confidence = 'Append', folders[0], 'high (same group as imported clips)'
            continue
        if len(folders) > 1:   # user already split this cluster: follow the user's boundaries
            imp = [v for v in vids if v.status == 'Imported' and v.lib_folder]
            g.action, g.confidence = 'AppendSplit', 'high (user split this cluster before)'
            g.target = {v.id: min(imp, key=lambda i: (abs(i.utc - v.utc), i.utc)).lib_folder for v in new}
            continue
        tz = next((v.tz for v in vids if v.tz_source == 'gps'), vids[0].tz)
        gs = min(v.local_date for v in vids if v.status == 'New')
        ge = max(v.local_date for v in vids if v.status == 'New')
        best = None
        for ef in lib.folders.values():
            f0, f1 = lib.folder_range(ef, tz, offset)
            if not (f0 <= gs <= f1 + timedelta(days=p.gap_days)):
                continue            # never prepend before the folder's name date
            c = g.centroid
            if ef.centroid and c:
                dist = haversine_km(ef.centroid, c)
                if dist > p.radius_km:
                    continue
                conf = 'high (date + location)'
            elif gs <= f1:          # date overlap, location unknown on one side
                dist, conf = float('inf'), 'medium (same local date, location unknown)'
            else:                   # only adjacent, location unknown -> offer, don't default
                continue
            key = ((gs - f1).days if gs > f1 else 0, dist)
            if best is None or key < best[0]:
                best = (key, ef, conf)
        if best:
            g.action, g.target, g.confidence = 'Append', best[1].rel, best[2]
        else:
            g.action, g.target, g.confidence = 'NewFolder', proposed_rel(gs, ''), ''


def run(raws, lib_files, photo_root_prefix, p=Params(), pc_tz='America/Anchorage',
        clock_setting=None, ledger=()):
    items = normalize(raws, p, pc_tz, clock_setting)
    lib = Library(lib_files, photo_root_prefix, ledger)
    samples = learn_offsets(raws)
    offset = (samples[-1][1] if samples else clock_setting) or timedelta(0)
    videos = [i for i in items if i.kind == 'video']
    photos = [i for i in items if i.kind != 'video']
    for v in videos:
        classify_video(v, lib, p)
    lib.learn_locations_from_card(videos)
    # dates that already have imported videos (only used to word the photo 'reason')
    imported_dates = set()
    for ef in lib.folders.values():
        imported_dates |= lib.folder_dates(ef, pc_tz, offset)
    new_dates = {v.local_date for v in videos if v.status == 'New'}
    wm = lib.watermark(offset)
    for ph in photos:
        classify_photo(ph, lib, photo_root_prefix or '\0', new_dates, wm, imported_dates)
    vgroups = cluster(videos, p)
    pgroups = attach_photos(vgroups, photos, p)
    decide(vgroups, lib, p, offset)
    for g in pgroups:
        g.action = 'PhotosOnly'
    return vgroups, pgroups, items, lib
