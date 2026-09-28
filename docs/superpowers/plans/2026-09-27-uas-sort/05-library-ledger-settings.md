# Part 05 — Library index, ledger and settings

Goal: give the planner everything it knows about the library and the history without ever opening a library file. This part builds the pure Core pieces behind `LibraryIndex` (listings of the video root, the photo root and `previousPhotoRoots`; `FileKey` with `NormName`, trailing ` (n)` stripped; event folders at any depth; member start times through the card's `ClockModel`, incl. the SiteLocal ledger-`tz` rule and the mtime check; folder zone and centroid from the ledger; the fixed watermark from event-folder videos without a ledger `file` record (00-interfaces decision 4); the `.uas-sort` subtree dropped even if a lister returned it), the ledger reader (record codec for all eight kinds, the union of every top-level `ledger*.jsonl` incl. conflict copies, dedupe by `id`, `torn` markers, the torn final line, parse issues, revokes, the `LedgerSnapshot` with the strongest verification per key, decision 5), the attribute-first `LedgerFolderStatus` builder and the loader that opens only listed local ledger files, and settings (`settings.json` schema 1 codec with no `ledgerDir`, derived defaults, recovery decisions, the read-only load for the CLI, the photo-root change that appends `previousPhotoRoots`). The Windows stores that call these (`LedgerStore`, `SettingsStore`) are Part 09. Ref sections: §7.1, §11, §4.1 (`ILedgerStore`, `ISettingsStore` semantics), §4.3 (ledger exemption), §13 *Newness*, *Ledger*, *Settings*, §14 step 5; main spec §5.4, §8. Review Focus item 3 (a video root with non-event folders and loose files) is tested in Tasks 05.9 and 05.11.

**Depends on:** Part 01 (solution, test projects, `Directory.Build.props` with `TreatWarningsAsErrors`), Part 02 (the §3 model and §4.1 ports: `FileKey`, `LedgerSnapshot`, `LedgerFile`, `LedgerSet`, `LedgerDecision`, `LedgerFolder`, `LedgerRun`, `LedgerCardDelete`, `LedgerParseIssue`, `LedgerFolderState`, `LedgerFolderStatus`, `LedgerPaths` (incl. `IsLedgerFileName`), `PathRules`, `FileKey.Of`/`NormalizeName`, `LedgerJsonContext` and `CoreJsonContext` (namespace `UasSort.Core.Json`), the ledger records `LedgerRecord`/`FileRecord`/`FolderRecord`/`SeenRecord`/`DecisionRecord`/`RevokeRecord`/`RunRecord`/`RunCard`/`RunRoots`/`TornRecord`/`CardDeleteRecord` with `[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]` and one `[JsonDerivedType]` per kind, `VerifyKind`, `FolderSource`, `DecisionKind`, `DestRoot`, `VerdictLevel`, `AuditCategory`, `CardIdentity`, `GeoPoint`, `SessionKey`, `LibraryFolderRef`, `LibraryFolder`, `LocationSource`, `RootListing`, `LibraryListings`, `LibraryFile`, `SetFolderListing`, `Settings`, `MapSettings`, `LayoutSettings`, `SettingsLoad`, `StoredClockMode`, `ClockModel`, `ClockMode`, `ClockSample`, `FsEntry`, `ListingResult`; ports `IDirectoryLister`, `ILedgerStore`, `ISettingsStore`; the fixed `GlobalUsings.Core.cs` in Core and Core.Tests, Task 02.1), Part 04 (`ClockModel.ToUtc` implemented by `ClockConversion`; `DroneClock.ApplyLearned` already covers learned-mode saving, so this part does not repeat it).

**Conventions used below (fixed by Part 02 and `00-interfaces.md`, which wins on names, namespaces and signatures).** Every shared model type, port and guard type is in the namespace `UasSort.Core` (Part 02); component code uses one namespace per folder (decision 1); path comparison goes through Part 02's `PathRules` (case-insensitive, `\` or `/`, drive roots keep their `\`), name+size keys through `FileKey.Of`/`FileKey.NormalizeName`, ledger file names through `LedgerPaths.IsLedgerFileName`, and JSON through Part 02's source-generated `LedgerJsonContext` (ledger lines) and `CoreJsonContext` (settings). Part 02 declares `LibraryFolder` and `LibraryIndex` as `partial` so this part adds `DaysIn` and every `LibraryIndex` member in its own files, in namespace `UasSort.Core`. New Core code goes to `src/UasSort.Core/Library/` (namespace `UasSort.Core.Library`), `src/UasSort.Core/Ledger/` (`UasSort.Core.Ledger`) and `src/UasSort.Core/Settings/` (namespace `UasSort.Core.Config`: a namespace `UasSort.Core.Settings` cannot exist next to the record `UasSort.Core.Settings`, CS0101). Core and Core.Tests get `System.Collections.Immutable` and every Core namespace (`UasSort.Core`, `.Json`, `.Library`, `.Ledger`, `.Config`, …) from the fixed `GlobalUsings.Core.cs` (Part 02 Task 02.1), and Core.Tests also gets `UasSort.Testing` and `Microsoft.Extensions.Time.Testing` from its `GlobalUsings.cs`; the explicit `using UasSort.Core…;` lines in the files below are redundant but allowed. Tests live in one folder per area under `tests/UasSort.Core.Tests/`, with the namespace `UasSort.Core.Tests.` plus the folder name: `Library/`, `Ledger/` and, for the settings code, `Config/` (namespace `UasSort.Core.Tests.Config`; a namespace `UasSort.Core.Tests.Settings` would shadow the `Settings` record in every Core.Tests file, CS0118). All test paths are Windows paths and the suite runs on Windows through `dotnet.exe` (`tools/r.sh` from WSL).

Test command pattern: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Name*"`; whole suite: `dotnet test --solution uas-sort.slnx`.

---

### Task 05.1: Event folder names

**Files:**
- Create: `src/UasSort.Core/Library/EventFolderName.cs`
- Test: `tests/UasSort.Core.Tests/Library/EventFolderNameTests.cs`

**Interfaces:**
```csharp
// Produces (defined here):
namespace UasSort.Core.Library;
public static partial class EventFolderName {      // ^(\d{4})-(\d{2})-(\d{2})(?:\s+(.*))?$ with ASCII digits and a real calendar date
  public static bool TryParse(string folderName, out DateOnly date, out string description);   // description "" when absent; trimmed
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Library/EventFolderNameTests.cs
using UasSort.Core.Library;

namespace UasSort.Core.Tests.Library;

public sealed class EventFolderNameTests
{
    [Theory]
    [InlineData("2026-09-27 Zachar Bay", 2026, 9, 27, "Zachar Bay")]
    [InlineData("2026-09-27", 2026, 9, 27, "")]
    [InlineData("2022-03-27 Makaha Valley", 2022, 3, 27, "Makaha Valley")]
    [InlineData("2026-07-25  Council  Road ", 2026, 7, 25, "Council  Road")]
    [InlineData("2024-02-29 Leap", 2024, 2, 29, "Leap")]
    public void EventFolderName_Matches(string name, int y, int m, int d, string description)
    {
        Assert.True(EventFolderName.TryParse(name, out DateOnly date, out string desc));
        Assert.Equal(new DateOnly(y, m, d), date);
        Assert.Equal(description, desc);
    }

    [Theory]
    [InlineData("Picture Offload")]
    [InlineData("Exports")]
    [InlineData("2026")]
    [InlineData("2026-07")]
    [InlineData("2026-13-01 Bad month")]
    [InlineData("2026-02-30 Bad day")]
    [InlineData("0000-01-01 Year zero")]
    [InlineData("2026-09-27Zachar")]
    [InlineData("x 2026-09-27")]
    [InlineData("２０２６-09-27 Fullwidth")]
    [InlineData(".uas-sort")]
    public void EventFolderName_Rejects(string name)
        => Assert.False(EventFolderName.TryParse(name, out _, out _));
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*EventFolderName_*"`
Expected: build fails with `CS0103: The name 'EventFolderName' does not exist in the current context`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Library/EventFolderName.cs
using System.Globalization;
using System.Text.RegularExpressions;

namespace UasSort.Core.Library;

/// <summary>An event folder's name (Ref §7.1): YYYY-MM-DD, optionally followed by whitespace and a description.</summary>
public static partial class EventFolderName
{
    [GeneratedRegex(@"^([0-9]{4})-([0-9]{2})-([0-9]{2})(?:\s+(.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool TryParse(string folderName, out DateOnly date, out string description)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        date = default;
        description = "";
        Match m = Pattern().Match(folderName);
        if (!m.Success) return false;
        int y = int.Parse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        int mo = int.Parse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        int d = int.Parse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        if (y < 1 || mo is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo)) return false;
        date = new DateOnly(y, mo, d);
        description = m.Groups[4].Success ? m.Groups[4].Value.Trim() : "";
        return true;
    }
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*EventFolderName_*"`
Expected: 16 passed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Library/EventFolderName.cs tests/UasSort.Core.Tests/Library/EventFolderNameTests.cs
git commit -m "feat(core): parse event folder names (Part 05)" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 05.2: `LibraryFolder.DaysIn`

Part 02 declares `LibraryFolder` as a `sealed partial record` without `DaysIn`; this task adds it in a second partial declaration: the local days of the folder's members in the folder's own ledger zone, else in the zone the caller passes (the zone of the group being compared, Ref §7.1).

**Files:**
- Create: `src/UasSort.Core/Library/LibraryFolder.DaysIn.cs` (partial of Part 02's `LibraryFolder` in `src/UasSort.Core/Model/Library.cs`)
- Test: `tests/UasSort.Core.Tests/Library/LibraryFolderDaysTests.cs`

**Interfaces:**
```csharp
// Consumes (Part 02): LibraryFolder(LibraryFolderRef Ref, ImmutableArray<DateTime> MemberStartsUtc, string? TzId,
//                                   GeoPoint? Centroid, LocationSource Loc); LibraryFolderRef(string FullPath, DateOnly NameDate, string Description)
// Produces (Ref §3 signature, added here through the partial record):
namespace UasSort.Core;
public sealed partial record LibraryFolder { public ImmutableHashSet<DateOnly> DaysIn(string tzId); }   // local dates in TzId ?? tzId
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Library/LibraryFolderDaysTests.cs
using UasSort.Core;

namespace UasSort.Core.Tests.Library;

public sealed class LibraryFolderDaysTests
{
    private static readonly LibraryFolderRef Council =
        new(@"C:\Lib\UAS Videos\2026\2026-07\2026-07-25 Council Road", new DateOnly(2026, 7, 25), "Council Road");

    // 06:30Z = Jul 25 22:30 AKDT = Jul 26 02:30 EDT; 20:00Z = Jul 26 in both zones
    private static readonly ImmutableArray<DateTime> Starts =
        [new DateTime(2026, 7, 26, 6, 30, 0, DateTimeKind.Utc), new DateTime(2026, 7, 26, 20, 0, 0, DateTimeKind.Utc)];

    [Fact]
    public void LibraryFolder_DaysIn_UsesTheFolderLedgerZoneFirst()
    {
        var f = new LibraryFolder(Council, Starts, "America/Anchorage", null, LocationSource.Ledger);
        Assert.Equal(new[] { new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26) }, f.DaysIn("America/New_York").Order());
    }

    [Fact]
    public void LibraryFolder_DaysIn_FallsBackToTheGivenZone()
    {
        var f = new LibraryFolder(Council, Starts, null, null, LocationSource.Unknown);
        Assert.Equal(new[] { new DateOnly(2026, 7, 26) }, f.DaysIn("America/New_York").Order());
        Assert.Equal(new[] { new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26) }, f.DaysIn("America/Anchorage").Order());
    }

    [Fact]
    public void LibraryFolder_DaysIn_NoMembersIsEmpty()
        => Assert.Empty(new LibraryFolder(Council, [], null, null, LocationSource.Unknown).DaysIn("America/Anchorage"));
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LibraryFolder_DaysIn_*"`
Expected: build fails with `CS1061: 'LibraryFolder' does not contain a definition for 'DaysIn'`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Library/LibraryFolder.DaysIn.cs
namespace UasSort.Core;

public sealed partial record LibraryFolder
{
    /// <summary>Local days of the members in the folder's ledger zone, else in <paramref name="tzId"/> (Ref §7.1).</summary>
    public ImmutableHashSet<DateOnly> DaysIn(string tzId)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(TzId ?? tzId);
        return MemberStartsUtc
            .Select(u => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(u, DateTimeKind.Utc), zone)))
            .ToImmutableHashSet();
    }
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LibraryFolder_DaysIn_*"`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Library/LibraryFolder.DaysIn.cs tests/UasSort.Core.Tests/Library/LibraryFolderDaysTests.cs
git commit -m "feat(core): implement LibraryFolder.DaysIn (Part 05)" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 05.3: Ledger record codec (`LedgerCodec`)

A thin, total wrapper over Part 02's `LedgerJsonContext`: one JSON line per record with `t` first (Ref §11 examples), and a parse that never throws but returns the reason (invalid JSON, unknown or missing `t`, `v` other than 1, empty `id` or `machine`). Part 09's `LedgerWriter` (the Platform `ILedgerWriter`) serialises every line with `LedgerCodec.Serialize` and Part 11's `LedgerSamples.AllRecordKindsV1()` builds its lines with it (decisions 31, 37); the reader below parses with `TryParse`.

**Files:**
- Create: `src/UasSort.Core/Ledger/LedgerCodec.cs`
- Create: `tests/UasSort.Core.Tests/Ledger/LedgerLines.cs` (record builders reused by later tasks)
- Test: `tests/UasSort.Core.Tests/Ledger/LedgerCodecTests.cs`

**Interfaces:**
```csharp
// Consumes (Part 02): closed record LedgerRecord(int V, string Id, string Machine) with [JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
//   and [JsonDerivedType] "file" FileRecord, "folder" FolderRecord, "seen" SeenRecord, "decision" DecisionRecord, "revoke" RevokeRecord,
//   "run" RunRecord (RunCard, RunRoots), "torn" TornRecord, "cardDelete" CardDeleteRecord — fields as Ref §3 "ledger records";
//   UasSort.Core.Json.LedgerJsonContext (compact, camelCase, AllowOutOfOrderMetadataProperties) with LedgerJsonContext.Default.LedgerRecord
// Produces (defined here):
namespace UasSort.Core.Ledger;
public static class LedgerCodec {
  public const int Version = 1;
  public static string Serialize(LedgerRecord record);                   // one line, no '\n', "t" first, nulls written
  public static LedgerRecord? TryParse(string line, out string? error);  // null + reason: invalid JSON, unknown/missing "t", v != 1, empty id or machine
}
```

- [ ] **Step 1: Write the record builders and the failing test**

```csharp
// tests/UasSort.Core.Tests/Ledger/LedgerLines.cs
using UasSort.Core.Ledger;
using UasSort.Core;

namespace UasSort.Core.Tests.Ledger;

/// <summary>Ledger record builders with the Ref §11 example values.</summary>
internal static class LedgerLines
{
    public const string Folder = @"C:\Lib\UAS Videos\.uas-sort";
    public static readonly RunCard Card = new("1A2B3C4D", null, "exFAT", "FC9113", "9f3c0a6d12e4b7a1");

    public static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0) => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    public static FileRecord FileRec(string id, string name, long size, string dest, string verify = "unbuffered",
        DateTime? at = null, string machine = "DESKTOP-A", double? lat = null, double? lon = null, string? tz = null,
        string? set = null, DateTime? captureUtc = null, string root = "video", string kind = "video",
        DateTime? sessionUtc = null, string? serial = null, string? xxh128 = "5e0c0000000000000000000000000001")
        => new(1, id, machine, "8f1c0001", at ?? Utc(2026, 9, 27, 21, 7, 2), kind, name, size, "DCIM/DJI_001/" + name, root, dest,
               xxh128, verify, Utc(2026, 9, 27, 18, 8, 1), captureUtc, captureUtc is null ? null : "Mvhd", lat, lon, tz,
               captureUtc is { } c ? DateOnly.FromDateTime(c) : null, sessionUtc, serial, set);

    public static FolderRecord FolderRec(string id, string path, string desc, DateOnly start, DateOnly end, string tz,
        double? lat = null, double? lon = null, string source = "created", string machine = "DESKTOP-A")
        => new(1, id, machine, "8f1c0001", path, desc, source, lat, lon, start, end, tz);

    public static SeenRecord Seen(string id, string name, long size, DateTime at, string? set = null, string machine = "DESKTOP-A")
        => new(1, id, machine, "8f1c0001", at, name, size, "DCIM/DJI_001/" + name, null, "New", "unticked", set);

    public static DecisionRecord Decision(string id, string name, long size, string kind = "assumedImported",
        DateTime? at = null, string? set = null, string machine = "DESKTOP-A")
        => new(1, id, machine, "8f1c0001", at ?? Utc(2026, 9, 27, 21, 40), kind, name, size, "DCIM/DJI_001/" + name, null,
               "confirmed by you", set);

    public static RevokeRecord Revoke(string id, string decisionId, string machine = "LAPTOP-B")
        => new(1, id, machine, Utc(2026, 10, 5, 2), decisionId);

    public static RunRecord Run(string id, string run, DateTime start, DateTime end, string videoRoot, string photoRoot,
        string verdict = "SafeWithAssumptions", string machine = "DESKTOP-A")
        => new(1, id, machine, run, start, end, "0.1.0", Card, new RunRoots(videoRoot, photoRoot), verdict,
               ImmutableDictionary<string, int>.Empty.Add("VerifiedThisRun", 17).Add("AssumedByRule", 40));

    public static TornRecord Torn(string id, int line, string machine = "DESKTOP-A")
        => new(1, id, machine, Utc(2026, 10, 6, 18, 2, 11), line);

    public static CardDeleteRecord CardDelete(string id, string name, long size)
        => new(1, id, "DESKTOP-A", "c4e20001", Utc(2026, 10, 12, 19, 30, 5), name, size, "DCIM/DJI_001/" + name,
               "DCIM/DJI_001/" + name, Utc(2026, 7, 26, 3, 26, 55), "InLedger", "in the history, verified", "beforeDate", Card, null);

    public static string Line(LedgerRecord r) => LedgerCodec.Serialize(r);

    /// <summary>Every record as one line, each terminated by '\n' (a cleanly closed file).</summary>
    public static string Text(params LedgerRecord[] records) => string.Concat(records.Select(r => Line(r) + "\n"));
}
```

```csharp
// tests/UasSort.Core.Tests/Ledger/LedgerCodecTests.cs
using UasSort.Core.Ledger;
using UasSort.Core;
using static UasSort.Core.Tests.Ledger.LedgerLines;

namespace UasSort.Core.Tests.Ledger;

public sealed class LedgerCodecTests
{
    private const string Dest = @"C:\Lib\UAS Videos\2026\2026-09\2026-09-27 Zachar Bay\DJI_20260927140627_0128_D.MP4";

    public static TheoryData<string, LedgerRecord> EveryKind => new()
    {
        { "file", FileRec("6d0e0001", "DJI_20260927140627_0128_D.MP4", 89_612_345, Dest, lat: 57.5504421, lon: -153.738973,
                       tz: "America/Anchorage", captureUtc: Utc(2026, 9, 27, 18, 6, 27), sessionUtc: Utc(2026, 9, 27, 17, 59, 28), serial: "1581F0001") },
        { "folder", FolderRec("f0000001", @"C:\Lib\UAS Videos\2026\2026-09\2026-09-27 Zachar Bay", "Zachar Bay",
                              new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), "America/Anchorage", 57.5415, -153.7409) },
        { "seen", Seen("s0000001", "DJI_20261002184012_0131_D.DNG", 27_399_100, Utc(2026, 10, 4, 20, 11)) },
        { "decision", Decision("a91", "PANO_0001.DNG", 13_751_808, set: "001_0087") },
        { "revoke", Revoke("r0000001", "a91") },
        { "run", Run("u0000001", "8f1c0001", Utc(2026, 9, 27, 21, 0), Utc(2026, 9, 27, 21, 30), @"C:\Lib\UAS Videos", @"C:\Lib\UAS Videos\Picture Offload") },
        { "torn", Torn("t0000001", 412) },
        { "cardDelete", CardDelete("c0000001", "DJI_20260725232655_0117_D.MP4", 1_234_567_890) },
    };

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void LedgerCodec_EveryKind_RoundTripsAsOneLineWithTFirst(string kind, LedgerRecord record)
    {
        string line = LedgerCodec.Serialize(record);
        Assert.StartsWith("{\"t\":\"" + kind + "\",", line, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", line, StringComparison.Ordinal);
        LedgerRecord? back = LedgerCodec.TryParse(line, out string? error);
        Assert.Null(error);
        Assert.NotNull(back);
        Assert.Equal(record.GetType(), back.GetType());
        Assert.Equal(line, LedgerCodec.Serialize(back));
    }

    [Fact]
    public void LedgerCodec_ParsesTheReferenceFileLine()
    {
        const string line = """
            {"t":"file","v":1,"id":"6d0e0001","machine":"DESKTOP-A","run":"8f1c0001","at":"2026-09-27T21:07:02Z","kind":"video","name":"DJI_20260927140627_0128_D.MP4","size":89612345,"src":"DCIM/DJI_001/DJI_20260927140627_0128_D.MP4","root":"video","dest":"C:\\Lib\\UAS Videos\\2026\\2026-09\\2026-09-27 Zachar Bay\\DJI_20260927140627_0128_D.MP4","xxh128":"5e0c0000000000000000000000000001","verify":"unbuffered","mtime":"2026-09-27T18:08:01Z","captureUtc":"2026-09-27T18:06:27Z","timeSource":"Mvhd","lat":57.5504421,"lon":-153.738973,"tz":"America/Anchorage","localDate":"2026-09-27","sessionUtc":"2026-09-27T17:59:28Z","serial":"1581F0001","set":null}
            """;
        var f = Assert.IsType<FileRecord>(LedgerCodec.TryParse(line, out string? error));
        Assert.Null(error);
        Assert.Equal("DJI_20260927140627_0128_D.MP4", f.Name);
        Assert.Equal(89_612_345, f.Size);
        Assert.Equal("unbuffered", f.Verify);
        Assert.Equal(Utc(2026, 9, 27, 18, 6, 27), f.CaptureUtc);
        Assert.Equal(DateTimeKind.Utc, f.At.Kind);
        Assert.Equal(new DateOnly(2026, 9, 27), f.LocalDate);
        Assert.Equal(Dest, f.Dest);
        Assert.Null(f.Set);
    }

    [Fact]
    public void LedgerCodec_AcceptsTheDiscriminatorOutOfOrder()
    {
        const string line = """{"v":1,"id":"r0000001","machine":"LAPTOP-B","at":"2026-10-05T02:00:00Z","decision":"a91","t":"revoke"}""";
        var r = Assert.IsType<RevokeRecord>(LedgerCodec.TryParse(line, out _));
        Assert.Equal("a91", r.Decision);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("""{"v":1,"id":"x1","machine":"A","at":"2026-10-05T02:00:00Z","decision":"a91"}""")]
    [InlineData("""{"t":"bogus","v":1,"id":"x1","machine":"A"}""")]
    [InlineData("""{"t":"revoke","v":2,"id":"x1","machine":"A","at":"2026-10-05T02:00:00Z","decision":"a91"}""")]
    [InlineData("""{"t":"revoke","v":1,"id":"","machine":"A","at":"2026-10-05T02:00:00Z","decision":"a91"}""")]
    [InlineData("""{"t":"revoke","v":1,"id":"x1","machine":"","at":"2026-10-05T02:00:00Z","decision":"a91"}""")]
    public void LedgerCodec_RejectsBadLinesWithAReason(string line)
    {
        Assert.Null(LedgerCodec.TryParse(line, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void LedgerCodec_NamesTheUnsupportedVersion()
    {
        LedgerCodec.TryParse("""{"t":"torn","v":2,"id":"t1","machine":"A","at":"2026-10-06T18:02:11Z","line":4}""", out string? error);
        Assert.Equal("unsupported v 2", error);
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LedgerCodec_*"`
Expected: build fails with `CS0103: The name 'LedgerCodec' does not exist in the current context`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Ledger/LedgerCodec.cs
using System.Text.Json;
using UasSort.Core.Json;
using UasSort.Core;

namespace UasSort.Core.Ledger;

/// <summary>One ledger record ⇄ one JSON line (Ref §11: JSON Lines, "t" discriminator, v:1).</summary>
public static class LedgerCodec
{
    public const int Version = 1;

    public static string Serialize(LedgerRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(record, LedgerJsonContext.Default.LedgerRecord);
    }

    public static LedgerRecord? TryParse(string line, out string? error)
    {
        ArgumentNullException.ThrowIfNull(line);
        LedgerRecord? record;
        try
        {
            record = JsonSerializer.Deserialize(line, LedgerJsonContext.Default.LedgerRecord);
        }
        catch (JsonException ex)
        {
            error = "invalid record: " + ex.Message;
            return null;
        }
        catch (NotSupportedException ex)          // unknown or missing "t" on the abstract base
        {
            error = "invalid record: " + ex.Message;
            return null;
        }
        error = record is null ? "empty record" : Validate(record);
        return error is null ? record : null;
    }

    private static string? Validate(LedgerRecord r)
        => r.V != Version ? $"unsupported v {r.V}"
         : string.IsNullOrWhiteSpace(r.Id) ? "missing id"
         : string.IsNullOrWhiteSpace(r.Machine) ? "missing machine"
         : null;
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LedgerCodec_*"`
Expected: 18 passed (8 kinds, 2 parse facts, 7 bad lines, 1 version fact).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Ledger/LedgerCodec.cs tests/UasSort.Core.Tests/Ledger/LedgerLines.cs tests/UasSort.Core.Tests/Ledger/LedgerCodecTests.cs
git commit -m "feat(core): add the ledger record codec (Part 05)" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 05.4: Ledger line parser (union, dedupe, torn lines, parse issues)

Rules (Ref §11, main spec §8): sources are read in path order (`OrdinalIgnoreCase`), so the union is deterministic; a record `id` seen before is dropped (own file and its OneDrive conflict copy hold the same lines); a final line without `\n` is skipped without an issue (an append writes the line and its `\n` together, so an unterminated line was never finished); a `torn` record at line k naming an earlier line n of the **same file** skips line n without an issue (it is written by `OpenOwn()` into the file whose tail it repaired); blank lines are ignored; any other unparseable line is a `LedgerParseIssue(file, line, reason)`.

**Files:**
- Create: `src/UasSort.Core/Ledger/LedgerParser.cs`
- Test: `tests/UasSort.Core.Tests/Ledger/LedgerParserTests.cs`

**Interfaces:**
```csharp
// Consumes: LedgerCodec (05.3); Part 02: LedgerRecord, TornRecord, LedgerParseIssue(string File, int Line, string Reason)
// Produces (defined here):
namespace UasSort.Core.Ledger;
public sealed record LedgerFileText(string FullPath, string Text);                 // one ledger*.jsonl, full path + whole text
public sealed record ParsedRecord(string File, int Line, LedgerRecord Record);  // Line is 1-based
public sealed record LedgerParseResult(ImmutableArray<ParsedRecord> Records, ImmutableArray<LedgerParseIssue> Issues,
                                       ImmutableArray<string> SourceFiles);   // SourceFiles in read order
public static class LedgerParser { public static LedgerParseResult Parse(IReadOnlyList<LedgerFileText> sources); }
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Ledger/LedgerParserTests.cs
using UasSort.Core.Ledger;
using static UasSort.Core.Tests.Ledger.LedgerLines;

namespace UasSort.Core.Tests.Ledger;

public sealed class LedgerParserTests
{
    private const string Dest = @"C:\Lib\UAS Videos\2026\2026-09\2026-09-27 Zachar Bay\";
    private static readonly string OwnA = Folder + @"\ledger-DESKTOP-A.jsonl";
    private static readonly string CopyA = Folder + @"\ledger-DESKTOP-A-LAPTOP-B.jsonl";
    private static readonly string OwnB = Folder + @"\ledger-LAPTOP-B.jsonl";

    private static readonly string F1 = Line(FileRec("f1", "DJI_20260927140127_0123_D.MP4", 105_764_094, Dest + "DJI_20260927140127_0123_D.MP4"));
    private static readonly string F2 = Line(FileRec("f2", "DJI_20260927140144_0124_D.MP4", 98_000_000, Dest + "DJI_20260927140144_0124_D.MP4"));
    private static readonly string F3 = Line(FileRec("f3", "DJI_20260927142416_0148_D.MP4", 77_000_000, Dest + "DJI_20260927142416_0148_D.MP4"));
    private const string Half = """{"t":"file","v":1,"id":"f9","machine":"DESKTOP-A","run":"8f1c""";

    private static string[] Ids(LedgerParseResult r) => [.. r.Records.Select(p => p.Record.Id)];

    [Fact]
    public void LedgerParser_UnionOfMachinesAndConflictCopies_DedupesById()
    {
        var r = LedgerParser.Parse([
            new LedgerFileText(OwnA, F1 + "\n" + F2 + "\n"),
            new LedgerFileText(CopyA, F1 + "\n" + F2 + "\n"),
            new LedgerFileText(OwnB, Line(Decision("d1", "DJI_20260725233000_0116_D.DNG", 27_411_200, machine: "LAPTOP-B")) + "\n")]);
        Assert.Equal(new[] { "d1", "f1", "f2" }, Ids(r).Order(StringComparer.Ordinal));
        Assert.Empty(r.Issues);
        Assert.Equal(new[] { CopyA, OwnA, OwnB }, r.SourceFiles);
    }

    [Fact]
    public void LedgerParser_TornFinalLine_IsSkippedWithoutAnIssue()
    {
        var r = LedgerParser.Parse([new LedgerFileText(OwnA, F1 + "\n" + Half)]);
        Assert.Equal(new[] { "f1" }, Ids(r));
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void LedgerParser_UnterminatedFinalLine_IsSkippedEvenWhenComplete()
    {
        var r = LedgerParser.Parse([new LedgerFileText(OwnA, F1 + "\n" + F2)]);
        Assert.Equal(new[] { "f1" }, Ids(r));
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void LedgerParser_BadMiddleLine_IsAParseIssueWithFileAndLine()
    {
        var r = LedgerParser.Parse([new LedgerFileText(OwnA, F1 + "\n" + "garbage\n" + F2 + "\n")]);
        Assert.Equal(new[] { "f1", "f2" }, Ids(r));
        var issue = Assert.Single(r.Issues);
        Assert.Equal(OwnA, issue.File);
        Assert.Equal(2, issue.Line);
        Assert.StartsWith("invalid record", issue.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DESKTOP-A")]
    [InlineData("LAPTOP-B")]
    public void LedgerParser_CrashMidAppendThenRepair_NoIssueAndLaterRecordsParse(string machine)
    {
        // own file after a crash (line 2 half written), then OpenOwn() appended "\n" + torn(2), then a normal run appended f3
        string path = Folder + $@"\ledger-{machine}.jsonl";
        string text = F1 + "\n" + Half + "\n" + Line(Torn("t1", 2, machine)) + "\n" + F3 + "\n";
        var r = LedgerParser.Parse([new LedgerFileText(path, text)]);
        Assert.Empty(r.Issues);
        Assert.Equal(new[] { "f1", "t1", "f3" }, Ids(r));
    }

    [Fact]
    public void LedgerParser_TornRecordCannotNameALaterLine()
    {
        string text = F1 + "\n" + Line(Torn("t1", 3)) + "\n" + "garbage\n" + F2 + "\n";
        var r = LedgerParser.Parse([new LedgerFileText(OwnA, text)]);
        Assert.Equal(3, Assert.Single(r.Issues).Line);
    }

    [Fact]
    public void LedgerParser_TornRecordOnlyAppliesToItsOwnFile()
    {
        var r = LedgerParser.Parse([
            new LedgerFileText(OwnA, F1 + "\n" + Line(Torn("t1", 1)) + "\n"),
            new LedgerFileText(OwnB, "garbage\n" + F2 + "\n")]);
        var issue = Assert.Single(r.Issues);
        Assert.Equal((OwnB, 1), (issue.File, issue.Line));
    }

    [Fact]
    public void LedgerParser_BlankLinesAndCrLf_AreIgnored()
    {
        var r = LedgerParser.Parse([new LedgerFileText(OwnA, F1 + "\r\n\r\n" + F2 + "\r\n")]);
        Assert.Equal(new[] { "f1", "f2" }, Ids(r));
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void LedgerParser_EmptyFile_GivesNothing()
    {
        var r = LedgerParser.Parse([new LedgerFileText(OwnA, "")]);
        Assert.Empty(r.Records);
        Assert.Empty(r.Issues);
        Assert.Equal(new[] { OwnA }, r.SourceFiles);
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LedgerParser_*"`
Expected: build fails with `CS0246: The type or namespace name 'LedgerFileText' could not be found`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Ledger/LedgerParser.cs
using UasSort.Core;

namespace UasSort.Core.Ledger;

public sealed record LedgerFileText(string FullPath, string Text);
public sealed record ParsedRecord(string File, int Line, LedgerRecord Record);
public sealed record LedgerParseResult(ImmutableArray<ParsedRecord> Records, ImmutableArray<LedgerParseIssue> Issues,
                                       ImmutableArray<string> SourceFiles);

/// <summary>The union of every ledger*.jsonl (Ref §11): dedupe by id, torn lines skipped, other bad lines reported.</summary>
public static class LedgerParser
{
    public static LedgerParseResult Parse(IReadOnlyList<LedgerFileText> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var records = ImmutableArray.CreateBuilder<ParsedRecord>();
        var issues = ImmutableArray.CreateBuilder<LedgerParseIssue>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        List<LedgerFileText> ordered = [.. sources.OrderBy(s => s.FullPath, StringComparer.OrdinalIgnoreCase)];

        foreach (LedgerFileText source in ordered)
        {
            string[] lines = source.Text.Split('\n');
            bool terminated = source.Text.Length == 0 || source.Text[^1] == '\n';
            int count = terminated ? lines.Length - 1 : lines.Length;   // after a final '\n', Split yields one empty tail
            var parsed = new LedgerRecord?[count];
            var errors = new string?[count];
            for (int i = 0; i < count; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line)) continue;
                parsed[i] = LedgerCodec.TryParse(line, out errors[i]);
            }

            var tornLines = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                if (parsed[i] is TornRecord t && t.Line >= 1 && t.Line < i + 1) tornLines.Add(t.Line);
            }

            for (int i = 0; i < count; i++)
            {
                int lineNo = i + 1;
                if (tornLines.Contains(lineNo)) continue;             // named by a later torn marker of this file
                if (!terminated && i == count - 1) continue;          // torn final line: never an issue
                if (errors[i] is { } error)
                {
                    issues.Add(new LedgerParseIssue(source.FullPath, lineNo, error));
                    continue;
                }
                if (parsed[i] is not { } record) continue;           // blank line
                if (ids.Add(record.Id)) records.Add(new ParsedRecord(source.FullPath, lineNo, record));
            }
        }

        return new LedgerParseResult(records.ToImmutable(), issues.ToImmutable(), [.. ordered.Select(s => s.FullPath)]);
    }
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LedgerParser_*"`
Expected: 10 passed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Ledger/LedgerParser.cs tests/UasSort.Core.Tests/Ledger/LedgerParserTests.cs
git commit -m "feat(core): parse the ledger union with torn-line handling (Part 05)" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 05.5: `LedgerSnapshot` builder and `LedgerReader`

Builds the in-memory snapshot of Ref §3 from the parsed union: `Files` keeps, per `FileKey`, the record with the strongest verification (`Unbuffered` &gt; `Cached` &gt; `NameSize`) and, among equal strength, the latest `at`, so a later weaker record (e.g. a re-run's AlreadyThere written as `verify:"nameSize"`) never downgrades a verified one (00-interfaces decision 5), `SetsByName` (member records grouped by set name and first-frame `captureUtc`), `Decisions` after every `revoke` (latest unrevoked per key), `Seen` (latest per key), `Folders` by full path case-insensitively (a later record of the same folder widens `Start`/`End`), `Runs`, `CardDeletes` (informational), the parse issues (incl. records whose field values are invalid), the source files and the status. `torn` records carry no data.

**Files:**
- Create: `src/UasSort.Core/Ledger/LedgerSnapshotBuilder.cs`
- Create: `src/UasSort.Core/Ledger/LedgerReader.cs`
- Create: `tests/UasSort.Core.Tests/Ledger/TestLedger.cs`
- Test: `tests/UasSort.Core.Tests/Ledger/LedgerSnapshotTests.cs`

**Interfaces:**
```csharp
// Consumes: LedgerParser, LedgerParseResult, LedgerFileText (05.4); `FileKey.Of`, `PathRules` (Part 02)
// Consumes (Part 02): LedgerSnapshot(Files, SetsByName, Decisions, Seen, Folders, Runs, CardDeletes, ParseIssues, SourceFiles, Status),
//   LedgerFile, LedgerSet, LedgerDecision, LedgerFolder, LedgerRun, LedgerCardDelete, LedgerParseIssue, LedgerFolderStatus,
//   LedgerFolderState, VerifyKind, FolderSource, DecisionKind, DestRoot, VerdictLevel, AuditCategory, CardIdentity, GeoPoint, SessionKey
// Produces (defined here):
namespace UasSort.Core.Ledger;
public static class LedgerSnapshotBuilder {    // Files: strongest Verify per key (Unbuffered > Cached > NameSize), latest AtUtc among equals
  public static LedgerSnapshot Build(LedgerParseResult parsed, LedgerFolderStatus status);
}
public static class LedgerReader { public static LedgerSnapshot Read(IReadOnlyList<LedgerFileText> sources, LedgerFolderStatus status); }
public static class LedgerSnapshots {
  public static LedgerSnapshot Empty(LedgerFolderStatus status);                           // no records; Folders/SetsByName OrdinalIgnoreCase
  public static LedgerFolderStatus Detached(string folder, IEnumerable<string> files);     // for sources that are not a live .uas-sort
}                                                                                          // (local backup mirrors, tests): State Ok, Exists true
```

- [ ] **Step 1: Write the helper and the failing test**

```csharp
// tests/UasSort.Core.Tests/Ledger/TestLedger.cs
using UasSort.Core.Ledger;
using UasSort.Core;

namespace UasSort.Core.Tests.Ledger;

internal static class TestLedger
{
    public static readonly string OwnFile = LedgerLines.Folder + @"\ledger-DESKTOP-A.jsonl";

    public static LedgerFolderStatus Status(params string[] files) => LedgerSnapshots.Detached(LedgerLines.Folder, files);

    public static LedgerSnapshot Snapshot(params LedgerRecord[] records)
        => LedgerReader.Read([new LedgerFileText(OwnFile, LedgerLines.Text(records))], Status(OwnFile));

    public static LedgerSnapshot Empty() => LedgerSnapshots.Empty(Status());
}
```

```csharp
// tests/UasSort.Core.Tests/Ledger/LedgerSnapshotTests.cs
using System.Globalization;
using UasSort.Core.Ledger;
using UasSort.Core;
using static UasSort.Core.Tests.Ledger.LedgerLines;

namespace UasSort.Core.Tests.Ledger;

public sealed class LedgerSnapshotTests
{
    private const string ZrelDir = @"C:\Lib\UAS Videos\2026\2026-09\2026-09-27 Zachar Bay";
    private const string N128 = "DJI_20260927140627_0128_D.MP4";

    [Fact]
    public void LedgerSnapshot_Files_OnePerKeyWithTypedFields()
    {
        var snap = TestLedger.Snapshot(
            FileRec("f1", N128, 89_612_345, ZrelDir + @"\" + N128, verify: "nameSize", at: Utc(2026, 9, 27, 21, 0), xxh128: null),
            FileRec("f2", N128, 89_612_345, ZrelDir + @"\" + N128, at: Utc(2026, 9, 27, 21, 7, 2), lat: 57.5504421, lon: -153.738973,
                    tz: "America/Anchorage", captureUtc: Utc(2026, 9, 27, 18, 6, 27), sessionUtc: Utc(2026, 9, 27, 17, 59, 28),
                    serial: "1581F0001"));
        LedgerFile f = Assert.Single(snap.Files).Value;
        Assert.Equal(FileKey.Of(N128, 89_612_345), f.Key);
        Assert.Equal(VerifyKind.Unbuffered, f.Verify);
        Assert.Equal(DestRoot.Video, f.Root);
        Assert.Equal(UInt128.Parse("5e0c0000000000000000000000000001", NumberStyles.HexNumber, CultureInfo.InvariantCulture), f.Xxh128);
        Assert.Equal(new GeoPoint(57.5504421, -153.738973), f.Point);
        Assert.Equal("America/Anchorage", f.TzId);
        Assert.Equal(new DateOnly(2026, 9, 27), f.LocalDate);
        Assert.Equal(Utc(2026, 9, 27, 18, 6, 27), f.CaptureUtc);
        Assert.True(f.Session is { DroneSerial: "1581F0001" } s && s.SessionUtc == Utc(2026, 9, 27, 17, 59, 28));
        Assert.Equal(("DESKTOP-A", "8f1c0001"), (f.Machine, f.Run));
        Assert.Empty(snap.ParseIssues);
    }

    [Fact]
    public void LedgerSnapshot_LaterNameSizeRecord_DoesNotDowngradeVerified()
    {
        // decision 5: strongest verification wins (Unbuffered > Cached > NameSize); a later weaker record never downgrades
        string dest = ZrelDir + @"\" + N128;
        var snap = TestLedger.Snapshot(
            FileRec("f1", N128, 89_612_345, dest, verify: "cached", at: Utc(2026, 9, 27, 21, 0)),
            FileRec("f2", N128, 89_612_345, dest, verify: "unbuffered", at: Utc(2026, 9, 27, 21, 7, 2)),
            FileRec("f3", N128, 89_612_345, dest, verify: "nameSize", at: Utc(2026, 10, 4, 20, 11), xxh128: null, machine: "LAPTOP-B"),
            FileRec("f4", N128, 89_612_345, dest, verify: "cached", at: Utc(2026, 10, 5, 9, 0), machine: "LAPTOP-B"));
        LedgerFile kept = Assert.Single(snap.Files).Value;
        Assert.Equal(VerifyKind.Unbuffered, kept.Verify);
        Assert.Equal(Utc(2026, 9, 27, 21, 7, 2), kept.AtUtc);
        Assert.Equal("DESKTOP-A", kept.Machine);
        Assert.NotNull(kept.Xxh128);

        // among equal strength the latest record wins
        var twice = TestLedger.Snapshot(
            FileRec("g1", N128, 89_612_345, dest, verify: "nameSize", at: Utc(2026, 9, 27, 21, 0), xxh128: null),
            FileRec("g2", N128, 89_612_345, dest, verify: "nameSize", at: Utc(2026, 10, 4, 20, 11), xxh128: null, machine: "LAPTOP-B"));
        LedgerFile latest = Assert.Single(twice.Files).Value;
        Assert.Equal((VerifyKind.NameSize, "LAPTOP-B"), (latest.Verify, latest.Machine));
        Assert.Empty(snap.ParseIssues);
    }

    [Fact]
    public void LedgerSnapshot_Decisions_AreTakenAfterRevokesFromAnyMachine()
    {
        var snap = TestLedger.Snapshot(
            Decision("a91", "DJI_20260725233000_0120_D.DNG", 27_411_200),
            Decision("b02", "DJI_20260726001000_0121_D.DNG", 27_000_000, kind: "dismissed"),
            Revoke("r1", "a91", machine: "LAPTOP-B"));
        LedgerDecision d = Assert.Single(snap.Decisions).Value;
        Assert.Equal("b02", d.Id);
        Assert.Equal(DecisionKind.Dismissed, d.Kind);
        Assert.Equal(FileKey.Of("DJI_20260726001000_0121_D.DNG", 27_000_000), d.Key);
    }

    [Fact]
    public void LedgerSnapshot_Seen_LatestPerKey()
    {
        var snap = TestLedger.Snapshot(
            Seen("s1", "X.DNG", 10, Utc(2026, 10, 2)),
            Seen("s2", "X.DNG", 10, Utc(2026, 10, 4, 20, 11)),
            Seen("s3", "Y.DNG", 11, Utc(2026, 10, 4)));
        Assert.Equal(2, snap.Seen.Count);
        Assert.Equal(Utc(2026, 10, 4, 20, 11), snap.Seen[FileKey.Of("x.dng", 10)]);
    }

    [Fact]
    public void LedgerSnapshot_Sets_GroupedByNameAndFirstFrame()
    {
        DateTime first = Utc(2026, 5, 25, 13, 30, 28), other = Utc(2026, 6, 1, 10, 0, 0);
        const string dir = @"C:\Lib\UAS Videos\Picture Offload\001_0087\";
        var snap = TestLedger.Snapshot(
            FileRec("p2", "PANO_0002.DNG", 12_882_432, dir + "PANO_0002.DNG", root: "photo", kind: "setMember", set: "001_0087", captureUtc: first),
            FileRec("p1", "PANO_0001.DNG", 13_751_808, dir + "PANO_0001.DNG", root: "photo", kind: "setMember", set: "001_0087", captureUtc: first),
            FileRec("q1", "PANO_0001.DNG", 13_000_000, @"C:\Lib\UAS Videos\Picture Offload\001_0087 2026-06-01\PANO_0001.DNG",
                    root: "photo", kind: "setMember", set: "001_0087", captureUtc: other));
        ImmutableArray<LedgerSet> sets = snap.SetsByName["001_0087"];
        Assert.Equal(2, sets.Length);
        Assert.Equal(first, sets[0].FirstFrameCaptureUtc);
        Assert.Equal(new[] { ("PANO_0001.DNG", 13_751_808L), ("PANO_0002.DNG", 12_882_432L) },
                     sets[0].Members.Select(m => (m.Member, m.Size)));
        Assert.Equal(other, sets[1].FirstFrameCaptureUtc);
        Assert.Equal(3, snap.Files.Count);
    }

    [Fact]
    public void LedgerSnapshot_Folders_CaseInsensitiveAndWidened()
    {
        var snap = TestLedger.Snapshot(
            FolderRec("fo1", ZrelDir, "Zachar Bay", new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), "America/Anchorage", 57.5415, -153.7409),
            FolderRec("fo2", ZrelDir.ToUpperInvariant() + @"\", "Zachar Bay", new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 28),
                      "America/Anchorage", source: "appended"));
        Assert.Single(snap.Folders);
        LedgerFolder f = snap.Folders[@"c:\lib\uas videos\2026\2026-09\2026-09-27 zachar bay"];
        Assert.Equal((new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 28)), (f.Start, f.End));
        Assert.Equal(FolderSource.Appended, f.Source);
        Assert.Equal(new GeoPoint(57.5415, -153.7409), f.Centroid);
        Assert.Equal("America/Anchorage", f.TzId);
    }

    [Fact]
    public void LedgerSnapshot_RunsAndCardDeletes()
    {
        var snap = TestLedger.Snapshot(
            Run("u1", "8f1c0001", Utc(2026, 9, 27, 21, 0), Utc(2026, 9, 27, 21, 30), @"C:\Lib\UAS Videos", @"C:\Lib\UAS Videos\Picture Offload"),
            CardDelete("c1", "DJI_20260725232655_0117_D.MP4", 1_234_567_890));
        LedgerRun run = Assert.Single(snap.Runs);
        Assert.Equal(VerdictLevel.SafeWithAssumptions, run.Verdict);
        Assert.Equal(0x1A2B3C4Du, run.Card.VolumeSerial);
        Assert.Equal("exFAT", run.Card.FileSystem);
        Assert.Equal(17, run.Counts[AuditCategory.VerifiedThisRun]);
        Assert.Equal(@"C:\Lib\UAS Videos", run.VideoRoot);
        Assert.Equal(@"C:\Lib\UAS Videos\Picture Offload", run.PhotoRoot);
        Assert.Equal("FC9113", run.Model);
        LedgerCardDelete cd = Assert.Single(snap.CardDeletes);
        Assert.Equal(FileKey.Of("DJI_20260725232655_0117_D.MP4", 1_234_567_890), cd.Key);
        Assert.Equal("InLedger", cd.Evidence);
        Assert.Empty(snap.Files);            // a cardDelete is read by no rule
    }

    [Fact]
    public void LedgerSnapshot_BadFieldValues_AreParseIssuesAndDropped()
    {
        var snap = TestLedger.Snapshot(
            FileRec("f1", N128, 1, ZrelDir + @"\" + N128, verify: "maybe"),
            Run("u1", "r1", Utc(2026, 9, 27), Utc(2026, 9, 27), @"C:\V", @"C:\P", verdict: "Great"),
            Decision("d1", "X.DNG", 1, kind: "perhaps"),
            FolderRec("fo1", ZrelDir, "Z", new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), "America/Anchorage", source: "moved"));
        Assert.Equal(new[] { 1, 2, 3, 4 }, snap.ParseIssues.Select(i => i.Line));
        Assert.Empty(snap.Files);
        Assert.Empty(snap.Runs);
        Assert.Empty(snap.Decisions);
        Assert.Empty(snap.Folders);
    }

    [Fact]
    public void LedgerSnapshots_Empty_CarriesTheStatus()
    {
        LedgerFolderStatus status = TestLedger.Status();
        LedgerSnapshot e = LedgerSnapshots.Empty(status);
        Assert.Same(status, e.Status);
        Assert.Empty(e.Files);
        Assert.Empty(e.SourceFiles);
        Assert.Empty(e.ParseIssues);
        Assert.False(e.Folders.ContainsKey(@"C:\anything"));
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LedgerSnapshot*"`
Expected: build fails with `CS0103: The name 'LedgerSnapshots' does not exist in the current context` (and `LedgerReader`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Ledger/LedgerSnapshotBuilder.cs
using System.Globalization;
using UasSort.Core;

namespace UasSort.Core.Ledger;

/// <summary>Parsed ledger union → <see cref="LedgerSnapshot"/> (Ref §3, §11 "Ledger snapshot and scale").</summary>
public static class LedgerSnapshotBuilder
{
    public static LedgerSnapshot Build(LedgerParseResult parsed, LedgerFolderStatus status)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        var issues = parsed.Issues.ToBuilder();
        var files = new Dictionary<FileKey, LedgerFile>();
        var sets = new Dictionary<(string Name, DateTime First), SortedDictionary<string, long>>();
        var decisions = new List<LedgerDecision>();
        var revoked = new HashSet<string>(StringComparer.Ordinal);
        var seen = new Dictionary<FileKey, DateTime>();
        var folders = new Dictionary<string, LedgerFolder>(StringComparer.OrdinalIgnoreCase);
        var runs = ImmutableArray.CreateBuilder<LedgerRun>();
        var cardDeletes = ImmutableArray.CreateBuilder<LedgerCardDelete>();

        foreach (ParsedRecord p in parsed.Records)
        {
            string? error;
            switch (p.Record)
            {
                case FileRecord f: error = AddFile(f, files, sets); break;
                case FolderRecord f: error = AddFolder(f, folders); break;
                case SeenRecord s: error = AddSeen(s, seen); break;
                case DecisionRecord d: error = AddDecision(d, decisions); break;
                case RevokeRecord r: error = string.IsNullOrEmpty(r.Decision) ? "missing decision id" : Revoke(r, revoked); break;
                case RunRecord r: error = AddRun(r, runs); break;
                case CardDeleteRecord c: error = AddCardDelete(c, cardDeletes); break;
                case TornRecord: error = null; break;
                default: error = "unknown record kind"; break;
            }
            if (error is not null) issues.Add(new LedgerParseIssue(p.File, p.Line, error));
        }

        var live = new Dictionary<FileKey, LedgerDecision>();
        foreach (LedgerDecision d in decisions)
        {
            if (revoked.Contains(d.Id)) continue;
            if (!live.TryGetValue(d.Key, out LedgerDecision? prev) || d.AtUtc > prev.AtUtc) live[d.Key] = d;
        }

        ImmutableDictionary<string, ImmutableArray<LedgerSet>> setsByName = sets
            .GroupBy(kv => kv.Key.Name, StringComparer.OrdinalIgnoreCase)
            .ToImmutableDictionary(
                g => g.Key,
                g => g.OrderBy(kv => kv.Key.First)
                      .Select(kv => new LedgerSet(kv.Key.Name, kv.Key.First,
                                                  [.. kv.Value.Select(m => (Member: m.Key, Size: m.Value))]))
                      .ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase);

        return new LedgerSnapshot(files.ToImmutableDictionary(), setsByName, live.ToImmutableDictionary(), seen.ToImmutableDictionary(),
                                  folders.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase), runs.ToImmutable(), cardDeletes.ToImmutable(),
                                  issues.ToImmutable(), parsed.SourceFiles, status);
    }

    private static string? AddFile(FileRecord f, Dictionary<FileKey, LedgerFile> files,
                                   Dictionary<(string Name, DateTime First), SortedDictionary<string, long>> sets)
    {
        if (string.IsNullOrEmpty(f.Name) || f.Size < 0) return "bad name or size";
        if (f.Src is null || f.Dest is null || f.Run is null) return "missing src, dest or run";
        DestRoot root;
        switch (f.Root)
        {
            case "video": root = DestRoot.Video; break;
            case "photo": root = DestRoot.Photo; break;
            default: return $"bad root '{f.Root}'";
        }
        VerifyKind verify;
        switch (f.Verify)
        {
            case "unbuffered": verify = VerifyKind.Unbuffered; break;
            case "cached": verify = VerifyKind.Cached; break;
            case "nameSize": verify = VerifyKind.NameSize; break;
            default: return $"bad verify '{f.Verify}'";
        }
        UInt128? hash = null;
        if (f.Xxh128 is { Length: > 0 } hex)
        {
            if (!UInt128.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out UInt128 h)) return $"bad xxh128 '{hex}'";
            hash = h;
        }
        GeoPoint? point = f.Lat is { } lat && f.Lon is { } lon ? new GeoPoint(lat, lon) : null;
        SessionKey? session = f.SessionUtc is { } su ? new SessionKey(f.Serial, Utc(su)) : null;
        DateTime? capture = f.CaptureUtc is { } c ? Utc(c) : null;
        var lf = new LedgerFile(FileKey.Of(f.Name, f.Size), f.Src, root, f.Dest, hash, verify, Utc(f.At), capture, point, f.Tz,
                                f.LocalDate, session, f.Set, f.Machine, f.Run);
        if (!files.TryGetValue(lf.Key, out LedgerFile? prev) || Replaces(lf, prev)) files[lf.Key] = lf;

        if (f.Set is { Length: > 0 } set)
        {
            var key = (set, capture ?? DateTime.MinValue);
            if (!sets.TryGetValue(key, out SortedDictionary<string, long>? members))
            {
                members = new SortedDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                sets[key] = members;
            }
            members[f.Name] = f.Size;
        }
        return null;
    }

    /// <summary>00-interfaces decision 5: the strongest verification per key wins (Unbuffered > Cached > NameSize);
    /// among equal strength the later record wins; a later weaker record never downgrades.</summary>
    private static bool Replaces(LedgerFile candidate, LedgerFile current)
    {
        int byStrength = Strength(candidate.Verify).CompareTo(Strength(current.Verify));
        return byStrength != 0 ? byStrength > 0 : candidate.AtUtc > current.AtUtc;
    }

    private static int Strength(VerifyKind verify) => verify switch
    {
        VerifyKind.Unbuffered => 2,
        VerifyKind.Cached => 1,
        _ => 0,
    };

    private static string? AddFolder(FolderRecord f, Dictionary<string, LedgerFolder> folders)
    {
        if (string.IsNullOrEmpty(f.Path)) return "missing path";
        FolderSource source;
        switch (f.Source)
        {
            case "created": source = FolderSource.Created; break;
            case "appended": source = FolderSource.Appended; break;
            case "cardLeftovers": source = FolderSource.CardLeftovers; break;
            default: return $"bad source '{f.Source}'";
        }
        GeoPoint? centroid = f.Lat is { } lat && f.Lon is { } lon ? new GeoPoint(lat, lon) : null;
        string key = PathRules.Normalize(f.Path);
        var folder = new LedgerFolder(key, f.Desc ?? "", source, centroid, f.Start, f.End, f.Tz);
        if (folders.TryGetValue(key, out LedgerFolder? prev))
        {
            folder = folder with
            {
                Start = prev.Start < folder.Start ? prev.Start : folder.Start,
                End = prev.End > folder.End ? prev.End : folder.End,
                Centroid = folder.Centroid ?? prev.Centroid,
            };
        }
        folders[key] = folder;
        return null;
    }

    private static string? AddSeen(SeenRecord s, Dictionary<FileKey, DateTime> seen)
    {
        if (string.IsNullOrEmpty(s.Name) || s.Size < 0) return "bad name or size";
        FileKey key = FileKey.Of(s.Name, s.Size);
        DateTime at = Utc(s.At);
        if (!seen.TryGetValue(key, out DateTime prev) || at > prev) seen[key] = at;
        return null;
    }

    private static string? AddDecision(DecisionRecord d, List<LedgerDecision> decisions)
    {
        if (string.IsNullOrEmpty(d.Name) || d.Size < 0) return "bad name or size";
        DecisionKind kind;
        switch (d.Kind)
        {
            case "assumedImported": kind = DecisionKind.AssumedImported; break;
            case "dismissed": kind = DecisionKind.Dismissed; break;
            default: return $"bad decision kind '{d.Kind}'";
        }
        decisions.Add(new LedgerDecision(d.Id, FileKey.Of(d.Name, d.Size), kind, Utc(d.At), d.Machine, d.Set, d.Why ?? ""));
        return null;
    }

    private static string? Revoke(RevokeRecord r, HashSet<string> revoked)
    {
        revoked.Add(r.Decision);
        return null;
    }

    private static string? AddRun(RunRecord r, ImmutableArray<LedgerRun>.Builder runs)
    {
        if (r.Card is null || r.Roots is null) return "missing card or roots";
        if (!Enum.TryParse(r.Verdict, ignoreCase: false, out VerdictLevel verdict) || !Enum.IsDefined(verdict)
            || r.Verdict.Any(char.IsDigit)) return $"bad verdict '{r.Verdict}'";
        if (!uint.TryParse(r.Card.Serial, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint serial))
            return $"bad card serial '{r.Card.Serial}'";
        var counts = ImmutableDictionary.CreateBuilder<AuditCategory, int>();
        foreach ((string name, int n) in r.Counts ?? ImmutableDictionary<string, int>.Empty)
        {
            if (Enum.TryParse(name, ignoreCase: false, out AuditCategory cat) && Enum.IsDefined(cat) && !name.Any(char.IsDigit)) counts[cat] = n;
        }
        var card = new CardIdentity(serial, r.Card.Label, r.Card.Fs, 0);   // RunCard carries no capacity (Part 05 gap list)
        runs.Add(new LedgerRun(r.Run, r.Machine, Utc(r.Start), Utc(r.End), r.App, card, r.Card.Model, r.Card.InventoryHash,
                               r.Roots.Video, r.Roots.Photo, verdict, counts.ToImmutable()));
        return null;
    }

    private static string? AddCardDelete(CardDeleteRecord c, ImmutableArray<LedgerCardDelete>.Builder cardDeletes)
    {
        if (string.IsNullOrEmpty(c.Name) || c.Size < 0) return "bad name or size";
        cardDeletes.Add(new LedgerCardDelete(c.Run, Utc(c.At), FileKey.Of(c.Name, c.Size), c.Src, c.Evidence, c.Machine));
        return null;
    }

    private static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };
}
```

```csharp
// src/UasSort.Core/Ledger/LedgerReader.cs
using UasSort.Core;

namespace UasSort.Core.Ledger;

public static class LedgerReader
{
    /// <summary>Parse the union of <paramref name="sources"/> and build the snapshot (Ref §4.1 ILedgerStore.Load, pure half).</summary>
    public static LedgerSnapshot Read(IReadOnlyList<LedgerFileText> sources, LedgerFolderStatus status)
        => LedgerSnapshotBuilder.Build(LedgerParser.Parse(sources), status);
}

public static class LedgerSnapshots
{
    public static LedgerSnapshot Empty(LedgerFolderStatus status)
        => new(ImmutableDictionary<FileKey, LedgerFile>.Empty,
               ImmutableDictionary<string, ImmutableArray<LedgerSet>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
               ImmutableDictionary<FileKey, LedgerDecision>.Empty,
               ImmutableDictionary<FileKey, DateTime>.Empty,
               ImmutableDictionary<string, LedgerFolder>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
               [], [], [], [], status);

    /// <summary>A status for ledger text that doesn't come from a live .uas-sort folder (backup mirrors, tests).</summary>
    public static LedgerFolderStatus Detached(string folder, IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return new(folder, LedgerFolderState.Ok, Exists: true, InSyncRoot: false, Pinned: false, Writable: false, [.. files], [], []);
    }
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LedgerSnapshot*"`
Expected: 9 passed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Ledger/LedgerSnapshotBuilder.cs src/UasSort.Core/Ledger/LedgerReader.cs tests/UasSort.Core.Tests/Ledger/TestLedger.cs tests/UasSort.Core.Tests/Ledger/LedgerSnapshotTests.cs
git commit -m "feat(core): build the ledger snapshot with revokes, sets and strongest verification (Part 05)" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 05.6: Ledger folder status (attributes first)

`ILedgerStore.Check()` (Part 09) gathers only attributes, a top-level listing of the video root's `.uas-sort` folder (`recurse: false`, `excludeDirNames = {}`), the sync-root test and the security-descriptor write check, then calls this pure builder (decision 37); Part 06's `UasSort.Testing.FakeLedgerStore.Check()` calls it too. Only top-level `ledger*.jsonl` files count; `notes.txt`, a `sub\` folder and anything in it never do. State is the most severe that applies: VideoRootMissing &gt; CloudOnly &gt; Unwritable &gt; NotPinned &gt; Missing &gt; Empty &gt; Ok (Ref §3, §11 "Ledger folder status").

**Files:**
- Create: `src/UasSort.Core/Ledger/LedgerFolderStatusBuilder.cs`
- Test: `tests/UasSort.Core.Tests/Ledger/LedgerFolderStatusTests.cs`

**Interfaces:**
```csharp
// Consumes (Part 02): LedgerPaths.For(videoRoot), LedgerPaths.OwnFile(videoRoot, machine), LedgerFolderStatus, LedgerFolderState;
//                     LedgerPaths.IsLedgerFileName(fileName); FsEntry, ListingResult (ports); PathRules
// Produces (defined here):
namespace UasSort.Core.Ledger;
public sealed record LedgerFolderFacts(bool VideoRootExists,
                                       FsEntry? Folder,          // the attributes of <videoRoot>\.uas-sort; null = missing
                                       ListingResult? TopLevel,  // listing of the folder, recurse:false, excludeDirNames {}; null when missing
                                       bool InSyncRoot, bool Writable);
public static class LedgerFolderStatusBuilder {
  public const uint PinnedBit = 0x80000;                      // FILE_ATTRIBUTE_PINNED
  public const uint CloudBits = 0x400000 | 0x40000 | 0x1000;  // RecallOnDataAccess | RecallOnOpen | Offline
  public static LedgerFolderStatus Build(string videoRoot, string machine, LedgerFolderFacts facts);
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Ledger/LedgerFolderStatusTests.cs
using UasSort.Core.Ledger;
using UasSort.Core;

namespace UasSort.Core.Tests.Ledger;

public sealed class LedgerFolderStatusTests
{
    private const string V = @"C:\Lib\UAS Videos";
    private const string L = V + @"\.uas-sort";
    private const string Machine = "DESKTOP-A";
    private static readonly DateTime T = new(2026, 9, 27, 20, 0, 0, DateTimeKind.Utc);

    private static FsEntry Dir(string path, uint attrs = 0x10) => new(path, PathRules.FileName(path), true, 0, T, T, T, attrs);
    private static FsEntry Fil(string path, uint attrs = 0x20) => new(path, PathRules.FileName(path), false, 100, T, T, T, attrs);
    private static ListingResult Listing(params FsEntry[] entries) => new([.. entries], []);

    private static LedgerFolderStatus Build(FsEntry? folder, ListingResult? top, bool inSync = true, bool writable = true, bool rootExists = true)
        => LedgerFolderStatusBuilder.Build(V, Machine, new LedgerFolderFacts(rootExists, folder, top, inSync, writable));

    private static readonly FsEntry PinnedFolder = Dir(L, 0x10 | 0x80000);

    [Fact]
    public void LedgerStatus_DerivedLocation()
    {
        Assert.Equal(@"D:\Vids\.uas-sort", LedgerPaths.For(@"D:\Vids"));
        Assert.Equal(@"D:\Vids\.uas-sort\ledger-PC1.jsonl", LedgerPaths.OwnFile(@"D:\Vids", "PC1"));
        Assert.Equal(L, Build(null, null).Folder);
    }

    [Fact]
    public void LedgerStatus_MissingFolder_IsMissing()
    {
        LedgerFolderStatus s = Build(null, null);
        Assert.Equal(LedgerFolderState.Missing, s.State);
        Assert.False(s.Exists);
        Assert.Empty(s.LedgerFiles);
    }

    [Fact]
    public void LedgerStatus_MissingVideoRoot_IsVideoRootMissing()
        => Assert.Equal(LedgerFolderState.VideoRootMissing, Build(null, null, rootExists: false).State);

    [Fact]
    public void LedgerStatus_FolderWithoutLedgerFiles_IsEmpty()
        => Assert.Equal(LedgerFolderState.Empty, Build(PinnedFolder, Listing(Fil(L + @"\notes.txt"), Fil(L + @"\ledgers.txt"))).State);

    [Fact]
    public void LedgerStatus_OnlyTopLevelLedgerJsonlCount()
    {
        LedgerFolderStatus s = Build(PinnedFolder, Listing(
            Fil(L + @"\ledger-DESKTOP-A.jsonl"),
            Fil(L + @"\ledger-LAPTOP-B.jsonl"),
            Fil(L + @"\ledger-LAPTOP-B-DESKTOP-A.jsonl"),   // OneDrive conflict copy
            Fil(@"C:\LIB\UAS VIDEOS\.UAS-SORT\LEDGER-C.JSONL"),
            Fil(L + @"\notes.txt"),
            Dir(L + @"\sub"),
            Fil(L + @"\sub\ledger-D.jsonl"),
            Fil(V + @"\.uas-sort2\ledger-E.jsonl"),
            Fil(V + @"\ledger-X.jsonl")));
        Assert.Equal(LedgerFolderState.Ok, s.State);
        Assert.True(s.Pinned);
        Assert.Equal(4, s.LedgerFiles.Length);
        Assert.DoesNotContain(s.LedgerFiles, f => f.Contains("notes", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(s.LedgerFiles, f => f.Contains(@"\sub\", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(s.LedgerFiles, f => f.Contains(".uas-sort2", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(s.LedgerFiles, f => f.EndsWith(@"UAS Videos\ledger-X.jsonl", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, s.OtherMachineFiles.Length);
        Assert.DoesNotContain(L + @"\ledger-DESKTOP-A.jsonl", s.OtherMachineFiles);
    }

    [Fact]
    public void LedgerStatus_UnpinnedInSyncRoot_IsNotPinned_OutsideSyncRootIsOk()
    {
        ListingResult top = Listing(Fil(L + @"\ledger-DESKTOP-A.jsonl"));
        Assert.Equal(LedgerFolderState.NotPinned, Build(Dir(L), top).State);
        Assert.Equal(LedgerFolderState.Ok, Build(Dir(L), top, inSync: false).State);
    }

    [Fact]
    public void LedgerStatus_CloudOnlyLedgerFile_IsCloudOnlyAndBeatsUnwritable()
    {
        ListingResult top = Listing(Fil(L + @"\ledger-DESKTOP-A.jsonl"), Fil(L + @"\ledger-LAPTOP-B.jsonl", 0x400000 | 0x20));
        LedgerFolderStatus s = Build(PinnedFolder, top, writable: false);
        Assert.Equal(LedgerFolderState.CloudOnly, s.State);
        Assert.Equal(new[] { L + @"\ledger-LAPTOP-B.jsonl" }, s.CloudOnlyFiles);
        Assert.Equal(LedgerFolderState.CloudOnly, Build(PinnedFolder, Listing(Fil(L + @"\ledger-B.jsonl", 0x1000))).State);
        Assert.Equal(LedgerFolderState.CloudOnly, Build(PinnedFolder, Listing(Fil(L + @"\ledger-B.jsonl", 0x40000))).State);
    }

    [Fact]
    public void LedgerStatus_Unwritable_BeatsNotPinnedAndMissing()
    {
        ListingResult top = Listing(Fil(L + @"\ledger-DESKTOP-A.jsonl"));
        Assert.Equal(LedgerFolderState.Unwritable, Build(Dir(L), top, writable: false).State);
        Assert.Equal(LedgerFolderState.Unwritable, Build(null, null, writable: false).State);
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LedgerStatus_*"`
Expected: build fails with `CS0246: The type or namespace name 'LedgerFolderFacts' could not be found`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Ledger/LedgerFolderStatusBuilder.cs
using UasSort.Core;

namespace UasSort.Core.Ledger;

public sealed record LedgerFolderFacts(bool VideoRootExists, FsEntry? Folder, ListingResult? TopLevel, bool InSyncRoot, bool Writable);

/// <summary>Attributes-only folder status (Ref §11): computed before any ledger file is opened.</summary>
public static class LedgerFolderStatusBuilder
{
    public const uint PinnedBit = 0x80000;
    public const uint CloudBits = 0x400000 | 0x40000 | 0x1000;

    public static LedgerFolderStatus Build(string videoRoot, string machine, LedgerFolderFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        string folder = LedgerPaths.For(videoRoot);
        bool exists = facts.VideoRootExists && facts.Folder is { IsDirectory: true };
        bool pinned = exists && (facts.Folder!.RawAttributes & PinnedBit) != 0;

        ImmutableArray<FsEntry> ledgerEntries = exists && facts.TopLevel is { } top
            ? [.. top.Entries
                  .Where(e => !e.IsDirectory
                              && PathRules.Parent(e.FullPath) is { } parent && PathRules.Equal(parent, folder)
                              && LedgerPaths.IsLedgerFileName(PathRules.FileName(e.FullPath)))
                  .OrderBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase)]
            : [];

        string own = LedgerPaths.OwnFile(videoRoot, machine);
        ImmutableArray<string> files = [.. ledgerEntries.Select(e => e.FullPath)];
        ImmutableArray<string> cloudOnly = [.. ledgerEntries.Where(e => (e.RawAttributes & CloudBits) != 0).Select(e => e.FullPath)];
        ImmutableArray<string> others = [.. files.Where(f => !PathRules.Equal(f, own))];

        LedgerFolderState state =
            !facts.VideoRootExists ? LedgerFolderState.VideoRootMissing
            : !cloudOnly.IsEmpty ? LedgerFolderState.CloudOnly
            : !facts.Writable ? LedgerFolderState.Unwritable
            : exists && facts.InSyncRoot && !pinned ? LedgerFolderState.NotPinned
            : !exists ? LedgerFolderState.Missing
            : files.IsEmpty ? LedgerFolderState.Empty
            : LedgerFolderState.Ok;

        return new LedgerFolderStatus(folder, state, exists, facts.InSyncRoot, pinned, facts.Writable, files, cloudOnly, others);
    }
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LedgerStatus_*"`
Expected: 8 passed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Ledger/LedgerFolderStatusBuilder.cs tests/UasSort.Core.Tests/Ledger/LedgerFolderStatusTests.cs
git commit -m "feat(core): derive the ledger folder status from attributes (Part 05)" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 05.7: `LedgerLoader` — open only the listed, local ledger files

The Core half of `ILedgerStore.Load()` (Part 09's `LedgerStore.Load` and Part 06's `FakeLedgerStore.Load` both call it, decision 37): given the status from `Check()`, it opens each path in `status.LedgerFiles` through the caller's guarded opener (Part 09 passes `LedgerStore`'s policy-checked `ReadData` open with `FileShare.ReadWrite`) and reads the union. It opens **nothing** when the state is VideoRootMissing, Missing or CloudOnly (a cloud-only file blocks the whole load, Ref §13 fake-FS tripwire), so a cloud-only file is never hydrated and `notes.txt` or `sub\` are never touched.

**Files:**
- Create: `src/UasSort.Core/Ledger/LedgerLoader.cs`
- Test: `tests/UasSort.Core.Tests/Ledger/LedgerLoaderTests.cs`

**Interfaces:**
```csharp
// Consumes: LedgerFolderStatus (05.6 builder), LedgerReader, LedgerSnapshots, LedgerFileText (05.4/05.5)
// Produces (defined here):
namespace UasSort.Core.Ledger;
public static class LedgerLoader {
  public static LedgerSnapshot Load(LedgerFolderStatus status, Func<string, Stream> openRead);   // UTF-8 (BOM tolerated)
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Ledger/LedgerLoaderTests.cs
using System.Text;
using UasSort.Core.Ledger;
using UasSort.Core;
using static UasSort.Core.Tests.Ledger.LedgerLines;

namespace UasSort.Core.Tests.Ledger;

public sealed class LedgerLoaderTests
{
    private const string V = @"C:\Lib\UAS Videos";
    private const string L = V + @"\.uas-sort";
    private static readonly DateTime T = new(2026, 9, 27, 20, 0, 0, DateTimeKind.Utc);

    private sealed class Opener(Dictionary<string, string> files)
    {
        public List<string> Opened { get; } = [];
        public Stream Open(string path)
        {
            Opened.Add(path);
            return new MemoryStream(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(files[path])).ToArray());
        }
    }

    private static FsEntry Fil(string path, uint attrs = 0x20) => new(path, PathRules.FileName(path), false, 100, T, T, T, attrs);

    private static LedgerFolderStatus Status(bool writable, params FsEntry[] top)
        => LedgerFolderStatusBuilder.Build(V, "DESKTOP-A", new LedgerFolderFacts(true,
               new FsEntry(L, ".uas-sort", true, 0, T, T, T, 0x10 | 0x80000), new ListingResult([.. top], []), InSyncRoot: true, writable));

    private static readonly string Dest = V + @"\2026\2026-09\2026-09-27 Zachar Bay\DJI_20260927140127_0123_D.MP4";

    private static Dictionary<string, string> Files() => new(StringComparer.OrdinalIgnoreCase)
    {
        [L + @"\ledger-DESKTOP-A.jsonl"] = Text(FileRec("f1", "DJI_20260927140127_0123_D.MP4", 105_764_094, Dest),
                                                Decision("a91", "DJI_20260725233000_0120_D.DNG", 27_411_200),
                                                FolderRec("fo1", V + @"\2026\2026-09\2026-09-27 Café Bay", "Café Bay",
                                                          new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), "America/Anchorage")),
        [L + @"\ledger-LAPTOP-B.jsonl"] = Text(Revoke("r1", "a91")),
        [L + @"\ledger-DESKTOP-A-LAPTOP-B.jsonl"] = Text(FileRec("f1", "DJI_20260927140127_0123_D.MP4", 105_764_094, Dest)),
        [L + @"\notes.txt"] = "never read",
        [L + @"\sub\ledger-C.jsonl"] = "never read",
    };

    [Fact]
    public void LedgerLoader_ReadsOnlyTopLevelLedgerFiles()
    {
        var opener = new Opener(Files());
        LedgerFolderStatus status = Status(true,
            Fil(L + @"\ledger-DESKTOP-A.jsonl"), Fil(L + @"\ledger-LAPTOP-B.jsonl"), Fil(L + @"\ledger-DESKTOP-A-LAPTOP-B.jsonl"),
            Fil(L + @"\notes.txt"), Fil(L + @"\sub\ledger-C.jsonl"));
        LedgerSnapshot snap = LedgerLoader.Load(status, opener.Open);

        Assert.Equal(3, opener.Opened.Count);
        Assert.DoesNotContain(opener.Opened, p => p.EndsWith("notes.txt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(opener.Opened, p => p.Contains(@"\sub\", StringComparison.OrdinalIgnoreCase));
        Assert.Single(snap.Files);                   // f1 from the own file and its conflict copy, deduped by id
        Assert.Empty(snap.Decisions);                // a91 revoked on LAPTOP-B
        Assert.Equal("Café Bay", Assert.Single(snap.Folders).Value.Description);
        Assert.Equal(3, snap.SourceFiles.Length);
        Assert.Same(status, snap.Status);
        Assert.Empty(snap.ParseIssues);
    }

    [Fact]
    public void LedgerLoader_CloudOnlyLedgerFile_OpensNothing()
    {
        var opener = new Opener(Files());
        LedgerFolderStatus status = Status(true, Fil(L + @"\ledger-DESKTOP-A.jsonl"), Fil(L + @"\ledger-LAPTOP-B.jsonl", 0x400000 | 0x20));
        LedgerSnapshot snap = LedgerLoader.Load(status, opener.Open);
        Assert.Empty(opener.Opened);
        Assert.Equal(LedgerFolderState.CloudOnly, snap.Status.State);
        Assert.Empty(snap.Files);
    }

    [Fact]
    public void LedgerLoader_MissingFolder_OpensNothing()
    {
        var opener = new Opener(Files());
        LedgerFolderStatus status = LedgerFolderStatusBuilder.Build(V, "DESKTOP-A", new LedgerFolderFacts(true, null, null, true, true));
        LedgerSnapshot snap = LedgerLoader.Load(status, opener.Open);
        Assert.Empty(opener.Opened);
        Assert.Equal(LedgerFolderState.Missing, snap.Status.State);
    }

    [Fact]
    public void LedgerLoader_UnwritableFolder_StillReads()
    {
        var opener = new Opener(Files());
        LedgerSnapshot snap = LedgerLoader.Load(Status(false, Fil(L + @"\ledger-DESKTOP-A.jsonl")), opener.Open);
        Assert.Equal(LedgerFolderState.Unwritable, snap.Status.State);
        Assert.Single(snap.Files);
        Assert.Single(snap.Decisions);
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LedgerLoader_*"`
Expected: build fails with `CS0103: The name 'LedgerLoader' does not exist in the current context`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Ledger/LedgerLoader.cs
using System.Text;
using UasSort.Core;

namespace UasSort.Core.Ledger;

/// <summary>Reads the listed local ledger files through the caller's guarded opener (Ref §4.1 ILedgerStore.Load, §4.3 exemption 1).</summary>
public static class LedgerLoader
{
    public static LedgerSnapshot Load(LedgerFolderStatus status, Func<string, Stream> openRead)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(openRead);
        if (status.State is LedgerFolderState.VideoRootMissing or LedgerFolderState.Missing or LedgerFolderState.CloudOnly
            || status.LedgerFiles.IsEmpty)
        {
            return LedgerSnapshots.Empty(status);
        }

        var sources = new List<LedgerFileText>(status.LedgerFiles.Length);
        foreach (string path in status.LedgerFiles)
        {
            using Stream stream = openRead(path);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            sources.Add(new LedgerFileText(path, reader.ReadToEnd()));
        }
        return LedgerReader.Read(sources, status);
    }
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LedgerLoader_*"`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Ledger/LedgerLoader.cs tests/UasSort.Core.Tests/Ledger/LedgerLoaderTests.cs
git commit -m "feat(core): load the ledger union from listed local files only (Part 05)" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 05.8: Collect library entries (overlaps removed, `.uas-sort` dropped)

The first stage of `LibraryIndex.Build` (Ref §7.1 Listing): walk the video root, the photo root and every previous photo root; skip unavailable roots (reported); drop every entry at or under `LedgerPaths.For(videoRoot)` even if a lister returned it (defence in depth behind `excludeDirNames = {".uas-sort"}`); remove overlaps (the photo root inside the video root is listed twice, kept once); carry listing errors (access denied on a subfolder → the folder is simply missing from the index; Part 06 raises the warning).

**Files:**
- Create: `src/UasSort.Core/Library/LibraryEntries.cs`
- Create: `tests/UasSort.Core.Tests/Library/LibraryFixture.cs` (listing builder reused by 05.9–05.11)
- Test: `tests/UasSort.Core.Tests/Library/LibraryEntriesTests.cs`

**Interfaces:**
```csharp
// Consumes (Part 02): LibraryListings(RootListing Video, RootListing Photo, ImmutableArray<RootListing> PreviousPhoto),
//   RootListing(string Root, DestRoot Kind, bool IsPrevious, bool Available, ListingResult Listing), FsEntry, ListingResult, LedgerPaths
// Produces (defined here):
namespace UasSort.Core.Library;
public sealed record LibraryEntry(FsEntry Entry, RootListing Root);
public sealed record CollectedLibrary(ImmutableArray<LibraryEntry> Files, ImmutableArray<LibraryEntry> Directories,
                                      ImmutableArray<string> UnavailableRoots, ImmutableArray<(string Path, int Win32Error)> Errors);
public static class LibraryEntries { public static CollectedLibrary Collect(LibraryListings listings); }
```

- [ ] **Step 1: Write the fixture and the failing test**

```csharp
// tests/UasSort.Core.Tests/Library/LibraryFixture.cs
using UasSort.Core;

namespace UasSort.Core.Tests.Library;

/// <summary>Builds <see cref="LibraryListings"/> as the lister returns them (directories included, recursive, relative paths).
/// It deliberately does NOT skip .uas-sort, so the tests prove the index drops that subtree itself.</summary>
internal sealed class LibraryFixture
{
    public const string VideoRoot = @"C:\Lib\UAS Videos";
    public const string PhotoRoot = VideoRoot + @"\Picture Offload";
    public static readonly DateTime T = Utc(2026, 9, 27, 20, 0, 0);

    private readonly List<FsEntry> _entries = [];
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _previous = [];
    private readonly HashSet<string> _unavailable = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Path, int Win32Error)> _errors = [];
    private string _photoRoot = PhotoRoot;

    public static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0) => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    public LibraryFixture WithFile(string fullPath, long size, DateTime mtimeUtc, uint attributes = 0x20)
    {
        AddParents(fullPath);
        _entries.Add(new FsEntry(fullPath, "", false, size, mtimeUtc, mtimeUtc, mtimeUtc, attributes));
        return this;
    }

    public LibraryFixture WithDir(string fullPath)
    {
        AddParents(fullPath);
        AddDir(fullPath);
        return this;
    }

    public LibraryFixture WithPhotoRoot(string root) { _photoRoot = root; return this; }
    public LibraryFixture WithPrevious(string root) { _previous.Add(root); return this; }
    public LibraryFixture WithUnavailable(string root) { _unavailable.Add(root); return this; }
    public LibraryFixture WithError(string path, int win32Error) { _errors.Add((path, win32Error)); return this; }

    public LibraryListings Build()
        => new(Listing(VideoRoot, DestRoot.Video, false), Listing(_photoRoot, DestRoot.Photo, false),
               [.. _previous.Select(p => Listing(p, DestRoot.Photo, true))]);

    private void AddParents(string path)
    {
        for (string? p = PathRules.Parent(path); p is not null; p = PathRules.Parent(p)) AddDir(p);
    }

    private void AddDir(string path)
    {
        string n = PathRules.Normalize(path);
        if (_dirs.Add(n)) _entries.Add(new FsEntry(n, "", true, 0, T, T, T, 0x10));
    }

    private RootListing Listing(string root, DestRoot kind, bool previous)
    {
        string r = PathRules.Normalize(root);
        bool available = !_unavailable.Contains(root);
        ImmutableArray<FsEntry> entries = available
            ? [.. _entries.Where(e => PathRules.IsSameOrUnder(e.FullPath, r) && !PathRules.Equal(e.FullPath, r))
                          .Select(e => e with { RelPath = PathRules.RelativeCardPath(e.FullPath, r).Replace('/', '\\') })]
            : [];
        ImmutableArray<(string Path, int Win32Error)> errors = available ? [.. _errors.Where(x => PathRules.IsSameOrUnder(x.Path, r))] : [];
        return new RootListing(root, kind, previous, available, new ListingResult(entries, errors));
    }
}
```

```csharp
// tests/UasSort.Core.Tests/Library/LibraryEntriesTests.cs
using UasSort.Core.Library;
using static UasSort.Core.Tests.Library.LibraryFixture;

namespace UasSort.Core.Tests.Library;

public sealed class LibraryEntriesTests
{
    private const string V = VideoRoot;

    [Fact]
    public void LibraryEntries_DropTheLedgerSubtreeEvenIfListed()
    {
        var listings = new LibraryFixture()
            .WithFile(V + @"\.uas-sort\ledger-A.jsonl", 10, T)
            .WithFile(V + @"\.uas-sort\sub\DJI_20261001120000_0200_D.MP4", 5_000, T)
            .WithFile(V + @"\.uas-sort2\DJI_20261002120000_0300_D.MP4", 6_000, T)
            .Build();
        CollectedLibrary c = LibraryEntries.Collect(listings);
        Assert.Equal(new[] { V + @"\.uas-sort2\DJI_20261002120000_0300_D.MP4" }, c.Files.Select(f => f.Entry.FullPath));
        Assert.DoesNotContain(c.Directories, d => PathRules.IsSameOrUnder(d.Entry.FullPath, V + @"\.uas-sort"));
        Assert.Contains(c.Directories, d => PathRules.Equal(d.Entry.FullPath, V + @"\.uas-sort2"));
    }

    [Fact]
    public void LibraryEntries_PhotoRootInsideVideoRoot_IsListedOnce()
    {
        var listings = new LibraryFixture().WithFile(PhotoRoot + @"\DJI_20260815200000_0119_D.DNG", 27_000_000, T).Build();
        Assert.Single(LibraryEntries.Collect(listings).Files);
    }

    [Fact]
    public void LibraryEntries_UnavailableRoots_AreReportedAndSkipped()
    {
        var listings = new LibraryFixture()
            .WithPrevious(@"D:\Old Offload").WithUnavailable(@"D:\Old Offload")
            .WithFile(@"D:\Old Offload\DJI_20250101120000_0001_D.DNG", 1, T)
            .Build();
        CollectedLibrary c = LibraryEntries.Collect(listings);
        Assert.Equal(new[] { @"D:\Old Offload" }, c.UnavailableRoots);
        Assert.Empty(c.Files);
    }

    [Fact]
    public void LibraryEntries_ListingErrors_AreCarriedExceptUnderTheLedgerFolder()
    {
        var listings = new LibraryFixture()
            .WithDir(V + @"\2026\locked").WithError(V + @"\2026\locked", 5)
            .WithDir(V + @"\.uas-sort\sub").WithError(V + @"\.uas-sort\sub", 5)
            .Build();
        Assert.Equal(new[] { (V + @"\2026\locked", 5) }, LibraryEntries.Collect(listings).Errors.Select(e => (e.Path, e.Win32Error)));
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LibraryEntries_*"`
Expected: build fails with `CS0246: The type or namespace name 'CollectedLibrary' could not be found`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Library/LibraryEntries.cs
using UasSort.Core;

namespace UasSort.Core.Library;

public sealed record LibraryEntry(FsEntry Entry, RootListing Root);
public sealed record CollectedLibrary(ImmutableArray<LibraryEntry> Files, ImmutableArray<LibraryEntry> Directories,
                                      ImmutableArray<string> UnavailableRoots, ImmutableArray<(string Path, int Win32Error)> Errors);

/// <summary>Ref §7.1 Listing: all roots, overlaps once, the .uas-sort subtree never.</summary>
public static class LibraryEntries
{
    public static CollectedLibrary Collect(LibraryListings listings)
    {
        ArgumentNullException.ThrowIfNull(listings);
        string ledgerDir = LedgerPaths.For(listings.Video.Root);
        ImmutableArray<RootListing> roots = [listings.Video, listings.Photo, .. listings.PreviousPhoto];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenErrors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = ImmutableArray.CreateBuilder<LibraryEntry>();
        var dirs = ImmutableArray.CreateBuilder<LibraryEntry>();
        var unavailable = ImmutableArray.CreateBuilder<string>();
        var errors = ImmutableArray.CreateBuilder<(string Path, int Win32Error)>();

        foreach (RootListing root in roots)
        {
            if (!root.Available)
            {
                if (!unavailable.Any(u => PathRules.Equal(u, root.Root))) unavailable.Add(root.Root);
                continue;
            }
            foreach ((string path, int code) in root.Listing.Errors)
            {
                if (!PathRules.IsSameOrUnder(path, ledgerDir) && seenErrors.Add(PathRules.Normalize(path))) errors.Add((path, code));
            }
            foreach (FsEntry e in root.Listing.Entries)
            {
                if (PathRules.IsSameOrUnder(e.FullPath, ledgerDir)) continue;
                if (!seen.Add(PathRules.Normalize(e.FullPath))) continue;
                (e.IsDirectory ? dirs : files).Add(new LibraryEntry(e, root));
            }
        }
        return new CollectedLibrary(files.ToImmutable(), dirs.ToImmutable(), unavailable.ToImmutable(), errors.ToImmutable());
    }
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LibraryEntries_*"`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Library/LibraryEntries.cs tests/UasSort.Core.Tests/Library/LibraryFixture.cs tests/UasSort.Core.Tests/Library/LibraryEntriesTests.cs
git commit -m "feat(core): collect library entries without the ledger folder (Part 05)" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 05.9: Event folder locator (nearest dated ancestor, video root only)

An event folder is the nearest ancestor at any depth under the video root whose name parses as `YYYY-MM-DD …` (05.1). The photo root, every previous photo root and `.uas-sort` are excluded, so a dated folder inside `Picture Offload` is never an event folder. Loose files (a stray MP4 at `2026\`, `Exports\…`, `desktop.ini` at the root) get no event folder, so no pseudo-folder is ever created.

**Files:**
- Create: `src/UasSort.Core/Library/EventFolderLocator.cs`
- Test: `tests/UasSort.Core.Tests/Library/EventFolderLocatorTests.cs`

**Interfaces:**
```csharp
// Consumes: PathRules (Part 02), EventFolderName (05.1); Part 02: LibraryFolderRef, LedgerPaths
// Produces (defined here):
namespace UasSort.Core.Library;
public static class EventFolderLocator {
  // includeSelf: true for a directory entry (the directory itself may be the event folder), false for a file
  public static LibraryFolderRef? For(string path, string videoRoot, IReadOnlyList<string> photoRoots, bool includeSelf);
}
```

- [ ] **Step 1: Write the failing test** **[Review Focus]** (item 3, locator half)

```csharp
// tests/UasSort.Core.Tests/Library/EventFolderLocatorTests.cs
using UasSort.Core.Library;
using UasSort.Core;

namespace UasSort.Core.Tests.Library;

public sealed class EventFolderLocatorTests
{
    private const string V = @"C:\Lib\UAS Videos";
    private static readonly string[] Photos = [V + @"\Picture Offload", @"D:\Old Offload"];
    private const string Council = V + @"\2026\2026-07\2026-07-25 Council Road";

    private static LibraryFolderRef? Locate(string path) => EventFolderLocator.For(path, V, Photos, includeSelf: false);

    [Fact]
    public void EventFolderLocator_FileInDatedFolder_GetsIt()
    {
        LibraryFolderRef? f = Locate(Council + @"\DJI_20260725232655_0117_D.MP4");
        Assert.NotNull(f);
        Assert.Equal(Council, f.FullPath);
        Assert.Equal(new DateOnly(2026, 7, 25), f.NameDate);
        Assert.Equal("Council Road", f.Description);
    }

    [Theory]
    [InlineData(V + @"\2026\DJI_20260801120000_0001_D.MP4")]       // stray MP4 at 2026\
    [InlineData(V + @"\Exports\Council edit.mp4")]                   // non-event folder
    [InlineData(V + @"\desktop.ini")]                                // loose file at the root
    [InlineData(V + @"\2026\2026-07\desktop.ini")]                   // month folder is not an event folder
    [InlineData(V + @"\Picture Offload\2026-05-25 Pano\PANO_0001.DNG")]   // dated folder inside the photo root
    [InlineData(V + @"\Picture Offload\DJI_20260815200000_0119_D.DNG")]
    [InlineData(V + @"\.uas-sort\2026-10-01 Stray\x.MP4")]           // ledger folder
    [InlineData(@"D:\Old Offload\2026-01-01 Old\x.DNG")]             // previous photo root
    [InlineData(@"E:\Elsewhere\2026-01-01 X\x.MP4")]                 // outside the video root
    public void EventFolderLocator_NonEventLocations_GetNone(string path) => Assert.Null(Locate(path));

    [Fact]
    public void EventFolderLocator_AnyDepth_NearestAncestorWins()
    {
        Assert.Equal(V + @"\2022\2022-03-27 Makaha Valley", Locate(V + @"\2022\2022-03-27 Makaha Valley\MAX_0061.MP4")?.FullPath);
        Assert.Equal(Council, Locate(Council + @"\sub\deeper\x.MP4")?.FullPath);
        Assert.Equal(V + @"\2026-07-25 Trip\2026-07-26 Day2", Locate(V + @"\2026-07-25 Trip\2026-07-26 Day2\x.MP4")?.FullPath);
    }

    [Fact]
    public void EventFolderLocator_DirectoryItself_WithIncludeSelf()
    {
        Assert.Equal(Council, EventFolderLocator.For(Council, V, Photos, includeSelf: true)?.FullPath);
        Assert.Null(EventFolderLocator.For(V + @"\Exports", V, Photos, includeSelf: true));
        Assert.Null(EventFolderLocator.For(V, V, Photos, includeSelf: true));
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*EventFolderLocator_*"`
Expected: build fails with `CS0103: The name 'EventFolderLocator' does not exist in the current context`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Library/EventFolderLocator.cs
using UasSort.Core;

namespace UasSort.Core.Library;

/// <summary>Ref §7.1 Event folders: the nearest YYYY-MM-DD ancestor under the video root; photo roots and .uas-sort excluded.</summary>
public static class EventFolderLocator
{
    public static LibraryFolderRef? For(string path, string videoRoot, IReadOnlyList<string> photoRoots, bool includeSelf)
    {
        ArgumentNullException.ThrowIfNull(photoRoots);
        if (!PathRules.IsSameOrUnder(path, videoRoot) || PathRules.Equal(path, videoRoot)) return null;
        if (PathRules.IsSameOrUnder(path, LedgerPaths.For(videoRoot))) return null;
        foreach (string photoRoot in photoRoots)
        {
            if (PathRules.IsSameOrUnder(path, photoRoot)) return null;
        }

        for (string? dir = includeSelf ? PathRules.Normalize(path) : PathRules.Parent(path);
             dir is not null && PathRules.IsSameOrUnder(dir, videoRoot) && !PathRules.Equal(dir, videoRoot);
             dir = PathRules.Parent(dir))
        {
            if (EventFolderName.TryParse(PathRules.FileName(dir), out DateOnly date, out string description))
                return new LibraryFolderRef(PathRules.Normalize(dir), date, description);
        }
        return null;
    }
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*EventFolderLocator_*"`
Expected: 12 passed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Library/EventFolderLocator.cs tests/UasSort.Core.Tests/Library/EventFolderLocatorTests.cs
git commit -m "feat(core): locate event folders at any depth under the video root (Part 05)" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 05.10: Member start times through the card's `ClockModel`

Ref §7.1 / §6.1: a DJI filename stamp converts to UTC through the card's `ClockModel`; in SiteLocal mode (and Setting with a stored SiteLocal) through the event folder's ledger `tz`, else the stored zone. If the file's mtime (true UTC, about start + duration) is earlier than that start, or more than 2 h later, mtime is used and flagged. Non-DJI names (Autel `MAX_####`) use mtime.

**Files:**
- Create: `src/UasSort.Core/Library/MemberStartResolver.cs`
- Test: `tests/UasSort.Core.Tests/Library/MemberStartResolverTests.cs`

**Interfaces:**
```csharp
// Consumes (Part 02/04): ClockModel.ToUtc(DateTime droneStamp, string? siteZoneId) -> (DateTime Utc, TimeSource Src)?; ClockModel.SettingZoneId
// Produces (defined here):
namespace UasSort.Core.Library;
public readonly record struct MemberStart(DateTime Utc, bool FromMtime);
public static partial class MemberStartResolver {
  public static readonly TimeSpan MaxMtimeAfterStart;   // 2 h
  public static bool TryStamp(string fileName, out DateTime droneStamp);   // DJI_yyyyMMddHHmmss_nnnn_X[...].ext, case-insensitive
  public static MemberStart Resolve(string fileName, DateTime mtimeUtc, string? folderLedgerTz, ClockModel clock);
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Library/MemberStartResolverTests.cs
using UasSort.Core.Library;
using UasSort.Core;

namespace UasSort.Core.Tests.Library;

public sealed class MemberStartResolverTests
{
    private static readonly ClockModel EasternZone =
        new(ClockMode.Zone, "America/New_York", [], null, StoredClockMode.Zone, "America/New_York");
    private static readonly ClockModel SiteLocal =
        new(ClockMode.SiteLocal, null, [], null, StoredClockMode.SiteLocal, "America/New_York");

    private static DateTime Utc(int y, int mo, int d, int h, int mi, int s) => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    [Fact]
    public void MemberStart_ZoneClock_ConvertsTheFilenameStamp()   // Zachar Bay 0148: the watermark clip
        => Assert.Equal(new MemberStart(Utc(2026, 9, 27, 18, 24, 16), false),
                        MemberStartResolver.Resolve("DJI_20260927142416_0148_D.MP4", Utc(2026, 9, 27, 18, 25, 46), null, EasternZone));

    [Fact]
    public void MemberStart_SiteLocal_UsesTheFolderLedgerZone()
        => Assert.Equal(new MemberStart(Utc(2026, 9, 27, 18, 1, 27), false),
                        MemberStartResolver.Resolve("DJI_20260927100127_0123_D.MP4", Utc(2026, 9, 27, 18, 2, 57), "America/Anchorage", SiteLocal));

    [Fact]
    public void MemberStart_SiteLocal_WithoutLedgerZone_UsesTheStoredZone()
        => Assert.Equal(new MemberStart(Utc(2026, 9, 27, 14, 1, 27), false),
                        MemberStartResolver.Resolve("DJI_20260927100127_0123_D.MP4", Utc(2026, 9, 27, 14, 2, 57), null, SiteLocal));

    [Theory]
    [InlineData(18, 0, 0, true)]      // mtime before the stamp start → mtime, flagged
    [InlineData(20, 24, 17, true)]    // more than 2 h after → mtime, flagged
    [InlineData(20, 24, 16, false)]   // exactly 2 h after → the stamp stands
    [InlineData(18, 24, 16, false)]   // equal → the stamp stands
    public void MemberStart_MtimeCheck(int h, int mi, int s, bool fromMtime)
    {
        DateTime mtime = Utc(2026, 9, 27, h, mi, s);
        MemberStart r = MemberStartResolver.Resolve("DJI_20260927142416_0148_D.MP4", mtime, null, EasternZone);
        Assert.Equal(fromMtime, r.FromMtime);
        Assert.Equal(fromMtime ? mtime : Utc(2026, 9, 27, 18, 24, 16), r.Utc);
    }

    [Theory]
    [InlineData("MAX_0061.MP4")]                       // Autel: always mtime
    [InlineData("Council edit.mp4")]
    [InlineData("DJI_20261399000000_0001_D.MP4")]      // impossible stamp
    public void MemberStart_NonDjiNames_UseMtime(string name)
        => Assert.Equal(new MemberStart(Utc(2022, 3, 27, 15, 1, 0), true),
                        MemberStartResolver.Resolve(name, Utc(2022, 3, 27, 15, 1, 0), null, EasternZone));

    [Theory]
    [InlineData("dji_20260927142416_0148_d.mp4")]
    [InlineData("DJI_20260927142416_0148_D_Tele.MP4")]
    public void MemberStart_StampVariants(string name)
    {
        Assert.True(MemberStartResolver.TryStamp(name, out DateTime stamp));
        Assert.Equal(new DateTime(2026, 9, 27, 14, 24, 16), stamp);
        Assert.Equal(DateTimeKind.Unspecified, stamp.Kind);
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*MemberStart_*"`
Expected: build fails with `CS0246: The type or namespace name 'MemberStart' could not be found`.

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Library/MemberStartResolver.cs
using System.Globalization;
using System.Text.RegularExpressions;
using UasSort.Core;

namespace UasSort.Core.Library;

public readonly record struct MemberStart(DateTime Utc, bool FromMtime);

/// <summary>Ref §7.1 member start: filename stamp through the card's ClockModel, checked against mtime.</summary>
public static partial class MemberStartResolver
{
    public static readonly TimeSpan MaxMtimeAfterStart = TimeSpan.FromHours(2);

    [GeneratedRegex(@"^DJI_([0-9]{14})_([0-9]{4})_([A-Z])(?:_[^.]*)?\.([A-Za-z0-9]+)$",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DjiName();

    public static bool TryStamp(string fileName, out DateTime droneStamp)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        droneStamp = default;
        Match m = DjiName().Match(fileName);
        return m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                                                   DateTimeStyles.None, out droneStamp);
    }

    public static MemberStart Resolve(string fileName, DateTime mtimeUtc, string? folderLedgerTz, ClockModel clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DateTime mtime = mtimeUtc.Kind == DateTimeKind.Utc ? mtimeUtc : DateTime.SpecifyKind(mtimeUtc, DateTimeKind.Utc);
        if (!TryStamp(fileName, out DateTime stamp)) return new MemberStart(mtime, true);
        if (clock.ToUtc(stamp, folderLedgerTz ?? clock.SettingZoneId) is not { } converted) return new MemberStart(mtime, true);
        DateTime start = converted.Utc;
        if (mtime < start || mtime > start + MaxMtimeAfterStart) return new MemberStart(mtime, true);
        return new MemberStart(start, false);
    }
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*MemberStart_*"`
Expected: 12 passed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Library/MemberStartResolver.cs tests/UasSort.Core.Tests/Library/MemberStartResolverTests.cs
git commit -m "feat(core): resolve library member start times through the ClockModel (Part 05)" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 05.11: `LibraryIndex.Build` (keys, event folders, member starts, watermark, set folders)

Assembles 05.8–05.10 into the Ref §3 `LibraryIndex`. Built from listings plus the ledger and the card's `ClockModel` only; it is never given a stream. Per file: `LibraryFile(FullPath, FileKey, MtimeUtc, RawAttributes, EventFolder)`. Per event folder: sorted member starts of its **video** files (mp4, mov, insv, avi; `desktop.ini` and stills are never members), `TzId` from the ledger `folder` record, centroid from the ledger `folder` record, else the mean (unit vectors) of the ledger `file` records whose `dest` lies directly in that folder, else unknown (`LocationSource.Unknown`; the card-leftover centroid needs card items and is Part 06's). Watermark = the latest start of any listed library video that lies in an event folder (`EventFolderLocator.For(path, videoRoot, photoRoots, includeSelf: false)` is non-null) and has no ledger `file` record (Ref §7.1 as decided by the user, 00-interfaces decision 4: the app's own copies never move it, and videos elsewhere, e.g. `Exports\` or a loose clip at `2026\`, never move it either; null = no event-folder video imported outside uas-sort). `SetFolder(name)` = the directories directly inside any available listed root with that name, members = files directly in them (name, size, mtime).

**Files:**
- Create: `src/UasSort.Core/Library/LibraryIndex.cs` (the members of Part 02's empty `public sealed partial class LibraryIndex` in `src/UasSort.Core/Model/Library.cs`; namespace `UasSort.Core`)
- Test: `tests/UasSort.Core.Tests/Library/LibraryIndexTests.cs`

**Interfaces:**
```csharp
// Consumes: LibraryEntries/CollectedLibrary (05.8), EventFolderLocator (05.9), MemberStartResolver (05.10), `FileKey.Of`, `PathRules` (Part 02);
//   Part 02: LibraryListings, RootListing, LibraryFile, LibraryFolder, LibraryFolderRef, LocationSource, SetFolderListing, LedgerSnapshot,
//   LedgerFolder, LedgerFile, FileKey, GeoPoint, ClockModel
// Produces (Ref §3 Supporting types (2), added here to Part 02's partial class):
namespace UasSort.Core;
public sealed partial class LibraryIndex {
  public static LibraryIndex Build(LibraryListings listings, LedgerSnapshot ledger, ClockModel clock);   // .uas-sort subtree dropped
  public ImmutableArray<LibraryFile> Match(FileKey key);                            // same NormName + Size in any listed root
  public ImmutableArray<LibraryFile> SameNameOtherSize(string normName, long size); // conflict evidence from listings (normName as FileKey.NormName)
  public ImmutableArray<LibraryFolder> Folders { get; }                             // event folders, ordered by FullPath
  public ImmutableArray<SetFolderListing> SetFolder(string name);                   // <any listed root>\name, if present (case-insensitive)
  public DateTime? WatermarkUtc { get; }                                            // latest start of an event-folder video without a
                                                                                    // ledger `file` record (decision 4); null = none
  public ImmutableArray<string> UnavailableRoots { get; }
  // defined here (not in Ref §3; Part 06 uses them):
  public ImmutableArray<LibraryFile> Files { get; }                                 // every indexed file (card-leftover matching, reports)
  public ImmutableArray<LibraryFile> FilesIn(LibraryFolderRef folder);              // files whose event folder is that folder
  public ImmutableHashSet<string> StartsFromMtime { get; }                          // videos whose start came from mtime (the §7.1 flag)
  public ImmutableArray<(string Path, int Win32Error)> ListingErrors { get; }       // for the "folder skipped" warning (Ref §12)
}
```

- [ ] **Step 1: Write the failing test** **[Review Focus]** (item 3: `LibraryIndex_ReviewFocus_NonEventFoldersAndLooseFiles`)

```csharp
// tests/UasSort.Core.Tests/Library/LibraryIndexTests.cs
using UasSort.Core;
using UasSort.Core.Tests.Ledger;
using static UasSort.Core.Tests.Ledger.LedgerLines;
using static UasSort.Core.Tests.Library.LibraryFixture;

namespace UasSort.Core.Tests.Library;

public sealed class LibraryIndexTests
{
    private const string V = VideoRoot;
    private const string Zrel = V + @"\2026\2026-09\2026-09-27 Zachar Bay";
    private const string Council = V + @"\2026\2026-07\2026-07-25 Council Road";
    private const string Anvil = V + @"\2026\2026-07\2026-07-26 Anvil Mountain";

    private static readonly ClockModel EasternZone =
        new(ClockMode.Zone, "America/New_York", [], null, StoredClockMode.Zone, "America/New_York");
    private static readonly ClockModel SiteLocalClock =
        new(ClockMode.SiteLocal, null, [], null, StoredClockMode.SiteLocal, "America/New_York");

    private static LibraryFolder FolderAt(LibraryIndex lib, string path)
        => lib.Folders.Single(f => string.Equals(f.Ref.FullPath, path, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void LibraryIndex_ReviewFocus_NonEventFoldersAndLooseFiles()
    {
        var listings = new LibraryFixture()
            .WithFile(V + @"\desktop.ini", 282, LibraryFixture.Utc(2026, 1, 1))
            .WithFile(PhotoRoot + @"\DJI_20260815200000_0119_D.DNG", 27_000_000, LibraryFixture.Utc(2026, 8, 16))
            .WithFile(PhotoRoot + @"\desktop.ini", 282, LibraryFixture.Utc(2026, 1, 1))
            .WithFile(PhotoRoot + @"\2026-05-25 Pano\PANO_0001.DNG", 13_751_808, LibraryFixture.Utc(2026, 5, 25, 13, 31))
            .WithFile(V + @"\Exports\Council edit.mp4", 500_000_000, LibraryFixture.Utc(2026, 7, 30, 12))
            .WithFile(V + @"\2026\DJI_20260801120000_0001_D.MP4", 1_000_000, LibraryFixture.Utc(2026, 8, 1, 16, 1, 30))
            .WithFile(Council + @"\DJI_20260725232655_0117_D.MP4", 1_234_567_890, LibraryFixture.Utc(2026, 7, 26, 3, 28, 25))
            .WithFile(Council + @"\DJI_20260726022937_0118_D.MP4", 2_000_000_000, LibraryFixture.Utc(2026, 7, 26, 6, 31, 7))
            .WithFile(Council + @"\desktop.ini", 100, LibraryFixture.Utc(2026, 7, 27))
            .Build();
        LibraryIndex lib = LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone);

        // only the YYYY-MM-DD folder is an event folder; Picture Offload, Exports, 2026, 2026-07 and the dated folder in the photo root are not
        LibraryFolder council = Assert.Single(lib.Folders);
        Assert.Equal(Council, council.Ref.FullPath);
        Assert.Equal(new DateOnly(2026, 7, 25), council.Ref.NameDate);
        Assert.Equal("Council Road", council.Ref.Description);
        Assert.Equal(new[] { LibraryFixture.Utc(2026, 7, 26, 3, 26, 55), LibraryFixture.Utc(2026, 7, 26, 6, 29, 37) }, council.MemberStartsUtc);
        Assert.Equal(3, lib.FilesIn(council.Ref).Length);       // incl. its desktop.ini, which is no member start

        // name+size matching works everywhere; loose files never get a (pseudo) event folder
        Assert.Null(Assert.Single(lib.Match(FileKey.Of("DJI_20260801120000_0001_D.MP4", 1_000_000))).EventFolder);
        Assert.Null(Assert.Single(lib.Match(FileKey.Of("Council edit.mp4", 500_000_000))).EventFolder);
        Assert.Null(Assert.Single(lib.Match(FileKey.Of("DJI_20260815200000_0119_D.DNG", 27_000_000))).EventFolder);
        Assert.Null(Assert.Single(lib.Match(FileKey.Of("PANO_0001.DNG", 13_751_808))).EventFolder);
        Assert.Equal(2, lib.Match(FileKey.Of("desktop.ini", 282)).Length);
        Assert.All(lib.Match(FileKey.Of("desktop.ini", 282)), f => Assert.Null(f.EventFolder));
        Assert.Equal(Council, Assert.Single(lib.Match(FileKey.Of("desktop.ini", 100))).EventFolder?.FullPath);
        Assert.Equal(Council, Assert.Single(lib.Match(FileKey.Of("DJI_20260725232655_0117_D.MP4", 1_234_567_890))).EventFolder?.FullPath);

        // decision 4: only event-folder videos count for the watermark; the Exports clip (mtime Jul 30) and the stray 0001 at 2026\
        // (Aug 1 16:00Z) are later but leave it at Council 0118 (02:29:37 EDT = 06:29:37Z)
        Assert.Equal(LibraryFixture.Utc(2026, 7, 26, 6, 29, 37), lib.WatermarkUtc);
        Assert.Contains(V + @"\Exports\Council edit.mp4", lib.StartsFromMtime);
        Assert.Empty(lib.UnavailableRoots);
    }

    [Fact]
    public void LibraryIndex_Watermark_IgnoresVideosOutsideEventFolders()
    {
        // decision 4 (user): only videos inside event folders count; an Exports\ clip and a loose 2026\x.MP4 with later mtimes
        // leave the watermark at Zachar Bay 0148 (stamp 14:24:16 EDT = 18:24:16Z)
        var listings = new LibraryFixture()
            .WithFile(Zrel + @"\DJI_20260927142416_0148_D.MP4", 77_000_000, LibraryFixture.Utc(2026, 9, 27, 18, 25, 46))
            .WithFile(V + @"\Exports\Zachar edit.mp4", 900_000_000, LibraryFixture.Utc(2026, 10, 1))
            .WithFile(V + @"\2026\x.MP4", 800_000_000, LibraryFixture.Utc(2026, 10, 2, 12))
            .Build();
        Assert.Equal(LibraryFixture.Utc(2026, 9, 27, 18, 24, 16), LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone).WatermarkUtc);

        // without an event-folder video there is no watermark at all
        var outsideOnly = new LibraryFixture()
            .WithFile(V + @"\Exports\Zachar edit.mp4", 900_000_000, LibraryFixture.Utc(2026, 10, 1))
            .WithFile(V + @"\2026\x.MP4", 800_000_000, LibraryFixture.Utc(2026, 10, 2, 12))
            .Build();
        Assert.Null(LibraryIndex.Build(outsideOnly, TestLedger.Empty(), EasternZone).WatermarkUtc);
    }

    [Fact]
    public void LibraryIndex_LedgerFolderIsNotIndexed()
    {
        var listings = new LibraryFixture()
            .WithFile(Zrel + @"\DJI_20260927142416_0148_D.MP4", 77_000_000, LibraryFixture.Utc(2026, 9, 27, 18, 25, 46))
            .WithFile(V + @"\.uas-sort\ledger-A.jsonl", 2_500, LibraryFixture.Utc(2026, 10, 1))
            .WithFile(V + @"\.uas-sort\ledger-A-LAPTOP-B.jsonl", 2_400, LibraryFixture.Utc(2026, 10, 1))
            .WithFile(V + @"\.uas-sort\DJI_20261001120000_0200_D.MP4", 5_000, LibraryFixture.Utc(2026, 10, 1, 16, 1, 30))
            .WithFile(V + @"\.uas-sort\DJI_20261001120500_0201_D.DNG", 6_000, LibraryFixture.Utc(2026, 10, 1, 16, 6))
            .WithFile(V + @"\.uas-sort\2026-10-01 Stray\DJI_20261001130000_0202_D.MP4", 7_000, LibraryFixture.Utc(2026, 10, 1, 17, 1))
            .Build();
        LibraryIndex lib = LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone);
        Assert.Empty(lib.Match(FileKey.Of("DJI_20261001120000_0200_D.MP4", 5_000)));
        Assert.Empty(lib.Match(FileKey.Of("DJI_20261001120500_0201_D.DNG", 6_000)));
        Assert.Empty(lib.Match(FileKey.Of("DJI_20261001130000_0202_D.MP4", 7_000)));
        Assert.Empty(lib.Match(FileKey.Of("ledger-A.jsonl", 2_500)));
        Assert.Empty(lib.SameNameOtherSize("dji_20261001120000_0200_d.mp4", 1));
        Assert.Equal(Zrel, Assert.Single(lib.Folders).Ref.FullPath);
        Assert.Empty(lib.SetFolder(".uas-sort"));
        Assert.Empty(lib.SetFolder("2026-10-01 Stray"));
        Assert.Equal(LibraryFixture.Utc(2026, 9, 27, 18, 24, 16), lib.WatermarkUtc);
        Assert.DoesNotContain(lib.Files, f => f.FullPath.Contains(".uas-sort", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LibraryIndex_AppCopiesNeverMoveTheWatermark()
    {
        var listings = new LibraryFixture()
            .WithFile(Zrel + @"\DJI_20260927140127_0123_D.MP4", 105_764_094, LibraryFixture.Utc(2026, 9, 27, 18, 2, 57))
            .WithFile(Zrel + @"\DJI_20260927140144_0124_D.MP4", 98_000_000, LibraryFixture.Utc(2026, 9, 27, 18, 3, 14))
            .WithFile(Zrel + @"\DJI_20260927142416_0148_D.MP4", 77_000_000, LibraryFixture.Utc(2026, 9, 27, 18, 25, 46))
            .WithFile(Zrel + @"\DJI_20260927160000_0160_D.MP4", 60_000_000, LibraryFixture.Utc(2026, 9, 27, 20, 1, 30))
            .Build();
        LedgerSnapshot ledger = TestLedger.Snapshot(
            FileRec("f160", "DJI_20260927160000_0160_D.MP4", 60_000_000, Zrel + @"\DJI_20260927160000_0160_D.MP4"));
        LibraryIndex lib = LibraryIndex.Build(listings, ledger, EasternZone);
        Assert.Equal(LibraryFixture.Utc(2026, 9, 27, 18, 24, 16), lib.WatermarkUtc);    // 10:24:16 AKDT, Zachar Bay 0148
        Assert.Equal(4, FolderAt(lib, Zrel).MemberStartsUtc.Length);                   // the app copy is still a member
    }

    [Fact]
    public void LibraryIndex_NoWatermark_WhenEveryVideoHasALedgerRecordOrThereAreNoVideos()
    {
        var withRecord = new LibraryFixture()
            .WithFile(Zrel + @"\DJI_20260927160000_0160_D.MP4", 60_000_000, LibraryFixture.Utc(2026, 9, 27, 20, 1, 30)).Build();
        LedgerSnapshot ledger = TestLedger.Snapshot(
            FileRec("f160", "DJI_20260927160000_0160_D.MP4", 60_000_000, Zrel + @"\DJI_20260927160000_0160_D.MP4"));
        Assert.Null(LibraryIndex.Build(withRecord, ledger, EasternZone).WatermarkUtc);

        var photosOnly = new LibraryFixture()
            .WithFile(PhotoRoot + @"\DJI_20260815200000_0119_D.DNG", 27_000_000, LibraryFixture.Utc(2026, 8, 16)).Build();
        Assert.Null(LibraryIndex.Build(photosOnly, TestLedger.Empty(), EasternZone).WatermarkUtc);
        Assert.Null(LibraryIndex.Build(new LibraryFixture().Build(), TestLedger.Empty(), EasternZone).WatermarkUtc);
    }

    [Fact]
    public void LibraryIndex_FolderZoneAndCentroid_FromTheLedger()
    {
        var listings = new LibraryFixture()
            .WithFile(Zrel + @"\DJI_20260927142416_0148_D.MP4", 77_000_000, LibraryFixture.Utc(2026, 9, 27, 18, 25, 46))
            .WithFile(Anvil + @"\DJI_20260726235645_0001_D.MP4", 100, LibraryFixture.Utc(2026, 7, 27, 3, 58, 15))
            .WithFile(Anvil + @"\DJI_20260727000012_0002_D.MP4", 200, LibraryFixture.Utc(2026, 7, 27, 4, 1, 42))
            .WithFile(Council + @"\DJI_20260725232655_0117_D.MP4", 1_234_567_890, LibraryFixture.Utc(2026, 7, 26, 3, 28, 25))
            .Build();
        LedgerSnapshot ledger = TestLedger.Snapshot(
            FolderRec("fo1", Zrel, "Zachar Bay", new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), "America/Anchorage", 57.5415, -153.7409),
            FileRec("a1", "DJI_20260726235645_0001_D.MP4", 100, Anvil + @"\DJI_20260726235645_0001_D.MP4", lat: 64.5627, lon: -165.3696),
            FileRec("a2", "DJI_20260727000012_0002_D.MP4", 200, Anvil + @"\DJI_20260727000012_0002_D.MP4", lat: 64.5627, lon: -165.3696));
        LibraryIndex lib = LibraryIndex.Build(listings, ledger, EasternZone);

        LibraryFolder z = FolderAt(lib, Zrel);
        Assert.Equal("America/Anchorage", z.TzId);
        Assert.Equal(new GeoPoint(57.5415, -153.7409), z.Centroid);
        Assert.Equal(LocationSource.Ledger, z.Loc);

        LibraryFolder a = FolderAt(lib, Anvil);
        Assert.Null(a.TzId);
        GeoPoint ac = Assert.NotNull(a.Centroid);
        Assert.Equal(64.5627, ac.Lat, 9);
        Assert.Equal(-165.3696, ac.Lon, 9);
        Assert.Equal(LocationSource.Ledger, a.Loc);

        LibraryFolder c = FolderAt(lib, Council);
        Assert.Null(c.Centroid);
        Assert.Equal(LocationSource.Unknown, c.Loc);
        Assert.Equal(3, lib.Folders.Length);
    }

    [Fact]
    public void LibraryIndex_SiteLocalMemberStarts_UseTheFolderLedgerZoneElseTheStoredZone()
    {
        const string other = V + @"\2026\2026-09\2026-09-28 Other";
        var listings = new LibraryFixture()
            .WithFile(Zrel + @"\DJI_20260927100127_0123_D.MP4", 105_764_094, LibraryFixture.Utc(2026, 9, 27, 18, 2, 57))
            .WithFile(other + @"\DJI_20260928100000_0001_D.MP4", 1, LibraryFixture.Utc(2026, 9, 28, 14, 1, 30))
            .Build();
        LedgerSnapshot ledger = TestLedger.Snapshot(
            FolderRec("fo1", Zrel, "Zachar Bay", new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), "America/Anchorage"));
        LibraryIndex lib = LibraryIndex.Build(listings, ledger, SiteLocalClock);
        Assert.Equal(new[] { LibraryFixture.Utc(2026, 9, 27, 18, 1, 27) }, FolderAt(lib, Zrel).MemberStartsUtc);
        Assert.Equal(new[] { LibraryFixture.Utc(2026, 9, 28, 14, 0, 0) }, FolderAt(lib, other).MemberStartsUtc);
        Assert.Empty(lib.StartsFromMtime);
    }

    [Fact]
    public void LibraryIndex_PhotoRootMovedToD_OldRootsStillMatch()
    {
        var listings = new LibraryFixture()
            .WithPhotoRoot(@"D:\Photos")
            .WithPrevious(PhotoRoot)                  // the old Picture Offload inside the video root
            .WithPrevious(@"D:\Old Offload")
            .WithFile(PhotoRoot + @"\DJI_20260815200000_0119_D.DNG", 27_000_000, LibraryFixture.Utc(2026, 8, 16))
            .WithFile(PhotoRoot + @"\2026-05-25 Pano\PANO_0001.DNG", 13_751_808, LibraryFixture.Utc(2026, 5, 25))
            .WithFile(@"D:\Old Offload\DJI_20250101120000_0001_D.DNG", 25_000_000, LibraryFixture.Utc(2025, 1, 1, 17))
            .WithFile(@"D:\Photos\DJI_20260901120000_0002_D.DNG", 26_000_000, LibraryFixture.Utc(2026, 9, 1, 16))
            .Build();
        LibraryIndex lib = LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone);
        Assert.Single(lib.Match(FileKey.Of("DJI_20260815200000_0119_D.DNG", 27_000_000)));
        Assert.Single(lib.Match(FileKey.Of("PANO_0001.DNG", 13_751_808)));
        Assert.Single(lib.Match(FileKey.Of("DJI_20250101120000_0001_D.DNG", 25_000_000)));
        Assert.Single(lib.Match(FileKey.Of("DJI_20260901120000_0002_D.DNG", 26_000_000)));
        Assert.Empty(lib.Folders);                   // a dated folder in a previous photo root is not an event folder
    }

    [Fact]
    public void LibraryIndex_SetFolders_InAnyListedRoot()
    {
        var listings = new LibraryFixture()
            .WithPrevious(@"D:\Old Offload")
            .WithFile(PhotoRoot + @"\001_0087\PANO_0002.DNG", 12_882_432, LibraryFixture.Utc(2026, 5, 25, 13, 30, 30))
            .WithFile(PhotoRoot + @"\001_0087\PANO_0001.DNG", 13_751_808, LibraryFixture.Utc(2026, 5, 25, 13, 30, 28))
            .WithDir(PhotoRoot + @"\001_0112")
            .WithFile(@"D:\Old Offload\001_0087\PANO_0001.DNG", 1, LibraryFixture.Utc(2025, 1, 1))
            .Build();
        LibraryIndex lib = LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone);

        var sets = lib.SetFolder("001_0087");
        Assert.Equal(2, sets.Length);
        SetFolderListing inPhotoRoot = sets.Single(s => s.FullPath.StartsWith(PhotoRoot, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("001_0087", inPhotoRoot.Name);
        Assert.Equal(new[] { ("PANO_0001.DNG", 13_751_808L, LibraryFixture.Utc(2026, 5, 25, 13, 30, 28)),
                             ("PANO_0002.DNG", 12_882_432L, LibraryFixture.Utc(2026, 5, 25, 13, 30, 30)) },
                     inPhotoRoot.Members.Select(m => (m.Member, m.Size, m.MtimeUtc)));
        Assert.Empty(Assert.Single(lib.SetFolder("001_0112")).Members);        // an empty folder is listed; Part 06 decides it doesn't count
        Assert.Empty(lib.SetFolder("001_9999"));
    }

    [Fact]
    public void LibraryIndex_CopySuffixMatchesAndConflictEvidence()
    {
        var listings = new LibraryFixture()
            .WithFile(Zrel + @"\DJI_20260927140127_0123_D (2).MP4", 105_764_094, LibraryFixture.Utc(2026, 9, 27, 18, 2, 57))
            .WithFile(Zrel + @"\best shot.MP4", 555_555_555, LibraryFixture.Utc(2026, 9, 27, 19))
            .Build();
        LibraryIndex lib = LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone);
        Assert.Single(lib.Match(FileKey.Of("DJI_20260927140127_0123_D.MP4", 105_764_094)));
        LibraryFile other = Assert.Single(lib.SameNameOtherSize("dji_20260927140127_0123_d.mp4", 7_340_032));
        Assert.Equal(105_764_094, other.Key.Size);
        Assert.Empty(lib.SameNameOtherSize("dji_20260927140127_0123_d.mp4", 105_764_094));
        Assert.Empty(lib.Match(FileKey.Of("DJI_20260927140130_0130_D.MP4", 555_555_555)));   // same size, other name: no match
    }

    [Fact]
    public void LibraryIndex_LegacyDepthAutelMembers_StartFromMtime()
    {
        const string makaha = V + @"\2022\2022-03-27 Makaha Valley";
        var listings = new LibraryFixture()
            .WithFile(makaha + @"\MAX_0061.MP4", 61_000_000, LibraryFixture.Utc(2022, 3, 27, 15, 1))
            .WithFile(makaha + @"\MAX_0062.MP4", 62_000_000, LibraryFixture.Utc(2022, 3, 27, 15, 2))
            .WithFile(makaha + @"\MAX_0064.MP4", 64_000_000, LibraryFixture.Utc(2022, 3, 27, 15, 4))
            .Build();
        LibraryIndex lib = LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone);
        LibraryFolder f = Assert.Single(lib.Folders);
        Assert.Equal((new DateOnly(2022, 3, 27), "Makaha Valley"), (f.Ref.NameDate, f.Ref.Description));
        Assert.Equal(new[] { LibraryFixture.Utc(2022, 3, 27, 15, 1), LibraryFixture.Utc(2022, 3, 27, 15, 2), LibraryFixture.Utc(2022, 3, 27, 15, 4) },
                     f.MemberStartsUtc);
        Assert.Equal(3, lib.StartsFromMtime.Count);
    }

    [Fact]
    public void LibraryIndex_UnavailableRootsAndListingErrors_AreCarried()
    {
        var listings = new LibraryFixture()
            .WithPrevious(@"E:\Gone").WithUnavailable(@"E:\Gone")
            .WithDir(V + @"\2026\locked").WithError(V + @"\2026\locked", 5)
            .Build();
        LibraryIndex lib = LibraryIndex.Build(listings, TestLedger.Empty(), EasternZone);
        Assert.Equal(new[] { @"E:\Gone" }, lib.UnavailableRoots);
        Assert.Equal((V + @"\2026\locked", 5), Assert.Single(lib.ListingErrors));
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LibraryIndex_*"`
Expected: build fails with `CS0117: 'LibraryIndex' does not contain a definition for 'Build'` (Part 02's partial class is still empty).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Library/LibraryIndex.cs
using UasSort.Core.Library;

namespace UasSort.Core;

// The library as seen through listings and the ledger only (Ref §7.1). Never opens a library file.
public sealed partial class LibraryIndex
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".insv", ".avi" };

    private readonly ImmutableDictionary<FileKey, ImmutableArray<LibraryFile>> _byKey;
    private readonly ImmutableDictionary<string, ImmutableArray<LibraryFile>> _byName;
    private readonly ImmutableDictionary<string, ImmutableArray<LibraryFile>> _byFolder;
    private readonly ImmutableDictionary<string, ImmutableArray<SetFolderListing>> _setFolders;

    private LibraryIndex(ImmutableArray<LibraryFile> files, ImmutableArray<LibraryFolder> folders,
                         ImmutableDictionary<string, ImmutableArray<SetFolderListing>> setFolders, DateTime? watermarkUtc,
                         ImmutableArray<string> unavailableRoots, ImmutableHashSet<string> startsFromMtime,
                         ImmutableArray<(string Path, int Win32Error)> listingErrors)
    {
        Files = files;
        Folders = folders;
        WatermarkUtc = watermarkUtc;
        UnavailableRoots = unavailableRoots;
        StartsFromMtime = startsFromMtime;
        ListingErrors = listingErrors;
        _setFolders = setFolders;
        _byKey = files.GroupBy(f => f.Key).ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray());
        _byName = files.GroupBy(f => f.Key.NormName, StringComparer.Ordinal)
                       .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray(), StringComparer.Ordinal);
        _byFolder = files.Where(f => f.EventFolder is not null)
                         .GroupBy(f => f.EventFolder!.FullPath, StringComparer.OrdinalIgnoreCase)
                         .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
    }

    public ImmutableArray<LibraryFolder> Folders { get; }
    public DateTime? WatermarkUtc { get; }
    public ImmutableArray<string> UnavailableRoots { get; }
    public ImmutableArray<LibraryFile> Files { get; }
    public ImmutableHashSet<string> StartsFromMtime { get; }
    public ImmutableArray<(string Path, int Win32Error)> ListingErrors { get; }

    public ImmutableArray<LibraryFile> Match(FileKey key) => _byKey.TryGetValue(key, out var v) ? v : [];

    public ImmutableArray<LibraryFile> SameNameOtherSize(string normName, long size)
        => _byName.TryGetValue(normName, out var v) ? [.. v.Where(f => f.Key.Size != size)] : [];

    public ImmutableArray<SetFolderListing> SetFolder(string name) => _setFolders.TryGetValue(name, out var v) ? v : [];

    public ImmutableArray<LibraryFile> FilesIn(LibraryFolderRef folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return _byFolder.TryGetValue(PathRules.Normalize(folder.FullPath), out var v) ? v : [];
    }

    public static LibraryIndex Build(LibraryListings listings, LedgerSnapshot ledger, ClockModel clock)
    {
        ArgumentNullException.ThrowIfNull(listings);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(clock);
        CollectedLibrary collected = LibraryEntries.Collect(listings);
        string videoRoot = listings.Video.Root;
        ImmutableArray<string> photoRoots = [listings.Photo.Root, .. listings.PreviousPhoto.Select(p => p.Root)];

        var ledgerFolders = new Dictionary<string, LedgerFolder>(StringComparer.OrdinalIgnoreCase);
        foreach (LedgerFolder lf in ledger.Folders.Values) ledgerFolders[PathRules.Normalize(lf.Path)] = lf;

        // event folders from directory entries (incl. ones with no files yet)
        var refs = new Dictionary<string, LibraryFolderRef>(StringComparer.OrdinalIgnoreCase);
        foreach (LibraryEntry d in collected.Directories)
        {
            if (EventFolderLocator.For(d.Entry.FullPath, videoRoot, photoRoots, includeSelf: true) is { } r
                && PathRules.Equal(r.FullPath, d.Entry.FullPath))
            {
                refs.TryAdd(r.FullPath, r);
            }
        }

        var files = ImmutableArray.CreateBuilder<LibraryFile>(collected.Files.Length);
        var starts = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
        var fromMtime = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        DateTime? watermark = null;

        foreach (LibraryEntry entry in collected.Files)
        {
            FsEntry e = entry.Entry;
            string path = PathRules.Normalize(e.FullPath);
            string name = PathRules.FileName(path);
            LibraryFolderRef? folder = EventFolderLocator.For(path, videoRoot, photoRoots, includeSelf: false);
            if (folder is not null)
            {
                if (refs.TryGetValue(folder.FullPath, out LibraryFolderRef? known)) folder = known;
                else refs[folder.FullPath] = folder;
            }
            var file = new LibraryFile(path, FileKey.Of(name, e.Size), AsUtc(e.MtimeUtc), e.RawAttributes, folder);
            files.Add(file);
            if (!VideoExtensions.Contains(Path.GetExtension(name))) continue;

            string? tz = folder is not null && ledgerFolders.TryGetValue(folder.FullPath, out LedgerFolder? lf) ? lf.TzId : null;
            MemberStart start = MemberStartResolver.Resolve(name, e.MtimeUtc, tz, clock);
            if (start.FromMtime) fromMtime.Add(path);
            if (folder is not null)
            {
                if (!starts.TryGetValue(folder.FullPath, out List<DateTime>? list))
                {
                    list = new List<DateTime>();
                    starts[folder.FullPath] = list;
                }
                list.Add(start.Utc);

                // decision 4: only event-folder videos without a ledger `file` record move the watermark
                if (!ledger.Files.ContainsKey(file.Key) && (watermark is null || start.Utc > watermark)) watermark = start.Utc;
            }
        }

        ImmutableArray<LibraryFile> built = files.ToImmutable();

        var folders = ImmutableArray.CreateBuilder<LibraryFolder>(refs.Count);
        foreach (LibraryFolderRef r in refs.Values.OrderBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            ImmutableArray<DateTime> memberStarts = starts.TryGetValue(r.FullPath, out List<DateTime>? list) ? [.. list.Order()] : [];
            ledgerFolders.TryGetValue(r.FullPath, out LedgerFolder? lf);
            GeoPoint? centroid = lf?.Centroid ?? Centroid(ledger.Files.Values
                .Where(f => f.Point is not null && PathRules.Parent(f.Dest) is { } parent && PathRules.Equal(parent, r.FullPath))
                .Select(f => f.Point.GetValueOrDefault()));
            folders.Add(new LibraryFolder(r, memberStarts, lf?.TzId, centroid, centroid is null ? LocationSource.Unknown : LocationSource.Ledger));
        }

        // set folders: directories directly inside an available listed root
        ImmutableArray<RootListing> roots = [listings.Video, listings.Photo, .. listings.PreviousPhoto];
        var rootPaths = new HashSet<string>(roots.Where(x => x.Available).Select(x => PathRules.Normalize(x.Root)), StringComparer.OrdinalIgnoreCase);
        var byParent = built.GroupBy(f => PathRules.Parent(f.FullPath) is { } p ? PathRules.Normalize(p) : "", StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var setFolders = new Dictionary<string, List<SetFolderListing>>(StringComparer.OrdinalIgnoreCase);
        foreach (LibraryEntry d in collected.Directories)
        {
            string dir = PathRules.Normalize(d.Entry.FullPath);
            if (PathRules.Parent(dir) is not { } parent || !rootPaths.Contains(PathRules.Normalize(parent))) continue;
            ImmutableArray<(string Member, long Size, DateTime MtimeUtc)> members = byParent.TryGetValue(dir, out List<LibraryFile>? inDir)
                ? [.. inDir.OrderBy(f => PathRules.FileName(f.FullPath), StringComparer.OrdinalIgnoreCase)
                           .Select(f => (Member: PathRules.FileName(f.FullPath), Size: f.Key.Size, MtimeUtc: f.MtimeUtc))]
                : [];
            string name = PathRules.FileName(dir);
            if (!setFolders.TryGetValue(name, out List<SetFolderListing>? same))
            {
                same = new List<SetFolderListing>();
                setFolders[name] = same;
            }
            same.Add(new SetFolderListing(dir, name, members));
        }

        return new LibraryIndex(built, folders.ToImmutable(),
                                setFolders.ToImmutableDictionary(kv => kv.Key, kv => kv.Value.ToImmutableArray(), StringComparer.OrdinalIgnoreCase),
                                watermark, collected.UnavailableRoots, fromMtime.ToImmutable(), collected.Errors);
    }

    private static DateTime AsUtc(DateTime d) => d.Kind == DateTimeKind.Utc ? d : DateTime.SpecifyKind(d, DateTimeKind.Utc);

    /// <summary>Mean of unit vectors (the same centroid definition as clustering, Ref §8.2).</summary>
    private static GeoPoint? Centroid(IEnumerable<GeoPoint> points)
    {
        double x = 0, y = 0, z = 0;
        int n = 0;
        foreach (GeoPoint p in points)
        {
            double lat = p.Lat * Math.PI / 180, lon = p.Lon * Math.PI / 180;
            x += Math.Cos(lat) * Math.Cos(lon);
            y += Math.Cos(lat) * Math.Sin(lon);
            z += Math.Sin(lat);
            n++;
        }
        if (n == 0) return null;
        x /= n;
        y /= n;
        z /= n;
        return new GeoPoint(Math.Atan2(z, Math.Sqrt((x * x) + (y * y))) * 180 / Math.PI, Math.Atan2(y, x) * 180 / Math.PI);
    }
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*LibraryIndex_*"`
Expected: 12 passed. Then `dotnet test --solution uas-sort.slnx` — everything green.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Library/LibraryIndex.cs tests/UasSort.Core.Tests/Library/LibraryIndexTests.cs
git commit -m "feat(core): build the LibraryIndex from listings and the ledger, event-folder watermark (Part 05)" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 05.12: Settings defaults and the `settings.json` codec

`settings.json` is schema 1, camelCase, enums as names, and has **no `ledgerDir` key** (the `Settings` record has no such property; the folder is always `LedgerPaths.For(videoRoot)`). Defaults are derived from the Pictures known folder, never literal user paths (Ref §11, §13 *Settings*). A file that fails to parse or validate is "unreadable" and goes through recovery (05.13).

**Files:**
- Create: `src/UasSort.Core/Settings/SettingsDefaults.cs`
- Create: `src/UasSort.Core/Settings/SettingsCodec.cs`
- Test: `tests/UasSort.Core.Tests/Config/SettingsCodecTests.cs`

**Interfaces:**
```csharp
// Consumes (Part 02): Settings(int Schema, string VideoRoot, string PhotoRoot, ImmutableArray<string> PreviousPhotoRoots, double RadiusMiles,
//   int GapDays, StoredClockMode DroneClockMode, string DroneClockZone, bool CopyJpgTwin, MapSettings Map, LayoutSettings Layout,
//   bool RootsConfirmed); MapSettings(string Base, string StreetsStyleUrl, string StreetsDarkStyleUrl, string SatelliteUrl,
//   ImmutableDictionary<string, string> SatellitePresets); LayoutSettings(double TimelineWidth, double MapHeightRatio); LedgerPaths;
//   PathRules; UasSort.Core.Json.CoreJsonContext.Default.Settings (indented, camelCase, enums as names)
// Produces (defined here):
namespace UasSort.Core.Config;
public static class SettingsDefaults {
  public const string StreetsStyleUrl, StreetsDarkStyleUrl, EsriUrl, UsgsUrl;       // Ref §11 settings.json values
  public static MapSettings Map();
  public static LayoutSettings Layout();                                            // (380, 0.45)
  public static Settings Derive(string picturesFolder);   // <Pictures>\UAS Videos, <videoRoot>\Picture Offload, R 50, G 1, Zone
}                                                         // America/New_York, copyJpgTwin on, RootsConfirmed false
public sealed record SettingsParse(Settings? Settings, string? Error);
public static class SettingsCodec {
  public const int Schema = 1;
  public static string Serialize(Settings settings);
  public static SettingsParse Parse(string json);   // Error when unparseable, schema != 1, a root missing, R outside 5..100,
}                                                   // G outside 0..7, no droneClockZone; missing map/layout/previousPhotoRoots → defaults
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Config/SettingsCodecTests.cs
using UasSort.Core.Config;
using UasSort.Core;

namespace UasSort.Core.Tests.Config;

public sealed class SettingsCodecTests
{
    private const string Pictures = @"C:\Users\u\Pictures";

    [Fact]
    public void Settings_Derive_DefaultsComeFromThePicturesFolder()
    {
        Settings d = SettingsDefaults.Derive(Pictures);
        Assert.Equal(@"C:\Users\u\Pictures\UAS Videos", d.VideoRoot);
        Assert.Equal(@"C:\Users\u\Pictures\UAS Videos\Picture Offload", d.PhotoRoot);
        Assert.Empty(d.PreviousPhotoRoots);
        Assert.Equal((1, 50.0, 1), (d.Schema, d.RadiusMiles, d.GapDays));
        Assert.Equal((StoredClockMode.Zone, "America/New_York"), (d.DroneClockMode, d.DroneClockZone));
        Assert.True(d.CopyJpgTwin);
        Assert.False(d.RootsConfirmed);
        Assert.Equal("streets", d.Map.Base);
        Assert.Equal(SettingsDefaults.EsriUrl, d.Map.SatelliteUrl);
        Assert.Equal(new[] { "Esri", "USGS" }, d.Map.SatellitePresets.Keys.Order(StringComparer.Ordinal));
        Assert.Equal((380.0, 0.45), (d.Layout.TimelineWidth, d.Layout.MapHeightRatio));
        Assert.Equal(@"C:\Users\u\Pictures\UAS Videos\.uas-sort", LedgerPaths.For(d.VideoRoot));
    }

    [Fact]
    public void Settings_Serialize_HasNoLedgerDirKey()
    {
        string json = SettingsCodec.Serialize(SettingsDefaults.Derive(Pictures) with { RootsConfirmed = true });
        Assert.DoesNotContain("ledger", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"schema\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"droneClockMode\": \"Zone\"", json, StringComparison.Ordinal);
        Assert.Contains("\"rootsConfirmed\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"previousPhotoRoots\": []", json, StringComparison.Ordinal);
        Assert.Contains("\"USGS\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        Settings s = SettingsDefaults.Derive(Pictures) with
        {
            PreviousPhotoRoots = [@"D:\Old Offload"], DroneClockMode = StoredClockMode.SiteLocal, RadiusMiles = 33, GapDays = 0,
            RootsConfirmed = true,
        };
        string json = SettingsCodec.Serialize(s);
        SettingsParse back = SettingsCodec.Parse(json);
        Assert.Null(back.Error);
        Assert.NotNull(back.Settings);
        Assert.Equal(json, SettingsCodec.Serialize(back.Settings));
    }

    [Fact]
    public void Settings_Parse_TheReferenceFile()
    {
        const string json = """
            { "schema": 1, "rootsConfirmed": true,
              "videoRoot": "C:\\Users\\u\\OneDrive\\Pictures\\UAS Videos",
              "photoRoot": "C:\\Users\\u\\OneDrive\\Pictures\\UAS Videos\\Picture Offload",
              "previousPhotoRoots": [],
              "radiusMiles": 50, "gapDays": 1, "droneClockMode": "Zone", "droneClockZone": "America/New_York", "copyJpgTwin": true,
              "map": { "base": "streets", "streetsStyleUrl": "https://tiles.openfreemap.org/styles/liberty",
                       "streetsDarkStyleUrl": "https://tiles.openfreemap.org/styles/dark",
                       "satelliteUrl": "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                       "satellitePresets": { "Esri": "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                                             "USGS": "https://basemap.nationalmap.gov/arcgis/rest/services/USGSImageryOnly/MapServer/tile/{z}/{y}/{x}" } },
              "layout": { "timelineWidth": 380, "mapHeightRatio": 0.45 } }
            """;
        SettingsParse p = SettingsCodec.Parse(json);
        Assert.Null(p.Error);
        Settings s = Assert.IsType<Settings>(p.Settings);
        Assert.Equal(@"C:\Users\u\OneDrive\Pictures\UAS Videos", s.VideoRoot);
        Assert.True(s.RootsConfirmed);
        Assert.Equal(StoredClockMode.Zone, s.DroneClockMode);
        Assert.Equal(SettingsDefaults.UsgsUrl, s.Map.SatellitePresets["USGS"]);
    }

    [Fact]
    public void Settings_Parse_MissingOptionalBlocks_GetDefaults()
    {
        SettingsParse p = SettingsCodec.Parse("""
            { "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\V\\Picture Offload", "radiusMiles": 50, "gapDays": 1,
              "droneClockMode": "SiteLocal", "droneClockZone": "America/New_York", "copyJpgTwin": false, "rootsConfirmed": true }
            """);
        Settings s = Assert.IsType<Settings>(p.Settings);
        Assert.Empty(s.PreviousPhotoRoots);
        Assert.Equal(SettingsDefaults.Map().SatelliteUrl, s.Map.SatelliteUrl);
        Assert.Equal(380.0, s.Layout.TimelineWidth);
        Assert.Equal(StoredClockMode.SiteLocal, s.DroneClockMode);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{ "schema": 2, "videoRoot": "C:\\V", "photoRoot": "C:\\P", "radiusMiles": 50, "gapDays": 1, "droneClockMode": "Zone", "droneClockZone": "America/New_York" }""")]
    [InlineData("""{ "schema": 1, "photoRoot": "C:\\P", "radiusMiles": 50, "gapDays": 1, "droneClockMode": "Zone", "droneClockZone": "America/New_York" }""")]
    [InlineData("""{ "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\P", "radiusMiles": 500, "gapDays": 1, "droneClockMode": "Zone", "droneClockZone": "America/New_York" }""")]
    [InlineData("""{ "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\P", "radiusMiles": 50, "gapDays": 9, "droneClockMode": "Zone", "droneClockZone": "America/New_York" }""")]
    [InlineData("""{ "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\P", "radiusMiles": 50, "gapDays": 1, "droneClockMode": "Bogus", "droneClockZone": "America/New_York" }""")]
    [InlineData("""{ "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\P", "radiusMiles": 50, "gapDays": 1, "droneClockMode": 7, "droneClockZone": "America/New_York" }""")]
    [InlineData("""{ "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\P", "radiusMiles": 50, "gapDays": 1, "droneClockMode": "Zone" }""")]
    public void Settings_Parse_UnreadableFiles_GiveAnError(string json)
    {
        SettingsParse p = SettingsCodec.Parse(json);
        Assert.Null(p.Settings);
        Assert.False(string.IsNullOrWhiteSpace(p.Error));
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Settings_*"`
Expected: build fails with `CS0103: The name 'SettingsDefaults' does not exist in the current context` (and `SettingsCodec`; the namespace `UasSort.Core.Config` itself already exists through Part 02's `Namespaces.cs` anchor).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Settings/SettingsDefaults.cs
using UasSort.Core;

namespace UasSort.Core.Config;

/// <summary>Derived defaults (Ref §11 settings.json, §4.5 CLI defaults). No literal user paths.</summary>
public static class SettingsDefaults
{
    public const string StreetsStyleUrl = "https://tiles.openfreemap.org/styles/liberty";
    public const string StreetsDarkStyleUrl = "https://tiles.openfreemap.org/styles/dark";
    public const string EsriUrl = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}";
    public const string UsgsUrl = "https://basemap.nationalmap.gov/arcgis/rest/services/USGSImageryOnly/MapServer/tile/{z}/{y}/{x}";

    public static MapSettings Map()
        => new("streets", StreetsStyleUrl, StreetsDarkStyleUrl, EsriUrl,
               ImmutableDictionary<string, string>.Empty.Add("Esri", EsriUrl).Add("USGS", UsgsUrl));

    public static LayoutSettings Layout() => new(380, 0.45);

    public static Settings Derive(string picturesFolder)
    {
        string videoRoot = PathRules.Join(picturesFolder, "UAS Videos");
        return new Settings(SettingsCodec.Schema, videoRoot, PathRules.Join(videoRoot, "Picture Offload"), [], 50, 1,
                            StoredClockMode.Zone, "America/New_York", CopyJpgTwin: true, Map(), Layout(), RootsConfirmed: false);
    }
}
```

```csharp
// src/UasSort.Core/Settings/SettingsCodec.cs
using System.Text.Json;
using UasSort.Core;
using UasSort.Core.Json;

namespace UasSort.Core.Config;

public sealed record SettingsParse(Settings? Settings, string? Error);

/// <summary>settings.json ⇄ <see cref="Settings"/> (Ref §11). There is no ledgerDir key because there is no such property.</summary>
public static class SettingsCodec
{
    public const int Schema = 1;

    public static string Serialize(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(settings, CoreJsonContext.Default.Settings);
    }

    public static SettingsParse Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Settings? s;
        try
        {
            s = JsonSerializer.Deserialize(json, CoreJsonContext.Default.Settings);
        }
        catch (JsonException ex)
        {
            return new SettingsParse(null, "unreadable: " + ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return new SettingsParse(null, "unreadable: " + ex.Message);
        }
        if (s is null) return new SettingsParse(null, "empty settings");

        string? error =
            s.Schema != Schema ? $"unsupported schema {s.Schema}"
            : string.IsNullOrWhiteSpace(s.VideoRoot) || string.IsNullOrWhiteSpace(s.PhotoRoot) ? "missing videoRoot or photoRoot"
            : double.IsNaN(s.RadiusMiles) || s.RadiusMiles < 5 || s.RadiusMiles > 100 ? "radiusMiles outside 5..100"
            : s.GapDays is < 0 or > 7 ? "gapDays outside 0..7"
            : !Enum.IsDefined(s.DroneClockMode) ? "bad droneClockMode"
            : string.IsNullOrWhiteSpace(s.DroneClockZone) ? "missing droneClockZone"
            : null;
        if (error is not null) return new SettingsParse(null, error);

        return new SettingsParse(s with
        {
            PreviousPhotoRoots = s.PreviousPhotoRoots.IsDefault ? [] : s.PreviousPhotoRoots,
            Map = s.Map ?? SettingsDefaults.Map(),
            Layout = s.Layout ?? SettingsDefaults.Layout(),
        }, null);
    }
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Settings_*"`
Expected: 16 passed (5 facts + 11 unreadable rows).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Settings/SettingsDefaults.cs src/UasSort.Core/Settings/SettingsCodec.cs tests/UasSort.Core.Tests/Config/SettingsCodecTests.cs
git commit -m "feat(core): add settings.json codec and derived defaults (Part 05)" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 05.13: Settings load decisions (recovery, read-only load) and roots from the last run

`ISettingsStore.Load(readOnly)` (Part 09) reads `settings.json` (or learns it is missing), then asks this pure policy what to do. Missing file (first run): derived defaults with `RootsConfirmed = false`, so Setup shows. Readable file: used as is. Unreadable file: derived defaults, `Recovered = true`, `RootsConfirmed = false` (Blocking until the user confirms roots), the bad file to be kept as `settings.json.corrupt-` plus a `yyyyMMdd-HHmmss` UTC stamp, and the roots offered from the latest `run` record in the local backup mirrors. **Read-only** (the CLI): the same defaults, but no rename, no roots lookup, nothing created or written (Ref §4.5, §11, §13 *Settings*).

**Files:**
- Create: `src/UasSort.Core/Settings/SettingsLoadPolicy.cs`
- Create: `src/UasSort.Core/Settings/SettingsRecovery.cs`
- Test: `tests/UasSort.Core.Tests/Config/SettingsLoadPolicyTests.cs`

**Interfaces:**
```csharp
// Consumes: SettingsCodec, SettingsParse (05.12); LedgerReader, LedgerSnapshots, LedgerFileText (05.4/05.5);
//           Part 02: SettingsLoad(Settings Settings, bool Recovered, string? CorruptCopyPath, RunRoots? RootsFromLastRun), RunRoots, LedgerRun
// Produces (defined here):
namespace UasSort.Core.Config;
public sealed record SettingsLoadDecision(SettingsLoad Load, string? MoveCorruptTo);   // MoveCorruptTo: Platform renames the bad file there
public static class SettingsLoadPolicy {
  public static SettingsLoadDecision Decide(string settingsPath, string? fileText /* null = missing */, bool readOnly,
                                            Settings derivedDefaults, DateTime nowUtc, Func<RunRoots?> rootsFromLastRun);
}
public static class SettingsRecovery {
  public static RunRoots? RootsFromLastRun(IReadOnlyList<LedgerFileText> mirrors);   // latest run by EndUtc across every mirror given
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Config/SettingsLoadPolicyTests.cs
using UasSort.Core.Config;
using UasSort.Core.Ledger;
using UasSort.Core;
using static UasSort.Core.Tests.Ledger.LedgerLines;

namespace UasSort.Core.Tests.Config;

public sealed class SettingsLoadPolicyTests
{
    private const string SettingsPath = @"C:\Users\u\AppData\Local\uas-sort\settings.json";
    private const string Backup = @"C:\Users\u\AppData\Local\uas-sort\ledger-backup\";
    private static readonly Settings Defaults = SettingsDefaults.Derive(@"C:\Users\u\Pictures");
    private static readonly DateTime Now = new(2026, 10, 4, 20, 11, 0, DateTimeKind.Utc);

    private static RunRoots? MustNotBeCalled() => throw new InvalidOperationException("the read-only load must not look for roots");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsLoad_MissingFile_GivesFirstRunDefaults(bool readOnly)
    {
        SettingsLoadDecision d = SettingsLoadPolicy.Decide(SettingsPath, null, readOnly, Defaults, Now, MustNotBeCalled);
        Assert.Equal(Defaults, d.Load.Settings);
        Assert.False(d.Load.Settings.RootsConfirmed);
        Assert.False(d.Load.Recovered);
        Assert.Null(d.Load.CorruptCopyPath);
        Assert.Null(d.MoveCorruptTo);
    }

    [Fact]
    public void SettingsLoad_ReadableFile_IsUsed()
    {
        string text = SettingsCodec.Serialize(Defaults with { RootsConfirmed = true, RadiusMiles = 40 });
        SettingsLoadDecision d = SettingsLoadPolicy.Decide(SettingsPath, text, false, Defaults, Now, MustNotBeCalled);
        Assert.Equal(40, d.Load.Settings.RadiusMiles);
        Assert.True(d.Load.Settings.RootsConfirmed);
        Assert.False(d.Load.Recovered);
        Assert.Null(d.MoveCorruptTo);
    }

    [Fact]
    public void SettingsLoad_CorruptFile_RecoversBlockingAndOffersRootsFromTheLastRun()
    {
        var roots = new RunRoots(@"D:\Vids", @"D:\Vids\Picture Offload");
        SettingsLoadDecision d = SettingsLoadPolicy.Decide(SettingsPath, "{ \"schema\": 1, \"videoRoot\": ", false, Defaults, Now, () => roots);
        Assert.True(d.Load.Recovered);
        Assert.False(d.Load.Settings.RootsConfirmed);
        Assert.Equal(Defaults.VideoRoot, d.Load.Settings.VideoRoot);
        Assert.Equal(SettingsPath + ".corrupt-20261004-201100", d.Load.CorruptCopyPath);
        Assert.Equal(d.Load.CorruptCopyPath, d.MoveCorruptTo);
        Assert.Equal(roots, d.Load.RootsFromLastRun);
    }

    [Fact]
    public void SettingsLoad_ReadOnly_CorruptFile_RenamesNothingAndLooksUpNothing()
    {
        SettingsLoadDecision d = SettingsLoadPolicy.Decide(SettingsPath, "{bad", readOnly: true, Defaults, Now, MustNotBeCalled);
        Assert.Equal(Defaults, d.Load.Settings);
        Assert.True(d.Load.Recovered);
        Assert.Null(d.Load.CorruptCopyPath);
        Assert.Null(d.Load.RootsFromLastRun);
        Assert.Null(d.MoveCorruptTo);
    }

    [Fact]
    public void SettingsRecovery_RootsFromTheLatestRunAcrossMirrors()
    {
        var mirrors = new[]
        {
            new LedgerFileText(Backup + @"0123456789abcdef\ledger-DESKTOP-A.jsonl", Text(
                Run("u1", "run-a", Utc(2026, 9, 27, 21, 0), Utc(2026, 9, 27, 21, 30), @"C:\Old\UAS Videos", @"C:\Old\UAS Videos\Picture Offload"))),
            new LedgerFileText(Backup + @"fedcba9876543210\ledger-DESKTOP-A.jsonl", "garbage\n" + Text(
                Run("u2", "run-b", Utc(2026, 10, 4, 18, 0), Utc(2026, 10, 4, 18, 45), @"D:\Vids", @"D:\Vids\Picture Offload"),
                Run("u3", "run-c", Utc(2026, 9, 1, 18, 0), Utc(2026, 9, 1, 18, 45), @"E:\Older", @"E:\Older\Pics"))),
        };
        Assert.Equal(new RunRoots(@"D:\Vids", @"D:\Vids\Picture Offload"), SettingsRecovery.RootsFromLastRun(mirrors));
    }

    [Fact]
    public void SettingsRecovery_NoRunRecords_GivesNull()
    {
        Assert.Null(SettingsRecovery.RootsFromLastRun([]));
        Assert.Null(SettingsRecovery.RootsFromLastRun(
            [new LedgerFileText(Backup + @"0123456789abcdef\ledger-DESKTOP-A.jsonl", Text(Seen("s1", "X.DNG", 1, Utc(2026, 10, 4))))]));
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*SettingsLoad_*"`
Expected: build fails with `CS0103: The name 'SettingsLoadPolicy' does not exist in the current context` (and `SettingsRecovery`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Settings/SettingsLoadPolicy.cs
using System.Globalization;
using UasSort.Core;

namespace UasSort.Core.Config;

public sealed record SettingsLoadDecision(SettingsLoad Load, string? MoveCorruptTo);

/// <summary>What ISettingsStore.Load does with the file it found (Ref §11 Settings row, §4.5 read-only load).</summary>
public static class SettingsLoadPolicy
{
    public static SettingsLoadDecision Decide(string settingsPath, string? fileText, bool readOnly, Settings derivedDefaults,
                                              DateTime nowUtc, Func<RunRoots?> rootsFromLastRun)
    {
        ArgumentNullException.ThrowIfNull(derivedDefaults);
        ArgumentNullException.ThrowIfNull(rootsFromLastRun);
        Settings defaults = derivedDefaults with { RootsConfirmed = false };
        if (fileText is null) return new SettingsLoadDecision(new SettingsLoad(defaults, false, null, null), null);

        SettingsParse parsed = SettingsCodec.Parse(fileText);
        if (parsed.Settings is { } settings) return new SettingsLoadDecision(new SettingsLoad(settings, false, null, null), null);

        if (readOnly) return new SettingsLoadDecision(new SettingsLoad(defaults, true, null, null), null);

        string corrupt = settingsPath + ".corrupt-" + nowUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return new SettingsLoadDecision(new SettingsLoad(defaults, true, corrupt, rootsFromLastRun()), corrupt);
    }
}
```

```csharp
// src/UasSort.Core/Settings/SettingsRecovery.cs
using UasSort.Core.Ledger;
using UasSort.Core;

namespace UasSort.Core.Config;

/// <summary>Roots offered after a settings recovery: the latest run record in the local backup mirrors (Ref §11).</summary>
public static class SettingsRecovery
{
    public static RunRoots? RootsFromLastRun(IReadOnlyList<LedgerFileText> mirrors)
    {
        ArgumentNullException.ThrowIfNull(mirrors);
        if (mirrors.Count == 0) return null;
        LedgerSnapshot snap = LedgerReader.Read(mirrors, LedgerSnapshots.Detached("", mirrors.Select(m => m.FullPath)));
        LedgerRun? last = snap.Runs.OrderByDescending(r => r.EndUtc).ThenByDescending(r => r.StartUtc).FirstOrDefault();
        return last is null ? null : new RunRoots(last.VideoRoot, last.PhotoRoot);
    }
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*SettingsLoad_*"` then `-- --filter-method "*SettingsRecovery_*"`
Expected: 5 passed (2 theory rows + 3 facts), then 2 passed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Settings/SettingsLoadPolicy.cs src/UasSort.Core/Settings/SettingsRecovery.cs tests/UasSort.Core.Tests/Config/SettingsLoadPolicyTests.cs
git commit -m "feat(core): decide settings recovery and the read-only load (Part 05)" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 05.14: Root changes — `previousPhotoRoots` append and the video-root history checks

Two pure rules the Settings page (Parts 10–11) and `LedgerStore.CopyInto` (Part 09) build on. (1) When the photo root changes, the old one is appended to `previousPhotoRoots` automatically (Ref §7.1), and a root that becomes current again leaves that list. (2) After the video root changes (the new root is saved first, main spec §8), the "No history found in &lt;new root>\\.uas-sort; copy current ledger there?" prompt appears when the new root's status is Missing or Empty while the loaded snapshot has records; **[Start empty]** first confirms "N videos here were copied by uas-sort …" when the new root's listing holds videos matching the current ledger's `file` keys (Ref §13 *Newness*, *Ledger*). The learned drone-clock save is Part 04's `DroneClock.ApplyLearned`.

**Files:**
- Create: `src/UasSort.Core/Settings/SettingsEdits.cs`
- Create: `src/UasSort.Core/Ledger/VideoRootChange.cs`
- Test: `tests/UasSort.Core.Tests/Config/RootChangeTests.cs`

**Interfaces:**
```csharp
// Consumes: PathRules, FileKey.Of (Part 02), LibraryIndex (05.11) in the test; Part 02: Settings, LedgerSnapshot, LedgerFolderStatus,
//           LedgerFolderState, LedgerFile, DestRoot, LedgerPaths, ListingResult
// Produces (defined here):
namespace UasSort.Core.Config;
public static class SettingsEdits {
  public static Settings ChangePhotoRoot(Settings s, string newPhotoRoot);   // old root appended to PreviousPhotoRoots (no duplicates);
}                                                                           // the new root removed from it; same root → s unchanged
namespace UasSort.Core.Ledger;
public static class VideoRootChange {
  public static bool HasRecords(LedgerSnapshot snapshot);
  public static bool NeedsHistoryPrompt(LedgerFolderStatus newRootStatus, LedgerSnapshot current);   // NoHistoryInNewRoot (Ref §9.10)
  public static int AppCopiedVideos(ListingResult newRootListing, string newVideoRoot, LedgerSnapshot current);
                                                  // files (outside .uas-sort) whose FileKey has a ledger `file` record with root video
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Config/RootChangeTests.cs
using UasSort.Core.Config;
using UasSort.Core.Ledger;
using UasSort.Core;
using UasSort.Core.Tests.Ledger;
using UasSort.Core.Tests.Library;
using static UasSort.Core.Tests.Ledger.LedgerLines;

namespace UasSort.Core.Tests.Config;

public sealed class RootChangeTests
{
    private static readonly Settings Defaults = SettingsDefaults.Derive(@"C:\Users\u\Pictures");
    private static readonly string OldPhoto = Defaults.PhotoRoot;
    private const string NewVideo = @"D:\UAS Videos";
    private const string NewZrel = NewVideo + @"\2026\2026-09\2026-09-27 Zachar Bay";

    [Fact]
    public void RootChange_PhotoRootChange_AppendsTheOldRoot()
    {
        Settings s1 = SettingsEdits.ChangePhotoRoot(Defaults, @"D:\Photos");
        Assert.Equal(@"D:\Photos", s1.PhotoRoot);
        Assert.Equal(new[] { OldPhoto }, s1.PreviousPhotoRoots);

        Settings s2 = SettingsEdits.ChangePhotoRoot(s1, @"E:\P");
        Assert.Equal(new[] { OldPhoto, @"D:\Photos" }, s2.PreviousPhotoRoots);

        Settings s3 = SettingsEdits.ChangePhotoRoot(s2, OldPhoto.ToUpperInvariant() + @"\");   // back to the old root
        Assert.Equal(new[] { @"D:\Photos", @"E:\P" }, s3.PreviousPhotoRoots);

        Assert.Same(s3, SettingsEdits.ChangePhotoRoot(s3, s3.PhotoRoot.ToUpperInvariant() + @"\"));
    }

    private static LedgerFolderStatus NewRootStatus(LedgerFolderState state)
        => new(LedgerPaths.For(NewVideo), state, state != LedgerFolderState.Missing, true, false, true, [], [], []);

    [Fact]
    public void RootChange_HistoryPrompt_OnlyWhenTheNewRootHasNoLedgerAndWeHaveRecords()
    {
        LedgerSnapshot current = TestLedger.Snapshot(Seen("s1", "X.DNG", 1, Utc(2026, 10, 4)));
        Assert.True(VideoRootChange.NeedsHistoryPrompt(NewRootStatus(LedgerFolderState.Missing), current));
        Assert.True(VideoRootChange.NeedsHistoryPrompt(NewRootStatus(LedgerFolderState.Empty), current));
        Assert.False(VideoRootChange.NeedsHistoryPrompt(NewRootStatus(LedgerFolderState.Ok), current));        // new root already has ledger files
        Assert.False(VideoRootChange.NeedsHistoryPrompt(NewRootStatus(LedgerFolderState.Missing), TestLedger.Empty()));
        Assert.False(VideoRootChange.HasRecords(TestLedger.Empty()));
    }

    [Fact]
    public void RootChange_StartEmpty_CountsAppCopiedVideosAndTheWatermarkMoves()
    {
        DateTime T = LibraryFixture.T;
        var listing = new ListingResult([
            new FsEntry(NewZrel + @"\DJI_20260927142416_0148_D.MP4", "", false, 77_000_000, LibraryFixture.Utc(2026, 9, 27, 18, 25, 46), T, T, 0x20),
            new FsEntry(NewZrel + @"\DJI_20260927160000_0160_D.MP4", "", false, 60_000_000, LibraryFixture.Utc(2026, 9, 27, 20, 1, 30), T, T, 0x20),
            new FsEntry(NewVideo + @"\Picture Offload\DJI_20260927161000_0161_D.DNG", "", false, 27_000_000, T, T, T, 0x20),
            new FsEntry(NewVideo + @"\.uas-sort\DJI_20260927170000_0170_D.MP4", "", false, 5_000, T, T, T, 0x20)], []);
        LedgerSnapshot current = TestLedger.Snapshot(
            FileRec("f160", "DJI_20260927160000_0160_D.MP4", 60_000_000, @"C:\Old\UAS Videos\x\DJI_20260927160000_0160_D.MP4"),
            FileRec("f161", "DJI_20260927161000_0161_D.DNG", 27_000_000, @"C:\Old\Picture Offload\DJI_20260927161000_0161_D.DNG", root: "photo", kind: "photo"),
            FileRec("f170", "DJI_20260927170000_0170_D.MP4", 5_000, @"C:\Old\UAS Videos\y\DJI_20260927170000_0170_D.MP4"));

        Assert.Equal(1, VideoRootChange.AppCopiedVideos(listing, NewVideo, current));   // 0160 only: photos and .uas-sort don't count

        // the documented cost of [Start empty]: without the history the app copy 0160 moves the watermark later
        var clock = new ClockModel(ClockMode.Zone, "America/New_York", [], null, StoredClockMode.Zone, "America/New_York");
        var root = new RootListing(NewVideo, DestRoot.Video, false, true, listing);
        var photo = new RootListing(NewVideo + @"\Picture Offload", DestRoot.Photo, false, true, new ListingResult([], []));
        var listings = new LibraryListings(root, photo, []);
        Assert.Equal(LibraryFixture.Utc(2026, 9, 27, 18, 24, 16), LibraryIndex.Build(listings, current, clock).WatermarkUtc);
        Assert.Equal(LibraryFixture.Utc(2026, 9, 27, 20, 0, 0), LibraryIndex.Build(listings, TestLedger.Empty(), clock).WatermarkUtc);
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*RootChange_*"`
Expected: build fails with `CS0103: The name 'SettingsEdits' does not exist in the current context` (and `VideoRootChange`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Settings/SettingsEdits.cs
using UasSort.Core;

namespace UasSort.Core.Config;

public static class SettingsEdits
{
    /// <summary>Ref §7.1: when the photo root changes, the old one is appended to previousPhotoRoots automatically.</summary>
    public static Settings ChangePhotoRoot(Settings s, string newPhotoRoot)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(newPhotoRoot);
        if (PathRules.Equal(newPhotoRoot, s.PhotoRoot)) return s;
        var previous = s.PreviousPhotoRoots.Where(p => !PathRules.Equal(p, newPhotoRoot)).ToList();
        if (!previous.Any(p => PathRules.Equal(p, s.PhotoRoot))) previous.Add(s.PhotoRoot);
        return s with { PhotoRoot = PathRules.Normalize(newPhotoRoot), PreviousPhotoRoots = [.. previous] };
    }
}
```

```csharp
// src/UasSort.Core/Ledger/VideoRootChange.cs
using UasSort.Core;

namespace UasSort.Core.Ledger;

/// <summary>Checks behind the video-root change prompt and the [Start empty] confirmation (main spec §8, Ref §11, §13).</summary>
public static class VideoRootChange
{
    public static bool HasRecords(LedgerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return !snapshot.Files.IsEmpty || !snapshot.Decisions.IsEmpty || !snapshot.Seen.IsEmpty || !snapshot.Folders.IsEmpty
            || !snapshot.Runs.IsEmpty || !snapshot.CardDeletes.IsEmpty;
    }

    public static bool NeedsHistoryPrompt(LedgerFolderStatus newRootStatus, LedgerSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(newRootStatus);
        return newRootStatus.State is LedgerFolderState.Missing or LedgerFolderState.Empty && HasRecords(current);
    }

    public static int AppCopiedVideos(ListingResult newRootListing, string newVideoRoot, LedgerSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(newRootListing);
        ArgumentNullException.ThrowIfNull(current);
        string ledgerDir = LedgerPaths.For(newVideoRoot);
        return newRootListing.Entries.Count(e =>
            !e.IsDirectory
            && !PathRules.IsSameOrUnder(e.FullPath, ledgerDir)
            && current.Files.TryGetValue(FileKey.Of(PathRules.FileName(e.FullPath), e.Size), out LedgerFile? f)
            && f.Root == DestRoot.Video);
    }
}
```

Note: `ChangePhotoRoot` stores the new root normalized (no trailing separator), so the test's `OldPhoto.ToUpperInvariant() + @"\"` case compares through `PathRules.Equal`; the test asserts only `PreviousPhotoRoots` and `Assert.Same` identity, never the stored spelling of the re-selected root.

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*RootChange_*"`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Settings/SettingsEdits.cs src/UasSort.Core/Ledger/VideoRootChange.cs tests/UasSort.Core.Tests/Config/RootChangeTests.cs
git commit -m "feat(core): add photo-root history and video-root change checks (Part 05)" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 05.15: Part 05 verification

**Files:** none changed.

**Interfaces:** none.

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test --solution uas-sort.slnx`
Expected: all tests pass with `TreatWarningsAsErrors` on, including Part 05's 134 new cases: EventFolderName 16, LibraryFolder_DaysIn 3, LedgerCodec 18, LedgerParser 10, LedgerSnapshot 9, LedgerStatus 8, LedgerLoader 4, LibraryEntries 4, EventFolderLocator 12, MemberStart 12, LibraryIndex 12, Settings 16, SettingsLoad 5, SettingsRecovery 2, RootChange 3.

- [ ] **Step 2: Confirm no file-system type crept into this part's Core code**

Run: `grep -rnE "\b(File|Directory|FileInfo|DirectoryInfo|FileStream|DriveInfo)\." src/UasSort.Core/Library src/UasSort.Core/Ledger src/UasSort.Core/Settings`
Expected: no output (BannedApiAnalyzers already enforces this at build time; the grep is a second look). `Path.GetExtension` in `LibraryIndex.cs` is a string function and is not banned.

- [ ] **Step 3: Stop the build servers after WSL-driven builds**

Run: `dotnet build-server shutdown`
Expected: the compiler servers stop. No commit (nothing changed).

---

## Part 05 — Produces (summary)

**Core — `UasSort.Core` (partials of Part 02 types)**
- `LibraryFolder.DaysIn(tzId)` — local days of the members in the folder's ledger zone, else the given zone.
- `LibraryIndex` — `static Build(LibraryListings, LedgerSnapshot, ClockModel)`, `Match(FileKey)`, `SameNameOtherSize(normName, size)`, `Folders`, `SetFolder(name)`, `WatermarkUtc` (event-folder videos without a ledger `file` record only, decision 4), `UnavailableRoots` (Ref §3); plus, defined here, `Files`, `FilesIn(LibraryFolderRef)`, `StartsFromMtime` (the §7.1 mtime flag), `ListingErrors`.

**Core — `UasSort.Core.Library`**
- `EventFolderName.TryParse(name, out DateOnly, out string description)`.
- `EventFolderLocator.For(path, videoRoot, photoRoots, includeSelf) → LibraryFolderRef?` (video root only; photo roots and `.uas-sort` excluded).
- `MemberStart(Utc, FromMtime)`; `MemberStartResolver` — `TryStamp`, `Resolve(name, mtimeUtc, folderLedgerTz, clock)`, `MaxMtimeAfterStart` (2 h).
- `LibraryEntry`, `CollectedLibrary`, `LibraryEntries.Collect(listings)` (overlaps once, `.uas-sort` dropped, unavailable roots, listing errors).

**Core — `UasSort.Core.Ledger`**
- `LedgerCodec` — `Version`, `Serialize(record)` (one line, `t` first), `TryParse(line, out error)`.
- `LedgerFileText(FullPath, Text)`, `ParsedRecord`, `LedgerParseResult`, `LedgerParser.Parse(files)` (union, dedupe by `id`, torn final line, `torn` markers, parse issues).
- `LedgerSnapshotBuilder.Build(parsed, status)` (`Files`: strongest verification per key, `Unbuffered` &gt; `Cached` &gt; `NameSize`, latest among equals, decision 5), `LedgerReader.Read(files, status)`, `LedgerSnapshots.Empty(status)`, `LedgerSnapshots.Detached(folder, files)` (consumed by Parts 06–10).
- `LedgerFolderFacts`, `LedgerFolderStatusBuilder.Build(videoRoot, machine, facts)` (`PinnedBit`, `CloudBits`) for `ILedgerStore.Check()`.
- `LedgerLoader.Load(status, openRead)` for `ILedgerStore.Load()` (opens only the listed local files; nothing for Missing, VideoRootMissing or CloudOnly).
- Part 06 creates `UasSort.Testing.FakeLedgerStore` on top of `LedgerFolderStatusBuilder` and `LedgerLoader`; Part 09 `LedgerStore` uses the same two.
- `VideoRootChange` — `HasRecords`, `NeedsHistoryPrompt` (`NoHistoryInNewRoot`), `AppCopiedVideos` (the [Start empty] confirmation count).

**Core — `UasSort.Core.Config`** (folder `src/UasSort.Core/Settings/`)
- `SettingsDefaults` — URL constants, `Map()`, `Layout()`, `Derive(picturesFolder)`.
- `SettingsParse`, `SettingsCodec` — `Schema`, `Serialize`, `Parse` (validation; no `ledgerDir`; through Part 02's `CoreJsonContext`).
- `SettingsLoadDecision`, `SettingsLoadPolicy.Decide(path, text, readOnly, defaults, nowUtc, rootsFromLastRun)`; `SettingsRecovery.RootsFromLastRun(mirrors)`.
- `SettingsEdits.ChangePhotoRoot(settings, newRoot)` (old root appended to `previousPhotoRoots`).

**Tests (Core.Tests):** reusable internal helpers `LedgerLines`, `TestLedger` (namespace `UasSort.Core.Tests.Ledger`) and `LibraryFixture` (namespace `UasSort.Core.Tests.Library`), used by Parts 05–08 inside Core.Tests only: `LedgerLines` (record builders with the Ref §11 values), `TestLedger` (`Snapshot`, `Status`, `Empty`), `LibraryFixture` (listing builder that deliberately does not skip `.uas-sort`). Settings tests live in `tests/UasSort.Core.Tests/Config/` (namespace `UasSort.Core.Tests.Config`). 134 cases, incl. `LedgerSnapshot_LaterNameSizeRecord_DoesNotDowngradeVerified` (decision 5) and `LibraryIndex_Watermark_IgnoresVideosOutsideEventFolders` (decision 4), incl. **[Review Focus] 3**: `EventFolderLocator_NonEventLocations_GetNone` (Task 05.9) and `LibraryIndex_ReviewFocus_NonEventFoldersAndLooseFiles` (Task 05.11).

**Spec gaps, contradictions and cross-part mismatches (listed, not silently resolved):**
1. **Cross-part names.** Names reconciled in `00-interfaces.md`.
2. **Cloud-only ledger file.** Ref §13's tripwire says a cloud-only `ledger-B` "opens none of the ledger files". `LedgerLoader` implements the Ref (opens nothing while any file is cloud-only); decided (00-interfaces decisions 12, 37): Part 09's `LedgerStore.Load` loads through `LedgerLoader.Load`.
3. **`torn` scope.** The spec says a reader skips "line N of that machine's file". A `torn` record here applies only to an earlier line of the file that contains it, since `OpenOwn()` writes it into the file it repaired and conflict copies are byte copies. Consequence (decided, 00-interfaces decision 12): `CopyInto` ([Copy], Part 09) never copies `torn` records into the new root's own file, where they would name unrelated lines.
4. **Unterminated but complete final line.** Skipped like a torn line, without an issue, because `OpenOwn()` will mark it `torn` on the next append anyway; counting it now and dropping it later would make the snapshot unstable. That record is lost, so its file shows as NameSizeMatch on the next scan (the safe direction). Blank lines are ignored silently (the spec is silent).
5. **Watermark and non-event videos.** Ref §7.1: "the latest start of any library video without a ledger `file` record". Taken literally, an edited export under `Exports\` with a later mtime would move the watermark forward and could turn genuinely new photos into ProbablyImported (unticked). Decided (user): event-folder videos only — mention in the completion summary (00-interfaces decision 4; pinned by `LibraryIndex_Watermark_IgnoresVideosOutsideEventFolders` and the watermark assertion in `LibraryIndex_ReviewFocus_NonEventFoldersAndLooseFiles`).
6. **The member-start flag** (mtime used) has no field in Ref's `LibraryFile`/`LibraryFolder`; it is `LibraryIndex.StartsFromMtime`.
7. **Card-leftover centroids.** Ref §7.1 says the centroid comes from the ledger, "else from leftover clips on the card that match the folder's files", but `LibraryIndex.Build` receives no card items. The centroid here is ledger-only and `LocationSource.CardLeftovers` is never set; Part 06 must add leftover centroids (via `FilesIn` and `Match`).
8. **`RunCard` has no capacity**, so `LedgerRun.Card.TotalBytes` is 0; the serial is parsed as hex into `VolumeSerial`.
9. **"Latest per key" for `LedgerSnapshot.Files`** would let a later `verify:"nameSize"` record (e.g. AlreadyThere on a re-run) replace an earlier verified one, downgrading the audit from InLedger to NameSizeMatch. Decided: strongest verification wins (`Unbuffered` &gt; `Cached` &gt; `NameSize`, latest among equals; 00-interfaces decision 5; pinned by `LedgerSnapshot_LaterNameSizeRecord_DoesNotDowngradeVerified`).
10. **Set records.** `SetsByName` groups member `file` records by set name and first-frame `captureUtc`; this assumes Part 07 writes the set's first-frame `captureUtc` on every member record.
11. **Merging records:** for one folder path the later `folder` record's description, source, zone and centroid win and `Start`/`End` widen; for one key the latest unrevoked `decision` wins. The spec is silent on both.
12. **Settings.** R must be 5–100 and G 0–7 (the slider ranges); a `schema` other than 1 is unreadable, so an older build would move a future schema-2 file to `settings.json.corrupt-` plus a timestamp. A missing file (first run) gives `Recovered = false` with `RootsConfirmed = false`, although Ref §4.1 says "Recovered=true if defaults were used"; here recovery means an unreadable file only (kept, 00-interfaces decision 46). The "latest run" for recovered roots is taken by `EndUtc`.
13. **Video extensions** for member starts and the watermark are mp4, mov, insv, avi (the §5 media list minus stills). A photo root equal to the video root would suppress every event folder (not guarded here).
