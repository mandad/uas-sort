# uas-sort: scoring of the three designs and a combined recommendation

## Yes, the files carry coordinates

- **Photos have them.** DNG EXIF includes GPS latitude, longitude and altitude. PANO_0001.DNG gives 57.799648, −152.390180, 143.67 m, read with MetadataExtractor from only 94 KB of the file. There is no GPS time and no UTC offset in the photo.
- **Videos have them too, but not in EXIF.** Every DJI MP4 records GPS for each frame in its djmd telemetry track.
  - A reader that only looks at the first sample needs about 2 KB of the file and 3–50 ms.
  - Its results matched `exiftool -ee3` exactly on every file tested. exiftool took 7–87 s per file.
  - It also recovers GPS from the two broken clips that have no moov box (Anvil 0014 and 0024).
- **Legacy Autel files have no GPS.** They can only be grouped by time.
- **So location grouping works for every DJI item.** The rule I recommend:
  - A clip more than **R = 25 km** from its group's centre starts a new group, whether it's the same day or the next day.
  - Consecutive local days join (**G = 1**).
  - This splits Council Road from Anvil Mountain (53 km apart, consecutive days). It rebuilds all 8 of your existing folders.

## What I checked in this pass

All checks were read-only, and nothing was written to user data or `/mnt/c/dev/uas-sort`.
- **Grouping tests:** `spikes/grouping/test_grouping.py`, 37 tests, all pass (0.006 s).
- **Replay of the real library** (`replay.py`, names, sizes and mtimes only):
  - It gives 8 groups, matching your 8 folders.
  - Every R from 12.5 to 53 km works, and every G from 1 to 11 days works.
  - Scenarios A–E give the expected decisions.
  - The newest imported video is at 2026-09-27 18:24:16Z. Photos after that time count as new ("watermark").
- **Simplified rules (MVP design):** `minimal_cluster.py` and `minimal_decide.py` also give 8/8, with the same R and G ranges. Scenario C appends to Zachar Bay (4 new clips); D and E create a new folder, 2026-07-26 (21 clips).
- **Listing doesn't download cloud-only files:** after the replay, all 30 Kodiak files still show attributes `0x401620` (cloud-only).
- **New finding, `spikes/judge/crossday.py`:** no library folder has GPS on more than one day. Kodiak spans 5/22–5/25 but is cloud-only, so its GPS can't be read. Any radius from 2 to 25 km for day-to-day links still rebuilds all 8 folders. **Your data doesn't show how far apart two consecutive days can be and still belong in one folder.** That's open question 1.
- **Corrections to the project notes:**
  - Zachar Bay has 13 MP4s, not 15.
  - Kodiak's first clip was at 5/22 21:52 AKDT, not 17:52. The folder date (5/22) is unchanged.
  - Windows has only .NET SDK 9.0.316. There is no 8.0 SDK.

---

## 1. Scores

| Criterion | MVP | Safety | UX | Why |
|---|---|---|---|---|
| Requirement fit | 8 | 8 | 9 | All three do radius and big-jump grouping, appending to folders, and your photo rule. UX shows why a split happened ("54 km jump · 21 h"), which is exactly what you want to judge. MVP drops GPS for truncated clips and has no name suggestions. |
| Data safety | 8 | 10 | 8 | All three verify each copy before it gets its final name, and never overwrite. Safety adds a journal, crash recovery, a re-check of card files and compile-time bans on risky calls. With temp-then-rename, simply re-running the others has the same effect for one user. |
| Simplicity | 9 | 3 | 5 | MVP: 3 projects, 2 runtime packages. Safety: 11 projects, a journal and a test of a crash at every step. UX: 7 projects, a map component, drafts, sliders and an undo stack. |
| Review UX | 6 | 7 | 10 | MVP: expandable groups, no split explanations, no undo, no thumbnails. Safety: an 8-screen wizard (heavy for a personal tool). UX: split chips, one-click merge, undo, thumbnails, map. |
| Testability | 8 | 10 | 8 | All three have a pure planning core and a fake file system. Safety adds the crash tests and property tests. UX keeps view models free of WPF so they test on Linux. |
| Delivery effort | 9 | 3 | 5 | 5–6 days, 27–35 days and 20–24 days of developer time. |
| **Total** | **48** | **41** | **45** | |

## 2. Winner: the MVP design, with the UX design's split explanations added

The MVP design's safety steps are the same as the others': copy to a temp file while hashing, flush, re-read to verify, rename without overwriting, then record it in the ledger. It is also the only one that fits a personal tool you launch by hand. Its weakest point is the review screen, and the location grouping you asked for only helps if you can see *why* each split happened. So the combined design is the MVP core plus UX's split explanations.

### Ideas to add from the runners-up

From **UX**:
1. **Split explanations.** The grouping step returns each boundary with its cause: day gap, distance, existing folder, or a split you made. The screen shows each one as a chip, e.g. "── 54 km jump · next day ── [Merge]" or "── 62 days ──". This makes the GPS rule visible and one click to undo.
2. **Undo and redo.** Plans can't be modified in place, so undo is just a stack of earlier plans (about 20 lines).
3. **Thumbnails.** Each DJI MP4 already contains a 160×90 JPEG (12.7 KB). Record its position while probing and show it in the clip list. Autel files and truncated clips get a placeholder.
4. **"Safe to format?" verdict** with three levels: Safe, Safe with assumptions, Not safe.
5. **A trip-wire in the fake file system:** opening any file flagged 0x400000, 0x40000 or 0x1000 (cloud-only markers) throws. The real placeholder check refuses to read anything when run off Windows.
6. **Keyboard shortcuts:** F2 rename, Ctrl+M merge, Ctrl+Shift+S split, Space include, Ctrl+Z/Y.

From **Safety**:
1. **Re-check each card file** (size and mtime) just before copying it. If it changed, report it and skip it.
2. **Rename with write-through** (`MoveFileEx(MOVEFILE_WRITE_THROUGH)`, never replace). D: is exFAT, which has no journaling. Also mark temp files Hidden.
3. **Keep the PC awake** during a copy (`SetThreadExecutionState`), and **allow only one instance** (named mutex).
4. **Per-file audit categories** behind the verdict:
   - hash-verified this run;
   - in the ledger;
   - matched by name and size only;
   - only assumed imported by the photo rule;
   - skipped by rule;
   - unaccounted.
5. **A list of banned calls** (`BannedSymbols.txt` with Microsoft.CodeAnalysis.BannedApiAnalyzers, used at build time only). `File.Delete`, `File.Copy`, `File.Move`/`File.Replace` with overwrite, `FileMode.Create`/`Truncate`/`OpenOrCreate`, and `DateTime.Now` are allowed only inside the file-access layer.
6. **A dry-run CLI** (both Safety and UX had one), cut down to `plan --card <dir> --json`. The core targets `net10.0`, so this runs in WSL against a copy of a card folder. That is the fastest way to check the first real card without the UI.

From the **research reports** (the MVP design cut too far here):
1. **Recover GPS from truncated clips** by reading the start of the video data at offset 512 (about 30 lines, verified on Anvil 0014 and 0024). Two such clips already exist in the library.
2. **Search further for a first GPS fix:** samples 0–9, then 16, 32, 64…, then the last sample (about 15 lines, covered by the synthetic tests). MVP only tried the first and last samples.
3. **A table of where each drone model stores GPS, plus a generic search for latitude/longitude**, so a new drone doesn't silently lose GPS.
4. **Offline place-name suggestions** from a filtered GeoNames file: 0.27 MB gzipped for Alaska, 7.7 MB for the US. It found "Zachar Bay" 0.7 km away and "Anvil Mountain" 0.3 km away from your clips. This goes in the last milestone and can be dropped.
5. **CommunityToolkit.Mvvm 8.4.2** instead of hand-written MVVM plumbing. It's MIT-licensed and generates the repetitive code.

### What to cut from the MVP design
- The 3 h guard (H) as a user setting. It only matters when G = 0, so make it a constant.
- A separate warning path for DJI RC 2 controller storage. Say "No DJI media found on this drive", and mention RC caches in the text.
- A separate place to store the learned clock offset. Keep it only as the settings fallback, updated after each run.

### Don't add these
- **From UX:** the map component (its .NET 10 compatibility is unverified, plus OpenStreetMap tile policy), the R/G sliders, draft autosave, the placeholder timeline before scanning finishes, parallel reads, online name lookups (Nominatim), auto-rescan when a card is inserted, and the 7-project layout. **Replace the map with an "Open in map" button** that opens OpenStreetMap at the group's centre in the browser.
- **From Safety:** the journal, the recovery service and the crash-at-every-step tests. Temp files plus idempotent re-runs cover the same failures. Also leave out: a separate validator component (fold it into preflight), FsCheck, the 8-screen wizard, offloading several volumes at once, "ignore forever", and "paranoid" re-reading of the card.

---

## 3. Recommended design

### 3.1 Architecture and stack

**Runtime**
- .NET 10 LTS, SDK 10.0.401. Install it on Windows first: .NET 8 and 9 reach end of support on 2026-11-10.
- Use the dotnet CLI or VS Code. VS 2022 17.14 can't target .NET 10 (error NETSDK1233).

**Projects** (repo at `C:\dev\uas-sort`)
- `UasSort.Core` (net10.0): no UI, no Windows calls outside `Io/RealFileOps`.
- `UasSort.App` (net10.0-windows, WPF, `ThemeMode="System"`).
- `UasSort.Cli` (net10.0): dry-run planning only.
- `UasSort.Core.Tests` (xUnit): runs in WSL with the Linux SDK and on Windows.

**Packages**
- Runtime: MetadataExtractor 2.9.3, GeoTimeZone 6.1.0, CommunityToolkit.Mvvm 8.4.2.
- Build only: BannedApiAnalyzers.
- Built into .NET, no package needed:
  - SHA-256 hashing (`IncrementalHash`). It runs at 2 GB/s, far faster than any SD card, and avoids another package.
  - System.Text.Json.
  - `TimeZoneInfo` with IANA IDs through ICU. Never set `InvariantGlobalization` or `UseNls`.
- Not used: ExifTool, SQLite, WinUI, Avalonia, a map component.

**Publishing:** a framework-dependent single-file win-x64 exe, about 4.9 MB measured (plus the GeoNames file if added). It needs the .NET 10 Desktop Runtime installed.

### 3.2 Components

| Component | Responsibility | Interface |
|---|---|---|
| **Io** (`IFileOps`, `GuardedFileOps`, `RealFileOps`) | The only file-system access in the app. **Guard rules:** never open library files (except our own `*.uas-sort.tmp`), never write under the card root, delete only our own temp files. **Real implementation:** create-new with preallocation; `Flush(true)`; unbuffered verify read with 4096-aligned buffers (buffered fallback); write-through rename without replace; Hidden temp files. | `List(root, includeHidden)`, `OpenRead`, `CreateNew(path, len)`, `OpenVerify`, `SetTimes`, `MoveNoOverwrite`, `DeleteTemp`, `EnsureDirectory`, `Stat`, `FreeBytes`; `FileEntry(FullPath, Size, MtimeUtc, RawAttr)` |
| **CardLocator** | Finds cards from the list of ready volumes by folder structure: `DCIM\DJI_\d{3}(_.+)?`, `\d{3}MEDIA`, `PANORAMA`, `HYPERLAPSE`. Doesn't rely on DriveType (D: reports as Fixed exFAT). Skips network and CD drives and the drives holding the two roots. | `Find(volumes, settings, fs) → CardCandidate[]` |
| **CardScanner + FileClassifier** | Scans every DCIM media folder and classifies each file:<br>• **Video**<br>• **Photo:** a DNG and JPG with the same name count as one item<br>• **Set item:** one per pano or hyperlapse folder<br>• **Skip:** dot files (a `.trinf` flags its MP4 as truncated), LRF, SRT, MISC, LOST.DIR, system folders<br>• **Unknown:** listed, never dropped silently | `Scan(root, fs) → CardInventory`; `Classify(relPath, mp4Stems) → FileClass` |
| **Mp4Probe** (our own code, about 350 lines) | Port of `djmd_gps.py`:<br>• walks boxes lazily, never reads the video data or the whole `moov`<br>• creation time as UTC; whether `moov` is present<br>• first GPS fix, using the per-model table plus the generic search<br>• searches samples 0–9, then 2^k, then the last<br>• fallback at offset 512 when there's no `moov`<br>• position of the thumbnail | `Read(Stream) → Mp4Info(CreatedRaw, HasMoov, Gps?, Protocol?, ThumbRange?)` |
| **StillProbe** (MetadataExtractor) | Reads DateTimeOriginal from the EXIF directory that actually contains it (in DNGs, the first sub-IFD has none). Also GPS, camera model, and the embedded thumbnail's position. | `Read(Stream) → StillInfo(DtoNaive?, OffsetTime?, Gps?, Model?)` |
| **DroneClock, TimeResolver, ISiteZone** | **Drone-clock offset:** each MP4 gives (filename time, filename time − creation time, rounded to 15 min). For other files use the nearest sample within 60 days, else the card's most common offset, else the setting (−240 min).<br>**Capture time**, first rule that applies:<br>• MP4 creation time<br>• EXIF DateTimeOriginal plus its UTC offset<br>• drone-clock time minus the offset<br>• Autel wall clock, taken as local time<br>• file time<br>**Time zone:** from the item's own GPS, else the nearest GPS item within 12 h, else the PC's zone. Ocean `Etc/*` results fall back to the PC's zone.<br>**Badges:** "check date" when GeoTimeZone returns alternatives near midnight; "clock not set" for times before 2015 or in the future. | `DroneClock.Learn(samples, fallback)`, `OffsetNear(stamp)`; `Resolve(item, probe, clock, zones) → MediaTime(CaptureUtc, LocalDate, Source, TzId, Gps?, Flags)` |
| **LibraryIndex** | Built from directory listings only; it is never given a stream. Holds:<br>• (lowercase name, size) keys over both roots (the photo root is inside the video root and holds old `MAX_*.MP4`)<br>• name→size, to spot conflicts<br>• pano set folders with their members<br>• event folders at any depth matching `^\d{4}-\d{2}-\d{2}( .*)?$`, excluding the photo root's subtree<br>• DJI filename times for folder date ranges and the watermark | `Build(videoListing, photoListing, roots)`, `Has`, `SizeOf`, `FolderOf`, `HasSet`, `SetNameTaken`, `Folders`, `Watermark(clock)` |
| **LedgerStore** | Append-only `ledger.jsonl`, one line per verified file, flushed to disk. Tolerates a torn last line. Folder centre and date range are worked out from the entries when loading. | `Load() → LedgerSnapshot` (`Has`, `CentroidOf`, `Places`, `LastVideoUtc`); `Append(LedgerEntry)` |
| **Newness** | **Videos:** Imported (in the library or ledger); Conflict (same name, different size; can't be ticked); New. Truncated clips are New but unticked.<br>**Photos and sets:** Imported (in the ledger, or name and size in the photo root; for sets, the set name and all members); New (after the watermark, or on a day with new videos); otherwise ProbablyImported, with the reason shown. | `Video(item, lib, led)`, `Photo(item, time, lib, led, newVideoDates, watermark)` |
| **Clusterer** | Groups **videos only**, in order of capture time. Starts a new group on a time split, a distance split, or when a clip belongs to a different existing library folder (existing folder boundaries are never crossed). Clips without GPS never trigger a distance split and never move the centre. **Returns boundaries with their cause.** | `Cluster(items, GroupingSettings) → (Groups, Boundaries)`; `Boundary(Cause: DayGap\|Distance\|LibraryFolder\|User, Km?, Gap)` |
| **FolderDecider, FolderNamer, DescriptionSuggester** | Chooses each group's target (§3.5). New folder path: `<videoRoot>\YYYY\YYYY-MM\YYYY-MM-DD <desc>` from the start local date. `Clean()` keeps commas and caps names at 80 characters. Description suggestions, in order: existing folder's name, a previous ledger name within R, GeoNames feature or town (milestone 5), blank. | `Decide(g, lib, led, clock, zones, s) → Target{AlreadyImported \| Append(F, conf) \| NewFolder(start)}`, `AppendCandidates`, `NewFolderPath`, `Clean`, `Suggest` |
| **Planner + PlanEditor** | A pure pipeline that produces a plan that can't be modified in place. Each edit returns a new plan and re-runs the target decision only on the groups it touched; it never re-clusters. Merging two groups that append to two different existing folders is refused. | `Build(...)`, `Merge`, `SplitBefore`, `SetDescription`, `SetTarget`, `SetIncluded(itemOrDay)`, `Jobs(plan) → CopyJob[]` |
| **CopyEngine + CardAudit** | Preflight checks, then the per-file protocol in §3.6, then the audit verdict. | `Preflight(jobs, fs) → issues`, `RunAsync(jobs, fs, ledger, progress, ct) → CopyReport`, `Audit(inventory, plan, report, lib, led) → Verdict` |
| **SettingsStore** | `%LOCALAPPDATA%\uas-sort\settings.json`: roots, R, G, clock fallback. Written to a temp file, then swapped in with `File.Replace`. | `Load`, `Save` |
| **App** (WPF) | One MainWindow and its view models. Undo is a stack of plans. Keeps the PC awake while copying, and allows one instance only. | `MainViewModel`, `GroupViewModel`, `ItemViewModel`, `PhotoDayViewModel` |
| **Cli** | `uas-sort plan --card <dir> [--video-root …] [--photo-root …] --json`. Always a dry run. | |

### 3.3 Data flow

1. **Launch.** Load settings and the ledger, then look for a card. If one is found, select it. Otherwise show Rescan and "Browse to folder…".
2. **Scan the card.** List it through `GuardedFileOps` and classify every file. Unknown files are listed; `.trinf` files flag their MP4 as truncated.
3. **Read metadata**, one file at a time with a progress bar. MP4s read about 2 KB each, DNGs about 94 KB, and each pano set only its first frame. If reading fails, the item falls back to filename or file time with no GPS, and the error is shown.
4. **List the library.** Both roots, names, sizes and mtimes only, feeding `LibraryIndex.Build`. The guard makes it impossible to open a library file.
5. **Work out time and place.** Learn the drone-clock offset from the MP4s on the card (−4 h on every folder so far). Then give each item a capture time in UTC, a time zone from GPS, and a local date. Worked example: Zachar Bay clip 0128 has drone time 14:06:27, creation time 18:06:27Z, zone America/Anchorage, local date 2026-09-27.
6. **Decide what's new.** Videos first. Then collect the local dates that have new videos and compute the watermark. Then classify the photos.
7. **Group the videos and decide each group's folder.** Clusterer, then FolderDecider, then name suggestions. Photos are listed by local day for display only; they always go flat to `<photoRoot>\name`, or `<photoRoot>\<set>\` for pano and hyperlapse sets.
8. **Review.** Every edit is a PlanEditor call that returns a new plan, with undo and redo.
9. **Preflight:** free space per destination drive (total plus 1 GiB), roots reachable, no existing name with a different size, leftover temp files of our own removed.
10. **Copy and verify** each file (§3.6), adding a ledger line after each verified file. Afterwards, save the last learned clock offset to settings.
11. **Audit.** Show the verdict and the list of files that weren't copied, with reasons. Nothing is ever written to the card.

### 3.4 Review screen (one window)

**Header**
- A card picker (label and drive letter), Rescan and Browse.
- Video root and photo root, each with a picker.
- A clock line: "Drone clock UTC−4, learned from 13 videos; folder dates use local time at the site."
- A first-run note while the ledger is empty.

**Groups** (oldest first; groups already in the library fold into one grey row)
- Each group header shows:
  - an include checkbox and the local date range with a time zone badge;
  - a target dropdown: New folder, or "Append to …" with high or medium confidence;
  - a description box with suggestions, and a preview of the full path;
  - clips (new/total) and GB;
  - the centre, the spread in km, and an "Open in map" button;
  - thumbnails.
- **Between groups is a boundary chip** giving the cause, with **[Merge]**.
- Inside a group, a clip grid: include, thumbnail, name, local time (tooltip shows the time source), size, latitude/longitude, km from the centre, and a status badge (New, Imported, Conflict, Truncated).
- Hovering between rows shows "Split here". Where the local day changes inside a group, a split point is suggested.

**Photos**
- Grouped by local day. Each day has a tri-state checkbox, a count, and a reason: "day has new videos", "after last imported video 2026-09-27 10:24 AKDT", or "probably imported: videos from this day already in library".
- Pano and hyperlapse sets are one row each, showing the target folder and any name-collision suffix.

**Skipped and Unknown:** a collapsed section.

**Footer**
- "Copy 17 files · 9.4 GB (C: 8.1 GB, D: 1.3 GB)".
- Copy, and while copying a progress bar, the current file and phase, and Cancel.
- **Afterwards, the verdict:** "Safe to format", "Safe with assumptions: 40 photos only assumed imported", or "Don't format yet: …". Each comes with counts per audit category.

Settings such as R and G live in `settings.json`; there's no settings dialog.

### 3.5 Grouping and new-file rules

**Grouping**, videos only, taken in order of capture time:
1. **Time split:** the local date is more than **G = 1** day after the previous clip, **and** more than 3 h have passed. The 3 h is a fixed constant; it stops a flight crossing midnight from being split.
2. **Distance split:** the clip has GPS, the group has a centre (the average of unit vectors, which is safe across the date line), and the distance to the centre is more than **R = 25 km**. This covers both "a radius around where they're taken" and "a big jump, even on the next day".
3. **Existing folders are hard walls:** a clip already imported into folder F never shares a group with clips imported into a different folder.

A visit to site A, then B, then A again on one day stays as 3 groups; you merge them in the UI.

**Choosing each group's target**

| Situation | Target |
|---|---|
| No new clips | AlreadyImported |
| Some clips are already imported into folder F | Append to F (high confidence) |
| F's name date ≤ group start ≤ F's end + G, and both locations are known and within R | Append to F (high) |
| As above, but one location is unknown and the dates overlap | Append to F (medium, with a badge) |
| Dates only adjacent and a location unknown, or more than R apart | New folder (appending still offered in the dropdown) |

Clips are **never** appended to a folder whose name date is later than the clips. Folder locations come from the ledger, or from clips still on the card that match library files.

**Calibration evidence** (re-verified today)

| Evidence | Value | What it means |
|---|---|---|
| Largest spread inside one folder | 12.5 km, Nome Roads (a 12 km drive in 26 min). Others: Newport 5.4, Zachar 1.83, Anvil 1.25, Council 1.54 km | R must be above 12.5 km |
| The one consecutive-day pair you split | Council Road → Anvil Mountain: 53.2 km apart, 21.4 h later | R must be below 53 km |
| Two sites on the same day | Zachar Bay ↔ Kodiak town: 84.7 km | Split even on the same day |
| Shortest gap between folders in time (other than that pair) | Newport → Kodiak: 12.3 days | G must be 11 days or less |
| Closest pair of folders in distance | Nome Roads ↔ Anvil: 14.1 km (22 days apart) | At R = 25, flights in the same area on consecutive days would merge (open question 1) |
| Multi-day folders with GPS | None (Kodiak's files are cloud-only) | How far apart consecutive days can be is not constrained by your data |
| Replay | 8/8 folders for R 12.5–53 km and G 1–11 days | The defaults sit in the middle of both ranges |

**New files**
- **Videos:** key is (name, size) against both roots plus the ledger. A ledger hit counts as Imported even if you've since removed the file from the library.
- **Photos** (your rule): in the ledger, or name and size in the photo root, means Imported. Otherwise New if taken after the watermark (currently **2026-09-27 18:24:16Z**) or on a local date with new videos. Otherwise ProbablyImported and unticked, with the reason shown.
- **Pano and hyperlapse sets** count as imported only if the set name and every member's name and size match, because `PANO_000N` names repeat across sets.

### 3.6 Errors and recovery (no journal needed)

**Per-file copy**
1. Re-check the card file's size and mtime.
2. Copy it to `<dest>.uas-sort.tmp` (Hidden, create-new, preallocated), hashing with SHA-256 as it goes.
3. `Flush(true)`.
4. Copy the source's creation and modified times.
5. Re-read the temp file unbuffered and compare hashes.
6. Rename with write-through, never replacing.
7. Append the ledger line and flush.

A file only ever gets its final name after it has been verified.

**Recovery**
- Leftover `*.uas-sort.tmp` files (our suffix only) are removed at preflight.
- Re-running is safe: finished files show as Imported by name and size.
- If a crash lands between the rename and the ledger line, the file still shows as Imported by name and size.

**Failures during a copy**
- **Hash mismatch:** delete the temp file, retry once, then mark Failed.
- **Target already exists at rename time** (error 0x800700B7): Conflict. Nothing is overwritten.
- **Card pulled, disk full, or root unreachable** (for example D: unplugged): delete the current temp file and stop. The summary lists what finished.
- **Unbuffered verify fails:** re-read buffered, and say so in the report.
- **Cancel:** delete the current temp file and stop.
- **A ledger line can't be written:** stop the run.

**Other cases**
- **Destination exists with the same size:** AlreadyThere. The ledger records it as "name+size, not hashed", because cloud-only files can't be read.
- **Truncated MP4** (no `moov`, or a `.trinf` beside it): New but unticked. It still gets GPS from the fallback. The badge suggests powering the drone on with the card inserted to repair it (UNVERIFIED on the Air 3S).
- **The guard is tripped** (`UnsafeIoException`): stop, and treat it as a bug.

Your library already holds two such broken clips, Anvil `…0014` and `…0024`.

### 3.7 Testing

- **Grouping and decisions:** port the 37 Python tests, dropping the power-on-session and photo-attach cases this design removes. Add:
  - existing folders as hard walls;
  - the boundary cause recorded for each split;
  - undo and redo;
  - a merge across two existing folders is refused.
- **Replay fixture:** the library listing (paths, sizes, mtimes) plus the GPS from `calibration.json` and `more_gps.json`. Assert 8/8 folders, the R and G ranges, and scenarios A–E.
- **MP4 reader:** a synthetic MP4 builder ported from `synth_test.py`:
  - `moov` at the start and at the end;
  - 64-bit `mdat` and `co64`;
  - several `stsd` entries;
  - GPS first found at sample 7, at sample 32, or never;
  - no `moov` (fallback path);
  - the thumbnail position.
- **Real-file checks, optional:** run when the environment variable `UASSORT_GOLDEN` points at local files. Zachar Bay 0128 must give 57.5504420579265, −153.738972972093 and 18:06:27Z; PANO_0001 must give DateTimeOriginal 2026-05-25 09:30:28 and 57.799648, −152.390180.
- **Time:**
  - a midnight case (a flight at 7/26 03:50 drone time is 7/25 local);
  - Hawaii 2/28;
  - a DST offset change;
  - a card with no MP4;
  - Autel wall-clock times;
  - an `Etc/*` ocean zone falling back.
  One Windows-only test uses the real GeoTimeZone.
- **Fake file system:**
  - the cloud-only trip-wire over a full scan, plan and offload of a library whose files all carry 0x401620;
  - the guard rules;
  - the copy engine with injected faults: hash mismatch, disk full, card pulled, target appearing before the rename, cancel.
- **Windows integration** (in `%TEMP%\uas-sort-test`, cleaned up afterwards): unbuffered verify, write-through rename refusing to replace, timestamps preserved.
- **Acceptance:** run `uas-sort plan` on the first real card and compare with what you expect. Then do one rehearsal copy into scratch roots before using the real ones.

### 3.8 Out of scope, or later

- **Not built:** tray icon, service, auto-launch and auto-rescan on card insert.
- **Maybe later:**
  - a map pane;
  - online name lookups;
  - offloading the drone's internal storage and its SD card together;
  - thumbnails bigger than 160×90;
  - R and G controls in the UI.
- **Never:**
  - changing the card: no deleting, formatting or repairing;
  - changing or reading existing library files, including the Lightroom catalog;
  - bundling ExifTool, SQLite or a journal;
  - copying LRF or SRT files;
  - parallel copies or resuming half-copied files.

**Effort:** about **8–9 developer days**, or 7–8 without the GeoNames suggestions.

| Milestone | Work | Days |
|---|---|---|
| M0 | Install SDK 10, set up the solution | 0.25 |
| M1 | File-access layer and guard, card scanning, MP4 reader and synthetic tests | 1.5 |
| M2 | Photo reader, drone clock, time zones, library index, ledger, settings | 1 |
| M3 | Planning (new-file rules, grouping with boundaries, folder decisions, editor), ported tests and replay fixture, CLI | 2 |
| M4 | Copy engine and audit, fault tests, Windows integration test | 1.5 |
| M5 | WPF screen, then GeoNames suggestions | 2 |
| M6 | Dry run on a real card, then publish | 0.5–1 |

---

## 4. Open questions for you

1. **Flights in the same area on consecutive days.** Nome Roads and Anvil Mountain are 14 km apart. If you flew them on back-to-back days, should they be one folder or two? Your data can't settle this; see the cross-day test above.
   - **Recommended default:** a single R = 25 km, so they merge, with the day change shown as a one-click split point.
   - **Alternative:** a tighter radius of about 5 km for links between days.
2. **Photos on the first run.** How do DNGs leave Picture Offload: does Lightroom move them, or do you clear the folder by hand? Almost none from 2026 are there.
   - **Recommended default:** assume they leave, so the app's own record is the long-term memory. Photos before the watermark on days whose videos are already imported stay unticked, per your rule, with a "don't format yet" warning and one click to include a whole day.
   - **Alternative:** tick everything on the first run and rely on Lightroom to skip duplicates.
3. **Pano/hyperlapse folder name clashes.** When `001_0087` already exists in the photo folder with different photos in it, what should the new set folder be called?
   - **Recommended default:** add the date, e.g. `001_0087 2026-09-27`. Sets whose folder name is free keep the plain name.
4. **Same video name, different size.** For example, if the drone later repairs the broken Anvil 0014.
   - **Recommended default:** unticked. If you tick it, the app copies it as `DJI_…_0014_D (2).MP4` and never replaces the library file.
5. **Learning folder locations from clips already downloaded to this PC.** This would read about 2 KB from library clips that aren't cloud-only, which bends your "never read library content" rule.
   - **Recommended default:** off. Clips still on the card and the app's own records cover it.

## 5. Approaches to show the user

1. **Recommended: a lean app with an explained review screen (about 8–9 days).**
   - A small core does the scanning, the GPS and time grouping, and the copy-then-verify. One review window shows why every group was split ("54 km jump · next day") with one-click merge, split and undo, plus thumbnails and a "safe to format?" verdict.
   - Trade-offs: no map (an "Open in map" button instead), and no crash journal (re-running after an interruption is safe instead).
2. **Maximum safety (about 27–35 days).**
   - Adds a step-by-step copy journal with crash recovery, compile-time guards, tests that crash at every step, an 8-screen wizard and a full command-line tool.
   - Trade-offs: the safest option, but three to four times the work. The lean design's copy steps already mean no file ever gets its final name unverified.
3. **Richest review screen (about 20–24 days).**
   - A map with group-coloured dots, radius circles and labelled jumps, R/G sliders with live regrouping, draft autosave and a skeleton view while the card is still being read.
   - Trade-offs: the nicest review experience, but it depends on an unverified map component and OpenStreetMap tile rules. It's more UI than a personal tool you open a few times a month needs; the recommended design keeps its most useful parts (split explanations, undo, thumbnails).

Files are in `/tmp/claude-1000/-mnt-c-dev-uas-sort/187b6b83-e3e1-4971-8c9e-c13c3990d115/scratchpad/spikes/`:
- `judge/crossday.py`
- `grouping/test_grouping.py`
- `grouping/replay.py`
- `minimal-arch/minimal_cluster.py`
- `minimal-arch/minimal_decide.py`