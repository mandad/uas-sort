# uas-sort

A small Windows desktop app that offloads a drone's SD card into an organised library: videos into dated event folders, photos into a Lightroom import folder. It proposes how clips should be grouped (by date **and** GPS location), lets you review and rename everything, then copies with verification and tells you whether the card is safe to format.

Built for the DJI Air 3S workflow, but the rules are general enough for other DJI Fly-era drones.

> **Status: initial version built; the first-real-card acceptance is next.**
> The approved design spec lives in [`docs/superpowers/specs/`](docs/superpowers/specs/) and the implementation plan in [`docs/superpowers/plans/`](docs/superpowers/plans/). The initial, feature-complete version was built in one pass (see [Build approach](#build-approach)); the user's acceptance on a real card follows (dry run, rehearsal, real offload, card cleanup).

## What it does

- **Finds what's new on the card.** Videos are compared against the existing library by name and size (listings only — it never opens library files, so OneDrive cloud-only placeholders are never downloaded). An append-only ledger remembers everything the app has offloaded, including photos that Lightroom has since moved away.
- **Groups videos into events.** Clips are clustered by site-local date and GPS:
  - consecutive days join (a multi-day trip lands in one folder);
  - a clip more than **50 miles** (configurable) from its group's centre starts a new group, even on the same day;
  - existing library folders are never merged.
  Every split is explained ("34 mi jump · 21 h", "62 days") and reversible with one click.
- **Proposes destinations.** Each group becomes `<videoRoot>\YYYY\YYYY-MM\YYYY-MM-DD <Description>\`, or an append to an existing folder when dates and location line up. Descriptions are suggested from previous folders and an offline GeoNames place index ("Hidden Lake · feature · 0.4 mi").
- **Routes photos.** DNG (and JPG twins) go flat into the photo root for Lightroom import. Panorama and hyperlapse source sets keep their set subfolder (`001_0087`), with a date suffix if the name is already taken.
- **Copies safely.** Each file is copied to a hidden temp file while hashing, flushed, re-read unbuffered and compared, and only then renamed into place — never overwriting anything. Offloading never writes to the card.
- **Gives an honest verdict.** *Safe to format* / *Safe, with assumptions* / *Don't format yet*, backed by a per-file audit of every file on the card.
- **Optionally cleans up the card.** A separate, explicitly confirmed action deletes the oldest offloaded files — those captured before a date you pick, or just enough of the oldest to free up (or leave) a given amount of space — and always shows the resulting cutoff date first. By default only files proven to be in your library are eligible (verified copies or name + size matches); anything else goes only after you review it. That is how you weed out clips that aren't in the library (bad, unnecessary or accidental footage): each one is listed with its date, clip length and location before anything is deleted. It works only on a detected drone card, never on a browsed folder or a backup drive.

## How it works

```
card ─► classify files ─► read metadata ─► normalise time & place ─► decide what's new
      (DCIM/DJI_###,      (~2 KB per MP4:   (drone clock ► UTC,       (library listing
       PANORAMA, …)        UTC time, GPS,    GPS ► time zone ►          + ledger)
                           thumbnail)        local date)
                                                                            │
   verdict ◄─ audit ◄─ copy + verify ◄─ preflight ◄─ review & edit ◄─ group & propose
```

A few things the design had to get right:

- **Drone clock ≠ local time.** DJI filenames and DNG timestamps use the remote controller's clock (which may be set to a different time zone than where you fly), while MP4 `creation_time` is true UTC. The app learns the drone-clock time zone from the videos on each card, converts everything to UTC, then derives the local date from the GPS position's time zone — so an evening flight doesn't land in tomorrow's folder.
- **Fast GPS.** DJI MP4s store per-frame GPS in a protobuf telemetry track (`djmd`). Instead of scanning the whole file (ExifTool takes 7–87 s per clip), a custom reader decodes just the first valid sample: ~2 KB read, a few milliseconds per clip. It even recovers GPS from unfinished recordings that have no `moov` box.
- **Safety enforced in code.** All file-system access goes through one guarded I/O layer; direct file APIs elsewhere fail the build (Roslyn banned-API analyzer), and tests run against a fake file system that throws if anything touches a cloud-only placeholder.

## Tech stack

| Layer | Choice |
|---|---|
| Runtime / language | .NET 11, C# 15 |
| UI | WinUI 3 on the Windows App SDK 2.5 (Mica, TitleBar, ItemsView) |
| MVVM | CommunityToolkit.Mvvm |
| Map | WebView2 + MapLibre GL JS (vendored); OpenFreeMap streets, Esri / USGS satellite imagery |
| Metadata | Custom MP4 box + protobuf reader for videos; MetadataExtractor for DNG/JPG |
| Time zones | GeoTimeZone (offline lat/lon ► IANA) + .NET `TimeZoneInfo` |
| Hashing | System.IO.Hashing (XxHash128) |
| Tests | xUnit v3 on Microsoft.Testing.Platform |
| Packaging | Unpackaged, self-contained Native AOT folder with a Start-menu shortcut |

## Requirements

- Windows 11 (24H2 or later), x64 only
- To build (install once):
  - .NET 11 SDK: `winget install Microsoft.DotNet.SDK.Preview` (11.0.100-rc.1 for now; the GA SDK from Nov 10)
  - PowerShell 7: `winget install Microsoft.PowerShell`
  - C++ build tools for Native AOT (the Visual Studio Build Tools 2022 C++ workload):
    ```powershell
    & "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vs_installer.exe" modify --installPath "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools" --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --passive
    ```
  - Optional: VS Code + C# Dev Kit (`winget install Microsoft.VisualStudioCode`), or Visual Studio 2026 Insiders for the XAML designer and Hot Reload
- To run: nothing extra — the published app is self-contained (native code plus its own Windows App SDK runtime), and WebView2 ships with Windows 11

## Build and test

Run everything from the repository root (`C:\dev\uas-sort`) in PowerShell 7 on Windows; `global.json` pins the SDK, and warnings are errors in every project.

```powershell
dotnet build uas-sort.slnx                          # the first restore takes ~8-13 min (~1.5-2 GB of NuGet packages)
dotnet test --solution uas-sort.slnx                # the whole suite (xUnit v3 on Microsoft.Testing.Platform)
dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Zachar*"   # one project, narrowed
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -CheckBannedApi   # every banned file-system call must fail the build
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.tests.ps1            # checks for the deploy script
```

Optional, only if you drive the build from WSL instead: run the same commands through `tools/r.sh` (it calls the Windows `dotnet.exe` and `pwsh.exe` with the working directory set to the repository), then `dotnet build-server shutdown` so the compiler server releases its file locks.

## Install

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1 -Version 0.1.0
```

`deploy.ps1` publishes the app as Native AOT for `win-x64` (uas-sort is built for x64 PCs only) and runs the published `uas-sort.exe --selftest` twice: both runs must pass, and the second (warm) run must reach its first frame within 1 s. Only then does it copy the build to `%LOCALAPPDATA%\Programs\uas-sort\<version>\`, point the Start-menu shortcut **uas-sort** at it, and delete all but the two newest versions. It never touches settings, drafts, reports, logs or any ledger.

- Use a higher `-Version` for every install (`-Force` replaces an installed version with the same number).
- If your video root has no cloud-only files, the selftest's placeholder check reports "not applicable"; add `-AllowNoPlaceholders` only in that case.
- Per-PC state lives in `%LOCALAPPDATA%\uas-sort\` (settings, drafts, reports, logs and the local ledger backup). The shared offload history lives in `<videoRoot>\.uas-sort\`.
- To uninstall, delete `%LOCALAPPDATA%\Programs\uas-sort\` and the Start-menu shortcut. Keep `<videoRoot>\.uas-sort\`: it is the offload history for every PC.
- Native AOT notes (both approved by the user): the App project sets `AllowUnsafeBlocks` and `CsWinRTAotWarningLevel` 2, because CsWinRT generates its AOT interop code only with unsafe blocks allowed (hand-written App code never uses `unsafe`), and level 2 turns CsWinRT's AOT diagnostics into warnings, and so into errors. The main window creates its own `OverlappedPresenter` and keeps a typed reference to it (no cast of `AppWindow.Presenter`, which fails under AOT), so the minimum window size holds in the published build.

## Dry run (CLI)

```powershell
dotnet run --project src/UasSort.Cli -- plan --card E:\ --json
dotnet run --project src/UasSort.Cli -- plan --card E:\ --expect tests\acceptance\first-card-expected.json
```

`uas-sort-cli plan` scans a card and prints the plan the app would propose. It writes nothing, anywhere (no drafts, no ledger, no files; logs go to stderr) and has no cleanup command. Roots and tuning come from the app's settings, or from `--video-root`, `--photo-root`, `--radius-mi` (5-100), `--gap-days` (0-7) and `--settings <file>`. Exit codes: 0 = plan printed (even with blocking issues), 1 = the card source was refused (the reason is on stderr), 2 = any other error. `--expect` compares the plan with your expected folder list and prints the differences and the edit count; see [`tests/acceptance/README.md`](tests/acceptance/README.md).

## First-real-card acceptance

After the build is complete, the first real card is the user's acceptance of the app (design reference §13). With the card in the reader (here `E:\`):

1. Save a listing snapshot of the card first (relative path, size, modified, created and last-access times, attributes).
2. Write `tests\acceptance\first-card-expected.json` from `first-card-expected.example.json` **before** the dry run (see [`tests/acceptance/README.md`](tests/acceptance/README.md)), then run `dotnet run --project src/UasSort.Cli -- plan --card E:\ --json --expect tests\acceptance\first-card-expected.json`. It passes at 2 edits or fewer; note the scan + plan time on stderr. Re-list the card and compare with the snapshot.
3. Rehearse: in the app, point the roots at `%TEMP%\uas-sort-rehearsal\video` and `%TEMP%\uas-sort-rehearsal\photo` and offload. Check the verdict and the report, that OneDrive doesn't sync the `.tmp` files, the unbuffered verify and post-run flush on a scratch folder on the exFAT `D:` drive, and [Eject D:].
4. Real offload: point the roots back at the real library, answer **[Start empty]** at the ledger prompt (so rehearsal records never enter the real ledger), offload, then rescan the card: everything shows Imported and the verdict is Safe.
5. Card cleanup: snapshot the card listing, run Before date with a cutoff that takes exactly one old eligible clip (and its companions), confirm, then re-list: only that clip's files are gone.

## Maintenance

The SDK is pinned in `global.json` (`11.0.100-rc.1.26425.128`). When .NET 11 RC2 (~Oct 13) and GA (Nov 10) ship: install the new SDK, set its exact version in `global.json` (at GA also `"allowPrerelease": false`), move `System.IO.Hashing` in `Directory.Packages.props` to the matching version, then re-run the build, the tests, the banned-API check and `deploy.ps1` (which repeats the Native AOT publish and the selftest gate). `AnalysisLevel` stays pinned, so new analyzer rules don't appear by surprise.

## Repository layout

```
src/
  UasSort.Core/        UI-free logic: model, probes, time and place, library index, planning, offload, cleanup
  UasSort.Platform/    the only code that touches disk, Win32 or the shell
  UasSort.Review/      view models (no WinUI types)
  UasSort.App/         WinUI 3 app (uas-sort.exe), map pane, --selftest
  UasSort.Cli/         uas-sort-cli plan (dry run)
tests/                 unit, integration and view-model tests; acceptance/ holds the first-card expectations
tools/                 build.ps1, deploy.ps1, r.sh (optional, WSL only), place-index and fixture builders
docs/
  superpowers/specs/
    2026-09-27-uas-sort-design.md             # main design spec — start here
    2026-09-27-uas-sort-design-reference.md   # detailed companion (types, rules, tests)
  superpowers/plans/
    2026-09-27-uas-sort.md                    # implementation plan (index of the build parts)
  research/
    01-card-layout.md                         # DJI Air 3S card layout and file taxonomy
    02-djmd-gps-calibration.md                # first-sample djmd GPS reader + calibration
    03-dotnet-libraries.md                    # metadata, time zone, hashing libraries
    04-grouping-algorithm.md                  # clustering and newness rules
    05-approach-judging.md                    # three candidate architectures, scored
    06-modern-stack.md                        # .NET 11 / WinUI 3 / packaging measurements
    07-map-options.md                         # map control and imagery options
    08-winui-spike.md                         # WinUI 3 + WebView2 build spike
    spikes/                                   # throwaway prototype code kept for porting
                                              # (djmd GPS reader, grouping algorithm, WinUI/map spikes)
```

The research uses real flights from the author's library (Alaska and Rhode Island) as calibration data.

Dependencies point inwards: App → Review → Core ← Platform, and the CLI uses Platform and Core only. Direct file-system APIs outside Platform fail the build.

## Build approach

The initial version is built complete, in one pass, by an AI coding agent — not progressively or in milestones. Once the prerequisites above are installed, the parts are built in dependency order, with each part's tests written alongside it:

1. Stack proof: solution skeleton, pinned packages, banned-API analyzer; a probe app that publishes as Native AOT and passes its selftest
2. Core model, I/O ports and the guard policy (plus a fake file system with tripwires)
3. Media probes: MP4 time/GPS/clip length, DNG metadata, thumbnails
4. Time and place: drone-clock learning, site time zones, clock-mismatch flag, offline GeoNames place index
5. Library index, ledger and settings
6. Planning and editing: newness, grouping, day splits, folder decisions, naming, sets, undo-able edits
7. Offload engine: copy/verify/rename, audit, verdict
8. Card cleanup: planner and executor (delete by date or to reach a free-space target; weed out clips not in the library after a listing)
9. Windows platform layer: listing, card reader, card eraser, guarded file ops, ledger store
10. View models
11. WinUI app: setup, review screens, map pane, commit flow, cleanup, settings
12. `uas-sort-cli plan` dry run
13. Native AOT publish, `deploy.ps1` and the `--selftest` gate

It is complete when the whole solution builds with warnings as errors, the full test suite passes (unit, golden replay, synthetic media, fake-file-system tripwire, fault injection, Windows integration, view models), the banned-API probe trips every expected analyzer error, and the Native AOT build passes `--selftest`. A dry run, rehearsal, real offload and card cleanup on a real card are then the user's acceptance of the finished app.

## Non-goals

No tray icon or auto-launch (you open it when you want to offload), no formatting or repairing of the card (card files are deleted only through the optional, explicitly confirmed card cleanup; offloading never touches the card), no editing or reorganising of existing library files, no Lightroom catalog access, no video playback or transcoding.

## License

[MIT](LICENSE) © 2026 Damian Manda

## Acknowledgements

Planned data and libraries: [GeoNames](https://www.geonames.org/) (CC BY 4.0), [OpenFreeMap](https://openfreemap.org/) / © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors, Esri World Imagery, USGS The National Map, [MapLibre GL JS](https://maplibre.org/) (BSD-3-Clause), [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) (Apache-2.0), [GeoTimeZone](https://github.com/mattjohnsonpint/GeoTimeZone) (MIT). The `djmd` field layout was worked out with reference to [ExifTool](https://exiftool.org/)'s DJI module.
