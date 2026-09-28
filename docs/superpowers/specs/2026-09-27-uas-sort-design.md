# uas-sort — design spec

**2026-09-27 · Approved.** The user approved every design section on 2026-09-27.

The companion [design reference](2026-09-27-uas-sort-design-reference.md) ("Ref §n") holds the full types, tables, test lists and review disposition. **If the two disagree, this spec wins.**

UNVERIFIED means not yet proven on this hardware. Each UNVERIFIED item has a milestone check or a fallback, and none waits on the user.

---

## 1. Summary

uas-sort is a personal Windows 11 app, launched by hand on any of the user's PCs, that offloads a DJI Air 3S card into a OneDrive-synced library.

It lists card and library (**library: listings only, never content, except the app's own `.uas-sort\ledger*.jsonl`**), reads card metadata (MP4 `mvhd` time, `djmd` GPS, DNG EXIF), finds what is new, groups new videos by site-local date and GPS into `<videoRoot>\YYYY\YYYY-MM\YYYY-MM-DD <Description>\` (here `C:\Users\damia\OneDrive\Pictures\UAS Videos`, many files cloud-only), sends photos flat to `<photoRoot>` (sets keep their folder), lets the user review and edit, copies with verification (never overwriting, never writing the card), and ends with an itemised **"safe to format?"** verdict.

**Goals** (Ref §1.3; non-goals §1.4): **G1** zero-risk offload — hashed while written, re-read unbuffered before the final name; durability from `FlushFileBuffers`, a post-rename size check, a non-NTFS directory flush and safe removal, not write-through. **G2** every proposal explains itself. **G3** one-gesture, undoable fixes; drafts survive restarts. **G4** an honest verdict on the card in the reader. **G5** native Windows 11, < 1 s start.

**Success criteria** (Ref §1.5)
1. **Mechanical safety.**
   - File-system types are banned outside Platform; the probe build raises RS0030 once per banned kind.
   - The table-driven `IoGuardPolicy` test passes, and the fake-FS hydration tripwire passes over scan, plan and offload.
   - Windows tests prove: the rename never replaces; verify is unbuffered, or the fallback is recorded; the lister opens nothing; no pre-existing library file is opened except `.uas-sort\ledger*.jsonl` (read) and the own ledger (append), while this run's own `*.uas-sort.tmp` and just-renamed files are tracked exceptions; the card reader works under a deny-write ACL.
2. **Golden replay** (the checked-in fixture, Ref §13).
   - Scenario A0 (card = every library video, empty library) at R 50 mi / G 1 → **7 groups**.
   - Council/Anvil's emphasised ~34 mi split → the user's 8 folders.
   - Scenario D → **Append · Medium** with the hint.
   - A and B–E pass.
3. **First real card.**
   - The CLI `plan` is within 2 edits of the user's checked-in expected folder list (`tests/acceptance/first-card-expected.json`; one edit = one `PlanEdit`).
   - 0 failures, and a verdict of Safe or SafeWithAssumptions.
   - A before/after card listing shows no change made by the app.
4. **Responsiveness.**
   - Derive: median < 50 ms over 20 derives of a synthetic 500-item plan (M3 Release benchmark), off the UI thread.
   - Warm start: ≤ 1 s to first frame (0.37 s measured), from the first-frame time in `selftest-result.json` of the second of two selftest runs, checked by `deploy.ps1` (M10).
   - Scan + plan of a 300-file card on USB 3: ≤ 20 s (UNVERIFIED; recorded on the real card at M4).
5. **"Safe to format"** appears only with nothing Unaccounted or AssumedByRule, and with the identity and listing unchanged.

---

## 2. Decisions record (2026-09-27)

### 2.1 User-stated requirements (binding; Ref §1.1)

- **App:** personal, hand-launched; no tray/service/auto-launch (device-arrival refresh is fine); runs on **several PCs**, so the ledger is shared.
- **Card:** copy-only — never written, renamed or deleted.
- **Library:** listings only, never content; no "learn locations from library clips"; Lightroom catalog untouched.
- **Videos:** `<videoRoot>\YYYY\YYYY-MM\YYYY-MM-DD <Description>\` by local start date; a multi-day trip is one folder.
- **Photos:** flat into `<photoRoot>` (now `UAS Videos\Picture Offload`, may move to exFAT `D:\`); sets keep their folder, date appended on a clash (`001_0087 2026-09-27`). **The ledger is the long-term memory**; without a record a photo is new if its date has new videos or it follows the last-imported-video watermark, else "probably imported" (unticked, reason shown, per-day include).
- **Conflicts:** same name, different size → unticked; ticked → `name (2).ext`; never overwrite.
- **Grouping:** R 50 mi shown in miles, G 1; Council Road + Anvil Mountain may merge if the day split is one click; R slider regroups live.
- **Time:** drone clock US Eastern; MP4 `creation_time` true UTC; folder date = site-local date via GPS → zone.
- **Frameworks:** most modern; no compatibility constraints.
- **Grafts:** safety — card re-check before copy, write-through no-replace rename, hidden temps, keep awake, single instance, audit categories, banned-API analyzer, dry-run CLI; research — truncated-clip GPS, multi-sample GPS search, per-model GPS table + generic search, offline GeoNames suggestions.

### 2.2 Approved decisions

- **Approach:** rich review UX (Plan/Review/Commit; timeline group cards with boundary chips + [Merge]; map pane; live R/G sliders; clip list with thumbnails; photo day wall; undo/redo; drafts) + safety + research grafts.
- **Stack:** .NET 11 (RC1 go-live → RC2 → GA Nov 10), C# 15, WinUI 3 on Windows App SDK 2.5.1 lean packages, WebView2 + vendored MapLibre (OpenFreeMap streets, Esri World Imagery, USGS preset), offline GeoNames, unpackaged self-contained trimmed ReadyToRun folder + Start-menu `.lnk`; no MSIX, no Native AOT (optional M10 attempt). The user installs the .NET 11 SDK preview + PowerShell 7 via winget at the start.
- **Rules, Review UI, offload safety:** as drafted (§5–§7).
- **Defaults accepted:** copy JPG twin; no group-span cap; Autel cards unsupported (legacy Autel library folders match by name+size); truncated clips unticked; Esri + USGS preset; publish for the machine's own RID (x64; ARM64 built, unverified).

**Ledger location (changed): `<videoRoot>\.uas-sort\`** (here `C:\Users\damia\OneDrive\Pictures\UAS Videos\.uas-sort\`) — one `ledger-<MACHINE>.jsonl` per PC; union read, deduped by record id, conflict copies included. Consequences:
- (a) The folder is derived by `LedgerPaths.For(videoRoot)` = `<videoRoot>\.uas-sort`. There is no setting, no `ledgerDir` key, and no `Settings` property that a serialiser could write. Settings shows the folder's status, with [Keep on this device] and [Open].
- (b) It is the only exemption in the IO guard for files the app didn't create in this run (§3.3).
- (c) It is excluded from library listings, event folders, newness and the watermark.
- (d) It is pinned "Always keep on this device". Attributes are checked before any open. A cloud-only ledger file is Blocking, with [Keep on this device].
- (e) `CardSourceValidator` refuses it as a card source (as it refuses the whole video root).
- (f) Backups stay local, in `%LOCALAPPDATA%\uas-sort\ledger-backup\` (one subfolder per video root, §8).
- (g) It moves with the video root, via [Copy] or [Start empty] (§8).
- (h) The former open question on ledger location is resolved.
- (i) Setup no longer asks for a ledger folder.

### 2.3 Departures and assumptions

**Approved departures** (Ref §1.1): unpackaged, not MSIX (trusted cert per PC; framework-dependent MSIX needs Windows App Runtime ≥ 2.5.1, winget has 2.3.1; self-contained MSIX UNVERIFIED); ReadyToRun, not AOT (needs machine-wide C++ build tools); toolkit 8.3.260402-preview2 (only line compatible with the lean packages; fallback 8.2.251219 + full package); TimeProvider.Testing 10.10.0 (no 11.x).

**Assumptions** (Ref §1.2; each cheap to undo): Windows 11 24H2+ x64/ARM64; one card or drone volume per run (≤ ~1,000 files, ≤ 256 GB); the Air 3S layout (else Unknown → NotSafe); drone clock follows `America/New_York` incl. DST (else the learner falls back); DJI cards only; internet at home (else offline map); Lightroom dedupes; nothing else writes the library mid-offload; the video root syncs to every PC (else "no other PCs' ledgers found").

---

## 3. Architecture

### 3.1 Stack (Ref §2.1; NuGet versions as of 2026-09-27)

| Layer | Choice · version |
|---|---|
| Runtime | .NET 11 SDK `11.0.100-rc.1.26425.128` → RC2 (~Oct 13, UNVERIFIED) → `11.0.100` GA on Nov 10. Fallback: `net10.0` |
| Language | C# 15 unions and `closed` (cross-assembly exhaustiveness UNVERIFIED) |
| UI | `Microsoft.WindowsAppSDK.WinUI` 2.3.9, `.Foundation` 2.3.12, `.InteractiveExperiences` 2.1.9; SDK.BuildTools 10.0.28000.2705; ItemsView |
| Toolkit, MVVM | CommunityToolkit Sizers and SettingsControls 8.3.260402-preview2; Mvvm 8.4.2 |
| Map | WebView2 1.0.4191.47; vendored MapLibre GL JS 6.11.2 (`.mjs` MIME UNVERIFIED; Leaflet 1.9.4 as last resort) |
| Media, zones | MetadataExtractor 2.9.3 (Stream overloads only; trimmer-rooted, trim behaviour UNVERIFIED); GeoTimeZone 6.1.0 + ICU `TimeZoneInfo` (never `InvariantGlobalization`/`UseNls`) |
| Hash, JSON | System.IO.Hashing 11.0.0-rc.1.26425.128 → 11.0.0 at GA (XxHash128 for copies at 9.2 GB/s, XxHash64 for keys); source-generated System.Text.Json; `TimeProvider` |
| Quality | BannedApiAnalyzers 5.6.0; `AnalysisLevel` pinned to `11.0-recommended` (fallback `10.0-recommended` if RC1 rejects it, UNVERIFIED); warnings as errors; xunit.v3.mtp-v2 4.0.1 (MTP 2.4.1); TimeProvider.Testing 10.10.0 |
| Other | GeoNames CC-BY 4.0 (`tools/places/build-places.cs`); PowerShell 7; no DI container |
| Packaging | Unpackaged, self-contained, trimmed, ReadyToRun; `win-x64`/`win-arm64`; `EnableMsixTooling=true` (else 0xC000027B). x64 is 87–97 MB with a 0.37 s warm start; ARM64 is UNVERIFIED |

Build configuration and spike pitfalls: Ref §2.5–2.7.

### 3.2 Layout and dependencies (Ref §2.3–2.4)

```
src/  UasSort.Core      model, probes, time, geo, library index, planning, editing, naming, offload, audit, ports,
                        IoGuardPolicy (the pure rules every file open is checked against)
      UasSort.Platform  the ONLY code touching disk, Win32 or the shell
      UasSort.Review    view models, map bridge (no WinUI types)
      UasSort.App       WinUI shell, pages, MapPane + MapAssets, places.bin.gz, SelfTest
      UasSort.Cli       `uas-sort-cli plan` dry run (contract: Ref §4.5); writes nothing, ever
tests/ Testing (fakes, synthetic MP4/DNG), Core/Review/Platform.Tests, BannedApi.Probe
App ──► Review ──► Core ◄── Platform ◄── App        Cli ──► Platform, Core
```

Core and Review target `net11.0`; Platform, App and Cli target `net11.0-windows10.0.26100.0`. **Only `ICardReader`, `IFileOps` and Platform's stores open files, and each asks `IoGuardPolicy` first.**

### 3.3 IO-safety model (Ref §2.4, §4.1, §4.3)

**Ports:** `IDirectoryLister.Enumerate(root, recurse, excludeDirNames)` lists only (`AttributesToSkip = 0`, `IgnoreInaccessible = false`, errors collected; the library listing passes `{".uas-sort"}`); `ICardReader` (made by `ICardReaderFactory.Open(CardSource, CardIdentity)`) is bound to a `CardIdentity`, `FileAccess.Read`/`FileShare.ReadWrite` only, identity re-checked at Commit start, per file and at the verdict; `IFileOps` does guarded destination writes; `ILedgerStore.Check()` reads attributes before any open, and `ILedgerStore.EnsureFolder()` is the only code that creates (folder only) and pins `.uas-sort`.

**Policy:** one pure Core function decides every open, create, attribute change, delete and rename: `IoGuardPolicy.Check(IoOp, canonicalPath, attributes, GuardContext) → Allow | UnsafeIo(reason) | CloudOnly | Hydration`. `GuardedFileOps`, `WindowsCardReader`, the Platform `LedgerStore` and `FakeFileSystem` all call it, so the table-driven Core test proves the rules that ship (Ref §4.3).

**Guard:** the card is read-only under its anchored root. Card sources are anchored at the nearest ancestor containing `DCIM`, canonicalised (`GetFinalPathNameByHandleW`), and refused if equal to, inside or containing the video root, photo root, `previousPhotoRoots`, `<videoRoot>\.uas-sort`, `%LOCALAPPDATA%\uas-sort` or a cloud sync root. Pre-existing library files are never opened except under the ledger exemption; this run's own `*.uas-sort.tmp` and just-renamed files are tracked exceptions. Files are created with `CreateNew` only; `IFileOps.EnsureDirectory` creates only NewFolder paths + `YYYY`/`YYYY-MM` parents; `DeleteOwnTemp` only `*.uas-sort.tmp`.

**Banned APIs** (Core, Review, App, Cli): whole types `File`, `Directory`, `FileInfo`, `DirectoryInfo`, `FileSystemInfo`, `RandomAccess`, `FileStream`, `DriveInfo`, `FileSystemWatcher`, enumeration types, `ZipFile`, WinRT storage, VB `FileSystem`; path-taking members (incl. `ReadMetadata(String)`); `BitmapImage(Uri)` and `BitmapImage.UriSource`; `Process.Start`; `DateTime.Now`, `DateTime.Today`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`. The full list is Ref §2.4, which is authoritative for `BannedSymbols.txt`. Platform has a member list; each allowed call carries `#pragma warning disable RS0030 // IO layer: <why>`. Guarded by a `BannedSymbols.txt` test, `tools/build.ps1 -CheckBannedApi` (one RS0030 per probe call) and a XAML lint (no `{Binding`, `DisplayMemberPath`, `TextMemberPath`, `SelectedValuePath`, non-`ms-appx:///` images).

**Placeholder tripwire:** `RtlSetProcessPlaceholderCompatibilityMode(PHCM_EXPOSE_PLACEHOLDERS)` at start (UNVERIFIED; `--selftest` checks); opening a file with `0x400000`, `0x40000` or `0x1000` throws on any path, as do unreadable attributes; `FakeFileSystem` throws `HydrationViolation` on placeholder or library opens outside the exemption.

**Ledger exemption: exactly four operations** (paths canonical, case-insensitive)
1. Read any `ledger*.jsonl` directly in `<videoRoot>\.uas-sort\`, with `FileShare.ReadWrite`. This includes other PCs' files and conflict copies.
2. Append to this PC's own `ledger-<MACHINE>.jsonl` (created if missing), with `FileShare.Read`, so there is a single writer.
3. Create the `.uas-sort` folder itself when missing, through `ILedgerStore.EnsureFolder()` (on Start offload, or on the video-root [Copy]).
4. Set `FILE_ATTRIBUTE_PINNED` on that folder only (`EnsureFolder()` and [Keep on this device]). Whether OneDrive honours an API-set pin is UNVERIFIED.

**Anything else there throws `UnsafeIoException`:** other names, subfolders, writing to others' files or conflict copies, deletes and renames. The run stops, and the event is logged as a bug. A cloud-only ledger file is `CloudOnly` (Blocking, with [Keep on this device]), not a violation.

**WebView2:** downloads are cancelled, navigation stays on the virtual host, and there are no `file:` URIs.

---

## 4. Data flow (Ref §4.4)

1. **Launch.**
   - `Program.Main` calls `AppInstance.FindOrRegisterForKey("uas-sort")`. A second launch brings the first window forward; the fallback is a `Local\uas-sort` mutex.
   - **Setup** (first run, or after a settings recovery) confirms the video root (default `KnownFolder(Pictures)\UAS Videos`) and the photo root (`<videoRoot>\Picture Offload`). The ledger is shown as a status only.
2. **Card.** `CardDetector` skips network, CD, rootless and root-holding drives; one DJI card → Scan, else Rescan (F5)/Browse; every source passes `CardSourceValidator`.
3. **Scan.** List the card (< 1 s) and all roots in parallel; `Check()`, then load the ledger union; harvest metadata sequentially (failures → `ProbeError`); pin the card identity; offer a matching draft.
4. **Time and location.** Learn the clock's time zone; resolve CaptureUtc, zone, local date and flags; gate generic GPS. None of this depends on R or G.
5. **Newness.** `LibraryIndex` (without `.uas-sort`) → newness per unit → set placements → photo days → `PlanBase`.
6. **Grouping.** `Planner.Derive(PlanBase, Tuning, edits, SessionFlags)` is pure, takes milliseconds and runs on the thread pool. Edits are serialised; slider previews are latest-wins (§5.9).
7. **Review.** Gestures become `PlanEdit`s and re-derive; `CollectionSync` keeps the selection; the draft autosaves after 1 s; the library is untouched.
8. **Preflight.**
   - Take `Local\uas-sort-offload`, pin the card identity, and disable refresh, Rescan, Settings and Browse. Then compile and check (§7.1). The check writes nothing.
   - **Start offload** calls `ILedgerStore.EnsureFolder()` (creates `.uas-sort` if it's missing, the folder only, and pins it, also when it already exists), calls `SnapshotToBackup(runId)`, deletes the stale temps preflight listed, and keeps the PC awake.
9. **Copy.** One file at a time (§7.2). The ledger gets a `file` record per file, a `folder` per group, and `seen` + `run` at the end. Non-NTFS drives get a flush.
10. **Verdict.** Re-check the identity, re-list and diff the card, categorise, then issue the verdict and the report.

---

## 5. Rules

### 5.1 Card detection and classification (Ref §5)

**DJI card:** `\DCIM\` contains `^DJI_\d{3}(_.+)?$`, `PANORAMA\` or `HYPERLAPSE\`, **and** some file matches `^DJI_(\d{14})_(\d{4})_([A-Z])(?:_[^.]*)?\.([A-Za-z0-9]+)$`. DriveType and label are never trusted (D: is "Fixed" exFAT); a drone over USB is two volumes, one run each; the model comes from `\MISC\FC*.db` (FC9113 = Air 3S, UNVERIFIED), else the first DNG's EXIF.

Paths are relative to the anchored root, case-insensitive; `<media>` = `DJI_\d{3}(_.*)?`; media extensions = mp4, mov, dng, jpg, jpeg, heic, heif, tif, tiff, insv, avi. **Only named rules produce SkippedByRule; unclaimed media is Unknown (Unaccounted).**

| Match | Class → destination |
|---|---|
| `DCIM\<media>\*.MP4` | Video → group folder. Truncated if there is no `moov` or a `.<name>.MP4.trinf` sibling exists |
| `DCIM\<media>\*.DNG` | Photo → `<photoRoot>\name` |
| A JPG with a DNG's base name | PhotoTwin, copied if `copyJpgTwin` is on |
| A JPG with an MP4's base name | **Unknown** ("possible video cover") until M4 confirms what it is; it then gets a named Skip rule |
| Any other `DCIM\<media>\*.JPG` | Photo |
| `DCIM\PANORAMA\<set>\*.(DNG\|JPG)`, `DCIM\HYPERLAPSE\<set>\*.(DNG\|JPG)` | One Set unit → `<photoRoot>\<set folder>\member` (§5.8) |
| `*.LRF`, `*.SRT`; `._*`, `.*.trinf`, `.*.avc1`, `.Trashes`, `.Spotlight-V100`, `.fseventsd`; `MISC`, `LOST.DIR`, `Android`, `System Volume Information`, `$RECYCLE.BIN`; non-media outside `DCIM` | Skip. Any other dot file with a media extension is Unknown |
| Media anywhere else (e.g. `DCIM\DJI_A001`, nested, outside `DCIM`) | **Unknown**: Unaccounted until each file is individually marked "not needed" |
| An enumeration error | A `ForcesNotSafe` warning |

### 5.2 Time (Ref §6.1–6.5)

**The drone clock as a time zone**
Each DJI MP4 with `moov` gives a sample `(stamp, mvhdUtc, offset = round15min(stamp − mvhdUtc))` (so far all −4 h, all summer). A zone fits if its offset at every stamp equals the sample's; first fit of: the stored `droneClockZone` (default `America/New_York`); New York, Chicago, Denver, Phoenix, Los Angeles, Anchorage, Honolulu; the PC zone.

| Mode | When | Conversion · source |
|---|---|---|
| Zone | A zone fits | Through the zone, DST-aware · `DroneClockZone` |
| NearestSample | Nothing fits | The nearest sample within 60 days, else the most common offset; a banner says why · `DroneClockSample` |
| Setting | No samples | The stored zone, flagged `ClockFromSetting` · `DroneClockSetting` |

One `ClockModel` converts card items, library start times and the watermark. A zone that fits is saved after a successful run.

**Capture time** (first match): (1) `mvhd` as UTC for DJI video with `moov` (`Mvhd`); (2) EXIF DTO − `OffsetTimeOriginal` (`ExifWithOffset`; DJI doesn't write it today); (3) `ClockModel.ToUtc(stamp)` for DJI photos, sets, truncated or probe-failed items, stamp from EXIF DTO else filename (`DroneClock*`); (4) card mtime (`Mtime`; exFAT UTC UNVERIFIED).

**Zone and local date:** GPS → GeoTimeZone → `TimeZoneInfo`. `Etc/*` → nearest land-zone GPS item **on the whole card** ≤ 12 h → nearest GeoNames place's `tz` ≤ 60 mi → PC zone (`TzFallback`). No GPS → same-session GPS item → nearest GPS item ≤ 12 h → PC zone. Local date = `ConvertTimeFromUtc(CaptureUtc, tz).Date` and **never depends on R or G** (`20260726035000` = 07:50Z = **Jul 25** 23:50 AKDT; Makaha 09:30Z = Feb 28 in Honolulu).

**Flags:** `NoGps` (time-only grouping), `GpsGuessed`, `Truncated`, `ClockNotSet` (< 2015-01-01 or > now + 1 day), `TzFallback`, `ClockFromSetting`, `ProbeFailed` (timed from filename, else mtime; still copyable), and `CheckDate` when (a) zone alternatives exist **and** ≤ 60 min from local midnight, or (b) the source is `Mvhd`/`DroneClock*` **and** ≤ **75 min** from midnight.

### 5.3 GPS (Ref §6.3)

- **Videos:** `Mp4Probe` ports `docs/research/spikes/djmd/djmd_gps.py`: lazy box walk (never `mdat`), `djmd` track, generic protobuf decode. Per-model GPS path (model from field 1-1-1, `*.proto`; alt at the sibling `…-2`; units from field 1 of the GPS message: 0 or absent = radians, 1 = degrees, except Mavic4/Mini5Pro, always degrees): `3-3-4-1` for dvtm_Air3s, Air3, Mini4_Pro, wm265e, pm320, wm261, wa345e, Mavic4 and Mini5Pro; `3-4-4-1` for dvtm_AVATA2, dji_neo; `3-4-2-1` for dvtm_ac203/204/206, oq101. Unknown protocol → generic search for the first sub-message whose fields 2 and 3 are valid lat/lon doubles (degrees or radians), recorded with `FieldPath`.
- **No fix** = |lat|, |lon| < 1e-6. Sample indexes are 0-based: sample 0 (protocol + first fix), then 1–9, then 16, 32, 64, … below n, then n − 1. That is 1 + 9 + |{2^k : k ≥ 4, 2^k < n}| + 1 reads at most (16 for n = 300). **Generic hits** need first↔last ≤ **3 mi**, a non-`Etc` zone and ≤ **500 mi** from the median of the card's other GPS items (skipped if none) → `GpsGuessed`, else `NoFix(GenericHitImplausible)`.
- `SessionUtc = mvhd − uptime`. Sessions compare as `SessionKey(DroneSerial, SessionUtc)`: the same session when the serials are equal and |ΔSessionUtc| ≤ 2 s (the research measured ±1 s per power cycle); never by exact equality. **Truncated clips:** 4 KB at the `mdat` payload start, then offset 512; accept only a protocol ending `.proto` (recovered Anvil 0014/0024). `tnal` 160×90 range; 4 KB block cache, ~6–7 reads, < 50 ms/file.
- **Stills** (MetadataExtractor streams): DTO from the EXIF directory that has it, GPS, IFD0 model and thumbnail range (placeholder if missing; presence in ordinary Air 3S DNGs UNVERIFIED); sets use frame 1.

### 5.4 Newness (Ref §7)

**Library index**
- It is built from listings of the video root, the photo root and `previousPhotoRoots` (auto-appended).
- **`.uas-sort` is excluded.** It provides no keys, set or event folders, evidence, or watermark input.
- **Key:** `FileKey(NormName, Size)`, where `NormName` is lowercase without a trailing ` (n)`.
- **Event folder:** the nearest ancestor matching `^(\d{4})-(\d{2})-(\d{2})(?:\s+(.*))?$`. Photo roots and `.uas-sort` are excluded.
- **Member start:** the filename stamp converted through the card's `ClockModel`. Mtime is used instead, and flagged, if it is earlier than that or more than 2 h later. Autel `MAX_####` files use mtime.
- **Watermark** (photos only): the latest start of any library video **without a ledger `file` record**. App copies never move it. It is now **2026-09-27 18:24:16Z** (10:24 AKDT, Zachar Bay 0148). With no such video there is no watermark, and rule 4 below treats every unseen photo as after it ("no videos imported outside uas-sort").

**Videos**

| Status | When | Shown |
|---|---|---|
| Imported | `(NormName, Size)` is in a listed root or a ledger `file` record (`nameSize` → `LedgerNameSize`) | Hidden, folded |
| Decided | An unrevoked `dismissed` (videos are dismissed one at a time) | Other tab, [Un-dismiss] |
| Conflict | Same `NormName`, different size, no same-size match | ☐; if ticked → `name (2).ext` |
| New | Otherwise | ☑ (Truncated: ☐) |

**Photos and sets** (the first rule that applies)
1. A ledger `file` (every member, for a set) or an unrevoked `decision` → **Imported** / **Decided**.
2. The name and size are in a listed root → **Imported**. A set needs a non-empty folder with every member matching name, size and **mtime ±2 s**.
   - 2b. The same `NormName` with a different size in a listed root or the ledger, and no same-size match → **Conflict** (unticked). The JPG twin follows its DNG, including the `(n)` name.
3. `seen`, with no `file` or `decision` → **New**, "not copied on Oct 4".
4. After the watermark → **New**, "after last imported video, Sep 27 10:24 AKDT".
5. The drone-clock time is **≤ 75 min before** the watermark → **New**, "near the last imported video; time estimated".
6. The local date has a New video → **New**, "day has new videos".
7. Otherwise **ProbablyImported**, unticked, with one of two reasons: "videos from this day are already in the library", or "photo-only day before the last imported video (Sep 27)".

- **Pairs:** the DNG decides; an Imported DNG's missing JPG is **AssumedByRule**. **Ledger precedence:** once a photo has `file`, `decision` or `seen`, rules 1–3 decide it; 4–7 apply only to unseen photos.
- **Sets in the ledger:** `file`, `decision` and `seen` records for a set are written one per member, each with `set:<SetName>`. A set is Imported, Decided or seen only when every member has such a record (unrevoked, for decisions); a partial set falls through to the next rule.
- **Conflicts:** unticked, "A different file named X exists: <path>, <size>"; ticked → `stem (2).ext` or the next free `(n)` across listings, ledger and batch.
- **Truncated clips:** New, unticked, chip "Unfinished recording. Powering the drone on with this card inserted may repair it (UNVERIFIED on the Air 3S). Rescan afterwards, or tick to copy as-is."; copied or name+size matched → verdict ≤ SafeWithAssumptions; repaired (new size) → Conflict.
- **First run:** an InfoBar shows the watermark; per-day include and "record as imported" handle ProbablyImported DNGs.

### 5.5 Grouping (Ref §8.1–8.4)

**Parameters:** **R** 50 mi (80.467 km; slider 5–100) · **G** 1 day (slider 0–7) · **H** 3 h (fixed; matters only at G = 0) · **Near** 10 mi (split emphasis, next-day append confidence).

**Clustering.** Videos are taken in `(CaptureUtc, Id)` order. A new group starts on:
1. **Time:** `(x.LocalDate − prev.LocalDate).Days > G` **and** `x.CaptureUtc − prev.CaptureUtc > H`.
2. **Wall:** x is Imported into folder F while the group already holds items Imported into a different folder F′.
3. **Distance:** x has GPS, the group has a centroid (mean of unit vectors), x's session isn't any **GPS-bearing** member's (`SessionKey.SameSession`), and `Haversine(x, centroid) > R`.

**No-GPS items** never split a group and never move its centroid. When a distance split fires, each trailing no-GPS item moves with x if it is in x's session, stays if it is in the last GPS item's session, and otherwise goes to the side nearer in time. There is no automatic A-B-A re-merge.

**Boundary chips** head the next card and record cause (`DayGap`/`Distance`/`LibraryFolder`/`UserSplit`), jump and gaps: `── 34 mi jump · 21 h ──`, `── 62 days ──`, `── different library folder ──`, `── split by you ── [Undo split]`. All have **[Merge]** except LibraryFolder ("These clips are already in two different folders").

**Day splits:** each local-date change in a group puts a banner on the new day's first clip, `── Jul 25 → Jul 26 · 34 mi apart · 21 h ── [Split here]` (day-centroid distance; applies `SplitBefore(FirstOfDay)`).
- **≥ 10 mi → emphasised:** accent + "Likely separate outing", a Warning acknowledged at preflight, and a card chip "2 days · 34 mi apart [Split]".

**Calibration:** `docs/research/spikes/grouping/replay50.py` (listing + extracted GPS; opens no files) gives 8 groups for R 8–33 mi and **7 for R 40–60 mi**. At 50 mi Council Road + Anvil Mountain are **one 25-clip group "Jul 25–26"** with one emphasised split; one click gives the user's 8 folders.

| Pair | Distance | At 50 mi |
|---|---|---|
| Nome Roads (the widest folder) | 7.8 mi | One group |
| Council Road → Anvil Mountain (next day) | 33.1 closest / 33.7 centroids / 33.9 fixture | **Merged**, with an emphasised split |
| Safety Roadhouse ↔ Council Road | 20.7 mi, 18 days | Split by G |
| Nome Roads ↔ Anvil | 8.8 mi, 22 days | Split by G |
| Zachar Bay ↔ Kodiak town, same day | 52.6 closest / **53.4 fixture** | Split; a test pins the 3.4 mi margin |

### 5.6 Target folder (Ref §8.5)

Only the **New and ticked-Conflict** items are judged; inclusion is resolved before this step (§5.9), so ticking a Conflict can change the target. "F's days" are the local dates of F's members, taken from the listing and the ledger. The first matching row wins.

| Situation | Target · confidence, Why |
|---|---|
| No New or ticked-Conflict items, and a wall F | AlreadyImported(F). Folded only if no member is Conflict, Truncated or ProbeFailed |
| No New or ticked-Conflict items, and no wall | **NothingToCopy**, "nothing to copy: 3 conflicts, 1 dismissed". Never folded |
| Wall F, and the first New item's date precedes `F.NameDate` | NewFolder; Info `NewBeforeWallFolder` with [Split here] = `SplitBefore(split point)` |
| Wall: the group has items Imported into F, and some New item's date is one of F's days | Append(F) · High, "same day as clips already in this folder" |
| Wall F, and none of the New items' dates are F's days | Append(F) · **Medium**, "different day, 34 mi from Council Road" (or "different day, location unknown"). [New folder instead] = `SplitBefore(split point)` |
| No wall; `F.NameDate ≤ g.Start ≤ F.End + G days`; both centroids known and ≤ R apart; the New days overlap F's | Append(F) · High, "same dates, 3.1 mi" |
| As above, with adjacent dates and **< 10 mi** apart | Append(F) · High, "next day, 3.1 mi" |
| As above, with adjacent dates and **10 mi to R** apart | Append(F) · **Medium**, "different day, 34 mi". [New folder instead] = `Retarget(NewFolderTarget)` |
| One centroid unknown, overlapping dates | Append(F) · Medium, "same dates, location unknown" |
| Adjacent dates with one location unknown, or > R apart | NewFolder (F is still offered) |
| The group borders a `UserSplit` boundary, and F is the Wall of the group on the other side, or the pass-1 target of the earlier group across it (§5.9) | NewFolder ("Split here" means "separate folder") |
| Otherwise | NewFolder |

**Split point** (both wall hints): the first New item, in `(CaptureUtc, Id)` order, that is not on F's days; if that is the group's first item (the New run comes first), the first item Imported into F instead. It is never the group's first item, so the fix is never `Rejected(SplitAtGroupStart)`.

Ties: smaller date gap, then distance. Earlier clips are never auto-appended to a later-dated folder (by hand: confirm "Folder is dated Sep 28; these clips start Sep 27"). A shared target → Info + [Merge]. **Every Medium append is acknowledged at preflight.**

**Scenario D** (library lacks Anvil; Council leftovers on the card): one group of 4 Imported 7/25 + 21 New 7/26 clips → **Append(Council Road) · Medium**, "different day, 34 mi from Council Road", [New folder instead], emphasised split at the same point; either click → AlreadyImported(Council Road) + **NewFolder `2026\2026-07\2026-07-26`** (blank name, so Blocking `EmptyFolderName` until named when no suggestion exists).

### 5.7 Naming and suggestions (Ref §8.6–8.7)

**Folder names:** dated by the **earliest video's** local date (7/31–8/2 → `2026\2026-07\`); Append keeps F's real path at any depth; a new path equal to an existing folder becomes Append ("Folder exists; appending"). `Clean`: `<>:"/\|?*` and chars < 0x20 → space; collapse and trim whitespace; strip trailing dots/spaces; ≤ 80 chars; commas kept.
- **Blocking:** an empty NewFolder name, or a temp path (`final + ".uas-sort.tmp"`) longer than 400 characters. Every P/Invoke path gets `\\?\`.

**Suggestions** (≤ 6, deduplicated, per **local-day centroid**): (1) existing description on Append; (2) ledger folders ≤ 3 mi; (3) GeoNames feature ≤ 1.5 mi (mountain, peak, hill, valley, pass, cape, island, peninsula, point, bay, lake, glacier, fjord, cove, lagoon, inlet, sound, strait, harbor, falls, park; feature codes in Ref §8.7); (4) populated place ≤ 3 mi; (5) "near <town>" (≥ 1,000 people, ≤ 30 mi). NewFolder names are prefilled in italics from 1–4 (not Blocking); none → blank → Blocking. Data ~7.7 MB gzipped, from GeoNames `US.zip` plus `cities5000` (dump of 2026-09-27; record layout in Ref §8.7); About credits "GeoNames CC-BY 4.0".

### 5.8 Set folders (Ref §8.8)

```
Resolve(set):
  ledger has this SetName with the same (member, size) list and first-frame captureUtc → Imported
  for candidate in [SetName, "SetName yyyy-MM-dd" (first-frame local date), "… (2)", "… (3)", …]:
    used by another set in this batch                          → next
    no <any listed root>\candidate, or it is empty             → Plain / DateSuffixed
    existing == card members (name + size + mtime ±2 s)        → Imported
    existing ⊂ card members, all matching, nothing extra       → Resume: copy only the missing members
    else                                                       → clash → next
```

The Photos tab shows the result, e.g. "→ `001_0087 2026-09-27` (001_0087 holds a different set)". The folder name isn't editable.

### 5.9 Edits (Ref §8.9)

Edits are anchored to items (`GroupId` = earliest video), so they replay onto any R/G and onto a fresh scan.

**Derive order** (Ref §8.9 has the full algorithm and worked examples)
1. Auto-cluster (§5.5).
2. Apply the structural edits (`Merge`, `SplitBefore`, `MoveToNewGroup`, `MoveToGroup`) to that partition in log order. `Merge` joins every group from a's through b's in timeline order. `SplitBefore` gives cause `UserSplit`, overriding any auto cause at the same point. Moves may leave a group non-contiguous in time; groups are ordered by their anchor. An edit whose items are missing is dropped and counted ("3 of 4 edits still apply"); an edit that is a no-op at this tuning stays in the log, inactive.
3. Inclusion: defaults, then `SetIncluded`/`SetDayIncluded` in log order.
4. Decide in two passes, then suggest. Pass 1 decides every group without the UserSplit rule; pass 2 re-decides the groups that border a `UserSplit` boundary, excluding F when it is the Wall of the neighbour across it, or the pass-1 target of the earlier neighbour (so the earlier group keeps a shared target).
5. Pins: last wins; `PinnedMembers` changed → Warning [Keep]/[Reset to Auto], acknowledged at preflight; two pins after a merge → **Blocking** [Use first]/[Use second].
6. Issues (catalogue: Ref §9.10).

**Rejected:** merge across walls; move an Imported item; split at a group's start; rename an Append/AlreadyImported group; retarget outside the video root, into `<videoRoot>\.uas-sort` or its subtree, or into the photo root or any `previousPhotoRoots` (`RetargetIntoReservedFolder`); retarget to a later-dated folder unconfirmed.

**Threading** (`PlanSession`, Ref §4.2): edits are serialised; each is validated against the plan that includes every earlier edit, then derived on the thread pool (`ApplyAsync`). Slider previews cancel each other (latest wins). `Changed` fires off the UI thread, VMs marshal through `IUiDispatcher`, and a plan with a lower `Revision` than the one shown is ignored.

---

## 6. Review UI (Ref §9)

**Stages:** Setup → Card → Scan → Review → Commit (Preflight → Copy → Verdict).

- **Setup:** roots with free space; read-only ledger status ("History: 2 PCs' ledgers found" / "No history yet…" / cloud-only → Blocking + [Keep on this device]). **Card** only for zero or several candidates. **Commit** disables refresh, Rescan, Settings, Browse.
- **Chrome:** a TitleBar card chip ("E:\ · DJI Air 3S · serial 1A2B-3C4D · 214 files · 61.3 GB"), Mica, the system theme, a 1100×700 minimum, and tabs **Videos / Photos / Other**.

```
┌ Timeline (resizable) ───────────────┬ Map (WebView2 + MapLibre) ─────────────────────────────┐
│ ▸ 5 groups already in library       │  dots coloured by group · dashed 50-mi circle           │
│ ┌■ Jul 25–26 · AKDT ─────── NEW ──┐ │  jump lines "34 mi · 21 h"                              │
│ │ [Council Road          ▾]  ⓘ    │ │ [Streets|Satellite|Off]   R ──●── 50 mi   G ●─ 1 day    │
│ │ 2026\2026-07\2026-07-25 Council…│ ├ Clips in selected group ───────────────────────────────┤
│ │ 25 clips · 31.4 GB · spread 34mi│ │ ☑ ▭ DJI_…0118_D.MP4 Jul 25 22:29 AKDT 1.2 GB New 0.3 mi  │
│ │ ⚑ 2 days · 34 mi apart [Split]  │ │ ── Jul 25 → Jul 26 · 34 mi apart · 21 h ──── [Split here]│
│ │ ▭▭▭▭▭▭▭▭ +17                    │ │ ☑ ▭ DJI_…0001_D.MP4 Jul 26 19:56 AKDT 1.2 GB New 0.2 mi  │
│ └─────────────────────────────────┘ │ ☐ ▭ DJI_…0014_D.MP4 Jul 26 ~20:20     7 MB Unfinished …  │
│ ── 62 days ── [Merge]  (header of   │                                                         │
│    the next card)                   │                                                         │
├─────────────────────────────────────┴─────────────────────────────────────────────────────────┤
│ 25 videos · 31.4 GB → C: (317 GB free) · 40 photos · 1.1 GB → C:   ⛔ 0  ⚠ 2  ⓘ 1   [Offload ▶] │
└───────────────────────────────────────────────────────────────────────────────────────────────┘
```

- **Timeline:** ItemsView of selectable `GroupCardVm`/`FoldedRunVm`; each chip is the header of the next card, so no row is unselectable (`ItemContainer.CanUserSelect` is 2.0-experimental only); imported runs fold into one grey row.
- **Group card** (Ref §9.4): NewFolder description = `AutoSuggestBox` of `SuggestionVm` (`ToString()` = text, `UpdateTextOnSelect="False"`), Append = read-only + "[New folder instead]"; badge (NEW FOLDER, APPEND, ALREADY IN LIBRARY, NOTHING TO COPY, SKIP) + confidence; target `DropDownButton` built on `Opening` (Auto, New folder, candidates, Browse existing… inside the video root, Skip; a Browse result outside the video root, in `.uas-sort`, or in the photo root or a previous photo root is refused with an InfoBar); dates, spread, counts, ≤ 8 thumbnails, issue chips.
- **Clip list:** virtualised ItemsView, Extended selection; day-split banners inside the new day's first row; columns include, 96 px thumbnail, name, local time + source, size, status, miles from centre; thumbnails via `Thumb.Key` (LRU 400, key re-checked before assigning).
- **Map** (Ref §9.6): WebView2 (data in `%LOCALAPPDATA%\uas-sort\WebView2`), virtual host `map.uas-sort.example` → `MapAssets`, handler registered before `Navigate`, downloads cancelled. If `.mjs` isn't served as JavaScript (M8's first check): `.js` + `setWorkerUrl` → `WebResourceRequested` with explicit Content-Type → Leaflet 1.9.4. Messages carry `v:1` + `type` (`AllowOutOfOrderMetadataProperties = true`, `[lon,lat]`, unknown types logged).

| Direction | Type | Payload |
|---|---|---|
| host→map | `init` | `config{streetsStyleUrl, streetsDarkStyleUrl, satelliteUrl}`, `base`, `radiusMiles`, `online`, `theme` |
| host→map | `setData` | `rev`, `items[{id, groupId, lon, lat, kind}]`, `groups[{id, color, center, label}]`, `jumps[{from, to, label}]`; every derive, ≤ 10/s while dragging |
| host→map | `select` | `groupId`, `itemIds[]`, `fit`, `bbox` |
| host→map | `setRadius` / `setBase` / `setTheme` / `fit` | `radiusMiles` / `base` / `theme` / `bbox` |
| host→map | `ping` | `n` (selftest) |
| map→host | `ready` | `maplibre`, `webgl2` |
| map→host | `pong` | `n` (echoed) |
| map→host | `click` / `clickEmpty` | `itemIds[]`, `groupId`, `ctrl`, `shift` |
| map→host | `contextMenu` | `itemIds[]`, `x`, `y` → WinUI `MenuFlyout` |
| map→host | `tileError` / `baseUnavailable` / `error` | `base`, `message` |

Value sets: `base` ∈ {`streets`, `satellite`, `none`} (the toolbar's Off sends `none`); `kind` ∈ {`video`, `videoNoGps`}; `color` = `#RRGGBB`; `center`, `jumps[].from`/`to` = `[lon,lat]`; `bbox` = `[west,south,east,north]`; `theme` ∈ {`light`, `dark`}. One JSON example per message: Ref §9.6.

- **Map behaviour:** C# computes all distances, labels and colours; JS draws only the circle (Earth radius 6,371,008.8 m). Bases: OpenFreeMap `liberty`/`dark`, Esri World Imagery (keyless legacy endpoint), Off; USGS is a Settings preset; attribution visible; imperial scale. No `ready` in 5 s or no WebGL 2 → "Map unavailable" + "Open in browser"; offline (`NetworkInformation` or 4 tile errors) → overlays without a base.
- **Sliders:** R 5–100 mi (ticks 10/25/50/75/100, step 1), G 0–7, live "7 groups"; `ValueChanged` → background `Preview`, latest wins; `PointerReleased` (`handledEventsToo`) or `PointerCaptureLost` commits **one** undo entry; keyboard/wheel commit after **400 ms** idle; "Reset to 50 mi · 1 day".
- **Photos tab:** days with tri-state `SetDayIncluded`, counts and reason; a `LinedFlowLayout` wall; a set is one tile ("Panorama · 33 frames → `001_0087 2026-09-27`"); "Confirmed by you" tiles have [Undo] (`revoke`).
- **Other tab:** Unknown files ("Not copied by uas-sort; copy manually if needed"), rule skips, probe errors, scan warnings, and "Dismissed by you" with [Un-dismiss].
- **Undo and drafts:** a `(Tuning, edits)` stack (a quick fix or slider commit = one entry); drafts `drafts\<DraftKey>.json` (per PC; `vol-<serial>` or XxHash64 of the lowercase canonical root) saved 1 s after a change, deleted after a failure-free offload. A draft is offered whenever its `DraftKey` matches. If its `InventoryHash` (XxHash64 over the sorted lines `relPath|size|mtimeTicks`) differs, it is still offered, and the dropped count is the number of edits whose referenced `ItemId`s are missing.
- **Footer:** per-drive totals against free space; a ⛔/⚠/ⓘ flyout with quick fixes (the issue catalogue, with codes, severities and quick fixes, is Ref §9.10); **Offload ▶** is disabled while anything is Blocking. A Blocking bad ledger line offers **[Accept and continue]**: for this session only (not persisted, raised again on each scan while the line exists) it becomes a RequiresAck Warning, and the verdict cap applies.
- **Keyboard** (page keys use modifiers; list keys act only on items): Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z undo/redo (not in a TextBox); Ctrl+M merge with next; Ctrl+Shift+N move to new group; Ctrl+1/2/3 tabs; F5 Rescan; Ctrl+Enter Offload; Space include/exclude; Ctrl+Shift+S split before; F2 rename.
- **Units:** always miles ("<0.1 mi", one decimal below 10, otherwise whole numbers); decimal sizes; site-local times with the zone abbreviation, and "~" when the time comes from the drone clock.
- **Settings** (Ref §9.14): roots (old photo root → `previousPhotoRoots`); ledger card (derived path + status, [Keep on this device], [Open], local backup, video-root prompt §8); R/G defaults; clock zone; copy JPG twin; map URLs; About.

---

## 7. Offload engine (Ref §10)

### 7.1 Compile and preflight

**Compile:** one job per file of each included unit — videos → group target; photos → `<photoRoot>\name` (+ twin if `copyJpgTwin`); sets → `<photoRoot>\<folder>\member` (Resume: missing members only). SkipGroup, NothingToCopy, Imported and Decided are skipped; ticked conflicts get `(n)`; `CreatesFolder` only for NewFolder; `SeenIfNotCopied` = New, Conflict or ticked photos/sets; order groups → photos → sets.

**Blocking:** card identity changed ("A different card is in E:; rescan") or top level unreadable; lock held elsewhere; a root that an included job targets (the video root always counts, because it holds the ledger) is missing, not a directory, or under the card — a missing root that no included job targets only unticks its items, with the reason shown and newness unknown; ledger folder unlistable, unwritable or holding a cloud-only `ledger*.jsonl` ([Keep on this device]) — a merely *missing* folder is fine (Start offload creates it; a failed create stops before any copy); roots unconfirmed after recovery; **Append target gone** ("folder renamed or moved since the scan; rescan"); free space on a destination volume < `Σ size + max(1 GiB, 2 % of Σ size)`, where Σ is that volume's job bytes (Σ 31.4 GB → 32.47 GB free needed); duplicate destinations; temp path > 400 chars; Blocking plan issues.

**Actions** (`Preflight.Check` writes nothing): stale `*.uas-sort.tmp` files in the job destination directories only are listed, then deleted by `DeleteOwnTemp` on Start offload (Back deletes nothing); same-size existing destination → AlreadyThere. **Sheet:** acknowledgement checkboxes (Start disabled until all ticked) for Medium appends, emphasised splits, changed pin memberships and accepted ledger parse issues; warnings for assumptions, probably-imported photos left out, unticked new items, unfinished recordings and conflicts left out; Info "this offload starts it" when `<videoRoot>\.uas-sort` has no history yet. There is no "not pinned" warning here, because Start offload pins the folder.

### 7.2 Per-file protocol

0. Identity mismatch → `CardSwapped`. Stop; the rest are `NotStarted`.
1. `Stat` the card file. If its size or mtime changed → `ChangedOnCard`.
2. Destination exists at the same size → `AlreadyThere`, with a `file` record, `verify:"nameSize"`. At a different size → `ConflictAtRename`.
3. `EnsureDirectory(dir, allowCreate: job.CreatesFolder)`. Append targets must already exist.
4. Create `<final>.uas-sort.tmp` with `CreateNew`: preallocated, Hidden | NotContentIndexed. OneDrive skipping it is UNVERIFIED.
5. Copy in 1 MiB buffers through `XxHash128.Append`. The byte count must equal the size. A card read error re-reads that chunk once at the same offset; a second failure is `Failed(Copy)`, or a stop if the card is gone.
6. `FlushFileBuffers`.
7. Verify with `File.OpenHandle(…, FILE_FLAG_NO_BUFFERING)` and 4096-aligned 1 MiB reads.
   - Use `CreateFileW` if .NET rejects the flag (UNVERIFIED).
   - If the read fails, re-read buffered (`Cached`).
   - On a mismatch, delete the temp and retry once from step 4; a second mismatch is `Failed(Verify)`.
   - Unbuffered reads on exFAT D: are UNVERIFIED.
8. Copy the card timestamps, and **clear Hidden before the rename**.
9. `MoveFileExW(\\?\tmp, \\?\final, MOVEFILE_WRITE_THROUGH)`, never `REPLACE_EXISTING`. The flag is not relied on. `ERROR_ALREADY_EXISTS`/`ERROR_FILE_EXISTS` → delete the temp, `ConflictAtRename`.
10. `ConfirmFinal`, from metadata. If it fails → `Failed(Confirm)`.
11. Append and flush the ledger `file` record. **If that fails, the run stops.**
12. Report progress at 10 Hz.

**Stop causes** (Ref §10.3 has the table of the in-flight file's outcome, the remaining jobs and `StopReason`): card swapped, card removed, destination full or lost, Cancel, ledger append failed, internal safety stop. After a stop the remaining jobs are `NotStarted`.

**After the loop** (also after Cancel): non-NTFS/non-fixed destinations (exFAT D:) get `FlushDestination` for renamed files and directories (UNVERIFIED; M10) and are listed for safe removal. **Invariants:** final name only after verification (a crash leaves only temps, listed by the next preflight and deleted at Start offload); keep-awake and the offload lock for the whole Commit; no card thumbnail reads while copying.

### 7.3 Ledger writes

Records go to the own file and its local mirror, through `ILedgerWriter.Append` (flush per line). Each carries `id` (GUID), `machine`, `v:1` and `run`.
- **`file`:** one per Verified (`unbuffered`/`cached`) or AlreadyThere (`nameSize`) file.
- **`folder`:** one per group; also `source:"cardLeftovers"`.
- **`seen`:** at the end of **every** Commit (including cancel and failures), one for each uncopied `SeenIfNotCopied` unit; a set gets one per member (§5.4).
- **`run`:** one per run.
- **`torn`:** written by `OpenOwn()` when the own file doesn't end in `\n` (a crash mid-append): it first appends `\n`, then `{"t":"torn","v":1,"id":…,"machine":…,"line":N}`. Readers skip line N of that machine's file without a parse issue, so one crash never caps later verdicts.

Outside Commit, only user actions write: decisions (one per member for a set), `revoke`, and the video-root [Copy].

### 7.4 Audit and verdict

**Before the verdict:** re-check identity (mismatch → NotSafe, "The card in E: is not the one that was offloaded"); re-list and diff `(RelPath, Size, MtimeUtc, CreationUtc, Attributes)` excluding `System Volume Information` — changes are Unaccounted ("changed since scan"); last-access-only changes are reported, not counted.

| Category (best → worst) | When |
|---|---|
| VerifiedThisRun | Verified in this run |
| InLedger | A `file` record with `verify:"unbuffered"`/`"cached"` |
| ConfirmedByYou | An unrevoked `decision` |
| NameSizeMatch | A library listing, AlreadyThere, or `verify:"nameSize"` (**never** InLedger) |
| SkippedByRule | A named Skip rule, or a twin not copied because `copyJpgTwin` is off |
| AssumedByRule | ProbablyImported and not copied, or a twin assumed imported with its DNG |
| Unaccounted | New or Conflict and not copied; Failed; ChangedOnCard; CardSwapped; Cancelled; NotStarted; ConflictAtRename; Unknown; an uncopied probe error; changed since scan |

A unit takes the category of its worst file.

**NotSafe:** anything Unaccounted, a changed identity, or a `ForcesNotSafe` warning. **SafeWithAssumptions:** anything AssumedByRule, a Truncated clip copied or name+size matched, or accepted ledger parse issues. **Safe** otherwise.

**The headline** names the card and splits the evidence: "E: · DJI Air 3S · serial 1A2B-3C4D: Safe to format: 17 verified, 4 matched by name+size only". It counts buffered verifies. Non-NTFS destinations add "Safely remove D: before formatting the card" and **[Eject D:]**.

**Verdict page:** "Not copied" list with **nothing preselected**; [Record selected photos as already imported] → `assumedImported` (photos, sets; tile or day); [Mark selected as not needed] → `dismissed`. **Only photos and sets may be selected per day. Truncated clips are videos, and like every other video they are dismissed one at a time; unknown files are also one at a time (they usually have no local date).** Both actions confirm counts and GB; [Undo] → `revoke`; the report saves automatically.

---

## 8. Persistence (Ref §11)

Per-PC state lives in `%LOCALAPPDATA%\uas-sort\` and binaries in `%LOCALAPPDATA%\Programs\uas-sort\<version>\`. **The ledger lives in `<videoRoot>\.uas-sort\`.**

| Item | Where / format | Safety |
|---|---|---|
| Settings | `settings.json`, `schema:1`, **no `ledgerDir`** | Temp file + `File.Replace` with `.bak`. If unreadable: derived defaults, the file kept as `.corrupt-<ts>`, `RootsConfirmed=false` (**Blocking until the roots are confirmed**), and roots offered from the latest `run` record in any local backup subfolder (else `<derived default video root>\.uas-sort\`). `Load(readOnly: true)` (the CLI) never renames or creates anything |
| Ledger | Appends only to `<videoRoot>\.uas-sort\ledger-<MACHINE>.jsonl`. **Reads the union** of every `ledger*.jsonl` directly in that folder, deduped by `id`. JSON Lines, `t` discriminator, `v:1` | Flush per line; single writer. A torn **final** line is skipped, and so is a line that a later `torn` record of the same machine names (written by `OpenOwn()` before its next append, §7.3). **Any other bad line is Blocking until [Accept and continue]**, and then caps the verdict at SafeWithAssumptions |
| Ledger backup | `%LOCALAPPDATA%\uas-sort\ledger-backup\<root key>\` (**local**; `<root key>` = XxHash64 of the lowercase canonical video root, 16 hex digits): a mirror plus a snapshot at each Start offload (20 kept per root) | Mirror written during Commit and on every own-file append (decisions, revokes, [Copy]). Source for recovery and for [Copy] |
| Drafts / reports / logs | `drafts\<DraftKey>.json`; `reports\<yyyyMMdd-HHmmss>-<run8>.json`; `logs\` (14 days) | Temp file then replace; written once; — |

**Record kinds:** `file`, `folder`, `seen`, `decision`, `revoke`, `run`, `torn`. At 10k lines the ledger is ~2.5 MB and loads in ~120 ms.

**Folder status** (`Check()`, attributes only, before each load)

| Status | Shown as |
|---|---|
| Missing | Info "No history yet"; the folder is created and pinned at Start offload |
| Not pinned while in a sync root | Warning InfoBar, with [Keep on this device]; Start offload pins it anyway, so it is not on the preflight sheet |
| A cloud-only `ledger*.jsonl` | **Blocking**, with [Keep on this device]. The file is never opened |
| Unwritable | Blocking for Commit |
| The video root changed and has no ledger | Warning: "No history found in `<new root>\.uas-sort`; copy current ledger there?" |

**Moving the video root**
- The new video root is saved to settings **before** [Copy] or [Start empty] runs, so the guard's derived own-file path is the new one.
- **[Copy]** calls `EnsureFolder()` (creates and pins) and appends every loaded record (original `id` and `machine`) to the own file there. If the old root is gone, the records are the latest local snapshot ∪ the old root's mirror, deduped by `id`.
- **[Start empty]** leaves the new root without history; the first-run photo rules (§5.4) apply. If the new root's listing holds files matching the current ledger's `file` keys (name + size), a confirmation comes first: "N videos here were copied by uas-sort; starting empty treats them as manual imports and may mark un-copied photos as probably imported. [Copy] is recommended."
- The old folder is never modified.

---

## 9. Error handling (Ref §12)

| Situation | Behaviour → recovery |
|---|---|
| The source overlaps a root, the ledger folder or a sync root, or has no DCIM | Refused with the reason → pick the card's top folder |
| A different card in the same drive letter | Blocking at preflight; `CardSwapped` stops the run; NotSafe → Rescan |
| The card is removed during Scan or Review | Scan aborts (the draft is kept), or Offload is disabled → reinsert |
| A library root is missing (D: unplugged) | Banner; its items stay New and unticked (newness unknown); Blocking only if an included job targets it (the video root always) → plug in, then Rescan |
| A ledger file is cloud-only, or the folder is unwritable | Blocking, with [Keep on this device] → pin and sync, then Rescan |
| A bad ledger line that isn't the last one (and isn't marked `torn`) | Blocking until [Accept and continue] (this session only); then a RequiresAck Warning and the verdict cap → fix it, or accept |
| A crash left the own ledger file without a final `\n` | `OpenOwn()` appends `\n` + a `torn` marker before the next record; no parse issue, no cap |
| A file opened outside the exemption | `UnsafeIoException`: stop, log, "internal safety stop" → bug fix |
| Draft no longer matches the card | "3 of 4 edits still apply" → Resume or Discard |
| An Append target moved; low disk space | Blocking at preflight → Rescan, or free up space |
| A card file changed; a hash mismatch | `ChangedOnCard`; one retry, then `Failed(Verify)` |
| A card read error | Re-read the chunk once; then `Failed(Copy)` and continue, or stop (`CardRemoved`) if the card is gone |
| Disk full, destination lost, card pulled, or Cancel | Delete the temp and stop (in-flight file `Failed(<phase>)`, or `Cancelled` for Cancel); the rest are `NotStarted`; `seen` is still written → re-run |
| A ledger append fails | Stop at once; the file stays `Verified` this run and counts as NameSizeMatch on the next scan |
| Unreadable settings; a second window tries to offload | Blocking until the roots are confirmed; "Another uas-sort window is offloading" |

---

## 10. Testing strategy (Ref §13)

Run with `dotnet test --solution uas-sort.slnx` (xUnit v3 on MTP); analyzer probe via `tools/build.ps1 -CheckBannedApi`.

| Layer | Key cases |
|---|---|
| **Core unit** | The 37 tests from `docs/research/spikes/grouping/test_grouping.py`, ported per the table in Ref §13 (C# name, inputs, expected values): 24 keep their expectations with spike strings mapped to enums, 9 are restated for the approved model (e.g. `AppendSplit` → two groups split by a LibraryFolder wall; `PhotosOnly` → PhotoDays), 3 are R-dependent, and the Autel floating-time test is dropped. 34 of 37 pass at 50 mi in the spike (re-checked 2026-09-27). `test_consecutive_days_far_apart_split` becomes R = 50 mi (1 group + emphasised DaySplit) and R = 25 mi (2 groups); the two no-GPS tests are pinned to 25 mi **and** duplicated on a synthetic pair ~70 mi apart. New cases cover walls, a wall folder dated after the New clips, `NothingToCopy`, a ticked Conflict turning AlreadyImported into Append, the two-pass UserSplit rule, Scenario D, next-day confidence, pins, folding, sets, photo Conflicts (rule 2b), `(n)`, `SessionKey` tolerance (0.8 s apart = same, 3 s = different) and the 400-character temp path. Also the time cases: midnight → Jul 25, and Hawaii Feb 28 |
| **IO guard policy** | Table-driven Core test of `IoGuardPolicy.Check` over every `IoOp`: ledger reads allowed only for top-level `.uas-sort\ledger*.jsonl`; append only to the own file; `CreateDir` only for NewFolder paths and `.uas-sort` itself; placeholder bits → `Hydration`/`CloudOnly`; everything else `UnsafeIo` |
| **Invariants** | Each video is in exactly one group; undo+redo is the identity; a quick fix is one entry; a replayed draft is equal; edits stay in the log for every R from 5 to 100 (SplitBefore at R 50 → 25 → 50 keeps its UserSplit chip); **local dates never change with R or G** |
| **Performance** | M3 Release benchmark: a synthetic 500-item `PlanBase`, median of 20 derives < 50 ms |
| **Newness, ledger** | Photo rule order; **app copies never move the watermark**; no watermark → every unseen photo New; `seen` across two runs and after a cancel; a set with a partial `decision` stays undecided; the photo root moved to D:; **`.uas-sort` is not indexed**; the per-PC union and conflict-copy dedupe; torn vs. bad lines, and a crash mid-append followed by a normal run → no parse issue, no cap; the derived location; [Copy] is idempotent and, with the old root gone, uses snapshot ∪ mirror; [Start empty] confirms when the new root holds app-copied videos |
| **Golden replay** | The checked-in fixture `tests/UasSort.Testing/Replay/library-listing.json` (a listing snapshot of 2026-09-27 plus the GPS in `docs/research/spikes/djmd/calibration.json` and `docs/research/spikes/grouping/more_gps.json`; never regenerated by tests). Scenarios A0, A and B–E are tabulated in Ref §13: A0 gives 7 groups at 50 mi, and 8 after `SplitBefore`; R {8, 10, 20, 30, 33} → 8 and {40, 50, 60} → 7; A (walls) → 8 AlreadyImported; B → NewFolder `2026\2026-09\2026-09-27` (13 clips); C → Append High (4 new); D → Append Medium with the hint; E → NewFolder `2026\2026-07\2026-07-26`; every NewFolder is blank-named with a Blocking `EmptyFolderName` (no PlaceIndex before M9, no ledger); watermark 2026-09-27T18:24:16Z |
| **Synthetic media** | `SyntheticMp4Builder` (port of `docs/research/spikes/djmd/synth_test.py`; box layouts, zeroed GPS, units, unknown protocol, no `moov`; ≤ 16 reads to the first fix) and `SyntheticDngBuilder` |
| **Fake-FS tripwire** | Every library file except the local `.uas-sort\ledger*.jsonl` carries `0x401620`; over scan, plan and offload only the ledger exemption (local ledger files read, own file appended) and this run's own temps and just-renamed files are ever opened. A cloud-only `ledger-B` opens nothing and gives Blocking `CloudOnly`. Writing `ledger-B`, `.uas-sort\settings.json`, `.uas-sort\sub\…`, or `<videoRoot>\ledger-X.jsonl` → `UnsafeIoException` |
| **Fault injection** | Bit flip; disk full; card read error (one re-read, then `Failed(Copy)`); card vanishes; **identity change → CardSwapped**; target appears; wrong size after rename; ledger throws; Cancel writes `seen`; ChangedOnCard; Append folder deleted; Back from the sheet deletes no stale temp |
| **Windows integration** | Unbuffered read-back; no-replace rename; 300-char path; lister sees Hidden/System, **opens nothing** (`FileShare.None` locks), skips `.uas-sort` via `excludeDirNames`; deny-write ACL reader; exemption via `subst`/junction (`.uas-sort2` refused); `LedgerStore` consults the policy (a planted `.uas-sort\notes.txt` is never opened); `EnsureFolder()`/`KeepOnDevice()`; mutex; XAML lint |
| **View models** | Chip merge; split; quick-fix undo; slider drag and 400 ms idle commit; a gated `IPlanDeriver` proves a slow earlier derive never overwrites a later one; draft resume; preflight acknowledgements; [Accept and continue] on a bad ledger line; Verdict page (nothing preselected, per-day selection for photos and sets only); map JSON with `v` before `type`, incl. `ping`/`pong` |
| **`--selftest`** | Trimmed build, off-screen, in `%TEMP%`. Checks templates render their text ("Anvil Mountain"); MapLibre `ready` and a `ping`/`pong`; StillProbe; GeoTimeZone; JSON round-trips; a visible `0x400000`/`0x1000` entry (otherwise `-AllowNoPlaceholders`). Exits 0/1 and writes `selftest-result.json` (with the first-frame time); deploy waits 60 s per run, runs it twice, and fails if the second (warm) run's first frame is over 1 s |

**First-card acceptance**
1. Snapshot the card listing, including last-access times.
2. Compare the CLI `plan --json` output with `tests/acceptance/first-card-expected.json`, which the user writes before M4 (≤ 2 `PlanEdit`s apart). Record the scan + plan time.
3. Re-list and diff. Any last-access change is documented; the app never suppresses it.
4. Rehearse into `%TEMP%\uas-sort-rehearsal\{video,photo}`. Check the verdict, `.tmp` not syncing, exFAT verify/flush on D:, and [Eject D:].
5. Return to the library with **[Start empty]** (confirm its dialog if it appears: the rehearsal copied the same card files), confirm the pin in Explorer, and do the real offload. Then diff again.
6. Rescan: everything should be Imported and the verdict Safe, or SafeWithAssumptions for photos the user chose to leave.

---

## 11. Milestones (Ref §14)

The work is CLI-first: **34.5 developer-days**, or about 38 with 10% contingency.

| # | Work | Exit (tests green unless stated) | Days |
|---|---|---|---|
| M0 | **Stack proof.** User installs SDK 11 RC1 + `pwsh`; skeleton with pinned versions, BannedSymbols, probe. Fallback: toolkit 8.2 + full package, or fixed panes | The real App with a probe page (GridSplitter, SettingsCard, FolderPicker, AppInstance + mutex, WebView2, ItemsView) publishes trimmed + ReadyToRun with warnings as errors and passes the **M0 selftest**: {probe page renders, WebView2 reaches the virtual host, MetadataExtractor Stream read of a checked-in ≤ 2 KB EXIF JPEG `m0-exif.jpg` (hand-assembled, no user data), GeoTimeZone lookup, closed-record JSON round-trip}; cross-assembly exhaustiveness test | 1 |
| M1 | Card and media: model, detector, validator, classifier, Mp4Probe, synthetic builders, StillProbe, harvester | Classification, synthetic MP4/DNG and source-validation policy tests | 3.5 |
| M2 | Time, library and ledger: zone learner, TimeResolver, GPS gate; LibraryIndex; ledger reader (derived location, union, dedupe, parse issues, `torn` markers, attributes first); `IoGuardPolicy` and the guard exemption; settings recovery; `WindowsVolumeProvider`, Windows lister and card reader; PlaceholderGuard; fake FS | Time, newness-ledger, settings and `IoGuardPolicy` tests; the scan half of the fake-FS tripwire; Windows lister/reader integration tests | 2 |
| M3 | Planning: newness, clustering, structural edits, splits, targets, naming, sets, Planner, PlanSession; the ported tests, replay and invariants; **CLI `plan`** (Ref §4.5) | The 37 ported tests, new planning cases, invariants, golden replay and the derive benchmark; `uas-sort-cli plan --json` runs on a copied card folder under `%TEMP%` | 4.5 |
| M4 | **Checkpoint:** a CLI dry run on the first real card, plus the last-access diff | CLI plan ≤ 2 edits from `tests/acceptance/first-card-expected.json`; before/after card diff shows no app change; scan + plan time recorded | 0.5 |
| M5 | Offload engine: compiler, preflight, CopyEngine, CardAudit, file ops, ledger writer, reports, keep-awake, lock, eject | Fault injection, the audit table, the offload half of the tripwire, and Windows integration | 3.5 |
| M6a | Shell and stages; **Setup and Settings** (ledger status, [Keep on this device], video-root prompt); Card and Scan; timeline and group card | Named VM tests for these pages; selftest template checks for the group card | 5 |
| M6b | Clip list, Photos and Other tabs, undo, drafts, keyboard, dialogs, VM tests, full `--selftest` | All VM tests (incl. the gated-deriver test); full selftest template checks | 4.5 |
| M7 | Commit UI and device-arrival refresh (UNVERIFIED). **→ First real verified offload, without the map, around day 28** | A real offload with 0 failures and a verdict of Safe or SafeWithAssumptions | 3 |
| M8 | Map: the `.mjs` check first, then the bridge, sync, live sliders and offline view | `MapBridge` golden messages; selftest `ready` + `ping`/`pong` (skipped if M8 is dropped) | 3 |
| M9 | GeoNames: `build-places.cs`, PlaceIndex, suggester | PlaceIndex round-trip: "Anvil Mountain" ≤ 0.2 mi, "Zachar Bay" ≤ 0.4 mi | 1.5 |
| M10 | Packaging: x64 and ARM64; `deploy.ps1` (selftest gate, `.lnk`, keep 2 versions); rehearsal and full offload (exFAT flush, eject); AOT behind `-p:UasAot=true`, only if the C++ tools are installed | `deploy.ps1` passes its selftest gate on x64, incl. the warm first frame ≤ 1 s | 1.5 |
| SDK | **RC2** (~Oct 13, UNVERIFIED) and **GA** (Nov 10) bumps, each followed by the trimmed publish, `--selftest` and analyzer check; `AnalysisLevel` stays pinned | The same three checks pass | 0.5 + 0.5 |

- RC2 lands around M2–M3, and GA around M7–M8.
- M8 and M9 can each be dropped. Without M9, folder names come from ledger folders only.

---

## 12. Open questions (Ref §15)

The ledger-location question is **resolved** (§2.2) and removed. Every remaining item has an accepted default; none blocks work.

| # | Question | Default | Still open |
|---|---|---|---|
| 1 | Tick truncated clips? | Unticked; GPS recovered; the verdict says "Don't format yet" | Whether the Air 3S repairs a clip when powered on with the card inserted |
| 2 | Copy the JPG twin? | Yes; when switched off, it counts as SkippedByRule | — |
| 3 | Esri imagery without a key? | Esri, with a USGS preset | Esri's terms. The fallback is USGS, with no code change |
| 4 | Daily flights chaining into one group? | No span cap; one-click splits; next-day ≥ 10 mi is Medium | A max-span setting (Later) |
| 5 | Packaging? | Unpackaged self-contained ReadyToRun; MSIX deferred | AOT in M10, if the C++ tools are installed |
| 6 | ARM64? | Build for the machine's own RID; ARM64 is built in M10 | Needs an ARM64 PC to run on |
| 7 | PowerShell 7? | The user installs it with winget | — |
| 8 | Autel cards? | Unsupported (Unknown → NotSafe); legacy folders still match | — |

**Other UNVERIFIED items**
- **M0:**
  - cross-assembly exhaustiveness;
  - `AnalysisLevel` 11.0;
  - trimming MetadataExtractor;
  - AppInstance;
  - Sizers.
- **M4:**
  - exFAT mtime;
  - FC9113;
  - last-access writes.
- **M6b/M10:** the placeholder compatibility call (`--selftest` placeholder visibility; M10's deploy gate, with `-AllowNoPlaceholders` as the override).
- **M8:** the `.mjs` MIME type.
- **M10:**
  - exFAT unbuffered reads and flushes;
  - eject without admin rights;
  - OneDrive honouring the pin;
  - OneDrive skipping `.tmp`.
- **Elsewhere:**
  - an unpackaged `FolderPicker`;
  - focus return after a map click;
  - drone-over-USB volume names;
  - AOT's startup gain over ReadyToRun;
  - the winget `pwsh` version;
  - the .NET 11 end-of-support date (~Nov 2028);
  - the RC2 date;
  - device-arrival refresh;
  - the 20 s scan time;
  - sync roots other than OneDrive.

---

## 13. References

**The Reference.** [`2026-09-27-uas-sort-design-reference.md`](2026-09-27-uas-sort-design-reference.md) maps to this spec as follows:

| This spec | Reference |
|---|---|
| §1–2 | §1, §15 |
| §3 | §2–4.3 (§3 is the full C# model) |
| §4 | §4.4 |
| §5 | §5–8 |
| §6 | §9 |
| §7 | §10 |
| §8 | §11 |
| §9 | §12 |
| §10 | §13 |
| §11 | §14 |

Its closing Review disposition covers the 53 findings in `docs/research/09-design-review-findings.json`, numbered as in that file.

**Research** (`docs/research/`)
- `01-card-layout.md`
- `02-djmd-gps-calibration.md`
- `03-dotnet-libraries.md`
- `04-grouping-algorithm.md`
- `05-approach-judging.md` and `05-approaches.json`
- `06-modern-stack.md`
- `07-map-options.md`
- `08-winui-spike.md`

**Spikes** (`docs/research/spikes/`)
- `djmd/`: `djmd_gps.py`, `calibrate.py`, `calibration.json`, `synth_test.py`
- `grouping/`: `grouping.py`, `test_grouping.py`, `replay.py`, `replay50.py`, `more_gps.json`
- `review-ux/mp4thumb.py`
- `dotnet-stack/geonames/rg.py`
- `judge/crossday.py`
- `safety-review/scenD50.py`
- `modern-stack/`, `winui/`, `map-pane/`, `card-layout/`, `minimal-arch/`
