# WinUI 3 + WebView2 map spike on .NET 11: it builds, runs, publishes and passes its checks

The stack works from the command line on this machine. I built with the .NET 11 RC1 SDK (C# 15) and WinUI 3 on Windows App SDK 2.5.1. The app is unpackaged and carries its own .NET and Windows App SDK runtimes. The window shows a WebView2 map using a bundled copy of Leaflet, and map clicks reach C# and get logged. Every build variant I launched passed the self-test except Native AOT, which can't build here because the Visual Studio C++ tools aren't installed.

## Versions used (checked on nuget.org, npm and the .NET release index, 2026-09-27)

| Component | Version | Notes |
|---|---|---|
| .NET SDK | **11.0.100-rc.1.26425.128** (runtime 11.0.0-rc.1) | Newest available; Microsoft marks RC1 "go-live". .NET 10.0.401 LTS was the fallback and wasn't needed. Installed with `dotnet-install.ps1 -Version … -InstallDir <temp>\dotnet -NoPath` (3 min 10 s, 702 MB). |
| C# | **15.0** (compiler 5.11.0), the default for `net11.0` | C# 14 extension members compiled fine. |
| Windows App SDK | **2.5.1** (Sep 16), latest stable | I used the component packages rather than the full package (see gotcha 2): `Microsoft.WindowsAppSDK.WinUI` 2.3.9, `.Foundation` 2.3.12, `.InteractiveExperiences` 2.1.9. |
| Windows SDK build tools | 10.0.28000.2705 | Target framework `net11.0-windows10.0.26100.0`, minimum Windows version 10.0.26100.0 |
| WebView2 SDK / runtime | SDK **1.0.4191.47** (newer than the 1.0.3719.77 WinUI asks for; works). Runtime 153.0.4234.48, already installed. | |
| CommunityToolkit.Mvvm | 8.4.2 | Uses the newer `[ObservableProperty] public partial …` style |
| xUnit v3 | `xunit.v3.mtp-v2` **4.0.1** on Microsoft.Testing.Platform v2 | `dotnet test` runs through the new test runner, set in `global.json` |
| Templates | `Microsoft.WindowsAppSDK.WinUI.CSharp.Templates` **0.0.7-alpha** (official Microsoft, Sep 23) and `xunit.v3.templates` 4.0.1 | `dotnet new winui-mvvm`, `dotnet new xunit3` |
| Leaflet | **1.9.4** from the npm registry; sha512 matched npm | 2.0 is still alpha. MapLibre 6.11.2 was the alternative. |
| Machine | Windows 11 build 26200; Visual Studio Build Tools 2022 17.14 **without** the C++ tools | |

## Commands (the dotnet commands ran through `env.ps1`, which keeps everything inside the temp folder)
```
powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1        # dotnet-install.ps1 -Version 11.0.100-rc.1.26425.128 -InstallDir …\dotnet -NoPath
dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates::0.0.7-alpha
dotnet new winui-mvvm -n UasSpike.App -o UasSpike.App
dotnet new classlib -n UasSpike.Core -o UasSpike.Core
dotnet new install xunit.v3.templates::4.0.1
dotnet new xunit3 -n UasSpike.Core.Tests -f net10.0 --test-runner mtp-v2 --command-line mtp   # then retargeted to net11.0
dotnet new sln -n UasSpike            # produces .slnx
dotnet sln UasSpike.slnx add …
dotnet build UasSpike.slnx -c Debug                                   # clean build 22 s, 0 warnings
dotnet test --solution UasSpike.slnx                                  # 4/4 passed, 1.2 s
dotnet publish UasSpike.App/UasSpike.App.csproj -c Release -r win-x64 -o ..\pub\final                         # trimmed + precompiled, set in csproj
dotnet publish UasSpike.App/UasSpike.App.csproj -c Release -r win-x64 -p:PublishSingleFile=true -o …          # after a clean
dotnet publish … -p:PublishAot=true                                   # FAILED, see below
dotnet run --project UasSpike.App                                     # works (about 20 s including the build)
```
The isolation settings in `env.ps1` are process-only:
- `DOTNET_ROOT` and `PATH` point at the temp SDK.
- `DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `NUGET_HTTP_CACHE_PATH`, `NUGET_PLUGINS_CACHE_PATH` and `TEMP`/`TMP` point inside the temp folder.
- `MSBUILDDISABLENODEREUSE=1`, `UseSharedCompilation=false`, `DOTNET_CLI_USE_MSBUILD_SERVER=0`.

## Publish results (win-x64, self-contained .NET and Windows App SDK)

| Variant | Result | Size |
|---|---|---|
| Full Windows App SDK package, trimmed + precompiled (ReadyToRun) | Works | **145.5 MB**, 255 files |
| **Component packages only, trimmed + precompiled** (final) | **Works** | **87.2 MB**, 200 files |
| Component packages, **single file**, compressed, trimmed + precompiled | **Works**, but only after a clean publish (gotcha 5) | **38.9 MB exe**. First launch unpacks 88 MB to the extraction folder. |
| `PublishAot=true` | **FAILED**: "vswhere.exe failed to locate Visual Studio with Microsoft.VisualStudio.Component.VC.Tools.x86.x64" | n/a |

Building with the AOT and trimming analyzers on reported **0 warnings**. Whether the app actually runs under AOT is **UNVERIFIED**; it needs the "Desktop development with C++" Visual Studio component, which is a machine-wide install and your call. Trimming caused no runtime breakage in these runs.

## Startup check (launched with `Start-Process`, self-test driven by the app itself)
Every variant I launched came up. The process stayed alive and responding, the window title was "uas-sort spike", and it started one `msedgewebview2.exe` child. The working set was about 155 MB, not counting the WebView2 processes. When stopped, no WebView2 processes were left behind.

The self-test log for every successful run showed the same sequence:
- The page's `ready` message arrived (Leaflet 1.9.4) and 6 OSM tiles loaded with 0 errors.
- `NavigationCompleted success=True http=200` was logged.
- A "ping" sent from C# came back as `pong`.
- A **real mouse click, sent through the WebView2 DevTools protocol (`Input.dispatchMouseEvent`)**, produced `click` → `CLICK lat=57.33939 lon=-155.22583 distZB=57.17mi`. The distance comes from the Core library's haversine function.
- Moving the R slider from C# to 55 mi produced a `radius-ack` from the page.
- WebView2 `CapturePreviewAsync` wrote a PNG.

Timings are measured from process start:

| Build | Window's `OnLaunched` | Map ready |
|---|---|---|
| Debug | 0.34–0.6 s | 1.2–1.6 s |
| Release folder, first launch | 0.55 s | 1.44 s |
| Release folder, repeat launch | **0.11 s** | **0.91 s** |
| Single file, first launch (unpacks) | 0.69 s | 1.46 s |
| Single file, repeat launch | 0.12 s | 0.92 s |

The screenshot shows the OSM map around Kodiak Island, with the two dots, the 55-mile ring and the status text "Clicked … (57.2 mi from Zachar Bay)".

## Errors hit, and their fixes (for the implementation plan)
1. **The official template's defaults don't fit this app.**
   - It builds a packaged (MSIX) app by default.
   - Every package reference is a floating `Version="*"`.
   - It adds `Microsoft.Windows.SDK.BuildTools.WinApp`, 53 MB and only needed for packaged apps.
   - `-tfm` only offers up to net10.0, yet it produced net11.0 on SDK 11.
   - It ignored `-tpmv 10.0.26100.0` and wrote 17763.
   - The namespace came out as `UasSpike_App`.
   - The view model used the old field-style `[ObservableProperty]`.

   **Fix:** set `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true` and `SelfContained=true`. Keep `EnableMsixTooling=true`, which is still needed to generate the resources file and for single file. Delete the manifest and packaging assets, pin versions centrally in `Directory.Packages.props`, and switch to partial properties.
2. **The full Windows App SDK package is bloated.** It pulls in AI, ML, Search and Widgets: onnxruntime is 20.7 MB and DirectML 17.8 MB, 58 MB of dead weight. **Fix:** reference `Microsoft.WindowsAppSDK.WinUI` and `.Foundation` only.
3. **NU1603 warning:** WinUI 2.3.9 asks for InteractiveExperiences 2.1.8 or later, and 2.1.8 was never published. **Fix:** pin InteractiveExperiences 2.1.9 explicitly.
4. **The first launch showed a blank grey map and no messages.** Leaflet's `L.circle(...).getBounds()` throws before the map has a view, because layers are only added once the map is ready. **Fix:** compute the bounds with `L.latLng(c).toBounds(2*R)`. I also made the page's first lines forward `error` and `unhandledrejection` events to C# through `postMessage`; keep that in the real app.
5. **Single-file crash:** exit code 0xE0434352 with `COMException 0x80040111 ClassFactory cannot supply requested class` at `Application` startup. The cause is a stale Windows App SDK manifest left by an earlier non-single-file build of the same `obj` folder; that build step doesn't notice that `PublishSingleFile` changed. **Fix:** delete `obj/Release` before publishing a single file. Two follow-on points:
   - Expect warning NETSDK1244 ("legacy `IncludeAllContentForSelfExtract`"). Windows App SDK requires that mode for its DLL redirection.
   - `AppContext.BaseDirectory` points at the extraction folder, so the real app must put the WebView2 data folder and its logs under `%LOCALAPPDATA%\uas-sort\` explicitly.
6. **Native AOT is blocked** by the missing Visual Studio C++ tools (see the publish table).
7. **`AppWindow.Resize` takes physical pixels.** At what looks like 175% display scaling, 1200×800 gave a 672×378 CSS-pixel map. Multiply by `XamlRoot.RasterizationScale` or the window DPI.
8. **Overriding `BaseIntermediateOutputPath` on the command line breaks referenced projects** with CS0579 "duplicate AssemblyInfo" errors. Don't do it; use a clean or a separate copy of the source.
9. **The xUnit v3 template drops its own `global.json` in the test folder**, which would override the root SDK pin when running from there. **Fix:** merge its `test.runner` setting into the root `global.json` and delete it. The template's `-f` also only goes up to net10.0, so retarget by hand. CA1707 fires under `AnalysisLevel=latest-recommended`; I added `NoWarn` in the test project because an `.editorconfig` glob didn't take effect.
10. **The first restore is slow and large.** It took 7.7 minutes and filled 2.0 GB of NuGet cache, mostly because of the full package. The first publish also downloads the runtime and precompiler packs and hit one transient "response ended prematurely" error that NuGet retried by itself.
11. **Map setup notes.**
    - Use `SetVirtualHostNameToFolderMapping("uas-sort.example", …, DenyCors)`; a `.example` name avoids the delay a `.local` name causes.
    - Register `WebMessageReceived` before calling `Navigate`: the page's `ready` message and tile events can arrive **before** `NavigationCompleted`.
    - OSM tiles load fine from the virtual host. The OSM tile usage policy is fine for light personal use. Keep the attribution.
12. **WinUI 3 has no built-in DataGrid.** That matters for the review UI, and I didn't evaluate alternatives here (**UNVERIFIED**).
13. **Support window: UNVERIFIED.** .NET 11 is a short-term release. If it follows the 24-month short-term support policy, it would be supported until about November 2028, the same as .NET 10 LTS (2028-11-14). Moving to the final 11.0 release should just need an SDK bump; `global.json` uses `rollForward: latestFeature`.

**Recommended deployment:** the trimmed, precompiled folder publish (87 MB) copied to `%LOCALAPPDATA%\Programs\uas-sort` with a Start-menu shortcut. The single file (39 MB) is optional. Add AOT later if you install the C++ tools.

## Cleanup and safety
- The temp folder `C:\Users\damia\AppData\Local\Temp\uas-sort-spike-winui` is **deleted** (4.2 GB: the SDK, NuGet cache, publish outputs and extraction folder). No spike processes are left running; build servers were shut down.
- Your profile's dotnet and NuGet folders (`~/.nuget`, `~/.dotnet`, `AppData\Local\NuGet`, `Roaming\NuGet`) are unchanged compared with a snapshot taken before I started. Nothing new appeared in your local or roaming AppData or home folder.
- `C:\dev\uas-sort` is still empty. Its folder timestamp changed to 14:12, but I can't attribute that to this spike.

## Files are in /tmp/claude-1000/-mnt-c-dev-uas-sort/187b6b83-e3e1-4971-8c9e-c13c3990d115/scratchpad/spikes/winui/
- `src/`: the working solution (no bin/obj/SDK): `UasSpike.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, the WinUI app (`UasSpike.App/`, including `Web/map.html` and `Web/lib/leaflet`), `UasSpike.Core/Geo.cs` and `UasSpike.Core.Tests/GeoTests.cs`
- `scripts/`: `env.ps1`, `install.ps1`, `r.ps1`, `r.sh`, `launch.ps1` (start, check, screenshot, stop), `devrun.ps1`
- `screenshot.png`: screenshot of the final published app's window
- `webview-capture.png`: the WebView2's own capture
- `logs/`: build, test and publish output, each launch's log, `debug1-bug-blank-map.png`, and the before/after snapshots of your profile folders