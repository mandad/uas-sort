# Part 09 — Platform implementations

**Goal:** build `UasSort.Platform`, the only code that touches disk, Win32 or the shell, as Windows implementations of every Core port (Ref §4.1): the volume provider with bus facts, the directory lister, the read-only card reader and its factory, the card eraser and its factory with a Win32 volume check, `GuardedFileOps` over `WindowsFileOps`, the `.uas-sort` ledger store (status check, load, folder create and pin, own-file append with torn-tail repair and a local mirror, snapshots, copy into a new root), the settings, draft and report stores, placeholder and sync-root detection, canonical paths, keep-awake, the offload lock (over Part 01's `NamedMutexLock`), eject, the shell launcher, the file log and the app assets. Every open, create, attribute change, delete and rename first asks Core's `IoGuardPolicy.Check`. The Windows integration tests of Ref §13 land here. Part 01 owns the single-instance and placeholder-mode helpers (`NamedMutexLock`, `SingleInstance`, `ForegroundWindow`, `PlaceholderMode`, `SelfTestSandbox`) and the `BannedSymbols.txt` content test; this part reuses them and redefines none.

**Read `00-interfaces.md` first:** it wins over this part's text on names, namespaces, signatures and ownership.

**Ref sections:** Ref §4.1 (every port), §4.2 (Platform row), §4.3 (guard, placeholders, sync roots, canonical paths, ledger exemption, `EnsureFolder` and pinning), §5 (listing options), §10.3 (Win32 details of the per-file protocol and the post-run flush), §10.6 (the cleanup volume check, `WindowsCardEraser` and its factory), §11 (stores, backup, logs), §12 (rows for the ledger folder, settings recovery, second instance, eject), §13 (Windows integration; the BannedSymbols content test is Part 01's), §14 step 9. Main spec §3.3, §7.2, §7.5, §8.

**Depends on:** Parts 01–08 — Part 01 (solution, `Directory.Build.props` with `UasSortBannedList=platform`, `Directory.Packages.props`, `BannedSymbols.txt` and its content test, the Platform and Platform.Tests projects, the `UasSort.Platform.Win32` helpers `NativeMethods`, `NamedMutexLock`, `SingleInstance`, `ForegroundWindow`, `PlaceholderMode`, `SelfTestSandbox` in `UasSort.Platform.Stores`, and `UasSort.Testing.RepoPaths`), Part 02 (the model of Ref §3, the ports of Ref §4.1, `IoGuardPolicy`, `GuardContext`, `IoOp`, `GuardDecision`, `UnsafeIoException`, all in `UasSort.Core`; `CoreJsonContext`, `LedgerJsonContext`; the `GlobalUsings.Core.cs` already present in `src/UasSort.Platform` and `tests/UasSort.Platform.Tests`), Part 05 (`LedgerFolderStatusBuilder`, `LedgerLoader`, `LedgerReader`, `LedgerCodec`; `SettingsDefaults`, `SettingsCodec`, `SettingsLoadPolicy`, `SettingsRecovery`), Part 07 (report types, `OffloadCompiler.NewFolderDirs`), Part 08 (`CleanupPlanFixtures` in `UasSort.Testing`).

## Cross-part contract consumed here

Platform code sees every Core namespace through Part 02's fixed `GlobalUsings.Core.cs` (already in `src/UasSort.Platform` and `tests/UasSort.Platform.Tests`; this part does not touch it) and its own namespaces through the two project-specific `GlobalUsings.cs` files of Task 09.1, whose content is exactly the registry's (`00-interfaces.md`, "Namespaces and GlobalUsings"). Files that need `System.Runtime.InteropServices` or `Microsoft.Win32.SafeHandles` import them with a per-file `using`. The members below are what this part calls beyond the exact Ref §3/§4 signatures:

| From | Member (exact signature) | Used by |
|---|---|---|
| Part 01 | `public sealed class NamedMutexLock : IDisposable { static NamedMutexLock? TryAcquire(string name); string Name; void Dispose(); }` in `UasSort.Platform.Win32` (existence-based, not thread-affine) | `OffloadLock` (Task 09.12) |
| Part 01 | `PlaceholderMode.ExposePlaceholders()`, `SingleInstance`, `ForegroundWindow` in `UasSort.Platform.Win32` — the only definitions; `Program.Main` (Part 11) and the CLI (Part 12) call them directly | not called here |
| Part 01 (Testing) | `UasSort.Testing.RepoPaths` — `Root`, `Of(string relativePath)`, `EnumerateFiles(string searchPattern, params string[] topFolders)` (skips `bin`, `obj`, `.git`, `artifacts`, `node_modules`, `TestResults`), `Relative(string fullPath)` | source guards (Task 09.13) |
| Part 02 | `public class UnsafeIoException : Exception` in `UasSort.Core` (not an `IOException`; ctors `()`, `(string message)`, `(string message, Exception inner)`) | every guarded call |
| Part 02 | `public static GuardDecision IoGuardPolicy.Check(IoOp op, string canonicalPath, uint? attributes, GuardContext ctx)` in `UasSort.Core` (Ref §4.2) | `IoGate` (Task 09.3) |
| Part 02 | `CoreJsonContext.Default.Draft`, `.OffloadReport`, `.CleanupReport` in `UasSort.Core.Json` (ledger lines never use a JSON context directly here; they go through `LedgerCodec`, which uses `LedgerJsonContext.Default.LedgerRecord`) | draft and report stores |
| Part 05 | `LedgerFolderFacts(bool VideoRootExists, FsEntry? Folder, ListingResult? TopLevel, bool InSyncRoot, bool Writable)` and `static LedgerFolderStatus LedgerFolderStatusBuilder.Build(string videoRoot, string machine, LedgerFolderFacts facts)` in `UasSort.Core.Ledger` (every state, most severe first) | `LedgerStore.Check` |
| Part 05 | `static LedgerSnapshot LedgerLoader.Load(LedgerFolderStatus status, Func<string, Stream> openRead)` in `UasSort.Core.Ledger` (opens nothing for Missing, VideoRootMissing, CloudOnly) | `LedgerStore.Load` |
| Part 05 | `LedgerFileText(string FullPath, string Text)`, `static LedgerSnapshot LedgerReader.Read(IReadOnlyList<LedgerFileText> sources, LedgerFolderStatus status)`, `static LedgerFolderStatus LedgerSnapshots.Detached(string folder, IEnumerable<string> files)` in `UasSort.Core.Ledger` | `LedgerStore.LoadFromBackup` |
| Part 05 | `const int LedgerCodec.Version = 1`, `static string LedgerCodec.Serialize(LedgerRecord record)` (one line, no `\n`), `static LedgerRecord? LedgerCodec.TryParse(string line, out string? error)` in `UasSort.Core.Ledger` | `LedgerWriter`, `CopyInto`, test helper `LedgerRecords` |
| Part 05 | `static Settings SettingsDefaults.Derive(string picturesFolder)`, `static string SettingsCodec.Serialize(Settings)`, `static SettingsParse SettingsCodec.Parse(string json)`, `SettingsLoadDecision(SettingsLoad Load, string? MoveCorruptTo)`, `static SettingsLoadDecision SettingsLoadPolicy.Decide(string settingsPath, string? fileText, bool readOnly, Settings derivedDefaults, DateTime nowUtc, Func<RunRoots?> rootsFromLastRun)`, `static RunRoots? SettingsRecovery.RootsFromLastRun(IReadOnlyList<LedgerFileText> mirrors)` in `UasSort.Core.Config` (folder `src/UasSort.Core/Settings/`) | `SettingsStore`, `TestEnv` |
| Part 07 | `static ImmutableHashSet<string> OffloadCompiler.NewFolderDirs(OffloadBatch batch, string videoRoot)` in `UasSort.Core.Offload` (the run's `YYYY`, `YYYY-MM`, event and set folders); `CommitEnvironment.FileOpsForRun` hands it to the composition root | `GuardedFileOps` (taken verbatim, decision 27) |
| Part 08 (Testing) | `public static ConfirmedCleanupPlan CleanupPlanFixtures.Confirmed(string cardRoot, CardIdentity card, IReadOnlyList<string> fileRelPaths, IReadOnlyList<string> setFolderRelDirs, TimeProvider clock)` in `UasSort.Testing`, built through the internal `CleanupPlan`/`ConfirmedCleanupPlan` constructors (Core's `InternalsVisibleTo("UasSort.Testing")`) | eraser tests (Task 09.11) |

`FsEntry.RelPath` produced here uses the Windows separator (`DCIM\DJI_001\x.MP4`), exactly as `Path.GetRelativePath` returns it; Core converts to the `/` form of `ItemId` where it builds ids.

**Test commands used in this part** (run natively on Windows from the repo root `C:\dev\uas-sort` in PowerShell 7 or Claude Code's Bash tool; only if driving the build from WSL (optional), run them through `tools/r.sh`, then `dotnet build-server shutdown`):

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-method "*Name*"
dotnet test --solution uas-sort.slnx
```

---

### Task 09.1: Platform project settings, Core imports, long paths and the test fixture

**Files:**
- Modify (add items, keep Part 01's): `src/UasSort.Platform/UasSort.Platform.csproj`
- Create: `src/UasSort.Platform/GlobalUsings.cs`
- Create: `src/UasSort.Platform/Win32/LongPath.cs`
- Create: `src/UasSort.Platform/Namespaces.cs`
- Modify (add items, keep Part 01's): `tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj`
- Create: `tests/UasSort.Platform.Tests/GlobalUsings.cs`
- Create: `tests/UasSort.Platform.Tests/TempDir.cs`
- Create: `tests/UasSort.Platform.Tests/LongPathTests.cs`

**Interfaces:**
- Consumes (Part 01): the projects exist in `uas-sort.slnx`; `Directory.Build.props` sets `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, `IsTrimmable`/`IsAotCompatible` (Platform), `NoWarn` CA1707 (tests) and `UasSortBannedList=platform` for `UasSort.Platform`; Part 01's Platform csproj (`RootNamespace`, `AllowUnsafeBlocks`, the Core reference), Part 01's test csproj (`OutputType Exe`, `<Using Include="Xunit" />`, xunit and TimeProvider.Testing packages, Platform and Testing references) and Part 01's test files (`Build/BannedSymbolsTests.cs`, `Win32/*Tests.cs`, `Stores/SelfTestSandboxTests.cs`) stay as they are. (Part 02): `GlobalUsings.Core.cs` in both projects.
- Produces (defined here):
  - `internal static class LongPath { public static string Prefix(string fullPath); public static string Strip(string path); }`
  - Test fixture `public sealed class TempDir : IDisposable { public string Path { get; } public string Sub(params string[] parts); public string File(string relPath, int bytes = 16, FileAttributes attributes = FileAttributes.Normal); }` creating `%TEMP%\uas-sort-test-{guid}` and deleting it (after clearing ReadOnly attributes and removing deny ACEs this part adds).

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj` — modify Part 01's file: keep every existing item and add only the direct Core reference (the tests use Core types by name). The result:

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
    <ProjectReference Include="..\..\src\UasSort.Core\UasSort.Core.csproj" />  <!-- added by Part 09 -->
    <ProjectReference Include="..\UasSort.Testing\UasSort.Testing.csproj" />
  </ItemGroup>
</Project>
```

`tests/UasSort.Platform.Tests/GlobalUsings.cs` (exactly the registry content; `System.Collections.Immutable` and every Core namespace come from Part 02's `GlobalUsings.Core.cs`, `Xunit` from the csproj):

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

`tests/UasSort.Platform.Tests/TempDir.cs`:

```csharp
using System.Security.AccessControl;
using System.Security.Principal;

namespace UasSort.Platform.Tests;

/// <summary>%TEMP%\uas-sort-test-&lt;guid&gt;, deleted on Dispose (Global Constraints: temp artefacts only there).</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), "uas-sort-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(params string[] parts)
    {
        var p = System.IO.Path.Join([Path, .. parts]);
        Directory.CreateDirectory(p);
        return p;
    }

    public string File(string relPath, int bytes = 16, FileAttributes attributes = FileAttributes.Normal)
    {
        var p = System.IO.Path.Join(Path, relPath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        var data = new byte[bytes];
        new Random(relPath.Length + bytes).NextBytes(data);
        System.IO.File.WriteAllBytes(p, data);
        if (attributes != FileAttributes.Normal) System.IO.File.SetAttributes(p, attributes);
        return p;
    }

    /// <summary>Adds a Deny ACE for the current user; Dispose removes every Deny ACE it finds.</summary>
    public static void Deny(string path, FileSystemRights rights)
    {
        var me = WindowsIdentity.GetCurrent().User!;
        if (Directory.Exists(path))
        {
            var d = new DirectoryInfo(path);
            var acl = d.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(me, rights, AccessControlType.Deny));
            d.SetAccessControl(acl);
        }
        else
        {
            var f = new FileInfo(path);
            var acl = f.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(me, rights, AccessControlType.Deny));
            f.SetAccessControl(acl);
        }
    }

    public void Dispose()
    {
        if (!Directory.Exists(Path)) return;
        foreach (var e in new DirectoryInfo(Path).EnumerateFileSystemInfos("*", new EnumerationOptions
                 { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true }).Prepend(new DirectoryInfo(Path)))
        {
            try { ClearDeny(e); } catch (UnauthorizedAccessException) { }
            try { if ((e.Attributes & FileAttributes.ReadOnly) != 0) e.Attributes &= ~FileAttributes.ReadOnly; } catch (IOException) { }
        }
        // a second pass reaches folders that were unlistable before their deny ACE was removed
        foreach (var d in Directory.EnumerateDirectories(Path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            try { ClearDeny(new DirectoryInfo(d)); } catch (UnauthorizedAccessException) { }
        Directory.Delete(Path, recursive: true);
    }

    private static void ClearDeny(FileSystemInfo e)
    {
        if (e is DirectoryInfo d)
        {
            var acl = d.GetAccessControl();
            foreach (FileSystemAccessRule r in acl.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                if (r.AccessControlType == AccessControlType.Deny) acl.RemoveAccessRuleSpecific(r);
            d.SetAccessControl(acl);
        }
        else if (e is FileInfo f)
        {
            var acl = f.GetAccessControl();
            foreach (FileSystemAccessRule r in acl.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                if (r.AccessControlType == AccessControlType.Deny) acl.RemoveAccessRuleSpecific(r);
            f.SetAccessControl(acl);
        }
    }
}
```

`tests/UasSort.Platform.Tests/LongPathTests.cs`:

```csharp
namespace UasSort.Platform.Tests;

public sealed class LongPathTests
{
    [Theory]
    [InlineData(@"C:\a\b.MP4", @"\\?\C:\a\b.MP4")]
    [InlineData(@"E:\", @"\\?\E:\")]
    [InlineData(@"\\server\share\x", @"\\?\UNC\server\share\x")]
    [InlineData(@"\\?\C:\already", @"\\?\C:\already")]
    public void Prefix_AddsExtendedPrefix(string input, string expected) => Assert.Equal(expected, LongPath.Prefix(input));

    [Theory]
    [InlineData(@"\\?\C:\a\b", @"C:\a\b")]
    [InlineData(@"\\?\UNC\server\share\x", @"\\server\share\x")]
    [InlineData(@"C:\plain", @"C:\plain")]
    public void Strip_RemovesExtendedPrefix(string input, string expected) => Assert.Equal(expected, LongPath.Strip(input));

    [Fact]
    public void Prefix_RejectsRelativePath() => Assert.Throws<ArgumentException>(() => LongPath.Prefix(@"a\b"));

    [Fact]
    public void TempDir_IsUnderTempAndDeleted()
    {
        string p;
        using (var t = new TempDir())
        {
            p = t.Path;
            t.File(@"x\y.bin", attributes: FileAttributes.ReadOnly | FileAttributes.Hidden);
            Assert.StartsWith(Path.GetTempPath(), p, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("uas-sort-test-", p, StringComparison.Ordinal);
        }
        Assert.False(Directory.Exists(p));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-method "*LongPath*"`
Expected: build FAILS with CS0103 "The name 'LongPath' does not exist in the current context" (and CS0234 for `UasSort.Platform.Card`, `.Io`, `.Ledger`, `.Logging` and `.Shell`, which this part creates; `.Win32` and `.Stores` already hold Part 01's types).

- [ ] **Step 3: Implement**

`src/UasSort.Platform/UasSort.Platform.csproj` — modify Part 01's file: keep `RootNamespace`, `AllowUnsafeBlocks` and the Core reference, and add the `System.IO.Hashing` reference and the `InternalsVisibleTo` item. `IsTrimmable`/`IsAotCompatible` and `UasSortBannedList=platform` (with its `BannedSymbols.Platform.txt` analyzer file) stay in Part 01's `Directory.Build.props`; do not repeat them here. The result:

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
    <!-- added by Part 09 -->
    <PackageReference Include="System.IO.Hashing" />
    <InternalsVisibleTo Include="UasSort.Platform.Tests" />
  </ItemGroup>
</Project>
```

`src/UasSort.Platform/GlobalUsings.cs` (exactly the registry content; Core namespaces and `System.Collections.Immutable` come from Part 02's `GlobalUsings.Core.cs`; files that call `Marshal`, `LibraryImport`, `StructLayout`, `NativeMemory` or `SafeFileHandle` add `using System.Runtime.InteropServices;` / `using Microsoft.Win32.SafeHandles;` themselves):

```csharp
global using UasSort.Platform.Card;
global using UasSort.Platform.Io;
global using UasSort.Platform.Ledger;
global using UasSort.Platform.Logging;
global using UasSort.Platform.Shell;
global using UasSort.Platform.Stores;
global using UasSort.Platform.Win32;
```

`src/UasSort.Platform/Win32/LongPath.cs`:

```csharp
namespace UasSort.Platform.Win32;

/// <summary>The \\?\ prefix on every path handed to a P/Invoke (Ref §4.2 Platform row, §13 300-character test).</summary>
internal static class LongPath
{
    private const string Extended = @"\\?\";
    private const string ExtendedUnc = @"\\?\UNC\";

    public static string Prefix(string fullPath)
    {
        if (fullPath.StartsWith(Extended, StringComparison.Ordinal)) return fullPath;
        if (!Path.IsPathFullyQualified(fullPath)) throw new ArgumentException($"Not a full path: {fullPath}", nameof(fullPath));
        return fullPath.StartsWith(@"\\", StringComparison.Ordinal) ? ExtendedUnc + fullPath[2..] : Extended + fullPath;
    }

    public static string Strip(string path)
    {
        if (path.StartsWith(ExtendedUnc, StringComparison.OrdinalIgnoreCase)) return @"\\" + path[ExtendedUnc.Length..];
        if (path.StartsWith(Extended, StringComparison.Ordinal)) return path[Extended.Length..];
        return path;
    }
}
```

Create empty namespace anchors so both projects' `global using`s resolve until later tasks fill them — `src/UasSort.Platform/Namespaces.cs`:

```csharp
// Each namespace below gets its real types in Tasks 09.2–09.12 (Stores already holds Part 01's SelfTestSandbox);
// these markers keep both GlobalUsings.cs files compiling from the first task on. They are internal and carry no behaviour.
namespace UasSort.Platform.Card { internal static class NamespaceMarker { } }
namespace UasSort.Platform.Io { internal static class NamespaceMarker { } }
namespace UasSort.Platform.Ledger { internal static class NamespaceMarker { } }
namespace UasSort.Platform.Logging { internal static class NamespaceMarker { } }
namespace UasSort.Platform.Shell { internal static class NamespaceMarker { } }
namespace UasSort.Platform.Stores { internal static class NamespaceMarker { } }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-method "*LongPath*"`
Expected: PASS, `Passed: 9` (4 + 3 theory rows, the rejection fact, the TempDir fact).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform tests/UasSort.Platform.Tests
git commit -m "build: platform project settings, long-path helper and temp-dir test fixture

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 09.2: File interop, canonical paths (`PathFacts`), sync roots and known folders

**Files:**
- Create: `src/UasSort.Platform/Win32/Kernel32.Files.cs`
- Create: `src/UasSort.Platform/Win32/Shell32.cs`
- Create: `src/UasSort.Platform/Win32/CldApi.cs`
- Create: `src/UasSort.Platform/Io/PathFacts.cs`
- Create: `src/UasSort.Platform/Io/KnownFolders.cs`
- Create: `tests/UasSort.Platform.Tests/Cmd.cs`
- Create: `tests/UasSort.Platform.Tests/PathFactsTests.cs`

**Interfaces:**
- Consumes (Part 02): `IPathFacts` (Ref §3 Supporting types (2)).
- Produces (defined here):
  - `internal static partial class Kernel32` (file members): `GetFileAttributesEx`, `CreateFile`, `SetFileAttributes`, `FlushFileBuffers`, `MoveFileEx`, `GetFinalPathNameByHandle`, constants, `struct Win32FileAttributeData`; plus `static uint? Kernel32.TryGetAttributes(string fullPath, out int error)`.
  - `public sealed class PathFacts : IPathFacts` — `Canonical` resolves junctions, subst drives and 8.3 names through the nearest existing **directory** (it never opens a file, so a placeholder can't be recalled), then appends the missing or file tail; the result has no `\\?\` prefix and no trailing separator except on a volume root. `InSyncRoot` is `CfGetSyncRootInfoByPath` success; `SyncRoots` reads `HKCU\Software\Microsoft\OneDrive\Accounts\*\UserFolder` and every `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager\*\UserSyncRoots` value (read-only).
  - `public static class KnownFolders { public static string Pictures(); public static string AppDataDir(); public static string SystemVolumeRoot(); }`

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/Cmd.cs`:

```csharp
using System.Diagnostics;

namespace UasSort.Platform.Tests;

internal static class Cmd
{
    public static int Run(string arguments)
    {
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
                                                    RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add(arguments);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    /// <summary>A drive letter that is not in use, from Z: down to M:.</summary>
    public static char FreeDriveLetter()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        for (var c = 'Z'; c >= 'M'; c--) if (!used.Contains(c)) return c;
        throw new InvalidOperationException("no free drive letter for the subst test");
    }
}
```

`tests/UasSort.Platform.Tests/PathFactsTests.cs`:

```csharp
namespace UasSort.Platform.Tests;

public sealed class PathFactsTests
{
    private readonly PathFacts _facts = new();

    [Fact]
    public void Canonical_MissingTail_KeepsNamesUnderCanonicalParent()
    {
        using var t = new TempDir();
        var c = _facts.Canonical(Path.Join(t.Path, "a", "b", "c.MP4"));
        Assert.Equal(Path.Join(_facts.Canonical(t.Path), "a", "b", "c.MP4"), c);
    }

    [Fact]
    public void Canonical_ReturnsOnDiskCase_AndNoPrefix()
    {
        using var t = new TempDir();
        var d = t.Sub("MixedCase");
        var c = _facts.Canonical(d.ToUpperInvariant());
        Assert.EndsWith(@"\MixedCase", c, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\\?\", c, StringComparison.Ordinal);
        Assert.Equal(_facts.Canonical(d), c);
    }

    [Fact]
    public void Canonical_VolumeRootKeepsSeparator()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Equal(root.ToUpperInvariant(), _facts.Canonical(root).ToUpperInvariant());
        Assert.EndsWith(@"\", _facts.Canonical(root), StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_ResolvesJunctionIntoLedgerFolder()
    {
        using var t = new TempDir();
        var ledger = t.Sub("video", ".uas-sort");
        File.WriteAllText(Path.Join(ledger, "ledger-A.jsonl"), "");
        var link = Path.Join(t.Path, "link");
        Assert.Equal(0, Cmd.Run($"mklink /J \"{link}\" \"{ledger}\""));
        Assert.Equal(_facts.Canonical(Path.Join(ledger, "ledger-A.jsonl")),
                     _facts.Canonical(Path.Join(link, "ledger-A.jsonl")));
        Assert.EndsWith(@"\video\.uas-sort\ledger-A.jsonl", _facts.Canonical(Path.Join(link, "ledger-A.jsonl")), StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_ResolvesSubstDrive()
    {
        using var t = new TempDir();
        var ledger = t.Sub("video", ".uas-sort");
        var letter = Cmd.FreeDriveLetter();
        Assert.Equal(0, Cmd.Run($"subst {letter}: \"{ledger}\""));
        try
        {
            Assert.Equal(_facts.Canonical(Path.Join(ledger, "ledger-A.jsonl")),
                         _facts.Canonical($@"{letter}:\ledger-A.jsonl"));
        }
        finally { Cmd.Run($"subst {letter}: /D"); }
    }

    [Fact]
    public void InSyncRoot_IsFalseForTemp()
    {
        using var t = new TempDir();
        Assert.False(_facts.InSyncRoot(_facts.Canonical(t.Path)));
    }

    [Fact]
    public void SyncRoots_AreFullyQualified()
    {
        foreach (var r in _facts.SyncRoots()) Assert.True(Path.IsPathFullyQualified(r), r);
    }

    [Fact]
    public void KnownFolders_AreFullyQualified()
    {
        Assert.True(Path.IsPathFullyQualified(KnownFolders.Pictures()));
        Assert.EndsWith(@"\uas-sort", KnownFolders.AppDataDir(), StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"^[A-Za-z]:\\$", KnownFolders.SystemVolumeRoot());
    }

    [Fact]
    public void TryGetAttributes_MissingIsNull_InvalidNameIsError()
    {
        using var t = new TempDir();
        Assert.Null(Kernel32.TryGetAttributes(Path.Join(t.Path, "none.bin"), out var e1));
        Assert.Equal(Kernel32.ERROR_FILE_NOT_FOUND, e1);
        Assert.Null(Kernel32.TryGetAttributes(Path.Join(t.Path, "bad|name"), out var e2));
        Assert.Equal(Kernel32.ERROR_INVALID_NAME, e2);
        var f = t.File("x.bin", attributes: FileAttributes.Hidden);
        Assert.True((Kernel32.TryGetAttributes(f, out _)!.Value & (uint)FileAttributes.Hidden) != 0);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*PathFactsTests*"`
Expected: build FAILS with CS0246 "The type or namespace name 'PathFacts' could not be found" and CS0103 for `KnownFolders` and `Kernel32`.

- [ ] **Step 3: Implement**

`src/UasSort.Platform/Win32/Kernel32.Files.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UasSort.Platform.Win32;

[StructLayout(LayoutKind.Sequential)]
internal struct Win32FileAttributeData
{
    public uint FileAttributes;
    public uint CreationLow, CreationHigh;
    public uint LastAccessLow, LastAccessHigh;
    public uint LastWriteLow, LastWriteHigh;
    public uint SizeHigh, SizeLow;

    public readonly long Size => ((long)SizeHigh << 32) | SizeLow;
    public readonly DateTime CreationUtc => ToUtc(CreationHigh, CreationLow);
    public readonly DateTime LastAccessUtc => ToUtc(LastAccessHigh, LastAccessLow);
    public readonly DateTime LastWriteUtc => ToUtc(LastWriteHigh, LastWriteLow);
    private static DateTime ToUtc(uint high, uint low) => DateTime.FromFileTimeUtc(((long)high << 32) | low);
}

internal static unsafe partial class Kernel32
{
    public const int ERROR_FILE_NOT_FOUND = 2, ERROR_PATH_NOT_FOUND = 3, ERROR_ACCESS_DENIED = 5, ERROR_NOT_READY = 21,
                     ERROR_WRITE_PROTECT = 19, ERROR_SHARING_VIOLATION = 32, ERROR_FILE_EXISTS = 80, ERROR_INVALID_NAME = 123,
                     ERROR_DIR_NOT_EMPTY = 145, ERROR_ALREADY_EXISTS = 183, ERROR_MORE_DATA = 234, ERROR_DEVICE_NOT_CONNECTED = 1167;
    public const uint FILE_ATTRIBUTE_READONLY = 0x1, FILE_ATTRIBUTE_HIDDEN = 0x2, FILE_ATTRIBUTE_DIRECTORY = 0x10,
                      FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 0x2000, FILE_ATTRIBUTE_OFFLINE = 0x1000,
                      FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x40000, FILE_ATTRIBUTE_PINNED = 0x80000, FILE_ATTRIBUTE_UNPINNED = 0x100000,
                      FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x400000;
    public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, FILE_SHARE_DELETE = 4;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000, FILE_FLAG_NO_BUFFERING = 0x20000000;
    public const uint MOVEFILE_WRITE_THROUGH = 0x8;
    private const int GetFileExInfoStandard = 0;

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileAttributesExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileAttributesEx(string lpFileName, int fInfoLevelId, out Win32FileAttributeData lpFileInformation);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, nint lpSecurityAttributes,
                                                    uint dwCreationDisposition, uint dwFlagsAndAttributes, nint hTemplateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "SetFileAttributesW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetFileAttributes(string lpFileName, uint dwFileAttributes);

    [LibraryImport("kernel32.dll", EntryPoint = "FlushFileBuffers", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FlushFileBuffers(SafeFileHandle hFile);

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveFileEx(string lpExistingFileName, string lpNewFileName, uint dwFlags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    public static partial uint GetFinalPathNameByHandle(SafeFileHandle hFile, char* lpszFilePath, uint cchFilePath, uint dwFlags);

    /// <summary>Attributes without opening anything; null when the call fails (error in <paramref name="error"/>).</summary>
    public static uint? TryGetAttributes(string fullPath, out int error)
    {
        if (GetFileAttributesEx(LongPath.Prefix(fullPath), GetFileExInfoStandard, out var data)) { error = 0; return data.FileAttributes; }
        error = Marshal.GetLastPInvokeError();
        return null;
    }

    public static bool TryGetAttributeData(string fullPath, out Win32FileAttributeData data, out int error)
    {
        if (GetFileAttributesEx(LongPath.Prefix(fullPath), GetFileExInfoStandard, out data)) { error = 0; return true; }
        error = Marshal.GetLastPInvokeError();
        return false;
    }

    public static bool IsNotFound(int error) => error is ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND;
}
```

`src/UasSort.Platform/Win32/Shell32.cs`:

```csharp
using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

internal static partial class Shell32
{
    [LibraryImport("shell32.dll", EntryPoint = "SHGetKnownFolderPath")]
    public static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, nint hToken, out nint ppszPath);
}
```

`src/UasSort.Platform/Win32/CldApi.cs`:

```csharp
using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

internal static unsafe partial class CldApi
{
    public const int CF_SYNC_ROOT_INFO_BASIC = 0;
    public const int HRESULT_ERROR_MORE_DATA = unchecked((int)0x800700EA);

    [LibraryImport("cldapi.dll", EntryPoint = "CfGetSyncRootInfoByPath", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int CfGetSyncRootInfoByPath(string filePath, int infoClass, void* infoBuffer, uint infoBufferLength, uint* returnedLength);
}
```

`src/UasSort.Platform/Io/PathFacts.cs`:

```csharp
using Microsoft.Win32;

namespace UasSort.Platform.Io;

/// <summary>Canonical paths and cloud sync roots (Ref §4.3 card source validation steps 2–3; the ledger exemption's path matching).</summary>
public sealed class PathFacts : IPathFacts
{
    public string Canonical(string path)
    {
        var full = Path.GetFullPath(LongPath.Strip(path));
        var current = IsRoot(full) ? full : Path.TrimEndingDirectorySeparator(full);
        var tail = new Stack<string>();
        // Walk up to the nearest existing DIRECTORY. A file is never opened here: a placeholder with
        // RecallOnOpen would be hydrated by any open (Ref §4.3 Placeholders).
        while (Kernel32.TryGetAttributes(current, out _) is not uint a || (a & Kernel32.FILE_ATTRIBUTE_DIRECTORY) == 0)
        {
            var parent = Path.GetDirectoryName(current);
            if (parent is null) return full;                       // no existing ancestor (missing drive): lexical form
            tail.Push(Path.GetFileName(current));
            current = parent;
        }
        var resolved = FinalDirectoryPath(current) ?? current;
        foreach (var name in tail) resolved = Path.Join(resolved, name);   // Stack enumerates shallowest first
        return resolved;
    }

    public bool InSyncRoot(string canonicalPath)
    {
        unsafe
        {
            var buffer = stackalloc byte[64];
            uint returned = 0;
            var hr = CldApi.CfGetSyncRootInfoByPath(LongPath.Strip(canonicalPath), CldApi.CF_SYNC_ROOT_INFO_BASIC, buffer, 64, &returned);
            return hr >= 0 || hr == CldApi.HRESULT_ERROR_MORE_DATA;
        }
    }

    public IReadOnlyList<string> SyncRoots()
    {
        var roots = new List<string>();
        using (var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts"))
        {
            foreach (var name in accounts?.GetSubKeyNames() ?? [])
            {
                using var account = accounts!.OpenSubKey(name);
                if (account?.GetValue("UserFolder") is string folder && Path.IsPathFullyQualified(folder)) roots.Add(folder);
            }
        }
        using (var manager = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager"))
        {
            foreach (var name in manager?.GetSubKeyNames() ?? [])
            {
                using var user = manager!.OpenSubKey(name + @"\UserSyncRoots");
                foreach (var valueName in user?.GetValueNames() ?? [])
                    if (user!.GetValue(valueName) is string folder && Path.IsPathFullyQualified(folder)) roots.Add(folder);
            }
        }
        return roots.Select(r => Path.TrimEndingDirectorySeparator(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsRoot(string full) => string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase);

    private static unsafe string? FinalDirectoryPath(string directory)
    {
        using var handle = Kernel32.CreateFile(LongPath.Prefix(directory), 0 /* no data access */,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE | Kernel32.FILE_SHARE_DELETE, 0, Kernel32.OPEN_EXISTING,
            Kernel32.FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (handle.IsInvalid) return null;
        var size = 512u;
        while (true)
        {
            var buffer = new char[size];
            fixed (char* p = buffer)
            {
                var n = Kernel32.GetFinalPathNameByHandle(handle, p, size, 0 /* FILE_NAME_NORMALIZED | VOLUME_NAME_DOS */);
                if (n == 0) return null;
                if (n < size)
                {
                    var s = LongPath.Strip(new string(p, 0, (int)n));
                    return IsRoot(s) ? s : Path.TrimEndingDirectorySeparator(s);
                }
                size = n + 1;
            }
        }
    }
}
```

`src/UasSort.Platform/Io/KnownFolders.cs`:

```csharp
using System.Runtime.InteropServices;

namespace UasSort.Platform.Io;

public static class KnownFolders
{
    private static readonly Guid FolderIdPictures = new("33E28130-4E1E-4676-835A-98395C3BC3BB");

    /// <summary>SHGetKnownFolderPath(FOLDERID_Pictures): follows OneDrive folder backup (Ref §9.1 Setup).</summary>
    public static string Pictures()
    {
        var hr = Shell32.SHGetKnownFolderPath(in FolderIdPictures, 0, 0, out var p);
        try
        {
            if (hr < 0) throw Marshal.GetExceptionForHR(hr)!;
            return Marshal.PtrToStringUni(p)!;
        }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    /// <summary>%LOCALAPPDATA%\uas-sort (Ref §11).</summary>
    public static string AppDataDir()
        => Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uas-sort");

    /// <summary>The volume holding Windows, e.g. C:\ (GuardContext.SystemVolumeRoot).</summary>
    public static string SystemVolumeRoot() => Path.GetPathRoot(Environment.SystemDirectory)!;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*PathFactsTests*"`
Expected: PASS, `Passed: 9`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform tests/UasSort.Platform.Tests
git commit -m "feat: canonical paths, sync-root detection and known folders

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 09.3: `PlaceholderGuard`, the guard gate and guard contexts

**Files:**
- Create: `src/UasSort.Platform/Io/PlaceholderGuard.cs`
- Create: `src/UasSort.Platform/Io/IoGate.cs`
- Create: `src/UasSort.Platform/Io/GuardContexts.cs`
- Create: `src/UasSort.Platform/Io/CloudOnlyFileException.cs`
- Create: `tests/UasSort.Platform.Tests/TestEnv.cs`
- Create: `tests/UasSort.Platform.Tests/IoGateTests.cs`

**Interfaces:**
- Consumes (Part 02): `IoGuardPolicy.Check`, `IoOp`, `GuardContext`, `GuardDecision` (`GuardAllow`, `GuardUnsafe`, `GuardCloudOnly`, `GuardHydration`), `UnsafeIoException`. (Part 05): `SettingsDefaults.Derive`.
- Produces (defined here):
  - `public static class PlaceholderGuard { public static uint? ReadAttributes(string fullPath); }` — its only member: returns null only for "not found" and throws `UnsafeIoException` for any other failure (Ref §4.3 rule 1). Exposing placeholders at process start is not here: `Program.Main` (Part 11) and the CLI (Part 12) call Part 01's `PlaceholderMode.ExposePlaceholders()` (`UasSort.Platform.Win32`, the only `RtlSetProcessPlaceholderCompatibilityMode` declaration, in Part 01's `NativeMethods`).
  - `internal static class IoGate { public static uint? Require(IoOp op, string canonicalPath, GuardContext ctx); }` — the single place Platform turns a `GuardDecision` into allow or throw: `GuardUnsafe`/`GuardHydration` → `UnsafeIoException`, `GuardCloudOnly` → `CloudOnlyFileException`.
  - `public sealed class CloudOnlyFileException(string path) : IOException` with `public string Path { get; }`.
  - `public static class GuardContexts { public static GuardContext For(Settings s, string appDataDir, string machine, IPathFacts facts, string? cardRoot = null, IEnumerable<string>? newFolderDirs = null, IReadOnlySet<string>? ownTemps = null, IReadOnlySet<string>? renamed = null); }` — every path canonical, every set `OrdinalIgnoreCase`, `SystemVolumeRoot = KnownFolders.SystemVolumeRoot()`, `CardIsVerifiedCardVolume = false`, `Cleanup = null` (only `WindowsCardEraserFactory` sets those two, Task 09.11).
  - Test helper `public sealed class TestEnv : IDisposable` (video, photo, appdata and card folders in one `TempDir`; `Settings`; `Facts`; `Machine = "TESTPC"`; `Context(string? cardRoot = null)`; `C(string path)` = canonical).

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/TestEnv.cs`:

```csharp
namespace UasSort.Platform.Tests;

/// <summary>A temp library + app-data + card layout with its Settings and GuardContext.</summary>
public sealed class TestEnv : IDisposable
{
    public const string Machine = "TESTPC";

    public TestEnv()
    {
        VideoRoot = Temp.Sub("video");
        PhotoRoot = Temp.Sub("photo");
        AppData = Temp.Sub("appdata");
        CardRoot = Temp.Sub("card");
        Settings = SettingsDefaults.Derive(Temp.Path) with
        {
            VideoRoot = VideoRoot, PhotoRoot = PhotoRoot, PreviousPhotoRoots = [], RootsConfirmed = true,
        };
    }

    public TempDir Temp { get; } = new();
    public PathFacts Facts { get; } = new();
    public string VideoRoot { get; }
    public string PhotoRoot { get; }
    public string AppData { get; }
    public string CardRoot { get; }
    public Settings Settings { get; }

    public GuardContext Context(string? cardRoot = null) => GuardContexts.For(Settings, AppData, Machine, Facts, cardRoot);
    public string C(string path) => Facts.Canonical(path);
    public void Dispose() => Temp.Dispose();
}
```

`tests/UasSort.Platform.Tests/IoGateTests.cs`:

```csharp
namespace UasSort.Platform.Tests;

public sealed class IoGateTests
{
    [Fact]
    public void AppDataFile_IsAllowed_AndReturnsAttributes()
    {
        using var env = new TestEnv();
        var f = env.Temp.File(@"appdata\settings.json");
        Assert.NotNull(IoGate.Require(IoOp.ReadData, env.C(f), env.Context()));
    }

    [Fact]
    public void PreExistingLibraryFile_ReadIsRefused()
    {
        using var env = new TestEnv();
        var f = env.Temp.File(@"video\2026\2026-09\2026-09-27 Zachar Bay\DJI_0001.MP4");
        Assert.Throws<UnsafeIoException>(() => IoGate.Require(IoOp.ReadData, env.C(f), env.Context()));
    }

    [Fact]
    public void OfflineLedgerFile_IsCloudOnly_NotAViolation()
    {
        using var env = new TestEnv();
        var f = env.Temp.File(@"video\.uas-sort\ledger-B.jsonl", attributes: FileAttributes.Offline);
        var ex = Assert.Throws<CloudOnlyFileException>(() => IoGate.Require(IoOp.ReadData, env.C(f), env.Context()));
        Assert.Equal(env.C(f), ex.Path, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void OfflineFileAnywhereElse_IsRefusedAsHydration()
    {
        using var env = new TestEnv();
        var f = env.Temp.File(@"appdata\drafts\x.json", attributes: FileAttributes.Offline);
        var ex = Assert.Throws<UnsafeIoException>(() => IoGate.Require(IoOp.ReadData, env.C(f), env.Context()));
        Assert.Contains("placeholder", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LookAlikeLedgerFolder_IsRefused()
    {
        using var env = new TestEnv();
        var f = env.Temp.File(@"video\.uas-sort2\ledger-A.jsonl");
        Assert.Throws<UnsafeIoException>(() => IoGate.Require(IoOp.ReadData, env.C(f), env.Context()));
    }

    [Fact]
    public void JunctionIntoLedgerFolder_GetsTheSameRules()
    {
        using var env = new TestEnv();
        env.Temp.File(@"video\.uas-sort\ledger-A.jsonl");
        env.Temp.File(@"video\.uas-sort\notes.txt");
        var link = Path.Join(env.Temp.Path, "link");
        Assert.Equal(0, Cmd.Run($"mklink /J \"{link}\" \"{Path.Join(env.VideoRoot, ".uas-sort")}\""));
        Assert.NotNull(IoGate.Require(IoOp.ReadData, env.C(Path.Join(link, "ledger-A.jsonl")), env.Context()));
        Assert.Throws<UnsafeIoException>(() => IoGate.Require(IoOp.ReadData, env.C(Path.Join(link, "notes.txt")), env.Context()));
    }

    [Fact]
    public void UnreadableAttributes_AreRefusedBeforeThePolicy()
    {
        using var env = new TestEnv();
        Assert.Throws<UnsafeIoException>(() => PlaceholderGuard.ReadAttributes(Path.Join(env.AppData, "bad|name")));
        Assert.Null(PlaceholderGuard.ReadAttributes(Path.Join(env.AppData, "missing.json")));
    }

    [Fact]
    public void GuardContexts_AreCanonicalAndUnverified()
    {
        using var env = new TestEnv();
        var ctx = env.Context(env.CardRoot);
        Assert.Equal(env.C(env.VideoRoot), ctx.VideoRoot);
        Assert.Equal(env.C(env.CardRoot), ctx.CardRoot);
        Assert.False(ctx.CardIsVerifiedCardVolume);
        Assert.Null(ctx.Cleanup);
        Assert.Equal(TestEnv.Machine, ctx.Machine);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*IoGateTests*"`
Expected: build FAILS with CS0103 "The name 'IoGate' does not exist in the current context" and CS0246 for `CloudOnlyFileException` and `GuardContexts`.

- [ ] **Step 3: Implement**

`src/UasSort.Platform/Io/PlaceholderGuard.cs`:

```csharp
namespace UasSort.Platform.Io;

/// <summary>Ref §4.3 Placeholders: attributes are read first (no open); unreadable attributes refuse the open.
/// Process start exposes placeholders through Part 01's PlaceholderMode.ExposePlaceholders(), not here.</summary>
public static class PlaceholderGuard
{
    public static uint? ReadAttributes(string fullPath)
    {
        var attributes = Kernel32.TryGetAttributes(fullPath, out var error);
        if (attributes is not null) return attributes;
        if (Kernel32.IsNotFound(error)) return null;
        throw new UnsafeIoException($"The attributes of {fullPath} can't be read (Win32 error {error}); refusing to open it");
    }
}
```

`src/UasSort.Platform/Io/CloudOnlyFileException.cs`:

```csharp
namespace UasSort.Platform.Io;

/// <summary>A cloud-only top-level .uas-sort\ledger*.jsonl: reported as LedgerFolderState.CloudOnly, never a bug (Ref §4.3).</summary>
public sealed class CloudOnlyFileException(string path) : IOException($"{path} is cloud-only; set the .uas-sort folder to Always keep on this device")
{
    public string Path { get; } = path;
}
```

`src/UasSort.Platform/Io/IoGate.cs`:

```csharp
namespace UasSort.Platform.Io;

/// <summary>Every open, create, attribute change, delete and rename in Platform goes through here first (Ref §4.2 Platform row).</summary>
internal static class IoGate
{
    /// <returns>The target's attributes (null = it doesn't exist), when the policy allows the operation.</returns>
    public static uint? Require(IoOp op, string canonicalPath, GuardContext ctx)
    {
        var attributes = PlaceholderGuard.ReadAttributes(canonicalPath);
        return IoGuardPolicy.Check(op, canonicalPath, attributes, ctx) switch
        {
            GuardAllow => attributes,
            GuardCloudOnly c => throw new CloudOnlyFileException(c.Path),
            GuardHydration h => throw new UnsafeIoException(
                $"Refused {op} of {h.Path}: a cloud placeholder (attributes 0x{h.Attributes:X}) would be downloaded"),
            GuardUnsafe u => throw new UnsafeIoException($"Refused {op} of {canonicalPath}: {u.Reason}"),
        };
    }
}
```

`src/UasSort.Platform/Io/GuardContexts.cs`:

```csharp
namespace UasSort.Platform.Io;

public static class GuardContexts
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static GuardContext For(Settings s, string appDataDir, string machine, IPathFacts facts, string? cardRoot = null,
                                   IEnumerable<string>? newFolderDirs = null, IReadOnlySet<string>? ownTemps = null,
                                   IReadOnlySet<string>? renamed = null)
        => new(VideoRoot: facts.Canonical(s.VideoRoot),
               PhotoRoot: facts.Canonical(s.PhotoRoot),
               PreviousPhotoRoots: [.. s.PreviousPhotoRoots.Select(facts.Canonical)],
               CardRoot: cardRoot is null ? null : facts.Canonical(cardRoot),
               AppDataDir: facts.Canonical(appDataDir),
               Machine: machine,
               NewFolderDirs: newFolderDirs is null ? None
                   : new HashSet<string>(newFolderDirs.Select(facts.Canonical), StringComparer.OrdinalIgnoreCase),
               OwnTempsThisRun: ownTemps ?? None,
               RenamedThisRun: renamed ?? None,
               SystemVolumeRoot: KnownFolders.SystemVolumeRoot(),
               CardIsVerifiedCardVolume: false,
               Cleanup: null);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*IoGateTests*"`
Expected: PASS, `Passed: 8`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform tests/UasSort.Platform.Tests
git commit -m "feat: placeholder guard, guard gate and canonical guard contexts

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 09.4: `WindowsDirectoryLister`

**Files:**
- Create: `src/UasSort.Platform/Io/WindowsDirectoryLister.cs`
- Create: `tests/UasSort.Platform.Tests/DirectoryListerTests.cs`

**Interfaces:**
- Consumes (Part 02): `IDirectoryLister`, `FsEntry`, `ListingResult` (Ref §4.1).
- Produces (defined here): `public sealed class WindowsDirectoryLister : IDirectoryLister` — one `FileSystemEnumerator<FsEntry>` subclass per directory (so every `ContinueOnError` code is attributed to the directory being read), options `AttributesToSkip = 0`, `IgnoreInaccessible = false`, `ReturnSpecialDirectories = false`; `excludeDirNames` matched case-insensitively on directory names, neither returned nor entered; reparse-point directories are returned but not entered; never opens a file.

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/DirectoryListerTests.cs`:

```csharp
using System.Security.AccessControl;

namespace UasSort.Platform.Tests;

public sealed class DirectoryListerTests
{
    private static readonly IReadOnlySet<string> NoExclusions = new HashSet<string>();
    private readonly WindowsDirectoryLister _lister = new();

    [Fact]
    public void HiddenAndSystemFiles_AreListed_WithRawAttributes()
    {
        using var t = new TempDir();
        t.File(@"DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", attributes: FileAttributes.Hidden);
        t.File(@"DCIM\DJI_001\.DJI_20260927140627_0128_D.MP4.trinf", attributes: FileAttributes.Hidden);
        t.File(@"MISC\FC9113.db", attributes: FileAttributes.System);
        var r = _lister.Enumerate(t.Path, recurse: true, NoExclusions);
        Assert.Empty(r.Errors);
        var mp4 = Assert.Single(r.Entries, e => e.RelPath == @"DCIM\DJI_001\DJI_20260927140627_0128_D.MP4");
        Assert.True((mp4.RawAttributes & (uint)FileAttributes.Hidden) != 0);
        Assert.Equal(16, mp4.Size);
        Assert.Contains(r.Entries, e => e.RelPath == @"DCIM\DJI_001\.DJI_20260927140627_0128_D.MP4.trinf");
        Assert.Contains(r.Entries, e => e.RelPath == @"MISC\FC9113.db" && (e.RawAttributes & (uint)FileAttributes.System) != 0);
        Assert.Contains(r.Entries, e => e.RelPath == "DCIM" && e.IsDirectory);
    }

    [Fact]
    public void AccessDeniedFolder_IsRecordedAsAnError_AndTheRestIsListed()
    {
        using var t = new TempDir();
        t.File(@"ok\a.MP4");
        t.File(@"locked\b.MP4");
        var locked = Path.Join(t.Path, "locked");
        TempDir.Deny(locked, FileSystemRights.ListDirectory);
        var r = _lister.Enumerate(t.Path, recurse: true, NoExclusions);
        Assert.Contains(r.Errors, e => e.Path == locked && e.Win32Error == 5);
        Assert.Contains(r.Entries, e => e.RelPath == @"ok\a.MP4");
        Assert.Contains(r.Entries, e => e.RelPath == "locked" && e.IsDirectory);
        Assert.DoesNotContain(r.Entries, e => e.RelPath == @"locked\b.MP4");
    }

    [Fact]
    public void Listing_OpensNoFile()
    {
        using var t = new TempDir();
        var files = new[] { @"DCIM\DJI_001\a.MP4", @"DCIM\DJI_001\b.DNG", @"MISC\FC9113.db", "x.bin" }.Select(p => t.File(p, 4096)).ToList();
        var locks = files.Select(f => new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None)).ToList();
        try
        {
            var r = _lister.Enumerate(t.Path, recurse: true, NoExclusions);
            Assert.Empty(r.Errors);
            Assert.Equal(4, r.Entries.Count(e => !e.IsDirectory && e.Size == 4096));
        }
        finally { locks.ForEach(l => l.Dispose()); }
    }

    [Fact]
    public void LibraryListing_ExcludesLedgerFolder_CaseInsensitively()
    {
        using var t = new TempDir();
        t.File(@"video\.uas-sort\ledger-A.jsonl");
        t.File(@"video\.uas-sort\sub\x.MP4");
        t.File(@"video\2026\2026-09\2026-09-27 Zachar Bay\DJI_0128.MP4");
        var root = Path.Join(t.Path, "video");
        foreach (var exclude in new[] { ".uas-sort", ".UAS-SORT" })
        {
            var r = _lister.Enumerate(root, recurse: true, new HashSet<string> { exclude });
            Assert.DoesNotContain(r.Entries, e => e.FullPath.Contains(".uas-sort", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(r.Entries, e => e.RelPath == @"2026\2026-09\2026-09-27 Zachar Bay\DJI_0128.MP4");
            Assert.Equal(4, r.Entries.Length);   // 2026, 2026-09, the event folder, the clip
        }
    }

    [Fact]
    public void LedgerStoreTopLevelListing_SeesNothingBelow()
    {
        using var t = new TempDir();
        t.File(@"video\.uas-sort\ledger-A.jsonl");
        t.File(@"video\.uas-sort\sub\x.MP4");
        var r = _lister.Enumerate(Path.Join(t.Path, "video", ".uas-sort"), recurse: false, NoExclusions);
        Assert.Equal(["ledger-A.jsonl", "sub"], r.Entries.Select(e => e.RelPath).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void MissingRoot_IsAnError_NotAnException()
    {
        using var t = new TempDir();
        var missing = Path.Join(t.Path, "gone");
        var r = _lister.Enumerate(missing, recurse: true, NoExclusions);
        Assert.Empty(r.Entries);
        Assert.Equal(missing, Assert.Single(r.Errors).Path);
    }

    [Fact]
    public void Junction_IsListedButNotEntered()
    {
        using var t = new TempDir();
        t.File(@"target\inside.MP4");
        var link = Path.Join(t.Path, "root", "link");
        Directory.CreateDirectory(Path.Join(t.Path, "root"));
        Assert.Equal(0, Cmd.Run($"mklink /J \"{link}\" \"{Path.Join(t.Path, "target")}\""));
        var r = _lister.Enumerate(Path.Join(t.Path, "root"), recurse: true, NoExclusions);
        Assert.Equal("link", Assert.Single(r.Entries).RelPath);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*DirectoryListerTests*"`
Expected: build FAILS with CS0246 "The type or namespace name 'WindowsDirectoryLister' could not be found".

- [ ] **Step 3: Implement**

`src/UasSort.Platform/Io/WindowsDirectoryLister.cs`:

```csharp
using System.IO.Enumeration;

namespace UasSort.Platform.Io;

/// <summary>Listing only (Ref §4.1, §5): explicit options, errors collected per directory, never opens a file.</summary>
public sealed class WindowsDirectoryLister : IDirectoryLister
{
    private static readonly EnumerationOptions Options = new()
    {
        AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false, ReturnSpecialDirectories = false,
    };

    public ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames)
    {
        var exclude = new HashSet<string>(excludeDirNames, StringComparer.OrdinalIgnoreCase);
        var fullRoot = Path.GetFullPath(root);
        var entries = ImmutableArray.CreateBuilder<FsEntry>();
        var errors = ImmutableArray.CreateBuilder<(string Path, int Win32Error)>();
        var pending = new Queue<string>();
        pending.Enqueue(fullRoot);
        while (pending.TryDequeue(out var directory))
        {
            using var one = new OneDirectory(directory, fullRoot, exclude);
            while (one.MoveNext())
            {
                var e = one.Current;
                entries.Add(e);
                if (recurse && e.IsDirectory && (e.RawAttributes & (uint)FileAttributes.ReparsePoint) == 0) pending.Enqueue(e.FullPath);
            }
            foreach (var code in one.Errors) errors.Add((directory, code));
        }
        return new ListingResult(entries.ToImmutable(), errors.ToImmutable());
    }

#pragma warning disable RS0030 // IO layer: the one lister; FileSystemEnumerator never opens files, only directory handles
    private sealed class OneDirectory(string directory, string root, HashSet<string> exclude)
        : FileSystemEnumerator<FsEntry>(directory, Options)
#pragma warning restore RS0030
    {
        // A field initializer: assigned before the base constructor, which may already call ContinueOnError.
        private readonly List<int> _errors = [];

        public IReadOnlyList<int> Errors => _errors;

        protected override bool ContinueOnError(int error)
        {
            _errors.Add(error);
            return true;
        }

        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
            => !(entry.IsDirectory && exclude.Contains(entry.FileName.ToString()));

        protected override FsEntry TransformEntry(ref FileSystemEntry entry)
        {
            var full = entry.ToFullPath();
            return new FsEntry(full, Path.GetRelativePath(root, full), entry.IsDirectory, entry.IsDirectory ? 0 : entry.Length,
                               entry.LastWriteTimeUtc.UtcDateTime, entry.CreationTimeUtc.UtcDateTime,
                               entry.LastAccessTimeUtc.UtcDateTime, (uint)entry.Attributes);
        }
    }
}
```

(The unused primary-constructor parameter `directory` is consumed by the base call; `root` and `exclude` are captured only for `TransformEntry` and `ShouldIncludeEntry`, which the base constructor never calls.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*DirectoryListerTests*"`
Expected: PASS, `Passed: 7`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform tests/UasSort.Platform.Tests
git commit -m "feat: windows directory lister with explicit options and per-directory errors

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 09.5: Volume facts from Win32 and `WindowsVolumeProvider`

**Files:**
- Create: `src/UasSort.Platform/Win32/Kernel32.Volumes.cs`
- Create: `src/UasSort.Platform/Io/VolumeQuery.cs`
- Create: `src/UasSort.Platform/Io/WindowsVolumeProvider.cs`
- Create: `tests/UasSort.Platform.Tests/VolumeTests.cs`

**Interfaces:**
- Consumes (Part 02): `IVolumeProvider`, `VolumeInfo`, `CardIdentity`, `CardSpace` (Ref §3, §4.1).
- Produces (defined here):
  - `internal static partial class Kernel32` (volume members): `GetVolumeInformation`, `GetVolumePathName`, `GetDiskFreeSpaceEx`, `GetDiskFreeSpace`, `DeviceIoControl`, `IOCTL_STORAGE_QUERY_PROPERTY`, `FILE_READ_ONLY_VOLUME`.
  - `internal static class VolumeQuery` — `VolumeInformation? Information(string volumeRoot)` (`record VolumeInformation(uint Serial, string? Label, string FileSystem, uint Flags)`, defined here); `string? VolumePathName(string path)`; `(long Free, long Total)? Space(string existingDir)`; `int ClusterBytes(string volumeRoot)`; `(string BusType, bool RemovableMedia)? Bus(string volumeRoot)` (IOCTL on `\\.\X:` opened with no data access); `bool IsSystemBootOrPaging(string volumeRoot)`; `CardIdentity? Identity(string volumeRoot)`; `static string BusName(uint storageBusType)`.
  - `public sealed class WindowsVolumeProvider : IVolumeProvider` — one `VolumeInfo` per logical drive, bus fields filled for letter drives that are not network drives.

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/VolumeTests.cs`:

```csharp
namespace UasSort.Platform.Tests;

public sealed class VolumeTests
{
    [Fact]
    public void Provider_ReportsTheSystemVolume()
    {
        var system = KnownFolders.SystemVolumeRoot();
        var v = Assert.Single(new WindowsVolumeProvider().GetVolumes(), v => string.Equals(v.Root, system, StringComparison.OrdinalIgnoreCase));
        Assert.True(v.IsReady);
        Assert.True(v.IsSystemBootOrPaging);
        Assert.Equal("Fixed", v.DriveType);
        Assert.NotEqual(0u, v.Identity.VolumeSerial);
        Assert.True(v.Identity.TotalBytes > 0 && v.FreeBytes > 0);
        Assert.Equal(v.Identity.FileSystem == "NTFS", v.IsNtfs);
        Assert.False(string.IsNullOrEmpty(v.BusType));
    }

    [Fact]
    public void VolumePathName_OfATempFolder_IsItsDriveRoot()
    {
        using var t = new TempDir();
        Assert.Equal(Path.GetPathRoot(t.Path)!.ToUpperInvariant(), VolumeQuery.VolumePathName(t.Path)!.ToUpperInvariant());
        Assert.NotEqual(t.Path, VolumeQuery.VolumePathName(t.Path));
    }

    [Fact]
    public void Space_And_ClusterSize_OfTheSystemVolume()
    {
        var root = KnownFolders.SystemVolumeRoot();
        var (free, total) = VolumeQuery.Space(root)!.Value;
        Assert.InRange(free, 1, total);
        var cluster = VolumeQuery.ClusterBytes(root);
        Assert.True(cluster >= 512 && (cluster & (cluster - 1)) == 0);
    }

    [Theory]
    [InlineData(7u, "Usb")]
    [InlineData(12u, "Sd")]
    [InlineData(13u, "Mmc")]
    [InlineData(17u, "Nvme")]
    [InlineData(99u, "Unknown")]
    public void BusName_MapsStorageBusType(uint value, string expected) => Assert.Equal(expected, VolumeQuery.BusName(value));

    [Fact]
    public void MissingDrive_HasNoFacts()
    {
        var letter = Cmd.FreeDriveLetter();
        Assert.Null(VolumeQuery.Information($@"{letter}:\"));
        Assert.Null(VolumeQuery.Bus($@"{letter}:\"));
        Assert.False(VolumeQuery.IsSystemBootOrPaging($@"{letter}:\"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*VolumeTests*"`
Expected: build FAILS with CS0246 "The type or namespace name 'WindowsVolumeProvider' could not be found" and CS0103 for `VolumeQuery`.

- [ ] **Step 3: Implement**

`src/UasSort.Platform/Win32/Kernel32.Volumes.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UasSort.Platform.Win32;

[StructLayout(LayoutKind.Sequential)]
internal struct StoragePropertyQuery
{
    public int PropertyId;              // 0 = StorageDeviceProperty
    public int QueryType;               // 0 = PropertyStandardQuery
    public byte AdditionalParameters;
}

internal static unsafe partial class Kernel32
{
    public const uint FILE_READ_ONLY_VOLUME = 0x00080000;
    public const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    public const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumeInformation(string lpRootPathName, char* lpVolumeNameBuffer, uint nVolumeNameSize,
        out uint lpVolumeSerialNumber, out uint lpMaximumComponentLength, out uint lpFileSystemFlags,
        char* lpFileSystemNameBuffer, uint nFileSystemNameSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumePathName(string lpszFileName, char* lpszVolumePathName, uint cchBufferLength);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetDiskFreeSpaceEx(string lpDirectoryName, out ulong lpFreeBytesAvailableToCaller,
        out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetDiskFreeSpace(string lpRootPathName, out uint lpSectorsPerCluster, out uint lpBytesPerSector,
        out uint lpNumberOfFreeClusters, out uint lpTotalNumberOfClusters);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, void* lpInBuffer, uint nInBufferSize,
        void* lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, nint lpOverlapped);
}
```

`src/UasSort.Platform/Io/VolumeQuery.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace UasSort.Platform.Io;

internal sealed record VolumeInformation(uint Serial, string? Label, string FileSystem, uint Flags);

/// <summary>Volume facts read from Win32 without opening any file or any data handle (Ref §4.1 VolumeInfo, §10.6).</summary>
internal static unsafe class VolumeQuery
{
    private static readonly string[] BusNames =
        ["Unknown", "Scsi", "Atapi", "Ata", "1394", "Ssa", "Fibre", "Usb", "Raid", "iScsi", "Sas", "Sata", "Sd", "Mmc",
         "Virtual", "FileBackedVirtual", "Spaces", "Nvme", "Scm", "Ufs"];

    public static string BusName(uint storageBusType) => storageBusType < BusNames.Length ? BusNames[storageBusType] : "Unknown";

    public static VolumeInformation? Information(string volumeRoot)
    {
        var name = stackalloc char[261];
        var fs = stackalloc char[261];
        if (!Kernel32.GetVolumeInformation(WithSeparator(volumeRoot), name, 261, out var serial, out _, out var flags, fs, 261)) return null;
        var label = new string(name);
        return new VolumeInformation(serial, label.Length == 0 ? null : label, new string(fs), flags);
    }

    public static CardIdentity? Identity(string volumeRoot)
        => Information(volumeRoot) is { } i && Space(volumeRoot) is { } s ? new CardIdentity(i.Serial, i.Label, i.FileSystem, s.Total) : null;

    public static string? VolumePathName(string path)
    {
        var buffer = stackalloc char[1024];
        return Kernel32.GetVolumePathName(Path.GetFullPath(path), buffer, 1024) ? LongPath.Strip(new string(buffer)) : null;
    }

    public static (long Free, long Total)? Space(string existingDir)
        => Kernel32.GetDiskFreeSpaceEx(WithSeparator(existingDir), out var free, out var total, out _) ? ((long)free, (long)total) : null;

    public static int ClusterBytes(string volumeRoot)
        => Kernel32.GetDiskFreeSpace(WithSeparator(volumeRoot), out var spc, out var bps, out _, out _)
            ? checked((int)(spc * bps))
            : throw new IOException($"GetDiskFreeSpaceW({volumeRoot}) failed: {Marshal.GetLastPInvokeError()}");

    public static (string BusType, bool RemovableMedia)? Bus(string volumeRoot)
    {
        if (!IsLetterRoot(volumeRoot)) return null;
        using var device = Kernel32.CreateFile(@"\\.\" + volumeRoot[..2], 0 /* no data access */,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE, 0, Kernel32.OPEN_EXISTING, 0, 0);
        if (device.IsInvalid) return null;
        var query = new StoragePropertyQuery();
        var output = stackalloc byte[1024];
        if (!Kernel32.DeviceIoControl(device, Kernel32.IOCTL_STORAGE_QUERY_PROPERTY, &query, (uint)sizeof(StoragePropertyQuery),
                                      output, 1024, out var returned, 0) || returned < 32) return null;
        // STORAGE_DEVICE_DESCRIPTOR: RemovableMedia at byte 10, BusType (STORAGE_BUS_TYPE) at byte 28
        return (BusName(*(uint*)(output + 28)), output[10] != 0);
    }

    public static bool IsSystemBootOrPaging(string volumeRoot)
    {
        var root = WithSeparator(volumeRoot);
        if (Same(root, KnownFolders.SystemVolumeRoot())) return true;
        if (Same(root, Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "")) return true;
        foreach (var name in new[] { "pagefile.sys", "swapfile.sys", "hiberfil.sys" })
        {
            var a = Kernel32.TryGetAttributes(Path.Join(root, name), out var error);
            if (a is not null || error == Kernel32.ERROR_SHARING_VIOLATION) return true;
        }
        using var mm = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
        foreach (var entry in mm?.GetValue("PagingFiles") as string[] ?? [])
            if (entry.Length >= 3 && Same(entry[..3], root)) return true;
        return false;
    }

    private static bool IsLetterRoot(string root) => root.Length is 2 or 3 && char.IsAsciiLetter(root[0]) && root[1] == ':';
    private static string WithSeparator(string root) => root.EndsWith('\\') ? root : root + '\\';
    private static bool Same(string a, string b) => string.Equals(WithSeparator(a), WithSeparator(b), StringComparison.OrdinalIgnoreCase);
}
```

`src/UasSort.Platform/Io/WindowsVolumeProvider.cs`:

```csharp
namespace UasSort.Platform.Io;

public sealed class WindowsVolumeProvider : IVolumeProvider
{
    public IReadOnlyList<VolumeInfo> GetVolumes()
    {
        var list = new List<VolumeInfo>();
#pragma warning disable RS0030 // IO layer: the volume provider is the one place that enumerates drives
        foreach (var drive in DriveInfo.GetDrives())
        {
            var root = drive.Name;
            var type = drive.DriveType.ToString();
            var ready = drive.IsReady;
#pragma warning restore RS0030
            var info = ready ? VolumeQuery.Information(root) : null;
            var space = ready ? VolumeQuery.Space(root) : null;
            var bus = type is "Network" or "CDRom" or "NoRootDirectory" || !ready ? null : VolumeQuery.Bus(root);
            var busType = bus?.BusType ?? "Unknown";
            list.Add(new VolumeInfo(
                Root: root,
                Identity: new CardIdentity(info?.Serial ?? 0, info?.Label, info?.FileSystem ?? "", space?.Total ?? 0),
                DriveType: type,
                IsReady: ready && info is not null,
                IsReadOnlyVolume: info is not null && (info.Flags & Kernel32.FILE_READ_ONLY_VOLUME) != 0,
                IsNtfs: info?.FileSystem == "NTFS",
                IsRemovableBus: busType is "Usb" or "Sd" or "Mmc" or "1394",
                FreeBytes: space?.Free ?? 0,
                BusType: busType,
                RemovableMedia: bus?.RemovableMedia ?? false,
                IsSystemBootOrPaging: VolumeQuery.IsSystemBootOrPaging(root)));
        }
        return list;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*VolumeTests*"`
Expected: PASS, `Passed: 9`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform tests/UasSort.Platform.Tests
git commit -m "feat: volume provider with bus, removable-media and system-volume facts

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 09.6: `WindowsCardReader` and `WindowsCardReaderFactory`

**Files:**
- Create: `src/UasSort.Platform/Card/WindowsCardReader.cs`
- Create: `src/UasSort.Platform/Card/WindowsCardReaderFactory.cs`
- Create: `tests/UasSort.Platform.Tests/CardReaderTests.cs`

**Interfaces:**
- Consumes (Part 02): `ICardReader`, `ICardReaderFactory`, `CardSource`, `CardIdentity`, `CardSpace`, `FsEntry`, `ListingResult`. This part: `IoGate`, `GuardContexts`, `VolumeQuery`, `WindowsDirectoryLister`.
- Produces (defined here):
  - `public sealed class WindowsCardReaderFactory(Settings settings, string appDataDir, string machine, IPathFacts facts, IDirectoryLister lister) : ICardReaderFactory`
  - `public sealed class WindowsCardReader : ICardReader` (internal constructor `(CardSource source, CardIdentity identity, GuardContext ctx, IDirectoryLister lister)`) — every open is `IoGate.Require(IoOp.ReadData, …)` then `FileMode.Open`, `FileAccess.Read`, `FileShare.ReadWrite`, `BufferSize = 0` (Core adds its 4 KB block cache for random reads and 1 MiB buffers for copies); `Stat` and `Space` read metadata only; `CurrentIdentity` throws `IOException` when the volume is gone; public `CardIdentity BoundIdentity { get; }`, `CardSource Source { get; }` and `string Root { get; }`.

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/CardReaderTests.cs`:

```csharp
using System.Security.AccessControl;

namespace UasSort.Platform.Tests;

public sealed class CardReaderTests
{
    private static ICardReader Open(TestEnv env)
    {
        var identity = VolumeQuery.Identity(VolumeQuery.VolumePathName(env.CardRoot)!)!;
        var source = new CardSource(env.C(env.CardRoot), identity, IsBrowsedFolder: false, IsWriteProtected: false);
        return new WindowsCardReaderFactory(env.Settings, env.AppData, TestEnv.Machine, env.Facts, new WindowsDirectoryLister())
            .Open(source, identity);
    }

    [Fact]
    public void Reader_OpensFilesWhoseAclDeniesAllWriteRights()
    {
        using var env = new TestEnv();
        var f = env.Temp.File(@"card\DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", 8192);
        TempDir.Deny(f, FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership);
        var reader = Open(env);
        var expected = File.ReadAllBytes(f);
        using (var s = reader.OpenSequential(@"DCIM\DJI_001\DJI_20260927140627_0128_D.MP4"))
        {
            var got = new byte[8192];
            s.ReadExactly(got);
            Assert.Equal(expected, got);
            Assert.False(s.CanWrite);
        }
        using (var r = reader.OpenRandom("DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"))
        {
            r.Position = 4096;
            Assert.Equal(expected[4096], (byte)r.ReadByte());
        }
    }

    [Fact]
    public void Reader_RefusesPathsOutsideTheCard()
    {
        using var env = new TestEnv();
        env.Temp.File(@"photo\DJI_0001.DNG");
        env.Temp.File("outside.bin");
        var reader = Open(env);
        Assert.Throws<UnsafeIoException>(() => reader.OpenRandom(@"..\photo\DJI_0001.DNG"));
        Assert.Throws<UnsafeIoException>(() => reader.OpenSequential(@"..\outside.bin"));
    }

    [Fact]
    public void Reader_RefusesAPlaceholderOnTheCard()
    {
        using var env = new TestEnv();
        env.Temp.File(@"card\DCIM\DJI_001\x.MP4", attributes: FileAttributes.Offline);
        Assert.Throws<UnsafeIoException>(() => Open(env).OpenRandom(@"DCIM\DJI_001\x.MP4"));
    }

    [Fact]
    public void Stat_ReportsSizeTimesAndAttributes_WithoutOpening()
    {
        using var env = new TestEnv();
        var f = env.Temp.File(@"card\DCIM\DJI_001\a.DNG", 100, FileAttributes.Hidden);
        using var hold = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None);
        var e = Open(env).Stat(@"DCIM\DJI_001\a.DNG");
        Assert.Equal(100, e.Size);
        Assert.Equal(new FileInfo(f).LastWriteTimeUtc, e.MtimeUtc);
        Assert.True((e.RawAttributes & (uint)FileAttributes.Hidden) != 0);
        Assert.Equal(@"DCIM\DJI_001\a.DNG", e.RelPath);
        Assert.Throws<FileNotFoundException>(() => Open(env).Stat(@"DCIM\DJI_001\gone.DNG"));
    }

    [Fact]
    public void Relist_Identity_And_Space()
    {
        using var env = new TestEnv();
        env.Temp.File(@"card\DCIM\DJI_001\.x.MP4.trinf", attributes: FileAttributes.Hidden);
        var reader = Open(env);
        Assert.Contains(reader.Relist().Entries, e => e.RelPath == @"DCIM\DJI_001\.x.MP4.trinf");
        Assert.Equal(VolumeQuery.Identity(VolumeQuery.VolumePathName(env.CardRoot)!)!, reader.CurrentIdentity());
        var space = reader.Space();
        Assert.True(space.FreeBytes > 0 && space.TotalBytes >= space.FreeBytes && space.ClusterBytes >= 512);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*CardReaderTests*"`
Expected: build FAILS with CS0246 "The type or namespace name 'WindowsCardReaderFactory' could not be found".

- [ ] **Step 3: Implement**

`src/UasSort.Platform/Card/WindowsCardReader.cs`:

```csharp
using UasSort.Platform.Io;

namespace UasSort.Platform.Card;

/// <summary>Read-only card access bound to one anchored root and one identity (Ref §4.1, §4.3 Card).</summary>
public sealed class WindowsCardReader : ICardReader
{
    private static readonly IReadOnlySet<string> NoExclusions = new HashSet<string>();
    private readonly GuardContext _ctx;
    private readonly IDirectoryLister _lister;
    private readonly string _volumeRoot;

    internal WindowsCardReader(CardSource source, CardIdentity identity, GuardContext ctx, IDirectoryLister lister)
    {
        _ctx = ctx;
        _lister = lister;
        Root = ctx.CardRoot ?? throw new ArgumentException("the card GuardContext has no CardRoot", nameof(ctx));
        BoundIdentity = identity;
        _volumeRoot = VolumeQuery.VolumePathName(Root) ?? Path.GetPathRoot(Root)!;
        Source = source;
    }

    public CardSource Source { get; }
    public string Root { get; }
    public CardIdentity BoundIdentity { get; }

    public CardIdentity CurrentIdentity()
        => VolumeQuery.Identity(_volumeRoot) ?? throw new IOException($"The card volume {_volumeRoot} is not available");

    public Stream OpenRandom(string cardRelPath) => Open(cardRelPath, FileOptions.RandomAccess);
    public Stream OpenSequential(string cardRelPath) => Open(cardRelPath, FileOptions.SequentialScan);

    public FsEntry Stat(string cardRelPath)
    {
        var full = Resolve(cardRelPath);
        if (!Kernel32.TryGetAttributeData(full, out var d, out var error))
            throw Kernel32.IsNotFound(error) ? new FileNotFoundException("Not on the card", full)
                                             : new IOException($"Stat {full} failed (Win32 error {error})", error);
        return new FsEntry(full, Path.GetRelativePath(Root, full), (d.FileAttributes & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0, d.Size,
                           d.LastWriteUtc, d.CreationUtc, d.LastAccessUtc, d.FileAttributes);
    }

    public ListingResult Relist() => _lister.Enumerate(Root, recurse: true, NoExclusions);

    public CardSpace Space()
    {
        var (free, total) = VolumeQuery.Space(Root) ?? throw new IOException($"The card volume {_volumeRoot} is not available");
        return new CardSpace(free, total, VolumeQuery.ClusterBytes(_volumeRoot));
    }

    private Stream Open(string cardRelPath, FileOptions options)
    {
        var full = Resolve(cardRelPath);
        IoGate.Require(IoOp.ReadData, full, _ctx);
#pragma warning disable RS0030 // IO layer: the card reader; FileAccess.Read only, never a write right (Ref §4.3)
        return new FileStream(full, new FileStreamOptions
        {
            Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite, Options = options, BufferSize = 0,
        });
#pragma warning restore RS0030
    }

    private string Resolve(string cardRelPath) => Path.GetFullPath(Path.Join(Root, cardRelPath.Replace('/', '\\')));
}
```

`src/UasSort.Platform/Card/WindowsCardReaderFactory.cs`:

```csharp
using UasSort.Platform.Io;

namespace UasSort.Platform.Card;

public sealed class WindowsCardReaderFactory(Settings settings, string appDataDir, string machine, IPathFacts facts,
                                             IDirectoryLister lister) : ICardReaderFactory
{
    public ICardReader Open(CardSource source, CardIdentity identity)
        => new WindowsCardReader(source, identity, GuardContexts.For(settings, appDataDir, machine, facts, source.Root), lister);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*CardReaderTests*"`
Expected: PASS, `Passed: 5`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform tests/UasSort.Platform.Tests
git commit -m "feat: read-only windows card reader bound to identity, guarded opens

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 09.7: `GuardedFileOps` over `WindowsFileOps`

**Files:**
- Create: `src/UasSort.Platform/Io/WindowsFileOps.cs`
- Create: `src/UasSort.Platform/Io/GuardedFileOps.cs`
- Create: `tests/UasSort.Platform.Tests/FileOpsTests.cs`

**Interfaces:**
- Consumes (Part 02): `IFileOps`, `VerifyResult` (`HashMatch`, `HashMismatch`), `RenameResult` (`Renamed`, `TargetExists`), `VerifyMode`, `IoOp`, `IoGuardPolicy.TempSuffix`. (Part 07, contract only): the set from `OffloadCompiler.NewFolderDirs(OffloadBatch, string videoRoot)`, handed over by `CommitEnvironment.FileOpsForRun`. This part: `IoGate`, `GuardContexts`, `VolumeQuery`, `Kernel32`, `LongPath`.
- Produces (defined here):
  - `internal sealed class WindowsFileOps(bool allowUnbuffered = true)` — raw operations on paths the caller has already cleared with the guard: `FileStream CreateNew(string path, long size)`, `void MarkTemp(FileStream s)`, `(UInt128 Hash, VerifyMode Mode) Hash(string path, CancellationToken ct)`, `void SetTimesAndClearHidden(string path, uint attributes, DateTime creationUtc, DateTime mtimeUtc)`, `int Move(string from, string to)` (0 or the Win32 error), `void FlushFile(string path)`, `void FlushDirectory(string dir)`, `void Delete(string path)`, `void CreateDirectory(string dir)`, `bool ExactNameExists(string path)`.
  - `public sealed class GuardedFileOps : IFileOps` — `public GuardedFileOps(Settings settings, string appDataDir, string machine, IPathFacts facts, IReadOnlySet<string> newFolderDirs)`, one per Commit. `newFolderDirs` is the run's `GuardContext.NewFolderDirs` taken verbatim (decision 27): each entry canonicalised into an `OrdinalIgnoreCase` set by `GuardContexts.For`, with no ancestor expansion — the caller passes the full set, which `OffloadCompiler.NewFolderDirs(batch, videoRoot)` (Part 07) already builds with every `YYYY`, `YYYY-MM`, event and set folder. The composition root (Part 11) builds it through `CommitEnvironment.FileOpsForRun`; `CommitSession.Begin` (Part 07) builds one instance per Commit from that set and uses it for both Preflight and the copy. It tracks `OwnTempsThisRun` and `RenamedThisRun` itself. The internal constructor adds a `WindowsFileOps` (tests force the buffered fallback). Rename is accepted only from `X.uas-sort.tmp` to `X` in the same directory.

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/FileOpsTests.cs`:

```csharp
using System.IO.Hashing;

namespace UasSort.Platform.Tests;

public sealed class FileOpsTests
{
    private static readonly DateTime Creation = new(2026, 9, 27, 18, 6, 27, DateTimeKind.Utc);
    private static readonly DateTime Mtime = new(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc);

    private static (GuardedFileOps Ops, string Folder) Setup(TestEnv env, string desc = "Zachar Bay", bool unbuffered = true)
    {
        var year = Path.Join(env.VideoRoot, "2026");
        var month = Path.Join(year, "2026-09");
        var folder = Path.Join(month, $"2026-09-27 {desc}");
        // The run's full NewFolderDirs set, as OffloadCompiler.NewFolderDirs builds it: YYYY, YYYY-MM and the event folder.
        var newDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { year, month, folder };
        var ops = new GuardedFileOps(env.Settings, env.AppData, TestEnv.Machine, env.Facts, newDirs, new WindowsFileOps(unbuffered));
        return (ops, folder);
    }

    private static byte[] Data(int n) { var b = new byte[n]; new Random(n).NextBytes(b); return b; }

    private static string CopyOne(GuardedFileOps ops, string folder, string name, byte[] data, out VerifyResult verify)
    {
        ops.EnsureDirectory(folder, allowCreate: true);
        var final = Path.Join(folder, name);
        string temp;
        using (var s = ops.CreateTemp(final, data.Length, out temp))
        {
            Assert.EndsWith(".uas-sort.tmp", temp, StringComparison.Ordinal);
            Assert.True((File.GetAttributes(temp) & (FileAttributes.Hidden | FileAttributes.NotContentIndexed))
                        == (FileAttributes.Hidden | FileAttributes.NotContentIndexed));
            s.Write(data);
            ops.FlushToDisk(s);
        }
        verify = ops.VerifyHash(temp, data.Length, XxHash128.HashToUInt128(data), CancellationToken.None);
        ops.FinalizeAttributes(temp, Creation, Mtime);
        Assert.True(ops.RenameNoReplace(temp, final) is Renamed);
        return final;
    }

    [Fact]
    public void FullProtocol_UnbufferedVerify_TimesCopied_HiddenCleared_Confirmed_Flushed()
    {
        using var env = new TestEnv();
        var (ops, folder) = Setup(env);
        var data = Data(1_500_000);
        var final = CopyOne(ops, folder, "DJI_20260927140627_0128_D.MP4", data, out var verify);
        Assert.True(verify is HashMatch { Mode: VerifyMode.Unbuffered });
        Assert.Equal(data, File.ReadAllBytes(final));
        Assert.Equal(Creation, File.GetCreationTimeUtc(final));
        Assert.Equal(Mtime, File.GetLastWriteTimeUtc(final));
        Assert.Equal((FileAttributes)0, File.GetAttributes(final) & FileAttributes.Hidden);
        Assert.True(ops.ConfirmFinal(final, data.Length));
        Assert.False(ops.ConfirmFinal(final, data.Length + 1));
        Assert.False(ops.ConfirmFinal(final.ToLowerInvariant(), data.Length));   // the name must match exactly
        ops.FlushDestination(folder, [final]);                                     // file + directory flush on NTFS
        Assert.True(Directory.Exists(Path.Join(env.VideoRoot, "2026", "2026-09")));
    }

    [Fact]
    public void Verify_Mismatch_ReportsTheHashItGot()
    {
        using var env = new TestEnv();
        var (ops, folder) = Setup(env);
        ops.EnsureDirectory(folder, true);
        var data = Data(10_000);
        string temp;
        using (var s = ops.CreateTemp(Path.Join(folder, "a.MP4"), data.Length, out temp))
        {
            s.Write(data);
            ops.FlushToDisk(s);
        }
        var r = ops.VerifyHash(temp, data.Length, UInt128.One, CancellationToken.None);
        Assert.True(r is HashMismatch { Mode: VerifyMode.Unbuffered } m && m.Got == XxHash128.HashToUInt128(data));
        ops.DeleteOwnTemp(temp);
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void Verify_BufferedFallback_IsRecordedAsCached()
    {
        using var env = new TestEnv();
        var (ops, folder) = Setup(env, unbuffered: false);
        CopyOne(ops, folder, "b.DNG", Data(5000), out var verify);
        Assert.True(verify is HashMatch { Mode: VerifyMode.Cached });
    }

    [Fact]
    public void Rename_NeverReplaces_ReturnsTargetExists()
    {
        using var env = new TestEnv();
        var (ops, folder) = Setup(env);
        ops.EnsureDirectory(folder, true);
        var final = Path.Join(folder, "c.MP4");
        File.WriteAllText(final, "existing");
        string temp;
        using (var s = ops.CreateTemp(final, 3, out temp)) s.Write([1, 2, 3]);
        ops.FinalizeAttributes(temp, Creation, Mtime);
        Assert.True(ops.RenameNoReplace(temp, final) is TargetExists);
        Assert.Equal("existing", File.ReadAllText(final));
        Assert.True(File.Exists(temp));
        ops.DeleteOwnTemp(temp);
    }

    [Fact]
    public void Path300Characters_RoundTrip()
    {
        using var env = new TestEnv();
        var (ops, folder) = Setup(env, desc: new string('x', 220));
        var final = CopyOne(ops, folder, "DJI_20260927140627_0128_D.MP4", Data(70_000), out var verify);
        Assert.True(final.Length > 300, $"path is only {final.Length} characters");
        Assert.True(verify is HashMatch);
        Assert.True(ops.ConfirmFinal(final, 70_000));
        Assert.True(ops.TryGetSize(final, out var size) && size == 70_000);
    }

    [Fact]
    public void Preallocation_BeyondFreeSpace_FailsAtCreate()
    {
        using var env = new TestEnv();
        var (ops, folder) = Setup(env);
        ops.EnsureDirectory(folder, true);
        var tooBig = ops.FreeBytes(folder) + (10L << 30);
        Assert.ThrowsAny<IOException>(() => ops.CreateTemp(Path.Join(folder, "d.MP4"), tooBig, out _));
    }

    [Fact]
    public void Refusals_FollowThePolicy()
    {
        using var env = new TestEnv();
        var (ops, folder) = Setup(env);
        var existing = env.Temp.File(@"video\2026\2026-08\2026-08-01 Anvil\DJI_0001.MP4");
        Assert.Throws<UnsafeIoException>(() => ops.CreateTemp(Path.Join(env.Temp.Path, "elsewhere", "x.MP4"), 1, out _));
        Assert.Throws<UnsafeIoException>(() => ops.VerifyHash(existing, 16, UInt128.Zero, CancellationToken.None));
        Assert.Throws<UnsafeIoException>(() => ops.DeleteOwnTemp(existing));
        Assert.Throws<UnsafeIoException>(() => ops.EnsureDirectory(Path.Join(env.VideoRoot, "2026", "2026-10", "not planned"), true));
        Assert.Throws<DirectoryNotFoundException>(() => ops.EnsureDirectory(Path.Join(env.VideoRoot, "gone append target"), false));
        ops.EnsureDirectory(folder, true);
        using (ops.CreateTemp(Path.Join(folder, "e.MP4"), 1, out _)) { }
        Assert.Throws<UnsafeIoException>(() => ops.RenameNoReplace(Path.Join(folder, "e.MP4.uas-sort.tmp"), Path.Join(folder, "other.MP4")));
    }

    [Fact]
    public void NewFolderDirs_AreUsedVerbatim_NoAncestorIsAdded()
    {
        using var env = new TestEnv();
        var folder = Path.Join(env.VideoRoot, "2026", "2026-09", "2026-09-27 Zachar Bay");
        var onlyTheFolder = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { folder };
        var ops = new GuardedFileOps(env.Settings, env.AppData, TestEnv.Machine, env.Facts, onlyTheFolder, new WindowsFileOps());
        Assert.Throws<UnsafeIoException>(() => ops.EnsureDirectory(folder, allowCreate: true));   // 2026 is not in the set
        Assert.False(Directory.Exists(Path.Join(env.VideoRoot, "2026")));
    }

    [Fact]
    public void StaleTemp_InAnAppendFolder_CanBeDeleted()
    {
        using var env = new TestEnv();
        var (ops, _) = Setup(env);
        var stale = env.Temp.File(@"video\2026\2026-08\2026-08-01 Anvil\DJI_0002.MP4.uas-sort.tmp", attributes: FileAttributes.Hidden);
        ops.EnsureDirectory(Path.GetDirectoryName(stale)!, allowCreate: false);
        ops.DeleteOwnTemp(stale);
        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void TryGetSize_And_FreeBytes()
    {
        using var env = new TestEnv();
        var (ops, _) = Setup(env);
        Assert.False(ops.TryGetSize(Path.Join(env.VideoRoot, "none.MP4"), out _));
        Assert.True(ops.FreeBytes(Path.Join(env.VideoRoot, "not", "yet", "there")) > 0);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*FileOpsTests*"`
Expected: build FAILS with CS0246 "The type or namespace name 'GuardedFileOps' could not be found" and CS0246 for `WindowsFileOps`.

- [ ] **Step 3: Implement**

`src/UasSort.Platform/Io/WindowsFileOps.cs`:

```csharp
using System.Buffers;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UasSort.Platform.Io;

/// <summary>Raw destination writes (Ref §10.3). Callers clear every path with IoGate first; nothing here decides safety.</summary>
internal sealed class WindowsFileOps(bool allowUnbuffered = true)
{
    private const int Chunk = 1 << 20;
    private const FileOptions NoBuffering = (FileOptions)0x20000000;

#pragma warning disable RS0030 // IO layer: GuardedFileOps is the only caller; each path was cleared by IoGuardPolicy
    public FileStream CreateNew(string path, long size) => new(path, new FileStreamOptions
    {
        Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, PreallocationSize = size, BufferSize = 0,
    });

    public void MarkTemp(FileStream s)
        => File.SetAttributes(s.SafeFileHandle, FileAttributes.Hidden | FileAttributes.NotContentIndexed);

    public (UInt128 Hash, VerifyMode Mode) Hash(string path, CancellationToken ct)
    {
        if (allowUnbuffered)
        {
            try { return (HashUnbuffered(path, ct), VerifyMode.Unbuffered); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return (HashBuffered(path, ct), VerifyMode.Cached);
    }

    private static unsafe UInt128 HashUnbuffered(string path, CancellationToken ct)
    {
        SafeFileHandle handle;
        try { handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, NoBuffering); }
        catch (ArgumentOutOfRangeException)
        {
            handle = Kernel32.CreateFile(LongPath.Prefix(path), Kernel32.GENERIC_READ, Kernel32.FILE_SHARE_READ, 0,
                                         Kernel32.OPEN_EXISTING, Kernel32.FILE_FLAG_NO_BUFFERING, 0);
            if (handle.IsInvalid) throw new IOException($"CreateFileW(NO_BUFFERING) failed: {Marshal.GetLastPInvokeError()}");
        }
        using (handle)
        {
            var buffer = NativeMemory.AlignedAlloc(Chunk, 4096);
            try
            {
                var hasher = new XxHash128();
                var span = new Span<byte>(buffer, Chunk);
                for (long offset = 0; ; offset += Chunk)
                {
                    ct.ThrowIfCancellationRequested();
                    var n = RandomAccess.Read(handle, span, offset);
                    hasher.Append(span[..n]);
                    if (n < Chunk) break;           // the next unbuffered read would start at an unaligned offset
                }
                return hasher.GetCurrentHashAsUInt128();
            }
            finally { NativeMemory.AlignedFree(buffer); }
        }
    }

    private static UInt128 HashBuffered(string path, CancellationToken ct)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(Chunk);
        try
        {
            var hasher = new XxHash128();
            long offset = 0;
            int n;
            while ((n = RandomAccess.Read(handle, buffer.AsSpan(0, Chunk), offset)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                hasher.Append(buffer.AsSpan(0, n));
                offset += n;
            }
            return hasher.GetCurrentHashAsUInt128();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    public void SetTimesAndClearHidden(string path, uint attributes, DateTime creationUtc, DateTime mtimeUtc)
    {
        File.SetCreationTimeUtc(path, creationUtc);
        File.SetLastWriteTimeUtc(path, mtimeUtc);
        var cleared = attributes & ~Kernel32.FILE_ATTRIBUTE_HIDDEN;
        if (!Kernel32.SetFileAttributes(LongPath.Prefix(path), cleared == 0 ? 0x80u /* NORMAL */ : cleared))
            throw new IOException($"SetFileAttributesW({path}) failed: {Marshal.GetLastPInvokeError()}");
    }

    public int Move(string from, string to)
        => Kernel32.MoveFileEx(LongPath.Prefix(from), LongPath.Prefix(to), Kernel32.MOVEFILE_WRITE_THROUGH) ? 0 : Marshal.GetLastPInvokeError();

    public void FlushFile(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        if (!Kernel32.FlushFileBuffers(handle)) throw new IOException($"FlushFileBuffers({path}) failed: {Marshal.GetLastPInvokeError()}");
    }

    public void FlushDirectory(string dir)
    {
        using var handle = Kernel32.CreateFile(LongPath.Prefix(dir), Kernel32.GENERIC_WRITE,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE | Kernel32.FILE_SHARE_DELETE, 0, Kernel32.OPEN_EXISTING,
            Kernel32.FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (handle.IsInvalid) throw new IOException($"Opening {dir} for flush failed: {Marshal.GetLastPInvokeError()}");
        if (!Kernel32.FlushFileBuffers(handle)) throw new IOException($"FlushFileBuffers({dir}) failed: {Marshal.GetLastPInvokeError()}");
    }

    public void Delete(string path) => File.Delete(path);

    public void CreateDirectory(string dir) => Directory.CreateDirectory(dir);

    public bool ExactNameExists(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        return Directory.EnumerateFiles(dir, name, new EnumerationOptions { AttributesToSkip = 0, MatchCasing = MatchCasing.CaseInsensitive })
                        .Any(p => string.Equals(Path.GetFileName(p), name, StringComparison.Ordinal));
    }
#pragma warning restore RS0030
}
```

`src/UasSort.Platform/Io/GuardedFileOps.cs`:

```csharp
using System.Runtime.InteropServices;

namespace UasSort.Platform.Io;

/// <summary>IFileOps for one Commit: every create, read-back, attribute change, rename, flush and delete asks IoGuardPolicy first.</summary>
public sealed class GuardedFileOps : IFileOps
{
    private const string TempSuffix = IoGuardPolicy.TempSuffix;
    private readonly IPathFacts _facts;
    private readonly WindowsFileOps _inner;
    private readonly HashSet<string> _ownTemps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _renamed = new(StringComparer.OrdinalIgnoreCase);
    private readonly GuardContext _ctx;

    /// <param name="newFolderDirs">The run's GuardContext.NewFolderDirs (OffloadCompiler.NewFolderDirs), used verbatim; empty for Preflight.</param>
    public GuardedFileOps(Settings settings, string appDataDir, string machine, IPathFacts facts, IReadOnlySet<string> newFolderDirs)
        : this(settings, appDataDir, machine, facts, newFolderDirs, new WindowsFileOps()) { }

    internal GuardedFileOps(Settings settings, string appDataDir, string machine, IPathFacts facts,
                            IReadOnlySet<string> newFolderDirs, WindowsFileOps inner)
    {
        _facts = facts;
        _inner = inner;
        // Decision 27: the set is taken as given. GuardContexts.For canonicalises each entry into an OrdinalIgnoreCase set;
        // no ancestor is added here (the caller's set already lists every YYYY and YYYY-MM folder the run creates).
        _ctx = GuardContexts.For(settings, appDataDir, machine, facts, cardRoot: null, newFolderDirs, _ownTemps, _renamed);
    }

    public Stream CreateTemp(string finalPath, long size, out string tempPath)
    {
        tempPath = Canon(finalPath) + TempSuffix;
        IoGate.Require(IoOp.CreateNew, tempPath, _ctx);
        var stream = _inner.CreateNew(tempPath, size);
        _ownTemps.Add(tempPath);
        try
        {
            IoGate.Require(IoOp.SetAttributesOrTimes, tempPath, _ctx);
            _inner.MarkTemp(stream);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    public void FlushToDisk(Stream s)
    {
        if (s is not FileStream fs) throw new ArgumentException("not a stream from CreateTemp", nameof(s));
        fs.Flush();
        if (!Kernel32.FlushFileBuffers(fs.SafeFileHandle))
            throw new IOException($"FlushFileBuffers failed: {Marshal.GetLastPInvokeError()}");
    }

    public VerifyResult VerifyHash(string tempPath, long size, UInt128 expected, CancellationToken ct)
    {
        var path = Canon(tempPath);
        IoGate.Require(IoOp.ReadData, path, _ctx);
        var (got, mode) = _inner.Hash(path, ct);
        return got == expected ? new HashMatch(mode) : new HashMismatch(got, mode);
    }

    public void FinalizeAttributes(string tempPath, DateTime creationUtc, DateTime mtimeUtc)
    {
        var path = Canon(tempPath);
        var attributes = IoGate.Require(IoOp.SetAttributesOrTimes, path, _ctx)
                         ?? throw new FileNotFoundException("temp file missing", path);
        _inner.SetTimesAndClearHidden(path, attributes, creationUtc, mtimeUtc);
    }

    public RenameResult RenameNoReplace(string tempPath, string finalPath)
    {
        var from = Canon(tempPath);
        var to = Canon(finalPath);
        if (!string.Equals(from, to + TempSuffix, StringComparison.OrdinalIgnoreCase))
            throw new UnsafeIoException($"Refused rename {from} → {to}: a temp may only take its own final name in its own folder");
        IoGate.Require(IoOp.Rename, from, _ctx);
        var error = _inner.Move(from, to);
        if (error is Kernel32.ERROR_FILE_EXISTS or Kernel32.ERROR_ALREADY_EXISTS) return new TargetExists();
        if (error != 0) throw new IOException($"MoveFileExW({from}) failed: {error}", error);
        _ownTemps.Remove(from);
        _renamed.Add(to);
        return new Renamed();
    }

    public bool ConfirmFinal(string finalPath, long size)
    {
        var path = Path.GetFullPath(finalPath);
        return Kernel32.TryGetAttributeData(path, out var d, out _)
               && (d.FileAttributes & Kernel32.FILE_ATTRIBUTE_DIRECTORY) == 0
               && d.Size == size
               && _inner.ExactNameExists(path);
    }

    public void FlushDestination(string dir, IReadOnlyList<string> filesCreatedThisRun)
    {
        foreach (var file in filesCreatedThisRun)
        {
            var path = Canon(file);
            IoGate.Require(IoOp.OpenForFlush, path, _ctx);
            _inner.FlushFile(path);
        }
        var d = Canon(dir);
        IoGate.Require(IoOp.OpenForFlush, d, _ctx);
        _inner.FlushDirectory(d);
    }

    public void DeleteOwnTemp(string tempPath)
    {
        var path = Canon(tempPath);
        if (!path.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase))
            throw new UnsafeIoException($"Refused delete of {path}: only *{TempSuffix} files are ever deleted");
        IoGate.Require(IoOp.Delete, path, _ctx);
        _inner.Delete(path);
        _ownTemps.Remove(path);
    }

    public void EnsureDirectory(string dir, bool allowCreate)
    {
        var target = Canon(dir);
        var missing = new Stack<string>();
        for (var d = target; ; d = Path.GetDirectoryName(d)!)
        {
            var attributes = Kernel32.TryGetAttributes(d, out var error);
            if (attributes is uint a)
            {
                if ((a & Kernel32.FILE_ATTRIBUTE_DIRECTORY) == 0) throw new IOException($"{d} exists and is not a folder");
                break;
            }
            if (!Kernel32.IsNotFound(error)) throw new IOException($"{d} can't be read (Win32 error {error})", error);
            if (!allowCreate) throw new DirectoryNotFoundException($"{target} no longer exists (renamed or moved since the scan)");
            missing.Push(d);
            if (Path.GetDirectoryName(d) is null) throw new DirectoryNotFoundException($"{d}: the volume is missing");
        }
        foreach (var d in missing)               // shallowest first
        {
            IoGate.Require(IoOp.CreateDir, d, _ctx);
            _inner.CreateDirectory(d);
        }
    }

    public bool TryGetSize(string path, out long size)
    {
        size = 0;
        if (!Kernel32.TryGetAttributeData(Path.GetFullPath(path), out var d, out _) || (d.FileAttributes & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0)
            return false;
        size = d.Size;
        return true;
    }

    public long FreeBytes(string anyPathOnVolume)
    {
        for (var d = Path.GetFullPath(anyPathOnVolume); d is not null; d = Path.GetDirectoryName(d))
            if (Kernel32.TryGetAttributes(d, out _) is uint a && (a & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0)
                return VolumeQuery.Space(d)?.Free ?? throw new IOException($"GetDiskFreeSpaceExW({d}) failed");
        throw new DirectoryNotFoundException($"No existing folder on the volume of {anyPathOnVolume}");
    }

    private string Canon(string path) => _facts.Canonical(path);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*FileOpsTests*"`
Expected: PASS, `Passed: 10`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform tests/UasSort.Platform.Tests
git commit -m "feat: guarded file ops with CreateNew temps, unbuffered verify, no-replace rename and flushes

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 09.8: `LedgerStore` — `Check()` and `Load()`

**Files:**
- Create: `src/UasSort.Platform/Win32/Advapi32.cs`
- Create: `src/UasSort.Platform/Io/AccessProbe.cs`
- Create: `src/UasSort.Platform/Ledger/LedgerStore.cs`
- Create: `tests/UasSort.Platform.Tests/LedgerRecords.cs`
- Create: `tests/UasSort.Platform.Tests/LedgerStoreReadTests.cs`

**Interfaces:**
- Consumes (Part 02): `LedgerPaths` (`For`, `OwnFile`, `BackupDir`, `FolderName`), `LedgerFolderStatus`, `LedgerFolderState`, `LedgerSnapshot`, `FsEntry`, `ListingResult`, `LedgerRecord`, `RunRecord`, `RunCard`, `RunRoots`. (Part 05): `LedgerFolderFacts`, `LedgerFolderStatusBuilder.Build`, `LedgerLoader.Load`; `LedgerCodec.Serialize` (test helper). This part: `IoGate`, `GuardContexts`, `WindowsDirectoryLister`, `PathFacts`.
- Produces (defined here):
  - `internal static class AccessProbe { public const uint FILE_ADD_FILE = 0x2, FILE_APPEND_DATA = 0x4; public static bool Has(string path, uint desiredAccess); }` — `GetFileSecurityW` (reads the security descriptor, never data) + `AccessCheck` against the process token.
  - `public sealed partial class LedgerStore` with `public LedgerStore(Settings settings, string appDataDir, string machine, IPathFacts facts, IDirectoryLister lister, TimeProvider clock)`, `public string Folder { get; }` (`LedgerPaths.For(canonical video root)`), `public string OwnFile { get; }` (`LedgerPaths.OwnFile(canonical video root, machine)`), `public string BackupDir { get; }`, `public LedgerFolderStatus Check()`, `public LedgerSnapshot Load()`. `ILedgerStore` is completed in Task 09.9.
  - `Check()` gathers only facts and lets Core decide (decision 37): it builds `LedgerFolderFacts` — `VideoRootExists` (the canonical video root is a folder), `Folder` (the `.uas-sort` folder's `FsEntry` from `GetFileAttributesExW`; null when missing), `TopLevel` (`WindowsDirectoryLister.Enumerate(folder, recurse: false, {})`; null when missing), `InSyncRoot` (`IPathFacts.InSyncRoot` of the folder, else of the video root), `Writable` via `AccessProbe` (`FILE_ADD_FILE` on the folder, on the video root when the folder is missing, and, when the own file exists, no ReadOnly attribute and `FILE_APPEND_DATA` on it) — and returns `LedgerFolderStatusBuilder.Build(videoRoot, machine, facts)`, which picks the top-level `ledger*.jsonl` files, the cloud-only ones, the other machines' files and the most severe state (Ref §3, §11). No ledger file is opened.
  - `Load()` returns `LedgerLoader.Load(Check(), openRead)`. `openRead` canonicalises the path, calls `IoGate.Require(IoOp.ReadData, …)` (a cloud-only file throws `CloudOnlyFileException` before any open) and opens it with `FileAccess.Read` and `FileShare.ReadWrite`. `LedgerLoader` opens nothing for `Missing`, `VideoRootMissing` or `CloudOnly`, so a cloud-only `ledger-B` opens none of the ledger files (Ref §13 tripwire).

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/LedgerRecords.cs`:

```csharp
namespace UasSort.Platform.Tests;

internal static class LedgerRecords
{
    public static RunRecord Run(string machine, string run, string videoRoot = @"C:\v", string photoRoot = @"C:\p")
        => new(1, Guid.NewGuid().ToString("N"), machine, run,
               new DateTime(2026, 9, 27, 21, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 27, 21, 30, 0, DateTimeKind.Utc), "0.1.0",
               new RunCard("1A2B3C4D", null, "exFAT", "FC9113", "9f3c0a6d12e4b7a1"), new RunRoots(videoRoot, photoRoot),
               "Safe", ImmutableDictionary<string, int>.Empty.Add("VerifiedThisRun", 1));

    public static string Line(LedgerRecord r) => LedgerCodec.Serialize(r);

    public static void Write(string path, params LedgerRecord[] records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Concat(records.Select(r => Line(r) + "\n")));
    }
}
```

`tests/UasSort.Platform.Tests/LedgerStoreReadTests.cs`:

```csharp
using System.Security.AccessControl;

namespace UasSort.Platform.Tests;

public sealed class LedgerStoreReadTests
{
    internal static LedgerStore Store(TestEnv env)
        => new(env.Settings, env.AppData, TestEnv.Machine, env.Facts, new WindowsDirectoryLister(),
               new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 21, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void Check_VideoRootMissing()
    {
        using var env = new TestEnv();
        Directory.Delete(env.VideoRoot);
        Assert.Equal(LedgerFolderState.VideoRootMissing, Store(env).Check().State);
    }

    [Fact]
    public void Check_MissingFolder_IsWritableAndNotPinned()
    {
        using var env = new TestEnv();
        var s = Store(env).Check();
        Assert.Equal(LedgerFolderState.Missing, s.State);
        Assert.False(s.Exists);
        Assert.True(s.Writable);
        Assert.Equal(env.C(Path.Join(env.VideoRoot, ".uas-sort")), s.Folder);
    }

    [Fact]
    public void Check_EmptyFolder()
    {
        using var env = new TestEnv();
        env.Temp.Sub("video", ".uas-sort");
        env.Temp.File(@"video\.uas-sort\notes.txt");
        Assert.Equal(LedgerFolderState.Empty, Store(env).Check().State);
    }

    [Fact]
    public void Check_Ok_ListsTopLevelLedgersAndOtherMachines()
    {
        using var env = new TestEnv();
        foreach (var n in new[] { "ledger-TESTPC.jsonl", "ledger-B.jsonl", "LEDGER-B-DESKTOP-A.JSONL", "notes.txt", @"sub\ledger-C.jsonl" })
            env.Temp.File(@"video\.uas-sort\" + n);
        var s = Store(env).Check();
        Assert.Equal(LedgerFolderState.Ok, s.State);
        Assert.Equal(["LEDGER-B-DESKTOP-A.JSONL", "ledger-B.jsonl", "ledger-TESTPC.jsonl"],
                     s.LedgerFiles.Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(2, s.OtherMachineFiles.Length);
        Assert.Empty(s.CloudOnlyFiles);
        Assert.False(s.InSyncRoot);
    }

    [Fact]
    public void Check_CloudOnlyLedger_IsBlockingState()
    {
        using var env = new TestEnv();
        env.Temp.File(@"video\.uas-sort\ledger-TESTPC.jsonl");
        var b = env.Temp.File(@"video\.uas-sort\ledger-B.jsonl", attributes: FileAttributes.Offline);
        var s = Store(env).Check();
        Assert.Equal(LedgerFolderState.CloudOnly, s.State);
        Assert.Equal(env.C(b), Assert.Single(s.CloudOnlyFiles), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_DenyAddFile_IsUnwritable()
    {
        using var env = new TestEnv();
        var folder = env.Temp.Sub("video", ".uas-sort");
        env.Temp.File(@"video\.uas-sort\ledger-B.jsonl");
        TempDir.Deny(folder, FileSystemRights.CreateFiles);
        var s = Store(env).Check();
        Assert.False(s.Writable);
        Assert.Equal(LedgerFolderState.Unwritable, s.State);
    }

    [Fact]
    public void Load_ReadsTopLevelLedgers_NeverOpensAnythingElse()
    {
        using var env = new TestEnv();
        var dir = Path.Join(env.VideoRoot, ".uas-sort");
        LedgerRecords.Write(Path.Join(dir, "ledger-TESTPC.jsonl"), LedgerRecords.Run("TESTPC", "run-a"));
        LedgerRecords.Write(Path.Join(dir, "ledger-B.jsonl"), LedgerRecords.Run("B", "run-b"));
        LedgerRecords.Write(Path.Join(dir, "sub", "ledger-C.jsonl"), LedgerRecords.Run("C", "run-c"));
        var notes = env.Temp.File(@"video\.uas-sort\notes.txt");
        using var lockNotes = new FileStream(notes, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var lockSub = new FileStream(Path.Join(dir, "sub", "ledger-C.jsonl"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var snapshot = Store(env).Load();
        Assert.Equal(["run-a", "run-b"], snapshot.Runs.Select(r => r.Run).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(LedgerFolderState.Ok, snapshot.Status.State);
        Assert.Equal(2, snapshot.SourceFiles.Length);
    }

    [Fact]
    public void Load_WithACloudOnlyLedger_OpensNoLedgerFile()
    {
        using var env = new TestEnv();
        var own = Path.Join(env.VideoRoot, ".uas-sort", "ledger-TESTPC.jsonl");
        LedgerRecords.Write(own, LedgerRecords.Run("TESTPC", "run-a"));
        env.Temp.File(@"video\.uas-sort\ledger-B.jsonl", attributes: FileAttributes.Offline);   // cloud-only: listed, never opened
        using var lockOwn = new FileStream(own, FileMode.Open, FileAccess.ReadWrite, FileShare.None);   // any open of it would throw
        var snapshot = Store(env).Load();
        Assert.Equal(LedgerFolderState.CloudOnly, snapshot.Status.State);
        Assert.Empty(snapshot.Runs);
        Assert.Empty(snapshot.SourceFiles);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*LedgerStoreReadTests*"`
Expected: build FAILS with CS0246 "The type or namespace name 'LedgerStore' could not be found".

- [ ] **Step 3: Implement**

`src/UasSort.Platform/Win32/Advapi32.cs`:

```csharp
using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

[StructLayout(LayoutKind.Sequential)]
internal struct GenericMapping
{
    public uint GenericRead, GenericWrite, GenericExecute, GenericAll;
}

internal static unsafe partial class Advapi32
{
    public const uint OWNER_GROUP_DACL = 0x1 | 0x2 | 0x4;
    public const uint TOKEN_DUPLICATE = 0x2, TOKEN_QUERY = 0x8;
    public const int SecurityImpersonation = 2;

    [LibraryImport("advapi32.dll", EntryPoint = "GetFileSecurityW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileSecurity(string lpFileName, uint requestedInformation, byte* pSecurityDescriptor, uint nLength,
                                               out uint lpnLengthNeeded);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenProcessToken", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", EntryPoint = "DuplicateToken", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DuplicateToken(nint existingTokenHandle, int impersonationLevel, out nint duplicateTokenHandle);

    [LibraryImport("advapi32.dll", EntryPoint = "AccessCheck", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AccessCheck(byte* pSecurityDescriptor, nint clientToken, uint desiredAccess, ref GenericMapping genericMapping,
                                           byte* privilegeSet, ref uint privilegeSetLength, out uint grantedAccess,
                                           [MarshalAs(UnmanagedType.Bool)] out bool accessStatus);
}

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);
}
```

`src/UasSort.Platform/Io/AccessProbe.cs`:

```csharp
namespace UasSort.Platform.Io;

/// <summary>Effective access from the security descriptor only (Ref §11 Ledger folder status); no data handle is opened.</summary>
internal static unsafe class AccessProbe
{
    public const uint FILE_ADD_FILE = 0x2, FILE_APPEND_DATA = 0x4;

    public static bool Has(string path, uint desiredAccess)
    {
        var prefixed = LongPath.Prefix(Path.GetFullPath(path));
        Advapi32.GetFileSecurity(prefixed, Advapi32.OWNER_GROUP_DACL, null, 0, out var needed);
        if (needed == 0) return false;
        var descriptor = new byte[needed];
        nint process = 0, token = 0;
        try
        {
            fixed (byte* sd = descriptor)
            {
                if (!Advapi32.GetFileSecurity(prefixed, Advapi32.OWNER_GROUP_DACL, sd, needed, out _)) return false;
                if (!Advapi32.OpenProcessToken(Kernel32.GetCurrentProcess(), Advapi32.TOKEN_DUPLICATE | Advapi32.TOKEN_QUERY, out process)) return false;
                if (!Advapi32.DuplicateToken(process, Advapi32.SecurityImpersonation, out token)) return false;
                var mapping = new GenericMapping { GenericRead = 0x120089, GenericWrite = 0x120116, GenericExecute = 0x1200A0, GenericAll = 0x1F01FF };
                var privileges = stackalloc byte[256];
                var privilegesLength = 256u;
                return Advapi32.AccessCheck(sd, token, desiredAccess, ref mapping, privileges, ref privilegesLength, out _, out var granted)
                       && granted;
            }
        }
        finally
        {
            if (token != 0) Kernel32.CloseHandle(token);
            if (process != 0) Kernel32.CloseHandle(process);
        }
    }
}
```

`src/UasSort.Platform/Ledger/LedgerStore.cs`:

```csharp
using System.Text;
using UasSort.Platform.Io;

namespace UasSort.Platform.Ledger;

/// <summary>The ledger folder videoRoot\.uas-sort: the one exemption from "never open library files" (Ref §4.3, §11).
/// Platform gathers facts and opens files; Core's LedgerFolderStatusBuilder and LedgerLoader decide (decision 37).</summary>
public sealed partial class LedgerStore
{
    private static readonly IReadOnlySet<string> NoExclusions = new HashSet<string>();
    private readonly IPathFacts _facts;
    private readonly IDirectoryLister _lister;
    private readonly GuardContext _ctx;
    private readonly string _videoRoot;

    public LedgerStore(Settings settings, string appDataDir, string machine, IPathFacts facts, IDirectoryLister lister, TimeProvider clock)
    {
        CurrentSettings = settings;
        AppDataDir = appDataDir;
        Machine = machine;
        Clock = clock;
        _facts = facts;
        _lister = lister;
        _videoRoot = facts.Canonical(settings.VideoRoot);
        Folder = LedgerPaths.For(_videoRoot);                 // the same path LedgerFolderStatusBuilder and IoGuardPolicy derive
        OwnFile = LedgerPaths.OwnFile(_videoRoot, machine);
        BackupDir = LedgerPaths.BackupDir(appDataDir, _videoRoot);
        _ctx = GuardContexts.For(settings, appDataDir, machine, facts);
    }

    public string Folder { get; }
    public string OwnFile { get; }
    public string BackupDir { get; }

    // Used by the write half (Task 09.9): CopyInto builds a store for the new root, SnapshotToBackup names and copies.
    private Settings CurrentSettings { get; }
    private string AppDataDir { get; }
    private string Machine { get; }
    private TimeProvider Clock { get; }
    private ImmutableArray<string>? LoadedFiles { get; set; }   // the SourceFiles of the last Load(); null until one ran

    /// <summary>Attributes, one top-level listing, the sync-root test and the security-descriptor write check; no ledger file is opened.</summary>
    public LedgerFolderStatus Check() => LedgerFolderStatusBuilder.Build(_videoRoot, Machine, GatherFacts());

    /// <summary>The union of the listed local ledger files; LedgerLoader opens nothing for Missing, VideoRootMissing or CloudOnly.</summary>
    public LedgerSnapshot Load()
    {
        var snapshot = LedgerLoader.Load(Check(), OpenLedgerRead);
        LoadedFiles = snapshot.SourceFiles;
        return snapshot;
    }

    private LedgerFolderFacts GatherFacts()
    {
        if (!IsDirectory(Kernel32.TryGetAttributes(_videoRoot, out _)))
            return new LedgerFolderFacts(VideoRootExists: false, Folder: null, TopLevel: null, InSyncRoot: false, Writable: false);

        FsEntry? folder = Kernel32.TryGetAttributeData(Folder, out var d, out _)
            ? new FsEntry(Folder, LedgerPaths.FolderName, (d.FileAttributes & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0, d.Size,
                          d.LastWriteUtc, d.CreationUtc, d.LastAccessUtc, d.FileAttributes)
            : null;
        var exists = folder is { IsDirectory: true };
        var topLevel = exists ? _lister.Enumerate(Folder, recurse: false, NoExclusions) : null;
        var inSyncRoot = _facts.InSyncRoot(exists ? Folder : _videoRoot);

        var ownAttributes = Kernel32.TryGetAttributes(OwnFile, out _);
        var writable = exists
            ? AccessProbe.Has(Folder, AccessProbe.FILE_ADD_FILE)
              && (ownAttributes is not uint own
                  || ((own & Kernel32.FILE_ATTRIBUTE_READONLY) == 0 && AccessProbe.Has(OwnFile, AccessProbe.FILE_APPEND_DATA)))
            : AccessProbe.Has(_videoRoot, AccessProbe.FILE_ADD_FILE);

        return new LedgerFolderFacts(VideoRootExists: true, folder, topLevel, inSyncRoot, writable);
    }

    /// <summary>LedgerLoader's opener: the guard first (a cloud-only file throws CloudOnlyFileException before any open).</summary>
    private Stream OpenLedgerRead(string path)
    {
        var canonical = _facts.Canonical(path);
        IoGate.Require(IoOp.ReadData, canonical, _ctx);
        return OpenShared(canonical);
    }

    private static FileStream OpenShared(string path)
    {
#pragma warning disable RS0030 // IO layer: ledger exemption read, FileShare.ReadWrite (Ref §4.3)
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite, Options = FileOptions.SequentialScan,
        });
#pragma warning restore RS0030
    }

    /// <summary>Whole text of a file the caller has already cleared with IoGate (backup load, [Copy]).</summary>
    private static string ReadAllText(string path)
    {
        using var stream = OpenShared(path);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    private static bool IsDirectory(uint? attributes) => attributes is uint a && (a & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*LedgerStoreReadTests*"`
Expected: PASS, `Passed: 8`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform tests/UasSort.Platform.Tests
git commit -m "feat: ledger store status check and guarded union load

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 09.9: `LedgerStore` — folder, pin, own-file writer, snapshots, backup load and [Copy]

**Files:**
- Create: `src/UasSort.Platform/Ledger/LedgerStore.Writes.cs`
- Create: `src/UasSort.Platform/Ledger/LedgerWriter.cs`
- Create: `tests/UasSort.Platform.Tests/LedgerStoreWriteTests.cs`

**Interfaces:**
- Consumes (Part 02): `ILedgerStore`, `ILedgerWriter`, `LedgerRecord`, `TornRecord`, `LedgerSnapshot` (its `SourceFiles`), `LedgerPaths`, `IoGuardPolicy` rules (SetPinned is exempt from the placeholder bits; Rename is checked on the source). (Part 05): `LedgerCodec.Version`, `LedgerCodec.Serialize`, `LedgerCodec.TryParse`, `LedgerReader.Read`, `LedgerSnapshots.Detached`, `LedgerFileText`. This part: `LedgerStore` (Task 09.8), `IoGate`.
- Produces (defined here):
  - `public sealed partial class LedgerStore : ILedgerStore` — `EnsureFolder()`, `KeepOnDevice()`, `OpenOwn()`, `SnapshotToBackup(string runId)`, `CopyInto(string newVideoRoot, LedgerSnapshot current)`, and `public LedgerSnapshot LoadFromBackup()` (defined here: the latest `snapshots\*` folder ∪ the own-file mirror of this root's `BackupDir`, read with `LedgerReader.Read` under `LedgerSnapshots.Detached(BackupDir, files)`, for settings recovery and for [Copy] when the old root is gone; Ref §9.14, §11).
  - `internal sealed class LedgerWriter : ILedgerWriter` — `Append` writes `LedgerCodec.Serialize(r)` + `\n` (UTF-8) to the own file, `FlushFileBuffers`, then the same bytes to the mirror, `FlushFileBuffers`; any failure throws. `internal void AppendRawLine(string jsonLine)` (used by [Copy] to keep every copied record byte-identical). Each file's torn tail is repaired on open with its own `torn` record, also written with `LedgerCodec.Serialize` (line numbers differ between the own file and the mirror, so a torn record is never mirrored).
  - `CopyInto` is called on the store of the **old** root (its guard context allows reading the old `.uas-sort`); it builds a store for `newVideoRoot`, runs `EnsureFolder()` there, and appends every line of `current.SourceFiles` whose `id` is not yet in the new own file, keeping `id` and `machine`; lines are recognised with `LedgerCodec.TryParse`, and `torn` records and unparseable lines are never copied.
  - `SnapshotToBackup` copies the `SourceFiles` of the last `Load()` (before any `Load()`: the local, not cloud-only, files `Check()` lists) into `BackupDir\snapshots\yyyyMMdd-HHmmss-run8\` (UTC from the `TimeProvider`) and keeps the newest 20.

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/LedgerStoreWriteTests.cs`:

```csharp
namespace UasSort.Platform.Tests;

public sealed class LedgerStoreWriteTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 9, 27, 21, 0, 0, TimeSpan.Zero));

    private static LedgerStore Store(TestEnv env, Settings? settings = null, TimeProvider? clock = null)
        => new(settings ?? env.Settings, env.AppData, TestEnv.Machine, env.Facts, new WindowsDirectoryLister(), clock ?? Clock);

    private static bool Pinned(string path) => ((uint)File.GetAttributes(path) & 0x80000) != 0;

    [Fact]
    public void EnsureFolder_CreatesTheFolderOnly_AndPinsIt()
    {
        using var env = new TestEnv();
        var store = Store(env);
        store.EnsureFolder();
        var folder = Path.Join(env.VideoRoot, ".uas-sort");
        Assert.True(Directory.Exists(folder));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        Assert.True(Pinned(folder));
        Assert.Equal([folder], Directory.EnumerateFileSystemEntries(env.VideoRoot).ToArray());
    }

    [Fact]
    public void EnsureFolder_OnAnExistingUnpinnedFolder_OnlyPins_AndKeepOnDevicePinsNothingElse()
    {
        using var env = new TestEnv();
        var b = env.Temp.File(@"video\.uas-sort\ledger-B.jsonl", 64);
        var before = File.ReadAllBytes(b);
        Store(env).EnsureFolder();
        Store(env).KeepOnDevice();
        Assert.True(Pinned(Path.Join(env.VideoRoot, ".uas-sort")));
        Assert.False(Pinned(b));
        Assert.False(Pinned(env.VideoRoot));
        Assert.Equal(before, File.ReadAllBytes(b));
    }

    [Fact]
    public void OpenOwn_AppendsFlushedLines_Mirrors_AndIsTheSingleWriter()
    {
        using var env = new TestEnv();
        var store = Store(env);
        store.EnsureFolder();
        using (var w = store.OpenOwn())
        {
            w.Append(LedgerRecords.Run("TESTPC", "r1"));
            w.Append(LedgerRecords.Run("TESTPC", "r2"));
            Assert.ThrowsAny<IOException>(() => new FileStream(store.OwnFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
            using var reader = new FileStream(store.OwnFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        var own = File.ReadAllBytes(store.OwnFile);
        Assert.Equal((byte)'\n', own[^1]);
        Assert.Equal(2, own.Count(b => b == (byte)'\n'));
        Assert.Equal(own, File.ReadAllBytes(Path.Join(store.BackupDir, "ledger-TESTPC.jsonl")));
        Assert.StartsWith(Path.Join(env.AppData, "ledger-backup"), store.BackupDir, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenOwn_RepairsATornTail_WithATornRecord()
    {
        using var env = new TestEnv();
        var store = Store(env);
        store.EnsureFolder();
        File.WriteAllText(store.OwnFile, LedgerRecords.Line(LedgerRecords.Run("TESTPC", "r1")) + "\n"
                                         + LedgerRecords.Line(LedgerRecords.Run("TESTPC", "r2")) + "\n" + "{\"t\":\"file\",\"v\":1,\"id\":\"ab");
        using (var w = store.OpenOwn()) w.Append(LedgerRecords.Run("TESTPC", "r3"));
        var lines = File.ReadAllText(store.OwnFile).Split('\n');
        Assert.Equal(6, lines.Length);                       // 5 lines + the empty string after the final \n
        Assert.StartsWith("{\"t\":\"file\"", lines[2], StringComparison.Ordinal);
        Assert.Contains("\"t\":\"torn\"", lines[3], StringComparison.Ordinal);
        Assert.Contains("\"line\":3", lines[3], StringComparison.Ordinal);
        var snapshot = store.Load();
        Assert.Equal(3, snapshot.Runs.Length);
        Assert.Empty(snapshot.ParseIssues);
    }

    [Fact]
    public void OpenOwn_OnACloudOnlyOwnFile_IsCloudOnly()
    {
        using var env = new TestEnv();
        env.Temp.File(@"video\.uas-sort\ledger-TESTPC.jsonl", attributes: FileAttributes.Offline);
        Assert.Throws<CloudOnlyFileException>(() => Store(env).OpenOwn());
    }

    [Fact]
    public void SnapshotToBackup_CopiesLoadedFiles_AndKeepsTwenty()
    {
        using var env = new TestEnv();
        LedgerRecords.Write(Path.Join(env.VideoRoot, ".uas-sort", "ledger-B.jsonl"), LedgerRecords.Run("B", "rb"));
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 21, 0, 0, TimeSpan.Zero));
        var store = Store(env, clock: clock);
        store.Load();
        for (var i = 0; i < 22; i++)
        {
            store.SnapshotToBackup($"{i:D2}aaaaaa-bbbb");
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        var snapshots = Directory.GetDirectories(Path.Join(store.BackupDir, "snapshots")).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(20, snapshots.Length);
        Assert.EndsWith("20260927-210021-21aaaaaa", snapshots[^1], StringComparison.Ordinal);
        Assert.Equal(File.ReadAllBytes(Path.Join(env.VideoRoot, ".uas-sort", "ledger-B.jsonl")),
                     File.ReadAllBytes(Path.Join(snapshots[^1], "ledger-B.jsonl")));
    }

    [Fact]
    public void CopyInto_AppendsTheUnionKeepingIds_SkipsTorn_AndIsIdempotent()
    {
        using var env = new TestEnv();
        var oldFolder = Path.Join(env.VideoRoot, ".uas-sort");
        LedgerRecords.Write(Path.Join(oldFolder, "ledger-B.jsonl"), LedgerRecords.Run("B", "b1"), LedgerRecords.Run("B", "b2"));
        var old = Store(env);
        old.EnsureFolder();
        using (var w = old.OpenOwn()) w.Append(LedgerRecords.Run("TESTPC", "t1"));
        File.AppendAllText(old.OwnFile, "{\"t\":\"run\"");                  // torn tail …
        using (old.OpenOwn()) { }                                              // … repaired with a torn record
        var current = old.Load();
        var newRoot = env.Temp.Sub("video2");
        old.CopyInto(newRoot, current);
        old.CopyInto(newRoot, current);
        var newStore = Store(env, env.Settings with { VideoRoot = newRoot });
        var lines = File.ReadAllLines(newStore.OwnFile);
        Assert.Equal(3, lines.Length);
        Assert.DoesNotContain(lines, l => l.Contains("\"t\":\"torn\"", StringComparison.Ordinal));
        Assert.Equal(["b1", "b2", "t1"], newStore.Load().Runs.Select(r => r.Run).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(current.Runs.Select(r => r.Machine).Order(), newStore.Load().Runs.Select(r => r.Machine).Order());
        Assert.True(Pinned(Path.Join(newRoot, ".uas-sort")));
        Assert.Equal(3, File.ReadAllLines(Path.Join(newStore.BackupDir, "ledger-TESTPC.jsonl")).Length);
    }

    [Fact]
    public void LoadFromBackup_IsLatestSnapshotUnionMirror()
    {
        using var env = new TestEnv();
        LedgerRecords.Write(Path.Join(env.VideoRoot, ".uas-sort", "ledger-B.jsonl"), LedgerRecords.Run("B", "b1"));
        var store = Store(env);
        store.EnsureFolder();
        store.Load();
        store.SnapshotToBackup("run00001");
        using (var w = store.OpenOwn()) w.Append(LedgerRecords.Run("TESTPC", "after-snapshot"));
        var backup = Store(env).LoadFromBackup();
        Assert.Equal(["after-snapshot", "b1"], backup.Runs.Select(r => r.Run).Order(StringComparer.Ordinal).ToArray());
        Assert.All(backup.SourceFiles, f => Assert.StartsWith(env.AppData, f, StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*LedgerStoreWriteTests*"`
Expected: build FAILS with CS1061 "'LedgerStore' does not contain a definition for 'EnsureFolder'" (and for `OpenOwn`, `KeepOnDevice`, `SnapshotToBackup`, `CopyInto`, `LoadFromBackup`).

- [ ] **Step 3: Implement**

`src/UasSort.Platform/Ledger/LedgerWriter.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace UasSort.Platform.Ledger;

/// <summary>ILedgerWriter over the own file (FileShare.Read: the single writer) and its local mirror (Ref §4.1, §11).
/// Every line is LedgerCodec.Serialize output (decision 37).</summary>
internal sealed class LedgerWriter : ILedgerWriter
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly FileStream _own;
    private readonly FileStream _mirror;

    /// <summary>Takes ownership of both streams; repairs each torn tail with its own torn record.</summary>
    public LedgerWriter(FileStream own, FileStream mirror, string machine, TimeProvider clock)
    {
        _own = own;
        _mirror = mirror;
        RepairTail(_own, machine, clock);
        RepairTail(_mirror, machine, clock);
    }

    public void Append(LedgerRecord r) => AppendRawLine(LedgerCodec.Serialize(r));

    internal void AppendRawLine(string jsonLine)
    {
        var bytes = Utf8.GetBytes(jsonLine + "\n");
        WriteFlushed(_own, bytes);
        WriteFlushed(_mirror, bytes);
    }

    public void Dispose()
    {
        _own.Dispose();
        _mirror.Dispose();
    }

    private static void RepairTail(FileStream s, string machine, TimeProvider clock)
    {
        var (length, newlines, lastByte) = Scan(s);
        s.Seek(0, SeekOrigin.End);
        if (length == 0 || lastByte == (byte)'\n') return;
        var torn = new TornRecord(LedgerCodec.Version, Guid.NewGuid().ToString("N"), machine, clock.GetUtcNow().UtcDateTime, newlines + 1);
        WriteFlushed(s, Utf8.GetBytes("\n" + LedgerCodec.Serialize(torn) + "\n"));
    }

    private static (long Length, int Newlines, byte LastByte) Scan(FileStream s)
    {
        s.Seek(0, SeekOrigin.Begin);
        var buffer = new byte[1 << 16];
        var newlines = 0;
        byte last = 0;
        long length = 0;
        int n;
        while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            newlines += buffer.AsSpan(0, n).Count((byte)'\n');
            last = buffer[n - 1];
            length += n;
        }
        return (length, newlines, last);
    }

    private static void WriteFlushed(FileStream s, byte[] bytes)
    {
        s.Write(bytes);
        s.Flush();
        if (!Kernel32.FlushFileBuffers(s.SafeFileHandle))
            throw new IOException($"FlushFileBuffers({s.Name}) failed: {Marshal.GetLastPInvokeError()}");
    }
}
```

`src/UasSort.Platform/Ledger/LedgerStore.Writes.cs`:

```csharp
using System.Globalization;
using System.Runtime.InteropServices;
using UasSort.Platform.Io;

namespace UasSort.Platform.Ledger;

public sealed partial class LedgerStore : ILedgerStore
{
    private const int SnapshotsKept = 20;
    // SetFileAttributesW accepts only these bits; DIRECTORY, REPARSE_POINT, SPARSE, COMPRESSED, ENCRYPTED are masked out
    private const uint SettableAttributes = 0x1 | 0x2 | 0x4 | 0x20 | 0x100 | 0x1000 | 0x2000 | 0x8000 | 0x20000 | 0x80000 | 0x100000;

    public void EnsureFolder()
    {
        if (!IsDirectory(Kernel32.TryGetAttributes(_videoRoot, out _)))
            throw new DirectoryNotFoundException($"The video root {_videoRoot} is missing");
        if (!IsDirectory(Kernel32.TryGetAttributes(Folder, out _)))
        {
            IoGate.Require(IoOp.CreateDir, Folder, _ctx);
#pragma warning disable RS0030 // IO layer: ledger exemption 3, creates videoRoot\.uas-sort itself (the parent exists)
            Directory.CreateDirectory(Folder);
#pragma warning restore RS0030
        }
        KeepOnDevice();
    }

    public void KeepOnDevice()
    {
        var attributes = IoGate.Require(IoOp.SetPinned, Folder, _ctx) ?? throw new DirectoryNotFoundException(Folder);
        var pinned = ((attributes | Kernel32.FILE_ATTRIBUTE_PINNED) & ~Kernel32.FILE_ATTRIBUTE_UNPINNED) & SettableAttributes;
        if ((attributes & Kernel32.FILE_ATTRIBUTE_PINNED) != 0 && (attributes & Kernel32.FILE_ATTRIBUTE_UNPINNED) == 0) return;
        if (!Kernel32.SetFileAttributes(LongPath.Prefix(Folder), pinned))
            throw new IOException($"Pinning {Folder} failed: {Marshal.GetLastPInvokeError()}");
    }

    public ILedgerWriter OpenOwn() => OpenOwnWriter();

    public void SnapshotToBackup(string runId)
    {
        var sources = LoadedFiles ?? LocalLedgerFiles(Check());
        var stamp = Clock.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var run8 = new string(runId.Where(char.IsAsciiLetterOrDigit).Take(8).ToArray());
        var snapshotsRoot = Path.Join(BackupDir, "snapshots");
        var dir = Path.Join(snapshotsRoot, $"{stamp}-{run8}");
        IoGate.Require(IoOp.CreateDir, dir, _ctx);
#pragma warning disable RS0030 // IO layer: local backup under %LOCALAPPDATA%\uas-sort (guard rule 5)
        Directory.CreateDirectory(dir);
        foreach (var listed in sources)
        {
            var source = _facts.Canonical(listed);
            IoGate.Require(IoOp.ReadData, source, _ctx);
            var destination = Path.Join(dir, Path.GetFileName(source));
            IoGate.Require(IoOp.CreateNew, destination, _ctx);
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
        }
        var old = _lister.Enumerate(snapshotsRoot, recurse: false, NoExclusions).Entries
                         .Where(e => e.IsDirectory).Select(e => e.FullPath).Order(StringComparer.Ordinal).ToList();
        foreach (var stale in old.Take(Math.Max(0, old.Count - SnapshotsKept)))
        {
            IoGate.Require(IoOp.Delete, stale, _ctx);
            Directory.Delete(stale, recursive: true);
        }
#pragma warning restore RS0030
    }

    public LedgerSnapshot LoadFromBackup()
    {
        var files = new List<string>();
        var snapshotsRoot = Path.Join(BackupDir, "snapshots");
        var latest = _lister.Enumerate(snapshotsRoot, recurse: false, NoExclusions).Entries
                            .Where(e => e.IsDirectory).Select(e => e.FullPath).Order(StringComparer.Ordinal).LastOrDefault();
        if (latest is not null)
            files.AddRange(_lister.Enumerate(latest, recurse: false, NoExclusions).Entries.Where(e => !e.IsDirectory).Select(e => e.FullPath));
        var mirror = Path.Join(BackupDir, Path.GetFileName(OwnFile));
        if (Kernel32.TryGetAttributes(mirror, out _) is not null) files.Add(mirror);
        var texts = files.Select(f =>
        {
            IoGate.Require(IoOp.ReadData, f, _ctx);
            return new LedgerFileText(f, ReadAllText(f));
        }).ToList();
        return LedgerReader.Read(texts, LedgerSnapshots.Detached(BackupDir, files));
    }

    public void CopyInto(string newVideoRoot, LedgerSnapshot current)
    {
        var target = new LedgerStore(CurrentSettings with { VideoRoot = newVideoRoot }, AppDataDir, Machine, _facts, _lister, Clock);
        target.EnsureFolder();
        var present = new HashSet<string>(StringComparer.Ordinal);
        if (Kernel32.TryGetAttributes(target.OwnFile, out _) is not null)
        {
            IoGate.Require(IoOp.ReadData, target.OwnFile, target._ctx);
            foreach (var line in ReadAllText(target.OwnFile).Split('\n'))
                if (TryRecord(line) is { } r) present.Add(r.Id);
        }
        using var writer = target.OpenOwnWriter();
        foreach (var listed in current.SourceFiles)
        {
            var source = _facts.Canonical(listed);
            IoGate.Require(IoOp.ReadData, source, _ctx);
            foreach (var line in ReadAllText(source).Split('\n'))
            {
                if (TryRecord(line) is not { } r || r is TornRecord) continue;   // torn line numbers belong to their own file
                if (present.Add(r.Id)) writer.AppendRawLine(line.TrimEnd('\r'));
            }
        }
    }

    private static ImmutableArray<string> LocalLedgerFiles(LedgerFolderStatus status)
        => [.. status.LedgerFiles.Where(f => !status.CloudOnlyFiles.Contains(f, StringComparer.OrdinalIgnoreCase))];

    private LedgerWriter OpenOwnWriter()
    {
        IoGate.Require(IoOp.AppendOwnLedger, OwnFile, _ctx);
        var mirrorPath = Path.Join(BackupDir, Path.GetFileName(OwnFile));
        IoGate.Require(IoOp.CreateDir, BackupDir, _ctx);
        IoGate.Require(IoOp.AppendOwnLedger, mirrorPath, _ctx);
#pragma warning disable RS0030 // IO layer: ledger exemption 2 (own file, FileShare.Read single writer) and its local mirror
        var own = new FileStream(OwnFile, new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.Read, BufferSize = 0,
        });
        try
        {
            Directory.CreateDirectory(BackupDir);
            var mirror = new FileStream(mirrorPath, new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.Read, BufferSize = 0,
            });
#pragma warning restore RS0030
            try { return new LedgerWriter(own, mirror, Machine, Clock); }
            catch { mirror.Dispose(); throw; }
        }
        catch { own.Dispose(); throw; }
    }

    /// <summary>One ledger line through Core's codec; null for a blank, torn-tail or otherwise unparseable line (never copied).</summary>
    private static LedgerRecord? TryRecord(string line)
        => line.TrimEnd('\r') is { Length: > 0 } text ? LedgerCodec.TryParse(text, out _) : null;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*LedgerStore*"`
Expected: PASS, `Passed: 16` (8 read tests + 8 write tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform tests/UasSort.Platform.Tests
git commit -m "feat: ledger folder create and pin, own-file writer with torn-tail repair, mirror, snapshots and copy

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 09.10: Settings, draft and report stores

**Files:**
- Modify: `src/UasSort.Platform/Io/GuardContexts.cs` (add `ForAppData`)
- Create: `src/UasSort.Platform/Stores/StoreFiles.cs`
- Create: `src/UasSort.Platform/Stores/SettingsStore.cs`
- Create: `src/UasSort.Platform/Stores/DraftStore.cs`
- Create: `src/UasSort.Platform/Stores/ReportStore.cs`
- Create: `tests/UasSort.Platform.Tests/StoreTests.cs`

**Interfaces:**
- Consumes (Part 02): `ISettingsStore`, `SettingsLoad`, `IDraftStore`, `Draft`, `IReportStore`, `OffloadReport`, `CleanupReport`, `RunRoots`, `LedgerPaths.IsLedgerFileName`, `CoreJsonContext.Default.Draft`, `.OffloadReport`, `.CleanupReport`. (Part 05, `UasSort.Core.Config`): `SettingsDefaults.Derive`, `SettingsCodec.Serialize`/`Parse`, `SettingsLoadPolicy.Decide`, `SettingsLoadDecision`, `SettingsRecovery.RootsFromLastRun`; (`UasSort.Core.Ledger`): `LedgerFileText`. This part: `IoGate`, `WindowsDirectoryLister`.
- Produces (defined here):
  - `GuardContexts.ForAppData(string appDataDir, string machine, IPathFacts facts)` — a context whose only root is the store folder (video and photo roots are the never-existing sentinel `appDataDir\.no-library`), so the policy allows the store's own files (rule 5) and still refuses placeholders (rule 1) and anything outside it.
  - `internal static class StoreFiles` — `string? ReadText(string path, GuardContext ctx)` (null when missing), `void WriteReplace(string path, string text, GuardContext ctx, string? backupPath)` (temp file `path.tmp`, flush, then `File.Replace` with the backup when the target exists, else a move), `string WriteNew(string path, string text, GuardContext ctx)` (`CreateNew`; adds `-2`, `-3`, … before `.json` when taken; returns the path written).
  - `public sealed class SettingsStore : ISettingsStore` — `SettingsStore(string appDataDir, string picturesFolder, string machine, IPathFacts facts, IDirectoryLister lister, TimeProvider clock)` for `appDataDir\settings.json`; the static `SettingsStore.ForFile(string settingsFile, string appDataDir, string picturesFolder, string machine, IPathFacts facts, IDirectoryLister lister, TimeProvider clock)` (CLI `--settings`) roots the store at the file's folder. Core decides (decision 37): `Load(readOnly)` reads the file text (null when missing; a placeholder is refused by the guard) and returns `SettingsLoadPolicy.Decide(path, text, readOnly, SettingsDefaults.Derive(picturesFolder), clock UTC now, () => SettingsRecovery.RootsFromLastRun(mirrors)).Load`, renaming the bad file to `MoveCorruptTo` only when not read-only; `mirrors` are the local ledger mirrors `ledger-backup\*\ledger*.jsonl`. So a missing file gives the derived defaults with `Recovered = false`, `RootsConfirmed = false` (decision 46); an unreadable file (`SettingsCodec.Parse` error: not JSON, `schema` other than 1, a root missing, R or G out of range) gives the defaults with `Recovered = true`, `RootsConfirmed = false`, the corrupt copy `settings.json.corrupt-yyyyMMdd-HHmmss` (UTC) and the roots of the latest run in the mirrors (Ref §11); `Load(readOnly: true)` never renames, creates or writes. `Save` writes `SettingsCodec.Serialize(s)` through `File.Replace` with `settings.json.bak`.
  - `public sealed class DraftStore(string appDataDir, string machine, IPathFacts facts) : IDraftStore` — `drafts\{cardKey}.json`, keys must match `^[A-Za-z0-9-]+$`; unreadable drafts load as null.
  - `public sealed class ReportStore(string appDataDir, string machine, IPathFacts facts, TimeProvider clock) : IReportStore` — `reports\yyyyMMdd-HHmmss-run8.json` and `…-run8-cleanup.json` (local time of the `TimeProvider`), written once.

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/StoreTests.cs`:

```csharp
using System.Text.Json;

namespace UasSort.Platform.Tests;

public sealed class StoreTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 9, 27, 21, 7, 2, TimeSpan.Zero));

    private static SettingsStore Settings(TestEnv env)
        => new(env.AppData, env.Temp.Sub("pictures"), TestEnv.Machine, env.Facts, new WindowsDirectoryLister(), Clock);

    private static string[] Snapshot(string dir)
        => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Select(f => f + "|" + Convert.ToHexString(File.ReadAllBytes(f))).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void Settings_Missing_GivesUnconfirmedDefaults_NotRecovered_AndWritesNothing()
    {
        using var env = new TestEnv();
        var load = Settings(env).Load();
        Assert.False(load.Recovered);                       // decision 46: Recovered means an unreadable file only
        Assert.Null(load.CorruptCopyPath);
        Assert.False(load.Settings.RootsConfirmed);
        Assert.EndsWith(@"\pictures\UAS Videos", load.Settings.VideoRoot, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(env.AppData));
    }

    [Fact]
    public void Settings_SaveThenLoad_RoundTrips_AndKeepsABak()
    {
        using var env = new TestEnv();
        var store = Settings(env);
        store.Save(env.Settings);
        store.Save(env.Settings with { GapDays = 2 });
        var load = store.Load();
        Assert.False(load.Recovered);
        Assert.Equal(2, load.Settings.GapDays);
        Assert.Equal(env.Settings.VideoRoot, load.Settings.VideoRoot);
        Assert.Equal(1, SettingsCodec.Parse(File.ReadAllText(Path.Join(env.AppData, "settings.json.bak"))).Settings!.GapDays);
        Assert.False(File.Exists(Path.Join(env.AppData, "settings.json.tmp")));
    }

    [Fact]
    public void Settings_Corrupt_IsKept_DefaultsBlock_AndRootsComeFromTheLastRun()
    {
        using var env = new TestEnv();
        File.WriteAllText(Path.Join(env.AppData, "settings.json"), "{ not json");
        LedgerRecords.Write(Path.Join(env.AppData, "ledger-backup", "0123456789abcdef", "ledger-TESTPC.jsonl"),
                            LedgerRecords.Run("TESTPC", "r1", videoRoot: @"D:\Videos", photoRoot: @"D:\Photos"));
        var load = Settings(env).Load();
        Assert.True(load.Recovered);
        Assert.False(load.Settings.RootsConfirmed);
        Assert.Equal(env.C(Path.Join(env.AppData, "settings.json")) + ".corrupt-20260927-210702", load.CorruptCopyPath);
        Assert.Equal("{ not json", File.ReadAllText(load.CorruptCopyPath!));
        Assert.False(File.Exists(Path.Join(env.AppData, "settings.json")));
        Assert.Equal(new RunRoots(@"D:\Videos", @"D:\Photos"), load.RootsFromLastRun);
    }

    [Fact]
    public void Settings_ReadOnlyLoad_OfACorruptFile_ChangesNothing()
    {
        using var env = new TestEnv();
        File.WriteAllText(Path.Join(env.AppData, "settings.json"), "{\"schema\":7}");
        var before = Snapshot(env.AppData);
        var load = Settings(env).Load(readOnly: true);
        Assert.True(load.Recovered);
        Assert.Null(load.CorruptCopyPath);
        Assert.Equal(before, Snapshot(env.AppData));
    }

    [Fact]
    public void Settings_Placeholder_IsNeverOpened()
    {
        using var env = new TestEnv();
        env.Temp.File(@"appdata\settings.json", attributes: FileAttributes.Offline);
        Assert.Throws<UnsafeIoException>(() => Settings(env).Load());
    }

    [Fact]
    public void Drafts_SaveLoadDelete()
    {
        using var env = new TestEnv();
        var drafts = new DraftStore(env.AppData, TestEnv.Machine, env.Facts);
        var d = new Draft(1, "vol-1A2B3C4D", "9f3c0a6d12e4b7a1", new DateTime(2026, 9, 27, 21, 0, 0, DateTimeKind.Utc), new Tuning(40, 1),
                          [new SplitBefore(new ItemId("DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"))]);
        Assert.Null(drafts.Load("vol-1A2B3C4D"));
        drafts.Save("vol-1A2B3C4D", d);
        var back = drafts.Load("vol-1A2B3C4D")!;
        Assert.Equal(d.InventoryHash, back.InventoryHash);
        Assert.Equal(d.Tuning, back.Tuning);
        Assert.Equal(d.Edits.Single(), back.Edits.Single());
        File.WriteAllText(Path.Join(env.AppData, "drafts", "vol-00000000.json"), "garbage");
        Assert.Null(drafts.Load("vol-00000000"));
        drafts.Delete("vol-1A2B3C4D");
        Assert.Null(drafts.Load("vol-1A2B3C4D"));
        Assert.Throws<ArgumentException>(() => drafts.Save(@"..\x", d));
    }

    [Fact]
    public void Reports_AreWrittenOnce_WithTimestampAndRun8()
    {
        using var env = new TestEnv();
        var reports = new ReportStore(env.AppData, TestEnv.Machine, env.Facts, Clock);
        var offload = new OffloadReport(1, "8f1c2d3e-aaaa-bbbb", env.Settings, "1 group", [], [], [], VerdictLevel.Safe, "Safe to format", null);
        var p1 = reports.Save(offload);
        var p2 = reports.Save(offload);
        var dir = Path.Join(env.C(env.AppData), "reports");
        Assert.Equal(Path.Join(dir, "20260927-210702-8f1c2d3e.json"), p1);
        Assert.Equal(Path.Join(dir, "20260927-210702-8f1c2d3e-2.json"), p2);
        Assert.Equal("Safe to format", JsonSerializer.Deserialize(File.ReadAllText(p1), CoreJsonContext.Default.OffloadReport)!.Headline);
        var cleanup = new CleanupReport(1, "c4e2aaaa-1111", new CardIdentity(0x1A2B3C4D, null, "exFAT", 256_060_514_304),
            new CleanupRequest(CleanupMode.BeforeDate, new DateOnly(2026, 7, 26), null, false),
            new CleanupCutoff(new DateOnly(2026, 7, 26), null, null, null, 0, 0, null), [], [], null, 1, 2, VerdictLevel.NotSafe);
        Assert.Equal(Path.Join(dir, "20260927-210702-c4e2aaaa-cleanup.json"), reports.Save(cleanup));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*StoreTests*"`
Expected: build FAILS with CS0246 "The type or namespace name 'SettingsStore' could not be found" (and `DraftStore`, `ReportStore`).

- [ ] **Step 3: Implement**

Add to `src/UasSort.Platform/Io/GuardContexts.cs` (inside `GuardContexts`):

```csharp
    /// <summary>For Platform's own stores: the store folder is the only place the policy allows (rule 5).</summary>
    public static GuardContext ForAppData(string appDataDir, string machine, IPathFacts facts)
    {
        var root = facts.Canonical(appDataDir);
        var noLibrary = Path.Join(root, ".no-library");     // never created; keeps rules 3–4 away from the store
        return new(noLibrary, noLibrary, [], null, root, machine, None, None, None, KnownFolders.SystemVolumeRoot(), false, null);
    }
```

`src/UasSort.Platform/Stores/StoreFiles.cs`:

```csharp
using System.Text;
using UasSort.Platform.Io;

namespace UasSort.Platform.Stores;

internal static class StoreFiles
{
    private static readonly UTF8Encoding Utf8 = new(false);

#pragma warning disable RS0030 // IO layer: Platform's own stores under %LOCALAPPDATA%\uas-sort, each path cleared by IoGate
    public static string? ReadText(string path, GuardContext ctx)
    {
        if (!Exists(path)) return null;
        IoGate.Require(IoOp.ReadData, path, ctx);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, Utf8);
        return reader.ReadToEnd();
    }

    public static void WriteReplace(string path, string text, GuardContext ctx, string? backupPath)
    {
        var dir = Path.GetDirectoryName(path)!;
        IoGate.Require(IoOp.CreateDir, dir, ctx);
        Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        Delete(temp, ctx);                                   // a temp left by a crash
        IoGate.Require(IoOp.CreateNew, temp, ctx);
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(Utf8.GetBytes(text));
            stream.Flush(flushToDisk: true);
        }
        IoGate.Require(IoOp.Rename, temp, ctx);
        if (Exists(path))
        {
            IoGate.Require(IoOp.Rename, path, ctx);
            if (backupPath is not null) IoGate.Require(IoOp.CreateNew, backupPath, ctx);
            File.Replace(temp, path, backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            IoGate.Require(IoOp.CreateNew, path, ctx);
            File.Move(temp, path);
        }
    }

    public static string WriteNew(string path, string text, GuardContext ctx)
    {
        var dir = Path.GetDirectoryName(path)!;
        IoGate.Require(IoOp.CreateDir, dir, ctx);
        Directory.CreateDirectory(dir);
        var stem = path[..^".json".Length];
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? path : $"{stem}-{n}.json";
            if (Exists(candidate)) continue;
            IoGate.Require(IoOp.CreateNew, candidate, ctx);
            try
            {
                using var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(Utf8.GetBytes(text));
                stream.Flush(flushToDisk: true);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate)) { }
        }
    }

    public static void Rename(string from, string to, GuardContext ctx)
    {
        IoGate.Require(IoOp.Rename, from, ctx);
        IoGate.Require(IoOp.CreateNew, to, ctx);
        File.Move(from, to);
    }

    public static void Delete(string path, GuardContext ctx)
    {
        if (!Exists(path)) return;
        IoGate.Require(IoOp.Delete, path, ctx);
        File.Delete(path);
    }
#pragma warning restore RS0030

    /// <summary>Attributes only. Null attributes are accepted by the policy only for creates, so every other op checks this first;
    /// a failure other than "not found" counts as existing and lets IoGate refuse it.</summary>
    private static bool Exists(string path)
        => Kernel32.TryGetAttributes(path, out var error) is not null || !Kernel32.IsNotFound(error);
}
```

`src/UasSort.Platform/Stores/SettingsStore.cs`:

```csharp
using UasSort.Platform.Io;

namespace UasSort.Platform.Stores;

/// <summary>settings.json with File.Replace + .bak (Ref §11). Parsing, validation and the recovery decision are Core's
/// SettingsCodec, SettingsLoadPolicy and SettingsRecovery (decision 37); this class only reads, renames and writes.</summary>
public sealed class SettingsStore : ISettingsStore
{
    private static readonly IReadOnlySet<string> NoExclusions = new HashSet<string>();
    private readonly string _appDataDir;
    private readonly string _path;
    private readonly string _picturesFolder;
    private readonly string _machine;
    private readonly IPathFacts _facts;
    private readonly IDirectoryLister _lister;
    private readonly TimeProvider _clock;
    private readonly GuardContext _ctx;

    public SettingsStore(string appDataDir, string picturesFolder, string machine, IPathFacts facts, IDirectoryLister lister, TimeProvider clock)
        : this(appDataDir, Path.Join(appDataDir, "settings.json"), picturesFolder, machine, facts, lister, clock) { }

    private SettingsStore(string appDataDir, string path, string picturesFolder, string machine, IPathFacts facts,
                          IDirectoryLister lister, TimeProvider clock)
    {
        _appDataDir = appDataDir;
        _path = facts.Canonical(path);
        _picturesFolder = picturesFolder;
        _machine = machine;
        _facts = facts;
        _lister = lister;
        _clock = clock;
        _ctx = GuardContexts.ForAppData(Path.GetDirectoryName(_path)!, machine, facts);
    }

    /// <summary>The CLI's --settings file: the store is rooted at that file's folder; backups still come from appDataDir.</summary>
    public static SettingsStore ForFile(string settingsFile, string appDataDir, string picturesFolder, string machine, IPathFacts facts,
                                        IDirectoryLister lister, TimeProvider clock)
        => new(appDataDir, settingsFile, picturesFolder, machine, facts, lister, clock);

    public SettingsLoad Load(bool readOnly = false)
    {
        var text = StoreFiles.ReadText(_path, _ctx);                    // null = missing; a placeholder throws in IoGate
        var decision = SettingsLoadPolicy.Decide(_path, text, readOnly, SettingsDefaults.Derive(_picturesFolder),
                                                 _clock.GetUtcNow().UtcDateTime,
                                                 () => SettingsRecovery.RootsFromLastRun(BackupMirrors()));
        if (!readOnly && decision.MoveCorruptTo is { } corrupt) StoreFiles.Rename(_path, corrupt, _ctx);
        return decision.Load;
    }

    public void Save(Settings s) => StoreFiles.WriteReplace(_path, SettingsCodec.Serialize(s), _ctx, _path + ".bak");

    /// <summary>The local ledger mirrors, appDataDir\ledger-backup\{root key}\ledger*.jsonl (top level of each key folder;
    /// snapshots are not mirrors). Read only when Core asks for them (an unreadable, non-read-only load).</summary>
    private List<LedgerFileText> BackupMirrors()
    {
        var appCtx = GuardContexts.ForAppData(_appDataDir, _machine, _facts);
        var mirrors = new List<LedgerFileText>();
        var backupRoot = Path.Join(_appDataDir, "ledger-backup");
        foreach (var rootKey in _lister.Enumerate(backupRoot, recurse: false, NoExclusions).Entries.Where(e => e.IsDirectory))
            foreach (var e in _lister.Enumerate(rootKey.FullPath, recurse: false, NoExclusions).Entries)
                if (!e.IsDirectory && LedgerPaths.IsLedgerFileName(Path.GetFileName(e.FullPath)))
                    mirrors.Add(new LedgerFileText(e.FullPath, StoreFiles.ReadText(_facts.Canonical(e.FullPath), appCtx) ?? ""));
        return mirrors;
    }
}
```

`src/UasSort.Platform/Stores/DraftStore.cs`:

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using UasSort.Platform.Io;

namespace UasSort.Platform.Stores;

public sealed partial class DraftStore(string appDataDir, string machine, IPathFacts facts) : IDraftStore
{
    private readonly GuardContext _ctx = GuardContexts.ForAppData(appDataDir, machine, facts);
    private readonly string _dir = Path.Join(facts.Canonical(appDataDir), "drafts");

    public Draft? Load(string cardKey)
    {
        var text = StoreFiles.ReadText(PathFor(cardKey), _ctx);
        if (text is null) return null;
        try { return JsonSerializer.Deserialize(text, CoreJsonContext.Default.Draft); }
        catch (Exception e) when (e is JsonException or NotSupportedException) { return null; }   // NotSupported: unknown "t" (as LedgerCodec)
    }

    public void Save(string cardKey, Draft d)
        => StoreFiles.WriteReplace(PathFor(cardKey), JsonSerializer.Serialize(d, CoreJsonContext.Default.Draft), _ctx, backupPath: null);

    public void Delete(string cardKey) => StoreFiles.Delete(PathFor(cardKey), _ctx);

    private string PathFor(string cardKey)
        => KeyPattern().IsMatch(cardKey) ? Path.Join(_dir, cardKey + ".json") : throw new ArgumentException($"Bad draft key {cardKey}", nameof(cardKey));

    [GeneratedRegex("^[A-Za-z0-9-]+$")]
    private static partial Regex KeyPattern();
}
```

`src/UasSort.Platform/Stores/ReportStore.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using UasSort.Platform.Io;

namespace UasSort.Platform.Stores;

public sealed class ReportStore(string appDataDir, string machine, IPathFacts facts, TimeProvider clock) : IReportStore
{
    private readonly GuardContext _ctx = GuardContexts.ForAppData(appDataDir, machine, facts);
    private readonly string _dir = Path.Join(facts.Canonical(appDataDir), "reports");

    public string Save(OffloadReport r)
        => StoreFiles.WriteNew(Name(r.RunId, ""), JsonSerializer.Serialize(r, CoreJsonContext.Default.OffloadReport), _ctx);

    public string Save(CleanupReport r)
        => StoreFiles.WriteNew(Name(r.RunId, "-cleanup"), JsonSerializer.Serialize(r, CoreJsonContext.Default.CleanupReport), _ctx);

    private string Name(string runId, string suffix)
    {
        var stamp = clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var run8 = new string(runId.Where(char.IsAsciiLetterOrDigit).Take(8).ToArray());
        return Path.Join(_dir, $"{stamp}-{run8}{suffix}.json");
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*StoreTests*"`
Expected: PASS, `Passed: 7`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform tests/UasSort.Platform.Tests
git commit -m "feat: settings store with replace, backup and recovery; draft and report stores

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 09.11: `WindowsCardEraser`, `WindowsCardEraserFactory` and `WindowsVolumeFacts`

**Files:**
- Create: `src/UasSort.Platform/Card/VolumeFacts.cs`
- Create: `src/UasSort.Platform/Card/WindowsCardEraserFactory.cs`
- Create: `src/UasSort.Platform/Card/WindowsCardEraser.cs`
- Create: `tests/UasSort.Platform.Tests/CardEraserTests.cs`

**Interfaces:**
- Consumes (Part 02): `ICardEraserFactory`, `ICardEraser`, `EraseResult` (`EraseOk`, `EraseError`), `CardSource`, `CardIdentity`, `ConfirmedCleanupPlan` (`Plan.CardRoot`, `Plan.Card`), `IoOp.CardDelete`, `UnsafeIoException`. (Part 08, Testing): `UasSort.Testing.CleanupPlanFixtures.Confirmed(string cardRoot, CardIdentity card, IReadOnlyList<string> fileRelPaths, IReadOnlyList<string> setFolderRelDirs, TimeProvider clock)` (built through the internal `CleanupPlan`/`ConfirmedCleanupPlan` constructors; `FilePaths`/`SetFolders` are `PathRules.Join(cardRoot, rel)`, so `\` or `/` relative paths both work). This part: `IoGate`, `GuardContexts`, `VolumeQuery`, `WindowsDirectoryLister`.
- Produces (defined here):
  - `internal sealed record VolumeFacts(CardIdentity Identity, bool IsReadOnlyVolume, string BusType, bool RemovableMedia, bool IsSystemBootOrPaging)`
  - `internal interface IVolumeFacts { string? VolumePathName(string path); VolumeFacts? Describe(string volumeRoot); }` and `internal sealed class WindowsVolumeFacts : IVolumeFacts` (`GetVolumePathNameW`, `GetVolumeInformationW`, `GetDiskFreeSpaceExW`, `IOCTL_STORAGE_QUERY_PROPERTY` on `\\.\X:` opened with no data access, system/boot/paging).
  - `public sealed class WindowsCardEraserFactory : ICardEraserFactory` — public constructor `(Settings settings, string appDataDir, string machine, IPathFacts facts, IDirectoryLister lister)` uses `WindowsVolumeFacts` and the real system volume; the internal constructor adds `(IVolumeFacts volumeFacts, string systemVolumeRoot)` (tests only: the `%TEMP%` fake card lies on the system volume, which guard rule 1b refuses for every `CardDelete`). `Open` throws `UnsafeIoException` naming the failed check, in this order: browsed source; write-protected source; not a volume root; facts unavailable; file system not exFAT/FAT32; identity differs from `pinned`; read-only volume; bus not `Sd`/`Mmc` and not `Usb` with removable media; system/boot/paging volume or the system volume root; a configured root, previous photo root or the app-data folder on the volume; no `MISC\FC*.db` and no `MISC\IDX\`; the plan's card root or identity differ. Only then does it build the eraser's own `GuardContext` (`CardIsVerifiedCardVolume = true`, `Cleanup = plan`).
  - `public sealed partial class WindowsCardEraser : ICardEraser` — the only declarations of `DeleteFileW` and `RemoveDirectoryW` in the repository; each call is preceded by `IoGate.Require(IoOp.CardDelete, …)` and uses a `\\?\` path; failures return `EraseError(win32Error, message)`; never clears attributes, never opens a file, never recurses.

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/CardEraserTests.cs`:

```csharp
namespace UasSort.Platform.Tests;

public sealed class CardEraserTests
{
    private static readonly CardIdentity Card = new(0x1A2B3C4D, null, "exFAT", 256_060_514_304);
    private const string Mp4 = @"DCIM\DJI_001\DJI_20260725232655_0117_D.MP4";
    private const string Lrf = @"DCIM\DJI_001\DJI_20260725232655_0117_D.LRF";
    private const string Trinf = @"DCIM\DJI_001\.DJI_20260725232655_0117_D.MP4.trinf";
    private const string Sibling = @"DCIM\DJI_001\DJI_20260726022937_0118_D.MP4";
    private const string ReadOnlyDng = @"DCIM\DJI_001\DJI_20260725233000_0116_D.DNG";
    private const string Set = @"DCIM\PANORAMA\001_0087";

    public enum Fault { None, NotVolumeRoot, NoFacts, Ntfs, OtherIdentity, ReadOnlyVolume, FixedUsb, Nvme, SystemVolume, RootOnVolume, NoMisc }

    private sealed class FakeVolumeFacts(string cardRoot, Fault fault, string videoRoot) : IVolumeFacts
    {
        private static string Sep(string p) => p.EndsWith('\\') ? p : p + '\\';

        public string? VolumePathName(string path)
        {
            if (fault == Fault.RootOnVolume && path.StartsWith(videoRoot, StringComparison.OrdinalIgnoreCase)) return Sep(cardRoot);
            if (path.StartsWith(cardRoot, StringComparison.OrdinalIgnoreCase))
                return fault == Fault.NotVolumeRoot ? Path.GetPathRoot(cardRoot) : Sep(cardRoot);
            return VolumeQuery.VolumePathName(path);
        }

        public VolumeFacts? Describe(string volumeRoot) => fault switch
        {
            Fault.NoFacts => null,
            Fault.Ntfs => new(Card with { FileSystem = "NTFS" }, false, "Sd", true, false),
            Fault.OtherIdentity => new(Card with { VolumeSerial = 0xDEADBEEF }, false, "Sd", true, false),
            Fault.ReadOnlyVolume => new(Card, true, "Sd", true, false),
            Fault.FixedUsb => new(Card, false, "Usb", false, false),
            Fault.Nvme => new(Card, false, "Nvme", false, false),
            Fault.SystemVolume => new(Card, false, "Sd", true, true),
            _ => new(Card, false, "Sd", true, false),
        };
    }

    private static void MakeCard(TestEnv env)
    {
        foreach (var f in new[] { Mp4, Lrf, Sibling, @"DCIM\PANORAMA\001_0087\PANO_0001.DNG", @"DCIM\PANORAMA\001_0087\PANO_0002.DNG",
                                  @"DCIM\PANORAMA\001_0088\PANO_0001.DNG", @"MISC\FC9113.db" })
            env.Temp.File(@"card\" + f);
        env.Temp.File(@"card\" + Trinf, attributes: FileAttributes.Hidden);
        env.Temp.File(@"card\" + ReadOnlyDng, attributes: FileAttributes.ReadOnly);
    }

    private static ConfirmedCleanupPlan Plan(TestEnv env, string[] files, string[] setFolders, string? cardRoot = null)
        => CleanupPlanFixtures.Confirmed(cardRoot ?? env.C(env.CardRoot), Card, files, setFolders,
                                         new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 19, 30, 0, TimeSpan.Zero)));

    private static WindowsCardEraserFactory Factory(TestEnv env, Fault fault = Fault.None)
        => new(env.Settings, env.AppData, TestEnv.Machine, env.Facts, new WindowsDirectoryLister(),
               new FakeVolumeFacts(env.C(env.CardRoot), fault, env.C(env.VideoRoot)), $@"{Cmd.FreeDriveLetter()}:\");

    private static CardSource Source(TestEnv env, bool browsed = false, bool writeProtected = false)
        => new(env.C(env.CardRoot), Card, browsed, writeProtected);

    private static string[] Listing(TestEnv env)
        => new WindowsDirectoryLister().Enumerate(env.CardRoot, true, new HashSet<string>()).Entries
                                       .Select(e => e.RelPath).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void DeleteFile_RemovesExactlyTheNamedFiles_IncludingAHiddenTrinf()
    {
        using var env = new TestEnv();
        MakeCard(env);
        var before = Listing(env);
        using var eraser = Factory(env).Open(Source(env), Card, Plan(env, [Lrf, Trinf, Mp4], []));
        foreach (var f in new[] { Lrf, Trinf, Mp4 }) Assert.True(eraser.DeleteFile(f) is EraseOk, f);
        Assert.Equal(before.Except([Lrf, Trinf, Mp4]).ToArray(), Listing(env));
    }

    [Fact]
    public void RemoveEmptySetFolder_FailsWhileNotEmpty_ThenRemovesIt()
    {
        using var env = new TestEnv();
        MakeCard(env);
        string[] members = [$@"{Set}\PANO_0001.DNG", $@"{Set}\PANO_0002.DNG"];
        using var eraser = Factory(env).Open(Source(env), Card, Plan(env, members, [Set]));
        Assert.True(eraser.RemoveEmptySetFolder(Set) is EraseError { Win32Error: 145 });
        Assert.True(Directory.Exists(Path.Join(env.CardRoot, Set)));
        foreach (var m in members) Assert.True(eraser.DeleteFile(m) is EraseOk);
        Assert.True(eraser.RemoveEmptySetFolder(Set) is EraseOk);
        Assert.False(Directory.Exists(Path.Join(env.CardRoot, Set)));
        Assert.True(Directory.Exists(Path.Join(env.CardRoot, @"DCIM\PANORAMA\001_0088")));
    }

    [Fact]
    public void ReadOnlyFile_FailsWithAccessDenied_AndIsUnchanged()
    {
        using var env = new TestEnv();
        MakeCard(env);
        using var eraser = Factory(env).Open(Source(env), Card, Plan(env, [ReadOnlyDng], []));
        Assert.True(eraser.DeleteFile(ReadOnlyDng) is EraseError { Win32Error: 5 });
        Assert.True(File.GetAttributes(Path.Join(env.CardRoot, ReadOnlyDng)).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void PathsOutsideThePlan_ThrowBeforeAnyWin32Call()
    {
        using var env = new TestEnv();
        MakeCard(env);
        env.Temp.File(@"photo\DJI_0001.DNG");
        using var eraser = Factory(env).Open(Source(env), Card, Plan(env, [Mp4], [Set]));
        Assert.Throws<UnsafeIoException>(() => eraser.DeleteFile(Sibling));
        Assert.Throws<UnsafeIoException>(() => eraser.RemoveEmptySetFolder(@"DCIM\DJI_001"));
        Assert.Throws<UnsafeIoException>(() => eraser.RemoveEmptySetFolder("MISC"));
        Assert.Throws<UnsafeIoException>(() => eraser.DeleteFile(@"..\photo\DJI_0001.DNG"));
        Assert.True(File.Exists(Path.Join(env.CardRoot, Sibling)));
        Assert.True(File.Exists(Path.Join(env.PhotoRoot, "DJI_0001.DNG")));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Factory_RefusesBrowsedOrWriteProtectedSources(bool browsed, bool writeProtected)
    {
        using var env = new TestEnv();
        MakeCard(env);
        Assert.Throws<UnsafeIoException>(() => Factory(env).Open(Source(env, browsed, writeProtected), Card, Plan(env, [Mp4], [])));
    }

    [Theory]
    [InlineData(Fault.NotVolumeRoot)]
    [InlineData(Fault.NoFacts)]
    [InlineData(Fault.Ntfs)]
    [InlineData(Fault.OtherIdentity)]
    [InlineData(Fault.ReadOnlyVolume)]
    [InlineData(Fault.FixedUsb)]
    [InlineData(Fault.Nvme)]
    [InlineData(Fault.SystemVolume)]
    [InlineData(Fault.RootOnVolume)]
    [InlineData(Fault.NoMisc)]
    public void Factory_RefusesAVolumeThatFailsAnyCheck(Fault fault)
    {
        using var env = new TestEnv();
        MakeCard(env);
        if (fault == Fault.NoMisc) Directory.Delete(Path.Join(env.CardRoot, "MISC"), recursive: true);
        Assert.Throws<UnsafeIoException>(() => Factory(env, fault).Open(Source(env), Card, Plan(env, [Mp4], [])));
    }

    [Fact]
    public void Factory_RefusesAPlanForAnotherCardRoot()
    {
        using var env = new TestEnv();
        MakeCard(env);
        var other = env.Temp.Sub("other-card");
        Assert.Throws<UnsafeIoException>(() => Factory(env).Open(Source(env), Card, Plan(env, [Mp4], [], env.C(other))));
    }

    [Fact]
    public void ProductionFactory_RefusesATempFolder()
    {
        using var env = new TestEnv();
        MakeCard(env);
        var production = new WindowsCardEraserFactory(env.Settings, env.AppData, TestEnv.Machine, env.Facts, new WindowsDirectoryLister());
        var ex = Assert.Throws<UnsafeIoException>(() => production.Open(Source(env), Card, Plan(env, [Mp4], [])));
        Assert.Contains("volume root", ex.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Join(env.CardRoot, Mp4)));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*CardEraserTests*"`
Expected: build FAILS with CS0246 "The type or namespace name 'IVolumeFacts' could not be found" (and `WindowsCardEraserFactory`, `VolumeFacts`).

- [ ] **Step 3: Implement**

`src/UasSort.Platform/Card/VolumeFacts.cs`:

```csharp
using UasSort.Platform.Io;

namespace UasSort.Platform.Card;

internal sealed record VolumeFacts(CardIdentity Identity, bool IsReadOnlyVolume, string BusType, bool RemovableMedia, bool IsSystemBootOrPaging);

/// <summary>The factory's seam (Ref §4.1 ICardEraserFactory): production reads Win32; only Platform.Tests substitutes it.</summary>
internal interface IVolumeFacts
{
    string? VolumePathName(string path);
    VolumeFacts? Describe(string volumeRoot);
}

internal sealed class WindowsVolumeFacts : IVolumeFacts
{
    public string? VolumePathName(string path) => VolumeQuery.VolumePathName(path);

    public VolumeFacts? Describe(string volumeRoot)
    {
        var info = VolumeQuery.Information(volumeRoot);
        var identity = VolumeQuery.Identity(volumeRoot);
        var bus = VolumeQuery.Bus(volumeRoot);
        if (info is null || identity is null || bus is null) return null;
        return new VolumeFacts(identity, (info.Flags & Kernel32.FILE_READ_ONLY_VOLUME) != 0, bus.Value.BusType, bus.Value.RemovableMedia,
                               VolumeQuery.IsSystemBootOrPaging(volumeRoot));
    }
}
```

`src/UasSort.Platform/Card/WindowsCardEraserFactory.cs`:

```csharp
using System.IO.Enumeration;
using UasSort.Platform.Io;

namespace UasSort.Platform.Card;

/// <summary>Re-derives the cleanup volume check from Win32 (Ref §10.6); never trusts CardSource flags or a caller's context.</summary>
public sealed class WindowsCardEraserFactory : ICardEraserFactory
{
    private static readonly IReadOnlySet<string> NoExclusions = new HashSet<string>();
    private readonly Settings _settings;
    private readonly string _appDataDir;
    private readonly string _machine;
    private readonly IPathFacts _facts;
    private readonly IDirectoryLister _lister;
    private readonly IVolumeFacts _volumes;
    private readonly string _systemVolumeRoot;

    public WindowsCardEraserFactory(Settings settings, string appDataDir, string machine, IPathFacts facts, IDirectoryLister lister)
        : this(settings, appDataDir, machine, facts, lister, new WindowsVolumeFacts(), KnownFolders.SystemVolumeRoot()) { }

    internal WindowsCardEraserFactory(Settings settings, string appDataDir, string machine, IPathFacts facts, IDirectoryLister lister,
                                      IVolumeFacts volumeFacts, string systemVolumeRoot)
    {
        _settings = settings;
        _appDataDir = appDataDir;
        _machine = machine;
        _facts = facts;
        _lister = lister;
        _volumes = volumeFacts;
        _systemVolumeRoot = systemVolumeRoot;
    }

    public ICardEraser Open(CardSource source, CardIdentity pinned, ConfirmedCleanupPlan plan)
    {
        if (source.IsBrowsedFolder) throw Refuse("the source is a browsed folder, not a detected card");
        if (source.IsWriteProtected) throw Refuse("the card is write-protected");
        var root = _facts.Canonical(source.Root);
        if (_volumes.VolumePathName(root) is not { } volumeRoot || !Same(volumeRoot, root)) throw Refuse($"{root} is not a volume root");
        var v = _volumes.Describe(Sep(root)) ?? throw Refuse($"the facts of volume {root} can't be read");
        if (v.Identity.FileSystem is not ("exFAT" or "FAT32")) throw Refuse($"file system {v.Identity.FileSystem} is not exFAT or FAT32");
        if (v.Identity != pinned) throw Refuse("the volume's identity differs from the scanned card");
        if (v.IsReadOnlyVolume) throw Refuse("the volume is write-protected");
        if (!(v.BusType is "Sd" or "Mmc" || (v.BusType == "Usb" && v.RemovableMedia)))
            throw Refuse($"bus {v.BusType} (removable media: {v.RemovableMedia}) is not a card reader");
        if (v.IsSystemBootOrPaging || Same(root, _systemVolumeRoot)) throw Refuse("it is the system, boot or paging volume");
        foreach (var configured in new[] { _settings.VideoRoot, _settings.PhotoRoot, _appDataDir }.Concat(_settings.PreviousPhotoRoots))
            if (_volumes.VolumePathName(_facts.Canonical(configured)) is { } on && Same(on, root))
                throw Refuse($"{configured} lies on this volume");
        var misc = _lister.Enumerate(Path.Join(root, "MISC"), recurse: false, NoExclusions).Entries;
        if (!misc.Any(e => e.IsDirectory ? string.Equals(Path.GetFileName(e.FullPath), "IDX", StringComparison.OrdinalIgnoreCase)
                                         : FileSystemName.MatchesSimpleExpression("FC*.db", Path.GetFileName(e.FullPath), ignoreCase: true)))
            throw Refuse(@"no drone index (MISC\FC*.db or MISC\IDX\)");
        if (!Same(_facts.Canonical(plan.Plan.CardRoot), root)) throw Refuse("the confirmed plan names another card root");
        if (plan.Plan.Card != pinned) throw Refuse("the confirmed plan names another card");

        var ctx = GuardContexts.For(_settings, _appDataDir, _machine, _facts, root) with
        {
            SystemVolumeRoot = _systemVolumeRoot, CardIsVerifiedCardVolume = true, Cleanup = plan,
        };
        return new WindowsCardEraser(ctx);
    }

    private static UnsafeIoException Refuse(string why) => new($"Card cleanup refused: {why}");
    private static string Sep(string p) => p.EndsWith('\\') ? p : p + '\\';
    private static bool Same(string a, string b) => string.Equals(Sep(a), Sep(b), StringComparison.OrdinalIgnoreCase);
}
```

`src/UasSort.Platform/Card/WindowsCardEraser.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using UasSort.Platform.Io;

namespace UasSort.Platform.Card;

/// <summary>The only code that deletes anything on a card (Ref §10.6). DeleteFileW and RemoveDirectoryW are declared here only.</summary>
public sealed partial class WindowsCardEraser : ICardEraser
{
    private readonly GuardContext _ctx;
    private readonly string _root;
    private bool _disposed;

    internal WindowsCardEraser(GuardContext ctx)
    {
        _ctx = ctx;
        _root = ctx.CardRoot ?? throw new ArgumentException("no card root", nameof(ctx));
    }

    public EraseResult DeleteFile(string cardRelPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var path = Resolve(cardRelPath);
        IoGate.Require(IoOp.CardDelete, path, _ctx);
#pragma warning disable RS0030 // IO layer: Card cleanup, confirmed plan only
        var ok = DeleteFileW(LongPath.Prefix(path));
#pragma warning restore RS0030
        return ok ? new EraseOk() : LastError();
    }

    public EraseResult RemoveEmptySetFolder(string cardRelDir)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var path = Resolve(cardRelDir);
        IoGate.Require(IoOp.CardDelete, path, _ctx);
#pragma warning disable RS0030 // IO layer: Card cleanup, confirmed plan only
        var ok = RemoveDirectoryW(LongPath.Prefix(path));
#pragma warning restore RS0030
        return ok ? new EraseOk() : LastError();
    }

    public void Dispose() => _disposed = true;

    private string Resolve(string rel) => Path.GetFullPath(Path.Join(_root, rel.Replace('/', '\\')));

    private static EraseError LastError()
    {
        var error = Marshal.GetLastPInvokeError();
        return new EraseError(error, new Win32Exception(error).Message);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "DeleteFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteFileW(string lpFileName);

    [LibraryImport("kernel32.dll", EntryPoint = "RemoveDirectoryW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveDirectoryW(string lpPathName);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*CardEraserTests*"`
Expected: PASS, `Passed: 18` (4 facts, 2 + 10 theory rows, 2 facts).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform tests/UasSort.Platform.Tests
git commit -m "feat: card eraser and factory with win32 cleanup volume check

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 09.12: Keep-awake, offload lock, eject, shell launcher, file log and app assets

**Files:**
- Create: `src/UasSort.Platform/Win32/Kernel32.Power.cs`
- Create: `src/UasSort.Platform/Win32/SetupApi.cs`
- Create: `src/UasSort.Platform/Shell/PowerRequest.cs`
- Create: `src/UasSort.Platform/Shell/OffloadLock.cs`
- Create: `src/UasSort.Platform/Shell/DeviceEject.cs`
- Create: `src/UasSort.Platform/Shell/ShellLauncher.cs`
- Create: `src/UasSort.Platform/Shell/AppAssets.cs`
- Create: `src/UasSort.Platform/Logging/FileLog.cs`
- Create: `tests/UasSort.Platform.Tests/RuntimeServicesTests.cs`

No single-instance code is written here: Part 01's `NamedMutexLock`, `SingleInstance` (`AppInstanceKey`, `MutexName`) and `ForegroundWindow` (`AllowSetForeground`, `BringToFront`, declared in Part 01's `NativeMethods`) in `UasSort.Platform.Win32` are the only definitions and are tested in Part 01 (`Win32/NamedMutexLockTests.cs`); the App's `Program.Main` and `SingleInstanceGate` (Part 01, extended by Part 11) use them directly. This task declares no `user32` import, no own mutex holder and no second `SingleInstance`.

**Interfaces:**
- Consumes (Part 01): `NamedMutexLock.TryAcquire(string name)` (existence-based, not thread-affine, so the async Commit and Card cleanup may release it from any thread; null only while another holder has the name). (Part 02): `IPowerRequest`, `IOffloadLock`, `IDeviceEject`, `EjectResult` (`Ejected`, `EjectRefused`), `IShellLauncher`, `IAppAssets`. This part: `VolumeQuery`, `IoGate`, `GuardContexts.ForAppData`, `Kernel32`.
- Produces (defined here):
  - `public sealed class PowerRequest : IPowerRequest` — `PowerCreateRequest` (simple reason string) + `PowerSetRequest(PowerRequestSystemRequired)`; the returned handle clears and closes on `Dispose` (idempotent).
  - `public sealed class OffloadLock : IOffloadLock` — wraps Part 01's `NamedMutexLock.TryAcquire(@"Local\uas-sort-offload")`; `TryAcquire()` returns null only when another holder has the name (Ref §4.1; `CommitSession` passes Preflight a stand-in while it holds the lock, so no re-entrancy is needed); the internal constructor takes another name for tests.
  - `public sealed class DeviceEject : IDeviceEject` — refuses without any PnP call a root that isn't a drive letter, isn't a mounted volume, or is the system/boot/paging volume; otherwise finds the disk by `IOCTL_STORAGE_GET_DEVICE_NUMBER` among `GUID_DEVINTERFACE_DISK` interfaces and calls `CM_Request_Device_EjectW` on the disk, then on its parent; a veto becomes `EjectRefused` with the veto type and name.
  - `public sealed class ShellLauncher : IShellLauncher` — `OpenFolder` (explorer), `OpenFile` (shell association), `OpenHttps` (https only; anything else throws `ArgumentException`).
  - `public sealed class FileLog(string appDataDir, string machine, IPathFacts facts, TimeProvider clock)` — `Info(string)`, `Warn(string)`, `Error(string, Exception? e = null)`, `Prune()`; `logs\uas-sort-yyyyMMdd.log` (local date), lines `yyyy-MM-ddTHH:mm:ss.fffZ LEVEL message`; `Prune` deletes logs 14 or more days old.
  - `public sealed class AppAssets(string appDir, System.Reflection.Assembly selfTestAssembly) : IAppAssets` — `OpenPlaces()` reads `appDir\places.bin.gz`; `OpenSelfTest(name)` returns the embedded resource whose name ends with `.SelfTest.{name}`, else `FileNotFoundException`.

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/RuntimeServicesTests.cs`:

```csharp
namespace UasSort.Platform.Tests;

public sealed class RuntimeServicesTests
{
    private static string UniqueName() => @"Local\uas-sort-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void PowerRequest_IsCreatedAndCleared()
    {
        var awake = new PowerRequest().KeepSystemAwake("Offloading drone media");
        awake.Dispose();
        awake.Dispose();
    }

    [Fact]
    public void OffloadLock_IsExclusive_AndReleasableFromAnotherThread()
    {
        var name = UniqueName();
        var first = new OffloadLock(name).TryAcquire();
        Assert.NotNull(first);
        Assert.Null(new OffloadLock(name).TryAcquire());
        Task.Run(() => first!.Dispose()).Wait();
        using var again = new OffloadLock(name).TryAcquire();
        Assert.NotNull(again);
    }

    [Fact]
    public void Eject_RefusesTheSystemVolume_AndAMissingDrive_WithoutPnPCalls()
    {
        var eject = new DeviceEject();
        Assert.True(eject.Eject(KnownFolders.SystemVolumeRoot()) is EjectRefused r1 && r1.Reason.Contains("system", StringComparison.OrdinalIgnoreCase));
        Assert.True(eject.Eject($@"{Cmd.FreeDriveLetter()}:\") is EjectRefused);
        Assert.True(eject.Eject(Path.GetTempPath()) is EjectRefused);
    }

    [Fact]
    public void ShellLauncher_ValidatesBeforeLaunching()
    {
        using var t = new TempDir();
        var shell = new ShellLauncher();
        Assert.Throws<ArgumentException>(() => shell.OpenHttps(new Uri("http://example.com/")));
        Assert.Throws<ArgumentException>(() => shell.OpenHttps(new Uri("file:///C:/Windows/notepad.exe")));
        Assert.Throws<DirectoryNotFoundException>(() => shell.OpenFolder(Path.Join(t.Path, "missing")));
        Assert.Throws<FileNotFoundException>(() => shell.OpenFile(Path.Join(t.Path, "missing.json")));
    }

    [Fact]
    public void FileLog_AppendsDailyFiles_AndPrunesAfter14Days()
    {
        using var env = new TestEnv();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 21, 7, 2, TimeSpan.Zero));
        var log = new FileLog(env.AppData, TestEnv.Machine, env.Facts, clock);
        log.Info("scan started");
        log.Error("copy failed", new IOException("disk full"));
        var today = Path.Join(env.AppData, "logs", "uas-sort-20260927.log");
        var lines = File.ReadAllLines(today);
        Assert.StartsWith("2026-09-27T21:07:02.000Z INFO scan started", lines[0], StringComparison.Ordinal);
        Assert.Contains("ERROR copy failed", lines[1], StringComparison.Ordinal);
        Assert.Contains("disk full", string.Join('\n', lines), StringComparison.Ordinal);
        env.Temp.File(@"appdata\logs\uas-sort-20260913.log");
        env.Temp.File(@"appdata\logs\uas-sort-20260914.log");
        env.Temp.File(@"appdata\logs\other.txt");
        log.Prune();
        Assert.False(File.Exists(Path.Join(env.AppData, "logs", "uas-sort-20260913.log")));
        Assert.True(File.Exists(Path.Join(env.AppData, "logs", "uas-sort-20260914.log")));
        Assert.True(File.Exists(Path.Join(env.AppData, "logs", "other.txt")));
        Assert.True(File.Exists(today));
    }

    [Fact]
    public void AppAssets_OpenPlacesFromTheAppFolder_AndMissingSelfTestThrows()
    {
        using var t = new TempDir();
        t.File("places.bin.gz", 32);
        var assets = new AppAssets(t.Path, typeof(RuntimeServicesTests).Assembly);
        using (var s = assets.OpenPlaces()) Assert.Equal(32, s.Length);
        Assert.Throws<FileNotFoundException>(() => assets.OpenSelfTest("selftest.dng"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*RuntimeServicesTests*"`
Expected: build FAILS with CS0246 "The type or namespace name 'PowerRequest' could not be found" (and `OffloadLock`, `DeviceEject`, `ShellLauncher`, `FileLog`, `AppAssets`).

- [ ] **Step 3: Implement**

`src/UasSort.Platform/Win32/Kernel32.Power.cs`:

```csharp
using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

[StructLayout(LayoutKind.Sequential)]
internal struct ReasonContext
{
    public uint Version;                 // POWER_REQUEST_CONTEXT_VERSION = 0
    public uint Flags;                   // POWER_REQUEST_CONTEXT_SIMPLE_STRING = 1
    public nint SimpleReasonString;
    public nint DetailedPadding1, DetailedPadding2;   // the union's Detailed arm is 24 bytes on 64-bit
}

[StructLayout(LayoutKind.Sequential)]
internal struct StorageDeviceNumber
{
    public uint DeviceType, DeviceNumber, PartitionNumber;
}

internal static partial class Kernel32
{
    public const int PowerRequestSystemRequired = 1;

    [LibraryImport("kernel32.dll", EntryPoint = "PowerCreateRequest", SetLastError = true)]
    public static partial nint PowerCreateRequest(in ReasonContext context);

    [LibraryImport("kernel32.dll", EntryPoint = "PowerSetRequest", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PowerSetRequest(nint powerRequest, int requestType);

    [LibraryImport("kernel32.dll", EntryPoint = "PowerClearRequest", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PowerClearRequest(nint powerRequest, int requestType);
}
```

`src/UasSort.Platform/Win32/SetupApi.cs`:

```csharp
using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

[StructLayout(LayoutKind.Sequential)]
internal struct SpDeviceInterfaceData
{
    public uint CbSize;
    public Guid InterfaceClassGuid;
    public uint Flags;
    public nuint Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SpDevinfoData
{
    public uint CbSize;
    public Guid ClassGuid;
    public uint DevInst;
    public nuint Reserved;
}

internal static unsafe partial class SetupApi
{
    public const uint DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
    public static readonly Guid GuidDevInterfaceDisk = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    public static partial nint SetupDiGetClassDevs(in Guid classGuid, nint enumerator, nint hwndParent, uint flags);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiEnumDeviceInterfaces", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, nint deviceInfoData, in Guid interfaceClassGuid,
                                                           uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDeviceInterfaceDetail(nint deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData,
                                                               byte* detail, uint detailSize, out uint requiredSize, ref SpDevinfoData deviceInfoData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiDestroyDeviceInfoList", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);
}

internal static unsafe partial class CfgMgr32
{
    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Parent")]
    public static partial uint CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Request_Device_EjectW")]
    public static partial uint CM_Request_Device_Eject(uint devInst, out int vetoType, char* vetoName, uint nameLength, uint flags);
}
```

`src/UasSort.Platform/Shell/PowerRequest.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace UasSort.Platform.Shell;

/// <summary>Keeps the PC awake during Commit and Card cleanup (Ref §10.3 Invariants, §10.6 step 1).</summary>
public sealed class PowerRequest : IPowerRequest
{
    public IDisposable KeepSystemAwake(string reason) => new Held(reason);

    private sealed class Held : IDisposable
    {
        private nint _handle;
        private nint _reason;

        public Held(string reason)
        {
            _reason = Marshal.StringToHGlobalUni(reason);
            var context = new ReasonContext { Version = 0, Flags = 1, SimpleReasonString = _reason };
            _handle = Kernel32.PowerCreateRequest(in context);
            if (_handle == -1 || !Kernel32.PowerSetRequest(_handle, Kernel32.PowerRequestSystemRequired))
            {
                var error = Marshal.GetLastPInvokeError();
                Dispose();
                throw new Win32Exception(error, "PowerCreateRequest/PowerSetRequest failed");
            }
        }

        public void Dispose()
        {
            if (_handle is not (0 or -1))
            {
                Kernel32.PowerClearRequest(_handle, Kernel32.PowerRequestSystemRequired);
                Kernel32.CloseHandle(_handle);
            }
            _handle = 0;
            if (_reason != 0) Marshal.FreeHGlobal(_reason);
            _reason = 0;
        }
    }
}
```

`src/UasSort.Platform/Shell/OffloadLock.cs`:

```csharp
namespace UasSort.Platform.Shell;

/// <summary>Local\uas-sort-offload, held for a whole Commit or Card cleanup (Ref §4.1, §10.6 step 1).
/// Wraps Part 01's NamedMutexLock (existence-based, not thread-affine: any thread may dispose it).</summary>
public sealed class OffloadLock : IOffloadLock
{
    public const string MutexName = @"Local\uas-sort-offload";
    private readonly string _name;

    public OffloadLock() : this(MutexName) { }
    internal OffloadLock(string name) => _name = name;

    /// <returns>The held lock, or null only when another holder (another process, or a still-held lock here) has the name.</returns>
    public IDisposable? TryAcquire() => NamedMutexLock.TryAcquire(_name);
}
```

`src/UasSort.Platform/Shell/DeviceEject.cs`:

```csharp
using UasSort.Platform.Io;

namespace UasSort.Platform.Shell;

/// <summary>[Eject E:] (Ref §10.5, §10.6): the PnP eject flushes the volume; no write or flush handle is ever opened on it.</summary>
public sealed unsafe class DeviceEject : IDeviceEject
{
    public EjectResult Eject(string volumeRoot)
    {
        if (volumeRoot.Length is not (2 or 3) || !char.IsAsciiLetter(volumeRoot[0]) || volumeRoot[1] != ':')
            return new EjectRefused(volumeRoot, "not a drive letter");
        var root = volumeRoot[..2] + "\\";
        if (VolumeQuery.Information(root) is null) return new EjectRefused(root, "no volume is mounted there");
        if (VolumeQuery.IsSystemBootOrPaging(root)) return new EjectRefused(root, "the system volume can't be ejected");
        if (DeviceNumber(@"\\.\" + root[..2]) is not { } number) return new EjectRefused(root, "the disk number can't be read");
        if (FindDisk(number) is not { } disk) return new EjectRefused(root, "the disk device wasn't found");

        var reason = "";
        var name = stackalloc char[260];                         // outside the loop (CA2014)
        foreach (var candidate in Candidates(disk))
        {
            name[0] = '\0';
            var result = CfgMgr32.CM_Request_Device_Eject(candidate, out var veto, name, 260, 0);
            if (result == 0 && veto == 0) return new Ejected(root);
            reason = $"Windows refused (result {result}, veto {veto}: {new string(name)})";
        }
        return new EjectRefused(root, reason);
    }

    private static IEnumerable<uint> Candidates(uint disk)
    {
        yield return disk;
        if (CfgMgr32.CM_Get_Parent(out var parent, disk, 0) == 0) yield return parent;
    }

    private static StorageDeviceNumber? DeviceNumber(string devicePath)
    {
        using var handle = Kernel32.CreateFile(devicePath, 0, Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE, 0, Kernel32.OPEN_EXISTING, 0, 0);
        if (handle.IsInvalid) return null;
        StorageDeviceNumber number;
        return Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_STORAGE_GET_DEVICE_NUMBER, null, 0, &number,
                                        (uint)sizeof(StorageDeviceNumber), out _, 0) ? number : null;
    }

    private static uint? FindDisk(StorageDeviceNumber volume)
    {
        var set = SetupApi.SetupDiGetClassDevs(in SetupApi.GuidDevInterfaceDisk, 0, 0, SetupApi.DIGCF_PRESENT | SetupApi.DIGCF_DEVICEINTERFACE);
        if (set == -1) return null;
        var detail = stackalloc byte[2048];                        // outside the loop (CA2014)
        try
        {
            for (uint i = 0; ; i++)
            {
                var iface = new SpDeviceInterfaceData { CbSize = (uint)sizeof(SpDeviceInterfaceData) };
                if (!SetupApi.SetupDiEnumDeviceInterfaces(set, 0, in SetupApi.GuidDevInterfaceDisk, i, ref iface)) return null;
                var info = new SpDevinfoData { CbSize = (uint)sizeof(SpDevinfoData) };
                new Span<byte>(detail, 2048).Clear();
                *(uint*)detail = (uint)(IntPtr.Size == 8 ? 8 : 6);      // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize
                if (!SetupApi.SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, 2048, out _, ref info)) continue;
                var path = new string((char*)(detail + 4));
                if (DeviceNumber(path) is { } disk && disk.DeviceNumber == volume.DeviceNumber && disk.DeviceType == volume.DeviceType)
                    return info.DevInst;
            }
        }
        finally { SetupApi.SetupDiDestroyDeviceInfoList(set); }
    }
}
```

`src/UasSort.Platform/Shell/ShellLauncher.cs`:

```csharp
using System.Diagnostics;

namespace UasSort.Platform.Shell;

public sealed class ShellLauncher : IShellLauncher
{
    public void OpenFolder(string path)
    {
        var full = Path.GetFullPath(path);
        if (Kernel32.TryGetAttributes(full, out _) is not uint a || (a & Kernel32.FILE_ATTRIBUTE_DIRECTORY) == 0)
            throw new DirectoryNotFoundException(full);
        var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        psi.ArgumentList.Add(full);
        Start(psi);
    }

    public void OpenFile(string path)
    {
        var full = Path.GetFullPath(path);
        if (Kernel32.TryGetAttributes(full, out _) is not uint a || (a & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0)
            throw new FileNotFoundException("Nothing to open", full);
        Start(new ProcessStartInfo(full) { UseShellExecute = true });
    }

    public void OpenHttps(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps) throw new ArgumentException($"Only https links open: {uri}", nameof(uri));
        Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private static void Start(ProcessStartInfo psi)
    {
#pragma warning disable RS0030 // IO layer: IShellLauncher is the one place that starts processes (Ref §2.4 Processes)
        using var _ = Process.Start(psi);
#pragma warning restore RS0030
    }
}
```

`src/UasSort.Platform/Shell/AppAssets.cs`:

```csharp
using System.Reflection;

namespace UasSort.Platform.Shell;

/// <summary>App-folder and embedded assets only (Ref §4.1 IAppAssets).</summary>
public sealed class AppAssets(string appDir, Assembly selfTestAssembly) : IAppAssets
{
    public Stream OpenPlaces()
    {
#pragma warning disable RS0030 // IO layer: the app's own install folder, read-only
        return new FileStream(Path.Join(appDir, "places.bin.gz"), FileMode.Open, FileAccess.Read, FileShare.Read);
#pragma warning restore RS0030
    }

    public Stream OpenSelfTest(string name)
    {
        var resource = selfTestAssembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(".SelfTest." + name, StringComparison.Ordinal));
        return (resource is null ? null : selfTestAssembly.GetManifestResourceStream(resource))
               ?? throw new FileNotFoundException($"Embedded self-test asset {name} not found");
    }
}
```

`src/UasSort.Platform/Logging/FileLog.cs`:

```csharp
using System.Globalization;
using System.Text;
using UasSort.Platform.Io;

namespace UasSort.Platform.Logging;

/// <summary>logs\uas-sort-yyyyMMdd.log under %LOCALAPPDATA%\uas-sort, kept 14 days (Ref §11).</summary>
public sealed class FileLog(string appDataDir, string machine, IPathFacts facts, TimeProvider clock)
{
    private const int KeepDays = 14;
    private readonly Lock _gate = new();
    private readonly GuardContext _ctx = GuardContexts.ForAppData(appDataDir, machine, facts);
    private readonly string _dir = Path.Join(facts.Canonical(appDataDir), "logs");

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? e = null) => Write("ERROR", e is null ? message : $"{message}\n{e}");

    public void Prune()
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        foreach (var file in new WindowsDirectoryLister().Enumerate(_dir, recurse: false, new HashSet<string>()).Entries.Where(e => !e.IsDirectory))
        {
            var name = Path.GetFileName(file.FullPath);
            if (!name.StartsWith("uas-sort-", StringComparison.Ordinal) || !name.EndsWith(".log", StringComparison.Ordinal)) continue;
            if (!DateOnly.TryParseExact(name["uas-sort-".Length..^".log".Length], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                continue;
            if (today.DayNumber - day.DayNumber < KeepDays) continue;
            IoGate.Require(IoOp.Delete, file.FullPath, _ctx);
#pragma warning disable RS0030 // IO layer: the app's own log files
            File.Delete(file.FullPath);
#pragma warning restore RS0030
        }
    }

    private void Write(string level, string message)
    {
        var now = clock.GetUtcNow();
        var line = $"{now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)}Z {level} {message}\n";
        var path = Path.Join(_dir, $"uas-sort-{clock.GetLocalNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
        lock (_gate)
        {
            // The policy has no append op outside the ledger; under AppDataDir (rule 5) every op is allowed and rule 1 still refuses placeholders.
            IoGate.Require(IoOp.CreateDir, _dir, _ctx);
            IoGate.Require(IoOp.CreateNew, path, _ctx);
#pragma warning disable RS0030 // IO layer: the app's own log files
            Directory.CreateDirectory(_dir);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
#pragma warning restore RS0030
            stream.Write(Encoding.UTF8.GetBytes(line));
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*RuntimeServicesTests*"`
Expected: PASS, `Passed: 6`.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Platform tests/UasSort.Platform.Tests
git commit -m "feat: keep-awake, offload lock over NamedMutexLock, eject, shell launcher, file log and app assets

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 09.13: Source guards — eraser-only deletes, justified suppressions

**Files:**
- Create: `tests/UasSort.Platform.Tests/SourceGuardTests.cs`

**Interfaces:**
- Consumes (Part 01): `UasSort.Testing.RepoPaths` (`Root`, `Of(string relativePath)`, `EnumerateFiles(string searchPattern, params string[] topFolders)`, which skips `bin`, `obj`, `.git`, `artifacts`, `node_modules`, `TestResults`; `Relative(string fullPath)`). The `BannedSymbols.txt` content test is Part 01's (`tests/UasSort.Platform.Tests/Build/BannedSymbolsTests.cs`, Task 01.3); it is not repeated here, and this task never edits `BannedSymbols.txt`. This part: every Platform source file.
- Produces (defined here): the two guard tests below. They are content tests, so they fail on a later regression rather than on this task's code.

- [ ] **Step 1: Write the failing test**

`tests/UasSort.Platform.Tests/SourceGuardTests.cs`:

```csharp
using System.Text.RegularExpressions;

namespace UasSort.Platform.Tests;

public sealed class SourceGuardTests
{
    private static IEnumerable<string> SourceFiles(string topFolder) => RepoPaths.EnumerateFiles("*.cs", topFolder);

    [Fact]
    public void DeleteFileW_And_RemoveDirectoryW_AreDeclaredOnlyInWindowsCardEraser()
    {
        var eraser = RepoPaths.Of("src/UasSort.Platform/Card/WindowsCardEraser.cs");
        var offenders = SourceFiles("src")
            .Where(f => !string.Equals(Path.GetFullPath(f), eraser, StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f) is var text && (text.Contains("DeleteFileW", StringComparison.Ordinal)
                                                           || text.Contains("RemoveDirectoryW", StringComparison.Ordinal)))
            .Select(RepoPaths.Relative)
            .ToList();
        Assert.True(offenders.Count == 0, "Card deletes outside WindowsCardEraser: " + string.Join(", ", offenders));

        var lines = File.ReadAllLines(eraser);
        var calls = lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("= DeleteFileW(", StringComparison.Ordinal)
                                                             || x.l.Contains("= RemoveDirectoryW(", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Contains("// IO layer: Card cleanup, confirmed plan only", lines[c.i - 1], StringComparison.Ordinal));
    }

    [Fact]
    public void EveryRs0030Suppression_SaysWhyItIsAllowed()
    {
        var bad = SourceFiles("src")
            .SelectMany(f => File.ReadAllLines(f).Select((l, i) => (File: f, Line: i + 1, Text: l.Trim())))
            .Where(x => x.Text.StartsWith("#pragma warning disable RS0030", StringComparison.Ordinal)
                        && !Regex.IsMatch(x.Text, @"^#pragma warning disable RS0030 // IO layer: \S.{4,}$"))
            .Select(x => $"{RepoPaths.Relative(x.File)}:{x.Line}")
            .ToList();
        Assert.True(bad.Count == 0, "Unjustified RS0030 suppressions: " + string.Join(", ", bad));
        Assert.DoesNotContain(SourceFiles("src/UasSort.Core"),
                              f => File.ReadAllText(f).Contains("disable RS0030", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Temporarily prove the guards bite: add the line `// DeleteFileW` to `src/UasSort.Platform/Io/GuardedFileOps.cs` and the line `#pragma warning disable RS0030` (no reason) above the `DriveInfo.GetDrives()` loop's existing pragma in `src/UasSort.Platform/Io/WindowsVolumeProvider.cs`, then run:

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*SourceGuardTests*"`
Expected: FAIL — `DeleteFileW_And_RemoveDirectoryW_AreDeclaredOnlyInWindowsCardEraser` ("Card deletes outside WindowsCardEraser: src/UasSort.Platform/Io/GuardedFileOps.cs") and `EveryRs0030Suppression_SaysWhyItIsAllowed` ("Unjustified RS0030 suppressions: src/UasSort.Platform/Io/WindowsVolumeProvider.cs:…").

- [ ] **Step 3: Implement**

Remove the two temporary lines added in Step 2 (the production code of Tasks 09.1–09.12 already satisfies both guards). No other change.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*SourceGuardTests*"`
Expected: PASS, `Passed: 2`.

Run the whole Platform project, then the whole suite:

Run: `dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj`
Expected: PASS, `Failed: 0` — the 106 tests of Tasks 09.1–09.13 plus Part 01's Platform tests (`Build/BannedSymbolsTests`, the `BannedSymbols.txt` content check; `Win32/NamedMutexLockTests`; `Win32/PlaceholderModeTests`; `Stores/SelfTestSandboxTests`).

Run: `dotnet test --solution uas-sort.slnx`
Expected: every project passes, `Failed: 0`. Then `dotnet build-server shutdown` when run from WSL.

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Platform.Tests
git commit -m "test: eraser-only card deletes and justified RS0030 suppressions

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

## Part 09 — Produces (summary)

All in `UasSort.Platform` (`net11.0-windows10.0.26100.0`, `AllowUnsafeBlocks`, `InternalsVisibleTo UasSort.Platform.Tests`). Every Win32 call is a `LibraryImport` with a full signature; every allowed risky call carries `#pragma warning disable RS0030 // IO layer: {why}`.

| Namespace | Type | Implements / role | Composition (Parts 11–12) |
|---|---|---|---|
| `UasSort.Platform.Io` | `PathFacts` | `IPathFacts`: canonical paths via the nearest directory's `GetFinalPathNameByHandleW` (never opens a file), `CfGetSyncRootInfoByPath`, OneDrive + SyncRootManager roots | `new PathFacts()` |
| `UasSort.Platform.Io` | `KnownFolders` | `Pictures()` (`SHGetKnownFolderPath`), `AppDataDir()` (`%LOCALAPPDATA%\uas-sort`), `SystemVolumeRoot()` | Settings defaults, app data |
| `UasSort.Platform.Io` | `PlaceholderGuard` | `ReadAttributes` only (refuses unreadable attributes) | used by `IoGate`; process start calls Part 01's `PlaceholderMode.ExposePlaceholders()` (Program.Main, CLI `Main`) |
| `UasSort.Platform.Io` | `GuardContexts` | `For(settings, appDataDir, machine, facts, cardRoot?, newFolderDirs?, ownTemps?, renamed?)`, `ForAppData(...)` | — |
| `UasSort.Platform.Io` | `CloudOnlyFileException` | a cloud-only top-level ledger file (never a bug) | caught where `LedgerFolderState.CloudOnly` is shown |
| `UasSort.Platform.Io` | `WindowsDirectoryLister` | `IDirectoryLister` | `new WindowsDirectoryLister()` |
| `UasSort.Platform.Io` | `WindowsVolumeProvider` | `IVolumeProvider` (identity, read-only, NTFS, bus type, removable media, system/boot/paging) | `new WindowsVolumeProvider()` |
| `UasSort.Platform.Io` | `GuardedFileOps` | `IFileOps` for one Commit; `NewFolderDirs` used verbatim (decision 27) | `new GuardedFileOps(settings, appDataDir, Environment.MachineName, facts, OffloadCompiler.NewFolderDirs(batch, settings.VideoRoot))`, reached through `CommitEnvironment.FileOpsForRun` (`PlatformServices.FileOpsFor`); one instance per Commit, shared by Preflight and the copy (`CommitSession.Begin`) |
| `UasSort.Platform.Card` | `WindowsCardReaderFactory`, `WindowsCardReader` | `ICardReaderFactory`, `ICardReader` (read-only, identity, `Space()`) | `new WindowsCardReaderFactory(settings, appDataDir, machine, facts, lister)` |
| `UasSort.Platform.Card` | `WindowsCardEraserFactory`, `WindowsCardEraser` | `ICardEraserFactory`, `ICardEraser` (the only `DeleteFileW`/`RemoveDirectoryW`) | `new WindowsCardEraserFactory(settings, appDataDir, machine, facts, lister)` |
| `UasSort.Platform.Ledger` | `LedgerStore` | `ILedgerStore` + `LoadFromBackup()`; `Folder`, `OwnFile`, `BackupDir`; `Check` via `LedgerFolderStatusBuilder`, `Load` via `LedgerLoader`, lines via `LedgerCodec` (decision 37) | one per video root: `new LedgerStore(settings, appDataDir, machine, facts, lister, TimeProvider.System)`; [Copy] calls `CopyInto` on the **old** root's store |
| `UasSort.Platform.Stores` | `SettingsStore` (+ `ForFile` for CLI `--settings`), `DraftStore`, `ReportStore` | `ISettingsStore` (`SettingsCodec`, `SettingsLoadPolicy`, `SettingsRecovery`), `IDraftStore`, `IReportStore` (`CoreJsonContext`) | app data folder, `KnownFolders.Pictures()`, `TimeProvider.System` |
| `UasSort.Platform.Shell` | `PowerRequest`, `OffloadLock`, `DeviceEject`, `ShellLauncher`, `AppAssets` | `IPowerRequest`, `IOffloadLock` (over Part 01's `NamedMutexLock`), `IDeviceEject`, `IShellLauncher`, `IAppAssets` | `AppAssets(AppContext.BaseDirectory, typeof(App).Assembly)` |
| `UasSort.Platform.Logging` | `FileLog` | `Info`/`Warn`/`Error`, `Prune()` (14 days) | app start calls `Prune()` |

Not defined here: the single-instance and placeholder-mode helpers (`NamedMutexLock`, `SingleInstance`, `ForegroundWindow`, `PlaceholderMode` in `UasSort.Platform.Win32`) and `SelfTestSandbox` (`UasSort.Platform.Stores`) are Part 01's; `PlatformServices` (the composition record of every Platform port, namespace `UasSort.Platform`) is Part 11's (Task 11.3); `ReadOnlyTextFile` (`UasSort.Platform.Io`) is Part 12's; `IAppAssets.OpenMapAsset(string)` and its `AppAssets` implementation are added by Part 11 only if Task 11.7 reaches its fallback 2 (decision 44).

**Tests added** (`tests/UasSort.Platform.Tests`, 106): long paths and the temp fixture; canonical paths through junctions and `subst`; the guard gate (library read refused, cloud-only ledger, placeholder refusal, `.uas-sort2` look-alike, junction into `.uas-sort`); the lister (Hidden/System incl. a Hidden `.MP4` and `.trinf`, access-denied folder as an error, opens nothing under `FileShare.None` locks, `.uas-sort` excluded, junctions not entered); volumes; the card reader (deny-write ACL, refusals, placeholder); file ops (unbuffered verify and the recorded buffered fallback, mismatch, no-replace rename, times copied and Hidden cleared, preallocation, 300-character path, directory flush on NTFS, stale temps, `NewFolderDirs` used verbatim); the ledger store (every status, load opens only top-level `ledger*.jsonl` with a locked `notes.txt` beside them, a cloud-only ledger opens no ledger file, create-and-pin, pin only the folder, single writer, mirror, torn-tail repair, snapshots kept 20, [Copy] idempotent, backup load); the stores (replace + `.bak`, a missing file is not `Recovered`, corrupt copy, read-only load changes nothing, roots from the last run, drafts, reports); the card eraser on a `%TEMP%` fake card (exact deletes incl. a Hidden `.trinf`, emptied set folder only, read-only file refused, out-of-plan paths throw before Win32, every volume-check refusal, the production factory refuses `%TEMP%`); keep-awake, the offload lock, eject refusals, shell validation, logs, assets; and the source guards (eraser-only deletes, justified suppressions; the `BannedSymbols.txt` content test stays Part 01's).
