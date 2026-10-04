# Cross-part interface registry

This file is the single source of truth for every name, namespace, signature, owner and file that more than one part touches. Executors read it **before** each part and whenever a task's **Interfaces:** block, its code, or a "Produces (summary)" section disagrees with it: **this registry wins over any part text on names, namespaces, signatures, ownership and cross-part contracts** (the spec still wins on behaviour). "Required edits per part" lists the concrete changes each part file needs so that its text matches this registry; until those edits are applied, an executor applies them on the fly while executing the task (rename in the code it writes, skip a duplicate class, use the listed owner's type). Symbols used only inside one part and not listed here keep the part's own spelling. Outside code blocks, `&lt;` stands for a literal less-than sign.

## Decisions

Binding decisions (from the user; apply, don't re-litigate):

1. Namespaces: shared model, ports, guard types and exceptions in `UasSort.Core` (as Part 02); component code in folder namespaces `UasSort.Core.Card`, `.Media`, `.Time`, `.Geo`, `.Library`, `.Ledger`, `.Config` (settings code, folder `src/UasSort.Core/Settings/`), `.Planning`, `.Editing`, `.Naming`, `.Offload`, `.Cleanup`, `.Json`; Review VMs in `UasSort.Review`; Platform in `UasSort.Platform` + one sub-namespace per folder; tests/Testing in `UasSort.Testing` (+ `.Planning`, `.Offload` for scenario builders only). Every consuming project gets the fixed `GlobalUsings.Core.cs` below.
2. Selftest CLI contract: `uas-sort.exe --selftest --result <path> [--only <check>[,<check>…]]`; result JSON `{ "ok", "firstFrameMs", "checks": [ { "name", "status", "detail" } ] }` per Part 13. `--selftest-result` does not exist.
3. Startup gate: warm first frame ≤ 1000 ms is the hard gate everywhere (Part 01 stack proof and Part 13 deploy). Slower than the 370 ms ReadyToRun baseline sets `slowerThanBaseline`, which is reported to the user in the completion summary; it is not a build failure and does not stop any part.
4. Watermark (Ref §7.1): only videos inside event folders (`YYYY-MM-DD …`, via `EventFolderLocator`) without a ledger `file` record count; videos elsewhere (e.g. `Exports\`, loose files) never move it.
5. Ledger "latest per key": `LedgerSnapshot.Files` keeps the strongest verification per `FileKey` (`Unbuffered` > `Cached` > `NameSize`); among equal strength the latest record wins; a later weaker record never downgrades.
6. Fakes/test helpers have one owner each and no duplicate class names (owners in the Testing registry table). `FakeCardWriter` → Part 12 (its only user). The one fake ledger store is `UasSort.Testing.FakeLedgerStore` (Part 06 creates it, Part 07 completes it). `CleanupPlanFixtures.Confirmed(...)` → Part 08, in `UasSort.Testing`, with `InternalsVisibleTo("UasSort.Testing")` on Core. Tests use `Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider`; `FixedTimeProvider` is deleted.
7. Platform single-instance helpers: Part 01 owns `NamedMutexLock`, `SingleInstance`, `ForegroundWindow`, `PlaceholderMode`, `SelfTestSandbox`; Parts 09 and 11 reuse or extend them, never redefine. Exposing placeholders is only `PlaceholderMode.ExposePlaceholders()`; Part 09's `PlaceholderGuard` keeps only `ReadAttributes`.
8. Static vs instance: `MetadataHarvester` static; `CardClassifier(TimeProvider)` instance; `CardDetector` static; `TimeResolver` static (`TimeResolver.Resolve(...)`); `Planner : IPlanDeriver` instance with static `AppendCandidates`; `FolderDecider` instance (ctor) with the statics listed below. The VM retarget flyout calls `Planner.AppendCandidates`, never re-ranks folders.
9. View models: Part 10's members are canonical. Part 11's Contract C11 is rewritten to Part 10's names (mapping table in the Part 11 edits); the four members Part 10 lacks are added to Part 10.
10. Commit orchestration: Part 07's `CommitSession` (Begin/StartAsync/Dispose, `CommitEnvironment`, `CommitResult`) owns the order; Part 10's Preflight/Copy VMs bind to it (the `CommitEngine` delegate record is deleted). Cleanup: Part 08's `CleanupExecutor.RunAsync` owns the run; Part 10's `CleanupEngine.Run` is bound to exactly that call.
11. Review reuses Core helpers: `FileKey` (+ `FileKey.OfPath`) instead of `FileKeys.Of`; Core `GeoMath`; Core `ZoneNames` and `Zones`; Core `GeoPointJsonConverter`; the clock banner text is `ClockSummary.Headline` from `DroneClock.Summarize` (Review only formats).
12. Ledger: Platform loads through Part 05 `LedgerLoader`/`LedgerReader`; `CopyInto` never copies `torn` records; JSON contexts are only `LedgerJsonContext` and `CoreJsonContext` (no `UasSortJsonContext`, no `CoreJson`); settings code in `UasSort.Core.Config` (`SettingsDefaults`, `SettingsCodec`, `SettingsLoadPolicy`, `SettingsRecovery`).
13. `CardAudit.Audit(CardInventory, ListingResult, CardIdentity? now, Plan, OffloadResult?, LedgerSnapshot, CardIdentity? pinned = null)`; every changed/added/removed audit detail starts with exactly `changed since scan`.
14. `IssueCode.NothingNew` (Part 06, Blocking). Index Review Focus #1: "every AlreadyImported group folded; NothingToCopy groups follow the spec's fold rule (never folded)".
15. Resolved by the user on 2026-09-28 (see "Resolved user decisions" at the end): **x64 only** — `Platforms=x64`, `RuntimeIdentifiers=win-x64`, no ARM64 build, the former ARM64 publish task of Part 13 is deleted (the README task is now 13.6, the completion criteria 13.7); the **replay fixture** is pre-generated and checked in at `docs/research/fixtures/library-listing.json` (listing-only metadata; no video copies), and Task 06.19 copies it into place; execution is **subagent-driven**; development runs from a **PowerShell Claude CLI session on Windows** (not WSL), with Task 01.0 installing the prerequisites first.

Further decisions made for this registry:

16. Global usings are two files per project: `GlobalUsings.Core.cs` (identical everywhere, exact content below) and a project-specific `GlobalUsings.cs`. Core namespaces that have no types yet are anchored by `src/UasSort.Core/Namespaces.cs` (Part 02 Task 02.1); Platform's by `src/UasSort.Platform/Namespaces.cs` (Part 09 Task 09.1).
17. No namespace is imported twice in one project (ImplicitUsings, csproj `<Using>`, `GlobalUsings.Core.cs`, `GlobalUsings.cs`); `Xunit` comes only from the test csproj `<Using Include="Xunit" />`.
18. Shared fakes live in flat `UasSort.Testing`; `UasSort.Testing.Planning` and `UasSort.Testing.Offload` hold only scenario builders; `UasSort.Testing.Cleanup` is not used.
19. `CleanupPlan` and `ConfirmedCleanupPlan` stay in namespace `UasSort.Core` (Part 02). `CleanupPlan`'s internal constructor gains `string inventoryHash, string? cameraModel` right after `cardRoot` (18 parameters) and the properties `InventoryHash`, `CameraModel`; Part 08 adds `Confirm` and `CleanupPlanner.Build` only. `ConfirmedCleanupPlan`'s derivation (Part 02, `PathRules.Join`) is canonical.
20. Core `InternalsVisibleTo`: `UasSort.Core.Tests` and `UasSort.Testing`, both in `UasSort.Core.csproj` (Part 02 Task 02.4); no assembly-attribute file elsewhere.
21. `PlatformServices` (composition record of every Platform port) is owned by Part 11 (Task 11.3) in namespace `UasSort.Platform`.
22. Selftest types are Part 01's and extended by Part 11 in place: `LaunchOptions` (parses `--selftest`, `--result`, `--only`, `--single-instance-mutex`), `SelfTestCheck(Name, Status, Detail)`, `SelfTestResult(Ok, FirstFrameMs, Checks)`, `SelfTestJsonContext`, `SingleInstanceGate`. `SelfTestOptions` and `SelfTestJson` do not exist. The result JSON has no `v`/`kind`.
23. CLI `PlanText` is renamed `PlanTextRenderer` (it would be ambiguous with `UasSort.Core.Planning.PlanText` in Platform.Tests).
24. Review's `NotCopiedKind` is deleted; `UasSort.Core.Offload.NotCopiedKind` is used. The Verdict page uses Part 07's `VerdictDecisions` for rows, checks, confirmation text, decision and revoke records.
25. `FileKey.OfPath(string pathOrName, long size)` (last segment after `/` or `\`, then `Of`) is added to Part 02; Review's `FileKeys` becomes `DecisionTargets` (`For`, `ForEntry`; no `Of`).
26. Core `ZoneNames` gains `OffsetAt(string ianaId, DateTime utc)` and `IsUs(string ianaId)` (Part 04) so Review drops its own `ZoneNames`. `DroneClock.Round15` replaces `Fmt.Round15`.
27. `GuardedFileOps` takes the run's `NewFolderDirs` set verbatim (`OffloadCompiler.NewFolderDirs`), canonicalised, with no ancestor expansion.
28. `OffloadLock` wraps Part 01's `NamedMutexLock`; Part 09's `MutexHolder`, `Shell.SingleInstance`, `NtDll` placeholder call and foreground `User32` imports are deleted.
29. `GatedPlanDeriver`: owner Part 06, namespace `UasSort.Testing`, file `tests/UasSort.Testing/GatedPlanDeriver.cs`; Part 06's int counter is `CallCount`; Part 10 adds `Hold`, `Calls` (list of `DeriveCall`), `CallStartedAsync`, `Release(int)`, `ReleaseAll` and `DeriveCall` in the same file.
30. `FakePlaceIndex(params IReadOnlyList<PlaceHit> places)` (Part 04) serves both call shapes.
31. `LedgerSamples.AllRecordKindsV1()` is owned by Part 11 (Task 11.9) in `UasSort.Testing`, built with `LedgerCodec.Serialize`.
32. Machine name is `Environment.MachineName`, passed explicitly by the composition roots (App, CLI); only `CleanupExecutor` reads it directly (Part 08 G4).
33. Layout persistence: Part 10 adds `ShellDeps.SaveSettings` and `ShellVm.UpdateLayout`; Part 11 reads `ShellVm.Settings.Layout`.
34. App thumbnail facade: Part 11 `CardThumbnails : IThumbnailSource` swaps the per-scan `ThumbnailReader` (built in `ShellDeps.CreateReview` from `PlanBase.Scan.Raw`); it is the `IThumbnailSource` given to `ReviewServices`, `CommitEnvironment` and `CleanupEnvironment`.
35. `src/UasSort.App/places.bin.gz` becomes App `Content` (PreserveNewest) in Part 11 Task 11.4 (Part 01 does not add it).
36. `tools/fixtures/make-selftest-assets.cs`: Part 01 creates it; Part 11 extends it (adds `selftest.dng`, `ledger-v1.jsonl`, `selftest-000{1,2,3}.mp4`); Part 03 never creates it.
37. Part 09 `LedgerStore.Check` builds `LedgerFolderFacts` and calls `LedgerFolderStatusBuilder.Build`; `Load` calls `LedgerLoader.Load`; `LedgerWriter` serialises with `LedgerCodec.Serialize`; `SettingsStore` uses `SettingsCodec`, `SettingsLoadPolicy` and `SettingsRecovery`; drafts and reports use `CoreJsonContext`.
38. `CleanupRules.ChangedSinceScanDetail` is defined as `CardDiffResult.ChangedDetail`.
39. `ReviewSessionFactory` is deleted; the VM creates `new PlanSession(b, deriver, tuning, time)` and mirrors `PlanSession.CanUndo/CanRedo`.
40. Part 09's source-guard tests keep "eraser-only deletes" and "justified suppressions"; the `BannedSymbols.txt` content test is Part 01's; `RepoFiles` is replaced by `RepoPaths`.
41. Review.Tests' `Clip` record is renamed `PlanClip` (no clash with `UasSort.Testing.Planning.Clip`).
42. `Sites` (Part 06) keeps its name but each field is `=> FixturePoints.<same name>` (no second copy of coordinates).
43. `CleanupVm` binds `CleanupTexts` (Part 08) for every cutoff/shortfall/evidence/never-copied/nothing-to-delete text and `CleanupRowOps` for Keep all / Delete all / single-row changes.
44. `IAppAssets.OpenMapAsset(string)` exists only if Part 11 Task 11.7 reaches fallback 2; then Part 11 adds it to Part 02's port, to Part 09's `AppAssets` and to Part 04's test fake `BytesAssets` (`tests/UasSort.Core.Tests/Geo/PlaceIndexTests.cs`, returning `Stream.Null`) in the same commit.
45. Planning helpers delegate to Core helpers: `PlanningGeo.Haversine` → `GeoMath.Haversine`, `PlanningGeo.EarthRadiusM` → `GeoMath.EarthRadiusMeters`, `PlanText.ZoneAbbrev` → `ZoneNames.Abbreviation`, `Planner` clock summary → `DroneClock.Summarize`.
46. `SettingsLoad.Recovered` keeps Part 05's meaning (true only for an unreadable file; a missing file gives `Recovered = false`, `RootsConfirmed = false`).

## Namespaces and GlobalUsings

### Namespace per folder

| Project / folder | Namespace |
|---|---|
| `src/UasSort.Core/Model`, `Ports`, `Guard`, `Ledger/LedgerPaths.cs` | `UasSort.Core` |
| `src/UasSort.Core/Card` | `UasSort.Core.Card` |
| `src/UasSort.Core/Media` | `UasSort.Core.Media` |
| `src/UasSort.Core/Time` (incl. the `ClockModel` partial file) | `UasSort.Core.Time` (the `ClockModel` partial itself: `UasSort.Core`) |
| `src/UasSort.Core/Geo` | `UasSort.Core.Geo` |
| `src/UasSort.Core/Library` (the `LibraryIndex`/`LibraryFolder` partials: `UasSort.Core`) | `UasSort.Core.Library` |
| `src/UasSort.Core/Ledger` (except `LedgerPaths`) | `UasSort.Core.Ledger` |
| `src/UasSort.Core/Settings` | `UasSort.Core.Config` |
| `src/UasSort.Core/Planning` | `UasSort.Core.Planning` |
| `src/UasSort.Core/Editing` | `UasSort.Core.Editing` |
| `src/UasSort.Core/Naming` | `UasSort.Core.Naming` |
| `src/UasSort.Core/Offload` | `UasSort.Core.Offload` |
| `src/UasSort.Core/Cleanup` (the `CleanupPlan` partial: `UasSort.Core`) | `UasSort.Core.Cleanup` |
| `src/UasSort.Core/Json` | `UasSort.Core.Json` |
| `src/UasSort.Core/StackProof` (Part 01 canaries) | `UasSort.Core.StackProof` (explicit `using` only) |
| `src/UasSort.Platform/PlatformServices.cs` | `UasSort.Platform` |
| `src/UasSort.Platform/Io`, `Card`, `Ledger`, `Stores`, `Win32`, `Shell`, `Logging` | `UasSort.Platform.Io`, `.Card`, `.Ledger`, `.Stores`, `.Win32`, `.Shell`, `.Logging` |
| `src/UasSort.Review/**` (every folder) | `UasSort.Review` |
| `src/UasSort.App` root, `Pages`, `Controls`, `Services`, `MapHost`, `SelfTest` | `UasSort.App`, `UasSort.App.Pages`, `.Controls`, `.Services`, `.MapHost`, `.SelfTest` |
| `src/UasSort.Cli` | `UasSort.Cli` |
| `tests/UasSort.Testing` (shared fakes and builders) | `UasSort.Testing` |
| `tests/UasSort.Testing/Planning`, `Replay` | `UasSort.Testing.Planning` |
| `tests/UasSort.Testing/Offload` | `UasSort.Testing.Offload` |
| `tests/UasSort.Core.Tests/<Folder>` | `UasSort.Core.Tests.<Folder>` |
| `tests/UasSort.Review.Tests` | `UasSort.Review.Tests` |
| `tests/UasSort.Platform.Tests/<Folder>` | `UasSort.Platform.Tests[.<Folder>]` |

### `GlobalUsings.Core.cs` (identical file in every project that references Core)

Projects: `src/UasSort.Core`, `src/UasSort.Review`, `src/UasSort.Platform`, `src/UasSort.App`, `src/UasSort.Cli`, `tests/UasSort.Testing`, `tests/UasSort.Core.Tests`, `tests/UasSort.Review.Tests`, `tests/UasSort.Platform.Tests`. Created by Part 02 Task 02.1 in all nine projects at once (never generated by a script).

```csharp
// GlobalUsings.Core.cs — fixed content, see docs/superpowers/plans/2026-09-27-uas-sort/00-interfaces.md
global using System.Collections.Immutable;
global using UasSort.Core;
global using UasSort.Core.Card;
global using UasSort.Core.Cleanup;
global using UasSort.Core.Config;
global using UasSort.Core.Editing;
global using UasSort.Core.Geo;
global using UasSort.Core.Json;
global using UasSort.Core.Ledger;
global using UasSort.Core.Library;
global using UasSort.Core.Media;
global using UasSort.Core.Naming;
global using UasSort.Core.Offload;
global using UasSort.Core.Planning;
global using UasSort.Core.Time;
```

### `src/UasSort.Core/Namespaces.cs` (Part 02 Task 02.1)

```csharp
// Anchors every Core namespace named in GlobalUsings.Core.cs before its first real type exists (Parts 03–08).
namespace UasSort.Core.Card { internal static class NamespaceMarker { } }
namespace UasSort.Core.Cleanup { internal static class NamespaceMarker { } }
namespace UasSort.Core.Config { internal static class NamespaceMarker { } }
namespace UasSort.Core.Editing { internal static class NamespaceMarker { } }
namespace UasSort.Core.Geo { internal static class NamespaceMarker { } }
namespace UasSort.Core.Json { internal static class NamespaceMarker { } }
namespace UasSort.Core.Ledger { internal static class NamespaceMarker { } }
namespace UasSort.Core.Library { internal static class NamespaceMarker { } }
namespace UasSort.Core.Media { internal static class NamespaceMarker { } }
namespace UasSort.Core.Naming { internal static class NamespaceMarker { } }
namespace UasSort.Core.Offload { internal static class NamespaceMarker { } }
namespace UasSort.Core.Planning { internal static class NamespaceMarker { } }
namespace UasSort.Core.Time { internal static class NamespaceMarker { } }
```

### Project-specific `GlobalUsings.cs`

`src/UasSort.Core/GlobalUsings.cs` — none (not created).

`src/UasSort.Review/GlobalUsings.cs` (Part 10 Task 10.1):

```csharp
global using System.Collections.ObjectModel;
global using System.Globalization;
global using CommunityToolkit.Mvvm.ComponentModel;
global using CommunityToolkit.Mvvm.Input;
```

`src/UasSort.Platform/GlobalUsings.cs` (Part 09 Task 09.1; the markers of `src/UasSort.Platform/Namespaces.cs` cover `Card`, `Io`, `Ledger`, `Logging`, `Shell`, `Stores`; `Win32` and `Stores` already have Part 01 types):

```csharp
global using UasSort.Platform.Card;
global using UasSort.Platform.Io;
global using UasSort.Platform.Ledger;
global using UasSort.Platform.Logging;
global using UasSort.Platform.Shell;
global using UasSort.Platform.Stores;
global using UasSort.Platform.Win32;
```

`src/UasSort.App/GlobalUsings.cs` (Part 11 Task 11.4):

```csharp
global using System.Collections.ObjectModel;
global using UasSort.App.Controls;
global using UasSort.App.MapHost;
global using UasSort.App.Pages;
global using UasSort.App.SelfTest;
global using UasSort.App.Services;
global using UasSort.Platform;
global using UasSort.Platform.Card;
global using UasSort.Platform.Io;
global using UasSort.Platform.Ledger;
global using UasSort.Platform.Logging;
global using UasSort.Platform.Shell;
global using UasSort.Platform.Stores;
global using UasSort.Platform.Win32;
global using UasSort.Review;
```

`src/UasSort.Cli/GlobalUsings.cs` (Part 12 Task 12.1):

```csharp
global using UasSort.Platform.Card;
global using UasSort.Platform.Io;
global using UasSort.Platform.Ledger;
global using UasSort.Platform.Shell;
global using UasSort.Platform.Stores;
global using UasSort.Platform.Win32;
```

`tests/UasSort.Testing/GlobalUsings.cs` (Part 02 Task 02.1):

```csharp
global using Microsoft.Extensions.Time.Testing;
```

`tests/UasSort.Core.Tests/GlobalUsings.cs` (Part 02 Task 02.1; csproj keeps `<Using Include="Xunit" />`):

```csharp
global using Microsoft.Extensions.Time.Testing;
global using UasSort.Testing;
```

`tests/UasSort.Review.Tests/GlobalUsings.cs` (Part 10 Task 10.1; csproj keeps `<Using Include="Xunit" />`):

```csharp
global using System.Globalization;
global using Microsoft.Extensions.Time.Testing;
global using UasSort.Review;
global using UasSort.Testing;
```

`tests/UasSort.Platform.Tests/GlobalUsings.cs` (Part 09 Task 09.1; csproj keeps `<Using Include="Xunit" />`):

```csharp
global using Microsoft.Extensions.Time.Testing;
global using UasSort.Platform.Card;
global using UasSort.Platform.Io;
global using UasSort.Platform.Ledger;
global using UasSort.Platform.Logging;
global using UasSort.Platform.Shell;
global using UasSort.Platform.Stores;
global using UasSort.Platform.Win32;
global using UasSort.Testing;
```

Every test project and `UasSort.Testing` has `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />` (version from `Directory.Packages.props`). `UasSort.Testing.Planning` / `UasSort.Testing.Offload` are imported per file with `using`, never globally. `UasSort.Core.StackProof` is imported per file.

## Registry

Owner = the part/task that declares the symbol (later parts that extend it are named in the signature cell). Consumers = parts that call or bind it. "Ref" = spec reference types, verbatim.

### Model (namespace `UasSort.Core`, owner Part 02 unless noted)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `PathRules` | static class | `Normalize(string)`, `Equal(string,string)`, `IsSameOrUnder(string path,string root)`, `IsStrictlyUnder`, `Overlaps`, `Parent(string)→string?`, `FileName(string)`, `Join(string root,string relative)`, `RelativeCardPath(string path,string root)`, `SetContains(IEnumerable<string>,string)` | `UasSort.Core` | 02.1 | 03–12 |
| `LedgerPaths` | static class | `const FolderName` (".uas-sort"), `const FilePattern`, `For(string videoRoot)`, `OwnFile(string videoRoot,string machine)`, `BackupDir(string appDataDir,string canonicalVideoRoot)`, `IsLedgerFileName(string)` | `UasSort.Core` | 02.1 | 05–12 |
| `ItemId` | record struct | `ItemId(string CardRelPath)` ('/' separators; JSON string via `ItemIdJsonConverter`) | `UasSort.Core` | 02.1 | 03–12 |
| `GeoPoint` | record struct | `GeoPoint(double Lat, double Lon)` (JSON `[lon,lat]`) | `UasSort.Core` | 02.1 | 03–12 |
| `Distance` | record struct | `Distance(double Meters) { const EarthRadiusMeters; double Miles; static FromMiles(double) }` | `UasSort.Core` | 02.1 | 04–12 |
| `ByteRange` | record struct | `ByteRange(long Offset, int Length)` | `UasSort.Core` | 02.1 | 03, 11 |
| `ItemKind`, `SetKind` | enums | `{ Video, Photo, Set }`, `{ Panorama, Hyperlapse }` | `UasSort.Core` | 02.1 | 03–10 |
| `FileKey` | record struct | `FileKey(string NormName, long Size) { static string NormalizeName(string fileName); static FileKey Of(string fileName, long size); static FileKey OfPath(string pathOrName, long size) /* added, decision 25 */ }` | `UasSort.Core` | 02.1 | 05–10 |
| `EntryClass` | enum | `{ Video, Photo, PhotoTwin, SetMember, Skip, Unknown }` | `UasSort.Core` | 02.2 | 03, 07, 08, 12 |
| `CardEntry` | record | `CardEntry(string RelPath /* '/' */, long Size, DateTime MtimeUtc, DateTime CreationUtc, DateTime LastAccessUtc, uint RawAttributes, EntryClass Class, string? Rule)` | `UasSort.Core` | 02.2 | 03–12 |
| `CardIdentity` | record | `CardIdentity(uint VolumeSerial, string? Label, string FileSystem, long TotalBytes)` | `UasSort.Core` | 02.2 | 03–12 |
| `MediaUnit` | closed record | `MediaUnit(ItemId Id)`: `VideoUnit(Id, CardEntry Mp4, bool HasTrinf)`, `PhotoUnit(Id, CardEntry Primary, CardEntry? JpgTwin)`, `SetUnit(Id, SetKind Kind, string SetName, ImmutableArray<CardEntry> Members)` | `UasSort.Core` | 02.2 | 03–10 |
| `CardSource` | record | `CardSource(string Root, CardIdentity? Identity, bool IsBrowsedFolder, bool IsWriteProtected) { string DraftKey }` | `UasSort.Core` | 02.2 | 06–12 |
| `CardInventory` | record | `CardInventory(CardSource Source, DateTime ListedUtc, string InventoryHash, ImmutableArray<CardEntry> Entries, ImmutableArray<MediaUnit> Units, string? CameraModel, ImmutableArray<ScanWarning> Warnings)` | `UasSort.Core` | 02.2 | 03, 06–10, 12 |
| `ScanWarning` | record | `ScanWarning(string Code, string Message, string? RelPath, bool ForcesNotSafe)` | `UasSort.Core` | 02.2 | 06, 07, 10 |
| `CardCandidate`, `SourceOk`, `SourceRefused`, `CardSourceCheck` | records + union | `CardCandidate(VolumeInfo Volume, bool IsDjiCard, string? NotCardReason, int MediaCount)`; `SourceOk(CardSource Source)`; `SourceRefused(string Reason)`; `union CardSourceCheck(SourceOk, SourceRefused)` | `UasSort.Core` | 02.2 | 10–12 |
| `VolumeInfo` | record | `VolumeInfo(string Root, CardIdentity Identity, string DriveType, bool IsReady, bool IsReadOnlyVolume, bool IsNtfs, bool IsRemovableBus, long FreeBytes, string BusType, bool RemovableMedia, bool IsSystemBootOrPaging)` | `UasSort.Core` | 02.2 | 07–12 |
| `FsEntry`, `ListingResult` | records | `FsEntry(string FullPath, string RelPath /* '\' */, bool IsDirectory, long Size, DateTime MtimeUtc, DateTime CreationUtc, DateTime LastAccessUtc, uint RawAttributes)`; `ListingResult(ImmutableArray<FsEntry> Entries, ImmutableArray<(string Path, int Win32Error)> Errors)` | `UasSort.Core` | 02.2 | 02–12 |
| `GpsSource`, `GpsFix`, `NoFixReason`, `NoFix`, `GpsProbe` | enum/records/union | `GpsSource { DjmdModelTable, DjmdGenericSearch, MdatHeadFallback, Exif }`; `GpsFix(GeoPoint Point, double? AltM, int Sample, GpsSource Source, string? FieldPath)`; `NoFixReason { NotDji, NoDjmdTrack, AllProbedSamplesZero, Unparseable, NoGpsTag, GenericHitImplausible }`; `NoFix(NoFixReason Reason)`; `union GpsProbe(GpsFix, NoFix)` | `UasSort.Core` | 02.2 | 03, 04, 06, 10, 11 |
| `Mp4Info` | record | `Mp4Info(DateTime? MvhdUtc, bool HasMoov, GpsProbe First, GpsFix? LastSameField, string? Protocol, DateTime? SessionUtc, string? DroneSerial, ByteRange? Thumb, TimeSpan? Duration)` | `UasSort.Core` | 02.2 | 03, 04, 06, 08 |
| `SessionKey` | record struct | `SessionKey(string? DroneSerial, DateTime SessionUtc) { bool SameSession(SessionKey o) }` | `UasSort.Core` | 02.2 | 04, 06–08 |
| `StillInfo` | record | `StillInfo(DateTime? DtoNaive, TimeSpan? OffsetTime, GpsProbe Gps, string? Model, ByteRange? Thumb)` | `UasSort.Core` | 02.2 | 03, 04, 11 |
| `RawItem` | record | `RawItem(MediaUnit Unit, ItemKind Kind, string Name, long Bytes, DateTime CardMtimeUtc, DateTime? DroneStamp, Mp4Info? Mp4, StillInfo? Still, string? ProbeError)` | `UasSort.Core` | 02.2 | 03–10 |
| `ClockMode`, `StoredClockMode`, `ClockSample` | enums/record | `ClockMode { SiteLocal, Zone, NearestSample, Setting }`; `StoredClockMode { SiteLocal, Zone }`; `ClockSample(DateTime DroneStamp, DateTime MvhdUtc, TimeSpan Offset, string? SiteZoneId)` | `UasSort.Core` | 02.2 | 04–06, 10, 12 |
| `ClockModel` | partial record | `ClockModel(ClockMode Mode, string? ZoneId, ImmutableArray<ClockSample> Samples, TimeSpan? Modal, StoredClockMode SettingMode, string SettingZoneId)`; Part 04 adds `(DateTime Utc, TimeSource Src)? ToUtc(DateTime droneStamp, string? siteZoneId)`, `TimeSpan OffsetAt(DateTime droneStamp, string? siteZoneId)` | `UasSort.Core` | 02.2 (+04.5) | 04–06, 10 |
| `ClockSummary`, `ClockChange` | records | `ClockSummary(ClockMode Mode, string? ZoneId, int SampleCount, string Headline, int MismatchItems, ImmutableArray<string> MismatchSiteZones, ImmutableArray<ClockChange> Changes)`; `ClockChange(DateTime AtUtc, TimeSpan From, TimeSpan To)` | `UasSort.Core` | 02.2 | 04, 06, 10, 12 |
| `TimeSource`, `TzSource`, `ItemFlags` | enums | `TimeSource { Mvhd, ExifWithOffset, DroneClockSiteLocal, DroneClockZone, DroneClockSample, DroneClockSetting, Mtime }`; `TzSource { Gps, SameSession, NearestGpsWithin12h, NearestLandGpsOnCard, GeoNamesTz, PcZone }`; `[Flags] ItemFlags { None, NoGps=1, Truncated=2, ClockNotSet=4, CheckDate=8, TzFallback=16, ProbeFailed=32, GpsGuessed=64, ClockFromSetting=128, ClockMismatch=256 }` | `UasSort.Core` | 02.2 | 04–12 |
| `ItemTime` | record | `ItemTime(DateTime CaptureUtc, TimeSource Source, string TzId, TzSource TzSource, DateOnly LocalDate, DateTime LocalTime)` | `UasSort.Core` | 02.2 | 04–12 |
| `NewReason`, `Evidence`, `DecisionKind` | enums | `NewReason { NoMatch, SeenNotCopied, AfterWatermark, NearWatermark, DayHasNewVideos }`; `Evidence { LibraryNameSize, LedgerVerified, LedgerNameSize }`; `DecisionKind { AssumedImported, Dismissed }` | `UasSort.Core` | 02.2 | 06–10 |
| `LibraryFolderRef` | record | `LibraryFolderRef(string FullPath, DateOnly NameDate, string Description)` | `UasSort.Core` | 02.2 | 05–10 |
| `Newness` | closed record | `Newness`: `IsNew(NewReason Why, DateTime? SeenUtc)`, `Imported(Evidence By, LibraryFolderRef? Folder, string Why)`, `Decided(DecisionKind Kind, DateTime AtUtc, string Machine)`, `ProbablyImported(string Why)`, `Conflict(string ExistingPath, long ExistingSize)` | `UasSort.Core` | 02.2 | 06–12 |
| `Item` | record | `Item(RawItem Raw, ItemTime Time, GpsFix? Gps, SessionKey? Session, ItemFlags Flags, Newness Newness)` | `UasSort.Core` | 02.2 | 06–12 |
| `LocationSource`, `LibraryFolder` | enum / partial record | `LocationSource { Ledger, CardLeftovers, Unknown }`; `LibraryFolder(LibraryFolderRef Ref, ImmutableArray<DateTime> MemberStartsUtc, string? TzId, GeoPoint? Centroid, LocationSource Loc)`; Part 05 adds `ImmutableHashSet<DateOnly> DaysIn(string tzId)` | `UasSort.Core` | 02.3 (+05.2) | 05–10 |
| `RootListing`, `LibraryListings`, `LibraryFile`, `SetFolderListing` | records | `RootListing(string Root, DestRoot Kind, bool IsPrevious, bool Available, ListingResult Listing)`; `LibraryListings(RootListing Video, RootListing Photo, ImmutableArray<RootListing> PreviousPhoto)`; `LibraryFile(string FullPath, FileKey Key, DateTime MtimeUtc, uint RawAttributes, LibraryFolderRef? EventFolder)`; `SetFolderListing(string FullPath, string Name, ImmutableArray<(string Member, long Size, DateTime MtimeUtc)> Members)` | `UasSort.Core` | 02.3 | 05–10 |
| `LibraryIndex` | partial class | members in the Library table (Part 05) | `UasSort.Core` | 02.3 (+05.11) | 06–10 |
| `VerifyKind`, `FolderSource` | enums | `VerifyKind { Unbuffered, Cached, NameSize }` (strength order Unbuffered > Cached > NameSize, decision 5); `FolderSource { Created, Appended, CardLeftovers }` | `UasSort.Core` | 02.3 | 05, 07, 08 |
| `LedgerFile`, `LedgerSet`, `LedgerDecision`, `LedgerFolder`, `LedgerRun`, `LedgerCardDelete`, `LedgerParseIssue` | records | `LedgerFile(FileKey Key, string Src, DestRoot Root, string Dest, UInt128? Xxh128, VerifyKind Verify, DateTime AtUtc, DateTime? CaptureUtc, GeoPoint? Point, string? TzId, DateOnly? LocalDate, SessionKey? Session, string? Set, string Machine, string Run)`; `LedgerSet(string SetName, DateTime FirstFrameCaptureUtc, ImmutableArray<(string Member, long Size)> Members)`; `LedgerDecision(string Id, FileKey Key, DecisionKind Kind, DateTime AtUtc, string Machine, string? Set, string Why)`; `LedgerFolder(string Path, string Description, FolderSource Source, GeoPoint? Centroid, DateOnly Start, DateOnly End, string TzId)`; `LedgerRun(string Run, string Machine, DateTime StartUtc, DateTime EndUtc, string App, CardIdentity Card, string? Model, string InventoryHash, string VideoRoot, string PhotoRoot, VerdictLevel Verdict, ImmutableDictionary<AuditCategory,int> Counts)`; `LedgerCardDelete(string Run, DateTime AtUtc, FileKey Key, string Src, string Evidence, string Machine)`; `LedgerParseIssue(string File, int Line, string Reason)` | `UasSort.Core` | 02.3 | 05–10 |
| `LedgerFolderState`, `LedgerFolderStatus` | enum / record | `LedgerFolderState { Ok, Empty, Missing, NotPinned, Unwritable, CloudOnly, VideoRootMissing, Unlistable }` (Unlistable: the folder exists but its listing failed; final fix F2, Blocking `LedgerUnlistable`); `LedgerFolderStatus(string Folder, LedgerFolderState State, bool Exists, bool InSyncRoot, bool Pinned, bool Writable, ImmutableArray<string> LedgerFiles, ImmutableArray<string> CloudOnlyFiles, ImmutableArray<string> OtherMachineFiles)` | `UasSort.Core` | 02.3 | 05–10 |
| `LedgerSnapshot` | record | `LedgerSnapshot(ImmutableDictionary<FileKey,LedgerFile> Files /* strongest verify per key, decision 5 */, ImmutableDictionary<string,ImmutableArray<LedgerSet>> SetsByName, ImmutableDictionary<FileKey,LedgerDecision> Decisions, ImmutableDictionary<FileKey,DateTime> Seen, ImmutableDictionary<string,LedgerFolder> Folders, ImmutableArray<LedgerRun> Runs, ImmutableArray<LedgerCardDelete> CardDeletes, ImmutableArray<LedgerParseIssue> ParseIssues, ImmutableArray<string> SourceFiles, LedgerFolderStatus Status)` | `UasSort.Core` | 02.3 | 05–10 |
| `ILedgerWriter` | interface | `ILedgerWriter : IDisposable { void Append(LedgerRecord r); }` | `UasSort.Core` | 02.3 | 07–10 |
| `LedgerRecord` | closed record (JSON `t`) | `LedgerRecord(int V, string Id, string Machine)`: `"file" FileRecord(V, Id, Machine, string Run, DateTime At, string Kind, string Name, long Size, string Src, string Root, string Dest, string? Xxh128, string Verify, DateTime Mtime, DateTime? CaptureUtc, string? TimeSource, double? Lat, double? Lon, string? Tz, DateOnly? LocalDate, DateTime? SessionUtc, string? Serial, string? Set)`; `"folder" FolderRecord(V, Id, Machine, string Run, string Path, string Desc, string Source, double? Lat, double? Lon, DateOnly Start, DateOnly End, string Tz)`; `"seen" SeenRecord(V, Id, Machine, string Run, DateTime At, string Name, long Size, string Src, DateTime? CaptureUtc, string Status, string Why, string? Set)`; `"decision" DecisionRecord(V, Id, Machine, string? Run, DateTime At, string Kind, string Name, long Size, string Src, DateTime? CaptureUtc, string Why, string? Set)`; `"revoke" RevokeRecord(V, Id, Machine, DateTime At, string Decision)`; `"run" RunRecord(V, Id, Machine, string Run, DateTime Start, DateTime End, string App, RunCard Card, RunRoots Roots, string Verdict, ImmutableDictionary<string,int> Counts)`; `"torn" TornRecord(V, Id, Machine, DateTime At, int Line)`; `"cardDelete" CardDeleteRecord(V, Id, Machine, string Run, DateTime At, string Name, long Size, string Src, string Unit, DateTime? CaptureUtc, string Evidence, string Reason, string Mode, RunCard Card, string? Set)` | `UasSort.Core` | 02.3 | 05, 07–11 |
| `RunCard`, `RunRoots` | records | `RunCard(string Serial, string? Label, string Fs, string? Model, string InventoryHash)`; `RunRoots(string Video, string Photo)` | `UasSort.Core` | 02.3 | 05, 07–09 |
| `Settings` | record | `Settings(int Schema, string VideoRoot, string PhotoRoot, ImmutableArray<string> PreviousPhotoRoots, double RadiusMiles, int GapDays, StoredClockMode DroneClockMode, string DroneClockZone, bool CopyJpgTwin, MapSettings Map, LayoutSettings Layout, bool RootsConfirmed)` | `UasSort.Core` | 02.3 | 02–12 |
| `MapSettings`, `LayoutSettings`, `SettingsLoad`, `Draft` | records | `MapSettings(string Base, string StreetsStyleUrl, string StreetsDarkStyleUrl, string SatelliteUrl, ImmutableDictionary<string,string> SatellitePresets)`; `LayoutSettings(double TimelineWidth, double MapHeightRatio)`; `SettingsLoad(Settings Settings, bool Recovered, string? CorruptCopyPath, RunRoots? RootsFromLastRun)`; `Draft(int V, string CardKey, string InventoryHash, DateTime SavedUtc, Tuning Tuning, ImmutableArray<PlanEdit> Edits)` | `UasSort.Core` | 02.3 | 05, 06, 09–12 |
| `TzLookup`, `PlaceClass`, `PlaceHit`, `ResolvedItem` | records/enum | `TzLookup(string IanaId, ImmutableArray<string> Alternatives, bool IsEtc)`; `PlaceClass : byte { Populated, Feature }`; `PlaceHit(string Name, GeoPoint Point, PlaceClass Class, string FeatureCode, int Population, string TzId, Distance Away)`; `ResolvedItem(RawItem Raw, ItemTime Time, ItemFlags Flags, GpsFix? Gps, SessionKey? Session)` | `UasSort.Core` | 02.3 | 04, 06, 08 |
| `Tuning`, `GroupId`, `BoundaryCause`, `Boundary`, `DaySplit`, `Confidence` | records/enums | `Tuning(double RadiusMiles = 50, int GapDays = 1)`; `GroupId(ItemId Anchor)` (record struct); `BoundaryCause { DayGap, Distance, LibraryFolder, UserSplit }`; `Boundary(GroupId Left, GroupId Right, BoundaryCause Cause, Distance? Jump, TimeSpan Gap, int DayGap)`; `DaySplit(ItemId FirstOfDay, DateOnly From, DateOnly To, Distance? Apart, TimeSpan Gap, bool Emphasised)`; `Confidence { High, Medium }` | `UasSort.Core` | 02.3 | 06, 07, 10–12 |
| `GroupTarget` | closed record | `AlreadyImported(LibraryFolderRef Folder)`, `NothingToCopy(string Summary)`, `NewFolder(string RelPath)`, `Append(LibraryFolderRef Folder, Confidence Confidence, string Why, CrossDayHint? Hint)`, `SkipGroup()` | `UasSort.Core` | 02.3 | 06, 07, 10–12 |
| `CrossDayHint`, `DescSource`, `Suggestion`, `PinState` | records/enum | `CrossDayHint(string Text, ImmutableArray<PlanEdit> Fix)`; `DescSource { ExistingFolder, Ledger, Feature, Place, Town, User, None }`; `Suggestion(string Text, DescSource Source, Distance? Away, DateOnly? ForDay)`; `PinState(bool MembershipChanged, int PinnedCount, int NowCount)` | `UasSort.Core` | 02.3 | 06, 10 |
| `VideoGroup` | record | `VideoGroup(GroupId Id, ImmutableArray<ItemId> Videos, GeoPoint? Centroid, Distance Spread, DateOnly Start, DateOnly End, LibraryFolderRef? Wall, GroupTarget Target, PinState? TargetPin, string Description, DescSource DescSource, bool DescriptionEditable, PinState? NamePin, ImmutableArray<Suggestion> Suggestions, ImmutableArray<DaySplit> DaySplits, ImmutableArray<string> Hints, bool Foldable, int ColorIndex)` | `UasSort.Core` | 02.3 | 06, 07, 10–12 |
| `SetResolution`, `SetPlacement`, `PhotoDay` | enum/records | `SetResolution { Plain, Resume, DateSuffixed, Imported }`; `SetPlacement(ItemId Set, string FolderName, SetResolution Resolution, ImmutableArray<string> MembersToCopy)`; `PhotoDay(DateOnly Date, string TzId, ImmutableArray<ItemId> Items, string Reason)` | `UasSort.Core` | 02.3 | 06, 07, 10, 12 |
| `IssueSeverity`, `IssueCode` | enums | `IssueSeverity { Blocking, Warning, Info }`; `IssueCode` = Part 02's catalogue **plus `NothingNew`** appended to the Planner.Derive group (Part 06 Task 06.1 edits Part 02's file) | `UasSort.Core` | 02.3 (+06.1) | 06, 07, 10, 12 |
| `Issue`, `QuickFix`, `SessionFlags` | records | `Issue(IssueSeverity Severity, IssueCode Code, string Message, ItemId? Anchor, ImmutableArray<QuickFix> QuickFixes, bool RequiresAckAtPreflight)`; `QuickFix(string Label, ImmutableArray<PlanEdit> Edits)`; `SessionFlags(bool LedgerIssuesAccepted)` | `UasSort.Core` | 02.3 | 06, 07, 10, 12 |
| `ScanResult`, `PlanBase`, `Plan` | records | `ScanResult(CardInventory Inventory, ImmutableArray<RawItem> Raw, LibraryIndex Library, LedgerSnapshot Ledger, ClockModel Clock, ImmutableArray<ScanWarning> Warnings, Settings Settings)`; `PlanBase(ScanResult Scan, ImmutableArray<Item> Items, ImmutableArray<PhotoDay> PhotoDays, ImmutableDictionary<ItemId,SetPlacement> Sets, ClockSummary Clock, DateTime? WatermarkUtc)`; `Plan(int Revision, PlanBase Base, Tuning Tuning, ImmutableArray<VideoGroup> Groups, ImmutableArray<Boundary> Boundaries, ImmutableHashSet<ItemId> Included, ImmutableArray<Issue> Issues)` | `UasSort.Core` | 02.3 | 06–12 |
| `GroupDraft`, `ClusterResult` | records | `GroupDraft(GroupId Id, ImmutableArray<Item> Videos, GeoPoint? Centroid, DateOnly Start, DateOnly End, LibraryFolderRef? Wall, GroupId? UserSplitNeighbour)`; `ClusterResult(ImmutableArray<GroupDraft> Groups, ImmutableArray<Boundary> Boundaries)` | `UasSort.Core` | 02.3 | 06 |
| `PlanEdit` | closed record (JSON `t`, camelCase) | `"merge" Merge(ItemId InA, ItemId InB)`, `"splitBefore" SplitBefore(ItemId First)`, `"moveToNewGroup" MoveToNewGroup(ImmutableArray<ItemId> Items)`, `"moveToGroup" MoveToGroup(ImmutableArray<ItemId> Items, ItemId InTarget)`, `"rename" Rename(ItemId InGroup, string? Description, ImmutableArray<ItemId> PinnedMembers)`, `"retarget" Retarget(ItemId InGroup, TargetChoice Choice, bool ConfirmedBeforeFolderDate, ImmutableArray<ItemId> PinnedMembers)`, `"setIncluded" SetIncluded(ImmutableArray<ItemId> Items, bool Included)`, `"setDayIncluded" SetDayIncluded(DateOnly Day, bool Included)` | `UasSort.Core` | 02.3 | 06, 10, 11 |
| `TargetChoice` | closed record (JSON `t`) | `"auto" AutoTarget()`, `"newFolder" NewFolderTarget()`, `"appendTo" AppendTo(string FolderFullPath)`, `"skip" SkipTarget()` | `UasSort.Core` | 02.3 | 06, 10 |
| `RejectReason`, `Applied`, `Rejected`, `EditResult` | enum/records/union | `RejectReason { MergeAcrossLibraryFolders, MoveImportedItem, SplitAtGroupStart, RenameExistingFolder, RetargetOutsideVideoRoot, RetargetIntoReservedFolder, RetargetLaterDatedFolderUnconfirmed, ItemsNotFound }`; `Applied(Plan Plan)`; `Rejected(RejectReason Reason, string Message)`; `union EditResult(Applied, Rejected)` | `UasSort.Core` | 02.3 | 06, 10 |
| `DestRoot`, `CopyJob`, `VerifyMode`, `CopyPhase` | enums/record | `DestRoot { Video, Photo }`; `CopyJob(ItemId Item, string CardRelPath, long Size, DateTime CardMtimeUtc, DateTime CardCreationUtc, string DestPath, DestRoot Root, GroupId? Group, bool CreatesFolder)`; `VerifyMode { Unbuffered, Cached }`; `CopyPhase { CardCheck, Stat, CreateTemp, Copy, Flush, Verify, Finalize, Rename, Confirm, Ledger }` | `UasSort.Core` | 02.4 | 07, 09, 10 |
| `VerifyResult`, `RenameResult` | unions | `HashMatch(VerifyMode Mode)`, `HashMismatch(UInt128 Got, VerifyMode Mode)`, `union VerifyResult(HashMatch, HashMismatch)`; `Renamed`, `TargetExists`, `union RenameResult(Renamed, TargetExists)` | `UasSort.Core` | 02.4 | 07, 09 |
| `CopyOutcome` | closed record | `CopyOutcome(CopyJob Job)`: `Verified(Job, UInt128 Hash, VerifyMode Mode)`, `AlreadyThere(Job)`, `ConflictAtRename(Job)`, `ChangedOnCard(Job, long NowSize, DateTime NowMtimeUtc)`, `CardSwapped(Job, CardIdentity Now)`, `Failed(Job, CopyPhase Phase, string Error)`, `Cancelled(Job)`, `NotStarted(Job)` | `UasSort.Core` | 02.4 | 07, 08, 10 |
| `OffloadBatch`, `FolderPlan`, `StopReason`, `OffloadResult` | records/enum | `OffloadBatch(string RunId, CardIdentity Card, ImmutableArray<CopyJob> Jobs, ImmutableArray<ItemId> SeenIfNotCopied, ImmutableArray<FolderPlan> Folders)`; `FolderPlan(GroupId Group, string FullPath, bool Create, string Description, GeoPoint? Centroid, DateOnly Start, DateOnly End, string TzId)`; `StopReason { Cancelled, CardSwapped, CardRemoved, DestinationFull, DestinationLost, LedgerWriteFailed, InternalSafetyStop }`; `OffloadResult(string RunId, ImmutableArray<CopyOutcome> Outcomes, StopReason? Stop, DateTime StartUtc, DateTime EndUtc, ImmutableArray<string> VolumesNeedingSafeRemoval) { bool LedgerIncomplete { get; init; } }` (final fix F18) | `UasSort.Core` | 02.4 | 07–11 |
| `VolumeNeed`, `PreflightReport` | records | `VolumeNeed(string Volume, int Files, long Bytes, long FreeBytes, long RequiredFree)`; `PreflightReport(ImmutableArray<Issue> Issues, ImmutableArray<string> FoldersToCreate, ImmutableArray<(string Path, Confidence Confidence)> FoldersAppended, ImmutableArray<VolumeNeed> Volumes, ImmutableArray<string> StaleTemps, ImmutableArray<ItemId> AlreadyThere) { bool CanStart }` | `UasSort.Core` | 02.4 | 07, 10, 11 |
| `EjectResult` | union | `Ejected(string Volume)`, `EjectRefused(string Volume, string Reason)`, `union EjectResult(Ejected, EjectRefused)` | `UasSort.Core` | 02.4 | 09, 10 |
| `ScanPhase`, `ScanProgress`, `OffloadProgress` | enum/records | `ScanPhase { ListingCard, ListingLibrary, ReadingLedger, ReadingMetadata, BuildingPlan }`; `ScanProgress(ScanPhase Phase, int Done, int Total, string? Current)`; `OffloadProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, double MBps, TimeSpan? Eta, string? CurrentFile, CopyPhase Phase, GroupId? Group)` | `UasSort.Core` | 02.4 | 03, 06, 07, 10–12 |
| `AuditCategory`, `VerdictLevel`, `AuditLine`, `UnitAudit`, `FormatVerdict` | enums/records | `AuditCategory { VerifiedThisRun, InLedger, ConfirmedByYou, NameSizeMatch, SkippedByRule, AssumedByRule, Unaccounted }`; `VerdictLevel { Safe, SafeWithAssumptions, NotSafe }`; `AuditLine(string CardRelPath, long Size, AuditCategory Category, string Detail)`; `UnitAudit(ItemId Unit, AuditCategory Worst, ImmutableArray<AuditLine> Lines)`; `FormatVerdict(VerdictLevel Level, CardIdentity Card, string Headline, ImmutableDictionary<AuditCategory,int> Counts, int NameSizeOnly, int CachedVerifies, ImmutableArray<UnitAudit> Units, ImmutableArray<string> CardChanges, string? SafeRemovalNote)` | `UasSort.Core` | 02.4 | 05, 07, 08, 10, 11 |
| Cleanup request types | enums/records | `CleanupMode { BeforeDate, FreeSpace }`; `FreeSpaceKind { HaveFree, FreeUp }`; `FreeSpaceGoal(FreeSpaceKind Kind, long Bytes) { long TargetFreeBytes(CardSpace s) }`; `CleanupRequest(CleanupMode Mode, DateOnly? Before, FreeSpaceGoal? Goal, bool IncludeNotInLibrary)`; `CleanupEligibility { Evidence, NotInLibrary, Never }`; `EvidenceSource { Listed, HistoryOnly }`; `NotInLibraryReason { New, ProbablyImported, Conflict, Unfinished, Dismissed, RecordedAsImported, NoLongerInLibrary }`; `CardSpace(long FreeBytes, long TotalBytes, int ClusterBytes)` | `UasSort.Core` | 02.4 | 02, 08–10 |
| Cleanup plan inputs | records | `FileProof(string CardRelPath, FileKey Key, AuditCategory Category, string? ListedFolder, bool LedgerVerified, string? DecisionId)`; `CleanupCandidate(ItemId Unit, ImmutableArray<CardEntry> Files, long AllocatedBytes, DateTime CaptureUtc, DateOnly LocalDate, string TzId, CleanupEligibility Eligibility, AuditCategory Evidence, string Reason, TimeSpan? Duration, GeoPoint? Location, string? PlaceLabel, ItemKind Kind, DateTime LocalTime, NotInLibraryReason? NotInLibrary, string? SetFolder, SessionKey? Session, ImmutableArray<FileProof> Proofs, EvidenceSource? Source, bool TickedForOffload, ImmutableArray<CardEntry> NeverCopied)`; `CleanupKept(ItemId? Unit, ImmutableArray<string> CardRelPaths, long Bytes, DateTime? CaptureUtc, string Reason)`; `CleanupCutoff(DateOnly? BeforeDate, DateTime? LastCaptureUtc, DateTime? LastLocalTime, string? TzId, int FilesDeletedOnCutoffDay, int FilesOnCutoffDay, DateTime? FlightContinuesLocal)`; `FreeSpaceShortfall(long FreeableBytes, long HeldByNotInLibrary, long HeldByNever)`; `CleanupInputs(CardInventory Inventory, Plan Plan, FormatVerdict Audit, OffloadResult? Offload, CardSpace Space, LibraryListings FreshListings, LedgerSnapshot FreshLedger, VolumeInfo Volume, IPlaceIndex? Places, Settings Settings)`; `CleanupRows(ImmutableHashSet<ItemId> Keep, ImmutableHashSet<ItemId> Delete)`; `CleanupAck(string PlanFingerprint, bool CantBeRecovered, bool IncludesNotInLibrary, ImmutableHashSet<ItemId> NotInLibraryDelete)` | `UasSort.Core` | 02.4 | 08–10 |
| `CleanupPlan` | sealed partial class | `internal CleanupPlan(string planId, CardIdentity card, string cardRoot, string inventoryHash, string? cameraModel, CleanupRequest request, CardSpace spaceBefore, ImmutableArray<CleanupCandidate> delete, ImmutableArray<CleanupCandidate> notInLibraryInScope, CleanupRows rows, ImmutableHashSet<ItemId> undecided, ImmutableArray<CleanupKept> notDeletable, CleanupCutoff cutoff, int fileCount, long allocatedBytes, long expectedFreeAfter, FreeSpaceShortfall? shortfall, string fingerprint)`; properties `PlanId, Card, CardRoot, InventoryHash, CameraModel, Request, SpaceBefore, Delete, NotInLibraryInScope, Rows, Undecided, NotDeletable, Cutoff, FileCount, AllocatedBytes, ExpectedFreeAfter, Shortfall, Fingerprint`; Part 08 adds `ConfirmedCleanupPlan Confirm(CleanupAck ack, TimeProvider clock)` | `UasSort.Core` | 02.4 (+08.9) | 08–10 |
| `ConfirmedCleanupPlan` | sealed class | `internal ConfirmedCleanupPlan(CleanupPlan plan, Guid token, DateTime confirmedUtc)`; `CleanupPlan Plan; Guid Token; DateTime ConfirmedUtc; IReadOnlySet<string> FilePaths; IReadOnlySet<string> SetFolders; IReadOnlySet<ItemId> NotInLibraryConfirmed` (paths `PathRules.Join(plan.CardRoot, rel)`, OrdinalIgnoreCase) | `UasSort.Core` | 02.4 | 02, 08–10 |
| `CleanupOutcome` | closed record | `CleanupOutcome(ItemId Unit)`: `Deleted(Unit, int Files, long Bytes, bool SetFolderRemoved)`, `SkippedChanged(Unit, string CardRelPath, long? NowSize, DateTime? NowMtimeUtc)`, `SkippedEvidenceGone(Unit, string CardRelPath, string Why)`, `PartiallyDeleted(Unit, ImmutableArray<string> DeletedPaths, ImmutableArray<string> StillOnCard, string Why)`, `CleanupFailed(Unit, string CardRelPath, int Win32Error, string Error)`, `CleanupNotStarted(Unit)`, `CleanupCardSwapped(Unit, CardIdentity Now)` | `UasSort.Core` | 02.4 | 08, 10 |
| `CleanupStop`, `CleanupEnvironment`, `CleanupResult`, `CleanupProgress` | enum/records | `CleanupStop { OffloadLockHeld, LedgerUnavailable, Cancelled, CardSwapped, CardRemoved, WriteProtected, LedgerWriteFailed, InternalSafetyStop }`; `CleanupEnvironment(CardSource Source, CardIdentity Pinned, ICardReader Reader, IThumbnailSource Thumbnails, ICardEraserFactory Erasers, IDirectoryLister Lister, ILedgerStore Ledger, IOffloadLock Lock, IPowerRequest Power, TimeProvider Clock, Settings Settings)`; `CleanupResult(string RunId, ConfirmedCleanupPlan Plan, ImmutableArray<CleanupOutcome> Outcomes, CleanupStop? Stop, CardSpace SpaceAfter, ImmutableArray<string> StillListed, DateTime StartUtc, DateTime EndUtc, string? ClosingReadError = null /* Task 07fix: "Couldn't re-read E: after cleanup: …" when the closing Relist/Space failed */)`; `CleanupProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, string? CurrentFile, ItemId? Unit)` | `UasSort.Core` | 02.4 | 08, 10, 11 |
| Reports | records | `ReportLine(string CardRelPath, string? Dest, string Outcome, CopyPhase? Phase, string? Error, string? Xxh128, string? Verify)`; `OffloadReport(int V, string RunId, Settings SettingsSnapshot, string PlanSummary, ImmutableArray<ReportLine> Files, ImmutableArray<AuditLine> Audit, ImmutableArray<string> CardChanges, VerdictLevel Verdict, string Headline, StopReason? Stop)`; `CleanupReportLine(string CardRelPath, long Size, string Unit, string Outcome, string Eligibility, string Evidence, string Reason, int? Win32Error, bool LedgerRecorded)`; `CleanupReport(int V, string RunId, CardIdentity Card, CleanupRequest Request, CleanupCutoff Cutoff, ImmutableArray<CleanupReportLine> Files, ImmutableArray<CleanupKept> NotDeletable, CleanupStop? Stop, long FreeBefore, long FreeAfter, VerdictLevel VerdictAfter)` | `UasSort.Core` | 02.4 | 07–10 |
| `IPlanDeriver` | interface | `Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct)` | `UasSort.Core` | 02.3 | 06, 10, 12 |
| `UnsafeIoException` | class | `public class UnsafeIoException : Exception { (); (string message); (string message, Exception inner) }` (not an `IOException`; not sealed) | `UasSort.Core` | 02.4 | 02–12 |

### JSON (owner Part 02 Task 02.5)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `GeoPointJsonConverter` | converter | `sealed class GeoPointJsonConverter : JsonConverter<GeoPoint>` (`[lon,lat]`) | `UasSort.Core.Json` | 02.5 | 10 (registered in `MapJsonContext`), 12 |
| `ItemIdJsonConverter` | converter | `sealed class ItemIdJsonConverter : JsonConverter<ItemId>` (plain string) | `UasSort.Core.Json` | 02.5 | 05, 10 |
| `LedgerJsonContext` | JSON context | `sealed partial class LedgerJsonContext : JsonSerializerContext { JsonTypeInfo<LedgerRecord> LedgerRecord }` (compact, camelCase, enum names, `AllowOutOfOrderMetadataProperties`) | `UasSort.Core.Json` | 02.5 | 05, 09, 11 |
| `CoreJsonContext` | JSON context | `sealed partial class CoreJsonContext : JsonSerializerContext { Settings; Draft; PlanEdit; TargetChoice; OffloadReport; CleanupReport; GeoPoint }` (indented, camelCase, enum names) | `UasSort.Core.Json` | 02.5 | 05, 09–11 |
| `StackProofJsonContext` | JSON context | `Default.ProbeOutcome`, `Default.ListProbeOutcome` | `UasSort.Core.Json` | 01.5 | 01 |
| `ProbeOutcome`, `ProbeGps` (canaries) | closed record / union | `ProbeOutcome` (`t`): `ProbeCopied(string Path, long Bytes)`, `ProbeSkipped(string Reason)`, `ProbeConflict(string Existing, long ExistingSize)`; `ProbeFix(double Lat, double Lon)`, `ProbeNoFix(string Reason)`, `union ProbeGps(ProbeFix, ProbeNoFix)` | `UasSort.Core.StackProof` | 01.5 | 01 |

### Ports (namespace `UasSort.Core`, owner Part 02 Task 02.4)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `IVolumeProvider` | interface | `IReadOnlyList<VolumeInfo> GetVolumes()` | `UasSort.Core` | 02.4 | 06, 07, 09–12 |
| `IDirectoryLister` | interface | `ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames)` | `UasSort.Core` | 02.4 | 02, 05–12 |
| `ICardSourceValidator` | interface | `CardSourceCheck Validate(string chosenPath, VolumeInfo? detected, Settings s, IDirectoryLister lister, IPathFacts facts, string appDataDir)` | `UasSort.Core` | 02.4 | 10–12 |
| `IPathFacts` | interface | `string Canonical(string path); bool InSyncRoot(string canonicalPath); IReadOnlyList<string> SyncRoots()` | `UasSort.Core` | 02.4 | 02, 09, 11, 12 |
| `ICardReader` | interface | `CardIdentity CurrentIdentity(); Stream OpenRandom(string cardRelPath); Stream OpenSequential(string cardRelPath); FsEntry Stat(string cardRelPath); ListingResult Relist(); CardSpace Space()` | `UasSort.Core` | 02.4 | 03, 06–12 |
| `ICardReaderFactory` | interface | `ICardReader Open(CardSource source, CardIdentity identity)` | `UasSort.Core` | 02.4 | 06, 09–12 |
| `ICardEraserFactory` | interface | `ICardEraser Open(CardSource source, CardIdentity pinned, ConfirmedCleanupPlan plan)` | `UasSort.Core` | 02.4 | 08, 09, 11 |
| `ICardEraser` | interface | `ICardEraser : IDisposable { EraseResult DeleteFile(string cardRelPath); EraseResult RemoveEmptySetFolder(string cardRelDir); }` | `UasSort.Core` | 02.4 | 08, 09 |
| `EraseResult` | union | `EraseOk`, `EraseError(int Win32Error, string Message)`, `union EraseResult(EraseOk, EraseError)` | `UasSort.Core` | 02.4 | 08–10 |
| `IFileOps` | interface | `Stream CreateTemp(string finalPath, long size, out string tempPath); void FlushToDisk(Stream s); VerifyResult VerifyHash(string tempPath, long size, UInt128 expected, CancellationToken ct); void FinalizeAttributes(string tempPath, DateTime creationUtc, DateTime mtimeUtc); RenameResult RenameNoReplace(string tempPath, string finalPath); bool ConfirmFinal(string finalPath, long size); void FlushDestination(string dir, IReadOnlyList<string> filesCreatedThisRun); void DeleteOwnTemp(string tempPath); void EnsureDirectory(string dir, bool allowCreate); bool TryGetSize(string path, out long size); long FreeBytes(string anyPathOnVolume)` | `UasSort.Core` | 02.4 | 07, 09–11 |
| `ILedgerStore` | interface | `LedgerFolderStatus Check(); LedgerSnapshot Load(); void EnsureFolder(); ILedgerWriter OpenOwn(); void SnapshotToBackup(string runId); void KeepOnDevice(); void CopyInto(string newVideoRoot, LedgerSnapshot current)` | `UasSort.Core` | 02.4 | 06–12 |
| `ISettingsStore` | interface | `SettingsLoad Load(bool readOnly = false); void Save(Settings s)` | `UasSort.Core` | 02.4 | 09–12 |
| `IDraftStore` | interface | `Draft? Load(string cardKey); void Save(string cardKey, Draft d); void Delete(string cardKey)` | `UasSort.Core` | 02.4 | 09–11 |
| `IReportStore` | interface | `string Save(OffloadReport r); string Save(CleanupReport r)` | `UasSort.Core` | 02.4 | 07, 09–11 |
| `IAppAssets` | interface | `Stream OpenPlaces(); Stream OpenSelfTest(string name)` (+ `Stream OpenMapAsset(string)` only per decision 44) | `UasSort.Core` | 02.4 | 04, 09, 11, 12 |
| `IPowerRequest` | interface | `IDisposable KeepSystemAwake(string reason)` | `UasSort.Core` | 02.4 | 07–11 |
| `IOffloadLock` | interface | `IDisposable? TryAcquire()` (`Local\uas-sort-offload`; null only when another holder has it) | `UasSort.Core` | 02.4 | 07–11 |
| `IDeviceEject` | interface | `EjectResult Eject(string volumeRoot)` | `UasSort.Core` | 02.4 | 09–11 |
| `IShellLauncher` | interface | `void OpenFolder(string path); void OpenFile(string path); void OpenHttps(Uri uri)` | `UasSort.Core` | 02.4 | 09–11 |
| `ITimeZoneResolver` | interface | `TzLookup Resolve(GeoPoint p)` | `UasSort.Core` | 02.4 | 04, 06, 11, 12 |
| `IPlaceIndex` | interface | `IReadOnlyList<PlaceHit> Near(GeoPoint p, Distance r, PlaceClass cls, int max)` | `UasSort.Core` | 02.4 | 04, 06, 08, 11, 12 |
| `IThumbnailSource` | interface | `ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct); IDisposable Pause()` | `UasSort.Core` | 02.4 | 03, 07, 08, 10, 11 |
| `IUiDispatcher`, `DialogResult`, `DialogRequest`, `IDialogService` | interfaces/enum/record | `IUiDispatcher { bool HasThreadAccess { get; } void Post(Action a); }`; `DialogResult { Primary, Secondary, Close }`; `DialogRequest(string Title, string Body, string Primary, string? Secondary, string Close)`; `IDialogService { Task<DialogResult> ShowAsync(DialogRequest r); }` | `UasSort.Core` | 02.4 | 10, 11 |

### Guard (namespace `UasSort.Core`, owner Part 02)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `IoOp` | enum | `{ ReadData, AppendOwnLedger, CreateNew, CreateDir, SetPinned, SetAttributesOrTimes, Delete, Rename, OpenForFlush, CardDelete }` | `UasSort.Core` | 02.6 | 02, 07–09 |
| `GuardContext` | record | `GuardContext(string VideoRoot, string PhotoRoot, ImmutableArray<string> PreviousPhotoRoots, string? CardRoot, string AppDataDir, string Machine, IReadOnlySet<string> NewFolderDirs, IReadOnlySet<string> OwnTempsThisRun, IReadOnlySet<string> RenamedThisRun, string SystemVolumeRoot, bool CardIsVerifiedCardVolume, ConfirmedCleanupPlan? Cleanup)` | `UasSort.Core` | 02.6 | 02, 07–09 |
| `GuardDecision` | union | `GuardAllow`, `GuardUnsafe(string Reason)`, `GuardCloudOnly(string Path)`, `GuardHydration(string Path, uint Attributes)`, `union GuardDecision(GuardAllow, GuardUnsafe, GuardCloudOnly, GuardHydration)` | `UasSort.Core` | 02.6 | 08, 09 |
| `CardDeleteViolation` | record | `CardDeleteViolation(string Path, IoOp Op, string Reason)` | `UasSort.Core` | 02.6 | 02, 06–08 |
| `IoGuardPolicy` | static class | `static GuardDecision Check(IoOp op, string canonicalPath, uint? attributes, GuardContext ctx)`; `const uint FileAttributeReadOnly…FileAttributeRecallOnDataAccess`; `const string TempSuffix = ".uas-sort.tmp"`. Rules: SetPinned exempt from placeholder bits; Rename checked on the source; rule 1b refuses CardDelete on the system volume | `UasSort.Core` | 02.6–02.7 | 02, 07–09 |

### Card (namespace `UasSort.Core.Card`, owner Part 02)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `CardClassifier` | sealed partial class | `CardClassifier(TimeProvider clock)`; `CardInventory Classify(CardSource source, ListingResult listing)`; `static ImmutableHashSet<string> MediaExtensions`; `static string ComputeInventoryHash(IEnumerable<CardEntry> files)` | `UasSort.Core.Card` | 02.11 | 06, 08 |
| `CardDetector` | static partial class | `const string NotACard = "not a DJI card"`; `static IReadOnlyList<CardCandidate> Detect(IReadOnlyList<VolumeInfo> volumes, IDirectoryLister lister, Settings settings)`; `static CardCandidate? SingleDjiCard(IReadOnlyList<CardCandidate> candidates)` | `UasSort.Core.Card` | 02.12 | 10, 11 |
| `CardSourceValidator` | sealed class | `: ICardSourceValidator`; `const PickTopFolder`, `const NoDcim`, `const PartOfLibrary` | `UasSort.Core.Card` | 02.13 | 11, 12 |

### Media (namespace `UasSort.Core.Media`, owner Part 03)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `BlockCache` | sealed class | `BlockCache(Stream stream, int blockSize = 4096)`; `const int DefaultBlockSize`; `long Length`; `int BlockReads`; `byte[] ReadAt(long offset, int count)` | `UasSort.Core.Media` | 03.1 | 03 |
| `Mp4Box`, `Mp4Boxes` | record struct / static | `Mp4Box(string Type, long Offset, long Size, int HeaderSize, bool Truncated) { long Body; long End }`; `Walk(BlockCache, long start, long end)`, `Child(BlockCache, Mp4Box parent, string type)` | `UasSort.Core.Media` | 03.2 | 03 |
| `PbField`, `Protobuf` | record / static | per Part 03 summary (`MaxDepth = 12`, `TryReadVarint`, `Parse`, `DecodeTree`, `GetPath`, `FindProtocol`, `LooksLikeText`) | `UasSort.Core.Media` | 03.3 | 03 |
| `DjmdGpsPath`, `DjmdReading`, `DjmdDecoder` | records / static | per Part 03 summary (`ModelTable`, `IsFix`, `Decode`, `DecodeAt`) | `UasSort.Core.Media` | 03.5 | 03 |
| `SampleLocation`, `Mp4SampleTable`, `Mp4Thumb`, `Mp4MdatHead` | record struct / classes | per Part 03 summary | `UasSort.Core.Media` | 03.6–03.8 | 03 |
| `Mp4Probe` | static class | `const int MaxSampleBytes = 1 << 20`; `static Mp4Info Read(Stream s)`; `static IReadOnlyList<int> ProbeIndices(int sampleCount)` | `UasSort.Core.Media` | 03.9–03.10 | 03, 11 |
| `StillProbe` | static class | `static StillInfo Read(Stream s)` | `UasSort.Core.Media` | 03.11 | 03, 11 |
| `DroneStampParser` | static partial class | `static DateTime? FromFileName(string nameOrRelPath)`; `static string FileName(string nameOrRelPath)` | `UasSort.Core.Media` | 03.12 | 03 |
| `MetadataHarvester` | **static** class | `static IAsyncEnumerable<RawItem> HarvestAsync(CardInventory inventory, ICardReader reader, IProgress<ScanProgress> progress, CancellationToken ct)`; `static RawItem Harvest(MediaUnit unit, ICardReader reader)`; `static CardEntry FirstFrame(SetUnit set)` | `UasSort.Core.Media` | 03.12 | 06 |
| `ThumbLocation`, `ThumbnailReader` | record struct / class | `ThumbLocation(string CardRelPath, ByteRange Range)`; `ThumbnailReader(ICardReader reader, IEnumerable<RawItem> items) : IThumbnailSource, IDisposable`; `static ThumbLocation? Locate(RawItem item)`; `GetAsync`, `Pause`, `Dispose` | `UasSort.Core.Media` | 03.13 | 11 |

### Time (namespace `UasSort.Core.Time`, owner Part 04)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `Zones` | static class | `static bool TryFind(string? id, [NotNullWhen(true)] out TimeZoneInfo? zone)`; `static TimeZoneInfo Find(string id)`; `static string IanaId(TimeZoneInfo zone)` | `UasSort.Core.Time` | 04.4 | 06, 08, 10 |
| `ZoneNames` | static class | `Region(string ianaId)`, `ClockName(string ianaId)`, `Abbreviation(string ianaId, DateTime utc)`, `FormatOffset(TimeSpan offset)`, `FormatLocal(DateTime utc, string? ianaId)`, **`TimeSpan OffsetAt(string ianaId, DateTime utc)`**, **`bool IsUs(string ianaId)`** (the last two added, decision 26) | `UasSort.Core.Time` | 04.4 | 06, 08, 10 |
| `ClockConversion` | static class | `static readonly TimeSpan SampleWindow`; `ToUtc(ClockModel, DateTime droneStamp, string? siteZoneId) → (DateTime Utc, TimeSource Src)?`; `OffsetAt(ClockModel, DateTime, string?)`; `SourceFor(ClockMode)`; `Nearest(ImmutableArray<ClockSample>, DateTime, TimeSpan[]?)` | `UasSort.Core.Time` | 04.5 | 04, 05 |
| `DroneClock` | static partial class | `UsZones`; `ClockModel Learn(IEnumerable<RawItem> items, StoredClockMode settingMode, string settingZoneId, ITimeZoneResolver tz, TimeZoneInfo pc)`; `ImmutableArray<ClockSample> Samples(IEnumerable<RawItem>, ITimeZoneResolver)`; `TimeSpan Round15(TimeSpan)`; `ClockSummary Summarize(ClockModel clock, IReadOnlyList<ResolvedItem> items)`; `ImmutableArray<ClockChange> Changes(ImmutableArray<ClockSample>)`; `Settings ApplyLearned(Settings current, ClockModel clock)` | `UasSort.Core.Time` | 04.6, 04.9 | 06, 07, 10 |
| `TimeResolver` | **static** class | `static readonly TimeSpan NearbyWindow`; `static readonly Distance GeoNamesTzRadius`; `static ImmutableArray<ResolvedItem> Resolve(IReadOnlyList<RawItem> raw, ClockModel clock, ITimeZoneResolver tz, IPlaceIndex? places, TimeZoneInfo pc, DateTime nowUtc)` | `UasSort.Core.Time` | 04.7 | 06 |
| `TimeFlags` | static class | `MismatchThreshold`, `EarliestPlausibleUtc`, `AlternativesWindowMinutes`, `ClockWindowMinutes`, `IsClockMismatch(TimeSpan,TimeSpan)`, `MinutesFromMidnight(DateTime)`, `IsClockSource(TimeSource)`, `For(RawItem, ItemTime, TzLookup?, ClockModel, TimeZoneInfo, DateTime)` | `UasSort.Core.Time` | 04.8 | 04 |

### Geo (namespace `UasSort.Core.Geo`, owner Part 04)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `GeoMath` | static class | `const double EarthRadiusMeters = 6_371_008.8`; `static double MetersPerDegree`; `static Distance Haversine(GeoPoint a, GeoPoint b)`; `static GeoPoint Median(IReadOnlyList<GeoPoint> points)` | `UasSort.Core.Geo` | 04.1 | 06, 08, 10 |
| `GeoTimeZoneResolver` | sealed class | `: ITimeZoneResolver`; `TzLookup Resolve(GeoPoint p)`; `static bool IsEtc(string ianaId)` | `UasSort.Core.Geo` | 04.2 | 06, 11, 12 |
| `GpsPlausibility` | static class | `MaxFirstToLast`, `MaxFromCard`, `static GpsProbe Check(GpsFix first, GpsFix? last, TzLookup zone, IReadOnlyList<GeoPoint> cardPoints)` | `UasSort.Core.Geo` | 04.3 | 04 |
| `PlaceRecord`, `PlacesFormat` | record / static | `PlaceRecord(long GeonameId, string Name, double Lat, double Lon, PlaceClass Class, string? FeatureCode, long Population, string TzId)`; `Magic`, `Version = 1`, `PopulatedCode = 255`, `FeatureCodes`, `TryParseGeoNamesLine`, `Build`, `Write` | `UasSort.Core.Geo` | 04.10 | 04 (tool) |
| `PlaceIndex` | sealed class | `: IPlaceIndex`; `static PlaceIndex Load(Stream gz)`; `static Task<PlaceIndex> LoadAsync(IAppAssets assets, CancellationToken ct = default)`; `int Count`; `Near(GeoPoint, Distance, PlaceClass, int max)` | `UasSort.Core.Geo` | 04.10 | 11, 12 |

### Library (owner Part 05)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `LibraryIndex` members | partial class | `static LibraryIndex Build(LibraryListings listings, LedgerSnapshot ledger, ClockModel clock)`; `ImmutableArray<LibraryFile> Match(FileKey key)`; `ImmutableArray<LibraryFile> SameNameOtherSize(string normName, long size)`; `ImmutableArray<LibraryFolder> Folders`; `ImmutableArray<SetFolderListing> SetFolder(string name)`; `DateTime? WatermarkUtc` (decision 4); `ImmutableArray<string> UnavailableRoots`; `ImmutableArray<LibraryFile> Files`; `ImmutableArray<LibraryFile> FilesIn(LibraryFolderRef)`; `ImmutableHashSet<string> StartsFromMtime`; `ImmutableArray<(string Path, int Win32Error)> ListingErrors` | `UasSort.Core` | 05.11 | 06–10 |
| `EventFolderName` | static partial class | `static bool TryParse(string folderName, out DateOnly date, out string description)` | `UasSort.Core.Library` | 05.1 | 05 |
| `EventFolderLocator` | static class | `static LibraryFolderRef? For(string path, string videoRoot, IReadOnlyList<string> photoRoots, bool includeSelf)` | `UasSort.Core.Library` | 05.9 | 05 |
| `MemberStart`, `MemberStartResolver` | record struct / static | `MemberStart(DateTime Utc, bool FromMtime)`; `MaxMtimeAfterStart`; `TryStamp(string, out DateTime)`; `Resolve(string fileName, DateTime mtimeUtc, string? folderLedgerTz, ClockModel clock)` | `UasSort.Core.Library` | 05.10 | 05 |
| `LibraryEntry`, `CollectedLibrary`, `LibraryEntries` | records / static | `LibraryEntry(FsEntry Entry, RootListing Root)`; `CollectedLibrary(ImmutableArray<LibraryEntry> Files, ImmutableArray<LibraryEntry> Directories, ImmutableArray<string> UnavailableRoots, ImmutableArray<(string Path, int Win32Error)> Errors)`; `static CollectedLibrary Collect(LibraryListings listings)` | `UasSort.Core.Library` | 05.8 | 05 |

### Ledger (owner Part 05)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `LedgerCodec` | static class | `const int Version = 1`; `static string Serialize(LedgerRecord record)`; `static LedgerRecord? TryParse(string line, out string? error)` | `UasSort.Core.Ledger` | 05.3 | 09, 11 |
| `LedgerFileText`, `ParsedRecord`, `LedgerParseResult`, `LedgerParser` | records / static | `LedgerFileText(string FullPath, string Text)`; `ParsedRecord(string File, int Line, LedgerRecord Record)`; `LedgerParseResult(ImmutableArray<ParsedRecord> Records, ImmutableArray<LedgerParseIssue> Issues, ImmutableArray<string> SourceFiles)`; `static LedgerParseResult Parse(IReadOnlyList<LedgerFileText> sources)` | `UasSort.Core.Ledger` | 05.4 | 09 |
| `LedgerSnapshotBuilder`, `LedgerReader`, `LedgerSnapshots` | static classes | `static LedgerSnapshot Build(LedgerParseResult parsed, LedgerFolderStatus status)` (decision 5); `static LedgerSnapshot Read(IReadOnlyList<LedgerFileText> sources, LedgerFolderStatus status)`; `static LedgerSnapshot Empty(LedgerFolderStatus status)`; `static LedgerFolderStatus Detached(string folder, IEnumerable<string> files)` | `UasSort.Core.Ledger` | 05.5 | 06–10 |
| `LedgerFolderFacts`, `LedgerFolderStatusBuilder` | record / static | `LedgerFolderFacts(bool VideoRootExists, FsEntry? Folder, ListingResult? TopLevel, bool InSyncRoot, bool Writable)`; `const uint PinnedBit`, `const uint CloudBits`; `static LedgerFolderStatus Build(string videoRoot, string machine, LedgerFolderFacts facts)` | `UasSort.Core.Ledger` | 05.6 | 06 (fake), 09 |
| `LedgerLoader` | static class | `static LedgerSnapshot Load(LedgerFolderStatus status, Func<string, Stream> openRead)` (opens nothing for Missing, VideoRootMissing, CloudOnly) | `UasSort.Core.Ledger` | 05.7 | 06 (fake), 09 |
| `VideoRootChange` | static class | `static bool HasRecords(LedgerSnapshot)`; `static bool NeedsHistoryPrompt(LedgerFolderStatus newRootStatus, LedgerSnapshot current)`; `static int AppCopiedVideos(ListingResult newRootListing, string newVideoRoot, LedgerSnapshot current)` | `UasSort.Core.Ledger` | 05.14 | 10 |

### Config (namespace `UasSort.Core.Config`, folder `src/UasSort.Core/Settings/`, owner Part 05)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `SettingsDefaults` | static class | `const string StreetsStyleUrl, StreetsDarkStyleUrl, EsriUrl, UsgsUrl`; `static MapSettings Map()`; `static LayoutSettings Layout()`; `static Settings Derive(string picturesFolder)` | `UasSort.Core.Config` | 05.12 | 09–12 |
| `SettingsParse`, `SettingsCodec` | record / static | `SettingsParse(Settings? Settings, string? Error)`; `const int Schema = 1`; `static string Serialize(Settings)`; `static SettingsParse Parse(string json)` | `UasSort.Core.Config` | 05.12 | 09 |
| `SettingsLoadDecision`, `SettingsLoadPolicy`, `SettingsRecovery` | record / static | `SettingsLoadDecision(SettingsLoad Load, string? MoveCorruptTo)`; `static SettingsLoadDecision Decide(string settingsPath, string? fileText, bool readOnly, Settings derivedDefaults, DateTime nowUtc, Func<RunRoots?> rootsFromLastRun)`; `static RunRoots? RootsFromLastRun(IReadOnlyList<LedgerFileText> mirrors)` | `UasSort.Core.Config` | 05.13 | 09 |
| `SettingsEdits` | static class | `static Settings ChangePhotoRoot(Settings s, string newPhotoRoot)` | `UasSort.Core.Config` | 05.14 | 10 |

### Planning (namespace `UasSort.Core.Planning`, owner Part 06)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `Planner` | sealed partial class | `: IPlanDeriver`; `Planner(ITimeZoneResolver tz, IPlaceIndex? places, TimeZoneInfo pcZone, TimeProvider clock)`; `PlanBase Prepare(ScanResult scan)` (clock summary via `DroneClock.Summarize`); `Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct)`; `static IReadOnlyList<LibraryFolder> AppendCandidates(Plan plan, GroupId id, int max = 8)`; `internal static LibraryFolderRef FolderRefFor(string path, DateOnly fallback, LibraryIndex lib)` | `UasSort.Core.Planning` | 06.11, 06.12, 06.14 | 10–12 |
| `ScanService` | sealed class | `static readonly IReadOnlySet<string> LibraryExcludes`; `ScanService(Settings settings, IDirectoryLister lister, ILedgerStore ledger, IVolumeProvider volumes, ITimeZoneResolver tz, TimeZoneInfo pcZone, TimeProvider clock)`; `Task<ScanResult> ScanAsync(CardSource source, ICardReaderFactory readers, IProgress<ScanProgress> progress, CancellationToken ct)` | `UasSort.Core.Planning` | 06.21 | 11, 12 |
| `NewnessRules` | static partial class | `Newness Video(VideoUnit v, LibraryIndex lib, LedgerSnapshot ledger)`; `Newness Photo(MediaUnit unit, ItemTime t, LibraryIndex lib, LedgerSnapshot ledger, SetPlacement? placement, IReadOnlySet<DateOnly> newVideoDays, DateTime? watermarkUtc)`; `NearWatermarkWindow` | `UasSort.Core.Planning` | 06.4, 06.6 | 06 |
| `Clusterer`, `DaySplitFinder` | static classes | `IComparer<Item> Order`; `ClusterResult Cluster(IReadOnlyList<Item> videos, Tuning t, IReadOnlyList<PlanEdit> structuralEdits, out int missingEdits)`; `ImmutableArray<DaySplit> Find(IReadOnlyList<Item> groupVideos)` | `UasSort.Core.Planning` | 06.7, 06.8 | 06 |
| `FolderDecider` | sealed class | `FolderDecider(LibraryIndex lib, IReadOnlyList<GroupDraft> all, Tuning t)`; `static GroupTarget Decide(GroupDraft g, IReadOnlyList<GroupDraft> all, IReadOnlySet<ItemId> included, LibraryIndex lib, Tuning t, IReadOnlyDictionary<GroupId,GroupTarget>? pass1)`; `GroupTarget Decide(GroupDraft g, IReadOnlySet<ItemId> included, IReadOnlyDictionary<GroupId,GroupTarget>? pass1)`; `IReadOnlyList<LibraryFolder> AppendCandidates(GroupDraft g, int max)`; `SplitPoint`, `NewBeforeWallSplitPoint`, `BordersUserSplit`, `static Judged`, `static TargetFolder` | `UasSort.Core.Planning` | 06.9 | 06 |
| `PlanKeys`, `PlanningGeo`, `PlanText`, `PlanEditRefs` | static classes | per Part 06 Task 06.1 (`PlanningGeo`/`PlanText` zone and distance members delegate to `GeoMath`/`ZoneNames`, decision 45) | `UasSort.Core.Planning` | 06.1 | 06 |

### Editing and Naming (owner Part 06)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `PlanSession` | sealed class | `PlanSession(PlanBase b, IPlanDeriver deriver, Tuning tuning, TimeProvider clock)`; `Plan Current`; `Task<EditResult> ApplyAsync(PlanEdit, CancellationToken)`; `Task<EditResult> ApplyAllAsync(IReadOnlyList<PlanEdit>, CancellationToken)`; `void Preview(Tuning)`; `Task PreviewAsync(Tuning)`; `Task CommitTuningAsync()`; `Task<Plan> UndoAsync()`; `Task<Plan> RedoAsync()`; `void AcceptLedgerIssues()`; `Task AcceptLedgerIssuesAsync()`; `Draft ToDraft()`; `static PlanSession Resume(PlanBase, Draft, IPlanDeriver, out int dropped)` (+ `TimeProvider` overload); `event Action<Plan> Changed`; `Tuning CommittedTuning`; `IReadOnlyList<PlanEdit> Edits`; `int UndoDepth`; `bool CanUndo`; `bool CanRedo`; `Task WhenIdleAsync()` | `UasSort.Core.Editing` | 06.16 | 10 |
| `EditValidator` | static class | `static Rejected? Validate(Plan current, PlanEdit edit)`; `static bool IsUnder(string path, string root)` | `UasSort.Core.Editing` | 06.15 | 06 |
| `FolderNamer` | static class | `const int MaxDescription = 80`; `const int MaxTempPath = 400`; `const string TempSuffix`; `const string FolderExistsWhy = "Folder exists; appending"`; `Clean(string)`; `NewFolderRel(DateOnly, string)`; `TempPath(string)`; `ConflictName(string, Func<string,bool>)`; `TwinName(string, string)` | `UasSort.Core.Naming` | 06.3 | 06, 10 |
| `SetFolderNamer` | static class | `static SetPlacement Resolve(SetUnit set, Item first, LibraryIndex lib, LedgerSnapshot ledger, ISet<string> batchTaken, string photoRoot)` (final fix M 06.5: only `<photoRoot>\<name>` can be resumed) | `UasSort.Core.Naming` | 06.5 | 06 |
| `DescriptionSuggester` | static class | `const int Max = 6`; `static IReadOnlyList<Suggestion> Suggest(GroupDraft g, LibraryIndex lib, IPlaceIndex? places)`; `static bool Prefills(DescSource s)` | `UasSort.Core.Naming` | 06.10 | 06 |

### Offload (namespace `UasSort.Core.Offload`, owner Part 07)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `OffloadPaths` | static class | `TempSuffix`, `MaxTempPathLength`, `Join`, `FileName`, `DirectoryOf`, `TempOf`, `IsTemp`, `VolumeRoot`, `DriveLabel`, `Same`, `IsUnder`, `NormRel`, `NormName`, `Key(string pathOrName, long size)`, `WithCopyNumber`, `NewFolderDirs(string videoRoot, string folder)`, `Gb(long)` | `UasSort.Core.Offload` | 07.1 | 07 |
| `OffloadCompiler`, `JobMeta` | static partial / record | `static OffloadBatch Compile(Plan plan)`; `static OffloadBatch Compile(Plan plan, string runId)`; `static OffloadBatch Compile(Plan plan, string runId, CardIdentity card)` (final fix F5: CommitSession.Begin pins a Browse source's volume identity); `static ImmutableDictionary<string,JobMeta> Describe(Plan plan, OffloadBatch batch)`; `static ImmutableHashSet<string> NewFolderDirs(OffloadBatch batch, string videoRoot)`; `JobMeta(string Kind, DateTime? CaptureUtc, TimeSource? TimeSource, GeoPoint? Point, string? TzId, DateOnly? LocalDate, SessionKey? Session, string? Set)` | `UasSort.Core.Offload` | 07.2, 07.3 | 07, 09 |
| `Preflight` | static class | `static PreflightReport Check(OffloadBatch batch, Plan plan, IFileOps files, IDirectoryLister lister, ICardReader card, ILedgerStore ledger, IOffloadLock offloadLock, Settings settings)` | `UasSort.Core.Offload` | 07.5 | 07 (via `CommitSession`) |
| `AckKey`, `PreflightAcks` | record / static | `AckKey(IssueCode Code, ItemId? Anchor, string Message)`; `static AckKey Key(Issue)`; `static ImmutableArray<AckKey> Required(PreflightReport)`; `static bool CanStart(PreflightReport, IReadOnlySet<AckKey> acknowledged)` | `UasSort.Core.Offload` | 07.6 | 10 |
| `CopyEngine`, `CopyEngineOptions`, `OffloadRecords` | class / record / static | `CopyEngineOptions(string Machine, IReadOnlyDictionary<string,JobMeta> Meta, IReadOnlyList<VolumeInfo> Volumes)`; `CopyEngine(TimeProvider clock, CopyEngineOptions options)`; `const int ChunkBytes`; `Task<OffloadResult> RunAsync(OffloadBatch batch, ICardReader card, IFileOps files, ILedgerWriter ledger, IProgress<OffloadProgress> progress, CancellationToken ct)`; `OffloadRecords.File(...)`, `.Folder(...)`, `.NewId()`, `.Version` | `UasSort.Core.Offload` | 07.7 | 07 |
| `CommitTailRecords` | static class | `CardLeftovers(OffloadBatch, string machine)`; `Seen(OffloadBatch, Plan, OffloadResult, string machine, DateTime atUtc)`; `Run(OffloadBatch, Plan, OffloadResult, FormatVerdict, string machine, string appVersion)`; `RunCard Card(CardIdentity id, string? model, string inventoryHash)` | `UasSort.Core.Offload` | 07.11 | 07, 08 |
| `CardDiff`, `CardDiffResult`, `CardDiffEntry` | static / records | `CardDiffEntry(string RelPath, long Size)`; `CardDiffResult(ImmutableArray<CardDiffEntry> Added, Removed, Changed, int LastAccessOnly) { const ChangedDetail = "changed since scan"; const AddedDetail = "changed since scan (added)"; const RemovedDetail = "changed since scan (removed)"; bool IsEmpty; string? Touched(string relPath) }`; `static CardDiffResult Compare(IEnumerable<CardEntry> scanned, ListingResult relisted)` | `UasSort.Core.Offload` | 07.12 | 08 |
| `AuditCategorizer`, `AuditUnits`, `UnaccountedKind` | static / record / enum | `static AuditUnits Categorize(CardInventory, Plan, OffloadResult?, LedgerSnapshot, CardDiffResult)` | `UasSort.Core.Offload` | 07.13 | 07 |
| `CardAudit` | static class | `static FormatVerdict Audit(CardInventory inventory, ListingResult relisted, CardIdentity? now, Plan plan, OffloadResult? offload, LedgerSnapshot ledger, CardIdentity? pinned = null)`; `static ImmutableArray<string> ChangedPaths(FormatVerdict verdict)` | `UasSort.Core.Offload` | 07.14 | 08, 10, 11 |
| `VerdictText` | static class | `CameraName(string?)`, `Serial(CardIdentity)`, `CardName(CardInventory, CardIdentity)`, `Headline(...)` | `UasSort.Core.Offload` | 07.14 | 10 |
| `NotCopiedKind`, `NotCopiedRow`, `DecisionCheck`, `VerdictDecisions` | enum / records / static | `NotCopiedKind { Video, Photo, Set, Unknown }`; `NotCopiedRow(ItemId Unit, NotCopiedKind Kind, bool Truncated, DateOnly? LocalDate, int Files, long Bytes, AuditCategory Category, string Detail, bool CanDecide = true, string? CannotDecideReason = null /* Task 07fix: "Rescan the card first" for added/changed-since-scan and CardSwapped rows */)`; `DecisionCheck(bool Ok, string? Refusal)`; `NotCopied(FormatVerdict, Plan)`, `Day(IEnumerable<NotCopiedRow>, DateOnly)`, `Check(DecisionKind, IReadOnlyList<NotCopiedRow>)`, `Confirmation(DecisionKind, IReadOnlyList<NotCopiedRow>)`, `Records(DecisionKind, IReadOnlyList<NotCopiedRow>, Plan, string? runId, string machine, TimeProvider)`, `Revokes(IEnumerable<string> decisionIds, string machine, TimeProvider)`, `Apply(LedgerSnapshot, IEnumerable<DecisionRecord>, IEnumerable<RevokeRecord>)` | `UasSort.Core.Offload` | 07.15 | 10 |
| `OffloadReportBuilder` | static class | `const int Version = 1`; `Build(Plan, OffloadBatch, OffloadResult, FormatVerdict)`; `Line(CopyOutcome)`; `PlanSummary(OffloadBatch, Plan)` | `UasSort.Core.Offload` | 07.16 | 07 |
| `CommitEnvironment` | record | `CommitEnvironment(ICardReader Reader, IDirectoryLister Lister, ILedgerStore Ledger, IOffloadLock Lock, IPowerRequest Power, IThumbnailSource Thumbnails, IReportStore Reports, IVolumeProvider Volumes, Func<IReadOnlySet<string>, IFileOps> FileOpsForRun, TimeProvider Clock, string Machine, string AppVersion)` | `UasSort.Core.Offload` | 07.17 | 10, 11 |
| `CommitResult` | record | `CommitResult(OffloadBatch Batch, OffloadResult Offload, FormatVerdict Verdict, OffloadReport Report, string? ReportPath, bool LedgerComplete) { bool FailureFree }` | `UasSort.Core.Offload` | 07.17 | 10, 11 |
| `CommitSession` | sealed class | `static CommitSession Begin(Plan plan, CommitEnvironment env, string? runId = null)`; `Plan Plan`; `OffloadBatch Batch`; `PreflightReport Preflight`; `ImmutableArray<AckKey> RequiredAcks`; `Task<CommitResult> StartAsync(IReadOnlySet<AckKey> acknowledged, IProgress<OffloadProgress> progress, CancellationToken ct)`; `void Dispose()` | `UasSort.Core.Offload` | 07.17 | 10, 11 |

### Cleanup (namespace `UasSort.Core.Cleanup`, owner Part 08)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `CleanupPaths`, `CleanupKeys`, `CleanupFormat` | static classes | per Part 08 Task 08.1 | `UasSort.Core.Cleanup` | 08.1 | 08 |
| `CleanupVolumeCheck` | static class | `const string NotACard`, `const string WriteProtected`; `static string? Refusal(VolumeInfo volume, ListingResult card, Settings settings, string appDataDir)`; `static (string? Refusal, string? Detail) Evaluate(VolumeInfo volume, ListingResult card, Settings settings, string appDataDir)` (Detail names the failing rule and its facts; rule 4 = removable media on any bus, or Sd/Mmc; Task U4); `static ListingResult CardListing(IDirectoryLister lister, string root)` (the root's and MISC's children, RelPaths under the root; final fix F7) | `UasSort.Core.Cleanup` | 08.2 | 10, 11 |
| `FileFacts`, `FileVerdict`, `CleanupRules` | records / static | per Part 08 Task 08.3; `const string ChangedSinceScanDetail = CardDiffResult.ChangedDetail` | `UasSort.Core.Cleanup` | 08.3 | 08 |
| `FreshEvidence` | sealed class | `static FreshEvidence From(LibraryListings)`; `string? ListedFolder(FileKey)` | `UasSort.Core.Cleanup` | 08.4 | 08 |
| `CleanupPlanner` | static partial class | `static ImmutableArray<CleanupCandidate> Candidates(CleanupInputs inputs)`; `static ImmutableArray<CleanupKept> LooseFiles(CleanupInputs, ImmutableArray<CleanupCandidate>)`; `static CleanupPlan Build(CleanupInputs inputs, ImmutableArray<CleanupCandidate> candidates, CleanupRequest request, CleanupRows rows, IReadOnlySet<ItemId> firstShown)` | `UasSort.Core.Cleanup` | 08.4–08.6 | 10 |
| `CleanupFingerprint` | static class | `static string Compute(CleanupRequest request, CardSpace spaceBefore, IEnumerable<CleanupCandidate> delete)` | `UasSort.Core.Cleanup` | 08.5 | 08 |
| `CleanupRowOps` | static class | `static CleanupRows Set(CleanupRows rows, ItemId unit, bool delete)`; `static CleanupRows DeleteAll(CleanupPlan plan)`; `static CleanupRows KeepAll(CleanupPlan plan)` | `UasSort.Core.Cleanup` | 08.7 | 10 |
| `CleanupTexts` | static class | `CutoffLine(CleanupPlan)`, `NothingToDelete(CleanupPlan) → string?`, `ShortfallLine(CleanupPlan) → string?`, `EvidenceSplit(CleanupPlan)`, `NeverCopiesLine(CleanupPlan) → string?`, `VolumeName(string cardRoot)` | `UasSort.Core.Cleanup` | 08.8 | 10 |
| `CleanupExecutor` | static partial class | `static Task<CleanupResult> RunAsync(ConfirmedCleanupPlan confirmed, CleanupEnvironment env, IProgress<CleanupProgress> progress, CancellationToken ct)`; Win32 error constants | `UasSort.Core.Cleanup` | 08.11–08.12 | 10, 11 |

### Platform

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `NamedMutexLock` | sealed class | `static NamedMutexLock? TryAcquire(string name)`; `string Name`; `void Dispose()` | `UasSort.Platform.Win32` | 01.8 | 01, 09, 11 |
| `SingleInstance` | static class | `const string AppInstanceKey = "uas-sort"`; `const string MutexName = @"Local\uas-sort"` | `UasSort.Platform.Win32` | 01.8 | 01, 11 |
| `ForegroundWindow` | static class | `static bool AllowSetForeground(uint processId)`; `static bool BringToFront(nint hwnd)` | `UasSort.Platform.Win32` | 01.8 | 01, 11 |
| `PlaceholderMode` | static class | `const sbyte PhcmExposePlaceholders = 2`; `static sbyte ExposePlaceholders()` | `UasSort.Platform.Win32` | 01.8 | 01, 11, 12 |
| `CriticalErrorMode` | static class | `const uint SemFailCriticalErrors = 0x0001`, `SemNoOpenFileErrorBox = 0x8000`; `static uint FailQuietly()` (SetErrorMode, keeps other flags; returns the previous mode); `static uint Current()`; called by App and CLI Program.Main right after `ExposePlaceholders()` | `UasSort.Platform.Win32` | final fix (M 0910fixP) | 11, 12 |
| `SelfTestSandbox` | sealed **partial** class | Part 01: `const string FolderPrefix = "uas-sort-selftest-"`; `static SelfTestSandbox Create(string tempRoot)`; `string Root`; `string WebView2Folder`; `static void WriteResult(string path, ReadOnlySpan<byte> utf8Json)`; `bool TryDelete()`; `void Dispose()`. Part 11 adds: `static SelfTestSandbox Create()` (= `Create(Path.GetTempPath())`), `string AppDataDir`, `string VideoRoot`, `string PhotoRoot`, `string CardRoot`, `void WriteFile(string relativeToRoot, ReadOnlySpan<byte> content)`. Final fix F19: `static readonly TimeSpan BrowserExitWait` (5 s), `string? CleanupProblem`; `TryDelete()` first waits for, then terminates only, the msedgewebview2.exe processes whose --user-data-dir is under `Root` (`WebView2Processes.Stop`), and leaves the sandbox in place (with `CleanupProblem`) if one survives or the folder can't be deleted | `UasSort.Platform.Stores` | 01.9 (+11.3) | 01, 11 |
| `WebView2Processes` | static class | `const string BrowserProcessName = "msedgewebview2"`; `IReadOnlyList<Process> Under(string folder, string processName = BrowserProcessName)`; `int Stop(string folder, TimeSpan wait, string processName = BrowserProcessName)` (returns how many are still alive); `bool UserDataDirIsUnder(string commandLine, string folder)`; `string? CommandLine(int pid)` (NtQueryInformationProcess) | `UasSort.Platform.Win32` | final fix F19 | 11 |
| `WindowInterop` | static partial class | `nint Send(nint hwnd, uint msg, nint wParam, nint lParam)`; `nint CreateMessageOnlyWindow()`; `void DestroyWindow(nint hwnd)` (no foreground members) | `UasSort.Platform.Win32` | 11.3 | 11 |
| `WindowMessageHook` | static class | `IDisposable Attach(nint hwnd, uint message, Action<nint, nint> handler)`; `WM_DEVICECHANGE`, `DBT_DEVICEARRIVAL`, `DBT_DEVICEREMOVECOMPLETE` | `UasSort.Platform.Win32` | 11.3 | 11 |
| `PathFacts`, `KnownFolders` | class / static | `PathFacts : IPathFacts` (`new PathFacts()`); `KnownFolders.Pictures()`, `.AppDataDir()`, `.SystemVolumeRoot()` | `UasSort.Platform.Io` | 09.2 | 11, 12 |
| `PlaceholderGuard` | static class | `static uint? ReadAttributes(string fullPath)` only | `UasSort.Platform.Io` | 09.3 | 09 |
| `IoGate`, `CloudOnlyFileException`, `GuardContexts` | internal static / exception / static | `internal static uint? IoGate.Require(IoOp, string canonicalPath, GuardContext)`; `CloudOnlyFileException(string path) : IOException { string Path }`; `GuardContexts.For(Settings s, string appDataDir, string machine, IPathFacts facts, string? cardRoot = null, IEnumerable<string>? newFolderDirs = null, IReadOnlySet<string>? ownTemps = null, IReadOnlySet<string>? renamed = null)`; `GuardContexts.ForAppData(string appDataDir, string machine, IPathFacts facts)` | `UasSort.Platform.Io` | 09.3, 09.10 | 09, 11 |
| `WindowsDirectoryLister`, `WindowsVolumeProvider` | classes | `: IDirectoryLister` (`new()`); `: IVolumeProvider` (`new()`) | `UasSort.Platform.Io` | 09.4, 09.5 | 11, 12 |
| `GuardedFileOps` | sealed class | `GuardedFileOps(Settings settings, string appDataDir, string machine, IPathFacts facts, IReadOnlySet<string> newFolderDirs) : IFileOps` (set used verbatim, decision 27) | `UasSort.Platform.Io` | 09.7 | 11 |
| `WindowsCardReaderFactory`, `WindowsCardReader` | classes | `WindowsCardReaderFactory(Settings settings, string appDataDir, string machine, IPathFacts facts, IDirectoryLister lister) : ICardReaderFactory`; `WindowsCardReader : ICardReader { CardIdentity BoundIdentity; CardSource Source; string Root }` | `UasSort.Platform.Card` | 09.6 | 11, 12 |
| `WindowsCardEraserFactory`, `WindowsCardEraser` | classes | `WindowsCardEraserFactory(Settings settings, string appDataDir, string machine, IPathFacts facts, IDirectoryLister lister) : ICardEraserFactory` (+ internal test ctor with `IVolumeFacts`, `string systemVolumeRoot`); `WindowsCardEraser : ICardEraser` | `UasSort.Platform.Card` | 09.11 | 11 |
| `LedgerStore` | sealed partial class | `LedgerStore(Settings settings, string appDataDir, string machine, IPathFacts facts, IDirectoryLister lister, TimeProvider clock) : ILedgerStore`; `string Folder`; `string OwnFile`; `string BackupDir`; `LedgerSnapshot LoadFromBackup()` (Check via `LedgerFolderStatusBuilder`, Load via `LedgerLoader`, lines via `LedgerCodec`, decision 37) | `UasSort.Platform.Ledger` | 09.8–09.9 | 11, 12 |
| `SettingsStore`, `DraftStore`, `ReportStore` | classes | `SettingsStore(string appDataDir, string picturesFolder, string machine, IPathFacts facts, IDirectoryLister lister, TimeProvider clock)`, `static SettingsStore ForFile(string settingsFile, string appDataDir, string picturesFolder, string machine, IPathFacts facts, IDirectoryLister lister, TimeProvider clock)`; `DraftStore(string appDataDir, string machine, IPathFacts facts)`; `ReportStore(string appDataDir, string machine, IPathFacts facts, TimeProvider clock)` | `UasSort.Platform.Stores` | 09.10 | 11, 12 |
| `PowerRequest`, `OffloadLock`, `DeviceEject`, `ShellLauncher`, `AppAssets` | classes | `PowerRequest : IPowerRequest`; `OffloadLock : IOffloadLock` (wraps `NamedMutexLock` on `Local\uas-sort-offload`; internal ctor takes a name); `DeviceEject : IDeviceEject`; `ShellLauncher : IShellLauncher`; `AppAssets(string appDir, Assembly selfTestAssembly) : IAppAssets` | `UasSort.Platform.Shell` | 09.12 | 11, 12 |
| `FileLog` | sealed class | `FileLog(string appDataDir, string machine, IPathFacts facts, TimeProvider clock)`; `Info(string)`, `Warn(string)`, `Error(string, Exception? e = null)`, `Prune()` | `UasSort.Platform.Logging` | 09.12 | 11 |
| `ReadOnlyTextFile` | static class | `static string Read(string path, int maxBytes)` | `UasSort.Platform.Io` | 12.4 | 12 |
| `PlatformServices` | record | `PlatformServices(TimeProvider Clock, string AppDataDir, string Machine, IVolumeProvider Volumes, IDirectoryLister Lister, IPathFacts PathFacts, ICardSourceValidator Validator, ICardReaderFactory Readers, ICardEraserFactory Erasers, ISettingsStore Settings, IDraftStore Drafts, IReportStore Reports, IAppAssets Assets, IPowerRequest Power, IOffloadLock OffloadLock, IDeviceEject Eject, IShellLauncher Shell, Func<string, ILedgerStore> LedgerFor, Func<Settings, IReadOnlySet<string>, IFileOps> FileOpsFor, FileLog Log) { static PlatformServices Create(string appDataDir, TimeProvider clock); }` (writes nothing on creation) | `UasSort.Platform` | 11.3 | 11 |

### Review VMs (namespace `UasSort.Review`, owner Part 10; "add" = member Part 10 must add for Part 11)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `IReviewLog`, `IFreeSpace`, `ReviewServices` | interfaces / record | `IReviewLog { void Warn(string); void Info(string) /* Task U4 */ }`; `IFreeSpace { long? FreeBytes(string anyPathOnVolume) }`; `ReviewServices(IUiDispatcher Ui, IDialogService Dialogs, IShellLauncher Shell, IThumbnailSource Thumbs, IDraftStore Drafts, IFreeSpace Space, TimeProvider Time, IReviewLog Log)` | `UasSort.Review` | 10.1 | 11 |
| `Fmt` | static class | `Miles`, `Size`, `ClipLength`, `Day`, `DayWithWeekday`, `DayYear`, `DateRange`, `Gap`, `Offset`, `Clock`, `Count`, `Serial`, `ModelName`, `CardChip`, `Drive` (no `Round15`: use `DroneClock.Round15`) | `UasSort.Review` | 10.3 | 11 |
| `ClockText` | static class | `Banner(ClockSummary s, ClockModel model, IReadOnlyList<Item> items)` returns `s.Headline` (decision 11); `MismatchInfoBar`, `SourceText`, `SourceGlyph`, `IsEstimated`, `TimeCell`, `TimeTooltip`, `ChipTooltip`, `const WhyText` (all via Core `ZoneNames`) | `UasSort.Review` | 10.4 | 11 |
| `IKeyed`, `CollectionSync` | interface / static | `IKeyed { string Key }`; `Sync<TVm,TModel>(ObservableCollection<TVm> target, IReadOnlyList<TModel> source, Func<TModel,string> key, Func<TModel,TVm> create, Action<TVm,TModel> update)` | `UasSort.Review` | 10.5 | 11 |
| Map messages | closed records | `HostToMap` (`type`): `MapInit(MapConfig Config, string Base, double RadiusMiles, bool Online, string Theme)`, `MapSetData(int Rev, ImmutableArray<MapItem> Items, ImmutableArray<MapGroup> Groups, ImmutableArray<MapJump> Jumps)`, `MapSelect(string GroupId, ImmutableArray<string> ItemIds, bool Fit, ImmutableArray<double> Bbox)`, `MapSetRadius(double RadiusMiles)`, `MapSetBase(string Base)`, `MapSetTheme(string Theme)`, `MapFit(ImmutableArray<double> Bbox)`, `MapPing(int N)`; `MapToHost`: `MapReady(string Maplibre, bool Webgl2)`, `MapPong(int N)`, `MapClick(ImmutableArray<string> ItemIds, string? GroupId, bool Ctrl, bool Shift)`, `MapClickEmpty(…same…)`, `MapContextMenu(ImmutableArray<string> ItemIds, double X, double Y)`, `MapTileError(string? Base, string Message)`, `MapBaseUnavailable(string? Base, string Message)`, `MapError(string? Base, string Message)`; `MapConfig`, `MapItem`, `MapGroup`, `MapJump`; internal `MapJsonContext` (registers Core `GeoPointJsonConverter`) | `UasSort.Review` | 10.6 | 11 |
| `MapBridge`, `MapProjection` | class / static | `MapBridge(Action<string> post, IReviewLog log, TimeProvider time) : IDisposable`; `event Action<MapToHost>? Received`; `void Send(HostToMap)`; `void Dispatch(string json)`; `void SendData(Plan)`; `int Rev`; `static Serialize`, `static Parse`, `static ParseHostMessage`; `MapProjection.Palette`, `ImportedGrey`, `Color(VideoGroup)`, `Init(MapSettings, double radiusMiles, bool online, bool dark)`, `SetData(Plan, int rev)`, `Select(Plan, GroupId, IReadOnlyList<ItemId>, bool fit)`, `Bbox(IReadOnlyList<GeoPoint>)` | `UasSort.Review` | 10.6–10.7 | 11 |
| `IReviewActions`, `PlanIndex` | interface / class | `SetIncludedAsync(IReadOnlyList<ItemId>, bool)`, `SplitBeforeAsync(ItemId)`, `MergeAsync(ItemId, ItemId)`, `ApplyQuickFixAsync(QuickFix)`, `RenameAsync(GroupCardVm, string)`, `RetargetAsync(GroupCardVm, RetargetOptionVm)`; `PlanIndex(Plan)`: `Plan`, `Items`, `VideoRoot`, `Before(GroupId)`, `Candidates(VideoGroup)` (via `Planner.AppendCandidates`), `PhotoCounts(VideoGroup)` | `UasSort.Review` | 10.8 | 11 |
| Small review VMs | classes / enums | `ChipKind`, `RetargetKind { Auto, NewFolder, Append, Browse, Skip }`, `QuickFixVm(string label, Func<Task> run)` (`Label`, `Command`), `ChipVm(ChipKind kind, string text, string? tooltip, IReadOnlyList<QuickFixVm> actions)` (`Kind`, `Text`, `Tooltip`, `Actions`), `ThumbVm(ItemId key)` (`Key`), `SuggestionVm(Suggestion s)` (`Model`, `Text`, `Detail`), `RetargetOptionVm(RetargetKind kind, string label, string? detail, string? folderPath, DateOnly? folderDate)`, `BoundaryChipVm` (`Model`, `Text`, `ButtonText`, `CanMerge`, `MergeTooltip`, `MergeCommand`), `DaySplitBannerVm` (`Model`, `Text`, `IsEmphasised`, `EmphasisText`, `SplitCommand`) | `UasSort.Review` | 10.8 | 11 |
| `TimelineEntryVm`, `GroupCardVm`, `FoldedRunVm` | classes | `abstract TimelineEntryVm : ObservableObject, IKeyed`; `GroupCardVm(GroupId id, IReviewActions actions)`: `Id`, `Anchor`, `Group`, `Chip` (`BoundaryChipVm?`), `Swatch`, `Description` (settable), `DescriptionIsSuggestion`, `IsDescriptionReadOnly`, `ReadOnlyHint`, `Badge`, `ConfidenceText`, `TargetPath`, `DateRangeText`, `ZoneBadges`, `LocationText`, `VideoCountsText`, `PhotoCountsText`, `Thumbs` (`IReadOnlyList<ThumbVm>`), `MoreThumbsText`, `HasClockMismatch`, `Suggestions`, `Chips`, `CommitDescriptionCommand`, `NewFolderInsteadCommand`, `RetargetOptions()`; `FoldedRunVm(string key)`: `Groups`, `Text`, `ClipCount`, `IsExpanded` | `UasSort.Review` | 10.8 | 11 |
| `ClipRowVm` | class | `ClipRowVm(ItemId id, IReviewActions actions)`: `Id`, `ThumbKey`, `Key`, `Name`, `TimeText`, `TimeGlyph`, `TimeTooltip`, `SizeText`, `StatusText`, `StatusTooltip`, `DistanceText`, `IsIncluded`, `IsReadOnly`, `Banner`, `ToggleIncludedCommand`, `SplitBeforeCommand`, `Item` | `UasSort.Review` | 10.8 | 11 |
| `VideosTabVm` | class | `VideosTabVm(IReviewActions)`; `Timeline` (`ObservableCollection<TimelineEntryVm>`), `Clips`, `SelectedClipIds`, `SelectedEntry`, `Header`, `SelectedCard`, `MapSelectRequested`, `ClipSelectionChanged`, `ToggleFold(FoldedRunVm)`, `NextCard`, `CardFor(ItemId anchor)`, `SetSelectedClips(IReadOnlyList<ItemId>)`, `OnMapClick`, `IReadOnlyList<ItemId> OnMapContextMenu(MapContextMenu)`, `OnMapClickEmpty` | `UasSort.Review` | 10.9 | 11 |
| `IssueVm`, `IssuesVm`, `Footer` | classes / static | `IssueVm(Issue, Func<Issue,QuickFix,Task>)`: `Model`, `Severity`, `Code`, `Message`, `Anchor`, `Glyph`, `QuickFixes`; `IssuesVm(Func<Issue,QuickFix,Task> runFix)`: `Entries`, `BlockingCount`, `WarningCount`, `InfoCount`, `BlockedTooltip`, `HasBlocking`; `Footer.Text(Plan, IFreeSpace)` | `UasSort.Review` | 10.10 | 11 |
| `ITuningHost`, `TuningVm` | interface / class | `ITuningHost { void Preview(Tuning); Task CommitTuningAsync(Tuning); }`; `TuningVm(ITuningHost host, TimeProvider time, IUiDispatcher ui)`: `IdleCommit`, `RadiusMiles` (double), `GapDays` (int), `GroupCountText`, `RadiusText`, `GapText`, `ResetText`, `IsDragging`, `Current`, `Committed`, `ResetCommand`, `BeginDrag()`, `EndDragAsync()` | `UasSort.Review` | 10.11 | 11 |
| `DecisionTarget`, `DecisionTargets`, `IDecisionService`, `LedgerDecisionService` | record / static / interface / class | `DecisionTarget(CardEntry File, DateTime? CaptureUtc, string? Set)`; `DecisionTargets.For(Item)`, `.ForEntry(CardEntry)` (renamed from `FileKeys`; no `Of`); `IDecisionService { Record; Revoke; DecisionIdsFor }`; `LedgerDecisionService(ILedgerStore store, TimeProvider time, string machine)` | `UasSort.Review` | 10.12 | 11 |
| Photos / Other tabs | classes | `PhotoDayVm(DateOnly date, Func<PlanEdit,Task> apply)`: `Date`, `Key`, `Items`, `DayText`, `CountsText`, `StatusText`, `Reason`, `IsIncluded` (`bool?`), `IsAllImported`, `ToggleCommand`; `PhotoTileVm(Item, Func<PlanEdit,Task>, Func<IReadOnlyList<Item>,Task>)`: `Item`, `ThumbKey`, `Key`, `Text`, `StatusText`, `PairText`, `IsIncluded`, `CanUndo`, `ToggleCommand`, `UndoCommand`; `PhotosTabVm(Func<PlanEdit,Task> apply, Func<IReadOnlyList<Item>,Task> undoConfirmed)`: `Days`, `Tiles`, `SelectedDay`, `ShowImportedDays`, `Header`, `ShowImportedText`; `OtherRowVm(string text, string? detail, IAsyncRelayCommand? undismissCommand)`; `OtherSectionVm(string title, string? note, IReadOnlyList<OtherRowVm> rows, bool isCollapsed)`; `OtherTabVm(Func<IReadOnlyList<Item>,Task> undismiss, Func<IReadOnlyList<CardEntry>,Task> undismissEntries)`: `Sections`, `Header` | `UasSort.Review` | 10.12 | 11 |
| `InfoSeverity`, `InfoBarVm`, `DraftOffer`, `DraftOffers` | enum / classes | `InfoSeverity { Informational, Success, Warning, Error }`; `InfoBarVm(string key, InfoSeverity severity, string message, bool isClosable, IReadOnlyList<QuickFixVm> actions)`; `DraftOffer(Draft Draft, PlanSession Session, int Dropped)`; `DraftOffers.Find(PlanBase b, IDraftStore store, IPlanDeriver deriver, TimeProvider? clock = null)` | `UasSort.Review` | 10.13, 10.15 | 11 |
| `ReviewVm` | class | `ReviewVm(PlanSession session, ReviewServices services, IDecisionService decisions, DraftOffer? offer = null)`; `Session`, `Plan`, `Index`, `Videos`, `Photos`, `Other`, `Tuning`, `Issues`, `InfoBars`, `CardChipText`, `Map` (`MapBridge?`, settable), `SelectedTab`, `FooterText`, `CanOffload`, `OffloadDisabledReason`, `NothingNew`, `EmptyStateText`, `CanUndo`, `CanRedo` (mirror `Session`), `LastError`, `IsReadOnly`, `UndoCommand`, `RedoCommand`, `OffloadCommand` (`IRelayCommand`), `ShowVerdictCommand`, events `OffloadRequested`, `VerdictRequested`, `RescanRequested`, `FocusRenameRequested`, `UiActionRequested`, `MapContextMenuRequested`, `MapStatus`; `CloseInfoBar(string)`, `MergeSelectedWithNextAsync()`, `MoveSelectedToNewGroupAsync()`, `ToggleSelectedClipsAsync()`, `BrowseRetargetAsync(GroupCardVm, string?)`, `OpenClip(ItemId)`, `DraftDelay`, `Offer`, `DraftKey`, `ResumeDraftAsync()`, `DiscardDraftAsync()`, `FocusedClip`, `FocusedTile`, `HandleKey(ReviewKey, KeyMods, KeyFocus)`; **add:** `string MapBase` (observable; setter sends `MapSetBase`), `void GoTo(ItemId anchor)`, `IReadOnlyList<GroupCardVm> MoveTargets()`, `Task MoveSelectedToGroupAsync(GroupCardVm target)` (applies `MoveToGroup(selected, target.Anchor)`) | `UasSort.Review` | 10.13–10.16 | 11 |
| `ReviewKey`, `KeyMods`, `KeyFocus` | enums | `ReviewKey { Z, Y, M, N, S, D1, D2, D3, F2, F5, Enter, Space }`; `[Flags] KeyMods { None, Ctrl=1, Shift=2, Alt=4 }`; `KeyFocus { Other, TextBox, TimelineItem, ClipItem, PhotoItem }` | `UasSort.Review` | 10.16 | 11 |
| `LedgerStatusText`, `SetupVm`, `PreviousRootVm`, `SettingsPageVm` | static / classes | `LedgerStatusText.For(LedgerFolderStatus) → (string Text, InfoSeverity Severity, bool OfferKeepOnDevice)`; `SetupVm(SettingsLoad load, ISettingsStore store, Func<string,ILedgerStore> ledgerFor, IFreeSpace space)`: `VideoRoot`, `PhotoRoot`, `VideoFreeText`, `PhotoFreeText`, `LedgerStatus`, `LedgerSeverity`, `CanKeepOnDevice`, `CanConfirm`, `RecoveryText`, `PhotoInsideVideoNote`, `ConfirmCommand`, `KeepOnDeviceCommand`, `Confirmed`, `SetVideoRoot(string)`, `SetPhotoRoot(string)`; `PreviousRootVm(string path, IRelayCommand forgetCommand)`; `SettingsPageVm(...)`: `Current`, `PreviousPhotoRoots`, `VideoRoot`, `PhotoRoot`, `VideoFreeText`, `PhotoFreeText`, `LedgerFolder`, `BackupFolder`, `LedgerStatus`, `LedgerSeverity`, `CanKeepOnDevice`, `NoHistoryPrompt`, `RadiusMiles`, `GapDays`, `IsSiteLocal`, `ClockZone`, `CopyJpgTwin`, `MapBase`, `StreetsUrl`, `StreetsDarkUrl`, `SatelliteUrl`, `SatellitePresets`, `ClockLearnedText`, `AboutText`, `KeepOnDeviceCommand`, `OpenLedgerCommand`, `OpenBackupCommand`, `CopyLedgerCommand`, `StartEmptyCommand`, `SaveDelay`, `ChangeVideoRootAsync(string)`, `ChangePhotoRoot(string)` | `UasSort.Review` | 10.17 | 11 |
| `UiProgress<T>`, `CardRowVm`, `CardStageVm`, `ScanStageVm` | classes | `UiProgress<T>(IUiDispatcher ui, Action<T> onReport)`; `CardRowVm(CardCandidate c, Action<CardRowVm> use)`: `Candidate`, `Volume`, `Text`, `KindText`, `IsDjiCard`, `IsWriteProtected`, `UseCommand`; `CardStageVm(IVolumeProvider volumes, Func<IReadOnlyList<VolumeInfo>,IReadOnlyList<CardCandidate>> detect, Func<string,VolumeInfo?,CardSourceCheck> validate)`: `Rows`, `StatusText`, `Message`, `RescanCommand`, `CardChosen`, `Refresh()`, `Browse(string?)`; `ScanStageVm(Func<CardSource,IProgress<ScanProgress>,CancellationToken,Task<ScanResult>> scan, Func<ScanResult,PlanBase> prepare, Settings settings, IUiDispatcher ui)`: `PhaseText`, `Progress`, `IsIndeterminate`, `ErrorText`, `IsRunning`, `CancelCommand`, `RunAsync(CardSource)`, `static PhaseTextFor(ScanProgress, Settings)`; final fix: `CardStageVm.Refresh(bool autoChoose)` (false after a Cancel or a failed scan: never starts a scan by itself; F9), `Report(string message)`; `ScanStageVm` also catches `UnauthorizedAccessException` (F10) | `UasSort.Review` | 10.18 | 11 |
| `CommitPorts`, `AckVm`, `PreflightVm`, `CopyVm` | record / classes | `CommitPorts(IDraftStore Drafts, IDialogService Dialogs, IUiDispatcher Ui)`; `AckVm(AckKey key)`: `Key`, `Text` (= `Key.Message`), `IsChecked`; `PreflightVm(Plan plan, CardSource source, Func<Plan, CommitSession> begin, CommitPorts ports) : IDisposable`: `Open()`, `Session` (`CommitSession?`), `Batch`, `Report`, `Acks`, `Blocking`, `Warnings`, `Infos`, `FoldersToCreate`, `FoldersAppended`, `VolumeLines`, `CanStart` (= `PreflightAcks.CanStart`), `LockMessage`, `Plan`, `Source`, `BackCommand`, `BackRequested`, `Task<CommitResult> StartAsync(IProgress<OffloadProgress>, CancellationToken)`, `Dispose()` (= `Session.Dispose()`); `CopyVm(PreflightVm preflight, IDialogService dialogs, IUiDispatcher ui)`: `FilesText`, `BytesText`, `SpeedText`, `EtaText`, `CurrentText`, `PhaseText`, `Fraction`, `IsRunning`, `ErrorText`, `CancelCommand`, `Task<CommitResult?> RunAsync()`, `static PhaseName(CopyPhase)`. `CommitEngine` does not exist; final fix F12/F18: `CopyVm(PreflightVm, IDialogService, IUiDispatcher, IReviewLog? log = null)`, `WarningText`, `static string? StopTextFor(OffloadResult)`, `const string InternalSafetyStopTitle` (InternalSafetyStop: log + dialog) | `UasSort.Review` | 10.19 | 11 |
| `VerdictPorts`, `NotCopiedRowVm`, `NotCopiedDayVm`, `EjectVm`, `VerdictGroupRowVm`, `VerdictVm` | record / classes | `VerdictPorts(ILedgerStore Ledger, string Machine, TimeProvider Time, IDialogService Dialogs, IShellLauncher Shell, IDeviceEject Eject, Func<FormatVerdict> Reaudit)`; `NotCopiedRowVm(NotCopiedRow row, string text, Action<NotCopiedRowVm> toggle)`: `Row`, `Unit`, `Kind` (Core `NotCopiedKind`), `Text`, `Detail`, `Bytes`, `SizeText`, `LocalDate`, `IsPhotoLike`, `IsSelected`, `ToggleCommand`; `NotCopiedDayVm(DateOnly date, string text, IRelayCommand selectDayCommand)`; `EjectVm(string volume, IDeviceEject eject)`: `Volume`, `Text`, `EjectCommand`, `ResultText`; `VerdictGroupRowVm(string text, string path, IRelayCommand openFolderCommand)`; `VerdictVm(FormatVerdict verdict, Plan plan, OffloadResult? result, string? reportPath, VerdictPorts ports)`: `Level`, `Headline`, `LevelText`, `CategoryLines`, `CardChanges`, `SafeRemovalNote`, `SelectionText`, `CanCleanup`, `CleanupTooltip`, `StopText` (observable, internal set; ShellVm hands it `CopyVm.ErrorText`), `NotCopied`, `NotCopiedDays`, `Ejects`, `Groups`, `RecordImportedCommand`, `MarkNotNeededCommand`, `UndoCommand`, `OpenPhotoRootCommand`, `OpenReportCommand`, `CleanupCommand`, `DoneCommand`, `ShowPlanCommand`, `CleanupRequested`, `DoneRequested`, `ShowPlanRequested`, `static LevelName`, `static CategoryName`, `SetCleanupAvailability(bool, string?)`; final fix F18: `VerdictVm.HistoryWarning` (Warning InfoBar); Task U4: `SetCleanupAvailability(bool enabled, string? tooltip, string? unavailableText = null)`, `CleanupUnavailableText` (visible reason, null while enabled) | `UasSort.Review` | 10.20 | 11 |
| Cleanup VMs | records / classes | `CleanupContext(bool CommitRunning, bool ScanRunning, CardSource? Source, bool CardPresent, string? VolumeRefusal, string? VolumeRefusalDetail = null)`; `CleanupAvailability.For(CleanupContext) → (bool Enabled, string? Tooltip)`; `CleanupAvailability.Text(CleanupContext) → string?` (visible reason; Task U4); `RowDecision { Undecided, Keep, Delete }`; `CleanupRowVm(CleanupCandidate c, Action<CleanupRowVm,RowDecision> set)`: `Candidate`, `Unit`, `ThumbKey`, `Key`, `DateText`, `LengthText`, `LocationText`, `SizeText`, `ReasonText`, `Badge`, `Decision`, `Text`, `KeepCommand`, `DeleteCommand`; `CleanupResultVm(CleanupResult r, VerdictLevel after, string reportPath, string cardRoot, IDeviceEject eject)`: `HeadlineText`, `Problems`, `StillListed`, `VerdictText`, `StopText`, `ReportPath`, `Eject`, `SafeRemovalText`, `static DeletedTotals`; `CleanupPreparation(CleanupInputs? Inputs, string? BlockingText, bool OfferRescan)`; `CleanupEngine(Func<CleanupPreparation> Prepare, Func<ConfirmedCleanupPlan,IProgress<CleanupProgress>,CancellationToken,Task<CleanupResult>> Run /* = CleanupExecutor.RunAsync(c, env, p, ct) */, Func<Task<VerdictLevel>> Rescan, Func<CleanupReport,string> SaveReport, IDeviceEject Eject)`; `CleanupStep { Choose, Review, Confirm, Deleting, Result }`; `KeptGroupVm(string reason, IReadOnlyList<string> lines)`; `CleanupReports.Build(CleanupPlan, CleanupResult, VerdictLevel)`; final fix F17: `CleanupPreparation { Action? KeepOnDevice { get; init; } }`, `CleanupLedgerGate.Load(ILedgerStore, Settings) → (LedgerSnapshot? Ledger, CleanupPreparation? Refused)` | `UasSort.Review` | 10.21–10.22 | 11 |
| `CleanupVm` | class | `CleanupVm(CleanupEngine engine, IDialogService dialogs, IUiDispatcher ui, TimeProvider time)`: `Open()`, `PickDate(DateTimeOffset?)`, `Rows`, `Kept`, `Step`, `BlockingText`, `CanRescan`, `Mode`, `PickedDate`, `Before`, `FreeKind`, `GbValue`, `IncludeNotInLibrary`, `Plan`, `CardSummary`, `FreeNowText`, `WillDeleteText`, `CutoffText`, `ShortfallText`, `CanIncludeNotInLibraryFix`, `CountsText`, `FilesText`, `RangeText`, `FreeAfterText`, `EvidenceText`, `NeverCopiedText`, `NeverTouchedText`, `AckCantBeRecovered`, `AckNotInLibrary`, `ShowNotInLibraryAck`, `NotInLibraryAckText`, `DeleteButtonText`, `CanDelete`, `CanContinue`, `ProgressText`, `FirstUndecided`, `Result`, `CantBeRecoveredText`, `ContinueCommand`, `BackCommand`, `KeepAllCommand`, `DeleteAllCommand`, `IncludeNotInLibraryCommand`, `DeleteCommand`, `CancelCommand`, `RescanCommand`, `DoneCommand`, `Closed`, `RescanRequested`; final fix F17: `CanKeepOnDevice`, `KeepOnDeviceCommand` (pins, then prepares again) | `UasSort.Review` | 10.22 | 11 |
| `Stage`, `CleanupOrigin`, `ShellDeps`, `ShellVm` | enums / record / class | `Stage { Setup, Card, Scan, Review, Preflight, Copy, Verdict, Cleanup, Settings }`; `CleanupOrigin { Review, Verdict }`; `ShellDeps(SettingsLoad Settings, Func<SettingsLoad,SetupVm> CreateSetup, Func<CardStageVm> CreateCard, Func<Settings,ScanStageVm> CreateScan, Func<CardSource,PlanBase,ReviewVm> CreateReview, Func<ReviewVm,PreflightVm> CreatePreflight, Func<PreflightVm,CopyVm> CreateCopy, Func<ReviewVm,CommitResult?,VerdictVm> CreateVerdict, Func<CleanupOrigin,OffloadResult?,CleanupVm> CreateCleanup, Func<Settings,SettingsPageVm> CreateSettings, Func<ReviewVm,FormatVerdict> AuditNow, Func<CardSource,bool> CardPresent, Func<CardSource,(string? Refusal, string? Detail)> VolumeRefusal /* Task U4: was string? */, Action<Settings> SaveSettings /* add */) { IReviewLog? Log { get; init; } /* Task U4 */ }`; `ShellVm(ShellDeps deps)`: `Settings`, `Source`, `Card`, `Review`, `Preflight`, `Copy`, `Verdict`, `Cleanup`, `Stage`, `Current`, `CardChipText`, `CanRescan`, `CanOpenSettings`, `CanBrowse`, `CanUndoRedo`, `CleanupEnabled`, `CleanupTooltip`, `CleanupUnavailableText` (Task U4), `IsScanning`, `RescanCommand`, `SettingsCommand`, `CleanupCommand`, `StartAsync()`, `UseCardAsync(CardSource)`, `RescanAsync()`, `RescanForCleanupAsync()`, `BeginOffload()`, `StartCopyAsync()`, `OpenCleanup(CleanupOrigin)`, `OpenSettings()`, `CloseSettings()`, `DeviceChanged()`; **add:** `void UpdateLayout(double timelineWidth, double mapHeightRatio)`; final fix: `event Action<Exception>? Faulted` (F10), `string? RunId` (F16); a successful run saves `DroneClock.ApplyLearned` (F1); `CloseSettings` rescans a Review whose plan inputs changed (F11); `_lastResult` is only the shown verdict's (F13) | `UasSort.Review` | 10.23 | 11 |
| `Observed` | static class | `static void Forget(Task task, Action<Exception> onFault)` (awaits on the caller's context; a fault, never a cancellation, goes to onFault); `static void Run(Action callback, Action<Exception> onFault)` — the one helper for fire-and-forget tasks and raw dispatcher/timer callbacks (Ref §12 "log, keep running") | `UasSort.Review` | final fix F10/F14 | 10, 11 |
| `UnhandledText` | static class | `const string Title`, `const int MaxListed = 10`; `static IReadOnlyList<string> CopiedThisRun(LedgerSnapshot ledger, string runId)`; `static string Body(string message, IReadOnlyList<string>? copiedThisRun)` (Ref §12 dialog listing what this run copied) | `UasSort.Review` | final fix F16 | 11 |
| `LruCache<TKey,TValue>` | sealed class | `LruCache(int capacity)`; `Count`; `Capacity`; `bool TryGet(TKey, out TValue)`; `void Set(TKey, TValue)`; `void Clear()` | `UasSort.Review` | 11.2 | 11 |

### App (owner Part 01 then Part 11)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `Program` | static class | `[STAThread] static int Main(string[] args)`: ComWrappers → `PlaceholderMode.ExposePlaceholders()` → `LaunchOptions.Parse` → `SingleInstanceGate.Claim(options.ForceMutex)` unless `SelfTest` → `Application.Start` | `UasSort.App` | 01.10 (+11.4) | 13 |
| `LaunchOptions` | internal record | `LaunchOptions(bool SelfTest, string ResultPath, IReadOnlySet<string>? Only, bool ForceMutex, DateTime ProcessStartUtc)`; `static LaunchOptions Parse(IReadOnlyList<string> args, DateTime processStartUtc)`; consts `SelfTestFlag = "--selftest"`, `ResultFlag = "--result"`, `OnlyFlag = "--only"` (comma-separated), `ForceMutexFlag = "--single-instance-mutex"`, `DefaultResultFileName = "uas-sort-selftest-result.json"` (in `%TEMP%`) | `UasSort.App` | 01.10 (+11.4) | 11, 13 |
| `SingleInstanceGate` | internal sealed class | `static SingleInstanceGate Claim(bool forceMutex)`; `bool IsMain`; `string Mechanism`; `AppInstance? Key`; Part 11 adds `event Action? Activated` in the same file | `UasSort.App` | 01.10 (+11.4) | 11 |
| `App`, `MainWindow` | classes | `App.MainWindow`, `App.WebView2Folder`; `MainWindow`: `Task<double> FirstFrameMs`, `ShowOffScreen()`, `BringToFront()` (kept by Part 11), Part 11 adds `Services`, `Hwnd`, `Shell`, `DeviceWatcher`, `Start()`, `ApplyMinimumSize()`, `MinWidth = 1100`, `MinHeight = 700`; `ProbePage` removed with the probe page in Part 11 | `UasSort.App` | 01.10 (+11.4) | 11 |
| `SelfTestCheck`, `SelfTestResult`, `SelfTestJsonContext` | internal records / JSON context | `SelfTestCheck(string Name, string Status /* pass \| fail \| notApplicable */, string Detail)` with `static Pass(name, detail)`, `Fail(name, detail)`, `NotApplicable(name, detail)`; `SelfTestResult(bool Ok, double FirstFrameMs, IReadOnlyList<SelfTestCheck> Checks)`; `SelfTestJsonContext` (camelCase) | `UasSort.App.SelfTest` | 01.11 (+11.4) | 11, 13 |
| `MinimalSelfTest` | internal static class | Part 01 only; deleted by Part 11 Task 11.4 (replaced by `SelfTestRunner`) | `UasSort.App.SelfTest` | 01.11 | 01 |
| `SelfTestRunner`, `SelfTestChecks`, `SelfTestContext`, `SelfTestFixture` | classes | `static Task RunAsync(SelfTestContext ctx)`; `static List<(string Name, Func<SelfTestContext,Task<SelfTestCheck>> Run)> All`; `SelfTestFixture.Materialize(SelfTestSandbox)` | `UasSort.App.SelfTest` | 11.4, 11.9, 11.18 | 13 |
| `AppServices`, `CompositionRoot` | record / static | `AppServices(PlatformServices Platform, ShellVm Shell, ThumbnailCache Thumbnails, DialogService Dialogs, WinUiDispatcher Ui, string WebView2DataDir, SelfTestSandbox? Sandbox)`; `static AppServices Build(DispatcherQueue ui, Func<XamlRoot?> xamlRoot, SelfTestSandbox? sandbox)` (builds `ShellDeps`, `CommitEnvironment`, `CleanupEnvironment`; `NoVolumes` provider under selftest) | `UasSort.App` | 11.4 | 11 |
| `CardThumbnails` | sealed class | `: IThumbnailSource`; `void Use(ICardReader reader, IEnumerable<RawItem> items)` (disposes the previous `ThumbnailReader`) | `UasSort.App.Services` | 11.4 | 11 |
| App services | classes | `WinUiDispatcher : IUiDispatcher`, `DialogService : IDialogService`, `UiFormat`, `VisualTree`, `DeferredPlaceIndex : IPlaceIndex`, `FolderPickerService`, `KeyRouting`, `DeviceChangeWatcher`; final fix F14: `WinUiDispatcher(DispatcherQueue queue, Action<Exception> onFault)` (each posted callback runs through `Observed.Run`) | `UasSort.App.Services` | 11.4–11.17 | 11 |
| `ThumbnailCache`, `Thumb` | class / static | `ThumbnailCache(IThumbnailSource source, DispatcherQueue ui)`, `Capacity = 400`, `DecodeWidth = 96`; `Thumb.KeyProperty`, `GetKey`, `SetKey`, `Cache` | `UasSort.App.Controls` | 11.2 | 11 |
| `MapPane` | UserControl | `Host`, `ReadyTimeout`, `IsReady`, `IsUnavailable`, `MessageReceived`, `Ready`, `InitializeAsync(AppServices)`, `PostJson(string)`, `EvaluateAsync(string)`, `ShowUnavailable(string)`, `OpenInBrowserUri`, `Close()`, `Attach(MapBridge)`, `Detach()`, `ContextMenuRequested`, `FocusReturnRequested`, `static IsOnline()`, `ThemeName`; final fix F15: `StartTimeout`, `UnavailableText`, internal selftest seam `InitializeAsync(AppServices, Func<string, Task<CoreWebView2Environment>> createEnvironment, TimeSpan startTimeout)` (no Edge process starts); a start-up failure or hang shows the unavailable panel (never the crash dialog); F19: `static CloseAll()` (window Closed, selftest exit), internal `LiveBrowserProcessIds()` | `UasSort.App.MapHost` | 11.7–11.8 | 11 |
| Pages and controls | XAML types | `ShellPage`, `SetupPage`, `CardPage`, `ScanPage`, `ReviewPage`, `PreflightPage`, `CopyPage`, `VerdictPage`, `CleanupPage`, `SettingsPage`; `InfoBarList`, `TuningStrip`, `FooterBar`, `TimelineView`, `TimelineTemplateSelector`, `ClipListView`, `ClipMenu`, `PhotosTab`, `OtherTab`, `IssueList`; stage lookup keyed by Review `Stage` | `UasSort.App.Pages` / `.Controls` | 11.4–11.16 | 11 |

### CLI (namespace `UasSort.Cli`, owner Part 12)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `Program` | static class | `static int Main(string[] args)` (first calls `PlaceholderMode.ExposePlaceholders()`) | `UasSort.Cli` | 01.1 (+12.6) | 12 |
| `CliArgs` | internal record | `CliArgs(string Card, string? VideoRoot, string? PhotoRoot, double? RadiusMiles, int? GapDays, string? SettingsPath, bool Json, string? ExpectPath)`; `const string Usage`; `static bool TryParse(IReadOnlyList<string> args, out CliArgs? parsed, out string? error)` | `UasSort.Cli` | 12.1 | 12 |
| `PlanDocument` family, `CliJsonContext` | internal records / JSON context | per Part 12 Task 12.2 (`v:1`, camelCase, enum names) | `UasSort.Cli` | 12.2 | 12 |
| `PlanTextRenderer` | internal static class | `static void Render(PlanDocument doc, TextWriter w)`; `static string FormatDays(DateOnly start, DateOnly end)` (renamed from `PlanText`) | `UasSort.Cli` | 12.2 | 12 |
| Expect types | internal | `ExpectedFileJson`, `ExpectedFolderJson`, `ExpectDifferenceKind`, `ExpectDifference`, `ExpectReport { PassAt = 2 }`, `ExpectDiff.Compare/Write`, `ExpectedFile { MaxBytes = 1_000_000; Parse }` | `UasSort.Cli` | 12.3–12.4 | 12 |
| `ICliHost`, `PlanCommand`, `PlanDocumentMapper`, `StderrProgress`, `WindowsCliHost` | internal | `ICliHost { AppDataDir; LoadSettings; Validate; IdentityFor; ScanAsync; Plan; ReadExpectFile }`; `PlanCommand.RunAsync(IReadOnlyList<string>, TextWriter stdout, TextWriter stderr, ICliHost, CancellationToken) → Task<int>`; `PlanCommand.Effective(Settings, CliArgs)`; `PlanDocumentMapper.Map(Plan, CardIdentity?)` | `UasSort.Cli` | 12.5–12.6 | 12 |
| CLI command line | contract | `uas-sort-cli plan --card <path> [--video-root <path>] [--photo-root <path>] [--radius-mi <5..100>] [--gap-days <0..7>] [--settings <path>] [--json] [--expect <expected.json>]`; exit 0 / 1 / 2; writes nothing | — | 12.1 | 13 (README) |

### Testing (`tests/UasSort.Testing`, namespace `UasSort.Testing` unless noted)

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `RepoPaths` | static class | `Root`, `Of(string relativePath)`, `EnumerateFiles(string searchPattern, params string[] topFolders)`, `Relative(string fullPath)` | `UasSort.Testing` | 01.2 | 01, 09, 12 |
| `TestTempDir` | sealed class | `TestTempDir()` (`%TEMP%\uas-sort-test-<guid>`), `FullPath`, `Combine(params string[])`, `Dispose()` | `UasSort.Testing` | 01.2 | 01, 12 |
| `HydrationViolation`, `FakeFaults`, `FakeGuardCall` | classes / record | per Part 02 Task 02.8; Part 08 adds to `FakeFaults`: `Action<string>? OnCardDelete` | `UasSort.Testing` | 02.8 (+08.10) | 03–10 |
| `FakeFileSystem` | sealed class | per Part 02 Task 02.8 (`: IDirectoryLister`; `Context`, `Faults`, `DestinationFreeBytes`, `GuardLog`, `CardDeleteViolations`, `HydrationViolations`, `AssertNoViolations`, `AddDirectory`, `AddFile` ×2, `SetAttributes`, `GetAttributes`, `Exists`, `Metadata`, `PeekContent`, `RemoveUnguarded`, `AddCardVolume`, `SetCardIdentity`, `CardIdentityOf`, `CardSpaceOf`, `Guard`, `OpenRead`, `OpenAppend`, `CreateDirectory`, `SetPinned`, `Enumerate`); Part 08 adds `void Touch(string path, long? size = null, DateTime? mtimeUtc = null)` | `UasSort.Testing` | 02.8 (+08.10) | 04–10 |
| `FakeLayout` | static class | `VideoRoot`, `PhotoRoot`, `AppDataDir`, `CardRoot = @"E:\"`, `Machine = "DESKTOP-A"`, `SystemVolumeRoot = @"C:\"`, `CardId`, `CardSpace`, `Context(...)`, `Settings(...)`, `NewFileSystem()` | `UasSort.Testing` | 02.8 | 06–10 |
| `FakeCardReaderFactory`, `FakeCardReader` | classes | `FakeCardReaderFactory(FakeFileSystem fs) : ICardReaderFactory { OpenCount }`; `FakeCardReader(FakeFileSystem fs, string root, CardIdentity pinned) : ICardReader { Pinned; IdentityCalls }` | `UasSort.Testing` | 02.9 | 06–10 |
| `FakeCardEraserFactory`, `FakeCardEraser` | classes | `FakeCardEraserFactory(FakeFileSystem fs) : ICardEraserFactory { VolumeVerified; OpenCount }`; `FakeCardEraser : ICardEraser { Deleted; IsDisposed }`; Part 08 adds `IReadOnlyList<string> FakeCardEraserFactory.AllDeleted` and `ICardEraser OpenUnchecked(GuardContext ctx)` | `UasSort.Testing` | 02.9 (+08.10) | 08, 10 |
| `FakeFileOps` | sealed class | Part 02: `FakeFileOps(FakeFileSystem fs) : IFileOps { OwnTemps; Renamed; Flushed; CreatedDirectories; FlushToDiskCount }`; Part 07 adds (same file): ctor `FakeFileOps(FakeFileSystem fs, IEnumerable<string> newFolderDirs)`, `List<string> Calls`, `IReadOnlyList<(string Dir, int Files)> FlushedDestinations`, `Action<string,long>? OnTempWrite`, `Exception? ThrowOnFinalize`, `Exception? ThrowOnFlush` (Task 07fix), `Func<string,long>? FreeBytesOverride` | `UasSort.Testing` | 02.10 (+07.4) | 07, 10 |
| `FakePathFacts` | sealed class | `: IPathFacts { AddAlias(string from, string to); AddSyncRoot(string root) }` | `UasSort.Testing` | 02.13 | 09, 10 |
| `CountingStream`, `SyntheticMp4`, `SyntheticMp4Builder`, `SyntheticDng`, `SyntheticDngBuilder`, `MemoryCardReader`, `GoldenFolder` | classes / records | per Part 03 summary (`SyntheticMp4Builder`: `With…` + `Build()`/`BuildFile()`/`BuildSample`; `MemoryCardReader(CardIdentity)`; `GoldenFolder.Variable = "UASSORT_GOLDEN"`) | `UasSort.Testing` | 03.1, 03.4, 03.11, 03.12, 03.14 | 04, 06, 11, 12 |
| `FixturePoints`, `RawItemBuilder`, `FakeTimeZoneResolver` | static / class | per Part 04 Task 04.6 (`FakeTimeZoneResolver(ITimeZoneResolver fallback) { With(GeoPoint, TzLookup) }`) | `UasSort.Testing` | 04.6 | 06 |
| `FakePlaceIndex` | sealed class | `FakePlaceIndex(params IReadOnlyList<PlaceHit> places) : IPlaceIndex` | `UasSort.Testing` | 04.7 | 06, 08 |
| `FakeLedgerStore` | sealed class | `FakeLedgerStore(FakeFileSystem? fs /* null = in memory */, string videoRoot, string machine, LedgerSnapshot? snapshot = null) : ILedgerStore`; `string Folder`; `List<string> Calls`; `LedgerFolderStatus? StatusOverride`; `bool CheckThrows`; `Check()` (via `LedgerFolderStatusBuilder`), `Load()` (via `LedgerLoader` + `fs.OpenRead`); Part 07 adds (same file) `FakeLedgerWriter Writer`, `bool EnsureFolderThrows` and the write members | `UasSort.Testing` | 06.21 (+07.4) | 06–10 |
| `FakeLedgerWriter`, `FakeOffloadLock`, `FakePowerRequest`, `FakeThumbnails`, `MemReportStore`, `FakeVolumeProvider`, `ListProgress<T>` | classes | per Part 07 Task 07.4 (`FakeThumbnails` also counts active pauses and returns empty bytes from `GetAsync`) | `UasSort.Testing` | 07.4 | 07, 08, 10 |
| `OffloadPlanBuilder`, `OffloadRig`, `OffloadVolumes` | classes | per Part 07 Task 07.4 | `UasSort.Testing.Offload` | 07.2, 07.4 | 07, 10 |
| `GatedPlanDeriver`, `DeriveCall` | classes | Part 06: `GatedPlanDeriver(IPlanDeriver inner) : IPlanDeriver { Gate Arm(Func<Tuning,IReadOnlyList<PlanEdit>,bool> match); int CallCount; sealed class Gate { Task Entered; void Release(); } }`; Part 10 adds: `bool Hold`, `IReadOnlyList<DeriveCall> Calls`, `Task<DeriveCall> CallStartedAsync(int index)`, `void Release(int index)`, `void ReleaseAll()`, `DeriveCall { Index; Tuning; Edits; Flags; Revision; Held; Completed; Cancelled }` | `UasSort.Testing` | 06.16 (+10.1) | 06, 10 |
| `PlanFingerprint` | static class | `static string Of(Plan p)` | `UasSort.Testing` | 06.16 | 06, 10 |
| Planning scenario builders | classes | `Sites` (fields delegate to `FixturePoints`), `Clip`, `PlanScenario`, `ItemFactory`, `PlanAsserts`, `DecisionScenarios`, `InvariantScenario`, `BenchmarkScenario`, `ReplayFixture`, `ReplayEntry`, `ReplayClip`, `ReplayTruth` | `UasSort.Testing.Planning` | 06.2–06.20 | 06 |
| `CleanupPlanFixtures` | static class | `static ConfirmedCleanupPlan Confirmed(string cardRoot, CardIdentity card, IReadOnlyList<string> fileRelPaths, IReadOnlyList<string> setFolderRelDirs, TimeProvider clock)`; `static CleanupPlan Plan(string cardRoot, CardIdentity card, IReadOnlyList<string> fileRelPaths, IReadOnlyList<string> setFolderRelDirs)` (internal ctors via IVT) | `UasSort.Testing` | 08.9 | 09, 10 |
| `Disposer` | sealed class | `Disposer : IDisposable` (as Part 08 Task 08.10) | `UasSort.Testing` | 08.10 | 08, 10 |
| `FakeCardWriter` | sealed class | `FakeCardWriter(string cardRoot)`; `Root`; `RelPaths`; `AddDjiVideo(string mediaFolder, DateTime droneStamp, int number, GeoPoint? gps)`; `AddFile(string cardRelPath, byte[] content, DateTime? mtimeUtc = null)`; `Write()`; `DefaultMtimeUtc` | `UasSort.Testing` | 12.6 | 12 |
| `LedgerSamples` | static class | `static IReadOnlyList<string> AllRecordKindsV1()` (one `LedgerCodec.Serialize` line per record kind) | `UasSort.Testing` | 11.9 | 11 |
| `TestCleanupPlans` | internal static class | Core.Tests only (Part 02 guard tests; adapted to the 18-argument ctor) | `UasSort.Core.Tests.Support` | 02.4 | 02, 07 |
| `LedgerLines`, `TestLedger`, `LibraryFixture` | internal helpers | Core.Tests only | `UasSort.Core.Tests.Ledger` / `.Library` | 05 | 05–08 |
| `TempDir`, `TestEnv`, `Cmd`, `LedgerRecords` | Platform.Tests helpers | Platform.Tests only (`TempDir` adds deny-ACL cleanup; prefix `uas-sort-test-`) | `UasSort.Platform.Tests` | 09.1–09.8 | 09, 11, 12 |
| Review.Tests helpers | internal | `FakeUiDispatcher`, `FakeDialogService`, `FakeShellLauncher`, `FakeDraftStore`, `FakeFreeSpace`, `ListLog`, `Eventually`, `PlanClip` (renamed from `Clip`), `TestPlans`, `ScriptedDeriver`, `RecordingActions`, `ReviewHarness`, `FakeSettingsStore`, `FakeEjectOk`, `CleanupFixture` | `UasSort.Review.Tests` | 10.1–10.22 | 10 |

### Tools and scripts

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| Development prerequisites | installed toolchain | winget, Git for Windows, PowerShell 7.4+, .NET 11 SDK (11.0.100-rc.1.26425.128 or a later 11.0.1xx), Build Tools 2022 C++ workload (`Microsoft.VisualStudio.Workload.VCTools`), Windows SDK 10.0.26100, WebView2 runtime (reported); versions recorded in `docs/research/10-stack-proof.md` | — | 01.0 | all |
| `tools/r.sh` | bash wrapper (optional) | `tools/r.sh dotnet\|pwsh <args…>`; only for someone driving the build from WSL — the primary commands run natively on Windows | — | 01.1 | optional (WSL only) |
| `tools/build.ps1` | pwsh | `-File tools\build.ps1 [-CheckBannedApi]`; probe marker `// probe: <documentation id>` | — | 01.4 | 13 |
| `tools/run-selftest.ps1` | pwsh | `-Exe <path> [-Runs 2] [-MaxWarmFirstFrameMs 1000] [-BaselineMs 370]`; runs `--selftest --result <tmp>`; fails on any `status: fail`; prints `slowerThanBaseline` | — | 01.11 | 01 |
| `tools/selftest.ps1` | pwsh | `[-Only a,b] [-Configuration Debug] [-TimeoutSec 60]`; builds the App, runs `uas-sort.exe --selftest --result <tmp> [--only …]` | — | 11.4 | 11 |
| `tools/vendor-maplibre.ps1` | pwsh | `[-RenameToJs] [-Leaflet]` | — | 11.7 | 11 |
| `tools/places/build-places.cs` | file-based app | `dotnet run --file tools/places/build-places.cs -- --us <US.zip> --cities <cities5000.zip> --out <places.bin.gz>` | — | 04.11 | 04 |
| `tools/fixtures/make-selftest-assets.cs` | file-based app | `dotnet run tools/fixtures/make-selftest-assets.cs -- .`; `SelfTestAssets.All()` = `stack-exif.jpg` (01.7) + `selftest.dng`, `ledger-v1.jsonl`, `selftest-0001.mp4`, `selftest-0002.mp4`, `selftest-0003.mp4` (11.9) | — | 01.7 (+11.9) | 01, 11 |
| `tools/fixtures/make-replay-fixture.ps1` | pwsh | writes `tests/UasSort.Testing/Replay/library-listing.json`; record of how the fixture was produced; not run during the build | — | 06.19 | 06 |
| `tools/Deploy.psm1` | module | `Get-AppPublishConfig`, `Test-SelftestRun`, `Test-SelftestGate -Runs <object[]> [-MaxWarmMs 1000] [-BaselineMs 370]` (`SlowerThanBaseline` never changes `Ok`), `Invoke-SelftestProcess`, `Get-UasSortRid`, `Get-PeMachine`, `Get-FolderStats`, `Test-AotPublishOutput`, `Test-PathUnder`, `Assert-DeployTargetSafe`, `Copy-UasSortBuild`, `Get-VersionsToPrune`, `Remove-OldVersions`, `New-UasSortShortcut` | — | 13.1–13.4 | 13 |
| `tools/deploy.tests.ps1` | pwsh | `[-Name <wildcard>]`; cases in `tools/deploy-tests/*.Tests.ps1` | — | 13.1 | 13 |
| `tools/deploy.ps1` | pwsh | `-Version <x.y.z> [-AllowNoPlaceholders] [-Force] [-TimeoutSec 60]` | — | 13.5 | 13 |

### File artefacts

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `global.json`, `nuget.config`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `uas-sort.slnx` | repo files | values of the index Global Constraints; `RepoRoot`, `UasSortBannedList` (`main`/`platform`) properties | — | 01.1–01.3 | all |
| `BannedSymbols.txt`, `src/UasSort.Platform/BannedSymbols.Platform.txt` | analyzer lists | exactly the 73 Ref §2.4 IDs; Platform member list; allowed calls carry `#pragma warning disable RS0030 // IO layer: <why>` | — | 01.3 | 09 (tests reuse Part 01's content test) |
| `selftest-result.json` | JSON | `{ "ok": bool, "firstFrameMs": number, "checks": [ { "name": string, "status": "pass" \| "fail" \| "notApplicable", "detail": string } ] }`; only `placeholderVisibility` may be `notApplicable` | — | 13 (contract), written by 01.11 / 11.4 | 01, 11, 13 |
| `artifacts\deploy-<rid>.json` | JSON | `version`, `rid`, `commit`, `dirty`, `sizeMB`, `coldMs`, `warmMs`, `maxWarmMs` (1000), `baselineMs` (370), `slowerThanBaseline`, `installedTo`, `atUtc` | — | 13.5 | 13 |
| `src/UasSort.App/places.bin.gz` | binary | Ref §8.7 layout (`UPLC`, version 1), App `Content`/`PreserveNewest`; CLI copies it beside `uas-sort-cli.exe` | — | 04.11 (content item 11.4) | 11, 12 |
| `src/UasSort.App/SelfTest/*` | embedded resources `UasSort.App.SelfTest.<file>` | `stack-exif.jpg`, `selftest.dng`, `ledger-v1.jsonl`, `selftest-000{1,2,3}.mp4` | — | 01.7, 11.9 | 01, 11 |
| `src/UasSort.App/MapAssets/**` | web assets | `probe.html` (01.10; removed or kept unused by 11.8), `index.html`, `map.js` (protocol v1), `lib/manifest.json`, `fonts/` | — | 01.10, 11.7–11.8 | 11 |
| `<videoRoot>\.uas-sort\ledger-<MACHINE>.jsonl` | JSONL | one `LedgerRecord` per line (`LedgerCodec`) | — | 05 (format), 09 (store) | 06–12 |
| `%LOCALAPPDATA%\uas-sort\settings.json` | JSON | `CoreJsonContext.Default.Settings` via `SettingsCodec`; corrupt copy `settings.json.corrupt-yyyyMMdd-HHmmss` | — | 05, 09 | 10–12 |
| `%LOCALAPPDATA%\uas-sort\ledger-backup\…`, `drafts\{cardKey}.json`, `reports\yyyyMMdd-HHmmss-run8[-cleanup].json`, `logs\uas-sort-yyyyMMdd.log`, `WebView2\` | app data | per Part 09 Tasks 09.9–09.12, Part 11 map pane | — | 09, 11 | 10–12 |
| `tests/UasSort.Testing/Replay/library-listing.json` | embedded JSON | `UasSort.Testing.Replay.library-listing.json`; copied from the checked-in `docs/research/fixtures/library-listing.json` (pre-generated 2026-09-28, listing only) | — | 06.19 | 06 |
| `tests/acceptance/first-card-expected.schema.json`, `.example.json`, `README.md` | acceptance template | per Part 12 Task 12.4 | — | 12.4 | user |
| `docs/research/10-stack-proof.md` | record | `## Toolchain (Task 01.0)` section (installed tool versions), then the stack-proof measurements appended by 01.12; warm row "gate ≤ 1000 ms; baseline 370 ms; slowerThanBaseline: …" | — | 01.0 (creates), 01.12 (appends) | 13 |

## Required edits per part

### Index (`2026-09-27-uas-sort.md`)

1. Review Focus #1: replace "with all groups folded" by "with every AlreadyImported group folded (NothingToCopy groups follow the spec's fold rule and stay unfolded)".
2. Add under "How to use this plan": "Read `2026-09-27-uas-sort/00-interfaces.md` first; it wins over part text on names, namespaces, signatures and ownership."
3. File Structure: `tests/UasSort.Testing/` line becomes `FakeFileSystem.cs FakeLedgerStore.cs SyntheticMp4Builder.cs SyntheticDngBuilder.cs FakeCardWriter.cs GatedPlanDeriver.cs CleanupPlanFixtures.cs LedgerSamples.cs Planning/ Offload/ Replay/`; `src/UasSort.Core/` gains `Namespaces.cs GlobalUsings.Core.cs`; `src/UasSort.Platform/` gains `PlatformServices.cs`.
4. Completion criteria: append "a warm first frame slower than the 370 ms ReadyToRun baseline is reported in the completion summary (`slowerThanBaseline`), not a failure".

### Part 01 — Stack proof

1. Task 01.8: declare `SelfTestSandbox` (Task 01.9) as `public sealed partial class`; keep `NamedMutexLock`, `SingleInstance`, `ForegroundWindow`, `PlaceholderMode` exactly as specified (they are the only definitions; Parts 09/11 reuse them).
2. Task 01.10: `LaunchOptions` becomes `internal sealed record LaunchOptions(bool SelfTest, string ResultPath, IReadOnlySet<string>? Only, bool ForceMutex, DateTime ProcessStartUtc)`; replace `SelfTestResultFlag = "--selftest-result"` by `ResultFlag = "--result"`, add `OnlyFlag = "--only"` (comma-separated names, case-sensitive), keep `--selftest` and `--single-instance-mutex`, keep the `%TEMP%\uas-sort-selftest-result.json` default; update the parser tests (`--selftest --result <path>`, `--only a,b`, unknown flag rejected).
3. Task 01.10: Interfaces/Produces text and the `Program.Main` comment: `--selftest-result <path>` → `--result <path> [--only …]`.
4. Task 01.11: `SelfTestCheck(string Name, string Status, string Detail)` with statics `Pass`, `Fail`, `NotApplicable`; `SelfTestResult(bool Ok, double FirstFrameMs, IReadOnlyList<SelfTestCheck> Checks)`; delete the `V`/`Kind` members and the `Kind = "stack-proof"` constant; `Ok` = no check has `Status == "fail"`. Every `new SelfTestCheck(name, true/false, …)` becomes `SelfTestCheck.Pass/Fail(name, …)`. `MinimalSelfTest.RunAndExitAsync` skips checks not in `Only` when `Only` is set. Update the expected JSON in the step text to `{"ok":true,"firstFrameMs":312,"checks":[{"name":"probePage","status":"pass","detail":"…"},…]}`.
5. Task 01.11: `tools/run-selftest.ps1` passes `'--selftest', '--result', $res`; parameters `-MaxWarmFirstFrameMs` (default 1000) and `-BaselineMs` (default 370); a run fails when its exit code is non-zero, `ok` is false or any check has `status` `fail`; after the runs print `slowerThanBaseline: True|False` (warm > baseline) without failing.
6. Task 01.12: every `-MaxWarmFirstFrameMs 370` → `-MaxWarmFirstFrameMs 1000`; expected output "warm value ≤ 1000"; the record table row reads "`--selftest` run 2 (warm) `firstFrameMs` \| (value) — gate ≤ 1000 ms; ReadyToRun baseline 370 ms; slowerThanBaseline: (True/False)".
7. Task 01.12 Stop rule and the part intro: a warm first frame over 1000 ms is a failed concrete check; slower than 370 ms (but ≤ 1000 ms) is recorded as `slowerThanBaseline` in `docs/research/10-stack-proof.md` and reported to the user in the completion summary; it does not stop Part 02.
8. Produces summary: update the `LaunchOptions`, selftest contract and `run-selftest.ps1` lines to items 2–6; note "Part 11 extends `SelfTestSandbox`, `LaunchOptions`, `SingleInstanceGate`, `SelfTestCheck`/`SelfTestResult`/`SelfTestJsonContext` and `tools/fixtures/make-selftest-assets.cs` in place".
9. Task 01.2: `UasSort.Testing.csproj` and every test csproj reference `Microsoft.Extensions.TimeProvider.Testing`; test csprojs carry `<Using Include="Xunit" />`.
10. (Applied 2026-09-28.) Task 01.0 (install development prerequisites) precedes Task 01.1; Task 01.12 appends to the `docs/research/10-stack-proof.md` that Task 01.0 creates; the App and BannedApi.Probe csprojs use `Platforms=x64` and the App `RuntimeIdentifiers=win-x64`; `tools/r.sh` is optional (WSL only) and every primary command runs natively on Windows.

### Part 02 — Core model and guard

1. Task 02.1: create `src/UasSort.Core/Namespaces.cs` and the identical `GlobalUsings.Core.cs` in the nine projects listed under "Namespaces and GlobalUsings", plus `tests/UasSort.Testing/GlobalUsings.cs` and `tests/UasSort.Core.Tests/GlobalUsings.cs` with the exact content above; drop per-file `using UasSort.Core;`/`using System.Collections.Immutable;` lines that the global usings now cover (optional, harmless if kept).
2. Task 02.1: add `public static FileKey OfPath(string pathOrName, long size)` to `FileKey` (takes the text after the last `/` or `\`, then `Of`), with a test for `"DCIM/DJI_001/X (2).MP4"` and `@"C:\a\x.mp4"`.
3. Task 02.3: comment on `LedgerSnapshot.Files` becomes "strongest verify per key (Unbuffered > Cached > NameSize), latest among equals".
4. Task 02.4: `CleanupPlan`'s internal ctor gains `string inventoryHash, string? cameraModel` directly after `cardRoot` (18 parameters) and the properties `public string InventoryHash { get; }`, `public string? CameraModel { get; }`; update `TestCleanupPlans` accordingly.
5. Task 02.4: `UasSort.Core.csproj` gets both `<InternalsVisibleTo Include="UasSort.Core.Tests" />` and `<InternalsVisibleTo Include="UasSort.Testing" />`.
6. Task 02.4 summary text: `ConfirmedCleanupPlan` (with its `PathRules.Join` derivation) stays here; Part 08 adds only `CleanupPlan.Confirm` and `CleanupPlanner.Build`.
7. Task 02.11: delete `FixedTimeProvider` and its tests; every use becomes `new FakeTimeProvider(utcNow)` (`Microsoft.Extensions.Time.Testing`), and `x.UtcNow = t` becomes `x.SetUtcNow(t)`.
8. Tasks 02.8–02.10 notes: `FakeFileOps`, `FakeFaults`, `FakeFileSystem`, `FakeCardEraserFactory` are the only fakes of their kind; Parts 07 and 08 add members in these files (registry Testing table), so keep them one class per file.
9. Produces summary: list `FileKey.OfPath`, `Namespaces.cs`, `GlobalUsings.Core.cs`, both IVTs, the 18-argument `CleanupPlan` ctor; remove `FixedTimeProvider`; replace the gap-note about Part 01 canaries with "Part 01 canaries stay in `UasSort.Core.StackProof`".

### Part 03 — Media probes

1. Adopt `GlobalUsings.Core.cs` (explicit `using UasSort.Core;` lines may stay; no namespace changes: `UasSort.Core.Media`, test helpers `UasSort.Testing`).
2. Notes for later parts: "Part 11 owns selftest.dng" → "Part 11 extends Part 01's `tools/fixtures/make-selftest-assets.cs` (adds `selftest.dng` and the MP4s); this part never creates that file".
3. Notes for later parts, Parts 10–11 bullet: the per-scan `ThumbnailReader` sits behind Part 11's `CardThumbnails` facade (decision 34).

### Part 04 — Time and geo

1. Every `using UasSort.Core.Model;` and `using UasSort.Core.Ports;` is deleted (model and ports are `UasSort.Core`, imported by `GlobalUsings.Core.cs`); test files keep `using UasSort.Testing;` only if not global (Core.Tests has it globally).
2. Task 04.5: the `ClockModel` partial goes in a new file `src/UasSort.Core/Time/ClockModel.Conversion.cs` declaring `namespace UasSort.Core; public sealed partial record ClockModel { … }` (do not edit Part 02's `Model/Clock.cs`).
3. Task 04.4: add to `ZoneNames` `public static TimeSpan OffsetAt(string ianaId, DateTime utc)` (`Zones.Find(ianaId).GetUtcOffset(utc)`, UTC for unknown ids) and `public static bool IsUs(string ianaId)` (true for the seven `DroneClock.UsZones` plus `America/Phoenix`, `America/Anchorage`, `Pacific/Honolulu`), each with a test.
4. Task 04.7: `FakePlaceIndex` constructor becomes `FakePlaceIndex(params IReadOnlyList<PlaceHit> places)`.
5. Summary "Spec gaps" item 4 and the gap-log assumption: replace with "Model and ports are in `UasSort.Core` (Part 02); resolved".
6. Produces summary: add the two `ZoneNames` members and note "Parts 06 and 10 use `FakePlaceIndex`, `ZoneNames`, `GeoMath` from here; they do not define their own".

### Part 05 — Library, ledger, settings

1. Task 05.5 (`LedgerSnapshotBuilder`): for each `FileKey` keep the record with the strongest `Verify` (`Unbuffered` > `Cached` > `NameSize`), latest `AtUtc` among equals; add test `LedgerSnapshot_LaterNameSizeRecord_DoesNotDowngradeVerified`; update gap item 9 to "decided: strongest verification wins".
2. Task 05.11 (`LibraryIndex.Build`): the watermark counts only videos whose `EventFolderLocator.For(path, videoRoot, photoRoots, includeSelf: false)` is non-null and that have no ledger `file` record; replace test `LibraryIndex_Watermark_LiteralRule_CountsNonEventVideosByMtime` by `LibraryIndex_Watermark_IgnoresVideosOutsideEventFolders` (an `Exports\` clip and a loose `2026\x.MP4` with later mtimes leave the watermark unchanged); extend `LibraryIndex_ReviewFocus_NonEventFoldersAndLooseFiles` with that assertion; update gap item 5 to "decided (user): event-folder videos only — mention in the completion summary".
3. Namespaces: keep `UasSort.Core.Library`, `UasSort.Core.Ledger`, `UasSort.Core.Config`; `LibraryIndex`/`LibraryFolder` partials stay `namespace UasSort.Core;`.
4. Summary gap item 1: replace the cross-part name list with "names reconciled in `00-interfaces.md`".
5. Summary: add "Part 06 creates `UasSort.Testing.FakeLedgerStore` on top of `LedgerFolderStatusBuilder` and `LedgerLoader`; Part 09 `LedgerStore` uses the same two".

### Part 06 — Planning and editing

1. Header conventions table: namespaces of Part 02 types are `UasSort.Core` (not `.Model`/`.Ports`); `LibraryIndex` is `UasSort.Core`; `TimeResolver` is static; `MetadataHarvester` is static; `CardClassifier` takes a `TimeProvider`; replace every `using UasSort.Core.Model;`/`using UasSort.Core.Ports;` by nothing (global usings).
2. Task 06.1: `PlanningGeo.EarthRadiusM` → `=> GeoMath.EarthRadiusMeters`, `PlanningGeo.Haversine` → `GeoMath.Haversine`; `PlanText.ZoneAbbrev` → `ZoneNames.Abbreviation`; `IssueCode.NothingNew` appended to Part 02's enum (as written).
3. Task 06.2: delete this part's `FakePlaceIndex` (use Part 04's `UasSort.Testing.FakePlaceIndex`); `Sites` fields become `=> FixturePoints.<Name>` (same names).
4. Task 06.11: `new TimeResolver().Resolve(...)` → `TimeResolver.Resolve(...)`; delete `Planner.SummarizeClock` and build `PlanBase.Clock` with `DroneClock.Summarize(scan.Clock, resolved)`.
5. Task 06.16: move `GatedPlanDeriver` to `tests/UasSort.Testing/GatedPlanDeriver.cs`, namespace `UasSort.Testing`, rename its `int Calls` to `int CallCount`; keep `PlanFingerprint` in `UasSort.Testing` (flat) too.
6. Task 06.21: `new CardClassifier()` → `new CardClassifier(_clock)`; `new MetadataHarvester().HarvestAsync(...)` → `MetadataHarvester.HarvestAsync(...)`; the test uses `new FakeCardReaderFactory(fs)` (the fake FS is not itself an `ICardReaderFactory`), `fs.Opened` → `fs.GuardLog.Where(c => c.Op is IoOp.ReadData or IoOp.AppendOwnLedger).Select(c => c.Path)`, and `fs.LedgerStore(VideoRoot, "PC1")` → `new FakeLedgerStore(fs, VideoRoot, "PC1")`.
7. Task 06.21: create `tests/UasSort.Testing/FakeLedgerStore.cs` with the registry signature: `Check()` = `StatusOverride ?? LedgerFolderStatusBuilder.Build(videoRoot, machine, facts from fs listings/attributes)` (throws `IOException` when `CheckThrows`), `Load()` = `LedgerLoader.Load(Check(), p => fs.OpenRead(p))` (or the in-memory snapshot when `fs` is null); `EnsureFolder`, `OpenOwn`, `SnapshotToBackup`, `KeepOnDevice`, `CopyInto` throw `NotSupportedException` until Part 07; each call appends its name to `Calls`.
8. Summary table: `UasSort.Testing.Planning` loses `FakePlaceIndex` and `GatedPlanDeriver`; add `UasSort.Testing.FakeLedgerStore`, `UasSort.Testing.GatedPlanDeriver`, `UasSort.Testing.PlanFingerprint`; gaps list items about instances/`FakeFileSystem` guesses marked resolved.
9. Review Focus #1 text in Tasks 06.12/06.14 and the summary: "every AlreadyImported group folded; NothingToCopy groups never fold".
10. Task 06.19: copies the pre-generated `docs/research/fixtures/library-listing.json` (resolved 2026-09-28; applied in the part file); the script is not run during the build.

### Part 07 — Offload and audit

1. Task 07.4: delete `UasSort.Testing.Offload.FakeFileOps`; extend Part 02's `tests/UasSort.Testing/FakeFileOps.cs` in place with the members in the registry (ctor overload with `newFolderDirs` overriding `fs.Context.NewFolderDirs`, `Calls`, `FlushedDestinations`, `OnTempWrite`, `ThrowOnFinalize`, `FreeBytesOverride`); in tests `Files.Flushed` (tuple list) → `Files.FlushedDestinations`, `RenamedThisRun` → `Renamed`; Part 02's FakeFileOps tests stay green.
2. Task 07.4: delete `FakeOffloadLedgerStore`; complete `UasSort.Testing.FakeLedgerStore` (Part 06) in place: implement `EnsureFolder` (guarded `CreateDirectory` + `SetPinned` when `fs` is set; throws when `EnsureFolderThrows`), `OpenOwn` (returns `Writer`, a `FakeLedgerWriter` appending through `fs.OpenAppend` when on the file system), `SnapshotToBackup`, `KeepOnDevice`, `CopyInto`; rename every `FakeOffloadLedgerStore` use to `FakeLedgerStore` (same ctor order: fs, videoRoot, machine, snapshot).
3. Task 07.4: move `FakeLedgerWriter`, `FakeOffloadLock`, `FakePowerRequest`, `FakeThumbnails` (add `ActivePauses` and an empty-bytes `GetAsync`), `MemReportStore`, `FakeVolumeProvider`, `ListProgress<T>` to namespace `UasSort.Testing` (files directly under `tests/UasSort.Testing/`); `OffloadPlanBuilder`, `OffloadRig`, `OffloadVolumes` stay in `UasSort.Testing.Offload`.
4. Task 07.5 rule 4 note: delete "the Platform lock must be re-entrant within the process"; replace with "`CommitSession` passes Preflight a stand-in lock while it holds the real one (Task 07.17), so no re-entrancy is needed".
5. Task 07.14: keep the signature and the `changed since scan` prefix exactly (Part 08 binds `CardDiffResult.ChangedDetail`).
6. Summary "For Part 10": add "Part 10 builds `PreflightVm` on `CommitSession` (no `CommitEngine`), and its tests use `OffloadRig`/`OffloadPlanBuilder` plus the shared fakes"; "For Part 09": `GuardedFileOps` takes the `NewFolderDirs` set verbatim.
7. Summary table Testing row: update names and namespaces per items 1–3.

### Part 08 — Card cleanup

1. Conventions block: model, ports, guard and `UnsafeIoException` are `UasSort.Core` (no `using UasSort.Core.Model/Ports/Guard`); other Core namespaces come from `GlobalUsings.Core.cs`.
2. Tasks 08.5 and 08.9: do not redeclare `CleanupPlan` or `ConfirmedCleanupPlan`. `CleanupPlan.Confirm` goes in `src/UasSort.Core/Cleanup/CleanupPlan.Confirm.cs` as `namespace UasSort.Core; public sealed partial class CleanupPlan { public ConfirmedCleanupPlan Confirm(CleanupAck ack, TimeProvider clock) … }`; `CleanupPlanner.Build` calls the 18-argument ctor (passing `inputs.Inventory.InventoryHash`, `inputs.Inventory.CameraModel`, and the computed `fileCount`, `allocatedBytes`, `expectedFreeAfter`). Delete this part's `CanonicalFile`/`CanonicalSetFolder` or make them internal helpers that call `PathRules.Join` (same result as Part 02's derivation); delete the "delete that shell" instruction.
3. Task 08.5: delete `src/UasSort.Core/Cleanup/InternalsVisibleTo.Cleanup.cs` (IVT is in the csproj, Part 02).
4. Task 08.3: `public const string ChangedSinceScanDetail = CardDiffResult.ChangedDetail;`.
5. Task 08.9 (or 08.10): add `tests/UasSort.Testing/CleanupPlanFixtures.cs` (namespace `UasSort.Testing`) with `Confirmed(string cardRoot, CardIdentity card, IReadOnlyList<string> fileRelPaths, IReadOnlyList<string> setFolderRelDirs, TimeProvider clock)` and `Plan(...)` built through the internal ctors (IVT), plus a test that its `FilePaths`/`SetFolders` equal the expected canonical paths.
6. Task 08.10: delete `CleanupFakeCard`, `CleanupFakeLister`, `CleanupFakeLedgerStore`, `CleanupFakeLock`, `CleanupFakePower`, `CleanupFakeThumbs` and the `UasSort.Testing.Cleanup` namespace. Use: `FakeFileSystem` + `FakeLayout` (card volume with `CardSpace` for cluster size/free bytes; lister), `FakeCardReader`, `FakeCardEraserFactory` (`VolumeVerified`; `FakeFaults.CardRemoved`, `DeleteErrors`, `DeletePending`, `IdentityOnCall`), `FakeLedgerStore`, `FakeOffloadLock`, `FakePowerRequest`, `FakeThumbnails`. Add in place to Part 02's files: `FakeFileSystem.Touch(...)`, `FakeFaults.OnCardDelete`, `FakeCardEraserFactory.AllDeleted`, `FakeCardEraserFactory.OpenUnchecked(GuardContext)`. Knob mapping: `Identity` → `fs.SetCardIdentity`; `Removed` → `Faults.CardRemoved`; `WriteProtected` → the `CardSource.IsWriteProtected` passed to `Open`; `VerifiedVolume` → `VolumeVerified`; `Opens` → `fs.GuardLog` filtered on `ReadData`; `ErasersOpened` → `OpenCount`; `Deleted`/`RemovedDirs` → `AllDeleted` (folders end in `/`). `Disposer` moves to `UasSort.Testing`.
7. `CleanupScenario.MakeCard/MakeLister/MakeLedgerStore` return the fakes of item 6; `CleanupScenario.Confirmed(...)` may delegate to `CleanupPlanFixtures`.
8. Summary: Testing section rewritten per items 5–7; G3 → "resolved: Part 02's ctor carries `InventoryHash`/`CameraModel`"; G8 → "resolved: Part 02 fakes"; G14 → "namespaces per `00-interfaces.md`".

### Part 09 — Platform

1. Cross-part contract table: `UnsafeIoException` is `public class UnsafeIoException : Exception` in `UasSort.Core`; `SettingsDefaults` is in `UasSort.Core.Config`; `UasSortJsonContext` → `LedgerJsonContext.Default.LedgerRecord` / `LedgerCodec.Serialize` for ledger lines and `CoreJsonContext.Default.Draft/OffloadReport/CleanupReport` for stores; `CleanupPlanFixtures` is Part 08's `UasSort.Testing` class with the listed signature.
2. Task 09.1: modify (do not replace) `src/UasSort.Platform/UasSort.Platform.csproj` and `tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj`, keeping Part 01's items (`UasSortBannedList=platform`, `AllowUnsafeBlocks`, existing test files and references); add `InternalsVisibleTo UasSort.Platform.Tests`.
3. Task 09.1: `src/UasSort.Platform/GlobalUsings.cs` and `tests/UasSort.Platform.Tests/GlobalUsings.cs` get exactly the registry content (no `UasSort.Core.Model/Ports/Guard/Settings`); keep `Namespaces.cs` markers for `Card`, `Io`, `Ledger`, `Logging`, `Shell`, `Stores`.
4. Task 09.3: delete `Win32/NtDll.cs` and `PlaceholderGuard.ExposePlaceholders`; `PlaceholderGuard` keeps only `ReadAttributes`; text "called by `Program.Main` and the CLI" → "they call Part 01's `PlaceholderMode.ExposePlaceholders()`".
5. Task 09.7: `GuardedFileOps(Settings settings, string appDataDir, string machine, IPathFacts facts, IReadOnlySet<string> newFolderDirs)` uses the set verbatim (canonicalised, OrdinalIgnoreCase), no ancestor expansion; composition passes `OffloadCompiler.NewFolderDirs(batch, videoRoot)` through `CommitEnvironment.FileOpsForRun`; Preflight's instance gets an empty set; adjust the tests to pass the full dir set.
6. Task 09.8: `LedgerStore.Check()` collects `LedgerFolderFacts` (video root exists, folder `FsEntry`, top-level listing, `InSyncRoot`, `Writable` via `AccessProbe`) and returns `LedgerFolderStatusBuilder.Build(videoRoot, machine, facts)`; `Load()` returns `LedgerLoader.Load(Check(), openRead)` where `openRead` goes through `IoGate` (so a cloud-only file opens nothing, per Ref §13).
7. Task 09.9: `LedgerWriter.Append` writes `LedgerCodec.Serialize(r)` + `\n`; `CopyInto` skips `torn` records and unparseable lines (as written); keep SetPinned exempt from placeholder bits and Rename checked on the source (Part 02 guard rules).
8. Task 09.10: `SettingsStore.Load` = `SettingsLoadPolicy.Decide(path, text, readOnly, SettingsDefaults.Derive(pictures), now, () => SettingsRecovery.RootsFromLastRun(mirrors))`, renaming the bad file to `MoveCorruptTo` only when not read-only; `Save` writes `SettingsCodec.Serialize`.
9. Task 09.11: eraser tests use `CleanupPlanFixtures.Confirmed(...)` from `UasSort.Testing`.
10. Task 09.12: delete `MutexHolder` and `Shell/SingleInstance.cs` (and `User32` foreground imports, the file too if empty); `OffloadLock` wraps `NamedMutexLock.TryAcquire(@"Local\uas-sort-offload")`; tests for single instance move to Part 01's types (already tested there) — delete duplicates.
11. Task 09.13: delete `RepoFiles` (use `UasSort.Testing.RepoPaths`) and the `BannedSymbols.txt` content test (Part 01 Task 01.3 owns it); keep the eraser-only-deletes and justified-suppression tests.
12. Summary table: remove `PlaceholderGuard.ExposePlaceholders` and `Shell.SingleInstance`; `GuardedFileOps` composition line → `new GuardedFileOps(settings, appDataDir, Environment.MachineName, facts, OffloadCompiler.NewFolderDirs(batch, settings.VideoRoot))`; add "`PlatformServices` is Part 11's".

### Part 10 — View models

1. Conventions + Task 10.1: delete the generation script for `GlobalUsings.Core.cs` (Part 02 already created the fixed file in both projects); `src/UasSort.Review/GlobalUsings.cs` and `tests/UasSort.Review.Tests/GlobalUsings.cs` get exactly the registry content (drop `System.Collections.Immutable` and `Xunit` there).
2. Task 10.1: `GatedPlanDeriver`: do not create a new class; add `Hold`, `Calls` (list of `DeriveCall`), `CallStartedAsync`, `Release(int)`, `ReleaseAll` and `DeriveCall` to Part 06's `tests/UasSort.Testing/GatedPlanDeriver.cs`, keeping `Arm`/`Gate`/`CallCount` working.
3. Task 10.1 test fakes: delete `FakeThumbnailSource` (use `FakeThumbnails`), rename Review.Tests `Clip` → `PlanClip`; keep `FakeUiDispatcher`, `FakeDialogService`, `FakeShellLauncher`, `FakeDraftStore`, `FakeFreeSpace`, `ListLog`.
4. Task 10.3: delete `src/UasSort.Review/Services/ZoneNames.cs` and `GeoMath.cs` and `Fmt.Round15`; callers use Core `ZoneNames.Region` (was `Friendly`), `ZoneNames.Abbreviation`, `ZoneNames.OffsetAt`, `ZoneNames.IsUs`, `GeoMath.Haversine` (was `GeoMath.Between`), `DroneClock.Round15`; move the corresponding tests into assertions against the Core members or delete them (Part 04 tests them).
5. Task 10.4: `ClockText.Banner` returns `s.Headline`; delete its private text building and replace the three `ClockText_Banner_*` tests with one passthrough test; the ReviewVm clock InfoBar uses `Plan.Base.Clock.Headline`.
6. Task 10.6: delete `MapGeoPointConverter`; `MapJsonContext` registers `GeoPointJsonConverter` (Core).
7. Task 10.8: `PlanIndex.Candidates(g)` uses `Planner.AppendCandidates(Plan, g.Id)` (no own ranking, no "7 days, max 5").
8. Task 10.12: rename `FileKeys` → `DecisionTargets` (keep `For`, `ForEntry`), delete `Of`; every `FileKeys.Of(x, size)` → `FileKey.OfPath(x, size)`; `LedgerDecisionService` builds revoke records with `VerdictDecisions.Revokes`; delete Review.Tests `FakeLedgerStore` and `RecordingLedgerWriter` (use `UasSort.Testing.FakeLedgerStore`/`FakeLedgerWriter`).
9. Task 10.13: delete `ReviewSessionFactory`; construction is `new PlanSession(b, deriver, tuning, time)`; `ReviewVm.CanUndo/CanRedo` mirror `Session.CanUndo/CanRedo` after each `Changed`; add `string MapBase` (observable; initial `Plan.Base.Scan.Settings.Map.Base`; setter sends `new MapSetBase(value)` through `Map`), `void GoTo(ItemId anchor)` (selects the Videos tab, the card containing the anchor and the clip; Photos tab for photos), `IReadOnlyList<GroupCardVm> MoveTargets()` (every other non-AlreadyImported card) and `Task MoveSelectedToGroupAsync(GroupCardVm target)` (applies `new MoveToGroup([.. Videos.SelectedClipIds], target.Anchor)`), each with a test; Review Focus #1 wording per the index edit.
10. Task 10.17: delete Review.Tests `FakeLister` (use `FakeFileSystem`, which implements `IDirectoryLister`); keep `FakeSettingsStore`.
11. Task 10.19: delete `CommitEngine`; `CommitPorts(IDraftStore Drafts, IDialogService Dialogs, IUiDispatcher Ui)`; `PreflightVm(Plan plan, CardSource source, Func<Plan, CommitSession> begin, CommitPorts ports)` with the registry members (`Open` = `begin(plan)`; acks from `Session.RequiredAcks` as `AckVm(AckKey)`; `CanStart` = `PreflightAcks.CanStart(Session.Preflight, checked keys)`; `StartAsync` = `Session.StartAsync(...)` returning `CommitResult` and deleting the draft when `FailureFree`; `Dispose` = `Session.Dispose()`); `CopyVm.RunAsync()` returns `Task<CommitResult?>`; the "Start offload runs EnsureFolder → … → copy" paragraph now describes `CommitSession`; tests build the session with `OffloadRig`/`OffloadPlanBuilder` (`UasSort.Testing.Offload`) and delete Review.Tests `FakeFileOps`, `FakeOffloadLock`, `FakePower`.
12. Task 10.20: delete Review `NotCopiedKind` (use `UasSort.Core.Offload.NotCopiedKind`); `VerdictPorts(ILedgerStore Ledger, string Machine, TimeProvider Time, IDialogService Dialogs, IShellLauncher Shell, IDeviceEject Eject, Func<FormatVerdict> Reaudit)`; rows from `VerdictDecisions.NotCopied`/`Day`, selection check `VerdictDecisions.Check`, dialog body `VerdictDecisions.Confirmation`, records `VerdictDecisions.Records(...)` and undo `VerdictDecisions.Revokes(...)` appended through `Ledger.OpenOwn()`; `NotCopiedRowVm` wraps `NotCopiedRow`; then `Reaudit()`.
13. Tasks 10.21–10.22: `CleanupVm` texts come from `CleanupTexts` (`CutoffLine`, `NothingToDelete`, `ShortfallLine`, `EvidenceSplit`, `NeverCopiesLine`, `VolumeName`), row changes from `CleanupRowOps` (delete the private copies and their duplicated wording tests); `CleanupEngine.Run` is documented as exactly `CleanupExecutor.RunAsync(confirmed, env, progress, ct)`; fixtures use `CleanupPlanFixtures` instead of `null!`; cleanup tests use the Part 02/06/07 fakes (no `UasSort.Testing.Cleanup`).
14. Task 10.23: `ShellDeps.CreateVerdict` becomes `Func<ReviewVm, CommitResult?, VerdictVm>`; add `Action<Settings> SaveSettings` to `ShellDeps` and `void UpdateLayout(double timelineWidth, double mapHeightRatio)` to `ShellVm` (saves `Settings with { Layout = new(timelineWidth, mapHeightRatio) }` and updates `Settings`); `_lastResult` is a `CommitResult?`; the shell disposes `Preflight` (the session) when the verdict is shown; `CreateCleanup` gets `_lastResult?.Offload`.
15. Summary table: apply items 2–14 (remove `ZoneNames`, `GeoMath`, `FileKeys`, `MapGeoPointConverter`, `ReviewSessionFactory`, `CommitEngine`, Review `NotCopiedKind`; add the new members); "Part 11 binds" line → "`CommitSession` via `ShellDeps.CreatePreflight`, `CleanupEngine`/`CleanupPreparation`, `ShellDeps`, stage-VM delegates, `VerdictPorts.Reaudit`, `ReviewServices`, `IFreeSpace`, `IReviewLog`, `MapBridge`".

### Part 11 — WinUI App

1. Replace the whole "Contract C11" block by: "Part 11 binds only the Part 09/10 members listed in `00-interfaces.md` (Review VMs table); the C11 → Part 10 mapping below is applied in every page, control and selftest check of this part", followed by the mapping table in this section (item 22).
2. Task 11.3: extend Part 01's `SelfTestSandbox` (`UasSort.Platform.Stores`, partial) with `Create()`, `AppDataDir`, `VideoRoot`, `PhotoRoot`, `CardRoot`, `WriteFile`; use `WebView2Folder` (not `WebView2Dir`) and Part 01's `WriteResult(string, ReadOnlySpan<byte>)`; delete the `UasSort.Platform.SelfTest` namespace; the new members' tests go into Part 01's `SelfTestSandboxTests` (`UasSort.Platform.Tests.Stores`), not a second class of that name.
3. Task 11.3: `WindowInterop` drops `AllowForeground`/`BringToFront` (use `ForegroundWindow.AllowSetForeground`/`BringToFront`); keep `Send`, `CreateMessageOnlyWindow`, `DestroyWindow`; `WindowMessageHook` as written (`UasSort.Platform.Win32`).
4. Task 11.3: add `src/UasSort.Platform/PlatformServices.cs` (`namespace UasSort.Platform`) with the registry record; `Create` builds `KnownFolders`, `PathFacts`, `WindowsDirectoryLister`, `WindowsVolumeProvider`, `CardSourceValidator`, `WindowsCardReaderFactory`, `WindowsCardEraserFactory`, `SettingsStore`, `DraftStore`, `ReportStore`, `AppAssets`, `PowerRequest`, `OffloadLock`, `DeviceEject`, `ShellLauncher`, `FileLog` with `Environment.MachineName`, `LedgerFor = root => new LedgerStore(settings with { VideoRoot = root }, …)`, `FileOpsFor = (s, dirs) => new GuardedFileOps(s, appDataDir, machine, facts, dirs)`; it writes nothing (selftest `placeholderVisibility`).
5. Task 11.4: `Program.Main` keeps Part 01's order and calls `PlaceholderMode.ExposePlaceholders()` (not `PlaceholderGuard`); parse with Part 01's `LaunchOptions` (delete `SelfTestOptions`); extend Part 01's `SingleInstanceGate` in place (add `event Action? Activated`, redirect on a worker thread) instead of a static `TryBecomePrimary`.
6. Task 11.4: modify Part 01's `SelfTestCheck`/`SelfTestResult`/`SelfTestJsonContext` files (already Part 13 shape after the Part 01 edits) — do not redeclare; `SelfTestJson` → `SelfTestJsonContext`; delete `MinimalSelfTest`, `StackProbePage`, the probe VMs, `ProbeTemplateSelector` and `MainWindow.ProbePage` (keep `FirstFrameMs`, `ShowOffScreen`, `BringToFront`).
7. Task 11.4: `src/UasSort.App/GlobalUsings.cs` gets exactly the registry content (no `UasSort.Core.Ports`, no `UasSort.Review.Shell/…` sub-namespaces).
8. Task 11.4: add `<Content Include="places.bin.gz" CopyToOutputDirectory="PreserveNewest" />` to the App csproj; the composition root starts `PlaceIndex.LoadAsync(platform.Assets)` in the background behind `DeferredPlaceIndex`.
9. Task 11.4 composition root: build `ShellDeps` (not `ReviewDependencies`): `CreateCard` = `new CardStageVm(selfTest ? NoVolumes : platform.Volumes, v => CardDetector.Detect(v, platform.Lister, settings), (p, d) => platform.Validator.Validate(p, d, settings, platform.Lister, platform.PathFacts, platform.AppDataDir))`; `CreateScan` = `new ScanStageVm((src, pr, ct) => new ScanService(settings, platform.Lister, platform.LedgerFor(settings.VideoRoot), platform.Volumes, tz, pcZone, clock).ScanAsync(src, platform.Readers, pr, ct), planner.Prepare, settings, ui)`; `CreateReview` = sets `CardThumbnails.Use(reader, b.Scan.Raw)` then `new ReviewVm(new PlanSession(b, planner, new Tuning(settings.RadiusMiles, settings.GapDays), clock), services, decisions, DraftOffers.Find(b, platform.Drafts, planner, clock))`; `CreatePreflight` = `new PreflightVm(r.Plan, source, plan => CommitSession.Begin(plan, commitEnv), new CommitPorts(platform.Drafts, dialogs, ui))` with `CommitEnvironment(reader, platform.Lister, ledger, platform.OffloadLock, platform.Power, cardThumbnails, platform.Reports, platform.Volumes, dirs => platform.FileOpsFor(settings, dirs), clock, platform.Machine, appVersion)`; `CreateCleanup` builds `CleanupEngine(Prepare, (c, p, ct) => CleanupExecutor.RunAsync(c, cleanupEnv, p, ct), shell.RescanForCleanupAsync, platform.Reports.Save, platform.Eject)`; `SaveSettings` = `platform.Settings.Save`; `AuditNow` = relist + ledger `Load` + `CardAudit.Audit(...)`.
10. Task 11.4: add `CardThumbnails : IThumbnailSource` (`UasSort.App.Services`, `Use(ICardReader, IEnumerable<RawItem>)`), passed as `ReviewServices.Thumbs`, `CommitEnvironment.Thumbnails`, `CleanupEnvironment.Thumbnails`; `ShellVm.Thumbnails` does not exist.
11. Task 11.2: `LruCache` in namespace `UasSort.Review` (not `UasSort.Review.Services`).
12. Stage enum: every `ShellStage` → `Stage`; `ShellPage.StagePages` keyed by `Stage`; pages take their VM from `ShellVm.Current` (cast) or the typed properties (`Card`, `Review`, `Preflight`, `Copy`, `Verdict`, `Cleanup`); Setup and Settings VMs come only from `Current`.
13. Task 11.9: `tools/fixtures/make-selftest-assets.cs` is **Modify** (Part 01 created it): add `selftest.dng`, `ledger-v1.jsonl`, `selftest-000{1,2,3}.mp4` to `SelfTestAssets.All()`, keep `stack-exif.jpg`; add `tests/UasSort.Testing/LedgerSamples.cs` (`AllRecordKindsV1()` via `LedgerCodec.Serialize`); `CoreJson` → `LedgerJsonContext.Default.LedgerRecord` / `CoreJsonContext.Default.PlanEdit`; `CardStageVm.BrowseResultAsync(path)` → `CardStageVm.Browse(path)`.
14. Task 11.8: map host binding: build `new MapBridge(json => pane.PostJson(json), log, clock)`, on `Ready` send `MapProjection.Init(settings.Map, review.Tuning.RadiusMiles, MapPane.IsOnline(), dark)` then assign `review.Map = bridge` (sends `setData`); `pane.MessageReceived += bridge.Dispatch`; theme → `bridge.Send(new MapSetTheme(t))`; connectivity change → re-send `MapProjection.Init(...)`; ping → `bridge.Send(new MapPing(n))`, pong/ready via `ReviewVm.MapStatus`; context menu via `ReviewVm.MapContextMenuRequested`; `OpenInBrowserUri` is built inside `MapPane` from the selected group.
15. Task 11.10: layout from `Shell.Settings.Layout`, splitter changes call `Shell.UpdateLayout(width, ratio)`; map base selector binds `ReviewVm.MapBase`; tab headers `Videos.Header`/`Photos.Header`/`Other.Header`; footer `FooterText`; Offload button `OffloadCommand` + tooltip `OffloadDisabledReason`; issues flyout items `IssuesVm.Entries` (`IssueVm`), click → `ReviewVm.GoTo(issue.Anchor)`.
16. Task 11.12: clip menu "Move to group ▸" uses `ReviewVm.MoveTargets()`/`MoveSelectedToGroupAsync`; "Copy card path" = `PathRules.Join(review.Plan.Base.Scan.Inventory.Source.Root, row.Id.CardRelPath.Replace('/', '\\'))`.
17. Task 11.15: Start offload button calls `ShellVm.StartCopyAsync()`; Preflight lists bind `IReadOnlyList<string>` (`Blocking`, `Warnings`, `Infos`, `FoldersToCreate`, `FoldersAppended`, `VolumeLines`); Verdict binds `CategoryLines`, `Ejects` (`EjectVm`), `Groups` (`VerdictGroupRowVm`), `NotCopied`, `NotCopiedDays`; no `IssueRowVm`, `CommandRowVm`, `CategoryVm`.
18. Task 11.16: entry points are `ShellVm.CleanupCommand`/`CleanupEnabled`/`CleanupTooltip` and `VerdictVm.CleanupCommand`/`CanCleanup`/`CleanupTooltip`; `CleanupStep` has five values (`Choose`, `Review`, `Confirm`, `Deleting`, `Result`); scroll to `CleanupVm.FirstUndecided` when it changes.
19. Task 11.17: `DeviceChangeWatcher` suppresses when `Shell.Stage is Stage.Preflight or Stage.Copy or Stage.Cleanup` and otherwise calls `Shell.DeviceChanged()` (no `DeviceRefreshEnabled`/`OnDevicesChangedAsync`).
20. Tasks 11.4/11.18 and the summary: command line `uas-sort.exe --selftest --result <path> [--only a,b]` parsed by `LaunchOptions`; result `{ok, firstFrameMs, checks[{name,status,detail}]}` from Part 01's records.
21. Summary: Platform line → "`SelfTestSandbox` extension (Part 01 type), `WindowInterop` (no foreground members), `WindowMessageHook`, `PlatformServices`"; Review line keeps `LruCache` (`UasSort.Review`); Selftest line drops `SelfTestOptions`/`SelfTestJson`; Consumed contract line → "`00-interfaces.md`".
22. C11 → Part 10 mapping (apply in the listed App files; "none" = removed from the App):

| C11 item | Part 10 canonical |
|---|---|
| `ReviewDependencies`, `ShellVm(ReviewDependencies)` | `ShellDeps`, `ShellVm(ShellDeps)` (item 9) |
| `ShellVm.Thumbnails` | App `CardThumbnails` (item 10) |
| `ShellStage` | `Stage` |
| `ShellVm.Setup`, `.Scan`, `.Settings` (SettingsPageVm) | `ShellVm.Current` cast to `SetupVm`/`ScanStageVm`/`SettingsPageVm` (`ShellVm.Settings` is the `Settings` record) |
| `ShellVm.Card`, `.Review`, `.Preflight`, `.Copy`, `.Verdict`, `.Cleanup` | same names |
| `ShellVm.CardChipText`, `CanRescan`, `CanOpenSettings`, `RescanCommand` | same names (`RescanCommand` is `IAsyncRelayCommand`) |
| `ShellVm.HasCard` | `CardChipText is not null` (via `UiFormat`) |
| `ShellVm.CanUndo`, `CanRedo`, `UndoCommand`, `RedoCommand` | `ShellVm.CanUndoRedo` + `Review.CanUndo`/`CanRedo`/`UndoCommand`/`RedoCommand` |
| `ShellVm.CanCleanUp`, `CleanUpTooltip`, `OpenCleanupCommand`, `OpenSettingsCommand` | `CleanupEnabled`, `CleanupTooltip`, `CleanupCommand`, `SettingsCommand` |
| `ShellVm.DeviceRefreshEnabled`, `OnDevicesChangedAsync()` | Stage check + `DeviceChanged()` (item 19) |
| `ShellVm.InfoBars` | none (stage pages show their own: `ReviewVm.InfoBars`, `SetupVm.RecoveryText`, `CleanupVm.BlockingText`) |
| `InfoBarVm.Key`, `Message`, `IsClosable` | same |
| `InfoBarVm.Severity` (IssueSeverity) | `InfoSeverity` |
| `InfoBarVm.Title`, `IsOpen` | none (closing calls `ReviewVm.CloseInfoBar(Key)`) |
| `InfoBarVm.ActionLabel`, `HasAction`, `ActionCommand`, `DismissCommand` | `Actions` (`QuickFixVm` list: `Label`, `Command`); dismiss = `CloseInfoBar` |
| `SetupVm.VideoRootFree`, `PhotoRootFree`, `LedgerStatusText` | `VideoFreeText`, `PhotoFreeText`, `LedgerStatus` |
| `SetupVm.VideoRootError`, `PhotoRootError` | none (use `CanConfirm`) |
| `SetupVm.LedgerFolder` | `LedgerPaths.For(VideoRoot)` via `UiFormat` |
| `SetupVm.LedgerBlocking` | `LedgerSeverity == InfoSeverity.Error` |
| `SetupVm.SetVideoRootAsync`, `SetPhotoRootAsync` | `SetVideoRoot`, `SetPhotoRoot` (sync) |
| `SetupVm.ConfirmCommand`, `KeepOnDeviceCommand` | same (`IRelayCommand`); plus `CanKeepOnDevice`, `RecoveryText`, `PhotoInsideVideoNote` |
| `CardStageVm.Volumes` / `VolumeRowVm` | `Rows` / `CardRowVm` (`Text`, `KindText`, `IsDjiCard`, `IsWriteProtected`, `UseCommand`) |
| `VolumeRowVm.Letter`, `Label`, `FileSystem`, `MediaCountText` | composed in `CardRowVm.Text` |
| `CardStageVm.IsEmpty`, `EmptyText`, `RefusalMessage` | `Rows.Count == 0`, `StatusText`, `Message` |
| `CardStageVm.ScanVolumeCommand` | `CardRowVm.UseCommand` |
| `CardStageVm.CanBrowse`, `BrowseResultAsync(path)` | `ShellVm.CanBrowse`, `Browse(string?)` |
| `CardStageVm.RescanCommand` (async) | `RescanCommand` (`IRelayCommand`) |
| `ScanStageVm.PhaseText`, `Progress`, `IsIndeterminate`, `CancelCommand` | same (+ `ErrorText`, `IsRunning`) |
| `ReviewVm.Map` (non-null) | `Map` (`MapBridge?`, set by the App, item 14) |
| `ReviewVm.SelectedTabIndex` | `SelectedTab` |
| `ReviewVm.VideosTabText`, `PhotosTabText`, `OtherTabText` | `Videos.Header`, `Photos.Header`, `Other.Header` |
| `ReviewVm.FooterTotalsText`, `OffloadTooltip` | `FooterText`, `OffloadDisabledReason` |
| `ReviewVm.OffloadCommand` (async) | `OffloadCommand` (`IRelayCommand`) |
| `ReviewVm.TimelineWidth`, `MapHeightRatio`, `UpdateLayout` | `ShellVm.Settings.Layout` + `ShellVm.UpdateLayout` (added) |
| `ReviewVm.MapBaseIndex` | `ReviewVm.MapBase` (added; `"streets"`/`"satellite"`/`"none"`) |
| `VideosTabVm.Timeline` (`TimelineItemVm`) | `Timeline` (`TimelineEntryVm`) |
| `VideosTabVm.SelectedItem` | `SelectedEntry` |
| `VideosTabVm.SelectedClips`, `SetSelectedClips(rows)` | `SelectedClipIds`, `SetSelectedClips(IReadOnlyList<ItemId>)` |
| `VideosTabVm.ClipSelectionChangedByMap` | `ClipSelectionChanged` (`Action<IReadOnlyList<ItemId>>`) |
| `VideosTabVm.MergeWithNextCommand`, `MoveSelectedToNewGroupCommand` | `ReviewVm.MergeSelectedWithNextAsync()`, `ReviewVm.MoveSelectedToNewGroupAsync()` |
| `VideosTabVm.MoveTargets()`, `MoveSelectedToGroupAsync(target)` | `ReviewVm.MoveTargets()`, `ReviewVm.MoveSelectedToGroupAsync(target)` (added) |
| `VideosTabVm.SetSelectedIncludedAsync(bool)` | `ReviewVm.SetIncludedAsync(Videos.SelectedClipIds, bool)` |
| `VideosTabVm.ToggleIncludedAsync(row)`, `SplitBeforeAsync(row)` | `row.ToggleIncludedCommand`, `row.SplitBeforeCommand` |
| `VideosTabVm.OpenInPlayer(row)`, `CardPathOf(row)` | `ReviewVm.OpenClip(row.Id)`, item 16 |
| `TimelineItemVm.HasChip`, `ChipText`, `ChipHasAction`, `ChipActionLabel`, `ChipTooltip`, `ChipCommand` | `GroupCardVm.Chip` (`BoundaryChipVm?`): `Chip is not null`, `Text`, `CanMerge`, `ButtonText`, `MergeTooltip`, `MergeCommand` |
| `GroupCardVm.SwatchColor`, `DescriptionIsSuggested`, `BadgeText` | `Swatch`, `DescriptionIsSuggestion`, `Badge` |
| `GroupCardVm.IsDescriptionEditable` | `!IsDescriptionReadOnly` |
| `GroupCardVm.FilterSuggestions(text)` | none (show `Suggestions`, at most 6) |
| `GroupCardVm.CommitRenameAsync(text)` | set `Description`, then `CommitDescriptionCommand` |
| `GroupCardVm.IsAppend`, `AppendHint` | `ReadOnlyHint is not null`, `ReadOnlyHint` |
| `GroupCardVm.HasConfidence`, `ConfidenceText` | `ConfidenceText is not null`, `ConfidenceText` |
| `GroupCardVm.BuildTargetMenu()` / `TargetMenuEntryVm` / `TargetMenuKind` | `RetargetOptions()` / `RetargetOptionVm` / `RetargetKind` (separators added by the App; click → `ReviewVm.RetargetAsync(card, option)`) |
| `GroupCardVm.RetargetToBrowsedFolderAsync(path)`, `VideoRootForPicker` | `ReviewVm.BrowseRetargetAsync(card, path)`, `ReviewVm.Index.VideoRoot` |
| `GroupCardVm.ShowPhotosCommand` | App sets `ReviewVm.SelectedTab = 1` |
| `GroupCardVm.Thumbs` (ObservableCollection), `HasMoreThumbs` | `Thumbs` (`IReadOnlyList<ThumbVm>`, replaced whole), `MoreThumbsText is not null` |
| `GroupCardVm.DateRangeText`, `ZoneBadges`, `LocationText`, `VideoCountsText`, `PhotoCountsText`, `MoreThumbsText`, `Chips`, `Suggestions`, `NewFolderInsteadCommand`, `TargetPath`, `Description` | same |
| `FoldedRunVm.ToggleExpandCommand` | `VideosTabVm.ToggleFold(run)` (+ `Text`, `IsExpanded`, `ClipCount`) |
| `ThumbVm.ThumbKey` | `ThumbVm.Key` |
| `ChipVm.HasAction`, `ActionLabel`, `ActionCommand` | `Actions.Count > 0`, `Actions[0].Label`, `Actions[0].Command` |
| `ClipRowVm.Included`, `CanToggleInclude`, `TimeSourceGlyph`, `HasDayBanner` | `IsIncluded`, `!IsReadOnly`, `TimeGlyph`, `Banner is not null` |
| `DaySplitBannerVm.Emphasised`, `SplitHereCommand` | `IsEmphasised`, `SplitCommand` (+ `EmphasisText`) |
| `TuningVm.GapDays` (double) | `GapDays` (int) |
| `TuningVm.BeginPointerDrag()`, `EndPointerDragAsync()` | `BeginDrag()`, `EndDragAsync()` |
| `IssuesVm.Items` / `IssueRowVm` | `Entries` / `IssueVm` |
| `IssueRowVm.CodeText`, `Fixes`, `GoToCommand` | `Code` (formatted by `UiFormat`), `QuickFixes`, `ReviewVm.GoTo(issue.Anchor)` (added) |
| `PhotosTabVm.ToggleIncludedAsync(tile)` | `tile.ToggleCommand` |
| `PhotoDayVm.Title`, `ReasonText` | `DayText`, `Reason` |
| `PhotoTileVm.Included`, `Name`, `IsConfirmedByYou` | `IsIncluded`, `Text`, `CanUndo` |
| `PhotoTileVm.IsPaired`, `IsSet`, `SetText`, `ClashText` | `PairText is not null`; set/clash text is part of `StatusText` |
| `OtherSectionVm.IsExpanded`, `Rows` (ObservableCollection) | `!IsCollapsed`, `Rows` (`IReadOnlyList<OtherRowVm>`) (+ `Note`) |
| `OtherRowVm.Path`, `CanUndismiss` | `Text`, `UndismissCommand is not null` |
| `MapBridge.Outgoing`, `OnIncoming`, `Start`, `SetTheme`, `SetOnline`, `Ping`, `PongReceived`, `ReadyReceived`, `ClickHandled`, `ContextMenuRequested`, `OpenInBrowserUri()` | ctor `post`, `Dispatch`, `Send(MapProjection.Init(...))`, `Send(new MapSetTheme(...))`, re-send init, `Send(new MapPing(n))`, `ReviewVm.MapStatus`, `ReviewVm.MapStatus`, none, `ReviewVm.MapContextMenuRequested`, App-side (item 14) |
| `PreflightVm.IsChecking` | none (`Open()` is synchronous) |
| `PreflightVm.Blocking`, `Warnings`, `Infos` (`IssueRowVm`) | `IReadOnlyList<string>` |
| `PreflightVm.FoldersToCreate`, `FoldersAppended`, `VolumeLines` (ObservableCollection) | `IReadOnlyList<string>` |
| `PreflightVm.StartCommand` | `ShellVm.StartCopyAsync()` |
| `PreflightVm.Acks`, `CanStart`, `BackCommand`; `AckVm.Text`, `IsChecked` | same |
| `CopyVm.CurrentFileText`, `Progress` | `CurrentText`, `Fraction` |
| `VerdictVm.HasSafeRemovalNote` | `SafeRemovalNote is not null` |
| `VerdictVm.Ejects`, `Groups` (`CommandRowVm`) | `Ejects` (`EjectVm`: `Text`, `EjectCommand`, `ResultText`), `Groups` (`VerdictGroupRowVm`: `Text`, `Path`, `OpenFolderCommand`) |
| `VerdictVm.Categories` (`CategoryVm`) | `CategoryLines` |
| `VerdictVm.RecordAsImportedCommand`, `CanUndo` | `RecordImportedCommand`, `UndoCommand.CanExecute` |
| `VerdictVm.CanCleanUp`, `CleanUpTooltip`, `CleanUpCommand` | `CanCleanup`, `CleanupTooltip`, `CleanupCommand` |
| `VerdictVm.DoneCommand` (async) | `DoneCommand` (`IRelayCommand`) |
| `NotCopiedRowVm.IsDayHeader`, `CanSelect` | `NotCopiedDays` (`NotCopiedDayVm`: `Date`, `Text`, `SelectDayCommand`); rows always selectable via `ToggleCommand` |
| `CleanupStep { Choose, Deleting, Result }` | `CleanupStep { Choose, Review, Confirm, Deleting, Result }` |
| `CleanupVm.ModeIndex`, `FreeSpaceKindIndex`, `FreeSpaceGb` | `Mode` (`CleanupMode`), `FreeKind` (`FreeSpaceKind`), `GbValue` |
| `CleanupVm.CardSpaceText`, `CardLine` | `CardSummary` / `FreeNowText` |
| `CleanupVm.ShowReviewList`, `ReviewRows`, `ScrollToRowRequested` | `Step == CleanupStep.Review`, `Rows`, `FirstUndecided` changes |
| `CleanupVm.CutoffLine`, `CountsLine`, `EvidenceLine`, `NeverCopiedLine`, `HasNeverCopiedLine`, `NeverTouchedLine` | `CutoffText`, `CountsText`, `EvidenceText`, `NeverCopiedText`, `NeverCopiedText is not null`, `NeverTouchedText` |
| `CleanupVm.FlightContinuesLine`, `HasFlightContinuesLine` | part of `CutoffText` (`CleanupTexts.CutoffLine`) |
| `CleanupVm.KeptLines` | `Kept` (`KeptGroupVm`: `Reason`, `Lines`) |
| `CleanupVm.HasShortfall`, `ShortfallOffersInclude` | `ShortfallText is not null`, `CanIncludeNotInLibraryFix` |
| `CleanupVm.AckCantRecover`, `ShowAckNotInLibrary`, `AckNotInLibraryText` | `AckCantBeRecovered`, `ShowNotInLibraryAck`, `NotInLibraryAckText` |
| `CleanupVm.InfoBars` | `BlockingText` + `CanRescan`/`RescanCommand` |
| `CleanupVm.Progress` | indeterminate bar + `ProgressText` |
| `CleanupVm.BackCommand` (async) | `BackCommand` (`IRelayCommand`); other commands same names |
| `CleanupRowVm.WhenText`, `TickedForOffload`/`IsNewInRange`, `DecisionIndex` | `DateText`, `Badge`, `Decision` (`RowDecision`) + `KeepCommand`/`DeleteCommand` |
| `CleanupResultVm.Headline`, `SkippedLines`, `StillListedLines`, `RemovalNote`, `EjectLabel`, `EjectCommand`, `DoneCommand` | `HeadlineText`, `Problems`, `StillListed`, `SafeRemovalText`, `Eject.Text`, `Eject.EjectCommand`, `CleanupVm.DoneCommand` |
| `SettingsPageVm.VideoRootFree`, `PhotoRootFree`, `LedgerStatusText` | `VideoFreeText`, `PhotoFreeText`, `LedgerStatus` |
| `SettingsPageVm.PhotoInsideVideo` | `PathRules.IsStrictlyUnder(PhotoRoot, VideoRoot)` via `UiFormat` |
| `SettingsPageVm.OpenLedgerFolderCommand`, `OpenBackupFolderCommand` | `OpenLedgerCommand`, `OpenBackupCommand` |
| `SettingsPageVm.ShowNoHistoryPrompt`, `NoHistoryText` | `NoHistoryPrompt is not null`, `NoHistoryPrompt` |
| `SettingsPageVm.DefaultRadiusMiles`, `DefaultGapDays` (double) | `RadiusMiles`, `GapDays` (int) |
| `SettingsPageVm.ClockModeIndex`, `ClockFollowsSiteLocal` | `IsSiteLocal` |
| `SettingsPageVm.ZoneIds`, `ClockZoneIndex` | `DroneClock.UsZones` (+ current `ClockZone`), `ClockZone` (string) |
| `SettingsPageVm.MapBaseIndex`, `StreetsStyleUrl`, `StreetsDarkStyleUrl` | `MapBase` (string), `StreetsUrl`, `StreetsDarkUrl` |
| `SettingsPageVm.SatellitePresets` (`PresetVm`) | `SatellitePresets` (dictionary; menu item sets `SatelliteUrl`) |
| `SettingsPageVm.SetVideoRootAsync`, `SetPhotoRootAsync`, `CloseCommand` | `ChangeVideoRootAsync`, `ChangePhotoRoot` (sync), `ShellVm.CloseSettings()` |
| `PreviousRootVm.Path`, `ForgetCommand` | same |
| `CoreJson` | `LedgerJsonContext` / `CoreJsonContext` |
| `PlaceholderGuard.ExposePlaceholders()` | `PlaceholderMode.ExposePlaceholders()` |
| `PlatformServices` (Part 09) | Part 11's own record (item 4) |
| `LedgerSamples` (Part 03/05) | Part 11 Task 11.9 (`UasSort.Testing`) |

### Part 12 — CLI

1. Rename `PlanText` → `PlanTextRenderer` (`src/UasSort.Cli/PlanTextRenderer.cs`), including every call in `PlanCommand` and the Platform.Tests `PlanDocumentTests`.
2. Task 12.6: `Program.Main` first calls `PlaceholderMode.ExposePlaceholders()` (Part 01, `UasSort.Platform.Win32`), then sets the console encoding; add a test asserting the call happens before `PlanCommand.RunAsync` (or a source check).
3. Task 12.1: `src/UasSort.Cli/GlobalUsings.cs` gets exactly the registry content (Core namespaces come from `GlobalUsings.Core.cs`); delete the "If the model is moved to `UasSort.Core.Model`" row text.
4. Task 12.6: `WindowsCliHost` passes `Environment.MachineName` as `machine` to every Platform constructor and builds `ScanService`/`Planner` with the registry constructors; tests use Part 01's `TestTempDir`/`RepoPaths`.
5. `FakeCardWriter` stays here (owner; no earlier part uses it); remove the "If an earlier part has since added one" sentence.

### Part 13 — AOT and deploy

1. Header "Depends on": "through `SelfTestOptions.Parse` and writes `SelfTestResult(Ok, FirstFrameMs, Checks)`" → "through Part 01's `LaunchOptions.Parse` (extended by Part 11) and `SelfTestResult(Ok, FirstFrameMs, Checks)`".
2. Task 13.2: `SlowerThanBaseline` sentence → "does not change `Ok`; it is recorded in the summary file and reported to the user in the completion summary (decision 3); it is not an AOT concrete-check failure".
3. Task 13.5 `deploy.ps1`: replace the yellow warning text by "NOTE: warm first frame N ms is slower than the 0.37 s ReadyToRun baseline; recorded as slowerThanBaseline and reported in the completion summary."; in the troubleshooting list remove "or `slowerThanBaseline : True`" from the stop-and-ask bullet.
4. Task 13.7 (completion criteria; was 13.8) Step: "If `slowerThanBaseline` is `True`, or the gate fails: stop and ask" → "If the gate fails: stop and ask. If `slowerThanBaseline` is `True`, continue, and state the warm value and the 370 ms baseline in the completion commit message and the final summary to the user"; expected `slowerThanBaseline` may be either value.
5. "If Native AOT fails a concrete check": remove "or a warm first frame slower than the 0.37 s ReadyToRun baseline (`slowerThanBaseline : True`)" from the definition.
6. The ARM64 publish task (formerly 13.6) is deleted (x64 only, user decision 2026-09-28; applied in the part file): the README task is 13.6 and the completion criteria 13.7; `win-arm64` is gone from `Get-AppPublishConfig`, `Get-UasSortRid`, `Test-AotPublishOutput` and the tests (37 deploy cases).

## Resolved user decisions (2026-09-28)

Nothing is open for the user. Formerly open, now decided:

1. **x64 only.** The user has no ARM64 PC ("Both of my personal laptops are regular x86 Intel"). No `win-arm64` publish, no MSVC ARM64 build tools; `Platforms=x64`, `RuntimeIdentifiers=win-x64`; `deploy.ps1` publishes `win-x64`.
2. **Replay fixture pre-generated.** `docs/research/fixtures/library-listing.json` (listing-only metadata; no video copies) is checked in; Task 06.19 copies it; `tools/fixtures/make-replay-fixture.ps1` is the record of how it was produced and is not run during the build.
3. **Execution = subagent-driven** (superpowers:subagent-driven-development; a fresh implementer and reviewer per task, whole-branch review at the end).
4. **Development runs from a PowerShell Claude CLI session on Windows** (not WSL); Task 01.0 installs and verifies the prerequisites when the user says to start development.
