# djmd first-sample GPS reader: spike results

The reader works. On every file I tested it returns the same GPS as `exiftool -ee3`, it takes 3–50 ms per file instead of 7–87 s, and it reads about 2.2 KB of the file. It also recovers GPS from two truncated recordings that have no `moov` box. On the grouping question: clips inside one of the user's folders are at most 1.8 km apart, and separate folders are at least 14.8 km apart, so GPS separates them clearly.

**Spike code** (all in `/tmp/claude-1000/-mnt-c-dev-uas-sort/187b6b83-e3e1-4971-8c9e-c13c3990d115/scratchpad/spikes/djmd/`):
- `djmd_gps.py` is the reader. Run `python3 djmd_gps.py [--json] [--dump] FILE...`. `--dump` prints the decoded protobuf tree, and setting the environment variable `DJMD_BLOCK=4096` turns on a block cache.
- `calibrate.py` runs the calibration and writes `calibration.json`.
- `synth_test.py` builds synthetic test MP4s from real samples into `synth/`.

I did not use a Windows temp directory, and I wrote nothing to user data or to `/mnt/c/dev/uas-sort`.

## 1. Where the GPS lives in the protobuf (from ExifTool 13.59 `DJI.pm`)

ExifTool finds the model from any length-delimited field whose value ends in `.proto`. On the Air 3S this is field 1-1-1 = `dvtm_Air3s.proto`, and it appears **only in djmd sample 0** (486–493 bytes). Every later sample (about 300 bytes) contains only top-level field 3.

GPSInfo sub-message layout, the same for every model:

| Field | Meaning |
|---|---|
| 1 | CoordinateUnits, varint. 0 or absent means **radians**; 1 means degrees. |
| 2 | Latitude, 64-bit little-endian double |
| 3 | Longitude, 64-bit little-endian double |

The sibling field `…-2` is AbsoluteAltitude as a signed 64-bit integer in millimetres.

| Protocol (model) | GPSInfo path | Alt (mm) | Real GPS date/time? |
|---|---|---|---|
| **dvtm_Air3s (Air 3S)**, dvtm_Air3 (Air 3), dvtm_Mini4_Pro, dvtm_wm265e (Mavic 3), dvtm_pm320 (Matrice 30) | **3-3-4-1** | 3-3-4-2 | **No** |
| dvtm_wm261 (Mavic 3 Pro), dvtm_wa345e (Matrice 4E) | 3-3-4-1 | 3-3-4-2 | Yes, string at 3-3-4-6-1 |
| dvtm_Mavic4, dvtm_Mini5Pro | 3-3-4-1, **in degrees** | 3-3-4-2 | No |
| dvtm_AVATA2 (Avata 2), dvtm_dji_neo | 3-4-4-1 | 3-4-4-2 | No |
| dvtm_ac203/204/206 (Osmo Action 4/5/6), dvtm_oq101 (Osmo 360) | 3-4-2-1 | 3-4-2-2 | Yes, 3-4-2-6-1 |

**Timestamps and clock on the Air 3S.** The djmd track has no absolute time and no timezone.
- Fields 3-1-2 and 1-1-9 are a **microsecond counter since the drone was powered on** (ExifTool calls it TimeStamp, divided by 1e6).
- ExifTool's `GPSDateTime` for the Air 3S is made up (see `QuickTimeStream.pl` around line 1531, `SetGPSDateTime`). It is CreateDate plus the sample time, not a GPS reading.
- Other fields seen: 1-1-5 is the drone serial (`<drone-serial>`) and 1-1-10 is `DJI Air3s`. The file's handler names are "HAL meta" for djmd and "HAL dbgi" for dbgi.

## 2. How the reader works

1. **Top-level boxes.** Read each 8-byte header, or 16 bytes when size is 1 (64-bit size). Size 0 means the box runs to the end of the file. Skip the payloads by seeking, so `mdat` is never read. DJI files are laid out `ftyp, free, free, mdat, moov`, with `moov` at the end.
2. **Inside `moov`.** Walk the children the same lazy way and read `mvhd` for the creation time. The time is seconds since 1904-01-01 UTC: 32-bit in version 0, 64-bit in version 1.
   - For each track, read `mdhd`, `hdlr` and `stsd`, and pick the track whose `stsd` has an entry with format `djmd`. Track 2 of 3 in these files.
   - Never slurp `moov`. It is about 1 MB because `udta/meta/ilst` holds JPEGs (see port notes).
3. **Sample tables.** Read the `stsc` table whole. Read `stsz` (or `stz2`) and `stco` (or `co64`) one entry at a time by seeking.
   - Sample k maps to a chunk through the `stsc` runs.
   - Its offset is the chunk offset plus the sizes of the earlier samples in that chunk.
   - Check that `stsc`'s description index points at the `djmd` entry.
4. **Protobuf decoding.** Decode generically: varint, 64-bit fixed, length-delimited, 32-bit fixed. Decode a length-delimited value as a nested message only when it is not printable text.
   - Look up the GPS path in the protocol table above.
   - If the protocol is unknown, fall back to searching for any sub-message whose fields 2 and 3 are doubles in the valid latitude/longitude range.
   - Convert radians to degrees when the units field is absent or 0.
5. **No-fix probing.** Treat `|lat|` and `|lon|` both below 1e-6 as "no fix". In that case probe samples 1–9, then 16, 32, 64 and so on, then the last sample.
6. **Fallback when there is no `moov`.** DJI writes djmd sample 0 first in `mdat`, at file offset 512. Read 4 KB there, consume top-level fields 1, 2 and 3, and decode them. Accept the result only if the protocol string ends in `.proto`.
7. **Optional.** Read the last sample too, to measure how far the drone moved during the clip.

## 3. Validation against exiftool

| File | Spike lat, lon, alt | exiftool `-ee3` Doc1 | exiftool time | Spike time / bytes read |
|---|---|---|---|---|
| Zachar Bay `DJI_20260927140627_0128_D.MP4` (89 MB) | 57.5504420579265, −153.738972972093, 298.394 | identical | 7.1 s | 8.1 ms cold, 4.8 ms warm / 1,873 B, 63 reads |
| Anvil `DJI_20260726235645_0001_D.MP4` (1.2 GB) | 64.5626761883622, −165.369637797651, 358.234 | identical | **87 s** | 15.7 ms cold, 5.9 ms warm / 1,880 B, 63 reads |

- Samples 7 and 32 of file 0128 also match exiftool's Doc8 and Doc33 exactly.
- With a 4 KB block cache, a file takes 6–7 reads and about 25 KB. With a 64 KB cache, 3–4 reads and about 200 KB. Timings were measured over WSL's `/mnt/c` mount, so native Windows should be faster. Every result is far below the 0.5 s target.
- Synthetic edge-case tests all pass:
  - `moov` at the start, a 64-bit `mdat`, `co64`, and 3 samples per chunk, with two `stsd` entries where `djmd` is entry 2.
  - `moov` at the end with `stco`.
  - GPS zeroed in the first 7 samples: the fix comes from sample 7.
  - GPS zeroed in the first 25 samples: the fix comes from sample 32.
  - GPS zeroed everywhere: the reader returns "no GPS fix".

## 4. Calibration

### Which files I read

The Windows attributes fall into three classes. I checked them with `[IO.File]::GetAttributes` through `powershell.exe`, and checked the reparse tag with `fsutil`.

| Attributes | Meaning | Count |
|---|---|---|
| `0x20` | Plain local file, not a placeholder | 36 |
| `0x420` | ReparsePoint with tag `0x9000601A` (a cloud placeholder), but neither Offline nor RecallOnDataAccess is set. Likely downloaded (hydrated): `PANO_0001` is in this class and the context says it is local. | 88 |
| `0x401620` | **Cloud-only**: RecallOnDataAccess, Offline and Sparse are set | 30 (all of Kodiak) |

Only cloud-only files were skipped. The three `0x420` Anvil files were read.

- **Zachar Bay has 13 MP4s, not the 15 stated in the task context.**
- The `.trinf` sidecar belongs to Anvil 0014, which is exactly 7 MiB with `ftyp/free/free/mdat` and **no `moov`**. ffprobe fails on it: "moov atom not found".
- Anvil 0024 is exactly 11 MiB and also has no `moov`. The fallback recovered GPS from both.

### Per-file first fix

`creation_time` is from `mvhd`. Local time is AKDT. "Drone − UTC" is the time in the filename minus `creation_time`.

| Folder | File | creation UTC | Local AKDT | Drone − UTC | lat | lon |
|---|---|---|---|---|---|---|
| Nome Roads | …0704014132_0110 | 07-04 05:41:33 | 07-03 21:41:33 | −4.0 h | 64.59171 | −165.67254 |
| Council Road | …0726022937_0118 | 07-26 06:29:38 | 07-25 22:29:38 | −4.0 | 64.69469 | −164.27628 |
| Anvil | …0726235645_0001 | 07-27 03:56:45 | 07-26 19:56:45 | −4.0 | 64.56268 | −165.36964 |
| Anvil | …0727000012_0002 | 04:00:12 | 20:00:12 | −4.0 | 64.56384 | −165.36877 |
| Anvil | …0004 | 04:01:29 | 20:01:29 | −4.0 | 64.56450 | −165.37006 |
| Anvil | …0005 | 04:03:02 | 20:03:02 | −4.0 | 64.56434 | −165.36934 |
| Anvil | …0006 | 04:05:11 | 20:05:11 | −4.0 | 64.56425 | −165.37024 |
| Anvil | …0007 | 04:08:32 | 20:08:32 | −4.0 | 64.56374 | −165.36941 |
| Anvil | …0011 | 04:11:08 | 20:11:08 | −4.0 | 64.56264 | −165.37322 |
| Anvil | …0012 | 04:12:16 | 20:12:16 | −4.0 | 64.56264 | −165.37323 |
| Anvil | …0013 | 04:13:16 | 20:13:16 | −4.0 | 64.56264 | −165.37323 |
| Anvil | …0014 (no `moov`, fallback) | n/a (mtime 04:20:47) | ~20:20 | n/a | 64.56353 | −165.37125 |
| Anvil | …0015 | 04:21:18 | 20:21:18 | −4.0 | 64.56290 | −165.37430 |
| Anvil | …0016 | 04:22:19 | 20:22:19 | −4.0 | 64.56351 | −165.37116 |
| Anvil | …0017 | 04:23:38 | 20:23:38 | −4.0 | 64.56275 | −165.37462 |
| Anvil | …0018 | 04:24:35 | 20:24:35 | −4.0 | 64.56274 | −165.37246 |
| Anvil | …0023 | 04:27:00 | 20:27:00 | −4.0 | 64.56230 | −165.37368 |
| Anvil | …0024 (no `moov`, fallback) | n/a | ~20:52 | n/a | 64.55569 | −165.36014 |
| Anvil | …0028 | 04:54:39 | 20:54:39 | −4.0 | 64.55535 | −165.36135 |
| Anvil | …0029 | 04:55:26 | 20:55:26 | −4.0 | 64.55379 | −165.37090 |
| Anvil | …0030 | 04:55:52 | 20:55:52 | −4.0 | 64.55442 | −165.37579 |
| Anvil | …0032 | 04:57:56 | 20:57:56 | −4.0 | 64.56038 | −165.37335 |
| Anvil | …0033 | 04:58:27 | 20:58:27 | −4.0 | 64.56035 | −165.37335 |
| Zachar Bay | …0927140127_0123 | 09-27 18:01:27 | 10:01:27 | −4.0 | 57.53683 | −153.74839 |
| Zachar Bay | …0124 | 18:01:44 | 10:01:44 | −4.0 | 57.53696 | −153.74725 |
| Zachar Bay | …0125 | 18:03:08 | 10:03:08 | −4.0 | 57.54435 | −153.74100 |
| Zachar Bay | …0126 | 18:03:56 | 10:03:56 | −4.0 | 57.54676 | −153.73954 |
| Zachar Bay | …0128 | 18:06:27 | 10:06:27 | −4.0 | 57.55044 | −153.73897 |
| Zachar Bay | …0132 | 18:09:35 | 10:09:35 | −4.0 | 57.54995 | −153.73981 |
| Zachar Bay | …0133 | 18:13:27 | 10:13:27 | −4.0 | 57.54262 | −153.74373 |
| Zachar Bay | …0136 | 18:15:33 | 10:15:33 | −4.0 | 57.54275 | −153.74327 |
| Zachar Bay | …0137 | 18:16:55 | 10:16:55 | −4.0 | 57.54436 | −153.74430 |
| Zachar Bay | …0140 | 18:19:32 | 10:19:32 | −4.0 | 57.53503 | −153.73635 |
| Zachar Bay | …0141 | 18:20:31 | 10:20:31 | −4.0 | 57.53814 | −153.72861 |
| Zachar Bay | …0143 | 18:21:38 | 10:21:38 | −4.0 | 57.53623 | −153.73269 |
| Zachar Bay | …0148 | 18:24:16 | 10:24:16 | −4.0 | 57.53484 | −153.74808 |

### Spread within each folder

| Folder | Clips with GPS | Max distance between first fixes | Including last fixes | Largest movement within one clip | Time span |
|---|---|---|---|---|---|
| Anvil Mountain | 21 | 1.19 km | 1.25 km | 0.67 km | 62 min |
| Zachar Bay | 13 | 1.82 km | 1.83 km | 1.16 km | 24 min |
| Nome Roads | 1 | n/a | 0.46 km | 0.46 km | only 1 clip read |
| Council Road | 1 | n/a | 0.50 km | 0.50 km | only 1 clip read |

### Between folders

Distances are between folder centroids. Kodiak uses the PANO DNG position (57.7996, −152.3902) as a stand-in.

| km | Kodiak | Nome Rd | Council Rd | Anvil | Zachar |
|---|---|---|---|---|---|
| Kodiak | 0 | 1033.7 | 992.8 | 1020.7 | **85.3** |
| Nome Roads | | 0 | 67.5 | **14.8** | 1009.9 |
| Council Road | | | 0 | **54.2** | 973.5 |
| Anvil | | | | 0 | 997.4 |

The time gaps come from the whole folder: file names (+4 h) and mtimes, with no file contents read.

| Folder transition | Gap | Distance |
|---|---|---|
| Newport → Kodiak | 12.3 d | n/a |
| Kodiak (05-22 21:52 → 05-25 05:32 AKDT) → Safety Roadhouse | 18.7 d | n/a |
| Safety Roadhouse → Nome Roads | 21.0 d | n/a |
| Nome Roads → Council Road | 21.9 d | 67.5 km |
| **Council Road → Anvil Mountain** | **21.4 h (consecutive days)** | **54.2 km** |
| Anvil Mountain → Zachar Bay | 62.5 d | 997 km |

**What this means for grouping:**
- Clusters inside a folder are 1.8 km across or less, including in-flight movement.
- The only pair of consecutive days that the user split into two folders (Council Road → Anvil) is 54 km apart.
- A clustering radius of about 5 km, and a "big jump" threshold somewhere around 20–25 km, would reproduce every folder the user made.
- **Caveat:** folders shot along a road (Nome Roads, Council Road) could span tens of km on their own. I read only one clip from each, so their real spread is unknown. The other clips in those folders are `0x420` (not cloud-only) and could be read to confirm this if you want.

## 5. Autel MAX_0065 (and other findings)

- **MAX_0065.MP4** has a single track (Ambarella.AVC, format `avc1`) and **no djmd**, so the reader returns null (14 ms, 589 B). exiftool `-ee3` finds no GPS at all.
- **Autel `creation_time` quirk.** `creation_time` = 2022-03-27 07:07:51, labelled UTC. The file's mtime is 8.0 h later (28,828 s) than that "UTC" value. That fits the context's claim that Autel `creation_time` is the drone's local wall clock mislabelled as UTC. Both values look like the drone's wall clock rather than true UTC; "local time" is inferred from that. Use the date as written: 2022-03-27, local.
- **DJI library files keep a true-UTC mtime.** For every DJI clip, mtime equals `creation_time` + duration to within 0.3–1.6 s (one outlier, 0015, at +30 s). mtime is readable on cloud-only placeholders without downloading them, so it gives true UTC times for library files the app cannot open.
- **The drone clock is UTC−4 in every folder** (filename minus mtime, all 7 DJI folders). **Correction to the task context:** Kodiak's first file (`20260523015251`) is **5/22 21:52 AKDT, not 17:52**. The folder date conclusion (5/22) still holds.
- **Power-on time identifies a battery/flight session.** `creation_time` minus the uptime field is constant to within ±1 s per power cycle. Anvil breaks into 3 sessions (03:54:00Z, 04:17:32Z, 04:50:36Z) and Zachar Bay into 1 (17:59:28Z). This could help sub-group clips and place truncated files, but it is not calendar time.
- **Cheap thumbnails.** Each DJI MP4 carries `udta/meta/ilst/tnal`, a 160×90 JPEG of about 12.7 KB, plus `covr` and `snal` at 960×540 (about 500 KB). The UI can use these without decoding video.
- DJI MP4s also have a `udta/fsid` box holding the original card path (`/mnt/media_rw/sdcard0/DCIM/DJI_001/…`).
- The file index is shared between photos and videos: `PANO 001_0087` sits between videos 0086 and 0088.

## 6. Notes for the C# port

**Estimated size:** about 300–350 lines of code. Box walker ~80, sample table ~100, protobuf ~70, GPS lookup and heuristic ~50, fallback ~30. Plus tests built on synthetic MP4s, as in `synth_test.py`. No NuGet packages are needed.

**I/O:**
- Open with `FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess)`.
- Use `BinaryPrimitives.ReadUInt32BigEndian` / `ReadUInt64BigEndian` for box fields and `ReadDoubleLittleEndian` for the protobuf doubles.
- A 4 KB aligned block cache cuts the work to about 6 reads per file. That is worth having on SD card readers.

**Boxes:**
- Handle size 1 (64-bit size), size 0 (runs to end of file) and `uuid` (+16 bytes of header).
- Guard against a size smaller than the header or larger than what remains in the file (truncated).
- Never read `mdat`, and never read `moov` whole, since `udta` covers is about 1 MB.

**Tables:**
- Keep `stsc` in memory and read `stsz`/`stco`/`co64` entries on demand.
- Support `co64` (files over 4 GB) and constant sample sizes (`stsz` sample_size ≠ 0).
- `stz2` is optional. In real DJI files: one sample per chunk, `stco`, and one `stsd` entry per track.

**Multiple `stsd` entries:** match on the format fourcc and check `stsc`'s description index. The synthetic test covers this.

**Fragmented MP4** (`moof`/`mvex`): DJI does not use it. If `moov` has `mvex` or `stsz` count is 0, go to the mdat-head fallback or return null. Don't implement `trun`.

**Truncated recordings (no `moov`):** these are real (Anvil 0014 and 0024). Use the fallback at the start of `mdat`, and take the time from the filename (drone clock, offset learned from any complete file on the card: −4 h) or from mtime. That exFAT mtimes on the card itself are true UTC is **UNVERIFIED**: there was no card to test, and only library copies were checked.

**Protobuf:**
- Varints must be `ulong`.
- The altitude field is a signed int64 stored as a plain varint (not zigzag); negative values appear as 2^64 − n, so cast to `long`.
- Degrees = radians × 180/π unless field 1 = 1.
- Walk forward only through the first-occurrence path.

**Missing fix:** probe 0–9, then powers of two, then the last sample, which is about 13 reads in the worst case.

**Time zone at the site:** turning the local date into a folder date needs a lookup from latitude/longitude to a time zone, because trips reach Rhode Island and Hawaii as well as Alaska. An offline package such as GeoTimeZone was suggested; it has **not been checked**.

**Ledger:** since Kodiak's library files are cloud-only, the app's ledger should store per-file lat/lon, UTC start, and the power-on/session time when it offloads. Later grouping against the library then never needs to open library content.