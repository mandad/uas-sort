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

**Gate result: FAILED (selftest failure in the Native AOT build).** The stop rule applies: `PublishAot` is unchanged, no
`PublishReadyToRun` was added, no fix is committed, and Part 02 does not start until the user decides.

| Check | Result |
|---|---|
| SDK (`dotnet --version`) | 11.0.100-rc.1.26425.128 |
| First restore (Task 01.1) | about 0.35 min (21 s) for the 4 projects; whole first build 27 s; NU1603/NU1507: none |
| `AnalysisLevel` in effect | `11.0-recommended` (`EffectiveAnalysisLevel` 11.0; `analysislevel_11_recommended.globalconfig` listed under `EditorConfigFiles`, since this SDK leaves `GlobalAnalyzerConfigFiles` empty) |
| Toolkit 8.3.260402-preview2 (Sizers, SettingsControls) | restored and rendered in Debug (GridSplitter, SettingsCard in `probePage`); not reached in the AOT build (see below) |
| Banned-API probe | `OK: 73 banned entries, 73 probe calls, 73 RS0030 (one per call)` |
| Cross-assembly exhaustiveness | green; removing an arm gives CS8509 (closed and union) |
| AppInstance (Debug) | second launch exits 0, first window comes forward; title mechanism `uas-sort (stack proof, single instance: AppInstance)` |
| Named-mutex fallback | `--single-instance-mutex`: second launch exits 0 (title `… single instance: Mutex`) |
| Native AOT publish | exit 0, 0 warnings, 0 errors with TreatWarningsAsErrors (clean `bin/obj` Release, ILC "Generating native code", 27 s); NoWarn IL2104/IL3053 added: no (none raised) |
| AOT size | 133.4 MB / 241 files (of which `uas-sort.pdb` 58.1 MB; 75.2 MB / 237 files without the `.pdb` files; `uas-sort.exe` 18.0 MB; no `uas-sort.dll`) — ReadyToRun baseline 87–97 MB |
| `--selftest` run 1 (cold) `firstFrameMs` | -1 — **FAIL**: exit 1, `ok` false, one check `harness`: `unhandled: ArgumentException: The parameter is incorrect. Argument 'source' is not a supported vector.` |
| `--selftest` run 2 (warm) `firstFrameMs` | -1 (same failure) — gate ≤ 1000 ms; ReadyToRun baseline 370 ms; slowerThanBaseline: False (printed by the runner; meaningless here, no frame was measured) |
| WebView2 runtime | not reached in the AOT build; 153.0.4234.48 (Task 01.0, and the `webView2` check detail of the diagnosis build below) |
| MetadataExtractor under trimming | not reached in the AOT build (the harness failed first); `metadataExtractor` check passed in the diagnosis AOT build below (DTO 2026:09:27 14:01:27; GPS 57.5368,-153.7484) |
| VS Code C# union/closed | pending (asked the user) |

### Failure and diagnosis

Gate command: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-selftest.ps1 -Exe artifacts/stack-proof/win-x64/uas-sort.exe -Runs 2 -MaxWarmFirstFrameMs 1000 -BaselineMs 370`
→ both runs `Exit 1`, `Ok False`, `FirstFrameMs -1`, `Checks 1`, `Failed harness: unhandled: ArgumentException …`; `selftest failed`, exit 1.

Hand-run ReadyToRun diagnosis (the stop rule's command):
`dotnet publish src/UasSort.App/UasSort.App.csproj -c Release -r win-x64 -p:PublishAot=false -p:PublishTrimmed=true -p:PublishReadyToRun=true -o artifacts/stack-proof/r2r-diagnosis`
→ exit 0, 0 warnings, 0 errors; 101.3 MB / 318 files. The same selftest **also fails**: both runs `Exit 1`, `Ok False`,
`FirstFrameMs -1`, `harness: unhandled: NullReferenceException: Object reference not set to an instance of an object.`
So the failure is **not AOT-specific**: it is a trimming failure shared by both trimmed builds. The untrimmed builds
pass: Debug (warm 543 ms, 8/8) and the Release JIT build in `bin/Release` (cold 967 ms, warm 519 ms, 8/8).

Stack traces (from a temporary, uncommitted change that wrote `ex.ToString()` into the result file):

- AOT: `ArgumentException … Argument 'source' is not a supported vector.` at
  `ABI.Microsoft.UI.Xaml.Controls.IItemsViewMethods.set_ItemsSource` ← `StackProbePage_obj1_Bindings.Update_Vm` ←
  `Loading` (the `x:Bind Vm.Items` → `ItemsView.ItemsSource` setter).
- Trimmed ReadyToRun: `NullReferenceException` at `WinRT.TypeExtensions.GetAbiToProjectionVftblPtr` ←
  `ComWrappersSupport.GetInterfaceTableEntries` (JIT fallback) ← `ComWrappers.CreateManagedObjectWrapper` ←
  `MarshalInspectable.CreateMarshaler2` ← the same `IItemsViewMethods.set_ItemsSource` in `Update_Vm`.

Cause: the `ObservableCollection<ProbeItemVm>` handed to `ItemsView.ItemsSource` has no CsWinRT-generated CCW vtable
(`IBindableVector`/`IVector`/`IIterable`), so the native `ItemsView` gets an object it cannot use as a vector. CsWinRT
2.3.1's AOT generator (in Microsoft.Windows.SDK.NET.Ref 10.0.26100.87) did not emit one because the App project does
not set `AllowUnsafeBlocks`. At the default `CsWinRTAotWarningLevel` 1 this is silent; building the App with
`-p:CsWinRTAotWarningLevel=2` reports it:

```
StackProbePage.g.cs(501,21): error CsWinRT1030: Type 'System.Collections.ObjectModel.ObservableCollection<UasSort.App.Pages.ProbeItemVm>' implements generic WinRT interfaces which requires generated code using unsafe for trimming and AOT compatibility if passed across the WinRT ABI. Project needs to be updated with '<AllowUnsafeBlocks>true</AllowUnsafeBlocks>'.
XamlTypeInfo.g.cs(1437,13): error CsWinRT1030: Type 'System.Threading.Tasks.Task<string>' … '<AllowUnsafeBlocks>true</AllowUnsafeBlocks>'.
Program.cs(35,23): error CsWinRT1030: Type 'string[]' … '<AllowUnsafeBlocks>true</AllowUnsafeBlocks>'.
SingleInstanceGate.cs(14,23): error CsWinRT1028: Class 'SingleInstanceGate' implements WinRT interfaces but it or a parent type isn't marked partial.
```

(Level 2 on Platform also reports CsWinRT1028 for `NamedMutexLock`.) Adding
`[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(ObservableCollection<ProbeItemVm>))]` alone did not help:
the generator emitted nothing for it without unsafe code.

Verified candidate fix (diagnosis only, **not committed**, build configuration unchanged): publishing with
`-p:AllowUnsafeBlocks=true` makes both trimmed builds pass the gate with 0 warnings:

| Build (diagnosis, `-p:AllowUnsafeBlocks=true`) | Size | Cold `firstFrameMs` | Warm `firstFrameMs` | Checks |
|---|---|---|---|---|
| Native AOT (`artifacts/stack-proof/diag-aot-unsafe`) | 138.7 MB / 241 files | 347 | 261 (slowerThanBaseline: False) | 8/8 pass |
| Trimmed ReadyToRun (`artifacts/stack-proof/diag-r2r-unsafe`) | 101.3 MB / 318 files | 978 | 360 (slowerThanBaseline: False) | 8/8 pass |

Decision needed from the user: whether to set `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in
`src/UasSort.App/UasSort.App.csproj` (and whether to raise `CsWinRTAotWarningLevel` to 2 so such gaps fail the build,
which also needs the CsWinRT1028/1030 findings above fixed), then re-run this task's gate.
