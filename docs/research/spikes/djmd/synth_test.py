#!/usr/bin/env python3
"""Build synthetic MP4s from real djmd sample payloads to exercise edge cases:
moov-first, 64-bit mdat largesize, co64, multi-sample chunks (stsc), two stsd
entries, and GPS zeroed in the first N samples (probe-forward path)."""
import os
import struct
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from djmd_gps import CountingFile, iter_boxes, child, SampleTable, read_mp4, parse_pb  # noqa

SRC = "/mnt/c/Users/damia/OneDrive/Pictures/UAS Videos/2026/2026-09/2026-09-27 Zachar Bay/DJI_20260927140627_0128_D.MP4"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'synth')
os.makedirs(OUT, exist_ok=True)

# --- grab first 40 real djmd samples
cf = CountingFile(SRC)
moov = [b for b in iter_boxes(cf, 0, cf.size) if b.type == b'moov'][0]
samples = None
for tr in [b for b in iter_boxes(cf, moov.body, moov.end) if b.type == b'trak']:
    st = SampleTable(cf, child(cf, child(cf, child(cf, tr, b'mdia'), b'minf'), b'stbl'))
    if st.formats == [b'djmd']:
        samples = [cf.read_at(*st.locate(k)[:2]) for k in range(40)]


def zero_gps(buf):
    """Replace lat/lon doubles (fields 3-3-4-1-2/3) with 0.0 by byte-pattern (same length)."""
    import math
    out = bytearray(buf)
    # find the two fixed64 records: tag 0x11 (field2,wt1) and 0x19 (field3,wt1) inside GPSInfo
    i = out.find(b'\x11')
    while i != -1:
        if i + 18 <= len(out) and out[i + 9] == 0x19:
            lat = struct.unpack('<d', out[i + 1:i + 9])[0]
            if 0.5 < abs(lat) < math.pi / 2:
                out[i + 1:i + 9] = b'\0' * 8
                out[i + 10:i + 18] = b'\0' * 8
                return bytes(out)
        i = out.find(b'\x11', i + 1)
    raise RuntimeError('gps not found')


def box(t, payload):
    return struct.pack('>I4s', 8 + len(payload), t) + payload


def fullbox(t, ver, flags, payload):
    return box(t, struct.pack('>I', (ver << 24) | flags) + payload)


def build(path, samples, spc=3, co64=True, moov_first=True, two_stsd=True, zero_first=0):
    samples = [zero_gps(s) if i < zero_first else s for i, s in enumerate(samples)]
    # chunks of spc samples, filler between chunks to make offsets non-contiguous
    chunks = [samples[i:i + spc] for i in range(0, len(samples), spc)]
    mdat_payload = bytearray()
    rel_offsets = []
    for c in chunks:
        mdat_payload += b'\xAA' * 100              # filler (e.g. video data)
        rel_offsets.append(len(mdat_payload))
        for s in c:
            mdat_payload += s

    def make_moov(mdat_body_start):
        offs = [mdat_body_start + o for o in rel_offsets]
        if two_stsd:
            # entry 1 = 'mett' dummy, entry 2 = 'djmd'; all chunks reference desc 2
            stsd_entries = box(b'mett', b'\0' * 8) + box(b'djmd', b'\0' * 8)
            n_entries, desc = 2, 2
        else:
            stsd_entries = box(b'djmd', b'\0' * 8)
            n_entries, desc = 1, 1
        stsd = fullbox(b'stsd', 0, 0, struct.pack('>I', n_entries) + stsd_entries)
        last_spc = len(chunks[-1])
        stsc_e = [(1, spc, desc)]
        if last_spc != spc:
            stsc_e.append((len(chunks), last_spc, desc))
        stsc = fullbox(b'stsc', 0, 0, struct.pack('>I', len(stsc_e)) + b''.join(struct.pack('>III', *e) for e in stsc_e))
        stsz = fullbox(b'stsz', 0, 0, struct.pack('>II', 0, len(samples)) + b''.join(struct.pack('>I', len(s)) for s in samples))
        if co64:
            co = fullbox(b'co64', 0, 0, struct.pack('>I', len(offs)) + b''.join(struct.pack('>Q', o) for o in offs))
        else:
            co = fullbox(b'stco', 0, 0, struct.pack('>I', len(offs)) + b''.join(struct.pack('>I', o) for o in offs))
        stts = fullbox(b'stts', 0, 0, struct.pack('>III', 1, len(samples), 1001))
        stbl = box(b'stbl', stsd + stts + stsc + stsz + co)
        minf = box(b'minf', stbl)
        ct = 3875977587  # 2026-09-27T18:06:27Z in QT epoch
        mdhd = fullbox(b'mdhd', 1, 0, struct.pack('>QQIQHH', ct, ct, 30000, 1001 * len(samples), 0, 0))
        hdlr = fullbox(b'hdlr', 0, 0, b'\0' * 4 + b'meta' + b'\0' * 12 + b'HAL meta\0')
        mdia = box(b'mdia', mdhd + hdlr + minf)
        trak = box(b'trak', mdia)
        mvhd = fullbox(b'mvhd', 1, 0, struct.pack('>QQIQ', ct, ct, 1000, 1000) + b'\0' * 80)
        return box(b'moov', mvhd + trak)

    ftyp = box(b'ftyp', b'isom\0\0\0\0isom')
    mdat_hdr_len = 16   # force 64-bit largesize
    if moov_first:
        moov_len = len(make_moov(0))
        mdat_start = len(ftyp) + moov_len
        moov_b = make_moov(mdat_start + mdat_hdr_len)
        assert len(moov_b) == moov_len
        mdat = struct.pack('>I4sQ', 1, b'mdat', mdat_hdr_len + len(mdat_payload)) + mdat_payload
        data = ftyp + moov_b + mdat
    else:
        mdat_start = len(ftyp)
        mdat = struct.pack('>I4sQ', 1, b'mdat', mdat_hdr_len + len(mdat_payload)) + mdat_payload
        data = ftyp + mdat + make_moov(mdat_start + mdat_hdr_len)
    open(path, 'wb').write(data)


cases = [
    ('moovfirst_co64_spc3_2stsd', dict(spc=3, co64=True, moov_first=True, two_stsd=True)),
    ('moovlast_stco_spc1', dict(spc=1, co64=False, moov_first=False, two_stsd=False)),
    ('zero_first7_spc4', dict(spc=4, co64=True, moov_first=False, two_stsd=True, zero_first=7)),
    ('zero_first25_spc5', dict(spc=5, co64=False, moov_first=True, two_stsd=False, zero_first=25)),
    ('zero_all', dict(spc=2, zero_first=40)),
]
for name, kw in cases:
    p = os.path.join(OUT, name + '.mp4')
    build(p, samples, **kw)
    r = read_mp4(p)
    print(f"{name:28s} lat={r.get('lat')} lon={r.get('lon')} idx={r.get('gps_sample_index')} "
          f"probed={r.get('samples_probed')} err={r.get('error')} bytes={r['bytes_read']}")
