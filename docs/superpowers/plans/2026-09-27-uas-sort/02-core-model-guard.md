# Part 02 — Core model, ports and guard policy

**Goal.** Declare every shared Core type (the whole domain model of Ref §3 including both "Supporting types" blocks, the ledger records and the cleanup types), every port of Ref §4.1, the source-generated JSON contexts, the one pure IO rule set `IoGuardPolicy` (Ref §4.3, including the ledger exemption and the `CardDelete` rules), the in-memory `FakeFileSystem` with its hydration and card-delete tripwires and the fault hooks later parts inject, and the three card units `CardClassifier`, `CardDetector` and `CardSourceValidator` (Ref §5, §4.2). Implements Ref §3, §4.1, §4.3, §5 and the §13 test groups "IO guard policy", "Classification" and "Source validation" (Ref §14 step 2).

**Depends on:** Part 01 (the solution skeleton: `uas-sort.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `src/UasSort.Core/UasSort.Core.csproj` with MetadataExtractor, GeoTimeZone and System.IO.Hashing, the projects `src/UasSort.Review`, `src/UasSort.Platform`, `src/UasSort.App`, `src/UasSort.Cli`, `tests/UasSort.Review.Tests`, `tests/UasSort.Platform.Tests`, `tests/UasSort.Testing/UasSort.Testing.csproj` referencing Core, `tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj` referencing Core and Testing with xunit.v3.mtp-v2; every test project and `UasSort.Testing` reference `Microsoft.Extensions.TimeProvider.Testing`, and every test csproj imports `Xunit` through its csproj `Using` item (Part 01 Task 01.2); `RepoPaths` and `TestTempDir` in `UasSort.Testing`; the stack-proof canaries in `UasSort.Core.StackProof` and `StackProofJsonContext` in `UasSort.Core.Json`). Read `2026-09-27-uas-sort/00-interfaces.md` first: it wins over this part's text on names, namespaces, signatures and ownership.

**Conventions fixed by this part (later parts rely on them).**
- Every shared type, port and guard type lives in the single namespace `UasSort.Core` (files split by folder: `Model/`, `Ports/`, `Guard/`, `Ledger/LedgerPaths.cs`). Units live in folder namespaces (`UasSort.Core.Card` and `UasSort.Core.Json` here; `.Media`, `.Time`, `.Geo`, `.Library`, `.Ledger`, `.Config` (folder `Settings/`), `.Planning`, `.Editing`, `.Naming`, `.Offload`, `.Cleanup` in later parts, each anchored now by `src/UasSort.Core/Namespaces.cs`). Shared test fakes live in the flat namespace `UasSort.Testing`, one owner per fake (registry Testing table). Part 01 canaries stay in `UasSort.Core.StackProof` (explicit `using` only).
- Global usings are two files per project: the identical `GlobalUsings.Core.cs` (all nine projects that reference Core, created in Task 02.1) and a project-specific `GlobalUsings.cs` (none for Core). No namespace is imported twice by global usings in one project; `Xunit` comes only from the test csproj's `Using` item for `Xunit`. Source files therefore need no `using` line for `System.Collections.Immutable`, `UasSort.Core` or any Core namespace listed in `GlobalUsings.Core.cs`, nor (in Core.Tests) for `UasSort.Testing` or `Microsoft.Extensions.Time.Testing`.
- Card-relative paths (`ItemId.CardRelPath`, `CardEntry.RelPath`, ledger `src`) always use `/`. `FsEntry.RelPath` (lister output) uses the native `\`; `CardClassifier` converts. Full paths are Windows paths compared through `PathRules` (case-insensitive, `\` or `/`, `\\?\` stripped).
- Methods on shared types whose logic belongs to a later part are added by that part through `partial` declarations (in namespace `UasSort.Core`), never stubbed here: `ClockModel.ToUtc`/`OffsetAt` (Part 04, `Time/ClockModel.Conversion.cs`), `LibraryFolder.DaysIn` and all `LibraryIndex` members (Part 05), `CleanupPlan.Confirm` (Part 08, `Cleanup/CleanupPlan.Confirm.cs`). `IssueCode.NothingNew` is appended to this part's enum by Part 06 Task 06.1.
- Warnings are errors. If an analyzer rule this part did not anticipate fires, fix the code at the call site; never add a project-wide suppression.

### Task 02.1: Global usings, path rules, primitives and ledger paths

**Files:**
- Create: `src/UasSort.Core/Namespaces.cs`
- Create (identical content in all nine): `src/UasSort.Core/GlobalUsings.Core.cs`, `src/UasSort.Review/GlobalUsings.Core.cs`, `src/UasSort.Platform/GlobalUsings.Core.cs`, `src/UasSort.App/GlobalUsings.Core.cs`, `src/UasSort.Cli/GlobalUsings.Core.cs`, `tests/UasSort.Testing/GlobalUsings.Core.cs`, `tests/UasSort.Core.Tests/GlobalUsings.Core.cs`, `tests/UasSort.Review.Tests/GlobalUsings.Core.cs`, `tests/UasSort.Platform.Tests/GlobalUsings.Core.cs`
- Create: `tests/UasSort.Testing/GlobalUsings.cs`
- Create: `tests/UasSort.Core.Tests/GlobalUsings.cs`
- Create: `src/UasSort.Core/Guard/PathRules.cs`
- Create: `src/UasSort.Core/Model/Primitives.cs`
- Create: `src/UasSort.Core/Ledger/LedgerPaths.cs`
- Test: `tests/UasSort.Core.Tests/Model/PrimitivesTests.cs`

**Interfaces:**

```csharp
// Consumes (Part 01): the UasSort.Core project (net11.0, System.IO.Hashing referenced), UasSort.Core.Tests and UasSort.Testing
//   (both referencing Microsoft.Extensions.TimeProvider.Testing), and the Review, Platform, App, Cli, Review.Tests, Platform.Tests projects.
// Produces:
// src/UasSort.Core/Namespaces.cs: internal static class NamespaceMarker in each of UasSort.Core.{Card, Cleanup, Config, Editing, Geo,
//   Json, Ledger, Library, Media, Naming, Offload, Planning, Time} (anchors every namespace GlobalUsings.Core.cs imports).
// GlobalUsings.Core.cs (identical in the nine projects that reference Core): System.Collections.Immutable, UasSort.Core and the 13
//   Core namespaces above. tests/UasSort.Testing/GlobalUsings.cs: Microsoft.Extensions.Time.Testing.
//   tests/UasSort.Core.Tests/GlobalUsings.cs: Microsoft.Extensions.Time.Testing, UasSort.Testing.
public static class PathRules                                  // (defined here)
{ string Normalize(string path); bool Equal(string a, string b); bool IsSameOrUnder(string path, string root);
  bool IsStrictlyUnder(string path, string root); bool Overlaps(string a, string b); string? Parent(string path);
  string FileName(string path); string Join(string root, string relative); string RelativeCardPath(string path, string root);
  bool SetContains(IEnumerable<string> set, string path); }
public readonly record struct ItemId(string CardRelPath);
public readonly record struct GeoPoint(double Lat, double Lon);
public readonly record struct Distance(double Meters) { const double EarthRadiusMeters; double Miles; static Distance FromMiles(double mi); }
public readonly record struct ByteRange(long Offset, int Length);
public enum ItemKind { Video, Photo, Set }   public enum SetKind { Panorama, Hyperlapse }
public readonly record struct FileKey(string NormName, long Size)
{ static string NormalizeName(string fileName); static FileKey Of(string fileName, long size);      // helpers (defined here), rule of Ref §7.1
  static FileKey OfPath(string pathOrName, long size); }    // (defined here) text after the last '/' or '\', then Of
public static class LedgerPaths { const string FolderName; const string FilePattern; static string For(string videoRoot);
  static string OwnFile(string videoRoot, string machine); static string BackupDir(string appDataDir, string canonicalVideoRoot);
  static bool IsLedgerFileName(string fileName); }       // IsLedgerFileName (defined here): "ledger*.jsonl", case-insensitive
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Model/PrimitivesTests.cs
namespace UasSort.Core.Tests.Model;

public class PrimitivesTests
{
    [Theory]
    [InlineData(@"C:\V\", @"C:\V")]
    [InlineData("C:/V/2026", @"C:\V\2026")]
    [InlineData(@"\\?\C:\V", @"C:\V")]
    [InlineData("E:", @"E:\")]
    [InlineData(@"E:\", @"E:\")]
    public void Normalize_UsesBackslashesAndDropsTrailingSeparator(string input, string expected)
        => Assert.Equal(expected, PathRules.Normalize(input));

    [Theory]
    [InlineData(@"C:\V\.uas-sort\ledger-A.jsonl", @"C:\V\.uas-sort", true)]
    [InlineData(@"c:\v\.UAS-SORT", @"C:\V\.uas-sort", true)]
    [InlineData(@"C:\V\.uas-sort2\ledger-A.jsonl", @"C:\V\.uas-sort", false)]
    [InlineData(@"E:\DCIM", @"E:\", true)]
    [InlineData(@"D:\x", @"E:\", false)]
    public void IsSameOrUnder_RespectsSeparatorBoundaries(string path, string root, bool expected)
        => Assert.Equal(expected, PathRules.IsSameOrUnder(path, root));

    [Fact]
    public void Parent_WalksUpToTheDriveRoot()
    {
        Assert.Equal(@"E:\DCIM", PathRules.Parent(@"E:\DCIM\DJI_001"));
        Assert.Equal(@"E:\", PathRules.Parent(@"E:\DCIM"));
        Assert.Null(PathRules.Parent(@"E:\"));
    }

    [Fact]
    public void Join_And_RelativeCardPath_RoundTrip()
    {
        var full = PathRules.Join(@"E:\", "DCIM/DJI_001/DJI_20260927140627_0128_D.MP4");
        Assert.Equal(@"E:\DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", full);
        Assert.Equal("DCIM/DJI_001/DJI_20260927140627_0128_D.MP4", PathRules.RelativeCardPath(full, @"E:\"));
        Assert.Equal("DCIM/PANORAMA/001_0087", PathRules.RelativeCardPath(@"C:\Temp\card\DCIM\PANORAMA\001_0087", @"C:\Temp\card"));
    }

    [Fact]
    public void Distance_ConvertsMiles()
    {
        Assert.Equal(80_467.2, Distance.FromMiles(50).Meters, 6);
        Assert.Equal(50, Distance.FromMiles(50).Miles, 9);
    }

    [Theory]
    [InlineData("DJI_20260927140627_0128_D.MP4", "dji_20260927140627_0128_d.mp4")]
    [InlineData("X (2).MP4", "x.mp4")]
    [InlineData("best shot (12).DNG", "best shot.dng")]
    [InlineData("(2).MP4", "(2).mp4")]
    [InlineData("noext (3)", "noext")]
    public void FileKey_NormalizeName_LowercasesAndDropsCopySuffix(string name, string expected)
        => Assert.Equal(expected, FileKey.NormalizeName(name));

    [Theory]
    [InlineData("DCIM/DJI_001/X (2).MP4", "x.mp4")]
    [InlineData(@"C:\a\x.mp4", "x.mp4")]
    [InlineData("x.mp4", "x.mp4")]
    public void FileKey_OfPath_KeysTheLastSegment(string pathOrName, string expectedName)
        => Assert.Equal(new FileKey(expectedName, 42), FileKey.OfPath(pathOrName, 42));

    [Fact]
    public void LedgerPaths_AreDerivedFromTheVideoRoot()
    {
        const string root = @"C:\Users\u\OneDrive\Pictures\UAS Videos";
        Assert.Equal(root + @"\.uas-sort", LedgerPaths.For(root));
        Assert.Equal(root + @"\.uas-sort\ledger-DESKTOP-A.jsonl", LedgerPaths.OwnFile(root, "DESKTOP-A"));
        var a = LedgerPaths.BackupDir(@"C:\L\uas-sort", root);
        Assert.Equal(a, LedgerPaths.BackupDir(@"C:\L\uas-sort", root.ToUpperInvariant()));
        Assert.NotEqual(a, LedgerPaths.BackupDir(@"C:\L\uas-sort", @"D:\Other Videos"));
        Assert.Matches(@"^C:\\L\\uas-sort\\ledger-backup\\[0-9a-f]{16}$", a);
    }

    [Theory]
    [InlineData("ledger-DESKTOP-A.jsonl", true)]
    [InlineData("LEDGER-B.JSONL", true)]
    [InlineData("ledger-B-DESKTOP-A.jsonl", true)]
    [InlineData("notes.txt", false)]
    [InlineData("xledger.jsonl", false)]
    public void IsLedgerFileName_MatchesLedgerStarJsonl(string name, bool expected)
        => Assert.Equal(expected, LedgerPaths.IsLedgerFileName(name));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PrimitivesTests"`
Expected: build FAILS with CS0246/CS0103 (`PathRules`, `Distance`, `FileKey`, `LedgerPaths` not found).

- [ ] **Step 3: Implement**

First the namespace anchors and global usings (exact content from `00-interfaces.md`; write each file by hand, never generate them with a script).

```csharp
// src/UasSort.Core/Namespaces.cs
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

Write this file, byte for byte, at each of the nine paths `src/UasSort.Core/GlobalUsings.Core.cs`, `src/UasSort.Review/GlobalUsings.Core.cs`, `src/UasSort.Platform/GlobalUsings.Core.cs`, `src/UasSort.App/GlobalUsings.Core.cs`, `src/UasSort.Cli/GlobalUsings.Core.cs`, `tests/UasSort.Testing/GlobalUsings.Core.cs`, `tests/UasSort.Core.Tests/GlobalUsings.Core.cs`, `tests/UasSort.Review.Tests/GlobalUsings.Core.cs`, `tests/UasSort.Platform.Tests/GlobalUsings.Core.cs`:

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

```csharp
// tests/UasSort.Testing/GlobalUsings.cs
global using Microsoft.Extensions.Time.Testing;
```

```csharp
// tests/UasSort.Core.Tests/GlobalUsings.cs
global using Microsoft.Extensions.Time.Testing;
global using UasSort.Testing;
```

`src/UasSort.Core` gets no project-specific `GlobalUsings.cs`. The project-specific `GlobalUsings.cs` of Review, Platform, App, Cli, Review.Tests and Platform.Tests are created later (Parts 09–12, exact content in the registry). Part 01 imports none of these namespaces globally (its test csprojs import only `Xunit`), so no project imports a namespace twice.

```csharp
// src/UasSort.Core/Guard/PathRules.cs
namespace UasSort.Core;

/// <summary>Windows-style path comparison shared by the guard, the validator and the fakes.
/// Pure string logic: '\' or '/', case-insensitive, "\\?\" stripped, no trailing separator except on a drive root.</summary>
public static class PathRules
{
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + p[8..];
        else if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p[4..];
        while (p.Length > 1 && p.EndsWith('\\') && !IsDriveRoot(p)) p = p[..^1];
        if (p.Length == 2 && p[1] == ':') p += "\\";
        return p;
    }

    public static bool Equal(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static bool IsSameOrUnder(string path, string root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = r.EndsWith('\\') ? r : r + "\\";
        return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStrictlyUnder(string path, string root) => IsSameOrUnder(path, root) && !Equal(path, root);

    public static bool Overlaps(string a, string b) => IsSameOrUnder(a, b) || IsSameOrUnder(b, a);

    public static string? Parent(string path)
    {
        var p = Normalize(path);
        if (IsDriveRoot(p)) return null;
        var i = p.LastIndexOf('\\');
        if (i < 0) return null;
        if (i == 2 && p[1] == ':') return p[..3];
        return i <= 1 ? null : p[..i];
    }

    public static string FileName(string path)
    {
        var p = Normalize(path);
        var i = p.LastIndexOf('\\');
        return i < 0 ? p : p[(i + 1)..];
    }

    public static string Join(string root, string relative)
    {
        ArgumentNullException.ThrowIfNull(relative);
        var r = Normalize(root);
        var rel = relative.Replace('/', '\\').TrimStart('\\');
        if (rel.Length == 0) return r;
        return Normalize(r.EndsWith('\\') ? r + rel : r + "\\" + rel);
    }

    /// <summary>The '/'-separated path of <paramref name="path"/> relative to <paramref name="root"/>.</summary>
    public static string RelativeCardPath(string path, string root)
    {
        if (!IsSameOrUnder(path, root)) throw new ArgumentException($"{path} is not under {root}", nameof(path));
        var p = Normalize(path);
        var r = Normalize(root);
        if (p.Length == r.Length) return "";
        var start = r.EndsWith('\\') ? r.Length : r.Length + 1;
        return p[start..].Replace('\\', '/');
    }

    public static bool SetContains(IEnumerable<string> set, string path)
    {
        ArgumentNullException.ThrowIfNull(set);
        foreach (var s in set)
            if (Equal(s, path)) return true;
        return false;
    }

    private static bool IsDriveRoot(string p) => p.Length == 3 && p[1] == ':' && p[2] == '\\';
}
```

```csharp
// src/UasSort.Core/Model/Primitives.cs
namespace UasSort.Core;

public readonly record struct ItemId(string CardRelPath);      // "DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"; a set = "DCIM/PANORAMA/001_0087"

public readonly record struct GeoPoint(double Lat, double Lon); // WGS84 degrees; JSON as [lon,lat] (Task 02.5)

public readonly record struct Distance(double Meters)          // Earth radius 6,371,008.8 m, shared with map.js
{
    public const double EarthRadiusMeters = 6_371_008.8;
    public double Miles => Meters / 1609.344;
    public static Distance FromMiles(double mi) => new(mi * 1609.344);
}

public readonly record struct ByteRange(long Offset, int Length);

public enum ItemKind { Video, Photo, Set }

public enum SetKind { Panorama, Hyperlapse }

public readonly record struct FileKey(string NormName, long Size)
{
    /// <summary>Ref §7.1: the lowercase name with any trailing " (n)" before the extension removed.</summary>
    public static string NormalizeName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var dot = fileName.LastIndexOf('.');
        var stem = dot < 0 ? fileName : fileName[..dot];
        var ext = dot < 0 ? "" : fileName[dot..];
        if (stem.EndsWith(')'))
        {
            var open = stem.LastIndexOf(" (", StringComparison.Ordinal);
            if (open > 0)
            {
                var digits = stem[(open + 2)..^1];
                if (digits.Length > 0 && digits.All(char.IsAsciiDigit)) stem = stem[..open];
            }
        }
#pragma warning disable CA1308 // Ref §7.1: keys are lowercase by definition
        return (stem + ext).ToLowerInvariant();
#pragma warning restore CA1308
    }

    public static FileKey Of(string fileName, long size) => new(NormalizeName(fileName), size);

    /// <summary>The key of the last segment of a card-relative ('/') or Windows ('\') path, or of a bare file name.</summary>
    public static FileKey OfPath(string pathOrName, long size)
    {
        ArgumentNullException.ThrowIfNull(pathOrName);
        var cut = Math.Max(pathOrName.LastIndexOf('/'), pathOrName.LastIndexOf('\\'));
        return Of(cut < 0 ? pathOrName : pathOrName[(cut + 1)..], size);
    }
}
```

```csharp
// src/UasSort.Core/Ledger/LedgerPaths.cs
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace UasSort.Core;

/// <summary>The ledger folder is DERIVED, never stored or configurable (Ref §11).</summary>
public static class LedgerPaths
{
    public const string FolderName = ".uas-sort";          // excluded from every library listing (Ref §7.1)
    public const string FilePattern = "ledger*.jsonl";     // read-only union, top level only

    public static string For(string videoRoot) => PathRules.Join(videoRoot, FolderName);

    public static string OwnFile(string videoRoot, string machine) => PathRules.Join(For(videoRoot), $"ledger-{machine}.jsonl");

    public static string BackupDir(string appDataDir, string canonicalVideoRoot)   // local only; one subfolder per video root
    {
        ArgumentNullException.ThrowIfNull(canonicalVideoRoot);
#pragma warning disable CA1308 // Ref §11: the key hashes the lowercase canonical root
        var key = XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(PathRules.Normalize(canonicalVideoRoot).ToLowerInvariant()));
#pragma warning restore CA1308
        return PathRules.Join(PathRules.Join(appDataDir, "ledger-backup"), key.ToString("x16", CultureInfo.InvariantCulture));
    }

    public static bool IsLedgerFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return fileName.StartsWith("ledger", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PrimitivesTests"`
Expected: PASS (all `PrimitivesTests` cases green, including the three `FileKey_OfPath_KeysTheLastSegment` rows).
Then run `dotnet test --solution uas-sort.slnx` — expected: every project (all nine with `GlobalUsings.Core.cs`) builds with no warnings and every test passes.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Namespaces.cs src/UasSort.Core/GlobalUsings.Core.cs src/UasSort.Review/GlobalUsings.Core.cs src/UasSort.Platform/GlobalUsings.Core.cs src/UasSort.App/GlobalUsings.Core.cs src/UasSort.Cli/GlobalUsings.Core.cs tests/UasSort.Testing/GlobalUsings.Core.cs tests/UasSort.Core.Tests/GlobalUsings.Core.cs tests/UasSort.Review.Tests/GlobalUsings.Core.cs tests/UasSort.Platform.Tests/GlobalUsings.Core.cs tests/UasSort.Testing/GlobalUsings.cs tests/UasSort.Core.Tests/GlobalUsings.cs src/UasSort.Core/Guard/PathRules.cs src/UasSort.Core/Model/Primitives.cs src/UasSort.Core/Ledger/LedgerPaths.cs tests/UasSort.Core.Tests/Model/PrimitivesTests.cs
git commit -m "feat: add global usings, path rules, primitives and derived ledger paths

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 02.2: Card, listing, probe, clock and item model

**Files:**
- Create: `src/UasSort.Core/Ports/FileSystemRecords.cs`
- Create: `src/UasSort.Core/Model/Card.cs`
- Create: `src/UasSort.Core/Model/Probes.cs`
- Create: `src/UasSort.Core/Model/Clock.cs`
- Create: `src/UasSort.Core/Model/Items.cs`
- Test: `tests/UasSort.Core.Tests/Model/CardAndItemModelTests.cs`

**Interfaces:**

```csharp
// Consumes: Task 02.1 (ItemId, GeoPoint, ByteRange, SetKind, ItemKind, PathRules).
// Produces (Ref §3 and §4.1, verbatim names and positional order):
public sealed record VolumeInfo(string Root, CardIdentity Identity, string DriveType, bool IsReady, bool IsReadOnlyVolume,
    bool IsNtfs, bool IsRemovableBus, long FreeBytes, string BusType, bool RemovableMedia, bool IsSystemBootOrPaging);
public sealed record FsEntry(string FullPath, string RelPath /* native '\' */, bool IsDirectory, long Size, DateTime MtimeUtc,
    DateTime CreationUtc, DateTime LastAccessUtc, uint RawAttributes);
public sealed record ListingResult(ImmutableArray<FsEntry> Entries, ImmutableArray<(string Path, int Win32Error)> Errors);
public enum EntryClass { Video, Photo, PhotoTwin, SetMember, Skip, Unknown }
public sealed record CardEntry(string RelPath /* '/' */, long Size, DateTime MtimeUtc, DateTime CreationUtc, DateTime LastAccessUtc,
    uint RawAttributes, EntryClass Class, string? Rule);
public sealed record CardIdentity(uint VolumeSerial, string? Label, string FileSystem, long TotalBytes);
public closed record class MediaUnit(ItemId Id);  // VideoUnit(Id, Mp4, HasTrinf) | PhotoUnit(Id, Primary, JpgTwin) | SetUnit(Id, Kind, SetName, Members)
public sealed record CardSource(string Root, CardIdentity? Identity, bool IsBrowsedFolder, bool IsWriteProtected) { string DraftKey; }
public sealed record CardInventory(CardSource Source, DateTime ListedUtc, string InventoryHash, ImmutableArray<CardEntry> Entries,
    ImmutableArray<MediaUnit> Units, string? CameraModel, ImmutableArray<ScanWarning> Warnings);
public sealed record ScanWarning(string Code, string Message, string? RelPath, bool ForcesNotSafe);
public sealed record CardCandidate(VolumeInfo Volume, bool IsDjiCard, string? NotCardReason, int MediaCount);
public sealed record SourceOk(CardSource Source);   public sealed record SourceRefused(string Reason);
public union CardSourceCheck(SourceOk, SourceRefused);
// probes, clock, items: the Ref §3 declarations verbatim (every positional parameter is spelled out in Step 3):
//   GpsSource, GpsFix(Point, AltM, Sample, Source, FieldPath), NoFixReason, NoFix(Reason), union GpsProbe(GpsFix, NoFix),
//   Mp4Info(MvhdUtc, HasMoov, First, LastSameField, Protocol, SessionUtc, DroneSerial, Thumb, Duration),
//   SessionKey(DroneSerial, SessionUtc) { bool SameSession(SessionKey o) }, StillInfo(DtoNaive, OffsetTime, Gps, Model, Thumb),
//   RawItem(Unit, Kind, Name, Bytes, CardMtimeUtc, DroneStamp, Mp4, Still, ProbeError),
//   ClockMode, StoredClockMode, ClockSample(DroneStamp, MvhdUtc, Offset, SiteZoneId)
public sealed partial record ClockModel(ClockMode Mode, string? ZoneId, ImmutableArray<ClockSample> Samples, TimeSpan? Modal,
    StoredClockMode SettingMode, string SettingZoneId);            // ToUtc / OffsetAt are added by Part 04 (partial)
//   ClockSummary(Mode, ZoneId, SampleCount, Headline, MismatchItems, MismatchSiteZones, Changes), ClockChange(AtUtc, From, To),
//   TimeSource, TzSource, [Flags] ItemFlags, ItemTime(CaptureUtc, Source, TzId, TzSource, LocalDate, LocalTime)
public enum NewReason; public enum Evidence; public enum DecisionKind;
public closed record class Newness;  // IsNew | Imported | Decided | ProbablyImported | Conflict
public sealed record Item(RawItem Raw, ItemTime Time, GpsFix? Gps, SessionKey? Session, ItemFlags Flags, Newness Newness);
```

`Imported` references `LibraryFolderRef`, which this task declares in `Model/Items.cs` next to it (Task 02.3 declares the rest of the library model).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Model/CardAndItemModelTests.cs
namespace UasSort.Core.Tests.Model;

public class CardAndItemModelTests
{
    private static readonly CardIdentity Card = new(0x1A2B3C4D, null, "exFAT", 256_060_514_304);

    [Fact]
    public void DraftKey_UsesTheVolumeSerialForADetectedCard()
        => Assert.Equal("vol-1A2B3C4D", new CardSource(@"E:\", Card, false, false).DraftKey);

    [Fact]
    public void DraftKey_HashesTheLowercaseRootForABrowsedFolder()
    {
        var a = new CardSource(@"C:\Temp\Card", null, true, false).DraftKey;
        var b = new CardSource(@"c:\temp\card", null, true, false).DraftKey;
        Assert.Equal(a, b);
        Assert.Matches("^dir-[0-9a-f]{16}$", a);
    }

    [Fact]
    public void SessionKey_ToleratesTwoSecondsForTheSameSerial()
    {
        var t = new DateTime(2026, 9, 27, 17, 59, 28, DateTimeKind.Utc);
        var s = new SessionKey("SER1", t);
        Assert.True(s.SameSession(new SessionKey("SER1", t.AddSeconds(0.8))));
        Assert.False(s.SameSession(new SessionKey("SER1", t.AddSeconds(3))));
        Assert.False(s.SameSession(new SessionKey("SER2", t)));
    }

    [Fact]
    public void GpsProbe_IsAnExhaustiveUnion()
    {
        GpsProbe fix = new GpsFix(new GeoPoint(57.5504421, -153.738973), 12.5, 1, GpsSource.DjmdModelTable, "gps.lat");
        GpsProbe none = new NoFix(NoFixReason.AllProbedSamplesZero);
        Assert.Equal("fix", Describe(fix));
        Assert.Equal("none:AllProbedSamplesZero", Describe(none));

        static string Describe(GpsProbe p) => p switch
        {
            GpsFix => "fix",
            NoFix n => $"none:{n.Reason}",
        };
    }

    [Fact]
    public void MediaUnit_And_Newness_AreClosedHierarchies()
    {
        var mp4 = new CardEntry("DCIM/DJI_001/DJI_20260927140627_0128_D.MP4", 89_612_345,
            new DateTime(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc), new DateTime(2026, 9, 27, 18, 6, 27, DateTimeKind.Utc),
            new DateTime(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc), 0x20, EntryClass.Video, null);
        MediaUnit unit = new VideoUnit(new ItemId(mp4.RelPath), mp4, false);
        Assert.Equal(ItemKind.Video, KindOf(unit));

        Newness n = new Imported(Evidence.LibraryNameSize, null, "same name and size in the library");
        Assert.Equal("Imported", NameOf(n));

        static ItemKind KindOf(MediaUnit u) => u switch
        {
            VideoUnit => ItemKind.Video,
            PhotoUnit => ItemKind.Photo,
            SetUnit => ItemKind.Set,
        };
        static string NameOf(Newness x) => x switch
        {
            IsNew => "IsNew",
            Imported => "Imported",
            Decided => "Decided",
            ProbablyImported => "ProbablyImported",
            Conflict => "Conflict",
        };
    }

    [Fact]
    public void CardSourceCheck_IsAnExhaustiveUnion()
    {
        CardSourceCheck ok = new SourceOk(new CardSource(@"E:\", Card, false, true));
        CardSourceCheck refused = new SourceRefused("No DCIM folder here");
        Assert.True(IsOk(ok));
        Assert.False(IsOk(refused));

        static bool IsOk(CardSourceCheck c) => c switch { SourceOk => true, SourceRefused => false };
    }

    [Fact]
    public void ItemFlags_HaveTheSpecifiedBitValues()
    {
        Assert.Equal(256, (int)ItemFlags.ClockMismatch);
        Assert.Equal(1 | 2, (int)(ItemFlags.NoGps | ItemFlags.Truncated));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CardAndItemModelTests"`
Expected: build FAILS (CS0246: `CardIdentity`, `CardSource`, `GpsProbe`, … not found).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Ports/FileSystemRecords.cs
namespace UasSort.Core;

public sealed record VolumeInfo(string Root, CardIdentity Identity, string DriveType, bool IsReady,
                                bool IsReadOnlyVolume /* FILE_READ_ONLY_VOLUME */,
                                bool IsNtfs, bool IsRemovableBus, long FreeBytes,
                                string BusType /* IOCTL_STORAGE_QUERY_PROPERTY: "Sd", "Mmc", "Usb", "Nvme", … */,
                                bool RemovableMedia /* STORAGE_DEVICE_DESCRIPTOR.RemovableMedia */,
                                bool IsSystemBootOrPaging);   // the last three feed the cleanup volume check (Ref §10.6)

/// <summary>One listed entry. RelPath is relative to the listed root with the native '\' separator.</summary>
public sealed record FsEntry(string FullPath, string RelPath, bool IsDirectory, long Size, DateTime MtimeUtc, DateTime CreationUtc,
                             DateTime LastAccessUtc, uint RawAttributes);

public sealed record ListingResult(ImmutableArray<FsEntry> Entries, ImmutableArray<(string Path, int Win32Error)> Errors);
```

```csharp
// src/UasSort.Core/Model/Card.cs
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace UasSort.Core;

public enum EntryClass { Video, Photo, PhotoTwin, SetMember, Skip, Unknown }

/// <summary>A classified card file. RelPath is card-relative with '/' separators.</summary>
public sealed record CardEntry(string RelPath, long Size, DateTime MtimeUtc, DateTime CreationUtc, DateTime LastAccessUtc,
                               uint RawAttributes, EntryClass Class, string? Rule);

public sealed record CardIdentity(uint VolumeSerial, string? Label, string FileSystem, long TotalBytes);

public closed record class MediaUnit(ItemId Id);
public sealed record class VideoUnit(ItemId Id, CardEntry Mp4, bool HasTrinf) : MediaUnit(Id);
public sealed record class PhotoUnit(ItemId Id, CardEntry Primary, CardEntry? JpgTwin) : MediaUnit(Id);
public sealed record class SetUnit(ItemId Id, SetKind Kind, string SetName, ImmutableArray<CardEntry> Members) : MediaUnit(Id);

public sealed record CardSource(string Root /* canonical, anchored at the folder holding DCIM */, CardIdentity? Identity,
                                bool IsBrowsedFolder /* set only by CardSourceValidator: detected == null (Ref §4.1) */,
                                bool IsWriteProtected /* detected?.IsReadOnlyVolume */)   // UI hints only: the eraser factory
                                                                                          // re-derives every fact from Win32
{
    public string DraftKey => Identity is { } i
        ? string.Create(CultureInfo.InvariantCulture, $"vol-{i.VolumeSerial:X8}")
        : "dir-" + HashRoot(Root);

    private static string HashRoot(string root)
    {
#pragma warning disable CA1308 // Ref §3: the draft key hashes the lowercase root
        var bytes = Encoding.UTF8.GetBytes(root.ToLowerInvariant());
#pragma warning restore CA1308
        return XxHash64.HashToUInt64(bytes).ToString("x16", CultureInfo.InvariantCulture);
    }
}

public sealed record CardInventory(CardSource Source, DateTime ListedUtc, string InventoryHash, ImmutableArray<CardEntry> Entries,
                                   ImmutableArray<MediaUnit> Units, string? CameraModel, ImmutableArray<ScanWarning> Warnings);

public sealed record ScanWarning(string Code, string Message, string? RelPath, bool ForcesNotSafe);

// ── card detection and source validation (Ref §3 Supporting types (2))
public sealed record CardCandidate(VolumeInfo Volume, bool IsDjiCard, string? NotCardReason /* "not a DJI card" */, int MediaCount);
public sealed record SourceOk(CardSource Source);
public sealed record SourceRefused(string Reason);
public union CardSourceCheck(SourceOk, SourceRefused);
```

```csharp
// src/UasSort.Core/Model/Probes.cs
namespace UasSort.Core;

public enum GpsSource { DjmdModelTable, DjmdGenericSearch, MdatHeadFallback, Exif }
public sealed record GpsFix(GeoPoint Point, double? AltM, int Sample, GpsSource Source, string? FieldPath);
public enum NoFixReason { NotDji, NoDjmdTrack, AllProbedSamplesZero, Unparseable, NoGpsTag, GenericHitImplausible }
public sealed record NoFix(NoFixReason Reason);
public union GpsProbe(GpsFix, NoFix);

public sealed record Mp4Info(DateTime? MvhdUtc, bool HasMoov, GpsProbe First, GpsFix? LastSameField /* generic hits only */,
                             string? Protocol, DateTime? SessionUtc, string? DroneSerial, ByteRange? Thumb,
                             TimeSpan? Duration /* mvhd duration / timescale; null when there is no moov (Ref §6.3) */);

public readonly record struct SessionKey(string? DroneSerial, DateTime SessionUtc)   // SessionUtc = mvhd − uptime µs (Ref §6.3 step 6)
{
    public bool SameSession(SessionKey o) => DroneSerial == o.DroneSerial
                                             && Math.Abs((SessionUtc - o.SessionUtc).TotalSeconds) <= 2;   // never compare with ==
}

public sealed record StillInfo(DateTime? DtoNaive, TimeSpan? OffsetTime, GpsProbe Gps, string? Model, ByteRange? Thumb);

public sealed record RawItem(MediaUnit Unit, ItemKind Kind, string Name, long Bytes, DateTime CardMtimeUtc,
                             DateTime? DroneStamp /* filename or EXIF DTO, naive */, Mp4Info? Mp4, StillInfo? Still, string? ProbeError);
```

```csharp
// src/UasSort.Core/Model/Clock.cs
namespace UasSort.Core;

public enum ClockMode { SiteLocal, Zone, NearestSample, Setting }    // Ref §6.1
public enum StoredClockMode { SiteLocal, Zone }                       // what a successful run saves (Settings.DroneClockMode)

public sealed record ClockSample(DateTime DroneStamp, DateTime MvhdUtc, TimeSpan Offset,
                                 string? SiteZoneId /* sample's own GPS; null = no GPS or Etc/* */);

/// <summary>The learned drone clock. ToUtc and OffsetAt are added by Part 04 in Time/ClockModel.Conversion.cs.</summary>
public sealed partial record ClockModel(ClockMode Mode, string? ZoneId, ImmutableArray<ClockSample> Samples, TimeSpan? Modal,
                                        StoredClockMode SettingMode, string SettingZoneId);

public sealed record ClockSummary(ClockMode Mode, string? ZoneId, int SampleCount, string Headline,
                                  int MismatchItems, ImmutableArray<string> MismatchSiteZones /* IANA IDs, for the InfoBar */,
                                  ImmutableArray<ClockChange> Changes /* NearestSample only */);

public sealed record ClockChange(DateTime AtUtc, TimeSpan From, TimeSpan To);
```

```csharp
// src/UasSort.Core/Model/Items.cs
namespace UasSort.Core;

public enum TimeSource { Mvhd, ExifWithOffset, DroneClockSiteLocal, DroneClockZone, DroneClockSample, DroneClockSetting, Mtime }
public enum TzSource { Gps, SameSession, NearestGpsWithin12h, NearestLandGpsOnCard, GeoNamesTz, PcZone }

[Flags]
public enum ItemFlags
{
    None = 0, NoGps = 1, Truncated = 2, ClockNotSet = 4, CheckDate = 8, TzFallback = 16,
    ProbeFailed = 32, GpsGuessed = 64, ClockFromSetting = 128, ClockMismatch = 256,
}

public sealed record ItemTime(DateTime CaptureUtc, TimeSource Source, string TzId, TzSource TzSource, DateOnly LocalDate, DateTime LocalTime);

public enum NewReason { NoMatch, SeenNotCopied, AfterWatermark, NearWatermark, DayHasNewVideos }
public enum Evidence { LibraryNameSize, LedgerVerified, LedgerNameSize }
public enum DecisionKind { AssumedImported, Dismissed }

public sealed record LibraryFolderRef(string FullPath, DateOnly NameDate, string Description);

public closed record class Newness;
public sealed record class IsNew(NewReason Why, DateTime? SeenUtc) : Newness;
public sealed record class Imported(Evidence By, LibraryFolderRef? Folder, string Why) : Newness;
public sealed record class Decided(DecisionKind Kind, DateTime AtUtc, string Machine) : Newness;      // ConfirmedByYou; undoable
public sealed record class ProbablyImported(string Why) : Newness;
public sealed record class Conflict(string ExistingPath, long ExistingSize) : Newness;

public sealed record Item(RawItem Raw, ItemTime Time, GpsFix? Gps, SessionKey? Session, ItemFlags Flags, Newness Newness);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CardAndItemModelTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Ports/FileSystemRecords.cs src/UasSort.Core/Model/Card.cs src/UasSort.Core/Model/Probes.cs src/UasSort.Core/Model/Clock.cs src/UasSort.Core/Model/Items.cs tests/UasSort.Core.Tests/Model/CardAndItemModelTests.cs
git commit -m "feat: add card, listing, probe, clock and item model

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 02.3: Library, ledger, settings, plan and edit model

**Files:**
- Create: `src/UasSort.Core/Model/Library.cs`
- Create: `src/UasSort.Core/Model/Ledger.cs`
- Create: `src/UasSort.Core/Model/LedgerRecords.cs`
- Create: `src/UasSort.Core/Model/Settings.cs`
- Create: `src/UasSort.Core/Model/Geo.cs`
- Create: `src/UasSort.Core/Model/Plan.cs`
- Create: `src/UasSort.Core/Model/Edits.cs`
- Create: `src/UasSort.Core/Ports/IPlanDeriver.cs`
- Test: `tests/UasSort.Core.Tests/Model/PlanModelTests.cs`

**Interfaces:**

```csharp
// Consumes: Tasks 02.1–02.2.
// Produces (Ref §3 verbatim; full declarations in Step 3):
// library:  LibraryFolderRef (02.2), LocationSource, sealed partial record LibraryFolder (DaysIn added by Part 05),
//           RootListing, LibraryListings, LibraryFile, SetFolderListing, sealed partial class LibraryIndex (all members added by Part 05)
// ledger:   VerifyKind, FolderSource, LedgerFile, LedgerSet, LedgerDecision, LedgerFolder, LedgerRun, LedgerCardDelete,
//           LedgerParseIssue, LedgerFolderState, LedgerFolderStatus, LedgerSnapshot
// records:  closed LedgerRecord(int V, string Id, string Machine) with [JsonPolymorphic("t")] and discriminators
//           "file" FileRecord, "folder" FolderRecord, "seen" SeenRecord, "decision" DecisionRecord, "revoke" RevokeRecord,
//           "run" RunRecord, "torn" TornRecord, "cardDelete" CardDeleteRecord; RunCard, RunRoots
// settings: Settings, MapSettings, LayoutSettings, SettingsLoad, Draft
// geo:      TzLookup, PlaceClass, PlaceHit, ResolvedItem
// plan:     Tuning, GroupId, BoundaryCause, Boundary, DaySplit, Confidence, closed GroupTarget (AlreadyImported | NothingToCopy |
//           NewFolder | Append | SkipGroup), CrossDayHint, DescSource, Suggestion, PinState, VideoGroup, SetResolution, SetPlacement,
//           PhotoDay, IssueSeverity, IssueCode, Issue, QuickFix, SessionFlags, PlanBase, Plan, ScanResult, GroupDraft, ClusterResult
// edits:    closed PlanEdit [JsonPolymorphic("t")]: "merge" Merge, "splitBefore" SplitBefore, "moveToNewGroup" MoveToNewGroup,
//           "moveToGroup" MoveToGroup, "rename" Rename, "retarget" Retarget, "setIncluded" SetIncluded, "setDayIncluded" SetDayIncluded;
//           closed TargetChoice [JsonPolymorphic("t")]: "auto" AutoTarget, "newFolder" NewFolderTarget, "appendTo" AppendTo, "skip" SkipTarget;
//           RejectReason, Applied, Rejected, union EditResult(Applied, Rejected)
public interface IPlanDeriver
{ Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct); }
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Model/PlanModelTests.cs
using System.Reflection;

namespace UasSort.Core.Tests.Model;

public class PlanModelTests
{
    [Fact]
    public void Tuning_DefaultsToFiftyMilesAndOneDay()
    {
        var t = new Tuning();
        Assert.Equal(50, t.RadiusMiles);
        Assert.Equal(1, t.GapDays);
    }

    [Fact]
    public void Settings_HasNoLedgerFolderProperty()
    {
        var names = typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
        Assert.DoesNotContain(names, n => n.Contains("Ledger", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("VideoRoot", names);
    }

    [Fact]
    public void GroupTarget_PlanEdit_TargetChoice_And_EditResult_AreExhaustive()
    {
        var a = new ItemId("DCIM/DJI_001/DJI_20260725232655_0117_D.MP4");
        var folder = new LibraryFolderRef(@"C:\V\2026\2026-07\2026-07-25 Council Road", new DateOnly(2026, 7, 25), "Council Road");
        GroupTarget[] targets =
        [
            new AlreadyImported(folder), new NothingToCopy("nothing to copy: 2 conflicts, 1 dismissed"),
            new NewFolder(@"2026\2026-07\2026-07-26"), new Append(folder, Confidence.Medium, "different day, 34 mi from Council Road", null),
            new SkipGroup(),
        ];
        Assert.Equal("AINAS", string.Concat(targets.Select(Letter)));

        PlanEdit[] edits =
        [
            new Merge(a, a), new SplitBefore(a), new MoveToNewGroup([a]), new MoveToGroup([a], a),
            new Rename(a, "Anvil", []), new Retarget(a, new AutoTarget(), false, []), new SetIncluded([a], true),
            new SetDayIncluded(new DateOnly(2026, 7, 26), true),
        ];
        Assert.Equal(8, edits.Select(EditName).Distinct().Count());

        TargetChoice[] choices = [new AutoTarget(), new NewFolderTarget(), new AppendTo(folder.FullPath), new SkipTarget()];
        Assert.Equal(4, choices.Select(ChoiceName).Distinct().Count());

        EditResult rejected = new Rejected(RejectReason.MergeAcrossLibraryFolders, "Can't merge across library folders");
        Assert.Equal("rejected", rejected switch { Applied => "applied", Rejected => "rejected" });

        static char Letter(GroupTarget t) => t switch
        {
            AlreadyImported => 'A', NothingToCopy => 'I', NewFolder => 'N', Append => 'A', SkipGroup => 'S',
        };
        static string EditName(PlanEdit e) => e switch
        {
            Merge => "m", SplitBefore => "s", MoveToNewGroup => "mn", MoveToGroup => "mg", Rename => "r",
            Retarget => "rt", SetIncluded => "si", SetDayIncluded => "sd",
        };
        static string ChoiceName(TargetChoice c) => c switch
        {
            AutoTarget => "auto", NewFolderTarget => "new", AppendTo => "append", SkipTarget => "skip",
        };
    }

    [Fact]
    public void LedgerRecord_IsAClosedHierarchyOfEightKinds()
    {
        var card = new RunCard("1A2B3C4D", null, "exFAT", "FC9113", "9f3c0a6d12e4b7a1");
        var at = new DateTime(2026, 10, 6, 18, 2, 11, DateTimeKind.Utc);
        LedgerRecord[] records =
        [
            new FileRecord(1, "f", "DESKTOP-A", "r", at, "video", "a.MP4", 1, "DCIM/DJI_001/a.MP4", "video", @"C:\V\a.MP4", null, "nameSize", at, null, null, null, null, null, null, null, null, null),
            new FolderRecord(1, "d", "DESKTOP-A", "r", @"C:\V\x", "x", "created", null, null, new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), "America/Anchorage"),
            new SeenRecord(1, "s", "DESKTOP-A", "r", at, "b.DNG", 2, "DCIM/DJI_001/b.DNG", null, "New", "unticked", null),
            new DecisionRecord(1, "c", "DESKTOP-A", "r", at, "dismissed", "b.DNG", 2, "DCIM/DJI_001/b.DNG", null, "not needed", null),
            new RevokeRecord(1, "v", "LAPTOP-B", at, "c"),
            new RunRecord(1, "u", "DESKTOP-A", "r", at, at, "0.1.0", card, new RunRoots(@"C:\V", @"C:\V\P"), "Safe", ImmutableDictionary<string, int>.Empty),
            new TornRecord(1, "t", "DESKTOP-A", at, 412),
            new CardDeleteRecord(1, "x", "DESKTOP-A", "r", at, "a.MP4", 1, "DCIM/DJI_001/a.MP4", "DCIM/DJI_001/a.MP4", null, "InLedger", "in the history, verified", "beforeDate", card, null),
        ];
        Assert.Equal(8, records.Select(Kind).Distinct().Count());

        static string Kind(LedgerRecord r) => r switch
        {
            FileRecord => "file", FolderRecord => "folder", SeenRecord => "seen", DecisionRecord => "decision",
            RevokeRecord => "revoke", RunRecord => "run", TornRecord => "torn", CardDeleteRecord => "cardDelete",
        };
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PlanModelTests"`
Expected: build FAILS (CS0246: `Tuning`, `Settings`, `GroupTarget`, `LedgerRecord`, … not found).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Model/Library.cs
namespace UasSort.Core;

public enum LocationSource { Ledger, CardLeftovers, Unknown }

/// <summary>An event folder of the library. DaysIn(tzId) is added by Part 05 (partial).</summary>
public sealed partial record LibraryFolder(LibraryFolderRef Ref, ImmutableArray<DateTime> MemberStartsUtc, string? TzId,
                                           GeoPoint? Centroid, LocationSource Loc);

public sealed record RootListing(string Root, DestRoot Kind, bool IsPrevious, bool Available, ListingResult Listing);
public sealed record LibraryListings(RootListing Video, RootListing Photo, ImmutableArray<RootListing> PreviousPhoto);
public sealed record LibraryFile(string FullPath, FileKey Key, DateTime MtimeUtc, uint RawAttributes, LibraryFolderRef? EventFolder);
public sealed record SetFolderListing(string FullPath, string Name, ImmutableArray<(string Member, long Size, DateTime MtimeUtc)> Members);

/// <summary>Built from listings + ledger only (Ref §7.1). Build, Match, SameNameOtherSize, Folders, SetFolder,
/// WatermarkUtc and UnavailableRoots are added by Part 05 in Library/LibraryIndex.cs (partial).</summary>
public sealed partial class LibraryIndex
{
}
```

```csharp
// src/UasSort.Core/Model/Ledger.cs
namespace UasSort.Core;

public enum VerifyKind { Unbuffered, Cached, NameSize }
public enum FolderSource { Created, Appended, CardLeftovers }

public sealed record LedgerFile(FileKey Key, string Src, DestRoot Root, string Dest, UInt128? Xxh128, VerifyKind Verify, DateTime AtUtc,
                                DateTime? CaptureUtc, GeoPoint? Point, string? TzId, DateOnly? LocalDate, SessionKey? Session,
                                string? Set, string Machine, string Run);
public sealed record LedgerSet(string SetName, DateTime FirstFrameCaptureUtc, ImmutableArray<(string Member, long Size)> Members);
public sealed record LedgerDecision(string Id, FileKey Key, DecisionKind Kind, DateTime AtUtc, string Machine, string? Set, string Why);
public sealed record LedgerFolder(string Path, string Description, FolderSource Source, GeoPoint? Centroid,
                                  DateOnly Start, DateOnly End, string TzId);
public sealed record LedgerRun(string Run, string Machine, DateTime StartUtc, DateTime EndUtc, string App, CardIdentity Card,
                               string? Model, string InventoryHash, string VideoRoot, string PhotoRoot, VerdictLevel Verdict,
                               ImmutableDictionary<AuditCategory, int> Counts);
public sealed record LedgerCardDelete(string Run, DateTime AtUtc, FileKey Key, string Src, string Evidence, string Machine);
public sealed record LedgerParseIssue(string File, int Line, string Reason);

public enum LedgerFolderState { Ok, Empty, Missing, NotPinned, Unwritable, CloudOnly, VideoRootMissing }

public sealed record LedgerFolderStatus(string Folder, LedgerFolderState State /* the most severe that applies: VideoRootMissing >
    CloudOnly > Unwritable > NotPinned > Missing > Empty > Ok */, bool Exists, bool InSyncRoot, bool Pinned, bool Writable,
    ImmutableArray<string> LedgerFiles, ImmutableArray<string> CloudOnlyFiles, ImmutableArray<string> OtherMachineFiles);

public sealed record LedgerSnapshot(
    ImmutableDictionary<FileKey, LedgerFile> Files,           // strongest verify per key (Unbuffered > Cached > NameSize), latest among equals
    ImmutableDictionary<string, ImmutableArray<LedgerSet>> SetsByName,
    ImmutableDictionary<FileKey, LedgerDecision> Decisions,   // after applying revoke records; a set has one per member (Ref §7.3)
    ImmutableDictionary<FileKey, DateTime> Seen,              // a set has one per member (Ref §7.3)
    ImmutableDictionary<string, LedgerFolder> Folders,        // by full path (case-insensitive)
    ImmutableArray<LedgerRun> Runs,
    ImmutableArray<LedgerCardDelete> CardDeletes,             // Card cleanup's audit trail; informational, read by no rule
    ImmutableArray<LedgerParseIssue> ParseIssues,
    ImmutableArray<string> SourceFiles, LedgerFolderStatus Status);

public interface ILedgerWriter : IDisposable       // from ILedgerStore.OpenOwn(); one instance per Commit or user action
{
    void Append(LedgerRecord r);                   // one line + "\n", FlushFileBuffers, then the same line to the local mirror;
}                                                  // throws on any failure (Commit stops, Ref §10.3)
```

```csharp
// src/UasSort.Core/Model/LedgerRecords.cs
using System.Text.Json.Serialization;

namespace UasSort.Core;

// One JSON line each; fields mirror Ref §11 one for one, camelCase (LedgerJsonContext, Task 02.5).
[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(FileRecord), "file")]
[JsonDerivedType(typeof(FolderRecord), "folder")]
[JsonDerivedType(typeof(SeenRecord), "seen")]
[JsonDerivedType(typeof(DecisionRecord), "decision")]
[JsonDerivedType(typeof(RevokeRecord), "revoke")]
[JsonDerivedType(typeof(RunRecord), "run")]
[JsonDerivedType(typeof(TornRecord), "torn")]
[JsonDerivedType(typeof(CardDeleteRecord), "cardDelete")]
public closed record class LedgerRecord(int V, string Id, string Machine);

public sealed record class FileRecord(int V, string Id, string Machine, string Run, DateTime At, string Kind /* video|photo|twin|setMember */,
    string Name, long Size, string Src, string Root /* video|photo */, string Dest, string? Xxh128, string Verify /* unbuffered|cached|nameSize */,
    DateTime Mtime, DateTime? CaptureUtc, string? TimeSource, double? Lat, double? Lon, string? Tz, DateOnly? LocalDate,
    DateTime? SessionUtc, string? Serial, string? Set) : LedgerRecord(V, Id, Machine);

public sealed record class FolderRecord(int V, string Id, string Machine, string Run, string Path, string Desc,
    string Source /* created|appended|cardLeftovers */, double? Lat, double? Lon, DateOnly Start, DateOnly End, string Tz) : LedgerRecord(V, Id, Machine);

public sealed record class SeenRecord(int V, string Id, string Machine, string Run, DateTime At, string Name, long Size, string Src,
    DateTime? CaptureUtc, string Status /* New|Conflict */, string Why, string? Set) : LedgerRecord(V, Id, Machine);

public sealed record class DecisionRecord(int V, string Id, string Machine, string? Run, DateTime At, string Kind /* assumedImported|dismissed */,
    string Name, long Size, string Src, DateTime? CaptureUtc, string Why, string? Set) : LedgerRecord(V, Id, Machine);

public sealed record class RevokeRecord(int V, string Id, string Machine, DateTime At, string Decision /* the decision's id */) : LedgerRecord(V, Id, Machine);

public sealed record class RunRecord(int V, string Id, string Machine, string Run, DateTime Start, DateTime End, string App, RunCard Card,
    RunRoots Roots, string Verdict, ImmutableDictionary<string, int> Counts) : LedgerRecord(V, Id, Machine);

public sealed record RunCard(string Serial, string? Label, string Fs, string? Model, string InventoryHash);
public sealed record RunRoots(string Video, string Photo);

public sealed record class TornRecord(int V, string Id, string Machine, DateTime At, int Line) : LedgerRecord(V, Id, Machine);

public sealed record class CardDeleteRecord(int V, string Id, string Machine, string Run, DateTime At, string Name, long Size,
    string Src /* card rel path */, string Unit /* ItemId */, DateTime? CaptureUtc,
    string Evidence /* VerifiedThisRun|InLedger|NameSizeMatch, "historyOnly:" prefix, notInLibraryConfirmed, companionOf:<evidence> */,
    string Reason, string Mode /* beforeDate|freeSpace */, RunCard Card, string? Set) : LedgerRecord(V, Id, Machine);
```

```csharp
// src/UasSort.Core/Model/Settings.cs
namespace UasSort.Core;

public sealed record Settings(int Schema, string VideoRoot, string PhotoRoot, ImmutableArray<string> PreviousPhotoRoots,
                              double RadiusMiles, int GapDays, StoredClockMode DroneClockMode /* last learned; default Zone */,
                              string DroneClockZone /* last learned zone; default America/New_York */, bool CopyJpgTwin,
                              MapSettings Map, LayoutSettings Layout, bool RootsConfirmed);
                              // deliberately NO ledger-folder property: STJ would serialise a computed getter as "ledgerDir"

public sealed record MapSettings(string Base /* streets|satellite|none */, string StreetsStyleUrl, string StreetsDarkStyleUrl,
                                 string SatelliteUrl, ImmutableDictionary<string, string> SatellitePresets);
public sealed record LayoutSettings(double TimelineWidth, double MapHeightRatio);
public sealed record SettingsLoad(Settings Settings, bool Recovered, string? CorruptCopyPath, RunRoots? RootsFromLastRun);

public sealed record Draft(int V, string CardKey, string InventoryHash, DateTime SavedUtc, Tuning Tuning, ImmutableArray<PlanEdit> Edits);
```

```csharp
// src/UasSort.Core/Model/Geo.cs
namespace UasSort.Core;

public sealed record TzLookup(string IanaId, ImmutableArray<string> Alternatives, bool IsEtc);
public enum PlaceClass : byte { Populated, Feature }
public sealed record PlaceHit(string Name, GeoPoint Point, PlaceClass Class, string FeatureCode, int Population, string TzId, Distance Away);
public sealed record ResolvedItem(RawItem Raw, ItemTime Time, ItemFlags Flags, GpsFix? Gps, SessionKey? Session);
```

```csharp
// src/UasSort.Core/Model/Plan.cs
namespace UasSort.Core;

public sealed record Tuning(double RadiusMiles = 50, int GapDays = 1);
public readonly record struct GroupId(ItemId Anchor);           // earliest video; stable across re-derivations
public enum BoundaryCause { DayGap, Distance, LibraryFolder, UserSplit }
public sealed record Boundary(GroupId Left, GroupId Right, BoundaryCause Cause, Distance? Jump, TimeSpan Gap, int DayGap);
public sealed record DaySplit(ItemId FirstOfDay, DateOnly From, DateOnly To, Distance? Apart, TimeSpan Gap, bool Emphasised);
public enum Confidence { High, Medium }

public closed record class GroupTarget;
public sealed record class AlreadyImported(LibraryFolderRef Folder) : GroupTarget;
public sealed record class NothingToCopy(string Summary) : GroupTarget;
public sealed record class NewFolder(string RelPath) : GroupTarget;   // @"2026\2026-09\2026-09-27 Zachar Bay"
public sealed record class Append(LibraryFolderRef Folder, Confidence Confidence, string Why, CrossDayHint? Hint) : GroupTarget;
public sealed record class SkipGroup() : GroupTarget;

public sealed record CrossDayHint(string Text, ImmutableArray<PlanEdit> Fix);
public enum DescSource { ExistingFolder, Ledger, Feature, Place, Town, User, None }
public sealed record Suggestion(string Text, DescSource Source, Distance? Away, DateOnly? ForDay);
public sealed record PinState(bool MembershipChanged, int PinnedCount, int NowCount);
public sealed record VideoGroup(GroupId Id, ImmutableArray<ItemId> Videos, GeoPoint? Centroid, Distance Spread,
    DateOnly Start, DateOnly End, LibraryFolderRef? Wall, GroupTarget Target, PinState? TargetPin,
    string Description, DescSource DescSource, bool DescriptionEditable, PinState? NamePin,
    ImmutableArray<Suggestion> Suggestions, ImmutableArray<DaySplit> DaySplits, ImmutableArray<string> Hints,
    bool Foldable, int ColorIndex);
public enum SetResolution { Plain, Resume, DateSuffixed, Imported }
public sealed record SetPlacement(ItemId Set, string FolderName, SetResolution Resolution, ImmutableArray<string> MembersToCopy);
public sealed record PhotoDay(DateOnly Date, string TzId, ImmutableArray<ItemId> Items, string Reason);
public enum IssueSeverity { Blocking, Warning, Info }

public enum IssueCode                       // the closed catalogue of Ref §9.10; tests assert on codes, never on message text
{
    // Planner.Derive (Part 06 Task 06.1 appends NothingNew to this group)
    EmptyFolderName, TempPathTooLong, MediumAppend, EmphasisedDaySplit, PinMembershipChanged, ConflictingPins, SharedTarget,
    FolderExistsAppending, NewBeforeWallFolder, CheckDate, ClockNotSet, ClockMismatch, RootMissing, RootsUnconfirmed,
    LedgerParseIssue, LedgerCloudOnly, LedgerUnwritable, LedgerNotPinned, LedgerNoHistory,
    // PlanSession.Resume and the Settings page
    StaleEditsDropped, NoHistoryInNewRoot,
    // Preflight.Check
    CardIdentityChanged, CardUnreadable, OffloadLockHeld, AppendTargetGone, LowDiskSpace, DuplicateDestination, LedgerUnlistable,
    StaleTempFiles, DestinationAlreadyThere, AssumptionsTicked, ProbablyImportedLeftOut, NewItemsUnticked, UnfinishedRecordings,
    ConflictsLeftOut,
}

public sealed record Issue(IssueSeverity Severity, IssueCode Code, string Message, ItemId? Anchor,
                           ImmutableArray<QuickFix> QuickFixes, bool RequiresAckAtPreflight);
public sealed record QuickFix(string Label, ImmutableArray<PlanEdit> Edits);     // applied as ONE undo entry; Edits empty = a UI action
public sealed record SessionFlags(bool LedgerIssuesAccepted);                     // per session, never persisted in drafts

public sealed record ScanResult(CardInventory Inventory, ImmutableArray<RawItem> Raw, LibraryIndex Library,
                                LedgerSnapshot Ledger, ClockModel Clock, ImmutableArray<ScanWarning> Warnings, Settings Settings);
public sealed record PlanBase(ScanResult Scan, ImmutableArray<Item> Items, ImmutableArray<PhotoDay> PhotoDays,
                              ImmutableDictionary<ItemId, SetPlacement> Sets, ClockSummary Clock, DateTime? WatermarkUtc); // tuning-independent
public sealed record Plan(int Revision, PlanBase Base, Tuning Tuning, ImmutableArray<VideoGroup> Groups,
                          ImmutableArray<Boundary> Boundaries, ImmutableHashSet<ItemId> Included, ImmutableArray<Issue> Issues);
public sealed record GroupDraft(GroupId Id, ImmutableArray<Item> Videos, GeoPoint? Centroid, DateOnly Start, DateOnly End,
                                LibraryFolderRef? Wall, GroupId? UserSplitNeighbour);
public sealed record ClusterResult(ImmutableArray<GroupDraft> Groups, ImmutableArray<Boundary> Boundaries);
```

```csharp
// src/UasSort.Core/Model/Edits.cs
using System.Text.Json.Serialization;

namespace UasSort.Core;

// Item-anchored, so they replay onto any clustering and onto a fresh scan; serialised in drafts (CoreJsonContext, Task 02.5).
[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(Merge), "merge")]
[JsonDerivedType(typeof(SplitBefore), "splitBefore")]
[JsonDerivedType(typeof(MoveToNewGroup), "moveToNewGroup")]
[JsonDerivedType(typeof(MoveToGroup), "moveToGroup")]
[JsonDerivedType(typeof(Rename), "rename")]
[JsonDerivedType(typeof(Retarget), "retarget")]
[JsonDerivedType(typeof(SetIncluded), "setIncluded")]
[JsonDerivedType(typeof(SetDayIncluded), "setDayIncluded")]
public closed record class PlanEdit;
public sealed record class Merge(ItemId InA, ItemId InB) : PlanEdit;
public sealed record class SplitBefore(ItemId First) : PlanEdit;
public sealed record class MoveToNewGroup(ImmutableArray<ItemId> Items) : PlanEdit;
public sealed record class MoveToGroup(ImmutableArray<ItemId> Items, ItemId InTarget) : PlanEdit;
public sealed record class Rename(ItemId InGroup, string? Description /* null = back to suggestion */,
                                  ImmutableArray<ItemId> PinnedMembers) : PlanEdit;
public sealed record class Retarget(ItemId InGroup, TargetChoice Choice, bool ConfirmedBeforeFolderDate,
                                    ImmutableArray<ItemId> PinnedMembers) : PlanEdit;
public sealed record class SetIncluded(ImmutableArray<ItemId> Items, bool Included) : PlanEdit;
public sealed record class SetDayIncluded(DateOnly Day, bool Included) : PlanEdit;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(AutoTarget), "auto")]
[JsonDerivedType(typeof(NewFolderTarget), "newFolder")]
[JsonDerivedType(typeof(AppendTo), "appendTo")]
[JsonDerivedType(typeof(SkipTarget), "skip")]
public closed record class TargetChoice;
public sealed record class AutoTarget() : TargetChoice;
public sealed record class NewFolderTarget() : TargetChoice;
public sealed record class AppendTo(string FolderFullPath) : TargetChoice;
public sealed record class SkipTarget() : TargetChoice;

public enum RejectReason
{
    MergeAcrossLibraryFolders, MoveImportedItem, SplitAtGroupStart, RenameExistingFolder,
    RetargetOutsideVideoRoot, RetargetIntoReservedFolder /* .uas-sort subtree, photo root, previousPhotoRoots */,
    RetargetLaterDatedFolderUnconfirmed, ItemsNotFound,
}
public sealed record Applied(Plan Plan);
public sealed record Rejected(RejectReason Reason, string Message);
public union EditResult(Applied, Rejected);
```

```csharp
// src/UasSort.Core/Ports/IPlanDeriver.cs
namespace UasSort.Core;

/// <summary>The planning seam: PlanSession derives through it; Planner implements it; tests inject GatedPlanDeriver.</summary>
public interface IPlanDeriver
{
    Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct);
}
```

`Library.cs` and `Ledger.cs` reference `DestRoot`, `VerdictLevel` and `AuditCategory`, which Task 02.4 declares; add them now at the top of `src/UasSort.Core/Model/Offload.cs` and `src/UasSort.Core/Model/Audit.cs` so this task compiles, and Task 02.4 completes those two files:

```csharp
// src/UasSort.Core/Model/Offload.cs  (first lines; Task 02.4 appends the rest of the offload model)
namespace UasSort.Core;

public enum DestRoot { Video, Photo }
```

```csharp
// src/UasSort.Core/Model/Audit.cs  (first lines; Task 02.4 appends the rest of the audit model)
namespace UasSort.Core;

public enum AuditCategory { VerifiedThisRun, InLedger, ConfirmedByYou, NameSizeMatch, SkippedByRule, AssumedByRule, Unaccounted } // ascending badness
public enum VerdictLevel { Safe, SafeWithAssumptions, NotSafe }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PlanModelTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Model/Library.cs src/UasSort.Core/Model/Ledger.cs src/UasSort.Core/Model/LedgerRecords.cs src/UasSort.Core/Model/Settings.cs src/UasSort.Core/Model/Geo.cs src/UasSort.Core/Model/Plan.cs src/UasSort.Core/Model/Edits.cs src/UasSort.Core/Model/Offload.cs src/UasSort.Core/Model/Audit.cs src/UasSort.Core/Ports/IPlanDeriver.cs tests/UasSort.Core.Tests/Model/PlanModelTests.cs
git commit -m "feat: add library, ledger, settings, plan and edit model

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 02.4: Offload, audit and cleanup model, ports and `UnsafeIoException`

**Files:**
- Modify: `src/UasSort.Core/Model/Offload.cs` (complete it)
- Modify: `src/UasSort.Core/Model/Audit.cs` (complete it)
- Create: `src/UasSort.Core/Model/Cleanup.cs`
- Create: `src/UasSort.Core/Model/Reports.cs`
- Create: `src/UasSort.Core/Ports/Ports.cs`
- Create: `src/UasSort.Core/Ports/ReviewServices.cs`
- Create: `src/UasSort.Core/Guard/UnsafeIoException.cs`
- Modify: `src/UasSort.Core/UasSort.Core.csproj` (add `InternalsVisibleTo` for `UasSort.Core.Tests` and `UasSort.Testing`)
- Create: `tests/UasSort.Core.Tests/Support/TestCleanupPlans.cs`
- Test: `tests/UasSort.Core.Tests/Model/OffloadCleanupModelTests.cs`

**Interfaces:**

```csharp
// Consumes: Tasks 02.1–02.3.
// Produces (Ref §3, §4.1 verbatim; full declarations in Step 3):
// offload: DestRoot, CopyJob, VerifyMode, CopyPhase, HashMatch, HashMismatch, union VerifyResult, Renamed, TargetExists,
//          union RenameResult, closed CopyOutcome (Verified | AlreadyThere | ConflictAtRename | ChangedOnCard | CardSwapped |
//          Failed | Cancelled | NotStarted), OffloadBatch, FolderPlan, StopReason, OffloadResult, VolumeNeed, PreflightReport { bool CanStart },
//          Ejected, EjectRefused, union EjectResult, ScanPhase, ScanProgress, OffloadProgress
// audit:   AuditCategory, VerdictLevel, AuditLine, UnitAudit, FormatVerdict
// cleanup: CleanupMode, FreeSpaceKind, FreeSpaceGoal { long TargetFreeBytes(CardSpace s) }, CleanupRequest, CleanupEligibility,
//          EvidenceSource, NotInLibraryReason, CardSpace, FileProof, CleanupCandidate, CleanupKept, CleanupCutoff, FreeSpaceShortfall,
//          CleanupInputs, CleanupRows, CleanupAck, CleanupStop, CleanupEnvironment, CleanupResult, CleanupProgress,
//          closed CleanupOutcome (Deleted | SkippedChanged | SkippedEvidenceGone | PartiallyDeleted | CleanupFailed | CleanupNotStarted | CleanupCardSwapped)
public sealed partial class CleanupPlan      // internal 18-argument ctor (all properties, below); Part 08 adds only Confirm(CleanupAck, TimeProvider)
{ internal CleanupPlan(string planId, CardIdentity card, string cardRoot, string inventoryHash, string? cameraModel,
      CleanupRequest request, CardSpace spaceBefore,
      ImmutableArray<CleanupCandidate> delete, ImmutableArray<CleanupCandidate> notInLibraryInScope, CleanupRows rows,
      ImmutableHashSet<ItemId> undecided, ImmutableArray<CleanupKept> notDeletable, CleanupCutoff cutoff, int fileCount,
      long allocatedBytes, long expectedFreeAfter, FreeSpaceShortfall? shortfall, string fingerprint);
  string PlanId; CardIdentity Card; string CardRoot; string InventoryHash; string? CameraModel; CleanupRequest Request;
  CardSpace SpaceBefore; ImmutableArray<CleanupCandidate> Delete; ImmutableArray<CleanupCandidate> NotInLibraryInScope; CleanupRows Rows;
  ImmutableHashSet<ItemId> Undecided; ImmutableArray<CleanupKept> NotDeletable; CleanupCutoff Cutoff; int FileCount; long AllocatedBytes;
  long ExpectedFreeAfter; FreeSpaceShortfall? Shortfall; string Fingerprint; }
public sealed class ConfirmedCleanupPlan     // (defined here, canonical) internal ctor derives FilePaths/SetFolders as
{                                            // PathRules.Join(plan.CardRoot, rel), OrdinalIgnoreCase, and NotInLibraryConfirmed
  internal ConfirmedCleanupPlan(CleanupPlan plan, Guid token, DateTime confirmedUtc);
  CleanupPlan Plan; Guid Token; DateTime ConfirmedUtc; IReadOnlySet<string> FilePaths; IReadOnlySet<string> SetFolders;
  IReadOnlySet<ItemId> NotInLibraryConfirmed; }
// Part 08 adds only CleanupPlan.Confirm (partial, Cleanup/CleanupPlan.Confirm.cs) and CleanupPlanner.Build; it never redeclares these two.
// reports: ReportLine, OffloadReport, CleanupReportLine, CleanupReport
// ports:   IVolumeProvider, IDirectoryLister, ICardSourceValidator, ICardReader, ICardReaderFactory, ICardEraserFactory, ICardEraser,
//          EraseOk, EraseError, union EraseResult, IFileOps, ILedgerStore, ISettingsStore, IDraftStore, IReportStore, IAppAssets,
//          IPowerRequest, IOffloadLock, IDeviceEject, IShellLauncher, ITimeZoneResolver, IPlaceIndex, IThumbnailSource, IPathFacts,
//          IUiDispatcher, DialogResult, DialogRequest, IDialogService
public class UnsafeIoException : Exception    // (defined here) not an IOException, so no IO catch swallows a safety stop
{ UnsafeIoException(); UnsafeIoException(string message); UnsafeIoException(string message, Exception inner); }
// test support (Core.Tests only, internal, namespace UasSort.Core.Tests.Support): TestCleanupPlans.Card, .T, .InventoryHash, .CameraModel,
//   .Candidate(unitRelPath, fileRelPaths, eligibility, setFolder), .Plan(cardRoot, params delete) (18-argument ctor),
//   .Confirmed(cardRoot, params delete). The shared UasSort.Testing.CleanupPlanFixtures is Part 08's (through the UasSort.Testing IVT).
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Support/TestCleanupPlans.cs
namespace UasSort.Core.Tests.Support;

/// <summary>Builds CleanupPlan / ConfirmedCleanupPlan through Core's internal constructors (InternalsVisibleTo Core.Tests).</summary>
internal static class TestCleanupPlans
{
    public static readonly CardIdentity Card = new(0x1A2B3C4D, null, "exFAT", 256_060_514_304);
    public static readonly DateTime T = new(2026, 7, 26, 7, 50, 0, DateTimeKind.Utc);
    public const string InventoryHash = "9f3c0a6d12e4b7a1";
    public const string CameraModel = "FC9113";

    public static CleanupCandidate Candidate(string unitRelPath, IReadOnlyList<string> fileRelPaths,
        CleanupEligibility eligibility = CleanupEligibility.Evidence, string? setFolder = null)
    {
        var cls = setFolder is null ? EntryClass.Video : EntryClass.SetMember;
        var files = fileRelPaths.Select(p => new CardEntry(p, 1000, T, T, T, 0x20, cls, null)).ToImmutableArray();
        return new CleanupCandidate(new ItemId(unitRelPath), files, 131_072L * files.Length, T, new DateOnly(2026, 7, 25),
            "America/Anchorage", eligibility, AuditCategory.InLedger, "in the history, verified", null, null, null,
            setFolder is null ? ItemKind.Video : ItemKind.Set, T.AddHours(-8),
            eligibility == CleanupEligibility.NotInLibrary ? NotInLibraryReason.New : null, setFolder, null, [],
            eligibility == CleanupEligibility.Evidence ? EvidenceSource.Listed : null, false, []);
    }

    public static CleanupPlan Plan(string cardRoot, params CleanupCandidate[] delete)
    {
        var files = delete.Sum(d => d.Files.Length);
        var bytes = delete.Sum(d => d.AllocatedBytes);
        var space = new CardSpace(12_400_000_000, 256_060_514_304, 131_072);
        return new CleanupPlan("plan-1", Card, cardRoot, InventoryHash, CameraModel,
            new CleanupRequest(CleanupMode.BeforeDate, new DateOnly(2026, 7, 26), null,
                delete.Any(d => d.Eligibility == CleanupEligibility.NotInLibrary)),
            space, [.. delete], [], new CleanupRows([], []), [], [],
            new CleanupCutoff(new DateOnly(2026, 7, 26), null, null, null, 0, 0, null), files, bytes, space.FreeBytes + bytes, null,
            "0000000000000000");
    }

    public static ConfirmedCleanupPlan Confirmed(string cardRoot, params CleanupCandidate[] delete)
        => new(Plan(cardRoot, delete), Guid.NewGuid(), T);
}
```

```csharp
// tests/UasSort.Core.Tests/Model/OffloadCleanupModelTests.cs
using System.Reflection;
using UasSort.Core.Tests.Support;

namespace UasSort.Core.Tests.Model;

public class OffloadCleanupModelTests
{
    private static readonly CardSpace Space = new(12_400_000_000, 256_060_514_304, 131_072);

    [Theory]
    [InlineData(FreeSpaceKind.FreeUp, 20_000_000_000L, 32_400_000_000L)]
    [InlineData(FreeSpaceKind.HaveFree, 20_000_000_000L, 20_000_000_000L)]
    [InlineData(FreeSpaceKind.HaveFree, 300_000_000_000L, 256_060_514_304L)]
    public void FreeSpaceGoal_TargetsBothReadings(FreeSpaceKind kind, long bytes, long expected)
        => Assert.Equal(expected, new FreeSpaceGoal(kind, bytes).TargetFreeBytes(Space));

    [Fact]
    public void PreflightReport_CanStartOnlyWithoutBlockingIssues()
    {
        static Issue I(IssueSeverity s) => new(s, IssueCode.StaleTempFiles, "x", null, [], false);
        Assert.True(new PreflightReport([I(IssueSeverity.Warning), I(IssueSeverity.Info)], [], [], [], [], []).CanStart);
        Assert.False(new PreflightReport([I(IssueSeverity.Blocking)], [], [], [], [], []).CanStart);
    }

    [Fact]
    public void ConfirmedCleanupPlan_DerivesCanonicalCaseInsensitiveSets()
    {
        var video = TestCleanupPlans.Candidate("DCIM/DJI_001/DJI_20260726035000_0001_D.MP4",
            ["DCIM/DJI_001/DJI_20260726035000_0001_D.LRF", "DCIM/DJI_001/DJI_20260726035000_0001_D.MP4"]);
        var set = TestCleanupPlans.Candidate("DCIM/PANORAMA/001_0087",
            ["DCIM/PANORAMA/001_0087/PANO_0001.DNG", "DCIM/PANORAMA/001_0087/PANO_0002.DNG"], setFolder: "DCIM/PANORAMA/001_0087");
        var nil = TestCleanupPlans.Candidate("DCIM/DJI_001/DJI_20260726001000_0002_D.MP4",
            ["DCIM/DJI_001/DJI_20260726001000_0002_D.MP4"], CleanupEligibility.NotInLibrary);

        var c = TestCleanupPlans.Confirmed(@"E:\", video, set, nil);

        Assert.Equal(5, c.FilePaths.Count);
        Assert.True(c.FilePaths.Contains(@"e:\dcim\dji_001\DJI_20260726035000_0001_d.lrf"));
        Assert.True(c.FilePaths.Contains(@"E:\DCIM\PANORAMA\001_0087\PANO_0002.DNG"));
        Assert.Equal(@"E:\DCIM\PANORAMA\001_0087", Assert.Single(c.SetFolders));
        Assert.Equal(nil.Unit, Assert.Single(c.NotInLibraryConfirmed));
        Assert.Equal(@"E:\", c.Plan.CardRoot);
        Assert.Equal(TestCleanupPlans.InventoryHash, c.Plan.InventoryHash);
        Assert.Equal(TestCleanupPlans.CameraModel, c.Plan.CameraModel);
    }

    [Fact]
    public void CleanupPlan_And_ConfirmedCleanupPlan_HaveNoPublicConstructor()
    {
        Assert.Empty(typeof(CleanupPlan).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(typeof(ConfirmedCleanupPlan).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void Outcome_And_Result_Unions_AreExhaustive()
    {
        var job = new CopyJob(new ItemId("DCIM/DJI_001/a.MP4"), "DCIM/DJI_001/a.MP4", 1, TestCleanupPlans.T, TestCleanupPlans.T,
            @"C:\V\a.MP4", DestRoot.Video, null, false);
        CopyOutcome[] outcomes =
        [
            new Verified(job, 1, VerifyMode.Unbuffered), new AlreadyThere(job), new ConflictAtRename(job),
            new ChangedOnCard(job, 2, TestCleanupPlans.T), new CardSwapped(job, TestCleanupPlans.Card),
            new Failed(job, CopyPhase.Verify, "hash mismatch"), new Cancelled(job), new NotStarted(job),
        ];
        Assert.Equal(8, outcomes.Select(o => o switch
        {
            Verified => 1, AlreadyThere => 2, ConflictAtRename => 3, ChangedOnCard => 4, CardSwapped => 5,
            Failed => 6, Cancelled => 7, NotStarted => 8,
        }).Distinct().Count());

        var u = new ItemId("DCIM/DJI_001/a.MP4");
        CleanupOutcome[] cleanup =
        [
            new Deleted(u, 1, 1, false), new SkippedChanged(u, u.CardRelPath, null, null),
            new SkippedEvidenceGone(u, u.CardRelPath, "no longer listed"), new PartiallyDeleted(u, [], [], "error"),
            new CleanupFailed(u, u.CardRelPath, 5, "Access is denied."), new CleanupNotStarted(u),
            new CleanupCardSwapped(u, TestCleanupPlans.Card),
        ];
        Assert.Equal(7, cleanup.Select(o => o switch
        {
            Deleted => 1, SkippedChanged => 2, SkippedEvidenceGone => 3, PartiallyDeleted => 4, CleanupFailed => 5,
            CleanupNotStarted => 6, CleanupCardSwapped => 7,
        }).Distinct().Count());

        EraseResult erase = new EraseError(19, "The media is write protected.");
        VerifyResult verify = new HashMismatch(7, VerifyMode.Cached);
        RenameResult rename = new TargetExists();
        EjectResult eject = new EjectRefused(@"D:\", "in use");
        Assert.Equal("err", erase switch { EraseOk => "ok", EraseError => "err" });
        Assert.Equal("mismatch", verify switch { HashMatch => "match", HashMismatch => "mismatch" });
        Assert.Equal("exists", rename switch { Renamed => "renamed", TargetExists => "exists" });
        Assert.Equal("refused", eject switch { Ejected => "ejected", EjectRefused => "refused" });
    }

    [Fact]
    public void UnsafeIoException_IsNotAnIOException()
    {
        var e = new UnsafeIoException("ReadData of a library file");
        Assert.IsNotAssignableFrom<IOException>(e);
        Assert.Equal("ReadData of a library file", e.Message);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*OffloadCleanupModelTests"`
Expected: build FAILS (CS0246: `CleanupCandidate`, `CleanupPlan`, `CopyOutcome`, `UnsafeIoException`, … not found).

- [ ] **Step 3: Implement**

Add to `src/UasSort.Core/UasSort.Core.csproj`:

```xml
  <ItemGroup>
    <!-- Ref §10.6: only test code reaches CleanupPlan/ConfirmedCleanupPlan's internal constructors: Core.Tests
         (TestCleanupPlans) and the shared fakes (CleanupPlanFixtures, Part 08). No assembly-attribute file anywhere else. -->
    <InternalsVisibleTo Include="UasSort.Core.Tests" />
    <InternalsVisibleTo Include="UasSort.Testing" />
  </ItemGroup>
```

```csharp
// src/UasSort.Core/Model/Offload.cs  (whole file)
namespace UasSort.Core;

public enum DestRoot { Video, Photo }

public sealed record CopyJob(ItemId Item, string CardRelPath, long Size, DateTime CardMtimeUtc, DateTime CardCreationUtc,
                             string DestPath, DestRoot Root, GroupId? Group, bool CreatesFolder);
public enum VerifyMode { Unbuffered, Cached }
public enum CopyPhase { CardCheck, Stat, CreateTemp, Copy, Flush, Verify, Finalize, Rename, Confirm, Ledger }
public sealed record HashMatch(VerifyMode Mode);
public sealed record HashMismatch(UInt128 Got, VerifyMode Mode);
public union VerifyResult(HashMatch, HashMismatch);
public sealed record Renamed;
public sealed record TargetExists;
public union RenameResult(Renamed, TargetExists);

public closed record class CopyOutcome(CopyJob Job);
public sealed record class Verified(CopyJob Job, UInt128 Hash, VerifyMode Mode) : CopyOutcome(Job);
public sealed record class AlreadyThere(CopyJob Job) : CopyOutcome(Job);
public sealed record class ConflictAtRename(CopyJob Job) : CopyOutcome(Job);
public sealed record class ChangedOnCard(CopyJob Job, long NowSize, DateTime NowMtimeUtc) : CopyOutcome(Job);
public sealed record class CardSwapped(CopyJob Job, CardIdentity Now) : CopyOutcome(Job);
public sealed record class Failed(CopyJob Job, CopyPhase Phase, string Error) : CopyOutcome(Job);
public sealed record class Cancelled(CopyJob Job) : CopyOutcome(Job);
public sealed record class NotStarted(CopyJob Job) : CopyOutcome(Job);

public sealed record OffloadBatch(string RunId, CardIdentity Card, ImmutableArray<CopyJob> Jobs,
                                  ImmutableArray<ItemId> SeenIfNotCopied, ImmutableArray<FolderPlan> Folders);
public sealed record FolderPlan(GroupId Group, string FullPath, bool Create, string Description, GeoPoint? Centroid,
                                DateOnly Start, DateOnly End, string TzId);
public enum StopReason { Cancelled, CardSwapped, CardRemoved, DestinationFull, DestinationLost, LedgerWriteFailed, InternalSafetyStop }
public sealed record OffloadResult(string RunId, ImmutableArray<CopyOutcome> Outcomes, StopReason? Stop /* null = ran to the end */,
                                   DateTime StartUtc, DateTime EndUtc, ImmutableArray<string> VolumesNeedingSafeRemoval);

public sealed record VolumeNeed(string Volume, int Files, long Bytes, long FreeBytes, long RequiredFree);   // RequiredFree = Σ + max(1 GiB, 2 % of Σ)
public sealed record PreflightReport(ImmutableArray<Issue> Issues, ImmutableArray<string> FoldersToCreate,
                                     ImmutableArray<(string Path, Confidence Confidence)> FoldersAppended, ImmutableArray<VolumeNeed> Volumes,
                                     ImmutableArray<string> StaleTemps /* listed only; deleted at Start offload */,
                                     ImmutableArray<ItemId> AlreadyThere)
{
    public bool CanStart => !Issues.Any(i => i.Severity == IssueSeverity.Blocking);
}

public sealed record Ejected(string Volume);
public sealed record EjectRefused(string Volume, string Reason);
public union EjectResult(Ejected, EjectRefused);

public enum ScanPhase { ListingCard, ListingLibrary, ReadingLedger, ReadingMetadata, BuildingPlan }
public sealed record ScanProgress(ScanPhase Phase, int Done, int Total, string? Current);
public sealed record OffloadProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, double MBps /* rolling 5 s */,
                                     TimeSpan? Eta, string? CurrentFile, CopyPhase Phase, GroupId? Group);
```

```csharp
// src/UasSort.Core/Model/Audit.cs  (whole file)
namespace UasSort.Core;

public enum AuditCategory { VerifiedThisRun, InLedger, ConfirmedByYou, NameSizeMatch, SkippedByRule, AssumedByRule, Unaccounted } // ascending badness
public enum VerdictLevel { Safe, SafeWithAssumptions, NotSafe }
public sealed record AuditLine(string CardRelPath, long Size, AuditCategory Category, string Detail);
public sealed record UnitAudit(ItemId Unit, AuditCategory Worst, ImmutableArray<AuditLine> Lines);
public sealed record FormatVerdict(VerdictLevel Level, CardIdentity Card, string Headline,
                                   ImmutableDictionary<AuditCategory, int> Counts, int NameSizeOnly, int CachedVerifies,
                                   ImmutableArray<UnitAudit> Units, ImmutableArray<string> CardChanges, string? SafeRemovalNote);
```

```csharp
// src/UasSort.Core/Model/Cleanup.cs
namespace UasSort.Core;

public enum CleanupMode { BeforeDate, FreeSpace }
public enum FreeSpaceKind { HaveFree /* default: target = Bytes */, FreeUp /* target = free + Bytes */ }

public sealed record FreeSpaceGoal(FreeSpaceKind Kind, long Bytes /* decimal GB × 10^9 */)
{
    public long TargetFreeBytes(CardSpace s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Kind == FreeSpaceKind.FreeUp ? s.FreeBytes + Bytes : Math.Min(Bytes, s.TotalBytes);
    }
}

public sealed record CleanupRequest(CleanupMode Mode,
                                    DateOnly? Before /* BeforeDate: site-local day, kept */,
                                    FreeSpaceGoal? Goal /* FreeSpace */, bool IncludeNotInLibrary);
public enum CleanupEligibility { Evidence, NotInLibrary, Never }       // ascending strictness; a unit takes the worst of its files
public enum EvidenceSource { Listed, HistoryOnly }
public enum NotInLibraryReason { New, ProbablyImported, Conflict, Unfinished, Dismissed, RecordedAsImported, NoLongerInLibrary }
public sealed record CardSpace(long FreeBytes, long TotalBytes, int ClusterBytes);   // GetDiskFreeSpaceExW + GetDiskFreeSpaceW
public sealed record FileProof(string CardRelPath, FileKey Key, AuditCategory Category, string? ListedFolder,
                               bool LedgerVerified, string? DecisionId);
public sealed record CleanupCandidate(ItemId Unit, ImmutableArray<CardEntry> Files /* incl. twin and companions, in delete order */,
    long AllocatedBytes /* Σ size rounded up to ClusterBytes */, DateTime CaptureUtc, DateOnly LocalDate, string TzId,
    CleanupEligibility Eligibility, AuditCategory Evidence /* worst category of the primary files */, string Reason,
    TimeSpan? Duration, GeoPoint? Location, string? PlaceLabel /* "near Anvil Mountain · 0.2 mi" */,
    ItemKind Kind, DateTime LocalTime, NotInLibraryReason? NotInLibrary, string? SetFolder /* card rel dir, removed once empty */,
    SessionKey? Session, ImmutableArray<FileProof> Proofs /* one per primary file */,
    EvidenceSource? Source /* Evidence units */, bool TickedForOffload,
    ImmutableArray<CardEntry> NeverCopied /* companions and uncopied JPG twins, for the summary line */);
public sealed record CleanupKept(ItemId? Unit, ImmutableArray<string> CardRelPaths, long Bytes, DateTime? CaptureUtc, string Reason);
public sealed record CleanupCutoff(DateOnly? BeforeDate, DateTime? LastCaptureUtc, DateTime? LastLocalTime, string? TzId,
                                   int FilesDeletedOnCutoffDay, int FilesOnCutoffDay, DateTime? FlightContinuesLocal);
public sealed record FreeSpaceShortfall(long FreeableBytes, long HeldByNotInLibrary, long HeldByNever);
public sealed record CleanupInputs(CardInventory Inventory, Plan Plan, FormatVerdict Audit, OffloadResult? Offload, CardSpace Space,
                                   LibraryListings FreshListings, LedgerSnapshot FreshLedger,
                                   VolumeInfo Volume, IPlaceIndex? Places, Settings Settings);
public sealed record CleanupRows(ImmutableHashSet<ItemId> Keep, ImmutableHashSet<ItemId> Delete /* the rest in range are undecided */);

/// <summary>A class, not a record: no `with`. Only CleanupPlanner.Build (Part 08) calls the 18-argument constructor, passing
/// the inventory's InventoryHash and CameraModel; Confirm(CleanupAck, TimeProvider) is added by Part 08 in
/// Cleanup/CleanupPlan.Confirm.cs (partial, namespace UasSort.Core).</summary>
public sealed partial class CleanupPlan
{
    internal CleanupPlan(string planId, CardIdentity card, string cardRoot, string inventoryHash, string? cameraModel,
        CleanupRequest request, CardSpace spaceBefore,
        ImmutableArray<CleanupCandidate> delete, ImmutableArray<CleanupCandidate> notInLibraryInScope, CleanupRows rows,
        ImmutableHashSet<ItemId> undecided, ImmutableArray<CleanupKept> notDeletable, CleanupCutoff cutoff, int fileCount,
        long allocatedBytes, long expectedFreeAfter, FreeSpaceShortfall? shortfall, string fingerprint)
    {
        PlanId = planId; Card = card; CardRoot = cardRoot; InventoryHash = inventoryHash; CameraModel = cameraModel;
        Request = request; SpaceBefore = spaceBefore;
        Delete = delete; NotInLibraryInScope = notInLibraryInScope; Rows = rows; Undecided = undecided;
        NotDeletable = notDeletable; Cutoff = cutoff; FileCount = fileCount; AllocatedBytes = allocatedBytes;
        ExpectedFreeAfter = expectedFreeAfter; Shortfall = shortfall; Fingerprint = fingerprint;
    }

    public string PlanId { get; }
    public CardIdentity Card { get; }
    public string CardRoot { get; }
    public string InventoryHash { get; }                                    // the scanned inventory the plan was built from
    public string? CameraModel { get; }                                     // CardInventory.CameraModel (cardDelete records' RunCard)
    public CleanupRequest Request { get; }
    public CardSpace SpaceBefore { get; }
    public ImmutableArray<CleanupCandidate> Delete { get; }                 // oldest first
    public ImmutableArray<CleanupCandidate> NotInLibraryInScope { get; }    // the review list
    public CleanupRows Rows { get; }
    public ImmutableHashSet<ItemId> Undecided { get; }                      // rows that joined later, not yet set
    public ImmutableArray<CleanupKept> NotDeletable { get; }                // the "Kept" list
    public CleanupCutoff Cutoff { get; }
    public int FileCount { get; }
    public long AllocatedBytes { get; }
    public long ExpectedFreeAfter { get; }
    public FreeSpaceShortfall? Shortfall { get; }
    public string Fingerprint { get; }                                      // for binding the checkboxes; Confirm recomputes it
}

public sealed record CleanupAck(string PlanFingerprint, bool CantBeRecovered, bool IncludesNotInLibrary,
                                ImmutableHashSet<ItemId> NotInLibraryDelete /* review rows left on Delete */);

/// <summary>A class, not a record, so `with` can't copy it; the constructor is internal to Core (Confirm is the only production
/// factory). This derivation (PathRules.Join, OrdinalIgnoreCase) is the canonical one; Part 08 never re-derives the sets.</summary>
public sealed class ConfirmedCleanupPlan
{
    internal ConfirmedCleanupPlan(CleanupPlan plan, Guid token, DateTime confirmedUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan;
        Token = token;
        ConfirmedUtc = confirmedUtc;
        var files = new List<string>();
        var sets = new List<string>();
        var notInLibrary = new List<ItemId>();
        foreach (var c in plan.Delete)
        {
            foreach (var f in c.Files) files.Add(PathRules.Join(plan.CardRoot, f.RelPath));
            if (c.SetFolder is { } folder) sets.Add(PathRules.Join(plan.CardRoot, folder));
            if (c.Eligibility == CleanupEligibility.NotInLibrary) notInLibrary.Add(c.Unit);
        }
        FilePaths = files.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        SetFolders = sets.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        NotInLibraryConfirmed = notInLibrary.ToImmutableHashSet();
    }

    public CleanupPlan Plan { get; }
    public Guid Token { get; }
    public DateTime ConfirmedUtc { get; }
    public IReadOnlySet<string> FilePaths { get; }              // canonical card paths the guard allows for CardDelete
    public IReadOnlySet<string> SetFolders { get; }             // canonical set folders: RemoveDirectory only, once empty
    public IReadOnlySet<ItemId> NotInLibraryConfirmed { get; }  // the per-unit confirmation tokens of NotInLibrary units
}

// outcome per unit; the cases that CopyOutcome also has are prefixed "Cleanup" to keep the names apart
public closed record class CleanupOutcome(ItemId Unit);
public sealed record class Deleted(ItemId Unit, int Files, long Bytes, bool SetFolderRemoved) : CleanupOutcome(Unit);
public sealed record class SkippedChanged(ItemId Unit, string CardRelPath, long? NowSize /* null = gone */, DateTime? NowMtimeUtc) : CleanupOutcome(Unit);
public sealed record class SkippedEvidenceGone(ItemId Unit, string CardRelPath, string Why) : CleanupOutcome(Unit);
public sealed record class PartiallyDeleted(ItemId Unit, ImmutableArray<string> DeletedPaths, ImmutableArray<string> StillOnCard, string Why) : CleanupOutcome(Unit);
public sealed record class CleanupFailed(ItemId Unit, string CardRelPath, int Win32Error, string Error) : CleanupOutcome(Unit);
public sealed record class CleanupNotStarted(ItemId Unit) : CleanupOutcome(Unit);
public sealed record class CleanupCardSwapped(ItemId Unit, CardIdentity Now) : CleanupOutcome(Unit);

public enum CleanupStop
{
    OffloadLockHeld, LedgerUnavailable, Cancelled, CardSwapped, CardRemoved, WriteProtected, LedgerWriteFailed, InternalSafetyStop,
}

public sealed record CleanupEnvironment(CardSource Source, CardIdentity Pinned, ICardReader Reader, IThumbnailSource Thumbnails,
    ICardEraserFactory Erasers, IDirectoryLister Lister, ILedgerStore Ledger, IOffloadLock Lock, IPowerRequest Power,
    TimeProvider Clock, Settings Settings);

public sealed record CleanupResult(string RunId, ConfirmedCleanupPlan Plan, ImmutableArray<CleanupOutcome> Outcomes,
    CleanupStop? Stop /* null = ran to the end */, CardSpace SpaceAfter /* re-read */,
    ImmutableArray<string> StillListed /* deleted, yet present at the re-list */,
    DateTime StartUtc, DateTime EndUtc);

public sealed record CleanupProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, string? CurrentFile, ItemId? Unit);
```

```csharp
// src/UasSort.Core/Model/Reports.cs
namespace UasSort.Core;

public sealed record ReportLine(string CardRelPath, string? Dest, string Outcome, CopyPhase? Phase, string? Error, string? Xxh128, string? Verify);
public sealed record OffloadReport(int V, string RunId, Settings SettingsSnapshot, string PlanSummary, ImmutableArray<ReportLine> Files,
                                   ImmutableArray<AuditLine> Audit, ImmutableArray<string> CardChanges, VerdictLevel Verdict,
                                   string Headline, StopReason? Stop);
public sealed record CleanupReportLine(string CardRelPath, long Size, string Unit, string Outcome, string Eligibility, string Evidence,
                                       string Reason, int? Win32Error, bool LedgerRecorded);
public sealed record CleanupReport(int V, string RunId, CardIdentity Card, CleanupRequest Request, CleanupCutoff Cutoff,
                                   ImmutableArray<CleanupReportLine> Files, ImmutableArray<CleanupKept> NotDeletable, CleanupStop? Stop,
                                   long FreeBefore, long FreeAfter, VerdictLevel VerdictAfter /* NotSafe if the rescan failed */);
```

```csharp
// src/UasSort.Core/Ports/Ports.cs
namespace UasSort.Core;

public interface IVolumeProvider { IReadOnlyList<VolumeInfo> GetVolumes(); }

public interface IDirectoryLister                                 // listing only; never opens a file
{
    // AttributesToSkip=0, IgnoreInaccessible=false, errors collected; a directory whose name is in excludeDirNames
    // (case-insensitive) is neither returned nor entered. Library roots pass {".uas-sort"}; the card, the ledger
    // store and stale-temp checks pass {}.
    ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames);
}

public interface ICardSourceValidator                             // anchor + overlap + sync roots (Ref §4.3)
{
    // IsBrowsedFolder = detected is null, even when a Browse result is a volume root;
    // IsWriteProtected = detected?.IsReadOnlyVolume ?? false; Identity = detected?.Identity when detected is given
    CardSourceCheck Validate(string chosenPath, VolumeInfo? detected /* null = Browse to folder (and the CLI) */,
                             Settings s, IDirectoryLister lister, IPathFacts facts, string appDataDir);
}

public interface IPathFacts                                       // Platform: canonical paths and sync roots (Ref §4.3 steps 2–3)
{
    string Canonical(string path);                                // GetFullPath → BACKUP_SEMANTICS handle → GetFinalPathNameByHandleW
    bool InSyncRoot(string canonicalPath);                        // CfGetSyncRootInfoByPath succeeds
    IReadOnlyList<string> SyncRoots();                            // OneDrive UserFolder values + HKLM SyncRootManager roots
}

public interface ICardReader                                      // bound to one CardSource + CardIdentity; FileAccess.Read, FileShare.ReadWrite
{
    CardIdentity CurrentIdentity();                               // GetVolumeInformationW on the card root (cheap)
    Stream OpenRandom(string cardRelPath);                        // probes, thumbnails (4 KB block cache on top)
    Stream OpenSequential(string cardRelPath);                    // copy
    FsEntry Stat(string cardRelPath);
    ListingResult Relist();                                       // audit-time re-listing
    CardSpace Space();                                            // GetDiskFreeSpaceExW + GetDiskFreeSpaceW; opens nothing
}

public interface ICardReaderFactory { ICardReader Open(CardSource source, CardIdentity identity); }

public interface ICardEraserFactory                               // Platform; Card cleanup only (Ref §10.6); re-derives every volume fact
{                                                                 // from Win32 and builds the eraser's own GuardContext; throws UnsafeIoException
    ICardEraser Open(CardSource source, CardIdentity pinned, ConfirmedCleanupPlan plan);
}

public interface ICardEraser : IDisposable                        // IoGuardPolicy.Check(CardDelete, …) before every call
{
    EraseResult DeleteFile(string cardRelPath);                   // DeleteFileW(\\?\…); never clears attributes, never opens the file
    EraseResult RemoveEmptySetFolder(string cardRelDir);          // RemoveDirectoryW(\\?\…); fails on a non-empty folder; never recursive
}

public sealed record EraseOk;
public sealed record EraseError(int Win32Error, string Message);
public union EraseResult(EraseOk, EraseError);

public interface IFileOps                                         // destination writes only; guarded
{
    Stream CreateTemp(string finalPath, long size, out string tempPath);   // CreateNew + preallocate, Hidden|NotContentIndexed, "<final>.uas-sort.tmp"
    void FlushToDisk(Stream s);
    VerifyResult VerifyHash(string tempPath, long size, UInt128 expected, CancellationToken ct);
    void FinalizeAttributes(string tempPath, DateTime creationUtc, DateTime mtimeUtc);   // copy times, clear Hidden
    RenameResult RenameNoReplace(string tempPath, string finalPath);                       // MoveFileExW, never REPLACE_EXISTING
    bool ConfirmFinal(string finalPath, long size);                                        // metadata only
    void FlushDestination(string dir, IReadOnlyList<string> filesCreatedThisRun);          // non-NTFS / removable volumes
    void DeleteOwnTemp(string tempPath);
    void EnsureDirectory(string dir, bool allowCreate);           // allowCreate only for NewFolder paths and their YYYY/YYYY-MM parents
    bool TryGetSize(string path, out long size);
    long FreeBytes(string anyPathOnVolume);
}

public interface ILedgerStore                                     // folder = LedgerPaths.For(videoRoot) (derived, never configured)
{
    LedgerFolderStatus Check();                                   // attributes and security descriptors only, BEFORE any open
    LedgerSnapshot Load();                                        // union of every top-level ledger*.jsonl, deduped by id; honours torn
    void EnsureFolder();                                          // creates <videoRoot>\.uas-sort (folder only), then KeepOnDevice()
    ILedgerWriter OpenOwn();                                      // ledger-<MACHINE>.jsonl, append, FileShare.Read; torn-tail repair; mirrored
    void SnapshotToBackup(string runId);                          // BackupDir\snapshots\<yyyyMMdd-HHmmss>-<run8>\ (keeps 20)
    void KeepOnDevice();                                          // FILE_ATTRIBUTE_PINNED on the .uas-sort folder only
    void CopyInto(string newVideoRoot, LedgerSnapshot current);   // video-root change [Copy]
}

public interface ISettingsStore { SettingsLoad Load(bool readOnly = false); void Save(Settings s); }
public interface IDraftStore { Draft? Load(string cardKey); void Save(string cardKey, Draft d); void Delete(string cardKey); }
public interface IReportStore { string Save(OffloadReport r); string Save(CleanupReport r); }
public interface IAppAssets { Stream OpenPlaces(); Stream OpenSelfTest(string name); }   // Part 11 adds OpenMapAsset only at its fallback 2
public interface IPowerRequest { IDisposable KeepSystemAwake(string reason); }
public interface IOffloadLock { IDisposable? TryAcquire(); }                    // named mutex Local\uas-sort-offload
public interface IDeviceEject { EjectResult Eject(string volumeRoot); }
public interface IShellLauncher { void OpenFolder(string path); void OpenFile(string path); void OpenHttps(Uri uri); }
public interface ITimeZoneResolver { TzLookup Resolve(GeoPoint p); }
public interface IPlaceIndex { IReadOnlyList<PlaceHit> Near(GeoPoint p, Distance r, PlaceClass cls, int max); }

public interface IThumbnailSource                                 // bytes, not images
{
    ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct);
    IDisposable Pause();                                          // Commit and Card cleanup: closes the cached card handles
}
```

```csharp
// src/UasSort.Core/Ports/ReviewServices.cs
namespace UasSort.Core;

public interface IUiDispatcher { bool HasThreadAccess { get; } void Post(Action a); }
public enum DialogResult { Primary, Secondary, Close }
public sealed record DialogRequest(string Title, string Body, string Primary, string? Secondary, string Close);
public interface IDialogService { Task<DialogResult> ShowAsync(DialogRequest r); }   // queued: one ContentDialog open at a time
```

```csharp
// src/UasSort.Core/Guard/UnsafeIoException.cs
namespace UasSort.Core;

/// <summary>An IO operation the guard refused (Ref §4.3). Deliberately NOT an IOException: IO error handling must never
/// swallow a safety stop. The run stops with InternalSafetyStop and the event is logged as a bug.</summary>
public class UnsafeIoException : Exception
{
    public UnsafeIoException() : base("Unsafe IO refused by the guard") { }
    public UnsafeIoException(string message) : base(message) { }
    public UnsafeIoException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*OffloadCleanupModelTests"`
Expected: PASS (8 tests). Then `dotnet test --solution uas-sort.slnx` — all green.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/UasSort.Core.csproj src/UasSort.Core/Model/Offload.cs src/UasSort.Core/Model/Audit.cs src/UasSort.Core/Model/Cleanup.cs src/UasSort.Core/Model/Reports.cs src/UasSort.Core/Ports/Ports.cs src/UasSort.Core/Ports/ReviewServices.cs src/UasSort.Core/Guard/UnsafeIoException.cs tests/UasSort.Core.Tests/Support/TestCleanupPlans.cs tests/UasSort.Core.Tests/Model/OffloadCleanupModelTests.cs
git commit -m "feat: add offload, audit and cleanup model, ports and UnsafeIoException

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 02.5: Source-generated JSON contexts and converters

**Files:**
- Create: `src/UasSort.Core/Json/Converters.cs`
- Create: `src/UasSort.Core/Json/LedgerJsonContext.cs`
- Create: `src/UasSort.Core/Json/CoreJsonContext.cs`
- Modify: `src/UasSort.Core/Model/Primitives.cs` (converter attributes on `ItemId` and `GeoPoint`)
- Test: `tests/UasSort.Core.Tests/Json/JsonContextTests.cs`

**Interfaces:**

```csharp
// Consumes: Tasks 02.1–02.4 (LedgerRecord family, PlanEdit/TargetChoice, Draft, Settings, OffloadReport, CleanupReport).
// Produces:
namespace UasSort.Core.Json;
public sealed class GeoPointJsonConverter : JsonConverter<GeoPoint>;   // [lon,lat]
public sealed class ItemIdJsonConverter : JsonConverter<ItemId>;       // (defined here) a plain string "DCIM/DJI_001/…"
public sealed partial class LedgerJsonContext : JsonSerializerContext  // one compact line per record; camelCase; enums as names;
{ JsonTypeInfo<LedgerRecord> LedgerRecord; }                           // AllowOutOfOrderMetadataProperties
public sealed partial class CoreJsonContext : JsonSerializerContext    // indented; camelCase; enums as names; AllowOutOfOrderMetadataProperties
{ JsonTypeInfo<Settings> Settings; JsonTypeInfo<Draft> Draft; JsonTypeInfo<PlanEdit> PlanEdit; JsonTypeInfo<TargetChoice> TargetChoice;
  JsonTypeInfo<OffloadReport> OffloadReport; JsonTypeInfo<CleanupReport> CleanupReport; JsonTypeInfo<GeoPoint> GeoPoint; }
```

Rules (Ref §3 "JSON"): unions (`GpsProbe`, `EditResult`, …) are never serialised; `CopyOutcome`, `CleanupOutcome`, `CleanupPlan` and `ConfirmedCleanupPlan` are never serialised (reports use `ReportLine` / `CleanupReportLine`). Map messages get their own context in Review (Part 10).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Json/JsonContextTests.cs
using System.Text.Json;

namespace UasSort.Core.Tests.Json;

public class JsonContextTests
{
    // Ref §11 example lines with concrete values
    private const string FileLine = """{"t":"file","v":1,"id":"6d0e","machine":"DESKTOP-A","run":"8f1c","at":"2026-09-27T21:07:02Z","kind":"video","name":"DJI_20260927140627_0128_D.MP4","size":89612345,"src":"DCIM/DJI_001/DJI_20260927140627_0128_D.MP4","root":"video","dest":"C:\\V\\2026\\2026-09\\2026-09-27 Zachar Bay\\DJI_20260927140627_0128_D.MP4","xxh128":"5e0c","verify":"unbuffered","mtime":"2026-09-27T18:08:01Z","captureUtc":"2026-09-27T18:06:27Z","timeSource":"Mvhd","lat":57.5504421,"lon":-153.738973,"tz":"America/Anchorage","localDate":"2026-09-27","sessionUtc":"2026-09-27T17:59:28Z","serial":"SER1","set":null}""";

    private const string RunLine = """{"t":"run","v":1,"id":"u1","machine":"DESKTOP-A","run":"8f1c","start":"2026-09-27T21:00:00Z","end":"2026-09-27T21:30:00Z","app":"0.1.0","card":{"serial":"1A2B3C4D","label":null,"fs":"exFAT","model":"FC9113","inventoryHash":"9f3c0a6d12e4b7a1"},"roots":{"video":"C:\\V","photo":"C:\\V\\Picture Offload"},"verdict":"SafeWithAssumptions","counts":{"VerifiedThisRun":17,"AssumedByRule":40}}""";

    public static TheoryData<string, string> LedgerLines => new()
    {
        { "file", FileLine },
        { "folder", """{"t":"folder","v":1,"id":"f1","machine":"DESKTOP-A","run":"8f1c","path":"C:\\V\\2026\\2026-09\\2026-09-27 Zachar Bay","desc":"Zachar Bay","source":"created","lat":57.5415,"lon":-153.7409,"start":"2026-09-27","end":"2026-09-27","tz":"America/Anchorage"}""" },
        { "seen", """{"t":"seen","v":1,"id":"s1","machine":"DESKTOP-A","run":"8f1c","at":"2026-10-04T20:11:00Z","name":"DJI_20261002224012_0131_D.DNG","size":27399100,"src":"DCIM/DJI_001/DJI_20261002224012_0131_D.DNG","captureUtc":"2026-10-02T22:40:12Z","status":"New","why":"unticked","set":null}""" },
        { "decision", """{"t":"decision","v":1,"id":"b02","machine":"DESKTOP-A","run":"8f1c","at":"2026-09-27T21:40:00Z","kind":"assumedImported","name":"PANO_0001.DNG","size":13751808,"src":"DCIM/PANORAMA/001_0087/PANO_0001.DNG","captureUtc":"2026-05-25T13:30:28Z","why":"confirmed by you","set":"001_0087"}""" },
        { "revoke", """{"t":"revoke","v":1,"id":"r1","machine":"LAPTOP-B","at":"2026-10-05T02:00:00Z","decision":"a91"}""" },
        { "torn", """{"t":"torn","v":1,"id":"t1","machine":"DESKTOP-A","at":"2026-10-06T18:02:11Z","line":412}""" },
        { "cardDelete", """{"t":"cardDelete","v":1,"id":"c1","machine":"DESKTOP-A","run":"c4e2","at":"2026-10-12T19:30:05Z","name":"DJI_20260725232655_0117_D.MP4","size":1234567890,"src":"DCIM/DJI_001/DJI_20260725232655_0117_D.MP4","unit":"DCIM/DJI_001/DJI_20260725232655_0117_D.MP4","captureUtc":"2026-07-26T03:26:55Z","evidence":"InLedger","reason":"in the history, verified","mode":"beforeDate","card":{"serial":"1A2B3C4D","label":null,"fs":"exFAT","model":"FC9113","inventoryHash":"9f3c0a6d12e4b7a1"},"set":null}""" },
        { "run", RunLine },
    };

    [Theory]
    [MemberData(nameof(LedgerLines))]
    public void LedgerRecords_RoundTripThroughTheSourceGeneratedContext(string kind, string line)
    {
        var record = JsonSerializer.Deserialize(line, LedgerJsonContext.Default.LedgerRecord)!;
        var once = JsonSerializer.Serialize(record, LedgerJsonContext.Default.LedgerRecord);
        var twice = JsonSerializer.Serialize(JsonSerializer.Deserialize(once, LedgerJsonContext.Default.LedgerRecord)!,
                                             LedgerJsonContext.Default.LedgerRecord);
        Assert.StartsWith($$"""{"t":"{{kind}}",""", once, StringComparison.Ordinal);
        Assert.Equal(once, twice);
        Assert.DoesNotContain("\n", once, StringComparison.Ordinal);
        Assert.Equal(1, record.V);
    }

    [Fact]
    public void FileRecord_KeepsEveryField()
    {
        var r = Assert.IsType<FileRecord>(JsonSerializer.Deserialize(FileLine,
            LedgerJsonContext.Default.LedgerRecord));
        Assert.Equal(89_612_345, r.Size);
        Assert.Equal(new DateOnly(2026, 9, 27), r.LocalDate);
        Assert.Equal(DateTimeKind.Utc, r.CaptureUtc!.Value.Kind);
        Assert.Equal(57.5504421, r.Lat);
        Assert.Null(r.Set);
    }

    [Fact]
    public void RunRecord_KeepsCountsKeysVerbatim()
    {
        var r = Assert.IsType<RunRecord>(JsonSerializer.Deserialize(RunLine,
            LedgerJsonContext.Default.LedgerRecord));
        Assert.Equal(17, r.Counts["VerifiedThisRun"]);
        Assert.Equal("FC9113", r.Card.Model);
    }

    [Fact]
    public void Discriminator_MayComeAfterOtherProperties()
    {
        var r = JsonSerializer.Deserialize("""{"v":1,"id":"t1","machine":"DESKTOP-A","t":"torn","at":"2026-10-06T18:02:11Z","line":412}""",
            LedgerJsonContext.Default.LedgerRecord);
        Assert.Equal(412, Assert.IsType<TornRecord>(r).Line);
    }

    [Fact]
    public void Settings_SerialiseCamelCaseWithoutALedgerDirKey()
    {
        var s = new Settings(1, @"C:\Users\u\OneDrive\Pictures\UAS Videos", @"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload", [],
            50, 1, StoredClockMode.Zone, "America/New_York", true,
            new MapSettings("streets", "https://tiles.openfreemap.org/styles/liberty", "https://tiles.openfreemap.org/styles/dark",
                "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                ImmutableDictionary<string, string>.Empty.Add("USGS", "https://basemap.nationalmap.gov/arcgis/rest/services/USGSImageryOnly/MapServer/tile/{z}/{y}/{x}")),
            new LayoutSettings(380, 0.45), true);
        var json = JsonSerializer.Serialize(s, CoreJsonContext.Default.Settings);
        Assert.Contains("\"rootsConfirmed\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"droneClockMode\": \"Zone\"", json, StringComparison.Ordinal);
        Assert.Contains("\"USGS\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ledgerDir", json, StringComparison.OrdinalIgnoreCase);
        var back = JsonSerializer.Deserialize(json, CoreJsonContext.Default.Settings)!;
        Assert.Equal(s.VideoRoot, back.VideoRoot);
        Assert.Equal(0.45, back.Layout.MapHeightRatio);
    }

    [Fact]
    public void Draft_WithClosedEdits_RoundTrips()
    {
        var a = new ItemId("DCIM/DJI_001/DJI_20260725232655_0117_D.MP4");
        var b = new ItemId("DCIM/DJI_001/DJI_20260726235645_0001_D.MP4");
        var draft = new Draft(1, "vol-1A2B3C4D", "9f3c0a6d12e4b7a1", new DateTime(2026, 9, 27, 21, 0, 0, DateTimeKind.Utc), new Tuning(25, 1),
            [new Merge(a, b), new SplitBefore(b), new Rename(b, "Anvil Mountain", [b]),
             new Retarget(a, new AppendTo(@"C:\V\2026\2026-07\2026-07-25 Council Road"), false, []), new SetDayIncluded(new DateOnly(2026, 7, 26), false)]);
        var json = JsonSerializer.Serialize(draft, CoreJsonContext.Default.Draft);
        Assert.Contains("\"inA\": \"DCIM/DJI_001/DJI_20260725232655_0117_D.MP4\"", json, StringComparison.Ordinal);
        Assert.Contains("\"t\": \"appendTo\"", json, StringComparison.Ordinal);
        var back = JsonSerializer.Deserialize(json, CoreJsonContext.Default.Draft)!;
        Assert.Equal(json, JsonSerializer.Serialize(back, CoreJsonContext.Default.Draft));
        Assert.IsType<Merge>(back.Edits[0]);
        var retarget = Assert.IsType<Retarget>(back.Edits[3]);
        Assert.Equal(@"C:\V\2026\2026-07\2026-07-25 Council Road", Assert.IsType<AppendTo>(retarget.Choice).FolderFullPath);
        Assert.Equal(b, Assert.Single(Assert.IsType<Rename>(back.Edits[2]).PinnedMembers));
    }

    [Fact]
    public void GeoPoint_IsWrittenAsLonLat()
    {
        var json = JsonSerializer.Serialize(new GeoPoint(64.5627, -165.3696), CoreJsonContext.Default.GeoPoint);
        Assert.Equal("[-165.3696,64.5627]", json);
        Assert.Equal(new GeoPoint(64.5627, -165.3696), JsonSerializer.Deserialize(json, CoreJsonContext.Default.GeoPoint));
    }

    [Fact]
    public void CleanupReport_Serialises()
    {
        var r = new CleanupReport(1, "c4e2", new CardIdentity(0x1A2B3C4D, null, "exFAT", 256_060_514_304),
            new CleanupRequest(CleanupMode.BeforeDate, new DateOnly(2026, 7, 26), null, false),
            new CleanupCutoff(new DateOnly(2026, 7, 26), null, null, null, 0, 0, null),
            [new CleanupReportLine("DCIM/DJI_001/a.MP4", 10, "DCIM/DJI_001/a.MP4", "Deleted", "Evidence", "InLedger", "in the history, verified", null, true)],
            [], null, 12_400_000_000, 12_400_131_072, VerdictLevel.Safe);
        var json = JsonSerializer.Serialize(r, CoreJsonContext.Default.CleanupReport);
        Assert.Contains("\"mode\": \"BeforeDate\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ledgerRecorded\": true", json, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*JsonContextTests"`
Expected: build FAILS (CS0246/CS0103: `LedgerJsonContext`, `CoreJsonContext` not found; the namespace `UasSort.Core.Json` already exists through Part 01's `StackProofJsonContext` and `Namespaces.cs`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Json/Converters.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UasSort.Core.Json;

/// <summary>GeoPoint as [lon,lat] (GeoJSON order, shared with map.js).</summary>
public sealed class GeoPointJsonConverter : JsonConverter<GeoPoint>
{
    public override GeoPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("GeoPoint must be [lon,lat]");
        reader.Read();
        var lon = reader.GetDouble();
        reader.Read();
        var lat = reader.GetDouble();
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("GeoPoint must be [lon,lat]");
        return new GeoPoint(lat, lon);
    }

    public override void Write(Utf8JsonWriter writer, GeoPoint value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Lon);
        writer.WriteNumberValue(value.Lat);
        writer.WriteEndArray();
    }
}

/// <summary>ItemId as its card-relative path string.</summary>
public sealed class ItemIdJsonConverter : JsonConverter<ItemId>
{
    public override ItemId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? throw new JsonException("ItemId must be a string"));

    public override void Write(Utf8JsonWriter writer, ItemId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.CardRelPath);
    }
}
```

```csharp
// src/UasSort.Core/Json/LedgerJsonContext.cs
using System.Text.Json.Serialization;

namespace UasSort.Core.Json;

/// <summary>Ledger lines (Ref §11): one compact JSON object per line, "t" discriminator, camelCase, enums as names.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    WriteIndented = false, AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(LedgerRecord))]
public sealed partial class LedgerJsonContext : JsonSerializerContext
{
}
```

```csharp
// src/UasSort.Core/Json/CoreJsonContext.cs
using System.Text.Json.Serialization;

namespace UasSort.Core.Json;

/// <summary>Settings, drafts, reports and the closed edit types (Ref §3 JSON, §11).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    WriteIndented = true, AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(Draft))]
[JsonSerializable(typeof(PlanEdit))]
[JsonSerializable(typeof(TargetChoice))]
[JsonSerializable(typeof(OffloadReport))]
[JsonSerializable(typeof(CleanupReport))]
[JsonSerializable(typeof(GeoPoint))]
public sealed partial class CoreJsonContext : JsonSerializerContext
{
}
```

In `src/UasSort.Core/Model/Primitives.cs`, add `using System.Text.Json.Serialization;` at the top (`UasSort.Core.Json` comes from `GlobalUsings.Core.cs`) and replace the two declarations:

```csharp
[JsonConverter(typeof(ItemIdJsonConverter))]
public readonly record struct ItemId(string CardRelPath);      // "DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"; a set = "DCIM/PANORAMA/001_0087"

[JsonConverter(typeof(GeoPointJsonConverter))]
public readonly record struct GeoPoint(double Lat, double Lon); // WGS84 degrees; JSON as [lon,lat]
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*JsonContextTests"`
Expected: PASS (8 ledger theory rows + 7 facts).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Json src/UasSort.Core/Model/Primitives.cs tests/UasSort.Core.Tests/Json/JsonContextTests.cs
git commit -m "feat: add source-generated ledger and core JSON contexts

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 02.6: `IoGuardPolicy` — open, create and ledger-exemption rules

**Files:**
- Create: `src/UasSort.Core/Guard/GuardTypes.cs`
- Create: `src/UasSort.Core/Guard/IoGuardPolicy.cs`
- Test: `tests/UasSort.Core.Tests/Guard/IoGuardPolicyTests.cs`

**Interfaces:**

```csharp
// Consumes: PathRules, LedgerPaths (02.1); ConfirmedCleanupPlan (02.4).
// Produces (Ref §4.3 verbatim):
public enum IoOp { ReadData, AppendOwnLedger, CreateNew, CreateDir, SetPinned, SetAttributesOrTimes, Delete, Rename, OpenForFlush, CardDelete }
public sealed record GuardContext(string VideoRoot, string PhotoRoot, ImmutableArray<string> PreviousPhotoRoots, string? CardRoot,
    string AppDataDir, string Machine, IReadOnlySet<string> NewFolderDirs, IReadOnlySet<string> OwnTempsThisRun,
    IReadOnlySet<string> RenamedThisRun, string SystemVolumeRoot, bool CardIsVerifiedCardVolume, ConfirmedCleanupPlan? Cleanup);
public sealed record CardDeleteViolation(string Path, IoOp Op, string Reason);
public sealed record GuardAllow;  public sealed record GuardUnsafe(string Reason);
public sealed record GuardCloudOnly(string Path);  public sealed record GuardHydration(string Path, uint Attributes);
public union GuardDecision(GuardAllow, GuardUnsafe, GuardCloudOnly, GuardHydration);
public static class IoGuardPolicy
{
    const uint FileAttributeReadOnly = 0x1, FileAttributeHidden = 0x2, FileAttributeDirectory = 0x10, FileAttributeArchive = 0x20,
               FileAttributeOffline = 0x1000, FileAttributeNotContentIndexed = 0x2000, FileAttributeRecallOnOpen = 0x40000,
               FileAttributePinned = 0x80000, FileAttributeUnpinned = 0x100000, FileAttributeRecallOnDataAccess = 0x400000;   // (defined here)
    const string TempSuffix = ".uas-sort.tmp";                                                                            // (defined here)
    static GuardDecision Check(IoOp op, string canonicalPath, uint? attributes, GuardContext ctx);
}
```

Rule order implemented (Ref §4.3): 1 attributes/placeholders → 1b `CardDelete` protected roots (Task 02.7) → 2 card root → 3 ledger folder → 4 library roots → 5 app data → 6 refuse. Two decisions this plan makes where the Ref is silent: the placeholder bits of rule 1 are not applied to `SetPinned` (pinning a cloud-only `.uas-sort` folder is its purpose), and `Rename` is checked on the source path (the own temp), the target being "its final name in the same directory".

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Guard/IoGuardPolicyTests.cs
namespace UasSort.Core.Tests.Guard;

public class IoGuardPolicyTests
{
    internal const string V = @"C:\Users\u\OneDrive\Pictures\UAS Videos";
    internal const string P = V + @"\Picture Offload";
    internal const string Prev = @"D:\Old Photos";
    internal const string L = V + @"\.uas-sort";
    internal const string A = @"C:\Users\u\AppData\Local\uas-sort";
    internal const string Z = V + @"\2026\2026-09\2026-09-27 Zachar Bay";
    internal const string Council = V + @"\2026\2026-07\2026-07-25 Council Road";
    internal const string OwnTemp = Z + @"\DJI_20260927140627_0128_D.MP4.uas-sort.tmp";
    internal const string RenamedFile = Z + @"\DJI_20260927140127_0123_D.MP4";
    internal const string LibraryFile = Council + @"\DJI_20260725232655_0117_D.MP4";
    private const long None = -1;

    internal static GuardContext Context(ConfirmedCleanupPlan? cleanup = null, string? cardRoot = @"E:\", bool verified = false) => new(
        V, P, [Prev], cardRoot, A, "DESKTOP-A",
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, V + @"\2026", V + @"\2026\2026-09", Z),
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, OwnTemp),
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, RenamedFile),
        @"C:\", verified, cleanup);

    internal static string Kind(GuardDecision d) => d switch
    {
        GuardAllow => "Allow",
        GuardUnsafe => "Unsafe",
        GuardCloudOnly => "CloudOnly",
        GuardHydration => "Hydration",
    };

    [Theory]
    // ── allowed (Ref §13 IO guard policy)
    [InlineData(IoOp.ReadData, L + @"\ledger-B.jsonl", 0x20, "Allow")]
    [InlineData(IoOp.ReadData, L + @"\ledger-B-DESKTOP-A.jsonl", 0x20, "Allow")]
    [InlineData(IoOp.AppendOwnLedger, L + @"\ledger-DESKTOP-A.jsonl", 0x20, "Allow")]
    [InlineData(IoOp.AppendOwnLedger, L + @"\ledger-DESKTOP-A.jsonl", None, "Allow")]
    [InlineData(IoOp.CreateDir, L, None, "Allow")]
    [InlineData(IoOp.SetPinned, L, 0x10, "Allow")]
    [InlineData(IoOp.SetPinned, L, 0x400010, "Allow")]
    [InlineData(IoOp.CreateNew, Z + @"\X.MP4.uas-sort.tmp", None, "Allow")]
    [InlineData(IoOp.ReadData, OwnTemp, 0x2022, "Allow")]
    [InlineData(IoOp.SetAttributesOrTimes, OwnTemp, 0x2022, "Allow")]
    [InlineData(IoOp.Rename, OwnTemp, 0x2020, "Allow")]
    [InlineData(IoOp.Delete, OwnTemp, 0x2022, "Allow")]
    [InlineData(IoOp.Delete, Council + @"\DJI_20260725232655_0117_D.MP4.uas-sort.tmp", 0x2022, "Allow")]
    [InlineData(IoOp.OpenForFlush, RenamedFile, 0x20, "Allow")]
    [InlineData(IoOp.OpenForFlush, Z, 0x10, "Allow")]
    [InlineData(IoOp.CreateDir, V + @"\2026", None, "Allow")]
    [InlineData(IoOp.CreateDir, V + @"\2026\2026-09", None, "Allow")]
    [InlineData(IoOp.CreateDir, Z, None, "Allow")]
    [InlineData(IoOp.ReadData, @"E:\DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", 0x20, "Allow")]
    [InlineData(IoOp.ReadData, A + @"\settings.json", 0x20, "Allow")]
    [InlineData(IoOp.CreateNew, A + @"\drafts\vol-1A2B3C4D.json", None, "Allow")]
    [InlineData(IoOp.ReadData, V + @"\.UAS-SORT\LEDGER-B.JSONL", 0x20, "Allow")]
    // ── unsafe
    [InlineData(IoOp.AppendOwnLedger, L + @"\ledger-B.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.CreateNew, L + @"\ledger-B.jsonl", None, "Unsafe")]
    [InlineData(IoOp.SetAttributesOrTimes, L + @"\ledger-B.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.ReadData, L + @"\settings.json", 0x20, "Unsafe")]
    [InlineData(IoOp.ReadData, L + @"\sub\ledger-C.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.Delete, L + @"\ledger-DESKTOP-A.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.Rename, L + @"\ledger-B.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.CreateDir, L + @"\sub", None, "Unsafe")]
    [InlineData(IoOp.ReadData, V + @"\ledger-X.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.ReadData, P + @"\ledger-X.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.ReadData, V + @"\.uas-sort2\ledger-A.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.ReadData, LibraryFile, 0x20, "Unsafe")]
    [InlineData(IoOp.ReadData, Prev + @"\DJI_20260725233000_0116_D.DNG", 0x20, "Unsafe")]
    [InlineData(IoOp.CreateNew, Council + @"\DJI_20260726235645_0001_D.MP4", None, "Unsafe")]
    [InlineData(IoOp.CreateDir, Council, 0x10, "Unsafe")]
    [InlineData(IoOp.CreateDir, V + @"\2026\2026-10", None, "Unsafe")]
    [InlineData(IoOp.Rename, RenamedFile, 0x20, "Unsafe")]
    [InlineData(IoOp.Delete, LibraryFile, 0x20, "Unsafe")]
    [InlineData(IoOp.CreateNew, @"E:\DCIM\x.txt", None, "Unsafe")]
    [InlineData(IoOp.SetAttributesOrTimes, @"E:\DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", 0x20, "Unsafe")]
    [InlineData(IoOp.OpenForFlush, @"E:\", 0x10, "Unsafe")]
    [InlineData(IoOp.Delete, @"E:\DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", 0x20, "Unsafe")]
    [InlineData(IoOp.ReadData, Z + @"\missing.MP4", None, "Unsafe")]
    [InlineData(IoOp.ReadData, @"C:\Windows\win.ini", 0x20, "Unsafe")]
    // ── placeholders
    [InlineData(IoOp.ReadData, L + @"\ledger-B.jsonl", 0x400000, "CloudOnly")]
    [InlineData(IoOp.ReadData, LibraryFile, 0x401620, "Hydration")]
    [InlineData(IoOp.ReadData, LibraryFile, 0x40000, "Hydration")]
    [InlineData(IoOp.ReadData, LibraryFile, 0x1000, "Hydration")]
    [InlineData(IoOp.ReadData, L + @"\notes.txt", 0x400000, "Hydration")]
    public void Check_FollowsTheRuleTable(IoOp op, string path, long attributes, string expected)
        => Assert.Equal(expected, Kind(IoGuardPolicy.Check(op, path, attributes < 0 ? null : (uint)attributes, Context())));

    [Fact]
    public void Unsafe_NamesTheOperationAndPath()
    {
        var d = IoGuardPolicy.Check(IoOp.ReadData, LibraryFile, 0x20, Context());
        var reason = d switch { GuardUnsafe u => u.Reason, GuardAllow => "", GuardCloudOnly => "", GuardHydration => "" };
        Assert.Contains("ReadData", reason, StringComparison.Ordinal);
        Assert.Contains(LibraryFile, reason, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutACardRoot_CardPathsAreOutsideEveryRoot()
        => Assert.Equal("Unsafe", Kind(IoGuardPolicy.Check(IoOp.ReadData, @"E:\DCIM\DJI_001\a.MP4", 0x20, Context(cardRoot: null))));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*IoGuardPolicyTests"`
Expected: build FAILS (CS0246: `GuardContext`, `IoGuardPolicy`, `IoOp` not found).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Guard/GuardTypes.cs
namespace UasSort.Core;

public enum IoOp
{
    ReadData, AppendOwnLedger, CreateNew, CreateDir, SetPinned, SetAttributesOrTimes, Delete, Rename, OpenForFlush,
    CardDelete /* Card cleanup only (Ref §10.6): a card file, or an emptied set folder */,
}

public sealed record GuardContext(string VideoRoot, string PhotoRoot, ImmutableArray<string> PreviousPhotoRoots, string? CardRoot,
    string AppDataDir /* %LOCALAPPDATA%\uas-sort */, string Machine,
    IReadOnlySet<string> NewFolderDirs /* the run's set as given (OffloadCompiler.NewFolderDirs: incl. YYYY, YYYY-MM parents); no ancestor expansion */,
    IReadOnlySet<string> OwnTempsThisRun, IReadOnlySet<string> RenamedThisRun,   // all canonical, compared case-insensitively
    string SystemVolumeRoot /* e.g. C:\ */,
    bool CardIsVerifiedCardVolume /* true only in the GuardContext that the eraser factory builds after its volume check */,
    ConfirmedCleanupPlan? Cleanup /* set only in the eraser's own context while CleanupExecutor runs; null everywhere else */);

public sealed record CardDeleteViolation(string Path, IoOp Op, string Reason);   // FakeFileSystem.CardDeleteViolations

public sealed record GuardAllow;
public sealed record GuardUnsafe(string Reason);
public sealed record GuardCloudOnly(string Path);
public sealed record GuardHydration(string Path, uint Attributes);
public union GuardDecision(GuardAllow, GuardUnsafe, GuardCloudOnly, GuardHydration);
```

```csharp
// src/UasSort.Core/Guard/IoGuardPolicy.cs
namespace UasSort.Core;

/// <summary>The one set of IO rules (Ref §4.3). Pure; Platform and FakeFileSystem both call it before every open, create,
/// attribute change, delete or rename. Listings and attribute reads are not checked.</summary>
public static class IoGuardPolicy
{
    public const uint FileAttributeReadOnly = 0x1;
    public const uint FileAttributeHidden = 0x2;
    public const uint FileAttributeDirectory = 0x10;
    public const uint FileAttributeArchive = 0x20;
    public const uint FileAttributeOffline = 0x1000;
    public const uint FileAttributeNotContentIndexed = 0x2000;
    public const uint FileAttributeRecallOnOpen = 0x40000;
    public const uint FileAttributePinned = 0x80000;
    public const uint FileAttributeUnpinned = 0x100000;
    public const uint FileAttributeRecallOnDataAccess = 0x400000;
    public const string TempSuffix = ".uas-sort.tmp";

    private const uint PlaceholderBits = FileAttributeOffline | FileAttributeRecallOnOpen | FileAttributeRecallOnDataAccess;

    public static GuardDecision Check(IoOp op, string canonicalPath, uint? attributes, GuardContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var path = PathRules.Normalize(canonicalPath);
        var ledgerDir = LedgerPaths.For(ctx.VideoRoot);

        // Rule 1: attributes first. Null = the target doesn't exist, accepted only by the creating ops.
        if (attributes is null && op is not (IoOp.CreateNew or IoOp.CreateDir or IoOp.AppendOwnLedger))
            return Unsafe(op, path, "the target doesn't exist or its attributes couldn't be read");
        if (attributes is uint a && (a & PlaceholderBits) != 0 && op != IoOp.SetPinned)
            return IsTopLevelLedgerFile(path, ledgerDir) ? new GuardCloudOnly(path) : new GuardHydration(path, a);

        // Rule 2: the card root. CardDelete has its own rules (Task 02.7).
        if (op == IoOp.CardDelete) return Unsafe(op, path, "card deletes need a confirmed cleanup plan");
        if (ctx.CardRoot is { } card && PathRules.IsSameOrUnder(path, card))
            return op == IoOp.ReadData ? Allow() : Unsafe(op, path, "nothing on the card is ever created, written, renamed or changed");

        // Rule 3: the ledger folder exemption.
        if (PathRules.IsSameOrUnder(path, ledgerDir)) return CheckLedgerFolder(op, path, attributes, ledgerDir, ctx);

        // Rule 4: library roots.
        if (IsUnderLibraryRoot(path, ctx)) return CheckLibrary(op, path, ctx);

        // Rule 5: Platform's own stores.
        if (PathRules.IsSameOrUnder(path, ctx.AppDataDir)) return Allow();

        // Rule 6.
        return Unsafe(op, path, "outside every configured root");
    }

    private static GuardDecision CheckLedgerFolder(IoOp op, string path, uint? attributes, string ledgerDir, GuardContext ctx)
    {
        var parent = PathRules.Parent(path);
        var topLevel = parent is not null && PathRules.Equal(parent, ledgerDir);
        var isDirectory = attributes is uint a && (a & FileAttributeDirectory) != 0;
        var allowed = op switch
        {
            IoOp.ReadData => topLevel && !isDirectory && LedgerPaths.IsLedgerFileName(PathRules.FileName(path)),
            IoOp.AppendOwnLedger => PathRules.Equal(path, LedgerPaths.OwnFile(ctx.VideoRoot, ctx.Machine)),
            IoOp.CreateDir or IoOp.SetPinned => PathRules.Equal(path, ledgerDir),
            _ => false,
        };
        return allowed ? Allow() : Unsafe(op, path, "the ledger folder allows only reading ledger*.jsonl, appending the own file, creating and pinning the folder");
    }

    private static GuardDecision CheckLibrary(IoOp op, string path, GuardContext ctx)
    {
        var isTemp = PathRules.FileName(path).EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase);
        var ownTemp = isTemp && PathRules.SetContains(ctx.OwnTempsThisRun, path);
        var allowed = op switch
        {
            IoOp.CreateNew => isTemp,
            IoOp.ReadData or IoOp.SetAttributesOrTimes or IoOp.Rename => ownTemp,
            IoOp.Delete => isTemp,
            IoOp.OpenForFlush => PathRules.SetContains(ctx.RenamedThisRun, path)
                                 || PathRules.SetContains(ctx.NewFolderDirs, path)
                                 || HoldsRenamedFile(path, ctx),
            IoOp.CreateDir => PathRules.SetContains(ctx.NewFolderDirs, path),
            _ => false,
        };
        return allowed ? Allow() : Unsafe(op, path, "pre-existing library content is never opened, changed or created");
    }

    private static bool HoldsRenamedFile(string dir, GuardContext ctx)
    {
        foreach (var r in ctx.RenamedThisRun)
            if (PathRules.Parent(r) is { } parent && PathRules.Equal(parent, dir)) return true;
        return false;
    }

    private static bool IsUnderLibraryRoot(string path, GuardContext ctx)
    {
        if (PathRules.IsSameOrUnder(path, ctx.VideoRoot) || PathRules.IsSameOrUnder(path, ctx.PhotoRoot)) return true;
        foreach (var p in ctx.PreviousPhotoRoots)
            if (PathRules.IsSameOrUnder(path, p)) return true;
        return false;
    }

    private static bool IsTopLevelLedgerFile(string path, string ledgerDir)
        => PathRules.Parent(path) is { } parent && PathRules.Equal(parent, ledgerDir)
           && LedgerPaths.IsLedgerFileName(PathRules.FileName(path));

    private static GuardDecision Allow() => new GuardAllow();

    private static GuardDecision Unsafe(IoOp op, string path, string why) => new GuardUnsafe($"{op} {path}: {why}");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*IoGuardPolicyTests"`
Expected: PASS (every theory row and both facts).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Guard/GuardTypes.cs src/UasSort.Core/Guard/IoGuardPolicy.cs tests/UasSort.Core.Tests/Guard/IoGuardPolicyTests.cs
git commit -m "feat: add IoGuardPolicy with the ledger exemption and library rules

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 02.7: `IoGuardPolicy` — `CardDelete` rules 1b and 2

**Files:**
- Modify: `src/UasSort.Core/Guard/IoGuardPolicy.cs`
- Test: `tests/UasSort.Core.Tests/Guard/IoGuardCardDeleteTests.cs`

**Interfaces:**

```csharp
// Consumes: IoGuardPolicy, GuardContext (02.6); ConfirmedCleanupPlan (02.4); TestCleanupPlans (02.4, Core.Tests);
//           IoGuardPolicyTests.Context/Kind and path constants (02.6, internal to Core.Tests).
// Produces: IoGuardPolicy.Check now allows IoOp.CardDelete exactly as Ref §4.3 rules 1b and 2 say; signature unchanged.
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Guard/IoGuardCardDeleteTests.cs
using UasSort.Core.Tests.Support;
using static UasSort.Core.Tests.Guard.IoGuardPolicyTests;

namespace UasSort.Core.Tests.Guard;

public class IoGuardCardDeleteTests
{
    private const string Mp4 = @"E:\DCIM\DJI_001\DJI_20260726035000_0001_D.MP4";
    private const string Lrf = @"E:\DCIM\DJI_001\DJI_20260726035000_0001_D.LRF";
    private const string Pano = @"E:\DCIM\PANORAMA\001_0087";

    private static ConfirmedCleanupPlan PlanFor(string cardRoot) => TestCleanupPlans.Confirmed(cardRoot,
        TestCleanupPlans.Candidate("DCIM/DJI_001/DJI_20260726035000_0001_D.MP4",
            ["DCIM/DJI_001/DJI_20260726035000_0001_D.LRF", "DCIM/DJI_001/DJI_20260726035000_0001_D.MP4"]),
        TestCleanupPlans.Candidate("DCIM/PANORAMA/001_0087", ["DCIM/PANORAMA/001_0087/PANO_0001.DNG"], setFolder: "DCIM/PANORAMA/001_0087"));

    private static string Delete(string path, uint? attributes, GuardContext ctx)
        => Kind(IoGuardPolicy.Check(IoOp.CardDelete, path, attributes, ctx));

    private static readonly GuardContext Verified = Context(PlanFor(@"E:\"), @"E:\", verified: true);

    [Fact]
    public void NamedFilesAndSetFolders_AreAllowedOnAVerifiedVolume()
    {
        Assert.Equal("Allow", Delete(Mp4, 0x20, Verified));
        Assert.Equal("Allow", Delete(Lrf, 0x22, Verified));
        Assert.Equal("Allow", Delete(@"e:\dcim\dji_001\dji_20260726035000_0001_d.mp4", 0x20, Verified));
        Assert.Equal("Allow", Delete(@"E:\DCIM\PANORAMA\001_0087\PANO_0001.DNG", 0x20, Verified));
        Assert.Equal("Allow", Delete(Pano, 0x10, Verified));
    }

    [Fact]
    public void NoPlan_UnnamedPath_OrUnverifiedVolume_AreUnsafe()
    {
        Assert.Equal("Unsafe", Delete(Mp4, 0x20, Context(null, @"E:\", verified: true)));
        Assert.Equal("Unsafe", Delete(@"E:\DCIM\DJI_001\DJI_20260726001000_0002_D.MP4", 0x20, Verified));
        Assert.Equal("Unsafe", Delete(Mp4, 0x20, Context(PlanFor(@"E:\"), @"E:\", verified: false)));
        Assert.Equal("Unsafe", Delete(Mp4, null, Verified));
    }

    [Theory]
    [InlineData(@"E:\DCIM")]
    [InlineData(@"E:\DCIM\DJI_001")]
    [InlineData(@"E:\MISC")]
    [InlineData(@"E:\")]
    public void DirectoriesOtherThanNamedSetFolders_AreUnsafe(string dir)
        => Assert.Equal("Unsafe", Delete(dir, 0x10, Verified));

    [Fact]
    public void ANamedFilePath_ThatIsADirectory_IsUnsafe()
        => Assert.Equal("Unsafe", Delete(Mp4, 0x10, Verified));

    [Fact]
    public void APlanForAnotherCardRoot_IsUnsafe()
        => Assert.Equal("Unsafe", Delete(@"F:\DCIM\DJI_001\DJI_20260726035000_0001_D.MP4", 0x20,
            Context(PlanFor(@"E:\"), @"F:\", verified: true)));

    [Theory]
    [InlineData(Council, "DJI_20260725232655_0117_D.MP4")]
    [InlineData(P, "DJI_20260725233000_0116_D.DNG")]
    [InlineData(Prev, "DJI_20260725233000_0116_D.DNG")]
    [InlineData(L, "ledger-DESKTOP-A.jsonl")]
    [InlineData(A, "settings.json")]
    [InlineData(@"C:\Temp\card\DCIM\DJI_001", "DJI_20260726035000_0001_D.MP4")]
    public void Rule1b_ProtectedRootsAreUnsafeEvenWhenTheCardRootAndPlanPointThere(string folder, string name)
    {
        var plan = TestCleanupPlans.Confirmed(folder, TestCleanupPlans.Candidate(name, [name]));
        var ctx = Context(plan, folder, verified: true);
        Assert.Equal("Unsafe", Delete(PathRules.Join(folder, name), 0x20, ctx));
    }

    [Theory]
    [InlineData(IoOp.Delete, Mp4, 0x20)]
    [InlineData(IoOp.OpenForFlush, Mp4, 0x20)]
    [InlineData(IoOp.OpenForFlush, @"E:\", 0x10)]
    [InlineData(IoOp.SetAttributesOrTimes, Mp4, 0x20)]
    [InlineData(IoOp.SetAttributesOrTimes, @"E:\", 0x10)]
    [InlineData(IoOp.CreateNew, @"E:\DCIM\DJI_001\new.MP4", -1)]
    [InlineData(IoOp.CreateDir, @"E:\DCIM\DJI_009", -1)]
    public void OtherWritesUnderTheCard_AreUnsafeWithOrWithoutAPlan(IoOp op, string path, long attributes)
    {
        uint? a = attributes < 0 ? null : (uint)attributes;
        Assert.Equal("Unsafe", Kind(IoGuardPolicy.Check(op, path, a, Verified)));
        Assert.Equal("Unsafe", Kind(IoGuardPolicy.Check(op, path, a, Context())));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*IoGuardCardDeleteTests"`
Expected: FAIL — `NamedFilesAndSetFolders_AreAllowedOnAVerifiedVolume` reports `Assert.Equal() Failure: Expected "Allow", Actual "Unsafe"`; the other tests pass.

- [ ] **Step 3: Implement**

In `src/UasSort.Core/Guard/IoGuardPolicy.cs` replace

```csharp
        // Rule 2: the card root. CardDelete has its own rules (Task 02.7).
        if (op == IoOp.CardDelete) return Unsafe(op, path, "card deletes need a confirmed cleanup plan");
```

with

```csharp
        // Rules 1b and 2 for CardDelete (Card cleanup only, Ref §10.6); then rule 2 for every other op.
        if (op == IoOp.CardDelete) return CheckCardDelete(path, attributes!.Value, ledgerDir, ctx);
```

and add these two methods to the class:

```csharp
    private static GuardDecision CheckCardDelete(string path, uint attributes, string ledgerDir, GuardContext ctx)
    {
        // Rule 1b: never a library, ledger, app-data or system-volume path, whatever else the context says.
        if (ProtectedRootOf(path, ledgerDir, ctx) is { } what)
            return Unsafe(IoOp.CardDelete, path, $"a card delete never touches {what}");

        // Rule 2.
        if (ctx.CardRoot is not { } card || !PathRules.IsSameOrUnder(path, card))
            return Unsafe(IoOp.CardDelete, path, "outside the card root");
        if (ctx.Cleanup is not { } plan)
            return Unsafe(IoOp.CardDelete, path, "no confirmed cleanup plan");
        if (!ctx.CardIsVerifiedCardVolume)
            return Unsafe(IoOp.CardDelete, path, "the card volume wasn't verified from Win32");
        if (!PathRules.Equal(plan.Plan.CardRoot, card))
            return Unsafe(IoOp.CardDelete, path, "the confirmed plan is for another card root");

        var isDirectory = (attributes & FileAttributeDirectory) != 0;
        if (!isDirectory && PathRules.SetContains(plan.FilePaths, path)) return Allow();
        if (isDirectory && PathRules.SetContains(plan.SetFolders, path)) return Allow();
        return Unsafe(IoOp.CardDelete, path, isDirectory ? "not a set folder the confirmed plan names" : "not a file the confirmed plan names");
    }

    private static string? ProtectedRootOf(string path, string ledgerDir, GuardContext ctx)
    {
        if (PathRules.IsSameOrUnder(path, ledgerDir)) return "the ledger folder";
        if (PathRules.IsSameOrUnder(path, ctx.VideoRoot)) return "the video root";
        if (PathRules.IsSameOrUnder(path, ctx.PhotoRoot)) return "the photo root";
        foreach (var p in ctx.PreviousPhotoRoots)
            if (PathRules.IsSameOrUnder(path, p)) return "a previous photo root";
        if (PathRules.IsSameOrUnder(path, ctx.AppDataDir)) return "the app's data folder";
        if (PathRules.IsSameOrUnder(path, ctx.SystemVolumeRoot)) return "the system volume";
        return null;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*IoGuard*"`
Expected: PASS (`IoGuardPolicyTests` and `IoGuardCardDeleteTests`).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Guard/IoGuardPolicy.cs tests/UasSort.Core.Tests/Guard/IoGuardCardDeleteTests.cs
git commit -m "feat: allow CardDelete only for a confirmed plan on a verified card volume

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 02.8: `FakeFileSystem` — tree, lister, guarded opens and the hydration tripwire

**Files:**
- Create: `tests/UasSort.Testing/HydrationViolation.cs`
- Create: `tests/UasSort.Testing/FakeFaults.cs`
- Create: `tests/UasSort.Testing/FakeStreams.cs`
- Create: `tests/UasSort.Testing/FakeFileSystem.cs`
- Create: `tests/UasSort.Testing/FakeLayout.cs`
- Test: `tests/UasSort.Core.Tests/Testing/FakeFileSystemTests.cs`

**Interfaces:**

```csharp
// Consumes: IoGuardPolicy, GuardContext, CardDeleteViolation (02.6); IDirectoryLister, FsEntry, ListingResult, CardIdentity,
//           CardSpace, Settings (02.2–02.4); UnsafeIoException (02.4); PathRules, LedgerPaths (02.1).
// Produces (all in namespace UasSort.Testing; (defined here) unless Ref-named):
public sealed class HydrationViolation : UnsafeIoException { HydrationViolation(string path, uint attributes); string Path; uint Attributes; }  // Ref-named
public sealed class FakeFaults            // every hook later parts inject (paths: full = normalized Windows path; card = '/' relative)
{ HashSet<string> TransientCardReadError; HashSet<string> PersistentCardReadError; Dictionary<string, long> CardVanishesAfterBytes;
  bool CardRemoved { get; set; } Func<int, CardIdentity?>? IdentityOnCall { get; set; } long? DiskFullAfterBytes { get; set; }
  Dictionary<string, int> CorruptVerify; HashSet<string> UnbufferedUnsupported; HashSet<string> TargetAppearsBeforeRename;
  Dictionary<string, long> SizeAfterRename; HashSet<string> AppendFails; HashSet<string> LostRoots;
  Dictionary<string, int> EnumerationErrors; Dictionary<string, int> DeleteErrors; HashSet<string> DeletePending; }
public sealed record FakeGuardCall(IoOp Op, string Path, string Decision);          // Decision: Allow|Unsafe|CloudOnly|Hydration
public sealed class FakeFileSystem : IDirectoryLister
{ const uint ArchiveAttribute = 0x20, DirectoryAttribute = 0x10, CloudOnlyPlaceholder = 0x401620;
  FakeFileSystem(GuardContext context); GuardContext Context { get; set; } FakeFaults Faults { get; } long DestinationFreeBytes { get; set; }
  IReadOnlyList<FakeGuardCall> GuardLog; IReadOnlyList<CardDeleteViolation> CardDeleteViolations; IReadOnlyList<string> HydrationViolations;
  void AssertNoViolations();                                                            // fixtures call it at teardown
  void AddDirectory(string path, uint attributes = DirectoryAttribute);
  void AddFile(string path, byte[] content, DateTime mtimeUtc, uint attributes = ArchiveAttribute);
  void AddFile(string path, long size, DateTime mtimeUtc, uint attributes = ArchiveAttribute);   // deterministic pattern content
  void SetAttributes(string path, uint attributes); uint? GetAttributes(string path); bool Exists(string path);
  FsEntry? Metadata(string path); byte[] PeekContent(string path); void RemoveUnguarded(string path);
  void AddCardVolume(string root, CardIdentity identity, CardSpace space); void SetCardIdentity(string root, CardIdentity identity);
  CardIdentity CardIdentityOf(string root); CardSpace CardSpaceOf(string root);
  string Guard(IoOp op, string path, GuardContext? context = null);                    // the tripwire: IoGuardPolicy.Check + throw + record
  Stream OpenRead(string path, GuardContext? context = null);                           // guarded ReadData
  Stream OpenAppend(string path, GuardContext? context = null);                         // guarded AppendOwnLedger
  void CreateDirectory(string path, GuardContext? context = null);                      // guarded CreateDir
  void SetPinned(string path, GuardContext? context = null);                            // guarded SetPinned
  ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames); }
public static class FakeLayout            // the standard fake PC used by every later fake-FS test
{ const string VideoRoot, PhotoRoot, AppDataDir, CardRoot = @"E:\", Machine = "DESKTOP-A", SystemVolumeRoot = @"C:\";
  static readonly CardIdentity CardId; static readonly CardSpace CardSpace;
  static GuardContext Context(string? cardRoot = CardRoot, IEnumerable<string>? newFolderDirs = null, IEnumerable<string>? previousPhotoRoots = null);
  static Settings Settings(string videoRoot = VideoRoot, string photoRoot = PhotoRoot, IEnumerable<string>? previousPhotoRoots = null);
  static FakeFileSystem NewFileSystem(); }   // roots + app data created, card volume E:\ registered
```

Tripwire semantics: `GuardHydration` and `GuardCloudOnly` throw `HydrationViolation`; a `GuardUnsafe` data open (`ReadData`, `AppendOwnLedger`, `OpenForFlush`, `SetAttributesOrTimes`) of an existing file under a library root throws `HydrationViolation` too ("any open of a pre-existing library file", Ref §4.3); a refused `CardDelete`, or a refused `Delete` under the card root, throws `UnsafeIoException` and is appended to `CardDeleteViolations`; every other refusal throws `UnsafeIoException`. Every violation is also recorded, so a swallowed exception still fails `AssertNoViolations()`.

Ownership (registry Testing table): `FakeFaults`, `FakeFileSystem`, `FakeGuardCall`, `HydrationViolation` and `FakeLayout` are the only fakes of their kind; no later part declares a second fake file system, fault set or layout. Each is one non-partial class declared once, in the file named here: `FakeFaults` in `FakeFaults.cs`, `FakeFileSystem` (with its internal `FakeNode`/`FakeVolume` helpers and the `FakeGuardCall` record) in `FakeFileSystem.cs`. Later parts add members in place in these files: Part 08 Task 08.10 adds the `OnCardDelete` callback (called with the card path) to `FakeFaults` and `Touch(string path, long? size = null, DateTime? mtimeUtc = null)` to `FakeFileSystem` (exact signatures in the registry Testing table).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Testing/FakeFileSystemTests.cs
using System.Text;

namespace UasSort.Core.Tests.Testing;

public class FakeFileSystemTests
{
    private static readonly DateTime T = new(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc);
    private const string V = FakeLayout.VideoRoot;
    private static readonly string L = LedgerPaths.For(V);
    private const string LibraryFile = V + @"\2026\2026-07\2026-07-25 Council Road\DJI_20260725232655_0117_D.MP4";

    private static FakeFileSystem Library()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(LibraryFile, 1_000, T, FakeFileSystem.CloudOnlyPlaceholder);
        fs.AddFile(V + @"\2026\2026-07\2026-07-25 Council Road\DJI_20260725232655_0117_D.LRF", 10, T, 0x20);
        fs.AddFile(L + @"\ledger-A.jsonl", Encoding.UTF8.GetBytes("{}\n"), T);
        fs.AddFile(L + @"\notes.txt", Encoding.UTF8.GetBytes("x"), T);
        fs.AddFile(L + @"\sub\ledger-C.jsonl", Encoding.UTF8.GetBytes("{}\n"), T);
        return fs;
    }

    [Fact]
    public void Lister_ReturnsHiddenAndSystemFiles_AndSkipsExcludedDirectories()
    {
        var fs = Library();
        fs.AddFile(V + @"\2026\2026-07\2026-07-25 Council Road\.DJI_20260727002013_0014_D.MP4.trinf", 5, T, 0x2 | 0x4 | 0x20);
        var listing = fs.Enumerate(V, recurse: true, ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, ".uas-sort"));
        var rel = listing.Entries.Select(e => e.RelPath).ToList();
        Assert.Contains(@"2026\2026-07\2026-07-25 Council Road\.DJI_20260727002013_0014_D.MP4.trinf", rel);
        Assert.Contains(@"2026\2026-07\2026-07-25 Council Road", rel);
        Assert.DoesNotContain(rel, r => r.Contains(".uas-sort", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(listing.Errors);
        Assert.Empty(fs.GuardLog);   // listing opens nothing
    }

    [Fact]
    public void Lister_NonRecursive_ListsOnlyTopLevel()
    {
        var fs = Library();
        var listing = fs.Enumerate(L, recurse: false, ImmutableHashSet<string>.Empty);
        string[] expected = ["ledger-A.jsonl", "notes.txt", "sub"];
        Assert.Equal(expected, listing.Entries.Select(e => e.RelPath).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Lister_RecordsEnumerationErrors_AndDoesNotEnterTheFolder()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(@"E:\DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", 100, T);
        fs.AddFile(@"E:\DCIM\DJI_002\DJI_20260927150000_0200_D.MP4", 100, T);
        fs.Faults.EnumerationErrors[@"E:\DCIM\DJI_002"] = 5;
        var listing = fs.Enumerate(@"E:\", true, ImmutableHashSet<string>.Empty);
        Assert.Equal((@"E:\DCIM\DJI_002", 5), Assert.Single(listing.Errors));
        Assert.Contains(listing.Entries, e => e.RelPath == @"DCIM\DJI_002" && e.IsDirectory);
        Assert.DoesNotContain(listing.Entries, e => e.RelPath.StartsWith(@"DCIM\DJI_002\", StringComparison.Ordinal));
    }

    [Fact]
    public void Lister_ReportsAMissingRootAsError3()
        => Assert.Equal(3, Assert.Single(FakeLayout.NewFileSystem().Enumerate(@"F:\", true, ImmutableHashSet<string>.Empty).Errors).Win32Error);

    [Fact]
    public void OpenRead_OfALocalTopLevelLedgerFile_IsAllowed()
    {
        var fs = Library();
        using var s = fs.OpenRead(L + @"\ledger-A.jsonl");
        using var r = new StreamReader(s);
        Assert.Equal("{}\n", r.ReadToEnd());
        fs.AssertNoViolations();
    }

    [Theory]
    [InlineData(LibraryFile)]                                                                     // placeholder bits
    [InlineData(V + @"\2026\2026-07\2026-07-25 Council Road\DJI_20260725232655_0117_D.LRF")]    // local library file
    [InlineData(V + @"\.uas-sort\notes.txt")]
    [InlineData(V + @"\.uas-sort\sub\ledger-C.jsonl")]
    public void OpenRead_OfAnyOtherLibraryFile_FiresTheHydrationTripwire(string path)
    {
        var fs = Library();
        Assert.Throws<HydrationViolation>(() => fs.OpenRead(path));
        Assert.Equal(PathRules.Normalize(path), Assert.Single(fs.HydrationViolations));
        Assert.Throws<InvalidOperationException>(fs.AssertNoViolations);
    }

    [Fact]
    public void OpenRead_OfACloudOnlyLedgerFile_IsAViolationToo()
    {
        var fs = Library();
        fs.SetAttributes(L + @"\ledger-A.jsonl", 0x400020);
        Assert.Throws<HydrationViolation>(() => fs.OpenRead(L + @"\ledger-A.jsonl"));
        Assert.Equal("CloudOnly", fs.GuardLog[^1].Decision);
    }

    [Fact]
    public void OpenAppend_CreatesAndAppendsTheOwnLedgerFileOnly()
    {
        var fs = Library();
        var own = LedgerPaths.OwnFile(V, FakeLayout.Machine);
        using (var s = fs.OpenAppend(own)) s.Write("{\"a\":1}\n"u8);
        using (var s = fs.OpenAppend(own)) s.Write("{\"b\":2}\n"u8);
        Assert.Equal("{\"a\":1}\n{\"b\":2}\n", Encoding.UTF8.GetString(fs.PeekContent(own)));
        Assert.ThrowsAny<UnsafeIoException>(() => fs.OpenAppend(L + @"\ledger-A.jsonl"));
    }

    [Fact]
    public void OpenAppend_WithTheAppendFailsFault_ThrowsIOExceptionOnWrite()
    {
        var fs = Library();
        var own = LedgerPaths.OwnFile(V, FakeLayout.Machine);
        fs.Faults.AppendFails.Add(own);
        using var s = fs.OpenAppend(own);
        Assert.Throws<IOException>(() => s.Write("{}\n"u8));
    }

    [Fact]
    public void CreateDirectory_And_SetPinned_FollowTheLedgerExemption()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.CreateDirectory(L);
        fs.SetPinned(L);
        Assert.Equal(0x80000u, fs.GetAttributes(L)!.Value & 0x80000u);
        Assert.Throws<UnsafeIoException>(() => fs.CreateDirectory(L + @"\sub"));
        Assert.False(fs.Exists(L + @"\sub"));
    }

    [Fact]
    public void PatternContent_IsDeterministicAndOfTheGivenSize()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(FakeLayout.AppDataDir + @"\x.bin", 3_000_000, T);
        var a = fs.PeekContent(FakeLayout.AppDataDir + @"\x.bin");
        Assert.Equal(3_000_000, a.Length);
        Assert.Equal(a, fs.PeekContent(FakeLayout.AppDataDir + @"\x.bin"));
        Assert.Equal(3_000_000, fs.Metadata(FakeLayout.AppDataDir + @"\x.bin")!.Size);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*FakeFileSystemTests"`
Expected: build FAILS (CS0246: `FakeLayout`, `FakeFileSystem`, `HydrationViolation` not found).

- [ ] **Step 3: Implement**

`tests/UasSort.Testing/GlobalUsings.cs` and `GlobalUsings.Core.cs` already exist (Task 02.1), so these files use Core types and `System.Collections.Immutable` without `using` lines.

```csharp
// tests/UasSort.Testing/HydrationViolation.cs
using System.Globalization;

namespace UasSort.Testing;

/// <summary>The fake file system's hydration tripwire (Ref §4.3): a placeholder, cloud-only ledger file or pre-existing
/// library file was opened.</summary>
public sealed class HydrationViolation : UnsafeIoException
{
    public HydrationViolation() : base("Hydration tripwire") { }
    public HydrationViolation(string message) : base(message) { }
    public HydrationViolation(string message, Exception inner) : base(message, inner) { }

    public HydrationViolation(string path, uint attributes)
        : base(string.Create(CultureInfo.InvariantCulture, $"Hydration tripwire: {path} (attributes 0x{attributes:X})"))
    {
        Path = path;
        Attributes = attributes;
    }

    public string Path { get; } = "";
    public uint Attributes { get; }
}
```

```csharp
// tests/UasSort.Testing/FakeFaults.cs
namespace UasSort.Testing;

/// <summary>Fault hooks. Full paths are normalized Windows paths (PathRules.Normalize); card paths are '/'-relative.
/// All lookups are case-insensitive.</summary>
public sealed class FakeFaults
{
    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    // ── card reader (Part 07 fault injection, Part 08 executor)
    /// <summary>Card paths whose first Read throws IOException once; the re-read succeeds.</summary>
    public HashSet<string> TransientCardReadError { get; } = new(Cmp);
    /// <summary>Card paths whose every Read throws IOException while the card stays present.</summary>
    public HashSet<string> PersistentCardReadError { get; } = new(Cmp);
    /// <summary>Card path → bytes readable before the card disappears (CardRemoved becomes true).</summary>
    public Dictionary<string, long> CardVanishesAfterBytes { get; } = new(Cmp);
    /// <summary>The card is gone: identity, stat, reads and deletes fail with ERROR_NOT_READY (21).</summary>
    public bool CardRemoved { get; set; }
    /// <summary>Called with the 1-based count of CurrentIdentity() calls; a non-null result is returned instead of the volume's identity.</summary>
    public Func<int, CardIdentity?>? IdentityOnCall { get; set; }

    // ── destination (FakeFileOps, Part 07)
    /// <summary>Total bytes all temp writes may take before IOException "disk full" (HResult 0x80070070).</summary>
    public long? DiskFullAfterBytes { get; set; }
    /// <summary>Final path → number of VerifyHash calls that see a flipped bit.</summary>
    public Dictionary<string, int> CorruptVerify { get; } = new(Cmp);
    /// <summary>Final paths whose verify falls back to VerifyMode.Cached.</summary>
    public HashSet<string> UnbufferedUnsupported { get; } = new(Cmp);
    /// <summary>Final paths where another file appears just before RenameNoReplace.</summary>
    public HashSet<string> TargetAppearsBeforeRename { get; } = new(Cmp);
    /// <summary>Final path → the size ConfirmFinal sees after the rename.</summary>
    public Dictionary<string, long> SizeAfterRename { get; } = new(Cmp);
    /// <summary>Full paths whose append stream throws IOException on write (ledger append failure).</summary>
    public HashSet<string> AppendFails { get; } = new(Cmp);
    /// <summary>Roots that vanished: every IFileOps call under one throws DirectoryNotFoundException.</summary>
    public HashSet<string> LostRoots { get; } = new(Cmp);

    // ── lister and eraser
    /// <summary>Directory full path → Win32 error the lister records instead of entering it.</summary>
    public Dictionary<string, int> EnumerationErrors { get; } = new(Cmp);
    /// <summary>Card path → Win32 error DeleteFile returns (19 write-protect, 5 access denied, 32 sharing, 21 not ready).</summary>
    public Dictionary<string, int> DeleteErrors { get; } = new(Cmp);
    /// <summary>Card paths whose delete succeeds but whose entry stays listed (another program holds it open).</summary>
    public HashSet<string> DeletePending { get; } = new(Cmp);

    internal bool IsLost(string fullPath)
    {
        foreach (var root in LostRoots)
            if (PathRules.IsSameOrUnder(fullPath, root)) return true;
        return false;
    }
}
```

```csharp
// tests/UasSort.Testing/FakeStreams.cs
namespace UasSort.Testing;

/// <summary>Read-only deterministic content of a given length (no allocation).</summary>
internal sealed class PatternStream(long length, uint seed) : Stream
{
    private long _position;

    public static byte ByteAt(uint seed, long i) => unchecked((byte)((((ulong)i + seed) * 0x9E3779B97F4A7C15UL) >> 56));

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => _position; set => _position = value; }
    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var n = (int)Math.Max(0, Math.Min(count, length - _position));
        for (var i = 0; i < n; i++) buffer[offset + i] = ByteAt(seed, _position + i);
        _position += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };
        return _position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Write-only stream that appends to a fake node under the file system's lock.</summary>
internal sealed class FakeAppendStream(FakeFileSystem fs, FakeNode node, bool fails) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => node.Size;
    public override long Position { get => node.Size; set => throw new NotSupportedException(); }

    public override void Flush()
    {
        if (fails) throw new IOException("The device is not ready.");
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (fails) throw new IOException("The device is not ready.");
        fs.AppendBytes(node, buffer.AsSpan(offset, count));
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
```

```csharp
// tests/UasSort.Testing/FakeFileSystem.cs
namespace UasSort.Testing;

public sealed record FakeGuardCall(IoOp Op, string Path, string Decision);

internal sealed class FakeNode(string path, bool isDirectory, uint attributes, DateTime mtimeUtc)
{
    public string Path { get; set; } = path;
    public bool IsDirectory { get; } = isDirectory;
    public uint Attributes { get; set; } = attributes;
    public DateTime MtimeUtc { get; set; } = mtimeUtc;
    public DateTime CreationUtc { get; set; } = mtimeUtc;
    public DateTime LastAccessUtc { get; set; } = mtimeUtc;
    public byte[]? Bytes { get; set; }
    public long PatternLength { get; set; }
    public uint PatternSeed { get; set; }
    public bool DeletePending { get; set; }
    public long Size => IsDirectory ? 0 : Bytes?.LongLength ?? PatternLength;

    public Stream OpenContent() => Bytes is { } b ? new MemoryStream(b, writable: false) : new PatternStream(PatternLength, PatternSeed);

    public byte[] Materialize()
    {
        if (Bytes is null)
        {
            var b = new byte[PatternLength];
            for (long i = 0; i < b.LongLength; i++) b[i] = PatternStream.ByteAt(PatternSeed, i);
            Bytes = b;
            PatternLength = 0;
        }
        return Bytes;
    }
}

internal sealed class FakeVolume(CardIdentity identity, CardSpace space)
{
    public CardIdentity Identity { get; set; } = identity;
    public CardSpace Space { get; set; } = space;
}

/// <summary>In-memory file system that asks Core's IoGuardPolicy before every open, create, attribute change, delete or
/// rename, like Platform does (Ref §4.3 "Fake file system"). Thread-safe.</summary>
public sealed class FakeFileSystem : IDirectoryLister
{
    public const uint ArchiveAttribute = IoGuardPolicy.FileAttributeArchive;
    public const uint DirectoryAttribute = IoGuardPolicy.FileAttributeDirectory;
    public const uint CloudOnlyPlaceholder = 0x401620;   // Ref §13 fixture bits

    private readonly Lock _gate = new();
    private readonly Dictionary<string, FakeNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FakeVolume> _volumes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FakeGuardCall> _guardLog = [];
    private readonly List<CardDeleteViolation> _cardDeleteViolations = [];
    private readonly List<string> _hydrationViolations = [];
    private uint _nextSeed = 1;

    public FakeFileSystem(GuardContext context) => Context = context;

    public GuardContext Context { get; set; }
    public FakeFaults Faults { get; } = new();
    public long DestinationFreeBytes { get; set; } = 1_000_000_000_000;

    public IReadOnlyList<FakeGuardCall> GuardLog { get { lock (_gate) { return [.. _guardLog]; } } }
    public IReadOnlyList<CardDeleteViolation> CardDeleteViolations { get { lock (_gate) { return [.. _cardDeleteViolations]; } } }
    public IReadOnlyList<string> HydrationViolations { get { lock (_gate) { return [.. _hydrationViolations]; } } }

    public void AssertNoViolations()
    {
        lock (_gate)
        {
            if (_hydrationViolations.Count > 0 || _cardDeleteViolations.Count > 0)
                throw new InvalidOperationException(
                    $"Tripwire: {_hydrationViolations.Count} hydration violation(s) [{string.Join("; ", _hydrationViolations)}], "
                    + $"{_cardDeleteViolations.Count} card-delete violation(s) [{string.Join("; ", _cardDeleteViolations.Select(v => v.Path))}]");
        }
    }

    // ── setup (unguarded; tests only)
    public void AddDirectory(string path, uint attributes = DirectoryAttribute)
    {
        lock (_gate) { EnsureDirectoryChain(PathRules.Normalize(path), attributes | DirectoryAttribute); }
    }

    public void AddFile(string path, byte[] content, DateTime mtimeUtc, uint attributes = ArchiveAttribute)
    {
        ArgumentNullException.ThrowIfNull(content);
        lock (_gate) { PutFile(PathRules.Normalize(path), attributes, mtimeUtc).Bytes = [.. content]; }
    }

    public void AddFile(string path, long size, DateTime mtimeUtc, uint attributes = ArchiveAttribute)
    {
        lock (_gate)
        {
            var n = PutFile(PathRules.Normalize(path), attributes, mtimeUtc);
            n.PatternLength = size;
            n.PatternSeed = _nextSeed++;
        }
    }

    public void SetAttributes(string path, uint attributes)
    {
        lock (_gate) { Require(path).Attributes = attributes; }
    }

    public uint? GetAttributes(string path)
    {
        lock (_gate) { return Find(path)?.Attributes; }
    }

    public bool Exists(string path)
    {
        lock (_gate) { return Find(path) is not null; }
    }

    public FsEntry? Metadata(string path)
    {
        lock (_gate)
        {
            var n = Find(path);
            return n is null ? null : ToEntry(n, PathRules.Parent(n.Path) ?? n.Path);
        }
    }

    public byte[] PeekContent(string path)
    {
        lock (_gate)
        {
            var n = Require(path);
            return n.Bytes is { } b ? [.. b] : [.. n.Materialize()];
        }
    }

    public void RemoveUnguarded(string path)
    {
        lock (_gate) { RemoveTree(PathRules.Normalize(path)); }
    }

    // ── card volumes
    public void AddCardVolume(string root, CardIdentity identity, CardSpace space)
    {
        lock (_gate)
        {
            var r = PathRules.Normalize(root);
            EnsureDirectoryChain(r, DirectoryAttribute);
            _volumes[r] = new FakeVolume(identity, space);
        }
    }

    public void SetCardIdentity(string root, CardIdentity identity)
    {
        lock (_gate) { Volume(root).Identity = identity; }
    }

    public CardIdentity CardIdentityOf(string root)
    {
        lock (_gate) { return Volume(root).Identity; }
    }

    public CardSpace CardSpaceOf(string root)
    {
        lock (_gate) { return Volume(root).Space; }
    }

    // ── the tripwire
    public string Guard(IoOp op, string path, GuardContext? context = null)
    {
        var ctx = context ?? Context;
        lock (_gate)
        {
            var p = PathRules.Normalize(path);
            var node = Find(p);
            var decision = IoGuardPolicy.Check(op, p, node?.Attributes, ctx);
            var kind = decision switch
            {
                GuardAllow => "Allow",
                GuardUnsafe => "Unsafe",
                GuardCloudOnly => "CloudOnly",
                GuardHydration => "Hydration",
            };
            _guardLog.Add(new FakeGuardCall(op, p, kind));
            if (decision is GuardHydration h)
            {
                _hydrationViolations.Add(p);
                throw new HydrationViolation(h.Path, h.Attributes);
            }
            if (decision is GuardCloudOnly)
            {
                _hydrationViolations.Add(p);
                throw new HydrationViolation(p, node?.Attributes ?? 0);
            }
            if (decision is GuardUnsafe u)
            {
                if (op == IoOp.CardDelete || (op == IoOp.Delete && ctx.CardRoot is { } card && PathRules.IsSameOrUnder(p, card)))
                {
                    _cardDeleteViolations.Add(new CardDeleteViolation(p, op, u.Reason));
                    throw new UnsafeIoException(u.Reason);
                }
                if (node is { IsDirectory: false } && IsDataOpen(op) && IsUnderLibrary(p, ctx))
                {
                    _hydrationViolations.Add(p);
                    throw new HydrationViolation(p, node.Attributes);
                }
                throw new UnsafeIoException(u.Reason);
            }
            return p;
        }
    }

    public Stream OpenRead(string path, GuardContext? context = null)
    {
        var p = Guard(IoOp.ReadData, path, context);
        lock (_gate)
        {
            var n = Find(p) ?? throw new FileNotFoundException("Not found", p);
            if (n.IsDirectory) throw new UnauthorizedAccessException(p);
            return n.OpenContent();
        }
    }

    public Stream OpenAppend(string path, GuardContext? context = null)
    {
        var p = Guard(IoOp.AppendOwnLedger, path, context);
        lock (_gate)
        {
            var n = Find(p);
            if (n is null)
            {
                RequireParentDirectory(p);
                n = PutFile(p, ArchiveAttribute, DateTime.UnixEpoch);
                n.Bytes = [];
            }
            n.Materialize();
            return new FakeAppendStream(this, n, Faults.AppendFails.Contains(p));
        }
    }

    public void CreateDirectory(string path, GuardContext? context = null)
    {
        var p = Guard(IoOp.CreateDir, path, context);
        lock (_gate)
        {
            if (Find(p) is { IsDirectory: true }) return;
            RequireParentDirectory(p);
            _nodes[p] = new FakeNode(p, true, DirectoryAttribute, DateTime.UnixEpoch);
        }
    }

    public void SetPinned(string path, GuardContext? context = null)
    {
        var p = Guard(IoOp.SetPinned, path, context);
        lock (_gate)
        {
            var n = Require(p);
            n.Attributes = (n.Attributes | IoGuardPolicy.FileAttributePinned) & ~IoGuardPolicy.FileAttributeUnpinned;
        }
    }

    // ── IDirectoryLister: listing only, never checks the guard, never opens
    public ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames)
    {
        ArgumentNullException.ThrowIfNull(excludeDirNames);
        lock (_gate)
        {
            var r = PathRules.Normalize(root);
            var entries = ImmutableArray.CreateBuilder<FsEntry>();
            var errors = ImmutableArray.CreateBuilder<(string Path, int Win32Error)>();
            if (Find(r) is not { IsDirectory: true })
            {
                errors.Add((r, 3));   // ERROR_PATH_NOT_FOUND
                return new ListingResult(entries.ToImmutable(), errors.ToImmutable());
            }
            if (Faults.EnumerationErrors.TryGetValue(r, out var rootError))
            {
                errors.Add((r, rootError));
                return new ListingResult(entries.ToImmutable(), errors.ToImmutable());
            }
            var queue = new Queue<string>();
            queue.Enqueue(r);
            while (queue.Count > 0)
            {
                var dir = queue.Dequeue();
                foreach (var child in ChildrenOf(dir))
                {
                    var name = PathRules.FileName(child.Path);
                    if (child.IsDirectory && excludeDirNames.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) continue;
                    entries.Add(ToEntry(child, r));
                    if (!child.IsDirectory || !recurse) continue;
                    if (Faults.EnumerationErrors.TryGetValue(child.Path, out var error)) errors.Add((child.Path, error));
                    else queue.Enqueue(child.Path);
                }
            }
            return new ListingResult(entries.ToImmutable(), errors.ToImmutable());
        }
    }

    // ── internals shared with the other fakes of this assembly
    internal Lock Gate => _gate;

    internal FakeNode? Find(string path) => _nodes.GetValueOrDefault(PathRules.Normalize(path));

    internal FakeNode Require(string path) => Find(path) ?? throw new FileNotFoundException("Not found", PathRules.Normalize(path));

    internal FakeVolume Volume(string root)
        => _volumes.GetValueOrDefault(PathRules.Normalize(root)) ?? throw new InvalidOperationException($"No card volume at {root}");

    internal FakeNode PutFile(string normalizedPath, uint attributes, DateTime mtimeUtc)
    {
        if (PathRules.Parent(normalizedPath) is { } parent) EnsureDirectoryChain(parent, DirectoryAttribute);
        var n = new FakeNode(normalizedPath, false, attributes & ~DirectoryAttribute, mtimeUtc);
        _nodes[normalizedPath] = n;
        return n;
    }

    internal void RequireParentDirectory(string normalizedPath)
    {
        if (PathRules.Parent(normalizedPath) is not { } parent || Find(parent) is not { IsDirectory: true })
            throw new DirectoryNotFoundException($"Parent of {normalizedPath} doesn't exist");
    }

    internal void AppendBytes(FakeNode node, ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            var old = node.Materialize();
            var grown = new byte[old.Length + bytes.Length];
            old.CopyTo(grown, 0);
            bytes.CopyTo(grown.AsSpan(old.Length));
            node.Bytes = grown;
        }
    }

    internal void Move(FakeNode node, string newNormalizedPath)
    {
        _nodes.Remove(node.Path);
        node.Path = newNormalizedPath;
        _nodes[newNormalizedPath] = node;
    }

    internal void RemoveTree(string normalizedPath)
    {
        foreach (var key in _nodes.Keys.Where(k => PathRules.IsSameOrUnder(k, normalizedPath)).ToList()) _nodes.Remove(key);
    }

    internal IReadOnlyList<FakeNode> ChildrenOf(string dir)
        => [.. _nodes.Values.Where(n => PathRules.Parent(n.Path) is { } p && PathRules.Equal(p, dir))
                            .OrderBy(n => n.Path, StringComparer.Ordinal)];

    internal static FsEntry ToEntry(FakeNode n, string root)
        => new(n.Path, PathRules.IsStrictlyUnder(n.Path, root) ? PathRules.RelativeCardPath(n.Path, root).Replace('/', '\\') : PathRules.FileName(n.Path),
               n.IsDirectory, n.Size, n.MtimeUtc, n.CreationUtc, n.LastAccessUtc, n.Attributes);

    private void EnsureDirectoryChain(string normalizedDir, uint attributes)
    {
        if (Find(normalizedDir) is { IsDirectory: true }) return;
        if (PathRules.Parent(normalizedDir) is { } parent) EnsureDirectoryChain(parent, DirectoryAttribute);
        _nodes[normalizedDir] = new FakeNode(normalizedDir, true, attributes, DateTime.UnixEpoch);
    }

    private static bool IsDataOpen(IoOp op)
        => op is IoOp.ReadData or IoOp.AppendOwnLedger or IoOp.OpenForFlush or IoOp.SetAttributesOrTimes;

    private static bool IsUnderLibrary(string p, GuardContext ctx)
        => PathRules.IsSameOrUnder(p, ctx.VideoRoot) || PathRules.IsSameOrUnder(p, ctx.PhotoRoot)
           || ctx.PreviousPhotoRoots.Any(r => PathRules.IsSameOrUnder(p, r));
}
```

```csharp
// tests/UasSort.Testing/FakeLayout.cs
namespace UasSort.Testing;

/// <summary>The standard fake PC every fake-FS test uses (no real user paths are touched).</summary>
public static class FakeLayout
{
    public const string VideoRoot = @"C:\Users\u\OneDrive\Pictures\UAS Videos";
    public const string PhotoRoot = VideoRoot + @"\Picture Offload";
    public const string AppDataDir = @"C:\Users\u\AppData\Local\uas-sort";
    public const string CardRoot = @"E:\";
    public const string Machine = "DESKTOP-A";
    public const string SystemVolumeRoot = @"C:\";
    public static readonly CardIdentity CardId = new(0x1A2B3C4D, null, "exFAT", 256_060_514_304);
    public static readonly CardSpace CardSpace = new(12_400_000_000, 256_060_514_304, 131_072);

    public static GuardContext Context(string? cardRoot = CardRoot, IEnumerable<string>? newFolderDirs = null,
                                       IEnumerable<string>? previousPhotoRoots = null)
        => new(VideoRoot, PhotoRoot, [.. previousPhotoRoots ?? []], cardRoot, AppDataDir, Machine,
               (newFolderDirs ?? []).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
               ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase),
               ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase),
               SystemVolumeRoot, false, null);

    public static Settings Settings(string videoRoot = VideoRoot, string photoRoot = PhotoRoot, IEnumerable<string>? previousPhotoRoots = null)
        => new(1, videoRoot, photoRoot, [.. previousPhotoRoots ?? []], 50, 1, StoredClockMode.Zone, "America/New_York", true,
               new MapSettings("streets", "https://tiles.openfreemap.org/styles/liberty", "https://tiles.openfreemap.org/styles/dark",
                   "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                   ImmutableDictionary<string, string>.Empty),
               new LayoutSettings(380, 0.45), true);

    public static FakeFileSystem NewFileSystem()
    {
        var fs = new FakeFileSystem(Context());
        fs.AddDirectory(VideoRoot);
        fs.AddDirectory(PhotoRoot);
        fs.AddDirectory(AppDataDir);
        fs.AddCardVolume(CardRoot, CardId, CardSpace);
        return fs;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*FakeFileSystemTests"`
Expected: PASS (13 tests).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Testing tests/UasSort.Core.Tests/Testing/FakeFileSystemTests.cs
git commit -m "test: add FakeFileSystem with hydration tripwire and fault hooks

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 02.9: Fake card reader and eraser — card faults and the card-delete tripwire

**Files:**
- Create: `tests/UasSort.Testing/FakeCard.cs`
- Modify: `tests/UasSort.Testing/FakeStreams.cs` (add `FaultyCardStream`)
- Test: `tests/UasSort.Core.Tests/Testing/FakeCardTests.cs`

**Interfaces:**

```csharp
// Consumes: FakeFileSystem, FakeFaults, FakeLayout (02.8); ICardReader, ICardReaderFactory, ICardEraser, ICardEraserFactory,
//           EraseResult, ConfirmedCleanupPlan, CardSource (02.2–02.4); IoGuardPolicy (02.6–02.7); TestCleanupPlans (02.4).
// Produces (namespace UasSort.Testing):
public sealed class FakeCardReaderFactory(FakeFileSystem fs) : ICardReaderFactory { int OpenCount { get; } }
public sealed class FakeCardReader : ICardReader
{ FakeCardReader(FakeFileSystem fs, string root, CardIdentity pinned); CardIdentity Pinned { get; } int IdentityCalls { get; } }
public sealed class FakeCardEraserFactory(FakeFileSystem fs) : ICardEraserFactory
{ bool VolumeVerified { get; set; } = true;   // false = a Win32 volume fact fails (Ref §4.3 "Fake file system")
  int OpenCount { get; } }
public sealed class FakeCardEraser : ICardEraser { IReadOnlyList<string> Deleted { get; } bool IsDisposed { get; } }   // '/' card paths; folders end in '/'
```

Reader behaviour: reads go through `FakeFileSystem.Guard(ReadData, …)` with `Context with { CardRoot = root }`; faults per `FakeFaults` (a transient error fires once per opened stream, before any byte is consumed, so a re-read at the same offset succeeds). Eraser behaviour: the factory refuses (with `UnsafeIoException`) a browsed or write-protected source, `VolumeVerified == false`, or a plan whose card root or identity differs from the source, the pinned identity or the volume; it builds the eraser's own `GuardContext` (`CardIsVerifiedCardVolume = true`, `Cleanup = plan`). `DeleteFile` returns `EraseError(21)` when the card is gone and `EraseError(2)` for a file that no longer exists (no Win32 call happens, so there is nothing to guard); otherwise it calls the guard (a refusal throws and is recorded in `CardDeleteViolations`), then applies `DeleteErrors`, returns `EraseError(5)` for a read-only file, and on success frees the cluster-rounded size on the volume (or leaves a delete-pending entry that stays listed).

Ownership (registry Testing table): `FakeCardReaderFactory`, `FakeCardReader`, `FakeCardEraserFactory` and `FakeCardEraser` are the only card fakes; Part 08 deletes its own `CleanupFakeCard` and uses these. Each is one non-partial class declared once, in `tests/UasSort.Testing/FakeCard.cs`; Part 08 Task 08.10 adds `AllDeleted` (every deleted card path of every eraser; folders end in `/`) and `ICardEraser OpenUnchecked(GuardContext ctx)` to `FakeCardEraserFactory` in place in that file (exact signatures in the registry Testing table).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Testing/FakeCardTests.cs
using UasSort.Core.Tests.Support;

namespace UasSort.Core.Tests.Testing;

public class FakeCardTests
{
    private static readonly DateTime T = new(2026, 7, 26, 7, 51, 30, DateTimeKind.Utc);
    private const string Clip = "DCIM/DJI_001/DJI_20260726035000_0001_D.MP4";
    private const string Proxy = "DCIM/DJI_001/DJI_20260726035000_0001_D.LRF";
    private const string Other = "DCIM/DJI_001/DJI_20260726001000_0002_D.MP4";
    private static readonly CardSource Source = new(@"E:\", FakeLayout.CardId, false, false);

    private static FakeFileSystem Card()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(@"E:\" + Clip, 3_000_000, T);
        fs.AddFile(@"E:\" + Proxy, 1_000, T);
        fs.AddFile(@"E:\" + Other, 1_000, T);
        fs.AddFile(@"E:\DCIM\PANORAMA\001_0087\PANO_0001.DNG", 13_751_808, T);
        fs.AddFile(@"E:\DCIM\PANORAMA\001_0087\PANO_0002.DNG", 12_882_432, T);
        fs.AddFile(@"E:\MISC\FC9113.db", 4_096, T);
        return fs;
    }

    private static ConfirmedCleanupPlan Plan() => TestCleanupPlans.Confirmed(@"E:\",
        TestCleanupPlans.Candidate(Clip, [Proxy, Clip]),
        TestCleanupPlans.Candidate("DCIM/PANORAMA/001_0087",
            ["DCIM/PANORAMA/001_0087/PANO_0001.DNG", "DCIM/PANORAMA/001_0087/PANO_0002.DNG"], setFolder: "DCIM/PANORAMA/001_0087"));

    [Fact]
    public void Reader_ReadsCardFilesThroughTheGuard()
    {
        var fs = Card();
        var reader = new FakeCardReaderFactory(fs).Open(Source, FakeLayout.CardId);
        using var s = reader.OpenSequential(Clip);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        Assert.Equal(fs.PeekContent(@"E:\" + Clip), ms.ToArray());
        Assert.Equal(new FakeGuardCall(IoOp.ReadData, @"E:\" + Clip.Replace('/', '\\'), "Allow"), fs.GuardLog[^1]);
        Assert.Equal(3_000_000, reader.Stat(Clip).Size);
        Assert.Throws<FileNotFoundException>(() => reader.Stat("DCIM/DJI_001/missing.MP4"));
    }

    [Fact]
    public void Reader_TransientErrorFiresOnceThenTheReReadSucceeds()
    {
        var fs = Card();
        fs.Faults.TransientCardReadError.Add(Clip);
        using var s = new FakeCardReaderFactory(fs).Open(Source, FakeLayout.CardId).OpenSequential(Clip);
        var buffer = new byte[1 << 20];
        Assert.Throws<IOException>(() => s.Read(buffer, 0, buffer.Length));
        Assert.Equal(0, s.Position);
        Assert.Equal(buffer.Length, s.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public void Reader_CardVanishingMidFile_RemovesTheCard()
    {
        var fs = Card();
        fs.Faults.CardVanishesAfterBytes[Clip] = 1_000_000;
        var reader = new FakeCardReaderFactory(fs).Open(Source, FakeLayout.CardId);
        using var s = reader.OpenSequential(Clip);
        Assert.Throws<IOException>(() => s.CopyTo(Stream.Null));
        Assert.True(fs.Faults.CardRemoved);
        Assert.Throws<IOException>(reader.CurrentIdentity);
        Assert.Equal(21, Assert.Single(reader.Relist().Errors).Win32Error);
    }

    [Fact]
    public void Reader_IdentityOnCall_SwapsTheCardMidRun()
    {
        var fs = Card();
        var other = FakeLayout.CardId with { VolumeSerial = 0x0BADF00D };
        fs.Faults.IdentityOnCall = n => n >= 3 ? other : null;
        var reader = new FakeCardReaderFactory(fs).Open(Source, FakeLayout.CardId);
        Assert.Equal(FakeLayout.CardId, reader.CurrentIdentity());
        Assert.Equal(FakeLayout.CardId, reader.CurrentIdentity());
        Assert.Equal(other, reader.CurrentIdentity());
    }

    [Fact]
    public void Reader_Relist_ListsTheWholeCard()
    {
        var listing = new FakeCardReaderFactory(Card()).Open(Source, FakeLayout.CardId).Relist();
        Assert.Contains(listing.Entries, e => e.RelPath == @"MISC\FC9113.db");
        Assert.Equal(6, listing.Entries.Count(e => !e.IsDirectory));
    }

    [Fact]
    public void Eraser_DeletesExactlyTheNamedFiles_AndFreesClusterRoundedSpace()
    {
        var fs = Card();
        using var eraser = new FakeCardEraserFactory(fs).Open(Source, FakeLayout.CardId, Plan());
        Assert.IsType<EraseOk>(Unwrap(eraser.DeleteFile(Proxy)));
        Assert.IsType<EraseOk>(Unwrap(eraser.DeleteFile(Clip)));
        Assert.False(fs.Exists(@"E:\" + Clip));
        Assert.True(fs.Exists(@"E:\" + Other));
        // 1,000 B → 1 cluster; 3,000,000 B → 23 clusters of 131,072 B
        Assert.Equal(12_400_000_000 + 131_072 + 23 * 131_072, fs.CardSpaceOf(@"E:\").FreeBytes);
        fs.AssertNoViolations();
    }

    [Fact]
    public void Eraser_AnUnnamedFile_ThrowsAndRecordsACardDeleteViolation()
    {
        var fs = Card();
        using var eraser = new FakeCardEraserFactory(fs).Open(Source, FakeLayout.CardId, Plan());
        Assert.Throws<UnsafeIoException>(() => eraser.DeleteFile(Other));
        Assert.True(fs.Exists(@"E:\" + Other));
        Assert.Equal(IoOp.CardDelete, Assert.Single(fs.CardDeleteViolations).Op);
        Assert.Throws<InvalidOperationException>(fs.AssertNoViolations);
    }

    [Fact]
    public void Eraser_RemovesASetFolderOnlyOnceEmpty()
    {
        var fs = Card();
        using var eraser = new FakeCardEraserFactory(fs).Open(Source, FakeLayout.CardId, Plan());
        Assert.Equal(145, Assert.IsType<EraseError>(Unwrap(eraser.RemoveEmptySetFolder("DCIM/PANORAMA/001_0087"))).Win32Error);
        eraser.DeleteFile("DCIM/PANORAMA/001_0087/PANO_0001.DNG");
        eraser.DeleteFile("DCIM/PANORAMA/001_0087/PANO_0002.DNG");
        Assert.IsType<EraseOk>(Unwrap(eraser.RemoveEmptySetFolder("DCIM/PANORAMA/001_0087")));
        Assert.False(fs.Exists(@"E:\DCIM\PANORAMA\001_0087"));
        Assert.True(fs.Exists(@"E:\DCIM\PANORAMA"));
        Assert.Throws<UnsafeIoException>(() => eraser.RemoveEmptySetFolder("DCIM/PANORAMA"));
    }

    [Fact]
    public void Eraser_InjectedErrors_AndDeletePending()
    {
        var fs = Card();
        fs.Faults.DeleteErrors[Proxy] = 19;
        fs.Faults.DeletePending.Add(Clip);
        using var eraser = new FakeCardEraserFactory(fs).Open(Source, FakeLayout.CardId, Plan());
        Assert.Equal(19, Assert.IsType<EraseError>(Unwrap(eraser.DeleteFile(Proxy))).Win32Error);
        Assert.True(fs.Exists(@"E:\" + Proxy));
        Assert.IsType<EraseOk>(Unwrap(eraser.DeleteFile(Clip)));
        var listing = new FakeCardReaderFactory(fs).Open(Source, FakeLayout.CardId).Relist();
        Assert.Contains(listing.Entries, e => e.RelPath == Clip.Replace('/', '\\'));
    }

    [Fact]
    public void Eraser_ReadOnlyFile_FailsWithAccessDenied()
    {
        var fs = Card();
        fs.SetAttributes(@"E:\" + Proxy, 0x21);
        using var eraser = new FakeCardEraserFactory(fs).Open(Source, FakeLayout.CardId, Plan());
        Assert.Equal(5, Assert.IsType<EraseError>(Unwrap(eraser.DeleteFile(Proxy))).Win32Error);
        Assert.True(fs.Exists(@"E:\" + Proxy));
    }

    [Fact]
    public void Factory_RefusesBrowsedWriteProtectedUnverifiedAndMismatchedSources()
    {
        var fs = Card();
        var factory = new FakeCardEraserFactory(fs);
        Assert.Throws<UnsafeIoException>(() => factory.Open(Source with { IsBrowsedFolder = true }, FakeLayout.CardId, Plan()));
        Assert.Throws<UnsafeIoException>(() => factory.Open(Source with { IsWriteProtected = true }, FakeLayout.CardId, Plan()));
        Assert.Throws<UnsafeIoException>(() => factory.Open(Source, FakeLayout.CardId with { VolumeSerial = 1 }, Plan()));
        fs.SetCardIdentity(@"E:\", FakeLayout.CardId with { VolumeSerial = 2 });
        Assert.Throws<UnsafeIoException>(() => factory.Open(Source, FakeLayout.CardId, Plan()));
        fs.SetCardIdentity(@"E:\", FakeLayout.CardId);
        factory.VolumeVerified = false;
        Assert.Throws<UnsafeIoException>(() => factory.Open(Source, FakeLayout.CardId, Plan()));
        Assert.Equal(0, factory.OpenCount);
    }

    [Fact]
    public void Eraser_AfterDispose_Throws()
    {
        var eraser = new FakeCardEraserFactory(Card()).Open(Source, FakeLayout.CardId, Plan());
        eraser.Dispose();
        Assert.Throws<ObjectDisposedException>(() => eraser.DeleteFile(Clip));
    }

    private static object Unwrap(EraseResult r) => r switch { EraseOk ok => ok, EraseError e => e };
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*FakeCardTests"`
Expected: build FAILS (CS0246: `FakeCardReaderFactory`, `FakeCardEraserFactory` not found).

- [ ] **Step 3: Implement**

Append to `tests/UasSort.Testing/FakeStreams.cs`:

```csharp
/// <summary>Card read stream with the FakeFaults card hooks.</summary>
internal sealed class FaultyCardStream(Stream inner, FakeFaults faults, string cardRelPath) : Stream
{
    private bool _transientFired;

    public override bool CanRead => true;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (faults.CardRemoved) throw FakeCardReader.NotReady();
        if (faults.PersistentCardReadError.Contains(cardRelPath))
            throw new IOException("Data error (cyclic redundancy check).", unchecked((int)0x80070017));
        if (!_transientFired && faults.TransientCardReadError.Contains(cardRelPath))
        {
            _transientFired = true;
            throw new IOException("The request could not be performed because of an I/O device error.", unchecked((int)0x8007045D));
        }
        if (faults.CardVanishesAfterBytes.TryGetValue(cardRelPath, out var limit))
        {
            if (inner.Position >= limit)
            {
                faults.CardRemoved = true;
                throw FakeCardReader.NotReady();
            }
            count = (int)Math.Min(count, limit - inner.Position);
        }
        return inner.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
```

```csharp
// tests/UasSort.Testing/FakeCard.cs
namespace UasSort.Testing;

public sealed class FakeCardReaderFactory(FakeFileSystem fs) : ICardReaderFactory
{
    public int OpenCount { get; private set; }

    public ICardReader Open(CardSource source, CardIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(source);
        OpenCount++;
        return new FakeCardReader(fs, source.Root, identity);
    }
}

public sealed class FakeCardReader : ICardReader
{
    private readonly FakeFileSystem _fs;
    private readonly string _root;
    private int _identityCalls;

    public FakeCardReader(FakeFileSystem fs, string root, CardIdentity pinned)
    {
        _fs = fs;
        _root = PathRules.Normalize(root);
        Pinned = pinned;
    }

    public CardIdentity Pinned { get; }
    public int IdentityCalls => Volatile.Read(ref _identityCalls);

    internal static IOException NotReady() => new("The device is not ready.", unchecked((int)0x80070015));

    public CardIdentity CurrentIdentity()
    {
        if (_fs.Faults.CardRemoved) throw NotReady();
        var n = Interlocked.Increment(ref _identityCalls);
        return _fs.Faults.IdentityOnCall?.Invoke(n) ?? _fs.CardIdentityOf(_root);
    }

    public Stream OpenRandom(string cardRelPath) => Open(cardRelPath);

    public Stream OpenSequential(string cardRelPath) => Open(cardRelPath);

    public FsEntry Stat(string cardRelPath)
    {
        if (_fs.Faults.CardRemoved) throw NotReady();
        var full = PathRules.Join(_root, cardRelPath);
        return _fs.Metadata(full) is { IsDirectory: false } e
            ? e with { RelPath = cardRelPath.Replace('/', '\\') }
            : throw new FileNotFoundException("Not found", full);
    }

    public ListingResult Relist()
        => _fs.Faults.CardRemoved
            ? new ListingResult([], [(_root, 21)])
            : _fs.Enumerate(_root, recurse: true, ImmutableHashSet<string>.Empty);

    public CardSpace Space()
    {
        if (_fs.Faults.CardRemoved) throw NotReady();
        return _fs.CardSpaceOf(_root);
    }

    private Stream Open(string cardRelPath)
    {
        if (_fs.Faults.CardRemoved) throw NotReady();
        var key = cardRelPath.Replace('\\', '/');
        var inner = _fs.OpenRead(PathRules.Join(_root, key), _fs.Context with { CardRoot = _root });
        return new FaultyCardStream(inner, _fs.Faults, key);
    }
}

public sealed class FakeCardEraserFactory(FakeFileSystem fs) : ICardEraserFactory
{
    /// <summary>False = one of the Win32 cleanup volume facts fails (not a volume root, wrong bus, no MISC index, …).</summary>
    public bool VolumeVerified { get; set; } = true;
    public int OpenCount { get; private set; }

    public ICardEraser Open(CardSource source, CardIdentity pinned, ConfirmedCleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);
        if (source.IsBrowsedFolder) throw new UnsafeIoException("Cleanup works only on a detected card volume");
        if (source.IsWriteProtected) throw new UnsafeIoException("The card is write-protected");
        if (!VolumeVerified) throw new UnsafeIoException("The volume failed the cleanup volume check");
        if (fs.Faults.CardRemoved) throw new UnsafeIoException("The card volume is gone");
        if (!PathRules.Equal(plan.Plan.CardRoot, source.Root) || plan.Plan.Card != pinned || fs.CardIdentityOf(source.Root) != pinned)
            throw new UnsafeIoException("The confirmed plan is for another card");
        OpenCount++;
        var root = PathRules.Normalize(source.Root);
        var ctx = fs.Context with { CardRoot = root, CardIsVerifiedCardVolume = true, Cleanup = plan };
        return new FakeCardEraser(fs, root, ctx);
    }
}

public sealed class FakeCardEraser : ICardEraser
{
    private readonly FakeFileSystem _fs;
    private readonly string _root;
    private readonly GuardContext _ctx;
    private readonly List<string> _deleted = [];

    internal FakeCardEraser(FakeFileSystem fs, string root, GuardContext ctx)
    {
        _fs = fs;
        _root = root;
        _ctx = ctx;
    }

    public IReadOnlyList<string> Deleted => [.. _deleted];
    public bool IsDisposed { get; private set; }

    public EraseResult DeleteFile(string cardRelPath)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var key = cardRelPath.Replace('\\', '/');
        var full = PathRules.Join(_root, key);
        if (_fs.Faults.CardRemoved) return new EraseError(21, "The device is not ready.");
        if (_fs.Metadata(full) is not { IsDirectory: false }) return new EraseError(2, "The system cannot find the file specified.");
        _fs.Guard(IoOp.CardDelete, full, _ctx);   // throws UnsafeIoException + records a CardDeleteViolation on refusal
        lock (_fs.Gate)
        {
            if (_fs.Faults.DeleteErrors.TryGetValue(key, out var code)) return new EraseError(code, $"Win32 error {code}");
            var node = _fs.Require(full);
            if (node.DeletePending) return new EraseError(2, "The system cannot find the file specified.");
            if ((node.Attributes & IoGuardPolicy.FileAttributeReadOnly) != 0) return new EraseError(5, "Access is denied.");
            var volume = _fs.Volume(_root);
            var cluster = volume.Space.ClusterBytes;
            var allocated = node.Size == 0 ? 0 : (node.Size + cluster - 1) / cluster * cluster;
            if (_fs.Faults.DeletePending.Contains(key)) node.DeletePending = true;
            else _fs.RemoveTree(full);
            volume.Space = volume.Space with { FreeBytes = volume.Space.FreeBytes + allocated };
            _deleted.Add(key);
            return new EraseOk();
        }
    }

    public EraseResult RemoveEmptySetFolder(string cardRelDir)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var key = cardRelDir.Replace('\\', '/').TrimEnd('/');
        var full = PathRules.Join(_root, key);
        if (_fs.Faults.CardRemoved) return new EraseError(21, "The device is not ready.");
        if (_fs.Metadata(full) is not { IsDirectory: true }) return new EraseError(3, "The system cannot find the path specified.");
        _fs.Guard(IoOp.CardDelete, full, _ctx);
        lock (_fs.Gate)
        {
            if (_fs.ChildrenOf(full).Count > 0) return new EraseError(145, "The directory is not empty.");
            _fs.RemoveTree(full);
            _deleted.Add(key + "/");
            return new EraseOk();
        }
    }

    public void Dispose()
    {
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*FakeCardTests"`
Expected: PASS (12 tests).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Testing/FakeCard.cs tests/UasSort.Testing/FakeStreams.cs tests/UasSort.Core.Tests/Testing/FakeCardTests.cs
git commit -m "test: add fake card reader and eraser with card-delete tripwire

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 02.10: `FakeFileOps` — guarded destination writes with fault hooks

**Files:**
- Create: `tests/UasSort.Testing/FakeFileOps.cs`
- Test: `tests/UasSort.Core.Tests/Testing/FakeFileOpsTests.cs`

**Interfaces:**

```csharp
// Consumes: FakeFileSystem, FakeFaults, FakeLayout (02.8); IFileOps, VerifyResult, RenameResult (02.4); IoGuardPolicy (02.6).
// Produces (namespace UasSort.Testing):
public sealed class FakeFileOps : IFileOps     // the fake twin of Platform's GuardedFileOps
{ FakeFileOps(FakeFileSystem fs);
  IReadOnlySet<string> OwnTemps { get; } IReadOnlySet<string> Renamed { get; }       // this run's tracked exceptions
  IReadOnlyList<string> Flushed { get; } IReadOnlyList<string> CreatedDirectories { get; } int FlushToDiskCount { get; } }
```

Behaviour: the guard context of every call is `fs.Context with { OwnTempsThisRun = OwnTemps, RenamedThisRun = Renamed }` (the base context supplies `NewFolderDirs`); temps are the final name plus `.uas-sort.tmp`, Hidden | NotContentIndexed | Archive; `EnsureDirectory` guards `CreateDir` for every missing level; `DeleteOwnTemp` of a temp that no longer exists does nothing; `LostRoots` makes every call under that root throw `DirectoryNotFoundException`.

Ownership (registry Testing table): `FakeFileOps` is the only fake `IFileOps`; Part 07 deletes its own `UasSort.Testing.Offload.FakeFileOps`. It is one non-partial class declared once, in `tests/UasSort.Testing/FakeFileOps.cs` (with its internal `FakeWriteStream`); Part 07 Task 07.4 adds in place in that file a second constructor taking `(FakeFileSystem fs, … newFolderDirs)`, and the members `Calls`, `FlushedDestinations`, `OnTempWrite`, `ThrowOnFinalize` and `FreeBytesOverride` (exact signatures in the registry Testing table), keeping this task's tests green.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Testing/FakeFileOpsTests.cs
using System.IO.Hashing;

namespace UasSort.Core.Tests.Testing;

public class FakeFileOpsTests
{
    private const string V = FakeLayout.VideoRoot;
    private const string Z = V + @"\2026\2026-09\2026-09-27 Zachar Bay";
    private const string Council = V + @"\2026\2026-07\2026-07-25 Council Road";
    private const string Final = Z + @"\DJI_20260927140627_0128_D.MP4";
    private static readonly byte[] Data = [1, 2, 3, 4, 5];
    private static readonly DateTime T = new(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc);

    private static (FakeFileSystem Fs, FakeFileOps Ops) Setup()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.Context = FakeLayout.Context(newFolderDirs: [V + @"\2026", V + @"\2026\2026-09", Z]);
        fs.AddFile(Council + @"\DJI_20260725232655_0117_D.MP4", 1_000, T, FakeFileSystem.CloudOnlyPlaceholder);
        return (fs, new FakeFileOps(fs));
    }

    private static string WriteTemp(FakeFileOps ops, string final)
    {
        using var s = ops.CreateTemp(final, Data.Length, out var temp);
        s.Write(Data);
        ops.FlushToDisk(s);
        return temp;
    }

    [Fact]
    public void HappyPath_CreatesVerifiesFinalisesAndRenames()
    {
        var (fs, ops) = Setup();
        ops.EnsureDirectory(Z, allowCreate: true);
        string[] created = [V + @"\2026", V + @"\2026\2026-09", Z];
        Assert.Equal(created, ops.CreatedDirectories);
        var temp = WriteTemp(ops, Final);
        Assert.Equal(Final + ".uas-sort.tmp", temp);
        Assert.Equal(0x2022u, fs.GetAttributes(temp));
        Assert.IsType<HashMatch>(Unwrap(ops.VerifyHash(temp, Data.Length, XxHash128.HashToUInt128(Data), CancellationToken.None)));
        ops.FinalizeAttributes(temp, T, T);
        Assert.Equal(0u, fs.GetAttributes(temp)!.Value & 0x2u);
        Assert.IsType<Renamed>(Unwrap(ops.RenameNoReplace(temp, Final)));
        Assert.True(ops.ConfirmFinal(Final, Data.Length));
        Assert.Equal(Data, fs.PeekContent(Final));
        Assert.Equal(T, fs.Metadata(Final)!.MtimeUtc);
        ops.FlushDestination(Z, [Final]);
        string[] flushed = [Final, Z];
        Assert.Equal(flushed, ops.Flushed);
        Assert.Equal(1, ops.FlushToDiskCount);
        fs.AssertNoViolations();
    }

    [Fact]
    public void EnsureDirectory_NeverCreatesAnAppendTargetOrANonNewFolderPath()
    {
        var (fs, ops) = Setup();
        Assert.Throws<DirectoryNotFoundException>(() => ops.EnsureDirectory(V + @"\2026\2026-08\2026-08-01 Gone", allowCreate: false));
        Assert.Throws<UnsafeIoException>(() => ops.EnsureDirectory(V + @"\2026\2026-10\2026-10-04 Nome Roads", allowCreate: true));
        Assert.False(fs.Exists(V + @"\2026\2026-10"));
        ops.EnsureDirectory(Council, allowCreate: false);   // exists: no-op
    }

    [Fact]
    public void CorruptVerify_MismatchesTheGivenNumberOfTimes()
    {
        var (fs, ops) = Setup();
        ops.EnsureDirectory(Z, true);
        fs.Faults.CorruptVerify[Final] = 1;
        var temp = WriteTemp(ops, Final);
        var expected = XxHash128.HashToUInt128(Data);
        Assert.IsType<HashMismatch>(Unwrap(ops.VerifyHash(temp, Data.Length, expected, CancellationToken.None)));
        Assert.IsType<HashMatch>(Unwrap(ops.VerifyHash(temp, Data.Length, expected, CancellationToken.None)));
    }

    [Fact]
    public void UnbufferedUnsupported_VerifiesInCachedMode()
    {
        var (fs, ops) = Setup();
        ops.EnsureDirectory(Z, true);
        fs.Faults.UnbufferedUnsupported.Add(Final);
        var temp = WriteTemp(ops, Final);
        var r = ops.VerifyHash(temp, Data.Length, XxHash128.HashToUInt128(Data), CancellationToken.None);
        Assert.Equal(VerifyMode.Cached, Assert.IsType<HashMatch>(Unwrap(r)).Mode);
    }

    [Fact]
    public void TargetAppearingBeforeRename_IsNeverOverwritten()
    {
        var (fs, ops) = Setup();
        ops.EnsureDirectory(Z, true);
        fs.Faults.TargetAppearsBeforeRename.Add(Final);
        var temp = WriteTemp(ops, Final);
        Assert.IsType<TargetExists>(Unwrap(ops.RenameNoReplace(temp, Final)));
        byte[] marker = [0x42];
        Assert.Equal(marker, fs.PeekContent(Final));
        ops.DeleteOwnTemp(temp);
        Assert.False(fs.Exists(temp));
    }

    [Fact]
    public void SizeAfterRename_FailsConfirm()
    {
        var (fs, ops) = Setup();
        ops.EnsureDirectory(Z, true);
        fs.Faults.SizeAfterRename[Final] = 4;
        var temp = WriteTemp(ops, Final);
        ops.RenameNoReplace(temp, Final);
        Assert.False(ops.ConfirmFinal(Final, Data.Length));
    }

    [Fact]
    public void DiskFull_ThrowsWhileWriting()
    {
        var (fs, ops) = Setup();
        ops.EnsureDirectory(Z, true);
        fs.Faults.DiskFullAfterBytes = 3;
        using var s = ops.CreateTemp(Final, Data.Length, out _);
        var e = Assert.Throws<IOException>(() => s.Write(Data));
        Assert.Equal(unchecked((int)0x80070070), e.HResult);
    }

    [Fact]
    public void DeleteOwnTemp_AcceptsOnlyTempNames_AndDeletesStaleTemps()
    {
        var (fs, ops) = Setup();
        Assert.Throws<UnsafeIoException>(() => ops.DeleteOwnTemp(Council + @"\DJI_20260725232655_0117_D.MP4"));
        fs.AddFile(Council + @"\DJI_20260725232655_0117_D.MP4.uas-sort.tmp", 10, T, 0x2022);
        ops.DeleteOwnTemp(Council + @"\DJI_20260725232655_0117_D.MP4.uas-sort.tmp");
        Assert.False(fs.Exists(Council + @"\DJI_20260725232655_0117_D.MP4.uas-sort.tmp"));
        Assert.True(fs.Exists(Council + @"\DJI_20260725232655_0117_D.MP4"));
        fs.AssertNoViolations();
    }

    [Fact]
    public void CreateTemp_RefusesAnExistingTemp_AndALostRoot()
    {
        var (fs, ops) = Setup();
        ops.EnsureDirectory(Z, true);
        WriteTemp(ops, Final);
        Assert.Throws<IOException>(() => ops.CreateTemp(Final, 5, out _));
        fs.Faults.LostRoots.Add(V);
        Assert.Throws<DirectoryNotFoundException>(() => ops.CreateTemp(Z + @"\other.MP4", 5, out _));
    }

    [Fact]
    public void TryGetSize_And_FreeBytes_AreMetadataOnly()
    {
        var (fs, ops) = Setup();
        Assert.True(ops.TryGetSize(Council + @"\DJI_20260725232655_0117_D.MP4", out var size));
        Assert.Equal(1_000, size);
        Assert.False(ops.TryGetSize(Z + @"\missing.MP4", out _));
        Assert.Equal(fs.DestinationFreeBytes, ops.FreeBytes(Z));
        fs.AssertNoViolations();
    }

    private static object Unwrap(VerifyResult r) => r switch { HashMatch m => m, HashMismatch x => x };
    private static object Unwrap(RenameResult r) => r switch { Renamed x => x, TargetExists t => t };
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*FakeFileOpsTests"`
Expected: build FAILS (CS0246: `FakeFileOps` not found).

- [ ] **Step 3: Implement**

```csharp
// tests/UasSort.Testing/FakeFileOps.cs
using System.IO.Hashing;

namespace UasSort.Testing;

public sealed class FakeFileOps(FakeFileSystem fs) : IFileOps
{
    private const uint TempAttributes = IoGuardPolicy.FileAttributeHidden | IoGuardPolicy.FileAttributeNotContentIndexed
                                        | IoGuardPolicy.FileAttributeArchive;
    private readonly HashSet<string> _ownTemps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _renamed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _flushed = [];
    private readonly List<string> _createdDirectories = [];
    private long _bytesWritten;

    public IReadOnlySet<string> OwnTemps { get { lock (fs.Gate) { return _ownTemps.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase); } } }
    public IReadOnlySet<string> Renamed { get { lock (fs.Gate) { return _renamed.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase); } } }
    public IReadOnlyList<string> Flushed { get { lock (fs.Gate) { return [.. _flushed]; } } }
    public IReadOnlyList<string> CreatedDirectories { get { lock (fs.Gate) { return [.. _createdDirectories]; } } }
    public int FlushToDiskCount { get; private set; }

    private GuardContext Ctx => fs.Context with { OwnTempsThisRun = OwnTemps, RenamedThisRun = Renamed };

    public Stream CreateTemp(string finalPath, long size, out string tempPath)
    {
        var final = PathRules.Normalize(finalPath);
        ThrowIfLost(final);
        var temp = final + IoGuardPolicy.TempSuffix;
        tempPath = temp;
        fs.Guard(IoOp.CreateNew, temp, Ctx);
        lock (fs.Gate)
        {
            if (fs.Find(temp) is not null) throw new IOException($"The file '{temp}' already exists.", unchecked((int)0x80070050));
            fs.RequireParentDirectory(temp);
            if (size > fs.DestinationFreeBytes) throw DiskFull();
            var node = fs.PutFile(temp, TempAttributes, DateTime.UnixEpoch);
            node.Bytes = [];
            _ownTemps.Add(temp);
            return new FakeWriteStream(this, node);
        }
    }

    public void FlushToDisk(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);
        s.Flush();
        FlushToDiskCount++;
    }

    public VerifyResult VerifyHash(string tempPath, long size, UInt128 expected, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var temp = PathRules.Normalize(tempPath);
        ThrowIfLost(temp);
        fs.Guard(IoOp.ReadData, temp, Ctx);
        byte[] data;
        VerifyMode mode;
        lock (fs.Gate)
        {
            data = [.. fs.Require(temp).Materialize()];
            var final = temp[..^IoGuardPolicy.TempSuffix.Length];
            mode = fs.Faults.UnbufferedUnsupported.Contains(final) ? VerifyMode.Cached : VerifyMode.Unbuffered;
            if (fs.Faults.CorruptVerify.TryGetValue(final, out var left) && left > 0 && data.Length > 0)
            {
                fs.Faults.CorruptVerify[final] = left - 1;
                data[0] ^= 1;
            }
        }
        var got = XxHash128.HashToUInt128(data);
        return got == expected && data.LongLength == size ? new HashMatch(mode) : new HashMismatch(got, mode);
    }

    public void FinalizeAttributes(string tempPath, DateTime creationUtc, DateTime mtimeUtc)
    {
        var temp = PathRules.Normalize(tempPath);
        ThrowIfLost(temp);
        fs.Guard(IoOp.SetAttributesOrTimes, temp, Ctx);
        lock (fs.Gate)
        {
            var n = fs.Require(temp);
            n.CreationUtc = creationUtc;
            n.MtimeUtc = mtimeUtc;
            n.Attributes &= ~IoGuardPolicy.FileAttributeHidden;
        }
    }

    public RenameResult RenameNoReplace(string tempPath, string finalPath)
    {
        var temp = PathRules.Normalize(tempPath);
        var final = PathRules.Normalize(finalPath);
        ThrowIfLost(final);
        fs.Guard(IoOp.Rename, temp, Ctx);
        lock (fs.Gate)
        {
            if (!PathRules.Equal(PathRules.Parent(temp) ?? "", PathRules.Parent(final) ?? ""))
                throw new UnsafeIoException($"Rename {temp} → {final} leaves its directory");
            if (fs.Faults.TargetAppearsBeforeRename.Contains(final) && fs.Find(final) is null)
                fs.AddFile(final, [0x42], DateTime.UnixEpoch);
            if (fs.Find(final) is not null) return new TargetExists();
            fs.Move(fs.Require(temp), final);
            _ownTemps.Remove(temp);
            _renamed.Add(final);
            return new Renamed();
        }
    }

    public bool ConfirmFinal(string finalPath, long size)
    {
        var final = PathRules.Normalize(finalPath);
        ThrowIfLost(final);
        lock (fs.Gate)
        {
            if (fs.Metadata(final) is not { IsDirectory: false } e) return false;
            var seen = fs.Faults.SizeAfterRename.TryGetValue(final, out var faked) ? faked : e.Size;
            return seen == size;
        }
    }

    public void FlushDestination(string dir, IReadOnlyList<string> filesCreatedThisRun)
    {
        ArgumentNullException.ThrowIfNull(filesCreatedThisRun);
        var d = PathRules.Normalize(dir);
        ThrowIfLost(d);
        foreach (var f in filesCreatedThisRun)
        {
            var p = fs.Guard(IoOp.OpenForFlush, f, Ctx);
            lock (fs.Gate) { _flushed.Add(p); }
        }
        var dp = fs.Guard(IoOp.OpenForFlush, d, Ctx);
        lock (fs.Gate) { _flushed.Add(dp); }
    }

    public void DeleteOwnTemp(string tempPath)
    {
        var temp = PathRules.Normalize(tempPath);
        if (!temp.EndsWith(IoGuardPolicy.TempSuffix, StringComparison.OrdinalIgnoreCase))
            throw new UnsafeIoException($"DeleteOwnTemp refuses {temp}: not a *{IoGuardPolicy.TempSuffix} file");
        ThrowIfLost(temp);
        if (!fs.Exists(temp)) return;
        fs.Guard(IoOp.Delete, temp, Ctx);
        lock (fs.Gate)
        {
            fs.RemoveTree(temp);
            _ownTemps.Remove(temp);
        }
    }

    public void EnsureDirectory(string dir, bool allowCreate)
    {
        var d = PathRules.Normalize(dir);
        ThrowIfLost(d);
        if (fs.Metadata(d) is { IsDirectory: true }) return;
        if (!allowCreate) throw new DirectoryNotFoundException($"{d} doesn't exist");
        var missing = new Stack<string>();
        for (var cur = d; cur is not null && !fs.Exists(cur); cur = PathRules.Parent(cur)) missing.Push(cur);
        while (missing.Count > 0)
        {
            var m = missing.Pop();
            fs.Guard(IoOp.CreateDir, m, Ctx);
            lock (fs.Gate)
            {
                fs.AddDirectory(m);
                _createdDirectories.Add(m);
            }
        }
    }

    public bool TryGetSize(string path, out long size)
    {
        var e = fs.Metadata(path);
        size = e is { IsDirectory: false } ? e.Size : 0;
        return e is { IsDirectory: false };
    }

    public long FreeBytes(string anyPathOnVolume)
    {
        ThrowIfLost(PathRules.Normalize(anyPathOnVolume));
        return fs.DestinationFreeBytes;
    }

    internal void WriteTemp(FakeNode node, ReadOnlySpan<byte> bytes)
    {
        ThrowIfLost(node.Path);
        var allowed = bytes.Length;
        if (fs.Faults.DiskFullAfterBytes is long limit)
            allowed = (int)Math.Max(0, Math.Min(bytes.Length, limit - Interlocked.Read(ref _bytesWritten)));
        fs.AppendBytes(node, bytes[..allowed]);
        Interlocked.Add(ref _bytesWritten, allowed);
        if (allowed < bytes.Length) throw DiskFull();
    }

    private void ThrowIfLost(string path)
    {
        if (fs.Faults.IsLost(path)) throw new DirectoryNotFoundException($"The destination root of {path} is gone");
    }

    private static IOException DiskFull() => new("There is not enough space on the disk.", unchecked((int)0x80070070));
}

internal sealed class FakeWriteStream(FakeFileOps ops, FakeNode node) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => node.Size;
    public override long Position { get => node.Size; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override void Write(byte[] buffer, int offset, int count) => ops.WriteTemp(node, buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer) => ops.WriteTemp(node, buffer);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*FakeFileOpsTests"`
Expected: PASS (10 tests).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Testing/FakeFileOps.cs tests/UasSort.Core.Tests/Testing/FakeFileOpsTests.cs
git commit -m "test: add guarded FakeFileOps with destination fault hooks

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 02.11: `CardClassifier` — fail-safe classification, units and inventory hash

**Files:**
- Create: `src/UasSort.Core/Card/CardClassifier.cs`
- Test: `tests/UasSort.Core.Tests/Card/CardClassifierTests.cs`

**Interfaces:**

```csharp
// Consumes: CardSource, ListingResult, FsEntry, CardEntry, EntryClass, MediaUnit family, CardInventory, ScanWarning (02.2);
//           PathRules (02.1); FakeFileSystem, FakeLayout (02.8);
//           Microsoft.Extensions.Time.Testing.FakeTimeProvider (package Microsoft.Extensions.TimeProvider.Testing, global using from 02.1).
// Produces:
namespace UasSort.Core.Card;
public sealed partial class CardClassifier(TimeProvider clock)
{ static readonly ImmutableHashSet<string> MediaExtensions;                     // (defined here) mp4 mov dng jpg jpeg heic heif tif tiff insv avi
  CardInventory Classify(CardSource source, ListingResult listing);              // Ref §4.2
  static string ComputeInventoryHash(IEnumerable<CardEntry> files); }          // (defined here) XxHash64 of "relPath|size|mtimeTicks\n", sorted by lowercase relPath
// Tests use new FakeTimeProvider(now) (advance with SetUtcNow); no hand-written TimeProvider fake exists in UasSort.Testing.
```

Classification (Ref §5, first match wins; paths relative to the anchored root, `/`, case-insensitive): `MISC\**`, `LOST.DIR\**` → Skip "DJI: no backup needed"; `Android\**`, `System Volume Information\**`, `$RECYCLE.BIN\**` → Skip "system"; anything inside `.Trashes`, `.Spotlight-V100`, `.fseventsd` → Skip "system/recovery"; `*.LRF` → Skip "proxy"; `*.SRT` → Skip "captions"; `._*`, `.*.trinf`, `.*.avc1` → Skip "system/recovery"; any other dot file → Unknown if media or under `DCIM`, else Skip "outside DCIM, not media"; a file directly in a DCIM media folder (a folder matching `DJI_\d{3}(_.*)?`): MP4 → Video, DNG → Photo, JPG with a DNG's stem → PhotoTwin, JPG with an MP4's stem → Unknown "possible video cover", other JPG → Photo, anything else → Unknown; a DNG or JPG directly in a set folder under `DCIM\PANORAMA` or `DCIM\HYPERLAPSE` → SetMember; everything else → Unknown if media or under `DCIM`, else Skip "outside DCIM, not media". Only files become `CardEntry`s. Enumeration errors become `ScanWarning("EnumerationError", …, ForcesNotSafe: true)`. `CameraModel` comes from `MISC\FC*.db` (else null; the harvester adds the EXIF fallback, Part 03).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Card/CardClassifierTests.cs
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace UasSort.Core.Tests.Card;

public class CardClassifierTests
{
    private static readonly DateTime T = new(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 20, 0, 0, TimeSpan.Zero);
    private static readonly CardSource Source = new(@"E:\", FakeLayout.CardId, false, false);

    private static readonly (string Rel, EntryClass Class, string? Rule)[] Rows =
    [
        ("DCIM/DJI_001/DJI_20260927140627_0128_D.MP4", EntryClass.Video, null),
        ("DCIM/DJI_001/DJI_20260927140627_0128_D.LRF", EntryClass.Skip, "proxy"),
        ("DCIM/DJI_001/DJI_20260927140627_0128_D.SRT", EntryClass.Skip, "captions"),
        ("DCIM/DJI_001/DJI_20260927141000_0129_D.DNG", EntryClass.Photo, null),
        ("DCIM/DJI_001/DJI_20260927141000_0129_D.JPG", EntryClass.PhotoTwin, null),
        ("DCIM/DJI_001/DJI_20260927141100_0130_D.JPG", EntryClass.Photo, null),
        ("DCIM/DJI_001/DJI_20260927140700_0131_D.MP4", EntryClass.Video, null),
        ("DCIM/DJI_001/DJI_20260927140700_0131_D.JPG", EntryClass.Unknown, "possible video cover"),
        ("DCIM/DJI_001/DJI_20260727002013_0014_D.MP4", EntryClass.Video, null),
        ("DCIM/DJI_001/.DJI_20260727002013_0014_D.MP4.trinf", EntryClass.Skip, "system/recovery"),
        ("DCIM/DJI_001/.DJI_20260727002013_0014_D.avc1", EntryClass.Skip, "system/recovery"),
        ("DCIM/DJI_001/._DJI_20260927140627_0128_D.MP4", EntryClass.Skip, "system/recovery"),
        ("DCIM/DJI_001/.foo.MP4", EntryClass.Unknown, null),
        ("DCIM/DJI_001/DJI_20260927150000_0140_D.HEIC", EntryClass.Unknown, null),
        ("DCIM/DJI_002_A01/DJI_20260928100000_0001_D.MP4", EntryClass.Video, null),
        ("DCIM/DJI_A001/x.MP4", EntryClass.Unknown, null),
        ("DCIM/DJI_001/nested/DJI_20260927140000_0001_D.MP4", EntryClass.Unknown, null),
        ("DCIM/PANORAMA/001_0087/PANO_0001.DNG", EntryClass.SetMember, null),
        ("DCIM/PANORAMA/001_0087/PANO_0002.DNG", EntryClass.SetMember, null),
        ("DCIM/PANORAMA/001_0087/notes.txt", EntryClass.Unknown, null),
        ("DCIM/HYPERLAPSE/001_0090/HYPERLAPSE_0001.JPG", EntryClass.SetMember, null),
        ("x.MP4", EntryClass.Unknown, null),
        ("readme.txt", EntryClass.Skip, "outside DCIM, not media"),
        ("MISC/FC9113.db", EntryClass.Skip, "DJI: no backup needed"),
        ("MISC/THM/DJI_0001.JPG", EntryClass.Skip, "DJI: no backup needed"),
        ("LOST.DIR/1", EntryClass.Skip, "DJI: no backup needed"),
        ("System Volume Information/IndexerVolumeGuid", EntryClass.Skip, "system"),
        (".Trashes/501/DJI_20260927140627_0128_D.MP4", EntryClass.Skip, "system/recovery"),
    ];

    public static TheoryData<string, EntryClass, string?> Expectations
    {
        get
        {
            var data = new TheoryData<string, EntryClass, string?>();
            foreach (var (rel, cls, rule) in Rows) data.Add(rel, cls, rule);
            return data;
        }
    }

    private static CardInventory Classify(Action<FakeFileSystem>? more = null)
    {
        var fs = FakeLayout.NewFileSystem();
        foreach (var row in Rows) fs.AddFile(@"E:\" + row.Rel, 1_000, T);
        more?.Invoke(fs);
        var listing = fs.Enumerate(@"E:\", recurse: true, ImmutableHashSet<string>.Empty);
        return new CardClassifier(new FakeTimeProvider(Now)).Classify(Source, listing);
    }

    [Theory]
    [MemberData(nameof(Expectations))]
    public void EveryFile_GetsItsFailSafeClass(string relPath, EntryClass expected, string? rule)
    {
        var e = Assert.Single(Classify().Entries, x => x.RelPath == relPath);
        Assert.Equal(expected, e.Class);
        Assert.Equal(rule, e.Rule);
    }

    [Fact]
    public void OnlyFilesBecomeEntries()
    {
        var inv = Classify();
        Assert.Equal(Rows.Length, inv.Entries.Length);
        Assert.DoesNotContain(inv.Entries, e => e.RelPath is "DCIM" or "DCIM/DJI_001");
    }

    [Fact]
    public void Units_ArePairsSetsAndVideosWithTrinfFlags()
    {
        var inv = Classify();
        var videos = inv.Units.OfType<VideoUnit>().ToDictionary(v => v.Id.CardRelPath);
        Assert.Equal(4, videos.Count);
        Assert.True(videos["DCIM/DJI_001/DJI_20260727002013_0014_D.MP4"].HasTrinf);
        Assert.False(videos["DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"].HasTrinf);

        var photos = inv.Units.OfType<PhotoUnit>().ToDictionary(p => p.Id.CardRelPath);
        Assert.Equal(2, photos.Count);
        Assert.Equal("DCIM/DJI_001/DJI_20260927141000_0129_D.JPG", photos["DCIM/DJI_001/DJI_20260927141000_0129_D.DNG"].JpgTwin!.RelPath);
        Assert.Null(photos["DCIM/DJI_001/DJI_20260927141100_0130_D.JPG"].JpgTwin);

        var sets = inv.Units.OfType<SetUnit>().ToDictionary(s => s.Id.CardRelPath);
        Assert.Equal(2, sets.Count);
        var pano = sets["DCIM/PANORAMA/001_0087"];
        Assert.Equal(SetKind.Panorama, pano.Kind);
        Assert.Equal("001_0087", pano.SetName);
        string[] members = ["DCIM/PANORAMA/001_0087/PANO_0001.DNG", "DCIM/PANORAMA/001_0087/PANO_0002.DNG"];
        Assert.Equal(members, pano.Members.Select(m => m.RelPath));
        Assert.Equal(SetKind.Hyperlapse, sets["DCIM/HYPERLAPSE/001_0090"].Kind);
        Assert.Equal(inv.Units.Select(u => u.Id.CardRelPath).Order(StringComparer.Ordinal), inv.Units.Select(u => u.Id.CardRelPath));
    }

    [Fact]
    public void CameraModel_ComesFromTheMiscDatabase_AndListedUtcFromTheClock()
    {
        var inv = Classify();
        Assert.Equal("FC9113", inv.CameraModel);
        Assert.Equal(Now.UtcDateTime, inv.ListedUtc);
        Assert.Same(Source, inv.Source);
    }

    [Fact]
    public void EnumerationErrors_BecomeForcesNotSafeWarnings()
    {
        var inv = Classify(fs =>
        {
            fs.AddFile(@"E:\DCIM\DJI_003\DJI_20260929100000_0300_D.MP4", 10, T);
            fs.Faults.EnumerationErrors[@"E:\DCIM\DJI_003"] = 5;
        });
        var w = Assert.Single(inv.Warnings);
        Assert.Equal("EnumerationError", w.Code);
        Assert.Equal("DCIM/DJI_003", w.RelPath);
        Assert.True(w.ForcesNotSafe);
    }

    [Fact]
    public void InventoryHash_SortsByLowercasePath_AndHashesSizeAndMtimeTicks()
    {
        var t1 = new DateTime(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc);
        var a = new CardEntry("DCIM/a.MP4", 1, t1, t1, t1, 0x20, EntryClass.Unknown, null);
        var b = new CardEntry("DCIM/B.MP4", 2, t1, t1, t1, 0x20, EntryClass.Unknown, null);
        var text = string.Create(CultureInfo.InvariantCulture, $"DCIM/a.MP4|1|{t1.Ticks}\nDCIM/B.MP4|2|{t1.Ticks}\n");
        var expected = XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(text)).ToString("x16", CultureInfo.InvariantCulture);
        Assert.Equal(expected, CardClassifier.ComputeInventoryHash([b, a]));
        Assert.NotEqual(expected, CardClassifier.ComputeInventoryHash([a, b with { Size = 3 }]));
        Assert.Matches("^[0-9a-f]{16}$", Classify().InventoryHash);
        Assert.Equal(Classify().InventoryHash, Classify().InventoryHash);
    }

    [Fact]
    public void ACardWithMediaOnlyInUnrecognisedLocations_HasNoUnitsAndOnlyUnknownMedia()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(@"E:\DCIM\DJI_A001\x.MP4", 10, T);
        fs.AddFile(@"E:\Videos\DJI_20260927140627_0128_D.MP4", 10, T);
        var inv = new CardClassifier(new FakeTimeProvider(Now)).Classify(Source, fs.Enumerate(@"E:\", true, ImmutableHashSet<string>.Empty));
        Assert.Empty(inv.Units);
        Assert.All(inv.Entries, e => Assert.Equal(EntryClass.Unknown, e.Class));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CardClassifierTests"`
Expected: build FAILS (CS0246: `CardClassifier` not found; the namespace `UasSort.Core.Card` already exists through `Namespaces.cs`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Card/CardClassifier.cs
using System.Globalization;
using System.IO.Hashing;
using System.Text;
using System.Text.RegularExpressions;

namespace UasSort.Core.Card;

/// <summary>Classifies every card file with the fail-safe rules of Ref §5: only named rules skip; unclaimed media is Unknown.</summary>
public sealed partial class CardClassifier(TimeProvider clock)
{
    public static readonly ImmutableHashSet<string> MediaExtensions = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
        "mp4", "mov", "dng", "jpg", "jpeg", "heic", "heif", "tif", "tiff", "insv", "avi");

    private const string DjiNoBackup = "DJI: no backup needed";
    private const string SystemRule = "system";
    private const string Recovery = "system/recovery";
    private const string OutsideDcim = "outside DCIM, not media";

    [GeneratedRegex(@"^DJI_\d{3}(_.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MediaFolder();

    [GeneratedRegex(@"^MISC/(FC\d+)\.db$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModelDatabase();

    public CardInventory Classify(CardSource source, ListingResult listing)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listing);

        var files = listing.Entries.Where(e => !e.IsDirectory)
            .Select(e => (Rel: e.RelPath.Replace('\\', '/').TrimStart('/'), Entry: e))
            .OrderBy(x => x.Rel, StringComparer.Ordinal)
            .ToList();

        var dngStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mp4Stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trinfTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rel, _) in files)
        {
            var (folder, name) = SplitFolder(rel);
            var ext = Extension(name);
            if (ext == "dng") dngStems.Add(folder + "/" + Stem(name));
            if (ext == "mp4") mp4Stems.Add(folder + "/" + Stem(name));
            if (name.StartsWith('.') && ext == "trinf") trinfTargets.Add(folder + "/" + name[1..^".trinf".Length]);
        }

        var entries = ImmutableArray.CreateBuilder<CardEntry>(files.Count);
        foreach (var (rel, e) in files)
        {
            var (cls, rule) = ClassifyOne(rel, dngStems, mp4Stems);
            entries.Add(new CardEntry(rel, e.Size, e.MtimeUtc, e.CreationUtc, e.LastAccessUtc, e.RawAttributes, cls, rule));
        }
        var all = entries.ToImmutable();

        var model = all.Select(x => ModelDatabase().Match(x.RelPath)).FirstOrDefault(m => m.Success)?.Groups[1].Value
                       .ToUpperInvariant();

        var warnings = listing.Errors.Select(err => new ScanWarning("EnumerationError",
            string.Create(CultureInfo.InvariantCulture, $"Couldn't read {err.Path} (Win32 error {err.Win32Error})"),
            RelativeOrNull(err.Path, source.Root), ForcesNotSafe: true)).ToImmutableArray();

        return new CardInventory(source, clock.GetUtcNow().UtcDateTime, ComputeInventoryHash(all), all,
                                 BuildUnits(all, trinfTargets), model, warnings);
    }

    public static string ComputeInventoryHash(IEnumerable<CardEntry> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var sb = new StringBuilder();
        foreach (var e in files.OrderBy(e => Lower(e.RelPath), StringComparer.Ordinal))
            sb.Append(e.RelPath).Append('|')
              .Append(e.Size.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(e.MtimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(sb.ToString())).ToString("x16", CultureInfo.InvariantCulture);
    }

    private static (EntryClass Class, string? Rule) ClassifyOne(string rel, HashSet<string> dngStems, HashSet<string> mp4Stems)
    {
        var seg = rel.Split('/');
        var name = seg[^1];
        var ext = Extension(name);
        var isMedia = MediaExtensions.Contains(ext);
        var underDcim = seg.Length > 1 && Eq(seg[0], "DCIM");

        if (seg.Length > 1 && (Eq(seg[0], "MISC") || Eq(seg[0], "LOST.DIR"))) return (EntryClass.Skip, DjiNoBackup);
        if (seg.Length > 1 && (Eq(seg[0], "Android") || Eq(seg[0], "System Volume Information") || Eq(seg[0], "$RECYCLE.BIN")))
            return (EntryClass.Skip, SystemRule);
        for (var i = 0; i < seg.Length - 1; i++)
            if (Eq(seg[i], ".Trashes") || Eq(seg[i], ".Spotlight-V100") || Eq(seg[i], ".fseventsd")) return (EntryClass.Skip, Recovery);
        if (ext == "lrf") return (EntryClass.Skip, "proxy");
        if (ext == "srt") return (EntryClass.Skip, "captions");
        if (name.StartsWith("._", StringComparison.Ordinal) || (name.StartsWith('.') && ext is "trinf" or "avc1"))
            return (EntryClass.Skip, Recovery);
        if (name.StartsWith('.')) return isMedia || underDcim ? (EntryClass.Unknown, null) : (EntryClass.Skip, OutsideDcim);

        if (seg.Length == 3 && underDcim && MediaFolder().IsMatch(seg[1]))
        {
            var key = seg[0] + "/" + seg[1] + "/" + Stem(name);
            return ext switch
            {
                "mp4" => (EntryClass.Video, null),
                "dng" => (EntryClass.Photo, null),
                "jpg" when dngStems.Contains(key) => (EntryClass.PhotoTwin, null),
                "jpg" when mp4Stems.Contains(key) => (EntryClass.Unknown, "possible video cover"),
                "jpg" => (EntryClass.Photo, null),
                _ => (EntryClass.Unknown, null),
            };
        }
        if (seg.Length == 4 && underDcim && (Eq(seg[1], "PANORAMA") || Eq(seg[1], "HYPERLAPSE")) && ext is "dng" or "jpg")
            return (EntryClass.SetMember, null);
        return isMedia || underDcim ? (EntryClass.Unknown, null) : (EntryClass.Skip, OutsideDcim);
    }

    private static ImmutableArray<MediaUnit> BuildUnits(ImmutableArray<CardEntry> all, HashSet<string> trinfTargets)
    {
        var units = new List<MediaUnit>();
        var twins = new Dictionary<string, CardEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in all.Where(e => e.Class == EntryClass.PhotoTwin)) twins.TryAdd(StemKey(t.RelPath), t);
        foreach (var e in all)
        {
            if (e.Class == EntryClass.Video)
                units.Add(new VideoUnit(new ItemId(e.RelPath), e, trinfTargets.Contains(e.RelPath)));
            else if (e.Class == EntryClass.Photo)
                units.Add(new PhotoUnit(new ItemId(e.RelPath), e,
                    Extension(e.RelPath) == "dng" ? twins.GetValueOrDefault(StemKey(e.RelPath)) : null));
        }
        foreach (var set in all.Where(e => e.Class == EntryClass.SetMember)
                               .GroupBy(e => string.Join('/', e.RelPath.Split('/')[..3]), StringComparer.OrdinalIgnoreCase))
        {
            var seg = set.Key.Split('/');
            var kind = Eq(seg[1], "PANORAMA") ? SetKind.Panorama : SetKind.Hyperlapse;
            units.Add(new SetUnit(new ItemId(set.Key), kind, seg[2], [.. set.OrderBy(m => m.RelPath, StringComparer.Ordinal)]));
        }
        return [.. units.OrderBy(u => u.Id.CardRelPath, StringComparer.Ordinal)];
    }

    private static (string Folder, string Name) SplitFolder(string rel)
    {
        var i = rel.LastIndexOf('/');
        return i < 0 ? ("", rel) : (rel[..i], rel[(i + 1)..]);
    }

    private static string StemKey(string rel)
    {
        var (folder, name) = SplitFolder(rel);
        return folder + "/" + Stem(name);
    }

    private static string Stem(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name[..dot];
    }

    private static string Extension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 || dot == name.Length - 1 ? "" : Lower(name[(dot + 1)..]);
    }

    private static string? RelativeOrNull(string path, string root)
        => PathRules.IsStrictlyUnder(path, root) ? PathRules.RelativeCardPath(path, root) : null;

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

#pragma warning disable CA1308 // extensions and the inventory-hash sort key are lowercase by definition (Ref §4.2)
    private static string Lower(string s) => s.ToLowerInvariant();
#pragma warning restore CA1308
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CardClassifierTests"`
Expected: PASS (28 theory rows and 6 facts).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Card/CardClassifier.cs tests/UasSort.Core.Tests/Card/CardClassifierTests.cs
git commit -m "feat: add fail-safe CardClassifier with units and inventory hash

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 02.12: `CardDetector` — DJI card detection, never auto-picking between two cards

**Files:**
- Create: `src/UasSort.Core/Card/CardDetector.cs`
- Test: `tests/UasSort.Core.Tests/Card/CardDetectorTests.cs`

**Interfaces:**

```csharp
// Consumes: VolumeInfo, CardCandidate, IDirectoryLister, Settings (02.2–02.4); CardClassifier.MediaExtensions (02.11);
//           PathRules (02.1); FakeFileSystem, FakeLayout (02.8).
// Produces:
namespace UasSort.Core.Card;
public static partial class CardDetector
{ const string NotACard = "not a DJI card";                                        // (defined here)
  static IReadOnlyList<CardCandidate> Detect(IReadOnlyList<VolumeInfo> volumes, IDirectoryLister lister, Settings settings);   // Ref §4.2
  static CardCandidate? SingleDjiCard(IReadOnlyList<CardCandidate> candidates); }  // (defined here) the one DJI card, or null when 0 or ≥ 2
```

Rules (Ref §5): skip volumes that aren't ready, `DriveType` Network/CDRom/NoRootDirectory, and volumes holding the video root, photo root or a previous photo root (reachable only through Browse). Never rely on `DriveType` or the label otherwise (D: is Fixed exFAT). A volume is a DJI card when `\DCIM\` holds a folder matching `^DJI_\d{3}(_.+)?$`, `PANORAMA` or `HYPERLAPSE` **and** some file under `DCIM` matches `^DJI_(\d{14})_(\d{4})_([A-Z])(?:_[^.]*)?\.([A-Za-z0-9]+)$`; anything else is listed with `NotCardReason = "not a DJI card"`. `MediaCount` = media-extension files in the matched folders. Candidates are ordered by root. The Card stage (Part 10) scans automatically only when `SingleDjiCard` returns a card.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Card/CardDetectorTests.cs
namespace UasSort.Core.Tests.Card;

public class CardDetectorTests
{
    private static readonly DateTime T = new(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc);

    private static VolumeInfo Vol(string root, uint serial, string driveType = "Removable", bool ready = true,
                                  string fs = "exFAT", string? label = null)
        => new(root, new CardIdentity(serial, label, fs, 256_060_514_304), driveType, ready, false, fs == "NTFS",
               driveType == "Removable", 12_400_000_000, "Sd", true, false);

    private sealed class CountingLister(IDirectoryLister inner) : IDirectoryLister
    {
        public List<string> Roots { get; } = [];
        public ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames)
        {
            Roots.Add(root);
            return inner.Enumerate(root, recurse, excludeDirNames);
        }
    }

    private static void AddDjiCard(FakeFileSystem fs, string root)
    {
        fs.AddFile(root + @"DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", 100, T);
        fs.AddFile(root + @"DCIM\DJI_001\DJI_20260927140627_0128_D.LRF", 10, T);
        fs.AddFile(root + @"DCIM\DJI_001\DJI_20260927141000_0129_D.DNG", 50, T);
        fs.AddFile(root + @"MISC\FC9113.db", 4, T);
    }

    [Fact]
    public void Detect_FlagsDjiCardsAndListsOtherVolumesAsNotACard()
    {
        var fs = FakeLayout.NewFileSystem();
        AddDjiCard(fs, @"E:\");
        fs.AddFile(@"F:\DCIM\100MEDIA\MAX_0061.MP4", 100, T);                                    // Autel
        fs.AddFile(@"G:\Android\data\dji.go.v5\files\MediaCaches\x.jpg", 10, T);                 // RC 2 storage
        fs.AddFile(@"K:\DCIM\PANORAMA\001_0087\PANO_0001.DNG", 10, T);                          // no DJI-named file
        AddDjiCard(fs, @"D:\");                                                                  // Fixed exFAT copy: DriveType is not trusted
        var volumes = new[] { Vol(@"E:\", 1), Vol(@"F:\", 2), Vol(@"G:\", 3), Vol(@"K:\", 4), Vol(@"D:\", 5, "Fixed") };

        var found = CardDetector.Detect(volumes, fs, FakeLayout.Settings());

        string[] roots = [@"D:\", @"E:\", @"F:\", @"G:\", @"K:\"];
        Assert.Equal(roots, found.Select(c => c.Volume.Root));
        var e = found.Single(c => c.Volume.Root == @"E:\");
        Assert.True(e.IsDjiCard);
        Assert.Null(e.NotCardReason);
        Assert.Equal(2, e.MediaCount);
        Assert.True(found.Single(c => c.Volume.Root == @"D:\").IsDjiCard);
        foreach (var root in new[] { @"F:\", @"G:\", @"K:\" })
        {
            var c = found.Single(x => x.Volume.Root == root);
            Assert.False(c.IsDjiCard);
            Assert.Equal(CardDetector.NotACard, c.NotCardReason);
        }
    }

    [Fact]
    public void Detect_SkipsUnreadyNetworkOpticalRootlessAndRootHoldingVolumes_WithoutListingThem()
    {
        var fs = FakeLayout.NewFileSystem();
        foreach (var r in new[] { @"H:\", @"I:\", @"M:\", @"N:\", @"O:\" }) AddDjiCard(fs, r);
        AddDjiCard(fs, @"C:\");
        var lister = new CountingLister(fs);
        var volumes = new[]
        {
            Vol(@"H:\", 1, "Network"), Vol(@"I:\", 2, ready: false), Vol(@"M:\", 3, "CDRom"), Vol(@"N:\", 4, "NoRootDirectory"),
            Vol(@"C:\", 5, "Fixed", fs: "NTFS"),   // holds the video root
            Vol(@"O:\", 6, "Fixed"),               // holds a previous photo root
        };
        var settings = FakeLayout.Settings(previousPhotoRoots: [@"O:\Old Photos"]);

        Assert.Empty(CardDetector.Detect(volumes, lister, settings));
        Assert.Empty(lister.Roots);
    }

    [Fact] // [Review Focus] item 5
    public void DroneOverUsb_TwoDjiVolumes_AreBothListed_AndNeverAutoPicked()
    {
        var fs = FakeLayout.NewFileSystem();
        AddDjiCard(fs, @"J:\");   // aircraft internal storage
        AddDjiCard(fs, @"L:\");   // the SD card in the aircraft
        var volumes = new[] { Vol(@"L:\", 0x5D000002, label: "SD_card"), Vol(@"J:\", 0x1A000001, label: "InternalStorage") };

        var found = CardDetector.Detect(volumes, fs, FakeLayout.Settings());

        Assert.Equal(2, found.Count);
        Assert.All(found, c => Assert.True(c.IsDjiCard));
        Assert.Null(CardDetector.SingleDjiCard(found));
        var keys = found.Select(c => new CardSource(c.Volume.Root, c.Volume.Identity, false, false).DraftKey).ToList();
        string[] expectedKeys = ["vol-1A000001", "vol-5D000002"];
        Assert.Equal(expectedKeys, keys);
    }

    [Fact]
    public void SingleDjiCard_PicksTheOnlyDjiCard_IgnoringNonCards()
    {
        var fs = FakeLayout.NewFileSystem();
        AddDjiCard(fs, @"E:\");
        fs.AddFile(@"F:\DCIM\100MEDIA\MAX_0061.MP4", 100, T);
        var found = CardDetector.Detect([Vol(@"E:\", 1), Vol(@"F:\", 2)], fs, FakeLayout.Settings());
        Assert.Equal(@"E:\", CardDetector.SingleDjiCard(found)!.Volume.Root);
        Assert.Null(CardDetector.SingleDjiCard([]));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CardDetectorTests"`
Expected: build FAILS (CS0103: `CardDetector` not found).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Card/CardDetector.cs
using System.Text.RegularExpressions;

namespace UasSort.Core.Card;

/// <summary>Flags DJI cards among the ready volumes (Ref §5). Never picks between several cards.</summary>
public static partial class CardDetector
{
    public const string NotACard = "not a DJI card";

    [GeneratedRegex(@"^DJI_\d{3}(_.+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DjiFolder();

    [GeneratedRegex(@"^DJI_(\d{14})_(\d{4})_([A-Z])(?:_[^.]*)?\.([A-Za-z0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DjiFileName();

    public static IReadOnlyList<CardCandidate> Detect(IReadOnlyList<VolumeInfo> volumes, IDirectoryLister lister, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(settings);
        var result = new List<CardCandidate>();
        foreach (var v in volumes.OrderBy(v => PathRules.Normalize(v.Root), StringComparer.OrdinalIgnoreCase))
        {
            if (!v.IsReady || IsSkippedDriveType(v.DriveType) || HoldsConfiguredRoot(v.Root, settings)) continue;
            var listing = lister.Enumerate(PathRules.Join(v.Root, "DCIM"), recurse: true, ImmutableHashSet<string>.Empty);
            var mediaDirs = listing.Entries
                .Where(e => e.IsDirectory && TopSegment(e.RelPath) == e.RelPath.Replace('/', '\\'))
                .Select(e => PathRules.FileName(e.FullPath))
                .Where(n => DjiFolder().IsMatch(n) || Eq(n, "PANORAMA") || Eq(n, "HYPERLAPSE"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var files = listing.Entries.Where(e => !e.IsDirectory).ToList();
            var djiNamed = files.Any(e => DjiFileName().IsMatch(PathRules.FileName(e.FullPath)));
            var isDji = mediaDirs.Count > 0 && djiNamed;
            var mediaCount = isDji
                ? files.Count(e => mediaDirs.Contains(TopSegment(e.RelPath)) && CardClassifier.MediaExtensions.Contains(Extension(e.FullPath)))
                : 0;
            result.Add(new CardCandidate(v, isDji, isDji ? null : NotACard, mediaCount));
        }
        return result;
    }

    public static CardCandidate? SingleDjiCard(IReadOnlyList<CardCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var dji = candidates.Where(c => c.IsDjiCard).ToList();
        return dji.Count == 1 ? dji[0] : null;
    }

    private static bool IsSkippedDriveType(string driveType)
        => Eq(driveType, "Network") || Eq(driveType, "CDRom") || Eq(driveType, "NoRootDirectory");

    private static bool HoldsConfiguredRoot(string volumeRoot, Settings s)
        => PathRules.IsSameOrUnder(s.VideoRoot, volumeRoot) || PathRules.IsSameOrUnder(s.PhotoRoot, volumeRoot)
           || s.PreviousPhotoRoots.Any(r => PathRules.IsSameOrUnder(r, volumeRoot));

    private static string TopSegment(string relPath)
    {
        var rel = relPath.Replace('/', '\\');
        var i = rel.IndexOf('\\', StringComparison.Ordinal);
        return i < 0 ? rel : rel[..i];
    }

    private static string Extension(string path)
    {
        var name = PathRules.FileName(path);
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[(dot + 1)..];
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CardDetectorTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Card/CardDetector.cs tests/UasSort.Core.Tests/Card/CardDetectorTests.cs
git commit -m "feat: add CardDetector that lists every DJI volume and never auto-picks

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 02.13: `CardSourceValidator` — anchor, canonicalise, refuse overlaps

**Files:**
- Create: `src/UasSort.Core/Card/CardSourceValidator.cs`
- Create: `tests/UasSort.Testing/FakePathFacts.cs`
- Test: `tests/UasSort.Core.Tests/Card/CardSourceValidatorTests.cs`

**Interfaces:**

```csharp
// Consumes: ICardSourceValidator, IPathFacts, IDirectoryLister, VolumeInfo, Settings, CardSource, CardSourceCheck (02.2–02.4);
//           LedgerPaths, PathRules (02.1); CardClassifier.MediaExtensions (02.11); FakeFileSystem, FakeLayout (02.8).
// Produces:
namespace UasSort.Core.Card;
public sealed class CardSourceValidator : ICardSourceValidator
{ const string PickTopFolder = "Pick the card's top folder (the one containing DCIM)";                      // (defined here)
  const string NoDcim = "No DCIM folder here";                                                               // (defined here)
  const string PartOfLibrary = "This is part of your library (or a synced folder); uas-sort only offloads from cards.";   // (defined here)
  CardSourceCheck Validate(string chosenPath, VolumeInfo? detected, Settings s, IDirectoryLister lister, IPathFacts facts, string appDataDir); }
namespace UasSort.Testing;
public sealed class FakePathFacts : IPathFacts           // (defined here) subst/junction aliases + sync roots
{ void AddAlias(string from, string to); void AddSyncRoot(string root); }
```

Steps (Ref §4.3 "Card source validation"): (1) walk up from the chosen folder (itself included) to the nearest folder whose listing holds a `DCIM` directory; none → `PickTopFolder` if the chosen folder holds a media-extension file, else `NoDcim`; (2) `facts.Canonical(anchor)`; (3) refuse when the root equals, is inside or contains the video root, photo root, any previous photo root, `LedgerPaths.For(videoRoot)`, `appDataDir` (each canonicalised; a root that can't be canonicalised is compared as written), when `facts.InSyncRoot(root)`, or when it overlaps any `facts.SyncRoots()` entry. Only listings are used; nothing is opened.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Testing/FakePathFacts.cs
namespace UasSort.Testing;

/// <summary>Canonical paths through subst drives / junctions (aliases) and cloud sync roots, for validator tests.</summary>
public sealed class FakePathFacts : IPathFacts
{
    private readonly List<(string From, string To)> _aliases = [];
    private readonly List<string> _syncRoots = [];

    public void AddAlias(string from, string to) => _aliases.Add((PathRules.Normalize(from), PathRules.Normalize(to)));

    public void AddSyncRoot(string root) => _syncRoots.Add(PathRules.Normalize(root));

    public string Canonical(string path)
    {
        var p = PathRules.Normalize(path);
        foreach (var (from, to) in _aliases.OrderByDescending(a => a.From.Length))
            if (PathRules.IsSameOrUnder(p, from)) return PathRules.Join(to, PathRules.RelativeCardPath(p, from));
        return p;
    }

    public bool InSyncRoot(string canonicalPath) => _syncRoots.Any(r => PathRules.IsSameOrUnder(canonicalPath, r));

    public IReadOnlyList<string> SyncRoots() => [.. _syncRoots];
}
```

```csharp
// tests/UasSort.Core.Tests/Card/CardSourceValidatorTests.cs
namespace UasSort.Core.Tests.Card;

public class CardSourceValidatorTests
{
    private const string V = FakeLayout.VideoRoot;
    private const string A = FakeLayout.AppDataDir;
    private static readonly DateTime T = new(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc);
    private static readonly Settings S = FakeLayout.Settings(photoRoot: @"F:\Photos", previousPhotoRoots: [@"D:\Old Photos"]);
    private static readonly CardSourceValidator Validator = new();

    private static FakeFileSystem CardAt(params string[] anchors)
    {
        var fs = FakeLayout.NewFileSystem();
        foreach (var a in anchors) fs.AddFile(PathRules.Join(a, @"DCIM\DJI_001\DJI_20260927140627_0128_D.MP4"), 100, T);
        return fs;
    }

    private static VolumeInfo Volume(bool readOnly) => new(@"E:\", FakeLayout.CardId, "Removable", true, readOnly, false, true,
                                                             12_400_000_000, "Sd", true, false);

    private static CardSource Ok(CardSourceCheck c) => c switch
    {
        SourceOk ok => ok.Source,
        SourceRefused r => throw new Xunit.Sdk.XunitException($"refused: {r.Reason}"),
    };

    private static string Refused(CardSourceCheck c) => c switch
    {
        SourceOk ok => throw new Xunit.Sdk.XunitException($"accepted: {ok.Source.Root}"),
        SourceRefused r => r.Reason,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetectedVolume_IsNotBrowsed_AndCarriesItsWriteProtection(bool readOnly)
    {
        var src = Ok(Validator.Validate(@"E:\", Volume(readOnly), S, CardAt(@"E:\"), new FakePathFacts(), A));
        Assert.Equal(@"E:\", src.Root);
        Assert.False(src.IsBrowsedFolder);
        Assert.Equal(readOnly, src.IsWriteProtected);
        Assert.Equal(FakeLayout.CardId, src.Identity);
    }

    [Theory]
    [InlineData(@"E:\")]
    [InlineData(@"E:\DCIM")]
    [InlineData(@"E:\DCIM\DJI_001")]
    public void Browse_AnchorsAtTheFolderHoldingDcim_AndIsAlwaysBrowsed(string chosen)
    {
        var src = Ok(Validator.Validate(chosen, null, S, CardAt(@"E:\"), new FakePathFacts(), A));
        Assert.Equal(@"E:\", src.Root);
        Assert.True(src.IsBrowsedFolder);
        Assert.False(src.IsWriteProtected);
        Assert.Null(src.Identity);
    }

    [Fact]
    public void Browse_ACopiedCardInATempFolder_AnchorsThere()
        => Assert.Equal(@"C:\Temp\card",
            Ok(Validator.Validate(@"C:\Temp\card\DCIM\DJI_001", null, S, CardAt(@"C:\Temp\card"), new FakePathFacts(), A)).Root);

    [Fact]
    public void Browse_WithoutDcim_IsRefusedWithTheRightReason()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(@"C:\Temp\loose\DJI_20260927140627_0128_D.MP4", 100, T);
        fs.AddFile(@"C:\Temp\empty\notes.txt", 1, T);
        Assert.Equal(CardSourceValidator.PickTopFolder, Refused(Validator.Validate(@"C:\Temp\loose", null, S, fs, new FakePathFacts(), A)));
        Assert.Equal(CardSourceValidator.NoDcim, Refused(Validator.Validate(@"C:\Temp\empty", null, S, fs, new FakePathFacts(), A)));
    }

    [Theory]
    [InlineData(V)]                                       // equals the video root
    [InlineData(V + @"\2026\card copy")]                  // inside it
    [InlineData(@"C:\Users\u\OneDrive\Pictures")]         // contains it
    [InlineData(@"F:\Photos")]                            // equals the photo root
    [InlineData(@"F:\Photos\sub")]
    [InlineData(@"F:\")]
    [InlineData(@"D:\Old Photos")]                        // a previous photo root
    [InlineData(@"D:\Old Photos\x")]
    [InlineData(@"D:\")]
    [InlineData(V + @"\.uas-sort")]                       // the ledger folder, browsed straight into
    [InlineData(V + @"\.uas-sort\x")]
    [InlineData(A)]                                       // app data
    [InlineData(A + @"\drafts")]
    [InlineData(@"C:\Users\u\AppData\Local")]
    public void Overlaps_WithLibraryLedgerOrAppData_AreRefused(string anchor)
        => Assert.Equal(CardSourceValidator.PartOfLibrary,
            Refused(Validator.Validate(anchor, null, S, CardAt(anchor), new FakePathFacts(), A)));

    [Fact]
    public void Overlaps_WithACloudSyncRoot_AreRefused()
    {
        var facts = new FakePathFacts();
        facts.AddSyncRoot(@"G:\Sync");
        Assert.Equal(CardSourceValidator.PartOfLibrary, Refused(Validator.Validate(@"G:\Sync\card", null, S, CardAt(@"G:\Sync\card"), facts, A)));
        Assert.Equal(CardSourceValidator.PartOfLibrary, Refused(Validator.Validate(@"G:\", null, S, CardAt(@"G:\"), facts, A)));
        Assert.Equal(@"H:\", Ok(Validator.Validate(@"H:\", null, S, CardAt(@"H:\"), facts, A)).Root);
    }

    [Fact]
    public void Canonicalisation_SeesThroughSubstAndJunctions()
    {
        var facts = new FakePathFacts();
        facts.AddAlias(@"S:\", V + @"\2026");          // subst S: into the library
        facts.AddAlias(@"C:\Temp\link", A);            // junction into app data
        Assert.Equal(CardSourceValidator.PartOfLibrary, Refused(Validator.Validate(@"S:\", null, S, CardAt(@"S:\"), facts, A)));
        Assert.Equal(CardSourceValidator.PartOfLibrary, Refused(Validator.Validate(@"C:\Temp\link", null, S, CardAt(@"C:\Temp\link"), facts, A)));
    }

    [Fact]
    public void Validation_OpensNothing()
    {
        var fs = CardAt(@"E:\");
        Ok(Validator.Validate(@"E:\DCIM\DJI_001", null, S, fs, new FakePathFacts(), A));
        Assert.Empty(fs.GuardLog);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CardSourceValidatorTests"`
Expected: build FAILS (CS0246: `CardSourceValidator` not found).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Card/CardSourceValidator.cs
namespace UasSort.Core.Card;

/// <summary>Card source policy (Ref §4.3): anchor at the folder holding DCIM, canonicalise through IPathFacts, refuse any
/// overlap with the library, the ledger folder, app data or a cloud sync root. Listings only; opens nothing.</summary>
public sealed class CardSourceValidator : ICardSourceValidator
{
    public const string PickTopFolder = "Pick the card's top folder (the one containing DCIM)";
    public const string NoDcim = "No DCIM folder here";
    public const string PartOfLibrary = "This is part of your library (or a synced folder); uas-sort only offloads from cards.";

    private static readonly IReadOnlySet<string> NoExclusions = ImmutableHashSet<string>.Empty;

    public CardSourceCheck Validate(string chosenPath, VolumeInfo? detected, Settings s, IDirectoryLister lister,
                                    IPathFacts facts, string appDataDir)
    {
        ArgumentNullException.ThrowIfNull(chosenPath);
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(appDataDir);

        // 1. Anchor.
        var chosen = PathRules.Normalize(chosenPath);
        string? anchor = null;
        for (var dir = chosen; dir is not null; dir = PathRules.Parent(dir))
        {
            if (HoldsDcim(lister.Enumerate(dir, recurse: false, NoExclusions)))
            {
                anchor = dir;
                break;
            }
        }
        if (anchor is null)
        {
            var here = lister.Enumerate(chosen, recurse: false, NoExclusions);
            var hasMedia = here.Entries.Any(e => !e.IsDirectory && CardClassifier.MediaExtensions.Contains(Extension(e.FullPath)));
            return new SourceRefused(hasMedia ? PickTopFolder : NoDcim);
        }

        // 2. Canonicalise.
        var root = PathRules.Normalize(facts.Canonical(anchor));

        // 3. Refuse overlaps.
        var guarded = new List<string> { s.VideoRoot, s.PhotoRoot, LedgerPaths.For(s.VideoRoot), appDataDir };
        guarded.AddRange(s.PreviousPhotoRoots);
        foreach (var g in guarded)
            if (PathRules.Overlaps(root, CanonicalOrAsWritten(facts, g))) return new SourceRefused(PartOfLibrary);
        if (facts.InSyncRoot(root)) return new SourceRefused(PartOfLibrary);
        foreach (var syncRoot in facts.SyncRoots())
            if (PathRules.Overlaps(root, CanonicalOrAsWritten(facts, syncRoot))) return new SourceRefused(PartOfLibrary);

        return new SourceOk(new CardSource(root, detected?.Identity, IsBrowsedFolder: detected is null,
                                           IsWriteProtected: detected?.IsReadOnlyVolume ?? false));
    }

    private static bool HoldsDcim(ListingResult listing)
        => listing.Entries.Any(e => e.IsDirectory && string.Equals(PathRules.FileName(e.FullPath), "DCIM", StringComparison.OrdinalIgnoreCase));

    private static string CanonicalOrAsWritten(IPathFacts facts, string path)
    {
        try
        {
            return PathRules.Normalize(facts.Canonical(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return PathRules.Normalize(path);   // e.g. D: unplugged: compare the configured path as written
        }
    }

    private static string Extension(string path)
    {
        var name = PathRules.FileName(path);
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[(dot + 1)..];
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CardSourceValidatorTests"`
Expected: PASS (2 + 3 + 1 + 1 + 14 + 3 facts/rows = all green).
Then run the whole suite: `dotnet test --solution uas-sort.slnx` — expected: every test passes, no warnings.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Card/CardSourceValidator.cs tests/UasSort.Testing/FakePathFacts.cs tests/UasSort.Core.Tests/Card/CardSourceValidatorTests.cs
git commit -m "feat: add CardSourceValidator policy with anchoring and overlap refusal

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

## Part 02 — Produces (summary)

All in namespace `UasSort.Core` unless noted; files under `src/UasSort.Core/`. Names, namespaces and owners match `00-interfaces.md`.

- **Namespaces and global usings** (Task 02.1): `src/UasSort.Core/Namespaces.cs` (an internal `NamespaceMarker` in each of `UasSort.Core.Card`, `.Cleanup`, `.Config`, `.Editing`, `.Geo`, `.Json`, `.Ledger`, `.Library`, `.Media`, `.Naming`, `.Offload`, `.Planning`, `.Time`); the identical `GlobalUsings.Core.cs` in `src/UasSort.Core`, `src/UasSort.Review`, `src/UasSort.Platform`, `src/UasSort.App`, `src/UasSort.Cli`, `tests/UasSort.Testing`, `tests/UasSort.Core.Tests`, `tests/UasSort.Review.Tests`, `tests/UasSort.Platform.Tests`; `tests/UasSort.Testing/GlobalUsings.cs` (`Microsoft.Extensions.Time.Testing`) and `tests/UasSort.Core.Tests/GlobalUsings.cs` (`Microsoft.Extensions.Time.Testing`, `UasSort.Testing`). Core has no project-specific `GlobalUsings.cs`. Part 01 canaries stay in `UasSort.Core.StackProof`.
- **Path and ledger helpers** (`Guard/PathRules.cs`, `Ledger/LedgerPaths.cs`): `PathRules` (`Normalize`, `Equal`, `IsSameOrUnder`, `IsStrictlyUnder`, `Overlaps`, `Parent`, `FileName`, `Join`, `RelativeCardPath`, `SetContains`); `LedgerPaths` (`FolderName`, `FilePattern`, `For`, `OwnFile`, `BackupDir`, `IsLedgerFileName`).
- **Primitives** (`Model/Primitives.cs`): `ItemId` (JSON as a string), `GeoPoint` (JSON `[lon,lat]`), `Distance` (`Miles`, `FromMiles`, `EarthRadiusMeters`), `ByteRange`, `ItemKind`, `SetKind`, `FileKey` (`NormalizeName`, `Of`, `OfPath` — the last segment after `/` or `\`, then `Of`).
- **Card and listing model** (`Model/Card.cs`, `Ports/FileSystemRecords.cs`): `VolumeInfo`, `FsEntry` (native `\` `RelPath`), `ListingResult`, `EntryClass`, `CardEntry` (`/` `RelPath`), `CardIdentity`, closed `MediaUnit` (`VideoUnit`, `PhotoUnit`, `SetUnit`), `CardSource` (`DraftKey`), `CardInventory`, `ScanWarning`, `CardCandidate`, `SourceOk`, `SourceRefused`, union `CardSourceCheck`.
- **Probe, clock and item model** (`Model/Probes.cs`, `Model/Clock.cs`, `Model/Items.cs`): `GpsSource`, `GpsFix`, `NoFixReason`, `NoFix`, union `GpsProbe`, `Mp4Info`, `SessionKey.SameSession`, `StillInfo`, `RawItem`; `ClockMode`, `StoredClockMode`, `ClockSample`, **partial** `ClockModel` (Part 04 adds `ToUtc`, `OffsetAt`), `ClockSummary`, `ClockChange`; `TimeSource`, `TzSource`, `ItemFlags`, `ItemTime`, `NewReason`, `Evidence`, `DecisionKind`, `LibraryFolderRef`, closed `Newness` (`IsNew`, `Imported`, `Decided`, `ProbablyImported`, `Conflict`), `Item`.
- **Library, ledger, settings, geo** (`Model/Library.cs`, `Model/Ledger.cs`, `Model/LedgerRecords.cs`, `Model/Settings.cs`, `Model/Geo.cs`): `LocationSource`, **partial** `LibraryFolder` (Part 05 adds `DaysIn`), `RootListing`, `LibraryListings`, `LibraryFile`, `SetFolderListing`, **partial** `LibraryIndex` (Part 05 adds every member); `VerifyKind`, `FolderSource`, `LedgerFile`, `LedgerSet`, `LedgerDecision`, `LedgerFolder`, `LedgerRun`, `LedgerCardDelete`, `LedgerParseIssue`, `LedgerFolderState`, `LedgerFolderStatus`, `LedgerSnapshot` (`Files`: strongest verify per key, Unbuffered > Cached > NameSize, latest among equals), `ILedgerWriter`; closed `LedgerRecord` with discriminators `file`/`folder`/`seen`/`decision`/`revoke`/`run`/`torn`/`cardDelete` (`FileRecord` … `CardDeleteRecord`), `RunCard`, `RunRoots`; `Settings` (no ledger property), `MapSettings`, `LayoutSettings`, `SettingsLoad`, `Draft`; `TzLookup`, `PlaceClass`, `PlaceHit`, `ResolvedItem`.
- **Plan and edits** (`Model/Plan.cs`, `Model/Edits.cs`, `Ports/IPlanDeriver.cs`): `Tuning`, `GroupId`, `BoundaryCause`, `Boundary`, `DaySplit`, `Confidence`, closed `GroupTarget` (`AlreadyImported`, `NothingToCopy`, `NewFolder`, `Append`, `SkipGroup`), `CrossDayHint`, `DescSource`, `Suggestion`, `PinState`, `VideoGroup`, `SetResolution`, `SetPlacement`, `PhotoDay`, `IssueSeverity`, `IssueCode` (Part 06 Task 06.1 appends `NothingNew`), `Issue`, `QuickFix`, `SessionFlags`, `ScanResult`, `PlanBase`, `Plan`, `GroupDraft`, `ClusterResult`; closed `PlanEdit` (`Merge`, `SplitBefore`, `MoveToNewGroup`, `MoveToGroup`, `Rename`, `Retarget`, `SetIncluded`, `SetDayIncluded`; discriminators camelCase), closed `TargetChoice` (`AutoTarget` "auto", `NewFolderTarget` "newFolder", `AppendTo` "appendTo", `SkipTarget` "skip"), `RejectReason`, `Applied`, `Rejected`, union `EditResult`; `IPlanDeriver`.
- **Offload, audit, cleanup, reports** (`Model/Offload.cs`, `Model/Audit.cs`, `Model/Cleanup.cs`, `Model/Reports.cs`): `DestRoot`, `CopyJob`, `VerifyMode`, `CopyPhase`, `HashMatch`, `HashMismatch`, union `VerifyResult`, `Renamed`, `TargetExists`, union `RenameResult`, closed `CopyOutcome` (8 cases), `OffloadBatch`, `FolderPlan`, `StopReason`, `OffloadResult`, `VolumeNeed`, `PreflightReport.CanStart`, `Ejected`, `EjectRefused`, union `EjectResult`, `ScanPhase`, `ScanProgress`, `OffloadProgress`; `AuditCategory`, `VerdictLevel`, `AuditLine`, `UnitAudit`, `FormatVerdict`; every cleanup type of Ref §3 including `FreeSpaceGoal.TargetFreeBytes`, **partial** `CleanupPlan` with its internal 18-argument constructor (`planId, card, cardRoot, inventoryHash, cameraModel, request, …, fingerprint`) and the properties `PlanId`, `Card`, `CardRoot`, `InventoryHash`, `CameraModel`, `Request`, `SpaceBefore`, `Delete`, `NotInLibraryInScope`, `Rows`, `Undecided`, `NotDeletable`, `Cutoff`, `FileCount`, `AllocatedBytes`, `ExpectedFreeAfter`, `Shortfall`, `Fingerprint`, `ConfirmedCleanupPlan` (internal ctor deriving case-insensitive `FilePaths`, `SetFolders` through `PathRules.Join(plan.CardRoot, rel)`, and `NotInLibraryConfirmed`; this derivation is canonical and stays here), closed `CleanupOutcome` (7 cases), `CleanupStop`, `CleanupEnvironment`, `CleanupResult`, `CleanupProgress`; `ReportLine`, `OffloadReport`, `CleanupReportLine`, `CleanupReport`. Part 08 adds only `CleanupPlan.Confirm` and `CleanupPlanner.Build`. `UasSort.Core.csproj` carries two `InternalsVisibleTo` items, for `UasSort.Core.Tests` and `UasSort.Testing` (no assembly-attribute file anywhere).
- **Ports** (`Ports/Ports.cs`, `Ports/ReviewServices.cs`): `IVolumeProvider`, `IDirectoryLister`, `ICardSourceValidator`, `IPathFacts`, `ICardReader`, `ICardReaderFactory`, `ICardEraserFactory`, `ICardEraser`, `EraseOk`, `EraseError`, union `EraseResult`, `IFileOps`, `ILedgerStore`, `ISettingsStore`, `IDraftStore`, `IReportStore`, `IAppAssets`, `IPowerRequest`, `IOffloadLock`, `IDeviceEject`, `IShellLauncher`, `ITimeZoneResolver`, `IPlaceIndex`, `IThumbnailSource`, `IUiDispatcher`, `DialogResult`, `DialogRequest`, `IDialogService`.
- **Guard** (`Guard/GuardTypes.cs`, `Guard/IoGuardPolicy.cs`, `Guard/UnsafeIoException.cs`): `IoOp`, `GuardContext`, `CardDeleteViolation`, `GuardAllow`, `GuardUnsafe`, `GuardCloudOnly`, `GuardHydration`, union `GuardDecision`, `IoGuardPolicy.Check` plus the `FileAttribute*` constants and `TempSuffix`; `UnsafeIoException` (not an `IOException`).
- **JSON** (namespace `UasSort.Core.Json`): `GeoPointJsonConverter`, `ItemIdJsonConverter`, `LedgerJsonContext` (`LedgerRecord`, compact lines), `CoreJsonContext` (`Settings`, `Draft`, `PlanEdit`, `TargetChoice`, `OffloadReport`, `CleanupReport`, `GeoPoint`; indented). All camelCase, enums as names, `AllowOutOfOrderMetadataProperties`.
- **Card units** (namespace `UasSort.Core.Card`): `CardClassifier(TimeProvider)` (`Classify`, static `MediaExtensions`, static `ComputeInventoryHash`); static `CardDetector` (`Detect`, `SingleDjiCard`, `NotACard`); `CardSourceValidator : ICardSourceValidator` (`PickTopFolder`, `NoDcim`, `PartOfLibrary`).
- **Test support** (namespace `UasSort.Testing`, `tests/UasSort.Testing/`): `HydrationViolation`, `FakeFaults`, `FakeGuardCall`, `FakeFileSystem` (tree, lister, `Guard`, `OpenRead`, `OpenAppend`, `CreateDirectory`, `SetPinned`, card volumes, `GuardLog`, `CardDeleteViolations`, `HydrationViolations`, `AssertNoViolations`), `FakeLayout` (standard paths, `CardId`, `CardSpace`, `Context`, `Settings`, `NewFileSystem`), `FakeCardReaderFactory`, `FakeCardReader`, `FakeCardEraserFactory` (`VolumeVerified`, `OpenCount`), `FakeCardEraser`, `FakeFileOps`, `FakePathFacts`. These are the only fakes of their kind, each one non-partial class declared once in its file (`FakeFaults.cs`, `FakeFileSystem.cs`, `FakeCard.cs`, `FakeFileOps.cs`, …); Parts 07 and 08 add members in place there (registry Testing table). Clocks in tests are `Microsoft.Extensions.Time.Testing.FakeTimeProvider` (there is no `FixedTimeProvider`). Internal to Core.Tests: `TestCleanupPlans` (`tests/UasSort.Core.Tests/Support/`, namespace `UasSort.Core.Tests.Support`, 18-argument ctor) and `IoGuardPolicyTests.Context`/`Kind` with the path constants.
