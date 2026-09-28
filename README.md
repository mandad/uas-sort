# uas-sort

A small Windows desktop app that offloads a drone's SD card into an organised library: videos into dated event folders, photos into a Lightroom import folder. It proposes how clips should be grouped (by date **and** GPS location), lets you review and rename everything, then copies with verification and tells you whether the card is safe to format.

Built for the DJI Air 3S workflow, but the rules are general enough for other DJI Fly-era drones.

> **Status: design complete, implementation not started.**
> The approved design spec lives in [`docs/superpowers/specs/`](docs/superpowers/specs/). Code arrives milestone by milestone (see [Roadmap](#roadmap)).

## What it does

- **Finds what's new on the card.** Videos are compared against the existing library by name and size (listings only — it never opens library files, so OneDrive cloud-only placeholders are never downloaded). An append-only ledger remembers everything the app has offloaded, including photos that Lightroom has since moved away.
- **Groups videos into events.** Clips are clustered by site-local date and GPS:
  - consecutive days join (a multi-day trip lands in one folder);
  - a clip more than **50 miles** (configurable) from its group's centre starts a new group, even on the same day;
  - existing library folders are never merged.
  Every split is explained ("34 mi jump · 21 h", "62 days") and reversible with one click.
- **Proposes destinations.** Each group becomes `<videoRoot>\YYYY\YYYY-MM\YYYY-MM-DD <Description>\`, or an append to an existing folder when dates and location line up. Descriptions are suggested from previous folders and an offline GeoNames place index ("Hidden Lake · feature · 0.4 mi").
- **Routes photos.** DNG (and JPG twins) go flat into the photo root for Lightroom import. Panorama and hyperlapse source sets keep their set subfolder (`001_0087`), with a date suffix if the name is already taken.
- **Copies safely.** Each file is copied to a hidden temp file while hashing, flushed, re-read unbuffered and compared, and only then renamed into place — never overwriting anything. The card is only ever read.
- **Gives an honest verdict.** *Safe to format* / *Safe, with assumptions* / *Don't format yet*, backed by a per-file audit of every file on the card.

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
| Packaging | Unpackaged, self-contained, trimmed + ReadyToRun folder with a Start-menu shortcut |

## Requirements (planned)

- Windows 11 (24H2 or later), x64 (ARM64 builds are produced but untested)
- To build: .NET 11 SDK and PowerShell 7 (`winget install Microsoft.DotNet.SDK.Preview`, `winget install Microsoft.PowerShell`)
- To run: nothing extra — the published app carries its own .NET and Windows App SDK runtimes

Build and run instructions will be added with the first code milestone (M0).

## Repository layout

```
docs/
  superpowers/specs/
    2026-09-27-uas-sort-design.md             # main design spec — start here
    2026-09-27-uas-sort-design-reference.md   # detailed companion (types, rules, tests)
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

Planned source layout (from the spec): `src/UasSort.Core` (UI-free logic), `src/UasSort.Platform` (the only code that touches disk/Win32), `src/UasSort.Review` (view models), `src/UasSort.App` (WinUI shell), `src/UasSort.Cli` (dry-run planner), plus matching test projects.

## Roadmap

| Milestone | Scope |
|---|---|
| M0 | Solution skeleton; prove the exact package set builds, trims and runs |
| M1 | Card detection, file classification, MP4/DNG metadata probes |
| M2 | Drone-clock learning, time zones, library index, ledger |
| M3 | Newness, grouping, folder decisions, edit model; `uas-sort-cli plan` dry run |
| M4 | Checkpoint: dry run against a real card |
| M5 | Offload engine: copy/verify/rename, audit, verdict |
| M6–M7 | WinUI review screens and commit flow — first real offload from the app |
| M8 | Map pane |
| M9 | Offline place-name suggestions |
| M10 | Packaging and acceptance |

## Non-goals

No tray icon or auto-launch (you open it when you want to offload), no changes to the card (no deleting or formatting), no editing or reorganising of existing library files, no Lightroom catalog access, no video playback or transcoding.

## License

[MIT](LICENSE) © 2026 Damian Manda

## Acknowledgements

Planned data and libraries: [GeoNames](https://www.geonames.org/) (CC BY 4.0), [OpenFreeMap](https://openfreemap.org/) / © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors, Esri World Imagery, USGS The National Map, [MapLibre GL JS](https://maplibre.org/) (BSD-3-Clause), [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) (Apache-2.0), [GeoTimeZone](https://github.com/mattjohnsonpint/GeoTimeZone) (MIT). The `djmd` field layout was worked out with reference to [ExifTool](https://exiftool.org/)'s DJI module.
