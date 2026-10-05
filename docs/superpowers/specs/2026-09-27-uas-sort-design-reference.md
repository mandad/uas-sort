# uas-sort — design reference (approved 2026-09-27)

This is the detailed companion to the main spec, [`2026-09-27-uas-sort-design.md`](2026-09-27-uas-sort-design.md). It holds the full types, rules, thresholds, tables and test lists behind it. **If the two disagree, the main spec wins.**

- **Approved:** 2026-09-27. The user reviewed and approved every design section: approach, stack, rules, Review UI and offload safety. The user also chose the ledger location (`<videoRoot>\.uas-sort\`, §11) and accepted the remaining defaults (§15).
- **Changed 2026-09-27: Card cleanup.** The user added an explicit action that deletes card files (§1.1, §10.6). This replaces the earlier requirement "copy only, never delete from card" as follows: the offload and copy path still never writes the card, and the card reader stays read-only; only `ICardEraser`, during Card cleanup, deletes, and only files in a confirmed `CleanupPlan`.
- **Changed 2026-09-27: drone clock and packaging.** The user confirmed that the RC 2 clock was simply never switched from US Eastern (it is not set from GPS) and asked for dates built from UTC with the site's real zone plus a flag when the drone clock doesn't match (§1.1, §6.1, §6.5). The user also made Native AOT the primary build and asked to be told what to install rather than get a weaker fallback (§1.1, §2.1, §2.6).
- **Changed 2026-09-27: single-pass build.** The user asked for the initial, feature-complete version to be built in one pass by an AI coding agent, not progressively or in milestones (§1.1). §14 is now a dependency-ordered build plan with completion criteria; the first-real-card steps (§13) are the user's acceptance of the finished product.
- **Still marked UNVERIFIED:** facts nobody has proven on this hardware yet. Each has a check in a named build step (§14) or acceptance step (§13), or a named fallback. None of them waits on a user decision, except that a Native AOT build failing a concrete check in the stack-proof step at the start of the build is raised with the user rather than accepted (§1.1).

**Evidence.** "The spike" and "the replay" in this document refer to these research reports and spike files in the repo:

| Topic | Report | Spike files |
|---|---|---|
| Card layout, `.trinf` recordings | `docs/research/01-card-layout.md` | `docs/research/spikes/card-layout/` |
| djmd GPS reader, calibration, `tnal` thumbnails | `docs/research/02-djmd-gps-calibration.md` | `docs/research/spikes/djmd/djmd_gps.py`, `calibrate.py`, `calibration.json`, `synth_test.py`; `docs/research/spikes/review-ux/mp4thumb.py` |
| Libraries: MetadataExtractor, GeoTimeZone, XxHash128 (9.2 GB/s), ledger at 10,000 rows, unbuffered verify, GeoNames | `docs/research/03-dotnet-libraries.md` | `docs/research/spikes/dotnet-stack/` (incl. `geonames/rg.py`) |
| Grouping algorithm, the 37 Python tests, library replay | `docs/research/04-grouping-algorithm.md` | `docs/research/spikes/grouping/grouping.py`, `test_grouping.py`, `replay.py`, `replay50.py`, `more_gps.json` |
| Approach scoring and grafts | `docs/research/05-approach-judging.md`, `docs/research/05-approaches.json` | `docs/research/spikes/judge/crossday.py`, `docs/research/spikes/minimal-arch/`, `docs/research/spikes/safety-review/` |
| .NET 11 / C# 15 / WinUI 3 lean stack, trim + ReadyToRun, warm start (the baseline for the Native AOT build) | `docs/research/06-modern-stack.md` | `docs/research/spikes/modern-stack/` (`winui-src/`, `rc1-*.md`) |
| Map: MapLibre, OpenFreeMap, Esri, USGS | `docs/research/07-map-options.md` | `docs/research/spikes/map-pane/` (`web/`, `shot_*.png`) |
| WinUI 3 + WebView2 spike: virtual host, messaging, clicks, Leaflet 1.9.4 | `docs/research/08-winui-spike.md` | `docs/research/spikes/winui/` (`src/`, `logs/`, `scripts/`) |
| Design review findings (Review disposition, end of this document) | `docs/research/09-design-review-findings.json` | — |

---

## 1. Goals, non-goals, success criteria

### 1.1 What the user said (binding)

**What the app does**
- It is a personal Windows app, launched by hand, that offloads a DJI Air 3S card.
- There is no tray icon, service or auto-launch. Refreshing when a device arrives while the window is open is fine.
- It finds media not yet in the library, groups videos by local date and GPS, and lets the user review and edit the groups and descriptions. It then copies with verification.
- The offload is copy-only: it never writes, renames or deletes anything on the card. *(Stated earlier as "copy only, never delete from card"; changed on 2026-09-27 by the Card cleanup request below, which is the only way the app deletes card files.)*
- **Card cleanup** (user request, 2026-09-27; design in §10.6). In the user's words, verbatim: "Add an option to delete files on the card that are older than a certain date. Or to clear a specific amount of free space that would delete up to the, starting from the oldest up to the most recent, that has to be deleted to fit that criteria. It would confirm what the date is of that cutoff." Follow-up (as recorded with the request; may be lightly edited for grammar): "Verified name/size, and then also give an option to delete files that aren't found in the repository, because sometimes I may need to weed out footage that's bad, unnecessary, or accidental. Not everything from the card will be in the repository, but it should confirm the ones that are missing with a listing of their date, the length of the clip, and the location."
  - It is a separate, explicit action. The card **reader** stays read-only; only `ICardEraser` deletes, and only files named in a confirmed `CleanupPlan`.
  - It is offered only for detected card volumes that also pass the stricter cleanup volume check (removable media on any bus (SD, MMC, USB, or a PCIe/SCSI card reader); fixed media is refused; exFAT/FAT32, the drone's `MISC` index; §10.6), never for a "Browse to folder" source or a backup drive, which could hold a copy of a card.
  - **Recorded interpretations** (2026-09-27; the user may overrule any of them):
    - "Clear a specific amount of free space" reads two ways, so both are offered: **Have at least [X] GB free** (the default) and **Free up [X] GB**.
    - "Verified name/size" is the default evidence. A video counts only while a current library listing holds it (the ledger alone is history, not the library); a photo may also rely on a verified ledger copy, because Lightroom moves photos.
    - **Exception to the name/size rule:** an unfinished (truncated) recording is treated as not in the library even when name and size match, because the library copy is unfinished too and the drone may still repair the card copy.
    - Files the user marked "not needed" or "already imported" on the Verdict page are not proven to be in the library; they are deleted only through the not-in-library review list.
- **The app runs on the user's personal computers (plural).** This is a fact, not an assumption, so the ledger must be shared between them (§11).
- **The ledger lives inside the video root, in `<videoRoot>\.uas-sort\`** (user decision, 2026-09-27). On this PC that is `C:\Users\damia\OneDrive\Pictures\UAS Videos\.uas-sort\`. It syncs to every PC along with the library. Each PC appends to its own `ledger-<MACHINE>.jsonl` and reads the union of all of them. The folder is derived from the video root and is not a separate setting.

**Where videos go**
- `<videoRoot>\YYYY\YYYY-MM\YYYY-MM-DD <Description>\`, dated by the local start date. A multi-day trip goes in one folder.
- The video root on this PC is `C:\Users\damia\OneDrive\Pictures\UAS Videos`. It uses OneDrive Files-On-Demand, and many files there are cloud-only placeholders.
- **Library file content is never read**, only directory listings. The "learn locations from library clips" option is dropped, and the Lightroom catalog is never touched.
  - The one exemption for pre-existing files is the app's own ledger: it reads `<videoRoot>\.uas-sort\ledger*.jsonl` and appends to its own ledger file (§4.3). No other pre-existing file under the library is ever opened; this run's own `*.uas-sort.tmp` and just-renamed files are tracked exceptions.

**Where photos go**
- DNGs (and their JPGs) go flat into `<photoRoot>`. Panorama and hyperlapse sets keep their set folder.
- If a set folder name is already used by different content, the date is appended, e.g. `001_0087 2026-09-27`.
- The photo root is currently `UAS Videos\Picture Offload` and may move to `D:\` (an external exFAT drive).
- Lightroom moves photos out after import, so the app's **ledger is the long-term memory**.
- When the ledger has no record, a photo counts as new if it was taken on a date with new videos or after the last-imported-video watermark. Otherwise it is "probably imported": unticked, with the reason shown and an include switch per day.

**Conflicts**
- A file with the same name as a library file but a different size is an unticked conflict. If ticked, it is copied as `name (2).ext`. Nothing is ever overwritten.

**Grouping**
- R defaults to **50 mi** and is **shown in miles**; G = 1.
- The Council Road + Anvil Mountain merge is accepted as long as splitting at the day change takes one click.
- The R slider regroups live.

**Time**
- MP4 `creation_time` is true UTC.
- Folder dates are the local date at the site, found from GPS via the time zone.
- **The drone clock is whatever the RC 2 is set to** (user, 2026-09-27: it was simply never switched from US Eastern after first use; it is **not** set from GPS). In the user's words: "build it from the UTC time using the actual local time zone conversion", and "get a flag for if those don't match with the time set on the drone clock". So:
  - local dates **always** come from true UTC converted with the GPS site zone (§6.4);
  - drone-clock time is used only to convert stamps that carry no UTC (DNG EXIF has none; filenames of clips without `mvhd`, library members and the watermark), and never for a date directly;
  - the clock learner tries **SiteLocal** first (§6.1), and `America/New_York` is only the default of the last learned zone, not an assumption;
  - the **`ClockMismatch`** flag (§6.5) marks items whose drone clock differs from site-local time by ≥ 15 min, with a Review InfoBar and a group-card chip; it does not affect the verdict.

**Frameworks**
- The user wants the most modern frameworks: the latest .NET and C#, WinUI 3 on the latest Windows App SDK, and modern packaging. There are no compatibility constraints ("personal computers").
- **Approved stack (2026-09-27):**
  - .NET 11 (RC1 go-live now, then RC2, then GA on Nov 10) with C# 15;
  - WinUI 3 on Windows App SDK 2.5.1, lean component packages;
  - WebView2 with vendored MapLibre: OpenFreeMap streets, Esri World Imagery satellite, and a USGS preset;
  - offline GeoNames suggestions;
  - an unpackaged, self-contained **Native AOT** folder (`PublishAot=true`, trimmed; `win-x64` only — x64 only, user decision 2026-09-28) plus a Start-menu `.lnk`. No MSIX. *(Changed 2026-09-27 from "trimmed ReadyToRun, no Native AOT (an optional later attempt)"; see below.)*
- The prerequisites are installed once when implementation starts (§2.6): the .NET 11 SDK preview, PowerShell 7, and the C++ build tools for Native AOT. The plan's Task 01.0 installs and verifies them (idempotent; the user approves the UAC prompts). Development runs from a Claude Code CLI session in PowerShell on Windows, not WSL (user decision, 2026-09-28).
- **Native AOT is the primary build** (user decision, 2026-09-27). It is built from the start: the stack-proof step at the start of the build (§14 step 1) is an AOT publish that passes `--selftest` with `TreatWarningsAsErrors`, and `deploy.ps1` publishes AOT (§14 step 13). Trimmed + ReadyToRun is no longer the plan: its measured numbers (x64 87–97 MB, 0.37 s warm first frame) are the baseline AOT must meet or beat (AOT startup UNVERIFIED until the stack-proof step). It remains only as the diagnosis path if AOT fails a concrete check, and that situation is raised with the user, never silently accepted.
- **Installs over fallbacks** (user decision, 2026-09-27; verbatim: "If a framework needed to build this is not on my computer, just ask or give me the information on how to install it… I just want to maintain the best version for the app if that takes me installing something."): when the best option needs a tool that isn't installed, the plan asks the user to install it rather than designing a weaker fallback. Fallbacks remain only for runtime/behaviour risks (e.g. MapLibre-in-WebView2 MIME, unbuffered verify on exFAT, AppInstance, the preview toolkit line's compatibility).

**How the initial version is built** (user decision, 2026-09-27; verbatim: "I want this to be implemented for the initial version in one shot via the AI agent development. So revise anything that's necessary to build it in one go. Doesn't need to be built progressively or tested in batches or milestones, just the initial full feature-complete version.")
- The initial version is built complete, in one pass, by an AI coding agent, in the dependency order of §14. There are no milestones, effort estimates, checkpoints or staged deliveries; every feature in this document is in it, and only the Later list (§1.4) is deferred.
- The tests of each part are written with it (test-first), in the same pass, not as separate batches.
- The first-real-card steps (§13) are the user's acceptance of the finished product, not build stages.

**Grafts to include**
- From Safety: re-check card files before copying, a write-through rename that never replaces, hidden temp files, keep the PC awake, single instance, audit categories behind the "safe to format?" verdict, a banned-API analyzer, and a dry-run CLI.
- From research: GPS for truncated clips, a multi-sample GPS search, a per-model GPS table plus a generic search, and offline GeoNames name suggestions.

**Where this design departs from "most modern frameworks"** (the user approved each departure on 2026-09-27)

| Area | Most-modern option | What this design does | Why |
|---|---|---|---|
| Packaging | MSIX with package identity | **Unpackaged, self-contained folder** plus a Start-menu `.lnk` | **Certificate trust, not a missing install:** MSIX needs a signing certificate trusted on every PC (or a loose Developer Mode registration), which no install fixes. The app needs no identity (no notifications or background tasks), and single instance works unpackaged (§4.4). Self-contained MSIX is UNVERIFIED. Rejected option, for the record: self-contained MSIX via the winapp CLI with a self-signed certificate trusted on each PC (it would give package identity and a clean uninstall); MSIX stays on the Later list (§1.4). |
| Toolkit controls | Stable releases | **8.3.260402-preview2** | It is the only toolkit line that works with the lean WinUI package set. Fallback: 8.2.251219 with the full Windows App SDK package. |
| Test clock package | 11.x | TimeProvider.Testing **10.10.0** | No 11.x is published. |

Native AOT (code generation) was a departure until 2026-09-27: the plan was trimmed + ReadyToRun, with AOT as an optional later attempt that waited on a C++ build-tools install. It is now the primary build, and the C++ build tools are a prerequisite (§2.6).

### 1.2 Assumptions (not stated by the user; each is cheap to undo)

| Assumption | If wrong |
|---|---|
| Every PC runs Windows 11 24H2+ (build ≥ 26100), **x64** (x64 only, user decision 2026-09-28: "Both of my personal laptops are regular x86 Intel") | Lower `TargetPlatformMinVersion`. The modern-stack spike also ran with 10.0.22621.0 (`docs/research/spikes/modern-stack/winui-src/Spike.App/Spike.App.csproj`). An ARM64 PC would need `win-arm64` added back to `RuntimeIdentifiers`/`Platforms` and the MSVC ARM64 build tools (§15 Q5, resolved). |
| One card, or one drone volume, per run; ≤ ~1,000 files; ≤ 256 GB | Offload the second volume in a second run |
| The Air 3S layout matches the research: `DCIM\DJI_###[_x]`, `DCIM\PANORAMA\<set>`, `DCIM\HYPERLAPSE\<set>`, `MISC` | Anything else falls to **Unknown**, which is Unaccounted and therefore NotSafe (§5). Add a rule after the first-card acceptance. |
| The RC 2 clock follows one IANA zone **including DST**, or local time at each site. (Which zone it is set to is not an assumption: it is whatever the RC 2 is set to, observed US Eastern and never reset, and the learner finds it; §1.1, §6.1) | The learner (§6.1) finds no fit on the first card with MP4s and falls back to the nearest sample. Video dates are unaffected, because they come from `mvhd` UTC and the GPS site zone |
| Only DJI cards are offloaded; Autel exists only as 2022 library content | An Autel card's media all shows as Unknown (NotSafe); Autel support would be added later (§15 Q7) |
| There is internet at home, maybe not on trips | The map falls back to an offline canvas |
| Lightroom removes duplicates on its own imports | A double copy of a photo is harmless |
| Nothing else writes to the library during an offload | The rename never replaces anyway |
| Deleting DCIM files on a PC leaves the drone's `MISC` media index usable | The drone may show stale thumbnails until it rebuilds its index; formatting in the drone remains the clean option (§15 Q8) |
| The video root is synced to every PC (OneDrive today), so `<videoRoot>\.uas-sort\` reaches them all and can be pinned "Always keep on this device" | A PC whose video root isn't synced keeps a ledger that only that PC sees. Setup and Settings show the ledger status ("no other PCs' ledgers found"), so the user can tell. The local backup in `%LOCALAPPDATA%\uas-sort\ledger-backup\` is unaffected |

### 1.3 Goals

| # | Goal |
|---|---|
| G1 | **Zero-risk offload.** The offload never writes the card (deletions happen only in the separate Card cleanup, G6), library files are never overwritten, and library content is never read. The app's own ledger files in `<videoRoot>\.uas-sort\` are the one exemption for pre-existing files (§4.3). Every copy is hashed while copying and re-read before it gets its final name. The re-read uses an unbuffered handle; when a buffered fallback is used, that is recorded. Durability rests on `FlushFileBuffers` before the rename, a size check after it, a directory flush on non-NTFS destinations, and safe removal of external drives. It is **not** claimed from the rename's write-through flag. |
| G2 | **Proposals that explain themselves.** Every group boundary states its cause, every photo decision states its reason, and every append states its confidence and reason. |
| G3 | **One-gesture fixes.** Merge, split, move, rename, retarget, include a day, or move the R/G sliders. Everything can be undone, and drafts survive a restart. |
| G4 | **An honest, itemised "safe to format?" verdict about the card that is in the reader right now**: identity re-checked and contents re-listed at verdict time. |
| G5 | **A native Windows 11 app** (WinUI 3, Mica, TitleBar, system dark mode) that starts in under 1 s. |
| G6 | **A card cleanup that deletes only what the user confirmed** (§10.6): oldest first, with the cutoff stated; by default only files with library evidence; not-in-library clips only after an opt-in and a per-row review; each file re-checked (identity, size and mtime, evidence) just before its delete, and recorded in the ledger. |

### 1.4 Non-goals

- Formatting the card, or repairing `.trinf` recordings. Card files are deleted only by the explicit Card cleanup (§10.6), never by the offload.
- Reading, renaming, moving or de-duplicating library files; any access to the Lightroom catalog. The app's own `<videoRoot>\.uas-sort\` ledger folder is not library content (§4.3).
- Copying LRF or SRT files; playback, transcoding or MP4 repair.
- Offloading Autel or other non-DJI cards.
- Tray icon, service, auto-launch, auto-update, telemetry.
- Nominatim or any other online name lookup; offline street or satellite tiles; Store packaging.
- Parallel copies, pausing, resuming a half-copied file, or a crash journal. Temp-then-rename plus a safe re-run covers these cases.
- Editable photo groups or photo folders. Photos always land flat, except sets.

**Later (explicitly deferred, not in the initial version)**
- Map: GeoNames place labels (and the `viewport`/`places` messages), Natural Earth outlines, keyboard forwarding from the map, Shift-drag box select, and a Protomaps offline street map.
- Review: 960 px hover previews (MP4 `covr` / DNG preview ranges), a flight-number column, and a "Recent offloads" list.
- Media: a stale-first-fix check (last-sample comparison for known models), GPS-time capture sources for other DJI models, a USGS toolbar button (USGS stays available as a settings URL), and a maximum group span.
- Deployment: MSIX packaging.

### 1.5 Success criteria

1. **Safety is enforced mechanically.**
   - Whole-type bans on file-system APIs apply outside Platform (§2.4). `tools/build.ps1` builds a probe project containing one call of each banned kind and expects every one of them to raise RS0030.
   - The table-driven `IoGuardPolicy` test passes (§4.3, §13), and the fake-FS hydration tripwire passes over scan, plan and offload.
   - `CardDelete` is allowed only for a file, or an emptied set folder, named in a `ConfirmedCleanupPlan` on a card volume the eraser factory verified from Win32; the fake-FS card-delete tripwire passes over scan, offload and Card cleanup.
   - Windows integration tests prove:
     - the rename never replaces;
     - verification opened an unbuffered handle (or recorded the buffered fallback);
     - the lister opens no files;
     - no pre-existing library file is opened except `.uas-sort\ledger*.jsonl` (read) and the own ledger file (append); this run's own `*.uas-sort.tmp` and just-renamed files are tracked exceptions;
     - the card reader works on files whose ACL denies write access;
     - the card eraser deletes only the files it is given (`DeleteFileW` on `\\?\` paths) and removes only an emptied set folder.
2. **The golden replay passes** (the checked-in fixture and scenarios of §13).
   - Scenario A0 (card = every library video, empty library): clustering at R = 50 mi, G = 1 gives 7 groups.
   - The Council/Anvil group carries an emphasised day-split suggestion (about 34 mi), and applying it gives exactly the user's 8 folders.
   - Scenario D at 50 mi proposes **Append · Medium** with the cross-day hint.
   - Scenarios A and B–E in §13 pass.
3. **The first real card works.**
   - `uas-sort-cli plan` (§4.5) is within 2 edits of the user's checked-in `tests/acceptance/first-card-expected.json`; one edit is one `PlanEdit`.
   - The offload ends with 0 failures and a verdict of Safe or SafeWithAssumptions.
   - A before/after card listing shows no change made by the offload.
4. **The app is responsive.**
   - Each edit or slider step derives in under 50 ms for 500 items, on a background thread, with the UI never blocked. Measured by the Release benchmark in the test suite (§13): a synthetic 500-item `PlanBase`, median of 20 derives.
   - Warm start to first frame is ≤ 1 s (0.37 s measured in the spike's trimmed ReadyToRun build, the baseline the Native AOT build must meet or beat; AOT startup is UNVERIFIED until the stack-proof step, §14 step 1). Measured by `--selftest`, which writes the first-frame time to `selftest-result.json`; `deploy.ps1` runs it twice and checks the second (warm) run (§14 step 13).
   - Scanning and planning a 300-file card on a USB 3 reader takes ≤ 20 s (UNVERIFIED; recorded on the real card in first-card acceptance, §13).
5. **The verdict never overstates safety.** "Safe to format" appears only when:
   - no card file is Unaccounted or AssumedByRule;
   - the card identity is unchanged;
   - the card listing is unchanged since the scan.

   A table-driven test covers every outcome and newness combination, including multi-file units, `seen` records and a card swap.
6. **Card cleanup deletes exactly what was confirmed** (§10.6).
   - On a `%TEMP%` fake card (Windows integration) and, after the build, on the real card (acceptance step 7, §13: one old eligible clip), the card re-listed after cleanup differs from the one re-listed before it by exactly the files reported deleted, and every one of them is named in the confirmed plan.
   - Every deleted file has a `cardDelete` ledger record.
   - Nothing is deleted without a `ConfirmedCleanupPlan`, and never on a browsed folder, a volume that fails the cleanup volume check, or a write-protected card.

---

## 2. Stack & solution layout

### 2.1 Choices (versions as of 2026-09-27, from the NuGet registry queried that day)

| Layer | Choice | Version | Status |
|---|---|---|---|
| SDK / runtime | .NET 11 | SDK `11.0.100-rc.1.26425.128` (go-live), then RC2 (~Oct 13; date UNVERIFIED), then `11.0.100` GA (Nov 10) | Built, tested and published in two spikes (`docs/research/06-modern-stack.md`, `docs/research/08-winui-spike.md`) |
| Language | C# 15, the default for `net11.0` | Unions, `closed` hierarchies, collection-expression arguments; C# 14 `field` keyword and extension members | Compiled in the spike. **Exhaustiveness across assemblies is UNVERIFIED** and gets a test in the stack-proof step (§14 step 1) |
| UI | WinUI 3 from the Windows App SDK 2.5.1 component packages | `Microsoft.WindowsAppSDK.WinUI` 2.3.9, `.Foundation` 2.3.12, `.InteractiveExperiences` 2.1.9 (pinned to avoid NU1603) | Spike: built, ran and published |
| Build tools | `Microsoft.Windows.SDK.BuildTools`; for Native AOT, the MSVC linker from the VS Build Tools 2022 C++ workload (§2.6) | 10.0.28000.2705 | No Visual Studio IDE needed. The C++ workload is a one-time prerequisite: the installed Build Tools have an MSVC 14.44 folder but no `cl.exe`/`link.exe` |
| Web host | `Microsoft.Web.WebView2` | 1.0.4191.47 (Evergreen runtime 153.x already present) | Spike: virtual host, messaging and clicks worked |
| Map | MapLibre GL JS, vendored in the repo | 6.11.2. Streets from OpenFreeMap; satellite from Esri World Imagery; USGS as an alternate URL in settings | Verified in Chromium. **ESM `.mjs` under the WebView2 virtual host is UNVERIFIED** (checked first when the map pane is built, §14 step 11; §9.6). Leaflet 1.9.4 is proven in WebView2 and is the last-resort fallback |
| First-party controls | TitleBar, SelectorBar, **ItemsView** (+ LinedFlowLayout), AutoSuggestBox, DropDownButton + MenuFlyout, InfoBar, Slider, ContentDialog (queued), Expander, ToolTip | Part of WinUI 2.3.9 | TitleBar, ItemsView and Mica verified |
| Toolkit controls | `CommunityToolkit.WinUI.Controls.Sizers` and `.SettingsControls` | 8.3.260402-preview2 (the latest; the only line that works with the lean package set) | Preview. Sizers was never restored in a spike, so it is part of the stack-proof build (§14 step 1). Fallback: 8.2.251219 with the full `Microsoft.WindowsAppSDK` package, or fixed-ratio panes |
| Grid | **No DataGrid.** ItemsView with x:Bind templates | — | §9.3. WinUI.TableView 1.5.0 works on 2.5.1 but isn't used |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 (partial `[ObservableProperty]`) | Verified on C# 15 |
| Still-photo metadata | MetadataExtractor | 2.9.3, **Stream overloads only** (path overloads banned) | Trim behaviour UNVERIFIED, so rooted with `TrimmerRootAssembly`; the stack-proof publish (§14 step 1) makes a MetadataExtractor Stream call, and the full `--selftest` a StillProbe call |
| Time zones | GeoTimeZone + built-in `TimeZoneInfo` (ICU) | 6.1.0 | Verified on Windows 11. Never set `InvariantGlobalization` or `UseNls` |
| Hash | System.IO.Hashing `XxHash128` (copies), `XxHash64` (keys) | **11.0.0-rc.1.26425.128**, then 11.0.0 at GA | 9.2 GB/s |
| JSON | System.Text.Json, source-generated contexts only | Built in | Safe under trimming |
| Clock | `TimeProvider` (built in); `Microsoft.Extensions.TimeProvider.Testing` in tests | 10.10.0 (no 11.x published) | — |
| Analyzers | `Microsoft.CodeAnalysis.BannedApiAnalyzers`; `AnalysisLevel` **pinned** to `11.0-recommended`, falling back to `10.0-recommended` if RC1 rejects it (UNVERIFIED); `TreatWarningsAsErrors` | 5.6.0 | — |
| Tests | `xunit.v3.mtp-v2` on Microsoft.Testing.Platform 2.4.1 | 4.0.1 | Passed 5/5 in the spike |
| Place names | GeoNames extract (CC-BY 4.0), built by `tools/places/build-places.cs` (a .NET file-based app) | Dump of 2026-09-27 | The Python spike (`docs/research/spikes/dotnet-stack/geonames/rg.py`; `docs/research/03-dotnet-libraries.md`) found "Zachar Bay" 0.4 mi and "Anvil Mountain" 0.2 mi from the clips |
| Scripts | PowerShell 7 (`pwsh`) | Latest from winget `Microsoft.PowerShell` (version UNVERIFIED). **Not installed on this PC** (only Windows PowerShell 5.1, which the scripts don't target) | Approved: the user installs it once with winget at implementation start, next to the SDK (§2.6 Prerequisites) |
| Packaging | Unpackaged, self-contained (.NET and Windows App SDK), lean, **Native AOT** (`PublishAot=true`, which trims); a folder plus a Start-menu `.lnk`; `win-x64` only (user decision 2026-09-28) | — | Built and gated from the stack-proof step at the start of the build (§14 step 1). Baseline to meet or beat: the spike's trimmed ReadyToRun x64 build, 87–97 MB with a warm first frame of about 0.37 s. AOT size and startup are UNVERIFIED until the stack-proof step |
| Dependency injection | None: a hand-written composition root | — | YAGNI |

### 2.2 Why these, and what they replace

**.NET 11 rather than .NET 10**
- RC1 is go-live, so production use is allowed.
- Short-term releases now get 24 months, so .NET 11 ends around November 2028 (exact date UNVERIFIED), about when .NET 10 LTS ends (2028-11-14).
- It brings C# 15, and the self-contained app needs no runtime on the PCs.
- Required upgrades, each applied when it ships (§14, Maintenance after the build):
  1. RC2, around Oct 13, because RC1's go-live support ends Oct 13.
  2. GA on Nov 10, which also sets `allowPrerelease:false`.
- Fallback: retarget to `net10.0` and replace `union` and `closed` with abstract records.

**WinUI 3**
- Neither spike found a showstopper; both built, ran and published from the command line.
- There is no DataGrid, but the review lists aren't spreadsheets. Microsoft's guidance for tabular data points to ListView/ItemsView. ItemsView is chosen because it is proven in the spike, takes a template selector, and supports Extended selection.
- WinUI.TableView adds a trim warning (IL2026) and nothing this app needs.

**Map: WebView2 + MapLibre**
- The Windows App SDK MapControl can't colour or label pins, can't draw circles or lines, and needs an Azure Maps key.
- Mapsui.WinUI is unverified on Windows App SDK 2.x.
- Leaflet dates from 2023, but it is proven inside WebView2, so it is the last-resort fallback.

**Packaging.** Unpackaged, as recorded in §1.1 (the reason is certificate trust, not a missing install). Single-file publishing works only after a clean publish and unpacks 88 MB to temp, so it isn't used; the AOT output is still a folder (the native exe beside the Windows App SDK DLLs and the `.pri`/`.xbf` resources).

**Native AOT rather than trimmed + ReadyToRun** (user decision, 2026-09-27)
- It is the best-performing build the stack offers: native code with no JIT at startup, and Core and Review are already `IsAotCompatible`.
- Its only missing piece on this PC was an install (the C++ build tools), and the install rule (§1.1) says to install it rather than design around it.
- The spike's ReadyToRun numbers (x64 87–97 MB, 0.37 s warm first frame) are the baseline; AOT must meet or beat the warm start. That, and whether the WinUI/WebView2/CsWinRT stack publishes under AOT with warnings as errors, is UNVERIFIED until the stack-proof step (§14 step 1).
- If AOT fails a concrete check, a ReadyToRun publish of the same commit is used only to tell whether the failure is AOT-specific, and the result is taken to the user; it is never adopted silently.

### 2.3 Solution layout (in `C:\dev\uas-sort`; not scaffolded yet)

```
uas-sort.slnx  global.json  Directory.Build.props  Directory.Packages.props  BannedSymbols.txt  .editorconfig
src/
  UasSort.Core/       net11.0                      model, media probes, time, geo, library index, planning, editing,
                                                   naming, offload engine, card cleanup, audit, ports, JSON contexts
  UasSort.Platform/   net11.0-windows10.0.26100.0  the ONLY code that touches disk, Win32 or the shell: lister, card reader,
                                                   card eraser, GuardedFileOps/WindowsFileOps, CardSourceValidator helpers, sync-root
                                                   detection, stores, app assets, power request, eject, single-instance
                                                   and offload locks, shell launcher, logging
  UasSort.Review/     net11.0                      view models (MVVM Toolkit), map bridge, collection sync; no WinUI types
  UasSort.App/        net11.0-windows10.0.26100.0  Program.Main, WinUI shell, pages, MapPane (WebView2), MapAssets/,
                                                   places.bin.gz, SelfTest/ (stack-exif.jpg, selftest.dng, ledger-v1.jsonl), composition root
  UasSort.Cli/        net11.0-windows10.0.26100.0  dry-run planner, AssemblyName=uas-sort-cli: `plan` (dry run; writes nothing,
                                                   ever; contract in §4.5)
tests/
  UasSort.Testing/          net11.0  FakeFileSystem (tripwire, faults; asks Core's IoGuardPolicy), SyntheticMp4Builder,
                                     SyntheticDngBuilder, FakeCardWriter (materialises scenarios under %TEMP%\uas-sort-test-*
                                     only), Replay/library-listing.json (checked-in golden fixture, §13), GatedPlanDeriver
  acceptance/first-card-expected.json   the user's expected folder list for the first real card, written before acceptance (§4.5, §13)
  UasSort.Core.Tests/       net11.0
  UasSort.Review.Tests/     net11.0  includes cross-assembly exhaustive-switch tests on Core's closed/union types
  UasSort.Platform.Tests/   net11.0-windows…  Windows integration in %TEMP%, XAML lint, BannedSymbols content test
  UasSort.BannedApi.Probe/  net11.0-windows…  NOT in the solution build: one call per banned kind; built by tools/build.ps1
tools/
  build.ps1  r.sh (optional WSL wrapper)  deploy.ps1  vendor-maplibre.ps1  places/build-places.cs
  fixtures/make-replay-fixture.ps1 (record only; not run during the build)  fixtures/make-selftest-assets.cs (writes App/SelfTest/* from the synthetic builders)
```

### 2.4 Dependency direction and banned APIs

```
App ──► Review ──► Core ◄── Platform ◄── App
Cli ──► Platform, Core          (Cli never references Review or App)
```

- Core has no project references. Its packages are MetadataExtractor, GeoTimeZone and System.IO.Hashing.
- **ICardReader and IFileOps (plus Platform's own stores) are the only ways any code may open a file, ICardEraser is the only way anything on the card is deleted (Card cleanup, §10.6), and each of them asks `IoGuardPolicy` (§4.3) before every open, create, attribute change, delete or rename.**

**Banned in Core, Review, App and Cli** (`BannedSymbols.txt`, whole types where possible):

| Group | Entries |
|---|---|
| File-system types | `T:System.IO.File`, `T:System.IO.Directory`, `T:System.IO.FileInfo`, `T:System.IO.DirectoryInfo`, `T:System.IO.FileSystemInfo`, `T:System.IO.RandomAccess`, `T:System.IO.FileStream`, `T:System.IO.DriveInfo`, `T:System.IO.FileSystemWatcher`, `T:System.IO.Enumeration.FileSystemEnumerable`1`, `T:System.IO.Enumeration.FileSystemEnumerator`1`, `T:System.IO.Compression.ZipFile` |
| Path-taking members | `M:System.IO.Path.GetTempFileName`; the `StreamReader(String…)` and `StreamWriter(String…)` constructors; `M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(…)` overloads; every `ReadMetadata(System.String)` overload of the MetadataExtractor readers; `XDocument.Load/Save(String)` and `XmlDocument.Load/Save(String)` |
| WinRT storage | `T:Windows.Storage.StorageFile`, `T:Windows.Storage.StorageFolder`, `T:Windows.Storage.FileIO`, `T:Windows.Storage.PathIO`, `T:Microsoft.VisualBasic.FileIO.FileSystem` |
| Images from paths | `M:Microsoft.UI.Xaml.Media.Imaging.BitmapImage.#ctor(System.Uri)`, `P:Microsoft.UI.Xaml.Media.Imaging.BitmapImage.UriSource` |
| Processes | every `M:System.Diagnostics.Process.Start(…)` overload (the `IShellLauncher` implementation lives in Platform) |
| Clock | `P:System.DateTime.Now`, `P:System.DateTime.Today`, `P:System.DateTime.UtcNow`, `P:System.DateTimeOffset.Now`, `P:System.DateTimeOffset.UtcNow` (use `TimeProvider`) |

**Platform gets a member-level list** (writes, deletes, moves, `FileMode.Create/Truncate/OpenOrCreate/Append`, `File.SetAttributes/SetLastWriteTime*/SetCreationTime*`, the clock). Every allowed risky call there carries a local `#pragma warning disable RS0030 // IO layer: <why>`, so each one is deliberate and easy to grep. Card cleanup changes nothing in `BannedSymbols.txt`: `File.Delete`, `Directory.Delete` and the rest stay banned outside Platform. The card deletes are `LibraryImport` calls of `DeleteFileW` and `RemoveDirectoryW`, declared only in `WindowsCardEraser` (a Platform source test checks this), and each call site carries the same pragma (`// IO layer: Card cleanup, confirmed plan only`).

**XAML lint** (a Platform.Tests test over `src/UasSort.App/**/*.xaml`) fails on:
- `{Binding`, `DisplayMemberPath`, `TextMemberPath` and `SelectedValuePath`;
- any `Image`/`BitmapImage` `Source`/`UriSource` that isn't `ms-appx:///`.

**Guards on the guards**
- A unit test asserts that `BannedSymbols.txt` still contains every entry above.
- `tools/build.ps1 -CheckBannedApi` builds `UasSort.BannedApi.Probe` and expects exactly one RS0030 per probe call.
- WebView2 is fenced in: `DownloadStarting` is cancelled, and navigation outside the virtual host is cancelled (§9.6).

### 2.5 Build configuration essentials

```jsonc
// global.json (the only one; delete any the xUnit template drops)
{ "sdk": { "version": "11.0.100-rc.1.26425.128", "rollForward": "latestFeature", "allowPrerelease": true },
  "test": { "runner": "Microsoft.Testing.Platform" } }
```

**`Directory.Build.props`**
- `Nullable=enable`, `ImplicitUsings=enable`, `AnalysisLevel=11.0-recommended` (pinned; fallback `10.0-recommended`), `TreatWarningsAsErrors=true`, `ManagePackageVersionsCentrally=true`.
- Core, Review and Platform: `IsTrimmable=true`, `IsAotCompatible=true` (so trim and AOT analyzers run on every build, not only at publish).
- Test projects: `NoWarn` CA1707.

**`Directory.Packages.props`** pins exactly:
- WinUI 2.3.9, Foundation 2.3.12, InteractiveExperiences 2.1.9, WebView2 1.0.4191.47, BuildTools 10.0.28000.2705;
- Sizers and SettingsControls 8.3.260402-preview2, Mvvm 8.4.2;
- MetadataExtractor 2.9.3, GeoTimeZone 6.1.0, System.IO.Hashing 11.0.0-rc.1.26425.128;
- BannedApiAnalyzers 5.6.0, xunit.v3.mtp-v2 4.0.1, TimeProvider.Testing 10.10.0.
- There are no floating versions.

**`UasSort.Platform.csproj`**
- `AllowUnsafeBlocks=true`, which `LibraryImport` needs (without it the build fails with SYSLIB1062).

**`UasSort.App.csproj`**
- `OutputType=WinExe`, `UseWinUI=true`, `WinUISDKReferences=false`, `Platforms=x64`, `RuntimeIdentifiers=win-x64` (x64 only, user decision 2026-09-28).
- `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, `SelfContained=true`.
- **`EnableMsixTooling=true`**. It is still required when unpackaged, or the `.pri`/`.xbf` files are missing and startup crashes with 0xC000027B.
- `TargetPlatformMinVersion=10.0.26100.0`.
- `DefineConstants` gets `DISABLE_XAML_GENERATED_MAIN` (custom `Program.Main`, §4.4).
- Release builds: **`PublishAot=true`** (Native AOT; it implies trimming, so there is no separate `PublishTrimmed` and no `PublishReadyToRun`), `<TrimmerRootAssembly Include="MetadataExtractor;XmpCore"/>`. There is no opt-in flag: every Release publish, the stack-proof publish (§14 step 1) and `deploy.ps1` included, is AOT.
  - If the stack-proof publish shows IL2104/IL3053-class warnings **only** from those rooted assemblies, add a targeted `<NoWarn>` for exactly those codes with a comment. Any other trim or AOT warning stays an error.
  - ReadyToRun (`PublishReadyToRun=true` without `PublishAot`) is not a build configuration of the app. It is used only by hand, to diagnose an AOT failure, and that failure is raised with the user (§1.1).
- Content with `CopyToOutputDirectory=PreserveNewest`: `MapAssets\**` and `places.bin.gz`. `SelfTest\*` are embedded resources.

### 2.6 Commands

**Prerequisites (install once).** Machine-wide, on the development PC (approved 2026-09-27). The build (§14) starts only once they are installed; the plan's Task 01.0 installs and verifies them (winget, Git for Windows, the rows below, Windows SDK 10.0.26100 and the WebView2 runtime) when the user says to start development. Under the install rule (§1.1), a missing tool is installed, never designed around.

| What | Command | Notes |
|---|---|---|
| .NET 11 SDK | `winget install Microsoft.DotNet.SDK.Preview` | 11.0.100-rc.1 now; switch to the GA SDK on Nov 10 (§14, Maintenance after the build) |
| PowerShell 7 | `winget install Microsoft.PowerShell` | Every script runs via `pwsh` (§15 Q6) |
| C++ build tools for Native AOT | see below | The installed VS Build Tools 2022 has an MSVC 14.44 folder but no `cl.exe`/`link.exe`; Windows SDK 10.0.26100 is present |
| Optional editor | `winget install Microsoft.VisualStudioCode` plus the C# Dev Kit extension; or Visual Studio 2026 Insiders for the XAML designer and Hot Reload | The default is the dotnet CLI plus VS Code |

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vs_installer.exe" modify --installPath "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools" --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --passive
```

Runtime PCs need nothing: the app is self-contained, and WebView2 ships with Windows 11.

```powershell
dotnet build uas-sort.slnx                                  # first restore ~8-13 min (~1.5-2 GB NuGet)
dotnet test --solution uas-sort.slnx                        # MTP runner via global.json
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -CheckBannedApi
dotnet run --project src/UasSort.Cli -- plan --card E:\ --json     # = uas-sort-cli plan … (§4.5)
dotnet publish src/UasSort.App -c Release -r win-x64         # Native AOT (PublishAot in the csproj); needs the C++ build tools
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1 -Version 0.1.0   # AOT publish for win-x64, selftest gate, copy, .lnk, keep 2 versions
dotnet build-server shutdown                                # optional: releases the compiler server's file locks
```

These commands run natively on Windows from the repo root `C:\dev\uas-sort`, in PowerShell 7 or Claude Code's Bash tool (Git Bash on Windows); that is how development runs (user decision, 2026-09-28). Optional, only for someone driving the build from WSL: `tools/r.sh` wraps `"/mnt/c/Program Files/dotnet/dotnet.exe"` and `pwsh.exe`, with the working directory set to `/mnt/c/dev/uas-sort`. Incremental builds take about 15 s. Pass environment variables through `WSLENV`.

### 2.7 Pitfalls from the spikes and the review, as implementation rules

1. **Don't keep the `dotnet new winui` template defaults.**
   - It builds a packaged app, uses floating `Version="*"` references and the 53 MB `...BuildTools.WinApp`, writes the wrong minimum platform, and names the namespace `X_App`.
   - Its view model uses the old field-style `[ObservableProperty]`.
2. **Reference the component packages, not the full `Microsoft.WindowsAppSDK`.** The full package adds 58 MB of onnxruntime and DirectML.
3. **All bindings are `x:Bind`.**
   - No property-path features: `DisplayMemberPath`, `TextMemberPath`, `SelectedValuePath`. The XAML lint enforces this.
   - The window hosts a `Frame`, and every view is a `Page` or `UserControl`, because x:Bind in a `Window` initialises only on `Activated`.
   - Never call `Bindings.Update()` from `CompositionTarget.Rendering`; it hung the UI thread in the spike.
4. **Every `ItemsSource` is an `ObservableCollection<T>` or `List<T>` of view-model classes.** Never an `ImmutableArray`, a raw record or a union, because those were untested under trimming with CsWinRT. Every VM shown as text overrides `ToString()` with its display text.
5. **Size windows in physical pixels.** `AppWindow.Resize` takes physical pixels, so multiply by `XamlRoot.RasterizationScale`.
6. **WebView2 order and data folder.**
   - Register `WebMessageReceived` **before** `Navigate`, and cancel `DownloadStarting`.
   - The page's first lines forward `error` and `unhandledrejection` to the host.
   - Set the user data folder explicitly; never put it next to the exe.
7. **Map JSON.** Source-generated `[JsonPolymorphic]` needs `AllowOutOfOrderMetadataProperties = true`, because map.js sends `v` before `type`. Unknown types are logged, not thrown.
8. **Clean up after the xUnit template.** Delete the `global.json` it drops, retarget its `-f` by hand, and disable CA1707 in test projects.
9. **Never override `BaseIntermediateOutputPath` on the command line.** It causes CS0579 duplicate-attribute errors.
10. **Slider pointer release.** `Slider` has no public drag-completed event (§9.7).
11. **ContentDialogs.** Only one may be open at a time, so every dialog goes through `IDialogService`, which queues them.

---

## 3. Domain model (`UasSort.Core`)

```csharp
// ── primitives
public readonly record struct ItemId(string CardRelPath);      // "DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"; a set = "DCIM/PANORAMA/001_0087"
public readonly record struct GeoPoint(double Lat, double Lon); // WGS84 degrees; JSON as [lon,lat] via GeoPointJsonConverter
public readonly record struct Distance(double Meters)          // Earth radius 6,371,008.8 m, shared with map.js
{ public double Miles => Meters / 1609.344; public static Distance FromMiles(double mi) => new(mi * 1609.344); }
public readonly record struct ByteRange(long Offset, int Length);
public enum ItemKind { Video, Photo, Set }   public enum SetKind { Panorama, Hyperlapse }

// ── card
public enum EntryClass { Video, Photo, PhotoTwin, SetMember, Skip, Unknown }
public sealed record CardEntry(string RelPath, long Size, DateTime MtimeUtc, DateTime CreationUtc, DateTime LastAccessUtc,
                               uint RawAttributes, EntryClass Class, string? Rule);
public sealed record CardIdentity(uint VolumeSerial, string? Label, string FileSystem, long TotalBytes);
public closed record class MediaUnit(ItemId Id);
public sealed record class VideoUnit(ItemId Id, CardEntry Mp4, bool HasTrinf) : MediaUnit(Id);
public sealed record class PhotoUnit(ItemId Id, CardEntry Primary, CardEntry? JpgTwin) : MediaUnit(Id);
public sealed record class SetUnit(ItemId Id, SetKind Kind, string SetName, ImmutableArray<CardEntry> Members) : MediaUnit(Id);
public sealed record CardSource(string Root /* canonical, anchored at the folder holding DCIM */, CardIdentity? Identity,
                                bool IsBrowsedFolder /* set only by CardSourceValidator: detected == null (§4.1) */,
                                bool IsWriteProtected /* detected?.IsReadOnlyVolume */)   // UI hints only: the eraser factory
                                                                                          // re-derives every fact from Win32
{ public string DraftKey => Identity is { } i ? $"vol-{i.VolumeSerial:X8}"
                                              : $"dir-{XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(Root.ToLowerInvariant())):x16}"; }
public sealed record CardInventory(CardSource Source, DateTime ListedUtc, string InventoryHash, ImmutableArray<CardEntry> Entries,
                                   ImmutableArray<MediaUnit> Units, string? CameraModel, ImmutableArray<ScanWarning> Warnings);
public sealed record ScanWarning(string Code, string Message, string? RelPath, bool ForcesNotSafe);

// ── probes (C# 15 union: exhaustive, no default arm)
public enum GpsSource { DjmdModelTable, DjmdGenericSearch, MdatHeadFallback, Exif }
public sealed record GpsFix(GeoPoint Point, double? AltM, int Sample, GpsSource Source, string? FieldPath);
public enum NoFixReason { NotDji, NoDjmdTrack, AllProbedSamplesZero, Unparseable, NoGpsTag, GenericHitImplausible }
public sealed record NoFix(NoFixReason Reason);
public union GpsProbe(GpsFix, NoFix);
public sealed record Mp4Info(DateTime? MvhdUtc, bool HasMoov, GpsProbe First, GpsFix? LastSameField /* generic hits only */,
                             string? Protocol, DateTime? SessionUtc, string? DroneSerial, ByteRange? Thumb,
                             TimeSpan? Duration /* mvhd duration / timescale; null when there is no moov (§6.3) */);
public readonly record struct SessionKey(string? DroneSerial, DateTime SessionUtc)   // SessionUtc = mvhd − uptime µs (§6.3 step 6)
{ public bool SameSession(SessionKey o) => DroneSerial == o.DroneSerial
                                           && Math.Abs((SessionUtc - o.SessionUtc).TotalSeconds) <= 2; }  // never compare with ==
public sealed record StillInfo(DateTime? DtoNaive, TimeSpan? OffsetTime, GpsProbe Gps, string? Model, ByteRange? Thumb);
public sealed record RawItem(MediaUnit Unit, ItemKind Kind, string Name, long Bytes, DateTime CardMtimeUtc,
                             DateTime? DroneStamp /* filename or EXIF DTO, naive */, Mp4Info? Mp4, StillInfo? Still, string? ProbeError);

// ── clock
public enum ClockMode { SiteLocal, Zone, NearestSample, Setting }    // §6.1; SiteLocal = the RC 2 follows local time at each site
public enum StoredClockMode { SiteLocal, Zone }                       // what a successful run saves (Settings.DroneClockMode)
public sealed record ClockSample(DateTime DroneStamp, DateTime MvhdUtc, TimeSpan Offset, string? SiteZoneId /* sample's own GPS; null = no GPS or Etc/* */);
public sealed record ClockModel(ClockMode Mode, string? ZoneId, ImmutableArray<ClockSample> Samples, TimeSpan? Modal,
                                StoredClockMode SettingMode, string SettingZoneId)
{ // siteZoneId: the zone that SiteLocal (or Setting with SettingMode SiteLocal) converts through, chosen by the caller (§6.1);
  // ignored by the other modes
  public (DateTime Utc, TimeSource Src)? ToUtc(DateTime droneStamp, string? siteZoneId) => /* §6.1 */ default;
  public TimeSpan OffsetAt(DateTime droneStamp, string? siteZoneId) => /* the drone clock's UTC offset at that stamp (§6.5) */ default; }
public sealed record ClockSummary(ClockMode Mode, string? ZoneId, int SampleCount, string Headline,
                                  int MismatchItems, ImmutableArray<string> MismatchSiteZones /* IANA IDs, for the InfoBar (§9.2) */,
                                  ImmutableArray<ClockChange> Changes /* NearestSample only; §6.1 "Clock change" */);
public sealed record ClockChange(DateTime AtUtc, TimeSpan From, TimeSpan To);

// ── normalized item
public enum TimeSource { Mvhd, ExifWithOffset, DroneClockSiteLocal, DroneClockZone, DroneClockSample, DroneClockSetting, Mtime }
public enum TzSource { Gps, SameSession, NearestGpsWithin12h, NearestLandGpsOnCard, GeoNamesTz, PcZone }
[Flags] public enum ItemFlags { None = 0, NoGps = 1, Truncated = 2, ClockNotSet = 4, CheckDate = 8, TzFallback = 16,
                               ProbeFailed = 32, GpsGuessed = 64, ClockFromSetting = 128, ClockMismatch = 256 }
public sealed record ItemTime(DateTime CaptureUtc, TimeSource Source, string TzId, TzSource TzSource, DateOnly LocalDate, DateTime LocalTime);
public enum NewReason { NoMatch, SeenNotCopied, AfterWatermark, NearWatermark, DayHasNewVideos }
public enum Evidence { LibraryNameSize, LedgerVerified, LedgerNameSize }
public enum DecisionKind { AssumedImported, Dismissed }
public closed record class Newness;
public sealed record class IsNew(NewReason Why, DateTime? SeenUtc) : Newness;
public sealed record class Imported(Evidence By, LibraryFolderRef? Folder, string Why) : Newness;
public sealed record class Decided(DecisionKind Kind, DateTime AtUtc, string Machine) : Newness;      // ConfirmedByYou; undoable
public sealed record class ProbablyImported(string Why) : Newness;
public sealed record class Conflict(string ExistingPath, long ExistingSize) : Newness;
public sealed record Item(RawItem Raw, ItemTime Time, GpsFix? Gps, SessionKey? Session, ItemFlags Flags, Newness Newness);

// ── library (listings + ledger only)
public sealed record LibraryFolderRef(string FullPath, DateOnly NameDate, string Description);
public enum LocationSource { Ledger, CardLeftovers, Unknown }
public sealed record LibraryFolder(LibraryFolderRef Ref, ImmutableArray<DateTime> MemberStartsUtc, string? TzId,
                                   GeoPoint? Centroid, LocationSource Loc)
{ public ImmutableHashSet<DateOnly> DaysIn(string tzId) => default!; }   // local days of members in F's zone (else the given zone)

// ── plan
public sealed record Tuning(double RadiusMiles = 50, int GapDays = 1);
public readonly record struct GroupId(ItemId Anchor);           // earliest video; stable across re-derivations
public enum BoundaryCause { DayGap, Distance, LibraryFolder, UserSplit }
public sealed record Boundary(GroupId Left, GroupId Right, BoundaryCause Cause, Distance? Jump, TimeSpan Gap, int DayGap);
public sealed record DaySplit(ItemId FirstOfDay, DateOnly From, DateOnly To, Distance? Apart, TimeSpan Gap, bool Emphasised);
public enum Confidence { High, Medium }
public closed record class GroupTarget;
public sealed record class AlreadyImported(LibraryFolderRef Folder) : GroupTarget;
public sealed record class NothingToCopy(string Summary) : GroupTarget;   // no wall, nothing New/ticked-Conflict: "nothing to copy: 3 conflicts, 1 dismissed"; never folded
public sealed record class NewFolder(string RelPath) : GroupTarget;   // @"2026\2026-09\2026-09-27 Zachar Bay"
public sealed record class Append(LibraryFolderRef Folder, Confidence Confidence, string Why, CrossDayHint? Hint) : GroupTarget;
public sealed record class SkipGroup() : GroupTarget;
public sealed record CrossDayHint(string Text, ImmutableArray<PlanEdit> Fix);       // "[New folder instead]"; Fix = SplitBefore(split point, §8.5) or Retarget(NewFolderTarget)
public enum DescSource { ExistingFolder, Ledger, Feature, Place, Town, User, None }
public sealed record Suggestion(string Text, DescSource Source, Distance? Away, DateOnly? ForDay);
public sealed record PinState(bool MembershipChanged, int PinnedCount, int NowCount);
public sealed record VideoGroup(GroupId Id, ImmutableArray<ItemId> Videos, GeoPoint? Centroid, Distance Spread,
    DateOnly Start, DateOnly End, LibraryFolderRef? Wall, GroupTarget Target, PinState? TargetPin,
    string Description, DescSource DescSource, bool DescriptionEditable, PinState? NamePin,
    ImmutableArray<Suggestion> Suggestions, ImmutableArray<DaySplit> DaySplits, ImmutableArray<string> Hints,
    bool Foldable, int ColorIndex);
public enum SetResolution { Plain, Resume, DateSuffixed, Imported }
public sealed record SetPlacement(ItemId Set, string FolderName, SetResolution Resolution, ImmutableArray<string> MembersToCopy);
public sealed record PhotoDay(DateOnly Date, string TzId, ImmutableArray<ItemId> Items, string Reason);
public enum IssueSeverity { Blocking, Warning, Info }
public enum IssueCode {                       // the closed catalogue of §9.10; tests assert on codes, never on message text
  // Planner.Derive
  EmptyFolderName, TempPathTooLong, MediumAppend, EmphasisedDaySplit, PinMembershipChanged, ConflictingPins, SharedTarget,
  FolderExistsAppending, NewBeforeWallFolder, CheckDate, ClockNotSet, ClockMismatch, RootMissing, RootsUnconfirmed,
  LedgerParseIssue, LedgerCloudOnly, LedgerUnwritable, LedgerNotPinned, LedgerNoHistory,
  // PlanSession.Resume and the Settings page
  StaleEditsDropped, NoHistoryInNewRoot,
  // Preflight.Check
  CardIdentityChanged, CardUnreadable, OffloadLockHeld, AppendTargetGone, LowDiskSpace, DuplicateDestination, LedgerUnlistable,
  StaleTempFiles, DestinationAlreadyThere, AssumptionsTicked, ProbablyImportedLeftOut, NewItemsUnticked, UnfinishedRecordings,
  ConflictsLeftOut }
public sealed record Issue(IssueSeverity Severity, IssueCode Code, string Message, ItemId? Anchor,
                           ImmutableArray<QuickFix> QuickFixes, bool RequiresAckAtPreflight);
public sealed record QuickFix(string Label, ImmutableArray<PlanEdit> Edits);     // applied as ONE undo entry; Edits empty = a UI action (§9.10)
public sealed record SessionFlags(bool LedgerIssuesAccepted);                     // per session, never persisted in drafts
public sealed record PlanBase(ScanResult Scan, ImmutableArray<Item> Items, ImmutableArray<PhotoDay> PhotoDays,
                              ImmutableDictionary<ItemId, SetPlacement> Sets, ClockSummary Clock, DateTime? WatermarkUtc); // tuning-independent
public sealed record Plan(int Revision, PlanBase Base, Tuning Tuning, ImmutableArray<VideoGroup> Groups,
                          ImmutableArray<Boundary> Boundaries, ImmutableHashSet<ItemId> Included, ImmutableArray<Issue> Issues);

// ── edits (item-anchored, so they replay onto any clustering and onto a fresh scan); serialised in drafts
[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")] /* + [JsonDerivedType] per case */
public closed record class PlanEdit;
public sealed record class Merge(ItemId InA, ItemId InB) : PlanEdit;
public sealed record class SplitBefore(ItemId First) : PlanEdit;
public sealed record class MoveToNewGroup(ImmutableArray<ItemId> Items) : PlanEdit;
public sealed record class MoveToGroup(ImmutableArray<ItemId> Items, ItemId InTarget) : PlanEdit;
public sealed record class Rename(ItemId InGroup, string? Description /* null = back to suggestion */,
                                  ImmutableArray<ItemId> PinnedMembers) : PlanEdit;
public sealed record class Retarget(ItemId InGroup, TargetChoice Choice, bool ConfirmedBeforeFolderDate,
                                    ImmutableArray<ItemId> PinnedMembers) : PlanEdit;
public sealed record class SetIncluded(ImmutableArray<ItemId> Items, bool Included) : PlanEdit;
public sealed record class SetDayIncluded(DateOnly Day, bool Included) : PlanEdit;
[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
public closed record class TargetChoice;
public sealed record class AutoTarget() : TargetChoice;
public sealed record class NewFolderTarget() : TargetChoice;
public sealed record class AppendTo(string FolderFullPath) : TargetChoice;
public sealed record class SkipTarget() : TargetChoice;
public enum RejectReason { MergeAcrossLibraryFolders, MoveImportedItem, SplitAtGroupStart, RenameExistingFolder,
                           RetargetOutsideVideoRoot, RetargetIntoReservedFolder /* .uas-sort subtree, photo root, previousPhotoRoots */,
                           RetargetLaterDatedFolderUnconfirmed, ItemsNotFound }
public sealed record Applied(Plan Plan);   public sealed record Rejected(RejectReason Reason, string Message);
public union EditResult(Applied, Rejected);

// ── offload
public enum DestRoot { Video, Photo }
public sealed record CopyJob(ItemId Item, string CardRelPath, long Size, DateTime CardMtimeUtc, DateTime CardCreationUtc,
                             string DestPath, DestRoot Root, GroupId? Group, bool CreatesFolder);
public enum VerifyMode { Unbuffered, Cached }
public enum CopyPhase { CardCheck, Stat, CreateTemp, Copy, Flush, Verify, Finalize, Rename, Confirm, Ledger }
public sealed record HashMatch(VerifyMode Mode);   public sealed record HashMismatch(UInt128 Got, VerifyMode Mode);
public union VerifyResult(HashMatch, HashMismatch);
public sealed record Renamed; public sealed record TargetExists;  public union RenameResult(Renamed, TargetExists);
public closed record class CopyOutcome(CopyJob Job);
public sealed record class Verified(CopyJob Job, UInt128 Hash, VerifyMode Mode) : CopyOutcome(Job);
public sealed record class AlreadyThere(CopyJob Job) : CopyOutcome(Job);
public sealed record class ConflictAtRename(CopyJob Job) : CopyOutcome(Job);
public sealed record class ChangedOnCard(CopyJob Job, long NowSize, DateTime NowMtimeUtc) : CopyOutcome(Job);
public sealed record class CardSwapped(CopyJob Job, CardIdentity Now) : CopyOutcome(Job);
public sealed record class Failed(CopyJob Job, CopyPhase Phase, string Error) : CopyOutcome(Job);
public sealed record class Cancelled(CopyJob Job) : CopyOutcome(Job);
public sealed record class NotStarted(CopyJob Job) : CopyOutcome(Job);
public sealed record OffloadBatch(string RunId, CardIdentity Card, ImmutableArray<CopyJob> Jobs,
                                  ImmutableArray<ItemId> SeenIfNotCopied, ImmutableArray<FolderPlan> Folders);
public sealed record FolderPlan(GroupId Group, string FullPath, bool Create, string Description, GeoPoint? Centroid,
                                DateOnly Start, DateOnly End, string TzId);
public enum StopReason { Cancelled, CardSwapped, CardRemoved, DestinationFull, DestinationLost, LedgerWriteFailed, InternalSafetyStop }
public sealed record OffloadResult(string RunId, ImmutableArray<CopyOutcome> Outcomes, StopReason? Stop /* null = ran to the end; §10.3 */,
                                   DateTime StartUtc, DateTime EndUtc, ImmutableArray<string> VolumesNeedingSafeRemoval);

// ── audit (per card FILE; a unit takes the worst of its files)
public enum AuditCategory { VerifiedThisRun, InLedger, ConfirmedByYou, NameSizeMatch, SkippedByRule, AssumedByRule, Unaccounted } // ascending badness
public enum VerdictLevel { Safe, SafeWithAssumptions, NotSafe }
public sealed record AuditLine(string CardRelPath, long Size, AuditCategory Category, string Detail);
public sealed record UnitAudit(ItemId Unit, AuditCategory Worst, ImmutableArray<AuditLine> Lines);
public sealed record FormatVerdict(VerdictLevel Level, CardIdentity Card, string Headline,
                                   ImmutableDictionary<AuditCategory, int> Counts, int NameSizeOnly, int CachedVerifies,
                                   ImmutableArray<UnitAudit> Units, ImmutableArray<string> CardChanges, string? SafeRemovalNote);

// ── card cleanup (§10.6; added 2026-09-27)
public enum CleanupMode { BeforeDate, FreeSpace }
public enum FreeSpaceKind { HaveFree /* default: target = Bytes */, FreeUp /* target = free + Bytes */ }
public sealed record FreeSpaceGoal(FreeSpaceKind Kind, long Bytes /* decimal GB × 10^9 */)
{ public long TargetFreeBytes(CardSpace s) => Kind == FreeSpaceKind.FreeUp ? s.FreeBytes + Bytes : Math.Min(Bytes, s.TotalBytes); }
public sealed record CleanupRequest(CleanupMode Mode,
                                    DateOnly? Before /* BeforeDate: site-local day, kept; = DateOnly.FromDateTime(picker.Date.Value.DateTime) */,
                                    FreeSpaceGoal? Goal /* FreeSpace */, bool IncludeNotInLibrary);
public enum CleanupEligibility { Evidence, NotInLibrary, Never }       // ascending strictness; a unit takes the worst of its files
public enum EvidenceSource { Listed /* a fresh library listing holds it */, HistoryOnly /* photos only: a verified ledger record */ }
public enum NotInLibraryReason { New, ProbablyImported, Conflict, Unfinished, Dismissed, RecordedAsImported, NoLongerInLibrary }
public sealed record CardSpace(long FreeBytes, long TotalBytes, int ClusterBytes);   // GetDiskFreeSpaceExW + GetDiskFreeSpaceW
public sealed record FileProof(string CardRelPath, FileKey Key, AuditCategory Category,
                               string? ListedFolder /* folder whose FRESH listing (§10.6 Preparation 3) held (NormName, size) */,
                               bool LedgerVerified /* a `file` record with verify unbuffered|cached */,
                               string? DecisionId /* ConfirmedByYou: shown in the reason, never evidence */);   // what the executor re-checks
public sealed record CleanupCandidate(ItemId Unit, ImmutableArray<CardEntry> Files /* incl. twin and companions, in delete order */,
    long AllocatedBytes /* Σ size rounded up to ClusterBytes */, DateTime CaptureUtc, DateOnly LocalDate, string TzId,
    CleanupEligibility Eligibility, AuditCategory Evidence /* worst category of the primary files */, string Reason,
    TimeSpan? Duration /* videos: Mp4Info.Duration */, GeoPoint? Location, string? PlaceLabel /* "near Anvil Mountain · 0.2 mi" */,
    ItemKind Kind, DateTime LocalTime, NotInLibraryReason? NotInLibrary, string? SetFolder /* card rel dir, removed once empty */,
    SessionKey? Session, ImmutableArray<FileProof> Proofs /* one per primary file */,
    EvidenceSource? Source /* Evidence units */, bool TickedForOffload /* in Plan.Included and not copied: row starts on Keep */,
    ImmutableArray<CardEntry> NeverCopied /* companions and uncopied JPG twins, for the summary line */);
public sealed record CleanupKept(ItemId? Unit, ImmutableArray<string> CardRelPaths, long Bytes, DateTime? CaptureUtc, string Reason);
public sealed record CleanupCutoff(DateOnly? BeforeDate, DateTime? LastCaptureUtc, DateTime? LastLocalTime, string? TzId,
                                   int FilesDeletedOnCutoffDay, int FilesOnCutoffDay, DateTime? FlightContinuesLocal);
public sealed record FreeSpaceShortfall(long FreeableBytes /* Σ allocated of every deletable unit */, long HeldByNotInLibrary, long HeldByNever);
public sealed record CleanupInputs(CardInventory Inventory, Plan Plan, FormatVerdict Audit, OffloadResult? Offload, CardSpace Space,
                                   LibraryListings FreshListings, LedgerSnapshot FreshLedger /* §10.6 Preparation 3 */,
                                   VolumeInfo Volume, IPlaceIndex? Places, Settings Settings);
public sealed record CleanupRows(ImmutableHashSet<ItemId> Keep, ImmutableHashSet<ItemId> Delete /* the rest in range are undecided */);
public sealed class CleanupPlan {   // a class, not a record: no `with`; the ctor is internal and only CleanupPlanner.Build calls it
  internal CleanupPlan(/* every property below */) { }
  public string PlanId { get; }   public CardIdentity Card { get; }   public string CardRoot { get; }
  public CleanupRequest Request { get; }   public CardSpace SpaceBefore { get; }
  public ImmutableArray<CleanupCandidate> Delete { get; }                 // oldest first
  public ImmutableArray<CleanupCandidate> NotInLibraryInScope { get; }    // the review list
  public CleanupRows Rows { get; }   public ImmutableHashSet<ItemId> Undecided { get; }   // rows that joined later, not yet set
  public ImmutableArray<CleanupKept> NotDeletable { get; }                // the "Kept" list
  public CleanupCutoff Cutoff { get; }   public int FileCount { get; }   public long AllocatedBytes { get; }
  public long ExpectedFreeAfter { get; }   public FreeSpaceShortfall? Shortfall { get; }
  public string Fingerprint { get; }                                      // for binding the checkboxes; Confirm recomputes it
  public ConfirmedCleanupPlan Confirm(CleanupAck ack, TimeProvider clock) => /* the only factory; checks in §10.6 */ default!; }
public sealed record CleanupAck(string PlanFingerprint, bool CantBeRecovered, bool IncludesNotInLibrary,
                                ImmutableHashSet<ItemId> NotInLibraryDelete /* review rows left on Delete */);
public sealed class ConfirmedCleanupPlan {    // a class, not a record, so `with` can't copy it; the ctor is internal to Core
  internal ConfirmedCleanupPlan(CleanupPlan plan, Guid token, DateTime confirmedUtc) { /* derives the sets below */ }
  public CleanupPlan Plan { get; }   public Guid Token { get; }   public DateTime ConfirmedUtc { get; }
  public IReadOnlySet<string> FilePaths { get; }              // canonical card paths the guard allows for CardDelete
  public IReadOnlySet<string> SetFolders { get; }             // canonical set folders: RemoveDirectory only, once empty
  public IReadOnlySet<ItemId> NotInLibraryConfirmed { get; } } // the per-unit confirmation tokens of NotInLibrary units
// outcome per unit; the cases that CopyOutcome also has are prefixed "Cleanup" to keep the names apart
public closed record class CleanupOutcome(ItemId Unit);
public sealed record class Deleted(ItemId Unit, int Files, long Bytes, bool SetFolderRemoved) : CleanupOutcome(Unit);
public sealed record class SkippedChanged(ItemId Unit, string CardRelPath, long? NowSize /* null = gone */, DateTime? NowMtimeUtc) : CleanupOutcome(Unit);
public sealed record class SkippedEvidenceGone(ItemId Unit, string CardRelPath, string Why) : CleanupOutcome(Unit);
public sealed record class PartiallyDeleted(ItemId Unit, ImmutableArray<string> DeletedPaths, ImmutableArray<string> StillOnCard, string Why) : CleanupOutcome(Unit);
public sealed record class CleanupFailed(ItemId Unit, string CardRelPath, int Win32Error, string Error) : CleanupOutcome(Unit);   // nothing of the unit deleted
public sealed record class CleanupNotStarted(ItemId Unit) : CleanupOutcome(Unit);
public sealed record class CleanupCardSwapped(ItemId Unit, CardIdentity Now) : CleanupOutcome(Unit);
public enum CleanupStop { OffloadLockHeld, LedgerUnavailable, Cancelled, CardSwapped, CardRemoved, WriteProtected, LedgerWriteFailed,
                          InternalSafetyStop }
public sealed record CleanupEnvironment(CardSource Source, CardIdentity Pinned, ICardReader Reader, IThumbnailSource Thumbnails,
    ICardEraserFactory Erasers, IDirectoryLister Lister, ILedgerStore Ledger, IOffloadLock Lock, IPowerRequest Power,
    TimeProvider Clock, Settings Settings);                               // everything CleanupExecutor.RunAsync needs (§10.6)
public sealed record CleanupResult(string RunId, ConfirmedCleanupPlan Plan, ImmutableArray<CleanupOutcome> Outcomes,
    CleanupStop? Stop /* null = ran to the end */, CardSpace SpaceAfter /* re-read */,
    ImmutableArray<string> StillListed /* deleted, yet present at the re-list (another program held it open) */,
    DateTime StartUtc, DateTime EndUtc);
```

**Supporting types** (all Core)

```csharp
public sealed record ScanResult(CardInventory Inventory, ImmutableArray<RawItem> Raw, LibraryIndex Library,
                                LedgerSnapshot Ledger, ClockModel Clock, ImmutableArray<ScanWarning> Warnings, Settings Settings);
public sealed record GroupDraft(GroupId Id, ImmutableArray<Item> Videos, GeoPoint? Centroid, DateOnly Start, DateOnly End,
                                LibraryFolderRef? Wall, GroupId? UserSplitNeighbour);
public sealed record ClusterResult(ImmutableArray<GroupDraft> Groups, ImmutableArray<Boundary> Boundaries);
public readonly record struct FileKey(string NormName, long Size);
public sealed record LedgerSnapshot(
    ImmutableDictionary<FileKey, LedgerFile> Files,           // latest per key; LedgerFile.Verify ∈ unbuffered|cached|nameSize
    ImmutableDictionary<string, ImmutableArray<LedgerSet>> SetsByName,
    ImmutableDictionary<FileKey, LedgerDecision> Decisions,   // after applying revoke records; a set has one per member (§7.3)
    ImmutableDictionary<FileKey, DateTime> Seen,              // a set has one per member (§7.3)
    ImmutableDictionary<string, LedgerFolder> Folders,        // by full path (case-insensitive)
    ImmutableArray<LedgerRun> Runs,
    ImmutableArray<LedgerCardDelete> CardDeletes,             // Card cleanup's audit trail (§10.6); informational, read by no rule
    ImmutableArray<LedgerParseIssue> ParseIssues,             // file, line, reason; a torn final line, or a line named by a later
                                                              // `torn` record of the same machine (§11), is not an issue
    ImmutableArray<string> SourceFiles, LedgerFolderStatus Status);
public sealed record Draft(int V, string CardKey, string InventoryHash, DateTime SavedUtc, Tuning Tuning, ImmutableArray<PlanEdit> Edits);
public sealed record Settings(int Schema, string VideoRoot, string PhotoRoot, ImmutableArray<string> PreviousPhotoRoots,
                              double RadiusMiles, int GapDays, StoredClockMode DroneClockMode /* last learned; default Zone */,
                              string DroneClockZone /* last learned zone; default America/New_York */, bool CopyJpgTwin,
                              MapSettings Map, LayoutSettings Layout, bool RootsConfirmed);
                              // deliberately NO ledger-folder property: STJ would serialise a computed getter as "ledgerDir"
public static class LedgerPaths                                      // the ledger folder is DERIVED, never stored or configurable (§11)
{ public const string FolderName = ".uas-sort";                      // excluded from every library listing (§7.1)
  public const string FilePattern = "ledger*.jsonl";                 // read-only union, top level only
  public static string For(string videoRoot) => Path.Join(videoRoot, FolderName);
  public static string OwnFile(string videoRoot, string machine) => Path.Join(For(videoRoot), $"ledger-{machine}.jsonl");
  public static string BackupDir(string appDataDir, string canonicalVideoRoot)      // local only; one subfolder per video root
    => Path.Join(appDataDir, "ledger-backup",
                 XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(canonicalVideoRoot.ToLowerInvariant())).ToString("x16")); }
```

**Supporting types (2)** (Core unless marked; every signature elided elsewhere is spelled out here)

```csharp
// ── card detection and source validation
public sealed record CardCandidate(VolumeInfo Volume, bool IsDjiCard, string? NotCardReason /* "not a DJI card" */, int MediaCount);
public sealed record SourceOk(CardSource Source);   public sealed record SourceRefused(string Reason);
public union CardSourceCheck(SourceOk, SourceRefused);
public interface IPathFacts {                                    // Platform: canonical paths and sync roots (§4.3 steps 2–3)
  string Canonical(string path);                                  // GetFullPath → handle with BACKUP_SEMANTICS, no data access → GetFinalPathNameByHandleW
  bool InSyncRoot(string canonicalPath);                          // CfGetSyncRootInfoByPath succeeds
  IReadOnlyList<string> SyncRoots(); }                            // OneDrive UserFolder values + HKLM SyncRootManager roots
public interface ICardReaderFactory { ICardReader Open(CardSource source, CardIdentity identity); }   // ScanService, Commit and the CLI

// ── library
public sealed record RootListing(string Root, DestRoot Kind, bool IsPrevious, bool Available, ListingResult Listing);
public sealed record LibraryListings(RootListing Video, RootListing Photo, ImmutableArray<RootListing> PreviousPhoto);
public sealed record LibraryFile(string FullPath, FileKey Key, DateTime MtimeUtc, uint RawAttributes, LibraryFolderRef? EventFolder);
public sealed record SetFolderListing(string FullPath, string Name, ImmutableArray<(string Member, long Size, DateTime MtimeUtc)> Members);
public sealed class LibraryIndex {
  public static LibraryIndex Build(LibraryListings listings, LedgerSnapshot ledger, ClockModel clock);  // .uas-sort subtree dropped
  public ImmutableArray<LibraryFile> Match(FileKey key);                            // same NormName + Size in any listed root
  public ImmutableArray<LibraryFile> SameNameOtherSize(string normName, long size); // conflict evidence from listings
  public ImmutableArray<LibraryFolder> Folders { get; }                             // event folders (§7.1)
  public ImmutableArray<SetFolderListing> SetFolder(string name);                   // `<any listed root>\name`, if present
  public DateTime? WatermarkUtc { get; }                                            // §7.1; null = no video imported outside uas-sort
  public ImmutableArray<string> UnavailableRoots { get; } }

// ── ledger (in memory, after the union and revokes)
public enum VerifyKind { Unbuffered, Cached, NameSize }
public enum FolderSource { Created, Appended, CardLeftovers }
public sealed record LedgerFile(FileKey Key, string Src, DestRoot Root, string Dest, UInt128? Xxh128, VerifyKind Verify, DateTime AtUtc,
                                DateTime? CaptureUtc, GeoPoint? Point, string? TzId, DateOnly? LocalDate, SessionKey? Session,
                                string? Set, string Machine, string Run);
public sealed record LedgerSet(string SetName, DateTime FirstFrameCaptureUtc, ImmutableArray<(string Member, long Size)> Members);
public sealed record LedgerDecision(string Id, FileKey Key, DecisionKind Kind, DateTime AtUtc, string Machine, string? Set, string Why);
public sealed record LedgerFolder(string Path, string Description, FolderSource Source, GeoPoint? Centroid,
                                  DateOnly Start, DateOnly End, string TzId);
public sealed record LedgerRun(string Run, string Machine, DateTime StartUtc, DateTime EndUtc, string App, CardIdentity Card,
                               string? Model, string InventoryHash, string VideoRoot, string PhotoRoot, VerdictLevel Verdict,
                               ImmutableDictionary<AuditCategory, int> Counts);
public sealed record LedgerCardDelete(string Run, DateTime AtUtc, FileKey Key, string Src, string Evidence, string Machine);
public sealed record LedgerParseIssue(string File, int Line, string Reason);
public enum LedgerFolderState { Ok, Empty, Missing, NotPinned, Unwritable, CloudOnly, VideoRootMissing }
public sealed record LedgerFolderStatus(string Folder, LedgerFolderState State /* the most severe that applies: VideoRootMissing >
    CloudOnly > Unwritable > NotPinned > Missing > Empty > Ok */, bool Exists, bool InSyncRoot, bool Pinned, bool Writable,
    ImmutableArray<string> LedgerFiles, ImmutableArray<string> CloudOnlyFiles, ImmutableArray<string> OtherMachineFiles);

// ── ledger records (serialised; one JSON line each; fields mirror §11 one for one, camelCase)
[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")] /* + [JsonDerivedType] per case: file, folder, seen, decision, revoke, run, torn, cardDelete */
public closed record class LedgerRecord(int V, string Id, string Machine);
public sealed record class FileRecord(int V, string Id, string Machine, string Run, DateTime At, string Kind /* video|photo|twin|setMember */,
    string Name, long Size, string Src, string Root /* video|photo */, string Dest, string? Xxh128, string Verify /* unbuffered|cached|nameSize */,
    DateTime Mtime, DateTime? CaptureUtc, string? TimeSource, double? Lat, double? Lon, string? Tz, DateOnly? LocalDate,
    DateTime? SessionUtc, string? Serial, string? Set) : LedgerRecord(V, Id, Machine);
public sealed record class FolderRecord(int V, string Id, string Machine, string Run, string Path, string Desc,
    string Source /* created|appended|cardLeftovers */, double? Lat, double? Lon, DateOnly Start, DateOnly End, string Tz) : LedgerRecord(V, Id, Machine);
public sealed record class SeenRecord(int V, string Id, string Machine, string Run, DateTime At, string Name, long Size, string Src,
    DateTime? CaptureUtc, string Status /* New|Conflict */, string Why, string? Set) : LedgerRecord(V, Id, Machine);
public sealed record class DecisionRecord(int V, string Id, string Machine, string? Run, DateTime At, string Kind /* assumedImported|dismissed */,
    string Name, long Size, string Src, DateTime? CaptureUtc, string Why, string? Set) : LedgerRecord(V, Id, Machine);
public sealed record class RevokeRecord(int V, string Id, string Machine, DateTime At, string Decision /* the decision's id */) : LedgerRecord(V, Id, Machine);
public sealed record class RunRecord(int V, string Id, string Machine, string Run, DateTime Start, DateTime End, string App, RunCard Card,
    RunRoots Roots, string Verdict, ImmutableDictionary<string, int> Counts) : LedgerRecord(V, Id, Machine);
public sealed record RunCard(string Serial, string? Label, string Fs, string? Model, string InventoryHash);
public sealed record RunRoots(string Video, string Photo);
public sealed record class TornRecord(int V, string Id, string Machine, DateTime At, int Line) : LedgerRecord(V, Id, Machine);
public sealed record class CardDeleteRecord(int V, string Id, string Machine, string Run, DateTime At, string Name, long Size,
    string Src /* card rel path */, string Unit /* ItemId */, DateTime? CaptureUtc,
    string Evidence /* VerifiedThisRun|InLedger|NameSizeMatch, "historyOnly:" prefix, notInLibraryConfirmed, companionOf:<evidence> */,
    string Reason,
    string Mode /* beforeDate|freeSpace */, RunCard Card, string? Set) : LedgerRecord(V, Id, Machine);   // written right after each delete (§10.6)
public interface ILedgerWriter : IDisposable {    // from ILedgerStore.OpenOwn(); one instance per Commit or user action
  void Append(LedgerRecord r); }                   // one line + "\n", FlushFileBuffers, then the same line to the local mirror;
                                                   // throws on any failure (Commit stops, §10.3)

// ── settings, reports, offload plumbing
public sealed record MapSettings(string Base /* streets|satellite|none */, string StreetsStyleUrl, string StreetsDarkStyleUrl,
                                 string SatelliteUrl, ImmutableDictionary<string, string> SatellitePresets);
public sealed record LayoutSettings(double TimelineWidth, double MapHeightRatio);
public sealed record SettingsLoad(Settings Settings, bool Recovered, string? CorruptCopyPath, RunRoots? RootsFromLastRun);
public sealed record ReportLine(string CardRelPath, string? Dest, string Outcome, CopyPhase? Phase, string? Error, string? Xxh128, string? Verify);
public sealed record OffloadReport(int V, string RunId, Settings SettingsSnapshot, string PlanSummary, ImmutableArray<ReportLine> Files,
                                   ImmutableArray<AuditLine> Audit, ImmutableArray<string> CardChanges, VerdictLevel Verdict,
                                   string Headline, StopReason? Stop);
public sealed record CleanupReportLine(string CardRelPath, long Size, string Unit, string Outcome, string Eligibility, string Evidence,
                                       string Reason, int? Win32Error, bool LedgerRecorded);
public sealed record CleanupReport(int V, string RunId, CardIdentity Card, CleanupRequest Request, CleanupCutoff Cutoff,
                                   ImmutableArray<CleanupReportLine> Files, ImmutableArray<CleanupKept> NotDeletable, CleanupStop? Stop,
                                   long FreeBefore, long FreeAfter, VerdictLevel VerdictAfter /* NotSafe if the rescan failed */);
public sealed record CleanupProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, string? CurrentFile, ItemId? Unit);
public sealed record VolumeNeed(string Volume, int Files, long Bytes, long FreeBytes, long RequiredFree);   // RequiredFree = Σ + max(1 GiB, 2 % of Σ)
public sealed record PreflightReport(ImmutableArray<Issue> Issues, ImmutableArray<string> FoldersToCreate,
                                     ImmutableArray<(string Path, Confidence Confidence)> FoldersAppended, ImmutableArray<VolumeNeed> Volumes,
                                     ImmutableArray<string> StaleTemps /* listed only; deleted at Start offload */,
                                     ImmutableArray<ItemId> AlreadyThere)
{ public bool CanStart => !Issues.Any(i => i.Severity == IssueSeverity.Blocking); }
public sealed record Ejected(string Volume);   public sealed record EjectRefused(string Volume, string Reason);
public union EjectResult(Ejected, EjectRefused);
public enum ScanPhase { ListingCard, ListingLibrary, ReadingLedger, ReadingMetadata, BuildingPlan }
public sealed record ScanProgress(ScanPhase Phase, int Done, int Total, string? Current);
public sealed record OffloadProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, double MBps /* rolling 5 s */,
                                     TimeSpan? Eta, string? CurrentFile, CopyPhase Phase, GroupId? Group);

// ── geo
public sealed record TzLookup(string IanaId, ImmutableArray<string> Alternatives, bool IsEtc);
public enum PlaceClass : byte { Populated, Feature }
public sealed record PlaceHit(string Name, GeoPoint Point, PlaceClass Class, string FeatureCode, int Population, string TzId, Distance Away);

// ── time resolution output
public sealed record ResolvedItem(RawItem Raw, ItemTime Time, ItemFlags Flags, GpsFix? Gps, SessionKey? Session);

// ── planning seam (PlanSession derives through it; tests inject GatedPlanDeriver)
public interface IPlanDeriver {
  Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct); }

// ── Review services
public interface IUiDispatcher { bool HasThreadAccess { get; } void Post(Action a); }
public enum DialogResult { Primary, Secondary, Close }
public sealed record DialogRequest(string Title, string Body, string Primary, string? Secondary, string Close);
public interface IDialogService { Task<DialogResult> ShowAsync(DialogRequest r); }   // queued: one ContentDialog open at a time
```

**Rules for the model**
- **Plan is immutable.** `Planner.Derive(PlanBase, Tuning, edits, SessionFlags)` is pure and takes milliseconds. Every edit re-derives the whole plan on the thread pool. Edits are serialised, each validated against the plan that includes every earlier edit; tuning previews are latest-wins; a plan never replaces one with a higher `Revision` (§4.2 PlanSession, §9.7).
- **JSON.**
  - Unions stay in memory only.
  - Everything serialised (`PlanEdit`, `TargetChoice`, `LedgerRecord`, map messages) is a `closed` record with `[JsonPolymorphic]` in a source-generated context. Reports serialise flattened `ReportLine`s and `CleanupReportLine`s, not `CopyOutcome` or `CleanupOutcome`. `CleanupPlan` and `ConfirmedCleanupPlan` are never serialised (a confirmation can't outlive the page that made it).
  - The stack-proof step (§14 step 1) proves that closed records round-trip, both in unit tests and in the Native AOT `--selftest`.

---

## 4. Components

### 4.1 Ports (in Core; implemented in Platform or in fakes)

```csharp
public interface IVolumeProvider  { IReadOnlyList<VolumeInfo> GetVolumes(); }
public sealed record VolumeInfo(string Root, CardIdentity Identity, string DriveType, bool IsReady, bool IsReadOnlyVolume /* FILE_READ_ONLY_VOLUME */,
                                bool IsNtfs, bool IsRemovableBus, long FreeBytes,
                                string BusType /* IOCTL_STORAGE_QUERY_PROPERTY: "Sd", "Mmc", "Usb", "Nvme", … */,
                                bool RemovableMedia /* STORAGE_DEVICE_DESCRIPTOR.RemovableMedia */,
                                bool IsSystemBootOrPaging);   // the last three feed the cleanup volume check (§10.6)
public interface IDirectoryLister {                               // listing only; never opens a file
  ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames); }
                                                                  // AttributesToSkip=0, IgnoreInaccessible=false, errors collected;
                                                                  // a directory whose name is in excludeDirNames (case-insensitive) is
                                                                  // neither returned nor entered. Library roots pass {".uas-sort"};
                                                                  // the card, the ledger store and stale-temp checks pass {}
public sealed record FsEntry(string FullPath, string RelPath, bool IsDirectory, long Size, DateTime MtimeUtc, DateTime CreationUtc,
                             DateTime LastAccessUtc, uint RawAttributes);
public sealed record ListingResult(ImmutableArray<FsEntry> Entries, ImmutableArray<(string Path, int Win32Error)> Errors);
public interface ICardSourceValidator {                           // anchor + overlap + sync roots (§4.3)
  CardSourceCheck Validate(string chosenPath, VolumeInfo? detected /* from CardDetector; null = Browse to folder (and the CLI) */,
                           Settings s, IDirectoryLister lister, IPathFacts facts, string appDataDir); }
                                                                  // IsBrowsedFolder = detected is null, even when a Browse result is a
                                                                  // volume root; IsWriteProtected = detected?.IsReadOnlyVolume ?? false;
                                                                  // Identity = detected?.Identity when detected is given
public interface ICardReader {                                   // bound to one CardSource + CardIdentity; FileAccess.Read, FileShare.ReadWrite
  CardIdentity CurrentIdentity();                                 // GetVolumeInformationW on the card root (cheap)
  Stream OpenRandom(string cardRelPath);                          // probes, thumbnails (4 KB block cache on top)
  Stream OpenSequential(string cardRelPath);                      // copy
  FsEntry Stat(string cardRelPath);
  ListingResult Relist();                                         // audit-time re-listing
  CardSpace Space(); }                                            // GetDiskFreeSpaceExW + GetDiskFreeSpaceW on the card root; opens nothing
public interface ICardEraserFactory {                            // Platform; Card cleanup only (§10.6)
  ICardEraser Open(CardSource source, CardIdentity pinned, ConfirmedCleanupPlan plan); }
                                                                  // Re-derives from Win32, through an internal IVolumeFacts, every item of
                                                                  // the cleanup volume check (§10.6): volume root (GetVolumePathNameW), FS,
                                                                  // identity (GetVolumeInformationW), bus + RemovableMedia
                                                                  // (IOCTL_STORAGE_QUERY_PROPERTY), not system/boot/paging, no configured
                                                                  // root on it, the MISC index. It never trusts CardSource flags. Throws
                                                                  // UnsafeIoException on any failure, a write-protected volume, or a plan
                                                                  // whose card root or identity differ. Builds the eraser's own
                                                                  // GuardContext (CardIsVerifiedCardVolume, Cleanup = plan); no caller
                                                                  // can pass one in. The IVolumeFacts constructor is internal
                                                                  // (InternalsVisibleTo Platform.Tests only)
public interface ICardEraser : IDisposable {                     // bound to one verified card volume, one identity, one confirmed plan;
                                                                  // IoGuardPolicy.Check(CardDelete, …) before every call
  EraseResult DeleteFile(string cardRelPath);                     // DeleteFileW(\\?\…); never clears attributes, never opens the file
  EraseResult RemoveEmptySetFolder(string cardRelDir); }          // RemoveDirectoryW(\\?\…); fails on a non-empty folder; never recursive
                                                                  // (no volume flush: safe removal flushes the card, §10.6)
public sealed record EraseOk;   public sealed record EraseError(int Win32Error, string Message);
public union EraseResult(EraseOk, EraseError);
public interface IFileOps {                                      // destination writes only; guarded
  Stream CreateTemp(string finalPath, long size, out string tempPath);   // CreateNew + preallocate, Hidden|NotContentIndexed, "*.uas-sort.tmp"
  void FlushToDisk(Stream s);
  VerifyResult VerifyHash(string tempPath, long size, UInt128 expected, CancellationToken ct); // File.OpenHandle(NO_BUFFERING) → buffered fallback
  void FinalizeAttributes(string tempPath, DateTime creationUtc, DateTime mtimeUtc);          // copy times, clear Hidden
  RenameResult RenameNoReplace(string tempPath, string finalPath);                              // MoveFileExW, never REPLACE_EXISTING
  bool ConfirmFinal(string finalPath, long size);                                               // metadata only
  void FlushDestination(string dir, IReadOnlyList<string> filesCreatedThisRun);                 // non-NTFS / removable volumes (§10.3)
  void DeleteOwnTemp(string tempPath);
  void EnsureDirectory(string dir, bool allowCreate);             // allowCreate only for NewFolder targets (+ their YYYY/YYYY-MM parents)
                                                                  // and new set folders under the photo root;
                                                                  // never .uas-sort (only ILedgerStore.EnsureFolder creates that)
  bool TryGetSize(string path, out long size);
  long FreeBytes(string anyPathOnVolume); }
public interface ILedgerStore {                                  // folder = LedgerPaths.For(videoRoot) = <videoRoot>\.uas-sort (derived, never configured)
  LedgerFolderStatus Check();                                     // attributes and security descriptors only, BEFORE any open (§11)
  LedgerSnapshot Load();                                          // union of every <videoRoot>\.uas-sort\ledger*.jsonl (top level only, incl. OneDrive
                                                                  // conflict copies), deduped by record id; honours `torn` records
  void EnsureFolder();                                            // creates <videoRoot>\.uas-sort if missing (the folder only), then KeepOnDevice(),
                                                                  // also when the folder already existed unpinned. Called by Start offload, CopyInto
                                                                  // and the start of Card cleanup's deletes (§10.6)
  ILedgerWriter OpenOwn();                                        // <videoRoot>\.uas-sort\ledger-<MACHINE>.jsonl, created if missing, append,
                                                                  // FileShare.Read (single writer). If the file doesn't end in "\n", it first appends
                                                                  // "\n" and a TornRecord naming the torn line N (§11). Mirrored to
                                                                  // LedgerPaths.BackupDir(...) (local, never under the library)
  void SnapshotToBackup(string runId);                            // copies every loaded ledger*.jsonl into BackupDir\snapshots\<yyyyMMdd-HHmmss>-<run8>\
                                                                  // (keeps 20); called by Start offload before the first copy and by
                                                                  // Card cleanup before the first delete
  void KeepOnDevice();                                            // FILE_ATTRIBUTE_PINNED on the .uas-sort folder only (§4.3)
  void CopyInto(string newVideoRoot, LedgerSnapshot current); }   // video-root change [Copy]: EnsureFolder() there, then append every current
                                                                  // record, original id and machine kept, to the own ledger file under
                                                                  // <newVideoRoot>\.uas-sort (§9.14). Settings already hold the new root
public interface ISettingsStore { SettingsLoad Load(bool readOnly = false); void Save(Settings s); }
                                                                  // Recovered=true if defaults were used; readOnly (the CLI) never renames,
                                                                  // creates or writes anything, and just returns derived defaults on a bad file
public interface IDraftStore { Draft? Load(string cardKey); void Save(string cardKey, Draft d); void Delete(string cardKey); }
public interface IReportStore { string Save(OffloadReport r); string Save(CleanupReport r); }   // cleanup: reports\<ts>-<run8>-cleanup.json
public interface IAppAssets { Stream OpenPlaces(); Stream OpenSelfTest(string name); }   // app folder / embedded only
public interface IPowerRequest { IDisposable KeepSystemAwake(string reason); }   // PowerCreateRequest/PowerSetRequest
public interface IOffloadLock  { IDisposable? TryAcquire(); }                    // named mutex Local\uas-sort-offload
public interface IDeviceEject  { EjectResult Eject(string volumeRoot); }        // CM_Request_Device_Eject; no admin (UNVERIFIED per drive)
public interface IShellLauncher { void OpenFolder(string path); void OpenFile(string path); void OpenHttps(Uri uri); }
public interface ITimeZoneResolver { TzLookup Resolve(GeoPoint p); }           // (IanaId, Alternatives, IsEtc)
public interface IPlaceIndex { IReadOnlyList<PlaceHit> Near(GeoPoint p, Distance r, PlaceClass cls, int max); }
public interface IThumbnailSource {                              // bytes, not images
  ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct);
  IDisposable Pause(); }                                          // Commit (§10.3) and Card cleanup (§10.6): closes the cached card handles;
                                                                  // GetAsync returns empty (placeholders show) until disposed
// Clock: System.TimeProvider everywhere.
```

### 4.2 Units

| Component | Where | What it does | Key members |
|---|---|---|---|
| **CardDetector** | Core | Checks ready volumes and flags DJI cards (§5). It skips drives that hold a configured root; those are reachable only through Browse, which is validated | `IReadOnlyList<CardCandidate> Detect(IReadOnlyList<VolumeInfo>, IDirectoryLister, Settings)` |
| **CardSourceValidator** | Core (policy) + Platform (`IPathFacts`: canonical paths, sync roots) | Anchors a browsed folder at the nearest ancestor holding `DCIM`. Rejects any root that equals, is inside, or contains a library root, a previous photo root, the ledger folder (`<videoRoot>\.uas-sort`, which the video-root check covers too), app data, or any cloud sync root (§4.3). Sets `IsBrowsedFolder` and `IsWriteProtected` from `detected` (§4.1) | `CardSourceCheck Validate(string chosenPath, VolumeInfo? detected, Settings s, IDirectoryLister lister, IPathFacts facts, string appDataDir)` → `SourceOk(CardSource)` / `SourceRefused(reason)` |
| **CardClassifier** | Core | Classifies every entry with fail-safe rules (§5); builds pairs and sets, flags `.trinf` files, turns enumeration errors into `ForcesNotSafe` warnings, and computes the inventory hash: `InventoryHash` = XxHash64 (16 hex digits) over the UTF-8 lines `relPath\|size\|mtimeTicks` plus a line feed of every file entry, sorted ordinally by lowercase `relPath` | `CardInventory Classify(CardSource, ListingResult)` |
| **Mp4Probe** | Core.Media | Port of `docs/research/spikes/djmd/djmd_gps.py`, about 350 lines (§6.3), plus the `mvhd` duration. Never reads `mdat` or the whole `moov` | `static Mp4Info Read(Stream s)` |
| **StillProbe** | Core.Media | DTO from the EXIF directory that actually has it, offset, GPS, model and IFD0 thumbnail range, via MetadataExtractor **Stream** overloads | `static StillInfo Read(Stream s)` |
| **MetadataHarvester** | Core.Media | Reads **sequentially**: videos, photos, then the first frame of each set. Reports progress. A probe failure becomes `ProbeError`, not an exception | `IAsyncEnumerable<RawItem> HarvestAsync(CardInventory, ICardReader, IProgress<ScanProgress>, CancellationToken)` |
| **ThumbnailReader** | Core.Media | Reads a stored byte range from the card: MP4 `tnal` 160×90 or DNG IFD0 160×120. `Pause()` closes its cached card handles until disposed (Commit, Card cleanup) | `: IThumbnailSource` |
| **DroneClock** | Core.Time | Learns the drone clock: **SiteLocal** first, then a zone (§6.1). Sample site zones come from each sample's own GPS through `ITimeZoneResolver` | `static ClockModel Learn(IEnumerable<RawItem>, StoredClockMode settingMode, string settingZoneId, ITimeZoneResolver tz, TimeZoneInfo pc)` |
| **TimeResolver** | Core.Time | Works out capture time, zone, local date, session and flags (§6.2–6.5), including `ClockMismatch` from `ClockModel.OffsetAt`. Zone fallbacks don't depend on R or G | `ImmutableArray<ResolvedItem> Resolve(IReadOnlyList<RawItem> raw, ClockModel clock, ITimeZoneResolver tz, IPlaceIndex? places, TimeZoneInfo pc, DateTime nowUtc)` |
| **GpsPlausibility** | Core.Geo | Gates generic-search GPS hits (§6.3 step 5b) | `GpsProbe Check(GpsFix first, GpsFix? last, TzLookup, IReadOnlyList<GeoPoint> cardPoints)` |
| **GeoTimeZoneResolver** | Core.Geo | Wraps GeoTimeZone and flags `Etc/*` results | `: ITimeZoneResolver` |
| **PlaceIndex** | Core.Geo | Compact binary GeoNames extract with a 0.1° grid index, loaded lazily on a background thread via `IAppAssets` | `static PlaceIndex Load(Stream gz) : IPlaceIndex` |
| **LibraryIndex** | Core.Library | Built from listings of the video root, photo root and `previousPhotoRoots`, plus the ledger (§7.1); never given a stream. The `<videoRoot>\.uas-sort` subtree is excluded from every key, event folder, newness test and the watermark | Declared in §3 Supporting types (2): `Build`, `Match`, `SameNameOtherSize`, `Folders`, `SetFolder(name)`, `WatermarkUtc`, `UnavailableRoots` |
| **NewnessRules** | Core.Planning | Applies §7 | `Newness Video(VideoUnit v, LibraryIndex lib, LedgerSnapshot ledger)`; `Newness Photo(MediaUnit unit /* PhotoUnit or SetUnit */, ItemTime t, LibraryIndex lib, LedgerSnapshot ledger, SetPlacement? placement, IReadOnlySet<DateOnly> newVideoDays, DateTime? watermarkUtc)` |
| **Clusterer** | Core.Planning | Applies §8.2, then the structural edits by the algorithm of §8.9; returns groups and **caused** boundaries | `ClusterResult Cluster(IReadOnlyList<Item> videos, Tuning t, IReadOnlyList<PlanEdit> structuralEdits, out int missingEdits)` |
| **DaySplitFinder** | Core.Planning | Finds day changes inside a group (§8.3) | `ImmutableArray<DaySplit> Find(IReadOnlyList<Item> groupVideos)` |
| **FolderDecider** | Core.Planning | Chooses the target from the New and ticked-Conflict subset (§8.5), with cross-day rules; pass 2 adds the UserSplit rule (§8.9); ranks append candidates | `GroupTarget Decide(GroupDraft g, IReadOnlyList<GroupDraft> all, IReadOnlySet<ItemId> included, LibraryIndex lib, Tuning t, IReadOnlyDictionary<GroupId, GroupTarget>? pass1)` (`pass1` null = pass 1); `IReadOnlyList<LibraryFolder> AppendCandidates(GroupDraft, int max)` |
| **FolderNamer / SetFolderNamer** | Core.Naming | Builds paths, cleans names (§8.6), applies the set-clash rule (§8.8) | `string NewFolderRel(DateOnly, string)`, `string Clean(string)`, `SetPlacement Resolve(SetUnit, Item, LibraryIndex, LedgerSnapshot, ISet<string> batchTaken)` |
| **DescriptionSuggester** | Core.Naming | Ranks name suggestions per local day (§8.7) | `IReadOnlyList<Suggestion> Suggest(GroupDraft, LibraryIndex, IPlaceIndex?)` |
| **Planner** | Core.Planning | `Prepare` covers the tuning-independent steps (normalise, newness, photo days, sets). `Derive` follows the derive order of §8.9: clustering and structural edits, inclusion, two-pass decisions, names, pins, issues | `PlanBase Prepare(ScanResult)`; `Plan Derive(PlanBase, Tuning, IReadOnlyList<PlanEdit>, SessionFlags, int revision, CancellationToken)`; `: IPlanDeriver` |
| **PlanSession** | Core.Editing | Keeps the edit log and the undo/redo stack of `(Tuning, edits)` states; applies quick fixes as one entry; produces the draft snapshot. **Threading contract:** edits, undo and redo go through one serial queue; each is validated synchronously against the plan that includes every earlier queued edit (a `Rejected` result returns at once and changes nothing), then derived on the thread pool through the injected `IPlanDeriver`. `Preview` cancels the previous preview's token (latest wins); a preview whose edit log was superseded by a completed edit is discarded and re-run on the new log. `Changed` is raised on a thread-pool thread with a strictly increasing `Revision`; VMs marshal through `IUiDispatcher` and ignore a lower revision | `Plan Current { get; }` (last completed); `Task<EditResult> ApplyAsync(PlanEdit, CancellationToken)`; `Task<EditResult> ApplyAllAsync(IReadOnlyList<PlanEdit>, CancellationToken)` (one undo entry); `void Preview(Tuning)`; `Task CommitTuningAsync()`; `Task<Plan> UndoAsync()` / `RedoAsync()`; `void AcceptLedgerIssues()` (sets `SessionFlags.LedgerIssuesAccepted` and re-derives); `Draft ToDraft()`; `static PlanSession Resume(PlanBase, Draft, IPlanDeriver, out int dropped)`; `event Action<Plan> Changed` |
| **ScanService** | Core | Runs the Plan stage: validate the source, list the card, list the library roots (in parallel, `excludeDirNames = {".uas-sort"}`), `Check()` then load the ledger, harvest, prepare | `Task<ScanResult> ScanAsync(CardSource, ICardReaderFactory, IProgress<ScanProgress>, CancellationToken)` |
| **OffloadCompiler / Preflight** | Core.Offload | §10.1–10.2. `Check` writes nothing | `OffloadBatch Compile(Plan)`; `PreflightReport Check(OffloadBatch, Plan, IFileOps, IDirectoryLister, ICardReader, ILedgerStore, IOffloadLock, Settings)` |
| **IoGuardPolicy** | Core | The one set of IO rules (§4.3), pure and table-tested; Platform and the fake FS both call it | `static GuardDecision Check(IoOp op, string canonicalPath, uint? attributes, GuardContext ctx)` |
| **CopyEngine** | Core.Offload | §10.3–10.4 | `Task<OffloadResult> RunAsync(OffloadBatch, ICardReader, IFileOps, ILedgerWriter, IProgress<OffloadProgress>, CancellationToken)` |
| **CardAudit** | Core.Offload | §10.5: re-lists and diffs the card, audits per file, and takes the worst category per unit. Also run by Card cleanup's preparation, with this run's `OffloadResult` or none (§10.6) | `FormatVerdict Audit(CardInventory, ListingResult relisted, CardIdentity now, Plan, OffloadResult?, LedgerSnapshot)` |
| **CleanupVolumeCheck** | Core.Cleanup | §10.6: the cleanup volume check for the button (the eraser factory repeats it from Win32) | `static string? Refusal(VolumeInfo, ListingResult card, Settings, string appDataDir)` (null = passes); `static (string? Refusal, string? Detail) Evaluate(…)` (same arguments; Detail names the failing rule) |
| **CleanupPlanner** | Core.Cleanup | §10.6: builds every unit's candidate from the fresh listings and ledger (files in delete order, eligibility, evidence source, reason, allocated bytes, time, clip length, place, ticked for offload), then the oldest-first plan for either mode, the cutoff, the shortfall and the fingerprint. Pure; the only caller of `CleanupPlan`'s constructor | `ImmutableArray<CleanupCandidate> Candidates(CleanupInputs)`; `CleanupPlan Build(CleanupInputs, ImmutableArray<CleanupCandidate>, CleanupRequest, CleanupRows rows, IReadOnlySet<ItemId> firstShown /* rows present when the list was first shown; later ones start undecided */)` |
| **CleanupExecutor** | Core.Cleanup | §10.6: owns the whole run: offload lock, keep-awake, thumbnail pause, ledger `Check`/`EnsureFolder`/`SnapshotToBackup`/`OpenOwn`, fresh listings, `ICardEraserFactory.Open`, per-file identity and stat checks, the evidence re-check, deletes, a `cardDelete` record after each delete, the closing re-list. The rescan and the report belong to `CleanupVm` | `Task<CleanupResult> RunAsync(ConfirmedCleanupPlan, CleanupEnvironment, IProgress<CleanupProgress>, CancellationToken)` |
| **Platform** | Platform | Windows implementations of the ports: &lt;br>• `WindowsVolumeProvider`; &lt;br>• `WindowsDirectoryLister`: a `FileSystemEnumerator<FsEntry>` subclass with `ContinueOnError` recording errors; &lt;br>• `WindowsCardReader` (`LibraryImport` for `GetDiskFreeSpaceW`/`GetDiskFreeSpaceExW` behind `Space()`); &lt;br>• `WindowsCardEraser` + `WindowsCardEraserFactory` (Card cleanup, §10.6): `LibraryImport` for `DeleteFileW` and `RemoveDirectoryW` (declared only there), `\\?\` paths; the factory's internal `IVolumeFacts` (`WindowsVolumeFacts`: `GetVolumePathNameW`, `GetVolumeInformationW`, `IOCTL_STORAGE_QUERY_PROPERTY` through a `DeviceIoControl` on a volume handle opened with no data access, system/boot/paging checks); &lt;br>• `GuardedFileOps` wrapping `WindowsFileOps`: `LibraryImport` for `MoveFileExW`, `FlushFileBuffers`, `GetFinalPathNameByHandleW`, `CfGetSyncRootInfoByPath`, `CM_Request_Device_Eject`, `RtlSetProcessPlaceholderCompatibilityMode`, `AllowSetForegroundWindow`; `\\?\` prefix on every P/Invoke path; `File.OpenHandle` for unbuffered verify; &lt;br>• `PlaceholderGuard`, `SyncRootDetector`, `KnownFolders`; &lt;br>• JSON stores (settings, `LedgerStore` for `<videoRoot>\.uas-sort\` with its local backup, drafts, reports), `AppAssets`, `PowerRequest`, `OffloadLock`, `SingleInstance`, `ShellLauncher`, `FileLog`; &lt;br>• `WindowsCardReaderFactory : ICardReaderFactory`, `PathFacts : IPathFacts`. &lt;br>Every open, create, attribute change, delete and rename in `GuardedFileOps`, `WindowsCardReader`, `WindowsCardEraser` and `LedgerStore` first calls `IoGuardPolicy.Check` | — |
| **Review VMs** | Review | &lt;br>• Stages: `ShellVm` (stage machine), `SetupVm`, `CardStageVm`, `ScanStageVm`. &lt;br>• Review: `ReviewVm` (`VideosTabVm`, `PhotosTabVm`, `OtherTabVm`), `GroupCardVm` (carries its preceding boundary chip and any folded run), `FoldedRunVm`, `ClipRowVm` (carries an optional day-split banner), `SuggestionVm` (`ToString()` = text), `PhotoDayVm`, `TuningVm`, `IssuesVm`. &lt;br>• Map and commit: `MapBridge`, `PreflightVm`, `CopyVm`, `VerdictVm`, `SettingsPageVm`. &lt;br>• Card cleanup: `CleanupVm` (mode, date picker to `DateOnly`, free-space kind, summary, acknowledgements; after the run it asks `ShellVm` to rescan and saves the `CleanupReport` through `IReportStore`), `CleanupRowVm` (one not-in-library review row with Keep/Delete/undecided; `ToString()` = its text), `CleanupResultVm`. &lt;br>• Plumbing: `CollectionSync` (keyed diff that keeps selection and scroll) | Services: `IUiDispatcher`, `IDialogService` (queued), `IShellLauncher`, `IThumbnailSource` |
| **App** | App | `Program.Main` (single instance), `MainWindow` (TitleBar, Mica, Frame), Pages (Setup, Card, Scan, Review, Preflight, Copy, Verdict, Cleanup, Settings), `MapPane` (WebView2), `Thumb.Key` attached property + `ThumbnailCache` (LRU of 400 decoded at 96 px), `DeviceChangeWatcher`, `SelfTest`, `CompositionRoot` | — |
| **Cli** | Cli | `uas-sort-cli plan` is a dry run that writes nothing: no drafts, no ledger, no files; logs go to stderr. There is no cleanup command. It uses the same `CardSourceValidator`. Contract in §4.5 | `static int Main(string[] args)` |

### 4.3 IO guard (`IoGuardPolicy`, enforced by `GuardedFileOps`, `WindowsCardReader`, `WindowsCardEraser`, `LedgerStore`, `PlaceholderGuard`, `CardSourceValidator`)

**The policy is one pure Core function.** Every rule in this section is implemented once, in `IoGuardPolicy.Check`, and never re-implemented. `GuardedFileOps`, `WindowsCardReader`, `WindowsCardEraser` and the Platform `LedgerStore` call it before every open, create, attribute change, delete or rename, and throw `UnsafeIoException` (or report `CloudOnly`) on anything but `GuardAllow`. `FakeFileSystem` (Testing, `net11.0`) calls the same function and throws `HydrationViolation` for `GuardHydration`. Listings and attribute reads are not opens and are not checked.

```csharp
public enum IoOp { ReadData, AppendOwnLedger, CreateNew, CreateDir, SetPinned, SetAttributesOrTimes, Delete, Rename, OpenForFlush,
                   CardDelete /* Card cleanup only (§10.6): a card file, or an emptied set folder */ }
public sealed record GuardContext(string VideoRoot, string PhotoRoot, ImmutableArray<string> PreviousPhotoRoots, string? CardRoot,
    string AppDataDir /* %LOCALAPPDATA%\uas-sort */, string Machine, IReadOnlySet<string> NewFolderDirs /* incl. YYYY, YYYY-MM parents */,
    IReadOnlySet<string> OwnTempsThisRun, IReadOnlySet<string> RenamedThisRun,   // all canonical, compared case-insensitively
    string SystemVolumeRoot /* e.g. C:\ */,
    bool CardIsVerifiedCardVolume /* true only in the GuardContext that WindowsCardEraserFactory builds after its Win32 volume check */,
    ConfirmedCleanupPlan? Cleanup /* set only in the eraser's own context while CleanupExecutor runs; null everywhere else */);
public sealed record CardDeleteViolation(string Path, IoOp Op, string Reason);   // FakeFileSystem.CardDeleteViolations (§4.3, §13)
public sealed record GuardAllow;   public sealed record GuardUnsafe(string Reason);
public sealed record GuardCloudOnly(string Path);   public sealed record GuardHydration(string Path, uint Attributes);
public union GuardDecision(GuardAllow, GuardUnsafe, GuardCloudOnly, GuardHydration);
```

`Check(op, canonicalPath, attributes, ctx)` evaluates, in order:
1. Callers read the target's attributes first; a read that fails for any reason other than "not found" is refused before the policy runs (PlaceholderGuard). `attributes` is null only for a target that doesn't exist, which only `CreateNew`, `CreateDir` and `AppendOwnLedger` accept (null for any other op → `GuardUnsafe`). Any of `0x400000`, `0x40000`, `0x1000` set → `GuardCloudOnly` if the path is a top-level `.uas-sort\ledger*.jsonl`, else `GuardHydration`.
1b. `CardDelete` of a path equal to or under the video root, the photo root, a previous photo root, `LedgerPaths.For(VideoRoot)`, `AppDataDir` or `SystemVolumeRoot` → unsafe, whatever else the context says. This runs before rule 2, so a wrong `CardRoot` can never reach a library, ledger or system file.
2. `CardDelete` outside `CardRoot` → unsafe. Under `CardRoot`:
   - `ReadData` → allow.
   - `CardDelete` → allow only if **all** hold: `Cleanup` is set; `CardIsVerifiedCardVolume`; `Cleanup.Plan.CardRoot` equals `CardRoot`; and either the target is a file (attributes without `FILE_ATTRIBUTE_DIRECTORY`) whose path is in `Cleanup.FilePaths`, or a directory whose path is in `Cleanup.SetFolders` (the eraser removes it with `RemoveDirectoryW`, which fails unless it is empty).
   - Anything else, including `OpenForFlush`, `SetAttributesOrTimes`, `CreateNew` and the offload's `Delete`, of the card root or anything under it → unsafe, in every context.
3. Under `LedgerPaths.For(VideoRoot)`: the four exemption operations below → allow; anything else → unsafe.
4. Under the video root, photo root or a previous photo root: `CreateNew` of a `*.uas-sort.tmp`; `ReadData`, `SetAttributesOrTimes`, `Rename` (to its final name in the same directory) and `Delete` of a path in `OwnTempsThisRun`; `Delete` of any other `*.uas-sort.tmp` (stale temps, at Start offload); `OpenForFlush` of a path in `RenamedThisRun` or a directory in `NewFolderDirs` or holding a renamed file; `CreateDir` of a path in `NewFolderDirs` → allow; anything else → unsafe.
5. Under `AppDataDir`: allow (Platform's own stores).
6. Anything else → unsafe.

**Card**
- The **reader** only reads, and only paths under the anchored card root.
- Its handles request `FileAccess.Read` (GENERIC_READ); never `FILE_WRITE_ATTRIBUTES` or any write right. A Platform test runs the reader against files whose ACL denies all write rights.
- Nothing under the card root is ever created, written or renamed, and no attribute or time is ever changed. The offload never deletes anything there.
- **The one exception is Card cleanup (§10.6):** `ICardEraser` deletes card files, and set folders it has emptied, and only those named in the `ConfirmedCleanupPlan` in its own `GuardContext` (rules 1b and 2). It is created only for a volume that `WindowsCardEraserFactory` has verified from Win32 as a removable card volume (§10.6 cleanup volume check), never for a browsed folder or a backup drive (which could hold a copy of a card) and never for a write-protected card. It never opens a card file, never opens a write or flush handle on the volume, never clears an attribute, and never deletes a directory recursively.
- The reader is bound to the `CardIdentity` taken at scan time. `CurrentIdentity()` is re-checked at Commit start, before each file, and before the verdict, and before each delete during Card cleanup.
- A write-protected volume (`FILE_READ_ONLY_VOLUME`) gets a "write-protected" badge, and [Clean up card…] is disabled.

**Card source validation** (detected volumes and Browse alike)
1. **Anchor.**
   - Walk up from the chosen folder to the nearest ancestor that contains `DCIM` (the chosen folder itself counts).
   - If there is none and the folder holds any media-extension file, refuse: "Pick the card's top folder (the one containing DCIM)".
   - If there is none and the folder holds no media, refuse: "No DCIM folder here".
2. **Canonicalise.**
   - `Path.GetFullPath`, then open the directory with `FILE_FLAG_BACKUP_SEMANTICS` and no data access, then `GetFinalPathNameByHandleW` (resolves subst, junctions and 8.3 names).
   - Compare case-insensitively with a trailing separator.
3. **Refuse overlaps.** Refuse if the root equals, is inside, or contains any of: the video root, the photo root, any `previousPhotoRoots`, the ledger folder `<videoRoot>\.uas-sort` (checked explicitly although the video-root check already covers it), `%LOCALAPPDATA%\uas-sort`, or **any cloud sync root**.
   - "Inside" a sync root: `CfGetSyncRootInfoByPath` succeeds.
   - "Contains" a sync root: OneDrive's `HKCU\Software\Microsoft\OneDrive\Accounts\*\UserFolder` values, plus the registered sync roots under `HKLM\…\SyncRootManager` (read-only registry reads; UNVERIFIED for non-OneDrive providers).
   - Message: "This is part of your library (or a synced folder); uas-sort only offloads from cards."

**Library roots**
- Never opened for reading, with these exceptions:
  - our own `*.uas-sort.tmp` files during verification, and our own just-renamed files during the post-run flush. These are tracked in an in-memory set for the run.
  - **The ledger exemption** (below). It is the only exemption for files the app didn't create in this run.
- Files are created only with `CreateNew`, and only under a configured root. The own ledger file is the one exception: it is opened for append and created if it is missing (§4.1).
- `IFileOps.EnsureDirectory(allowCreate:true)` is allowed only for NewFolder targets (and their `YYYY`/`YYYY-MM` parents) and new set folders under the photo root. Append targets must already exist (§10.2).
- The `<videoRoot>\.uas-sort` folder itself (never anything below it) is created only by `ILedgerStore.EnsureFolder()`.
- `DeleteOwnTemp` accepts only names ending in `.uas-sort.tmp`.

**Placeholders (defence in depth)**
- At process start, Platform calls `RtlSetProcessPlaceholderCompatibilityMode(PHCM_EXPOSE_PLACEHOLDERS)` so placeholders aren't disguised. Its effect on these attribute bits is UNVERIFIED; `--selftest` proves the bits are visible (§13).
- Any open of a file with `0x400000` (RecallOnDataAccess), `0x40000` (RecallOnOpen) or `0x1000` (Offline) throws, whatever the path. If attributes can't be read, the guard refuses.

**Ledger folder: `<videoRoot>\.uas-sort\`, the one exemption from "never open pre-existing library files"**
- **Location.** The path is derived: `LedgerPaths.For(videoRoot) = <videoRoot>\.uas-sort`. There is no separate setting and no `Settings` property. On this PC it is `C:\Users\damia\OneDrive\Pictures\UAS Videos\.uas-sort\`.
- **What the exemption allows.** Only the operations below; nothing else under the library. Paths are matched on the canonical path (`GetFinalPathNameByHandleW`), case-insensitively:
  - **Read** any file **directly** in `<videoRoot>\.uas-sort\` whose name matches `ledger*.jsonl`. This covers other PCs' files and OneDrive conflict copies. Reads use `FileAccess.Read` and `FileShare.ReadWrite`.
  - **Append** only to this PC's own file, `<videoRoot>\.uas-sort\ledger-<MACHINE>.jsonl` (created if missing), with `FileShare.Read`. That makes it the single writer.
  - **Create the `.uas-sort` folder itself** when it is missing, through `ILedgerStore.EnsureFolder()`: on **Start offload** (§4.4), on the video-root [Copy] (§9.14), or when Card cleanup starts deleting (§10.6).
  - **Set `FILE_ATTRIBUTE_PINNED` on that folder**, from `EnsureFolder()` and [Keep on this device].
- **Everything else there is a violation** and throws `UnsafeIoException`. That includes:
  - any other file name;
  - a subfolder;
  - writing to another PC's `ledger-*.jsonl` or to a conflict copy;
  - a delete or a rename.
- **Pinned "Always keep on this device".** The ledger files may be cloud-only on a second PC, so `ILedgerStore.Check()` inspects attributes **before** any open. The PlaceholderGuard rule above still applies: a cloud-only ledger file is never opened.
- **Cloud-only ledger file.** It yields `LedgerFolderStatus.State == LedgerFolderState.CloudOnly` (the file listed in `CloudOnlyFiles`), shown as a Blocking InfoBar: "Set `UAS Videos\.uas-sort` to *Always keep on this device*", with a [Keep on this device] button.
  - The button sets `FILE_ATTRIBUTE_PINNED` on the `.uas-sort` folder only, like `attrib +P`. Whether OneDrive honours an attribute set by API is UNVERIFIED.
  - The status is re-checked after the files have downloaded.
  - This is never the internal-bug `UnsafeIoException`.
- **Invisible to planning.** The `.uas-sort` subtree is excluded from `LibraryIndex`: it gives no keys, no event folders, no newness evidence and no watermark input (§7.1).
- **Backups stay local.** The mirror and the snapshots live in `%LOCALAPPDATA%\uas-sort\ledger-backup\`, never under the library (§11).
- **Never a card source.** `CardSourceValidator` refuses the folder, as it refuses the whole video root (step 3 above).

**Violations**
- Any other violation throws `UnsafeIoException`. The run stops and the error is logged as a bug.

**WebView2**
- Downloads are cancelled, navigation is limited to the virtual host, and no `file:` URIs are allowed.

**Fake file system**
- `FakeFileSystem` does not copy these rules: it calls `IoGuardPolicy.Check` like Platform does. That gives the **hydration tripwire**: any open of a placeholder throws `HydrationViolation`, and so does any open of a pre-existing library file, except under the ledger exemption above (local `.uas-sort\ledger*.jsonl` read, own file append). This run's own temps and just-renamed files are tracked exceptions.
- **Card-delete tripwire.** A card delete the policy refuses (no `ConfirmedCleanupPlan`, a path the plan doesn't name, a directory that isn't a named set folder, an unverified volume) throws `UnsafeIoException` **and** is appended to `FakeFileSystem.CardDeleteViolations` (`IReadOnlyList<CardDeleteViolation>`, §4.3 types); every fixture asserts the list is empty at teardown, so a swallowed exception still fails the test. The fake's eraser factory treats its card as a verified card volume unless a test marks it browsed or fails one of its volume facts.
- Tests therefore prove the shipped rules in three layers (§13): the table-driven policy test (Core.Tests), the end-to-end tripwires (fake FS), and Platform tests showing that `GuardedFileOps`, `WindowsCardReader`, `WindowsCardEraser` and `LedgerStore` consult the policy.

### 4.4 Flow

1. **Launch (`Program.Main`, `DISABLE_XAML_GENERATED_MAIN`).**
   1. `WinRT.ComWrappersSupport.InitializeComWrappers()`, then the placeholder compatibility call.
   2. Unless `--selftest`:
      - `AppInstance.FindOrRegisterForKey("uas-sort")`.
      - If another instance owns the key: `AllowSetForegroundWindow(existing.ProcessId)`, then `RedirectActivationToAsync(args)` **on a worker thread**, while Main waits on an event. Then exit.
      - In the main instance, `Activated` brings the window forward with `SetForegroundWindow(hwnd)` through the dispatcher.
      - Fallback if AppInstance misbehaves in the lean self-contained build (UNVERIFIED): the named mutex `Local\uas-sort` (tested in the stack-proof step, §14 step 1).
   3. `Application.Start(...)`.
   4. Independently, Commit holds `Local\uas-sort-offload`, and the own ledger file (`<videoRoot>\.uas-sort\ledger-<MACHINE>.jsonl`) is opened for appending with `FileShare.Read`. Two offloads can never run at once.
2. **Setup.** Shown on first run, and after a settings recovery (§11). The user chooses the video root and the photo root. The ledger folder is **not asked for**: it is derived as `<videoRoot>\.uas-sort`, and Setup only shows its status (§9.14).
3. **Card.** `CardDetector` runs, or the user browses; the source is validated.
4. **Plan.**
   - `ScanService` lists the card (under 1 s) and lists every root in parallel.
   - It loads the ledger (the union of `<videoRoot>\.uas-sort\ledger*.jsonl`, after `Check()`), harvests metadata sequentially, then calls `Planner.Prepare`.
   - It checks for a draft by `DraftKey` and offers to resume it.
5. **Review.** Each gesture becomes a `PlanEdit` or a tuning change. `PlanSession` derives a new `Plan` on the thread pool (edits serialised, previews latest-wins; §4.2), the VMs diff it in through `IUiDispatcher`, and the draft autosaves after 1 s. Nothing is written to the library.
6. **Commit.**
   - Acquire the offload lock, pin the card identity, and disable device refresh, Rescan, Settings and Browse.
   - Compile and run preflight (with acknowledgements). Preflight writes nothing.
   - On **Start offload**, before the first copy: `ILedgerStore.EnsureFolder()` (creates `<videoRoot>\.uas-sort` if it is missing, the folder only, and pins it, also when it already existed unpinned), `SnapshotToBackup(runId)` (§11), and `DeleteOwnTemp` for each stale temp preflight listed.
   - Run `CopyEngine`. Ledger lines are written per file, and `seen` records at the end.
   - Then the re-list, the audit, the verdict, and the report.
7. **Card cleanup** (optional; §10.6). From the Verdict page, or from the Review title bar, for a scanned card that passes the cleanup volume check.
   - Preparation re-checks the identity, re-lists the card, runs `CardAudit`, lists the library roots afresh and loads the ledger; `CleanupPlanner` builds the plan live as the user chooses a mode, a date or a target, and toggles rows.
   - The confirmation turns the plan into a `ConfirmedCleanupPlan`. `CleanupExecutor.RunAsync` then owns the run: it takes the offload lock, keeps the PC awake, pauses thumbnails, runs `EnsureFolder()`, `SnapshotToBackup(runId)` and `OpenOwn()`, opens the eraser (which re-verifies the volume), deletes oldest first with per-file re-checks, appends a `cardDelete` record after each delete, and re-lists the card.
   - The caller, `CleanupVm`, then has the card rescanned (the verdict is recomputed), saves the cleanup report and shows the result page.

### 4.5 CLI contract (`UasSort.Cli`, `AssemblyName=uas-sort-cli`)

```
uas-sort-cli plan --card <path> [--video-root <path>] [--photo-root <path>] [--radius-mi <5..100>] [--gap-days <0..7>]
                  [--settings <path>] [--json] [--expect <expected.json>]
```

- **Roots and tuning.** Command-line values win. Otherwise they come from `ISettingsStore.Load(readOnly: true)` of `%LOCALAPPDATA%\uas-sort\settings.json` (or `--settings`). A missing or unreadable file gives the derived defaults (video root `KnownFolder(Pictures)\UAS Videos`, photo root `<videoRoot>\Picture Offload`, R 50 mi, G 1, stored clock mode `Zone` with `America/New_York`, `copyJpgTwin` on) and **nothing is renamed, created or written**; that is how the CLI runs on a PC where the app's Setup has never been completed. `previousPhotoRoots` come only from settings.
- **What it runs.** `CardSourceValidator` → `ScanService.ScanAsync` (the ledger is read through `Check()` + `Load()`; never `OpenOwn`, `EnsureFolder` or `SnapshotToBackup`) → `Planner.Prepare` → `Derive` with no edits. The guard is the same as the app's.
- **Output.** Logs and progress go to stderr; the plan goes to stdout. Without `--json`: one block per group, `NEW FOLDER 2026\2026-09\2026-09-27 · 13 clips · Sep 27 · issues: EmptyFolderName`, then photo days, sets, other files and issues. With `--json`, this schema (`v:1`, camelCase, enums as names):

```json
{ "v": 1,
  "card": { "root": "E:\\", "identity": { "serial": "1A2B3C4D", "label": null, "fs": "exFAT", "totalBytes": 256060514304 },
            "model": "FC9113", "files": 214, "inventoryHash": "9f3c0a6d12e4b7a1" },
  "settings": { "videoRoot": "C:\\…\\UAS Videos", "photoRoot": "C:\\…\\Picture Offload", "radiusMiles": 50, "gapDays": 1 },
  "clock": { "mode": "Zone", "zone": "America/New_York", "samples": 13,
             "mismatch": { "items": 25, "siteZones": [ "America/Anchorage" ] } },
  "watermarkUtc": "2026-09-27T18:24:16Z",
  "groups": [ { "id": "DCIM/DJI_001/DJI_20260725232655_0117_D.MP4", "target": "Append",
                "relPath": "2026\\2026-07\\2026-07-25 Council Road", "confidence": "Medium", "why": "different day, 34 mi from Council Road",
                "start": "2026-07-25", "end": "2026-07-26",
                "boundaryBefore": { "cause": "DayGap", "jumpMiles": null, "gapHours": 1488.5, "dayGap": 62 },
                "videos": [ { "id": "DCIM/DJI_001/DJI_20260725232655_0117_D.MP4", "status": "Imported", "included": false,
                              "captureUtc": "2026-07-26T03:26:55Z", "localDate": "2026-07-25", "timeSource": "Mvhd", "flags": [ "ClockMismatch" ] } ],
                "daySplits": [ { "firstOfDay": "DCIM/DJI_001/DJI_20260726235645_0001_D.MP4", "from": "2026-07-25", "to": "2026-07-26",
                                 "apartMiles": 33.7, "emphasised": true } ],
                "issues": [ "MediumAppend", "EmphasisedDaySplit" ] } ],
  "photoDays": [ { "date": "2026-07-25", "tz": "America/Anchorage", "units": 12, "new": 8, "probablyImported": 4, "reason": "videos from this day are already in the library" } ],
  "sets": [ { "id": "DCIM/PANORAMA/001_0087", "folder": "001_0087 2026-09-27", "resolution": "DateSuffixed", "members": 33 } ],
  "other": [ { "relPath": "DCIM/DJI_A001/x.MP4", "class": "Unknown", "rule": null } ],
  "issues": [ { "severity": "Blocking", "code": "EmptyFolderName", "anchor": "DCIM/DJI_001/DJI_20260927140127_0123_D.MP4",
                "message": "Name this folder", "requiresAck": false } ] }
```

- **No cleanup.** The CLI has no Card cleanup command and never deletes anything, on the card or elsewhere.
- **Exit codes.** 0 = plan printed (even with Blocking issues); 1 = source refused by the validator (reason on stderr); 2 = any other error (bad arguments, IO, unhandled exception).
- **Expected folder list.** Before the first-card acceptance (§13) the user writes `tests/acceptance/first-card-expected.json`: `{ "folders": [ { "relPath": "2026\\2026-10\\2026-10-04 Nome Roads", "clips": ["DJI_…_0151_D.MP4", …] } ] }`. `--expect <file>` prints the differences and the **edit count**: for each expected folder whose clips span k > 1 CLI groups, k − 1 (merges); for each CLI group whose clips span k > 1 expected folders, k − 1 (splits or moves); plus 1 per matched folder whose description or target kind differs (a `Rename` or `Retarget`). One edit is one `PlanEdit`; acceptance step 2 passes at ≤ 2.

---

## 5. Card detection & file classification

**Detection** (every ready volume, via `IVolumeProvider`)
- Skip Network, CDRom and NoRootDirectory drives, and drives that hold a configured root.
- **Don't rely on DriveType or the volume label.** D: reports as Fixed exFAT.
- **DJI card:**
  - `\DCIM\` contains a folder matching `^DJI_\d{3}(_.+)?$`, or `PANORAMA\`, or `HYPERLAPSE\`;
  - and at least one file matches `^DJI_(\d{14})_(\d{4})_([A-Z])(?:_[^.]*)?\.([A-Za-z0-9]+)$`.
- **Anything else is "not a card".** That includes RC 2 internal storage and Autel cards; they are listed only as "not a DJI card".
- **Detection is not enough for Card cleanup.** A USB SSD or thumb drive whose root holds a copied card passes these rules, so Card cleanup adds its own, stricter volume check (§10.6).
- **Drone over USB:** it appears as two mass-storage volumes (names UNVERIFIED). Both are listed, and each one is its own run.
- **Camera model:** from `\MISC\FC*.db` (FC9113 = Air 3S, UNVERIFIED), otherwise from the EXIF Model of the first DNG.
- **Listing options:** explicit `EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, ReturnSpecialDirectories = false }`, with errors collected per path. Hidden and system files are listed like any other.

**Classification**
- Paths are relative to the **anchored** card root and matched case-insensitively.
- `<media>` = `DJI_\d{3}(_.*)?`.
- *Media extensions* = mp4, mov, dng, jpg, jpeg, heic, heif, tif, tiff, insv, avi.
- **Only the named rules below produce SkippedByRule.** Any media-extension file that no copy rule claims is **Unknown**, which counts as Unaccounted.

| Match | Class | Destination | Notes |
|---|---|---|---|
| `DCIM\<media>\*.MP4` | Video | `<videoRoot>\<group folder>\name` | Includes QuickShots, MasterShots clips and hyperlapse composites. Flag Truncated if it has no `moov` or a `.<name>.MP4.trinf` sibling exists |
| `DCIM\<media>\*.DNG` | Photo | `<photoRoot>\name` | Burst, AEB and timed shots too; flat |
| `DCIM\<media>\*.JPG` with the same base name as a DNG | PhotoTwin, part of the Photo unit | `<photoRoot>\name` | Copied when `copyJpgTwin` is on (default, accepted by the user; §15 Q2) |
| `DCIM\<media>\*.JPG` with the same base name as an MP4 | **Unknown** ("possible video cover") | — | Stays Unknown until the first-card acceptance (§13) confirms these are covers. After that, a named Skip rule "video cover" is switched on |
| `DCIM\<media>\*.JPG` otherwise | Photo | `<photoRoot>\name` | Includes in-drone stitched panos |
| `DCIM\PANORAMA\<set>\*.(DNG\|JPG)` | SetMember → one Set unit | `<photoRoot>\<set folder>\member` | Set folder rule in §8.8 |
| `DCIM\HYPERLAPSE\<set>\*.(DNG\|JPG)` | SetMember → one Set unit | same | Can hold hundreds of frames; the thumbnail comes from the first frame |
| `*.LRF` | Skip ("proxy") | — | Never copied |
| `*.SRT` | Skip ("captions") | — | Not copied or read (the djmd GPS is enough) |
| `._*`, `.*.trinf`, `.*.avc1`, `.Trashes\**`, `.Spotlight-V100\**`, `.fseventsd\**` | Skip ("system/recovery") | — | A `.trinf` marks its MP4 as Truncated. **Any other dot file with a media extension is Unknown** |
| `MISC\**`, `LOST.DIR\**`, `Android\**`, `System Volume Information\**`, `$RECYCLE.BIN\**` | Skip ("DJI: no backup needed" or "system") | — | Listed, but not probed beyond the model check |
| Media-extension file anywhere else: an unmatched `DCIM` subfolder (e.g. `DCIM\DJI_A001`), a nested folder, outside `DCIM`, or another extension under `DCIM` | **Unknown** | — | Shown on the Other tab. Unaccounted until the user marks it "not needed" (individually, §10.5) |
| Non-media file outside `DCIM` | Skip ("outside DCIM, not media") | — | Listed, collapsed |
| Enumeration error (access denied, I/O error) | ScanWarning, `ForcesNotSafe` | — | The verdict is NotSafe while it remains |

**Companions (used only by Card cleanup, §10.6).** Card cleanup deletes an MP4 together with these Skip files in the same folder, matched case-insensitively on its stem: `<stem>.LRF`, `<stem>.SRT`, the hidden `.<stem>.MP4.trinf`, `.<stem>.MP4.avc1` or `.<stem>.avc1`, and `<stem>.JPG` once the "video cover" Skip rule is on (until then that JPG is Unknown and never deleted). Every other Skip file is never deleted: `MISC\**`, `LOST.DIR\**`, system and dot files not tied to a unit, and an LRF or SRT whose MP4 is gone.

---

## 6. Time & location normalization

### 6.1 Learning the drone clock (site-local or a time zone)

**What the drone clock is** (user decision, 2026-09-27; §1.1). It is whatever the RC 2 is set to: observed US Eastern, never switched since first use, and **not** set from GPS. **Local dates always come from true UTC converted with the GPS site zone** (§6.4). The drone clock is used only to turn stamps that carry no UTC into UTC (DNG EXIF; filenames of truncated or probe-failed clips; library member starts and the watermark, §7.1), and a drone-clock date or time is never used as a local date directly.

**Samples**
- Every DJI MP4 that has a `moov` gives one sample: `(stamp, mvhdUtc, offset = round15min(stamp − mvhdUtc), siteZone)`. So far every sample is −4 h, and all are from summer.
- `siteZone` is the zone of the sample's own first GPS fix through `ITimeZoneResolver` (GeoTimeZone), only for a model-table fix (`DjmdModelTable`) that resolves to a non-`Etc` zone. Any other sample has no `siteZone` (generic-search hits are not gated until §6.3 step 5b runs, and `Etc/*` means the sea).

**Candidates, in order; the first that fits is chosen**
1. **SiteLocal.** Fits when, for every sample that has a `siteZone`, `offset == siteZone.GetUtcOffset(mvhdUtc)`. Samples without a `siteZone` are ignored for this test, and at least one sample must have one.
2. **The stored `droneClockZone`**: the last learned zone (default `America/New_York`; a default, not an assumption).
3. `America/New_York`, `America/Chicago`, `America/Denver`, `America/Phoenix`, `America/Los_Angeles`, `America/Anchorage`, `Pacific/Honolulu`.
4. The PC zone.

A zone (2–4) *fits* when `zone.GetUtcOffset(stamp as zone-local) == offset` for every sample. Summer samples fit both New York and zones fixed at −4 (e.g. `America/Puerto_Rico`); the order resolves that. An Eastern clock flown only in the Eastern zone (Newport RI) fits both SiteLocal and New York; SiteLocal wins by order, and the conversions are identical there.

**Modes**

| Mode | When | Conversion | Time source |
|---|---|---|---|
| `SiteLocal` | Candidate 1 fits | Through the site zone of the item itself: the zone of its own GPS; without GPS (or with an `Etc` zone), the zone of the nearest GPS item on the card within 12 h (compared on drone stamps); else the stored zone. So DST and a trip across zones are handled | `DroneClockSiteLocal` |
| `Zone` | A zone (2–4) fits every sample | Every stamp converts through the zone, so DST is handled | `DroneClockZone` |
| `NearestSample` | No candidate fits (e.g. the RC clock was changed partway through the card — observed when the user corrected a clock that had kept a ship Wi-Fi network's Eastern time zone — or the RC doesn't follow DST, or clock drift) | The nearest sample **in time** (by drone stamp, then by `mvhdUtc` for tie-breaks) within 60 days, else the card's most common offset. `ClockMismatch` is evaluated per item against the offset used for that item, so only items before a correction are flagged | `DroneClockSample`; the clock banner says why |
| `Setting` | No MP4 samples on the card (a photo-only card) | The stored mode: `SiteLocal` converts as in the SiteLocal row, `Zone` through the stored zone | `DroneClockSetting`, flag `ClockFromSetting` |

- **The same `ClockModel` is used everywhere** a drone stamp becomes UTC: card items, library member start times, and the watermark (§7.1). Library members have no GPS of their own, so in `SiteLocal` mode (and `Setting` with a stored `SiteLocal`) a member converts through its event folder's ledger `tz` (`LedgerFolder.TzId`), else the stored zone; the mtime check of §7.1 still applies.
- **After a successful run,** the learned mode is saved: `SiteLocal` as `droneClockMode = SiteLocal` (`droneClockZone` is left as it was), or a fitted zone as `droneClockMode = Zone` plus `droneClockZone`. `NearestSample` and `Setting` save nothing.
- **Header text** (the clock banner, §9.2):
  - Zone: "Drone clock: US Eastern (America/New_York), learned from 13 videos. Folder dates use local time at each site."
  - SiteLocal: "Drone clock: follows local time at each site, learned from 13 videos."
  - NearestSample and Setting say why ("no single time zone fits these videos"; "no videos on this card: using the last learned clock").
  - **Clock change:** in `NearestSample` mode, when the samples sorted by `mvhdUtc` form two or more consecutive runs with different offsets (each run ≥ 1 sample), the banner names each change: "Drone clock changed during this card: UTC−4 until Sep 27 14:30 AKDT, then UTC−8." The change time is the midpoint between the last sample of one run and the first of the next, shown in the site zone of the later sample. It is carried in `ClockSummary.Changes` (§3).
  - In any mode, when any item has `ClockMismatch`, the line continues: "It doesn't match local time where this card was shot (Alaska)." The clock-mismatch InfoBar (§9.2) sits right below it.
  - A "Why?" link explains the RC 2 time-zone setting.

**Worked example** (Zachar Bay 0128, a real clip; §11 ledger example)
- Filename `DJI_20260927140627_0128_D.MP4`: 14:06:27 on the drone clock.
- `mvhd` `creation_time`: 2026-09-27T18:06:27Z, so the sample offset is −4 h.
- GPS 57.55044, −153.73897 → `America/Anchorage`, UTC−8 (AKDT) on that date → local **10:06:27 AKDT, Sep 27**; the folder date is 2026-09-27.
- SiteLocal doesn't fit (−4 ≠ −8); `America/New_York` fits (EDT, −4) → `Zone` mode.
- Drone clock UTC−4 vs site UTC−8: 4 h ≥ 15 min → `ClockMismatch` (§6.5).

### 6.2 Capture-time precedence (first rule that applies)

| # | Condition | CaptureUtc | Source |
|---|---|---|---|
| 1 | DJI video with `moov` | `mvhd` creation time, taken as UTC (`SpecifyKind(Utc)`) | `Mvhd` |
| 2 | EXIF DTO plus `OffsetTimeOriginal` (DJI doesn't write it today) | DTO − offset | `ExifWithOffset` |
| 3 | DJI photo, set, truncated clip or probe-failed item with a drone stamp (EXIF DTO, else the filename) | `ClockModel.ToUtc(stamp, siteZoneId)`, with the item's site zone for SiteLocal (§6.1) | `DroneClockSiteLocal` / `DroneClockZone` / `DroneClockSample` / `DroneClockSetting` |
| 4 | Anything else | Card mtime (whether exFAT mtime is true UTC is UNVERIFIED) | `Mtime` |

GPS-time sources for other DJI models are deferred (the Air 3S has no real GPS time).

### 6.3 GPS extraction

**MP4 (`Mp4Probe`)**
1. **Walk the top-level boxes lazily.**
   - Size 1 means a 64-bit size follows; size 0 means the box runs to end of file; `uuid` boxes have 16 extra header bytes.
   - Reject any size smaller than its header or larger than what remains of the file. Never read `mdat`.
2. **Inside `moov`:**
   - `mvhd` gives the creation time (seconds since 1904; 32-bit in version 0, 64-bit in version 1) and the clip length: `duration / timescale` (duration 32-bit in version 0, 64-bit in version 1) → `Mp4Info.Duration`, shown by Card cleanup (§10.6). A file without `moov` has no duration (`null`); Card cleanup then shows "unfinished · ~7 MB".
   - For each track, check `mdhd`, `hdlr` and `stsd`, and pick the track whose `stsd` has an entry with format `djmd`.
   - Keep `stsc` in memory. Read `stsz`/`stz2` and `stco`/`co64` entries on demand.
   - Check that `stsc`'s description index points at the `djmd` entry.
3. **Decode the protobuf generically:** varint (as `ulong`), fixed 64-bit, length-delimited, fixed 32-bit. A length-delimited value counts as a nested message only if it isn't printable text.
4. **Per-model GPS table.** The model comes from field 1-1-1, which ends in `.proto` (sample 0 only). Positions only.

| Protocol | GPS path | Alt (mm) | Units |
|---|---|---|---|
| dvtm_Air3s, dvtm_Air3, dvtm_Mini4_Pro, dvtm_wm265e, dvtm_pm320 | 3-3-4-1 | 3-3-4-2 | field 1: 0 or absent = radians, 1 = degrees |
| dvtm_wm261, dvtm_wa345e | 3-3-4-1 | 3-3-4-2 | field 1: 0 or absent = radians, 1 = degrees |
| dvtm_Mavic4, dvtm_Mini5Pro | 3-3-4-1 | 3-3-4-2 | always degrees |
| dvtm_AVATA2, dvtm_dji_neo | 3-4-4-1 | 3-4-4-2 | field 1: 0 or absent = radians, 1 = degrees |
| dvtm_ac203/204/206, dvtm_oq101 | 3-4-2-1 | 3-4-2-2 | field 1: 0 or absent = radians, 1 = degrees |

   - **Unknown protocol:** use a generic search for the first sub-message whose fields 2 and 3 are doubles within the valid latitude/longitude range, in degrees or radians. It is recorded with `FieldPath` and source `DjmdGenericSearch`.
5. **Multi-sample search.** A fix with |lat| and |lon| both below 1e-6 means "no fix". Sample indexes are **0-based**; sample 0 is always read (it carries the protocol and the first fix). If it has no fix, probe samples 1–9, then 16, 32, 64, … (each below n), then the last sample, n − 1. The read count is at most 1 + 9 + |{2^k : k ≥ 4, 2^k &lt; n}| + 1, e.g. 16 for n = 300.
   - **5b. Generic hits only.** Also decode the last sample at the same `FieldPath` (`LastSameField`). During normalisation, `GpsPlausibility` accepts the hit only if all of these hold:
     - the first and last fixes are within 3 mi;
     - the point resolves to a non-`Etc` zone;
     - it is within 500 mi of the median of the card's other GPS items (skipped if there are none).
   - An accepted hit gets the `GpsGuessed` flag. Anything else becomes `NoFix(GenericHitImplausible)`.
6. **Session and serial.** `SessionUtc = mvhd − uptime µs` (field 3-1-2 or 1-1-9). The drone serial is field 1-1-5. The research found `SessionUtc` constant only to within ±1 s per power cycle (a 1 s `mvhd` minus a µs uptime), so sessions are compared with `SessionKey(DroneSerial, SessionUtc).SameSession`: equal serials and |Δ| ≤ 2 s. Exact equality is never used.
7. **Truncated clips (no `moov`).** Find the start of the `mdat` payload by walking the top-level boxes, then try the fixed offset 512. Read 4 KB and decode top-level fields 1–3. Accept the result only if the protocol string ends in `.proto`. This recovered GPS from Anvil 0014 and 0024.
8. **Thumbnail.** Record the byte range of `udta/meta/ilst/tnal` (160×90), walking the children without reading `udta` whole. `covr` is deferred.
9. **I/O.** Reads go through a 4 KB aligned block cache, about 6–7 reads per file. The budget is under 50 ms per file.

**Stills (`StillProbe`, Stream overloads only)**
- **DTO:** `dirs.OfType<ExifDirectoryBase>().FirstOrDefault(d => d.ContainsTag(TagDateTimeOriginal))`. The first DNG sub-IFD is the raw image and has no DTO.
- **GPS:** `GpsDirectory.TryGetGeoLocation`. **Model:** from IFD0.
- **Thumbnail:** the IFD0 JPEG range. Placeholder if it's missing; whether ordinary Air 3S DNGs have it is UNVERIFIED.
- **Sets:** use the first frame.

### 6.4 Site zone and local date

1. **Items with GPS:** GeoTimeZone gives an IANA ID, then `TimeZoneInfo.FindSystemTimeZoneById`, which is verified for all test points.
2. **`Etc/*` results** (ocean), in order:
   - the zone of the **nearest land-zone GPS item on the whole card** within 12 h;
   - the `tz` column of the nearest GeoNames place within 60 mi;
   - the PC zone.

   Set `TzFallback`. None of this depends on clustering, so R and G never change a local date.
3. **Items without GPS:** the zone of a GPS item in the same power-on session (`SessionKey.SameSession`), else the nearest GPS item within 12 h, else the PC zone.
4. **Local date:** `ConvertTimeFromUtc(CaptureUtc, tz).Date`. It never comes from the drone clock's own date (§6.1).
5. **Worked examples:**
   - Clip `20260726035000` is 07:50Z, which is **Jul 25** 23:50 AKDT.
   - Zachar Bay 0128: 14:06:27 on the drone clock, 18:06:27Z, **10:06:27 AKDT** on Sep 27 (§6.1).
   - A Makaha clip at 09:30Z falls on Feb 28 in Honolulu (it would be Mar 1 in Alaska).

### 6.5 Flags

| Flag | Rule | UI |
|---|---|---|
| `NoGps` | No fix after the full search | "no GPS" badge; the item is grouped by time only |
| `GpsGuessed` | Generic-search hit that passed the plausibility gate | "GPS guessed" badge with the field path in the tooltip |
| `Truncated` | No `moov`, or a `.trinf` sibling | "Unfinished recording" chip (§7.5) |
| `ClockNotSet` | CaptureUtc before 2015-01-01 or after now + 1 day | Warning, with the time source |
| `CheckDate` | (a) GeoTimeZone gave alternative zones **and** local time is within 60 min of midnight; or (b) the time source is `Mvhd` or any `DroneClock*`, **and** local time is within **75 min** of midnight (covers a 1 h DST error plus Air 3S drift of 23–30 min) | "Check date" chip on the item and its group |
| `TzFallback` | An `Etc/*` fallback or the PC zone was used | Zone badge shown in italics, with a tooltip |
| `ClockFromSetting` | No clock sample on the card | Clock banner: "from settings" |
| `ClockMismatch` | The item has a drone stamp (filename or EXIF DTO; videos with `mvhd` included), its site zone is not the PC-zone fallback (`TzSource.PcZone`), and \|`ClockModel.OffsetAt(stamp, siteZone)` − `siteZone.GetUtcOffset(CaptureUtc)`\| **≥ 15 min** (the drone-clock offset from the learned model at that stamp vs the site zone's offset). Never set in `SiteLocal` mode for an item converted through its own site zone | Info only: the Review InfoBar (§9.2), a "clock ≠ local" chip on the group card (§9.4), the clip time tooltip (§9.5), Info issue `ClockMismatch` (§9.10). It never changes a date, and it does not affect the verdict (§10.5) |
| `ProbeFailed` | Reading metadata threw an error | Item timed from the filename, else mtime; still copyable |

---

## 7. Newness rules

### 7.1 Keys and the library index (listings only)

**Listing**
- Root listings: the video root, the photo root, and every `previousPhotoRoots` entry. When the photo root changes in Settings, the old one is appended to that list automatically.
- Overlaps are removed; for example, the photo root inside the video root is listed once.
- **The ledger folder `<videoRoot>\.uas-sort\` is excluded.** Library roots are listed with `excludeDirNames = {".uas-sort"}`, so the lister neither returns nor enters it, and the index also drops any entry under `LedgerPaths.For(videoRoot)`. Its files never become keys, sizes or set folders. It is never an event folder, never newness evidence, and never input to the watermark. A stray media file placed there is ignored too.
- Fields collected: name, size, mtime and raw attributes, using the explicit enumeration options of §5.

**Keys**
- **Matching key:** `FileKey(NormName, Size)`. `NormName` is the lowercase name with any trailing ` (n)` before the extension removed, so `X (2).MP4` still matches card file `X.MP4` of the same size.
- Other indexes: name → sizes (for conflicts); set folders in any listed root → members (name, size, mtime).

**Event folders**
- An event folder is the nearest ancestor, at any depth, whose name matches `^(\d{4})-(\d{2})-(\d{2})(?:\s+(.*))?$`. The photo-root subtrees and the `.uas-sort` subtree are excluded. Legacy depths (e.g. `2022\2022-03-27 Makaha Valley`) work too.
- Member start times: DJI filename stamp → UTC via the **card's `ClockModel`** (in `SiteLocal` mode through the folder's ledger `tz`, else the stored zone; §6.1). If mtime (true UTC, about start + duration) is earlier than that, or more than 2 h later, use mtime and set a flag. Non-DJI names (Autel `MAX_####`) use mtime.
- A folder's days are its members' local dates in the folder's ledger zone, else in the zone of the group being compared.
- The centroid comes from ledger folder or file records, else from leftover clips on the card that match the folder's files, else it is unknown.

**Watermark (photos only)**
- Watermark = the maximum start UTC of library videos **that have no ledger `file` record**, i.e. videos imported outside the app. Only the listed library counts, and the `.uas-sort` subtree is excluded.
- The app's own copies never advance it, so after adoption it stays fixed at the last manual import.
- As of 2026-09-27 it is **2026-09-27 18:24:16Z**, i.e. 10:24 AKDT (Zachar Bay 0148).
- **No watermark.** If no listed library video lacks a ledger `file` record, `WatermarkUtc` is null, and photo rule 4 (§7.3) treats every unseen photo as after it: **New**, "no videos imported outside uas-sort".

### 7.2 Videos

| Status | Test | Ticked |
|---|---|---|
| Imported | `(NormName, Size)` found in any listed root (evidence `LibraryNameSize`), or a ledger `file` record (`LedgerVerified`; or `LedgerNameSize` when the record says `verify:"nameSize"`), including files culled from the library since. (This is newness only; Card cleanup never treats a ledger record alone as proof that a video is in the library, §10.6) | Hidden, folded into "already in library" (§8.5 fold rule) |
| Decided | A ledger `decision` (only `dismissed` is possible for videos, and only set individually), not revoked | Listed under "Dismissed by you" on the Other tab with [Un-dismiss] |
| Conflict | Same `NormName` with a different size in any listed root or the ledger, and no same-size match | ☐ with the reason. If ticked, it copies as `name (2).ext` (§7.4) |
| New | Anything else | ☑; a Truncated clip is ☐ (§7.5) |

DJI names contain a timestamp, so they are unique. Autel `MAX_####` names repeat across cards, which is why size is part of the key.

### 7.3 Photos and sets (first rule that applies)

1. **Ledger `file` record** (for a set: every member), or a ledger **`decision`** (`assumedImported` or `dismissed`) that hasn't been revoked → **Imported** (evidence Ledger) or **Decided**.
2. **Name and size found in any listed root** (video root, photo root, or previous photo roots).
   - For a set: a set folder exists whose members match the card's by name, size and **mtime ±2 s**: every card member matches and the folder holds nothing else, or the folder holds a superset of the card's members (the card kept only some of them, e.g. after a partial Card cleanup, §10.6). An empty folder doesn't count.
   - → **Imported**.
   - **2b.** The same `NormName` with a different size in any listed root or the ledger, and no same-size match → **Conflict**, unticked (§7.4). For a pair, the DNG decides and the JPG twin follows it, including its `(n)` name when ticked (`X (2).DNG` + `X (2).JPG`).
3. **A ledger `seen` record, with no `file` or `decision` record** → **New**, "not copied on Oct 4" (§10.4).
4. **CaptureUtc later than the watermark, or no watermark (§7.1)** → **New**, "after last imported video, Sep 27 10:24 AKDT" (or "no videos imported outside uas-sort").
5. **Time from the drone clock** (`DroneClock*`) **and within 75 min before the watermark** → **New**, "near the last imported video; time estimated".
6. **Local date has a New video** → **New**, "day has new videos".
7. **Otherwise ProbablyImported**, unticked, with one of these reasons:
   - "videos from this day are already in the library";
   - "photo-only day before the last imported video (Sep 27)".

**Other photo rules**
- **Pairs.** A DNG+JPG pair is decided by the DNG. If the DNG is Imported and the JPG isn't found, the unit is Imported, but the JPG gets its own audit line, **AssumedByRule** ("JPG twin assumed imported with its DNG"). That line caps the verdict (§10.5).
- **Per-day include** toggles every unit on that local date (§9.8).
- **Sets in the ledger.** `file`, `decision` and `seen` records for a set are written **one per member**, each carrying `set:"<SetName>"`. Rules 1 and 3 match a set only when **every** member has such a record (unrevoked, for decisions). A set with records for only some members falls through to the next rule; for example, a 33-frame pano with 5 `assumedImported` members is not Decided.

**The ledger ends the ambiguity.** Photos that were copied (`file`), confirmed by the user (`decision`), or evaluated as New/Conflict/ticked and not copied (`seen`) are decided by rules 1–3 from then on. The heuristic in rules 4–7 applies only to photos the ledger has never seen, and because the watermark is fixed by manual imports, it can't flip a photo seen in an earlier run.

### 7.4 Conflicts (videos and flat photos)

- **What counts as a conflict:** the same `NormName` exists with a different size, anywhere in the listed roots or the ledger, and no same-size match exists (video table §7.2; photo rule 2b §7.3). By default it is unticked, with the text "A different file named X exists: &lt;path>, &lt;size>".
- **If ticked:** the destination becomes `stem (2).ext`, or the next free `(n)` after checking the listings, the ledger and the batch. The existing file is never touched.
- **A target that appears between preflight and rename** is caught by the no-replace rename and becomes a `ConflictAtRename` outcome.

### 7.5 Truncated clips

- **Status:** New, **unticked** by default (default accepted by the user; §15 Q1). The GPS comes from the `mdat` fallback, and the time from the filename via the drone clock.
- **Chip text:** "Unfinished recording. Powering the drone on with this card inserted may repair it (UNVERIFIED on the Air 3S). Rescan afterwards, or tick to copy as-is."
- **If copied,** or if it matches the library by name and size (the library copy is also unfinished, as with Anvil 0014 and 0024), the verdict is at best SafeWithAssumptions: "the drone may still be able to repair it".
- **If later repaired** (different size), it is a normal Conflict. This is the user's own example for the conflict rule (§1.1: unticked, copied as `name (2)` if ticked).

### 7.6 First run (empty ledger)

- An InfoBar explains the photo rule and shows the concrete watermark.
- On the card in hand on 2026-09-27, most DNGs taken before the watermark will be ProbablyImported, because the photo root holds almost no 2026 DNGs. Per-day include and the verdict page's "record as imported" action (individual or per-day selection, §10.5) take care of this.

---

## 8. Grouping & folder decisions

### 8.1 Parameters

| Name | Default | Kind | Evidence |
|---|---|---|---|
| **R**, radius | **50 mi** (80.467 km) | Setting and live slider, 5–100 mi | User decision, approved 2026-09-27. Replay (`docs/research/spikes/grouping/replay50.py`; it uses only the directory listing and GPS already extracted, and opens no files): 8 groups for every tested R from 8 to 33 mi, **7 groups for every tested R from 40 to 60 mi**. At 50 mi, Council Road and Anvil Mountain merge into one group of 25 clips |
| **G**, day gap | **1 day** | Setting and live slider, 0–7 | Consecutive local days join (Kodiak ran 5/22–5/25). Replay: G 1–11 all work |
| **H**, hours guard | 3 h | Constant | Only matters when G = 0; stops a flight that crosses midnight from being split |
| **Near** threshold | 10 mi | Constant, used for day-split emphasis **and** for next-day append confidence | Widest single folder: Nome Roads, 7.8 mi. Closest split the user made: Council → Anvil, 33.1 mi |
| Generic-GPS plausibility | 3 mi first↔last; 500 mi from the card | Constants | §6.3 step 5b |

### 8.2 Clustering (videos only; items in `(CaptureUtc, Id)` order)

1. **Time split.** `(x.LocalDate − prev.LocalDate).Days > G` **and** `x.CaptureUtc − prev.CaptureUtc > H`. `prev` is the previous video.
2. **Library-folder wall.** x is Imported into folder F, and the group already contains items Imported into a different folder F′. Cause: `LibraryFolder`. The folders the user already drew are never crossed.
3. **Distance split.** All of these hold:
   - x has GPS;
   - the group has a centroid (the mean of unit vectors, so it is safe across the antimeridian);
   - x's session is not the session of any **GPS-bearing** member (`SessionKey.SameSession`, §6.3 step 6; the spike's regression rule);
   - `Haversine(x, centroid) > R`.
4. **Items without GPS** never trigger a distance split and never move the centroid. When a distance split fires, each trailing item without GPS:
   - moves with x if it is in x's session;
   - stays if it is in the session of the last GPS item;
   - otherwise goes to whichever side is nearer in time.
5. **No automatic A-B-A re-merge.** Three groups result, and the user merges them.

Every boundary records its cause, the jump distance, the time gap and the day gap.

### 8.3 Boundary chips and day-split suggestions

**Boundary chip text**
- `── 34 mi jump · 21 h ──` (Distance)
- `── 62 days ──` (DayGap)
- `── different library folder ──` (LibraryFolder)
- `── split by you ── [Undo split]` (UserSplit)

**Merge on a chip.** Every chip except LibraryFolder has **[Merge]**. On a LibraryFolder chip, Merge is disabled with the tooltip "These clips are already in two different folders".

**Day-split suggestions** (`DaySplitFinder`)
- Inside each group, every point where consecutive videos' local dates differ gets a suggestion banner in the clip list, drawn at the top of the first clip of the new day: `── Jul 25 → Jul 26 · 34 mi apart · 21 h ── [Split here]`.
- "Apart" is the distance between the two days' centroids.
- At **10 mi or more** the suggestion is **emphasised**:
  - accent colour and a "Likely separate outing" label;
  - for a group that will copy something (its target is NewFolder or Append and at least one of its videos is included), a **Warning** issue that must be acknowledged at preflight. A group that copies nothing (AlreadyImported, NothingToCopy, a Skip pin, or every video unticked) keeps the banner and the chip but raises no issue and needs no acknowledgement;
  - a card-level chip "2 days · 34 mi apart [Split]".
- [Split here] applies `SplitBefore(FirstOfDay)`.

**Council/Anvil at the default setting.** One group, "Jul 25–26", 25 clips, with an emphasised suggestion. One click gives the user's two folders.

**Cross-day append hint** (§8.5). When an append is Medium because the New clips are on a day the folder doesn't have, the group card shows "Different day from 'Council Road' (Jul 25) · 34 mi **[New folder instead]**". The button applies the hint's `QuickFix` as one undo entry.

### 8.4 Calibration at R = 50 mi

| Pair | Distance | Outcome at 50 mi |
|---|---|---|
| Widest folder (Nome Roads, a drive) | 7.8 mi | Stays one group |
| Council Road → Anvil Mountain (consecutive days) | 33.1 mi closest real points; 33.7 mi between centroids; 33.9 mi between the test fixture points | **Merged**, with an emphasised day-split suggestion |
| Safety Roadhouse ↔ Council Road | 20.7 mi, 18 days apart | Split by G |
| Nome Roads ↔ Anvil | 8.8 mi, 22 days apart | Split by G |
| Zachar Bay ↔ Kodiak town, same day | 52.6 mi between the closest real points; **53.4 mi between the test fixture points** | Split. The fixture margin is 3.4 mi, and the test pins it |

### 8.5 Choosing a target (`FolderDecider`; judged on the group's **New and ticked-Conflict** subset)

"F's days" means F's members' local dates, from the listing and the ledger (§7.1). "New" below means the New items plus the ticked Conflict items: inclusion is resolved before `Decide` (§8.9 step 3), and `Decide` receives the `included` set, so ticking a Conflict can turn an AlreadyImported group into an Append. The first matching row wins. The Why texts are the exact templates (`{d}` formatted per §9.13).

| Situation | Target | Confidence, Why |
|---|---|---|
| No New or ticked-Conflict members, and the group has a wall F | AlreadyImported(F). **Folded only if** the group has no Conflict, Truncated or ProbeFailed members; otherwise shown with its chips | – |
| No New or ticked-Conflict members, and no wall (e.g. only unticked Conflicts, only dismissed videos, or only items Imported through ledger records of since-culled files, whose `Imported.Folder` is null) | **NothingToCopy**, summary "nothing to copy: 3 conflicts, 1 dismissed". **Never folded** | – |
| Wall F, and the first New item's local date precedes `F.NameDate` | NewFolder (F still offered; retargeting to it needs the date confirmation below), plus Info `NewBeforeWallFolder` "These clips start before '{F}' (dated {date})" with [Split here] = `SplitBefore(split point)` | – |
| Contains items Imported into F (the wall), and some New item's local date is one of F's days | Append(F) | High, "same day as clips already in this folder" |
| Contains items Imported into F, and **none** of the New items' dates are F's days | Append(F), with `CrossDayHint` "[New folder instead]" = `SplitBefore(split point)` | **Medium**: "different day, {d} from {F.Description}" (distance from the New items' centroid to F's centroid), or "different day, location unknown" |
| No wall; a folder F with `F.NameDate ≤ g.Start ≤ F.End + G days`; both centroids known and ≤ R apart; the New days overlap F's days | Append(F) | High, "same dates, {d}" |
| As above, but the dates are only adjacent (no overlap) and the distance is **&lt; 10 mi** | Append(F) | High, "next day, {d}" |
| As above, dates only adjacent, distance **10 mi–R** | Append(F), with `CrossDayHint` "[New folder instead]" = `Retarget(NewFolderTarget)` | **Medium**, "different day, {d}" |
| One centroid unknown and the dates overlap | Append(F) | Medium, "same dates, location unknown" (badge) |
| Dates only adjacent and a location unknown, or more than R apart | NewFolder (F still offered in the dropdown) | – |
| **The group borders a `UserSplit` boundary, and F is the Wall of the group on the other side, or the pass-1 target of the earlier group across it** (pass 2, §8.9) | NewFolder (F still offered in the dropdown). "Split here" means "separate folder" | – |
| Otherwise | NewFolder | – |

**Split point** of both wall hints: the first New item, in `(CaptureUtc, Id)` order, whose local date is not one of F's days; if that item is the group's first item (the New run comes first), the first item Imported into F instead. It is never the group's first item, so the quick fix is never `Rejected(SplitAtGroupStart)`. Example: New 7/24 clips followed by Imported 7/25 clips in F dated 7/25 → NewFolder with [Split here] before the first 7/25 clip; after it, the 7/25 group is AlreadyImported(F) and the 7/24 group NewFolder.

**Further rules**
- **Tie-break:** the smallest date gap, then the smallest distance.
- **Never auto-append earlier clips to a later-dated folder** (F's name date after g.Start). A manual `Retarget` to such a folder needs the confirmation "Folder is dated Sep 28; these clips start Sep 27", and the edit stores that confirmation.
- **Two groups with the same target:** both get an Info note, "also targeted by &lt;group>; both land in the same folder", with a [Merge] quick fix.
- **Every Medium append** is a Warning that must be acknowledged at preflight.

**Scenario D check** (library lacks Anvil; Council leftovers on the card; R = 50 mi)
- One group: 4 Imported 7/25 clips (the wall is Council Road) plus 21 New 7/26 clips.
- → **Append(Council Road) · Medium**, "different day, 34 mi from Council Road", with [New folder instead] = `SplitBefore(first 7/26 clip)`. The emphasised day split is at the same place.
- After either click, the 7/26 group borders a UserSplit next to Council Road (the neighbour's Wall) → **NewFolder `2026\2026-07\2026-07-26`**; with no suggestion (no PlaceIndex, no ledger folder nearby) the name is blank and Blocking `EmptyFolderName`.

### 8.6 Folder naming

- **Path:** `<videoRoot>\YYYY\YYYY-MM\YYYY-MM-DD <Description>`. Every part comes from the local date of the **earliest video in the group**, so 7/31–8/2 lands in `2026\2026-07\`. An append uses F's real path at whatever depth it is.
- **`Clean(raw)`:**
  1. Replace `<>:"/\|?*` and any character below 0x20 with a space.
  2. Collapse runs of whitespace and trim.
  3. Strip trailing dots and spaces.
  4. Keep at most 80 characters.
  5. Commas are kept ("Newport, RI"). Reserved device names can't occur, because the name starts with the date.
- **Blocking checks:**
  - an empty description on a NewFolder group ("Name this folder");
  - a **temp** path (`final + ".uas-sort.tmp"`, 13 more characters) over 400 characters, OneDrive's limit.
- **Long paths.** Every P/Invoke path gets the `\\?\` prefix, so paths over 260 characters work.
- **Same path as an existing folder** (case-insensitive): it becomes an explicit Append with the note "Folder exists; appending".

### 8.7 Description suggestions (at most 6, deduplicated)

Each suggestion is computed at **each local day's centroid**, in day order. That way a merged two-day group offers both names.

1. The existing folder's description (on Append).
2. Names of ledger folders whose centroid is within 3 mi ("used before, 1.1 mi").
3. A GeoNames named feature within 1.5 mi: mountain, peak, hill, valley, pass, cape, island, peninsula, point, bay, lake, glacier, fjord, cove, lagoon, inlet, sound, strait, harbor, falls, or park ("Anvil Mountain · feature · 0.2 mi").
4. A GeoNames populated place within 3 mi.
5. "near &lt;town>", for the nearest town with population ≥ 1,000 within 30 mi.

- **Prefill:** a NewFolder group is prefilled with the top suggestion from rules 1–4, shown in italics as a suggestion. It is not blocking, and accepting it is a no-op. With no suggestion, the box stays blank, which is blocking. After a UserSplit, a suggestion whose folder path equals a folder excluded from this group (§8.9 step 4) is skipped; with none left the box stays blank. A name the user types that equals that folder still becomes an explicit Append.
- **Data** (`tools/places/build-places.cs` writes `src/UasSort.App/places.bin.gz`; `PlaceIndex.Load` reads it):
  - **Sources**, from the GeoNames dump of 2026-09-27: `US.zip` (every US record of the classes below) plus `cities5000.zip` as the worldwide fallback (populated places only). About 7.7 MB gzipped.
  - **Kept records:** class P (populated places; population from the dump) and these feature codes, mapping the words of rule 3: mountain `MT`, peak `PK`, hill `HLL`, valley `VAL`, pass `PASS`, cape `CAPE`, island `ISL`, peninsula `PEN`, point `PT`, bay `BAY`, lake `LK`, glacier `GLCR`, fjord `FJD`, cove `COVE`, lagoon `LGN`, inlet `INLT`, sound `SD`, strait `STRT`, harbor `HBR`, falls `FLLS`, park `PRK`.
  - **Layout** (little-endian, then gzip): header `"UPLC"`, `u16 version = 1`, `u32 recordCount`, `u16 tzCount`, then `tzCount` zone names (`u8 length` + UTF-8), then per record: `u8 nameLength` + UTF-8 name, `i32 lat × 1e6`, `i32 lon × 1e6`, `u8 class` (0 = populated, 1 = feature), `u8 featureCodeIndex` (into the list above, in that order; 255 for populated), `u32 population`, `u16 tzIndex`. `PlaceIndex` builds its 0.1° grid at load time.
  - Credit "GeoNames CC-BY 4.0" appears in About.
  - **Round-trip test (§14 step 4):** `build-places` → `PlaceIndex.Load` → the nearest feature to the Anvil Mountain clips is "Anvil Mountain" at ≤ 0.2 mi, and to the Zachar Bay clips "Zachar Bay" at ≤ 1.5 mi (rule 3's feature radius; user decision 2026-09-28 — GeoNames places the bay point ~1.0 mi from the flights; the spike's 0.4 mi was the populated village).

### 8.8 Set folders (the user's set-clash rule, §1.1)

```
Resolve(set):
  if ledger has a set with this SetName, identical (member, size) list, and the same first-frame captureUtc → Imported
  plain = SetName ("001_0087")
  for candidate in [plain, $"{plain} {firstFrameLocalDate:yyyy-MM-dd}", "… (2)", "… (3)", …]:
      if candidate taken by another set in this batch                                 → next
      existing = <any listed root>\candidate (listing)
      if !exists or existing is empty                                                 → Plain / DateSuffixed(candidate)
      if existing members == card members (name + size + mtime ±2 s)                  → Imported
      if existing members ⊂ card members, all by name + size + mtime ±2 s, no extras  → Resume(candidate): copy only missing members
      if card members ⊂ existing members, all by name + size + mtime ±2 s              → Imported (e.g. after a partial Card cleanup)
      else                                                                            → clash → next
```

The Photos tab shows each set's folder, e.g. "→ `001_0087 2026-09-27` (001_0087 holds a different set)". The folder name isn't editable.

### 8.9 Edit semantics

**Edits are anchored to items.** A group is "the group containing item X", and `GroupId` is its earliest video. The same edit log therefore replays onto:
- a different R or G (live slider);
- a fresh scan of the same card (draft resume).

**Derive order** (`Planner.Derive(PlanBase, Tuning, edits, SessionFlags, revision, ct)`)
1. **Auto-cluster.** §8.2 on every video, giving an ordered partition and a caused boundary at each auto split.
2. **Structural edits** (`Merge`, `SplitBefore`, `MoveToNewGroup`, `MoveToGroup`) are applied **to that partition, in log order**. They are post-processing, not constraints inside the sequential pass:
   1. An edit whose referenced items are not all in `PlanBase` is **dropped** from this derive and counted (`Cluster(..., out int missingEdits)`; drafts report "3 of 4 edits still apply"). It stays in the log.
   2. `SplitBefore(x)`: the items of x's group ordered at or after x (by `(CaptureUtc, Id)`) form a new group. If x is already its group's first item (an auto boundary at the same point), nothing moves, but the boundary before x takes cause `UserSplit`. `UserSplit` overrides any auto cause at that point.
   3. `Merge(a, b)`: the groups of a and b, **and every group between them in timeline order**, become one group. If a wall (§8.2 rule 2) separates any two of them, the edit is **inactive** for this derive (at `ApplyAsync` time it is `Rejected(MergeAcrossLibraryFolders)`). If a and b are already in one group, it is a no-op.
   4. `MoveToNewGroup(items)`: the items leave their groups and form a new group. `MoveToGroup(items, t)`: the items leave their groups and join the group containing t; if t is among the items, the edit is inactive.
   5. Empty groups are removed. Each group's `GroupId` is its earliest video (its anchor); groups are ordered on the timeline by `(anchor CaptureUtc, anchor Id)`. A group may be non-contiguous in time after moves: its Start/End are the min/max member local dates, its DaySplits come from consecutive members in `(CaptureUtc, Id)` order, and its centroid is the mean unit vector of its GPS members.
   6. **Boundaries** exist only between timeline-adjacent groups L and R, and each gets the first cause that applies: `UserSplit` if an active `SplitBefore` names R's anchor; `LibraryFolder` if L and R have different walls; `DayGap` if §8.2 rule 1 holds between L's last video and R's anchor; `Distance` if R's first GPS video is more than R from L's centroid; otherwise `UserSplit` (the two are apart only because of an edit). [Undo split] on a `UserSplit` chip applies `Merge(L.Anchor, R.Anchor)`, like [Merge].
   7. An edit that changes nothing at this tuning (already satisfied, or inactive) **stays in the log**; only missing-item edits are counted as dropped.
3. **Inclusion:** defaults (New ☑, Truncated/Conflict/ProbablyImported ☐), then `SetIncluded` / `SetDayIncluded` in log order. Inclusion is per item and doesn't depend on targets, so it runs before `Decide`.
4. **Decide, two passes, then Suggest.** Pass 1: `FolderDecider.Decide(g, all, included, lib, tuning, pass1: null)` for every group, **without** the UserSplit row. Pass 2: only groups bordering a `UserSplit` boundary are decided again with `pass1` supplied. The UserSplit row excludes F from group g when F is the `Wall` of g's neighbour on either side of a `UserSplit` boundary, or the pass-1 target of g's **earlier** neighbour across one. The earlier group therefore keeps a shared target and the later one becomes NewFolder; the result never depends on evaluation order. Pins (step 5) never feed the UserSplit row. Then `Suggest` per group.
5. **Pins.** Rename and Retarget are applied, the last one per group winning. Each pin carries `PinnedMembers`, the group's video IDs when the user chose.
   - If the group's current membership differs, the pin still applies and a **Warning** appears: "target chosen for 4 clips; group now has 25". It has [Keep] (re-issues the same pin with the current members) and [Reset to Auto] (`Retarget(AutoTarget)` or `Rename(null)`). It must be acknowledged at preflight.
   - If a merge leaves **two different pins** in one group, that is **Blocking**: "Two choices for this group: 'Council Road' vs 'Anvil Mountain' [Use first] [Use second]".
6. **Issues** (catalogue §9.10).

**Worked examples** (each is a Core test, §13)
- `SplitBefore(Anvil 0001)` at R = 50 (one Council+Anvil group) → 2 groups, chip "split by you". At R = 25 the auto Distance split falls at the same point; the chip still reads "split by you" (cause `UserSplit`). Back at R = 50 → 2 groups, chip "split by you". The edit never leaves the log.
- Synthetic sites A, B and C on one day, 8 mi apart in a line. At R = 10 the auto partition is {A, B}, {C}; `Merge(A₁, C₁)` gives {A, B, C}. At R = 5 the auto partition is {A}, {B}, {C}, and the same edit still gives {A, B, C}, because the group between a's and b's is absorbed.
- `MoveToNewGroup([x])` for a clip x in the middle of a group → the group keeps its other clips (now non-contiguous in time) and keeps its anchor; {x} is ordered after it by its own anchor x, and the boundary between them is `UserSplit` (they are apart only because of the edit).
- A draft edit naming a clip that is no longer on the card → dropped, "3 of 4 edits still apply".

**Refused edits** return `Rejected` with a reason (validated at `ApplyAsync` against the plan that includes every earlier edit):
- merging groups walled to different library folders (`MergeAcrossLibraryFolders`);
- moving an Imported item (`MoveImportedItem`);
- splitting before the group's first item (`SplitAtGroupStart`);
- **renaming a group whose target is Append or AlreadyImported** (`RenameExistingFolder`): "Appending to an existing folder; choose New folder to name a new one";
- retargeting outside the video root (`RetargetOutsideVideoRoot`);
- retargeting into `<videoRoot>\.uas-sort` or anything under it, into the photo root, or into any `previousPhotoRoots` entry, including through Browse existing… (`RetargetIntoReservedFolder`, same InfoBar as §9.4);
- retargeting to a later-dated folder without the confirmation (`RetargetLaterDatedFolderUnconfirmed`);
- an edit naming items that don't exist (`ItemsNotFound`).

**Undo/redo** keeps a stack of `(Tuning, ImmutableList<PlanEdit>)` states.
- A quick fix with several edits is **one** entry.
- A slider drag previews live and becomes **one** entry when it is committed (§9.7).

---

## 9. Review UI (WinUI 3)

### 9.1 Stages (the `ShellVm` state machine)

`Setup → Card → Scan → Review → Commit (Preflight sheet → Copy → Verdict)`, plus the optional `Cleanup (Choose → Not-in-library review → Confirm → Deleting → Result)`, entered from Review or Verdict and followed by a rescan (§10.6)

- **Setup.** Shown on first run and after a settings recovery; the same cards as the Settings page, §9.14.
  - The user confirms the video root and the photo root, each validated with free space shown. **Setup doesn't ask for a ledger folder.**
  - Defaults are derived, never literal:
    - video root = `KnownFolder(Pictures)\UAS Videos` (via `SHGetKnownFolderPath`, which follows OneDrive folder backup);
    - photo root = `<videoRoot>\Picture Offload`.
  - **The ledger folder is always `<videoRoot>\.uas-sort`**, derived from the chosen video root. Setup shows it as a read-only status line:
    - "History: 2 PCs' ledgers found";
    - "No history yet. It will be created at the first offload. If you've used uas-sort on another PC, let OneDrive finish syncing first";
    - cloud-only files, shown Blocking with [Keep on this device].
  - Until confirmed, Offload is Blocking.
- **Card.** Shown only when no card or several cards are found; a single valid card skips straight to Scan.
  - Each volume row shows letter, label, file system, detected kind (DJI card, or "not a DJI card"), media count, and a write-protected badge.
  - Buttons: **Rescan** (F5) and **Browse to folder…** (Windows App SDK `FolderPicker` with `SuggestedStartFolder`; UNVERIFIED unpackaged; the owner comes from `AppWindow.Id`). The result goes through `CardSourceValidator`, and a refusal shows as an InfoBar with the reason.
- **Scan.**
  - Phases: "Listing card" → "Listing library (C:, D:)" → "Reading ledger (2 PCs)" → "Reading metadata 37/214 · DJI_…0128" → "Building plan".
  - A progress bar and Cancel.
- **Review.** Described below.
- **Commit.** §10. During Commit, device-arrival refresh, Rescan, Settings and Browse are disabled. Review comes back read-only on the Verdict page through "Show plan".
- **Cleanup.** §10.6. The same controls are disabled as during Commit, and so are Offload and Undo/Redo. Leaving before Delete deletes nothing. After the run the card is rescanned automatically; the result page shows the recomputed verdict, and Done returns to Review of the rescanned card.

### 9.2 Window chrome

- **TitleBar** (Windows App SDK 2.1): app icon; "uas-sort"; a card chip ("E:\ · DJI Air 3S · serial 1A2B-3C4D · 214 files · 61.3 GB"); Rescan, Undo, Redo, **Clean up card…** (§10.6; disabled with a tooltip giving the reason) and Settings buttons. Settings navigates the Frame to the Settings page.
- **Backdrop:** Mica through `SystemBackdrop`. **Theme** follows the system; the map follows it with `setTheme`.
- **Size:** minimum 1100×700 through `OverlappedPresenter` preferred minimums, scaled by `RasterizationScale`.
- **Clock-mismatch InfoBar** (Warning; dismissible for the session, not persisted; shown again on the next scan while any item has `ClockMismatch`, §6.5). Text, with the clock's offset and zone from the `ClockModel` and the card's site zones from `ClockSummary.MismatchSiteZones`: "Drone clock is set to UTC−4 (America/New_York), but footage on this card was shot in Alaska (UTC−8). Dates here use local time at each site. To fix the drone clock: RC 2 → Settings → System → Date & time → turn off the network-provided time zone and set the zone for where you are flying. Network time zones can be wrong on ship or hotel Wi-Fi, and the RC keeps the last one it saw until it reconnects." Site zones are named from a short table for US zones (Eastern, Central, Mountain, Arizona, Pacific, Alaska, Hawaii), else by IANA ID, each with its UTC offset at the flagged items' times, joined with commas. In `NearestSample` mode the clock is named by its offset only ("set to UTC−4"). It changes nothing in the plan or the verdict.
- **InfoBars** sit under the title bar: clock banner, clock mismatch (above), first-run banner, draft resume ("Resume edits from 14:02? 3 of 4 still apply [Resume] [Discard]"), ledger status (cloud-only / not pinned / parse issues / no history in a changed video root), settings recovery, and device notices.
- **SelectorBar tabs:** **Videos · 3 to offload**, **Photos · 5 days**, **Other · 12**.

### 9.3 Videos tab layout

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

- **Panes.** The two splitters are the app's own `PaneSplitter` (drag, arrow keys); the toolkit `GridSplitter` (Sizers) unboxes a projected `GridLength` and threw on every drag under Native AOT (user report 2026-09-30, Task U1). Pane sizes are remembered in settings.
- **Timeline.** An **ItemsView** with a `DataTemplateSelector` over two **selectable** item kinds: `GroupCardVm` and `FoldedRunVm`.
  - There are **no non-selectable rows.** The boundary chip that precedes a group (with its [Merge] / [Undo split] button) is drawn as the header part of that group's card template.
  - Keyboard navigation, Shift-range selection and clicks therefore only ever hit real items, and the chip buttons stay live.
  - This avoids `ItemContainer.CanUserSelect`. Microsoft Learn documents it only in the "windows-app-sdk-2.0-experimental" API view, and the 1.8 and 2.0 views fall back to that page (checked 2026-09-27).
- **Folded rows.** Runs of AlreadyImported groups fold into one grey `FoldedRunVm` at their place in time. Selecting it shows those clips read-only in the clip list, and an expander unfolds it.

### 9.4 Group card

1. **Swatch** in the map colour. Colours come from a 10-colour palette in timeline order, so neighbours always differ; imported groups are grey.
2. **Description.**
   - **NewFolder groups:** an `AutoSuggestBox` bound to `SuggestionVm` items (x:Bind `ItemTemplate`; `ToString()` returns the text; `UpdateTextOnSelect="False"`, which is in the stable 2.0 API view, checked 2026-09-27). The VM commits a `Rename` from `SuggestionChosen`/`QuerySubmitted`, or on Enter or losing focus. F2 focuses the box.
   - **Append and AlreadyImported groups:** the box is **read-only** and shows the existing folder's description, with the hint "Appending to an existing folder · [New folder instead]".
3. **Action badge:** NEW FOLDER, APPEND, ALREADY IN LIBRARY, NOTHING TO COPY or SKIP, plus a confidence pill. Medium shows its reason.
4. **Target path**, with a `DropDownButton` whose `MenuFlyout` items are **built in code-behind on `Opening`** from the VM's candidate list:
   - Auto (proposed);
   - New folder;
   - Append to candidates, each with its reason ("Sep 27 · 3.1 mi");
   - Browse existing…: `FolderPicker` starting in the video root. A result outside the video root is rejected with an InfoBar "Pick a folder inside UAS Videos"; a result in `<videoRoot>\.uas-sort` (or below it), in the photo root or in a previous photo root is rejected with "That folder is reserved for uas-sort history or photos" (`RetargetIntoReservedFolder`, §8.9);
   - Skip this group.
5. **Local date range** with a zone badge: "Sep 27 10:01–10:24 AKDT". Multiple zones show several badges.
6. **Location:** "near Zachar Bay · spread 1.1 mi", or "no GPS".
7. **Counts:** "Videos 4 new / 13 · 2.1 GB" and "Photos that day: 12 new, 40 probably imported" (links to the Photos tab, filtered).
8. **Thumbnail strip:** up to 8 `tnal` thumbnails, with "+N".
9. **Chips:** unfinished recordings, conflicts, check date, GPS guessed, "clock ≠ local" (any member has `ClockMismatch`; tooltip "The drone clock (UTC−4) doesn't match local time here (UTC−8). Dates use local time."), empty description, cross-day append (Medium), emphasised day split, pin membership changed, conflicting pins.

### 9.5 Clip list

- **Control.** A virtualized **ItemsView** of `ClipRowVm` with `SelectionMode=Extended`.
  - A row that begins a new local day carries a `DaySplitBannerVm` drawn **above the row content, inside the same template**, with [Split here]. The banner is not a separate item.
- **Columns** (a shared column-width header grid):
  - include checkbox;
  - 96 px thumbnail;
  - name;
  - local time with a time-source icon. The tooltip shows UTC, the drone-clock time and the site-local time side by side, each with its offset, then the source: "18:06:27 UTC · drone clock 14:06:27 (UTC−4) · 10:06:27 AKDT (UTC−8) · from the video (mvhd)"; with `ClockMismatch` it adds "Drone clock ≠ local time". Every `TimeSource` value has its icon and source text (a VM test enumerates the enum, §13);
  - size;
  - status pill (New, Conflict, Unfinished, Imported, Dismissed) with a reason tooltip;
  - distance from the group centre in miles.
- **Thumbnails.** The row VM exposes only `ItemId ThumbKey`.
  - In XAML: `<Image local:Thumb.Key="{x:Bind ThumbKey}"/>`.
  - The attached property asks `ThumbnailCache`, which gets bytes from `IThumbnailSource` and decodes them with `BitmapImage.SetSourceAsync(InMemoryRandomAccessStream)` at `DecodePixelWidth=96`.
  - It assigns `Source` **only if** the element's current key still matches when the load finishes, so recycled containers never show the wrong image.
- **Split before.** Hovering a row shows a small "Split before" button. `Ctrl+Shift+S` splits before the focused row (§9.12).
- **Context menu:** Move selected to new group; Move to group…; Include/Exclude; Open in default player (the card file through `IShellLauncher`); Copy path.

### 9.6 Map pane

**Host** (`MapPane`, App)
- Created when Review opens.
- User data folder: `%LOCALAPPDATA%\uas-sort\WebView2` (a temp root under `--selftest`).
- `SetVirtualHostNameToFolderMapping("map.uas-sort.example", <app>\MapAssets, DenyCors)`.
- Settings: default context menu, accelerator keys, zoom control and status bar are off; DevTools only with a debugger attached.
- The user agent gets `uas-sort/<ver>` appended.
- `NavigationStarting` cancels anything outside the virtual host. `NewWindowRequested` sends https attribution links to `IShellLauncher.OpenHttps`. **`DownloadStarting` is always cancelled.**
- `WebMessageReceived` is registered before `Navigate`. `Dispatch` wraps deserialisation in `try/catch (JsonException or NotSupportedException)`, and unknown types are logged.
- After handling a map `click`, the host moves focus back to the clip list so page shortcuts keep working (UNVERIFIED with WebView2 focus).

**Assets**
- `index.html`, `map.js` (our code), `lib/maplibre-gl*` (6.11.2, about 1.2 MB, pinned and vendored by `tools/vendor-maplibre.ps1`).
- `fonts/Noto Sans {Regular,Bold}/{0-255,256-511}.pbf` (about 420 KB).

**MapLibre is ESM-only, and modules load only when served with a JavaScript MIME type.** The first task when the map pane is built (§14 step 11) checks that the virtual host serves `.mjs` as `text/javascript` (UNVERIFIED). If it doesn't, the fallbacks in order are:
1. The vendoring script renames the files to `.js`, rewrites their relative import specifiers, and `map.js` calls `maplibregl.setWorkerUrl('lib/maplibre-gl-worker.js')`.
2. Serve `lib/*` through `AddWebResourceRequestedFilter("https://map.uas-sort.example/lib/*")` with an explicit `Content-Type: text/javascript`, streamed via `IAppAssets`.
3. Leaflet 1.9.4.

The page's error forwarding makes a MIME rejection visible in the host log, and `--selftest` waits for `ready`.

**Messages**
- Every message carries `v:1` and `type`, in source-generated System.Text.Json with `[JsonPolymorphic(TypeDiscriminatorPropertyName="type")]`.
- The context sets **`AllowOutOfOrderMetadataProperties = true`**, because map.js sends `{v, type, …}`.
- `GeoPoint` is written as `[lon,lat]` by a `JsonConverter` registered on the context.

| Direction | Type | Payload |
|---|---|---|
| host→map | `init` | `config{streetsStyleUrl, streetsDarkStyleUrl, satelliteUrl}`, `base`, `radiusMiles`, `online`, `theme` |
| host→map | `setData` | `rev`, `items[{id, groupId, lon, lat, kind}]`, `groups[{id, color, center[lon,lat], label}]`, `jumps[{from, to, label}]`. Sent on every derive; throttled to 10/s during drags |
| host→map | `select` | `groupId`, `itemIds[]`, `fit`, `bbox` |
| host→map | `setRadius` / `setBase` / `setTheme` / `fit` | `radiusMiles` / `base` / `theme` / `bbox` |
| host→map | `ping` | `n` (used by `--selftest`) |
| map→host | `ready` | `maplibre`, `webgl2` |
| map→host | `pong` | `n`, echoed from the `ping` |
| map→host | `click` / `clickEmpty` | `itemIds[]`, `groupId`, `ctrl`, `shift` |
| map→host | `contextMenu` | `itemIds[]`, `x`, `y`; the host opens a WinUI `MenuFlyout` |
| map→host | `tileError` / `baseUnavailable` / `error` | `base`, `message` |

**Value sets:** `base` ∈ {`streets`, `satellite`, `none`} (the toolbar's Off sends `none`, as does the offline fallback); `kind` ∈ {`video`, `videoNoGps`} (a no-GPS video carries its group's centre as `lon`/`lat` and is drawn as a hollow dot; if the group has no centroid, its items are not sent); `color` = `#RRGGBB`; `center`, `from`, `to` = `[lon,lat]`; `bbox` = `[west,south,east,north]`; `theme` ∈ {`light`, `dark`}; ids are `ItemId`/`GroupId` strings; `x`, `y` are CSS pixels relative to the WebView. **Golden examples** (one per message; `MapBridge` tests deserialise exactly these):

```json
{"v":1,"type":"init","config":{"streetsStyleUrl":"https://tiles.openfreemap.org/styles/liberty","streetsDarkStyleUrl":"https://tiles.openfreemap.org/styles/dark","satelliteUrl":"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"},"base":"streets","radiusMiles":50,"online":true,"theme":"light"}
{"v":1,"type":"setData","rev":7,"items":[{"id":"DCIM/DJI_001/DJI_20260927140627_0128_D.MP4","groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","lon":-153.738973,"lat":57.550442,"kind":"video"}],"groups":[{"id":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","color":"#1F77B4","center":[-153.7409,57.5415],"label":"Sep 27 · 13 clips"}],"jumps":[{"from":[-164.2657,64.6935],"to":[-165.3696,64.5627],"label":"34 mi · 21 h"}]}
{"v":1,"type":"select","groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","itemIds":[],"fit":true,"bbox":[-153.76,57.53,-153.72,57.56]}
{"v":1,"type":"setRadius","radiusMiles":25}
{"v":1,"type":"setBase","base":"satellite"}
{"v":1,"type":"setTheme","theme":"dark"}
{"v":1,"type":"fit","bbox":[-165.40,64.55,-164.25,64.70]}
{"v":1,"type":"ping","n":1}
{"v":1,"type":"ready","maplibre":"6.11.2","webgl2":true}
{"v":1,"type":"pong","n":1}
{"v":1,"type":"click","itemIds":["DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"],"groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","ctrl":false,"shift":false}
{"v":1,"type":"clickEmpty","itemIds":[],"groupId":null,"ctrl":false,"shift":false}
{"v":1,"type":"contextMenu","itemIds":["DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"],"x":412,"y":288}
{"v":1,"type":"tileError","base":"satellite","message":"HTTP 503"}
{"v":1,"type":"baseUnavailable","base":"satellite","message":"4 tile errors"}
{"v":1,"type":"error","base":null,"message":"Uncaught TypeError: …"}
```

- **C# computes every distance, label and colour,** so the chips and the map always agree. JS only draws the circle, from `center` and `radiusMiles`, using the same Earth radius.
- **Before updating overlays,** check `map.getSource('items')`, not `isStyleLoaded()`.

**Base maps**
- **Streets:** OpenFreeMap `liberty`, or `dark` in dark mode. No key and no request limits; no prefetching.
- **Satellite:** Esri World Imagery (legacy keyless endpoint; terms UNVERIFIED; §15 Q3). USGS `USGSImageryOnly` (public domain, NAIP 2020 in Alaska) is offered as an alternate satellite URL in Settings, not as a toolbar button.
- **Off:** no base map.
- All URLs live in `settings.json`, attribution is always visible, and the scale bar is imperial.

**Degradation**
- No `ready` within 5 s, or no WebGL 2: the panel shows "Map unavailable" with "Open in browser" (openstreetmap.org at the group centroid). Everything else works.
- **Offline:** detected by `NetworkInformation` or by 4 tile errors. The base switches to `none` (dots, circle, jump lines, scale bar) with an "Offline: base map unavailable" badge.

**Sync**
- Selecting a group card sends `select` + `fit`.
- Clicking a dot selects the clip row and its group; Ctrl-click adds to the selection.
- Selecting clip rows highlights their dots.

### 9.7 Tuning (R and G)

- **Placement:** a strip under the map. R slider 5–100 mi (ticks at 10, 25, 50, 75, 100; step 1 mi); G slider 0–7 days; a live label "7 groups".
- **Preview.** Each `ValueChanged` calls `PlanSession.Preview(tuning)`. `Planner.Derive` runs on the thread pool with **latest-wins cancellation**: each call cancels the previous preview's token (Plan is immutable, so this is safe). A preview never overwrites a plan with a higher `Revision`. Only the collection diff and the map `setData` run on the UI thread through `IUiDispatcher`.
- **Commit, pointer.** `Slider.AddHandler(UIElement.PointerReleasedEvent, handler, handledEventsToo: true)` plus `PointerCaptureLost` commit the tuning as **one** undo entry.
- **Commit, keyboard and wheel.** These have no release, so the tuning commits after **400 ms** without a `ValueChanged`.
- **Your edits survive** because they are anchored to items (§8.9); pin-membership warnings flag any group whose members changed.
- **Reset:** "Reset to 50 mi · 1 day". Saving new defaults happens on the Settings page.

### 9.8 Photos tab

- **Left pane:** an ItemsView of days, oldest first. Each row has:
  - a tri-state checkbox (`SetDayIncluded`);
  - "Jul 25 (Fri) · AKDT";
  - "12 photos · 1 pano set · 0.4 GB";
  - "8 new · 4 probably imported · 2 confirmed by you";
  - the reason text, e.g. "probably imported: videos from this day already in library".
  - Days where everything is already imported collapse behind "Show imported days".
- **Right pane:** the selected day's units in an `ItemsView` + `LinedFlowLayout` thumbnail wall.
  - Each tile has a checkbox, a status pill, and "DNG+JPG" when paired.
  - Sets are single tiles: "Panorama · 33 frames → `001_0087 2026-09-27`", with the clash explanation.
  - "Confirmed by you" tiles have [Undo], which appends a `revoke` record.
- **First-run banner:** "No photo history yet. Photos after Sep 27 10:24 AKDT, within 75 min before it, or on days with new videos are ticked; the others are probably already in Lightroom. You can confirm them on the last screen."

### 9.9 Other tab

- **Unknown files**, including media in unrecognised locations and possible video covers. Text: "Not copied by uas-sort; copy manually if needed". Each is Unaccounted until it is individually marked "not needed" on the Verdict page.
- **Skipped by rule:** grouped by rule and collapsed: LRF, SRT, `.trinf` (with its linked MP4), MISC, system files, and non-media files outside DCIM.
- **Probe errors:** with the message.
- **Scan warnings:** enumeration errors, which force NotSafe.
- **Dismissed by you:** any kind, with [Un-dismiss] (a `revoke` record).

### 9.10 Footer and issues

- **Totals per destination drive,** compared with free space.
- **Issue counters** (⛔ Blocking / ⚠ Warning / ⓘ Info) open a flyout. Each entry links to its group or item and may offer quick fixes: [Merge], [Split here], [Name it], [New folder instead], [Keep], [Reset to Auto], [Use first], [Use second], [Accept and continue], [Keep on this device].
- **Offload ▶** (`Ctrl+Enter`) is disabled while any Blocking issue remains; its tooltip lists them.
- Warnings marked `RequiresAckAtPreflight` reappear on the preflight sheet with checkboxes: Medium appends, emphasised day splits, pin-membership changes, and accepted ledger parse issues.
- **A bad ledger line** is Blocking with **[Accept and continue]** on its InfoBar and flyout entry (the preflight sheet can't be reached while it blocks). The button calls `PlanSession.AcceptLedgerIssues()`: for this session only (not persisted in drafts; raised again on every scan while the line exists) the issue becomes a `RequiresAckAtPreflight` Warning, and the verdict is capped at SafeWithAssumptions.

**Issue catalogue** (`IssueCode`, §3). "Raised by" is the only component that creates the issue; Preflight re-raises the Planner's Blocking issues unchanged. Quick fixes with no `PlanEdit` are UI actions.

| Code | Severity | Ack at preflight | Raised by | Anchor | Message template | Quick fixes (exact edits) |
|---|---|---|---|---|---|---|
| `EmptyFolderName` | Blocking | – | Planner.Derive | group | "Name this folder" | [Name it] (focuses the rename box) |
| `TempPathTooLong` | Blocking | – | Planner.Derive (NewFolder paths), Preflight.Check (every job) | group / item | "Path too long for OneDrive ({n} > 400 characters): {path}" | – |
| `MediumAppend` | Warning | yes | Planner.Derive | group | "Appending to '{F}': {why}" | [New folder instead] = `CrossDayHint.Fix` |
| `EmphasisedDaySplit` | Warning | yes (raised only for a group that will copy something: target NewFolder or Append and ≥ 1 included video) | Planner.Derive | first item of the new day | "{from} → {to} · {d} apart: likely separate outing" | [Split here] = `SplitBefore(FirstOfDay)` |
| `PinMembershipChanged` | Warning | yes while the pin decides what gets copied: a target pin (Skip included) on a group with ≥ 1 included video, a name pin on a group that will copy something; otherwise no | Planner.Derive | group | "{target or name} chosen for {n} clips; group now has {m}" | [Keep] = the same `Retarget`/`Rename` with current `PinnedMembers`; [Reset to Auto] = `Retarget(AutoTarget)` or `Rename(null)` |
| `ConflictingPins` | Blocking | – | Planner.Derive | group | "Two choices for this group: '{a}' vs '{b}'" | [Use first] / [Use second] = that pin re-issued with current `PinnedMembers` |
| `SharedTarget` | Info | – | Planner.Derive | group | "Also targeted by {group}; both land in the same folder" | [Merge] = `Merge(this.Anchor, other.Anchor)` |
| `FolderExistsAppending` | Info | – | Planner.Derive | group | "Folder exists; appending" | – |
| `NewBeforeWallFolder` | Info | – | Planner.Derive | group | "These clips start before '{F}' (dated {date})" | [Split here] = `SplitBefore(split point)` (§8.5) |
| `CheckDate` | Warning | – | Planner.Derive | item | "Check date: {local time} is within {60 or 75} min of midnight ({source})" | – |
| `ClockNotSet` | Warning | – | Planner.Derive | item | "Clock not set: {time} ({source})" | – |
| `ClockMismatch` | Info (never blocking; shown as the Warning-styled InfoBar of §9.2, dismissible per session) | – | Planner.Derive (one issue per plan, from the items' `ClockMismatch` flags) | – | "Drone clock is set to {clock offset} ({clock zone}), but footage on this card was shot in {site zones}. Dates here use local time at each site. To fix the drone clock: RC 2 → Settings → System → Date & time → turn off the network-provided time zone and set the zone for where you are flying. Network time zones can be wrong on ship or hotel Wi-Fi, and the RC keeps the last one it saw until it reconnects." | – |
| `RootMissing` | Blocking if an included job targets the root (the video root always), else Warning | – | Planner.Derive; re-checked by Preflight.Check | – | "{root} is not available; its items are unticked" | – |
| `RootsUnconfirmed` | Blocking | – | Planner.Derive (from `Settings.RootsConfirmed`) | – | "Confirm the video and photo folders (settings were recovered)" | [Open Settings] |
| `LedgerParseIssue` | Blocking; Warning after [Accept and continue] | yes, once accepted | Planner.Derive (from `LedgerSnapshot.ParseIssues`, `SessionFlags`) | – | "Ledger line {file}:{line} can't be read: {reason}" | [Accept and continue] = `PlanSession.AcceptLedgerIssues()` |
| `LedgerCloudOnly` | Blocking | – | Planner.Derive (from `LedgerSnapshot.Status`); re-checked by Preflight.Check | – | "Set `UAS Videos\.uas-sort` to Always keep on this device" | [Keep on this device] = `ILedgerStore.KeepOnDevice()`, then Rescan |
| `LedgerUnwritable` | Blocking | – | Planner.Derive; re-checked by Preflight.Check | – | "Can't write the history file in {folder}" | – |
| `LedgerNotPinned` | Warning (InfoBar only) | – | Planner.Derive | – | "Set `UAS Videos\.uas-sort` to Always keep on this device" | [Keep on this device] |
| `LedgerNoHistory` | Info | – | Planner.Derive; also shown on the sheet | – | "No history yet; this offload starts it" | – |
| `StaleEditsDropped` | Info | – | PlanSession.Resume | – | "{n} of {m} edits still apply" | – |
| `NoHistoryInNewRoot` | Warning | – | Settings page (`SettingsPageVm`) | – | "No history found in {new root}\.uas-sort; copy current ledger there?" | [Copy] = `ILedgerStore.CopyInto`; [Start empty] |
| `CardIdentityChanged` | Blocking | – | Preflight.Check | – | "A different card is in {drive}; rescan" | [Rescan] |
| `CardUnreadable` | Blocking | – | Preflight.Check | – | "The card's top level can't be read" | [Rescan] |
| `OffloadLockHeld` | Blocking | – | Preflight.Check | – | "Another uas-sort window is offloading" | – |
| `AppendTargetGone` | Blocking | – | Preflight.Check | group | "'{F}' was renamed or moved since the scan; rescan" | [Rescan] |
| `LowDiskSpace` | Blocking | – | Preflight.Check | – | "{volume} needs {required} free; {free} available" | – |
| `DuplicateDestination` | Blocking | – | Preflight.Check | item | "Two files would be written to {path}" (a bug guard) | – |
| `LedgerUnlistable` | Blocking | – | Preflight.Check | – | "Can't list {folder}" | – |
| `StaleTempFiles` | Info | – | Preflight.Check | – | "{n} unfinished temp files from an earlier run will be deleted when the offload starts" | – |
| `DestinationAlreadyThere` | Info | – | Preflight.Check | item | "Already at the destination (same size); recorded, not copied" | – |
| `AssumptionsTicked` | Warning | – | Preflight.Check | – | "{n} ticked items rely on assumptions" | – |
| `ProbablyImportedLeftOut` | Warning | – | Preflight.Check | – | "{n} probably-imported photos are left out" | – |
| `NewItemsUnticked` | Warning | – | Preflight.Check | – | "{n} new items are unticked" | – |
| `UnfinishedRecordings` | Warning | – | Preflight.Check | – | "{n} unfinished recordings" | – |
| `ConflictsLeftOut` | Warning | – | Preflight.Check | – | "{n} conflicts are left out" | – |

Scan warnings (`ScanWarning`, e.g. enumeration errors with `ForcesNotSafe`) are not issues; they appear on the Other tab and in the verdict.

### 9.11 Undo/redo and drafts

- **Undo/redo:** `Ctrl+Z` and `Ctrl+Y`/`Ctrl+Shift+Z`, plus the title-bar buttons. When a `TextBox` has focus, these go to the TextBox's own text undo (§9.12). The history holds everything since the scan, including slider commits and quick fixes as single entries.
- **Drafts:**
  - `drafts\<DraftKey>.json` is saved 1 s after the last change. Drafts are per-PC.
  - When the same card is scanned again (`DraftKey` matches), the app offers to resume. If the `InventoryHash` (§4.2 CardClassifier) also matches, every edit applies. If it differs, the draft is still offered; the dropped count is the number of edits whose referenced `ItemId`s are missing from the new scan ("3 of 4 edits still apply"), and pins whose membership changed raise warnings (§8.9). Edits that are merely inactive at the current tuning are not counted as dropped.
  - A draft is deleted after an offload with no failures.

### 9.12 Keyboard

**Page-level `KeyboardAccelerator`s (modified keys only)**

| Keys | Action |
|---|---|
| Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z | Plan undo/redo. Skipped when `FocusManager.GetFocusedElement(XamlRoot) is TextBox`, so text undo works |
| Ctrl+M | Merge the selected group with the next group |
| Ctrl+Shift+N | Move the selected clips to a new group |
| Ctrl+1 / 2 / 3 | Switch tabs |
| F5 | Rescan (disabled during Commit) |
| Ctrl+Enter | Offload |

**List-scoped keys** (the list's `KeyDown` / `PreviewKeyDown`, acting **only** when the focused element is an item container, never a TextBox)

| Keys | Where | Action |
|---|---|---|
| ↑ / ↓ | Timeline, clip list | Native ItemsView navigation (items only; chips are not items) |
| Tab | Timeline | Move into the clip list |
| F2 | Timeline | Focus the rename box |
| Space | Clip list, photo wall | Include/exclude, marked Handled so the container doesn't also toggle selection. If the built clip list shows the container still toggles, Space is left to the row's focused CheckBox |
| Ctrl+Shift+S | Clip list | Split before the focused clip |
| Esc | Flyouts | Native close |

A smoke test types a space into the rename box and checks that `Included` is unchanged.

### 9.13 Units and formatting

| What | Format |
|---|---|
| Distances | Always miles: under 0.1 → "&lt;0.1 mi"; under 10 → one decimal ("7.8 mi"); otherwise whole numbers ("34 mi") |
| Sizes | Decimal units ("31.4 GB", "7 MB") |
| Times | Site local, with the zone abbreviation. A time estimated from the drone clock is prefixed "~" |
| Dates | "Jul 25–26" |
| Clip lengths | `mvhd` duration as "m:ss" ("3:42"), or "h:mm:ss" from one hour ("1:02:05") |

### 9.14 Settings (a **Page** in the Frame, built with toolkit `SettingsCard`s)

The Setup stage shows the first three cards: video root, photo root, and the read-only ledger status.

- **Video root and photo root:** folder pickers with free-space readout. If the photo root is inside the video root, a note says this is supported. Changing the photo root appends the old one to `previousPhotoRoots`, which is shown read-only with [Forget].
- **Ledger (history):** there is no ledger-folder setting. The folder is always `<videoRoot>\.uas-sort` and moves with the video root.
  - The card shows the derived path read-only, plus its status: synced / pinned / cloud-only files / other PCs' ledgers found. Buttons: [Keep on this device] and [Open].
  - It also shows the local backup folder for this video root, `%LOCALAPPDATA%\uas-sort\ledger-backup\<root key>\` (`LedgerPaths.BackupDir`), with [Open].
  - **When the video root changes, the ledger moves with it.**
    - If `<new root>\.uas-sort\` has no `ledger*.jsonl`, a warning asks: "No history found in `<new root>\.uas-sort`; copy current ledger there?" with [Copy] and [Start empty].
    - **Order:** the new video root is saved to `settings.json` first, so the guard's derived own-file path (`LedgerPaths.OwnFile(newRoot, MACHINE)`) and the backup subfolder are the new root's before [Copy] writes anything.
    - [Copy] calls `ILedgerStore.CopyInto`, which calls `EnsureFolder()` (creates the folder if missing and pins it). It then appends every record of the ledger currently loaded (the union from the old root, all PCs, with each record's `id` and `machine` kept) to this PC's own `ledger-<MACHINE>.jsonl` there, and to the new root's mirror. Dedupe by `id` keeps the union exact.
    - If the old root is unavailable, the records are the latest local snapshot **∪ the mirror** of the old root's backup subfolder, deduped by `id`. The mirror matters because Verdict-page decisions and revokes are written after the Start-offload snapshot.
    - [Start empty] leaves the new root without history. The first-run photo rules (§7.6) then apply. **If the new root's listing already holds files matching the current ledger's `file` keys (name + size)**, a confirmation comes first: "N videos here were copied by uas-sort; starting empty treats them as manual imports and may mark un-copied photos as probably imported. [Copy] is recommended." (Starting empty there would move the watermark to the latest of those videos and could turn photos that were New into ProbablyImported, the failure of review finding #16.)
    - The old `.uas-sort` folder is left untouched.
- **Grouping defaults:** R in miles and G.
- **Drone clock:** the last learned clock (§6.1), editable: **Follows local time at each site** (`SiteLocal`) or **Fixed zone** with an IANA zone picker (default `America/New_York`), plus the learned status ("learned from 13 videos on Sep 27"). The stored zone is the learner's candidate 2; the stored mode and zone are used on their own only when a card has no videos to learn from (`Setting` mode). A successful run overwrites them with what it learned.
- **Copy the JPG twin** (on by default).
- **Map:** default base; streets, dark streets and satellite URLs under an "Advanced" expander, where USGS is a preset.
- **About:** versions, and attributions for OpenFreeMap/OpenStreetMap, Esri, USGS, GeoNames CC-BY, and MapLibre BSD-3.

---

## 10. Offload engine

### 10.1 Compile (`OffloadCompiler.Compile(Plan)`)

- **Jobs:** one per file of each included unit.
  - Videos go to the group target.
  - Photos go to `<photoRoot>\name`, plus the twin when `copyJpgTwin` is on.
  - Sets go to `<photoRoot>\<folder>\member`. `Resume` placements copy only the missing members.
- **Skipped:** groups with target `SkipGroup` or `NothingToCopy`, Imported items and Decided items.
- **Ticked conflicts** get their `(n)` name.
- **`CreatesFolder`** is true only for NewFolder targets and new set folders under the photo root (`Plain` and `DateSuffixed` placements; never `Resume`).
- **`SeenIfNotCopied`** lists every photo or set unit that is New, Conflict, or ticked. Those not copied by the end get `seen` records (§10.4).
- **Order:** groups by start time; within each group, videos by capture time; then photos by capture time; then sets.

### 10.2 Preflight (`Preflight.Check` → the sheet)

**Blocking**
- `CurrentIdentity()` differs from the scan identity: "A different card is in E:; rescan".
- The card listing's top level can't be read.
- The offload lock is held by another window.
- A root that an included job targets is missing, not a directory, or under the card. The video root always counts, because it holds the ledger. A missing root that no included job targets is not Blocking: its items stay unticked, with the reason shown and newness unknown (§12).
- The ledger folder `<videoRoot>\.uas-sort` can't be listed, holds a cloud-only `ledger*.jsonl` (with [Keep on this device]), or isn't writable. A folder that is merely missing is not Blocking: **Start offload** creates it (§4.4). If creating it fails, the run stops before the first copy.
- Settings were recovered and the roots haven't been confirmed.
- **An Append target folder no longer exists:** "folder renamed or moved since the scan; rescan".
- There isn't enough free space on a destination volume. Each volume needs `Σ + max(1 GiB, 2 % of Σ)` free, where Σ is the total size of that volume's jobs. Worked value: Σ = 31.4 GB → 31.4 + max(1.074, 0.628) = **32.47 GB** free required.
- Two jobs share one destination path (a bug guard).
- A temp path is over 400 characters.
- The plan has Blocking issues.

**Actions** (`Preflight.Check` itself writes nothing)
- Stale `*.uas-sort.tmp` files are looked for **only in the job destination directories** that already exist (a non-recursive listing of each). They are listed on the sheet (`StaleTempFiles`, Info) and deleted by `DeleteOwnTemp` when the user presses **Start offload**; **Back** deletes nothing.
- A destination that already exists with the same size becomes **AlreadyThere**, shown as Info.

**Sheet**
- Lists the folders to create and the folders being appended to (with paths and confidence), the files and bytes per drive, and the free space after.
- **Acknowledgements (checkboxes; Start is disabled until all are ticked):**
  - each Medium append;
  - each emphasised day split still in the plan;
  - each pin whose membership changed;
  - ledger parse issues accepted with [Accept and continue].
- Warnings:
  - N ticked items rely on assumptions;
  - N probably-imported photos are left out;
  - N new items are unticked;
  - N unfinished recordings;
  - N conflicts left out;
  - no ledger history exists yet in `<videoRoot>\.uas-sort` (Info: "this offload starts it").
- There is no "not pinned" warning on the sheet: **Start offload** pins the folder through `EnsureFolder()` in any case. The scan-time InfoBar (§11) still shows it.
- Buttons: **Start offload** and **Back**.

### 10.3 Per-file protocol (sequential; one card read per file)

0. **Check the card identity.** `ICardReader.CurrentIdentity()` must match the pinned identity. Otherwise the outcome is `CardSwapped`, the run stops, and the remaining jobs are `NotStarted`.
1. **Re-check the card file.** `ICardReader.Stat`: if its size or mtime differs from the scan, the outcome is `ChangedOnCard` and the file is not copied.
2. **Check the destination.**
   - Exists with the same size → `AlreadyThere`, and a ledger `file` record is written with `verify:"nameSize"`, `xxh128:null`.
   - Exists with a different size → `ConflictAtRename`.
3. **Folder.**
   - `EnsureDirectory(dir, allowCreate: job.CreatesFolder)`, lazily, the first time a file lands there.
   - For NewFolder targets it may create the folder and its `YYYY` and `YYYY-MM` parents. For Append targets it only checks that the folder exists, and fails the job otherwise.
4. **Create the temp file.** `CreateTemp`: `<destDir>\<final name>.uas-sort.tmp`, `CreateNew`, `PreallocationSize = size`, attributes Hidden | NotContentIndexed. The `.tmp` suffix keeps it from syncing to OneDrive (per Microsoft's docs; UNVERIFIED on this account).
5. **Copy.** Read the card sequentially in 1 MiB `ArrayPool` buffers, feed `XxHash128.Append`, and write. The byte count must equal the size.
   - **Card read error** (an `IOException` from the card stream): re-read that chunk once at the same offset. If that fails too, call `CurrentIdentity()`: if the volume is gone or different, stop the run (`CardRemoved` or `CardSwapped`, table below); otherwise delete the temp, record `Failed(Copy)` and continue with the next job.
6. **Flush.** `FlushToDisk` (`FlushFileBuffers`).
7. **Verify.**
   - Re-open the temp file with `File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, (FileOptions)0x20000000 /* FILE_FLAG_NO_BUFFERING */)` and read with `RandomAccess.Read` into 4096-aligned 1 MiB `NativeMemory.AlignedAlloc` buffers. If .NET rejects the undefined flag (UNVERIFIED), a `CreateFileW` P/Invoke is used instead.
   - Compare the hash. If the unbuffered open or read fails, re-read buffered and set `VerifyMode.Cached`.
   - Unbuffered reading was verified on NTFS; on exFAT D: it is UNVERIFIED.
   - On a mismatch: delete the temp file and retry once from step 4. A second mismatch is `Failed(Verify)`.
8. **Finalise.** `FinalizeAttributes`: copy the card's CreationTimeUtc and LastWriteTimeUtc, and **clear Hidden before the rename**.
9. **Rename.** `RenameNoReplace`: `MoveFileExW(\\?\tmp, \\?\final, MOVEFILE_WRITE_THROUGH)` without `MOVEFILE_REPLACE_EXISTING`.
   - The write-through flag is kept because the approved Safety graft names a write-through rename (§1.1). It is harmless, but Microsoft documents it as mattering only for cross-volume copy-and-delete moves, so **nothing relies on it for durability**.
   - `ERROR_ALREADY_EXISTS` or `ERROR_FILE_EXISTS` means `TargetExists`: delete the temp file and record `ConflictAtRename`.
10. **Confirm.** `ConfirmFinal(final, size)` (metadata only). If the name or size is wrong, the outcome is `Failed(Confirm)`.
11. **Ledger.** Append a `file` record and flush it. If the write fails, **stop the run**; the file itself remains and will show as NameSizeMatch next time.
12. **Progress** is reported at 10 Hz: files and bytes done, MB/s (rolling 5 s), ETA, current file, phase, and group.

**Outcomes per cause** (fault-injection tests assert every row)

| Cause | In-flight file | Remaining jobs | `OffloadResult.Stop` |
|---|---|---|---|
| Identity mismatch at step 0 | `CardSwapped` | `NotStarted` | `CardSwapped` |
| Card read error, card still present (after one chunk re-read) | temp deleted; `Failed(Copy)` | continue | – |
| Card read error, card gone | temp deleted; `Failed(Copy)` | `NotStarted` | `CardRemoved` |
| Card read error, a different card | temp deleted; `CardSwapped` | `NotStarted` | `CardSwapped` |
| Destination disk full | temp deleted; `Failed(<phase>)` (`CreateTemp`, `Copy` or `Flush`) | `NotStarted` | `DestinationFull` |
| Destination root lost | temp deleted if reachable; `Failed(<phase>)` | `NotStarted` | `DestinationLost` |
| Cancel (checked between chunks and between jobs) | temp deleted; `Cancelled` (a job not yet begun stays `NotStarted`) | `NotStarted` | `Cancelled` |
| Ledger append fails (step 11) | stays renamed and confirmed: `Verified` (or `AlreadyThere`), without a ledger line; next scan shows NameSizeMatch | `NotStarted` | `LedgerWriteFailed` |
| Hash mismatch twice | temp deleted; `Failed(Verify)` | continue | – |
| Card file changed (step 1) | `ChangedOnCard` | continue | – |
| Target appears before rename | temp deleted; `ConflictAtRename` | continue | – |
| Wrong name or size after rename | `Failed(Confirm)` | continue | – |
| `UnsafeIoException` | temp deleted if it is ours; `Failed(<phase>)` | `NotStarted` | `InternalSafetyStop` |

In every case the `seen` records (§10.4) and the `run` record are still written if the ledger is writable, and the report says what finished.

**After the loop**, including after Cancel or a stop:
- For every destination volume that is not NTFS on a fixed disk (e.g. exFAT D:), `FlushDestination` runs FlushFileBuffers on each renamed file (re-opened with write access, never read; tracked as created this run) and on each destination directory handle (`FILE_FLAG_BACKUP_SEMANTICS`). Both are UNVERIFIED on exFAT; the rehearsal offload (acceptance step 4, §13) tests them on a scratch folder on D:.
- Those volumes are listed in `VolumesNeedingSafeRemoval`.

**Invariants**
- A file gets its final name only after it has been verified, so a crash can leave only `*.uas-sort.tmp` files, which the next preflight lists and Start offload deletes.
- **Keep awake:** `IPowerRequest.KeepSystemAwake("Offloading drone media")` for the whole run. **Offload lock** held for the whole Commit.
- **No thumbnails during a copy:** `IThumbnailSource.Pause()` for the whole Commit stops thumbnail reads from the card and closes the cached card handles, so placeholders show.

### 10.4 Ledger writes during Commit

Every record carries `id` (a GUID, used to deduplicate across files and OneDrive conflict copies), `machine`, `v:1` and `run`.

- One `file` record per Verified file (`verify:"unbuffered"` or `"cached"`) or AlreadyThere file (`verify:"nameSize"`).
- After each group, a `folder` record: created or appended, description, centroid, local date range, zone.
- `folder` records with `source:"cardLeftovers"` for existing folders whose location was learned from leftover clips on the card. This is how a location survives formatting the card.
- **At the end of every Commit, including Cancel, failures and stops,** one `seen` record per unit in `SeenIfNotCopied` that wasn't copied; for a set, one per member, each with `set:"<SetName>"` (§7.3).
- One `run` record: card identity, video and photo roots, verdict, counts.
- A `torn` record only from `OpenOwn()`, when the own file lacks its final `\n` (§11).

Nothing is written to the ledger before Commit, except explicit user actions: Verdict-page decisions (one `decision` per member for a set), Un-dismiss/Undo (`revoke`, one per revoked decision), the video-root [Copy] (§9.14), and Card cleanup (one `cardDelete` per deleted card file, §10.6). All of these go to this PC's own `<videoRoot>\.uas-sort\ledger-<MACHINE>.jsonl` and its local mirror (`LedgerPaths.BackupDir`), through `ILedgerWriter.Append`.

### 10.5 Audit and verdict (`CardAudit`)

**Before auditing**
1. Re-check `CurrentIdentity()`. A mismatch makes the verdict **NotSafe**: "The card in E: is not the one that was offloaded".
2. Re-list the card (listing only, under 1 s) and diff it against the inventory on `(RelPath, Size, MtimeUtc, CreationUtc, Attributes)`, excluding `System Volume Information`.
   - Any added, removed or changed entry is **Unaccounted**, "changed since scan".
   - Differences only in LastAccessTimeUtc are listed as "OS updated last-access times" and don't count against the verdict (§13, acceptance step 3).

**Per-file categories.** Every card file gets exactly one category:

| Category | When |
|---|---|
| VerifiedThisRun | Verified in this run |
| InLedger | Ledger `file` record with `verify:"unbuffered"` or `"cached"` |
| ConfirmedByYou | A ledger `decision` (`assumedImported` or `dismissed`), not revoked |
| NameSizeMatch | Imported by a library listing, AlreadyThere, or a ledger record with `verify:"nameSize"` (**never** InLedger) |
| SkippedByRule | A named Skip rule (§5), or a JPG twin not copied because `copyJpgTwin` is off ("JPG twin: copying disabled in Settings") |
| AssumedByRule | ProbablyImported and not copied, or a JPG twin assumed imported with its DNG |
| Unaccounted | New or Conflict and not copied; Failed; ChangedOnCard; CardSwapped; Cancelled; NotStarted; ConflictAtRename; Unknown; a probe error that wasn't copied; changed since scan |

**A unit's category is the worst of its files**, in this order: Unaccounted > AssumedByRule > SkippedByRule > NameSizeMatch > ConfirmedByYou > InLedger > VerifiedThisRun. Examples:
- a 33-frame set with one Failed member is Unaccounted;
- a verified DNG whose JPG twin failed is Unaccounted;
- a Resume set mixing NameSizeMatch and VerifiedThisRun members is NameSizeMatch.

**Verdict levels**
- **NotSafe** if anything is Unaccounted, the identity changed, or any `ForcesNotSafe` scan warning remains. Example: "Don't format yet: 1 file failed, 2 unfinished recordings not copied".
- **SafeWithAssumptions** if anything is AssumedByRule; or a Truncated clip was copied or matched by name and size; or ledger parse issues were accepted ([Accept and continue], §9.10). Example: "Safe, with assumptions: 40 photos were assumed already imported".
- **Safe** otherwise.
- **The headline names the card and splits the evidence**, e.g. "E: · DJI Air 3S · serial 1A2B-3C4D: Safe to format: 17 verified, 4 matched by name+size only". Buffered verifications are counted too.
- **If `VolumesNeedingSafeRemoval` isn't empty,** the verdict adds "Safely remove D: before formatting the card" with an **[Eject D:]** button (`IDeviceEject`).

**Verdict page**
- The verdict, with a count for each category (expandable to per-file lines).
- One row per group with **Open folder**. **Open photo root** (for the Lightroom import).
- The "Not copied" list. **Nothing is preselected.**
  - **[Record selected photos as already imported]** writes `decision` records of kind `assumedImported`. Selection is per tile, per day or all at once; it covers photos and sets only.
  - **[Mark selected as not needed]** writes `decision` records of kind `dismissed`.
    - **Photos and sets** can be selected individually or per day. The per-day buttons wrap onto as many lines as the dates need, so every day stays reachable at the minimum window size.
    - **[Select all photos and sets]** selects every photo and set that can be decided, across all days: exactly what clicking every day button selects. It never selects a video, an unknown file or a row that can't be decided, and it clears a selected video or unknown file, as a day button does. **[Clear selection]** deselects everything. Each is disabled when it has nothing to do.
    - **Unknown files, truncated clips and all other videos only one at a time.** Truncated clips are videos, and bulk selection is never offered for videos. Unknown files usually have no local date, so they are never selected per day either.
  - Both actions open a confirmation dialog showing counts by kind and total GB. Then they append to the ledger and recompute the verdict.
  - **[Undo]** on the page, and later [Un-dismiss] on the Other and Photos tabs, append `revoke` records.
- **Report** is saved automatically, and there is a button to open it.
- **[Clean up card…]** opens Card cleanup (§10.6) for this card, with this run's `OffloadResult`.
- **Done** goes back to the Card stage.

### 10.6 Card cleanup (`CleanupPlanner`, `CleanupExecutor`, `ICardEraser`; added 2026-09-27)

The user asked for this on 2026-09-27 (§1.1). It is the only code path that deletes anything on the card. The offload (§10.1–10.5) still never writes the card, and `ICardReader` stays read-only. Types: §3 (card cleanup); ports: §4.1; guard rules: §4.3 rules 1b and 2.

**Where it starts, and when it can't.** **[Clean up card…]** sits on the Verdict page (§10.5) and in the title bar (§9.2).

| Condition | [Clean up card…] | Tooltip |
|---|---|---|
| A card has been scanned (Review, or the Verdict page after a Commit) and passes the cleanup volume check | enabled | — |
| Commit is running, or a scan is running | disabled | "Wait until the offload finishes" / "Wait until the scan finishes" |
| The volume is write-protected (`CardSource.IsWriteProtected`, from `FILE_READ_ONLY_VOLUME`) | disabled | "The card is write-protected (lock switch)" |
| The source came from **Browse to folder** (`CardSource.IsBrowsedFolder`) | disabled | "Cleanup works only on a detected card. A browsed folder could be a backup copy." |
| The volume fails the cleanup volume check (below) | disabled | "This doesn't look like a drone card (it may be a backup drive)" |
| The card was removed, or no card has been scanned | disabled | "Rescan first" |

- **Decision: detected card volumes only, and only real card media.** A browsed folder may be a copy of a card on a PC or a backup drive, where deleting "old files" would destroy the backup. Detection (§5) deliberately ignores DriveType, so a USB SSD, a spare SD card or a thumb drive whose root holds a copied card passes it; the cleanup volume check is therefore stricter than detection.

**Cleanup volume check.** Core's `CleanupVolumeCheck.Refusal(VolumeInfo, ListingResult card, Settings, string appDataDir)` decides the button; `WindowsCardEraserFactory.Open` repeats every check from Win32 through its own `IVolumeFacts` (§4.1) and never trusts `CardSource` flags or a caller's `GuardContext`.
1. The source is not a Browse result (`CardSource.IsBrowsedFolder` false; the factory ignores this flag and relies on 2–6).
2. The root is a volume root: `GetVolumePathNameW(root) == root`.
3. The file system is exFAT or FAT32 (`GetVolumeInformationW`), and the volume identity equals the pinned one.
4. The bus: removable media on any bus (SD, MMC, USB, or a PCIe/SCSI card reader); fixed media is refused (user decision 2026-10-04). `IOCTL_STORAGE_QUERY_PROPERTY` (`StorageDeviceProperty`) reports `RemovableMedia` true on any bus (`BusTypeSd`, `BusTypeMmc`, `BusTypeUsb`, `BusTypeScsi`, `BusTypeUnknown`, …), or the bus is `BusTypeSd` or `BusTypeMmc`. A fixed USB SSD or hard disk, a SATA disk or an NVMe drive (`RemovableMedia` false) fails. (Why: a laptop's built-in SD slot is often a PCIe card reader, e.g. the Realtek RTS5208, which reports `BusTypeScsi` or `BusTypeUnknown` with removable media; the earlier SD/MMC/USB-only rule refused every card in it.)
5. The volume is not the system, boot or paging volume, and no configured root, previous photo root or `%LOCALAPPDATA%\uas-sort` lies on it.
6. The drone-written index is present: `MISC\FC*.db` or a `MISC\IDX\` folder (both seen on the Air 3 in the research; which the Air 3S writes is confirmed in first-card acceptance, §13; UNVERIFIED).

`CleanupVolumeCheck.Evaluate(…)` returns `(string? Refusal, string? Detail)`; `Refusal(…)` is its first half. Detail names the failing rule and its facts ("rule 3: file system NTFS", "rule 4: bus Scsi, removable media false", "rule 5: E:\Photos lies on this volume", "rule 6: no MISC\FC*.db or MISC\IDX"); the factory's refusal message names the same rule. Because a disabled button shows no tooltip, the reason is also shown as visible text beside [Clean up card…] (the title bar and the Verdict page); for a volume that fails this check it is "This doesn't look like a drone card (rule 4: bus Scsi, removable media false)". Each change of the availability for a card source is logged once (Info, deduplicated by source root and reason).

A thumb drive holding a full clone of a card, `MISC` included, can still pass; the summary names the volume ("E: · SD card · exFAT · 256 GB · serial 1A2B-3C4D") so the user can see what is about to be changed.

**Preparation** (when the page opens; nothing is written)
1. `ICardReader.CurrentIdentity()` must equal the scan identity. Otherwise a Blocking InfoBar says "A different card is in E:; rescan", with [Rescan]. From here on the identity is pinned.
2. `ICardReader.Relist()`, then `CardAudit.Audit(inventory, relisted, now, plan, offloadResult, ledger)`. `offloadResult` is this session's Commit result when the page is opened from the Verdict page, else null. Every card file gets its audit line, and `CardChanges` (files added, removed or changed since the scan) feed the eligibility rules.
3. **Fresh evidence:** list the video root, the photo root and every `previousPhotoRoots` entry (`excludeDirNames = {".uas-sort"}`; listings only), then `ILedgerStore.Check()` and `Load()`. The proofs below come from these listings, not from the scan-time index, which may be old. An unavailable root contributes nothing. `CloudOnly`, `Unwritable` or `VideoRootMissing` shows a Blocking InfoBar (as for Commit) and disables Delete.
4. `ICardReader.Space()` → `CardSpace` (free bytes, total bytes, cluster size).
5. `CleanupPlanner.Candidates(inputs)` builds one candidate per unit, plus the Never list of files outside any unit. Every change of mode, date, target, switch or row then calls `Build`, which is pure and takes milliseconds, so the summary updates live.

**Eligibility** (per file, first match). A unit's eligibility is the worst over its **primary** files (the MP4; the DNG, or a lone JPG, and its JPG twin unless twin copying is off; every set member) in the order Evidence &lt; NotInLibrary &lt; Never, and it is Never if any companion is Never. A companion otherwise inherits its unit's eligibility, so it can never make a unit eligible. "Listed" below means that a fresh listing of any library root (Preparation step 3) holds `(NormName, size)`.

| # | Per-file state | Eligibility | Reason text |
|---|---|---|---|
| 1 | A directory | Never (an emptied set folder is removed after its members, below) | — |
| 2 | Added, removed or changed since the scan (`CardChanges`), or `ChangedOnCard` in this run | Never | "changed since the scan" |
| 3 | Under a path with an enumeration error (a `ForcesNotSafe` `ScanWarning`) | Never | "part of the card couldn't be read" |
| 4 | `FILE_ATTRIBUTE_READONLY` set | Never | "marked read-only on the card" (the app never clears attributes) |
| 5 | Class `Unknown` | Never | "unknown file" |
| 6 | Its unit has a `ProbeError` | Never | "its metadata couldn't be read" |
| 7 | Class `Skip` and not a companion (§5) | Never | "DJI system file" / "system file" / "not tied to a clip" |
| 8 | A companion (§5), or a JPG twin that is SkippedByRule because twin copying is off | inherits the unit; counted in the summary as a file uas-sort never copies | — |
| 9 | Its unit is a Truncated video | NotInLibrary (`Unfinished`) | "unfinished recording; the drone may still repair it", or with a name+size match "unfinished (a same-size copy is in your library; the drone may still repair the card copy)" |
| 10 | `ConfirmedByYou`, `dismissed` | NotInLibrary (`Dismissed`) | "you marked it not needed on Oct 4" |
| 11 | `ConfirmedByYou`, `assumedImported` | NotInLibrary (`RecordedAsImported`) | "you recorded it as imported on Oct 4; not verified" |
| 12 | A video that is `VerifiedThisRun`, `InLedger` or `NameSizeMatch`, and Listed | Evidence (`Listed`) | "copied and verified today" / "in the history, verified" / "same name and size in the library" |
| 13 | A video that is `VerifiedThisRun`, `InLedger` or `NameSizeMatch`, not Listed | NotInLibrary (`NoLongerInLibrary`) | "copied on Sep 27, no longer in your library" |
| 14 | A photo, JPG twin or set member that is `VerifiedThisRun`, `InLedger` or `NameSizeMatch`, and Listed | Evidence (`Listed`) | as row 12 |
| 15 | A photo, JPG twin or set member, not Listed, with a fresh ledger `file` record of `verify` `unbuffered` or `cached` | Evidence (`HistoryOnly`) | "copied and verified on Sep 27; Lightroom may have moved it" |
| 16 | A photo, JPG twin or set member, not Listed, with only a `nameSize` ledger record | NotInLibrary (`NoLongerInLibrary`) | "matched by name and size on Sep 27, no longer in your library" |
| 17 | `AssumedByRule` | NotInLibrary (`ProbablyImported`) | "probably imported, not proven" |
| 18 | `Unaccounted` with newness `IsNew`, including this run's `Failed`, `Cancelled`, `NotStarted` and `ConflictAtRename` | NotInLibrary (`New`) | "new: not in your library" |
| 19 | `Unaccounted` with newness `Conflict` | NotInLibrary (`Conflict`) | "a different file named X is in the library" |
| 20 | Anything else | Never | "not proven either way" |

- **Row 9** applies even when the clip matches the library by name and size: the library copy is unfinished too, and the card copy is the one the drone may repair (§7.5). This is a recorded exception to the user's name/size rule (§1.1).
- **Rows 10–11:** a Verdict-page decision settles the format verdict; it is not consent to delete. It may be months old, made on another PC through the shared ledger, or made per day in bulk (photos), so those files go through the review list like any other file not proven to be in the library.
- **Rows 12–16:** the ledger is history, not the library's current state. A video's clip may have been deleted from the library, lost in a sync incident, or moved with a renamed event folder, and then the card holds the only copy; so a video needs a current listing. Photos and set members may rely on a verified ledger record, because Lightroom moves them out of the photo root (§1.1); the summary counts them apart.
- The unit's `Evidence` is the worst audit category among its primary files and its `EvidenceSource` is `Listed` unless any primary file is `HistoryOnly`; both go into the `cardDelete` record and select the re-check below.
- **Ticked for offload.** A NotInLibrary unit that is in `CleanupInputs.Plan.Included` and was not copied (no `Verified` or `AlreadyThere` outcome in this session) has `TickedForOffload = true`. Its review row starts on **Keep** with the badge "ticked for offload" (the user's own plan says to copy it), and [Delete all] leaves it alone; only its own toggle can set it to Delete.
- NotInLibrary units are deleted **only** when the switch "Also delete files not in my library" is on **and** the unit's review row is set to Delete.

**Units and delete order**

| Unit | Files, in delete order | Folder |
|---|---|---|
| Video | its companions (`<stem>.LRF`, `<stem>.SRT`, `.<stem>.MP4.trinf`, the `.avc1` journal, and `<stem>.JPG` once the "video cover" rule is on), then the MP4 | never removed |
| Photo | the JPG twin if present, then the DNG (a lone JPG is its own primary) | never removed |
| Set | its members, in ordinal name order | `DCIM\PANORAMA\<set>` or `DCIM\HYPERLAPSE\<set>`: `RemoveDirectoryW` after the last member, only if the folder is then empty |

- Deleting the primary file last means an interrupted unit always keeps its primary file on the card, never an orphan companion (which rule 7 would make undeletable later).
- `DCIM`, `DCIM\DJI_###`, `PANORAMA`, `HYPERLAPSE` and `MISC` are never removed, even when empty. Nothing is deleted recursively.

**Order and size**
- Units are ordered by `CaptureUtc` (§6.2; a set uses its first frame), then by `ItemId` (ordinal). Both modes use this order.
- `Allocated(file) = ⌈size / ClusterBytes⌉ × ClusterBytes`, and 0 for an empty file. A unit's `AllocatedBytes` sums all its files, companions included. exFAT frees whole clusters, so this is what a delete gives back (directory entries aside); the result page shows the real free space, re-read afterwards.
- Sizes and targets use decimal GB (10^9 bytes, §9.13).

**Mode 1: Before date**
- A `CalendarDatePicker` with no default. Continue stays disabled until a date is picked.
- **The cutoff day** is the picker's own calendar date: `Before = DateOnly.FromDateTime(picker.Date.Value.DateTime)`. It is never converted through `UtcDateTime`, `ToLocalTime` or any other zone, and every cutoff text is formatted from that same `DateOnly`, so the day shown is the day kept.
- In range: units whose `LocalDate` (site-local, §6.4; never the PC's date) is **strictly before** the chosen day. The chosen day itself is kept.
- Deleted: in-range Evidence units, plus in-range NotInLibrary units when the switch is on and their row is on Delete.
- **Cutoff line**, worded from the plan: "Deletes 152 files captured before Jul 26, 2026 (local time at each site) that are in your library; Jul 26 and later are kept. 14 older files are kept (see 'Kept')." With not-in-library rows included: "…that are in your library, plus 9 you reviewed…". It says "Deletes everything captured before Jul 26, 2026" only when the Kept list is empty.
- **Midnight.** Clip `20260726035000` (07:50Z, Jul 25 23:50 AKDT) is before Jul 26 and goes; the next clip of the same flight at Jul 26 00:10 AKDT stays. When a flight (`SessionKey.SameSession`) straddles the cutoff, the summary adds "A flight continues past the cutoff (Jul 26 00:10 AKDT); its later clips are kept" (`CleanupCutoff.FlightContinuesLocal`).

**Mode 2: Free space**
- The user's words ("clear a specific amount of free space") read two ways, so both are offered as `RadioButtons` above one `NumberBox` (one decimal, GB):
  - **Free up [X] GB** (`FreeSpaceGoal(FreeUp, X)`): target free = `FreeBytes + X`;
  - **Have at least [X] GB free** (default; `FreeSpaceGoal(HaveFree, X)`): target free = `X`, at most the card's total.
- Next to it: "E: 12.4 GB free of 256.1 GB" and a live "will delete ≈ Y GB" (in the second option also "= free up ≈ Y GB").
- The sequence S is every unit, in the order above, that is Evidence, or NotInLibrary with the switch on and its row on Delete or undecided. Never units and Keep rows are passed over; they don't end the walk.
- The plan is the **shortest prefix** S₁…S_k with `FreeBytes + Σ_{i≤k} AllocatedBytes(S_i) ≥ target`.
  - k = 0 when the card already has the target free: "E: already has 73.8 GB free; nothing to delete".
  - **Unreachable** (`FreeBytes + Σ S < target`): the plan is all of S, and `FreeSpaceShortfall` drives the message: "Only 41.0 GB can be freed; 12.3 GB is held by files that are not in your library", with [Include files not in my library], which turns the switch on. When the switch is already on, or no NotInLibrary bytes are involved: "Only 41.0 GB can be freed; 3.1 GB is held by files uas-sort never deletes (unknown files, changed files, DJI system files)". The Delete button still works: it deletes everything deletable, after the same acknowledgements.
- **Cutoff.** The last unit S_k is the cutoff: "Deletes 143 files · 58.2 GB · captured Jul 3 – Aug 30 14:22 AKDT → cutoff: Aug 30 14:22 (3 of 9 files from Aug 30)".
  - The range runs from the first deleted unit's local date to the cutoff's local date and time. If the two ends are in different zones, each shows its own abbreviation.
  - "3 of 9 files from Aug 30" counts card files (companions included) of the units whose local date is the cutoff's day: those the plan deletes, of all of them.
- **In range** (for the Kept list and the review list) means at or before S_k in the order above.

**The not-in-library switch and the review list**
- The switch "Also delete files not in my library" starts off on every visit and is never remembered.
- When it is on, the review list is **required**: Confirm can't be reached without it, and it can't be collapsed away. It holds one `CleanupRowVm` per NotInLibrary unit in range, oldest first, each with:
  - a thumbnail (`Thumb.Key`, the same `IThumbnailSource` as the clip list);
  - the capture's local date, time and zone ("Jul 26 20:20 AKDT"; "~" for drone-clock times);
  - the clip length: `Mp4Info.Duration` as "3:42"; a truncated clip "unfinished · ~7 MB"; photos "photo"; sets "panorama · 33 frames" or "hyperlapse · 240 frames";
  - the location: `PlaceLabel` from the PlaceIndex, in the §8.7 order (a feature ≤ 1.5 mi, a populated place ≤ 3 mi, "near &lt;town>" ≤ 30 mi), as "near Anvil Mountain · 0.2 mi"; else the coordinates to 4 decimals, "64.5627, -165.3709"; else "no GPS";
  - the size, and the reason (the row 9–19 text: new, probably imported, conflict, unfinished, not needed, recorded as imported, no longer in your library), plus the "ticked for offload" badge where it applies;
  - a **Keep/Delete** toggle, with [Keep all] and [Delete all] above the list.
- **Initial state.** When the list is first shown, every row starts on **Delete** because the user opted in, except "ticked for offload" rows, which start on Keep.
- **Rows that join later.** A row that enters the list after it was first shown (another date, another target, or a Keep that extends the Free-space prefix) arrives **undecided** with the badge "new in range". The Delete button then reads "Decide 2 new rows" and stays disabled until each is set to Keep or Delete, or [Delete all] is pressed again; the list scrolls to the first undecided row. An undecided row counts as Delete for the Free-space walk (so the range doesn't keep growing) but is never deleted.
- **Recompute.** Every toggle rebuilds the plan. In Free-space mode a Keep row leaves S, so the prefix may reach later units and move the cutoff later. A row that leaves the range keeps its toggle for when it returns.

**Summary and acknowledgement** (`CleanupPage`)
- **Summary:**
  - the mode and the cutoff line (the date, or the computed date and time with zone);
  - the card: "E: · SD card · exFAT · 256 GB · serial 1A2B-3C4D";
  - counts by kind (videos, photos, sets); files (companions included) and GB (allocated); the captured range; free space now → after (≈);
  - the evidence split: "140 in the library listing · 12 photos found only in the history (Lightroom may have moved them)";
  - the files uas-sort never copies, on their own line: "Also deletes 38 files uas-sort never copies: 20 JPG twins (copying is off in Settings), 12 LRF proxies, 6 SRT captions".
- **"Kept (older than the cutoff but not deletable)"**, an expander of the in-range units the plan doesn't delete (Never, NotInLibrary with the switch off, or rows set to Keep), grouped by reason with one line per unit or file (`CleanupKept`), plus a collapsed line "Never touched: `MISC` (DJI index), system files" with its count.
- **Checkboxes:** "Files deleted from a memory card can't be recovered." and, when the plan holds any NotInLibrary unit, "Includes 9 files not proven to be in your library." (the card files of those units).
- **The button** says "Delete 152 files (61.4 GB)". It is disabled while a box is unticked, a row is undecided, the plan is empty, or a Blocking InfoBar shows (identity changed; ledger folder cloud-only, unwritable or video root missing; the volume check failed). Another window's offload lock is not probed here; execution step 1 reports it.
- **Fingerprint.** XxHash64 (16 hex digits) over the request, `SpaceBefore`, the sorted lines `relPath|size|mtimeTicks` of every file in `Delete`, and the sorted `ItemId`s of the NotInLibrary units in `Delete`. The checkboxes are bound to it, so any recompute that changes it clears them.
- It is a Page in the Frame because the review list can be long. Its own dialogs (for example "Stop the cleanup after the current file?") go through `IDialogService`.

**Confirm** (`CleanupPlan.Confirm(CleanupAck, TimeProvider)`, the only factory of `ConfirmedCleanupPlan`)
- `CleanupPlan` is a sealed **class** whose constructor is `internal` to Core and called only by `CleanupPlanner.Build`, so no other code can build one or copy one with `with`.
- `Confirm` **recomputes** the fingerprint from the plan's content and never trusts the stored field. It throws `InvalidOperationException` (a VM bug) unless all of these hold:
  - the recomputed fingerprint equals both `Fingerprint` and `ack.PlanFingerprint`;
  - `ack.CantBeRecovered`; `Delete` isn't empty; `Undecided` is empty;
  - no candidate in `Delete` is Never;
  - every NotInLibrary unit in `Delete` is in `ack.NotInLibraryDelete`, that set equals the NotInLibrary units in `Delete`, `Request.IncludeNotInLibrary` is true whenever there is one, and `ack.IncludesNotInLibrary` is true exactly then;
  - every file path, canonicalised, lies under `<CardRoot>\DCIM\`, and every set folder is a `DCIM\PANORAMA\<set>` or `DCIM\HYPERLAPSE\<set>` folder.
- It returns a `ConfirmedCleanupPlan` with a new `Token` (GUID), `ConfirmedUtc` (from the `TimeProvider`), the canonical `FilePaths` and `SetFolders`, and `NotInLibraryConfirmed` (the per-unit confirmation tokens).
- `ConfirmedCleanupPlan` is also a class with an `internal` constructor (`InternalsVisibleTo` only for Core.Tests). Neither type is serialised; a confirmation lives only in memory and dies with the page.

**Execution.** `CleanupExecutor.RunAsync(ConfirmedCleanupPlan, CleanupEnvironment, IProgress<CleanupProgress>, CancellationToken)` runs on a background thread, reports progress at 10 Hz, and **owns every step below**; `CleanupEnvironment` (§3) carries the source, the pinned identity, the reader, the thumbnail source, the eraser factory, the lister, the ledger store, the offload lock, the power request, the `TimeProvider` and the settings. The rescan and the report belong to the caller, `CleanupVm` (after the loop, below).

Before the first delete:
1. `IOffloadLock.TryAcquire()`: if another window holds it, the run returns at once with `Stop = OffloadLockHeld` ("Another uas-sort window is offloading"), every unit `CleanupNotStarted`, and nothing deleted. Then `IPowerRequest.KeepSystemAwake("Cleaning up drone card")`. Both are held until the run ends.
2. `IThumbnailSource.Pause()`: card thumbnail reads stop and the reader's cached card handles are closed, so no handle of ours holds a file being deleted. Disposed at the end.
3. `ILedgerStore.Check()`: `CloudOnly`, `Unwritable` or `VideoRootMissing` returns with `Stop = LedgerUnavailable` before anything is deleted. Then `EnsureFolder()`, `SnapshotToBackup(runId)`, and only then `OpenOwn()`, so the writer is opened after the folder exists.
4. Fresh evidence: list the video root, the photo root and every `previousPhotoRoots` entry (`excludeDirNames = {".uas-sort"}`), and `Load()` the ledger union. An unavailable root contributes nothing.
5. `ICardEraserFactory.Open(source, pinned, confirmed)`, which re-runs the cleanup volume check from Win32 and builds the eraser's own `GuardContext` with `Cleanup = confirmed`; a refusal is `Stop = InternalSafetyStop` with nothing deleted.

For each unit in plan order: first steps 0 and 1 for each of its files and step 2 once for the unit; then, for each file in delete order, steps 0, 1, 3 and 4. After a set's last member, `RemoveEmptySetFolder`.

| Step | Check or action | On failure |
|---|---|---|
| 0 | `ICardReader.CurrentIdentity()` equals the pinned identity | `CleanupCardSwapped` (or `PartiallyDeleted` if part of the unit is gone); stop `CardSwapped`; the rest `CleanupNotStarted` |
| 1 | `ICardReader.Stat`: the file exists with the size and mtime of the preparation re-list | `SkippedChanged` before the unit's first delete; `PartiallyDeleted` after it |
| 2 | Evidence re-check for Evidence units (table below). NotInLibrary units skip it, but the unit must be in `NotInLibraryConfirmed` | `SkippedEvidenceGone`. A NotInLibrary unit without its token → `CleanupFailed` and stop `InternalSafetyStop` (Confirm makes this unreachable) |
| 3 | `ICardEraser.DeleteFile` → `DeleteFileW(\\?\E:\DCIM\…)`. Removable media have no Recycle Bin, and `DeleteFileW` never uses it | See the outcome table |
| 4 | Append and flush a `cardDelete` record **immediately after** the successful delete | Stop `LedgerWriteFailed`; the file is gone and the report names it |

- **Why step 4 comes after the delete:** a record written first would claim a delete that might then fail. Written after, a crash between the two loses at most one audit line, and the rescan shows the truth. The records are informational only (below).

**Evidence re-check** (listings and the ledger only; no library file is ever opened)

| `EvidenceSource` at plan time | Videos | Photos, JPG twins, set members |
|---|---|---|
| `Listed` | The fresh listing of any library root still holds `(NormName, size)`. A ledger record alone is never enough | The fresh listing still holds `(NormName, size)`, or the fresh ledger has a verified (`unbuffered`/`cached`) `file` record (Lightroom may have moved it since the plan) |
| `HistoryOnly` | — (a video is never `HistoryOnly`) | The fresh ledger still has the verified `file` record |

Every primary file of the unit must pass; companions follow their unit.

**Outcomes** (fault-injection tests assert every row)

| Cause | Unit in flight | Remaining units | `CleanupResult.Stop` |
|---|---|---|---|
| Offload lock held elsewhere (before step 0) | `CleanupNotStarted` | `CleanupNotStarted` | `OffloadLockHeld` |
| Ledger folder cloud-only, unwritable, or video root missing (before step 0) | `CleanupNotStarted` | `CleanupNotStarted` | `LedgerUnavailable` |
| Identity mismatch (step 0) | `CleanupCardSwapped`, or `PartiallyDeleted` | `CleanupNotStarted` | `CardSwapped` |
| File changed or gone (step 1) | `SkippedChanged`, or `PartiallyDeleted` | continue | – |
| Evidence gone (step 2) | `SkippedEvidenceGone` | continue | – |
| `ERROR_WRITE_PROTECT` (the lock switch moved) | `CleanupFailed`, or `PartiallyDeleted` | `CleanupNotStarted` | `WriteProtected` |
| Card gone (`ERROR_NOT_READY`, `ERROR_DEVICE_NOT_CONNECTED`, or `CurrentIdentity()` fails after an error) | `CleanupFailed`, or `PartiallyDeleted` | `CleanupNotStarted` | `CardRemoved` |
| `ERROR_ACCESS_DENIED`, `ERROR_SHARING_VIOLATION` or any other delete error | `CleanupFailed`, or `PartiallyDeleted` | continue | – |
| The emptied set folder can't be removed (`ERROR_DIR_NOT_EMPTY` or another error) | `Deleted` with `SetFolderRemoved = false`, noted "folder kept" | continue | – |
| The `cardDelete` append fails (step 4) | the file counts as deleted: `Deleted` if it was the unit's last file, else `PartiallyDeleted` | `CleanupNotStarted` | `LedgerWriteFailed` |
| Cancel (checked between files) | `PartiallyDeleted` if mid-unit | `CleanupNotStarted` | `Cancelled` |
| `UnsafeIoException` from the guard, or the eraser factory refuses the volume | `CleanupFailed`, or `PartiallyDeleted` (`CleanupNotStarted` if the factory refused) | `CleanupNotStarted` | `InternalSafetyStop` |

A failed file always stops the rest of its unit. The unit's remaining files stay on the card, and the rescan audits them as usual: a partial set's remaining members form a smaller set (Imported when a matching library set folder holds them, §8.8), and a video whose companions went first still has its MP4.

**After the loop** (also after a stop or Cancel)
1. The executor calls `ICardReader.Relist()` and `Space()`. A deleted file that is still listed (another program held it open, so Windows removes it when that program closes it) goes into `StillListed`.
2. The executor disposes the eraser, the ledger writer and the thumbnail pause, releases the offload lock and keep-awake, and returns the `CleanupResult`.
3. `CleanupVm` asks `ShellVm` to rescan the card (the normal Scan stage, `ScanService.ScanAsync`), which recomputes the plan and the verdict for what is left.
4. `CleanupVm` builds the report `reports\<yyyyMMdd-HHmmss>-<run8>-cleanup.json` (`CleanupReport`: every planned file with its outcome, eligibility, evidence, reason, error, and whether its `cardDelete` record was written, plus free space before and after and the verdict after the rescan) and saves it with `IReportStore.Save(CleanupReport)`. If the rescan fails (card pulled), the report is still written, with the verdict after as `NotSafe`.
5. **Result page:** "Deleted 152 files (61.4 GB) · E: now has 73.8 GB free"; skipped and partial units with reasons and the files still on the card; anything in `StillListed`; the recomputed verdict; "Safely remove the card before putting it back in the drone. The drone may show old thumbnails until it rebuilds its index; formatting in the drone is the clean option (§15 Q8)", with **[Eject E:]** and [Done].
- **Durability comes from safe removal.** [Eject E:] (`IDeviceEject`, `CM_Request_Device_Eject`) flushes the volume. The app never opens a write or flush handle on the card volume or a card file (a whole-volume `FlushFileBuffers` needs a write-capable `\\.\E:` handle and administrator rights, so it was dropped on 2026-09-27).

**Ledger `cardDelete` records** (`CardDeleteRecord`, §3; example in §11)
- One per deleted file, companions included, written right after its delete. Fields: name, size, card relative path (`src`), unit, `captureUtc`, evidence, the reason text, the mode, the card identity (`RunCard`), the set name, and the cleanup's run id.
- `evidence` is the primary file's category (`VerifiedThisRun`, `InLedger`, `NameSizeMatch`), prefixed `historyOnly:` for a photo found only in the history; `notInLibraryConfirmed` for a reviewed NotInLibrary unit; or `companionOf:<the unit's evidence>` for a companion or a JPG twin that uas-sort never copies.
- They are informational: loaded into `LedgerSnapshot.CardDeletes` and read by no rule. They change no newness, watermark or evidence of other files. A later scan of the same card simply no longer lists the deleted files.
- A cleanup writes no `run` record (those describe offloads and feed settings recovery).

**What Card cleanup never does**
- Delete anything the confirmed plan doesn't name, or anything on a browsed folder, a volume that fails the cleanup volume check, or a write-protected card.
- Open a card file for writing, open a write or flush handle on the card volume, clear an attribute, or change a time.
- Remove a directory other than an emptied set folder, delete recursively, or touch `MISC`, `LOST.DIR` or system files.
- Treat a Verdict-page decision, or a ledger record alone for a video, as proof that a file is in the library.
- Open a library file (evidence comes from listings and the ledger).
- Run from the CLI, or without the offload lock.

---

## 11. Persistence

- Per-PC state lives under `%LOCALAPPDATA%\uas-sort\`, outside OneDrive.
- **The ledger lives in `<videoRoot>\.uas-sort\`** (user decision, 2026-09-27). On this PC that is `C:\Users\damia\OneDrive\Pictures\UAS Videos\.uas-sort\`.
  - The folder is derived from the video root and is not a separate setting. It syncs to every PC with the library, it is pinned *Always keep on this device*, and it moves with the video root (§9.14).
  - Its backup mirror and snapshots stay **local**, in `%LOCALAPPDATA%\uas-sort\ledger-backup\`, one subfolder per video root (`LedgerPaths.BackupDir`: XxHash64 of the lowercase canonical video root, 16 hex digits), so a root change never mixes two roots' records.
- Binaries live in `%LOCALAPPDATA%\Programs\uas-sort\<version>\`.

| Item | Path | Format | Written | Crash safety |
|---|---|---|---|---|
| Settings | `settings.json` | JSON, `schema:1` | On change (debounced 500 ms) | Write a temp file, then `File.Replace` with a `.bak`. If unreadable: use derived defaults, keep the bad file as `.corrupt-<ts>`, set `RootsConfirmed=false` (**Blocking until the user confirms the roots**), and offer to restore the roots from the latest ledger `run` record. That record comes from the local backup mirrors (the latest `run` across every `ledger-backup\<root key>\`), because the ledger folder itself hangs off the lost video root. It may also come from `<derived default video root>\.uas-sort\`. `Load(readOnly: true)` (the CLI, §4.5) does none of this: it never renames, creates or writes, and returns the derived defaults |
| Ledger | `<videoRoot>\.uas-sort\ledger-<MACHINE>.jsonl` (append; the only file the app writes there). **Reads the union of every `ledger*.jsonl`** directly in `<videoRoot>\.uas-sort\`, including other PCs' files and OneDrive conflict copies, deduplicated by record `id` | JSON Lines, `t` discriminator, `v:1` | During Commit, on explicit decision/undo/[Copy] actions, and during Card cleanup (`cardDelete`, §10.6) | Append and `FlushFileBuffers` per line. The own file is opened with `FileShare.Read`, so there is a single writer. **Torn tail repair:** if the own file doesn't end in `\n` (a crash mid-append), `OpenOwn()` first appends `\n` and a `torn` record naming that line's number N; readers skip line N of that machine's file without a parse issue. A torn **final** line of any file is skipped too. **Any other bad line is a Blocking issue until [Accept and continue]** (§9.10), and the verdict is then capped at SafeWithAssumptions |
| Ledger backup | `%LOCALAPPDATA%\uas-sort\ledger-backup\<root key>\` (**local**, never under the library or synced; one subfolder per video root) | A live mirror of the own file (appended together with it) plus `snapshots\<yyyyMMdd-HHmmss>-<run8>\` of all ledger files at each Start offload and at the start of Card cleanup's deletes (keep 20 per root) | Mirror: during Commit and on every own-file append (decisions, revokes, [Copy], `cardDelete`). Snapshots: at Start offload and before Card cleanup's first delete | Restorable by hand; Settings has [Open]. Also the source for settings recovery and for [Copy] when the old video root is gone (latest snapshot ∪ mirror, deduped by `id`) |
| Drafts | `drafts\<DraftKey>.json` | `{v, cardKey, inventoryHash, savedUtc, tuning, edits[]}` | 1 s after a change | Temp file, then replace |
| Reports | `reports\<yyyyMMdd-HHmmss>-<run8>.json`; Card cleanup: `reports\<yyyyMMdd-HHmmss>-<run8>-cleanup.json` | Full run report: settings snapshot, plan summary, outcome per file, audit lines, card diff, verdict. Cleanup: `CleanupReport` (request, cutoff, every planned file's outcome, the Kept list, free space before and after, the verdict after) | End of each run or cleanup | Written once |
| Logs | `logs\uas-sort-<yyyyMMdd>.log` | Text | Always | Kept 14 days |
| WebView2 | `WebView2\` | WebView2's own data, including the HTTP tile cache | By WebView2 | — |
| Thumbnails | Memory only | Decoded images, LRU of 400 | — | Re-read from the card; about 12.7 KB each |
| Selftest | `%TEMP%\uas-sort-selftest-<guid>\` (settings, a temp video root whose `.uas-sort\` holds the test ledger, WebView2) | — | Only under `--selftest` | Deleted at exit |

**Ledger folder status** (`ILedgerStore.Check()` → `LedgerFolderStatus`, §3; attributes and security descriptors only, before every load; `Writable` comes from the ReadOnly attribute plus `GetFileSecurityW` + `AccessCheck` for `FILE_ADD_FILE` on the folder (or the video root, if the folder is missing) and `FILE_APPEND_DATA` on the own file, which reads the security descriptor with `READ_CONTROL` and never opens data)
- **`VideoRootMissing`:** Blocking, because the root itself is missing (§10.2).
- **`Missing`** (`<videoRoot>\.uas-sort` doesn't exist): Info `LedgerNoHistory`, "No history yet". The folder is created (the folder only) and pinned by `EnsureFolder()` on **Start offload**, or when Card cleanup starts deleting. If creating it fails, Commit stops before the first copy, and a cleanup before the first delete.
- **`Empty`** (the folder exists without any `ledger*.jsonl`): Info `LedgerNoHistory`, as above.
- **`NotPinned`** (in a sync root, `Pinned` false): Warning InfoBar `LedgerNotPinned`, "Set `UAS Videos\.uas-sort` to *Always keep on this device*", with [Keep on this device]. Start offload pins it anyway, so the preflight sheet doesn't repeat it.
- **`CloudOnly`** (any `ledger*.jsonl` in `CloudOnlyFiles`): Blocking `LedgerCloudOnly`, with [Keep on this device] (§4.3). Never opened until it is local.
- **`Unwritable`:** Blocking `LedgerUnwritable` for Commit and for Card cleanup.
- **Video root changed, and `<new root>\.uas-sort\` has no `ledger*.jsonl`** (the new root's `Check()` gives `Missing` or `Empty` while the loaded snapshot has records): Warning `NoHistoryInNewRoot` on the Settings page, "No history found in `<new root>\.uas-sort`; copy current ledger there?", with [Copy] and [Start empty] (§9.14).

**`settings.json`**. The values shown are this PC's; defaults are derived at Setup, never literal. **There is no `ledgerDir` key.** The ledger folder is always derived as `<videoRoot>\.uas-sort` (here `C:\Users\damia\OneDrive\Pictures\UAS Videos\.uas-sort`).

```json
{ "schema": 1, "rootsConfirmed": true,
  "videoRoot": "C:\\Users\\damia\\OneDrive\\Pictures\\UAS Videos",
  "photoRoot": "C:\\Users\\damia\\OneDrive\\Pictures\\UAS Videos\\Picture Offload",
  "previousPhotoRoots": [],
  "radiusMiles": 50, "gapDays": 1, "droneClockMode": "Zone", "droneClockZone": "America/New_York", "copyJpgTwin": true,
  "map": { "base": "streets", "streetsStyleUrl": "https://tiles.openfreemap.org/styles/liberty",
           "streetsDarkStyleUrl": "https://tiles.openfreemap.org/styles/dark",
           "satelliteUrl": "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
           "satellitePresets": { "Esri": "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                                 "USGS": "https://basemap.nationalmap.gov/arcgis/rest/services/USGSImageryOnly/MapServer/tile/{z}/{y}/{x}" } },
  "layout": { "timelineWidth": 380, "mapHeightRatio": 0.45 } }
```

**Ledger records** (one line each; examples)

```json
{"t":"file","v":1,"id":"6d0e…","machine":"DESKTOP-A","run":"8f1c…","at":"2026-09-27T21:07:02Z","kind":"video",
 "name":"DJI_20260927140627_0128_D.MP4","size":89612345,"src":"DCIM/DJI_001/DJI_20260927140627_0128_D.MP4","root":"video",
 "dest":"C:\\…\\UAS Videos\\2026\\2026-09\\2026-09-27 Zachar Bay\\DJI_20260927140627_0128_D.MP4",
 "xxh128":"5e0c…","verify":"unbuffered","mtime":"2026-09-27T18:08:01Z","captureUtc":"2026-09-27T18:06:27Z","timeSource":"Mvhd",
 "lat":57.5504421,"lon":-153.7389730,"tz":"America/Anchorage","localDate":"2026-09-27",
 "sessionUtc":"2026-09-27T17:59:28Z","serial":"<drone-serial>","set":null}
{"t":"folder","v":1,"id":"…","machine":"DESKTOP-A","run":"8f1c…","path":"C:\\…\\2026-09-27 Zachar Bay","desc":"Zachar Bay",
 "source":"created","lat":57.5415,"lon":-153.7409,"start":"2026-09-27","end":"2026-09-27","tz":"America/Anchorage"}
{"t":"seen","v":1,"id":"…","machine":"DESKTOP-A","run":"8f1c…","at":"2026-10-04T20:11:00Z","name":"DJI_20261002…_0131_D.DNG",
 "size":27399100,"src":"DCIM/DJI_001/…","captureUtc":"2026-10-02T22:40:12Z","status":"New","why":"unticked","set":null}
{"t":"decision","v":1,"id":"a91…","machine":"DESKTOP-A","run":"8f1c…","at":"2026-09-27T21:40:00Z","kind":"assumedImported",
 "name":"DJI_20260725…_0120_D.DNG","size":27411200,"src":"DCIM/DJI_001/…","captureUtc":"2026-07-26T05:12:40Z",
 "why":"videos from this day already in library","set":null}
{"t":"decision","v":1,"id":"b02…","machine":"DESKTOP-A","run":"8f1c…","at":"2026-09-27T21:40:00Z","kind":"assumedImported",
 "name":"PANO_0001.DNG","size":13751808,"src":"DCIM/PANORAMA/001_0087/PANO_0001.DNG","captureUtc":"2026-05-25T13:30:28Z",
 "why":"confirmed by you","set":"001_0087"}
{"t":"revoke","v":1,"id":"…","machine":"LAPTOP-B","at":"2026-10-05T02:00:00Z","decision":"a91…"}
{"t":"torn","v":1,"id":"…","machine":"DESKTOP-A","at":"2026-10-06T18:02:11Z","line":412}
{"t":"cardDelete","v":1,"id":"…","machine":"DESKTOP-A","run":"c4e2…","at":"2026-10-12T19:30:05Z","name":"DJI_20260725232655_0117_D.MP4",
 "size":1234567890,"src":"DCIM/DJI_001/DJI_20260725232655_0117_D.MP4","unit":"DCIM/DJI_001/DJI_20260725232655_0117_D.MP4",
 "captureUtc":"2026-07-26T03:26:55Z","evidence":"InLedger","reason":"in the history, verified","mode":"beforeDate",
 "card":{"serial":"1A2B3C4D","label":null,"fs":"exFAT","model":"FC9113","inventoryHash":"…"},"set":null}
{"t":"run","v":1,"id":"…","machine":"DESKTOP-A","run":"8f1c…","start":"…","end":"…","app":"0.1.0",
 "card":{"serial":"1A2B3C4D","label":null,"fs":"exFAT","model":"FC9113","inventoryHash":"…"},
 "roots":{"video":"C:\\…\\UAS Videos","photo":"C:\\…\\Picture Offload"},
 "verdict":"SafeWithAssumptions","counts":{"VerifiedThisRun":17,"AssumedByRule":40}}
```

**Ledger snapshot and scale**
- Loading builds `LedgerSnapshot` (§3): files, sets, decisions after revokes, seen, folders, runs, card deletes (informational), parse issues, and the source files.
- At 10k lines the ledger is about 2.5 MB and loads in about 120 ms (measured single-file). The per-machine union scales linearly.

---

## 12. Error handling & recovery

| Situation | Detection | Behaviour | Recovery |
|---|---|---|---|
| No card | Detector finds nothing | Card stage: "Insert a card", Browse, Rescan; arrival is watched | F5 / auto on arrival |
| Browsed folder is inside, equal to or contains a root, the ledger folder (`<videoRoot>\.uas-sort`) or a sync root | `CardSourceValidator` | Refused with the reason | Pick the card |
| Browsed folder without DCIM | Validator | Refused: "pick the card's top folder" | — |
| Card write-protected | `FILE_READ_ONLY_VOLUME` | Badge; [Clean up card…] disabled | — |
| Card removed during Scan | IOException / device gone | Scan aborts; the draft is kept | Reinsert, then Rescan |
| Card removed during Review | Removal event | InfoBar; Offload disabled; thumbnails show placeholders | Reinsert |
| **Different card inserted** (same letter) | Identity check at Commit start, per file, and at verdict | Blocking at preflight; `CardSwapped` stops the run; verdict NotSafe | Rescan |
| Card enumeration error | Lister error list | `ForcesNotSafe` scan warning on the Other tab | Re-seat the card, then Rescan |
| Probe failure on one file | Exception inside the probe | `ProbeError`; time from filename, else mtime; copyable | — |
| Truncated MP4 | No `moov` / `.trinf` | GPS from fallback; unticked; chip | Repair in the drone and rescan, or tick |
| No GPS / implausible generic GPS | Search exhausted / plausibility gate | Time-only grouping; badge | Move items by hand if needed |
| `Etc/*` or ambiguous zone | GeoTimeZone result | Fallback zone (independent of R/G); `CheckDate` chip | Retarget or rename if the date is wrong |
| Drone clock doesn't fit any zone | Clock learner | Nearest-sample mode; clock banner explains | — |
| Drone clock doesn't match site-local time (e.g. still on US Eastern in Alaska) | `ClockMismatch` (≥ 15 min, §6.5) | Warning InfoBar (dismissible per session), "clock ≠ local" chips, tooltips; dates use site-local time; verdict unaffected | Set the RC 2 to automatic time zone (the InfoBar says where) |
| Implausible clock | Before 2015 or after now + 1 d | `ClockNotSet` warning | — |
| A library root is missing (D: unplugged) | Listing fails | Banner (`RootMissing`). Newness for that root is shown as unknown (items stay New and unticked, with a reason). Blocking at preflight only if an included job targets that root; the video root always blocks, because it holds the ledger | Plug in, then Rescan |
| Access denied on a library subfolder | Listing error | Warning; the folder is skipped from the index | — |
| Ledger folder `<videoRoot>\.uas-sort` has a cloud-only `ledger*.jsonl`, or isn't writable | `ILedgerStore.Check` (attributes before any open) | InfoBar; Blocking for Commit and Card cleanup; [Keep on this device] | Pin the folder and let OneDrive download it, or fix permissions; then Rescan |
| Ledger folder `<videoRoot>\.uas-sort` missing | `ILedgerStore.Check` | Info "No history yet"; created (the folder only) and pinned on Start offload or when Card cleanup starts deleting; a failed create stops Commit before the first copy, or cleanup before the first delete | If the app was used on another PC, wait for OneDrive to sync, then Rescan |
| Ledger folder not pinned | `ILedgerStore.Check` | Warning with [Keep on this device] | Click it |
| Ledger bad line (not the final one, and not named by a `torn` record) | Parse issues | Blocking `LedgerParseIssue` until [Accept and continue] (this session only; raised again on each scan); then a RequiresAck Warning and the verdict capped at SafeWithAssumptions; file and line shown | Fix or accept; backups in `ledger-backup\<root key>\` |
| Own ledger file ends without `\n` (crash mid-append) | `OpenOwn()` | Appends `\n` and a `torn` record naming the torn line; that line is skipped by every reader; no parse issue and no cap | — |
| Video root changed to one without `.uas-sort\ledger*.jsonl` | Settings change | Warning "No history found in `<new root>\.uas-sort`; copy current ledger there?" (the new root is saved first) | [Copy] (current union appended to the own file there; from the old root's latest local snapshot ∪ mirror if the old root is gone) or [Start empty] (with a confirmation if the new root holds videos the ledger says the app copied) |
| Placeholder or library read attempted (anything outside the ledger exemption, §4.3) | Guard | `UnsafeIoException`: stop, log, dialog "internal safety stop" | Bug fix |
| Draft doesn't match the card | Edits no longer apply | "3 of 4 edits still apply"; pin-membership warnings | Resume or Discard |
| Map fails (no WebGL, `.mjs` MIME, offline, no `ready`) | Timeout, error messages | Fallback panel or offline canvas | "Open in browser" |
| Not enough space | Preflight | Blocking | Free space, or untick |
| Append target renamed or moved since scan | Preflight existence check | Blocking | Rescan |
| Stale temp files | Preflight (job destination directories only) | Listed; deleted at Start offload; Back deletes nothing | — |
| Card file changed since the scan | Re-check before copy / audit re-list | `ChangedOnCard` / "changed since scan" (Unaccounted) | Rescan |
| Card read error | IOException | Re-read the chunk once, then `Failed(Copy)` and continue; if the card is gone or different, stop (`CardRemoved` / `CardSwapped`, §10.3 table) | Re-run |
| Hash mismatch | Verify | Delete temp, retry once, then `Failed(Verify)` | Re-run; check the reader |
| Unbuffered verify unsupported | Open/read error | Buffered verify; counted in the headline and the report | — |
| Target appears before rename | `ERROR_ALREADY_EXISTS` | Delete temp; `ConflictAtRename` | Rescan shows the conflict |
| Final name/size wrong after rename | `ConfirmFinal` | `Failed(Confirm)` | Re-run |
| Disk full, or destination root lost mid-copy | IOException | Delete the current temp; the in-flight file is `Failed(<phase>)`; stop (`DestinationFull` / `DestinationLost`); the rest are `NotStarted`; `seen` records still written | Fix, then re-run (finished files show as Imported) |
| Card pulled mid-copy | Device error | Delete the current temp; `Failed(Copy)`; stop (`CardRemoved`); the rest are `NotStarted`; `seen` records still written | Reinsert, then re-run |
| Ledger append fails | IOException | Stop the run immediately (`LedgerWriteFailed`); the in-flight file stays `Verified` | Fix disk; the copied file counts as NameSizeMatch on the next scan |
| Cancel | Token | Delete the current temp; the in-flight file is `Cancelled`; stop; the rest are `NotStarted`; `seen` records; the report says what finished | Re-run |
| Crash or power loss mid-copy | Next launch | Only temp files can be left; preflight lists them and Start offload deletes them. A crash between rename and ledger write leaves a file that shows as NameSizeMatch; a crash mid-append leaves a torn tail that `OpenOwn()` repairs | Re-run |
| External exFAT destination | Volume kind | Post-run flush; "Safely remove D: before formatting" + [Eject] | — |
| Settings unreadable | JSON error | Derived defaults; `.corrupt-<ts>`; Blocking until the roots are confirmed; offer the roots from the last `run` record (the local `ledger-backup\<root key>\` mirrors, else `<derived default video root>\.uas-sort\`). The CLI's read-only load only uses derived defaults | Confirm roots |
| Second launch | AppInstance (mutex fallback) | Activates and foregrounds the existing window | — |
| Second instance tries to offload | Offload lock / single-writer ledger | Blocking: "Another uas-sort window is offloading" | — |
| Unhandled exception | `Application.UnhandledException`, `TaskScheduler.UnobservedTaskException` | Log; dialog listing what this run copied (from the ledger) | Restart; the draft survives |
| Card cleanup requested on a browsed folder | `CardSource.IsBrowsedFolder` | [Clean up card…] disabled with the reason; `ICardEraserFactory.Open` would throw `UnsafeIoException` (it checks the volume itself) | Insert the card and let detection find it |
| Card cleanup on a volume that fails the cleanup volume check (a USB SSD or spare card holding a card copy; no `MISC` index; not a volume root) | `CleanupVolumeCheck`; again in `ICardEraserFactory.Open` from Win32 | [Clean up card…] disabled: "This doesn't look like a drone card (it may be a backup drive)"; the factory refuses it with `UnsafeIoException` | Offload from the real card; delete a backup's files by hand if intended |
| Card cleanup: another window holds the offload lock, or the ledger folder is cloud-only, unwritable or its video root missing | Execution steps 1 and 3 | Nothing deleted; `Stop = OffloadLockHeld` ("Another uas-sort window is offloading") / `LedgerUnavailable` | Finish the other offload, or fix the ledger folder; run cleanup again |
| Card cleanup: card removed mid-run | Delete error `ERROR_NOT_READY` / `ERROR_DEVICE_NOT_CONNECTED`, then `CurrentIdentity()` fails | Stop (`CardRemoved`); the unit in flight is `PartiallyDeleted` or `CleanupFailed`; the rest `CleanupNotStarted`; files already deleted keep their `cardDelete` records; report written | Reinsert, then Rescan (the listing shows what is left) |
| Card cleanup: a different card inserted | Step 0 identity check | `CleanupCardSwapped`; stop; the rest `CleanupNotStarted`; nothing on the new card is touched | Rescan |
| Card cleanup: a card file changed since the plan | Step 1 `Stat` | `SkippedChanged` (or `PartiallyDeleted` mid-unit); cleanup continues | Rescan and plan again |
| Card cleanup: library evidence gone | Step 2 re-check (fresh listings, ledger) | `SkippedEvidenceGone`; the unit stays on the card; cleanup continues | Offload it again, or delete it as a not-in-library file |
| Card cleanup: delete refused | `ERROR_WRITE_PROTECT`; `ERROR_ACCESS_DENIED`; `ERROR_SHARING_VIOLATION` | Write-protected → stop (`WriteProtected`); access denied or sharing violation → that unit `CleanupFailed` / `PartiallyDeleted`, continue | Unlock the card, or close the program holding the file; run cleanup again |
| Card cleanup: `cardDelete` append fails | IOException from `ILedgerWriter.Append` | Stop at once (`LedgerWriteFailed`); the file is already gone and the cleanup report names it | Fix the ledger folder; the rescan shows the card as it is |
| Card cleanup: set partly deleted | A member's delete fails | `PartiallyDeleted`; the remaining members and the set folder stay; the rescan audits the smaller set normally (a subset of a matching library set folder is Imported, §8.8) | Run cleanup again |
| Card cleanup: a deleted file is still listed | Re-list after the loop | Listed under "still on the card": another program held it open, and Windows removes it when that program closes it | Close the program, then Rescan |

---

## 13. Testing strategy

Everything runs with `dotnet test --solution uas-sort.slnx` (xUnit v3 on Microsoft.Testing.Platform). It runs natively on Windows (optionally from WSL through `tools/r.sh`, which calls `dotnet.exe`). `tools/build.ps1 -CheckBannedApi` runs the analyzer probe.

**Unit tests: Core**

*Planner, ported from `docs/research/spikes/grouping/test_grouping.py` (37 cases)*
- **Summary.** 24 keep their expectations ("Same": spike strings mapped to enums, and the approved model may add assertions such as the blank-name Blocking issue); 9 are restated for the approved model ("Changed"); 3 are R-dependent; 1 is dropped. Verified 2026-09-27 by re-running `test_grouping.py` with the default R set to 80.467 km: 34 of 37 pass, and the 3 R-dependent ones fail at R = 50 mi because they depend on Council and Anvil being split.
- **Fixture.** The spike's constants: ANVIL (64.5627, −165.3696), COUNCIL (64.6935, −164.2657), NOME_A (64.6932, −165.7665), NOME_B (64.5925, −165.6731), ZACHAR (57.5368, −153.7484), KODIAK_TOWN (57.7996, −152.3902), NEWPORT_AM (41.5155, −71.2967), NEWPORT_PM (41.4762, −71.3237), MAKAHA (21.47, −158.21); PC zone `America/Anchorage`. `vid(stamp, n, loc)` is a DJI clip `DJI_<stamp>_<n:0000>_D.MP4` whose `mvhd` = stamp + 4 h as UTC and mtime = `mvhd` + 90 s; `dng(stamp, n, loc)` is a DNG with EXIF DTO = stamp. Z = `vid` 0123, 0124, 0148 at 20260927140127, 140144, 142416, ZACHAR; ZREL = `2026\2026-09\2026-09-27 Zachar Bay`. Defaults: `Tuning(50, 1)`, `droneClockZone` America/New_York, `IPlaceIndex` null, empty ledger. Scenarios are built as `ScanResult`s by `UasSort.Testing`'s scenario builder and run through `Planner.Prepare` + `Derive`.
- **Clock flags in the fixture.** Its clock is Eastern (`mvhd` = stamp + 4 h) and most sites are in Alaska, so the learner picks Zone `America/New_York` (SiteLocal doesn't fit), and every drone-stamped item at an Alaska or Hawaii site also carries `ClockMismatch` (Newport items don't). Tests that assert an exact flag set include it; the others don't mention it.
- **String mapping.** `droneclock+learned` → `DroneClockZone` (a zone fits); `droneclock+setting` → `DroneClockSetting` + `ClockFromSetting`; `mtime` → `Mtime`; `autel-*-floating` → dropped; `high (date + location)` → `High`, Why "same dates, &lt;0.1 mi"; `medium…` → `Medium` with the §8.5 Why; `AppendSplit` → two groups split by a `LibraryFolder` wall, each an Append; `PhotosOnly` groups → `PhotoDays`; `PossibleDuplicate` → `IsNew(NoMatch)`; `RemovedFromLibrary` → `Imported(LedgerVerified)`; `/` in paths → `\`; `radius_km` → `RadiusMiles`.

| # | Spike test → C# test | Inputs | Expected (approved model) | Port |
|---|---|---|---|---|
| 1 | `test_midnight_local_date_uses_site_zone_not_filename` → `Midnight_LocalDateFromSiteZone` | vid 20260726035000 #1, 20260726041000 #2, ANVIL | 1 group; Start 2026-07-25; `NewFolderRel(2026-07-25, "Anvil")` = `2026\2026-07\2026-07-25 Anvil` | Same |
| 2 | `test_midnight_with_G0_not_split_inside_session` → `Midnight_G0_HoursGuardKeepsOneGroup` | as #1, `Tuning(50, 0)` | 1 group (H = 3 h) | Same |
| 3 | `test_dng_uses_learned_offset_and_site_zone` → `Dng_UsesClockZoneAndSiteZone` | vid 20260726035000 #1, dng 20260726035500 #2, ANVIL | DNG: `DroneClockZone` (America/New_York), CaptureUtc 2026-07-26T07:55Z, LocalDate 2026-07-25 | Same |
| 4 | `test_hawaii_site_zone_differs_from_pc_zone` → `Hawaii_SiteZoneNotPcZone` | vid 20260301053000 #1, MAKAHA | Tz `Pacific/Honolulu`, LocalDate 2026-02-28 | Same |
| 5 | `test_offset_nearest_sample_handles_clock_change` → `ZoneLearner_DstChangeStillFitsNewYork` | vid 20261020120000 #1 (−4 h), vid 20261110120000 #2 (−5 h), dng 20261110121000 #3, ANVIL | `ClockMode.Zone`, America/New_York; DNG `DroneClockZone`, 2026-11-10T17:10Z | Changed (zone learner) |
| 6 | `test_offset_from_setting_when_card_has_no_mp4` → `NoMp4_UsesStoredZone` | dng 20260927150000 #1, ZACHAR; stored mode `Zone`, once with `droneClockZone` = America/New_York set explicitly, once with the default | Both: `ClockMode.Setting`, `DroneClockSetting`, flags `ClockFromSetting` and `ClockMismatch`, 2026-09-27T19:00Z. Never `Mtime`: Setting mode always has a zone | Changed |
| 7 | `test_truncated_clip_without_moov_uses_filename_and_joins` → `Truncated_TimedByClockAndJoins` | vid 20260726235645 #1, 20260727002013 #14 (no `moov`), 20260727002118 #15, ANVIL | #14: `DroneClockZone`, 2026-07-27T04:20:13Z, flags `Truncated` and `ClockMismatch`; 1 group | Same |
| 8 | `test_autel_floating_time_no_gps` | — | Dropped with the Autel card path (§15 Q7) | Dropped |
| 9 | `test_trip_across_month_boundary` → `Trip_AcrossMonth` | vid 20260731230000 ANVIL, 20260801230000 (64.58, −165.40), 20260802230000 ANVIL | 1 group; `2026\2026-07\2026-07-31 Trip` | Same |
| 10 | `test_trip_across_year_boundary` → `Trip_AcrossYear` | vid 20261231230000, 20270101230000, ANVIL | `2026\2026-12\2026-12-31 NY` | Same |
| 11 | `test_two_day_gap_splits` → `TwoDayGap_Splits` | vid 20260722230000, 20260725230000, ANVIL | 2 groups, boundary `DayGap` | Same |
| 12 | `test_consecutive_days_far_apart_split` → `CouncilAnvil_R50_OneGroupEmphasisedSplit` and `CouncilAnvil_R25_TwoGroups` | vid 20260725232655 #117, 20260726022937 #118 COUNCIL; 20260726235645 #1, 20260727000012 #2 ANVIL | R 50: 1 group of 4, one DaySplit before #1, Apart 33.9 mi, Emphasised, Warning `EmphasisedDaySplit`. R 25: [[117, 118], [1, 2]], boundary `Distance` | R-dependent |
| 13 | `test_consecutive_days_same_place_join` → `Kodiak_MultiDayJoins` | vid 20260523015251 #40, 20260523201928 #52 KODIAK_TOWN; 20260524190521 #64 (57.75, −152.50); 20260525092718 #85 KODIAK_TOWN | 1 group; Start 2026-05-22, End 2026-05-25 | Same |
| 14 | `test_same_day_two_distant_sites` → `ZacharKodiak_SameDay_R50_Splits` | vid 20260927140127 #123, 140144 #124 ZACHAR; 20260927190000 #150 KODIAK_TOWN | 2 groups (53.4 mi > 50; the 3.4 mi margin is pinned), both Start 2026-09-27 | Same |
| 15 | `test_same_day_A_B_A_is_not_re_merged` → `SameDay_ABA_NotReMerged` | vid #1 ZACHAR, #2 KODIAK_TOWN, #3 ZACHAR (14:01, 19:00, 23:00 drone clock, 9/27) | 3 groups | Same |
| 16 | `test_same_day_near_sites_join` → `NearSites_Join` | vid 20260510104103 #2 NEWPORT_AM, 20260510193745 #30 NEWPORT_PM; vid 20260704010948 #101 NOME_A, 20260704013615 #102 NOME_B | 1 group each | Same |
| 17 | `test_no_gps_clip_goes_to_nearer_neighbour` → `NoGps_NearerNeighbour_R25` and `NoGps_NearerNeighbour_Synthetic70mi` | vid #117 COUNCIL, no-GPS vid 20260726234000 #99, vid #1 ANVIL | R 25: [[117], [99, 1]]. The synthetic duplicate (two sites 70 mi apart, same times) gives the same shape at R 50 | R-dependent |
| 18 | `test_no_gps_clip_session_beats_time` → `NoGps_SessionBeatsTime_R25` and `…_Synthetic70mi` | as #17, with #117 and #99 in one session (same serial, `SessionUtc` 2026-07-26T03:20Z) | R 25: [[117, 99], [1]]; the synthetic duplicate the same at R 50 | R-dependent |
| 19 | `test_no_gps_first_clip_of_new_session_follows_session` → `NoGps_FirstClipOfNewSessionFollowsSession` | vid 20260927140000 #1 ZACHAR (session 17:55Z); 142000 #2 no GPS and 150000 #3 KODIAK_TOWN (session 18:18Z) | [[1], [2, 3]] | Same |
| 20 | `test_all_no_gps_time_only` → `AllNoGps_TimeOnly` | vid 20260725232655, 20260726235645, 20260729120000, no GPS | 2 groups | Same |
| 21 | `test_R_and_G_are_parameters` → `R_And_G_AreParameters` | vid #117 COUNCIL, #1 ANVIL | R 40 → 1 group; R 25 → 2 (`Distance`); R 50 with G 0 → 2 (`DayGap`) | Changed (miles; G case added) |
| 22 | `test_video_only_card_new_folder` → `EmptyLibrary_NewFolder` | card Z; empty library | 1 group: NewFolder `2026\2026-09\2026-09-27`, Blocking `EmptyFolderName`; `PhotoDays` empty | Same |
| 23 | `test_append_today_via_card_leftovers` → `AppendToday_ViaCardLeftovers` | card Z + vid 20260927160000 #160, 161000 #161 ZACHAR; library Z in ZREL | 1 group, wall ZREL: Append(ZREL) · High, "same day as clips already in this folder" | Same |
| 24 | `test_append_today_via_ledger_centroid` → `AppendToday_ViaLedgerCentroid` | card #160; library Z in ZREL; ledger `file` records for Z with ZACHAR lat/lon, tz America/Anchorage | Append(ZREL) · High, "same dates, &lt;0.1 mi" | Same |
| 25 | `test_append_today_location_unknown_same_date_is_medium` → `AppendToday_LocationUnknown_Medium` | card #160; library Z in ZREL | Append(ZREL) · Medium, "same dates, location unknown" | Same |
| 26 | `test_next_day_far_away_new_folder` → `NextDay_Far_NewFolder` | card vid 20260928180000 #170 KODIAK_TOWN; library Z; ledger centroid as #24 | NewFolder `2026\2026-09\2026-09-28` + Blocking `EmptyFolderName` (53.4 mi > R) | Same |
| 27 | `test_next_day_same_place_appends` → `NextDay_SamePlace_AppendHigh` | card #170 at ZACHAR; library Z; ledger centroid | Append(ZREL) · High, "next day, &lt;0.1 mi" | Same |
| 28 | `test_next_day_location_unknown_defaults_new` → `NextDay_LocationUnknown_NewFolder` | card #170 at ZACHAR; library Z; no ledger | NewFolder `2026\2026-09\2026-09-28`; `AppendCandidates` contains ZREL | Same |
| 29 | `test_never_prepend_before_folder_name_date` → `NeverAppendBeforeFolderNameDate` | card vid 20260926180000 #170 ZACHAR; library Z | NewFolder `2026\2026-09\2026-09-26` | Same |
| 30 | `test_legacy_depth_autel_append` → `LegacyDepth_DjiAppendsBesideAutelMembers` | library `2022\2022-03-27 Makaha Valley\MAX_0061.MP4`, `MAX_0062.MP4`, `MAX_0064.MP4` (61,000,000 / 62,000,000 / 64,000,000 bytes; mtimes 2022-03-27T15:01Z, 15:02Z, 15:04Z); card items built directly: those three (TimeSource `Mtime`, no GPS; matched by name and size) + vid 20220327110500 #1 MAKAHA | 1 group, wall Makaha Valley: Append(`2022\2022-03-27 Makaha Valley`) · High, "same day as clips already in this folder"; the MAX members' start times come from mtime (§7.1) | Changed (rewritten) |
| 31 | `test_append_split_follows_user_boundaries` → `TwoWalls_TwoAppends` | library `2026\2026-08\2026-08-01 Anvil AM` = vid 20260801200000 #1, 201000 #2; `2026\2026-08\2026-08-02 Anvil PM` = vid 20260802200000 #3, 203000 #5; card = those + New vid 20260801202000 #9 and 20260802202000 #4, all ANVIL | 2 groups: {#1, #2, #9} Append(Anvil AM) · High and {#3, #4, #5} Append(Anvil PM) · High, both "same day as clips already in this folder"; the boundary is `LibraryFolder` | Changed |
| 32 | `test_leftovers_card` → `LeftoversCard_WallsAndPhotoDays` | card #117, #118 COUNCIL, #1, #2 ANVIL, Z, dng 20260725233000 #116 COUNCIL, 20260927140000 #122 ZACHAR, 20260815200000 #119 ANVIL; library Council (#117, #118) in `2026\2026-07\2026-07-25 Council Road`, Anvil (#1, #2) in `2026\2026-07\2026-07-26 Anvil Mountain` | Groups: AlreadyImported(Council Road), AlreadyImported(Anvil Mountain) (boundary `LibraryFolder`), NewFolder `2026\2026-09\2026-09-27` + Blocking `EmptyFolderName`. Watermark 2026-07-27T04:00:12Z. #116 ProbablyImported, "videos from this day are already in the library"; #122 and #119 New (`AfterWatermark`). `PhotoDays`: 2026-07-25, 2026-08-15, 2026-09-27 | Changed |
| 33 | `test_photo_only_day_before_watermark_is_probably_imported` → `PhotoOnlyDay_BeforeWatermark_ProbablyImported` | library Z in ZREL; card dng 20260815200000 #119 ANVIL | No groups; ProbablyImported, "photo-only day before the last imported video (Sep 27)"; `ClockMode.Setting` | Same |
| 34 | `test_photo_only_card_after_watermark` → `PhotoOnlyCard_AfterWatermark_PhotoDays` | library Z; card dng 20260930200000 #200, 200500 #201 ZACHAR | Both New (`AfterWatermark`); `PhotoDays` = [2026-09-30, America/Anchorage, 2 units]; no groups | Changed |
| 35 | `test_photo_in_photo_root_is_imported_and_pano_sets_are_distinct` → `PanoSets_ImportedByFolderAndDistinct` | card sets 001_0087 (PANO_0001.DNG 13,751,808 B, PANO_0002.DNG 12,882,432 B; first-frame DTO 2026-05-25 09:30:28, KODIAK_TOWN) and 001_0112 (same member names and sizes; DTO 10:00:00); library `Picture Offload\001_0087\` with both members at the card's mtimes; no library videos | 001_0087 Imported, `SetPlacement` Imported. 001_0112 New ("no videos imported outside uas-sort": no watermark, §7.1), `SetPlacement` Plain `001_0112` | Changed (mtime ±2 s; no-watermark rule) |
| 36 | `test_video_conflict_duplicate_removed` → `VideoNewness_ConflictNoMatchLedger` | library ZREL holds #123 at 105,764,094 B and `best shot.MP4` at 555,555,555 B; ledger `file` record (unbuffered) for #131; card #123 at 7,340,032 B, #130 at 555,555,555 B, #131 | #123 `Conflict(…\DJI_20260927140127_0123_D.MP4, 105764094)`; #130 `IsNew(NoMatch)`; #131 `Imported(LedgerVerified)` | Changed |
| 37 | `test_sanitize_and_naming` → `Clean_And_NewFolderRel` | `Clean("Newport, RI")`, `Clean(" A/B: \"test\"?  ")`, `Clean("Nome Rd...")`, `NewFolderRel(2026-09-27, "  ")` | "Newport, RI"; "A B test"; "Nome Rd"; `2026\2026-09\2026-09-27` | Same |

- Other explicit R values: Nome A + B (7.5 mi) → 1 group; Newport AM + PM → 1 group; the session-link regression (GPS-bearing members only).

*New planning cases*
- Library-folder walls, and the cause recorded on every boundary; merge across walls is refused.
- **Scenario D at R = 50 mi:** Append(Council Road) **Medium** with the cross-day hint; after `SplitBefore` → NewFolder `2026\2026-07\2026-07-26` (UserSplit rule).
- **A wall folder dated after the New clips:** New 7/24 clips + Imported 7/25 clips in F dated 7/25 → NewFolder with Info `NewBeforeWallFolder`, [Split here] = `SplitBefore(first item Imported into F)`; never `Rejected(SplitAtGroupStart)`.
- **`NothingToCopy`:** a group of only unticked Conflicts and dismissed videos, with no wall → `NothingToCopy("nothing to copy: 2 conflicts, 1 dismissed")`, never folded; a group of only ledger-Imported videos whose files were culled (`Imported.Folder` null) → the same.
- **Inclusion before Decide:** a group whose only non-Imported member is a Conflict is AlreadyImported(F); `SetIncluded([conflict], true)` turns it into Append(F) with the conflict copied as `name (2).ext`.
- **Two-pass UserSplit:** groups A | B (A earlier) split by the user, both of which could target F, neither walled → pass 1 gives both Append(F); pass 2 gives B NewFolder (F is its earlier neighbour's pass-1 target) and A keeps Append(F). With F as B's Wall instead, A becomes NewFolder. A Retarget pin on A never feeds B's rule.
- **Structural-edit algorithm (§8.9 worked examples):** SplitBefore at R 50 → 25 → 50 keeps a `UserSplit` chip; Merge absorbs the groups between a and b; MoveToNewGroup leaves a non-contiguous group ordered by its anchor, with a `UserSplit` boundary; a missing-item edit is counted as dropped, an inactive one is not.
- Adjacent-day appends: &lt; 10 mi → High; 10 mi–R → Medium with hint.
- Pins: membership change → Warning with [Keep]/[Reset]; conflicting pins after merge → Blocking; `Rename` on Append → `Rejected(RenameExistingFolder)`.
- Retarget into `<videoRoot>\.uas-sort`, `<videoRoot>\.uas-sort\x`, the photo root or a previous photo root → `Rejected(RetargetIntoReservedFolder)`; to a later-dated folder without confirmation → `Rejected(RetargetLaterDatedFolderUnconfirmed)`.
- Fold rule: a group with Conflict, Truncated or ProbeFailed members never folds.
- The set-folder rule: Plain, Resume, DateSuffixed, `(2)`, Imported; mtime ±2 s; an empty existing folder → Plain; ledger first-frame check.
- Conflict `(n)` naming, and ` (n)` removal for matching. **Photo rule 2b:** a DNG whose name exists in the photo root at another size → Conflict, unticked; ticked → `X (2).DNG` with its twin `X (2).JPG`.
- **`SessionKey` tolerance:** two clips with the same serial and `SessionUtc` 0.8 s apart are one session; 3 s apart are two; different serials are never one session.
- The 400-character limit on the **temp** path; suggestions ranked per day.
- **Issue catalogue:** each `IssueCode` of §9.10 is raised by the component, with the severity, `RequiresAckAtPreflight` flag and quick-fix edits the table gives.

*Performance* (Release build)
- A synthetic 500-item `PlanBase` (one card of 400 videos in 12 groups plus 100 photos): the median of 20 `Derive` calls is under 50 ms.

*Edit invariants* (seeded random sequences of merge, split, move, rename, retarget, include, tuning, quick fixes, undo and redo):
- every video is in exactly one group, and there are no empty groups;
- undo then redo gives the same plan;
- a quick fix is one undo entry;
- replaying the draft on a copy of the scan gives the same plan;
- item-anchored edits survive every R from 5 to 100 mi;
- **local dates never change with R or G.**

*Time*
- Midnight: drone `20260726035000` → Jul 25. Hawaii Feb 28.
- **Clock learner** (`DroneClock.Learn`, §6.1):
  - summer −4 samples at Alaska sites → New York; the same samples at Eastern sites → SiteLocal (New York also fits; SiteLocal wins by order);
  - a winter card with −5 samples in Alaska → New York (DST handled);
  - a fixed −4 in winter → no candidate fits → nearest-sample mode;
  - a photo-only winter card with a stored `Zone` → the stored zone (−5);
  - library member starts and the watermark use the same model; in SiteLocal mode a member converts through its folder's ledger `tz`, else the stored zone.
- **SiteLocal and `ClockMismatch`** (user decision 2026-09-27; §6.1, §6.5):
  - **SiteLocal fit:** vid 20260927100127 #123 and 100144 #124 at ZACHAR with `mvhd` = stamp + 8 h (an RC 2 set to Alaska time) → `ClockMode.SiteLocal`; a DNG 20260927101000 at ZACHAR → `DroneClockSiteLocal`, 2026-09-27T18:10Z; no `ClockMismatch`; the saved settings after a successful run are `droneClockMode = SiteLocal` with `droneClockZone` unchanged.
  - **Eastern clock in Alaska:** card Z (stamp + 4 h, ZACHAR) plus a DNG → Zone `America/New_York`; `ClockMismatch` on every item; one Info issue `ClockMismatch`; the InfoBar reads "Drone clock is set to UTC−4 (America/New_York), but footage on this card was shot in Alaska (UTC−8). …"; the verdict computed for the same card is unchanged by the flag.
  - **Eastern clock in Newport RI:** vid 20260510104103 #2 NEWPORT_AM and 20260510193745 #30 NEWPORT_PM (stamp + 4 h) → SiteLocal (New York also fits); no `ClockMismatch`, no InfoBar.
  - **Trip crossing zones with a site-local clock:** vid 20260510104103 at NEWPORT_AM (stamp + 4 h) and vid 20260726195000 at ANVIL (stamp + 8 h) → no single zone fits, SiteLocal does; a no-GPS DNG 20260726195500 converts through the ANVIL clip's zone (nearest GPS item ≤ 12 h) → 2026-07-27T03:55Z; no `ClockMismatch`.
  - **Photo-only card uses the stored mode:** dng 20260927110000 #1 at ZACHAR with stored `SiteLocal` → `ClockMode.Setting`, `DroneClockSetting`, `ClockFromSetting`, converted through `America/Anchorage` → 2026-09-27T19:00Z, no `ClockMismatch`; the same card with stored `Zone` `America/New_York` → 2026-09-27T15:00Z and `ClockMismatch`.
  - **Clock corrected mid-card:** 6 Alaska videos with an Eastern clock (offset −4 h) followed by 5 with an Alaska clock (−8 h), plus DNGs before, between and after → mode `NearestSample`; `ClockSummary.Changes` has one change at the midpoint; each DNG takes the offset of its nearest video in time; only items before the change carry `ClockMismatch`; the banner names the change.
  - **15-min boundary:** the predicate is true at a 15:00 min difference and false at 14:59; end to end, a clock set to `Asia/Kolkata` (+5:30) flown in Kathmandu (27.7172, 85.3240, `Asia/Kathmandu`, +5:45) → NearestSample (+5:30), `ClockMismatch` (exactly 15 min); the same clock flown in Kolkata → no flag.
  - **Worked example:** `DJI_20260927140627_0128_D.MP4` with `mvhd` 2026-09-27T18:06:27Z at 57.55044, −153.73897 → Zone `America/New_York`, local 10:06:27 AKDT, LocalDate 2026-09-27, `ClockMismatch`.
  - No `ClockMismatch` for an item whose site zone is the PC-zone fallback, or whose time source is `Mtime`.
- `Etc/*` fallback uses the nearest land-zone GPS item on the whole card.
- `CheckDate` windows: 60 min for zone alternatives; 75 min for `Mvhd`/`DroneClock*`.
- Pre-2015 and future timestamps.
- Generic-GPS plausibility: accept consistent; reject inconsistent first/last, an `Etc` zone, or > 500 mi from the card.

*Newness*
- The photo rule's order of checks.
- **The watermark is not advanced by the app's own ledger copies.**
- **Two-run `seen` scenario:** run 1 copies only the 10/4 video and leaves the 10/2 photo unticked; run 2 → the photo is still New ("not copied on …"). Also the **cancel-then-rerun** variant (a NotStarted photo). Also a same-day photo whose day's videos were copied in run 1.
- Near-watermark (±75 min) for drone-clock photos.
- **Photo root moved to D:** a DNG still in the old `Picture Offload` (inside the video root, or in `previousPhotoRoots`) → Imported.
- A ledger `decision` beats the heuristic; a `revoke` restores it.
- A pair is decided by its DNG, with the JPG line AssumedByRule when the twin isn't found. A set needs all members to match.
- **Sets in the ledger:** a 33-frame pano with `assumedImported` records for 5 members is not Decided and falls through to the photo rules; with all 33 it is Decided; revoking one member's record makes it undecided again. The same for `seen` (a set with `seen` for only some members is not "not copied on …").
- **No watermark:** a library whose every video has a ledger `file` record (or no videos at all) gives `WatermarkUtc` null, and every unseen photo is New ("no videos imported outside uas-sort").
- **[Start empty] on a root holding app-copied videos:** the confirmation appears when the new root's listing matches current ledger `file` keys; after confirming, the watermark moves to the latest of those videos, and a photo that was New by rule 4 becomes ProbablyImported (the documented cost). With [Copy] instead, nothing changes.
- **The ledger folder is excluded from the library index.** A video root whose `.uas-sort\` holds `ledger-A.jsonl`, a conflict copy, and a stray `DJI_…_D.MP4` plus `.DNG` gives:
  - no keys, sizes or set folders from that subtree;
  - no event folder;
  - no Imported or Conflict evidence;
  - no change to the watermark.

*Classification*
- Fail-safe rules:
  - `DCIM\DJI_A001\x.MP4` → Unknown;
  - a media file outside DCIM → Unknown;
  - a cover JPG → Unknown;
  - `.foo.MP4` → Unknown;
  - named skips only.
- Browse anchoring: `E:\DCIM` and `E:\DCIM\DJI_001` anchor to `E:\`. A copied card at `C:\Temp\card\` anchors correctly. A folder with media and no DCIM is refused.
- **Audit test:** a card whose media all sit in unrecognised locations → NotSafe.

*Audit* (table-driven over every outcome and newness combination)
- A Safe verdict is impossible while anything is Unaccounted or AssumedByRule, the identity changed, or the listing changed.
- **Per-file worst-of:**
  - a set with one Failed member;
  - a DNG verified while its JPG twin failed;
  - a Resume set with mixed evidence;
  - a JPG twin assumed imported;
  - `copyJpgTwin=false` → SkippedByRule;
  - a truncated clip Imported by name and size → capped at SafeWithAssumptions.
- `verify:"nameSize"` ledger records → NameSizeMatch, never InLedger.
- Headline counts: name+size-only and cached verifications.

*Ledger*
- Per-machine union; OneDrive conflict copies deduplicated by `id`.
- A torn final line is skipped; a bad middle line → Blocking plus, after [Accept and continue], the verdict cap.
- **Crash mid-append:** the own file ends in half a record; the next `OpenOwn()` appends `\n` and a `torn` record naming that line; a normal run follows. Reloading gives no parse issue, no Blocking issue and no verdict cap, and every later record parses. Another machine's file with a `torn` record is read the same way.
- `revoke` handling; backup snapshot at Start offload, written to the local `LedgerPaths.BackupDir(appData, videoRoot)` (`%LOCALAPPDATA%\uas-sort\ledger-backup\<root key>\`); the mirror receives every own-file append, including Verdict-page decisions and revokes made after the snapshot. Two video roots get two backup subfolders.
- **Derived location.** `LedgerPaths.For(videoRoot) == <videoRoot>\.uas-sort` for any video root. Serialising `Settings` gives no `ledgerDir` key (there is no such property), and Setup asks for no ledger folder.
- **Only top-level `ledger*.jsonl` is read.** `.uas-sort\notes.txt`, `.uas-sort\old\ledger-X.jsonl` and `<videoRoot>\ledger-X.jsonl` are never opened. Only the own `ledger-<MACHINE>.jsonl` is ever written.
- **Status** (`LedgerFolderStatus.State`). A missing folder gives `Missing` (Info) and is created (the folder only) and pinned by `EnsureFolder()` on Start offload; an existing unpinned folder is pinned there too. Not pinned gives `NotPinned` (Warning InfoBar, not on the sheet). A cloud-only `ledger*.jsonl` gives `CloudOnly` (Blocking) and is never opened. Not writable gives `Unwritable` (Blocking).
- **Video-root change migration prompt.** Changing the video root to one whose `.uas-sort\` has no `ledger*.jsonl` raises the "No history found in `<new root>\.uas-sort`; copy current ledger there?" warning. The new root is in `settings.json` before [Copy] writes.
  - [Copy] appends the current union to the own file there, keeping ids and machines. A reload gives an equal `LedgerSnapshot`. A second [Copy] adds nothing after dedupe.
  - [Copy] with the old root gone uses the old root's latest local snapshot ∪ its mirror, deduped by `id`, so a decision made on the Verdict page after the snapshot survives.
  - [Start empty] leaves the new root without history, and the first-run photo rules apply; it confirms first when the new root holds videos matching the ledger's `file` keys.
  - A new root that already has ledger files raises no prompt.
  - The old folder is never modified.

*Settings*
- Recovery → Blocking until the roots are confirmed. Roots are offered from the last `run` record in the local `ledger-backup\<root key>\` mirrors (else `<derived default video root>\.uas-sort\`).
- `Load(readOnly: true)` on a corrupt file returns derived defaults and leaves the directory byte-for-byte unchanged (no `.corrupt-<ts>`, no new file).
- Derived defaults (Pictures known folder; the ledger folder derived from the video root); no literal user paths.

*IO guard policy* (Core.Tests, table-driven over `IoGuardPolicy.Check`; this is where the exemption is proven, and Platform and the fake FS both call the same function)
- Allowed: `ReadData` of `<videoRoot>\.uas-sort\ledger-B.jsonl` and of a conflict copy `ledger-B-DESKTOP-A.jsonl`; `AppendOwnLedger` of `ledger-<MACHINE>.jsonl` (existing, or null attributes = create); `CreateDir` of `<videoRoot>\.uas-sort` itself; `SetPinned` on it; `CreateNew` of `X.MP4.uas-sort.tmp` in a NewFolder directory; `ReadData`/`Rename`/`Delete` of an own temp; `Delete` of a stale `*.uas-sort.tmp`; `OpenForFlush` of a renamed file; `CreateDir` of a NewFolder path and its `YYYY`/`YYYY-MM` parents; `ReadData` under the card root; anything under `AppDataDir`.
- `GuardUnsafe`: `AppendOwnLedger` or any write to `ledger-B.jsonl`; `ReadData` of `.uas-sort\settings.json` or `.uas-sort\sub\ledger-C.jsonl`; `Delete` or `Rename` of any ledger file; `CreateDir` of `.uas-sort\sub`; `ReadData` of `<videoRoot>\ledger-X.jsonl` (outside `.uas-sort`) and of the same name under the photo root; `ReadData` of any pre-existing library file; `CreateNew` of a non-temp name in a library root; `CreateDir` of an Append target; any write under the card root; null attributes for `ReadData`.
- `GuardCloudOnly`: `ReadData` of a `ledger*.jsonl` with `0x400000`. `GuardHydration`: `ReadData` of a library file with `0x401620`, `0x40000` or `0x1000`.
- **`CardDelete`.** Allowed: a file named in the context's `ConfirmedCleanupPlan` with `CardIsVerifiedCardVolume`; a set folder named in its `SetFolders`. `GuardUnsafe`: no plan in the context; a path the plan doesn't name; a named path when `CardIsVerifiedCardVolume` is false; a directory not in `SetFolders` (`DCIM`, `DCIM\DJI_001`, `MISC`); a plan whose `CardRoot` differs; null attributes; `CardDelete` of a library, previous photo root, ledger, `AppDataDir` or system-volume path, **even with `CardRoot` set to that path's folder and the path in the plan** (rule 1b); the old `Delete` op under the card root; `OpenForFlush`, `SetAttributesOrTimes` and `CreateNew` of the card root or a card file in every context, with or without a plan.
- Case and form: `.UAS-SORT\LEDGER-B.JSONL` is allowed (case-insensitive); `.uas-sort2\ledger-A.jsonl` is refused (the canonical-path half of this lives in the Windows `subst`/junction test).

*Source validation*
- Refuses equal, inside and containing cases for each root, `previousPhotoRoots`, the ledger folder (`<videoRoot>\.uas-sort`, including a browse straight into it) and sync roots.
- `detected` given → `IsBrowsedFolder` false and `IsWriteProtected` from `IsReadOnlyVolume`; `detected` null → `IsBrowsedFolder` true, also when the browsed path is a volume root such as `E:\`.
- Canonical comparison through subst and junctions (fakes plus one Windows integration case).

*Card cleanup* (`CleanupPlanner` pure; `CleanupExecutor` against `FakeFileSystem`; §10.6)
- **Eligibility table** (table-driven): every `AuditCategory` × newness (`IsNew`, `Imported`, `Decided` assumedImported and dismissed, `ProbablyImported`, `Conflict`) × flags (Truncated, ProbeFailed) × class (Video, Photo, PhotoTwin, SetMember, Skip companion, Skip non-companion, Unknown) × fresh listing (holds it or not) × ledger record (verified, nameSize, none) × the include switch → the §10.6 eligibility, evidence source, reason text, and deleted or kept. Named cases:
  - changed since the scan, read-only, and under an enumeration-error folder → Never;
  - a truncated clip matched by name+size → NotInLibrary (`Unfinished`) with the "same-size copy" reason; a dismissed truncated clip → NotInLibrary (`Dismissed`);
  - a dismissed video and an `assumedImported` photo day → NotInLibrary (`Dismissed` / `RecordedAsImported`), never deleted with the switch off, and listed in the review list with the decision date;
  - a video with a verified ledger record whose library file was deleted (no fresh listing) → NotInLibrary (`NoLongerInLibrary`); the same video listed → Evidence (`Listed`);
  - a photo moved by Lightroom with a verified ledger record → Evidence (`HistoryOnly`), counted apart in the summary; with only a `nameSize` record → NotInLibrary (`NoLongerInLibrary`);
  - a DNG InLedger with its JPG twin AssumedByRule → the pair is NotInLibrary; a twin SkippedByRule (copying off) follows its DNG and is counted in "files uas-sort never copies", its `cardDelete` evidence `companionOf:…`;
  - this run's `Failed` or `NotStarted` New clip → NotInLibrary (`New`); a Conflict → NotInLibrary (`Conflict`).
- **Ticked for offload:** opened from Review with 20 ticked New clips and the switch on, every one of their rows starts on Keep with the badge, [Delete all] leaves them on Keep, and the plan deletes none of them until each row is set to Delete by hand (planner and VM test).
- **Unit completeness:**
  - a DNG+JPG pair is deleted together, twin first;
  - a set's members in name order, then its folder;
  - an MP4 goes with `.LRF`, `.SRT`, `.<stem>.MP4.trinf` and `.avc1`, and with its cover JPG only when the rule is on (with it off, the JPG is Unknown and kept while the MP4 goes);
  - a companion never makes a unit eligible: an Unknown or NotInLibrary MP4's LRF stays with it;
  - a changed companion keeps the whole unit; an orphan LRF is Never; the primary file is always deleted last.
- **Before date:**
  - strictly before the chosen site-local day; the day itself is kept; a PC in another zone changes nothing;
  - the midnight flight: with cutoff Jul 26, `20260726035000` (Jul 25 23:50 AKDT) is deleted and the same session's 00:10 AKDT clip is kept, with `FlightContinuesLocal` set;
  - a Makaha clip at 09:30Z is Feb 28 in Honolulu: deleted with cutoff Mar 1, kept with cutoff Feb 28 (it would be Mar 1 in the PC's Alaska zone);
  - the cutoff line: "…that are in your library; Jul 26 and later are kept. 14 older files are kept" with a non-empty Kept list, and "Deletes everything captured before…" only with an empty one.
- **Free space:**
  - both readings: `FreeUp` 20 GB on a card with 12.4 GB free targets 32.4 GB; `HaveFree` 20 GB targets 20 GB; the same prefix algorithm and cutoff line;
  - the minimal prefix (k − 1 units fall short);
  - cluster rounding: with 128 KiB clusters a 1-byte file counts 131,072 bytes, and a target met only with rounded sizes is met;
  - Never units are passed over without ending the walk;
  - target already met → empty plan and the "nothing to delete" text;
  - unreachable target → every deletable unit, with `FreeableBytes`, `HeldByNotInLibrary` and `HeldByNever` and both messages;
  - the cutoff text, including "3 of 9 files from Aug 30" and zone abbreviations at both ends.
- **Keep toggles and new rows:** in Free-space mode, Keep on a row inside the prefix extends it to the next deletable unit and moves the cutoff later; a NotInLibrary unit entering the range arrives **undecided** with the badge, counts as Delete for the walk, is not deleted, and keeps Delete disabled ("Decide 1 new row") until it is set or [Delete all] is pressed again; the fingerprint changes and the acknowledgements clear. In Before-date mode, Keep drops only that unit, and a later date brings new rows in undecided.
- **Confirm:** refused on a fingerprint mismatch, a missing acknowledgement, a NotInLibrary unit without its row decision, an undecided row, an unticked not-in-library box, or an empty plan. A plan whose `Delete` no longer matches its stored `Fingerprint` (built through Core.Tests internals) is refused, because Confirm recomputes it. A candidate that is Never, or a file outside `<CardRoot>\DCIM\`, is refused. Neither `CleanupPlan` nor `ConfirmedCleanupPlan` has a constructor reachable outside Core, and `with` doesn't compile on them (Review.Tests compile checks).
- **Executor** (every row of the §10.6 outcome table), including:
  - the offload lock held elsewhere → `OffloadLockHeld`, nothing deleted; a cloud-only or unwritable ledger folder → `LedgerUnavailable`, nothing deleted; `OpenOwn()` runs only after `EnsureFolder()` on a missing folder;
  - the identity changes before the 3rd file → `CleanupCardSwapped`, stop, the rest `CleanupNotStarted`;
  - a file's mtime changed → `SkippedChanged`, and the next unit is still deleted;
  - a `Listed` video whose library file is deleted between plan and run, while the ledger still has its verified record → `SkippedEvidenceGone`; a photo moved by Lightroom but with a verified ledger record is still deleted;
  - a delete error on a set's 5th member → `PartiallyDeleted` with 4 deleted and the rest still listed; the run continues; the set folder stays; the rescan finds the 29 remaining members Imported through the library set folder (§8.8);
  - `ERROR_WRITE_PROTECT` → stop `WriteProtected`; the card vanishes → `CardRemoved`;
  - the ledger append throws → stop `LedgerWriteFailed`, the file gone and named in the report;
  - a deleted file that stays in the re-list (a fake delete-pending entry) → `StillListed` and "still on the card" on the result page;
  - Cancel stops after the current file;
  - `IThumbnailSource.Pause()` is held for the whole run;
  - one `cardDelete` per deleted file (companions included) with the right evidence, `historyOnly:` prefix, `companionOf:` or `notInLibraryConfirmed`; no `run` record; after a reload, the newness of every other file is unchanged.
- **Report (`CleanupVm`):** one line per planned file with its outcome and `LedgerRecorded`; free space before and after; written with `VerdictAfter = NotSafe` when the rescan throws.
- **Tripwires and entry:** a card delete outside a confirmed plan records a `CardDeleteViolation`; a cleanup run opens no library file (the hydration tripwire); a browsed-folder source can't open an eraser; a write-protected card disables [Clean up card…] (VM test).
- **Volume check** (`CleanupVolumeCheck`, table-driven): an SD-bus exFAT card with `MISC\FC9113.db` passes; removable media on a Scsi or Unknown bus (a PCIe card reader) passes; a fixed exFAT USB volume (`RemovableMedia` false) holding a card copy, and fixed SATA, SCSI or NVMe media, are refused, with a rule-4 detail; a non-root folder, NTFS, the system volume, a volume holding the photo root, and a card without a `MISC` index are refused, each with the tooltip.

**Cross-assembly and JSON (`Review.Tests`)**
- A `switch` without a default arm on Core's `CopyOutcome` and `CleanupOutcome` (closed) and `GpsProbe` and `EraseResult` (unions) compiles with `TreatWarningsAsErrors`.
- A closed `PlanEdit` and `TargetChoice` round-trip through source-generated JSON.

**Golden replay**
- **Fixture.** `tests/UasSort.Testing/Replay/library-listing.json` was generated **once**, on 2026-09-28 (listing only; resolved with the user), and is checked in at `docs/research/fixtures/library-listing.json`, which the build copies into place; `tools/fixtures/make-replay-fixture.ps1` is the record of how it was produced and is not run during the build. It came from the library **listing** (names, sizes, mtimes, attributes; no file is opened) plus the GPS already extracted in `docs/research/spikes/djmd/calibration.json` and `docs/research/spikes/grouping/more_gps.json`: the same inputs as `docs/research/spikes/grouping/replay50.py`. It is checked in and **never regenerated by tests**, so later imports can't change the expectations. The repo is private, and no file content is included. Schema:

```json
{ "v": 1, "snapshotUtc": "2026-09-27T20:00:00Z", "pcZone": "America/Anchorage",
  "entries": [ { "relPath": "2026/2026-07/2026-07-25 Council Road/DJI_20260725232655_0117_D.MP4",
                 "size": 1234567890, "mtimeUtc": "2026-07-26T03:28:25Z", "attributes": 5248544 } ],
  "clips": { "DJI_20260725232655_0117_D.MP4": { "lat": 64.6935, "lon": -164.2657, "mvhdUtc": "2026-07-26T03:26:55Z",
                                                "hasMoov": true, "mvhdSimulated": false } } }
```

  `relPath` is relative to the video root (the photo root's entries sit under `Picture Offload/`). `clips` has one entry per library MP4: GPS where the research read it; for DJI clips whose `mvhd` wasn't read (Kodiak, cloud-only) `mvhdUtc` = filename stamp + 4 h with `mvhdSimulated: true`, as `replay50.py` does; Anvil 0014 and 0024 have `hasMoov: false`; Makaha's Autel `MAX_####.MP4` clips have no GPS and no `mvhdUtc`.
- **How items are built.** The replay builds `Item`s directly and never runs `CardClassifier` (on a real card the Autel clips would be Unknown). DJI clips go through `TimeResolver` with the fixture's GPS and `pcZone`; the clock is learned from the clips with a real `mvhd` (Zone `America/New_York`: SiteLocal doesn't fit the Alaska clips, so every Alaska DJI clip carries `ClockMismatch` and the Newport clips don't; no scenario expectation below changes). Makaha's clips get `TimeSource.Mtime`, no GPS and zone `pcZone`: their mtimes are the Autel wall clock read as AKDT, so their local date is 2022-03-27, the folder's date. Every scenario uses `Tuning(50, 1)` unless stated, `IPlaceIndex` = null, an empty ledger and no edits.
- **Scenarios** ("the user's 8 folders": Newport RI, Kodiak, Nome Roads, Safety Roadhouse, Council Road, Anvil Mountain, Zachar Bay, Makaha Valley)

| Scenario | Card | Library listing | Expected |
|---|---|---|---|
| **A0** (clustering alone) | every library MP4 | empty | **7 groups**, all NewFolder with blank descriptions (7 × Blocking `EmptyFolderName`). The partition equals the user's 8 folders except that Council Road (4 clips) + Anvil Mountain (21 clips) form one 25-clip group "Jul 25–26" with one emphasised DaySplit at the 7/25→7/26 change (Warning `EmphasisedDaySplit`, `RequiresAckAtPreflight`). Each NewFolder RelPath is `YYYY\YYYY-MM\YYYY-MM-DD` of its earliest clip's local date; the test pins Zachar Bay `2026\2026-09\2026-09-27`, Council + Anvil `2026\2026-07\2026-07-25`, Kodiak `2026\2026-05\2026-05-22` and Makaha `2022\2022-03\2022-03-27`. After `SplitBefore(first Anvil clip)`: **8 groups** = the user's 8 folders, the new boundary `UserSplit`. R ∈ {8, 10, 20, 30, 33} → 8 groups; R ∈ {40, 50, 60} → 7 |
| **A** (card = library) | every library MP4 | full | Walls: **8 AlreadyImported** groups, each pointing at its user folder; 7 fold, and Anvil Mountain stays unfolded with its "2 unfinished recordings" chip (0014, 0024 are Truncated). Watermark **2026-09-27T18:24:16Z** |
| **B** | every library MP4 | full minus the Zachar Bay folder | 7 AlreadyImported (Anvil unfolded, as in A) + **NewFolder `2026\2026-09\2026-09-27`**, 13 clips, Blocking `EmptyFolderName` |
| **C** | every library MP4 | full minus Zachar 0140–0148 | 7 AlreadyImported + **Append(`2026\2026-09\2026-09-27 Zachar Bay`) · High**, "same day as clips already in this folder", 13 clips of which 4 New |
| **D** | Council Road's 4 + Anvil Mountain's 21 clips | full minus the Anvil Mountain folder | One group of 25 (4 Imported 7/25 + 21 New 7/26): **Append(`2026\2026-07\2026-07-25 Council Road`) · Medium**, "different day, 34 mi from Council Road" (centroids from the card leftovers, 33.7 mi), `CrossDayHint.Fix` = `SplitBefore(first Anvil clip)`; Warnings `MediumAppend` and `EmphasisedDaySplit`, both `RequiresAckAtPreflight`. After either click: AlreadyImported(Council Road) + **NewFolder `2026\2026-07\2026-07-26`** (UserSplit rule), Blocking `EmptyFolderName` |
| **E** | Anvil Mountain's 21 clips | full minus the Anvil Mountain folder | One group: **NewFolder `2026\2026-07\2026-07-26`**, Blocking `EmptyFolderName`; `AppendCandidates` includes Council Road (adjacent dates, location unknown) |

- The watermark is asserted only in A (the other listings drop folders).

**Synthetic MP4s** (`SyntheticMp4Builder`, a port of `docs/research/spikes/djmd/synth_test.py`; no user data)
- `moov` first or last; 64-bit `mdat`, `co64`, `stco`; 1, 3 and 5 samples per chunk; `djmd` as the second `stsd` entry.
- GPS zeroed until sample 7, until sample 32, or everywhere.
- Degrees versus radians; an unknown protocol (generic search plus the plausibility gate).
- `mvhd` duration in versions 0 and 1 (timescale 1000 and 90000) → `Mp4Info.Duration`; no `moov` → null.
- No `moov` (the mdat-head fallback, both the payload-start and the 512 paths).
- Box sizes 0 and 1, and a truncated box size; `tnal` range.
- A read-count budget: at most 16 reads for the first fix.

**Synthetic DNG** (`SyntheticDngBuilder`): a minimal TIFF/DNG with DTO in the EXIF IFD, GPS and an IFD0 JPEG thumbnail. `tools/fixtures/make-selftest-assets.cs` writes `src/UasSort.App/SelfTest/selftest.dng` and `ledger-v1.jsonl` (every record type). Both are checked in and contain no user data.

**Real-file checks** (optional; skipped unless `UASSORT_GOLDEN` points at a folder of **copies** from a card)
- The test refuses any path under a configured library root or a sync root.
- Zachar 0128 → 57.5504420579265, −153.738972972093, 18:06:27Z.
- PANO_0001 → DTO 2026-05-25 09:30:28; 57.799648, −152.390180.

**Fake file system and hydration tripwire** (end to end; the fake calls `IoGuardPolicy`, whose rules are proven by the table test above)
- Fixture: every library file **except the local `.uas-sort\ledger*.jsonl` files** carries `0x401620`. A full scan, plan and offload must never open a pre-existing library file. The only opens allowed under the video root are `.uas-sort\ledger*.jsonl` (local, read), the own ledger file (append), and this run's own `*.uas-sort.tmp` and just-renamed files.
- The same run with one cloud-only `ledger-B.jsonl` gives the Blocking `CloudOnly` status and opens none of the ledger files; the offload phase is not reached. The tripwire never fires.
- The same run with a `.uas-sort\notes.txt` and a `.uas-sort\sub\ledger-C.jsonl` present never touches either.
- Preflight followed by **Back** deletes nothing, not even a stale temp it listed.
- **Card-delete tripwire.** Over scan, offload and Card cleanup, any card delete outside a confirmed plan is recorded as a `CardDeleteViolation`, and the fixture asserts none at teardown. Opening the Cleanup page and pressing Back deletes nothing.

**Fault injection** (`CopyEngine` against `FakeFileSystem`)
Every row of the §10.3 outcome table, including:
- A bit flip on read-back → retry, then `Failed(Verify)`.
- Disk full after N bytes → `Failed(Copy)`, stop `DestinationFull`, the rest `NotStarted`.
- A transient card read error → one chunk re-read, the copy completes; a persistent one with the card present → `Failed(Copy)`, the run continues.
- The card vanishes mid-file → `Failed(Copy)`, stop `CardRemoved`; **the card identity changes mid-run → CardSwapped, stop.**
- The target appears before the rename → ConflictAtRename, and nothing is overwritten.
- The post-rename size is wrong → Failed(Confirm).
- The ledger append throws → the file stays `Verified`, stop `LedgerWriteFailed`, the rest `NotStarted`.
- Cancel between chunks → the in-flight file `Cancelled`, no final file, no `file` line, **`seen` lines written.**
- A card file changed → ChangedOnCard.
- An Append folder deleted after scan → Blocking `AppendTargetGone` at preflight, and `EnsureDirectory` never creates it.
- Stale temps in a destination directory: listed by `Check`, deleted only after Start offload.

**Windows integration** (`Platform.Tests`; runs in `%TEMP%\uas-sort-test-<guid>`, deleted afterwards)
- Unbuffered read-back via `File.OpenHandle` + NO_BUFFERING, with the fallback recorded.
- `MoveFileExW` refuses to replace and returns `TargetExists`.
- Timestamps copied; Hidden cleared; preallocation.
- **A 300-character path** round trip (`\\?\` prefix).
- **The lister returns Hidden and System files, including a Hidden `.MP4` and a Hidden `.trinf`,** and records access-denied folders as errors.
- **The lister opens no files:** every file in a temp tree is locked with `FileShare.None`, then enumerated. The listing succeeds, and any data open would have thrown a sharing violation.
- **The library lister excludes `<videoRoot>\.uas-sort\`:** a temp video root with `.uas-sort\ledger-A.jsonl`, `.uas-sort\sub\x.MP4` and a normal event folder, listed with `excludeDirNames = {".uas-sort"}`, returns only the event folder. The ledger store's own top-level listing of `.uas-sort` (`excludeDirNames = {}`, `recurse: false`) finds `ledger-A.jsonl` and nothing from `sub\`.
- **Platform consults the policy:** `LedgerStore.Load()` on a temp `.uas-sort` that also holds `notes.txt` (locked with `FileShare.None`) succeeds and never opens it; `GuardedFileOps` and `WindowsCardReader` refuse a path the policy refuses, with `UnsafeIoException`.
- `EnsureFolder()` creates `.uas-sort` (and nothing inside it) and pins it; on an existing unpinned folder it only pins.
- **The card reader opens files whose ACL denies all write rights** (proves `FileAccess.Read` only).
- `PlaceholderGuard` against attribute bit patterns.
- The ledger exemption matched on canonical paths: a `subst` drive or a junction that points into `<videoRoot>\.uas-sort` resolves to the same folder and gets the same rules; a look-alike name such as `.uas-sort2\ledger-A.jsonl` is refused.
- `KeepOnDevice()` sets `FILE_ATTRIBUTE_PINNED` on a temp folder and on nothing else. Whether OneDrive honours it stays UNVERIFIED and is checked in acceptance.
- Directory flush on NTFS (exFAT is checked in acceptance).
- The named-mutex fallback and the offload lock.
- XAML lint: no `{Binding}`, `DisplayMemberPath`, `TextMemberPath`, `SelectedValuePath`, or non-`ms-appx` image sources in `src/UasSort.App/**/*.xaml`.
- `BannedSymbols.txt` contains every required entry (§2.4).
- **Card eraser** on a `%TEMP%\uas-sort-test-<guid>\card\` fake card (a folder tree with `DCIM\DJI_001`, `DCIM\PANORAMA\001_0087`, `MISC\FC9113.db`), opened through the factory's internal constructor with a test-only `IVolumeFacts` that reports the folder as a removable SD volume (the test assembly only; an optional elevated CI lane can use a mounted VHDX instead):
  - **the production factory** (real `WindowsVolumeFacts`) refuses the same `%TEMP%` folder, because it is not a volume root and lies on the system volume;
  - `DeleteFileW` on `\\?\` paths removes exactly the named files, including a Hidden `.trinf`, and leaves siblings;
  - `RemoveDirectoryW` removes the emptied set folder and fails on a non-empty one, leaving it;
  - a read-only file fails with access denied and is not changed;
  - `WindowsCardEraser` consults the policy: a path outside the plan throws `UnsafeIoException` before any Win32 call;
  - the factory refuses a source marked browsed or write-protected, and a source whose facts fail any cleanup volume check;
  - `DeleteFileW` and `RemoveDirectoryW` are declared only in `WindowsCardEraser` (source test).

**View-model tests** (`Review.Tests`, no WinUI)
- Scan → Review; boundary-chip merge; split-here; move to a new group; quick fix as one undo entry.
- Slider: a drag previews and becomes one undo entry; **keyboard/wheel commit after 400 ms idle** (FakeTimeProvider); latest-wins cancellation.
- **Ordering, with `GatedPlanDeriver`** (an `IPlanDeriver` whose derives block until the test opens a gate): a slow earlier preview never overwrites a later one; an edit queued behind a slow derive is validated against the plan that includes the earlier edit; `Changed` arrives off the UI thread and the VM applies it through a fake `IUiDispatcher`; a lower `Revision` is ignored; a `Rejected` edit changes nothing and returns without waiting for a derive.
- Draft resume with dropped edits and pin warnings; a changed `InventoryHash` still offers the draft with the right dropped count.
- Rename: validation, `Rejected` on Append, suggestion ranking; `SuggestionVm.ToString()` equals the text.
- Offload enabled or disabled by blocking issues; preflight acknowledgements; **[Accept and continue]** turns a Blocking `LedgerParseIssue` into a RequiresAck Warning for this session, the draft doesn't carry it, and a rescan raises it again.
- Verdict page: nothing preselected; per-day selection and [Select all photos and sets] only for photos and sets; [Clear selection]; unknown files, truncated clips and other videos one at a time; confirmation counts and GB; undo writes `revoke`.
- Cleanup page: [Clean up card…] disabled during Commit, during a scan, on a write-protected card, for a browsed folder and for a volume that fails the volume check, each with its tooltip; Preparation's Blocking InfoBars (identity mismatch at page open; ledger folder cloud-only or unwritable) disable Delete; Continue disabled until a date is picked; **with the PC zone Alaska and a picked value carrying a late-evening time or a UTC offset, `Request.Before` equals the displayed day**; Free up / Have at least switch the target and the "= free up ≈ Y GB" text; the review list required when the switch is on; "ticked for offload" rows start on Keep and [Delete all] leaves them; rows that join later are undecided and the button reads "Decide N new rows"; Keep/Delete, [Keep all] and [Delete all] recompute the summary; the acknowledgements clear on any fingerprint change; the cutoff line is worded from the plan; the summary shows the evidence split and the "files uas-sort never copies" line; the button reads "Delete 152 files (61.4 GB)" and is disabled until every box is ticked; `CleanupRowVm.ToString()` equals its text.
- Map/grid selection sync.
- `MapBridge` JSON golden messages: deserialise the exact objects of §9.6, the ones map.js produces (**`v` before `type`**), including `ping`/`pong`; `[lon,lat]`; miles labels; an unknown type is logged, not thrown.
- Verdict wording, including the card identity and the name+size split.
- Clock display: the clock-mismatch InfoBar text (site zones named and joined, offsets, dismissed for the session and shown again on the next scan), the "clock ≠ local" chip on exactly the groups with a `ClockMismatch` member, the side-by-side time tooltip; every `ClockMode` and `TimeSource` value has a banner or tooltip text (the test enumerates each enum, so a new value such as `SiteLocal` / `DroneClockSiteLocal` can't be missed).
- `CollectionSync` keeps the selection.

**UI smoke test** (`uas-sort.exe --selftest`, run by `deploy.ps1` against the **Native AOT** publish)
- **Isolation.** It skips single-instance registration and launches off-screen. It uses `%TEMP%\uas-sort-selftest-<guid>` for settings, WebView2 and a temp video root, whose `.uas-sort\` holds the test ledger.
- **Bindings.** It realises one of each templated element from a built-in fake plan and asserts the rendered text:
  - a group card with a boundary-chip header and thumbnail strip;
  - a clip row with a day-split banner;
  - the rename suggestion popup opened in code (text "Anvil Mountain", not a record `ToString`);
  - a Photos tile;
  - typing a space in the rename box leaves `Included` unchanged.
- **Map.** Waits for WebView2 `ready` with MapLibre under the virtual host, sends a ping and waits for the pong.
- **Probes.** Runs StillProbe on the embedded `selftest.dng`, plus a GeoTimeZone and `TimeZoneInfo` lookup.
- **JSON under trimming.** Round-trips the embedded `ledger-v1.jsonl` (every record type, `cardDelete` included) and a closed `PlanEdit`/`TargetChoice`.
- **Placeholder visibility.** Lists this PC's configured video root (read from the real `settings.json`; listing only, with `.uas-sort` excluded) and requires at least one entry showing `0x400000` or `0x1000`. If the root has no cloud-only files, the check reports "not applicable" and deploy needs `-AllowNoPlaceholders`.
- **Result.** Writes `selftest-result.json` (each check's result, plus `firstFrameMs`: process start to the first `CompositionTarget.Rendering` of the main window), then `Environment.Exit(ok ? 0 : 1)`. `deploy.ps1` runs it twice, checks both exit codes and result files, each with a 60 s timeout, and aborts on failure or when the second (warm) run's `firstFrameMs` exceeds 1000.
- **The stack-proof step's minimal selftest** (§14 step 1) is a subset: the probe page renders; WebView2 reaches the virtual host; MetadataExtractor reads the checked-in `SelfTest/stack-exif.jpg` (≤ 2 KB, hand-assembled EXIF with DTO and GPS, no user data) through the Stream API directly; a GeoTimeZone lookup; a closed-record JSON round-trip. The synthetic DNG and StillProbe arrive with the media probes (§14 step 3).
- FlaUI, `winapp ui` and WinAppDriver are **not** used: WinAppDriver is unmaintained, and the others are unverified on WinUI.

**Acceptance on the first real card** (by the user, after the build is complete; §14)
1. **Before anything else,** save a listing snapshot of the card (relative path, size, mtime, creation, **last-access**, attributes).
2. Run `uas-sort-cli plan --card E:\ --json --expect tests/acceptance/first-card-expected.json` (§4.5; the user writes that file before this acceptance run). It passes at ≤ 2 edits. Record the scan + plan time for success criterion 4. Fix classification surprises: LRF presence, cover JPGs (enable the "video cover" rule only if confirmed), the tele-camera suffix, hyperlapse frame names, folder rollover.
3. Re-list the card and diff against step 1. **If LastAccessTimeUtc changed** on exFAT, document it in the verdict text ("Windows updated last-access dates; uas-sort wrote nothing") and suggest the SD lock switch, which shows as the write-protected badge. The app never tries to suppress it: `SetFileTime(-1)` needs a write handle on the card.
4. Do a rehearsal offload in the app with the roots pointed at `%TEMP%\uas-sort-rehearsal\{video,photo}`. Its ledger then lands in `%TEMP%\uas-sort-rehearsal\video\.uas-sort\`. Check:
   - the verdict and the report;
   - OneDrive not syncing `.tmp` files;
   - unbuffered verify and the post-run directory/file flush on a scratch folder on exFAT D:;
   - [Eject D:].
5. Point the video root back at the real library. At the ledger prompt, answer **[Start empty]** so rehearsal records never enter the real ledger; confirm its dialog if it appears (the rehearsal's `file` records match videos already in the library by name and size, and the real library's watermark is exactly what a manual history implies). Then check that the pin sticks: [Keep on this device] on `UAS Videos\.uas-sort` must show as "Always keep on this device" in Explorer (UNVERIFIED until then). Do the real offload. Diff the card listing again (excluding `System Volume Information`).
6. Rescan the same card: everything shows Imported, and the verdict is Safe (or SafeWithAssumptions only for photos the user chose to leave).
7. **Card cleanup.** Snapshot the card listing. Run Before date with a cutoff that takes exactly one old eligible clip and its companions, and confirm. Re-list: only that unit's files are gone, each has a `cardDelete` record, and the rescanned verdict is unchanged for everything else. Put the card in the drone and note what its media browser shows (§15 Q8).

---

## 14. Build plan (single pass)

**Single pass** (user decision, 2026-09-27; §1.1). In the user's words: "I want this to be implemented for the initial version in one shot via the AI agent development. So revise anything that's necessary to build it in one go. Doesn't need to be built progressively or tested in batches or milestones, just the initial full feature-complete version."
- The initial, feature-complete version is built in one pass by an AI coding agent. There are no milestones, effort estimates, checkpoints or staged deliveries, and nothing in this document is deferred except the Later list (§1.4).
- The CLI is a product feature (the dry-run `plan`, §4.5), not a development stage.

**Prerequisites.** The install-once list of §2.6: the .NET 11 SDK, PowerShell 7, and the VS Build Tools C++ workload for Native AOT. The build starts only once they are installed.

**Build sequence.** The parts are built in this order because each uses only the parts before it. The tests of each part are written with it, test-first (§13), in the same pass; there are no separate test batches, and the whole suite stays green as later parts land.

1. **Stack proof.**
   - Builds: the repo skeleton (slnx, props, global.json, pinned `AnalysisLevel`, `Directory.Packages.props` with exact pins, BannedSymbols, the analyzer probe project; build scripts and the optional WSL wrapper; first restore). The real App project with a 30-line probe page: GridSplitter (Sizers), SettingsCard, FolderPicker (owner from `AppWindow.Id`), AppInstance plus the named-mutex fallback, WebView2, and an ItemsView with a template selector. MetadataExtractor is exercised directly on the checked-in `stack-exif.jpg` (StillProbe and the synthetic DNG come in step 3). A check that the VS Code C# extension handles `union` and `closed` without false errors.
   - Verified here: the App restores, builds and **publishes Native AOT (`PublishAot=true`) with `TreatWarningsAsErrors` on**, then passes the minimal selftest (`--selftest`, §13): probe page renders, WebView2 reaches the virtual host, MetadataExtractor Stream read of the checked-in `stack-exif.jpg`, GeoTimeZone lookup, closed-record JSON round-trip. The AOT build's size and warm first frame (second of two runs) are recorded against the ReadyToRun baseline (87–97 MB, 0.37 s). The cross-assembly union/closed exhaustiveness test is green. This settles the UNVERIFIED `AnalysisLevel` 11.0, MetadataExtractor under trimming, AppInstance and Sizers.
   - Fallbacks if the build fails on package compatibility: toolkit 8.2 with the full package, or fixed panes.
   - It comes first so that no later part is built on a package set or publish mode that doesn't work.
2. **Core model, ports and guard policy.**
   - Builds: the Core model (§3) and ports (§4.1); `IoGuardPolicy` (§4.3) with the guard exemption for `ledger*.jsonl` only and the `CardDelete` rules; FakeFileSystem with the hydration tripwire and the card-delete tripwire, calling `IoGuardPolicy`; CardDetector, CardSourceValidator (policy), CardClassifier (fail-safe rules, inventory hash).
   - Tests: the table-driven `IoGuardPolicy` test; classification and source-validation policy tests.
3. **Media probes.**
   - Builds: Mp4Probe (box walker, sample tables, protobuf, model table, generic search, multi-sample search, `mdat` fallback, `tnal` range, `mvhd` duration → `Mp4Info.Duration`); SyntheticMp4Builder and SyntheticDngBuilder; StillProbe (Stream overloads); ThumbnailReader; MetadataHarvester.
   - Tests: synthetic MP4/DNG, incl. the ≤ 16-read budget.
4. **Time and geo.**
   - Builds: the DroneClock learner (SiteLocal, then zones, NearestSample with `ClockChange` runs), TimeResolver (incl. `ClockMismatch`), GpsPlausibility, GeoTimeZoneResolver; PlaceIndex and the GeoNames extract tool `tools/places/build-places.cs` (writes `places.bin.gz`).
   - Tests: the time and clock-learner cases (§13); the PlaceIndex round-trip (§8.7: "Anvil Mountain" ≤ 0.2 mi, "Zachar Bay" ≤ 1.5 mi (user decision 2026-09-28)).
5. **Library index, ledger and settings.**
   - Builds: LibraryIndex (all roots, `previousPhotoRoots`, fixed watermark; `.uas-sort` excluded via `excludeDirNames`); ledger reading (derived `<videoRoot>\.uas-sort` location via `LedgerPaths`, multi-file union, dedupe, parse issues, `torn` markers, revokes, attribute-first `LedgerFolderStatus`); settings with recovery and the read-only load.
   - Tests: newness-ledger (incl. `torn` markers) and settings tests.
6. **Planning and editing.**
   - Builds: NewnessRules (with `seen`, per-member set records, the no-watermark rule), Clusterer with causes and the structural-edit algorithm (§8.9), DaySplitFinder, FolderDecider (New subset with `included`, cross-day rules, two-pass UserSplit), FolderNamer and SetFolderNamer, DescriptionSuggester, Planner (issue catalogue), PlanSession (threading contract, pins, quick fixes), ScanService.
   - Tests: the 37 ported tests (§13 table), the new planning cases, edit invariants, golden replay (A0, A–E) from the checked-in fixture, the derive benchmark, and the scan half of the fake-FS tripwire.
7. **Offload engine and audit.**
   - Builds: OffloadCompiler, Preflight, CopyEngine (identity per file, confirm, post-run flush), the Commit ledger writes (`file`, `folder`, `seen`, `run`), CardAudit (re-list, per-file, worst-of) and the verdict, reports.
   - Tests: fault injection (every §10.3 row), the audit table, the offload half of the tripwire.
8. **Card cleanup** (§10.6).
   - Builds: the cleanup types, `CleanupVolumeCheck`, CleanupPlanner (eligibility from fresh listings, units and companions, both modes and both free-space readings, cutoff, shortfall, ticked-for-offload and undecided rows, fingerprint, `Confirm` with re-validation), CleanupExecutor (the whole run: lock, keep-awake, thumbnail pause, ledger preparation, per-file checks, evidence re-check, outcomes, `cardDelete` records).
   - Tests: the §13 Card cleanup tests and the card-delete tripwire.
9. **Platform implementations.**
   - Builds: `WindowsVolumeProvider` (incl. the `VolumeInfo` bus fields), WindowsDirectoryLister (explicit options, error list), `WindowsCardReaderFactory`/WindowsCardReader (identity, read-only, `Space()`), `WindowsCardEraser` and its factory with `WindowsVolumeFacts` (volume root, FS, bus, removable media, system/boot/paging), GuardedFileOps and WindowsFileOps (`LibraryImport`, `\\?\`, `File.OpenHandle`), `LedgerStore` (`Check`, `EnsureFolder`, `OpenOwn` with torn-tail repair, mirror, `SnapshotToBackup`), the settings, draft and report stores (incl. `CleanupReport`), PlaceholderGuard, sync-root detection, canonical paths (`PathFacts`), power request, offload lock, eject, single-instance support, shell launcher, logging.
   - Tests: the Windows integration tests (§13), incl. the card eraser on a `%TEMP%` fake card; the BannedSymbols content test.
10. **Review view models.**
    - Builds: every VM of §4.2 (stages, Review, Commit, Cleanup, Settings), `MapBridge` and its messages, `CollectionSync`.
    - Tests: all view-model tests (§13), incl. the `GatedPlanDeriver` ordering tests and the Cleanup VM tests; the `MapBridge` golden messages.
11. **WinUI App.**
    - Builds: `Program.Main` single instance; TitleBar, Mica and Frame; the stages. The Setup stage and Settings page (roots; derived ledger-folder status with [Keep on this device] and [Open]; the video-root change prompt ([Copy] / [Start empty]); drone clock; map URLs; About). Card and Scan pages. Review: timeline ItemsView (cards with chip headers, folds), group card (rename with SuggestionVm, read-only on Append, retarget flyout built on Opening, Browse existing validation), clip list (day-split banners, `Thumb.Key` thumbnails), Photos and Other tabs, the clock banner, clock-mismatch InfoBar and "clock ≠ local" chip, undo/redo, drafts, scoped keyboard handling, queued dialogs. The map pane: **first** the MapLibre ESM and worker check under the WebView2 virtual host (`.mjs` MIME, §9.6) with the `.js`-rename / WebResourceRequested / Leaflet fallbacks, then the bridge wiring, selection sync, live R/G, base toggle and offline canvas. Commit: preflight sheet with acknowledgements, Copy page, Verdict page. The Cleanup page with [Clean up card…] on the Verdict page and in the title bar. Device-arrival refresh (WM_DEVICECHANGE via window subclassing; UNVERIFIED), disabled during Commit. The full `--selftest`.
    - Tests: the XAML lint; the full selftest checks (§13), incl. the template checks and `ready` + `ping`/`pong`.
12. **CLI.**
    - Builds: `uas-sort-cli plan` (§4.5), incl. `--expect`.
    - Tests: `uas-sort-cli plan --json` runs on a copied card folder under `%TEMP%` and writes nothing.
13. **Native AOT publish and deploy.**
    - Builds: the Native AOT publish for `win-x64` (x64 only, user decision 2026-09-28); `deploy.ps1` (AOT publish, selftest gate, `.lnk`, keep 2 versions).
    - Tests: `deploy.ps1` passes its selftest gate on x64, including the warm first frame ≤ 1 s.

**Completion criteria.** The one-pass build is complete when all of these hold:
- the full solution builds with `TreatWarningsAsErrors`;
- the whole test suite passes (§13): unit, IO guard policy, card cleanup, invariants, performance, newness and ledger, golden replay, synthetic media, fake-FS tripwire, fault injection, Windows integration and view-model tests, and the CLI run above;
- `tools/build.ps1 -CheckBannedApi` builds the probe and raises every expected RS0030;
- the Native AOT publish passes `--selftest`, through the `deploy.ps1` gate on x64 (both runs exit 0, and the warm first frame is ≤ 1 s).

**Acceptance by the user, after the build.** The first-real-card steps of §13 accept the finished product; they are not build stages: the CLI dry run matches `tests/acceptance/first-card-expected.json` within 2 edits, with the card-listing diff; a rehearsal offload to scratch roots (`%TEMP%\uas-sort-rehearsal\{video,photo}`); the real offload; and Card cleanup of one old eligible clip, so the first real cleanup runs on a card whose files the app has just verified. Anything they turn up (classification or time surprises, the cover-JPG rule, exFAT behaviour) is fixed in the finished code, and the completion criteria are re-run.

**Maintenance after the build.** The SDK **RC2** (~Oct 13, UNVERIFIED) and **GA** (Nov 10) updates are applied when each ships. Each re-runs the Native AOT publish, `--selftest` and the analyzer check; the GA update also moves the development PC to the GA SDK and sets `allowPrerelease:false` (§2.2, §2.6). `AnalysisLevel` stays pinned, so new rules don't appear by surprise.

**If Native AOT fails a concrete check** (a build or AOT warning, a selftest failure, or a warm first frame slower than the 0.37 s ReadyToRun baseline), a ReadyToRun publish of the same commit is used only to diagnose it, and the problem is raised with the user rather than silently switching the build type (§1.1).

---

## 15. Questions: accepted defaults and what is still open

- **Former Q1 (which synced folder holds the ledger) is resolved and removed.** The user decided the ledger lives in `<videoRoot>\.uas-sort\` (§1.1, §4.3, §11).
- **Former Q5 (packaging and code generation) is resolved and removed** (user decision, 2026-09-27): Native AOT is the primary build from the start of the build (§14 step 1), the C++ build tools are a prerequisite (§2.6), and the unpackaged, non-MSIX departure stays for certificate trust (§1.1). VS Code with the C# Dev Kit is the default editor, and Visual Studio 2026 Insiders stays optional (§2.6). The remaining questions are renumbered.
- The user approved the design on 2026-09-27 and accepted the defaults of items 1–7 without objection. Item 8 was added with Card cleanup the same day; its default is proposed and not yet reviewed by the user. None of them waits on a decision.
- What is genuinely open is marked **Still open**. Each such item is an UNVERIFIED fact with a planned check or a fallback.

1. **Unfinished (truncated) recordings: ticked or unticked by default?**
   - *Accepted default:* unticked, so the user can try the in-drone repair first. GPS is recovered through the `mdat` fallback (§6.3 step 7). The verdict stays "Don't format yet" until the clip is copied or individually marked not needed.
   - **Still open:** whether powering the Air 3S on with the card inserted repairs the clip (UNVERIFIED, §7.5).
2. **Copy the JPG twin of a DNG?**
   - *Accepted default:* yes. When a twin is present, both go to the photo root, and Lightroom treats the JPG as a sidecar. There is a switch in Settings. When it's off, twins are listed as SkippedByRule.
3. **Esri satellite imagery without a key.**
   - *Accepted default:* Esri World Imagery as the satellite source for personal use. USGS (public domain, sharp in Alaska) is a one-click preset in Settings. The URLs live in settings.
   - **Still open:** the Esri terms for use outside ArcGIS apps are UNVERIFIED. If they rule it out, the default switches to the USGS preset with no code change.
4. **Daily flights chaining together.** With R = 50 mi and G = 1, flights on consecutive days in the same area (for example, a week at home) become one multi-day folder.
   - *Accepted default:* no cap on how many days a group spans. Every day change shows a one-click split suggestion, emphasised at 10 mi or more. Next-day appends at 10 mi or more are Medium. A maximum-span setting stays on the Later list.
5. **Are any of the PCs ARM64?**
   - **Resolved: x64 only (user, 2026-09-28)** — "I'm not expecting to run on any ARM64 computers. Both of my personal laptops are regular x86 Intel." `RuntimeIdentifiers=win-x64`, `Platforms=x64`; `deploy.ps1` publishes `win-x64`; there is no ARM64 publish step.
6. **PowerShell 7.** The scripts use `pwsh`, which isn't installed on this PC.
   - *Approved:* the user installs it once with `winget install Microsoft.PowerShell` at implementation start, alongside the .NET 11 SDK preview and the C++ build tools (§2.6 Prerequisites). Every script runs via `pwsh -NoProfile -ExecutionPolicy Bypass -File`.
7. **Autel cards.**
   - *Accepted default:* Autel cards are not supported; their media shows as Unknown (NotSafe). Legacy Autel folders already in the library still match by name and size, and their members' start times come from mtime (§7.1).
8. **Does the drone's `MISC` media index cope with PC-side deletions?** (added with Card cleanup, 2026-09-27)
   - *Proposed default (2026-09-27, not yet reviewed by the user):* Card cleanup deletes only DCIM media and their companions and never touches `MISC\FC*.db`, `IDX` or `THM`. The result page says the drone may show stale thumbnails until it rebuilds its index, and that formatting in the drone remains the clean option.
   - **Still open:** whether the Air 3S rebuilds its index or shows stale entries after PC-side deletions (UNVERIFIED; observed in acceptance step 7, §13). Also UNVERIFIED until the first-card acceptance (§13): which `MISC` index files the Air 3S writes and which bus type and `RemovableMedia` value the user's reader reports, both used by the cleanup volume check (§10.6); if either differs, the check is adjusted to what that acceptance run records. **Resolved 2026-10-04 (user decision):** the user's Air 3S card holds `MISC\FC9113.db`; the laptop's SD slot is a Realtek RTS5208 PCIe reader that reports a SCSI/Unknown bus, so rule 4 now accepts removable media on any bus (SD, MMC, USB, or a PCIe/SCSI card reader) and still refuses fixed media.

---

## Review disposition

This table records how all 53 findings of the three review lenses (15 requirements, 22 safety, 16 feasibility; `docs/research/09-design-review-findings.json`) were handled before approval. Row numbers are the findings' 1-based positions in that file. Rows that a later user decision changed carry a *Note*.

| # | Lens | Severity | Finding (one line) | Disposition | Reason / where |
|---|---|---|---|---|---|
| 1 | requirements | major | Banned list covers only writes; library **reads** and `UtcNow` are unbanned | **Applied** | §2.4: whole-type and read bans, `UtcNow`/`DateTimeOffset` bans, a content test and a probe build; ICardReader/IFileOps are the only openers (§4.3) |
| 2 | requirements | major | "Personal computers" means several PCs, but the ledger was local to one | **Applied** | §1.1 fact; §11: per-machine files, union read, pinned-folder check without UnsafeIoException, local backups. *Note: superseded by user decision: ledger in `<videoRoot>\.uas-sort` (derived, no `ledgerDir` setting; the guard exemption is in §4.3). The former §15 Q1 was removed.* |
| 3 | requirements | major | Settings UI and first-run root choice unscheduled; user path hardcoded | **Applied** | §9.1 Setup stage, §9.14 Settings page; defaults derived from KnownFolder Pictures (the video root; the ledger folder follows it); scheduled in M6a (+1 day). *Note: the former synced-folder ledger default is superseded by user decision: ledger in `<videoRoot>\.uas-sort`, and Setup no longer asks for a ledger folder.* *Note: superseded: single-pass build (user decision 2026-09-27).* |
| 4 | requirements | minor | 3 R-dependent tests (not 1); Zachar↔Kodiak fixture is 53.4 mi | **Applied** | Re-verified 2026-09-27 (34/37 pass at 50 mi); §13 lists all three with the pin or duplicate plan; §8.4 shows 53.4 mi and a 3.4 mi margin |
| 5 | requirements | minor | Unpackaged and no-AOT silently deviate from the "most modern frameworks" decision; System.* not on 11.x | **Applied** | §1.1 deviation table; §15 Q5 options A/B/C (A approved 2026-09-27; AOT an optional later attempt in M10); System.IO.Hashing 11.0.0-rc.1. TimeProvider.Testing stays 10.10.0 because no 11.x exists. *Note: superseded by user decision (2026-09-27): Native AOT is the primary build from M0, the C++ build tools are a prerequisite, and only the MSIX departure remains (§1.1, §2.1, §2.6); the former §15 Q5 was removed.* *Note: superseded: single-pass build (user decision 2026-09-27).* |
| 6 | requirements | minor | Browse could select a library folder and probe library content | **Applied** | `CardSourceValidator` (§4.3) refuses equal, inside or containing roots, previous roots, ledger and sync roots; tests in §13 |
| 7 | requirements | minor | Rename meaning on Append/AlreadyImported undefined | **Applied** | §9.4 read-only box with hint; §8.9 `Rejected(RenameExistingFolder)`; VM test |
| 8 | requirements | minor | Photos in the old Picture Offload are not recognised after the photo root moves | **Applied** | §7.1/§7.3: photos matched against all root listings plus auto-maintained `previousPhotoRoots`; test in §13 |
| 9 | requirements | minor | Scope creep (Autel, make-fake-card, USGS button, NE outlines, map labels, key forwarding, recent offloads, 960 px previews, flight column, RC2 row, StaleFirstFix, GpsTime) | **Applied** | §1.4 non-goals and Later list; Autel only in LibraryIndex; FakeCardWriter moved to Testing; USGS as a settings preset. The map and GeoNames suggestions stay (they are part of the approved rich review UX and research grafts) |
| 10 | requirements | minor | Next-day ≥ 10 mi append labelled High | **Applied** | §8.5 uses the 10 mi threshold → Medium with reason and hint; Scenario D now expects Medium (§13) |
| 11 | requirements | minor | 22-day estimate understated; Settings not scheduled | **Applied** | §14 re-baselined: 34.5 days + contingency ≈ 38; M6 split into M6a/M6b. *Note: Card cleanup (user request, 2026-09-27) adds M7b (2 days): 36.5 days + contingency ≈ 40.* *Note: superseded: single-pass build (user decision 2026-09-27).* |
| 12 | requirements | minor | Exact package set never built together; TreatWarningsAsErrors risk | **Applied** | M0 exit criterion: full pinned set incl. Sizers, trimmed + ReadyToRun with warnings as errors, `--selftest`; fallbacks named. *Note: the M0 exit publish is now Native AOT with warnings as errors (user decision, 2026-09-27; §14)* *Note: superseded: single-pass build (user decision 2026-09-27).* |
| 13 | requirements | minor | Nothing checks that the card is left unmodified | **Applied** | Automatic audit re-list diff (§10.5); acceptance before/after snapshots incl. last-access (§13); read-only access ACL test. *Note: still true for the offload. Card cleanup (user decision, 2026-09-27; §10.6) is a separate, confirmed action whose deletes are checked by a re-list and recorded in the ledger.* |
| 14 | requirements | minor | Undefined types, unpinned versions, selftest DNG source, DraftKey hash | **Applied** | §3 supporting types, `TargetChoice` as a closed record with `[JsonPolymorphic]`; versions pinned from the 2026-09-27 NuGet query; SyntheticDngBuilder asset; DraftKey = XxHash64 of the lowercase canonical root |
| 15 | requirements | minor | x64 hardcoded; a PC may be ARM64 | **Applied** | §1.2 assumption row; §15 Q5. *Note: superseded: x64 only (user decision 2026-09-28); §2.5 RIDs `win-x64`, deploy publishes `win-x64`.* |
| 16 | safety | blocker | App copies advance the watermark and flip unseen photos to "probably imported" | **Applied** | §7.1 watermark from outside-app imports only; §10.4 `seen` records at every Commit end; §7.3 rule 3; two-run and cancel tests (§13) |
| 17 | safety | blocker | Classification fall-through to SkippedByRule; Browse anchoring makes everything "outside DCIM" | **Applied** | §5 fail-safe Unknown for unclaimed media, named skips only, cover JPGs Unknown until proven; DCIM anchoring and refusal (§4.3); NotSafe audit test |
| 18 | safety | major | Default `EnumerationOptions` drop Hidden/System files and swallow errors | **Applied** | §5/§4.1 explicit options, `ContinueOnError` error list → `ForcesNotSafe`; Hidden/System Platform test; enumeration types banned outside Platform |
| 19 | safety | major | No card identity or contents re-check at Commit or verdict | **Applied** | §4.1 `CurrentIdentity`; §10.2 Blocking; §10.3 step 0 `CardSwapped`; §10.5 re-list diff; identity in the headline; refresh disabled during Commit (§9.1) |
| 20 | safety | major | Rename durability not guaranteed; exFAT D: at risk | **Applied (partly)** | §10.3 post-rename `ConfirmFinal`, post-run file and directory flush for non-NTFS volumes, safe-removal note + Eject; G1 no longer claims write-through durability. The `MOVEFILE_WRITE_THROUGH` flag itself stays because the approved Safety graft names a write-through rename, and the flag is harmless |
| 21 | safety | major | Ledger can be silently erased (OneDrive conflicts, bad lines, no backup) | **Applied** | §11 per-machine files and union incl. conflict copies (dedupe by `id`), bad line → Blocking and verdict cap, selftest v1 round-trip, local backups, and the video-root-change prompt with [Copy] / [Start empty]. *Note: superseded by user decision: ledger in `<videoRoot>\.uas-sort` (the `ledgerDir`-change warning became the video-root-change prompt).* |
| 22 | safety | major | Fixed-offset clock breaks under DST; CheckDate window too small | **Applied** | §6.1 zone learner used for items, library members and the watermark; §7.3 rule 5 near-watermark ±75 min; §6.5 CheckDate 75 min for `Mvhd`/`DroneClock*`. *Note: user decision (2026-09-27) adds the SiteLocal mode and the `ClockMismatch` flag (§6.1, §6.5).* |
| 23 | safety | major | Scenario D appends next-day footage to yesterday's folder as High, no preflight warning | **Applied** | §8.5 New-subset judgement → Medium + hint; UserSplit rule so a split gives NewFolder; Medium appends and emphasised splits need a preflight acknowledgement; golden test |
| 24 | safety | major | Rename/Retarget pins silently apply to changed membership; merge drops a pin | **Applied** | §3 `PinnedMembers`; §8.9 membership Warning with [Keep]/[Reset] (preflight acknowledgement) and conflicting pins → Blocking |
| 25 | safety | major | Bulk "Mark as not needed" can permanently hide footage | **Applied** | §10.5 nothing preselected, confirmation with counts and GB, no bulk for videos, Dismissed section with Un-dismiss (`revoke`), undo on the page. *Note: Card cleanup (user decision, 2026-09-27) can delete clips that are not in the library, but only after an opt-in switch, a required per-row review (date, clip length, location), a second acknowledgement, and never for unknown or changed files (§10.6).* |
| 26 | safety | major | Read APIs not banned (duplicate of #1) | **Applied** | §2.4 (merged with #1 and #41); probes accept Streams only |
| 27 | safety | major | Browse into library/sync roots; placeholder attribute visibility unproven | **Applied (mechanism adjusted)** | §4.3 sync-root detection plus canonical paths; placeholder compatibility call at startup. `--selftest` requires **any** cloud-only entry in the video root to show 0x400000/0x1000, rather than one named Kodiak file that could get hydrated, with an explicit override |
| 28 | safety | major | Audit per unit undefined for multi-file units, twins and truncated matches | **Applied** | §10.5 per-file lines, worst-of per unit, twin lines (AssumedByRule or SkippedByRule), truncated name+size cap; table tests |
| 29 | safety | minor | `nameSize` records counted as InLedger; headline doesn't separate them | **Applied** | §10.5 mapping to NameSizeMatch; the headline shows the "matched by name+size only" count |
| 30 | safety | minor | Set identity by name+size only; empty folder treated as Resume | **Applied** | §8.8 mtime ±2 s, ledger first-frame check, empty folder → Plain |
| 31 | safety | minor | Preflight doesn't re-check that Append targets still exist | **Applied** | §10.2 Blocking `AppendTargetGone` "folder renamed or moved since the scan; rescan"; §10.3 step 3 (`EnsureDirectory` creates only NewFolder paths, never an Append target); fault test (§13) |
| 32 | safety | minor | Generic lat/lon search can accept attitude/velocity fields | **Applied** | §6.3 step 5b plausibility gate, `GpsGuessed` flag, else `NoFix` |
| 33 | safety | minor | `Etc/*` fallback depends on clustering, so dates move with R | **Applied** | §6.4 nearest land-zone GPS item on the whole card; invariant test "local dates never change with R/G" |
| 34 | safety | minor | exFAT last-access writes on read | **Applied** | M4 and acceptance check; documented in the verdict text if it happens; write-protect badge; no `SetFileTime` suppression. *Note: superseded: single-pass build (user decision 2026-09-27).* |
| 35 | safety | minor | P/Invoke paths lack long-path handling; temp path not length-checked | **Applied** | `\\?\` prefix on every P/Invoke path, 400-character check on the temp path, 300-character integration test |
| 36 | safety | minor | Folding hides Conflict/Truncated members | **Applied** | §8.5 fold rule; §9.3 folds only clean groups; test |
| 37 | safety | minor | Corrupt settings silently revert the photo root | **Applied** | §11 recovery → Blocking until roots confirmed; roots recorded in `run` records and offered back |
| 38 | feasibility | major | 22 days unrealistic; first real offload only at the end; SDK bumps mid-build | **Applied** | §14 re-baseline (≈ 38 days), Commit UI (M7) before Map (M8) so a real offload is possible around day 28, SDK bumps scheduled when they ship, VS Code union/closed check in M0. *Note: M7b (Card cleanup, 2 days) follows M7, so the first real offload stays around day 28; the total is now ≈ 40 days.* *Note: superseded: single-pass build (user decision 2026-09-27).* |
| 39 | feasibility | major | Trimming breaks property-path features (AutoSuggestBox ToString in folder names); ItemsSource types untested | **Applied** | §2.4 XAML lint bans property paths; `SuggestionVm` + `UpdateTextOnSelect=False`; ItemsSource rule (§2.7 #4); selftest realises each template and asserts text |
| 40 | feasibility | major | ListView can't make chip or split rows non-selectable; Space conflicts | **Applied (different mechanism)** | `ItemContainer.CanUserSelect` is documented only in the 2.0-experimental view (checked 2026-09-27), so chips and day-split banners are embedded in the following item's template instead. Every item is selectable, and buttons stay live (§9.3, §9.5). Space is list-scoped (§9.12) |
| 41 | feasibility | major | BannedApiAnalyzers can't express "File.* writes"; many write paths are open | **Applied** | §2.4 whole-type bans incl. WinRT storage, VB FileSystem and Process.Start; member list and pragmas in Platform; make-fake-card moved to Testing; `DownloadStarting` cancelled; probe build |
| 42 | feasibility | major | Selftest gate can't fail or can hang; single-instance details missing | **Applied** | §13 selftest isolation, `Environment.Exit(ok?0:1)` + result JSON + 60 s timeout; §4.4 custom Main, worker-thread redirect, `AllowSetForegroundWindow`; mutex fallback tested in M0; offload lock. *Note: superseded: single-pass build (user decision 2026-09-27).* |
| 43 | feasibility | minor | Map JSON needs `type` first, but map.js sends `v` first; unknown types throw | **Applied** | §9.6 `AllowOutOfOrderMetadataProperties`, try/catch with logging, golden test with `v` first, GeoPoint converter |
| 44 | feasibility | minor | `.mjs` MIME under the virtual host is unverified | **Applied** | §9.6: M8 first task plus ordered fallbacks (rename to `.js` + `setWorkerUrl`, WebResourceRequested with explicit Content-Type, Leaflet); selftest waits for `ready`. *Note: superseded: single-pass build (user decision 2026-09-27).* |
| 45 | feasibility | minor | Win32 details: write-through meaningless, AllowUnsafeBlocks, CreateFileW unnecessary, noisy handle probe | **Applied (flag kept)** | `AllowUnsafeBlocks`; `File.OpenHandle` + NO_BUFFERING; deterministic FileShare.None lister test; criterion 1 reworded; write-through test and claim dropped. The flag stays because the approved Safety graft names it (harmless) |
| 46 | feasibility | minor | exFAT last-access may write to the card (duplicate of #34) | **Applied** | M4 checkpoint and acceptance; write-protect detection. *Note: superseded: single-pass build (user decision 2026-09-27).* |
| 47 | feasibility | minor | TreatWarningsAsErrors vs trim warnings from rooted assemblies; AnalysisLevel drift | **Applied** | §2.5 targeted `NoWarn IL2104` only if M0 shows it; `AnalysisLevel` pinned; 0.5 day per SDK bump. *Note: superseded: single-pass build (user decision 2026-09-27).* |
| 48 | feasibility | minor | Lean package combination and cross-assembly `closed`/`union` untested | **Applied** | M0 exit build of the exact set; Review.Tests exhaustive-switch and JSON round-trip tests. *Note: superseded: single-pass build (user decision 2026-09-27).* |
| 49 | feasibility | minor | Slider has no drag-completed event; UI-thread derive may stutter | **Applied** | §9.7 `AddHandler(PointerReleased, handledEventsToo)` + `PointerCaptureLost`, 400 ms idle commit, background derive with latest-wins; FakeTimeProvider test |
| 50 | feasibility | minor | Unmodified-key accelerators collide with list and TextBox input | **Applied** | §9.12 page accelerators use modified keys only; list-scoped keys; TextBox check for undo; smoke test |
| 51 | feasibility | minor | Thumbnails need images without WinUI types in VMs; recycled containers | **Applied** | §9.5 `ThumbKey` + `Thumb.Key` attached property with key re-check; `IThumbnailSource` returns bytes |
| 52 | feasibility | minor | TeachingTip hover, ContentDialog settings, nested dialogs, MenuFlyout binding, Browse restriction | **Applied** | Hover preview cut to Later (a ToolTip would be used if restored); Settings as a Page; queued `IDialogService`; flyout built on Opening; Browse result validated with an InfoBar |
| 53 | feasibility | minor | `pwsh` not installed; execution policy | **Applied** | PowerShell 7 chosen (most modern) as a one-time user install, approved 2026-09-27; all scripts run via `pwsh -NoProfile -ExecutionPolicy Bypass -File` (§2.6, §15 Q6) |