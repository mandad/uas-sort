# Part 08 — Card cleanup

Goal: the Core side of Card cleanup — the cleanup volume check, the pure `CleanupPlanner` (per-file eligibility from fresh listings and the fresh ledger, units with companions in delete order, cluster-rounded sizes, Before-date and both Free-space readings, cutoff, shortfall, ticked-for-offload and undecided rows, fingerprint), `CleanupPlan.Confirm` with re-validation, and `CleanupExecutor.RunAsync` (lock, keep-awake, thumbnail pause, ledger preparation, fresh evidence, eraser, per-file identity/stat checks, evidence re-check, deletes, `cardDelete` records, outcomes, closing re-list), plus `CleanupPlanFixtures`, the few members this part adds to Part 02's fakes, and the card-delete tripwire tests. The Windows eraser is Part 09; the Cleanup page and `CleanupVm`/report are Part 10/11.

Ref sections: §10.6 (whole), §4.1 (`ICardReader.Space`, `ICardEraserFactory`, `ICardEraser`, `EraseResult`), §4.3 rules 1b and 2 and the card-delete tripwire, §3 card-cleanup types, §5 companions, §11 `cardDelete` record, §12 cleanup rows, §13 *Card cleanup*. Main spec §7.5.

Depends on: Parts 01–07 — the §3 model incl. every card-cleanup record, enum and outcome case, `CleanupPlan` (18-argument internal constructor) and `ConfirmedCleanupPlan` (Part 02, `src/UasSort.Core/Model/Cleanup.cs`), ports and `IoGuardPolicy`/`GuardContext`/`IoOp.CardDelete`/`CardDeleteViolation`/`UnsafeIoException`, `PathRules`, `LedgerPaths`, `FileKey.NormalizeName`/`FileKey.OfPath`, `Settings`, `LedgerSnapshot`, `Plan`/`PlanBase`/`Item` (Part 02), the fakes `FakeFileSystem`, `FakeFaults`, `FakeLayout`, `FakeCardReader`, `FakeCardEraserFactory` (Part 02), `Zones`, `ZoneNames`, `GeoMath`, `FakePlaceIndex` (Part 04), `LibraryIndex.Build` (Part 05), `FakeLedgerStore` (Part 06), `CardDiffResult.ChangedDetail`, `CommitTailRecords.Card`, `FormatVerdict`, `OffloadResult`, the `CopyOutcome` cases and the shared fakes `FakeLedgerWriter`, `FakeOffloadLock`, `FakePowerRequest`, `FakeThumbnails`, `ListProgress<T>` (Part 07).

**Conventions used by every task in this part**

- Namespaces per `00-interfaces.md`: this part's Core code is in `UasSort.Core.Cleanup` (folder `src/UasSort.Core/Cleanup/`), except `CleanupPlan.Confirm.cs`, which extends Part 02's `CleanupPlan` partial in namespace `UasSort.Core`. The model, ports, guard types and `UnsafeIoException` are in `UasSort.Core` — there is no `UasSort.Core.Model`, `.Ports` or `.Guard` namespace. Every Core namespace (`UasSort.Core`, `.Offload`, `.Ledger`, `.Time`, `.Geo`, …) and `System.Collections.Immutable` come from the fixed `GlobalUsings.Core.cs` (Part 02 Task 02.1), so no file here repeats them. Test files also get `UasSort.Testing` and `Microsoft.Extensions.Time.Testing` from the test project's `GlobalUsings.cs` and `Xunit` from the csproj `<Using Include="Xunit" />`; they add only `using static …`, `System.Reflection` and similar.
- `UnsafeIoException` is constructed with one `string message` argument (the Part 02 type).
- Card-relative paths use `/` (as `ItemId` and the ledger `src` do); `CleanupPaths` (Task 08.1) normalises `\` to `/` before any comparison, case-insensitively.
- `CleanupPlan` and `ConfirmedCleanupPlan` are Part 02's and are never redeclared here: this part calls `CleanupPlan`'s 18-argument internal constructor from `CleanupPlanner.Build` (Task 08.5) and adds only `CleanupPlan.Confirm` (Task 08.9, second partial file). `ConfirmedCleanupPlan`'s derivation of `FilePaths`/`SetFolders` (`PathRules.Join`, case-insensitive) is canonical. `InternalsVisibleTo` for `UasSort.Core.Tests` and `UasSort.Testing` is in `UasSort.Core.csproj` (Part 02 Task 02.4); this part adds no assembly attribute.
- Test fakes are the shared ones in `UasSort.Testing` (one owner each): Part 02's `FakeFileSystem` (card volume, card files, library files, lister, guard log, tripwires), `FakeLayout`, `FakeCardReader`, `FakeCardEraserFactory`/`FakeCardEraser`; Part 06/07's `FakeLedgerStore`/`FakeLedgerWriter`; Part 07's `FakeOffloadLock`, `FakePowerRequest`, `FakeThumbnails`, `ListProgress<T>`; Part 04's `FakePlaceIndex`. This part adds in place only `FakeFileSystem.Touch`, `FakeFaults.OnCardDelete`, `FakeCardEraserFactory.AllDeleted` and `FakeCardEraserFactory.OpenUnchecked` (Task 08.10), plus `CleanupPlanFixtures` (Task 08.9) and `Disposer` (Task 08.10).
- Test project: `tests/UasSort.Core.Tests` (already references `UasSort.Core` and `UasSort.Testing`, and `Microsoft.Extensions.TimeProvider.Testing` for `FakeTimeProvider`). Test command pattern: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*<Name>*"`.

---

### Task 08.1: Cleanup path, key and format helpers

**Files:**
- Create: `src/UasSort.Core/Cleanup/CleanupPaths.cs`
- Create: `src/UasSort.Core/Cleanup/CleanupFormat.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/CleanupHelpersTests.cs`

**Interfaces:**
- Consumes: `FileKey(string NormName, long Size)`, `FileKey.NormalizeName`, `FileKey.OfPath`, `Distance` (Part 02); `Zones.Find`, `ZoneNames.Abbreviation` (Part 04).
- Produces (all defined here):
  - `public static class CleanupPaths { string Rel(string); string Name(string); string Dir(string); bool IsUnder(string relPath, string relDir); string? Full(string cardRoot, string relPath); long Allocated(long size, int clusterBytes); }`
  - `public static class CleanupKeys { string NormName(string fileName); FileKey Key(string relPath, long size); }` — delegates to `FileKey.NormalizeName`/`FileKey.OfPath`, so a cleanup key is the ledger's and the library index's key for the same file.
  - `public static class CleanupFormat { string Gb(long bytes); string Miles(Distance d); DateTime Local(DateTime utc, string tzId); string MonthDay(DateOnly d); string MonthDayYear(DateOnly d); string DayOf(DateTime utc, string tzId); string Abbrev(string tzId, DateTime utc); }` — zones through `Zones.Find`, abbreviations through `ZoneNames.Abbreviation` (the same text every other screen shows).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupHelpersTests.cs
namespace UasSort.Core.Tests.Cleanup;

public class CleanupHelpersTests
{
    [Theory]
    [InlineData("DJI_20260725232655_0117_D (2).MP4", "dji_20260725232655_0117_d.mp4")]
    [InlineData("DJI_20260725232655_0117_D.MP4", "dji_20260725232655_0117_d.mp4")]
    [InlineData("PANO_0001 (12).DNG", "pano_0001.dng")]
    [InlineData("A (x).JPG", "a (x).jpg")]
    public void NormName_lowercases_and_drops_trailing_copy_number(string name, string expected)
        => Assert.Equal(expected, CleanupKeys.NormName(name));

    [Fact]
    public void Key_uses_the_file_name_of_a_card_path()
        => Assert.Equal(new FileKey("dji_20260725232655_0117_d.mp4", 42),
                        CleanupKeys.Key("DCIM/DJI_001/DJI_20260725232655_0117_D.MP4", 42));

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(1L, 131_072L)]
    [InlineData(131_072L, 131_072L)]
    [InlineData(131_073L, 262_144L)]
    [InlineData(5_000_003_584L, 5_000_003_584L)]
    public void Allocated_rounds_up_to_whole_clusters(long size, long expected)
        => Assert.Equal(expected, CleanupPaths.Allocated(size, 131_072));

    [Fact]
    public void Paths_normalise_separators_and_compose_canonical_card_paths()
    {
        Assert.Equal("DCIM/DJI_001/A.MP4", CleanupPaths.Rel(@"\DCIM\DJI_001\A.MP4"));
        Assert.Equal("A.MP4", CleanupPaths.Name("DCIM/DJI_001/A.MP4"));
        Assert.Equal("DCIM/DJI_001", CleanupPaths.Dir(@"DCIM\DJI_001\A.MP4"));
        Assert.True(CleanupPaths.IsUnder("dcim/dji_001/a.mp4", "DCIM/DJI_001"));
        Assert.False(CleanupPaths.IsUnder("DCIM/DJI_0010/a.mp4", "DCIM/DJI_001"));
        Assert.Equal(@"E:\DCIM\DJI_001\A.MP4", CleanupPaths.Full(@"E:\", "DCIM/DJI_001/A.MP4"));
        Assert.Null(CleanupPaths.Full(@"E:\", "DCIM/../Windows/x.dll"));
        Assert.Null(CleanupPaths.Full(@"E:\", "DCIM//x.MP4"));
    }

    [Fact]
    public void Format_uses_decimal_gb_miles_and_zone_abbreviations()
    {
        Assert.Equal("12.4 GB", CleanupFormat.Gb(12_400_000_000));
        Assert.Equal("0.0 GB", CleanupFormat.Gb(1));
        Assert.Equal("<0.1 mi", CleanupFormat.Miles(Distance.FromMiles(0.05)));
        Assert.Equal("0.2 mi", CleanupFormat.Miles(Distance.FromMiles(0.2)));
        Assert.Equal("34 mi", CleanupFormat.Miles(Distance.FromMiles(33.7)));
        var utc = new DateTime(2026, 7, 26, 8, 10, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 7, 26, 0, 10, 0), CleanupFormat.Local(utc, "America/Anchorage"));
        Assert.Equal("AKDT", CleanupFormat.Abbrev("America/Anchorage", utc));
        Assert.Equal("AKST", CleanupFormat.Abbrev("America/Anchorage", new DateTime(2026, 1, 5, 20, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("HST", CleanupFormat.Abbrev("Pacific/Honolulu", utc));
        Assert.Equal("UTC+5:30", CleanupFormat.Abbrev("Asia/Kolkata", utc));
        Assert.Equal("Jul 26", CleanupFormat.MonthDay(new DateOnly(2026, 7, 26)));
        Assert.Equal("Jul 26, 2026", CleanupFormat.MonthDayYear(new DateOnly(2026, 7, 26)));
        Assert.Equal("Sep 27", CleanupFormat.DayOf(new DateTime(2026, 9, 27, 21, 7, 2, DateTimeKind.Utc), "America/Anchorage"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupHelpersTests"`
Expected: build fails with `CS0246: The type or namespace name 'CleanupKeys' could not be found` (and `CleanupPaths`, `CleanupFormat`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Cleanup/CleanupPaths.cs
namespace UasSort.Core.Cleanup;

/// <summary>Card-relative path helpers for Card cleanup (defined here). Card paths use '/', like ItemId.</summary>
public static class CleanupPaths
{
    public static string Rel(string relPath)
    {
        ArgumentNullException.ThrowIfNull(relPath);
        return relPath.Replace('\\', '/').Trim('/');
    }

    public static string Name(string relPath)
    {
        var r = Rel(relPath);
        var i = r.LastIndexOf('/');
        return i < 0 ? r : r[(i + 1)..];
    }

    public static string Dir(string relPath)
    {
        var r = Rel(relPath);
        var i = r.LastIndexOf('/');
        return i < 0 ? "" : r[..i];
    }

    public static bool IsUnder(string relPath, string relDir)
    {
        var r = Rel(relPath);
        var d = Rel(relDir);
        return d.Length == 0
            || r.Equals(d, StringComparison.OrdinalIgnoreCase)
            || r.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Canonical Windows path of a card file: "&lt;root&gt;\a\b". Null for an empty, "." or ".." segment.</summary>
    public static string? Full(string cardRoot, string relPath)
    {
        ArgumentNullException.ThrowIfNull(cardRoot);
        ArgumentNullException.ThrowIfNull(relPath);
        var root = cardRoot.Replace('/', '\\').TrimEnd('\\');
        var raw = relPath.Replace('\\', '/');
        if (raw.StartsWith('/')) raw = raw[1..];
        var segments = raw.Split('/');
        foreach (var s in segments)
            if (s.Length == 0 || s == "." || s == "..") return null;
        return root + "\\" + string.Join('\\', segments);
    }

    /// <summary>⌈size / cluster⌉ × cluster; 0 for an empty file (Ref §10.6 Order and size).</summary>
    public static long Allocated(long size, int clusterBytes)
    {
        if (size <= 0) return 0;
        if (clusterBytes <= 0) return size;
        return (size + clusterBytes - 1) / clusterBytes * clusterBytes;
    }
}

/// <summary>FileKey for Card cleanup's fresh-listing proof (defined here): Part 02's FileKey rule (Ref §7.1), so the key of a
/// card file equals the ledger's and the library index's key for the same file.</summary>
public static class CleanupKeys
{
    public static string NormName(string fileName) => FileKey.NormalizeName(fileName);

    public static FileKey Key(string relPath, long size) => FileKey.OfPath(relPath, size);
}
```

```csharp
// src/UasSort.Core/Cleanup/CleanupFormat.cs
using System.Globalization;

namespace UasSort.Core.Cleanup;

/// <summary>Formatting used by cleanup reasons and texts (Ref §9.13; defined here). Zones come from Part 04's Zones and the
/// abbreviations from ZoneNames, so cleanup shows the same zone text as every other screen.</summary>
public static class CleanupFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Gb(long bytes) => (bytes / 1e9).ToString("0.0", Inv) + " GB";

    public static string Miles(Distance d)
    {
        var mi = d.Miles;
        if (mi < 0.1) return "<0.1 mi";
        if (mi < 10) return mi.ToString("0.0", Inv) + " mi";
        return Math.Round(mi).ToString("0", Inv) + " mi";
    }

    public static DateTime Local(DateTime utc, string tzId)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zones.Find(tzId));

    public static string MonthDay(DateOnly d) => d.ToString("MMM d", Inv);
    public static string MonthDayYear(DateOnly d) => d.ToString("MMM d, yyyy", Inv);
    public static string DayOf(DateTime utc, string tzId) => Local(utc, tzId).ToString("MMM d", Inv);

    /// <summary>"AKDT", "HST", "UTC+5:30" … (Part 04 ZoneNames.Abbreviation).</summary>
    public static string Abbrev(string tzId, DateTime utc) => ZoneNames.Abbreviation(tzId, utc);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupHelpersTests"`
Expected: PASS (5 tests, 12 cases).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/CleanupPaths.cs src/UasSort.Core/Cleanup/CleanupFormat.cs tests/UasSort.Core.Tests/Cleanup/CleanupHelpersTests.cs
git commit -m "feat: add card cleanup path, key and format helpers

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 08.2: CleanupVolumeCheck

**Files:**
- Create: `src/UasSort.Core/Cleanup/CleanupVolumeCheck.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/CleanupVolumeCheckTests.cs`

**Interfaces:**
- Consumes: `VolumeInfo`, `CardIdentity`, `ListingResult`, `FsEntry`, `Settings` (Part 02), `CleanupPaths` (08.1).
- Produces: `public static class CleanupVolumeCheck { const string NotACard; const string WriteProtected; static string? Refusal(VolumeInfo volume, ListingResult card, Settings settings, string appDataDir); }` — null = passes (Ref §4.2). Checks 2–6 of Ref §10.6; check 1 (browsed source) and the identity half of check 3 are the caller's (the VM holds the `CardSource` and the pinned identity; the Platform factory re-derives everything from Win32).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupVolumeCheckTests.cs
namespace UasSort.Core.Tests.Cleanup;

public class CleanupVolumeCheckTests
{
    private const string AppData = @"C:\Users\u\AppData\Local\uas-sort";
    private static readonly DateTime T = new(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc);

    private static Settings S(string video = @"C:\Lib\UAS Videos", string photo = @"C:\Lib\UAS Videos\Picture Offload")
        => new(1, video, photo, [], 50, 1, StoredClockMode.Zone, "America/New_York", true,
               new MapSettings("streets", "https://s", "https://d", "https://t", ImmutableDictionary<string, string>.Empty),
               new LayoutSettings(380, 0.45), true);

    private static VolumeInfo V(string root = @"E:\", string fs = "exFAT", string bus = "Sd", bool removable = true,
                                bool system = false, bool readOnly = false)
        => new(root, new CardIdentity(0x1A2B3C4D, null, fs, 256_060_514_304), "Removable", true, readOnly,
               fs == "NTFS", bus is "Sd" or "Usb", 12_400_000_000, bus, removable, system);

    private static FsEntry F(string root, string rel, bool dir = false)
        => new(root.TrimEnd('\\') + "\\" + rel, rel, dir, dir ? 0 : 10, T, T, T, dir ? 0x10u : 0x20u);

    private static ListingResult Card(string root = @"E:\", bool misc = true, bool idxOnly = false)
    {
        var e = new List<FsEntry> { F(root, "DCIM", true), F(root, @"DCIM\DJI_001", true), F(root, @"DCIM\DJI_001\DJI_20260927140627_0128_D.MP4") };
        if (misc) { e.Add(F(root, "MISC", true)); e.Add(idxOnly ? F(root, @"MISC\IDX", true) : F(root, @"MISC\FC9113.db")); }
        return new ListingResult([.. e], []);
    }

    [Fact]
    public void Sd_exfat_card_with_misc_index_passes()
        => Assert.Null(CleanupVolumeCheck.Refusal(V(), Card(), S(), AppData));

    [Fact]
    public void Usb_reader_with_removable_media_and_idx_folder_passes()
        => Assert.Null(CleanupVolumeCheck.Refusal(V(bus: "Usb", fs: "FAT32"), Card(idxOnly: true), S(), AppData));

    public static TheoryData<string, VolumeInfo, ListingResult, Settings> Refused() => new()
    {
        { "fixed USB exFAT volume holding a card copy", V(bus: "Usb", removable: false), Card(), S() },
        { "NVMe bus", V(bus: "Nvme", removable: false), Card(), S() },
        { "a non-root folder", V(root: @"E:\backup\card\"), Card(@"E:\backup\card\"), S() },
        { "listing not taken at the volume root", V(), Card(@"E:\backup\card\"), S() },
        { "NTFS", V(fs: "NTFS"), Card(), S() },
        { "system volume", V(system: true), Card(), S() },
        { "volume holds the photo root", V(), Card(), S(photo: @"E:\Photos") },
        { "volume holds the video root", V(), Card(), S(video: @"e:\UAS Videos") },
        { "no MISC index", V(), Card(misc: false), S() },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void Refused_volumes_get_the_backup_drive_tooltip(string why, VolumeInfo v, ListingResult card, Settings s)
    {
        Assert.NotNull(why);
        Assert.Equal("This doesn't look like a drone card (it may be a backup drive)", CleanupVolumeCheck.Refusal(v, card, s, AppData));
    }

    [Fact]
    public void AppData_on_the_volume_is_refused()
        => Assert.Equal(CleanupVolumeCheck.NotACard, CleanupVolumeCheck.Refusal(V(), Card(), S(), @"E:\AppData\uas-sort"));

    [Fact]
    public void Write_protected_card_gets_the_lock_switch_tooltip()
        => Assert.Equal("The card is write-protected (lock switch)", CleanupVolumeCheck.Refusal(V(readOnly: true), Card(), S(), AppData));
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupVolumeCheckTests"`
Expected: build fails with `CS0103: The name 'CleanupVolumeCheck' does not exist in the current context`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Cleanup/CleanupVolumeCheck.cs
using System.Text.RegularExpressions;

namespace UasSort.Core.Cleanup;

/// <summary>Ref §10.6 cleanup volume check, for the [Clean up card…] button. The eraser factory repeats it from Win32.</summary>
public static partial class CleanupVolumeCheck
{
    public const string NotACard = "This doesn't look like a drone card (it may be a backup drive)";
    public const string WriteProtected = "The card is write-protected (lock switch)";

    [GeneratedRegex(@"^[A-Za-z]:\\$", RegexOptions.CultureInvariant)]
    private static partial Regex DriveRoot();

    [GeneratedRegex(@"^MISC/FC[^/]*\.db$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MiscDb();

    public static string? Refusal(VolumeInfo volume, ListingResult card, Settings settings, string appDataDir)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(appDataDir);

        if (volume.IsReadOnlyVolume) return WriteProtected;

        // 2. a volume root, and the card listing was taken at that root
        var root = volume.Root.Replace('/', '\\');
        if (!root.EndsWith('\\')) root += "\\";
        if (!DriveRoot().IsMatch(root)) return NotACard;
        foreach (var e in card.Entries)
        {
            var listedRoot = e.FullPath.Length >= e.RelPath.Length ? e.FullPath[..^e.RelPath.Length] : "";
            if (!listedRoot.Replace('/', '\\').Equals(root, StringComparison.OrdinalIgnoreCase)) return NotACard;
        }

        // 3. exFAT or FAT32
        var fs = volume.Identity.FileSystem;
        if (!fs.Equals("exFAT", StringComparison.OrdinalIgnoreCase) && !fs.Equals("FAT32", StringComparison.OrdinalIgnoreCase))
            return NotACard;

        // 4. SD/MMC bus, or USB with removable media
        var bus = volume.BusType;
        var busOk = bus.Equals("Sd", StringComparison.OrdinalIgnoreCase)
                 || bus.Equals("Mmc", StringComparison.OrdinalIgnoreCase)
                 || (bus.Equals("Usb", StringComparison.OrdinalIgnoreCase) && volume.RemovableMedia);
        if (!busOk) return NotACard;

        // 5. not system/boot/paging; no configured root, previous photo root or app data on it
        if (volume.IsSystemBootOrPaging) return NotACard;
        var configured = new List<string> { settings.VideoRoot, settings.PhotoRoot, appDataDir };
        configured.AddRange(settings.PreviousPhotoRoots);
        foreach (var p in configured)
        {
            var q = p.Replace('/', '\\');
            if (q.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || (q + "\\").Equals(root, StringComparison.OrdinalIgnoreCase)) return NotACard;
        }

        // 6. the drone-written index: MISC\FC*.db or a MISC\IDX folder
        var hasIndex = false;
        foreach (var e in card.Entries)
        {
            var rel = CleanupPaths.Rel(e.RelPath);
            if ((!e.IsDirectory && MiscDb().IsMatch(rel))
                || (e.IsDirectory && rel.Equals("MISC/IDX", StringComparison.OrdinalIgnoreCase)))
            { hasIndex = true; break; }
        }
        return hasIndex ? null : NotACard;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupVolumeCheckTests"`
Expected: PASS (13 cases).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/CleanupVolumeCheck.cs tests/UasSort.Core.Tests/Cleanup/CleanupVolumeCheckTests.cs
git commit -m "feat: add cleanup volume check

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 08.3: Per-file eligibility rules

**Files:**
- Create: `src/UasSort.Core/Cleanup/CleanupRules.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/CleanupRulesTests.cs`

**Interfaces:**
- Consumes: `CardEntry`, `EntryClass`, `ItemKind`, `AuditCategory`, `Newness` cases (`IsNew`, `Imported`, `Decided`, `ProbablyImported`, `Conflict`), `LedgerDecision`, `LedgerFile`, `VerifyKind`, `DecisionKind`, `CleanupEligibility`, `NotInLibraryReason`, `EvidenceSource` (Part 02, Ref §3); `CardDiffResult.ChangedDetail` (Part 07 Task 07.12); `CleanupFormat`, `CleanupPaths` (08.1).
- Produces (defined here):
  - `public sealed record FileFacts(CardEntry Entry, ItemKind UnitKind, bool IsCompanion, bool ChangedSinceScan, bool UnderEnumerationError, bool UnitProbeError, bool UnitTruncated, AuditCategory Category, Newness? Newness, LedgerDecision? Decision, bool Listed, LedgerFile? LedgerRecord, string TzId);`
  - `public sealed record FileVerdict(CleanupEligibility Eligibility, string Reason, NotInLibraryReason? NotInLibrary, EvidenceSource? Source);`
  - `public static class CleanupRules { const string ChangedSinceScanDetail = CardDiffResult.ChangedDetail /* "changed since scan" */; static FileVerdict? Classify(FileFacts f) /* null = a companion that inherits its unit */; static string LooseReason(CardEntry e, bool changedSinceScan, bool underEnumerationError); }`

Rule order is Ref §10.6 rows 2–20 with one deliberate change, listed in the part summary as gap G1: rows 10–11 (`ConfirmedByYou`) are tested **before** row 9 (Truncated), because Ref §13 names "a dismissed truncated clip → NotInLibrary (`Dismissed`)", which row order 9-first can't produce. Both give NotInLibrary, so nothing becomes deletable either way; only the reason text differs.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupRulesTests.cs
namespace UasSort.Core.Tests.Cleanup;

public class CleanupRulesTests
{
    private const string Tz = "America/Anchorage";
    private const string VideoRel = "DCIM/DJI_001/DJI_20260725232655_0117_D.MP4";
    private const string PhotoRel = "DCIM/DJI_001/DJI_20260725232700_0118_D.DNG";
    private static readonly DateTime T = new(2026, 7, 26, 3, 28, 25, DateTimeKind.Utc);
    private static readonly DateTime Sep27 = new(2026, 9, 27, 21, 7, 2, DateTimeKind.Utc);
    private static readonly DateTime Oct4 = new(2026, 10, 4, 20, 0, 0, DateTimeKind.Utc);

    private static CardEntry Entry(string rel, EntryClass cls, uint attrs = 0x20) => new(rel, 1_000_000, T, T, T, attrs, cls, null);

    private static Newness NewnessOf(string n) => n switch
    {
        "new" => new IsNew(NewReason.NoMatch, null),
        "conflict" => new Conflict(@"C:\Lib\UAS Videos\2026\x\DJI_20260725232655_0117_D.MP4", 5),
        "probably" => new ProbablyImported("videos from this day are already in the library"),
        "dismissed" => new Decided(DecisionKind.Dismissed, Oct4, "DESKTOP-A"),
        "assumed" => new Decided(DecisionKind.AssumedImported, Oct4, "DESKTOP-A"),
        _ => new Imported(Evidence.LibraryNameSize, null, "same name and size"),
    };

    private static FileFacts Facts(AuditCategory cat, string newness = "imported", bool photo = false, bool listed = true,
                                   string ledger = "none", bool truncated = false)
    {
        var rel = photo ? PhotoRel : VideoRel;
        var key = CleanupKeys.Key(rel, 1_000_000);
        LedgerDecision? decision = newness switch
        {
            "dismissed" => new LedgerDecision("d1", key, DecisionKind.Dismissed, Oct4, "DESKTOP-A", null, "not needed"),
            "assumed" => new LedgerDecision("d2", key, DecisionKind.AssumedImported, Oct4, "DESKTOP-A", null, "confirmed by you"),
            _ => null,
        };
        LedgerFile? record = ledger switch
        {
            "unbuffered" or "cached" or "nameSize" => new LedgerFile(key, rel, photo ? DestRoot.Photo : DestRoot.Video, @"C:\Lib\x",
                null, ledger == "unbuffered" ? VerifyKind.Unbuffered : ledger == "cached" ? VerifyKind.Cached : VerifyKind.NameSize,
                Sep27, T, null, Tz, new DateOnly(2026, 7, 25), null, null, "DESKTOP-A", "run-1"),
            _ => null,
        };
        return new FileFacts(Entry(rel, photo ? EntryClass.Photo : EntryClass.Video), photo ? ItemKind.Photo : ItemKind.Video,
                             IsCompanion: false, ChangedSinceScan: false, UnderEnumerationError: false, UnitProbeError: false,
                             UnitTruncated: truncated, cat, NewnessOf(newness), decision, listed, record, Tz);
    }

    [Theory]
    [InlineData(AuditCategory.NameSizeMatch, "imported", false, true, "none", false, CleanupEligibility.Evidence, "", "Listed", "same name and size in the library")]
    [InlineData(AuditCategory.InLedger, "imported", false, true, "unbuffered", false, CleanupEligibility.Evidence, "", "Listed", "in the history, verified")]
    [InlineData(AuditCategory.VerifiedThisRun, "imported", false, true, "unbuffered", false, CleanupEligibility.Evidence, "", "Listed", "copied and verified today")]
    [InlineData(AuditCategory.InLedger, "imported", false, false, "unbuffered", false, CleanupEligibility.NotInLibrary, "NoLongerInLibrary", "", "copied on Sep 27, no longer in your library")]
    [InlineData(AuditCategory.NameSizeMatch, "imported", false, false, "none", false, CleanupEligibility.NotInLibrary, "NoLongerInLibrary", "", "no longer in your library")]
    [InlineData(AuditCategory.InLedger, "imported", true, false, "cached", false, CleanupEligibility.Evidence, "", "HistoryOnly", "copied and verified on Sep 27; Lightroom may have moved it")]
    [InlineData(AuditCategory.NameSizeMatch, "imported", true, false, "nameSize", false, CleanupEligibility.NotInLibrary, "NoLongerInLibrary", "", "matched by name and size on Sep 27, no longer in your library")]
    [InlineData(AuditCategory.NameSizeMatch, "imported", true, true, "none", false, CleanupEligibility.Evidence, "", "Listed", "same name and size in the library")]
    [InlineData(AuditCategory.AssumedByRule, "probably", true, false, "none", false, CleanupEligibility.NotInLibrary, "ProbablyImported", "", "probably imported, not proven")]
    [InlineData(AuditCategory.Unaccounted, "new", false, false, "none", false, CleanupEligibility.NotInLibrary, "New", "", "new: not in your library")]
    [InlineData(AuditCategory.Unaccounted, "conflict", false, false, "none", false, CleanupEligibility.NotInLibrary, "Conflict", "", "a different file named DJI_20260725232655_0117_D.MP4 is in the library")]
    [InlineData(AuditCategory.Unaccounted, "imported", false, true, "none", false, CleanupEligibility.Never, "", "", "not proven either way")]
    [InlineData(AuditCategory.SkippedByRule, "imported", true, true, "none", false, CleanupEligibility.Never, "", "", "not proven either way")]
    [InlineData(AuditCategory.NameSizeMatch, "imported", false, true, "none", true, CleanupEligibility.NotInLibrary, "Unfinished", "", "unfinished (a same-size copy is in your library; the drone may still repair the card copy)")]
    [InlineData(AuditCategory.Unaccounted, "new", false, false, "none", true, CleanupEligibility.NotInLibrary, "Unfinished", "", "unfinished recording; the drone may still repair it")]
    [InlineData(AuditCategory.ConfirmedByYou, "dismissed", false, false, "none", false, CleanupEligibility.NotInLibrary, "Dismissed", "", "you marked it not needed on Oct 4")]
    [InlineData(AuditCategory.ConfirmedByYou, "assumed", true, false, "none", false, CleanupEligibility.NotInLibrary, "RecordedAsImported", "", "you recorded it as imported on Oct 4; not verified")]
    [InlineData(AuditCategory.ConfirmedByYou, "dismissed", false, false, "none", true, CleanupEligibility.NotInLibrary, "Dismissed", "", "you marked it not needed on Oct 4")]
    public void Eligibility_table(AuditCategory cat, string newness, bool photo, bool listed, string ledger, bool truncated,
                                  CleanupEligibility expected, string nil, string source, string reason)
    {
        var v = CleanupRules.Classify(Facts(cat, newness, photo, listed, ledger, truncated));
        Assert.NotNull(v);
        Assert.Equal(expected, v.Eligibility);
        Assert.Equal(nil.Length == 0 ? (NotInLibraryReason?)null : Enum.Parse<NotInLibraryReason>(nil), v.NotInLibrary);
        Assert.Equal(source.Length == 0 ? (EvidenceSource?)null : Enum.Parse<EvidenceSource>(source), v.Source);
        Assert.Equal(reason, v.Reason);
    }

    [Fact]
    public void Never_rows_win_over_any_evidence()
    {
        var ok = Facts(AuditCategory.VerifiedThisRun);
        Assert.Equal("changed since the scan", CleanupRules.Classify(ok with { ChangedSinceScan = true })!.Reason);
        Assert.Equal("part of the card couldn't be read", CleanupRules.Classify(ok with { UnderEnumerationError = true })!.Reason);
        Assert.Equal("marked read-only on the card", CleanupRules.Classify(ok with { Entry = Entry(VideoRel, EntryClass.Video, 0x21) })!.Reason);
        Assert.Equal("unknown file", CleanupRules.Classify(ok with { Entry = Entry(VideoRel, EntryClass.Unknown) })!.Reason);
        Assert.Equal("its metadata couldn't be read", CleanupRules.Classify(ok with { UnitProbeError = true })!.Reason);
        Assert.Equal("DJI system file", CleanupRules.Classify(ok with { Entry = Entry("MISC/FC9113.db", EntryClass.Skip) })!.Reason);
        foreach (var f in new[] { ok with { ChangedSinceScan = true }, ok with { UnitProbeError = true } })
            Assert.Equal(CleanupEligibility.Never, CleanupRules.Classify(f)!.Eligibility);
    }

    [Fact]
    public void A_companion_inherits_unless_changed_read_only_or_unreadable()
    {
        var lrf = Facts(AuditCategory.Unaccounted, "new") with
        { Entry = Entry("DCIM/DJI_001/DJI_20260725232655_0117_D.LRF", EntryClass.Skip), IsCompanion = true };
        Assert.Null(CleanupRules.Classify(lrf));
        Assert.Equal(CleanupEligibility.Never, CleanupRules.Classify(lrf with { ChangedSinceScan = true })!.Eligibility);
        Assert.Equal(CleanupEligibility.Never, CleanupRules.Classify(lrf with { UnderEnumerationError = true })!.Eligibility);
        Assert.Equal(CleanupEligibility.Never,
            CleanupRules.Classify(lrf with { Entry = Entry("DCIM/DJI_001/DJI_20260725232655_0117_D.LRF", EntryClass.Skip, 0x21) })!.Eligibility);
    }

    [Theory]
    [InlineData("DCIM/DJI_001/DJI_20260725232655_0117_D.LRF", EntryClass.Skip, "not tied to a clip")]
    [InlineData("DCIM/DJI_001/.DJI_20260725232655_0117_D.MP4.trinf", EntryClass.Skip, "not tied to a clip")]
    [InlineData("MISC/FC9113.db", EntryClass.Skip, "DJI system file")]
    [InlineData("System Volume Information/IndexerVolumeGuid", EntryClass.Skip, "system file")]
    [InlineData("DCIM/DJI_A001/x.MP4", EntryClass.Unknown, "unknown file")]
    public void Loose_files_are_never_deleted_with_a_reason(string rel, EntryClass cls, string reason)
        => Assert.Equal(reason, CleanupRules.LooseReason(Entry(rel, cls), false, false));

    [Fact]
    public void Loose_reason_puts_changed_and_unreadable_first()
    {
        Assert.Equal("changed since the scan", CleanupRules.LooseReason(Entry("MISC/FC9113.db", EntryClass.Skip), true, true));
        Assert.Equal("part of the card couldn't be read", CleanupRules.LooseReason(Entry("MISC/FC9113.db", EntryClass.Skip), false, true));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupRulesTests"`
Expected: build fails with `CS0246: The type or namespace name 'FileFacts' could not be found`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Cleanup/CleanupRules.cs
namespace UasSort.Core.Cleanup;

/// <summary>Everything Ref §10.6 eligibility rows 2–20 look at, for one card file of a unit (defined here).</summary>
public sealed record FileFacts(CardEntry Entry, ItemKind UnitKind, bool IsCompanion, bool ChangedSinceScan,
    bool UnderEnumerationError, bool UnitProbeError, bool UnitTruncated, AuditCategory Category, Newness? Newness,
    LedgerDecision? Decision, bool Listed, LedgerFile? LedgerRecord, string TzId);

/// <summary>A file's eligibility, reason text, not-in-library reason and evidence source (defined here).</summary>
public sealed record FileVerdict(CleanupEligibility Eligibility, string Reason, NotInLibraryReason? NotInLibrary, EvidenceSource? Source);

public static class CleanupRules
{
    /// <summary>The prefix of every AuditLine.Detail CardAudit gives a file added, removed or changed since the scan
    /// (Ref §10.5; Part 07's CardDiffResult.ChangedDetail, "changed since scan").</summary>
    public const string ChangedSinceScanDetail = CardDiffResult.ChangedDetail;
    private const uint ReadOnlyAttribute = 0x1;

    private static FileVerdict Never(string reason) => new(CleanupEligibility.Never, reason, null, null);
    private static FileVerdict Nil(NotInLibraryReason why, string reason) => new(CleanupEligibility.NotInLibrary, reason, why, null);
    private static FileVerdict Proven(EvidenceSource src, string reason) => new(CleanupEligibility.Evidence, reason, null, src);

    public static FileVerdict? Classify(FileFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var e = f.Entry;
        if (f.ChangedSinceScan) return Never("changed since the scan");                                  // row 2
        if (f.UnderEnumerationError) return Never("part of the card couldn't be read");                  // row 3
        if ((e.RawAttributes & ReadOnlyAttribute) != 0) return Never("marked read-only on the card");     // row 4
        if (f.IsCompanion) return null;                                                                   // row 8
        if (e.Class == EntryClass.Unknown) return Never("unknown file");                                  // row 5
        if (f.UnitProbeError) return Never("its metadata couldn't be read");                             // row 6
        if (e.Class == EntryClass.Skip) return Never(SkipReason(e.RelPath));                             // row 7

        if (f.Category == AuditCategory.ConfirmedByYou)                                                   // rows 10–11 (gap G1: before row 9)
        {
            var decided = f.Newness as Decided;
            var kind = f.Decision?.Kind ?? decided?.Kind ?? DecisionKind.Dismissed;
            DateTime? at = f.Decision?.AtUtc ?? decided?.AtUtc;
            var on = at is { } a ? " on " + CleanupFormat.DayOf(a, f.TzId) : "";
            return kind == DecisionKind.Dismissed
                ? Nil(NotInLibraryReason.Dismissed, "you marked it not needed" + on)
                : Nil(NotInLibraryReason.RecordedAsImported, "you recorded it as imported" + on + "; not verified");
        }

        if (f.UnitTruncated)                                                                              // row 9
            return Nil(NotInLibraryReason.Unfinished, f.Listed || f.Category == AuditCategory.NameSizeMatch
                ? "unfinished (a same-size copy is in your library; the drone may still repair the card copy)"
                : "unfinished recording; the drone may still repair it");

        if (f.Category is AuditCategory.VerifiedThisRun or AuditCategory.InLedger or AuditCategory.NameSizeMatch)
        {
            if (f.Listed) return Proven(EvidenceSource.Listed, f.Category switch                          // rows 12, 14
            {
                AuditCategory.VerifiedThisRun => "copied and verified today",
                AuditCategory.InLedger => "in the history, verified",
                _ => "same name and size in the library",
            });
            var day = f.LedgerRecord is { } r ? CleanupFormat.DayOf(r.AtUtc, f.TzId) : null;
            if (f.UnitKind == ItemKind.Video)                                                             // row 13
                return Nil(NotInLibraryReason.NoLongerInLibrary,
                    day is null ? "no longer in your library" : $"copied on {day}, no longer in your library");
            if (f.LedgerRecord is { Verify: VerifyKind.Unbuffered or VerifyKind.Cached })                 // row 15
                return Proven(EvidenceSource.HistoryOnly, $"copied and verified on {day}; Lightroom may have moved it");
            return Nil(NotInLibraryReason.NoLongerInLibrary, day is null                                  // row 16
                ? "matched by name and size, no longer in your library"
                : $"matched by name and size on {day}, no longer in your library");
        }

        if (f.Category == AuditCategory.AssumedByRule)                                                    // row 17
            return Nil(NotInLibraryReason.ProbablyImported, "probably imported, not proven");
        if (f.Category == AuditCategory.Unaccounted && f.Newness is IsNew)                               // row 18
            return Nil(NotInLibraryReason.New, "new: not in your library");
        if (f.Category == AuditCategory.Unaccounted && f.Newness is Conflict)                            // row 19
            return Nil(NotInLibraryReason.Conflict, $"a different file named {CleanupPaths.Name(e.RelPath)} is in the library");
        return Never("not proven either way");                                                            // row 20
    }

    /// <summary>Reason for a card file outside every unit: always Never (rows 2–5, 7, 20).</summary>
    public static string LooseReason(CardEntry e, bool changedSinceScan, bool underEnumerationError)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (changedSinceScan) return "changed since the scan";
        if (underEnumerationError) return "part of the card couldn't be read";
        if ((e.RawAttributes & ReadOnlyAttribute) != 0) return "marked read-only on the card";
        return e.Class switch
        {
            EntryClass.Unknown => "unknown file",
            EntryClass.Skip => SkipReason(e.RelPath),
            _ => "not proven either way",
        };
    }

    private static string SkipReason(string relPath)
    {
        var rel = CleanupPaths.Rel(relPath);
        if (CleanupPaths.IsUnder(rel, "MISC")) return "DJI system file";
        var name = CleanupPaths.Name(rel);
        foreach (var ext in (ReadOnlySpan<string>)[".lrf", ".srt", ".trinf", ".avc1"])
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return "not tied to a clip";
        return "system file";
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupRulesTests"`
Expected: PASS (26 cases).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/CleanupRules.cs tests/UasSort.Core.Tests/Cleanup/CleanupRulesTests.cs
git commit -m "feat: add card cleanup per-file eligibility rules

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 08.4: CleanupPlanner.Candidates — units, companions, delete order, proofs

**Files:**
- Create: `src/UasSort.Core/Cleanup/FreshEvidence.cs`
- Create: `src/UasSort.Core/Cleanup/CleanupPlanner.Candidates.cs`
- Create: `tests/UasSort.Core.Tests/Cleanup/CleanupScenario.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/CleanupCandidatesTests.cs`

**Interfaces:**
- Consumes: `CleanupInputs`, `CleanupCandidate`, `CleanupKept`, `FileProof`, `CardInventory`, `MediaUnit`/`VideoUnit`/`PhotoUnit`/`SetUnit`, `Item`, `Plan`, `FormatVerdict`/`UnitAudit`/`AuditLine`, `OffloadResult` and `Verified`/`AlreadyThere`/`ChangedOnCard`, `LibraryListings`/`RootListing`, `LedgerSnapshot`, `IPlaceIndex`/`PlaceHit`/`PlaceClass`, `LedgerPaths.FolderName` (Part 02, Ref §3/§4); `LibraryIndex.Build` (Part 05, test builder only); `FakeLayout` (Part 02), `FakePlaceIndex`, `GeoMath.MetersPerDegree` (Part 04; tests only); `CleanupRules`, `FileFacts` (08.3); `CleanupPaths`, `CleanupKeys`, `CleanupFormat` (08.1).
- Produces:
  - `public sealed class FreshEvidence { static FreshEvidence From(LibraryListings listings); string? ListedFolder(FileKey key); }` (defined here) — `(NormName, size)` from fresh listings only; entries under `.uas-sort` ignored.
  - `public static partial class CleanupPlanner { static ImmutableArray<CleanupCandidate> Candidates(CleanupInputs inputs); static ImmutableArray<CleanupKept> LooseFiles(CleanupInputs inputs, ImmutableArray<CleanupCandidate> candidates); }` — `Candidates` per Ref §4.2, returned oldest first (`CaptureUtc`, then `ItemId` ordinal). `LooseFiles` (defined here) = every card file outside all candidates (never deletable), as `CleanupKept(null, [relPath], allocated, null, reason)`.
  - Test builder `internal sealed partial class CleanupScenario` and `[Flags] internal enum Comp` (defined here, Core.Tests). Its card, roots, identity and space are `FakeLayout`'s (`E:\`, `FakeLayout.VideoRoot`, `FakeLayout.PhotoRoot`, `FakeLayout.CardId`, `FakeLayout.CardSpace`), so Task 08.10 can put the same scenario on `FakeLayout.NewFileSystem()`.

- [ ] **Step 1: Write the test builder and the failing test**

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupScenario.cs
namespace UasSort.Core.Tests.Cleanup;

[Flags]
internal enum Comp { None = 0, Lrf = 1, Srt = 2, Trinf = 4, Avc1 = 8, CoverSkip = 16, CoverUnknown = 32 }

/// <summary>Builds CleanupInputs (and, in Task 08.10, the fake PC) from a few declarations. The card, roots, identity and
/// space are FakeLayout's: card E:\, 12.4 GB free, cluster 128 KiB.</summary>
internal sealed partial class CleanupScenario
{
    public const string CardRoot = FakeLayout.CardRoot;
    public const string VideoRoot = FakeLayout.VideoRoot;
    public const string PhotoRoot = FakeLayout.PhotoRoot;
    public const string Ak = "America/Anchorage";
    public const string CouncilDir = @"2026\2026-07\2026-07-25 Council Road";
    public static readonly CardIdentity Identity = FakeLayout.CardId;
    public static readonly DateTime ScanUtc = new(2026, 10, 12, 19, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime LedgerAt = new(2026, 9, 27, 21, 7, 2, DateTimeKind.Utc);

    public CardSpace Space { get; set; } = FakeLayout.CardSpace;
    public bool CopyJpgTwin { get; set; } = true;
    public IPlaceIndex? Places { get; set; }
    public List<CardEntry> Entries { get; } = [];
    public List<MediaUnit> Units { get; } = [];
    public List<Item> Items { get; } = [];
    public Dictionary<ItemId, List<AuditLine>> Audit { get; } = [];
    public HashSet<ItemId> Included { get; } = [];
    public List<CopyOutcome> Outcomes { get; } = [];
    public List<FsEntry> VideoListing { get; } = [];
    public List<FsEntry> PhotoListing { get; } = [];
    public Dictionary<FileKey, LedgerFile> LedgerFiles { get; } = [];
    public Dictionary<FileKey, LedgerDecision> Decisions { get; } = [];
    public List<ScanWarning> Warnings { get; } = [];
    private int _n = 100;

    public static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    private static string Stamp(DateTime utc, string tz) => CleanupFormat.Local(utc, tz).ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);

    private static CardEntry Entry(string rel, long size, EntryClass cls, DateTime mtime, uint attrs = 0x20, string? rule = null)
        => new(rel, size, mtime, mtime, mtime, attrs, cls, rule);

    private static FsEntry Lib(string root, string rel, long size)
        => new(root + "\\" + rel, rel, false, size, LedgerAt, LedgerAt, LedgerAt, 0x20);

    private LedgerFile Record(string rel, long size, VerifyKind verify, DestRoot root, DateTime captureUtc, string tz, string? set = null)
        => new(CleanupKeys.Key(rel, size), rel, root, (root == DestRoot.Video ? VideoRoot : PhotoRoot) + "\\" + CleanupPaths.Name(rel),
               null, verify, LedgerAt, captureUtc, null, tz, DateOnly.FromDateTime(CleanupFormat.Local(captureUtc, tz)), null, set,
               "DESKTOP-A", "run-1");

    private static Newness DefaultNewness(AuditCategory c) => c switch
    {
        AuditCategory.Unaccounted => new IsNew(NewReason.NoMatch, null),
        AuditCategory.AssumedByRule => new ProbablyImported("videos from this day are already in the library"),
        _ => new Imported(Evidence.LibraryNameSize, null, "in the library"),
    };

    private void AddItem(MediaUnit unit, ItemKind kind, string name, long bytes, DateTime utc, string tz, Newness newness,
                         ItemFlags flags, string? probeError, SessionKey? session, GeoPoint? gps, Mp4Info? mp4)
    {
        var local = CleanupFormat.Local(utc, tz);
        var raw = new RawItem(unit, kind, name, bytes, utc.AddSeconds(90), local, mp4, null, probeError);
        var time = new ItemTime(utc, TimeSource.Mvhd, tz, TzSource.Gps, DateOnly.FromDateTime(local), local);
        var fix = gps is { } p ? new GpsFix(p, null, 0, GpsSource.DjmdModelTable, null) : null;
        Items.Add(new Item(raw, time, fix, session, flags, newness));
    }

    public ItemId AddVideo(DateTime captureUtc, AuditCategory category = AuditCategory.NameSizeMatch, bool listed = true,
        string tz = Ak, Newness? newness = null, ItemFlags flags = ItemFlags.None, long size = 1_000_000_000,
        string? probeError = null, VerifyKind? ledger = null, SessionKey? session = null, TimeSpan? duration = null,
        GeoPoint? gps = null, Comp companions = Comp.None, uint attributes = 0x20)
    {
        var name = $"DJI_{Stamp(captureUtc, tz)}_{++_n:0000}_D.MP4";
        var stem = name[..^4];
        var rel = "DCIM/DJI_001/" + name;
        var mtime = captureUtc.AddSeconds(90);
        var mp4 = Entry(rel, size, EntryClass.Video, mtime, attributes);
        Entries.Add(mp4);
        var comps = new List<CardEntry>();
        if (companions.HasFlag(Comp.Lrf)) comps.Add(Entry($"DCIM/DJI_001/{stem}.LRF", 20_000_000, EntryClass.Skip, mtime, rule: "proxy"));
        if (companions.HasFlag(Comp.Srt)) comps.Add(Entry($"DCIM/DJI_001/{stem}.SRT", 2_000, EntryClass.Skip, mtime, rule: "captions"));
        if (companions.HasFlag(Comp.Trinf)) comps.Add(Entry($"DCIM/DJI_001/.{name}.trinf", 512, EntryClass.Skip, mtime, 0x22, "system/recovery"));
        if (companions.HasFlag(Comp.Avc1)) comps.Add(Entry($"DCIM/DJI_001/.{name}.avc1", 4_096, EntryClass.Skip, mtime, 0x22, "system/recovery"));
        if (companions.HasFlag(Comp.CoverSkip)) comps.Add(Entry($"DCIM/DJI_001/{stem}.JPG", 300_000, EntryClass.Skip, mtime, rule: "video cover"));
        if (companions.HasFlag(Comp.CoverUnknown)) comps.Add(Entry($"DCIM/DJI_001/{stem}.JPG", 300_000, EntryClass.Unknown, mtime));
        Entries.AddRange(comps);
        var unit = new VideoUnit(new ItemId(rel), mp4, companions.HasFlag(Comp.Trinf));
        Units.Add(unit);
        var info = duration is null ? null
            : new Mp4Info(captureUtc, true, new NoFix(NoFixReason.NoDjmdTrack), null, null, null, null, null, duration);
        AddItem(unit, ItemKind.Video, name, size, captureUtc, tz, newness ?? DefaultNewness(category), flags, probeError, session, gps, info);
        var lines = new List<AuditLine> { new(rel, size, category, "") };
        foreach (var c in comps.Where(c => c.Class == EntryClass.Skip))
            lines.Add(new(c.RelPath, c.Size, AuditCategory.SkippedByRule, c.Rule ?? ""));
        Audit[unit.Id] = lines;
        if (listed) VideoListing.Add(Lib(VideoRoot, CouncilDir + "\\" + name, size));
        if (ledger is { } v) LedgerFiles[CleanupKeys.Key(rel, size)] = Record(rel, size, v, DestRoot.Video, captureUtc, tz);
        return unit.Id;
    }

    public ItemId AddPhoto(DateTime captureUtc, AuditCategory category = AuditCategory.NameSizeMatch, bool listed = true,
        bool twin = false, AuditCategory? twinCategory = null, bool? twinListed = null, VerifyKind? ledger = null,
        string tz = Ak, Newness? newness = null, long size = 27_000_000)
    {
        var name = $"DJI_{Stamp(captureUtc, tz)}_{++_n:0000}_D.DNG";
        var rel = "DCIM/DJI_001/" + name;
        var mtime = captureUtc.AddSeconds(1);
        var primary = Entry(rel, size, EntryClass.Photo, mtime);
        Entries.Add(primary);
        CardEntry? jpg = twin ? Entry($"DCIM/DJI_001/{name[..^4]}.JPG", 9_000_000, EntryClass.PhotoTwin, mtime) : null;
        if (jpg is not null) Entries.Add(jpg);
        var unit = new PhotoUnit(new ItemId(rel), primary, jpg);
        Units.Add(unit);
        AddItem(unit, ItemKind.Photo, name, size, captureUtc, tz, newness ?? DefaultNewness(category), ItemFlags.None, null, null, null, null);
        var lines = new List<AuditLine> { new(rel, size, category, "") };
        if (jpg is not null) lines.Add(new(jpg.RelPath, jpg.Size, twinCategory ?? category, ""));
        Audit[unit.Id] = lines;
        if (listed) PhotoListing.Add(Lib(PhotoRoot, name, size));
        if (jpg is not null && (twinListed ?? listed)) PhotoListing.Add(Lib(PhotoRoot, CleanupPaths.Name(jpg.RelPath), jpg.Size));
        if (ledger is { } v)
        {
            LedgerFiles[CleanupKeys.Key(rel, size)] = Record(rel, size, v, DestRoot.Photo, captureUtc, tz);
            if (jpg is not null) LedgerFiles[CleanupKeys.Key(jpg.RelPath, jpg.Size)] = Record(jpg.RelPath, jpg.Size, v, DestRoot.Photo, captureUtc, tz);
        }
        return unit.Id;
    }

    public ItemId AddSet(string setName, DateTime firstUtc, int members = 3, AuditCategory category = AuditCategory.NameSizeMatch,
        bool listed = true, VerifyKind? ledger = null, string tz = Ak)
    {
        var dir = "DCIM/PANORAMA/" + setName;
        var mtime = firstUtc.AddSeconds(1);
        // added in reverse so the planner's ordinal sort is what puts them in order
        var files = Enumerable.Range(1, members).Reverse()
            .Select(i => Entry($"{dir}/PANO_{i:0000}.DNG", 13_751_808, EntryClass.SetMember, mtime)).ToImmutableArray();
        Entries.AddRange(files);
        var unit = new SetUnit(new ItemId(dir), SetKind.Panorama, setName, files);
        Units.Add(unit);
        AddItem(unit, ItemKind.Set, setName, files.Sum(f => f.Size), firstUtc, tz, DefaultNewness(category), ItemFlags.None, null, null, null, null);
        Audit[unit.Id] = [.. files.Select(f => new AuditLine(f.RelPath, f.Size, category, ""))];
        foreach (var f in files)
        {
            if (listed) PhotoListing.Add(Lib(PhotoRoot, setName + "\\" + CleanupPaths.Name(f.RelPath), f.Size));
            if (ledger is { } v) LedgerFiles[CleanupKeys.Key(f.RelPath, f.Size)] = Record(f.RelPath, f.Size, v, DestRoot.Photo, firstUtc, tz, setName);
        }
        return unit.Id;
    }

    public void AddLoose(string rel, EntryClass cls, long size = 1_000, uint attributes = 0x20)
        => Entries.Add(Entry(rel, size, cls, ScanUtc.AddDays(-30), attributes));

    public void AddDir(string rel) => Entries.Add(Entry(rel, 0, EntryClass.Skip, ScanUtc.AddDays(-30), 0x10));

    public MediaUnit Unit(ItemId id) => Units.Single(u => u.Id == id);

    public CardEntry Primary(ItemId id) => Unit(id) switch
    {
        VideoUnit v => v.Mp4,
        PhotoUnit p => p.Primary,
        SetUnit s => s.Members.OrderBy(m => m.RelPath, StringComparer.Ordinal).First(),
    };

    public void SetCategory(string rel, AuditCategory category, string detail = "")
    {
        foreach (var lines in Audit.Values)
            for (var i = 0; i < lines.Count; i++)
                if (CleanupPaths.Rel(lines[i].CardRelPath) == CleanupPaths.Rel(rel)) lines[i] = lines[i] with { Category = category, Detail = detail };
    }

    public void MarkChanged(string rel) => SetCategory(rel, AuditCategory.Unaccounted, CleanupRules.ChangedSinceScanDetail);

    public void Decide(ItemId id, DecisionKind kind, DateTime atUtc)
    {
        CardEntry[] files = Unit(id) switch
        {
            VideoUnit v => [v.Mp4],
            PhotoUnit p => p.JpgTwin is null ? [p.Primary] : [p.Primary, p.JpgTwin],
            SetUnit s => [.. s.Members],
        };
        foreach (var f in files)
        {
            var key = CleanupKeys.Key(f.RelPath, f.Size);
            Decisions[key] = new LedgerDecision($"dec-{Decisions.Count + 1}", key, kind, atUtc, "DESKTOP-A", null, "by you");
            SetCategory(f.RelPath, AuditCategory.ConfirmedByYou);
        }
        var i = Items.FindIndex(x => x.Raw.Unit.Id == id);
        Items[i] = Items[i] with { Newness = new Decided(kind, atUtc, "DESKTOP-A") };
    }

    public void AddEnumerationError(string relDir) => Warnings.Add(new ScanWarning("EnumerationError", "Access is denied.", relDir, true));

    private CopyJob Job(ItemId id)
    {
        var p = Primary(id);
        return new CopyJob(id, p.RelPath, p.Size, p.MtimeUtc, p.CreationUtc, VideoRoot + "\\" + CleanupPaths.Name(p.RelPath),
                           DestRoot.Video, null, false);
    }

    public bool HasOffload { get; private set; }
    public void OffloadVerified(ItemId id) { HasOffload = true; Outcomes.Add(new Verified(Job(id), UInt128.One, VerifyMode.Unbuffered)); }
    public void OffloadFailed(ItemId id) { HasOffload = true; Outcomes.Add(new Failed(Job(id), CopyPhase.Copy, "read error")); }
    public void OffloadNotStarted(ItemId id) { HasOffload = true; Outcomes.Add(new NotStarted(Job(id))); }
    public void OffloadChanged(ItemId id) { HasOffload = true; var j = Job(id); Outcomes.Add(new ChangedOnCard(j, j.Size + 1, j.CardMtimeUtc.AddMinutes(1))); }

    public Settings Settings() => FakeLayout.Settings() with { CopyJpgTwin = CopyJpgTwin };

    public LedgerSnapshot Ledger() => new(LedgerFiles.ToImmutableDictionary(), ImmutableDictionary<string, ImmutableArray<LedgerSet>>.Empty,
        Decisions.ToImmutableDictionary(), ImmutableDictionary<FileKey, DateTime>.Empty, ImmutableDictionary<string, LedgerFolder>.Empty,
        [], [], [], [], new LedgerFolderStatus(LedgerPaths.For(VideoRoot), LedgerFolderState.Ok, true, false, true, true, [], [], []));

    public LibraryListings Listings() => new(
        new RootListing(VideoRoot, DestRoot.Video, false, true, new ListingResult([.. VideoListing], [])),
        new RootListing(PhotoRoot, DestRoot.Photo, false, true, new ListingResult([.. PhotoListing], [])), []);

    public CardInventory Inventory() => new(new CardSource(CardRoot, Identity, false, false), ScanUtc, "9f3c0a6d12e4b7a1",
        [.. Entries], [.. Units], "FC9113", [.. Warnings]);

    public CleanupInputs Inputs()
    {
        var settings = Settings();
        var clock = new ClockModel(ClockMode.Zone, "America/New_York", [], null, StoredClockMode.Zone, "America/New_York");
        var ledger = Ledger();
        var listings = Listings();
        var inv = Inventory();
        var scan = new ScanResult(inv, [.. Items.Select(i => i.Raw)], LibraryIndex.Build(listings, ledger, clock), ledger, clock, [], settings);
        var summary = new ClockSummary(ClockMode.Zone, "America/New_York", 0, "Drone clock: America/New_York", 0, [], []);
        var pb = new PlanBase(scan, [.. Items], [], ImmutableDictionary<ItemId, SetPlacement>.Empty, summary, null);
        var plan = new Plan(1, pb, new Tuning(), [], [], [.. Included], []);
        var audits = Audit.Select(kv => new UnitAudit(kv.Key, kv.Value.Max(l => l.Category), [.. kv.Value])).ToImmutableArray();
        var verdict = new FormatVerdict(VerdictLevel.Safe, Identity, "E: · DJI Air 3S · serial 1A2B-3C4D",
            ImmutableDictionary<AuditCategory, int>.Empty, 0, 0, audits, [], null);
        OffloadResult? offload = HasOffload ? new OffloadResult("run-offload", [.. Outcomes], null, ScanUtc, ScanUtc, []) : null;
        var volume = new VolumeInfo(CardRoot, Identity, "Removable", true, false, false, true, Space.FreeBytes, "Sd", true, false);
        return new CleanupInputs(inv, plan, verdict, offload, Space, listings, ledger, volume, Places, settings);
    }

    public ImmutableArray<CleanupCandidate> Candidates() => CleanupPlanner.Candidates(Inputs());
    public CleanupCandidate Candidate(ItemId id) => Candidates().Single(c => c.Unit == id);
}
```

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupCandidatesTests.cs
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public class CleanupCandidatesTests
{
    private static readonly DateTime Jul3 = Utc(2026, 7, 3, 20, 0);    // Jul 3 12:00 AKDT

    private static string[] Names(CleanupCandidate c) => [.. c.Files.Select(f => CleanupPaths.Name(f.RelPath))];

    [Fact]
    public void Video_deletes_its_companions_first_and_the_mp4_last()
    {
        var s = new CleanupScenario();
        var id = s.AddVideo(Jul3, companions: Comp.Lrf | Comp.Srt | Comp.Trinf | Comp.Avc1);
        var c = s.Candidate(id);
        const string n = "DJI_20260703120000_0101_D";
        Assert.Equal(new[] { $".{n}.MP4.avc1", $".{n}.MP4.trinf", $"{n}.LRF", $"{n}.SRT", $"{n}.MP4" }, Names(c));
        Assert.Equal(4, c.NeverCopied.Length);
        Assert.Equal(1_020_526_592, c.AllocatedBytes);               // 7630 + 153 + 1 + 1 + 1 clusters of 128 KiB
        Assert.Equal(CleanupEligibility.Evidence, c.Eligibility);
        Assert.Equal(EvidenceSource.Listed, c.Source);
        Assert.Equal("same name and size in the library", c.Reason);
        Assert.Equal(ItemKind.Video, c.Kind);
        Assert.Single(c.Proofs);
    }

    [Fact]
    public void Cover_jpg_is_a_companion_only_when_its_skip_rule_is_on()
    {
        var s = new CleanupScenario();
        var on = s.AddVideo(Jul3, companions: Comp.CoverSkip);
        var off = s.AddVideo(Jul3.AddHours(1), companions: Comp.CoverUnknown);
        var cands = s.Candidates();
        Assert.Contains("DJI_20260703120000_0101_D.JPG", Names(cands.Single(c => c.Unit == on)));
        var offC = cands.Single(c => c.Unit == off);
        Assert.Equal(new[] { "DJI_20260703130000_0102_D.MP4" }, Names(offC));
        Assert.Equal(CleanupEligibility.Evidence, offC.Eligibility);           // the MP4 still goes
        var loose = CleanupPlanner.LooseFiles(s.Inputs(), cands);
        var jpg = Assert.Single(loose, k => k.CardRelPaths[0].EndsWith("0102_D.JPG", StringComparison.Ordinal));
        Assert.Equal("unknown file", jpg.Reason);
        Assert.Null(jpg.Unit);
    }

    [Fact]
    public void Photo_pair_deletes_the_twin_first_and_twin_off_counts_as_never_copied()
    {
        var s = new CleanupScenario();
        var id = s.AddPhoto(Jul3, twin: true);
        var c = s.Candidate(id);
        Assert.Equal(new[] { "DJI_20260703120000_0101_D.JPG", "DJI_20260703120000_0101_D.DNG" }, Names(c));
        Assert.Empty(c.NeverCopied);
        Assert.Equal(2, c.Proofs.Length);

        var off = new CleanupScenario { CopyJpgTwin = false };
        var id2 = off.AddPhoto(Jul3, twin: true, twinCategory: AuditCategory.SkippedByRule, twinListed: false);
        var c2 = off.Candidate(id2);
        Assert.Equal(CleanupEligibility.Evidence, c2.Eligibility);
        Assert.Equal(new[] { "DJI_20260703120000_0101_D.JPG" }, c2.NeverCopied.Select(f => CleanupPaths.Name(f.RelPath)));
        Assert.Single(c2.Proofs);
        Assert.Equal(new[] { "DJI_20260703120000_0101_D.JPG", "DJI_20260703120000_0101_D.DNG" }, Names(c2));
    }

    [Fact]
    public void Dng_in_ledger_with_an_assumed_twin_makes_the_pair_not_in_library()
    {
        var s = new CleanupScenario();
        var id = s.AddPhoto(Jul3, AuditCategory.InLedger, listed: true, twin: true, twinCategory: AuditCategory.AssumedByRule,
                            twinListed: false, ledger: null);
        var c = s.Candidate(id);
        Assert.Equal(CleanupEligibility.NotInLibrary, c.Eligibility);
        Assert.Equal(NotInLibraryReason.ProbablyImported, c.NotInLibrary);
        Assert.Equal(AuditCategory.AssumedByRule, c.Evidence);
        Assert.Null(c.Source);
    }

    [Fact]
    public void Set_members_go_in_ordinal_name_order_and_name_their_folder()
    {
        var s = new CleanupScenario();
        var id = s.AddSet("001_0087", Jul3, members: 3);
        var c = s.Candidate(id);
        Assert.Equal(new[] { "PANO_0001.DNG", "PANO_0002.DNG", "PANO_0003.DNG" }, Names(c));
        Assert.Equal("DCIM/PANORAMA/001_0087", c.SetFolder);
        Assert.Equal(ItemKind.Set, c.Kind);
        Assert.Equal(3, c.Proofs.Length);
    }

    [Fact]
    public void A_companion_never_makes_a_unit_eligible_and_a_changed_one_keeps_the_unit()
    {
        var s = new CleanupScenario();
        var nil = s.AddVideo(Jul3, AuditCategory.Unaccounted, listed: false, companions: Comp.Lrf);
        var changed = s.AddVideo(Jul3.AddHours(1), companions: Comp.Lrf);
        s.MarkChanged("DCIM/DJI_001/DJI_20260703130000_0102_D.LRF");
        s.AddLoose("DCIM/DJI_001/DJI_20260101000000_0999_D.LRF", EntryClass.Skip);   // orphan
        var cands = s.Candidates();
        var n = cands.Single(c => c.Unit == nil);
        Assert.Equal(CleanupEligibility.NotInLibrary, n.Eligibility);
        Assert.Contains("DJI_20260703120000_0101_D.LRF", Names(n));
        var ch = cands.Single(c => c.Unit == changed);
        Assert.Equal(CleanupEligibility.Never, ch.Eligibility);
        Assert.Equal("changed since the scan", ch.Reason);
        Assert.Equal("DJI_20260703130000_0102_D.MP4", Names(ch)[^1]);
        var orphan = Assert.Single(CleanupPlanner.LooseFiles(s.Inputs(), cands));
        Assert.Equal("not tied to a clip", orphan.Reason);
        Assert.DoesNotContain(cands, c => c.Files.Any(f => f.RelPath.EndsWith("0999_D.LRF", StringComparison.Ordinal)));
    }

    [Fact]
    public void Never_units_probe_error_read_only_enumeration_error_and_changed_on_card()
    {
        var s = new CleanupScenario();
        var probe = s.AddVideo(Jul3, probeError: "bad moov");
        var ro = s.AddVideo(Jul3.AddHours(1), attributes: 0x21);
        var changedRun = s.AddVideo(Jul3.AddHours(2));
        s.OffloadChanged(changedRun);
        var cands = s.Candidates();
        Assert.Equal("its metadata couldn't be read", cands.Single(c => c.Unit == probe).Reason);
        Assert.Equal("marked read-only on the card", cands.Single(c => c.Unit == ro).Reason);
        Assert.Equal("changed since the scan", cands.Single(c => c.Unit == changedRun).Reason);
        Assert.All(cands, c => Assert.Equal(CleanupEligibility.Never, c.Eligibility));

        var e = new CleanupScenario();
        var under = e.AddVideo(Jul3);
        e.AddEnumerationError("DCIM/DJI_001");
        Assert.Equal("part of the card couldn't be read", e.Candidate(under).Reason);
    }

    [Fact]
    public void Ticked_for_offload_is_set_only_for_not_in_library_units_that_were_not_copied()
    {
        var s = new CleanupScenario();
        var ticked = s.AddVideo(Jul3, AuditCategory.Unaccounted, listed: false);
        var failed = s.AddVideo(Jul3.AddHours(1), AuditCategory.Unaccounted, listed: false);
        var copied = s.AddVideo(Jul3.AddHours(2), AuditCategory.VerifiedThisRun, listed: true);
        var unticked = s.AddVideo(Jul3.AddHours(3), AuditCategory.Unaccounted, listed: false);
        foreach (var id in new[] { ticked, failed, copied }) s.Included.Add(id);
        s.OffloadFailed(failed);
        s.OffloadVerified(copied);
        var cands = s.Candidates();
        Assert.True(cands.Single(c => c.Unit == ticked).TickedForOffload);
        Assert.True(cands.Single(c => c.Unit == failed).TickedForOffload);
        Assert.Equal(NotInLibraryReason.New, cands.Single(c => c.Unit == failed).NotInLibrary);
        Assert.False(cands.Single(c => c.Unit == copied).TickedForOffload);
        Assert.False(cands.Single(c => c.Unit == unticked).TickedForOffload);
    }

    [Fact]
    public void One_byte_file_counts_a_whole_cluster()
    {
        var s = new CleanupScenario();
        var id = s.AddVideo(Jul3, size: 1);
        Assert.Equal(131_072, s.Candidate(id).AllocatedBytes);
    }

    [Fact]
    public void Proofs_name_the_listed_folder_or_the_verified_history()
    {
        var s = new CleanupScenario();
        var video = s.AddVideo(Jul3, AuditCategory.InLedger, ledger: VerifyKind.Unbuffered);
        var moved = s.AddPhoto(Jul3.AddHours(1), AuditCategory.InLedger, listed: false, ledger: VerifyKind.Cached);
        var cands = s.Candidates();
        var vp = Assert.Single(cands.Single(c => c.Unit == video).Proofs);
        Assert.Equal(VideoRoot + "\\" + CouncilDir, vp.ListedFolder);
        Assert.True(vp.LedgerVerified);
        var m = cands.Single(c => c.Unit == moved);
        Assert.Equal(EvidenceSource.HistoryOnly, m.Source);
        Assert.Null(m.Proofs[0].ListedFolder);
        Assert.True(m.Proofs[0].LedgerVerified);
        Assert.Equal("copied and verified on Sep 27; Lightroom may have moved it", m.Reason);
    }

    [Fact]
    public void Candidate_carries_time_length_place_and_session_oldest_first()
    {
        var anvil = new GeoPoint(64.5627, -165.3696);
        var summit = new GeoPoint(anvil.Lat + Distance.FromMiles(0.2).Meters / GeoMath.MetersPerDegree, anvil.Lon);   // 0.2 mi north
        var session = new SessionKey("1581F6Z", Utc(2026, 7, 26, 7, 40));
        var s = new CleanupScenario
        {
            Places = new FakePlaceIndex(new PlaceHit("Anvil Mountain", summit, PlaceClass.Feature, "MT", 0, Ak, Distance.FromMiles(0.2))),
        };
        var later = s.AddVideo(Utc(2026, 7, 26, 8, 10), session: session);
        var earlier = s.AddVideo(Utc(2026, 7, 26, 7, 50), duration: TimeSpan.FromSeconds(222), gps: anvil, session: session);
        var cands = s.Candidates();
        Assert.Equal(new[] { earlier, later }, cands.Select(c => c.Unit));
        var c = cands[0];
        Assert.Equal(new DateOnly(2026, 7, 25), c.LocalDate);
        Assert.Equal(new DateTime(2026, 7, 25, 23, 50, 0), c.LocalTime);
        Assert.Equal(Ak, c.TzId);
        Assert.Equal(TimeSpan.FromSeconds(222), c.Duration);
        Assert.Equal(anvil, c.Location);
        Assert.Equal("near Anvil Mountain · 0.2 mi", c.PlaceLabel);
        Assert.Equal(session, c.Session);
        Assert.Null(cands[1].PlaceLabel);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupCandidatesTests"`
Expected: build fails with `CS0117: 'CleanupPlanner' does not contain a definition for 'Candidates'` (or `CS0103: The name 'CleanupPlanner' does not exist`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Cleanup/FreshEvidence.cs
namespace UasSort.Core.Cleanup;

/// <summary>(NormName, size) → folder, from FRESH library listings only (Ref §10.6 Preparation 3; defined here).</summary>
public sealed class FreshEvidence
{
    private readonly Dictionary<FileKey, string> _folderByKey;

    private FreshEvidence(Dictionary<FileKey, string> folderByKey) => _folderByKey = folderByKey;

    public static FreshEvidence From(LibraryListings listings)
    {
        ArgumentNullException.ThrowIfNull(listings);
        var map = new Dictionary<FileKey, string>();
        foreach (var root in new[] { listings.Video, listings.Photo }.Concat(listings.PreviousPhoto))
        {
            if (!root.Available) continue;                               // an unavailable root contributes nothing
            foreach (var e in root.Listing.Entries)
            {
                if (e.IsDirectory || InLedgerFolder(e.RelPath)) continue;
                var full = e.FullPath.Replace('/', '\\');
                var i = full.LastIndexOf('\\');
                var name = i < 0 ? full : full[(i + 1)..];
                map.TryAdd(new FileKey(CleanupKeys.NormName(name), e.Size), i < 0 ? "" : full[..i]);
            }
        }
        return new FreshEvidence(map);
    }

    public string? ListedFolder(FileKey key) => _folderByKey.TryGetValue(key, out var f) ? f : null;

    private static bool InLedgerFolder(string rel)
    {
        foreach (var seg in rel.Replace('\\', '/').Split('/'))
            if (seg.Equals(LedgerPaths.FolderName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
```

```csharp
// src/UasSort.Core/Cleanup/CleanupPlanner.Candidates.cs
namespace UasSort.Core.Cleanup;

/// <summary>Ref §10.6: pure; the only caller of CleanupPlan's constructor (Build, Task 08.5).</summary>
public static partial class CleanupPlanner
{
    private const uint DirectoryAttribute = 0x10;

    private static bool IsDirectory(CardEntry e) => (e.RawAttributes & DirectoryAttribute) != 0;

    public static ImmutableArray<CleanupCandidate> Candidates(CleanupInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var ctx = new Context(inputs);
        var result = new List<CleanupCandidate>();
        foreach (var unit in inputs.Inventory.Units)
        {
            if (!ctx.Items.TryGetValue(unit.Id, out var item)) continue;   // no metadata: its files stay in LooseFiles (Never)
            var c = Candidate(ctx, unit, item);
            if (c is not null) result.Add(c);
        }
        return [.. result.OrderBy(c => c.CaptureUtc).ThenBy(c => c.Unit.CardRelPath, StringComparer.Ordinal)];
    }

    public static ImmutableArray<CleanupKept> LooseFiles(CleanupInputs inputs, ImmutableArray<CleanupCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var ctx = new Context(inputs);
        var inUnits = new HashSet<string>(candidates.SelectMany(c => c.Files).Select(f => CleanupPaths.Rel(f.RelPath)),
                                          StringComparer.OrdinalIgnoreCase);
        var cluster = inputs.Space.ClusterBytes;
        return [.. inputs.Inventory.Entries
            .Where(e => !IsDirectory(e) && !inUnits.Contains(CleanupPaths.Rel(e.RelPath)))
            .OrderBy(e => CleanupPaths.Rel(e.RelPath), StringComparer.Ordinal)
            .Select(e => new CleanupKept(null, [CleanupPaths.Rel(e.RelPath)], CleanupPaths.Allocated(e.Size, cluster), null,
                                         CleanupRules.LooseReason(e, ctx.Changed(e.RelPath), ctx.UnderEnumerationError(e.RelPath))))];
    }

    private sealed class Context
    {
        public Context(CleanupInputs inputs)
        {
            Inputs = inputs;
            Fresh = FreshEvidence.From(inputs.FreshListings);
            Items = inputs.Plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
            foreach (var u in inputs.Audit.Units)
                foreach (var l in u.Lines) AuditByPath[CleanupPaths.Rel(l.CardRelPath)] = l;
            if (inputs.Offload is { } off)
                foreach (var o in off.Outcomes)
                {
                    if (o is ChangedOnCard ch) ChangedInRun.Add(CleanupPaths.Rel(ch.Job.CardRelPath));
                    if (o is Verified or AlreadyThere) Copied.Add(o.Job.Item);
                }
            EnumerationErrorDirs = [.. inputs.Inventory.Warnings
                .Where(w => w.ForcesNotSafe && w.RelPath is not null).Select(w => CleanupPaths.Rel(w.RelPath!))];
            FilesByDir = inputs.Inventory.Entries.Where(e => !IsDirectory(e))
                .GroupBy(e => CleanupPaths.Dir(e.RelPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        }

        public CleanupInputs Inputs { get; }
        public FreshEvidence Fresh { get; }
        public Dictionary<ItemId, Item> Items { get; }
        public Dictionary<string, AuditLine> AuditByPath { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ChangedInRun { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<ItemId> Copied { get; } = [];
        public List<string> EnumerationErrorDirs { get; }
        public Dictionary<string, List<CardEntry>> FilesByDir { get; }

        public bool Changed(string relPath)
        {
            var rel = CleanupPaths.Rel(relPath);
            return ChangedInRun.Contains(rel)
                || (AuditByPath.TryGetValue(rel, out var l)
                    && l.Detail.StartsWith(CleanupRules.ChangedSinceScanDetail, StringComparison.OrdinalIgnoreCase));
        }

        public bool UnderEnumerationError(string relPath)
            => EnumerationErrorDirs.Exists(d => CleanupPaths.IsUnder(relPath, d));
    }

    private static CleanupCandidate? Candidate(Context ctx, MediaUnit unit, Item item)
    {
        var inputs = ctx.Inputs;
        var (kind, primaries, companions, deleteOrder) = unit switch
        {
            VideoUnit v => VideoFiles(ctx, v),
            PhotoUnit p => PhotoFiles(p, inputs.Settings.CopyJpgTwin),
            SetUnit s => SetFiles(s),
        };
        if (primaries.Count == 0) return null;

        var tz = item.Time.TzId;
        var truncated = kind == ItemKind.Video && item.Flags.HasFlag(ItemFlags.Truncated);
        var probeError = item.Raw.ProbeError is not null || item.Flags.HasFlag(ItemFlags.ProbeFailed);

        FileFacts Facts(CardEntry e, bool companion)
        {
            var rel = CleanupPaths.Rel(e.RelPath);
            var category = ctx.AuditByPath.TryGetValue(rel, out var line) ? line.Category : AuditCategory.Unaccounted;
            var key = CleanupKeys.Key(rel, e.Size);
            inputs.FreshLedger.Decisions.TryGetValue(key, out var decision);
            inputs.FreshLedger.Files.TryGetValue(key, out var record);
            return new FileFacts(e, kind, companion, ctx.Changed(rel), ctx.UnderEnumerationError(rel), probeError, truncated,
                                 category, item.Newness, decision, ctx.Fresh.ListedFolder(key) is not null, record, tz);
        }

        var primaryVerdicts = primaries.Select(p => (File: p, Facts: Facts(p, false), Verdict: CleanupRules.Classify(Facts(p, false))!)).ToList();
        var companionNever = companions.Select(c => CleanupRules.Classify(Facts(c, true))).FirstOrDefault(v => v is not null);

        var worst = primaryVerdicts.Max(x => x.Verdict.Eligibility);
        FileVerdict decisive;
        if (worst != CleanupEligibility.Never && companionNever is not null) { worst = CleanupEligibility.Never; decisive = companionNever; }
        else decisive = primaryVerdicts.First(x => x.Verdict.Eligibility == worst).Verdict;

        var evidence = primaryVerdicts.Max(x => x.Facts.Category);
        EvidenceSource? source = worst == CleanupEligibility.Evidence
            ? primaryVerdicts.Exists(x => x.Verdict.Source == EvidenceSource.HistoryOnly) ? EvidenceSource.HistoryOnly : EvidenceSource.Listed
            : null;
        var proofs = primaryVerdicts.Select(x =>
        {
            var key = CleanupKeys.Key(x.File.RelPath, x.File.Size);
            return new FileProof(CleanupPaths.Rel(x.File.RelPath), key, x.Facts.Category, ctx.Fresh.ListedFolder(key),
                                 x.Facts.LedgerRecord is { Verify: VerifyKind.Unbuffered or VerifyKind.Cached },
                                 x.Facts.Category == AuditCategory.ConfirmedByYou ? x.Facts.Decision?.Id : null);
        }).ToImmutableArray();

        var ticked = worst == CleanupEligibility.NotInLibrary && inputs.Plan.Included.Contains(unit.Id) && !ctx.Copied.Contains(unit.Id);
        var cluster = inputs.Space.ClusterBytes;
        var point = item.Gps?.Point;
        return new CleanupCandidate(unit.Id, [.. deleteOrder], deleteOrder.Sum(f => CleanupPaths.Allocated(f.Size, cluster)),
            item.Time.CaptureUtc, item.Time.LocalDate, tz, worst, evidence, decisive.Reason, item.Raw.Mp4?.Duration, point,
            Place(inputs.Places, point), kind, item.Time.LocalTime,
            worst == CleanupEligibility.NotInLibrary ? decisive.NotInLibrary : null,
            unit is SetUnit ? CleanupPaths.Rel(unit.Id.CardRelPath) : null, item.Session, proofs, source, ticked, [.. companions]);
    }

    private static (ItemKind, List<CardEntry>, List<CardEntry>, List<CardEntry>) VideoFiles(Context ctx, VideoUnit v)
    {
        var companions = VideoCompanions(ctx, v.Mp4);
        return (ItemKind.Video, [v.Mp4], companions, [.. companions, v.Mp4]);   // companions first, the MP4 last
    }

    private static List<CardEntry> VideoCompanions(Context ctx, CardEntry mp4)
    {
        var name = CleanupPaths.Name(mp4.RelPath);
        var dot = name.LastIndexOf('.');
        var stem = dot < 0 ? name : name[..dot];
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { stem + ".LRF", stem + ".SRT", "." + name + ".trinf", "." + name + ".avc1", "." + stem + ".avc1", stem + ".JPG" };
        if (!ctx.FilesByDir.TryGetValue(CleanupPaths.Dir(mp4.RelPath), out var siblings)) return [];
        // Skip class only: the cover JPG is a companion once the "video cover" Skip rule is on; while Unknown it stays loose (Never)
        return [.. siblings.Where(e => e.Class == EntryClass.Skip && wanted.Contains(CleanupPaths.Name(e.RelPath)))
                           .OrderBy(e => CleanupPaths.Name(e.RelPath), StringComparer.Ordinal)];
    }

    private static (ItemKind, List<CardEntry>, List<CardEntry>, List<CardEntry>) PhotoFiles(PhotoUnit p, bool copyJpgTwin)
    {
        if (p.JpgTwin is not { } twin) return (ItemKind.Photo, [p.Primary], [], [p.Primary]);
        return copyJpgTwin
            ? (ItemKind.Photo, [p.Primary, twin], [], [twin, p.Primary])
            : (ItemKind.Photo, [p.Primary], [twin], [twin, p.Primary]);           // twin never copied: a companion (row 8)
    }

    private static (ItemKind, List<CardEntry>, List<CardEntry>, List<CardEntry>) SetFiles(SetUnit s)
    {
        var members = s.Members.OrderBy(m => CleanupPaths.Name(m.RelPath), StringComparer.Ordinal).ToList();
        return (ItemKind.Set, members, [], members);
    }

    private static string? Place(IPlaceIndex? places, GeoPoint? point)
    {
        if (places is null || point is not { } p) return null;
        var feature = places.Near(p, Distance.FromMiles(1.5), PlaceClass.Feature, 1);
        if (feature.Count > 0) return Label(feature[0]);
        var populated = places.Near(p, Distance.FromMiles(3), PlaceClass.Populated, 1);
        if (populated.Count > 0) return Label(populated[0]);
        PlaceHit? town = null;
        foreach (var h in places.Near(p, Distance.FromMiles(30), PlaceClass.Populated, 50))
            if (h.Population >= 1000 && (town is null || h.Away.Meters < town.Away.Meters)) town = h;
        return town is null ? null : Label(town);
    }

    private static string Label(PlaceHit h) => $"near {h.Name} · {CleanupFormat.Miles(h.Away)}";
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupCandidatesTests"`
Expected: PASS (11 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/FreshEvidence.cs src/UasSort.Core/Cleanup/CleanupPlanner.Candidates.cs tests/UasSort.Core.Tests/Cleanup/CleanupScenario.cs tests/UasSort.Core.Tests/Cleanup/CleanupCandidatesTests.cs
git commit -m "feat: build card cleanup candidates with companions and proofs

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 08.5: Fingerprint and Build (Before date)

**Files:**
- Create: `src/UasSort.Core/Cleanup/CleanupFingerprint.cs`
- Create: `src/UasSort.Core/Cleanup/CleanupPlanner.Build.cs`
- Create: `tests/UasSort.Core.Tests/Cleanup/CleanupScenario.Build.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/CleanupBuildBeforeDateTests.cs`

**Interfaces:**
- Consumes: `CleanupRequest`, `CleanupMode`, `CleanupRows`, `CleanupKept`, `CleanupCutoff`, `FreeSpaceShortfall`, `CardSpace`, `CardIdentity`, `SessionKey.SameSession` (Part 02, Ref §3); Part 02's `CleanupPlan` (namespace `UasSort.Core`, `src/UasSort.Core/Model/Cleanup.cs`) through its 18-argument internal constructor `CleanupPlan(string planId, CardIdentity card, string cardRoot, string inventoryHash, string? cameraModel, CleanupRequest request, CardSpace spaceBefore, ImmutableArray<CleanupCandidate> delete, ImmutableArray<CleanupCandidate> notInLibraryInScope, CleanupRows rows, ImmutableHashSet<ItemId> undecided, ImmutableArray<CleanupKept> notDeletable, CleanupCutoff cutoff, int fileCount, long allocatedBytes, long expectedFreeAfter, FreeSpaceShortfall? shortfall, string fingerprint)` and its properties (incl. `InventoryHash`, `CameraModel` for the `cardDelete` record's `RunCard`); `CleanupPlanner.Candidates`/`LooseFiles` (08.4). This part never redeclares `CleanupPlan`; `Confirm` arrives in Task 08.9 (second partial file).
- Produces:
  - `public static class CleanupFingerprint { static string Compute(CleanupRequest request, CardSpace spaceBefore, IEnumerable<CleanupCandidate> delete); }` (defined here).
  - `public static partial class CleanupPlanner { static CleanupPlan Build(CleanupInputs inputs, ImmutableArray<CleanupCandidate> candidates, CleanupRequest request, CleanupRows rows, IReadOnlySet<ItemId> firstShown); }` (Ref §4.2) — the only caller of the constructor; it passes `inputs.Inventory.InventoryHash`, `inputs.Inventory.CameraModel` and the computed `fileCount` (Σ files of `Delete`), `allocatedBytes` (Σ `AllocatedBytes`) and `expectedFreeAfter` (`min(TotalBytes, FreeBytes + allocatedBytes)`). Row semantics: a NotInLibrary unit in `rows.Keep`/`rows.Delete` has that decision; one in neither but in `firstShown` has its initial state (Keep when `TickedForOffload`, else Delete); anything else is undecided. Passing every candidate's `ItemId` as `firstShown` gives "the list as first shown".
  - `NotDeletable` = in-range units the plan doesn't delete (reason per unit) followed by `LooseFiles` (`Unit` null: the "Never touched" line).
  - Test helpers on `CleanupScenario`: `Build(...)`, `NoRows`, `Before(...)`, `HaveFree(...)`, `FreeUp(...)`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupScenario.Build.cs
namespace UasSort.Core.Tests.Cleanup;

internal sealed partial class CleanupScenario
{
    public static CleanupRows NoRows => new([], []);

    public static CleanupRequest Before(int y, int m, int d, bool include = false)
        => new(CleanupMode.BeforeDate, new DateOnly(y, m, d), null, include);

    public static CleanupRequest HaveFree(long bytes, bool include = false)
        => new(CleanupMode.FreeSpace, null, new FreeSpaceGoal(FreeSpaceKind.HaveFree, bytes), include);

    public static CleanupRequest FreeUp(long bytes, bool include = false)
        => new(CleanupMode.FreeSpace, null, new FreeSpaceGoal(FreeSpaceKind.FreeUp, bytes), include);

    /// <summary>firstShown defaults to every candidate: the review list "as first shown".</summary>
    public CleanupPlan Build(CleanupRequest request, CleanupRows? rows = null, IReadOnlySet<ItemId>? firstShown = null)
    {
        var inputs = Inputs();
        var candidates = CleanupPlanner.Candidates(inputs);
        return CleanupPlanner.Build(inputs, candidates, request, rows ?? NoRows,
                                    firstShown ?? candidates.Select(c => c.Unit).ToHashSet());
    }
}
```

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupBuildBeforeDateTests.cs
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public class CleanupBuildBeforeDateTests
{
    private static readonly DateTime Oct4 = Utc(2026, 10, 4, 20, 0);

    [Fact]
    public void Deletes_units_strictly_before_the_site_local_day_and_keeps_the_day_itself()
    {
        var s = new CleanupScenario();
        var jul24 = s.AddVideo(Utc(2026, 7, 24, 20, 0));
        var jul25 = s.AddVideo(Utc(2026, 7, 26, 7, 50));          // Jul 25 23:50 AKDT
        var jul26 = s.AddVideo(Utc(2026, 7, 26, 8, 10));          // Jul 26 00:10 AKDT
        var jul27 = s.AddVideo(Utc(2026, 7, 27, 20, 0));
        var plan = s.Build(Before(2026, 7, 26));
        Assert.Equal(new[] { jul24, jul25 }, plan.Delete.Select(c => c.Unit));
        Assert.DoesNotContain(plan.Delete, c => c.Unit == jul26 || c.Unit == jul27);
        Assert.Equal(new DateOnly(2026, 7, 26), plan.Cutoff.BeforeDate);
        Assert.Equal(2, plan.FileCount);
        Assert.Equal(2_000_158_720, plan.AllocatedBytes);
        Assert.Equal(14_400_158_720, plan.ExpectedFreeAfter);
        Assert.Empty(plan.NotDeletable.Where(k => k.Unit is not null));
        Assert.Null(plan.Shortfall);
        Assert.Equal(CardRoot, plan.CardRoot);
        Assert.Equal(Identity, plan.Card);
        Assert.Equal("9f3c0a6d12e4b7a1", plan.InventoryHash);
        Assert.Equal("FC9113", plan.CameraModel);
    }

    [Fact]
    public void A_flight_across_local_midnight_is_split_and_reported()
    {
        var s = new CleanupScenario();
        var session = new SessionKey("1581F6Z", Utc(2026, 7, 26, 7, 40));
        var before = s.AddVideo(Utc(2026, 7, 26, 7, 50), session: session);   // 20260725235000 local
        var after = s.AddVideo(Utc(2026, 7, 26, 8, 10), session: session);
        var plan = s.Build(Before(2026, 7, 26));
        Assert.Equal(new[] { before }, plan.Delete.Select(c => c.Unit));
        Assert.DoesNotContain(plan.Delete, c => c.Unit == after);
        Assert.Equal(new DateTime(2026, 7, 26, 0, 10, 0), plan.Cutoff.FlightContinuesLocal);
        Assert.Equal(new DateTime(2026, 7, 25, 23, 50, 0), plan.Cutoff.LastLocalTime);
        Assert.Equal(Ak, plan.Cutoff.TzId);
    }

    [Fact]
    public void Site_local_date_decides_not_the_pc_zone()
    {
        var s = new CleanupScenario();
        var makaha = s.AddVideo(Utc(2026, 3, 1, 9, 30), tz: "Pacific/Honolulu");   // Feb 28 23:30 HST; Mar 1 in Alaska
        Assert.Equal(new[] { makaha }, s.Build(Before(2026, 3, 1)).Delete.Select(c => c.Unit));
        Assert.Empty(s.Build(Before(2026, 2, 28)).Delete);
    }

    [Fact]
    public void Not_in_library_units_need_the_switch_and_decisions_are_not_consent()
    {
        var s = new CleanupScenario();
        var fresh = s.AddVideo(Utc(2026, 7, 20, 20, 0), AuditCategory.Unaccounted, listed: false);
        var dismissed = s.AddVideo(Utc(2026, 7, 21, 20, 0), listed: false);
        s.Decide(dismissed, DecisionKind.Dismissed, Oct4);
        var assumed = s.AddPhoto(Utc(2026, 7, 22, 20, 0), listed: false);
        s.Decide(assumed, DecisionKind.AssumedImported, Oct4);
        var evidence = s.AddVideo(Utc(2026, 7, 23, 20, 0));

        var off = s.Build(Before(2026, 7, 26));
        Assert.Equal(new[] { evidence }, off.Delete.Select(c => c.Unit));
        Assert.Equal(new[] { fresh, dismissed, assumed }, off.NotInLibraryInScope.Select(c => c.Unit));
        Assert.Equal("not in your library: you marked it not needed on Oct 4",
                     off.NotDeletable.Single(k => k.Unit == dismissed).Reason);
        Assert.Empty(off.Undecided);
        Assert.Equal(NotInLibraryReason.RecordedAsImported, off.NotInLibraryInScope.Single(c => c.Unit == assumed).NotInLibrary);
        Assert.Equal("you recorded it as imported on Oct 4; not verified", off.NotInLibraryInScope.Single(c => c.Unit == assumed).Reason);

        var on = s.Build(Before(2026, 7, 26, include: true));
        Assert.Equal(new[] { fresh, dismissed, assumed, evidence }, on.Delete.Select(c => c.Unit));
    }

    [Fact]
    public void Never_units_in_range_are_kept_with_their_reason_and_loose_files_are_listed_apart()
    {
        var s = new CleanupScenario();
        var probe = s.AddVideo(Utc(2026, 7, 20, 20, 0), probeError: "bad moov");
        var laterNever = s.AddVideo(Utc(2026, 7, 28, 20, 0), probeError: "bad moov");
        s.AddLoose("MISC/FC9113.db", EntryClass.Skip, 2_000_000);
        s.AddDir("DCIM");
        var plan = s.Build(Before(2026, 7, 26));
        Assert.Empty(plan.Delete);
        var kept = plan.NotDeletable.Single(k => k.Unit == probe);
        Assert.Equal("its metadata couldn't be read", kept.Reason);
        Assert.Equal(new[] { "DCIM/DJI_001/DJI_20260720120000_0101_D.MP4" }, kept.CardRelPaths);
        Assert.DoesNotContain(plan.NotDeletable, k => k.Unit == laterNever);
        var misc = Assert.Single(plan.NotDeletable, k => k.Unit is null);
        Assert.Equal(new[] { "MISC/FC9113.db" }, misc.CardRelPaths);
        Assert.Equal("DJI system file", misc.Reason);
    }

    [Fact]
    public void Fingerprint_is_stable_for_the_same_content_and_changes_with_it()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 24, 20, 0));
        s.AddVideo(Utc(2026, 7, 27, 20, 0));
        var a = s.Build(Before(2026, 7, 26));
        var b = s.Build(Before(2026, 7, 26));
        Assert.NotEqual(a.PlanId, b.PlanId);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Matches("^[0-9a-f]{16}$", a.Fingerprint);
        Assert.NotEqual(a.Fingerprint, s.Build(Before(2026, 7, 25)).Fingerprint);   // same Delete, other request
        Assert.NotEqual(a.Fingerprint, s.Build(Before(2026, 7, 28)).Fingerprint);   // other Delete
        Assert.Equal(a.Fingerprint, CleanupFingerprint.Compute(a.Request, a.SpaceBefore, a.Delete.Reverse()));
    }

    [Fact]
    public void Before_date_without_a_date_is_a_caller_bug()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 24, 20, 0));
        Assert.Throws<ArgumentException>(() => s.Build(new CleanupRequest(CleanupMode.BeforeDate, null, null, false)));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupBuildBeforeDateTests"`
Expected: build fails with `CS0117: 'CleanupPlanner' does not contain a definition for 'Build'` and `CS0103: The name 'CleanupFingerprint' does not exist`.

- [ ] **Step 3: Implement**

`CleanupPlan` itself is Part 02's (`src/UasSort.Core/Model/Cleanup.cs`, namespace `UasSort.Core`); `Build` below is the only caller of its internal constructor. No `InternalsVisibleTo` file is added here (Part 02's `UasSort.Core.csproj` already grants it to `UasSort.Core.Tests` and `UasSort.Testing`).

```csharp
// src/UasSort.Core/Cleanup/CleanupFingerprint.cs
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace UasSort.Core.Cleanup;

/// <summary>Ref §10.6 Fingerprint: XxHash64 over request, SpaceBefore, the sorted delete lines and NotInLibrary ids (defined here).</summary>
public static class CleanupFingerprint
{
    public static string Compute(CleanupRequest request, CardSpace spaceBefore, IEnumerable<CleanupCandidate> delete)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(spaceBefore);
        ArgumentNullException.ThrowIfNull(delete);
        var inv = CultureInfo.InvariantCulture;
        var units = delete.ToList();
        var sb = new StringBuilder();
        sb.Append(inv, $"{request.Mode}|{request.Before?.ToString("yyyy-MM-dd", inv) ?? "-"}|{request.Goal?.Kind.ToString() ?? "-"}|{request.Goal?.Bytes ?? -1}|{request.IncludeNotInLibrary}\n");
        sb.Append(inv, $"{spaceBefore.FreeBytes}|{spaceBefore.TotalBytes}|{spaceBefore.ClusterBytes}\n");
        foreach (var line in units.SelectMany(c => c.Files)
                                  .Select(f => string.Create(inv, $"{CleanupPaths.Rel(f.RelPath)}|{f.Size}|{f.MtimeUtc.Ticks}"))
                                  .Order(StringComparer.Ordinal))
            sb.Append(line).Append('\n');
        foreach (var id in units.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary)
                                .Select(c => c.Unit.CardRelPath).Order(StringComparer.Ordinal))
            sb.Append(id).Append('\n');
        return XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(sb.ToString())).ToString("x16", inv);
    }
}
```

```csharp
// src/UasSort.Core/Cleanup/CleanupPlanner.Build.cs
namespace UasSort.Core.Cleanup;

public static partial class CleanupPlanner
{
    private enum RowState { Keep, Delete, Undecided }

    public static CleanupPlan Build(CleanupInputs inputs, ImmutableArray<CleanupCandidate> candidates, CleanupRequest request,
                                    CleanupRows rows, IReadOnlySet<ItemId> firstShown)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(firstShown);

        var ordered = candidates.OrderBy(c => c.CaptureUtc).ThenBy(c => c.Unit.CardRelPath, StringComparer.Ordinal).ToImmutableArray();
        var on = request.IncludeNotInLibrary;

        RowState Row(CleanupCandidate c) =>
            rows.Keep.Contains(c.Unit) ? RowState.Keep
            : rows.Delete.Contains(c.Unit) ? RowState.Delete
            : firstShown.Contains(c.Unit) ? (c.TickedForOffload ? RowState.Keep : RowState.Delete)
            : RowState.Undecided;
        static bool IsNil(CleanupCandidate c) => c.Eligibility == CleanupEligibility.NotInLibrary;
        bool Deletable(CleanupCandidate c) => c.Eligibility == CleanupEligibility.Evidence || (on && IsNil(c) && Row(c) == RowState.Delete);

        var space = inputs.Space;
        bool[] inRange;
        var cutoffIndex = -1;
        FreeSpaceShortfall? shortfall = null;
        switch (request.Mode)
        {
            case CleanupMode.BeforeDate:
                var before = request.Before ?? throw new ArgumentException("Before-date mode needs a picked date.", nameof(request));
                inRange = [.. ordered.Select(c => c.LocalDate < before)];     // strictly before; the chosen day is kept
                break;
            case CleanupMode.FreeSpace:
                throw new NotSupportedException("Free-space mode is added in Task 08.6.");
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "Unknown cleanup mode.");
        }

        var delete = ordered.Where((c, i) => inRange[i] && Deletable(c)).ToImmutableArray();
        var scope = ordered.Where((c, i) => inRange[i] && IsNil(c)).ToImmutableArray();
        var undecided = on ? scope.Where(c => Row(c) == RowState.Undecided).Select(c => c.Unit).ToImmutableHashSet()
                           : ImmutableHashSet<ItemId>.Empty;
        var kept = new List<CleanupKept>();
        for (var i = 0; i < ordered.Length; i++)
        {
            var c = ordered[i];
            if (!inRange[i] || Deletable(c)) continue;
            kept.Add(new CleanupKept(c.Unit, [.. c.Files.Select(f => CleanupPaths.Rel(f.RelPath))], c.AllocatedBytes, c.CaptureUtc,
                                     KeptReason(c, on, Row(c))));
        }
        kept.AddRange(LooseFiles(inputs, candidates));

        var cutoff = request.Mode == CleanupMode.BeforeDate
            ? BeforeDateCutoff(request.Before!.Value, ordered, delete)
            : FreeSpaceCutoff(ordered, cutoffIndex, delete);
        var source = inputs.Inventory.Source;
        var fileCount = delete.Sum(c => c.Files.Length);
        var allocatedBytes = delete.Sum(c => c.AllocatedBytes);
        var expectedFreeAfter = Math.Min(space.TotalBytes, space.FreeBytes + allocatedBytes);
        return new CleanupPlan(Guid.NewGuid().ToString("N"), source.Identity ?? inputs.Volume.Identity, source.Root,
            inputs.Inventory.InventoryHash, inputs.Inventory.CameraModel, request, space, delete, scope, rows, undecided,
            [.. kept], cutoff, fileCount, allocatedBytes, expectedFreeAfter, shortfall,
            CleanupFingerprint.Compute(request, space, delete));
    }

    private static string KeptReason(CleanupCandidate c, bool on, RowState row) => c.Eligibility switch
    {
        CleanupEligibility.NotInLibrary when !on => "not in your library: " + c.Reason,
        CleanupEligibility.NotInLibrary when row == RowState.Keep => (c.TickedForOffload ? "ticked for offload: " : "kept by you: ") + c.Reason,
        CleanupEligibility.NotInLibrary => "new in range, not decided yet: " + c.Reason,
        _ => c.Reason,
    };

    private static (int DeletedOnDay, int OnDay) DayCounts(ImmutableArray<CleanupCandidate> ordered,
                                                           ImmutableArray<CleanupCandidate> delete, DateOnly day)
        => (delete.Where(c => c.LocalDate == day).Sum(c => c.Files.Length),
            ordered.Where(c => c.LocalDate == day).Sum(c => c.Files.Length));

    private static CleanupCutoff BeforeDateCutoff(DateOnly before, ImmutableArray<CleanupCandidate> ordered,
                                                  ImmutableArray<CleanupCandidate> delete)
    {
        if (delete.IsEmpty) return new CleanupCutoff(before, null, null, null, 0, 0, null);
        var last = delete[^1];
        var (deletedOnDay, onDay) = DayCounts(ordered, delete, last.LocalDate);
        var deleted = delete.Select(c => c.Unit).ToHashSet();
        DateTime? continues = null;
        foreach (var d in delete)
        {
            if (d.Session is not { } s) continue;
            foreach (var c in ordered)
                if (!deleted.Contains(c.Unit) && c.LocalDate >= before && c.Session is { } t && s.SameSession(t)
                    && (continues is null || c.LocalTime < continues)) continues = c.LocalTime;
        }
        return new CleanupCutoff(before, last.CaptureUtc, last.LocalTime, last.TzId, deletedOnDay, onDay, continues);
    }

    private static CleanupCutoff FreeSpaceCutoff(ImmutableArray<CleanupCandidate> ordered, int cutoffIndex,
                                                 ImmutableArray<CleanupCandidate> delete)
    {
        if (cutoffIndex < 0) return new CleanupCutoff(null, null, null, null, 0, 0, null);
        var last = ordered[cutoffIndex];                                  // S_k (Ref §10.6 Mode 2 Cutoff)
        var (deletedOnDay, onDay) = DayCounts(ordered, delete, last.LocalDate);
        return new CleanupCutoff(null, last.CaptureUtc, last.LocalTime, last.TzId, deletedOnDay, onDay, null);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupBuildBeforeDateTests"`
Expected: PASS (7 tests). Also re-run `--filter-class "*CleanupCandidatesTests"`: still PASS.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/CleanupFingerprint.cs src/UasSort.Core/Cleanup/CleanupPlanner.Build.cs tests/UasSort.Core.Tests/Cleanup/CleanupScenario.Build.cs tests/UasSort.Core.Tests/Cleanup/CleanupBuildBeforeDateTests.cs
git commit -m "feat: build before-date card cleanup plans with fingerprint

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 08.6: Build — Free-space mode, minimal prefix and shortfall

**Files:**
- Modify: `src/UasSort.Core/Cleanup/CleanupPlanner.Build.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/CleanupBuildFreeSpaceTests.cs`

**Interfaces:**
- Consumes: `FreeSpaceGoal.TargetFreeBytes(CardSpace)` (Ref §3), `CleanupPlanner.Build` (08.5).
- Produces: Free-space branch of `CleanupPlanner.Build`: S = every unit in order that is Evidence, or NotInLibrary with the switch on and its row on Delete or undecided; the plan is the shortest prefix S₁…S_k with `FreeBytes + Σ AllocatedBytes ≥ target` (Never units and Keep rows passed over; undecided rows count for the walk but are never deleted); "in range" = at or before S_k; unreachable → all of S plus `FreeSpaceShortfall(FreeableBytes = Σ S, HeldByNotInLibrary = Σ NotInLibrary units outside S, HeldByNever = Σ Never units + loose files)`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupBuildFreeSpaceTests.cs
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public class CleanupBuildFreeSpaceTests
{
    private const long Unit5 = 5_000_003_584;     // 38,147 clusters of 128 KiB exactly

    private static (CleanupScenario S, ItemId[] Ids) SixEvidenceUnits()
    {
        var s = new CleanupScenario();
        var ids = Enumerable.Range(3, 6).Select(d => s.AddVideo(Utc(2026, 7, d, 20, 0), size: Unit5)).ToArray();
        return (s, ids);
    }

    [Fact]
    public void Have_free_targets_the_amount_and_takes_the_minimal_prefix()
    {
        var (s, ids) = SixEvidenceUnits();
        var plan = s.Build(HaveFree(20_000_000_000));
        Assert.Equal(new[] { ids[0], ids[1] }, plan.Delete.Select(c => c.Unit));     // 1 unit → 17.4 GB falls short
        Assert.Equal(22_400_007_168, plan.ExpectedFreeAfter);
        Assert.Null(plan.Shortfall);
        Assert.Equal(new DateTime(2026, 7, 4, 12, 0, 0), plan.Cutoff.LastLocalTime);
        Assert.Null(plan.Cutoff.BeforeDate);
    }

    [Fact]
    public void Free_up_targets_free_plus_the_amount()
    {
        var (s, ids) = SixEvidenceUnits();
        Assert.Equal(32_400_000_000, new FreeSpaceGoal(FreeSpaceKind.FreeUp, 20_000_000_000).TargetFreeBytes(s.Space));
        Assert.Equal(20_000_000_000, new FreeSpaceGoal(FreeSpaceKind.HaveFree, 20_000_000_000).TargetFreeBytes(s.Space));
        var plan = s.Build(FreeUp(20_000_000_000));
        Assert.Equal(ids[..4], plan.Delete.Select(c => c.Unit));                   // 3 units free up only 15.0 GB
    }

    [Fact]
    public void Never_units_are_passed_over_without_ending_the_walk()
    {
        var s = new CleanupScenario();
        var u1 = s.AddVideo(Utc(2026, 7, 3, 20, 0), size: Unit5);
        var never = s.AddVideo(Utc(2026, 7, 4, 20, 0), size: Unit5, probeError: "bad moov");
        var u3 = s.AddVideo(Utc(2026, 7, 5, 20, 0), size: Unit5);
        s.AddVideo(Utc(2026, 7, 6, 20, 0), size: Unit5);
        var plan = s.Build(HaveFree(20_000_000_000));
        Assert.Equal(new[] { u1, u3 }, plan.Delete.Select(c => c.Unit));
        Assert.Equal("its metadata couldn't be read", plan.NotDeletable.Single(k => k.Unit == never).Reason);
    }

    [Fact]
    public void Target_already_met_gives_an_empty_plan()
    {
        var (s, _) = SixEvidenceUnits();
        var plan = s.Build(HaveFree(10_000_000_000));
        Assert.Empty(plan.Delete);
        Assert.Null(plan.Shortfall);
        Assert.Null(plan.Cutoff.LastCaptureUtc);
        Assert.Equal(12_400_000_000, plan.ExpectedFreeAfter);
    }

    [Fact]
    public void Unreachable_target_deletes_everything_deletable_and_reports_the_shortfall()
    {
        var s = new CleanupScenario();
        var e1 = s.AddVideo(Utc(2026, 7, 3, 20, 0), size: Unit5);
        var nil = s.AddVideo(Utc(2026, 7, 4, 20, 0), AuditCategory.Unaccounted, listed: false, size: Unit5);
        s.AddVideo(Utc(2026, 7, 5, 20, 0), size: Unit5, probeError: "bad moov");
        var e2 = s.AddVideo(Utc(2026, 7, 6, 20, 0), size: Unit5);
        s.AddLoose("MISC/FC9113.db", EntryClass.Skip, 2_000_000);                  // 16 clusters = 2,097,152

        var off = s.Build(HaveFree(200_000_000_000));
        Assert.Equal(new[] { e1, e2 }, off.Delete.Select(c => c.Unit));
        Assert.Equal(new FreeSpaceShortfall(10_000_007_168, Unit5, Unit5 + 2_097_152), off.Shortfall);

        var on = s.Build(HaveFree(200_000_000_000, include: true));
        Assert.Equal(new[] { e1, nil, e2 }, on.Delete.Select(c => c.Unit));
        Assert.Equal(new FreeSpaceShortfall(15_000_010_752, 0, Unit5 + 2_097_152), on.Shortfall);
    }

    [Fact]
    public void Cluster_rounding_counts_toward_the_target()
    {
        var s = new CleanupScenario { Space = new CardSpace(1_000_000, 256_060_514_304, 131_072) };
        var one = s.AddVideo(Utc(2026, 7, 3, 20, 0), size: 1);
        var met = s.Build(HaveFree(1_131_072));
        Assert.Equal(new[] { one }, met.Delete.Select(c => c.Unit));
        Assert.Null(met.Shortfall);
        Assert.NotNull(s.Build(HaveFree(1_131_073)).Shortfall);
    }

    [Fact]
    public void Cutoff_counts_the_files_of_the_cutoff_day()
    {
        var s = new CleanupScenario { Space = new CardSpace(1_000_000_000, 256_060_514_304, 131_072) };
        s.AddVideo(Utc(2026, 7, 3, 20, 0), size: Unit5, companions: Comp.Lrf | Comp.Srt);
        var cut = s.AddVideo(Utc(2026, 8, 30, 22, 22), size: Unit5, companions: Comp.Lrf | Comp.Srt);   // Aug 30 14:22 AKDT
        s.AddVideo(Utc(2026, 8, 30, 23, 0), size: Unit5, companions: Comp.Lrf | Comp.Srt);
        s.AddVideo(Utc(2026, 8, 31, 1, 0), size: Unit5, companions: Comp.Lrf | Comp.Srt);              // Aug 30 17:00 AKDT
        var plan = s.Build(HaveFree(10_000_000_000));
        Assert.Equal(cut, plan.Delete[^1].Unit);
        Assert.Equal(6, plan.FileCount);
        Assert.Equal(10_040_377_344, plan.AllocatedBytes);
        Assert.Equal(new DateTime(2026, 8, 30, 14, 22, 0), plan.Cutoff.LastLocalTime);
        Assert.Equal(3, plan.Cutoff.FilesDeletedOnCutoffDay);
        Assert.Equal(9, plan.Cutoff.FilesOnCutoffDay);
    }

    [Fact]
    public void Free_space_without_a_goal_is_a_caller_bug()
    {
        var (s, _) = SixEvidenceUnits();
        Assert.Throws<ArgumentException>(() => s.Build(new CleanupRequest(CleanupMode.FreeSpace, null, null, false)));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupBuildFreeSpaceTests"`
Expected: FAIL — 7 tests throw `System.NotSupportedException: Free-space mode is added in Task 08.6.`; `Free_space_without_a_goal_is_a_caller_bug` fails with `Assert.Throws() Failure: Exception type was not an exact match`.

- [ ] **Step 3: Implement**

In `src/UasSort.Core/Cleanup/CleanupPlanner.Build.cs`, add the walk predicate right after the `Deletable` local function:

```csharp
        bool InWalk(CleanupCandidate c) => c.Eligibility == CleanupEligibility.Evidence || (on && IsNil(c) && Row(c) != RowState.Keep);
```

Replace the Free-space case:

```csharp
            case CleanupMode.FreeSpace:
                throw new NotSupportedException("Free-space mode is added in Task 08.6.");
```

with:

```csharp
            case CleanupMode.FreeSpace:
                var goal = request.Goal ?? throw new ArgumentException("Free-space mode needs a goal.", nameof(request));
                (inRange, cutoffIndex, var reached) = FreeSpaceWalk(ordered, InWalk, space.FreeBytes, goal.TargetFreeBytes(space));
                if (!reached) shortfall = Shortfall(ordered, InWalk, LooseFiles(inputs, candidates));
                break;
```

and add these two methods to the class:

```csharp
    /// <summary>Shortest prefix S₁…S_k of the walk with free + Σ allocated ≥ target (Ref §10.6 Mode 2).</summary>
    private static (bool[] InRange, int CutoffIndex, bool Reached) FreeSpaceWalk(ImmutableArray<CleanupCandidate> ordered,
        Func<CleanupCandidate, bool> inWalk, long freeBytes, long target)
    {
        var inRange = new bool[ordered.Length];
        if (freeBytes >= target) return (inRange, -1, true);               // k = 0: nothing to delete
        var acc = freeBytes;
        var last = -1;
        for (var i = 0; i < ordered.Length; i++)
        {
            if (!inWalk(ordered[i])) continue;                              // Never units and Keep rows are passed over
            acc += ordered[i].AllocatedBytes;
            last = i;
            if (acc >= target) break;
        }
        for (var i = 0; i <= last; i++) inRange[i] = true;
        return (inRange, last, acc >= target);
    }

    private static FreeSpaceShortfall Shortfall(ImmutableArray<CleanupCandidate> ordered, Func<CleanupCandidate, bool> inWalk,
                                                ImmutableArray<CleanupKept> loose)
        => new(ordered.Where(inWalk).Sum(c => c.AllocatedBytes),
               ordered.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary && !inWalk(c)).Sum(c => c.AllocatedBytes),
               ordered.Where(c => c.Eligibility == CleanupEligibility.Never).Sum(c => c.AllocatedBytes) + loose.Sum(k => k.Bytes));
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupBuild*"`
Expected: PASS (Before-date 7 + Free-space 8).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/CleanupPlanner.Build.cs tests/UasSort.Core.Tests/Cleanup/CleanupBuildFreeSpaceTests.cs
git commit -m "feat: add free-space card cleanup plans with shortfall

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 08.7: Review rows — ticked for offload, undecided rows, Keep recompute

**Files:**
- Create: `src/UasSort.Core/Cleanup/CleanupRowOps.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/CleanupRowsTests.cs`

**Interfaces:**
- Consumes: `CleanupPlan` (Part 02; built by 08.5), `CleanupRows`, `ItemId` (Part 02, Ref §3), `CleanupPlanner.Build` (08.5/08.6).
- Produces (defined here, used by `CleanupVm` in Part 10):
  - `public static class CleanupRowOps { static CleanupRows Set(CleanupRows rows, ItemId unit, bool delete); static CleanupRows DeleteAll(CleanupPlan plan); static CleanupRows KeepAll(CleanupPlan plan); }` — `DeleteAll` sets every review row to Delete except "ticked for offload" rows, which keep their state (an undecided ticked row becomes Keep, so [Delete all] also settles "new in range" rows without deleting a ticked one); `KeepAll` sets every review row to Keep.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupRowsTests.cs
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public class CleanupRowsTests
{
    private const long Unit5 = 5_000_003_584;

    [Fact]
    public void Keep_inside_the_free_space_prefix_extends_it_and_moves_the_cutoff()
    {
        var s = new CleanupScenario();
        var e1 = s.AddVideo(Utc(2026, 7, 3, 20, 0), size: Unit5);
        var n1 = s.AddVideo(Utc(2026, 7, 4, 20, 0), AuditCategory.Unaccounted, listed: false, size: Unit5);
        var e2 = s.AddVideo(Utc(2026, 7, 5, 20, 0), size: Unit5);
        var first = s.Build(HaveFree(20_000_000_000, include: true));
        Assert.Equal(new[] { e1, n1 }, first.Delete.Select(c => c.Unit));

        var kept = s.Build(HaveFree(20_000_000_000, include: true), CleanupRowOps.Set(NoRows, n1, delete: false));
        Assert.Equal(new[] { e1, e2 }, kept.Delete.Select(c => c.Unit));
        Assert.True(kept.Cutoff.LastCaptureUtc > first.Cutoff.LastCaptureUtc);
        Assert.Equal("kept by you: new: not in your library", kept.NotDeletable.Single(k => k.Unit == n1).Reason);
        Assert.NotEqual(first.Fingerprint, kept.Fingerprint);
    }

    [Fact]
    public void A_row_that_joins_later_is_undecided_counts_for_the_walk_and_is_never_deleted()
    {
        var s = new CleanupScenario();
        var e1 = s.AddVideo(Utc(2026, 7, 3, 20, 0), size: Unit5);
        var n1 = s.AddVideo(Utc(2026, 7, 4, 20, 0), AuditCategory.Unaccounted, listed: false, size: Unit5);
        var n2 = s.AddVideo(Utc(2026, 7, 5, 20, 0), AuditCategory.Unaccounted, listed: false, size: Unit5);
        var e2 = s.AddVideo(Utc(2026, 7, 6, 20, 0), size: Unit5);
        var shown = new HashSet<ItemId> { e1, n1, e2 };                          // n2 was out of range when first shown
        var req = HaveFree(20_000_000_000, include: true);

        var first = s.Build(req, NoRows, shown);
        Assert.Equal(new[] { e1, n1 }, first.Delete.Select(c => c.Unit));
        Assert.Empty(first.Undecided);

        var keepN1 = CleanupRowOps.Set(NoRows, n1, delete: false);
        var plan = s.Build(req, keepN1, shown);
        Assert.Equal(new[] { e1 }, plan.Delete.Select(c => c.Unit));             // n2 ends the walk but is not deleted
        Assert.Equal(new[] { n2 }, plan.Undecided);
        Assert.Equal(new[] { n1, n2 }, plan.NotInLibraryInScope.Select(c => c.Unit));
        Assert.Equal("new in range, not decided yet: new: not in your library", plan.NotDeletable.Single(k => k.Unit == n2).Reason);

        var decided = s.Build(req, CleanupRowOps.Set(keepN1, n2, delete: true), shown);
        Assert.Equal(new[] { e1, n2 }, decided.Delete.Select(c => c.Unit));
        Assert.Empty(decided.Undecided);

        var all = s.Build(req, CleanupRowOps.DeleteAll(plan), shown);
        Assert.Empty(all.Undecided);
        Assert.Equal(new[] { e1, n1 }, all.Delete.Select(c => c.Unit));           // [Delete all] also sets n1 back to Delete

        Assert.Empty(s.Build(HaveFree(20_000_000_000, include: false), keepN1, shown).Undecided);
    }

    [Fact]
    public void Before_date_keep_drops_only_that_unit_and_a_later_date_brings_undecided_rows()
    {
        var s = new CleanupScenario();
        var n1 = s.AddVideo(Utc(2026, 7, 20, 20, 0), AuditCategory.Unaccounted, listed: false);
        var e = s.AddVideo(Utc(2026, 7, 21, 20, 0));
        var n3 = s.AddVideo(Utc(2026, 7, 22, 20, 0), AuditCategory.Unaccounted, listed: false);
        var n2 = s.AddVideo(Utc(2026, 7, 27, 20, 0), AuditCategory.Unaccounted, listed: false);
        var shown = new HashSet<ItemId> { n1, e, n3 };
        var keepN1 = CleanupRowOps.Set(NoRows, n1, delete: false);

        var jul26 = s.Build(Before(2026, 7, 26, include: true), keepN1, shown);
        Assert.Equal(new[] { e, n3 }, jul26.Delete.Select(c => c.Unit));

        var jul28 = s.Build(Before(2026, 7, 28, include: true), keepN1, shown);
        Assert.Equal(new[] { e, n3 }, jul28.Delete.Select(c => c.Unit));
        Assert.Equal(new[] { n2 }, jul28.Undecided);
    }

    [Fact]
    public void Ticked_for_offload_rows_start_on_keep_and_delete_all_leaves_them()
    {
        var s = new CleanupScenario();
        var ticked = Enumerable.Range(0, 20)
            .Select(i => s.AddVideo(Utc(2026, 7, 10, 0, 0).AddHours(i), AuditCategory.Unaccounted, listed: false)).ToArray();
        foreach (var id in ticked) s.Included.Add(id);
        var req = Before(2026, 7, 26, include: true);

        var plan = s.Build(req);
        Assert.Empty(plan.Delete);
        Assert.Equal(20, plan.NotInLibraryInScope.Length);
        Assert.All(plan.NotInLibraryInScope, c => Assert.True(c.TickedForOffload));
        Assert.All(plan.NotDeletable.Where(k => k.Unit is not null), k => Assert.StartsWith("ticked for offload: ", k.Reason, StringComparison.Ordinal));

        var afterDeleteAll = s.Build(req, CleanupRowOps.DeleteAll(plan));
        Assert.Empty(afterDeleteAll.Delete);

        var one = s.Build(req, CleanupRowOps.Set(CleanupRowOps.DeleteAll(plan), ticked[7], delete: true));
        Assert.Equal(new[] { ticked[7] }, one.Delete.Select(c => c.Unit));
    }

    [Fact]
    public void Keep_all_keeps_every_review_row()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 20, 20, 0), AuditCategory.Unaccounted, listed: false);
        var e = s.AddVideo(Utc(2026, 7, 21, 20, 0));
        s.AddPhoto(Utc(2026, 7, 22, 20, 0), AuditCategory.AssumedByRule, listed: false);
        var req = Before(2026, 7, 26, include: true);
        var plan = s.Build(req);
        Assert.Equal(3, plan.Delete.Length);
        Assert.Equal(new[] { e }, s.Build(req, CleanupRowOps.KeepAll(plan)).Delete.Select(c => c.Unit));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupRowsTests"`
Expected: build fails with `CS0103: The name 'CleanupRowOps' does not exist in the current context`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Cleanup/CleanupRowOps.cs
namespace UasSort.Core.Cleanup;

/// <summary>Row decisions of the not-in-library review list (Ref §10.6; defined here). Build is re-run after each.</summary>
public static class CleanupRowOps
{
    public static CleanupRows Set(CleanupRows rows, ItemId unit, bool delete)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var keep = rows.Keep.Remove(unit);
        var del = rows.Delete.Remove(unit);
        return delete ? new CleanupRows(keep, del.Add(unit)) : new CleanupRows(keep.Add(unit), del);
    }

    /// <summary>[Delete all]: every review row to Delete, except "ticked for offload" rows, which keep their state
    /// (an undecided ticked row becomes Keep).</summary>
    public static CleanupRows DeleteAll(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var keep = plan.Rows.Keep.ToBuilder();
        var del = plan.Rows.Delete.ToBuilder();
        foreach (var c in plan.NotInLibraryInScope)
        {
            if (c.TickedForOffload)
            {
                if (plan.Undecided.Contains(c.Unit)) keep.Add(c.Unit);
                continue;
            }
            keep.Remove(c.Unit);
            del.Add(c.Unit);
        }
        return new CleanupRows(keep.ToImmutable(), del.ToImmutable());
    }

    /// <summary>[Keep all]: every review row to Keep.</summary>
    public static CleanupRows KeepAll(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var keep = plan.Rows.Keep.ToBuilder();
        var del = plan.Rows.Delete.ToBuilder();
        foreach (var c in plan.NotInLibraryInScope)
        {
            del.Remove(c.Unit);
            keep.Add(c.Unit);
        }
        return new CleanupRows(keep.ToImmutable(), del.ToImmutable());
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupRowsTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/CleanupRowOps.cs tests/UasSort.Core.Tests/Cleanup/CleanupRowsTests.cs
git commit -m "feat: add card cleanup review row operations

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 08.8: Cleanup texts — cutoff line, nothing to delete, shortfall, evidence split

**Files:**
- Create: `src/UasSort.Core/Cleanup/CleanupTexts.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/CleanupTextsTests.cs`

**Interfaces:**
- Consumes: `CleanupPlan` (Part 02; built by 08.5), `CleanupFormat` (08.1).
- Produces (defined here; `CleanupVm` in Part 10 binds these strings, so the wording is tested once, here):
  - `public static class CleanupTexts { static string CutoffLine(CleanupPlan plan); static string? NothingToDelete(CleanupPlan plan); static string? ShortfallLine(CleanupPlan plan); static string EvidenceSplit(CleanupPlan plan); static string? NeverCopiesLine(CleanupPlan plan); static string VolumeName(string cardRoot); }`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupTextsTests.cs
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public class CleanupTextsTests
{
    private const long Unit5 = 5_000_003_584;

    [Fact]
    public void Before_date_line_names_the_kept_files_and_the_reviewed_ones()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 20, 20, 0));
        s.AddVideo(Utc(2026, 7, 21, 20, 0));
        s.AddVideo(Utc(2026, 7, 22, 20, 0), probeError: "bad moov", companions: Comp.Lrf | Comp.Srt);
        s.AddVideo(Utc(2026, 7, 23, 20, 0), AuditCategory.Unaccounted, listed: false);
        Assert.Equal("Deletes 2 files captured before Jul 26, 2026 (local time at each site) that are in your library; "
                   + "Jul 26 and later are kept. 4 older files are kept (see 'Kept').",
                     CleanupTexts.CutoffLine(s.Build(Before(2026, 7, 26))));
        Assert.Equal("Deletes 2 files captured before Jul 26, 2026 (local time at each site) that are in your library, plus 1 you reviewed; "
                   + "Jul 26 and later are kept. 3 older files are kept (see 'Kept').",
                     CleanupTexts.CutoffLine(s.Build(Before(2026, 7, 26, include: true))));
    }

    [Fact]
    public void Before_date_says_everything_only_when_nothing_older_is_kept_and_names_a_continuing_flight()
    {
        var s = new CleanupScenario();
        var session = new SessionKey("1581F6Z", Utc(2026, 7, 26, 7, 40));
        s.AddVideo(Utc(2026, 7, 26, 7, 50), session: session);
        s.AddVideo(Utc(2026, 7, 26, 8, 10), session: session);
        Assert.Equal("Deletes everything captured before Jul 26, 2026 (local time at each site); Jul 26 and later are kept. "
                   + "A flight continues past the cutoff (Jul 26 00:10 AKDT); its later clips are kept.",
                     CleanupTexts.CutoffLine(s.Build(Before(2026, 7, 26))));
    }

    private static CleanupScenario AugustCard(string firstTz)
    {
        var s = new CleanupScenario { Space = new CardSpace(1_000_000_000, 256_060_514_304, 131_072) };
        s.AddVideo(Utc(2026, 7, 3, 20, 0), size: Unit5, companions: Comp.Lrf | Comp.Srt, tz: firstTz);
        s.AddVideo(Utc(2026, 8, 30, 22, 22), size: Unit5, companions: Comp.Lrf | Comp.Srt);
        s.AddVideo(Utc(2026, 8, 30, 23, 0), size: Unit5, companions: Comp.Lrf | Comp.Srt);
        s.AddVideo(Utc(2026, 8, 31, 1, 0), size: Unit5, companions: Comp.Lrf | Comp.Srt);
        return s;
    }

    [Fact]
    public void Free_space_line_states_range_cutoff_and_the_cutoff_day_count()
    {
        Assert.Equal("Deletes 6 files · 10.0 GB · captured Jul 3 – Aug 30 14:22 AKDT → cutoff: Aug 30 14:22 (3 of 9 files from Aug 30)",
                     CleanupTexts.CutoffLine(AugustCard(Ak).Build(HaveFree(10_000_000_000))));
        Assert.Equal("Deletes 6 files · 10.0 GB · captured Jul 3 HST – Aug 30 14:22 AKDT → cutoff: Aug 30 14:22 (3 of 9 files from Aug 30)",
                     CleanupTexts.CutoffLine(AugustCard("Pacific/Honolulu").Build(HaveFree(10_000_000_000))));
    }

    [Fact]
    public void Target_already_met_says_nothing_to_delete()
    {
        var s = new CleanupScenario { Space = new CardSpace(73_800_000_000, 256_060_514_304, 131_072) };
        s.AddVideo(Utc(2026, 7, 3, 20, 0));
        var plan = s.Build(HaveFree(20_000_000_000));
        Assert.Equal("E: already has 73.8 GB free; nothing to delete", CleanupTexts.NothingToDelete(plan));
        Assert.Equal("E: already has 73.8 GB free; nothing to delete", CleanupTexts.CutoffLine(plan));
        Assert.Null(CleanupTexts.ShortfallLine(plan));
    }

    [Fact]
    public void Shortfall_names_not_in_library_bytes_until_the_switch_is_on()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 3, 20, 0), size: Unit5);
        s.AddVideo(Utc(2026, 7, 4, 20, 0), AuditCategory.Unaccounted, listed: false, size: Unit5);
        s.AddVideo(Utc(2026, 7, 5, 20, 0), size: Unit5, probeError: "bad moov");
        s.AddVideo(Utc(2026, 7, 6, 20, 0), size: Unit5);
        Assert.Equal("Only 10.0 GB can be freed; 5.0 GB is held by files that are not in your library",
                     CleanupTexts.ShortfallLine(s.Build(HaveFree(200_000_000_000))));
        Assert.Equal("Only 15.0 GB can be freed; 5.0 GB is held by files uas-sort never deletes (unknown files, changed files, DJI system files)",
                     CleanupTexts.ShortfallLine(s.Build(HaveFree(200_000_000_000, include: true))));
        Assert.Null(CleanupTexts.NothingToDelete(s.Build(HaveFree(200_000_000_000))));
    }

    [Fact]
    public void Summary_splits_evidence_and_names_files_uas_sort_never_copies()
    {
        var s = new CleanupScenario { CopyJpgTwin = false };
        s.AddVideo(Utc(2026, 7, 3, 20, 0), companions: Comp.Lrf | Comp.Srt);
        s.AddPhoto(Utc(2026, 7, 3, 21, 0), AuditCategory.InLedger, listed: false, twin: true,
                   twinCategory: AuditCategory.SkippedByRule, twinListed: false, ledger: VerifyKind.Cached);
        s.AddPhoto(Utc(2026, 7, 3, 22, 0), AuditCategory.InLedger, listed: false, twin: true,
                   twinCategory: AuditCategory.SkippedByRule, twinListed: false, ledger: VerifyKind.Unbuffered);
        var plan = s.Build(Before(2026, 7, 26));
        Assert.Equal("1 in the library listing · 2 photos found only in the history (Lightroom may have moved them)",
                     CleanupTexts.EvidenceSplit(plan));
        Assert.Equal("Also deletes 4 files uas-sort never copies: 2 JPG twins (copying is off in Settings), 1 LRF proxy, 1 SRT caption",
                     CleanupTexts.NeverCopiesLine(plan));
        Assert.Equal("E:", CleanupTexts.VolumeName(@"E:\"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupTextsTests"`
Expected: build fails with `CS0103: The name 'CleanupTexts' does not exist in the current context`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Cleanup/CleanupTexts.cs
using System.Globalization;

namespace UasSort.Core.Cleanup;

/// <summary>Summary wording built from a CleanupPlan (Ref §10.6 Mode 1/Mode 2/Summary; defined here).</summary>
public static class CleanupTexts
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : string.Create(Inv, $"{n} {many}");

    public static string VolumeName(string cardRoot)
    {
        ArgumentNullException.ThrowIfNull(cardRoot);
        return cardRoot.TrimEnd('\\', '/');
    }

    public static string CutoffLine(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Request.Mode == CleanupMode.BeforeDate ? BeforeDateLine(plan) : FreeSpaceLine(plan);
    }

    private static string BeforeDateLine(CleanupPlan plan)
    {
        var before = plan.Request.Before ?? plan.Cutoff.BeforeDate
                     ?? throw new InvalidOperationException("A before-date plan without a date.");
        var day = CleanupFormat.MonthDayYear(before);
        var dayShort = CleanupFormat.MonthDay(before);
        var keptFiles = plan.NotDeletable.Where(k => k.Unit is not null).Sum(k => k.CardRelPaths.Length);
        var evidenceFiles = plan.Delete.Where(c => c.Eligibility == CleanupEligibility.Evidence).Sum(c => c.Files.Length);
        var reviewedFiles = plan.Delete.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary).Sum(c => c.Files.Length);
        string line;
        if (keptFiles == 0 && !plan.Delete.IsEmpty)
            line = $"Deletes everything captured before {day} (local time at each site); {dayShort} and later are kept.";
        else if (keptFiles == 0)
            line = $"Nothing was captured before {day} (local time at each site).";
        else
        {
            var plus = reviewedFiles > 0 ? string.Create(Inv, $", plus {reviewedFiles} you reviewed") : "";
            var older = keptFiles == 1 ? "1 older file is kept" : string.Create(Inv, $"{keptFiles} older files are kept");
            line = $"Deletes {Plural(evidenceFiles, "file", "files")} captured before {day} (local time at each site) that are in your library{plus}; "
                 + $"{dayShort} and later are kept. {older} (see 'Kept').";
        }
        if (plan.Cutoff.FlightContinuesLocal is { } t && plan.Cutoff.TzId is { } tz && plan.Cutoff.LastCaptureUtc is { } utc)
            line += $" A flight continues past the cutoff ({t.ToString("MMM d HH:mm", Inv)} {CleanupFormat.Abbrev(tz, utc)}); its later clips are kept.";
        return line;
    }

    private static string FreeSpaceLine(CleanupPlan plan)
    {
        if (plan.Delete.IsEmpty) return NothingToDelete(plan) ?? "Nothing on this card can be deleted.";
        var first = plan.Delete[0];
        var c = plan.Cutoff;
        var lastUtc = c.LastCaptureUtc ?? plan.Delete[^1].CaptureUtc;
        var lastTz = c.TzId ?? plan.Delete[^1].TzId;
        var lastLocal = c.LastLocalTime ?? plan.Delete[^1].LocalTime;
        var lastAbbr = CleanupFormat.Abbrev(lastTz, lastUtc);
        var firstAbbr = CleanupFormat.Abbrev(first.TzId, first.CaptureUtc);
        var from = firstAbbr == lastAbbr ? CleanupFormat.MonthDay(first.LocalDate) : $"{CleanupFormat.MonthDay(first.LocalDate)} {firstAbbr}";
        var cut = lastLocal.ToString("MMM d HH:mm", Inv);
        var cutDay = CleanupFormat.MonthDay(DateOnly.FromDateTime(lastLocal));
        return string.Create(Inv,
            $"Deletes {Plural(plan.FileCount, "file", "files")} · {CleanupFormat.Gb(plan.AllocatedBytes)} · captured {from} – {cut} {lastAbbr} → cutoff: {cut} ({c.FilesDeletedOnCutoffDay} of {c.FilesOnCutoffDay} files from {cutDay})");
    }

    public static string? NothingToDelete(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Request.Mode == CleanupMode.FreeSpace && plan.Delete.IsEmpty && plan.Shortfall is null
            ? $"{VolumeName(plan.CardRoot)} already has {CleanupFormat.Gb(plan.SpaceBefore.FreeBytes)} free; nothing to delete"
            : null;
    }

    public static string? ShortfallLine(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Shortfall is not { } sf) return null;
        return !plan.Request.IncludeNotInLibrary && sf.HeldByNotInLibrary > 0
            ? $"Only {CleanupFormat.Gb(sf.FreeableBytes)} can be freed; {CleanupFormat.Gb(sf.HeldByNotInLibrary)} is held by files that are not in your library"
            : $"Only {CleanupFormat.Gb(sf.FreeableBytes)} can be freed; {CleanupFormat.Gb(sf.HeldByNever)} is held by files uas-sort never deletes (unknown files, changed files, DJI system files)";
    }

    public static string EvidenceSplit(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var listed = plan.Delete.Count(c => c.Source == EvidenceSource.Listed);
        var history = plan.Delete.Count(c => c.Source == EvidenceSource.HistoryOnly);
        var text = string.Create(Inv, $"{listed} in the library listing");
        return history == 0 ? text
            : text + $" · {Plural(history, "photo", "photos")} found only in the history (Lightroom may have moved them)";
    }

    public static string? NeverCopiesLine(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var files = plan.Delete.SelectMany(c => c.NeverCopied).ToList();
        if (files.Count == 0) return null;
        int Ends(string ext) => files.Count(f => f.Class != EntryClass.PhotoTwin && f.RelPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        var twins = files.Count(f => f.Class == EntryClass.PhotoTwin);
        var lrf = Ends(".LRF");
        var srt = Ends(".SRT");
        var covers = Ends(".JPG");
        var recovery = files.Count - twins - lrf - srt - covers;
        var parts = new List<string>();
        if (twins > 0) parts.Add(Plural(twins, "JPG twin", "JPG twins") + " (copying is off in Settings)");
        if (lrf > 0) parts.Add(Plural(lrf, "LRF proxy", "LRF proxies"));
        if (srt > 0) parts.Add(Plural(srt, "SRT caption", "SRT captions"));
        if (covers > 0) parts.Add(Plural(covers, "video cover", "video covers"));
        if (recovery > 0) parts.Add(Plural(recovery, "recovery file", "recovery files"));
        return $"Also deletes {Plural(files.Count, "file", "files")} uas-sort never copies: {string.Join(", ", parts)}";
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupTextsTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/CleanupTexts.cs tests/UasSort.Core.Tests/Cleanup/CleanupTextsTests.cs
git commit -m "feat: add card cleanup summary texts

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 08.9: Confirm and CleanupPlanFixtures

**Files:**
- Create: `src/UasSort.Core/Cleanup/CleanupPlan.Confirm.cs` (namespace `UasSort.Core`: the second partial file of Part 02's `CleanupPlan`)
- Create: `tests/UasSort.Testing/CleanupPlanFixtures.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/CleanupConfirmTests.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/CleanupConfirmFixturesTests.cs`

**Interfaces:**
- Consumes: Part 02's `CleanupPlan` (18-argument internal constructor, properties) and `ConfirmedCleanupPlan` (internal constructor `ConfirmedCleanupPlan(CleanupPlan plan, Guid token, DateTime confirmedUtc)`; `Plan`, `Token`, `ConfirmedUtc`, `FilePaths`, `SetFolders`, `NotInLibraryConfirmed`, the path sets derived with `PathRules.Join(plan.CardRoot, rel)`, case-insensitive), `CleanupAck`, `CleanupEligibility`, `PathRules.Join`, `FakeLayout.CardSpace`/`CardId` (Part 02); `CleanupFingerprint` (08.5), `CleanupPaths` (08.1); `TimeProvider`. Both internal constructors are reached from `UasSort.Testing` through Part 02's `InternalsVisibleTo("UasSort.Testing")`.
- Produces:
  - `public ConfirmedCleanupPlan CleanupPlan.Confirm(CleanupAck ack, TimeProvider clock)` — Ref §10.6 Confirm; throws `InvalidOperationException` on any failed check. `ConfirmedCleanupPlan` is not redeclared: `Confirm` validates every path, then calls Part 02's internal constructor.
  - `internal static string CleanupPlan.CanonicalFile(string cardRoot, string relPath)` and `CanonicalSetFolder(string cardRoot, string relDir)` (defined here): validation helpers (plain segments, under `DCIM`, set folders only under `DCIM/PANORAMA` or `DCIM/HYPERLAPSE`) that return `PathRules.Join(cardRoot, rel)` — exactly the path Part 02's `ConfirmedCleanupPlan` derives — or throw.
  - `public static class CleanupPlanFixtures` (namespace `UasSort.Testing`, defined here, for Parts 09 and 10): `static ConfirmedCleanupPlan Confirmed(string cardRoot, CardIdentity card, IReadOnlyList<string> fileRelPaths, IReadOnlyList<string> setFolderRelDirs, TimeProvider clock)`; `static CleanupPlan Plan(string cardRoot, CardIdentity card, IReadOnlyList<string> fileRelPaths, IReadOnlyList<string> setFolderRelDirs)` — built through the internal constructors; the files under a named set folder form that set's unit, every other file is its own Evidence unit; the fingerprint is `CleanupFingerprint.Compute` of the plan, so `Confirm` accepts a fixture plan whose paths are under `DCIM`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupConfirmTests.cs
using System.Reflection;
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public class CleanupConfirmTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 12, 19, 30, 0, TimeSpan.Zero));

    private static CleanupAck Ack(CleanupPlan p, bool cantBeRecovered = true, bool? includes = null, ImmutableHashSet<ItemId>? nil = null)
    {
        var units = nil ?? p.Delete.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary).Select(c => c.Unit).ToImmutableHashSet();
        return new CleanupAck(p.Fingerprint, cantBeRecovered, includes ?? units.Count > 0, units);
    }

    private static (CleanupScenario S, ItemId Evidence, ItemId Nil, ItemId Set) Card()
    {
        var s = new CleanupScenario();
        var e = s.AddVideo(Utc(2026, 7, 20, 20, 0), companions: Comp.Lrf);
        var n = s.AddVideo(Utc(2026, 7, 21, 20, 0), AuditCategory.Unaccounted, listed: false);
        var set = s.AddSet("001_0087", Utc(2026, 7, 22, 20, 0));
        return (s, e, n, set);
    }

    /// <summary>The same plan with another Delete list, through Part 02's 18-argument internal constructor.</summary>
    private static CleanupPlan Rebuild(CleanupPlan p, ImmutableArray<CleanupCandidate> delete, string? fingerprint = null)
    {
        var allocated = delete.Sum(c => c.AllocatedBytes);
        return new(p.PlanId, p.Card, p.CardRoot, p.InventoryHash, p.CameraModel, p.Request, p.SpaceBefore, delete,
                   p.NotInLibraryInScope, p.Rows, p.Undecided, p.NotDeletable, p.Cutoff, delete.Sum(c => c.Files.Length), allocated,
                   Math.Min(p.SpaceBefore.TotalBytes, p.SpaceBefore.FreeBytes + allocated), p.Shortfall,
                   fingerprint ?? CleanupFingerprint.Compute(p.Request, p.SpaceBefore, delete));
    }

    [Fact]
    public void Confirm_returns_canonical_paths_set_folders_and_per_unit_tokens()
    {
        var (s, _, n, _) = Card();
        var plan = s.Build(Before(2026, 7, 26, include: true));
        var confirmed = plan.Confirm(Ack(plan), Clock);
        Assert.Same(plan, confirmed.Plan);
        Assert.NotEqual(Guid.Empty, confirmed.Token);
        Assert.Equal(new DateTime(2026, 10, 12, 19, 30, 0, DateTimeKind.Utc), confirmed.ConfirmedUtc);
        Assert.Equal(6, confirmed.FilePaths.Count);                              // LRF + MP4, MP4, 3 members
        Assert.True(confirmed.FilePaths.Contains(@"E:\DCIM\DJI_001\DJI_20260720120000_0101_D.MP4"));
        Assert.True(confirmed.FilePaths.Contains(@"e:\dcim\dji_001\dji_20260720120000_0101_d.lrf"));   // case-insensitive
        Assert.Equal(new[] { @"E:\DCIM\PANORAMA\001_0087" }, confirmed.SetFolders);
        Assert.Equal(new[] { n }, confirmed.NotInLibraryConfirmed);
        Assert.NotEqual(plan.Confirm(Ack(plan), Clock).Token, confirmed.Token);
    }

    [Fact]
    public void Confirm_refuses_a_missing_or_wrong_acknowledgement()
    {
        var (s, _, n, _) = Card();
        var plan = s.Build(Before(2026, 7, 26, include: true));
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan) with { PlanFingerprint = "0000000000000000" }, Clock));
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan, cantBeRecovered: false), Clock));
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan, nil: []), Clock));                    // row decision missing
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan, includes: false), Clock));            // box unticked
        var evidenceOnly = s.Build(Before(2026, 7, 26));
        Assert.Throws<InvalidOperationException>(() => evidenceOnly.Confirm(Ack(evidenceOnly, includes: true, nil: []), Clock));
        Assert.Throws<InvalidOperationException>(() => evidenceOnly.Confirm(Ack(evidenceOnly, nil: [n]), Clock));
    }

    [Fact]
    public void Confirm_refuses_an_undecided_row_and_an_empty_plan()
    {
        var (s, e, n, set) = Card();
        var undecided = s.Build(Before(2026, 7, 26, include: true), NoRows, new HashSet<ItemId> { e, set });
        Assert.Equal(new[] { n }, undecided.Undecided);
        Assert.Throws<InvalidOperationException>(() => undecided.Confirm(Ack(undecided), Clock));
        var empty = s.Build(Before(2026, 7, 1));
        Assert.Empty(empty.Delete);
        Assert.Throws<InvalidOperationException>(() => empty.Confirm(Ack(empty), Clock));
    }

    [Fact]
    public void Confirm_recomputes_the_fingerprint_and_never_trusts_the_stored_one()
    {
        var (s, _, _, _) = Card();
        var plan = s.Build(Before(2026, 7, 26));
        var tampered = Rebuild(plan, plan.Delete.RemoveAt(0), plan.Fingerprint);
        Assert.Throws<InvalidOperationException>(() => tampered.Confirm(Ack(plan), Clock));
    }

    [Fact]
    public void Confirm_refuses_never_candidates_paths_outside_dcim_and_non_set_folders()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 20, 20, 0));
        s.AddVideo(Utc(2026, 7, 21, 20, 0), probeError: "bad moov");
        var plan = s.Build(Before(2026, 7, 26));
        var never = s.Candidates().Single(c => c.Eligibility == CleanupEligibility.Never);
        var ok = plan.Delete[0];

        var withNever = Rebuild(plan, [ok, never]);
        Assert.Throws<InvalidOperationException>(() => withNever.Confirm(Ack(withNever), Clock));

        var misc = ok with { Files = [ok.Files[0] with { RelPath = "MISC/FC9113.db" }] };
        var outside = Rebuild(plan, [misc]);
        Assert.Throws<InvalidOperationException>(() => outside.Confirm(Ack(outside), Clock));

        var dots = ok with { Files = [ok.Files[0] with { RelPath = "DCIM/../Windows/explorer.exe" }] };
        var escaping = Rebuild(plan, [dots]);
        Assert.Throws<InvalidOperationException>(() => escaping.Confirm(Ack(escaping), Clock));

        var notSet = ok with { SetFolder = "DCIM/DJI_001" };
        var badFolder = Rebuild(plan, [notSet]);
        Assert.Throws<InvalidOperationException>(() => badFolder.Confirm(Ack(badFolder), Clock));
    }

    [Theory]
    [InlineData(typeof(CleanupPlan))]
    [InlineData(typeof(ConfirmedCleanupPlan))]
    public void Plans_are_sealed_classes_without_public_constructors_or_with(Type t)
    {
        Assert.True(t.IsClass && t.IsSealed);
        Assert.Empty(t.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.All(t.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance), c => Assert.True(c.IsAssembly));
        Assert.Null(t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
    }
}
```

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupConfirmFixturesTests.cs
namespace UasSort.Core.Tests.Cleanup;

public class CleanupConfirmFixturesTests
{
    private const string Proxy = "DCIM/DJI_001/DJI_20260726035000_0001_D.LRF";
    private const string Clip = "DCIM/DJI_001/DJI_20260726035000_0001_D.MP4";
    private const string Set = "DCIM/PANORAMA/001_0087";

    [Fact]
    public void Fixture_plan_names_exactly_the_canonical_paths_the_guard_checks()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 19, 30, 0, TimeSpan.Zero));
        var c = CleanupPlanFixtures.Confirmed(FakeLayout.CardRoot, FakeLayout.CardId,
            [Proxy, Clip, $"{Set}/PANO_0001.DNG", $"{Set}/PANO_0002.DNG"], [Set], clock);

        Assert.Equal(new[]
        {
            @"E:\DCIM\DJI_001\DJI_20260726035000_0001_D.LRF", @"E:\DCIM\DJI_001\DJI_20260726035000_0001_D.MP4",
            @"E:\DCIM\PANORAMA\001_0087\PANO_0001.DNG", @"E:\DCIM\PANORAMA\001_0087\PANO_0002.DNG",
        }, c.FilePaths.Order(StringComparer.Ordinal));
        Assert.True(c.FilePaths.Contains(@"e:\dcim\dji_001\dji_20260726035000_0001_d.lrf"));       // case-insensitive
        Assert.Equal(@"E:\DCIM\PANORAMA\001_0087", Assert.Single(c.SetFolders));
        Assert.Empty(c.NotInLibraryConfirmed);
        Assert.Equal(new DateTime(2026, 10, 12, 19, 30, 0, DateTimeKind.Utc), c.ConfirmedUtc);
        Assert.Equal((FakeLayout.CardRoot, FakeLayout.CardId), (c.Plan.CardRoot, c.Plan.Card));
        Assert.Equal(3, c.Plan.Delete.Length);                                  // the set, then the LRF and the MP4
        Assert.Equal(4, c.Plan.FileCount);
        Assert.Equal(CleanupFingerprint.Compute(c.Plan.Request, c.Plan.SpaceBefore, c.Plan.Delete), c.Plan.Fingerprint);

        var again = c.Plan.Confirm(new CleanupAck(c.Plan.Fingerprint, true, false, []), clock);   // Confirm accepts a fixture plan
        Assert.True(again.FilePaths.SetEquals(c.FilePaths));
        Assert.True(again.SetFolders.SetEquals(c.SetFolders));
    }

    [Fact]
    public void Plan_fixture_with_no_set_folder_makes_one_unit_per_file()
    {
        var plan = CleanupPlanFixtures.Plan(@"F:\", FakeLayout.CardId, [Clip], []);
        var unit = Assert.Single(plan.Delete);
        Assert.Equal(Clip, unit.Unit.CardRelPath);
        Assert.Null(unit.SetFolder);
        Assert.Equal(CleanupEligibility.Evidence, unit.Eligibility);
        Assert.Equal(@"F:\", plan.CardRoot);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupConfirm*"`
Expected: build fails with `CS1061: 'CleanupPlan' does not contain a definition for 'Confirm'` and `CS0103: The name 'CleanupPlanFixtures' does not exist in the current context`.

- [ ] **Step 3: Implement**

`ConfirmedCleanupPlan` is Part 02's and is not redeclared; `Confirm` validates, then calls its internal constructor, which derives `FilePaths`/`SetFolders` with `PathRules.Join` — the same paths the validation helpers below return.

```csharp
// src/UasSort.Core/Cleanup/CleanupPlan.Confirm.cs
namespace UasSort.Core;

public sealed partial class CleanupPlan
{
    /// <summary>The only factory of ConfirmedCleanupPlan (Ref §10.6 Confirm). Recomputes the fingerprint; throws on a VM bug.</summary>
    public ConfirmedCleanupPlan Confirm(CleanupAck ack, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(ack);
        ArgumentNullException.ThrowIfNull(clock);
        var recomputed = CleanupFingerprint.Compute(Request, SpaceBefore, Delete);
        if (!string.Equals(recomputed, Fingerprint, StringComparison.Ordinal)
            || !string.Equals(recomputed, ack.PlanFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The cleanup plan changed after it was shown; acknowledge it again.");
        if (!ack.CantBeRecovered) throw new InvalidOperationException("\"Files deleted from a memory card can't be recovered.\" is not ticked.");
        if (Delete.IsEmpty) throw new InvalidOperationException("The cleanup plan deletes nothing.");
        if (!Undecided.IsEmpty) throw new InvalidOperationException("Some review rows are still undecided.");
        if (Delete.Any(c => c.Eligibility == CleanupEligibility.Never))
            throw new InvalidOperationException("The cleanup plan holds a file uas-sort never deletes.");

        var notInLibrary = Delete.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary).Select(c => c.Unit).ToHashSet();
        if (!notInLibrary.SetEquals(ack.NotInLibraryDelete))
            throw new InvalidOperationException("Every file not in the library needs its own Delete row.");
        if (notInLibrary.Count > 0 && !Request.IncludeNotInLibrary)
            throw new InvalidOperationException("Files not in the library are in the plan while the switch is off.");
        if (ack.IncludesNotInLibrary != (notInLibrary.Count > 0))
            throw new InvalidOperationException("\"Includes files not proven to be in your library.\" does not match the plan.");

        foreach (var c in Delete)
        {
            foreach (var f in c.Files) _ = CanonicalFile(CardRoot, f.RelPath);
            if (c.SetFolder is { } folder) _ = CanonicalSetFolder(CardRoot, folder);
        }
        return new ConfirmedCleanupPlan(this, Guid.NewGuid(), clock.GetUtcNow().UtcDateTime);   // Part 02 derives the path sets
    }

    /// <summary>Validates a card file path (plain segments, under DCIM) and returns PathRules.Join(cardRoot, relPath) — the path
    /// ConfirmedCleanupPlan derives for it — or throws.</summary>
    internal static string CanonicalFile(string cardRoot, string relPath)
    {
        if (CleanupPaths.Full(cardRoot, relPath) is null)
            throw new InvalidOperationException($"Not a plain card path: {relPath}");
        var full = PathRules.Join(cardRoot, relPath);
        if (!full.StartsWith(PathRules.Join(cardRoot, "DCIM") + "\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Outside the card's DCIM folder: {relPath}");
        return full;
    }

    /// <summary>"&lt;CardRoot&gt;\DCIM\PANORAMA\&lt;set&gt;" or "…\HYPERLAPSE\&lt;set&gt;" (PathRules.Join), or throws.</summary>
    internal static string CanonicalSetFolder(string cardRoot, string relDir)
    {
        var segments = CleanupPaths.Rel(relDir).Split('/');
        if (segments.Length != 3 || !segments[0].Equals("DCIM", StringComparison.OrdinalIgnoreCase)
            || !(segments[1].Equals("PANORAMA", StringComparison.OrdinalIgnoreCase)
                 || segments[1].Equals("HYPERLAPSE", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Not a set folder: {relDir}");
        return CanonicalFile(cardRoot, relDir);
    }
}
```

```csharp
// tests/UasSort.Testing/CleanupPlanFixtures.cs
namespace UasSort.Testing;

/// <summary>Minimal CleanupPlan / ConfirmedCleanupPlan for eraser and view-model tests (Parts 09, 10), built through Core's
/// internal constructors (InternalsVisibleTo UasSort.Testing, Part 02). The files under a named set folder form that set's
/// unit; every other file is its own Evidence unit. No Confirm checks run, so a Platform test may use any card root.</summary>
public static class CleanupPlanFixtures
{
    private static readonly DateTime T = new(2026, 7, 26, 7, 50, 0, DateTimeKind.Utc);
    private const long FileSize = 1_000;

    public static CleanupPlan Plan(string cardRoot, CardIdentity card, IReadOnlyList<string> fileRelPaths,
                                   IReadOnlyList<string> setFolderRelDirs)
    {
        ArgumentNullException.ThrowIfNull(cardRoot);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(fileRelPaths);
        ArgumentNullException.ThrowIfNull(setFolderRelDirs);
        var files = fileRelPaths.Select(CleanupPaths.Rel).ToList();
        var sets = setFolderRelDirs.Select(CleanupPaths.Rel).ToList();
        var delete = new List<CleanupCandidate>();
        foreach (var set in sets)
            delete.Add(Candidate(set, [.. files.Where(f => CleanupPaths.IsUnder(f, set) && !f.Equals(set, StringComparison.OrdinalIgnoreCase))], set));
        foreach (var f in files.Where(f => !sets.Exists(set => CleanupPaths.IsUnder(f, set))))
            delete.Add(Candidate(f, [f], null));

        var space = FakeLayout.CardSpace;
        var request = new CleanupRequest(CleanupMode.BeforeDate, new DateOnly(2026, 7, 26), null, false);
        var allocated = delete.Sum(c => c.AllocatedBytes);
        return new CleanupPlan("fixture-plan", card, cardRoot, "0000000000000000", null, request, space, [.. delete], [],
            new CleanupRows([], []), [], [], new CleanupCutoff(new DateOnly(2026, 7, 26), null, null, null, 0, 0, null),
            delete.Sum(c => c.Files.Length), allocated, Math.Min(space.TotalBytes, space.FreeBytes + allocated), null,
            CleanupFingerprint.Compute(request, space, delete));
    }

    public static ConfirmedCleanupPlan Confirmed(string cardRoot, CardIdentity card, IReadOnlyList<string> fileRelPaths,
                                                 IReadOnlyList<string> setFolderRelDirs, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new ConfirmedCleanupPlan(Plan(cardRoot, card, fileRelPaths, setFolderRelDirs), Guid.NewGuid(),
                                        clock.GetUtcNow().UtcDateTime);
    }

    private static CleanupCandidate Candidate(string unitRelPath, IReadOnlyList<string> fileRelPaths, string? setFolder)
    {
        var cls = setFolder is null ? EntryClass.Video : EntryClass.SetMember;
        var files = fileRelPaths.Select(p => new CardEntry(p, FileSize, T, T, T, 0x20, cls, null)).ToImmutableArray();
        return new CleanupCandidate(new ItemId(unitRelPath), files,
            files.Length * CleanupPaths.Allocated(FileSize, FakeLayout.CardSpace.ClusterBytes), T, new DateOnly(2026, 7, 25),
            "America/Anchorage", CleanupEligibility.Evidence, AuditCategory.InLedger, "in the history, verified", null, null, null,
            setFolder is null ? ItemKind.Video : ItemKind.Set, T.AddHours(-8), null, setFolder, null, [], EvidenceSource.Listed,
            false, []);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupConfirm*"`
Expected: PASS (`CleanupConfirmTests` 7 cases + `CleanupConfirmFixturesTests` 2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/CleanupPlan.Confirm.cs tests/UasSort.Testing/CleanupPlanFixtures.cs tests/UasSort.Core.Tests/Cleanup/CleanupConfirmTests.cs tests/UasSort.Core.Tests/Cleanup/CleanupConfirmFixturesTests.cs
git commit -m "feat: confirm card cleanup plans with re-validation

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 08.10: Cleanup additions to the shared fakes and the card-delete tripwire

**Files:**
- Modify: `tests/UasSort.Testing/FakeFaults.cs` (add `OnCardDelete`)
- Modify: `tests/UasSort.Testing/FakeFileSystem.cs` (add `Touch`)
- Modify: `tests/UasSort.Testing/FakeCard.cs` (`FakeCardEraserFactory`: `AllDeleted`, `OpenUnchecked`; `FakeCardEraser.DeleteFile` invokes `OnCardDelete`)
- Create: `tests/UasSort.Testing/Disposer.cs`
- Create: `tests/UasSort.Core.Tests/Cleanup/CleanupScenario.Env.cs`
- Test: `tests/UasSort.Core.Tests/Testing/CleanupFakeAdditionsTests.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/CleanupTripwireTests.cs`

**Interfaces:**
- Consumes (Part 02, namespace `UasSort.Testing`): `FakeFileSystem` (`AddCardVolume`, `SetCardIdentity`, `AddDirectory`, `AddFile`, `SetAttributes`, `Exists`, `Metadata`, `RemoveUnguarded`, `Guard`, `GuardLog`, `CardDeleteViolations`, `HydrationViolations`, `AssertNoViolations`, `Enumerate`, and the assembly-internal `Gate`, `Require`, `Volume`, `ChildrenOf`, `RemoveTree`, `FakeNode`), `FakeFaults` (`CardRemoved`, `IdentityOnCall`, `DeleteErrors`, `DeletePending`), `FakeLayout` (`NewFileSystem`, `Context`, `Settings`, `CardRoot`, `CardId`, `CardSpace`, `Machine`), `FakeCardEraserFactory` (`VolumeVerified`, `OpenCount`), `FakeCardEraser` (`Deleted`, folders end in `/`); `FakeLedgerStore` (Part 06, completed by Part 07); `IoOp.CardDelete`, `GuardContext`, `CardSource`, `EraseOk`/`EraseError`, `UnsafeIoException`, `PathRules` (Part 02); `ConfirmedCleanupPlan` via `CleanupScenario.Confirmed` (08.9); `CleanupPlanFixtures` (08.9).
- Produces:
  - Added in place to Part 02's fakes (registry "Part 08 adds"): `Action<string>? FakeFaults.OnCardDelete` — called by `FakeCardEraser.DeleteFile` with the `/` card path once the guard has allowed the delete and before `DeleteErrors` or the delete itself apply (so a test can swap, remove or cancel between two deletes, or make a delete error coincide with the card vanishing); `void FakeFileSystem.Touch(string path, long? size = null, DateTime? mtimeUtc = null)` — changes a file in place, as another program writing it would (unguarded, tests only); `IReadOnlyList<string> FakeCardEraserFactory.AllDeleted` — every path deleted by every eraser this factory opened, in order (`/` card paths; removed folders end in `/`); `ICardEraser FakeCardEraserFactory.OpenUnchecked(GuardContext ctx)` — an eraser bound to any context (tripwire tests only; not counted in `OpenCount`).
  - `public sealed class Disposer(Action onDispose) : IDisposable` (namespace `UasSort.Testing`, defined here; runs the action once; Part 10 uses it).
  - `CleanupScenario.MakeCard()` → `FakeFileSystem` (`FakeLayout.NewFileSystem()` plus the card volume `E:\` with this scenario's `Space` and every card entry, plus the library files the scenario lists; the same object is the library `IDirectoryLister`, so there is no separate `MakeLister`), `CleanupScenario.MakeLedgerStore()` → in-memory `FakeLedgerStore` over `Ledger()` with `StatusOverride = Ledger().Status`, and `CleanupScenario.Confirmed(CleanupRequest, CleanupRows?)` (Core.Tests).
  - Knob mapping from the old cleanup-only fakes (deleted; there is no `UasSort.Testing.Cleanup` namespace): identity → `fs.SetCardIdentity`; removed → `Faults.CardRemoved`; write-protected → the `CardSource.IsWriteProtected` passed to `Open` (and `Faults.DeleteErrors[path] = 19` mid-run); verified volume → `VolumeVerified`; card file opens → `fs.GuardLog` filtered on `ReadData`; erasers opened → `OpenCount`; deleted files and removed folders → `AllDeleted` (folders end in `/`); per-delete hook → `Faults.OnCardDelete`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/UasSort.Core.Tests/Testing/CleanupFakeAdditionsTests.cs
namespace UasSort.Core.Tests.Testing;

public class CleanupFakeAdditionsTests
{
    private static readonly DateTime T = new(2026, 7, 20, 20, 1, 30, DateTimeKind.Utc);
    private const string Clip = "DCIM/DJI_001/DJI_20260720120000_0101_D.MP4";
    private static readonly string ClipFull = PathRules.Join(FakeLayout.CardRoot, Clip);
    private static readonly CardSource Detected = new(FakeLayout.CardRoot, FakeLayout.CardId, false, false);

    [Fact]
    public void Touch_changes_size_and_mtime_in_place()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(ClipFull, 1_000, T);
        fs.Touch(ClipFull, mtimeUtc: T.AddMinutes(1));
        Assert.Equal((1_000L, T.AddMinutes(1)), (fs.Metadata(ClipFull)!.Size, fs.Metadata(ClipFull)!.MtimeUtc));
        fs.Touch(ClipFull, size: 2_000);
        Assert.Equal((2_000L, T.AddMinutes(1)), (fs.Metadata(ClipFull)!.Size, fs.Metadata(ClipFull)!.MtimeUtc));
    }

    [Fact]
    public void OnCardDelete_runs_after_the_guard_and_before_the_delete_and_AllDeleted_spans_every_eraser()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(ClipFull, 1_000, T);
        fs.AddFile(@"E:\DCIM\DJI_001\other.MP4", 1_000, T);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 19, 30, 0, TimeSpan.Zero));
        var plan = CleanupPlanFixtures.Confirmed(FakeLayout.CardRoot, FakeLayout.CardId, [Clip], [], clock);
        var factory = new FakeCardEraserFactory(fs);
        var seen = new List<(string Path, bool StillOnCard)>();
        fs.Faults.OnCardDelete = p => seen.Add((p, fs.Exists(PathRules.Join(FakeLayout.CardRoot, p))));

        using (var eraser = factory.Open(Detected, FakeLayout.CardId, plan))
            Assert.True(eraser.DeleteFile(Clip) is EraseOk);
        Assert.Equal(Clip, Assert.Single(seen).Path);
        Assert.True(seen[0].StillOnCard);                                     // called before the delete applies
        Assert.Equal(new[] { Clip }, factory.AllDeleted);

        using (var raw = factory.OpenUnchecked(FakeLayout.Context()))           // no plan: the guard refuses first
            Assert.Throws<UnsafeIoException>(() => raw.DeleteFile("DCIM/DJI_001/other.MP4"));
        Assert.Single(seen);                                                  // a refused delete never reaches the hook
        Assert.Equal(1, factory.OpenCount);
        Assert.Single(fs.CardDeleteViolations);
    }

    [Fact]
    public void Disposer_runs_its_action_once()
    {
        var n = 0;
        var d = new Disposer(() => n++);
        d.Dispose();
        d.Dispose();
        Assert.Equal(1, n);
    }
}
```

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupScenario.Env.cs
namespace UasSort.Core.Tests.Cleanup;

internal sealed partial class CleanupScenario
{
    /// <summary>The fake PC for a cleanup run: FakeLayout.NewFileSystem() with card volume E:\ (this scenario's Space) holding
    /// every card entry, and the library files the scenario lists. The same FakeFileSystem is the library lister.</summary>
    public FakeFileSystem MakeCard()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddCardVolume(CardRoot, Identity, Space);
        foreach (var e in Entries)
        {
            var full = PathRules.Join(CardRoot, e.RelPath);
            if ((e.RawAttributes & FakeFileSystem.DirectoryAttribute) != 0) fs.AddDirectory(full);
            else fs.AddFile(full, e.Size, e.MtimeUtc, e.RawAttributes);
        }
        foreach (var e in VideoListing.Concat(PhotoListing)) fs.AddFile(e.FullPath, e.Size, e.MtimeUtc, e.RawAttributes);
        return fs;
    }

    /// <summary>In-memory FakeLedgerStore over this scenario's ledger; Check() returns its (Ok) status until a test overrides it.</summary>
    public FakeLedgerStore MakeLedgerStore()
        => new(null, VideoRoot, FakeLayout.Machine, Ledger()) { StatusOverride = Ledger().Status };

    /// <summary>Build + Confirm with every acknowledgement given (the rows left as Build decided them).</summary>
    public ConfirmedCleanupPlan Confirmed(CleanupRequest request, CleanupRows? rows = null)
    {
        var plan = Build(request, rows);
        var nil = plan.Delete.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary).Select(c => c.Unit).ToImmutableHashSet();
        return plan.Confirm(new CleanupAck(plan.Fingerprint, true, nil.Count > 0, nil), new FakeTimeProvider(new DateTimeOffset(ScanUtc)));
    }
}
```

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupTripwireTests.cs
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public class CleanupTripwireTests
{
    private static (CleanupScenario S, ItemId Old, ItemId Set) Card()
    {
        var s = new CleanupScenario();
        var old = s.AddVideo(Utc(2026, 7, 20, 20, 0), companions: Comp.Lrf);
        s.AddVideo(Utc(2026, 7, 28, 20, 0));                                 // after the cutoff: not in the plan
        var set = s.AddSet("001_0087", Utc(2026, 7, 21, 20, 0));
        s.AddLoose("MISC/FC9113.db", EntryClass.Skip);
        return (s, old, set);
    }

    private static CardSource Detected => new(CardRoot, Identity, IsBrowsedFolder: false, IsWriteProtected: false);

    private static bool OnCard(FakeFileSystem fs, string cardRelPath) => fs.Exists(PathRules.Join(CardRoot, cardRelPath));

    [Fact]
    public void Eraser_deletes_only_what_the_confirmed_plan_names()
    {
        var (s, old, _) = Card();
        var fs = s.MakeCard();
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        using var eraser = new FakeCardEraserFactory(fs).Open(Detected, Identity, confirmed);

        Assert.True(eraser.DeleteFile(old.CardRelPath) is EraseOk);   // EraseResult is a union: match, don't IsType
        Assert.Empty(fs.CardDeleteViolations);

        Assert.Throws<UnsafeIoException>(() => eraser.DeleteFile("DCIM/DJI_001/DJI_20260728120000_0102_D.MP4"));
        Assert.Throws<UnsafeIoException>(() => eraser.DeleteFile("MISC/FC9113.db"));
        Assert.Throws<UnsafeIoException>(() => eraser.RemoveEmptySetFolder("DCIM/DJI_001"));
        Assert.Throws<UnsafeIoException>(() => eraser.RemoveEmptySetFolder("DCIM"));
        Assert.Throws<UnsafeIoException>(() => eraser.RemoveEmptySetFolder("MISC"));
        Assert.Equal(5, fs.CardDeleteViolations.Count);
        Assert.All(fs.CardDeleteViolations, v => Assert.Equal(IoOp.CardDelete, v.Op));
        Assert.True(OnCard(fs, "DCIM/DJI_001/DJI_20260728120000_0102_D.MP4"));
        Assert.True(OnCard(fs, "MISC/FC9113.db"));
    }

    [Fact]
    public void A_named_set_folder_is_removed_only_once_empty()
    {
        var (s, _, set) = Card();
        var fs = s.MakeCard();
        var factory = new FakeCardEraserFactory(fs);
        using var eraser = factory.Open(Detected, Identity, s.Confirmed(Before(2026, 7, 26)));
        Assert.True(eraser.RemoveEmptySetFolder(set.CardRelPath) is EraseError { Win32Error: 145 });
        foreach (var m in new[] { "PANO_0001.DNG", "PANO_0002.DNG", "PANO_0003.DNG" })
            Assert.True(eraser.DeleteFile($"{set.CardRelPath}/{m}") is EraseOk);
        Assert.True(eraser.RemoveEmptySetFolder(set.CardRelPath) is EraseOk);
        Assert.Equal(new[] { "DCIM/PANORAMA/001_0087/" }, factory.AllDeleted.Where(p => p.EndsWith('/')));
        Assert.False(OnCard(fs, set.CardRelPath));
        Assert.Empty(fs.CardDeleteViolations);
    }

    [Fact]
    public void Any_context_without_a_verified_volume_and_a_plan_trips_the_wire()
    {
        var (s, old, _) = Card();
        var fs = s.MakeCard();
        var factory = new FakeCardEraserFactory(fs);
        var confirmed = s.Confirmed(Before(2026, 7, 26));

        using (var noPlan = factory.OpenUnchecked(FakeLayout.Context() with { CardIsVerifiedCardVolume = true }))
            Assert.Throws<UnsafeIoException>(() => noPlan.DeleteFile(old.CardRelPath));
        using (var unverified = factory.OpenUnchecked(FakeLayout.Context() with { Cleanup = confirmed }))
            Assert.Throws<UnsafeIoException>(() => unverified.DeleteFile(old.CardRelPath));
        Assert.Equal(2, fs.CardDeleteViolations.Count);
        Assert.True(OnCard(fs, old.CardRelPath));
        Assert.Empty(factory.AllDeleted);
    }

    [Fact]
    public void The_factory_refuses_browsed_write_protected_unverified_removed_and_foreign_cards()
    {
        var (s, _, _) = Card();
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        var fs = s.MakeCard();
        var factory = new FakeCardEraserFactory(fs);
        Assert.Throws<UnsafeIoException>(() => factory.Open(Detected with { IsBrowsedFolder = true }, Identity, confirmed));
        Assert.Throws<UnsafeIoException>(() => factory.Open(Detected with { IsWriteProtected = true }, Identity, confirmed));
        factory.VolumeVerified = false;
        Assert.Throws<UnsafeIoException>(() => factory.Open(Detected, Identity, confirmed));
        factory.VolumeVerified = true;
        Assert.Throws<UnsafeIoException>(() => factory.Open(Detected, Identity with { VolumeSerial = 0xDEADBEEF }, confirmed));
        fs.SetCardIdentity(CardRoot, Identity with { VolumeSerial = 0x5E6F7A8B });          // another card in the slot
        Assert.Throws<UnsafeIoException>(() => factory.Open(Detected, Identity, confirmed));
        fs.SetCardIdentity(CardRoot, Identity);
        Assert.Throws<UnsafeIoException>(() => factory.Open(Detected with { Root = @"F:\" }, Identity, confirmed));   // plan is for E:\
        fs.Faults.CardRemoved = true;
        Assert.Throws<UnsafeIoException>(() => factory.Open(Detected, Identity, confirmed));
        Assert.Equal(0, factory.OpenCount);
        Assert.Empty(factory.AllDeleted);
        Assert.Empty(fs.CardDeleteViolations);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupTripwireTests"`
Expected: build fails with `CS1061: 'FakeCardEraserFactory' does not contain a definition for 'OpenUnchecked'` (also `'AllDeleted'`, `'FakeFileSystem' … 'Touch'`, `'FakeFaults' … 'OnCardDelete'`, and `CS0246: The type or namespace name 'Disposer' could not be found`).

- [ ] **Step 3: Implement**

In `tests/UasSort.Testing/FakeFaults.cs`, add to the "lister and eraser" group, after `DeletePending`:

```csharp
    /// <summary>Called by FakeCardEraser.DeleteFile with the '/' card path once the guard has allowed the delete and before
    /// DeleteErrors or the delete itself apply (Part 08: swap, remove or cancel between two deletes).</summary>
    public Action<string>? OnCardDelete { get; set; }
```

In `tests/UasSort.Testing/FakeFileSystem.cs`, add after `RemoveUnguarded`:

```csharp
    /// <summary>Changes a file's size and/or mtime in place, as another program writing it would (Part 08; unguarded, tests only).</summary>
    public void Touch(string path, long? size = null, DateTime? mtimeUtc = null)
    {
        lock (_gate)
        {
            var n = Require(path);
            if (size is { } s)
            {
                n.Bytes = null;
                n.PatternLength = s;
            }
            if (mtimeUtc is { } m) n.MtimeUtc = m;
        }
    }
```

In `tests/UasSort.Testing/FakeCard.cs`, replace the whole `FakeCardEraserFactory` class with:

```csharp
public sealed class FakeCardEraserFactory(FakeFileSystem fs) : ICardEraserFactory
{
    private readonly List<FakeCardEraser> _erasers = [];

    /// <summary>False = one of the Win32 cleanup volume facts fails (not a volume root, wrong bus, no MISC index, …).</summary>
    public bool VolumeVerified { get; set; } = true;
    public int OpenCount { get; private set; }

    /// <summary>Every path deleted by every eraser this factory opened, in order ('/' card paths; removed folders end in '/').</summary>
    public IReadOnlyList<string> AllDeleted => [.. _erasers.SelectMany(e => e.Deleted)];

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
        return Track(new FakeCardEraser(fs, root, fs.Context with { CardRoot = root, CardIsVerifiedCardVolume = true, Cleanup = plan }));
    }

    /// <summary>Tripwire tests only (Part 08): an eraser bound to any context, so a refused context can be exercised.
    /// Not counted in OpenCount.</summary>
    public ICardEraser OpenUnchecked(GuardContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Track(new FakeCardEraser(fs, PathRules.Normalize(ctx.CardRoot ?? FakeLayout.CardRoot), ctx));
    }

    private FakeCardEraser Track(FakeCardEraser eraser)
    {
        _erasers.Add(eraser);
        return eraser;
    }
}
```

and in `FakeCardEraser.DeleteFile`, right after the guard line

```csharp
        _fs.Guard(IoOp.CardDelete, full, _ctx);   // throws UnsafeIoException + records a CardDeleteViolation on refusal
```

add

```csharp
        _fs.Faults.OnCardDelete?.Invoke(key);    // Part 08: outside the lock, before DeleteErrors and the delete apply
```

```csharp
// tests/UasSort.Testing/Disposer.cs
namespace UasSort.Testing;

/// <summary>Runs an action once on Dispose (defined here; Part 10's cleanup VM tests use it).</summary>
public sealed class Disposer(Action onDispose) : IDisposable
{
    private int _done;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _done, 1) == 0) onDispose();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupTripwireTests"`
Expected: PASS (4 tests).

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupFakeAdditionsTests"`
Expected: PASS (3 tests).

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*FakeCardTests"` and `-- --filter-class "*FakeFileSystemTests"`
Expected: PASS (Part 02's 12 and 13 tests unchanged).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Testing/FakeFaults.cs tests/UasSort.Testing/FakeFileSystem.cs tests/UasSort.Testing/FakeCard.cs tests/UasSort.Testing/Disposer.cs tests/UasSort.Core.Tests/Testing/CleanupFakeAdditionsTests.cs tests/UasSort.Core.Tests/Cleanup/CleanupScenario.Env.cs tests/UasSort.Core.Tests/Cleanup/CleanupTripwireTests.cs
git commit -m "test: add cleanup hooks to the shared fakes and the card-delete tripwire tests

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 08.11: CleanupExecutor — preparation, happy path, cardDelete records, closing re-list

**Files:**
- Create: `src/UasSort.Core/Cleanup/CleanupExecutor.cs`
- Create: `src/UasSort.Core/Cleanup/CleanupExecutor.Delete.cs`
- Create: `tests/UasSort.Core.Tests/Cleanup/CleanupExecutorHarness.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/CleanupExecutorTests.cs`

**Interfaces:**
- Consumes: `CleanupEnvironment`, `CleanupResult`, `CleanupProgress`, `CleanupStop`, outcome cases `Deleted`/`CleanupNotStarted`/`CleanupFailed`/`PartiallyDeleted`, `CardDeleteRecord`, `RunCard`, `LedgerFolderState`, `LedgerPaths`, `RootListing`, `LibraryListings`, `DestRoot`, `EraseOk`/`EraseError`, `UnsafeIoException` (Part 02, Ref §3/§4); `CommitTailRecords.Card(CardIdentity, string?, string)` (Part 07 Task 07.11: the `RunCard` of every `cardDelete` record); `ConfirmedCleanupPlan` (Part 02, via `Confirm`, 08.9); `FreshEvidence` (08.4); the shared fakes `FakeFileSystem`, `FakeCardReader`, `FakeCardEraserFactory` (+ 08.10 additions), `FakeLedgerStore`/`FakeLedgerWriter` (`Calls`, `StatusOverride`, `EnsureFolderThrows`, `Writer.Records`, `Writer.ThrowWhen`, `Writer.Disposed`), `FakeOffloadLock` (`HeldElsewhere`, `Holds`), `FakePowerRequest` (`Active`, `Reasons`), `FakeThumbnails` (`ActivePauses`), `ListProgress<T>` (`Items`) (Parts 02, 06, 07).
- Produces:
  - `public static partial class CleanupExecutor { static Task<CleanupResult> RunAsync(ConfirmedCleanupPlan confirmed, CleanupEnvironment env, IProgress<CleanupProgress> progress, CancellationToken ct); internal static string EvidenceText(CleanupCandidate unit, CardEntry file); const int ErrorFileNotFound = 2, ErrorAccessDenied = 5, ErrorWriteProtect = 19, ErrorNotReady = 21, ErrorSharingViolation = 32, ErrorDirNotEmpty = 145, ErrorDeviceNotConnected = 1167; }` (Ref §4.2). Runs on the thread pool; progress at most every 100 ms of `env.Clock` plus a final report.
  - Before the first delete (Ref §10.6 Execution 1–5): offload lock (else `OffloadLockHeld`), keep-awake `"Cleaning up drone card"`, `Thumbnails.Pause()`, `Ledger.Check()` (`CloudOnly`/`Unwritable`/`VideoRootMissing` → `LedgerUnavailable`), `EnsureFolder()` → `SnapshotToBackup(runId)` → `OpenOwn()` (an IO failure → `LedgerUnavailable`), fresh listings of video/photo/previous photo roots with `{".uas-sort"}` excluded and `Ledger.Load()`, then `Erasers.Open` (refusal → `InternalSafetyStop`). After the loop: `Reader.Relist()` (deleted yet listed → `StillListed`) and `Reader.Space()`, then everything is disposed in reverse order. Every unit without an outcome is `CleanupNotStarted`. The record's `Machine` is `Environment.MachineName` (decision 32: the executor is the only Core code that reads it).
  - `Run.EvidenceGone(CleanupCandidate)` — Ref §10.6 step 2 (listings and the ledger only): videos need the fresh listing; photos, twins and set members need the fresh listing or a verified fresh-ledger record when they were listed at plan time, and the verified record when they were `HistoryOnly`.
  - This task's `DeleteUnit` runs step 2, deletes in order, appends one `CardDeleteRecord` right after each delete, and removes an emptied set folder; Task 08.12 replaces it with steps 0–1, the NotInLibrary token check and every failure outcome.
  - Test harness `CleanupExecutorHarness` (Core.Tests): one run's shared fakes built from a scenario (`Fs` = `MakeCard()`, `Reader`, `Erasers`, `Ledger` = `MakeLedgerStore()`, `Lock`, `Power`, `Thumbs`, `Clock`, `Progress`) plus the views `Deleted`, `RemovedDirs`, `OnCard(rel)`, `SetLedgerState(state)`, `CallIndex(name)`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupExecutorHarness.cs
namespace UasSort.Core.Tests.Cleanup;

/// <summary>One cleanup run's shared fakes, built from a scenario after its units were added.</summary>
internal sealed class CleanupExecutorHarness
{
    public CleanupExecutorHarness(CleanupScenario s)
    {
        Scenario = s;
        Fs = s.MakeCard();
        Reader = new FakeCardReader(Fs, CleanupScenario.CardRoot, CleanupScenario.Identity);
        Erasers = new FakeCardEraserFactory(Fs);
        Ledger = s.MakeLedgerStore();
    }

    public CleanupScenario Scenario { get; }
    public FakeFileSystem Fs { get; }                     // card volume E:\, library roots, and the library lister
    public FakeCardReader Reader { get; }
    public FakeCardEraserFactory Erasers { get; }
    public FakeLedgerStore Ledger { get; }
    public FakeOffloadLock Lock { get; } = new();
    public FakePowerRequest Power { get; } = new();
    public FakeThumbnails Thumbs { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 12, 19, 30, 5, TimeSpan.Zero));
    public ListProgress<CleanupProgress> Progress { get; } = new();

    /// <summary>Card files deleted, in order ('/' paths).</summary>
    public IReadOnlyList<string> Deleted => [.. Erasers.AllDeleted.Where(p => !p.EndsWith('/'))];

    /// <summary>Set folders removed, in order ('/' paths without the trailing '/').</summary>
    public IReadOnlyList<string> RemovedDirs => [.. Erasers.AllDeleted.Where(p => p.EndsWith('/')).Select(p => p.TrimEnd('/'))];

    public bool OnCard(string cardRelPath) => Fs.Exists(PathRules.Join(CleanupScenario.CardRoot, cardRelPath));

    public void SetLedgerState(LedgerFolderState state) => Ledger.StatusOverride = Scenario.Ledger().Status with { State = state };

    /// <summary>Index of the first ledger-store call with this name ("SnapshotToBackup" may carry the run id), or -1.</summary>
    public int CallIndex(string name)
        => Ledger.Calls.FindIndex(c => c == name || c.StartsWith(name + " ", StringComparison.Ordinal));

    public CleanupEnvironment Env => new(new CardSource(CleanupScenario.CardRoot, CleanupScenario.Identity, false, false),
        CleanupScenario.Identity, Reader, Thumbs, Erasers, Fs, Ledger, Lock, Power, Clock, Scenario.Settings());

    public Task<CleanupResult> Run(ConfirmedCleanupPlan confirmed, CancellationToken ct = default)
        => CleanupExecutor.RunAsync(confirmed, Env, Progress, ct);
}
```

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupExecutorTests.cs
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public sealed class CleanupExecutorTests : IDisposable
{
    private readonly List<CleanupExecutorHarness> _harnesses = [];

    private CleanupExecutorHarness Harness(CleanupScenario s)
    {
        var h = new CleanupExecutorHarness(s);
        _harnesses.Add(h);
        return h;
    }

    public void Dispose()                                                    // the card-delete and hydration tripwires, at teardown
    {
        foreach (var h in _harnesses) h.Fs.AssertNoViolations();
    }

    private static (CleanupScenario S, ItemId Video, ItemId Set, ItemId Moved) Card()
    {
        var s = new CleanupScenario();
        var video = s.AddVideo(Utc(2026, 7, 20, 20, 0), companions: Comp.Lrf);
        var set = s.AddSet("001_0087", Utc(2026, 7, 21, 20, 0));
        var moved = s.AddPhoto(Utc(2026, 7, 22, 20, 0), AuditCategory.InLedger, listed: false, ledger: VerifyKind.Cached);
        s.AddVideo(Utc(2026, 7, 28, 20, 0));                                 // after the cutoff
        return (s, video, set, moved);
    }

    [Fact]
    public async Task Happy_path_deletes_oldest_first_primary_last_and_releases_everything()
    {
        var (s, video, set, moved) = Card();
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        var heldDuringDeletes = new List<int>();
        h.Fs.Faults.OnCardDelete = _ => heldDuringDeletes.Add(h.Thumbs.ActivePauses * 100 + h.Lock.Holds * 10 + h.Power.Active);

        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);

        Assert.Null(r.Stop);
        Assert.Equal(new[] { video, set, moved }, r.Outcomes.Select(o => o.Unit));
        Assert.All(r.Outcomes, o => Assert.IsType<Deleted>(o));
        Assert.True(((Deleted)r.Outcomes[1]).SetFolderRemoved);
        Assert.Equal(new[] { "DCIM/DJI_001/DJI_20260720120000_0101_D.LRF", "DCIM/DJI_001/DJI_20260720120000_0101_D.MP4",
                      "DCIM/PANORAMA/001_0087/PANO_0001.DNG", "DCIM/PANORAMA/001_0087/PANO_0002.DNG", "DCIM/PANORAMA/001_0087/PANO_0003.DNG",
                      "DCIM/DJI_001/DJI_20260722120000_0102_D.DNG" }, h.Deleted);
        Assert.Equal(new[] { "DCIM/PANORAMA/001_0087" }, h.RemovedDirs);
        Assert.True(h.OnCard("DCIM/DJI_001/DJI_20260728120000_0103_D.MP4"));
        Assert.Equal(6, heldDuringDeletes.Count);
        Assert.All(heldDuringDeletes, v => Assert.Equal(111, v));            // pause, lock and keep-awake held for every delete
        Assert.Equal((0, 0, 0), (h.Thumbs.ActivePauses, h.Lock.Holds, h.Power.Active));
        Assert.Equal(new[] { "Cleaning up drone card" }, h.Power.Reasons);
        var order = new[] { "Check", "EnsureFolder", "SnapshotToBackup", "OpenOwn", "Load" }.Select(h.CallIndex).ToArray();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order(), order);                                  // ledger prepared, then the fresh ledger loaded
        Assert.True(h.Ledger.Writer.Disposed);
        Assert.DoesNotContain(h.Fs.GuardLog, g => g.Op == IoOp.ReadData);    // no card or library file opened
        Assert.Equal(1, h.Erasers.OpenCount);
        Assert.Equal(confirmed.Plan.SpaceBefore.FreeBytes + confirmed.Plan.AllocatedBytes, r.SpaceAfter.FreeBytes);
        Assert.Empty(r.StillListed);
        Assert.Same(confirmed, r.Plan);
        Assert.Equal(new DateTime(2026, 10, 12, 19, 30, 5, DateTimeKind.Utc), r.StartUtc);
        var last = h.Progress.Items[^1];
        Assert.Equal((6, 6), (last.FilesDone, last.FilesTotal));
    }

    [Fact]
    public async Task One_card_delete_record_per_deleted_file_with_its_evidence_and_no_run_record()
    {
        var s = new CleanupScenario { CopyJpgTwin = false };
        var video = s.AddVideo(Utc(2026, 7, 20, 20, 0), companions: Comp.Lrf);
        s.AddSet("001_0087", Utc(2026, 7, 21, 20, 0), members: 1);
        s.AddPhoto(Utc(2026, 7, 22, 20, 0), AuditCategory.InLedger, listed: false, ledger: VerifyKind.Cached);
        s.AddPhoto(Utc(2026, 7, 23, 20, 0), AuditCategory.InLedger, listed: true, twin: true,
                   twinCategory: AuditCategory.SkippedByRule, twinListed: false, ledger: VerifyKind.Unbuffered);
        s.AddVideo(Utc(2026, 7, 24, 20, 0), AuditCategory.Unaccounted, listed: false);
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26, include: true));

        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);

        Assert.Null(r.Stop);
        var records = h.Ledger.Writer.Records.Cast<CardDeleteRecord>().ToList();
        Assert.Equal(h.Deleted, records.Select(x => x.Src));
        Assert.Equal(new[]
        {
            "companionOf:NameSizeMatch", "NameSizeMatch",                    // LRF, then its MP4
            "NameSizeMatch",                                                  // set member
            "historyOnly:InLedger",                                           // photo moved by Lightroom
            "companionOf:InLedger", "InLedger",                               // JPG twin (copying off), then its DNG
            "notInLibraryConfirmed",                                          // reviewed new clip
        }, records.Select(x => x.Evidence));
        Assert.DoesNotContain(h.Ledger.Writer.Records, x => x is RunRecord);
        var mp4 = records[1];
        Assert.Equal((1, "DJI_20260720120000_0101_D.MP4", 1_000_000_000L), (mp4.V, mp4.Name, mp4.Size));
        Assert.Equal(video.CardRelPath, mp4.Unit);
        Assert.Equal(Utc(2026, 7, 20, 20, 0), mp4.CaptureUtc);
        Assert.Equal("same name and size in the library", mp4.Reason);
        Assert.Equal("beforeDate", mp4.Mode);
        Assert.Equal(CommitTailRecords.Card(Identity, "FC9113", "9f3c0a6d12e4b7a1"), mp4.Card);
        Assert.Equal(new RunCard("1A2B3C4D", null, "exFAT", "FC9113", "9f3c0a6d12e4b7a1"), mp4.Card);
        Assert.Equal(r.RunId, mp4.Run);
        Assert.Equal(Environment.MachineName, mp4.Machine);
        Assert.Equal(new DateTime(2026, 10, 12, 19, 30, 5, DateTimeKind.Utc), mp4.At);
        Assert.Null(mp4.Set);
        Assert.Equal("001_0087", records[2].Set);
        Assert.Equal(records.Count, records.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public async Task A_deleted_file_that_stays_listed_is_reported_as_still_listed()
    {
        var (s, video, _, _) = Card();
        var h = Harness(s);
        h.Fs.Faults.DeletePending.Add("DCIM/DJI_001/DJI_20260720120000_0101_D.MP4");
        var r = await h.Run(s.Confirmed(Before(2026, 7, 26)), TestContext.Current.CancellationToken);
        Assert.IsType<Deleted>(r.Outcomes.Single(o => o.Unit == video));
        Assert.Equal(new[] { "DCIM/DJI_001/DJI_20260720120000_0101_D.MP4" }, r.StillListed);
    }

    [Fact]
    public async Task Offload_lock_held_elsewhere_deletes_nothing()
    {
        var (s, _, _, _) = Card();
        var h = Harness(s);
        h.Lock.HeldElsewhere = true;
        var r = await h.Run(s.Confirmed(Before(2026, 7, 26)), TestContext.Current.CancellationToken);
        Assert.Equal(CleanupStop.OffloadLockHeld, r.Stop);
        Assert.All(r.Outcomes, o => Assert.IsType<CleanupNotStarted>(o));
        Assert.Empty(h.Deleted);
        Assert.Empty(h.Ledger.Calls);
        Assert.Empty(h.Power.Reasons);
        Assert.Equal(0, h.Thumbs.ActivePauses);
    }

    [Theory]
    [InlineData(LedgerFolderState.CloudOnly)]
    [InlineData(LedgerFolderState.Unwritable)]
    [InlineData(LedgerFolderState.VideoRootMissing)]
    public async Task Unavailable_ledger_folder_deletes_nothing(LedgerFolderState state)
    {
        var (s, _, _, _) = Card();
        var h = Harness(s);
        h.SetLedgerState(state);
        var r = await h.Run(s.Confirmed(Before(2026, 7, 26)), TestContext.Current.CancellationToken);
        Assert.Equal(CleanupStop.LedgerUnavailable, r.Stop);
        Assert.All(r.Outcomes, o => Assert.IsType<CleanupNotStarted>(o));
        Assert.Empty(h.Deleted);
        Assert.Equal(new[] { "Check" }, h.Ledger.Calls);
        Assert.Equal((0, 0, 0), (h.Thumbs.ActivePauses, h.Lock.Holds, h.Power.Active));
    }

    [Fact]
    public async Task Missing_ledger_folder_is_created_before_the_writer_is_opened()
    {
        var (s, _, _, _) = Card();
        var h = Harness(s);
        h.SetLedgerState(LedgerFolderState.Missing);
        var r = await h.Run(s.Confirmed(Before(2026, 7, 26)), TestContext.Current.CancellationToken);
        Assert.Null(r.Stop);
        Assert.True(h.CallIndex("EnsureFolder") < h.CallIndex("OpenOwn"));
        Assert.True(h.CallIndex("SnapshotToBackup") < h.CallIndex("OpenOwn"));
    }

    [Fact]
    public async Task Failing_to_create_the_ledger_folder_stops_before_the_first_delete()
    {
        var (s, _, _, _) = Card();
        var h = Harness(s);
        h.SetLedgerState(LedgerFolderState.Missing);
        h.Ledger.EnsureFolderThrows = true;
        var r = await h.Run(s.Confirmed(Before(2026, 7, 26)), TestContext.Current.CancellationToken);
        Assert.Equal(CleanupStop.LedgerUnavailable, r.Stop);
        Assert.Empty(h.Deleted);
        Assert.Equal(-1, h.CallIndex("OpenOwn"));
    }

    [Fact]
    public async Task Eraser_factory_refusal_is_an_internal_safety_stop_with_nothing_deleted()
    {
        var (s, _, _, _) = Card();
        var h = Harness(s);
        h.Erasers.VolumeVerified = false;
        var r = await h.Run(s.Confirmed(Before(2026, 7, 26)), TestContext.Current.CancellationToken);
        Assert.Equal(CleanupStop.InternalSafetyStop, r.Stop);
        Assert.All(r.Outcomes, o => Assert.IsType<CleanupNotStarted>(o));
        Assert.Empty(h.Deleted);
        Assert.Equal((0, 0, 0), (h.Thumbs.ActivePauses, h.Lock.Holds, h.Power.Active));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupExecutorTests"`
Expected: build fails with `CS0103: The name 'CleanupExecutor' does not exist in the current context`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Cleanup/CleanupExecutor.cs
namespace UasSort.Core.Cleanup;

/// <summary>Ref §10.6 Execution: owns the whole run. The rescan and the report belong to CleanupVm (Part 10).</summary>
public static partial class CleanupExecutor
{
    public const int ErrorFileNotFound = 2, ErrorAccessDenied = 5, ErrorWriteProtect = 19, ErrorNotReady = 21,
                     ErrorSharingViolation = 32, ErrorDirNotEmpty = 145, ErrorDeviceNotConnected = 1167;

    private static readonly IReadOnlySet<string> LibraryExclude =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { LedgerPaths.FolderName };

    public static Task<CleanupResult> RunAsync(ConfirmedCleanupPlan confirmed, CleanupEnvironment env,
                                               IProgress<CleanupProgress> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(progress);
        return Task.Run(() => new Run(confirmed, env, progress, ct).Execute(), CancellationToken.None);
    }

    /// <summary>The cardDelete `evidence` field (Ref §10.6 Ledger cardDelete records).</summary>
    internal static string EvidenceText(CleanupCandidate unit, CardEntry file)
    {
        var rel = CleanupPaths.Rel(file.RelPath);
        var proof = unit.Proofs.FirstOrDefault(p => CleanupPaths.Rel(p.CardRelPath).Equals(rel, StringComparison.OrdinalIgnoreCase));
        var unitEvidence = unit.Eligibility == CleanupEligibility.NotInLibrary
            ? "notInLibraryConfirmed"
            : (unit.Source == EvidenceSource.HistoryOnly ? "historyOnly:" : "") + unit.Evidence;
        if (proof is null) return "companionOf:" + unitEvidence;
        if (unit.Eligibility == CleanupEligibility.NotInLibrary) return "notInLibraryConfirmed";
        return (proof.ListedFolder is null ? "historyOnly:" : "") + proof.Category;
    }

    private sealed partial class Run
    {
        private readonly ConfirmedCleanupPlan _confirmed;
        private readonly CleanupEnvironment _env;
        private readonly IProgress<CleanupProgress> _progress;
        private readonly CancellationToken _ct;
        private readonly CleanupPlan _plan;
        private readonly ImmutableArray<CleanupCandidate> _units;
        private readonly CleanupOutcome?[] _outcomes;
        private readonly List<string> _deleted = [];
        private readonly string _runId = Guid.NewGuid().ToString("N");
        private readonly DateTime _start;
        private readonly int _filesTotal;
        private readonly long _bytesTotal;
        private CleanupStop? _stop;
        private int _filesDone;
        private long _bytesDone;
        private DateTime _lastReport = DateTime.MinValue;
        private FreshEvidence? _fresh;
        private LedgerSnapshot? _ledger;
        private ILedgerWriter? _writer;
        private ICardEraser? _eraser;

        public Run(ConfirmedCleanupPlan confirmed, CleanupEnvironment env, IProgress<CleanupProgress> progress, CancellationToken ct)
        {
            _confirmed = confirmed; _env = env; _progress = progress; _ct = ct;
            _plan = confirmed.Plan;
            _units = _plan.Delete;
            _outcomes = new CleanupOutcome?[_units.Length];
            _start = env.Clock.GetUtcNow().UtcDateTime;
            _filesTotal = _plan.FileCount;
            _bytesTotal = _plan.AllocatedBytes;
        }

        public CleanupResult Execute()
        {
            var held = new Stack<IDisposable>();
            var lk = _env.Lock.TryAcquire();                                      // step 1
            if (lk is null) { _stop = CleanupStop.OffloadLockHeld; return Complete(); }
            held.Push(lk);
            try
            {
                held.Push(_env.Power.KeepSystemAwake("Cleaning up drone card"));
                held.Push(_env.Thumbnails.Pause());                               // step 2
                if (!PrepareLedger(held)) return Complete();                      // step 3
                if (!LoadFreshEvidence()) return Complete();                      // step 4
                if (!OpenEraser(held)) return Complete();                         // step 5
                for (var i = 0; i < _units.Length && _stop is null; i++)
                {
                    if (_ct.IsCancellationRequested) { _stop = CleanupStop.Cancelled; break; }
                    _outcomes[i] = DeleteUnit(_units[i]);
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
            if (status.State is LedgerFolderState.CloudOnly or LedgerFolderState.Unwritable or LedgerFolderState.VideoRootMissing)
            { _stop = CleanupStop.LedgerUnavailable; return false; }
            try
            {
                _env.Ledger.EnsureFolder();
                _env.Ledger.SnapshotToBackup(_runId);
                _writer = _env.Ledger.OpenOwn();                                  // only after the folder exists
                held.Push(_writer);
                return true;
            }
            catch (UnsafeIoException) { _stop = CleanupStop.InternalSafetyStop; return false; }
            catch (IOException) { _stop = CleanupStop.LedgerUnavailable; return false; }
            catch (UnauthorizedAccessException) { _stop = CleanupStop.LedgerUnavailable; return false; }
        }

        private bool LoadFreshEvidence()
        {
            var s = _env.Settings;
            _fresh = FreshEvidence.From(new LibraryListings(ListRoot(s.VideoRoot, DestRoot.Video, false), ListRoot(s.PhotoRoot, DestRoot.Photo, false),
                                                            [.. s.PreviousPhotoRoots.Select(r => ListRoot(r, DestRoot.Photo, true))]));
            try { _ledger = _env.Ledger.Load(); return true; }
            catch (UnsafeIoException) { _stop = CleanupStop.InternalSafetyStop; return false; }
            catch (IOException) { _stop = CleanupStop.LedgerUnavailable; return false; }
            catch (UnauthorizedAccessException) { _stop = CleanupStop.LedgerUnavailable; return false; }
        }

        private RootListing ListRoot(string root, DestRoot kind, bool previous)
        {
            try { return new RootListing(root, kind, previous, true, _env.Lister.Enumerate(root, true, LibraryExclude)); }
            catch (IOException) { return new RootListing(root, kind, previous, false, new ListingResult([], [])); }
            catch (UnauthorizedAccessException) { return new RootListing(root, kind, previous, false, new ListingResult([], [])); }
        }

        private bool OpenEraser(Stack<IDisposable> held)
        {
            try
            {
                _eraser = _env.Erasers.Open(_env.Source, _env.Pinned, _confirmed);   // re-runs the volume check from Win32
                held.Push(_eraser);
                return true;
            }
            catch (UnsafeIoException) { _stop = CleanupStop.InternalSafetyStop; return false; }
        }

        /// <summary>Step 2 (Ref §10.6 Evidence re-check): listings and the ledger only; no library file is opened.</summary>
        private (string Path, string Why)? EvidenceGone(CleanupCandidate c)
        {
            foreach (var p in c.Proofs)
            {
                var listed = _fresh!.ListedFolder(p.Key) is not null;
                var verified = _ledger!.Files.TryGetValue(p.Key, out var lf) && lf.Verify is VerifyKind.Unbuffered or VerifyKind.Cached;
                var ok = c.Kind == ItemKind.Video ? listed                         // a ledger record alone is never enough for a video
                       : p.ListedFolder is not null ? listed || verified          // Listed at plan time: Lightroom may have moved it since
                       : verified;                                                // HistoryOnly
                if (!ok)
                    return (p.CardRelPath, c.Kind == ItemKind.Video ? "no longer in the library listing"
                                                                    : "no longer in the library listing or the history");
            }
            return null;
        }

        private bool AppendRecord(CleanupCandidate c, CardEntry f)
        {
            try
            {
                _writer!.Append(new CardDeleteRecord(1, Guid.NewGuid().ToString("N"), Environment.MachineName, _runId,   // G4, decision 32
                    _env.Clock.GetUtcNow().UtcDateTime, CleanupPaths.Name(f.RelPath), f.Size, CleanupPaths.Rel(f.RelPath),
                    c.Unit.CardRelPath, c.CaptureUtc, EvidenceText(c, f), c.Reason,
                    _plan.Request.Mode == CleanupMode.BeforeDate ? "beforeDate" : "freeSpace",
                    CommitTailRecords.Card(_plan.Card, _plan.CameraModel, _plan.InventoryHash),   // same RunCard as the offload's run record
                    c.SetFolder is null ? null : CleanupPaths.Name(c.SetFolder)));
                return true;
            }
            catch (IOException) { _stop = CleanupStop.LedgerWriteFailed; return false; }
            catch (UnauthorizedAccessException) { _stop = CleanupStop.LedgerWriteFailed; return false; }
            catch (InvalidOperationException) { _stop = CleanupStop.LedgerWriteFailed; return false; }
        }

        private void MarkDeleted(CleanupCandidate c, CardEntry f)
        {
            var rel = CleanupPaths.Rel(f.RelPath);
            _deleted.Add(rel);
            _filesDone++;
            _bytesDone += CleanupPaths.Allocated(f.Size, _plan.SpaceBefore.ClusterBytes);
            Report(rel, c.Unit, force: false);
        }

        private bool RemoveSetFolder(CleanupCandidate c)
        {
            if (c.SetFolder is null) return false;
            try { return _eraser!.RemoveEmptySetFolder(c.SetFolder) is EraseOk; }   // ERROR_DIR_NOT_EMPTY etc.: "folder kept"
            catch (UnsafeIoException) { _stop = CleanupStop.InternalSafetyStop; return false; }
        }

        private PartiallyDeleted Partial(CleanupCandidate c, List<string> done, string why)
            => new(c.Unit, [.. done],
                   [.. c.Files.Select(f => CleanupPaths.Rel(f.RelPath)).Where(p => !done.Contains(p, StringComparer.OrdinalIgnoreCase))], why);

        private void Report(string? current, ItemId? unit, bool force)
        {
            var now = _env.Clock.GetUtcNow().UtcDateTime;
            if (!force && now - _lastReport < TimeSpan.FromMilliseconds(100)) return;   // 10 Hz
            _lastReport = now;
            _progress.Report(new CleanupProgress(_filesDone, _filesTotal, _bytesDone, _bytesTotal, current, unit));
        }

        private CleanupResult Complete()
        {
            for (var i = 0; i < _units.Length; i++) _outcomes[i] ??= new CleanupNotStarted(_units[i].Unit);
            var stillListed = ImmutableArray<string>.Empty;
            var after = _plan.SpaceBefore;
            try
            {
                var listed = _env.Reader.Relist().Entries.Where(e => !e.IsDirectory)
                    .Select(e => CleanupPaths.Rel(e.RelPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                stillListed = [.. _deleted.Where(listed.Contains)];
            }
            catch (IOException) { /* card gone: the rescan shows the truth */ }
            try { after = _env.Reader.Space(); }
            catch (IOException) { /* card gone: keep SpaceBefore */ }
            Report(null, null, force: true);
            return new CleanupResult(_runId, _confirmed, [.. _outcomes.Select(o => o!)], _stop, after, stillListed,
                                     _start, _env.Clock.GetUtcNow().UtcDateTime);
        }
    }
}
```

```csharp
// src/UasSort.Core/Cleanup/CleanupExecutor.Delete.cs
namespace UasSort.Core.Cleanup;

public static partial class CleanupExecutor
{
    private sealed partial class Run
    {
        /// <summary>Evidence re-check, then deletes in delete order (primary last); Task 08.12 adds the per-file checks and failures.</summary>
        private CleanupOutcome DeleteUnit(CleanupCandidate c)
        {
            if (c.Eligibility == CleanupEligibility.Evidence && EvidenceGone(c) is { } gone)   // step 2
                return new SkippedEvidenceGone(c.Unit, gone.Path, gone.Why);
            var done = new List<string>();
            long bytes = 0;
            for (var j = 0; j < c.Files.Length; j++)
            {
                var f = c.Files[j];
                var rel = CleanupPaths.Rel(f.RelPath);
                if (_eraser!.DeleteFile(f.RelPath) is EraseError err)
                    return done.Count == 0 ? new CleanupFailed(c.Unit, rel, err.Win32Error, err.Message) : Partial(c, done, err.Message);
                done.Add(rel);
                bytes += CleanupPaths.Allocated(f.Size, _plan.SpaceBefore.ClusterBytes);
                MarkDeleted(c, f);
                if (!AppendRecord(c, f))                                          // step 4: right after the delete
                    return j == c.Files.Length - 1 ? new Deleted(c.Unit, done.Count, bytes, false)
                                                   : Partial(c, done, "the ledger could not be written");
            }
            return new Deleted(c.Unit, done.Count, bytes, RemoveSetFolder(c));
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupExecutorTests"`
Expected: PASS (10 cases).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/CleanupExecutor.cs src/UasSort.Core/Cleanup/CleanupExecutor.Delete.cs tests/UasSort.Core.Tests/Cleanup/CleanupExecutorHarness.cs tests/UasSort.Core.Tests/Cleanup/CleanupExecutorTests.cs
git commit -m "feat: add card cleanup executor with ledger records

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 08.12: CleanupExecutor — per-file checks, evidence re-check and every outcome

**Files:**
- Modify (replace the whole file): `src/UasSort.Core/Cleanup/CleanupExecutor.Delete.cs`
- Test: `tests/UasSort.Core.Tests/Cleanup/CleanupExecutorFaultTests.cs`

**Interfaces:**
- Consumes: `CleanupExecutor.Run` members from 08.11 (`_env`, `_confirmed`, `_plan`, `_eraser`, `_ct`, `_stop`, `EvidenceGone`, `MarkDeleted`, `AppendRecord`, `RemoveSetFolder`, `Partial`); outcome cases `SkippedChanged`, `SkippedEvidenceGone`, `CleanupCardSwapped`, `FileProof`, `VerifyKind` (Part 02, Ref §3); `CleanupExecutorHarness` (08.11) with the shared fakes and their fault knobs: `FakeFileSystem.SetCardIdentity`, `Touch`, `RemoveUnguarded`, `AddFile`, `SetAttributes`, `CardDeleteViolations`, `HydrationViolations`; `FakeFaults.CardRemoved`, `DeleteErrors`, `OnCardDelete`; `FakeLedgerWriter.ThrowWhen`/`Records` (Parts 02, 07, 08.10).
- Produces: `Run.DeleteUnit(CleanupCandidate)` per Ref §10.6: steps 0 (identity) and 1 (`Stat` size + mtime against the plan's entry) for every file and step 2 once (evidence re-check: videos need the fresh listing; photos, twins and set members need the fresh listing or a verified fresh-ledger record when they were listed at plan time, and the verified record when they were `HistoryOnly`; NotInLibrary units need their `NotInLibraryConfirmed` token) before the first delete (step 2 itself is 08.11's `EvidenceGone`); then steps 0, 1, 3 and 4 per file. Outcome table: identity mismatch → `CleanupCardSwapped`/`PartiallyDeleted` + stop `CardSwapped`; changed or gone → `SkippedChanged`/`PartiallyDeleted`, continue; evidence gone → `SkippedEvidenceGone`, continue; `ERROR_WRITE_PROTECT` → stop `WriteProtected`; `ERROR_NOT_READY`, `ERROR_DEVICE_NOT_CONNECTED`, or `CurrentIdentity()` failing after an error → stop `CardRemoved`; any other delete error → `CleanupFailed`/`PartiallyDeleted`, continue; set folder not removable → `Deleted` with `SetFolderRemoved = false`; append failure → stop `LedgerWriteFailed`; Cancel between files → `PartiallyDeleted`, stop `Cancelled`; `UnsafeIoException` → stop `InternalSafetyStop`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Cleanup/CleanupExecutorFaultTests.cs
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public sealed class CleanupExecutorFaultTests : IDisposable
{
    private static readonly CardIdentity OtherCard = Identity with { VolumeSerial = 0x5E6F7A8B };
    private readonly Dictionary<CleanupExecutorHarness, int> _expectedViolations = [];

    private CleanupExecutorHarness Harness(CleanupScenario s, int expectedViolations = 0)
    {
        var h = new CleanupExecutorHarness(s);
        _expectedViolations[h] = expectedViolations;
        return h;
    }

    public void Dispose()                                                    // the card-delete and hydration tripwires, at teardown
    {
        foreach (var (h, n) in _expectedViolations)
        {
            Assert.Equal(n, h.Fs.CardDeleteViolations.Count);
            Assert.Empty(h.Fs.HydrationViolations);
        }
    }

    private static (CleanupScenario S, ItemId[] Ids) Singles(int n)
    {
        var s = new CleanupScenario();
        var ids = Enumerable.Range(0, n).Select(i => s.AddVideo(Utc(2026, 7, 10 + i, 20, 0))).ToArray();
        return (s, ids);
    }

    private static string Rel(CleanupScenario s, ItemId id) => s.Primary(id).RelPath;

    private static string Full(string cardRelPath) => PathRules.Join(CardRoot, cardRelPath);

    private static Type[] Kinds(CleanupResult r) => [.. r.Outcomes.Select(o => o.GetType())];

    [Fact]
    public async Task Identity_change_before_the_third_file_stops_the_run()
    {
        var (s, _) = Singles(4);
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Fs.Faults.OnCardDelete = _ => { if (h.Erasers.AllDeleted.Count == 1) h.Fs.SetCardIdentity(CardRoot, OtherCard); };
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { typeof(Deleted), typeof(Deleted), typeof(CleanupCardSwapped), typeof(CleanupNotStarted) }, Kinds(r));
        Assert.Equal(OtherCard, ((CleanupCardSwapped)r.Outcomes[2]).Now);
        Assert.Equal(CleanupStop.CardSwapped, r.Stop);
        Assert.Equal(2, h.Deleted.Count);
    }

    [Fact]
    public async Task Identity_change_mid_unit_leaves_the_primary_on_the_card()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 10, 20, 0), companions: Comp.Lrf | Comp.Srt);
        s.AddVideo(Utc(2026, 7, 11, 20, 0));
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Fs.Faults.OnCardDelete = _ => { if (h.Erasers.AllDeleted.Count == 1) h.Fs.SetCardIdentity(CardRoot, OtherCard); };
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        var partial = Assert.IsType<PartiallyDeleted>(r.Outcomes[0]);
        Assert.Equal(new[] { "DCIM/DJI_001/DJI_20260710120000_0101_D.LRF", "DCIM/DJI_001/DJI_20260710120000_0101_D.SRT" }, partial.DeletedPaths);
        Assert.Equal(new[] { "DCIM/DJI_001/DJI_20260710120000_0101_D.MP4" }, partial.StillOnCard);
        Assert.IsType<CleanupNotStarted>(r.Outcomes[1]);
        Assert.Equal(CleanupStop.CardSwapped, r.Stop);
    }

    [Fact]
    public async Task Changed_or_gone_files_are_skipped_and_the_run_continues()
    {
        var (s, ids) = Singles(4);
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        var touched = s.Primary(ids[1]).MtimeUtc.AddMinutes(1);
        h.Fs.Touch(Full(Rel(s, ids[1])), mtimeUtc: touched);
        h.Fs.RemoveUnguarded(Full(Rel(s, ids[2])));
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Null(r.Stop);
        Assert.Equal(new[] { typeof(Deleted), typeof(SkippedChanged), typeof(SkippedChanged), typeof(Deleted) }, Kinds(r));
        var changed = (SkippedChanged)r.Outcomes[1];
        Assert.Equal((1_000_000_000L, touched), (changed.NowSize!.Value, changed.NowMtimeUtc!.Value));
        var gone = (SkippedChanged)r.Outcomes[2];
        Assert.Null(gone.NowSize);
        Assert.Null(gone.NowMtimeUtc);
        Assert.True(h.OnCard(Rel(s, ids[1])));
    }

    [Fact]
    public async Task Evidence_recheck_uses_fresh_listings_and_the_fresh_ledger()
    {
        var s = new CleanupScenario();
        var video = s.AddVideo(Utc(2026, 7, 10, 20, 0), AuditCategory.InLedger, ledger: VerifyKind.Unbuffered);
        var photo = s.AddPhoto(Utc(2026, 7, 11, 20, 0), AuditCategory.InLedger, ledger: VerifyKind.Unbuffered);
        var history = s.AddPhoto(Utc(2026, 7, 12, 20, 0), AuditCategory.InLedger, listed: false, ledger: VerifyKind.Cached);
        var confirmed = s.Confirmed(Before(2026, 7, 26));                      // planned while every proof held
        var hp = s.Primary(history);
        s.LedgerFiles.Remove(CleanupKeys.Key(hp.RelPath, hp.Size));            // the fresh ledger no longer has it
        var h = Harness(s);
        h.Fs.RemoveUnguarded($@"{VideoRoot}\{CouncilDir}\{CleanupPaths.Name(Rel(s, video))}");   // clip deleted from the library
        h.Fs.RemoveUnguarded($@"{PhotoRoot}\{CleanupPaths.Name(Rel(s, photo))}");                // Lightroom moved it; the ledger still has it

        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);

        Assert.Null(r.Stop);
        var v = Assert.IsType<SkippedEvidenceGone>(r.Outcomes[0]);
        Assert.Equal((Rel(s, video), "no longer in the library listing"), (v.CardRelPath, v.Why));
        Assert.IsType<Deleted>(r.Outcomes[1]);
        var hg = Assert.IsType<SkippedEvidenceGone>(r.Outcomes[2]);
        Assert.Equal("no longer in the library listing or the history", hg.Why);
        Assert.True(h.OnCard(Rel(s, video)));
        Assert.True(h.OnCard(hp.RelPath));
    }

    [Fact]
    public async Task A_delete_error_on_a_sets_fifth_member_keeps_the_rest_and_the_folder()
    {
        var s = new CleanupScenario();
        var set = s.AddSet("001_0087", Utc(2026, 7, 10, 20, 0), members: 33);
        var next = s.AddVideo(Utc(2026, 7, 11, 20, 0));
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Fs.Faults.DeleteErrors["DCIM/PANORAMA/001_0087/PANO_0005.DNG"] = CleanupExecutor.ErrorAccessDenied;
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Null(r.Stop);
        var partial = Assert.IsType<PartiallyDeleted>(r.Outcomes[0]);
        Assert.Equal(set, partial.Unit);
        Assert.Equal(4, partial.DeletedPaths.Length);
        Assert.Equal(29, partial.StillOnCard.Length);
        Assert.Equal("DCIM/PANORAMA/001_0087/PANO_0005.DNG", partial.StillOnCard[0]);
        Assert.Empty(h.RemovedDirs);
        Assert.True(h.OnCard("DCIM/PANORAMA/001_0087/PANO_0033.DNG"));
        Assert.Equal(next, Assert.IsType<Deleted>(r.Outcomes[1]).Unit);
        Assert.Equal(5, h.Ledger.Writer.Records.Count);
    }

    [Fact]
    public async Task Write_protect_stops_the_run()
    {
        var (s, ids) = Singles(3);
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Fs.Faults.DeleteErrors[Rel(s, ids[1])] = CleanupExecutor.ErrorWriteProtect;   // the lock switch flipped after the first delete
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { typeof(Deleted), typeof(CleanupFailed), typeof(CleanupNotStarted) }, Kinds(r));
        var failed = (CleanupFailed)r.Outcomes[1];
        Assert.Equal((Rel(s, ids[1]), CleanupExecutor.ErrorWriteProtect), (failed.CardRelPath, failed.Win32Error));
        Assert.Equal(CleanupStop.WriteProtected, r.Stop);
    }

    [Theory]
    [InlineData(21)]                          // ERROR_NOT_READY from the delete itself
    [InlineData(1117)]                        // ERROR_IO_DEVICE, then CurrentIdentity() fails
    public async Task A_vanished_card_stops_with_card_removed(int error)
    {
        var (s, ids) = Singles(3);
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        var rel = Rel(s, ids[1]);
        h.Fs.Faults.DeleteErrors[rel] = error;
        h.Fs.Faults.OnCardDelete = p => { if (p == rel) h.Fs.Faults.CardRemoved = true; };   // the card goes as that delete fails
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { typeof(Deleted), typeof(CleanupFailed), typeof(CleanupNotStarted) }, Kinds(r));
        Assert.Equal(error, ((CleanupFailed)r.Outcomes[1]).Win32Error);
        Assert.Equal(CleanupStop.CardRemoved, r.Stop);
        Assert.Equal(confirmed.Plan.SpaceBefore, r.SpaceAfter);
        Assert.Empty(r.StillListed);
    }

    [Fact]
    public async Task Access_denied_and_sharing_violation_fail_only_their_unit()
    {
        var (s, ids) = Singles(3);
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Fs.Faults.DeleteErrors[Rel(s, ids[0])] = CleanupExecutor.ErrorAccessDenied;
        h.Fs.Faults.DeleteErrors[Rel(s, ids[1])] = CleanupExecutor.ErrorSharingViolation;
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Null(r.Stop);
        Assert.Equal(new[] { typeof(CleanupFailed), typeof(CleanupFailed), typeof(Deleted) }, Kinds(r));
        Assert.Equal(CleanupExecutor.ErrorSharingViolation, ((CleanupFailed)r.Outcomes[1]).Win32Error);
    }

    [Fact]
    public async Task A_failed_ledger_append_stops_and_the_file_counts_as_deleted()
    {
        var (s, ids) = Singles(2);
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Ledger.Writer.ThrowWhen = _ => true;                                   // the first append throws IOException
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Equal(CleanupStop.LedgerWriteFailed, r.Stop);
        Assert.Equal(1, Assert.IsType<Deleted>(r.Outcomes[0]).Files);
        Assert.IsType<CleanupNotStarted>(r.Outcomes[1]);
        Assert.False(h.OnCard(Rel(s, ids[0])));
        Assert.Empty(h.Ledger.Writer.Records);

        var multi = new CleanupScenario();
        multi.AddVideo(Utc(2026, 7, 10, 20, 0), companions: Comp.Lrf);
        var hm = Harness(multi);
        var cm = multi.Confirmed(Before(2026, 7, 26));
        hm.Ledger.Writer.ThrowWhen = _ => true;
        var rm = await hm.Run(cm, TestContext.Current.CancellationToken);
        var partial = Assert.IsType<PartiallyDeleted>(rm.Outcomes[0]);
        Assert.Equal(new[] { "DCIM/DJI_001/DJI_20260710120000_0101_D.LRF" }, partial.DeletedPaths);
        Assert.Equal(new[] { "DCIM/DJI_001/DJI_20260710120000_0101_D.MP4" }, partial.StillOnCard);
    }

    [Fact]
    public async Task Cancel_stops_after_the_current_file()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 10, 20, 0), companions: Comp.Lrf | Comp.Srt);
        s.AddVideo(Utc(2026, 7, 11, 20, 0));
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        using var cts = new CancellationTokenSource();
        h.Fs.Faults.OnCardDelete = _ => cts.Cancel();
        var r = await h.Run(confirmed, cts.Token);
        Assert.Equal(CleanupStop.Cancelled, r.Stop);
        Assert.Single(Assert.IsType<PartiallyDeleted>(r.Outcomes[0]).DeletedPaths);
        Assert.IsType<CleanupNotStarted>(r.Outcomes[1]);
        Assert.Single(h.Deleted);

        var (s2, _) = Singles(2);
        var h2 = Harness(s2);
        var r2 = await h2.Run(s2.Confirmed(Before(2026, 7, 26)), new CancellationToken(canceled: true));
        Assert.Equal(CleanupStop.Cancelled, r2.Stop);
        Assert.All(r2.Outcomes, o => Assert.IsType<CleanupNotStarted>(o));
        Assert.Empty(h2.Deleted);
    }

    [Fact]
    public async Task An_emptied_set_folder_that_cannot_be_removed_is_kept()
    {
        var s = new CleanupScenario();
        s.AddSet("001_0087", Utc(2026, 7, 10, 20, 0));
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Fs.AddFile(Full("DCIM/PANORAMA/001_0087/Thumbs.db"), 10, ScanUtc);     // appeared after the plan; never deleted
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Null(r.Stop);
        var d = Assert.IsType<Deleted>(r.Outcomes[0]);
        Assert.Equal(3, d.Files);
        Assert.False(d.SetFolderRemoved);
        Assert.True(h.OnCard("DCIM/PANORAMA/001_0087/Thumbs.db"));
    }

    [Fact]
    public async Task A_guard_refusal_mid_run_is_an_internal_safety_stop_and_trips_the_wire()
    {
        var (s, ids) = Singles(3);
        var h = Harness(s, expectedViolations: 1);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Fs.SetAttributes(Full(Rel(s, ids[1])), 0x30);                         // now looks like a directory the plan doesn't name
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { typeof(Deleted), typeof(CleanupFailed), typeof(CleanupNotStarted) }, Kinds(r));
        Assert.Equal(CleanupStop.InternalSafetyStop, r.Stop);
        Assert.True(h.OnCard(Rel(s, ids[1])));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupExecutorFaultTests"`
Expected: FAIL — `Identity_change_before_the_third_file_stops_the_run`, `Identity_change_mid_unit_leaves_the_primary_on_the_card`, `Changed_or_gone_files_are_skipped_and_the_run_continues`, `Write_protect_stops_the_run`, `A_vanished_card_stops_with_card_removed`, `Cancel_stops_after_the_current_file` and `A_guard_refusal_mid_run…` fail (08.11's `DeleteUnit` has no identity/stat checks, stops or guard handling); the evidence, set, access-denied, ledger and folder tests already pass.

- [ ] **Step 3: Implement** — replace the whole of `src/UasSort.Core/Cleanup/CleanupExecutor.Delete.cs` with:

```csharp
// src/UasSort.Core/Cleanup/CleanupExecutor.Delete.cs
namespace UasSort.Core.Cleanup;

public static partial class CleanupExecutor
{
    private enum FileCheck { Ok, Changed, Swapped, Gone }

    private sealed partial class Run
    {
        /// <summary>Ref §10.6: steps 0–1 for every file and step 2 once; then steps 0, 1, 3, 4 per file in delete order.</summary>
        private CleanupOutcome DeleteUnit(CleanupCandidate c)
        {
            foreach (var f in c.Files)
            {
                var pre = Verify(f);
                if (pre.Result != FileCheck.Ok) return Stopped(c, [], f, pre);
            }

            if (c.Eligibility == CleanupEligibility.NotInLibrary)
            {
                if (!_confirmed.NotInLibraryConfirmed.Contains(c.Unit))           // unreachable through Confirm
                {
                    _stop = CleanupStop.InternalSafetyStop;
                    return new CleanupFailed(c.Unit, CleanupPaths.Rel(c.Files[^1].RelPath), 0, "no confirmation for a unit not in the library");
                }
            }
            else if (c.Eligibility != CleanupEligibility.Evidence)                // unreachable through Confirm
            {
                _stop = CleanupStop.InternalSafetyStop;
                return new CleanupFailed(c.Unit, CleanupPaths.Rel(c.Files[^1].RelPath), 0, "not deletable");
            }
            else if (EvidenceGone(c) is { } gone)
                return new SkippedEvidenceGone(c.Unit, gone.Path, gone.Why);

            var done = new List<string>();
            long bytes = 0;
            for (var j = 0; j < c.Files.Length; j++)
            {
                var f = c.Files[j];
                var rel = CleanupPaths.Rel(f.RelPath);
                if (j > 0 && _ct.IsCancellationRequested)                          // Cancel is checked between files
                {
                    _stop = CleanupStop.Cancelled;
                    return Partial(c, done, "cancelled");
                }
                var check = Verify(f);                                            // steps 0 and 1
                if (check.Result != FileCheck.Ok) return Stopped(c, done, f, check);

                EraseResult result;
                try { result = _eraser!.DeleteFile(f.RelPath); }                  // step 3
                catch (UnsafeIoException ex)
                {
                    _stop = CleanupStop.InternalSafetyStop;
                    return done.Count == 0 ? new CleanupFailed(c.Unit, rel, 0, ex.Message) : Partial(c, done, ex.Message);
                }
                if (result is EraseError err)
                {
                    _stop = err.Win32Error switch
                    {
                        ErrorWriteProtect => CleanupStop.WriteProtected,
                        ErrorNotReady or ErrorDeviceNotConnected => CleanupStop.CardRemoved,
                        _ => IdentityGone() ? CleanupStop.CardRemoved : (CleanupStop?)null,   // access denied, sharing: this unit only
                    };
                    return done.Count == 0 ? new CleanupFailed(c.Unit, rel, err.Win32Error, err.Message) : Partial(c, done, err.Message);
                }

                done.Add(rel);
                bytes += CleanupPaths.Allocated(f.Size, _plan.SpaceBefore.ClusterBytes);
                MarkDeleted(c, f);
                if (!AppendRecord(c, f))                                          // step 4: right after the delete
                    return j == c.Files.Length - 1 ? new Deleted(c.Unit, done.Count, bytes, false)
                                                   : Partial(c, done, "the ledger could not be written");
            }
            return new Deleted(c.Unit, done.Count, bytes, RemoveSetFolder(c));
        }

        private CleanupOutcome Stopped(CleanupCandidate c, List<string> done, CardEntry f,
                                       (FileCheck Result, CardIdentity? Now, long? Size, DateTime? Mtime) check)
        {
            var rel = CleanupPaths.Rel(f.RelPath);
            switch (check.Result)
            {
                case FileCheck.Gone:
                    _stop = CleanupStop.CardRemoved;
                    return done.Count == 0 ? new CleanupFailed(c.Unit, rel, ErrorNotReady, "The card was removed.")
                                           : Partial(c, done, "the card was removed");
                case FileCheck.Swapped:
                    _stop = CleanupStop.CardSwapped;
                    return done.Count == 0 ? new CleanupCardSwapped(c.Unit, check.Now!)
                                           : Partial(c, done, "a different card is in the drive");
                default:
                    return done.Count == 0 ? new SkippedChanged(c.Unit, rel, check.Size, check.Mtime)
                                           : Partial(c, done, $"{CleanupPaths.Name(rel)} changed since the plan");
            }
        }

        /// <summary>Step 0 (identity) and step 1 (the file exists with the planned size and mtime).</summary>
        private (FileCheck Result, CardIdentity? Now, long? Size, DateTime? Mtime) Verify(CardEntry f)
        {
            CardIdentity now;
            try { now = _env.Reader.CurrentIdentity(); }
            catch (IOException) { return (FileCheck.Gone, null, null, null); }
            if (now != _env.Pinned) return (FileCheck.Swapped, now, null, null);
            try
            {
                var e = _env.Reader.Stat(f.RelPath);
                return e.Size == f.Size && e.MtimeUtc == f.MtimeUtc
                    ? (FileCheck.Ok, now, null, null)
                    : (FileCheck.Changed, now, e.Size, e.MtimeUtc);
            }
            catch (FileNotFoundException) { return (FileCheck.Changed, now, null, null); }
            catch (DirectoryNotFoundException) { return (FileCheck.Changed, now, null, null); }
            catch (IOException) { return IdentityGone() ? (FileCheck.Gone, null, null, null) : (FileCheck.Changed, now, null, null); }
        }

        private bool IdentityGone()
        {
            try { _ = _env.Reader.CurrentIdentity(); return false; }
            catch (IOException) { return true; }
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*CleanupExecutor*"`
Expected: PASS (`CleanupExecutorTests` 10 cases + `CleanupExecutorFaultTests` 13 cases).

Then the whole suite: `dotnet test --solution uas-sort.slnx`
Expected: PASS, no warnings (TreatWarningsAsErrors).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Cleanup/CleanupExecutor.Delete.cs tests/UasSort.Core.Tests/Cleanup/CleanupExecutorFaultTests.cs
git commit -m "feat: add card cleanup per-file checks and failure outcomes

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

## Part 08 — Produces (summary)

Core (`src/UasSort.Core/Cleanup/`, namespace `UasSort.Core.Cleanup` unless noted):

| Type | Members | Task |
|---|---|---|
| `CleanupPaths`, `CleanupKeys`, `CleanupFormat` | card-path normalisation and canonical `E:\DCIM\…` paths, cluster rounding; `NormName`/`Key` delegating to `FileKey.NormalizeName`/`FileKey.OfPath`; decimal GB, miles, zone conversion via `Zones.Find` and abbreviations via `ZoneNames.Abbreviation` | 08.1 |
| `CleanupVolumeCheck` | `static string? Refusal(VolumeInfo volume, ListingResult card, Settings settings, string appDataDir)`; `NotACard`, `WriteProtected` tooltips | 08.2 |
| `FileFacts`, `FileVerdict`, `CleanupRules` | `Classify(FileFacts)` (Ref §10.6 rows 2–20), `LooseReason(...)`, `const ChangedSinceScanDetail = CardDiffResult.ChangedDetail` | 08.3 |
| `FreshEvidence` | `static FreshEvidence From(LibraryListings)`, `string? ListedFolder(FileKey)` | 08.4 |
| `CleanupPlanner` (static partial) | `Candidates(CleanupInputs)`, `LooseFiles(CleanupInputs, ImmutableArray<CleanupCandidate>)`, `Build(CleanupInputs, ImmutableArray<CleanupCandidate>, CleanupRequest, CleanupRows, IReadOnlySet<ItemId> firstShown)` — both modes, both free-space readings, cutoff, shortfall, undecided rows; the only caller of Part 02's 18-argument `CleanupPlan` constructor | 08.4–08.6 |
| `CleanupFingerprint` | `static string Compute(CleanupRequest, CardSpace, IEnumerable<CleanupCandidate>)` | 08.5 |
| `CleanupRowOps` | `Set`, `DeleteAll`, `KeepAll` (review-list rows) | 08.7 |
| `CleanupTexts` | `CutoffLine`, `NothingToDelete`, `ShortfallLine`, `EvidenceSplit`, `NeverCopiesLine`, `VolumeName` | 08.8 |
| `CleanupPlan.Confirm` (namespace `UasSort.Core`, `Cleanup/CleanupPlan.Confirm.cs`, second partial file of Part 02's `CleanupPlan`) | `ConfirmedCleanupPlan Confirm(CleanupAck, TimeProvider)`; internal validation helpers `CanonicalFile`, `CanonicalSetFolder` returning `PathRules.Join(cardRoot, rel)` (the same paths Part 02's `ConfirmedCleanupPlan` derives) | 08.9 |
| `CleanupExecutor` (static partial) | `RunAsync(ConfirmedCleanupPlan, CleanupEnvironment, IProgress<CleanupProgress>, CancellationToken)`, Win32 error constants, internal `EvidenceText`; `cardDelete` records carry `CommitTailRecords.Card(...)` and `Environment.MachineName` | 08.11–08.12 |

Not declared here: `CleanupPlan` (18-argument internal constructor, properties incl. `InventoryHash`/`CameraModel`) and `ConfirmedCleanupPlan` are Part 02's (`src/UasSort.Core/Model/Cleanup.cs`, namespace `UasSort.Core`); `InternalsVisibleTo` (`UasSort.Core.Tests`, `UasSort.Testing`) is in Part 02's `UasSort.Core.csproj`.

Testing (`tests/UasSort.Testing/`, namespace `UasSort.Testing`; there is no `UasSort.Testing.Cleanup` namespace):
- Defined here: `CleanupPlanFixtures` (`static ConfirmedCleanupPlan Confirmed(string cardRoot, CardIdentity card, IReadOnlyList<string> fileRelPaths, IReadOnlyList<string> setFolderRelDirs, TimeProvider clock)`, `static CleanupPlan Plan(string cardRoot, CardIdentity card, IReadOnlyList<string> fileRelPaths, IReadOnlyList<string> setFolderRelDirs)`, through the internal constructors; for Parts 09 and 10) (08.9); `Disposer(Action onDispose) : IDisposable` (08.10; for Part 10).
- Added in place to Part 02's fakes (08.10): `FakeFileSystem.Touch(string path, long? size = null, DateTime? mtimeUtc = null)`; `Action<string>? FakeFaults.OnCardDelete` (invoked by `FakeCardEraser.DeleteFile` after the guard, before `DeleteErrors` and the delete); `IReadOnlyList<string> FakeCardEraserFactory.AllDeleted`; `ICardEraser FakeCardEraserFactory.OpenUnchecked(GuardContext ctx)`.
- Used, not declared: `FakeFileSystem`, `FakeFaults`, `FakeLayout`, `FakeCardReader`, `FakeCardEraserFactory`/`FakeCardEraser` (Part 02); `FakePlaceIndex` (Part 04); `FakeLedgerStore`/`FakeLedgerWriter` (Parts 06/07); `FakeOffloadLock`, `FakePowerRequest`, `FakeThumbnails`, `ListProgress<T>` (Part 07).

Core.Tests: `CleanupScenario` builder (partial: units, audit, listings, ledger on `FakeLayout`'s card, roots, identity and space; `Inputs()`, `Build(...)`, `MakeCard()` → `FakeFileSystem` (card volume, card entries, library files; also the lister), `MakeLedgerStore()` → in-memory `FakeLedgerStore`, `Confirmed(...)`), `CleanupExecutorHarness`, and the suites `CleanupHelpersTests`, `CleanupVolumeCheckTests`, `CleanupRulesTests`, `CleanupCandidatesTests`, `CleanupBuildBeforeDateTests`, `CleanupBuildFreeSpaceTests`, `CleanupRowsTests`, `CleanupTextsTests`, `CleanupConfirmTests`, `CleanupConfirmFixturesTests`, `CleanupFakeAdditionsTests` (`UasSort.Core.Tests.Testing`), `CleanupTripwireTests`, `CleanupExecutorTests`, `CleanupExecutorFaultTests`.

Left to later parts (Ref §13 items this Core part does not own): `WindowsCardEraser`/`WindowsCardEraserFactory`/`WindowsVolumeFacts` and their Platform tests (Part 09, which may build its plans with `CleanupPlanFixtures`); `CleanupVm`, `CleanupRowVm`, `CleanupResultVm`, the cleanup report (`CleanupReport`, `VerdictAfter = NotSafe` on a failed rescan), the rescan after the run, the Review.Tests compile checks that `with` and the constructors are unreachable, and "open the Cleanup page, press Back, nothing deleted" (Part 10: `CleanupEngine.Run` is bound to exactly `CleanupExecutor.RunAsync`; `CleanupVm` binds `CleanupTexts` and `CleanupRowOps`); the Cleanup page (Part 11). The "rescan finds the 29 remaining members Imported through the library set folder" half of the set case relies on Part 06's set-superset rule (Ref §7.3 rule 2) and is asserted there.

**Spec gaps and contradictions found while writing this part (not silently resolved; each choice is marked in the code):**
- **G1** Ref §10.6 eligibility is "first match" with row 9 (Truncated → `Unfinished`) before rows 10–11 (`ConfirmedByYou`), but Ref §13 names "a dismissed truncated clip → NotInLibrary (`Dismissed`)". Task 08.3 tests rows 10–11 first to satisfy the named test; eligibility is NotInLibrary either way.
- **G2** `CleanupInputs` has no re-listed card listing, and `FormatVerdict.CardChanges` is free text; "changed since the scan" is read from `AuditLine.Detail` starting with `CleanupRules.ChangedSinceScanDetail` (= Part 07's `CardDiffResult.ChangedDetail`, "changed since scan", the prefix of every changed/added/removed audit detail, decision 13) plus this run's `ChangedOnCard`.
- **G3** Resolved: Part 02's `CleanupPlan` constructor carries `InventoryHash`/`CameraModel` (the `cardDelete` record's `RunCard` needs them); `Build` passes `inputs.Inventory.InventoryHash` and `inputs.Inventory.CameraModel`.
- **G4** `CardDeleteRecord.Machine`: `CleanupEnvironment` carries no machine name; `Environment.MachineName` is used (decision 32: `CleanupExecutor` is the only Core code that reads it) and must match the `<MACHINE>` Part 09's `LedgerStore` uses.
- **G5** `CleanupVolumeCheck.Refusal` receives no `CardSource` or pinned identity, so check 1 (browsed) and the identity half of check 3 stay with the caller; it returns the write-protected tooltip itself when `VolumeInfo.IsReadOnlyVolume`.
- **G6** The brief says a "15-row eligibility table"; Ref §10.6 has 20 rows (the main spec's §7.5 table has 3 groups). This part follows the Ref's 20 rows.
- **G7** Dates in reasons ("on Oct 4", "copied on Sep 27") have no stated zone; they are formatted in the unit's site zone.
- **G8** Resolved: Part 02 fakes. The executor and tripwire tests run on `FakeFileSystem` + `FakeCardReader` + `FakeCardEraserFactory` (the one card-delete tripwire, `FakeFileSystem.CardDeleteViolations`) with the Part 06/07 shared fakes; this part only adds `Touch`, `OnCardDelete`, `AllDeleted` and `OpenUnchecked` to them.
- **G9** The NotInLibrary-without-token outcome (`CleanupFailed` + `InternalSafetyStop`) is implemented but untestable: `ConfirmedCleanupPlan` derives its tokens from the plan, so Confirm makes it unreachable, as the Ref says.
- **G10** Loose never-deletable files (MISC, orphans, Unknown) have no capture date, so they are always listed in `NotDeletable` (with `Unit` null, the "Never touched" line); the "N older files are kept" count and the "everything" wording use only unit rows.
- **G11** Wording not given by the spec: a Before-date plan with nothing in range reads "Nothing was captured before … (local time at each site)."; a Free-space cutoff line with nothing deletable while the target is unmet reads "Nothing on this card can be deleted." (the shortfall line explains why).
- **G12** `FlightContinuesLocal` is computed only in Before-date mode (the Ref describes it only there).
- **G13** [Delete all] on an undecided "ticked for offload" row: the Ref says [Delete all] leaves ticked rows alone and also settles undecided rows; `CleanupRowOps.DeleteAll` sets such a row to Keep.
- **G14** Namespaces per `00-interfaces.md` (model, ports, guard and `UnsafeIoException` in `UasSort.Core`, the rest through `GlobalUsings.Core.cs`); `UnsafeIoException(string)`; C# 15 union values matched with `is` patterns (`EraseResult`, `GuardDecision`, `GpsProbe`), never `Assert.IsType`.
