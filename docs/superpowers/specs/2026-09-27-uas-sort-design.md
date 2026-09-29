# uas-sort — design spec

**2026-09-27 · Approved.** The user approved every design section on 2026-09-27.

The companion [design reference](2026-09-27-uas-sort-design-reference.md) ("Ref §n") holds the full types, tables, test lists and review disposition. **If the two disagree, this spec wins.**

UNVERIFIED means not yet proven on this hardware. Each UNVERIFIED item has a check in a named build step or acceptance step (§11, §12) or a fallback, and none waits on the user, with one exception: if the Native AOT build fails a concrete check in the stack-proof step at the start of the build, that is raised with the user rather than accepted (§2.2).

---

## 1. Summary

uas-sort is a personal Windows 11 app, launched by hand on any of the user's PCs, that offloads a DJI Air 3S card into a OneDrive-synced library.

It lists card and library (**library: listings only, never content, except the app's own `.uas-sort\ledger*.jsonl`**), reads card metadata (MP4 `mvhd` time, `djmd` GPS, DNG EXIF), finds what is new, groups new videos by site-local date and GPS into `<videoRoot>\YYYY\YYYY-MM\YYYY-MM-DD <Description>\` (here `C:\Users\damia\OneDrive\Pictures\UAS Videos`, many files cloud-only), sends photos flat to `<photoRoot>` (sets keep their folder), lets the user review and edit, copies with verification (never overwriting; the offload never writes the card), and ends with an itemised **"safe to format?"** verdict. A separate, explicitly confirmed **Card cleanup** (§7.5) can then delete the oldest card files that are proven to be in the library (captured before a chosen date, or just enough to reach a free-space target, with the cutoff date always shown) and, only after a per-clip review, clips that are not in the library.

**Goals** (Ref §1.3): **G1** zero-risk offload — the offload never writes the card; hashed while written, re-read unbuffered before the final name; durability from `FlushFileBuffers`, a post-rename size check, a non-NTFS directory flush and safe removal, not write-through. **G2** every proposal explains itself. **G3** one-gesture, undoable fixes; drafts survive restarts. **G4** an honest verdict on the card in the reader. **G5** native Windows 11, &lt; 1 s start. **G6** a card cleanup that deletes only what the user confirmed, oldest first, each file re-checked just before its delete.

**Non-goals** (Ref §1.4): formatting the card or repairing `.trinf` recordings (card files are deleted only through Card cleanup, §7.5); reading, moving or de-duplicating library files; copying LRF/SRT, playback, transcoding; non-DJI cards; tray, service, auto-update, telemetry.

**Success criteria** (Ref §1.5)
1. **Mechanical safety.**
   - File-system types are banned outside Platform; the probe build raises RS0030 once per banned kind.
   - The table-driven `IoGuardPolicy` test passes, and the fake-FS hydration tripwire passes over scan, plan and offload. `CardDelete` is allowed only for paths in a `ConfirmedCleanupPlan` on a card volume the eraser verified from Win32 (never a browsed folder or a backup drive), and the fake-FS card-delete tripwire passes over Card cleanup.
   - Windows tests prove: the rename never replaces; verify is unbuffered, or the fallback is recorded; the lister opens nothing; no pre-existing library file is opened except `.uas-sort\ledger*.jsonl` (read) and the own ledger (append), while this run's own `*.uas-sort.tmp` and just-renamed files are tracked exceptions; the card reader works under a deny-write ACL; the card eraser deletes only the files it is given and removes only an emptied set folder.
2. **Golden replay** (the checked-in fixture, Ref §13).
   - Scenario A0 (card = every library video, empty library) at R 50 mi / G 1 → **7 groups**.
   - Council/Anvil's emphasised ~34 mi split → the user's 8 folders.
   - Scenario D → **Append · Medium** with the hint.
   - A and B–E pass.
3. **First real card.**
   - The CLI `plan` is within 2 edits of the user's checked-in expected folder list (`tests/acceptance/first-card-expected.json`; one edit = one `PlanEdit`).
   - 0 failures, and a verdict of Safe or SafeWithAssumptions.
   - A before/after card listing shows no change made by the offload.
4. **Responsiveness.**
   - Derive: median &lt; 50 ms over 20 derives of a synthetic 500-item plan (Release benchmark in the test suite), off the UI thread.
   - Warm start: ≤ 1 s to first frame (0.37 s measured for the spike's ReadyToRun build, the baseline the Native AOT build must meet or beat; AOT UNVERIFIED until the stack-proof step, §11 step 1), from the first-frame time in `selftest-result.json` of the second of two selftest runs, checked by `deploy.ps1` (§11 step 13).
   - Scan + plan of a 300-file card on USB 3: ≤ 20 s (UNVERIFIED; recorded on the real card in first-card acceptance, §10).
5. **"Safe to format"** appears only with nothing Unaccounted or AssumedByRule, and with the identity and listing unchanged.
6. **Card cleanup deletes exactly what was confirmed.** On a `%TEMP%` fake card, and in first-card acceptance step 7 on the real card (one old eligible clip), the card re-listed after cleanup differs from the one re-listed before it by exactly the files reported deleted, every one named in the confirmed plan and recorded by a `cardDelete` ledger record; nothing is deleted without a `ConfirmedCleanupPlan`.

---

## 2. Decisions record (2026-09-27)

### 2.1 User-stated requirements (binding; Ref §1.1)

- **App:** personal, hand-launched; no tray/service/auto-launch (device-arrival refresh is fine); runs on **several PCs**, so the ledger is shared.
- **Card:** the offload is copy-only: it never writes, renames or deletes anything on the card. **Changed 2026-09-27 (Card cleanup, §7.5):** a separate, explicit action may delete card files: those captured before a chosen date, or, oldest first, just enough to reach a free-space target, always confirming the cutoff date; optionally also clips that are not in the library ("to weed out footage that's bad, unnecessary, or accidental"), after a listing of each one's date, clip length and location. The card **reader** stays read-only; only `ICardEraser` deletes, and only files in a confirmed `CleanupPlan`.
- **Library:** listings only, never content; no "learn locations from library clips"; Lightroom catalog untouched.
- **Videos:** `<videoRoot>\YYYY\YYYY-MM\YYYY-MM-DD <Description>\` by local start date; a multi-day trip is one folder.
- **Photos:** flat into `<photoRoot>` (now `UAS Videos\Picture Offload`, may move to exFAT `D:\`); sets keep their folder, date appended on a clash (`001_0087 2026-09-27`). **The ledger is the long-term memory**; without a record a photo is new if its date has new videos or it follows the last-imported-video watermark, else "probably imported" (unticked, reason shown, per-day include).
- **Conflicts:** same name, different size → unticked; ticked → `name (2).ext`; never overwrite.
- **Grouping:** R 50 mi shown in miles, G 1; Council Road + Anvil Mountain may merge if the day split is one click; R slider regroups live.
- **Time:** MP4 `creation_time` true UTC; folder date = site-local date via GPS → zone. **Drone clock (confirmed 2026-09-27):** it is whatever the RC 2 is set to (observed: US Eastern, never switched since first use; it is not set from GPS). The user's rule: "build it from the UTC time using the actual local time zone conversion", and "get a flag for if those don't match with the time set on the drone clock" (§5.2).
- **Frameworks:** most modern; no compatibility constraints. **Installs (2026-09-27):** "If a framework needed to build this is not on my computer, just ask or give me the information on how to install it… I just want to maintain the best version for the app if that takes me installing something."
- **Grafts:** safety — card re-check before copy, write-through no-replace rename, hidden temps, keep awake, single instance, audit categories, banned-API analyzer, dry-run CLI; research — truncated-clip GPS, multi-sample GPS search, per-model GPS table + generic search, offline GeoNames suggestions.

### 2.2 Approved decisions

- **Approach:** rich review UX (Plan/Review/Commit; timeline group cards with boundary chips + [Merge]; map pane; live R/G sliders; clip list with thumbnails; photo day wall; undo/redo; drafts) + safety + research grafts.
- **Stack:** .NET 11 (RC1 go-live → RC2 → GA Nov 10), C# 15, WinUI 3 on Windows App SDK 2.5.1 lean packages, WebView2 + vendored MapLibre (OpenFreeMap streets, Esri World Imagery, USGS preset), offline GeoNames; an unpackaged, self-contained **Native AOT** folder (`PublishAot=true`, trimmed; `win-x64` only — x64 only, user decision 2026-09-28) + Start-menu `.lnk`; no MSIX. The prerequisites are installed once, at the start (§3.1: .NET 11 SDK preview, PowerShell 7, the C++ build tools for Native AOT); the plan's first task (Task 01.0) installs and verifies them.
- **Single-pass build (user decision 2026-09-27; §11, Ref §14).** Verbatim: "I want this to be implemented for the initial version in one shot via the AI agent development. So revise anything that's necessary to build it in one go. Doesn't need to be built progressively or tested in batches or milestones, just the initial full feature-complete version." The initial version is therefore built complete, in one pass, by an AI coding agent: no milestones, effort estimates, checkpoints or staged deliveries; every feature in this spec is in it (only the Later list, Ref §1.4, is deferred); tests are written alongside each part; and the first-real-card steps (§10) are the user's acceptance of the finished product, not build stages.
- **Native AOT is the primary build (changed 2026-09-27).** It is built from the start: the stack-proof step at the start of the build (§11 step 1) is an AOT publish that passes `--selftest` with `TreatWarningsAsErrors`, and `deploy.ps1` publishes AOT. Trimmed + ReadyToRun is no longer the plan; its measured numbers (x64 87–97 MB, 0.37 s warm first frame) are the baseline AOT must meet or beat (AOT startup UNVERIFIED until the stack-proof step). ReadyToRun remains only a diagnosis path if AOT fails a concrete check, and that situation is raised with the user, never silently accepted.
- **Installs over fallbacks (rule, 2026-09-27):** when the best option needs a tool that isn't installed, the plan asks the user to install it rather than designing a weaker fallback; fallbacks remain only for runtime/behaviour risks (e.g. MapLibre-in-WebView2 MIME, unbuffered verify on exFAT, the preview toolkit line, AppInstance).
- **Drone clock (user decision 2026-09-27; §5.2):** the drone clock is whatever the RC 2 is set to (observed: US Eastern, never reset). Local dates **always** come from true UTC converted with the GPS site zone. Drone-clock time is used only to convert stamps that carry no UTC (DNG EXIF, and filenames where there is no `mvhd`), never for a date directly. The learner tries **SiteLocal** (the clock follows local time at each site) first, then the last learned zone. A **`ClockMismatch`** flag, a Review InfoBar and a "clock ≠ local" chip show when the drone clock differs from site-local time by ≥ 15 min; the verdict is not affected.
- **Rules, Review UI, offload safety:** as drafted (§5–§7).
- **Defaults accepted:** copy JPG twin; no group-span cap; Autel cards unsupported (legacy Autel library folders match by name+size); truncated clips unticked; Esri + USGS preset; publish `win-x64` only (x64 only, user decision 2026-09-28; no ARM64 build).
- **Card cleanup (approved 2026-09-27; §7.5, Ref §10.6):** detected card volumes only, never a browsed folder (it could be a backup copy), and only a volume that also passes the stricter cleanup volume check (removable SD/USB media, exFAT/FAT32, drone index in `MISC`); by default only files with evidence (verified this run, a verified ledger copy, or a name+size match; a **video** also needs a current library listing that holds it); everything else that the app understands (new, probably imported, conflict, unfinished, and anything you marked "not needed" or "already imported") only after an opt-in switch, a per-row review list and a second acknowledgement; built in §11 steps 8–11 (Core, eraser, view models, page). Recorded interpretations (2026-09-27):
  - "Clear a specific amount of free space" is offered both ways: **Have at least [X] GB free** (default) and **Free up [X] GB**; both use the same oldest-first prefix and cutoff line.
  - Unfinished (truncated) recordings are treated as not in the library **even when name+size match**, an exception to the user's name/size rule, because the library copy is unfinished too and the drone may still repair the card copy.
  - A Verdict-page decision (`dismissed`, `assumedImported`) is never consent to delete: those files go through the review list.

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

**Approved departures** (Ref §1.1): unpackaged, not MSIX (the reason is certificate trust, not a missing install: MSIX needs a signing certificate trusted on every PC, or a Developer Mode registration, and the app needs no package identity; self-contained MSIX UNVERIFIED); toolkit 8.3.260402-preview2 (only line compatible with the lean packages; fallback 8.2.251219 + full package); TimeProvider.Testing 10.10.0 (no 11.x). Native AOT is no longer a departure (§2.2).

**Assumptions** (Ref §1.2; each cheap to undo): Windows 11 24H2+ x64 (x64 only, user decision 2026-09-28); one card or drone volume per run (≤ ~1,000 files, ≤ 256 GB); the Air 3S layout (else Unknown → NotSafe); the RC 2 clock follows one IANA zone incl. DST, or local time at each site (else the learner falls back to NearestSample; video dates are unaffected because they come from `mvhd` UTC); DJI cards only; internet at home (else offline map); Lightroom dedupes; nothing else writes the library mid-offload; the video root syncs to every PC (else "no other PCs' ledgers found").

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
| Packaging | Unpackaged, self-contained, **Native AOT** (`PublishAot=true`, trimmed); `win-x64` only (x64 only, user decision 2026-09-28); `EnableMsixTooling=true` (else 0xC000027B). Baseline to meet or beat: the spike's trimmed ReadyToRun x64 build, 87–97 MB with a 0.37 s warm start; AOT size and startup are UNVERIFIED until the stack-proof step (§11 step 1) |

Build configuration and spike pitfalls: Ref §2.5–2.7.

**Prerequisites (install once; Ref §2.6).** Machine-wide, on the development PC; the build (§11) starts only once they are installed. The plan's Task 01.0 installs and verifies them (idempotent; it also checks winget and Git for Windows) when the user says to start development; development runs from a Claude Code CLI session in PowerShell on Windows, not WSL (user decision 2026-09-28):
- .NET 11 SDK: `winget install Microsoft.DotNet.SDK.Preview` (11.0.100-rc.1 now; switch to the GA SDK on Nov 10).
- PowerShell 7: `winget install Microsoft.PowerShell`.
- C++ build tools for Native AOT. The installed VS Build Tools 2022 has an MSVC 14.44 folder but no `cl.exe`/`link.exe`; Windows SDK 10.0.26100 is present. In PowerShell:
  ```powershell
  & "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vs_installer.exe" modify --installPath "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools" --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --passive
  ```
- Optional: VS Code + C# Dev Kit (`winget install Microsoft.VisualStudioCode`), or Visual Studio 2026 Insiders for the XAML designer and Hot Reload.
- Runtime PCs need nothing: the app is self-contained, and WebView2 ships with Windows 11.

### 3.2 Layout and dependencies (Ref §2.3–2.4)

```
src/  UasSort.Core      model, probes, time, geo, library index, planning, editing, naming, offload, card cleanup, audit, ports,
                        IoGuardPolicy (the pure rules every file open is checked against)
      UasSort.Platform  the ONLY code touching disk, Win32 or the shell
      UasSort.Review    view models, map bridge (no WinUI types)
      UasSort.App       WinUI shell, pages, MapPane + MapAssets, places.bin.gz, SelfTest
      UasSort.Cli       `uas-sort-cli plan` dry run (contract: Ref §4.5); writes nothing, ever
tests/ Testing (fakes, synthetic MP4/DNG), Core/Review/Platform.Tests, BannedApi.Probe
App ──► Review ──► Core ◄── Platform ◄── App        Cli ──► Platform, Core
```

Core and Review target `net11.0`; Platform, App and Cli target `net11.0-windows10.0.26100.0`. **Only `ICardReader`, `IFileOps` and Platform's stores open files, only `ICardEraser` deletes anything on the card, and each asks `IoGuardPolicy` first.**

### 3.3 IO-safety model (Ref §2.4, §4.1, §4.3)

**Ports:** `IDirectoryLister.Enumerate(root, recurse, excludeDirNames)` lists only (`AttributesToSkip = 0`, `IgnoreInaccessible = false`, errors collected; the library listing passes `{".uas-sort"}`); `ICardReader` (made by `ICardReaderFactory.Open(CardSource, CardIdentity)`) is bound to a `CardIdentity`, `FileAccess.Read`/`FileShare.ReadWrite` only, identity re-checked at Commit start, per file and at the verdict, and per file during Card cleanup; `ICardEraser` (made by `ICardEraserFactory.Open(CardSource, CardIdentity, ConfirmedCleanupPlan)`, which re-derives from Win32 that the root is a removable card volume and never trusts a caller's flag) deletes card files and emptied set folders with `DeleteFileW`/`RemoveDirectoryW` and does nothing else; `IFileOps` does guarded destination writes; `ILedgerStore.Check()` reads attributes before any open, and `ILedgerStore.EnsureFolder()` is the only code that creates (folder only) and pins `.uas-sort`.

**Policy:** one pure Core function decides every open, create, attribute change, delete and rename: `IoGuardPolicy.Check(IoOp, canonicalPath, attributes, GuardContext) → Allow | UnsafeIo(reason) | CloudOnly | Hydration`. `GuardedFileOps`, `WindowsCardReader`, `WindowsCardEraser`, the Platform `LedgerStore` and `FakeFileSystem` all call it, so the table-driven Core test proves the rules that ship (Ref §4.3).

**Guard:** the card **reader** is read-only under its anchored root. The only other card operation is `CardDelete` (Card cleanup, §7.5). It is refused first for any path under a library root, a previous photo root, the ledger folder, `%LOCALAPPDATA%\uas-sort` or the system volume, and otherwise allowed only (a) under the anchored root of a card volume that the eraser factory verified from Win32 (volume root, removable SD/USB, exFAT/FAT32, never browsed), (b) for a path named in the `ConfirmedCleanupPlan` that the eraser's own `GuardContext` carries (the eraser builds it; no caller supplies one), and (c) for a file, or for an emptied set folder that plan names (never any other directory). No operation other than reads and `CardDelete` is ever allowed under the card root; in particular no write or flush handle to a card file or the card volume. Card sources are anchored at the nearest ancestor containing `DCIM`, canonicalised (`GetFinalPathNameByHandleW`), and refused if equal to, inside or containing the video root, photo root, `previousPhotoRoots`, `<videoRoot>\.uas-sort`, `%LOCALAPPDATA%\uas-sort` or a cloud sync root. Pre-existing library files are never opened except under the ledger exemption; this run's own `*.uas-sort.tmp` and just-renamed files are tracked exceptions. Files are created with `CreateNew` only; `IFileOps.EnsureDirectory` creates only NewFolder targets + `YYYY`/`YYYY-MM` parents and new set folders under the photo root; `DeleteOwnTemp` only `*.uas-sort.tmp`.

**Banned APIs** (Core, Review, App, Cli): whole types `File`, `Directory`, `FileInfo`, `DirectoryInfo`, `FileSystemInfo`, `RandomAccess`, `FileStream`, `DriveInfo`, `FileSystemWatcher`, enumeration types, `ZipFile`, WinRT storage, VB `FileSystem`; path-taking members (incl. `ReadMetadata(String)`); `BitmapImage(Uri)` and `BitmapImage.UriSource`; `Process.Start`; `DateTime.Now`, `DateTime.Today`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`. The full list is Ref §2.4, which is authoritative for `BannedSymbols.txt`. Platform has a member list; each allowed call carries `#pragma warning disable RS0030 // IO layer: <why>`, the eraser's `DeleteFileW`/`RemoveDirectoryW` calls included (`BannedSymbols.txt` is unchanged by Card cleanup). Guarded by a `BannedSymbols.txt` test, `tools/build.ps1 -CheckBannedApi` (one RS0030 per probe call) and a XAML lint (no `{Binding`, `DisplayMemberPath`, `TextMemberPath`, `SelectedValuePath`, non-`ms-appx:///` images).

**Placeholder tripwire:** `RtlSetProcessPlaceholderCompatibilityMode(PHCM_EXPOSE_PLACEHOLDERS)` at start (UNVERIFIED; `--selftest` checks); opening a file with `0x400000`, `0x40000` or `0x1000` throws on any path, as do unreadable attributes; `FakeFileSystem` throws `HydrationViolation` on placeholder or library opens outside the exemption, and records a `CardDeleteViolation(Path, Op, Reason)` in `FakeFileSystem.CardDeleteViolations` for any card delete outside a confirmed plan.

**Ledger exemption: exactly four operations** (paths canonical, case-insensitive)
1. Read any `ledger*.jsonl` directly in `<videoRoot>\.uas-sort\`, with `FileShare.ReadWrite`. This includes other PCs' files and conflict copies.
2. Append to this PC's own `ledger-<MACHINE>.jsonl` (created if missing), with `FileShare.Read`, so there is a single writer.
3. Create the `.uas-sort` folder itself when missing, through `ILedgerStore.EnsureFolder()` (on Start offload, on the video-root [Copy], or when Card cleanup starts deleting).
4. Set `FILE_ATTRIBUTE_PINNED` on that folder only (`EnsureFolder()` and [Keep on this device]). Whether OneDrive honours an API-set pin is UNVERIFIED.

**Anything else there throws `UnsafeIoException`:** other names, subfolders, writing to others' files or conflict copies, deletes and renames. The run stops, and the event is logged as a bug. A cloud-only ledger file is `CloudOnly` (Blocking, with [Keep on this device]), not a violation.

**WebView2:** downloads are cancelled, navigation stays on the virtual host, and there are no `file:` URIs.

---

## 4. Data flow (Ref §4.4)

1. **Launch.**
   - `Program.Main` calls `AppInstance.FindOrRegisterForKey("uas-sort")`. A second launch brings the first window forward; the fallback is a `Local\uas-sort` mutex.
   - **Setup** (first run, or after a settings recovery) confirms the video root (default `KnownFolder(Pictures)\UAS Videos`) and the photo root (`<videoRoot>\Picture Offload`). The ledger is shown as a status only.
2. **Card.** `CardDetector` skips network, CD, rootless and root-holding drives; one DJI card → Scan, else Rescan (F5)/Browse; every source passes `CardSourceValidator.Validate(path, detected /* the VolumeInfo, or null for Browse */, …)`, which sets `CardSource.IsBrowsedFolder` (a Browse result is always browsed, even a volume root) and `IsWriteProtected` from the detected volume.
3. **Scan.** List the card (&lt; 1 s) and all roots in parallel; `Check()`, then load the ledger union; harvest metadata sequentially (failures → `ProbeError`); pin the card identity; offer a matching draft.
4. **Time and location.** Learn the drone clock (SiteLocal or a zone); resolve CaptureUtc, zone, local date and flags (incl. `ClockMismatch`); gate generic GPS. None of this depends on R or G.
5. **Newness.** `LibraryIndex` (without `.uas-sort`) → newness per unit → set placements → photo days → `PlanBase`.
6. **Grouping.** `Planner.Derive(PlanBase, Tuning, edits, SessionFlags)` is pure, takes milliseconds and runs on the thread pool. Edits are serialised; slider previews are latest-wins (§5.9).
7. **Review.** Gestures become `PlanEdit`s and re-derive; `CollectionSync` keeps the selection; the draft autosaves after 1 s; the library is untouched.
8. **Preflight.**
   - Take `Local\uas-sort-offload`, pin the card identity, and disable refresh, Rescan, Settings and Browse. Then compile and check (§7.1). The check writes nothing.
   - **Start offload** calls `ILedgerStore.EnsureFolder()` (creates `.uas-sort` if it's missing, the folder only, and pins it, also when it already exists), calls `SnapshotToBackup(runId)`, deletes the stale temps preflight listed, and keeps the PC awake.
9. **Copy.** One file at a time (§7.2). The ledger gets a `file` record per file, a `folder` per group, and `seen` + `run` at the end. Non-NTFS drives get a flush.
10. **Verdict.** Re-check the identity, re-list and diff the card, categorise, then issue the verdict and the report.
11. **Card cleanup** (optional, from the Verdict page or the Review title bar; §7.5). Re-list and audit the card, plan oldest-first, confirm, delete with per-file re-checks and `cardDelete` records, then re-list and rescan.

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
| A JPG with an MP4's base name | **Unknown** ("possible video cover") until first-card acceptance (§10) confirms what it is; it then gets a named Skip rule |
| Any other `DCIM\<media>\*.JPG` | Photo |
| `DCIM\PANORAMA\<set>\*.(DNG\|JPG)`, `DCIM\HYPERLAPSE\<set>\*.(DNG\|JPG)` | One Set unit → `<photoRoot>\<set folder>\member` (§5.8) |
| `*.LRF`, `*.SRT`; `._*`, `.*.trinf`, `.*.avc1`, `.Trashes`, `.Spotlight-V100`, `.fseventsd`; `MISC`, `LOST.DIR`, `Android`, `System Volume Information`, `$RECYCLE.BIN`; non-media outside `DCIM` | Skip. Any other dot file with a media extension is Unknown |
| Media anywhere else (e.g. `DCIM\DJI_A001`, nested, outside `DCIM`) | **Unknown**: Unaccounted until each file is individually marked "not needed" |
| An enumeration error | A `ForcesNotSafe` warning |

### 5.2 Time (Ref §6.1–6.5)

**The drone clock** (user decision 2026-09-27; Ref §6.1)
The drone clock is whatever the RC 2 is set to (observed: US Eastern, never reset; it is not set from GPS). **Local dates always come from true UTC converted with the GPS site zone.** Drone-clock time is used only to convert stamps that carry no UTC (DNG EXIF; filenames of clips without `mvhd`, library members and the watermark) and never for a date directly.

Each DJI MP4 with `moov` gives a sample `(stamp, mvhdUtc, offset = round15min(stamp − mvhdUtc))` (so far all −4 h, all summer). The first candidate that fits wins:
1. **SiteLocal:** every sample's offset equals the UTC offset of its site zone at that sample (the zone of the sample's own GPS via GeoTimeZone). Samples without GPS are ignored for this test; it needs ≥ 1 GPS sample.
2. The stored `droneClockZone`: the last learned zone (default `America/New_York`).
3. New York, Chicago, Denver, Phoenix, Los Angeles, Anchorage, Honolulu.
4. The PC zone.

A zone (2–4) fits if its offset at every stamp equals the sample's.

| Mode | When | Conversion · source |
|---|---|---|
| SiteLocal | Candidate 1 fits | Through the zone of the item's own GPS; without GPS, the zone of the nearest GPS item within 12 h, else the stored zone · `DroneClockSiteLocal` |
| Zone | A zone (2–4) fits | Through the zone, DST-aware · `DroneClockZone` |
| NearestSample | Nothing fits (e.g. the RC clock was changed partway through the card) | The nearest sample in time within 60 days, else the most common offset; a banner says why, and names a clock change with its time when the samples form two or more runs of different offsets ("Drone clock changed during this card: UTC−4 until Sep 27 14:30, then UTC−8") · `DroneClockSample` |
| Setting | No samples | The stored mode: site-local as in SiteLocal, or the stored zone; flagged `ClockFromSetting` · `DroneClockSetting` |

One `ClockModel` converts card items, library start times (SiteLocal: through the folder's ledger `tz`, else the stored zone) and the watermark. After a successful run the learned mode is saved: SiteLocal, or the fitted zone (`droneClockMode`, `droneClockZone`); NearestSample saves nothing.

**`ClockMismatch`:** set on an item when its drone-clock offset (from the learned `ClockModel` at its stamp) differs from its site zone's UTC offset at `CaptureUtc` by **≥ 15 min** (not set when the site zone is the PC-zone fallback). UI (§6): a Warning InfoBar on Review, a "clock ≠ local" chip on affected group cards, and a clip time tooltip showing UTC, drone clock and site-local time side by side. The header clock line mentions it. It never changes a date and does not affect the verdict.

**Worked example** (Zachar Bay 0128): `DJI_20260927140627_0128_D.MP4` = 14:06:27 on the drone clock; `mvhd` `creation_time` = 18:06:27Z, so the sample offset is −4 h; the GPS resolves to `America/Anchorage`, so the local time is **10:06:27 AKDT, Sep 27**. SiteLocal doesn't fit (−4 ≠ −8), `America/New_York` does (EDT) → Zone mode; clock UTC−4 vs site UTC−8 → `ClockMismatch`.

**Capture time** (first match): (1) `mvhd` as UTC for DJI video with `moov` (`Mvhd`); (2) EXIF DTO − `OffsetTimeOriginal` (`ExifWithOffset`; DJI doesn't write it today); (3) `ClockModel.ToUtc(stamp)` for DJI photos, sets, truncated or probe-failed items, stamp from EXIF DTO else filename (`DroneClock*`); (4) card mtime (`Mtime`; exFAT UTC UNVERIFIED).

**Zone and local date:** GPS → GeoTimeZone → `TimeZoneInfo`. `Etc/*` → nearest land-zone GPS item **on the whole card** ≤ 12 h → nearest GeoNames place's `tz` ≤ 60 mi → PC zone (`TzFallback`). No GPS → same-session GPS item → nearest GPS item ≤ 12 h → PC zone. Local date = `ConvertTimeFromUtc(CaptureUtc, tz).Date` and **never depends on R or G** (`20260726035000` = 07:50Z = **Jul 25** 23:50 AKDT; Makaha 09:30Z = Feb 28 in Honolulu).

**Flags:** `NoGps` (time-only grouping), `GpsGuessed`, `Truncated`, `ClockNotSet` (&lt; 2015-01-01 or > now + 1 day), `TzFallback`, `ClockFromSetting`, `ClockMismatch` (above; Info only), `ProbeFailed` (timed from filename, else mtime; still copyable), and `CheckDate` when (a) zone alternatives exist **and** ≤ 60 min from local midnight, or (b) the source is `Mvhd`/`DroneClock*` **and** ≤ **75 min** from midnight.

### 5.3 GPS (Ref §6.3)

- **Videos:** `Mp4Probe` ports `docs/research/spikes/djmd/djmd_gps.py`: lazy box walk (never `mdat`), `djmd` track, generic protobuf decode. `mvhd` also gives the clip length (`duration / timescale` → `Mp4Info.Duration`; null without `moov`), which Card cleanup shows. Per-model GPS path (model from field 1-1-1, `*.proto`; alt at the sibling `…-2`; units from field 1 of the GPS message: 0 or absent = radians, 1 = degrees, except Mavic4/Mini5Pro, always degrees): `3-3-4-1` for dvtm_Air3s, Air3, Mini4_Pro, wm265e, pm320, wm261, wa345e, Mavic4 and Mini5Pro; `3-4-4-1` for dvtm_AVATA2, dji_neo; `3-4-2-1` for dvtm_ac203/204/206, oq101. Unknown protocol → generic search for the first sub-message whose fields 2 and 3 are valid lat/lon doubles (degrees or radians), recorded with `FieldPath`.
- **No fix** = |lat|, |lon| &lt; 1e-6. Sample indexes are 0-based: sample 0 (protocol + first fix), then 1–9, then 16, 32, 64, … below n, then n − 1. That is 1 + 9 + |{2^k : k ≥ 4, 2^k &lt; n}| + 1 reads at most (16 for n = 300). **Generic hits** need first↔last ≤ **3 mi**, a non-`Etc` zone and ≤ **500 mi** from the median of the card's other GPS items (skipped if none) → `GpsGuessed`, else `NoFix(GenericHitImplausible)`.
- `SessionUtc = mvhd − uptime`. Sessions compare as `SessionKey(DroneSerial, SessionUtc)`: the same session when the serials are equal and |ΔSessionUtc| ≤ 2 s (the research measured ±1 s per power cycle); never by exact equality. **Truncated clips:** 4 KB at the `mdat` payload start, then offset 512; accept only a protocol ending `.proto` (recovered Anvil 0014/0024). `tnal` 160×90 range; 4 KB block cache, ~6–7 reads, &lt; 50 ms/file.
- **Stills** (MetadataExtractor streams): DTO from the EXIF directory that has it, GPS, IFD0 model and thumbnail range (placeholder if missing; presence in ordinary Air 3S DNGs UNVERIFIED); sets use frame 1.

### 5.4 Newness (Ref §7)

**Library index**
- It is built from listings of the video root, the photo root and `previousPhotoRoots` (auto-appended).
- **`.uas-sort` is excluded.** It provides no keys, set or event folders, evidence, or watermark input.
- **Key:** `FileKey(NormName, Size)`, where `NormName` is lowercase without a trailing ` (n)`.
- **Event folder:** the nearest ancestor matching `^(\d{4})-(\d{2})-(\d{2})(?:\s+(.*))?$`. Photo roots and `.uas-sort` are excluded.
- **Member start:** the filename stamp converted through the card's `ClockModel` (in SiteLocal mode through the folder's ledger `tz`, else the stored zone; §5.2). Mtime is used instead, and flagged, if it is earlier than that or more than 2 h later. Autel `MAX_####` files use mtime.
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
2. The name and size are in a listed root → **Imported**. A set needs a non-empty folder whose members match the card's by name, size and **mtime ±2 s**: all of them, or a superset of the card's (the card kept only some members, e.g. after a partial Card cleanup).
   - 2b. The same `NormName` with a different size in a listed root or the ledger, and no same-size match → **Conflict** (unticked). The JPG twin follows its DNG, including the `(n)` name.
3. `seen`, with no `file` or `decision` → **New**, "not copied on Oct 4".
4. After the watermark → **New**, "after last imported video, Sep 27 10:24 AKDT".
5. The drone-clock time is **≤ 75 min before** the watermark → **New**, "near the last imported video; time estimated".
6. The local date has a New video → **New**, "day has new videos".
7. Otherwise **ProbablyImported**, unticked, with one of two reasons: "videos from this day are already in the library", or "photo-only day before the last imported video (Sep 27)".

- **Pairs:** the DNG decides; an Imported DNG's missing JPG is **AssumedByRule**. **Ledger precedence:** once a photo has `file`, `decision` or `seen`, rules 1–3 decide it; 4–7 apply only to unseen photos.
- **Sets in the ledger:** `file`, `decision` and `seen` records for a set are written one per member, each with `set:<SetName>`. A set is Imported, Decided or seen only when every member has such a record (unrevoked, for decisions); a partial set falls through to the next rule.
- **Conflicts:** unticked, "A different file named X exists: &lt;path>, &lt;size>"; ticked → `stem (2).ext` or the next free `(n)` across listings, ledger and batch.
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
| As above, with adjacent dates and **&lt; 10 mi** apart | Append(F) · High, "next day, 3.1 mi" |
| As above, with adjacent dates and **10 mi to R** apart | Append(F) · **Medium**, "different day, 34 mi". [New folder instead] = `Retarget(NewFolderTarget)` |
| One centroid unknown, overlapping dates | Append(F) · Medium, "same dates, location unknown" |
| Adjacent dates with one location unknown, or > R apart | NewFolder (F is still offered) |
| The group borders a `UserSplit` boundary, and F is the Wall of the group on the other side, or the pass-1 target of the earlier group across it (§5.9) | NewFolder ("Split here" means "separate folder") |
| Otherwise | NewFolder |

**Split point** (both wall hints): the first New item, in `(CaptureUtc, Id)` order, that is not on F's days; if that is the group's first item (the New run comes first), the first item Imported into F instead. It is never the group's first item, so the fix is never `Rejected(SplitAtGroupStart)`.

Ties: smaller date gap, then distance. Earlier clips are never auto-appended to a later-dated folder (by hand: confirm "Folder is dated Sep 28; these clips start Sep 27"). A shared target → Info + [Merge]. **Every Medium append is acknowledged at preflight.**

**Scenario D** (library lacks Anvil; Council leftovers on the card): one group of 4 Imported 7/25 + 21 New 7/26 clips → **Append(Council Road) · Medium**, "different day, 34 mi from Council Road", [New folder instead], emphasised split at the same point; either click → AlreadyImported(Council Road) + **NewFolder `2026\2026-07\2026-07-26`** (blank name, so Blocking `EmptyFolderName` until named when no suggestion exists).

### 5.7 Naming and suggestions (Ref §8.6–8.7)

**Folder names:** dated by the **earliest video's** local date (7/31–8/2 → `2026\2026-07\`); Append keeps F's real path at any depth; a new path equal to an existing folder becomes Append ("Folder exists; appending"). `Clean`: `<>:"/\|?*` and chars &lt; 0x20 → space; collapse and trim whitespace; strip trailing dots/spaces; ≤ 80 chars; commas kept.
- **Blocking:** an empty NewFolder name, or a temp path (`final + ".uas-sort.tmp"`) longer than 400 characters. Every P/Invoke path gets `\\?\`.

**Suggestions** (≤ 6, deduplicated, per **local-day centroid**): (1) existing description on Append; (2) ledger folders ≤ 3 mi; (3) GeoNames feature ≤ 1.5 mi (mountain, peak, hill, valley, pass, cape, island, peninsula, point, bay, lake, glacier, fjord, cove, lagoon, inlet, sound, strait, harbor, falls, park; feature codes in Ref §8.7); (4) populated place ≤ 3 mi; (5) "near &lt;town>" (≥ 1,000 people, ≤ 30 mi). NewFolder names are prefilled in italics from 1–4 (not Blocking); none → blank → Blocking. Data ~7.7 MB gzipped, from GeoNames `US.zip` plus `cities5000` (dump of 2026-09-27; record layout in Ref §8.7); About credits "GeoNames CC-BY 4.0".

### 5.8 Set folders (Ref §8.8)

```
Resolve(set):
  ledger has this SetName with the same (member, size) list and first-frame captureUtc → Imported
  for candidate in [SetName, "SetName yyyy-MM-dd" (first-frame local date), "… (2)", "… (3)", …]:
    used by another set in this batch                          → next
    no <any listed root>\candidate, or it is empty             → Plain / DateSuffixed
    existing == card members (name + size + mtime ±2 s)        → Imported
    existing ⊂ card members, all matching, nothing extra       → Resume: copy only the missing members
    card members ⊂ existing, all matching                      → Imported (e.g. after a partial Card cleanup)
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

**Stages:** Setup → Card → Scan → Review → Commit (Preflight → Copy → Verdict); optional **Cleanup** from Review or Verdict (Choose → Not-in-library review → Confirm → Delete → Result, then a rescan; §7.5).

- **Setup:** roots with free space; read-only ledger status ("History: 2 PCs' ledgers found" / "No history yet…" / cloud-only → Blocking + [Keep on this device]). **Card** only for zero or several candidates. **Commit** and **Cleanup** disable refresh, Rescan, Settings, Browse.
- **Chrome:** a TitleBar card chip ("E:\ · DJI Air 3S · serial 1A2B-3C4D · 214 files · 61.3 GB") and a **[Clean up card…]** command (§7.5), Mica, the system theme, a 1100×700 minimum, and tabs **Videos / Photos / Other**.

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
- **Clip list:** virtualised ItemsView, Extended selection; day-split banners inside the new day's first row; columns include, 96 px thumbnail, name, local time + source, size, status, miles from centre; thumbnails via `Thumb.Key` (LRU 400, key re-checked before assigning). The time tooltip shows UTC, drone clock and site-local time side by side ("18:06:27 UTC · drone clock 14:06:27 (UTC−4) · 10:06:27 AKDT (UTC−8)").
- **Clock mismatch** (§5.2; Ref §9.2, §9.4): when any item has `ClockMismatch`, a Warning InfoBar on Review, dismissible for the session (the site zones are filled in; Ref §9.2): "Drone clock is set to UTC−4 (America/New_York), but footage on this card was shot in Alaska (UTC−8). Dates here use local time at each site. To fix the drone clock: RC 2 → Settings → System → Date & time → turn off the network-provided time zone and set the zone for where you are flying. Network time zones can be wrong on ship or hotel Wi-Fi, and the RC keeps the last one it saw until it reconnects." Affected group cards carry a "clock ≠ local" chip. The verdict is not affected.
- **Map** (Ref §9.6): WebView2 (data in `%LOCALAPPDATA%\uas-sort\WebView2`), virtual host `map.uas-sort.example` → `MapAssets`, handler registered before `Navigate`, downloads cancelled. If `.mjs` isn't served as JavaScript (checked first when the map pane is built, §11 step 11): `.js` + `setWorkerUrl` → `WebResourceRequested` with explicit Content-Type → Leaflet 1.9.4. Messages carry `v:1` + `type` (`AllowOutOfOrderMetadataProperties = true`, `[lon,lat]`, unknown types logged).

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
- **Units:** always miles ("&lt;0.1 mi", one decimal below 10, otherwise whole numbers); decimal sizes; site-local times with the zone abbreviation, and "~" when the time comes from the drone clock; clip lengths "3:42" or "1:02:05".
- **Settings** (Ref §9.14): roots (old photo root → `previousPhotoRoots`); ledger card (derived path + status, [Keep on this device], [Open], local backup, video-root prompt §8); R/G defaults; drone clock (site-local or a zone; last learned); copy JPG twin; map URLs; About.

---

## 7. Offload engine (Ref §10)

### 7.1 Compile and preflight

**Compile:** one job per file of each included unit — videos → group target; photos → `<photoRoot>\name` (+ twin if `copyJpgTwin`); sets → `<photoRoot>\<folder>\member` (Resume: missing members only). SkipGroup, NothingToCopy, Imported and Decided are skipped; ticked conflicts get `(n)`; `CreatesFolder` only for NewFolder targets and new set folders under the photo root; `SeenIfNotCopied` = New, Conflict or ticked photos/sets; order groups → photos → sets.

**Blocking:** card identity changed ("A different card is in E:; rescan") or top level unreadable; lock held elsewhere; a root that an included job targets (the video root always counts, because it holds the ledger) is missing, not a directory, or under the card — a missing root that no included job targets only unticks its items, with the reason shown and newness unknown; ledger folder unlistable, unwritable or holding a cloud-only `ledger*.jsonl` ([Keep on this device]) — a merely *missing* folder is fine (Start offload creates it; a failed create stops before any copy); roots unconfirmed after recovery; **Append target gone** ("folder renamed or moved since the scan; rescan"); free space on a destination volume &lt; `Σ size + max(1 GiB, 2 % of Σ size)`, where Σ is that volume's job bytes (Σ 31.4 GB → 32.47 GB free needed); duplicate destinations; temp path > 400 chars; Blocking plan issues.

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

**After the loop** (also after Cancel): non-NTFS/non-fixed destinations (exFAT D:) get `FlushDestination` for renamed files and directories (UNVERIFIED; checked in the rehearsal offload, §10 acceptance step 4) and are listed for safe removal. **Invariants:** final name only after verification (a crash leaves only temps, listed by the next preflight and deleted at Start offload); keep-awake and the offload lock for the whole Commit; no card thumbnail reads while copying.

### 7.3 Ledger writes

Records go to the own file and its local mirror, through `ILedgerWriter.Append` (flush per line). Each carries `id` (GUID), `machine`, `v:1` and `run`.
- **`file`:** one per Verified (`unbuffered`/`cached`) or AlreadyThere (`nameSize`) file.
- **`folder`:** one per group; also `source:"cardLeftovers"`.
- **`seen`:** at the end of **every** Commit (including cancel and failures), one for each uncopied `SeenIfNotCopied` unit; a set gets one per member (§5.4).
- **`run`:** one per run.
- **`torn`:** written by `OpenOwn()` when the own file doesn't end in `\n` (a crash mid-append): it first appends `\n`, then `{"t":"torn","v":1,"id":…,"machine":…,"line":N}`. Readers skip line N of that machine's file without a parse issue, so one crash never caps later verdicts.

Outside Commit, only user actions write: decisions (one per member for a set), `revoke`, the video-root [Copy], and Card cleanup (`cardDelete`, §7.5).

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

**Verdict page:** "Not copied" list with **nothing preselected**; [Record selected photos as already imported] → `assumedImported` (photos, sets; tile or day); [Mark selected as not needed] → `dismissed`. **Only photos and sets may be selected per day. Truncated clips are videos, and like every other video they are dismissed one at a time; unknown files are also one at a time (they usually have no local date).** Both actions confirm counts and GB; [Undo] → `revoke`; the report saves automatically. **[Clean up card…]** opens Card cleanup (§7.5).

### 7.5 Card cleanup (Ref §10.6; added 2026-09-27)

An explicit action, separate from the offload, that deletes card files. The offload (§7.1–7.4) never writes the card and the card reader stays read-only; only `ICardEraser` deletes, and only files named in a `ConfirmedCleanupPlan`.

**Availability.** **[Clean up card…]** on the Verdict page and in the Review title bar, for a scanned card that passes the cleanup volume check below. Disabled during Commit, while a scan runs, on a write-protected card (`FILE_READ_ONLY_VOLUME`: "The card is write-protected (lock switch)"), for a **Browse to folder** source ("Cleanup works only on a detected card. A browsed folder could be a backup copy."), and when the volume check fails ("This doesn't look like a drone card (it may be a backup drive)"). The CLI has no cleanup command.

**Cleanup volume check** (stricter than detection, §5.1, which ignores DriveType): the root is a volume root (`GetVolumePathNameW(root) == root`); the file system is exFAT or FAT32; the bus is SD, or USB with removable media (`IOCTL_STORAGE_QUERY_PROPERTY`: `BusTypeSd` or `BusTypeMmc`, or `BusTypeUsb` with `RemovableMedia`; UNVERIFIED per reader until first-card acceptance, §10); the volume is not the system, boot or paging volume and holds no configured root, previous photo root or `%LOCALAPPDATA%`; and the drone-written index is present (`MISC\FC*.db` or `MISC\IDX\`; seen on the Air 3, UNVERIFIED on the Air 3S until first-card acceptance). The page computes it for the button; `ICardEraserFactory.Open` computes it again from Win32 and never trusts `CardSource` flags. The summary shows the volume's label, serial, capacity and bus.

**Precondition.** The identity equals the scan's (else "A different card is in E:; rescan") and is pinned from here on. A fresh re-list plus `CardAudit` (with this run's `OffloadResult`, or none) gives every card file its category, and fresh listings of the library roots (listings only) give the current evidence; a file changed since the scan is never deleted. `ILedgerStore.Check()` runs too: a cloud-only or unwritable ledger folder, or a missing video root, is Blocking, as for Commit.

**Modes** (a `SelectorBar`; both walk units **oldest first** by `CaptureUtc`, ties by `ItemId`)
1. **Before date:** a `CalendarDatePicker` with no default (Continue stays disabled until a date is picked). The cutoff is the picker's own calendar day, `DateOnly.FromDateTime(picker.Date.Value.DateTime)`, never converted through `UtcDateTime` or `ToLocalTime`, and the cutoff text is built from that same `DateOnly`. Deletes units whose **site-local** date is strictly before the chosen day; that day is kept. A flight that crosses local midnight is split at midnight, and the summary says so. The cutoff line is worded from the plan: "Deletes 152 files captured before Jul 26, 2026 (local time at each site) that are in your library; Jul 26 and later are kept. 14 older files are kept (see Kept)." It says "everything" only when the Kept list is empty.
2. **Free space:** two options with one `NumberBox` (decimal GB): **Have at least [X] GB free** (default; target free = X) or **Free up [X] GB** (target free = current free + X), beside "E: 12.4 GB free of 256.1 GB" and a live "will delete ≈ Y GB" (in the second option also "= free up ≈ Y GB"). Deletes the **shortest oldest-first prefix** of deletable units with `free + Σ allocated ≥ target`, where allocated = size rounded up to the cluster size (`GetDiskFreeSpaceW`); the real free space is re-read afterwards. The cutoff is stated: "Deletes 143 files · 58.2 GB · captured Jul 3 – Aug 30 14:22 AKDT → cutoff: Aug 30 14:22 (3 of 9 files from Aug 30)". Target already met → nothing to delete. Unreachable → the plan is every deletable unit, and "Only 41.0 GB can be freed; 12.3 GB is held by files that are not in your library" with [Include files not in my library].

**Eligibility** (per file, first match; a unit takes the worst of its files: Evidence &lt; NotInLibrary &lt; Never)

| Eligibility | Files |
|---|---|
| **Never** | Unknown files; probe or enumeration errors; `ChangedOnCard` or changed since the scan; read-only files (attributes are never cleared); Skip files that are not companions (`MISC\**`, `LOST.DIR`, system and dot files not tied to a unit); folders (`DCIM`, `DCIM\DJI_###`, `PANORAMA`, `HYPERLAPSE`, `MISC`), except an emptied set folder; anything the plan doesn't name |
| **NotInLibrary** ("not proven to be in your library"): deleted only with **"Also delete files not in my library"** on and the row left on Delete | New; ProbablyImported/AssumedByRule; Conflict (a different file with that name is in the library); Truncated (always, even when matched by name+size: the drone may still repair the card copy); ConfirmedByYou, both kinds: `dismissed` ("you marked it not needed on Oct 4") and `assumedImported` ("you recorded it as imported on Oct 4; not verified"); a **video** that no current library listing holds, whatever the ledger says ("copied on Sep 27, no longer in your library"); a photo whose only evidence is a `nameSize` ledger record |
| **Evidence** | VerifiedThisRun, InLedger, NameSizeMatch, with this proof: a **video** only when a current library listing holds `(NormName, size)`; a photo, JPG twin or set member when a current listing holds it **or** the ledger has a verified (`unbuffered`/`cached`) `file` record (Lightroom moves photos; counted separately as "found only in the history") |

**Units.** A DNG+JPG pair goes together. A pano/hyperlapse set is one unit: its members, then `RemoveDirectoryW` of the set folder only if it is empty (never a recursive delete). An MP4 takes its **companions**, same stem and folder: `.LRF`, `.SRT`, the hidden `.<name>.MP4.trinf`/`.avc1`, and the cover JPG only once its Skip rule is on. Companions inherit the unit's eligibility and never make a unit eligible; a companion that is Never keeps the whole unit. Delete order inside a unit: companions, then the JPG twin, then the primary file (set members in name order), so an interrupted unit keeps its primary file on the card.

**Confirmation** (`CleanupPage`, a Page in the Frame; its dialogs are queued through `IDialogService`)
1. **Summary:** mode; the cutoff line; the card (label, serial, capacity, bus); counts by kind; files and GB; captured date range; free space after; the evidence split ("140 in the library listing · 12 photos found only in the history"); a separate line for files uas-sort never copies, "Also deletes 38 files uas-sort never copies: 20 JPG twins (copying is off in Settings), 12 LRF proxies, 6 SRT captions"; an expandable "Kept (older than the cutoff but not deletable)" list with reasons.
2. **Not-in-library review**, required whenever the switch is on: one row per NotInLibrary unit in range with thumbnail; local date, time and zone; clip length (`mvhd` duration "3:42"; truncated "unfinished · ~7 MB"; "photo"; "panorama · 33 frames"); location ("near Anvil Mountain · 0.2 mi" from the PlaceIndex, else "64.5627, -165.3709", else "no GPS"); size; reason (new / probably imported / conflict / unfinished, with "a same-size copy is in your library; the drone may still repair the card copy" when name+size match / you marked it not needed / you recorded it as imported / no longer in your library); a **Keep/Delete** toggle; [Keep all] / [Delete all].
   - Rows start on Delete, because the user opted in, **except** a unit that is ticked for copying in the current plan (the Review plan, or this Commit's plan when it was not copied): it starts on Keep with the badge "ticked for offload", and [Delete all] leaves it alone; only its own toggle deletes it.
   - A row that joins the list after it was first shown (a new date, a new target, or a Keep that extends the Free-space prefix) arrives **undecided**: the Delete button reads "Decide 2 new rows" and stays disabled until each is set, or [Delete all] is pressed again, and the list scrolls to the first one. For the Free-space prefix an undecided row counts as Delete.
3. **Acknowledge:** "Files deleted from a memory card can't be recovered.", plus "Includes 9 files not proven to be in your library." when any are included. The button names the count and size, "Delete 152 files (61.4 GB)", and stays disabled until every box is ticked and no row is undecided. The boxes are bound to the plan's fingerprint, so any recompute clears them. `CleanupPlan` is a class that only `CleanupPlanner.Build` can construct; `CleanupPlan.Confirm(ack, clock)` recomputes the fingerprint from the plan's content (never trusting a stored field), re-validates every candidate (none Never, every NotInLibrary unit acknowledged row by row, every file under `<card root>\DCIM`), and is the only way to get a `ConfirmedCleanupPlan`.

**Execution** (`CleanupExecutor.RunAsync` in Core owns every step below; `ICardEraser` in Platform)
- Before the first delete: take the offload lock (if another window holds it: "Another uas-sort window is offloading", and nothing happens) and keep the PC awake; pause card thumbnail reads (`IThumbnailSource.Pause()` closes the cached card handles); `ILedgerStore.Check()` again, then `EnsureFolder()`, `SnapshotToBackup(runId)` and `OpenOwn()`; list the library roots and reload the ledger union (listings only) for the evidence re-check; then `ICardEraserFactory.Open`, which re-runs the volume check from Win32.
- For each unit, oldest first: steps 0–1 for all its files and step 2 once, before its first delete; then steps 0, 1, 3 and 4 for each file in delete order.
  0. `CurrentIdentity()` ≠ pinned → `CleanupCardSwapped`; stop, the rest `CleanupNotStarted`.
  1. `Stat`: size or mtime differs from the re-list, or the file is gone → `SkippedChanged` (`PartiallyDeleted` once part of the unit is gone).
  2. Evidence re-check (Evidence units only; library files are never opened): videos → the fresh library listing still holds `(NormName, size)` (the ledger alone is never enough); photos and sets → the fresh listing holds it or the fresh ledger has a verified `file` record. Otherwise `SkippedEvidenceGone`. NotInLibrary units skip this check but must carry their confirmation token.
  3. `DeleteFileW(\\?\…)`; removable media have no Recycle Bin.
  4. Append and flush a ledger `cardDelete` record **right after** the delete; if that fails, stop (`LedgerWriteFailed`).
- A failed file stops its unit: `PartiallyDeleted` (the rest stay on the card and are audited as usual) or `CleanupFailed` if nothing was deleted. The run stops if the card is gone or write-protected (`CardRemoved` / `WriteProtected`); an access-denied or sharing error fails only that unit. Cancel stops after the current file. (Outcome and stop types: Ref §3.)
- **After the loop:** re-list the card and re-read its free space (a deleted file that is still listed goes into `StillListed`), release the lock and keep-awake, and return the `CleanupResult`. `CleanupVm` then rescans the card through the normal Scan stage, so the verdict is recomputed, and saves the local report `reports\<ts>-<run8>-cleanup.json` (also when the rescan fails, with the verdict after as NotSafe). The result page shows files and GB deleted, the new free space, skipped units with reasons, and "Safely remove the card before putting it back in the drone" with **[Eject E:]**. Safe removal is what flushes the card; the app never opens a write or flush handle to the card volume.

**Ledger effect.** `cardDelete` records are an audit trail only. They change no newness, and a later scan simply no longer lists the deleted files.

---

## 8. Persistence (Ref §11)

Per-PC state lives in `%LOCALAPPDATA%\uas-sort\` and binaries in `%LOCALAPPDATA%\Programs\uas-sort\<version>\`. **The ledger lives in `<videoRoot>\.uas-sort\`.**

| Item | Where / format | Safety |
|---|---|---|
| Settings | `settings.json`, `schema:1`, **no `ledgerDir`** | Temp file + `File.Replace` with `.bak`. If unreadable: derived defaults, the file kept as `.corrupt-<ts>`, `RootsConfirmed=false` (**Blocking until the roots are confirmed**), and roots offered from the latest `run` record in any local backup subfolder (else `<derived default video root>\.uas-sort\`). `Load(readOnly: true)` (the CLI) never renames or creates anything |
| Ledger | Appends only to `<videoRoot>\.uas-sort\ledger-<MACHINE>.jsonl`. **Reads the union** of every `ledger*.jsonl` directly in that folder, deduped by `id`. JSON Lines, `t` discriminator, `v:1` | Flush per line; single writer. A torn **final** line is skipped, and so is a line that a later `torn` record of the same machine names (written by `OpenOwn()` before its next append, §7.3). **Any other bad line is Blocking until [Accept and continue]**, and then caps the verdict at SafeWithAssumptions |
| Ledger backup | `%LOCALAPPDATA%\uas-sort\ledger-backup\<root key>\` (**local**; `<root key>` = XxHash64 of the lowercase canonical video root, 16 hex digits): a mirror plus a snapshot at each Start offload and at the start of Card cleanup's deletes (20 kept per root) | Mirror written during Commit and on every own-file append (decisions, revokes, [Copy], `cardDelete`). Source for recovery and for [Copy] |
| Drafts / reports / logs | `drafts\<DraftKey>.json`; `reports\<yyyyMMdd-HHmmss>-<run8>.json` (Card cleanup: `…-<run8>-cleanup.json`); `logs\` (14 days) | Temp file then replace; written once; — |

**Record kinds:** `file`, `folder`, `seen`, `decision`, `revoke`, `run`, `torn`, and `cardDelete` (Card cleanup's audit trail, one per deleted card file; loaded into the snapshot, read by no rule). At 10k lines the ledger is ~2.5 MB and loads in ~120 ms.

**Folder status** (`Check()`, attributes only, before each load)

| Status | Shown as |
|---|---|
| Missing | Info "No history yet"; the folder is created and pinned at Start offload, or when Card cleanup starts deleting |
| Not pinned while in a sync root | Warning InfoBar, with [Keep on this device]; Start offload pins it anyway, so it is not on the preflight sheet |
| A cloud-only `ledger*.jsonl` | **Blocking**, with [Keep on this device]. The file is never opened |
| Unwritable | Blocking for Commit and Card cleanup |
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
| A ledger file is cloud-only, or the folder is unwritable | Blocking for Commit and Card cleanup, with [Keep on this device] → pin and sync, then Rescan |
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
| Card cleanup: the card is removed or swapped mid-run | Stop (`CardRemoved` / `CardSwapped`); the unit in flight is `PartiallyDeleted` or `CleanupFailed`, the rest `CleanupNotStarted`; files already deleted keep their `cardDelete` records → reinsert, then Rescan |
| Card cleanup: a card file changed, or its library evidence is gone | That unit is skipped (`SkippedChanged` / `SkippedEvidenceGone`) and listed on the result page; cleanup continues |
| Card cleanup: delete refused | Write-protected (lock switch moved) → stop (`WriteProtected`); access denied or sharing violation → that unit `CleanupFailed` / `PartiallyDeleted`, cleanup continues |
| Card cleanup: a `cardDelete` ledger append fails | Stop at once (`LedgerWriteFailed`); the file is already gone and is named in the cleanup report |
| Card cleanup: a set only partly deleted | `PartiallyDeleted`; the remaining members and the set folder stay on the card, and the rescan audits them normally (a subset of a matching library set folder is Imported, §5.8) |
| Card cleanup: a deleted file is still listed after the loop | Listed under "still on the card" (another program held it open; Windows removes it when that program closes it) → close the program, then Rescan |
| Card cleanup offered on a volume that fails the cleanup volume check (e.g. a USB backup drive holding a card copy) | [Clean up card…] disabled: "This doesn't look like a drone card (it may be a backup drive)"; the eraser factory refuses it too → offload from the real card |

---

## 10. Testing strategy (Ref §13)

Run with `dotnet test --solution uas-sort.slnx` (xUnit v3 on MTP); analyzer probe via `tools/build.ps1 -CheckBannedApi`.

| Layer | Key cases |
|---|---|
| **Core unit** | The 37 tests from `docs/research/spikes/grouping/test_grouping.py`, ported per the table in Ref §13 (C# name, inputs, expected values): 24 keep their expectations with spike strings mapped to enums, 9 are restated for the approved model (e.g. `AppendSplit` → two groups split by a LibraryFolder wall; `PhotosOnly` → PhotoDays), 3 are R-dependent, and the Autel floating-time test is dropped. 34 of 37 pass at 50 mi in the spike (re-checked 2026-09-27). `test_consecutive_days_far_apart_split` becomes R = 50 mi (1 group + emphasised DaySplit) and R = 25 mi (2 groups); the two no-GPS tests are pinned to 25 mi **and** duplicated on a synthetic pair ~70 mi apart. New cases cover walls, a wall folder dated after the New clips, `NothingToCopy`, a ticked Conflict turning AlreadyImported into Append, the two-pass UserSplit rule, Scenario D, next-day confidence, pins, folding, sets, photo Conflicts (rule 2b), `(n)`, `SessionKey` tolerance (0.8 s apart = same, 3 s = different) and the 400-character temp path. Also the time cases: midnight → Jul 25, and Hawaii Feb 28; the clock learner and `ClockMismatch` (Ref §13): SiteLocal fit (samples shot in Alaska with an Alaska-set clock → SiteLocal); an Eastern clock in Alaska → Zone `America/New_York`, `ClockMismatch` on every item and the InfoBar; an Eastern clock in Newport RI → no mismatch; a trip crossing zones with a site-local clock; a photo-only card using the stored mode; the 15-min mismatch boundary; Zachar Bay 0128 → 10:06:27 AKDT with `ClockMismatch` |
| **IO guard policy** | Table-driven Core test of `IoGuardPolicy.Check` over every `IoOp`: ledger reads allowed only for top-level `.uas-sort\ledger*.jsonl`; append only to the own file; `CreateDir` only for NewFolder targets, new set folders under the photo root and `.uas-sort` itself; `CardDelete` only for a file, or an emptied set folder, named in a confirmed plan on a verified card volume (no plan, a browsed source, another directory, a path outside the plan or outside the card, or any library, ledger, AppData or system-volume path → `UnsafeIo`); `OpenForFlush` or any write of the card root or a card file refused in every context; placeholder bits → `Hydration`/`CloudOnly`; everything else `UnsafeIo` |
| **Card cleanup** | Eligibility table (every audit category and newness × the include switch; `dismissed` and `assumedImported` → NotInLibrary; a video with a ledger record but no current listing → NotInLibrary; a ledger-only photo → Evidence, counted apart); unit completeness (pair, set, companions; a companion never makes a unit eligible); Before date strictly before the site-local day, incl. a flight across local midnight and the picker's `DateOnly` in an Alaska PC zone; Free space in both readings (free up X, have X free), minimal prefix with cluster rounding, target already met, unreachable target; Keep toggles re-compute the prefix, new rows arrive undecided, acknowledgements clear; units ticked for offload start on Keep; `Confirm` required, and a plan whose content no longer matches its fingerprint is refused; browsed source, a failed volume check (fixed exFAT USB drive holding a card copy) and the production factory on a `%TEMP%` folder refused; write-protected disabled; per-file re-checks (changed, evidence gone incl. a deleted library video with a ledger record, identity change mid-run); partial unit; Cancel after the current file; `StillListed`; `cardDelete` records and ledger-failure stop; the report, also when the rescan fails; Preparation's Blocking states (Ref §13) |
| **Invariants** | Each video is in exactly one group; undo+redo is the identity; a quick fix is one entry; a replayed draft is equal; edits stay in the log for every R from 5 to 100 (SplitBefore at R 50 → 25 → 50 keeps its UserSplit chip); **local dates never change with R or G** |
| **Performance** | Release benchmark: a synthetic 500-item `PlanBase`, median of 20 derives &lt; 50 ms |
| **Newness, ledger** | Photo rule order; **app copies never move the watermark**; no watermark → every unseen photo New; `seen` across two runs and after a cancel; a set with a partial `decision` stays undecided; the photo root moved to D:; **`.uas-sort` is not indexed**; the per-PC union and conflict-copy dedupe; torn vs. bad lines, and a crash mid-append followed by a normal run → no parse issue, no cap; the derived location; [Copy] is idempotent and, with the old root gone, uses snapshot ∪ mirror; [Start empty] confirms when the new root holds app-copied videos |
| **Golden replay** | The checked-in fixture `tests/UasSort.Testing/Replay/library-listing.json` (copied from `docs/research/fixtures/library-listing.json`, pre-generated on 2026-09-28 from the listing only, no video copies, plus the GPS in `docs/research/spikes/djmd/calibration.json` and `docs/research/spikes/grouping/more_gps.json`; never regenerated by tests). Scenarios A0, A and B–E are tabulated in Ref §13: A0 gives 7 groups at 50 mi, and 8 after `SplitBefore`; R {8, 10, 20, 30, 33} → 8 and {40, 50, 60} → 7; A (walls) → 8 AlreadyImported; B → NewFolder `2026\2026-09\2026-09-27` (13 clips); C → Append High (4 new); D → Append Medium with the hint; E → NewFolder `2026\2026-07\2026-07-26`; every NewFolder is blank-named with a Blocking `EmptyFolderName` (the replay uses no PlaceIndex and no ledger); watermark 2026-09-27T18:24:16Z |
| **Synthetic media** | `SyntheticMp4Builder` (port of `docs/research/spikes/djmd/synth_test.py`; box layouts, zeroed GPS, units, unknown protocol, no `moov`; ≤ 16 reads to the first fix; `mvhd` duration, versions 0 and 1) and `SyntheticDngBuilder` |
| **Fake-FS tripwire** | Every library file except the local `.uas-sort\ledger*.jsonl` carries `0x401620`; over scan, plan and offload only the ledger exemption (local ledger files read, own file appended) and this run's own temps and just-renamed files are ever opened. A cloud-only `ledger-B` opens nothing and gives Blocking `CloudOnly`. Writing `ledger-B`, `.uas-sort\settings.json`, `.uas-sort\sub\…`, or `<videoRoot>\ledger-X.jsonl` → `UnsafeIoException`. Over scan, offload and Card cleanup, any card delete outside a confirmed plan records a `CardDeleteViolation` (every fixture asserts none), and cleanup opens no library file |
| **Fault injection** | Bit flip; disk full; card read error (one re-read, then `Failed(Copy)`); card vanishes; **identity change → CardSwapped**; target appears; wrong size after rename; ledger throws; Cancel writes `seen`; ChangedOnCard; Append folder deleted; Back from the sheet deletes no stale temp |
| **Windows integration** | Unbuffered read-back; no-replace rename; 300-char path; lister sees Hidden/System, **opens nothing** (`FileShare.None` locks), skips `.uas-sort` via `excludeDirNames`; deny-write ACL reader; exemption via `subst`/junction (`.uas-sort2` refused); `LedgerStore` consults the policy (a planted `.uas-sort\notes.txt` is never opened); `EnsureFolder()`/`KeepOnDevice()`; mutex; XAML lint; `WindowsCardEraser` on a `%TEMP%` fake card through a test-only volume-facts seam (`DeleteFileW` on `\\?\` paths removes only named files, incl. a Hidden `.trinf`; `RemoveDirectoryW` removes an emptied set folder and leaves a non-empty one; a browsed source is refused; the production factory refuses the same `%TEMP%` folder) |
| **View models** | Chip merge; split; quick-fix undo; slider drag and 400 ms idle commit; a gated `IPlanDeriver` proves a slow earlier derive never overwrites a later one; draft resume; preflight acknowledgements; [Accept and continue] on a bad ledger line; Verdict page (nothing preselected, per-day selection for photos and sets only); Cleanup page ([Clean up card…] disabled states incl. the volume check, Preparation Blocking InfoBars, the picker-to-`DateOnly` conversion, required review list, "ticked for offload" rows on Keep, undecided new rows, Keep/Delete recompute, acknowledgements cleared on recompute, the plan-worded cutoff line, "Delete N files (X GB)" label); map JSON with `v` before `type`, incl. `ping`/`pong` |
| **`--selftest`** | Native AOT build, off-screen, in `%TEMP%`. Checks templates render their text ("Anvil Mountain"); MapLibre `ready` and a `ping`/`pong`; StillProbe; GeoTimeZone; JSON round-trips; a visible `0x400000`/`0x1000` entry (otherwise `-AllowNoPlaceholders`). Exits 0/1 and writes `selftest-result.json` (with the first-frame time); deploy waits 60 s per run, runs it twice, and fails if the second (warm) run's first frame is over 1 s |

**First-card acceptance** (by the user, after the build is complete; §11)
1. Snapshot the card listing, including last-access times.
2. Compare the CLI `plan --json` output with `tests/acceptance/first-card-expected.json`, which the user writes before this acceptance run (≤ 2 `PlanEdit`s apart). Record the scan + plan time.
3. Re-list and diff. Any last-access change is documented; the app never suppresses it.
4. Rehearse into `%TEMP%\uas-sort-rehearsal\{video,photo}`. Check the verdict, `.tmp` not syncing, exFAT verify/flush on D:, and [Eject D:].
5. Return to the library with **[Start empty]** (confirm its dialog if it appears: the rehearsal copied the same card files), confirm the pin in Explorer, and do the real offload. Then diff again.
6. Rescan: everything should be Imported and the verdict Safe, or SafeWithAssumptions for photos the user chose to leave.
7. **Card cleanup:** snapshot the card listing, run Before date with a cutoff that takes exactly one old eligible clip (and its companions), confirm, then re-list: only that unit's files are gone, each has a `cardDelete` record, and the rescanned verdict is unchanged for everything else.

---

## 11. Build plan (single pass) (Ref §14)

The initial, feature-complete version is built in one pass by an AI coding agent (user decision 2026-09-27, §2.2). There are no milestones, effort estimates, checkpoints or staged deliveries: every feature in this spec is in the initial version, and only the Later list (Ref §1.4) is deferred. The CLI is a product feature (the dry-run `plan`, Ref §4.5), not a development stage.

**Prerequisites.** The install-once list of §3.1 (Ref §2.6): the .NET 11 SDK, PowerShell 7 and the C++ build tools workload for Native AOT. The build starts only once they are installed.

**Build sequence.** Parts are built in this order because each uses only the parts before it. The tests of each part are written with it, test-first (§10), in the same pass; there are no separate test batches, and the whole suite stays green as later parts land. Ref §14 lists each step's contents and tests.
1. **Stack proof.** Solution skeleton with central pinned packages, `BannedSymbols.txt` and the analyzer probe; the App with a probe page (GridSplitter, SettingsCard, FolderPicker, AppInstance + mutex, WebView2, ItemsView) publishes **Native AOT** (`PublishAot=true`) with `TreatWarningsAsErrors` and passes the minimal `--selftest`: {probe page renders, WebView2 reaches the virtual host, MetadataExtractor Stream read of a checked-in ≤ 2 KB EXIF JPEG `stack-exif.jpg` (hand-assembled, no user data), GeoTimeZone lookup, closed-record JSON round-trip}; the AOT size and warm first frame are recorded against the 0.37 s ReadyToRun baseline; cross-assembly exhaustiveness test. Fallback (package compatibility): toolkit 8.2 + full package, or fixed panes. It comes first so that nothing is built on a stack that can't publish.
2. **Core model, ports and guard policy:** the model, the ports, `IoGuardPolicy` (ledger exemption, `CardDelete` rules), the fake FS with its tripwires; card detector, source validator (policy), classifier.
3. **Media probes:** Mp4Probe (incl. `mvhd` duration), synthetic MP4/DNG builders, StillProbe, thumbnail reader, harvester.
4. **Time and geo:** clock learner (SiteLocal, zones, NearestSample with clock changes), TimeResolver (incl. `ClockMismatch`), GPS gate, GeoTimeZone resolver; PlaceIndex and the GeoNames extract tool `tools/places/build-places.cs`.
5. **Library index, ledger and settings:** LibraryIndex; ledger reading (derived location, union, dedupe, parse issues, `torn` markers, attributes first); settings with recovery and the read-only load.
6. **Planning and editing:** newness, clustering and structural edits, day splits, folder decider, naming and suggestions, sets, Planner, PlanSession, ScanService.
7. **Offload engine and audit:** compiler, preflight, CopyEngine, ledger writes, CardAudit and verdict, reports.
8. **Card cleanup** (§7.5): cleanup volume check, CleanupPlanner, CleanupExecutor, `cardDelete` records.
9. **Platform implementations:** volume provider, lister, card reader, `WindowsCardEraser` and its factory, file ops, `LedgerStore`, stores, PlaceholderGuard, sync-root detection, keep-awake, locks, eject, single instance.
10. **Review view models**, incl. the map bridge messages.
11. **WinUI App:** shell and stages; Setup and Settings (ledger status, [Keep on this device], video-root prompt); Card and Scan; Review (timeline, group card, clip list, Photos and Other tabs, undo, drafts, keyboard, dialogs); the map pane (the MapLibre-in-WebView2 `.mjs` MIME check is done first when it is built, then the bridge, sync, live sliders and offline view); Commit (preflight, copy, verdict) and device-arrival refresh (UNVERIFIED); the Cleanup page and its entry points; the full `--selftest`.
12. **CLI:** `uas-sort-cli plan` (Ref §4.5).
13. **Native AOT publish and deploy:** `win-x64` only; `deploy.ps1` (AOT publish, selftest gate, `.lnk`, keep 2 versions).

**Completion criteria** (the one-pass build is complete when all hold)
- The full solution builds with `TreatWarningsAsErrors`.
- The whole test suite passes (§10): unit, IO guard policy, card cleanup, invariants, performance, newness and ledger, golden replay, synthetic media, fake-FS tripwire, fault injection, Windows integration and view-model tests; `uas-sort-cli plan --json` runs on a copied card folder under `%TEMP%` and writes nothing.
- The banned-API probe (`tools/build.ps1 -CheckBannedApi`) raises every expected RS0030.
- The Native AOT publish passes `--selftest`: `deploy.ps1` passes its selftest gate on x64, incl. the warm first frame ≤ 1 s.

**Acceptance by the user, after the build.** The first-card acceptance steps (§10): the CLI dry run matches `first-card-expected.json` within 2 edits; a rehearsal offload to scratch roots; the real offload; and Card cleanup of one old eligible clip. They accept the finished product and are not build stages. Anything they turn up (e.g. the cover-JPG rule) is fixed in the finished code, and the completion criteria are re-run.

**Maintenance after the build.** The SDK **RC2** (~Oct 13, UNVERIFIED) and **GA** (Nov 10) updates each re-run the AOT publish, `--selftest` and the analyzer check; `AnalysisLevel` stays pinned.

**If Native AOT fails a concrete check** (a build or AOT warning, a selftest failure, or a warm first frame slower than the 0.37 s baseline), ReadyToRun is used only to diagnose it, and the problem is raised with the user rather than silently switching the build type (§2.2).

---

## 12. Open questions (Ref §15)

The ledger-location question is **resolved** (§2.2) and removed, and so is the packaging question (resolved 2026-09-27: Native AOT is the primary build, MSIX stays unused; §2.2–2.3); the rest are renumbered. Items 1–7 have defaults the user accepted; item 8 was added with Card cleanup and its default is proposed, not yet reviewed. None blocks work.

| # | Question | Default | Still open |
|---|---|---|---|
| 1 | Tick truncated clips? | Unticked; GPS recovered; the verdict says "Don't format yet" | Whether the Air 3S repairs a clip when powered on with the card inserted |
| 2 | Copy the JPG twin? | Yes; when switched off, it counts as SkippedByRule | — |
| 3 | Esri imagery without a key? | Esri, with a USGS preset | Esri's terms. The fallback is USGS, with no code change |
| 4 | Daily flights chaining into one group? | No span cap; one-click splits; next-day ≥ 10 mi is Medium | A max-span setting (Later) |
| 5 | ARM64? | **Resolved: x64 only (user, 2026-09-28).** No ARM64 build; `deploy.ps1` publishes `win-x64` | — |
| 6 | PowerShell 7? | The user installs it with winget (§3.1 Prerequisites) | — |
| 7 | Autel cards? | Unsupported (Unknown → NotSafe); legacy folders still match | — |
| 8 | Does the drone's `MISC` media index cope with PC-side deletions? | *Proposed 2026-09-27 with Card cleanup; not yet reviewed by the user:* Card cleanup deletes only DCIM media and their companions and never touches `MISC`; the result page says the drone may show stale thumbnails until it rebuilds its index, and formatting in the drone remains the clean option | Whether the Air 3S rebuilds its index or shows stale entries (UNVERIFIED; checked in first-card acceptance step 7, §10) |

**Other UNVERIFIED items, by build step** (§11; facts that need the real card are checked in first-card acceptance, §10)
- **Step 1, stack proof:**
  - the Native AOT publish of the WinUI app with warnings as errors, its size, and its warm start against the 0.37 s ReadyToRun baseline (a failure is raised with the user, §2.2);
  - cross-assembly exhaustiveness;
  - `AnalysisLevel` 11.0;
  - trimming MetadataExtractor;
  - AppInstance;
  - Sizers.
- **Step 11, WinUI App (map pane):** the `.mjs` MIME type, checked first when the map pane is built.
- **Steps 11 and 13, full `--selftest` and the `deploy.ps1` gate:** the placeholder compatibility call (`--selftest` placeholder visibility; the deploy gate, with `-AllowNoPlaceholders` as the override).
- **First-card acceptance, steps 1–3 (CLI dry run and card diff):**
  - exFAT mtime;
  - FC9113;
  - last-access writes.
- **First-card acceptance, steps 2 and 7 (card facts used by Card cleanup):**
  - which `MISC` index files the Air 3S writes (`FC*.db`, `IDX\`), used by the cleanup volume check;
  - the storage bus and `RemovableMedia` reported for the user's card reader and for the drone over USB;
  - the drone's `MISC` index after PC-side deletions (Q8).
- **First-card acceptance, steps 4–5 (rehearsal and real offload):**
  - exFAT unbuffered reads and flushes;
  - eject without admin rights;
  - OneDrive honouring the pin;
  - OneDrive skipping `.tmp`.
- **Elsewhere:**
  - an unpackaged `FolderPicker`;
  - focus return after a map click;
  - drone-over-USB volume names;
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
| §7 | §10 (§7.5 ↔ §10.6) |
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
