#!/usr/bin/env python3
"""
Throwaway spike: read GPS lat/lon from the FIRST djmd sample of a DJI MP4
without scanning the file.  Also returns mvhd/mdhd creation time (UTC).

Strategy
  1. Walk top-level boxes by seeking over their payloads (mdat is never read).
  2. Inside moov, walk children lazily (header reads + seeks only); descend
     into trak/mdia/minf/stbl; read only small leaf boxes (mvhd, mdhd, hdlr,
     stsd, stsc) fully.  stsz / stco / co64 / stts entries are read on demand
     (seek to entry k) so a 1 GB file with 36k frames costs a few KB.
  3. Pick the trak whose stsd has a sample entry with format 'djmd'.
  4. Map sample index -> (chunk, offset) via stsc + stco/co64 + stsz, read the
     sample bytes, decode protobuf generically (varint / fixed64 / len / fixed32),
     then look up GPS by the per-protocol field path from ExifTool DJI.pm,
     falling back to a structural heuristic.
  5. If GPS is 0/0 or missing, probe more samples: 1..9, then 16,32,64,... and
     the last sample.

Usage: djmd_gps.py [--json] [--dump] FILE...
"""
import json
import math
import os
import struct
import sys
import time
from datetime import datetime, timedelta, timezone

QT_EPOCH = datetime(1904, 1, 1, tzinfo=timezone.utc)

CONTAINERS = {b'moov', b'trak', b'mdia', b'minf', b'stbl', b'edts', b'dinf', b'udta', b'mvex'}

# protocol (".proto" name without suffix) -> field path of the GPSInfo sub-message
# (from Image-ExifTool 13.59 lib/Image/ExifTool/DJI.pm).  GPSInfo: 1=CoordinateUnits
# (0=radians,1=degrees; absent => 0), 2=lat double, 3=lon double.
# Sibling "-2" of the GPSInfo path = AbsoluteAltitude int64 (mm).
GPS_PATHS = {
    'dvtm_Air3s':    (3, 3, 4, 1),
    'dvtm_Air3':     (3, 3, 4, 1),
    'dvtm_Mini4_Pro': (3, 3, 4, 1),
    'dvtm_wm265e':   (3, 3, 4, 1),   # Mavic 3
    'dvtm_pm320':    (3, 3, 4, 1),   # Matrice 30
    'dvtm_wa345e':   (3, 3, 4, 1),   # Matrice 4E
    'dvtm_wm261':    (3, 3, 4, 1),   # Mavic 3 Pro
    'dvtm_Mavic4':   (3, 3, 4, 1),   # Mavic 4 Pro (degrees)
    'dvtm_Mini5Pro': (3, 3, 4, 1),   # Mini 5 Pro (degrees, NC)
    'dvtm_AVATA2':   (3, 4, 4, 1),
    'dvtm_dji_neo':  (3, 4, 4, 1),
    'dvtm_ac203':    (3, 4, 2, 1),   # Osmo Action 4
    'dvtm_ac204':    (3, 4, 2, 1),   # Osmo Action 5
    'dvtm_ac206':    (3, 4, 2, 1),   # Osmo Action 6
    'dvtm_oq101':    (3, 4, 2, 1),   # Osmo 360
}


class CountingFile:
    """File wrapper that counts bytes read and read() calls."""
    def __init__(self, path):
        self.f = open(path, 'rb')
        self.size = os.fstat(self.f.fileno()).st_size
        self.bytes_read = 0
        self.reads = 0

    BLOCK = int(os.environ.get('DJMD_BLOCK', '0'))   # >0: aligned block cache (fewer syscalls)

    def read_at(self, off, n):
        if not self.BLOCK:
            self.f.seek(off)
            b = self.f.read(n)
            self.bytes_read += len(b)
            self.reads += 1
            return b
        cache = self.__dict__.setdefault('_cache', {})
        out = bytearray()
        end = min(off + n, self.size)
        pos = off
        while pos < end:
            bi = pos // self.BLOCK
            blk = cache.get(bi)
            if blk is None:
                self.f.seek(bi * self.BLOCK)
                blk = self.f.read(self.BLOCK)
                self.bytes_read += len(blk)
                self.reads += 1
                cache[bi] = blk
            s = pos - bi * self.BLOCK
            take = blk[s:s + (end - pos)]
            if not take:
                break
            out += take
            pos += len(take)
        return bytes(out)

    def close(self):
        self.f.close()


class Box:
    __slots__ = ('type', 'off', 'size', 'hdr')

    def __init__(self, typ, off, size, hdr):
        self.type, self.off, self.size, self.hdr = typ, off, size, hdr

    @property
    def body(self):
        return self.off + self.hdr

    @property
    def end(self):
        return self.off + self.size

    def __repr__(self):
        return f'{self.type.decode("latin1")}@{self.off}+{self.size}'


def iter_boxes(cf, start, end):
    pos = start
    while pos + 8 <= end:
        h = cf.read_at(pos, 16 if pos + 16 <= end else 8)
        size, typ = struct.unpack('>I4s', h[:8])
        hdr = 8
        if size == 1:
            if len(h) < 16:
                raise ValueError('truncated largesize header')
            size = struct.unpack('>Q', h[8:16])[0]
            hdr = 16
        elif size == 0:
            size = end - pos            # box extends to end of parent/file
        if size < hdr:
            raise ValueError(f'bad box size {size} at {pos}')
        if pos + size > end:
            # truncated file (recording interrupted) -> stop, caller decides
            yield Box(typ, pos, end - pos, hdr)
            return
        if typ == b'uuid':
            hdr += 16
        yield Box(typ, pos, size, hdr)
        pos += size


def child(cf, box, typ):
    for b in iter_boxes(cf, box.body, box.end):
        if b.type == typ:
            return b
    return None


def read_body(cf, box, limit=1 << 20):
    n = box.size - box.hdr
    if n > limit:
        raise ValueError(f'box {box} too large to slurp ({n})')
    return cf.read_at(box.body, n)


def qt_time(secs):
    if not secs:
        return None
    return QT_EPOCH + timedelta(seconds=secs)


def parse_mvhd(b):
    ver = b[0]
    if ver == 1:
        ctime, mtime, scale, dur = struct.unpack('>QQIQ', b[4:32])
    else:
        ctime, mtime, scale, dur = struct.unpack('>IIII', b[4:20])
    return qt_time(ctime), scale, dur


parse_mdhd = parse_mvhd   # same leading layout


class SampleTable:
    """Lazy random access into stsc/stsz/stco/co64/stts of one track."""
    def __init__(self, cf, stbl):
        self.cf = cf
        self.boxes = {b.type: b for b in iter_boxes(cf, stbl.body, stbl.end)}
        # stsd: count + entries (size, format)
        stsd = read_body(cf, self.boxes[b'stsd'], 1 << 16)
        n = struct.unpack('>I', stsd[4:8])[0]
        self.formats = []
        p = 8
        for _ in range(n):
            esz, fmt = struct.unpack('>I4s', stsd[p:p + 8])
            self.formats.append(fmt)
            p += esz
        # stsc (small): first_chunk, samples_per_chunk, desc_index
        stsc = read_body(cf, self.boxes[b'stsc'])
        cnt = struct.unpack('>I', stsc[4:8])[0]
        self.stsc = [struct.unpack('>III', stsc[8 + 12 * i:20 + 12 * i]) for i in range(cnt)]
        # stsz header (entries read on demand)
        if b'stsz' in self.boxes:
            bx = self.boxes[b'stsz']
            h = cf.read_at(bx.body, 12)
            self.const_size, self.sample_count = struct.unpack('>II', h[4:12])
            self.stsz_entries = bx.body + 12
            self.stz2 = None
        else:  # stz2 (compact sizes) -- rare; minimal support
            bx = self.boxes[b'stz2']
            h = cf.read_at(bx.body, 12)
            self.stz2 = h[7]
            self.sample_count = struct.unpack('>I', h[8:12])[0]
            self.const_size = 0
            self.stsz_entries = bx.body + 12
        # chunk offsets
        if b'co64' in self.boxes:
            bx, self.co_w = self.boxes[b'co64'], 8
        else:
            bx, self.co_w = self.boxes[b'stco'], 4
        self.chunk_count = struct.unpack('>I', cf.read_at(bx.body + 4, 4))[0]
        self.co_entries = bx.body + 8
        self._size_cache = {}

    def sample_size(self, k):
        if self.const_size:
            return self.const_size
        if k in self._size_cache:
            return self._size_cache[k]
        if self.stz2:
            if self.stz2 == 16:
                v = struct.unpack('>H', self.cf.read_at(self.stsz_entries + 2 * k, 2))[0]
            elif self.stz2 == 8:
                v = self.cf.read_at(self.stsz_entries + k, 1)[0]
            else:  # 4-bit
                byte = self.cf.read_at(self.stsz_entries + k // 2, 1)[0]
                v = (byte >> 4) if k % 2 == 0 else (byte & 0xF)
        else:
            v = struct.unpack('>I', self.cf.read_at(self.stsz_entries + 4 * k, 4))[0]
        self._size_cache[k] = v
        return v

    def sample_sizes(self, first, n):
        """Batch-read n consecutive entries (one read)."""
        if self.const_size:
            return [self.const_size] * n
        if self.stz2:
            return [self.sample_size(first + i) for i in range(n)]
        raw = self.cf.read_at(self.stsz_entries + 4 * first, 4 * n)
        out = list(struct.unpack(f'>{n}I', raw))
        for i, v in enumerate(out):
            self._size_cache[first + i] = v
        return out

    def chunk_offset(self, c):  # c is 0-based
        raw = self.cf.read_at(self.co_entries + self.co_w * c, self.co_w)
        return struct.unpack('>Q' if self.co_w == 8 else '>I', raw)[0]

    def locate(self, k):
        """sample index (0-based) -> (file offset, size, stsd desc index 1-based)."""
        if k >= self.sample_count:
            raise IndexError(k)
        s = 0  # samples before current stsc run
        for i, (first_chunk, spc, desc) in enumerate(self.stsc):
            next_first = self.stsc[i + 1][0] if i + 1 < len(self.stsc) else self.chunk_count + 1
            run_chunks = next_first - first_chunk
            run_samples = run_chunks * spc
            if k < s + run_samples:
                rel = k - s
                chunk = first_chunk - 1 + rel // spc
                first_in_chunk = k - rel % spc
                off = self.chunk_offset(chunk)
                if k > first_in_chunk:
                    off += sum(self.sample_sizes(first_in_chunk, k - first_in_chunk))
                return off, self.sample_size(k), desc
            s += run_samples
        raise IndexError(k)


# ---------------------------------------------------------------- protobuf ---
def varint(buf, p):
    v = 0
    shift = 0
    while True:
        if p >= len(buf):
            raise ValueError('truncated varint')
        b = buf[p]
        p += 1
        v |= (b & 0x7F) << shift
        if not b & 0x80:
            return v, p
        shift += 7
        if shift > 70:
            raise ValueError('varint too long')


def parse_pb(buf):
    """Return list of (field, wiretype, value) or None if not valid protobuf."""
    out = []
    p = 0
    try:
        while p < len(buf):
            key, p = varint(buf, p)
            fn, wt = key >> 3, key & 7
            if fn == 0:
                return None
            if wt == 0:
                v, p = varint(buf, p)
            elif wt == 1:
                if p + 8 > len(buf):
                    return None
                v = buf[p:p + 8]
                p += 8
            elif wt == 2:
                n, p = varint(buf, p)
                if p + n > len(buf):
                    return None
                v = buf[p:p + n]
                p += n
            elif wt == 5:
                if p + 4 > len(buf):
                    return None
                v = buf[p:p + 4]
                p += 4
            else:
                return None
            out.append((fn, wt, v))
    except ValueError:
        return None
    return out


def looks_like_text(b):
    return len(b) > 0 and all(0x20 <= c < 0x7F or c in (9, 10, 13) for c in b)


def decode_tree(buf, depth=0):
    """Generic decode: len-delimited payloads that parse as protobuf (and are not
    plain text) are recursed into.  Returns list of (field, wt, value|subtree)."""
    recs = parse_pb(buf)
    if recs is None:
        return None
    out = []
    for fn, wt, v in recs:
        if wt == 2 and not looks_like_text(v) and depth < 12:
            sub = decode_tree(v, depth + 1) if v else None
            out.append((fn, wt, sub if sub is not None else v))
        else:
            out.append((fn, wt, v))
    return out


def find_protocol(tree):
    for fn, wt, v in tree or []:
        if wt == 2 and isinstance(v, (bytes, bytearray)) and v.endswith(b'.proto'):
            return v.decode('latin1')
        if isinstance(v, list):
            r = find_protocol(v)
            if r:
                return r
    return None


def get_path(tree, path):
    node = tree
    for fn in path:
        if not isinstance(node, list):
            return None
        nxt = None
        for f, wt, v in node:
            if f == fn:
                nxt = v
                break           # first occurrence
        node = nxt
    return node


def gpsinfo_from(sub):
    if not isinstance(sub, list):
        return None
    units, lat, lon = 0, None, None
    for f, wt, v in sub:
        if f == 1 and wt == 0:
            units = v
        elif f == 2 and wt == 1:
            lat = struct.unpack('<d', v)[0]
        elif f == 3 and wt == 1:
            lon = struct.unpack('<d', v)[0]
    if lat is None and lon is None:
        return None
    lat = lat or 0.0
    lon = lon or 0.0
    if units == 0:
        lat, lon = math.degrees(lat), math.degrees(lon)
    return lat, lon, units


def heuristic_gps(tree, path=()):
    """Find a sub-message with fixed64 fields 2 and 3 that look like lat/lon."""
    for f, wt, v in tree or []:
        if isinstance(v, list):
            g = gpsinfo_from(v)
            if g and -90 <= g[0] <= 90 and -180 <= g[1] <= 180 and (g[0] or g[1]):
                return g, path + (f,)
            r = heuristic_gps(v, path + (f,))
            if r:
                return r
    return None


def int64s(v):
    return v - (1 << 64) if v >= 1 << 63 else v


def extract_sample(buf, protocol=None):
    tree = decode_tree(buf)
    if tree is None:
        return None
    proto = find_protocol(tree) or protocol
    key = proto[:-6] if proto and proto.endswith('.proto') else proto
    res = {'protocol': proto}
    path = GPS_PATHS.get(key)
    g = None
    if path:
        g = gpsinfo_from(get_path(tree, path))
        res['gps_path'] = '-'.join(map(str, path))
        alt = get_path(tree, path[:-1] + (2,))
        if isinstance(alt, int):
            res['abs_alt_m'] = int64s(alt) / 1000.0
    if g is None:
        h = heuristic_gps(tree)
        if h:
            g, hp = h
            res['gps_path'] = '-'.join(map(str, hp)) + ' (heuristic)'
    if g:
        res['lat'], res['lon'], res['coord_units'] = g
    ts = get_path(tree, (3, 1, 2))
    if isinstance(ts, int):
        res['timestamp_3_1_2'] = ts
    fno = get_path(tree, (3, 1, 1))
    if isinstance(fno, int):
        res['frame_number'] = fno
    res['_tree'] = tree
    return res


def probe_indices(n):
    idx = list(range(min(10, n)))
    k = 16
    while k < n:
        idx.append(k)
        k *= 2
    if n and n - 1 not in idx:
        idx.append(n - 1)
    return idx


def valid_fix(r):
    return r and 'lat' in r and (abs(r['lat']) > 1e-6 or abs(r['lon']) > 1e-6)


def read_mp4(path, dump=False, last=False):
    t0 = time.perf_counter()
    cf = CountingFile(path)
    out = {'file': path, 'size': cf.size}
    try:
        moov = None
        top = []
        for b in iter_boxes(cf, 0, cf.size):
            top.append(b.type.decode('latin1'))
            if b.type == b'moov':
                moov = b
            if b.type == b'moof':
                out['fragmented'] = True
        out['top'] = top
        if moov is None:
            out['error'] = 'no moov (truncated/unfinalized recording?)'
            # fallback: DJI writes djmd sample 0 first in mdat (seen at file offset 512).
            mdat = next((b for b in iter_boxes(cf, 0, cf.size) if b.type == b'mdat'), None)
            if mdat:
                head = cf.read_at(mdat.body, 4096)
                p = 0
                while p < len(head):   # consume top-level fields 1,2,3 (len-delimited) only
                    try:
                        key, q = varint(head, p)
                        if key & 7 != 2 or (key >> 3) not in (1, 2, 3):
                            break
                        n, q = varint(head, q)
                        p = q + n
                        if key >> 3 == 3:
                            break
                    except ValueError:
                        break
                r = extract_sample(head[:p]) if p else None
                if r and valid_fix(r) and (r.get('protocol') or '').endswith('.proto'):
                    out.update({x: r[x] for x in ('protocol', 'gps_path', 'lat', 'lon', 'coord_units',
                                                   'abs_alt_m', 'timestamp_3_1_2') if x in r})
                    out['gps_source'] = 'mdat-head fallback'
            return out
        tracks = []
        for b in iter_boxes(cf, moov.body, moov.end):
            if b.type == b'mvhd':
                ct, scale, dur = parse_mvhd(read_body(cf, b, 4096))
                out['mvhd_creation_utc'] = ct.isoformat() if ct else None
                out['duration_s'] = round(dur / scale, 3) if scale else None
            elif b.type == b'trak':
                tracks.append(b)
        djmd = None
        for tr in tracks:
            mdia = child(cf, tr, b'mdia')
            if not mdia:
                continue
            info = {}
            for mb in iter_boxes(cf, mdia.body, mdia.end):
                if mb.type == b'mdhd':
                    ct, scale, dur = parse_mdhd(read_body(cf, mb, 4096))
                    info['mdhd_creation_utc'] = ct.isoformat() if ct else None
                    info['timescale'] = scale
                elif mb.type == b'hdlr':
                    h = read_body(cf, mb, 4096)
                    info['handler'] = h[8:12].decode('latin1')
                    info['handler_name'] = h[24:].split(b'\0')[0].decode('latin1', 'replace').strip()
                elif mb.type == b'minf':
                    stbl = child(cf, mb, b'stbl')
                    if stbl:
                        # read stsd only first (cheap) to decide whether this is djmd
                        stsd = child(cf, stbl, b'stsd')
                        body = read_body(cf, stsd, 1 << 16)
                        n = struct.unpack('>I', body[4:8])[0]
                        fmts, p = [], 8
                        for _ in range(n):
                            esz, fmt = struct.unpack('>I4s', body[p:p + 8])
                            fmts.append(fmt.decode('latin1'))
                            p += esz
                        info['formats'] = fmts
                        info['_stbl'] = stbl
            out.setdefault('tracks', []).append({k: v for k, v in info.items() if not k.startswith('_')})
            if 'djmd' in info.get('formats', []) and djmd is None:
                djmd = info
        if djmd is None:
            out['error'] = 'no djmd track'
            return out
        out['djmd_mdhd_creation_utc'] = djmd.get('mdhd_creation_utc')
        st = SampleTable(cf, djmd['_stbl'])
        out['djmd_samples'] = st.sample_count
        djmd_desc = st.formats.index(b'djmd') + 1
        tried = []
        protocol = None
        for k in probe_indices(st.sample_count):
            off, sz, desc = st.locate(k)
            if desc != djmd_desc:
                continue       # sample belongs to a non-djmd sample entry
            if sz > 1 << 20:
                continue
            buf = cf.read_at(off, sz)
            r = extract_sample(buf, protocol)
            tried.append(k)
            if r is None:
                continue
            protocol = protocol or r.get('protocol')
            if dump and k == 0:
                out['_dump'] = r['_tree']
            if valid_fix(r):
                out.update({x: r[x] for x in ('protocol', 'gps_path', 'lat', 'lon', 'coord_units',
                                               'abs_alt_m', 'timestamp_3_1_2', 'frame_number') if x in r})
                out['gps_sample_index'] = k
                out['sample_bytes'] = sz
                break
        else:
            out['protocol'] = protocol
            out['error'] = 'no GPS fix in probed samples'
        out['samples_probed'] = tried
        if last and 'lat' in out:
            # optional: last fix (walk back a few samples) -> in-clip travel distance
            for k in range(st.sample_count - 1, max(-1, st.sample_count - 10), -1):
                off, sz, desc = st.locate(k)
                if desc != djmd_desc or sz > 1 << 20:
                    continue
                r = extract_sample(cf.read_at(off, sz), protocol)
                if valid_fix(r):
                    out['last_lat'], out['last_lon'] = r['lat'], r['lon']
                    out['last_sample_index'] = k
                    break
        return out
    except Exception as e:  # spike: report, don't crash the batch
        out['error'] = f'{type(e).__name__}: {e}'
        return out
    finally:
        out['bytes_read'] = cf.bytes_read
        out['read_calls'] = cf.reads
        out['elapsed_ms'] = round((time.perf_counter() - t0) * 1000, 1)
        cf.close()


def dump_tree(tree, indent=0, prefix=''):
    for fn, wt, v in tree:
        tag = f'{prefix}{fn}'
        if isinstance(v, list):
            print('  ' * indent + f'{tag} (msg)')
            dump_tree(v, indent + 1, tag + '-')
        elif wt == 0:
            print('  ' * indent + f'{tag} varint {v}')
        elif wt == 1:
            print('  ' * indent + f'{tag} fixed64 {v.hex()} double={struct.unpack("<d", v)[0]!r} int64={struct.unpack("<q", v)[0]}')
        elif wt == 5:
            print('  ' * indent + f'{tag} fixed32 {v.hex()} float={struct.unpack("<f", v)[0]!r}')
        else:
            print('  ' * indent + f'{tag} bytes[{len(v)}] {v[:60]!r}')


if __name__ == '__main__':
    args = sys.argv[1:]
    as_json = '--json' in args
    dump = '--dump' in args
    files = [a for a in args if not a.startswith('--')]
    for fp in files:
        r = read_mp4(fp, dump=dump)
        tree = r.pop('_dump', None)
        if as_json:
            print(json.dumps(r))
        else:
            for k, v in r.items():
                print(f'{k}: {v}')
            print()
        if dump and tree:
            dump_tree(tree)
