# Picture Offload cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Clean up Picture Offload" page that moves already-imported photos and photo sets out of the photo root (Picture Offload) into the Windows Recycle Bin, by date cutoff or verified against the Lightroom library folder, records every moved file as a `photoDelete` ledger record, and makes those records count as "imported" for later cards and for card cleanup.

**Architecture:** Core (namespace `UasSort.Core.Cleanup`, folder `src/UasSort.Core/Cleanup/Photos/`) surveys the photo root from a listing plus the ledger plus EXIF of local files only, builds a `PhotoCleanupPlan` (optionally verified against a read-only `LightroomIndex`), confirms it into an immutable `ConfirmedPhotoCleanupPlan`, and runs it through `PhotoCleanupExecutor` (per-item re-check, recycle through a port, one ledger record after each move, report). Two new guarded ops (`IoOp.PhotoCleanupRead`, `IoOp.PhotoRootRecycle`) extend `IoGuardPolicy`; Platform adds `GuardedPhotoFileReader` and `WindowsPhotoRootRecycler` (shell `IFileOperation` through `[GeneratedComInterface]` on a dedicated STA thread, refusing any delete the shell would not recycle). Review adds `PhotoCleanupVm` and a shell stage; App adds `PhotoCleanupPage`, a title-bar button, a Settings row, wiring and selftest checks.

**Tech Stack:** .NET 11 (SDK 11.0.100-rc.1.26425.128) · C# 15 (closed records, unions) · WinUI 3 (Windows App SDK component packages) · CommunityToolkit.Mvvm 8.4.2 · MetadataExtractor 2.9.3 · System.IO.Hashing · COM source generators (`System.Runtime.InteropServices.Marshalling`: `[GeneratedComInterface]`, `[GeneratedComClass]`, `ComInterfaceMarshaller<T>`) + `[LibraryImport]` · xUnit v3 on Microsoft.Testing.Platform · Native AOT publish (win-x64).

**Spec:** [`docs/superpowers/specs/2026-10-04-picture-offload-cleanup-design.md`](../specs/2026-10-04-picture-offload-cleanup-design.md) (Task P.16 copies the design there from the git-ignored `.superpowers/sdd/2026-09-27-uas-sort/2026-10-04-picture-offload-cleanup-design.md`, and this plan to `docs/superpowers/plans/2026-10-04-picture-offload-cleanup.md`); it extends [`docs/superpowers/specs/2026-09-27-uas-sort-design.md`](../specs/2026-09-27-uas-sort-design.md) and [`docs/superpowers/specs/2026-09-27-uas-sort-design-reference.md`](../specs/2026-09-27-uas-sort-design-reference.md) (Ref §4.3 IO guard, §7.3 photo newness, §8.8 set folders, §9.14 Settings, §10.6 card cleanup, §11 persistence), which govern wherever the new spec is silent. Executors read all three.

**Before Task P.1 (branch):** another session may still be committing on `fix/splitter-and-verdict-select`. Start only when `git status --porcelain` on that branch shows nothing but `?? .vscode/` (the user's folder: never add, commit, delete or stash it). Then `git switch -c feat/picture-offload-cleanup` from its tip. Never stash, reset or commit another session's changes. Every "Modify" below names the anchor text to find; files may have moved a few lines since this plan was written, so search for the anchor rather than trusting a line number.

## Global Constraints

Every task's requirements implicitly include this section. The values are verbatim from `docs/superpowers/plans/2026-09-27-uas-sort.md` (Global Constraints) where they still apply, plus the AOT rules learned since.

- **Target frameworks:** Core, Review, test libs `net11.0`; Platform, App, Cli, Platform.Tests, BannedApi.Probe `net11.0-windows10.0.26100.0`; `TargetPlatformMinVersion=10.0.26100.0`.
- **Directory.Build.props:** `Nullable=enable`, `ImplicitUsings=enable`, `AnalysisLevel=11.0-recommended` (fallback `10.0-recommended` only if RC1 rejects it), `TreatWarningsAsErrors=true`, `ManagePackageVersionsCentrally=true`; Core/Review/Platform `IsTrimmable=true`, `IsAotCompatible=true`; test projects `NoWarn` CA1707. No new package is added by this plan.
- **App csproj:** `OutputType=WinExe`, `UseWinUI=true`, `WinUISDKReferences=false`, `Platforms=x64`, `RuntimeIdentifiers=win-x64` (x64 only), `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, `SelfContained=true`, `EnableMsixTooling=true`, `DefineConstants` += `DISABLE_XAML_GENERATED_MAIN`; Release: `PublishAot=true`, `<TrimmerRootAssembly Include="MetadataExtractor;XmpCore"/>`; `AllowUnsafeBlocks=true` and `CsWinRTAotWarningLevel=2` stay as they are. Never set `InvariantGlobalization` or `UseNls`. ReadyToRun is never a build configuration; if AOT fails a concrete check, diagnose with ReadyToRun by hand and **raise it with the user**.
- **Platform csproj:** `AllowUnsafeBlocks=true` (for `LibraryImport` and the COM marshallers).
- **IO safety:** only `ICardReader`, `ICardEraser`, `IFileOps`, `IDirectoryLister`, Platform's own stores and (new) `IPhotoFileReader` / `IPhotoRootRecycler` may touch the file system; all consult `IoGuardPolicy`. The one documented exception is Platform's `internal` test/selftest helper `RecycleBinPurge` (Task P.11), which only ever deletes Recycle Bin items whose original path is this run's own `%TEMP%\uas-sort-test-*` / `%TEMP%\uas-sort-selftest-*` folder and is reachable from the app only through `SelfTestSandbox.PurgeOwnRecycleBinItems` (source-guard test). Core, Review, App and Cli never touch the file system. Only `IPhotoRootRecycler` moves anything out of the photo root, only items named in a `ConfirmedPhotoCleanupPlan`, only to the Recycle Bin, never a permanent delete. The Lightroom folder and `D:\LR_Catalog` are never written. `BannedSymbols.txt` is exactly Ref §2.4 (this plan does not change it); allowed risky calls in Platform carry `#pragma warning disable RS0030 // IO layer: <why>`.
- **Time:** never `DateTime.Now/UtcNow/Today`, `DateTimeOffset.Now/UtcNow` — inject `TimeProvider`.
- **XAML:** `x:Bind` only (no `{Binding`, `DisplayMemberPath`, `TextMemberPath`, `SelectedValuePath`); every view is a `Page`/`UserControl` in a `Frame`; `ItemsSource` is `ObservableCollection<T>`/`List<T>` of VM classes (strings through `UiFormat.Items`); images only via `ms-appx:///` or the `Thumb.Key` attached property.
- **AOT safety:** no C# cast, `as`, type test or unboxing of a projected WinRT value (the GridSplitter and `AppWindow.Presenter` crashes came from exactly that); read typed members instead (`CalendarDatePickerDateChangedEventArgs.NewDate`, `ToggleSwitch.IsOn`, `ItemsView` items through the VM). Managed objects you passed in yourself (`StageArgs`, a row VM in `DataContext`) may be type-tested as the existing pages do. COM interop is source-generated only (`[GeneratedComInterface]`/`[GeneratedComClass]`/`[LibraryImport]`), never `[ComImport]`, `Marshal.GetObjectForIUnknown` or `dynamic`.
- **JSON:** System.Text.Json source-generated contexts only; serialised polymorphic types are `closed` records with `[JsonPolymorphic]`; unions stay in memory.
- **Tests:** xUnit v3 on MTP; run with `dotnet test --solution uas-sort.slnx` (whole suite) or `dotnet test --project <test csproj> -- --filter-class "*Name"` (one class; `--filter-method "*Name*"` for one method). Windows-only tests live in `UasSort.Platform.Tests`. Temp artefacts only under `%TEMP%\uas-sort-test-*` (`TestTempDir`, Platform's `TempDir`/`TestEnv`), deleted by the test; a test that sends anything to the Recycle Bin purges **only its own items** (`RecycleBinPurge.PurgeOwn`).
- **Never touch user data:** nothing under `C:\Users\damia\OneDrive` is written, nothing on `D:\` or a real card is read or written, and `UASSORT_GOLDEN` is never set. Fake-FS tests may use path strings such as `D:\LR_Catalog` or `X:\Lightroom`; no test does real IO there.
- **Global usings win over local ones:** if a duplicate-using diagnostic appears (CS0105/CS8933/IDE0005 under TreatWarningsAsErrors), delete the local `using`.
- **Markdown in docs:** outside code, write `&lt;` instead of a raw `<`.
- **Commits:** one per task, conventional prefix (`feat:`, `test:`, `docs:`, `chore:`), message given in the task's commit step, ending with **the executing session's own** `Co-Authored-By` / `Claude-Session` trailer lines from its system reminder (written below as `<session trailer>`; never copy another session's lines). Use `git commit -F - <<'EOF' … EOF` from Git Bash.

## Review Focus

Inputs the spec implies but does not spell out, most likely to bite first; each has its pinning test, marked **[Review Focus n]**, in the owning task.

1. **Cloud-only (online-only OneDrive) photos and set members are never hydrated** — the survey, the verifier, the thumbnails and the Lightroom index read only files whose attributes say they are local; a cloud-only photo with no ledger record is "date unknown (cloud-only)" and not eligible, one with a ledger record is dated from the ledger without being opened, and its verification says "not on this PC (cloud-only) — couldn't check". → Task P.7 (`Survey_CloudOnlyPhoto_IsDateUnknown_AndNeverOpened`), Task P.8 (`Build_ACloudOnlyLibraryFile_IsNeverOpened`), Task P.9 (`Verify_ACloudOnlyPhoto_CantBeChecked`, `VerifyMode_ACloudOnlyLedgerDatedPhoto_IsNeverOpened_AndSaysCantCheck`) and Task P.12 (`Thumbnails_CloudOnlyPhoto_IsNeverOpened`).
2. **A set straddling the cutoff** (a hyperlapse that runs past midnight local time, or a pano whose last frame is on the next day) — the whole set is "not eligible", listed with "part of the set was shot after Jun 30", never split. → Task P.9 (`Build_ASetStraddlingTheCutoff_IsNotEligible`).
3. **Shots that differ only in sub-seconds** (bursts/AEB in one second) — a Lightroom match needs equal sub-seconds when both have them; when sub-seconds can't tell shots apart, the shots are verified only if Lightroom holds at least as many matching **DNG** files, otherwise none is verified ("3 shots in the same second; only 1 in Lightroom — can't tell which"); when a shot of that second could not be read (cloud-only or unreadable, counted by its ledger capture time), only an exact sub-second match verifies. → Task P.9 (`Verify_ShotsInTheSameSecond_NeedOneLightroomFileEach`, `Verify_AnUnreadableShotInTheSameSecond_LeavesOnlyExactSubSecondMatches`).
4. **Items changed between review and run** (a file re-synced with another size or mtime, a file deleted, a file added to a set folder) — the item is skipped "changed since review", nothing of it is moved and nothing is recorded. → Task P.10 (`Run_ItemsChangedSinceReview_AreSkipped`).
5. **The Recycle Bin can't take an item** (an online-only OneDrive file, a volume without a Recycle Bin, a file too large for it) — the shell's permanent delete is refused, the item stays in Picture Offload, is reported "kept: Windows would delete it permanently", and gets no ledger record. → Task P.10 (`Run_AnItemTheRecycleBinCantTake_IsKept_AndNotRecorded`) and Task P.11 (`RecycleSink_RefusesEveryDeleteWithoutTheRecycleFlag`, `Outcome_ARefusal_IsNotRecyclable_WhateverTheShellReturned`; acceptance step 2 proves the real OneDrive behaviour).

## Resolved ambiguities (binding for this plan)

1. **Recycler port shape.** The spec's `RecycleAsync(plan, progress, ct)` is split: `IPhotoRootRecyclerFactory.Open(ConfirmedPhotoCleanupPlan)` returns an `IPhotoRootRecycler` with `Stat(path)` and `Recycle(path)` for one item; `PhotoCleanupExecutor.RunAsync(confirmed, env, progress, ct)` is the async whole-plan entry. This is what lets the executor re-check each item just before its move and append the ledger record right after it.
2. **Interop choice.** `IFileOperation` (not `SHFileOperationW`): with `FOF_NOCONFIRMATION`, `SHFileOperationW` silently deletes permanently when the Recycle Bin can't take an item; `IFileOperation` lets an `IFileOperationProgressSink` refuse exactly those items in `PreDeleteItem` (the shell clears `TSF_DELETE_RECYCLE_IF_POSSIBLE` for them). It is declared with `[GeneratedComInterface]`/`[GeneratedComClass]` (AOT-safe source-generated COM), created with `[LibraryImport] CoCreateInstance` + `ComInterfaceMarshaller<T>`, and run on a dedicated STA thread (IFileOperation is STA-only). Flags: `FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT | FOFX_RECYCLEONDELETE | FOFX_EARLYFAILURE`.
3. **Reading photo-root and Lightroom files.** The existing guard never lets library files be opened, and the card reader is bound to a card. A new guarded op `IoOp.PhotoCleanupRead` and port `IPhotoFileReader` read only files directly in the photo root or in a direct child folder, and files under the Lightroom folder outside every catalog path; placeholders stay refused by guard rule 1.
4. **Which folders are sets.** A direct child folder of the photo root is a set when its name looks like a DJI set folder (`^\d{3}_\d{4}( yyyy-MM-dd)?( \(n\))?$`, Ref §8.8) or the ledger holds a set-member `file` record inside it, **and** it holds only photo files directly inside. Anything else (other folders, non-photo files) is listed "Not touched".
5. **Set kind** comes from the members' ledger `src` (`/PANORAMA/` or `/HYPERLAPSE/`); without a ledger record it is Unknown and only the "every frame in Lightroom" rule applies.
6. **The stitched panorama** (the scan has no stitched-image rule: Ref §8.8 files in-drone stitches as ordinary photos) is identified narrowly: among the loose **JPG-only** photo units of the same camera Model whose DateTimeOriginal lies within [first frame, last frame + 2 min] there must be **exactly one**, and it must be **panorama-shaped** (its longer side at least twice its shorter side, from EXIF PixelXDimension/PixelYDimension or the JPEG frame; `PhotoCleanupRules.LooksLikePanorama`); the set is verified when that JPG has a Lightroom match (DNG or JPG). Two or more JPGs in the window, a normal-shaped JPG, or unknown dimensions leave the set unverified with the reason. A wide-angle pano narrower than 2:1 therefore stays unverified (safe side). UNVERIFIED against real DJI output (acceptance step 3 checks it).
7. **Hyperlapse result video (spec §4 as written):** a ledger `file` record with root `video` whose session matches a member's session (`SessionKey.SameSession`) always counts; one whose `captureUtc` lies within the set's span ± 2 min counts **only when the set's drone serial is known and equals the video's**. A window match with an unknown serial on either side does not verify ("a video in the library was shot then, but these frames carry no drone serial — can't tell it is this hyperlapse's result"). See Review notes: DJI stills usually carry no serial, so in practice hyperlapse sets verify only through their frames.
8. **EXIF fallback date (as the scan resolves a still, `TimeResolver.Capture`/`ZoneFor`):** UTC = DateTimeOriginal − OffsetTimeOriginal when present; otherwise the drone clock from the saved `Settings.DroneClockMode`/`DroneClockZone` (`DroneClock.Learn` with no videos = `ClockMode.Setting`, through `ClockConversion.ToUtc`, the photo's own GPS zone as the site zone). The local date is that UTC in the photo's own GPS zone, else the PC zone. Only when the clock zone is unknown does the date fall back to the calendar date of the naive DateTimeOriginal (and `CaptureUtc` stays null). `PhotoCaptureClock` (Task P.7) does this.
8a. **Ledger date source (spec §3(1)):** only the `file` record whose `Dest` is this exact file (and whose size equals the file's size: a file replaced at the same path is dated from its EXIF instead). No fallback to records of the same name and size elsewhere.
8b. **Date-mode acknowledgement.** Spec §2 asks for the second acknowledgement only in verify mode, and the plan follows it; the date-mode acknowledgement text says the items are "not checked against Lightroom" so the single checkbox is not silent about it (see Review notes for the open question to the user).
9. **Lightroom index range:** files with mtime before (first eligible date − 2 days) are skipped without being opened; dated folders (`yyyy`, `yyyy-MM`, `yyyy-MM-dd…`) outside [first − 1 day, cutoff + 1 day] are not entered.
10. **Photo-root listing errors block the page** ("Part of … couldn't be listed; nothing will be moved"); a partial listing never feeds a plan.
11. **No `BannedSymbols.txt` change** (its exact set is asserted by `BannedSymbolsTests`); exclusivity is enforced by a source-guard test instead.
12. **Invalid `lightroomFolder` in settings.json** is dropped to null on load, never a reason to treat the file as corrupt.
13. **Thumbnails** reuse `ThumbnailCache` through `CardThumbnails` with `photo-root:`-prefixed keys served by a new `PhotoRootThumbnails`; JPG-only units without an embedded thumbnail use the whole JPEG when it is at most 16 MB.
14. **Where the page opens from:** the title-bar button and the Settings link work on the Card, Review, Verdict and Settings stages; Done/Back returns to the stage it was opened from.
15. **Revocation:** an existing `revoke` record naming a `photoDelete` id revokes it (same as decisions).

## File Structure

```
src/UasSort.Core/
  Model/Settings.cs                         M  Settings.LightroomFolder (optional, last parameter)
  Model/Probes.cs                           M  StillInfo.SubSec, PixelWidth, PixelHeight (optional, last parameters)
  Model/LedgerRecords.cs                    M  PhotoDeleteRecord, JSON "photoDelete"
  Model/Ledger.cs                           M  LedgerPhotoDelete; LedgerSnapshot.PhotoDeletes (init property)
  Guard/GuardTypes.cs                       M  IoOp.PhotoCleanupRead, IoOp.PhotoRootRecycle; GuardContext.LightroomFolder/PhotoCleanup
  Guard/IoGuardPolicy.cs                    M  the two ops; Lightroom folder and catalog read-only
  Ports/Ports.cs                            M  IPhotoFileReader, PhotoItemStat, RecycleOk/RecycleError/RecycleResult, IPhotoRootRecycler(+Factory); IReportStore.Save(PhotoCleanupReport)
  Settings/SettingsCodec.cs                 M  drops an invalid lightroomFolder
  Media/StillProbe.cs                       M  reads SubSecTimeOriginal and the pixel dimensions
  Ledger/LedgerSnapshotBuilder.cs           M  builds PhotoDeletes (revocable)
  Planning/NewnessRules.Photo.cs            M  rule 1 counts photoDelete like a file record
  Offload/AuditCategorizer.cs               M  photoDelete → InLedger
  Cleanup/CleanupRules.cs                   M  FileFacts.PhotoDelete; photoDelete → Evidence (HistoryOnly)
  Cleanup/CleanupPlanner.Candidates.cs      M  passes the photoDelete into FileFacts
  Cleanup/CleanupExecutor.cs                M  ProofHolds (re-check accepts photoDelete for photos)
  Json/CoreJsonContext.cs                   M  PhotoCleanupReport
  Cleanup/Photos/LightroomRules.cs          C  catalog paths, folder validation
  Cleanup/Photos/ExifStamp.cs               C  DateTimeOriginal + sub-seconds + Model, SameShot
  Cleanup/Photos/PhotoDeleteRecords.cs      C  PhotoCleanupMode, PhotoEvidence, record field values
  Cleanup/Photos/PhotoDeleteTexts.cs        C  newness and card-cleanup texts
  Cleanup/Photos/PhotoCleanupRules.cs       C  photo extensions, cloud-only bits, set-folder names, texts
  Cleanup/Photos/PhotoCleanupModel.cs       C  PhotoMember, PhotoItem, PhotoRow, PhotoSurvey, request/ack/totals/progress
  Cleanup/Photos/PhotoCleanupPaths.cs       C  what a row recycles
  Cleanup/Photos/PhotoCleanupFingerprint.cs C
  Cleanup/Photos/PhotoCleanupPlan.cs        C  plan + Confirm
  Cleanup/Photos/ConfirmedPhotoCleanupPlan.cs C
  Cleanup/Photos/PhotoExifCache.cs          C  EXIF through IPhotoFileReader, never for cloud-only
  Cleanup/Photos/PhotoCaptureClock.cs       C  EXIF capture time → UTC and local date as the scan does
  Cleanup/Photos/PhotoCleanupPlanner.Survey.cs C
  Cleanup/Photos/LightroomIndex.cs          C
  Cleanup/Photos/PhotoCleanupVerifier.cs    C
  Cleanup/Photos/PhotoCleanupPlanner.Build.cs C
  Cleanup/Photos/PhotoCleanupRun.cs         C  outcomes, stop, environment, result, progress
  Cleanup/Photos/PhotoCleanupExecutor.cs    C
  Cleanup/Photos/PhotoCleanupReport.cs      C  report records + PhotoCleanupReports.Build
  Cleanup/Photos/PhotoRootThumbnails.cs     C
src/UasSort.Platform/
  Io/GuardContexts.cs                       M  ForPhotoCleanup
  Io/FolderFacts.cs                         C  Exists (attributes only)
  Io/GuardedPhotoFileReader.cs              C
  Io/StaWorker.cs                           C
  Io/WindowsPhotoRootRecycler.cs            C  recycler + factory
  Win32/FileOperationCom.cs                 C  IFileOperation / sink / LibraryImports
  Stores/RecycleBinPurge.cs                 C  internal; tests and selftest only: purge own %TEMP% items from the Recycle Bin
  Stores/SelfTestSandbox.RecycleBin.cs      C  PurgeOwnRecycleBinItems (the selftest's only way to RecycleBinPurge)
  Stores/ReportStore.cs                     M  "-photos" report
  PlatformServices.cs                       M  PhotoRecyclers, PhotoReaderFor
src/UasSort.Review/
  PhotoCleanup/PhotoCleanupAvailability.cs  C
  PhotoCleanup/PhotoCleanupEngine.cs        C  engine, preparation, ports, PhotoCleanupEngines.Create
  PhotoCleanup/PhotoCleanupRowVm.cs         C
  PhotoCleanup/PhotoCleanupResultVm.cs      C
  PhotoCleanup/PhotoCleanupVm.cs            C
  Shell/ShellVm.cs                          M  Stage.PhotoCleanup, command, availability, open/close
  Settings/SettingsPageVm.cs                M  LightroomFolder, ChangeLightroomFolder, ClearLightroomCommand
src/UasSort.App/
  Pages/PhotoCleanupPage.xaml(.cs)          C
  Pages/ShellPage.xaml(.cs)                 M  button, reason text, stage page
  Pages/SettingsPage.xaml(.cs)              M  Lightroom row + picker, link
  Services/CardThumbnails.cs                M  photo-root key prefix → PhotoRootThumbnails
  Services/DeviceChangeWatcher.cs           M  suppressed on the PhotoCleanup stage
  CompositionRoot.cs                        M  CreatePhotoCleanup, FolderExists, selftest LightroomFolder
  SelfTest/SelfTestChecks.cs                M  registry entry
  SelfTest/SelfTestChecks.Pages.cs          M  PageFactories
  SelfTest/SelfTestChecks.Review.cs         M  ledger kinds include PhotoDeleteRecord
  SelfTest/SelfTestChecks.PhotoCleanup.cs   C  photoCleanup.flow
  SelfTest/ledger-v1.jsonl                  M  regenerated (one photoDelete line)
tests/UasSort.Testing/
  SyntheticDngBuilder.cs                    M  SubSecTimeOriginal, ExifPixels
  LedgerSamples.cs                          M  photoDelete sample
  OffloadFakes.cs                           M  MemReportStore.Save(PhotoCleanupReport)
  PhotoCleanupFixtures.cs                   C
  PhotoCleanupFakes.cs                      C  FakePhotoFileReader, FakePhotoRootRecyclerFactory
tests/UasSort.Core.Tests/…                  C/M  per task
tests/UasSort.Review.Tests/…                C/M  per task
tests/UasSort.Platform.Tests/…              C/M  per task
docs/superpowers/plans/2026-09-27-uas-sort/00-interfaces.md  M  registry section
docs/superpowers/plans/2026-10-04-picture-offload-cleanup.md C  copy of this plan (P.16)
docs/superpowers/specs/2026-10-04-picture-offload-cleanup-design.md C  copy of the spec (P.16)
README.md                                   M  "Clean up Picture Offload" section
```

---
### Task P.1: Lightroom library folder setting and catalog rules

**Files:**
- Modify: `src/UasSort.Core/Model/Settings.cs` (the `Settings` record)
- Modify: `src/UasSort.Core/Settings/SettingsCodec.cs` (the success `return` of `Parse`)
- Create: `src/UasSort.Core/Cleanup/Photos/LightroomRules.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/Photos/LightroomRulesTests.cs`, `tests/UasSort.Core.Tests/Config/LightroomFolderSettingsTests.cs`

**Interfaces:**
- Consumes: `PathRules` (`Normalize`, `IsSameOrUnder`, `IsStrictlyUnder`, `Overlaps`), `Settings`, `SettingsCodec`, `SettingsDefaults` (existing).
- Produces:
  - `Settings(..., bool RootsConfirmed, string? LightroomFolder = null)` — new optional last parameter; JSON key `lightroomFolder`.
  - `public static class LightroomRules { const string CatalogFolder = @"D:\LR_Catalog"; const string CatalogFolderName = "LR_Catalog"; static bool IsCatalogPath(string path); static bool IsCatalogName(string name); static string? FolderRefusal(string folder, Settings settings); }` (namespace `UasSort.Core.Cleanup`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/UasSort.Core.Tests/Cleanup/Photos/LightroomRulesTests.cs
namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class LightroomRulesTests
{
    private static readonly Settings S = FakeLayout.Settings();   // video C:\Users\u\OneDrive\Pictures\UAS Videos, photo …\Picture Offload

    [Theory]
    [InlineData(@"D:\LR_Catalog", true)]
    [InlineData(@"d:\lr_catalog\Lightroom Catalog.lrcat", true)]
    [InlineData(@"X:\Photos\LR_Catalog\x.dng", true)]
    [InlineData(@"X:\Photos\Lightroom Catalog.lrcat-data\a", true)]
    [InlineData(@"X:\Photos\Lightroom Catalog.lrcat.lock", true)]
    [InlineData(@"X:\Photos\Lightroom Catalog Previews.lrdata\0\x.lrprev", true)]
    [InlineData(@"X:\Photos\Lightroom Catalog Smart Previews.lrdata", true)]
    [InlineData(@"X:\Photos\2026\2026-09-27\Damian_20260927_001.dng", false)]
    [InlineData(@"D:\Lightroom Library\2026\x.dng", false)]
    public void IsCatalogPath_CoversTheCatalogItsFilesAndPreviews(string path, bool expected)
        => Assert.Equal(expected, LightroomRules.IsCatalogPath(path));

    [Theory]
    [InlineData(@"X:\Photos\Lightroom", null)]
    [InlineData(@"D:\Lightroom Library", null)]
    [InlineData("", "Pick a folder")]
    [InlineData(@"relative\folder", "Pick a folder on a drive")]
    [InlineData(@"D:\LR_Catalog", "That is Lightroom's catalog folder; pick the folder that holds the imported photos")]
    [InlineData(@"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload", "That folder is inside the photo folder (Picture Offload)")]
    [InlineData(@"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload\LR", "That folder is inside the photo folder (Picture Offload)")]
    [InlineData(@"C:\Users\u\OneDrive\Pictures", "That folder contains the photo folder (Picture Offload)")]
    [InlineData(@"C:\Users\u\OneDrive\Pictures\UAS Videos\2026", "That folder is inside the video folder")]
    public void FolderRefusal_FollowsTheSettingsRule(string folder, string? expected)
        => Assert.Equal(expected, LightroomRules.FolderRefusal(folder, S));

    [Fact]
    public void FolderRefusal_APreviousPhotoRoot_IsRefused()
        => Assert.Equal("That folder overlaps a previous photo folder",
                        LightroomRules.FolderRefusal(@"X:\Old Offload\sub", S with { PreviousPhotoRoots = [@"X:\Old Offload"] }));
}
```

```csharp
// tests/UasSort.Core.Tests/Config/LightroomFolderSettingsTests.cs
namespace UasSort.Core.Tests.Config;

public sealed class LightroomFolderSettingsTests
{
    private const string Pictures = @"C:\Users\u\Pictures";

    [Fact]
    public void Settings_LightroomFolder_DefaultsToNull_AndRoundTrips()
    {
        Settings d = SettingsDefaults.Derive(Pictures);
        Assert.Null(d.LightroomFolder);
        var s = d with { RootsConfirmed = true, LightroomFolder = @"X:\Photos\Lightroom" };
        string json = SettingsCodec.Serialize(s);
        Assert.Contains("\"lightroomFolder\": \"X:\\\\Photos\\\\Lightroom\"", json, StringComparison.Ordinal);
        SettingsParse back = SettingsCodec.Parse(json);
        Assert.Null(back.Error);
        Assert.Equal(@"X:\Photos\Lightroom", back.Settings!.LightroomFolder);
        Assert.Equal(json, SettingsCodec.Serialize(back.Settings));
    }

    [Fact]
    public void Settings_Parse_AFileWithoutTheKey_HasNoLightroomFolder()
    {
        SettingsParse p = SettingsCodec.Parse("""
            { "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\V\\Picture Offload", "radiusMiles": 50, "gapDays": 1,
              "droneClockMode": "Zone", "droneClockZone": "America/New_York", "copyJpgTwin": true, "rootsConfirmed": true }
            """);
        Assert.Null(Assert.IsType<Settings>(p.Settings).LightroomFolder);
    }

    [Theory]
    [InlineData(@"C:\\V\\Picture Offload\\LR")]
    [InlineData(@"D:\\LR_Catalog")]
    [InlineData(@"C:\\V\\Exports")]
    [InlineData(@"C:\\")]
    public void Settings_Parse_AnInvalidLightroomFolder_IsDropped_NotFatal(string jsonPath)
    {
        SettingsParse p = SettingsCodec.Parse($$"""
            { "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\V\\Picture Offload", "radiusMiles": 50, "gapDays": 1,
              "droneClockMode": "Zone", "droneClockZone": "America/New_York", "copyJpgTwin": true, "rootsConfirmed": true,
              "lightroomFolder": "{{jsonPath}}" }
            """);
        Assert.Null(p.Error);
        Assert.Null(Assert.IsType<Settings>(p.Settings).LightroomFolder);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*LightroomRulesTests" --filter-class "*LightroomFolderSettingsTests"`
Expected: build fails with `CS0103: The name 'LightroomRules' does not exist in the current context` and `CS1061: 'Settings' does not contain a definition for 'LightroomFolder'`.

- [ ] **Step 3: Implement**

In `src/UasSort.Core/Model/Settings.cs`, replace the `Settings` record declaration with:

```csharp
public sealed record Settings(int Schema, string VideoRoot, string PhotoRoot, ImmutableArray<string> PreviousPhotoRoots,
                              double RadiusMiles, int GapDays, StoredClockMode DroneClockMode /* last learned; default Zone */,
                              string DroneClockZone /* last learned zone; default America/New_York */, bool CopyJpgTwin,
                              MapSettings Map, LayoutSettings Layout, bool RootsConfirmed,
                              string? LightroomFolder = null /* Picture Offload cleanup's verify mode (spec 2026-10-04 §2); only ever read */);
                              // deliberately NO ledger-folder property: STJ would serialise a computed getter as "ledgerDir"
```

```csharp
// src/UasSort.Core/Cleanup/Photos/LightroomRules.cs
namespace UasSort.Core.Cleanup;

/// <summary>The Lightroom library folder (Settings.LightroomFolder) and Lightroom's catalog (spec 2026-10-04 §2, §4, §5). The app only
/// ever reads the library folder, and never lists into or opens the catalog: D:\LR_Catalog, any folder named LR_Catalog, any *.lrcat*
/// entry (the catalog and its -data, -wal, -shm and .lock files) and any *.lrdata folder (Previews, Smart Previews, Helper).</summary>
public static class LightroomRules
{
    public const string CatalogFolder = @"D:\LR_Catalog";
    public const string CatalogFolderName = "LR_Catalog";

    public static bool IsCatalogName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Equals(CatalogFolderName, StringComparison.OrdinalIgnoreCase)
            || name.Contains(".lrcat", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".lrdata", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCatalogPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var p = PathRules.Normalize(path);
        if (PathRules.IsSameOrUnder(p, CatalogFolder)) return true;
        foreach (var segment in p.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            if (IsCatalogName(segment)) return true;
        return false;
    }

    /// <summary>Why <paramref name="folder"/> can't be the Lightroom library folder, or null: it must be a full path, not the catalog, not
    /// inside the photo root, the video root or a previous photo root, and not an ancestor of the photo root.</summary>
    public static string? FolderRefusal(string folder, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(folder)) return "Pick a folder";
        var f = PathRules.Normalize(folder.Trim());
        if (!Path.IsPathFullyQualified(f)) return "Pick a folder on a drive";
        if (IsCatalogPath(f)) return "That is Lightroom's catalog folder; pick the folder that holds the imported photos";
        if (PathRules.IsSameOrUnder(f, settings.PhotoRoot)) return "That folder is inside the photo folder (Picture Offload)";
        if (PathRules.IsStrictlyUnder(settings.PhotoRoot, f)) return "That folder contains the photo folder (Picture Offload)";
        if (PathRules.IsSameOrUnder(f, settings.VideoRoot)) return "That folder is inside the video folder";
        var previousRoots = settings.PreviousPhotoRoots.IsDefault ? ImmutableArray<string>.Empty : settings.PreviousPhotoRoots;
        foreach (var previous in previousRoots)
            if (PathRules.Overlaps(f, previous)) return "That folder overlaps a previous photo folder";
        return null;
    }
}
```

In `src/UasSort.Core/Settings/SettingsCodec.cs`, replace the final `return new SettingsParse(s with { … }, null);` of `Parse` with:

```csharp
        var fixedUp = s with
        {
            PreviousPhotoRoots = s.PreviousPhotoRoots.IsDefault ? [] : s.PreviousPhotoRoots,
            Map = s.Map ?? SettingsDefaults.Map(),
            Layout = s.Layout ?? SettingsDefaults.Layout(),
        };
        // Spec 2026-10-04 §2: an unusable Lightroom folder is dropped (verify mode is then simply unavailable), never fatal.
        var lightroom = fixedUp.LightroomFolder is { } lr && LightroomRules.FolderRefusal(lr, fixedUp) is null
            ? PathRules.Normalize(lr.Trim())
            : null;
        return new SettingsParse(fixedUp with { LightroomFolder = lightroom }, null);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*LightroomRulesTests" --filter-class "*LightroomFolderSettingsTests" --filter-class "*SettingsCodecTests"`
Expected: PASS (the existing `SettingsCodecTests` stay green: the new key does not contain "ledger").

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Model/Settings.cs src/UasSort.Core/Settings/SettingsCodec.cs src/UasSort.Core/Cleanup/Photos/LightroomRules.cs tests/UasSort.Core.Tests/Cleanup/Photos/LightroomRulesTests.cs tests/UasSort.Core.Tests/Config/LightroomFolderSettingsTests.cs
git commit -F - <<'EOF'
feat: add the Lightroom library folder setting and catalog rules

<session trailer>
EOF
```

---

### Task P.2: EXIF sub-seconds, pixel size and the shot stamp

**Files:**
- Modify: `src/UasSort.Core/Model/Probes.cs` (`StillInfo`)
- Modify: `src/UasSort.Core/Media/StillProbe.cs` (`Read`, new `SubSecDigits`)
- Modify: `tests/UasSort.Testing/SyntheticDngBuilder.cs` (EXIF IFD)
- Create: `src/UasSort.Core/Cleanup/Photos/ExifStamp.cs`
- Test: `tests/UasSort.Core.Tests/Media/StillProbeSubSecTests.cs`, `tests/UasSort.Core.Tests/Cleanup/Photos/ExifStampTests.cs`

**Interfaces:**
- Consumes: `StillProbe.Read(Stream)`, `SyntheticDngBuilder` (existing).
- Produces:
  - `StillInfo(DateTime? DtoNaive, TimeSpan? OffsetTime, GpsProbe Gps, string? Model, ByteRange? Thumb, string? SubSec = null, int? PixelWidth = null, int? PixelHeight = null)`.
  - `internal static string? StillProbe.SubSecDigits(string raw)`.
  - `SyntheticDngBuilder.SubSecTimeOriginal { get; init; }` (string?, EXIF 0x9291) and `WithSubSec(string digits)`; `SyntheticDngBuilder.ExifPixels { get; init; }` (`(int Width, int Height)?`, EXIF 0xA002/0xA003).
  - `public sealed record ExifStamp(DateTime Second, string? SubSec, string Model) { static ExifStamp? From(StillInfo? info); bool SameSecondAndModel(ExifStamp o); bool BothHaveSubSec(ExifStamp o); bool SameShot(ExifStamp o); }` (namespace `UasSort.Core.Cleanup`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/UasSort.Core.Tests/Media/StillProbeSubSecTests.cs
namespace UasSort.Core.Tests.Media;

public sealed class StillProbeSubSecTests
{
    [Fact]
    public void SubSecTimeOriginal_IsReadNextToTheDto()
    {
        var info = StillProbe.Read(new MemoryStream(new SyntheticDngBuilder().WithSubSec("045").Build()));
        Assert.Equal(SyntheticDngBuilder.Pano0001Dto, info.DtoNaive);
        Assert.Equal("045", info.SubSec);
    }

    [Fact]
    public void NoSubSecTag_IsNull()
        => Assert.Null(StillProbe.Read(new MemoryStream(new SyntheticDngBuilder().Build())).SubSec);

    [Fact]
    public void PixelDimensions_AreReadFromTheExifPixelTags_AndNullWithoutThem()
    {
        var info = StillProbe.Read(new MemoryStream(new SyntheticDngBuilder { ExifPixels = (8192, 4096) }.Build()));
        Assert.Equal(((int?)8192, (int?)4096), (info.PixelWidth, info.PixelHeight));
        var none = StillProbe.Read(new MemoryStream(new SyntheticDngBuilder().Build()));
        Assert.Equal(((int?)null, (int?)null), (none.PixelWidth, none.PixelHeight));
    }

    [Theory]
    [InlineData("045", "045")]
    [InlineData(" 12 ", "12")]
    [InlineData("5\0", "5")]
    [InlineData("  ", null)]
    [InlineData("x1", null)]
    public void SubSecDigits_KeepsTheLeadingDigits(string raw, string? expected)
        => Assert.Equal(expected, StillProbe.SubSecDigits(raw));
}
```

```csharp
// tests/UasSort.Core.Tests/Cleanup/Photos/ExifStampTests.cs
namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class ExifStampTests
{
    private static readonly DateTime S = new(2026, 9, 27, 14, 5, 0);

    private static ExifStamp St(string? sub, string model = "FC9113", int second = 0) => new(S.AddSeconds(second), sub, model);

    [Fact]
    public void From_NeedsADtoAndAModel_AndDropsFractionsFromTheSecond()
    {
        var info = new StillInfo(S.AddMilliseconds(300), null, new NoFix(NoFixReason.NoGpsTag), " FC9113 ", null, "3");
        var stamp = ExifStamp.From(info)!;
        Assert.Equal(S, stamp.Second);
        Assert.Equal(("3", "FC9113"), (stamp.SubSec, stamp.Model));
        Assert.Null(ExifStamp.From(info with { DtoNaive = null }));
        Assert.Null(ExifStamp.From(info with { Model = null }));
        Assert.Null(ExifStamp.From(null));
    }

    [Theory]
    [InlineData("5", "500", true)]
    [InlineData("05", "5", false)]
    [InlineData(null, "123", true)]
    [InlineData("123", null, true)]
    [InlineData("1234567", "12345678", true)]
    [InlineData("100", "600", false)]
    public void SameShot_ComparesSubSecondsOnlyWhenBothHaveThem(string? a, string? b, bool expected)
        => Assert.Equal(expected, St(a).SameShot(St(b)));

    [Fact]
    public void SameShot_NeedsTheSameSecondAndModel()
    {
        Assert.False(St(null).SameShot(St(null, second: 1)));
        Assert.False(St(null).SameShot(St(null, model: "FC8282")));
        Assert.True(St(null).SameShot(St(null, model: "fc9113")));
        Assert.True(St("1").SameSecondAndModel(St("9")));
        Assert.True(St("1").BothHaveSubSec(St("9")));
        Assert.False(St(null).BothHaveSubSec(St("9")));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*StillProbeSubSecTests" --filter-class "*ExifStampTests"`
Expected: build fails (`CS1061: 'SyntheticDngBuilder' does not contain a definition for 'WithSubSec'`, `CS0246: … 'ExifStamp' could not be found`).

- [ ] **Step 3: Implement**

In `src/UasSort.Core/Model/Probes.cs`, replace the `StillInfo` record with:

```csharp
public sealed record StillInfo(DateTime? DtoNaive, TimeSpan? OffsetTime, GpsProbe Gps, string? Model, ByteRange? Thumb,
                               string? SubSec = null /* EXIF SubSecTimeOriginal digits; Picture Offload cleanup's Lightroom match */,
                               int? PixelWidth = null, int? PixelHeight = null /* the stitched-panorama shape test of Picture Offload cleanup */);
```

In `src/UasSort.Core/Media/StillProbe.cs`: add `using MetadataExtractor.Formats.Jpeg;` after `using MetadataExtractor.Formats.Exif;`; add `private const int TagSubSecTimeOriginal = 0x9291;` next to `TagOffsetTimeOriginal`; in `Read`, replace the final `return new StillInfo(...)` with:

```csharp
        string? subSec = dtoDir?.GetString(TagSubSecTimeOriginal) is { } raw ? SubSecDigits(raw) : null;
        var (width, height) = Pixels(dirs);
        return new StillInfo(dto, offset, Gps(dirs), string.IsNullOrEmpty(model) ? null : model, thumb, subSec, width, height);
```

and add the helpers at the end of the class:

```csharp
    /// <summary>The image size: EXIF PixelXDimension/PixelYDimension (0xA002/0xA003), else a JPEG's frame header; nulls when neither.</summary>
    private static (int? Width, int? Height) Pixels(IReadOnlyList<MetadataExtractor.Directory> dirs)
    {
        foreach (var d in dirs.OfType<ExifSubIfdDirectory>())
            if (d.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var w) && d.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var h)
                && w > 0 && h > 0)
                return (w, h);
        if (dirs.OfType<JpegDirectory>().FirstOrDefault() is { } jpeg
            && jpeg.TryGetInt32(JpegDirectory.TagImageWidth, out var jw) && jpeg.TryGetInt32(JpegDirectory.TagImageHeight, out var jh)
            && jw > 0 && jh > 0)
            return (jw, jh);
        return (null, null);
    }

    /// <summary>EXIF SubSecTimeOriginal ("045", " 12 ", "5\0") → its leading digits; null when there are none.</summary>
    internal static string? SubSecDigits(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var t = raw.Trim().TrimEnd('\0').Trim();
        var n = 0;
        while (n < t.Length && char.IsAsciiDigit(t[n])) n++;
        return n == 0 ? null : t[..n];
    }
```

In `tests/UasSort.Testing/SyntheticDngBuilder.cs`: add the property after `OffsetTimeOriginal`:

```csharp
    /// <summary>EXIF SubSecTimeOriginal (0x9291), e.g. "045"; null = absent.</summary>
    public string? SubSecTimeOriginal { get; init; }
```

add `public SyntheticDngBuilder WithSubSec(string digits) => this with { SubSecTimeOriginal = digits };` next to `WithDateTimeOriginal`, and in `Ifds` after the line `if (OffsetTimeOriginal is { } offset) exif.Add(Ascii(0x9011, offset));` add:

```csharp
        if (SubSecTimeOriginal is { } subSec) exif.Add(Ascii(0x9291, subSec));
        if (ExifPixels is { } px)                                            // after 0x9291: IFD entries stay sorted by tag
        {
            exif.Add(LongEntry(0xA002, (uint)px.Width));
            exif.Add(LongEntry(0xA003, (uint)px.Height));
        }
```

and the property next to `SubSecTimeOriginal`:

```csharp
    /// <summary>EXIF PixelXDimension/PixelYDimension (0xA002/0xA003); null = absent.</summary>
    public (int Width, int Height)? ExifPixels { get; init; }
```

```csharp
// src/UasSort.Core/Cleanup/Photos/ExifStamp.cs
using System.Globalization;

namespace UasSort.Core.Cleanup;

/// <summary>What identifies one shot across Picture Offload and the Lightroom library (spec 2026-10-04 §4): DateTimeOriginal to the
/// second (naive camera clock), its sub-seconds when present, and the camera Model. Lightroom's "Copy as DNG" keeps all three.</summary>
public sealed record ExifStamp(DateTime Second, string? SubSec, string Model)
{
    public static ExifStamp? From(StillInfo? info)
    {
        if (info?.DtoNaive is not { } dto || string.IsNullOrWhiteSpace(info.Model)) return null;
        var second = new DateTime(dto.Ticks - dto.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified);
        return new ExifStamp(second, info.SubSec, info.Model.Trim());
    }

    public bool SameSecondAndModel(ExifStamp o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return Second == o.Second && string.Equals(Model, o.Model, StringComparison.OrdinalIgnoreCase);
    }

    public bool BothHaveSubSec(ExifStamp o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return SubSec is not null && o.SubSec is not null;
    }

    /// <summary>Same second and Model, and the same sub-seconds when both have them ("5" = "500"; "05" ≠ "5").</summary>
    public bool SameShot(ExifStamp o)
        => SameSecondAndModel(o) && (!BothHaveSubSec(o) || string.Equals(Ticks(SubSec!), Ticks(o.SubSec!), StringComparison.Ordinal));

    private static string Ticks(string digits) => digits.Length >= 7 ? digits[..7] : digits.PadRight(7, '0');

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Second:yyyy-MM-dd HH:mm:ss}{(SubSec is null ? "" : "." + SubSec)} {Model}");
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*StillProbeSubSecTests" --filter-class "*ExifStampTests" --filter-class "*StillProbeTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Model/Probes.cs src/UasSort.Core/Media/StillProbe.cs tests/UasSort.Testing/SyntheticDngBuilder.cs src/UasSort.Core/Cleanup/Photos/ExifStamp.cs tests/UasSort.Core.Tests/Media/StillProbeSubSecTests.cs tests/UasSort.Core.Tests/Cleanup/Photos/ExifStampTests.cs
git commit -F - <<'EOF'
feat: read EXIF sub-seconds and pixel size, and add the shot stamp used to match Lightroom

<session trailer>
EOF
```

---

### Task P.3: The photoDelete ledger record

**Files:**
- Modify: `src/UasSort.Core/Model/LedgerRecords.cs` (attributes + new record)
- Modify: `src/UasSort.Core/Model/Ledger.cs` (`LedgerPhotoDelete`, `LedgerSnapshot` body)
- Modify: `src/UasSort.Core/Ledger/LedgerSnapshotBuilder.cs` (`Build`, new `AddPhotoDelete`)
- Create: `src/UasSort.Core/Cleanup/Photos/PhotoDeleteRecords.cs`
- Modify: `tests/UasSort.Core.Tests/Ledger/LedgerLines.cs` (builder), `tests/UasSort.Core.Tests/Ledger/LedgerCodecTests.cs` (`EveryKind`), `tests/UasSort.Core.Tests/Model/PlanModelTests.cs` (`LedgerRecord_IsAClosedHierarchyOfEightKinds`)
- Modify: `tests/UasSort.Testing/LedgerSamples.cs`, `src/UasSort.App/SelfTest/SelfTestChecks.Review.cs` (`JsonLedger` expected kinds), `src/UasSort.App/SelfTest/ledger-v1.jsonl` (regenerated)
- Test: `tests/UasSort.Core.Tests/Ledger/LedgerPhotoDeleteTests.cs`

**Interfaces:**
- Consumes: `LedgerCodec`, `LedgerReader`, `TestLedger`, `LedgerLines` (existing).
- Produces:
  - `public sealed record class PhotoDeleteRecord(int V, string Id, string Machine, string Run, DateTime At, string Name, long Size, string Dest, DateTime? CaptureUtc, string? Set, string Evidence, string Mode, DateOnly Cutoff) : LedgerRecord(V, Id, Machine)`; JSON `"t":"photoDelete"`.
  - `public sealed record LedgerPhotoDelete(string Id, FileKey Key, string Dest, DateTime AtUtc, DateTime? CaptureUtc, string? Set, string Evidence, string Mode, DateOnly Cutoff, string Machine, string Run)`.
  - `LedgerSnapshot.PhotoDeletes { get; init; }` : `ImmutableDictionary<FileKey, LedgerPhotoDelete>` (unrevoked, latest per key; default empty).
  - `public enum PhotoCleanupMode { BeforeDate, Verify }`, `public enum PhotoEvidence { Lightroom, HyperlapseResult, PanoramaStitch, DateOnly, UnverifiedConfirmed }`, `public static class PhotoDeleteRecords { const string EvidenceLightroom/EvidenceHyperlapseResult/EvidencePanoramaStitch/EvidenceDateOnly/EvidenceUnverifiedConfirmed, ModeBeforeDate, ModeVerify; static string Evidence(PhotoEvidence); static string Mode(PhotoCleanupMode); static bool IsEvidence(string?); static bool IsMode(string?); }` (namespace `UasSort.Core.Cleanup`).
  - `LedgerLines.PhotoDelete(string id, string name, long size, string evidence = "lightroom", string? set = null, DateTime? at = null, string mode = "verify")` (Core.Tests).

- [ ] **Step 1: Write the failing tests**

Add to `tests/UasSort.Core.Tests/Ledger/LedgerLines.cs`, after `CardDelete`:

```csharp
    public static PhotoDeleteRecord PhotoDelete(string id, string name, long size, string evidence = "lightroom", string? set = null,
                                                DateTime? at = null, string mode = "verify")
        => new(1, id, "DESKTOP-A", "p7a10001", at ?? Utc(2026, 10, 4, 20, 15), name, size,
               @"C:\Lib\UAS Videos\Picture Offload\" + (set is null ? "" : set + @"\") + name, Utc(2026, 6, 1, 20, 10), set,
               evidence, mode, new DateOnly(2026, 6, 30));
```

In `LedgerCodecTests.EveryKind` add the row (after `cardDelete`):

```csharp
        { "photoDelete", PhotoDelete("p0000001", "DJI_20260601121000_0002_D.DNG", 25_165_824) },
```

In `PlanModelTests`, rename `LedgerRecord_IsAClosedHierarchyOfEightKinds` to `LedgerRecord_IsAClosedHierarchyOfNineKinds`, add to `records` after the `CardDeleteRecord` line:

```csharp
            new PhotoDeleteRecord(1, "p", "DESKTOP-A", "r", at, "b.DNG", 2, @"C:\V\P\b.DNG", null, null, "lightroom", "verify", new DateOnly(2026, 9, 27)),
```

change `Assert.Equal(8, …)` to `Assert.Equal(9, …)`, and add `PhotoDeleteRecord => "photoDelete",` to the `Kind` switch after `CardDeleteRecord => "cardDelete",`.

```csharp
// tests/UasSort.Core.Tests/Ledger/LedgerPhotoDeleteTests.cs
using UasSort.Core.Ledger;
using static UasSort.Core.Tests.Ledger.LedgerLines;

namespace UasSort.Core.Tests.Ledger;

public sealed class LedgerPhotoDeleteTests
{
    private const string Dng = "DJI_20260601121000_0002_D.DNG";

    [Fact]
    public void PhotoDelete_IsIndexedByFileKey_WithItsFields_AndIsNoFileRecord()
    {
        var snap = TestLedger.Snapshot(PhotoDelete("p1", Dng, 25_165_824, evidence: "dateOnly", mode: "beforeDate"));
        var d = Assert.Single(snap.PhotoDeletes).Value;
        Assert.Equal(FileKey.Of(Dng, 25_165_824), d.Key);
        Assert.Equal(@"C:\Lib\UAS Videos\Picture Offload\" + Dng, d.Dest);
        Assert.Equal(("dateOnly", "beforeDate", new DateOnly(2026, 6, 30)), (d.Evidence, d.Mode, d.Cutoff));
        Assert.Equal(DateTimeKind.Utc, d.AtUtc.Kind);
        Assert.Empty(snap.ParseIssues);
        Assert.Empty(snap.Files);
    }

    [Fact]
    public void PhotoDelete_Revoked_IsGone_AndTheLatestWins()
    {
        Assert.Empty(TestLedger.Snapshot(PhotoDelete("p1", Dng, 1), Revoke("r1", "p1")).PhotoDeletes);
        var twice = TestLedger.Snapshot(PhotoDelete("p1", Dng, 1, at: Utc(2026, 10, 4)), PhotoDelete("p2", Dng, 1, at: Utc(2026, 10, 5)));
        Assert.Equal("p2", Assert.Single(twice.PhotoDeletes).Value.Id);
    }

    [Theory]
    [InlineData("bogus", "verify", "bad evidence 'bogus'")]
    [InlineData("lightroom", "sometimes", "bad mode 'sometimes'")]
    public void PhotoDelete_BadValues_AreParseIssues(string evidence, string mode, string issue)
    {
        var snap = TestLedger.Snapshot(PhotoDelete("p1", Dng, 1, evidence: evidence, mode: mode));
        Assert.Empty(snap.PhotoDeletes);
        Assert.Equal(issue, Assert.Single(snap.ParseIssues).Reason);
    }

    [Fact]
    public void PhotoDelete_SetMembers_CarryTheSetName()
    {
        var snap = TestLedger.Snapshot(PhotoDelete("p1", "PANO_0001.DNG", 10, set: "001_0087"), PhotoDelete("p2", "PANO_0002.DNG", 11, set: "001_0087"));
        Assert.Equal(2, snap.PhotoDeletes.Count);
        Assert.All(snap.PhotoDeletes.Values, d => Assert.Equal("001_0087", d.Set));
    }

    [Theory]
    [InlineData(PhotoEvidence.Lightroom, "lightroom")]
    [InlineData(PhotoEvidence.HyperlapseResult, "hyperlapseResult")]
    [InlineData(PhotoEvidence.PanoramaStitch, "panoramaStitch")]
    [InlineData(PhotoEvidence.DateOnly, "dateOnly")]
    [InlineData(PhotoEvidence.UnverifiedConfirmed, "unverifiedConfirmed")]
    public void PhotoDeleteRecords_EvidenceText(PhotoEvidence e, string text)
    {
        Assert.Equal(text, PhotoDeleteRecords.Evidence(e));
        Assert.True(PhotoDeleteRecords.IsEvidence(text));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*LedgerPhotoDeleteTests" --filter-class "*LedgerCodecTests" --filter-class "*PlanModelTests"`
Expected: build fails with `CS0246: The type or namespace name 'PhotoDeleteRecord' could not be found`.

- [ ] **Step 3: Implement**

In `src/UasSort.Core/Model/LedgerRecords.cs` add the attribute line after `[JsonDerivedType(typeof(CardDeleteRecord), "cardDelete")]`:

```csharp
[JsonDerivedType(typeof(PhotoDeleteRecord), "photoDelete")]
```

and at the end of the file:

```csharp
/// <summary>Picture Offload cleanup (spec 2026-10-04 §6): one per file moved from the photo root to the Recycle Bin, written after the move.
/// Evidence: lightroom|hyperlapseResult|panoramaStitch|dateOnly|unverifiedConfirmed. Mode: beforeDate|verify.</summary>
public sealed record class PhotoDeleteRecord(int V, string Id, string Machine, string Run, DateTime At, string Name, long Size,
    string Dest /* the Picture Offload path that went to the Recycle Bin */, DateTime? CaptureUtc, string? Set /* the card set name */,
    string Evidence, string Mode, DateOnly Cutoff) : LedgerRecord(V, Id, Machine);
```

In `src/UasSort.Core/Model/Ledger.cs`, add after `LedgerCardDelete`:

```csharp
public sealed record LedgerPhotoDelete(string Id, FileKey Key, string Dest, DateTime AtUtc, DateTime? CaptureUtc, string? Set,
                                       string Evidence, string Mode, DateOnly Cutoff, string Machine, string Run);
```

and give `LedgerSnapshot` a body: replace the final `ImmutableArray<string> SourceFiles, LedgerFolderStatus Status);` of its declaration with:

```csharp
    ImmutableArray<string> SourceFiles, LedgerFolderStatus Status)
{
    /// <summary>Unrevoked photoDelete records, the latest per key (spec 2026-10-04 §6). An init property, so every existing positional
    /// construction keeps compiling and gets none.</summary>
    public ImmutableDictionary<FileKey, LedgerPhotoDelete> PhotoDeletes { get; init; } = ImmutableDictionary<FileKey, LedgerPhotoDelete>.Empty;
}
```

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoDeleteRecords.cs
namespace UasSort.Core.Cleanup;

public enum PhotoCleanupMode { BeforeDate, Verify }

public enum PhotoEvidence { Lightroom, HyperlapseResult, PanoramaStitch, DateOnly, UnverifiedConfirmed }

/// <summary>The photoDelete record's field values (spec 2026-10-04 §6).</summary>
public static class PhotoDeleteRecords
{
    public const string EvidenceLightroom = "lightroom";
    public const string EvidenceHyperlapseResult = "hyperlapseResult";
    public const string EvidencePanoramaStitch = "panoramaStitch";
    public const string EvidenceDateOnly = "dateOnly";
    public const string EvidenceUnverifiedConfirmed = "unverifiedConfirmed";
    public const string ModeBeforeDate = "beforeDate";
    public const string ModeVerify = "verify";

    public static string Evidence(PhotoEvidence e) => e switch
    {
        PhotoEvidence.Lightroom => EvidenceLightroom,
        PhotoEvidence.HyperlapseResult => EvidenceHyperlapseResult,
        PhotoEvidence.PanoramaStitch => EvidencePanoramaStitch,
        PhotoEvidence.DateOnly => EvidenceDateOnly,
        PhotoEvidence.UnverifiedConfirmed => EvidenceUnverifiedConfirmed,
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };

    public static string Mode(PhotoCleanupMode m) => m == PhotoCleanupMode.Verify ? ModeVerify : ModeBeforeDate;

    public static bool IsEvidence(string? s)
        => s is EvidenceLightroom or EvidenceHyperlapseResult or EvidencePanoramaStitch or EvidenceDateOnly or EvidenceUnverifiedConfirmed;

    public static bool IsMode(string? s) => s is ModeBeforeDate or ModeVerify;
}
```

In `src/UasSort.Core/Ledger/LedgerSnapshotBuilder.cs`:
1. After `var cardDeletes = ImmutableArray.CreateBuilder<LedgerCardDelete>();` add `var photoDeletes = new List<LedgerPhotoDelete>();`.
2. In the record switch, after `case CardDeleteRecord c: …` add `case PhotoDeleteRecord d: error = AddPhotoDelete(d, photoDeletes); break;`.
3. After the `live` decisions loop add:

```csharp
        var removed = new Dictionary<FileKey, LedgerPhotoDelete>();
        foreach (LedgerPhotoDelete d in photoDeletes)
        {
            if (revoked.Contains(d.Id)) continue;
            if (!removed.TryGetValue(d.Key, out LedgerPhotoDelete? prev) || d.AtUtc > prev.AtUtc) removed[d.Key] = d;
        }
```

4. Append ` { PhotoDeletes = removed.ToImmutableDictionary() }` to the final `return new LedgerSnapshot(...)` expression (before the `;`).
5. Add the method after `AddCardDelete`:

```csharp
    private static string? AddPhotoDelete(PhotoDeleteRecord d, List<LedgerPhotoDelete> photoDeletes)
    {
        if (string.IsNullOrEmpty(d.Name) || d.Size < 0) return "bad name or size";
        if (string.IsNullOrEmpty(d.Dest) || string.IsNullOrEmpty(d.Run)) return "missing dest or run";
        if (!PhotoDeleteRecords.IsEvidence(d.Evidence)) return $"bad evidence '{d.Evidence}'";
        if (!PhotoDeleteRecords.IsMode(d.Mode)) return $"bad mode '{d.Mode}'";
        photoDeletes.Add(new LedgerPhotoDelete(d.Id, FileKey.Of(d.Name, d.Size), d.Dest, Utc(d.At), d.CaptureUtc is { } c ? Utc(c) : null,
                                               d.Set, d.Evidence, d.Mode, d.Cutoff, d.Machine, d.Run));
        return null;
    }
```

In `tests/UasSort.Testing/LedgerSamples.cs`, add to `records` after the `CardDeleteRecord` entry:

```csharp
            new PhotoDeleteRecord(v, "sample-photo-delete", machine, "run-selftest-0003", at.AddDays(2), still, 25_165_824,
                                  @"X:\UAS Videos\Picture Offload\" + still, at.AddHours(-2), null, "lightroom", "verify", new DateOnly(2025, 6, 1)),
```

In `src/UasSort.App/SelfTest/SelfTestChecks.Review.cs` (`JsonLedger`), add `"PhotoDeleteRecord"` at the end of the `expected` array.

Regenerate the selftest ledger asset:

Run: `dotnet run tools/fixtures/make-selftest-assets.cs -- .`
Then: `git status --short src/UasSort.App/SelfTest`
Expected: only ` M src/UasSort.App/SelfTest/ledger-v1.jsonl` (with one more line, `{"t":"photoDelete",…}`). If any other asset shows as modified, restore it with `git checkout -- <that file>` (the tool rewrites every asset; only the ledger may change in this task).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*LedgerPhotoDeleteTests" --filter-class "*LedgerCodecTests" --filter-class "*PlanModelTests" --filter-class "*LedgerSnapshotTests" --filter-class "*LedgerParserTests"`
Expected: PASS.
Run: `dotnet build uas-sort.slnx`
Expected: Build succeeded, 0 warnings (the App's selftest compiles with the new kind).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Model/LedgerRecords.cs src/UasSort.Core/Model/Ledger.cs src/UasSort.Core/Ledger/LedgerSnapshotBuilder.cs src/UasSort.Core/Cleanup/Photos/PhotoDeleteRecords.cs tests/UasSort.Core.Tests/Ledger/LedgerLines.cs tests/UasSort.Core.Tests/Ledger/LedgerCodecTests.cs tests/UasSort.Core.Tests/Model/PlanModelTests.cs tests/UasSort.Core.Tests/Ledger/LedgerPhotoDeleteTests.cs tests/UasSort.Testing/LedgerSamples.cs src/UasSort.App/SelfTest/SelfTestChecks.Review.cs src/UasSort.App/SelfTest/ledger-v1.jsonl
git commit -F - <<'EOF'
feat: add the photoDelete ledger record and its snapshot index

<session trailer>
EOF
```

---
### Task P.4: photoDelete as newness and card-cleanup evidence

**Files:**
- Create: `src/UasSort.Core/Cleanup/Photos/PhotoDeleteTexts.cs`
- Modify: `src/UasSort.Core/Planning/NewnessRules.Photo.cs` (rule 1)
- Modify: `src/UasSort.Core/Offload/AuditCategorizer.cs` (`Context.FromEvidence`, new `Context.PhotoDelete`)
- Modify: `src/UasSort.Core/Cleanup/CleanupRules.cs` (`FileFacts`, `Classify`)
- Modify: `src/UasSort.Core/Cleanup/CleanupPlanner.Candidates.cs` (`Candidate` → local `Facts`)
- Modify: `src/UasSort.Core/Cleanup/CleanupExecutor.cs` (`Run.EvidenceGone`, new `ProofHolds`)
- Test: `tests/UasSort.Core.Tests/Planning/PhotoDeleteNewnessTests.cs`, `tests/UasSort.Core.Tests/Cleanup/PhotoDeleteEvidenceTests.cs`

**Interfaces:**
- Consumes: `LedgerSnapshot.PhotoDeletes`, `LedgerPhotoDelete`, `PhotoDeleteRecords` (P.3); `PlanText.ShortDate` (existing).
- Produces:
  - `public static class PhotoDeleteTexts { static string Newness(LedgerPhotoDelete d); static string Evidence(LedgerPhotoDelete d); }` — "removed from Picture Offload on Oct 4"; "removed from Picture Offload after Lightroom import" (lightroom, panoramaStitch) / "removed from Picture Offload; its hyperlapse video is in the library" / "removed from Picture Offload by date" / "removed from Picture Offload (not confirmed in Lightroom)".
  - `FileFacts(..., string TzId, LedgerPhotoDelete? PhotoDelete = null)`.
  - `internal static bool CleanupExecutor.ProofHolds(ItemKind kind, FileProof p, FreshEvidence fresh, LedgerSnapshot ledger)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/UasSort.Core.Tests/Planning/PhotoDeleteNewnessTests.cs
using System.Globalization;
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class PhotoDeleteNewnessTests
{
    private static readonly DateTime Watermark = new(2026, 9, 27, 18, 24, 16, DateTimeKind.Utc);
    private static readonly DateTime RemovedAt = new(2026, 10, 4, 20, 0, 0, DateTimeKind.Utc);

    private static ItemTime T(RawItem r)
    {
        var utc = Clip.Utc(r.DroneStamp!.Value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("America/Anchorage"));
        return new ItemTime(utc, TimeSource.DroneClockZone, "America/Anchorage", TzSource.Gps, DateOnly.FromDateTime(local), local);
    }

    private static LedgerPhotoDelete Removed(string name, long size, string? set = null)
        => new($"p-{name}", FileKey.Of(name, size), @"X:\Picture Offload\" + name, RemovedAt, null, set, "lightroom", "verify",
               new DateOnly(2026, 9, 30), "DESKTOP-A", "run-p");

    private static Newness Of(RawItem r, params LedgerPhotoDelete[] removed)
    {
        var s = new PlanScenario().Card(r).Build();
        var ledger = s.Ledger with { PhotoDeletes = removed.ToImmutableDictionary(d => d.Key) };
        return NewnessRules.Photo(r.Unit, T(r), s.Library, ledger, null, new HashSet<DateOnly>(), Watermark);
    }

    [Fact]
    public void Rule1_APhotoRemovedFromPictureOffload_IsImported()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var imported = Assert.IsType<Imported>(Of(d, Removed(d.Name, d.Bytes)));
        Assert.Equal(Evidence.LedgerVerified, imported.By);
        Assert.Equal("removed from Picture Offload on Oct 4", imported.Why);
    }

    [Fact]
    public void Rule1_ASetCountsOnlyWhenEveryMemberWasRemovedUnderItsSetName()
    {
        var set = Clip.Set("001_0087", "2026-08-15 20:00:00", Sites.Anvil,
            ("PANO_0001.DNG", 13_751_808, new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc)),
            ("PANO_0002.DNG", 13_751_900, new DateTime(2026, 8, 16, 0, 0, 2, DateTimeKind.Utc)));
        Assert.IsType<Imported>(Of(set, Removed("PANO_0001.DNG", 13_751_808, "001_0087"), Removed("PANO_0002.DNG", 13_751_900, "001_0087")));
        Assert.IsNotType<Imported>(Of(set, Removed("PANO_0001.DNG", 13_751_808, "001_0087")));
        Assert.IsNotType<Imported>(Of(set, Removed("PANO_0001.DNG", 13_751_808, "002_0001"), Removed("PANO_0002.DNG", 13_751_900, "002_0001")));
    }
}
```

```csharp
// tests/UasSort.Core.Tests/Cleanup/PhotoDeleteEvidenceTests.cs
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Cleanup;

public sealed class PhotoDeleteEvidenceTests
{
    private const string P = "DJI_20260927140000_0002_D.DNG";
    private const string V = "DJI_20260927140000_0001_D.MP4";
    private static readonly CardDiffResult NoChange = new([], [], [], 0);

    internal static LedgerPhotoDelete Removed(string name, long size, string evidence = "lightroom", string? set = null)
        => new("p1", FileKey.Of(name, size), FakeLayout.PhotoRoot + @"\" + name, new DateTime(2026, 10, 4, 20, 0, 0, DateTimeKind.Utc), T0,
               set, evidence, "verify", new DateOnly(2026, 9, 30), "DESKTOP-A", "run-p");

    private static LedgerSnapshot With(LedgerSnapshot s, params LedgerPhotoDelete[] removed)
        => s with { PhotoDeletes = removed.ToImmutableDictionary(r => r.Key) };

    [Fact]
    public void Audit_APhotoRemovedFromPictureOffload_IsInLedger_WithTheRemovalDetail()
    {
        var b = new OffloadPlanBuilder();
        var photo = b.Photo(P, 200, T0, new IsNew(NewReason.NoMatch, null));
        var plan = b.Build();
        var units = AuditCategorizer.Categorize(plan.Base.Scan.Inventory, plan, null, With(plan.Base.Scan.Ledger, Removed(P, 200)), NoChange);
        var unit = units.Units.Single(u => u.Unit == photo);
        Assert.Equal(AuditCategory.InLedger, unit.Worst);
        Assert.Equal("removed from Picture Offload after Lightroom import", unit.Lines[0].Detail);
    }

    [Fact]
    public void Audit_AVideoKeyInPhotoDeletes_IsIgnored()
    {
        var b = new OffloadPlanBuilder();
        var video = b.Video(V, 100, T0, new IsNew(NewReason.NoMatch, null));
        b.Group(new NewFolder(@"2026\2026-09\2026-09-27 Zachar Bay"), Zachar, video);
        var plan = b.Build();
        var units = AuditCategorizer.Categorize(plan.Base.Scan.Inventory, plan, null, With(plan.Base.Scan.Ledger, Removed(V, 100)), NoChange);
        Assert.Equal(AuditCategory.Unaccounted, units.Units.Single(u => u.Unit == video).Worst);
    }

    [Theory]
    [InlineData("dateOnly", "removed from Picture Offload by date")]
    [InlineData("lightroom", "removed from Picture Offload after Lightroom import")]
    [InlineData("panoramaStitch", "removed from Picture Offload after Lightroom import")]
    [InlineData("hyperlapseResult", "removed from Picture Offload; its hyperlapse video is in the library")]
    [InlineData("unverifiedConfirmed", "removed from Picture Offload (not confirmed in Lightroom)")]
    public void Rules_APhotoWithAPhotoDelete_IsHistoryOnlyEvidence(string evidence, string reason)
    {
        var e = new CardEntry("DCIM/DJI_001/" + P, 200, T0, T0, T0, 0x20, EntryClass.Photo, null);
        var facts = new FileFacts(e, ItemKind.Photo, IsCompanion: false, ChangedSinceScan: false, UnderEnumerationError: false,
                                  UnitProbeError: false, UnitTruncated: false, AuditCategory.InLedger, new IsNew(NewReason.NoMatch, null),
                                  null, Listed: false, null, "America/Anchorage", Removed(P, 200, evidence));
        var v = CleanupRules.Classify(facts)!;
        Assert.Equal((CleanupEligibility.Evidence, (EvidenceSource?)EvidenceSource.HistoryOnly, reason), (v.Eligibility, v.Source, v.Reason));
    }

    [Fact]
    public void Rules_AVideoNeverUsesAPhotoDelete()
    {
        var e = new CardEntry("DCIM/DJI_001/" + V, 100, T0, T0, T0, 0x20, EntryClass.Video, null);
        var facts = new FileFacts(e, ItemKind.Video, false, false, false, false, false, AuditCategory.InLedger,
                                  new IsNew(NewReason.NoMatch, null), null, false, null, "America/Anchorage", Removed(V, 100));
        Assert.Equal(CleanupEligibility.NotInLibrary, CleanupRules.Classify(facts)!.Eligibility);
    }

    [Fact]
    public void ExecutorRecheck_APhotoDeleteKeepsHistoryOnlyPhotoEvidence()
    {
        var fresh = FreshEvidence.From(new LibraryListings(
            new RootListing(FakeLayout.VideoRoot, DestRoot.Video, false, true, new ListingResult([], [])),
            new RootListing(FakeLayout.PhotoRoot, DestRoot.Photo, false, true, new ListingResult([], [])), []));
        var proof = new FileProof("DCIM/DJI_001/" + P, FileKey.Of(P, 200), AuditCategory.InLedger, null, false, null);
        var empty = LedgerSnapshots.Empty(LedgerSnapshots.Detached(@"C:\x\.uas-sort", []));
        Assert.True(CleanupExecutor.ProofHolds(ItemKind.Photo, proof, fresh, With(empty, Removed(P, 200))));
        Assert.False(CleanupExecutor.ProofHolds(ItemKind.Video, proof, fresh, With(empty, Removed(P, 200))));
        Assert.False(CleanupExecutor.ProofHolds(ItemKind.Photo, proof, fresh, empty));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PhotoDeleteNewnessTests" --filter-class "*PhotoDeleteEvidenceTests"`
Expected: build fails (`CS1729: 'FileFacts' does not contain a constructor that takes 14 arguments`, `CS0117: 'CleanupExecutor' does not contain a definition for 'ProofHolds'`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoDeleteTexts.cs
using UasSort.Core.Planning;

namespace UasSort.Core.Cleanup;

/// <summary>What a later card shows for a photo moved out of Picture Offload (spec 2026-10-04 §6).</summary>
public static class PhotoDeleteTexts
{
    public static string Newness(LedgerPhotoDelete d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return $"removed from Picture Offload on {PlanText.ShortDate(DateOnly.FromDateTime(d.AtUtc))}";
    }

    public static string Evidence(LedgerPhotoDelete d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d.Evidence switch
        {
            PhotoDeleteRecords.EvidenceLightroom or PhotoDeleteRecords.EvidencePanoramaStitch => "removed from Picture Offload after Lightroom import",
            PhotoDeleteRecords.EvidenceHyperlapseResult => "removed from Picture Offload; its hyperlapse video is in the library",
            PhotoDeleteRecords.EvidenceDateOnly => "removed from Picture Offload by date",
            _ => "removed from Picture Offload (not confirmed in Lightroom)",
        };
    }
}
```

In `src/UasSort.Core/Planning/NewnessRules.Photo.cs`, directly after the rule-1 block that ends with `return new Imported(by, null, $"copied on …");` and its closing `}`, insert:

```csharp
        // 1 (Picture Offload cleanup, spec 2026-10-04 §6): an unrevoked photoDelete counts like a file record, member by member
        var removed = keys.Select(k => ledger.PhotoDeletes.TryGetValue(k, out var pd) && InSet(pd.Set) ? pd : null).ToList();
        if (removed.Any(r => r is not null) && recs.Zip(removed).All(p => p.First is not null || p.Second is not null))
            return new Imported(Evidence.LedgerVerified, null, PhotoDeleteTexts.Newness(removed.Where(r => r is not null).MaxBy(r => r!.AtUtc)!));
```

In `src/UasSort.Core/Offload/AuditCategorizer.cs`, inside `Context.FromEvidence`, after the line `if (Decision(e, set) is { } d) return Line(e, AuditCategory.ConfirmedByYou, DecisionText(d));` insert:

```csharp
            if (role != Role.Video && PhotoDelete(e, set) is { } removed)                               // Picture Offload cleanup (photoDelete)
                return Line(e, AuditCategory.InLedger, PhotoDeleteTexts.Evidence(removed));
```

and add to `Context`, after `Decision(...)`:

```csharp
        private LedgerPhotoDelete? PhotoDelete(CardEntry e, SetUnit? set)
            => _ledger.PhotoDeletes.TryGetValue(OffloadPaths.Key(e.RelPath, e.Size), out var d) && Fits(set, d.Set) ? d : null;
```

In `src/UasSort.Core/Cleanup/CleanupRules.cs`, replace the `FileFacts` record with:

```csharp
public sealed record FileFacts(CardEntry Entry, ItemKind UnitKind, bool IsCompanion, bool ChangedSinceScan,
    bool UnderEnumerationError, bool UnitProbeError, bool UnitTruncated, AuditCategory Category, Newness? Newness,
    LedgerDecision? Decision, bool Listed, LedgerFile? LedgerRecord, string TzId,
    LedgerPhotoDelete? PhotoDelete = null /* Picture Offload cleanup: this photo was moved out of the photo root (spec 2026-10-04 §6) */);
```

and in `Classify`, directly after the row-15 line (`return Proven(EvidenceSource.HistoryOnly, $"copied and verified on {day}; Lightroom may have moved it");`) insert:

```csharp
            if (f.PhotoDelete is { } removed)                                                             // photoDelete (spec 2026-10-04 §6)
                return Proven(EvidenceSource.HistoryOnly, PhotoDeleteTexts.Evidence(removed));
```

(Videos returned at row 13 above, so this line only ever sees photos and set members.)

In `src/UasSort.Core/Cleanup/CleanupPlanner.Candidates.cs`, in the local function `Facts`, replace its body's last statement with:

```csharp
            inputs.FreshLedger.PhotoDeletes.TryGetValue(key, out var removed);
            if (kind == ItemKind.Video
                || (unit is SetUnit su && !string.Equals(removed?.Set, su.SetName, StringComparison.OrdinalIgnoreCase))) removed = null;
            return new FileFacts(e, kind, companion, ctx.Changed(rel), ctx.UnderEnumerationError(rel), probeError, truncated,
                                 category, item.Newness, decision, ctx.Fresh.ListedFolder(key) is not null, record, tz, removed);
```

In `src/UasSort.Core/Cleanup/CleanupExecutor.cs`, replace `Run.EvidenceGone` with:

```csharp
        /// <summary>Step 2 (Ref §10.6 Evidence re-check): listings and the ledger only; no library file is opened.</summary>
        private (string Path, string Why)? EvidenceGone(CleanupCandidate c)
        {
            foreach (var p in c.Proofs)
                if (!ProofHolds(c.Kind, p, _fresh!, _ledger!))
                    return (p.CardRelPath, c.Kind == ItemKind.Video ? "no longer in the library listing"
                                                                    : "no longer in the library listing or the history");
            return null;
        }
```

and add to the static class `CleanupExecutor` (outside `Run`, after `EvidenceText`):

```csharp
    /// <summary>One proof of the evidence re-check: a video needs its library listing; a photo listed at plan time needs a listing or a
    /// verified ledger record; a HistoryOnly photo needs the record. An unrevoked photoDelete counts as a verified record for photos
    /// (Picture Offload cleanup, spec 2026-10-04 §6).</summary>
    internal static bool ProofHolds(ItemKind kind, FileProof p, FreshEvidence fresh, LedgerSnapshot ledger)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(fresh);
        ArgumentNullException.ThrowIfNull(ledger);
        var listed = fresh.ListedFolder(p.Key) is not null;
        var verified = (ledger.Files.TryGetValue(p.Key, out var lf) && lf.Verify is VerifyKind.Unbuffered or VerifyKind.Cached)
                       || (kind != ItemKind.Video && ledger.PhotoDeletes.ContainsKey(p.Key));
        return kind == ItemKind.Video ? listed : p.ListedFolder is not null ? listed || verified : verified;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PhotoDeleteNewnessTests" --filter-class "*PhotoDeleteEvidenceTests" --filter-class "*PhotoNewnessTests" --filter-class "*AuditCategorizerTests" --filter-class "*CleanupRulesTests" --filter-class "*CleanupExecutorTests" --filter-class "*CleanupCandidatesTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/Photos/PhotoDeleteTexts.cs src/UasSort.Core/Planning/NewnessRules.Photo.cs src/UasSort.Core/Offload/AuditCategorizer.cs src/UasSort.Core/Cleanup/CleanupRules.cs src/UasSort.Core/Cleanup/CleanupPlanner.Candidates.cs src/UasSort.Core/Cleanup/CleanupExecutor.cs tests/UasSort.Core.Tests/Planning/PhotoDeleteNewnessTests.cs tests/UasSort.Core.Tests/Cleanup/PhotoDeleteEvidenceTests.cs
git commit -F - <<'EOF'
feat: count photoDelete records as imported for newness and card cleanup

<session trailer>
EOF
```

---

### Task P.5: Picture Offload plan model, confirmation and fixtures

**Files:**
- Create: `src/UasSort.Core/Cleanup/Photos/PhotoCleanupRules.cs`, `PhotoCleanupModel.cs`, `PhotoCleanupPaths.cs`, `PhotoCleanupFingerprint.cs`, `PhotoCleanupPlan.cs`, `ConfirmedPhotoCleanupPlan.cs` (all under `src/UasSort.Core/Cleanup/Photos/`)
- Create: `tests/UasSort.Testing/PhotoCleanupFixtures.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupConfirmTests.cs`, `tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupRulesTests.cs`

**Interfaces:**
- Consumes: `ExifStamp` (P.2), `PhotoCleanupMode`, `PhotoEvidence` (P.3), `LedgerFile`, `LedgerSnapshot`, `PathRules`, `IoGuardPolicy.FileAttribute*` (existing).
- Produces (namespace `UasSort.Core.Cleanup`):
  - `public static partial class PhotoCleanupRules { const uint PlaceholderBits; const string DateUnknownCloudOnly = "date unknown (cloud-only)"; const string CloudOnlyCantCheck = "not on this PC (cloud-only) — couldn't check"; const string DateModeText = "not verified (date mode)"; static readonly ImmutableHashSet<string> PhotoExtensions; static bool IsCloudOnly(uint); static bool IsPhotoName(string); static bool IsJpg(string); static bool IsDng(string); static bool LooksLikeSetFolder(string); static string SetNameOf(string folderName); const double PanoramaAspect = 2.0; static (int Width, int Height)? PixelsOf(StillInfo? info); static bool LooksLikePanorama((int Width, int Height)? pixels); }`
  - `public enum PhotoItemKind { Photo, Set }`, `public enum PhotoSetKind { Unknown, Panorama, Hyperlapse }`, `public enum CaptureSource { None, Ledger, Exif }`, `public enum PhotoEligibility { Eligible, AfterCutoff, StraddlesCutoff, DateUnknown }`
  - `public sealed record PhotoMember(string RelPath, long Size, DateTime MtimeUtc, uint Attributes, DateTime? CaptureUtc, DateOnly? LocalDate, CaptureSource Source, ExifStamp? Stamp, string? DateProblem, LedgerFile? Ledger) { string Name; FileKey Key; bool IsCloudOnly; (int Width, int Height)? Pixels { get; init; } }`
  - `public sealed record PhotoItem(string RelPath, PhotoItemKind Kind, PhotoSetKind SetKind, string? SetName, ImmutableArray<PhotoMember> Members) { PhotoMember Primary; long Bytes; bool DateKnown; DateOnly? FirstDate; DateOnly? LastDate; }`
  - `public sealed record PhotoVerification(bool Verified, PhotoEvidence Evidence, string Text) { static readonly PhotoVerification DateMode; }`
  - `public sealed record PhotoRow(PhotoItem Item, PhotoEligibility Eligibility, PhotoVerification Verification, string? Why) { string Key; }`
  - `public sealed record PhotoSurvey(string PhotoRoot, ImmutableArray<PhotoItem> Items, ImmutableArray<string> NotTouched, LedgerSnapshot Ledger)`
  - `public sealed record PhotoCleanupRequest(PhotoCleanupMode Mode, DateOnly Cutoff, string? LightroomFolder)`
  - `public sealed record PhotoCleanupAck(string PlanFingerprint, ImmutableHashSet<string> Delete, bool MoveToRecycleBin, bool UnverifiedIncluded)`
  - `public sealed record PhotoTotals(int Photos, int Sets, int Files, long Bytes, int Unverified)`; `public sealed record PhotoScanProgress(string Phase, int Done, int Total)`
  - `public static class PhotoCleanupPaths { static string Full(string photoRoot, string relPath); static ImmutableArray<string> Targets(string photoRoot, PhotoItem item); }`
  - `public static class PhotoCleanupFingerprint { static string Compute(string photoRoot, PhotoCleanupRequest request, IEnumerable<PhotoRow> rows); }`
  - `public sealed class PhotoCleanupPlan { internal ctor(string planId, string photoRoot, PhotoCleanupRequest request, ImmutableArray<PhotoRow> rows, ImmutableArray<PhotoRow> notEligible, ImmutableArray<string> notTouched); string PlanId; string PhotoRoot; PhotoCleanupRequest Request; ImmutableArray<PhotoRow> Rows; ImmutableArray<PhotoRow> NotEligible; ImmutableArray<string> NotTouched; string Fingerprint; ImmutableHashSet<string> DefaultDelete(); PhotoTotals Totals(IReadOnlySet<string> delete); ConfirmedPhotoCleanupPlan Confirm(PhotoCleanupAck ack, TimeProvider clock); }`
  - `public sealed class ConfirmedPhotoCleanupPlan { PhotoCleanupPlan Plan; string PhotoRoot; ImmutableArray<PhotoRow> Items; ImmutableArray<PhotoRow> Kept; PhotoCleanupAck Ack; Guid Token; DateTime ConfirmedUtc; IReadOnlySet<string> Paths; PhotoEvidence EvidenceOf(PhotoRow row); }`
  - `UasSort.Testing.PhotoCleanupFixtures { const string PhotoRoot; static readonly DateTime Mtime; Member(...); Photo(...); Set(...); Row(...); Plan(...); Confirmed(...) }` (signatures in the code below).

- [ ] **Step 1: Write the fixtures and the failing tests**

```csharp
// tests/UasSort.Testing/PhotoCleanupFixtures.cs
namespace UasSort.Testing;

/// <summary>Picture Offload cleanup items, rows and plans for Core, Review and Platform tests, built through Core's internal constructors
/// (InternalsVisibleTo UasSort.Testing). Members default to a ledger-dated local file.</summary>
public static class PhotoCleanupFixtures
{
    public const string PhotoRoot = FakeLayout.PhotoRoot;
    public static readonly DateTime Mtime = new(2026, 6, 2, 3, 0, 0, DateTimeKind.Utc);

    public static PhotoMember Member(string relPath, DateOnly date, long size = 25_000_000, uint attributes = 0x20, ExifStamp? stamp = null,
                                     DateTime? mtimeUtc = null, DateTime? captureUtc = null, LedgerFile? ledger = null)
        => new(relPath, size, mtimeUtc ?? Mtime, attributes, captureUtc ?? date.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc), date,
               CaptureSource.Ledger, stamp, null, ledger);

    public static PhotoItem Photo(string name, DateOnly date, string? twin = null, long size = 25_000_000, ExifStamp? stamp = null)
        => new(name, PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
               twin is null ? [Member(name, date, size, stamp: stamp)] : [Member(name, date, size, stamp: stamp), Member(twin, date, 8_000_000)]);

    public static PhotoItem Set(string folder, DateOnly date, PhotoSetKind kind, params string[] members)
        => new(folder, PhotoItemKind.Set, kind, PhotoCleanupRules.SetNameOf(folder),
               [.. members.Select((m, i) => Member(folder + "\\" + m, date, 13_000_000 + i))]);

    public static PhotoRow Row(PhotoItem item, bool verified = true, PhotoEvidence evidence = PhotoEvidence.Lightroom)
        => new(item, PhotoEligibility.Eligible,
               verified ? new PhotoVerification(true, evidence, "in Lightroom") : new PhotoVerification(false, PhotoEvidence.Lightroom, "not found in Lightroom"),
               null);

    public static PhotoCleanupPlan Plan(PhotoCleanupMode mode, DateOnly cutoff, IEnumerable<PhotoRow> rows, string photoRoot = PhotoRoot,
                                        IEnumerable<PhotoRow>? notEligible = null, IEnumerable<string>? notTouched = null)
        => new("fixture-photos", photoRoot, new PhotoCleanupRequest(mode, cutoff, mode == PhotoCleanupMode.Verify ? @"X:\Lightroom" : null),
               [.. rows], [.. notEligible ?? []], [.. notTouched ?? []]);

    /// <summary>Every row set to Delete and confirmed through PhotoCleanupPlan.Confirm (both acknowledgements as needed).</summary>
    public static ConfirmedPhotoCleanupPlan Confirmed(TimeProvider clock, PhotoCleanupMode mode, IEnumerable<PhotoRow> rows, string photoRoot = PhotoRoot)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var plan = Plan(mode, new DateOnly(2026, 6, 30), rows, photoRoot);
        var all = plan.Rows.Select(r => r.Key).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var unverified = mode == PhotoCleanupMode.Verify && plan.Rows.Any(r => !r.Verification.Verified);
        return plan.Confirm(new PhotoCleanupAck(plan.Fingerprint, all, true, unverified), clock);
    }
}
```

```csharp
// tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupRulesTests.cs
namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class PhotoCleanupRulesTests
{
    [Theory]
    [InlineData("001_0087", true, "001_0087")]
    [InlineData("001_0087 2026-09-27", true, "001_0087")]
    [InlineData("001_0087 2026-09-27 (2)", true, "001_0087")]
    [InlineData("001_0087 (3)", true, "001_0087")]
    [InlineData("Exports", false, "Exports")]
    [InlineData("2026-09-27 Kodiak", false, "2026-09-27 Kodiak")]
    public void SetFolderNames_FollowRefSection88(string folder, bool looksLikeSet, string setName)
    {
        Assert.Equal(looksLikeSet, PhotoCleanupRules.LooksLikeSetFolder(folder));
        Assert.Equal(setName, PhotoCleanupRules.SetNameOf(folder));
    }

    [Theory]
    [InlineData("A.DNG", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("b.HEIC", true)]
    [InlineData("c.tif", true)]
    [InlineData("v.MP4", false)]
    [InlineData("desktop.ini", false)]
    public void PhotoNames(string name, bool photo) => Assert.Equal(photo, PhotoCleanupRules.IsPhotoName(name));

    [Fact]
    public void CloudOnly_IsAnyPlaceholderBit()
    {
        Assert.True(PhotoCleanupRules.IsCloudOnly(FakeFileSystem.CloudOnlyPlaceholder));
        Assert.True(PhotoCleanupRules.IsCloudOnly(IoGuardPolicy.FileAttributeOffline));
        Assert.False(PhotoCleanupRules.IsCloudOnly(0x20 | IoGuardPolicy.FileAttributePinned));
    }

    [Theory]
    [InlineData(8192, 4096, true)]      // DJI sphere: 2:1
    [InlineData(12000, 3000, true)]     // 180°
    [InlineData(3000, 9000, true)]      // vertical
    [InlineData(4032, 3024, false)]     // an ordinary 4:3 still
    [InlineData(5280, 2970, false)]     // an ordinary 16:9 still
    [InlineData(0, 100, false)]
    public void LooksLikePanorama_NeedsTheLongerSideAtLeastTwiceTheShorter(int w, int h, bool pano)
        => Assert.Equal(pano, PhotoCleanupRules.LooksLikePanorama((w, h)));

    [Fact]
    public void LooksLikePanorama_UnknownSize_IsNo() => Assert.False(PhotoCleanupRules.LooksLikePanorama(null));
}
```

```csharp
// tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupConfirmTests.cs
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class PhotoCleanupConfirmTests
{
    private static readonly DateOnly D = new(2026, 6, 1);
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 4, 20, 0, 0, TimeSpan.Zero));
    private static readonly PhotoRow Pair = Row(Photo("DJI_20260601121000_0002_D.DNG", D, twin: "DJI_20260601121000_0002_D.JPG"));
    private static readonly PhotoRow Pano = Row(Set("001_0087", D, PhotoSetKind.Panorama, "PANO_0001.DNG", "PANO_0002.DNG"), evidence: PhotoEvidence.PanoramaStitch);
    private static readonly PhotoRow Lonely = Row(Photo("DJI_20260601121500_0003_D.DNG", D), verified: false);

    private static PhotoCleanupAck Ack(PhotoCleanupPlan p, bool unverified, params string[] keys)
        => new(p.Fingerprint, [.. keys], MoveToRecycleBin: true, unverified);

    [Fact]
    public void Confirm_NamesTheFilesOfPhotoRowsAndTheFolderOfSetRows_AndKeepsTheRest()
    {
        var plan = Plan(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), [Pair, Pano, Lonely]);
        var c = plan.Confirm(Ack(plan, false, Pair.Key, Pano.Key), Clock);
        Assert.Equal(
            new[] { PhotoRoot + @"\001_0087", PhotoRoot + @"\DJI_20260601121000_0002_D.DNG", PhotoRoot + @"\DJI_20260601121000_0002_D.JPG" },
            c.Paths.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal([Pair.Key, Pano.Key], c.Items.Select(r => r.Key));
        Assert.Equal([Lonely.Key], c.Kept.Select(r => r.Key));
        Assert.Equal(Clock.GetUtcNow().UtcDateTime, c.ConfirmedUtc);
        Assert.Equal(PhotoEvidence.PanoramaStitch, c.EvidenceOf(Pano));
    }

    [Fact]
    public void DefaultDelete_DateModeEveryRow_VerifyModeOnlyVerifiedRows()
    {
        Assert.Equal(3, Plan(PhotoCleanupMode.BeforeDate, new DateOnly(2026, 6, 30), [Pair, Pano, Lonely]).DefaultDelete().Count);
        Assert.Equal([Pano.Key, Pair.Key], Plan(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), [Pair, Pano, Lonely]).DefaultDelete()
                                                .Order(StringComparer.OrdinalIgnoreCase));      // "001_0087" sorts before "DJI_…"
    }

    [Fact]
    public void Totals_CountPhotosSetsFilesBytesAndUnverified()
    {
        var plan = Plan(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), [Pair, Pano, Lonely]);
        var t = plan.Totals(new HashSet<string>([Pair.Key, Pano.Key, Lonely.Key]));
        Assert.Equal(new PhotoTotals(2, 1, 5, Pair.Item.Bytes + Pano.Item.Bytes + Lonely.Item.Bytes, 1), t);
    }

    [Fact]
    public void EvidenceOf_DateModeIsDateOnly_UnverifiedButConfirmedIsUnverifiedConfirmed()
    {
        var dated = Plan(PhotoCleanupMode.BeforeDate, new DateOnly(2026, 6, 30), [Pair]);
        Assert.Equal(PhotoEvidence.DateOnly, dated.Confirm(Ack(dated, false, Pair.Key), Clock).EvidenceOf(Pair));
        var verify = Plan(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), [Lonely]);
        Assert.Equal(PhotoEvidence.UnverifiedConfirmed, verify.Confirm(Ack(verify, true, Lonely.Key), Clock).EvidenceOf(Lonely));
    }

    [Fact]
    public void Confirm_RefusesEveryInconsistentAcknowledgement()
    {
        var plan = Plan(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), [Pair, Lonely]);
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(new PhotoCleanupAck("0000000000000000", [Pair.Key], true, false), Clock));
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(new PhotoCleanupAck(plan.Fingerprint, [Pair.Key], false, false), Clock));
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan, false), Clock));
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan, false, "Nope.DNG"), Clock));
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan, false, Lonely.Key), Clock));    // unverified without the second ack
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan, true, Pair.Key), Clock));       // second ack without an unverified row
    }

    [Fact]
    public void Targets_RefuseAnythingButPlainNamesDirectlyInTheRoot()
    {
        var escape = Photo(@"..\x.DNG", D);
        Assert.Throws<InvalidOperationException>(() => PhotoCleanupPaths.Targets(PhotoRoot, escape));
        var deep = new PhotoItem("001_0087", PhotoItemKind.Set, PhotoSetKind.Unknown, "001_0087", [Member(@"001_0087\sub\PANO_0001.DNG", D)]);
        Assert.Throws<InvalidOperationException>(() => PhotoCleanupPaths.Targets(PhotoRoot, deep));
        var foreign = new PhotoItem("001_0087", PhotoItemKind.Set, PhotoSetKind.Unknown, "001_0087", [Member(@"002_0001\PANO_0001.DNG", D)]);
        Assert.Throws<InvalidOperationException>(() => PhotoCleanupPaths.Targets(PhotoRoot, foreign));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PhotoCleanupConfirmTests" --filter-class "*PhotoCleanupRulesTests"`
Expected: build fails (`CS0246: … 'PhotoMember' could not be found`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoCleanupRules.cs
using System.Text.RegularExpressions;

namespace UasSort.Core.Cleanup;

/// <summary>Fixed rules and texts of Picture Offload cleanup (spec 2026-10-04 §3, §4).</summary>
public static partial class PhotoCleanupRules
{
    public const uint PlaceholderBits =
        IoGuardPolicy.FileAttributeOffline | IoGuardPolicy.FileAttributeRecallOnOpen | IoGuardPolicy.FileAttributeRecallOnDataAccess;

    public const string DateUnknownCloudOnly = "date unknown (cloud-only)";
    public const string CloudOnlyCantCheck = "not on this PC (cloud-only) — couldn't check";
    public const string DateModeText = "not verified (date mode)";

    /// <summary>The still extensions of Ref §5's media extensions (videos are never in Picture Offload's scope).</summary>
    public static readonly ImmutableHashSet<string> PhotoExtensions =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, ".dng", ".jpg", ".jpeg", ".heic", ".heif", ".tif", ".tiff");

    public static bool IsCloudOnly(uint attributes) => (attributes & PlaceholderBits) != 0;

    public static bool IsPhotoName(string name) => PhotoExtensions.Contains(Path.GetExtension(name));

    public static bool IsJpg(string name)
        => Path.GetExtension(name) is var e && (e.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || e.Equals(".jpeg", StringComparison.OrdinalIgnoreCase));

    public static bool IsDng(string name) => Path.GetExtension(name).Equals(".dng", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(?<set>\d{3}_\d{4})(?: \d{4}-\d{2}-\d{2})?(?: \(\d+\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex SetFolderPattern();

    /// <summary>A set folder the offload creates (Ref §8.8): the card set name, then an optional " yyyy-MM-dd" and " (n)".</summary>
    public static bool LooksLikeSetFolder(string folderName) => SetFolderPattern().IsMatch(folderName);

    public static string SetNameOf(string folderName)
        => SetFolderPattern().Match(folderName) is { Success: true } m ? m.Groups["set"].Value : folderName;

    /// <summary>A stitched panorama's shape (resolved ambiguity 6): the longer side at least this many times the shorter one.</summary>
    public const double PanoramaAspect = 2.0;

    public static (int Width, int Height)? PixelsOf(StillInfo? info)
        => info is { PixelWidth: { } w, PixelHeight: { } h } && w > 0 && h > 0 ? (w, h) : null;

    public static bool LooksLikePanorama((int Width, int Height)? pixels)
        => pixels is { } p && p.Width > 0 && p.Height > 0 && Math.Max(p.Width, p.Height) >= PanoramaAspect * Math.Min(p.Width, p.Height);
}
```

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoCleanupModel.cs
namespace UasSort.Core.Cleanup;

public enum PhotoItemKind { Photo, Set }

public enum PhotoSetKind { Unknown, Panorama, Hyperlapse }

public enum CaptureSource { None, Ledger, Exif }

public enum PhotoEligibility { Eligible, AfterCutoff, StraddlesCutoff, DateUnknown }

/// <summary>One file of a Picture Offload item. RelPath is relative to the photo root with '\': "X.DNG", or "001_0087\PANO_0001.DNG".
/// LocalDate is null when the date is unknown (DateProblem says why); Stamp is null until EXIF was read.</summary>
public sealed record PhotoMember(string RelPath, long Size, DateTime MtimeUtc, uint Attributes, DateTime? CaptureUtc, DateOnly? LocalDate,
                                 CaptureSource Source, ExifStamp? Stamp, string? DateProblem, LedgerFile? Ledger)
{
    public string Name => PathRules.FileName(RelPath);
    public FileKey Key => FileKey.Of(Name, Size);
    public bool IsCloudOnly => PhotoCleanupRules.IsCloudOnly(Attributes);

    /// <summary>The image size from EXIF, when it was read (the stitched-panorama shape test); null otherwise.</summary>
    public (int Width, int Height)? Pixels { get; init; }
}

/// <summary>A direct child of the photo root: a photo unit (the primary file, then its JPG twin) or a set folder (members by name).</summary>
public sealed record PhotoItem(string RelPath, PhotoItemKind Kind, PhotoSetKind SetKind, string? SetName, ImmutableArray<PhotoMember> Members)
{
    public PhotoMember Primary => Members[0];
    public long Bytes => Members.Sum(m => m.Size);
    public bool DateKnown => Members.All(m => m.LocalDate is not null);
    public DateOnly? FirstDate => DateKnown ? Members.Min(m => m.LocalDate!.Value) : null;
    public DateOnly? LastDate => DateKnown ? Members.Max(m => m.LocalDate!.Value) : null;
}

public sealed record PhotoVerification(bool Verified, PhotoEvidence Evidence, string Text)
{
    public static readonly PhotoVerification DateMode = new(false, PhotoEvidence.DateOnly, PhotoCleanupRules.DateModeText);
}

public sealed record PhotoRow(PhotoItem Item, PhotoEligibility Eligibility, PhotoVerification Verification, string? Why /* not eligible: why */)
{
    public string Key => Item.RelPath;
}

public sealed record PhotoSurvey(string PhotoRoot, ImmutableArray<PhotoItem> Items, ImmutableArray<string> NotTouched, LedgerSnapshot Ledger);

public sealed record PhotoCleanupRequest(PhotoCleanupMode Mode, DateOnly Cutoff /* "shot on or before", local date */, string? LightroomFolder);

public sealed record PhotoCleanupAck(string PlanFingerprint, ImmutableHashSet<string> Delete /* row keys */, bool MoveToRecycleBin,
                                     bool UnverifiedIncluded /* the second acknowledgement */);

public sealed record PhotoTotals(int Photos, int Sets, int Files, long Bytes, int Unverified);

public sealed record PhotoScanProgress(string Phase, int Done, int Total);
```

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoCleanupPaths.cs
namespace UasSort.Core.Cleanup;

/// <summary>What the recycler moves for a row (spec 2026-10-04 §5): a photo row's files, each directly in the photo root, or a set row's
/// folder, directly in the photo root with its members directly inside. Anything else throws (a VM or planner bug).</summary>
public static class PhotoCleanupPaths
{
    public static string Full(string photoRoot, string relPath) => PathRules.Join(photoRoot, relPath);

    public static ImmutableArray<string> Targets(string photoRoot, PhotoItem item)
    {
        ArgumentNullException.ThrowIfNull(photoRoot);
        ArgumentNullException.ThrowIfNull(item);
        Plain(item.RelPath);
        if (item.Members.IsDefaultOrEmpty) throw new InvalidOperationException($"{item.RelPath} has no files");
        if (item.Kind == PhotoItemKind.Photo)
        {
            foreach (var m in item.Members) Plain(m.RelPath);
            if (!string.Equals(item.Primary.RelPath, item.RelPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{item.RelPath}: the row is not named after its primary file");
            return [.. item.Members.Select(m => Full(photoRoot, m.RelPath))];
        }
        foreach (var m in item.Members)
        {
            var parts = m.RelPath.Split('\\');
            if (parts.Length != 2 || !string.Equals(parts[0], item.RelPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{m.RelPath} is not directly in the set folder {item.RelPath}");
            Plain(parts[1]);
        }
        return [Full(photoRoot, item.RelPath)];
    }

    private static void Plain(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.IndexOfAny(['\\', '/', ':']) >= 0
            || segment.EndsWith('.') || segment.EndsWith(' '))
            throw new InvalidOperationException($"Not a plain name in the photo folder: '{segment}'");
    }
}
```

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoCleanupFingerprint.cs
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace UasSort.Core.Cleanup;

/// <summary>XxHash64 over the request and every eligible row (key, kind, verification, members' names, sizes and mtimes).</summary>
public static class PhotoCleanupFingerprint
{
    public static string Compute(string photoRoot, PhotoCleanupRequest request, IEnumerable<PhotoRow> rows)
    {
        ArgumentNullException.ThrowIfNull(photoRoot);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rows);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(inv, $"{PathRules.Normalize(photoRoot).ToUpperInvariant()}|{request.Mode}|{request.Cutoff:yyyy-MM-dd}|{request.LightroomFolder ?? "-"}\n");
        foreach (var r in rows.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(inv, $"{r.Key.ToUpperInvariant()}|{r.Item.Kind}|{r.Eligibility}|{r.Verification.Verified}|{r.Verification.Evidence}\n");
            foreach (var m in r.Item.Members.OrderBy(m => m.RelPath, StringComparer.OrdinalIgnoreCase))
                sb.Append(inv, $"  {m.RelPath.ToUpperInvariant()}|{m.Size}|{m.MtimeUtc.Ticks}\n");
        }
        return XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(sb.ToString())).ToString("x16", inv);
    }
}
```

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoCleanupPlan.cs
namespace UasSort.Core.Cleanup;

/// <summary>A reviewed Picture Offload plan (spec 2026-10-04 §2). A class with an internal constructor: only PhotoCleanupPlanner.Build
/// (and the test fixtures) create one; Confirm is the only way to a ConfirmedPhotoCleanupPlan.</summary>
public sealed class PhotoCleanupPlan
{
    internal PhotoCleanupPlan(string planId, string photoRoot, PhotoCleanupRequest request, ImmutableArray<PhotoRow> rows,
                              ImmutableArray<PhotoRow> notEligible, ImmutableArray<string> notTouched)
    {
        PlanId = planId;
        PhotoRoot = photoRoot;
        Request = request;
        Rows = rows;
        NotEligible = notEligible;
        NotTouched = notTouched;
        Fingerprint = PhotoCleanupFingerprint.Compute(photoRoot, request, rows);
    }

    public string PlanId { get; }
    public string PhotoRoot { get; }
    public PhotoCleanupRequest Request { get; }
    public ImmutableArray<PhotoRow> Rows { get; }               // eligible, oldest first
    public ImmutableArray<PhotoRow> NotEligible { get; }        // date unknown, or a set straddling the cutoff
    public ImmutableArray<string> NotTouched { get; }           // other files and folders in the photo root
    public string Fingerprint { get; }

    /// <summary>Date mode: every row starts Delete. Verify mode: verified rows start Delete, unverified rows Keep.</summary>
    public ImmutableHashSet<string> DefaultDelete()
        => Rows.Where(r => Request.Mode == PhotoCleanupMode.BeforeDate || r.Verification.Verified)
               .Select(r => r.Key).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

    public PhotoTotals Totals(IReadOnlySet<string> delete)
    {
        ArgumentNullException.ThrowIfNull(delete);
        var chosen = Rows.Where(r => delete.Contains(r.Key)).ToList();
        return new PhotoTotals(chosen.Count(r => r.Item.Kind == PhotoItemKind.Photo), chosen.Count(r => r.Item.Kind == PhotoItemKind.Set),
                               chosen.Sum(r => r.Item.Members.Length), chosen.Sum(r => r.Item.Bytes),
                               Request.Mode == PhotoCleanupMode.Verify ? chosen.Count(r => !r.Verification.Verified) : 0);
    }

    /// <summary>The only factory of ConfirmedPhotoCleanupPlan (spec 2026-10-04 §5). Recomputes the fingerprint; throws on a VM bug.</summary>
    public ConfirmedPhotoCleanupPlan Confirm(PhotoCleanupAck ack, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(ack);
        ArgumentNullException.ThrowIfNull(clock);
        var recomputed = PhotoCleanupFingerprint.Compute(PhotoRoot, Request, Rows);
        if (!string.Equals(recomputed, Fingerprint, StringComparison.Ordinal) || !string.Equals(recomputed, ack.PlanFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The Picture Offload plan changed after it was shown; confirm it again.");
        if (!ack.MoveToRecycleBin) throw new InvalidOperationException("\"Move … to the Recycle Bin\" is not ticked.");
        if (ack.Delete.IsEmpty) throw new InvalidOperationException("Nothing is set to Delete.");
        var delete = new HashSet<string>(ack.Delete, StringComparer.OrdinalIgnoreCase);
        var keys = Rows.Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in delete)
            if (!keys.Contains(key)) throw new InvalidOperationException($"{key} is not an eligible row of this plan.");
        var chosen = Rows.Where(r => delete.Contains(r.Key)).ToImmutableArray();
        var unverified = Request.Mode == PhotoCleanupMode.Verify && chosen.Any(r => !r.Verification.Verified);
        if (unverified != ack.UnverifiedIncluded)
            throw new InvalidOperationException("The second acknowledgement does not match the rows set to Delete.");
        foreach (var r in chosen) _ = PhotoCleanupPaths.Targets(PhotoRoot, r.Item);
        return new ConfirmedPhotoCleanupPlan(this, chosen, [.. Rows.Where(r => !delete.Contains(r.Key))], ack, Guid.NewGuid(),
                                             clock.GetUtcNow().UtcDateTime);
    }
}
```

```csharp
// src/UasSort.Core/Cleanup/Photos/ConfirmedPhotoCleanupPlan.cs
namespace UasSort.Core.Cleanup;

/// <summary>Mirrors ConfirmedCleanupPlan: immutable, built only by PhotoCleanupPlan.Confirm. Paths is what the guard allows for
/// IoOp.PhotoRootRecycle: each file of a photo row, the folder of a set row (PathRules.Join, case-insensitive).</summary>
public sealed class ConfirmedPhotoCleanupPlan
{
    internal ConfirmedPhotoCleanupPlan(PhotoCleanupPlan plan, ImmutableArray<PhotoRow> items, ImmutableArray<PhotoRow> kept, PhotoCleanupAck ack,
                                       Guid token, DateTime confirmedUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan;
        Items = items;
        Kept = kept;
        Ack = ack;
        Token = token;
        ConfirmedUtc = confirmedUtc;
        Paths = items.SelectMany(r => PhotoCleanupPaths.Targets(plan.PhotoRoot, r.Item)).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public PhotoCleanupPlan Plan { get; }
    public string PhotoRoot => Plan.PhotoRoot;
    public ImmutableArray<PhotoRow> Items { get; }       // the rows set to Delete, oldest first
    public ImmutableArray<PhotoRow> Kept { get; }        // the rows left on Keep
    public PhotoCleanupAck Ack { get; }
    public Guid Token { get; }
    public DateTime ConfirmedUtc { get; }
    public IReadOnlySet<string> Paths { get; }

    /// <summary>The photoDelete evidence of a row (spec 2026-10-04 §6).</summary>
    public PhotoEvidence EvidenceOf(PhotoRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Plan.Request.Mode == PhotoCleanupMode.BeforeDate ? PhotoEvidence.DateOnly
             : row.Verification.Verified ? row.Verification.Evidence
             : PhotoEvidence.UnverifiedConfirmed;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PhotoCleanupConfirmTests" --filter-class "*PhotoCleanupRulesTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/Photos/PhotoCleanupRules.cs src/UasSort.Core/Cleanup/Photos/PhotoCleanupModel.cs src/UasSort.Core/Cleanup/Photos/PhotoCleanupPaths.cs src/UasSort.Core/Cleanup/Photos/PhotoCleanupFingerprint.cs src/UasSort.Core/Cleanup/Photos/PhotoCleanupPlan.cs src/UasSort.Core/Cleanup/Photos/ConfirmedPhotoCleanupPlan.cs tests/UasSort.Testing/PhotoCleanupFixtures.cs tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupConfirmTests.cs tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupRulesTests.cs
git commit -F - <<'EOF'
feat: add the Picture Offload cleanup plan, its confirmation and fixtures

<session trailer>
EOF
```

---

### Task P.6: IO guard for photo-root reads and recycles; ports and fakes

**Files:**
- Modify: `src/UasSort.Core/Guard/GuardTypes.cs` (`IoOp`, `GuardContext`)
- Modify: `src/UasSort.Core/Guard/IoGuardPolicy.cs` (`Check`, new `CheckPhotoCleanupRead`, `CheckPhotoRootRecycle`)
- Modify: `src/UasSort.Core/Ports/Ports.cs` (append the new ports)
- Create: `tests/UasSort.Testing/PhotoCleanupFakes.cs`
- Test: `tests/UasSort.Core.Tests/Guard/IoGuardPhotoCleanupTests.cs`, `tests/UasSort.Core.Tests/Testing/PhotoCleanupFakesTests.cs`

**Interfaces:**
- Consumes: `ConfirmedPhotoCleanupPlan.Paths`/`PhotoRoot` (P.5), `LightroomRules` (P.1), `FakeFileSystem.Guard/Metadata/RemoveUnguarded/PeekContent` (existing).
- Produces:
  - `IoOp.PhotoCleanupRead`, `IoOp.PhotoRootRecycle`.
  - `GuardContext.LightroomFolder { get; init; }` (string?), `GuardContext.PhotoCleanup { get; init; }` (ConfirmedPhotoCleanupPlan?).
  - `public interface IPhotoFileReader { Stream OpenRead(string fullPath); }`
  - `public sealed record PhotoItemStat(long Size, DateTime MtimeUtc, uint Attributes, bool IsDirectory);`
  - `public sealed record RecycleOk; public sealed record RecycleError(int Code, string Message, bool NotRecyclable); public union RecycleResult(RecycleOk, RecycleError);`
  - `public interface IPhotoRootRecycler : IDisposable { PhotoItemStat? Stat(string fullPath); RecycleResult Recycle(string fullPath); }`
  - `public interface IPhotoRootRecyclerFactory { IPhotoRootRecycler Open(ConfirmedPhotoCleanupPlan plan); }`
  - `UasSort.Testing.FakePhotoFileReader(FakeFileSystem fs, GuardContext ctx) : IPhotoFileReader { List<string> Opened; }`
  - `UasSort.Testing.FakePhotoRootRecyclerFactory(FakeFileSystem fs, GuardContext baseContext) : IPhotoRootRecyclerFactory { List<string> Recycled; HashSet<string> NotRecyclable; HashSet<string> Fails; Action<string>? BeforeRecycle; bool OpenThrows; int Opened; int Disposed; }`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/UasSort.Core.Tests/Guard/IoGuardPhotoCleanupTests.cs
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Guard;

public sealed class IoGuardPhotoCleanupTests
{
    private const string Root = FakeLayout.PhotoRoot;
    private const string Lr = @"X:\Lightroom";
    private const uint File = 0x20, Dir = 0x10, CloudOnly = FakeFileSystem.CloudOnlyPlaceholder;
    private const string Dng = Root + @"\DJI_20260601121000_0002_D.DNG";
    private const string Jpg = Root + @"\DJI_20260601121000_0002_D.JPG";
    private const string Pano = Root + @"\001_0087";
    private static readonly DateOnly D = new(2026, 6, 1);

    private static ConfirmedPhotoCleanupPlan Plan(string photoRoot = Root, string setFolder = "001_0087") => Confirmed(new FakeTimeProvider(), PhotoCleanupMode.Verify,
        [Row(Photo("DJI_20260601121000_0002_D.DNG", D, twin: "DJI_20260601121000_0002_D.JPG")),
         Row(Set(setFolder, D, PhotoSetKind.Panorama, "PANO_0001.DNG", "PANO_0002.DNG"))], photoRoot);

    private static GuardContext Ctx(ConfirmedPhotoCleanupPlan? plan = null, string? lightroom = Lr, IEnumerable<string>? previous = null)
        => FakeLayout.Context(cardRoot: null, previousPhotoRoots: previous) with { LightroomFolder = lightroom, PhotoCleanup = plan };

    private static string Kind(IoOp op, string path, uint? attributes, GuardContext ctx) => IoGuardPolicy.Check(op, path, attributes, ctx) switch
    {
        GuardAllow => "Allow",
        GuardUnsafe => "Unsafe",
        GuardCloudOnly => "CloudOnly",
        GuardHydration => "Hydration",
    };

    [Fact]
    public void Recycle_TheConfirmedFilesAndSetFolder_AreAllowed_EvenWhenCloudOnly()
    {
        var ctx = Ctx(Plan());
        Assert.Equal("Allow", Kind(IoOp.PhotoRootRecycle, Dng, File, ctx));
        Assert.Equal("Allow", Kind(IoOp.PhotoRootRecycle, Jpg.ToUpperInvariant(), File, ctx));
        Assert.Equal("Allow", Kind(IoOp.PhotoRootRecycle, Pano, Dir, ctx));
        Assert.Equal("Allow", Kind(IoOp.PhotoRootRecycle, Dng, CloudOnly, ctx));      // recycling never opens the data
    }

    [Theory]
    [InlineData(Root + @"\DJI_20260601999999_0009_D.DNG")]                 // not in the plan
    [InlineData(Root + @"\001_0087\PANO_0001.DNG")]                          // inside a set folder: only the folder moves
    [InlineData(Root)]                                                        // the root itself
    [InlineData(FakeLayout.VideoRoot + @"\2026\2026-06\2026-06-01 Juneau\DJI_20260601120000_0001_D.MP4")]
    [InlineData(FakeLayout.VideoRoot + @"\.uas-sort\ledger-DESKTOP-A.jsonl")]
    [InlineData(Lr + @"\2026\Damian_20260601_001.dng")]
    [InlineData(@"D:\LR_Catalog\Lightroom Catalog.lrcat")]
    [InlineData(FakeLayout.AppDataDir + @"\settings.json")]
    [InlineData(@"E:\DCIM\DJI_001\DJI_20260601121000_0002_D.DNG")]
    public void Recycle_AnythingElse_IsUnsafe(string path)
        => Assert.Equal("Unsafe", Kind(IoOp.PhotoRootRecycle, path, File, Ctx(Plan())));

    [Fact]
    public void Recycle_WithoutAPlan_OrAPlanForAnotherRoot_OrInAPreviousRoot_IsUnsafe()
    {
        Assert.Equal("Unsafe", Kind(IoOp.PhotoRootRecycle, Dng, File, Ctx()));
        Assert.Equal("Unsafe", Kind(IoOp.PhotoRootRecycle, @"X:\Old\DJI_20260601121000_0002_D.DNG", File, Ctx(Plan(@"X:\Old"))));
        Assert.Equal("Unsafe", Kind(IoOp.PhotoRootRecycle, Root + @"\001_0099", Dir,
                                    Ctx(Plan(setFolder: "001_0099"), previous: [Root + @"\001_0099"])));
        Assert.Equal("Unsafe", Kind(IoOp.PhotoRootRecycle, Dng, null, Ctx(Plan())));    // gone
    }

    [Fact]
    public void Read_PhotoRootFiles_SetMembers_AndLightroomFiles_AreAllowed_ButNeverHydrated()
    {
        var ctx = Ctx();
        Assert.Equal("Allow", Kind(IoOp.PhotoCleanupRead, Dng, File, ctx));
        Assert.Equal("Allow", Kind(IoOp.PhotoCleanupRead, Pano + @"\PANO_0001.DNG", File, ctx));
        Assert.Equal("Allow", Kind(IoOp.PhotoCleanupRead, Lr + @"\2026\2026-06-01\Damian_20260601_001.dng", File, ctx));
        Assert.Equal("Hydration", Kind(IoOp.PhotoCleanupRead, Dng, CloudOnly, ctx));
    }

    [Theory]
    [InlineData(FakeLayout.VideoRoot + @"\2026\x.MP4")]
    [InlineData(FakeLayout.VideoRoot + @"\.uas-sort\ledger-DESKTOP-A.jsonl")]
    [InlineData(Lr + @"\LR_Catalog\Lightroom Catalog.lrcat")]
    [InlineData(Lr + @"\Lightroom Catalog.lrcat")]
    [InlineData(Lr + @"\Lightroom Catalog Smart Previews.lrdata\a\b.dng")]
    [InlineData(Root + @"\001_0087\deeper\PANO_0001.DNG")]
    [InlineData(FakeLayout.AppDataDir + @"\settings.json")]
    [InlineData(@"Y:\Elsewhere\x.dng")]
    public void Read_AnythingElse_IsUnsafe(string path)
        => Assert.Equal("Unsafe", Kind(IoOp.PhotoCleanupRead, path, File, Ctx()));

    [Fact]
    public void Read_AFolder_OrALightroomFileWithoutTheSetting_IsUnsafe()
    {
        Assert.Equal("Unsafe", Kind(IoOp.PhotoCleanupRead, Pano, Dir, Ctx()));
        Assert.Equal("Unsafe", Kind(IoOp.PhotoCleanupRead, Lr + @"\2026\x.dng", File, Ctx(lightroom: null)));
    }

    [Theory]
    [InlineData(IoOp.ReadData)]
    [InlineData(IoOp.CreateNew)]
    [InlineData(IoOp.Delete)]
    [InlineData(IoOp.Rename)]
    [InlineData(IoOp.SetAttributesOrTimes)]
    public void EveryOtherOp_InTheLightroomFolderOrTheCatalog_IsUnsafe(IoOp op)
    {
        Assert.Equal("Unsafe", Kind(op, Lr + @"\2026\x.dng", File, Ctx()));
        Assert.Equal("Unsafe", Kind(op, @"D:\LR_Catalog\Lightroom Catalog.lrcat", File, Ctx()));
    }
}
```

```csharp
// tests/UasSort.Core.Tests/Testing/PhotoCleanupFakesTests.cs
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Testing;

public sealed class PhotoCleanupFakesTests
{
    private static readonly DateOnly D = new(2026, 6, 1);

    [Fact]
    public void FakeRecycler_MovesOnlyThroughTheGuard()
    {
        var fs = FakeLayout.NewFileSystem();
        var dng = PhotoRoot + @"\A.DNG";
        var other = PhotoRoot + @"\B.DNG";
        fs.AddFile(dng, 10, Mtime);
        fs.AddFile(other, 10, Mtime);
        var factory = new FakePhotoRootRecyclerFactory(fs, FakeLayout.Context(cardRoot: null));
        using var recycler = factory.Open(Confirmed(new FakeTimeProvider(), PhotoCleanupMode.BeforeDate, [Row(Photo("A.DNG", D, size: 10))]));
        Assert.Equal(new PhotoItemStat(10, Mtime, 0x20, false), recycler.Stat(dng));
        Assert.True(recycler.Recycle(dng) is RecycleOk);
        Assert.False(fs.Exists(dng));
        Assert.Throws<UnsafeIoException>(() => recycler.Recycle(other));
        Assert.True(fs.Exists(other));
        Assert.Equal([PathRules.Normalize(dng)], factory.Recycled);
        Assert.Null(recycler.Stat(dng));
    }

    [Fact]
    public void FakeReader_RefusesACloudOnlyFile_AsAHydrationViolation()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(PhotoRoot + @"\A.DNG", new SyntheticDngBuilder().Build(), Mtime, FakeFileSystem.CloudOnlyPlaceholder);
        var reader = new FakePhotoFileReader(fs, FakeLayout.Context(cardRoot: null));
        Assert.Throws<HydrationViolation>(() => reader.OpenRead(PhotoRoot + @"\A.DNG"));
        Assert.Single(fs.HydrationViolations);
        Assert.Empty(reader.Opened);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*IoGuardPhotoCleanupTests" --filter-class "*PhotoCleanupFakesTests"`
Expected: build fails (`CS0117: 'IoOp' does not contain a definition for 'PhotoRootRecycle'`).

- [ ] **Step 3: Implement**

In `src/UasSort.Core/Guard/GuardTypes.cs`, replace the `IoOp` enum with:

```csharp
public enum IoOp
{
    ReadData, AppendOwnLedger, CreateNew, CreateDir, SetPinned, SetAttributesOrTimes, Delete, Rename, OpenForFlush,
    CardDelete /* Card cleanup only (Ref §10.6): a card file, or an emptied set folder */,
    PhotoCleanupRead /* Picture Offload cleanup only: read EXIF of a photo-root file or a Lightroom library file (spec 2026-10-04 §3–§4) */,
    PhotoRootRecycle /* Picture Offload cleanup only: move a confirmed photo-root item to the Recycle Bin (spec 2026-10-04 §5) */,
}
```

and give `GuardContext` a body: replace the final `ConfirmedCleanupPlan? Cleanup /* … */);` of its declaration with:

```csharp
    ConfirmedCleanupPlan? Cleanup /* set only in the eraser's own context while CleanupExecutor runs; null everywhere else */)
{
    /// <summary>Settings.LightroomFolder (canonical) in Picture Offload cleanup's contexts: readable with IoOp.PhotoCleanupRead only.</summary>
    public string? LightroomFolder { get; init; }

    /// <summary>Set only in the photo-root recycler's own context (spec 2026-10-04 §5); null everywhere else.</summary>
    public ConfirmedPhotoCleanupPlan? PhotoCleanup { get; init; }
}
```

In `src/UasSort.Core/Guard/IoGuardPolicy.cs`:
1. Replace the rule-1 placeholder line `if (attributes is uint a && (a & PlaceholderBits) != 0 && op != IoOp.SetPinned)` with `if (attributes is uint a && (a & PlaceholderBits) != 0 && op is not (IoOp.SetPinned or IoOp.PhotoRootRecycle))` (moving a cloud-only file to the Recycle Bin never opens its data).
2. Directly after `if (op == IoOp.CardDelete) return CheckCardDelete(path, attributes!.Value, ledgerDir, ctx);` insert:

```csharp
        // Picture Offload cleanup (spec 2026-10-04 §5): its own read and recycle ops; the Lightroom folder and catalog are never written.
        if (op == IoOp.PhotoCleanupRead) return CheckPhotoCleanupRead(path, attributes!.Value, ledgerDir, ctx);
        if (op == IoOp.PhotoRootRecycle) return CheckPhotoRootRecycle(path, ledgerDir, ctx);
        if (ctx.LightroomFolder is { } lightroom && PathRules.IsSameOrUnder(path, lightroom))
            return Unsafe(op, path, "the Lightroom folder is only ever read, by Picture Offload cleanup");
        if (LightroomRules.IsCatalogPath(path)) return Unsafe(op, path, "the Lightroom catalog is never opened");
```

3. Add the two methods after `CheckCardDelete`:

```csharp
    private static GuardDecision CheckPhotoCleanupRead(string path, uint attributes, string ledgerDir, GuardContext ctx)
    {
        const IoOp op = IoOp.PhotoCleanupRead;
        if ((attributes & FileAttributeDirectory) != 0) return Unsafe(op, path, "only files are read");
        if (LightroomRules.IsCatalogPath(path)) return Unsafe(op, path, "the Lightroom catalog is never opened");
        if (PathRules.IsSameOrUnder(path, ledgerDir)) return Unsafe(op, path, "ledger files are read by the ledger store only");
        if (ctx.LightroomFolder is { } lightroom && PathRules.IsStrictlyUnder(path, lightroom)) return Allow();
        var parent = PathRules.Parent(path);
        if (parent is not null && (PathRules.Equal(parent, ctx.PhotoRoot)
                                   || (PathRules.Parent(parent) is { } grand && PathRules.Equal(grand, ctx.PhotoRoot))))
            return Allow();
        return Unsafe(op, path, "only Picture Offload photos, their set folders' members and Lightroom library files are read");
    }

    private static GuardDecision CheckPhotoRootRecycle(string path, string ledgerDir, GuardContext ctx)
    {
        const IoOp op = IoOp.PhotoRootRecycle;
        if (PathRules.IsSameOrUnder(path, ledgerDir)) return Unsafe(op, path, "a Picture Offload cleanup never touches the ledger folder");
        if (ctx.LightroomFolder is { } lightroom && PathRules.Overlaps(path, lightroom))
            return Unsafe(op, path, "a Picture Offload cleanup never touches the Lightroom folder");
        if (LightroomRules.IsCatalogPath(path)) return Unsafe(op, path, "a Picture Offload cleanup never touches the Lightroom catalog");
        foreach (var previous in ctx.PreviousPhotoRoots)
            if (PathRules.IsSameOrUnder(path, previous) && !PathRules.IsStrictlyUnder(ctx.PhotoRoot, previous))
                return Unsafe(op, path, "a Picture Offload cleanup never touches a previous photo root");
        if (PathRules.Overlaps(path, ctx.AppDataDir)) return Unsafe(op, path, "a Picture Offload cleanup never touches the app's data folder");
        if (ctx.CardRoot is { } card && PathRules.Overlaps(path, card)) return Unsafe(op, path, "a Picture Offload cleanup never touches the card");
        if (ctx.PhotoCleanup is not { } plan) return Unsafe(op, path, "no confirmed Picture Offload plan");
        if (!PathRules.Equal(plan.PhotoRoot, ctx.PhotoRoot)) return Unsafe(op, path, "the confirmed plan is for another photo folder");
        if (PathRules.Parent(path) is not { } parent || !PathRules.Equal(parent, ctx.PhotoRoot))
            return Unsafe(op, path, "only items directly in the photo folder are recycled");
        return PathRules.SetContains(plan.Paths, path) ? Allow() : Unsafe(op, path, "not an item the confirmed plan names");
    }
```

Append to `src/UasSort.Core/Ports/Ports.cs`:

```csharp
// ── Picture Offload cleanup (spec 2026-10-04 §5)
public interface IPhotoFileReader                                 // Platform: IoGuardPolicy.Check(PhotoCleanupRead, …) before every open;
{                                                                 // read-only, FileShare.ReadWrite | Delete; a placeholder is refused, never hydrated
    Stream OpenRead(string fullPath);
}

public sealed record PhotoItemStat(long Size, DateTime MtimeUtc, uint Attributes, bool IsDirectory);

public sealed record RecycleOk;
public sealed record RecycleError(int Code, string Message, bool NotRecyclable /* the shell would have deleted it permanently */);
public union RecycleResult(RecycleOk, RecycleError);

public interface IPhotoRootRecycler : IDisposable                 // IoGuardPolicy.Check(PhotoRootRecycle, …) before every move
{
    PhotoItemStat? Stat(string fullPath);                         // attributes only, never opens; null = gone
    RecycleResult Recycle(string fullPath);                       // one file or one set folder → the Recycle Bin; never a permanent delete
}

public interface IPhotoRootRecyclerFactory                        // Platform: re-derives the photo root from the saved settings and builds
{                                                                 // the recycler's own GuardContext with the plan; throws UnsafeIoException
    IPhotoRootRecycler Open(ConfirmedPhotoCleanupPlan plan);
}
```

```csharp
// tests/UasSort.Testing/PhotoCleanupFakes.cs
namespace UasSort.Testing;

/// <summary>IPhotoFileReader over the fake FS: every open asks the guard (IoOp.PhotoCleanupRead); a placeholder trips the hydration tripwire.</summary>
public sealed class FakePhotoFileReader(FakeFileSystem fs, GuardContext ctx) : IPhotoFileReader
{
    private readonly Lock _gate = new();
    private readonly List<string> _opened = [];

    public IReadOnlyList<string> Opened { get { lock (_gate) { return [.. _opened]; } } }

    public Stream OpenRead(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var p = fs.Guard(IoOp.PhotoCleanupRead, fullPath, ctx);
        lock (_gate) _opened.Add(p);
        return new MemoryStream(fs.PeekContent(p), writable: false);
    }
}

/// <summary>IPhotoRootRecyclerFactory over the fake FS: Open adds the plan to the base context; Recycle asks the guard
/// (IoOp.PhotoRootRecycle) and removes the item. Faults: NotRecyclable (the shell would delete permanently), Fails (access denied),
/// BeforeRecycle (runs first, e.g. to change a file or cancel), OpenThrows (the factory's own refusal).</summary>
public sealed class FakePhotoRootRecyclerFactory(FakeFileSystem fs, GuardContext baseContext) : IPhotoRootRecyclerFactory
{
    internal FakeFileSystem Fs { get; } = fs;
    public List<string> Recycled { get; } = [];
    public HashSet<string> NotRecyclable { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Fails { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Action<string>? BeforeRecycle { get; set; }
    public bool OpenThrows { get; set; }
    public int Opened { get; private set; }
    public int Disposed { get; private set; }

    public IPhotoRootRecycler Open(ConfirmedPhotoCleanupPlan plan)
    {
        if (OpenThrows) throw new UnsafeIoException("Picture Offload cleanup refused: fake factory refusal");
        Opened++;
        return new Recycler(this, baseContext with { PhotoCleanup = plan });
    }

    private sealed class Recycler(FakePhotoRootRecyclerFactory owner, GuardContext ctx) : IPhotoRootRecycler
    {
        public PhotoItemStat? Stat(string fullPath)
            => owner.Fs.Metadata(fullPath) is { } e ? new PhotoItemStat(e.Size, e.MtimeUtc, e.RawAttributes, e.IsDirectory) : null;

        public RecycleResult Recycle(string fullPath)
        {
            owner.BeforeRecycle?.Invoke(fullPath);
            var p = owner.Fs.Guard(IoOp.PhotoRootRecycle, fullPath, ctx);
            if (owner.NotRecyclable.Contains(p))
                return new RecycleError(0, "Windows would delete it permanently instead of moving it to the Recycle Bin; kept", true);
            if (owner.Fails.Contains(p)) return new RecycleError(5, "Access is denied.", false);
            owner.Fs.RemoveUnguarded(p);
            owner.Recycled.Add(p);
            return new RecycleOk();
        }

        public void Dispose() => owner.Disposed++;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*IoGuardPhotoCleanupTests" --filter-class "*PhotoCleanupFakesTests" --filter-class "*IoGuardPolicyTests" --filter-class "*IoGuardCardDeleteTests"`
Expected: PASS (the existing guard tests are unchanged by the new ops).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Guard/GuardTypes.cs src/UasSort.Core/Guard/IoGuardPolicy.cs src/UasSort.Core/Ports/Ports.cs tests/UasSort.Testing/PhotoCleanupFakes.cs tests/UasSort.Core.Tests/Guard/IoGuardPhotoCleanupTests.cs tests/UasSort.Core.Tests/Testing/PhotoCleanupFakesTests.cs
git commit -F - <<'EOF'
feat: guard photo-root reads and recycles; add the Picture Offload ports and fakes

<session trailer>
EOF
```

---
### Task P.7: Survey of the photo root (capture times, sets, cloud-only)

**Files:**
- Create: `src/UasSort.Core/Cleanup/Photos/PhotoExifCache.cs`
- Create: `src/UasSort.Core/Cleanup/Photos/PhotoCaptureClock.cs`
- Create: `src/UasSort.Core/Cleanup/Photos/PhotoCleanupPlanner.Survey.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupSurveyTests.cs`

**Interfaces:**
- Consumes: `IPhotoFileReader` (P.6), `StillProbe.Read` + `StillInfo.SubSec/PixelWidth/PixelHeight`, `ExifStamp` (P.2), `PhotoMember`/`PhotoItem`/`PhotoSurvey`/`PhotoCleanupRules`/`PhotoScanProgress` (P.5), `LedgerSnapshot.Files`, `LedgerFile`, `LedgerPaths.FolderName`, `DroneClock.Learn`, `ClockConversion.ToUtc`, `Zones`, `ITimeZoneResolver`, `GeoTimeZoneResolver`, `GpsFix`, `StoredClockMode` (existing, `UasSort.Core.Time` / `UasSort.Core.Geo`), `FakePhotoFileReader` (P.6), `TestLedger`, `LedgerLines.FileRec` (existing).
- Produces:
  - `public sealed record PhotoExifRead(StillInfo? Info, string? Problem) { ExifStamp? Stamp; }`
  - `public sealed class PhotoExifCache(IPhotoFileReader reader) { const string CloudOnlyProblem; int Opens; PhotoExifRead Read(string fullPath, long size, DateTime mtimeUtc, uint attributes); }`
  - `public sealed class PhotoCaptureClock { PhotoCaptureClock(StoredClockMode mode, string zoneId, ITimeZoneResolver tz, TimeZoneInfo pc); static PhotoCaptureClock For(Settings s, ITimeZoneResolver tz, TimeZoneInfo pc); (DateTime? Utc, DateOnly LocalDate) Resolve(StillInfo info); }` (needs `info.DtoNaive`)
  - `public static partial class PhotoCleanupPlanner { static readonly IReadOnlySet<string> Excludes; static PhotoSurvey Survey(string photoRoot, ListingResult listing, LedgerSnapshot ledger, PhotoExifCache exif, PhotoCaptureClock clock, IProgress<PhotoScanProgress>? progress, CancellationToken ct); }`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupSurveyTests.cs
using UasSort.Core.Tests.Ledger;
using static UasSort.Core.Tests.Ledger.LedgerLines;
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class PhotoCleanupSurveyTests
{
    private static readonly DateTime Shot = new(2026, 6, 1, 12, 10, 0);
    private static readonly DateTime May30Utc = new(2026, 5, 30, 20, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo JuneauPc = Zones.Find("America/Juneau");

    private sealed class Rig
    {
        public Rig()
        {
            Reader = new FakePhotoFileReader(Fs, FakeLayout.Context(cardRoot: null));
            Exif = new PhotoExifCache(Reader);
        }

        public FakeFileSystem Fs { get; } = FakeLayout.NewFileSystem();
        public FakePhotoFileReader Reader { get; }
        public PhotoExifCache Exif { get; }
        public LedgerSnapshot Ledger { get; set; } = TestLedger.Empty();
        public Settings Settings { get; set; } = FakeLayout.Settings();        // drone clock: Zone America/New_York

        public string Dng(string rel, DateTime dto, uint attributes = 0x20, string? offset = null, bool gps = true)
        {
            var path = PhotoRoot + @"\" + rel;
            var builder = new SyntheticDngBuilder { OffsetTimeOriginal = offset }.WithDateTimeOriginal(dto);
            Fs.AddFile(path, (gps ? builder : builder with { Gps = null }).Build(), Mtime, attributes);
            return path;
        }

        public long SizeOf(string path) => Fs.Metadata(path)!.Size;

        public PhotoSurvey Survey()
            => PhotoCleanupPlanner.Survey(PhotoRoot, Fs.Enumerate(PhotoRoot, true, PhotoCleanupPlanner.Excludes), Ledger, Exif,
                                          PhotoCaptureClock.For(Settings, new GeoTimeZoneResolver(), JuneauPc), null, CancellationToken.None);
    }

    [Fact]
    public void Survey_PairsDngAndJpg_FindsSetFolders_AndLeavesEverythingElseUntouched()
    {
        var rig = new Rig();
        rig.Dng("A.DNG", Shot);
        rig.Dng("A.JPG", Shot);
        rig.Dng("B.JPG", Shot.AddSeconds(3));
        rig.Fs.AddFile(PhotoRoot + @"\notes.txt", 10, Mtime);
        rig.Fs.AddFile(PhotoRoot + @"\desktop.ini", 10, Mtime);
        rig.Dng(@"001_0087\PANO_0001.DNG", Shot);
        rig.Dng(@"001_0087\PANO_0002.DNG", Shot.AddSeconds(2));
        rig.Dng(@"Exports\x.jpg", Shot);
        rig.Dng(@"002_0003\sub\x.DNG", Shot);

        var s = rig.Survey();

        Assert.Equal(["001_0087", "A.DNG", "B.JPG"], s.Items.Select(i => i.RelPath));
        Assert.Equal(["A.DNG", "A.JPG"], s.Items.Single(i => i.RelPath == "A.DNG").Members.Select(m => m.RelPath));
        var set = s.Items.Single(i => i.RelPath == "001_0087");
        Assert.Equal((PhotoItemKind.Set, PhotoSetKind.Unknown, "001_0087"), (set.Kind, set.SetKind, set.SetName));
        Assert.Equal([@"001_0087\PANO_0001.DNG", @"001_0087\PANO_0002.DNG"], set.Members.Select(m => m.RelPath));
        Assert.Equal(["002_0003", "desktop.ini", "Exports", "notes.txt"], s.NotTouched);
    }

    [Fact]
    public void Survey_ALedgerDate_WinsAndTheFileIsNotOpened()
    {
        var rig = new Rig();
        var path = rig.Dng("A.DNG", Shot);
        rig.Ledger = TestLedger.Snapshot(FileRec("f1", "A.DNG", rig.SizeOf(path), path, root: "photo", kind: "photo", captureUtc: May30Utc));
        var m = Assert.Single(Assert.Single(rig.Survey().Items).Members);
        Assert.Equal((CaptureSource.Ledger, (DateOnly?)new DateOnly(2026, 5, 30), (DateTime?)May30Utc), (m.Source, m.LocalDate, m.CaptureUtc));
        Assert.NotNull(m.Ledger);
        Assert.Null(m.Stamp);
        Assert.Empty(rig.Reader.Opened);
    }

    [Fact]
    public void Survey_OnlyTheRecordForThisDestAndSize_Dates()
    {
        var rig = new Rig();
        var path = rig.Dng("A.DNG", Shot);
        rig.Ledger = TestLedger.Snapshot(                                   // same name and size, another destination
            FileRec("f1", "A.DNG", rig.SizeOf(path), PhotoRoot + @"\Old\A.DNG", root: "photo", kind: "photo", captureUtc: May30Utc));
        var other = Assert.Single(Assert.Single(rig.Survey().Items).Members);
        Assert.Equal((CaptureSource.Exif, (LedgerFile?)null), (other.Source, other.Ledger));
        rig.Ledger = TestLedger.Snapshot(                                   // this destination, but the file there was replaced
            FileRec("f2", "A.DNG", rig.SizeOf(path) + 1, path, root: "photo", kind: "photo", captureUtc: May30Utc));
        Assert.Equal(CaptureSource.Exif, Assert.Single(Assert.Single(rig.Survey().Items).Members).Source);
    }

    [Fact]
    public void Survey_ALocalFileWithoutARecord_IsDatedFromItsExif()
    {
        var rig = new Rig();
        rig.Dng("A.DNG", new DateTime(2026, 6, 1, 23, 30, 0), offset: "-08:00");     // GPS: Kodiak (America/Anchorage, UTC−8)
        var m = Assert.Single(Assert.Single(rig.Survey().Items).Members);
        Assert.Equal((CaptureSource.Exif, (DateOnly?)new DateOnly(2026, 6, 1)), (m.Source, m.LocalDate));
        Assert.Equal(new DateTime(2026, 6, 2, 7, 30, 0, DateTimeKind.Utc), m.CaptureUtc);
        Assert.Equal(new ExifStamp(new DateTime(2026, 6, 1, 23, 30, 0), null, "FC9113"), m.Stamp);
        Assert.Single(rig.Reader.Opened);
    }

    [Fact]
    public void Survey_WithoutAnOffset_UsesTheSavedDroneClock_ThenThePcZone_LikeTheScan()
    {
        var rig = new Rig { Settings = FakeLayout.Settings() with { DroneClockMode = StoredClockMode.Zone, DroneClockZone = "Etc/UTC" } };
        rig.Dng("A.DNG", new DateTime(2026, 7, 1, 3, 30, 0), gps: false);             // a UTC drone clock, no GPS: the PC zone (Juneau, UTC−8)
        var m = Assert.Single(Assert.Single(rig.Survey().Items).Members);
        Assert.Equal(new DateTime(2026, 7, 1, 3, 30, 0, DateTimeKind.Utc), m.CaptureUtc);
        Assert.Equal((CaptureSource.Exif, (DateOnly?)new DateOnly(2026, 6, 30)), (m.Source, m.LocalDate));   // not the naive Jul 1
    }

    [Fact] // [Review Focus 1]
    public void Survey_CloudOnlyPhoto_IsDateUnknown_AndNeverOpened()
    {
        var rig = new Rig();
        rig.Dng("A.DNG", Shot, FakeFileSystem.CloudOnlyPlaceholder);                       // no ledger record
        var dated = rig.Dng("B.DNG", Shot, FakeFileSystem.CloudOnlyPlaceholder);           // has a ledger record
        rig.Dng(@"001_0087\PANO_0001.DNG", Shot);
        rig.Dng(@"001_0087\PANO_0002.DNG", Shot, FakeFileSystem.CloudOnlyPlaceholder);
        rig.Ledger = TestLedger.Snapshot(FileRec("f1", "B.DNG", rig.SizeOf(dated), dated, root: "photo", kind: "photo", captureUtc: May30Utc));

        var s = rig.Survey();

        var a = s.Items.Single(i => i.RelPath == "A.DNG").Primary;
        Assert.Null(a.LocalDate);
        Assert.Equal("date unknown (cloud-only)", a.DateProblem);
        Assert.Equal(new DateOnly(2026, 5, 30), s.Items.Single(i => i.RelPath == "B.DNG").Primary.LocalDate);
        Assert.False(s.Items.Single(i => i.RelPath == "001_0087").DateKnown);
        Assert.Empty(rig.Fs.HydrationViolations);
        Assert.Equal([PhotoRoot + @"\001_0087\PANO_0001.DNG"], rig.Reader.Opened);
    }

    [Fact]
    public void Survey_SetKindAndNameComeFromTheLedger()
    {
        var rig = new Rig();
        var hyper = rig.Dng(@"001_0042 2026-06-01\HYPERLAPSE_0001.DNG", Shot);
        var other = rig.Dng(@"Kodiak pano\PANO_0001.DNG", Shot);
        rig.Ledger = TestLedger.Snapshot(
            FileRec("f1", "HYPERLAPSE_0001.DNG", 1, hyper, root: "photo", kind: "setMember", set: "001_0042", captureUtc: May30Utc)
                with { Src = "DCIM/HYPERLAPSE/001_0042/HYPERLAPSE_0001.DNG" },
            FileRec("f2", "PANO_0001.DNG", 2, other, root: "photo", kind: "setMember", set: "003_0004", captureUtc: May30Utc)
                with { Src = "DCIM/PANORAMA/003_0004/PANO_0001.DNG" });
        var s = rig.Survey();
        var h = s.Items.Single(i => i.RelPath == "001_0042 2026-06-01");
        Assert.Equal((PhotoSetKind.Hyperlapse, "001_0042"), (h.SetKind, h.SetName));
        var p = s.Items.Single(i => i.RelPath == "Kodiak pano");
        Assert.Equal((PhotoSetKind.Panorama, "003_0004"), (p.SetKind, p.SetName));
    }

    [Fact]
    public void Survey_AnUnreadableLocalFile_HasADateProblem()
    {
        var rig = new Rig();
        rig.Fs.AddFile(PhotoRoot + @"\C.DNG", [1, 2, 3, 4, 5, 6, 7, 8], Mtime);
        var m = Assert.Single(Assert.Single(rig.Survey().Items).Members);
        Assert.Null(m.LocalDate);
        Assert.StartsWith("its capture time couldn't be read", m.DateProblem, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PhotoCleanupSurveyTests"`
Expected: build fails (`CS0246: … 'PhotoExifCache' could not be found`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoExifCache.cs
using System.Globalization;

namespace UasSort.Core.Cleanup;

public sealed record PhotoExifRead(StillInfo? Info, string? Problem)
{
    public ExifStamp? Stamp => ExifStamp.From(Info);
}

/// <summary>EXIF of Picture Offload and Lightroom files through IPhotoFileReader, read once per (path, size, mtime). A cloud-only file is
/// never opened (its attributes say so, spec 2026-10-04 §3); a failed or refused read becomes a Problem, never an exception — when the
/// guard refuses (UnsafeIoException) nothing was opened, and the file is simply treated as unreadable.</summary>
public sealed class PhotoExifCache(IPhotoFileReader reader)
{
    public const string CloudOnlyProblem = PhotoCleanupRules.DateUnknownCloudOnly;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, PhotoExifRead> _reads = new(StringComparer.Ordinal);

    public int Opens { get; private set; }

    public PhotoExifRead Read(string fullPath, long size, DateTime mtimeUtc, uint attributes)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        if (PhotoCleanupRules.IsCloudOnly(attributes)) return new PhotoExifRead(null, CloudOnlyProblem);
        var key = string.Create(CultureInfo.InvariantCulture, $"{PathRules.Normalize(fullPath).ToUpperInvariant()}|{size}|{mtimeUtc.Ticks}");
        lock (_gate)
        {
            if (_reads.TryGetValue(key, out var hit)) return hit;
        }
        PhotoExifRead read;
        try
        {
            using var s = reader.OpenRead(fullPath);
            var info = StillProbe.Read(s);
            read = new PhotoExifRead(info, info.DtoNaive is null ? "it has no capture time (DateTimeOriginal)" : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeIoException
                                      or MetadataExtractor.ImageProcessingException)
        {
            read = new PhotoExifRead(null, "its capture time couldn't be read: " + e.Message);
        }
        lock (_gate)
        {
            Opens++;
            _reads[key] = read;
        }
        return read;
    }
}
```

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoCaptureClock.cs
namespace UasSort.Core.Cleanup;

/// <summary>A Picture Offload still's capture time and local date from its EXIF, resolved as the scan resolves a still (spec 2026-10-04
/// §3(2), main spec §5.2, TimeResolver.Capture/ZoneFor): DateTimeOriginal − OffsetTimeOriginal when present, else the saved drone clock
/// (no videos to learn from: DroneClock.Learn gives ClockMode.Setting) with the photo's own GPS zone as the site zone; the local date in
/// the photo's own GPS zone, else the PC zone. Only when the clock zone is unknown is the naive calendar date used.</summary>
public sealed class PhotoCaptureClock
{
    private readonly ClockModel _model;
    private readonly ITimeZoneResolver _tz;
    private readonly TimeZoneInfo _pc;

    public PhotoCaptureClock(StoredClockMode mode, string zoneId, ITimeZoneResolver tz, TimeZoneInfo pc)
    {
        ArgumentNullException.ThrowIfNull(zoneId);
        ArgumentNullException.ThrowIfNull(tz);
        ArgumentNullException.ThrowIfNull(pc);
        _model = DroneClock.Learn([], mode, zoneId, tz, pc);
        _tz = tz;
        _pc = pc;
    }

    public static PhotoCaptureClock For(Settings s, ITimeZoneResolver tz, TimeZoneInfo pc)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new PhotoCaptureClock(s.DroneClockMode, s.DroneClockZone, tz, pc);
    }

    public (DateTime? Utc, DateOnly LocalDate) Resolve(StillInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var dto = DateTime.SpecifyKind(info.DtoNaive ?? throw new ArgumentException("no DateTimeOriginal", nameof(info)), DateTimeKind.Unspecified);
        string? own = info.Gps is GpsFix fix && _tz.Resolve(fix.Point) is { IsEtc: false } l && Zones.TryFind(l.IanaId, out _) ? l.IanaId : null;
        DateTime? utc = info.OffsetTime is { } offset ? DateTime.SpecifyKind(dto - offset, DateTimeKind.Utc)
                      : ClockConversion.ToUtc(_model, dto, own)?.Utc;
        if (utc is not { } u) return (null, DateOnly.FromDateTime(dto));
        var zone = own is not null && Zones.TryFind(own, out var z) ? z : _pc;
        return (u, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(u, zone)));
    }
}
```

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoCleanupPlanner.Survey.cs
namespace UasSort.Core.Cleanup;

/// <summary>Picture Offload cleanup planning (spec 2026-10-04 §3–§4). Pure apart from the EXIF reads through PhotoExifCache, which never
/// opens a cloud-only file.</summary>
public static partial class PhotoCleanupPlanner
{
    /// <summary>The photo root is listed like every library root: without the ledger folder (Ref §7.1).</summary>
    public static readonly IReadOnlySet<string> Excludes = new HashSet<string>([LedgerPaths.FolderName], StringComparer.OrdinalIgnoreCase);

    /// <summary>What is directly in the photo root, and when each file was shot: (1) the ledger file record for that destination with the
    /// file's size (resolved ambiguity 8a); (2) the EXIF of a local file, resolved by <paramref name="clock"/> as the scan does (8);
    /// (3) otherwise "date unknown (cloud-only)".</summary>
    public static PhotoSurvey Survey(string photoRoot, ListingResult listing, LedgerSnapshot ledger, PhotoExifCache exif, PhotoCaptureClock clock,
                                     IProgress<PhotoScanProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(photoRoot);
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(exif);
        ArgumentNullException.ThrowIfNull(clock);
        var root = PathRules.Normalize(photoRoot);
        var byDest = new Dictionary<string, LedgerFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in ledger.Files.Values)
            if (f.Root == DestRoot.Photo) byDest.TryAdd(PathRules.Normalize(f.Dest), f);

        var topFiles = new List<FsEntry>();
        var topDirs = new List<FsEntry>();
        var inDir = new Dictionary<string, List<FsEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in listing.Entries)
        {
            var rel = e.RelPath.Replace('/', '\\').Trim('\\');
            var cut = rel.IndexOf('\\');
            if (cut < 0)
            {
                (e.IsDirectory ? topDirs : topFiles).Add(e);
                continue;
            }
            var top = rel[..cut];
            if (!inDir.TryGetValue(top, out var list)) inDir[top] = list = [];
            list.Add(e with { RelPath = rel });
        }

        var notTouched = new List<string>();
        var pending = new List<(string RelPath, PhotoItemKind Kind, PhotoSetKind SetKind, string? SetName, List<FsEntry> Files)>();

        // Loose photos: a DNG and a JPG/JPEG with the same stem are one unit (the DNG decides); every other photo is its own unit.
        foreach (var stem in topFiles.Where(f => PhotoCleanupRules.IsPhotoName(Name(f)))
                                     .GroupBy(f => Path.GetFileNameWithoutExtension(Name(f)), StringComparer.OrdinalIgnoreCase))
        {
            var files = stem.OrderBy(Name, StringComparer.OrdinalIgnoreCase).ToList();
            var dng = files.FirstOrDefault(f => PhotoCleanupRules.IsDng(Name(f)));
            var twin = dng is null ? null : files.FirstOrDefault(f => PhotoCleanupRules.IsJpg(Name(f)));
            if (dng is not null && twin is not null)
            {
                pending.Add((Name(dng), PhotoItemKind.Photo, PhotoSetKind.Unknown, null, [dng, twin]));
                files.Remove(dng);
                files.Remove(twin);
            }
            foreach (var f in files) pending.Add((Name(f), PhotoItemKind.Photo, PhotoSetKind.Unknown, null, [f]));
        }
        foreach (var f in topFiles.Where(f => !PhotoCleanupRules.IsPhotoName(Name(f)))) notTouched.Add(Name(f));

        // Set folders: named like a set, or holding this app's set-member records; only photo files directly inside, nothing else.
        foreach (var d in topDirs)
        {
            var folder = Name(d);
            var content = inDir.GetValueOrDefault(folder) ?? [];
            var records = content.Select(c => byDest.GetValueOrDefault(PathRules.Join(root, c.RelPath))).OfType<LedgerFile>().ToList();
            var isSet = PhotoCleanupRules.LooksLikeSetFolder(folder) || records.Exists(r => r.Set is not null);
            var plain = content.Count > 0 && content.TrueForAll(c => !c.IsDirectory && c.RelPath.Count(ch => ch == '\\') == 1
                                                                      && PhotoCleanupRules.IsPhotoName(Name(c)));
            if (!isSet || !plain)
            {
                notTouched.Add(folder);
                continue;
            }
            var setName = records.Select(r => r.Set).FirstOrDefault(s => s is not null) ?? PhotoCleanupRules.SetNameOf(folder);
            pending.Add((folder, PhotoItemKind.Set, KindOf(records), setName, [.. content.OrderBy(Name, StringComparer.OrdinalIgnoreCase)]));
        }

        var toRead = pending.Sum(p => p.Files.Count(f => Record(Full(p.Kind, f), f.Size) is not { LocalDate: not null }
                                                         && !PhotoCleanupRules.IsCloudOnly(f.RawAttributes)));
        var done = 0;
        var items = new List<PhotoItem>(pending.Count);
        foreach (var p in pending)
        {
            ct.ThrowIfCancellationRequested();
            var members = ImmutableArray.CreateBuilder<PhotoMember>(p.Files.Count);
            foreach (var f in p.Files)
            {
                var rel = p.Kind == PhotoItemKind.Set ? f.RelPath.Replace('/', '\\').Trim('\\') : Name(f);
                var full = PathRules.Join(root, rel);
                var record = Record(full, f.Size);
                if (record is { LocalDate: { } date })
                {
                    members.Add(new PhotoMember(rel, f.Size, f.MtimeUtc, f.RawAttributes, record.CaptureUtc, date, CaptureSource.Ledger, null, null, record));
                    continue;
                }
                var read = exif.Read(full, f.Size, f.MtimeUtc, f.RawAttributes);
                if (!PhotoCleanupRules.IsCloudOnly(f.RawAttributes)) progress?.Report(new PhotoScanProgress("Reading photo dates", ++done, toRead));
                if (read.Info is { DtoNaive: not null } info)
                {
                    var (utc, localDate) = clock.Resolve(info);
                    members.Add(new PhotoMember(rel, f.Size, f.MtimeUtc, f.RawAttributes, utc, localDate, CaptureSource.Exif, read.Stamp, null, record)
                    {
                        Pixels = PhotoCleanupRules.PixelsOf(info),
                    });
                }
                else
                {
                    members.Add(new PhotoMember(rel, f.Size, f.MtimeUtc, f.RawAttributes, null, null, CaptureSource.None, null,
                                                read.Problem ?? "date unknown", record));
                }
            }
            items.Add(new PhotoItem(p.RelPath, p.Kind, p.SetKind, p.SetName, members.MoveToImmutable()));
        }

        return new PhotoSurvey(root,
            [.. items.OrderBy(i => i.FirstDate ?? DateOnly.MaxValue).ThenBy(i => i.RelPath, StringComparer.OrdinalIgnoreCase)],
            [.. notTouched.Order(StringComparer.OrdinalIgnoreCase)], ledger);

        string Full(PhotoItemKind kind, FsEntry f)
            => PathRules.Join(root, kind == PhotoItemKind.Set ? f.RelPath.Replace('/', '\\').Trim('\\') : Name(f));

        // Spec §3(1): the record for this destination only, and only while the file there still has the recorded size (8a).
        LedgerFile? Record(string full, long size)
            => byDest.GetValueOrDefault(PathRules.Normalize(full)) is { } r && r.Key.Size == size ? r : null;
    }

    private static string Name(FsEntry e) => PathRules.FileName(e.FullPath);

    private static PhotoSetKind KindOf(IEnumerable<LedgerFile> records)
    {
        foreach (var r in records)
        {
            var src = r.Src.Replace('\\', '/');
            if (src.Contains("/PANORAMA/", StringComparison.OrdinalIgnoreCase)) return PhotoSetKind.Panorama;
            if (src.Contains("/HYPERLAPSE/", StringComparison.OrdinalIgnoreCase)) return PhotoSetKind.Hyperlapse;
        }
        return PhotoSetKind.Unknown;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PhotoCleanupSurveyTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/Photos/PhotoExifCache.cs src/UasSort.Core/Cleanup/Photos/PhotoCaptureClock.cs src/UasSort.Core/Cleanup/Photos/PhotoCleanupPlanner.Survey.cs tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupSurveyTests.cs
git commit -F - <<'EOF'
feat: survey Picture Offload with ledger and local-EXIF capture times, never hydrating

<session trailer>
EOF
```

---

### Task P.8: Lightroom index

**Files:**
- Create: `src/UasSort.Core/Cleanup/Photos/LightroomIndex.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/Photos/LightroomIndexTests.cs`

**Interfaces:**
- Consumes: `IDirectoryLister`, `PhotoExifCache` (P.7), `ExifStamp` (P.2), `LightroomRules` (P.1), `PhotoScanProgress` (P.5).
- Produces:
  - `public sealed record LightroomPhoto(string FullPath, ExifStamp Stamp) { bool IsDng; }` (photos and set frames match `.dng` entries only; the stitched panorama may match a `.jpg`/`.jpeg` entry too — spec §4)
  - `public sealed class LightroomIndex { string Folder; int Count; int Opened; ImmutableArray<(string Path, int Win32Error)> Errors; ImmutableArray<string> Unreadable; static LightroomIndex Build(string folder, IDirectoryLister lister, PhotoExifCache exif, DateOnly from, DateOnly to, IProgress<PhotoScanProgress>? progress, CancellationToken ct); static LightroomIndex Empty(string folder); internal static LightroomIndex From(string folder, IEnumerable<LightroomPhoto> photos); IReadOnlyList<LightroomPhoto> SameSecond(ExifStamp stamp); internal static bool FolderOutside(string name, DateOnly lo, DateOnly hi); }`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/UasSort.Core.Tests/Cleanup/Photos/LightroomIndexTests.cs
namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class LightroomIndexTests
{
    private const string Lr = @"X:\Lightroom";
    private static readonly DateTime Shot = new(2026, 6, 1, 12, 10, 0);
    private static readonly DateTime ImportedAt = new(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);

    private sealed class CountingLister(IDirectoryLister inner) : IDirectoryLister
    {
        public List<string> Listed { get; } = [];

        public ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames)
        {
            Listed.Add(PathRules.Normalize(root));
            return inner.Enumerate(root, recurse, excludeDirNames);
        }
    }

    private sealed class Rig
    {
        public Rig()
        {
            Lister = new CountingLister(Fs);
            Reader = new FakePhotoFileReader(Fs, FakeLayout.Context(cardRoot: null) with { LightroomFolder = Lr });
            Exif = new PhotoExifCache(Reader);
            Fs.AddDirectory(Lr);
        }

        public FakeFileSystem Fs { get; } = FakeLayout.NewFileSystem();
        public CountingLister Lister { get; }
        public FakePhotoFileReader Reader { get; }
        public PhotoExifCache Exif { get; }

        public void Dng(string rel, DateTime dto, string? sub = null, DateTime? mtime = null, uint attributes = 0x20)
            => Fs.AddFile(Lr + @"\" + rel, new SyntheticDngBuilder { SubSecTimeOriginal = sub }.WithDateTimeOriginal(dto).Build(), mtime ?? ImportedAt,
                          attributes);

        public LightroomIndex Build()
            => LightroomIndex.Build(Lr, Lister, Exif, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, CancellationToken.None);
    }

    [Fact]
    public void Build_IndexesShotsInTheRange_BySecondAndModel()
    {
        var rig = new Rig();
        rig.Dng(@"2026\2026-06-01\Damian_20260601_001.dng", Shot, "045");
        rig.Dng(@"2026\2026-06-01\Damian_20260601_002.dng", Shot.AddSeconds(5));
        rig.Dng(@"2026\2026-06-01\Damian_20260601_003.jpg", Shot.AddSeconds(9));
        rig.Dng(@"2026\2026-08-10\Damian_20260810_001.dng", new DateTime(2026, 8, 10, 9, 0, 0));

        var index = rig.Build();

        Assert.Equal(3, index.Count);
        var hit = Assert.Single(index.SameSecond(new ExifStamp(Shot, null, "fc9113")));
        Assert.Equal(("045", Lr + @"\2026\2026-06-01\Damian_20260601_001.dng", true), (hit.Stamp.SubSec, hit.FullPath, hit.IsDng));
        Assert.False(Assert.Single(index.SameSecond(new ExifStamp(Shot.AddSeconds(9), null, "FC9113"))).IsDng);   // kept for stitched panoramas only
        Assert.DoesNotContain(rig.Lister.Listed, p => p.EndsWith("2026-08-10", StringComparison.Ordinal));
    }

    [Fact] // [Review Focus 1]
    public void Build_ACloudOnlyLibraryFile_IsNeverOpened()
    {
        var rig = new Rig();
        rig.Dng(@"2026\2026-06-01\Damian_20260601_001.dng", Shot, attributes: FakeFileSystem.CloudOnlyPlaceholder);
        var index = rig.Build();
        Assert.Equal((0, 0), (index.Count, index.Opened));
        Assert.Empty(rig.Reader.Opened);
        Assert.Empty(rig.Fs.HydrationViolations);
        Assert.Equal([Lr + @"\2026\2026-06-01\Damian_20260601_001.dng"], index.Unreadable);
    }

    [Fact]
    public void Build_NeverListsIntoOrOpensTheCatalog()
    {
        var rig = new Rig();
        rig.Dng(@"2026\2026-06-01\Damian_20260601_001.dng", Shot);
        rig.Dng(@"LR_Catalog\Lightroom Catalog.lrcat", Shot);
        rig.Dng(@"Lightroom Catalog Previews.lrdata\0\A\x.dng", Shot);
        rig.Dng(@"Lightroom Catalog Smart Previews.lrdata\x.dng", Shot);
        rig.Dng(@"Lightroom Catalog.lrcat-data\x.dng", Shot);
        rig.Dng("Lightroom Catalog.lrcat", Shot);

        var index = rig.Build();

        Assert.Equal(1, index.Count);
        Assert.DoesNotContain(rig.Lister.Listed, LightroomRules.IsCatalogPath);
        Assert.DoesNotContain(rig.Reader.Opened, LightroomRules.IsCatalogPath);
        Assert.DoesNotContain(rig.Fs.GuardLog, g => LightroomRules.IsCatalogPath(g.Path));
    }

    [Fact]
    public void Build_SkipsFilesWrittenBeforeTheRange_WithoutOpeningThem()
    {
        var rig = new Rig();
        rig.Dng(@"Old\x.dng", Shot, mtime: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(0, rig.Build().Count);
        Assert.Empty(rig.Reader.Opened);
    }

    [Theory]
    [InlineData("2026", false)]
    [InlineData("2025", true)]
    [InlineData("2026-06", false)]
    [InlineData("2026-05", false)]
    [InlineData("2026-08", true)]
    [InlineData("2026-06-01 Juneau", false)]
    [InlineData("2026-07-05", true)]
    [InlineData("Imports", false)]
    public void FolderOutside_PrunesDatedFoldersOnly(string name, bool outside)
        => Assert.Equal(outside, LightroomIndex.FolderOutside(name, new DateOnly(2026, 5, 31), new DateOnly(2026, 7, 1)));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*LightroomIndexTests"`
Expected: build fails (`CS0103: The name 'LightroomIndex' does not exist in the current context`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Cleanup/Photos/LightroomIndex.cs
using System.Globalization;

namespace UasSort.Core.Cleanup;

/// <summary>One indexed library file. Spec §4: a photo or a set frame is verified by a DNG only (IsDng); a JPG entry can only confirm a
/// stitched panorama (an export or edit of a photo must never verify the Picture Offload original).</summary>
public sealed record LightroomPhoto(string FullPath, ExifStamp Stamp)
{
    public bool IsDng => PhotoCleanupRules.IsDng(FullPath);
}

/// <summary>The Lightroom library folder, read-only (spec 2026-10-04 §4): its DNG files (and JPGs, for stitched panoramas only) shot within
/// the range ± 1 day, by (DateTimeOriginal second, Model). Walks folder by folder so the catalog (LightroomRules) is never listed into or
/// opened, dated folders outside the range are not entered, files written before the range are not opened, and a cloud-only file is never
/// opened (PhotoExifCache; it is listed in Unreadable).</summary>
public sealed class LightroomIndex
{
    private static readonly IReadOnlySet<string> NoExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] Extensions = [".dng", ".jpg", ".jpeg"];
    private readonly Dictionary<(DateTime Second, string Model), List<LightroomPhoto>> _bySecond;

    private LightroomIndex(string folder, Dictionary<(DateTime Second, string Model), List<LightroomPhoto>> bySecond, int opened,
                           ImmutableArray<(string Path, int Win32Error)> errors, ImmutableArray<string> unreadable)
    {
        Folder = folder;
        _bySecond = bySecond;
        Count = bySecond.Values.Sum(l => l.Count);
        Opened = opened;
        Errors = errors;
        Unreadable = unreadable;
    }

    public string Folder { get; }
    public int Count { get; }
    public int Opened { get; }
    public ImmutableArray<(string Path, int Win32Error)> Errors { get; }
    public ImmutableArray<string> Unreadable { get; }

    public static LightroomIndex Empty(string folder)
        => new(PathRules.Normalize(folder), new Dictionary<(DateTime Second, string Model), List<LightroomPhoto>>(), 0, [], []);

    internal static LightroomIndex From(string folder, IEnumerable<LightroomPhoto> photos)
    {
        var map = new Dictionary<(DateTime Second, string Model), List<LightroomPhoto>>();
        foreach (var p in photos) Add(map, p);
        return new LightroomIndex(PathRules.Normalize(folder), map, 0, [], []);
    }

    public IReadOnlyList<LightroomPhoto> SameSecond(ExifStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        return _bySecond.TryGetValue((stamp.Second, Upper(stamp.Model)), out var list) ? list : [];
    }

    public static LightroomIndex Build(string folder, IDirectoryLister lister, PhotoExifCache exif, DateOnly from, DateOnly to,
                                       IProgress<PhotoScanProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(exif);
        var lo = from.AddDays(-1);
        var hi = to.AddDays(1);
        var errors = ImmutableArray.CreateBuilder<(string Path, int Win32Error)>();
        var candidates = new List<FsEntry>();
        var dirs = new Queue<string>();
        dirs.Enqueue(PathRules.Normalize(folder));
        while (dirs.TryDequeue(out var dir))
        {
            ct.ThrowIfCancellationRequested();
            var listing = lister.Enumerate(dir, recurse: false, NoExcludes);
            errors.AddRange(listing.Errors);
            foreach (var e in listing.Entries)
            {
                var name = PathRules.FileName(e.FullPath);
                if (LightroomRules.IsCatalogName(name) || LightroomRules.IsCatalogPath(e.FullPath)) continue;
                if (e.IsDirectory)
                {
                    if (!FolderOutside(name, lo, hi)) dirs.Enqueue(e.FullPath);
                    continue;
                }
                if (!Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)) continue;
                if (DateOnly.FromDateTime(e.MtimeUtc) < lo.AddDays(-1)) continue;   // written before the range: it can't hold a shot from it
                candidates.Add(e);
            }
        }

        var map = new Dictionary<(DateTime Second, string Model), List<LightroomPhoto>>();
        var unreadable = ImmutableArray.CreateBuilder<string>();
        var opened = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var e = candidates[i];
            progress?.Report(new PhotoScanProgress("Reading the Lightroom library", i + 1, candidates.Count));
            var read = exif.Read(e.FullPath, e.Size, e.MtimeUtc, e.RawAttributes);
            if (!PhotoCleanupRules.IsCloudOnly(e.RawAttributes)) opened++;
            if (read.Stamp is not { } stamp)
            {
                unreadable.Add(e.FullPath);
                continue;
            }
            var day = DateOnly.FromDateTime(stamp.Second);
            if (day >= lo && day <= hi) Add(map, new LightroomPhoto(e.FullPath, stamp));
        }
        return new LightroomIndex(PathRules.Normalize(folder), map, opened, errors.ToImmutable(), unreadable.ToImmutable());
    }

    /// <summary>A dated folder ("yyyy", "yyyy-MM", "yyyy-MM-dd…") wholly outside [lo, hi]; any other name is entered.</summary>
    internal static bool FolderOutside(string name, DateOnly lo, DateOnly hi)
    {
        var inv = CultureInfo.InvariantCulture;
        if (name.Length >= 10 && DateOnly.TryParseExact(name[..10], "yyyy-MM-dd", inv, DateTimeStyles.None, out var day))
            return day < lo || day > hi;
        if (name.Length == 7 && DateOnly.TryParseExact(name + "-01", "yyyy-MM-dd", inv, DateTimeStyles.None, out var month))
            return month.AddMonths(1).AddDays(-1) < lo || month > hi;
        if (name.Length == 4 && int.TryParse(name, NumberStyles.None, inv, out var year) && year is > 1900 and < 3000)
            return year < lo.Year || year > hi.Year;
        return false;
    }

    private static void Add(Dictionary<(DateTime Second, string Model), List<LightroomPhoto>> map, LightroomPhoto p)
    {
        var key = (p.Stamp.Second, Upper(p.Stamp.Model));
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(p);
    }

    private static string Upper(string model) => model.Trim().ToUpperInvariant();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*LightroomIndexTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/Photos/LightroomIndex.cs tests/UasSort.Core.Tests/Cleanup/Photos/LightroomIndexTests.cs
git commit -F - <<'EOF'
feat: index the Lightroom library read-only, skipping the catalog and out-of-range folders

<session trailer>
EOF
```

---

### Task P.9: Verification and plan building

**Files:**
- Create: `src/UasSort.Core/Cleanup/Photos/PhotoCleanupVerifier.cs`
- Create: `src/UasSort.Core/Cleanup/Photos/PhotoCleanupPlanner.Build.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupVerifierTests.cs`, `tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupBuildTests.cs`

**Interfaces:**
- Consumes: `LightroomIndex` (+ internal `From`) (P.8), `PhotoExifCache` (P.7), `PhotoSurvey`, `PhotoRow`, `PhotoVerification`, `PhotoCleanupPlan` internal ctor (P.5), `CleanupFormat.MonthDay`, `SessionKey.SameSession`, `LedgerFile` (existing).
- Produces:
  - `public static class PhotoCleanupVerifier { static readonly TimeSpan HyperlapseWindow, PanoramaStitchWindow; sealed record Context(LightroomIndex Index, LedgerSnapshot Ledger, ImmutableArray<PhotoMember> PhotoRootMembers, ImmutableArray<PhotoItem> FlatPhotos); static Context ContextFor(IReadOnlyList<PhotoItem> items, LightroomIndex index, LedgerSnapshot ledger); static PhotoVerification Verify(PhotoItem item, Context ctx); internal static (bool Ok, string Text) Match(PhotoMember m, Context ctx, bool allowJpg = false); internal static (LedgerFile? Video, string Why) HyperlapseVideo(PhotoItem set, LedgerSnapshot ledger); internal static (PhotoMember? Stitched, string Why) StitchedImage(PhotoItem set, Context ctx); }`
  - `PhotoCleanupPlanner.VerifyRange(PhotoSurvey survey, DateOnly cutoff) → (DateOnly From, DateOnly To)?`
  - `PhotoCleanupPlanner.Build(PhotoSurvey survey, PhotoCleanupRequest request, LightroomIndex? index, PhotoExifCache exif, IProgress<PhotoScanProgress>? progress, CancellationToken ct) → PhotoCleanupPlan`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupVerifierTests.cs
using UasSort.Core.Tests.Ledger;
using static UasSort.Core.Tests.Ledger.LedgerLines;
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class PhotoCleanupVerifierTests
{
    private const string Lr = @"X:\Lightroom";
    private static readonly DateOnly D = new(2026, 6, 1);
    private static readonly DateTime S = new(2026, 6, 1, 12, 10, 0);
    private static readonly DateTime SUtc = new(2026, 6, 1, 20, 10, 0, DateTimeKind.Utc);      // S on a UTC−8 drone clock

    private static ExifStamp St(int second = 0, string? sub = null, string model = "FC9113") => new(S.AddSeconds(second), sub, model);

    private static LightroomPhoto L(string name, ExifStamp stamp) => new(Lr + @"\2026\2026-06-01\" + name, stamp);

    private static PhotoItem P(string name, ExifStamp? stamp, uint attributes = 0x20, DateTime? captureUtc = null)
        => new(name, PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
               [Member(name, D, attributes: attributes, stamp: stamp, captureUtc: captureUtc ?? (stamp is null ? null : (DateTime?)SUtc.AddSeconds((stamp.Second - S).TotalSeconds)))]);

    /// <summary>A loose JPG-only still with its pixel size (the stitched-panorama shape test).</summary>
    private static PhotoItem Jpg(string name, ExifStamp stamp, (int Width, int Height)? pixels)
        => new(name, PhotoItemKind.Photo, PhotoSetKind.Unknown, null, [Member(name, D, 9_000_000, stamp: stamp) with { Pixels = pixels }]);

    private static PhotoVerification Verify(PhotoItem item, IEnumerable<PhotoItem> all, LedgerSnapshot? ledger = null, params LightroomPhoto[] lightroom)
        => PhotoCleanupVerifier.Verify(item, PhotoCleanupVerifier.ContextFor([.. all], LightroomIndex.From(Lr, lightroom), ledger ?? TestLedger.Empty()));

    [Fact]
    public void Verify_APhotoWithTheSameSubSecondsAndModel_IsInLightroom()
    {
        var a = P("A.DNG", St(sub: "045"));
        Assert.Equal(new PhotoVerification(true, PhotoEvidence.Lightroom, @"in Lightroom: 2026\2026-06-01\Damian_20260601_001.dng"),
                     Verify(a, [a], null, L("Damian_20260601_001.dng", St(sub: "0450"))));
    }

    [Fact]
    public void Verify_AModelOrSecondMismatch_IsNotFound()
    {
        var a = P("A.DNG", St());
        Assert.Equal("not found in Lightroom", Verify(a, [a], null, L("x.dng", St(model: "FC8282"))).Text);
        Assert.False(Verify(a, [a], null, L("x.dng", St(second: 1))).Verified);
    }

    [Fact]
    public void Verify_ALightroomJpgWithTheSameStamp_NeverVerifiesAPhotoOrAFrame()
    {
        var a = P("A.DNG", St(sub: "045"));
        Assert.Equal("not found in Lightroom", Verify(a, [a], null, L("Damian_20260601_001.jpg", St(sub: "045"))).Text);   // an export or edit
        var set = new PhotoItem("001_0087", PhotoItemKind.Set, PhotoSetKind.Unknown, "001_0087", [Member(@"001_0087\PANO_0001.DNG", D, stamp: St(5))]);
        Assert.False(Verify(set, [set], null, L("pano-frame.jpg", St(5))).Verified);
        Assert.Equal(new PhotoVerification(true, PhotoEvidence.Lightroom, @"in Lightroom: 2026\2026-06-01\x.dng"),
                     Verify(a, [a], null, L("x.jpg", St(sub: "045")), L("x.dng", St(sub: "045"))));        // a DNG and its export: the DNG
    }

    [Fact] // [Review Focus 1]
    public void Verify_ACloudOnlyPhoto_CantBeChecked()
    {
        var a = P("A.DNG", null, FakeFileSystem.CloudOnlyPlaceholder);
        Assert.Equal(new PhotoVerification(false, PhotoEvidence.Lightroom, "not on this PC (cloud-only) — couldn't check"),
                     Verify(a, [a], null, L("x.dng", St())));
    }

    [Fact] // [Review Focus 3]
    public void Verify_ShotsInTheSameSecond_NeedOneLightroomFileEach()
    {
        var a = P("A.DNG", St(sub: "100"));
        var b = P("B.DNG", St(sub: "600"));
        Assert.True(Verify(a, [a, b], null, L("a.dng", St(sub: "100"))).Verified);
        Assert.Equal("not found in Lightroom (another shot in the same second is)", Verify(b, [a, b], null, L("a.dng", St(sub: "100"))).Text);
        Assert.False(Verify(a, [a, b], null, L("x.dng", St())).Verified);                        // Lightroom has no sub-seconds: can't tell which
        PhotoItem[] aeb = [P("E1.DNG", St()), P("E2.DNG", St()), P("E3.DNG", St())];
        Assert.Equal("3 shots in the same second; only 1 in Lightroom — can't tell which", Verify(aeb[0], aeb, null, L("x.dng", St())).Text);
        Assert.Equal("3 shots in the same second; only 1 in Lightroom — can't tell which",
                     Verify(aeb[0], aeb, null, L("x.dng", St()), L("x.jpg", St()), L("y.jpg", St())).Text);   // exports don't count
        Assert.True(Verify(aeb[1], aeb, null, L("x.dng", St()), L("y.dng", St()), L("z.dng", St())).Verified);
        var only = P("O.DNG", St());
        Assert.True(Verify(only, [only], null, L("o.dng", St(sub: "500"))).Verified);           // to the second when one side has none
    }

    [Fact] // [Review Focus 3]
    public void Verify_AnUnreadableShotInTheSameSecond_LeavesOnlyExactSubSecondMatches()
    {
        var a = P("A.DNG", St());
        var cloud = P("B.DNG", null, FakeFileSystem.CloudOnlyPlaceholder, captureUtc: SUtc.AddMilliseconds(400));   // dated from the ledger only
        Assert.Equal("2 shots in the same second, 1 of them couldn't be read — can't tell which",
                     Verify(a, [a, cloud], null, L("x.dng", St())).Text);
        var exact = P("C.DNG", St(sub: "100"));
        Assert.True(Verify(exact, [exact, cloud], null, L("c.dng", St(sub: "100"))).Verified);
        var later = P("D.DNG", null, FakeFileSystem.CloudOnlyPlaceholder, captureUtc: SUtc.AddSeconds(1));         // another second: no peer
        Assert.True(Verify(a, [a, later], null, L("x.dng", St())).Verified);
    }

    [Fact]
    public void Verify_APair_IsDecidedByItsDng()
    {
        var pair = new PhotoItem("A.DNG", PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
            [Member("A.DNG", D, stamp: St()), Member("A.JPG", D, 8_000_000, stamp: St(second: 40))]);
        Assert.True(Verify(pair, [pair], null, L("a.dng", St())).Verified);
    }

    [Fact]
    public void Verify_ASetWithEveryFrameInLightroom_IsVerified_ElseItSaysWhichFramesAreMissing()
    {
        var set = new PhotoItem("001_0087", PhotoItemKind.Set, PhotoSetKind.Panorama, "001_0087",
            [Member(@"001_0087\PANO_0001.DNG", D, stamp: St()), Member(@"001_0087\PANO_0002.DNG", D, stamp: St(second: 2))]);
        Assert.Equal(new PhotoVerification(true, PhotoEvidence.Lightroom, "every frame is in Lightroom (2)"),
                     Verify(set, [set], null, L("p1.dng", St()), L("p2.dng", St(second: 2))));
        Assert.Equal("no stitched panorama next to the frames; 1 of 2 frames not confirmed: not found in Lightroom",
                     Verify(set, [set], null, L("p1.dng", St())).Text);
    }

    [Fact]
    public void Verify_APanorama_ByItsOnePanoramaShapedStitchedImage()
    {
        var set = new PhotoItem("001_0087", PhotoItemKind.Set, PhotoSetKind.Panorama, "001_0087",
            [Member(@"001_0087\PANO_0001.DNG", D, stamp: St()), Member(@"001_0087\PANO_0002.DNG", D, stamp: St(second: 20))]);
        var stitched = Jpg("DJI_20260601121120_0010_D.JPG", St(second: 80), (8192, 4096));
        var late = Jpg("DJI_20260601121500_0011_D.JPG", St(second: 300), (8192, 4096));
        Assert.Equal(new PhotoVerification(true, PhotoEvidence.PanoramaStitch, "stitched panorama DJI_20260601121120_0010_D.JPG is in Lightroom"),
                     Verify(set, [set, stitched, late], null, L("pano.dng", St(second: 80))));
        Assert.True(Verify(set, [set, stitched], null, L("pano-export.jpg", St(second: 80))).Verified);      // spec §4: DNG or JPG
        Assert.False(Verify(set, [set, late], null, L("late.dng", St(second: 300))).Verified);              // more than 2 min after the last frame
    }

    [Fact]
    public void Verify_AnOrdinaryJpgNextToThePanorama_NeverVerifiesIt()
    {
        var set = new PhotoItem("001_0087", PhotoItemKind.Set, PhotoSetKind.Panorama, "001_0087",
            [Member(@"001_0087\PANO_0001.DNG", D, stamp: St()), Member(@"001_0087\PANO_0002.DNG", D, stamp: St(second: 20))]);
        var normal = Jpg("DJI_20260601121050_0010_D.JPG", St(second: 50), (4032, 3024));
        var stitched = Jpg("DJI_20260601121120_0011_D.JPG", St(second: 80), (8192, 4096));
        var unknownSize = Jpg("DJI_20260601121120_0012_D.JPG", St(second: 80), null);
        Assert.Equal("DJI_20260601121050_0010_D.JPG next to the frames is not panorama-shaped; 2 of 2 frames not confirmed: not found in Lightroom",
                     Verify(set, [set, normal], null, L("n.dng", St(second: 50))).Text);
        Assert.Equal("2 JPG photos next to the frames — can't tell which is the stitched panorama; 2 of 2 frames not confirmed: not found in Lightroom",
                     Verify(set, [set, normal, stitched], null, L("n.dng", St(second: 50)), L("s.dng", St(second: 80))).Text);
        Assert.False(Verify(set, [set, unknownSize], null, L("u.dng", St(second: 80))).Verified);
    }

    private static readonly DateTime T0 = new(2026, 6, 1, 20, 10, 0, DateTimeKind.Utc);
    private const string Serial = "1581F0001";

    private static PhotoItem Hyperlapse(SessionKey? session)
    {
        LedgerFile Rec(string name, DateTime capture) => new(FileKey.Of(name, 13_000_000), "DCIM/HYPERLAPSE/001_0042/" + name, DestRoot.Photo,
            PhotoRoot + @"\001_0042\" + name, null, VerifyKind.Unbuffered, T0.AddHours(5), capture, null, "America/Juneau",
            DateOnly.FromDateTime(capture), session, "001_0042", "DESKTOP-A", "run-1");
        return new PhotoItem("001_0042", PhotoItemKind.Set, PhotoSetKind.Hyperlapse, "001_0042",
            [Member(@"001_0042\HYPERLAPSE_0001.DNG", D, captureUtc: T0, ledger: Rec("HYPERLAPSE_0001.DNG", T0)),
             Member(@"001_0042\HYPERLAPSE_0002.DNG", D, captureUtc: T0.AddSeconds(90), ledger: Rec("HYPERLAPSE_0002.DNG", T0.AddSeconds(90)))]);
    }

    private static LedgerSnapshot Video(DateTime capture, string? serial, DateTime? sessionUtc)
        => TestLedger.Snapshot(FileRec("v1", "DJI_20260601161130_0007_D.MP4", 300_000_000,
            @"C:\Lib\UAS Videos\2026\2026-06\2026-06-01 Juneau\DJI_20260601161130_0007_D.MP4", captureUtc: capture, serial: serial, sessionUtc: sessionUtc));

    [Fact]
    public void Verify_AHyperlapse_ByItsResultVideo_SameSessionOrSameSerialWithinTwoMinutes()
    {
        var set = Hyperlapse(new SessionKey(Serial, T0.AddMinutes(-30)));
        var verified = new PhotoVerification(true, PhotoEvidence.HyperlapseResult, "hyperlapse result video is in the library: DJI_20260601161130_0007_D.MP4");
        Assert.Equal(verified, Verify(set, [set], Video(T0.AddHours(3), Serial, T0.AddMinutes(-30))));      // same session, any time
        Assert.Equal(verified, Verify(set, [set], Video(T0.AddSeconds(150), Serial, T0.AddMinutes(-5))));    // same serial, in the window
        Assert.Equal("hyperlapse result video not in the library", Verify(set, [set], Video(T0.AddHours(1), Serial, T0.AddMinutes(-5))).Text);
    }

    [Fact]
    public void Verify_AHyperlapse_AVideoOfAnotherOrAnUnknownDroneInTheWindow_DoesNotVerify()
    {
        var set = Hyperlapse(new SessionKey(Serial, T0.AddMinutes(-30)));
        Assert.Equal("hyperlapse result video not in the library", Verify(set, [set], Video(T0.AddSeconds(150), "1581F0999", T0.AddMinutes(-5))).Text);
        Assert.Equal("hyperlapse result video not in the library", Verify(set, [set], Video(T0.AddSeconds(150), null, null)).Text);
        var noSerial = Hyperlapse(null);                                                                     // DJI stills: usually no serial
        Assert.Equal("a video in the library was shot then, but these frames carry no drone serial — can't tell it is this hyperlapse's result",
                     Verify(noSerial, [noSerial], Video(T0.AddSeconds(150), Serial, T0.AddMinutes(-5))).Text);
    }
}
```

```csharp
// tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupBuildTests.cs
using UasSort.Core.Tests.Ledger;
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class PhotoCleanupBuildTests
{
    private static readonly DateOnly Jun1 = new(2026, 6, 1), Jun30 = new(2026, 6, 30), Jul1 = new(2026, 7, 1);

    private static PhotoSurvey Survey(params PhotoItem[] items) => new(PhotoRoot, [.. items], ["notes.txt"], TestLedger.Empty());

    private static PhotoExifCache NoExif() => new(new FakePhotoFileReader(FakeLayout.NewFileSystem(), FakeLayout.Context(cardRoot: null)));

    private static PhotoCleanupPlan Build(PhotoSurvey s, PhotoCleanupMode mode = PhotoCleanupMode.BeforeDate, LightroomIndex? index = null,
                                          PhotoExifCache? exif = null)
        => PhotoCleanupPlanner.Build(s, new PhotoCleanupRequest(mode, Jun30, mode == PhotoCleanupMode.Verify ? @"X:\Lightroom" : null),
                                     index, exif ?? NoExif(), null, CancellationToken.None);

    [Fact]
    public void Build_APairWithAnUndatedOrLaterTwin_IsNotEligible()
    {
        var undated = new PhotoItem("A.DNG", PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
            [Member("A.DNG", Jun1), new PhotoMember("A.JPG", 8_000_000, Mtime, FakeFileSystem.CloudOnlyPlaceholder, null, null, CaptureSource.None,
                                                    null, PhotoCleanupRules.DateUnknownCloudOnly, null)]);
        var later = new PhotoItem("B.DNG", PhotoItemKind.Photo, PhotoSetKind.Unknown, null, [Member("B.DNG", Jun30), Member("B.JPG", Jul1, 8_000_000)]);
        var plan = Build(Survey(undated, later));
        Assert.Empty(plan.Rows);
        Assert.Equal([("A.DNG", PhotoEligibility.DateUnknown, "date unknown (cloud-only)"),
                      ("B.DNG", PhotoEligibility.StraddlesCutoff, "one of its files was shot after Jun 30")],
                     plan.NotEligible.Select(r => (r.Key, r.Eligibility, r.Why)).OrderBy(t => t.Key, StringComparer.Ordinal));
    }

    [Fact] // [Review Focus 1]
    public void VerifyMode_ACloudOnlyLedgerDatedPhoto_IsNeverOpened_AndSaysCantCheck()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(PhotoRoot + @"\A.DNG", new SyntheticDngBuilder().Build(), Mtime, FakeFileSystem.CloudOnlyPlaceholder);
        var reader = new FakePhotoFileReader(fs, FakeLayout.Context(cardRoot: null));
        var cloud = new PhotoItem("A.DNG", PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
                                  [Member("A.DNG", Jun1, attributes: FakeFileSystem.CloudOnlyPlaceholder)]);     // dated from the ledger
        var plan = Build(Survey(cloud), PhotoCleanupMode.Verify, LightroomIndex.From(@"X:\Lightroom", []), new PhotoExifCache(reader));
        var row = Assert.Single(plan.Rows);
        Assert.Equal((false, PhotoCleanupRules.CloudOnlyCantCheck), (row.Verification.Verified, row.Verification.Text));
        Assert.Empty(plan.DefaultDelete());
        Assert.Empty(reader.Opened);
        Assert.Empty(fs.HydrationViolations);
    }

    [Fact]
    public void Build_DateMode_TheCutoffDayIsIncluded_EveryRowStartsDelete_LabelledDateMode()
    {
        var plan = Build(Survey(Photo("A.DNG", Jun30), Photo("B.DNG", Jun1), Photo("C.DNG", Jul1)));
        Assert.Equal(["B.DNG", "A.DNG"], plan.Rows.Select(r => r.Key));
        Assert.All(plan.Rows, r => Assert.Equal(PhotoVerification.DateMode, r.Verification));
        Assert.Equal(2, plan.DefaultDelete().Count);
        Assert.Empty(plan.NotEligible);
        Assert.Equal(["notes.txt"], plan.NotTouched);
    }

    [Fact] // [Review Focus 2]
    public void Build_ASetStraddlingTheCutoff_IsNotEligible()
    {
        var straddling = new PhotoItem("001_0042", PhotoItemKind.Set, PhotoSetKind.Hyperlapse, "001_0042",
            [Member(@"001_0042\HYPERLAPSE_0001.DNG", Jun30), Member(@"001_0042\HYPERLAPSE_0002.DNG", Jul1)]);
        var inside = Set("001_0087", Jun30, PhotoSetKind.Panorama, "PANO_0001.DNG", "PANO_0002.DNG");
        var plan = Build(Survey(straddling, inside));
        Assert.Equal(["001_0087"], plan.Rows.Select(r => r.Key));
        var row = Assert.Single(plan.NotEligible);
        Assert.Equal((PhotoEligibility.StraddlesCutoff, "part of the set was shot after Jun 30"), (row.Eligibility, row.Why));
    }

    [Fact]
    public void Build_DateUnknown_IsListedNotEligible_WithItsReason()
    {
        var cloud = new PhotoItem("A.DNG", PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
            [new PhotoMember("A.DNG", 10, Mtime, FakeFileSystem.CloudOnlyPlaceholder, null, null, CaptureSource.None, null,
                             PhotoCleanupRules.DateUnknownCloudOnly, null)]);
        var plan = Build(Survey(cloud));
        Assert.Empty(plan.Rows);
        Assert.Equal((PhotoEligibility.DateUnknown, "date unknown (cloud-only)"), (plan.NotEligible[0].Eligibility, plan.NotEligible[0].Why));
    }

    [Fact]
    public void Build_VerifyMode_VerifiedRowsStartDelete_UnverifiedKeep_AndNeedsAnIndex()
    {
        var stamp = new ExifStamp(new DateTime(2026, 6, 1, 12, 10, 0), null, "FC9113");
        var inLr = Photo("A.DNG", Jun1, stamp: stamp);
        var notInLr = Photo("B.DNG", Jun1, stamp: stamp with { Second = stamp.Second.AddSeconds(30) });
        var index = LightroomIndex.From(@"X:\Lightroom", [new LightroomPhoto(@"X:\Lightroom\a.dng", stamp)]);
        var plan = Build(Survey(inLr, notInLr), PhotoCleanupMode.Verify, index);
        Assert.Equal(["A.DNG"], plan.DefaultDelete());
        Assert.Equal("not found in Lightroom", plan.Rows.Single(r => r.Key == "B.DNG").Verification.Text);
        Assert.Throws<ArgumentException>(() => Build(Survey(inLr), PhotoCleanupMode.Verify));
    }

    [Fact]
    public void VerifyRange_IsTheFirstEligibleDateThroughTheCutoff()
    {
        Assert.Equal<(DateOnly From, DateOnly To)?>((Jun1, Jun30), PhotoCleanupPlanner.VerifyRange(Survey(Photo("A.DNG", Jun1), Photo("C.DNG", Jul1)), Jun30));
        Assert.Null(PhotoCleanupPlanner.VerifyRange(Survey(Photo("C.DNG", Jul1)), Jun30));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PhotoCleanupVerifierTests" --filter-class "*PhotoCleanupBuildTests"`
Expected: build fails (`CS0103: The name 'PhotoCleanupVerifier' does not exist in the current context`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoCleanupVerifier.cs
namespace UasSort.Core.Cleanup;

/// <summary>Verify mode (spec 2026-10-04 §4): is a Picture Offload row safely in the Lightroom library (a hyperlapse: its result video in
/// the video library)? Opens nothing: it works on stamps already read and on the ledger. Photos and frames are matched against Lightroom
/// DNGs only; a JPG in Lightroom can only confirm a stitched panorama.</summary>
public static class PhotoCleanupVerifier
{
    public static readonly TimeSpan HyperlapseWindow = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan PanoramaStitchWindow = TimeSpan.FromMinutes(2);

    /// <summary>PhotoRootMembers: every shot in Picture Offload (photo units' primaries and set frames, with or without a stamp; JPG twins
    /// excluded, they repeat their DNG) — the peers of the same-second rule. FlatPhotos: the photo units, where a stitched panorama is
    /// looked for.</summary>
    public sealed record Context(LightroomIndex Index, LedgerSnapshot Ledger, ImmutableArray<PhotoMember> PhotoRootMembers,
                                 ImmutableArray<PhotoItem> FlatPhotos);

    public static Context ContextFor(IReadOnlyList<PhotoItem> items, LightroomIndex index, LedgerSnapshot ledger)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(ledger);
        return new Context(index, ledger,
            [.. items.SelectMany(i => i.Kind == PhotoItemKind.Photo ? ImmutableArray.Create(i.Primary) : i.Members)],
            [.. items.Where(i => i.Kind == PhotoItemKind.Photo)]);
    }

    public static PhotoVerification Verify(PhotoItem item, Context ctx)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(ctx);
        if (item.Kind == PhotoItemKind.Photo)
        {
            var (ok, text) = Match(item.Primary, ctx);                          // a DNG+JPG pair: the DNG decides
            return new PhotoVerification(ok, PhotoEvidence.Lightroom, text);
        }
        var frames = item.Members.Select(m => Match(m, ctx)).ToList();
        if (frames.TrueForAll(f => f.Ok)) return new PhotoVerification(true, PhotoEvidence.Lightroom, $"every frame is in Lightroom ({frames.Count})");
        if (item.SetKind == PhotoSetKind.Hyperlapse)
        {
            var (video, why) = HyperlapseVideo(item, ctx.Ledger);
            return video is not null
                ? new PhotoVerification(true, PhotoEvidence.HyperlapseResult, $"hyperlapse result video is in the library: {PathRules.FileName(video.Dest)}")
                : new PhotoVerification(false, PhotoEvidence.HyperlapseResult, why);
        }
        if (item.SetKind == PhotoSetKind.Panorama)
        {
            var (stitched, why) = StitchedImage(item, ctx);
            return stitched is not null
                ? new PhotoVerification(true, PhotoEvidence.PanoramaStitch, $"stitched panorama {stitched.Name} is in Lightroom")
                : new PhotoVerification(false, PhotoEvidence.PanoramaStitch, FramesText(frames, why));
        }
        return new PhotoVerification(false, PhotoEvidence.Lightroom, FramesText(frames, null));
    }

    /// <summary>One shot against the index (Review Focus 3): equal sub-seconds when both have them; otherwise every Picture Offload shot of
    /// that second and Model needs its own Lightroom DNG (a burst can't be told apart without sub-seconds), and when a shot of that second
    /// couldn't be read (no stamp; found by its ledger capture time) only an exact sub-second match counts. allowJpg: the stitched
    /// panorama only (spec §4: "a Lightroom DNG/JPG").</summary>
    internal static (bool Ok, string Text) Match(PhotoMember m, Context ctx, bool allowJpg = false)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(ctx);
        if (m.Stamp is not { } stamp)
            return (false, m.IsCloudOnly ? PhotoCleanupRules.CloudOnlyCantCheck : m.DateProblem ?? "its capture time or camera model couldn't be read");
        var lr = ctx.Index.SameSecond(stamp).Where(p => allowJpg || p.IsDng).ToList();
        if (lr.Find(p => p.Stamp.BothHaveSubSec(stamp) && p.Stamp.SameShot(stamp)) is { } exact) return (true, InLightroom(ctx, exact));
        var known = ctx.PhotoRootMembers.Select(p => p.Stamp).OfType<ExifStamp>().Where(s => s.SameSecondAndModel(stamp)).ToList();
        var unknown = m.CaptureUtc is { } utc
            ? ctx.PhotoRootMembers.Count(p => p.Stamp is null && p.CaptureUtc is { } u && WholeSecond(u) == WholeSecond(utc))
            : 0;
        var loose = lr.Where(p => p.Stamp.SameShot(stamp) && !known.Exists(r => r.BothHaveSubSec(p.Stamp) && r.SameShot(p.Stamp))).ToList();
        if (loose.Count == 0)
            return (false, lr.Count == 0 ? "not found in Lightroom" : "not found in Lightroom (another shot in the same second is)");
        if (unknown > 0)
            return (false, $"{known.Count + unknown} shots in the same second, {unknown} of them couldn't be read — can't tell which");
        var peers = Math.Max(1, known.Count(r => !lr.Exists(p => p.Stamp.BothHaveSubSec(r) && p.Stamp.SameShot(r))));
        return loose.Count >= peers
            ? (true, InLightroom(ctx, loose[0]))
            : (false, $"{peers} shots in the same second; only {loose.Count} in Lightroom — can't tell which");
    }

    /// <summary>Spec §4 hyperlapse rule as written (resolved ambiguity 7): a ledger video whose session matches a member's always counts;
    /// one shot within the set's span ± 2 min counts only on the same, known drone serial.</summary>
    internal static (LedgerFile? Video, string Why) HyperlapseVideo(PhotoItem set, LedgerSnapshot ledger)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(ledger);
        var sessions = set.Members.Select(m => m.Ledger?.Session).OfType<SessionKey>().ToList();
        var times = set.Members.Select(m => m.CaptureUtc).OfType<DateTime>().ToList();
        var serial = sessions.Select(s => s.DroneSerial).FirstOrDefault(s => s is not null);
        var inWindowWithoutSerial = false;
        foreach (var v in ledger.Files.Values.Where(f => f.Root == DestRoot.Video).OrderBy(f => f.Dest, StringComparer.OrdinalIgnoreCase))
        {
            if (v.Session is { } vs && sessions.Exists(s => s.SameSession(vs))) return (v, "");
            if (times.Count == 0 || v.CaptureUtc is not { } at || at < times.Min() - HyperlapseWindow || at > times.Max() + HyperlapseWindow) continue;
            if (serial is null) inWindowWithoutSerial = true;
            else if (v.Session is { DroneSerial: { } videoSerial } && string.Equals(serial, videoSerial, StringComparison.Ordinal)) return (v, "");
        }
        return (null, inWindowWithoutSerial
            ? "a video in the library was shot then, but these frames carry no drone serial — can't tell it is this hyperlapse's result"
            : "hyperlapse result video not in the library");
    }

    /// <summary>The stitched panorama (resolved ambiguity 6; UNVERIFIED against real DJI output, acceptance step 3): the only JPG-only
    /// photo of the same Model shot within [first frame, last frame + 2 min], panorama-shaped, and in Lightroom (DNG or JPG).</summary>
    internal static (PhotoMember? Stitched, string Why) StitchedImage(PhotoItem set, Context ctx)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(ctx);
        var frames = set.Members.Select(m => m.Stamp).OfType<ExifStamp>().ToList();
        if (frames.Count == 0) return (null, "no stitched panorama can be looked for (the frames' capture times couldn't be read)");
        var first = frames.Min(s => s.Second);
        var last = frames.Max(s => s.Second);
        var near = ctx.FlatPhotos
            .Where(f => f.Members.Length == 1 && PhotoCleanupRules.IsJpg(f.Primary.Name))
            .Select(f => f.Primary)
            .Where(p => p.Stamp is { } s && string.Equals(s.Model, frames[0].Model, StringComparison.OrdinalIgnoreCase)
                        && s.Second >= first && s.Second <= last + PanoramaStitchWindow)
            .ToList();
        if (near.Count == 0) return (null, "no stitched panorama next to the frames");
        if (near.Count > 1) return (null, $"{near.Count} JPG photos next to the frames — can't tell which is the stitched panorama");
        var candidate = near[0];
        if (!PhotoCleanupRules.LooksLikePanorama(candidate.Pixels)) return (null, $"{candidate.Name} next to the frames is not panorama-shaped");
        var (ok, text) = Match(candidate, ctx, allowJpg: true);
        return ok ? (candidate, "") : (null, $"stitched panorama {candidate.Name}: {text}");
    }

    private static DateTime WholeSecond(DateTime t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, t.Kind);

    private static string InLightroom(Context ctx, LightroomPhoto p) => "in Lightroom: " + Path.GetRelativePath(ctx.Index.Folder, p.FullPath);

    private static string FramesText(List<(bool Ok, string Text)> frames, string? lead)
    {
        var text = $"{frames.Count(f => !f.Ok)} of {frames.Count} frames not confirmed: {frames.First(f => !f.Ok).Text}";
        return lead is null ? text : $"{lead}; {text}";
    }
}
```

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoCleanupPlanner.Build.cs
namespace UasSort.Core.Cleanup;

public static partial class PhotoCleanupPlanner
{
    /// <summary>The dates the Lightroom index must cover: the first eligible date through the cutoff; null when nothing is eligible.</summary>
    public static (DateOnly From, DateOnly To)? VerifyRange(PhotoSurvey survey, DateOnly cutoff)
    {
        ArgumentNullException.ThrowIfNull(survey);
        var eligible = survey.Items.Where(i => Eligibility(i, cutoff).Eligibility == PhotoEligibility.Eligible).ToList();
        return eligible.Count == 0 ? null : (eligible.Min(i => i.FirstDate!.Value), cutoff);
    }

    /// <summary>Spec 2026-10-04 §3–§4: an item is eligible when every member was shot on or before the cutoff; a set straddling it and an
    /// item with an unknown date are listed as not eligible; later items are left out. Verify mode reads the stamps it still needs
    /// (local files only) and verifies every eligible row against the index.</summary>
    public static PhotoCleanupPlan Build(PhotoSurvey survey, PhotoCleanupRequest request, LightroomIndex? index, PhotoExifCache exif,
                                         IProgress<PhotoScanProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(exif);
        if (request.Mode == PhotoCleanupMode.Verify && index is null) throw new ArgumentException("Verify mode needs a Lightroom index", nameof(index));

        var results = survey.Items.Select(i => Eligibility(i, request.Cutoff)).ToList();
        var items = survey.Items.ToList();
        PhotoCleanupVerifier.Context? verify = null;
        if (request.Mode == PhotoCleanupMode.Verify)
        {
            var eligibleFirst = items.Where((_, n) => results[n].Eligibility == PhotoEligibility.Eligible).Select(i => i.FirstDate!.Value).ToList();
            var lo = (eligibleFirst.Count == 0 ? request.Cutoff : eligibleFirst.Min()).AddDays(-1);
            var hi = request.Cutoff.AddDays(1);
            bool Needed(int n) => results[n].Eligibility == PhotoEligibility.Eligible
                                  || (items[n].Kind == PhotoItemKind.Photo && items[n].Members.Any(m => m.LocalDate is { } d && d >= lo && d <= hi));
            var toRead = Enumerable.Range(0, items.Count).Count(Needed);
            var done = 0;
            for (var n = 0; n < items.Count; n++)
            {
                ct.ThrowIfCancellationRequested();
                if (!Needed(n)) continue;
                items[n] = Enrich(survey.PhotoRoot, items[n], exif);
                progress?.Report(new PhotoScanProgress("Reading photo details", ++done, toRead));
            }
            verify = PhotoCleanupVerifier.ContextFor(items, index!, survey.Ledger);
        }

        var rows = new List<PhotoRow>();
        var notEligible = new List<PhotoRow>();
        for (var n = 0; n < items.Count; n++)
        {
            var (eligibility, why) = results[n];
            switch (eligibility)
            {
                case PhotoEligibility.Eligible:
                    rows.Add(new PhotoRow(items[n], eligibility,
                                          verify is null ? PhotoVerification.DateMode : PhotoCleanupVerifier.Verify(items[n], verify), null));
                    break;
                case PhotoEligibility.DateUnknown or PhotoEligibility.StraddlesCutoff:
                    notEligible.Add(new PhotoRow(items[n], eligibility, PhotoVerification.DateMode, why));
                    break;
            }
        }
        return new PhotoCleanupPlan(Guid.NewGuid().ToString("N"), survey.PhotoRoot, request, [.. Order(rows)], [.. Order(notEligible)], survey.NotTouched);
    }

    internal static (PhotoEligibility Eligibility, string? Why) Eligibility(PhotoItem item, DateOnly cutoff)
    {
        if (!item.DateKnown) return (PhotoEligibility.DateUnknown, item.Members.First(m => m.LocalDate is null).DateProblem ?? "date unknown");
        if (item.LastDate!.Value <= cutoff) return (PhotoEligibility.Eligible, null);
        if (item.FirstDate!.Value <= cutoff)
            return (PhotoEligibility.StraddlesCutoff, item.Kind == PhotoItemKind.Set
                ? $"part of the set was shot after {CleanupFormat.MonthDay(cutoff)}"
                : $"one of its files was shot after {CleanupFormat.MonthDay(cutoff)}");     // a DNG+JPG pair is one unit: never split
        return (PhotoEligibility.AfterCutoff, null);
    }

    /// <summary>Reads the stamp (and pixel size, for the stitched-panorama test) of local members that don't have one yet; never a
    /// cloud-only member (Review Focus 1).</summary>
    private static PhotoItem Enrich(string root, PhotoItem item, PhotoExifCache exif)
        => item with
        {
            Members = [.. item.Members.Select(m =>
            {
                if (m.Stamp is not null || m.IsCloudOnly) return m;
                var read = exif.Read(PhotoCleanupPaths.Full(root, m.RelPath), m.Size, m.MtimeUtc, m.Attributes);
                return m with { Stamp = read.Stamp, Pixels = PhotoCleanupRules.PixelsOf(read.Info) };
            })],
        };

    private static IEnumerable<PhotoRow> Order(IEnumerable<PhotoRow> rows)
        => rows.OrderBy(r => r.Item.FirstDate ?? DateOnly.MaxValue).ThenBy(r => r.Key, StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PhotoCleanupVerifierTests" --filter-class "*PhotoCleanupBuildTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/Photos/PhotoCleanupVerifier.cs src/UasSort.Core/Cleanup/Photos/PhotoCleanupPlanner.Build.cs tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupVerifierTests.cs tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupBuildTests.cs
git commit -F - <<'EOF'
feat: build Picture Offload plans by date or verified against Lightroom and the ledger

<session trailer>
EOF
```

---
### Task P.10: Executor and report

**Files:**
- Create: `src/UasSort.Core/Cleanup/Photos/PhotoCleanupRun.cs`
- Create: `src/UasSort.Core/Cleanup/Photos/PhotoCleanupExecutor.cs`
- Create: `src/UasSort.Core/Cleanup/Photos/PhotoCleanupReport.cs`
- Modify: `src/UasSort.Core/Json/CoreJsonContext.cs` (attribute list)
- Modify: `src/UasSort.Core/Ports/Ports.cs` (`IReportStore`)
- Modify: `src/UasSort.Platform/Stores/ReportStore.cs` (new `Save` overload)
- Modify: `tests/UasSort.Testing/OffloadFakes.cs` (`MemReportStore`)
- Test: `tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupExecutorTests.cs`, `tests/UasSort.Platform.Tests/PhotoReportStoreTests.cs`

**Interfaces:**
- Consumes: `ConfirmedPhotoCleanupPlan` (P.5), `IPhotoRootRecyclerFactory`/`IPhotoRootRecycler`/`RecycleResult` (P.6), `PhotoDeleteRecord`/`PhotoDeleteRecords` (P.3), `ILedgerStore`, `ILedgerWriter`, `IOffloadLock`, `IPowerRequest`, `IDirectoryLister`, `LedgerCodec.Version`, fakes `FakeLedgerStore`, `FakeOffloadLock`, `FakePowerRequest`, `ListProgress<T>`, `FakePhotoRootRecyclerFactory` (existing / P.6).
- Produces:
  - `public enum PhotoCleanupStop { OffloadLockHeld, LedgerUnavailable, RecyclerRefused, Cancelled, LedgerWriteFailed, InternalSafetyStop }`
  - `public closed record class PhotoCleanupOutcome(string Item)` with `PhotoRecycled(string Item, int Files, long Bytes)`, `PhotoSkippedChanged(string Item, string Why)`, `PhotoPartlyRecycled(string Item, ImmutableArray<string> Recycled, ImmutableArray<string> Left, string Why)`, `PhotoRecycleFailed(string Item, string Path, int Code, string Error, bool NotRecyclable)`, `PhotoNotStarted(string Item)`
  - `public sealed record PhotoCleanupProgress(int ItemsDone, int ItemsTotal, long BytesDone, long BytesTotal, string? Current)`
  - `public sealed record PhotoCleanupEnvironment(IPhotoRootRecyclerFactory Recyclers, IDirectoryLister Lister, ILedgerStore Ledger, IOffloadLock Lock, IPowerRequest Power, TimeProvider Clock, string Machine)`
  - `public sealed record PhotoCleanupResult(string RunId, ConfirmedPhotoCleanupPlan Plan, ImmutableArray<PhotoCleanupOutcome> Outcomes, PhotoCleanupStop? Stop, ImmutableArray<string> Unrecorded, DateTime StartUtc, DateTime EndUtc)`
  - `public static class PhotoCleanupExecutor { static Task<PhotoCleanupResult> RunAsync(ConfirmedPhotoCleanupPlan confirmed, PhotoCleanupEnvironment env, IProgress<PhotoCleanupProgress> progress, CancellationToken ct); }`
  - `public sealed record PhotoCleanupReportLine(string Item, string Kind, int Files, long Bytes, string Decision, bool Verified, string Verification, string Evidence, string Outcome, string? Error, bool LedgerRecorded)`; `public sealed record PhotoCleanupReport(int V, string RunId, string PhotoRoot, PhotoCleanupMode Mode, DateOnly Cutoff, string? LightroomFolder, ImmutableArray<PhotoCleanupReportLine> Items, ImmutableArray<string> NotEligible, ImmutableArray<string> NotTouched, PhotoCleanupStop? Stop, ImmutableArray<string> Unrecorded, DateTime StartUtc, DateTime EndUtc)`; `public static class PhotoCleanupReports { static PhotoCleanupReport Build(PhotoCleanupResult result); }`
  - `IReportStore.Save(PhotoCleanupReport r)` → `%LOCALAPPDATA%\uas-sort\reports\yyyyMMdd-HHmmss-<run8>-photos.json`; `MemReportStore.Photos` (List).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupExecutorTests.cs
using System.Text.Json;
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class PhotoCleanupExecutorTests
{
    private static readonly DateOnly D = new(2026, 6, 1);

    private sealed class Rig
    {
        public Rig()
        {
            Ledger = new FakeLedgerStore(Fs, FakeLayout.VideoRoot, FakeLayout.Machine);
            Recyclers = new FakePhotoRootRecyclerFactory(Fs, FakeLayout.Context(cardRoot: null));
        }

        public FakeFileSystem Fs { get; } = FakeLayout.NewFileSystem();
        public FakeLedgerStore Ledger { get; }
        public FakePhotoRootRecyclerFactory Recyclers { get; }
        public FakeOffloadLock Lock { get; } = new();
        public FakePowerRequest Power { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 4, 20, 0, 0, TimeSpan.Zero));
        public ListProgress<PhotoCleanupProgress> Progress { get; } = new();
        public List<PhotoDeleteRecord> Records => [.. Ledger.Writer.Records.OfType<PhotoDeleteRecord>()];

        public PhotoMember File(string rel, long size)
        {
            Fs.AddFile(PhotoRoot + @"\" + rel, size, Mtime);
            return Member(rel, D, size);
        }

        public PhotoRow PhotoRow(string name, string? twin = null, bool verified = true)
            => Row(new PhotoItem(name, PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
                                  twin is null ? [File(name, 1_000)] : [File(name, 1_000), File(twin, 400)]), verified);

        public PhotoRow SetRow(string folder, params string[] members)
            => Row(new PhotoItem(folder, PhotoItemKind.Set, PhotoSetKind.Panorama, PhotoCleanupRules.SetNameOf(folder),
                                  [.. members.Select((m, i) => File(folder + "\\" + m, 2_000 + i))]), evidence: PhotoEvidence.PanoramaStitch);

        public Task<PhotoCleanupResult> Run(ConfirmedPhotoCleanupPlan plan, CancellationToken? ct = null /* null = the test's own token (xUnit1051) */)
            => PhotoCleanupExecutor.RunAsync(plan, new PhotoCleanupEnvironment(Recyclers, Fs, Ledger, Lock, Power, Clock, FakeLayout.Machine),
                                             Progress, ct ?? TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Run_RecyclesEachChosenItem_AndRecordsEveryFileRightAfterItsMove()
    {
        var rig = new Rig();
        var pair = rig.PhotoRow("A.DNG", "A.JPG");
        var pano = rig.SetRow("001_0087", "PANO_0001.DNG", "PANO_0002.DNG");
        var recordsAtEachMove = new List<int>();
        rig.Recyclers.BeforeRecycle = _ => recordsAtEachMove.Add(rig.Records.Count);

        var result = await rig.Run(Confirmed(rig.Clock, PhotoCleanupMode.Verify, [pair, pano]));

        Assert.Null(result.Stop);
        Assert.All(result.Outcomes, o => Assert.IsType<PhotoRecycled>(o));
        Assert.Equal([PhotoRoot + @"\A.JPG", PhotoRoot + @"\A.DNG", PhotoRoot + @"\001_0087"], rig.Recyclers.Recycled);
        Assert.Equal([0, 1, 2], recordsAtEachMove);
        Assert.False(rig.Fs.Exists(PhotoRoot + @"\A.DNG"));
        var records = rig.Records;
        Assert.Equal(["A.JPG", "A.DNG", "PANO_0001.DNG", "PANO_0002.DNG"], records.Select(r => r.Name));
        Assert.Equal((PhotoRoot + @"\001_0087\PANO_0001.DNG", "001_0087", "panoramaStitch", "verify", new DateOnly(2026, 6, 30)),
                     (records[2].Dest, records[2].Set, records[2].Evidence, records[2].Mode, records[2].Cutoff));
        Assert.Equal(((string?)null, "lightroom", result.RunId), (records[0].Set, records[0].Evidence, records[0].Run));
        Assert.True(rig.Ledger.Calls.IndexOf("SnapshotToBackup " + result.RunId) is >= 0 and var snap && snap < rig.Ledger.Calls.IndexOf("OpenOwn"));
        Assert.Equal((0, 0, 1), (rig.Lock.Holds, rig.Power.Active, rig.Recyclers.Disposed));
        Assert.Equal(new PhotoCleanupProgress(2, 2, pair.Item.Bytes + pano.Item.Bytes, pair.Item.Bytes + pano.Item.Bytes, null), rig.Progress.Items[^1]);
    }

    [Fact]
    public async Task Run_RowsLeftOnKeep_AreNotTouched()
    {
        var rig = new Rig();
        var a = rig.PhotoRow("A.DNG");
        var b = rig.PhotoRow("B.DNG", verified: false);
        var plan = Plan(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), [a, b]);
        var result = await rig.Run(plan.Confirm(new PhotoCleanupAck(plan.Fingerprint, [a.Key], true, false), rig.Clock));
        Assert.Single(result.Outcomes);
        Assert.True(rig.Fs.Exists(PhotoRoot + @"\B.DNG"));
        Assert.Equal(["A.DNG"], rig.Records.Select(r => r.Name));
    }

    [Fact] // [Review Focus 4]
    public async Task Run_ItemsChangedSinceReview_AreSkipped()
    {
        var rig = new Rig();
        var resized = rig.PhotoRow("A.DNG");
        var gone = rig.PhotoRow("B.DNG");
        var grown = rig.SetRow("001_0087", "PANO_0001.DNG");
        var touched = rig.SetRow("001_0088", "PANO_0001.DNG");
        var plan = Confirmed(rig.Clock, PhotoCleanupMode.BeforeDate, [resized, gone, grown, touched]);
        rig.Fs.Touch(PhotoRoot + @"\A.DNG", size: 999);
        rig.Fs.RemoveUnguarded(PhotoRoot + @"\B.DNG");
        rig.Fs.AddFile(PhotoRoot + @"\001_0087\PANO_0002.DNG", 5, Mtime);
        rig.Fs.Touch(PhotoRoot + @"\001_0088\PANO_0001.DNG", mtimeUtc: Mtime.AddSeconds(1));

        var result = await rig.Run(plan);

        Assert.Equal(["A.DNG changed since review", "B.DNG is no longer in Picture Offload",
                      "001_0087 changed since review (2 files now, 1 at review)", "PANO_0001.DNG in 001_0088 changed since review"],
                     result.Outcomes.Select(o => Assert.IsType<PhotoSkippedChanged>(o).Why));
        Assert.Empty(rig.Recyclers.Recycled);
        Assert.Empty(rig.Records);
        Assert.True(rig.Fs.Exists(PhotoRoot + @"\A.DNG"));
    }

    [Fact] // [Review Focus 4]
    public async Task Run_ASetFolderThatNowHoldsAFolder_OrCantBeListedAgain_IsSkipped()
    {
        var rig = new Rig();
        var nested = rig.SetRow("001_0087", "PANO_0001.DNG");
        var unlisted = rig.SetRow("001_0088", "PANO_0001.DNG");
        var plan = Confirmed(rig.Clock, PhotoCleanupMode.BeforeDate, [nested, unlisted]);
        rig.Fs.AddDirectory(PhotoRoot + @"\001_0087\edits");
        rig.Fs.Faults.EnumerationErrors[PathRules.Normalize(PhotoRoot + @"\001_0088")] = 5;

        var result = await rig.Run(plan);

        Assert.Equal(["001_0087 changed since review (it now holds a folder)", "001_0088 couldn't be listed again"],
                     result.Outcomes.Select(o => Assert.IsType<PhotoSkippedChanged>(o).Why));
        Assert.Empty(rig.Recyclers.Recycled);
        Assert.Empty(rig.Records);
    }

    [Fact]
    public async Task Run_ASetFolderTheShellMovedOnlyPartly_RecordsAndReportsTheMembersAlreadyGone()
    {
        var rig = new Rig();
        var pano = rig.SetRow("001_0087", "PANO_0001.DNG", "PANO_0002.DNG");
        rig.Recyclers.Fails.Add(PhotoRoot + @"\001_0087");                                    // IFileOperation fails part-way …
        rig.Recyclers.BeforeRecycle = _ => rig.Fs.RemoveUnguarded(PhotoRoot + @"\001_0087\PANO_0001.DNG");   // … after moving one member

        var result = await rig.Run(Confirmed(rig.Clock, PhotoCleanupMode.BeforeDate, [pano]));

        var partly = Assert.IsType<PhotoPartlyRecycled>(Assert.Single(result.Outcomes));
        Assert.Equal([@"001_0087\PANO_0001.DNG"], partly.Recycled);
        Assert.Equal([@"001_0087\PANO_0002.DNG"], partly.Left);
        Assert.Equal(["PANO_0001.DNG"], rig.Records.Select(r => r.Name));
        Assert.Equal("001_0087", rig.Records[0].Set);
        Assert.Null(result.Stop);
    }

    [Fact]
    public async Task Run_AnUnexpectedRecyclerException_EndsInAResult_NotAFault()
    {
        var rig = new Rig();
        var a = rig.PhotoRow("A.DNG");
        var b = rig.PhotoRow("B.DNG");
        rig.Recyclers.BeforeRecycle = p =>
        {
            if (p.EndsWith(@"\A.DNG", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("IFileOperation: no object");
        };

        var result = await rig.Run(Confirmed(rig.Clock, PhotoCleanupMode.BeforeDate, [a, b]));

        Assert.Equal("IFileOperation: no object", Assert.IsType<PhotoRecycleFailed>(result.Outcomes[0]).Error);
        Assert.IsType<PhotoRecycled>(result.Outcomes[1]);
        Assert.True(rig.Fs.Exists(PhotoRoot + @"\A.DNG"));
        Assert.Equal(["B.DNG"], rig.Records.Select(r => r.Name));
    }

    [Fact] // [Review Focus 5]
    public async Task Run_AnItemTheRecycleBinCantTake_IsKept_AndNotRecorded()
    {
        var rig = new Rig();
        var cloud = rig.PhotoRow("C.DNG");
        var pair = rig.PhotoRow("A.DNG", "A.JPG");
        var fine = rig.PhotoRow("F.DNG");
        rig.Recyclers.NotRecyclable.Add(PhotoRoot + @"\C.DNG");
        rig.Recyclers.NotRecyclable.Add(PhotoRoot + @"\A.DNG");

        var result = await rig.Run(Confirmed(rig.Clock, PhotoCleanupMode.BeforeDate, [cloud, pair, fine]));

        Assert.True(Assert.IsType<PhotoRecycleFailed>(result.Outcomes[0]).NotRecyclable);
        Assert.True(rig.Fs.Exists(PhotoRoot + @"\C.DNG"));
        var partly = Assert.IsType<PhotoPartlyRecycled>(result.Outcomes[1]);
        Assert.Equal(["A.JPG"], partly.Recycled);
        Assert.Equal(["A.DNG"], partly.Left);
        Assert.True(rig.Fs.Exists(PhotoRoot + @"\A.DNG"));
        Assert.IsType<PhotoRecycled>(result.Outcomes[2]);
        Assert.Equal(["A.JPG", "F.DNG"], rig.Records.Select(r => r.Name));
        Assert.Null(result.Stop);
    }

    [Fact]
    public async Task Run_AFailedLedgerAppend_StopsAndNamesTheUnrecordedFile()
    {
        var rig = new Rig();
        var pair = rig.PhotoRow("A.DNG", "A.JPG");
        var next = rig.PhotoRow("B.DNG");
        rig.Ledger.Writer.ThrowWhen = r => r is PhotoDeleteRecord { Name: "A.JPG" };

        var result = await rig.Run(Confirmed(rig.Clock, PhotoCleanupMode.BeforeDate, [pair, next]));

        Assert.Equal(PhotoCleanupStop.LedgerWriteFailed, result.Stop);
        Assert.Equal([PhotoRoot + @"\A.JPG"], result.Unrecorded);
        Assert.Equal(["A.DNG"], Assert.IsType<PhotoPartlyRecycled>(result.Outcomes[0]).Left);
        Assert.True(rig.Fs.Exists(PhotoRoot + @"\A.DNG"));
        Assert.IsType<PhotoNotStarted>(result.Outcomes[1]);
    }

    [Fact]
    public async Task Run_WithTheOffloadLockHeld_OrARefusingRecycler_OrACloudOnlyLedger_MovesNothing()
    {
        var locked = new Rig();
        var a = locked.PhotoRow("A.DNG");
        locked.Lock.HeldElsewhere = true;
        var r1 = await locked.Run(Confirmed(locked.Clock, PhotoCleanupMode.BeforeDate, [a]));
        Assert.Equal(PhotoCleanupStop.OffloadLockHeld, r1.Stop);
        Assert.IsType<PhotoNotStarted>(Assert.Single(r1.Outcomes));
        Assert.DoesNotContain("OpenOwn", locked.Ledger.Calls);

        var refused = new Rig();
        var b = refused.PhotoRow("B.DNG");
        refused.Recyclers.OpenThrows = true;
        var r2 = await refused.Run(Confirmed(refused.Clock, PhotoCleanupMode.BeforeDate, [b]));
        Assert.Equal(PhotoCleanupStop.RecyclerRefused, r2.Stop);
        Assert.True(refused.Fs.Exists(PhotoRoot + @"\B.DNG"));

        var cloud = new Rig();
        var c = cloud.PhotoRow("C.DNG");
        cloud.Ledger.StatusOverride = new LedgerFolderStatus(LedgerPaths.For(FakeLayout.VideoRoot), LedgerFolderState.CloudOnly, true, true, false,
                                                             true, [], [], []);
        var r3 = await cloud.Run(Confirmed(cloud.Clock, PhotoCleanupMode.BeforeDate, [c]));
        Assert.Equal(PhotoCleanupStop.LedgerUnavailable, r3.Stop);
        Assert.Empty(cloud.Recyclers.Recycled);
    }

    [Fact]
    public async Task Run_Cancelled_StopsAfterTheCurrentItem()
    {
        var rig = new Rig();
        var a = rig.PhotoRow("A.DNG");
        var b = rig.PhotoRow("B.DNG");
        using var cts = new CancellationTokenSource();
        rig.Recyclers.BeforeRecycle = _ => cts.Cancel();
        var result = await rig.Run(Confirmed(rig.Clock, PhotoCleanupMode.BeforeDate, [a, b]), cts.Token);
        Assert.Equal(PhotoCleanupStop.Cancelled, result.Stop);
        Assert.IsType<PhotoRecycled>(result.Outcomes[0]);
        Assert.IsType<PhotoNotStarted>(result.Outcomes[1]);
    }

    [Fact]
    public async Task Report_ListsEveryRowWithItsDecisionEvidenceAndOutcome()
    {
        var rig = new Rig();
        var a = rig.PhotoRow("A.DNG");
        var b = rig.PhotoRow("B.DNG", verified: false);
        var plan = Plan(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), [a, b], notTouched: ["notes.txt"]);
        var result = await rig.Run(plan.Confirm(new PhotoCleanupAck(plan.Fingerprint, [a.Key], true, false), rig.Clock));

        var report = PhotoCleanupReports.Build(result);

        Assert.Equal(("A.DNG", "delete", "lightroom", "moved to the Recycle Bin", true),
                     (report.Items[0].Item, report.Items[0].Decision, report.Items[0].Evidence, report.Items[0].Outcome, report.Items[0].LedgerRecorded));
        Assert.Equal(("B.DNG", "keep", "kept", false), (report.Items[1].Item, report.Items[1].Decision, report.Items[1].Outcome, report.Items[1].LedgerRecorded));
        Assert.Equal(["notes.txt"], report.NotTouched);
        var json = JsonSerializer.Serialize(report, CoreJsonContext.Default.PhotoCleanupReport);
        Assert.Contains("\"mode\": \"Verify\"", json, StringComparison.Ordinal);
        Assert.Contains("\"outcome\": \"moved to the Recycle Bin\"", json, StringComparison.Ordinal);
    }
}
```

```csharp
// tests/UasSort.Platform.Tests/PhotoReportStoreTests.cs
using System.Text.Json;

namespace UasSort.Platform.Tests;

public sealed class PhotoReportStoreTests
{
    [Fact]
    public void ReportStore_SavesAPhotoCleanupReport_AsTimestampRun8Photos()
    {
        using var env = new TestEnv();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 20, 15, 30, TimeSpan.Zero));
        var at = clock.GetUtcNow().UtcDateTime;
        var report = new PhotoCleanupReport(1, "ab12cd34ef56", env.PhotoRoot, PhotoCleanupMode.BeforeDate, new DateOnly(2026, 6, 30), null,
                                            [], [], [], null, [], at, at);
        var path = new ReportStore(env.AppData, TestEnv.Machine, env.Facts, clock).Save(report);
        Assert.Equal("20261004-201530-ab12cd34-photos.json", Path.GetFileName(path));
        Assert.Equal("ab12cd34ef56", JsonSerializer.Deserialize(File.ReadAllText(path), CoreJsonContext.Default.PhotoCleanupReport)!.RunId);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PhotoCleanupExecutorTests"`
Expected: build fails (`CS0246: … 'PhotoCleanupProgress' could not be found`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoCleanupRun.cs
namespace UasSort.Core.Cleanup;

public enum PhotoCleanupStop { OffloadLockHeld, LedgerUnavailable, RecyclerRefused, Cancelled, LedgerWriteFailed, InternalSafetyStop }

/// <summary>Outcome per row set to Delete (spec 2026-10-04 §2 result page: moved / skipped-because-changed / failed).</summary>
public closed record class PhotoCleanupOutcome(string Item);
public sealed record class PhotoRecycled(string Item, int Files, long Bytes) : PhotoCleanupOutcome(Item);
public sealed record class PhotoSkippedChanged(string Item, string Why) : PhotoCleanupOutcome(Item);
public sealed record class PhotoPartlyRecycled(string Item, ImmutableArray<string> Recycled, ImmutableArray<string> Left, string Why) : PhotoCleanupOutcome(Item);
public sealed record class PhotoRecycleFailed(string Item, string Path, int Code, string Error, bool NotRecyclable) : PhotoCleanupOutcome(Item);
public sealed record class PhotoNotStarted(string Item) : PhotoCleanupOutcome(Item);

public sealed record PhotoCleanupProgress(int ItemsDone, int ItemsTotal, long BytesDone, long BytesTotal, string? Current);

public sealed record PhotoCleanupEnvironment(IPhotoRootRecyclerFactory Recyclers, IDirectoryLister Lister, ILedgerStore Ledger, IOffloadLock Lock,
                                             IPowerRequest Power, TimeProvider Clock, string Machine);

public sealed record PhotoCleanupResult(string RunId, ConfirmedPhotoCleanupPlan Plan, ImmutableArray<PhotoCleanupOutcome> Outcomes,
                                        PhotoCleanupStop? Stop /* null = ran to the end */,
                                        ImmutableArray<string> Unrecorded /* moved, but the photoDelete record couldn't be written */,
                                        DateTime StartUtc, DateTime EndUtc);
```

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoCleanupExecutor.cs
namespace UasSort.Core.Cleanup;

/// <summary>Runs a confirmed Picture Offload plan (spec 2026-10-04 §5–§6): offload lock, keep-awake, ledger preparation (snapshot before
/// the first append), the recycler (its factory re-checks the photo root), then per row: re-check against the review, move to the
/// Recycle Bin, and one photoDelete record per file right after its move. Cancellable between rows.</summary>
public static class PhotoCleanupExecutor
{
    private static readonly IReadOnlySet<string> NoExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static Task<PhotoCleanupResult> RunAsync(ConfirmedPhotoCleanupPlan confirmed, PhotoCleanupEnvironment env,
                                                    IProgress<PhotoCleanupProgress> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(progress);
        return Task.Run(() => new Run(confirmed, env, progress, ct).Execute(), CancellationToken.None);
    }

    private sealed class Run
    {
        private readonly ConfirmedPhotoCleanupPlan _confirmed;
        private readonly PhotoCleanupEnvironment _env;
        private readonly IProgress<PhotoCleanupProgress> _progress;
        private readonly CancellationToken _ct;
        private readonly ImmutableArray<PhotoRow> _items;
        private readonly PhotoCleanupOutcome?[] _outcomes;
        private readonly List<string> _unrecorded = [];
        private readonly string _runId = Guid.NewGuid().ToString("N");
        private readonly DateTime _start;
        private readonly long _bytesTotal;
        private PhotoCleanupStop? _stop;
        private ILedgerWriter? _writer;
        private IPhotoRootRecycler? _recycler;
        private int _itemsDone;
        private long _bytesDone;
        private DateTime _lastReport = DateTime.MinValue;

        public Run(ConfirmedPhotoCleanupPlan confirmed, PhotoCleanupEnvironment env, IProgress<PhotoCleanupProgress> progress, CancellationToken ct)
        {
            _confirmed = confirmed;
            _env = env;
            _progress = progress;
            _ct = ct;
            _items = confirmed.Items;
            _outcomes = new PhotoCleanupOutcome?[_items.Length];
            _start = env.Clock.GetUtcNow().UtcDateTime;
            _bytesTotal = _items.Sum(r => r.Item.Bytes);
        }

        private PhotoCleanupPlan Plan => _confirmed.Plan;

        public PhotoCleanupResult Execute()
        {
            var held = new Stack<IDisposable>();
            var lk = _env.Lock.TryAcquire();
            if (lk is null)
            {
                _stop = PhotoCleanupStop.OffloadLockHeld;
                return Complete();
            }
            held.Push(lk);
            try
            {
                held.Push(_env.Power.KeepSystemAwake("Cleaning up Picture Offload"));
                if (!PrepareLedger(held)) return Complete();
                if (!OpenRecycler(held)) return Complete();
                for (var i = 0; i < _items.Length && _stop is null; i++)
                {
                    if (_ct.IsCancellationRequested)
                    {
                        _stop = PhotoCleanupStop.Cancelled;
                        break;
                    }
                    _outcomes[i] = One(_items[i]);
                    _itemsDone++;
                    Report(null, force: false);
                }
                return Complete();
            }
            finally
            {
                while (held.Count > 0) held.Pop().Dispose();
            }
        }

        private bool PrepareLedger(Stack<IDisposable> held)
        {
            var status = _env.Ledger.Check();
            if (status.State is LedgerFolderState.CloudOnly or LedgerFolderState.Unwritable or LedgerFolderState.VideoRootMissing
                             or LedgerFolderState.Unlistable)
            {
                _stop = PhotoCleanupStop.LedgerUnavailable;
                return false;
            }
            try
            {
                _env.Ledger.EnsureFolder();
                _env.Ledger.SnapshotToBackup(_runId);                     // spec §6 Snapshot: before the first append, as for the offload
                _writer = _env.Ledger.OpenOwn();
                held.Push(_writer);
                return true;
            }
            catch (UnsafeIoException) { _stop = PhotoCleanupStop.InternalSafetyStop; return false; }
            catch (IOException) { _stop = PhotoCleanupStop.LedgerUnavailable; return false; }
            catch (UnauthorizedAccessException) { _stop = PhotoCleanupStop.LedgerUnavailable; return false; }
        }

        private bool OpenRecycler(Stack<IDisposable> held)
        {
            try
            {
                _recycler = _env.Recyclers.Open(_confirmed);
                held.Push(_recycler);
                return true;
            }
            catch (UnsafeIoException) { _stop = PhotoCleanupStop.RecyclerRefused; return false; }
            catch (IOException) { _stop = PhotoCleanupStop.RecyclerRefused; return false; }
        }

        private PhotoCleanupOutcome One(PhotoRow row)
        {
            var item = row.Item;
            Report(item.RelPath, force: true);
            if (Changed(item) is { } why) return new PhotoSkippedChanged(item.RelPath, why);
            return item.Kind == PhotoItemKind.Set ? RecycleSet(row) : RecyclePhoto(row);
        }

        /// <summary>The JPG twin first, the DNG (the unit's proof) last; a record right after each file's move.</summary>
        private PhotoCleanupOutcome RecyclePhoto(PhotoRow row)
        {
            var item = row.Item;
            var done = new List<string>();
            for (var k = item.Members.Length - 1; k >= 0; k--)
            {
                var m = item.Members[k];
                var result = Recycle(Full(m.RelPath));
                if (result is RecycleError e)
                    return done.Count == 0 ? new PhotoRecycleFailed(item.RelPath, m.RelPath, e.Code, e.Message, e.NotRecyclable)
                                           : Partly(item, done, e.Message);
                done.Add(m.RelPath);
                _bytesDone += m.Size;
                if (!Append(row, m))
                    return done.Count == item.Members.Length ? Recycled(item) : Partly(item, done, "the history couldn't be written; stopped");
            }
            return Recycled(item);
        }

        /// <summary>A set folder moves as one unit; then one record per member. When the shell fails part-way through the folder, the
        /// members already gone are recorded and reported like a partly moved photo (spec §6: never silent).</summary>
        private PhotoCleanupOutcome RecycleSet(PhotoRow row)
        {
            var item = row.Item;
            var result = Recycle(Full(item.RelPath));
            if (result is RecycleError e)
            {
                var gone = new List<string>();
                var recording = true;
                foreach (var m in item.Members)
                {
                    if (!Gone(Full(m.RelPath))) continue;
                    gone.Add(m.RelPath);
                    _bytesDone += m.Size;
                    if (!recording) _unrecorded.Add(Full(m.RelPath));            // after a failed append: named as unrecorded, never silent
                    else if (!Append(row, m)) recording = false;
                }
                return gone.Count == 0 ? new PhotoRecycleFailed(item.RelPath, item.RelPath, e.Code, e.Message, e.NotRecyclable)
                                       : Partly(item, gone, e.Message);
            }
            _bytesDone += item.Bytes;
            for (var k = 0; k < item.Members.Length; k++)
            {
                if (Append(row, item.Members[k])) continue;
                for (var rest = k + 1; rest < item.Members.Length; rest++) _unrecorded.Add(Full(item.Members[rest].RelPath));
                break;
            }
            return Recycled(item);
        }

        private RecycleResult Recycle(string fullPath)
        {
            try
            {
                return _recycler!.Recycle(fullPath);
            }
            catch (UnsafeIoException e)
            {
                _stop = PhotoCleanupStop.InternalSafetyStop;
                return new RecycleError(-1, "refused by the safety guard: " + e.Message, false);
            }
            catch (IOException e)
            {
                return new RecycleError(e.HResult, e.Message, false);
            }
#pragma warning disable CA1031 // any other recycler failure (a COM wrapper's exception rethrown by the STA worker) must end in a result and a report, never a faulted run
            catch (Exception e)
            {
                return new RecycleError(e.HResult, e.Message, false);
            }
#pragma warning restore CA1031
        }

        /// <summary>Whether a member is no longer at its path (attributes only); unknown (a failed stat) counts as still there.</summary>
        private bool Gone(string fullPath)
        {
            try
            {
                return _recycler!.Stat(fullPath) is null;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private bool Append(PhotoRow row, PhotoMember m)
        {
            var full = Full(m.RelPath);
            try
            {
                _writer!.Append(new PhotoDeleteRecord(LedgerCodec.Version, Guid.NewGuid().ToString("N"), _env.Machine, _runId,
                    _env.Clock.GetUtcNow().UtcDateTime, m.Name, m.Size, full, m.CaptureUtc,
                    row.Item.Kind == PhotoItemKind.Set ? row.Item.SetName : null,
                    PhotoDeleteRecords.Evidence(_confirmed.EvidenceOf(row)), PhotoDeleteRecords.Mode(Plan.Request.Mode), Plan.Request.Cutoff));
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _stop = PhotoCleanupStop.LedgerWriteFailed;
                _unrecorded.Add(full);
                return false;
            }
        }

        /// <summary>Spec §5: the item still exists with the size and mtime seen at review (a set: the same member list, sizes and mtimes, and
        /// no subfolder). Attributes and listings only; nothing is opened.</summary>
        private string? Changed(PhotoItem item)
        {
            try
            {
                if (item.Kind == PhotoItemKind.Photo)
                {
                    foreach (var m in item.Members)
                    {
                        var st = _recycler!.Stat(Full(m.RelPath));
                        if (st is null) return $"{m.Name} is no longer in Picture Offload";
                        if (st.IsDirectory || st.Size != m.Size || st.MtimeUtc != m.MtimeUtc) return $"{m.Name} changed since review";
                    }
                    return null;
                }
                var folder = Full(item.RelPath);
                var dir = _recycler!.Stat(folder);
                if (dir is null) return $"{item.RelPath} is no longer in Picture Offload";
                if (!dir.IsDirectory) return $"{item.RelPath} changed since review";
                var listing = _env.Lister.Enumerate(folder, recurse: true, NoExcludes);
                if (!listing.Errors.IsEmpty) return $"{item.RelPath} couldn't be listed again";
                if (listing.Entries.Any(e => e.IsDirectory)) return $"{item.RelPath} changed since review (it now holds a folder)";
                var now = listing.Entries.ToDictionary(e => PathRules.FileName(e.FullPath), StringComparer.OrdinalIgnoreCase);
                if (now.Count != item.Members.Length)
                    return $"{item.RelPath} changed since review ({now.Count} files now, {item.Members.Length} at review)";
                foreach (var m in item.Members)
                    if (!now.TryGetValue(m.Name, out var e) || e.Size != m.Size || e.MtimeUtc != m.MtimeUtc)
                        return $"{m.Name} in {item.RelPath} changed since review";
                return null;
            }
            catch (IOException e)
            {
                return "it couldn't be checked again: " + e.Message;
            }
        }

        private string Full(string relPath) => PhotoCleanupPaths.Full(Plan.PhotoRoot, relPath);

        private static PhotoRecycled Recycled(PhotoItem item) => new(item.RelPath, item.Members.Length, item.Bytes);

        private static PhotoPartlyRecycled Partly(PhotoItem item, List<string> done, string why)
            => new(item.RelPath, [.. done],
                   [.. item.Members.Select(m => m.RelPath).Where(r => !done.Contains(r, StringComparer.OrdinalIgnoreCase))], why);

        private void Report(string? current, bool force)
        {
            var now = _env.Clock.GetUtcNow().UtcDateTime;
            if (!force && now - _lastReport < TimeSpan.FromMilliseconds(100)) return;
            _lastReport = now;
            _progress.Report(new PhotoCleanupProgress(_itemsDone, _items.Length, _bytesDone, _bytesTotal, current));
        }

        private PhotoCleanupResult Complete()
        {
            for (var i = 0; i < _items.Length; i++) _outcomes[i] ??= new PhotoNotStarted(_items[i].Item.RelPath);
            _progress.Report(new PhotoCleanupProgress(_itemsDone, _items.Length, _bytesDone, _bytesTotal, null));
            return new PhotoCleanupResult(_runId, _confirmed, [.. _outcomes.Select(o => o!)], _stop, [.. _unrecorded], _start,
                                          _env.Clock.GetUtcNow().UtcDateTime);
        }
    }
}
```

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoCleanupReport.cs
namespace UasSort.Core.Cleanup;

public sealed record PhotoCleanupReportLine(string Item, string Kind /* photo|panorama|hyperlapse|set */, int Files, long Bytes,
                                            string Decision /* delete|keep */, bool Verified, string Verification, string Evidence,
                                            string Outcome, string? Error, bool LedgerRecorded);

/// <summary>reports\yyyyMMdd-HHmmss-&lt;run8&gt;-photos.json (spec 2026-10-04 §6): every eligible row with its decision, verification and
/// outcome, then what was not eligible and what was not touched.</summary>
public sealed record PhotoCleanupReport(int V, string RunId, string PhotoRoot, PhotoCleanupMode Mode, DateOnly Cutoff, string? LightroomFolder,
                                        ImmutableArray<PhotoCleanupReportLine> Items, ImmutableArray<string> NotEligible,
                                        ImmutableArray<string> NotTouched, PhotoCleanupStop? Stop, ImmutableArray<string> Unrecorded,
                                        DateTime StartUtc, DateTime EndUtc);

public static class PhotoCleanupReports
{
    public static PhotoCleanupReport Build(PhotoCleanupResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var plan = result.Plan.Plan;
        var byItem = result.Outcomes.ToDictionary(o => o.Item, StringComparer.OrdinalIgnoreCase);
        var unrecorded = result.Unrecorded.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = new List<PhotoCleanupReportLine>();
        foreach (var row in plan.Rows)
        {
            var item = row.Item;
            var chosen = byItem.TryGetValue(row.Key, out var o);
            var (outcome, error) = !chosen ? ("kept", (string?)null) : o! switch
            {
                PhotoRecycled => ("moved to the Recycle Bin", null),
                PhotoSkippedChanged s => ("skipped: changed since review", s.Why),
                PhotoPartlyRecycled p => ("partly moved", $"{p.Why}; still in Picture Offload: {string.Join(", ", p.Left)}"),
                PhotoRecycleFailed f => (f.NotRecyclable ? "kept: Windows would delete it permanently" : "failed", f.Error),
                PhotoNotStarted => ("not started", null),
            };
            var moved = o is PhotoRecycled or PhotoPartlyRecycled;
            var recorded = moved && item.Members.All(m => !unrecorded.Contains(PhotoCleanupPaths.Full(plan.PhotoRoot, m.RelPath)));
            lines.Add(new PhotoCleanupReportLine(item.RelPath, KindText(item), item.Members.Length, item.Bytes, chosen ? "delete" : "keep",
                row.Verification.Verified, row.Verification.Text, chosen ? PhotoDeleteRecords.Evidence(result.Plan.EvidenceOf(row)) : "-",
                outcome, error, recorded));
        }
        return new PhotoCleanupReport(1, result.RunId, plan.PhotoRoot, plan.Request.Mode, plan.Request.Cutoff, plan.Request.LightroomFolder,
            [.. lines], [.. plan.NotEligible.Select(r => $"{r.Key}: {r.Why}")], plan.NotTouched, result.Stop, result.Unrecorded,
            result.StartUtc, result.EndUtc);
    }

    private static string KindText(PhotoItem item) => item.Kind == PhotoItemKind.Photo ? "photo" : item.SetKind switch
    {
        PhotoSetKind.Panorama => "panorama",
        PhotoSetKind.Hyperlapse => "hyperlapse",
        _ => "set",
    };
}
```

In `src/UasSort.Core/Json/CoreJsonContext.cs` add `[JsonSerializable(typeof(PhotoCleanupReport))]` after `[JsonSerializable(typeof(CleanupReport))]`.

In `src/UasSort.Core/Ports/Ports.cs`, replace the `IReportStore` line with:

```csharp
public interface IReportStore { string Save(OffloadReport r); string Save(CleanupReport r); string Save(PhotoCleanupReport r); }
```

In `src/UasSort.Platform/Stores/ReportStore.cs`, add after the `CleanupReport` overload:

```csharp
    public string Save(PhotoCleanupReport r)
        => StoreFiles.WriteNew(Name(r.RunId, "-photos"), JsonSerializer.Serialize(r, CoreJsonContext.Default.PhotoCleanupReport), _ctx);
```

In `tests/UasSort.Testing/OffloadFakes.cs` (`MemReportStore`), add:

```csharp
    public List<PhotoCleanupReport> Photos { get; } = [];

    public string Save(PhotoCleanupReport r)
    {
        if (Throws) throw new IOException("The report couldn't be written.");
        Photos.Add(r);
        return $@"{FakeLayout.AppDataDir}\reports\{r.RunId}-photos.json";
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PhotoCleanupExecutorTests" --filter-class "*JsonContextTests"`
Expected: PASS.
Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*PhotoReportStoreTests" --filter-class "*StoreTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/Photos/PhotoCleanupRun.cs src/UasSort.Core/Cleanup/Photos/PhotoCleanupExecutor.cs src/UasSort.Core/Cleanup/Photos/PhotoCleanupReport.cs src/UasSort.Core/Json/CoreJsonContext.cs src/UasSort.Core/Ports/Ports.cs src/UasSort.Platform/Stores/ReportStore.cs tests/UasSort.Testing/OffloadFakes.cs tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupExecutorTests.cs tests/UasSort.Platform.Tests/PhotoReportStoreTests.cs
git commit -F - <<'EOF'
feat: run confirmed Picture Offload plans with per-item re-checks, photoDelete records and a report

<session trailer>
EOF
```

---
### Task P.11: Windows recycler, photo reader and source guard

**Files:**
- Create: `src/UasSort.Platform/Win32/FileOperationCom.cs`
- Create: `src/UasSort.Platform/Io/StaWorker.cs`, `src/UasSort.Platform/Io/WindowsPhotoRootRecycler.cs`, `src/UasSort.Platform/Io/GuardedPhotoFileReader.cs`, `src/UasSort.Platform/Io/FolderFacts.cs`
- Create: `src/UasSort.Platform/Stores/RecycleBinPurge.cs`, `src/UasSort.Platform/Stores/SelfTestSandbox.RecycleBin.cs`
- Modify: `src/UasSort.Platform/Io/GuardContexts.cs` (new `ForPhotoCleanup`)
- Modify: `src/UasSort.Platform/PlatformServices.cs` (two new members, composed in `Create`)
- Modify: `tests/UasSort.Platform.Tests/SourceGuardTests.cs` (new test), `tests/UasSort.Platform.Tests/PlatformServicesTests.cs` (two asserts)
- Test: `tests/UasSort.Platform.Tests/PhotoRootRecyclerTests.cs`, `tests/UasSort.Platform.Tests/PhotoFileReaderTests.cs`

**Interfaces:**
- Consumes: `IPhotoRootRecycler`, `IPhotoRootRecyclerFactory`, `IPhotoFileReader`, `PhotoItemStat`, `RecycleOk`/`RecycleError`/`RecycleResult`, `IoOp.PhotoCleanupRead`/`PhotoRootRecycle`, `GuardContext.LightroomFolder`/`PhotoCleanup` (P.6); `ConfirmedPhotoCleanupPlan` (P.5); `IoGate`, `Kernel32`, `GuardContexts.For`, `SelfTestSandbox.FolderPrefix`, `TestEnv`, `TempDir`, `RepoPaths`, `PhotoCleanupFixtures` (existing / P.5).
- Produces:
  - `public static GuardContext GuardContexts.ForPhotoCleanup(Settings s, string appDataDir, string machine, IPathFacts facts, ConfirmedPhotoCleanupPlan? plan)`
  - `public static class FolderFacts { static bool Exists(string path); }`
  - `public sealed class GuardedPhotoFileReader(Settings settings, string appDataDir, string machine, IPathFacts facts) : IPhotoFileReader`
  - `public sealed class WindowsPhotoRootRecycler : IPhotoRootRecycler` (internal constructor `(GuardContext ctx)`), `public sealed class WindowsPhotoRootRecyclerFactory(Func<Settings> settings, string appDataDir, string machine, IPathFacts facts) : IPhotoRootRecyclerFactory`
  - `internal sealed class StaWorker : IDisposable { StaWorker(string name); T Invoke<T>(Func<T> work); }`
  - `internal static unsafe partial class FileOperationCom { const int MaxShellPath = 260; const uint TsfDeleteRecycleIfPossible = 0x80; const int HResultCancelled; static RecycleResult Recycle(string path); static RecycleResult Outcome(bool refusedPermanentDelete, int performResult, int deleteResult, bool deleted, bool aborted); }` + `internal partial interface IFileOperation`, `internal partial interface IFileOperationProgressSink`, `internal sealed partial class RecycleSink { bool RefusedPermanentDelete; bool Deleted; int DeleteResult; }`
  - `internal static class RecycleBinPurge { static int PurgeOwn(string scratchRoot); static string? OriginalPath(ReadOnlySpan<byte> info); }` (internal: reachable only from Platform.Tests through the existing `InternalsVisibleTo` and from `SelfTestSandbox`)
  - `public int SelfTestSandbox.PurgeOwnRecycleBinItems()` (the selftest's only way to purge; `RecycleBinPurge.PurgeOwn(Root)`)
  - `PlatformServices(..., FileLog Log, IPhotoRootRecyclerFactory PhotoRecyclers, Func<UasSort.Core.Settings, IPhotoFileReader> PhotoReaderFor)`

**Why `IFileOperation` and how it stays AOT-safe:** see Resolved ambiguity 2. Everything is source-generated: `[GeneratedComInterface]` declares `IFileOperation` and `IFileOperationProgressSink` (vtable order exactly as in `shobjidl_core.h`; members the recycler does not call take raw `nint`), `[GeneratedComClass]` makes the managed sink callable from COM, `[LibraryImport]` declares `CoCreateInstance` and `SHCreateItemFromParsingName`, and `ComInterfaceMarshaller<IFileOperation>.ConvertToManaged/Free` turns the raw pointer into the generated wrapper — no `[ComImport]`, no `Marshal.GetObjectForIUnknown`, no casts of WinRT values. IFileOperation is STA-only, so every call runs on one dedicated STA thread (`StaWorker`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/UasSort.Platform.Tests/PhotoRootRecyclerTests.cs
using System.Buffers.Binary;
using System.Text;
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Platform.Tests;

public sealed class PhotoRootRecyclerTests
{
    private static readonly DateOnly D = new(2025, 6, 1);

    private static ConfirmedPhotoCleanupPlan PlanFor(string photoRoot, params PhotoRow[] rows)
        => Confirmed(TimeProvider.System, PhotoCleanupMode.BeforeDate, rows, photoRoot);

    private static IPhotoRootRecycler Open(TestEnv env, ConfirmedPhotoCleanupPlan plan)
        => new WindowsPhotoRootRecyclerFactory(() => env.Settings, env.AppData, TestEnv.Machine, env.Facts).Open(plan);

    [Fact]
    public void Recycle_AConfirmedFileAndSetFolder_GoToTheRecycleBin_AndTheTestPurgesOnlyItsOwnItems()
    {
        using var env = new TestEnv();
        try
        {
            var root = env.C(env.PhotoRoot);
            env.Temp.File(@"photo\DJI_20250601121000_0002_D.DNG", 1_000);
            env.Temp.File(@"photo\001_0042\PANO_0001.DNG", 500);
            env.Temp.File(@"photo\001_0042\PANO_0002.DNG", 500);
            var plan = PlanFor(root, Row(Photo("DJI_20250601121000_0002_D.DNG", D)),
                                     Row(Set("001_0042", D, PhotoSetKind.Panorama, "PANO_0001.DNG", "PANO_0002.DNG")));
            using (var recycler = Open(env, plan))
            {
                Assert.Equal(1_000, recycler.Stat(Path.Join(root, "DJI_20250601121000_0002_D.DNG"))!.Size);
                Assert.True(recycler.Stat(Path.Join(root, "001_0042"))!.IsDirectory);
                Assert.True(recycler.Recycle(Path.Join(root, "DJI_20250601121000_0002_D.DNG")) is RecycleOk);
                Assert.True(recycler.Recycle(Path.Join(root, "001_0042")) is RecycleOk);
                Assert.Null(recycler.Stat(Path.Join(root, "001_0042")));
            }
            Assert.False(File.Exists(Path.Join(root, "DJI_20250601121000_0002_D.DNG")));
            Assert.False(Directory.Exists(Path.Join(root, "001_0042")));
            Assert.Equal(2, RecycleBinPurge.PurgeOwn(env.Temp.Path));          // both went to the Recycle Bin; nothing of anyone else's is touched
        }
        finally
        {
            RecycleBinPurge.PurgeOwn(env.Temp.Path);
        }
    }

    [Fact]
    public void Recycle_AnythingTheConfirmedPlanDoesNotName_IsRefused_AndStays()
    {
        using var env = new TestEnv();
        var root = env.C(env.PhotoRoot);
        var named = env.Temp.File(@"photo\A.DNG", 10);
        var other = env.Temp.File(@"photo\B.DNG", 10);
        var nested = env.Temp.File(@"photo\001_0042\PANO_0001.DNG", 10);
        var video = env.Temp.File(@"video\2025\DJI_20250601120000_0001_D.MP4", 10);
        using var recycler = Open(env, PlanFor(root, Row(Photo("A.DNG", D)), Row(Set("001_0042", D, PhotoSetKind.Panorama, "PANO_0001.DNG"))));
        Assert.Throws<UnsafeIoException>(() => recycler.Recycle(other));
        Assert.Throws<UnsafeIoException>(() => recycler.Recycle(nested));
        Assert.Throws<UnsafeIoException>(() => recycler.Recycle(video));
        Assert.Throws<UnsafeIoException>(() => recycler.Recycle(root));
        Assert.True(File.Exists(named) && File.Exists(other) && File.Exists(nested) && File.Exists(video));
    }

    [Fact]
    public void Open_APlanForAnotherPhotoFolder_IsRefused()
    {
        using var env = new TestEnv();
        var plan = PlanFor(env.C(env.Temp.Sub("elsewhere")), Row(Photo("A.DNG", D)));
        Assert.Throws<UnsafeIoException>(() => Open(env, plan));
    }

    [Fact]
    public void RecycleBinPurge_ReadsBothRecordVersions_AndRefusesForeignRoots()
    {
        const string path = @"C:\Users\u\AppData\Local\Temp\uas-sort-test-1\photo\A.DNG";
        var v2 = new byte[28 + (path.Length + 1) * 2];
        BinaryPrimitives.WriteInt64LittleEndian(v2, 2);
        BinaryPrimitives.WriteInt32LittleEndian(v2.AsSpan(24), path.Length + 1);
        Encoding.Unicode.GetBytes(path).CopyTo(v2, 28);
        Assert.Equal(path, RecycleBinPurge.OriginalPath(v2));
        var v1 = new byte[24 + 520];
        BinaryPrimitives.WriteInt64LittleEndian(v1, 1);
        Encoding.Unicode.GetBytes(path).CopyTo(v1, 24);
        Assert.Equal(path, RecycleBinPurge.OriginalPath(v1));
        Assert.Throws<InvalidOperationException>(() => RecycleBinPurge.PurgeOwn(@"C:\Users"));
        Assert.Throws<InvalidOperationException>(() => RecycleBinPurge.PurgeOwn(Path.Join(Path.GetTempPath(), "someone-else")));
    }

    [Fact] // [Review Focus 5]
    public void RecycleSink_RefusesEveryDeleteWithoutTheRecycleFlag()
    {
        var permanent = new RecycleSink();
        Assert.Equal(FileOperationCom.HResultCancelled, permanent.PreDeleteItem(0, 0));
        Assert.True(permanent.RefusedPermanentDelete);
        var recycle = new RecycleSink();
        Assert.Equal(0, recycle.PreDeleteItem(FileOperationCom.TsfDeleteRecycleIfPossible, 0));
        Assert.False(recycle.RefusedPermanentDelete);
        Assert.Equal(0, recycle.PostDeleteItem(FileOperationCom.TsfDeleteRecycleIfPossible, 0, 0, 0));
        Assert.True(recycle.Deleted);
    }

    [Theory] // [Review Focus 5]
    [InlineData(0)]                                   // PerformOperations reported success
    [InlineData(unchecked((int)0x800704C7))]          // … or ERROR_CANCELLED
    [InlineData(unchecked((int)0x80070005))]          // … or another failure
    public void Outcome_ARefusal_IsNotRecyclable_WhateverTheShellReturned(int performResult)
    {
        var e = Assert.IsType<RecycleError>(FileOperationCom.Outcome(refusedPermanentDelete: true, performResult, 0, deleted: false, aborted: true));
        Assert.True(e.NotRecyclable);
        Assert.Equal(FileOperationCom.HResultCancelled, e.Code);
    }

    [Fact]
    public void Outcome_WithoutARefusal_MapsTheShellResult()
    {
        Assert.IsType<RecycleOk>(FileOperationCom.Outcome(false, 0, 0, deleted: true, aborted: false));
        var failed = Assert.IsType<RecycleError>(FileOperationCom.Outcome(false, unchecked((int)0x80070005), 0, false, false));
        Assert.Equal((unchecked((int)0x80070005), false), (failed.Code, failed.NotRecyclable));
        Assert.Equal(unchecked((int)0x80070020), Assert.IsType<RecycleError>(FileOperationCom.Outcome(false, 0, unchecked((int)0x80070020), false, false)).Code);
        var cancelled = Assert.IsType<RecycleError>(FileOperationCom.Outcome(false, 0, 0, deleted: false, aborted: true));
        Assert.Equal((FileOperationCom.HResultCancelled, false), (cancelled.Code, cancelled.NotRecyclable));
    }
}
```

```csharp
// tests/UasSort.Platform.Tests/PhotoFileReaderTests.cs
namespace UasSort.Platform.Tests;

public sealed class PhotoFileReaderTests
{
    [Fact]
    public void OpenRead_ReadsPictureOffloadAndLightroomFiles_ButNothingElse()
    {
        using var env = new TestEnv();
        var lightroom = env.Temp.Sub("lightroom");
        var dng = new SyntheticDngBuilder().Build();
        string Put(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, dng);
            return path;
        }
        var photo = Put(Path.Join(env.PhotoRoot, "A.DNG"));
        var member = Put(Path.Join(env.PhotoRoot, "001_0042", "PANO_0001.DNG"));
        var library = Put(Path.Join(lightroom, "2026", "Damian_20260601_001.dng"));
        var catalog = Put(Path.Join(lightroom, "LR_Catalog", "Lightroom Catalog.lrcat"));
        var video = Put(Path.Join(env.VideoRoot, "DJI_20250601120000_0001_D.MP4"));
        var reader = new GuardedPhotoFileReader(env.Settings with { LightroomFolder = lightroom }, env.AppData, TestEnv.Machine, env.Facts);

        foreach (var ok in new[] { photo, member, library })
        {
            using var s = reader.OpenRead(ok);
            Assert.Equal(SyntheticDngBuilder.Pano0001Dto, StillProbe.Read(s).DtoNaive);
        }
        Assert.Throws<UnsafeIoException>(() => reader.OpenRead(catalog));
        Assert.Throws<UnsafeIoException>(() => reader.OpenRead(video));
        using (reader.OpenRead(photo))
        {
            File.Move(photo, photo + ".moved");                                   // FileShare.Delete: an open read never blocks a move
        }
    }

    [Fact]
    public void FolderFacts_SeesOnlyExistingFolders()
    {
        using var env = new TestEnv();
        Assert.True(FolderFacts.Exists(env.PhotoRoot));
        Assert.False(FolderFacts.Exists(env.Temp.File("x.txt")));
        Assert.False(FolderFacts.Exists(Path.Join(env.Temp.Path, "missing")));
        Assert.False(FolderFacts.Exists(""));
    }
}
```

Add to `tests/UasSort.Platform.Tests/SourceGuardTests.cs`:

```csharp
    [Fact]
    public void OnlyTheWindowsPhotoRootRecycler_RecyclesUnderThePhotoRoot()
    {
        static IEnumerable<string> Code(string f)
            => File.ReadLines(f).Select(l => l.IndexOf("//", StringComparison.Ordinal) is var c and >= 0 ? l[..c] : l);
        static HashSet<string> Files(params string[] rel) => rel.Select(RepoPaths.Of).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var recycler = Files("src/UasSort.Platform/Io/WindowsPhotoRootRecycler.cs", "src/UasSort.Platform/Win32/FileOperationCom.cs");
        string[] markers = ["IFileOperation", "SHFileOperation", "FOFX_RECYCLEONDELETE", "FofxRecycleOnDelete", "FOF_ALLOWUNDO", "FofAllowUndo",
                            "SHCreateItemFromParsingName", "RecycleOption"];
        var movers = SourceFiles("src").Where(f => !recycler.Contains(Path.GetFullPath(f)))
            .Where(f => Code(f).Any(l => markers.Any(m => l.Contains(m, StringComparison.Ordinal))))
            .Select(RepoPaths.Relative).ToList();
        Assert.True(movers.Count == 0, "Recycle Bin moves outside WindowsPhotoRootRecycler: " + string.Join(", ", movers));

        var guarded = Files("src/UasSort.Core/Guard/IoGuardPolicy.cs", "src/UasSort.Platform/Io/WindowsPhotoRootRecycler.cs");
        var opUsers = SourceFiles("src").Where(f => !guarded.Contains(Path.GetFullPath(f)))
            .Where(f => Code(f).Any(l => l.Contains("IoOp.PhotoRootRecycle", StringComparison.Ordinal)))
            .Select(RepoPaths.Relative).ToList();
        Assert.True(opUsers.Count == 0, "IoOp.PhotoRootRecycle used outside the guard and the recycler: " + string.Join(", ", opUsers));
        Assert.Single(File.ReadAllLines(RepoPaths.Of("src/UasSort.Platform/Io/WindowsPhotoRootRecycler.cs")),
                      l => l.Contains("IoGate.Require(IoOp.PhotoRootRecycle,", StringComparison.Ordinal));

        // CardClassifier only *skips* a card's own $RECYCLE.BIN folder (Ref §5 system rule); it never touches the PC's Recycle Bin.
        var purge = Files("src/UasSort.Platform/Stores/RecycleBinPurge.cs", "src/UasSort.Core/Card/CardClassifier.cs");
        var binTouchers = SourceFiles("src").Where(f => !purge.Contains(Path.GetFullPath(f)))
            .Where(f => Code(f).Any(l => l.Contains("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)))
            .Select(RepoPaths.Relative).ToList();
        Assert.True(binTouchers.Count == 0, "Recycle Bin contents touched outside RecycleBinPurge: " + string.Join(", ", binTouchers));

        var purgeCallers = Files("src/UasSort.Platform/Stores/RecycleBinPurge.cs", "src/UasSort.Platform/Stores/SelfTestSandbox.RecycleBin.cs");
        var callers = SourceFiles("src").Where(f => !purgeCallers.Contains(Path.GetFullPath(f)))
            .Where(f => Code(f).Any(l => l.Contains("RecycleBinPurge", StringComparison.Ordinal)))
            .Select(RepoPaths.Relative).ToList();
        Assert.True(callers.Count == 0, "RecycleBinPurge reached outside SelfTestSandbox.PurgeOwnRecycleBinItems: " + string.Join(", ", callers));
    }
```

In `PlatformServicesTests.Create_ComposesThePlatformClasses_AndWritesNothing`, before the last assert add:

```csharp
        Assert.IsType<WindowsPhotoRootRecyclerFactory>(p.PhotoRecyclers);
        Assert.IsType<GuardedPhotoFileReader>(p.PhotoReaderFor(SettingsDefaults.Derive(temp.FullPath)));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*PhotoRootRecyclerTests" --filter-class "*PhotoFileReaderTests"`
Expected: build fails (`CS0246: … 'WindowsPhotoRootRecyclerFactory' could not be found`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Platform/Win32/FileOperationCom.cs
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace UasSort.Platform.Win32;

/// <summary>The shell's IFileOperation through source-generated COM (AOT-safe), used only by WindowsPhotoRootRecycler to move one item to
/// the Recycle Bin. Never a permanent delete: RecycleSink cancels every delete the shell would not recycle — the shell clears
/// TSF_DELETE_RECYCLE_IF_POSSIBLE for those (an online-only OneDrive file, a volume without a Recycle Bin, an item too large for it).</summary>
internal static unsafe partial class FileOperationCom
{
    public const int MaxShellPath = 260;
    internal const uint TsfDeleteRecycleIfPossible = 0x80;
    internal const int HResultCancelled = unchecked((int)0x800704C7);            // HRESULT_FROM_WIN32(ERROR_CANCELLED)
    private const uint FofSilent = 0x0004, FofNoConfirmation = 0x0010, FofAllowUndo = 0x0040, FofNoErrorUi = 0x0400;
    private const uint FofxRecycleOnDelete = 0x00080000, FofxEarlyFailure = 0x00100000;
    internal const uint RecycleFlags = FofAllowUndo | FofNoConfirmation | FofNoErrorUi | FofSilent | FofxRecycleOnDelete | FofxEarlyFailure;
    private const uint ClsctxInprocServer = 0x1, ClsctxLocalServer = 0x4;
    private static readonly Guid ClsidFileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    private static readonly Guid IidFileOperation = new("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8");
    private static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    /// <summary>Moves one file or folder to the Recycle Bin. Must run on an STA thread (StaWorker). Never throws for a shell or COM
    /// failure: every one becomes a RecycleError, so the executor always ends with a result and a report.</summary>
    public static RecycleResult Recycle(string path)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("IFileOperation needs an STA thread");               // a wiring bug, not a shell failure
#pragma warning disable CA1031 // a COM wrapper may throw (COMException, InvalidCastException, …): it becomes this item's RecycleError, never a faulted run
        try
        {
            return RecycleCore(path);
        }
        catch (Exception e)
        {
            return new RecycleError(e.HResult, "the shell's file operation failed: " + e.Message, false);
        }
#pragma warning restore CA1031
    }

    private static RecycleResult RecycleCore(string path)
    {
        var hr = CoCreateInstance(ClsidFileOperation, 0, ClsctxInprocServer | ClsctxLocalServer, IidFileOperation, out var opPtr);
        if (hr < 0) return Failed(hr, "the shell's file operation couldn't be created");
        IFileOperation op;
        try
        {
            op = ComInterfaceMarshaller<IFileOperation>.ConvertToManaged((void*)opPtr)
                 ?? throw new InvalidOperationException("IFileOperation: no object");
        }
        finally
        {
            ComInterfaceMarshaller<IFileOperation>.Free((void*)opPtr);
        }

        nint item = 0;
        uint cookie = 0;
        var sink = new RecycleSink();
        try
        {
            if ((hr = op.SetOperationFlags(RecycleFlags)) < 0) return Failed(hr, "the Recycle Bin flags were refused");
            if ((hr = SHCreateItemFromParsingName(path, 0, IidShellItem, out item)) < 0) return Failed(hr, "the shell couldn't find it");
            if ((hr = op.Advise(sink, out cookie)) < 0) return Failed(hr, "the shell refused the progress callback");
            if ((hr = op.DeleteItem(item, 0)) < 0) return Failed(hr, "the shell refused to queue the move");
            hr = op.PerformOperations();
            _ = op.GetAnyOperationsAborted(out var aborted);
            return Outcome(sink.RefusedPermanentDelete, hr, sink.DeleteResult, sink.Deleted, aborted != 0);
        }
        finally
        {
            if (cookie != 0) _ = op.Unadvise(cookie);
            if (item != 0) Marshal.Release(item);
            if ((object)op is ComObject com) com.FinalRelease();                                   // via object: ComObject is sealed (CS8121)
        }
    }

    /// <summary>The result of one IFileOperation run (pure, unit-tested). The sink's refusal is checked first: whatever PerformOperations
    /// returned (success, ERROR_CANCELLED, another failure), a refused permanent delete is NotRecyclable and the item was kept.</summary>
    internal static RecycleResult Outcome(bool refusedPermanentDelete, int performResult, int deleteResult, bool deleted, bool aborted)
    {
        if (refusedPermanentDelete)
            return new RecycleError(HResultCancelled, "Windows would delete it permanently instead of moving it to the Recycle Bin; kept", true);
        if (performResult < 0) return Failed(performResult, "moving it to the Recycle Bin failed");
        if (deleteResult < 0) return Failed(deleteResult, "moving it to the Recycle Bin failed");
        if (aborted || !deleted) return new RecycleError(HResultCancelled, "the move to the Recycle Bin was cancelled; kept", false);
        return new RecycleOk();
    }

    private static RecycleError Failed(int hr, string what)
        => new(hr, string.Create(CultureInfo.InvariantCulture, $"{what} (HRESULT 0x{hr:X8})"), false);

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHCreateItemFromParsingName(string pszPath, nint pbc, in Guid riid, out nint ppv);
}

/// <summary>IFileOperation (shobjidl_core.h), every member in vtable order; members the recycler never calls take raw pointers.</summary>
[GeneratedComInterface]
[Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
internal partial interface IFileOperation
{
    [PreserveSig] int Advise(IFileOperationProgressSink pfops, out uint pdwCookie);
    [PreserveSig] int Unadvise(uint dwCookie);
    [PreserveSig] int SetOperationFlags(uint dwOperationFlags);
    [PreserveSig] int SetProgressMessage(nint pszMessage);
    [PreserveSig] int SetProgressDialog(nint popd);
    [PreserveSig] int SetProperties(nint pproparray);
    [PreserveSig] int SetOwnerWindow(nint hwndOwner);
    [PreserveSig] int ApplyPropertiesToItem(nint psiItem);
    [PreserveSig] int ApplyPropertiesToItems(nint punkItems);
    [PreserveSig] int RenameItem(nint psiItem, nint pszNewName, nint pfopsItem);
    [PreserveSig] int RenameItems(nint pUnkItems, nint pszNewName);
    [PreserveSig] int MoveItem(nint psiItem, nint psiDestinationFolder, nint pszNewName, nint pfopsItem);
    [PreserveSig] int MoveItems(nint punkItems, nint psiDestinationFolder);
    [PreserveSig] int CopyItem(nint psiItem, nint psiDestinationFolder, nint pszCopyName, nint pfopsItem);
    [PreserveSig] int CopyItems(nint punkItems, nint psiDestinationFolder);
    [PreserveSig] int DeleteItem(nint psiItem, nint pfopsItem);
    [PreserveSig] int DeleteItems(nint punkItems);
    [PreserveSig] int NewItem(nint psiDestinationFolder, uint dwFileAttributes, nint pszName, nint pszTemplateName, nint pfopsItem);
    [PreserveSig] int PerformOperations();
    [PreserveSig] int GetAnyOperationsAborted(out int pfAnyOperationsAborted);
}

/// <summary>IFileOperationProgressSink (shobjidl_core.h), every member in vtable order.</summary>
[GeneratedComInterface]
[Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
internal partial interface IFileOperationProgressSink
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int hrResult);
    [PreserveSig] int PreRenameItem(uint dwFlags, nint psiItem, nint pszNewName);
    [PreserveSig] int PostRenameItem(uint dwFlags, nint psiItem, nint pszNewName, int hrRename, nint psiNewlyCreated);
    [PreserveSig] int PreMoveItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName);
    [PreserveSig] int PostMoveItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName, int hrMove, nint psiNewlyCreated);
    [PreserveSig] int PreCopyItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName);
    [PreserveSig] int PostCopyItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName, int hrCopy, nint psiNewlyCreated);
    [PreserveSig] int PreDeleteItem(uint dwFlags, nint psiItem);
    [PreserveSig] int PostDeleteItem(uint dwFlags, nint psiItem, int hrDelete, nint psiNewlyCreated);
    [PreserveSig] int PreNewItem(uint dwFlags, nint psiDestinationFolder, nint pszNewName);
    [PreserveSig] int PostNewItem(uint dwFlags, nint psiDestinationFolder, nint pszNewName, nint pszTemplateName, uint dwFileAttributes,
                                  int hrNew, nint psiNewItem);
    [PreserveSig] int UpdateProgress(uint iWorkTotal, uint iWorkSoFar);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}

/// <summary>Refuses (ERROR_CANCELLED) any delete the shell announces without TSF_DELETE_RECYCLE_IF_POSSIBLE, i.e. a permanent delete,
/// and records how the delete ended.</summary>
[GeneratedComClass]
internal sealed partial class RecycleSink : IFileOperationProgressSink
{
    public bool RefusedPermanentDelete { get; private set; }
    public bool Deleted { get; private set; }
    public int DeleteResult { get; private set; }

    public int PreDeleteItem(uint dwFlags, nint psiItem)
    {
        if ((dwFlags & FileOperationCom.TsfDeleteRecycleIfPossible) != 0) return 0;
        RefusedPermanentDelete = true;
        return FileOperationCom.HResultCancelled;
    }

    public int PostDeleteItem(uint dwFlags, nint psiItem, int hrDelete, nint psiNewlyCreated)
    {
        DeleteResult = hrDelete;
        Deleted = hrDelete >= 0;
        return 0;
    }

    public int StartOperations() => 0;
    public int FinishOperations(int hrResult) => 0;
    public int PreRenameItem(uint dwFlags, nint psiItem, nint pszNewName) => 0;
    public int PostRenameItem(uint dwFlags, nint psiItem, nint pszNewName, int hrRename, nint psiNewlyCreated) => 0;
    public int PreMoveItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName) => 0;
    public int PostMoveItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName, int hrMove, nint psiNewlyCreated) => 0;
    public int PreCopyItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName) => 0;
    public int PostCopyItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName, int hrCopy, nint psiNewlyCreated) => 0;
    public int PreNewItem(uint dwFlags, nint psiDestinationFolder, nint pszNewName) => 0;
    public int PostNewItem(uint dwFlags, nint psiDestinationFolder, nint pszNewName, nint pszTemplateName, uint dwFileAttributes, int hrNew,
                           nint psiNewItem) => 0;
    public int UpdateProgress(uint iWorkTotal, uint iWorkSoFar) => 0;
    public int ResetTimer() => 0;
    public int PauseTimer() => 0;
    public int ResumeTimer() => 0;
}
```

```csharp
// src/UasSort.Platform/Io/StaWorker.cs
using System.Collections.Concurrent;

namespace UasSort.Platform.Io;

/// <summary>One dedicated STA thread running work items in order (IFileOperation is STA-only). Invoke blocks its caller.</summary>
internal sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaWorker(string name)
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public T Invoke<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
#pragma warning disable CA1031 // every failure of the work item is handed back to the calling thread
            try { done.SetResult(work()); }
            catch (Exception e) { done.SetException(e); }
#pragma warning restore CA1031
        });
        return done.Task.GetAwaiter().GetResult();
    }

    private void Loop()
    {
        foreach (var work in _queue.GetConsumingEnumerable()) work();
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join();
        _queue.Dispose();
    }
}
```

```csharp
// src/UasSort.Platform/Io/WindowsPhotoRootRecycler.cs
namespace UasSort.Platform.Io;

/// <summary>The only code that moves anything out of the photo root (spec 2026-10-04 §5): IoGuardPolicy.Check(PhotoRootRecycle) — in
/// the confirmed plan, directly in the photo root — then IFileOperation to the Recycle Bin on its own STA thread. Stat reads attributes only.</summary>
public sealed class WindowsPhotoRootRecycler : IPhotoRootRecycler
{
    private readonly GuardContext _ctx;
    private readonly StaWorker _sta = new("uas-sort Recycle Bin");
    private bool _disposed;

    internal WindowsPhotoRootRecycler(GuardContext ctx) => _ctx = ctx;

    public PhotoItemStat? Stat(string fullPath)
    {
        var path = Path.GetFullPath(fullPath);
        if (Kernel32.TryGetAttributeData(path, out var d, out var error))
            return new PhotoItemStat(d.Size, d.LastWriteUtc, d.FileAttributes, (d.FileAttributes & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0);
        return Kernel32.IsNotFound(error) ? null : throw new IOException($"Stat {path} failed (Win32 error {error})", error);
    }

    public RecycleResult Recycle(string fullPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var path = Path.GetFullPath(fullPath);
        IoGate.Require(IoOp.PhotoRootRecycle, path, _ctx);
        if (path.Length >= FileOperationCom.MaxShellPath)
            return new RecycleError(206, "the path is too long for the Recycle Bin; kept", false);
        return _sta.Invoke(() => FileOperationCom.Recycle(path));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sta.Dispose();
    }
}

/// <summary>Re-derives the photo root from the saved settings (never trusts the plan's), refuses a plan for another photo folder, and
/// builds the recycler's own GuardContext with the plan and the Lightroom folder.</summary>
public sealed class WindowsPhotoRootRecyclerFactory(Func<Settings> settings, string appDataDir, string machine, IPathFacts facts)
    : IPhotoRootRecyclerFactory
{
    public IPhotoRootRecycler Open(ConfirmedPhotoCleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var s = settings();
        var root = facts.Canonical(s.PhotoRoot);
        if (!PathRules.Equal(facts.Canonical(plan.PhotoRoot), root))
            throw new UnsafeIoException($"Picture Offload cleanup refused: the confirmed plan is for {plan.PhotoRoot}, the photo folder is {root}");
        if (!FolderFacts.Exists(root)) throw new UnsafeIoException($"Picture Offload cleanup refused: {root} is not available");
        if (PathRules.IsSameOrUnder(root, LedgerPaths.For(facts.Canonical(s.VideoRoot))))
            throw new UnsafeIoException("Picture Offload cleanup refused: the photo folder is inside the history folder");
        return new WindowsPhotoRootRecycler(GuardContexts.ForPhotoCleanup(s, appDataDir, machine, facts, plan));
    }
}
```

```csharp
// src/UasSort.Platform/Io/GuardedPhotoFileReader.cs
namespace UasSort.Platform.Io;

/// <summary>IPhotoFileReader (spec 2026-10-04 §3–§4): read-only opens of Picture Offload and Lightroom library files for their EXIF, each
/// after IoGuardPolicy.Check(PhotoCleanupRead), which reads the attributes first and refuses a cloud placeholder (nothing hydrates).
/// FileShare.ReadWrite | Delete: an open read never blocks OneDrive, Lightroom or the recycler.</summary>
public sealed class GuardedPhotoFileReader(Settings settings, string appDataDir, string machine, IPathFacts facts) : IPhotoFileReader
{
    private readonly GuardContext _ctx = GuardContexts.ForPhotoCleanup(settings, appDataDir, machine, facts, plan: null);

    public Stream OpenRead(string fullPath)
    {
        var path = Path.GetFullPath(fullPath);
        IoGate.Require(IoOp.PhotoCleanupRead, path, _ctx);
#pragma warning disable RS0030 // IO layer: Picture Offload cleanup reads the EXIF of a local photo-root or Lightroom file (guarded, attributes first)
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.RandomAccess, BufferSize = 4096,
        });
#pragma warning restore RS0030
    }
}
```

```csharp
// src/UasSort.Platform/Io/FolderFacts.cs
namespace UasSort.Platform.Io;

/// <summary>Whether a folder exists, from its attributes only (never opened).</summary>
public static class FolderFacts
{
    public static bool Exists(string path)
        => !string.IsNullOrWhiteSpace(path)
           && Kernel32.TryGetAttributes(Path.GetFullPath(path), out _) is uint a && (a & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0;
}
```

```csharp
// src/UasSort.Platform/Stores/RecycleBinPurge.cs
using System.Buffers.Binary;
using System.Security.Principal;
using System.Text;

namespace UasSort.Platform.Stores;

/// <summary>Platform tests and --selftest only: removes from the current user's Recycle Bin exactly the items whose original path lies
/// under one of this run's own scratch folders (%TEMP%\uas-sort-test-* or %TEMP%\uas-sort-selftest-*). Each $I record names the original
/// path; its $R twin is the item. Nothing else is touched. Internal and outside IoGuardPolicy on purpose: the guard's model is the
/// library, card and app-data roots, and the Recycle Bin is none of them; instead this class refuses any root that is not this run's own
/// %TEMP% scratch folder, Platform.Tests reach it through InternalsVisibleTo, and the deployed app only through
/// SelfTestSandbox.PurgeOwnRecycleBinItems (source-guard test).</summary>
internal static class RecycleBinPurge
{
    public static int PurgeOwn(string scratchRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchRoot);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratchRoot));
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var name = Path.GetFileName(root);
        var own = name.StartsWith("uas-sort-test-", StringComparison.OrdinalIgnoreCase)
                  || name.StartsWith(SelfTestSandbox.FolderPrefix, StringComparison.OrdinalIgnoreCase);
        if (!own || !string.Equals(Path.GetDirectoryName(root), temp, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("RecycleBinPurge only purges this run's own %TEMP% scratch items, not " + root);
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("The current user has no SID");
        var bin = Path.Join(Path.GetPathRoot(root), "$Recycle.Bin", sid);
        var purged = 0;
#pragma warning disable RS0030 // IO layer: tests and selftest only; deletes only Recycle Bin items whose $I record names this run's own %TEMP% scratch folder
        if (!Directory.Exists(bin)) return 0;
        foreach (var info in Directory.EnumerateFiles(bin, "$I*"))
        {
            if (OriginalPath(File.ReadAllBytes(info)) is not { } original
                || !original.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            var data = Path.Join(bin, "$R" + Path.GetFileName(info)[2..]);
            if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
            else if (File.Exists(data)) File.Delete(data);
            File.Delete(info);
            purged++;
        }
#pragma warning restore RS0030
        return purged;
    }

    /// <summary>The original path in a $I record: version 2 (Windows 10 and later, length-prefixed UTF-16) or version 1 (260 chars).</summary>
    internal static string? OriginalPath(ReadOnlySpan<byte> info)
    {
        if (info.Length < 24) return null;
        var version = BinaryPrimitives.ReadInt64LittleEndian(info);
        if (version == 2 && info.Length >= 28)
        {
            var chars = BinaryPrimitives.ReadInt32LittleEndian(info[24..]);
            if (chars <= 0 || 28L + chars * 2L > info.Length) return null;
            return Encoding.Unicode.GetString(info.Slice(28, chars * 2)).TrimEnd('\0');
        }
        if (version == 1 && info.Length >= 24 + 520)
        {
            var text = Encoding.Unicode.GetString(info.Slice(24, 520));
            var end = text.IndexOf('\0', StringComparison.Ordinal);
            return end < 0 ? text : text[..end];
        }
        return null;
    }
}
```

```csharp
// src/UasSort.Platform/Stores/SelfTestSandbox.RecycleBin.cs
namespace UasSort.Platform.Stores;

public sealed partial class SelfTestSandbox
{
    /// <summary>Removes from the Recycle Bin exactly the items this selftest run moved there from its own sandbox (RecycleBinPurge refuses
    /// anything but this %TEMP%\uas-sort-selftest-* root); the selftest's only way to purge.</summary>
    public int PurgeOwnRecycleBinItems() => RecycleBinPurge.PurgeOwn(Root);
}
```

In `src/UasSort.Platform/Io/GuardContexts.cs`, add after `For`:

```csharp
    /// <summary>Picture Offload cleanup (spec 2026-10-04 §5): For(...) plus the canonical Lightroom folder and, for the recycler only, the plan.</summary>
    public static GuardContext ForPhotoCleanup(Settings s, string appDataDir, string machine, IPathFacts facts, ConfirmedPhotoCleanupPlan? plan)
        => For(s, appDataDir, machine, facts) with
        {
            LightroomFolder = s.LightroomFolder is { } lightroom ? facts.Canonical(lightroom) : null,
            PhotoCleanup = plan,
        };
```

In `src/UasSort.Platform/PlatformServices.cs`: append two parameters to the record after `FileLog Log`:

```csharp
    Func<string, ILedgerStore> LedgerFor, Func<UasSort.Core.Settings, IReadOnlySet<string>, IFileOps> FileOpsFor, FileLog Log,
    IPhotoRootRecyclerFactory PhotoRecyclers, Func<UasSort.Core.Settings, IPhotoFileReader> PhotoReaderFor)
```

and in `Create`, replace the last argument `new FileLog(appDataDir, machine, facts, clock));` with:

```csharp
            new FileLog(appDataDir, machine, facts, clock),
            new WindowsPhotoRootRecyclerFactory(Current, appDataDir, machine, facts),     // re-reads the saved photo root at every Open
            s => new GuardedPhotoFileReader(s, appDataDir, machine, facts));
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*PhotoRootRecyclerTests" --filter-class "*PhotoFileReaderTests" --filter-class "*SourceGuardTests" --filter-class "*PlatformServicesTests" --filter-class "*BannedSymbolsTests"`
Expected: PASS, including `RecycleSink_RefusesEveryDeleteWithoutTheRecycleFlag` and the `Outcome_…` tests (the refusal and its mapping, without the shell). `Recycle_AConfirmedFileAndSetFolder_…` really moves two temp items to the Recycle Bin and then purges exactly those two. If it fails with `NotRecyclable` on a `%TEMP%` file, this Windows build does not set `TSF_DELETE_RECYCLE_IF_POSSIBLE` in `PreDeleteItem` for recyclable items: stop and report it (BLOCKED, with the HRESULT and flags seen) instead of weakening the sink — the refusal is the feature's safety guarantee.
Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1 -CheckBannedApi`
Expected: `OK: 73 banned entries, 73 probe calls, 73 RS0030 (one per call)`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform/Win32/FileOperationCom.cs src/UasSort.Platform/Io/StaWorker.cs src/UasSort.Platform/Io/WindowsPhotoRootRecycler.cs src/UasSort.Platform/Io/GuardedPhotoFileReader.cs src/UasSort.Platform/Io/FolderFacts.cs src/UasSort.Platform/Stores/RecycleBinPurge.cs src/UasSort.Platform/Stores/SelfTestSandbox.RecycleBin.cs src/UasSort.Platform/Io/GuardContexts.cs src/UasSort.Platform/PlatformServices.cs tests/UasSort.Platform.Tests/PhotoRootRecyclerTests.cs tests/UasSort.Platform.Tests/PhotoFileReaderTests.cs tests/UasSort.Platform.Tests/SourceGuardTests.cs tests/UasSort.Platform.Tests/PlatformServicesTests.cs
git commit -F - <<'EOF'
feat: recycle Picture Offload items through IFileOperation (AOT-safe COM), refusing permanent deletes

<session trailer>
EOF
```

---
### Task P.12: Review — engine, rows, result, PhotoCleanupVm (and Picture Offload thumbnails)

**Files:**
- Create: `src/UasSort.Core/Cleanup/Photos/PhotoRootThumbnails.cs`
- Create: `src/UasSort.Review/PhotoCleanup/PhotoCleanupAvailability.cs`, `PhotoCleanupEngine.cs`, `PhotoCleanupRowVm.cs`, `PhotoCleanupResultVm.cs`, `PhotoCleanupVm.cs` (all under `src/UasSort.Review/PhotoCleanup/`, namespace `UasSort.Review`)
- Test: `tests/UasSort.Core.Tests/Cleanup/Photos/PhotoRootThumbnailsTests.cs`, `tests/UasSort.Review.Tests/PhotoCleanupVmTests.cs`, `tests/UasSort.Review.Tests/PhotoCleanupEngineTests.cs`

**Interfaces:**
- Consumes: everything Core produced in P.5–P.10 (`PhotoCleanupPlanner.Survey/Build/VerifyRange/Excludes`, `LightroomIndex.Build/Empty`, `PhotoExifCache`, `PhotoCleanupExecutor.RunAsync`, `PhotoCleanupReports.Build`, `PhotoCleanupEnvironment`, outcomes); `CleanupLedgerGate.Load` (existing Review, returns `(LedgerSnapshot?, CleanupPreparation?)`), `RowDecision`, `IKeyed`, `CollectionSync`, `UiProgress<T>`, `Observed.Forget`, `Fmt`, `IShellLauncher`, `IDialogService`, `IUiDispatcher` (existing).
- Produces:
  - Core: `public sealed class PhotoRootThumbnails(IPhotoFileReader reader) : IThumbnailSource { const string KeyPrefix = "photo-root:"; const long MaxWholeJpegBytes = 16_000_000; static ItemId KeyOf(PhotoItem item); static bool IsKey(ItemId id); void Use(string photoRoot, IEnumerable<PhotoItem> items); }`
  - `public sealed record PhotoCleanupContext(bool CommitRunning, bool ScanRunning, bool CardCleanupOpen, bool RootsConfirmed, string PhotoRoot, bool PhotoRootAvailable)`; `public static class PhotoCleanupAvailability { static string? Reason(PhotoCleanupContext c); }`
  - `public sealed record PhotoCleanupPreparation(PhotoSurvey? Survey, string? BlockingText, string? VerifyUnavailableText, string? LightroomFolder) { Action? KeepOnDevice { get; init; } }`
  - `public sealed record PhotoCleanupEngine(Func<IProgress<PhotoScanProgress>, CancellationToken, Task<PhotoCleanupPreparation>> Prepare, Func<PhotoSurvey, PhotoCleanupRequest, IProgress<PhotoScanProgress>, CancellationToken, Task<PhotoCleanupPlan>> Plan, Func<ConfirmedPhotoCleanupPlan, IProgress<PhotoCleanupProgress>, CancellationToken, Task<PhotoCleanupResult>> Run, Func<PhotoCleanupReport, string> SaveReport, IShellLauncher Shell)`
  - `public sealed record PhotoCleanupPorts(Settings Settings, string PhotoRoot, IDirectoryLister Lister, IPhotoFileReader Reader, ILedgerStore Ledger, PhotoCaptureClock Clock, PhotoCleanupEnvironment Environment, Func<string, bool> FolderExists, IReportStore Reports, IShellLauncher Shell) { PhotoRootThumbnails? Thumbnails { get; init; } }` (Settings.LightroomFolder and PhotoRoot canonical — P.14); `public static class PhotoCleanupEngines { static PhotoCleanupEngine Create(PhotoCleanupPorts p); }`
  - `public sealed partial class PhotoCleanupRowVm : ObservableObject, IKeyed { PhotoRow Row; string Key; ItemId ThumbKey; string? DayHeader; string Title; string DetailText; string StatusText; bool IsVerified; RowDecision Decision; IRelayCommand KeepCommand, DeleteCommand; }`
  - `public sealed class PhotoCleanupResultVm { string HeadlineText; string CountsText; IReadOnlyList<string> Problems; string? StopText; string? LedgerWarning; string RecycleBinText; string ReportPath; string? ReportProblem; IRelayCommand OpenReportCommand; }`
  - `public enum PhotoCleanupStep { Choose, Review, Confirm, Running, Result }`
  - `public sealed partial class PhotoCleanupVm : ObservableObject, IDisposable` — ctor `(PhotoCleanupEngine engine, IDialogService dialogs, IUiDispatcher ui, TimeProvider time)`; `Task OpenAsync()`; `void PickDate(DateTimeOffset? picked)`; observable: `Step, IsBusy, BusyText, BlockingText, CanKeepOnDevice, PhotoRootText, PickedDate, Cutoff, Verify (settable), VerifyEnabled, VerifyUnavailableText, Plan, ModeText, TotalsText, NotTouchedText, NotEligibleText, NotEligibleLines, AckMoveText, AckMove (settable), ShowUnverifiedAck, UnverifiedAckText, AckUnverified (settable), CanNext, CanRun, RunButtonText, ProgressText, Result`; `ObservableCollection<PhotoCleanupRowVm> Rows`; commands `NextCommand, BackCommand, DeleteAllCommand, KeepAllCommand, RunCommand, CancelCommand, DoneCommand, KeepOnDeviceCommand`; `event Action? Closed`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/UasSort.Core.Tests/Cleanup/Photos/PhotoRootThumbnailsTests.cs
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class PhotoRootThumbnailsTests
{
    private static readonly DateOnly D = new(2026, 6, 1);

    [Fact]
    public async Task Thumbnails_ADngGivesItsEmbeddedJpeg_UnderAPhotoRootKey()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(PhotoRoot + @"\A.DNG", new SyntheticDngBuilder().Build(), Mtime);
        var thumbs = new PhotoRootThumbnails(new FakePhotoFileReader(fs, FakeLayout.Context(cardRoot: null)));
        var item = Photo("A.DNG", D);
        thumbs.Use(PhotoRoot, [item]);
        var bytes = await thumbs.GetAsync(PhotoRootThumbnails.KeyOf(item), CancellationToken.None);
        Assert.Equal(SyntheticMp4Builder.TinyJpeg.ToArray(), bytes.ToArray());
        Assert.True(PhotoRootThumbnails.IsKey(PhotoRootThumbnails.KeyOf(item)));
        Assert.False(PhotoRootThumbnails.IsKey(new ItemId("DCIM/DJI_001/A.DNG")));
    }

    [Fact] // [Review Focus 1]
    public async Task Thumbnails_CloudOnlyPhoto_IsNeverOpened()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(PhotoRoot + @"\A.DNG", new SyntheticDngBuilder().Build(), Mtime, FakeFileSystem.CloudOnlyPlaceholder);
        var reader = new FakePhotoFileReader(fs, FakeLayout.Context(cardRoot: null));
        var thumbs = new PhotoRootThumbnails(reader);
        var item = new PhotoItem("A.DNG", PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
                                 [Member("A.DNG", D, attributes: FakeFileSystem.CloudOnlyPlaceholder)]);
        thumbs.Use(PhotoRoot, [item]);
        Assert.True((await thumbs.GetAsync(PhotoRootThumbnails.KeyOf(item), CancellationToken.None)).IsEmpty);
        Assert.Empty(reader.Opened);
        Assert.Empty(fs.HydrationViolations);
    }
}
```

```csharp
// tests/UasSort.Review.Tests/PhotoCleanupVmTests.cs
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Review.Tests;

public sealed class PhotoCleanupVmTests
{
    private static readonly DateOnly Jun1 = new(2026, 6, 1), Jun2 = new(2026, 6, 2);
    private const string ReportFile = @"C:\AppData\uas-sort\reports\20261004-200000-photos01-photos.json";

    private sealed class Rig
    {
        public FakeUiDispatcher Ui { get; } = new();
        public FakeDialogService Dialogs { get; } = new();
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 4, 20, 0, 0, TimeSpan.Zero));
        public FakeShellLauncher Shell { get; } = new();
        public PhotoCleanupPreparation Preparation { get; set; } =
            new(new PhotoSurvey(PhotoRoot, [], ["notes.txt"], TestPlans.Ledger()), null, null, @"X:\Lightroom");
        public List<PhotoRow> Rows { get; } = [];
        public List<PhotoCleanupRequest> Requests { get; } = [];
        public ConfirmedPhotoCleanupPlan? Confirmed { get; private set; }
        public PhotoCleanupReport? Report { get; private set; }
        public int Prepares { get; private set; }

        public PhotoCleanupVm Vm()
        {
            var engine = new PhotoCleanupEngine(
                (progress, ct) =>
                {
                    Prepares++;
                    return Task.FromResult(Preparation);
                },
                (survey, request, progress, ct) =>
                {
                    Requests.Add(request);
                    return Task.FromResult(Plan(request.Mode, request.Cutoff, Rows, notTouched: survey.NotTouched));
                },
                (confirmed, progress, ct) =>
                {
                    Confirmed = confirmed;
                    ImmutableArray<PhotoCleanupOutcome> outcomes =
                        [.. confirmed.Items.Select(r => (PhotoCleanupOutcome)new PhotoRecycled(r.Key, r.Item.Members.Length, r.Item.Bytes))];
                    var at = Time.GetUtcNow().UtcDateTime;
                    return Task.FromResult(new PhotoCleanupResult("photos01", confirmed, outcomes, null, [], at, at));
                },
                r =>
                {
                    Report = r;
                    return ReportFile;
                },
                Shell);
            return new PhotoCleanupVm(engine, Dialogs, Ui, Time);
        }
    }

    [Fact]
    public async Task DateMode_EveryRowStartsDelete_GroupedByDay_WithTotals()
    {
        var rig = new Rig();
        rig.Rows.AddRange([Row(Photo("A.DNG", Jun1, twin: "A.JPG")), Row(Set("001_0087", Jun2, PhotoSetKind.Panorama, "PANO_0001.DNG", "PANO_0002.DNG"))]);
        using var vm = rig.Vm();
        await vm.OpenAsync();
        Assert.Equal(PhotoCleanupStep.Choose, vm.Step);
        Assert.False(vm.NextCommand.CanExecute(null));

        vm.PickDate(new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.FromHours(-8)));
        Assert.Equal(new DateOnly(2026, 6, 30), vm.Cutoff);
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(PhotoCleanupStep.Review, vm.Step);
        Assert.Equal(new PhotoCleanupRequest(PhotoCleanupMode.BeforeDate, new DateOnly(2026, 6, 30), null), rig.Requests.Single());
        Assert.All(vm.Rows, r => Assert.Equal(RowDecision.Delete, r.Decision));
        Assert.Equal(["Jun 1 (Mon)", "Jun 2 (Tue)"], vm.Rows.Select(r => r.DayHeader));
        Assert.Equal(["A.DNG + JPG", "Panorama · 001_0087"], vm.Rows.Select(r => r.Title));
        Assert.Equal("1 photo, 1 set, 59 MB to the Recycle Bin", vm.TotalsText);
        Assert.Equal("Not touched: 1 other file or folder in Picture Offload", vm.NotTouchedText);
    }

    [Fact]
    public async Task VerifyMode_UnverifiedRowsStartKeep_AndNeedTheSecondAcknowledgementOnlyWhenSetToDelete()
    {
        var rig = new Rig();
        rig.Rows.AddRange([Row(Photo("A.DNG", Jun1)), Row(Photo("B.DNG", Jun1), verified: false)]);
        using var vm = rig.Vm();
        await vm.OpenAsync();
        Assert.True(vm.VerifyEnabled);
        vm.Verify = true;
        vm.PickDate(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero));
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(new PhotoCleanupRequest(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), @"X:\Lightroom"), rig.Requests.Single());
        Assert.Equal([RowDecision.Delete, RowDecision.Keep], vm.Rows.Select(r => r.Decision));
        Assert.Equal("not found in Lightroom", vm.Rows[1].StatusText);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(PhotoCleanupStep.Confirm, vm.Step);
        Assert.False(vm.ShowUnverifiedAck);

        vm.BackCommand.Execute(null);
        vm.Rows[1].DeleteCommand.Execute(null);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.True(vm.ShowUnverifiedAck);
        Assert.Equal("Move 2 items (50 MB) from Picture Offload to the Recycle Bin", vm.AckMoveText);              // verify mode: no date-mode note
        Assert.Equal("1 of them are not confirmed in Lightroom or your library — the Recycle Bin may hold their only copy", vm.UnverifiedAckText);
        vm.AckMove = true;
        Assert.False(vm.RunCommand.CanExecute(null));
        vm.AckUnverified = true;
        Assert.True(vm.RunCommand.CanExecute(null));
        await vm.RunCommand.ExecuteAsync(null);
        Assert.True(rig.Confirmed!.Ack.UnverifiedIncluded);
        Assert.Equal(2, rig.Confirmed.Items.Length);
    }

    [Fact]
    public async Task Run_ConfirmsExactlyTheDeleteRows_SavesTheReport_AndShowsTheResult()
    {
        var rig = new Rig();
        rig.Rows.AddRange([Row(Photo("A.DNG", Jun1)), Row(Photo("B.DNG", Jun1))]);
        using var vm = rig.Vm();
        await vm.OpenAsync();
        vm.PickDate(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero));
        await vm.NextCommand.ExecuteAsync(null);
        vm.Rows[1].KeepCommand.Execute(null);
        Assert.Equal("1 photo, 0 sets, 25 MB to the Recycle Bin", vm.TotalsText);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal("Move 1 item (25 MB) from Picture Offload to the Recycle Bin — not checked against Lightroom", vm.AckMoveText);   // date mode
        Assert.False(vm.RunCommand.CanExecute(null));
        vm.AckMove = true;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(PhotoCleanupStep.Result, vm.Step);
        Assert.Equal(["A.DNG"], rig.Confirmed!.Items.Select(r => r.Key));
        Assert.Equal(["B.DNG"], rig.Confirmed.Kept.Select(r => r.Key));
        Assert.Equal("photos01", rig.Report!.RunId);
        Assert.Equal("Moved 1 item (1 file, 25 MB) to the Recycle Bin", vm.Result!.HeadlineText);
        Assert.Equal("moved 1 · kept 1 · skipped because changed 0 · failed 0", vm.Result.CountsText);
        vm.Result.OpenReportCommand.Execute(null);
        Assert.Equal(["file:" + ReportFile], rig.Shell.Opened);
        var closed = false;
        vm.Closed += () => closed = true;
        vm.DoneCommand.Execute(null);
        Assert.True(closed);
    }

    [Fact]
    public async Task ABlockedPreparation_ShowsItsReason_AndKeepOnDevicePreparesAgain()
    {
        var rig = new Rig();
        var pinned = 0;
        rig.Preparation = new PhotoCleanupPreparation(null, @"Set UAS Videos\.uas-sort to Always keep on this device", null, null)
        {
            KeepOnDevice = () => pinned++,
        };
        using var vm = rig.Vm();
        await vm.OpenAsync();
        Assert.Equal(@"Set UAS Videos\.uas-sort to Always keep on this device", vm.BlockingText);
        Assert.True(vm.CanKeepOnDevice);
        vm.PickDate(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero));
        Assert.False(vm.NextCommand.CanExecute(null));

        rig.Preparation = new PhotoCleanupPreparation(new PhotoSurvey(PhotoRoot, [], [], TestPlans.Ledger()), null, null, null);
        vm.KeepOnDeviceCommand.Execute(null);
        await Eventually.TrueAsync(() => vm.BlockingText is null && rig.Prepares == 2, rig.Ui);
        Assert.Equal(1, pinned);
    }

    [Fact]
    public async Task WithoutALightroomFolder_VerifyStaysOff_WithItsReason()
    {
        var rig = new Rig
        {
            Preparation = new(new PhotoSurvey(PhotoRoot, [], [], TestPlans.Ledger()), null,
                              "Set a Lightroom library folder in Settings to verify against Lightroom", null),
        };
        using var vm = rig.Vm();
        await vm.OpenAsync();
        Assert.False(vm.VerifyEnabled);
        vm.Verify = true;
        Assert.False(vm.Verify);
        Assert.Equal("Set a Lightroom library folder in Settings to verify against Lightroom", vm.VerifyUnavailableText);
    }

    [Theory]
    [InlineData(true, false, false, true, true, "Wait until the offload finishes")]
    [InlineData(false, false, true, true, true, "Wait until the card cleanup finishes")]
    [InlineData(false, true, false, true, true, "Wait until the scan finishes")]
    [InlineData(false, false, false, false, true, "Set up the photo folder first")]
    [InlineData(false, false, false, true, false, @"The photo folder C:\P is not available")]
    [InlineData(false, false, false, true, true, null)]
    public void Availability_FirstMatchingReason(bool commit, bool scan, bool cardCleanup, bool roots, bool available, string? reason)
        => Assert.Equal(reason, PhotoCleanupAvailability.Reason(new PhotoCleanupContext(commit, scan, cardCleanup, roots, @"C:\P", available)));
}
```

```csharp
// tests/UasSort.Review.Tests/PhotoCleanupEngineTests.cs
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Review.Tests;

public sealed class PhotoCleanupEngineTests
{
    private const string Lr = @"X:\Lightroom";
    private static readonly DateTime Shot = new(2026, 6, 1, 12, 10, 0);

    private sealed class Rig
    {
        public Rig()
        {
            Fs.AddDirectory(Lr);
            var ctx = FakeLayout.Context(cardRoot: null) with { LightroomFolder = Lr };
            Reader = new FakePhotoFileReader(Fs, ctx);
            Ledger = new FakeLedgerStore(Fs, FakeLayout.VideoRoot, FakeLayout.Machine);
            Recyclers = new FakePhotoRootRecyclerFactory(Fs, ctx);
        }

        public FakeFileSystem Fs { get; } = FakeLayout.NewFileSystem();
        public FakePhotoFileReader Reader { get; }
        public FakeLedgerStore Ledger { get; }
        public FakePhotoRootRecyclerFactory Recyclers { get; }
        public MemReportStore Reports { get; } = new();
        public Settings Settings { get; set; } = FakeLayout.Settings() with { LightroomFolder = Lr };

        public void Dng(string path, DateTime dto) => Fs.AddFile(path, new SyntheticDngBuilder().WithDateTimeOriginal(dto).Build(), Mtime);

        public PhotoCleanupEngine Engine(PhotoRootThumbnails? thumbnails = null) => PhotoCleanupEngines.Create(
            new PhotoCleanupPorts(Settings, PhotoRoot, Fs, Reader, Ledger,
                PhotoCaptureClock.For(Settings, new GeoTimeZoneResolver(), Zones.Find("America/Anchorage")),
                new PhotoCleanupEnvironment(Recyclers, Fs, Ledger, new FakeOffloadLock(), new FakePowerRequest(),
                                            new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 20, 0, 0, TimeSpan.Zero)), FakeLayout.Machine),
                p => Fs.Metadata(p) is { IsDirectory: true }, Reports, new FakeShellLauncher()) { Thumbnails = thumbnails });
    }

    private static Task<PhotoCleanupPreparation> Prepare(PhotoCleanupEngine e) => e.Prepare(new Progress<PhotoScanProgress>(), CancellationToken.None);

    [Fact]
    public async Task Engine_PreparesVerifiesRunsAndReports_EndToEnd()
    {
        var rig = new Rig();
        rig.Dng(PhotoRoot + @"\A.DNG", Shot);
        rig.Dng(PhotoRoot + @"\B.DNG", Shot.AddSeconds(30));
        rig.Dng(Lr + @"\2026\2026-06-01\Damian_20260601_001.dng", Shot);
        var thumbnails = new PhotoRootThumbnails(rig.Reader);
        var engine = rig.Engine(thumbnails);

        var prep = await Prepare(engine);
        Assert.Null(prep.BlockingText);
        Assert.Null(prep.VerifyUnavailableText);
        Assert.Equal(Lr, prep.LightroomFolder);
        Assert.Equal(["A.DNG", "B.DNG"], prep.Survey!.Items.Select(i => i.RelPath));
        Assert.False((await thumbnails.GetAsync(PhotoRootThumbnails.KeyOf(prep.Survey.Items[0]), CancellationToken.None)).IsEmpty);

        var plan = await engine.Plan(prep.Survey, new PhotoCleanupRequest(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), Lr),
                                     new Progress<PhotoScanProgress>(), CancellationToken.None);
        Assert.Equal(["A.DNG"], plan.DefaultDelete());
        var confirmed = plan.Confirm(new PhotoCleanupAck(plan.Fingerprint, plan.DefaultDelete(), true, false), TimeProvider.System);
        var result = await engine.Run(confirmed, new Progress<PhotoCleanupProgress>(), CancellationToken.None);

        Assert.IsType<PhotoRecycled>(Assert.Single(result.Outcomes));
        Assert.False(rig.Fs.Exists(PhotoRoot + @"\A.DNG"));
        Assert.True(rig.Fs.Exists(PhotoRoot + @"\B.DNG"));
        Assert.Equal("lightroom", Assert.IsType<PhotoDeleteRecord>(Assert.Single(rig.Ledger.Writer.Records)).Evidence);
        Assert.EndsWith("-photos.json", engine.SaveReport(PhotoCleanupReports.Build(result)), StringComparison.Ordinal);
        Assert.Single(rig.Reports.Photos);
        Assert.Empty(rig.Fs.HydrationViolations);
    }

    [Fact]
    public async Task Engine_Prepare_SaysWhyVerifyOrThePageIsUnavailable()
    {
        Assert.Equal("Set a Lightroom library folder in Settings to verify against Lightroom",
                     (await Prepare(new Rig { Settings = FakeLayout.Settings() }.Engine())).VerifyUnavailableText);
        Assert.Equal(@"The Lightroom folder Y:\Missing is not available",
                     (await Prepare(new Rig { Settings = FakeLayout.Settings() with { LightroomFolder = @"Y:\Missing" } }.Engine())).VerifyUnavailableText);

        var noRoot = new Rig();
        noRoot.Fs.RemoveUnguarded(PhotoRoot);
        Assert.Equal($"The photo folder {PhotoRoot} is not available", (await Prepare(noRoot.Engine())).BlockingText);

        var cloud = new Rig();
        cloud.Ledger.StatusOverride = new LedgerFolderStatus(LedgerPaths.For(FakeLayout.VideoRoot), LedgerFolderState.CloudOnly, true, true, false,
                                                             true, [], [], []);
        var prep = await Prepare(cloud.Engine());
        Assert.StartsWith("Set ", prep.BlockingText, StringComparison.Ordinal);
        Assert.NotNull(prep.KeepOnDevice);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-class "*PhotoCleanupVmTests" --filter-class "*PhotoCleanupEngineTests"`
Expected: build fails (`CS0246: … 'PhotoCleanupVm' could not be found`, `'PhotoRootThumbnails' could not be found`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Cleanup/Photos/PhotoRootThumbnails.cs
namespace UasSort.Core.Cleanup;

/// <summary>Thumbnails of Picture Offload rows for the existing ThumbnailCache (spec 2026-10-04 §2), under "photo-root:&lt;primary rel
/// path&gt;" keys: a DNG's embedded IFD0 JPEG, or a JPEG of at most 16 MB itself. A cloud-only file is never opened: empty bytes, so the
/// placeholder shows.</summary>
public sealed class PhotoRootThumbnails(IPhotoFileReader reader) : IThumbnailSource
{
    public const string KeyPrefix = "photo-root:";
    public const long MaxWholeJpegBytes = 16_000_000;

    private readonly Lock _gate = new();
    private ImmutableDictionary<string, (string FullPath, uint Attributes, long Size)> _files =
        ImmutableDictionary<string, (string FullPath, uint Attributes, long Size)>.Empty;
    private int _pauses;

    public static ItemId KeyOf(PhotoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new ItemId(KeyPrefix + item.Primary.RelPath);
    }

    public static bool IsKey(ItemId id) => id.CardRelPath.StartsWith(KeyPrefix, StringComparison.Ordinal);

    public void Use(string photoRoot, IEnumerable<PhotoItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var b = ImmutableDictionary.CreateBuilder<string, (string FullPath, uint Attributes, long Size)>(StringComparer.Ordinal);
        foreach (var i in items) b[KeyOf(i).CardRelPath] = (PhotoCleanupPaths.Full(photoRoot, i.Primary.RelPath), i.Primary.Attributes, i.Primary.Size);
        lock (_gate) _files = b.ToImmutable();
    }

    public ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct)
    {
        (string FullPath, uint Attributes, long Size) file;
        lock (_gate)
        {
            if (_pauses > 0 || !_files.TryGetValue(id.CardRelPath, out file)) return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
        }
        return ValueTask.FromResult(PhotoCleanupRules.IsCloudOnly(file.Attributes) ? ReadOnlyMemory<byte>.Empty : Read(file.FullPath, file.Size));
    }

    public IDisposable Pause()
    {
        lock (_gate) _pauses++;
        return new Resume(this);
    }

    private ReadOnlyMemory<byte> Read(string fullPath, long size)
    {
        try
        {
            using var s = reader.OpenRead(fullPath);
            if (PhotoCleanupRules.IsJpg(fullPath))
            {
                if (size > MaxWholeJpegBytes) return ReadOnlyMemory<byte>.Empty;
                var whole = new byte[s.Length];
                s.ReadExactly(whole);
                return whole;
            }
            if (StillProbe.Read(s).Thumb is not { } range || range.Offset + range.Length > s.Length) return ReadOnlyMemory<byte>.Empty;
            var buffer = new byte[range.Length];
            s.Position = range.Offset;
            s.ReadExactly(buffer);
            return buffer;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeIoException or MetadataExtractor.ImageProcessingException)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    private sealed class Resume(PhotoRootThumbnails owner) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            lock (owner._gate) owner._pauses--;
        }
    }
}
```

```csharp
// src/UasSort.Review/PhotoCleanup/PhotoCleanupAvailability.cs
namespace UasSort.Review;

public sealed record PhotoCleanupContext(bool CommitRunning, bool ScanRunning, bool CardCleanupOpen, bool RootsConfirmed, string PhotoRoot,
                                         bool PhotoRootAvailable);

/// <summary>When [Clean up Picture Offload…] is enabled (spec 2026-10-04 §2): no offload, card cleanup or scan running, and the photo root
/// set and available; no card is needed. The reason is shown as visible text, first matching row wins (as Task U4 for card cleanup).</summary>
public static class PhotoCleanupAvailability
{
    public static string? Reason(PhotoCleanupContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.CommitRunning) return "Wait until the offload finishes";
        if (c.CardCleanupOpen) return "Wait until the card cleanup finishes";
        if (c.ScanRunning) return "Wait until the scan finishes";
        if (!c.RootsConfirmed) return "Set up the photo folder first";
        if (!c.PhotoRootAvailable) return $"The photo folder {c.PhotoRoot} is not available";
        return null;
    }
}
```

```csharp
// src/UasSort.Review/PhotoCleanup/PhotoCleanupEngine.cs
namespace UasSort.Review;

/// <summary>What the page opens with: the survey, or why the page is blocked (with [Keep on this device] for a cloud-only ledger
/// folder), and why verify mode is unavailable.</summary>
public sealed record PhotoCleanupPreparation(PhotoSurvey? Survey, string? BlockingText, string? VerifyUnavailableText, string? LightroomFolder)
{
    public Action? KeepOnDevice { get; init; }
}

/// <summary>What PhotoCleanupVm calls outside Review. Created by <see cref="PhotoCleanupEngines.Create"/> (Part P.14 composes it).</summary>
public sealed record PhotoCleanupEngine(
    Func<IProgress<PhotoScanProgress>, CancellationToken, Task<PhotoCleanupPreparation>> Prepare,
    Func<PhotoSurvey, PhotoCleanupRequest, IProgress<PhotoScanProgress>, CancellationToken, Task<PhotoCleanupPlan>> Plan,
    Func<ConfirmedPhotoCleanupPlan, IProgress<PhotoCleanupProgress>, CancellationToken, Task<PhotoCleanupResult>> Run,
    Func<PhotoCleanupReport, string> SaveReport,
    IShellLauncher Shell);

/// <summary>PhotoRoot and Settings.LightroomFolder are canonical (IPathFacts.Canonical, Task P.14), so the plan's paths match the recycler's
/// guard context and the Lightroom listing, its reads and the reader's guard all use the same form of the folder. Clock dates Picture
/// Offload files without a ledger record as the scan would (resolved ambiguity 8).</summary>
public sealed record PhotoCleanupPorts(Settings Settings, string PhotoRoot, IDirectoryLister Lister, IPhotoFileReader Reader, ILedgerStore Ledger,
                                       PhotoCaptureClock Clock, PhotoCleanupEnvironment Environment, Func<string, bool> FolderExists,
                                       IReportStore Reports, IShellLauncher Shell)
{
    public PhotoRootThumbnails? Thumbnails { get; init; }
}

public static class PhotoCleanupEngines
{
    public static PhotoCleanupEngine Create(PhotoCleanupPorts p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var exif = new PhotoExifCache(p.Reader);
        return new PhotoCleanupEngine(
            (progress, ct) => Task.Run(() => Prepare(p, exif, progress, ct), ct),
            (survey, request, progress, ct) => Task.Run(() => Plan(p, exif, survey, request, progress, ct), ct),
            (confirmed, progress, ct) => PhotoCleanupExecutor.RunAsync(confirmed, p.Environment, progress, ct),
            p.Reports.Save,
            p.Shell);
    }

    /// <summary>Ledger gate (Ref §7.5: a cloud-only or unwritable history blocks up front), the photo root's availability and full listing
    /// (an incomplete listing blocks), then the survey; and whether verify mode is available.</summary>
    internal static PhotoCleanupPreparation Prepare(PhotoCleanupPorts p, PhotoExifCache exif, IProgress<PhotoScanProgress> progress, CancellationToken ct)
    {
        var (ledger, refused) = CleanupLedgerGate.Load(p.Ledger, p.Settings);
        if (refused is not null)
            return new PhotoCleanupPreparation(null, refused.BlockingText, null, null) { KeepOnDevice = refused.KeepOnDevice };
        if (!p.FolderExists(p.PhotoRoot)) return new PhotoCleanupPreparation(null, $"The photo folder {p.PhotoRoot} is not available", null, null);
        var listing = p.Lister.Enumerate(p.PhotoRoot, recurse: true, PhotoCleanupPlanner.Excludes);
        if (!listing.Errors.IsEmpty)
        {
            var (path, code) = listing.Errors[0];
            return new PhotoCleanupPreparation(null, string.Create(CultureInfo.InvariantCulture,
                $"Part of {p.PhotoRoot} couldn't be listed ({path}: Win32 error {code}); nothing will be moved"), null, null);
        }
        var survey = PhotoCleanupPlanner.Survey(p.PhotoRoot, listing, ledger!, exif, p.Clock, progress, ct);
        p.Thumbnails?.Use(p.PhotoRoot, survey.Items);
        var lightroom = p.Settings.LightroomFolder;
        var verify = lightroom is null ? "Set a Lightroom library folder in Settings to verify against Lightroom"
                   : !p.FolderExists(lightroom) ? $"The Lightroom folder {lightroom} is not available"
                   : null;
        return new PhotoCleanupPreparation(survey, null, verify, verify is null ? lightroom : null);
    }

    internal static PhotoCleanupPlan Plan(PhotoCleanupPorts p, PhotoExifCache exif, PhotoSurvey survey, PhotoCleanupRequest request,
                                          IProgress<PhotoScanProgress> progress, CancellationToken ct)
    {
        LightroomIndex? index = null;
        if (request.Mode == PhotoCleanupMode.Verify)
        {
            var folder = request.LightroomFolder ?? throw new InvalidOperationException("Verify mode needs the Lightroom folder");
            index = PhotoCleanupPlanner.VerifyRange(survey, request.Cutoff) is { } range
                ? LightroomIndex.Build(folder, p.Lister, exif, range.From, range.To, progress, ct)
                : LightroomIndex.Empty(folder);
        }
        return PhotoCleanupPlanner.Build(survey, request, index, exif, progress, ct);
    }
}
```

```csharp
// src/UasSort.Review/PhotoCleanup/PhotoCleanupRowVm.cs
namespace UasSort.Review;

/// <summary>One review row (spec 2026-10-04 §2 step 2): a photo unit or a set folder, with its Keep/Delete toggle.</summary>
public sealed partial class PhotoCleanupRowVm : ObservableObject, IKeyed
{
    public PhotoCleanupRowVm(PhotoRow row, Action<PhotoCleanupRowVm, RowDecision> set)
    {
        ArgumentNullException.ThrowIfNull(set);
        Row = row;
        KeepCommand = new RelayCommand(() => set(this, RowDecision.Keep));
        DeleteCommand = new RelayCommand(() => set(this, RowDecision.Delete));
    }

    public PhotoRow Row { get; private set; }
    public string Key => Row.Key;
    public ItemId ThumbKey => PhotoRootThumbnails.KeyOf(Row.Item);

    [ObservableProperty] public partial string? DayHeader { get; private set; }
    [ObservableProperty] public partial string Title { get; private set; } = "";
    [ObservableProperty] public partial string DetailText { get; private set; } = "";
    [ObservableProperty] public partial string StatusText { get; private set; } = "";
    [ObservableProperty] public partial bool IsVerified { get; private set; }
    [ObservableProperty] public partial RowDecision Decision { get; private set; }

    public IRelayCommand KeepCommand { get; }
    public IRelayCommand DeleteCommand { get; }

    internal void Update(PhotoRow row, RowDecision decision, string? dayHeader)
    {
        Row = row;
        var item = row.Item;
        Title = item.Kind == PhotoItemKind.Photo
            ? item.Members.Length > 1 ? $"{item.RelPath} + JPG" : item.RelPath
            : $"{KindText(item.SetKind)} · {item.RelPath}";
        var dates = item.FirstDate is { } a && item.LastDate is { } b ? Fmt.DateRange(a, b) : "date unknown";
        DetailText = item.Kind == PhotoItemKind.Photo
            ? $"{dates} · {Fmt.Size(item.Bytes)}"
            : $"{dates} · {Fmt.Count(item.Members.Length, "frame", "frames")} · {Fmt.Size(item.Bytes)}";
        StatusText = row.Verification.Text;
        IsVerified = row.Verification.Verified;
        Decision = decision;
        DayHeader = dayHeader;
    }

    private static string KindText(PhotoSetKind k) => k switch
    {
        PhotoSetKind.Panorama => "Panorama",
        PhotoSetKind.Hyperlapse => "Hyperlapse",
        _ => "Set",
    };

    public override string ToString() => $"{Title} · {DetailText} · {StatusText}";
}
```

```csharp
// src/UasSort.Review/PhotoCleanup/PhotoCleanupResultVm.cs
namespace UasSort.Review;

/// <summary>The result page (spec 2026-10-04 §2 step 3): moved / kept / skipped-because-changed / failed, the Recycle Bin hint and the report.</summary>
public sealed class PhotoCleanupResultVm
{
    public PhotoCleanupResultVm(PhotoCleanupResult r, string reportPath, string? reportProblem, IShellLauncher shell)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(shell);
        var moved = r.Outcomes.OfType<PhotoRecycled>().ToList();
        var files = moved.Sum(m => m.Files) + r.Outcomes.OfType<PhotoPartlyRecycled>().Sum(p => p.Recycled.Length);
        var bytes = moved.Sum(m => m.Bytes);
        var skipped = r.Outcomes.Count(o => o is PhotoSkippedChanged);
        var failed = r.Outcomes.Count(o => o is PhotoRecycleFailed or PhotoPartlyRecycled);
        HeadlineText = $"Moved {Fmt.Count(moved.Count, "item", "items")} ({Fmt.Count(files, "file", "files")}, {Fmt.Size(bytes)}) to the Recycle Bin";
        CountsText = string.Create(CultureInfo.InvariantCulture,
            $"moved {moved.Count} · kept {r.Plan.Kept.Length} · skipped because changed {skipped} · failed {failed}");
        Problems = [.. r.Outcomes.Select(Problem).OfType<string>()];
        StopText = r.Stop switch
        {
            null => null,
            PhotoCleanupStop.OffloadLockHeld => "Another uas-sort window is offloading or cleaning up; nothing was moved.",
            PhotoCleanupStop.LedgerUnavailable => "The history folder can't be written; nothing was moved.",
            PhotoCleanupStop.RecyclerRefused => "The safety check refused the cleanup; nothing was moved.",
            PhotoCleanupStop.Cancelled => "Stopped after the current item.",
            PhotoCleanupStop.LedgerWriteFailed => "Recording a move in the history failed; stopped. The report names the file.",
            PhotoCleanupStop.InternalSafetyStop => "Internal safety stop; stopped.",
            _ => r.Stop.ToString(),
        };
        LedgerWarning = r.Unrecorded.IsEmpty ? null
            : $"{Fmt.Count(r.Unrecorded.Length, "moved file", "moved files")} couldn't be recorded in the history; a card that still holds them may show them as New. The report lists them.";
        ReportPath = reportPath;
        ReportProblem = reportProblem;
        OpenReportCommand = new RelayCommand(() => shell.OpenFile(ReportPath), () => ReportPath.Length > 0);
    }

    public string HeadlineText { get; }
    public string CountsText { get; }
    public IReadOnlyList<string> Problems { get; }
    public string? StopText { get; }
    public string? LedgerWarning { get; }
    public string ReportPath { get; }
    public string? ReportProblem { get; }
    public IRelayCommand OpenReportCommand { get; }

    public string RecycleBinText { get; } =
        "Moved items are in the Windows Recycle Bin until it is emptied (OneDrive also keeps deleted files in its own recycle bin); restore them from there if you need them.";

    private static string? Problem(PhotoCleanupOutcome o) => o switch
    {
        PhotoRecycled => null,
        PhotoSkippedChanged s => $"{s.Item}: skipped, {s.Why}",
        PhotoPartlyRecycled p => $"{p.Item}: partly moved ({p.Why}); still in Picture Offload: {string.Join(", ", p.Left)}",
        PhotoRecycleFailed f => f.NotRecyclable ? $"{f.Item}: kept — {f.Error}" : $"{f.Item}: not moved ({f.Error})",
        PhotoNotStarted => null,
    };
}
```

```csharp
// src/UasSort.Review/PhotoCleanup/PhotoCleanupVm.cs
namespace UasSort.Review;

public enum PhotoCleanupStep { Choose, Review, Confirm, Running, Result }

/// <summary>The "Clean up Picture Offload" page (spec 2026-10-04 §2): Choose (cutoff; verify against Lightroom) → Review (rows by day,
/// Keep/Delete) → Confirm (the move acknowledgement; a second one when an unverified row is on Delete) → Running (cancellable between
/// items) → Result.</summary>
public sealed partial class PhotoCleanupVm : ObservableObject, IDisposable
{
    private readonly PhotoCleanupEngine _engine;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _life = new();
    private readonly HashSet<string> _delete = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _run;
    private PhotoSurvey? _survey;
    private string? _lightroomFolder;
    private Action? _keepOnDevice;

    public PhotoCleanupVm(PhotoCleanupEngine engine, IDialogService dialogs, IUiDispatcher ui, TimeProvider time)
    {
        _engine = engine;
        _dialogs = dialogs;
        _ui = ui;
        _time = time;
        NextCommand = new AsyncRelayCommand(NextAsync, () => CanNext);
        BackCommand = new RelayCommand(Back, () => Step is PhotoCleanupStep.Choose or PhotoCleanupStep.Review or PhotoCleanupStep.Confirm);
        DeleteAllCommand = new RelayCommand(() => SetAll(delete: true), () => Step == PhotoCleanupStep.Review);
        KeepAllCommand = new RelayCommand(() => SetAll(delete: false), () => Step == PhotoCleanupStep.Review);
        RunCommand = new AsyncRelayCommand(RunAsync, () => CanRun);
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => Step == PhotoCleanupStep.Running);
        DoneCommand = new RelayCommand(() => Closed?.Invoke(), () => Step == PhotoCleanupStep.Result);
        KeepOnDeviceCommand = new RelayCommand(KeepOnDevice, () => CanKeepOnDevice);
    }

    public ObservableCollection<PhotoCleanupRowVm> Rows { get; } = [];

    [ObservableProperty] public partial PhotoCleanupStep Step { get; private set; }
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string? BusyText { get; private set; }
    [ObservableProperty] public partial string? BlockingText { get; private set; }
    [ObservableProperty] public partial bool CanKeepOnDevice { get; private set; }
    [ObservableProperty] public partial string PhotoRootText { get; private set; } = "";
    [ObservableProperty] public partial DateTimeOffset? PickedDate { get; set; }
    [ObservableProperty] public partial DateOnly? Cutoff { get; private set; }
    [ObservableProperty] public partial bool Verify { get; set; }
    [ObservableProperty] public partial bool VerifyEnabled { get; private set; }
    [ObservableProperty] public partial string? VerifyUnavailableText { get; private set; }
    [ObservableProperty] public partial PhotoCleanupPlan? Plan { get; private set; }
    [ObservableProperty] public partial string ModeText { get; private set; } = "";
    [ObservableProperty] public partial string TotalsText { get; private set; } = "";
    [ObservableProperty] public partial string? NotTouchedText { get; private set; }
    [ObservableProperty] public partial string? NotEligibleText { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<string> NotEligibleLines { get; private set; } = [];
    [ObservableProperty] public partial string AckMoveText { get; private set; } = "";
    [ObservableProperty] public partial bool AckMove { get; set; }
    [ObservableProperty] public partial bool ShowUnverifiedAck { get; private set; }
    [ObservableProperty] public partial string UnverifiedAckText { get; private set; } = "";
    [ObservableProperty] public partial bool AckUnverified { get; set; }
    [ObservableProperty] public partial bool CanNext { get; private set; }
    [ObservableProperty] public partial bool CanRun { get; private set; }
    [ObservableProperty] public partial string RunButtonText { get; private set; } = "Move to the Recycle Bin";
    [ObservableProperty] public partial string ProgressText { get; private set; } = "";
    [ObservableProperty] public partial PhotoCleanupResultVm? Result { get; private set; }

    public IAsyncRelayCommand NextCommand { get; }
    public IRelayCommand BackCommand { get; }
    public IRelayCommand DeleteAllCommand { get; }
    public IRelayCommand KeepAllCommand { get; }
    public IAsyncRelayCommand RunCommand { get; }
    public IAsyncRelayCommand CancelCommand { get; }
    public IRelayCommand DoneCommand { get; }
    public IRelayCommand KeepOnDeviceCommand { get; }

    public event Action? Closed;

    /// <summary>Prepares the page off the UI thread (ledger gate, listing, survey). Expected failures become BlockingText.</summary>
    public async Task OpenAsync()
    {
        IsBusy = true;
        BusyText = "Reading Picture Offload…";
        BlockingText = null;
        UpdateGates();
        var progress = new UiProgress<PhotoScanProgress>(_ui, p => { if (IsBusy) BusyText = ProgressLine(p); });
        PhotoCleanupPreparation prep;
        try
        {
            prep = await _engine.Prepare(progress, _life.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeIoException)
        {
            BlockingText = $"Picture Offload couldn't be read: {e.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
            UpdateGates();
        }
        _survey = prep.Survey;
        _lightroomFolder = prep.LightroomFolder;
        _keepOnDevice = prep.KeepOnDevice;
        CanKeepOnDevice = prep.KeepOnDevice is not null;
        BlockingText = prep.BlockingText;
        VerifyEnabled = prep.Survey is not null && prep.VerifyUnavailableText is null;
        VerifyUnavailableText = prep.VerifyUnavailableText;
        if (!VerifyEnabled) Verify = false;
        PhotoRootText = prep.Survey is { } s
            ? $"{s.PhotoRoot} · {Fmt.Count(s.Items.Length, "item", "items")}"
              + (s.NotTouched.IsEmpty ? "" : $" · {Fmt.Count(s.NotTouched.Length, "other file or folder", "other files and folders")} not touched")
            : "";
        UpdateGates();
    }

    /// <summary>The cutoff is the picker's own calendar day, never converted through UtcDateTime or ToLocalTime (as CleanupVm.PickDate).</summary>
    public void PickDate(DateTimeOffset? picked) => PickedDate = picked;

    public void Dispose()
    {
        _life.Cancel();
        _life.Dispose();
        _run?.Dispose();
    }

    partial void OnPickedDateChanged(DateTimeOffset? value)
    {
        Cutoff = value is { } v ? DateOnly.FromDateTime(v.DateTime) : null;
        UpdateGates();
    }

    partial void OnVerifyChanged(bool value)
    {
        if (value && !VerifyEnabled)
        {
            Verify = false;
            return;
        }
        UpdateGates();
    }

    partial void OnAckMoveChanged(bool value) => UpdateGates();
    partial void OnAckUnverifiedChanged(bool value) => UpdateGates();
    partial void OnStepChanged(PhotoCleanupStep value) => UpdateGates();

    private async Task NextAsync()
    {
        switch (Step)
        {
            case PhotoCleanupStep.Choose:
                await BuildPlanAsync().ConfigureAwait(true);
                break;
            case PhotoCleanupStep.Review:
                AckMove = false;
                AckUnverified = false;
                Step = PhotoCleanupStep.Confirm;
                UpdateTexts();
                break;
        }
    }

    private async Task BuildPlanAsync()
    {
        if (_survey is not { } survey || Cutoff is not { } cutoff) return;
        var request = new PhotoCleanupRequest(Verify ? PhotoCleanupMode.Verify : PhotoCleanupMode.BeforeDate, cutoff, Verify ? _lightroomFolder : null);
        IsBusy = true;
        BusyText = Verify ? "Checking the Lightroom library…" : "Building the list…";
        UpdateGates();
        var progress = new UiProgress<PhotoScanProgress>(_ui, p => { if (IsBusy) BusyText = ProgressLine(p); });
        PhotoCleanupPlan plan;
        try
        {
            plan = await _engine.Plan(survey, request, progress, _life.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeIoException)
        {
            BlockingText = $"The list couldn't be built: {e.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
            UpdateGates();
        }
        Plan = plan;
        _delete.Clear();
        _delete.UnionWith(plan.DefaultDelete());
        Step = PhotoCleanupStep.Review;
        Sync();
        UpdateTexts();
        UpdateGates();
    }

    private void SetRow(PhotoCleanupRowVm row, RowDecision decision)
    {
        if (Step != PhotoCleanupStep.Review) return;
        if (decision == RowDecision.Delete) _delete.Add(row.Key);
        else _delete.Remove(row.Key);
        Sync();
        UpdateTexts();
        UpdateGates();
    }

    /// <summary>[Delete all] puts every row on Delete (unverified ones then need the second acknowledgement); [Keep all] none.</summary>
    private void SetAll(bool delete)
    {
        if (Plan is not { } p) return;
        _delete.Clear();
        if (delete) _delete.UnionWith(p.Rows.Select(r => r.Key));
        Sync();
        UpdateTexts();
        UpdateGates();
    }

    /// <summary>Rows in plan order (oldest first); the first row of each local date carries the day header.</summary>
    private void Sync()
    {
        IReadOnlyList<PhotoRow> rows = Plan is { } p ? p.Rows : [];
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        DateOnly? last = null;
        foreach (var r in rows)
        {
            var day = r.Item.FirstDate;
            headers[r.Key] = day is { } d && d != last ? Fmt.DayWithWeekday(d) : null;
            last = day;
        }
        CollectionSync.Sync(Rows, rows, r => r.Key, r => new PhotoCleanupRowVm(r, SetRow),
            (vm, r) => vm.Update(r, _delete.Contains(r.Key) ? RowDecision.Delete : RowDecision.Keep, headers[r.Key]));
        NotEligibleLines = Plan is { } q ? [.. q.NotEligible.Select(r => $"{r.Key} · {r.Why}")] : [];
    }

    private void UpdateTexts()
    {
        if (Plan is not { } p)
        {
            ModeText = "";
            TotalsText = "";
            NotTouchedText = null;
            NotEligibleText = null;
            AckMoveText = "";
            ShowUnverifiedAck = false;
            return;
        }
        var t = p.Totals(_delete);
        var items = t.Photos + t.Sets;
        ModeText = p.Request.Mode == PhotoCleanupMode.BeforeDate
            ? "By date: rows are not checked against Lightroom."
            : string.Create(CultureInfo.InvariantCulture,
                $"Checked against {p.Request.LightroomFolder}: {p.Rows.Count(r => r.Verification.Verified)} of {Fmt.Count(p.Rows.Length, "row", "rows")} confirmed.");
        TotalsText = $"{Fmt.Count(t.Photos, "photo", "photos")}, {Fmt.Count(t.Sets, "set", "sets")}, {Fmt.Size(t.Bytes)} to the Recycle Bin";
        NotTouchedText = p.NotTouched.IsEmpty ? null
            : $"Not touched: {Fmt.Count(p.NotTouched.Length, "other file or folder", "other files and folders")} in Picture Offload";
        NotEligibleText = p.NotEligible.IsEmpty ? null : $"Not eligible (kept): {Fmt.Count(p.NotEligible.Length, "item", "items")}";
        AckMoveText = $"Move {Fmt.Count(items, "item", "items")} ({Fmt.Size(t.Bytes)}) from Picture Offload to the Recycle Bin"
                      + (p.Request.Mode == PhotoCleanupMode.BeforeDate ? " — not checked against Lightroom" : "");   // resolved ambiguity 8b
        ShowUnverifiedAck = t.Unverified > 0;
        UnverifiedAckText = string.Create(CultureInfo.InvariantCulture,
            $"{t.Unverified} of them are not confirmed in Lightroom or your library — the Recycle Bin may hold their only copy");
        RunButtonText = $"Move {Fmt.Count(items, "item", "items")} to the Recycle Bin";
        if (!ShowUnverifiedAck) AckUnverified = false;
    }

    private void UpdateGates()
    {
        var ready = !IsBusy && BlockingText is null;
        CanNext = Step switch
        {
            PhotoCleanupStep.Choose => ready && _survey is not null && Cutoff is not null && (!Verify || VerifyEnabled),
            PhotoCleanupStep.Review => ready && Plan is not null && _delete.Count > 0,
            _ => false,
        };
        CanRun = ready && Step == PhotoCleanupStep.Confirm && Plan is not null && _delete.Count > 0 && AckMove && (!ShowUnverifiedAck || AckUnverified);
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        DeleteAllCommand.NotifyCanExecuteChanged();
        KeepAllCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        DoneCommand.NotifyCanExecuteChanged();
        KeepOnDeviceCommand.NotifyCanExecuteChanged();
    }

    private void Back()
    {
        switch (Step)
        {
            case PhotoCleanupStep.Confirm:
                Step = PhotoCleanupStep.Review;
                break;
            case PhotoCleanupStep.Review:
                Step = PhotoCleanupStep.Choose;
                break;
            default:
                Closed?.Invoke();
                break;
        }
    }

    private async Task RunAsync()
    {
        if (!CanRun || Plan is not { } plan) return;
        ConfirmedPhotoCleanupPlan confirmed;
        try
        {
            confirmed = plan.Confirm(new PhotoCleanupAck(plan.Fingerprint, _delete.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase), AckMove,
                                                         ShowUnverifiedAck && AckUnverified), _time);
        }
        catch (InvalidOperationException e)
        {
            BlockingText = e.Message;
            return;
        }
        _run = new CancellationTokenSource();
        Step = PhotoCleanupStep.Running;
        ProgressText = "Starting…";
        var progress = new UiProgress<PhotoCleanupProgress>(_ui, p => ProgressText = string.Create(CultureInfo.InvariantCulture,
            $"{p.ItemsDone} / {Fmt.Count(p.ItemsTotal, "item", "items")} · {Fmt.Size(p.BytesDone)} of {Fmt.Size(p.BytesTotal)}{(p.Current is null ? "" : " · " + p.Current)}"));
        PhotoCleanupResult result;
#pragma warning disable CA1031 // the page must never stay stuck on Running after items may have moved; every failure is shown
        try
        {
            result = await _engine.Run(confirmed, progress, _run.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            BlockingText = $"The cleanup stopped unexpectedly: {ex.Message}. Items already moved are in the Recycle Bin and recorded in the history; open the page again to see what is left.";
            Step = PhotoCleanupStep.Review;
            return;
        }
        string reportPath;
        string? reportProblem = null;
        try
        {
            reportPath = _engine.SaveReport(PhotoCleanupReports.Build(result));
        }
        catch (Exception ex)
        {
            reportPath = "";
            reportProblem = $"The report couldn't be saved: {ex.Message}";
        }
#pragma warning restore CA1031
        Result = new PhotoCleanupResultVm(result, reportPath, reportProblem, _engine.Shell);
        Step = PhotoCleanupStep.Result;
    }

    private async Task CancelAsync()
    {
        var answer = await _dialogs.ShowAsync(new DialogRequest("Stop the cleanup?",
            "Stop after the current item? Items already moved stay in the Recycle Bin.", "Stop", null, "Keep going")).ConfigureAwait(true);
        if (answer == DialogResult.Primary && _run is { } run) await run.CancelAsync().ConfigureAwait(true);
    }

    /// <summary>[Keep on this device] on a cloud-only ledger folder refusal: pin it, then prepare again.</summary>
    private void KeepOnDevice()
    {
        if (_keepOnDevice is not { } keep) return;
        try
        {
            keep();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeIoException)
        {
            BlockingText = $"Keeping the history folder on this device failed: {e.Message}";
            return;
        }
        Observed.Forget(OpenAsync(), e => BlockingText = e.Message);
    }

    private static string ProgressLine(PhotoScanProgress p) => string.Create(CultureInfo.InvariantCulture, $"{p.Phase}… {p.Done} / {p.Total}");
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PhotoRootThumbnailsTests"`
Expected: PASS.
Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-class "*PhotoCleanupVmTests" --filter-class "*PhotoCleanupEngineTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/Photos/PhotoRootThumbnails.cs src/UasSort.Review/PhotoCleanup tests/UasSort.Core.Tests/Cleanup/Photos/PhotoRootThumbnailsTests.cs tests/UasSort.Review.Tests/PhotoCleanupVmTests.cs tests/UasSort.Review.Tests/PhotoCleanupEngineTests.cs
git commit -F - <<'EOF'
feat: add the Picture Offload cleanup view model, engine and thumbnails

<session trailer>
EOF
```

---
### Task P.13: Shell stage, title-bar command and the Lightroom setting in the Settings VM

**Files:**
- Modify: `src/UasSort.Review/Shell/ShellVm.cs` (`Stage`, `ShellDeps`, `ShellVm` members, `RescanAsync`, `OpenSettings`, `CloseSettings`, `UpdateFlags`)
- Modify: `src/UasSort.Review/Settings/SettingsPageVm.cs` (Lightroom folder members, `RecheckLightroom` after a root change, `FlushPendingSave`)
- Modify: `tests/UasSort.Review.Tests/ShellVmTests.cs` (`Rig` + five tests)
- Test: `tests/UasSort.Review.Tests/LightroomSettingsTests.cs`

**Interfaces:**
- Consumes: `PhotoCleanupVm`, `PhotoCleanupAvailability`, `PhotoCleanupContext` (P.12); `LightroomRules.FolderRefusal` (P.1); `Settings.LightroomFolder` (P.1).
- Produces:
  - `Stage.PhotoCleanup` (appended last).
  - `ShellDeps.CreatePhotoCleanup { get; init; }` : `Func<Settings, PhotoCleanupVm>?`; `ShellDeps.FolderExists { get; init; }` : `Func<string, bool>?`.
  - `ShellVm.PhotoCleanup` (PhotoCleanupVm?), `ShellVm.PhotoCleanupEnabled` (bool, observable), `ShellVm.PhotoCleanupUnavailableText` (string?, observable), `ShellVm.PhotoCleanupCommand` (IRelayCommand), `void ShellVm.OpenPhotoCleanup()`.
  - `SettingsPageVm.LightroomFolder` (string?, observable), `SettingsPageVm.LightroomError` (string?, observable), `string SettingsPageVm.LightroomText`, `IRelayCommand SettingsPageVm.ClearLightroomCommand`, `void SettingsPageVm.ChangeLightroomFolder(string folder)`, `internal void SettingsPageVm.FlushPendingSave()`; a photo- or video-root change re-runs `LightroomRules.FolderRefusal` and clears an invalid Lightroom folder with the reason in `LightroomError`.
  - On the Settings stage, `OpenPhotoCleanup` and the availability use the Settings page's `Current` (flushed to the store first), not the stale `ShellVm.Settings`.

- [ ] **Step 1: Write the failing tests**

In `tests/UasSort.Review.Tests/ShellVmTests.cs`, inside `Rig`, add:

```csharp
        public bool PhotoRootExists { get; set; } = true;
        public int PhotoCleanupOpens { get; private set; }
        public Settings? PhotoCleanupSettings { get; private set; }

        private PhotoCleanupVm CreatePhotoCleanup(Settings s)
        {
            PhotoCleanupOpens++;
            PhotoCleanupSettings = s;
            var engine = new PhotoCleanupEngine(
                (p, ct) => Task.FromResult(new PhotoCleanupPreparation(new PhotoSurvey(s.PhotoRoot, [], [], TestPlans.Ledger()), null,
                    s.LightroomFolder is null ? "Set a Lightroom library folder in Settings to verify against Lightroom" : null, s.LightroomFolder)),
                (survey, request, p, ct) => throw new InvalidOperationException("not reached"),
                (confirmed, p, ct) => throw new InvalidOperationException("not reached"),
                r => "",
                new FakeShellLauncher());
            return new PhotoCleanupVm(engine, new FakeDialogService(), Ui, new FakeTimeProvider());
        }
```

and in `Rig.Shell`, change the `ShellDeps` object initializer `{ Log = Log }` to:

```csharp
                Saved.Add) { Log = Log, CreatePhotoCleanup = CreatePhotoCleanup, FolderExists = _ => PhotoRootExists });
```

and expose the settings store the Settings page saves to: add `public FakeSettingsStore? Store { get; private set; }` to `Rig`, and in `Rig.Shell` change `var store = new FakeSettingsStore(load);` to `var store = Store = new FakeSettingsStore(load);`.

Add the tests to the class:

```csharp
    [Fact]
    public async Task PhotoCleanup_OpensFromReview_DisablesTheOtherCommands_AndBackReturnsToReview()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);
        Assert.True(shell.PhotoCleanupEnabled);
        Assert.Null(shell.PhotoCleanupUnavailableText);
        var review = shell.Current;

        shell.PhotoCleanupCommand.Execute(null);

        Assert.Equal(Stage.PhotoCleanup, shell.Stage);
        var vm = Assert.IsType<PhotoCleanupVm>(shell.Current);
        Assert.Same(vm, shell.PhotoCleanup);
        Assert.False(shell.CanRescan);
        Assert.False(shell.CanOpenSettings);
        Assert.False(shell.CleanupEnabled);
        Assert.False(shell.PhotoCleanupEnabled);
        await Eventually.TrueAsync(() => !vm.IsBusy, rig.Ui);
        vm.BackCommand.Execute(null);                                    // Back on Choose closes the page
        Assert.Equal(Stage.Review, shell.Stage);
        Assert.Same(review, shell.Current);
        Assert.Null(shell.PhotoCleanup);
        Assert.True(shell.PhotoCleanupEnabled);
    }

    [Fact]
    public async Task PhotoCleanup_IsDisabledWithAVisibleReason_WhenThePhotoFolderIsMissing()
    {
        var rig = new Rig { PhotoRootExists = false };
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);
        Assert.False(shell.PhotoCleanupEnabled);
        Assert.Equal($"The photo folder {TestPlans.PhotoRoot} is not available", shell.PhotoCleanupUnavailableText);
        shell.OpenPhotoCleanup();
        Assert.Equal(Stage.Review, shell.Stage);
        Assert.Equal(0, rig.PhotoCleanupOpens);
    }

    [Fact]
    public async Task PhotoCleanup_FromSettings_ReturnsToTheSameSettingsPage()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);
        shell.OpenSettings();
        var settings = shell.Current;
        Assert.True(shell.PhotoCleanupEnabled);
        shell.OpenPhotoCleanup();
        Assert.Equal(Stage.PhotoCleanup, shell.Stage);
        Assert.IsType<PhotoCleanupVm>(shell.Current).BackCommand.Execute(null);
        Assert.Equal(Stage.Settings, shell.Stage);
        Assert.Same(settings, shell.Current);
        shell.CloseSettings();
        Assert.Equal(Stage.Review, shell.Stage);
    }

    [Fact]
    public async Task PhotoCleanup_FromSettings_UsesTheEditsMadeOnThatPage_AndSavesThemFirst()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);
        shell.OpenSettings();
        var page = Assert.IsType<SettingsPageVm>(shell.Current);
        page.ChangeLightroomFolder(@"X:\Photos\Lightroom");                 // saved 500 ms later; ShellVm.Settings is still the old one
        shell.OpenPhotoCleanup();
        var vm = Assert.IsType<PhotoCleanupVm>(shell.Current);
        await Eventually.TrueAsync(() => !vm.IsBusy, rig.Ui);
        Assert.Equal(@"X:\Photos\Lightroom", rig.PhotoCleanupSettings!.LightroomFolder);
        Assert.True(vm.VerifyEnabled);
        Assert.Equal(@"X:\Photos\Lightroom", rig.Store!.Saved[^1].LightroomFolder);   // flushed before the page opened
    }

    [Fact]
    public async Task PhotoCleanup_OnTheSettingsPage_FollowsThePagesPhotoFolder()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);
        shell.OpenSettings();
        Assert.True(shell.PhotoCleanupEnabled);
        rig.PhotoRootExists = false;                                       // the new folder below is not available
        Assert.IsType<SettingsPageVm>(shell.Current).ChangePhotoRoot(@"X:\New Offload");
        Assert.False(shell.PhotoCleanupEnabled);
        Assert.Equal(@"The photo folder X:\New Offload is not available", shell.PhotoCleanupUnavailableText);
    }
```

```csharp
// tests/UasSort.Review.Tests/LightroomSettingsTests.cs
namespace UasSort.Review.Tests;

public sealed class LightroomSettingsTests
{
    private sealed class Rig
    {
        public FakeUiDispatcher Ui { get; } = new();
        public FakeTimeProvider Time { get; } = new();
        public FakeSettingsStore Store { get; } = new(new SettingsLoad(TestPlans.Settings(), false, null, null));

        public SettingsPageVm Vm(Settings? s = null) => new(s ?? TestPlans.Settings(), Store, _ => Fake.Ledger(), FakeLayout.NewFileSystem(),
            new FakeShellLauncher(), new FakeDialogService(), new FakeFreeSpace(), Time, Ui, () => TestPlans.Ledger(), r => @"C:\AppData\backup");

        public void Flush()
        {
            Time.Advance(SettingsPageVm.SaveDelay);
            Ui.RunAll();
        }
    }

    [Fact]
    public void ChangeLightroomFolder_AValidFolder_IsShownAndSaved()
    {
        var rig = new Rig();
        using var vm = rig.Vm();
        Assert.Equal("Not set", vm.LightroomText);
        vm.ChangeLightroomFolder(@"X:\Photos\Lightroom");
        Assert.Equal((@"X:\Photos\Lightroom", (string?)null), (vm.LightroomFolder, vm.LightroomError));
        Assert.Equal(@"X:\Photos\Lightroom", vm.LightroomText);
        rig.Flush();
        Assert.Equal(@"X:\Photos\Lightroom", rig.Store.Saved[^1].LightroomFolder);
    }

    [Fact]
    public void ChangeLightroomFolder_InsideThePhotoFolder_IsRefusedWithTheReason_AndNotSaved()
    {
        var rig = new Rig();
        using var vm = rig.Vm();
        vm.ChangeLightroomFolder(TestPlans.PhotoRoot + @"\LR");
        Assert.Equal("That folder is inside the photo folder (Picture Offload)", vm.LightroomError);
        Assert.Null(vm.Current.LightroomFolder);
        rig.Flush();
        Assert.Empty(rig.Store.Saved);
    }

    [Fact]
    public void ClearLightroom_RemovesTheFolder()
    {
        var rig = new Rig();
        using var vm = rig.Vm(TestPlans.Settings() with { LightroomFolder = @"X:\Photos\Lightroom" });
        Assert.Equal(@"X:\Photos\Lightroom", vm.LightroomFolder);
        vm.ClearLightroomCommand.Execute(null);
        Assert.Null(vm.Current.LightroomFolder);
        rig.Flush();
        Assert.Null(rig.Store.Saved[^1].LightroomFolder);
    }

    [Fact]
    public void ChangePhotoRoot_IntoTheLightroomFolder_ClearsItWithTheReason()
    {
        var rig = new Rig();
        using var vm = rig.Vm(TestPlans.Settings() with { LightroomFolder = @"X:\Photos\Lightroom" });
        vm.ChangePhotoRoot(@"X:\Photos\Lightroom\Offload");
        Assert.Null(vm.LightroomFolder);
        Assert.Equal("Lightroom folder cleared: That folder contains the photo folder (Picture Offload)", vm.LightroomError);
        rig.Flush();
        Assert.Equal((@"X:\Photos\Lightroom\Offload", (string?)null), (rig.Store.Saved[^1].PhotoRoot, rig.Store.Saved[^1].LightroomFolder));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-class "*ShellVmTests" --filter-class "*LightroomSettingsTests"`
Expected: build fails (`CS0117: 'ShellDeps' does not contain a definition for 'CreatePhotoCleanup'`, `'SettingsPageVm' does not contain a definition for 'ChangeLightroomFolder'`).

- [ ] **Step 3: Implement**

In `src/UasSort.Review/Shell/ShellVm.cs`:

1. Replace the `Stage` enum with `public enum Stage { Setup, Card, Scan, Review, Preflight, Copy, Verdict, Cleanup, Settings, PhotoCleanup }`.
2. In the `ShellDeps` body, after the `Log` property, add:

```csharp
    /// <summary>Builds the Picture Offload cleanup page for the current settings (spec 2026-10-04); none: [Clean up Picture Offload…] stays off.</summary>
    public Func<Settings, PhotoCleanupVm>? CreatePhotoCleanup { get; init; }

    /// <summary>Whether a folder exists (attributes only; Platform's FolderFacts.Exists): the photo root's availability.</summary>
    public Func<string, bool>? FolderExists { get; init; }
```

3. Add the field `private (Stage Stage, object? Current)? _beforePhotoCleanup;` next to `_beforeSettings`.
4. In the constructor, after `CleanupCommand = …;` add `PhotoCleanupCommand = new RelayCommand(OpenPhotoCleanup, () => PhotoCleanupEnabled);`.
5. Add the members after `public CleanupVm? Cleanup { get; private set; }`:

```csharp
    public PhotoCleanupVm? PhotoCleanup { get; private set; }
```

after the `CleanupUnavailableText` property:

```csharp
    [ObservableProperty] public partial bool PhotoCleanupEnabled { get; private set; }
    /// <summary>Why [Clean up Picture Offload…] is disabled, as visible text; null while it is enabled (spec 2026-10-04 §2).</summary>
    [ObservableProperty] public partial string? PhotoCleanupUnavailableText { get; private set; }
```

and after `public IRelayCommand CleanupCommand { get; }`:

```csharp
    public IRelayCommand PhotoCleanupCommand { get; }
```

6. In `RescanAsync`, extend the first condition to `if (Stage is Stage.Preflight or Stage.Copy or Stage.Verdict or Stage.Cleanup or Stage.Setup or Stage.PhotoCleanup || PlanFromVerdict) return Task.CompletedTask;`.
7. In `OpenSettings`, after `_settingsPage = _deps.CreateSettings(Settings);` add `_settingsPage.PropertyChanged += OnSettingsPageChanged;`, and in `CloseSettings`, before `page.Dispose();` add `page.PropertyChanged -= OnSettingsPageChanged;` (add `using System.ComponentModel;` for `PropertyChangedEventArgs` unless a global using already covers it). Add after `OpenSettings`:

```csharp
    /// <summary>The settings Picture Offload cleanup sees: on the Settings stage the page's own, possibly unsaved, edits (ShellVm.Settings
    /// only takes them in CloseSettings, and the page saves 500 ms after a change).</summary>
    private Settings PhotoCleanupSettings => Stage == Stage.Settings && _settingsPage is { } page ? page.Current : Settings;

    private void OnSettingsPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsPageVm.PhotoRoot) or nameof(SettingsPageVm.VideoRoot) or nameof(SettingsPageVm.LightroomFolder))
            UpdateFlags();
    }

    /// <summary>Opens Picture Offload cleanup from the Card, Review, Verdict or Settings stage; Done or Back returns to that stage. From
    /// Settings, the page's pending save is written first, so the recycler factory (which re-reads the saved photo root) and the page agree.</summary>
    public void OpenPhotoCleanup()
    {
        UpdateFlags();                                                   // availability against the settings the page will use
        if (!PhotoCleanupEnabled || _deps.CreatePhotoCleanup is not { } create) return;
        if (Stage == Stage.Settings) _settingsPage?.FlushPendingSave();
        var settings = PhotoCleanupSettings;
        _beforePhotoCleanup = (Stage, Current);
        var vm = create(settings);
        vm.Closed += () => ClosePhotoCleanup(vm);
        PhotoCleanup = vm;
        Go(Stage.PhotoCleanup, vm);
        Observed.Forget(vm.OpenAsync(), e => Faulted?.Invoke(e));
    }

    private void ClosePhotoCleanup(PhotoCleanupVm vm)
    {
        if (!ReferenceEquals(PhotoCleanup, vm)) return;
        PhotoCleanup = null;
        vm.Dispose();
        var back = _beforePhotoCleanup;
        _beforePhotoCleanup = null;
        if (back is { } b) Go(b.Stage, b.Current);
        else ShowCard();
    }
```

8. In `UpdateFlags`, change `var pageWithoutCleanup = Stage is Stage.Cleanup or Stage.Settings or Stage.Setup;` to `var pageWithoutCleanup = Stage is Stage.Cleanup or Stage.Settings or Stage.Setup or Stage.PhotoCleanup;`, and directly before `RescanCommand.NotifyCanExecuteChanged();` insert:

```csharp
        // Picture Offload cleanup (spec 2026-10-04 §2): no offload, card cleanup or scan running; the photo root set and available
        // (on the Settings stage: the page's own edits).
        var photoSettings = PhotoCleanupSettings;
        var photoReason = PhotoCleanupAvailability.Reason(new PhotoCleanupContext(committing, IsScanning || Stage == Stage.Scan,
            Stage == Stage.Cleanup, photoSettings.RootsConfirmed && Stage != Stage.Setup, photoSettings.PhotoRoot,
            _deps.FolderExists?.Invoke(photoSettings.PhotoRoot) ?? false));
        var photoPossible = _deps.CreatePhotoCleanup is not null && Stage != Stage.PhotoCleanup;
        PhotoCleanupEnabled = photoPossible && photoReason is null;
        PhotoCleanupUnavailableText = photoPossible ? photoReason : null;
        PhotoCleanupCommand.NotifyCanExecuteChanged();
```

In `src/UasSort.Review/Settings/SettingsPageVm.cs`:
1. Add the observable properties after `SatelliteUrl`:

```csharp
    [ObservableProperty] public partial string? LightroomFolder { get; private set; }
    [ObservableProperty] public partial string? LightroomError { get; private set; }
```

2. Add `public IRelayCommand ClearLightroomCommand { get; }` after `StartEmptyCommand`, and `public string LightroomText => LightroomFolder ?? "Not set";` after `ClockLearnedText`.
3. In the constructor: `ClearLightroomCommand = new RelayCommand(ClearLightroom);` after `StartEmptyCommand = …;`, and `LightroomFolder = settings.LightroomFolder;` inside the `_loading = true; … _loading = false;` block.
4. Add after `ChangePhotoRoot`:

```csharp
    /// <summary>Spec 2026-10-04 §2: the Lightroom library folder, refused with the reason shown when it is inside the photo root, the video
    /// root, a previous photo root or the Lightroom catalog, or above the photo root; saved 500 ms later like the other settings.</summary>
    public void ChangeLightroomFolder(string folder)
    {
        LightroomError = LightroomRules.FolderRefusal(folder, Current);
        if (LightroomError is not null) return;
        Current = Current with { LightroomFolder = PathRules.Normalize(folder.Trim()) };
        LightroomFolder = Current.LightroomFolder;
        ScheduleSave();
    }

    private void ClearLightroom()
    {
        LightroomError = null;
        if (Current.LightroomFolder is null) return;
        Current = Current with { LightroomFolder = null };
        LightroomFolder = null;
        ScheduleSave();
    }

    partial void OnLightroomFolderChanged(string? value) => OnPropertyChanged(nameof(LightroomText));

    /// <summary>A root change can make the Lightroom folder invalid (e.g. the new photo root lies inside it): it is cleared, with the
    /// reason shown, instead of staying in force until the next start.</summary>
    private void RecheckLightroom()
    {
        if (Current.LightroomFolder is not { } folder || LightroomRules.FolderRefusal(folder, Current) is not { } reason) return;
        Current = Current with { LightroomFolder = null };
        LightroomFolder = null;
        LightroomError = "Lightroom folder cleared: " + reason;
    }

    /// <summary>Writes a save still waiting for its debounce now (ShellVm, before opening Picture Offload cleanup from this page).</summary>
    internal void FlushPendingSave() => SaveNow();
```

5. Call `RecheckLightroom();` in `ChangePhotoRoot` directly before its `ScheduleSave();`, and in `ChangeVideoRootAsync` directly before `_store.Save(Current);`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj`
Expected: PASS (the whole Review suite: the new stage changes no existing flag).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Shell/ShellVm.cs src/UasSort.Review/Settings/SettingsPageVm.cs tests/UasSort.Review.Tests/ShellVmTests.cs tests/UasSort.Review.Tests/LightroomSettingsTests.cs
git commit -F - <<'EOF'
feat: add the Picture Offload cleanup stage, its title-bar command and the Lightroom folder setting

<session trailer>
EOF
```

---

### Task P.14: App — page, title bar, Settings row and composition

**Files:**
- Create: `src/UasSort.App/Pages/PhotoCleanupPage.xaml`, `src/UasSort.App/Pages/PhotoCleanupPage.xaml.cs`
- Modify: `src/UasSort.App/Pages/ShellPage.xaml` (`TitleBar.RightHeader`), `src/UasSort.App/Pages/ShellPage.xaml.cs` (`StagePages`)
- Modify: `src/UasSort.App/Pages/SettingsPage.xaml` ("Photos" section), `src/UasSort.App/Pages/SettingsPage.xaml.cs`
- Modify: `src/UasSort.App/Services/CardThumbnails.cs` (`GetAsync`, new `PhotoRoot`)
- Modify: `src/UasSort.App/Services/DeviceChangeWatcher.cs` (`IsSuppressed`)
- Modify: `src/UasSort.App/CompositionRoot.cs` (`ShellDeps` initializer, `CreatePhotoCleanup`, `SelfTestSettings`)

**Interfaces:**
- Consumes: `PhotoCleanupVm`, `PhotoCleanupRowVm`, `PhotoCleanupStep`, `PhotoCleanupResultVm`, `PhotoCleanupEngines.Create`, `PhotoCleanupPorts`, `PhotoRootThumbnails` (P.12); `ShellVm.PhotoCleanupCommand/PhotoCleanupEnabled/PhotoCleanupUnavailableText/OpenPhotoCleanup`, `SettingsPageVm.LightroomText/LightroomFolder/LightroomError/ClearLightroomCommand/ChangeLightroomFolder` (P.13); `PlatformServices.PhotoRecyclers/PhotoReaderFor`, `FolderFacts.Exists` (P.11); `PhotoCleanupEnvironment` (P.10); `PhotoCaptureClock` (P.7); `UiFormat`, `FolderPickerService`, `StageArgs`, `Thumb.Key`, `GeoTimeZoneResolver`, `IPathFacts.Canonical` (existing).
- Produces: `public sealed partial class PhotoCleanupPage : Page { PhotoCleanupVm Vm; internal static void ResyncDecision(ToggleButton b); }` (named elements `RowList`, `VerifySwitch`, `CutoffPicker`, `NextButton`, `RunButton`, `DoneButton`, all `x:FieldModifier="internal"`); `SettingsPage.Shell` (ShellVm); `CardThumbnails.PhotoRoot { get; set; }` (PhotoRootThumbnails?); title-bar elements `PhotoCleanUpButton` and `PhotoCleanupReasonText` (internal).

- [ ] **Step 1: Write the page**

```xml
<?xml version="1.0" encoding="utf-8"?>
<!-- src/UasSort.App/Pages/PhotoCleanupPage.xaml — spec 2026-10-04 §2 (Choose → Review → Confirm → Running → Result); x:Bind only -->
<Page
    x:Class="UasSort.App.Pages.PhotoCleanupPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ctl="using:UasSort.App.Controls"
    xmlns:ui="using:UasSort.App.Services"
    xmlns:rv="using:UasSort.Review">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <ScrollViewer>
            <StackPanel MaxWidth="1000" Padding="24" Spacing="10">
                <TextBlock Text="Clean up Picture Offload" Style="{StaticResource TitleTextBlockStyle}" />
                <TextBlock Text="{x:Bind Vm.PhotoRootText, Mode=OneWay}" Style="{StaticResource CaptionText}" TextWrapping="Wrap" />
                <StackPanel Orientation="Horizontal" Spacing="8" Visibility="{x:Bind ui:UiFormat.Visible(Vm.IsBusy), Mode=OneWay}">
                    <ProgressRing IsActive="{x:Bind Vm.IsBusy, Mode=OneWay}" Width="16" Height="16" />
                    <TextBlock Text="{x:Bind Vm.BusyText, Mode=OneWay}" VerticalAlignment="Center" />
                </StackPanel>
                <InfoBar IsOpen="True" IsClosable="False" Severity="Error" Message="{x:Bind Vm.BlockingText, Mode=OneWay}"
                         Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.BlockingText), Mode=OneWay}">
                    <InfoBar.ActionButton>
                        <Button Content="Keep on this device" Command="{x:Bind Vm.KeepOnDeviceCommand}"
                                Visibility="{x:Bind ui:UiFormat.Visible(Vm.CanKeepOnDevice), Mode=OneWay}" />
                    </InfoBar.ActionButton>
                </InfoBar>

                <!-- ── Choose ── -->
                <StackPanel x:Name="ChoosePanel" Spacing="10">
                    <TextBlock TextWrapping="Wrap"
                               Text="Move photos shot on or before this day (local time where they were shot) from Picture Offload to the Recycle Bin. Only Picture Offload is touched; your Lightroom library is only read." />
                    <!-- PhotoCleanupVm.PickDate: the picker's own calendar day becomes the cutoff -->
                    <CalendarDatePicker x:Name="CutoffPicker" x:FieldModifier="internal" Date="{x:Bind Vm.PickedDate, Mode=OneWay}"
                                        DateChanged="OnDateChanged" PlaceholderText="Pick a day" />
                    <ToggleSwitch x:Name="VerifySwitch" x:FieldModifier="internal" Header="Verify against Lightroom"
                                  IsOn="{x:Bind Vm.Verify, Mode=TwoWay}" IsEnabled="{x:Bind Vm.VerifyEnabled, Mode=OneWay}" />
                    <TextBlock Text="{x:Bind Vm.VerifyUnavailableText, Mode=OneWay}" Style="{StaticResource CaptionText}" TextWrapping="Wrap"
                               Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.VerifyUnavailableText), Mode=OneWay}" />
                </StackPanel>

                <!-- ── Review ── -->
                <StackPanel x:Name="ReviewPanel" Spacing="6" Visibility="Collapsed">
                    <TextBlock Text="{x:Bind Vm.ModeText, Mode=OneWay}" TextWrapping="Wrap" />
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <TextBlock Text="{x:Bind Vm.TotalsText, Mode=OneWay}" Style="{StaticResource BodyStrongTextBlockStyle}" VerticalAlignment="Center" />
                        <Button Content="Delete all" Command="{x:Bind Vm.DeleteAllCommand}" />
                        <Button Content="Keep all" Command="{x:Bind Vm.KeepAllCommand}" />
                    </StackPanel>
                    <ItemsView x:Name="RowList" x:FieldModifier="internal" SelectionMode="None" MaxHeight="560">
                        <ItemsView.ItemTemplate>
                            <DataTemplate x:DataType="rv:PhotoCleanupRowVm">
                                <!-- ItemsView leaves DataContext unset for an x:Bind template; OnDecisionClick reads the row from it -->
                                <ItemContainer DataContext="{x:Bind}">
                                    <StackPanel>
                                        <TextBlock Text="{x:Bind DayHeader, Mode=OneWay}" Style="{StaticResource BodyStrongTextBlockStyle}" Margin="4,10,0,2"
                                                   Visibility="{x:Bind ui:UiFormat.VisibleIfText(DayHeader), Mode=OneWay}" />
                                        <Grid ColumnSpacing="10" Padding="4,4">
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="104" />
                                                <ColumnDefinition Width="*" />
                                                <ColumnDefinition Width="Auto" />
                                            </Grid.ColumnDefinitions>
                                            <Border Width="96" Height="64" CornerRadius="3" Background="{ThemeResource SubtleFillColorSecondaryBrush}">
                                                <Image ctl:Thumb.Key="{x:Bind ThumbKey}" Stretch="UniformToFill" />
                                            </Border>
                                            <StackPanel Grid.Column="1" Spacing="2">
                                                <TextBlock Text="{x:Bind Title, Mode=OneWay}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" />
                                                <TextBlock Text="{x:Bind DetailText, Mode=OneWay}" Style="{StaticResource CaptionText}" />
                                                <TextBlock Text="{x:Bind StatusText, Mode=OneWay}" Style="{StaticResource CaptionText}" TextWrapping="Wrap" />
                                            </StackPanel>
                                            <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="4" VerticalAlignment="Center">
                                                <ToggleButton Content="Keep" Tag="Keep" IsChecked="{x:Bind ui:UiFormat.IsKeep(Decision), Mode=OneWay}"
                                                              Command="{x:Bind KeepCommand}" Click="OnDecisionClick" />
                                                <ToggleButton Content="Delete" Tag="Delete" IsChecked="{x:Bind ui:UiFormat.IsDelete(Decision), Mode=OneWay}"
                                                              Command="{x:Bind DeleteCommand}" Click="OnDecisionClick" />
                                            </StackPanel>
                                        </Grid>
                                    </StackPanel>
                                </ItemContainer>
                            </DataTemplate>
                        </ItemsView.ItemTemplate>
                    </ItemsView>
                    <TextBlock Text="{x:Bind Vm.NotEligibleText, Mode=OneWay}" Style="{StaticResource BodyStrongTextBlockStyle}"
                               Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.NotEligibleText), Mode=OneWay}" />
                    <ItemsControl ItemsSource="{x:Bind ui:UiFormat.Items(Vm.NotEligibleLines), Mode=OneWay}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="x:String"><TextBlock Text="{x:Bind}" Style="{StaticResource CaptionText}" TextWrapping="Wrap" /></DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <TextBlock Text="{x:Bind Vm.NotTouchedText, Mode=OneWay}" Style="{StaticResource CaptionText}"
                               Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.NotTouchedText), Mode=OneWay}" />
                </StackPanel>

                <!-- ── Confirm ── -->
                <StackPanel x:Name="ConfirmPanel" Spacing="8" Visibility="Collapsed">
                    <TextBlock Text="{x:Bind Vm.TotalsText, Mode=OneWay}" Style="{StaticResource BodyStrongTextBlockStyle}" />
                    <TextBlock Text="{x:Bind Vm.ModeText, Mode=OneWay}" TextWrapping="Wrap" />
                    <CheckBox IsChecked="{x:Bind Vm.AckMove, Mode=TwoWay}">
                        <TextBlock Text="{x:Bind Vm.AckMoveText, Mode=OneWay}" TextWrapping="Wrap" />
                    </CheckBox>
                    <CheckBox IsChecked="{x:Bind Vm.AckUnverified, Mode=TwoWay}" Visibility="{x:Bind ui:UiFormat.Visible(Vm.ShowUnverifiedAck), Mode=OneWay}">
                        <TextBlock Text="{x:Bind Vm.UnverifiedAckText, Mode=OneWay}" TextWrapping="Wrap" />
                    </CheckBox>
                </StackPanel>

                <!-- ── Running ── -->
                <StackPanel x:Name="RunningPanel" Spacing="10" Visibility="Collapsed">
                    <TextBlock Text="Moving to the Recycle Bin" Style="{StaticResource SubtitleTextBlockStyle}" />
                    <ProgressBar IsIndeterminate="True" />
                    <TextBlock Text="{x:Bind Vm.ProgressText, Mode=OneWay}" TextTrimming="CharacterEllipsis" />
                </StackPanel>

                <!-- ── Result ── -->
                <StackPanel x:Name="ResultPanel" Spacing="10" Visibility="Collapsed">
                    <TextBlock Text="{x:Bind Vm.Result.HeadlineText, Mode=OneWay, FallbackValue=''}" Style="{StaticResource SubtitleTextBlockStyle}" TextWrapping="Wrap" />
                    <TextBlock Text="{x:Bind Vm.Result.CountsText, Mode=OneWay, FallbackValue=''}" TextWrapping="Wrap" />
                    <TextBlock Text="{x:Bind Vm.Result.StopText, Mode=OneWay, FallbackValue=''}" TextWrapping="Wrap" FontWeight="SemiBold" />
                    <TextBlock Text="{x:Bind Vm.Result.LedgerWarning, Mode=OneWay, FallbackValue=''}" TextWrapping="Wrap" />
                    <ItemsControl ItemsSource="{x:Bind ui:UiFormat.Items(Vm.Result.Problems), Mode=OneWay}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="x:String"><TextBlock Text="{x:Bind}" TextWrapping="Wrap" /></DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <InfoBar IsOpen="True" IsClosable="False" Severity="Informational" Message="{x:Bind Vm.Result.RecycleBinText, Mode=OneWay, FallbackValue=''}" />
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <TextBlock Text="{x:Bind Vm.Result.ReportPath, Mode=OneWay, FallbackValue=''}" Style="{StaticResource CaptionText}" VerticalAlignment="Center"
                                   TextTrimming="CharacterEllipsis" MaxWidth="640" />
                        <Button Content="Open report" Command="{x:Bind Vm.Result.OpenReportCommand, Mode=OneWay}" />
                    </StackPanel>
                    <TextBlock Text="{x:Bind Vm.Result.ReportProblem, Mode=OneWay, FallbackValue=''}" TextWrapping="Wrap" />
                </StackPanel>
            </StackPanel>
        </ScrollViewer>
        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right" Padding="24,12"
                    BorderThickness="0,1,0,0" BorderBrush="{ThemeResource DividerStrokeColorDefaultBrush}">
            <Button x:Name="BackButton" Content="Back" Command="{x:Bind Vm.BackCommand}" />
            <Button x:Name="NextButton" x:FieldModifier="internal" Content="Next" Style="{StaticResource AccentButtonStyle}"
                    Command="{x:Bind Vm.NextCommand}" IsEnabled="{x:Bind Vm.CanNext, Mode=OneWay}" />
            <Button x:Name="RunButton" x:FieldModifier="internal" Content="{x:Bind Vm.RunButtonText, Mode=OneWay}" Style="{StaticResource AccentButtonStyle}"
                    Command="{x:Bind Vm.RunCommand}" IsEnabled="{x:Bind Vm.CanRun, Mode=OneWay}" Visibility="Collapsed" />
            <Button x:Name="StopButton" Content="Stop after the current item" Command="{x:Bind Vm.CancelCommand}" Visibility="Collapsed" />
            <Button x:Name="DoneButton" x:FieldModifier="internal" Content="Done" Style="{StaticResource AccentButtonStyle}"
                    Command="{x:Bind Vm.DoneCommand}" Visibility="Collapsed" />
        </StackPanel>
    </Grid>
</Page>
```

```csharp
// src/UasSort.App/Pages/PhotoCleanupPage.xaml.cs
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class PhotoCleanupPage : Page
{
    public PhotoCleanupPage() => InitializeComponent();

    public PhotoCleanupVm Vm { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (Vm is not null) Vm.PropertyChanged -= OnVmChanged;
        Vm = (PhotoCleanupVm)((StageArgs)e.Parameter).Vm;          // ShellVm.Current (= ShellVm.PhotoCleanup) on the PhotoCleanup stage
        Vm.PropertyChanged += OnVmChanged;
        RowList.ItemsSource = Vm.Rows;
        Bindings.Update();
        ShowStep(Vm.Step);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => Vm.PropertyChanged -= OnVmChanged;

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotoCleanupVm.Step)) ShowStep(Vm.Step);
    }

    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => Vm?.PickDate(args.NewDate);

    /// <summary>Same re-sync as CleanupPage.OnDecisionClick: a ToggleButton un-toggles itself on click before its Command runs.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "XAML event handler: the generated Connect code wires it as this.OnDecisionClick, so it must be an instance method.")]
    private void OnDecisionClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton b) ResyncDecision(b);
    }

    internal static void ResyncDecision(ToggleButton b)
    {
        if (b.DataContext is PhotoCleanupRowVm row)
            b.IsChecked = string.Equals(b.Tag as string, "Keep", StringComparison.Ordinal) ? row.Decision == RowDecision.Keep : row.Decision == RowDecision.Delete;
    }

    private void ShowStep(PhotoCleanupStep step)
    {
        static Visibility On(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        ChoosePanel.Visibility = On(step == PhotoCleanupStep.Choose);
        ReviewPanel.Visibility = On(step == PhotoCleanupStep.Review);
        ConfirmPanel.Visibility = On(step == PhotoCleanupStep.Confirm);
        RunningPanel.Visibility = On(step == PhotoCleanupStep.Running);
        ResultPanel.Visibility = On(step == PhotoCleanupStep.Result);
        BackButton.Visibility = On(step is PhotoCleanupStep.Choose or PhotoCleanupStep.Review or PhotoCleanupStep.Confirm);
        NextButton.Visibility = On(step is PhotoCleanupStep.Choose or PhotoCleanupStep.Review);
        RunButton.Visibility = On(step == PhotoCleanupStep.Confirm);
        StopButton.Visibility = On(step == PhotoCleanupStep.Running);
        DoneButton.Visibility = On(step == PhotoCleanupStep.Result);
        Bindings.Update();                                           // Result is created when the run ends
    }
}
```

`b.Tag as string` and `b.DataContext is PhotoCleanupRowVm` test values this page itself put there (a string literal Tag and the row VM via `DataContext="{x:Bind}"`), exactly as `CleanupPage` does; no projected WinRT value is cast.

- [ ] **Step 2: Title bar, Settings row, thumbnails and composition**

In `src/UasSort.App/Pages/ShellPage.xaml`, between the `CleanUpButton` and the `SettingsButton`, insert:

```xml
                    <!-- Why [Clean up Picture Offload…] is disabled, as visible text (spec 2026-10-04 §2) -->
                    <TextBlock x:Name="PhotoCleanupReasonText" x:FieldModifier="internal" Text="{x:Bind Vm.PhotoCleanupUnavailableText, Mode=OneWay}"
                               Style="{StaticResource CaptionText}" VerticalAlignment="Center" MaxWidth="320" Margin="8,0,4,0"
                               Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.PhotoCleanupUnavailableText), Mode=OneWay}"
                               ToolTipService.ToolTip="{x:Bind Vm.PhotoCleanupUnavailableText, Mode=OneWay}" />
                    <Button x:Name="PhotoCleanUpButton" x:FieldModifier="internal" Content="Clean up Picture Offload…"
                            Command="{x:Bind Vm.PhotoCleanupCommand}" IsEnabled="{x:Bind Vm.PhotoCleanupEnabled, Mode=OneWay}"
                            Style="{StaticResource SubtleButtonStyle}" />
```

In `src/UasSort.App/Pages/ShellPage.xaml.cs`, add `[Stage.PhotoCleanup] = typeof(PhotoCleanupPage),` to `StagePages`.

In `src/UasSort.App/Pages/SettingsPage.xaml`, after the "Copy the JPG twin" `SettingsCard`, insert:

```xml
                <ctk:SettingsCard Header="Lightroom library folder"
                                  Description="Picture Offload cleanup looks here for imported photos before it removes their copies. Only ever read.">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <TextBlock Text="{x:Bind Vm.LightroomText, Mode=OneWay}" VerticalAlignment="Center" MaxWidth="380" TextTrimming="CharacterEllipsis" />
                        <Button Content="Change…" Click="OnPickLightroomFolder" />
                        <Button Content="Clear" Command="{x:Bind Vm.ClearLightroomCommand}" />
                    </StackPanel>
                </ctk:SettingsCard>
                <InfoBar IsOpen="True" IsClosable="False" Severity="Warning" Message="{x:Bind Vm.LightroomError, Mode=OneWay}"
                         Visibility="{x:Bind ui:UiFormat.VisibleIfText(Vm.LightroomError), Mode=OneWay}" />
                <ctk:SettingsCard Header="Picture Offload" Description="Move photos already imported into Lightroom from Picture Offload to the Recycle Bin">
                    <HyperlinkButton x:Name="PhotoCleanupLink" x:FieldModifier="internal" Content="Clean up Picture Offload…"
                                     Click="OnOpenPhotoCleanup" IsEnabled="{x:Bind Shell.PhotoCleanupEnabled, Mode=OneWay}" />
                </ctk:SettingsCard>
```

In `src/UasSort.App/Pages/SettingsPage.xaml.cs`: add `public ShellVm Shell { get; private set; } = null!;`; in `OnNavigatedTo`, after `_window = args.Window;` add `Shell = _window.Services.Shell;` (before `Bindings.Update()`); and add the handlers:

```csharp
    private async void OnPickLightroomFolder(object sender, RoutedEventArgs e)
    {
        if (await FolderPickerService.PickFolderAsync(_window, Vm.LightroomFolder) is { } path) Vm.ChangeLightroomFolder(path);
    }

    private void OnOpenPhotoCleanup(object sender, RoutedEventArgs e) => _window.Services.Shell.OpenPhotoCleanup();
```

In `src/UasSort.App/Services/CardThumbnails.cs`, add the property and route `photo-root:` keys first in `GetAsync`:

```csharp
    /// <summary>Picture Offload cleanup's thumbnails (spec 2026-10-04 §2): "photo-root:" keys go here, every other key to the card.</summary>
    public PhotoRootThumbnails? PhotoRoot { get; set; }

    public ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct)
    {
        if (PhotoRootThumbnails.IsKey(id)) return PhotoRoot?.GetAsync(id, ct) ?? ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
        ThumbnailReader? reader;
        lock (_gate) reader = _pauses > 0 ? null : _current;
        return reader?.GetAsync(id, ct) ?? ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
    }
```

In `src/UasSort.App/Services/DeviceChangeWatcher.cs`: `public static bool IsSuppressed(Stage stage) => stage is Stage.Preflight or Stage.Copy or Stage.Cleanup or Stage.PhotoCleanup;`.

In `src/UasSort.App/CompositionRoot.cs`:
1. Change the `ShellDeps` initializer `{ Log = _log }` to `{ Log = _log, CreatePhotoCleanup = CreatePhotoCleanup, FolderExists = FolderFacts.Exists }`.
2. Add to `Wiring`, after `CreateCleanup`:

```csharp
        /// <summary>Picture Offload cleanup (spec 2026-10-04): the canonical photo root (so plan paths match the recycler's guard) and the
        /// canonical Lightroom folder (so the index lists, reads and is guarded in one form — a junction, subst or mapped drive would
        /// otherwise make every Lightroom read "outside the Lightroom folder"), the guarded reader and its thumbnails, the drone clock the
        /// scan uses, and the executor's environment.</summary>
        private PhotoCleanupVm CreatePhotoCleanup(Settings settings)
        {
            var s = settings with { LightroomFolder = settings.LightroomFolder is { } lr ? _p.PathFacts.Canonical(lr) : null };
            var root = _p.PathFacts.Canonical(s.PhotoRoot);
            var reader = _p.PhotoReaderFor(s);
            var thumbnails = new PhotoRootThumbnails(reader);
            Thumbnails.PhotoRoot = thumbnails;
            var ledger = _p.LedgerFor(s.VideoRoot);
            var clock = PhotoCaptureClock.For(s, new GeoTimeZoneResolver(), TimeZoneInfo.Local);      // as the Planner's scan resolves stills
            var env = new PhotoCleanupEnvironment(_p.PhotoRecyclers, _p.Lister, ledger, _p.OffloadLock, _p.Power, _p.Clock, _p.Machine);
            var engine = PhotoCleanupEngines.Create(new PhotoCleanupPorts(s, root, _p.Lister, reader, ledger, clock, env, FolderFacts.Exists,
                                                                          _p.Reports, _p.Shell)
            {
                Thumbnails = thumbnails,
            });
            return new PhotoCleanupVm(engine, _dialogs, _ui, _p.Clock);
        }
```

3. In `SelfTestSettings`, append the named argument `LightroomFolder: Path.Join(s.Root, "lightroom")` (a sandbox folder; Task P.15 fills it).

- [ ] **Step 3: Build and run the App-facing tests**

Run: `dotnet build uas-sort.slnx`
Expected: Build succeeded, 0 warnings (x:Bind paths compile-checked; CsWinRTAotWarningLevel=2 raises no warning).
Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*XamlLintTests"`
Expected: PASS (no `{Binding`, no `DisplayMemberPath`, images only via `Thumb.Key`).

- [ ] **Step 4: Commit**

```bash
git add src/UasSort.App/Pages/PhotoCleanupPage.xaml src/UasSort.App/Pages/PhotoCleanupPage.xaml.cs src/UasSort.App/Pages/ShellPage.xaml src/UasSort.App/Pages/ShellPage.xaml.cs src/UasSort.App/Pages/SettingsPage.xaml src/UasSort.App/Pages/SettingsPage.xaml.cs src/UasSort.App/Services/CardThumbnails.cs src/UasSort.App/Services/DeviceChangeWatcher.cs src/UasSort.App/CompositionRoot.cs
git commit -F - <<'EOF'
feat: add the Clean up Picture Offload page, title-bar button and Settings row

<session trailer>
EOF
```

---
### Task P.15: Selftest — the page and a real recycle inside the sandbox

**Files:**
- Create: `src/UasSort.App/SelfTest/SelfTestChecks.PhotoCleanup.cs`
- Modify: `src/UasSort.App/SelfTest/SelfTestChecks.cs` (`All`: one entry after `("verdict.daySelection", VerdictDaySelection)`)
- Modify: `src/UasSort.App/SelfTest/SelfTestChecks.Pages.cs` (`PageFactories`: add `() => new PhotoCleanupPage()`)

**Interfaces:**
- Consumes: `ShellVm.PhotoCleanupCommand/PhotoCleanupEnabled/PhotoCleanupUnavailableText/PhotoCleanup` (P.13), `PhotoCleanupVm` (P.12), `PhotoCleanupPage` (P.14), `SelfTestSandbox.PurgeOwnRecycleBinItems` (P.11; `RecycleBinPurge` itself is internal to Platform), `PhotoDeleteRecords.EvidenceLightroom` (P.3), `SelfTestSandbox.WriteFile`, `SelfTestFixture.ReadAll("selftest.dng")`, `WaitUntilAsync`, `CurrentPage<T>` (existing), the sandbox Lightroom folder set by `CompositionRoot.SelfTestSettings` (P.14).
- Produces: selftest check `photoCleanup.flow`.

The check runs on whatever stage the earlier checks left (Card, Review or Verdict; all allow the page). The sandbox photo root gets two loose DNGs and a set folder, plus `notes.txt`; the sandbox Lightroom folder holds a copy of only the first DNG. All DNGs are the embedded `selftest.dng` with their DateTimeOriginal patched, so none shares a second with the selftest card's DNG (14:05:00). Their local date is Sep 27 under the scan's time rules too (selftest drone clock America/New_York, GPS Zachar Bay: 14:04 EDT = 10:04 AKDT). The run recycles exactly one sandbox file; once the page has opened, a `finally` purges this run's own Recycle Bin items on every path (pass, fail or an early return), and both messages report the purge count.

- [ ] **Step 1: Write the check**

```csharp
// src/UasSort.App/SelfTest/SelfTestChecks.PhotoCleanup.cs
using System.Text;

namespace UasSort.App.SelfTest;

internal static partial class SelfTestChecks
{
    private const string SelfTestDngDto = "2026:09:27 14:05:00";

    /// <summary>Spec 2026-10-04 §8 selftest: a synthetic Picture Offload and Lightroom folder inside the sandbox; the title-bar command opens
    /// the page; verify mode puts only the photo found in Lightroom on Delete; the run really moves that sandbox file to the Recycle Bin and
    /// records it as photoDelete; Done returns to the stage it was opened from; the check purges exactly its own Recycle Bin item.</summary>
    private static async Task<SelfTestCheck> PhotoCleanupFlow(SelfTestContext ctx)
    {
        var shell = ctx.Services.Shell;
        var s = ctx.Sandbox;
        var dng = SelfTestFixture.ReadAll("selftest.dng");
        var inLightroom = WithDto(dng, "2026:09:27 14:04:10");
        var photo = Path.GetRelativePath(s.Root, s.PhotoRoot);
        s.WriteFile(Path.Join(photo, "SELFTEST_0001.DNG"), inLightroom);
        s.WriteFile(Path.Join(photo, "SELFTEST_0002.DNG"), WithDto(dng, "2026:09:27 14:06:00"));
        s.WriteFile(Path.Join(photo, "001_0042", "PANO_0001.DNG"), WithDto(dng, "2026:09:27 14:07:00"));
        s.WriteFile(Path.Join(photo, "001_0042", "PANO_0002.DNG"), WithDto(dng, "2026:09:27 14:07:02"));
        s.WriteFile(Path.Join(photo, "notes.txt"), "selftest"u8);
        s.WriteFile(Path.Join("lightroom", "2026", "2026-09-27", "Selftest_20260927_001.dng"), inLightroom);

        var before = shell.Stage;
        if (!shell.PhotoCleanupEnabled)
            return SelfTestCheck.Fail("photoCleanup.flow", $"[Clean up Picture Offload…] disabled on {before}: {shell.PhotoCleanupUnavailableText}");
        shell.PhotoCleanupCommand.Execute(null);
        int purged;
        string outcome;
        bool pass;
        try
        {
            (pass, outcome) = await PhotoCleanupFlowSteps(ctx, before);
        }
        finally
        {
            purged = s.PurgeOwnRecycleBinItems();     // on every path once the page opened: a sandbox file may already be in the Recycle Bin
        }
        return pass && purged >= 1
            ? SelfTestCheck.Pass("photoCleanup.flow", $"{outcome}; purged {purged} own Recycle Bin item(s)")
            : SelfTestCheck.Fail("photoCleanup.flow", $"{outcome}; purged {purged} own Recycle Bin item(s)");
    }

    private static async Task<(bool Pass, string Outcome)> PhotoCleanupFlowSteps(SelfTestContext ctx, Stage before)
    {
        var shell = ctx.Services.Shell;
        var s = ctx.Sandbox;
        if (!await WaitUntilAsync(() => CurrentPage<PhotoCleanupPage>(ctx) is { IsLoaded: true } && shell.PhotoCleanup is { IsBusy: false },
                                  TimeSpan.FromSeconds(10)))
            return (false, "PhotoCleanupPage did not open");
        var vm = shell.PhotoCleanup!;
        if (vm.BlockingText is { } blocked) return (false, "blocked: " + blocked);

        vm.Verify = true;
        vm.PickDate(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));
        await vm.NextCommand.ExecuteAsync(null);
        if (!await WaitUntilAsync(() => vm.Step == PhotoCleanupStep.Review && !vm.IsBusy, TimeSpan.FromSeconds(10)))
            return (false, $"review not reached (step {vm.Step}, {vm.BlockingText})");
        PhotoCleanupRowVm? Row(string key) => vm.Rows.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
        var decisions = string.Join(", ", vm.Rows.Select(r => $"{r.Key}={r.Decision} ({r.StatusText})"));
        var rowsOk = Row("SELFTEST_0001.DNG") is { Decision: RowDecision.Delete, IsVerified: true }
                     && Row("SELFTEST_0002.DNG") is { Decision: RowDecision.Keep }
                     && Row("001_0042") is { Decision: RowDecision.Keep }
                     && vm.NotTouchedText is not null;

        await vm.NextCommand.ExecuteAsync(null);
        vm.AckMove = true;
        await vm.RunCommand.ExecuteAsync(null);
        if (!await WaitUntilAsync(() => vm.Step == PhotoCleanupStep.Result, TimeSpan.FromSeconds(10)))
            return (false, $"result not reached (step {vm.Step}, {vm.BlockingText}); rows {decisions}");

        var left = ctx.Services.Platform.Lister.Enumerate(s.PhotoRoot, recurse: false, new HashSet<string>(StringComparer.OrdinalIgnoreCase))
                      .Entries.Select(e => Path.GetFileName(e.FullPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var moved = !left.Contains("SELFTEST_0001.DNG") && left.Contains("SELFTEST_0002.DNG") && left.Contains("001_0042") && left.Contains("notes.txt");
        var recorded = ctx.Services.Platform.LedgerFor(s.VideoRoot).Load().PhotoDeletes.Values
                          .Any(d => d.Evidence == PhotoDeleteRecords.EvidenceLightroom
                                    && d.Dest.EndsWith(@"\SELFTEST_0001.DNG", StringComparison.OrdinalIgnoreCase));
        var headline = vm.Result?.HeadlineText;
        vm.DoneCommand.Execute(null);
        var back = await WaitUntilAsync(() => shell.Stage == before, TimeSpan.FromSeconds(5));
        return rowsOk && moved && recorded && back
            ? (true, $"verify mode kept the unconfirmed rows; {headline}; photoDelete recorded")
            : (false, $"rows {decisions} (ok {rowsOk}), moved {moved}, recorded {recorded}, back {back}");
    }

    /// <summary>selftest.dng with every occurrence of its DateTimeOriginal text replaced (same length), so each copy is its own shot.</summary>
    private static byte[] WithDto(byte[] dng, string dto)
    {
        var from = Encoding.ASCII.GetBytes(SelfTestDngDto);
        var to = Encoding.ASCII.GetBytes(dto);
        var copy = dng.ToArray();
        for (var i = 0; i + from.Length <= copy.Length; i++)
            if (copy.AsSpan(i, from.Length).SequenceEqual(from)) to.CopyTo(copy, i);
        return copy;
    }
}
```

In `SelfTestChecks.All`, after `("verdict.daySelection", VerdictDaySelection),` add `("photoCleanup.flow", PhotoCleanupFlow),`. In `PageFactories`, add `() => new PhotoCleanupPage(),` after `() => new CleanupPage(),`.

- [ ] **Step 2: Run the new check alone, then the whole selftest**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/selftest.ps1 -Only photoCleanup.flow`
Expected: exit 0; the result lists `photoCleanup.flow` as pass with "purged 1 own Recycle Bin item(s)".
Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/selftest.ps1`
Expected: exit 0; every check passes, including `pages.construct` (now listing `PhotoCleanupPage`) and `json.ledger` (now including `PhotoDeleteRecord`). Then confirm nothing is left behind: `Get-ChildItem $env:TEMP -Filter 'uas-sort-selftest-*'` returns nothing.

- [ ] **Step 3: Commit**

```bash
git add src/UasSort.App/SelfTest/SelfTestChecks.PhotoCleanup.cs src/UasSort.App/SelfTest/SelfTestChecks.cs src/UasSort.App/SelfTest/SelfTestChecks.Pages.cs
git commit -F - <<'EOF'
test: selftest the Picture Offload cleanup page with a real sandbox recycle

<session trailer>
EOF
```

---

### Task P.16: Interface registry, README and the spec's home

**Files:**
- Modify: `docs/superpowers/plans/2026-09-27-uas-sort/00-interfaces.md` (append one section at the end)
- Modify: `README.md` (new section after "## Dry run (CLI)" and one acceptance block)
- Create (only if missing): `docs/superpowers/specs/2026-10-04-picture-offload-cleanup-design.md`
- Create: `docs/superpowers/plans/2026-10-04-picture-offload-cleanup.md` (a copy of this plan)

**Interfaces:**
- Consumes: every public member produced by P.1–P.15.
- Produces: documentation only.

- [ ] **Step 1: Put the spec and this plan where the plan header and the registry point**

`.superpowers/sdd/` is git-ignored (its `.gitignore` is `*`), so both are copied into the committed `docs/` tree.
Run (PowerShell): `if (-not (Test-Path docs/superpowers/specs/2026-10-04-picture-offload-cleanup-design.md)) { Copy-Item .superpowers/sdd/2026-09-27-uas-sort/2026-10-04-picture-offload-cleanup-design.md docs/superpowers/specs/2026-10-04-picture-offload-cleanup-design.md }`
Run (PowerShell): `Copy-Item .superpowers/sdd/2026-09-27-uas-sort/2026-10-04-picture-offload-cleanup-plan.md docs/superpowers/plans/2026-10-04-picture-offload-cleanup.md -Force`
Expected: both files exist under `docs/superpowers/` (the plan copy carries the ticked checkboxes of P.1–P.15).

- [ ] **Step 2: Append the registry section**

Append to `docs/superpowers/plans/2026-09-27-uas-sort/00-interfaces.md`:

```markdown

## Picture Offload cleanup (plan 2026-10-04, tasks P.1–P.15)

Owner column: the task of `docs/superpowers/plans/2026-10-04-picture-offload-cleanup.md` (spec: `docs/superpowers/specs/2026-10-04-picture-offload-cleanup-design.md`). Core code is in folder `src/UasSort.Core/Cleanup/Photos/`, namespace `UasSort.Core.Cleanup` (no new namespace); Review code in `src/UasSort.Review/PhotoCleanup/`, namespace `UasSort.Review`.

### Amended existing rows

| Symbol | Change | Owner |
|---|---|---|
| `Settings` | adds the optional last parameter `string? LightroomFolder = null` (JSON `lightroomFolder`; an invalid value is dropped on load) | P.1 |
| `StillInfo` | adds the optional last parameters `string? SubSec = null` (EXIF SubSecTimeOriginal digits), `int? PixelWidth = null`, `int? PixelHeight = null` (EXIF pixel dimensions or the JPEG frame) | P.2 |
| `LedgerRecord` | ninth JSON kind `"photoDelete"`: `PhotoDeleteRecord(int V, string Id, string Machine, string Run, DateTime At, string Name, long Size, string Dest, DateTime? CaptureUtc, string? Set, string Evidence, string Mode, DateOnly Cutoff)` | P.3 |
| `LedgerSnapshot` | adds `ImmutableDictionary<FileKey, LedgerPhotoDelete> PhotoDeletes { get; init; }` (unrevoked, latest per key; default empty) | P.3 |
| `NewnessRules.Photo` | rule 1: a member with an unrevoked photoDelete (same set name) counts like a file record → `Imported(LedgerVerified, null, "removed from Picture Offload on Oct 4")` | P.4 |
| `AuditCategorizer` | a photo, twin or set member with a photoDelete (and no verified file record or decision) → `InLedger`, detail `PhotoDeleteTexts.Evidence` | P.4 |
| `FileFacts` | adds the optional last parameter `LedgerPhotoDelete? PhotoDelete = null`; `CleanupRules.Classify` → Evidence (HistoryOnly) for photos with one | P.4 |
| `CleanupExecutor` | adds `internal static bool ProofHolds(ItemKind, FileProof, FreshEvidence, LedgerSnapshot)` (photoDelete counts as a verified record for photos) | P.4 |
| `IoOp` | adds `PhotoCleanupRead`, `PhotoRootRecycle` | P.6 |
| `GuardContext` | adds `string? LightroomFolder { get; init; }`, `ConfirmedPhotoCleanupPlan? PhotoCleanup { get; init; }` | P.6 |
| `IoGuardPolicy` | `PhotoCleanupRead`: a file directly in the photo root or in a direct child folder, or under the Lightroom folder outside every catalog path; `PhotoRootRecycle`: a path in `ConfirmedPhotoCleanupPlan.Paths` directly in the photo root (placeholders allowed: recycling never opens data), never the ledger folder, Lightroom folder, catalog, a previous photo root, app data or card; every other op under the Lightroom folder or a catalog path is Unsafe | P.6 |
| `IReportStore` | adds `string Save(PhotoCleanupReport r)` → `reports\yyyyMMdd-HHmmss-<run8>-photos.json` | P.10 |
| `MemReportStore` | adds `List<PhotoCleanupReport> Photos` and the `Save` overload | P.10 |
| `PlatformServices` | adds the last two parameters `IPhotoRootRecyclerFactory PhotoRecyclers, Func<Settings, IPhotoFileReader> PhotoReaderFor` | P.11 |
| `GuardContexts` | adds `static GuardContext ForPhotoCleanup(Settings s, string appDataDir, string machine, IPathFacts facts, ConfirmedPhotoCleanupPlan? plan)` | P.11 |
| `SyntheticDngBuilder` | adds `string? SubSecTimeOriginal { get; init; }`, `WithSubSec(string digits)`, `(int Width, int Height)? ExifPixels { get; init; }` | P.2 |
| `LedgerSamples.AllRecordKindsV1` | adds one photoDelete line (and `ledger-v1.jsonl` is regenerated) | P.3 |
| `Stage` | adds `PhotoCleanup` (last) | P.13 |
| `ShellDeps` | adds `Func<Settings, PhotoCleanupVm>? CreatePhotoCleanup { get; init; }`, `Func<string, bool>? FolderExists { get; init; }` | P.13 |
| `ShellVm` | adds `PhotoCleanupVm? PhotoCleanup`, observable `bool PhotoCleanupEnabled`, `string? PhotoCleanupUnavailableText`, `IRelayCommand PhotoCleanupCommand`, `void OpenPhotoCleanup()` | P.13 |
| `SettingsPageVm` | adds observable `string? LightroomFolder`, `string? LightroomError`; `string LightroomText`; `IRelayCommand ClearLightroomCommand`; `void ChangeLightroomFolder(string folder)`; `internal void FlushPendingSave()`; a root change clears a Lightroom folder that no longer passes `LightroomRules.FolderRefusal` | P.13 |
| `CardThumbnails` | adds `PhotoRootThumbnails? PhotoRoot { get; set; }`; `photo-root:` keys are served by it | P.14 |
| `DeviceChangeWatcher.IsSuppressed` | also on `Stage.PhotoCleanup` | P.14 |
| `SettingsPage` | adds `ShellVm Shell`; Lightroom folder row, picker and the Picture Offload link | P.14 |

### New symbols

| Symbol | Kind | Exact signature | Namespace | Owner | Consumers |
|---|---|---|---|---|---|
| `LightroomRules` | static class | `const string CatalogFolder = @"D:\LR_Catalog"`, `const string CatalogFolderName = "LR_Catalog"`; `static bool IsCatalogName(string name)`; `static bool IsCatalogPath(string path)`; `static string? FolderRefusal(string folder, Settings settings)` | `UasSort.Core.Cleanup` | P.1 | P.6, P.8, P.13 |
| `ExifStamp` | record | `ExifStamp(DateTime Second, string? SubSec, string Model) { static ExifStamp? From(StillInfo? info); bool SameSecondAndModel(ExifStamp o); bool BothHaveSubSec(ExifStamp o); bool SameShot(ExifStamp o); }` | `UasSort.Core.Cleanup` | P.2 | P.7–P.9 |
| `PhotoCleanupMode`, `PhotoEvidence` | enums | `{ BeforeDate, Verify }`; `{ Lightroom, HyperlapseResult, PanoramaStitch, DateOnly, UnverifiedConfirmed }` | `UasSort.Core.Cleanup` | P.3 | P.5–P.15 |
| `PhotoDeleteRecords` | static class | `const string EvidenceLightroom, EvidenceHyperlapseResult, EvidencePanoramaStitch, EvidenceDateOnly, EvidenceUnverifiedConfirmed, ModeBeforeDate, ModeVerify`; `static string Evidence(PhotoEvidence)`; `static string Mode(PhotoCleanupMode)`; `static bool IsEvidence(string?)`; `static bool IsMode(string?)` | `UasSort.Core.Cleanup` | P.3 | P.4, P.10, P.15 |
| `PhotoDeleteRecord` | sealed record class | see `LedgerRecord` above | `UasSort.Core` | P.3 | P.4, P.10 |
| `LedgerPhotoDelete` | record | `LedgerPhotoDelete(string Id, FileKey Key, string Dest, DateTime AtUtc, DateTime? CaptureUtc, string? Set, string Evidence, string Mode, DateOnly Cutoff, string Machine, string Run)` | `UasSort.Core` | P.3 | P.4 |
| `PhotoDeleteTexts` | static class | `static string Newness(LedgerPhotoDelete d)`; `static string Evidence(LedgerPhotoDelete d)` | `UasSort.Core.Cleanup` | P.4 | P.4 |
| `PhotoCleanupRules` | static class | `const uint PlaceholderBits`; `const string DateUnknownCloudOnly, CloudOnlyCantCheck, DateModeText`; `static readonly ImmutableHashSet<string> PhotoExtensions`; `static bool IsCloudOnly(uint)`, `IsPhotoName(string)`, `IsJpg(string)`, `IsDng(string)`, `LooksLikeSetFolder(string)`; `static string SetNameOf(string folderName)`; `const double PanoramaAspect = 2.0`; `static (int Width, int Height)? PixelsOf(StillInfo? info)`; `static bool LooksLikePanorama((int Width, int Height)? pixels)` | `UasSort.Core.Cleanup` | P.5 | P.7–P.12 |
| `PhotoItemKind`, `PhotoSetKind`, `CaptureSource`, `PhotoEligibility` | enums | `{ Photo, Set }`; `{ Unknown, Panorama, Hyperlapse }`; `{ None, Ledger, Exif }`; `{ Eligible, AfterCutoff, StraddlesCutoff, DateUnknown }` | `UasSort.Core.Cleanup` | P.5 | P.7–P.14 |
| `PhotoMember`, `PhotoItem` | records | `PhotoMember(string RelPath, long Size, DateTime MtimeUtc, uint Attributes, DateTime? CaptureUtc, DateOnly? LocalDate, CaptureSource Source, ExifStamp? Stamp, string? DateProblem, LedgerFile? Ledger) { string Name; FileKey Key; bool IsCloudOnly; (int Width, int Height)? Pixels { get; init; } }`; `PhotoItem(string RelPath, PhotoItemKind Kind, PhotoSetKind SetKind, string? SetName, ImmutableArray<PhotoMember> Members) { PhotoMember Primary; long Bytes; bool DateKnown; DateOnly? FirstDate; DateOnly? LastDate; }` | `UasSort.Core.Cleanup` | P.5 | P.7–P.14 |
| `PhotoVerification`, `PhotoRow`, `PhotoSurvey` | records | `PhotoVerification(bool Verified, PhotoEvidence Evidence, string Text) { static readonly PhotoVerification DateMode; }`; `PhotoRow(PhotoItem Item, PhotoEligibility Eligibility, PhotoVerification Verification, string? Why) { string Key; }`; `PhotoSurvey(string PhotoRoot, ImmutableArray<PhotoItem> Items, ImmutableArray<string> NotTouched, LedgerSnapshot Ledger)` | `UasSort.Core.Cleanup` | P.5 | P.7–P.14 |
| `PhotoCleanupRequest`, `PhotoCleanupAck`, `PhotoTotals`, `PhotoScanProgress` | records | `PhotoCleanupRequest(PhotoCleanupMode Mode, DateOnly Cutoff, string? LightroomFolder)`; `PhotoCleanupAck(string PlanFingerprint, ImmutableHashSet<string> Delete, bool MoveToRecycleBin, bool UnverifiedIncluded)`; `PhotoTotals(int Photos, int Sets, int Files, long Bytes, int Unverified)`; `PhotoScanProgress(string Phase, int Done, int Total)` | `UasSort.Core.Cleanup` | P.5 | P.9–P.12 |
| `PhotoCleanupPaths`, `PhotoCleanupFingerprint` | static classes | `static string Full(string photoRoot, string relPath)`; `static ImmutableArray<string> Targets(string photoRoot, PhotoItem item)`; `static string Compute(string photoRoot, PhotoCleanupRequest request, IEnumerable<PhotoRow> rows)` | `UasSort.Core.Cleanup` | P.5 | P.10, P.12 |
| `PhotoCleanupPlan` | sealed class | internal ctor `(string planId, string photoRoot, PhotoCleanupRequest request, ImmutableArray<PhotoRow> rows, ImmutableArray<PhotoRow> notEligible, ImmutableArray<string> notTouched)`; `PlanId`, `PhotoRoot`, `Request`, `Rows`, `NotEligible`, `NotTouched`, `Fingerprint`; `ImmutableHashSet<string> DefaultDelete()`; `PhotoTotals Totals(IReadOnlySet<string> delete)`; `ConfirmedPhotoCleanupPlan Confirm(PhotoCleanupAck ack, TimeProvider clock)` | `UasSort.Core.Cleanup` | P.5 | P.9–P.15 |
| `ConfirmedPhotoCleanupPlan` | sealed class | internal ctor; `Plan`, `PhotoRoot`, `Items`, `Kept`, `Ack`, `Token`, `ConfirmedUtc`, `IReadOnlySet<string> Paths`; `PhotoEvidence EvidenceOf(PhotoRow row)` | `UasSort.Core.Cleanup` | P.5 | P.6, P.10–P.12 |
| `IPhotoFileReader`, `PhotoItemStat`, `RecycleOk`, `RecycleError`, `RecycleResult`, `IPhotoRootRecycler`, `IPhotoRootRecyclerFactory` | ports | `Stream OpenRead(string fullPath)`; `PhotoItemStat(long Size, DateTime MtimeUtc, uint Attributes, bool IsDirectory)`; `RecycleError(int Code, string Message, bool NotRecyclable)`; `union RecycleResult(RecycleOk, RecycleError)`; `IPhotoRootRecycler : IDisposable { PhotoItemStat? Stat(string fullPath); RecycleResult Recycle(string fullPath); }`; `IPhotoRootRecycler Open(ConfirmedPhotoCleanupPlan plan)` | `UasSort.Core` | P.6 | P.7–P.14 |
| `PhotoCaptureClock` | sealed class | `PhotoCaptureClock(StoredClockMode mode, string zoneId, ITimeZoneResolver tz, TimeZoneInfo pc)`; `static PhotoCaptureClock For(Settings s, ITimeZoneResolver tz, TimeZoneInfo pc)`; `(DateTime? Utc, DateOnly LocalDate) Resolve(StillInfo info)` | `UasSort.Core.Cleanup` | P.7 | P.7, P.12, P.14 |
| `PhotoExifRead`, `PhotoExifCache` | record / sealed class | `PhotoExifRead(StillInfo? Info, string? Problem) { ExifStamp? Stamp; }`; `PhotoExifCache(IPhotoFileReader reader) { const string CloudOnlyProblem; int Opens; PhotoExifRead Read(string fullPath, long size, DateTime mtimeUtc, uint attributes); }` | `UasSort.Core.Cleanup` | P.7 | P.8, P.9, P.12 |
| `PhotoCleanupPlanner` | static partial class | `static readonly IReadOnlySet<string> Excludes`; `static PhotoSurvey Survey(string photoRoot, ListingResult listing, LedgerSnapshot ledger, PhotoExifCache exif, PhotoCaptureClock clock, IProgress<PhotoScanProgress>? progress, CancellationToken ct)`; `static (DateOnly From, DateOnly To)? VerifyRange(PhotoSurvey survey, DateOnly cutoff)`; `static PhotoCleanupPlan Build(PhotoSurvey survey, PhotoCleanupRequest request, LightroomIndex? index, PhotoExifCache exif, IProgress<PhotoScanProgress>? progress, CancellationToken ct)` | `UasSort.Core.Cleanup` | P.7, P.9 | P.12 |
| `LightroomPhoto`, `LightroomIndex` | record / sealed class | `LightroomPhoto(string FullPath, ExifStamp Stamp) { bool IsDng; }`; `LightroomIndex { string Folder; int Count; int Opened; ImmutableArray<(string Path, int Win32Error)> Errors; ImmutableArray<string> Unreadable; static LightroomIndex Build(string folder, IDirectoryLister lister, PhotoExifCache exif, DateOnly from, DateOnly to, IProgress<PhotoScanProgress>? progress, CancellationToken ct); static LightroomIndex Empty(string folder); IReadOnlyList<LightroomPhoto> SameSecond(ExifStamp stamp); }` | `UasSort.Core.Cleanup` | P.8 | P.9, P.12 |
| `PhotoCleanupVerifier` | static class | `static readonly TimeSpan HyperlapseWindow, PanoramaStitchWindow` (2 min); `sealed record Context(LightroomIndex Index, LedgerSnapshot Ledger, ImmutableArray<PhotoMember> PhotoRootMembers, ImmutableArray<PhotoItem> FlatPhotos)`; `static Context ContextFor(IReadOnlyList<PhotoItem> items, LightroomIndex index, LedgerSnapshot ledger)`; `static PhotoVerification Verify(PhotoItem item, Context ctx)` | `UasSort.Core.Cleanup` | P.9 | P.9 |
| `PhotoCleanupStop`, `PhotoCleanupOutcome` (+ `PhotoRecycled`, `PhotoSkippedChanged`, `PhotoPartlyRecycled`, `PhotoRecycleFailed`, `PhotoNotStarted`), `PhotoCleanupProgress`, `PhotoCleanupEnvironment`, `PhotoCleanupResult` | enum / closed records / records | per Task P.10 Interfaces | `UasSort.Core.Cleanup` | P.10 | P.12, P.14 |
| `PhotoCleanupExecutor` | static class | `static Task<PhotoCleanupResult> RunAsync(ConfirmedPhotoCleanupPlan confirmed, PhotoCleanupEnvironment env, IProgress<PhotoCleanupProgress> progress, CancellationToken ct)` | `UasSort.Core.Cleanup` | P.10 | P.12 |
| `PhotoCleanupReportLine`, `PhotoCleanupReport`, `PhotoCleanupReports` | records / static class | per Task P.10 Interfaces; `CoreJsonContext` serializes `PhotoCleanupReport` | `UasSort.Core.Cleanup` | P.10 | P.11, P.12 |
| `PhotoRootThumbnails` | sealed class | `PhotoRootThumbnails(IPhotoFileReader reader) : IThumbnailSource { const string KeyPrefix = "photo-root:"; const long MaxWholeJpegBytes; static ItemId KeyOf(PhotoItem item); static bool IsKey(ItemId id); void Use(string photoRoot, IEnumerable<PhotoItem> items); }` | `UasSort.Core.Cleanup` | P.12 | P.12, P.14 |
| `FolderFacts` | static class | `static bool Exists(string path)` (attributes only) | `UasSort.Platform.Io` | P.11 | P.14 |
| `GuardedPhotoFileReader` | sealed class | `GuardedPhotoFileReader(Settings settings, string appDataDir, string machine, IPathFacts facts) : IPhotoFileReader` | `UasSort.Platform.Io` | P.11 | P.11 |
| `WindowsPhotoRootRecycler`, `WindowsPhotoRootRecyclerFactory` | sealed classes | recycler (internal ctor `(GuardContext ctx)`) : `IPhotoRootRecycler`; `WindowsPhotoRootRecyclerFactory(Func<Settings> settings, string appDataDir, string machine, IPathFacts facts) : IPhotoRootRecyclerFactory` — the only code that moves anything out of the photo root (source-guard test) | `UasSort.Platform.Io` | P.11 | P.11 |
| `StaWorker`, `FileOperationCom`, `IFileOperation`, `IFileOperationProgressSink`, `RecycleSink` | internal | per Task P.11 (`[GeneratedComInterface]`/`[GeneratedComClass]`, STA-only) | `UasSort.Platform.Io` / `.Win32` | P.11 | P.11 |
| `RecycleBinPurge` | internal static class | `static int PurgeOwn(string scratchRoot)` (Platform.Tests through `InternalsVisibleTo`, and `SelfTestSandbox` only; own `%TEMP%` scratch items only) | `UasSort.Platform.Stores` | P.11 | P.11 |
| `SelfTestSandbox` (amended) | sealed partial class | adds `int PurgeOwnRecycleBinItems()` | `UasSort.Platform.Stores` | P.11 | P.15 |
| `PhotoCleanupContext`, `PhotoCleanupAvailability` | record / static class | `PhotoCleanupContext(bool CommitRunning, bool ScanRunning, bool CardCleanupOpen, bool RootsConfirmed, string PhotoRoot, bool PhotoRootAvailable)`; `static string? Reason(PhotoCleanupContext c)` | `UasSort.Review` | P.12 | P.13 |
| `PhotoCleanupPreparation`, `PhotoCleanupEngine`, `PhotoCleanupPorts`, `PhotoCleanupEngines` | records / static class | per Task P.12 Interfaces | `UasSort.Review` | P.12 | P.13, P.14 |
| `PhotoCleanupStep`, `PhotoCleanupRowVm`, `PhotoCleanupResultVm`, `PhotoCleanupVm` | enum / classes | per Task P.12 Interfaces | `UasSort.Review` | P.12 | P.13–P.15 |
| `PhotoCleanupPage` | Page | `PhotoCleanupVm Vm`; `internal static void ResyncDecision(ToggleButton b)` | `UasSort.App.Pages` | P.14 | P.15 |
| `PhotoCleanupFixtures`, `FakePhotoFileReader`, `FakePhotoRootRecyclerFactory` | test helpers | per Tasks P.5 and P.6 | `UasSort.Testing` | P.5, P.6 | P.6–P.13 |
```

- [ ] **Step 3: README**

Insert before `## First-real-card acceptance` in `README.md`:

```markdown
## Clean up Picture Offload

After an offload, photos land in the photo folder ("Picture Offload"). Once you have imported them into Lightroom (Copy as DNG into your Lightroom library folder), the Picture Offload copies only use OneDrive space. **Clean up Picture Offload…** (in the title bar, or Settings → Picture Offload) moves them to the Windows Recycle Bin:

1. **Choose** a day: photos shot on or before it (local time where they were shot) are candidates. Optionally switch on **Verify against Lightroom** (set Settings → Lightroom library folder first): then only items found in the Lightroom library start on Delete — a photo by its capture time (to the sub-second when both files have it) and camera model, a panorama by all its frames or by DJI's stitched image, a hyperlapse by its result video in the video library.
2. **Review** the list, grouped by day: each photo (a DNG and its JPG are one row) and each set folder has Keep/Delete, plus Delete all / Keep all. Items whose date can't be read without downloading them (online-only OneDrive files with no history record) and sets that run past the chosen day are listed as not eligible; other files are never touched.
3. **Confirm** ("Move N items … to the Recycle Bin"; a second confirmation when an unverified item is set to Delete), watch the progress (Stop works between items), and read the result. The report is saved in `%LOCALAPPDATA%\uas-sort\reports\…-photos.json`.

Safety: only items directly in Picture Offload, and only the ones you confirmed, are moved, and only to the Recycle Bin — if Windows would delete an item permanently instead, it is kept and reported. An item that changed since the review is skipped. Online-only files are never downloaded. The Lightroom folder is only read, and `D:\LR_Catalog` (and any `*.lrcat*` or `*.lrdata`) is never opened. Every moved file gets a `photoDelete` record in the history, so a later card that still holds the photo shows it as Imported, and Card cleanup accepts it as evidence.

**First use (acceptance, spec 2026-10-04 §8):** (1) set the Lightroom library folder; (2) on a scratch copy — a test folder set as a temporary photo folder — move one photo and one set, including one online-only OneDrive file, and check the Windows / OneDrive recycle bins and what the result says for the online-only file; (3) a real run with a cutoff covering one old day in verify mode: check the Recycle Bin, the `photoDelete` records in `<videoRoot>\.uas-sort\`, and that a rescan of a card holding those photos shows them Imported.
```

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/plans/2026-09-27-uas-sort/00-interfaces.md README.md docs/superpowers/specs/2026-10-04-picture-offload-cleanup-design.md docs/superpowers/plans/2026-10-04-picture-offload-cleanup.md
git commit -F - <<'EOF'
docs: register the Picture Offload cleanup interfaces and document the page

<session trailer>
EOF
```

---

### Task P.17: Full verification and deploy

**Files:** none changed (unless a check fails; then fix in a separate `fix:` commit and re-run this task from Step 1).

- [ ] **Step 1: Full suite, banned-API check, deploy-script tests**

Run: `dotnet test --solution uas-sort.slnx`
Expected: every test passes; the only skipped tests are the `UASSORT_GOLDEN` golden-media tests (never set it).
Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1 -CheckBannedApi`
Expected: `OK: 73 banned entries, 73 probe calls, 73 RS0030 (one per call)`, exit 0.
Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/deploy.tests.ps1`
Expected: all deploy checks pass.
Confirm: `Get-ChildItem $env:TEMP -Filter 'uas-sort-test-*'` and `-Filter 'uas-sort-selftest-*'` return nothing, and no Recycle Bin item names a `uas-sort-test-*` or `uas-sort-selftest-*` path.

- [ ] **Step 2: Pick the version**

Run: `Get-ChildItem "$env:LOCALAPPDATA\Programs\uas-sort" -Directory | Sort-Object { [version]$_.Name } | Select-Object -Last 1 -ExpandProperty Name`
Expected: the newest installed version (0.1.5 when this plan was written). Use the next patch number (0.1.6 then) as `<next>`; never reuse an installed number and never pass `-Force`.

- [ ] **Step 3: Deploy through the Native AOT gate**

`deploy.ps1` records untracked files as a dirty tree: the user's untracked `.vscode/` folder may be moved into the session scratchpad for the run and moved back afterwards, verified byte-identical by SHA-256 — never delete, add or commit it.
Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/deploy.ps1 -Version <next>` (no `-Force`, no `-AllowNoPlaceholders`)
Expected: `GATE PASS: warm first frame NNN ms (limit 1000 ms; ReadyToRun baseline 370 ms)` and `DEPLOYED uas-sort <next> (win-x64) to …\Programs\uas-sort\<next>`, exit 0; the summary `artifacts\deploy-win-x64.json` shows the current `git rev-parse HEAD`, `dirty : False`, and `slowerThanBaseline` either way. Both selftest runs include `photoCleanup.flow` (a real Recycle Bin move of one sandbox file under Native AOT, which proves the source-generated COM path).

**USER STOP RULE:** if the AOT gate fails a concrete check (a build, trim or AOT warning, a selftest failure, a warm first frame over 1000 ms), diagnose with a hand-run ReadyToRun publish as `docs/superpowers/plans/2026-09-27-uas-sort/13-aot-deploy.md` describes; do **not** switch to ReadyToRun or commit a workaround — report BLOCKED with the diagnosis and wait for the user.

- [ ] **Step 4: Report**

No commit is expected (the deploy writes only ignored artifacts; `git status --porcelain` shows nothing but `?? .vscode/`). Report: the version, size, cold/warm first frame, `slowerThanBaseline`, the install path, that no `msedgewebview2` with a `uas-sort-selftest` user-data-dir lingers, that no `%TEMP%\uas-sort-selftest-*` folder is left, and the user acceptance still open (README "First use", spec §8: the online-only OneDrive recycle behaviour and the stitched-panorama identification are UNVERIFIED until then).

---

## Spec coverage map

| Spec 2026-10-04 | Where |
|---|---|
| §1 purpose; Recycle Bin; photos and set folders; separate page | P.10–P.14 |
| §2 entry (title bar + Settings link; enabled rules; visible reason) | P.12 (`PhotoCleanupAvailability`), P.13 (`ShellVm`), P.14 (button, reason text, Settings link) |
| §2 Settings `LightroomFolder` (optional, picker, validation; verify only when set and available) | P.1, P.13, P.14, P.12 (`VerifyUnavailableText`) |
| §2 Step 1 cutoff + verify switch | P.12 (`PickDate`, `Verify`), P.14 |
| §2 Step 2 rows by day, thumbnails (never hydrating), size, status, Keep/Delete, defaults per mode, totals, Delete all / Keep all, reasons | P.9 (`DefaultDelete`), P.12 (rows, texts, `PhotoRootThumbnails`), P.14 |
| §2 Step 3 acknowledgements, progress, cancel, result, report path | P.5 (`Confirm`), P.10, P.12, P.14 |
| §3 eligibility (direct children only; set and DNG+JPG pair all-or-nothing; capture-time order ledger record for that `Dest` → local EXIF with offset or the drone clock as the scan does (`PhotoCaptureClock`) → cloud-only not eligible; not-touched note) | P.7, P.9 |
| §4 Lightroom index (read-only, range ± 1 day, mtime/folder pruning, catalog exclusions, cloud-only never opened), photo / set frames by a Lightroom **DNG** only, hyperlapse (same session, or same known serial within ± 2 min), panorama (the one panorama-shaped stitched JPG; Lightroom DNG or JPG), date-mode label | P.8, P.9 |
| §5 `IPhotoRootRecycler` (Platform only, Recycle Bin, sets as a unit), guard op and refusals, source-guard test, re-check before the move, `ConfirmedPhotoCleanupPlan`, Lightroom read-only, offload lock | P.5, P.6, P.10, P.11 |
| §6 `photoDelete` record (fields, one per file, after each move, failure reported), newness rule-1, card-cleanup evidence, report JSON, snapshot before the first append | P.3, P.4, P.10, P.12 |
| §7 components and layout | File Structure; P.1–P.15 |
| §8 Core tests, Platform tests in `%TEMP%\uas-sort-test-*` purging only their own items, selftest, user acceptance | P.1–P.11, P.15, P.16 (README), P.17 |
| §9 out of scope (previous photo roots, permanent delete, Lightroom writes/catalog, scheduling, CLI) | guard (P.6) and survey scope (P.7); nothing else built |

## Review notes

Two plan reviews (2026-10-04) raised 21 issues; all were checked against the code. Decisions that the reviews did not settle, or where the plan deliberately differs from a reviewer's suggestion:

- **Issue 17 (`$Recycle.Bin` source guard):** confirmed — `src/UasSort.Core/Card/CardClassifier.cs` has `Eq(seg[0], "$RECYCLE.BIN")` in code (it skips a card's own recycle folder). The check keeps scanning all of `src` (so App and Cli stay covered) and exempts that one file with its reason.
- **Issue 12 (`RecycleBinPurge` outside the guard):** made `internal`; Platform.Tests reach it through the existing `InternalsVisibleTo`, the app only through `SelfTestSandbox.PurgeOwnRecycleBinItems`, both pinned by the source-guard test. It still does not consult `IoGuardPolicy` — the guard has no model of the Recycle Bin — and the Global Constraints now name it as the one documented exception, bounded by its own-`%TEMP%`-root check.
- **Issue 2 (hyperlapse by result video):** implemented as spec §4 is written (session match, or ± 2 min on the same known serial). Consequence worth a decision by the user: DJI stills normally carry no session or drone serial in the ledger (sessions come from MP4s only), so in practice a hyperlapse set verifies only when every frame is in Lightroom; otherwise it shows "a video in the library was shot then, but these frames carry no drone serial — can't tell it is this hyperlapse's result" and starts on Keep. If the user wants the result video to verify hyperlapses, the spec needs another link between frames and video (for example a serial read from the DNG's EXIF/XMP — not available today).
- **Issue 3 (stitched panorama):** narrowed (one JPG-only candidate in the window, panorama-shaped ≥ 2:1, in Lightroom). This needs the pixel size, so Task P.2 also reads EXIF PixelXDimension/PixelYDimension (or the JPEG frame) into `StillInfo` and `PhotoMember.Pixels`. A wide-angle pano narrower than 2:1 stays unverified (safe side). Still UNVERIFIED against real DJI output; acceptance step 3 checks it.
- **Issue 7 (same-second peers):** stamp-less peers are found by their ledger/EXIF `CaptureUtc` to the second, compared with the matched shot's `CaptureUtc`. A cloud-only shot with no ledger record has no capture time at all and cannot be counted; it is itself "date unknown" and never eligible, and the remaining risk (it shares a second with an eligible burst shot) only matters when Lightroom has the burst's DNGs without sub-seconds.
- **Issue 8 (ledger fallback):** the fallback by name and size was dropped (Dest only, as the spec says), and a record whose size no longer matches the file at that path is not used either (resolved ambiguity 8a).
- **Issue 13 (date mode and the second acknowledgement):** the plan follows spec §2 (second acknowledgement only in verify mode) and makes the date-mode acknowledgement say "— not checked against Lightroom". **Open question for the user:** should date mode also require the "may hold their only copy" second checkbox (spec §1's assumption read strictly)? It is a one-line change in `PhotoCleanupPlan.Confirm`/`PhotoCleanupVm.UpdateTexts` if wanted.
- **Issue 18 (stale settings on the Settings stage):** besides using the page's `Current`, `OpenPhotoCleanup` flushes the page's pending save first, so `WindowsPhotoRootRecyclerFactory` (which re-reads the saved photo root) agrees with the plan, and the shell re-evaluates availability when the page's photo root, video root or Lightroom folder changes.
- **Issue 19 (canonical Lightroom folder):** canonicalised once, in `CompositionRoot.CreatePhotoCleanup`; `GuardContexts.ForPhotoCleanup` canonicalises again (idempotent), so the index listing, its reads and the reader's guard all use one form.
- **Issues 15 and 16:** applied exactly as the reviewer verified by building (`(object)op is ComObject com`; `CancellationToken? ct = null` → `ct ?? TestContext.Current.CancellationToken`).
