# Most-modern stack check for uas-sort (as of 2026-09-27)

WinUI 3 has no showstopper. Recommended stack: **.NET 11 RC1 now, switching to GA on Nov 10, with C# 15**, and **WinUI 3 on Windows App SDK 2.5.1**. Ship it **unpackaged and self-contained** (it carries its own .NET and Windows App SDK), **trimmed and precompiled with ReadyToRun**, as a folder with a Start-menu shortcut.

I proved this with a build on this machine. It used an SDK installed only in a temp folder, built entirely from the command line (called from WSL), and was run briefly off-screen. It exercised the parts the review UI needs: title bar, Mica background, tree, editable table, thumbnail strip, settings card, segmented control, compiled bindings, MVVM Toolkit, and C# 15 unions and closed hierarchies.

This reverses the earlier WPF / .NET 10 recommendation in `dotnetR.md`, as your decision #2 asks.

## 1. .NET

| | Version | Date | Support |
|---|---|---|---|
| .NET 10 (LTS) | runtime 10.0.12, SDK 10.0.401 | 2026-09-08 | until 2028-11-14 |
| .NET 11 (STS) | 11.0.0-rc.1.26425.128, SDK 11.0.100-rc.1.26425.128 | 2026-09-08 | "go-live" (production use allowed) |

- **.NET 11 status:** Microsoft's release index lists it as `support-phase: go-live`. RC1's go-live support runs Sep 8 to Oct 13, so RC2 around Oct 13 is my inference (UNVERIFIED). GA launches at .NET Conf, Nov 10–12.
- **Support length is the same either way.** Short-term releases now get 24 months, so .NET 11 ends around November 2028 (exact date UNVERIFIED). That is essentially when .NET 10 LTS ends (2028-11-14).
- **C# 15 is the default for net11.** It includes union types, `closed` class hierarchies, collection-expression arguments (`[with(StringComparer.OrdinalIgnoreCase), ...]`), extension indexers, and labeled `break`/`continue`.
  - Unions, closed hierarchies and collection-expression arguments compiled and passed tests in the spike.
  - Use unions for results such as copy outcome and GPS probe result, and closed records for plan edits, so `switch` statements are checked for completeness.
  - Skip the memory-safety rules; they are still preview and need a feature flag.
- **C# 14 (already in .NET 10):** extension members, the `field` keyword, null-conditional assignment, partial constructors and events. MVVM Toolkit 8.4.1+ supports source-generated partial properties with C# 14+ without the preview language flag, and I confirmed it on C# 15.
- **Plan:**
  1. `winget install Microsoft.DotNet.SDK.Preview`, currently 11.0.100-rc.1.
  2. Add a `global.json` with `rollForward: latestFeature` and `allowPrerelease: true`, and target `net11.0` / `net11.0-windows10.0.26100.0`.
  3. Move to RC2 in October and GA on Nov 10. Then set `allowPrerelease: false` and install the GA SDK (winget ID `Microsoft.DotNet.SDK.11` is UNVERIFIED).
  - Because the app is self-contained, the PCs never need a .NET runtime installed.
  - Fallback: retarget to `net10.0` and remove the C# 15 syntax.
- **Editors and IDEs:**
  - The .NET 11 RC1 notes name "Visual Studio 2026 Insiders" and VS Code with C# Dev Kit.
  - VS 2026 stable is 18.10.2 (Sep 22); I found no statement that it supports .NET 11 (UNVERIFIED).
  - This machine only has VS Build Tools 2022 17.14, which can't target .NET 10 or 11.
  - For WinUI without Visual Studio: the `dotnet new` templates (`Microsoft.WindowsAppSDK.WinUI.CSharp.Templates` 0.0.7-alpha), the WinApp VS Code extension, and `winapp` CLI 0.7.0 (`winget install Microsoft.WinAppCli`).

## 2. Windows App SDK and WinUI 3

- **Latest stable is 2.5.1 (2026-09-16).** Earlier 2.x releases: 2.0.1 (Apr), 2.1.3, 2.2.0, 2.3.1, 2.4.0 (Aug).
  - Version 2.0 moved to semantic versioning: the next side-by-side release will be 3.0.
  - The 1.8 line is still being patched (1.8.260921001 on 9/24).
  - Experimental 2.4.1 adds pen/ink controls.
- **2.x features that matter here:**
  - TitleBar control with automatic drag regions (2.1).
  - `SystemBackdropElement` for Mica/Acrylic anywhere (2.0).
  - New folder and file pickers with `SuggestedStartFolder` and `PickMultipleFoldersAsync` (2.0), useful for "Browse to folder…".
  - `ApplicationData.GetForUnpackaged()` (2.2).
  - Opt-in XAML performance switches (2.3).
- **Checked in the spike:** TitleBar, Mica, TreeView with bound items, ItemsView with LinedFlowLayout (47 thumbnails realized), and x:Bind.
- **Dark mode:** follows the system automatically (it came up Dark here). There is no WPF-style `ThemeMode`; to override at runtime, set `RequestedTheme` on the root element.
- **MapControl** is powered by Azure Maps (needs a key and a connection), so keep "Open in map" or the separate WebView2 + Leaflet approach.
- **DataGrid: there is no first-party one.**
  - Microsoft's page on tabular data (updated 2026-09-16) says: "Don't use the older UWP Community Toolkit DataGrid in a WinUI 3 app." It suggests ListView/ItemsView or a third-party grid.
  - Microsoft is building a first-party TableView in the WinUI repo (PR #11641, closed 2026-08-31, types marked preview). It is not in any published package yet.
  - **Recommendation: WinUI.TableView 1.5.0** (MIT, 2026-09-17). In the spike, 33 rows rendered with a two-way checkbox column on Windows App SDK 2.5.1 / net11.
  - Use `TableViewTemplateColumn` with x:Bind; the trimming section below explains why.
- **Community Toolkit for WinUI:**
  - Stable 8.2.251219 (2025-12-20) requires the full `Microsoft.WindowsAppSDK` package (≥1.6). It works with 2.5.1 when you reference the full package (SettingsCard and Segmented verified).
  - 8.3.260402-preview2 depends only on the WinUI component package. That allows a "lean" build and it worked on WinUI 2.3.9, but there is no stable 8.3 yet.
- **MVVM Toolkit:** 8.4.2 (2026-03-25).

## 3. Packaging (measured, win-x64 Release)

"Warm" is process start to first rendered frame on the 2nd and 3rd runs. "Lean" means referencing only the WinUI component package instead of the full Windows App SDK package, which also pulls in AI, ML and search components.

| Variant | Size / files | Warm first frame | Result |
|---|---|---|---|
| Self-contained, full Windows App SDK package | 244 MB / 540 | 600–770 ms | ok |
| Same, trimmed | 142 MB / 346 | ~820 ms | **the table's checkbox binding silently broke (0 ticked)**; fixed with x:Bind columns |
| Self-contained, lean (with toolkit 8.3 preview) | 184 MB / 473 | ~610 ms | ok |
| Lean + trimmed | 84 MB / 291 (34 MB zipped) | ~840 ms | ok |
| **Lean + trimmed + ReadyToRun** | **97 MB / 291** | **~370 ms** (cold 1.1 s) | **ok, best** |
| Shared .NET runtime, bundled Windows App SDK (lean + ReadyToRun) | 116 MB / 278 | ~440 ms | ok, needs .NET installed |
| Single-file exe, full package | 236 MB exe | ~690 ms | works, but unpacks 226 MB to temp on first run |
| Single-file exe, lean | 92 MB | — | **crashes** (`COMException 0x80040111` "ClassFactory cannot supply requested class") |
| Native AOT | — | — | **won't build**: needs Microsoft's C++ build tools, which aren't installed |
| MSIX package via `dotnet publish` (unsigned) | 32 MB .msix | not run | built without Visual Studio; needs the Windows App Runtime 2 ≥2.5.1 installed |

- **Recommended for a manually launched personal app:** lean + trimmed + ReadyToRun as a folder in `%LOCALAPPDATA%\Programs\uas-sort\<version>\`.
  - A small deploy script copies the folder and writes a Start-menu `.lnk`.
  - No certificate, runtime installer or Developer Mode needed.
  - The app doesn't need an MSIX package identity (no notifications or background tasks).
  - Native AOT can come later; its startup gain is UNVERIFIED.
- **Why not rely on the shared Windows App SDK runtime:** this PC has runtime 2 only up to 2.4.0, and winget's `Microsoft.WindowsAppRuntime.2` is at 2.3.1. Bundling the runtime avoids that.
- **Gotchas found:**
  1. Without `<EnableMsixTooling>true</EnableMsixTooling>`, `dotnet publish` of an unpackaged app leaves out the app's resource files (`.pri`/`.xbf`). The app then crashes at startup (0xC000027B, XamlParseException).
  2. x:Bind in a `Window` only initializes on `Activated`. My probe had to call `Bindings.Update()` itself, and calling it inside a `CompositionTarget.Rendering` handler hung the UI thread.
  3. The 8.2 toolkit with a lean build makes NuGet pull Windows App SDK 1.6 alongside 2.x, and the build fails. Either use toolkit 8.3-preview2 or reference the full `Microsoft.WindowsAppSDK` package.
  4. With lean references, pin `Microsoft.WindowsAppSDK.Foundation` 2.3.12 and `.InteractiveExperiences` 2.1.9 to silence a harmless NU1603 version warning.

## 4. Building from the command line

- **Verified:** `dotnet build`, `publish` and `test`, plus MSIX creation, with only the .NET SDK and NuGet packages; no Visual Studio. The XAML and resource tools come from `Microsoft.Windows.SDK.BuildTools` 10.0.28000.2705.
- **From WSL:** `cd /mnt/c/...; dotnet.exe build` works (15 s incremental). Environment variables pass through via `WSLENV`.
- **First restore is slow:** about 13 minutes, because roughly 1.5 GB of Windows App SDK packages download.
- **Leftover compiler server:** a Roslyn compiler server stayed running and locked files. Set `UseSharedCompilation=false` or run `dotnet build-server shutdown`.
- Keep the repo on `C:\`. Native AOT additionally needs the C++ build tools component.
- Whether the build works without the installed Windows SDK 26100 is UNVERIFIED; the winapp docs say it falls back to the SDK in NuGet.

## 5. Testing

- **Unit tests:**
  - xUnit v3 4.0.1 (2026-09-12) via `xunit.v3.mtp-v2`, with `global.json` set to `"test": {"runner": "Microsoft.Testing.Platform"}`.
  - `dotnet test --project` passed 5/5, covering C# 15 features and a view model.
  - Microsoft.Testing.Platform is 2.4.1; MSTest 4.4.1 is the alternative.
  - The core library tests run through `dotnet.exe` from WSL, so no Linux SDK is needed.
- **UI automation:**
  - WinAppDriver is unmaintained. The Appium Windows driver 5.x still sits on top of it.
  - Use FlaUI.UIA3 5.0.0 (Feb 2025; UNVERIFIED on WinUI here) or `winapp ui` (inspect, click, wait-for, screenshot).
  - Add a startup smoke test that checks bindings actually populate, like the spike's probe. Trimming breaks `{Binding}` silently.

## 6. Other libraries

- MetadataExtractor 2.9.3, GeoTimeZone 6.1.0 and System.IO.Hashing 10.0.12 are the latest stable (System.IO.Hashing also has 11.0.0-rc.1).
- GeoTimeZone produced no trim warnings. MetadataExtractor wasn't called in the spike, so its trim behaviour is UNVERIFIED.
- **Microsoft.Extensions.Hosting:** not worth it for a single window. Use `Microsoft.Extensions.DependencyInjection` (10.0.12, or 11.x at GA) or a hand-written composition root.
- The only trim warning came from WinUI.TableView (IL2026 on `TypeDescriptor.GetConverter`).

## 7. Alternatives

- **WPF on .NET 11:** RC1 has "no new user-facing WPF features", and Fluent styling is still "in progress".
- **Avalonia 12.1.3:** its DataGrid is deprecated and the tree-grid is commercial (from earlier research).
- **Uno Platform 6.7.30:** no benefit for a Windows-only app.

None of these beats WinUI 3 for "most modern native".

## Recommended stack

| Component | Package | Version | Why |
|---|---|---|---|
| SDK / runtime | .NET SDK | 11.0.100-rc.1.26425.128, then 11.0.100 GA on Nov 10 | Go-live now; support lasts about as long as .NET 10 LTS |
| Language | C# 15 | default for net11 | Unions and closed hierarchies for plan and result types |
| UI | Microsoft.WindowsAppSDK.WinUI (+ Foundation 2.3.12) | 2.3.9 (part of Windows App SDK 2.5.1) | Latest stable, lean build |
| Build tooling | Microsoft.Windows.SDK.BuildTools | 10.0.28000.2705 | Builds without Visual Studio |
| Controls | CommunityToolkit.WinUI.Controls.* (SettingsControls, Segmented, Sizers) | 8.3.260402-preview2 (fallback 8.2.251219 with the full package) | Only WinUI dependency |
| Grid | WinUI.TableView | 1.5.0 | Works on 2.5.1 / net11 |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 | Partial properties without preview flag |
| Metadata | MetadataExtractor | 2.9.3 | Latest |
| Time zones | GeoTimeZone | 6.1.0 | Latest |
| Hashing | System.IO.Hashing | 10.0.12, then 11.0.0 | XxHash128 |
| Dependency injection | Microsoft.Extensions.DependencyInjection | 10.0.12, then 11.0.0 (optional) | Skip Hosting |
| Tests | xunit.v3.mtp-v2 | 4.0.1 | Runs on Microsoft.Testing.Platform |
| UI smoke tests | FlaUI.UIA3 and/or winapp CLI | 5.0.0 / 0.7.0 | WinAppDriver is dead |
| Packaging | Unpackaged, self-contained, lean, trimmed, ReadyToRun, folder + Start-menu `.lnk` | n/a | 97 MB, about 0.37 s warm start |
| Editor | dotnet CLI + VS Code (C# Dev Kit, WinApp extension) | n/a | VS 2026 Insiders optional |

## Risks

1. **.NET 11 is a release candidate.** Changes before GA are possible, and RC1 go-live support ends Oct 13, so moving to RC2 and then GA is mandatory.
2. **Windows App SDK 2.x on .NET 11 isn't documented** (the templates list net8/9/10). It is proven only by this spike.
3. **Libraries built against Windows App SDK 1.6/1.8 run across a major version.** The toolkit and TableView worked here; pin versions.
4. **Toolkit 8.3 has been preview-only since April**, and toolkit 8.2 forces the full Windows App SDK package.
5. **The grid depends on a community control** (WinUI.TableView), and Microsoft's own table control may later make a migration worthwhile.
6. **Trimming silently breaks `{Binding}`.** Rules: x:Bind only, template columns in the table, and a startup smoke test. Fallback: don't trim; lean untrimmed is 184 MB, and I didn't measure lean + ReadyToRun untrimmed.
7. **Native AOT and single-file lean builds don't work here.** AOT needs the C++ build tools; single-file lean crashes. Neither is needed for folder deployment.
8. **Startup numbers are approximate.** They were measured with the window off-screen and never activated; cold starts were 1.1–2.3 s.
9. **Visual Studio 2026 stable support for .NET 11 is UNVERIFIED**, and the XAML designer and Hot Reload need Visual Studio.
10. **MSIX install and launch were not tested.**

## Housekeeping

- The temp folder `C:\Users\damia\AppData\Local\Temp\uas-sort-spike-modernstack` (SDK and builds) is deleted.
- The single-file extraction folder `%TEMP%\.net\Spike.Lean` is deleted.
- My 6 crash dumps in `%LOCALAPPDATA%\CrashDumps` and 6 Windows Error Reporting folders in `ProgramData\...\WER\ReportArchive` are deleted.
- `uas-sort-spike-winui` in the same temp folder belongs to a parallel agent and was left alone.
- `C:\dev\uas-sort` is still empty.
- Nothing was installed machine-wide, and no user data was read.
- Sources, logs and the size table are saved in `/tmp/claude-1000/-mnt-c-dev-uas-sort/187b6b83-e3e1-4971-8c9e-c13c3990d115/scratchpad/spikes/modern-stack/` (`winui-src/`, `logs/`, `publish-sizes.txt`, `probe.ps1`).

Sources:
- [Announcing .NET 11 RC1](https://devblogs.microsoft.com/dotnet/dotnet-11-rc-1/)
- [InfoQ: .NET 11 RC1](https://www.infoq.com/news/2026/09/dotnet-11-rc-1-release/)
- [.NET release metadata index](https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json)
- [.NET 11 RC1 release notes](https://github.com/dotnet/core/blob/main/release-notes/11.0/preview/rc1/11.0.0-rc.1.md)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [What's new in C# 15](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15)
- [.NET Conf 2026](https://devblogs.microsoft.com/dotnet/dotnet-conf-2026/)
- [Windows App SDK downloads](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)
- [Windows App SDK 2.x release notes](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0)
- [Microsoft.WindowsAppSDK on NuGet](https://www.nuget.org/packages/Microsoft.WindowsAppSDK)
- [What's new for Windows developers](https://learn.microsoft.com/en-us/windows/apps/whats-new/whats-new-for-developers)
- [Self-contained deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
- [Distribute an unpackaged WinUI app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)
- [Display tabular data in WinUI](https://learn.microsoft.com/en-us/windows/apps/get-started/line-of-business/display-tabular-data)
- [WinUI TableView PR #11641](https://github.com/microsoft/microsoft-ui-xaml/pull/11641)
- [WinUI.TableView on NuGet](https://www.nuget.org/packages/WinUI.TableView)
- [Community Toolkit for Windows releases](https://github.com/CommunityToolkit/Windows/releases)
- [Community Toolkit SettingsControls on NuGet](https://www.nuget.org/packages/CommunityToolkit.WinUI.Controls.SettingsControls)
- [MVVM Toolkit releases](https://github.com/CommunityToolkit/dotnet/releases)
- [CommunityToolkit.Mvvm on NuGet](https://www.nuget.org/packages/CommunityToolkit.Mvvm)
- [dotnet new WinUI templates](https://devblogs.microsoft.com/ifdef-windows/introducing-dotnet-new-templates-for-winui/)
- [WinUI templates on NuGet](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.WinUI.CSharp.Templates)
- [winapp CLI 0.7.0](https://devblogs.microsoft.com/ifdef-windows/winappcli-v0-7-0-release-announcement/)
- [winapp CLI with .NET](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/guides/dotnet)
- [winapp CLI 0.3 (`winapp ui`)](https://devblogs.microsoft.com/ifdef-windows/windows-app-development-cli-v0-3-new-run-and-ui-commands-plus-dotnet-run-support-for-packaged-apps/)
- [Visual Studio 2026 release notes](https://learn.microsoft.com/en-us/visualstudio/releases/2026/release-notes)
- [xunit.v3 on NuGet](https://www.nuget.org/packages/xunit.v3)
- [MSTest on NuGet](https://www.nuget.org/packages/MSTest)
- [Microsoft.Testing.Platform on NuGet](https://www.nuget.org/packages/Microsoft.Testing.Platform)
- [MetadataExtractor on NuGet](https://www.nuget.org/packages/MetadataExtractor)
- [GeoTimeZone on NuGet](https://www.nuget.org/packages/GeoTimeZone)
- [System.IO.Hashing on NuGet](https://www.nuget.org/packages/System.IO.Hashing)
- [Microsoft.Extensions.Hosting on NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Hosting)
- [FlaUI.UIA3 on NuGet](https://www.nuget.org/packages/FlaUI.UIA3)
- [WinAppDriver](https://github.com/microsoft/WinAppDriver)
- [Appium Windows driver](https://github.com/appium/appium-windows-driver)
- [CsWinRT releases](https://github.com/microsoft/cswinrt/releases)
- [Avalonia on NuGet](https://www.nuget.org/packages/Avalonia)
- [Uno.Sdk on NuGet](https://www.nuget.org/packages/Uno.Sdk)
- [What's new in WPF for .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)