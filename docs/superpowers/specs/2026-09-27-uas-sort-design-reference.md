# uas-sort — design reference (approved 2026-09-27)

This is the detailed companion to the main spec, [`2026-09-27-uas-sort-design.md`](2026-09-27-uas-sort-design.md). It holds the full types, rules, thresholds, tables and test lists behind it. **If the two disagree, the main spec wins.**

- **Approved:** 2026-09-27. The user reviewed and approved every design section: approach, stack, rules, Review UI and offload safety. The user also chose the ledger location (`<videoRoot>\.uas-sort\`, §11) and accepted the remaining defaults (§15).
- **Still marked UNVERIFIED:** facts nobody has proven on this hardware yet. Each is scheduled for a milestone check (§14) or has a named fallback. None of them waits on a user decision.

**Evidence.** "The spike" and "the replay" in this document refer to these research reports and spike files in the repo:

| Topic | Report | Spike files |
|---|---|---|
| Card layout, `.trinf` recordings | `docs/research/01-card-layout.md` | `docs/research/spikes/card-layout/` |
| djmd GPS reader, calibration, `tnal` thumbnails | `docs/research/02-djmd-gps-calibration.md` | `docs/research/spikes/djmd/djmd_gps.py`, `calibrate.py`, `calibration.json`, `synth_test.py`; `docs/research/spikes/review-ux/mp4thumb.py` |
| Libraries: MetadataExtractor, GeoTimeZone, XxHash128 (9.2 GB/s), ledger at 10,000 rows, unbuffered verify, GeoNames | `docs/research/03-dotnet-libraries.md` | `docs/research/spikes/dotnet-stack/` (incl. `geonames/rg.py`) |
| Grouping algorithm, the 37 Python tests, library replay | `docs/research/04-grouping-algorithm.md` | `docs/research/spikes/grouping/grouping.py`, `test_grouping.py`, `replay.py`, `replay50.py`, `more_gps.json` |
| Approach scoring and grafts | `docs/research/05-approach-judging.md`, `docs/research/05-approaches.json` | `docs/research/spikes/judge/crossday.py`, `docs/research/spikes/minimal-arch/`, `docs/research/spikes/safety-review/` |
| .NET 11 / C# 15 / WinUI 3 lean stack, trim + ReadyToRun, warm start | `docs/research/06-modern-stack.md` | `docs/research/spikes/modern-stack/` (`winui-src/`, `rc1-*.md`) |
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
- The card is copy-only: nothing on it is ever written, renamed or deleted.
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
- The drone clock is US Eastern, and MP4 `creation_time` is true UTC.
- Folder dates are the local date at the site, found from GPS via the time zone.

**Frameworks**
- The user wants the most modern frameworks: the latest .NET and C#, WinUI 3 on the latest Windows App SDK, and modern packaging. There are no compatibility constraints ("personal computers").
- **Approved stack (2026-09-27):**
  - .NET 11 (RC1 go-live now, then RC2, then GA on Nov 10) with C# 15;
  - WinUI 3 on Windows App SDK 2.5.1, lean component packages;
  - WebView2 with vendored MapLibre: OpenFreeMap streets, Esri World Imagery satellite, and a USGS preset;
  - offline GeoNames suggestions;
  - an unpackaged, self-contained, trimmed ReadyToRun folder plus a Start-menu `.lnk`. No MSIX. No Native AOT (an optional later attempt).
- The user installs the .NET 11 SDK preview and PowerShell 7 with winget when implementation starts.

**Grafts to include**
- From Safety: re-check card files before copying, a write-through rename that never replaces, hidden temp files, keep the PC awake, single instance, audit categories behind the "safe to format?" verdict, a banned-API analyzer, and a dry-run CLI.
- From research: GPS for truncated clips, a multi-sample GPS search, a per-model GPS table plus a generic search, and offline GeoNames name suggestions.

**Where this design departs from "most modern frameworks"** (the user approved each departure on 2026-09-27; see §15 Q5)

| Area | Most-modern option | What this design does | Why |
|---|---|---|---|
| Packaging | MSIX with package identity | **Unpackaged, self-contained folder** plus a Start-menu `.lnk` | MSIX needs a trusted certificate on every PC (or Developer Mode registration). Framework-dependent MSIX needs Windows App Runtime 2 ≥ 2.5.1, and winget only offers 2.3.1. Self-contained MSIX is UNVERIFIED. The app needs no identity (no notifications or background tasks); single instance works unpackaged (§4.4). |
| Code generation | Native AOT | **Trimmed + ReadyToRun** (0.37 s warm first frame). AOT is an optional later attempt: M10 tries it behind a flag **only if** the user has installed the C++ build-tools component | AOT can't build here without the VC tools. Those are a machine-wide install, so installing them is up to the user. Its startup gain over ReadyToRun is UNVERIFIED. |
| Toolkit controls | Stable releases | **8.3.260402-preview2** | It is the only toolkit line that works with the lean WinUI package set. Fallback: 8.2.251219 with the full Windows App SDK package. |
| Test clock package | 11.x | TimeProvider.Testing **10.10.0** | No 11.x is published. |

### 1.2 Assumptions (not stated by the user; each is cheap to undo)

| Assumption | If wrong |
|---|---|
| Every PC runs Windows 11 24H2+ (build ≥ 26100), **x64 or ARM64** | Lower `TargetPlatformMinVersion`. The modern-stack spike also ran with 10.0.22621.0 (`docs/research/spikes/modern-stack/winui-src/Spike.App/Spike.App.csproj`). `deploy.ps1` already publishes for the machine's own architecture; ARM64 is UNVERIFIED (§15 Q6). |
| One card, or one drone volume, per run; ≤ ~1,000 files; ≤ 256 GB | Offload the second volume in a second run |
| The Air 3S layout matches the research: `DCIM\DJI_###[_x]`, `DCIM\PANORAMA\<set>`, `DCIM\HYPERLAPSE\<set>`, `MISC` | Anything else falls to **Unknown**, which is Unaccounted and therefore NotSafe (§5). Add a rule after the first-card acceptance. |
| The drone clock follows `America/New_York` **including DST** | The zone learner (§6.1) finds the mismatch on the first card with MP4s and falls back to the nearest sample |
| Only DJI cards are offloaded; Autel exists only as 2022 library content | An Autel card's media all shows as Unknown (NotSafe); Autel support would be added later (§15 Q8) |
| There is internet at home, maybe not on trips | The map falls back to an offline canvas |
| Lightroom removes duplicates on its own imports | A double copy of a photo is harmless |
| Nothing else writes to the library during an offload | The rename never replaces anyway |
| The video root is synced to every PC (OneDrive today), so `<videoRoot>\.uas-sort\` reaches them all and can be pinned "Always keep on this device" | A PC whose video root isn't synced keeps a ledger that only that PC sees. Setup and Settings show the ledger status ("no other PCs' ledgers found"), so the user can tell. The local backup in `%LOCALAPPDATA%\uas-sort\ledger-backup\` is unaffected |

### 1.3 Goals

| # | Goal |
|---|---|
| G1 | **Zero-risk offload.** The card is never written, library files are never overwritten, and library content is never read. The app's own ledger files in `<videoRoot>\.uas-sort\` are the one exemption for pre-existing files (§4.3). Every copy is hashed while copying and re-read before it gets its final name. The re-read uses an unbuffered handle; when a buffered fallback is used, that is recorded. Durability rests on `FlushFileBuffers` before the rename, a size check after it, a directory flush on non-NTFS destinations, and safe removal of external drives. It is **not** claimed from the rename's write-through flag. |
| G2 | **Proposals that explain themselves.** Every group boundary states its cause, every photo decision states its reason, and every append states its confidence and reason. |
| G3 | **One-gesture fixes.** Merge, split, move, rename, retarget, include a day, or move the R/G sliders. Everything can be undone, and drafts survive a restart. |
| G4 | **An honest, itemised "safe to format?" verdict about the card that is in the reader right now**: identity re-checked and contents re-listed at verdict time. |
| G5 | **A native Windows 11 app** (WinUI 3, Mica, TitleBar, system dark mode) that starts in under 1 s. |

### 1.4 Non-goals

- Any change to the card: deleting, formatting, or repairing `.trinf` recordings.
- Reading, renaming, moving or de-duplicating library files; any access to the Lightroom catalog. The app's own `<videoRoot>\.uas-sort\` ledger folder is not library content (§4.3).
- Copying LRF or SRT files; playback, transcoding or MP4 repair.
- Offloading Autel or other non-DJI cards.
- Tray icon, service, auto-launch, auto-update, telemetry.
- Nominatim or any other online name lookup; offline street or satellite tiles; Store packaging.
- Parallel copies, pausing, resuming a half-copied file, or a crash journal. Temp-then-rename plus a safe re-run covers these cases.
- Editable photo groups or photo folders. Photos always land flat, except sets.

**Later (explicitly deferred, not in the milestones)**
- Map: GeoNames place labels (and the `viewport`/`places` messages), Natural Earth outlines, keyboard forwarding from the map, Shift-drag box select, and a Protomaps offline street map.
- Review: 960 px hover previews (MP4 `covr` / DNG preview ranges), a flight-number column, and a "Recent offloads" list.
- Media: a stale-first-fix check (last-sample comparison for known models), GPS-time capture sources for other DJI models, a USGS toolbar button (USGS stays available as a settings URL), and a maximum group span.
- Deployment: MSIX packaging; Native AOT if the M10 attempt doesn't happen.

### 1.5 Success criteria

1. **Safety is enforced mechanically.**
   - Whole-type bans on file-system APIs apply outside Platform (§2.4). `tools/build.ps1` builds a probe project containing one call of each banned kind and expects every one of them to raise RS0030.
   - The table-driven `IoGuardPolicy` test passes (§4.3, §13), and the fake-FS hydration tripwire passes over scan, plan and offload.
   - Windows integration tests prove:
     - the rename never replaces;
     - verification opened an unbuffered handle (or recorded the buffered fallback);
     - the lister opens no files;
     - no pre-existing library file is opened except `.uas-sort\ledger*.jsonl` (read) and the own ledger file (append); this run's own `*.uas-sort.tmp` and just-renamed files are tracked exceptions;
     - the card reader works on files whose ACL denies write access.
2. **The golden replay passes** (the checked-in fixture and scenarios of §13).
   - Scenario A0 (card = every library video, empty library): clustering at R = 50 mi, G = 1 gives 7 groups.
   - The Council/Anvil group carries an emphasised day-split suggestion (about 34 mi), and applying it gives exactly the user's 8 folders.
   - Scenario D at 50 mi proposes **Append · Medium** with the cross-day hint.
   - Scenarios A and B–E in §13 pass.
3. **The first real card works.**
   - `uas-sort-cli plan` (§4.5) is within 2 edits of the user's checked-in `tests/acceptance/first-card-expected.json`; one edit is one `PlanEdit`.
   - The offload ends with 0 failures and a verdict of Safe or SafeWithAssumptions.
   - A before/after card listing shows no change made by the app.
4. **The app is responsive.**
   - Each edit or slider step derives in under 50 ms for 500 items, on a background thread, with the UI never blocked. Measured by the M3 Release benchmark: a synthetic 500-item `PlanBase`, median of 20 derives.
   - Warm start to first frame is ≤ 1 s (0.37 s measured in the spike). Measured by `--selftest`, which writes the first-frame time to `selftest-result.json`; `deploy.ps1` runs it twice and checks the second (warm) run (M10).
   - Scanning and planning a 300-file card on a USB 3 reader takes ≤ 20 s (UNVERIFIED; recorded on the real card at M4).
5. **The verdict never overstates safety.** "Safe to format" appears only when:
   - no card file is Unaccounted or AssumedByRule;
   - the card identity is unchanged;
   - the card listing is unchanged since the scan.

   A table-driven test covers every outcome and newness combination, including multi-file units, `seen` records and a card swap.

---

## 2. Stack & solution layout

### 2.1 Choices (versions as of 2026-09-27, from the NuGet registry queried that day)

| Layer | Choice | Version | Status |
|---|---|---|---|
| SDK / runtime | .NET 11 | SDK `11.0.100-rc.1.26425.128` (go-live), then RC2 (~Oct 13; date UNVERIFIED), then `11.0.100` GA (Nov 10) | Built, tested and published in two spikes (`docs/research/06-modern-stack.md`, `docs/research/08-winui-spike.md`) |
| Language | C# 15, the default for `net11.0` | Unions, `closed` hierarchies, collection-expression arguments; C# 14 `field` keyword and extension members | Compiled in the spike. **Exhaustiveness across assemblies is UNVERIFIED** and gets an M0 test |
| UI | WinUI 3 from the Windows App SDK 2.5.1 component packages | `Microsoft.WindowsAppSDK.WinUI` 2.3.9, `.Foundation` 2.3.12, `.InteractiveExperiences` 2.1.9 (pinned to avoid NU1603) | Spike: built, ran and published |
| Build tools | `Microsoft.Windows.SDK.BuildTools` | 10.0.28000.2705 | No Visual Studio needed |
| Web host | `Microsoft.Web.WebView2` | 1.0.4191.47 (Evergreen runtime 153.x already present) | Spike: virtual host, messaging and clicks worked |
| Map | MapLibre GL JS, vendored in the repo | 6.11.2. Streets from OpenFreeMap; satellite from Esri World Imagery; USGS as an alternate URL in settings | Verified in Chromium. **ESM `.mjs` under the WebView2 virtual host is UNVERIFIED** (M8's first task; §9.6). Leaflet 1.9.4 is proven in WebView2 and is the last-resort fallback |
| First-party controls | TitleBar, SelectorBar, **ItemsView** (+ LinedFlowLayout), AutoSuggestBox, DropDownButton + MenuFlyout, InfoBar, Slider, ContentDialog (queued), Expander, ToolTip | Part of WinUI 2.3.9 | TitleBar, ItemsView and Mica verified |
| Toolkit controls | `CommunityToolkit.WinUI.Controls.Sizers` and `.SettingsControls` | 8.3.260402-preview2 (the latest; the only line that works with the lean package set) | Preview. Sizers was never restored in a spike, so it is part of M0's exit build. Fallback: 8.2.251219 with the full `Microsoft.WindowsAppSDK` package, or fixed-ratio panes |
| Grid | **No DataGrid.** ItemsView with x:Bind templates | — | §9.3. WinUI.TableView 1.5.0 works on 2.5.1 but isn't used |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 (partial `[ObservableProperty]`) | Verified on C# 15 |
| Still-photo metadata | MetadataExtractor | 2.9.3, **Stream overloads only** (path overloads banned) | Trim behaviour UNVERIFIED, so rooted with `TrimmerRootAssembly`; M0 publishes a StillProbe call |
| Time zones | GeoTimeZone + built-in `TimeZoneInfo` (ICU) | 6.1.0 | Verified on Windows 11. Never set `InvariantGlobalization` or `UseNls` |
| Hash | System.IO.Hashing `XxHash128` (copies), `XxHash64` (keys) | **11.0.0-rc.1.26425.128**, then 11.0.0 at GA | 9.2 GB/s |
| JSON | System.Text.Json, source-generated contexts only | Built in | Safe under trimming |
| Clock | `TimeProvider` (built in); `Microsoft.Extensions.TimeProvider.Testing` in tests | 10.10.0 (no 11.x published) | — |
| Analyzers | `Microsoft.CodeAnalysis.BannedApiAnalyzers`; `AnalysisLevel` **pinned** to `11.0-recommended`, falling back to `10.0-recommended` if RC1 rejects it (UNVERIFIED); `TreatWarningsAsErrors` | 5.6.0 | — |
| Tests | `xunit.v3.mtp-v2` on Microsoft.Testing.Platform 2.4.1 | 4.0.1 | Passed 5/5 in the spike |
| Place names | GeoNames extract (CC-BY 4.0), built by `tools/places/build-places.cs` (a .NET file-based app) | Dump of 2026-09-27 | The Python spike (`docs/research/spikes/dotnet-stack/geonames/rg.py`; `docs/research/03-dotnet-libraries.md`) found "Zachar Bay" 0.4 mi and "Anvil Mountain" 0.2 mi from the clips |
| Scripts | PowerShell 7 (`pwsh`) | Latest from winget `Microsoft.PowerShell` (version UNVERIFIED). **Not installed on this PC** (only Windows PowerShell 5.1) | Approved: the user installs it once with winget at implementation start, next to the SDK |
| Packaging | Unpackaged, self-contained (.NET and Windows App SDK), lean, trimmed, ReadyToRun; a folder plus a Start-menu `.lnk`; `win-x64` and `win-arm64` | — | x64 is 87–97 MB with a warm first frame of about 0.37 s. ARM64 is UNVERIFIED. AOT is an optional M10 attempt |
| Dependency injection | None: a hand-written composition root | — | YAGNI |

### 2.2 Why these, and what they replace

**.NET 11 rather than .NET 10**
- RC1 is go-live, so production use is allowed.
- Short-term releases now get 24 months, so .NET 11 ends around November 2028 (exact date UNVERIFIED), about when .NET 10 LTS ends (2028-11-14).
- It brings C# 15, and the self-contained app needs no runtime on the PCs.
- Required upgrades, each scheduled as a 0.5-day item when it ships (§14):
  1. RC2, around Oct 13, because RC1's go-live support ends Oct 13.
  2. GA on Nov 10, which also sets `allowPrerelease:false`.
- Fallback: retarget to `net10.0` and replace `union` and `closed` with abstract records. This takes about half a day.

**WinUI 3**
- Neither spike found a showstopper; both built, ran and published from the command line.
- There is no DataGrid, but the review lists aren't spreadsheets. Microsoft's guidance for tabular data points to ListView/ItemsView. ItemsView is chosen because it is proven in the spike, takes a template selector, and supports Extended selection.
- WinUI.TableView adds a trim warning (IL2026) and nothing this app needs.

**Map: WebView2 + MapLibre**
- The Windows App SDK MapControl can't colour or label pins, can't draw circles or lines, and needs an Azure Maps key.
- Mapsui.WinUI is unverified on Windows App SDK 2.x.
- Leaflet dates from 2023, but it is proven inside WebView2, so it is the last-resort fallback.

**Packaging.** Unpackaged, as recorded in §1.1. Single-file publishing works only after a clean publish and unpacks 88 MB to temp, so it isn't used.

### 2.3 Solution layout (in `C:\dev\uas-sort`; not scaffolded yet)

```
uas-sort.slnx  global.json  Directory.Build.props  Directory.Packages.props  BannedSymbols.txt  .editorconfig
src/
  UasSort.Core/       net11.0                      model, media probes, time, geo, library index, planning, editing,
                                                   naming, offload engine, audit, ports, JSON contexts
  UasSort.Platform/   net11.0-windows10.0.26100.0  the ONLY code that touches disk, Win32 or the shell: lister, card reader,
                                                   GuardedFileOps/WindowsFileOps, CardSourceValidator helpers, sync-root
                                                   detection, stores, app assets, power request, eject, single-instance
                                                   and offload locks, shell launcher, logging
  UasSort.Review/     net11.0                      view models (MVVM Toolkit), map bridge, collection sync; no WinUI types
  UasSort.App/        net11.0-windows10.0.26100.0  Program.Main, WinUI shell, pages, MapPane (WebView2), MapAssets/,
                                                   places.bin.gz, SelfTest/ (m0-exif.jpg, selftest.dng, ledger-v1.jsonl), composition root
  UasSort.Cli/        net11.0-windows10.0.26100.0  developer harness, AssemblyName=uas-sort-cli: `plan` (dry run; writes nothing,
                                                   ever; contract in §4.5)
tests/
  UasSort.Testing/          net11.0  FakeFileSystem (tripwire, faults; asks Core's IoGuardPolicy), SyntheticMp4Builder,
                                     SyntheticDngBuilder, FakeCardWriter (materialises scenarios under %TEMP%\uas-sort-test-*
                                     only), Replay/library-listing.json (checked-in golden fixture, §13), GatedPlanDeriver
  acceptance/first-card-expected.json   the user's expected folder list for the first real card, written before M4 (§4.5)
  UasSort.Core.Tests/       net11.0
  UasSort.Review.Tests/     net11.0  includes cross-assembly exhaustive-switch tests on Core's closed/union types
  UasSort.Platform.Tests/   net11.0-windows…  Windows integration in %TEMP%, XAML lint, BannedSymbols content test
  UasSort.BannedApi.Probe/  net11.0-windows…  NOT in the solution build: one call per banned kind; built by tools/build.ps1
tools/
  build.ps1  r.sh (WSL wrapper)  deploy.ps1  vendor-maplibre.ps1  places/build-places.cs
  fixtures/make-replay-fixture.ps1  fixtures/make-selftest-assets.cs (writes App/SelfTest/* from the synthetic builders)
```

### 2.4 Dependency direction and banned APIs

```
App ──► Review ──► Core ◄── Platform ◄── App
Cli ──► Platform, Core          (Cli never references Review or App)
```

- Core has no project references. Its packages are MetadataExtractor, GeoTimeZone and System.IO.Hashing.
- **ICardReader and IFileOps (plus Platform's own stores) are the only ways any code may open a file, and each of them asks `IoGuardPolicy` (§4.3) before every open, create, attribute change, delete or rename.**

**Banned in Core, Review, App and Cli** (`BannedSymbols.txt`, whole types where possible):

| Group | Entries |
|---|---|
| File-system types | `T:System.IO.File`, `T:System.IO.Directory`, `T:System.IO.FileInfo`, `T:System.IO.DirectoryInfo`, `T:System.IO.FileSystemInfo`, `T:System.IO.RandomAccess`, `T:System.IO.FileStream`, `T:System.IO.DriveInfo`, `T:System.IO.FileSystemWatcher`, `T:System.IO.Enumeration.FileSystemEnumerable`1`, `T:System.IO.Enumeration.FileSystemEnumerator`1`, `T:System.IO.Compression.ZipFile` |
| Path-taking members | `M:System.IO.Path.GetTempFileName`; the `StreamReader(String…)` and `StreamWriter(String…)` constructors; `M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(…)` overloads; every `ReadMetadata(System.String)` overload of the MetadataExtractor readers; `XDocument.Load/Save(String)` and `XmlDocument.Load/Save(String)` |
| WinRT storage | `T:Windows.Storage.StorageFile`, `T:Windows.Storage.StorageFolder`, `T:Windows.Storage.FileIO`, `T:Windows.Storage.PathIO`, `T:Microsoft.VisualBasic.FileIO.FileSystem` |
| Images from paths | `M:Microsoft.UI.Xaml.Media.Imaging.BitmapImage.#ctor(System.Uri)`, `P:Microsoft.UI.Xaml.Media.Imaging.BitmapImage.UriSource` |
| Processes | every `M:System.Diagnostics.Process.Start(…)` overload (the `IShellLauncher` implementation lives in Platform) |
| Clock | `P:System.DateTime.Now`, `P:System.DateTime.Today`, `P:System.DateTime.UtcNow`, `P:System.DateTimeOffset.Now`, `P:System.DateTimeOffset.UtcNow` (use `TimeProvider`) |

**Platform gets a member-level list** (writes, deletes, moves, `FileMode.Create/Truncate/OpenOrCreate/Append`, `File.SetAttributes/SetLastWriteTime*/SetCreationTime*`, the clock). Every allowed risky call there carries a local `#pragma warning disable RS0030 // IO layer: <why>`, so each one is deliberate and easy to grep.

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
- Core and Review: `IsTrimmable=true`, `IsAotCompatible=true`.
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
- `OutputType=WinExe`, `UseWinUI=true`, `WinUISDKReferences=false`, `Platforms=x64;ARM64`, `RuntimeIdentifiers=win-x64;win-arm64`.
- `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, `SelfContained=true`.
- **`EnableMsixTooling=true`**. It is still required when unpackaged, or the `.pri`/`.xbf` files are missing and startup crashes with 0xC000027B.
- `TargetPlatformMinVersion=10.0.26100.0`.
- `DefineConstants` gets `DISABLE_XAML_GENERATED_MAIN` (custom `Program.Main`, §4.4).
- Release builds: `PublishTrimmed=true`, `PublishReadyToRun=true`, `<TrimmerRootAssembly Include="MetadataExtractor;XmpCore"/>`.
  - If M0's publish shows IL2104-class warnings **only** from those rooted assemblies, add a targeted `<NoWarn>$(NoWarn);IL2104</NoWarn>` with a comment. Any other trim warning stays an error.
- Content with `CopyToOutputDirectory=PreserveNewest`: `MapAssets\**` and `places.bin.gz`. `SelfTest\*` are embedded resources.

### 2.6 Commands

```powershell
# One-time, installed by the user at implementation start (approved 2026-09-27; machine-wide):
#   winget install Microsoft.DotNet.SDK.Preview      # 11.0.100-rc.1
#   winget install Microsoft.PowerShell              # pwsh 7, used by every script
dotnet build uas-sort.slnx                                  # first restore ~8-13 min (~1.5-2 GB NuGet)
dotnet test --solution uas-sort.slnx                        # MTP runner via global.json
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -CheckBannedApi
dotnet run --project src/UasSort.Cli -- plan --card E:\ --json     # = uas-sort-cli plan … (§4.5)
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1 -Version 0.1.0   # publish for this machine's RID, selftest gate, copy, .lnk, keep 2 versions
dotnet build-server shutdown                                # after WSL-driven builds (compiler server locks files)
```

From WSL, `tools/r.sh` wraps `"/mnt/c/Program Files/dotnet/dotnet.exe"` and `pwsh.exe`, with the working directory set to `/mnt/c/dev/uas-sort`. Incremental builds take about 15 s. Pass environment variables through `WSLENV`.

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
                                bool IsBrowsedFolder, bool IsWriteProtected)
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
                             string? Protocol, DateTime? SessionUtc, string? DroneSerial, ByteRange? Thumb);
public readonly record struct SessionKey(string? DroneSerial, DateTime SessionUtc)   // SessionUtc = mvhd − uptime µs (§6.3 step 6)
{ public bool SameSession(SessionKey o) => DroneSerial == o.DroneSerial
                                           && Math.Abs((SessionUtc - o.SessionUtc).TotalSeconds) <= 2; }  // never compare with ==
public sealed record StillInfo(DateTime? DtoNaive, TimeSpan? OffsetTime, GpsProbe Gps, string? Model, ByteRange? Thumb);
public sealed record RawItem(MediaUnit Unit, ItemKind Kind, string Name, long Bytes, DateTime CardMtimeUtc,
                             DateTime? DroneStamp /* filename or EXIF DTO, naive */, Mp4Info? Mp4, StillInfo? Still, string? ProbeError);

// ── clock
public enum ClockMode { Zone, NearestSample, Setting }
public sealed record ClockSample(DateTime DroneStamp, DateTime MvhdUtc, TimeSpan Offset);
public sealed record ClockModel(ClockMode Mode, string? ZoneId, ImmutableArray<ClockSample> Samples, TimeSpan? Modal, string SettingZoneId)
{ public (DateTime Utc, TimeSource Src)? ToUtc(DateTime droneStamp) => /* §6.1 */ default; }
public sealed record ClockSummary(ClockMode Mode, string? ZoneId, int SampleCount, string Headline);

// ── normalized item
public enum TimeSource { Mvhd, ExifWithOffset, DroneClockZone, DroneClockSample, DroneClockSetting, Mtime }
public enum TzSource { Gps, SameSession, NearestGpsWithin12h, NearestLandGpsOnCard, GeoNamesTz, PcZone }
[Flags] public enum ItemFlags { None = 0, NoGps = 1, Truncated = 2, ClockNotSet = 4, CheckDate = 8, TzFallback = 16,
                               ProbeFailed = 32, GpsGuessed = 64, ClockFromSetting = 128 }
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
  FolderExistsAppending, NewBeforeWallFolder, CheckDate, ClockNotSet, RootMissing, RootsUnconfirmed,
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
    ImmutableArray<LedgerParseIssue> ParseIssues,             // file, line, reason; a torn final line, or a line named by a later
                                                              // `torn` record of the same machine (§11), is not an issue
    ImmutableArray<string> SourceFiles, LedgerFolderStatus Status);
public sealed record Draft(int V, string CardKey, string InventoryHash, DateTime SavedUtc, Tuning Tuning, ImmutableArray<PlanEdit> Edits);
public sealed record Settings(int Schema, string VideoRoot, string PhotoRoot, ImmutableArray<string> PreviousPhotoRoots,
                              double RadiusMiles, int GapDays, string DroneClockZone, bool CopyJpgTwin,
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
public sealed record LedgerParseIssue(string File, int Line, string Reason);
public enum LedgerFolderState { Ok, Empty, Missing, NotPinned, Unwritable, CloudOnly, VideoRootMissing }
public sealed record LedgerFolderStatus(string Folder, LedgerFolderState State /* the most severe that applies: VideoRootMissing >
    CloudOnly > Unwritable > NotPinned > Missing > Empty > Ok */, bool Exists, bool InSyncRoot, bool Pinned, bool Writable,
    ImmutableArray<string> LedgerFiles, ImmutableArray<string> CloudOnlyFiles, ImmutableArray<string> OtherMachineFiles);

// ── ledger records (serialised; one JSON line each; fields mirror §11 one for one, camelCase)
[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")] /* + [JsonDerivedType] per case: file, folder, seen, decision, revoke, run, torn */
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
  - Everything serialised (`PlanEdit`, `TargetChoice`, `LedgerRecord`, map messages) is a `closed` record with `[JsonPolymorphic]` in a source-generated context. Reports serialise flattened `ReportLine`s, not `CopyOutcome`.
  - M0 proves that closed records round-trip, both in unit tests and in the trimmed `--selftest`.

---

## 4. Components

### 4.1 Ports (in Core; implemented in Platform or in fakes)

```csharp
public interface IVolumeProvider  { IReadOnlyList<VolumeInfo> GetVolumes(); }
public sealed record VolumeInfo(string Root, CardIdentity Identity, string DriveType, bool IsReady, bool IsReadOnlyVolume /* FILE_READ_ONLY_VOLUME */,
                                bool IsNtfs, bool IsRemovableBus, long FreeBytes);
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
  CardSourceCheck Validate(string chosenPath, Settings s, IDirectoryLister lister, IPathFacts facts, string appDataDir); }
public interface ICardReader {                                   // bound to one CardSource + CardIdentity; FileAccess.Read, FileShare.ReadWrite
  CardIdentity CurrentIdentity();                                 // GetVolumeInformationW on the card root (cheap)
  Stream OpenRandom(string cardRelPath);                          // probes, thumbnails (4 KB block cache on top)
  Stream OpenSequential(string cardRelPath);                      // copy
  FsEntry Stat(string cardRelPath);
  ListingResult Relist(); }                                       // audit-time re-listing
public interface IFileOps {                                      // destination writes only; guarded
  Stream CreateTemp(string finalPath, long size, out string tempPath);   // CreateNew + preallocate, Hidden|NotContentIndexed, "*.uas-sort.tmp"
  void FlushToDisk(Stream s);
  VerifyResult VerifyHash(string tempPath, long size, UInt128 expected, CancellationToken ct); // File.OpenHandle(NO_BUFFERING) → buffered fallback
  void FinalizeAttributes(string tempPath, DateTime creationUtc, DateTime mtimeUtc);          // copy times, clear Hidden
  RenameResult RenameNoReplace(string tempPath, string finalPath);                              // MoveFileExW, never REPLACE_EXISTING
  bool ConfirmFinal(string finalPath, long size);                                               // metadata only
  void FlushDestination(string dir, IReadOnlyList<string> filesCreatedThisRun);                 // non-NTFS / removable volumes (§10.3)
  void DeleteOwnTemp(string tempPath);
  void EnsureDirectory(string dir, bool allowCreate);             // allowCreate only for NewFolder paths and their YYYY/YYYY-MM parents;
                                                                  // never .uas-sort (only ILedgerStore.EnsureFolder creates that)
  bool TryGetSize(string path, out long size);
  long FreeBytes(string anyPathOnVolume); }
public interface ILedgerStore {                                  // folder = LedgerPaths.For(videoRoot) = <videoRoot>\.uas-sort (derived, never configured)
  LedgerFolderStatus Check();                                     // attributes and security descriptors only, BEFORE any open (§11)
  LedgerSnapshot Load();                                          // union of every <videoRoot>\.uas-sort\ledger*.jsonl (top level only, incl. OneDrive
                                                                  // conflict copies), deduped by record id; honours `torn` records
  void EnsureFolder();                                            // creates <videoRoot>\.uas-sort if missing (the folder only), then KeepOnDevice(),
                                                                  // also when the folder already existed unpinned. Called by Start offload and CopyInto
  ILedgerWriter OpenOwn();                                        // <videoRoot>\.uas-sort\ledger-<MACHINE>.jsonl, created if missing, append,
                                                                  // FileShare.Read (single writer). If the file doesn't end in "\n", it first appends
                                                                  // "\n" and a TornRecord naming the torn line N (§11). Mirrored to
                                                                  // LedgerPaths.BackupDir(...) (local, never under the library)
  void SnapshotToBackup(string runId);                            // copies every loaded ledger*.jsonl into BackupDir\snapshots\<yyyyMMdd-HHmmss>-<run8>\
                                                                  // (keeps 20); called by Start offload before the first copy
  void KeepOnDevice();                                            // FILE_ATTRIBUTE_PINNED on the .uas-sort folder only (§4.3)
  void CopyInto(string newVideoRoot, LedgerSnapshot current); }   // video-root change [Copy]: EnsureFolder() there, then append every current
                                                                  // record, original id and machine kept, to the own ledger file under
                                                                  // <newVideoRoot>\.uas-sort (§9.14). Settings already hold the new root
public interface ISettingsStore { SettingsLoad Load(bool readOnly = false); void Save(Settings s); }
                                                                  // Recovered=true if defaults were used; readOnly (the CLI) never renames,
                                                                  // creates or writes anything, and just returns derived defaults on a bad file
public interface IDraftStore { Draft? Load(string cardKey); void Save(string cardKey, Draft d); void Delete(string cardKey); }
public interface IReportStore { string Save(OffloadReport r); }
public interface IAppAssets { Stream OpenPlaces(); Stream OpenSelfTest(string name); }   // app folder / embedded only
public interface IPowerRequest { IDisposable KeepSystemAwake(string reason); }   // PowerCreateRequest/PowerSetRequest
public interface IOffloadLock  { IDisposable? TryAcquire(); }                    // named mutex Local\uas-sort-offload
public interface IDeviceEject  { EjectResult Eject(string volumeRoot); }        // CM_Request_Device_Eject; no admin (UNVERIFIED per drive)
public interface IShellLauncher { void OpenFolder(string path); void OpenFile(string path); void OpenHttps(Uri uri); }
public interface ITimeZoneResolver { TzLookup Resolve(GeoPoint p); }           // (IanaId, Alternatives, IsEtc)
public interface IPlaceIndex { IReadOnlyList<PlaceHit> Near(GeoPoint p, Distance r, PlaceClass cls, int max); }
public interface IThumbnailSource { ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct); }  // bytes, not images
// Clock: System.TimeProvider everywhere.
```

### 4.2 Units

| Component | Where | What it does | Key members |
|---|---|---|---|
| **CardDetector** | Core | Checks ready volumes and flags DJI cards (§5). It skips drives that hold a configured root; those are reachable only through Browse, which is validated | `IReadOnlyList<CardCandidate> Detect(IReadOnlyList<VolumeInfo>, IDirectoryLister, Settings)` |
| **CardSourceValidator** | Core (policy) + Platform (`IPathFacts`: canonical paths, sync roots) | Anchors a browsed folder at the nearest ancestor holding `DCIM`. Rejects any root that equals, is inside, or contains a library root, a previous photo root, the ledger folder (`<videoRoot>\.uas-sort`, which the video-root check covers too), app data, or any cloud sync root (§4.3) | `CardSourceCheck Validate(string chosenPath, Settings s, IDirectoryLister lister, IPathFacts facts, string appDataDir)` → `SourceOk(CardSource)` / `SourceRefused(reason)` |
| **CardClassifier** | Core | Classifies every entry with fail-safe rules (§5); builds pairs and sets, flags `.trinf` files, turns enumeration errors into `ForcesNotSafe` warnings, and computes the inventory hash: `InventoryHash` = XxHash64 (16 hex digits) over the UTF-8 lines `relPath\|size\|mtimeTicks` plus a line feed of every file entry, sorted ordinally by lowercase `relPath` | `CardInventory Classify(CardSource, ListingResult)` |
| **Mp4Probe** | Core.Media | Port of `docs/research/spikes/djmd/djmd_gps.py`, about 350 lines (§6.3). Never reads `mdat` or the whole `moov` | `static Mp4Info Read(Stream s)` |
| **StillProbe** | Core.Media | DTO from the EXIF directory that actually has it, offset, GPS, model and IFD0 thumbnail range, via MetadataExtractor **Stream** overloads | `static StillInfo Read(Stream s)` |
| **MetadataHarvester** | Core.Media | Reads **sequentially**: videos, photos, then the first frame of each set. Reports progress. A probe failure becomes `ProbeError`, not an exception | `IAsyncEnumerable<RawItem> HarvestAsync(CardInventory, ICardReader, IProgress<ScanProgress>, CancellationToken)` |
| **ThumbnailReader** | Core.Media | Reads a stored byte range from the card: MP4 `tnal` 160×90 or DNG IFD0 160×120 | `: IThumbnailSource` |
| **DroneClock** | Core.Time | Learns the drone clock **as a zone** (§6.1) | `static ClockModel Learn(IEnumerable<RawItem>, string settingZoneId)` |
| **TimeResolver** | Core.Time | Works out capture time, zone, local date, session and flags (§6.2–6.5). Zone fallbacks don't depend on R or G | `ImmutableArray<ResolvedItem> Resolve(IReadOnlyList<RawItem> raw, ClockModel clock, ITimeZoneResolver tz, IPlaceIndex? places, TimeZoneInfo pc, DateTime nowUtc)` |
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
| **CardAudit** | Core.Offload | §10.5: re-lists and diffs the card, audits per file, and takes the worst category per unit | `FormatVerdict Audit(CardInventory, ListingResult relisted, CardIdentity now, Plan, OffloadResult?, LedgerSnapshot)` |
| **Platform** | Platform | Windows implementations of the ports: <br>• `WindowsVolumeProvider`; <br>• `WindowsDirectoryLister`: a `FileSystemEnumerator<FsEntry>` subclass with `ContinueOnError` recording errors; <br>• `WindowsCardReader`; <br>• `GuardedFileOps` wrapping `WindowsFileOps`: `LibraryImport` for `MoveFileExW`, `FlushFileBuffers`, `GetFinalPathNameByHandleW`, `CfGetSyncRootInfoByPath`, `CM_Request_Device_Eject`, `RtlSetProcessPlaceholderCompatibilityMode`, `AllowSetForegroundWindow`; `\\?\` prefix on every P/Invoke path; `File.OpenHandle` for unbuffered verify; <br>• `PlaceholderGuard`, `SyncRootDetector`, `KnownFolders`; <br>• JSON stores (settings, `LedgerStore` for `<videoRoot>\.uas-sort\` with its local backup, drafts, reports), `AppAssets`, `PowerRequest`, `OffloadLock`, `SingleInstance`, `ShellLauncher`, `FileLog`; <br>• `WindowsCardReaderFactory : ICardReaderFactory`, `PathFacts : IPathFacts`. <br>Every open, create, attribute change, delete and rename in `GuardedFileOps`, `WindowsCardReader` and `LedgerStore` first calls `IoGuardPolicy.Check` | — |
| **Review VMs** | Review | <br>• Stages: `ShellVm` (stage machine), `SetupVm`, `CardStageVm`, `ScanStageVm`. <br>• Review: `ReviewVm` (`VideosTabVm`, `PhotosTabVm`, `OtherTabVm`), `GroupCardVm` (carries its preceding boundary chip and any folded run), `FoldedRunVm`, `ClipRowVm` (carries an optional day-split banner), `SuggestionVm` (`ToString()` = text), `PhotoDayVm`, `TuningVm`, `IssuesVm`. <br>• Map and commit: `MapBridge`, `PreflightVm`, `CopyVm`, `VerdictVm`, `SettingsPageVm`. <br>• Plumbing: `CollectionSync` (keyed diff that keeps selection and scroll) | Services: `IUiDispatcher`, `IDialogService` (queued), `IShellLauncher`, `IThumbnailSource` |
| **App** | App | `Program.Main` (single instance), `MainWindow` (TitleBar, Mica, Frame), Pages (Setup, Card, Scan, Review, Preflight, Copy, Verdict, Settings), `MapPane` (WebView2), `Thumb.Key` attached property + `ThumbnailCache` (LRU of 400 decoded at 96 px), `DeviceChangeWatcher`, `SelfTest`, `CompositionRoot` | — |
| **Cli** | Cli | `uas-sort-cli plan` is a dry run that writes nothing: no drafts, no ledger, no files; logs go to stderr. It uses the same `CardSourceValidator`. Contract in §4.5 | `static int Main(string[] args)` |

### 4.3 IO guard (`IoGuardPolicy`, enforced by `GuardedFileOps`, `WindowsCardReader`, `LedgerStore`, `PlaceholderGuard`, `CardSourceValidator`)

**The policy is one pure Core function.** Every rule in this section is implemented once, in `IoGuardPolicy.Check`, and never re-implemented. `GuardedFileOps`, `WindowsCardReader` and the Platform `LedgerStore` call it before every open, create, attribute change, delete or rename, and throw `UnsafeIoException` (or report `CloudOnly`) on anything but `GuardAllow`. `FakeFileSystem` (Testing, `net11.0`) calls the same function and throws `HydrationViolation` for `GuardHydration`. Listings and attribute reads are not opens and are not checked.

```csharp
public enum IoOp { ReadData, AppendOwnLedger, CreateNew, CreateDir, SetPinned, SetAttributesOrTimes, Delete, Rename, OpenForFlush }
public sealed record GuardContext(string VideoRoot, string PhotoRoot, ImmutableArray<string> PreviousPhotoRoots, string? CardRoot,
    string AppDataDir /* %LOCALAPPDATA%\uas-sort */, string Machine, IReadOnlySet<string> NewFolderDirs /* incl. YYYY, YYYY-MM parents */,
    IReadOnlySet<string> OwnTempsThisRun, IReadOnlySet<string> RenamedThisRun);   // all canonical, compared case-insensitively
public sealed record GuardAllow;   public sealed record GuardUnsafe(string Reason);
public sealed record GuardCloudOnly(string Path);   public sealed record GuardHydration(string Path, uint Attributes);
public union GuardDecision(GuardAllow, GuardUnsafe, GuardCloudOnly, GuardHydration);
```

`Check(op, canonicalPath, attributes, ctx)` evaluates, in order:
1. Callers read the target's attributes first; a read that fails for any reason other than "not found" is refused before the policy runs (PlaceholderGuard). `attributes` is null only for a target that doesn't exist, which only `CreateNew`, `CreateDir` and `AppendOwnLedger` accept (null for any other op → `GuardUnsafe`). Any of `0x400000`, `0x40000`, `0x1000` set → `GuardCloudOnly` if the path is a top-level `.uas-sort\ledger*.jsonl`, else `GuardHydration`.
2. Under `CardRoot`: `ReadData` → allow; anything else → unsafe.
3. Under `LedgerPaths.For(VideoRoot)`: the four exemption operations below → allow; anything else → unsafe.
4. Under the video root, photo root or a previous photo root: `CreateNew` of a `*.uas-sort.tmp`; `ReadData`, `SetAttributesOrTimes`, `Rename` (to its final name in the same directory) and `Delete` of a path in `OwnTempsThisRun`; `Delete` of any other `*.uas-sort.tmp` (stale temps, at Start offload); `OpenForFlush` of a path in `RenamedThisRun` or a directory in `NewFolderDirs` or holding a renamed file; `CreateDir` of a path in `NewFolderDirs` → allow; anything else → unsafe.
5. Under `AppDataDir`: allow (Platform's own stores).
6. Anything else → unsafe.

**Card**
- Reads only, and only for paths under the anchored card root.
- Handles request `FileAccess.Read` (GENERIC_READ); never `FILE_WRITE_ATTRIBUTES` or any write right. A Platform test runs the reader against files whose ACL denies all write rights.
- Nothing under the card root is ever created, written, renamed or deleted.
- The reader is bound to the `CardIdentity` taken at scan time. `CurrentIdentity()` is re-checked at Commit start, before each file, and before the verdict.
- A write-protected volume (`FILE_READ_ONLY_VOLUME`) gets a "write-protected" badge.

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
   - Message: "This is part of your library (or a synced folder); uas-sort only reads cards."

**Library roots**
- Never opened for reading, with these exceptions:
  - our own `*.uas-sort.tmp` files during verification, and our own just-renamed files during the post-run flush. These are tracked in an in-memory set for the run.
  - **The ledger exemption** (below). It is the only exemption for files the app didn't create in this run.
- Files are created only with `CreateNew`, and only under a configured root. The own ledger file is the one exception: it is opened for append and created if it is missing (§4.1).
- `IFileOps.EnsureDirectory(allowCreate:true)` is allowed only for NewFolder paths and their `YYYY`/`YYYY-MM` parents. Append targets must already exist (§10.2).
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
  - **Create the `.uas-sort` folder itself** when it is missing, through `ILedgerStore.EnsureFolder()`: on **Start offload** (§4.4), or on the video-root [Copy] (§9.14).
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
- Tests therefore prove the shipped rules in three layers (§13): the table-driven policy test (Core.Tests), the end-to-end tripwire (fake FS), and Platform tests showing that `GuardedFileOps`, `WindowsCardReader` and `LedgerStore` consult the policy.

### 4.4 Flow

1. **Launch (`Program.Main`, `DISABLE_XAML_GENERATED_MAIN`).**
   1. `WinRT.ComWrappersSupport.InitializeComWrappers()`, then the placeholder compatibility call.
   2. Unless `--selftest`:
      - `AppInstance.FindOrRegisterForKey("uas-sort")`.
      - If another instance owns the key: `AllowSetForegroundWindow(existing.ProcessId)`, then `RedirectActivationToAsync(args)` **on a worker thread**, while Main waits on an event. Then exit.
      - In the main instance, `Activated` brings the window forward with `SetForegroundWindow(hwnd)` through the dispatcher.
      - Fallback if AppInstance misbehaves in the lean self-contained build (UNVERIFIED): the named mutex `Local\uas-sort` (tested in M0).
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

### 4.5 CLI contract (`UasSort.Cli`, `AssemblyName=uas-sort-cli`; M3's deliverable, M4's gate)

```
uas-sort-cli plan --card <path> [--video-root <path>] [--photo-root <path>] [--radius-mi <5..100>] [--gap-days <0..7>]
                  [--settings <path>] [--json] [--expect <expected.json>]
```

- **Roots and tuning.** Command-line values win. Otherwise they come from `ISettingsStore.Load(readOnly: true)` of `%LOCALAPPDATA%\uas-sort\settings.json` (or `--settings`). A missing or unreadable file gives the derived defaults (video root `KnownFolder(Pictures)\UAS Videos`, photo root `<videoRoot>\Picture Offload`, R 50 mi, G 1, `America/New_York`, `copyJpgTwin` on) and **nothing is renamed, created or written**; that is how the CLI runs before the Setup UI exists (M3–M6a). `previousPhotoRoots` come only from settings.
- **What it runs.** `CardSourceValidator` → `ScanService.ScanAsync` (the ledger is read through `Check()` + `Load()`; never `OpenOwn`, `EnsureFolder` or `SnapshotToBackup`) → `Planner.Prepare` → `Derive` with no edits. The guard is the same as the app's.
- **Output.** Logs and progress go to stderr; the plan goes to stdout. Without `--json`: one block per group, `NEW FOLDER 2026\2026-09\2026-09-27 · 13 clips · Sep 27 · issues: EmptyFolderName`, then photo days, sets, other files and issues. With `--json`, this schema (`v:1`, camelCase, enums as names):

```json
{ "v": 1,
  "card": { "root": "E:\\", "identity": { "serial": "1A2B3C4D", "label": null, "fs": "exFAT", "totalBytes": 256060514304 },
            "model": "FC9113", "files": 214, "inventoryHash": "9f3c0a6d12e4b7a1" },
  "settings": { "videoRoot": "C:\\…\\UAS Videos", "photoRoot": "C:\\…\\Picture Offload", "radiusMiles": 50, "gapDays": 1 },
  "clock": { "mode": "Zone", "zone": "America/New_York", "samples": 13 },
  "watermarkUtc": "2026-09-27T18:24:16Z",
  "groups": [ { "id": "DCIM/DJI_001/DJI_20260725232655_0117_D.MP4", "target": "Append",
                "relPath": "2026\\2026-07\\2026-07-25 Council Road", "confidence": "Medium", "why": "different day, 34 mi from Council Road",
                "start": "2026-07-25", "end": "2026-07-26",
                "boundaryBefore": { "cause": "DayGap", "jumpMiles": null, "gapHours": 1488.5, "dayGap": 62 },
                "videos": [ { "id": "DCIM/DJI_001/DJI_20260725232655_0117_D.MP4", "status": "Imported", "included": false,
                              "captureUtc": "2026-07-26T03:26:55Z", "localDate": "2026-07-25", "timeSource": "Mvhd", "flags": [] } ],
                "daySplits": [ { "firstOfDay": "DCIM/DJI_001/DJI_20260726235645_0001_D.MP4", "from": "2026-07-25", "to": "2026-07-26",
                                 "apartMiles": 33.7, "emphasised": true } ],
                "issues": [ "MediumAppend", "EmphasisedDaySplit" ] } ],
  "photoDays": [ { "date": "2026-07-25", "tz": "America/Anchorage", "units": 12, "new": 8, "probablyImported": 4, "reason": "videos from this day are already in the library" } ],
  "sets": [ { "id": "DCIM/PANORAMA/001_0087", "folder": "001_0087 2026-09-27", "resolution": "DateSuffixed", "members": 33 } ],
  "other": [ { "relPath": "DCIM/DJI_A001/x.MP4", "class": "Unknown", "rule": null } ],
  "issues": [ { "severity": "Blocking", "code": "EmptyFolderName", "anchor": "DCIM/DJI_001/DJI_20260927140127_0123_D.MP4",
                "message": "Name this folder", "requiresAck": false } ] }
```

- **Exit codes.** 0 = plan printed (even with Blocking issues); 1 = source refused by the validator (reason on stderr); 2 = any other error (bad arguments, IO, unhandled exception).
- **Expected folder list.** Before M4 the user writes `tests/acceptance/first-card-expected.json`: `{ "folders": [ { "relPath": "2026\\2026-10\\2026-10-04 Nome Roads", "clips": ["DJI_…_0151_D.MP4", …] } ] }`. `--expect <file>` prints the differences and the **edit count**: for each expected folder whose clips span k > 1 CLI groups, k − 1 (merges); for each CLI group whose clips span k > 1 expected folders, k − 1 (splits or moves); plus 1 per matched folder whose description or target kind differs (a `Rename` or `Retarget`). One edit is one `PlanEdit`; M4 passes at ≤ 2.

---

## 5. Card detection & file classification

**Detection** (every ready volume, via `IVolumeProvider`)
- Skip Network, CDRom and NoRootDirectory drives, and drives that hold a configured root.
- **Don't rely on DriveType or the volume label.** D: reports as Fixed exFAT.
- **DJI card:**
  - `\DCIM\` contains a folder matching `^DJI_\d{3}(_.+)?$`, or `PANORAMA\`, or `HYPERLAPSE\`;
  - and at least one file matches `^DJI_(\d{14})_(\d{4})_([A-Z])(?:_[^.]*)?\.([A-Za-z0-9]+)$`.
- **Anything else is "not a card".** That includes RC 2 internal storage and Autel cards; they are listed only as "not a DJI card".
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
| `DCIM\<media>\*.JPG` with the same base name as an MP4 | **Unknown** ("possible video cover") | — | Stays Unknown until the first-card acceptance (M4) confirms these are covers. After that, a named Skip rule "video cover" is switched on |
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

---

## 6. Time & location normalization

### 6.1 Learning the drone clock (as a time zone)

**Samples**
- Every DJI MP4 that has a `moov` gives one sample: `(stamp, mvhdUtc, offset = round15min(stamp − mvhdUtc))`. So far every sample is −4 h, and all are from summer.

**Zone fit.** A candidate IANA zone *fits* when `zone.GetUtcOffset(stamp as zone-local) == offset` for every sample. Candidates, in order:
1. the stored `droneClockZone` (default `America/New_York`, from the user's statement);
2. `America/New_York`, `America/Chicago`, `America/Denver`, `America/Phoenix`, `America/Los_Angeles`, `America/Anchorage`, `Pacific/Honolulu`;
3. the PC zone.

The first zone that fits is chosen. Summer samples fit both New York and zones fixed at −4 (e.g. `America/Puerto_Rico`); the order resolves that.

**Modes**

| Mode | When | Conversion | Time source |
|---|---|---|---|
| `Zone` | A candidate fits every sample | Every stamp converts through the zone, so DST is handled | `DroneClockZone` |
| `NearestSample` | No candidate fits (e.g. the RC doesn't follow DST, or clock drift) | The nearest sample within 60 days, else the card's most common offset | `DroneClockSample`; the clock banner says why |
| `Setting` | No MP4 samples on the card | The stored zone | `DroneClockSetting`, flag `ClockFromSetting` |

- **The same `ClockModel` is used everywhere** a drone stamp becomes UTC: card items, library member start times, and the watermark (§7.1).
- **After a successful run,** a fitted zone is saved as `droneClockZone`.
- **Header text:** "Drone clock: US Eastern (America/New_York), learned from 13 videos. Folder dates use local time at each site." A "Why?" link explains the RC 2 time-zone setting.

### 6.2 Capture-time precedence (first rule that applies)

| # | Condition | CaptureUtc | Source |
|---|---|---|---|
| 1 | DJI video with `moov` | `mvhd` creation time, taken as UTC (`SpecifyKind(Utc)`) | `Mvhd` |
| 2 | EXIF DTO plus `OffsetTimeOriginal` (DJI doesn't write it today) | DTO − offset | `ExifWithOffset` |
| 3 | DJI photo, set, truncated clip or probe-failed item with a drone stamp (EXIF DTO, else the filename) | `ClockModel.ToUtc(stamp)` | `DroneClockZone` / `DroneClockSample` / `DroneClockSetting` |
| 4 | Anything else | Card mtime (whether exFAT mtime is true UTC is UNVERIFIED) | `Mtime` |

GPS-time sources for other DJI models are deferred (the Air 3S has no real GPS time).

### 6.3 GPS extraction

**MP4 (`Mp4Probe`)**
1. **Walk the top-level boxes lazily.**
   - Size 1 means a 64-bit size follows; size 0 means the box runs to end of file; `uuid` boxes have 16 extra header bytes.
   - Reject any size smaller than its header or larger than what remains of the file. Never read `mdat`.
2. **Inside `moov`:**
   - `mvhd` gives the creation time (seconds since 1904; 32-bit in version 0, 64-bit in version 1).
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
5. **Multi-sample search.** A fix with |lat| and |lon| both below 1e-6 means "no fix". Sample indexes are **0-based**; sample 0 is always read (it carries the protocol and the first fix). If it has no fix, probe samples 1–9, then 16, 32, 64, … (each below n), then the last sample, n − 1. The read count is at most 1 + 9 + |{2^k : k ≥ 4, 2^k < n}| + 1, e.g. 16 for n = 300.
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
4. **Local date:** `ConvertTimeFromUtc(CaptureUtc, tz).Date`.
5. **Worked examples:**
   - Clip `20260726035000` is 07:50Z, which is **Jul 25** 23:50 AKDT.
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
- Member start times: DJI filename stamp → UTC via the **card's `ClockModel`**. If mtime (true UTC, about start + duration) is earlier than that, or more than 2 h later, use mtime and set a flag. Non-DJI names (Autel `MAX_####`) use mtime.
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
| Imported | `(NormName, Size)` found in any listed root (evidence `LibraryNameSize`), or a ledger `file` record (`LedgerVerified`; or `LedgerNameSize` when the record says `verify:"nameSize"`), including files culled from the library since | Hidden, folded into "already in library" (§8.5 fold rule) |
| Decided | A ledger `decision` (only `dismissed` is possible for videos, and only set individually), not revoked | Listed under "Dismissed by you" on the Other tab with [Un-dismiss] |
| Conflict | Same `NormName` with a different size in any listed root or the ledger, and no same-size match | ☐ with the reason. If ticked, it copies as `name (2).ext` (§7.4) |
| New | Anything else | ☑; a Truncated clip is ☐ (§7.5) |

DJI names contain a timestamp, so they are unique. Autel `MAX_####` names repeat across cards, which is why size is part of the key.

### 7.3 Photos and sets (first rule that applies)

1. **Ledger `file` record** (for a set: every member), or a ledger **`decision`** (`assumedImported` or `dismissed`) that hasn't been revoked → **Imported** (evidence Ledger) or **Decided**.
2. **Name and size found in any listed root** (video root, photo root, or previous photo roots).
   - For a set: a set folder exists with every member matching name, size and **mtime ±2 s**. An empty folder doesn't count.
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

- **What counts as a conflict:** the same `NormName` exists with a different size, anywhere in the listed roots or the ledger, and no same-size match exists (video table §7.2; photo rule 2b §7.3). By default it is unticked, with the text "A different file named X exists: <path>, <size>".
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
  - a **Warning** issue that must be acknowledged at preflight;
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
| As above, but the dates are only adjacent (no overlap) and the distance is **< 10 mi** | Append(F) | High, "next day, {d}" |
| As above, dates only adjacent, distance **10 mi–R** | Append(F), with `CrossDayHint` "[New folder instead]" = `Retarget(NewFolderTarget)` | **Medium**, "different day, {d}" |
| One centroid unknown and the dates overlap | Append(F) | Medium, "same dates, location unknown" (badge) |
| Dates only adjacent and a location unknown, or more than R apart | NewFolder (F still offered in the dropdown) | – |
| **The group borders a `UserSplit` boundary, and F is the Wall of the group on the other side, or the pass-1 target of the earlier group across it** (pass 2, §8.9) | NewFolder (F still offered in the dropdown). "Split here" means "separate folder" | – |
| Otherwise | NewFolder | – |

**Split point** of both wall hints: the first New item, in `(CaptureUtc, Id)` order, whose local date is not one of F's days; if that item is the group's first item (the New run comes first), the first item Imported into F instead. It is never the group's first item, so the quick fix is never `Rejected(SplitAtGroupStart)`. Example: New 7/24 clips followed by Imported 7/25 clips in F dated 7/25 → NewFolder with [Split here] before the first 7/25 clip; after it, the 7/25 group is AlreadyImported(F) and the 7/24 group NewFolder.

**Further rules**
- **Tie-break:** the smallest date gap, then the smallest distance.
- **Never auto-append earlier clips to a later-dated folder** (F's name date after g.Start). A manual `Retarget` to such a folder needs the confirmation "Folder is dated Sep 28; these clips start Sep 27", and the edit stores that confirmation.
- **Two groups with the same target:** both get an Info note, "also targeted by <group>; both land in the same folder", with a [Merge] quick fix.
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
5. "near <town>", for the nearest town with population ≥ 1,000 within 30 mi.

- **Prefill:** a NewFolder group is prefilled with the top suggestion from rules 1–4, shown in italics as a suggestion. It is not blocking, and accepting it is a no-op. With no suggestion, the box stays blank, which is blocking.
- **Data** (`tools/places/build-places.cs` writes `src/UasSort.App/places.bin.gz`; `PlaceIndex.Load` reads it):
  - **Sources**, from the GeoNames dump of 2026-09-27: `US.zip` (every US record of the classes below) plus `cities5000.zip` as the worldwide fallback (populated places only). About 7.7 MB gzipped.
  - **Kept records:** class P (populated places; population from the dump) and these feature codes, mapping the words of rule 3: mountain `MT`, peak `PK`, hill `HLL`, valley `VAL`, pass `PASS`, cape `CAPE`, island `ISL`, peninsula `PEN`, point `PT`, bay `BAY`, lake `LK`, glacier `GLCR`, fjord `FJD`, cove `COVE`, lagoon `LGN`, inlet `INLT`, sound `SD`, strait `STRT`, harbor `HBR`, falls `FLLS`, park `PRK`.
  - **Layout** (little-endian, then gzip): header `"UPLC"`, `u16 version = 1`, `u32 recordCount`, `u16 tzCount`, then `tzCount` zone names (`u8 length` + UTF-8), then per record: `u8 nameLength` + UTF-8 name, `i32 lat × 1e6`, `i32 lon × 1e6`, `u8 class` (0 = populated, 1 = feature), `u8 featureCodeIndex` (into the list above, in that order; 255 for populated), `u32 population`, `u16 tzIndex`. `PlaceIndex` builds its 0.1° grid at load time.
  - Credit "GeoNames CC-BY 4.0" appears in About.
  - **Round-trip test (M9):** `build-places` → `PlaceIndex.Load` → the nearest feature to the Anvil Mountain clips is "Anvil Mountain" at ≤ 0.2 mi, and to the Zachar Bay clips "Zachar Bay" at ≤ 0.4 mi (the distances the Python spike found).

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

`Setup → Card → Scan → Review → Commit (Preflight sheet → Copy → Verdict)`

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

### 9.2 Window chrome

- **TitleBar** (Windows App SDK 2.1): app icon; "uas-sort"; a card chip ("E:\ · DJI Air 3S · serial 1A2B-3C4D · 214 files · 61.3 GB"); Rescan, Undo, Redo and Settings buttons. Settings navigates the Frame to the Settings page.
- **Backdrop:** Mica through `SystemBackdrop`. **Theme** follows the system; the map follows it with `setTheme`.
- **Size:** minimum 1100×700 through `OverlappedPresenter` preferred minimums, scaled by `RasterizationScale`.
- **InfoBars** sit under the title bar: clock banner, first-run banner, draft resume ("Resume edits from 14:02? 3 of 4 still apply [Resume] [Discard]"), ledger status (cloud-only / not pinned / parse issues / no history in a changed video root), settings recovery, and device notices.
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

- **Panes.** The two splitters are toolkit `GridSplitter`s (Sizers). Pane sizes are remembered in settings.
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
9. **Chips:** unfinished recordings, conflicts, check date, GPS guessed, empty description, cross-day append (Medium), emphasised day split, pin membership changed, conflicting pins.

### 9.5 Clip list

- **Control.** A virtualized **ItemsView** of `ClipRowVm` with `SelectionMode=Extended`.
  - A row that begins a new local day carries a `DaySplitBannerVm` drawn **above the row content, inside the same template**, with [Split here]. The banner is not a separate item.
- **Columns** (a shared column-width header grid):
  - include checkbox;
  - 96 px thumbnail;
  - name;
  - local time with a time-source icon (the tooltip shows UTC, the drone-clock time and the source);
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

**MapLibre is ESM-only, and modules load only when served with a JavaScript MIME type.** M8's first task checks that the virtual host serves `.mjs` as `text/javascript` (UNVERIFIED). If it doesn't, the fallbacks in order are:
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
| `EmphasisedDaySplit` | Warning | yes | Planner.Derive | first item of the new day | "{from} → {to} · {d} apart: likely separate outing" | [Split here] = `SplitBefore(FirstOfDay)` |
| `PinMembershipChanged` | Warning | yes | Planner.Derive | group | "{target or name} chosen for {n} clips; group now has {m}" | [Keep] = the same `Retarget`/`Rename` with current `PinnedMembers`; [Reset to Auto] = `Retarget(AutoTarget)` or `Rename(null)` |
| `ConflictingPins` | Blocking | – | Planner.Derive | group | "Two choices for this group: '{a}' vs '{b}'" | [Use first] / [Use second] = that pin re-issued with current `PinnedMembers` |
| `SharedTarget` | Info | – | Planner.Derive | group | "Also targeted by {group}; both land in the same folder" | [Merge] = `Merge(this.Anchor, other.Anchor)` |
| `FolderExistsAppending` | Info | – | Planner.Derive | group | "Folder exists; appending" | – |
| `NewBeforeWallFolder` | Info | – | Planner.Derive | group | "These clips start before '{F}' (dated {date})" | [Split here] = `SplitBefore(split point)` (§8.5) |
| `CheckDate` | Warning | – | Planner.Derive | item | "Check date: {local time} is within {60 or 75} min of midnight ({source})" | – |
| `ClockNotSet` | Warning | – | Planner.Derive | item | "Clock not set: {time} ({source})" | – |
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
| Space | Clip list, photo wall | Include/exclude, marked Handled so the container doesn't also toggle selection. If M6 shows the container still toggles, Space is left to the row's focused CheckBox |
| Ctrl+Shift+S | Clip list | Split before the focused clip |
| Esc | Flyouts | Native close |

A smoke test types a space into the rename box and checks that `Included` is unchanged.

### 9.13 Units and formatting

| What | Format |
|---|---|
| Distances | Always miles: under 0.1 → "<0.1 mi"; under 10 → one decimal ("7.8 mi"); otherwise whole numbers ("34 mi") |
| Sizes | Decimal units ("31.4 GB", "7 MB") |
| Times | Site local, with the zone abbreviation. A time estimated from the drone clock is prefixed "~" |
| Dates | "Jul 25–26" |

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
- **Drone clock zone:** an IANA zone picker (default `America/New_York`) with the learned status.
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
- **`CreatesFolder`** is true only for NewFolder targets.
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
- For every destination volume that is not NTFS on a fixed disk (e.g. exFAT D:), `FlushDestination` runs FlushFileBuffers on each renamed file (re-opened with write access, never read; tracked as created this run) and on each destination directory handle (`FILE_FLAG_BACKUP_SEMANTICS`). Both are UNVERIFIED on exFAT; M10 acceptance tests them on a scratch folder on D:.
- Those volumes are listed in `VolumesNeedingSafeRemoval`.

**Invariants**
- A file gets its final name only after it has been verified, so a crash can leave only `*.uas-sort.tmp` files, which the next preflight lists and Start offload deletes.
- **Keep awake:** `IPowerRequest.KeepSystemAwake("Offloading drone media")` for the whole run. **Offload lock** held for the whole Commit.
- **No thumbnails during a copy:** thumbnail reads from the card are paused, so placeholders show.

### 10.4 Ledger writes during Commit

Every record carries `id` (a GUID, used to deduplicate across files and OneDrive conflict copies), `machine`, `v:1` and `run`.

- One `file` record per Verified file (`verify:"unbuffered"` or `"cached"`) or AlreadyThere file (`verify:"nameSize"`).
- After each group, a `folder` record: created or appended, description, centroid, local date range, zone.
- `folder` records with `source:"cardLeftovers"` for existing folders whose location was learned from leftover clips on the card. This is how a location survives formatting the card.
- **At the end of every Commit, including Cancel, failures and stops,** one `seen` record per unit in `SeenIfNotCopied` that wasn't copied; for a set, one per member, each with `set:"<SetName>"` (§7.3).
- One `run` record: card identity, video and photo roots, verdict, counts.
- A `torn` record only from `OpenOwn()`, when the own file lacks its final `\n` (§11).

Nothing is written to the ledger before Commit, except explicit user actions: Verdict-page decisions (one `decision` per member for a set), Un-dismiss/Undo (`revoke`, one per revoked decision), and the video-root [Copy] (§9.14). All of these go to this PC's own `<videoRoot>\.uas-sort\ledger-<MACHINE>.jsonl` and its local mirror (`LedgerPaths.BackupDir`), through `ILedgerWriter.Append`.

### 10.5 Audit and verdict (`CardAudit`)

**Before auditing**
1. Re-check `CurrentIdentity()`. A mismatch makes the verdict **NotSafe**: "The card in E: is not the one that was offloaded".
2. Re-list the card (listing only, under 1 s) and diff it against the inventory on `(RelPath, Size, MtimeUtc, CreationUtc, Attributes)`, excluding `System Volume Information`.
   - Any added, removed or changed entry is **Unaccounted**, "changed since scan".
   - Differences only in LastAccessTimeUtc are listed as "OS updated last-access times" and don't count against the verdict (§13, M4).

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
  - **[Record selected photos as already imported]** writes `decision` records of kind `assumedImported`. Selection is per tile or per day; it covers photos and sets only.
  - **[Mark selected as not needed]** writes `decision` records of kind `dismissed`.
    - **Photos and sets** can be selected individually or per day.
    - **Unknown files, truncated clips and all other videos only one at a time.** Truncated clips are videos, and bulk selection is never offered for videos. Unknown files usually have no local date, so they are never selected per day either.
  - Both actions open a confirmation dialog showing counts by kind and total GB. Then they append to the ledger and recompute the verdict.
  - **[Undo]** on the page, and later [Un-dismiss] on the Other and Photos tabs, append `revoke` records.
- **Report** is saved automatically, and there is a button to open it.
- **Done** goes back to the Card stage.

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
| Ledger | `<videoRoot>\.uas-sort\ledger-<MACHINE>.jsonl` (append; the only file the app writes there). **Reads the union of every `ledger*.jsonl`** directly in `<videoRoot>\.uas-sort\`, including other PCs' files and OneDrive conflict copies, deduplicated by record `id` | JSON Lines, `t` discriminator, `v:1` | During Commit and on explicit decision/undo/[Copy] actions | Append and `FlushFileBuffers` per line. The own file is opened with `FileShare.Read`, so there is a single writer. **Torn tail repair:** if the own file doesn't end in `\n` (a crash mid-append), `OpenOwn()` first appends `\n` and a `torn` record naming that line's number N; readers skip line N of that machine's file without a parse issue. A torn **final** line of any file is skipped too. **Any other bad line is a Blocking issue until [Accept and continue]** (§9.10), and the verdict is then capped at SafeWithAssumptions |
| Ledger backup | `%LOCALAPPDATA%\uas-sort\ledger-backup\<root key>\` (**local**, never under the library or synced; one subfolder per video root) | A live mirror of the own file (appended together with it) plus `snapshots\<yyyyMMdd-HHmmss>-<run8>\` of all ledger files at each Start offload (keep 20 per root) | Mirror: during Commit and on every own-file append (decisions, revokes, [Copy]). Snapshots: at Start offload | Restorable by hand; Settings has [Open]. Also the source for settings recovery and for [Copy] when the old video root is gone (latest snapshot ∪ mirror, deduped by `id`) |
| Drafts | `drafts\<DraftKey>.json` | `{v, cardKey, inventoryHash, savedUtc, tuning, edits[]}` | 1 s after a change | Temp file, then replace |
| Reports | `reports\<yyyyMMdd-HHmmss>-<run8>.json` | Full run report: settings snapshot, plan summary, outcome per file, audit lines, card diff, verdict | End of each run | Written once |
| Logs | `logs\uas-sort-<yyyyMMdd>.log` | Text | Always | Kept 14 days |
| WebView2 | `WebView2\` | WebView2's own data, including the HTTP tile cache | By WebView2 | — |
| Thumbnails | Memory only | Decoded images, LRU of 400 | — | Re-read from the card; about 12.7 KB each |
| Selftest | `%TEMP%\uas-sort-selftest-<guid>\` (settings, a temp video root whose `.uas-sort\` holds the test ledger, WebView2) | — | Only under `--selftest` | Deleted at exit |

**Ledger folder status** (`ILedgerStore.Check()` → `LedgerFolderStatus`, §3; attributes and security descriptors only, before every load; `Writable` comes from the ReadOnly attribute plus `GetFileSecurityW` + `AccessCheck` for `FILE_ADD_FILE` on the folder (or the video root, if the folder is missing) and `FILE_APPEND_DATA` on the own file, which reads the security descriptor with `READ_CONTROL` and never opens data)
- **`VideoRootMissing`:** Blocking, because the root itself is missing (§10.2).
- **`Missing`** (`<videoRoot>\.uas-sort` doesn't exist): Info `LedgerNoHistory`, "No history yet". The folder is created (the folder only) and pinned on **Start offload** by `EnsureFolder()`. If creating it fails, Commit stops before the first copy.
- **`Empty`** (the folder exists without any `ledger*.jsonl`): Info `LedgerNoHistory`, as above.
- **`NotPinned`** (in a sync root, `Pinned` false): Warning InfoBar `LedgerNotPinned`, "Set `UAS Videos\.uas-sort` to *Always keep on this device*", with [Keep on this device]. Start offload pins it anyway, so the preflight sheet doesn't repeat it.
- **`CloudOnly`** (any `ledger*.jsonl` in `CloudOnlyFiles`): Blocking `LedgerCloudOnly`, with [Keep on this device] (§4.3). Never opened until it is local.
- **`Unwritable`:** Blocking `LedgerUnwritable` for Commit.
- **Video root changed, and `<new root>\.uas-sort\` has no `ledger*.jsonl`** (the new root's `Check()` gives `Missing` or `Empty` while the loaded snapshot has records): Warning `NoHistoryInNewRoot` on the Settings page, "No history found in `<new root>\.uas-sort`; copy current ledger there?", with [Copy] and [Start empty] (§9.14).

**`settings.json`**. The values shown are this PC's; defaults are derived at Setup, never literal. **There is no `ledgerDir` key.** The ledger folder is always derived as `<videoRoot>\.uas-sort` (here `C:\Users\damia\OneDrive\Pictures\UAS Videos\.uas-sort`).

```json
{ "schema": 1, "rootsConfirmed": true,
  "videoRoot": "C:\\Users\\damia\\OneDrive\\Pictures\\UAS Videos",
  "photoRoot": "C:\\Users\\damia\\OneDrive\\Pictures\\UAS Videos\\Picture Offload",
  "previousPhotoRoots": [],
  "radiusMiles": 50, "gapDays": 1, "droneClockZone": "America/New_York", "copyJpgTwin": true,
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
 "sessionUtc":"2026-09-27T17:59:28Z","serial":"1581F895C261M01705SH","set":null}
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
{"t":"run","v":1,"id":"…","machine":"DESKTOP-A","run":"8f1c…","start":"…","end":"…","app":"0.1.0",
 "card":{"serial":"1A2B3C4D","label":null,"fs":"exFAT","model":"FC9113","inventoryHash":"…"},
 "roots":{"video":"C:\\…\\UAS Videos","photo":"C:\\…\\Picture Offload"},
 "verdict":"SafeWithAssumptions","counts":{"VerifiedThisRun":17,"AssumedByRule":40}}
```

**Ledger snapshot and scale**
- Loading builds `LedgerSnapshot` (§3): files, sets, decisions after revokes, seen, folders, runs, parse issues, and the source files.
- At 10k lines the ledger is about 2.5 MB and loads in about 120 ms (measured single-file). The per-machine union scales linearly.

---

## 12. Error handling & recovery

| Situation | Detection | Behaviour | Recovery |
|---|---|---|---|
| No card | Detector finds nothing | Card stage: "Insert a card", Browse, Rescan; arrival is watched | F5 / auto on arrival |
| Browsed folder is inside, equal to or contains a root, the ledger folder (`<videoRoot>\.uas-sort`) or a sync root | `CardSourceValidator` | Refused with the reason | Pick the card |
| Browsed folder without DCIM | Validator | Refused: "pick the card's top folder" | — |
| Card write-protected | `FILE_READ_ONLY_VOLUME` | Badge (informational) | — |
| Card removed during Scan | IOException / device gone | Scan aborts; the draft is kept | Reinsert, then Rescan |
| Card removed during Review | Removal event | InfoBar; Offload disabled; thumbnails show placeholders | Reinsert |
| **Different card inserted** (same letter) | Identity check at Commit start, per file, and at verdict | Blocking at preflight; `CardSwapped` stops the run; verdict NotSafe | Rescan |
| Card enumeration error | Lister error list | `ForcesNotSafe` scan warning on the Other tab | Re-seat the card, then Rescan |
| Probe failure on one file | Exception inside the probe | `ProbeError`; time from filename, else mtime; copyable | — |
| Truncated MP4 | No `moov` / `.trinf` | GPS from fallback; unticked; chip | Repair in the drone and rescan, or tick |
| No GPS / implausible generic GPS | Search exhausted / plausibility gate | Time-only grouping; badge | Move items by hand if needed |
| `Etc/*` or ambiguous zone | GeoTimeZone result | Fallback zone (independent of R/G); `CheckDate` chip | Retarget or rename if the date is wrong |
| Drone clock doesn't fit any zone | Zone learner | Nearest-sample mode; clock banner explains | — |
| Implausible clock | Before 2015 or after now + 1 d | `ClockNotSet` warning | — |
| A library root is missing (D: unplugged) | Listing fails | Banner (`RootMissing`). Newness for that root is shown as unknown (items stay New and unticked, with a reason). Blocking at preflight only if an included job targets that root; the video root always blocks, because it holds the ledger | Plug in, then Rescan |
| Access denied on a library subfolder | Listing error | Warning; the folder is skipped from the index | — |
| Ledger folder `<videoRoot>\.uas-sort` has a cloud-only `ledger*.jsonl`, or isn't writable | `ILedgerStore.Check` (attributes before any open) | InfoBar; Blocking for Commit; [Keep on this device] | Pin the folder and let OneDrive download it, or fix permissions; then Rescan |
| Ledger folder `<videoRoot>\.uas-sort` missing | `ILedgerStore.Check` | Info "No history yet"; created (the folder only) and pinned on Start offload; a failed create stops Commit before the first copy | If the app was used on another PC, wait for OneDrive to sync, then Rescan |
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

---

## 13. Testing strategy

Everything runs with `dotnet test --solution uas-sort.slnx` (xUnit v3 on Microsoft.Testing.Platform). From WSL, the same command runs through `dotnet.exe`. `tools/build.ps1 -CheckBannedApi` runs the analyzer probe.

**Unit tests: Core**

*Planner, ported from `docs/research/spikes/grouping/test_grouping.py` (37 cases)*
- **Summary.** 24 keep their expectations ("Same": spike strings mapped to enums, and the approved model may add assertions such as the blank-name Blocking issue); 9 are restated for the approved model ("Changed"); 3 are R-dependent; 1 is dropped. Verified 2026-09-27 by re-running `test_grouping.py` with the default R set to 80.467 km: 34 of 37 pass, and the 3 R-dependent ones fail at R = 50 mi because they depend on Council and Anvil being split.
- **Fixture.** The spike's constants: ANVIL (64.5627, −165.3696), COUNCIL (64.6935, −164.2657), NOME_A (64.6932, −165.7665), NOME_B (64.5925, −165.6731), ZACHAR (57.5368, −153.7484), KODIAK_TOWN (57.7996, −152.3902), NEWPORT_AM (41.5155, −71.2967), NEWPORT_PM (41.4762, −71.3237), MAKAHA (21.47, −158.21); PC zone `America/Anchorage`. `vid(stamp, n, loc)` is a DJI clip `DJI_<stamp>_<n:0000>_D.MP4` whose `mvhd` = stamp + 4 h as UTC and mtime = `mvhd` + 90 s; `dng(stamp, n, loc)` is a DNG with EXIF DTO = stamp. Z = `vid` 0123, 0124, 0148 at 20260927140127, 140144, 142416, ZACHAR; ZREL = `2026\2026-09\2026-09-27 Zachar Bay`. Defaults: `Tuning(50, 1)`, `droneClockZone` America/New_York, `IPlaceIndex` null, empty ledger. Scenarios are built as `ScanResult`s by `UasSort.Testing`'s scenario builder and run through `Planner.Prepare` + `Derive`.
- **String mapping.** `droneclock+learned` → `DroneClockZone` (a zone fits); `droneclock+setting` → `DroneClockSetting` + `ClockFromSetting`; `mtime` → `Mtime`; `autel-*-floating` → dropped; `high (date + location)` → `High`, Why "same dates, <0.1 mi"; `medium…` → `Medium` with the §8.5 Why; `AppendSplit` → two groups split by a `LibraryFolder` wall, each an Append; `PhotosOnly` groups → `PhotoDays`; `PossibleDuplicate` → `IsNew(NoMatch)`; `RemovedFromLibrary` → `Imported(LedgerVerified)`; `/` in paths → `\`; `radius_km` → `RadiusMiles`.

| # | Spike test → C# test | Inputs | Expected (approved model) | Port |
|---|---|---|---|---|
| 1 | `test_midnight_local_date_uses_site_zone_not_filename` → `Midnight_LocalDateFromSiteZone` | vid 20260726035000 #1, 20260726041000 #2, ANVIL | 1 group; Start 2026-07-25; `NewFolderRel(2026-07-25, "Anvil")` = `2026\2026-07\2026-07-25 Anvil` | Same |
| 2 | `test_midnight_with_G0_not_split_inside_session` → `Midnight_G0_HoursGuardKeepsOneGroup` | as #1, `Tuning(50, 0)` | 1 group (H = 3 h) | Same |
| 3 | `test_dng_uses_learned_offset_and_site_zone` → `Dng_UsesClockZoneAndSiteZone` | vid 20260726035000 #1, dng 20260726035500 #2, ANVIL | DNG: `DroneClockZone` (America/New_York), CaptureUtc 2026-07-26T07:55Z, LocalDate 2026-07-25 | Same |
| 4 | `test_hawaii_site_zone_differs_from_pc_zone` → `Hawaii_SiteZoneNotPcZone` | vid 20260301053000 #1, MAKAHA | Tz `Pacific/Honolulu`, LocalDate 2026-02-28 | Same |
| 5 | `test_offset_nearest_sample_handles_clock_change` → `ZoneLearner_DstChangeStillFitsNewYork` | vid 20261020120000 #1 (−4 h), vid 20261110120000 #2 (−5 h), dng 20261110121000 #3, ANVIL | `ClockMode.Zone`, America/New_York; DNG `DroneClockZone`, 2026-11-10T17:10Z | Changed (zone learner) |
| 6 | `test_offset_from_setting_when_card_has_no_mp4` → `NoMp4_UsesStoredZone` | dng 20260927150000 #1, ZACHAR; once with `droneClockZone` = America/New_York set explicitly, once with the default | Both: `ClockMode.Setting`, `DroneClockSetting`, flag `ClockFromSetting`, 2026-09-27T19:00Z. Never `Mtime`: Setting mode always has a zone | Changed |
| 7 | `test_truncated_clip_without_moov_uses_filename_and_joins` → `Truncated_TimedByClockAndJoins` | vid 20260726235645 #1, 20260727002013 #14 (no `moov`), 20260727002118 #15, ANVIL | #14: `DroneClockZone`, 2026-07-27T04:20:13Z, flag `Truncated`; 1 group | Same |
| 8 | `test_autel_floating_time_no_gps` | — | Dropped with the Autel card path (§15 Q8) | Dropped |
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
| 24 | `test_append_today_via_ledger_centroid` → `AppendToday_ViaLedgerCentroid` | card #160; library Z in ZREL; ledger `file` records for Z with ZACHAR lat/lon, tz America/Anchorage | Append(ZREL) · High, "same dates, <0.1 mi" | Same |
| 25 | `test_append_today_location_unknown_same_date_is_medium` → `AppendToday_LocationUnknown_Medium` | card #160; library Z in ZREL | Append(ZREL) · Medium, "same dates, location unknown" | Same |
| 26 | `test_next_day_far_away_new_folder` → `NextDay_Far_NewFolder` | card vid 20260928180000 #170 KODIAK_TOWN; library Z; ledger centroid as #24 | NewFolder `2026\2026-09\2026-09-28` + Blocking `EmptyFolderName` (53.4 mi > R) | Same |
| 27 | `test_next_day_same_place_appends` → `NextDay_SamePlace_AppendHigh` | card #170 at ZACHAR; library Z; ledger centroid | Append(ZREL) · High, "next day, <0.1 mi" | Same |
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
- Adjacent-day appends: < 10 mi → High; 10 mi–R → Medium with hint.
- Pins: membership change → Warning with [Keep]/[Reset]; conflicting pins after merge → Blocking; `Rename` on Append → `Rejected(RenameExistingFolder)`.
- Retarget into `<videoRoot>\.uas-sort`, `<videoRoot>\.uas-sort\x`, the photo root or a previous photo root → `Rejected(RetargetIntoReservedFolder)`; to a later-dated folder without confirmation → `Rejected(RetargetLaterDatedFolderUnconfirmed)`.
- Fold rule: a group with Conflict, Truncated or ProbeFailed members never folds.
- The set-folder rule: Plain, Resume, DateSuffixed, `(2)`, Imported; mtime ±2 s; an empty existing folder → Plain; ledger first-frame check.
- Conflict `(n)` naming, and ` (n)` removal for matching. **Photo rule 2b:** a DNG whose name exists in the photo root at another size → Conflict, unticked; ticked → `X (2).DNG` with its twin `X (2).JPG`.
- **`SessionKey` tolerance:** two clips with the same serial and `SessionUtc` 0.8 s apart are one session; 3 s apart are two; different serials are never one session.
- The 400-character limit on the **temp** path; suggestions ranked per day.
- **Issue catalogue:** each `IssueCode` of §9.10 is raised by the component, with the severity, `RequiresAckAtPreflight` flag and quick-fix edits the table gives.

*Performance* (Release build, M3)
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
- **Zone learner:**
  - summer samples → New York;
  - a winter card with −5 samples → New York (DST handled);
  - a fixed −4 in winter → no zone fits → nearest-sample mode;
  - a photo-only winter card → the stored zone (−5);
  - library member starts and the watermark use the same model.
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
- Case and form: `.UAS-SORT\LEDGER-B.JSONL` is allowed (case-insensitive); `.uas-sort2\ledger-A.jsonl` is refused (the canonical-path half of this lives in the Windows `subst`/junction test).

*Source validation*
- Refuses equal, inside and containing cases for each root, `previousPhotoRoots`, the ledger folder (`<videoRoot>\.uas-sort`, including a browse straight into it) and sync roots.
- Canonical comparison through subst and junctions (fakes plus one Windows integration case).

**Cross-assembly and JSON (`Review.Tests`)**
- A `switch` without a default arm on Core's `CopyOutcome` (closed) and `GpsProbe` (union) compiles with `TreatWarningsAsErrors`.
- A closed `PlanEdit` and `TargetChoice` round-trip through source-generated JSON.

**Golden replay**
- **Fixture.** `tests/UasSort.Testing/Replay/library-listing.json` is generated **once**, on 2026-09-27, by `tools/fixtures/make-replay-fixture.ps1` from the library **listing** (names, sizes, mtimes, attributes; no file is opened) plus the GPS already extracted in `docs/research/spikes/djmd/calibration.json` and `docs/research/spikes/grouping/more_gps.json`: the same inputs as `docs/research/spikes/grouping/replay50.py`. It is checked in and **never regenerated by tests**, so later imports can't change the expectations. The repo is private, and no file content is included. Schema:

```json
{ "v": 1, "snapshotUtc": "2026-09-27T20:00:00Z", "pcZone": "America/Anchorage",
  "entries": [ { "relPath": "2026/2026-07/2026-07-25 Council Road/DJI_20260725232655_0117_D.MP4",
                 "size": 1234567890, "mtimeUtc": "2026-07-26T03:28:25Z", "attributes": 5248544 } ],
  "clips": { "DJI_20260725232655_0117_D.MP4": { "lat": 64.6935, "lon": -164.2657, "mvhdUtc": "2026-07-26T03:26:55Z",
                                                "hasMoov": true, "mvhdSimulated": false } } }
```

  `relPath` is relative to the video root (the photo root's entries sit under `Picture Offload/`). `clips` has one entry per library MP4: GPS where the research read it; for DJI clips whose `mvhd` wasn't read (Kodiak, cloud-only) `mvhdUtc` = filename stamp + 4 h with `mvhdSimulated: true`, as `replay50.py` does; Anvil 0014 and 0024 have `hasMoov: false`; Makaha's Autel `MAX_####.MP4` clips have no GPS and no `mvhdUtc`.
- **How items are built.** The replay builds `Item`s directly and never runs `CardClassifier` (on a real card the Autel clips would be Unknown). DJI clips go through `TimeResolver` with the fixture's GPS and `pcZone`; the clock is learned from the clips with a real `mvhd`. Makaha's clips get `TimeSource.Mtime`, no GPS and zone `pcZone`: their mtimes are the Autel wall clock read as AKDT, so their local date is 2022-03-27, the folder's date. Every scenario uses `Tuning(50, 1)` unless stated, `IPlaceIndex` = null, an empty ledger and no edits.
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

**View-model tests** (`Review.Tests`, no WinUI)
- Scan → Review; boundary-chip merge; split-here; move to a new group; quick fix as one undo entry.
- Slider: a drag previews and becomes one undo entry; **keyboard/wheel commit after 400 ms idle** (FakeTimeProvider); latest-wins cancellation.
- **Ordering, with `GatedPlanDeriver`** (an `IPlanDeriver` whose derives block until the test opens a gate): a slow earlier preview never overwrites a later one; an edit queued behind a slow derive is validated against the plan that includes the earlier edit; `Changed` arrives off the UI thread and the VM applies it through a fake `IUiDispatcher`; a lower `Revision` is ignored; a `Rejected` edit changes nothing and returns without waiting for a derive.
- Draft resume with dropped edits and pin warnings; a changed `InventoryHash` still offers the draft with the right dropped count.
- Rename: validation, `Rejected` on Append, suggestion ranking; `SuggestionVm.ToString()` equals the text.
- Offload enabled or disabled by blocking issues; preflight acknowledgements; **[Accept and continue]** turns a Blocking `LedgerParseIssue` into a RequiresAck Warning for this session, the draft doesn't carry it, and a rescan raises it again.
- Verdict page: nothing preselected; per-day selection only for photos and sets; unknown files, truncated clips and other videos one at a time; confirmation counts and GB; undo writes `revoke`.
- Map/grid selection sync.
- `MapBridge` JSON golden messages: deserialise the exact objects of §9.6, the ones map.js produces (**`v` before `type`**), including `ping`/`pong`; `[lon,lat]`; miles labels; an unknown type is logged, not thrown.
- Verdict wording, including the card identity and the name+size split.
- `CollectionSync` keeps the selection.

**UI smoke test** (`uas-sort.exe --selftest`, run by `deploy.ps1` against the **trimmed** publish)
- **Isolation.** It skips single-instance registration and launches off-screen. It uses `%TEMP%\uas-sort-selftest-<guid>` for settings, WebView2 and a temp video root, whose `.uas-sort\` holds the test ledger.
- **Bindings.** It realises one of each templated element from a built-in fake plan and asserts the rendered text:
  - a group card with a boundary-chip header and thumbnail strip;
  - a clip row with a day-split banner;
  - the rename suggestion popup opened in code (text "Anvil Mountain", not a record `ToString`);
  - a Photos tile;
  - typing a space in the rename box leaves `Included` unchanged.
- **Map.** Waits for WebView2 `ready` with MapLibre under the virtual host, sends a ping and waits for the pong.
- **Probes.** Runs StillProbe on the embedded `selftest.dng`, plus a GeoTimeZone and `TimeZoneInfo` lookup.
- **JSON under trimming.** Round-trips the embedded `ledger-v1.jsonl` (every record type) and a closed `PlanEdit`/`TargetChoice`.
- **Placeholder visibility.** Lists this PC's configured video root (read from the real `settings.json`; listing only, with `.uas-sort` excluded) and requires at least one entry showing `0x400000` or `0x1000`. If the root has no cloud-only files, the check reports "not applicable" and deploy needs `-AllowNoPlaceholders`.
- **Result.** Writes `selftest-result.json` (each check's result, plus `firstFrameMs`: process start to the first `CompositionTarget.Rendering` of the main window), then `Environment.Exit(ok ? 0 : 1)`. `deploy.ps1` runs it twice, checks both exit codes and result files, each with a 60 s timeout, and aborts on failure or when the second (warm) run's `firstFrameMs` exceeds 1000.
- **M0's minimal selftest** is a subset: the probe page renders; WebView2 reaches the virtual host; MetadataExtractor reads the checked-in `SelfTest/m0-exif.jpg` (≤ 2 KB, hand-assembled EXIF with DTO and GPS, no user data) through the Stream API directly; a GeoTimeZone lookup; a closed-record JSON round-trip. The synthetic DNG and StillProbe arrive in M1.
- FlaUI, `winapp ui` and WinAppDriver are **not** used: WinAppDriver is unmaintained, and the others are unverified on WinUI.

**Acceptance on the first real card**
1. **Before anything else,** save a listing snapshot of the card (relative path, size, mtime, creation, **last-access**, attributes).
2. Run `uas-sort-cli plan --card E:\ --json --expect tests/acceptance/first-card-expected.json` (§4.5; the user writes that file before M4). It passes at ≤ 2 edits. Record the scan + plan time for success criterion 4. Fix classification surprises: LRF presence, cover JPGs (enable the "video cover" rule only if confirmed), the tele-camera suffix, hyperlapse frame names, folder rollover.
3. Re-list the card and diff against step 1. **If LastAccessTimeUtc changed** on exFAT, document it in the verdict text ("Windows updated last-access dates; uas-sort wrote nothing") and suggest the SD lock switch, which shows as the write-protected badge. The app never tries to suppress it: `SetFileTime(-1)` needs a write handle on the card.
4. Do a rehearsal offload in the app with the roots pointed at `%TEMP%\uas-sort-rehearsal\{video,photo}`. Its ledger then lands in `%TEMP%\uas-sort-rehearsal\video\.uas-sort\`. Check:
   - the verdict and the report;
   - OneDrive not syncing `.tmp` files;
   - unbuffered verify and the post-run directory/file flush on a scratch folder on exFAT D:;
   - [Eject D:].
5. Point the video root back at the real library. At the ledger prompt, answer **[Start empty]** so rehearsal records never enter the real ledger; confirm its dialog if it appears (the rehearsal's `file` records match videos already in the library by name and size, and the real library's watermark is exactly what a manual history implies). Then check that the pin sticks: [Keep on this device] on `UAS Videos\.uas-sort` must show as "Always keep on this device" in Explorer (UNVERIFIED until then). Do the real offload. Diff the card listing again (excluding `System Volume Information`).
6. Rescan the same card: everything shows Imported, and the verdict is Safe (or SafeWithAssumptions only for photos the user chose to leave).

---

## 14. Milestones (command-line first; about 34.5 developer-days plus ~10% contingency, about 38)

| # | Work | Exit | Days |
|---|---|---|---|
| M0 | **Setup and stack proof.** <br>• SDK 11 RC1 and PowerShell 7 (the user installs both via winget). <br>• Repo skeleton: slnx, props, global.json, pinned `AnalysisLevel`, `Directory.Packages.props` with exact pins, BannedSymbols, analyzer probe project; build and WSL scripts; first restore. <br>• The probe page uses GridSplitter (Sizers), SettingsCard, FolderPicker (owner from `AppWindow.Id`), AppInstance plus the named-mutex fallback, WebView2, and an ItemsView with a template selector. MetadataExtractor is exercised directly on the checked-in `m0-exif.jpg` (StillProbe and the synthetic DNG are M1 work). <br>• Cross-assembly union/closed exhaustiveness test; closed-record JSON round-trip. <br>• A 1-hour check that the VS Code C# extension handles `union` and `closed` without false errors. <br>• Fallbacks if the exit build fails: toolkit 8.2 with the full package, or fixed panes | The real App project with a 30-line probe page restores, builds and **publishes trimmed + ReadyToRun with `TreatWarningsAsErrors` on**, then passes the **M0 selftest** (§13): probe page renders, WebView2 reaches the virtual host, MetadataExtractor Stream read of the checked-in `m0-exif.jpg`, GeoTimeZone lookup, closed-record JSON round-trip. The cross-assembly exhaustiveness test is green | 1 |
| M1 | **Card and media.** <br>• Core model, CardDetector, CardSourceValidator (policy), CardClassifier (fail-safe rules). <br>• Mp4Probe: box walker, sample tables, protobuf, model table, generic search, multi-sample search, mdat fallback, `tnal` range. <br>• SyntheticMp4Builder and SyntheticDngBuilder; StillProbe (Stream overloads); MetadataHarvester | Classification, synthetic MP4/DNG (incl. the ≤ 16-read budget) and source-validation policy tests green | 3.5 |
| M2 | **Time, library and ledger.** <br>• DroneClock zone learner, TimeResolver, GpsPlausibility, GeoTimeZoneResolver. <br>• LibraryIndex (all roots, `previousPhotoRoots`, fixed watermark). <br>• Ledger reader: derived `<videoRoot>\.uas-sort` location (`LedgerPaths`), multi-file union, dedupe, parse issues, `torn` markers, revokes, attribute-first status check (`LedgerFolderStatus`). `IoGuardPolicy` with the guard exemption for `ledger*.jsonl` only; `.uas-sort` excluded from the library listing (`excludeDirNames`). Settings store with recovery and the read-only load. <br>• `WindowsVolumeProvider`, WindowsDirectoryLister (explicit options, error list), `WindowsCardReaderFactory`/WindowsCardReader (identity, read-only), PlaceholderGuard, sync-root detection, canonical paths (`PathFacts`). <br>• FakeFileSystem with the tripwire, calling `IoGuardPolicy` | Time, newness-ledger (incl. `torn` markers), settings and `IoGuardPolicy` tests green; the scan half of the fake-FS tripwire; the Windows lister/reader integration tests | 2 |
| M3 | **Planning.** <br>• NewnessRules (with `seen`, per-member set records, the no-watermark rule), Clusterer with causes and the structural-edit algorithm (§8.9), DaySplitFinder, FolderDecider (New-subset with `included`, cross-day rules, two-pass UserSplit), naming, SetFolderNamer, Planner (issue catalogue) and PlanSession (threading contract, pins, quick fixes). <br>• The 37 ported tests (§13 table), golden replay from the checked-in fixture, edit invariants and the derive benchmark. <br>• **CLI `plan`** (§4.5) | The 37 ported tests (§13 table), new planning cases, invariants, golden replay (A0, A–E) and the derive benchmark green; `uas-sort-cli plan --json` runs on a copied card folder under `%TEMP%` and writes nothing | 4.5 |
| M4 | **Checkpoint:** dry run of the CLI on the first real card; card-listing diff including last-access; fix any classification or time surprises | `plan --expect` ≤ 2 edits from `tests/acceptance/first-card-expected.json`; the before/after card diff shows no app change; scan + plan time recorded | 0.5 |
| M5 | **Offload engine.** <br>• Compiler, Preflight, CopyEngine (identity per file, confirm, post-run flush), CardAudit (re-list, per-file, worst-of). <br>• GuardedFileOps and WindowsFileOps (`LibraryImport`, `\\?\`, `File.OpenHandle`). <br>• Ledger writer (`OpenOwn` with torn-tail repair, `EnsureFolder`, mirror, `SnapshotToBackup`, `seen`), report store, power request, offload lock, eject. <br>• Fault tests and Windows integration tests | Fault injection (every §10.3 row), the audit table, the offload half of the tripwire, and the Windows integration tests green | 3.5 |
| M6a | **WinUI shell.** <br>• `Program.Main` single instance; TitleBar, Mica and Frame; stages. <br>• **Setup stage and Settings page**: roots; derived ledger-folder status with [Keep on this device] and [Open]; the video-root change prompt ([Copy] / [Start empty]); clock zone; map URLs; About. <br>• Card and Scan pages. <br>• Timeline ItemsView (cards with chip headers, folds); group card (rename with SuggestionVm, read-only on Append, retarget flyout built on Opening, Browse existing validation) | Named VM tests for Setup, Settings (incl. [Copy]/[Start empty]), Card, Scan, timeline and group card; selftest template checks for the group card | 5 |
| M6b | **Review lists and quality.** <br>• Clip list (ItemsView, day-split banners, `Thumb.Key` thumbnails); Photos and Other tabs (Dismissed / Confirmed sections). <br>• Undo/redo, drafts, scoped keyboard handling, queued dialogs. <br>• All VMs plus tests; full `--selftest` | All VM tests (incl. the `GatedPlanDeriver` ordering tests) green; the full selftest template checks pass | 4.5 |
| M7 | **Commit UI.** Preflight sheet with acknowledgements; Copy page; Verdict page (per-file lines, individual decisions, confirmation, undo, eject); device-arrival refresh (WM_DEVICECHANGE via window subclassing; UNVERIFIED), disabled during Commit. **→ The first real, verified offload with a map-less app, around day 28** | A real offload with 0 failures and a verdict of Safe or SafeWithAssumptions | 3 |
| M8 | **Map.** <br>• First task: MapLibre ESM and its worker under the WebView2 virtual host (`.mjs` MIME), with the `.js`-rename / WebResourceRequested / Leaflet fallbacks. <br>• Then the bridge and messages (out-of-order metadata, unknown types logged), selection sync, live R/G, base toggle, offline canvas | `MapBridge` golden messages green; selftest `ready` + `ping`/`pong` (skipped if M8 is dropped) | 3 |
| M9 | **GeoNames.** `build-places.cs` extract, PlaceIndex, suggester | PlaceIndex round-trip: "Anvil Mountain" ≤ 0.2 mi, "Zachar Bay" ≤ 0.4 mi | 1.5 |
| M10 | **Packaging and acceptance.** <br>• Publish for `win-x64` and `win-arm64`; `deploy.ps1` (selftest gate, `.lnk`, keep 2 versions). <br>• Rehearsal and first full-app offload (exFAT flush and eject checks). <br>• **Native AOT attempt behind `-p:UasAot=true`** only if the user has installed the C++ build-tools component; it ships only if `--selftest` passes and startup improves | `deploy.ps1` passes its selftest gate on x64, including the warm first frame ≤ 1 s | 1.5 |
| SDK | **RC2 bump** (~Oct 13, UNVERIFIED) and **GA bump** (Nov 10), each when it ships. Each re-runs the trimmed publish, `--selftest` and the analyzer check. `AnalysisLevel` stays pinned, so new rules don't appear by surprise | The trimmed publish, `--selftest` and the analyzer check pass | 0.5 + 0.5 |

- Calendar: RC2 lands around M2–M3, and GA around M7–M8. Neither is tied to M10.
- After M4, grouping and newness are proven on a real card before any UI exists.
- M8 (map) and M9 (GeoNames) can each be dropped without affecting any other milestone. Without M9, names come from ledger folders only.

---

## 15. Questions: accepted defaults and what is still open

- **Former Q1 (which synced folder holds the ledger) is resolved and removed.** The user decided the ledger lives in `<videoRoot>\.uas-sort\` (§1.1, §4.3, §11). The remaining questions are renumbered.
- The user approved the design on 2026-09-27 and accepted every default below without objection. None of them waits on a decision.
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
5. **Packaging and code generation (departures from "most modern frameworks").**
   - **A, approved:** an unpackaged, self-contained, trimmed ReadyToRun folder plus a Start-menu `.lnk`. It starts warm in 0.37 s and needs no certificate or runtime installs.
   - **B, not chosen:** self-contained MSIX via the winapp CLI, with a self-signed certificate trusted on each PC or a loose Developer Mode registration. It would give package identity and a clean uninstall. Self-contained MSIX is UNVERIFIED. MSIX stays on the Later list (§1.4).
   - **C, an optional later attempt:** Native AOT.
     - It needs the "C++ build tools" component added to the existing VS Build Tools 2022. That is a machine-wide install, whenever the user chooses to try it.
     - M10 tries AOT only if that component is installed. It ships only if it passes `--selftest` and starts faster.
   - VS 2026 Insiders (XAML designer, Hot Reload) stays optional; the default is the dotnet CLI plus VS Code.
6. **Are any of the PCs ARM64?**
   - *Accepted default:* `deploy.ps1` publishes for the RID of the machine it runs on (x64 here). `win-arm64` is built in M10.
   - **Still open:** ARM64 is UNVERIFIED until it runs on an ARM64 PC.
7. **PowerShell 7.** The scripts use `pwsh`, which isn't installed on this PC.
   - *Approved:* the user installs it once with `winget install Microsoft.PowerShell` at implementation start, alongside the .NET 11 SDK preview. Every script runs via `pwsh -NoProfile -ExecutionPolicy Bypass -File`.
8. **Autel cards.**
   - *Accepted default:* Autel cards are not supported; their media shows as Unknown (NotSafe). Legacy Autel folders already in the library still match by name and size, and their members' start times come from mtime (§7.1).

---

## Review disposition

This table records how all 53 findings of the three review lenses (15 requirements, 22 safety, 16 feasibility; `docs/research/09-design-review-findings.json`) were handled before approval. Row numbers are the findings' 1-based positions in that file. Rows that a later user decision changed carry a *Note*.

| # | Lens | Severity | Finding (one line) | Disposition | Reason / where |
|---|---|---|---|---|---|
| 1 | requirements | major | Banned list covers only writes; library **reads** and `UtcNow` are unbanned | **Applied** | §2.4: whole-type and read bans, `UtcNow`/`DateTimeOffset` bans, a content test and a probe build; ICardReader/IFileOps are the only openers (§4.3) |
| 2 | requirements | major | "Personal computers" means several PCs, but the ledger was local to one | **Applied** | §1.1 fact; §11: per-machine files, union read, pinned-folder check without UnsafeIoException, local backups. *Note: superseded by user decision: ledger in `<videoRoot>\.uas-sort` (derived, no `ledgerDir` setting; the guard exemption is in §4.3). The former §15 Q1 was removed.* |
| 3 | requirements | major | Settings UI and first-run root choice unscheduled; user path hardcoded | **Applied** | §9.1 Setup stage, §9.14 Settings page; defaults derived from KnownFolder Pictures (the video root; the ledger folder follows it); scheduled in M6a (+1 day). *Note: the former synced-folder ledger default is superseded by user decision: ledger in `<videoRoot>\.uas-sort`, and Setup no longer asks for a ledger folder.* |
| 4 | requirements | minor | 3 R-dependent tests (not 1); Zachar↔Kodiak fixture is 53.4 mi | **Applied** | Re-verified 2026-09-27 (34/37 pass at 50 mi); §13 lists all three with the pin or duplicate plan; §8.4 shows 53.4 mi and a 3.4 mi margin |
| 5 | requirements | minor | Unpackaged and no-AOT silently deviate from the "most modern frameworks" decision; System.* not on 11.x | **Applied** | §1.1 deviation table; §15 Q5 options A/B/C (A approved 2026-09-27; AOT an optional later attempt in M10); System.IO.Hashing 11.0.0-rc.1. TimeProvider.Testing stays 10.10.0 because no 11.x exists |
| 6 | requirements | minor | Browse could select a library folder and probe library content | **Applied** | `CardSourceValidator` (§4.3) refuses equal, inside or containing roots, previous roots, ledger and sync roots; tests in §13 |
| 7 | requirements | minor | Rename meaning on Append/AlreadyImported undefined | **Applied** | §9.4 read-only box with hint; §8.9 `Rejected(RenameExistingFolder)`; VM test |
| 8 | requirements | minor | Photos in the old Picture Offload are not recognised after the photo root moves | **Applied** | §7.1/§7.3: photos matched against all root listings plus auto-maintained `previousPhotoRoots`; test in §13 |
| 9 | requirements | minor | Scope creep (Autel, make-fake-card, USGS button, NE outlines, map labels, key forwarding, recent offloads, 960 px previews, flight column, RC2 row, StaleFirstFix, GpsTime) | **Applied** | §1.4 non-goals and Later list; Autel only in LibraryIndex; FakeCardWriter moved to Testing; USGS as a settings preset. The map and GeoNames suggestions stay (they are part of the approved rich review UX and research grafts) |
| 10 | requirements | minor | Next-day ≥ 10 mi append labelled High | **Applied** | §8.5 uses the 10 mi threshold → Medium with reason and hint; Scenario D now expects Medium (§13) |
| 11 | requirements | minor | 22-day estimate understated; Settings not scheduled | **Applied** | §14 re-baselined: 34.5 days + contingency ≈ 38; M6 split into M6a/M6b |
| 12 | requirements | minor | Exact package set never built together; TreatWarningsAsErrors risk | **Applied** | M0 exit criterion: full pinned set incl. Sizers, trimmed + ReadyToRun with warnings as errors, `--selftest`; fallbacks named |
| 13 | requirements | minor | Nothing checks that the card is left unmodified | **Applied** | Automatic audit re-list diff (§10.5); acceptance before/after snapshots incl. last-access (§13); read-only access ACL test |
| 14 | requirements | minor | Undefined types, unpinned versions, selftest DNG source, DraftKey hash | **Applied** | §3 supporting types, `TargetChoice` as a closed record with `[JsonPolymorphic]`; versions pinned from the 2026-09-27 NuGet query; SyntheticDngBuilder asset; DraftKey = XxHash64 of the lowercase canonical root |
| 15 | requirements | minor | x64 hardcoded; a PC may be ARM64 | **Applied** | §1.2 assumption row, §2.5 RIDs `win-x64;win-arm64`, deploy picks the machine's RID; §15 Q6 |
| 16 | safety | blocker | App copies advance the watermark and flip unseen photos to "probably imported" | **Applied** | §7.1 watermark from outside-app imports only; §10.4 `seen` records at every Commit end; §7.3 rule 3; two-run and cancel tests (§13) |
| 17 | safety | blocker | Classification fall-through to SkippedByRule; Browse anchoring makes everything "outside DCIM" | **Applied** | §5 fail-safe Unknown for unclaimed media, named skips only, cover JPGs Unknown until proven; DCIM anchoring and refusal (§4.3); NotSafe audit test |
| 18 | safety | major | Default `EnumerationOptions` drop Hidden/System files and swallow errors | **Applied** | §5/§4.1 explicit options, `ContinueOnError` error list → `ForcesNotSafe`; Hidden/System Platform test; enumeration types banned outside Platform |
| 19 | safety | major | No card identity or contents re-check at Commit or verdict | **Applied** | §4.1 `CurrentIdentity`; §10.2 Blocking; §10.3 step 0 `CardSwapped`; §10.5 re-list diff; identity in the headline; refresh disabled during Commit (§9.1) |
| 20 | safety | major | Rename durability not guaranteed; exFAT D: at risk | **Applied (partly)** | §10.3 post-rename `ConfirmFinal`, post-run file and directory flush for non-NTFS volumes, safe-removal note + Eject; G1 no longer claims write-through durability. The `MOVEFILE_WRITE_THROUGH` flag itself stays because the approved Safety graft names a write-through rename, and the flag is harmless |
| 21 | safety | major | Ledger can be silently erased (OneDrive conflicts, bad lines, no backup) | **Applied** | §11 per-machine files and union incl. conflict copies (dedupe by `id`), bad line → Blocking and verdict cap, selftest v1 round-trip, local backups, and the video-root-change prompt with [Copy] / [Start empty]. *Note: superseded by user decision: ledger in `<videoRoot>\.uas-sort` (the `ledgerDir`-change warning became the video-root-change prompt).* |
| 22 | safety | major | Fixed-offset clock breaks under DST; CheckDate window too small | **Applied** | §6.1 zone learner used for items, library members and the watermark; §7.3 rule 5 near-watermark ±75 min; §6.5 CheckDate 75 min for `Mvhd`/`DroneClock*` |
| 23 | safety | major | Scenario D appends next-day footage to yesterday's folder as High, no preflight warning | **Applied** | §8.5 New-subset judgement → Medium + hint; UserSplit rule so a split gives NewFolder; Medium appends and emphasised splits need a preflight acknowledgement; golden test |
| 24 | safety | major | Rename/Retarget pins silently apply to changed membership; merge drops a pin | **Applied** | §3 `PinnedMembers`; §8.9 membership Warning with [Keep]/[Reset] (preflight acknowledgement) and conflicting pins → Blocking |
| 25 | safety | major | Bulk "Mark as not needed" can permanently hide footage | **Applied** | §10.5 nothing preselected, confirmation with counts and GB, no bulk for videos, Dismissed section with Un-dismiss (`revoke`), undo on the page |
| 26 | safety | major | Read APIs not banned (duplicate of #1) | **Applied** | §2.4 (merged with #1 and #41); probes accept Streams only |
| 27 | safety | major | Browse into library/sync roots; placeholder attribute visibility unproven | **Applied (mechanism adjusted)** | §4.3 sync-root detection plus canonical paths; placeholder compatibility call at startup. `--selftest` requires **any** cloud-only entry in the video root to show 0x400000/0x1000, rather than one named Kodiak file that could get hydrated, with an explicit override |
| 28 | safety | major | Audit per unit undefined for multi-file units, twins and truncated matches | **Applied** | §10.5 per-file lines, worst-of per unit, twin lines (AssumedByRule or SkippedByRule), truncated name+size cap; table tests |
| 29 | safety | minor | `nameSize` records counted as InLedger; headline doesn't separate them | **Applied** | §10.5 mapping to NameSizeMatch; the headline shows the "matched by name+size only" count |
| 30 | safety | minor | Set identity by name+size only; empty folder treated as Resume | **Applied** | §8.8 mtime ±2 s, ledger first-frame check, empty folder → Plain |
| 31 | safety | minor | Preflight doesn't re-check that Append targets still exist | **Applied** | §10.2 Blocking `AppendTargetGone` "folder renamed or moved since the scan; rescan"; §10.3 step 3 (`EnsureDirectory` creates only NewFolder paths, never an Append target); fault test (§13) |
| 32 | safety | minor | Generic lat/lon search can accept attitude/velocity fields | **Applied** | §6.3 step 5b plausibility gate, `GpsGuessed` flag, else `NoFix` |
| 33 | safety | minor | `Etc/*` fallback depends on clustering, so dates move with R | **Applied** | §6.4 nearest land-zone GPS item on the whole card; invariant test "local dates never change with R/G" |
| 34 | safety | minor | exFAT last-access writes on read | **Applied** | M4 and acceptance check; documented in the verdict text if it happens; write-protect badge; no `SetFileTime` suppression |
| 35 | safety | minor | P/Invoke paths lack long-path handling; temp path not length-checked | **Applied** | `\\?\` prefix on every P/Invoke path, 400-character check on the temp path, 300-character integration test |
| 36 | safety | minor | Folding hides Conflict/Truncated members | **Applied** | §8.5 fold rule; §9.3 folds only clean groups; test |
| 37 | safety | minor | Corrupt settings silently revert the photo root | **Applied** | §11 recovery → Blocking until roots confirmed; roots recorded in `run` records and offered back |
| 38 | feasibility | major | 22 days unrealistic; first real offload only at the end; SDK bumps mid-build | **Applied** | §14 re-baseline (≈ 38 days), Commit UI (M7) before Map (M8) so a real offload is possible around day 28, SDK bumps scheduled when they ship, VS Code union/closed check in M0 |
| 39 | feasibility | major | Trimming breaks property-path features (AutoSuggestBox ToString in folder names); ItemsSource types untested | **Applied** | §2.4 XAML lint bans property paths; `SuggestionVm` + `UpdateTextOnSelect=False`; ItemsSource rule (§2.7 #4); selftest realises each template and asserts text |
| 40 | feasibility | major | ListView can't make chip or split rows non-selectable; Space conflicts | **Applied (different mechanism)** | `ItemContainer.CanUserSelect` is documented only in the 2.0-experimental view (checked 2026-09-27), so chips and day-split banners are embedded in the following item's template instead. Every item is selectable, and buttons stay live (§9.3, §9.5). Space is list-scoped (§9.12) |
| 41 | feasibility | major | BannedApiAnalyzers can't express "File.* writes"; many write paths are open | **Applied** | §2.4 whole-type bans incl. WinRT storage, VB FileSystem and Process.Start; member list and pragmas in Platform; make-fake-card moved to Testing; `DownloadStarting` cancelled; probe build |
| 42 | feasibility | major | Selftest gate can't fail or can hang; single-instance details missing | **Applied** | §13 selftest isolation, `Environment.Exit(ok?0:1)` + result JSON + 60 s timeout; §4.4 custom Main, worker-thread redirect, `AllowSetForegroundWindow`; mutex fallback tested in M0; offload lock |
| 43 | feasibility | minor | Map JSON needs `type` first, but map.js sends `v` first; unknown types throw | **Applied** | §9.6 `AllowOutOfOrderMetadataProperties`, try/catch with logging, golden test with `v` first, GeoPoint converter |
| 44 | feasibility | minor | `.mjs` MIME under the virtual host is unverified | **Applied** | §9.6: M8 first task plus ordered fallbacks (rename to `.js` + `setWorkerUrl`, WebResourceRequested with explicit Content-Type, Leaflet); selftest waits for `ready` |
| 45 | feasibility | minor | Win32 details: write-through meaningless, AllowUnsafeBlocks, CreateFileW unnecessary, noisy handle probe | **Applied (flag kept)** | `AllowUnsafeBlocks`; `File.OpenHandle` + NO_BUFFERING; deterministic FileShare.None lister test; criterion 1 reworded; write-through test and claim dropped. The flag stays because the approved Safety graft names it (harmless) |
| 46 | feasibility | minor | exFAT last-access may write to the card (duplicate of #34) | **Applied** | M4 checkpoint and acceptance; write-protect detection |
| 47 | feasibility | minor | TreatWarningsAsErrors vs trim warnings from rooted assemblies; AnalysisLevel drift | **Applied** | §2.5 targeted `NoWarn IL2104` only if M0 shows it; `AnalysisLevel` pinned; 0.5 day per SDK bump |
| 48 | feasibility | minor | Lean package combination and cross-assembly `closed`/`union` untested | **Applied** | M0 exit build of the exact set; Review.Tests exhaustive-switch and JSON round-trip tests |
| 49 | feasibility | minor | Slider has no drag-completed event; UI-thread derive may stutter | **Applied** | §9.7 `AddHandler(PointerReleased, handledEventsToo)` + `PointerCaptureLost`, 400 ms idle commit, background derive with latest-wins; FakeTimeProvider test |
| 50 | feasibility | minor | Unmodified-key accelerators collide with list and TextBox input | **Applied** | §9.12 page accelerators use modified keys only; list-scoped keys; TextBox check for undo; smoke test |
| 51 | feasibility | minor | Thumbnails need images without WinUI types in VMs; recycled containers | **Applied** | §9.5 `ThumbKey` + `Thumb.Key` attached property with key re-check; `IThumbnailSource` returns bytes |
| 52 | feasibility | minor | TeachingTip hover, ContentDialog settings, nested dialogs, MenuFlyout binding, Browse restriction | **Applied** | Hover preview cut to Later (a ToolTip would be used if restored); Settings as a Page; queued `IDialogService`; flyout built on Opening; Browse result validated with an InfoBar |
| 53 | feasibility | minor | `pwsh` not installed; execution policy | **Applied** | PowerShell 7 chosen (most modern) as a one-time user install, approved 2026-09-27; all scripts run via `pwsh -NoProfile -ExecutionPolicy Bypass -File` (§2.6, §15 Q7) |