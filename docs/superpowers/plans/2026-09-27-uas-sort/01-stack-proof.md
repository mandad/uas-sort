# Part 01 — Stack proof

This part scaffolds the whole repository (solution, every `src` and `tests` project, central pins, props, analyzers, scripts) and proves, before any product code exists, that the chosen stack builds, runs and **publishes Native AOT with `TreatWarningsAsErrors`**. It implements Ref §2.1–2.7 (stack, layout, banned APIs, build configuration, commands, spike pitfalls), the launch half of Ref §4.4 step 1 (`Program.Main`, AppInstance with the `Local\uas-sort` named-mutex fallback), the stack-proof subset of Ref §13 (minimal `--selftest`, the BannedSymbols content test, the analyzer probe, the cross-assembly union/closed exhaustiveness test) and Ref §14 step 1. It settles the step-1 UNVERIFIED items: `AnalysisLevel` 11.0, MetadataExtractor under trimming, AppInstance, Sizers, cross-assembly exhaustiveness, and Native AOT size and warm start (hard gate: warm first frame ≤ 1000 ms; compared with the 0.37 s ReadyToRun baseline). **If the Native AOT publish fails any concrete check (Task 01.12), stop and raise it with the user; no later part starts until the user answers.** A warm first frame over 1000 ms is a failed concrete check. A warm first frame slower than the 370 ms baseline but ≤ 1000 ms is not: it is recorded as `slowerThanBaseline` in `docs/research/10-stack-proof.md` and reported to the user in the completion summary, and it does not stop Part 02.

Cross-part names, namespaces and signatures follow `00-interfaces.md` (the registry wins over this file on names and contracts).

Depends on: none (first part).

**Execution notes for every task in this part**
- Commands are written for a Windows shell at `C:\dev\uas-sort`. From WSL, prefix each `dotnet …` with `tools/r.sh` and each `pwsh …` with `tools/r.sh pwsh` (Task 01.1 creates the wrapper), and run `tools/r.sh dotnet build-server shutdown` after a WSL-driven build.
- Prerequisites (Global Constraints) must already be installed. If `dotnet --version` is not `11.0.100-rc.1.26425.128`, `pwsh` is missing, or (Task 01.12) the AOT publish reports that `vswhere.exe` cannot find `Microsoft.VisualStudio.Component.VC.Tools.x86.x64`, stop and ask the user to install it.
- Never use the `dotnet new winui` or `dotnet new xunit3` templates (Ref §2.7 #1, #8): every file below is written by hand.

---

### Task 01.1: Repository skeleton, pins and props

**Files:**
- Create: `global.json`, `nuget.config`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `uas-sort.slnx`, `tools/r.sh`
- Create: `src/UasSort.Core/UasSort.Core.csproj`, `src/UasSort.Review/UasSort.Review.csproj`, `src/UasSort.Platform/UasSort.Platform.csproj`, `src/UasSort.Cli/UasSort.Cli.csproj`, `src/UasSort.Cli/Program.cs`

**Interfaces:**
- Consumes: Ref §2.1 versions, §2.3 layout, §2.5 build essentials, §2.6 WSL wrapper, §4.5 CLI usage line.
- Produces (files later parts rely on): the four `src` projects (target frameworks per Global Constraints; `IsTrimmable`/`IsAotCompatible` on Core, Review, Platform); `UasSort.Cli` with `AssemblyName=uas-sort-cli` and `public static int Main(string[] args)` in `UasSort.Cli.Program` (Part 12 Task 12.6 replaces the body; its `Main` first calls Part 01's `PlaceholderMode.ExposePlaceholders()` from Task 01.8); MSBuild property `RepoRoot` (defined here); `tools/r.sh dotnet|pwsh <args…>` (defined here); `nuget.config` with source mapping (defined here; not in the Ref layout, added so NU1507 can never fail a warnings-as-errors build).

This is a config task: the checks are build and property queries.

- [ ] **Step 1: Write the verification commands**

```powershell
dotnet --version
dotnet build uas-sort.slnx -tl:off
dotnet msbuild src/UasSort.Core/UasSort.Core.csproj -getItem:GlobalAnalyzerConfigFiles
dotnet msbuild src/UasSort.Core/UasSort.Core.csproj -getProperty:EffectiveAnalysisLevel
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build uas-sort.slnx -tl:off`
Expected: `MSBUILD : error MSB1009: Project file does not exist.` (nothing is scaffolded yet). `dotnet --version` must already print `11.0.100-rc.1.26425.128`; if it does not, stop (prerequisites).

- [ ] **Step 3: Implement**

`global.json` (the only one in the repo, Ref §2.5):

```json
{
  "sdk": { "version": "11.0.100-rc.1.26425.128", "rollForward": "latestFeature", "allowPrerelease": true },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

`nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <!-- One source, mapped, so central package management never raises NU1507 (an error under TreatWarningsAsErrors). -->
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

`Directory.Build.props`:

```xml
<Project>
  <!-- Ref §2.5. AnalysisLevel is pinned; fall back to 10.0-recommended only if RC1 has no 11.0 config (Task 01.1 Step 4). -->
  <PropertyGroup>
    <RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AnalysisLevel>11.0-recommended</AnalysisLevel>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <!-- Pins apply to transitive references too (WinUI and Foundation ask for InteractiveExperiences >= 2.1.8, never published: NU1603). -->
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
    <Deterministic>true</Deterministic>
  </PropertyGroup>

  <!-- Trim and AOT analyzers on every build, not only at publish (Ref §2.5). -->
  <PropertyGroup Condition="'$(MSBuildProjectName)' == 'UasSort.Core' or '$(MSBuildProjectName)' == 'UasSort.Review' or '$(MSBuildProjectName)' == 'UasSort.Platform'">
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>

  <!-- Test names use underscores (Ref §2.7 #8). -->
  <PropertyGroup Condition="$(MSBuildProjectName.EndsWith('.Tests'))">
    <NoWarn>$(NoWarn);CA1707</NoWarn>
  </PropertyGroup>
</Project>
```

`Directory.Packages.props` (exact pins, Ref §2.1/§2.5; nothing floats):

```xml
<Project>
  <!-- Exact pins queried from nuget.org on 2026-09-27 (Ref §2.1, §2.5). No floating versions, no VersionOverride. -->
  <ItemGroup>
    <!-- Windows App SDK 2.5.1 component packages. Never the full Microsoft.WindowsAppSDK (+58 MB onnxruntime/DirectML). -->
    <PackageVersion Include="Microsoft.WindowsAppSDK.WinUI" Version="2.3.9" />
    <PackageVersion Include="Microsoft.WindowsAppSDK.Foundation" Version="2.3.12" />
    <!-- WinUI 2.3.9 / Foundation 2.3.12 ask for InteractiveExperiences >= 2.1.8, which was never published (NU1603). -->
    <PackageVersion Include="Microsoft.WindowsAppSDK.InteractiveExperiences" Version="2.1.9" />
    <PackageVersion Include="Microsoft.Web.WebView2" Version="1.0.4191.47" />
    <PackageVersion Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.28000.2705" />
    <!-- The only toolkit line that works with the lean component packages (preview). -->
    <PackageVersion Include="CommunityToolkit.WinUI.Controls.Sizers" Version="8.3.260402-preview2" />
    <PackageVersion Include="CommunityToolkit.WinUI.Controls.SettingsControls" Version="8.3.260402-preview2" />
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageVersion Include="MetadataExtractor" Version="2.9.3" />
    <PackageVersion Include="GeoTimeZone" Version="6.1.0" />
    <PackageVersion Include="System.IO.Hashing" Version="11.0.0-rc.1.26425.128" />
    <PackageVersion Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="5.6.0" />
    <PackageVersion Include="xunit.v3.mtp-v2" Version="4.0.1" />
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />
  </ItemGroup>
</Project>
```

`.editorconfig`:

```ini
root = true

[*]
charset = utf-8
indent_style = space
insert_final_newline = true
trim_trailing_whitespace = true

[*.{cs,xaml}]
indent_size = 4

[*.{csproj,props,targets,slnx,xml,json,config,manifest,ps1,sh,html,js}]
indent_size = 2

[*.sh]
end_of_line = lf

[*.cs]
csharp_style_namespace_declarations = file_scoped:suggestion
csharp_using_directive_placement = outside_namespace:suggestion
dotnet_sort_system_directives_first = true
csharp_style_var_elsewhere = true:suggestion
```

`uas-sort.slnx` (src projects only for now; each later task adds the projects it creates):

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/UasSort.Cli/UasSort.Cli.csproj" />
    <Project Path="src/UasSort.Core/UasSort.Core.csproj" />
    <Project Path="src/UasSort.Platform/UasSort.Platform.csproj" />
    <Project Path="src/UasSort.Review/UasSort.Review.csproj" />
  </Folder>
</Solution>
```

`src/UasSort.Core/UasSort.Core.csproj` (no project references, ever — Ref §2.4):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <RootNamespace>UasSort.Core</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MetadataExtractor" />
    <PackageReference Include="GeoTimeZone" />
    <PackageReference Include="System.IO.Hashing" />
  </ItemGroup>
</Project>
```

`src/UasSort.Review/UasSort.Review.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <RootNamespace>UasSort.Review</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\UasSort.Core\UasSort.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/UasSort.Platform/UasSort.Platform.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0-windows10.0.26100.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.26100.0</TargetPlatformMinVersion>
    <RootNamespace>UasSort.Platform</RootNamespace>
    <!-- LibraryImport needs it, else SYSLIB1062 (Ref §2.5). -->
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\UasSort.Core\UasSort.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/UasSort.Cli/UasSort.Cli.csproj` (never references Review or App — Ref §2.4):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0-windows10.0.26100.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.26100.0</TargetPlatformMinVersion>
    <RootNamespace>UasSort.Cli</RootNamespace>
    <AssemblyName>uas-sort-cli</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\UasSort.Core\UasSort.Core.csproj" />
    <ProjectReference Include="..\UasSort.Platform\UasSort.Platform.csproj" />
  </ItemGroup>
</Project>
```

`src/UasSort.Cli/Program.cs` (Part 12 implements `plan`; until then every invocation is a usage error):

```csharp
namespace UasSort.Cli;

public static class Program
{
    private const string Usage =
        "usage: uas-sort-cli plan --card <path> [--video-root <path>] [--photo-root <path>] [--radius-mi <5..100>] "
        + "[--gap-days <0..7>] [--settings <path>] [--json] [--expect <expected.json>]";

    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
```

`tools/r.sh` (Ref §2.6 WSL wrapper):

```bash
#!/usr/bin/env bash
# WSL wrapper (Ref §2.6): runs the Windows dotnet.exe or pwsh.exe with the working directory C:\dev\uas-sort.
# usage: tools/r.sh dotnet <args...>   |   tools/r.sh pwsh <args...>
# Environment variables reach Windows only through WSLENV; add your own (e.g. UASSORT_GOLDEN) the same way.
set -uo pipefail
cd /mnt/c/dev/uas-sort || exit 1
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false DOTNET_CLI_TELEMETRY_OPTOUT=1
export WSLENV="${WSLENV:+$WSLENV:}MSBUILDDISABLENODEREUSE:DOTNET_CLI_USE_MSBUILD_SERVER:UseSharedCompilation:DOTNET_CLI_TELEMETRY_OPTOUT"
tool="${1:-}"
[ $# -gt 0 ] && shift
case "$tool" in
  dotnet) exe="/mnt/c/Program Files/dotnet/dotnet.exe" ;;
  pwsh)   exe="/mnt/c/Program Files/PowerShell/7/pwsh.exe" ;;
  *) echo "usage: tools/r.sh dotnet|pwsh <args...>" >&2; exit 2 ;;
esac
"$exe" "$@" 2>&1 | tr -d '\r'
exit "${PIPESTATUS[0]}"
```

Then: `chmod +x tools/r.sh`.

- [ ] **Step 4: Run the checks to verify they pass**

Run: `dotnet build uas-sort.slnx -tl:off` (first restore: allow 8–13 min; note the duration for Task 01.12's record)
Expected: `Build succeeded.` with `0 Warning(s)` and `0 Error(s)`; no `NU1603`, `NU1507` or `NU1608` anywhere in the output.

Run: `dotnet msbuild src/UasSort.Core/UasSort.Core.csproj -getItem:GlobalAnalyzerConfigFiles`
Expected: a path ending in `analysislevel_11_recommended.globalconfig`.

Run: `dotnet msbuild src/UasSort.Core/UasSort.Core.csproj -getProperty:EffectiveAnalysisLevel`
Expected: `11.0`.

If the item list has no `analysislevel_11_*` file, RC1 rejects `11.0` (the UNVERIFIED case): set `<AnalysisLevel>10.0-recommended</AnalysisLevel>` in `Directory.Build.props`, re-run both commands (expected `analysislevel_10_recommended.globalconfig` and `10.0`), change the expected value in `DirectoryBuildProps_EnforcesAnalysisAndWarnings` (Task 01.2) to `10.0-recommended`, and record the fallback for Task 01.12.

Run: `tools/r.sh dotnet --version` (from WSL)
Expected: `11.0.100-rc.1.26425.128`.

- [ ] **Step 5: Commit**

```bash
git add global.json nuget.config Directory.Build.props Directory.Packages.props .editorconfig uas-sort.slnx tools/r.sh src/UasSort.Core/UasSort.Core.csproj src/UasSort.Review/UasSort.Review.csproj src/UasSort.Platform/UasSort.Platform.csproj src/UasSort.Cli/UasSort.Cli.csproj src/UasSort.Cli/Program.cs
git update-index --chmod=+x tools/r.sh
git commit -F - <<'EOF'
build: scaffold uas-sort solution with exact pins and pinned analysis level

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 01.2: Build-configuration guard tests

**Files:**
- Create: `tests/UasSort.Testing/UasSort.Testing.csproj`, `tests/UasSort.Testing/RepoPaths.cs`, `tests/UasSort.Testing/TestTempDir.cs`
- Create: `tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj`
- Test: `tests/UasSort.Core.Tests/Build/BuildConfigTests.cs`
- Modify: `uas-sort.slnx`

**Interfaces:**
- Consumes: Task 01.1 files.
- Produces (defined here):
  - `public static class UasSort.Testing.RepoPaths` — `static string Root { get; }` (the folder holding `uas-sort.slnx`); `static string Of(string relativePath)`; `static IEnumerable<string> EnumerateFiles(string searchPattern, params string[] topFolders)` (full paths; skips `bin`, `obj`, `.git`, `artifacts`, `node_modules`, `TestResults`); `static string Relative(string fullPath)` (repo-relative, forward slashes).
  - `public sealed class UasSort.Testing.TestTempDir : IDisposable` — `TestTempDir()` creates `%TEMP%\uas-sort-test-<guid>`; `string FullPath { get; }`; `string Combine(params string[] parts)`; `void Dispose()` deletes it.
  - `UasSort.Testing` project (net11.0, not a test project; references Core and `Microsoft.Extensions.TimeProvider.Testing`, whose `FakeTimeProvider` every later test uses).
  - `UasSort.Core.Tests` project (net11.0, xUnit v3 on MTP, references Core and Testing and `Microsoft.Extensions.TimeProvider.Testing`; `<Using Include="Xunit" />` in the csproj, the only place `Xunit` is imported). Every test csproj of this part (Core.Tests, Platform.Tests, Review.Tests) has the same two package references and the same `<Using Include="Xunit" />`.

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Testing/UasSort.Testing.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <RootNamespace>UasSort.Testing</RootNamespace>
    <IsTestProject>false</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\UasSort.Core\UasSort.Core.csproj" />
  </ItemGroup>
</Project>
```

`tests/UasSort.Testing/RepoPaths.cs`:

```csharp
namespace UasSort.Testing;

/// <summary>Locates the repository (the folder holding uas-sort.slnx) from a test's output folder.</summary>
public static class RepoPaths
{
    private static readonly Lazy<string> RootLazy = new(FindRoot);
    private static readonly HashSet<string> SkippedFolders =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", "artifacts", "node_modules", "TestResults" };

    public static string Root => RootLazy.Value;

    public static string Of(string relativePath) => Path.GetFullPath(Path.Join(Root, relativePath));

    /// <summary>Files matching <paramref name="searchPattern"/> under the given top folders (repo-relative).</summary>
    public static IEnumerable<string> EnumerateFiles(string searchPattern, params string[] topFolders)
    {
        foreach (var top in topFolders)
        {
            var start = Of(top);
            if (!Directory.Exists(start))
            {
                continue;
            }

            var pending = new Stack<string>();
            pending.Push(start);
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                foreach (var file in Directory.EnumerateFiles(dir, searchPattern))
                {
                    yield return file;
                }

                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (!SkippedFolders.Contains(Path.GetFileName(sub)))
                    {
                        pending.Push(sub);
                    }
                }
            }
        }
    }

    public static string Relative(string fullPath) => Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Join(dir.FullName, "uas-sort.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("uas-sort.slnx not found above " + AppContext.BaseDirectory);
    }
}
```

`tests/UasSort.Testing/TestTempDir.cs`:

```csharp
namespace UasSort.Testing;

/// <summary>A fresh %TEMP%\uas-sort-test-&lt;guid&gt; folder, deleted on Dispose (Global Constraints: temp artefacts only there).</summary>
public sealed class TestTempDir : IDisposable
{
    public TestTempDir()
    {
        FullPath = Path.Join(Path.GetTempPath(), "uas-sort-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    public string Combine(params string[] parts) => Path.Join([FullPath, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(FullPath, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder must never fail a test; the next run uses a new guid.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
```

`tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <RootNamespace>UasSort.Core.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3.mtp-v2" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\UasSort.Core\UasSort.Core.csproj" />
    <ProjectReference Include="..\UasSort.Testing\UasSort.Testing.csproj" />
  </ItemGroup>
</Project>
```

`tests/UasSort.Core.Tests/Build/BuildConfigTests.cs`:

```csharp
using System.Text.Json;
using System.Xml.Linq;
using UasSort.Testing;

namespace UasSort.Core.Tests.Build;

/// <summary>Guards the build configuration of Ref §2.1, §2.4, §2.5 against drift.</summary>
public sealed class BuildConfigTests
{
    private static readonly string[] ExpectedPins =
    [
        "CommunityToolkit.Mvvm=8.4.2",
        "CommunityToolkit.WinUI.Controls.SettingsControls=8.3.260402-preview2",
        "CommunityToolkit.WinUI.Controls.Sizers=8.3.260402-preview2",
        "GeoTimeZone=6.1.0",
        "MetadataExtractor=2.9.3",
        "Microsoft.CodeAnalysis.BannedApiAnalyzers=5.6.0",
        "Microsoft.Extensions.TimeProvider.Testing=10.10.0",
        "Microsoft.Web.WebView2=1.0.4191.47",
        "Microsoft.Windows.SDK.BuildTools=10.0.28000.2705",
        "Microsoft.WindowsAppSDK.Foundation=2.3.12",
        "Microsoft.WindowsAppSDK.InteractiveExperiences=2.1.9",
        "Microsoft.WindowsAppSDK.WinUI=2.3.9",
        "System.IO.Hashing=11.0.0-rc.1.26425.128",
        "xunit.v3.mtp-v2=4.0.1",
    ];

    private static readonly string[] ForbiddenProperties = ["InvariantGlobalization", "UseNls"];

    [Fact]
    public void GlobalJson_PinsRc1SdkAndMicrosoftTestingPlatform()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoPaths.Of("global.json")));
        var sdk = doc.RootElement.GetProperty("sdk");
        Assert.Equal("11.0.100-rc.1.26425.128", sdk.GetProperty("version").GetString());
        Assert.Equal("latestFeature", sdk.GetProperty("rollForward").GetString());
        Assert.True(sdk.GetProperty("allowPrerelease").GetBoolean());
        Assert.Equal("Microsoft.Testing.Platform", doc.RootElement.GetProperty("test").GetProperty("runner").GetString());
    }

    [Fact]
    public void GlobalJson_IsTheOnlyOneInTheBuildTree()
    {
        var extra = RepoPaths.EnumerateFiles("global.json", "src", "tests", "tools").Select(RepoPaths.Relative).ToList();
        Assert.Empty(extra);
    }

    [Fact]
    public void DirectoryPackagesProps_PinsExactlyTheApprovedVersions()
    {
        var doc = XDocument.Load(RepoPaths.Of("Directory.Packages.props"));
        var pins = doc.Descendants("PackageVersion")
            .Select(e => $"{(string?)e.Attribute("Include")}={(string?)e.Attribute("Version")}")
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(ExpectedPins, pins);
    }

    [Fact]
    public void EveryProject_UsesCentralVersions_AndNeverTheFullWindowsAppSdk()
    {
        foreach (var csproj in RepoPaths.EnumerateFiles("*.csproj", "src", "tests"))
        {
            var doc = XDocument.Load(csproj);
            foreach (var reference in doc.Descendants("PackageReference"))
            {
                var name = (string?)reference.Attribute("Include");
                Assert.NotEqual("Microsoft.WindowsAppSDK", name);
                Assert.Null(reference.Attribute("Version"));
                Assert.Null(reference.Attribute("VersionOverride"));
            }
        }
    }

    [Fact]
    public void DirectoryBuildProps_EnforcesAnalysisAndWarnings()
    {
        var doc = XDocument.Load(RepoPaths.Of("Directory.Build.props"));
        var global = doc.Root!.Elements("PropertyGroup")
            .Where(g => g.Attribute("Condition") is null)
            .Elements()
            .Where(e => e.Attribute("Condition") is null)
            .GroupBy(e => e.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value.Trim(), StringComparer.Ordinal);
        Assert.Equal("enable", global["Nullable"]);
        Assert.Equal("enable", global["ImplicitUsings"]);
        Assert.Equal("11.0-recommended", global["AnalysisLevel"]);
        Assert.Equal("true", global["TreatWarningsAsErrors"]);
        Assert.Equal("true", global["ManagePackageVersionsCentrally"]);
    }

    [Fact]
    public void NoBuildFile_SetsInvariantGlobalizationOrUseNls()
    {
        var files = RepoPaths.EnumerateFiles("*.csproj", "src", "tests")
            .Append(RepoPaths.Of("Directory.Build.props"));
        foreach (var file in files)
        {
            var doc = XDocument.Load(file);
            foreach (var property in ForbiddenProperties)
            {
                Assert.Empty(doc.Descendants(property));
            }
        }
    }

    [Fact]
    public void Core_HasNoProjectReferences_AndCliNeverReferencesReviewOrApp()
    {
        var core = XDocument.Load(RepoPaths.Of("src/UasSort.Core/UasSort.Core.csproj"));
        Assert.Empty(core.Descendants("ProjectReference"));

        var cli = XDocument.Load(RepoPaths.Of("src/UasSort.Cli/UasSort.Cli.csproj"));
        var cliRefs = cli.Descendants("ProjectReference").Select(e => (string?)e.Attribute("Include") ?? "").ToList();
        Assert.DoesNotContain(cliRefs, r => r.Contains("UasSort.Review", StringComparison.Ordinal));
        Assert.DoesNotContain(cliRefs, r => r.Contains("UasSort.App", StringComparison.Ordinal));
    }

    [Fact]
    public void Solution_ListsEveryProjectExceptTheBannedApiProbe()
    {
        var slnx = XDocument.Load(RepoPaths.Of("uas-sort.slnx"));
        var listed = slnx.Descendants("Project").Select(p => (string)p.Attribute("Path")!).ToHashSet(StringComparer.Ordinal);
        var onDisk = RepoPaths.EnumerateFiles("*.csproj", "src", "tests").Select(RepoPaths.Relative).ToList();

        const string probe = "tests/UasSort.BannedApi.Probe/UasSort.BannedApi.Probe.csproj";
        Assert.DoesNotContain(probe, listed);
        var missing = onDisk.Where(p => p != probe && !listed.Contains(p)).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "missing from uas-sort.slnx: " + string.Join(", ", missing));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj`
Expected: FAIL — `Solution_ListsEveryProjectExceptTheBannedApiProbe` with `missing from uas-sort.slnx: tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj, tests/UasSort.Testing/UasSort.Testing.csproj`; the other 7 tests pass.

- [ ] **Step 3: Implement**

Add a `tests` folder to `uas-sort.slnx` so it reads:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/UasSort.Cli/UasSort.Cli.csproj" />
    <Project Path="src/UasSort.Core/UasSort.Core.csproj" />
    <Project Path="src/UasSort.Platform/UasSort.Platform.csproj" />
    <Project Path="src/UasSort.Review/UasSort.Review.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj" />
    <Project Path="tests/UasSort.Testing/UasSort.Testing.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --solution uas-sort.slnx`
Expected: PASS — summary `failed: 0, succeeded: 8`.

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Testing tests/UasSort.Core.Tests uas-sort.slnx
git commit -F - <<'EOF'
test: guard global.json, exact pins, props and solution membership

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 01.3: BannedSymbols.txt, the Platform member list, and the content test

**Files:**
- Create: `BannedSymbols.txt`, `src/UasSort.Platform/BannedSymbols.Platform.txt`
- Create: `tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj`
- Test: `tests/UasSort.Platform.Tests/Build/BannedSymbolsTests.cs`
- Modify: `Directory.Build.props`, `uas-sort.slnx`

**Interfaces:**
- Consumes: Ref §2.4 banned table; `RepoPaths` (Task 01.2).
- Produces: `BannedSymbols.txt` with exactly the Ref §2.4 entries (73 documentation IDs, below) applied to `UasSort.Core`, `UasSort.Review`, `UasSort.App`, `UasSort.Cli` and `UasSort.BannedApi.Probe`; `src/UasSort.Platform/BannedSymbols.Platform.txt` (defined here: the Ref's "member-level list" for Platform — writes, deletes, moves, `FileMode.Create/Truncate/OpenOrCreate/Append`, attribute/time setters, the clock); MSBuild property `UasSortBannedList` = `main` | `platform` (defined here); the `UasSort.Platform.Tests` project (net11.0-windows10.0.26100.0). Every allowed risky call in Platform carries `#pragma warning disable RS0030 // IO layer: <why>`.

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0-windows10.0.26100.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.26100.0</TargetPlatformMinVersion>
    <OutputType>Exe</OutputType>
    <RootNamespace>UasSort.Platform.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3.mtp-v2" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\UasSort.Platform\UasSort.Platform.csproj" />
    <ProjectReference Include="..\UasSort.Testing\UasSort.Testing.csproj" />
  </ItemGroup>
</Project>
```

`tests/UasSort.Platform.Tests/Build/BannedSymbolsTests.cs`:

```csharp
using UasSort.Testing;

namespace UasSort.Platform.Tests.Build;

/// <summary>Ref §2.4 "guards on the guards": BannedSymbols.txt holds exactly the required entries.</summary>
public sealed class BannedSymbolsTests
{
    private static readonly string[] Required =
    [
        // File-system types
        "T:System.IO.File",
        "T:System.IO.Directory",
        "T:System.IO.FileInfo",
        "T:System.IO.DirectoryInfo",
        "T:System.IO.FileSystemInfo",
        "T:System.IO.RandomAccess",
        "T:System.IO.FileStream",
        "T:System.IO.DriveInfo",
        "T:System.IO.FileSystemWatcher",
        "T:System.IO.Enumeration.FileSystemEnumerable`1",
        "T:System.IO.Enumeration.FileSystemEnumerator`1",
        "T:System.IO.Compression.ZipFile",
        // Path-taking members
        "M:System.IO.Path.GetTempFileName",
        "M:System.IO.StreamReader.#ctor(System.String)",
        "M:System.IO.StreamReader.#ctor(System.String,System.Boolean)",
        "M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding)",
        "M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean)",
        "M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean,System.Int32)",
        "M:System.IO.StreamReader.#ctor(System.String,System.IO.FileStreamOptions)",
        "M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean,System.IO.FileStreamOptions)",
        "M:System.IO.StreamWriter.#ctor(System.String)",
        "M:System.IO.StreamWriter.#ctor(System.String,System.Boolean)",
        "M:System.IO.StreamWriter.#ctor(System.String,System.Boolean,System.Text.Encoding)",
        "M:System.IO.StreamWriter.#ctor(System.String,System.Boolean,System.Text.Encoding,System.Int32)",
        "M:System.IO.StreamWriter.#ctor(System.String,System.IO.FileStreamOptions)",
        "M:System.IO.StreamWriter.#ctor(System.String,System.Text.Encoding,System.IO.FileStreamOptions)",
        "M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String)",
        "M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode)",
        "M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String)",
        "M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String,System.Int64)",
        "M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess)",
        "M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.IO.FileStream,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess,System.IO.HandleInheritability,System.Boolean)",
        "M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(Microsoft.Win32.SafeHandles.SafeFileHandle,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess,System.IO.HandleInheritability,System.Boolean)",
        "M:MetadataExtractor.ImageMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Avi.AviMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Bmp.BmpMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Eps.EpsMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Gif.GifMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Ico.IcoMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Jpeg.JpegMetadataReader.ReadMetadata(System.String,System.Collections.Generic.ICollection{MetadataExtractor.Formats.Jpeg.IJpegSegmentMetadataReader})",
        "M:MetadataExtractor.Formats.Netpbm.NetpbmMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Pcx.PcxMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Photoshop.PsdMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Png.PngMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Tga.TgaMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Tiff.TiffMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Wav.WavMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.WebP.WebPMetadataReader.ReadMetadata(System.String)",
        "M:System.Xml.Linq.XDocument.Load(System.String)",
        "M:System.Xml.Linq.XDocument.Load(System.String,System.Xml.Linq.LoadOptions)",
        "M:System.Xml.Linq.XDocument.Save(System.String)",
        "M:System.Xml.Linq.XDocument.Save(System.String,System.Xml.Linq.SaveOptions)",
        "M:System.Xml.XmlDocument.Load(System.String)",
        "M:System.Xml.XmlDocument.Save(System.String)",
        // WinRT storage
        "T:Windows.Storage.StorageFile",
        "T:Windows.Storage.StorageFolder",
        "T:Windows.Storage.FileIO",
        "T:Windows.Storage.PathIO",
        "T:Microsoft.VisualBasic.FileIO.FileSystem",
        // Images from paths
        "M:Microsoft.UI.Xaml.Media.Imaging.BitmapImage.#ctor(System.Uri)",
        "P:Microsoft.UI.Xaml.Media.Imaging.BitmapImage.UriSource",
        // Processes
        "M:System.Diagnostics.Process.Start",
        "M:System.Diagnostics.Process.Start(System.String)",
        "M:System.Diagnostics.Process.Start(System.String,System.String)",
        "M:System.Diagnostics.Process.Start(System.String,System.Collections.Generic.IEnumerable{System.String})",
        "M:System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo)",
        "M:System.Diagnostics.Process.Start(System.String,System.String,System.Security.SecureString,System.String)",
        "M:System.Diagnostics.Process.Start(System.String,System.String,System.String,System.Security.SecureString,System.String)",
        // Clock
        "P:System.DateTime.Now",
        "P:System.DateTime.Today",
        "P:System.DateTime.UtcNow",
        "P:System.DateTimeOffset.Now",
        "P:System.DateTimeOffset.UtcNow",
    ];

    private static readonly string[] PlatformMustBan =
    [
        "F:System.IO.FileMode.Create",
        "F:System.IO.FileMode.Truncate",
        "F:System.IO.FileMode.OpenOrCreate",
        "F:System.IO.FileMode.Append",
        "M:System.IO.File.Delete(System.String)",
        "M:System.IO.File.Move(System.String,System.String,System.Boolean)",
        "M:System.IO.File.Replace(System.String,System.String,System.String)",
        "M:System.IO.File.SetAttributes(System.String,System.IO.FileAttributes)",
        "M:System.IO.File.SetLastWriteTimeUtc(System.String,System.DateTime)",
        "M:System.IO.File.SetCreationTimeUtc(System.String,System.DateTime)",
        "M:System.IO.Directory.Delete(System.String,System.Boolean)",
        "M:System.IO.Directory.CreateDirectory(System.String)",
        "P:System.DateTime.Now",
        "P:System.DateTime.Today",
        "P:System.DateTime.UtcNow",
        "P:System.DateTimeOffset.Now",
        "P:System.DateTimeOffset.UtcNow",
    ];

    [Fact]
    public void BannedSymbolsTxt_HoldsExactlyTheRefEntries()
    {
        var entries = Entries(RepoPaths.Of("BannedSymbols.txt"));
        Assert.Equal(Required.Length, entries.Count);
        Assert.Equal(Required.Order(StringComparer.Ordinal), entries.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void BannedSymbolsTxt_EveryEntryHasAReason()
    {
        foreach (var line in DataLines(RepoPaths.Of("BannedSymbols.txt")))
        {
            var parts = line.Split(';', 2);
            Assert.True(parts.Length == 2 && parts[1].Trim().Length > 0, "no reason for " + line);
        }
    }

    [Fact]
    public void PlatformList_BansWritesDeletesMovesAndTheClock_ButNoWholeTypes()
    {
        var entries = Entries(RepoPaths.Of("src/UasSort.Platform/BannedSymbols.Platform.txt"));
        foreach (var required in PlatformMustBan)
        {
            Assert.Contains(required, entries);
        }

        Assert.DoesNotContain(entries, e => e.StartsWith("T:", StringComparison.Ordinal));
        Assert.DoesNotContain("F:System.IO.FileMode.CreateNew", entries);
    }

    private static List<string> Entries(string path) =>
        DataLines(path).Select(l => l.Split(';', 2)[0].Trim()).ToList();

    private static IEnumerable<string> DataLines(string path) =>
        File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal));
}
```

Add `<Project Path="tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj" />` to the `/tests/` folder of `uas-sort.slnx` (between `UasSort.Core.Tests` and `UasSort.Testing`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj`
Expected: FAIL — all 3 tests with `System.IO.FileNotFoundException: Could not find file '…\BannedSymbols.txt'` (and `…\BannedSymbols.Platform.txt`).

- [ ] **Step 3: Implement**

`BannedSymbols.txt` (repo root):

```text
// uas-sort banned APIs (Ref §2.4) for UasSort.Core, UasSort.Review, UasSort.App, UasSort.Cli and UasSort.BannedApi.Probe.
// Exactly these entries: tests/UasSort.Platform.Tests/Build/BannedSymbolsTests.cs asserts the set, and
// tools/build.ps1 -CheckBannedApi proves each one raises RS0030. Format: DocumentationCommentId;Reason

// File-system types
T:System.IO.File;Only ICardReader, IFileOps and Platform stores touch files (Ref §2.4)
T:System.IO.Directory;List through IDirectoryLister; create through IFileOps (Ref §2.4)
T:System.IO.FileInfo;List through IDirectoryLister (Ref §2.4)
T:System.IO.DirectoryInfo;List through IDirectoryLister (Ref §2.4)
T:System.IO.FileSystemInfo;List through IDirectoryLister (Ref §2.4)
T:System.IO.RandomAccess;Read through ICardReader, write through IFileOps (Ref §2.4)
T:System.IO.FileStream;Open through ICardReader or IFileOps (Ref §2.4)
T:System.IO.DriveInfo;Use IVolumeProvider (Ref §2.4)
T:System.IO.FileSystemWatcher;Not used; device changes come from the App's DeviceChangeWatcher (Ref §2.4)
T:System.IO.Enumeration.FileSystemEnumerable`1;List through IDirectoryLister (Ref §2.4)
T:System.IO.Enumeration.FileSystemEnumerator`1;List through IDirectoryLister (Ref §2.4)
T:System.IO.Compression.ZipFile;Opens paths; not used (Ref §2.4)

// Path-taking members
M:System.IO.Path.GetTempFileName;Creates a file; use IFileOps (Ref §2.4)
M:System.IO.StreamReader.#ctor(System.String);Opens a path; wrap a Stream from ICardReader or IFileOps (Ref §2.4)
M:System.IO.StreamReader.#ctor(System.String,System.Boolean);Opens a path; wrap a Stream from ICardReader or IFileOps (Ref §2.4)
M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding);Opens a path; wrap a Stream from ICardReader or IFileOps (Ref §2.4)
M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean);Opens a path; wrap a Stream from ICardReader or IFileOps (Ref §2.4)
M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean,System.Int32);Opens a path; wrap a Stream from ICardReader or IFileOps (Ref §2.4)
M:System.IO.StreamReader.#ctor(System.String,System.IO.FileStreamOptions);Opens a path; wrap a Stream from ICardReader or IFileOps (Ref §2.4)
M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean,System.IO.FileStreamOptions);Opens a path; wrap a Stream from ICardReader or IFileOps (Ref §2.4)
M:System.IO.StreamWriter.#ctor(System.String);Opens a path; write through IFileOps (Ref §2.4)
M:System.IO.StreamWriter.#ctor(System.String,System.Boolean);Opens a path; write through IFileOps (Ref §2.4)
M:System.IO.StreamWriter.#ctor(System.String,System.Boolean,System.Text.Encoding);Opens a path; write through IFileOps (Ref §2.4)
M:System.IO.StreamWriter.#ctor(System.String,System.Boolean,System.Text.Encoding,System.Int32);Opens a path; write through IFileOps (Ref §2.4)
M:System.IO.StreamWriter.#ctor(System.String,System.IO.FileStreamOptions);Opens a path; write through IFileOps (Ref §2.4)
M:System.IO.StreamWriter.#ctor(System.String,System.Text.Encoding,System.IO.FileStreamOptions);Opens a path; write through IFileOps (Ref §2.4)
M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String);Maps files outside the guard (Ref §2.4)
M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode);Maps files outside the guard (Ref §2.4)
M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String);Maps files outside the guard (Ref §2.4)
M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String,System.Int64);Maps files outside the guard (Ref §2.4)
M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess);Maps files outside the guard (Ref §2.4)
M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.IO.FileStream,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess,System.IO.HandleInheritability,System.Boolean);Maps files outside the guard (Ref §2.4)
M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(Microsoft.Win32.SafeHandles.SafeFileHandle,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess,System.IO.HandleInheritability,System.Boolean);Maps files outside the guard (Ref §2.4)
M:MetadataExtractor.ImageMetadataReader.ReadMetadata(System.String);Use the Stream overload (Ref §2.1)
M:MetadataExtractor.Formats.Avi.AviMetadataReader.ReadMetadata(System.String);Use the Stream overload (Ref §2.1)
M:MetadataExtractor.Formats.Bmp.BmpMetadataReader.ReadMetadata(System.String);Use the Stream overload (Ref §2.1)
M:MetadataExtractor.Formats.Eps.EpsMetadataReader.ReadMetadata(System.String);Use the Stream overload (Ref §2.1)
M:MetadataExtractor.Formats.Gif.GifMetadataReader.ReadMetadata(System.String);Use the Stream overload (Ref §2.1)
M:MetadataExtractor.Formats.Ico.IcoMetadataReader.ReadMetadata(System.String);Use the Stream overload (Ref §2.1)
M:MetadataExtractor.Formats.Jpeg.JpegMetadataReader.ReadMetadata(System.String,System.Collections.Generic.ICollection{MetadataExtractor.Formats.Jpeg.IJpegSegmentMetadataReader});Use the Stream overload (Ref §2.1)
M:MetadataExtractor.Formats.Netpbm.NetpbmMetadataReader.ReadMetadata(System.String);Use the Stream overload (Ref §2.1)
M:MetadataExtractor.Formats.Pcx.PcxMetadataReader.ReadMetadata(System.String);Use the Stream overload (Ref §2.1)
M:MetadataExtractor.Formats.Photoshop.PsdMetadataReader.ReadMetadata(System.String);Use the Stream overload (Ref §2.1)
M:MetadataExtractor.Formats.Png.PngMetadataReader.ReadMetadata(System.String);Use the Stream overload (Ref §2.1)
M:MetadataExtractor.Formats.Tga.TgaMetadataReader.ReadMetadata(System.String);Use the Stream overload (Ref §2.1)
M:MetadataExtractor.Formats.Tiff.TiffMetadataReader.ReadMetadata(System.String);Use the Stream overload (Ref §2.1)
M:MetadataExtractor.Formats.Wav.WavMetadataReader.ReadMetadata(System.String);Use the Stream overload (Ref §2.1)
M:MetadataExtractor.Formats.WebP.WebPMetadataReader.ReadMetadata(System.String);Use the Stream overload (Ref §2.1)
M:System.Xml.Linq.XDocument.Load(System.String);Opens a path; load from a Stream (Ref §2.4)
M:System.Xml.Linq.XDocument.Load(System.String,System.Xml.Linq.LoadOptions);Opens a path; load from a Stream (Ref §2.4)
M:System.Xml.Linq.XDocument.Save(System.String);Writes a path; write through IFileOps (Ref §2.4)
M:System.Xml.Linq.XDocument.Save(System.String,System.Xml.Linq.SaveOptions);Writes a path; write through IFileOps (Ref §2.4)
M:System.Xml.XmlDocument.Load(System.String);Opens a path; load from a Stream (Ref §2.4)
M:System.Xml.XmlDocument.Save(System.String);Writes a path; write through IFileOps (Ref §2.4)

// WinRT storage
T:Windows.Storage.StorageFile;WinRT storage bypasses the guard (Ref §2.4)
T:Windows.Storage.StorageFolder;WinRT storage bypasses the guard (Ref §2.4)
T:Windows.Storage.FileIO;WinRT storage bypasses the guard (Ref §2.4)
T:Windows.Storage.PathIO;WinRT storage bypasses the guard (Ref §2.4)
T:Microsoft.VisualBasic.FileIO.FileSystem;Bypasses the guard (Ref §2.4)

// Images from paths
M:Microsoft.UI.Xaml.Media.Imaging.BitmapImage.#ctor(System.Uri);Images only via ms-appx:/// or the Thumb.Key attached property (Ref §2.4)
P:Microsoft.UI.Xaml.Media.Imaging.BitmapImage.UriSource;Images only via ms-appx:/// or the Thumb.Key attached property (Ref §2.4)

// Processes
M:System.Diagnostics.Process.Start;Use IShellLauncher (Platform) (Ref §2.4)
M:System.Diagnostics.Process.Start(System.String);Use IShellLauncher (Platform) (Ref §2.4)
M:System.Diagnostics.Process.Start(System.String,System.String);Use IShellLauncher (Platform) (Ref §2.4)
M:System.Diagnostics.Process.Start(System.String,System.Collections.Generic.IEnumerable{System.String});Use IShellLauncher (Platform) (Ref §2.4)
M:System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo);Use IShellLauncher (Platform) (Ref §2.4)
M:System.Diagnostics.Process.Start(System.String,System.String,System.Security.SecureString,System.String);Use IShellLauncher (Platform) (Ref §2.4)
M:System.Diagnostics.Process.Start(System.String,System.String,System.String,System.Security.SecureString,System.String);Use IShellLauncher (Platform) (Ref §2.4)

// Clock
P:System.DateTime.Now;Inject TimeProvider (Ref §2.4)
P:System.DateTime.Today;Inject TimeProvider (Ref §2.4)
P:System.DateTime.UtcNow;Inject TimeProvider (Ref §2.4)
P:System.DateTimeOffset.Now;Inject TimeProvider (Ref §2.4)
P:System.DateTimeOffset.UtcNow;Inject TimeProvider (Ref §2.4)
```

`src/UasSort.Platform/BannedSymbols.Platform.txt` (IDs taken from the .NET 11 RC1 reference assemblies; `FileSystemInfo` attribute/time setters cannot be banned without also banning their getters, so they are not listed):

```text
// Platform's member-level list (Ref §2.4): writes, deletes, moves, FileMode.Create/Truncate/OpenOrCreate/Append,
// attribute and time setters, and the clock. Every allowed call carries
// #pragma warning disable RS0030 // IO layer: <why>
// FileMode
F:System.IO.FileMode.Append;Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
F:System.IO.FileMode.Create;Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
F:System.IO.FileMode.OpenOrCreate;Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
F:System.IO.FileMode.Truncate;Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
// File
M:System.IO.File.AppendAllBytes(System.String,System.Byte[]);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllBytes(System.String,System.ReadOnlySpan{System.Byte});Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllBytesAsync(System.String,System.Byte[],System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllBytesAsync(System.String,System.ReadOnlyMemory{System.Byte},System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllLines(System.String,System.Collections.Generic.IEnumerable{System.String});Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllLines(System.String,System.Collections.Generic.IEnumerable{System.String},System.Text.Encoding);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllLinesAsync(System.String,System.Collections.Generic.IEnumerable{System.String},System.Text.Encoding,System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllLinesAsync(System.String,System.Collections.Generic.IEnumerable{System.String},System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllText(System.String,System.ReadOnlySpan{System.Char});Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllText(System.String,System.ReadOnlySpan{System.Char},System.Text.Encoding);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllText(System.String,System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllText(System.String,System.String,System.Text.Encoding);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllTextAsync(System.String,System.ReadOnlyMemory{System.Char},System.Text.Encoding,System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllTextAsync(System.String,System.ReadOnlyMemory{System.Char},System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllTextAsync(System.String,System.String,System.Text.Encoding,System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendAllTextAsync(System.String,System.String,System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.AppendText(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.Copy(System.String,System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.Copy(System.String,System.String,System.Boolean);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.Create(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.Create(System.String,System.Int32);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.Create(System.String,System.Int32,System.IO.FileOptions);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.CreateSymbolicLink(System.String,System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.CreateText(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.Decrypt(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.Delete(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.Encrypt(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.Move(System.String,System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.Move(System.String,System.String,System.Boolean);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.OpenWrite(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.Replace(System.String,System.String,System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.Replace(System.String,System.String,System.String,System.Boolean);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetAttributes(Microsoft.Win32.SafeHandles.SafeFileHandle,System.IO.FileAttributes);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetAttributes(System.String,System.IO.FileAttributes);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetCreationTime(Microsoft.Win32.SafeHandles.SafeFileHandle,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetCreationTime(System.String,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetCreationTimeUtc(Microsoft.Win32.SafeHandles.SafeFileHandle,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetCreationTimeUtc(System.String,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetLastAccessTime(Microsoft.Win32.SafeHandles.SafeFileHandle,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetLastAccessTime(System.String,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetLastAccessTimeUtc(Microsoft.Win32.SafeHandles.SafeFileHandle,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetLastAccessTimeUtc(System.String,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetLastWriteTime(Microsoft.Win32.SafeHandles.SafeFileHandle,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetLastWriteTime(System.String,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetLastWriteTimeUtc(Microsoft.Win32.SafeHandles.SafeFileHandle,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetLastWriteTimeUtc(System.String,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetUnixFileMode(Microsoft.Win32.SafeHandles.SafeFileHandle,System.IO.UnixFileMode);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.SetUnixFileMode(System.String,System.IO.UnixFileMode);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllBytes(System.String,System.Byte[]);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllBytes(System.String,System.ReadOnlySpan{System.Byte});Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllBytesAsync(System.String,System.Byte[],System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllBytesAsync(System.String,System.ReadOnlyMemory{System.Byte},System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllLines(System.String,System.Collections.Generic.IEnumerable{System.String});Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllLines(System.String,System.Collections.Generic.IEnumerable{System.String},System.Text.Encoding);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllLines(System.String,System.String[]);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllLines(System.String,System.String[],System.Text.Encoding);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllLinesAsync(System.String,System.Collections.Generic.IEnumerable{System.String},System.Text.Encoding,System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllLinesAsync(System.String,System.Collections.Generic.IEnumerable{System.String},System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllText(System.String,System.ReadOnlySpan{System.Char});Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllText(System.String,System.ReadOnlySpan{System.Char},System.Text.Encoding);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllText(System.String,System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllText(System.String,System.String,System.Text.Encoding);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllTextAsync(System.String,System.ReadOnlyMemory{System.Char},System.Text.Encoding,System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllTextAsync(System.String,System.ReadOnlyMemory{System.Char},System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllTextAsync(System.String,System.String,System.Text.Encoding,System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.File.WriteAllTextAsync(System.String,System.String,System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
// Directory
M:System.IO.Directory.CreateDirectory(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.Directory.CreateDirectory(System.String,System.IO.UnixFileMode);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.Directory.CreateSymbolicLink(System.String,System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.Directory.CreateTempSubdirectory(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.Directory.Delete(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.Directory.Delete(System.String,System.Boolean);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.Directory.Move(System.String,System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.Directory.SetCreationTime(System.String,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.Directory.SetCreationTimeUtc(System.String,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.Directory.SetLastAccessTime(System.String,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.Directory.SetLastAccessTimeUtc(System.String,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.Directory.SetLastWriteTime(System.String,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.Directory.SetLastWriteTimeUtc(System.String,System.DateTime);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
// FileInfo
M:System.IO.FileInfo.AppendText;Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.FileInfo.CopyTo(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.FileInfo.CopyTo(System.String,System.Boolean);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.FileInfo.Create;Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.FileInfo.CreateText;Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.FileInfo.Decrypt;Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.FileInfo.Delete;Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.FileInfo.Encrypt;Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.FileInfo.MoveTo(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.FileInfo.MoveTo(System.String,System.Boolean);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.FileInfo.OpenWrite;Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.FileInfo.Replace(System.String,System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.FileInfo.Replace(System.String,System.String,System.Boolean);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
// DirectoryInfo
M:System.IO.DirectoryInfo.Create;Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.DirectoryInfo.CreateSubdirectory(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.DirectoryInfo.Delete;Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.DirectoryInfo.Delete(System.Boolean);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.DirectoryInfo.MoveTo(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
// FileSystemInfo
M:System.IO.FileSystemInfo.CreateAsSymbolicLink(System.String);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.FileSystemInfo.Delete;Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
// RandomAccess
M:System.IO.RandomAccess.FlushToDisk(Microsoft.Win32.SafeHandles.SafeFileHandle);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.RandomAccess.SetLength(Microsoft.Win32.SafeHandles.SafeFileHandle,System.Int64);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.RandomAccess.Write(Microsoft.Win32.SafeHandles.SafeFileHandle,System.Collections.Generic.IReadOnlyList{System.ReadOnlyMemory{System.Byte}},System.Int64);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.RandomAccess.Write(Microsoft.Win32.SafeHandles.SafeFileHandle,System.ReadOnlySpan{System.Byte},System.Int64);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.RandomAccess.WriteAsync(Microsoft.Win32.SafeHandles.SafeFileHandle,System.Collections.Generic.IReadOnlyList{System.ReadOnlyMemory{System.Byte}},System.Int64,System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
M:System.IO.RandomAccess.WriteAsync(Microsoft.Win32.SafeHandles.SafeFileHandle,System.ReadOnlyMemory{System.Byte},System.Int64,System.Threading.CancellationToken);Deliberate IO only: mark the call with the IO-layer pragma (Ref §2.4)
// Clock
P:System.DateTime.Now;Inject TimeProvider (Ref §2.4)
P:System.DateTime.Today;Inject TimeProvider (Ref §2.4)
P:System.DateTime.UtcNow;Inject TimeProvider (Ref §2.4)
P:System.DateTimeOffset.Now;Inject TimeProvider (Ref §2.4)
P:System.DateTimeOffset.UtcNow;Inject TimeProvider (Ref §2.4)
```

Append to `Directory.Build.props`, before `</Project>`:

```xml
  <!-- Ref §2.4 banned APIs: the whole list for Core, Review, App, Cli (and the probe that proves it);
       a member-level list for Platform. Nothing else is analysed for bans. -->
  <PropertyGroup>
    <UasSortBannedList Condition="'$(MSBuildProjectName)' == 'UasSort.Core' or '$(MSBuildProjectName)' == 'UasSort.Review' or '$(MSBuildProjectName)' == 'UasSort.App' or '$(MSBuildProjectName)' == 'UasSort.Cli' or '$(MSBuildProjectName)' == 'UasSort.BannedApi.Probe'">main</UasSortBannedList>
    <UasSortBannedList Condition="'$(MSBuildProjectName)' == 'UasSort.Platform'">platform</UasSortBannedList>
  </PropertyGroup>
  <ItemGroup Condition="'$(UasSortBannedList)' != ''">
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup Condition="'$(UasSortBannedList)' == 'main'">
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
  </ItemGroup>
  <ItemGroup Condition="'$(UasSortBannedList)' == 'platform'">
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)src\UasSort.Platform\BannedSymbols.Platform.txt" />
  </ItemGroup>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*BannedSymbolsTests"`
Expected: PASS — `failed: 0, succeeded: 3`.

Run: `dotnet msbuild src/UasSort.Core/UasSort.Core.csproj -getItem:AdditionalFiles` (repeat for `src/UasSort.Review/UasSort.Review.csproj` and `src/UasSort.Cli/UasSort.Cli.csproj`)
Expected: exactly one item, `…\BannedSymbols.txt`.

Run: `dotnet msbuild src/UasSort.Platform/UasSort.Platform.csproj -getItem:AdditionalFiles`
Expected: exactly one item, `…\src\UasSort.Platform\BannedSymbols.Platform.txt`.

Run: `dotnet test --solution uas-sort.slnx`
Expected: PASS — `failed: 0, succeeded: 11`; the build shows `0 Warning(s)` (no RS0030, no RS0031 duplicate-entry warning).

- [ ] **Step 5: Commit**

```bash
git add BannedSymbols.txt src/UasSort.Platform/BannedSymbols.Platform.txt Directory.Build.props tests/UasSort.Platform.Tests uas-sort.slnx
git commit -F - <<'EOF'
build: ban direct file, WinRT storage, process and clock APIs outside Platform

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 01.4: Banned-API probe and `tools/build.ps1 -CheckBannedApi`

**Files:**
- Create: `tests/UasSort.BannedApi.Probe/UasSort.BannedApi.Probe.csproj`, `tests/UasSort.BannedApi.Probe/Probe.cs`, `tools/build.ps1`

**Interfaces:**
- Consumes: `BannedSymbols.txt` and `UasSortBannedList` (Task 01.3).
- Produces: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 [-CheckBannedApi]` (no switch: builds `uas-sort.slnx`; with the switch: the probe check, exit 0 on success, 1 on any mismatch). Probe-line convention (defined here): every probe call ends with `// probe: <documentation id>`; the script requires a 1:1 match between those markers and `BannedSymbols.txt`, exactly one RS0030 on each marked line and none elsewhere. The probe is NOT in `uas-sort.slnx`.

- [ ] **Step 1: Write the failing test**

`tools/build.ps1`:

```powershell
#requires -Version 7.0
<#
.SYNOPSIS
  Builds uas-sort (no switch), or with -CheckBannedApi builds tests/UasSort.BannedApi.Probe (outside the solution)
  and checks Ref §2.4: every BannedSymbols.txt entry has exactly one probe call, and every probe call raises
  exactly one RS0030 (none elsewhere).
#>
[CmdletBinding()]
param([switch]$CheckBannedApi)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    if (-not $CheckBannedApi) {
        dotnet build uas-sort.slnx
        exit $LASTEXITCODE
    }

    $banned = @(Get-Content (Join-Path $repo 'BannedSymbols.txt') |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('//') } |
        ForEach-Object { ($_ -split ';', 2)[0].Trim() })

    $probeFile = Join-Path $repo 'tests/UasSort.BannedApi.Probe/Probe.cs'
    $probeLines = @(Get-Content $probeFile)
    $expected = @{}
    for ($i = 0; $i -lt $probeLines.Count; $i++) {
        if ($probeLines[$i] -match '//\s*probe:\s*(\S+)\s*$') { $expected[$i + 1] = $Matches[1] }
    }

    $failures = [System.Collections.Generic.List[string]]::new()
    $probedIds = @($expected.Values)
    foreach ($id in $banned) { if ($probedIds -cnotcontains $id) { $failures.Add("no probe call for $id") } }
    foreach ($id in $probedIds) { if ($banned -cnotcontains $id) { $failures.Add("probe marker $id is not in BannedSymbols.txt") } }
    $probedIds | Group-Object -CaseSensitive | Where-Object Count -gt 1 |
        ForEach-Object { $failures.Add("probe marker $($_.Name) appears $($_.Count) times") }

    $output = & dotnet build tests/UasSort.BannedApi.Probe/UasSort.BannedApi.Probe.csproj --no-incremental -tl:off `
        -clp:NoSummary -p:TreatWarningsAsErrors=false 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { Write-Host $output; throw "probe build failed (exit $LASTEXITCODE)" }

    $hits = @([regex]::Matches($output, 'Probe\.cs\((\d+),(\d+)\): warning RS0030: [^\r\n]*') |
        ForEach-Object Value | Sort-Object -Unique)
    $byLine = @{}
    foreach ($h in $hits) {
        $line = [int]([regex]::Match($h, 'Probe\.cs\((\d+),').Groups[1].Value)
        $byLine[$line] = 1 + ($byLine[$line] ?? 0)
    }
    foreach ($line in $expected.Keys) {
        $n = $byLine[$line] ?? 0
        if ($n -ne 1) { $failures.Add("line ${line} ($($expected[$line])): expected 1 RS0030, got $n") }
    }
    foreach ($line in $byLine.Keys) {
        if (-not $expected.ContainsKey($line)) { $failures.Add("line ${line}: RS0030 on a line without a probe marker") }
    }

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "FAIL $_" }
        exit 1
    }
    Write-Host "OK: $($banned.Count) banned entries, $($expected.Count) probe calls, $($hits.Count) RS0030 (one per call)"
    exit 0
}
finally {
    Pop-Location
}
```

`tests/UasSort.BannedApi.Probe/UasSort.BannedApi.Probe.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- NOT in uas-sort.slnx: every probe line is a banned call. Built only by tools/build.ps1 -CheckBannedApi. -->
  <PropertyGroup>
    <TargetFramework>net11.0-windows10.0.26100.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.26100.0</TargetPlatformMinVersion>
    <OutputType>Library</OutputType>
    <RootNamespace>UasSort.BannedApi.Probe</RootNamespace>
    <UseWinUI>true</UseWinUI>
    <WinUISDKReferences>false</WinUISDKReferences>
    <Platforms>x64;ARM64</Platforms>
    <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == ''">$(NETCoreSdkPortableRuntimeIdentifier)</RuntimeIdentifier>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK.WinUI" />
    <PackageReference Include="Microsoft.WindowsAppSDK.Foundation" />
    <PackageReference Include="Microsoft.WindowsAppSDK.InteractiveExperiences" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" />
    <PackageReference Include="MetadataExtractor" />
  </ItemGroup>
</Project>
```

`tests/UasSort.BannedApi.Probe/Probe.cs` (empty for now, so the check must fail):

```csharp
namespace UasSort.BannedApi.Probe;

internal static class Probe
{
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -CheckBannedApi`
Expected: exit code 1 with 73 lines `FAIL no probe call for …`, starting with `FAIL no probe call for T:System.IO.File`.

- [ ] **Step 3: Implement**

Replace `tests/UasSort.BannedApi.Probe/Probe.cs`:

```csharp
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Enumeration;
using System.IO.MemoryMappedFiles;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace UasSort.BannedApi.Probe;

// One call per BannedSymbols.txt entry (Ref §2.4). Each probe line ends with "// probe: <documentation id>";
// tools/build.ps1 -CheckBannedApi requires exactly one RS0030 on each such line and none anywhere else.
// Never executed: nothing references this project and it is not in uas-sort.slnx.
internal static class Probe
{
    internal static void FileSystemTypes()
    {
        _ = File.Exists("x"); // probe: T:System.IO.File
        _ = Directory.Exists("x"); // probe: T:System.IO.Directory
        _ = new FileInfo("x"); // probe: T:System.IO.FileInfo
        _ = new DirectoryInfo("x"); // probe: T:System.IO.DirectoryInfo
        _ = typeof(FileSystemInfo); // probe: T:System.IO.FileSystemInfo
        _ = RandomAccess.GetLength(null!); // probe: T:System.IO.RandomAccess
        _ = new FileStream("x", FileMode.Open); // probe: T:System.IO.FileStream
        _ = new DriveInfo("C"); // probe: T:System.IO.DriveInfo
        _ = new FileSystemWatcher(); // probe: T:System.IO.FileSystemWatcher
        _ = typeof(FileSystemEnumerable<string>); // probe: T:System.IO.Enumeration.FileSystemEnumerable`1
        _ = typeof(FileSystemEnumerator<string>); // probe: T:System.IO.Enumeration.FileSystemEnumerator`1
        _ = ZipFile.OpenRead("x.zip"); // probe: T:System.IO.Compression.ZipFile
    }

    internal static void PathTakingMembers()
    {
        _ = Path.GetTempFileName(); // probe: M:System.IO.Path.GetTempFileName
        _ = new StreamReader("x"); // probe: M:System.IO.StreamReader.#ctor(System.String)
        _ = new StreamReader("x", true); // probe: M:System.IO.StreamReader.#ctor(System.String,System.Boolean)
        _ = new StreamReader("x", Encoding.UTF8); // probe: M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding)
        _ = new StreamReader("x", Encoding.UTF8, true); // probe: M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean)
        _ = new StreamReader("x", Encoding.UTF8, true, 4096); // probe: M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean,System.Int32)
        _ = new StreamReader("x", new FileStreamOptions()); // probe: M:System.IO.StreamReader.#ctor(System.String,System.IO.FileStreamOptions)
        _ = new StreamReader("x", Encoding.UTF8, true, new FileStreamOptions()); // probe: M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean,System.IO.FileStreamOptions)
        _ = new StreamWriter("x"); // probe: M:System.IO.StreamWriter.#ctor(System.String)
        _ = new StreamWriter("x", true); // probe: M:System.IO.StreamWriter.#ctor(System.String,System.Boolean)
        _ = new StreamWriter("x", true, Encoding.UTF8); // probe: M:System.IO.StreamWriter.#ctor(System.String,System.Boolean,System.Text.Encoding)
        _ = new StreamWriter("x", true, Encoding.UTF8, 4096); // probe: M:System.IO.StreamWriter.#ctor(System.String,System.Boolean,System.Text.Encoding,System.Int32)
        _ = new StreamWriter("x", new FileStreamOptions()); // probe: M:System.IO.StreamWriter.#ctor(System.String,System.IO.FileStreamOptions)
        _ = new StreamWriter("x", Encoding.UTF8, new FileStreamOptions()); // probe: M:System.IO.StreamWriter.#ctor(System.String,System.Text.Encoding,System.IO.FileStreamOptions)
        _ = MemoryMappedFile.CreateFromFile("x"); // probe: M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String)
        _ = MemoryMappedFile.CreateFromFile("x", FileMode.Open); // probe: M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode)
        _ = MemoryMappedFile.CreateFromFile("x", FileMode.Open, "m"); // probe: M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String)
        _ = MemoryMappedFile.CreateFromFile("x", FileMode.Open, "m", 0); // probe: M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String,System.Int64)
        _ = MemoryMappedFile.CreateFromFile("x", FileMode.Open, "m", 0, MemoryMappedFileAccess.Read); // probe: M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess)
        _ = MemoryMappedFile.CreateFromFile(fileStream: null!, mapName: null, capacity: 0, access: MemoryMappedFileAccess.Read, inheritability: HandleInheritability.None, leaveOpen: false); // probe: M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.IO.FileStream,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess,System.IO.HandleInheritability,System.Boolean)
        _ = MemoryMappedFile.CreateFromFile(fileHandle: null!, mapName: null, capacity: 0, access: MemoryMappedFileAccess.Read, inheritability: HandleInheritability.None, leaveOpen: false); // probe: M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(Microsoft.Win32.SafeHandles.SafeFileHandle,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess,System.IO.HandleInheritability,System.Boolean)
        _ = MetadataExtractor.ImageMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.ImageMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Avi.AviMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Avi.AviMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Bmp.BmpMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Bmp.BmpMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Eps.EpsMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Eps.EpsMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Gif.GifMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Gif.GifMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Ico.IcoMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Ico.IcoMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Jpeg.JpegMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Jpeg.JpegMetadataReader.ReadMetadata(System.String,System.Collections.Generic.ICollection{MetadataExtractor.Formats.Jpeg.IJpegSegmentMetadataReader})
        _ = MetadataExtractor.Formats.Netpbm.NetpbmMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Netpbm.NetpbmMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Pcx.PcxMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Pcx.PcxMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Photoshop.PsdMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Photoshop.PsdMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Png.PngMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Png.PngMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Tga.TgaMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Tga.TgaMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Tiff.TiffMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Tiff.TiffMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Wav.WavMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Wav.WavMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.WebP.WebPMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.WebP.WebPMetadataReader.ReadMetadata(System.String)
        _ = XDocument.Load("x"); // probe: M:System.Xml.Linq.XDocument.Load(System.String)
        _ = XDocument.Load("x", LoadOptions.None); // probe: M:System.Xml.Linq.XDocument.Load(System.String,System.Xml.Linq.LoadOptions)
        new XDocument().Save("x"); // probe: M:System.Xml.Linq.XDocument.Save(System.String)
        new XDocument().Save("x", SaveOptions.None); // probe: M:System.Xml.Linq.XDocument.Save(System.String,System.Xml.Linq.SaveOptions)
        new XmlDocument().Load("x"); // probe: M:System.Xml.XmlDocument.Load(System.String)
        new XmlDocument().Save("x"); // probe: M:System.Xml.XmlDocument.Save(System.String)
    }

    internal static void WinRtStorageAndImages()
    {
        _ = Windows.Storage.StorageFile.GetFileFromPathAsync("x"); // probe: T:Windows.Storage.StorageFile
        _ = Windows.Storage.StorageFolder.GetFolderFromPathAsync("x"); // probe: T:Windows.Storage.StorageFolder
        _ = Windows.Storage.FileIO.ReadTextAsync(null!); // probe: T:Windows.Storage.FileIO
        _ = Windows.Storage.PathIO.ReadTextAsync("x"); // probe: T:Windows.Storage.PathIO
        _ = Microsoft.VisualBasic.FileIO.FileSystem.FileExists("x"); // probe: T:Microsoft.VisualBasic.FileIO.FileSystem
        _ = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///x.png")); // probe: M:Microsoft.UI.Xaml.Media.Imaging.BitmapImage.#ctor(System.Uri)
        _ = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage().UriSource; // probe: P:Microsoft.UI.Xaml.Media.Imaging.BitmapImage.UriSource
    }

    internal static void ProcessesAndClock()
    {
        _ = new Process().Start(); // probe: M:System.Diagnostics.Process.Start
        _ = Process.Start("x"); // probe: M:System.Diagnostics.Process.Start(System.String)
        _ = Process.Start("x", "y"); // probe: M:System.Diagnostics.Process.Start(System.String,System.String)
        _ = Process.Start("x", new List<string> { "y" }); // probe: M:System.Diagnostics.Process.Start(System.String,System.Collections.Generic.IEnumerable{System.String})
        _ = Process.Start(new ProcessStartInfo("x")); // probe: M:System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo)
        _ = Process.Start("x", "u", new SecureString(), "d"); // probe: M:System.Diagnostics.Process.Start(System.String,System.String,System.Security.SecureString,System.String)
        _ = Process.Start("x", "a", "u", new SecureString(), "d"); // probe: M:System.Diagnostics.Process.Start(System.String,System.String,System.String,System.Security.SecureString,System.String)
        _ = DateTime.Now; // probe: P:System.DateTime.Now
        _ = DateTime.Today; // probe: P:System.DateTime.Today
        _ = DateTime.UtcNow; // probe: P:System.DateTime.UtcNow
        _ = DateTimeOffset.Now; // probe: P:System.DateTimeOffset.Now
        _ = DateTimeOffset.UtcNow; // probe: P:System.DateTimeOffset.UtcNow
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -CheckBannedApi`
Expected: exit 0 and `OK: 73 banned entries, 73 probe calls, 73 RS0030 (one per call)`.

If a line reports `got 0`, that documentation ID does not resolve (fix the ID in `BannedSymbols.txt` **and** in `BannedSymbolsTests.Required` together). If a line reports `got 2`, rewrite that probe call so it names no second banned symbol; never loosen the script.

Run: `dotnet test --solution uas-sort.slnx`
Expected: PASS — `failed: 0, succeeded: 11` (the probe is not built by the solution).

- [ ] **Step 5: Commit**

```bash
git add tools/build.ps1 tests/UasSort.BannedApi.Probe
git commit -F - <<'EOF'
test: prove every banned symbol raises exactly one RS0030

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 01.5: Closed-record and union canaries in Core, with source-generated JSON

**Files:**
- Create: `src/UasSort.Core/StackProof/StackProofTypes.cs`, `src/UasSort.Core/Json/StackProofJsonContext.cs`
- Test: `tests/UasSort.Core.Tests/StackProof/StackProofJsonTests.cs`

**Interfaces:**
- Consumes: Ref §3 "Rules for the model → JSON" (closed records with `[JsonPolymorphic]` in a source-generated context; unions in memory only); Ref §2.7 #7 (`AllowOutOfOrderMetadataProperties = true`).
- Produces (defined here; the permanent stack-proof canaries — the real model types arrive in Part 02):
  - `[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")] public closed record class UasSort.Core.StackProof.ProbeOutcome;` with `public sealed record class ProbeCopied(string Path, long Bytes) : ProbeOutcome` (`"copied"`), `ProbeSkipped(string Reason)` (`"skipped"`), `ProbeConflict(string Existing, long ExistingSize)` (`"conflict"`).
  - `public sealed record ProbeFix(double Lat, double Lon);`, `public sealed record ProbeNoFix(string Reason);`, `public union ProbeGps(ProbeFix, ProbeNoFix);`
  - `public sealed partial class UasSort.Core.Json.StackProofJsonContext : JsonSerializerContext` (camelCase, `AllowOutOfOrderMetadataProperties = true`) exposing `Default.ProbeOutcome` and `Default.ListProbeOutcome`.

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/StackProof/StackProofJsonTests.cs`:

```csharp
using System.Text.Json;
using UasSort.Core.Json;
using UasSort.Core.StackProof;

namespace UasSort.Core.Tests.StackProof;

/// <summary>Ref §3 JSON rule and §14 step 1: closed records round-trip through a source-generated context.</summary>
public sealed class StackProofJsonTests
{
    private const string ExpectedJson =
        """[{"t":"copied","path":"2026/2026-09/DJI_0001.MP4","bytes":1024},{"t":"skipped","reason":"dup"},{"t":"conflict","existing":"DJI_0002.MP4","existingSize":7}]""";

    private static readonly List<ProbeOutcome> Outcomes =
    [
        new ProbeCopied("2026/2026-09/DJI_0001.MP4", 1024),
        new ProbeSkipped("dup"),
        new ProbeConflict("DJI_0002.MP4", 7),
    ];

    [Fact]
    public void ClosedRecords_SerialiseCamelCaseWithTheDiscriminatorFirst()
    {
        var json = JsonSerializer.Serialize(Outcomes, StackProofJsonContext.Default.ListProbeOutcome);
        Assert.Equal(ExpectedJson, json);
    }

    [Fact]
    public void ClosedRecords_RoundTripThroughTheSourceGeneratedContext()
    {
        var back = JsonSerializer.Deserialize(ExpectedJson, StackProofJsonContext.Default.ListProbeOutcome);
        Assert.NotNull(back);
        Assert.Equal(Outcomes, back);
    }

    [Fact]
    public void Deserialise_AcceptsTheDiscriminatorAfterOtherProperties()
    {
        var one = JsonSerializer.Deserialize("""{"reason":"dup","t":"skipped"}""", StackProofJsonContext.Default.ProbeOutcome);
        Assert.Equal(new ProbeSkipped("dup"), one);
    }

    [Fact]
    public void Deserialise_UnknownDiscriminator_IsRejected()
    {
        var ex = Record.Exception(() => JsonSerializer.Deserialize("""{"t":"moved"}""", StackProofJsonContext.Default.ProbeOutcome));
        Assert.True(ex is JsonException or NotSupportedException, "unexpected " + ex?.GetType().Name);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*StackProofJsonTests"`
Expected: FAIL — build error `CS0246: The type or namespace name 'ProbeOutcome' could not be found` (and `StackProofJsonContext`).

- [ ] **Step 3: Implement**

`src/UasSort.Core/StackProof/StackProofTypes.cs`:

```csharp
using System.Text.Json.Serialization;

namespace UasSort.Core.StackProof;

// Stack-proof canaries (Ref §14 step 1). They stay in Core permanently: the cross-assembly exhaustiveness test
// (Review.Tests) and the Native AOT selftest's closed-record JSON check use them.

/// <summary>A C# 15 closed hierarchy that is serialised (like PlanEdit, TargetChoice, LedgerRecord).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(ProbeCopied), "copied")]
[JsonDerivedType(typeof(ProbeSkipped), "skipped")]
[JsonDerivedType(typeof(ProbeConflict), "conflict")]
public closed record class ProbeOutcome;

public sealed record class ProbeCopied(string Path, long Bytes) : ProbeOutcome;

public sealed record class ProbeSkipped(string Reason) : ProbeOutcome;

public sealed record class ProbeConflict(string Existing, long ExistingSize) : ProbeOutcome;

public sealed record ProbeFix(double Lat, double Lon);

public sealed record ProbeNoFix(string Reason);

/// <summary>A C# 15 union kept in memory only (like GpsProbe, VerifyResult, EditResult).</summary>
public union ProbeGps(ProbeFix, ProbeNoFix);
```

`src/UasSort.Core/Json/StackProofJsonContext.cs`:

```csharp
using System.Text.Json.Serialization;
using UasSort.Core.StackProof;

namespace UasSort.Core.Json;

/// <summary>Source-generated context for the stack-proof canaries (Ref §3: source-generated contexts only).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(ProbeOutcome))]
[JsonSerializable(typeof(List<ProbeOutcome>))]
public sealed partial class StackProofJsonContext : JsonSerializerContext
{
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*StackProofJsonTests"`
Expected: PASS — `failed: 0, succeeded: 4`; the Core build shows no IL2xxx/IL3xxx trim or AOT warning.

Run: `dotnet test --solution uas-sort.slnx`
Expected: PASS — `failed: 0, succeeded: 15`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/StackProof src/UasSort.Core/Json tests/UasSort.Core.Tests/StackProof/StackProofJsonTests.cs
git commit -F - <<'EOF'
feat: add closed-record and union stack-proof canaries with source-generated JSON

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 01.6: Cross-assembly union/closed exhaustiveness test

**Files:**
- Create: `tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj`
- Test: `tests/UasSort.Review.Tests/StackProof/CrossAssemblyExhaustivenessTests.cs`
- Modify: `uas-sort.slnx`

**Interfaces:**
- Consumes: `ProbeOutcome`, `ProbeCopied`, `ProbeSkipped`, `ProbeConflict`, `ProbeGps`, `ProbeFix`, `ProbeNoFix` (Task 01.5).
- Produces: the `UasSort.Review.Tests` project (net11.0; references Review and Testing), where Ref §2.3 puts cross-assembly exhaustive-switch tests; Part 10 adds its tests here.

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <RootNamespace>UasSort.Review.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3.mtp-v2" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\UasSort.Review\UasSort.Review.csproj" />
    <ProjectReference Include="..\UasSort.Testing\UasSort.Testing.csproj" />
  </ItemGroup>
</Project>
```

`tests/UasSort.Review.Tests/StackProof/CrossAssemblyExhaustivenessTests.cs`:

```csharp
using System.Globalization;
using UasSort.Core.StackProof;

namespace UasSort.Review.Tests.StackProof;

/// <summary>
/// Ref §2.1 / §14 step 1: C# 15 exhaustiveness across assemblies. The switches below have NO default arm; they
/// compile only because the compiler treats Core's closed hierarchy and union as complete from another assembly.
/// If Core adds a case, this assembly stops compiling (CS8509 is an error under TreatWarningsAsErrors).
/// </summary>
public sealed class CrossAssemblyExhaustivenessTests
{
    private static readonly string[] ExpectedOutcomeCases = ["ProbeConflict", "ProbeCopied", "ProbeSkipped"];

    [Fact]
    public void ClosedHierarchyFromCore_SwitchWithoutDefaultArm_HandlesEveryCase()
    {
        Assert.Equal("copied DJI_0001.MP4 (1024 bytes)", Describe(new ProbeCopied("DJI_0001.MP4", 1024)));
        Assert.Equal("skipped: dup", Describe(new ProbeSkipped("dup")));
        Assert.Equal("conflict with DJI_0002.MP4 (7 bytes)", Describe(new ProbeConflict("DJI_0002.MP4", 7)));
    }

    [Fact]
    public void UnionFromCore_SwitchWithoutDefaultArm_HandlesEveryCase()
    {
        Assert.Equal("57.5368,-153.7484", Describe(new ProbeFix(57.5368, -153.7484)));
        Assert.Equal("no fix: NoDjmdTrack", Describe(new ProbeNoFix("NoDjmdTrack")));
    }

    [Fact]
    public void CoreDeclaresExactlyTheCasesThisSwitchCovers()
    {
        var cases = typeof(ProbeOutcome).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ProbeOutcome)))
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal);
        Assert.Equal(ExpectedOutcomeCases, cases);
    }

    private static string Describe(ProbeOutcome outcome) => outcome switch
    {
        ProbeCopied c => string.Create(CultureInfo.InvariantCulture, $"copied {c.Path} ({c.Bytes} bytes)"),
        ProbeSkipped s => "skipped: " + s.Reason,
        ProbeConflict k => string.Create(CultureInfo.InvariantCulture, $"conflict with {k.Existing} ({k.ExistingSize} bytes)"),
    };

    private static string Describe(ProbeGps gps) => gps switch
    {
        ProbeFix f => string.Create(CultureInfo.InvariantCulture, $"{f.Lat:F4},{f.Lon:F4}"),
        ProbeNoFix n => "no fix: " + n.Reason,
    };
}
```

Add `<Project Path="tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj" />` to the `/tests/` folder of `uas-sort.slnx` (after `UasSort.Platform.Tests`).

- [ ] **Step 2: Run test to verify it fails (negative exhaustiveness check)**

Temporarily delete the `ProbeConflict k => …` arm, then run: `dotnet build tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -tl:off`
Expected: FAIL — `error CS8509: The switch expression does not handle all possible values of its input type (it is not exhaustive). For example, the pattern 'ProbeConflict' is not covered.`

Restore that arm, temporarily delete the `ProbeNoFix n => …` arm, and build again.
Expected: FAIL — a non-exhaustive-switch error (CS8509) naming `ProbeNoFix`.

Restore the arm. (If either build succeeds with an arm missing, exhaustiveness across assemblies does not work: stop and raise it with the user; the Ref §2.2 fallback is `net10.0` with abstract records and default arms.)

- [ ] **Step 3: Implement**

No production code: the arms restored in Step 2 are the implementation. Confirm the file matches Step 1 exactly.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj`
Expected: PASS — `failed: 0, succeeded: 3`.

Run: `dotnet test --solution uas-sort.slnx`
Expected: PASS — `failed: 0, succeeded: 18`.

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Review.Tests uas-sort.slnx
git commit -F - <<'EOF'
test: prove C# 15 closed and union exhaustiveness across assemblies

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 01.7: `stack-exif.jpg` fixture, MetadataExtractor Stream read and GeoTimeZone lookup

**Files:**
- Create: `tools/fixtures/make-selftest-assets.cs`, `src/UasSort.App/SelfTest/stack-exif.jpg` (generated, checked in)
- Test: `tests/UasSort.Core.Tests/StackProof/StackExifFixtureTests.cs`

**Interfaces:**
- Consumes: `RepoPaths` (Task 01.2); Ref §13 (`stack-exif.jpg` ≤ 2 KB, hand-assembled EXIF with DTO and GPS, no user data); the Zachar Bay fixture point (57.5368, −153.7484) from Ref §13.
- Produces: `tools/fixtures/make-selftest-assets.cs` (Ref §2.3 name; a .NET file-based app run as `dotnet run tools/fixtures/make-selftest-assets.cs -- .`; it writes every asset of `SelfTestAssets.All()`, currently only `stack-exif.jpg` — Part 11 Task 11.9 modifies this file in place and adds `selftest.dng`, `ledger-v1.jsonl`, `selftest-0001.mp4`, `selftest-0002.mp4`, `selftest-0003.mp4`; no other part creates it); `src/UasSort.App/SelfTest/stack-exif.jpg` (204 bytes: DTO `2026:09:27 14:01:27`, GPS 57.5368 N 153.7484 W).

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Core.Tests/StackProof/StackExifFixtureTests.cs`:

```csharp
using GeoTimeZone;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using UasSort.Testing;

namespace UasSort.Core.Tests.StackProof;

/// <summary>Ref §13 / §14 step 1: the checked-in EXIF fixture read through MetadataExtractor's Stream API, and GeoTimeZone.</summary>
public sealed class StackExifFixtureTests
{
    private static readonly string FixturePath = RepoPaths.Of("src/UasSort.App/SelfTest/stack-exif.jpg");

    [Fact]
    public void Fixture_IsASmallJpegContainer()
    {
        var bytes = File.ReadAllBytes(FixturePath);
        Assert.InRange(bytes.Length, 100, 2048);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);   // SOI
        Assert.Equal(0xFF, bytes[^2]);
        Assert.Equal(0xD9, bytes[^1]);  // EOI
    }

    [Fact]
    public void StreamRead_FindsDateTimeOriginal()
    {
        using var stream = File.OpenRead(FixturePath);
        var directories = JpegMetadataReader.ReadMetadata(stream);
        var exif = directories.OfType<ExifSubIfdDirectory>().Single();
        Assert.Equal("2026:09:27 14:01:27", exif.GetString(ExifDirectoryBase.TagDateTimeOriginal));
    }

    [Fact]
    public void StreamRead_FindsZacharBayGps()
    {
        using var stream = File.OpenRead(FixturePath);
        var gps = JpegMetadataReader.ReadMetadata(stream).OfType<GpsDirectory>().Single();
        Assert.True(gps.TryGetGeoLocation(out var location));
        Assert.Equal(57.5368, location.Latitude, 6);
        Assert.Equal(-153.7484, location.Longitude, 6);
    }

    [Fact]
    public void GeoTimeZone_ZacharBay_IsAnchorage_AndIcuKnowsTheZone()
    {
        var tz = TimeZoneLookup.GetTimeZone(57.5368, -153.7484).Result;
        Assert.Equal("America/Anchorage", tz);
        Assert.Equal(TimeSpan.FromHours(-9), TimeZoneInfo.FindSystemTimeZoneById(tz).BaseUtcOffset);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*StackExifFixtureTests"`
Expected: FAIL — the three fixture tests with `System.IO.FileNotFoundException: Could not find file '…\src\UasSort.App\SelfTest\stack-exif.jpg'`; `GeoTimeZone_ZacharBay_IsAnchorage_AndIcuKnowsTheZone` passes.

- [ ] **Step 3: Implement**

`tools/fixtures/make-selftest-assets.cs`:

```csharp
// Writes the checked-in selftest assets into src/UasSort.App/SelfTest/ (Ref §2.3, §13). No user data.
// usage (from the repo root): dotnet run tools/fixtures/make-selftest-assets.cs -- .
using System.Buffers.Binary;
using System.Text;

var repoRoot = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var selfTestDir = Path.Join(repoRoot, "src", "UasSort.App", "SelfTest");
Directory.CreateDirectory(selfTestDir);
foreach (var (name, bytes) in SelfTestAssets.All())
{
    var path = Path.Join(selfTestDir, name);
    File.WriteAllBytes(path, bytes);
    Console.WriteLine($"{path} ({bytes.Length} bytes)");
}

internal static class SelfTestAssets
{
    private const ushort TypeByte = 1;
    private const ushort TypeAscii = 2;
    private const ushort TypeLong = 4;
    private const ushort TypeRational = 5;

    /// <summary>
    /// Every asset this tool writes. Part 11 (Task 11.9) extends this list in place with selftest.dng, ledger-v1.jsonl
    /// and selftest-0001..0003.mp4.
    /// </summary>
    public static IEnumerable<(string Name, byte[] Bytes)> All()
    {
        yield return ("stack-exif.jpg", StackExifJpeg());
    }

    /// <summary>
    /// SOI, APP1 "Exif" (little-endian TIFF: IFD0 -> Exif IFD with DateTimeOriginal, GPS IFD with Zachar Bay
    /// 57.5368 N 153.7484 W), EOI. A metadata carrier, not a decodable image; MetadataExtractor stops at EOI.
    /// </summary>
    public static byte[] StackExifJpeg()
    {
        const int Ifd0 = 8, ExifIfd = 38, Dto = 56, GpsIfd = 76, Lat = 142, Lon = 166, TiffLength = 190;
        var tiff = new byte[TiffLength];
        var s = tiff.AsSpan();
        s[0] = (byte)'I';
        s[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(s[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..], Ifd0);

        // IFD0 (2 entries; next-IFD offset at Ifd0 + 26 stays 0)
        BinaryPrimitives.WriteUInt16LittleEndian(s[Ifd0..], 2);
        Entry(s, Ifd0 + 2, 0x8769, TypeLong, 1, ExifIfd);
        Entry(s, Ifd0 + 14, 0x8825, TypeLong, 1, GpsIfd);

        // Exif IFD: DateTimeOriginal
        BinaryPrimitives.WriteUInt16LittleEndian(s[ExifIfd..], 1);
        Entry(s, ExifIfd + 2, 0x9003, TypeAscii, 20, Dto);
        Encoding.ASCII.GetBytes("2026:09:27 14:01:27\0").CopyTo(s[Dto..]);

        // GPS IFD
        BinaryPrimitives.WriteUInt16LittleEndian(s[GpsIfd..], 5);
        Entry(s, GpsIfd + 2, 0x0000, TypeByte, 4, 0x0000_0302); // GPSVersionID 2.3.0.0
        Entry(s, GpsIfd + 14, 0x0001, TypeAscii, 2, 'N');       // "N\0" inline
        Entry(s, GpsIfd + 26, 0x0002, TypeRational, 3, Lat);
        Entry(s, GpsIfd + 38, 0x0003, TypeAscii, 2, 'W');       // "W\0" inline
        Entry(s, GpsIfd + 50, 0x0004, TypeRational, 3, Lon);
        Rationals(s[Lat..], (57, 1), (32, 1), (1248, 100));     // 57° 32' 12.48" = 57.5368
        Rationals(s[Lon..], (153, 1), (44, 1), (5424, 100));    // 153° 44' 54.24" = 153.7484

        var header = "Exif\0\0"u8;
        var app1Length = 2 + header.Length + tiff.Length;       // the APP1 length field counts itself
        var jpg = new byte[2 + 2 + app1Length + 2];
        jpg[0] = 0xFF;
        jpg[1] = 0xD8;                                          // SOI
        jpg[2] = 0xFF;
        jpg[3] = 0xE1;                                          // APP1
        BinaryPrimitives.WriteUInt16BigEndian(jpg.AsSpan(4), (ushort)app1Length);
        header.CopyTo(jpg.AsSpan(6));
        tiff.CopyTo(jpg.AsSpan(6 + header.Length));
        jpg[^2] = 0xFF;
        jpg[^1] = 0xD9;                                         // EOI
        return jpg;
    }

    private static void Entry(Span<byte> s, int at, ushort tag, ushort type, uint count, uint valueOrOffset)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(s[at..], tag);
        BinaryPrimitives.WriteUInt16LittleEndian(s[(at + 2)..], type);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(at + 4)..], count);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(at + 8)..], valueOrOffset);
    }

    private static void Rationals(Span<byte> s, params ReadOnlySpan<(uint Numerator, uint Denominator)> values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(s[(i * 8)..], values[i].Numerator);
            BinaryPrimitives.WriteUInt32LittleEndian(s[(i * 8 + 4)..], values[i].Denominator);
        }
    }
}
```

Run: `dotnet run tools/fixtures/make-selftest-assets.cs -- .`
Expected output: `C:\dev\uas-sort\src\UasSort.App\SelfTest\stack-exif.jpg (204 bytes)`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*StackExifFixtureTests"`
Expected: PASS — `failed: 0, succeeded: 4`.

Run: `dotnet test --solution uas-sort.slnx`
Expected: PASS — `failed: 0, succeeded: 22`.

- [ ] **Step 5: Commit**

```bash
git add tools/fixtures/make-selftest-assets.cs src/UasSort.App/SelfTest/stack-exif.jpg tests/UasSort.Core.Tests/StackProof/StackExifFixtureTests.cs
git commit -F - <<'EOF'
test: add hand-assembled stack-exif.jpg and prove Stream reads and GeoTimeZone

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 01.8: Platform Win32 helpers — named mutex, single-instance names, foreground, placeholder mode

**Files:**
- Create: `src/UasSort.Platform/Win32/NativeMethods.cs`, `src/UasSort.Platform/Win32/NamedMutexLock.cs`, `src/UasSort.Platform/Win32/SingleInstance.cs`, `src/UasSort.Platform/Win32/ForegroundWindow.cs`, `src/UasSort.Platform/Win32/PlaceholderMode.cs`
- Test: `tests/UasSort.Platform.Tests/Win32/NamedMutexLockTests.cs`, `tests/UasSort.Platform.Tests/Win32/PlaceholderModeTests.cs`

**Interfaces:**
- Consumes: Ref §4.4 step 1 (`AppInstance` key `uas-sort`, fallback mutex `Local\uas-sort`, `AllowSetForegroundWindow`, `SetForegroundWindow`, the placeholder compatibility call); Ref §4.1 `IOffloadLock` ("named mutex `Local\uas-sort-offload`").
- Produces (defined here; these are the only definitions — Parts 09, 11 and 12 reuse them and never re-declare them; no `MutexHolder`, no `Shell.SingleInstance`, no second placeholder or foreground P/Invoke elsewhere):
  - `public sealed class UasSort.Platform.Win32.NamedMutexLock : IDisposable` — `static NamedMutexLock? TryAcquire(string name)` (null when another holder has the name; existence-based, not thread-affine, so any thread may dispose it); `string Name { get; }`; `void Dispose()`. Part 09's `OffloadLock : IOffloadLock` (`UasSort.Platform.Shell`) wraps it with `Local\uas-sort-offload`.
  - `public static class UasSort.Platform.Win32.SingleInstance` — `const string AppInstanceKey = "uas-sort"`; `const string MutexName = @"Local\uas-sort"`.
  - `public static class UasSort.Platform.Win32.ForegroundWindow` — `static bool AllowSetForeground(uint processId)`; `static bool BringToFront(nint hwnd)`.
  - `public static class UasSort.Platform.Win32.PlaceholderMode` — `const sbyte PhcmExposePlaceholders = 2`; `static sbyte ExposePlaceholders()` (returns the previous mode; negative = `PHCM_ERROR_*`). The only way to expose placeholders: the App's and the CLI's `Program.Main` call it first; Part 09's `PlaceholderGuard` keeps only `ReadAttributes`.
  - `internal static partial class UasSort.Platform.Win32.NativeMethods` (the `LibraryImport` declarations of this part).

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/Win32/NamedMutexLockTests.cs`:

```csharp
using UasSort.Platform.Win32;

namespace UasSort.Platform.Tests.Win32;

/// <summary>Ref §13 Windows integration: "the named-mutex fallback" (Ref §4.4 step 1).</summary>
public sealed class NamedMutexLockTests
{
    private static string UniqueName() => @"Local\uas-sort-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void TryAcquire_FirstCaller_GetsTheLock()
    {
        var name = UniqueName();
        using var first = NamedMutexLock.TryAcquire(name);
        Assert.NotNull(first);
        Assert.Equal(name, first.Name);
    }

    [Fact]
    public void TryAcquire_WhileHeld_ReturnsNull()
    {
        var name = UniqueName();
        using var first = NamedMutexLock.TryAcquire(name);
        using var second = NamedMutexLock.TryAcquire(name);
        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void TryAcquire_AfterTheHolderDisposes_Succeeds()
    {
        var name = UniqueName();
        NamedMutexLock.TryAcquire(name)!.Dispose();
        using var again = NamedMutexLock.TryAcquire(name);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task Dispose_FromAnotherThread_ReleasesTheName()
    {
        var name = UniqueName();
        var held = NamedMutexLock.TryAcquire(name)!;
        await Task.Run(held.Dispose, TestContext.Current.CancellationToken);
        using var again = NamedMutexLock.TryAcquire(name);
        Assert.NotNull(again);
    }

    [Fact]
    public void SingleInstanceNames_MatchTheSpec()
    {
        Assert.Equal("uas-sort", SingleInstance.AppInstanceKey);
        Assert.Equal(@"Local\uas-sort", SingleInstance.MutexName);
    }
}
```

`tests/UasSort.Platform.Tests/Win32/PlaceholderModeTests.cs`:

```csharp
using UasSort.Platform.Win32;

namespace UasSort.Platform.Tests.Win32;

/// <summary>Ref §4.3/§4.4: RtlSetProcessPlaceholderCompatibilityMode(PHCM_EXPOSE_PLACEHOLDERS) at process start.</summary>
public sealed class PlaceholderModeTests
{
    [Fact]
    public void ExposePlaceholders_Succeeds_AndLeavesTheProcessInExposeMode()
    {
        var previous = PlaceholderMode.ExposePlaceholders();
        Assert.True(previous >= 0, $"PHCM error {previous}");
        Assert.Equal(PlaceholderMode.PhcmExposePlaceholders, PlaceholderMode.ExposePlaceholders());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*UasSort.Platform.Tests.Win32*"`
Expected: FAIL — build error `CS0246: The type or namespace name 'NamedMutexLock' could not be found` (and `SingleInstance`, `PlaceholderMode`).

- [ ] **Step 3: Implement**

`src/UasSort.Platform/Win32/NativeMethods.cs`:

```csharp
using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

internal static partial class NativeMethods
{
    internal const int SwRestore = 9;

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(uint dwProcessId);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hWnd, int nCmdShow);

    /// <summary>ntdll: CHAR RtlSetProcessPlaceholderCompatibilityMode(CHAR Mode); returns the previous mode.</summary>
    [LibraryImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial sbyte RtlSetProcessPlaceholderCompatibilityMode(sbyte mode);
}
```

`src/UasSort.Platform/Win32/NamedMutexLock.cs`:

```csharp
namespace UasSort.Platform.Win32;

/// <summary>
/// An existence-based named-mutex lock: whoever creates the named mutex owns the name until it disposes its handle
/// (or the process exits). Ownership is never taken, so the lock is not thread-affine and any thread may dispose it.
/// Used for the single-instance fallback (Local\uas-sort) and, from Part 09, the offload lock (Local\uas-sort-offload).
/// </summary>
public sealed class NamedMutexLock : IDisposable
{
    private readonly Mutex _mutex;

    private NamedMutexLock(Mutex mutex, string name)
    {
        _mutex = mutex;
        Name = name;
    }

    public string Name { get; }

    public static NamedMutexLock? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        if (createdNew)
        {
            return new NamedMutexLock(mutex, name);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose() => _mutex.Dispose();
}
```

`src/UasSort.Platform/Win32/SingleInstance.cs`:

```csharp
namespace UasSort.Platform.Win32;

/// <summary>Single-instance identifiers (Ref §4.4 step 1).</summary>
public static class SingleInstance
{
    public const string AppInstanceKey = "uas-sort";
    public const string MutexName = @"Local\uas-sort";
}
```

`src/UasSort.Platform/Win32/ForegroundWindow.cs`:

```csharp
namespace UasSort.Platform.Win32;

/// <summary>Bringing the first instance forward (Ref §4.4 step 1).</summary>
public static class ForegroundWindow
{
    /// <summary>Called by the second instance before it redirects activation to <paramref name="processId"/>.</summary>
    public static bool AllowSetForeground(uint processId) => NativeMethods.AllowSetForegroundWindow(processId);

    /// <summary>Called by the main instance (on its UI thread) when activation is redirected to it.</summary>
    public static bool BringToFront(nint hwnd)
    {
        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
        }

        return NativeMethods.SetForegroundWindow(hwnd);
    }
}
```

`src/UasSort.Platform/Win32/PlaceholderMode.cs`:

```csharp
namespace UasSort.Platform.Win32;

/// <summary>
/// Makes cloud placeholders visible as such to this process (Ref §4.3: "At process start, Platform calls
/// RtlSetProcessPlaceholderCompatibilityMode(PHCM_EXPOSE_PLACEHOLDERS)"). Program.Main calls it first thing.
/// </summary>
public static class PlaceholderMode
{
    public const sbyte PhcmExposePlaceholders = 2;

    /// <summary>Returns the previous mode (0 = PHCM_APPLICATION_DEFAULT); a negative value is a PHCM_ERROR_* code.</summary>
    public static sbyte ExposePlaceholders() => NativeMethods.RtlSetProcessPlaceholderCompatibilityMode(PhcmExposePlaceholders);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*UasSort.Platform.Tests.Win32*"`
Expected: PASS — `failed: 0, succeeded: 6`.

Run: `dotnet test --solution uas-sort.slnx`
Expected: PASS — `failed: 0, succeeded: 28`; the Platform build has no SYSLIB1062 and no IL/AOT warning.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform/Win32 tests/UasSort.Platform.Tests/Win32
git commit -F - <<'EOF'
feat: add named-mutex lock, single-instance names, foreground and placeholder-mode helpers

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 01.9: Selftest sandbox in Platform

**Files:**
- Create: `src/UasSort.Platform/Stores/SelfTestSandbox.cs`
- Test: `tests/UasSort.Platform.Tests/Stores/SelfTestSandboxTests.cs`

**Interfaces:**
- Consumes: Ref §11 "Selftest" row (`%TEMP%\uas-sort-selftest-<guid>\`, deleted at exit); Ref §9.6 (WebView2 user data folder is a temp root under `--selftest`); `TestTempDir` (Task 01.2).
- Produces (defined here; the App is banned from file APIs, so the selftest's file work lives in Platform):
  - `public sealed partial class UasSort.Platform.Stores.SelfTestSandbox : IDisposable` — `const string FolderPrefix = "uas-sort-selftest-"`; `static SelfTestSandbox Create(string tempRoot)`; `string Root { get; }`; `string WebView2Folder { get; }` (`Root\WebView2`); `static void WriteResult(string path, ReadOnlySpan<byte> utf8Json)` (fully qualified `.json` path only; temp file then replace); `bool TryDelete()`; `void Dispose()`. The class is `partial` so that Part 11 Task 11.3 extends it in place, in a second file of the same namespace, with `static SelfTestSandbox Create()` (= `Create(Path.GetTempPath())`), `string AppDataDir`, `string VideoRoot`, `string PhotoRoot`, `string CardRoot` and `void WriteFile(string relativeToRoot, ReadOnlySpan<byte> content)`; their tests go into this task's `SelfTestSandboxTests`.

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/Stores/SelfTestSandboxTests.cs`:

```csharp
using UasSort.Platform.Stores;
using UasSort.Testing;

namespace UasSort.Platform.Tests.Stores;

public sealed class SelfTestSandboxTests
{
    [Fact]
    public void Create_MakesAFreshFolderUnderTheGivenTempRoot()
    {
        using var temp = new TestTempDir();
        using var sandbox = SelfTestSandbox.Create(temp.FullPath);
        Assert.True(Directory.Exists(sandbox.Root));
        Assert.StartsWith(Path.Join(temp.FullPath, "uas-sort-selftest-"), sandbox.Root, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Join(sandbox.Root, "WebView2"), sandbox.WebView2Folder);
    }

    [Fact]
    public void Create_TwiceGivesTwoDifferentFolders()
    {
        using var temp = new TestTempDir();
        using var a = SelfTestSandbox.Create(temp.FullPath);
        using var b = SelfTestSandbox.Create(temp.FullPath);
        Assert.NotEqual(a.Root, b.Root);
    }

    [Fact]
    public void Dispose_DeletesTheFolderAndEverythingInIt()
    {
        using var temp = new TestTempDir();
        var sandbox = SelfTestSandbox.Create(temp.FullPath);
        Directory.CreateDirectory(Path.Join(sandbox.WebView2Folder, "EBWebView"));
        File.WriteAllText(Path.Join(sandbox.WebView2Folder, "EBWebView", "Local State"), "{}");
        sandbox.Dispose();
        Assert.False(Directory.Exists(sandbox.Root));
    }

    [Fact]
    public void WriteResult_WritesExactBytes_ReplacingAnOlderResult()
    {
        using var temp = new TestTempDir();
        var path = temp.Combine("selftest-result.json");
        File.WriteAllText(path, "old");
        SelfTestSandbox.WriteResult(path, """{"ok":true,"firstFrameMs":312,"checks":[]}"""u8);
        Assert.Equal("""{"ok":true,"firstFrameMs":312,"checks":[]}""", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Theory]
    [InlineData("selftest-result.json")]
    [InlineData(@"C:\uas-sort-test\selftest-result.txt")]
    public void WriteResult_RefusesRelativeOrNonJsonPaths(string path)
    {
        Assert.Throws<ArgumentException>(() => SelfTestSandbox.WriteResult(path, "{}"u8));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*SelfTestSandboxTests"`
Expected: FAIL — build error `CS0246: The type or namespace name 'SelfTestSandbox' could not be found`.

- [ ] **Step 3: Implement**

`src/UasSort.Platform/Stores/SelfTestSandbox.cs`:

```csharp
namespace UasSort.Platform.Stores;

/// <summary>
/// The --selftest sandbox (Ref §11 "Selftest" row): %TEMP%\uas-sort-selftest-&lt;guid&gt;\ holding the WebView2 user data
/// folder (and, from Part 11, the app-data folder, video/photo roots and card root). Deleted at exit. Also writes the
/// result file. Partial: Part 11 (Task 11.3) adds its members in a second file; nothing redeclares this class.
/// </summary>
public sealed partial class SelfTestSandbox : IDisposable
{
    public const string FolderPrefix = "uas-sort-selftest-";

    private SelfTestSandbox(string root) => Root = root;

    public string Root { get; }

    public string WebView2Folder => Path.Join(Root, "WebView2");

    public static SelfTestSandbox Create(string tempRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempRoot);
        var root = Path.Join(Path.GetFullPath(tempRoot), FolderPrefix + Guid.NewGuid().ToString("N"));
#pragma warning disable RS0030 // IO layer: selftest sandbox, a fresh folder under %TEMP% (Ref §11)
        Directory.CreateDirectory(root);
#pragma warning restore RS0030
        return new SelfTestSandbox(root);
    }

    /// <summary>Writes selftest-result.json where the caller (deploy.ps1, tools/run-selftest.ps1) asked for it.</summary>
    public static void WriteResult(string path, ReadOnlySpan<byte> utf8Json)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The selftest result path must be a fully qualified .json path.", nameof(path));
        }

        var temp = path + ".tmp";
#pragma warning disable RS0030 // IO layer: the selftest result file named on the command line
        File.WriteAllBytes(temp, utf8Json);
        File.Move(temp, path, overwrite: true);
#pragma warning restore RS0030
    }

    /// <summary>Deletes the sandbox; retries while WebView2's child processes release their files (up to 3 s).</summary>
    public bool TryDelete()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (!Directory.Exists(Root))
            {
                return true;
            }

            try
            {
#pragma warning disable RS0030 // IO layer: deletes only this run's own selftest sandbox
                Directory.Delete(Root, recursive: true);
#pragma warning restore RS0030
                return true;
            }
            catch (IOException)
            {
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(300);
            }
        }

        return !Directory.Exists(Root);
    }

    public void Dispose() => TryDelete();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*SelfTestSandboxTests"`
Expected: PASS — `failed: 0, succeeded: 6`.

Run: `dotnet test --solution uas-sort.slnx`
Expected: PASS — `failed: 0, succeeded: 34`; `0 Warning(s)` (each banned Platform call carries its pragma).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform/Stores/SelfTestSandbox.cs tests/UasSort.Platform.Tests/Stores/SelfTestSandboxTests.cs
git commit -F - <<'EOF'
feat: add the selftest sandbox and result writer to Platform

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 01.10: WinUI App with the probe page and single-instance `Program.Main`

**Files:**
- Create: `src/UasSort.App/UasSort.App.csproj`, `src/UasSort.App/app.manifest`, `src/UasSort.App/Program.cs`, `src/UasSort.App/LaunchOptions.cs`, `src/UasSort.App/SingleInstanceGate.cs`, `src/UasSort.App/App.xaml`, `src/UasSort.App/App.xaml.cs`, `src/UasSort.App/MainWindow.xaml`, `src/UasSort.App/MainWindow.xaml.cs`
- Create: `src/UasSort.App/Pages/StackProbePage.xaml`, `src/UasSort.App/Pages/StackProbePage.xaml.cs`, `src/UasSort.App/Pages/StackProbeVm.cs`, `src/UasSort.App/Pages/ProbeTemplateSelector.cs`, `src/UasSort.App/MapAssets/probe.html`
- Test: `tests/UasSort.Core.Tests/Build/BuildConfigTests.cs` (add `AppProject_IsUnpackagedSelfContainedNativeAot`), `tests/UasSort.Platform.Tests/Launch/LaunchOptionsTests.cs`
- Modify: `uas-sort.slnx`, `tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj` (links `src/UasSort.App/LaunchOptions.cs` so the parser is testable without referencing the WinUI exe)

**Interfaces:**
- Consumes: Ref §2.5 App csproj; §2.7 #1–#6; §4.4 step 1; §9.6 host rules (virtual host `map.uas-sort.example` → `<app>\MapAssets`, `DenyCors`; `WebMessageReceived` before `Navigate`; `DownloadStarting` cancelled; navigation outside the host cancelled; user data folder `%LOCALAPPDATA%\uas-sort\WebView2`); `NamedMutexLock`, `SingleInstance`, `ForegroundWindow`, `PlaceholderMode` (Task 01.8).
- Produces:
  - `UasSort.App` project, `AssemblyName=uas-sort` (so the executable is `uas-sort.exe`, as Ref §13 names it).
  - `public static class UasSort.App.Program` — `[STAThread] public static int Main(string[] args)` (Ref §4.4 step 1): ComWrappers → `PlaceholderMode.ExposePlaceholders()` → `LaunchOptions.Parse` (a rejected command line exits `2`) → `SingleInstanceGate.Claim(options.ForceMutex)` unless `SelfTest` → `Application.Start`.
  - `internal sealed record UasSort.App.LaunchOptions(bool SelfTest, string ResultPath, IReadOnlySet<string>? Only, bool ForceMutex, DateTime ProcessStartUtc)` with `static LaunchOptions Parse(IReadOnlyList<string> args, DateTime processStartUtc)` and consts `SelfTestFlag = "--selftest"`, `ResultFlag = "--result"`, `OnlyFlag = "--only"` (comma-separated check names, case-sensitive; `Only` is null when absent), `ForceMutexFlag = "--single-instance-mutex"` (forces the fallback for testing), `DefaultResultFileName = "uas-sort-selftest-result.json"` (in `%TEMP%`). Command line: `uas-sort.exe --selftest --result <path> [--only <check>[,<check>…]]` (the Part 13 contract; there is no `--selftest-result`). Any other argument, or `--result`/`--only` without a value, throws `ArgumentException`. Plain C# only (no WinUI types): Platform.Tests compiles the file through a linked `Compile` item. Part 11 extends it in place.
  - `internal sealed class UasSort.App.SingleInstanceGate : IDisposable` — `static SingleInstanceGate Claim(bool forceMutex)`; `bool IsMain`; `string Mechanism` (`"AppInstance"` | `"Mutex"`); `AppInstance? Key`. Part 11 adds `event Action? Activated` in the same file.
  - `public partial class UasSort.App.App` — `internal static MainWindow? MainWindow`; `internal static string WebView2Folder`.
  - `public sealed partial class UasSort.App.MainWindow` — `internal Task<double> FirstFrameMs` (process start to first `CompositionTarget.Rendering`); `internal StackProbePage? ProbePage`; `internal void ShowOffScreen()`; `internal void BringToFront()`. The HWND title is `uas-sort (stack proof, single instance: <mechanism>)`. Part 11 keeps `FirstFrameMs`, `ShowOffScreen` and `BringToFront` and removes `ProbePage` with the probe page.
  - `public sealed partial class UasSort.App.Pages.StackProbePage : Page` — `const string VirtualHost = "map.uas-sort.example"`; `const string ProbeUrl = "https://map.uas-sort.example/probe.html"`; `StackProbeVm Vm`; `Task<string> MapRoundTrip` (completes on `ready` + `pong` n = 1); `internal string? RootCardHeader`; `internal void CloseWebView()`; `internal static bool IsVirtualHost(string uri)`.
  - `StackProbeVm`, `ProbeItemVm`, `ProbeVideoVm`, `ProbePhotoVm`, `ProbeTemplateSelector` (App-only probe types; Part 11 replaces the probe page).
  - `MapAssets/probe.html`: first lines forward `error`/`unhandledrejection`; posts `{"v":1,"type":"ready",…}`; answers `ping` with `pong` (message shapes from Ref §9.6).

- [ ] **Step 1: Write the failing test**

Add to `tests/UasSort.Core.Tests/Build/BuildConfigTests.cs` (inside the class):

```csharp
    private static readonly string[] ForbiddenAppProperties =
        ["PublishReadyToRun", "PublishTrimmed", "PublishSingleFile", "InvariantGlobalization", "UseNls"];

    [Fact]
    public void AppProject_IsUnpackagedSelfContainedNativeAot()
    {
        var csproj = XDocument.Load(RepoPaths.Of("src/UasSort.App/UasSort.App.csproj"));
        string Prop(string name) => csproj.Descendants(name).Select(e => e.Value.Trim()).FirstOrDefault() ?? "";

        Assert.Equal("WinExe", Prop("OutputType"));
        Assert.Equal("uas-sort", Prop("AssemblyName"));
        Assert.Equal("net11.0-windows10.0.26100.0", Prop("TargetFramework"));
        Assert.Equal("10.0.26100.0", Prop("TargetPlatformMinVersion"));
        Assert.Equal("true", Prop("UseWinUI"));
        Assert.Equal("false", Prop("WinUISDKReferences"));
        Assert.Equal("x64;ARM64", Prop("Platforms"));
        Assert.Equal("win-x64;win-arm64", Prop("RuntimeIdentifiers"));
        Assert.Equal("None", Prop("WindowsPackageType"));
        Assert.Equal("true", Prop("WindowsAppSDKSelfContained"));
        Assert.Equal("true", Prop("SelfContained"));
        Assert.Equal("true", Prop("EnableMsixTooling"));
        Assert.Contains("DISABLE_XAML_GENERATED_MAIN", Prop("DefineConstants"), StringComparison.Ordinal);

        var release = csproj.Descendants("PropertyGroup")
            .Single(g => ((string?)g.Attribute("Condition"))?.Contains("'Release'", StringComparison.Ordinal) == true);
        Assert.Equal("true", release.Element("PublishAot")?.Value.Trim());
        Assert.Equal("MetadataExtractor;XmpCore", (string?)csproj.Descendants("TrimmerRootAssembly").Single().Attribute("Include"));
        foreach (var forbidden in ForbiddenAppProperties)
        {
            Assert.Empty(csproj.Descendants(forbidden));
        }
    }
```

Add to `tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj`, before `</Project>`:

```xml
  <ItemGroup>
    <!-- No test project references the WinUI exe; its plain-C# command-line parser is compiled in here to be tested. -->
    <Compile Include="..\..\src\UasSort.App\LaunchOptions.cs" Link="Launch\LaunchOptions.cs" />
  </ItemGroup>
```

`tests/UasSort.Platform.Tests/Launch/LaunchOptionsTests.cs`:

```csharp
using UasSort.App;

namespace UasSort.Platform.Tests.Launch;

/// <summary>
/// uas-sort.exe command line (Part 13 contract): --selftest --result &lt;path&gt; [--only a,b] and --single-instance-mutex;
/// anything else is rejected.
/// </summary>
public sealed class LaunchOptionsTests
{
    private static readonly DateTime Start = new(2026, 9, 27, 14, 0, 0, DateTimeKind.Utc);
    private static readonly string[] SelfTestWithResult = ["--selftest", "--result", @"C:\uas-sort-test\r.json"];
    private static readonly string[] SelfTestWithOnly = ["--selftest", "--only", "probePage, webView2"];
    private static readonly string[] ExpectedOnly = ["probePage", "webView2"];
    private static readonly string[] MutexOnly = ["--single-instance-mutex"];

    [Fact]
    public void Parse_NoArguments_IsANormalLaunch_WithTheTempDefaultResultPath()
    {
        var options = LaunchOptions.Parse([], Start);
        Assert.False(options.SelfTest);
        Assert.False(options.ForceMutex);
        Assert.Null(options.Only);
        Assert.Equal(Path.Join(Path.GetTempPath(), "uas-sort-selftest-result.json"), options.ResultPath);
        Assert.Equal(Start, options.ProcessStartUtc);
    }

    [Fact]
    public void Parse_SelfTestWithResult_TakesTheFullResultPath()
    {
        var options = LaunchOptions.Parse(SelfTestWithResult, Start);
        Assert.True(options.SelfTest);
        Assert.Equal(@"C:\uas-sort-test\r.json", options.ResultPath);
        Assert.Null(options.Only);
    }

    [Fact]
    public void Parse_Only_SplitsCommaSeparatedNames_CaseSensitively()
    {
        var options = LaunchOptions.Parse(SelfTestWithOnly, Start);
        Assert.NotNull(options.Only);
        Assert.Equal(ExpectedOnly, options.Only.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("probepage", options.Only);
    }

    [Fact]
    public void Parse_SingleInstanceMutex_ForcesTheFallback_WithoutSelfTest()
    {
        var options = LaunchOptions.Parse(MutexOnly, Start);
        Assert.True(options.ForceMutex);
        Assert.False(options.SelfTest);
    }

    [Theory]
    [InlineData("--selftest-result")]
    [InlineData("--verbose")]
    [InlineData("stray")]
    public void Parse_UnknownArgument_IsRejected(string argument)
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--selftest", argument, @"C:\uas-sort-test\r.json"], Start));
    }

    [Theory]
    [InlineData("--result")]
    [InlineData("--only")]
    public void Parse_FlagWithoutValue_IsRejected(string flag)
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--selftest", flag], Start));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*AppProject_IsUnpackagedSelfContainedNativeAot*"`
Expected: FAIL — `System.IO.FileNotFoundException: Could not find file '…\src\UasSort.App\UasSort.App.csproj'`.

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*LaunchOptionsTests"`
Expected: FAIL — build error `CS2001: Source file '…\src\UasSort.App\LaunchOptions.cs' could not be found.`

- [ ] **Step 3: Implement**

`src/UasSort.App/UasSort.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- Ref §2.5. Unpackaged, self-contained (.NET + Windows App SDK), lean component packages, Native AOT in Release. -->
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net11.0-windows10.0.26100.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.26100.0</TargetPlatformMinVersion>
    <RootNamespace>UasSort.App</RootNamespace>
    <AssemblyName>uas-sort</AssemblyName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <UseWinUI>true</UseWinUI>
    <WinUISDKReferences>false</WinUISDKReferences>
    <Platforms>x64;ARM64</Platforms>
    <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
    <!-- dotnet build of the solution: this machine's own RID. Publishes pass -r explicitly. -->
    <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == ''">$(NETCoreSdkPortableRuntimeIdentifier)</RuntimeIdentifier>
    <WindowsPackageType>None</WindowsPackageType>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <SelfContained>true</SelfContained>
    <!-- Still required unpackaged, else the .pri/.xbf files are missing and startup crashes with 0xC000027B. -->
    <EnableMsixTooling>true</EnableMsixTooling>
    <!-- Custom Program.Main (Ref §4.4). -->
    <DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>
  </PropertyGroup>

  <!-- Every Release publish is Native AOT (it implies trimming). ReadyToRun is never a build configuration. -->
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <PublishAot>true</PublishAot>
  </PropertyGroup>
  <ItemGroup Condition="'$(Configuration)' == 'Release'">
    <!-- MetadataExtractor's trim behaviour is UNVERIFIED, so it is rooted (Ref §2.1, §2.5). -->
    <TrimmerRootAssembly Include="MetadataExtractor;XmpCore" />
  </ItemGroup>

  <ItemGroup>
    <Manifest Include="$(ApplicationManifest)" />
    <Content Include="MapAssets\**" CopyToOutputDirectory="PreserveNewest" />
    <EmbeddedResource Include="SelfTest\*.jpg;SelfTest\*.dng;SelfTest\*.jsonl" LogicalName="UasSort.App.SelfTest.%(Filename)%(Extension)" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK.WinUI" />
    <PackageReference Include="Microsoft.WindowsAppSDK.Foundation" />
    <PackageReference Include="Microsoft.WindowsAppSDK.InteractiveExperiences" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" />
    <PackageReference Include="Microsoft.Web.WebView2" />
    <PackageReference Include="CommunityToolkit.WinUI.Controls.Sizers" />
    <PackageReference Include="CommunityToolkit.WinUI.Controls.SettingsControls" />
    <PackageReference Include="CommunityToolkit.Mvvm" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\UasSort.Review\UasSort.Review.csproj" />
    <ProjectReference Include="..\UasSort.Platform\UasSort.Platform.csproj" />
  </ItemGroup>
</Project>
```

`src/UasSort.App/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="uas-sort.app" />
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 / 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
      <longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>
    </windowsSettings>
  </application>
</assembly>
```

`src/UasSort.App/LaunchOptions.cs`:

```csharp
namespace UasSort.App;

/// <summary>
/// Command line of uas-sort.exe. <c>--selftest --result &lt;path&gt; [--only &lt;check&gt;[,&lt;check&gt;…]]</c> is the contract
/// tools/run-selftest.ps1 and deploy.ps1 rely on (Part 13); <c>--single-instance-mutex</c> forces the named-mutex
/// fallback for testing. Any other argument is rejected. Plain C# only (no WinUI types): Platform.Tests compiles this
/// file through a linked Compile item. Part 11 extends it in place.
/// </summary>
internal sealed record LaunchOptions(bool SelfTest, string ResultPath, IReadOnlySet<string>? Only, bool ForceMutex, DateTime ProcessStartUtc)
{
    public const string SelfTestFlag = "--selftest";
    public const string ResultFlag = "--result";
    public const string OnlyFlag = "--only";
    public const string ForceMutexFlag = "--single-instance-mutex";
    public const string DefaultResultFileName = "uas-sort-selftest-result.json";

    /// <exception cref="ArgumentException">An unknown argument, or --result / --only without a value.</exception>
    public static LaunchOptions Parse(IReadOnlyList<string> args, DateTime processStartUtc)
    {
        ArgumentNullException.ThrowIfNull(args);
        var selfTest = false;
        var forceMutex = false;
        var resultPath = Path.Join(Path.GetTempPath(), DefaultResultFileName);
        HashSet<string>? only = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case SelfTestFlag:
                    selfTest = true;
                    break;
                case ResultFlag:
                    resultPath = Path.GetFullPath(ValueAfter(args, ref i));
                    break;
                case OnlyFlag:
                    only = ValueAfter(args, ref i)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToHashSet(StringComparer.Ordinal);
                    if (only.Count == 0)
                    {
                        throw new ArgumentException(OnlyFlag + " needs at least one check name", nameof(args));
                    }

                    break;
                case ForceMutexFlag:
                    forceMutex = true;
                    break;
                default:
                    throw new ArgumentException("unknown argument: " + args[i], nameof(args));
            }
        }

        return new LaunchOptions(selfTest, resultPath, only, forceMutex, processStartUtc);
    }

    private static string ValueAfter(IReadOnlyList<string> args, ref int i)
    {
        var flag = args[i];
        if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException(flag + " needs a value", nameof(args));
        }

        return args[++i];
    }
}
```

`src/UasSort.App/SingleInstanceGate.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;
using UasSort.Platform.Win32;

namespace UasSort.App;

/// <summary>
/// Ref §4.4 step 1.2: AppInstance keyed "uas-sort"; a second launch lets the owner take the foreground and redirects
/// its activation on a worker thread while Main waits. If AppInstance misbehaves in the lean self-contained build
/// (UNVERIFIED), or --single-instance-mutex is given, the named mutex Local\uas-sort decides instead; a second
/// instance then simply exits.
/// </summary>
internal sealed class SingleInstanceGate : IDisposable
{
    private readonly NamedMutexLock? _mutex;

    private SingleInstanceGate(bool isMain, string mechanism, AppInstance? key, NamedMutexLock? mutex)
    {
        IsMain = isMain;
        Mechanism = mechanism;
        Key = key;
        _mutex = mutex;
    }

    public bool IsMain { get; }

    public string Mechanism { get; }

    public AppInstance? Key { get; }

    public static SingleInstanceGate Claim(bool forceMutex)
    {
        if (!forceMutex)
        {
            try
            {
                var key = AppInstance.FindOrRegisterForKey(SingleInstance.AppInstanceKey);
                if (key.IsCurrent)
                {
                    return new SingleInstanceGate(true, "AppInstance", key, null);
                }

                RedirectTo(key);
                return new SingleInstanceGate(false, "AppInstance", key, null);
            }
            catch (Exception ex) when (ex is COMException or TypeInitializationException or DllNotFoundException
                                           or EntryPointNotFoundException or InvalidOperationException)
            {
                Trace.WriteLine("AppInstance unavailable; using the named-mutex fallback: " + ex.Message);
            }
        }

        var mutex = NamedMutexLock.TryAcquire(SingleInstance.MutexName);
        return new SingleInstanceGate(mutex is not null, "Mutex", null, mutex);
    }

    public void Dispose() => _mutex?.Dispose();

    private static void RedirectTo(AppInstance owner)
    {
        ForegroundWindow.AllowSetForeground(owner.ProcessId);
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var redirect = Task.Run(async () => await owner.RedirectActivationToAsync(activation));
        try
        {
            redirect.Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException ex)
        {
            Trace.WriteLine("Activation redirect failed: " + ex.InnerException?.Message);
        }
    }
}
```

`src/UasSort.App/Program.cs`:

```csharp
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using UasSort.Platform.Win32;

namespace UasSort.App;

/// <summary>
/// Ref §4.4 step 1 (DISABLE_XAML_GENERATED_MAIN). Command line: none (normal launch), or
/// <c>--selftest --result &lt;path&gt; [--only &lt;check&gt;[,&lt;check&gt;…]]</c>, or <c>--single-instance-mutex</c>;
/// anything else exits 2.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        DateTime processStartUtc;
        using (var self = Process.GetCurrentProcess())
        {
            processStartUtc = self.StartTime.ToUniversalTime();
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        var previousMode = PlaceholderMode.ExposePlaceholders();
        if (previousMode < 0)
        {
            Trace.WriteLine("RtlSetProcessPlaceholderCompatibilityMode failed: " + previousMode);
        }

        // --selftest --result <path> [--only <check>[,<check>…]] | --single-instance-mutex
        LaunchOptions options;
        try
        {
            options = LaunchOptions.Parse(args, processStartUtc);
        }
        catch (ArgumentException ex)
        {
            Trace.WriteLine("uas-sort: " + ex.Message);
            return 2;
        }

        SingleInstanceGate? gate = null;
        if (!options.SelfTest)
        {
            gate = SingleInstanceGate.Claim(options.ForceMutex);
            if (!gate.IsMain)
            {
                gate.Dispose();
                return 0;
            }
        }

        try
        {
            Application.Start(_ =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new App(options, gate);
            });
        }
        finally
        {
            gate?.Dispose();
        }

        return 0;
    }
}
```

`src/UasSort.App/App.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Application x:Class="UasSort.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Application.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </Application.Resources>
</Application>
```

`src/UasSort.App/App.xaml.cs` (Task 01.11 adds the selftest branch):

```csharp
using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace UasSort.App;

public partial class App : Application
{
    private readonly LaunchOptions _options;
    private readonly SingleInstanceGate? _gate;

    internal App(LaunchOptions options, SingleInstanceGate? gate)
    {
        _options = options;
        _gate = gate;
        WebView2Folder = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uas-sort", "WebView2");
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    internal static MainWindow? MainWindow { get; private set; }

    internal static string WebView2Folder { get; private set; } = "";

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow(_options.ProcessStartUtc, _gate?.Mechanism ?? "skipped (selftest)");
        MainWindow = window;
        if (_gate?.Key is { } key)
        {
            key.Activated += (_, _) => window.DispatcherQueue.TryEnqueue(window.BringToFront);
        }

        window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
        Trace.WriteLine("UNHANDLED " + e.Exception);
}
```

`src/UasSort.App/MainWindow.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Window x:Class="UasSort.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="uas-sort">
  <Window.SystemBackdrop>
    <MicaBackdrop />
  </Window.SystemBackdrop>
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto" />
      <RowDefinition Height="*" />
    </Grid.RowDefinitions>
    <TitleBar x:Name="AppTitleBar" Title="uas-sort" Subtitle="stack proof" />
    <!-- Every view is a Page in this Frame (x:Bind in a Window initialises only on Activated; Ref §2.7 #3). -->
    <Frame x:Name="RootFrame" Grid.Row="1" />
  </Grid>
</Window>
```

`src/UasSort.App/MainWindow.xaml.cs`:

```csharp
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using UasSort.App.Pages;
using UasSort.Platform.Win32;
using Windows.Graphics;

namespace UasSort.App;

public sealed partial class MainWindow : Window
{
    private readonly DateTime _processStartUtc;
    private readonly TaskCompletionSource<double> _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal MainWindow(DateTime processStartUtc, string singleInstanceMechanism)
    {
        _processStartUtc = processStartUtc;
        InitializeComponent();
        Title = $"uas-sort (stack proof, single instance: {singleInstanceMechanism})";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        CompositionTarget.Rendering += OnFirstRendering;
        RootFrame.Navigate(typeof(StackProbePage));
    }

    /// <summary>Milliseconds from process start to the first CompositionTarget.Rendering (Ref §13 firstFrameMs).</summary>
    internal Task<double> FirstFrameMs => _firstFrame.Task;

    internal StackProbePage? ProbePage => RootFrame.Content as StackProbePage;

    /// <summary>--selftest launches off-screen and never activates the window (Ref §13 Isolation).</summary>
    internal void ShowOffScreen()
    {
        AppWindow.Move(new PointInt32(-6000, -6000));
        AppWindow.Show(false);
    }

    internal void BringToFront() => ForegroundWindow.BringToFront(Win32Interop.GetWindowFromWindowId(AppWindow.Id));

    private void OnFirstRendering(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnFirstRendering;
        _firstFrame.TrySetResult((TimeProvider.System.GetUtcNow().UtcDateTime - _processStartUtc).TotalMilliseconds);
    }
}
```

`src/UasSort.App/Pages/StackProbeVm.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UasSort.App.Pages;

/// <summary>Probe-page view model: partial properties (MVVM Toolkit 8.4.2) and an ObservableCollection of VM classes.</summary>
public sealed partial class StackProbeVm : ObservableObject
{
    [ObservableProperty]
    public partial string Status { get; set; } = "Starting";

    [ObservableProperty]
    public partial string FolderText { get; set; } = "No folder chosen";

    public ObservableCollection<ProbeItemVm> Items { get; } =
        [new ProbeVideoVm("Anvil Mountain"), new ProbePhotoVm("Zachar Bay")];
}

/// <summary>Every VM shown as text overrides ToString with its display text (Ref §2.7 #4).</summary>
public abstract partial class ProbeItemVm
{
    public abstract string Text { get; }

    public override string ToString() => Text;
}

public sealed partial class ProbeVideoVm(string label) : ProbeItemVm
{
    public string Label { get; } = label;

    public override string Text => Label;
}

public sealed partial class ProbePhotoVm(string caption) : ProbeItemVm
{
    public string Caption { get; } = caption;

    public override string Text => Caption;
}
```

`src/UasSort.App/Pages/ProbeTemplateSelector.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Pages;

/// <summary>ItemsView template selector (Ref §2.1 "ItemsView … takes a template selector").</summary>
public sealed partial class ProbeTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Video { get; set; }

    public DataTemplate? Photo { get; set; }

    protected override DataTemplate SelectTemplateCore(object item) => item switch
    {
        ProbeVideoVm => Video ?? throw new InvalidOperationException("Video template not set"),
        ProbePhotoVm => Photo ?? throw new InvalidOperationException("Photo template not set"),
        _ => throw new ArgumentException("Unexpected probe item " + item?.GetType().Name, nameof(item)),
    };

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
```

`src/UasSort.App/Pages/StackProbePage.xaml` (the 30-line probe page, Ref §14 step 1):

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page x:Class="UasSort.App.Pages.StackProbePage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:ctk="using:CommunityToolkit.WinUI.Controls"
      xmlns:local="using:UasSort.App.Pages">
  <Page.Resources>
    <DataTemplate x:Key="VideoTemplate" x:DataType="local:ProbeVideoVm">
      <ItemContainer><TextBlock Text="{x:Bind Label}" Margin="8,4" /></ItemContainer>
    </DataTemplate>
    <DataTemplate x:Key="PhotoTemplate" x:DataType="local:ProbePhotoVm">
      <ItemContainer><TextBlock Text="{x:Bind Caption}" FontStyle="Italic" Margin="8,4" /></ItemContainer>
    </DataTemplate>
    <local:ProbeTemplateSelector x:Key="ProbeSelector" Video="{StaticResource VideoTemplate}" Photo="{StaticResource PhotoTemplate}" />
  </Page.Resources>
  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="340" MinWidth="200" />
      <ColumnDefinition Width="Auto" />
      <ColumnDefinition Width="*" />
    </Grid.ColumnDefinitions>
    <StackPanel Spacing="8" Padding="12">
      <ctk:SettingsCard x:Name="RootCard" Header="Video root" Description="{x:Bind Vm.FolderText, Mode=OneWay}">
        <Button Content="Browse…" Click="OnBrowse" />
      </ctk:SettingsCard>
      <ItemsView ItemsSource="{x:Bind Vm.Items}" ItemTemplate="{StaticResource ProbeSelector}" SelectionMode="Extended" />
      <TextBlock Text="{x:Bind Vm.Status, Mode=OneWay}" TextWrapping="Wrap" />
    </StackPanel>
    <ctk:GridSplitter Grid.Column="1" Width="8" ResizeBehavior="PreviousAndNext" ResizeDirection="Columns" />
    <WebView2 x:Name="Web" Grid.Column="2" />
  </Grid>
</Page>
```

`src/UasSort.App/Pages/StackProbePage.xaml.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Windows.Storage.Pickers;

namespace UasSort.App.Pages;

public sealed partial class StackProbePage : Page
{
    public const string VirtualHost = "map.uas-sort.example";
    public const string ProbeUrl = "https://map.uas-sort.example/probe.html";

    private readonly TaskCompletionSource<string> _mapRoundTrip = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _runtimeVersion = "";

    public StackProbePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public StackProbeVm Vm { get; } = new();

    /// <summary>Completes when the page under the virtual host sent `ready` and answered ping 1 with pong 1.</summary>
    public Task<string> MapRoundTrip => _mapRoundTrip.Task;

    internal string? RootCardHeader => RootCard.Header as string;

    internal void CloseWebView() => Web.Close();

    internal static bool IsVirtualHost(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps
        && string.Equals(u.Host, VirtualHost, StringComparison.OrdinalIgnoreCase);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await InitWebViewAsync();
        }
#pragma warning disable CA1031 // any WebView2 start-up failure is reported, never thrown from an async void handler
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Vm.Status = "WebView2 failed: " + ex.Message;
            _mapRoundTrip.TrySetException(ex);
        }
    }

    private async Task InitWebViewAsync()
    {
        // Explicit user data folder, never next to the exe (Ref §2.7 #6, §9.6).
        var env = await CoreWebView2Environment.CreateWithOptionsAsync("", App.WebView2Folder, new CoreWebView2EnvironmentOptions());
        _runtimeVersion = env.BrowserVersionString;
        await Web.EnsureCoreWebView2Async(env);
        var core = Web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
        core.SetVirtualHostNameToFolderMapping(
            VirtualHost, Path.Join(AppContext.BaseDirectory, "MapAssets"), CoreWebView2HostResourceAccessKind.DenyCors);

        // Registered BEFORE Navigate: `ready` can arrive before NavigationCompleted (Ref §2.7 #6).
        core.WebMessageReceived += OnWebMessage;
        core.DownloadStarting += (_, a) => a.Cancel = true;
        core.NavigationStarting += (_, a) => a.Cancel = !IsVirtualHost(a.Uri);
        core.NewWindowRequested += (_, a) => a.Handled = true;
        core.NavigationCompleted += (_, a) =>
        {
            if (!a.IsSuccess)
            {
                _mapRoundTrip.TrySetException(new InvalidOperationException("navigation failed: " + a.WebErrorStatus));
            }
        };
        core.Navigate(ProbeUrl);
    }

    private void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        using var doc = JsonDocument.Parse(args.WebMessageAsJson);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        switch (type)
        {
            case "ready":
                Vm.Status = "Virtual host ready";
                sender.PostWebMessageAsJson("""{"v":1,"type":"ping","n":1}""");
                break;
            case "pong" when root.TryGetProperty("n", out var n) && n.GetInt32() == 1:
                Vm.Status = "Virtual host ready · pong 1";
                _mapRoundTrip.TrySetResult($"ready and pong from {ProbeUrl}; WebView2 runtime {_runtimeVersion}");
                break;
            case "error":
                var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                Vm.Status = "Page error: " + message;
                _mapRoundTrip.TrySetException(new InvalidOperationException("page error: " + message));
                break;
        }
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        try
        {
            // Owner window from AppWindow.Id (Windows App SDK 2.x pickers take a WindowId).
            var picker = new FolderPicker(App.MainWindow!.AppWindow.Id) { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            var result = await picker.PickSingleFolderAsync();
            if (result is not null)
            {
                Vm.FolderText = result.Path;
            }
        }
        catch (COMException ex)
        {
            Vm.Status = "Folder picker failed: " + ex.Message;
        }
    }
}
```

`src/UasSort.App/MapAssets/probe.html`:

```html
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <title>uas-sort probe</title>
  <script>
    // First lines: forward errors to the host (Ref §2.7 #6). Messages carry v:1 before type (Ref §9.6).
    const post = m => window.chrome.webview.postMessage(Object.assign({ v: 1 }, m));
    window.addEventListener('error', e => post({ type: 'error', base: null, message: String(e.message) }));
    window.addEventListener('unhandledrejection', e => post({ type: 'error', base: null, message: String(e.reason) }));
    window.chrome.webview.addEventListener('message', e => {
      const m = e.data;
      if (m && m.type === 'ping') post({ type: 'pong', n: m.n });
    });
    post({ type: 'ready', maplibre: null, webgl2: !!document.createElement('canvas').getContext('webgl2') });
  </script>
</head>
<body style="margin:0;font:14px system-ui">
  <p style="margin:12px">uas-sort virtual host OK</p>
</body>
</html>
```

Add `<Project Path="src/UasSort.App/UasSort.App.csproj" />` as the first entry of the `/src/` folder of `uas-sort.slnx`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*AppProject_IsUnpackagedSelfContainedNativeAot*"`
Expected: PASS — `failed: 0, succeeded: 1`.

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*LaunchOptionsTests"`
Expected: PASS — `failed: 0, succeeded: 9`.

Run: `dotnet build uas-sort.slnx -tl:off`
Expected: `Build succeeded.`, `0 Warning(s)`, `0 Error(s)` — this restores and builds Sizers 8.3.260402-preview2 for the first time (UNVERIFIED until now). If the toolkit packages fail to restore or build, stop and raise it with the user: the Ref §2.1 fallback ("8.2.251219 with the full `Microsoft.WindowsAppSDK` package, or fixed-ratio panes") conflicts with the Global Constraint that forbids the full package, so the choice is theirs.

Run: `dotnet test --solution uas-sort.slnx`
Expected: PASS — `failed: 0, succeeded: 44`.

Manual launch (Debug), in pwsh:

```powershell
$exe = (Resolve-Path 'src/UasSort.App/bin/Debug/net11.0-windows10.0.26100.0/win-x64/uas-sort.exe').Path
$p = Start-Process $exe -PassThru; Start-Sleep -Seconds 10
(Get-Process -Id $p.Id).MainWindowTitle
```

Expected: the window shows the Mica title bar, a "Video root" settings card with Browse…, the items "Anvil Mountain" (upright) and "Zachar Bay" (italic), a draggable splitter, and the WebView pane reading "uas-sort virtual host OK"; the status line reads `Virtual host ready · pong 1`; the printed title is `uas-sort (stack proof, single instance: AppInstance)`. Click Browse…, pick any folder under Pictures: the card's description shows its path (the picker is owned by the window). Then `Stop-Process -Id $p.Id`.

Single-instance checks (AppInstance, then the forced mutex fallback), in pwsh:

```powershell
$exe = (Resolve-Path 'src/UasSort.App/bin/Debug/net11.0-windows10.0.26100.0/win-x64/uas-sort.exe').Path

$first = Start-Process $exe -PassThru; Start-Sleep -Seconds 8
$second = Start-Process $exe -PassThru; $null = $second.Handle
$exited = $second.WaitForExit(15000)
"AppInstance: title=$((Get-Process -Id $first.Id).MainWindowTitle) secondExited=$exited secondExit=$($second.ExitCode) running=$(@(Get-Process -Name 'uas-sort').Count)"
Stop-Process -Id $first.Id; Start-Sleep -Seconds 2

$first = Start-Process $exe -ArgumentList '--single-instance-mutex' -PassThru; Start-Sleep -Seconds 8
$second = Start-Process $exe -ArgumentList '--single-instance-mutex' -PassThru; $null = $second.Handle
$exited = $second.WaitForExit(15000)
"Mutex: title=$((Get-Process -Id $first.Id).MainWindowTitle) secondExited=$exited secondExit=$($second.ExitCode) running=$(@(Get-Process -Name 'uas-sort').Count)"
Stop-Process -Id $first.Id
```

Expected:
```
AppInstance: title=uas-sort (stack proof, single instance: AppInstance) secondExited=True secondExit=0 running=1
Mutex: title=uas-sort (stack proof, single instance: Mutex) secondExited=True secondExit=0 running=1
```
and, during the first block, the first window comes to the foreground when the second launch starts. If the first line shows `single instance: Mutex`, AppInstance failed in the lean self-contained build: the fallback is working as designed; record it for Task 01.12.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.App uas-sort.slnx tests/UasSort.Core.Tests/Build/BuildConfigTests.cs tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj tests/UasSort.Platform.Tests/Launch/LaunchOptionsTests.cs
git commit -F - <<'EOF'
feat: add WinUI app with stack probe page and single-instance Program.Main

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 01.11: Minimal `--selftest`

**Files:**
- Create: `tools/run-selftest.ps1`, `src/UasSort.App/SelfTest/MinimalSelfTest.cs`, `src/UasSort.App/SelfTest/SelfTestResult.cs`
- Modify: `src/UasSort.App/App.xaml.cs`

**Interfaces:**
- Consumes: `LaunchOptions` (`SelfTest`, `ResultPath`, `Only`), `MainWindow.FirstFrameMs`/`ProbePage`/`ShowOffScreen`, `StackProbePage.MapRoundTrip`/`RootCardHeader`/`CloseWebView` (Task 01.10); `SelfTestSandbox` (Task 01.9); `NamedMutexLock` (Task 01.8); `StackProofJsonContext`, `ProbeOutcome` cases (Task 01.5); embedded `UasSort.App.SelfTest.stack-exif.jpg` (Tasks 01.7, 01.10); Ref §13 minimal selftest list; the Part 13 selftest contract.
- Produces:
  - `uas-sort.exe --selftest --result <path> [--only <check>[,<check>…]]`: skips single-instance registration, launches off-screen, uses a `SelfTestSandbox` for WebView2, runs every check (or only the named ones), writes the result file, exits `0` (no check has `status` `fail`) or `1`.
  - Result file, in the Part 13 contract shape (Part 11 writes the same shape; Part 13's `deploy.ps1` reads `ok`, `firstFrameMs` and the check statuses): `{"ok":true,"firstFrameMs":312,"checks":[{"name":"probePage","status":"pass","detail":"…"},…]}` — no `v`, no `kind`; `status` is `pass` or `fail` here (`notApplicable` exists for Part 11's `placeholderVisibility` only). Check names, in order: `probePage`, `folderPicker`, `webView2`, `metadataExtractor`, `geoTimeZone`, `closedRecordJson`, `namedMutex`, `sandboxDeleted` (a failure outside a check, or an unknown name in `--only`, adds a failed `harness` check).
  - `internal sealed record UasSort.App.SelfTest.SelfTestCheck(string Name, string Status, string Detail)` with `static SelfTestCheck Pass(string name, string detail)`, `Fail(string name, string detail)`, `NotApplicable(string name, string detail)` (`Status` ∈ `pass`, `fail`, `notApplicable`); `internal sealed record SelfTestResult(bool Ok, double FirstFrameMs, IReadOnlyList<SelfTestCheck> Checks)` (`Ok` = no check has `Status == "fail"`); `internal sealed partial class SelfTestJsonContext` (camelCase). Part 11 modifies these files in place (never redeclares them). `internal static class MinimalSelfTest` (Part 01 only; Part 11 Task 11.4 deletes it for `SelfTestRunner`) with `Task RunAndExitAsync(MainWindow, LaunchOptions, SelfTestSandbox)` and `void FailAndExit(LaunchOptions, SelfTestSandbox?, Exception)`.
  - `pwsh -File tools/run-selftest.ps1 -Exe <path> [-Runs 2] [-MaxWarmFirstFrameMs 1000] [-BaselineMs 370]` (defined here): runs `--selftest --result <tmp>` with a 60 s timeout per run; a run fails when its exit code is non-zero, `ok` is false or any check has `status` `fail`; the last run's `firstFrameMs` must be ≤ `-MaxWarmFirstFrameMs` (the 1000 ms hard gate; `0` = not gated, used for Debug builds); after the runs it prints `slowerThanBaseline: True|False` (warm > `-BaselineMs`), which never fails the script.

- [ ] **Step 1: Write the failing test**

`tools/run-selftest.ps1`:

```powershell
#requires -Version 7.0
<#
.SYNOPSIS
  Runs `uas-sort.exe --selftest --result <tmp>` N times (60 s timeout each) and checks every run's exit code and
  result file (Ref §13, Part 13 contract). A run fails on a non-zero exit code, "ok": false, or any check with
  "status": "fail". The last run is the warm run: -MaxWarmFirstFrameMs gates its firstFrameMs (default 1000, the hard
  gate; 0 = not gated). -BaselineMs (default 370, the ReadyToRun baseline) only decides slowerThanBaseline, which is
  printed and never fails the script.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Exe,
    [int]$Runs = 2,
    [double]$MaxWarmFirstFrameMs = 1000,
    [double]$BaselineMs = 370
)

$ErrorActionPreference = 'Stop'
$exePath = (Resolve-Path $Exe).Path
$results = @()
for ($i = 1; $i -le $Runs; $i++) {
    $res = Join-Path $env:TEMP ("uas-sort-selftest-run{0}-{1}.json" -f $i, [guid]::NewGuid().ToString('N'))
    $p = Start-Process -FilePath $exePath -ArgumentList '--selftest', '--result', $res -PassThru
    $null = $p.Handle
    if (-not $p.WaitForExit(60000)) {
        Stop-Process -Id $p.Id -Force
        Write-Error "run ${i}: timed out after 60 s"
        exit 1
    }
    if (-not (Test-Path $res)) {
        Write-Error "run ${i}: no result file (exit $($p.ExitCode))"
        exit 1
    }
    $json = Get-Content $res -Raw | ConvertFrom-Json
    Remove-Item $res
    $failed = (@($json.checks) | Where-Object { $_.status -eq 'fail' } | ForEach-Object { "$($_.name): $($_.detail)" }) -join '; '
    $results += [pscustomobject]@{ Run = $i; Exit = $p.ExitCode; Ok = $json.ok; FirstFrameMs = $json.firstFrameMs; Checks = @($json.checks).Count; Failed = $failed }
}

$results | Format-Table -AutoSize | Out-String | Write-Host
$warm = $results[-1].FirstFrameMs
# Reported, never a failure (decision: 1000 ms is the gate; the 370 ms ReadyToRun baseline is informational).
Write-Host "slowerThanBaseline: $($warm -gt $BaselineMs)"
if ($results | Where-Object { $_.Exit -ne 0 -or -not $_.Ok -or $_.Failed }) {
    Write-Error 'selftest failed'
    exit 1
}
if ($MaxWarmFirstFrameMs -gt 0 -and $warm -gt $MaxWarmFirstFrameMs) {
    Write-Error "warm first frame $warm ms exceeds $MaxWarmFirstFrameMs ms"
    exit 1
}
Write-Host "OK: $Runs run(s), warm first frame $warm ms"
exit 0
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build src/UasSort.App/UasSort.App.csproj -tl:off` then `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-selftest.ps1 -Exe src/UasSort.App/bin/Debug/net11.0-windows10.0.26100.0/win-x64/uas-sort.exe -Runs 1 -MaxWarmFirstFrameMs 0`
Expected: exit 1 with `run 1: timed out after 60 s` (the app parses `--selftest --result` but ignores it so far and stays open). `-MaxWarmFirstFrameMs 0` because a Debug (JIT) build is never held to the 1000 ms gate; only the Native AOT publish is (Task 01.12).

- [ ] **Step 3: Implement**

`src/UasSort.App/SelfTest/SelfTestResult.cs`:

```csharp
using System.Text.Json.Serialization;

namespace UasSort.App.SelfTest;

/// <summary>One selftest check (Part 13 contract): Status is "pass", "fail" or "notApplicable".</summary>
internal sealed record SelfTestCheck(string Name, string Status, string Detail)
{
    public static SelfTestCheck Pass(string name, string detail) => new(name, "pass", detail);

    public static SelfTestCheck Fail(string name, string detail) => new(name, "fail", detail);

    public static SelfTestCheck NotApplicable(string name, string detail) => new(name, "notApplicable", detail);
}

/// <summary>
/// selftest-result.json (Ref §13, Part 13 contract): {"ok", "firstFrameMs", "checks": [{"name", "status", "detail"}]};
/// Ok is true when no check has Status "fail". No version or kind field. Part 11 modifies this file in place.
/// </summary>
internal sealed record SelfTestResult(bool Ok, double FirstFrameMs, IReadOnlyList<SelfTestCheck> Checks);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(SelfTestResult))]
internal sealed partial class SelfTestJsonContext : JsonSerializerContext
{
}
```

`src/UasSort.App/SelfTest/MinimalSelfTest.cs`:

```csharp
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.WinUI.Controls;
using GeoTimeZone;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using UasSort.App.Pages;
using UasSort.Core.Json;
using UasSort.Core.StackProof;
using UasSort.Platform.Stores;
using UasSort.Platform.Win32;
using Windows.UI.Text;

namespace UasSort.App.SelfTest;

/// <summary>
/// The stack-proof step's minimal selftest (Ref §13, §14 step 1): probe page renders, WebView2 reaches the virtual
/// host, MetadataExtractor Stream read of the embedded stack-exif.jpg, GeoTimeZone lookup, closed-record JSON
/// round-trip; plus the named mutex and the sandbox deletion. With --only, only the named checks run (the page still
/// loads and the sandbox is still deleted). Part 11 Task 11.4 deletes it for SelfTestRunner, keeping the command
/// line and the result shape.
/// </summary>
internal static class MinimalSelfTest
{
    internal const string StackExifResource = "UasSort.App.SelfTest.stack-exif.jpg";

    /// <summary>Every check, in run order; --only selects by these exact (case-sensitive) names.</summary>
    private static readonly string[] CheckNames =
        ["probePage", "folderPicker", "webView2", "metadataExtractor", "geoTimeZone", "closedRecordJson", "namedMutex", "sandboxDeleted"];

    public static async Task RunAndExitAsync(MainWindow window, LaunchOptions options, SelfTestSandbox sandbox)
    {
        var checks = new List<SelfTestCheck>();
        double firstFrameMs = -1;
        bool Selected(string name) => options.Only is null || options.Only.Contains(name);
        try
        {
            var unknown = (options.Only ?? Enumerable.Empty<string>())
                .Where(n => !CheckNames.Contains(n, StringComparer.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList();
            if (unknown.Count > 0)
            {
                checks.Add(SelfTestCheck.Fail("harness", "unknown check name(s) in --only: " + string.Join(", ", unknown)));
            }

            firstFrameMs = await window.FirstFrameMs.WaitAsync(TimeSpan.FromSeconds(20));
            var page = await WaitForPageAsync(window);
            if (Selected("probePage"))
            {
                checks.Add(await RunAsync("probePage", () => CheckProbePageAsync(page)));
            }

            if (Selected("folderPicker"))
            {
                checks.Add(Run("folderPicker", () => CheckFolderPicker(window)));
            }

            if (Selected("webView2"))
            {
                checks.Add(await RunAsync("webView2", () => page.MapRoundTrip.WaitAsync(TimeSpan.FromSeconds(20))));
            }

            if (Selected("metadataExtractor"))
            {
                checks.Add(Run("metadataExtractor", CheckMetadataExtractor));
            }

            if (Selected("geoTimeZone"))
            {
                checks.Add(Run("geoTimeZone", CheckGeoTimeZone));
            }

            if (Selected("closedRecordJson"))
            {
                checks.Add(Run("closedRecordJson", CheckClosedRecordJson));
            }

            if (Selected("namedMutex"))
            {
                checks.Add(Run("namedMutex", CheckNamedMutex));
            }

            page.CloseWebView();
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            var deleted = sandbox.TryDelete();
            if (Selected("sandboxDeleted"))
            {
                checks.Add(deleted
                    ? SelfTestCheck.Pass("sandboxDeleted", sandbox.Root)
                    : SelfTestCheck.Fail("sandboxDeleted", "could not delete " + sandbox.Root));
            }
        }
#pragma warning disable CA1031 // selftest: every failure becomes a failed check in the result file
        catch (Exception ex)
#pragma warning restore CA1031
        {
            checks.Add(SelfTestCheck.Fail("harness", Describe(ex)));
        }

        Finish(options, checks, firstFrameMs);
    }

    internal static void FailAndExit(LaunchOptions options, SelfTestSandbox? sandbox, Exception ex)
    {
        sandbox?.TryDelete();
        Finish(options, [SelfTestCheck.Fail("harness", "unhandled: " + Describe(ex))], -1);
    }

    private static void Finish(LaunchOptions options, List<SelfTestCheck> checks, double firstFrameMs)
    {
        var ok = checks.TrueForAll(c => c.Status != "fail");
        var result = new SelfTestResult(ok, Math.Round(firstFrameMs), checks);
        try
        {
            SelfTestSandbox.WriteResult(
                options.ResultPath, JsonSerializer.SerializeToUtf8Bytes(result, SelfTestJsonContext.Default.SelfTestResult));
        }
#pragma warning disable CA1031 // a result that cannot be written is a failed selftest, reported by the exit code
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Trace.WriteLine("could not write the selftest result: " + ex);
            ok = false;
        }

        Environment.Exit(ok ? 0 : 1);
    }

    private static async Task<StackProbePage> WaitForPageAsync(MainWindow window)
    {
        for (var i = 0; i < 100; i++)
        {
            if (window.ProbePage is { IsLoaded: true } page)
            {
                return page;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("StackProbePage did not load within 10 s");
    }

    private static async Task<string> CheckProbePageAsync(StackProbePage page)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var texts = Descendants(page).OfType<TextBlock>().ToList();
            var video = texts.Find(t => t.Text == "Anvil Mountain");
            var photo = texts.Find(t => t.Text == "Zachar Bay");
            var splitter = Descendants(page).OfType<GridSplitter>().FirstOrDefault();
            if (video is not null && photo is not null && splitter is not null)
            {
                Require(video.FontStyle == FontStyle.Normal && photo.FontStyle == FontStyle.Italic,
                    "the template selector chose the wrong template");
                Require(page.RootCardHeader == "Video root", "SettingsCard header was " + page.RootCardHeader);
                return "ItemsView realised both templates (x:Bind text), SettingsCard and GridSplitter rendered";
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("probe page items, SettingsCard or GridSplitter not realised within 5 s");
    }

    private static string CheckFolderPicker(MainWindow window)
    {
        var picker = new FolderPicker(window.AppWindow.Id) { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        Require(picker.SuggestedStartLocation == PickerLocationId.PicturesLibrary, "FolderPicker lost its start location");
        return string.Create(CultureInfo.InvariantCulture, $"constructed with owner window {window.AppWindow.Id.Value} (not shown)");
    }

    private static string CheckMetadataExtractor()
    {
        using var stream = typeof(MinimalSelfTest).Assembly.GetManifestResourceStream(StackExifResource)
            ?? throw new InvalidOperationException("missing embedded resource " + StackExifResource);
        var directories = JpegMetadataReader.ReadMetadata(stream);
        var dto = directories.OfType<ExifSubIfdDirectory>()
            .Select(d => d.GetString(ExifDirectoryBase.TagDateTimeOriginal))
            .FirstOrDefault(s => s is not null);
        Require(dto == "2026:09:27 14:01:27", $"DateTimeOriginal was '{dto}'");
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault() ?? throw new InvalidOperationException("no GPS directory");
        Require(gps.TryGetGeoLocation(out var location), "GPS directory has no location");
        Require(Math.Abs(location.Latitude - 57.5368) < 1e-6 && Math.Abs(location.Longitude + 153.7484) < 1e-6,
            string.Create(CultureInfo.InvariantCulture, $"GPS was {location.Latitude},{location.Longitude}"));
        return string.Create(CultureInfo.InvariantCulture, $"DTO {dto}; GPS {location.Latitude:F4},{location.Longitude:F4}");
    }

    private static string CheckGeoTimeZone()
    {
        var tz = TimeZoneLookup.GetTimeZone(57.5368, -153.7484).Result;
        Require(tz == "America/Anchorage", "GeoTimeZone returned " + tz);
        var info = TimeZoneInfo.FindSystemTimeZoneById(tz);
        Require(info.BaseUtcOffset == TimeSpan.FromHours(-9), "ICU base offset was " + info.BaseUtcOffset);
        return "Zachar Bay -> America/Anchorage (ICU base offset -09:00)";
    }

    private static string CheckClosedRecordJson()
    {
        List<ProbeOutcome> outcomes =
            [new ProbeCopied("2026/2026-09/DJI_0001.MP4", 1024), new ProbeSkipped("dup"), new ProbeConflict("DJI_0002.MP4", 7)];
        var json = JsonSerializer.Serialize(outcomes, StackProofJsonContext.Default.ListProbeOutcome);
        var back = JsonSerializer.Deserialize(json, StackProofJsonContext.Default.ListProbeOutcome);
        Require(back is not null && back.SequenceEqual(outcomes), "round trip changed the values: " + json);
        Require(json.StartsWith("""[{"t":"copied",""", StringComparison.Ordinal), "discriminator missing: " + json);
        return json;
    }

    private static string CheckNamedMutex()
    {
        var name = @"Local\uas-sort-selftest-" + Guid.NewGuid().ToString("N");
        using var first = NamedMutexLock.TryAcquire(name) ?? throw new InvalidOperationException("first acquire failed");
        using var second = NamedMutexLock.TryAcquire(name);
        Require(second is null, "a second acquire succeeded while the first was held");
        return "held; second acquire refused";
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                pending.Push(VisualTreeHelper.GetChild(node, i));
            }
        }
    }

    private static SelfTestCheck Run(string name, Func<string> check)
    {
        try
        {
            return SelfTestCheck.Pass(name, check());
        }
#pragma warning disable CA1031 // selftest: every failure becomes a failed check
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return SelfTestCheck.Fail(name, Describe(ex));
        }
    }

    private static async Task<SelfTestCheck> RunAsync(string name, Func<Task<string>> check)
    {
        try
        {
            return SelfTestCheck.Pass(name, await check());
        }
#pragma warning disable CA1031 // selftest: every failure becomes a failed check
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return SelfTestCheck.Fail(name, Describe(ex));
        }
    }

    private static string Describe(Exception ex) => ex.GetType().Name + ": " + ex.Message;

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
```

Replace `src/UasSort.App/App.xaml.cs`:

```csharp
using System.Diagnostics;
using Microsoft.UI.Xaml;
using UasSort.App.SelfTest;
using UasSort.Platform.Stores;

namespace UasSort.App;

public partial class App : Application
{
    private readonly LaunchOptions _options;
    private readonly SingleInstanceGate? _gate;
    private readonly SelfTestSandbox? _sandbox;

    internal App(LaunchOptions options, SingleInstanceGate? gate)
    {
        _options = options;
        _gate = gate;
        _sandbox = options.SelfTest ? SelfTestSandbox.Create(Path.GetTempPath()) : null;
        WebView2Folder = _sandbox?.WebView2Folder
            ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uas-sort", "WebView2");
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    internal static MainWindow? MainWindow { get; private set; }

    internal static string WebView2Folder { get; private set; } = "";

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow(_options.ProcessStartUtc, _gate?.Mechanism ?? "skipped (selftest)");
        MainWindow = window;
        if (_gate?.Key is { } key)
        {
            key.Activated += (_, _) => window.DispatcherQueue.TryEnqueue(window.BringToFront);
        }

        if (_options.SelfTest)
        {
            window.ShowOffScreen();
            _ = MinimalSelfTest.RunAndExitAsync(window, _options, _sandbox!);
        }
        else
        {
            window.Activate();
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Trace.WriteLine("UNHANDLED " + e.Exception);
        if (_options.SelfTest)
        {
            e.Handled = true;
            MinimalSelfTest.FailAndExit(_options, _sandbox, e.Exception);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build src/UasSort.App/UasSort.App.csproj -tl:off`
Expected: `0 Warning(s)`, `0 Error(s)`.

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-selftest.ps1 -Exe src/UasSort.App/bin/Debug/net11.0-windows10.0.26100.0/win-x64/uas-sort.exe -Runs 1 -MaxWarmFirstFrameMs 0`
Expected: a table row `1  0  True  <ms>  8` with an empty `Failed` column, then `slowerThanBaseline: True` or `False` (informational for a Debug build), then `OK: 1 run(s), warm first frame … ms`, exit 0.

Run: `Get-ChildItem $env:TEMP -Directory -Filter 'uas-sort-selftest-*'`
Expected: no output (the sandbox was deleted).

`--only` and the result shape, in pwsh:

```powershell
$exe = (Resolve-Path 'src/UasSort.App/bin/Debug/net11.0-windows10.0.26100.0/win-x64/uas-sort.exe').Path
$res = Join-Path $env:TEMP ('uas-sort-selftest-only-' + [guid]::NewGuid().ToString('N') + '.json')
$p = Start-Process $exe -ArgumentList '--selftest', '--result', $res, '--only', 'geoTimeZone,closedRecordJson' -PassThru
$null = $p.Handle; $null = $p.WaitForExit(60000)
"exit=$($p.ExitCode)"; Get-Content $res -Raw; Remove-Item $res
```

Expected: `exit=0`, then the JSON with exactly the top-level properties `ok` (`true`), `firstFrameMs` and `checks` (no `v`, no `kind`), and exactly two checks, `geoTimeZone` then `closedRecordJson`, each with `"status": "pass"` and a `detail`.

Run the same block with `'--only', 'geoTimezone'` (wrong case).
Expected: `exit=1` and a single check `harness` with `"status": "fail"` and `unknown check name(s) in --only: geoTimezone`.

Negative check: rename `src/UasSort.App/bin/Debug/net11.0-windows10.0.26100.0/win-x64/MapAssets/probe.html` to `probe.off`, run the same `run-selftest.ps1` command.
Expected: exit 1; `Failed` shows `webView2: …` (navigation failed or a timeout); every other check has `status` `pass`. Rename it back (or rebuild).

Run: `dotnet test --solution uas-sort.slnx`
Expected: PASS — `failed: 0, succeeded: 44`.

- [ ] **Step 5: Commit**

```bash
git add tools/run-selftest.ps1 src/UasSort.App/SelfTest/MinimalSelfTest.cs src/UasSort.App/SelfTest/SelfTestResult.cs src/UasSort.App/App.xaml.cs
git commit -F - <<'EOF'
feat: add the stack-proof --selftest and the selftest runner script

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 01.12: Native AOT publish, measured against the ReadyToRun baseline

**Files:**
- Create: `docs/research/10-stack-proof.md`
- Modify (only if Step 3 finds IL2104/IL3053 raised solely by the rooted `MetadataExtractor`/`XmpCore`): `src/UasSort.App/UasSort.App.csproj`

**Interfaces:**
- Consumes: everything above; `tools/run-selftest.ps1`; Ref §2.1 Packaging row, §2.2 "Native AOT rather than trimmed + ReadyToRun", §2.5 Release rules, §14 step 1 and "If Native AOT fails a concrete check".
- Produces: `docs/research/10-stack-proof.md` (defined here): the measured record every later part and the user can rely on — SDK, first restore, effective `AnalysisLevel`, package status, analyzer probe, exhaustiveness, AppInstance/mutex, AOT publish warnings, size and file count, cold and warm `firstFrameMs` (gate ≤ 1000 ms; ReadyToRun baseline 370 ms; `slowerThanBaseline`), WebView2 runtime, the VS Code check. A green gate here is the precondition for Part 02.

**Stop rule (Ref §1.1, §14):** a build or AOT warning, a selftest failure (non-zero exit, `ok` false or any check with `status` `fail`), or a warm first frame over 1000 ms is a failed concrete check. A warm first frame slower than the 370 ms ReadyToRun baseline but ≤ 1000 ms is **not** a failure: `run-selftest.ps1` prints `slowerThanBaseline: True`; record it in `docs/research/10-stack-proof.md`, report it to the user in the completion summary, and continue with Part 02. On a failed concrete check: do not change `PublishAot`, do not add `PublishReadyToRun`, and do not start Part 02. Diagnose only by hand with `dotnet publish src/UasSort.App/UasSort.App.csproj -c Release -r win-x64 -p:PublishAot=false -p:PublishTrimmed=true -p:PublishReadyToRun=true -o artifacts/stack-proof/r2r-diagnosis` and the same selftest, to tell whether the failure is AOT-specific; write both results into `docs/research/10-stack-proof.md`, commit it, and raise it with the user, waiting for their decision.

- [ ] **Step 1: Write the failing check**

The gate is the selftest runner against the AOT publish folder, with the 1000 ms hard gate as the warm limit and the 370 ms ReadyToRun baseline reported as `slowerThanBaseline`:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-selftest.ps1 -Exe artifacts/stack-proof/win-x64/uas-sort.exe -Runs 2 -MaxWarmFirstFrameMs 1000 -BaselineMs 370
```

- [ ] **Step 2: Run it to verify it fails**

Run the command above.
Expected: FAIL — `Resolve-Path: Cannot find path '…\artifacts\stack-proof\win-x64\uas-sort.exe' because it does not exist.`

- [ ] **Step 3: Implement (publish)**

Run: `dotnet publish src/UasSort.App/UasSort.App.csproj -c Release -r win-x64 -o artifacts/stack-proof/win-x64 -tl:off`
Expected: exit 0, `0 Warning(s)`, `0 Error(s)` with `TreatWarningsAsErrors` on (from `Directory.Build.props`), and an ILC/link step in the log.

- If it fails with `vswhere.exe failed to locate Visual Studio with Microsoft.VisualStudio.Component.VC.Tools.x86.x64`: the C++ workload prerequisite is missing — stop and ask the user to run the Global Constraints install command.
- If the only warnings are IL2104 and/or IL3053 **and each names `MetadataExtractor` or `XmpCore`**, add to the App csproj's first `PropertyGroup`, then publish again:

```xml
    <!-- Ref §2.5: IL2104/IL3053 raised only by the rooted MetadataExtractor/XmpCore (checked in the stack-proof
         publish). Every other trim or AOT warning stays an error. -->
    <NoWarn>$(NoWarn);IL2104;IL3053</NoWarn>
```

- Any other warning or error (CsWinRT, WebView2, toolkit, WinUI, our code): apply the stop rule.

Confirm the output is native:

```powershell
Test-Path artifacts/stack-proof/win-x64/uas-sort.dll
Test-Path artifacts/stack-proof/win-x64/MapAssets/probe.html
$files = Get-ChildItem artifacts/stack-proof/win-x64 -Recurse -File
'{0:N1} MB, {1} files' -f (($files | Measure-Object Length -Sum).Sum / 1MB), $files.Count
```

Expected: `False` (no managed app assembly: the exe is native), `True`, and the size line (record it; the ReadyToRun baseline is 87–97 MB).

- [ ] **Step 4: Run the gate to verify it passes**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-selftest.ps1 -Exe artifacts/stack-proof/win-x64/uas-sort.exe -Runs 2 -MaxWarmFirstFrameMs 1000 -BaselineMs 370`
Expected: two rows, both `Exit 0`, `Ok True`, `Checks 8`, empty `Failed`; then `slowerThanBaseline: True` or `False` (record it; `True` is reported to the user, never a failure); then `OK: 2 run(s), warm first frame … ms` with the warm value ≤ 1000; exit 0. (Run 1 is the cold run, run 2 the warm run.)

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -CheckBannedApi`
Expected: `OK: 73 banned entries, 73 probe calls, 73 RS0030 (one per call)`.

Run: `dotnet test --solution uas-sort.slnx`
Expected: PASS — `failed: 0, succeeded: 44`.

Ask the user (it needs a person at the editor, Ref §14 step 1): "Open `C:\dev\uas-sort` in VS Code with the C# Dev Kit, open `src/UasSort.Core/StackProof/StackProofTypes.cs` and `tests/UasSort.Review.Tests/StackProof/CrossAssemblyExhaustivenessTests.cs`: does the Problems panel show any error for `union` or `closed`?" Record the answer; false editor errors do not block the build.

Write `docs/research/10-stack-proof.md` with the measured values:

```markdown
# Stack proof (Part 01, Ref §14 step 1): measured results

Date: (the day of the run). Machine: the development PC, win-x64.

| Check | Result |
|---|---|
| SDK (`dotnet --version`) | (value) |
| First restore (Task 01.1) | (minutes); NU1603/NU1507: none |
| `AnalysisLevel` in effect | `11.0-recommended` (`analysislevel_11_recommended.globalconfig` listed), or the fallback and why |
| Toolkit 8.3.260402-preview2 (Sizers, SettingsControls) | restored and rendered (GridSplitter, SettingsCard in `probePage`) |
| Banned-API probe | `OK: 73 banned entries, 73 probe calls, 73 RS0030 (one per call)` |
| Cross-assembly exhaustiveness | green; removing an arm gives CS8509 (closed and union) |
| AppInstance (Debug) | second launch exits 0, first window comes forward; title mechanism (value) |
| Named-mutex fallback | `--single-instance-mutex`: second launch exits 0 |
| Native AOT publish | exit 0, 0 warnings with TreatWarningsAsErrors; NoWarn IL2104/IL3053 added: yes/no (which assembly) |
| AOT size | (MB) / (files) — ReadyToRun baseline 87–97 MB |
| `--selftest` run 1 (cold) `firstFrameMs` | (value) |
| `--selftest` run 2 (warm) `firstFrameMs` | (value) — gate ≤ 1000 ms; ReadyToRun baseline 370 ms; slowerThanBaseline: (True/False) |
| WebView2 runtime | (from the `webView2` check detail) |
| MetadataExtractor under trimming | `metadataExtractor` check ok in the AOT build |
| VS Code C# union/closed | (the user's answer) |
```

Replace every parenthesised cell with the measured value before committing. If `slowerThanBaseline` is `True`, state the warm value and the 370 ms baseline in the completion summary to the user; it does not stop Part 02.

- [ ] **Step 5: Commit**

```bash
git add docs/research/10-stack-proof.md src/UasSort.App/UasSort.App.csproj
git commit -F - <<'EOF'
docs: record the Native AOT stack-proof results

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

## Part 01 — Produces (summary)

- **Repo files:** `uas-sort.slnx` (src: App, Cli, Core, Platform, Review; tests: Core.Tests, Platform.Tests, Review.Tests, Testing; never the probe), `global.json`, `nuget.config`, `Directory.Build.props` (props of Ref §2.5; `RepoRoot`; `UasSortBannedList` = `main`/`platform`; transitive pinning), `Directory.Packages.props` (the 14 exact pins), `.editorconfig`, `BannedSymbols.txt` (exactly the 73 Ref §2.4 IDs), `src/UasSort.Platform/BannedSymbols.Platform.txt` (member-level list; allowed calls need `#pragma warning disable RS0030 // IO layer: <why>`).
- **Projects:** `UasSort.Core` (net11.0; MetadataExtractor, GeoTimeZone, System.IO.Hashing; no project refs), `UasSort.Review` (net11.0; Mvvm; → Core), `UasSort.Platform` (windows TFM; `AllowUnsafeBlocks`; → Core), `UasSort.App` (`uas-sort.exe`, WinUI, Native AOT in Release; → Review, Platform), `UasSort.Cli` (`uas-sort-cli`, `UasSort.Cli.Program.Main` stub returning 2; → Core, Platform), `UasSort.Testing`, `UasSort.Core.Tests`, `UasSort.Review.Tests`, `UasSort.Platform.Tests`, `UasSort.BannedApi.Probe` (outside the solution).
- **Tools:** `tools/r.sh dotnet|pwsh …`; `tools/build.ps1 [-CheckBannedApi]` (probe markers `// probe: <id>`); `tools/run-selftest.ps1 -Exe <path> [-Runs 2] [-MaxWarmFirstFrameMs 1000] [-BaselineMs 370]` (runs `--selftest --result <tmp>`; fails on a non-zero exit, `ok` false or any check `status` `fail`, or a warm first frame over the gate; prints `slowerThanBaseline: True|False` without failing); `tools/fixtures/make-selftest-assets.cs` (`SelfTestAssets.All()` = `stack-exif.jpg`; Part 11 Task 11.9 adds `selftest.dng`, `ledger-v1.jsonl`, `selftest-0001.mp4`, `selftest-0002.mp4`, `selftest-0003.mp4` in place).
- **Testing:** `UasSort.Testing.RepoPaths` (`Root`, `Of`, `EnumerateFiles`, `Relative`); `UasSort.Testing.TestTempDir` (`FullPath`, `Combine`, `Dispose`). `UasSort.Testing` and every test csproj reference `Microsoft.Extensions.TimeProvider.Testing`; test csprojs carry `<Using Include="Xunit" />`. Platform.Tests links `src/UasSort.App/LaunchOptions.cs` (`LaunchOptionsTests`).
- **Core:** `UasSort.Core.StackProof.ProbeOutcome` (closed; `ProbeCopied`, `ProbeSkipped`, `ProbeConflict`; discriminator `t`), `ProbeFix`, `ProbeNoFix`, `union ProbeGps`; `UasSort.Core.Json.StackProofJsonContext` (`Default.ProbeOutcome`, `Default.ListProbeOutcome`).
- **Platform:** `UasSort.Platform.Win32.NamedMutexLock` (`TryAcquire(string)`, `Name`, `Dispose`), `SingleInstance.AppInstanceKey`/`MutexName`, `ForegroundWindow.AllowSetForeground(uint)`/`BringToFront(nint)`, `PlaceholderMode.ExposePlaceholders()`/`PhcmExposePlaceholders`, internal `NativeMethods` — the only definitions of these helpers (Parts 09, 11, 12 reuse them); `UasSort.Platform.Stores.SelfTestSandbox`, a `public sealed partial class` (`Create(string tempRoot)`, `Root`, `WebView2Folder`, `WriteResult(string, ReadOnlySpan<byte>)`, `TryDelete`, `Dispose`, `FolderPrefix`).
- **App:** `Program.Main` (ComWrappers → `PlaceholderMode.ExposePlaceholders()` → `LaunchOptions.Parse` (rejected command line: exit 2) → `SingleInstanceGate.Claim(options.ForceMutex)` unless `SelfTest` → `Application.Start`); `LaunchOptions(bool SelfTest, string ResultPath, IReadOnlySet<string>? Only, bool ForceMutex, DateTime ProcessStartUtc)` (`SelfTestFlag` `--selftest`, `ResultFlag` `--result <path>`, `OnlyFlag` `--only <check>[,<check>…]` (case-sensitive), `ForceMutexFlag` `--single-instance-mutex`, `DefaultResultFileName` in `%TEMP%`; any other argument rejected); `SingleInstanceGate` (`Claim(bool forceMutex)`, `IsMain`, `Mechanism`, `Key`); `App.MainWindow`, `App.WebView2Folder`; `MainWindow` (`FirstFrameMs`, `ProbePage`, `ShowOffScreen`, `BringToFront`); `Pages.StackProbePage` (`VirtualHost` = `map.uas-sort.example`, `ProbeUrl`, `MapRoundTrip`, `CloseWebView`, `IsVirtualHost`), probe VMs and `ProbeTemplateSelector`; `MapAssets/probe.html`; embedded resources `UasSort.App.SelfTest.<file>` from `SelfTest\*.jpg;*.dng;*.jsonl`; `SelfTest/stack-exif.jpg`; `SelfTest.MinimalSelfTest` (Part 01 only; honours `Only`); `SelfTestCheck(string Name, string Status, string Detail)` with `Pass`/`Fail`/`NotApplicable`; `SelfTestResult(bool Ok, double FirstFrameMs, IReadOnlyList<SelfTestCheck> Checks)` (`Ok` = no check has `Status == "fail"`); `SelfTestJsonContext` (camelCase).
- **Selftest contract (Part 13):** `uas-sort.exe --selftest --result <path> [--only <check>[,<check>…]]` → exit 0/1 and `{"ok":true,"firstFrameMs":312,"checks":[{"name":"probePage","status":"pass","detail":"…"},…]}` (no `v`, no `kind`; `status` ∈ `pass`, `fail`, `notApplicable`). There is no `--selftest-result`.
- **Extended in place later (never redeclared):** Part 11 extends `SelfTestSandbox`, `LaunchOptions`, `SingleInstanceGate`, `SelfTestCheck`/`SelfTestResult`/`SelfTestJsonContext` and `tools/fixtures/make-selftest-assets.cs` in place.
- **Gate:** warm first frame ≤ 1000 ms is the hard gate; slower than the 370 ms ReadyToRun baseline sets `slowerThanBaseline` (recorded and reported to the user, never a failure).
- **Record:** `docs/research/10-stack-proof.md` with the AOT size, cold/warm first frame (warm row: gate ≤ 1000 ms; ReadyToRun baseline 370 ms; slowerThanBaseline) and every settled UNVERIFIED item.
