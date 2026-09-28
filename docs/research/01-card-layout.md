# DJI Air 3S card layout and file taxonomy: research report

**Scope.** I used web research (DJI manual and specs, DJI support pages, DJI forum threads including DJI staff replies, mavicpilots threads, the djiutil source). I also ran read-only checks on locally present library files after confirming their Windows attributes: 0x20 or 0x420, with no RecallOnDataAccess or Offline flag. Anything I could not confirm is marked UNVERIFIED.

## 0. Found while checking: two broken videos in the library

The two hidden `.trinf` files in `Picture Offload` match two videos with no moov box. A video without its moov index box won't play.
- `Picture Offload/.DJI_20260727002013_0014_D.MP4.trinf` goes with `2026-07-26 Anvil Mountain/DJI_20260727002013_0014_D.MP4`. That file is exactly 7,340,032 bytes (7 MiB). Its top-level boxes are `ftyp, free, free, mdat` with no `moov`, and ffprobe reports "moov atom not found".
- `.DJI_20260727005240_0024_D.MP4.trinf` goes with `DJI_20260727005240_0024_D.MP4`. That file is exactly 11,534,336 bytes (11 MiB), also with no `moov`.
- A good neighbour for comparison, `_0015_`, has a `moov` and plays (hvc1 + djmd + dbgi tracks).
- The `.trinf` content starts `mp4 PBFM…SBFM…` and contains track descriptors (`hvc1`, `djmd`, `dbgi`). It looks like DJI's recovery journal for a recording that never finished writing.
- This matches older DJI forum reports: a hidden `.DJI_0039.MP4.trinf`/`.avc1` pair appears when a recording isn't stopped before power-off. In those reports, putting the card back in the drone and powering it on repaired the file. Whether the Air 3S still does this is UNVERIFIED. djifix or untrunc are the fallback.
- **What the app should do:** after copying, a byte-for-byte check can still pass on a broken file. The app should also walk the top-level MP4 boxes (reads only a few bytes) and flag any MP4 that has no `moov`, especially when a matching `.trinf` exists.

## 1. Folder structure on the card (Air 3S and other DJI Fly-era drones)

```
<root>\
  DCIM\
    DJI_001\                 main media folder (all MP4 / JPG / DNG, plus LRF/SRT if written)
    DJI_00N[_<suffix>]\      further folders (custom folder naming or rollover)
    PANORAMA\<nnn_nnnn>\     pano source frames (PANO_0001.DNG|JPG ...), one folder per pano
    HYPERLAPSE\<nnn_nnnn>\   hyperlapse source frames (JPG or DNG), one folder per hyperlapse
  MISC\                      FC####.db (SQLite media DB), IDX\, THM\, GIS\dji.gis, LOG\ ...  -> skip
  LOST.DIR\ (if formatted by Android/RC)  -> skip
```

**Confirmed**
- The main folder is `DCIM\DJI_001` on newer DJI Fly drones: djiutil source tree comment, an Air 3 owner on mavicpilots, and droneblog on the Neo.
- On the Air 3, `MISC` held `FC8282.db` (FC8282 is the Air 3 camera model) plus empty `IDX` and `THM` folders. For the Air 3S I expect `FC9113.db` (UNVERIFIED).
- DJI support lists these storage paths: all photos and videos in the main folder, panorama in `DCIM\PANORAMA`, hyperlapse in `DCIM\HYPERLAPSE`. It says "LOST.DIR and MISC … no backup is required".
- DJI staff (Air 3 thread): "HYPERLAPSE: original time-lapse photos; PANORAMA: original panoramic photos". The composite hyperlapse MP4 goes in the main media folder.
- Pano and hyperlapse originals are saved **only if enabled**, and their format is set **separately** from normal photos. The Air 3S manual says to set the Hyperlapse photo type, or Off. Air 3S owners report the pano originals setting is separate: one got 33 JPEGs for a 360° pano while photo mode was set to RAW.
- Air 3S Free Panorama also saves its frames in their own folder, with names repeating across sets (PANO_000N).

**Inferred / UNVERIFIED**
- Set folder names follow `<3-digit folder index>_<4-digit counter>`. Your local example is `001_0087`; older drones used `100_0004`, which matched `DJI_0004.MP4`.
- The stitched pano JPG and the hyperlapse MP4 probably land in `DJI_001`, sharing the set's counter.
- Hyperlapse frame file names inside a set folder are unknown.
- Rollover from `DJI_001` to `DJI_002` is unknown for the Air 3S. The older scheme was 999 files per `1xxMEDIA` folder (DJI staff). DJI's custom folder naming creates a new folder, e.g. `DJI_002_A01` (Osmo Action 3, same Fly-era scheme; DJI staff say the same for the Mini 4 Pro).
- **Design implication:** enumerate every `DCIM` subfolder that matches the pattern. Don't hardcode `DJI_001`.

**Other modes**
- **QuickShots:** "generates a video" (manual). Output is MP4 in the main folder.
- **MasterShots:** the drone records clips to the card. The templated edit is made in the DJI Fly Editor and does not go on the card (UNVERIFIED).
- **Burst, AEB, Timed:** individual JPG/DNG files in the main folder with no subfolder. Evidence: a Mini 3 Pro owner complained they must regroup AEB sets and timed sequences by hand. UNVERIFIED for the Air 3S.
- **Night mode, Slow-mo, Vertical:** ordinary MP4/JPG.
- **Container formats (Air 3S specs):** stills are JPEG/DNG, video is **MP4 only** (no MOV), file system is **exFAT**, and internal storage is **42 GB**.

## 2. File types, name format, suffix and counter

**File name.** The format is `DJI_<YYYYMMDDhhmmss>_<NNNN>_<L>.<EXT>`. With custom file naming, extra text is appended: DJI staff say "add `_customized name` at the end of the original name". A pattern that tolerates this:
`^DJI_(\d{14})_(\d{4})_([A-Z])(?:_[^.]*)?\.([A-Za-z0-9]+)$`

**Suffix letter**
- **`_D`:** DJI staff say "D stands for the visual camera". On the Mavic 3 Pro, all three cameras write `_D` (forum). The Air 3S tele camera probably also writes `_D` (UNVERIFIED).
- **Enterprise and multi-stream letters:** `_W` wide, `_Z` zoom, `_T` thermal, `_V` visible, `_S` screen/split. The `_S`/`_T`/`_V` triplet was seen on a Mavic 3 Thermal. None of these is expected on the Air 3S.
- **Rule:** accept any single uppercase letter and don't make decisions based on it.

**Counter `NNNN`**
- 4 digits. After formatting in the drone it restarts at 0001 (DJI staff).
- Photos and videos appear to share one counter. This is inferred from gaps in Anvil Mountain: videos are 0001, 0002, 0004–0007, 0011… so 0003 and 0008–0010 went to non-video captures.
- The timestamp keeps DJI_ names effectively unique. **Pano and hyperlapse set folders (`001_0087`) and the files inside them (`PANO_0001`) are not unique across formats.** A later card could produce `001_0087` again and collide at `<photoRoot>\001_0087`. The app needs a collision policy (for example, append the date), which needs a user decision.

**File types**

| Ext | Meaning | Notes |
|---|---|---|
| .MP4 | video | H.264/H.265. Tracks: video + `djmd` telemetry + `dbgi` (seen locally) |
| .DNG / .JPG | photo | RAW+JPEG pairs share a basename |
| .LRF | low-res proxy (720p H.264, MP4 container) | Same folder and basename as the MP4. Cannot be disabled (DJI staff). Seen on the Air 3; Air 3S UNVERIFIED (your library has none, but you may simply never have copied them) |
| .SRT | "Video Captions/Subtitles" telemetry | Only when enabled. Per-frame **naive local** timestamp plus `latitude/longitude/rel_alt/abs_alt` (djiutil sample) |
| .JPG with the same basename as an MP4 | reported per video clip on an Air (DJI_001) | Probably a cover or thumbnail. UNVERIFIED |
| `.<name>.MP4.trinf` (older models also `.avc1`) | recovery journal for an unfinished recording | Hidden. See §0 |
| MISC\*, LOST.DIR, dot/AppleDouble files | system or config | skip |

## 3. Clock and time zone

- **DJI staff:** "The timestamp obtained in the video or photo taken by the aircraft comes from the App, that is, the time after the mobile device is connected to the Internet." With an RC 2, that means the RC's Android date, time and time zone.
- **Fixing the RC 2:** go to Settings > System > Date & time. DJI staff accepted this workaround: turn auto-update off, set the time and time zone, turn auto-update back on. Automatic network time zone often doesn't update without internet; on an RC Pro, DJI told one user they won't fix this.
- **Why your files show US Eastern:** the most likely cause is that the RC 2 time zone is still set to Eastern, possibly since the Newport trip or first setup (UNVERIFIED). Suggest the user fix it. The app must still handle old cards, and the offset may change once it's fixed.
- **No DJI file carries a UTC offset.** DNG EXIF has none (your data; a forum complaint confirms DJI writes no zone to EXIF/XMP). The SRT time is naive. The file name is naive.
- **The only UTC anchor is the MP4 `creation_time`** (your verified fact). Compute the offset per card or per session as filename time minus MP4 UTC, then carry it to DNGs by nearest counter or time. Local date then comes from the GPS position through a time zone lookup.
- **Caution:** an Air 3S owner in Cambodia (2026) measured file-name times 23–30 minutes off real time. Allow some tolerance in time-based grouping.

## 4. How the card appears in Windows

- **USB card reader:** exFAT volume. DriveType is usually Removable, but that's UNVERIFIED for this machine. The drone's volume label is also UNVERIFIED; an older Mavic Air card mounted on a Mac as "Untitled", which suggests no label. Don't rely on DriveType or label. D: "PhotoEdits" is an **exFAT Fixed** drive (SanDisk Extreme Pro SSD, from Get-Volume and the device list), which is why detection must be based on folder structure.
- **Drone plugged in by USB-C:**
  - It shows up as **USB mass storage, not MTP**, so it gets drive letters.
  - DJI support: "Open a local disk…", with "InternalStorage" = aircraft internal storage and "SD_card" = the microSD. Whether these are volume labels or folder names is UNVERIFIED.
  - Supporting evidence: Macs mount it on the desktop; a Neo owner relabelled the internal volume; an Air 2S internal partition shows in Disk Management; a 2026 Air 3 owner: "My Air appears as an external drive".
  - The Air 3S manual says the aircraft need not be powered on. It needs a data-capable cable; installing DJI Assistant 2 provides the drivers if Windows doesn't recognize it.
  - **Scan both volumes:** without a card the drone writes to its 42 GB internal storage, which a card reader never sees.
- **RC 2:**
  - Storage is 32 GB internal plus a microSD slot.
  - Connected by USB it is an Android device. It probably appears as MTP ("This PC\DJI RC 2\Internal shared storage") with no drive letter (UNVERIFIED).
  - It holds only **low-res caches** at `Android\data\dji.go.v5\files\MediaCaches` (DJI staff), not originals. Its SD card has `Android\` and no `DCIM\DJI_###`. Detect it and show a warning instead of importing.

## 5. Detecting a DJI card
1. Check every ready volume (`DriveInfo.GetDrives()`, `IsReady`), skipping the configured video and photo roots.
2. Treat it as a card if `\DCIM\` exists and contains at least one of:
   - a folder matching `^DJI_\d{3}(_.+)?$` (new DJI);
   - `^\d{3}MEDIA$` (legacy DJI or Autel: `100MEDIA`, `MAX_####`);
   - `PANORAMA\` or `HYPERLAPSE\`.
3. Confirm with at least one file matching the DJI file name pattern (or `MAX_\d{4}`).
4. Optional identity check: `\MISC\FC*.db` gives the camera model (FC8282 = Air 3; FC9113 = Air 3S, UNVERIFIED), or read EXIF Model of one DNG (FC9113).
5. If `\Android\data\dji.go.v5\` exists and there is no media folder, it's an RC card: warn, don't import.

## 6. GPS sources for location grouping (the user's request)
- **DNG/JPG:** EXIF GPS latitude/longitude. Fast; you have already confirmed it's present.
- **MP4:** first `djmd` sample (custom reader needed).
- **SRT (if captions are on):** coordinates on line 3 of the first cue. This is the cheapest source by far: a text read, no MP4 parsing. It may be worth suggesting the user turn on Video Captions. The app can read the SRT even if it doesn't copy it.
- **Pano/hyperlapse sets:** EXIF of the first frame.

## Classification rules

| Match (relative to volume root; case-insensitive) | Class | Destination | Notes |
|---|---|---|---|
| `DCIM\<media>\*.MP4` (media = `DJI_\d{3}(_.*)?` or `\d{3}MEDIA`) | **video** | `<videoRoot>\YYYY\YYYY-MM\YYYY-MM-DD Desc\` | Includes QuickShots, MasterShots clips and the hyperlapse composite. Flag if there's no `moov` or a `.trinf` sibling exists |
| `DCIM\<media>\*.DNG` | **photo** | `<photoRoot>\` (flat) | Burst, AEB and timed shots are flat too |
| `DCIM\<media>\*.JPG` with the same basename as a DNG | **photo** (RAW+JPEG pair) | `<photoRoot>\` | Make copying optional (Lightroom treats it as a sidecar by default) |
| `DCIM\<media>\*.JPG` with the same basename as an MP4 | **skip** | n/a | Video thumbnail (UNVERIFIED) |
| `DCIM\<media>\*.JPG` otherwise | **photo** | `<photoRoot>\` | Includes in-drone stitched panos |
| `DCIM\PANORAMA\<set>\*.(DNG\|JPG)` | **photo-set** | `<photoRoot>\<set>\` | Keep the set folder name. **Collision check** (set names repeat after in-drone format) |
| `DCIM\HYPERLAPSE\<set>\*.(DNG\|JPG)` | **photo-set** | `<photoRoot>\<set>\` | Same collision policy. Can be hundreds of frames |
| `DCIM\<media>\*.SRT` | **skip copy; read GPS** | (optional sidecar next to the MP4) | Naive local time + lat/lon |
| `*.LRF` | **skip** | n/a | 720p proxy |
| `.*` (dot files) incl. `.*.MP4.trinf`, `.*.avc1`, `._*` | **skip** | n/a | `.trinf` means the paired MP4 is probably unfinished |
| `MISC\**`, `LOST.DIR\**`, `Android\**`, `System Volume Information\**`, `$RECYCLE.BIN\**` | **skip** | n/a | DJI: no backup needed |
| Any other extension under `DCIM` (.MOV, .HEIC, etc.) | **unknown, needs review** | n/a | Show to the user, never drop silently |

## Housekeeping
- The Playwright browser tool automatically wrote a `.playwright-mcp/` log folder into `/mnt/c/dev/uas-sort` (5 console logs, 5 page snapshots). I deleted it, and the folder is empty again. It held only the tool's own page snapshots and logs, not user data.
- No other writes outside the scratchpad. Throwaway files (manual PDF and text, djiutil clone) are in `/tmp/claude-1000/-mnt-c-dev-uas-sort/187b6b83-e3e1-4971-8c9e-c13c3990d115/scratchpad/spikes/card-layout/`.

## Sources
- Air 3S User Manual v1.0: https://dl.djicdn.com/downloads/DJI_Air_3S/UM/20241108/DJI_Air_3S_User_Manual_v1.0_en.pdf (§4.4 Hyperlapse, §5.10 Storing/Exporting, §5.11 QuickTransfer)
- Air 3S specs: https://www.dji.com/air-3s/specs
- DJI support, export guide (InternalStorage/SD_card): https://support.dji.com/help/content?customId=en-us03400006743&spaceId=34&re=us&lang=en
- DJI support, backup paths and MISC/LOST.DIR: https://support.dji.com/help/content?customId=en-us03400007800&spaceId=34&re=US&lang=en&documentType=artical&paperDocType=paper
- DJI forum (staff: "D stands for the visual camera", counter reset, custom suffix): https://forum.dji.com/thread-300448-1-1.html
- DJI forum (staff: time comes from the App/RC): https://forum.dji.com/thread-301764-1-1.html
- DJI forum (RC 2 time fix, DST/UTC issue): https://forum.dji.com/thread-221739-1-1.html
- DJI forum (auto time zone broken; no GMT zone in EXIF): https://forum.dji.com/thread-268052-1-1.html
- DJI forum (staff: HYPERLAPSE/PANORAMA/composite paths, Air 3): https://forum.dji.com/thread-312410-1-1.html
- DJI forum (hyperlapse originals Off/JPEG/RAW, `100_XXXX` set folders): https://forum.dji.com/thread-226525-1-1.html and https://forum.dji.com/thread-271295-1-1.html
- DJI forum (999 files/folder, 100MEDIA→101MEDIA, staff): https://forum.dji.com/thread-277539-1-1.html
- DJI forum (custom folder naming → `DJI_002_A01`): https://forum.dji.com/thread-278815-1-1.html
- DJI forum (hidden `.MP4.trinf` + `.avc1` with a corrupt MP4): https://forum.dji.com/thread-188922-1-1.html and https://forum.dji.com/thread-199777-1-1.html
- DJI forum (LRF can't be disabled, staff): https://forum.dji.com/thread-280099-1-1.html
- DJI forum (RC 2 MediaCaches path, staff): https://forum.dji.com/thread-309359-1-1.html and https://forum.dji.com/thread-300834-1-1.html
- DJI forum (Air `DCIM\DJI_001`, MISC `FC8282.db`, IDX/THM): https://forum.dji.com/thread-301869-1-1.html and https://mavicpilots.com/threads/extra-files-created-on-media-card.142315/
- mavicpilots (Air 3: MP4+SRT+LRF; USB without power): https://mavicpilots.com/threads/how-do-i-get-videos-and-photos-off-my-new-air-3.141360/
- mavicpilots (Air 3S pano originals, separate format setting, PANO_ names): https://mavicpilots.com/threads/how-to-use-panorama-flight-modes-with-air-3s.148904/ and https://mavicpilots.com/threads/air-3s-panorama-samples.148930/
- mavicpilots (Air 3S file-name clock 23–30 min off, 2026): https://mavicpilots.com/threads/time-in-remote-control-is-30-minutes-ahead.156335/
- mavicpilots (RC time zone affects file times): https://mavicpilots.com/threads/wrong-date-time.147859/
- mavicpilots (DJI_001 dir vs 8-char DCF; drone as USB storage): https://mavicpilots.com/threads/mini4-pro-sd-card-transfer-via-card-reader-changed-due-to-software-update.143219/
- mavicpilots (Air appears as external drive, 2026): https://mavicpilots.com/threads/transferring-files-from-drone-to-pc.155478/
- mavicpilots (_S/_T/_V on Mavic 3 Thermal; W/Z/T on enterprise): https://mavicpilots.com/threads/file-naming-ends-with-s-t-or-v.134826/
- mavicpilots (trinf = unfinalized recording, fixed on reboot): https://mavicpilots.com/threads/what-are-these-2-files-please.74596/
- djiutil (DCIM/DJI_001 + MISC layout, SRT format): https://pypi.org/project/djiutil/ and https://github.com/will2dye4/djiutil
- Droneblog (Air 3 USB access, no power needed): https://www.droneblog.com/dji-air-3-connect-to-computer/ ; Neo internal volume: https://www.droneblog.com/dji-neo-access-internal-storage/
- RC 2 specs (32 GB + microSD): https://www.dji.com/rc-2/specs