# Stack proof (Part 01, Ref §14 step 1): measured results

## Toolchain (Task 01.0)

Recorded 2026-09-28 on the development PC (win-x64).

| Tool | Version |
|---|---|
| winget | v1.29.290 |
| Git | git version 2.55.0.windows.5 |
| PowerShell 7 | PowerShell 7.6.6 |
| .NET SDKs | 9.0.316, 11.0.100-rc.1.26425.128 |
| C++ build tools | C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools (MSVC 14.44.35207) |
| Windows SDK 10.0.26100 | present |
| WebView2 runtime | 153.0.4234.48 |

## Native AOT stack proof (Task 01.12)

Date: 2026-09-28. Machine: the development PC, win-x64.

**Gate result: PASSED** (after the user-approved CsWinRT interop fix below; `PublishAot` unchanged, no `PublishReadyToRun`).

| Check | Result |
|---|---|
| SDK (`dotnet --version`) | 11.0.100-rc.1.26425.128 |
| First restore (Task 01.1) | about 0.35 min (21 s) for the 4 projects; whole first build 27 s; NU1603/NU1507: none |
| `AnalysisLevel` in effect | `11.0-recommended` (`EffectiveAnalysisLevel` 11.0; `analysislevel_11_recommended.globalconfig` listed under `EditorConfigFiles`, since this SDK leaves `GlobalAnalyzerConfigFiles` empty) |
| Toolkit 8.3.260402-preview2 (Sizers, SettingsControls) | restored and rendered (GridSplitter, SettingsCard in `probePage`, also in the AOT build: "ItemsView realised both templates (x:Bind text), SettingsCard and GridSplitter rendered") |
| Banned-API probe | `OK: 73 banned entries, 73 probe calls, 73 RS0030 (one per call)` |
| Cross-assembly exhaustiveness | green; removing an arm gives CS8509 (closed and union) |
| AppInstance (Debug) | second launch exits 0, first window comes forward; title mechanism `uas-sort (stack proof, single instance: AppInstance)` |
| Named-mutex fallback | `--single-instance-mutex`: second launch exits 0 (title `… single instance: Mutex`) |
| Native AOT publish | exit 0, 0 warnings, 0 errors with TreatWarningsAsErrors and `CsWinRTAotWarningLevel` 2 (clean `bin/obj` Release, ILC "Generating native code", 40 s); NoWarn IL2104/IL3053 added: no (none raised) |
| AOT size | 133.6 MB / 241 files (75.2 MB / 237 files without the `.pdb` files; `uas-sort.exe` 18.1 MB; no `uas-sort.dll`) — ReadyToRun baseline 87–97 MB |
| `--selftest` run 1 (cold) `firstFrameMs` | 461 (exit 0, `ok` true, 8/8 checks pass) |
| `--selftest` run 2 (warm) `firstFrameMs` | 256 (exit 0, `ok` true, 8/8 checks pass) — gate ≤ 1000 ms; ReadyToRun baseline 370 ms; slowerThanBaseline: False |
| WebView2 runtime | 153.0.4234.48 (`webView2` check: "ready and pong from https://map.uas-sort.example/probe.html") |
| MetadataExtractor under trimming | `metadataExtractor` check ok in the AOT build (DTO 2026:09:27 14:01:27; GPS 57.5368,-153.7484) |
| VS Code C# union/closed | pending (asked the user) |

Gate command: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-selftest.ps1 -Exe artifacts/stack-proof/win-x64/uas-sort.exe -Runs 2 -MaxWarmFirstFrameMs 1000 -BaselineMs 370`
→ `OK: 2 run(s), warm first frame 256 ms`, exit 0.

### Resolved: CsWinRT interop (AllowUnsafeBlocks + CsWinRTAotWarningLevel 2), user-approved 2026-09-28

First attempt (commit c673405): the AOT build failed the selftest in both runs (exit 1, `harness: unhandled:
ArgumentException … Argument 'source' is not a supported vector.`). The stop rule's hand-run trimmed ReadyToRun publish
(`-p:PublishAot=false -p:PublishTrimmed=true -p:PublishReadyToRun=true -o artifacts/stack-proof/r2r-diagnosis`, 0
warnings, 101.3 MB / 318 files) failed the same way (`NullReferenceException` in
`WinRT.TypeExtensions.GetAbiToProjectionVftblPtr`), so the failure was trimming, not AOT; untrimmed Debug and Release
JIT builds passed 8/8.

Root cause: both stack traces end in `ABI.Microsoft.UI.Xaml.Controls.IItemsViewMethods.set_ItemsSource` from the
`{x:Bind Vm.Items}` binding. CsWinRT 2.3.1's AOT generator emits CCW vtables (`IBindableVector`/`IVector`/`IIterable`)
for `ObservableCollection<ProbeItemVm>` only when the project sets `AllowUnsafeBlocks`; the App did not, so the native
`ItemsView` received an object it could not use as a vector. At the default `CsWinRTAotWarningLevel` 1 this was
silent; level 2 reports it as CsWinRT1030 (also for `Task<string>` in XamlTypeInfo.g.cs and `string[]` in Program.cs),
plus CsWinRT1028 for the non-partial `SingleInstanceGate` (and `NamedMutexLock` in Platform).

Fix (user-approved, nothing else in the build configuration changed): the App csproj sets
`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` and `<CsWinRTAotWarningLevel>2</CsWinRTAotWarningLevel>` (CsWinRT AOT
diagnostics are now build errors under TreatWarningsAsErrors); `SingleInstanceGate` and `NamedMutexLock` are
`partial`. Guard tests in `BuildConfigTests` keep both properties and fail if hand-written App sources use the `unsafe`
keyword. With level 2 no further CsWinRT diagnostic was raised.
