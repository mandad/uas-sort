# Part 07 — Offload engine and audit

**Goal.** Build the Commit half of Core: `OffloadCompiler` (plan → jobs), `Preflight` (the blocking list, the actions and the acknowledgements; writes nothing), `CopyEngine` (the sequential per-file protocol, steps 0–12, every outcome row, the post-loop flush and `VolumesNeedingSafeRemoval`), the Commit ledger records (`file`, `folder`, `seen`, `run`), `CardAudit` (identity re-check, re-list diff, per-file categories, worst-of per unit, verdict level and headline), the Verdict-page decisions (`assumedImported` / `dismissed`, confirmation text, `revoke`), the offload report, and `CommitSession`, which runs Start offload → copy → ledger tail → audit → report in the order of Ref §4.4. Implements Ref §10.1–§10.5, the offload rows of §11 and §12, and the §13 test groups "Fault injection" (every §10.3 row), "Audit" and the offload half of the fake-FS tripwire (Ref §14 step 7).

**Depends on:** Part 01 (solution, test projects, pins), Part 02 (the §3 model incl. `CopyJob`, the `CopyOutcome` cases, `OffloadBatch`, `FolderPlan`, `OffloadResult`, `StopReason`, `AuditCategory`, `VerdictLevel`, `FormatVerdict`, `UnitAudit`, `AuditLine`, the ledger record types, `ReportLine`/`OffloadReport`, `VolumeNeed`/`PreflightReport`, `OffloadProgress`; the ports `ICardReader`, `IFileOps`, `IDirectoryLister`, `ILedgerStore`, `ILedgerWriter`, `IPowerRequest`, `IOffloadLock`, `IReportStore`, `IThumbnailSource`, `IVolumeProvider`; `PathRules`, `FileKey.NormalizeName`/`OfPath`, `LedgerPaths`, `GuardContext`, `UnsafeIoException`, and the shared fakes `FakeFileSystem`, `FakeFaults`, `FakeLayout`, `FakeCardReader`, `FakeFileOps`), Part 05 (`LibraryIndex.Build`/`Match`/`SameNameOtherSize`, `LedgerSnapshot`, `LedgerCodec`, `LedgerFolderFacts`/`LedgerFolderStatusBuilder`, `LedgerLoader`, `LedgerSnapshots`), Part 06 (`Plan`, `PlanBase`, `VideoGroup`, `Item`, `Newness` cases, `SetPlacement`, `Issue`, and the shared fake `UasSort.Testing.FakeLedgerStore` that Task 07.4 completes).

**Conventions used in this part.**
- Core code of this part is in namespace `UasSort.Core.Offload` (folder `src/UasSort.Core/Offload/`). Every Core namespace (`UasSort.Core` for the shared model, ports, guard types and exceptions; `UasSort.Core.Ledger`, `.Library`, `.Offload`, …) comes from the fixed `GlobalUsings.Core.cs` of `00-interfaces.md` (Part 02 Task 02.1), and Core.Tests also imports `UasSort.Testing` and `Microsoft.Extensions.Time.Testing` globally; the explicit `using` lines kept in the files below are redundant and harmless. `UasSort.Testing.Offload` is imported per file.
- Shared fakes are in flat `UasSort.Testing` (files directly under `tests/UasSort.Testing/`): Part 02's `FakeFileOps` and Part 06's `FakeLedgerStore` are extended in place by Task 07.4, which also adds `FakeLedgerWriter`, `FakeOffloadLock`, `FakePowerRequest`, `FakeThumbnails`, `MemReportStore`, `FakeVolumeProvider` and the generic `ListProgress` there. `tests/UasSort.Testing/Offload/` (namespace `UasSort.Testing.Offload`) holds only this part's scenario builders `OffloadPlanBuilder`, `OffloadRig`, `OffloadVolumes`. Tests are in `tests/UasSort.Core.Tests/Offload/` (namespace `UasSort.Core.Tests.Offload`).
- Inside `namespace UasSort.Core.Tests.Offload` the simple name `Card` resolves to the namespace `UasSort.Core.Card` (enclosing namespaces are searched before `using static` imports), so tests always write `OffloadPlanBuilder.Card` for the builder's pinned card identity.
- Card-relative paths use `/` (Part 02 convention); full paths are Windows paths. Path text is handled by `OffloadPaths` (Task 07.1), which delegates to Part 02's `PathRules` and `FileKey.NormalizeName` wherever they cover the case.
- Every place that catches IO failures uses `Failures.IsIo` (Task 07.1). `UnsafeIoException` is not an `IOException` (Part 02), so it is always caught in its own clause and becomes `InternalSafetyStop`.
- Fault injection uses the shared fakes: `FakeFileSystem` (tree, guard tripwire, `FakeFaults`), `FakeCardReader` (card faults: `TransientCardReadError`, `PersistentCardReadError`, `CardVanishesAfterBytes`, `CardRemoved`, `IdentityOnCall`) and `FakeLayout` as Part 02 wrote them; Part 02's `FakeFileOps` (the fake `IFileOps` that honours `FakeFaults.DiskFullAfterBytes`, `CorruptVerify`, `UnbufferedUnsupported`, `TargetAppearsBeforeRename`, `SizeAfterRename` and `LostRoots`) gains the offload hooks in Task 07.4; Part 06's `FakeLedgerStore` (the one fake `ILedgerStore`: in memory, or backed by the fake FS where `FakeFaults.AppendFails` applies) gains its write half and `FakeLedgerWriter` (records kept as objects, and as `LedgerCodec` lines when on the fake FS) in Task 07.4. No second fake of either kind exists (registry decision 6).
- Commands run natively on Windows (`dotnet.exe`) from the repo root `C:\dev\uas-sort`, in PowerShell 7 or Claude Code's Bash tool (Git Bash); only if driving the build from WSL (optional), use `tools/r.sh` with the same arguments.

---

### Task 07.1: Offload path helpers and failure classification

**Files:**
- Create: `src/UasSort.Core/Offload/OffloadPaths.cs`
- Create: `src/UasSort.Core/Offload/Failures.cs`
- Test: `tests/UasSort.Core.Tests/Offload/OffloadPathsTests.cs`

**Interfaces:**

```csharp
// Consumes (Part 02): PathRules.Normalize/Equal/IsSameOrUnder/FileName/Parent; FileKey.NormalizeName(string), FileKey.OfPath(string, long);
//   UnsafeIoException.
// Produces (defined here):
public static class OffloadPaths
{
    public const string TempSuffix = ".uas-sort.tmp";
    public const int MaxTempPathLength = 400;
    public static string Join(string dir, string relative);            // backslashes; relative may use '/' or '\'
    public static string FileName(string path);                         // last segment of a '/' or '\' path
    public static string DirectoryOf(string fullPath);                  // text before the last '\' ("" if none)
    public static string TempOf(string finalPath);                      // finalPath + ".uas-sort.tmp"
    public static bool IsTemp(string path);
    public static string VolumeRoot(string fullPath);                   // "C:\", "D:\", @"\\nas\share\"; strips \\?\
    public static string DriveLabel(string volumeRoot);                 // "E:\" -> "E:"
    public static bool Same(string a, string b);                        // PathRules.Equal
    public static bool IsUnder(string path, string root);               // PathRules.IsSameOrUnder
    public static string NormRel(string relPath);                       // '\' -> '/'
    public static string NormName(string fileName);                     // FileKey.NormalizeName(FileName(fileName))
    public static FileKey Key(string pathOrName, long size);             // FileKey.OfPath(pathOrName, size)
    public static string WithCopyNumber(string fileName, int n);        // "X.MP4", 2 -> "X (2).MP4"
    public static ImmutableArray<string> NewFolderDirs(string videoRoot, string folder); // every dir from below videoRoot down to folder
    public static string Gb(long bytes);                                // decimal GB, 2 places: "32.47 GB"
}
internal static class Failures
{
    public static bool IsIo(Exception e);        // IOException or UnauthorizedAccessException, never UnsafeIoException
    public static bool IsDiskFull(Exception e);  // IOException with Win32 code 0x70 (ERROR_DISK_FULL) or 0x27 (ERROR_HANDLE_DISK_FULL)
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/OffloadPathsTests.cs
using UasSort.Core;
using UasSort.Core.Offload;

namespace UasSort.Core.Tests.Offload;

public class OffloadPathsTests
{
    [Fact]
    public void Join_UsesBackslashes()
        => Assert.Equal(@"C:\V\2026\2026-09\2026-09-27 Zachar Bay",
                        OffloadPaths.Join(@"C:\V\", "2026/2026-09/2026-09-27 Zachar Bay"));

    [Theory]
    [InlineData(@"C:\Lib\x.mp4", @"C:\")]
    [InlineData(@"d:\Photos\a.dng", @"D:\")]
    [InlineData(@"\\?\D:\x\y.dng", @"D:\")]
    [InlineData(@"\\nas\share\v\x.mp4", @"\\nas\share\")]
    public void VolumeRoot_FindsTheVolume(string path, string expected)
        => Assert.Equal(expected, OffloadPaths.VolumeRoot(path));

    [Fact]
    public void DriveLabel_DropsTheSeparator() => Assert.Equal("E:", OffloadPaths.DriveLabel(@"E:\"));

    [Theory]
    [InlineData("DCIM/DJI_001/DJI_1 (2).MP4", "dji_1.mp4")]
    [InlineData("PANO_0001.DNG", "pano_0001.dng")]
    [InlineData(@"C:\V\A (12).dng", "a.dng")]
    public void NormName_MatchesTheLibraryKeyRule(string path, string expected)
        => Assert.Equal(expected, OffloadPaths.NormName(path));

    [Fact]
    public void Key_UsesNormNameAndSize()
        => Assert.Equal(new FileKey("x.mp4", 7), OffloadPaths.Key("DCIM/DJI_001/X.MP4", 7));

    [Theory]
    [InlineData("DJI_1.MP4", 2, "DJI_1 (2).MP4")]
    [InlineData("noext", 3, "noext (3)")]
    public void WithCopyNumber_InsertsBeforeTheExtension(string name, int n, string expected)
        => Assert.Equal(expected, OffloadPaths.WithCopyNumber(name, n));

    [Fact]
    public void NewFolderDirs_ListsYearMonthAndFolder()
        => Assert.Equal(
            [@"C:\V\2026", @"C:\V\2026\2026-09", @"C:\V\2026\2026-09\2026-09-27 Z"],
            OffloadPaths.NewFolderDirs(@"C:\V", @"C:\V\2026\2026-09\2026-09-27 Z").ToArray());

    [Fact]
    public void NewFolderDirs_OutsideTheVideoRoot_IsJustTheFolder()
        => Assert.Equal([@"D:\Photos\001_0087"], OffloadPaths.NewFolderDirs(@"C:\V", @"D:\Photos\001_0087").ToArray());

    [Theory]
    [InlineData(@"C:\V\a", @"C:\V", true)]
    [InlineData(@"c:\v\A", @"C:\V\", true)]
    [InlineData(@"C:\V2\a", @"C:\V", false)]
    public void IsUnder_RespectsSeparators(string path, string root, bool expected)
        => Assert.Equal(expected, OffloadPaths.IsUnder(path, root));

    [Fact]
    public void TempOf_And_IsTemp()
    {
        Assert.Equal(@"C:\V\x.MP4.uas-sort.tmp", OffloadPaths.TempOf(@"C:\V\x.MP4"));
        Assert.True(OffloadPaths.IsTemp(@"C:\V\x.MP4.UAS-SORT.TMP"));
        Assert.False(OffloadPaths.IsTemp(@"C:\V\x.MP4"));
    }

    [Fact]
    public void Gb_TwoDecimals() => Assert.Equal("32.47 GB", OffloadPaths.Gb(32_473_741_824));
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*OffloadPathsTests"`
Expected: build FAILS with CS0246 / CS0103 (`OffloadPaths` does not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Offload/OffloadPaths.cs
using System.Collections.Immutable;
using System.Globalization;

namespace UasSort.Core.Offload;

/// <summary>Pure path text for the offload (Windows syntax; never touches the file system).</summary>
public static class OffloadPaths
{
    public const string TempSuffix = ".uas-sort.tmp";
    public const int MaxTempPathLength = 400;

    public static string Join(string dir, string relative)
        => dir.TrimEnd('\\', '/') + "\\" + relative.Replace('/', '\\').Trim('\\');

    public static string FileName(string path)
    {
        int i = path.LastIndexOfAny(['\\', '/']);
        return i < 0 ? path : path[(i + 1)..];
    }

    public static string DirectoryOf(string fullPath)
    {
        int i = fullPath.LastIndexOf('\\');
        return i < 0 ? "" : fullPath[..i];
    }

    public static string TempOf(string finalPath) => finalPath + TempSuffix;

    public static bool IsTemp(string path) => path.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase);

    public static string VolumeRoot(string fullPath)
    {
        var p = fullPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + fullPath[8..]
              : fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) ? fullPath[4..]
              : fullPath;
        if (p.Length >= 2 && p[1] == ':') return char.ToUpperInvariant(p[0]) + @":\";
        if (p.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = p[2..].Split('\\');
            if (parts.Length >= 2) return $@"\\{parts[0]}\{parts[1]}\";
        }
        return p;
    }

    public static string DriveLabel(string volumeRoot) => volumeRoot.TrimEnd('\\');

    public static bool Same(string a, string b) => PathRules.Equal(a, b);

    public static bool IsUnder(string path, string root) => PathRules.IsSameOrUnder(path, root);

    public static string NormRel(string relPath) => relPath.Replace('\\', '/');

    public static string NormName(string fileName) => FileKey.NormalizeName(FileName(fileName));

    public static FileKey Key(string pathOrName, long size) => FileKey.OfPath(pathOrName, size);

    public static string WithCopyNumber(string fileName, int n)
    {
        int dot = fileName.LastIndexOf('.');
        return dot <= 0 ? $"{fileName} ({n})" : $"{fileName[..dot]} ({n}){fileName[dot..]}";
    }

    public static ImmutableArray<string> NewFolderDirs(string videoRoot, string folder)
    {
        var root = PathRules.Normalize(videoRoot);
        var target = PathRules.Normalize(folder);
        if (!PathRules.IsSameOrUnder(target, root) || PathRules.Equal(target, root)) return [target];
        var dirs = ImmutableArray.CreateBuilder<string>();
        var current = root.TrimEnd('\\');
        foreach (var part in target[(root.TrimEnd('\\').Length + 1)..].Split('\\'))
        {
            current += "\\" + part;
            dirs.Add(current);
        }
        return dirs.ToImmutable();
    }

    public static string Gb(long bytes) => (bytes / 1e9).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
}
```

```csharp
// src/UasSort.Core/Offload/Failures.cs
namespace UasSort.Core.Offload;

/// <summary>How the offload tells IO failures apart (Ref §10.3 outcome table).</summary>
internal static class Failures
{
    /// <summary>An ordinary IO failure. UnsafeIoException (a guard refusal) is never one: it stops the run.</summary>
    public static bool IsIo(Exception e)
        => (e is IOException || e is UnauthorizedAccessException) && e is not UnsafeIoException;

    /// <summary>ERROR_DISK_FULL (0x70) or ERROR_HANDLE_DISK_FULL (0x27), as the low word of the HRESULT.</summary>
    public static bool IsDiskFull(Exception e) => e is IOException && (e.HResult & 0xFFFF) is 0x70 or 0x27;
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*OffloadPathsTests"`
Expected: PASS (19 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/OffloadPaths.cs src/UasSort.Core/Offload/Failures.cs tests/UasSort.Core.Tests/Offload/OffloadPathsTests.cs
git commit -m "feat: add offload path helpers and failure classification

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.2: Test plan builder and `OffloadCompiler.Compile`

**Files:**
- Create: `tests/UasSort.Testing/Offload/OffloadPlanBuilder.cs`
- Create: `src/UasSort.Core/Offload/OffloadCompiler.cs`
- Test: `tests/UasSort.Core.Tests/Offload/OffloadCompilerTests.cs`

**Interfaces:**

```csharp
// Consumes (Ref §3 constructors, Parts 02/05/06): Plan, PlanBase, ScanResult, CardInventory, CardSource, CardIdentity, CardEntry,
//   EntryClass, VideoUnit, PhotoUnit, SetUnit, RawItem, Item, ItemTime, TimeSource, TzSource, ItemFlags, GpsFix, GpsSource,
//   Newness cases (IsNew, Imported, Decided, ProbablyImported, Conflict), VideoGroup, GroupId, GroupTarget cases (NewFolder, Append,
//   AlreadyImported, NothingToCopy, SkipGroup), LibraryFolderRef, SetPlacement, SetResolution, Settings, MapSettings, LayoutSettings,
//   ClockModel, ClockSummary, LedgerSnapshot, LedgerFile, LedgerDecision, LedgerFolder, LedgerFolderStatus, LibraryListings,
//   RootListing, ListingResult, FsEntry, LibraryIndex.Build/Match/SameNameOtherSize (Part 05), Issue, Tuning; OffloadPaths (07.1);
//   FakeLayout.VideoRoot/PhotoRoot/CardId (Part 02, UasSort.Testing).
// Produces:
namespace UasSort.Testing.Offload;
public sealed class OffloadPlanBuilder                                    // (defined here) builds a Plan straight from Ref §3 records
{
    public const string CardRoot = @"E:\";   public const string Tz = "America/Anchorage";
    public static readonly CardIdentity Card;                             // FakeLayout.CardId (0x1A2B3C4D, null, "exFAT", 256_060_514_304)
    public static readonly DateTime T0;                                   // 2026-09-27T18:00:00Z
    public static readonly GeoPoint Zachar;                               // (57.5368, -153.7484)
    public string VideoRoot { get; }  public string PhotoRoot { get; }    // FakeLayout.VideoRoot / FakeLayout.PhotoRoot by default
    public IReadOnlyList<CardEntry> Entries { get; }
    public OffloadPlanBuilder WithPhotoRoot(string root); CopyJpgTwin(bool on); RootsConfirmed(bool on);
    public OffloadPlanBuilder LedgerState(LedgerFolderState state); Warning(ScanWarning w); Issue(Issue i);
    public CardEntry Entry(string relPath, long size, EntryClass cls, string? rule = null, uint attributes = 0x20, DateTime? mtimeUtc = null);
    public ItemId Video(string name, long size, DateTime captureUtc, Newness? newness = null, ItemFlags flags = ItemFlags.None,
                        GeoPoint? gps = null, bool included = true, bool hasTrinf = false, string? probeError = null);
    public ItemId Photo(string name, long size, DateTime captureUtc, Newness? newness = null, long? twinSize = null, bool included = true);
    public ItemId Set(string setName, (string Member, long Size)[] members, DateTime captureUtc, SetResolution resolution,
                      string? folderName = null, string[]? membersToCopy = null, Newness? newness = null, bool included = true);
    public CardEntry Unknown(string relPath, long size);   public CardEntry Skip(string relPath, long size, string rule);
    public OffloadPlanBuilder Group(GroupTarget target, GeoPoint? centroid, params ItemId[] videos);
    public OffloadPlanBuilder LibraryVideo(string relUnderVideoRoot, long size, uint attributes = 0x20);
    public OffloadPlanBuilder LibraryPhoto(string relUnderPhotoRoot, long size, uint attributes = 0x20);
    public OffloadPlanBuilder LedgerFile(string name, long size, VerifyKind verify, string? set = null);
    public OffloadPlanBuilder LedgerDecision(string name, long size, DecisionKind kind, string? set = null);
    public OffloadPlanBuilder LedgerFolder(string fullPath);
    public string NewFolderPath(string relUnderVideoRoot);                 // Join(VideoRoot, rel)
    public Plan Build();
}
namespace UasSort.Core.Offload;
public static partial class OffloadCompiler                               // Ref §4.2 OffloadCompiler.Compile(Plan)
{
    public static OffloadBatch Compile(Plan plan);                        // RunId = new GUID ("N")
    public static OffloadBatch Compile(Plan plan, string runId);          // (defined here) deterministic RunId for tests and CommitSession
}
```

Rules implemented (Ref §10.1, §7.4, §8.8; where the Ref is silent, the rule below is this plan's decision):
- Jobs, in order: groups by the earliest `CaptureUtc` of their videos (ties by anchor), videos inside a group by `(CaptureUtc, Id)`; then photos by `(CaptureUtc, Id)`; then sets by `(CaptureUtc, Id)`, members by name.
- A video is copied when its group targets `NewFolder`/`Append`, it is in `Plan.Included`, and its newness is `IsNew` or `Conflict`. A photo or set is copied when included and not `Imported`/`Decided` (a ticked `ProbablyImported` is copied). A set whose placement is `Imported` is skipped; `Resume` copies only `MembersToCopy`.
- A ticked `Conflict` gets the first `stem (n).ext`, n ≥ 2, that is free across the listings (`LibraryIndex.Match`/`SameNameOtherSize` of the same NormName), the ledger `file` records' `Dest` names and this batch; a DNG and its twin take the same n.
- `CreatesFolder` is true for NewFolder video jobs and for the members of a `Plain`/`DateSuffixed` set (its folder is new under the photo root); false for Append, flat photos and `Resume` sets.
- `SeenIfNotCopied`: every photo/set unit that is `IsNew` or `Conflict`, or is included (never `Imported`/`Decided`).
- `Folders`: one `FolderPlan` per group with at least one job (`Create` = NewFolder), plus one per `AlreadyImported(F)` group whose centroid is known and whose `F` has no ledger `folder` record — the "location learned from leftover clips on the card" record (a `FolderPlan` for a group with no jobs is the `cardLeftovers` kind, Task 07.11).
- The batch card is `Inventory.Source.Identity`; a null identity throws `InvalidOperationException` (nothing can be pinned).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Testing/Offload/OffloadPlanBuilder.cs
using System.Collections.Immutable;
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing;

namespace UasSort.Testing.Offload;

/// <summary>Builds a Plan directly from the Ref §3 records, for the offload and audit tests (no probes, no Planner).</summary>
public sealed class OffloadPlanBuilder
{
    public const string CardRoot = @"E:\";
    public const string Tz = "America/Anchorage";
    public static readonly CardIdentity Card = FakeLayout.CardId;          // (0x1A2B3C4D, null, "exFAT", 256_060_514_304)
    public static readonly DateTime T0 = new(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc);
    public static readonly GeoPoint Zachar = new(57.5368, -153.7484);

    private readonly List<Item> _items = [];
    private readonly List<CardEntry> _entries = [];
    private readonly List<MediaUnit> _units = [];
    private readonly HashSet<ItemId> _included = [];
    private readonly List<(GroupTarget Target, GeoPoint? Centroid, ItemId[] Videos)> _groups = [];
    private readonly Dictionary<ItemId, SetPlacement> _sets = [];
    private readonly List<FsEntry> _videoListing = [];
    private readonly List<FsEntry> _photoListing = [];
    private readonly Dictionary<FileKey, LedgerFile> _ledgerFiles = [];
    private readonly Dictionary<FileKey, LedgerDecision> _decisions = [];
    private readonly Dictionary<string, LedgerFolder> _ledgerFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ScanWarning> _warnings = [];
    private readonly List<Issue> _issues = [];
    private LedgerFolderState _ledgerState = LedgerFolderState.Ok;
    private bool _copyTwin = true;
    private bool _rootsConfirmed = true;

    public string VideoRoot { get; } = FakeLayout.VideoRoot;
    public string PhotoRoot { get; private set; } = FakeLayout.PhotoRoot;
    public IReadOnlyList<CardEntry> Entries => _entries;

    public OffloadPlanBuilder WithPhotoRoot(string root) { PhotoRoot = root; return this; }
    public OffloadPlanBuilder CopyJpgTwin(bool on) { _copyTwin = on; return this; }
    public OffloadPlanBuilder RootsConfirmed(bool on) { _rootsConfirmed = on; return this; }
    public OffloadPlanBuilder LedgerState(LedgerFolderState state) { _ledgerState = state; return this; }
    public OffloadPlanBuilder Warning(ScanWarning w) { _warnings.Add(w); return this; }
    public OffloadPlanBuilder Issue(Issue i) { _issues.Add(i); return this; }

    public string NewFolderPath(string relUnderVideoRoot) => OffloadPaths.Join(VideoRoot, relUnderVideoRoot);

    public CardEntry Entry(string relPath, long size, EntryClass cls, string? rule = null, uint attributes = 0x20, DateTime? mtimeUtc = null)
    {
        var m = mtimeUtc ?? T0;
        var e = new CardEntry(relPath, size, m, m, m, attributes, cls, rule);   // creation = last access = mtime, as FakeFileSystem lists it
        _entries.Add(e);
        return e;
    }

    public ItemId Video(string name, long size, DateTime captureUtc, Newness? newness = null, ItemFlags flags = ItemFlags.None,
                        GeoPoint? gps = null, bool included = true, bool hasTrinf = false, string? probeError = null)
    {
        var e = Entry($"DCIM/DJI_001/{name}", size, EntryClass.Video, mtimeUtc: captureUtc.AddSeconds(90));
        var id = new ItemId(e.RelPath);
        Add(new VideoUnit(id, e, hasTrinf), ItemKind.Video, name, size, captureUtc, newness, flags, gps, included, probeError);
        return id;
    }

    public ItemId Photo(string name, long size, DateTime captureUtc, Newness? newness = null, long? twinSize = null, bool included = true)
    {
        var p = Entry($"DCIM/DJI_001/{name}", size, EntryClass.Photo, mtimeUtc: captureUtc);
        CardEntry? twin = twinSize is long ts
            ? Entry($"DCIM/DJI_001/{Path.GetFileNameWithoutExtension(name)}.JPG", ts, EntryClass.PhotoTwin, mtimeUtc: captureUtc)
            : null;
        var id = new ItemId(p.RelPath);
        Add(new PhotoUnit(id, p, twin), ItemKind.Photo, name, size + (twinSize ?? 0), captureUtc, newness, ItemFlags.None, null, included, null);
        return id;
    }

    public ItemId Set(string setName, (string Member, long Size)[] members, DateTime captureUtc, SetResolution resolution,
                      string? folderName = null, string[]? membersToCopy = null, Newness? newness = null, bool included = true)
    {
        var entries = members
            .Select(m => Entry($"DCIM/PANORAMA/{setName}/{m.Member}", m.Size, EntryClass.SetMember, mtimeUtc: captureUtc))
            .ToImmutableArray();
        var id = new ItemId($"DCIM/PANORAMA/{setName}");
        Add(new SetUnit(id, SetKind.Panorama, setName, entries), ItemKind.Set, setName, members.Sum(m => m.Size), captureUtc,
            newness, ItemFlags.None, null, included, null);
        _sets[id] = new SetPlacement(id, folderName ?? setName, resolution,
                                     [.. membersToCopy ?? members.Select(m => m.Member).ToArray()]);
        return id;
    }

    public CardEntry Unknown(string relPath, long size) => Entry(relPath, size, EntryClass.Unknown);
    public CardEntry Skip(string relPath, long size, string rule) => Entry(relPath, size, EntryClass.Skip, rule);

    public OffloadPlanBuilder Group(GroupTarget target, GeoPoint? centroid, params ItemId[] videos)
    {
        _groups.Add((target, centroid, videos));
        return this;
    }

    public OffloadPlanBuilder LibraryVideo(string relUnderVideoRoot, long size, uint attributes = 0x20)
    {
        _videoListing.Add(new FsEntry(OffloadPaths.Join(VideoRoot, relUnderVideoRoot), relUnderVideoRoot.Replace('/', '\\'),
                                      false, size, T0.AddDays(-2), T0.AddDays(-2), T0.AddDays(-2), attributes));
        return this;
    }

    public OffloadPlanBuilder LibraryPhoto(string relUnderPhotoRoot, long size, uint attributes = 0x20)
    {
        _photoListing.Add(new FsEntry(OffloadPaths.Join(PhotoRoot, relUnderPhotoRoot), relUnderPhotoRoot.Replace('/', '\\'),
                                      false, size, T0.AddDays(-2), T0.AddDays(-2), T0.AddDays(-2), attributes));
        return this;
    }

    public OffloadPlanBuilder LedgerFile(string name, long size, VerifyKind verify, string? set = null)
    {
        var key = OffloadPaths.Key(name, size);
        _ledgerFiles[key] = new LedgerFile(key, $"DCIM/DJI_001/{name}", DestRoot.Video, OffloadPaths.Join(VideoRoot, name), null,
            verify, T0.AddDays(-1), null, null, null, null, null, set, "DESKTOP-A", "run-0");
        return this;
    }

    public OffloadPlanBuilder LedgerDecision(string name, long size, DecisionKind kind, string? set = null)
    {
        var key = OffloadPaths.Key(name, size);
        _decisions[key] = new LedgerDecision($"dec-{name}", key, kind, T0.AddDays(-1), "DESKTOP-A", set, "test");
        return this;
    }

    public OffloadPlanBuilder LedgerFolder(string fullPath)
    {
        var day = DateOnly.FromDateTime(T0);
        _ledgerFolders[fullPath] = new LedgerFolder(fullPath, OffloadPaths.FileName(fullPath), FolderSource.Created, null, day, day, Tz);
        return this;
    }

    private void Add(MediaUnit unit, ItemKind kind, string name, long bytes, DateTime captureUtc, Newness? newness,
                     ItemFlags flags, GeoPoint? gps, bool included, string? probeError)
    {
        var local = captureUtc.AddHours(-8);
        var raw = new RawItem(unit, kind, name, bytes, captureUtc.AddSeconds(90), null, null, null, probeError);
        var time = new ItemTime(captureUtc, kind == ItemKind.Video ? TimeSource.Mvhd : TimeSource.DroneClockZone, Tz, TzSource.Gps,
                                DateOnly.FromDateTime(local), local);
        GpsFix? fix = gps is GeoPoint g ? new GpsFix(g, null, 0, GpsSource.Exif, null) : null;
        _items.Add(new Item(raw, time, fix, null, flags, newness ?? new IsNew(NewReason.NoMatch, null)));
        _units.Add(unit);
        if (included) _included.Add(unit.Id);
    }

    public Plan Build()
    {
        var settings = new Settings(1, VideoRoot, PhotoRoot, [], 50, 1, StoredClockMode.Zone, "America/New_York", _copyTwin,
            new MapSettings("streets", "https://tiles.example/liberty", "https://tiles.example/dark", "https://imagery.example/{z}/{y}/{x}",
                            ImmutableDictionary<string, string>.Empty),
            new LayoutSettings(380, 0.45), _rootsConfirmed);
        var clock = new ClockModel(ClockMode.Zone, "America/New_York", [], TimeSpan.FromHours(-4), StoredClockMode.Zone, "America/New_York");
        var status = new LedgerFolderStatus(LedgerPaths.For(VideoRoot), _ledgerState, _ledgerState != LedgerFolderState.Missing,
            false, true, _ledgerState != LedgerFolderState.Unwritable, [], [], []);
        var ledger = new LedgerSnapshot(_ledgerFiles.ToImmutableDictionary(), ImmutableDictionary<string, ImmutableArray<LedgerSet>>.Empty,
            _decisions.ToImmutableDictionary(), ImmutableDictionary<FileKey, DateTime>.Empty,
            _ledgerFolders.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase), [], [], [], [], status);
        var listings = new LibraryListings(
            new RootListing(VideoRoot, DestRoot.Video, false, true, new ListingResult([.. _videoListing], [])),
            new RootListing(PhotoRoot, DestRoot.Photo, false, true, new ListingResult([.. _photoListing], [])),
            []);
        var library = LibraryIndex.Build(listings, ledger, clock);
        var inventory = new CardInventory(new CardSource(CardRoot, Card, false, false), T0, "0123456789abcdef",
                                          [.. _entries], [.. _units], "FC9113", [.. _warnings]);
        var scan = new ScanResult(inventory, [.. _items.Select(i => i.Raw)], library, ledger, clock, [.. _warnings], settings);
        var summary = new ClockSummary(ClockMode.Zone, "America/New_York", 0, "Drone clock: America/New_York", 0, [], []);
        var planBase = new PlanBase(scan, [.. _items], [], _sets.ToImmutableDictionary(), summary, null);

        var byId = _items.ToDictionary(i => i.Raw.Unit.Id);
        var groups = _groups.Select((g, n) =>
        {
            var videos = g.Videos.OrderBy(v => byId[v].Time.CaptureUtc).ThenBy(v => v.CardRelPath, StringComparer.Ordinal).ToImmutableArray();
            var start = videos.Min(v => byId[v].Time.LocalDate);
            var end = videos.Max(v => byId[v].Time.LocalDate);
            LibraryFolderRef? wall = g.Target switch { Append a => a.Folder, AlreadyImported ai => ai.Folder, _ => null };
            var description = g.Target switch
            {
                NewFolder nf => OffloadPaths.FileName(nf.RelPath) is var leaf && leaf.Length > 11 ? leaf[11..] : "",
                _ => wall?.Description ?? "",
            };
            return new VideoGroup(new GroupId(videos[0]), videos, g.Centroid, new Distance(0), start, end, wall, g.Target, null,
                description, DescSource.User, g.Target is NewFolder, null, [], [], [], false, n);
        }).ToImmutableArray();

        return new Plan(1, planBase, new Tuning(), groups, [], [.. _included], [.. _issues]);
    }
}
```

```csharp
// tests/UasSort.Core.Tests/Offload/OffloadCompilerTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class OffloadCompilerTests
{
    private const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private static readonly LibraryFolderRef ZFolder = new(@"C:\Users\u\OneDrive\Pictures\UAS Videos\" + ZRel, new DateOnly(2026, 9, 27), "Zachar Bay");

    [Fact]
    public void NewFolderGroup_OneJobPerIncludedNewVideo_InCaptureOrder()
    {
        var b = new OffloadPlanBuilder();
        var v2 = b.Video("DJI_20260927141000_0124_D.MP4", 2_000, T0.AddMinutes(10));
        var v1 = b.Video("DJI_20260927140000_0123_D.MP4", 1_000, T0);
        var imported = b.Video("DJI_20260927142000_0125_D.MP4", 3_000, T0.AddMinutes(20), new Imported(Evidence.LibraryNameSize, ZFolder, "listed"));
        var unticked = b.Video("DJI_20260927143000_0126_D.MP4", 4_000, T0.AddMinutes(30), included: false);
        b.Group(new NewFolder(ZRel), Zachar, v2, v1, imported, unticked);

        var batch = OffloadCompiler.Compile(b.Build(), "run-1");

        Assert.Equal("run-1", batch.RunId);
        Assert.Equal(OffloadPlanBuilder.Card, batch.Card);
        Assert.Equal([v1, v2], batch.Jobs.Select(j => j.Item).ToArray());
        var first = batch.Jobs[0];
        Assert.Equal(b.NewFolderPath(ZRel) + @"\DJI_20260927140000_0123_D.MP4", first.DestPath);
        Assert.Equal("DCIM/DJI_001/DJI_20260927140000_0123_D.MP4", first.CardRelPath);
        Assert.Equal(1_000, first.Size);
        Assert.Equal(DestRoot.Video, first.Root);
        Assert.Equal(new GroupId(v1), first.Group);
        Assert.True(first.CreatesFolder);
        var folder = Assert.Single(batch.Folders);
        Assert.Equal(b.NewFolderPath(ZRel), folder.FullPath);
        Assert.True(folder.Create);
        Assert.Equal("Zachar Bay", folder.Description);
        Assert.Equal(Zachar, folder.Centroid);
        Assert.Equal(Tz, folder.TzId);
        Assert.Empty(batch.SeenIfNotCopied);
    }

    [Fact]
    public void Append_TargetsTheExistingFolder_WithoutCreating()
    {
        var b = new OffloadPlanBuilder();
        var v = b.Video("DJI_20260927160000_0160_D.MP4", 1_000, T0.AddHours(2));
        b.Group(new Append(ZFolder, Confidence.High, "same day as clips already in this folder", null), Zachar, v);

        var job = Assert.Single(OffloadCompiler.Compile(b.Build(), "r").Jobs);

        Assert.Equal(ZFolder.FullPath + @"\DJI_20260927160000_0160_D.MP4", job.DestPath);
        Assert.False(job.CreatesFolder);
    }

    [Fact]
    public void SkipGroup_NothingToCopy_AndAlreadyImported_CopyNothing()
    {
        var b = new OffloadPlanBuilder();
        var a = b.Video("DJI_20260927140000_0001_D.MP4", 1, T0);
        var c = b.Video("DJI_20260928140000_0002_D.MP4", 1, T0.AddDays(1));
        var d = b.Video("DJI_20260929140000_0003_D.MP4", 1, T0.AddDays(2), new Imported(Evidence.LibraryNameSize, ZFolder, "listed"));
        b.Group(new SkipGroup(), Zachar, a).Group(new NothingToCopy("nothing to copy: 1 conflict"), null, c)
         .Group(new AlreadyImported(ZFolder), null, d);

        var batch = OffloadCompiler.Compile(b.Build(), "r");

        Assert.Empty(batch.Jobs);
        Assert.Empty(batch.Folders);
    }

    [Fact]
    public void AlreadyImported_WithCentroidAndNoLedgerFolder_GetsACardLeftoversFolderPlan()
    {
        var b = new OffloadPlanBuilder();
        var d = b.Video("DJI_20260927140000_0123_D.MP4", 1, T0, new Imported(Evidence.LibraryNameSize, ZFolder, "listed"));
        b.Group(new AlreadyImported(ZFolder), Zachar, d);

        var plan = Assert.Single(OffloadCompiler.Compile(b.Build(), "r").Folders);

        Assert.Equal(ZFolder.FullPath, plan.FullPath);
        Assert.False(plan.Create);
        Assert.Equal(Zachar, plan.Centroid);
        Assert.Empty(OffloadCompiler.Compile(new OffloadPlanBuilder().Also(x =>
        {
            var d2 = x.Video("DJI_20260927140000_0123_D.MP4", 1, T0, new Imported(Evidence.LibraryNameSize, ZFolder, "listed"));
            x.Group(new AlreadyImported(ZFolder), Zachar, d2).LedgerFolder(ZFolder.FullPath);
        }).Build(), "r").Folders);
    }

    [Fact]
    public void TickedConflictVideo_TakesTheNextFreeCopyNumber()
    {
        var b = new OffloadPlanBuilder();
        b.LibraryVideo(ZRel + @"\DJI_20260927140127_0123_D.MP4", 105_764_094)
         .LibraryVideo(ZRel + @"\DJI_20260927140127_0123_D (2).MP4", 50_000);
        var v = b.Video("DJI_20260927140127_0123_D.MP4", 7_340_032, T0,
                        new Conflict(ZFolder.FullPath + @"\DJI_20260927140127_0123_D.MP4", 105_764_094));
        b.Group(new Append(ZFolder, Confidence.High, "same day", null), Zachar, v);

        var job = Assert.Single(OffloadCompiler.Compile(b.Build(), "r").Jobs);

        Assert.Equal(ZFolder.FullPath + @"\DJI_20260927140127_0123_D (3).MP4", job.DestPath);
    }

    [Fact]
    public void Order_GroupsByStart_ThenPhotos_ThenSets()
    {
        var b = new OffloadPlanBuilder();
        var set = b.Set("001_0087", [("PANO_0002.DNG", 20), ("PANO_0001.DNG", 10)], T0.AddHours(-5), SetResolution.Plain);
        var photo = b.Photo("DJI_20260927100000_0001_D.DNG", 30, T0.AddHours(-6));
        var late = b.Video("DJI_20260928100000_0010_D.MP4", 40, T0.AddDays(1));
        var early = b.Video("DJI_20260927100000_0002_D.MP4", 50, T0.AddHours(-4));
        b.Group(new NewFolder(@"2026\2026-09\2026-09-28 Later"), null, late)
         .Group(new NewFolder(@"2026\2026-09\2026-09-27 Earlier"), null, early);

        var batch = OffloadCompiler.Compile(b.Build(), "r");

        Assert.Equal(
            ["DJI_20260927100000_0002_D.MP4", "DJI_20260928100000_0010_D.MP4", "DJI_20260927100000_0001_D.DNG", "PANO_0001.DNG", "PANO_0002.DNG"],
            batch.Jobs.Select(j => OffloadPaths.FileName(j.CardRelPath)).ToArray());
        Assert.Equal([photo, set], batch.SeenIfNotCopied.ToArray());
    }

    [Fact]
    public void PhotoWithTwin_TwoFlatJobsInThePhotoRoot()
    {
        var b = new OffloadPlanBuilder();
        b.Photo("DJI_20260927100000_0001_D.DNG", 30, T0, twinSize: 5);

        var batch = OffloadCompiler.Compile(b.Build(), "r");

        Assert.Equal([@"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload\DJI_20260927100000_0001_D.DNG", @"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload\DJI_20260927100000_0001_D.JPG"],
                     batch.Jobs.Select(j => j.DestPath).ToArray());
        Assert.All(batch.Jobs, j => { Assert.Equal(DestRoot.Photo, j.Root); Assert.Null(j.Group); Assert.False(j.CreatesFolder); });
    }

    [Fact]
    public void CopyJpgTwinOff_CopiesOnlyTheDng()
    {
        var b = new OffloadPlanBuilder().CopyJpgTwin(false);
        b.Photo("DJI_20260927100000_0001_D.DNG", 30, T0, twinSize: 5);

        var job = Assert.Single(OffloadCompiler.Compile(b.Build(), "r").Jobs);

        Assert.EndsWith(".DNG", job.DestPath, StringComparison.Ordinal);
    }

    [Fact]
    public void TickedPhotoConflict_TwinFollowsTheCopyNumber()
    {
        var b = new OffloadPlanBuilder();
        b.LibraryPhoto("DJI_20260927100000_0001_D.DNG", 99);
        b.Photo("DJI_20260927100000_0001_D.DNG", 30, T0, new Conflict(@"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload\DJI_20260927100000_0001_D.DNG", 99), twinSize: 5);

        var batch = OffloadCompiler.Compile(b.Build(), "r");

        Assert.Equal(["DJI_20260927100000_0001_D (2).DNG", "DJI_20260927100000_0001_D (2).JPG"],
                     batch.Jobs.Select(j => OffloadPaths.FileName(j.DestPath)).ToArray());
    }

    [Fact]
    public void ProbablyImported_CopiedOnlyWhenTicked_AndSeenOnlyWhenTicked()
    {
        var b = new OffloadPlanBuilder();
        var ticked = b.Photo("DJI_20260927100000_0001_D.DNG", 30, T0, new ProbablyImported("videos from this day are already in the library"));
        var left = b.Photo("DJI_20260927100500_0002_D.DNG", 30, T0.AddMinutes(5), new ProbablyImported("videos from this day are already in the library"), included: false);
        var newUnticked = b.Photo("DJI_20260927101000_0003_D.DNG", 30, T0.AddMinutes(10), included: false);
        var imported = b.Photo("DJI_20260927101500_0004_D.DNG", 30, T0.AddMinutes(15), new Imported(Evidence.LedgerVerified, null, "ledger"));

        var batch = OffloadCompiler.Compile(b.Build(), "r");

        Assert.Equal([ticked], batch.Jobs.Select(j => j.Item).ToArray());
        Assert.Equal([ticked, newUnticked], batch.SeenIfNotCopied.ToArray());
        Assert.DoesNotContain(left, batch.SeenIfNotCopied);
        Assert.DoesNotContain(imported, batch.SeenIfNotCopied);
    }

    [Fact]
    public void Sets_PlainCreatesItsFolder_ResumeCopiesOnlyMissing_ImportedIsSkipped()
    {
        var b = new OffloadPlanBuilder();
        b.Set("001_0087", [("PANO_0001.DNG", 10), ("PANO_0002.DNG", 20)], T0, SetResolution.DateSuffixed, "001_0087 2026-09-27");
        b.Set("001_0090", [("PANO_0001.DNG", 11), ("PANO_0002.DNG", 21)], T0.AddMinutes(1), SetResolution.Resume, membersToCopy: ["PANO_0002.DNG"]);
        b.Set("001_0091", [("PANO_0001.DNG", 12)], T0.AddMinutes(2), SetResolution.Imported);

        var jobs = OffloadCompiler.Compile(b.Build(), "r").Jobs;

        Assert.Equal(
            [@"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload\001_0087 2026-09-27\PANO_0001.DNG",
             @"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload\001_0087 2026-09-27\PANO_0002.DNG",
             @"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload\001_0090\PANO_0002.DNG"],
            jobs.Select(j => j.DestPath).ToArray());
        Assert.Equal([true, true, false], jobs.Select(j => j.CreatesFolder).ToArray());
    }
}

internal static class BuilderTestExtensions
{
    public static OffloadPlanBuilder Also(this OffloadPlanBuilder b, Action<OffloadPlanBuilder> arrange) { arrange(b); return b; }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*OffloadCompilerTests"`
Expected: build FAILS with CS0103 (`OffloadCompiler` does not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Offload/OffloadCompiler.cs
using System.Collections.Immutable;

namespace UasSort.Core.Offload;

/// <summary>Ref §10.1: turns a Plan into the ordered copy jobs of one Commit. Pure.</summary>
public static partial class OffloadCompiler
{
    public static OffloadBatch Compile(Plan plan) => Compile(plan, Guid.NewGuid().ToString("N"));

    public static OffloadBatch Compile(Plan plan, string runId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var scan = plan.Base.Scan;
        var settings = scan.Settings;
        var card = scan.Inventory.Source.Identity
                   ?? throw new InvalidOperationException("The card identity isn't pinned; rescan the card.");
        var items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var jobs = ImmutableArray.CreateBuilder<CopyJob>();
        var folders = ImmutableArray.CreateBuilder<FolderPlan>();
        var seen = ImmutableArray.CreateBuilder<ItemId>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new NameAllocator(scan.Library, scan.Ledger, taken);

        var groups = plan.Groups
            .OrderBy(g => g.Videos.Min(v => items[v].Time.CaptureUtc))
            .ThenBy(g => g.Id.Anchor.CardRelPath, StringComparer.Ordinal);
        foreach (var g in groups)
        {
            (string Dir, bool Create)? dest = g.Target switch
            {
                NewFolder nf => (OffloadPaths.Join(settings.VideoRoot, nf.RelPath), true),
                Append a => (a.Folder.FullPath, false),
                AlreadyImported or NothingToCopy or SkipGroup => null,
            };
            if (dest is not { } d)
            {
                if (g.Target is AlreadyImported ai && g.Centroid is not null && !scan.Ledger.Folders.ContainsKey(ai.Folder.FullPath))
                    folders.Add(new FolderPlan(g.Id, ai.Folder.FullPath, false, ai.Folder.Description, g.Centroid, g.Start, g.End, TzOf(g, items)));
                continue;
            }

            int before = jobs.Count;
            var videos = g.Videos.OrderBy(v => items[v].Time.CaptureUtc).ThenBy(v => v.CardRelPath, StringComparer.Ordinal);
            foreach (var id in videos)
            {
                var item = items[id];
                if (!plan.Included.Contains(id) || item.Newness is not (IsNew or Conflict)) continue;
                var mp4 = ((VideoUnit)item.Raw.Unit).Mp4;
                var name = OffloadPaths.FileName(mp4.RelPath);
                if (item.Newness is Conflict) name = names.Free(d.Dir, [(name, mp4.Size)])[0];
                jobs.Add(Job(id, mp4, OffloadPaths.Join(d.Dir, name), DestRoot.Video, g.Id, d.Create, taken));
            }
            if (jobs.Count > before)
                folders.Add(new FolderPlan(g.Id, d.Dir, d.Create, g.Description, g.Centroid, g.Start, g.End, TzOf(g, items)));
        }

        var photos = plan.Base.Items.Where(i => i.Raw.Unit is PhotoUnit)
            .OrderBy(i => i.Time.CaptureUtc).ThenBy(i => i.Raw.Unit.Id.CardRelPath, StringComparer.Ordinal);
        foreach (var item in photos)
        {
            var unit = (PhotoUnit)item.Raw.Unit;
            if (item.Newness is Imported or Decided) continue;
            bool included = plan.Included.Contains(unit.Id);
            if (included || item.Newness is IsNew or Conflict) seen.Add(unit.Id);
            if (!included) continue;
            var files = new List<(string Name, long Size)> { (OffloadPaths.FileName(unit.Primary.RelPath), unit.Primary.Size) };
            bool twin = settings.CopyJpgTwin && unit.JpgTwin is not null;
            if (twin) files.Add((OffloadPaths.FileName(unit.JpgTwin!.RelPath), unit.JpgTwin.Size));
            var destNames = item.Newness is Conflict ? names.Free(settings.PhotoRoot, files) : files.Select(f => f.Name).ToList();
            jobs.Add(Job(unit.Id, unit.Primary, OffloadPaths.Join(settings.PhotoRoot, destNames[0]), DestRoot.Photo, null, false, taken));
            if (twin)
                jobs.Add(Job(unit.Id, unit.JpgTwin!, OffloadPaths.Join(settings.PhotoRoot, destNames[1]), DestRoot.Photo, null, false, taken));
        }

        var sets = plan.Base.Items.Where(i => i.Raw.Unit is SetUnit)
            .OrderBy(i => i.Time.CaptureUtc).ThenBy(i => i.Raw.Unit.Id.CardRelPath, StringComparer.Ordinal);
        foreach (var item in sets)
        {
            var unit = (SetUnit)item.Raw.Unit;
            if (item.Newness is Imported or Decided) continue;
            if (!plan.Base.Sets.TryGetValue(unit.Id, out var placement) || placement.Resolution == SetResolution.Imported) continue;
            bool included = plan.Included.Contains(unit.Id);
            if (included || item.Newness is IsNew or Conflict) seen.Add(unit.Id);
            if (!included) continue;
            var dir = OffloadPaths.Join(settings.PhotoRoot, placement.FolderName);
            bool resume = placement.Resolution == SetResolution.Resume;
            var wanted = new HashSet<string>(placement.MembersToCopy, StringComparer.OrdinalIgnoreCase);
            foreach (var member in unit.Members.OrderBy(m => OffloadPaths.FileName(m.RelPath), StringComparer.OrdinalIgnoreCase))
            {
                var name = OffloadPaths.FileName(member.RelPath);
                if (resume && !wanted.Contains(name)) continue;
                jobs.Add(Job(unit.Id, member, OffloadPaths.Join(dir, name), DestRoot.Photo, null, !resume, taken));
            }
        }

        return new OffloadBatch(runId, card, jobs.ToImmutable(), seen.ToImmutable(), folders.ToImmutable());
    }

    private static CopyJob Job(ItemId id, CardEntry entry, string dest, DestRoot root, GroupId? group, bool creates, HashSet<string> taken)
    {
        taken.Add(dest);
        return new CopyJob(id, entry.RelPath, entry.Size, entry.MtimeUtc, entry.CreationUtc, dest, root, group, creates);
    }

    private static string TzOf(VideoGroup g, Dictionary<ItemId, Item> items)
        => items[g.Videos.MinBy(v => items[v].Time.CaptureUtc)].Time.TzId;

    /// <summary>Ref §7.4: the next free "stem (n).ext" across the listings, the ledger and this batch.</summary>
    private sealed class NameAllocator(LibraryIndex library, LedgerSnapshot ledger, HashSet<string> batch)
    {
        public List<string> Free(string dir, IReadOnlyList<(string Name, long Size)> files)
        {
            for (int n = 2; ; n++)
            {
                var candidates = files.Select(f => OffloadPaths.WithCopyNumber(f.Name, n)).ToList();
                bool free = true;
                for (int i = 0; i < files.Count && free; i++) free = !Taken(dir, candidates[i], files[i].Size);
                if (free) return candidates;
            }
        }

        private bool Taken(string dir, string candidate, long size)
        {
            if (batch.Contains(OffloadPaths.Join(dir, candidate))) return true;
            var norm = OffloadPaths.NormName(candidate);
            bool Named(string path) => string.Equals(OffloadPaths.FileName(path), candidate, StringComparison.OrdinalIgnoreCase);
            if (library.SameNameOtherSize(norm, size).Any(f => Named(f.FullPath))) return true;
            if (library.Match(new FileKey(norm, size)).Any(f => Named(f.FullPath))) return true;
            return ledger.Files.Values.Any(f => f.Key.NormName == norm && Named(f.Dest));
        }
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*OffloadCompilerTests"`
Expected: PASS (11 tests).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Testing/Offload/OffloadPlanBuilder.cs src/UasSort.Core/Offload/OffloadCompiler.cs tests/UasSort.Core.Tests/Offload/OffloadCompilerTests.cs
git commit -m "feat: compile a plan into ordered offload jobs

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.3: Job metadata for ledger records and the run's new-folder set

**Files:**
- Create: `src/UasSort.Core/Offload/OffloadCompiler.Meta.cs`
- Test: `tests/UasSort.Core.Tests/Offload/OffloadCompilerMetaTests.cs`

**Interfaces:**

```csharp
// Consumes: OffloadCompiler.Compile (07.2), OffloadPaths (07.1), Item/ItemTime/GpsFix/SessionKey (Ref §3).
// Produces (defined here):
public sealed record JobMeta(string Kind /* video|photo|twin|setMember */, DateTime? CaptureUtc, TimeSource? TimeSource,
                             GeoPoint? Point, string? TzId, DateOnly? LocalDate, SessionKey? Session, string? Set);
public static partial class OffloadCompiler
{
    // The facts a ledger `file` record needs that CopyJob doesn't carry (Ref §11 record fields), keyed by CopyJob.CardRelPath
    // (case-insensitive).
    public static ImmutableDictionary<string, JobMeta> Describe(Plan plan, OffloadBatch batch);
    // GuardContext.NewFolderDirs for this run: for every CreatesFolder video job its YYYY, YYYY-MM and event folder under the video
    // root; for every CreatesFolder set member its set folder. Case-insensitive.
    public static ImmutableHashSet<string> NewFolderDirs(OffloadBatch batch, string videoRoot);
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/OffloadCompilerMetaTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class OffloadCompilerMetaTests
{
    [Fact]
    public void Describe_GivesKindTimeZoneAndSetPerJob()
    {
        var b = new OffloadPlanBuilder();
        var v = b.Video("DJI_20260927140000_0123_D.MP4", 10, T0, gps: Zachar);
        b.Group(new NewFolder(@"2026\2026-09\2026-09-27 Zachar Bay"), Zachar, v);
        b.Photo("DJI_20260927140500_0124_D.DNG", 20, T0.AddMinutes(5), twinSize: 3);
        b.Set("001_0087", [("PANO_0001.DNG", 5)], T0.AddMinutes(9), SetResolution.Plain);
        var plan = b.Build();
        var batch = OffloadCompiler.Compile(plan, "r");

        var meta = OffloadCompiler.Describe(plan, batch);

        Assert.Equal("video", meta["DCIM/DJI_001/DJI_20260927140000_0123_D.MP4"].Kind);
        Assert.Equal(Zachar, meta["DCIM/DJI_001/DJI_20260927140000_0123_D.MP4"].Point);
        Assert.Equal(TimeSource.Mvhd, meta["DCIM/DJI_001/DJI_20260927140000_0123_D.MP4"].TimeSource);
        Assert.Equal("photo", meta["DCIM/DJI_001/DJI_20260927140500_0124_D.DNG"].Kind);
        Assert.Equal("twin", meta["dcim/dji_001/DJI_20260927140500_0124_D.JPG"].Kind);
        var member = meta["DCIM/PANORAMA/001_0087/PANO_0001.DNG"];
        Assert.Equal("setMember", member.Kind);
        Assert.Equal("001_0087", member.Set);
        Assert.Equal(T0.AddMinutes(9), member.CaptureUtc);
        Assert.Equal(Tz, member.TzId);
        Assert.Equal(new DateOnly(2026, 9, 27), member.LocalDate);
    }

    [Fact]
    public void NewFolderDirs_CoversYearMonthEventAndSetFolders()
    {
        var b = new OffloadPlanBuilder().WithPhotoRoot(@"D:\Photos");
        var v = b.Video("DJI_20260927140000_0123_D.MP4", 10, T0);
        b.Group(new NewFolder(@"2026\2026-09\2026-09-27 Zachar Bay"), null, v);
        b.Set("001_0087", [("PANO_0001.DNG", 5)], T0, SetResolution.Plain);
        b.Photo("DJI_20260927140500_0124_D.DNG", 20, T0);

        var dirs = OffloadCompiler.NewFolderDirs(OffloadCompiler.Compile(b.Build(), "r"), b.VideoRoot);

        Assert.Equal(4, dirs.Count);
        Assert.True(dirs.Contains(@"c:\users\u\onedrive\pictures\uas videos\2026"));
        Assert.True(dirs.Contains(@"C:\Users\u\OneDrive\Pictures\UAS Videos\2026\2026-09"));
        Assert.True(dirs.Contains(@"C:\Users\u\OneDrive\Pictures\UAS Videos\2026\2026-09\2026-09-27 Zachar Bay"));
        Assert.True(dirs.Contains(@"D:\Photos\001_0087"));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*OffloadCompilerMetaTests"`
Expected: build FAILS with CS0117 (`OffloadCompiler` has no `Describe` / `NewFolderDirs`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Offload/OffloadCompiler.Meta.cs
using System.Collections.Immutable;

namespace UasSort.Core.Offload;

/// <summary>What a ledger `file` record needs beyond the CopyJob (Ref §11 field list).</summary>
public sealed record JobMeta(string Kind, DateTime? CaptureUtc, TimeSource? TimeSource, GeoPoint? Point, string? TzId,
                             DateOnly? LocalDate, SessionKey? Session, string? Set);

public static partial class OffloadCompiler
{
    public static ImmutableDictionary<string, JobMeta> Describe(Plan plan, OffloadBatch batch)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(batch);
        var items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var meta = ImmutableDictionary.CreateBuilder<string, JobMeta>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in batch.Jobs)
        {
            if (!items.TryGetValue(job.Item, out var item)) continue;
            string kind;
            string? set = null;
            switch (item.Raw.Unit)
            {
                case VideoUnit:
                    kind = "video";
                    break;
                case PhotoUnit p:
                    kind = p.JpgTwin is { } twin && string.Equals(twin.RelPath, job.CardRelPath, StringComparison.OrdinalIgnoreCase)
                        ? "twin" : "photo";
                    break;
                case SetUnit s:
                    kind = "setMember";
                    set = s.SetName;
                    break;
                default:
                    continue;
            }
            meta[job.CardRelPath] = new JobMeta(kind, item.Time.CaptureUtc, item.Time.Source, item.Gps?.Point, item.Time.TzId,
                                                item.Time.LocalDate, item.Session, set);
        }
        return meta.ToImmutable();
    }

    public static ImmutableHashSet<string> NewFolderDirs(OffloadBatch batch, string videoRoot)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var dirs = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in batch.Jobs.Where(j => j.CreatesFolder))
        {
            var dir = OffloadPaths.DirectoryOf(job.DestPath);
            if (job.Root == DestRoot.Video) dirs.UnionWith(OffloadPaths.NewFolderDirs(videoRoot, dir));
            else dirs.Add(dir);
        }
        return dirs.ToImmutable();
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*OffloadCompilerMetaTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/OffloadCompiler.Meta.cs tests/UasSort.Core.Tests/Offload/OffloadCompilerMetaTests.cs
git commit -m "feat: describe offload jobs for ledger records and list the run's new folders

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.4: Destination and ledger fakes, the offload rig

**Files:**
- Modify: `tests/UasSort.Testing/FakeFileOps.cs` (Part 02 Task 02.10: add the offload members in place)
- Modify: `tests/UasSort.Testing/FakeLedgerStore.cs` (Part 06 Task 06.21: complete the write half in place; add `FakeLedgerWriter`)
- Create: `tests/UasSort.Testing/OffloadFakes.cs`
- Create: `tests/UasSort.Testing/Offload/OffloadVolumes.cs`
- Create: `tests/UasSort.Testing/Offload/OffloadRig.cs`
- Test: `tests/UasSort.Core.Tests/Offload/OffloadRigTests.cs`

**Interfaces:**

```csharp
// Consumes (Part 02, namespace UasSort.Testing): FakeFileSystem (Context, Faults, DestinationFreeBytes, AddFile, AddDirectory, Exists,
//   Metadata, PeekContent, RemoveUnguarded, Guard, OpenRead, OpenAppend, CreateDirectory, SetPinned, Enumerate, AssertNoViolations,
//   GuardLog, and the assembly-internal Gate, Find, Require, PutFile, RequireParentDirectory, AppendBytes, Move, RemoveTree, FakeNode),
//   FakeFaults (DiskFullAfterBytes, CorruptVerify, UnbufferedUnsupported, TargetAppearsBeforeRename, SizeAfterRename, LostRoots,
//   AppendFails, internal IsLost), FakeFileOps (Task 02.10: OwnTemps, Renamed, Flushed, CreatedDirectories, FlushToDiskCount),
//   FakeCardReader(FakeFileSystem, string root, CardIdentity pinned), FakeLayout (NewFileSystem, Machine, AppDataDir, CardId);
//   IoGuardPolicy (FileAttributeHidden/NotContentIndexed/Archive, TempSuffix); IoOp; PathRules; LedgerPaths.For/OwnFile/BackupDir/IsLedgerFileName;
//   Part 05: LedgerCodec.Serialize/TryParse, LedgerFolderFacts, LedgerFolderStatusBuilder.Build, LedgerLoader.Load, LedgerSnapshots.Empty;
//   Part 06 (Task 06.21): FakeLedgerStore (ctor, Folder, Calls, StatusOverride, CheckThrows, Check, Load);
//   OffloadCompiler.Compile/NewFolderDirs (07.2–07.3); OffloadPlanBuilder (07.2).
// Produces:
// namespace UasSort.Testing — Part 02's FakeFileOps gains (same file; Part 02's members and behaviour unchanged):
public sealed class FakeFileOps : IFileOps
{ FakeFileOps(FakeFileSystem fs, IEnumerable<string> newFolderDirs);   // the run's NewFolderDirs verbatim, instead of fs.Context.NewFolderDirs
  List<string> Calls { get; }                                          // "CreateTemp <temp>", "FlushToDisk", "VerifyHash <temp>",
                                                                       // "FinalizeAttributes <temp>", "RenameNoReplace <final>", "ConfirmFinal <final>",
                                                                       // "FlushDestination <dir>", "DeleteOwnTemp <temp>", "EnsureDirectory <dir> <True|False>"
  IReadOnlyList<(string Dir, int Files)> FlushedDestinations { get; }
  Action<string, long>? OnTempWrite { get; set; }   // (final path, bytes in this temp so far), after each write
  Exception? ThrowOnFinalize { get; set; }  Func<string, long>? FreeBytesOverride { get; set; } }
// namespace UasSort.Testing — Part 06's FakeLedgerStore gains (same file):
public sealed class FakeLedgerStore : ILedgerStore
{ FakeLedgerWriter Writer { get; }  bool EnsureFolderThrows { get; set; }
  void EnsureFolder(); ILedgerWriter OpenOwn(); void SnapshotToBackup(string runId); void KeepOnDevice();
  void CopyInto(string newVideoRoot, LedgerSnapshot current); }                   // replace Part 06's NotSupportedException bodies
public sealed class FakeLedgerWriter : ILedgerWriter
{ List<LedgerRecord> Records { get; } Func<LedgerRecord, bool>? ThrowWhen { get; set; } int Opened { get; } bool Disposed { get; } }
// namespace UasSort.Testing (tests/UasSort.Testing/OffloadFakes.cs, all defined here):
public sealed class FakeOffloadLock : IOffloadLock { bool HeldElsewhere { get; set; } int Holds { get; } }
public sealed class FakePowerRequest : IPowerRequest { int Active { get; } List<string> Reasons { get; } }
public sealed class FakeThumbnails : IThumbnailSource { int Pauses { get; } int ActivePauses { get; } int Paused { get; } }  // GetAsync: empty bytes
public sealed class MemReportStore : IReportStore { List<OffloadReport> Offload; List<CleanupReport> Cleanup; bool Throws { get; set; } }
public sealed class FakeVolumeProvider(IReadOnlyList<VolumeInfo> volumes) : IVolumeProvider;
public sealed class ListProgress<T> : IProgress<T> { List<T> Items { get; } }
// namespace UasSort.Testing.Offload (scenario builders only, defined here):
public static class OffloadVolumes { static readonly VolumeInfo NtfsC; static readonly VolumeInfo ExFatD; }
public sealed class OffloadRig
{ OffloadRig(OffloadPlanBuilder builder);  OffloadPlanBuilder B; FakeFileSystem Fs; Plan Plan; OffloadBatch Batch;
  FakeCardReader Reader; FakeFileOps Files; FakeLedgerStore Ledger; FakeLedgerWriter Writer; FakeOffloadLock Lock;
  FakePowerRequest Power; FakeThumbnails Thumbnails; MemReportStore Reports; List<VolumeInfo> Volumes; Dictionary<string, byte[]> CardData;
  OffloadRig Build(bool cardFiles = true, bool ledgerOnFileSystem = false);  void Rebatch(OffloadBatch batch);
  static string CardPath(string cardRelPath);  static byte[] Bytes(long size, int seed); }
```

Part 02's `FakeFileOps` and Part 06's `FakeLedgerStore` are the only fakes of their kind (registry decision 6): this task extends those two files in place and defines no second `IFileOps` or `ILedgerStore` fake. Part 02's `FakeFileOpsTests` and Part 06's `ScanServiceTripwireTests` stay green (Step 4 runs them). Two small additions to `FakeFileOps` follow Part 02's own contract "`LostRoots` makes every call under that root throw": `TryGetSize` and `FlushToDisk` now honour it too.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/OffloadRigTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class OffloadRigTests
{
    private const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";

    private static OffloadRig OneVideo()
    {
        var b = new OffloadPlanBuilder();
        var v = b.Video("DJI_20260927140000_0123_D.MP4", 1_500_000, T0);
        b.Group(new NewFolder(ZRel), Zachar, v);
        return new OffloadRig(b).Build();
    }

    [Fact]
    public void FakeFileOps_WritesVerifiesAndRenamesAnOwnTemp()
    {
        var rig = OneVideo();
        var job = rig.Batch.Jobs[0];
        var data = rig.CardData[job.CardRelPath];

        rig.Files.EnsureDirectory(OffloadPaths.DirectoryOf(job.DestPath), allowCreate: true);
        var stream = rig.Files.CreateTemp(job.DestPath, job.Size, out var temp);
        Assert.Equal(OffloadPaths.TempOf(job.DestPath), temp);
        Assert.NotEqual(0u, rig.Fs.GetAttributes(temp)!.Value & 0x2u);
        stream.Write(data);
        rig.Files.FlushToDisk(stream);
        stream.Dispose();
        var check = rig.Files.VerifyHash(temp, job.Size, System.IO.Hashing.XxHash128.HashToUInt128(data), CancellationToken.None);
        Assert.True(check is HashMatch { Mode: VerifyMode.Unbuffered });
        rig.Files.FinalizeAttributes(temp, job.CardCreationUtc, job.CardMtimeUtc);
        Assert.Equal(0u, rig.Fs.GetAttributes(temp)!.Value & 0x2u);
        Assert.True(rig.Files.RenameNoReplace(temp, job.DestPath) is Renamed);
        Assert.True(rig.Files.ConfirmFinal(job.DestPath, job.Size));

        Assert.Equal(data, rig.Fs.PeekContent(job.DestPath));
        Assert.False(rig.Fs.Exists(temp));
        Assert.Equal(job.CardMtimeUtc, rig.Fs.Metadata(job.DestPath)!.MtimeUtc);
        rig.Fs.AssertNoViolations();
    }

    [Fact]
    public void FakeFileOps_NeverCreatesAnAppendFolder()
    {
        var rig = OneVideo();
        var append = rig.B.NewFolderPath(@"2026\2026-08\2026-08-01 Gone");

        Assert.Throws<DirectoryNotFoundException>(() => rig.Files.EnsureDirectory(append, allowCreate: false));
        Assert.Throws<UnsafeIoException>(() => rig.Files.EnsureDirectory(append, allowCreate: true));
        Assert.False(rig.Fs.Exists(append));
        Assert.Throws<UnsafeIoException>(() => rig.Files.DeleteOwnTemp(rig.B.NewFolderPath("x.MP4")));
    }

    [Fact]
    public void FakeFileOps_HonoursTheDestinationFaults()
    {
        var rig = OneVideo();
        var job = rig.Batch.Jobs[0];
        var data = rig.CardData[job.CardRelPath];
        var expected = System.IO.Hashing.XxHash128.HashToUInt128(data);
        rig.Files.EnsureDirectory(OffloadPaths.DirectoryOf(job.DestPath), allowCreate: true);
        rig.Fs.Faults.CorruptVerify[job.DestPath] = 1;
        rig.Fs.Faults.TargetAppearsBeforeRename.Add(job.DestPath);

        using (var s = rig.Files.CreateTemp(job.DestPath, job.Size, out _)) s.Write(data);
        var temp = OffloadPaths.TempOf(job.DestPath);
        Assert.True(rig.Files.VerifyHash(temp, job.Size, expected, CancellationToken.None) is HashMismatch);
        Assert.True(rig.Files.VerifyHash(temp, job.Size, expected, CancellationToken.None) is HashMatch);
        rig.Files.FinalizeAttributes(temp, job.CardCreationUtc, job.CardMtimeUtc);
        Assert.True(rig.Files.RenameNoReplace(temp, job.DestPath) is TargetExists);
        rig.Files.DeleteOwnTemp(temp);
        Assert.NotEqual(data.Length, rig.Fs.PeekContent(job.DestPath).Length);

        rig.Fs.Faults.SizeAfterRename[job.DestPath] = 1;
        Assert.False(rig.Files.ConfirmFinal(job.DestPath, 3));

        rig.Fs.Faults.DiskFullAfterBytes = 10;
        var other = rig.B.NewFolderPath(ZRel + @"\other.MP4");
        using (var s = rig.Files.CreateTemp(other, 100, out _))
        {
            var e = Assert.Throws<IOException>(() => s.Write(new byte[100]));
            Assert.Equal(0x70, e.HResult & 0xFFFF);
        }
        rig.Fs.Faults.LostRoots.Add(rig.B.VideoRoot);
        Assert.Throws<DirectoryNotFoundException>(() => rig.Files.FreeBytes(other));
        rig.Fs.AssertNoViolations();
    }

    [Fact]
    public void FakeFileOps_RecordsCallsAndFlushedDestinations()
    {
        var rig = OneVideo();
        var dir = OffloadPaths.DirectoryOf(rig.Batch.Jobs[0].DestPath);
        rig.Files.EnsureDirectory(dir, allowCreate: true);

        rig.Files.FlushDestination(dir, []);

        Assert.Equal([$"EnsureDirectory {dir} True", $"FlushDestination {dir}"], rig.Files.Calls.ToArray());
        Assert.Equal([(dir, 0)], rig.Files.FlushedDestinations.ToArray());
        rig.Fs.AssertNoViolations();
    }

    [Fact]
    public void FakeLedgerStore_OnTheFileSystem_CreatesPinsAndAppendsOnlyTheOwnFile()
    {
        var rig = OneVideo();
        rig.Build(ledgerOnFileSystem: true);

        Assert.Equal(LedgerFolderState.Missing, rig.Ledger.Check().State);
        rig.Ledger.EnsureFolder();
        using (var w = rig.Ledger.OpenOwn()) w.Append(new TornRecord(1, "id-1", FakeLayout.Machine, T0, 3));

        Assert.Single(rig.Writer.Records);
        var own = LedgerPaths.OwnFile(rig.B.VideoRoot, FakeLayout.Machine);
        var line = System.Text.Encoding.UTF8.GetString(rig.Fs.PeekContent(own)).TrimEnd('\n');
        Assert.IsType<TornRecord>(LedgerCodec.TryParse(line, out _));
        Assert.Equal(LedgerFolderState.Ok, rig.Ledger.Check().State);
        Assert.Equal(["Check", "EnsureFolder", "OpenOwn", "Check"], rig.Ledger.Calls);
        rig.Fs.AssertNoViolations();
    }

    [Fact]
    public void FakeLedgerStore_CopyInto_CopiesEveryRecordButTorn_Once()
    {
        var rig = OneVideo();
        rig.Build(ledgerOnFileSystem: true);
        rig.Ledger.EnsureFolder();
        using (var w = rig.Ledger.OpenOwn())
        {
            w.Append(new RevokeRecord(1, "id-1", FakeLayout.Machine, T0, "dec-1"));
            w.Append(new TornRecord(1, "id-2", FakeLayout.Machine, T0, 3));
        }
        var current = LedgerSnapshots.Empty(rig.Ledger.Check()) with { SourceFiles = [LedgerPaths.OwnFile(rig.B.VideoRoot, FakeLayout.Machine)] };
        const string newRoot = @"C:\Users\u\Videos\UAS";
        rig.Fs.AddDirectory(newRoot);

        rig.Ledger.CopyInto(newRoot, current);
        rig.Ledger.CopyInto(newRoot, current);

        var copied = System.Text.Encoding.UTF8.GetString(rig.Fs.PeekContent(LedgerPaths.OwnFile(newRoot, FakeLayout.Machine)))
                           .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.IsType<RevokeRecord>(LedgerCodec.TryParse(Assert.Single(copied), out _));
        Assert.Equal($"CopyInto {newRoot}", rig.Ledger.Calls[^1]);
        rig.Fs.AssertNoViolations();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*OffloadRigTests"`
Expected: build FAILS with CS0246 (`OffloadRig` does not exist) and CS1729 / CS1061 (`FakeFileOps` has no two-argument constructor, no `Calls`/`FlushedDestinations`; `FakeLedgerStore` has no `Writer`).

- [ ] **Step 3: Implement**

Replace the whole content of Part 02's `tests/UasSort.Testing/FakeFileOps.cs` with the version below. Part 02's members (`OwnTemps`, `Renamed`, `Flushed`, `CreatedDirectories`, `FlushToDiskCount`) and behaviour are unchanged; the members marked Part 07 are added, and `TryGetSize`/`FlushToDisk` now honour `LostRoots` like every other call.

```csharp
// tests/UasSort.Testing/FakeFileOps.cs
using System.IO.Hashing;
using UasSort.Core;

namespace UasSort.Testing;

/// <summary>The fake twin of Platform's GuardedFileOps (Part 02), with the offload's hooks (Part 07). Every call is guarded through
/// FakeFileSystem.Guard with this run's own temps and renamed files, and honours FakeFaults' destination hooks.</summary>
public sealed class FakeFileOps(FakeFileSystem fs) : IFileOps
{
    private const uint TempAttributes = IoGuardPolicy.FileAttributeHidden | IoGuardPolicy.FileAttributeNotContentIndexed
                                        | IoGuardPolicy.FileAttributeArchive;
    private readonly HashSet<string> _ownTemps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _renamed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _flushed = [];
    private readonly List<(string Dir, int Files)> _flushedDestinations = [];
    private readonly List<string> _createdDirectories = [];
    private readonly IReadOnlySet<string>? _newFolderDirs;
    private long _bytesWritten;

    /// <summary>Part 07: guard with the run's NewFolderDirs (OffloadCompiler.NewFolderDirs), used verbatim instead of
    /// fs.Context.NewFolderDirs, exactly as GuardedFileOps takes them (registry decision 27).</summary>
    public FakeFileOps(FakeFileSystem fs, IEnumerable<string> newFolderDirs) : this(fs)
    {
        ArgumentNullException.ThrowIfNull(newFolderDirs);
        _newFolderDirs = newFolderDirs.Select(PathRules.Normalize).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // ── Part 02
    public IReadOnlySet<string> OwnTemps { get { lock (fs.Gate) { return _ownTemps.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase); } } }
    public IReadOnlySet<string> Renamed { get { lock (fs.Gate) { return _renamed.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase); } } }
    public IReadOnlyList<string> Flushed { get { lock (fs.Gate) { return [.. _flushed]; } } }
    public IReadOnlyList<string> CreatedDirectories { get { lock (fs.Gate) { return [.. _createdDirectories]; } } }
    public int FlushToDiskCount { get; private set; }

    // ── Part 07 (Task 07.4)
    /// <summary>Every IFileOps call, e.g. "CreateTemp {temp}", "EnsureDirectory {dir} False" (paths normalised).</summary>
    public List<string> Calls { get; } = [];
    /// <summary>One entry per FlushDestination call: the directory and how many files were flushed in it.</summary>
    public IReadOnlyList<(string Dir, int Files)> FlushedDestinations { get { lock (fs.Gate) { return [.. _flushedDestinations]; } } }
    /// <summary>Called after each temp write with (final path, bytes in this temp so far).</summary>
    public Action<string, long>? OnTempWrite { get; set; }
    /// <summary>Thrown by FinalizeAttributes before anything else (e.g. an UnsafeIoException for the safety-stop test).</summary>
    public Exception? ThrowOnFinalize { get; set; }
    /// <summary>Replaces fs.DestinationFreeBytes in FreeBytes (argument: the path asked about).</summary>
    public Func<string, long>? FreeBytesOverride { get; set; }

    private GuardContext Ctx => fs.Context with
    {
        NewFolderDirs = _newFolderDirs ?? fs.Context.NewFolderDirs,
        OwnTempsThisRun = OwnTemps,
        RenamedThisRun = Renamed,
    };

    private void Call(string text)
    {
        lock (fs.Gate) { Calls.Add(text); }
    }

    public Stream CreateTemp(string finalPath, long size, out string tempPath)
    {
        var final = PathRules.Normalize(finalPath);
        var temp = final + IoGuardPolicy.TempSuffix;
        Call("CreateTemp " + temp);
        ThrowIfLost(final);
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
        Call("FlushToDisk");
        if (s is FakeWriteStream w) ThrowIfLost(w.TempPath);
        s.Flush();
        FlushToDiskCount++;
    }

    public VerifyResult VerifyHash(string tempPath, long size, UInt128 expected, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var temp = PathRules.Normalize(tempPath);
        Call("VerifyHash " + temp);
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
        Call("FinalizeAttributes " + temp);
        if (ThrowOnFinalize is { } injected) throw injected;
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
        Call("RenameNoReplace " + final);
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
        Call("ConfirmFinal " + final);
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
        Call("FlushDestination " + d);
        ThrowIfLost(d);
        foreach (var f in filesCreatedThisRun)
        {
            var p = fs.Guard(IoOp.OpenForFlush, f, Ctx);
            lock (fs.Gate) { _flushed.Add(p); }
        }
        var dp = fs.Guard(IoOp.OpenForFlush, d, Ctx);
        lock (fs.Gate)
        {
            _flushed.Add(dp);
            _flushedDestinations.Add((d, filesCreatedThisRun.Count));
        }
    }

    public void DeleteOwnTemp(string tempPath)
    {
        var temp = PathRules.Normalize(tempPath);
        Call("DeleteOwnTemp " + temp);
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
        Call($"EnsureDirectory {d} {allowCreate}");
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
        var p = PathRules.Normalize(path);
        ThrowIfLost(p);
        var e = fs.Metadata(p);
        size = e is { IsDirectory: false } ? e.Size : 0;
        return e is { IsDirectory: false };
    }

    public long FreeBytes(string anyPathOnVolume)
    {
        ThrowIfLost(PathRules.Normalize(anyPathOnVolume));
        return FreeBytesOverride?.Invoke(anyPathOnVolume) ?? fs.DestinationFreeBytes;
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
        OnTempWrite?.Invoke(node.Path[..^IoGuardPolicy.TempSuffix.Length], node.Size);
    }

    private void ThrowIfLost(string path)
    {
        if (fs.Faults.IsLost(path)) throw new DirectoryNotFoundException($"The destination root of {path} is gone");
    }

    private static IOException DiskFull() => new("There is not enough space on the disk.", unchecked((int)0x80070070));
}

internal sealed class FakeWriteStream(FakeFileOps ops, FakeNode node) : Stream
{
    public string TempPath => node.Path;
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

Replace the whole content of Part 06's `tests/UasSort.Testing/FakeLedgerStore.cs` with the version below. The Part 06 members (constructor, `Folder`, `Calls`, `StatusOverride`, `CheckThrows`, `Check()` through `LedgerFolderStatusBuilder`, `Load()` through `LedgerLoader`) keep the behaviour Part 06 gave them (registry, Part 06 required edit 7); the Part 07 members replace the `NotSupportedException` bodies and add `Writer`, `EnsureFolderThrows` and `FakeLedgerWriter`.

```csharp
// tests/UasSort.Testing/FakeLedgerStore.cs
using System.Text;
using UasSort.Core;

namespace UasSort.Testing;

/// <summary>The one fake ILedgerStore (Part 06 creates it, Part 07 completes it). With fs == null it is purely in memory (the
/// snapshot, or an empty one); with a FakeFileSystem every folder create, pin, read and append goes through the guard.</summary>
public sealed class FakeLedgerStore(FakeFileSystem? fs, string videoRoot, string machine, LedgerSnapshot? snapshot = null) : ILedgerStore
{
    // ── Part 06 (Task 06.21): status and load — verbatim from Part 06 (Load records only "Load", never a second "Check")
    private static readonly IReadOnlySet<string> NoExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string Folder { get; } = LedgerPaths.For(videoRoot);
    public List<string> Calls { get; } = [];
    public LedgerFolderStatus? StatusOverride { get; set; }
    public bool CheckThrows { get; set; }

    public LedgerFolderStatus Check()
    {
        Calls.Add(nameof(Check));
        return Status();
    }

    public LedgerSnapshot Load()
    {
        Calls.Add(nameof(Load));
        var status = Status();
        if (fs is not { } files) return snapshot ?? LedgerSnapshots.Empty(status);
        return LedgerLoader.Load(status, p => files.OpenRead(p));      // guarded ReadData; logged in files.GuardLog
    }

    private LedgerFolderStatus Status()
    {
        if (CheckThrows) throw new IOException("Fake ledger folder check failed.");
        if (StatusOverride is { } overridden) return overridden;
        if (fs is null)
            return snapshot?.Status ?? LedgerFolderStatusBuilder.Build(videoRoot, machine, new LedgerFolderFacts(true, null, null, false, true));
        var root = fs.Metadata(videoRoot);
        var folder = fs.Metadata(Folder);
        var top = folder is { IsDirectory: true } ? fs.Enumerate(Folder, false, NoExcludes) : null;
        return LedgerFolderStatusBuilder.Build(videoRoot, machine,
            new LedgerFolderFacts(root is { IsDirectory: true }, folder, top, InSyncRoot: false, Writable: true));
    }

    // ── Part 07 (Task 07.4): the write half
    public FakeLedgerWriter Writer { get; } = new();
    public bool EnsureFolderThrows { get; set; }

    public void EnsureFolder()
    {
        Calls.Add("EnsureFolder");
        if (EnsureFolderThrows) throw new IOException($"Couldn't create {Folder}.");
        if (fs is not { } files) return;
        if (files.Metadata(Folder) is null) files.CreateDirectory(Folder);
        files.SetPinned(Folder);
    }

    public ILedgerWriter OpenOwn()
    {
        Calls.Add("OpenOwn");
        Writer.Attach(fs?.OpenAppend(LedgerPaths.OwnFile(videoRoot, machine)));
        return Writer;
    }

    public void SnapshotToBackup(string runId)
    {
        Calls.Add("SnapshotToBackup " + runId);
        if (fs is not { } files || files.Metadata(Folder) is not { IsDirectory: true }) return;
        var dir = PathRules.Join(LedgerPaths.BackupDir(files.Context.AppDataDir, videoRoot), $"snapshots/{runId}");
        foreach (var e in files.Enumerate(Folder, recurse: false, ImmutableHashSet<string>.Empty).Entries)
        {
            if (e.IsDirectory || !LedgerPaths.IsLedgerFileName(PathRules.FileName(e.FullPath))) continue;
            byte[] content;
            using (var s = files.OpenRead(e.FullPath))
            using (var copy = new MemoryStream())
            {
                s.CopyTo(copy);
                content = copy.ToArray();
            }
            var dest = PathRules.Join(dir, PathRules.FileName(e.FullPath));
            files.Guard(IoOp.CreateNew, dest);
            files.AddFile(dest, content, DateTime.UnixEpoch);
        }
    }

    public void KeepOnDevice()
    {
        Calls.Add("KeepOnDevice");
        fs?.SetPinned(Folder);
    }

    /// <summary>Like Part 09's LedgerStore.CopyInto: ensures the new root's ledger folder and appends every line of
    /// current.SourceFiles whose id is not yet in the new own file; torn and unparseable lines are never copied.</summary>
    public void CopyInto(string newVideoRoot, LedgerSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);
        Calls.Add("CopyInto " + newVideoRoot);
        if (fs is not { } files) return;
        var target = files.Context with { VideoRoot = newVideoRoot };
        var targetFolder = LedgerPaths.For(newVideoRoot);
        if (files.Metadata(targetFolder) is null) files.CreateDirectory(targetFolder, target);
        files.SetPinned(targetFolder, target);
        var own = LedgerPaths.OwnFile(newVideoRoot, machine);
        var present = new HashSet<string>(StringComparer.Ordinal);
        if (files.Exists(own))
            foreach (var line in ReadLines(files.OpenRead(own, target)))
                if (LedgerCodec.TryParse(line, out _) is { } r) present.Add(r.Id);
        using var append = files.OpenAppend(own, target);
        foreach (var source in current.SourceFiles)
            foreach (var line in ReadLines(files.OpenRead(source)))
                if (LedgerCodec.TryParse(line, out _) is { } r && r is not TornRecord && present.Add(r.Id))
                    append.Write(Encoding.UTF8.GetBytes(line + "\n"));
    }

    private static List<string> ReadLines(Stream stream)
    {
        using (stream)
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            return [.. reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0)];
    }
}

/// <summary>ILedgerWriter that keeps every record as an object and, when opened on the fake FS, appends its LedgerCodec line
/// to the own file.</summary>
public sealed class FakeLedgerWriter : ILedgerWriter
{
    private Stream? _stream;

    public List<LedgerRecord> Records { get; } = [];
    public Func<LedgerRecord, bool>? ThrowWhen { get; set; }
    public int Opened { get; private set; }
    public bool Disposed { get; private set; }

    internal void Attach(Stream? stream)
    {
        _stream = stream;
        Opened++;
        Disposed = false;
    }

    public void Append(LedgerRecord r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (ThrowWhen?.Invoke(r) == true) throw new IOException("The ledger file couldn't be written.");
        if (_stream is not null)
        {
            _stream.Write(Encoding.UTF8.GetBytes(LedgerCodec.Serialize(r) + "\n"));
            _stream.Flush();
        }
        Records.Add(r);
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
        Disposed = true;
        GC.SuppressFinalize(this);
    }
}
```

```csharp
// tests/UasSort.Testing/OffloadFakes.cs
using UasSort.Core;

namespace UasSort.Testing;

/// <summary>Runs its action on the first Dispose only.</summary>
internal sealed class ReleaseOnce(Action onDispose) : IDisposable
{
    private bool _done;
    public void Dispose()
    {
        if (_done) return;
        _done = true;
        onDispose();
    }
}

public sealed class FakeOffloadLock : IOffloadLock
{
    public bool HeldElsewhere { get; set; }
    public int Holds { get; private set; }
    public IDisposable? TryAcquire()
    {
        if (HeldElsewhere) return null;
        Holds++;
        return new ReleaseOnce(() => Holds--);
    }
}

public sealed class FakePowerRequest : IPowerRequest
{
    public int Active { get; private set; }
    public List<string> Reasons { get; } = [];
    public IDisposable KeepSystemAwake(string reason)
    {
        Reasons.Add(reason);
        Active++;
        return new ReleaseOnce(() => Active--);
    }
}

/// <summary>IThumbnailSource that serves empty bytes and counts pauses: Pauses = Pause() calls so far, ActivePauses = not yet released.</summary>
public sealed class FakeThumbnails : IThumbnailSource
{
    public int Pauses { get; private set; }
    public int ActivePauses { get; private set; }
    /// <summary>Same count as ActivePauses (the name the cleanup tests read).</summary>
    public int Paused => ActivePauses;
    public ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct) => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
    public IDisposable Pause()
    {
        Pauses++;
        ActivePauses++;
        return new ReleaseOnce(() => ActivePauses--);
    }
}

public sealed class MemReportStore : IReportStore
{
    public List<OffloadReport> Offload { get; } = [];
    public List<CleanupReport> Cleanup { get; } = [];
    public bool Throws { get; set; }

    public string Save(OffloadReport r)
    {
        if (Throws) throw new IOException("The report couldn't be written.");
        Offload.Add(r);
        return $@"{FakeLayout.AppDataDir}\reports\{r.RunId}.json";
    }

    public string Save(CleanupReport r)
    {
        if (Throws) throw new IOException("The report couldn't be written.");
        Cleanup.Add(r);
        return $@"{FakeLayout.AppDataDir}\reports\{r.RunId}-cleanup.json";
    }
}

public sealed class FakeVolumeProvider(IReadOnlyList<VolumeInfo> volumes) : IVolumeProvider
{
    public IReadOnlyList<VolumeInfo> GetVolumes() => volumes;
}

public sealed class ListProgress<T> : IProgress<T>
{
    private readonly Lock _gate = new();
    private readonly List<T> _items = [];
    public List<T> Items { get { lock (_gate) { return [.. _items]; } } }
    public void Report(T value) { lock (_gate) { _items.Add(value); } }
}
```

```csharp
// tests/UasSort.Testing/Offload/OffloadVolumes.cs
using UasSort.Core;

namespace UasSort.Testing.Offload;

/// <summary>Destination volumes for the post-loop flush tests: C: NTFS fixed, D: exFAT on USB.</summary>
public static class OffloadVolumes
{
    public static readonly VolumeInfo NtfsC = new(@"C:\", new CardIdentity(0xC0C0C0C0, "Windows", "NTFS", 1_000_000_000_000), "Fixed",
        true, false, true, false, 500_000_000_000, "Nvme", false, true);
    public static readonly VolumeInfo ExFatD = new(@"D:\", new CardIdentity(0xD0D0D0D0, "Photos", "exFAT", 2_000_000_000_000), "Fixed",
        true, false, false, false, 1_000_000_000_000, "Usb", false, false);
}
```

```csharp
// tests/UasSort.Testing/Offload/OffloadRig.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing;

namespace UasSort.Testing.Offload;

/// <summary>One offload fixture: a Plan from OffloadPlanBuilder, its batch, the card files on Part 02's FakeFileSystem (FakeLayout),
/// Part 02's FakeCardReader and FakeFileOps (with the run's NewFolderDirs), and the shared FakeLedgerStore.</summary>
public sealed class OffloadRig
{
    public OffloadRig(OffloadPlanBuilder builder)
    {
        B = builder;
        Fs = FakeLayout.NewFileSystem();
        if (!PathRules.Equal(builder.PhotoRoot, FakeLayout.PhotoRoot))
        {
            Fs.AddDirectory(builder.PhotoRoot);
            Fs.Context = Fs.Context with { PhotoRoot = builder.PhotoRoot };
        }
    }

    public OffloadPlanBuilder B { get; }
    public FakeFileSystem Fs { get; }
    public Plan Plan { get; private set; } = null!;
    public OffloadBatch Batch { get; private set; } = null!;
    public FakeCardReader Reader { get; private set; } = null!;
    public FakeFileOps Files { get; private set; } = null!;
    public FakeLedgerStore Ledger { get; private set; } = null!;
    public FakeLedgerWriter Writer => Ledger.Writer;
    public FakeOffloadLock Lock { get; } = new();
    public FakePowerRequest Power { get; } = new();
    public FakeThumbnails Thumbnails { get; } = new();
    public MemReportStore Reports { get; } = new();
    public List<VolumeInfo> Volumes { get; } = [OffloadVolumes.NtfsC];
    public Dictionary<string, byte[]> CardData { get; } = new(StringComparer.OrdinalIgnoreCase);

    public OffloadRig Build(bool cardFiles = true, bool ledgerOnFileSystem = false)
    {
        Plan = B.Build();
        if (cardFiles && CardData.Count == 0)
        {
            int seed = 1;
            foreach (var e in B.Entries)
            {
                var data = Bytes(e.Size, seed++);
                CardData[e.RelPath] = data;
                Fs.AddFile(CardPath(e.RelPath), data, e.MtimeUtc, e.RawAttributes);
            }
        }
        Reader = new FakeCardReader(Fs, OffloadPlanBuilder.CardRoot, OffloadPlanBuilder.Card);
        Ledger = new FakeLedgerStore(ledgerOnFileSystem ? Fs : null, B.VideoRoot, FakeLayout.Machine, Plan.Base.Scan.Ledger);
        Rebatch(OffloadCompiler.Compile(Plan, "run-1"));
        return this;
    }

    public void Rebatch(OffloadBatch batch)
    {
        Batch = batch;
        Files = new FakeFileOps(Fs, OffloadCompiler.NewFolderDirs(batch, B.VideoRoot));
    }

    public static string CardPath(string cardRelPath) => PathRules.Join(OffloadPlanBuilder.CardRoot, cardRelPath);

    public static byte[] Bytes(long size, int seed)
    {
        var b = new byte[size];
        for (long i = 0; i < size; i++) b[i] = unchecked((byte)(((ulong)i + (ulong)seed * 7919UL) * 0x9E3779B97F4A7C15UL >> 56));
        return b;
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*OffloadRigTests" --filter-class "*FakeFileOpsTests" --filter-class "*ScanServiceTripwireTests"`
Expected: PASS (6 `OffloadRigTests`, Part 02's 10 `FakeFileOpsTests` unchanged, and every Part 06 `ScanServiceTripwireTests` case).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Testing/FakeFileOps.cs tests/UasSort.Testing/FakeLedgerStore.cs tests/UasSort.Testing/OffloadFakes.cs tests/UasSort.Testing/Offload/OffloadVolumes.cs tests/UasSort.Testing/Offload/OffloadRig.cs tests/UasSort.Core.Tests/Offload/OffloadRigTests.cs
git commit -m "test: extend the shared file-ops and ledger fakes for the offload and add the offload rig

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.5: `Preflight.Check` — the blocking list

**Files:**
- Create: `src/UasSort.Core/Offload/Preflight.cs`
- Test: `tests/UasSort.Core.Tests/Offload/PreflightBlockingTests.cs`

**Interfaces:**

```csharp
// Consumes: OffloadBatch, Plan, Settings, Issue/IssueCode/IssueSeverity, PreflightReport, VolumeNeed (Ref §3); IFileOps.TryGetSize/FreeBytes,
//   IDirectoryLister.Enumerate, ICardReader.CurrentIdentity, ILedgerStore.Check, IOffloadLock.TryAcquire (Ref §4.1); LedgerPaths.For;
//   OffloadPaths, Failures (07.1).
// Produces:
public static class Preflight                                        // Ref §4.2; writes nothing (no create, no delete, no open)
{
    public static PreflightReport Check(OffloadBatch batch, Plan plan, IFileOps files, IDirectoryLister lister, ICardReader card,
                                        ILedgerStore ledger, IOffloadLock offloadLock, Settings settings);
}
```

Blocking rules (Ref §10.2, catalogue messages of Ref §9.10), in this order; issues are de-duplicated on `(Code, Anchor, Message)`:
1. The plan's issues that are Blocking, marked `RequiresAckAtPreflight`, or `LedgerNoHistory` are carried onto the sheet unchanged.
2. `CurrentIdentity()` ≠ `batch.Card` → `CardIdentityChanged` "A different card is in E:; rescan"; `CurrentIdentity()` throws → `CardUnreadable`.
3. A non-recursive listing of the card root that fails or reports an error → `CardUnreadable` "The card's top level can't be read".
4. `IOffloadLock.TryAcquire()` returns null → `OffloadLockHeld` (a probe: the handle is released at once; `CommitSession` passes Preflight a stand-in lock while it holds the real one (Task 07.17), so no re-entrancy is needed).
5. The video root (always) and the photo root (when a photo job exists): missing, not a directory, or under the card root → `RootMissing` "{root} is not available; its items are unticked".
6. `Settings.RootsConfirmed == false` → `RootsUnconfirmed`.
7. `ILedgerStore.Check()`: `CloudOnly` → `LedgerCloudOnly`; `Unwritable` → `LedgerUnwritable`; `VideoRootMissing` → `RootMissing`; `Missing`/`Empty` → Info `LedgerNoHistory` "No history yet; this offload starts it" (not Blocking); `Check()` throwing IO → `LedgerUnlistable` "Can't list {folder}".
8. An Append `FolderPlan` (not `Create`) with jobs whose folder can't be listed → `AppendTargetGone`, anchored to the group.
9. Two jobs with the same destination (case-insensitive) → `DuplicateDestination`, anchored to the second job's item.
10. `DestPath + ".uas-sort.tmp"` longer than 400 characters → `TempPathTooLong`, anchored to the job's item.
11. Per destination volume (jobs already at the destination excluded): free &lt; Σ + max(1 GiB, ⌈Σ/50⌉) → `LowDiskSpace` "{drive} needs {required} free; {free} available" (decimal GB, 2 places).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/PreflightBlockingTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class PreflightBlockingTests
{
    internal const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";

    internal static PreflightReport Check(OffloadRig rig)
        => Preflight.Check(rig.Batch, rig.Plan, rig.Files, rig.Fs, rig.Reader, rig.Ledger, rig.Lock, rig.Plan.Base.Scan.Settings);

    internal static OffloadRig NewFolderRig(long size = 1_000, Action<OffloadPlanBuilder>? arrange = null, bool cardFiles = true)
    {
        var b = new OffloadPlanBuilder();
        var v = b.Video("DJI_20260927140000_0123_D.MP4", size, T0);
        b.Group(new NewFolder(ZRel), Zachar, v);
        arrange?.Invoke(b);
        return new OffloadRig(b).Build(cardFiles);
    }

    private static Issue Only(PreflightReport r, IssueCode code) => Assert.Single(r.Issues, i => i.Code == code);

    [Fact]
    public void CleanBatch_CanStart_AndWritesNothing()
    {
        var rig = NewFolderRig();
        var before = rig.Fs.GuardLog.Count;

        var r = Check(rig);

        Assert.True(r.CanStart);
        Assert.Equal([rig.B.NewFolderPath(ZRel)], r.FoldersToCreate.ToArray());
        var vol = Assert.Single(r.Volumes);
        Assert.Equal(@"C:\", vol.Volume);
        Assert.Equal(1, vol.Files);
        Assert.Equal(1_000, vol.Bytes);
        Assert.Equal(1_000 + (1L << 30), vol.RequiredFree);
        Assert.Equal(before, rig.Fs.GuardLog.Count);          // no guarded operation at all: nothing opened or created
        Assert.Equal(0, rig.Lock.Holds);
    }

    [Fact]
    public void IdentityChanged_Blocks()
    {
        var rig = NewFolderRig();
        rig.Fs.SetCardIdentity(CardRoot, OffloadPlanBuilder.Card with { VolumeSerial = 0xDEADBEEF });

        var i = Only(Check(rig), IssueCode.CardIdentityChanged);

        Assert.Equal(IssueSeverity.Blocking, i.Severity);
        Assert.Equal("A different card is in E:; rescan", i.Message);
    }

    [Fact]
    public void CardTopLevelUnreadable_Blocks()
    {
        var rig = NewFolderRig();
        rig.Fs.Faults.EnumerationErrors[CardRoot] = 5;

        Assert.Equal("The card's top level can't be read", Only(Check(rig), IssueCode.CardUnreadable).Message);
    }

    [Fact]
    public void LockHeldElsewhere_Blocks()
    {
        var rig = NewFolderRig();
        rig.Lock.HeldElsewhere = true;

        Assert.Equal("Another uas-sort window is offloading", Only(Check(rig), IssueCode.OffloadLockHeld).Message);
    }

    [Fact]
    public void VideoRootMissing_Blocks_ButAnUntargetedPhotoRootDoesNot()
    {
        var rig = NewFolderRig();
        rig.Fs.RemoveUnguarded(rig.B.PhotoRoot);
        Assert.DoesNotContain(Check(rig).Issues, i => i.Code == IssueCode.RootMissing);

        rig.Fs.RemoveUnguarded(rig.B.VideoRoot);
        var i = Only(Check(rig), IssueCode.RootMissing);
        Assert.Equal($"{rig.B.VideoRoot} is not available; its items are unticked", i.Message);
        Assert.False(Check(rig).CanStart);
    }

    [Fact]
    public void RootsUnconfirmed_Blocks()
        => Assert.Equal(IssueSeverity.Blocking,
                        Only(Check(NewFolderRig(arrange: b => b.RootsConfirmed(false))), IssueCode.RootsUnconfirmed).Severity);

    [Theory]
    [InlineData(LedgerFolderState.CloudOnly, IssueCode.LedgerCloudOnly)]
    [InlineData(LedgerFolderState.Unwritable, IssueCode.LedgerUnwritable)]
    [InlineData(LedgerFolderState.VideoRootMissing, IssueCode.RootMissing)]
    public void LedgerFolderProblems_Block(LedgerFolderState state, IssueCode code)
    {
        var rig = NewFolderRig();
        rig.Ledger.StatusOverride = rig.Plan.Base.Scan.Ledger.Status with { State = state };

        var r = Check(rig);

        Assert.Equal(IssueSeverity.Blocking, Only(r, code).Severity);
        Assert.False(r.CanStart);
    }

    [Fact]
    public void MissingLedgerFolder_IsInfoOnly()
    {
        var rig = NewFolderRig();
        rig.Ledger.StatusOverride = rig.Plan.Base.Scan.Ledger.Status with { State = LedgerFolderState.Missing, Exists = false };

        var r = Check(rig);

        var i = Only(r, IssueCode.LedgerNoHistory);
        Assert.Equal(IssueSeverity.Info, i.Severity);
        Assert.Equal("No history yet; this offload starts it", i.Message);
        Assert.True(r.CanStart);
    }

    [Fact]
    public void LedgerFolderUnlistable_Blocks()
    {
        var rig = NewFolderRig();
        rig.Ledger.CheckThrows = true;

        Assert.Equal($@"Can't list {rig.B.VideoRoot}\.uas-sort", Only(Check(rig), IssueCode.LedgerUnlistable).Message);
    }

    [Fact]
    public void AppendTargetGone_Blocks_AnchoredToTheGroup()
    {
        var b = new OffloadPlanBuilder();
        var folder = new LibraryFolderRef(b.NewFolderPath(ZRel), new DateOnly(2026, 9, 27), "Zachar Bay");
        var v = b.Video("DJI_20260927160000_0160_D.MP4", 1_000, T0.AddHours(2));
        b.Group(new Append(folder, Confidence.High, "same day as clips already in this folder", null), Zachar, v);
        var rig = new OffloadRig(b).Build();

        var i = Only(Check(rig), IssueCode.AppendTargetGone);

        Assert.Equal("'2026-09-27 Zachar Bay' was renamed or moved since the scan; rescan", i.Message);
        Assert.Equal(v, i.Anchor);
        rig.Fs.AddDirectory(folder.FullPath);
        Assert.DoesNotContain(Check(rig).Issues, x => x.Code == IssueCode.AppendTargetGone);
    }

    [Fact]
    public void DuplicateDestination_Blocks()
    {
        var rig = NewFolderRig();
        var copy = new ItemId("DCIM/DJI_001/copy.MP4");
        rig.Rebatch(rig.Batch with { Jobs = rig.Batch.Jobs.Add(rig.Batch.Jobs[0] with { Item = copy }) });

        var i = Only(Check(rig), IssueCode.DuplicateDestination);

        Assert.Equal(copy, i.Anchor);
        Assert.Equal($"Two files would be written to {rig.Batch.Jobs[0].DestPath}", i.Message);
    }

    [Fact]
    public void TempPathOver400Characters_Blocks()
    {
        var b = new OffloadPlanBuilder();
        var v = b.Video("DJI_20260927140000_0123_D.MP4", 1_000, T0);
        b.Group(new NewFolder(@"2026\2026-09\2026-09-27 " + new string('a', 330)), null, v);
        var rig = new OffloadRig(b).Build();

        var i = Only(Check(rig), IssueCode.TempPathTooLong);

        Assert.StartsWith("Path too long for OneDrive (437 > 400 characters): ", i.Message, StringComparison.Ordinal);
        Assert.Equal(v, i.Anchor);
    }

    [Fact]
    public void LowDiskSpace_UsesOneGiBOrTwoPercent()
    {
        var rig = NewFolderRig(size: 31_400_000_000, cardFiles: false);
        rig.Files.FreeBytesOverride = _ => 31_000_000_000;

        var r = Check(rig);

        Assert.Equal("C: needs 32.47 GB free; 31.00 GB available", Only(r, IssueCode.LowDiskSpace).Message);
        Assert.Equal(32_473_741_824, Assert.Single(r.Volumes).RequiredFree);
        rig.Files.FreeBytesOverride = _ => 32_473_741_824;
        Assert.DoesNotContain(Check(rig).Issues, i => i.Code == IssueCode.LowDiskSpace);

        var big = NewFolderRig(size: 100_000_000_000, cardFiles: false);
        Assert.Equal(102_000_000_000, Assert.Single(Check(big).Volumes).RequiredFree);
    }

    [Fact]
    public void PlanBlockingIssues_AreCarriedOnto_TheSheet()
    {
        var rig = NewFolderRig(arrange: b => b.Issue(new Issue(IssueSeverity.Blocking, IssueCode.EmptyFolderName, "Name this folder",
                                                               new ItemId("DCIM/DJI_001/DJI_20260927140000_0123_D.MP4"), [], false)));

        var r = Check(rig);

        Assert.Equal("Name this folder", Only(r, IssueCode.EmptyFolderName).Message);
        Assert.False(r.CanStart);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PreflightBlockingTests"`
Expected: build FAILS with CS0103 (`Preflight` does not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Offload/Preflight.cs
using System.Collections.Immutable;

namespace UasSort.Core.Offload;

/// <summary>Ref §10.2: the checks before Start offload. Writes nothing: listings, attribute reads, identity and a lock probe only.</summary>
public static class Preflight
{
    private const long OneGiB = 1L << 30;
    private static readonly IReadOnlySet<string> NoExclusions = ImmutableHashSet<string>.Empty;

    public static PreflightReport Check(OffloadBatch batch, Plan plan, IFileOps files, IDirectoryLister lister, ICardReader card,
                                        ILedgerStore ledger, IOffloadLock offloadLock, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(offloadLock);
        ArgumentNullException.ThrowIfNull(settings);

        var issues = new IssueList();
        var cardRoot = plan.Base.Scan.Inventory.Source.Root;
        var drive = OffloadPaths.DriveLabel(OffloadPaths.VolumeRoot(cardRoot));

        foreach (var i in plan.Issues)
            if (i.Severity == IssueSeverity.Blocking || i.RequiresAckAtPreflight || i.Code == IssueCode.LedgerNoHistory) issues.Add(i);

        try
        {
            if (card.CurrentIdentity() != batch.Card)
                issues.Block(IssueCode.CardIdentityChanged, $"A different card is in {drive}; rescan");
        }
        catch (Exception e) when (Failures.IsIo(e))
        {
            issues.Block(IssueCode.CardUnreadable, "The card's top level can't be read");
        }
        if (TryList(lister, cardRoot) is not { } top || top.Errors.Length > 0)
            issues.Block(IssueCode.CardUnreadable, "The card's top level can't be read");

        using (var probe = offloadLock.TryAcquire())
            if (probe is null) issues.Block(IssueCode.OffloadLockHeld, "Another uas-sort window is offloading");

        var targeted = new List<string> { settings.VideoRoot };
        if (batch.Jobs.Any(j => j.Root == DestRoot.Photo)) targeted.Add(settings.PhotoRoot);
        foreach (var root in targeted)
            if (OffloadPaths.IsUnder(root, cardRoot) || TryList(lister, root) is null)
                issues.Block(IssueCode.RootMissing, $"{root} is not available; its items are unticked");

        if (!settings.RootsConfirmed)
            issues.Block(IssueCode.RootsUnconfirmed, "Confirm the video and photo folders (settings were recovered)");

        var ledgerFolder = LedgerPaths.For(settings.VideoRoot);
        try
        {
            switch (ledger.Check().State)
            {
                case LedgerFolderState.CloudOnly:
                    issues.Block(IssueCode.LedgerCloudOnly,
                                 $"Set `{OffloadPaths.FileName(settings.VideoRoot)}\\{LedgerPaths.FolderName}` to Always keep on this device");
                    break;
                case LedgerFolderState.Unwritable:
                    issues.Block(IssueCode.LedgerUnwritable, $"Can't write the history file in {ledgerFolder}");
                    break;
                case LedgerFolderState.VideoRootMissing:
                    issues.Block(IssueCode.RootMissing, $"{settings.VideoRoot} is not available; its items are unticked");
                    break;
                case LedgerFolderState.Missing or LedgerFolderState.Empty:
                    issues.Info(IssueCode.LedgerNoHistory, "No history yet; this offload starts it");
                    break;
            }
        }
        catch (Exception e) when (Failures.IsIo(e))
        {
            issues.Block(IssueCode.LedgerUnlistable, $"Can't list {ledgerFolder}");
        }

        var groupsWithJobs = batch.Jobs.Where(j => j.Group is not null).Select(j => j.Group!.Value).ToHashSet();
        foreach (var f in batch.Folders.Where(f => !f.Create && groupsWithJobs.Contains(f.Group)))
            if (TryList(lister, f.FullPath) is null)
                issues.Block(IssueCode.AppendTargetGone,
                             $"'{OffloadPaths.FileName(f.FullPath)}' was renamed or moved since the scan; rescan", f.Group.Anchor);

        foreach (var dup in batch.Jobs.GroupBy(j => j.DestPath, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Block(IssueCode.DuplicateDestination, $"Two files would be written to {dup.Key}", dup.Skip(1).First().Item);

        var alreadyThere = new List<ItemId>();
        var alreadyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in batch.Jobs)
        {
            var temp = OffloadPaths.TempOf(job.DestPath);
            if (temp.Length > OffloadPaths.MaxTempPathLength)
                issues.Block(IssueCode.TempPathTooLong,
                             $"Path too long for OneDrive ({temp.Length} > {OffloadPaths.MaxTempPathLength} characters): {job.DestPath}", job.Item);
            if (SizeOf(files, job.DestPath) == job.Size)
            {
                if (!alreadyThere.Contains(job.Item)) alreadyThere.Add(job.Item);
                alreadyPaths.Add(job.DestPath);
                issues.Info(IssueCode.DestinationAlreadyThere, "Already at the destination (same size); recorded, not copied", job.Item);
            }
        }

        var volumes = ImmutableArray.CreateBuilder<VolumeNeed>();
        var byVolume = batch.Jobs.Where(j => !alreadyPaths.Contains(j.DestPath))
                                 .GroupBy(j => OffloadPaths.VolumeRoot(j.DestPath), StringComparer.OrdinalIgnoreCase);
        foreach (var vol in byVolume)
        {
            long sum = vol.Sum(j => j.Size);
            long required = sum + Math.Max(OneGiB, (sum + 49) / 50);
            long free;
            try { free = files.FreeBytes(vol.Key); }
            catch (Exception e) when (Failures.IsIo(e)) { free = 0; }
            volumes.Add(new VolumeNeed(vol.Key, vol.Count(), sum, free, required));
            if (free < required)
                issues.Block(IssueCode.LowDiskSpace,
                             $"{OffloadPaths.DriveLabel(vol.Key)} needs {OffloadPaths.Gb(required)} free; {OffloadPaths.Gb(free)} available");
        }

        var stale = ImmutableArray.CreateBuilder<string>();
        foreach (var dir in batch.Jobs.Select(j => OffloadPaths.DirectoryOf(j.DestPath)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (TryList(lister, dir) is { } listing)
                stale.AddRange(listing.Entries.Where(e => !e.IsDirectory && OffloadPaths.IsTemp(e.FullPath)).Select(e => e.FullPath));
        if (stale.Count > 0)
            issues.Info(IssueCode.StaleTempFiles, $"{stale.Count} unfinished temp files from an earlier run will be deleted when the offload starts");

        AddWarnings(plan, issues);

        var foldersToCreate = batch.Folders.Where(f => f.Create).Select(f => f.FullPath).ToImmutableArray();
        var appended = plan.Groups
            .Where(g => g.Target is Append && groupsWithJobs.Contains(g.Id))
            .Select(g => (((Append)g.Target).Folder.FullPath, ((Append)g.Target).Confidence))
            .ToImmutableArray();
        return new PreflightReport(issues.ToImmutable(), foldersToCreate, appended, volumes.ToImmutable(), stale.ToImmutable(),
                                   [.. alreadyThere]);
    }

    /// <summary>The sheet's warnings (Ref §10.2 "Warnings"): counted over the plan's items that are not ticked, plus ticked items whose
    /// date or place rests on an assumption (CheckDate, GpsGuessed, TzFallback, ClockFromSetting, ProbeFailed).</summary>
    private static void AddWarnings(Plan plan, IssueList issues)
    {
        const ItemFlags assumed = ItemFlags.CheckDate | ItemFlags.GpsGuessed | ItemFlags.TzFallback | ItemFlags.ClockFromSetting | ItemFlags.ProbeFailed;
        int assumptions = 0, probably = 0, unticked = 0, unfinished = 0, conflicts = 0;
        foreach (var item in plan.Base.Items)
        {
            bool included = plan.Included.Contains(item.Raw.Unit.Id);
            bool truncated = item.Flags.HasFlag(ItemFlags.Truncated) || item.Raw.Unit is VideoUnit { HasTrinf: true };
            if (included)
            {
                if ((item.Flags & assumed) != 0) assumptions++;
                continue;
            }
            switch (item.Newness)
            {
                case ProbablyImported when item.Raw.Unit is not VideoUnit: probably++; break;
                case IsNew when truncated: unfinished++; break;
                case IsNew: unticked++; break;
                case Conflict: conflicts++; break;
            }
        }
        if (assumptions > 0) issues.Warn(IssueCode.AssumptionsTicked, $"{assumptions} ticked items rely on assumptions");
        if (probably > 0) issues.Warn(IssueCode.ProbablyImportedLeftOut, $"{probably} probably-imported photos are left out");
        if (unticked > 0) issues.Warn(IssueCode.NewItemsUnticked, $"{unticked} new items are unticked");
        if (unfinished > 0) issues.Warn(IssueCode.UnfinishedRecordings, $"{unfinished} unfinished recordings");
        if (conflicts > 0) issues.Warn(IssueCode.ConflictsLeftOut, $"{conflicts} conflicts are left out");
    }

    /// <summary>A non-recursive listing of an existing directory, or null when it is missing, not a directory or unreadable.</summary>
    internal static ListingResult? TryList(IDirectoryLister lister, string dir)
    {
        try
        {
            var r = lister.Enumerate(dir, recurse: false, NoExclusions);
            return r.Errors.Any(e => OffloadPaths.Same(e.Path, dir)) ? null : r;
        }
        catch (Exception e) when (Failures.IsIo(e))
        {
            return null;
        }
    }

    private static long? SizeOf(IFileOps files, string path)
    {
        try { return files.TryGetSize(path, out var size) ? size : null; }
        catch (Exception e) when (Failures.IsIo(e)) { return null; }
    }

    private sealed class IssueList
    {
        private readonly List<Issue> _all = [];
        private readonly HashSet<(IssueCode, ItemId?, string)> _keys = [];

        public void Add(Issue i)
        {
            if (_keys.Add((i.Code, i.Anchor, i.Message))) _all.Add(i);
        }

        public void Block(IssueCode code, string message, ItemId? anchor = null)
            => Add(new Issue(IssueSeverity.Blocking, code, message, anchor, [], false));

        public void Warn(IssueCode code, string message, ItemId? anchor = null)
            => Add(new Issue(IssueSeverity.Warning, code, message, anchor, [], false));

        public void Info(IssueCode code, string message, ItemId? anchor = null)
            => Add(new Issue(IssueSeverity.Info, code, message, anchor, [], false));

        public ImmutableArray<Issue> ToImmutable() => [.. _all];
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PreflightBlockingTests"`
Expected: PASS (16 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/Preflight.cs tests/UasSort.Core.Tests/Offload/PreflightBlockingTests.cs
git commit -m "feat: add preflight blocking checks

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.6: Preflight actions, warnings and acknowledgements

**Files:**
- Create: `src/UasSort.Core/Offload/PreflightAcks.cs`
- Test: `tests/UasSort.Core.Tests/Offload/PreflightActionsTests.cs`

**Interfaces:**

```csharp
// Consumes: Preflight.Check (07.5), PreflightReport, Issue (Ref §3).
// Produces (defined here):
public sealed record AckKey(IssueCode Code, ItemId? Anchor, string Message);   // one sheet checkbox
public static class PreflightAcks
{
    public static AckKey Key(Issue issue);
    public static ImmutableArray<AckKey> Required(PreflightReport report);          // issues with RequiresAckAtPreflight, distinct
    public static bool CanStart(PreflightReport report, IReadOnlySet<AckKey> acknowledged); // no Blocking issue and every ack ticked
}
```

The action tests below exercise code written in Task 07.5 (stale temps listed, never deleted; same-size destinations become AlreadyThere; the five sheet warnings); the red step of this task is `PreflightAcks`, which does not exist yet.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/PreflightActionsTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Core.Tests.Offload.PreflightBlockingTests;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class PreflightActionsTests
{
    private static (OffloadRig Rig, ItemId Video, LibraryFolderRef Folder) AppendRig()
    {
        var b = new OffloadPlanBuilder();
        var folder = new LibraryFolderRef(b.NewFolderPath(ZRel), new DateOnly(2026, 9, 27), "Zachar Bay");
        var v = b.Video("DJI_20260927160000_0160_D.MP4", 1_000, T0.AddHours(2));
        b.Group(new Append(folder, Confidence.Medium, "different day, 34 mi from Council Road", null), Zachar, v);
        var rig = new OffloadRig(b).Build();
        rig.Fs.AddDirectory(folder.FullPath);
        return (rig, v, folder);
    }

    [Fact]
    public void StaleTemps_InDestinationDirectories_AreListedButNotDeleted()
    {
        var (rig, _, folder) = AppendRig();
        var stale = folder.FullPath + @"\DJI_20260927150000_0150_D.MP4.uas-sort.tmp";
        var elsewhere = rig.B.VideoRoot + @"\left.MP4.uas-sort.tmp";
        rig.Fs.AddFile(stale, 10, T0, 0x22);
        rig.Fs.AddFile(elsewhere, 10, T0, 0x22);

        var r = Check(rig);

        Assert.Equal([stale], r.StaleTemps.ToArray());
        var info = Assert.Single(r.Issues, i => i.Code == IssueCode.StaleTempFiles);
        Assert.Equal(IssueSeverity.Info, info.Severity);
        Assert.Equal("1 unfinished temp files from an earlier run will be deleted when the offload starts", info.Message);
        Assert.True(rig.Fs.Exists(stale));
        Assert.True(r.CanStart);
    }

    [Fact]
    public void SameSizeDestination_IsAlreadyThere_AndNeedsNoSpace()
    {
        var (rig, v, _) = AppendRig();
        rig.Fs.AddFile(rig.Batch.Jobs[0].DestPath, 1_000, T0);

        var r = Check(rig);

        Assert.Equal([v], r.AlreadyThere.ToArray());
        Assert.Equal(IssueSeverity.Info, Assert.Single(r.Issues, i => i.Code == IssueCode.DestinationAlreadyThere).Severity);
        Assert.Empty(r.Volumes);
        Assert.Equal([(rig.Batch.Folders[0].FullPath, Confidence.Medium)], r.FoldersAppended.ToArray());
    }

    [Fact]
    public void Warnings_CountWhatIsLeftOut_AndTickedAssumptions()
    {
        var b = new OffloadPlanBuilder();
        var ticked = b.Video("DJI_20260927140000_0001_D.MP4", 10, T0, flags: ItemFlags.CheckDate);
        var unticked = b.Video("DJI_20260927140100_0002_D.MP4", 10, T0.AddMinutes(1), included: false);
        var unfinished = b.Video("DJI_20260927140200_0003_D.MP4", 10, T0.AddMinutes(2), flags: ItemFlags.Truncated, included: false);
        var conflict = b.Video("DJI_20260927140300_0004_D.MP4", 10, T0.AddMinutes(3), new Conflict(@"C:\x\DJI_20260927140300_0004_D.MP4", 99), included: false);
        b.Photo("DJI_20260927140400_0005_D.DNG", 10, T0.AddMinutes(4), new ProbablyImported("videos from this day are already in the library"), included: false);
        b.Group(new NewFolder(ZRel), Zachar, ticked, unticked, unfinished, conflict);

        var r = Check(new OffloadRig(b).Build());

        string W(IssueCode c) => Assert.Single(r.Issues, i => i.Code == c && i.Severity == IssueSeverity.Warning).Message;
        Assert.Equal("1 ticked items rely on assumptions", W(IssueCode.AssumptionsTicked));
        Assert.Equal("1 probably-imported photos are left out", W(IssueCode.ProbablyImportedLeftOut));
        Assert.Equal("1 new items are unticked", W(IssueCode.NewItemsUnticked));
        Assert.Equal("1 unfinished recordings", W(IssueCode.UnfinishedRecordings));
        Assert.Equal("1 conflicts are left out", W(IssueCode.ConflictsLeftOut));
        Assert.True(r.CanStart);
    }

    [Fact]
    public void Acknowledgements_ComeFromTheRequiresAckIssues()
    {
        var anchor = new ItemId("DCIM/DJI_001/DJI_20260927140000_0123_D.MP4");
        var medium = new Issue(IssueSeverity.Warning, IssueCode.MediumAppend, "Appending to 'Council Road': different day, 34 mi", anchor, [], true);
        var split = new Issue(IssueSeverity.Warning, IssueCode.EmphasisedDaySplit, "Jul 25 → Jul 26 · 34 mi apart: likely separate outing", anchor, [], true);
        var info = new Issue(IssueSeverity.Info, IssueCode.SharedTarget, "Also targeted by another group", anchor, [], false);
        var rig = NewFolderRig(arrange: b => b.Issue(medium).Issue(split).Issue(info));

        var r = Check(rig);
        var required = PreflightAcks.Required(r);

        Assert.Equal([PreflightAcks.Key(medium), PreflightAcks.Key(split)], required.ToArray());
        Assert.DoesNotContain(r.Issues, i => i.Code == IssueCode.SharedTarget);
        Assert.False(PreflightAcks.CanStart(r, new HashSet<AckKey>()));
        Assert.False(PreflightAcks.CanStart(r, new HashSet<AckKey> { PreflightAcks.Key(medium) }));
        Assert.True(PreflightAcks.CanStart(r, required.ToHashSet()));

        rig.Lock.HeldElsewhere = true;
        Assert.False(PreflightAcks.CanStart(Check(rig), required.ToHashSet()));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PreflightActionsTests"`
Expected: build FAILS with CS0103 / CS0246 (`PreflightAcks`, `AckKey` do not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Offload/PreflightAcks.cs
using System.Collections.Immutable;

namespace UasSort.Core.Offload;

/// <summary>One acknowledgement checkbox on the preflight sheet (Ref §10.2, §9.10 "Ack at preflight").</summary>
public sealed record AckKey(IssueCode Code, ItemId? Anchor, string Message);

public static class PreflightAcks
{
    public static AckKey Key(Issue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return new AckKey(issue.Code, issue.Anchor, issue.Message);
    }

    public static ImmutableArray<AckKey> Required(PreflightReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return [.. report.Issues.Where(i => i.RequiresAckAtPreflight).Select(Key).Distinct()];
    }

    /// <summary>Start offload is enabled only with no Blocking issue and every acknowledgement ticked.</summary>
    public static bool CanStart(PreflightReport report, IReadOnlySet<AckKey> acknowledged)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(acknowledged);
        return report.CanStart && Required(report).All(acknowledged.Contains);
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*Preflight*Tests"`
Expected: PASS (20 tests: 16 blocking + 4 actions).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/PreflightAcks.cs tests/UasSort.Core.Tests/Offload/PreflightActionsTests.cs
git commit -m "feat: add preflight acknowledgements; test stale temps, AlreadyThere and sheet warnings

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.7: `CopyEngine` — the per-file protocol, `file`/`folder` records and progress

**Files:**
- Create: `src/UasSort.Core/Offload/OffloadRecords.cs`
- Create: `src/UasSort.Core/Offload/ProgressMeter.cs`
- Create: `src/UasSort.Core/Offload/CopyEngine.cs`
- Create: `tests/UasSort.Core.Tests/Offload/EngineRun.cs`
- Test: `tests/UasSort.Core.Tests/Offload/CopyEngineTests.cs`

**Interfaces:**

```csharp
// Consumes: CopyJob, OffloadBatch, FolderPlan, the CopyOutcome cases, VerifyResult/HashMatch/HashMismatch, RenameResult/Renamed/TargetExists,
//   OffloadResult, StopReason, OffloadProgress, FileRecord, FolderRecord, VolumeInfo (Ref §3–§4.1); ICardReader, IFileOps, ILedgerWriter;
//   System.IO.Hashing.XxHash128; JobMeta, OffloadCompiler.Describe (07.3); OffloadPaths, Failures (07.1); UnsafeIoException.
// Produces (defined here unless Ref-named):
public sealed record CopyEngineOptions(string Machine, IReadOnlyDictionary<string, JobMeta> Meta, IReadOnlyList<VolumeInfo> Volumes);
public sealed class CopyEngine(TimeProvider clock, CopyEngineOptions options)
{
    public const int ChunkBytes = 1 << 20;
    public Task<OffloadResult> RunAsync(OffloadBatch batch, ICardReader card, IFileOps files, ILedgerWriter ledger,
                                        IProgress<OffloadProgress> progress, CancellationToken ct);   // Ref §4.2
}
public static class OffloadRecords
{
    public const int Version = 1;
    public static FileRecord File(CopyOutcome outcome /* Verified or AlreadyThere */, string runId, string machine, DateTime atUtc, JobMeta? meta);
    public static FolderRecord Folder(FolderPlan folder, string source /* created|appended|cardLeftovers */, string runId, string machine);
    public static string NewId();                                   // Guid "D" format
}
internal sealed class ProgressMeter;                                 // 10 Hz throttle, rolling 5 s MB/s, ETA
```

Protocol as built in this task (Ref §10.3): step 0 identity (`CardSwapped` + stop; unreadable → `Failed(CardCheck)` + `CardRemoved`); step 1 `Stat` (size or mtime differ → `ChangedOnCard`); step 2 destination exists (same size → `AlreadyThere`; other size → `ConflictAtRename`); step 3 `EnsureDirectory(dir, job.CreatesFolder)` once per directory; steps 4–7 temp, 1 MiB chunks through `XxHash128`, flush, verify; step 8 `FinalizeAttributes`; step 9 no-replace rename (`TargetExists` → temp deleted, `ConflictAtRename`); step 10 `ConfirmFinal` (false → `Failed(Confirm)`); step 11 `file` record (failure → stop `LedgerWriteFailed`, the file stays `Verified`); a `folder` record after each group's last job when any of its files landed (created or appended); step 12 progress at 10 Hz. Cancel is checked between chunks (in-flight → `Cancelled`, temp deleted) and between jobs; an `UnsafeIoException` → `Failed(phase)` + `InternalSafetyStop`; after any stop the remaining jobs are `NotStarted`. Four private methods are deliberately minimal here and completed by the next tasks, each with its own red test: `ReadChunk` and `CardError` (Task 07.8: chunk re-read, card gone or swapped), `DestinationError` and `CopyVerified` (Task 07.9: disk full, destination lost, verify retry), `FlushDestinations` (Task 07.10).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/EngineRun.cs
using Microsoft.Extensions.Time.Testing;
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing;
using UasSort.Testing.Offload;

namespace UasSort.Core.Tests.Offload;

internal static class EngineRun
{
    public static FakeTimeProvider Clock() => new(new DateTimeOffset(OffloadPlanBuilder.T0.AddHours(1)));

    public static Task<OffloadResult> RunEngineAsync(this OffloadRig rig, TimeProvider? clock = null, CancellationToken ct = default,
                                                     IProgress<OffloadProgress>? progress = null)
    {
        var engine = new CopyEngine(clock ?? Clock(),
            new CopyEngineOptions(FakeLayout.Machine, OffloadCompiler.Describe(rig.Plan, rig.Batch), rig.Volumes));
        var writer = rig.Ledger.OpenOwn();
        return engine.RunAsync(rig.Batch, rig.Reader, rig.Files, writer, progress ?? new ListProgress<OffloadProgress>(), ct);
    }

    public static string[] Kinds(this OffloadResult r) => [.. r.Outcomes.Select(o => o.GetType().Name)];
}
```

```csharp
// tests/UasSort.Core.Tests/Offload/CopyEngineTests.cs
using System.IO.Hashing;
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class CopyEngineTests
{
    internal const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";

    /// <summary>A NewFolder group of videos with the given sizes, one minute apart.</summary>
    internal static OffloadRig Videos(params long[] sizes)
    {
        var b = new OffloadPlanBuilder();
        var ids = sizes.Select((s, i) => b.Video($"DJI_2026092714{i:00}00_{i + 1:0000}_D.MP4", s, T0.AddMinutes(i), gps: Zachar)).ToArray();
        b.Group(new NewFolder(ZRel), Zachar, ids);
        return new OffloadRig(b).Build();
    }

    [Fact]
    public async Task CopiesVerifiesAndRenames_WritingFileAndFolderRecords()
    {
        var rig = Videos(3 * CopyEngine.ChunkBytes + 5, CopyEngine.ChunkBytes);

        var r = await rig.RunEngineAsync();

        Assert.Null(r.Stop);
        Assert.Equal("run-1", r.RunId);
        Assert.Equal(["Verified", "Verified"], r.Kinds());
        foreach (var job in rig.Batch.Jobs)
        {
            Assert.Equal(rig.CardData[job.CardRelPath], rig.Fs.PeekContent(job.DestPath));
            Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(job.DestPath)));
            Assert.Equal(0u, rig.Fs.GetAttributes(job.DestPath)!.Value & 0x2u);
        }
        var records = rig.Writer.Records;
        Assert.Equal(["FileRecord", "FileRecord", "FolderRecord"], records.Select(x => x.GetType().Name).ToArray());
        var file = (FileRecord)records[0];
        var job0 = rig.Batch.Jobs[0];
        var verified = (Verified)r.Outcomes[0];
        Assert.Equal(XxHash128.HashToUInt128(rig.CardData[job0.CardRelPath]), verified.Hash);
        Assert.Equal(verified.Hash.ToString("x32", System.Globalization.CultureInfo.InvariantCulture), file.Xxh128);
        Assert.Equal(verified.Mode == VerifyMode.Cached ? "cached" : "unbuffered", file.Verify);
        Assert.Equal((1, "DESKTOP-A", "run-1", "video", "video"), (file.V, file.Machine, file.Run, file.Kind, file.Root));
        Assert.Equal(("DJI_20260927140000_0001_D.MP4", 3 * CopyEngine.ChunkBytes + 5L, job0.CardRelPath, job0.DestPath),
                     (file.Name, file.Size, file.Src, file.Dest));
        Assert.Equal((job0.CardMtimeUtc, (DateTime?)T0, "Mvhd", Tz, (DateOnly?)new DateOnly(2026, 9, 27)),
                     (file.Mtime, file.CaptureUtc, file.TimeSource, file.Tz, file.LocalDate));
        Assert.Equal((Zachar.Lat, Zachar.Lon), (file.Lat!.Value, file.Lon!.Value));
        var folder = (FolderRecord)records[2];
        Assert.Equal((rig.B.NewFolderPath(ZRel), "Zachar Bay", "created", "run-1", Tz), (folder.Path, folder.Desc, folder.Source, folder.Run, folder.Tz));
        rig.Fs.AssertNoViolations();
    }

    [Fact]
    public async Task DestinationWithTheSameSize_IsAlreadyThere_WithANameSizeRecord()
    {
        var rig = Videos(1_000);
        var dest = rig.Batch.Jobs[0].DestPath;
        rig.Fs.AddFile(dest, 1_000, T0);
        var before = rig.Fs.PeekContent(dest);

        var r = await rig.RunEngineAsync();

        Assert.Equal(["AlreadyThere"], r.Kinds());
        var file = Assert.IsType<FileRecord>(rig.Writer.Records[0]);
        Assert.Equal(("nameSize", (string?)null), (file.Verify, file.Xxh128));
        Assert.Equal(before, rig.Fs.PeekContent(dest));
        Assert.Equal("created", Assert.IsType<FolderRecord>(rig.Writer.Records[1]).Source);   // a file landed, so the group's folder is recorded
    }

    [Fact]
    public async Task DestinationWithAnotherSize_IsConflictAtRename_AndNothingIsWritten()
    {
        var rig = Videos(1_000);
        var dest = rig.Batch.Jobs[0].DestPath;
        rig.Fs.AddFile(dest, 7, T0);

        var r = await rig.RunEngineAsync();

        Assert.Equal(["ConflictAtRename"], r.Kinds());
        Assert.Null(r.Stop);
        Assert.Empty(rig.Writer.Records);
        Assert.Equal(7, rig.Fs.PeekContent(dest).Length);
    }

    [Fact]
    public async Task TargetAppearingBeforeTheRename_IsConflictAtRename_TheTempIsDeleted_AndTheRunContinues()
    {
        var rig = Videos(1_000, 2_000);
        var dest = rig.Batch.Jobs[0].DestPath;
        rig.Fs.Faults.TargetAppearsBeforeRename.Add(dest);

        var r = await rig.RunEngineAsync();

        Assert.Equal(["ConflictAtRename", "Verified"], r.Kinds());
        byte[] marker = [0x42];                                          // the 1-byte file Part 02's FakeFileOps puts there
        Assert.Equal(marker, rig.Fs.PeekContent(dest));
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(dest)));
        Assert.Null(r.Stop);
    }

    [Fact]
    public async Task WrongSizeAfterTheRename_IsFailedConfirm_AndTheRunContinues()
    {
        var rig = Videos(1_000, 2_000);
        rig.Fs.Faults.SizeAfterRename[rig.Batch.Jobs[0].DestPath] = 999;

        var r = await rig.RunEngineAsync();

        Assert.Equal(CopyPhase.Confirm, Assert.IsType<Failed>(r.Outcomes[0]).Phase);
        Assert.IsType<Verified>(r.Outcomes[1]);
        Assert.Null(r.Stop);
        Assert.Single(rig.Writer.Records.OfType<FileRecord>());
    }

    [Fact]
    public async Task IdentityChangeAtStepZero_IsCardSwapped_AndStops()
    {
        var rig = Videos(1_000, 2_000, 3_000);
        var other = OffloadPlanBuilder.Card with { VolumeSerial = 0xDEADBEEF };
        rig.Fs.Faults.IdentityOnCall = n => n >= 2 ? other : null;

        var r = await rig.RunEngineAsync();

        Assert.Equal(["Verified", "CardSwapped", "NotStarted"], r.Kinds());
        Assert.Equal(other, ((CardSwapped)r.Outcomes[1]).Now);
        Assert.Equal(StopReason.CardSwapped, r.Stop);
        Assert.False(rig.Fs.Exists(rig.Batch.Jobs[1].DestPath));
    }

    [Fact]
    public async Task CardFileChangedSinceTheScan_IsChangedOnCard_AndTheRunContinues()
    {
        var rig = Videos(1_000, 2_000);
        var job = rig.Batch.Jobs[0];
        rig.Fs.AddFile(OffloadRig.CardPath(job.CardRelPath), 1_001, job.CardMtimeUtc);

        var r = await rig.RunEngineAsync();

        var changed = Assert.IsType<ChangedOnCard>(r.Outcomes[0]);
        Assert.Equal(1_001, changed.NowSize);
        Assert.IsType<Verified>(r.Outcomes[1]);
        Assert.Null(r.Stop);
    }

    [Fact]
    public async Task LedgerAppendFailure_KeepsTheFileVerified_AndStops()
    {
        var rig = Videos(1_000, 2_000);
        rig.Writer.ThrowWhen = x => x is FileRecord;

        var r = await rig.RunEngineAsync();

        Assert.Equal(["Verified", "NotStarted"], r.Kinds());
        Assert.Equal(StopReason.LedgerWriteFailed, r.Stop);
        Assert.True(rig.Fs.Exists(rig.Batch.Jobs[0].DestPath));
        Assert.Empty(rig.Writer.Records);
    }

    [Fact]
    public async Task CancelBetweenChunks_CancelsTheInFlightFile_AndLeavesNoTempAndNoRecord()
    {
        var rig = Videos(1_000, 2 * CopyEngine.ChunkBytes + 1, 3_000);
        using var cts = new CancellationTokenSource();
        var second = rig.Batch.Jobs[1].DestPath;
        rig.Files.OnTempWrite = (final, _) => { if (string.Equals(final, second, StringComparison.OrdinalIgnoreCase)) cts.Cancel(); };

        var r = await rig.RunEngineAsync(ct: cts.Token);

        Assert.Equal(["Verified", "Cancelled", "NotStarted"], r.Kinds());
        Assert.Equal(StopReason.Cancelled, r.Stop);
        Assert.False(rig.Fs.Exists(second));
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(second)));
        Assert.Equal(["FileRecord", "FolderRecord"], rig.Writer.Records.Select(x => x.GetType().Name).ToArray());
    }

    [Fact]
    public async Task UnsafeIo_IsAnInternalSafetyStop()
    {
        var rig = Videos(1_000, 2_000);
        rig.Files.ThrowOnFinalize = new UnsafeIoException("SetAttributesOrTimes refused");

        var r = await rig.RunEngineAsync();

        Assert.Equal(CopyPhase.Finalize, Assert.IsType<Failed>(r.Outcomes[0]).Phase);
        Assert.IsType<NotStarted>(r.Outcomes[1]);
        Assert.Equal(StopReason.InternalSafetyStop, r.Stop);
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(rig.Batch.Jobs[0].DestPath)));
    }

    [Fact]
    public async Task Progress_IsThrottledToTenPerSecond_WithRollingSpeed()
    {
        var rig = Videos(6 * CopyEngine.ChunkBytes);
        var clock = EngineRun.Clock();
        rig.Files.OnTempWrite = (_, _) => clock.Advance(TimeSpan.FromMilliseconds(50));
        var progress = new ListProgress<OffloadProgress>();

        await rig.RunEngineAsync(clock, progress: progress);

        var items = progress.Items;
        Assert.Equal(4, items.Count);
        Assert.Equal([2 * CopyEngine.ChunkBytes, 4L * CopyEngine.ChunkBytes, 6L * CopyEngine.ChunkBytes, 6L * CopyEngine.ChunkBytes],
                     items.Select(p => p.BytesDone).ToArray());
        Assert.Equal(21.0, items[2].MBps);
        Assert.InRange(items[0].Eta!.Value.TotalSeconds, 0.19, 0.21);
        Assert.Equal((1, 1, "DJI_20260927140000_0001_D.MP4"), (items[3].FilesDone, items[3].FilesTotal, items[3].CurrentFile));
        Assert.Equal(new GroupId(rig.Batch.Jobs[0].Item), items[3].Group);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CopyEngineTests"`
Expected: build FAILS with CS0246 (`CopyEngine`, `CopyEngineOptions` do not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Offload/OffloadRecords.cs
using System.Globalization;

namespace UasSort.Core.Offload;

/// <summary>Ledger records written during Commit (Ref §10.4, fields as Ref §11).</summary>
public static class OffloadRecords
{
    public const int Version = 1;

    public static string NewId() => Guid.NewGuid().ToString();

    public static FileRecord File(CopyOutcome outcome, string runId, string machine, DateTime atUtc, JobMeta? meta)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var job = outcome.Job;
        var (xxh, verify) = outcome switch
        {
            Verified v => (v.Hash.ToString("x32", CultureInfo.InvariantCulture), v.Mode == VerifyMode.Cached ? "cached" : "unbuffered"),
            AlreadyThere => ((string?)null, "nameSize"),
            _ => throw new ArgumentException("Only Verified and AlreadyThere outcomes get a file record.", nameof(outcome)),
        };
        var root = job.Root == DestRoot.Video ? "video" : "photo";
        return new FileRecord(Version, NewId(), machine, runId, atUtc, meta?.Kind ?? root, OffloadPaths.FileName(job.CardRelPath),
            job.Size, job.CardRelPath, root, job.DestPath, xxh, verify, job.CardMtimeUtc, meta?.CaptureUtc, meta?.TimeSource?.ToString(),
            meta?.Point?.Lat, meta?.Point?.Lon, meta?.TzId, meta?.LocalDate, meta?.Session?.SessionUtc, meta?.Session?.DroneSerial, meta?.Set);
    }

    public static FolderRecord Folder(FolderPlan folder, string source, string runId, string machine)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new FolderRecord(Version, NewId(), machine, runId, folder.FullPath, folder.Description, source,
                                folder.Centroid?.Lat, folder.Centroid?.Lon, folder.Start, folder.End, folder.TzId);
    }
}
```

```csharp
// src/UasSort.Core/Offload/ProgressMeter.cs
using System.Collections.Immutable;

namespace UasSort.Core.Offload;

/// <summary>Step 12: progress at most every 100 ms while copying, plus once at the end of every job; MB/s over the last 5 s.</summary>
internal sealed class ProgressMeter
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);
    private readonly TimeProvider _clock;
    private readonly IProgress<OffloadProgress> _sink;
    private readonly int _filesTotal;
    private readonly long _bytesTotal;
    private readonly Queue<(DateTime At, long Bytes)> _samples = new();
    private int _filesDone;
    private long _bytesDone;
    private long _jobStartBytes;
    private CopyJob? _job;
    private CopyPhase _phase;
    private DateTime _last;

    public ProgressMeter(TimeProvider clock, ImmutableArray<CopyJob> jobs, IProgress<OffloadProgress> sink)
    {
        _clock = clock;
        _sink = sink;
        _filesTotal = jobs.Length;
        _bytesTotal = jobs.Sum(j => j.Size);
        _last = Now();
        _samples.Enqueue((_last, 0));
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

    public void Begin(CopyJob job)
    {
        _job = job;
        _jobStartBytes = _bytesDone;
        _phase = CopyPhase.CardCheck;
    }

    public void Phase(CopyPhase phase) => _phase = phase;

    public void Bytes(int count)
    {
        _bytesDone += count;
        var now = Now();
        if (now - _last >= Interval) Report(now);
    }

    public void End(CopyJob job)
    {
        _filesDone++;
        _bytesDone = _jobStartBytes + job.Size;
        Report(Now());
    }

    private void Report(DateTime now)
    {
        _last = now;
        _samples.Enqueue((now, _bytesDone));
        while (_samples.Count > 1 && now - _samples.Peek().At > Window) _samples.Dequeue();
        var (at0, bytes0) = _samples.Peek();
        double seconds = (now - at0).TotalSeconds;
        double mbps = seconds > 0 ? (_bytesDone - bytes0) / seconds / 1e6 : 0;
        TimeSpan? eta = mbps > 0 ? TimeSpan.FromSeconds((_bytesTotal - _bytesDone) / (mbps * 1e6)) : null;
        _sink.Report(new OffloadProgress(_filesDone, _filesTotal, _bytesDone, _bytesTotal, Math.Round(mbps, 1), eta,
            _job is null ? null : OffloadPaths.FileName(_job.CardRelPath), _phase, _job?.Group));
    }
}
```

```csharp
// src/UasSort.Core/Offload/CopyEngine.cs
using System.Buffers;
using System.Collections.Immutable;
using System.IO.Hashing;

namespace UasSort.Core.Offload;

/// <summary>What CopyEngine needs besides the batch: this PC's name for ledger records, the per-job facts for `file` records
/// (OffloadCompiler.Describe) and the volumes, for the post-run flush (Ref §10.3 "After the loop").</summary>
public sealed record CopyEngineOptions(string Machine, IReadOnlyDictionary<string, JobMeta> Meta, IReadOnlyList<VolumeInfo> Volumes);

/// <summary>Ref §10.3–§10.4: copies one file at a time with verification, never overwriting; the card is only read.</summary>
public sealed class CopyEngine(TimeProvider clock, CopyEngineOptions options)
{
    public const int ChunkBytes = 1 << 20;

    private readonly record struct Step(CopyOutcome Outcome, StopReason? Stop);
    private readonly record struct ChunkRead(int Count, Step? Fault);
    private readonly record struct DataResult(UInt128 Hash, Step? Fault);
    private readonly record struct CopyResult(UInt128 Hash, VerifyMode Mode, bool Mismatch, Step? Fault);

    public Task<OffloadResult> RunAsync(OffloadBatch batch, ICardReader card, IFileOps files, ILedgerWriter ledger,
                                        IProgress<OffloadProgress> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(progress);
        return Task.Run(() => Run(batch, card, files, ledger, progress, ct), CancellationToken.None);
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private JobMeta? MetaOf(CopyJob job) => options.Meta.TryGetValue(job.CardRelPath, out var m) ? m : null;

    private OffloadResult Run(OffloadBatch batch, ICardReader card, IFileOps files, ILedgerWriter ledger,
                              IProgress<OffloadProgress> progress, CancellationToken ct)
    {
        var start = Now();
        var jobs = batch.Jobs;
        var outcomes = new CopyOutcome[jobs.Length];
        var meter = new ProgressMeter(clock, jobs, progress);
        var ensured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var renamed = new List<string>();
        var landed = new List<GroupId>();
        var folderDone = new HashSet<GroupId>();
        var lastOfGroup = new Dictionary<GroupId, int>();
        for (int i = 0; i < jobs.Length; i++)
            if (jobs[i].Group is { } g) lastOfGroup[g] = i;
        StopReason? stop = null;

        bool WriteFolder(GroupId g)
        {
            folderDone.Add(g);
            var plan = batch.Folders.First(f => f.Group == g);
            return TryAppend(ledger, OffloadRecords.Folder(plan, plan.Create ? "created" : "appended", batch.RunId, options.Machine));
        }

        for (int i = 0; i < jobs.Length; i++)
        {
            var job = jobs[i];
            if (stop is null && ct.IsCancellationRequested) stop = StopReason.Cancelled;
            if (stop is not null)
            {
                outcomes[i] = new NotStarted(job);
                continue;
            }
            meter.Begin(job);
            var step = CopyOne(batch, job, card, files, ensured, meter, ct);
            outcomes[i] = step.Outcome;
            stop = step.Stop;
            if (step.Outcome is Verified) renamed.Add(job.DestPath);
            if (step.Outcome is Verified or AlreadyThere)
            {
                if (job.Group is { } lg && !landed.Contains(lg)) landed.Add(lg);
                if (!TryAppend(ledger, OffloadRecords.File(step.Outcome, batch.RunId, options.Machine, Now(), MetaOf(job))))
                    stop = StopReason.LedgerWriteFailed;
            }
            if (stop is null && job.Group is { } done && lastOfGroup[done] == i && landed.Contains(done) && !WriteFolder(done))
                stop = StopReason.LedgerWriteFailed;
            meter.End(job);
        }
        if (stop != StopReason.LedgerWriteFailed)
            foreach (var g in landed.Where(g => !folderDone.Contains(g)).ToList())
                if (!WriteFolder(g)) break;

        var safeRemoval = FlushDestinations(files, renamed);
        return new OffloadResult(batch.RunId, [.. outcomes], stop, start, Now(), safeRemoval);
    }

    private static bool TryAppend(ILedgerWriter ledger, LedgerRecord record)
    {
        try
        {
            ledger.Append(record);
            return true;
        }
        catch (UnsafeIoException)
        {
            return false;
        }
        catch (Exception e) when (Failures.IsIo(e))
        {
            return false;
        }
    }

    private Step CopyOne(OffloadBatch batch, CopyJob job, ICardReader card, IFileOps files, HashSet<string> ensured,
                         ProgressMeter meter, CancellationToken ct)
    {
        var phase = CopyPhase.CardCheck;
        string? temp = null;
        try
        {
            // step 0: the card in the reader is still the pinned one
            CardIdentity now;
            try { now = card.CurrentIdentity(); }
            catch (Exception e) when (Failures.IsIo(e)) { return new Step(new Failed(job, CopyPhase.CardCheck, e.Message), StopReason.CardRemoved); }
            if (now != batch.Card) return new Step(new CardSwapped(job, now), StopReason.CardSwapped);

            // step 1: the card file is unchanged since the scan
            phase = CopyPhase.Stat;
            meter.Phase(phase);
            FsEntry stat;
            try { stat = card.Stat(job.CardRelPath); }
            catch (Exception e) when (Failures.IsIo(e))
            {
                return CardError(batch, job, card, CopyPhase.Stat, e, new ChangedOnCard(job, -1, DateTime.MinValue));
            }
            if (stat.Size != job.Size || stat.MtimeUtc != job.CardMtimeUtc)
                return new Step(new ChangedOnCard(job, stat.Size, stat.MtimeUtc), null);

            // step 2: never overwrite
            if (files.TryGetSize(job.DestPath, out var existing))
            {
                if (existing == job.Size) return new Step(new AlreadyThere(job), null);
                return new Step(new ConflictAtRename(job), null);
            }

            // step 3: the folder (created only for NewFolder paths; Append targets must exist)
            phase = CopyPhase.CreateTemp;
            var dir = OffloadPaths.DirectoryOf(job.DestPath);
            if (!ensured.Contains(dir))
            {
                files.EnsureDirectory(dir, job.CreatesFolder);
                ensured.Add(dir);
            }

            // steps 4–7
            var copied = CopyVerified(batch, job, card, files, meter, ct, ref phase, ref temp);
            if (copied.Fault is { } fault)
            {
                DeleteQuietly(files, temp);
                return fault;
            }

            // step 8: card times, and Hidden cleared before the rename
            phase = CopyPhase.Finalize;
            meter.Phase(phase);
            files.FinalizeAttributes(temp!, job.CardCreationUtc, job.CardMtimeUtc);

            // step 9: no-replace rename
            phase = CopyPhase.Rename;
            meter.Phase(phase);
            if (files.RenameNoReplace(temp!, job.DestPath) is TargetExists)
            {
                DeleteQuietly(files, temp);
                return new Step(new ConflictAtRename(job), null);
            }
            temp = null;

            // step 10: confirm from metadata
            phase = CopyPhase.Confirm;
            meter.Phase(phase);
            if (!files.ConfirmFinal(job.DestPath, job.Size))
                return new Step(new Failed(job, CopyPhase.Confirm, "The file's name or size is wrong after the rename"), null);
            return new Step(new Verified(job, copied.Hash, copied.Mode), null);
        }
        catch (UnsafeIoException e)
        {
            DeleteQuietly(files, temp);
            return new Step(new Failed(job, phase, e.Message), StopReason.InternalSafetyStop);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DeleteQuietly(files, temp);
            return new Step(new Cancelled(job), StopReason.Cancelled);
        }
        catch (Exception e) when (Failures.IsIo(e))
        {
            DeleteQuietly(files, temp);
            return DestinationError(job, files, phase, e);
        }
    }

    // Completed in Task 07.9 (retry once on a mismatch).
    private CopyResult CopyVerified(OffloadBatch batch, CopyJob job, ICardReader card, IFileOps files, ProgressMeter meter,
                                    CancellationToken ct, ref CopyPhase phase, ref string? temp)
    {
        var once = CopyOnce(batch, job, card, files, meter, ct, ref phase, ref temp);
        if (!once.Mismatch) return once;
        return new CopyResult(default, default, false, new Step(new Failed(job, CopyPhase.Verify, "The copy didn't match the card"), null));
    }

    /// <summary>Steps 4–7 once: create the temp, copy through the hash, flush, verify.</summary>
    private CopyResult CopyOnce(OffloadBatch batch, CopyJob job, ICardReader card, IFileOps files, ProgressMeter meter,
                                CancellationToken ct, ref CopyPhase phase, ref string? temp)
    {
        phase = CopyPhase.CreateTemp;
        meter.Phase(phase);
        var stream = files.CreateTemp(job.DestPath, job.Size, out var tempPath);
        temp = tempPath;
        UInt128 hash;
        try
        {
            var data = CopyData(batch, job, card, stream, meter, ct, ref phase);
            if (data.Fault is { } fault) return new CopyResult(default, default, false, fault);
            hash = data.Hash;
            phase = CopyPhase.Flush;
            meter.Phase(phase);
            files.FlushToDisk(stream);
        }
        finally
        {
            stream.Dispose();
        }

        phase = CopyPhase.Verify;
        meter.Phase(phase);
        var check = files.VerifyHash(tempPath, job.Size, hash, ct);
        if (check is HashMatch m) return new CopyResult(hash, m.Mode, false, null);
        return new CopyResult(default, default, true, null);
    }

    private DataResult CopyData(OffloadBatch batch, CopyJob job, ICardReader card, Stream dest, ProgressMeter meter,
                                CancellationToken ct, ref CopyPhase phase)
    {
        var hasher = new XxHash128();
        var buffer = ArrayPool<byte>.Shared.Rent(ChunkBytes);
        Stream? src = null;
        try
        {
            phase = CopyPhase.Copy;
            meter.Phase(phase);
            for (long offset = 0; offset < job.Size;)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(ChunkBytes, job.Size - offset);
                var read = ReadChunk(batch, job, card, ref src, buffer, offset, want);
                if (read.Fault is { } fault) return new DataResult(default, fault);
                if (read.Count != want)
                    return new DataResult(default, new Step(
                        new Failed(job, CopyPhase.Copy, $"The card returned {offset + read.Count} of {job.Size} bytes"), null));
                hasher.Append(buffer.AsSpan(0, want));
                dest.Write(buffer, 0, want);
                offset += want;
                meter.Bytes(want);
            }
            return new DataResult(hasher.GetCurrentHashAsUInt128(), null);
        }
        finally
        {
            src?.Dispose();
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static int ReadFully(Stream src, byte[] buffer, int want)
    {
        int total = 0;
        while (total < want)
        {
            int n = src.Read(buffer, total, want - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    // Completed in Task 07.8 (one re-read of the chunk at the same offset).
    private static ChunkRead ReadChunk(OffloadBatch batch, CopyJob job, ICardReader card, ref Stream? src, byte[] buffer,
                                       long offset, int want)
    {
        try
        {
            src ??= card.OpenSequential(job.CardRelPath);
            return new ChunkRead(ReadFully(src, buffer, want), null);
        }
        catch (Exception e) when (Failures.IsIo(e))
        {
            return new ChunkRead(0, CardError(batch, job, card, CopyPhase.Copy, e, new Failed(job, CopyPhase.Copy, e.Message)));
        }
    }

    // Completed in Task 07.8 (card gone → CardRemoved; another card → CardSwapped).
    private static Step CardError(OffloadBatch batch, CopyJob job, ICardReader card, CopyPhase phase, Exception error, CopyOutcome whenPresent)
        => new(whenPresent, null);

    // Completed in Task 07.9 (disk full → DestinationFull; volume gone → DestinationLost).
    private static Step DestinationError(CopyJob job, IFileOps files, CopyPhase phase, Exception error)
        => new(new Failed(job, phase, error.Message), null);

    // Completed in Task 07.10 (non-NTFS / removable destinations).
    private static ImmutableArray<string> FlushDestinations(IFileOps files, List<string> renamed) => [];

    private static void DeleteQuietly(IFileOps files, string? temp)
    {
        if (temp is null) return;
        try
        {
            files.DeleteOwnTemp(temp);
        }
        catch (UnsafeIoException)
        {
            // a refused delete is already recorded by the guard; the in-flight outcome says what happened
        }
        catch (Exception e) when (Failures.IsIo(e))
        {
            // unreachable destination: the temp stays and the next preflight lists it (Ref §10.3 invariants)
        }
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CopyEngineTests"`
Expected: PASS (11 tests). `CardError`, `DestinationError` and `FlushDestinations` do not use every parameter yet; Tasks 07.8–07.10 replace them with bodies that do. If the build enforces IDE0060 (unused parameter) as an error, put `[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060", Justification = "Completed in Tasks 07.8-07.10")]` on those three methods and drop it when each is replaced.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/OffloadRecords.cs src/UasSort.Core/Offload/ProgressMeter.cs src/UasSort.Core/Offload/CopyEngine.cs tests/UasSort.Core.Tests/Offload/EngineRun.cs tests/UasSort.Core.Tests/Offload/CopyEngineTests.cs
git commit -m "feat: add the copy engine's per-file protocol with file and folder records

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.8: Card-side faults — chunk re-read, card removed, card swapped

**Files:**
- Modify: `src/UasSort.Core/Offload/CopyEngine.cs` (replace `ReadChunk` and `CardError`)
- Test: `tests/UasSort.Core.Tests/Offload/CopyEngineCardFaultTests.cs`

**Interfaces:**

```csharp
// Consumes: CopyEngine (07.7); FakeFaults.TransientCardReadError / PersistentCardReadError / CardVanishesAfterBytes / IdentityOnCall (Part 02;
//   a transient error fires once per opened stream, so the re-read uses the same stream, seeked back to the chunk's offset).
// Produces: no new API. Behaviour (Ref §10.3 step 5 and outcome rows 2–4): a card read error re-reads that chunk once at the same offset;
//   a second failure asks CurrentIdentity(): throws → in-flight Failed(Copy) + stop CardRemoved; a different identity → in-flight
//   CardSwapped + stop CardSwapped; the same card → Failed(Copy) and the run continues. The temp is deleted in every case.
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/CopyEngineCardFaultTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Core.Tests.Offload.CopyEngineTests;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class CopyEngineCardFaultTests
{
    [Fact]
    public async Task TransientReadError_ReReadsTheChunkOnce_AndTheCopyCompletes()
    {
        var rig = Videos(2 * CopyEngine.ChunkBytes + 7);
        var job = rig.Batch.Jobs[0];
        rig.Fs.Faults.TransientCardReadError.Add(job.CardRelPath);

        var r = await rig.RunEngineAsync();

        Assert.IsType<Verified>(r.Outcomes[0]);
        Assert.Equal(rig.CardData[job.CardRelPath], rig.Fs.PeekContent(job.DestPath));
        Assert.Null(r.Stop);
    }

    [Fact]
    public async Task PersistentReadError_WithTheCardPresent_FailsThatFile_AndContinues()
    {
        var rig = Videos(1_000, 2_000);
        var job = rig.Batch.Jobs[0];
        rig.Fs.Faults.PersistentCardReadError.Add(job.CardRelPath);

        var r = await rig.RunEngineAsync();

        Assert.Equal(CopyPhase.Copy, Assert.IsType<Failed>(r.Outcomes[0]).Phase);
        Assert.IsType<Verified>(r.Outcomes[1]);
        Assert.Null(r.Stop);
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(job.DestPath)));
    }

    [Fact]
    public async Task CardVanishingMidFile_IsFailedCopy_StopsWithCardRemoved()
    {
        var rig = Videos(2 * CopyEngine.ChunkBytes, 1_000);
        var job = rig.Batch.Jobs[0];
        rig.Fs.Faults.CardVanishesAfterBytes[job.CardRelPath] = CopyEngine.ChunkBytes + 10;

        var r = await rig.RunEngineAsync();

        Assert.Equal(["Failed", "NotStarted"], r.Kinds());
        Assert.Equal(CopyPhase.Copy, ((Failed)r.Outcomes[0]).Phase);
        Assert.Equal(StopReason.CardRemoved, r.Stop);
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(job.DestPath)));
        Assert.False(rig.Fs.Exists(job.DestPath));
    }

    [Fact]
    public async Task ADifferentCardAfterAReadError_IsCardSwapped_AndStops()
    {
        var rig = Videos(1_000, 2_000, 3_000);
        var job = rig.Batch.Jobs[1];
        var other = OffloadPlanBuilder.Card with { VolumeSerial = 0xDEADBEEF };
        rig.Fs.Faults.PersistentCardReadError.Add(job.CardRelPath);
        rig.Fs.Faults.IdentityOnCall = n => n >= 3 ? other : null;   // 1, 2: step 0 of jobs 1 and 2; 3: after the failed re-read

        var r = await rig.RunEngineAsync();

        Assert.Equal(["Verified", "CardSwapped", "NotStarted"], r.Kinds());
        Assert.Equal(StopReason.CardSwapped, r.Stop);
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(job.DestPath)));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CopyEngineCardFaultTests"`
Expected: FAIL — `TransientReadError…` (outcome is `Failed`, not `Verified`), `CardVanishingMidFile…` (`Stop` is null, not `CardRemoved`) and `ADifferentCardAfterAReadError…` (`Failed` instead of `CardSwapped`); `PersistentReadError…` passes.

- [ ] **Step 3: Implement** — in `src/UasSort.Core/Offload/CopyEngine.cs`, replace the whole `ReadChunk` method and the whole `CardError` method (with their "Completed in Task 07.8" comments) by:

```csharp
    /// <summary>Step 5: read one chunk; on a card read error re-read it once at the same offset (same stream, seeked back;
    /// reopened only if the stream can't seek). A second failure goes to CardError.</summary>
    private static ChunkRead ReadChunk(OffloadBatch batch, CopyJob job, ICardReader card, ref Stream? src, byte[] buffer,
                                       long offset, int want)
    {
        try
        {
            src ??= card.OpenSequential(job.CardRelPath);
            return new ChunkRead(ReadFully(src, buffer, want), null);
        }
        catch (Exception first) when (Failures.IsIo(first))
        {
            try
            {
                if (src is null || !src.CanSeek)
                {
                    src?.Dispose();
                    src = card.OpenSequential(job.CardRelPath);
                }
                src.Seek(offset, SeekOrigin.Begin);
                return new ChunkRead(ReadFully(src, buffer, want), null);
            }
            catch (Exception second) when (Failures.IsIo(second) || second is NotSupportedException)
            {
                return new ChunkRead(0, CardError(batch, job, card, CopyPhase.Copy, second, new Failed(job, CopyPhase.Copy, second.Message)));
            }
        }
    }

    /// <summary>After a card-side failure: is the card gone (CardRemoved), another card (CardSwapped), or still ours?</summary>
    private static Step CardError(OffloadBatch batch, CopyJob job, ICardReader card, CopyPhase phase, Exception error, CopyOutcome whenPresent)
    {
        CardIdentity now;
        try
        {
            now = card.CurrentIdentity();
        }
        catch (Exception gone) when (Failures.IsIo(gone))
        {
            return new Step(new Failed(job, phase, error.Message), StopReason.CardRemoved);
        }
        if (now != batch.Card) return new Step(new CardSwapped(job, now), StopReason.CardSwapped);
        return new Step(whenPresent, null);
    }
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CopyEngine*Tests"`
Expected: PASS (15 tests: 11 engine + 4 card faults).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/CopyEngine.cs tests/UasSort.Core.Tests/Offload/CopyEngineCardFaultTests.cs
git commit -m "feat: re-read failed card chunks and stop on a removed or swapped card

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 07.9: Destination faults — disk full, destination lost, verify retry

**Files:**
- Modify: `src/UasSort.Core/Offload/CopyEngine.cs` (replace `DestinationError` and `CopyVerified`)
- Test: `tests/UasSort.Core.Tests/Offload/CopyEngineDestinationFaultTests.cs`

**Interfaces:**

```csharp
// Consumes: CopyEngine (07.7); FakeFaults.DiskFullAfterBytes / LostRoots / CorruptVerify / UnbufferedUnsupported (Part 02),
//   FakeFileOps (Part 02, with Task 07.4's Calls / OnTempWrite).
// Produces: no new API. Behaviour (Ref §10.3 rows 5, 6, 9 and step 7): a destination IOException whose Win32 code is ERROR_DISK_FULL or
//   ERROR_HANDLE_DISK_FULL → Failed(<phase>) + stop DestinationFull; any other destination IO failure probes IFileOps.FreeBytes(volume root):
//   it throws → Failed(<phase>) + stop DestinationLost, else Failed(<phase>) and the run continues (e.g. an Append folder deleted after the
//   scan, which EnsureDirectory never creates); a hash mismatch deletes the temp and repeats steps 4–7 once; a second mismatch → Failed(Verify).
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/CopyEngineDestinationFaultTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Core.Tests.Offload.CopyEngineTests;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class CopyEngineDestinationFaultTests
{
    [Fact]
    public async Task DiskFull_FailsTheInFlightFile_AndStops()
    {
        var rig = Videos(2 * CopyEngine.ChunkBytes, 1_000);
        rig.Fs.Faults.DiskFullAfterBytes = CopyEngine.ChunkBytes + 100;

        var r = await rig.RunEngineAsync();

        Assert.Equal(["Failed", "NotStarted"], r.Kinds());
        Assert.Equal(CopyPhase.Copy, ((Failed)r.Outcomes[0]).Phase);
        Assert.Equal(StopReason.DestinationFull, r.Stop);
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(rig.Batch.Jobs[0].DestPath)));
    }

    [Fact]
    public async Task DestinationVolumeLost_FailsTheInFlightFile_AndStops()
    {
        var b = new OffloadPlanBuilder().WithPhotoRoot(@"D:\Photos");
        b.Photo("DJI_20260927140000_0001_D.DNG", 2 * CopyEngine.ChunkBytes, T0);
        b.Photo("DJI_20260927140100_0002_D.DNG", 1_000, T0.AddMinutes(1));
        var rig = new OffloadRig(b).Build();
        rig.Files.OnTempWrite = (_, _) => rig.Fs.Faults.LostRoots.Add(@"D:\");

        var r = await rig.RunEngineAsync();

        Assert.Equal(["Failed", "NotStarted"], r.Kinds());
        Assert.Equal(StopReason.DestinationLost, r.Stop);
        Assert.False(rig.Fs.Exists(rig.Batch.Jobs[0].DestPath));
    }

    [Fact]
    public async Task OneBitFlipOnReadBack_RetriesOnce_AndVerifies()
    {
        var rig = Videos(1_000);
        rig.Fs.Faults.CorruptVerify[rig.Batch.Jobs[0].DestPath] = 1;

        var r = await rig.RunEngineAsync();

        Assert.IsType<Verified>(r.Outcomes[0]);
        Assert.Equal(2, rig.Files.Calls.Count(c => c.StartsWith("CreateTemp ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TwoMismatches_AreFailedVerify_TheTempIsDeleted_AndTheRunContinues()
    {
        var rig = Videos(1_000, 2_000);
        var dest = rig.Batch.Jobs[0].DestPath;
        rig.Fs.Faults.CorruptVerify[dest] = 2;

        var r = await rig.RunEngineAsync();

        Assert.Equal(CopyPhase.Verify, Assert.IsType<Failed>(r.Outcomes[0]).Phase);
        Assert.IsType<Verified>(r.Outcomes[1]);
        Assert.Null(r.Stop);
        Assert.False(rig.Fs.Exists(dest));
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(dest)));
    }

    [Fact]
    public async Task AppendFolderDeletedAfterTheScan_FailsItsJobs_AndIsNeverCreated()
    {
        var b = new OffloadPlanBuilder();
        var gone = new LibraryFolderRef(b.NewFolderPath(ZRel), new DateOnly(2026, 9, 27), "Zachar Bay");
        var a = b.Video("DJI_20260927160000_0160_D.MP4", 1_000, T0.AddHours(2));
        var n = b.Video("DJI_20260928160000_0170_D.MP4", 1_000, T0.AddDays(1));
        b.Group(new Append(gone, Confidence.High, "same day as clips already in this folder", null), Zachar, a)
         .Group(new NewFolder(@"2026\2026-09\2026-09-28 Next"), Zachar, n);
        var rig = new OffloadRig(b).Build();

        var r = await rig.RunEngineAsync();

        Assert.Equal(CopyPhase.CreateTemp, Assert.IsType<Failed>(r.Outcomes[0]).Phase);
        Assert.IsType<Verified>(r.Outcomes[1]);
        Assert.Null(r.Stop);
        Assert.False(rig.Fs.Exists(gone.FullPath));
        Assert.Contains($"EnsureDirectory {gone.FullPath} False", rig.Files.Calls);
    }

    [Fact]
    public async Task UnbufferedVerifyUnsupported_FallsBackToCached_AndSaysSo()
    {
        var rig = Videos(1_000);
        rig.Fs.Faults.UnbufferedUnsupported.Add(rig.Batch.Jobs[0].DestPath);

        var r = await rig.RunEngineAsync();

        Assert.Equal(VerifyMode.Cached, Assert.IsType<Verified>(r.Outcomes[0]).Mode);
        Assert.Equal("cached", Assert.IsType<FileRecord>(rig.Writer.Records[0]).Verify);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CopyEngineDestinationFaultTests"`
Expected: FAIL — `DiskFull…` and `DestinationVolumeLost…` (`Stop` is null; the second job ran), `OneBitFlipOnReadBack…` (`Failed(Verify)` after one attempt); the other three pass.

- [ ] **Step 3: Implement** — in `src/UasSort.Core/Offload/CopyEngine.cs`, replace the whole `DestinationError` method and the whole `CopyVerified` method (with their "Completed in Task 07.9" comments) by:

```csharp
    /// <summary>A destination failure: disk full stops (DestinationFull); a vanished volume stops (DestinationLost);
    /// anything else fails only this file.</summary>
    private static Step DestinationError(CopyJob job, IFileOps files, CopyPhase phase, Exception error)
    {
        var failed = new Failed(job, phase, error.Message);
        if (Failures.IsDiskFull(error)) return new Step(failed, StopReason.DestinationFull);
        try
        {
            files.FreeBytes(OffloadPaths.VolumeRoot(job.DestPath));
        }
        catch (Exception probe) when (Failures.IsIo(probe))
        {
            return new Step(failed, StopReason.DestinationLost);
        }
        return new Step(failed, null);
    }

    /// <summary>Steps 4–7; on a mismatch the temp is deleted and the file copied once more from step 4.</summary>
    private CopyResult CopyVerified(OffloadBatch batch, CopyJob job, ICardReader card, IFileOps files, ProgressMeter meter,
                                    CancellationToken ct, ref CopyPhase phase, ref string? temp)
    {
        var first = CopyOnce(batch, job, card, files, meter, ct, ref phase, ref temp);
        if (!first.Mismatch) return first;
        files.DeleteOwnTemp(temp!);
        temp = null;
        var second = CopyOnce(batch, job, card, files, meter, ct, ref phase, ref temp);
        if (!second.Mismatch) return second;
        return new CopyResult(default, default, false,
                              new Step(new Failed(job, CopyPhase.Verify, "The copy didn't match the card twice"), null));
    }
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CopyEngine*Tests"`
Expected: PASS (21 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/CopyEngine.cs tests/UasSort.Core.Tests/Offload/CopyEngineDestinationFaultTests.cs
git commit -m "feat: stop on a full or lost destination and retry a failed verify once

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 07.10: After the loop — destination flush and `VolumesNeedingSafeRemoval`

**Files:**
- Modify: `src/UasSort.Core/Offload/CopyEngine.cs` (replace `FlushDestinations`; add `NeedsSafeRemoval`)
- Test: `tests/UasSort.Core.Tests/Offload/CopyEngineFlushTests.cs`

**Interfaces:**

```csharp
// Consumes: CopyEngineOptions.Volumes (07.7), VolumeInfo (Ref §4.1: Root, DriveType, IsNtfs, IsRemovableBus), IFileOps.FlushDestination,
//   FakeFileOps.FlushedDestinations (07.4), OffloadVolumes.NtfsC / ExFatD (07.4).
// Produces: no new API. Behaviour (Ref §10.3 "After the loop", also after Cancel or a stop): every destination volume holding a file renamed
//   in this run that is not NTFS on a fixed, non-removable disk (or is not in CopyEngineOptions.Volumes at all) gets
//   IFileOps.FlushDestination(dir, renamed files in dir) per directory and is listed in OffloadResult.VolumesNeedingSafeRemoval
//   (e.g. "D:\"). A flush IO failure is ignored (the volume is still listed); NTFS fixed volumes are never flushed.
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/CopyEngineFlushTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class CopyEngineFlushTests
{
    private static OffloadRig VideoOnCPhotosOnD(bool knowD = true)
    {
        var b = new OffloadPlanBuilder().WithPhotoRoot(@"D:\Photos");
        var v = b.Video("DJI_20260927140000_0001_D.MP4", 1_000, T0);
        b.Group(new NewFolder(@"2026\2026-09\2026-09-27 Zachar Bay"), Zachar, v);
        b.Photo("DJI_20260927140100_0002_D.DNG", 2_000, T0.AddMinutes(1));
        b.Photo("DJI_20260927140200_0003_D.DNG", 3_000, T0.AddMinutes(2));
        var rig = new OffloadRig(b).Build();
        if (knowD) rig.Volumes.Add(OffloadVolumes.ExFatD);
        return rig;
    }

    [Fact]
    public async Task ExFatDestination_IsFlushed_AndNeedsSafeRemoval_NtfsIsNot()
    {
        var rig = VideoOnCPhotosOnD();

        var r = await rig.RunEngineAsync();

        Assert.Equal([@"D:\"], r.VolumesNeedingSafeRemoval.ToArray());
        Assert.Equal([(@"D:\Photos", 2)], rig.Files.FlushedDestinations.ToArray());
        rig.Fs.AssertNoViolations();
    }

    [Fact]
    public async Task AfterCancel_TheFilesAlreadyRenamed_AreStillFlushed()
    {
        var rig = VideoOnCPhotosOnD();
        using var cts = new CancellationTokenSource();
        var second = rig.Batch.Jobs[1].DestPath;   // a one-chunk photo: it completes, and the loop sees the cancel before the third job
        rig.Files.OnTempWrite = (final, _) => { if (string.Equals(final, second, StringComparison.OrdinalIgnoreCase)) cts.Cancel(); };

        var r = await rig.RunEngineAsync(ct: cts.Token);

        Assert.Equal(["Verified", "Verified", "NotStarted"], r.Kinds());
        Assert.Equal(StopReason.Cancelled, r.Stop);
        Assert.Equal([(@"D:\Photos", 1)], rig.Files.FlushedDestinations.ToArray());
        Assert.Equal([@"D:\"], r.VolumesNeedingSafeRemoval.ToArray());
    }

    [Fact]
    public async Task AnUnknownVolume_IsTreatedAsNeedingSafeRemoval()
    {
        var rig = VideoOnCPhotosOnD(knowD: false);

        var r = await rig.RunEngineAsync();

        Assert.Equal([@"D:\"], r.VolumesNeedingSafeRemoval.ToArray());
    }

    [Fact]
    public async Task AllOnNtfsFixed_NothingIsFlushed()
    {
        var rig = CopyEngineTests.Videos(1_000, 2_000);

        var r = await rig.RunEngineAsync();

        Assert.Empty(r.VolumesNeedingSafeRemoval);
        Assert.Empty(rig.Files.FlushedDestinations);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CopyEngineFlushTests"`
Expected: FAIL — the three D: tests (`VolumesNeedingSafeRemoval` is empty); `AllOnNtfsFixed…` passes.

- [ ] **Step 3: Implement** — in `src/UasSort.Core/Offload/CopyEngine.cs`, replace the whole `FlushDestinations` method (with its "Completed in Task 07.10" comment) by:

```csharp
    /// <summary>After the loop: flush renamed files and their directories on volumes that are not NTFS on a fixed disk, and list
    /// those volumes for safe removal (Ref §10.3). Durability there comes from the flush plus safe removal, not write-through.</summary>
    private ImmutableArray<string> FlushDestinations(IFileOps files, List<string> renamed)
    {
        var needing = ImmutableArray.CreateBuilder<string>();
        foreach (var volume in renamed.GroupBy(OffloadPaths.VolumeRoot, StringComparer.OrdinalIgnoreCase))
        {
            if (!NeedsSafeRemoval(volume.Key)) continue;
            foreach (var dir in volume.GroupBy(OffloadPaths.DirectoryOf, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    files.FlushDestination(dir.Key, [.. dir]);
                }
                catch (Exception e) when (Failures.IsIo(e))
                {
                    // the volume is still listed for safe removal, which is what makes the copies durable there
                }
            }
            needing.Add(volume.Key);
        }
        return needing.ToImmutable();
    }

    private bool NeedsSafeRemoval(string volumeRoot)
    {
        var v = options.Volumes.FirstOrDefault(x => OffloadPaths.Same(x.Root, volumeRoot));
        return v is null || !v.IsNtfs || v.IsRemovableBus || !string.Equals(v.DriveType, "Fixed", StringComparison.OrdinalIgnoreCase);
    }
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CopyEngine*Tests"`
Expected: PASS (25 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/CopyEngine.cs tests/UasSort.Core.Tests/Offload/CopyEngineFlushTests.cs
git commit -m "feat: flush non-NTFS destinations after the copy loop and list them for safe removal

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.11: The Commit's closing ledger records — `seen`, `cardLeftovers` folders, `run`

**Files:**
- Create: `src/UasSort.Core/Offload/CommitTailRecords.cs`
- Test: `tests/UasSort.Core.Tests/Offload/CommitTailRecordsTests.cs`

**Interfaces:**

```csharp
// Consumes: OffloadBatch.SeenIfNotCopied/Folders/Jobs, OffloadResult, Plan, Item, PhotoUnit, SetUnit, SeenRecord, FolderRecord, RunRecord,
//   RunCard, RunRoots, FormatVerdict (Ref §3); OffloadRecords (07.7).
// Produces (defined here):
public static class CommitTailRecords
{
    // FolderPlans whose group has no job: the location learned from leftover clips on the card (Ref §10.4), source "cardLeftovers".
    public static ImmutableArray<FolderRecord> CardLeftovers(OffloadBatch batch, string machine);
    // One per uncopied SeenIfNotCopied unit (a photo: its primary file; a set: one per member not copied, each with set = SetName).
    // Status "Conflict" for a Conflict unit, else "New". Why: "unticked" when the unit isn't in Plan.Included, else the outcome of that
    // file in camelCase ("notStarted", "cancelled", "failed", "changedOnCard", "cardSwapped", "conflictAtRename"), "notCopied" without one.
    public static ImmutableArray<SeenRecord> Seen(OffloadBatch batch, Plan plan, OffloadResult result, string machine, DateTime atUtc);
    public static RunRecord Run(OffloadBatch batch, Plan plan, OffloadResult result, FormatVerdict verdict, string machine, string appVersion);
    public static RunCard Card(CardIdentity id, string? model, string inventoryHash);   // serial "X8" (e.g. "1A2B3C4D")
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/CommitTailRecordsTests.cs
using System.Collections.Immutable;
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class CommitTailRecordsTests
{
    internal static OffloadResult Result(OffloadBatch batch, Func<CopyJob, CopyOutcome> outcome, StopReason? stop = null)
        => new(batch.RunId, [.. batch.Jobs.Select(outcome)], stop, T0, T0.AddMinutes(5), []);

    [Fact]
    public void Seen_OnePerUncopiedPhoto_AndPerUncopiedSetMember()
    {
        var b = new OffloadPlanBuilder();
        var unticked = b.Photo("DJI_20260927140000_0001_D.DNG", 10, T0, included: false);
        var notStarted = b.Photo("DJI_20260927140100_0002_D.DNG", 20, T0.AddMinutes(1));
        var copied = b.Photo("DJI_20260927140200_0003_D.DNG", 30, T0.AddMinutes(2), twinSize: 3);
        var conflict = b.Photo("DJI_20260927140300_0004_D.DNG", 40, T0.AddMinutes(3), new Conflict(@"C:\x\DJI_20260927140300_0004_D.DNG", 99), included: false);
        b.Set("001_0087", [("PANO_0001.DNG", 5), ("PANO_0002.DNG", 6)], T0.AddMinutes(4), SetResolution.Plain);
        var plan = b.Build();
        var batch = OffloadCompiler.Compile(plan, "run-1");
        var result = Result(batch, j => j.Item == copied ? new Verified(j, UInt128.One, VerifyMode.Unbuffered) : new NotStarted(j), StopReason.Cancelled);

        var seen = CommitTailRecords.Seen(batch, plan, result, "DESKTOP-A", T0.AddMinutes(6));

        Assert.Equal(
            [("DJI_20260927140000_0001_D.DNG", "New", "unticked", (string?)null),
             ("DJI_20260927140100_0002_D.DNG", "New", "notStarted", null),
             ("DJI_20260927140300_0004_D.DNG", "Conflict", "unticked", null),
             ("PANO_0001.DNG", "New", "notStarted", "001_0087"),
             ("PANO_0002.DNG", "New", "notStarted", "001_0087")],
            seen.Select(x => (x.Name, x.Status, x.Why, x.Set)).ToArray());
        var first = seen[0];
        Assert.Equal((1, "DESKTOP-A", "run-1", T0.AddMinutes(6), 10L, "DCIM/DJI_001/DJI_20260927140000_0001_D.DNG", (DateTime?)T0),
                     (first.V, first.Machine, first.Run, first.At, first.Size, first.Src, first.CaptureUtc));
        Assert.DoesNotContain(seen, x => x.Name.Contains("0003", StringComparison.Ordinal));
        Assert.Equal(unticked, new ItemId(first.Src));
        Assert.Equal(notStarted, new ItemId(seen[1].Src));
        Assert.Equal(conflict, new ItemId(seen[2].Src));
    }

    [Fact]
    public void CardLeftovers_AreTheFolderPlansWithoutJobs()
    {
        var b = new OffloadPlanBuilder();
        var zFolder = new LibraryFolderRef(b.NewFolderPath(@"2026\2026-09\2026-09-27 Zachar Bay"), new DateOnly(2026, 9, 27), "Zachar Bay");
        var old = b.Video("DJI_20260927140000_0123_D.MP4", 10, T0, new Imported(Evidence.LibraryNameSize, zFolder, "listed"));
        var fresh = b.Video("DJI_20260928140000_0170_D.MP4", 10, T0.AddDays(1));
        b.Group(new AlreadyImported(zFolder), Zachar, old).Group(new NewFolder(@"2026\2026-09\2026-09-28 Next"), Zachar, fresh);
        var batch = OffloadCompiler.Compile(b.Build(), "run-1");

        var record = Assert.Single(CommitTailRecords.CardLeftovers(batch, "DESKTOP-A"));

        Assert.Equal((zFolder.FullPath, "Zachar Bay", "cardLeftovers", "run-1"), (record.Path, record.Desc, record.Source, record.Run));
        Assert.Equal((Zachar.Lat, Zachar.Lon), (record.Lat!.Value, record.Lon!.Value));
    }

    [Fact]
    public void Run_CarriesCardRootsVerdictAndNonZeroCounts()
    {
        var b = new OffloadPlanBuilder();
        b.Photo("DJI_20260927140000_0001_D.DNG", 10, T0);
        var plan = b.Build();
        var batch = OffloadCompiler.Compile(plan, "run-1");
        var result = Result(batch, j => new Verified(j, UInt128.One, VerifyMode.Unbuffered));
        var counts = Enum.GetValues<AuditCategory>().ToImmutableDictionary(c => c, c => c == AuditCategory.VerifiedThisRun ? 17 : c == AuditCategory.AssumedByRule ? 40 : 0);
        var verdict = new FormatVerdict(VerdictLevel.SafeWithAssumptions, OffloadPlanBuilder.Card, "h", counts, 0, 0, [], [], null);

        var run = CommitTailRecords.Run(batch, plan, result, verdict, "DESKTOP-A", "0.1.0");

        Assert.Equal(("run-1", T0, T0.AddMinutes(5), "0.1.0", "SafeWithAssumptions"), (run.Run, run.Start, run.End, run.App, run.Verdict));
        Assert.Equal(new RunCard("1A2B3C4D", null, "exFAT", "FC9113", "0123456789abcdef"), run.Card);
        Assert.Equal(new RunRoots(b.VideoRoot, b.PhotoRoot), run.Roots);
        Assert.Equal(2, run.Counts.Count);
        Assert.Equal((17, 40), (run.Counts["VerifiedThisRun"], run.Counts["AssumedByRule"]));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CommitTailRecordsTests"`
Expected: build FAILS with CS0103 (`CommitTailRecords` does not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Offload/CommitTailRecords.cs
using System.Collections.Immutable;
using System.Globalization;

namespace UasSort.Core.Offload;

/// <summary>Ledger records written at the end of every Commit, including after Cancel, failures and stops (Ref §10.4).</summary>
public static class CommitTailRecords
{
    public static ImmutableArray<FolderRecord> CardLeftovers(OffloadBatch batch, string machine)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var withJobs = batch.Jobs.Where(j => j.Group is not null).Select(j => j.Group!.Value).ToHashSet();
        return [.. batch.Folders.Where(f => !withJobs.Contains(f.Group))
                                .Select(f => OffloadRecords.Folder(f, "cardLeftovers", batch.RunId, machine))];
    }

    public static ImmutableArray<SeenRecord> Seen(OffloadBatch batch, Plan plan, OffloadResult result, string machine, DateTime atUtc)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        var items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var outcomes = new Dictionary<string, CopyOutcome>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in result.Outcomes) outcomes[o.Job.CardRelPath] = o;

        var records = ImmutableArray.CreateBuilder<SeenRecord>();
        foreach (var id in batch.SeenIfNotCopied)
        {
            if (!items.TryGetValue(id, out var item)) continue;
            IReadOnlyList<CardEntry> files;
            string? set = null;
            switch (item.Raw.Unit)
            {
                case PhotoUnit p:
                    files = [p.Primary];
                    break;
                case SetUnit s:
                    files = s.Members;
                    set = s.SetName;
                    break;
                default:
                    continue;
            }
            bool included = plan.Included.Contains(id);
            var status = item.Newness is Conflict ? "Conflict" : "New";
            foreach (var f in files)
            {
                outcomes.TryGetValue(f.RelPath, out var o);
                if (o is Verified or AlreadyThere) continue;
                var why = !included ? "unticked" : o is null ? "notCopied" : Why(o);
                records.Add(new SeenRecord(OffloadRecords.Version, OffloadRecords.NewId(), machine, batch.RunId, atUtc,
                    OffloadPaths.FileName(f.RelPath), f.Size, f.RelPath, item.Time.CaptureUtc, status, why, set));
            }
        }
        return records.ToImmutable();
    }

    private static string Why(CopyOutcome o) => o switch
    {
        Verified => "verified",
        AlreadyThere => "alreadyThere",
        ConflictAtRename => "conflictAtRename",
        ChangedOnCard => "changedOnCard",
        CardSwapped => "cardSwapped",
        Failed => "failed",
        Cancelled => "cancelled",
        NotStarted => "notStarted",
    };

    public static RunRecord Run(OffloadBatch batch, Plan plan, OffloadResult result, FormatVerdict verdict, string machine, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(verdict);
        var inventory = plan.Base.Scan.Inventory;
        var settings = plan.Base.Scan.Settings;
        return new RunRecord(OffloadRecords.Version, OffloadRecords.NewId(), machine, batch.RunId, result.StartUtc, result.EndUtc, appVersion,
            Card(batch.Card, inventory.CameraModel, inventory.InventoryHash), new RunRoots(settings.VideoRoot, settings.PhotoRoot),
            verdict.Level.ToString(),
            verdict.Counts.Where(kv => kv.Value > 0).ToImmutableDictionary(kv => kv.Key.ToString(), kv => kv.Value));
    }

    public static RunCard Card(CardIdentity id, string? model, string inventoryHash)
    {
        ArgumentNullException.ThrowIfNull(id);
        return new RunCard(id.VolumeSerial.ToString("X8", CultureInfo.InvariantCulture), id.Label, id.FileSystem, model, inventoryHash);
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CommitTailRecordsTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/CommitTailRecords.cs tests/UasSort.Core.Tests/Offload/CommitTailRecordsTests.cs
git commit -m "feat: build the seen, card-leftovers folder and run ledger records

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.12: Card re-list diff

**Files:**
- Create: `src/UasSort.Core/Offload/CardDiff.cs`
- Test: `tests/UasSort.Core.Tests/Offload/CardDiffTests.cs`

**Interfaces:**

```csharp
// Consumes: CardEntry, ListingResult, FsEntry (Ref §3–§4.1); OffloadPaths.NormRel (07.1).
// Produces (defined here):
public sealed record CardDiffEntry(string RelPath /* '/' separators */, long Size /* the size now; the scanned size for Removed */);
public sealed record CardDiffResult(ImmutableArray<CardDiffEntry> Added, ImmutableArray<CardDiffEntry> Removed,
                                    ImmutableArray<CardDiffEntry> Changed, int LastAccessOnly)
{
    public const string ChangedDetail = "changed since scan";                  // every detail below starts with it (Part 08 relies on the prefix)
    public const string AddedDetail = "changed since scan (added)";
    public const string RemovedDetail = "changed since scan (removed)";
    public bool IsEmpty { get; }
    public string? Touched(string relPath);                                    // RemovedDetail / ChangedDetail / null
}
public static class CardDiff
{
    // Ref §10.5 "Before auditing" 2: files only, compared on (RelPath, Size, MtimeUtc, CreationUtc, Attributes), case-insensitive paths,
    // "System Volume Information" and everything under it excluded; a difference in LastAccessUtc alone is counted, not a change.
    public static CardDiffResult Compare(IEnumerable<CardEntry> scanned, ListingResult relisted);
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/CardDiffTests.cs
using UasSort.Core;
using UasSort.Core.Offload;

namespace UasSort.Core.Tests.Offload;

public class CardDiffTests
{
    private static readonly DateTime T = new(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc);

    private static CardEntry E(string rel, long size = 10, uint attrs = 0x20)
        => new(rel, size, T, T, T, attrs, EntryClass.Video, null);

    private static FsEntry F(string rel, long size = 10, uint attrs = 0x20, DateTime? mtime = null, DateTime? creation = null,
                             DateTime? access = null, bool dir = false)
        => new(@"E:\" + rel.Replace('/', '\\'), rel.Replace('/', '\\'), dir, size, mtime ?? T, creation ?? T, access ?? T, attrs);

    private static ListingResult L(params FsEntry[] entries) => new([.. entries], []);

    [Fact]
    public void IdenticalListing_IsEmpty()
    {
        var d = CardDiff.Compare([E("DCIM/DJI_001/a.MP4")], L(F("DCIM/DJI_001/a.MP4"), F("DCIM", dir: true)));

        Assert.True(d.IsEmpty);
        Assert.Equal(0, d.LastAccessOnly);
    }

    [Theory]
    [InlineData("size")]
    [InlineData("mtime")]
    [InlineData("creation")]
    [InlineData("attributes")]
    public void AnyComparedFieldChanged_IsAChange(string field)
    {
        var now = field switch
        {
            "size" => F("DCIM/DJI_001/a.MP4", size: 11),
            "mtime" => F("DCIM/DJI_001/a.MP4", mtime: T.AddSeconds(2)),
            "creation" => F("DCIM/DJI_001/a.MP4", creation: T.AddSeconds(2)),
            _ => F("DCIM/DJI_001/a.MP4", attrs: 0x21),
        };

        var d = CardDiff.Compare([E("DCIM/DJI_001/a.MP4")], L(now));

        Assert.Equal("DCIM/DJI_001/a.MP4", Assert.Single(d.Changed).RelPath);
        Assert.Equal(CardDiffResult.ChangedDetail, d.Touched("dcim/dji_001/A.mp4"));
    }

    [Fact]
    public void AddedAndRemovedFiles_AreChanges()
    {
        var d = CardDiff.Compare([E("DCIM/DJI_001/a.MP4"), E("DCIM/DJI_001/gone.MP4")],
                                 L(F("DCIM/DJI_001/a.MP4"), F("DCIM/DJI_001/new.MP4", size: 5)));

        Assert.Equal(new CardDiffEntry("DCIM/DJI_001/new.MP4", 5), Assert.Single(d.Added));
        Assert.Equal(new CardDiffEntry("DCIM/DJI_001/gone.MP4", 10), Assert.Single(d.Removed));
        Assert.Equal(CardDiffResult.RemovedDetail, d.Touched("DCIM/DJI_001/gone.MP4"));
        Assert.Null(d.Touched("DCIM/DJI_001/a.MP4"));
        Assert.StartsWith(CardDiffResult.ChangedDetail, CardDiffResult.AddedDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void LastAccessOnly_IsCountedButNotAChange()
    {
        var d = CardDiff.Compare([E("DCIM/DJI_001/a.MP4")], L(F("DCIM/DJI_001/a.MP4", access: T.AddDays(1))));

        Assert.True(d.IsEmpty);
        Assert.Equal(1, d.LastAccessOnly);
    }

    [Fact]
    public void SystemVolumeInformation_IsIgnored()
    {
        var d = CardDiff.Compare([E("System Volume Information/WPSettings.dat")],
                                 L(F("System Volume Information/IndexerVolumeGuid", size: 76)));

        Assert.True(d.IsEmpty);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CardDiffTests"`
Expected: build FAILS with CS0103 (`CardDiff` does not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Offload/CardDiff.cs
using System.Collections.Immutable;

namespace UasSort.Core.Offload;

public sealed record CardDiffEntry(string RelPath, long Size);

public sealed record CardDiffResult(ImmutableArray<CardDiffEntry> Added, ImmutableArray<CardDiffEntry> Removed,
                                    ImmutableArray<CardDiffEntry> Changed, int LastAccessOnly)
{
    public const string ChangedDetail = "changed since scan";
    public const string AddedDetail = "changed since scan (added)";
    public const string RemovedDetail = "changed since scan (removed)";

    private readonly HashSet<string> _removed = new(Removed.Select(e => OffloadPaths.NormRel(e.RelPath)), StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _changed = new(Changed.Select(e => OffloadPaths.NormRel(e.RelPath)), StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => Added.IsEmpty && Removed.IsEmpty && Changed.IsEmpty;

    public string? Touched(string relPath)
    {
        var key = OffloadPaths.NormRel(relPath);
        return _removed.Contains(key) ? RemovedDetail : _changed.Contains(key) ? ChangedDetail : null;
    }
}

/// <summary>Ref §10.5: re-list the card and diff it against the scanned inventory (listing only).</summary>
public static class CardDiff
{
    private const string Svi = "System Volume Information";

    private static bool Excluded(string relPath)
    {
        var r = OffloadPaths.NormRel(relPath);
        return r.Equals(Svi, StringComparison.OrdinalIgnoreCase) || r.StartsWith(Svi + "/", StringComparison.OrdinalIgnoreCase);
    }

    public static CardDiffResult Compare(IEnumerable<CardEntry> scanned, ListingResult relisted)
    {
        ArgumentNullException.ThrowIfNull(scanned);
        ArgumentNullException.ThrowIfNull(relisted);
        var now = new Dictionary<string, FsEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in relisted.Entries)
            if (!e.IsDirectory && !Excluded(e.RelPath)) now.TryAdd(OffloadPaths.NormRel(e.RelPath), e);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = ImmutableArray.CreateBuilder<CardDiffEntry>();
        var changed = ImmutableArray.CreateBuilder<CardDiffEntry>();
        int lastAccessOnly = 0;
        foreach (var e in scanned)
        {
            if (Excluded(e.RelPath)) continue;
            var key = OffloadPaths.NormRel(e.RelPath);
            seen.Add(key);
            if (!now.TryGetValue(key, out var n))
            {
                removed.Add(new CardDiffEntry(key, e.Size));
                continue;
            }
            if (n.Size != e.Size || n.MtimeUtc != e.MtimeUtc || n.CreationUtc != e.CreationUtc || n.RawAttributes != e.RawAttributes)
                changed.Add(new CardDiffEntry(key, n.Size));
            else if (n.LastAccessUtc != e.LastAccessUtc)
                lastAccessOnly++;
        }
        var added = now.Where(kv => !seen.Contains(kv.Key))
                       .Select(kv => new CardDiffEntry(kv.Key, kv.Value.Size))
                       .OrderBy(e => e.RelPath, StringComparer.Ordinal);
        return new CardDiffResult([.. added], removed.ToImmutable(), changed.ToImmutable(), lastAccessOnly);
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CardDiffTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/CardDiff.cs tests/UasSort.Core.Tests/Offload/CardDiffTests.cs
git commit -m "feat: diff the re-listed card against the scanned inventory

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.13: Per-file audit categories and worst-of per unit

**Files:**
- Create: `src/UasSort.Core/Offload/AuditCategorizer.cs`
- Test: `tests/UasSort.Core.Tests/Offload/AuditCategorizerTests.cs`

**Interfaces:**

```csharp
// Consumes: CardInventory, MediaUnit cases, CardEntry/EntryClass, Plan, Item, Newness cases, SetPlacement, Settings.CopyJpgTwin,
//   LedgerSnapshot.Files/Decisions, LedgerFile.Verify/Set, LedgerDecision, LibraryIndex.Match, OffloadResult + CopyOutcome cases,
//   AuditCategory, AuditLine, UnitAudit (Ref §3); CardDiffResult (07.12); OffloadPaths (07.1).
// Produces (defined here):
public enum UnaccountedKind { ChangedSinceScan, Failed, UnfinishedNotCopied, NotCopied, Unrecognised }   // a unit's most urgent reason
public sealed record AuditUnits(ImmutableArray<UnitAudit> Units, ImmutableDictionary<ItemId, UnaccountedKind> Unaccounted,
                                int TruncatedAssumed /* truncated clips copied or matched */, int CachedVerifies);
public static class AuditCategorizer
{
    public static AuditUnits Categorize(CardInventory inventory, Plan plan, OffloadResult? offload, LedgerSnapshot ledger, CardDiffResult diff);
}
```

Rules (Ref §10.5 table; every card file gets exactly one line, a unit takes the worst):
1. A file removed or changed since the scan → Unaccounted, detail `CardDiffResult.RemovedDetail` / `ChangedDetail`. A file added since the scan becomes its own unit (id = its path), Unaccounted, `AddedDetail`.
2. A file with an outcome in this run: `Verified` → VerifiedThisRun ("… (buffered read-back)" for Cached); `AlreadyThere` → NameSizeMatch; `Failed`, `ChangedOnCard`, `CardSwapped`, `Cancelled`, `NotStarted`, `ConflictAtRename` → Unaccounted.
3. Otherwise the evidence, best first: a ledger `file` record with `verify` unbuffered/cached → InLedger; an unrevoked `decision` → ConfirmedByYou; a `nameSize` ledger record → NameSizeMatch (never InLedger); a library listing of `(NormName, size)` → NameSizeMatch (for set members instead: the set placement is `Imported`, or `Resume` and the member is not in `MembersToCopy`; ledger records and decisions must carry the same `set`).
4. A JPG twin without its own evidence: copying off → SkippedByRule "JPG twin: copying disabled in Settings"; its DNG Imported → AssumedByRule "JPG twin assumed imported with its DNG"; its DNG Decided → ConfirmedByYou.
5. By newness: `ProbablyImported` → AssumedByRule; `Decided` → ConfirmedByYou; `Imported` → InLedger (LedgerVerified) or NameSizeMatch; `Conflict`/`IsNew` not copied (including a probe error) → Unaccounted.
6. Loose files (no unit): `Skip` → SkippedByRule with the rule name; `Unknown` → ConfirmedByYou if the ledger has a decision for it, else Unaccounted "not recognised by uas-sort".
7. `TruncatedAssumed` counts truncated video units whose worst is VerifiedThisRun, InLedger or NameSizeMatch; `CachedVerifies` counts `Verified` outcomes with `VerifyMode.Cached`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/AuditCategorizerTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Core.Tests.Offload.CommitTailRecordsTests;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class AuditCategorizerTests
{
    private const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private const string V = "DJI_20260927140000_0001_D.MP4";
    private const string P = "DJI_20260927140000_0002_D.DNG";
    private static readonly CardDiffResult NoChange = new([], [], [], 0);

    internal sealed record Fixture(Plan Plan, OffloadResult? Result, CardDiffResult Diff, ItemId Unit)
    {
        public AuditUnits Run() => AuditCategorizer.Categorize(Plan.Base.Scan.Inventory, Plan, Result, Plan.Base.Scan.Ledger, Diff);
        public UnitAudit Unit_ => Run().Units.Single(u => u.Unit == Unit);
    }

    private static LibraryFolderRef ZFolder(OffloadPlanBuilder b) => new(b.NewFolderPath(ZRel), new DateOnly(2026, 9, 27), "Zachar Bay");

    private static ItemId NewVideo(OffloadPlanBuilder b, bool included = true, ItemFlags flags = ItemFlags.None, string? probeError = null,
                                   Newness? newness = null, bool hasTrinf = false)
    {
        var v = b.Video(V, 100, T0, newness, flags, Zachar, included, hasTrinf, probeError);
        b.Group(new NewFolder(ZRel), Zachar, v);
        return v;
    }

    private static Fixture Build(OffloadPlanBuilder b, ItemId unit, Func<CopyJob, CopyOutcome>? outcome = null, CardDiffResult? diff = null)
    {
        var plan = b.Build();
        var result = outcome is null ? null : Result(OffloadCompiler.Compile(plan, "run-1"), outcome);
        return new Fixture(plan, result, diff ?? NoChange, unit);
    }

    private static CopyOutcome Ok(CopyJob j) => new Verified(j, UInt128.One, VerifyMode.Unbuffered);

    internal static Fixture Case(string name)
    {
        var b = new OffloadPlanBuilder();
        switch (name)
        {
            case "verified": return Build(b, NewVideo(b), Ok);
            case "verifiedCached": return Build(b, NewVideo(b), j => new Verified(j, UInt128.One, VerifyMode.Cached));
            case "alreadyThere": return Build(b, NewVideo(b), j => new AlreadyThere(j));
            case "failed": return Build(b, NewVideo(b), j => new Failed(j, CopyPhase.Copy, "Data error"));
            case "changedOnCard": return Build(b, NewVideo(b), j => new ChangedOnCard(j, 101, T0));
            case "cardSwapped": return Build(b, NewVideo(b), j => new CardSwapped(j, OffloadPlanBuilder.Card with { VolumeSerial = 1 }));
            case "cancelled": return Build(b, NewVideo(b), j => new Cancelled(j));
            case "notStarted": return Build(b, NewVideo(b), j => new NotStarted(j));
            case "conflictAtRename": return Build(b, NewVideo(b), j => new ConflictAtRename(j));
            case "newUnticked": return Build(b, NewVideo(b, included: false));
            case "unfinishedUnticked": return Build(b, NewVideo(b, included: false, flags: ItemFlags.Truncated, hasTrinf: true));
            case "probeErrorUnticked": return Build(b, NewVideo(b, included: false, flags: ItemFlags.ProbeFailed, probeError: "bad box"));
            case "conflictUnticked":
                return Build(b, NewVideo(b, included: false, newness: new Conflict(ZFolder(b).FullPath + "\\" + V, 555)));
            case "ledgerVerified":
            {
                b.LedgerFile(V, 100, VerifyKind.Unbuffered);
                var v = b.Video(V, 100, T0, new Imported(Evidence.LedgerVerified, null, "ledger"));
                b.Group(new AlreadyImported(ZFolder(b)), null, v);
                return Build(b, v);
            }
            case "ledgerNameSize":
            {
                b.LedgerFile(V, 100, VerifyKind.NameSize);
                var v = b.Video(V, 100, T0, new Imported(Evidence.LedgerNameSize, null, "ledger"));
                b.Group(new AlreadyImported(ZFolder(b)), null, v);
                return Build(b, v);
            }
            case "libraryListed":
            {
                b.LibraryVideo(ZRel + "\\" + V, 100);
                var v = b.Video(V, 100, T0, new Imported(Evidence.LibraryNameSize, ZFolder(b), "listed"));
                b.Group(new AlreadyImported(ZFolder(b)), null, v);
                return Build(b, v);
            }
            case "dismissedVideo":
            {
                b.LedgerDecision(V, 100, DecisionKind.Dismissed);
                var v = b.Video(V, 100, T0, new Decided(DecisionKind.Dismissed, T0, "DESKTOP-A"), included: false);
                b.Group(new NothingToCopy("nothing to copy: 1 dismissed"), null, v);
                return Build(b, v);
            }
            case "probablyImportedPhoto":
                return Build(b, b.Photo(P, 50, T0, new ProbablyImported("videos from this day are already in the library"), included: false));
            case "twinCopyOff":
            {
                b.CopyJpgTwin(false);
                return Build(b, b.Photo(P, 50, T0, twinSize: 5), Ok);
            }
            case "twinAssumed":
            {
                b.LedgerFile(P, 50, VerifyKind.Unbuffered);
                return Build(b, b.Photo(P, 50, T0, new Imported(Evidence.LedgerVerified, null, "ledger"), twinSize: 5));
            }
            case "dngVerifiedTwinFailed":
                return Build(b, b.Photo(P, 50, T0, twinSize: 5),
                             j => j.CardRelPath.EndsWith(".JPG", StringComparison.Ordinal) ? new Failed(j, CopyPhase.Verify, "mismatch twice") : Ok(j));
            case "setImportedByFolder":
                return Build(b, b.Set("001_0087", [("PANO_0001.DNG", 5), ("PANO_0002.DNG", 6)], T0, SetResolution.Imported,
                                      newness: new Imported(Evidence.LibraryNameSize, null, "set folder")));
            case "resumeMixed":
                return Build(b, b.Set("001_0087", [("PANO_0001.DNG", 5), ("PANO_0002.DNG", 6)], T0, SetResolution.Resume,
                                      membersToCopy: ["PANO_0002.DNG"]), Ok);
            case "setOneFailed":
                return Build(b, b.Set("001_0087", [("PANO_0001.DNG", 5), ("PANO_0002.DNG", 6), ("PANO_0003.DNG", 7)], T0, SetResolution.Plain),
                             j => j.CardRelPath.EndsWith("PANO_0002.DNG", StringComparison.Ordinal) ? new Failed(j, CopyPhase.Copy, "Data error") : Ok(j));
            case "skip":
                return Build(b, new ItemId(b.Skip("DCIM/DJI_001/DJI_20260927140000_0001_D.LRF", 5, "proxy").RelPath));
            case "unknown":
                return Build(b, new ItemId(b.Unknown("DCIM/DJI_A001/x.MP4", 5).RelPath));
            case "unknownDismissed":
            {
                b.LedgerDecision("x.MP4", 5, DecisionKind.Dismissed);
                return Build(b, new ItemId(b.Unknown("DCIM/DJI_A001/x.MP4", 5).RelPath));
            }
            case "changedSinceScan":
            {
                var v = NewVideo(b);
                return Build(b, v, Ok, new CardDiffResult([], [], [new CardDiffEntry(v.CardRelPath, 101)], 0));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
    }

    [Theory]
    [InlineData("verified", AuditCategory.VerifiedThisRun)]
    [InlineData("verifiedCached", AuditCategory.VerifiedThisRun)]
    [InlineData("alreadyThere", AuditCategory.NameSizeMatch)]
    [InlineData("failed", AuditCategory.Unaccounted)]
    [InlineData("changedOnCard", AuditCategory.Unaccounted)]
    [InlineData("cardSwapped", AuditCategory.Unaccounted)]
    [InlineData("cancelled", AuditCategory.Unaccounted)]
    [InlineData("notStarted", AuditCategory.Unaccounted)]
    [InlineData("conflictAtRename", AuditCategory.Unaccounted)]
    [InlineData("newUnticked", AuditCategory.Unaccounted)]
    [InlineData("unfinishedUnticked", AuditCategory.Unaccounted)]
    [InlineData("probeErrorUnticked", AuditCategory.Unaccounted)]
    [InlineData("conflictUnticked", AuditCategory.Unaccounted)]
    [InlineData("ledgerVerified", AuditCategory.InLedger)]
    [InlineData("ledgerNameSize", AuditCategory.NameSizeMatch)]
    [InlineData("libraryListed", AuditCategory.NameSizeMatch)]
    [InlineData("dismissedVideo", AuditCategory.ConfirmedByYou)]
    [InlineData("probablyImportedPhoto", AuditCategory.AssumedByRule)]
    [InlineData("twinCopyOff", AuditCategory.SkippedByRule)]
    [InlineData("twinAssumed", AuditCategory.AssumedByRule)]
    [InlineData("dngVerifiedTwinFailed", AuditCategory.Unaccounted)]
    [InlineData("setImportedByFolder", AuditCategory.NameSizeMatch)]
    [InlineData("resumeMixed", AuditCategory.NameSizeMatch)]
    [InlineData("setOneFailed", AuditCategory.Unaccounted)]
    [InlineData("skip", AuditCategory.SkippedByRule)]
    [InlineData("unknown", AuditCategory.Unaccounted)]
    [InlineData("unknownDismissed", AuditCategory.ConfirmedByYou)]
    [InlineData("changedSinceScan", AuditCategory.Unaccounted)]
    public void UnitTakesTheWorstCategoryOfItsFiles(string name, AuditCategory expected)
        => Assert.Equal(expected, Case(name).Unit_.Worst);

    [Fact]
    public void NameSizeLedgerRecords_AreNeverInLedger()
    {
        var line = Assert.Single(Case("ledgerNameSize").Unit_.Lines);
        Assert.Equal(AuditCategory.NameSizeMatch, line.Category);
        Assert.Equal("in the history, matched by name and size", line.Detail);
    }

    [Fact]
    public void TwinLines_SayWhy()
    {
        Assert.Equal("JPG twin: copying disabled in Settings", Case("twinCopyOff").Unit_.Lines[1].Detail);
        var assumed = Case("twinAssumed").Unit_;
        Assert.Equal([AuditCategory.InLedger, AuditCategory.AssumedByRule], assumed.Lines.Select(l => l.Category).ToArray());
        Assert.Equal("JPG twin assumed imported with its DNG", assumed.Lines[1].Detail);
        Assert.Equal([AuditCategory.NameSizeMatch, AuditCategory.VerifiedThisRun],
                     Case("resumeMixed").Unit_.Lines.Select(l => l.Category).ToArray());
    }

    [Theory]
    [InlineData("failed", UnaccountedKind.Failed)]
    [InlineData("newUnticked", UnaccountedKind.NotCopied)]
    [InlineData("notStarted", UnaccountedKind.NotCopied)]
    [InlineData("unfinishedUnticked", UnaccountedKind.UnfinishedNotCopied)]
    [InlineData("unknown", UnaccountedKind.Unrecognised)]
    [InlineData("changedSinceScan", UnaccountedKind.ChangedSinceScan)]
    [InlineData("changedOnCard", UnaccountedKind.ChangedSinceScan)]
    public void UnaccountedUnits_CarryTheirMostUrgentReason(string name, UnaccountedKind expected)
    {
        var f = Case(name);
        Assert.Equal(expected, f.Run().Unaccounted[f.Unit]);
    }

    [Fact]
    public void ChangedSinceScan_DetailStartsWithTheSharedPrefix()
        => Assert.StartsWith(CardDiffResult.ChangedDetail, Assert.Single(Case("changedSinceScan").Unit_.Lines).Detail, StringComparison.Ordinal);

    [Fact]
    public void TruncatedClipCopiedOrMatched_IsCounted_AndCachedVerifiesAreCounted()
    {
        var b = new OffloadPlanBuilder();
        var copied = NewVideo(b, flags: ItemFlags.Truncated, hasTrinf: true);
        var f = Build(b, copied, j => new Verified(j, UInt128.One, VerifyMode.Cached));
        var audit = f.Run();
        Assert.Equal((1, 1), (audit.TruncatedAssumed, audit.CachedVerifies));

        var m = new OffloadPlanBuilder();
        m.LibraryVideo(ZRel + "\\" + V, 100);
        var listed = m.Video(V, 100, T0, new Imported(Evidence.LibraryNameSize, ZFolder(m), "listed"), ItemFlags.Truncated, hasTrinf: true);
        m.Group(new AlreadyImported(ZFolder(m)), null, listed);
        Assert.Equal(1, Build(m, listed).Run().TruncatedAssumed);
        Assert.Equal(0, Case("unfinishedUnticked").Run().TruncatedAssumed);
    }

    [Fact]
    public void EveryCardFile_GetsExactlyOneLine_AndAddedFilesBecomeUnits()
    {
        var b = new OffloadPlanBuilder();
        var v = NewVideo(b);
        b.Skip("DCIM/DJI_001/DJI_20260927140000_0001_D.LRF", 5, "proxy");
        b.Unknown("DCIM/DJI_A001/x.MP4", 5);
        b.Photo(P, 50, T0, twinSize: 5);
        var f = Build(b, v, Ok, new CardDiffResult([new CardDiffEntry("DCIM/DJI_001/late.MP4", 9)], [], [], 0));

        var audit = f.Run();

        var lines = audit.Units.SelectMany(u => u.Lines).Select(l => l.CardRelPath).ToList();
        Assert.Equal(f.Plan.Base.Scan.Inventory.Entries.Length + 1, lines.Count);
        Assert.Equal(lines.Count, lines.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var late = audit.Units.Single(u => u.Unit == new ItemId("DCIM/DJI_001/late.MP4"));
        Assert.Equal((AuditCategory.Unaccounted, CardDiffResult.AddedDetail), (late.Worst, late.Lines[0].Detail));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*AuditCategorizerTests"`
Expected: build FAILS with CS0103 / CS0246 (`AuditCategorizer`, `AuditUnits`, `UnaccountedKind` do not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Offload/AuditCategorizer.cs
using System.Collections.Immutable;

namespace UasSort.Core.Offload;

/// <summary>A unit's most urgent reason for being Unaccounted (enum order = urgency); used for the NotSafe headline.</summary>
public enum UnaccountedKind { ChangedSinceScan, Failed, UnfinishedNotCopied, NotCopied, Unrecognised }

public sealed record AuditUnits(ImmutableArray<UnitAudit> Units, ImmutableDictionary<ItemId, UnaccountedKind> Unaccounted,
                                int TruncatedAssumed, int CachedVerifies);

/// <summary>Ref §10.5: one category per card file, the worst per unit.</summary>
public static class AuditCategorizer
{
    private enum Role { Video, Photo, Twin, SetMember }

    public static AuditUnits Categorize(CardInventory inventory, Plan plan, OffloadResult? offload, LedgerSnapshot ledger, CardDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(diff);
        var ctx = new Context(plan, offload, ledger, diff);
        var units = ImmutableArray.CreateBuilder<UnitAudit>();
        var kinds = ImmutableDictionary.CreateBuilder<ItemId, UnaccountedKind>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        int truncatedAssumed = 0;

        AuditCategory Add(ItemId id, List<(AuditLine Line, UnaccountedKind? Kind)> lines)
        {
            var worst = lines.Max(l => l.Line.Category);
            units.Add(new UnitAudit(id, worst, [.. lines.Select(l => l.Line)]));
            if (worst == AuditCategory.Unaccounted)
                kinds[id] = lines.Where(l => l.Kind is not null).Min(l => l.Kind!.Value);
            return worst;
        }

        foreach (var unit in inventory.Units)
        {
            items.TryGetValue(unit.Id, out var item);
            var lines = new List<(AuditLine Line, UnaccountedKind? Kind)>();
            switch (unit)
            {
                case VideoUnit v:
                    lines.Add(ctx.Media(v.Mp4, item, Role.Video, null));
                    break;
                case PhotoUnit p:
                    lines.Add(ctx.Media(p.Primary, item, Role.Photo, null));
                    if (p.JpgTwin is { } twin) lines.Add(ctx.Media(twin, item, Role.Twin, null));
                    break;
                case SetUnit s:
                    foreach (var m in s.Members) lines.Add(ctx.Media(m, item, Role.SetMember, s));
                    break;
            }
            foreach (var l in lines) claimed.Add(OffloadPaths.NormRel(l.Line.CardRelPath));
            var worst = Add(unit.Id, lines);
            if (Context.Truncated(item) || unit is VideoUnit { HasTrinf: true })
                if (worst is AuditCategory.VerifiedThisRun or AuditCategory.InLedger or AuditCategory.NameSizeMatch) truncatedAssumed++;
        }
        foreach (var e in inventory.Entries)
            if (!claimed.Contains(OffloadPaths.NormRel(e.RelPath))) Add(new ItemId(e.RelPath), [ctx.Loose(e)]);
        foreach (var added in diff.Added)
            Add(new ItemId(added.RelPath),
                [(new AuditLine(added.RelPath, added.Size, AuditCategory.Unaccounted, CardDiffResult.AddedDetail), UnaccountedKind.ChangedSinceScan)]);

        int cached = offload?.Outcomes.Count(o => o is Verified { Mode: VerifyMode.Cached }) ?? 0;
        return new AuditUnits(units.ToImmutable(), kinds.ToImmutable(), truncatedAssumed, cached);
    }

    private sealed class Context
    {
        private readonly Plan _plan;
        private readonly LedgerSnapshot _ledger;
        private readonly CardDiffResult _diff;
        private readonly Dictionary<string, CopyOutcome> _outcomes = new(StringComparer.OrdinalIgnoreCase);

        public Context(Plan plan, OffloadResult? offload, LedgerSnapshot ledger, CardDiffResult diff)
        {
            _plan = plan;
            _ledger = ledger;
            _diff = diff;
            if (offload is not null)
                foreach (var o in offload.Outcomes) _outcomes[OffloadPaths.NormRel(o.Job.CardRelPath)] = o;
        }

        public static bool Truncated(Item? item)
            => item is not null && (item.Flags.HasFlag(ItemFlags.Truncated) || item.Raw.Unit is VideoUnit { HasTrinf: true });

        private static UnaccountedKind NotCopied(Item? item) => Truncated(item) ? UnaccountedKind.UnfinishedNotCopied : UnaccountedKind.NotCopied;

        private static (AuditLine Line, UnaccountedKind? Kind) Line(CardEntry e, AuditCategory category, string detail)
            => (new AuditLine(e.RelPath, e.Size, category, detail), null);

        private static (AuditLine Line, UnaccountedKind? Kind) Bad(CardEntry e, string detail, UnaccountedKind kind)
            => (new AuditLine(e.RelPath, e.Size, AuditCategory.Unaccounted, detail), kind);

        private static string DecisionText(LedgerDecision d)
            => d.Kind == DecisionKind.Dismissed ? "you marked it not needed" : "you recorded it as imported";

        public (AuditLine Line, UnaccountedKind? Kind) Loose(CardEntry e)
        {
            if (_diff.Touched(e.RelPath) is { } changed) return Bad(e, changed, UnaccountedKind.ChangedSinceScan);
            if (e.Class == EntryClass.Skip) return Line(e, AuditCategory.SkippedByRule, e.Rule ?? "skipped by rule");
            if (_ledger.Decisions.TryGetValue(OffloadPaths.Key(e.RelPath, e.Size), out var d))
                return Line(e, AuditCategory.ConfirmedByYou, DecisionText(d));
            return Bad(e, "not recognised by uas-sort; copy it by hand if you need it", UnaccountedKind.Unrecognised);
        }

        public (AuditLine Line, UnaccountedKind? Kind) Media(CardEntry e, Item? item, Role role, SetUnit? set)
        {
            if (_diff.Touched(e.RelPath) is { } changed) return Bad(e, changed, UnaccountedKind.ChangedSinceScan);
            if (_outcomes.TryGetValue(OffloadPaths.NormRel(e.RelPath), out var o)) return FromOutcome(e, o, item);
            return FromEvidence(e, item, role, set);
        }

        private static (AuditLine Line, UnaccountedKind? Kind) FromOutcome(CardEntry e, CopyOutcome o, Item? item) => o switch
        {
            Verified v => Line(e, AuditCategory.VerifiedThisRun, v.Mode == VerifyMode.Cached ? "verified this run (buffered read-back)" : "verified this run"),
            AlreadyThere => Line(e, AuditCategory.NameSizeMatch, "already at the destination with the same size"),
            Failed f => Bad(e, $"failed ({f.Phase}): {f.Error}", UnaccountedKind.Failed),
            ChangedOnCard => Bad(e, CardDiffResult.ChangedDetail + " (during the offload)", UnaccountedKind.ChangedSinceScan),
            CardSwapped => Bad(e, "not copied: the card was swapped", NotCopied(item)),
            Cancelled => Bad(e, "not copied: cancelled", NotCopied(item)),
            NotStarted => Bad(e, "not copied: the offload stopped first", NotCopied(item)),
            ConflictAtRename => Bad(e, "not copied: a file with this name appeared at the destination", NotCopied(item)),
        };

        private (AuditLine Line, UnaccountedKind? Kind) FromEvidence(CardEntry e, Item? item, Role role, SetUnit? set)
        {
            var key = OffloadPaths.Key(e.RelPath, e.Size);
            bool Fits(string? recordSet) => set is null || string.Equals(recordSet, set.SetName, StringComparison.OrdinalIgnoreCase);

            _ledger.Files.TryGetValue(key, out var file);
            if (file is not null && !Fits(file.Set)) file = null;
            if (file is { Verify: VerifyKind.Unbuffered or VerifyKind.Cached }) return Line(e, AuditCategory.InLedger, "in the history, verified");
            if (_ledger.Decisions.TryGetValue(key, out var d) && Fits(d.Set)) return Line(e, AuditCategory.ConfirmedByYou, DecisionText(d));
            if (file is { Verify: VerifyKind.NameSize }) return Line(e, AuditCategory.NameSizeMatch, "in the history, matched by name and size");
            if (set is not null)
            {
                if (_plan.Base.Sets.TryGetValue(set.Id, out var placement)
                    && (placement.Resolution == SetResolution.Imported
                        || (placement.Resolution == SetResolution.Resume
                            && !placement.MembersToCopy.Contains(OffloadPaths.FileName(e.RelPath), StringComparer.OrdinalIgnoreCase))))
                    return Line(e, AuditCategory.NameSizeMatch, "in the library's set folder (name, size and time)");
            }
            else if (!_plan.Base.Scan.Library.Match(key).IsEmpty)
            {
                return Line(e, AuditCategory.NameSizeMatch, "same name and size in the library");
            }
            if (role == Role.Twin)
            {
                if (!_plan.Base.Scan.Settings.CopyJpgTwin) return Line(e, AuditCategory.SkippedByRule, "JPG twin: copying disabled in Settings");
                if (item?.Newness is Imported) return Line(e, AuditCategory.AssumedByRule, "JPG twin assumed imported with its DNG");
                if (item?.Newness is Decided) return Line(e, AuditCategory.ConfirmedByYou, "follows its DNG (confirmed by you)");
            }
            return item?.Newness switch
            {
                ProbablyImported p => Line(e, AuditCategory.AssumedByRule, $"probably imported: {p.Why}"),
                Decided => Line(e, AuditCategory.ConfirmedByYou, "confirmed by you"),
                Imported i when i.By == Evidence.LedgerVerified => Line(e, AuditCategory.InLedger, "in the history, verified"),
                Imported => Line(e, AuditCategory.NameSizeMatch, "matched by name and size"),
                Conflict => Bad(e, "a different file with this name is in the library; not copied", NotCopied(item)),
                IsNew => Bad(e, NewDetail(item!), NotCopied(item)),
                null => Bad(e, "not part of the plan", UnaccountedKind.Unrecognised),
            };
        }

        private static string NewDetail(Item item)
            => item.Raw.ProbeError is not null ? "new, metadata unreadable; not copied"
             : Truncated(item) ? "unfinished recording; not copied"
             : "new; not copied";
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*AuditCategorizerTests"`
Expected: PASS (40 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/AuditCategorizer.cs tests/UasSort.Core.Tests/Offload/AuditCategorizerTests.cs
git commit -m "feat: categorise every card file for the audit and take the worst per unit

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.14: `CardAudit.Audit` — verdict level, headline, card changes, safe removal

**Files:**
- Create: `src/UasSort.Core/Offload/VerdictText.cs`
- Create: `src/UasSort.Core/Offload/CardAudit.cs`
- Test: `tests/UasSort.Core.Tests/Offload/CardAuditTests.cs`

**Interfaces:**

```csharp
// Consumes: CardInventory, ListingResult, CardIdentity, Plan (Issues, Base.Scan.Warnings), OffloadResult (VolumesNeedingSafeRemoval),
//   LedgerSnapshot, FormatVerdict, VerdictLevel, AuditCategory, ScanWarning.ForcesNotSafe (Ref §3); CardDiff (07.12); AuditCategorizer (07.13).
// Produces:
public static class CardAudit                                   // Ref §4.2
{
    // `now` null = CurrentIdentity() failed (the card is gone) → NotSafe. `pinned` = the identity the Commit pinned (OffloadBatch.Card);
    // default Inventory.Source.Identity. This exact signature is registry decision 13 (Parts 08, 10 and 11 call it).
    public static FormatVerdict Audit(CardInventory inventory, ListingResult relisted, CardIdentity? now, Plan plan,
                                      OffloadResult? offload, LedgerSnapshot ledger, CardIdentity? pinned = null);
    // FormatVerdict.CardChanges lines are "added: <rel>", "removed: <rel>", "changed: <rel>" and, for last-access-only differences,
    // "OS updated last-access times on N files (not counted)"; ChangedPaths returns the <rel> of the first three kinds.
    public static ImmutableArray<string> ChangedPaths(FormatVerdict verdict);
}
public static class VerdictText                                 // (defined here)
{
    public static string CameraName(string? model);             // "FC9113" -> "DJI Air 3S"; null -> "DJI drone"; else the model text
    public static string Serial(CardIdentity id);               // 0x1A2B3C4D -> "1A2B-3C4D"
    public static string CardName(CardInventory inventory, CardIdentity id);   // "E: · DJI Air 3S · serial 1A2B-3C4D"
    public static string Headline(VerdictLevel level, CardInventory inventory, CardIdentity card, bool identityChanged, int forcesNotSafe,
                                  bool ledgerIssuesAccepted, IReadOnlyDictionary<AuditCategory, int> counts, AuditUnits audit);
}
```

Verdict (Ref §10.5): **NotSafe** when the identity changed or is unreadable, a `ForcesNotSafe` scan warning remains, or any unit is Unaccounted (the re-list diff feeds Unaccounted through Task 07.13); else **SafeWithAssumptions** when any unit is AssumedByRule, a truncated clip was copied or matched (`TruncatedAssumed`), or the plan carries an accepted `LedgerParseIssue` (a Warning, not Blocking — how `PlanSession.AcceptLedgerIssues` leaves it); else **Safe**. `Counts` are per unit (worst category); `NameSizeOnly` = units whose worst is NameSizeMatch; `CachedVerifies` from Task 07.13. Headlines:
- Safe: "{card}: Safe to format: {V} verified[ ({c} with buffered read-back)][, {n} matched by name+size only][, {k} confirmed by you]" with V = VerifiedThisRun + InLedger units.
- SafeWithAssumptions: "{card}: Safe, with assumptions: {reasons}; {the same evidence split}" — reasons "{n} photo(s) was/were assumed already imported", "{t} unfinished recording(s) copied as-is; the drone may still be able to repair it/them", "unreadable history lines were accepted".
- NotSafe: "{card}: Don't format yet: The card in E: is not the one that was offloaded" for an identity change; otherwise the reasons in this order: "{n} part(s) of the card couldn't be read", "{n} file(s) failed", "{n} unfinished recording(s) not copied", "{n} new file(s) not copied", "{n} file(s) changed since the scan", "{n} unrecognised file(s)".
- `SafeRemovalNote` = "Safely remove D: before formatting the card" (volumes joined with " and ") when `VolumesNeedingSafeRemoval` is not empty.
- Every changed/added/removed audit detail starts with exactly `changed since scan` (`CardDiffResult.ChangedDetail`, Task 07.12; also the "(during the offload)" detail of a `ChangedOnCard` outcome). Keep this signature and this prefix as they are: Part 08 binds `CleanupRules.ChangedSinceScanDetail = CardDiffResult.ChangedDetail` (registry decision 13).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/CardAuditTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Core.Tests.Offload.CommitTailRecordsTests;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class CardAuditTests
{
    private const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private const string Name = "E: · DJI Air 3S · serial 1A2B-3C4D";

    internal static ListingResult Unchanged(CardInventory inventory)
        => new([.. inventory.Entries.Select(e => new FsEntry(PathRules.Join(CardRoot, e.RelPath), e.RelPath.Replace('/', '\\'), false,
                                                               e.Size, e.MtimeUtc, e.CreationUtc, e.LastAccessUtc, e.RawAttributes))], []);

    private static CopyOutcome Ok(CopyJob j) => new Verified(j, UInt128.One, VerifyMode.Unbuffered);

    private static FormatVerdict Audit(Plan plan, OffloadResult? result, ListingResult? relisted = null, CardIdentity? now = null, bool gone = false)
        => CardAudit.Audit(plan.Base.Scan.Inventory, relisted ?? Unchanged(plan.Base.Scan.Inventory), gone ? null : now ?? OffloadPlanBuilder.Card,
                           plan, result, plan.Base.Scan.Ledger, OffloadPlanBuilder.Card);

    private static (Plan Plan, OffloadResult Result) TwoVideosAndAListedPhoto(Func<CopyJob, CopyOutcome>? outcome = null)
    {
        var b = new OffloadPlanBuilder();
        var v1 = b.Video("DJI_20260927140000_0001_D.MP4", 100, T0);
        var v2 = b.Video("DJI_20260927140100_0002_D.MP4", 200, T0.AddMinutes(1));
        b.Group(new NewFolder(ZRel), Zachar, v1, v2);
        b.LibraryPhoto("DJI_20260927140200_0003_D.DNG", 50);
        b.Photo("DJI_20260927140200_0003_D.DNG", 50, T0.AddMinutes(2), new Imported(Evidence.LibraryNameSize, null, "listed"), included: false);
        var plan = b.Build();
        return (plan, Result(OffloadCompiler.Compile(plan, "run-1"), outcome ?? Ok));
    }

    [Fact]
    public void Safe_NamesTheCard_AndSplitsTheEvidence()
    {
        var (plan, result) = TwoVideosAndAListedPhoto();

        var v = Audit(plan, result);

        Assert.Equal(VerdictLevel.Safe, v.Level);
        Assert.Equal($"{Name}: Safe to format: 2 verified, 1 matched by name+size only", v.Headline);
        Assert.Equal((2, 1, 1, 0), (v.Counts[AuditCategory.VerifiedThisRun], v.Counts[AuditCategory.NameSizeMatch], v.NameSizeOnly, v.CachedVerifies));
        Assert.Equal(OffloadPlanBuilder.Card, v.Card);
        Assert.Empty(v.CardChanges);
        Assert.Null(v.SafeRemovalNote);
    }

    [Fact]
    public void BufferedVerifies_AreCountedInTheHeadline()
    {
        var (plan, result) = TwoVideosAndAListedPhoto(j => j.CardRelPath.EndsWith("0001_D.MP4", StringComparison.Ordinal)
                                                              ? new Verified(j, UInt128.One, VerifyMode.Cached) : Ok(j));

        var v = Audit(plan, result);

        Assert.Equal($"{Name}: Safe to format: 2 verified (1 with buffered read-back), 1 matched by name+size only", v.Headline);
        Assert.Equal(1, v.CachedVerifies);
    }

    [Fact]
    public void ProbablyImportedPhotos_GiveSafeWithAssumptions()
    {
        var b = new OffloadPlanBuilder();
        var v1 = b.Video("DJI_20260927140000_0001_D.MP4", 100, T0);
        b.Group(new NewFolder(ZRel), Zachar, v1);
        b.Photo("DJI_20260927100000_0001_D.DNG", 50, T0.AddHours(-4), new ProbablyImported("videos from this day are already in the library"), included: false);
        b.Photo("DJI_20260927100100_0002_D.DNG", 50, T0.AddHours(-4), new ProbablyImported("videos from this day are already in the library"), included: false);
        var plan = b.Build();

        var v = Audit(plan, Result(OffloadCompiler.Compile(plan, "run-1"), Ok));

        Assert.Equal(VerdictLevel.SafeWithAssumptions, v.Level);
        Assert.Equal($"{Name}: Safe, with assumptions: 2 photos were assumed already imported; 1 verified", v.Headline);
    }

    [Fact]
    public void ATruncatedClipCopiedAsIs_GivesSafeWithAssumptions()
    {
        var b = new OffloadPlanBuilder();
        var v1 = b.Video("DJI_20260727002013_0014_D.MP4", 100, T0, flags: ItemFlags.Truncated, hasTrinf: true);
        b.Group(new NewFolder(ZRel), Zachar, v1);
        var plan = b.Build();

        var v = Audit(plan, Result(OffloadCompiler.Compile(plan, "run-1"), Ok));

        Assert.Equal($"{Name}: Safe, with assumptions: 1 unfinished recording copied as-is; the drone may still be able to repair it; 1 verified", v.Headline);
    }

    [Fact]
    public void AcceptedLedgerParseIssues_CapTheVerdict()
    {
        var b = new OffloadPlanBuilder();
        var v1 = b.Video("DJI_20260927140000_0001_D.MP4", 100, T0);
        b.Group(new NewFolder(ZRel), Zachar, v1);
        b.Issue(new Issue(IssueSeverity.Warning, IssueCode.LedgerParseIssue, "Ledger line ledger-B.jsonl:12 can't be read: bad JSON", null, [], true));
        var plan = b.Build();

        var v = Audit(plan, Result(OffloadCompiler.Compile(plan, "run-1"), Ok));

        Assert.Equal($"{Name}: Safe, with assumptions: unreadable history lines were accepted; 1 verified", v.Headline);
    }

    [Fact]
    public void FailuresAndUnfinishedRecordingsLeftOnTheCard_AreNotSafe()
    {
        var b = new OffloadPlanBuilder();
        var ok = b.Video("DJI_20260927140000_0001_D.MP4", 100, T0);
        var bad = b.Video("DJI_20260927140100_0002_D.MP4", 100, T0.AddMinutes(1));
        var u1 = b.Video("DJI_20260927140200_0003_D.MP4", 7, T0.AddMinutes(2), flags: ItemFlags.Truncated, included: false, hasTrinf: true);
        var u2 = b.Video("DJI_20260927140300_0004_D.MP4", 7, T0.AddMinutes(3), flags: ItemFlags.Truncated, included: false, hasTrinf: true);
        b.Group(new NewFolder(ZRel), Zachar, ok, bad, u1, u2);
        var plan = b.Build();
        var result = Result(OffloadCompiler.Compile(plan, "run-1"), j => j.Item == bad ? new Failed(j, CopyPhase.Verify, "mismatch twice") : Ok(j));

        var v = Audit(plan, result);

        Assert.Equal(VerdictLevel.NotSafe, v.Level);
        Assert.Equal($"{Name}: Don't format yet: 1 file failed, 2 unfinished recordings not copied", v.Headline);
    }

    [Fact]
    public void ADifferentOrMissingCard_IsNotSafe()
    {
        var (plan, result) = TwoVideosAndAListedPhoto();

        var swapped = Audit(plan, result, now: OffloadPlanBuilder.Card with { VolumeSerial = 0xDEADBEEF });
        var gone = Audit(plan, result, gone: true);

        Assert.Equal(VerdictLevel.NotSafe, swapped.Level);
        Assert.Equal($"{Name}: Don't format yet: The card in E: is not the one that was offloaded", swapped.Headline);
        Assert.Equal(VerdictLevel.NotSafe, gone.Level);
    }

    [Fact]
    public void AnEnumerationErrorWarning_ForcesNotSafe()
    {
        var b = new OffloadPlanBuilder().Warning(new ScanWarning("EnumerationError", "Access denied: DCIM/DJI_002", "DCIM/DJI_002", true));
        var v1 = b.Video("DJI_20260927140000_0001_D.MP4", 100, T0);
        b.Group(new NewFolder(ZRel), Zachar, v1);
        var plan = b.Build();

        var v = Audit(plan, Result(OffloadCompiler.Compile(plan, "run-1"), Ok));

        Assert.Equal($"{Name}: Don't format yet: 1 part of the card couldn't be read", v.Headline);
    }

    [Fact]
    public void AFileAddedToTheCardAfterTheScan_IsNotSafe_AndListed()
    {
        var (plan, result) = TwoVideosAndAListedPhoto();
        var relisted = Unchanged(plan.Base.Scan.Inventory);
        relisted = relisted with { Entries = relisted.Entries.Add(new FsEntry(@"E:\DCIM\DJI_001\late.MP4", @"DCIM\DJI_001\late.MP4", false, 9, T0, T0, T0, 0x20)) };

        var v = Audit(plan, result, relisted);

        Assert.Equal(VerdictLevel.NotSafe, v.Level);
        Assert.Equal($"{Name}: Don't format yet: 1 file changed since the scan", v.Headline);
        Assert.Equal(["added: DCIM/DJI_001/late.MP4"], v.CardChanges.ToArray());
        Assert.Equal(["DCIM/DJI_001/late.MP4"], CardAudit.ChangedPaths(v).ToArray());
    }

    [Fact]
    public void LastAccessOnlyDifferences_AreReportedNotCounted()
    {
        var (plan, result) = TwoVideosAndAListedPhoto();
        var relisted = Unchanged(plan.Base.Scan.Inventory);
        relisted = relisted with { Entries = [.. relisted.Entries.Select(e => e with { LastAccessUtc = e.LastAccessUtc.AddDays(1) })] };

        var v = Audit(plan, result, relisted);

        Assert.Equal(VerdictLevel.Safe, v.Level);
        Assert.Equal(["OS updated last-access times on 3 files (not counted)"], v.CardChanges.ToArray());
        Assert.Empty(CardAudit.ChangedPaths(v));
    }

    [Fact]
    public void ANonNtfsDestination_AddsTheSafeRemovalNote()
    {
        var (plan, result) = TwoVideosAndAListedPhoto();

        var v = Audit(plan, result with { VolumesNeedingSafeRemoval = [@"D:\"] });

        Assert.Equal("Safely remove D: before formatting the card", v.SafeRemovalNote);
    }

    [Fact]
    public void SafeIsImpossible_WhileAnythingIsUnaccountedOrAssumed()
    {
        string[] names = ["verified", "verifiedCached", "alreadyThere", "failed", "changedOnCard", "cardSwapped", "cancelled", "notStarted",
                          "conflictAtRename", "newUnticked", "unfinishedUnticked", "probeErrorUnticked", "conflictUnticked", "ledgerVerified",
                          "ledgerNameSize", "libraryListed", "dismissedVideo", "probablyImportedPhoto", "twinCopyOff", "twinAssumed",
                          "dngVerifiedTwinFailed", "setImportedByFolder", "resumeMixed", "setOneFailed", "skip", "unknown", "unknownDismissed"];
        foreach (var name in names)
        {
            var f = AuditCategorizerTests.Case(name);
            var v = Audit(f.Plan, f.Result);
            if (v.Units.Any(u => u.Worst == AuditCategory.Unaccounted)) Assert.True(v.Level == VerdictLevel.NotSafe, name);
            else if (v.Units.Any(u => u.Worst == AuditCategory.AssumedByRule)) Assert.True(v.Level == VerdictLevel.SafeWithAssumptions, name);
            else Assert.True(v.Level == VerdictLevel.Safe, name);
        }
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CardAuditTests"`
Expected: build FAILS with CS0103 (`CardAudit` does not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Offload/VerdictText.cs
using System.Globalization;

namespace UasSort.Core.Offload;

/// <summary>The verdict's wording (Ref §10.5): the card named, the evidence split, the reasons.</summary>
public static class VerdictText
{
    public static string CameraName(string? model) => model switch
    {
        null or "" => "DJI drone",
        "FC9113" => "DJI Air 3S",
        _ => model,
    };

    public static string Serial(CardIdentity id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var x = id.VolumeSerial.ToString("X8", CultureInfo.InvariantCulture);
        return $"{x[..4]}-{x[4..]}";
    }

    public static string CardName(CardInventory inventory, CardIdentity id)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return $"{Drive(inventory)} · {CameraName(inventory.CameraModel)} · serial {Serial(id)}";
    }

    private static string Drive(CardInventory inventory) => OffloadPaths.DriveLabel(OffloadPaths.VolumeRoot(inventory.Source.Root));

    private static string N(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    public static string Headline(VerdictLevel level, CardInventory inventory, CardIdentity card, bool identityChanged, int forcesNotSafe,
                                  bool ledgerIssuesAccepted, IReadOnlyDictionary<AuditCategory, int> counts, AuditUnits audit)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(audit);
        var name = CardName(inventory, card);
        switch (level)
        {
            case VerdictLevel.NotSafe:
            {
                if (identityChanged) return $"{name}: Don't format yet: The card in {Drive(inventory)} is not the one that was offloaded";
                var reasons = new List<string>();
                if (forcesNotSafe > 0) reasons.Add(N(forcesNotSafe, "part of the card couldn't be read", "parts of the card couldn't be read"));
                int Kind(UnaccountedKind k) => audit.Unaccounted.Values.Count(v => v == k);
                if (Kind(UnaccountedKind.Failed) is > 0 and var failed) reasons.Add(N(failed, "file failed", "files failed"));
                if (Kind(UnaccountedKind.UnfinishedNotCopied) is > 0 and var unfinished)
                    reasons.Add(N(unfinished, "unfinished recording not copied", "unfinished recordings not copied"));
                if (Kind(UnaccountedKind.NotCopied) is > 0 and var notCopied) reasons.Add(N(notCopied, "new file not copied", "new files not copied"));
                if (Kind(UnaccountedKind.ChangedSinceScan) is > 0 and var changed)
                    reasons.Add(N(changed, "file changed since the scan", "files changed since the scan"));
                if (Kind(UnaccountedKind.Unrecognised) is > 0 and var unknown) reasons.Add(N(unknown, "unrecognised file", "unrecognised files"));
                return $"{name}: Don't format yet: {string.Join(", ", reasons)}";
            }
            case VerdictLevel.SafeWithAssumptions:
            {
                var reasons = new List<string>();
                if (Count(counts, AuditCategory.AssumedByRule) is > 0 and var assumed)
                    reasons.Add($"{N(assumed, "photo was", "photos were")} assumed already imported");
                if (audit.TruncatedAssumed > 0)
                    reasons.Add($"{N(audit.TruncatedAssumed, "unfinished recording", "unfinished recordings")} copied as-is; "
                                + $"the drone may still be able to repair {(audit.TruncatedAssumed == 1 ? "it" : "them")}");
                if (ledgerIssuesAccepted) reasons.Add("unreadable history lines were accepted");
                return $"{name}: Safe, with assumptions: {string.Join(", ", reasons)}; {Evidence(counts, audit)}";
            }
            default:
                return $"{name}: Safe to format: {Evidence(counts, audit)}";
        }
    }

    private static int Count(IReadOnlyDictionary<AuditCategory, int> counts, AuditCategory c) => counts.TryGetValue(c, out var n) ? n : 0;

    private static string Evidence(IReadOnlyDictionary<AuditCategory, int> counts, AuditUnits audit)
    {
        int verified = Count(counts, AuditCategory.VerifiedThisRun) + Count(counts, AuditCategory.InLedger);
        var parts = new List<string>
        {
            $"{verified} verified" + (audit.CachedVerifies > 0 ? $" ({audit.CachedVerifies} with buffered read-back)" : ""),
        };
        if (Count(counts, AuditCategory.NameSizeMatch) is > 0 and var nameSize) parts.Add($"{nameSize} matched by name+size only");
        if (Count(counts, AuditCategory.ConfirmedByYou) is > 0 and var confirmed) parts.Add($"{confirmed} confirmed by you");
        return string.Join(", ", parts);
    }
}
```

```csharp
// src/UasSort.Core/Offload/CardAudit.cs
using System.Collections.Immutable;

namespace UasSort.Core.Offload;

/// <summary>Ref §10.5: the "safe to format?" verdict for the card in the reader. Listing only; opens nothing.</summary>
public static class CardAudit
{
    private static readonly string[] ChangePrefixes = ["added: ", "removed: ", "changed: "];

    public static FormatVerdict Audit(CardInventory inventory, ListingResult relisted, CardIdentity? now, Plan plan,
                                      OffloadResult? offload, LedgerSnapshot ledger, CardIdentity? pinned = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(relisted);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ledger);
        var expected = pinned ?? inventory.Source.Identity;
        bool identityChanged = now is null || (expected is not null && now != expected);
        var card = expected ?? now ?? new CardIdentity(0, null, "", 0);

        var diff = CardDiff.Compare(inventory.Entries, relisted);
        var audit = AuditCategorizer.Categorize(inventory, plan, offload, ledger, diff);
        var counts = Enum.GetValues<AuditCategory>().ToImmutableDictionary(c => c, c => audit.Units.Count(u => u.Worst == c));
        int forces = inventory.Warnings.Concat(plan.Base.Scan.Warnings).Distinct().Count(w => w.ForcesNotSafe);
        bool ledgerAccepted = plan.Issues.Any(i => i.Code == IssueCode.LedgerParseIssue && i.Severity != IssueSeverity.Blocking);

        var level = identityChanged || forces > 0 || counts[AuditCategory.Unaccounted] > 0 ? VerdictLevel.NotSafe
                  : counts[AuditCategory.AssumedByRule] > 0 || audit.TruncatedAssumed > 0 || ledgerAccepted ? VerdictLevel.SafeWithAssumptions
                  : VerdictLevel.Safe;

        var volumes = offload?.VolumesNeedingSafeRemoval ?? [];
        var note = volumes.IsEmpty ? null
                 : $"Safely remove {string.Join(" and ", volumes.Select(OffloadPaths.DriveLabel))} before formatting the card";
        var headline = VerdictText.Headline(level, inventory, card, identityChanged, forces, ledgerAccepted, counts, audit);
        return new FormatVerdict(level, card, headline, counts, counts[AuditCategory.NameSizeMatch], audit.CachedVerifies, audit.Units,
                                 CardChanges(diff), note);
    }

    private static ImmutableArray<string> CardChanges(CardDiffResult diff)
    {
        var lines = new List<string>();
        lines.AddRange(diff.Added.Select(e => "added: " + e.RelPath));
        lines.AddRange(diff.Removed.Select(e => "removed: " + e.RelPath));
        lines.AddRange(diff.Changed.Select(e => "changed: " + e.RelPath));
        if (diff.LastAccessOnly > 0) lines.Add($"OS updated last-access times on {diff.LastAccessOnly} files (not counted)");
        return [.. lines];
    }

    public static ImmutableArray<string> ChangedPaths(FormatVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        return [.. verdict.CardChanges
            .Select(c => ChangePrefixes.FirstOrDefault(prefix => c.StartsWith(prefix, StringComparison.Ordinal)) is { } hit ? c[hit.Length..] : null)
            .OfType<string>()];
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CardAuditTests"`
Expected: PASS (12 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/VerdictText.cs src/UasSort.Core/Offload/CardAudit.cs tests/UasSort.Core.Tests/Offload/CardAuditTests.cs
git commit -m "feat: compute the safe-to-format verdict and its headline

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.15: Verdict-page decisions — record as imported, not needed, undo

**Files:**
- Create: `src/UasSort.Core/Offload/VerdictDecisions.cs`
- Test: `tests/UasSort.Core.Tests/Offload/VerdictDecisionsTests.cs`

**Interfaces:**

```csharp
// Consumes: FormatVerdict/UnitAudit/AuditLine, Plan, CardInventory units and entries, Item, ProbablyImported, DecisionKind, DecisionRecord,
//   RevokeRecord, LedgerSnapshot (a sealed record: `with`), LedgerDecision (Ref §3); OffloadRecords.NewId/Version (07.7); OffloadPaths (07.1);
//   CardAudit (07.14, to recompute the verdict).
// Produces (defined here):
public enum NotCopiedKind { Video, Photo, Set, Unknown }
public sealed record NotCopiedRow(ItemId Unit, NotCopiedKind Kind, bool Truncated, DateOnly? LocalDate, int Files, long Bytes,
                                  AuditCategory Category, string Detail);
public sealed record DecisionCheck(bool Ok, string? Refusal);
public static class VerdictDecisions
{
    public static ImmutableArray<NotCopiedRow> NotCopied(FormatVerdict verdict, Plan plan);          // Unaccounted + AssumedByRule units
    public static ImmutableArray<NotCopiedRow> Day(IEnumerable<NotCopiedRow> rows, DateOnly localDate); // photos and sets of that day only
    public static DecisionCheck Check(DecisionKind kind, IReadOnlyList<NotCopiedRow> selected);
    public static string Confirmation(DecisionKind kind, IReadOnlyList<NotCopiedRow> selected);       // counts by kind + total GB
    public static ImmutableArray<DecisionRecord> Records(DecisionKind kind, IReadOnlyList<NotCopiedRow> selected, Plan plan,
                                                         string? runId, string machine, TimeProvider clock);
    public static ImmutableArray<RevokeRecord> Revokes(IEnumerable<string> decisionIds, string machine, TimeProvider clock);
    public static LedgerSnapshot Apply(LedgerSnapshot ledger, IEnumerable<DecisionRecord> made, IEnumerable<RevokeRecord> revoked);
}
```

Rules (Ref §10.5 "Verdict page"): the list holds every unit whose worst category is Unaccounted or AssumedByRule; nothing is preselected (the VM starts with an empty selection; Core only lists). `assumedImported` is for photos and sets only (tile or day). `dismissed` may take photos and sets in bulk or per day, but a video (truncated or not) and an unknown file only one at a time. Records: one per file — a video's MP4; a photo's DNG and its JPG twin; every member of a set (each with `set` = the set name); an unknown file itself. `why` is the ProbablyImported reason for `assumedImported` when there is one, else "confirmed by you"; "not needed" for `dismissed`. Undo writes one `revoke` per decision made. `Apply` gives the snapshot the caller passes back to `CardAudit.Audit` to recompute the verdict without reloading the ledger.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/VerdictDecisionsTests.cs
using Microsoft.Extensions.Time.Testing;
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Core.Tests.Offload.CardAuditTests;
using static UasSort.Core.Tests.Offload.CommitTailRecordsTests;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class VerdictDecisionsTests
{
    private const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(T0.AddHours(3)));

    private sealed record Fixture(Plan Plan, OffloadResult Result, FormatVerdict Verdict, ItemId Copied, ItemId NewVideo, ItemId Truncated,
                                  ItemId Photo1, ItemId Photo2, ItemId Set, ItemId Unknown);

    private static Fixture Build()
    {
        var b = new OffloadPlanBuilder();
        var copied = b.Video("DJI_20260927140000_0001_D.MP4", 1_200_000_000, T0);
        var fresh = b.Video("DJI_20260927140100_0002_D.MP4", 1_200_000_000, T0.AddMinutes(1), included: false);
        var truncated = b.Video("DJI_20260927140200_0003_D.MP4", 7_000_000, T0.AddMinutes(2), flags: ItemFlags.Truncated, included: false, hasTrinf: true);
        b.Group(new NewFolder(ZRel), Zachar, copied, fresh, truncated);
        var why = "videos from this day are already in the library";
        var p1 = b.Photo("DJI_20260927100000_0010_D.DNG", 27_000_000, T0.AddHours(-4), new ProbablyImported(why), twinSize: 3_000_000, included: false);
        var p2 = b.Photo("DJI_20260927100100_0011_D.DNG", 27_000_000, T0.AddHours(-4).AddMinutes(1), new ProbablyImported(why), included: false);
        var set = b.Set("001_0087", [("PANO_0001.DNG", 13_000_000), ("PANO_0002.DNG", 12_000_000)], T0.AddHours(-3), SetResolution.Plain,
                        newness: new ProbablyImported(why), included: false);
        var unknown = new ItemId(b.Unknown("DCIM/DJI_A001/x.MP4", 5_000).RelPath);
        b.Skip("DCIM/DJI_001/DJI_20260927140000_0001_D.LRF", 10, "proxy");
        var plan = b.Build();
        var result = Result(OffloadCompiler.Compile(plan, "run-1"), j => new Verified(j, UInt128.One, VerifyMode.Unbuffered));
        var verdict = CardAudit.Audit(plan.Base.Scan.Inventory, Unchanged(plan.Base.Scan.Inventory), OffloadPlanBuilder.Card, plan, result,
                                      plan.Base.Scan.Ledger, OffloadPlanBuilder.Card);
        return new Fixture(plan, result, verdict, copied, fresh, truncated, p1, p2, set, unknown);
    }

    private static NotCopiedRow Row(Fixture f, ItemId id) => VerdictDecisions.NotCopied(f.Verdict, f.Plan).Single(r => r.Unit == id);

    [Fact]
    public void NotCopied_ListsUnaccountedAndAssumedUnits_WithKindDayAndSize()
    {
        var f = Build();

        var rows = VerdictDecisions.NotCopied(f.Verdict, f.Plan);

        Assert.Equal([f.NewVideo, f.Truncated, f.Photo1, f.Photo2, f.Set, f.Unknown], rows.Select(r => r.Unit).ToArray());
        Assert.Equal([NotCopiedKind.Video, NotCopiedKind.Video, NotCopiedKind.Photo, NotCopiedKind.Photo, NotCopiedKind.Set, NotCopiedKind.Unknown],
                     rows.Select(r => r.Kind).ToArray());
        Assert.True(Row(f, f.Truncated).Truncated);
        Assert.Equal((2, 30_000_000L, (DateOnly?)new DateOnly(2026, 9, 27)), (Row(f, f.Photo1).Files, Row(f, f.Photo1).Bytes, Row(f, f.Photo1).LocalDate));
        Assert.Null(Row(f, f.Unknown).LocalDate);
        Assert.Equal(AuditCategory.AssumedByRule, Row(f, f.Set).Category);
    }

    [Fact]
    public void Day_SelectsOnlyPhotosAndSets()
    {
        var f = Build();

        var day = VerdictDecisions.Day(VerdictDecisions.NotCopied(f.Verdict, f.Plan), new DateOnly(2026, 9, 27));

        Assert.Equal([f.Photo1, f.Photo2, f.Set], day.Select(r => r.Unit).ToArray());
    }

    [Fact]
    public void Check_EnforcesWhatMayBeSelectedTogether()
    {
        var f = Build();
        NotCopiedRow R(ItemId id) => Row(f, id);

        Assert.False(VerdictDecisions.Check(DecisionKind.Dismissed, []).Ok);
        Assert.Equal("Only photos and sets can be recorded as imported",
                     VerdictDecisions.Check(DecisionKind.AssumedImported, [R(f.Photo1), R(f.NewVideo)]).Refusal);
        Assert.Equal("Videos and unknown files are marked one at a time",
                     VerdictDecisions.Check(DecisionKind.Dismissed, [R(f.NewVideo), R(f.Truncated)]).Refusal);
        Assert.False(VerdictDecisions.Check(DecisionKind.Dismissed, [R(f.Unknown), R(f.Photo1)]).Ok);
        Assert.True(VerdictDecisions.Check(DecisionKind.Dismissed, [R(f.Truncated)]).Ok);
        Assert.True(VerdictDecisions.Check(DecisionKind.Dismissed, [R(f.Unknown)]).Ok);
        Assert.True(VerdictDecisions.Check(DecisionKind.AssumedImported, [R(f.Photo1), R(f.Photo2), R(f.Set)]).Ok);
        Assert.True(VerdictDecisions.Check(DecisionKind.Dismissed, [R(f.Photo1), R(f.Photo2), R(f.Set)]).Ok);
    }

    [Fact]
    public void Confirmation_NamesCountsByKindAndGigabytes()
    {
        var f = Build();

        Assert.Equal("Record 2 photos, 1 set (0.08 GB) as already imported?",
                     VerdictDecisions.Confirmation(DecisionKind.AssumedImported, [Row(f, f.Photo1), Row(f, f.Photo2), Row(f, f.Set)]));
        Assert.Equal("Mark 1 video (1.20 GB) as not needed?", VerdictDecisions.Confirmation(DecisionKind.Dismissed, [Row(f, f.NewVideo)]));
    }

    [Fact]
    public void Records_OnePerFile_SetMembersCarryTheSetName()
    {
        var f = Build();

        var records = VerdictDecisions.Records(DecisionKind.AssumedImported, [Row(f, f.Photo1), Row(f, f.Set)], f.Plan, "run-1", "DESKTOP-A", Clock);

        Assert.Equal(
            [("DJI_20260927100000_0010_D.DNG", (string?)null), ("DJI_20260927100000_0010_D.JPG", null), ("PANO_0001.DNG", "001_0087"), ("PANO_0002.DNG", "001_0087")],
            records.Select(r => (r.Name, r.Set)).ToArray());
        Assert.All(records, r =>
        {
            Assert.Equal(("assumedImported", "run-1", "DESKTOP-A", T0.AddHours(3), 1), (r.Kind, r.Run, r.Machine, r.At, r.V));
            Assert.Equal("videos from this day are already in the library", r.Why);
        });
        Assert.Throws<InvalidOperationException>(() =>
            VerdictDecisions.Records(DecisionKind.AssumedImported, [Row(f, f.NewVideo)], f.Plan, "run-1", "DESKTOP-A", Clock));
    }

    [Fact]
    public void DismissingTheLastUnaccountedFiles_MakesTheCardSafe_AndUndoRevokesIt()
    {
        var b = new OffloadPlanBuilder();
        var copied = b.Video("DJI_20260927140000_0001_D.MP4", 100, T0);
        var fresh = b.Video("DJI_20260927140100_0002_D.MP4", 100, T0.AddMinutes(1), included: false);
        b.Group(new NewFolder(ZRel), Zachar, copied, fresh);
        var plan = b.Build();
        var result = Result(OffloadCompiler.Compile(plan, "run-1"), j => new Verified(j, UInt128.One, VerifyMode.Unbuffered));
        var relisted = Unchanged(plan.Base.Scan.Inventory);
        FormatVerdict Audit(LedgerSnapshot l)
            => CardAudit.Audit(plan.Base.Scan.Inventory, relisted, OffloadPlanBuilder.Card, plan, result, l, OffloadPlanBuilder.Card);
        var before = Audit(plan.Base.Scan.Ledger);
        Assert.Equal(VerdictLevel.NotSafe, before.Level);

        var made = VerdictDecisions.Records(DecisionKind.Dismissed, [VerdictDecisions.NotCopied(before, plan).Single()], plan, "run-1", "DESKTOP-A", Clock);
        var after = Audit(VerdictDecisions.Apply(plan.Base.Scan.Ledger, made, []));

        Assert.Equal("not needed", Assert.Single(made).Why);
        Assert.Equal(VerdictLevel.Safe, after.Level);
        Assert.Equal("E: · DJI Air 3S · serial 1A2B-3C4D: Safe to format: 1 verified, 1 confirmed by you", after.Headline);

        var revokes = VerdictDecisions.Revokes(made.Select(m => m.Id), "DESKTOP-A", Clock);
        Assert.Equal(made[0].Id, Assert.Single(revokes).Decision);
        var undone = Audit(VerdictDecisions.Apply(plan.Base.Scan.Ledger, made, revokes));
        Assert.Equal(VerdictLevel.NotSafe, undone.Level);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*VerdictDecisionsTests"`
Expected: build FAILS with CS0103 / CS0246 (`VerdictDecisions`, `NotCopiedRow`, `NotCopiedKind` do not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Offload/VerdictDecisions.cs
using System.Collections.Immutable;

namespace UasSort.Core.Offload;

public enum NotCopiedKind { Video, Photo, Set, Unknown }

public sealed record NotCopiedRow(ItemId Unit, NotCopiedKind Kind, bool Truncated, DateOnly? LocalDate, int Files, long Bytes,
                                  AuditCategory Category, string Detail);

public sealed record DecisionCheck(bool Ok, string? Refusal);

/// <summary>Ref §10.5 "Verdict page": the "Not copied" list, the two decisions, their confirmation text, and undo.</summary>
public static class VerdictDecisions
{
    public static ImmutableArray<NotCopiedRow> NotCopied(FormatVerdict verdict, Plan plan)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(plan);
        var units = plan.Base.Scan.Inventory.Units.ToDictionary(u => u.Id);
        var items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var rows = ImmutableArray.CreateBuilder<NotCopiedRow>();
        foreach (var u in verdict.Units.Where(u => u.Worst is AuditCategory.Unaccounted or AuditCategory.AssumedByRule))
        {
            units.TryGetValue(u.Unit, out var unit);
            items.TryGetValue(u.Unit, out var item);
            var kind = unit switch
            {
                VideoUnit => NotCopiedKind.Video,
                PhotoUnit => NotCopiedKind.Photo,
                SetUnit => NotCopiedKind.Set,
                _ => NotCopiedKind.Unknown,
            };
            bool truncated = unit is VideoUnit { HasTrinf: true } || (item?.Flags.HasFlag(ItemFlags.Truncated) ?? false);
            var detail = u.Lines.First(l => l.Category == u.Worst).Detail;
            rows.Add(new NotCopiedRow(u.Unit, kind, truncated, kind == NotCopiedKind.Unknown ? null : item?.Time.LocalDate,
                                      u.Lines.Length, u.Lines.Sum(l => l.Size), u.Worst, detail));
        }
        return rows.ToImmutable();
    }

    public static ImmutableArray<NotCopiedRow> Day(IEnumerable<NotCopiedRow> rows, DateOnly localDate)
        => [.. rows.Where(r => r.Kind is NotCopiedKind.Photo or NotCopiedKind.Set && r.LocalDate == localDate)];

    public static DecisionCheck Check(DecisionKind kind, IReadOnlyList<NotCopiedRow> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.Count == 0) return new DecisionCheck(false, "Nothing is selected");
        if (kind == DecisionKind.AssumedImported && selected.Any(r => r.Kind is not (NotCopiedKind.Photo or NotCopiedKind.Set)))
            return new DecisionCheck(false, "Only photos and sets can be recorded as imported");
        if (selected.Count > 1 && selected.Any(r => r.Kind is NotCopiedKind.Video or NotCopiedKind.Unknown))
            return new DecisionCheck(false, "Videos and unknown files are marked one at a time");
        return new DecisionCheck(true, null);
    }

    public static string Confirmation(DecisionKind kind, IReadOnlyList<NotCopiedRow> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        static string N(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
        var parts = new List<string>();
        void Part(NotCopiedKind k, string one, string many)
        {
            int n = selected.Count(r => r.Kind == k);
            if (n > 0) parts.Add(N(n, one, many));
        }
        Part(NotCopiedKind.Video, "video", "videos");
        Part(NotCopiedKind.Photo, "photo", "photos");
        Part(NotCopiedKind.Set, "set", "sets");
        Part(NotCopiedKind.Unknown, "unknown file", "unknown files");
        var what = $"{string.Join(", ", parts)} ({OffloadPaths.Gb(selected.Sum(r => r.Bytes))})";
        return kind == DecisionKind.AssumedImported ? $"Record {what} as already imported?" : $"Mark {what} as not needed?";
    }

    public static ImmutableArray<DecisionRecord> Records(DecisionKind kind, IReadOnlyList<NotCopiedRow> selected, Plan plan,
                                                         string? runId, string machine, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(clock);
        var check = Check(kind, selected);
        if (!check.Ok) throw new InvalidOperationException(check.Refusal);
        var inventory = plan.Base.Scan.Inventory;
        var units = inventory.Units.ToDictionary(u => u.Id);
        var items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var at = clock.GetUtcNow().UtcDateTime;
        var kindText = kind == DecisionKind.Dismissed ? "dismissed" : "assumedImported";
        var records = ImmutableArray.CreateBuilder<DecisionRecord>();
        foreach (var row in selected)
        {
            units.TryGetValue(row.Unit, out var unit);
            items.TryGetValue(row.Unit, out var item);
            IReadOnlyList<CardEntry> files;
            string? set = null;
            switch (unit)
            {
                case VideoUnit v: files = [v.Mp4]; break;
                case PhotoUnit p: files = p.JpgTwin is { } t ? [p.Primary, t] : [p.Primary]; break;
                case SetUnit s: files = s.Members; set = s.SetName; break;
                default:
                    files = [.. inventory.Entries.Where(e => string.Equals(OffloadPaths.NormRel(e.RelPath), OffloadPaths.NormRel(row.Unit.CardRelPath),
                                                                           StringComparison.OrdinalIgnoreCase))];
                    break;
            }
            var why = kind == DecisionKind.Dismissed ? "not needed"
                    : item?.Newness is ProbablyImported p2 ? p2.Why : "confirmed by you";
            foreach (var f in files)
                records.Add(new DecisionRecord(OffloadRecords.Version, OffloadRecords.NewId(), machine, runId, at, kindText,
                    OffloadPaths.FileName(f.RelPath), f.Size, f.RelPath, item?.Time.CaptureUtc, why, set));
        }
        return records.ToImmutable();
    }

    public static ImmutableArray<RevokeRecord> Revokes(IEnumerable<string> decisionIds, string machine, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(decisionIds);
        ArgumentNullException.ThrowIfNull(clock);
        var at = clock.GetUtcNow().UtcDateTime;
        return [.. decisionIds.Select(id => new RevokeRecord(OffloadRecords.Version, OffloadRecords.NewId(), machine, at, id))];
    }

    public static LedgerSnapshot Apply(LedgerSnapshot ledger, IEnumerable<DecisionRecord> made, IEnumerable<RevokeRecord> revoked)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(made);
        ArgumentNullException.ThrowIfNull(revoked);
        var decisions = ledger.Decisions.ToBuilder();
        foreach (var r in made)
        {
            var key = OffloadPaths.Key(r.Name, r.Size);
            var kind = r.Kind == "dismissed" ? DecisionKind.Dismissed : DecisionKind.AssumedImported;
            decisions[key] = new LedgerDecision(r.Id, key, kind, r.At, r.Machine, r.Set, r.Why);
        }
        var gone = revoked.Select(x => x.Decision).ToHashSet(StringComparer.Ordinal);
        foreach (var kv in decisions.Where(kv => gone.Contains(kv.Value.Id)).ToList()) decisions.Remove(kv.Key);
        return ledger with { Decisions = decisions.ToImmutable() };
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*VerdictDecisionsTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/VerdictDecisions.cs tests/UasSort.Core.Tests/Offload/VerdictDecisionsTests.cs
git commit -m "feat: add verdict-page decisions with confirmation text and undo

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.16: The offload report

**Files:**
- Create: `src/UasSort.Core/Offload/OffloadReportBuilder.cs`
- Test: `tests/UasSort.Core.Tests/Offload/OffloadReportBuilderTests.cs`

**Interfaces:**

```csharp
// Consumes: OffloadReport, ReportLine (Ref §3 "settings, reports"), Plan, OffloadBatch, OffloadResult, CopyOutcome cases, FormatVerdict;
//   VerdictText.Serial (07.14); OffloadPaths.Gb (07.1).
// Produces (defined here):
public static class OffloadReportBuilder
{
    public const int Version = 1;
    // Ref §11 "Reports": settings snapshot, plan summary, one line per job, every audit line, the card diff, verdict, headline, stop.
    // IReportStore.Save (Platform, Part 09) writes it to reports\<yyyyMMdd-HHmmss>-<run8>.json.
    public static OffloadReport Build(Plan plan, OffloadBatch batch, OffloadResult result, FormatVerdict verdict);
    public static ReportLine Line(CopyOutcome outcome);        // Outcome = the case name; Verify = unbuffered|cached|nameSize
    public static string PlanSummary(OffloadBatch batch, Plan plan);   // "1 folder · 2 videos · 1 photo · 1 set · 0.01 GB"
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/OffloadReportBuilderTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Core.Tests.Offload.CardAuditTests;
using static UasSort.Core.Tests.Offload.CommitTailRecordsTests;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class OffloadReportBuilderTests
{
    [Fact]
    public void Report_HasOneLinePerJob_EveryAuditLine_AndTheVerdict()
    {
        var b = new OffloadPlanBuilder();
        var ok = b.Video("DJI_20260927140000_0001_D.MP4", 4_000_000, T0);
        var bad = b.Video("DJI_20260927140100_0002_D.MP4", 3_000_000, T0.AddMinutes(1));
        b.Group(new NewFolder(@"2026\2026-09\2026-09-27 Zachar Bay"), Zachar, ok, bad);
        var photo = b.Photo("DJI_20260927140200_0003_D.DNG", 2_000_000, T0.AddMinutes(2));
        b.Set("001_0087", [("PANO_0001.DNG", 500_000), ("PANO_0002.DNG", 500_000)], T0.AddMinutes(3), SetResolution.Plain);
        b.Skip("DCIM/DJI_001/DJI_20260927140000_0001_D.LRF", 10, "proxy");
        var plan = b.Build();
        var batch = OffloadCompiler.Compile(plan, "run-1");
        var hash = (UInt128)0xABCDEF;
        var result = Result(batch, j => j.Item == bad ? new Failed(j, CopyPhase.Verify, "The copy didn't match the card twice")
                                       : j.Item == photo ? new AlreadyThere(j)
                                       : new Verified(j, hash, VerifyMode.Cached), StopReason.Cancelled);
        var verdict = CardAudit.Audit(plan.Base.Scan.Inventory, Unchanged(plan.Base.Scan.Inventory), OffloadPlanBuilder.Card, plan, result,
                                      plan.Base.Scan.Ledger, OffloadPlanBuilder.Card);

        var report = OffloadReportBuilder.Build(plan, batch, result, verdict);

        Assert.Equal((1, "run-1", StopReason.Cancelled, verdict.Level, verdict.Headline),
                     (report.V, report.RunId, report.Stop, report.Verdict, report.Headline));
        Assert.Equal(plan.Base.Scan.Settings, report.SettingsSnapshot);
        Assert.Equal("1 folder · 2 videos · 1 photo · 1 set · 0.01 GB", report.PlanSummary);
        Assert.Equal(batch.Jobs.Length, report.Files.Length);
        var first = report.Files[0];
        Assert.Equal((batch.Jobs[0].CardRelPath, (string?)batch.Jobs[0].DestPath, "Verified", (CopyPhase?)null, (string?)null,
                      (string?)"00000000000000000000000000abcdef", (string?)"cached"),
                     (first.CardRelPath, first.Dest, first.Outcome, first.Phase, first.Error, first.Xxh128, first.Verify));
        var failed = report.Files[1];
        Assert.Equal(("Failed", (CopyPhase?)CopyPhase.Verify, (string?)"The copy didn't match the card twice"), (failed.Outcome, failed.Phase, failed.Error));
        Assert.Equal(("AlreadyThere", (string?)"nameSize"), (report.Files[2].Outcome, report.Files[2].Verify));
        Assert.Equal(plan.Base.Scan.Inventory.Entries.Length, report.Audit.Length);
        Assert.Equal(verdict.CardChanges, report.CardChanges);
    }

    [Fact]
    public void Lines_DescribeCardChangesAndSwaps()
    {
        var job = new CopyJob(new ItemId("DCIM/DJI_001/a.MP4"), "DCIM/DJI_001/a.MP4", 10, T0, T0, @"C:\V\a.MP4", DestRoot.Video, null, true);

        Assert.Equal("now 11 bytes, modified 2026-09-27 18:00:02Z",
                     OffloadReportBuilder.Line(new ChangedOnCard(job, 11, T0.AddSeconds(2))).Error);
        Assert.Equal("a different card is in the reader (serial DEAD-BEEF)",
                     OffloadReportBuilder.Line(new CardSwapped(job, OffloadPlanBuilder.Card with { VolumeSerial = 0xDEADBEEF })).Error);
        Assert.Equal(("NotStarted", (string?)null), (OffloadReportBuilder.Line(new NotStarted(job)).Outcome, OffloadReportBuilder.Line(new NotStarted(job)).Verify));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*OffloadReportBuilderTests"`
Expected: build FAILS with CS0103 (`OffloadReportBuilder` does not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Offload/OffloadReportBuilder.cs
using System.Globalization;

namespace UasSort.Core.Offload;

/// <summary>Ref §11 "Reports": the per-run report, flattened (no CopyOutcome is serialised; Ref §3 "JSON").</summary>
public static class OffloadReportBuilder
{
    public const int Version = 1;

    public static OffloadReport Build(Plan plan, OffloadBatch batch, OffloadResult result, FormatVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(verdict);
        return new OffloadReport(Version, result.RunId, plan.Base.Scan.Settings, PlanSummary(batch, plan),
            [.. result.Outcomes.Select(Line)], [.. verdict.Units.SelectMany(u => u.Lines)], verdict.CardChanges, verdict.Level,
            verdict.Headline, result.Stop);
    }

    public static ReportLine Line(CopyOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var job = outcome.Job;
        var name = outcome.GetType().Name;
        return outcome switch
        {
            Verified v => new ReportLine(job.CardRelPath, job.DestPath, name, null, null,
                                         v.Hash.ToString("x32", CultureInfo.InvariantCulture), v.Mode == VerifyMode.Cached ? "cached" : "unbuffered"),
            AlreadyThere => new ReportLine(job.CardRelPath, job.DestPath, name, null, null, null, "nameSize"),
            Failed f => new ReportLine(job.CardRelPath, job.DestPath, name, f.Phase, f.Error, null, null),
            ChangedOnCard c => new ReportLine(job.CardRelPath, job.DestPath, name, null,
                string.Create(CultureInfo.InvariantCulture, $"now {c.NowSize} bytes, modified {c.NowMtimeUtc:yyyy-MM-dd HH:mm:ss}Z"), null, null),
            CardSwapped s => new ReportLine(job.CardRelPath, job.DestPath, name, null,
                $"a different card is in the reader (serial {VerdictText.Serial(s.Now)})", null, null),
            ConflictAtRename or Cancelled or NotStarted => new ReportLine(job.CardRelPath, job.DestPath, name, null, null, null, null),
        };
    }

    public static string PlanSummary(OffloadBatch batch, Plan plan)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(plan);
        static string N(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
        var units = plan.Base.Scan.Inventory.Units.ToDictionary(u => u.Id);
        var groups = batch.Jobs.Where(j => j.Group is not null).Select(j => j.Group!.Value).Distinct().Count();
        var videos = batch.Jobs.Count(j => j.Root == DestRoot.Video);
        var photos = batch.Jobs.Where(j => j.Root == DestRoot.Photo && units.GetValueOrDefault(j.Item) is PhotoUnit)
                               .Select(j => j.Item).Distinct().Count();
        var sets = batch.Jobs.Where(j => units.GetValueOrDefault(j.Item) is SetUnit).Select(j => j.Item).Distinct().Count();
        return $"{N(groups, "folder", "folders")} · {N(videos, "video", "videos")} · {N(photos, "photo", "photos")} · "
             + $"{N(sets, "set", "sets")} · {OffloadPaths.Gb(batch.Jobs.Sum(j => j.Size))}";
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*OffloadReportBuilderTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/OffloadReportBuilder.cs tests/UasSort.Core.Tests/Offload/OffloadReportBuilderTests.cs
git commit -m "feat: build the offload report

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.17: `CommitSession` — preflight, Start offload, copy, ledger tail, audit, report

**Files:**
- Create: `src/UasSort.Core/Offload/CommitSession.cs`
- Test: `tests/UasSort.Core.Tests/Offload/CommitSessionTests.cs`

**Interfaces:**

```csharp
// Consumes: OffloadCompiler (07.2–07.3), Preflight (07.5), PreflightAcks/AckKey (07.6), CopyEngine (07.7–07.10), CommitTailRecords (07.11),
//   CardAudit (07.14), OffloadReportBuilder (07.16); ports ICardReader, IDirectoryLister, ILedgerStore, ILedgerWriter, IOffloadLock,
//   IPowerRequest, IThumbnailSource, IReportStore, IVolumeProvider, IFileOps (Ref §4.1).
// Produces (defined here):
public sealed record CommitEnvironment(ICardReader Reader, IDirectoryLister Lister, ILedgerStore Ledger, IOffloadLock Lock,
    IPowerRequest Power, IThumbnailSource Thumbnails, IReportStore Reports, IVolumeProvider Volumes,
    Func<IReadOnlySet<string>, IFileOps> FileOpsForRun /* builds the run's guarded IFileOps from GuardContext.NewFolderDirs */,
    TimeProvider Clock, string Machine, string AppVersion);
public sealed record CommitResult(OffloadBatch Batch, OffloadResult Offload, FormatVerdict Verdict, OffloadReport Report,
                                  string? ReportPath, bool LedgerComplete)
{ public bool FailureFree { get; } }            // Stop null and every outcome Verified or AlreadyThere (the draft may then be deleted)
public sealed class CommitSession : IDisposable
{
    public static CommitSession Begin(Plan plan, CommitEnvironment env, string? runId = null);   // lock + thumbnail pause + compile + preflight
    public Plan Plan { get; }  public OffloadBatch Batch { get; }  public PreflightReport Preflight { get; }
    public ImmutableArray<AckKey> RequiredAcks { get; }
    public Task<CommitResult> StartAsync(IReadOnlySet<AckKey> acknowledged, IProgress<OffloadProgress> progress, CancellationToken ct);
    public void Dispose();                      // Back or Done: releases the lock and the thumbnail pause; deletes nothing
}
```

Order (Ref §4.4 step 6, §10.3 invariants, §10.4): **Begin** takes `Local\uas-sort-offload` (`IOffloadLock.TryAcquire`; null leaves the preflight Blocking with `OffloadLockHeld`), pauses card thumbnails, compiles, builds the run's `IFileOps` and runs `Preflight.Check` (which writes nothing). **StartAsync** refuses unless `PreflightAcks.CanStart`; keeps the PC awake ("Offloading drone media"); `EnsureFolder()` → `SnapshotToBackup(runId)` → `DeleteOwnTemp` for each stale temp preflight listed → `OpenOwn()` (a failure here copies nothing: every job `NotStarted`, stop `LedgerWriteFailed`); runs `CopyEngine`; appends the `cardLeftovers` folder records and the `seen` records (also after Cancel or a stop); re-checks the identity and re-lists the card; `CardAudit.Audit`; appends the `run` record; closes the writer; builds and saves the report (a save failure leaves `ReportPath` null). `LedgerComplete` is false when any ledger write failed.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Offload/CommitSessionTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class CommitSessionTests
{
    internal const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";

    internal sealed class Harness
    {
        public Harness(OffloadRig rig)
        {
            Rig = rig;
            Env = new CommitEnvironment(rig.Reader, rig.Fs, rig.Ledger, rig.Lock, rig.Power, rig.Thumbnails, rig.Reports,
                new FakeVolumeProvider(rig.Volumes), dirs => Files = new FakeFileOps(rig.Fs, dirs), EngineRun.Clock(), FakeLayout.Machine, "0.1.0");
        }

        public OffloadRig Rig { get; }
        public CommitEnvironment Env { get; }
        public FakeFileOps Files { get; private set; } = null!;
        public CommitSession Begin() => CommitSession.Begin(Rig.Plan, Env, "run-1");
    }

    /// <summary>An Append group (existing folder with a stale temp) with one new video, plus an unticked new photo.</summary>
    internal static (Harness H, string Stale, string Folder) AppendWithStaleTemp(bool ledgerOnFileSystem = false, Action<OffloadPlanBuilder>? arrange = null)
    {
        var b = new OffloadPlanBuilder();
        var folder = new LibraryFolderRef(b.NewFolderPath(ZRel), new DateOnly(2026, 9, 27), "Zachar Bay");
        var v = b.Video("DJI_20260927160000_0160_D.MP4", 3_000, T0.AddHours(2));
        b.Group(new Append(folder, Confidence.High, "same day as clips already in this folder", null), Zachar, v);
        b.Photo("DJI_20260927160500_0161_D.DNG", 2_000, T0.AddHours(2).AddMinutes(5), included: false);
        arrange?.Invoke(b);
        var rig = new OffloadRig(b).Build(ledgerOnFileSystem: ledgerOnFileSystem);
        var stale = folder.FullPath + @"\DJI_20260927150000_0150_D.MP4.uas-sort.tmp";
        rig.Fs.AddFile(stale, 10, T0, 0x22);
        return (new Harness(rig), stale, folder.FullPath);
    }

    private static IReadOnlySet<AckKey> All(CommitSession s) => s.RequiredAcks.ToHashSet();

    [Fact]
    public void Begin_TakesTheLock_PausesThumbnails_AndChecksWithoutWriting_BackDeletesNothing()
    {
        var (h, stale, _) = AppendWithStaleTemp();
        var guardCalls = h.Rig.Fs.GuardLog.Count;

        var session = h.Begin();

        Assert.Equal((1, 1), (h.Rig.Lock.Holds, h.Rig.Thumbnails.ActivePauses));
        Assert.Equal([stale], session.Preflight.StaleTemps.ToArray());
        Assert.True(session.Preflight.CanStart);
        Assert.Equal(["Check"], h.Rig.Ledger.Calls);
        Assert.Equal(guardCalls, h.Rig.Fs.GuardLog.Count);

        session.Dispose();

        Assert.Equal((0, 0), (h.Rig.Lock.Holds, h.Rig.Thumbnails.ActivePauses));
        Assert.True(h.Rig.Fs.Exists(stale));
        Assert.Equal(guardCalls, h.Rig.Fs.GuardLog.Count);
    }

    [Fact]
    public async Task Start_IsRefused_UntilEveryAcknowledgementIsTicked()
    {
        var medium = new Issue(IssueSeverity.Warning, IssueCode.MediumAppend, "Appending to 'Zachar Bay': different day, 34 mi",
                               new ItemId("DCIM/DJI_001/DJI_20260927160000_0160_D.MP4"), [], true);
        var (h, _, _) = AppendWithStaleTemp(arrange: b => b.Issue(medium));
        using var session = h.Begin();

        Assert.Single(session.RequiredAcks);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.StartAsync(new HashSet<AckKey>(), new ListProgress<OffloadProgress>(), CancellationToken.None));
        var result = await session.StartAsync(All(session), new ListProgress<OffloadProgress>(), CancellationToken.None);
        Assert.IsType<Verified>(result.Offload.Outcomes[0]);
    }

    [Fact]
    public async Task Start_RunsTheSteps_InOrder_AndWritesTheLedgerTail()
    {
        var (h, stale, folder) = AppendWithStaleTemp();
        using var session = h.Begin();

        var result = await session.StartAsync(All(session), new ListProgress<OffloadProgress>(), CancellationToken.None);

        Assert.Equal(["Check", "EnsureFolder", "SnapshotToBackup run-1", "OpenOwn"], h.Rig.Ledger.Calls);
        Assert.False(h.Rig.Fs.Exists(stale));
        var calls = h.Files.Calls;
        Assert.True(calls.IndexOf("DeleteOwnTemp " + stale) < calls.FindIndex(c => c.StartsWith("CreateTemp ", StringComparison.Ordinal)));
        Assert.Equal(["FileRecord", "FolderRecord", "SeenRecord", "RunRecord"], h.Rig.Writer.Records.Select(r => r.GetType().Name).ToArray());
        Assert.Equal("appended", ((FolderRecord)h.Rig.Writer.Records[1]).Source);
        Assert.Equal("unticked", ((SeenRecord)h.Rig.Writer.Records[2]).Why);
        Assert.Equal(result.Verdict.Level.ToString(), ((RunRecord)h.Rig.Writer.Records[3]).Verdict);
        Assert.True(h.Rig.Writer.Disposed);
        Assert.Equal(["Offloading drone media"], h.Rig.Power.Reasons);
        Assert.Equal(0, h.Rig.Power.Active);
        Assert.Equal(VerdictLevel.NotSafe, result.Verdict.Level);           // the unticked new photo stays Unaccounted
        Assert.Same(h.Rig.Reports.Offload.Single(), result.Report);
        Assert.NotNull(result.ReportPath);
        Assert.True(result.LedgerComplete);
        Assert.True(result.FailureFree);
        Assert.True(h.Rig.Fs.Exists(folder + @"\DJI_20260927160000_0160_D.MP4"));
        h.Rig.Fs.AssertNoViolations();
    }

    [Fact]
    public async Task Cancel_StillWritesSeenAndRun()
    {
        var (h, _, _) = AppendWithStaleTemp(arrange: b => b.Photo("DJI_20260927161000_0162_D.DNG", 1_000, T0.AddHours(2).AddMinutes(10)));
        using var session = h.Begin();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await session.StartAsync(All(session), new ListProgress<OffloadProgress>(), cts.Token);

        Assert.Equal(StopReason.Cancelled, result.Offload.Stop);
        Assert.All(result.Offload.Outcomes, o => Assert.IsType<NotStarted>(o));
        var seen = h.Rig.Writer.Records.OfType<SeenRecord>().ToList();
        Assert.Equal(["unticked", "notStarted"], seen.Select(s => s.Why).ToArray());
        Assert.IsType<RunRecord>(h.Rig.Writer.Records[^1]);
        Assert.False(result.FailureFree);
        Assert.Equal(VerdictLevel.NotSafe, result.Verdict.Level);
    }

    [Fact]
    public async Task ALedgerFolderThatCannotBeCreated_CopiesNothing()
    {
        var (h, stale, folder) = AppendWithStaleTemp();
        h.Rig.Ledger.EnsureFolderThrows = true;
        using var session = h.Begin();

        var result = await session.StartAsync(All(session), new ListProgress<OffloadProgress>(), CancellationToken.None);

        Assert.Equal(StopReason.LedgerWriteFailed, result.Offload.Stop);
        Assert.All(result.Offload.Outcomes, o => Assert.IsType<NotStarted>(o));
        Assert.DoesNotContain("OpenOwn", h.Rig.Ledger.Calls);
        Assert.True(h.Rig.Fs.Exists(stale));
        Assert.False(h.Rig.Fs.Exists(folder + @"\DJI_20260927160000_0160_D.MP4"));
        Assert.False(result.LedgerComplete);
        Assert.Single(h.Rig.Reports.Offload);
    }

    [Fact]
    public async Task AnotherWindowHoldingTheLock_BlocksStart()
    {
        var (h, _, _) = AppendWithStaleTemp();
        h.Rig.Lock.HeldElsewhere = true;
        using var session = h.Begin();

        Assert.Contains(session.Preflight.Issues, i => i.Code == IssueCode.OffloadLockHeld && i.Severity == IssueSeverity.Blocking);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.StartAsync(All(session), new ListProgress<OffloadProgress>(), CancellationToken.None));
    }

    [Fact]
    public async Task ACleanOffload_IsSafe_AndFailureFree()
    {
        var b = new OffloadPlanBuilder();
        var v = b.Video("DJI_20260927140000_0123_D.MP4", 5_000, T0);
        b.Group(new NewFolder(ZRel), Zachar, v);
        var h = new Harness(new OffloadRig(b).Build());
        using var session = h.Begin();

        var result = await session.StartAsync(All(session), new ListProgress<OffloadProgress>(), CancellationToken.None);

        Assert.Equal(VerdictLevel.Safe, result.Verdict.Level);
        Assert.Equal("E: · DJI Air 3S · serial 1A2B-3C4D: Safe to format: 1 verified", result.Verdict.Headline);
        Assert.True(result.FailureFree);
        Assert.Equal("created", h.Rig.Writer.Records.OfType<FolderRecord>().Single().Source);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CommitSessionTests"`
Expected: build FAILS with CS0246 (`CommitSession`, `CommitEnvironment` do not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Offload/CommitSession.cs
using System.Collections.Immutable;

namespace UasSort.Core.Offload;

public sealed record CommitEnvironment(ICardReader Reader, IDirectoryLister Lister, ILedgerStore Ledger, IOffloadLock Lock,
    IPowerRequest Power, IThumbnailSource Thumbnails, IReportStore Reports, IVolumeProvider Volumes,
    Func<IReadOnlySet<string>, IFileOps> FileOpsForRun, TimeProvider Clock, string Machine, string AppVersion);

public sealed record CommitResult(OffloadBatch Batch, OffloadResult Offload, FormatVerdict Verdict, OffloadReport Report,
                                  string? ReportPath, bool LedgerComplete)
{
    public bool FailureFree => Offload.Stop is null && Offload.Outcomes.All(o => o is Verified or AlreadyThere);
}

/// <summary>One Commit (Ref §4.4 step 6): preflight at Begin, then Start offload → copy → ledger tail → audit → report.
/// Holds the offload lock and the thumbnail pause from Begin until Dispose.</summary>
public sealed class CommitSession : IDisposable
{
    private readonly CommitEnvironment _env;
    private readonly IFileOps _files;
    private IDisposable? _lock;
    private IDisposable? _pause;
    private bool _started;
    private bool _disposed;

    private CommitSession(Plan plan, CommitEnvironment env, IDisposable? lockHandle, IDisposable pause, OffloadBatch batch, IFileOps files,
                          PreflightReport preflight)
    {
        Plan = plan;
        _env = env;
        _lock = lockHandle;
        _pause = pause;
        Batch = batch;
        _files = files;
        Preflight = preflight;
        RequiredAcks = PreflightAcks.Required(preflight);
    }

    public Plan Plan { get; }
    public OffloadBatch Batch { get; }
    public PreflightReport Preflight { get; }
    public ImmutableArray<AckKey> RequiredAcks { get; }

    public static CommitSession Begin(Plan plan, CommitEnvironment env, string? runId = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(env);
        var lockHandle = env.Lock.TryAcquire();
        var pause = env.Thumbnails.Pause();
        try
        {
            var settings = plan.Base.Scan.Settings;
            var batch = OffloadCompiler.Compile(plan, runId ?? Guid.NewGuid().ToString("N"));
            var files = env.FileOpsForRun(OffloadCompiler.NewFolderDirs(batch, settings.VideoRoot));
            var preflight = global::UasSort.Core.Offload.Preflight.Check(batch, plan, files, env.Lister, env.Reader, env.Ledger,
                                                    lockHandle is null ? env.Lock : HeldLock.Instance, settings);
            return new CommitSession(plan, env, lockHandle, pause, batch, files, preflight);
        }
        catch
        {
            pause.Dispose();
            lockHandle?.Dispose();
            throw;
        }
    }

    public async Task<CommitResult> StartAsync(IReadOnlySet<AckKey> acknowledged, IProgress<OffloadProgress> progress, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(acknowledged);
        ArgumentNullException.ThrowIfNull(progress);
        if (_started) throw new InvalidOperationException("This Commit has already started.");
        if (_lock is null || !PreflightAcks.CanStart(Preflight, acknowledged))
            throw new InvalidOperationException("Start offload needs a clear preflight: no Blocking issue and every acknowledgement ticked.");
        _started = true;

        var env = _env;
        using var awake = env.Power.KeepSystemAwake("Offloading drone media");
        ILedgerWriter? writer = null;
        bool ledgerComplete = true;
        OffloadResult result;
        try
        {
            env.Ledger.EnsureFolder();
            env.Ledger.SnapshotToBackup(Batch.RunId);
            foreach (var stale in Preflight.StaleTemps) DeleteQuietly(stale);
            writer = env.Ledger.OpenOwn();
        }
        catch (Exception e) when (Failures.IsIo(e) || e is UnsafeIoException)
        {
            writer?.Dispose();
            writer = null;
            ledgerComplete = false;
        }

        if (writer is null)
        {
            var now = Now();
            result = new OffloadResult(Batch.RunId, [.. Batch.Jobs.Select(j => (CopyOutcome)new NotStarted(j))], StopReason.LedgerWriteFailed,
                                       now, now, []);
        }
        else
        {
            var engine = new CopyEngine(env.Clock, new CopyEngineOptions(env.Machine, OffloadCompiler.Describe(Plan, Batch), env.Volumes.GetVolumes()));
            result = await engine.RunAsync(Batch, env.Reader, _files, writer, progress, ct).ConfigureAwait(false);
            if (result.Stop == StopReason.LedgerWriteFailed) ledgerComplete = false;
            ledgerComplete &= TryAppendAll(writer,
                [.. CommitTailRecords.CardLeftovers(Batch, env.Machine), .. CommitTailRecords.Seen(Batch, Plan, result, env.Machine, Now())]);
        }

        CardIdentity? identity;
        try { identity = env.Reader.CurrentIdentity(); }
        catch (Exception e) when (Failures.IsIo(e)) { identity = null; }
        ListingResult relisted;
        try { relisted = env.Reader.Relist(); }
        catch (Exception e) when (Failures.IsIo(e)) { relisted = new ListingResult([], [(Plan.Base.Scan.Inventory.Source.Root, 21)]); }
        var verdict = CardAudit.Audit(Plan.Base.Scan.Inventory, relisted, identity, Plan, result, Plan.Base.Scan.Ledger, Batch.Card);

        if (writer is not null)
        {
            ledgerComplete &= TryAppendAll(writer, [CommitTailRecords.Run(Batch, Plan, result, verdict, env.Machine, env.AppVersion)]);
            writer.Dispose();
        }

        var report = OffloadReportBuilder.Build(Plan, Batch, result, verdict);
        string? path;
        try { path = env.Reports.Save(report); }
        catch (Exception e) when (Failures.IsIo(e)) { path = null; }
        return new CommitResult(Batch, result, verdict, report, path, ledgerComplete);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pause?.Dispose();
        _pause = null;
        _lock?.Dispose();
        _lock = null;
    }

    private DateTime Now() => _env.Clock.GetUtcNow().UtcDateTime;

    private void DeleteQuietly(string staleTemp)
    {
        try
        {
            _files.DeleteOwnTemp(staleTemp);
        }
        catch (Exception e) when (Failures.IsIo(e))
        {
            // it stays listed for the next preflight; the copy never writes over a temp (CreateTemp is CreateNew)
        }
    }

    private static bool TryAppendAll(ILedgerWriter writer, IEnumerable<LedgerRecord> records)
    {
        try
        {
            foreach (var r in records) writer.Append(r);
            return true;
        }
        catch (Exception e) when (Failures.IsIo(e) || e is UnsafeIoException)
        {
            return false;
        }
    }

    /// <summary>The lock this session already holds, as Preflight's probe sees it.</summary>
    private sealed class HeldLock : IOffloadLock
    {
        public static readonly HeldLock Instance = new();
        public IDisposable? TryAcquire() => new Nothing();
        private sealed class Nothing : IDisposable { public void Dispose() { } }
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CommitSessionTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Offload/CommitSession.cs tests/UasSort.Core.Tests/Offload/CommitSessionTests.cs
git commit -m "feat: run a Commit from preflight through copy, ledger tail, audit and report

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 07.18: The offload half of the fake-FS tripwire

**Files:**
- Test: `tests/UasSort.Core.Tests/Offload/OffloadTripwireTests.cs`

**Interfaces:**

```csharp
// Consumes: CommitSession (07.17) and its test Harness/AppendWithStaleTemp; FakeFileSystem.GuardLog / AssertNoViolations /
//   CardDeleteViolations / CloudOnlyPlaceholder, FakeLayout.AppDataDir (Part 02); FakeLedgerStore on the file system (Part 06, completed in 07.4);
//   PathRules, LedgerPaths.For / IsLedgerFileName (Part 02); OffloadPaths.TempSuffix (07.1).
// Produces: tests only (Ref §13 "Fake file system and hydration tripwire", offload half; the scan half is Part 06's).
```

The fixture carries `0x401620` on every pre-existing library file (a video in the Append folder, a DNG in the photo root) and leaves the local `.uas-sort\ledger-B.jsonl` plain. Over Begin (preflight) and Start (copy, ledger tail, audit), the only guarded operations allowed are: reads under the card root, this run's own `*.uas-sort.tmp` files, the ledger folder itself (pin) and its top-level `ledger*.jsonl` (read; own file append), and `%LOCALAPPDATA%\uas-sort` (backup). This task's code already exists; the tests pin the invariant for every later change, so the red step is the missing test class.

- [ ] **Step 1: Write the test** (run Step 2's command once before saving the file)

```csharp
// tests/UasSort.Core.Tests/Offload/OffloadTripwireTests.cs
using System.Text;
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class OffloadTripwireTests
{
    private const uint Placeholder = FakeFileSystem.CloudOnlyPlaceholder;
    private const string ZRel = CommitSessionTests.ZRel;

    private sealed record Fixture(CommitSessionTests.Harness H, string Stale, string OldVideo, string OldPhoto, string LedgerFolder);

    private static Fixture Library(bool cloudOnlyLedgerB = false)
    {
        const string oldName = "DJI_20260927150000_0150_D.MP4", photoName = "DJI_20260801100000_0100_D.DNG";
        var (h, stale, folder) = CommitSessionTests.AppendWithStaleTemp(ledgerOnFileSystem: true, arrange: b =>
        {
            b.LibraryVideo(ZRel + "\\" + oldName, 90_000, Placeholder);
            b.LibraryPhoto(photoName, 27_000_000, Placeholder);
        });
        var fs = h.Rig.Fs;
        var oldVideo = folder + "\\" + oldName;
        var oldPhoto = h.Rig.B.PhotoRoot + "\\" + photoName;
        fs.AddFile(oldVideo, 90_000, T0.AddDays(-2), Placeholder);
        fs.AddFile(oldPhoto, 27_000_000, T0.AddDays(-50), Placeholder);
        var ledgerFolder = LedgerPaths.For(h.Rig.B.VideoRoot);
        fs.AddFile(ledgerFolder + @"\ledger-B.jsonl", Encoding.UTF8.GetBytes("{\"t\":\"run\"}\n"), T0.AddDays(-1),
                   cloudOnlyLedgerB ? Placeholder : FakeFileSystem.ArchiveAttribute);
        return new Fixture(h, stale, oldVideo, oldPhoto, ledgerFolder);
    }

    private static bool Permitted(FakeGuardCall call, Fixture f)
        => PathRules.IsSameOrUnder(call.Path, CardRoot)
           || call.Path.EndsWith(OffloadPaths.TempSuffix, StringComparison.OrdinalIgnoreCase)
           || PathRules.Equal(call.Path, f.LedgerFolder)
           || (PathRules.Equal(PathRules.Parent(call.Path) ?? "", f.LedgerFolder) && LedgerPaths.IsLedgerFileName(PathRules.FileName(call.Path)))
           || PathRules.IsSameOrUnder(call.Path, FakeLayout.AppDataDir);

    [Fact]
    public async Task PreflightAndOffload_OpenOnlyCardFiles_OwnTemps_AndTheLedgerExemption()
    {
        var f = Library();
        using var session = f.H.Begin();

        var result = await session.StartAsync(session.RequiredAcks.ToHashSet(), new ListProgress<OffloadProgress>(), CancellationToken.None);

        Assert.IsType<Verified>(result.Offload.Outcomes[0]);
        var log = f.H.Rig.Fs.GuardLog;
        Assert.All(log, c => Assert.True(c.Decision == "Allow" && Permitted(c, f), $"{c.Op} {c.Path} → {c.Decision}"));
        Assert.DoesNotContain(log, c => PathRules.Equal(c.Path, f.OldVideo) || PathRules.Equal(c.Path, f.OldPhoto));
        Assert.Contains(log, c => c.Op == IoOp.ReadData && c.Path.EndsWith(@"\ledger-B.jsonl", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(log, c => c.Op == IoOp.AppendOwnLedger
                                  && PathRules.Equal(c.Path, LedgerPaths.OwnFile(f.H.Rig.B.VideoRoot, FakeLayout.Machine)));
        Assert.Empty(f.H.Rig.Fs.CardDeleteViolations);
        f.H.Rig.Fs.AssertNoViolations();
    }

    [Fact]
    public async Task StrayFilesInTheLedgerFolder_AreNeverTouched()
    {
        var f = Library();
        f.H.Rig.Fs.AddFile(f.LedgerFolder + @"\notes.txt", 10, T0);
        f.H.Rig.Fs.AddFile(f.LedgerFolder + @"\sub\ledger-C.jsonl", 10, T0);
        using var session = f.H.Begin();

        await session.StartAsync(session.RequiredAcks.ToHashSet(), new ListProgress<OffloadProgress>(), CancellationToken.None);

        Assert.DoesNotContain(f.H.Rig.Fs.GuardLog, c => c.Path.EndsWith(@"\notes.txt", StringComparison.OrdinalIgnoreCase)
                                                        || c.Path.Contains(@"\.uas-sort\sub", StringComparison.OrdinalIgnoreCase));
        f.H.Rig.Fs.AssertNoViolations();
    }

    [Fact]
    public void ACloudOnlyLedgerFile_IsBlocking_AndNothingIsOpened()
    {
        var f = Library(cloudOnlyLedgerB: true);
        using var session = f.H.Begin();

        Assert.Contains(session.Preflight.Issues, i => i.Code == IssueCode.LedgerCloudOnly && i.Severity == IssueSeverity.Blocking);
        Assert.False(session.Preflight.CanStart);
        Assert.Empty(f.H.Rig.Fs.GuardLog);
        f.H.Rig.Fs.AssertNoViolations();
    }

    [Fact]
    public void PreflightThenBack_DeletesNothing_NotEvenTheStaleTempItListed()
    {
        var f = Library();

        using (var session = f.H.Begin()) Assert.Contains(f.Stale, session.Preflight.StaleTemps);

        Assert.True(f.H.Rig.Fs.Exists(f.Stale));
        Assert.Empty(f.H.Rig.Fs.GuardLog);
        Assert.Empty(f.H.Rig.Fs.CardDeleteViolations);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*OffloadTripwireTests"`
Expected: run once **before** creating the file — MTP reports "No test matches the given testcase filter" and a non-zero exit (the red step); then create the file and continue. These are guard tests over code built in Tasks 07.4–07.17, so after the file exists they are expected to pass; any failure is a real tripwire hit (Step 3).

- [ ] **Step 3: Implement**

No production code: the behaviour comes from Tasks 07.4–07.17. If a test fails, fix the offending component (never the test, never `IoGuardPolicy`): a guarded call outside the permitted set means an offload step opened something it must not.

- [ ] **Step 4: Run it to verify it passes, then the whole suite**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*OffloadTripwireTests"`
Expected: PASS (4 tests).

Run: `dotnet test --solution uas-sort.slnx`
Expected: every test passes, including all Part 07 classes (`OffloadPathsTests`, `OffloadCompilerTests`, `OffloadCompilerMetaTests`, `OffloadRigTests`, `PreflightBlockingTests`, `PreflightActionsTests`, `CopyEngineTests`, `CopyEngineCardFaultTests`, `CopyEngineDestinationFaultTests`, `CopyEngineFlushTests`, `CommitTailRecordsTests`, `CardDiffTests`, `AuditCategorizerTests`, `CardAuditTests`, `VerdictDecisionsTests`, `OffloadReportBuilderTests`, `CommitSessionTests`, `OffloadTripwireTests`). From WSL, run `dotnet build-server shutdown` afterwards.

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Core.Tests/Offload/OffloadTripwireTests.cs
git commit -m "test: add the offload half of the fake-FS hydration and card-delete tripwire

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

## Part 07 — Produces (summary)

All in `src/UasSort.Core/Offload/` (namespace `UasSort.Core.Offload`) unless marked Testing.

| Type | Members later parts use |
|---|---|
| `OffloadPaths` | `Join`, `FileName`, `DirectoryOf`, `TempOf`, `IsTemp`, `VolumeRoot`, `DriveLabel`, `Same`, `IsUnder`, `NormRel`, `NormName`, `Key`, `WithCopyNumber`, `NewFolderDirs`, `Gb`, `TempSuffix`, `MaxTempPathLength` |
| `OffloadCompiler` | `Compile(Plan)`, `Compile(Plan, string runId)`, `Describe(Plan, OffloadBatch)` → a dictionary of `JobMeta` by card path, `NewFolderDirs(OffloadBatch, string videoRoot)` (the run's `GuardContext.NewFolderDirs`) |
| `JobMeta` | `(Kind, CaptureUtc, TimeSource, Point, TzId, LocalDate, Session, Set)` |
| `Preflight` | `Check(OffloadBatch, Plan, IFileOps, IDirectoryLister, ICardReader, ILedgerStore, IOffloadLock, Settings)` → `PreflightReport` (writes nothing) |
| `AckKey`, `PreflightAcks` | `Key(Issue)`, `Required(PreflightReport)`, `CanStart(PreflightReport, acknowledged set of AckKey)` |
| `CopyEngine`, `CopyEngineOptions` | `new CopyEngine(TimeProvider, new CopyEngineOptions(machine, meta, volumes))`, `RunAsync(OffloadBatch, ICardReader, IFileOps, ILedgerWriter, progress, CancellationToken)` (progress reports `OffloadProgress`) → `OffloadResult`; `ChunkBytes` |
| `OffloadRecords` | `File(...)`, `Folder(...)`, `NewId()`, `Version` |
| `CommitTailRecords` | `CardLeftovers`, `Seen`, `Run`, `Card(CardIdentity, model, inventoryHash)` → `RunCard` |
| `CardDiff`, `CardDiffResult`, `CardDiffEntry` | `Compare(scanned CardEntry sequence, ListingResult)`; `Touched(rel)`; `ChangedDetail` = "changed since scan" (prefix of every changed/added/removed audit detail; Part 08's `CleanupRules.ChangedSinceScanDetail` matches it) |
| `AuditCategorizer`, `AuditUnits`, `UnaccountedKind` | `Categorize(CardInventory, Plan, OffloadResult?, LedgerSnapshot, CardDiffResult)` |
| `CardAudit` | `Audit(CardInventory, ListingResult relisted, CardIdentity? now, Plan, OffloadResult?, LedgerSnapshot, CardIdentity? pinned = null)` → `FormatVerdict`; `ChangedPaths(FormatVerdict)` |
| `VerdictText` | `CameraName`, `Serial` ("1A2B-3C4D"), `CardName`, `Headline` |
| `VerdictDecisions`, `NotCopiedRow`, `NotCopiedKind`, `DecisionCheck` | `NotCopied(FormatVerdict, Plan)`, `Day(rows, DateOnly)`, `Check(DecisionKind, rows)`, `Confirmation(DecisionKind, rows)`, `Records(DecisionKind, rows, Plan, runId, machine, TimeProvider)`, `Revokes(ids, machine, TimeProvider)`, `Apply(LedgerSnapshot, made, revoked)` |
| `OffloadReportBuilder` | `Build(Plan, OffloadBatch, OffloadResult, FormatVerdict)` → `OffloadReport`; `Line(CopyOutcome)`; `PlanSummary(OffloadBatch, Plan)` |
| `CommitSession`, `CommitEnvironment`, `CommitResult` | `CommitEnvironment(ICardReader Reader, IDirectoryLister Lister, ILedgerStore Ledger, IOffloadLock Lock, IPowerRequest Power, IThumbnailSource Thumbnails, IReportStore Reports, IVolumeProvider Volumes, FileOpsForRun, TimeProvider Clock, string Machine, string AppVersion)` where `FileOpsForRun` is a `Func` from the run's `NewFolderDirs` set to the run's guarded `IFileOps`; `CommitSession.Begin(Plan, CommitEnvironment, string? runId = null)`; `Plan`, `Batch`, `Preflight`, `RequiredAcks`; `StartAsync(acknowledged AckKey set, progress, CancellationToken)` (progress reports `OffloadProgress`) → `CommitResult(Batch, Offload, Verdict, Report, ReportPath, LedgerComplete) { FailureFree }`; `Dispose()` (Back/Done) |
| Testing, extended in place (namespace `UasSort.Testing`): `FakeFileOps` (Part 02's file) | ctor `FakeFileOps(FakeFileSystem fs, newFolderDirs)` (a string sequence: the run's `NewFolderDirs` verbatim), `Calls` (a string list), `FlushedDestinations` (a read-only list of `(string Dir, int Files)`), `OnTempWrite`, `ThrowOnFinalize`, `FreeBytesOverride`; Part 02's `OwnTemps`, `Renamed`, `Flushed`, `CreatedDirectories`, `FlushToDiskCount` unchanged |
| Testing, completed in place (namespace `UasSort.Testing`): `FakeLedgerStore` (Part 06's file) | `FakeLedgerWriter Writer`, `bool EnsureFolderThrows`, working `EnsureFolder`, `OpenOwn`, `SnapshotToBackup`, `KeepOnDevice`, `CopyInto`; Part 06's ctor `(FakeFileSystem? fs, string videoRoot, string machine, LedgerSnapshot? snapshot = null)`, `Folder`, `Calls`, `StatusOverride`, `CheckThrows`, `Check`, `Load` unchanged |
| Testing, new shared fakes (namespace `UasSort.Testing`, files directly under `tests/UasSort.Testing/`): `FakeLedgerWriter`, `FakeOffloadLock`, `FakePowerRequest`, `FakeThumbnails`, `MemReportStore`, `FakeVolumeProvider`, `ListProgress` (generic) | `FakeLedgerWriter { Records, ThrowWhen, Opened, Disposed }` (writes `LedgerCodec` lines on the fake FS); `FakeOffloadLock { HeldElsewhere, Holds }`; `FakePowerRequest { Active, Reasons }`; `FakeThumbnails { Pauses, ActivePauses, Paused }` (empty bytes from `GetAsync`); `MemReportStore { Offload, Cleanup, Throws }`; `FakeVolumeProvider(volumes)`; `ListProgress { Items }` |
| Testing, scenario builders (namespace `UasSort.Testing.Offload`): `OffloadPlanBuilder`, `OffloadRig`, `OffloadVolumes` | `OffloadPlanBuilder` (Plan straight from Ref §3 records; `Card`, `CardRoot`, `T0`, `Tz`, `Zachar`); `OffloadRig(builder).Build(cardFiles, ledgerOnFileSystem)` → `Fs`, `Plan`, `Batch`, `Reader`, `Files`, `Ledger`, `Writer`, `Lock`, `Power`, `Thumbnails`, `Reports`, `Volumes`, `CardData`; `OffloadVolumes.NtfsC` / `ExFatD` |

**For Part 09 (Platform):** `GuardedFileOps(Settings, appDataDir, machine, IPathFacts, newFolderDirs)` takes the run's `NewFolderDirs` set verbatim (`OffloadCompiler.NewFolderDirs(batch, videoRoot)`, already holding every YYYY, YYYY-MM, event and set folder; canonicalised, OrdinalIgnoreCase, no ancestor expansion — registry decision 27), which the composition passes through `CommitEnvironment.FileOpsForRun`; `IOffloadLock.TryAcquire` (Part 09's `OffloadLock` wrapping Part 01's `NamedMutexLock`, decision 28) returns null only when another holder has `Local\uas-sort-offload` (`CommitSession` passes Preflight a stand-in while it holds the lock, so re-entrancy is not required); `IReportStore.Save(OffloadReport)` serialises the flattened report through `CoreJsonContext`.

**For Part 10 (view models):** Part 10 builds `PreflightVm` (and the Copy and Verdict VMs) on `CommitSession` — there is no `CommitEngine` delegate record (registry decision 10): Begin on entering Commit, `StartAsync` on Start offload, `Dispose` on Back or Done; delete the draft when `CommitResult.FailureFree`. Its tests use `OffloadRig`/`OffloadPlanBuilder` (`UasSort.Testing.Offload`) plus the shared fakes of `UasSort.Testing` (`FakeFileOps`, `FakeLedgerStore`, `FakeLedgerWriter`, `FakeOffloadLock`, `FakePowerRequest`, `FakeThumbnails`, `MemReportStore`, `FakeVolumeProvider`, the generic `ListProgress`). The Verdict page uses `VerdictDecisions` and `UasSort.Core.Offload.NotCopiedKind` for rows, checks, confirmation text, decision and revoke records (decision 24; nothing preselected is the VM's initial state) and re-audits with `CardAudit.Audit(..., VerdictDecisions.Apply(ledger, made, revoked), ...)` after appending the records through `ILedgerStore.OpenOwn()`; `FormatVerdict.SafeRemovalNote` drives [Eject D:].
