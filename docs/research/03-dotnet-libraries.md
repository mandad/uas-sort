# .NET technology choices for uas-sort (checked 2026-09-27)

**Recommendation:** use .NET 10 LTS with WPF, CommunityToolkit.Mvvm and MetadataExtractor. Add GeoTimeZone for time zones, with the time zone support built into Windows. Use an offline extract of GeoNames place names to suggest folder names, System.IO.Hashing XxHash128 to check copies, and JSON / JSON Lines files for settings and the ledger. You don't need TimeZoneConverter, SQLite, ExifTool, WinUI or Avalonia.

Most of this was confirmed by a spike: I built it with a .NET 10 SDK installed only in the scratchpad and ran it on Linux and on this Windows 11 machine. Anything not tested is marked UNVERIFIED.

## Recommended stack

| Package / component | Version (2026-09) | Purpose | License |
|---|---|---|---|
| .NET SDK / runtime | **SDK 10.0.401, runtime 10.0.12** (2026-09-08) | Target framework `net10.0-windows` for the app, `net10.0` for the core library | MIT |
| WPF (part of Microsoft.WindowsDesktop.App) | ships with .NET 10 | UI: built-in DataGrid, TreeView and GridView; dark mode via `ThemeMode="System"` | MIT |
| CommunityToolkit.Mvvm | **8.4.2** (2026-03-25) | Source-generated `[ObservableProperty]` and `[RelayCommand]` | MIT |
| MetadataExtractor (pulls in XmpCore) | **2.9.3** (2026-04-08) | DNG/JPG EXIF (DateTimeOriginal, GPS, Model) and the MP4 creation time (`mvhd` box) | Apache-2.0 |
| GeoTimeZone | **6.1.0** (2025-10-31), boundary data 2025b | Latitude/longitude → IANA time zone ID, offline | MIT (boundary data comes from OpenStreetMap) |
| .NET built-in TimeZoneInfo (uses Windows' ICU library) | built in | Converts IANA IDs to `TimeZoneInfo` on Windows 11; no extra package | — |
| System.IO.Hashing | **10.0.12** (2026-09-08) | XxHash128 / XxHash3 to check copies | MIT |
| System.Text.Json | built in | `settings.json` and an append-only `ledger.jsonl` in `%LOCALAPPDATA%\uas-sort\` | MIT |
| GeoNames extract (a data file, not a package) | daily dump 2026-09-27 | Offline nearest-place names to suggest folder descriptions | CC-BY 4.0 (credit required) |
| Optional: Nominatim (online) | — | Extra "road name" suggestion when online, cached | Data ODbL; usage policy applies |
| Optional: WPF-UI | 4.3.0 (2026-05-04) | Only if the built-in Fluent styling isn't enough | MIT |
| Not needed | TimeZoneConverter 7.2.0, Microsoft.Data.Sqlite 10.0.12, ExifTool 13.59, Windows App SDK 2.5.1, Avalonia 12.1.3 | — | — |

## 1. Target framework: .NET 10 LTS

- **Support dates** (Microsoft support-policy page): .NET 8 (LTS) and .NET 9 (STS) **both end support on 2026-11-10**, 44 days from today. **.NET 10 (LTS, released 2025-11-11) is supported until 2028-11-14.** .NET 11 RC1 came out on 2026-09-08; it's a short-term release.
- **What this machine actually has differs from the project context.**
  - The only Windows SDK is **9.0.316**. There is no 8.0 SDK.
  - The runtimes are 8.0.29 and 9.0.18, both behind the September patches (8.0.31 and 9.0.20).
  - Visual Studio 2022 is installed as **BuildTools only**, plus VS Code and winget 1.29.
- **Installing the .NET 10 SDK is worth it.**
  - Starting on 8 or 9 means migrating within weeks.
  - The `[ObservableProperty] public partial string X {get;set;}` style compiled cleanly with .NET 10's default C# 14.
  - Suggested install: `winget install Microsoft.DotNet.SDK.10`. The winget ID is UNVERIFIED.
- **Visual Studio caveat:** VS 2022 17.14 gives NETSDK1233 ("Targeting .NET 10.0 ... in Visual Studio 2022 17.14 is not supported"). Use the dotnet CLI, VS Code or VS 2026.
- **Checked in this session:** I installed SDK 10.0.401 on Linux inside the scratchpad only (`.../spikes/dotnet-stack/dotnet10`). It restores, builds and runs the core libraries in WSL. It can also **publish the WPF app for win-x64** with `EnableWindowsTargeting=true`.

## 2. UI framework: WPF

| | WPF (.NET 10) | WinUI 3 (Windows App SDK 2.5.1) | WinForms (.NET 10) | Avalonia 12.1.3 |
|---|---|---|---|---|
| Editable grid / tree | Built-in DataGrid, TreeView, ListView+GridView, free | No built-in DataGrid | DataGridView | **DataGrid deprecated** ("only receives bug fixes"); **TreeDataGrid is now commercial** (Avalonia Accelerate); the new free "Table" control is read-only |
| MVVM / testability | Strong binding; works well with CommunityToolkit.Mvvm | Strong | Weak | Strong |
| Single-file exe | Yes (sizes measured below) | **No.** Microsoft's docs (2026-09-11): "cannot produce a single-file EXE for WinUI 3"; self-contained adds about 200 MB | Yes | Yes |
| Dark mode | `ThemeMode="System"` in XAML compiles with no WPF0001 error. Setting ThemeMode from code is still experimental in .NET 10. Microsoft says Fluent styling is "still in progress" | Native | `Application.SetColorMode` is **no longer experimental in .NET 10** | Yes |

**Recommendation: WPF.** Put all logic in a UI-free `UasSort.Core` library targeting `net10.0`, so its tests also run in WSL. I confirmed MetadataExtractor, GeoTimeZone and the hashing all run on Linux .NET 10 against `/mnt/c` files.

One exception: on Linux, `FileInfo.Attributes` doesn't show OneDrive's placeholder bits (it read 0x80 on Linux and 0x20 on Windows). The placeholder check therefore has to be tested on Windows, or behind an interface. WinForms is a reasonable fallback. I'd avoid WinUI 3 (no single-file exe) and Avalonia (no free editable grid).

**Package size, measured** (WPF + DataGrid + MVVM Toolkit + MetadataExtractor + GeoTimeZone + hashing, win-x64):
- Framework-dependent single-file: **4.9 MB** (GeoTimeZone accounts for 3.5 MB). Needs the .NET 10 Desktop Runtime installed. This is the best fit for a personal tool.
- Self-contained single-file with `IncludeNativeLibrariesForSelfExtract`: **144.4 MB**, or **68.8 MB** with `EnableCompressionInSingleFile`. Trimming isn't supported for WPF. With self-extract, native files unpack to `%TEMP%\.net`.
- I didn't launch the GUI (to avoid opening a window on your desktop), so startup time is UNVERIFIED.

## 3. Metadata: MetadataExtractor 2.9.3 works (spike, Windows and Linux)

**PANO_0001.DNG (13.75 MB):**
- Make=DJI, Model=FC9113.
- GPS via `GpsDirectory.TryGetGeoLocation` = 57.799648, -152.390180; altitude 143.67 m; no GPS date or time.
- It read only 94 KB (0.69% of the file).

**Gotcha with DNG files:** there are three `ExifSubIfdDirectory` entries.
- `OfType<ExifSubIfdDirectory>().FirstOrDefault()` returns the **raw-image sub-IFD** ("Full-resolution image", 4096 px), which has **no DateTimeOriginal**.
- The value (2026-05-25 09:30:28, Kind=Unspecified, no OffsetTimeOriginal) is in the third one. IFD0 `DateTime` holds the same value.
- Use `dirs.OfType<ExifDirectoryBase>().FirstOrDefault(d => d.ContainsTag(ExifDirectoryBase.TagDateTimeOriginal))`.

**Zachar Bay DJI_20260927140627_0128_D.MP4 (89.6 MB):**
- `QuickTimeMovieHeaderDirectory.TagCreated` = 2026-09-27T18:06:27, Kind=**Unspecified**. Apply `DateTime.SpecifyKind(..., Utc)`.
- It read **566 bytes in 40 seeks**, taking 10–28 ms, and skips the video data, so it's fine on an SD card.
- It exposes **no GPS and no djmd track** (only three track headers), so the planned custom djmd first-sample parser is still needed.

**Autel MAX_0061.MP4:** Created = 2022-03-27 07:03:57. This is local time labelled as UTC; apply the Autel rule.

**ExifTool:** 13.59 (2026-05-27). The Windows zip is 11.2 MB: the exe plus an `exiftool_files` folder containing Perl. License, from the local README: "same terms as Perl itself (either the Perl Artistic License or GPL)". It runs as a separate process per call (or needs `-stay_open`), and `-ee3` is slow. Don't bundle it; keep it as a developer and diagnostic tool.

## 4. Latitude/longitude → time zone: GeoTimeZone 6.1.0 plus built-in TimeZoneInfo

**Cost, measured on Windows:**
- The DLL is 3.5 MB.
- The first lookup loads the data lazily: 312–425 ms, and +16 MB managed heap.
- After that, 12 lookups plus conversions took 27.7 ms in total.

**Lookup results (Windows 11 build 26200, .NET 10.0.12).** The test time was 2026-09-27T18:06:27Z.

| Point | GeoTimeZone result | Local time |
|---|---|---|
| Zachar Bay / Kodiak | America/Anchorage | 10:06 |
| Anvil Mountain (Nome) | America/Nome | 10:06 |
| Newport RI | America/New_York | 14:06 |
| Makaha HI | Pacific/Honolulu | 08:06 |
| Adak | America/Adak | 09:06 |
| Gulf of Alaska, open ocean | **Etc/GMT+10** | 08:06 |
| Bering Sea off Nome | **Etc/GMT+11** | 07:06 |
| Stewart BC / Hyder (approximate coordinates) | **America/Sitka**, with alternative America/Vancouver | 1 hour off for Stewart |
| Lake Erie | America/New_York, with alternative America/Toronto | — |

**Built-in TimeZoneInfo works on Windows with IANA IDs**, so TimeZoneConverter isn't needed.
- `FindSystemTimeZoneById` succeeded for every ID above, including `Etc/GMT+10`.
- `TryConvertIanaIdToWindowsId("America/Anchorage")` returned "Alaskan Standard Time".
- `TimeZoneInfo.Local.Id` is "Alaskan Standard Time".
- The drone clock zone `America/New_York` gives an offset of -4:00.
- Don't enable `InvariantGlobalization` or `UseNls`; either one breaks IANA lookups.

**Rules to build in:**
- If the result starts with `Etc/` (points over water, e.g. flights from a boat), use the group's land time zone, the nearest GeoNames place's time zone (GeoNames has a timezone column), or a default of America/Anchorage.
- If `AlternativeResults` isn't empty **and** local time is within about an hour of midnight, flag the group's date for review.

## 5. Suggesting folder names: offline GeoNames place and feature data, Nominatim optional

**GeoNames file sizes** (CC-BY 4.0, updated 2026-09-27): cities500.zip 13 MB (235,879 rows), cities1000 11 MB, cities5000 5.4 MB, cities15000 3.2 MB, US.zip 68 MB (2,241,404 rows).

**Tested against the first GPS fix of your real folders:**

| Folder | cities500 nearest | US features nearest | Nominatim zoom 10/14 | Nominatim zoom 17 |
|---|---|---|---|---|
| Zachar Bay | Womens Bay, **65 km** | **Zachar Bay** village 0.7 km; **Zachar Bay** bay 1.7 km | Kodiak Island Borough | Kodiak Island Borough |
| Kodiak (PANO) | **Kodiak** 1.5 km | Kodiak 1.5 km | Kodiak | — |
| Anvil Mountain | Nome 7 km | **Anvil Mountain** (mountain) **0.3 km** | Unorganized Borough | — |
| Council Road | Nome 58 km | East Fork 0.2 km | Unorganized Borough | **Nome-Council Road** |
| Nome Roads | Nome 16 km | Willow Creek 0.9 km | — | Bob Blodgett Highway |
| Safety Roadhouse (by name search) | — | "Safety Sound" (bay) at 64.49, -164.77 | — | — |

City-only lists fail in remote Alaska, and Nominatim at zoom 10/14 only returns the borough.

**Recommendation:** bundle a pre-filtered GeoNames extract.
- Keep populated places (all), terrain features (mountain, peak, hill, valley, pass, cape, island, peninsula, point), water features (bay, lake, glacier, fjord, cove, lagoon, inlet, sound, strait, harbor, falls) and parks.
- For the whole US that's **561,784 rows: 25.4 MB raw, 7.7 MB gzipped**. Alaska alone is 19,156 rows, **0.27 MB gzipped**.
- Add cities5000 or cities15000 as a worldwide fallback.
- Offer three kinds of suggestion: the nearest place within about 5 km, the nearest named feature within about 2 km, and "near <town with population over 1000>" within about 50 km.
- A simple precomputed lat/lon grid index is enough; no library needed.

**Optional online button:** Nominatim at zoom 17 is useful for road names.
- The policy requires at most 1 request per second, an identifying User-Agent, **mandatory caching**, visible ODbL credit, and no bulk or grid queries.
- One request per group, not per file, fits within that.
- This spike made 9 requests in total, spaced about 1.1 s apart.

## 6. Copy and verify (tested in `%TEMP%` on C:, then deleted)

**Hash speed** (Core Ultra 9 185H, in memory):

| Hash | Speed |
|---|---|
| XxHash128 | 9.2 GB/s |
| Crc64 | 8.8 GB/s |
| XxHash3 | 7.8 GB/s |
| XxHash64 | 6.7 GB/s |
| SHA-256 | 2.0 GB/s |
| MD5 | 0.6 GB/s |

Streaming XxHash3 over a local OneDrive file ran at 403 MB/s, limited by disk reads, not hashing. An SD card is far slower than any of these, so hashing is never the bottleneck. **Use XxHash128**; SHA-256 would also be fine if you want a standard hash in the ledger.

**The copy pattern works:**
1. Read the source once and hash while writing to `<name>.uas-sort.tmp`, opened with `FileStreamOptions{Mode=CreateNew, PreallocationSize=len}`.
2. Call `Flush(flushToDisk:true)`.
3. Copy `CreationTimeUtc` and `LastWriteTimeUtc` from the source.
4. `File.Move(tmp, final, overwrite:false)`. If the target exists this throws **IOException 0x800700B7**, so it never overwrites a file.
5. Re-read the destination with **FILE_FLAG_NO_BUFFERING** so the check doesn't just read Windows' cache: `(FileOptions)0x20000000` via `File.OpenHandle` plus `RandomAccess.Read` into a 4096-aligned `NativeMemory` buffer. This worked and the hash matched.

**Other copy findings:**
- `File.Copy` keeps LastWriteTime but sets **CreationTime to now** (tested). Set timestamps explicitly.
- OneDrive: Microsoft's docs say "Temporary TMP files will not be synced to OneDrive", so partial copies with a `.tmp` name shouldn't upload. I checked the docs only, not on your OneDrive.
- Free space: `new DriveInfo(root).AvailableFreeSpace` works (C: showed 317 GB free, D: 1,598 GB). Check the total per destination drive, plus a margin, before starting.
- .NET's `FileAttributes` enum has no named value for 0x400000 (RecallOnDataAccess). Test the raw bit mask `0x400000 | 0x40000 | 0x1000`.

## 7. Ledger and settings: JSON files, not SQLite

- Store `%LOCALAPPDATA%\uas-sort\settings.json` (write to a temp file, then replace) and an append-only `ledger.jsonl`, one line per verified file.
- **Measured with 10,000 rows:** 2.5 MB; writing took 463 ms (unoptimised); reading and building a `(path, size)` dictionary took 123 ms.
- An append-only file survives a crash mid-offload, is human-readable and needs no native DLLs.
- Microsoft.Data.Sqlite 10.0.12 would add a native SQLite DLL (SQLitePCLRaw 2.1.12, extracted at startup in a single-file build) and schema migrations. It's only worth it above roughly 100k rows or if you need ad-hoc queries.
- Keep the ledger out of OneDrive.

## 8. Detecting the card

**Tested on this machine:**
- `DriveInfo.GetDrives()` takes 3 ms.
- C: Fixed NTFS; **D: "PhotoEdits" reports Fixed, exFAT, 4.0 TB**.
- U:, X: and Z: are network drives, "not ready", about 1 ms each.
- No card was inserted.

**Rules:**
- Skip Network, CDRom and NoRootDirectory drives.
- For Removable or Fixed drives that are `IsReady`, treat them as a card only if they have `DCIM\DJI_*` (or `DCIM\MAX*`/Autel folders). Don't rely on DriveType.
- Exclude the drives holding the configured video and photo roots.
- Show the volume label, and provide Rescan and "Browse to folder…" buttons.

**Optional auto-refresh:** hook `HwndSource.AddHook` on the main window for `WM_DEVICECHANGE` (0x0219) with `DBT_DEVICEARRIVAL` (0x8000) or `DBT_DEVICEREMOVECOMPLETE` (0x8004), device type `DBT_DEVTYP_VOLUME` (2), and the `DBTF_MEDIA` flag when a card goes into an already-connected reader. Wait 1–2 s, then rescan. This comes from the Win32 docs and is UNVERIFIED (no card to test with).

## 9. Spike details and cleanup

- **Where:** `/tmp/claude-1000/-mnt-c-dev-uas-sort/187b6b83-e3e1-4971-8c9e-c13c3990d115/scratchpad/spikes/dotnet-stack/`
  - `meta/Program.cs`: metadata, time zone, hash, drive, copy and ledger tests.
  - `meta/pub-win/meta.exe`: a win-x64 self-contained build you can run from WSL with `meta|tz|hash|drives|copy|ledger`.
  - `wpfsize/`: the WPF size project.
  - `geonames/rg.py`, plus `cities500.zip` and `US.zip`.
  - `dotnet10/`: the Linux SDK 10.0.401. Use it with `DOTNET_ROOT`, `NUGET_PACKAGES` and `DOTNET_CLI_HOME` pointed inside the spike folder.
- **Files read:** only the locally-present files listed in the context. I checked the Windows attributes of the three July MP4s (0x20) before reading them.
- **ExifTool** was used only to get first GPS fixes for the reverse-geocoding test:

| File | First fix |
|---|---|
| Anvil Mountain `_0001` | 64.5626762, -165.3696378 |
| Nome Roads `_0110` | 64.5917112, -165.6725375 |
| Council Road `_0118` | 64.6946887, -164.2762760 |

- **Cleanup:** the copy test used `%TEMP%\uas-sort-spike-copy`, and the ledger test used a file `%TEMP%\uas-sort-spike-ledger.jsonl` (outside that folder, a small departure from the naming rule). Both were deleted, and `Get-ChildItem $env:TEMP -Filter uas-sort*` now returns nothing.
- Nothing was written to `/mnt/c/dev/uas-sort` (still empty), to OneDrive, to D: or to the Lightroom catalog.

## Risks

1. **.NET 8 and 9 end support on 2026-11-10.** The Windows machine currently has only SDK 9.0.316, so installing SDK 10 is a prerequisite.
2. **WPF's Fluent dark theme is still "in progress"**, and ThemeMode from code is experimental (WPF0001). Check early how DataGrid looks in dark mode; WPF-UI 4.3.0 is the fallback.
3. **Package size:** self-contained is 69 MB compressed; framework-dependent is 4.9 MB but needs the Desktop Runtime 10 installed.
4. **MetadataExtractor pitfalls:** the DNG sub-IFD DateTimeOriginal issue, the `mvhd` time coming back with Kind Unspecified, and no MP4 GPS, so the djmd parser is still required.
5. **GeoTimeZone limits:** ocean points return Etc/ zones, border results can be wrong by an hour, and the data is fixed at 2025b, so time zone law changes need package updates.
6. **Name suggestions are only suggestions.** Some of your names come from roads or buildings, which only Nominatim at zoom 17 or the user can supply.
7. **Unbuffered read-back needs aligned buffers.** Fall back to a normal read if it fails.
8. **Not tested here:** OneDrive's `.tmp` exclusion, `WM_DEVICECHANGE`, UI startup time, and building with Windows dotnet.exe directly from the WSL path. Keep the repo at `C:\dev\uas-sort` so both toolchains can reach it.

Sources:
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [.NET 8/9 end-of-support blog post](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)
- [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [NETSDK1233 issue (VS 2022 and net10)](https://github.com/dotnet/sdk/issues/51678)
- [What's new in WPF for .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)
- [What's new in WinForms for .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/whats-new/net100)
- [WPF ThemeMode API](https://learn.microsoft.com/en-us/dotnet/api/system.windows.application.thememode?view=windowsdesktop-10.0)
- [Windows App SDK self-contained deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
- [Windows App SDK on NuGet](https://www.nuget.org/packages/Microsoft.WindowsAppSDK)
- [Avalonia on NuGet](https://www.nuget.org/packages/Avalonia)
- [Avalonia.Controls.DataGrid on NuGet](https://www.nuget.org/packages/Avalonia.Controls.DataGrid)
- [Avalonia Accelerate licensing blog post](https://avaloniaui.net/blog/building-a-sustainable-future-for-avalonia)
- [CommunityToolkit.Mvvm on NuGet](https://www.nuget.org/packages/CommunityToolkit.Mvvm)
- [WPF-UI on NuGet](https://www.nuget.org/packages/WPF-UI)
- [MetadataExtractor on NuGet](https://www.nuget.org/packages/MetadataExtractor)
- [MetadataExtractor releases](https://github.com/drewnoakes/metadata-extractor-dotnet/releases)
- [GeoTimeZone on NuGet](https://www.nuget.org/packages/GeoTimeZone)
- [GeoTimeZone on GitHub](https://github.com/mattjohnsonpint/GeoTimeZone)
- [TimeZoneConverter on NuGet](https://www.nuget.org/packages/TimeZoneConverter)
- [.NET globalization and ICU](https://learn.microsoft.com/en-us/dotnet/core/extensions/globalization-icu)
- [Single-file deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [WPF trimming issue](https://github.com/dotnet/wpf/issues/4216)
- [System.IO.Hashing on NuGet](https://www.nuget.org/packages/System.IO.Hashing)
- [Microsoft.Data.Sqlite on NuGet](https://www.nuget.org/packages/Microsoft.Data.Sqlite)
- [ExifTool home page](https://exiftool.org/)
- [ExifTool install guide](https://exiftool.org/install.html)
- [GeoNames dump](https://download.geonames.org/export/dump/)
- [Nominatim usage policy](https://operations.osmfoundation.org/policies/nominatim/)
- [OneDrive restrictions and limitations](https://support.microsoft.com/en-us/office/restrictions-and-limitations-in-onedrive-and-sharepoint-64883a5d-228e-48f5-b3d2-eb39e07630fa)
- [DBT_DEVICEARRIVAL](https://learn.microsoft.com/en-us/windows/win32/DevIO/dbt-devicearrival)
- [Detecting media insertion or removal](https://learn.microsoft.com/en-us/windows/win32/DevIO/detecting-media-insertion-or-removal)