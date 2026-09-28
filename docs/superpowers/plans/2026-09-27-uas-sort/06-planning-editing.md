# Part 06 — Planning and editing

**Goal:** turn a `ScanResult` into a reviewable, editable `Plan`: newness for videos, photos and sets (with `seen`, per-member set records and the no-watermark rule), clustering with caused boundaries and the item-anchored structural-edit algorithm, day splits, the two-pass `FolderDecider` (judged on the New and ticked-Conflict subset), folder naming and description suggestions, set folders, `Planner` (Prepare + Derive + the issue catalogue), `PlanSession` (serial edit queue, latest-wins previews, undo/redo, drafts) and `ScanService`. Tests: the 37 ported spike tests, the new planning cases, edit invariants, the golden replay (A0, A–E) from the checked-in fixture, the derive benchmark and the scan half of the fake-FS tripwire.

**Ref sections:** §7 (newness), §8 (grouping, decisions, naming, sets, edits), §9.10 (issue catalogue), §4.2 (Planner, PlanSession, ScanService), §13 (tests), §14 step 6. Main spec §5.4–5.9 wins on conflict.

**Depends on:** Part 01 (solution, props, test projects), Part 02 (model §3, ports §4.1, `IoGuardPolicy`, `FakeFileSystem`, `FakeCardReaderFactory`, `CardClassifier`, `FileKey`, `GlobalUsings.Core.cs`), Part 03 (`MetadataHarvester`, `DroneStampParser`), Part 04 (`DroneClock`, `TimeResolver`, `GeoTimeZoneResolver`, `GeoMath`, `Zones`, `ZoneNames`, `PlaceIndex`; test helpers `FixturePoints`, `FakePlaceIndex`), Part 05 (`LibraryIndex`, `EventFolderName`, `LedgerFolderStatusBuilder`, `LedgerLoader`, `LedgerSnapshots`, `Settings`).

## Conventions used by every task in this part

**Namespaces (fixed by `00-interfaces.md`, which wins over this part on names, namespaces and signatures):**

| Namespace | Types used here |
|---|---|
| `UasSort.Core` | every record, enum and union of Ref §3 and every port of Ref §4.1 (incl. `Settings`, `LedgerSnapshot`, `LedgerFile`, `LedgerDecision`, `LedgerSet`, `LedgerFolderStatus`, `ScanResult`, `GroupDraft`, `ClusterResult`, `FileKey`, `Draft`, `IPlanDeriver`, `IDirectoryLister`, `ICardReaderFactory`, `ICardReader`, `ILedgerStore`, `IVolumeProvider`, `VolumeInfo`, `FsEntry`, `ListingResult`, `ITimeZoneResolver`, `IPlaceIndex`, `PathRules`, `LedgerPaths`); also `LibraryIndex` (a Part 02 partial class completed by Part 05: static `Build`, instance `Match`, `SameNameOtherSize`, `Folders`, `SetFolder`, `WatermarkUtc`, `UnavailableRoots`) |
| `UasSort.Core.Library` | `EventFolderName.TryParse` (Part 05) |
| `UasSort.Core.Ledger` | `LedgerSnapshots.Empty`, `LedgerFolderFacts`, `LedgerFolderStatusBuilder.Build`, `LedgerLoader.Load` (Part 05) |
| `UasSort.Core.Time` | `DroneClock.Learn`, `DroneClock.Summarize` (static), `TimeResolver.Resolve(...)` (**static** class), `Zones`, `ZoneNames` (static) |
| `UasSort.Core.Geo` | `GeoMath` (static), `GeoTimeZoneResolver` (parameterless ctor) |
| `UasSort.Core.Card` | `CardClassifier` (instance, ctor `CardClassifier(TimeProvider clock)`, `Classify(CardSource, ListingResult)`) |
| `UasSort.Core.Media` | `MetadataHarvester` (**static** class, `MetadataHarvester.HarvestAsync(...)`) |
| `UasSort.Testing` | `FakeFileSystem`, `FakeCardReaderFactory`, `FakeGuardCall` (Part 02); `FixturePoints`, `FakePlaceIndex` (Part 04) |

Every project that references Core carries the fixed `GlobalUsings.Core.cs` (Part 02 Task 02.1), which imports `System.Collections.Immutable`, `UasSort.Core` and every `UasSort.Core.*` component namespace; `UasSort.Testing` and `Microsoft.Extensions.Time.Testing` are global in Core.Tests, and `Xunit` comes from the test csproj. There is no `UasSort.Core.Model` or `UasSort.Core.Ports` namespace, so no file here imports one; the remaining explicit `using UasSort.Core…;` lines are redundant but allowed. `UasSort.Testing.Planning` is imported per file.

**New namespaces produced here:** `UasSort.Core.Planning` (Planner, NewnessRules, Clusterer, DaySplitFinder, FolderDecider, ScanService, helpers), `UasSort.Core.Naming` (FolderNamer, SetFolderNamer, DescriptionSuggester), `UasSort.Core.Editing` (EditValidator, PlanSession), `UasSort.Testing.Planning` (scenario builders and the replay fixture only). The shared fakes this part creates live in flat `UasSort.Testing`: `FakeLedgerStore` (Task 06.21), `GatedPlanDeriver` and `PlanFingerprint` (Task 06.16).

**Commands.** From Windows: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Name*"`. From WSL prefix with `tools/r.sh` and run `dotnet build-server shutdown` afterwards (Global Constraints). The whole suite: `dotnet test --solution uas-sort.slnx`.

**Test project references.** `UasSort.Core.Tests` references `UasSort.Testing` (Part 01/02). `UasSort.Testing` and every test project reference `Microsoft.Extensions.TimeProvider.Testing` (Part 01 Task 01.2; central version 10.10.0); tests use its `FakeTimeProvider`.

---

### Task 06.1: Planning primitives (keys, geo, text) and the `NothingNew` issue code

**Files:**
- Create: `src/UasSort.Core/Planning/PlanKeys.cs`
- Create: `src/UasSort.Core/Planning/PlanningGeo.cs`
- Create: `src/UasSort.Core/Planning/PlanText.cs`
- Create: `src/UasSort.Core/Planning/PlanEditRefs.cs`
- Modify: the file that declares `public enum IssueCode` (Part 02; find it with `grep -rn "enum IssueCode" src/UasSort.Core`)
- Test: `tests/UasSort.Core.Tests/Planning/PlanPrimitivesTests.cs`

**Interfaces:**
- Consumes: `FileKey` (`NormalizeName`, `Of`), `PathRules.FileName`, `GeoPoint`, `Distance`, `ItemId`, `PlanEdit` and its cases, `IssueCode` (Part 02); `DroneStampParser.FromFileName` (Part 03); `GeoMath.EarthRadiusMeters`, `GeoMath.Haversine`, `Zones.Find`, `ZoneNames.Abbreviation`/`FormatOffset`/`FormatLocal`/`Region`/`ClockName` (Part 04).
- Produces (the key, distance and zone members delegate to the Core helpers above, registry decision 45; no second implementation):
  - `static class PlanKeys { string NormName(string fileName) /* = FileKey.NormalizeName */; FileKey Key(string fileName, long size) /* = FileKey.Of */; string FileName(string path) /* = PathRules.FileName */; DateTime? DjiStamp(string fileName) /* = DroneStampParser.FromFileName */; }`
  - `static class PlanningGeo { const double EarthRadiusM = GeoMath.EarthRadiusMeters; const double NearMiles = 10; static readonly TimeSpan HoursGuard; Distance Haversine(GeoPoint a, GeoPoint b) /* = GeoMath.Haversine */; GeoPoint? Centroid(IEnumerable<GeoPoint> points); Distance MaxPairwise(IReadOnlyList<GeoPoint> points); }`
  - `static class PlanText { string Miles(Distance d); string ShortDate(DateOnly d); string DateRange(DateOnly a, DateOnly b); string Offset(TimeSpan o) /* = ZoneNames.FormatOffset */; string ZoneAbbrev(string tzId, DateTime utc) /* = ZoneNames.Abbreviation */; string LocalTime(DateTime utc, string tzId) /* = ZoneNames.FormatLocal */; string ZoneName(string tzId) /* = ZoneNames.Region */; string ClockZoneName(string tzId) /* = ZoneNames.ClockName */; TimeZoneInfo Zone(string tzId) /* = Zones.Find */; string Count(int n, string one, string many); }`
  - `static class PlanEditRefs { IReadOnlyList<ItemId> Referenced(PlanEdit e); bool IsStructural(PlanEdit e); }`
  - `IssueCode.NothingNew` (Blocking; appended by this task to the Planner.Derive group of Part 02's closed catalogue — registry decision 14, Review Focus #1).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Planning/PlanPrimitivesTests.cs
using System.Collections.Immutable;
using UasSort.Core.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class PlanPrimitivesTests
{
    [Theory]
    [InlineData("DJI_20260927140127_0123_D.MP4", "dji_20260927140127_0123_d.mp4")]
    [InlineData("DJI_20260927140127_0123_D (2).MP4", "dji_20260927140127_0123_d.mp4")]
    [InlineData("best shot (12).mp4", "best shot.mp4")]
    [InlineData("PANO_0001.DNG", "pano_0001.dng")]
    [InlineData("name (x).MP4", "name (x).mp4")]
    public void NormName_LowercasesAndDropsTrailingCounter(string name, string expected)
        => Assert.Equal(expected, PlanKeys.NormName(name));

    [Fact]
    public void Key_UsesNormNameAndSize()
        => Assert.Equal(new FileKey("x.mp4", 42), PlanKeys.Key("X (3).MP4", 42));

    [Fact]
    public void DjiStamp_ParsesFilenameStamp()
    {
        Assert.Equal(new DateTime(2026, 9, 27, 14, 6, 27), PlanKeys.DjiStamp("DJI_20260927140627_0128_D.MP4"));
        Assert.Null(PlanKeys.DjiStamp("MAX_0061.MP4"));
        Assert.Equal("DJI_1.MP4", PlanKeys.FileName("DCIM/DJI_001/DJI_1.MP4"));
        Assert.Equal("b.mp4", PlanKeys.FileName(@"C:\a\b.mp4"));
    }

    [Fact]
    public void Haversine_MatchesCalibrationTable()
    {
        var council = new GeoPoint(64.6935, -164.2657);
        var anvil = new GeoPoint(64.5627, -165.3696);
        var zachar = new GeoPoint(57.5368, -153.7484);
        var kodiak = new GeoPoint(57.7996, -152.3902);
        Assert.Equal(33.9, PlanningGeo.Haversine(council, anvil).Miles, 1);
        Assert.Equal(53.4, PlanningGeo.Haversine(zachar, kodiak).Miles, 1);   // the 3.4 mi margin over R = 50
    }

    [Fact]
    public void Centroid_IsMeanUnitVector_AndNullWhenEmpty()
    {
        Assert.Null(PlanningGeo.Centroid([]));
        var c = PlanningGeo.Centroid([new GeoPoint(10, 179.9), new GeoPoint(10, -179.9)])!.Value;
        Assert.Equal(10, c.Lat, 3);
        Assert.Equal(180, Math.Abs(c.Lon), 3);                                  // antimeridian safe
    }

    [Theory]
    [InlineData(0.05, "<0.1 mi")]
    [InlineData(7.84, "7.8 mi")]
    [InlineData(33.7, "34 mi")]
    [InlineData(53.4, "53 mi")]
    public void Miles_FormatsPerUnitsTable(double miles, string expected)
        => Assert.Equal(expected, PlanText.Miles(Distance.FromMiles(miles)));

    [Fact]
    public void Dates_ZonesAndOffsets()
    {
        Assert.Equal("Sep 27", PlanText.ShortDate(new DateOnly(2026, 9, 27)));
        Assert.Equal("Jul 25–26", PlanText.DateRange(new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26)));
        Assert.Equal("Jul 31–Aug 2", PlanText.DateRange(new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 2)));
        Assert.Equal("Sep 27", PlanText.DateRange(new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27)));
        Assert.Equal("UTC\u22124", PlanText.Offset(TimeSpan.FromHours(-4)));
        Assert.Equal("UTC+5:30", PlanText.Offset(new TimeSpan(5, 30, 0)));
        var utc = new DateTime(2026, 9, 27, 18, 24, 16, DateTimeKind.Utc);
        Assert.Equal("AKDT", PlanText.ZoneAbbrev("America/Anchorage", utc));
        Assert.Equal("Sep 27 10:24 AKDT", PlanText.LocalTime(utc, "America/Anchorage"));
        Assert.Equal("Alaska", PlanText.ZoneName("America/Anchorage"));
        Assert.Equal("US Eastern", PlanText.ClockZoneName("America/New_York"));
        Assert.Equal("1 conflict", PlanText.Count(1, "conflict", "conflicts"));
        Assert.Equal("2 conflicts", PlanText.Count(2, "conflict", "conflicts"));
    }

    [Fact]
    public void EditRefs_ListEveryReferencedItem()
    {
        var a = new ItemId("a"); var b = new ItemId("b");
        Assert.Equal(new[] { a, b }, PlanEditRefs.Referenced(new Merge(a, b)));
        Assert.Equal(new[] { a, b }, PlanEditRefs.Referenced(new MoveToGroup([a], b)));
        Assert.Equal(new[] { a }, PlanEditRefs.Referenced(new Rename(a, "x", [b])));
        Assert.True(PlanEditRefs.IsStructural(new SplitBefore(a)));
        Assert.False(PlanEditRefs.IsStructural(new SetDayIncluded(new DateOnly(2026, 1, 1), true)));
    }

    [Fact]
    public void IssueCatalogue_HasNothingNew()
        => Assert.True(Enum.IsDefined(IssueCode.NothingNew));
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PlanPrimitivesTests"`
Expected: build error CS0103/CS0246 (`PlanKeys`, `PlanningGeo`, `PlanText`, `PlanEditRefs` do not exist) and CS0117 (`IssueCode` has no `NothingNew`).

- [ ] **Step 3: Implement**

Add `NothingNew` to the Planner.Derive group of `IssueCode` (Part 02 file), right after `LedgerNoHistory`:

```csharp
  LedgerParseIssue, LedgerCloudOnly, LedgerUnwritable, LedgerNotPinned, LedgerNoHistory,
  NothingNew,   // Review Focus #1: every clip and photo is already imported or decided; Blocking, blocks Offload with its reason
```

```csharp
// src/UasSort.Core/Planning/PlanKeys.cs
using UasSort.Core.Media;

namespace UasSort.Core.Planning;

/// <summary>Matching keys (Ref §7.1) for planning code: thin names over Part 02's FileKey/PathRules and Part 03's DroneStampParser,
/// so the planner's keys are exactly the keys Part 05 indexed.</summary>
public static class PlanKeys
{
    public static string NormName(string fileName) => FileKey.NormalizeName(fileName);

    public static FileKey Key(string fileName, long size) => FileKey.Of(fileName, size);

    public static string FileName(string path) => PathRules.FileName(path);

    public static DateTime? DjiStamp(string fileName) => DroneStampParser.FromFileName(fileName);
}
```

```csharp
// src/UasSort.Core/Planning/PlanningGeo.cs
using UasSort.Core.Geo;

namespace UasSort.Core.Planning;

/// <summary>Distances and centroids for grouping (Ref §8.1–8.2). Distances come from Part 04's GeoMath (Earth radius shared with map.js).</summary>
public static class PlanningGeo
{
    public const double EarthRadiusM = GeoMath.EarthRadiusMeters;
    public const double NearMiles = 10;                                // day-split emphasis and next-day append confidence
    public static readonly TimeSpan HoursGuard = TimeSpan.FromHours(3); // H

    public static Distance Haversine(GeoPoint a, GeoPoint b) => GeoMath.Haversine(a, b);

    public static GeoPoint? Centroid(IEnumerable<GeoPoint> points)
    {
        double x = 0, y = 0, z = 0; var n = 0;
        foreach (var p in points)
        {
            double la = Rad(p.Lat), lo = Rad(p.Lon);
            x += Math.Cos(la) * Math.Cos(lo); y += Math.Cos(la) * Math.Sin(lo); z += Math.Sin(la); n++;
        }
        if (n == 0) return null;
        var hyp = Math.Sqrt(x * x + y * y);
        return new GeoPoint(Deg(Math.Atan2(z, hyp)), Deg(Math.Atan2(y, x)));
    }

    public static Distance MaxPairwise(IReadOnlyList<GeoPoint> points)
    {
        double best = 0;
        for (var i = 0; i < points.Count; i++)
            for (var j = i + 1; j < points.Count; j++)
                best = Math.Max(best, Haversine(points[i], points[j]).Meters);
        return new Distance(best);
    }

    private static double Rad(double d) => d * Math.PI / 180;
    private static double Deg(double r) => r * 180 / Math.PI;
}
```

```csharp
// src/UasSort.Core/Planning/PlanText.cs
using System.Globalization;
using UasSort.Core.Time;

namespace UasSort.Core.Planning;

/// <summary>Why/issue text formatting per Ref §9.13 (miles, dates); zone names, abbreviations and offsets come from Part 04's ZoneNames.</summary>
public static class PlanText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static TimeZoneInfo Zone(string tzId) => Zones.Find(tzId);

    public static string Miles(Distance d)
    {
        var mi = d.Miles;
        if (mi < 0.1) return "<0.1 mi";
        if (mi < 10) return mi.ToString("0.0", Inv) + " mi";
        return Math.Round(mi, MidpointRounding.AwayFromZero).ToString("0", Inv) + " mi";
    }

    public static string ShortDate(DateOnly d) => d.ToString("MMM d", Inv);

    public static string DateRange(DateOnly a, DateOnly b)
    {
        if (a == b) return ShortDate(a);
        return a.Month == b.Month && a.Year == b.Year
            ? $"{ShortDate(a)}–{b.Day.ToString(Inv)}"
            : $"{ShortDate(a)}–{ShortDate(b)}";
    }

    public static string Offset(TimeSpan o) => ZoneNames.FormatOffset(o);

    public static string ZoneAbbrev(string tzId, DateTime utc) => ZoneNames.Abbreviation(tzId, utc);

    public static string LocalTime(DateTime utc, string tzId) => ZoneNames.FormatLocal(utc, tzId);

    /// <summary>Site-zone names for the clock-mismatch InfoBar (Ref §9.2 short table), else the IANA ID.</summary>
    public static string ZoneName(string tzId) => ZoneNames.Region(tzId);

    public static string ClockZoneName(string tzId) => ZoneNames.ClockName(tzId);

    public static string Count(int n, string one, string many) => $"{n.ToString(Inv)} {(n == 1 ? one : many)}";
}
```

Note: `ZoneName` gives the InfoBar's short names (Eastern, Central, Mountain, Arizona, Pacific, Alaska, Hawaii; Ref §9.2); `ClockZoneName` gives the clock-banner names ("US Eastern (America/New_York)", Ref §6.1). Both, with `ZoneAbbrev`, `Offset`, `LocalTime` and `Zone`, are one-line delegations to Part 04's `ZoneNames`/`Zones` (registry decision 45), so plan texts and the Review clock banner never disagree; `Offset(TimeSpan.Zero)` is `"UTC"`.

```csharp
// src/UasSort.Core/Planning/PlanEditRefs.cs

namespace UasSort.Core.Planning;

/// <summary>Items an edit references (drop rule of Ref §8.9 step 2.1 and draft resume counting).</summary>
public static class PlanEditRefs
{
    public static IReadOnlyList<ItemId> Referenced(PlanEdit e) => e switch
    {
        Merge m => [m.InA, m.InB],
        SplitBefore s => [s.First],
        MoveToNewGroup mv => [.. mv.Items],
        MoveToGroup mt => [.. mt.Items, mt.InTarget],
        Rename r => [r.InGroup],
        Retarget rt => [rt.InGroup],
        SetIncluded si => [.. si.Items],
        SetDayIncluded => [],
    };

    public static bool IsStructural(PlanEdit e) => e is Merge or SplitBefore or MoveToNewGroup or MoveToGroup;
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PlanPrimitivesTests"`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Planning tests/UasSort.Core.Tests/Planning src/UasSort.Core
git commit -m "feat: planning primitives (keys, geo, text) and NothingNew issue code

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.2: Planning scenario builder (Testing)

The spike's `vid`/`dng` helpers and the Ref §13 fixture constants, producing `ScanResult`s that go through the real `DroneClock.Learn` and `LibraryIndex.Build` ("Scenarios are built as `ScanResult`s by `UasSort.Testing`'s scenario builder", Ref §13).

**Files:**
- Create: `tests/UasSort.Testing/Planning/Sites.cs`
- Create: `tests/UasSort.Testing/Planning/Clip.cs`
- Create: `tests/UasSort.Testing/Planning/PlanScenario.cs`
- Test: `tests/UasSort.Core.Tests/Planning/PlanScenarioTests.cs`

This part defines no place-index fake: tests use Part 04's `UasSort.Testing.FakePlaceIndex(params IReadOnlyList<PlaceHit> places)` (Task 04.7), which filters by class and radius and recomputes `Away` with `GeoMath`. `Microsoft.Extensions.TimeProvider.Testing` is already referenced by `UasSort.Testing` (Part 01 Task 01.2).

**Interfaces:**
- Consumes: Ref §3 records (`RawItem`, `VideoUnit`, `PhotoUnit`, `SetUnit`, `CardEntry`, `Mp4Info`, `StillInfo`, `GpsFix`, `NoFix`, `CardSource`, `CardIdentity`, `CardInventory`, `Settings`, `MapSettings`, `LayoutSettings`, `LibraryListings`, `RootListing`, `LedgerSnapshot`, `LedgerFile`, `LedgerDecision`, `LedgerSet`, `LedgerFolderStatus`, `ScanResult`); `FsEntry`, `ListingResult`, `IPlaceIndex`, `PlaceHit` (Part 02, namespace `UasSort.Core`); `DroneClock.Learn` (Part 04), `GeoTimeZoneResolver` (Part 04); `UasSort.Testing.FixturePoints` (Part 04 Task 04.6); `LibraryIndex.Build` (Part 05); `Planner` (Task 06.11 — the two `Planner` helpers below compile once 06.11 lands; until then they are added in 06.11 Step 3, see there).
- Produces (all `UasSort.Testing.Planning`, defined here):
  - `static class Sites { GeoPoint Anvil, Council, NomeA, NomeB, Zachar, KodiakTown, NewportAm, NewportPm, Makaha; }` — each member is `=> FixturePoints.<same name>` (registry decision 42; no second copy of the coordinates)
  - `static class Clip { const string Serial; RawItem Vid(string stamp, int n, GeoPoint? loc = null, long? size = null, bool moov = true, DateTime? session = null, int clockMinusUtcHours = -4); RawItem Dng(string stamp, int n, GeoPoint? loc = null, long? size = null, bool withJpgTwin = false); RawItem Set(string setName, string firstDto, GeoPoint? loc, params (string Name, long Size, DateTime MtimeUtc)[] members); RawItem Autel(string name, long size, DateTime mtimeUtc); ItemId Id(this RawItem r); DateTime Utc(string stamp, int clockMinusUtcHours = -4); }`
  - `sealed class PlanScenario` with `const string VideoRoot = @"C:\lib\UAS Videos"`, `PcZoneId = "America/Anchorage"`, `static DateTime NowUtc`, `static CardSource Source`, settable `Root`, `ClockMode`, `ClockZone`, `RootsConfirmed`, `VideoRootAvailable`, `PhotoRootAvailable`, `LedgerState`; fluent `Card`, `Library`, `LibraryFile`, `LibraryDir`, `PreviousPhotoRootFile`, `LedgerFile`, `LedgerDecision`, `LedgerSeen`, `LedgerSet`, `LedgerParseIssue`; `ScanResult Build()`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Planning/PlanScenarioTests.cs
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class PlanScenarioTests
{
    [Fact]
    public void Vid_FollowsTheSpikeHelper()
    {
        var v = Clip.Vid("20260927140127", 123, Sites.Zachar);
        Assert.Equal("DJI_20260927140127_0123_D.MP4", v.Name);
        Assert.Equal(100_000_123, v.Bytes);
        Assert.Equal(new DateTime(2026, 9, 27, 18, 1, 27, DateTimeKind.Utc), v.Mp4!.MvhdUtc);
        Assert.Equal(new DateTime(2026, 9, 27, 18, 2, 57, DateTimeKind.Utc), v.CardMtimeUtc);
        Assert.Equal(new ItemId("DCIM/DJI_001/DJI_20260927140127_0123_D.MP4"), v.Id());
        Assert.True(v.Mp4!.First is GpsFix);
        Assert.Null(Clip.Vid("20260727002013", 14, Sites.Anvil, moov: false).Mp4!.MvhdUtc);
    }

    [Fact]
    public void Build_ProducesScanResultWithLibraryMatches()
    {
        var z = Clip.Vid("20260927140127", 123, Sites.Zachar);
        var scan = new PlanScenario()
            .Card(z)
            .Library(@"2026\2026-09\2026-09-27 Zachar Bay", z)
            .Build();
        Assert.Single(scan.Inventory.Units);
        Assert.Single(scan.Raw);
        Assert.Single(scan.Library.Match(PlanKeys.Key(z.Name, z.Bytes)));
        Assert.Contains(scan.Library.Folders, f => f.Ref.Description == "Zachar Bay");
        Assert.Equal(ClockMode.Zone, scan.Clock.Mode);
        Assert.Equal(@"C:\lib\UAS Videos", scan.Settings.VideoRoot);
    }

    [Fact] // Sites are Part 04's FixturePoints, not a second copy
    public void Sites_AreTheFixturePoints()
    {
        Assert.Equal(FixturePoints.Zachar, Sites.Zachar);
        Assert.Equal(FixturePoints.KodiakTown, Sites.KodiakTown);
        Assert.Equal(FixturePoints.Makaha, Sites.Makaha);
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PlanScenarioTests"`
Expected: build error CS0246 (`Clip`, `Sites`, `PlanScenario` not found).

- [ ] **Step 3: Implement**

```csharp
// tests/UasSort.Testing/Planning/Sites.cs
namespace UasSort.Testing.Planning;

/// <summary>The spike's fixture constants (Ref §13), taken from Part 04's <see cref="FixturePoints"/> (one copy of the coordinates).</summary>
public static class Sites
{
    public static GeoPoint Anvil => FixturePoints.Anvil;
    public static GeoPoint Council => FixturePoints.Council;
    public static GeoPoint NomeA => FixturePoints.NomeA;
    public static GeoPoint NomeB => FixturePoints.NomeB;
    public static GeoPoint Zachar => FixturePoints.Zachar;
    public static GeoPoint KodiakTown => FixturePoints.KodiakTown;
    public static GeoPoint NewportAm => FixturePoints.NewportAm;
    public static GeoPoint NewportPm => FixturePoints.NewportPm;
    public static GeoPoint Makaha => FixturePoints.Makaha;
}
```

```csharp
// tests/UasSort.Testing/Planning/Clip.cs
using System.Collections.Immutable;
using System.Globalization;

namespace UasSort.Testing.Planning;

/// <summary>Port of the spike's vid()/dng() helpers: mvhd = stamp + 4 h as UTC, mtime = mvhd + 90 s.</summary>
public static class Clip
{
    public const string Serial = "TESTSERIAL";

    public static DateTime Stamp(string stamp) =>
        DateTime.SpecifyKind(DateTime.ParseExact(stamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture), DateTimeKind.Unspecified);

    public static DateTime Utc(string stamp, int clockMinusUtcHours = -4) =>
        DateTime.SpecifyKind(Stamp(stamp).AddHours(-clockMinusUtcHours), DateTimeKind.Utc);

    public static ItemId Id(this RawItem r) => r.Unit.Id;

    public static RawItem Vid(string stamp, int n, GeoPoint? loc = null, long? size = null, bool moov = true,
                              DateTime? session = null, int clockMinusUtcHours = -4)
    {
        var dc = Stamp(stamp);
        var utc = Utc(stamp, clockMinusUtcHours);
        var name = $"DJI_{stamp}_{n:0000}_D.MP4";
        var rel = $"DCIM/DJI_001/{name}";
        var bytes = size ?? 100_000_000L + n;
        var mtime = utc.AddSeconds(90);
        var entry = new CardEntry(rel, bytes, mtime, mtime, mtime, 0x20, EntryClass.Video, null);
        GpsProbe first;
        if (loc is { } p) first = new GpsFix(p, 100, 0, moov ? GpsSource.DjmdModelTable : GpsSource.MdatHeadFallback, moov ? "3-3-4-1" : null);
        else first = new NoFix(NoFixReason.AllProbedSamplesZero);
        var info = new Mp4Info(moov ? utc : (DateTime?)null, moov, first, null, "dvtm_Air3s.proto",
                               session, session is null ? null : Serial, null, moov ? TimeSpan.FromSeconds(60) : (TimeSpan?)null);
        return new RawItem(new VideoUnit(new ItemId(rel), entry, false), ItemKind.Video, name, bytes, mtime, dc, info, null, null);
    }

    public static RawItem Dng(string stamp, int n, GeoPoint? loc = null, long? size = null, bool withJpgTwin = false)
    {
        var dc = Stamp(stamp);
        var name = $"DJI_{stamp}_{n:0000}_D.DNG";
        var rel = $"DCIM/DJI_001/{name}";
        var bytes = size ?? 25_000_000L + n;
        var mtime = Utc(stamp);
        var entry = new CardEntry(rel, bytes, mtime, mtime, mtime, 0x20, EntryClass.Photo, null);
        CardEntry? twin = withJpgTwin
            ? new CardEntry($"DCIM/DJI_001/DJI_{stamp}_{n:0000}_D.JPG", 8_000_000L + n, mtime, mtime, mtime, 0x20, EntryClass.PhotoTwin, null)
            : null;
        GpsProbe gps;
        if (loc is { } p) gps = new GpsFix(p, null, 0, GpsSource.Exif, null);
        else gps = new NoFix(NoFixReason.NoGpsTag);
        var still = new StillInfo(dc, null, gps, "FC9113", null);
        return new RawItem(new PhotoUnit(new ItemId(rel), entry, twin), ItemKind.Photo, name, bytes, mtime, dc, null, still, null);
    }

    public static RawItem Set(string setName, string firstDto, GeoPoint? loc, params (string Name, long Size, DateTime MtimeUtc)[] members)
    {
        var dto = DateTime.SpecifyKind(DateTime.ParseExact(firstDto, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), DateTimeKind.Unspecified);
        var dir = $"DCIM/PANORAMA/{setName}";
        var entries = members.Select(m => new CardEntry($"{dir}/{m.Name}", m.Size, m.MtimeUtc, m.MtimeUtc, m.MtimeUtc, 0x20,
                                                        EntryClass.SetMember, null)).ToImmutableArray();
        GpsProbe gps;
        if (loc is { } p) gps = new GpsFix(p, null, 0, GpsSource.Exif, null);
        else gps = new NoFix(NoFixReason.NoGpsTag);
        var still = new StillInfo(dto, null, gps, "FC9113", null);
        return new RawItem(new SetUnit(new ItemId(dir), SetKind.Panorama, setName, entries), ItemKind.Set, setName,
                           members.Sum(m => m.Size), members[0].MtimeUtc, dto, null, still, null);
    }

    /// <summary>A non-DJI clip (Autel MAX_####): no mvhd, no stamp, so TimeResolver times it from mtime.</summary>
    public static RawItem Autel(string name, long size, DateTime mtimeUtc)
    {
        var rel = $"DCIM/100MEDIA/{name}";
        var entry = new CardEntry(rel, size, mtimeUtc, mtimeUtc, mtimeUtc, 0x20, EntryClass.Video, null);
        return new RawItem(new VideoUnit(new ItemId(rel), entry, false), ItemKind.Video, name, size, mtimeUtc, null, null, null, null);
    }
}
```

```csharp
// tests/UasSort.Testing/Planning/PlanScenario.cs
using System.Collections.Immutable;
using UasSort.Core.Geo;
using UasSort.Core.Library;
using UasSort.Core.Planning;
using UasSort.Core.Time;

namespace UasSort.Testing.Planning;

/// <summary>Builds a ScanResult the way ScanService would, without any disk: card raws, library listings, ledger.</summary>
public sealed partial class PlanScenario
{
    public const string VideoRoot = @"C:\lib\UAS Videos";
    public const string PcZoneId = "America/Anchorage";
    public const string OldMachine = "PC-OLD";
    public static readonly DateTime NowUtc = new(2026, 10, 4, 20, 0, 0, DateTimeKind.Utc);
    public static readonly CardSource Source = new(@"E:\", new CardIdentity(0x1A2B3C4D, "DJI", "exFAT", 256_060_514_304), false, false);
    public static TimeZoneInfo PcZone => TimeZoneInfo.FindSystemTimeZoneById(PcZoneId);

    private readonly List<RawItem> _card = [];
    private readonly List<FsEntry> _video = [];
    private readonly List<FsEntry> _photo = [];
    private readonly Dictionary<string, List<FsEntry>> _previous = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FileKey, LedgerFile> _files = [];
    private readonly Dictionary<FileKey, LedgerDecision> _decisions = [];
    private readonly Dictionary<FileKey, DateTime> _seen = [];
    private readonly Dictionary<string, List<LedgerSet>> _sets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LedgerParseIssue> _parse = [];

    public string Root { get; init; } = VideoRoot;
    public string PhotoRoot => Root + @"\Picture Offload";
    public StoredClockMode ClockMode { get; set; } = StoredClockMode.Zone;
    public string ClockZone { get; set; } = "America/New_York";
    public bool RootsConfirmed { get; set; } = true;
    public bool VideoRootAvailable { get; set; } = true;
    public bool PhotoRootAvailable { get; set; } = true;
    public LedgerFolderState LedgerState { get; set; } = LedgerFolderState.Ok;

    public PlanScenario Card(params RawItem[] items) { _card.AddRange(items); return this; }

    /// <summary>Lists every file of the given units under folderRel (relative to the video root, backslashes).</summary>
    public PlanScenario Library(string folderRel, params RawItem[] items)
    {
        foreach (var r in items)
            foreach (var f in Files(r))
                LibraryFile($@"{folderRel}\{(r.Unit is SetUnit s ? s.SetName + @"\" : "")}{f.Name}", f.Size, f.MtimeUtc);
        return this;
    }

    public PlanScenario LibraryFile(string relPath, long size, DateTime mtimeUtc, uint attributes = 0x20)
    {
        var full = Root + @"\" + relPath;
        _video.Add(new FsEntry(full, relPath, false, size, mtimeUtc, mtimeUtc, mtimeUtc, attributes));
        const string photoPrefix = @"Picture Offload\";
        if (relPath.StartsWith(photoPrefix, StringComparison.OrdinalIgnoreCase))
            _photo.Add(new FsEntry(full, relPath[photoPrefix.Length..], false, size, mtimeUtc, mtimeUtc, mtimeUtc, attributes));
        return this;
    }

    public PlanScenario LibraryDir(string relPath)
    {
        var full = Root + @"\" + relPath;
        var t = NowUtc.AddDays(-30);
        _video.Add(new FsEntry(full, relPath, true, 0, t, t, t, 0x10));
        const string photoPrefix = @"Picture Offload\";
        if (relPath.StartsWith(photoPrefix, StringComparison.OrdinalIgnoreCase))
            _photo.Add(new FsEntry(full, relPath[photoPrefix.Length..], true, 0, t, t, t, 0x10));
        return this;
    }

    public PlanScenario PreviousPhotoRootFile(string root, string relPath, long size, DateTime mtimeUtc)
    {
        if (!_previous.TryGetValue(root, out var list)) _previous[root] = list = [];
        list.Add(new FsEntry(root + @"\" + relPath, relPath, false, size, mtimeUtc, mtimeUtc, mtimeUtc, 0x20));
        return this;
    }

    /// <summary>One ledger `file` record per file of the unit; folderRel null = the photo root.</summary>
    public PlanScenario LedgerFile(RawItem r, string? folderRel, GeoPoint? point = null, string? tz = null,
                                   VerifyKind verify = VerifyKind.Unbuffered)
    {
        foreach (var f in Files(r))
        {
            var key = PlanKeys.Key(f.Name, f.Size);
            var dest = folderRel is null ? $@"{PhotoRoot}\{f.Name}" : $@"{Root}\{folderRel}\{f.Name}";
            _files[key] = new LedgerFile(key, f.CardRel, folderRel is null ? DestRoot.Photo : DestRoot.Video, dest, null, verify,
                NowUtc.AddDays(-7), r.Mp4?.MvhdUtc, point, tz, null, null, r.Unit is SetUnit s ? s.SetName : null, OldMachine, "run-1");
        }
        return this;
    }

    public PlanScenario LedgerDecision(RawItem r, DecisionKind kind, int members = int.MaxValue)
    {
        foreach (var f in Files(r).Take(members))
        {
            var key = PlanKeys.Key(f.Name, f.Size);
            _decisions[key] = new LedgerDecision(Guid.NewGuid().ToString(), key, kind, NowUtc.AddDays(-3), OldMachine,
                                                 r.Unit is SetUnit s ? s.SetName : null, "test");
        }
        return this;
    }

    public PlanScenario LedgerSeen(RawItem r, DateTime atUtc, int members = int.MaxValue)
    {
        foreach (var f in Files(r).Take(members)) _seen[PlanKeys.Key(f.Name, f.Size)] = atUtc;
        return this;
    }

    public PlanScenario LedgerSet(string setName, DateTime firstFrameUtc, params (string Member, long Size)[] members)
    {
        if (!_sets.TryGetValue(setName, out var list)) _sets[setName] = list = [];
        list.Add(new LedgerSet(setName, firstFrameUtc, [.. members]));
        return this;
    }

    public PlanScenario LedgerParseIssue(string file, int line, string reason)
    {
        _parse.Add(new LedgerParseIssue(file, line, reason));
        return this;
    }

    public ScanResult Build()
    {
        var raw = _card.ToImmutableArray();
        var clock = DroneClock.Learn(raw, ClockMode, ClockZone, new GeoTimeZoneResolver(), PcZone);
        var settings = new Settings(1, Root, PhotoRoot, [.. _previous.Keys], 50, 1, ClockMode, ClockZone, true,
            new MapSettings("streets", "https://tiles.openfreemap.org/styles/liberty", "https://tiles.openfreemap.org/styles/dark",
                            "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                            ImmutableDictionary<string, string>.Empty),
            new LayoutSettings(420, 0.55), RootsConfirmed);
        var listings = new LibraryListings(
            new RootListing(Root, DestRoot.Video, false, VideoRootAvailable,
                            new ListingResult(VideoRootAvailable ? [.. _video] : [], [])),
            new RootListing(PhotoRoot, DestRoot.Photo, false, PhotoRootAvailable,
                            new ListingResult(PhotoRootAvailable ? [.. _photo] : [], [])),
            [.. _previous.Select(kv => new RootListing(kv.Key, DestRoot.Photo, true, true, new ListingResult([.. kv.Value], [])))]);
        var ledger = BuildLedger();
        var lib = LibraryIndex.Build(listings, ledger, clock);
        var entries = raw.SelectMany(r => Entries(r)).ToImmutableArray();
        var inv = new CardInventory(Source, NowUtc, "0123456789abcdef", entries, [.. raw.Select(r => r.Unit)], "FC9113", []);
        return new ScanResult(inv, raw, lib, ledger, clock, [], settings);
    }

    private LedgerSnapshot BuildLedger()
    {
        var folder = Root + @"\.uas-sort";
        var exists = LedgerState is not (LedgerFolderState.Missing or LedgerFolderState.VideoRootMissing);
        var status = new LedgerFolderStatus(folder, LedgerState, exists, false, LedgerState != LedgerFolderState.NotPinned,
            LedgerState != LedgerFolderState.Unwritable, exists ? [folder + @"\ledger-PC-OLD.jsonl"] : [],
            LedgerState == LedgerFolderState.CloudOnly ? [folder + @"\ledger-B.jsonl"] : [], []);
        return new LedgerSnapshot(
            _files.ToImmutableDictionary(),
            _sets.ToImmutableDictionary(kv => kv.Key, kv => kv.Value.ToImmutableArray(), StringComparer.OrdinalIgnoreCase),
            _decisions.ToImmutableDictionary(),
            _seen.ToImmutableDictionary(),
            ImmutableDictionary.Create<string, LedgerFolder>(StringComparer.OrdinalIgnoreCase),
            [], [], [.. _parse], [], status);
    }

    private static IEnumerable<CardEntry> Entries(RawItem r) => r.Unit switch
    {
        VideoUnit v => [v.Mp4],
        PhotoUnit p => p.JpgTwin is { } t ? [p.Primary, t] : [p.Primary],
        SetUnit s => s.Members,
    };

    private static IEnumerable<(string Name, long Size, DateTime MtimeUtc, string CardRel)> Files(RawItem r) =>
        Entries(r).Select(e => (PlanKeys.FileName(e.RelPath), e.Size, e.MtimeUtc, e.RelPath));
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PlanScenarioTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Testing/Planning tests/UasSort.Core.Tests/Planning/PlanScenarioTests.cs
git commit -m "test: planning scenario builder and spike clip helpers

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.3: FolderNamer — `Clean`, `NewFolderRel`, conflict names

**Files:**
- Create: `src/UasSort.Core/Naming/FolderNamer.cs`
- Test: `tests/UasSort.Core.Tests/Naming/FolderNamerTests.cs`

**Interfaces:**
- Consumes: nothing beyond the BCL.
- Produces (`UasSort.Core.Naming`, defined here):
  - `static class FolderNamer { const int MaxDescription = 80; const int MaxTempPath = 400; const string TempSuffix = ".uas-sort.tmp"; const string FolderExistsWhy = "Folder exists; appending"; string Clean(string raw); string NewFolderRel(DateOnly start, string description); string TempPath(string finalPath); string ConflictName(string fileName, Func<string, bool> taken); string TwinName(string conflictPrimaryName, string twinName); }`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Naming/FolderNamerTests.cs
using UasSort.Core.Naming;

namespace UasSort.Core.Tests.Naming;

public sealed class FolderNamerTests
{
    // Ported test #37 (test_sanitize_and_naming → Clean_And_NewFolderRel)
    [Fact]
    public void Clean_And_NewFolderRel()
    {
        Assert.Equal("Newport, RI", FolderNamer.Clean("Newport, RI"));
        Assert.Equal("A B test", FolderNamer.Clean(" A/B: \"test\"?  "));
        Assert.Equal("Nome Rd", FolderNamer.Clean("Nome Rd..."));
        Assert.Equal(@"2026\2026-09\2026-09-27", FolderNamer.NewFolderRel(new DateOnly(2026, 9, 27), "  "));
        Assert.Equal(@"2026\2026-07\2026-07-25 Anvil", FolderNamer.NewFolderRel(new DateOnly(2026, 7, 25), "Anvil"));
    }

    [Fact]
    public void Clean_ReplacesControlAndReservedCharacters()
    {
        Assert.Equal("a b c d", FolderNamer.Clean("a<b>c|d"));
        Assert.Equal("tab here", FolderNamer.Clean("tab\there"));
        Assert.Equal("x y", FolderNamer.Clean("x\u0001y"));
        Assert.Equal("", FolderNamer.Clean(" . . "));
    }

    [Fact] // [Review Focus] #2: Unicode letters and emoji are kept; trailing dots/spaces stripped
    public void Clean_KeepsUnicodeAndEmoji_StripsTrailingDotsAndSpaces()
    {
        Assert.Equal("Café Ñandú", FolderNamer.Clean("Café Ñandú. . "));
        Assert.Equal("Sunset 🌅 Kodiak", FolderNamer.Clean("  Sunset   🌅 Kodiak... "));
        Assert.Equal("Øksfjord – 北海道", FolderNamer.Clean("Øksfjord – 北海道 ."));
    }

    [Fact] // [Review Focus] #2: the 80-char cap never splits a surrogate pair or leaves a trailing dot
    public void Clean_TruncatesAt80WithoutSplittingEmoji()
    {
        var s = new string('a', 79) + "🌅";                     // 79 + 2 UTF-16 units
        var cleaned = FolderNamer.Clean(s);
        Assert.Equal(new string('a', 79), cleaned);
        Assert.Equal(80, FolderNamer.Clean(new string('b', 100)).Length);
        Assert.Equal(new string('c', 77), FolderNamer.Clean(new string('c', 77) + "..." + new string('d', 10)).TrimEnd('d'));
    }

    [Fact]
    public void TempPath_AddsSuffix()
        => Assert.Equal(@"C:\v\x.MP4.uas-sort.tmp", FolderNamer.TempPath(@"C:\v\x.MP4"));

    [Fact] // Conflict (n) naming (Ref §7.4): stem (2).ext or the next free (n)
    public void ConflictName_PicksNextFreeCounter()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "X (2).DNG", "x (3).dng" };
        Assert.Equal("X (4).DNG", FolderNamer.ConflictName("X.DNG", taken.Contains));
        Assert.Equal("clip (2).MP4", FolderNamer.ConflictName("clip.MP4", _ => false));
        Assert.Equal("X (4).JPG", FolderNamer.TwinName("X (4).DNG", "X.JPG"));
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*FolderNamerTests"`
Expected: build error CS0103 (`FolderNamer` does not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Naming/FolderNamer.cs
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UasSort.Core.Naming;

/// <summary>Folder paths and description cleaning (Ref §8.6), conflict names (Ref §7.4).</summary>
public static partial class FolderNamer
{
    public const int MaxDescription = 80;
    public const int MaxTempPath = 400;
    public const string TempSuffix = ".uas-sort.tmp";
    public const string FolderExistsWhy = "Folder exists; appending";

    private const string Reserved = "<>:\"/\\|?*";

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    public static string Clean(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw) sb.Append(ch < 0x20 || Reserved.Contains(ch) ? ' ' : ch);
        var s = Whitespace().Replace(sb.ToString(), " ").Trim().TrimEnd('.', ' ');
        if (s.Length <= MaxDescription) return s;
        var cut = new StringBuilder(MaxDescription);
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext())
        {
            var el = e.GetTextElement();
            if (cut.Length + el.Length > MaxDescription) break;
            cut.Append(el);
        }
        return cut.ToString().TrimEnd('.', ' ');
    }

    public static string NewFolderRel(DateOnly start, string description)
    {
        var d = Clean(description);
        var leaf = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + (d.Length > 0 ? " " + d : "");
        return $@"{start.ToString("yyyy", CultureInfo.InvariantCulture)}\{start.ToString("yyyy-MM", CultureInfo.InvariantCulture)}\{leaf}";
    }

    public static string TempPath(string finalPath) => finalPath + TempSuffix;

    public static string ConflictName(string fileName, Func<string, bool> taken)
    {
        var dot = fileName.LastIndexOf('.');
        var (stem, ext) = dot <= 0 ? (fileName, "") : (fileName[..dot], fileName[dot..]);
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem} ({n.ToString(CultureInfo.InvariantCulture)}){ext}";
            if (!taken(candidate)) return candidate;
        }
    }

    /// <summary>The JPG twin follows its DNG's (n) name: "X (2).DNG" + "X.JPG" → "X (2).JPG" (Ref §7.3 rule 2b).</summary>
    public static string TwinName(string conflictPrimaryName, string twinName)
    {
        var pd = conflictPrimaryName.LastIndexOf('.');
        var td = twinName.LastIndexOf('.');
        return (pd <= 0 ? conflictPrimaryName : conflictPrimaryName[..pd]) + (td < 0 ? "" : twinName[td..]);
    }
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*FolderNamerTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Naming tests/UasSort.Core.Tests/Naming
git commit -m "feat: FolderNamer clean, new-folder paths and conflict names

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.4: NewnessRules — videos

**Files:**
- Create: `src/UasSort.Core/Planning/NewnessRules.cs`
- Test: `tests/UasSort.Core.Tests/Planning/VideoNewnessTests.cs`

**Interfaces:**
- Consumes: `LibraryIndex.Match(FileKey)`, `LibraryIndex.SameNameOtherSize(string, long)`, `LibraryFile.EventFolder`, `LedgerSnapshot.Files/Decisions` (Part 05); `PlanKeys`, `PlanText` (06.1).
- Produces: `static partial class NewnessRules { Newness Video(VideoUnit v, LibraryIndex lib, LedgerSnapshot ledger); }` (Photo added in 06.6).

Rules (Ref §7.2): Imported (listing gives the event folder; evidence from the ledger record when there is one, else `LibraryNameSize`) → Decided (unrevoked decision) → Conflict (same NormName, other size, in listings or ledger) → New(`NoMatch`).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Planning/VideoNewnessTests.cs
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class VideoNewnessTests
{
    private const string Zrel = @"2026\2026-09\2026-09-27 Zachar Bay";

    private static Newness Of(ScanResult s, RawItem r) => NewnessRules.Video((VideoUnit)r.Unit, s.Library, s.Ledger);

    // Ported test #36 (test_video_conflict_duplicate_removed → VideoNewness_ConflictNoMatchLedger)
    [Fact]
    public void VideoNewness_ConflictNoMatchLedger()
    {
        var a = Clip.Vid("20260927140127", 123, Sites.Zachar, size: 105_764_094);
        var conflict = Clip.Vid("20260927140127", 123, Sites.Zachar, size: 7_340_032);
        var dup = Clip.Vid("20260927150000", 130, Sites.Zachar, size: 555_555_555);
        var gone = Clip.Vid("20260927151000", 131, Sites.Zachar);
        var s = new PlanScenario()
            .Card(conflict, dup, gone)
            .Library(Zrel, a)
            .LibraryFile($@"{Zrel}\best shot.MP4", 555_555_555, new DateTime(2026, 9, 27, 19, 0, 0, DateTimeKind.Utc))
            .LedgerFile(gone, Zrel, Sites.Zachar, "America/Anchorage")
            .Build();

        var c = Assert.IsType<Conflict>(Of(s, conflict));
        Assert.Equal($@"{PlanScenario.VideoRoot}\{Zrel}\DJI_20260927140127_0123_D.MP4", c.ExistingPath);
        Assert.Equal(105_764_094, c.ExistingSize);
        Assert.Equal(new IsNew(NewReason.NoMatch, null), Of(s, dup));
        var imp = Assert.IsType<Imported>(Of(s, gone));
        Assert.Equal(Evidence.LedgerVerified, imp.By);
        Assert.Null(imp.Folder);                                   // culled from the library: no wall
    }

    [Fact]
    public void Listed_IsImportedWithItsEventFolder()
    {
        var z = Clip.Vid("20260927140127", 123, Sites.Zachar);
        var s = new PlanScenario().Card(z).Library(Zrel, z).Build();
        var imp = Assert.IsType<Imported>(Of(s, z));
        Assert.Equal(Evidence.LibraryNameSize, imp.By);
        Assert.Equal("Zachar Bay", imp.Folder!.Description);
        Assert.Equal(new DateOnly(2026, 9, 27), imp.Folder.NameDate);
    }

    [Fact] // " (n)" removal for matching (Ref §7.1)
    public void CounterSuffixedLibraryCopy_StillMatches()
    {
        var z = Clip.Vid("20260927140127", 123, Sites.Zachar);
        var s = new PlanScenario().Card(z)
            .LibraryFile($@"{Zrel}\DJI_20260927140127_0123_D (2).MP4", z.Bytes, z.CardMtimeUtc).Build();
        Assert.IsType<Imported>(Of(s, z));
    }

    [Fact]
    public void ListedAndInLedger_TakesLedgerEvidenceAndListedFolder()
    {
        var z = Clip.Vid("20260927140127", 123, Sites.Zachar);
        var s = new PlanScenario().Card(z).Library(Zrel, z).LedgerFile(z, Zrel, verify: VerifyKind.NameSize).Build();
        var imp = Assert.IsType<Imported>(Of(s, z));
        Assert.Equal(Evidence.LedgerNameSize, imp.By);
        Assert.Equal("Zachar Bay", imp.Folder!.Description);
    }

    [Fact]
    public void Dismissed_IsDecided()
    {
        var z = Clip.Vid("20260927140127", 123, Sites.Zachar);
        var s = new PlanScenario().Card(z).LedgerDecision(z, DecisionKind.Dismissed).Build();
        var d = Assert.IsType<Decided>(Of(s, z));
        Assert.Equal(DecisionKind.Dismissed, d.Kind);
        Assert.Equal(PlanScenario.OldMachine, d.Machine);
    }

    [Fact]
    public void LedgerOnlyOtherSize_IsConflictWithLedgerDest()
    {
        var old = Clip.Vid("20260927140127", 123, Sites.Zachar, size: 1_000);
        var card = Clip.Vid("20260927140127", 123, Sites.Zachar, size: 2_000);
        var s = new PlanScenario().Card(card).LedgerFile(old, Zrel).Build();
        var c = Assert.IsType<Conflict>(Of(s, card));
        Assert.Equal(1_000, c.ExistingSize);
        Assert.EndsWith(@"Zachar Bay\DJI_20260927140127_0123_D.MP4", c.ExistingPath);
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*VideoNewnessTests"`
Expected: build error CS0103 (`NewnessRules` does not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Planning/NewnessRules.cs
using UasSort.Core.Library;

namespace UasSort.Core.Planning;

/// <summary>Newness rules of Ref §7 (videos §7.2; photos and sets §7.3 in NewnessRules.Photo.cs).</summary>
public static partial class NewnessRules
{
    public static Newness Video(VideoUnit v, LibraryIndex lib, LedgerSnapshot ledger)
    {
        var name = PlanKeys.FileName(v.Mp4.RelPath);
        var key = PlanKeys.Key(name, v.Mp4.Size);
        var listed = lib.Match(key);
        ledger.Files.TryGetValue(key, out var lf);
        if (!listed.IsEmpty || lf is not null)
        {
            var inFolder = listed.FirstOrDefault(f => f.EventFolder is not null) ?? (listed.IsEmpty ? null : listed[0]);
            var by = lf is null ? Evidence.LibraryNameSize
                   : lf.Verify == VerifyKind.NameSize ? Evidence.LedgerNameSize : Evidence.LedgerVerified;
            var why = inFolder is not null
                ? $"in library: {inFolder.FullPath}"
                : $"copied on {PlanText.ShortDate(DateOnly.FromDateTime(lf!.AtUtc))}; no longer in the library";
            return new Imported(by, inFolder?.EventFolder, why);
        }
        if (ledger.Decisions.TryGetValue(key, out var d))
            return new Decided(d.Kind, d.AtUtc, d.Machine);
        return FindConflict(key, lib, ledger) ?? (Newness)new IsNew(NewReason.NoMatch, null);
    }

    /// <summary>Same NormName, different size, in any listed root or the ledger (callers have already ruled out a same-size match).</summary>
    internal static Conflict? FindConflict(FileKey key, LibraryIndex lib, LedgerSnapshot ledger)
    {
        var other = lib.SameNameOtherSize(key.NormName, key.Size);
        if (!other.IsEmpty) return new Conflict(other[0].FullPath, other[0].Key.Size);
        foreach (var (k, f) in ledger.Files)
            if (k.NormName == key.NormName && k.Size != key.Size)
                return new Conflict(f.Dest, k.Size);
        return null;
    }
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*VideoNewnessTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Planning/NewnessRules.cs tests/UasSort.Core.Tests/Planning/VideoNewnessTests.cs
git commit -m "feat: video newness rules (imported, decided, conflict, new)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.5: SetFolderNamer — the set-clash rule

**Files:**
- Create: `src/UasSort.Core/Naming/SetFolderNamer.cs`
- Test: `tests/UasSort.Core.Tests/Naming/SetFolderNamerTests.cs`

**Interfaces:**
- Consumes: `LibraryIndex.SetFolder(string)` → `ImmutableArray<SetFolderListing>`; `LedgerSnapshot.SetsByName` (Part 05); `SetUnit`, `Item`, `SetPlacement`, `SetResolution` (Part 02); `PlanKeys` (06.1).
- Produces: `static class SetFolderNamer { SetPlacement Resolve(SetUnit set, Item first, LibraryIndex lib, LedgerSnapshot ledger, ISet<string> batchTaken); }` — `batchTaken` must be case-insensitive (`StringComparer.OrdinalIgnoreCase`); `MembersToCopy` holds member file names.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Naming/SetFolderNamerTests.cs
using UasSort.Core.Naming;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Naming;

public sealed class SetFolderNamerTests
{
    private static readonly DateTime M1 = new(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc);
    private static readonly DateTime M2 = new(2026, 5, 25, 13, 30, 31, DateTimeKind.Utc);
    private static readonly DateTime FirstFrameUtc = new(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc);

    private static RawItem Pano(string name = "001_0087", int members = 2) => Clip.Set(name, "2026-05-25 09:30:28", Sites.KodiakTown,
        [.. new (string, long, DateTime)[] { ("PANO_0001.DNG", 13_751_808, M1), ("PANO_0002.DNG", 12_882_432, M2), ("PANO_0003.DNG", 12_000_000, M2) }.Take(members)]);

    private static Item ItemOf(RawItem r) => new(r,
        new ItemTime(FirstFrameUtc, TimeSource.DroneClockZone, "America/Anchorage", TzSource.Gps, new DateOnly(2026, 5, 25),
                     new DateTime(2026, 5, 25, 5, 30, 28)),
        null, null, ItemFlags.None, new IsNew(NewReason.NoMatch, null));

    private static SetPlacement Resolve(PlanScenario s, RawItem set, ISet<string>? taken = null)
    {
        var scan = s.Card(set).Build();
        return SetFolderNamer.Resolve((SetUnit)set.Unit, ItemOf(set), scan.Library, scan.Ledger,
                                      taken ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoFolder_IsPlain()
    {
        var p = Resolve(new PlanScenario(), Pano());
        Assert.Equal(("001_0087", SetResolution.Plain), (p.FolderName, p.Resolution));
        Assert.Equal(new[] { "PANO_0001.DNG", "PANO_0002.DNG" }, p.MembersToCopy);
    }

    [Fact]
    public void EmptyExistingFolder_IsPlain()
        => Assert.Equal(SetResolution.Plain, Resolve(new PlanScenario().LibraryDir(@"Picture Offload\001_0087"), Pano()).Resolution);

    [Fact]
    public void SameMembersWithin2s_IsImported()
    {
        var s = new PlanScenario()
            .LibraryFile(@"Picture Offload\001_0087\PANO_0001.DNG", 13_751_808, M1.AddSeconds(2))
            .LibraryFile(@"Picture Offload\001_0087\PANO_0002.DNG", 12_882_432, M2.AddSeconds(-1));
        var p = Resolve(s, Pano());
        Assert.Equal(("001_0087", SetResolution.Imported), (p.FolderName, p.Resolution));
        Assert.Empty(p.MembersToCopy);
    }

    [Fact]
    public void MtimeOff3s_Clashes_ThenDateSuffixed()
    {
        var s = new PlanScenario()
            .LibraryFile(@"Picture Offload\001_0087\PANO_0001.DNG", 13_751_808, M1.AddSeconds(3))
            .LibraryFile(@"Picture Offload\001_0087\PANO_0002.DNG", 12_882_432, M2);
        var p = Resolve(s, Pano());
        Assert.Equal(("001_0087 2026-05-25", SetResolution.DateSuffixed), (p.FolderName, p.Resolution));
    }

    [Fact]
    public void ExistingSubsetOfCard_Resumes()
    {
        var s = new PlanScenario().LibraryFile(@"Picture Offload\001_0087\PANO_0001.DNG", 13_751_808, M1);
        var p = Resolve(s, Pano());
        Assert.Equal(("001_0087", SetResolution.Resume), (p.FolderName, p.Resolution));
        Assert.Equal(new[] { "PANO_0002.DNG" }, p.MembersToCopy);
    }

    [Fact] // after a partial Card cleanup the card keeps only some members
    public void CardSubsetOfExisting_IsImported()
    {
        var s = new PlanScenario()
            .LibraryFile(@"Picture Offload\001_0087\PANO_0001.DNG", 13_751_808, M1)
            .LibraryFile(@"Picture Offload\001_0087\PANO_0002.DNG", 12_882_432, M2)
            .LibraryFile(@"Picture Offload\001_0087\PANO_0003.DNG", 12_000_000, M2);
        Assert.Equal(SetResolution.Imported, Resolve(s, Pano(members: 2)).Resolution);
    }

    [Fact]
    public void PlainAndDatedClash_TakesCounter2()
    {
        var s = new PlanScenario()
            .LibraryFile(@"Picture Offload\001_0087\OTHER.DNG", 1, M1)
            .LibraryFile(@"Picture Offload\001_0087 2026-05-25\OTHER.DNG", 1, M1);
        Assert.Equal("001_0087 2026-05-25 (2)", Resolve(s, Pano()).FolderName);
    }

    [Fact]
    public void BatchTaken_SkipsToNextCandidate()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "001_0087" };
        var p = Resolve(new PlanScenario(), Pano(), taken);
        Assert.Equal(("001_0087 2026-05-25", SetResolution.DateSuffixed), (p.FolderName, p.Resolution));
        Assert.Contains("001_0087 2026-05-25", taken);
    }

    [Fact]
    public void LedgerSetWithSameMembersAndFirstFrame_IsImported()
    {
        var s = new PlanScenario().LedgerSet("001_0087", FirstFrameUtc, ("PANO_0001.DNG", 13_751_808), ("PANO_0002.DNG", 12_882_432));
        Assert.Equal(SetResolution.Imported, Resolve(s, Pano()).Resolution);
        var other = new PlanScenario().LedgerSet("001_0087", FirstFrameUtc.AddHours(1), ("PANO_0001.DNG", 13_751_808), ("PANO_0002.DNG", 12_882_432));
        Assert.Equal(SetResolution.Plain, Resolve(other, Pano()).Resolution);
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*SetFolderNamerTests"`
Expected: build error CS0103 (`SetFolderNamer` does not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Naming/SetFolderNamer.cs
using System.Globalization;
using UasSort.Core.Library;
using UasSort.Core.Planning;

namespace UasSort.Core.Naming;

/// <summary>The user's set-clash rule (Ref §8.8).</summary>
public static class SetFolderNamer
{
    private static readonly TimeSpan MtimeTolerance = TimeSpan.FromSeconds(2);

    public static SetPlacement Resolve(SetUnit set, Item first, LibraryIndex lib, LedgerSnapshot ledger, ISet<string> batchTaken)
    {
        var card = set.Members.Select(m => (Name: PlanKeys.FileName(m.RelPath), m.Size, m.MtimeUtc)).ToList();

        if (ledger.SetsByName.TryGetValue(set.SetName, out var known)
            && known.Any(k => SameMemberList(k, card) && (k.FirstFrameCaptureUtc - first.Time.CaptureUtc).Duration() <= MtimeTolerance))
            return new SetPlacement(set.Id, set.SetName, SetResolution.Imported, []);

        foreach (var candidate in Candidates(set.SetName, first.Time.LocalDate))
        {
            if (batchTaken.Contains(candidate)) continue;
            var existing = lib.SetFolder(candidate).Where(l => !l.Members.IsEmpty).ToList();
            if (existing.Count == 0)
            {
                batchTaken.Add(candidate);
                return new SetPlacement(set.Id, candidate,
                    string.Equals(candidate, set.SetName, StringComparison.OrdinalIgnoreCase) ? SetResolution.Plain : SetResolution.DateSuffixed,
                    [.. card.Select(c => c.Name)]);
            }
            SetPlacement? resume = null;
            foreach (var l in existing)
            {
                var cardAllMatch = card.All(c => l.Members.Any(m => Same(m, c)));
                var existingAllMatch = l.Members.All(m => card.Any(c => Same(m, c)));
                if (cardAllMatch)                                             // equal, or card ⊂ existing
                {
                    batchTaken.Add(candidate);
                    return new SetPlacement(set.Id, candidate, SetResolution.Imported, []);
                }
                if (existingAllMatch)                                         // existing ⊂ card: copy the missing members
                    resume ??= new SetPlacement(set.Id, candidate, SetResolution.Resume,
                        [.. card.Where(c => !l.Members.Any(m => Same(m, c))).Select(c => c.Name)]);
            }
            if (resume is not null)
            {
                batchTaken.Add(candidate);
                return resume;
            }
            // clash → next candidate
        }
        throw new InvalidOperationException("Candidate sequence is infinite; unreachable.");
    }

    private static IEnumerable<string> Candidates(string plain, DateOnly firstFrameLocalDate)
    {
        yield return plain;
        var dated = $"{plain} {firstFrameLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        yield return dated;
        for (var n = 2; ; n++) yield return $"{dated} ({n.ToString(CultureInfo.InvariantCulture)})";
    }

    private static bool Same((string Member, long Size, DateTime MtimeUtc) existing, (string Name, long Size, DateTime MtimeUtc) card) =>
        string.Equals(existing.Member, card.Name, StringComparison.OrdinalIgnoreCase)
        && existing.Size == card.Size
        && (existing.MtimeUtc - card.MtimeUtc).Duration() <= MtimeTolerance;

    private static bool SameMemberList(LedgerSet k, List<(string Name, long Size, DateTime MtimeUtc)> card) =>
        k.Members.Length == card.Count
        && card.All(c => k.Members.Any(m => string.Equals(m.Member, c.Name, StringComparison.OrdinalIgnoreCase) && m.Size == c.Size));
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*SetFolderNamerTests"`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Naming/SetFolderNamer.cs tests/UasSort.Core.Tests/Naming/SetFolderNamerTests.cs
git commit -m "feat: set folder resolution (plain, date-suffixed, resume, imported)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.6: NewnessRules — photos and sets (seen, per-member set records, no-watermark rule)

**Files:**
- Create: `src/UasSort.Core/Planning/NewnessRules.Photo.cs`
- Test: `tests/UasSort.Core.Tests/Planning/PhotoNewnessTests.cs`

**Interfaces:**
- Consumes: `LibraryIndex.Match`, `LibraryIndex.Folders`, `LibraryFolder.DaysIn(string)` (Part 05); `LedgerSnapshot.Files/Decisions/Seen`; `SetPlacement` (06.5 output); `NewnessRules.FindConflict` (06.4).
- Produces: `NewnessRules.Photo(MediaUnit unit, ItemTime t, LibraryIndex lib, LedgerSnapshot ledger, SetPlacement? placement, IReadOnlySet<DateOnly> newVideoDays, DateTime? watermarkUtc) → Newness` (exact Ref §4.2 signature).

Rules (Ref §7.3, first that applies): 1 ledger `file` for every member (sets: records carrying `set == SetName`) → Imported; unrevoked `decision` for every member → Decided · 2 listed (sets: placement Imported) → Imported · 2b same NormName other size (flat photos only) → Conflict · 3 `seen` for every member → New(`SeenNotCopied`) · 4 no watermark or after it → New(`AfterWatermark`) · 5 `DroneClock*` time ≤ 75 min before it → New(`NearWatermark`) · 6 day has a New video → New(`DayHasNewVideos`) · 7 ProbablyImported with one of the two reasons.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Planning/PhotoNewnessTests.cs
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class PhotoNewnessTests
{
    private const string Zrel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private static readonly DateTime Watermark = new(2026, 9, 27, 18, 24, 16, DateTimeKind.Utc);
    private static readonly HashSet<DateOnly> NoNewDays = [];

    private static ItemTime T(DateTime utc, TimeSource src = TimeSource.DroneClockZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("America/Anchorage"));
        return new ItemTime(utc, src, "America/Anchorage", TzSource.Gps, DateOnly.FromDateTime(local), local);
    }

    private static Newness Of(ScanResult s, RawItem r, DateTime? watermark, IReadOnlySet<DateOnly>? newDays = null,
                              SetPlacement? placement = null, TimeSource src = TimeSource.DroneClockZone) =>
        NewnessRules.Photo(r.Unit, T(Clip.Utc(r.DroneStamp!.Value.ToString("yyyyMMddHHmmss")), src), s.Library, s.Ledger,
                           placement, newDays ?? NoNewDays, watermark);

    [Fact]
    public void Rule1_LedgerFileBeatsEverything()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var s = new PlanScenario().Card(d).LedgerFile(d, null).LedgerSeen(d, PlanScenario.NowUtc).Build();
        Assert.Equal(Evidence.LedgerVerified, Assert.IsType<Imported>(Of(s, d, Watermark)).By);
    }

    [Fact]
    public void Rule1_DecisionBeatsHeuristic_RevokeRestoresIt()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var decided = new PlanScenario().Card(d).LedgerDecision(d, DecisionKind.AssumedImported).Build();
        Assert.Equal(DecisionKind.AssumedImported, Assert.IsType<Decided>(Of(decided, d, Watermark)).Kind);
        var revoked = new PlanScenario().Card(d).Build();   // a revoked decision is absent from LedgerSnapshot.Decisions (Part 05)
        Assert.IsType<ProbablyImported>(Of(revoked, d, Watermark));
    }

    [Fact]
    public void Rule2_ListedInPhotoRoot_OrPreviousPhotoRoot_IsImported()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var s = new PlanScenario().Card(d).LibraryFile($@"Picture Offload\{d.Name}", d.Bytes, d.CardMtimeUtc).Build();
        Assert.Equal(Evidence.LibraryNameSize, Assert.IsType<Imported>(Of(s, d, Watermark)).By);
        var moved = new PlanScenario().Card(d).PreviousPhotoRootFile(@"D:\Old Offload", d.Name, d.Bytes, d.CardMtimeUtc).Build();
        Assert.IsType<Imported>(Of(moved, d, Watermark));      // photo root moved to D: (Ref §13 Newness)
    }

    [Fact]
    public void Rule2b_SameNameOtherSize_IsConflict()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil, withJpgTwin: true);
        var s = new PlanScenario().Card(d).LibraryFile($@"Picture Offload\{d.Name}", 1_234, d.CardMtimeUtc).Build();
        var c = Assert.IsType<Conflict>(Of(s, d, Watermark));
        Assert.Equal(1_234, c.ExistingSize);
    }

    [Fact] // two-run seen scenario: an unticked photo stays New across runs
    public void Rule3_SeenWithoutFile_IsNewNotCopied()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var seenAt = new DateTime(2026, 10, 4, 18, 0, 0, DateTimeKind.Utc);
        var s = new PlanScenario().Card(d).LedgerSeen(d, seenAt).Build();
        Assert.Equal(new IsNew(NewReason.SeenNotCopied, seenAt), Of(s, d, Watermark));
    }

    [Fact]
    public void Rule4_AfterWatermark_AndNoWatermark()
    {
        var d = Clip.Dng("20260930200000", 200, Sites.Zachar);
        var s = new PlanScenario().Card(d).Build();
        Assert.Equal(new IsNew(NewReason.AfterWatermark, null), Of(s, d, Watermark));
        var early = Clip.Dng("20260101120000", 1, Sites.Zachar);
        Assert.Equal(new IsNew(NewReason.AfterWatermark, null), Of(new PlanScenario().Card(early).Build(), early, null));
    }

    [Fact]
    public void Rule5_DroneClockWithin75MinBefore_IsNear_MtimeIsNot()
    {
        var d = Clip.Dng("20260927131000", 5, Sites.Zachar);        // 17:10Z, 74 min 16 s before the watermark
        var s = new PlanScenario().Card(d).Build();
        Assert.Equal(new IsNew(NewReason.NearWatermark, null), Of(s, d, Watermark));
        Assert.IsType<ProbablyImported>(Of(s, d, Watermark, src: TimeSource.Mtime));
        var far = Clip.Dng("20260927130800", 6, Sites.Zachar);      // 76 min 16 s before
        Assert.IsType<ProbablyImported>(Of(new PlanScenario().Card(far).Build(), far, Watermark));
    }

    [Fact]
    public void Rule6_DayHasNewVideos()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil);
        Assert.Equal(new IsNew(NewReason.DayHasNewVideos, null),
            Of(new PlanScenario().Card(d).Build(), d, Watermark, new HashSet<DateOnly> { new(2026, 8, 15) }));
    }

    [Fact]
    public void Rule7_ProbablyImported_BothReasons()
    {
        var z = Clip.Vid("20260927140127", 123, Sites.Zachar);
        var photoOnly = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var s = new PlanScenario().Card(photoOnly).Library(Zrel, z).Build();
        Assert.Equal("photo-only day before the last imported video (Sep 27)",
            Assert.IsType<ProbablyImported>(Of(s, photoOnly, Watermark)).Why);
        var sameDay = Clip.Dng("20260927100000", 7, Sites.Zachar);  // 14:00Z, Sep 27 AKDT, a day with library videos
        var s2 = new PlanScenario().Card(sameDay).Library(Zrel, z).Build();
        Assert.Equal("videos from this day are already in the library",
            Assert.IsType<ProbablyImported>(Of(s2, sameDay, Watermark)).Why);
    }

    [Fact] // sets: every member needs a record; a partial set falls through
    public void Sets_PartialDecisionOrSeen_FallsThrough_FullIsDecided()
    {
        var members = Enumerable.Range(1, 33)
            .Select(i => ($"PANO_{i:0000}.DNG", 12_000_000L + i, new DateTime(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc)))
            .ToArray();
        var pano = Clip.Set("001_0087", "2026-05-25 09:30:28", Sites.KodiakTown, members);
        var placement = new SetPlacement(pano.Id(), "001_0087", SetResolution.Plain, []);
        var t = T(new DateTime(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc));
        Newness Run(PlanScenario sc)
        {
            var scan = sc.Card(pano).Build();
            return NewnessRules.Photo(pano.Unit, t, scan.Library, scan.Ledger, placement, NoNewDays, null);
        }
        Assert.IsType<IsNew>(Run(new PlanScenario().LedgerDecision(pano, DecisionKind.AssumedImported, members: 5)));
        Assert.IsType<Decided>(Run(new PlanScenario().LedgerDecision(pano, DecisionKind.AssumedImported)));
        Assert.Equal(NewReason.AfterWatermark, Assert.IsType<IsNew>(Run(new PlanScenario().LedgerSeen(pano, PlanScenario.NowUtc, members: 5))).Why);
        Assert.Equal(NewReason.SeenNotCopied, Assert.IsType<IsNew>(Run(new PlanScenario().LedgerSeen(pano, PlanScenario.NowUtc))).Why);
    }

    [Fact]
    public void Sets_ImportedPlacement_IsImported()
    {
        var pano = Clip.Set("001_0087", "2026-05-25 09:30:28", Sites.KodiakTown,
            ("PANO_0001.DNG", 13_751_808, new DateTime(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc)));
        var scan = new PlanScenario().Card(pano).Build();
        var n = NewnessRules.Photo(pano.Unit, T(new DateTime(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc)), scan.Library, scan.Ledger,
            new SetPlacement(pano.Id(), "001_0087", SetResolution.Imported, []), NoNewDays, null);
        Assert.IsType<Imported>(n);
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PhotoNewnessTests"`
Expected: build error CS0117 (`NewnessRules` has no `Photo`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Planning/NewnessRules.Photo.cs
using UasSort.Core.Library;

namespace UasSort.Core.Planning;

public static partial class NewnessRules
{
    public static readonly TimeSpan NearWatermarkWindow = TimeSpan.FromMinutes(75);

    public static Newness Photo(MediaUnit unit, ItemTime t, LibraryIndex lib, LedgerSnapshot ledger, SetPlacement? placement,
                                IReadOnlySet<DateOnly> newVideoDays, DateTime? watermarkUtc)
    {
        var setName = unit is SetUnit su ? su.SetName : null;
        IReadOnlyList<CardEntry> files = unit switch
        {
            PhotoUnit p => [p.Primary],
            SetUnit s => s.Members,
            VideoUnit v => [v.Mp4],
        };
        var keys = files.Select(f => PlanKeys.Key(PlanKeys.FileName(f.RelPath), f.Size)).ToList();
        bool InSet(string? recordSet) => setName is null || string.Equals(recordSet, setName, StringComparison.OrdinalIgnoreCase);

        // 1. ledger file for every member, or an unrevoked decision for every member
        var recs = keys.Select(k => ledger.Files.TryGetValue(k, out var f) && InSet(f.Set) ? f : null).ToList();
        if (recs.All(r => r is not null))
        {
            var by = recs.Any(r => r!.Verify == VerifyKind.NameSize) ? Evidence.LedgerNameSize : Evidence.LedgerVerified;
            return new Imported(by, null, $"copied on {PlanText.ShortDate(DateOnly.FromDateTime(recs.Max(r => r!.AtUtc)))}");
        }
        var decs = keys.Select(k => ledger.Decisions.TryGetValue(k, out var d) && InSet(d.Set) ? d : null).ToList();
        if (decs.All(d => d is not null))
        {
            var last = decs.OrderByDescending(d => d!.AtUtc).First()!;
            return new Decided(last.Kind, last.AtUtc, last.Machine);
        }

        // 2. listed (sets: the set-folder rule), 2b. conflict (flat photos)
        if (unit is SetUnit)
        {
            if (placement is { Resolution: SetResolution.Imported })
                return new Imported(Evidence.LibraryNameSize, null, $"in library: {placement.FolderName}");
        }
        else
        {
            var listed = lib.Match(keys[0]);
            if (!listed.IsEmpty) return new Imported(Evidence.LibraryNameSize, null, $"in library: {listed[0].FullPath}");
            if (FindConflict(keys[0], lib, ledger) is { } c) return c;
        }

        // 3. seen, without file or decision
        if (keys.All(ledger.Seen.ContainsKey))
            return new IsNew(NewReason.SeenNotCopied, keys.Max(k => ledger.Seen[k]));

        // 4. after the watermark, or no watermark
        if (watermarkUtc is not { } wm || t.CaptureUtc > wm)
            return new IsNew(NewReason.AfterWatermark, null);

        // 5. drone-clock time within 75 min before the watermark
        if (IsDroneClock(t.Source) && wm - t.CaptureUtc <= NearWatermarkWindow)
            return new IsNew(NewReason.NearWatermark, null);

        // 6. the day has a New video
        if (newVideoDays.Contains(t.LocalDate))
            return new IsNew(NewReason.DayHasNewVideos, null);

        // 7. probably imported
        var hasLibraryVideos = lib.Folders.Any(f => f.DaysIn(t.TzId).Contains(t.LocalDate));
        var wmDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(wm, PlanText.Zone(t.TzId)));
        return new ProbablyImported(hasLibraryVideos
            ? "videos from this day are already in the library"
            : $"photo-only day before the last imported video ({PlanText.ShortDate(wmDay)})");
    }

    private static bool IsDroneClock(TimeSource s) =>
        s is TimeSource.DroneClockSiteLocal or TimeSource.DroneClockZone or TimeSource.DroneClockSample or TimeSource.DroneClockSetting;
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PhotoNewnessTests"`
Expected: PASS (11 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Planning/NewnessRules.Photo.cs tests/UasSort.Core.Tests/Planning/PhotoNewnessTests.cs
git commit -m "feat: photo and set newness rules with seen, per-member set records and no-watermark rule

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.7: Clusterer — auto clustering, structural edits, caused boundaries

**Files:**
- Create: `src/UasSort.Core/Planning/Clusterer.cs`
- Create: `tests/UasSort.Testing/Planning/ItemFactory.cs`
- Test: `tests/UasSort.Core.Tests/Planning/ClustererTests.cs`

**Interfaces:**
- Consumes: `Item`, `Tuning`, `GroupDraft`, `ClusterResult`, `Boundary`, `BoundaryCause`, `GroupId`, `SessionKey.SameSession`, structural `PlanEdit`s (Part 02); `PlanningGeo`, `PlanEditRefs` (06.1).
- Produces:
  - `static class Clusterer { IComparer<Item> Order; ClusterResult Cluster(IReadOnlyList<Item> videos, Tuning t, IReadOnlyList<PlanEdit> structuralEdits, out int missingEdits); }` plus internal helpers `FolderOf`, `SameFolder`, `TimeSplit`, `WallOf` used by FolderDecider/Planner/EditValidator.
  - `GroupDraft.UserSplitNeighbour` is set to the **earlier** neighbour's `GroupId` when the boundary before the group is `UserSplit` (the later neighbour across a UserSplit is found as the draft whose `UserSplitNeighbour` equals this group's Id).
  - Testing: `static class ItemFactory { Item Of(RawItem r, Newness? n = null, SessionKey? session = null, string tz = "America/Anchorage"); LibraryFolderRef Folder(string rel); }`

Algorithm (Ref §8.2, §8.9 step 2): the spike's sequential pass in `(CaptureUtc, Id)` order (time split with the H guard; library-folder wall; distance split against the mean-unit-vector centroid, skipped when x shares a session with a GPS-bearing member; trailing no-GPS items re-homed by session, then time); then the structural edits applied to that partition in log order (drop and count missing-item edits; inactive edits change nothing and are not counted); then boundaries between timeline-adjacent groups, each with the first cause that applies: UserSplit (an active SplitBefore names R's anchor) → LibraryFolder → DayGap → Distance → UserSplit.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Testing/Planning/ItemFactory.cs

namespace UasSort.Testing.Planning;

/// <summary>Builds Items directly (no TimeResolver) for component tests: UTC from mvhd (else stamp + 4 h), zone as given.</summary>
public static class ItemFactory
{
    public static Item Of(RawItem r, Newness? n = null, SessionKey? session = null, string tz = "America/Anchorage")
    {
        var utc = r.Mp4?.MvhdUtc ?? DateTime.SpecifyKind(r.DroneStamp!.Value.AddHours(4), DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(tz));
        GpsFix? gps = null;
        if (r.Mp4 is { } m && m.First is GpsFix f) gps = f;
        else if (r.Still is { } st && st.Gps is GpsFix g) gps = g;
        var time = new ItemTime(utc, r.Mp4?.MvhdUtc is null ? TimeSource.DroneClockZone : TimeSource.Mvhd, tz, TzSource.Gps,
                                DateOnly.FromDateTime(local), local);
        return new Item(r, time, gps, session, gps is null ? ItemFlags.NoGps : ItemFlags.None, n ?? new IsNew(NewReason.NoMatch, null));
    }

    public static LibraryFolderRef Folder(string rel)
    {
        var leaf = rel[(rel.LastIndexOf('\\') + 1)..];
        return new LibraryFolderRef($@"{PlanScenario.VideoRoot}\{rel}", DateOnly.ParseExact(leaf[..10], "yyyy-MM-dd"), leaf.Length > 11 ? leaf[11..] : "");
    }

    public static Imported ImportedInto(string rel) => new(Evidence.LibraryNameSize, Folder(rel), "test");
}
```

```csharp
// tests/UasSort.Core.Tests/Planning/ClustererTests.cs
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class ClustererTests
{
    private static readonly Tuning R50 = new(50, 1);
    private static readonly Tuning R25 = new(25, 1);

    private static ClusterResult Run(Tuning t, IReadOnlyList<PlanEdit> edits, params Item[] items) => Clusterer.Cluster(items, t, edits, out _);
    private static ClusterResult Run(Tuning t, params Item[] items) => Run(t, [], items);
    private static string[][] Ids(ClusterResult r) =>
        r.Groups.Select(g => g.Videos.Select(v => v.Raw.Name[4..18]).ToArray()).ToArray();

    private static Item C117 => ItemFactory.Of(Clip.Vid("20260725232655", 117, Sites.Council));
    private static Item A1 => ItemFactory.Of(Clip.Vid("20260726235645", 1, Sites.Anvil));

    [Fact]
    public void TwoDayGap_SplitsWithDayGapCause()
    {
        var r = Run(R50, ItemFactory.Of(Clip.Vid("20260722230000", 1, Sites.Anvil)), ItemFactory.Of(Clip.Vid("20260725230000", 2, Sites.Anvil)));
        Assert.Equal(2, r.Groups.Length);
        var b = Assert.Single(r.Boundaries);
        Assert.Equal((BoundaryCause.DayGap, 3), (b.Cause, b.DayGap));
    }

    [Fact]
    public void HoursGuard_KeepsMidnightFlightTogetherAtG0()
        => Assert.Single(Run(new Tuning(50, 0), ItemFactory.Of(Clip.Vid("20260726035000", 1, Sites.Anvil)),
                                                 ItemFactory.Of(Clip.Vid("20260726041000", 2, Sites.Anvil))).Groups);

    [Fact]
    public void Distance_DependsOnR()
    {
        Assert.Single(Run(new Tuning(40, 1), C117, A1).Groups);
        var r = Run(R25, C117, A1);
        Assert.Equal(BoundaryCause.Distance, Assert.Single(r.Boundaries).Cause);
        Assert.Equal(33.9, r.Boundaries[0].Jump!.Value.Miles, 1);
    }

    [Fact]
    public void NoGps_GoesToNearerNeighbourInTime()
    {
        var nogps = ItemFactory.Of(Clip.Vid("20260726234000", 99));
        Assert.Equal(new[] { new[] { "20260725232655" }, new[] { "20260726234000", "20260726235645" } }, Ids(Run(R25, C117, nogps, A1)));
    }

    [Fact]
    public void NoGps_SessionOfLastGpsItemBeatsTime()
    {
        var s = new SessionKey(Clip.Serial, new DateTime(2026, 7, 26, 3, 20, 0, DateTimeKind.Utc));
        var c = ItemFactory.Of(Clip.Vid("20260725232655", 117, Sites.Council), session: s);
        var nogps = ItemFactory.Of(Clip.Vid("20260726234000", 99), session: s);
        Assert.Equal(new[] { new[] { "20260725232655", "20260726234000" }, new[] { "20260726235645" } }, Ids(Run(R25, c, nogps, A1)));
    }

    [Theory] // SessionKey tolerance (Ref §13): 0.8 s apart = same session, 3 s = different; different serials never
    [InlineData("TESTSERIAL", 0.8, 1)]
    [InlineData("TESTSERIAL", 3.0, 2)]
    [InlineData("OTHERSERIAL", 0.0, 2)]
    public void SessionTolerance_BlocksDistanceSplitOnlyWithinTwoSeconds(string serial2, double deltaS, int groups)
    {
        var t0 = new DateTime(2026, 9, 27, 17, 55, 0, DateTimeKind.Utc);
        var a = ItemFactory.Of(Clip.Vid("20260927140000", 1, Sites.Zachar), session: new SessionKey(Clip.Serial, t0));
        var b = ItemFactory.Of(Clip.Vid("20260927142000", 2, Sites.KodiakTown), session: new SessionKey(serial2, t0.AddSeconds(deltaS)));
        Assert.Equal(groups, Run(R50, a, b).Groups.Length);
    }

    [Fact]
    public void LibraryFolderWall_SplitsWithCause_AndSetsWall()
    {
        var am = ItemFactory.Of(Clip.Vid("20260801200000", 1, Sites.Anvil), ItemFactory.ImportedInto(@"2026\2026-08\2026-08-01 Anvil AM"));
        var nw = ItemFactory.Of(Clip.Vid("20260801202000", 9, Sites.Anvil));
        var pm = ItemFactory.Of(Clip.Vid("20260802200000", 3, Sites.Anvil), ItemFactory.ImportedInto(@"2026\2026-08\2026-08-02 Anvil PM"));
        var r = Run(R50, am, nw, pm);
        Assert.Equal(2, r.Groups.Length);
        Assert.Equal(BoundaryCause.LibraryFolder, r.Boundaries[0].Cause);
        Assert.Equal("Anvil AM", r.Groups[0].Wall!.Description);
        Assert.Equal("Anvil PM", r.Groups[1].Wall!.Description);
    }

    [Fact]
    public void SplitBefore_MidGroup_IsUserSplitWithNeighbour()
    {
        var c118 = ItemFactory.Of(Clip.Vid("20260726022937", 118, Sites.Council));
        var r = Run(R50, [new SplitBefore(A1.Raw.Unit.Id)], C117, c118, A1);
        Assert.Equal(2, r.Groups.Length);
        Assert.Equal(BoundaryCause.UserSplit, r.Boundaries[0].Cause);
        Assert.Equal(r.Groups[0].Id, r.Groups[1].UserSplitNeighbour);
        Assert.Null(r.Groups[0].UserSplitNeighbour);
    }

    [Fact] // §8.9 worked example: the chip still reads "split by you" where R 25 would split anyway
    public void SplitBefore_AtAutoBoundary_OverridesCause()
    {
        IReadOnlyList<PlanEdit> edits = [new SplitBefore(A1.Raw.Unit.Id)];
        foreach (var t in new[] { R50, R25, R50 })
        {
            var r = Run(t, edits, C117, A1);
            Assert.Equal(BoundaryCause.UserSplit, Assert.Single(r.Boundaries).Cause);
        }
    }

    [Fact] // §8.9 worked example: sites A, B, C 8 mi apart in a line
    public void Merge_AbsorbsGroupsBetween()
    {
        var a = ItemFactory.Of(Clip.Vid("20260601100000", 1, new GeoPoint(60, -150)));
        var b = ItemFactory.Of(Clip.Vid("20260601110000", 2, new GeoPoint(60.11578526651503, -150)));
        var c = ItemFactory.Of(Clip.Vid("20260601120000", 3, new GeoPoint(60.23157053303006, -150)));
        Assert.Equal(new[] { new[] { "20260601100000", "20260601110000" }, new[] { "20260601120000" } }, Ids(Run(new Tuning(10, 1), a, b, c)));
        Assert.Equal(3, Run(new Tuning(5, 1), a, b, c).Groups.Length);
        IReadOnlyList<PlanEdit> merge = [new Merge(a.Raw.Unit.Id, c.Raw.Unit.Id)];
        Assert.Single(Run(new Tuning(10, 1), merge, a, b, c).Groups);
        Assert.Single(Run(new Tuning(5, 1), merge, a, b, c).Groups);
    }

    [Fact]
    public void MergeAcrossWalls_IsInactive_NotDropped()
    {
        var am = ItemFactory.Of(Clip.Vid("20260801200000", 1, Sites.Anvil), ItemFactory.ImportedInto(@"2026\2026-08\2026-08-01 Anvil AM"));
        var pm = ItemFactory.Of(Clip.Vid("20260802200000", 3, Sites.Anvil), ItemFactory.ImportedInto(@"2026\2026-08\2026-08-02 Anvil PM"));
        var r = Clusterer.Cluster([am, pm], R50, [new Merge(am.Raw.Unit.Id, pm.Raw.Unit.Id)], out var missing);
        Assert.Equal(2, r.Groups.Length);
        Assert.Equal(0, missing);
    }

    [Fact] // §8.9 worked example: MoveToNewGroup leaves a non-contiguous group ordered by its anchor
    public void MoveToNewGroup_MiddleClip()
    {
        var v = Enumerable.Range(0, 3).Select(i => ItemFactory.Of(Clip.Vid($"2026092714{i:00}00", i + 1, Sites.Zachar))).ToArray();
        var r = Run(R50, [new MoveToNewGroup([v[1].Raw.Unit.Id])], v);
        Assert.Equal(2, r.Groups.Length);
        Assert.Equal(v[0].Raw.Unit.Id, r.Groups[0].Id.Anchor);
        Assert.Equal(new[] { v[0].Raw.Unit.Id, v[2].Raw.Unit.Id }, r.Groups[0].Videos.Select(x => x.Raw.Unit.Id));
        Assert.Equal(v[1].Raw.Unit.Id, r.Groups[1].Id.Anchor);
        Assert.Equal(BoundaryCause.UserSplit, r.Boundaries[0].Cause);
    }

    [Fact]
    public void MoveToGroup_TargetAmongItems_IsInactive_MissingItemsAreCounted()
    {
        var v = Enumerable.Range(0, 2).Select(i => ItemFactory.Of(Clip.Vid($"2026092714{i:00}00", i + 1, Sites.Zachar))).ToArray();
        var gone = new ItemId("DCIM/DJI_001/DJI_20260101000000_0999_D.MP4");
        var r = Clusterer.Cluster(v, R50,
            [new MoveToGroup([v[0].Raw.Unit.Id], v[0].Raw.Unit.Id), new SplitBefore(gone), new Merge(gone, v[0].Raw.Unit.Id)], out var missing);
        Assert.Single(r.Groups);
        Assert.Equal(2, missing);
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*ClustererTests"`
Expected: build error CS0103 (`Clusterer` does not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Planning/Clusterer.cs
using System.Collections.Immutable;

namespace UasSort.Core.Planning;

/// <summary>Ref §8.2 auto clustering + §8.9 structural edits + caused boundaries. Pure.</summary>
public static class Clusterer
{
    public static readonly IComparer<Item> Order = Comparer<Item>.Create((a, b) =>
    {
        var c = a.Time.CaptureUtc.CompareTo(b.Time.CaptureUtc);
        return c != 0 ? c : string.CompareOrdinal(a.Raw.Unit.Id.CardRelPath, b.Raw.Unit.Id.CardRelPath);
    });

    public static ClusterResult Cluster(IReadOnlyList<Item> videos, Tuning t, IReadOnlyList<PlanEdit> structuralEdits, out int missingEdits)
    {
        var ordered = videos.ToList();
        ordered.Sort(Order);
        var parts = AutoPartition(ordered, t);
        var userSplit = new HashSet<ItemId>();
        missingEdits = ApplyEdits(parts, ordered, structuralEdits, userSplit);
        return Build(parts, userSplit, t);
    }

    internal static LibraryFolderRef? FolderOf(Item i) => i.Newness is Imported { Folder: { } f } ? f : null;

    internal static bool SameFolder(LibraryFolderRef a, LibraryFolderRef b) =>
        string.Equals(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);

    internal static bool TimeSplit(Item prev, Item x, Tuning t) =>
        x.Time.LocalDate.DayNumber - prev.Time.LocalDate.DayNumber > t.GapDays
        && x.Time.CaptureUtc - prev.Time.CaptureUtc > PlanningGeo.HoursGuard;

    internal static LibraryFolderRef? WallOf(IEnumerable<Item> items)
    {
        foreach (var i in items) if (FolderOf(i) is { } f) return f;
        return null;
    }

    internal static GeoPoint? CentroidOf(IEnumerable<Item> items) =>
        PlanningGeo.Centroid(items.Where(i => i.Gps is not null).Select(i => i.Gps!.Point));

    private static bool SameSession(Item a, Item b) => a.Session is { } sa && b.Session is { } sb && sa.SameSession(sb);

    private sealed class Acc
    {
        private readonly List<GeoPoint> _points = [];
        private GeoPoint? _cached;
        public void Add(Item i) { if (i.Gps is { } g) { _points.Add(g.Point); _cached = null; } }
        public GeoPoint? Get() => _cached ??= PlanningGeo.Centroid(_points);
    }

    private static List<List<Item>> AutoPartition(List<Item> ordered, Tuning t)
    {
        var radiusM = Distance.FromMiles(t.RadiusMiles).Meters;
        var groups = new List<List<Item>>();
        List<Item>? cur = null;
        var acc = new Acc();
        var tail = new List<Item>();                 // no-GPS items after the group's last GPS item
        Item? lastGps = null;

        void Start(Item x)
        {
            cur = [x];
            groups.Add(cur);
            acc = new Acc();
            acc.Add(x);
            tail = x.Gps is null ? [x] : [];
            lastGps = x.Gps is null ? null : x;
        }

        foreach (var x in ordered)
        {
            if (cur is null) { Start(x); continue; }
            var prev = cur[^1];
            if (TimeSplit(prev, x, t)) { Start(x); continue; }
            if (FolderOf(x) is { } fx && cur.Any(i => FolderOf(i) is { } f && !SameFolder(f, fx))) { Start(x); continue; }
            if (x.Gps is not null && acc.Get() is { } c
                && !cur.Any(m => m.Gps is not null && SameSession(m, x))
                && PlanningGeo.Haversine(x.Gps.Point, c).Meters > radiusM)
            {
                var moved = new List<Item>();
                foreach (var ti in tail)
                {
                    bool move;
                    if (SameSession(ti, x)) move = true;
                    else if (lastGps is not null && SameSession(ti, lastGps)) move = false;
                    else move = lastGps is null || x.Time.CaptureUtc - ti.Time.CaptureUtc < ti.Time.CaptureUtc - lastGps.Time.CaptureUtc;
                    if (move) moved.Add(ti);
                }
                foreach (var m in moved) cur.Remove(m);
                if (cur.Count == 0) groups.Remove(cur);
                var next = new List<Item>(moved) { x };
                next.Sort(Order);
                cur = next;
                groups.Add(cur);
                acc = new Acc();
                acc.Add(x);                          // moved items have no GPS
                tail = [];
                lastGps = x;
                continue;
            }
            cur.Add(x);
            acc.Add(x);
            if (x.Gps is not null) { tail = []; lastGps = x; }
            else tail.Add(x);
        }
        return groups;
    }

    private static int ApplyEdits(List<List<Item>> parts, List<Item> ordered, IReadOnlyList<PlanEdit> edits, HashSet<ItemId> userSplit)
    {
        var byId = ordered.ToDictionary(i => i.Raw.Unit.Id);
        var missing = 0;
        int Find(ItemId id) => parts.FindIndex(p => p.Exists(i => i.Raw.Unit.Id == id));

        foreach (var e in edits)
        {
            if (!PlanEditRefs.IsStructural(e)) continue;
            if (PlanEditRefs.Referenced(e).Any(r => !byId.ContainsKey(r))) { missing++; continue; }
            switch (e)
            {
                case SplitBefore s:
                {
                    var x = byId[s.First];
                    var g = parts[Find(s.First)];
                    var after = g.Where(i => Order.Compare(i, x) >= 0).ToList();
                    if (after.Count < g.Count)
                    {
                        g.RemoveAll(after.Contains);
                        parts.Add(after);
                    }
                    userSplit.Add(s.First);
                    break;
                }
                case Merge m:
                {
                    int a = Find(m.InA), b = Find(m.InB);
                    if (a == b) break;
                    int lo = Math.Min(a, b), hi = Math.Max(a, b);
                    var range = parts.GetRange(lo, hi - lo + 1);
                    var walls = range.Select(WallOf).Where(w => w is not null)
                                     .Select(w => w!.FullPath.ToUpperInvariant()).Distinct().Count();
                    if (walls > 1) break;                                  // inactive: MergeAcrossLibraryFolders
                    foreach (var p in range.Skip(1)) userSplit.Remove(p[0].Raw.Unit.Id);
                    var merged = range.SelectMany(p => p).ToList();
                    parts.RemoveRange(lo, hi - lo + 1);
                    parts.Insert(lo, merged);
                    break;
                }
                case MoveToNewGroup mv:
                {
                    var items = mv.Items.Distinct().Select(id => byId[id]).ToList();
                    if (items.Count == 0 || items.Exists(i => i.Newness is Imported)) break;
                    foreach (var p in parts) p.RemoveAll(items.Contains);
                    parts.Add(items);
                    break;
                }
                case MoveToGroup mt:
                {
                    if (mt.Items.Contains(mt.InTarget)) break;
                    var items = mt.Items.Distinct().Select(id => byId[id]).ToList();
                    if (items.Count == 0 || items.Exists(i => i.Newness is Imported)) break;
                    var target = parts[Find(mt.InTarget)];
                    foreach (var p in parts) p.RemoveAll(items.Contains);
                    target.AddRange(items);
                    break;
                }
            }
            Normalize(parts);
        }
        return missing;
    }

    private static void Normalize(List<List<Item>> parts)
    {
        parts.RemoveAll(p => p.Count == 0);
        foreach (var p in parts) p.Sort(Order);
        parts.Sort((a, b) => Order.Compare(a[0], b[0]));
    }

    private static ClusterResult Build(List<List<Item>> parts, HashSet<ItemId> userSplit, Tuning t)
    {
        Normalize(parts);
        var radiusM = Distance.FromMiles(t.RadiusMiles).Meters;
        var drafts = ImmutableArray.CreateBuilder<GroupDraft>(parts.Count);
        var bounds = ImmutableArray.CreateBuilder<Boundary>(Math.Max(0, parts.Count - 1));
        GeoPoint? prevC = null;
        for (var i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            var c = CentroidOf(p);
            var id = new GroupId(p[0].Raw.Unit.Id);
            GroupId? neighbour = null;
            if (i > 0)
            {
                var l = parts[i - 1];
                var cause = CauseOf(l, p, prevC, userSplit, t, radiusM);
                Distance? jump = prevC is { } a && c is { } b ? PlanningGeo.Haversine(a, b) : null;
                bounds.Add(new Boundary(drafts[i - 1].Id, id, cause, jump, p[0].Time.CaptureUtc - l[^1].Time.CaptureUtc,
                                        p[0].Time.LocalDate.DayNumber - l[^1].Time.LocalDate.DayNumber));
                if (cause == BoundaryCause.UserSplit) neighbour = drafts[i - 1].Id;
            }
            drafts.Add(new GroupDraft(id, [.. p], c, p.Min(x => x.Time.LocalDate), p.Max(x => x.Time.LocalDate), WallOf(p), neighbour));
            prevC = c;
        }
        return new ClusterResult(drafts.MoveToImmutable(), bounds.ToImmutable());
    }

    private static BoundaryCause CauseOf(List<Item> l, List<Item> r, GeoPoint? lc, HashSet<ItemId> userSplit, Tuning t, double radiusM)
    {
        if (userSplit.Contains(r[0].Raw.Unit.Id)) return BoundaryCause.UserSplit;
        if (WallOf(l) is { } wl && WallOf(r) is { } wr && !SameFolder(wl, wr)) return BoundaryCause.LibraryFolder;
        if (TimeSplit(l[^1], r[0], t)) return BoundaryCause.DayGap;
        if (r.Find(i => i.Gps is not null) is { } g && lc is { } c && PlanningGeo.Haversine(g.Gps!.Point, c).Meters > radiusM)
            return BoundaryCause.Distance;
        return BoundaryCause.UserSplit;
    }
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*ClustererTests"`
Expected: PASS (15 tests incl. theory rows).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Planning/Clusterer.cs tests/UasSort.Testing/Planning/ItemFactory.cs tests/UasSort.Core.Tests/Planning/ClustererTests.cs
git commit -m "feat: clusterer with walls, session-aware distance splits, structural edits and caused boundaries

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.8: DaySplitFinder

**Files:**
- Create: `src/UasSort.Core/Planning/DaySplitFinder.cs`
- Test: `tests/UasSort.Core.Tests/Planning/DaySplitFinderTests.cs`

**Interfaces:**
- Consumes: `Item`, `DaySplit` (Part 02); `Clusterer.Order`, `PlanningGeo` (06.1, 06.7).
- Produces: `static class DaySplitFinder { ImmutableArray<DaySplit> Find(IReadOnlyList<Item> groupVideos); }` — one `DaySplit` per change of local date between consecutive members in `(CaptureUtc, Id)` order; `Apart` = distance between the two days' GPS centroids (null if either day has no GPS); `Gap` = time between the two clips; `Emphasised` = `Apart ≥ 10 mi` (Ref §8.3).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Planning/DaySplitFinderTests.cs
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class DaySplitFinderTests
{
    [Fact]
    public void CouncilAnvil_OneEmphasisedSplitBeforeFirstAnvilClip()
    {
        var items = new[]
        {
            ItemFactory.Of(Clip.Vid("20260725232655", 117, Sites.Council)),
            ItemFactory.Of(Clip.Vid("20260726022937", 118, Sites.Council)),
            ItemFactory.Of(Clip.Vid("20260726235645", 1, Sites.Anvil)),
            ItemFactory.Of(Clip.Vid("20260727000012", 2, Sites.Anvil)),
        };
        var s = Assert.Single(DaySplitFinder.Find(items));
        Assert.Equal(items[2].Raw.Unit.Id, s.FirstOfDay);
        Assert.Equal((new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26)), (s.From, s.To));
        Assert.Equal(33.9, s.Apart!.Value.Miles, 1);
        Assert.True(s.Emphasised);
        Assert.Equal(new TimeSpan(21, 27, 8), s.Gap);
    }

    [Fact]
    public void KodiakMultiDay_SplitsAreNotEmphasised()
    {
        var items = new[]
        {
            ItemFactory.Of(Clip.Vid("20260523015251", 40, Sites.KodiakTown)),
            ItemFactory.Of(Clip.Vid("20260523201928", 52, Sites.KodiakTown)),
            ItemFactory.Of(Clip.Vid("20260524190521", 64, new GeoPoint(57.75, -152.50))),
            ItemFactory.Of(Clip.Vid("20260525092718", 85, Sites.KodiakTown)),
        };
        var splits = DaySplitFinder.Find(items);
        Assert.Equal(3, splits.Length);
        Assert.All(splits, s => Assert.False(s.Emphasised));
    }

    [Fact]
    public void DayWithoutGps_HasNoDistance()
    {
        var items = new[]
        {
            ItemFactory.Of(Clip.Vid("20260725232655", 1)),
            ItemFactory.Of(Clip.Vid("20260726235645", 2, Sites.Anvil)),
        };
        var s = Assert.Single(DaySplitFinder.Find(items));
        Assert.Null(s.Apart);
        Assert.False(s.Emphasised);
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*DaySplitFinderTests"`
Expected: build error CS0103 (`DaySplitFinder` does not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Planning/DaySplitFinder.cs
using System.Collections.Immutable;

namespace UasSort.Core.Planning;

/// <summary>Day-split suggestions inside a group (Ref §8.3).</summary>
public static class DaySplitFinder
{
    public static ImmutableArray<DaySplit> Find(IReadOnlyList<Item> groupVideos)
    {
        var v = groupVideos.ToList();
        v.Sort(Clusterer.Order);
        var dayCentroids = v.Where(i => i.Gps is not null)
                            .GroupBy(i => i.Time.LocalDate)
                            .ToDictionary(g => g.Key, g => PlanningGeo.Centroid(g.Select(i => i.Gps!.Point)));
        var result = ImmutableArray.CreateBuilder<DaySplit>();
        for (var i = 1; i < v.Count; i++)
        {
            DateOnly from = v[i - 1].Time.LocalDate, to = v[i].Time.LocalDate;
            if (from == to) continue;
            Distance? apart = dayCentroids.GetValueOrDefault(from) is { } a && dayCentroids.GetValueOrDefault(to) is { } b
                ? PlanningGeo.Haversine(a, b) : null;
            result.Add(new DaySplit(v[i].Raw.Unit.Id, from, to, apart, v[i].Time.CaptureUtc - v[i - 1].Time.CaptureUtc,
                                    apart is { } d && d.Miles >= PlanningGeo.NearMiles));
        }
        return result.ToImmutable();
    }
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*DaySplitFinderTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Planning/DaySplitFinder.cs tests/UasSort.Core.Tests/Planning/DaySplitFinderTests.cs
git commit -m "feat: day-split suggestions with 10 mi emphasis

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.9: FolderDecider — New subset, cross-day rules, two-pass UserSplit

**Files:**
- Create: `src/UasSort.Core/Planning/FolderDecider.cs`
- Test: `tests/UasSort.Core.Tests/Planning/FolderDeciderTests.cs`

**Interfaces:**
- Consumes: `LibraryIndex.Folders`, `LibraryFolder.DaysIn`, `LibraryFolder.Centroid` (Part 05); `GroupDraft`, `GroupTarget` cases, `CrossDayHint`, `Confidence` (Part 02); `Clusterer` helpers (06.7); `PlanText`, `PlanningGeo` (06.1).
- Produces (`UasSort.Core.Planning`):
  - `sealed class FolderDecider { FolderDecider(LibraryIndex lib, IReadOnlyList<GroupDraft> all, Tuning t); static GroupTarget Decide(GroupDraft g, IReadOnlyList<GroupDraft> all, IReadOnlySet<ItemId> included, LibraryIndex lib, Tuning t, IReadOnlyDictionary<GroupId, GroupTarget>? pass1); GroupTarget Decide(GroupDraft g, IReadOnlySet<ItemId> included, IReadOnlyDictionary<GroupId, GroupTarget>? pass1); IReadOnlyList<LibraryFolder> AppendCandidates(GroupDraft g, int max); ItemId? SplitPoint(GroupDraft g, LibraryFolderRef f, IReadOnlySet<ItemId> included); ItemId? NewBeforeWallSplitPoint(GroupDraft g, IReadOnlySet<ItemId> included); bool BordersUserSplit(GroupDraft g); static IReadOnlyList<Item> Judged(GroupDraft g, IReadOnlySet<ItemId> included); static LibraryFolderRef? TargetFolder(GroupTarget t); }`
  - A decided `NewFolder` carries `RelPath = ""`; the Planner (06.12) fills the named path.

Decisions (Ref §8.5, main spec §5.6; first row wins; "New" = included `IsNew` + included `Conflict`): no New → AlreadyImported(wall) / NothingToCopy(summary) · wall + first New before `F.NameDate` → NewFolder · wall + a New item on F's days → Append High · wall, none on F's days → Append Medium + `SplitBefore(split point)` hint · no wall: date-eligible folders (`F.NameDate ≤ start ≤ F.End + G`), both centroids ≤ R: overlap → High "same dates", adjacent &lt; 10 mi → High "next day", adjacent 10 mi–R → Medium + `Retarget(NewFolderTarget)` hint; one centroid unknown + overlap → Medium "same dates, location unknown"; ranking by date gap then distance · pass 2 (only groups bordering a UserSplit) excludes F when it is the Wall of a neighbour across a UserSplit or the pass-1 target of the earlier neighbour across one — **never the group's own wall** (see Spec gaps) · otherwise NewFolder. F's centroid: `LibraryFolder.Centroid` (ledger), else the card leftovers imported into F across all groups.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Planning/FolderDeciderTests.cs
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class FolderDeciderTests
{
    private const string Zrel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private const string CouncilRel = @"2026\2026-07\2026-07-25 Council Road";
    private static readonly Tuning R50 = new(50, 1);
    private static readonly RawItem[] Z =
    [
        Clip.Vid("20260927140127", 123, Sites.Zachar), Clip.Vid("20260927140144", 124, Sites.Zachar), Clip.Vid("20260927142416", 148, Sites.Zachar),
    ];

    private sealed record Setup(ScanResult Scan, ClusterResult Clusters, HashSet<ItemId> Included, FolderDecider Decider)
    {
        public GroupTarget Pass1(int i) => Decider.Decide(Clusters.Groups[i], Included, null);
        public GroupTarget Final(int i)
        {
            var p1 = Clusters.Groups.ToDictionary(g => g.Id, g => Decider.Decide(g, Included, null));
            var g = Clusters.Groups[i];
            return Decider.BordersUserSplit(g) ? Decider.Decide(g, Included, p1) : p1[g.Id];
        }
    }

    private static Setup Make(PlanScenario s, IReadOnlyList<PlanEdit>? edits = null, IEnumerable<ItemId>? alsoInclude = null, Tuning? t = null)
    {
        var scan = s.Build();
        var items = scan.Raw.Where(r => r.Unit is VideoUnit)
            .Select(r => ItemFactory.Of(r, NewnessRules.Video((VideoUnit)r.Unit, scan.Library, scan.Ledger))).ToList();
        var cr = Clusterer.Cluster(items, t ?? R50, edits ?? [], out _);
        var inc = items.Where(i => i.Newness is IsNew).Select(i => i.Raw.Unit.Id).ToHashSet();
        foreach (var id in alsoInclude ?? []) inc.Add(id);
        return new Setup(scan, cr, inc, new FolderDecider(scan.Library, cr.Groups, t ?? R50));
    }

    private static PlanScenario ZWithLedger() =>
        Z.Aggregate(new PlanScenario().Library(Zrel, Z), (s, z) => s.LedgerFile(z, Zrel, Sites.Zachar, "America/Anchorage"));

    [Fact]
    public void Wall_SameDay_AppendHigh()
    {
        var s = Make(new PlanScenario().Card([.. Z, Clip.Vid("20260927160000", 160, Sites.Zachar)]).Library(Zrel, Z));
        var a = Assert.IsType<Append>(s.Pass1(0));
        Assert.Equal((Confidence.High, "same day as clips already in this folder"), (a.Confidence, a.Why));
    }

    [Fact] // Scenario D in miniature
    public void Wall_DifferentDay_AppendMediumWithSplitHint()
    {
        var c = new[] { Clip.Vid("20260725232655", 117, Sites.Council), Clip.Vid("20260726022937", 118, Sites.Council) };
        var a1 = Clip.Vid("20260726235645", 1, Sites.Anvil);
        var s = Make(new PlanScenario().Card([.. c, a1, Clip.Vid("20260727000012", 2, Sites.Anvil)]).Library(CouncilRel, c));
        var a = Assert.IsType<Append>(s.Pass1(0));
        Assert.Equal(Confidence.Medium, a.Confidence);
        Assert.Equal("different day, 34 mi from Council Road", a.Why);
        Assert.Equal(new SplitBefore(a1.Id()), Assert.Single(a.Hint!.Fix));
    }

    [Fact] // a wall folder dated after the New clips
    public void Wall_NewClipsBeforeFolderDate_NewFolderWithSplitAtFirstImported()
    {
        var f = new[] { Clip.Vid("20260726000000", 5, Sites.Anvil), Clip.Vid("20260726010000", 6, Sites.Anvil) };  // Jul 25 AKDT
        var early = Clip.Vid("20260725000000", 1, Sites.Anvil);                                                      // Jul 24 AKDT
        var s = Make(new PlanScenario().Card([early, .. f]).Library(@"2026\2026-07\2026-07-25 Anvil", f));
        Assert.IsType<NewFolder>(s.Pass1(0));
        Assert.Equal(f[0].Id(), s.Decider.NewBeforeWallSplitPoint(s.Clusters.Groups[0], s.Included));
    }

    [Fact]
    public void NoNew_WithWall_IsAlreadyImported_WithoutWall_IsNothingToCopy()
    {
        Assert.IsType<AlreadyImported>(Make(new PlanScenario().Card(Z).Library(Zrel, Z)).Pass1(0));

        var c1 = Clip.Vid("20260927140127", 123, Sites.Zachar, size: 1);
        var c2 = Clip.Vid("20260927140144", 124, Sites.Zachar, size: 2);
        var d = Clip.Vid("20260927142416", 148, Sites.Zachar);
        var s = Make(new PlanScenario().Card(c1, c2, d)
            .LedgerFile(Clip.Vid("20260927140127", 123, Sites.Zachar, size: 9), Zrel)
            .LedgerFile(Clip.Vid("20260927140144", 124, Sites.Zachar, size: 9), Zrel)
            .LedgerDecision(d, DecisionKind.Dismissed));
        Assert.Equal(new NothingToCopy("nothing to copy: 2 conflicts, 1 dismissed"), s.Pass1(0));

        var culled = Make(new PlanScenario().Card(Z[0], Z[1]).LedgerFile(Z[0], Zrel).LedgerFile(Z[1], Zrel));
        Assert.Equal(new NothingToCopy("nothing to copy: 2 already imported"), culled.Pass1(0));
    }

    [Fact] // inclusion before Decide: a ticked Conflict turns AlreadyImported into Append
    public void TickedConflict_TurnsAlreadyImportedIntoAppend()
    {
        var conflict = Clip.Vid("20260927150000", 150, Sites.Zachar, size: 7_340_032);
        var libVersion = Clip.Vid("20260927150000", 150, Sites.Zachar, size: 9_999_999);
        var scen = () => new PlanScenario().Card([.. Z, conflict]).Library(Zrel, [.. Z, libVersion]);
        Assert.IsType<AlreadyImported>(Make(scen()).Pass1(0));
        Assert.Equal(Confidence.High, Assert.IsType<Append>(Make(scen(), alsoInclude: [conflict.Id()]).Pass1(0)).Confidence);
    }

    [Fact] // ported #24 and #25
    public void NoWall_SameDates_HighWithLedgerCentroid_MediumWithout()
    {
        var n = Clip.Vid("20260927160000", 160, Sites.Zachar);
        var high = Assert.IsType<Append>(Make(ZWithLedger().Card(n)).Pass1(0));
        Assert.Equal((Confidence.High, "same dates, <0.1 mi"), (high.Confidence, high.Why));
        var med = Assert.IsType<Append>(Make(new PlanScenario().Library(Zrel, Z).Card(n)).Pass1(0));
        Assert.Equal((Confidence.Medium, "same dates, location unknown"), (med.Confidence, med.Why));
    }

    [Fact] // ported #26–#29 and the adjacent-day confidence rows
    public void NoWall_AdjacentDays()
    {
        var near = Assert.IsType<Append>(Make(ZWithLedger().Card(Clip.Vid("20260928180000", 170, Sites.Zachar))).Pass1(0));
        Assert.Equal((Confidence.High, "next day, <0.1 mi"), (near.Confidence, near.Why));

        var twenty = Clip.Vid("20260928180000", 170, new GeoPoint(57.5368 + 0.2894631662875806, -153.7484));
        var s20 = Make(ZWithLedger().Card(twenty));
        var mid = Assert.IsType<Append>(s20.Pass1(0));
        Assert.Equal((Confidence.Medium, "different day, 20 mi"), (mid.Confidence, mid.Why));
        var fix = Assert.IsType<Retarget>(Assert.Single(mid.Hint!.Fix));
        Assert.IsType<NewFolderTarget>(fix.Choice);

        Assert.IsType<NewFolder>(Make(ZWithLedger().Card(Clip.Vid("20260928180000", 170, Sites.KodiakTown))).Pass1(0));

        var unknown = Make(new PlanScenario().Library(Zrel, Z).Card(Clip.Vid("20260928180000", 170, Sites.Zachar)));
        Assert.IsType<NewFolder>(unknown.Pass1(0));
        Assert.Contains(unknown.Decider.AppendCandidates(unknown.Clusters.Groups[0], 5), f => f.Ref.Description == "Zachar Bay");

        Assert.IsType<NewFolder>(Make(ZWithLedger().Card(Clip.Vid("20260926180000", 170, Sites.Zachar))).Pass1(0));
    }

    [Fact] // two-pass UserSplit: the earlier group keeps the shared target
    public void TwoPass_LaterNeighbourBecomesNewFolder()
    {
        var a = Clip.Vid("20260927160000", 160, Sites.Zachar);
        var b = Clip.Vid("20260927161000", 161, Sites.Zachar);
        var s = Make(ZWithLedger().Card(a, b), [new SplitBefore(b.Id())]);
        Assert.IsType<Append>(s.Pass1(0));
        Assert.IsType<Append>(s.Pass1(1));
        Assert.IsType<Append>(s.Final(0));
        Assert.IsType<NewFolder>(s.Final(1));
    }

    [Fact] // two-pass UserSplit: F is the Wall of the later neighbour → the earlier group is NewFolder, the walled one keeps F
    public void TwoPass_WallOfNeighbour_ExcludedForOtherSide()
    {
        var early = Clip.Vid("20260927130000", 120, Sites.Zachar);
        var late = Clip.Vid("20260927160000", 160, Sites.Zachar);
        var s = Make(ZWithLedger().Card([early, .. Z, late]), [new SplitBefore(Z[0].Id())]);
        Assert.Equal(2, s.Clusters.Groups.Length);
        Assert.IsType<Append>(s.Pass1(0));
        Assert.IsType<NewFolder>(s.Final(0));
        Assert.Equal("Zachar Bay", Assert.IsType<Append>(s.Final(1)).Folder.Description);
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*FolderDeciderTests"`
Expected: build error CS0246 (`FolderDecider` not found).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Planning/FolderDecider.cs
using System.Collections.Immutable;
using UasSort.Core.Library;

namespace UasSort.Core.Planning;

/// <summary>Target choice per group (Ref §8.5) with the two-pass UserSplit rule (Ref §8.9 step 4). One instance per derive (caches).</summary>
public sealed class FolderDecider
{
    private static readonly StringComparer PathCmp = StringComparer.OrdinalIgnoreCase;
    private readonly LibraryIndex _lib;
    private readonly IReadOnlyList<GroupDraft> _all;
    private readonly Tuning _t;
    private readonly Dictionary<string, LibraryFolder> _byPath;
    private readonly Dictionary<(string, string), ImmutableHashSet<DateOnly>> _days = [];
    private readonly Dictionary<string, GeoPoint?> _centroids = new(PathCmp);

    public FolderDecider(LibraryIndex lib, IReadOnlyList<GroupDraft> all, Tuning t)
    {
        _lib = lib;
        _all = all;
        _t = t;
        _byPath = lib.Folders.GroupBy(f => f.Ref.FullPath, PathCmp).ToDictionary(g => g.Key, g => g.First(), PathCmp);
    }

    public static GroupTarget Decide(GroupDraft g, IReadOnlyList<GroupDraft> all, IReadOnlySet<ItemId> included, LibraryIndex lib,
                                     Tuning t, IReadOnlyDictionary<GroupId, GroupTarget>? pass1)
        => new FolderDecider(lib, all, t).Decide(g, included, pass1);

    public static IReadOnlyList<Item> Judged(GroupDraft g, IReadOnlySet<ItemId> included) =>
        g.Videos.Where(v => included.Contains(v.Raw.Unit.Id) && v.Newness is IsNew or Conflict).ToList();

    public static LibraryFolderRef? TargetFolder(GroupTarget t) => t switch
    {
        Append a => a.Folder,
        AlreadyImported ai => ai.Folder,
        _ => null,
    };

    public bool BordersUserSplit(GroupDraft g) => g.UserSplitNeighbour is not null || _all.Any(r => r.UserSplitNeighbour == g.Id);

    public GroupTarget Decide(GroupDraft g, IReadOnlySet<ItemId> included, IReadOnlyDictionary<GroupId, GroupTarget>? pass1)
    {
        var judged = Judged(g, included);
        if (judged.Count == 0)
            return g.Wall is { } w ? new AlreadyImported(w) : new NothingToCopy(NothingToCopySummary(g, included));

        var tz = ZoneOf(g);
        if (g.Wall is { } wall)
        {
            var days = FolderDays(wall, tz);
            if (judged[0].Time.LocalDate < wall.NameDate) return new NewFolder("");
            if (judged.Any(v => days.Contains(v.Time.LocalDate)))
                return new Append(wall, Confidence.High, "same day as clips already in this folder", null);
            var newC = Clusterer.CentroidOf(judged);
            var fc = FolderCentroid(wall);
            Distance? d = newC is { } a && fc is { } b ? PlanningGeo.Haversine(a, b) : null;
            var why = d is { } dd ? $"different day, {PlanText.Miles(dd)} from {wall.Description}" : "different day, location unknown";
            var sp = SplitPoint(g, wall, included);
            var hint = sp is { } p
                ? new CrossDayHint($"Different day from '{wall.Description}' ({PlanText.ShortDate(days.Min())})"
                                   + (d is { } x ? $" · {PlanText.Miles(x)}" : ""), [new SplitBefore(p)])
                : null;
            return new Append(wall, Confidence.Medium, why, hint);
        }

        var excluded = Excluded(g, pass1);
        var best = Candidates(g, judged, tz, excluded).OrderBy(c => c.DateGap).ThenBy(c => c.Dist).FirstOrDefault();
        return best.Target ?? new NewFolder("");
    }

    public IReadOnlyList<LibraryFolder> AppendCandidates(GroupDraft g, int max)
    {
        var tz = ZoneOf(g);
        var list = new List<(LibraryFolder F, int Gap, double Dist)>();
        foreach (var f in _lib.Folders)
        {
            var isWall = g.Wall is { } w && PathCmp.Equals(w.FullPath, f.Ref.FullPath);
            var days = FolderDays(f.Ref, tz);
            var fEnd = days.Max();
            if (!isWall && !(f.Ref.NameDate <= g.Start && g.Start <= fEnd.AddDays(_t.GapDays))) continue;
            var fc = FolderCentroid(f.Ref);
            var dist = g.Centroid is { } a && fc is { } b ? PlanningGeo.Haversine(a, b).Meters : double.PositiveInfinity;
            list.Add((f, isWall ? -1 : Math.Max(0, g.Start.DayNumber - fEnd.DayNumber), dist));
        }
        return list.OrderBy(x => x.Gap).ThenBy(x => x.Dist).Select(x => x.F).Take(max).ToList();
    }

    public ItemId? SplitPoint(GroupDraft g, LibraryFolderRef f, IReadOnlySet<ItemId> included)
    {
        var days = FolderDays(f, ZoneOf(g));
        var first = Judged(g, included).FirstOrDefault(v => !days.Contains(v.Time.LocalDate));
        if (first is null) return null;
        ItemId? point = first.Raw.Unit.Id == g.Id.Anchor
            ? g.Videos.FirstOrDefault(v => Clusterer.FolderOf(v) is { } x && Clusterer.SameFolder(x, f))?.Raw.Unit.Id
            : first.Raw.Unit.Id;
        return point is { } p && p != g.Id.Anchor ? p : null;
    }

    public ItemId? NewBeforeWallSplitPoint(GroupDraft g, IReadOnlySet<ItemId> included)
    {
        var judged = Judged(g, included);
        return g.Wall is { } w && judged.Count > 0 && judged[0].Time.LocalDate < w.NameDate ? SplitPoint(g, w, included) : null;
    }

    private IEnumerable<(GroupTarget? Target, int DateGap, double Dist)> Candidates(GroupDraft g, IReadOnlyList<Item> judged, string tz,
                                                                                  HashSet<string> excluded)
    {
        var gs = judged.Min(v => v.Time.LocalDate);
        var newDays = judged.Select(v => v.Time.LocalDate).ToHashSet();
        var gc = Clusterer.CentroidOf(judged) ?? g.Centroid;
        var radiusM = Distance.FromMiles(_t.RadiusMiles).Meters;
        var members = g.Videos.Select(v => v.Raw.Unit.Id).ToImmutableArray();
        foreach (var f in _lib.Folders)
        {
            if (excluded.Contains(f.Ref.FullPath)) continue;
            var days = FolderDays(f.Ref, tz);
            var fEnd = days.Max();
            if (!(f.Ref.NameDate <= gs && gs <= fEnd.AddDays(_t.GapDays))) continue;   // never auto-append earlier clips
            var overlap = newDays.Overlaps(days);
            var gap = Math.Max(0, gs.DayNumber - fEnd.DayNumber);
            if (gc is { } a && FolderCentroid(f.Ref) is { } b)
            {
                var d = PlanningGeo.Haversine(a, b);
                if (d.Meters > radiusM) continue;
                if (overlap)
                    yield return (new Append(f.Ref, Confidence.High, $"same dates, {PlanText.Miles(d)}", null), 0, d.Meters);
                else if (d.Miles < PlanningGeo.NearMiles)
                    yield return (new Append(f.Ref, Confidence.High, $"next day, {PlanText.Miles(d)}", null), gap, d.Meters);
                else
                    yield return (new Append(f.Ref, Confidence.Medium, $"different day, {PlanText.Miles(d)}",
                        new CrossDayHint($"Different day from '{f.Ref.Description}' ({PlanText.ShortDate(fEnd)}) · {PlanText.Miles(d)}",
                                         [new Retarget(g.Id.Anchor, new NewFolderTarget(), false, members)])), gap, d.Meters);
            }
            else if (overlap)
                yield return (new Append(f.Ref, Confidence.Medium, "same dates, location unknown", null), 0, double.PositiveInfinity);
        }
    }

    private HashSet<string> Excluded(GroupDraft g, IReadOnlyDictionary<GroupId, GroupTarget>? pass1)
    {
        var set = new HashSet<string>(PathCmp);
        if (pass1 is null) return set;
        if (g.UserSplitNeighbour is { } left && _all.FirstOrDefault(x => x.Id == left) is { } ld)
        {
            if (ld.Wall is { } w) set.Add(w.FullPath);
            if (pass1.TryGetValue(left, out var lt) && TargetFolder(lt) is { } f) set.Add(f.FullPath);
        }
        foreach (var r in _all)
            if (r.UserSplitNeighbour == g.Id && r.Wall is { } w2) set.Add(w2.FullPath);
        if (g.Wall is { } own) set.Remove(own.FullPath);
        return set;
    }

    private static string NothingToCopySummary(GroupDraft g, IReadOnlySet<ItemId> included)
    {
        bool Inc(Item v) => included.Contains(v.Raw.Unit.Id);
        var parts = new List<string>();
        var conflicts = g.Videos.Count(v => v.Newness is Conflict && !Inc(v));
        var dismissed = g.Videos.Count(v => v.Newness is Decided);
        var unticked = g.Videos.Count(v => v.Newness is IsNew && !Inc(v));
        var imported = g.Videos.Count(v => v.Newness is Imported);
        if (conflicts > 0) parts.Add(PlanText.Count(conflicts, "conflict", "conflicts"));
        if (dismissed > 0) parts.Add(PlanText.Count(dismissed, "dismissed", "dismissed"));
        if (unticked > 0) parts.Add(PlanText.Count(unticked, "unticked", "unticked"));
        if (imported > 0) parts.Add(PlanText.Count(imported, "already imported", "already imported"));
        return "nothing to copy: " + string.Join(", ", parts);
    }

    private static string ZoneOf(GroupDraft g) =>
        g.Videos.FirstOrDefault(v => v.Time.TzSource == TzSource.Gps)?.Time.TzId ?? g.Videos[0].Time.TzId;

    private ImmutableHashSet<DateOnly> FolderDays(LibraryFolderRef r, string tz)
    {
        var key = (r.FullPath.ToUpperInvariant(), tz);
        if (_days.TryGetValue(key, out var d)) return d;
        d = _byPath.TryGetValue(r.FullPath, out var f) ? f.DaysIn(tz).Add(r.NameDate) : [r.NameDate];
        _days[key] = d;
        return d;
    }

    private GeoPoint? FolderCentroid(LibraryFolderRef r)
    {
        if (_centroids.TryGetValue(r.FullPath, out var c)) return c;
        c = _byPath.TryGetValue(r.FullPath, out var f) && f.Centroid is { } lc
            ? lc
            : Clusterer.CentroidOf(_all.SelectMany(g => g.Videos).Where(v => Clusterer.FolderOf(v) is { } x && Clusterer.SameFolder(x, r)));
        _centroids[r.FullPath] = c;
        return c;
    }
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*FolderDeciderTests"`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Planning/FolderDecider.cs tests/UasSort.Core.Tests/Planning/FolderDeciderTests.cs
git commit -m "feat: folder decider with New subset, cross-day hints and two-pass UserSplit rule

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.10: DescriptionSuggester — per-day suggestions

**Files:**
- Create: `src/UasSort.Core/Naming/DescriptionSuggester.cs`
- Test: `tests/UasSort.Core.Tests/Naming/DescriptionSuggesterTests.cs`

**Interfaces:**
- Consumes: `IPlaceIndex.Near`, `PlaceHit`, `PlaceClass` (Part 02/04); `LibraryIndex.Folders`, `LibraryFolder.Loc/Centroid` (Part 05); `Suggestion`, `DescSource` (Part 02).
- Produces: `static class DescriptionSuggester { const int Max = 6; IReadOnlyList<Suggestion> Suggest(GroupDraft g, LibraryIndex lib, IPlaceIndex? places); bool Prefills(DescSource s); }` — order: the wall's description, then per local day (in day order) ledger folders ≤ 3 mi, features ≤ 1.5 mi, populated places ≤ 3 mi, "near &lt;town>" (≥ 1,000 people, ≤ 30 mi); deduplicated case-insensitively; at most 6. `Prefills` is true for ExistingFolder, Ledger, Feature and Place (rules 1–4, Ref §8.7).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Naming/DescriptionSuggesterTests.cs
using UasSort.Core.Naming;
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Naming;

public sealed class DescriptionSuggesterTests
{
    private static PlaceHit P(string name, double lat, double lon, PlaceClass cls, int pop = 0) =>
        new(name, new GeoPoint(lat, lon), cls, cls == PlaceClass.Feature ? "MT" : "", pop, "America/Anchorage", default);

    private static GroupDraft OneGroup(ScanResult scan)
    {
        var items = scan.Raw.Select(r => ItemFactory.Of(r, NewnessRules.Video((VideoUnit)r.Unit, scan.Library, scan.Ledger))).ToList();
        return Assert.Single(Clusterer.Cluster(items, new Tuning(50, 1), [], out _).Groups);
    }

    [Fact] // suggestions ranked per local day: a merged two-day group offers both names
    public void PerDay_FeatureThenTown_InDayOrder()
    {
        var scan = new PlanScenario().Card(
            Clip.Vid("20260725232655", 117, Sites.Council), Clip.Vid("20260726235645", 1, Sites.Anvil)).Build();
        var places = new FakePlaceIndex(
            P("Council Hill", 64.6945, -164.2657, PlaceClass.Feature),
            P("Anvil Mountain", 64.5650, -165.3700, PlaceClass.Feature),
            P("Nome", 64.5011, -165.4064, PlaceClass.Populated, 3699));
        var s = DescriptionSuggester.Suggest(OneGroup(scan), scan.Library, places);
        Assert.Equal(new[] { "Council Hill", "Anvil Mountain", "near Nome" }, s.Select(x => x.Text));
        Assert.Equal(new[] { DescSource.Feature, DescSource.Feature, DescSource.Town }, s.Select(x => x.Source));
        Assert.Equal(new DateOnly(2026, 7, 25), s[0].ForDay);
        Assert.Equal(new DateOnly(2026, 7, 26), s[1].ForDay);
    }

    [Fact]
    public void WallDescriptionFirst_ThenLedgerFolderWithin3Mi()
    {
        const string zrel = @"2026\2026-09\2026-09-27 Zachar Bay";
        var z = Clip.Vid("20260927140127", 123, Sites.Zachar);
        var walled = new PlanScenario().Card(z).Library(zrel, z).Build();
        Assert.Equal((DescSource.ExistingFolder, "Zachar Bay"), (DescriptionSuggester.Suggest(OneGroup(walled), walled.Library, null)[0].Source,
                                                                  DescriptionSuggester.Suggest(OneGroup(walled), walled.Library, null)[0].Text));
        var ledger = new PlanScenario().Library(zrel, z).LedgerFile(z, zrel, Sites.Zachar, "America/Anchorage")
            .Card(Clip.Vid("20260928180000", 170, Sites.Zachar)).Build();
        var s = Assert.Single(DescriptionSuggester.Suggest(OneGroup(ledger), ledger.Library, null));
        Assert.Equal((DescSource.Ledger, "Zachar Bay"), (s.Source, s.Text));
    }

    [Fact]
    public void Deduplicated_AndCappedAtSix()
    {
        var scan = new PlanScenario().Card(
            Clip.Vid("20260601100000", 1, new GeoPoint(60, -150)),
            Clip.Vid("20260602100000", 2, new GeoPoint(60, -150)),
            Clip.Vid("20260603100000", 3, new GeoPoint(60, -150))).Build();
        var places = new FakePlaceIndex(
            P("Alpha Peak", 60.001, -150, PlaceClass.Feature), P("Beta Lake", 60.002, -150, PlaceClass.Feature),
            P("Gamma Cove", 60.003, -150, PlaceClass.Feature), P("Delta", 60.004, -150, PlaceClass.Populated, 50),
            P("Epsilon", 60.005, -150, PlaceClass.Populated, 60), P("Zeta", 60.006, -150, PlaceClass.Populated, 70));
        var s = DescriptionSuggester.Suggest(OneGroup(scan), scan.Library, places);
        Assert.Equal(6, s.Count);
        Assert.Equal(s.Count, s.Select(x => x.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(DescriptionSuggester.Prefills(DescSource.Place));
        Assert.False(DescriptionSuggester.Prefills(DescSource.Town));
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*DescriptionSuggesterTests"`
Expected: build error CS0103 (`DescriptionSuggester` does not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Naming/DescriptionSuggester.cs
using UasSort.Core.Library;
using UasSort.Core.Planning;

namespace UasSort.Core.Naming;

/// <summary>Description suggestions at each local day's centroid (Ref §8.7).</summary>
public static class DescriptionSuggester
{
    public const int Max = 6;

    public static bool Prefills(DescSource s) => s is DescSource.ExistingFolder or DescSource.Ledger or DescSource.Feature or DescSource.Place;

    public static IReadOnlyList<Suggestion> Suggest(GroupDraft g, LibraryIndex lib, IPlaceIndex? places)
    {
        var list = new List<Suggestion>();
        if (g.Wall is { Description.Length: > 0 } w) list.Add(new Suggestion(w.Description, DescSource.ExistingFolder, null, null));

        var ledgerFolders = lib.Folders.Where(f => f.Loc == LocationSource.Ledger && f.Centroid is not null && f.Ref.Description.Length > 0).ToList();
        foreach (var day in g.Videos.Where(v => v.Gps is not null).GroupBy(v => v.Time.LocalDate).OrderBy(d => d.Key))
        {
            var c = PlanningGeo.Centroid(day.Select(v => v.Gps!.Point))!.Value;
            foreach (var (f, d) in ledgerFolders.Select(f => (f, PlanningGeo.Haversine(c, f.Centroid!.Value)))
                                                .Where(x => x.Item2.Miles <= 3).OrderBy(x => x.Item2.Meters))
                list.Add(new Suggestion(f.Ref.Description, DescSource.Ledger, d, day.Key));
            if (places is null) continue;
            foreach (var p in places.Near(c, Distance.FromMiles(1.5), PlaceClass.Feature, 3))
                list.Add(new Suggestion(p.Name, DescSource.Feature, p.Away, day.Key));
            foreach (var p in places.Near(c, Distance.FromMiles(3), PlaceClass.Populated, 3))
                list.Add(new Suggestion(p.Name, DescSource.Place, p.Away, day.Key));
            var town = places.Near(c, Distance.FromMiles(30), PlaceClass.Populated, 50)
                             .Where(p => p.Population >= 1000).OrderBy(p => p.Away.Meters).FirstOrDefault();
            if (town is not null) list.Add(new Suggestion($"near {town.Name}", DescSource.Town, town.Away, day.Key));
        }
        return list.Where(s => s.Text.Length > 0).DistinctBy(s => s.Text, StringComparer.OrdinalIgnoreCase).Take(Max).ToList();
    }
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*DescriptionSuggesterTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Naming/DescriptionSuggester.cs tests/UasSort.Core.Tests/Naming/DescriptionSuggesterTests.cs
git commit -m "feat: per-day description suggestions from ledger folders and GeoNames

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.11: Planner.Prepare — tuning-independent plan base

**Files:**
- Create: `src/UasSort.Core/Planning/Planner.cs`
- Create: `tests/UasSort.Testing/Planning/PlanScenario.Planner.cs`
- Test: `tests/UasSort.Core.Tests/Planning/PlannerPrepareTests.cs`

**Interfaces:**
- Consumes: `TimeResolver.Resolve(IReadOnlyList<RawItem>, ClockModel, ITimeZoneResolver, IPlaceIndex?, TimeZoneInfo, DateTime)` → `ImmutableArray<ResolvedItem>` (Part 04, **static**); `DroneClock.Summarize(ClockModel clock, IReadOnlyList<ResolvedItem> items)` → `ClockSummary` (Part 04 Task 04.9: headline, clock changes, ClockMismatch zones); `ClockModel`, `ClockSummary`, `PlanBase`, `PhotoDay` (Part 02); `LibraryIndex.WatermarkUtc` (Part 05); `NewnessRules`, `SetFolderNamer`, `Clusterer.Order` (06.4–06.7).
- Produces:
  - `sealed partial class Planner { Planner(ITimeZoneResolver tz, IPlaceIndex? places, TimeZoneInfo pcZone, TimeProvider clock); PlanBase Prepare(ScanResult scan); }` — `PlanBase.Clock` = `DroneClock.Summarize(scan.Clock, resolved)`; the Planner has no clock-summary code of its own (registry decision 45).
  - `PlanBase.Items` holds videos, photos and sets in `(CaptureUtc, Id)` order; `Truncated` is set for a video with a `.trinf` sibling or without `moov`, `ProbeFailed` for a `ProbeError` (ORed onto the resolver's flags); set placements are resolved oldest first with one case-insensitive `batchTaken`; `PhotoDay.Reason` = the day's first ProbablyImported reason, else "already in library" when every unit is Imported or Decided, else "".
  - Testing: `PlanScenario.CreatePlanner(IPlaceIndex? places = null)`, `PlanScenario.Prepare(IPlaceIndex? places = null)`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Planning/PlannerPrepareTests.cs
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class PlannerPrepareTests
{
    private static readonly RawItem[] Z =
    [
        Clip.Vid("20260927140127", 123, Sites.Zachar), Clip.Vid("20260927140144", 124, Sites.Zachar), Clip.Vid("20260927142416", 148, Sites.Zachar),
    ];

    [Fact]
    public void Prepare_BuildsItemsNewnessPhotoDaysAndSets()
    {
        var dng = Clip.Dng("20260927141000", 125, Sites.Zachar);
        var pano = Clip.Set("001_0087", "2026-05-25 09:30:28", Sites.KodiakTown,
            ("PANO_0001.DNG", 13_751_808, new DateTime(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc)));
        var b = new PlanScenario().Card([.. Z, dng, pano]).Prepare();

        Assert.Equal(5, b.Items.Length);
        Assert.Equal(pano.Id(), b.Items[0].Raw.Unit.Id);                              // oldest first
        Assert.All(b.Items.Where(i => i.Raw.Kind == ItemKind.Video), i => Assert.Equal(new IsNew(NewReason.NoMatch, null), i.Newness));
        Assert.Null(b.WatermarkUtc);                                                  // no library videos
        Assert.Equal(new IsNew(NewReason.AfterWatermark, null), b.Items.Single(i => i.Raw.Unit.Id == dng.Id()).Newness);
        Assert.Equal(new[] { new DateOnly(2026, 5, 25), new DateOnly(2026, 9, 27) }, b.PhotoDays.Select(d => d.Date));
        Assert.Equal(SetResolution.Plain, b.Sets[pano.Id()].Resolution);
    }

    [Fact]
    public void Prepare_SetsTruncatedAndProbeFailedFlags()
    {
        var t = Clip.Vid("20260727002013", 14, Sites.Anvil, moov: false);
        var b = new PlanScenario().Card(Clip.Vid("20260726235645", 1, Sites.Anvil), t).Prepare();
        Assert.True(b.Items.Single(i => i.Raw.Unit.Id == t.Id()).Flags.HasFlag(ItemFlags.Truncated));
    }

    [Fact] // Eastern clock in Alaska → Zone America/New_York, ClockMismatch on every item; the summary is Part 04's DroneClock.Summarize
    public void Prepare_ClockSummary_EasternClockInAlaska()
    {
        var scan = new PlanScenario().Card([.. Z, Clip.Dng("20260927141000", 125, Sites.Zachar)]).Build();
        var b = PlanScenario.CreatePlanner().Prepare(scan);
        Assert.Equal(ClockMode.Zone, b.Clock.Mode);
        Assert.Equal("America/New_York", b.Clock.ZoneId);
        Assert.Equal(4, b.Clock.MismatchItems);
        Assert.Equal(new[] { "America/Anchorage" }, b.Clock.MismatchSiteZones);
        Assert.StartsWith("Drone clock: US Eastern (America/New_York), learned from 3 videos.", b.Clock.Headline);
        Assert.Contains("(Alaska)", b.Clock.Headline);
        var resolved = TimeResolver.Resolve(scan.Raw, scan.Clock, new GeoTimeZoneResolver(), null, PlanScenario.PcZone, PlanScenario.NowUtc);
        Assert.Equal(DroneClock.Summarize(scan.Clock, resolved).Headline, b.Clock.Headline);
    }

    [Fact]
    public void Prepare_PhotoDayReasonFromProbablyImported()
    {
        const string zrel = @"2026\2026-09\2026-09-27 Zachar Bay";
        var b = new PlanScenario().Library(zrel, Z).Card(Clip.Dng("20260815200000", 119, Sites.Anvil)).Prepare();
        Assert.Equal("photo-only day before the last imported video (Sep 27)", Assert.Single(b.PhotoDays).Reason);
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PlannerPrepareTests"`
Expected: build error CS1061 (`PlanScenario` has no `Prepare`) and CS0246 (`Planner` not found).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Planning/Planner.cs
using UasSort.Core.Naming;
using UasSort.Core.Time;

namespace UasSort.Core.Planning;

/// <summary>Prepare = tuning-independent steps (Ref §4.2); Derive (Planner.Derive.cs) = the derive order of Ref §8.9.</summary>
public sealed partial class Planner
{
    private readonly ITimeZoneResolver _tz;
    private readonly IPlaceIndex? _places;
    private readonly TimeZoneInfo _pc;
    private readonly TimeProvider _clock;

    public Planner(ITimeZoneResolver tz, IPlaceIndex? places, TimeZoneInfo pcZone, TimeProvider clock)
    {
        _tz = tz;
        _places = places;
        _pc = pcZone;
        _clock = clock;
    }

    public PlanBase Prepare(ScanResult scan)
    {
        var resolved = TimeResolver.Resolve(scan.Raw, scan.Clock, _tz, _places, _pc, _clock.GetUtcNow().UtcDateTime);
        var lib = scan.Library;
        var ledger = scan.Ledger;

        var videos = resolved.Where(r => r.Raw.Unit is VideoUnit)
            .Select(r => new Item(r.Raw, r.Time, r.Gps, r.Session, Flags(r), NewnessRules.Video((VideoUnit)r.Raw.Unit, lib, ledger)))
            .ToList();
        var newVideoDays = videos.Where(v => v.Newness is IsNew).Select(v => v.Time.LocalDate).ToHashSet();
        var watermark = lib.WatermarkUtc;

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sets = ImmutableDictionary.CreateBuilder<ItemId, SetPlacement>();
        var photos = new List<Item>();
        foreach (var r in resolved.Where(r => r.Raw.Unit is not VideoUnit)
                                  .OrderBy(r => r.Time.CaptureUtc).ThenBy(r => r.Raw.Unit.Id.CardRelPath, StringComparer.Ordinal))
        {
            SetPlacement? placement = null;
            if (r.Raw.Unit is SetUnit su)
            {
                var provisional = new Item(r.Raw, r.Time, r.Gps, r.Session, Flags(r), new IsNew(NewReason.NoMatch, null));
                placement = SetFolderNamer.Resolve(su, provisional, lib, ledger, taken);
                sets[su.Id] = placement;
            }
            photos.Add(new Item(r.Raw, r.Time, r.Gps, r.Session, Flags(r),
                                NewnessRules.Photo(r.Raw.Unit, r.Time, lib, ledger, placement, newVideoDays, watermark)));
        }

        var items = videos.Concat(photos).ToList();
        items.Sort(Clusterer.Order);
        var all = items.ToImmutableArray();
        return new PlanBase(scan, all, PhotoDays(photos), sets.ToImmutable(), DroneClock.Summarize(scan.Clock, resolved), watermark);
    }

    private static ItemFlags Flags(ResolvedItem r)
    {
        var f = r.Flags;
        if (r.Raw.Unit is VideoUnit v && (v.HasTrinf || r.Raw.Mp4 is { HasMoov: false })) f |= ItemFlags.Truncated;
        if (r.Raw.ProbeError is not null) f |= ItemFlags.ProbeFailed;
        return f;
    }

    private static ImmutableArray<PhotoDay> PhotoDays(List<Item> photos) =>
    [
        .. photos.GroupBy(p => p.Time.LocalDate).OrderBy(g => g.Key)
                 .Select(g => new PhotoDay(g.Key, g.First().Time.TzId, [.. g.Select(p => p.Raw.Unit.Id)], DayReason(g))),
    ];

    private static string DayReason(IEnumerable<Item> day)
    {
        var list = day.ToList();
        if (list.Select(p => p.Newness).OfType<ProbablyImported>().FirstOrDefault() is { } pi) return pi.Why;
        return list.All(p => p.Newness is Imported or Decided) ? "already in library" : "";
    }
}
```

```csharp
// tests/UasSort.Testing/Planning/PlanScenario.Planner.cs
using UasSort.Core.Geo;
using UasSort.Core.Planning;

namespace UasSort.Testing.Planning;

public sealed partial class PlanScenario
{
    public static Planner CreatePlanner(IPlaceIndex? places = null) =>
        new(new GeoTimeZoneResolver(), places, PcZone, new FakeTimeProvider(new DateTimeOffset(NowUtc)));

    public PlanBase Prepare(IPlaceIndex? places = null) => CreatePlanner(places).Prepare(Build());
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PlannerPrepareTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Planning/Planner.cs tests/UasSort.Testing/Planning/PlanScenario.Planner.cs tests/UasSort.Core.Tests/Planning/PlannerPrepareTests.cs
git commit -m "feat: Planner.Prepare (time, newness, set placements, photo days, DroneClock summary)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.12: Planner.Derive — inclusion, two-pass decisions, names, pins, issue catalogue

**Files:**
- Create: `src/UasSort.Core/Planning/Planner.Derive.cs`
- Modify: `tests/UasSort.Testing/Planning/PlanScenario.Planner.cs` (add `Derive`)
- Test: `tests/UasSort.Core.Tests/Planning/PlannerDeriveTests.cs`

**Interfaces:**
- Consumes: everything from 06.1–06.11; `Plan`, `VideoGroup`, `PinState`, `Issue`, `QuickFix`, `SessionFlags`, `IPlanDeriver`, `LedgerFolderStatus` (Part 02, namespace `UasSort.Core`); `ClockModel.OffsetAt` (Part 04 partial); `EventFolderName.TryParse` (Part 05, for `FolderRefFor`).
- Produces:
  - `Planner : IPlanDeriver` with `Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct)` and `internal static LibraryFolderRef FolderRefFor(string path, DateOnly fallback, LibraryIndex lib)` (used by EditValidator).
  - `VideoGroup.Videos` in `(CaptureUtc, Id)` order (index 0 = anchor); `ColorIndex` = timeline order among non-AlreadyImported groups mod 10, `-1` for AlreadyImported; `Spread` = largest pairwise GPS distance; `Hints` = cross-day hint text and the "N days · X apart" chip.
  - Folding: AlreadyImported and no member is Conflict, IsNew (an unticked New clip), Truncated or ProbeFailed. `NothingToCopy` groups never fold (main spec §5.6).
  - Name: NewFolder → the last active `Rename` (cleaned, `DescSource.User`), else the top prefill suggestion (rules 1–4), else blank (`EmptyFolderName`). A NewFolder path equal (case-insensitive) to an existing event folder becomes `Append(F, High, "Folder exists; appending")` + Info `FolderExistsAppending` (Review Focus #2); such a group stays `DescriptionEditable`.
  - Pins: per group, a later pin replaces an earlier one whose `PinnedMembers ∪ {InGroup}` overlap; `Rename(null)` and `Retarget(AutoTarget)` clear; two surviving pins with different values → Blocking `ConflictingPins`.
  - `IssueCode.NothingNew` (Blocking) when every item is Imported or Decided (Review Focus #1: every AlreadyImported group folded; NothingToCopy groups never fold).

Issue catalogue raised here (Ref §9.10 rows "Raised by Planner.Derive"): `EmptyFolderName`, `TempPathTooLong`, `MediumAppend`, `EmphasisedDaySplit`, `PinMembershipChanged`, `ConflictingPins`, `SharedTarget`, `FolderExistsAppending`, `NewBeforeWallFolder`, `CheckDate`, `ClockNotSet`, `ClockMismatch`, `RootMissing`, `RootsUnconfirmed`, `LedgerParseIssue`, `LedgerCloudOnly`, `LedgerUnwritable`, `LedgerNotPinned`, `LedgerNoHistory`, `NothingNew`.

- [ ] **Step 1: Write the failing test**

Add to `tests/UasSort.Testing/Planning/PlanScenario.Planner.cs` (inside the partial class):

```csharp
    public static Plan Derive(PlanBase b, Tuning? t = null, IReadOnlyList<PlanEdit>? edits = null, bool ledgerAccepted = false,
                              IPlaceIndex? places = null) =>
        CreatePlanner(places).Derive(b, t ?? new Tuning(), edits ?? [], new SessionFlags(ledgerAccepted), 1, CancellationToken.None);
```

```csharp
// tests/UasSort.Core.Tests/Planning/PlannerDeriveTests.cs
using UasSort.Core.Naming;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class PlannerDeriveTests
{
    private const string Zrel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private static readonly RawItem[] Z =
    [
        Clip.Vid("20260927140127", 123, Sites.Zachar), Clip.Vid("20260927140144", 124, Sites.Zachar), Clip.Vid("20260927142416", 148, Sites.Zachar),
    ];
    private static readonly RawItem C117 = Clip.Vid("20260725232655", 117, Sites.Council);
    private static readonly RawItem C118 = Clip.Vid("20260726022937", 118, Sites.Council);
    private static readonly RawItem A1 = Clip.Vid("20260726235645", 1, Sites.Anvil);
    private static readonly RawItem A2 = Clip.Vid("20260727000012", 2, Sites.Anvil);

    private static IEnumerable<Issue> Of(Plan p, IssueCode c) => p.Issues.Where(i => i.Code == c);

    [Fact]
    public void NewFolder_PrefilledFromSuggestion_ElseBlankAndBlocking()
    {
        var b = new PlanScenario().Card(Z).Prepare();
        var blank = PlanScenario.Derive(b);
        var g = Assert.Single(blank.Groups);
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-27"), g.Target);
        Assert.True(g.DescriptionEditable);
        var e = Assert.Single(Of(blank, IssueCode.EmptyFolderName));
        Assert.Equal((IssueSeverity.Blocking, g.Id.Anchor), (e.Severity, e.Anchor));

        var places = new FakePlaceIndex(new PlaceHit("Zachar Bay", new GeoPoint(57.5400, -153.7500), PlaceClass.Feature, "BAY", 0, "America/Anchorage", default));
        var named = PlanScenario.Derive(b, places: places);
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-27 Zachar Bay"), named.Groups[0].Target);
        Assert.Equal(DescSource.Feature, named.Groups[0].DescSource);
        Assert.Empty(Of(named, IssueCode.EmptyFolderName));
    }

    [Fact]
    public void Rename_NamesTheFolder_UnicodeKept()
    {
        var b = new PlanScenario().Card(Z).Prepare();
        var ids = Z.Select(z => z.Id()).ToArray();
        var p = PlanScenario.Derive(b, edits: [new Rename(ids[0], "  Sunset 🌅 Kodiak. ", [.. ids])]);
        var g = p.Groups[0];
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-27 Sunset 🌅 Kodiak"), g.Target);
        Assert.Equal((DescSource.User, false), (g.DescSource, g.NamePin!.MembershipChanged));
    }

    [Theory] // [Review Focus] #2: a case-only (or trailing-dot) clash with an existing same-date folder is an explicit Append
    [InlineData("zachar bay")]
    [InlineData("ZACHAR BAY. ")]
    public void CaseOnlyClash_BecomesAppend_NeverASecondFolder(string typed)
    {
        var kodiak = Clip.Vid("20260927190000", 150, Sites.KodiakTown);        // same day, 53 mi away: auto = NewFolder
        var b = Z.Aggregate(new PlanScenario().Library(Zrel, Z), (s, z) => s.LedgerFile(z, Zrel, Sites.Zachar, "America/Anchorage"))
                 .Card(kodiak).Prepare();
        Assert.IsType<NewFolder>(PlanScenario.Derive(b).Groups[0].Target);
        var p = PlanScenario.Derive(b, edits: [new Rename(kodiak.Id(), typed, [kodiak.Id()])]);
        var a = Assert.IsType<Append>(p.Groups[0].Target);
        Assert.Equal($@"{PlanScenario.VideoRoot}\{Zrel}", a.Folder.FullPath);
        Assert.Equal(FolderNamer.FolderExistsWhy, a.Why);
        Assert.Equal(IssueSeverity.Info, Assert.Single(Of(p, IssueCode.FolderExistsAppending)).Severity);
        Assert.True(p.Groups[0].DescriptionEditable);
    }

    [Theory] // the 400-character limit is on the TEMP path
    [InlineData(331, true)]
    [InlineData(330, false)]
    public void TempPathLimit_Is400(int pad, bool blocking)
    {
        var s = new PlanScenario { Root = @"C:\" + new string('r', pad) }.Card(Z[0]);
        var p = PlanScenario.Derive(s.Prepare());
        Assert.Equal(blocking, Of(p, IssueCode.TempPathTooLong).Any());
    }

    [Fact] // pins: membership change → Warning with [Keep]/[Reset to Auto]
    public void RetargetPin_MembershipChanged_WarnsWithQuickFixes()
    {
        var b = new PlanScenario().Card(C117, C118, A1, A2).Prepare();
        var members = new[] { C117, C118, A1, A2 }.Select(x => x.Id()).ToArray();
        var pin = new Retarget(C117.Id(), new SkipTarget(), false, [.. members]);
        var at50 = PlanScenario.Derive(b, new Tuning(50, 1), [pin]);
        Assert.IsType<SkipGroup>(at50.Groups[0].Target);
        Assert.Empty(Of(at50, IssueCode.PinMembershipChanged));

        var at25 = PlanScenario.Derive(b, new Tuning(25, 1), [pin]);
        Assert.Equal(new PinState(true, 4, 2), at25.Groups[0].TargetPin);
        var w = Assert.Single(Of(at25, IssueCode.PinMembershipChanged));
        Assert.True(w.RequiresAckAtPreflight);
        Assert.Equal(new[] { "Keep", "Reset to Auto" }, w.QuickFixes.Select(q => q.Label));
        Assert.IsType<AutoTarget>(Assert.IsType<Retarget>(Assert.Single(w.QuickFixes[1].Edits)).Choice);
    }

    [Fact] // two pins after a merge → Blocking [Use first]/[Use second]
    public void TwoRenamesAfterMerge_AreConflictingPins()
    {
        var b = new PlanScenario().Card(C117, C118, A1, A2).Prepare();
        var r1 = new Rename(C117.Id(), "Council Road", [C117.Id(), C118.Id()]);
        var r2 = new Rename(A1.Id(), "Anvil Mountain", [A1.Id(), A2.Id()]);
        var split = PlanScenario.Derive(b, new Tuning(25, 1), [r1, r2]);
        Assert.Empty(Of(split, IssueCode.ConflictingPins));
        var merged = PlanScenario.Derive(b, new Tuning(25, 1), [r1, r2, new Merge(C117.Id(), A1.Id())]);
        var c = Assert.Single(Of(merged, IssueCode.ConflictingPins));
        Assert.Equal(IssueSeverity.Blocking, c.Severity);
        Assert.Equal(new[] { "Use first", "Use second" }, c.QuickFixes.Select(q => q.Label));
        var fixedPlan = PlanScenario.Derive(b, new Tuning(25, 1), [r1, r2, new Merge(C117.Id(), A1.Id()), .. c.QuickFixes[1].Edits]);
        Assert.Empty(Of(fixedPlan, IssueCode.ConflictingPins));
        Assert.Equal("Anvil Mountain", fixedPlan.Groups[0].Description);
    }

    [Fact]
    public void SharedTarget_InfoWithMerge()
    {
        var late = Clip.Vid("20261001120000", 300, Sites.Zachar);
        var b = new PlanScenario().Library(Zrel, Z).Card([.. Z, Clip.Vid("20260927160000", 160, Sites.Zachar), late]).Prepare();
        var p = PlanScenario.Derive(b, edits: [new Retarget(late.Id(), new AppendTo($@"{PlanScenario.VideoRoot}\{Zrel}"), false, [late.Id()])]);
        Assert.Equal(2, p.Groups.Count(g => g.Target is Append));
        var shared = Of(p, IssueCode.SharedTarget).ToList();
        Assert.Equal(2, shared.Count);
        Assert.All(shared, s => Assert.IsType<Merge>(Assert.Single(Assert.Single(s.QuickFixes).Edits)));
    }

    [Fact] // fold rule: Conflict, Truncated or ProbeFailed members never fold
    public void FoldRule()
    {
        var folded = PlanScenario.Derive(new PlanScenario().Library(Zrel, Z).Card(Z).Prepare());
        Assert.True(folded.Groups[0].Foldable);
        Assert.Equal(-1, folded.Groups[0].ColorIndex);
        var trunc = Clip.Vid("20260927143000", 149, Sites.Zachar, moov: false);
        var withTrunc = PlanScenario.Derive(new PlanScenario().Library(Zrel, [.. Z, trunc]).Card([.. Z, trunc]).Prepare());
        Assert.IsType<AlreadyImported>(withTrunc.Groups[0].Target);
        Assert.False(withTrunc.Groups[0].Foldable);
    }

    [Fact] // [Review Focus] #1: nothing new on the card → Blocking NothingNew; every AlreadyImported group folded; NothingToCopy groups never fold
    public void NothingNew_BlocksOffload_AlreadyImportedFold_NothingToCopyNever()
    {
        var dng = Clip.Dng("20260927141000", 125, Sites.Zachar);
        var council = new[] { C117, C118 };
        var culled = Clip.Vid("20260801200000", 7, Sites.Anvil);                 // in the ledger only (culled from the library)
        var b = new PlanScenario()
            .Library(Zrel, Z).Library(@"2026\2026-07\2026-07-25 Council Road", council)
            .LedgerFile(dng, null)
            .LedgerFile(culled, @"2026\2026-08\2026-08-01 Anvil")
            .Card([.. council, culled, .. Z, dng]).Prepare();
        var p = PlanScenario.Derive(b);
        var n = Assert.Single(Of(p, IssueCode.NothingNew));
        Assert.Equal(IssueSeverity.Blocking, n.Severity);
        Assert.Equal(3, p.Groups.Length);
        Assert.Equal(2, p.Groups.Count(g => g.Target is AlreadyImported));
        Assert.All(p.Groups.Where(g => g.Target is AlreadyImported), g => Assert.True(g.Foldable));
        var nothing = Assert.Single(p.Groups, g => g.Target is NothingToCopy);
        Assert.Equal(new NothingToCopy("nothing to copy: 1 already imported"), nothing.Target);
        Assert.False(nothing.Foldable);
        Assert.Empty(p.Included);

        var oneNew = PlanScenario.Derive(new PlanScenario().Library(Zrel, Z).Card([.. Z, Clip.Vid("20260927160000", 160, Sites.Zachar)]).Prepare());
        Assert.Empty(Of(oneNew, IssueCode.NothingNew));
    }

    [Fact]
    public void Inclusion_DefaultsThenEditsInLogOrder()
    {
        var trunc = Clip.Vid("20260927143000", 149, Sites.Zachar, moov: false);
        var dng = Clip.Dng("20260927141000", 125, Sites.Zachar);
        var b = new PlanScenario().Card([.. Z, trunc, dng]).Prepare();
        var p = PlanScenario.Derive(b);
        Assert.DoesNotContain(trunc.Id(), p.Included);                          // truncated: unticked
        Assert.Contains(dng.Id(), p.Included);
        var p2 = PlanScenario.Derive(b, edits: [new SetIncluded([trunc.Id()], true), new SetDayIncluded(new DateOnly(2026, 9, 27), false)]);
        Assert.Contains(trunc.Id(), p2.Included);
        Assert.DoesNotContain(dng.Id(), p2.Included);
    }

    [Fact]
    public void MediumAppend_AndEmphasisedSplit_RequireAck_WithQuickFixes()
    {
        var b = new PlanScenario().Library(@"2026\2026-07\2026-07-25 Council Road", C117, C118).Card(C117, C118, A1, A2).Prepare();
        var p = PlanScenario.Derive(b);
        var m = Assert.Single(Of(p, IssueCode.MediumAppend));
        Assert.True(m.RequiresAckAtPreflight);
        Assert.Equal(new SplitBefore(A1.Id()), Assert.Single(Assert.Single(m.QuickFixes).Edits));
        var d = Assert.Single(Of(p, IssueCode.EmphasisedDaySplit));
        Assert.Equal((IssueSeverity.Warning, true, (ItemId?)A1.Id()), (d.Severity, d.RequiresAckAtPreflight, d.Anchor));
        Assert.Contains(p.Groups[0].Hints, h => h.StartsWith("2 days", StringComparison.Ordinal));
    }

    [Fact]
    public void NewBeforeWallFolder_InfoWithSplitHere()
    {
        var f = new[] { Clip.Vid("20260726000000", 5, Sites.Anvil), Clip.Vid("20260726010000", 6, Sites.Anvil) };
        var early = Clip.Vid("20260725000000", 1, Sites.Anvil);
        var p = PlanScenario.Derive(new PlanScenario().Library(@"2026\2026-07\2026-07-25 Anvil", f).Card([early, .. f]).Prepare());
        var i = Assert.Single(Of(p, IssueCode.NewBeforeWallFolder));
        Assert.Equal(new SplitBefore(f[0].Id()), Assert.Single(Assert.Single(i.QuickFixes).Edits));
    }

    [Fact]
    public void LedgerAndRootIssues()
    {
        var bad = new PlanScenario { LedgerState = LedgerFolderState.Ok }.LedgerParseIssue("ledger-A.jsonl", 7, "bad json").Card(Z[0]).Prepare();
        var blocked = Assert.Single(Of(PlanScenario.Derive(bad), IssueCode.LedgerParseIssue));
        Assert.Equal((IssueSeverity.Blocking, false), (blocked.Severity, blocked.RequiresAckAtPreflight));
        var accepted = Assert.Single(Of(PlanScenario.Derive(bad, ledgerAccepted: true), IssueCode.LedgerParseIssue));
        Assert.Equal((IssueSeverity.Warning, true), (accepted.Severity, accepted.RequiresAckAtPreflight));

        Assert.Equal(IssueSeverity.Blocking, Assert.Single(Of(PlanScenario.Derive(
            new PlanScenario { LedgerState = LedgerFolderState.CloudOnly }.Card(Z[0]).Prepare()), IssueCode.LedgerCloudOnly)).Severity);
        Assert.Equal(IssueSeverity.Blocking, Assert.Single(Of(PlanScenario.Derive(
            new PlanScenario { LedgerState = LedgerFolderState.Unwritable }.Card(Z[0]).Prepare()), IssueCode.LedgerUnwritable)).Severity);
        Assert.Equal(IssueSeverity.Warning, Assert.Single(Of(PlanScenario.Derive(
            new PlanScenario { LedgerState = LedgerFolderState.NotPinned }.Card(Z[0]).Prepare()), IssueCode.LedgerNotPinned)).Severity);
        Assert.Equal(IssueSeverity.Info, Assert.Single(Of(PlanScenario.Derive(
            new PlanScenario { LedgerState = LedgerFolderState.Missing }.Card(Z[0]).Prepare()), IssueCode.LedgerNoHistory)).Severity);
        Assert.Equal(IssueSeverity.Blocking, Assert.Single(Of(PlanScenario.Derive(
            new PlanScenario { RootsConfirmed = false }.Card(Z[0]).Prepare()), IssueCode.RootsUnconfirmed)).Severity);

        var dng = Clip.Dng("20260927141000", 125, Sites.Zachar);
        var noPhotoRoot = new PlanScenario { PhotoRootAvailable = false }.Card(Z[0], dng).Prepare();
        var p = PlanScenario.Derive(noPhotoRoot);
        Assert.DoesNotContain(dng.Id(), p.Included);                            // its items are unticked
        Assert.Equal(IssueSeverity.Warning, Assert.Single(Of(p, IssueCode.RootMissing)).Severity);
        var ticked = PlanScenario.Derive(noPhotoRoot, edits: [new SetIncluded([dng.Id()], true)]);
        Assert.Equal(IssueSeverity.Blocking, Assert.Single(Of(ticked, IssueCode.RootMissing)).Severity);
    }

    [Fact]
    public void ClockAndDateIssues()
    {
        var b = new PlanScenario().Card([.. Z, Clip.Vid("20260726035000", 1, Sites.Anvil)]).Prepare();
        var p = PlanScenario.Derive(b);
        var cm = Assert.Single(Of(p, IssueCode.ClockMismatch));
        Assert.Equal((IssueSeverity.Info, (ItemId?)null), (cm.Severity, cm.Anchor));
        Assert.StartsWith("Drone clock is set to UTC\u22124 (America/New_York), but footage on this card was shot in Alaska (UTC\u22128).", cm.Message);
        Assert.Equal(b.Items.Count(i => i.Flags.HasFlag(ItemFlags.CheckDate)), Of(p, IssueCode.CheckDate).Count());
        Assert.All(Of(p, IssueCode.CheckDate), i => Assert.Equal(IssueSeverity.Warning, i.Severity));
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PlannerDeriveTests"`
Expected: build error CS1061 (`Planner` has no `Derive`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Planning/Planner.Derive.cs
using System.Globalization;
using UasSort.Core.Library;
using UasSort.Core.Naming;

namespace UasSort.Core.Planning;

public sealed partial class Planner : IPlanDeriver
{
    private static readonly StringComparer PathCmp = StringComparer.OrdinalIgnoreCase;

    private sealed record Roots(bool VideoMissing, bool PhotoMissing, ImmutableArray<string> Unavailable);

    public Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var scan = b.Scan;
        var lib = scan.Library;
        var settings = scan.Settings;

        // 1–2. auto-cluster + structural edits
        var videos = b.Items.Where(i => i.Raw.Kind == ItemKind.Video).ToList();
        var cr = Clusterer.Cluster(videos, t, edits.Where(PlanEditRefs.IsStructural).ToList(), out _);
        ct.ThrowIfCancellationRequested();

        // 3. inclusion
        var roots = RootState(b);
        var included = Inclusion(b, edits, roots);

        // 4. decide in two passes
        var decider = new FolderDecider(lib, cr.Groups, t);
        var pass1 = cr.Groups.ToDictionary(g => g.Id, g => decider.Decide(g, included, null));
        var auto = new Dictionary<GroupId, GroupTarget>(pass1);
        foreach (var g in cr.Groups)
            if (decider.BordersUserSplit(g)) auto[g.Id] = decider.Decide(g, included, pass1);
        ct.ThrowIfCancellationRequested();

        // 5–6. names, pins, issues
        var issues = ImmutableArray.CreateBuilder<Issue>();
        var groups = ImmutableArray.CreateBuilder<VideoGroup>(cr.Groups.Length);
        var color = 0;
        foreach (var g in cr.Groups)
            groups.Add(BuildGroup(g, auto[g.Id], edits, included, decider, settings, lib, issues, ref color));
        SharedTargets(groups, issues);
        ItemIssues(b, issues);
        PlanIssues(b, flags, roots, included, issues);
        return new Plan(revision, b, t, groups.MoveToImmutable(), cr.Boundaries, included.ToImmutableHashSet(), issues.ToImmutable());
    }

    private static Roots RootState(PlanBase b)
    {
        var s = b.Scan.Settings;
        var un = b.Scan.Library.UnavailableRoots;
        bool Eq(string a, string c) => PathCmp.Equals(a.TrimEnd('\\'), c.TrimEnd('\\'));
        var video = un.Any(r => Eq(r, s.VideoRoot)) || b.Scan.Ledger.Status.State == LedgerFolderState.VideoRootMissing;
        return new Roots(video, video && s.PhotoRoot.StartsWith(s.VideoRoot, StringComparison.OrdinalIgnoreCase) || un.Any(r => Eq(r, s.PhotoRoot)), un);
    }

    private static HashSet<ItemId> Inclusion(PlanBase b, IReadOnlyList<PlanEdit> edits, Roots roots)
    {
        var byId = b.Items.ToDictionary(i => i.Raw.Unit.Id);
        bool RootOk(Item i) => i.Raw.Kind == ItemKind.Video ? !roots.VideoMissing : !roots.PhotoMissing;
        static bool Includable(Item i) => i.Newness is IsNew or Conflict or ProbablyImported;
        var inc = b.Items.Where(i => i.Newness is IsNew && !i.Flags.HasFlag(ItemFlags.Truncated) && RootOk(i))
                         .Select(i => i.Raw.Unit.Id).ToHashSet();
        foreach (var e in edits)
        {
            switch (e)
            {
                case SetIncluded s:
                    foreach (var id in s.Items)
                        if (byId.TryGetValue(id, out var it) && Includable(it))
                        {
                            if (s.Included) inc.Add(id);
                            else inc.Remove(id);
                        }
                    break;
                case SetDayIncluded d:
                    foreach (var it in b.Items)
                        if (it.Raw.Kind != ItemKind.Video && it.Time.LocalDate == d.Day && Includable(it))
                        {
                            if (d.Included) inc.Add(it.Raw.Unit.Id);
                            else inc.Remove(it.Raw.Unit.Id);
                        }
                    break;
            }
        }
        return inc;
    }

    private static bool PinsOverlap(ItemId aIn, ImmutableArray<ItemId> aPinned, ItemId bIn, ImmutableArray<ItemId> bPinned)
    {
        var a = new HashSet<ItemId>(aPinned.IsDefault ? [] : aPinned) { aIn };
        return a.Contains(bIn) || (!bPinned.IsDefault && bPinned.Any(a.Contains));
    }

    private static (List<Rename> Renames, List<Retarget> Retargets) Pins(GroupDraft g, IReadOnlyList<PlanEdit> edits)
    {
        var members = g.Videos.Select(v => v.Raw.Unit.Id).ToHashSet();
        var renames = new List<Rename>();
        var retargets = new List<Retarget>();
        foreach (var e in edits)
        {
            if (e is Rename r && members.Contains(r.InGroup))
            {
                renames.RemoveAll(o => PinsOverlap(o.InGroup, o.PinnedMembers, r.InGroup, r.PinnedMembers));
                renames.Add(r);
            }
            else if (e is Retarget rt && members.Contains(rt.InGroup))
            {
                retargets.RemoveAll(o => PinsOverlap(o.InGroup, o.PinnedMembers, rt.InGroup, rt.PinnedMembers));
                retargets.Add(rt);
            }
        }
        renames.RemoveAll(r => r.Description is null);                // back to suggestion
        retargets.RemoveAll(r => r.Choice is AutoTarget);              // back to auto
        return (renames, retargets);
    }

    private static string ChoiceKey(TargetChoice c) => c switch
    {
        AutoTarget => "auto",
        NewFolderTarget => "new",
        AppendTo a => "append:" + a.FolderFullPath.TrimEnd('\\').ToUpperInvariant(),
        SkipTarget => "skip",
    };

    private static string ChoiceLabel(TargetChoice c) => c switch
    {
        AutoTarget => "Auto",
        NewFolderTarget => "New folder",
        AppendTo a => Path.GetFileName(a.FolderFullPath.TrimEnd('\\')),
        SkipTarget => "Skip",
    };

    private static bool SameMembers(ImmutableArray<ItemId> pinned, ImmutableArray<ItemId> now) =>
        !pinned.IsDefault && pinned.Length == now.Length && pinned.ToHashSet().SetEquals(now);

    internal static LibraryFolderRef FolderRefFor(string path, DateOnly fallback, LibraryIndex lib)
    {
        var p = path.TrimEnd('\\');
        if (lib.Folders.FirstOrDefault(f => PathCmp.Equals(f.Ref.FullPath.TrimEnd('\\'), p)) is { } found) return found.Ref;
        var leaf = PathRules.FileName(p);
        return EventFolderName.TryParse(leaf, out var date, out var description)      // Part 05's event-folder rule
            ? new LibraryFolderRef(p, date, description)
            : new LibraryFolderRef(p, fallback, leaf);
    }

    private VideoGroup BuildGroup(GroupDraft g, GroupTarget auto, IReadOnlyList<PlanEdit> edits, HashSet<ItemId> included,
                                  FolderDecider decider, Settings s, LibraryIndex lib, ImmutableArray<Issue>.Builder issues, ref int color)
    {
        var anchor = g.Id.Anchor;
        var memberIds = g.Videos.Select(v => v.Raw.Unit.Id).ToImmutableArray();
        var (renames, retargets) = Pins(g, edits);

        // target pin (Retarget)
        var rtConflict = retargets.Select(r => ChoiceKey(r.Choice)).Distinct().Count() > 1;
        var rt = retargets.Count == 0 ? null : rtConflict ? retargets[0] : retargets[^1];
        GroupTarget target = rt is null ? auto : rt.Choice switch
        {
            AutoTarget => auto,
            NewFolderTarget => (GroupTarget)new NewFolder(""),
            AppendTo a => new Append(FolderRefFor(a.FolderFullPath, g.Start, lib), Confidence.High, "chosen by you", null),
            SkipTarget => new SkipGroup(),
        };
        PinState? targetPin = rt is null ? null : new PinState(!SameMembers(rt.PinnedMembers, memberIds), rt.PinnedMembers.Length, memberIds.Length);

        // suggestions (the append target's description first when it is not the wall)
        var suggestions = DescriptionSuggester.Suggest(g, lib, _places).ToList();
        if (target is Append ap && (g.Wall is null || !Clusterer.SameFolder(ap.Folder, g.Wall)) && ap.Folder.Description.Length > 0)
            suggestions.Insert(0, new Suggestion(ap.Folder.Description, DescSource.ExistingFolder, null, null));
        suggestions = suggestions.DistinctBy(x => x.Text, StringComparer.OrdinalIgnoreCase).Take(DescriptionSuggester.Max).ToList();

        // name pin (Rename)
        var rnConflict = renames.Select(r => FolderNamer.Clean(r.Description!).ToUpperInvariant()).Distinct().Count() > 1;
        var rn = renames.Count == 0 ? null : rnConflict ? renames[0] : renames[^1];
        PinState? namePin = rn is null ? null : new PinState(!SameMembers(rn.PinnedMembers, memberIds), rn.PinnedMembers.Length, memberIds.Length);

        string description;
        DescSource descSource;
        var editable = false;
        var hints = ImmutableArray.CreateBuilder<string>();
        switch (target)
        {
            case NewFolder:
            {
                var top = suggestions.FirstOrDefault(x => DescriptionSuggester.Prefills(x.Source));
                (description, descSource) = rn is not null ? (FolderNamer.Clean(rn.Description!), DescSource.User)
                                          : top is not null ? (FolderNamer.Clean(top.Text), top.Source)
                                          : ("", DescSource.None);
                editable = true;
                var rel = FolderNamer.NewFolderRel(g.Start, description);
                var full = Path.Join(s.VideoRoot, rel);
                var existing = lib.Folders.FirstOrDefault(f => PathCmp.Equals(f.Ref.FullPath.TrimEnd('\\'), full));
                if (existing is not null)
                {
                    target = new Append(existing.Ref, Confidence.High, FolderNamer.FolderExistsWhy, null);
                    description = existing.Ref.Description;
                    descSource = DescSource.ExistingFolder;
                    issues.Add(new Issue(IssueSeverity.Info, IssueCode.FolderExistsAppending, FolderNamer.FolderExistsWhy, anchor, [], false));
                }
                else
                {
                    target = new NewFolder(rel);
                    if (description.Length == 0)
                        issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.EmptyFolderName, "Name this folder", anchor,
                                             [new QuickFix("Name it", [])], false));
                    var longest = g.Videos.Where(v => included.Contains(v.Raw.Unit.Id)).Select(v => v.Raw.Name)
                                   .DefaultIfEmpty(g.Videos[0].Raw.Name).MaxBy(n => n.Length)!;
                    var tmp = FolderNamer.TempPath(Path.Join(full, longest));
                    if (tmp.Length > FolderNamer.MaxTempPath)
                        issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.TempPathTooLong,
                                             $"Path too long for OneDrive ({tmp.Length} > {FolderNamer.MaxTempPath} characters): {tmp}", anchor, [], false));
                }
                break;
            }
            case Append a:
                description = a.Folder.Description;
                descSource = DescSource.ExistingFolder;
                editable = a.Why == FolderNamer.FolderExistsWhy;
                break;
            case AlreadyImported ai:
                description = ai.Folder.Description;
                descSource = DescSource.ExistingFolder;
                break;
            default:
                description = "";
                descSource = DescSource.None;
                break;
        }

        // target issues and hints
        if (target is Append { Confidence: Confidence.Medium } medium)
            issues.Add(new Issue(IssueSeverity.Warning, IssueCode.MediumAppend, $"Appending to '{medium.Folder.Description}': {medium.Why}", anchor,
                                 medium.Hint is { } h ? [new QuickFix("New folder instead", h.Fix)] : [], true));
        if (target is Append { Hint: { } hint }) hints.Add(hint.Text);
        if (rt is null && target is NewFolder && decider.NewBeforeWallSplitPoint(g, included) is { } sp && g.Wall is { } wall)
            issues.Add(new Issue(IssueSeverity.Info, IssueCode.NewBeforeWallFolder,
                                 $"These clips start before '{wall.Description}' (dated {PlanText.ShortDate(wall.NameDate)})", anchor,
                                 [new QuickFix("Split here", [new SplitBefore(sp)])], false));

        // day splits
        var daySplits = DaySplitFinder.Find(g.Videos);
        foreach (var ds in daySplits.Where(d => d.Emphasised))
            issues.Add(new Issue(IssueSeverity.Warning, IssueCode.EmphasisedDaySplit,
                                 $"{PlanText.ShortDate(ds.From)} → {PlanText.ShortDate(ds.To)} · {PlanText.Miles(ds.Apart!.Value)} apart: likely separate outing",
                                 ds.FirstOfDay, [new QuickFix("Split here", [new SplitBefore(ds.FirstOfDay)])], true));
        if (daySplits.Any(d => d.Emphasised))
        {
            var days = g.Videos.Select(v => v.Time.LocalDate).Distinct().Count();
            var apart = daySplits.Where(d => d.Emphasised).Max(d => d.Apart!.Value.Meters);
            hints.Add($"{PlanText.Count(days, "day", "days")} · {PlanText.Miles(new Distance(apart))} apart");
        }

        // pin issues
        if (rt is not null)
        {
            if (rtConflict)
                issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.ConflictingPins,
                    $"Two choices for this group: '{ChoiceLabel(retargets[0].Choice)}' vs '{ChoiceLabel(retargets[1].Choice)}'", anchor,
                    [new QuickFix("Use first", [retargets[0] with { PinnedMembers = memberIds }]),
                     new QuickFix("Use second", [retargets[1] with { PinnedMembers = memberIds }])], false));
            else if (targetPin is { MembershipChanged: true })
                issues.Add(new Issue(IssueSeverity.Warning, IssueCode.PinMembershipChanged,
                    $"{ChoiceLabel(rt.Choice)} chosen for {rt.PinnedMembers.Length} clips; group now has {memberIds.Length}", anchor,
                    [new QuickFix("Keep", [rt with { PinnedMembers = memberIds }]),
                     new QuickFix("Reset to Auto", [new Retarget(anchor, new AutoTarget(), false, [])])], true));
        }
        if (rn is not null)
        {
            if (rnConflict)
                issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.ConflictingPins,
                    $"Two choices for this group: '{renames[0].Description}' vs '{renames[1].Description}'", anchor,
                    [new QuickFix("Use first", [renames[0] with { PinnedMembers = memberIds }]),
                     new QuickFix("Use second", [renames[1] with { PinnedMembers = memberIds }])], false));
            else if (namePin is { MembershipChanged: true })
                issues.Add(new Issue(IssueSeverity.Warning, IssueCode.PinMembershipChanged,
                    $"'{rn.Description}' chosen for {rn.PinnedMembers.Length} clips; group now has {memberIds.Length}", anchor,
                    [new QuickFix("Keep", [rn with { PinnedMembers = memberIds }]),
                     new QuickFix("Reset to Auto", [new Rename(anchor, null, [])])], true));
        }

        var foldable = target is AlreadyImported
            && !g.Videos.Any(v => v.Newness is Conflict or IsNew || (v.Flags & (ItemFlags.Truncated | ItemFlags.ProbeFailed)) != 0);
        var colorIndex = target is AlreadyImported ? -1 : color++ % 10;
        var spread = PlanningGeo.MaxPairwise(g.Videos.Where(v => v.Gps is not null).Select(v => v.Gps!.Point).ToList());

        return new VideoGroup(g.Id, memberIds, g.Centroid, spread, g.Start, g.End, g.Wall, target, targetPin,
                              description, descSource, editable, namePin, [.. suggestions], daySplits, hints.ToImmutable(), foldable, colorIndex);
    }

    private static void SharedTargets(ImmutableArray<VideoGroup>.Builder groups, ImmutableArray<Issue>.Builder issues)
    {
        static string? Key(VideoGroup v) => v.Target switch
        {
            Append a => "A:" + a.Folder.FullPath.TrimEnd('\\').ToUpperInvariant(),
            NewFolder n when v.Description.Length > 0 => "N:" + n.RelPath.ToUpperInvariant(),
            _ => null,
        };
        var byKey = Enumerable.Range(0, groups.Count).Where(i => Key(groups[i]) is not null).GroupBy(i => Key(groups[i])!);
        foreach (var set in byKey.Where(s => s.Count() > 1))
        {
            var idx = set.ToList();
            foreach (var i in idx)
            {
                var other = groups[idx.First(j => j != i)];
                var me = groups[i];
                issues.Add(new Issue(IssueSeverity.Info, IssueCode.SharedTarget,
                    $"Also targeted by {PlanText.DateRange(other.Start, other.End)}; both land in the same folder", me.Id.Anchor,
                    [new QuickFix("Merge", [new Merge(me.Id.Anchor, other.Id.Anchor)])], false));
            }
        }
    }

    private static void ItemIssues(PlanBase b, ImmutableArray<Issue>.Builder issues)
    {
        foreach (var i in b.Items)
        {
            var src = i.Time.Source;
            if (i.Flags.HasFlag(ItemFlags.CheckDate))
            {
                var window = src is TimeSource.Mtime or TimeSource.ExifWithOffset ? 60 : 75;
                issues.Add(new Issue(IssueSeverity.Warning, IssueCode.CheckDate,
                    $"Check date: {PlanText.LocalTime(i.Time.CaptureUtc, i.Time.TzId)} is within {window} min of midnight ({src})",
                    i.Raw.Unit.Id, [], false));
            }
            if (i.Flags.HasFlag(ItemFlags.ClockNotSet))
                issues.Add(new Issue(IssueSeverity.Warning, IssueCode.ClockNotSet,
                    $"Clock not set: {i.Time.CaptureUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC ({src})",
                    i.Raw.Unit.Id, [], false));
        }
    }

    private static void PlanIssues(PlanBase b, SessionFlags flags, Roots roots, HashSet<ItemId> included, ImmutableArray<Issue>.Builder issues)
    {
        var s = b.Scan.Settings;
        var ledger = b.Scan.Ledger;

        if (b.Clock.MismatchItems > 0 && b.Items.Any(i => i.Flags.HasFlag(ItemFlags.ClockMismatch)))
            issues.Add(new Issue(IssueSeverity.Info, IssueCode.ClockMismatch, ClockMismatchMessage(b), null, [], false));

        bool Eq(string a, string c) => PathCmp.Equals(a.TrimEnd('\\'), c.TrimEnd('\\'));
        var photoIncluded = b.Items.Any(i => i.Raw.Kind != ItemKind.Video && included.Contains(i.Raw.Unit.Id));
        var missing = roots.Unavailable.ToList();
        if (roots.VideoMissing && !missing.Any(r => Eq(r, s.VideoRoot))) missing.Insert(0, s.VideoRoot);
        foreach (var r in missing)
        {
            var blocking = Eq(r, s.VideoRoot) || (Eq(r, s.PhotoRoot) && photoIncluded);
            issues.Add(new Issue(blocking ? IssueSeverity.Blocking : IssueSeverity.Warning, IssueCode.RootMissing,
                                 $"{r} is not available; its items are unticked", null, [], false));
        }
        if (!s.RootsConfirmed)
            issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.RootsUnconfirmed,
                                 "Confirm the video and photo folders (settings were recovered)", null, [new QuickFix("Open Settings", [])], false));

        foreach (var p in ledger.ParseIssues)
        {
            var msg = $"Ledger line {p.File}:{p.Line} can't be read: {p.Reason}";
            issues.Add(flags.LedgerIssuesAccepted
                ? new Issue(IssueSeverity.Warning, IssueCode.LedgerParseIssue, msg, null, [], true)
                : new Issue(IssueSeverity.Blocking, IssueCode.LedgerParseIssue, msg, null, [new QuickFix("Accept and continue", [])], false));
        }

        var st = ledger.Status;
        var display = $@"{Path.GetFileName(s.VideoRoot.TrimEnd('\\'))}\.uas-sort";
        if (st.State == LedgerFolderState.CloudOnly || !st.CloudOnlyFiles.IsDefaultOrEmpty)
            issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.LedgerCloudOnly, $"Set {display} to Always keep on this device", null,
                                 [new QuickFix("Keep on this device", [])], false));
        if (st.State == LedgerFolderState.Unwritable || (st.Exists && !st.Writable))
            issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.LedgerUnwritable, $"Can't write the history file in {st.Folder}", null, [], false));
        if (st.State == LedgerFolderState.NotPinned)
            issues.Add(new Issue(IssueSeverity.Warning, IssueCode.LedgerNotPinned, $"Set {display} to Always keep on this device", null,
                                 [new QuickFix("Keep on this device", [])], false));
        if (st.State is LedgerFolderState.Missing or LedgerFolderState.Empty)
            issues.Add(new Issue(IssueSeverity.Info, IssueCode.LedgerNoHistory, "No history yet; this offload starts it", null, [], false));

        if (b.Items.All(i => i.Newness is Imported or Decided))
            issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.NothingNew,
                                 "Nothing new on this card: every clip and photo is already in your library or history", null, [], false));
    }

    private static string ClockMismatchMessage(PlanBase b)
    {
        var c = b.Scan.Clock;
        var flagged = b.Items.Where(i => i.Flags.HasFlag(ItemFlags.ClockMismatch)).ToList();
        var first = flagged[0];
        var offset = first.Raw.DroneStamp is { } stamp ? c.OffsetAt(stamp, first.Time.TzId) : c.Modal ?? TimeSpan.Zero;
        var clock = PlanText.Offset(offset) + (c.Mode == ClockMode.Zone && c.ZoneId is { } z ? $" ({z})" : "");
        var sites = flagged.GroupBy(i => i.Time.TzId, StringComparer.Ordinal)
            .Select(g => $"{PlanText.ZoneName(g.Key)} ({PlanText.Offset(PlanText.Zone(g.Key).GetUtcOffset(g.First().Time.CaptureUtc))})");
        return $"Drone clock is set to {clock}, but footage on this card was shot in {string.Join(", ", sites)}. "
             + "Dates here use local time at each site. To fix the drone clock: RC 2 → Settings → System → Date & time → turn off the "
             + "network-provided time zone and set the zone for where you are flying. Network time zones can be wrong on ship or hotel "
             + "Wi-Fi, and the RC keeps the last one it saw until it reconnects.";
    }
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PlannerDeriveTests"`
Expected: PASS (all facts and theory rows).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Planning/Planner.Derive.cs tests/UasSort.Testing/Planning/PlanScenario.Planner.cs tests/UasSort.Core.Tests/Planning/PlannerDeriveTests.cs
git commit -m "feat: Planner.Derive with inclusion, two-pass decisions, naming, pins and issue catalogue

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.13: Ported spike tests #1–#21 (time and clustering)

Port of `docs/research/spikes/grouping/test_grouping.py` classes `Normalisation` and `Clustering` per the Ref §13 table (#8 `test_autel_floating_time_no_gps` is **dropped** with the Autel card path). Every scenario runs through `PlanScenario` → `Planner.Prepare` → `Planner.Derive`, so these tests pin Parts 04–06 together.

**Files:**
- Create: `tests/UasSort.Testing/Planning/PlanAsserts.cs`
- Test: `tests/UasSort.Core.Tests/Planning/PortedClusteringTests.cs`

**Interfaces:**
- Consumes: `PlanScenario`, `Clip`, `Sites` (06.2), `Planner` (06.11–06.12), `FolderNamer` (06.3).
- Produces (Testing, defined here): `static class PlanAsserts { string[][] Ids(Plan p); Item ItemOf(PlanBase b, RawItem r); VideoGroup GroupOf(Plan p, RawItem r); IEnumerable<IssueCode> Codes(Plan p); }` — `Ids` returns each group's 14-digit filename stamps, like the spike's `ids()`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Testing/Planning/PlanAsserts.cs
using UasSort.Core.Planning;

namespace UasSort.Testing.Planning;

public static class PlanAsserts
{
    public static string[][] Ids(Plan p) =>
        p.Groups.Select(g => g.Videos.Select(id => PlanKeys.FileName(id.CardRelPath)[4..18]).ToArray()).ToArray();

    public static Item ItemOf(PlanBase b, RawItem r) => b.Items.Single(i => i.Raw.Unit.Id == r.Id());

    public static VideoGroup GroupOf(Plan p, RawItem r) => p.Groups.Single(g => g.Videos.Contains(r.Id()));

    public static IEnumerable<IssueCode> Codes(Plan p) => p.Issues.Select(i => i.Code);
}
```

```csharp
// tests/UasSort.Core.Tests/Planning/PortedClusteringTests.cs
using UasSort.Core.Naming;
using UasSort.Testing.Planning;
using static UasSort.Testing.Planning.PlanAsserts;

namespace UasSort.Core.Tests.Planning;

/// <summary>Ref §13 table rows #1–#21 (spike: test_grouping.py Normalisation + Clustering).</summary>
public sealed class PortedClusteringTests
{
    private static readonly GeoPoint SiteW = Sites.Anvil;
    private static readonly GeoPoint SiteE70 = new(64.5627, -165.3696 + 2.36);    // ~70.0 mi east of SiteW

    private static (PlanBase B, Plan P) Run(Tuning t, params RawItem[] card)
    {
        var b = new PlanScenario().Card(card).Prepare();
        return (b, PlanScenario.Derive(b, t));
    }

    private static Plan Plan(Tuning t, params RawItem[] card) => Run(t, card).P;
    private static readonly Tuning R50 = new(50, 1);
    private static readonly Tuning R25 = new(25, 1);

    [Fact] // #1
    public void Midnight_LocalDateFromSiteZone()
    {
        var p = Plan(R50, Clip.Vid("20260726035000", 1, Sites.Anvil), Clip.Vid("20260726041000", 2, Sites.Anvil));
        var g = Assert.Single(p.Groups);
        Assert.Equal(new DateOnly(2026, 7, 25), g.Start);
        Assert.Equal(@"2026\2026-07\2026-07-25 Anvil", FolderNamer.NewFolderRel(g.Start, "Anvil"));
    }

    [Fact] // #2
    public void Midnight_G0_HoursGuardKeepsOneGroup()
        => Assert.Single(Plan(new Tuning(50, 0), Clip.Vid("20260726035000", 1, Sites.Anvil), Clip.Vid("20260726041000", 2, Sites.Anvil)).Groups);

    [Fact] // #3
    public void Dng_UsesClockZoneAndSiteZone()
    {
        var dng = Clip.Dng("20260726035500", 2, Sites.Anvil);
        var (b, _) = Run(R50, Clip.Vid("20260726035000", 1, Sites.Anvil), dng);
        var d = ItemOf(b, dng);
        Assert.Equal(("America/New_York", TimeSource.DroneClockZone), (b.Scan.Clock.ZoneId, d.Time.Source));
        Assert.Equal(new DateTime(2026, 7, 26, 7, 55, 0, DateTimeKind.Utc), d.Time.CaptureUtc);
        Assert.Equal(new DateOnly(2026, 7, 25), d.Time.LocalDate);
    }

    [Fact] // #4
    public void Hawaii_SiteZoneNotPcZone()
    {
        var v = Clip.Vid("20260301053000", 1, Sites.Makaha);
        var i = ItemOf(Run(R50, v).B, v);
        Assert.Equal(("Pacific/Honolulu", new DateOnly(2026, 2, 28)), (i.Time.TzId, i.Time.LocalDate));
    }

    [Fact] // #5 (Changed: the zone learner handles the DST change)
    public void ZoneLearner_DstChangeStillFitsNewYork()
    {
        var dng = Clip.Dng("20261110121000", 3, Sites.Anvil);
        var (b, _) = Run(R50, Clip.Vid("20261020120000", 1, Sites.Anvil, clockMinusUtcHours: -4),
                              Clip.Vid("20261110120000", 2, Sites.Anvil, clockMinusUtcHours: -5), dng);
        Assert.Equal((ClockMode.Zone, "America/New_York"), (b.Scan.Clock.Mode, b.Scan.Clock.ZoneId));
        var d = ItemOf(b, dng);
        Assert.Equal(TimeSource.DroneClockZone, d.Time.Source);
        Assert.Equal(new DateTime(2026, 11, 10, 17, 10, 0, DateTimeKind.Utc), d.Time.CaptureUtc);
    }

    [Theory] // #6 (Changed: Setting mode always has a zone; never Mtime)
    [InlineData(true)]
    [InlineData(false)]
    public void NoMp4_UsesStoredZone(bool explicitZone)
    {
        var dng = Clip.Dng("20260927150000", 1, Sites.Zachar);
        var s = explicitZone ? new PlanScenario { ClockMode = StoredClockMode.Zone, ClockZone = "America/New_York" } : new PlanScenario();
        var b = s.Card(dng).Prepare();
        var d = ItemOf(b, dng);
        Assert.Equal(ClockMode.Setting, b.Scan.Clock.Mode);
        Assert.Equal(TimeSource.DroneClockSetting, d.Time.Source);
        Assert.True(d.Flags.HasFlag(ItemFlags.ClockFromSetting));
        Assert.True(d.Flags.HasFlag(ItemFlags.ClockMismatch));
        Assert.Equal(new DateTime(2026, 9, 27, 19, 0, 0, DateTimeKind.Utc), d.Time.CaptureUtc);
    }

    [Fact] // #7
    public void Truncated_TimedByClockAndJoins()
    {
        var t = Clip.Vid("20260727002013", 14, Sites.Anvil, moov: false);
        var (b, p) = Run(R50, Clip.Vid("20260726235645", 1, Sites.Anvil), t, Clip.Vid("20260727002118", 15, Sites.Anvil));
        var i = ItemOf(b, t);
        Assert.Equal(TimeSource.DroneClockZone, i.Time.Source);
        Assert.Equal(new DateTime(2026, 7, 27, 4, 20, 13, DateTimeKind.Utc), i.Time.CaptureUtc);
        Assert.True(i.Flags.HasFlag(ItemFlags.Truncated));
        Assert.True(i.Flags.HasFlag(ItemFlags.ClockMismatch));
        Assert.Single(p.Groups);
    }

    [Fact] // #9
    public void Trip_AcrossMonth()
    {
        var g = Assert.Single(Plan(R50, Clip.Vid("20260731230000", 1, Sites.Anvil), Clip.Vid("20260801230000", 2, new GeoPoint(64.58, -165.40)),
                                   Clip.Vid("20260802230000", 3, Sites.Anvil)).Groups);
        Assert.Equal(@"2026\2026-07\2026-07-31 Trip", FolderNamer.NewFolderRel(g.Start, "Trip"));
    }

    [Fact] // #10
    public void Trip_AcrossYear()
    {
        var g = Assert.Single(Plan(R50, Clip.Vid("20261231230000", 1, Sites.Anvil), Clip.Vid("20270101230000", 2, Sites.Anvil)).Groups);
        Assert.Equal(@"2026\2026-12\2026-12-31 NY", FolderNamer.NewFolderRel(g.Start, "NY"));
    }

    [Fact] // #11
    public void TwoDayGap_Splits()
    {
        var p = Plan(R50, Clip.Vid("20260722230000", 1, Sites.Anvil), Clip.Vid("20260725230000", 2, Sites.Anvil));
        Assert.Equal(2, p.Groups.Length);
        Assert.Equal(BoundaryCause.DayGap, Assert.Single(p.Boundaries).Cause);
    }

    private static RawItem[] CouncilAnvil() =>
    [
        Clip.Vid("20260725232655", 117, Sites.Council), Clip.Vid("20260726022937", 118, Sites.Council),
        Clip.Vid("20260726235645", 1, Sites.Anvil), Clip.Vid("20260727000012", 2, Sites.Anvil),
    ];

    [Fact] // #12 (R-dependent) at 50 mi
    public void CouncilAnvil_R50_OneGroupEmphasisedSplit()
    {
        var card = CouncilAnvil();
        var p = Plan(R50, card);
        var g = Assert.Single(p.Groups);
        Assert.Equal(4, g.Videos.Length);
        var ds = Assert.Single(g.DaySplits);
        Assert.Equal(card[2].Id(), ds.FirstOfDay);
        Assert.Equal(33.9, ds.Apart!.Value.Miles, 1);
        Assert.True(ds.Emphasised);
        var w = Assert.Single(p.Issues, i => i.Code == IssueCode.EmphasisedDaySplit);
        Assert.Equal(IssueSeverity.Warning, w.Severity);
    }

    [Fact] // #12 (R-dependent) at 25 mi
    public void CouncilAnvil_R25_TwoGroups()
    {
        var p = Plan(R25, CouncilAnvil());
        Assert.Equal(new[] { new[] { "20260725232655", "20260726022937" }, new[] { "20260726235645", "20260727000012" } }, Ids(p));
        Assert.Equal(BoundaryCause.Distance, Assert.Single(p.Boundaries).Cause);
    }

    [Fact] // #13
    public void Kodiak_MultiDayJoins()
    {
        var g = Assert.Single(Plan(R50, Clip.Vid("20260523015251", 40, Sites.KodiakTown), Clip.Vid("20260523201928", 52, Sites.KodiakTown),
                                   Clip.Vid("20260524190521", 64, new GeoPoint(57.75, -152.50)), Clip.Vid("20260525092718", 85, Sites.KodiakTown)).Groups);
        Assert.Equal((new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 25)), (g.Start, g.End));
    }

    [Fact] // #14 (53.4 mi > 50; the 3.4 mi margin is pinned)
    public void ZacharKodiak_SameDay_R50_Splits()
    {
        Assert.Equal(53.4, UasSort.Core.Planning.PlanningGeo.Haversine(Sites.Zachar, Sites.KodiakTown).Miles, 1);
        var p = Plan(R50, Clip.Vid("20260927140127", 123, Sites.Zachar), Clip.Vid("20260927140144", 124, Sites.Zachar),
                     Clip.Vid("20260927190000", 150, Sites.KodiakTown));
        Assert.Equal(2, p.Groups.Length);
        Assert.All(p.Groups, g => Assert.Equal(new DateOnly(2026, 9, 27), g.Start));
    }

    [Fact] // #15
    public void SameDay_ABA_NotReMerged()
        => Assert.Equal(3, Plan(R50, Clip.Vid("20260927140127", 1, Sites.Zachar), Clip.Vid("20260927190000", 2, Sites.KodiakTown),
                                Clip.Vid("20260927230000", 3, Sites.Zachar)).Groups.Length);

    [Fact] // #16 + other explicit R values (Nome A+B 7.5 mi, Newport AM+PM)
    public void NearSites_Join()
    {
        Assert.Single(Plan(R50, Clip.Vid("20260510104103", 2, Sites.NewportAm), Clip.Vid("20260510193745", 30, Sites.NewportPm)).Groups);
        Assert.Single(Plan(R50, Clip.Vid("20260704010948", 101, Sites.NomeA), Clip.Vid("20260704013615", 102, Sites.NomeB)).Groups);
        Assert.Single(Plan(new Tuning(8, 1), Clip.Vid("20260704010948", 101, Sites.NomeA), Clip.Vid("20260704013615", 102, Sites.NomeB)).Groups);
    }

    [Fact] // #17 (R-dependent)
    public void NoGps_NearerNeighbour_R25()
        => Assert.Equal(new[] { new[] { "20260725232655" }, new[] { "20260726234000", "20260726235645" } },
            Ids(Plan(R25, Clip.Vid("20260725232655", 117, Sites.Council), Clip.Vid("20260726234000", 99), Clip.Vid("20260726235645", 1, Sites.Anvil))));

    [Fact] // #17 duplicated on a synthetic pair ~70 mi apart, at R 50
    public void NoGps_NearerNeighbour_Synthetic70mi()
        => Assert.Equal(new[] { new[] { "20260725232655" }, new[] { "20260726234000", "20260726235645" } },
            Ids(Plan(R50, Clip.Vid("20260725232655", 117, SiteW), Clip.Vid("20260726234000", 99), Clip.Vid("20260726235645", 1, SiteE70))));

    private static readonly DateTime S1 = new(2026, 7, 26, 3, 20, 0, DateTimeKind.Utc);

    [Fact] // #18 (R-dependent)
    public void NoGps_SessionBeatsTime_R25()
        => Assert.Equal(new[] { new[] { "20260725232655", "20260726234000" }, new[] { "20260726235645" } },
            Ids(Plan(R25, Clip.Vid("20260725232655", 117, Sites.Council, session: S1), Clip.Vid("20260726234000", 99, session: S1),
                     Clip.Vid("20260726235645", 1, Sites.Anvil))));

    [Fact] // #18 synthetic duplicate at R 50
    public void NoGps_SessionBeatsTime_Synthetic70mi()
        => Assert.Equal(new[] { new[] { "20260725232655", "20260726234000" }, new[] { "20260726235645" } },
            Ids(Plan(R50, Clip.Vid("20260725232655", 117, SiteW, session: S1), Clip.Vid("20260726234000", 99, session: S1),
                     Clip.Vid("20260726235645", 1, SiteE70))));

    [Fact] // session-link regression: only GPS-bearing members' sessions block a distance split
    public void SessionLink_OnlyGpsBearingMembers_R25()
    {
        var s2 = new DateTime(2026, 7, 27, 3, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new[] { new[] { "20260725232655" }, new[] { "20260726234000", "20260726235645" } },
            Ids(Plan(R25, Clip.Vid("20260725232655", 117, Sites.Council, session: S1), Clip.Vid("20260726234000", 99, session: s2),
                     Clip.Vid("20260726235645", 1, Sites.Anvil, session: s2))));
    }

    [Fact] // #19
    public void NoGps_FirstClipOfNewSessionFollowsSession()
    {
        var s1 = new DateTime(2026, 9, 27, 17, 55, 0, DateTimeKind.Utc);
        var s2 = new DateTime(2026, 9, 27, 18, 18, 0, DateTimeKind.Utc);
        Assert.Equal(new[] { new[] { "20260927140000" }, new[] { "20260927142000", "20260927150000" } },
            Ids(Plan(R50, Clip.Vid("20260927140000", 1, Sites.Zachar, session: s1), Clip.Vid("20260927142000", 2, session: s2),
                     Clip.Vid("20260927150000", 3, Sites.KodiakTown, session: s2))));
    }

    [Fact] // #20
    public void AllNoGps_TimeOnly()
        => Assert.Equal(2, Plan(R50, Clip.Vid("20260725232655", 1), Clip.Vid("20260726235645", 2), Clip.Vid("20260729120000", 3)).Groups.Length);

    [Fact] // #21 (Changed: miles; G case added)
    public void R_And_G_AreParameters()
    {
        RawItem[] card = [Clip.Vid("20260725232655", 117, Sites.Council), Clip.Vid("20260726235645", 1, Sites.Anvil)];
        Assert.Single(Plan(new Tuning(40, 1), card).Groups);
        var r25 = Plan(R25, card);
        Assert.Equal((2, BoundaryCause.Distance), (r25.Groups.Length, r25.Boundaries[0].Cause));
        var g0 = Plan(new Tuning(50, 0), card);
        Assert.Equal((2, BoundaryCause.DayGap), (g0.Groups.Length, g0.Boundaries[0].Cause));
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PortedClusteringTests"`
Expected: build error CS0103 (`PlanAsserts` / `Ids` / `ItemOf` not found) until the helper exists.

- [ ] **Step 3: Implement**

Create `tests/UasSort.Testing/Planning/PlanAsserts.cs` with the code shown in Step 1. No production code changes are expected; if a ported row fails, the failure names the component (TimeResolver/DroneClock → Part 04; Clusterer/Planner → this part) — fix it there, never the expectation (the expectations are the Ref §13 table).

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PortedClusteringTests"`
Expected: PASS (22 tests incl. theory rows).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Testing/Planning/PlanAsserts.cs tests/UasSort.Core.Tests/Planning/PortedClusteringTests.cs
git commit -m "test: port spike time and clustering tests #1-#21 to the approved model

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.14: Ported spike tests #22–#35 (decisions) and the remaining new planning cases

Port of the spike's `Decisions` class per the Ref §13 table; #36 lives in `VideoNewnessTests` (06.4) and #37 in `FolderNamerTests` (06.3). String mapping: `high (date + location)` → `High` with Why "same dates, &lt;0.1 mi"; `medium…` → `Medium` with the §8.5 Why; `AppendSplit` → two groups split by a `LibraryFolder` wall, each an Append; `PhotosOnly` → `PhotoDays`; `/` → `\`.

**Files:**
- Create: `tests/UasSort.Testing/Planning/DecisionScenarios.cs`
- Modify: `src/UasSort.Core/Planning/Planner.Derive.cs` (add `AppendCandidates(Plan, GroupId, int)`)
- Test: `tests/UasSort.Core.Tests/Planning/PortedDecisionTests.cs`

**Interfaces:**
- Consumes: 06.2–06.13.
- Produces:
  - `Planner.AppendCandidates(Plan plan, GroupId id, int max = 8) → IReadOnlyList<LibraryFolder>` (static; rebuilds the plan's drafts without re-clustering and asks `FolderDecider.AppendCandidates`; used by the group card's target dropdown in Part 10).
  - Testing: `static class DecisionScenarios { const string Zrel; RawItem[] Z; PlanScenario ZLibrary(); PlanScenario ZWithLedger(); }`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Testing/Planning/DecisionScenarios.cs

namespace UasSort.Testing.Planning;

/// <summary>The spike's Decisions fixture: Z = vid 0123, 0124, 0148 at ZACHAR; ZREL = 2026\2026-09\2026-09-27 Zachar Bay.</summary>
public static class DecisionScenarios
{
    public const string Zrel = @"2026\2026-09\2026-09-27 Zachar Bay";

    public static RawItem[] Z =>
    [
        Clip.Vid("20260927140127", 123, Sites.Zachar), Clip.Vid("20260927140144", 124, Sites.Zachar), Clip.Vid("20260927142416", 148, Sites.Zachar),
    ];

    public static PlanScenario ZLibrary() => new PlanScenario().Library(Zrel, Z);

    public static PlanScenario ZWithLedger() =>
        Z.Aggregate(ZLibrary(), (s, z) => s.LedgerFile(z, Zrel, Sites.Zachar, "America/Anchorage"));
}
```

```csharp
// tests/UasSort.Core.Tests/Planning/PortedDecisionTests.cs
using UasSort.Core.Planning;
using UasSort.Testing.Planning;
using static UasSort.Testing.Planning.DecisionScenarios;
using static UasSort.Testing.Planning.PlanAsserts;

namespace UasSort.Core.Tests.Planning;

/// <summary>Ref §13 table rows #22–#35 (spike: test_grouping.py Decisions) + new planning cases.</summary>
public sealed class PortedDecisionTests
{
    private static Plan P(PlanScenario s, params PlanEdit[] edits) => PlanScenario.Derive(s.Prepare(), edits: edits);
    private static string Full(string rel) => $@"{PlanScenario.VideoRoot}\{rel}";

    [Fact] // #22
    public void EmptyLibrary_NewFolder()
    {
        var p = P(new PlanScenario().Card(Z));
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-27"), Assert.Single(p.Groups).Target);
        Assert.Contains(p.Issues, i => i.Code == IssueCode.EmptyFolderName && i.Severity == IssueSeverity.Blocking);
        Assert.Empty(p.Base.PhotoDays);
    }

    [Fact] // #23
    public void AppendToday_ViaCardLeftovers()
    {
        var p = P(ZLibrary().Card([.. Z, Clip.Vid("20260927160000", 160, Sites.Zachar), Clip.Vid("20260927161000", 161, Sites.Zachar)]));
        var g = Assert.Single(p.Groups);
        Assert.Equal(Full(Zrel), g.Wall!.FullPath);
        var a = Assert.IsType<Append>(g.Target);
        Assert.Equal((Full(Zrel), Confidence.High, "same day as clips already in this folder"), (a.Folder.FullPath, a.Confidence, a.Why));
    }

    [Fact] // #24
    public void AppendToday_ViaLedgerCentroid()
    {
        var a = Assert.IsType<Append>(Assert.Single(P(ZWithLedger().Card(Clip.Vid("20260927160000", 160, Sites.Zachar))).Groups).Target);
        Assert.Equal((Full(Zrel), Confidence.High, "same dates, <0.1 mi"), (a.Folder.FullPath, a.Confidence, a.Why));
    }

    [Fact] // #25
    public void AppendToday_LocationUnknown_Medium()
    {
        var a = Assert.IsType<Append>(Assert.Single(P(ZLibrary().Card(Clip.Vid("20260927160000", 160, Sites.Zachar))).Groups).Target);
        Assert.Equal((Confidence.Medium, "same dates, location unknown"), (a.Confidence, a.Why));
    }

    [Fact] // #26
    public void NextDay_Far_NewFolder()
    {
        var p = P(ZWithLedger().Card(Clip.Vid("20260928180000", 170, Sites.KodiakTown)));
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-28"), Assert.Single(p.Groups).Target);
        Assert.Contains(p.Issues, i => i.Code == IssueCode.EmptyFolderName);
    }

    [Fact] // #27
    public void NextDay_SamePlace_AppendHigh()
    {
        var a = Assert.IsType<Append>(Assert.Single(P(ZWithLedger().Card(Clip.Vid("20260928180000", 170, Sites.Zachar))).Groups).Target);
        Assert.Equal((Full(Zrel), Confidence.High, "next day, <0.1 mi"), (a.Folder.FullPath, a.Confidence, a.Why));
    }

    [Fact] // #28
    public void NextDay_LocationUnknown_NewFolder()
    {
        var p = P(ZLibrary().Card(Clip.Vid("20260928180000", 170, Sites.Zachar)));
        var g = Assert.Single(p.Groups);
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-28"), g.Target);
        Assert.Contains(Planner.AppendCandidates(p, g.Id), f => f.Ref.FullPath == Full(Zrel));
    }

    [Fact] // #29
    public void NeverAppendBeforeFolderNameDate()
        => Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-26"),
                        Assert.Single(P(ZLibrary().Card(Clip.Vid("20260926180000", 170, Sites.Zachar))).Groups).Target);

    [Fact] // #30 (Changed: rewritten for the approved model)
    public void LegacyDepth_DjiAppendsBesideAutelMembers()
    {
        const string makaha = @"2022\2022-03-27 Makaha Valley";
        var mtimes = new[] { new DateTime(2022, 3, 27, 15, 1, 0, DateTimeKind.Utc), new DateTime(2022, 3, 27, 15, 2, 0, DateTimeKind.Utc),
                             new DateTime(2022, 3, 27, 15, 4, 0, DateTimeKind.Utc) };
        var sizes = new long[] { 61_000_000, 62_000_000, 64_000_000 };
        var names = new[] { "MAX_0061.MP4", "MAX_0062.MP4", "MAX_0064.MP4" };
        var s = new PlanScenario();
        var autel = new List<RawItem>();
        for (var i = 0; i < 3; i++)
        {
            s.LibraryFile($@"{makaha}\{names[i]}", sizes[i], mtimes[i]);
            autel.Add(Clip.Autel(names[i], sizes[i], mtimes[i]));
        }
        var b = s.Card([.. autel, Clip.Vid("20220327110500", 1, Sites.Makaha)]).Prepare();
        Assert.All(autel, a => Assert.Equal(TimeSource.Mtime, ItemOf(b, a).Time.Source));
        var g = Assert.Single(PlanScenario.Derive(b).Groups);
        Assert.Equal("Makaha Valley", g.Wall!.Description);
        var app = Assert.IsType<Append>(g.Target);
        Assert.Equal((Full(makaha), Confidence.High, "same day as clips already in this folder"), (app.Folder.FullPath, app.Confidence, app.Why));
    }

    [Fact] // #31 (Changed: AppendSplit → two groups split by a LibraryFolder wall)
    public void TwoWalls_TwoAppends()
    {
        var d1 = new[] { Clip.Vid("20260801200000", 1, Sites.Anvil), Clip.Vid("20260801201000", 2, Sites.Anvil) };
        var d2 = new[] { Clip.Vid("20260802200000", 3, Sites.Anvil), Clip.Vid("20260802203000", 5, Sites.Anvil) };
        var n9 = Clip.Vid("20260801202000", 9, Sites.Anvil);
        var n4 = Clip.Vid("20260802202000", 4, Sites.Anvil);
        var p = P(new PlanScenario().Library(@"2026\2026-08\2026-08-01 Anvil AM", d1).Library(@"2026\2026-08\2026-08-02 Anvil PM", d2)
                                    .Card([.. d1, .. d2, n9, n4]));
        Assert.Equal(2, p.Groups.Length);
        Assert.Equal(new[] { d1[0].Id(), d1[1].Id(), n9.Id() }, p.Groups[0].Videos);
        Assert.Equal(new[] { d2[0].Id(), n4.Id(), d2[1].Id() }, p.Groups[1].Videos);
        Assert.Equal(("Anvil AM", Confidence.High), (Assert.IsType<Append>(p.Groups[0].Target).Folder.Description, ((Append)p.Groups[0].Target).Confidence));
        Assert.Equal(("Anvil PM", Confidence.High), (Assert.IsType<Append>(p.Groups[1].Target).Folder.Description, ((Append)p.Groups[1].Target).Confidence));
        Assert.All(p.Groups, g => Assert.Equal("same day as clips already in this folder", ((Append)g.Target).Why));
        Assert.Equal(BoundaryCause.LibraryFolder, Assert.Single(p.Boundaries).Cause);
    }

    [Fact] // #32 (Changed)
    public void LeftoversCard_WallsAndPhotoDays()
    {
        var council = new[] { Clip.Vid("20260725232655", 117, Sites.Council), Clip.Vid("20260726022937", 118, Sites.Council) };
        var anvil = new[] { Clip.Vid("20260726235645", 1, Sites.Anvil), Clip.Vid("20260727000012", 2, Sites.Anvil) };
        var p116 = Clip.Dng("20260725233000", 116, Sites.Council);
        var p122 = Clip.Dng("20260927140000", 122, Sites.Zachar);
        var p119 = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var b = new PlanScenario().Library(@"2026\2026-07\2026-07-25 Council Road", council).Library(@"2026\2026-07\2026-07-26 Anvil Mountain", anvil)
            .Card([.. council, .. anvil, .. Z, p116, p122, p119]).Prepare();
        var p = PlanScenario.Derive(b);
        Assert.Equal(3, p.Groups.Length);
        Assert.Equal("Council Road", Assert.IsType<AlreadyImported>(p.Groups[0].Target).Folder.Description);
        Assert.Equal("Anvil Mountain", Assert.IsType<AlreadyImported>(p.Groups[1].Target).Folder.Description);
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-27"), p.Groups[2].Target);
        Assert.Equal(BoundaryCause.LibraryFolder, p.Boundaries[0].Cause);
        Assert.Contains(p.Issues, i => i.Code == IssueCode.EmptyFolderName && i.Anchor == p.Groups[2].Id.Anchor);
        Assert.Equal(new DateTime(2026, 7, 27, 4, 0, 12, DateTimeKind.Utc), b.WatermarkUtc);
        Assert.Equal("videos from this day are already in the library", Assert.IsType<ProbablyImported>(ItemOf(b, p116).Newness).Why);
        Assert.Equal(new IsNew(NewReason.AfterWatermark, null), ItemOf(b, p122).Newness);
        Assert.Equal(new IsNew(NewReason.AfterWatermark, null), ItemOf(b, p119).Newness);
        Assert.Equal(new[] { new DateOnly(2026, 7, 25), new DateOnly(2026, 8, 15), new DateOnly(2026, 9, 27) }, b.PhotoDays.Select(d => d.Date));
    }

    [Fact] // #33
    public void PhotoOnlyDay_BeforeWatermark_ProbablyImported()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var b = ZLibrary().Card(d).Prepare();
        Assert.Empty(PlanScenario.Derive(b).Groups);
        Assert.Equal("photo-only day before the last imported video (Sep 27)", Assert.IsType<ProbablyImported>(ItemOf(b, d).Newness).Why);
        Assert.Equal(ClockMode.Setting, b.Scan.Clock.Mode);
    }

    [Fact] // #34 (Changed: PhotosOnly → PhotoDays)
    public void PhotoOnlyCard_AfterWatermark_PhotoDays()
    {
        var ph = new[] { Clip.Dng("20260930200000", 200, Sites.Zachar), Clip.Dng("20260930200500", 201, Sites.Zachar) };
        var b = ZLibrary().Card(ph).Prepare();
        Assert.All(ph, x => Assert.Equal(new IsNew(NewReason.AfterWatermark, null), ItemOf(b, x).Newness));
        var day = Assert.Single(b.PhotoDays);
        Assert.Equal((new DateOnly(2026, 9, 30), "America/Anchorage", 2), (day.Date, day.TzId, day.Items.Length));
        Assert.Empty(PlanScenario.Derive(b).Groups);
    }

    [Fact] // #35 (Changed: mtime ±2 s; no-watermark rule)
    public void PanoSets_ImportedByFolderAndDistinct()
    {
        var m1 = new DateTime(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc);
        var m2 = new DateTime(2026, 5, 25, 13, 30, 31, DateTimeKind.Utc);
        var p87 = Clip.Set("001_0087", "2026-05-25 09:30:28", Sites.KodiakTown, ("PANO_0001.DNG", 13_751_808, m1), ("PANO_0002.DNG", 12_882_432, m2));
        var p112 = Clip.Set("001_0112", "2026-05-25 10:00:00", Sites.KodiakTown, ("PANO_0001.DNG", 13_751_808, m1.AddMinutes(30)), ("PANO_0002.DNG", 12_882_432, m2.AddMinutes(30)));
        var b = new PlanScenario()
            .LibraryFile(@"Picture Offload\001_0087\PANO_0001.DNG", 13_751_808, m1)
            .LibraryFile(@"Picture Offload\001_0087\PANO_0002.DNG", 12_882_432, m2)
            .Card(p87, p112).Prepare();
        Assert.IsType<Imported>(ItemOf(b, p87).Newness);
        Assert.Equal(SetResolution.Imported, b.Sets[p87.Id()].Resolution);
        Assert.Equal(new IsNew(NewReason.AfterWatermark, null), ItemOf(b, p112).Newness);
        Assert.Equal(("001_0112", SetResolution.Plain), (b.Sets[p112.Id()].FolderName, b.Sets[p112.Id()].Resolution));
    }

    [Fact] // Scenario D in miniature: after [New folder instead] the UserSplit rule makes the new day a NewFolder
    public void CrossDayFix_GivesAlreadyImportedPlusNewFolder()
    {
        var council = new[] { Clip.Vid("20260725232655", 117, Sites.Council), Clip.Vid("20260726022937", 118, Sites.Council) };
        var a1 = Clip.Vid("20260726235645", 1, Sites.Anvil);
        var b = new PlanScenario().Library(@"2026\2026-07\2026-07-25 Council Road", council)
            .Card([.. council, a1, Clip.Vid("20260727000012", 2, Sites.Anvil)]).Prepare();
        var before = PlanScenario.Derive(b);
        var fix = Assert.IsType<Append>(Assert.Single(before.Groups).Target).Hint!.Fix;
        var after = PlanScenario.Derive(b, edits: [.. fix]);
        Assert.IsType<AlreadyImported>(after.Groups[0].Target);
        Assert.Equal(new NewFolder(@"2026\2026-07\2026-07-26"), after.Groups[1].Target);
        Assert.Equal(BoundaryCause.UserSplit, after.Boundaries[0].Cause);
    }

    [Fact] // two-pass UserSplit: a Retarget pin on A never feeds B's rule
    public void PinOnEarlierGroup_DoesNotFeedUserSplitRule()
    {
        var a = Clip.Vid("20260927160000", 160, Sites.Zachar);
        var bClip = Clip.Vid("20260927161000", 161, Sites.Zachar);
        var b = ZWithLedger().Card(a, bClip).Prepare();
        var p = PlanScenario.Derive(b, edits: [new SplitBefore(bClip.Id()), new Retarget(a.Id(), new SkipTarget(), false, [a.Id()])]);
        Assert.IsType<SkipGroup>(p.Groups[0].Target);
        Assert.IsType<NewFolder>(p.Groups[1].Target);                 // still excluded: F was A's pass-1 target
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PortedDecisionTests"`
Expected: build error CS0117 (`Planner` has no `AppendCandidates`) and CS0103 (`DecisionScenarios` members not found).

- [ ] **Step 3: Implement**

Create `DecisionScenarios.cs` as shown in Step 1, and add to `src/UasSort.Core/Planning/Planner.Derive.cs` (inside the partial class):

```csharp
    /// <summary>Append candidates for a group of an existing plan (the target dropdown, Ref §9.4). Never re-clusters.</summary>
    public static IReadOnlyList<LibraryFolder> AppendCandidates(Plan plan, GroupId id, int max = 8)
    {
        var byId = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var drafts = plan.Groups.Select(g => new GroupDraft(g.Id, [.. g.Videos.Select(v => byId[v])], g.Centroid, g.Start, g.End, g.Wall, null))
                                .ToList();
        var draft = drafts.First(d => d.Id == id);
        return new FolderDecider(plan.Base.Scan.Library, drafts, plan.Tuning).AppendCandidates(draft, max);
    }
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PortedDecisionTests"`
Expected: PASS (16 tests).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Testing/Planning/DecisionScenarios.cs tests/UasSort.Core.Tests/Planning/PortedDecisionTests.cs src/UasSort.Core/Planning/Planner.Derive.cs
git commit -m "test: port spike decision tests #22-#35 and add cross-day and two-pass cases

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.15: EditValidator — refused edits

**Files:**
- Create: `src/UasSort.Core/Editing/EditValidator.cs`
- Test: `tests/UasSort.Core.Tests/Editing/EditValidatorTests.cs`

**Interfaces:**
- Consumes: `Plan`, `VideoGroup`, `Rejected`, `RejectReason`, `PlanEdit` cases, `LedgerPaths.For` (Part 02); `Planner.FolderRefFor` (06.12, internal — Core only); `PlanEditRefs` (06.1); `FolderNamer.FolderExistsWhy` (06.3).
- Produces: `static class EditValidator { Rejected? Validate(Plan current, PlanEdit edit); static bool IsUnder(string path, string root); }` — `null` = valid. Reasons (Ref §8.9 "Refused edits"): `ItemsNotFound`, `MergeAcrossLibraryFolders`, `MoveImportedItem`, `SplitAtGroupStart`, `RenameExistingFolder` (Append — except a "Folder exists; appending" group — or AlreadyImported), `RetargetIntoReservedFolder` (checked first: `<videoRoot>\.uas-sort` subtree, the photo root, any previous photo root, each including its subtree), `RetargetOutsideVideoRoot`, `RetargetLaterDatedFolderUnconfirmed`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Editing/EditValidatorTests.cs
using UasSort.Core.Editing;
using UasSort.Testing.Planning;
using static UasSort.Testing.Planning.DecisionScenarios;

namespace UasSort.Core.Tests.Editing;

public sealed class EditValidatorTests
{
    private static readonly RawItem C117 = Clip.Vid("20260725232655", 117, Sites.Council);
    private static readonly RawItem A1 = Clip.Vid("20260726235645", 1, Sites.Anvil);
    private static readonly RawItem A2 = Clip.Vid("20260727000012", 2, Sites.Anvil);

    private static RejectReason? Reason(Plan p, PlanEdit e) => EditValidator.Validate(p, e)?.Reason;

    [Fact]
    public void ItemsNotFound()
    {
        var p = PlanScenario.Derive(new PlanScenario().Card(Z).Prepare());
        Assert.Equal(RejectReason.ItemsNotFound, Reason(p, new SplitBefore(new ItemId("DCIM/DJI_001/nope.MP4"))));
        Assert.Null(Reason(p, new SetDayIncluded(new DateOnly(2026, 9, 27), false)));
    }

    [Fact]
    public void MergeAcrossWalls_IsRejected()
    {
        var am = Clip.Vid("20260801200000", 1, Sites.Anvil);
        var pm = Clip.Vid("20260802200000", 3, Sites.Anvil);
        var p = PlanScenario.Derive(new PlanScenario().Library(@"2026\2026-08\2026-08-01 Anvil AM", am)
            .Library(@"2026\2026-08\2026-08-02 Anvil PM", pm).Card(am, pm).Prepare());
        Assert.Equal(RejectReason.MergeAcrossLibraryFolders, Reason(p, new Merge(am.Id(), pm.Id())));
    }

    [Fact]
    public void MoveImported_AndSplitAtStart_AreRejected()
    {
        var n = Clip.Vid("20260927160000", 160, Sites.Zachar);
        var p = PlanScenario.Derive(ZLibrary().Card([.. Z, n]).Prepare());
        Assert.Equal(RejectReason.MoveImportedItem, Reason(p, new MoveToNewGroup([Z[1].Id()])));
        Assert.Equal(RejectReason.MoveImportedItem, Reason(p, new MoveToGroup([Z[1].Id()], n.Id())));
        Assert.Null(Reason(p, new MoveToNewGroup([n.Id()])));
        Assert.Equal(RejectReason.SplitAtGroupStart, Reason(p, new SplitBefore(Z[0].Id())));
        Assert.Null(Reason(p, new SplitBefore(n.Id())));
    }

    [Fact]
    public void RenameAppendOrAlreadyImported_IsRejected_NewFolderIsNot()
    {
        var appendPlan = PlanScenario.Derive(ZLibrary().Card([.. Z, Clip.Vid("20260927160000", 160, Sites.Zachar)]).Prepare());
        Assert.Equal(RejectReason.RenameExistingFolder, Reason(appendPlan, new Rename(Z[0].Id(), "x", [])));
        var imported = PlanScenario.Derive(ZLibrary().Card(Z).Prepare());
        Assert.Equal(RejectReason.RenameExistingFolder, Reason(imported, new Rename(Z[0].Id(), "x", [])));
        var fresh = PlanScenario.Derive(new PlanScenario().Card(Z).Prepare());
        Assert.Null(Reason(fresh, new Rename(Z[0].Id(), "Zachar Bay", [])));
    }

    [Theory]
    [InlineData(@"C:\lib\UAS Videos\.uas-sort", RejectReason.RetargetIntoReservedFolder)]
    [InlineData(@"C:\lib\UAS Videos\.uas-sort\x", RejectReason.RetargetIntoReservedFolder)]
    [InlineData(@"C:\lib\UAS Videos\Picture Offload", RejectReason.RetargetIntoReservedFolder)]
    [InlineData(@"C:\lib\UAS Videos\picture offload\sub", RejectReason.RetargetIntoReservedFolder)]
    [InlineData(@"D:\Old Offload", RejectReason.RetargetIntoReservedFolder)]
    [InlineData(@"D:\Elsewhere", RejectReason.RetargetOutsideVideoRoot)]
    [InlineData(@"C:\lib\UAS Videos\2026\2026-09\2026-09-28 Later", RejectReason.RetargetLaterDatedFolderUnconfirmed)]
    [InlineData(@"C:\lib\UAS Videos\2026\2026-09\2026-09-20 Earlier", null)]
    public void Retarget_Rules(string path, RejectReason? expected)
    {
        var b = new PlanScenario().PreviousPhotoRootFile(@"D:\Old Offload", "x.DNG", 1, PlanScenario.NowUtc).Card(Z).Prepare();
        var p = PlanScenario.Derive(b);
        Assert.Equal(expected, Reason(p, new Retarget(Z[0].Id(), new AppendTo(path), false, [])));
    }

    [Fact]
    public void LaterDatedFolder_AllowedWhenConfirmed()
    {
        var p = PlanScenario.Derive(new PlanScenario().Card(Z).Prepare());
        Assert.Null(Reason(p, new Retarget(Z[0].Id(), new AppendTo(@"C:\lib\UAS Videos\2026\2026-09\2026-09-28 Later"), true, [])));
    }

    [Fact]
    public void Merge_OfTwoGroupsWithoutWalls_IsValid()
    {
        var p = PlanScenario.Derive(new PlanScenario().Card(C117, A1, A2).Prepare(), new Tuning(25, 1));
        Assert.Null(Reason(p, new Merge(C117.Id(), A1.Id())));
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*EditValidatorTests"`
Expected: build error CS0103 (`EditValidator` does not exist).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Editing/EditValidator.cs
using UasSort.Core.Naming;
using UasSort.Core.Planning;

namespace UasSort.Core.Editing;

/// <summary>Refused edits (Ref §8.9), validated against the plan that includes every earlier edit.</summary>
public static class EditValidator
{
    public static Rejected? Validate(Plan current, PlanEdit edit)
    {
        var items = current.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var videoGroup = new Dictionary<ItemId, int>();
        for (var gi = 0; gi < current.Groups.Length; gi++)
            foreach (var v in current.Groups[gi].Videos) videoGroup[v] = gi;

        var refs = PlanEditRefs.Referenced(edit);
        var needsVideo = edit is not (SetIncluded or SetDayIncluded);
        if (refs.Any(r => needsVideo ? !videoGroup.ContainsKey(r) : !items.ContainsKey(r)))
            return new Rejected(RejectReason.ItemsNotFound, "Some clips in this change are no longer on the card.");

        switch (edit)
        {
            case Merge m:
            {
                int a = videoGroup[m.InA], b = videoGroup[m.InB];
                if (a == b) return null;
                var walls = current.Groups.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1)
                    .Select(g => g.Wall?.FullPath.ToUpperInvariant()).Where(w => w is not null).Distinct().Count();
                return walls > 1 ? new Rejected(RejectReason.MergeAcrossLibraryFolders, "These clips are already in two different folders") : null;
            }
            case SplitBefore s:
                return current.Groups[videoGroup[s.First]].Videos[0] == s.First
                    ? new Rejected(RejectReason.SplitAtGroupStart, "This clip already starts its group")
                    : null;
            case MoveToNewGroup mv when mv.Items.Any(id => items[id].Newness is Imported):
            case MoveToGroup mt when mt.Items.Any(id => items[id].Newness is Imported):
                return new Rejected(RejectReason.MoveImportedItem, "Clips already in the library stay in their folder");
            case Rename r:
            {
                var target = current.Groups[videoGroup[r.InGroup]].Target;
                var existing = target is AlreadyImported || (target is Append a && a.Why != FolderNamer.FolderExistsWhy);
                return existing
                    ? new Rejected(RejectReason.RenameExistingFolder, "Appending to an existing folder; choose New folder to name a new one")
                    : null;
            }
            case Retarget { Choice: AppendTo to } rt:
                return ValidateAppendTo(current, current.Groups[videoGroup[rt.InGroup]], to.FolderFullPath, rt.ConfirmedBeforeFolderDate);
            default:
                return null;
        }
    }

    private static Rejected? ValidateAppendTo(Plan current, VideoGroup g, string path, bool confirmed)
    {
        var s = current.Base.Scan.Settings;
        string[] reserved = [LedgerPaths.For(s.VideoRoot), s.PhotoRoot, .. s.PreviousPhotoRoots];
        if (reserved.Any(r => IsUnder(path, r)))
            return new Rejected(RejectReason.RetargetIntoReservedFolder, "That folder is reserved for uas-sort history or photos");
        if (!IsUnder(path, s.VideoRoot) || Norm(path) == Norm(s.VideoRoot))
            return new Rejected(RejectReason.RetargetOutsideVideoRoot, $"Pick a folder inside {Path.GetFileName(Norm(s.VideoRoot))}");
        var f = Planner.FolderRefFor(path, g.Start, current.Base.Scan.Library);
        if (f.NameDate > g.Start && !confirmed)
            return new Rejected(RejectReason.RetargetLaterDatedFolderUnconfirmed,
                                $"Folder is dated {PlanText.ShortDate(f.NameDate)}; these clips start {PlanText.ShortDate(g.Start)}");
        return null;
    }

    private static string Norm(string p) => p.Replace('/', '\\').TrimEnd('\\').ToUpperInvariant();

    public static bool IsUnder(string path, string root)
    {
        var p = Norm(path);
        var r = Norm(root);
        return p == r || p.StartsWith(r + "\\", StringComparison.Ordinal);
    }
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*EditValidatorTests"`
Expected: PASS (all facts and 8 theory rows).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Editing/EditValidator.cs tests/UasSort.Core.Tests/Editing/EditValidatorTests.cs
git commit -m "feat: edit validation with the refused-edit reasons of the edit semantics

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.16: PlanSession — serial edit queue, latest-wins previews, undo/redo, drafts

**Files:**
- Create: `src/UasSort.Core/Editing/PlanSession.cs`
- Create: `tests/UasSort.Testing/GatedPlanDeriver.cs` (namespace `UasSort.Testing`; Part 10 Task 10.1 adds its hold/release members in the same file)
- Create: `tests/UasSort.Testing/PlanFingerprint.cs` (namespace `UasSort.Testing`)
- Test: `tests/UasSort.Core.Tests/Editing/PlanSessionTests.cs`

**Interfaces:**
- Consumes: `IPlanDeriver`, `Plan`, `PlanBase`, `Draft`, `EditResult`, `Applied`, `Rejected`, `SessionFlags`, `Issue` (Part 02); `EditValidator` (06.15); `PlanEditRefs` (06.1); `Planner` as the production `IPlanDeriver`.
- Produces (Ref §4.2 contract, plus the members marked "defined here"):
  - `sealed class PlanSession`:
    - `PlanSession(PlanBase b, IPlanDeriver deriver, Tuning tuning, TimeProvider clock)` (defined here; derives the first plan synchronously)
    - `Plan Current { get; }` — the last completed plan of the committed state (edits, undo/redo, tuning commits, ledger acceptance); previews raise `Changed` but do not replace `Current`
    - `Task<EditResult> ApplyAsync(PlanEdit, CancellationToken)`, `Task<EditResult> ApplyAllAsync(IReadOnlyList<PlanEdit>, CancellationToken)` (one undo entry)
    - `void Preview(Tuning)` and `Task PreviewAsync(Tuning)` (defined here; the awaitable form for tests and the VM)
    - `Task CommitTuningAsync()`, `Task<Plan> UndoAsync()`, `Task<Plan> RedoAsync()`
    - `void AcceptLedgerIssues()` and `Task AcceptLedgerIssuesAsync()` (defined here)
    - `Draft ToDraft()`; `static PlanSession Resume(PlanBase, Draft, IPlanDeriver, out int dropped)` and an overload taking `TimeProvider` (defined here)
    - `event Action<Plan> Changed` (raised on a thread-pool thread; strictly increasing `Revision` among published plans)
    - `Tuning CommittedTuning`, `IReadOnlyList<PlanEdit> Edits`, `int UndoDepth`, `bool CanUndo`, `bool CanRedo`, `Task WhenIdleAsync()` (defined here)
  - Testing (flat namespace `UasSort.Testing`, one owner each): `sealed class GatedPlanDeriver(IPlanDeriver inner) : IPlanDeriver { Gate Arm(Func<Tuning, IReadOnlyList<PlanEdit>, bool> match); int CallCount; sealed class Gate { Task Entered; void Release(); } }` (Part 10 adds `Hold`, `Calls` (list of `DeriveCall`), `CallStartedAsync`, `Release(int)`, `ReleaseAll` and `DeriveCall` in the same file, so the int counter is `CallCount`, never `Calls`); `static class PlanFingerprint { string Of(Plan p); }` (revision-independent).

Threading contract (Ref §4.2): edits, undo, redo, tuning commits and ledger acceptance go through one `SemaphoreSlim(1,1)`; each edit is validated synchronously against the plan that includes every earlier queued edit (a `Rejected` returns at once and changes nothing), then derived on the thread pool. `Preview` cancels the previous preview's token; a preview whose edit log was superseded by a completed edit is re-run on the new log; a committed-state plan whose revision is not above the last published one is re-stamped with a fresh revision so it is never hidden by an older-log preview.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Testing/GatedPlanDeriver.cs
namespace UasSort.Testing;

/// <summary>Blocks matching Derive calls until released: proves a slow earlier derive never overwrites a later one (Ref §13).</summary>
public sealed class GatedPlanDeriver(IPlanDeriver inner) : IPlanDeriver
{
    private readonly List<Gate> _armed = [];
    private readonly Lock _lock = new();
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public sealed class Gate(Func<Tuning, IReadOnlyList<PlanEdit>, bool> match)
    {
        internal readonly Func<Tuning, IReadOnlyList<PlanEdit>, bool> Match = match;
        internal readonly TaskCompletionSource EnteredTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => EnteredTcs.Task;
        public void Release() => ReleaseTcs.TrySetResult();
    }

    public Gate Arm(Func<Tuning, IReadOnlyList<PlanEdit>, bool> match)
    {
        var g = new Gate(match);
        lock (_lock) _armed.Add(g);
        return g;
    }

    public Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct)
    {
        Interlocked.Increment(ref _callCount);
        Gate? gate;
        lock (_lock)
        {
            gate = _armed.FirstOrDefault(g => g.Match(t, edits));
            if (gate is not null) _armed.Remove(gate);
        }
        if (gate is not null)
        {
            gate.EnteredTcs.TrySetResult();
            gate.ReleaseTcs.Task.Wait(CancellationToken.None);
        }
        return inner.Derive(b, t, edits, flags, revision, ct);
    }
}
```

```csharp
// tests/UasSort.Testing/PlanFingerprint.cs
using System.Globalization;
using System.Text;

namespace UasSort.Testing;

/// <summary>A revision-independent text form of a plan, for "undo then redo gives the same plan" and draft replay.</summary>
public static class PlanFingerprint
{
    public static string Of(Plan p)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"T:{p.Tuning.RadiusMiles}/{p.Tuning.GapDays}\n");
        foreach (var g in p.Groups)
            sb.Append(string.Join(",", g.Videos.Select(v => v.CardRelPath))).Append(" | ").Append(Target(g.Target))
              .Append(" | ").Append(g.Description).Append(" | ").Append(g.Foldable).Append('\n');
        foreach (var b in p.Boundaries) sb.Append(b.Cause).Append(';');
        sb.Append("\nI:").Append(string.Join(",", p.Included.Select(i => i.CardRelPath).Order(StringComparer.Ordinal)));
        sb.Append("\nX:").Append(string.Join(",", p.Issues.Select(i => $"{i.Code}/{i.Severity}@{i.Anchor?.CardRelPath}").Order(StringComparer.Ordinal)));
        return sb.ToString();
    }

    private static string Target(GroupTarget t) => t switch
    {
        NewFolder n => "N:" + n.RelPath,
        Append a => $"A:{a.Folder.FullPath}:{a.Confidence}:{a.Why}",
        AlreadyImported ai => "I:" + ai.Folder.FullPath,
        NothingToCopy n => "0:" + n.Summary,
        SkipGroup => "S",
    };
}
```

```csharp
// tests/UasSort.Core.Tests/Editing/PlanSessionTests.cs
using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using UasSort.Core.Editing;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Editing;

public sealed class PlanSessionTests
{
    private static readonly RawItem C117 = Clip.Vid("20260725232655", 117, Sites.Council);
    private static readonly RawItem C118 = Clip.Vid("20260726022937", 118, Sites.Council);
    private static readonly RawItem A1 = Clip.Vid("20260726235645", 1, Sites.Anvil);
    private static readonly RawItem A2 = Clip.Vid("20260727000012", 2, Sites.Anvil);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlanBase Base(PlanScenario? s = null) => (s ?? new PlanScenario()).Card(C117, C118, A1, A2).Prepare();
    private static PlanSession Session(PlanBase b, IPlanDeriver? d = null) =>
        new(b, d ?? PlanScenario.CreatePlanner(), new Tuning(50, 1), new FakeTimeProvider(new DateTimeOffset(PlanScenario.NowUtc)));

    [Fact]
    public async Task Apply_DerivesNewPlan_RejectedChangesNothing()
    {
        var s = Session(Base());
        Assert.Single(s.Current.Groups);
        var r = await s.ApplyAsync(new SplitBefore(A1.Id()), Ct);
        Assert.True(r is Applied);
        Assert.Equal(2, s.Current.Groups.Length);
        Assert.Equal(1, s.UndoDepth);
        var before = PlanFingerprint.Of(s.Current);
        var rej = await s.ApplyAsync(new SplitBefore(A1.Id()), Ct);
        Assert.True(rej is Rejected { Reason: RejectReason.SplitAtGroupStart });
        Assert.Equal(before, PlanFingerprint.Of(s.Current));
        Assert.Equal(1, s.UndoDepth);
    }

    [Fact] // edits are serialised: the second is validated against the plan that includes the first
    public async Task ConcurrentEdits_AreSerialised()
    {
        var s = Session(Base());
        var t1 = s.ApplyAsync(new SplitBefore(A1.Id()), Ct);
        var t2 = s.ApplyAsync(new SplitBefore(A1.Id()), Ct);
        Assert.True(await t1 is Applied);
        Assert.True(await t2 is Rejected { Reason: RejectReason.SplitAtGroupStart });
    }

    [Fact] // a quick fix is one undo entry; undo then redo is the identity
    public async Task QuickFix_IsOneUndoEntry_UndoRedoIdentity()
    {
        var s = Session(Base());
        var original = PlanFingerprint.Of(s.Current);
        await s.ApplyAllAsync([new SplitBefore(A1.Id()), new Rename(A1.Id(), "Anvil Mountain", [A1.Id(), A2.Id()])], Ct);
        Assert.Equal(1, s.UndoDepth);
        Assert.True(s.CanUndo);          // Part 10's ReviewVm mirrors CanUndo/CanRedo (registry decision 39)
        Assert.False(s.CanRedo);
        var after = PlanFingerprint.Of(s.Current);
        Assert.Equal(original, PlanFingerprint.Of(await s.UndoAsync()));
        Assert.True(s.CanRedo);
        Assert.Equal(after, PlanFingerprint.Of(await s.RedoAsync()));
    }

    [Fact]
    public async Task Changed_RaisedOnThreadPool_WithIncreasingRevisions()
    {
        var s = Session(Base());
        var seen = new ConcurrentQueue<(bool Pool, int Rev)>();
        s.Changed += p => seen.Enqueue((Thread.CurrentThread.IsThreadPoolThread, p.Revision));
        await s.ApplyAsync(new SplitBefore(A1.Id()), Ct);
        await s.UndoAsync();
        var list = seen.ToList();
        Assert.Equal(2, list.Count);
        Assert.All(list, x => Assert.True(x.Pool));
        Assert.True(list[1].Rev > list[0].Rev);
    }

    [Fact] // a gated IPlanDeriver proves a slow earlier derive never overwrites a later one
    public async Task SlowEarlierPreview_NeverOverwritesLaterPreview()
    {
        var gated = new GatedPlanDeriver(PlanScenario.CreatePlanner());
        var s = Session(Base(), gated);
        var plans = new ConcurrentQueue<Plan>();
        s.Changed += plans.Enqueue;
        var gate = gated.Arm((t, _) => t.RadiusMiles == 30);
        var slow = s.PreviewAsync(new Tuning(30, 1));
        await gate.Entered;
        await s.PreviewAsync(new Tuning(40, 1));
        gate.Release();
        await slow;
        Assert.Equal(40, plans.Last().Tuning.RadiusMiles);
        Assert.DoesNotContain(plans, p => p.Tuning.RadiusMiles == 30);
        Assert.Equal(50, s.Current.Tuning.RadiusMiles);                 // previews never replace Current
    }

    [Fact] // a preview whose edit log was superseded by a completed edit is re-run on the new log
    public async Task SupersededPreview_IsRerunOnNewLog()
    {
        var gated = new GatedPlanDeriver(PlanScenario.CreatePlanner());
        var s = Session(Base(), gated);
        var plans = new ConcurrentQueue<Plan>();
        s.Changed += plans.Enqueue;
        var gate = gated.Arm((t, e) => t.RadiusMiles == 25 && e.Count == 0);
        var preview = s.PreviewAsync(new Tuning(25, 1));
        await gate.Entered;
        Assert.True(await s.ApplyAsync(new SplitBefore(A1.Id()), Ct) is Applied);
        gate.Release();
        await preview;
        var last = plans.Last();
        Assert.Equal(25, last.Tuning.RadiusMiles);
        Assert.Equal(BoundaryCause.UserSplit, Assert.Single(last.Boundaries).Cause);   // derived with the split in the log
        Assert.True(last.Revision > s.Current.Revision);
    }

    [Fact] // slider commit = one undo entry; nothing to commit = no entry
    public async Task CommitTuning_IsOneEntry()
    {
        var s = Session(Base());
        await s.CommitTuningAsync();
        Assert.Equal(0, s.UndoDepth);
        await s.PreviewAsync(new Tuning(30, 1));
        await s.PreviewAsync(new Tuning(25, 1));
        await s.CommitTuningAsync();
        Assert.Equal(1, s.UndoDepth);
        Assert.Equal(new Tuning(25, 1), s.CommittedTuning);
        Assert.Equal(2, s.Current.Groups.Length);
        await s.UndoAsync();
        Assert.Equal(new Tuning(50, 1), s.CommittedTuning);
    }

    [Fact] // [Accept and continue]: the Blocking parse issue becomes a RequiresAck Warning for this session
    public async Task AcceptLedgerIssues_DowngradesParseIssue()
    {
        var s = Session(Base(new PlanScenario().LedgerParseIssue("ledger-A.jsonl", 3, "bad")));
        Assert.Equal(IssueSeverity.Blocking, s.Current.Issues.Single(i => i.Code == IssueCode.LedgerParseIssue).Severity);
        await s.AcceptLedgerIssuesAsync();
        var i = s.Current.Issues.Single(x => x.Code == IssueCode.LedgerParseIssue);
        Assert.Equal((IssueSeverity.Warning, true), (i.Severity, i.RequiresAckAtPreflight));
        Assert.Equal(0, s.UndoDepth);
    }

    [Fact] // drafts: resume replays the same plan; missing-item edits are counted as dropped
    public async Task Draft_ResumeReplays_AndCountsDropped()
    {
        var b = Base();
        var s = Session(b);
        await s.ApplyAsync(new SplitBefore(A1.Id()), Ct);
        await s.ApplyAsync(new Rename(A1.Id(), "Anvil Mountain", [A1.Id(), A2.Id()]), Ct);
        var draft = s.ToDraft();
        Assert.Equal((1, "vol-1A2B3C4D", "0123456789abcdef", PlanScenario.NowUtc), (draft.V, draft.CardKey, draft.InventoryHash, draft.SavedUtc));

        var resumed = PlanSession.Resume(Base(), draft, PlanScenario.CreatePlanner(), out var dropped);
        Assert.Equal(0, dropped);
        Assert.Equal(PlanFingerprint.Of(s.Current), PlanFingerprint.Of(resumed.Current));

        var withGone = draft with { Edits = [.. draft.Edits, new SplitBefore(new ItemId("DCIM/DJI_001/gone.MP4"))] };
        var r2 = PlanSession.Resume(Base(), withGone, PlanScenario.CreatePlanner(), out var dropped2);
        Assert.Equal(1, dropped2);
        var info = Assert.Single(r2.Current.Issues, i => i.Code == IssueCode.StaleEditsDropped);
        Assert.Equal((IssueSeverity.Info, "2 of 3 edits still apply"), (info.Severity, info.Message));
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PlanSessionTests"`
Expected: build error CS0246 (`PlanSession` not found).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Editing/PlanSession.cs
using System.Collections.Immutable;
using UasSort.Core.Planning;

namespace UasSort.Core.Editing;

/// <summary>Edit log, undo/redo of (Tuning, edits) states, drafts, and the threading contract of Ref §4.2.</summary>
public sealed class PlanSession
{
    private sealed record State(Tuning Tuning, ImmutableList<PlanEdit> Edits);

    private readonly PlanBase _base;
    private readonly IPlanDeriver _deriver;
    private readonly TimeProvider _clock;
    private readonly ImmutableArray<Issue> _sessionIssues;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _lock = new();
    private readonly Stack<State> _undo = new();
    private readonly Stack<State> _redo = new();
    private State _state;
    private SessionFlags _flags = new(false);
    private Plan _current;
    private int _revision;
    private int _published;
    private CancellationTokenSource? _previewCts;
    private Tuning? _previewTuning;

    public event Action<Plan>? Changed;

    public PlanSession(PlanBase b, IPlanDeriver deriver, Tuning tuning, TimeProvider clock)
        : this(b, deriver, new State(tuning, []), clock, []) { }

    private PlanSession(PlanBase b, IPlanDeriver deriver, State state, TimeProvider clock, ImmutableArray<Issue> sessionIssues)
    {
        _base = b;
        _deriver = deriver;
        _clock = clock;
        _sessionIssues = sessionIssues;
        _state = state;
        _current = DeriveNow(state, CancellationToken.None);
        _published = _current.Revision;
    }

    public Plan Current { get { lock (_lock) return _current; } }
    public Tuning CommittedTuning { get { lock (_lock) return _state.Tuning; } }
    public IReadOnlyList<PlanEdit> Edits { get { lock (_lock) return _state.Edits; } }
    public int UndoDepth { get { lock (_lock) return _undo.Count; } }
    public bool CanUndo => UndoDepth > 0;
    public bool CanRedo { get { lock (_lock) return _redo.Count > 0; } }

    public static PlanSession Resume(PlanBase b, Draft d, IPlanDeriver deriver, out int dropped) =>
        Resume(b, d, deriver, TimeProvider.System, out dropped);

    public static PlanSession Resume(PlanBase b, Draft d, IPlanDeriver deriver, TimeProvider clock, out int dropped)
    {
        var known = b.Items.Select(i => i.Raw.Unit.Id).ToHashSet();
        dropped = d.Edits.Count(e => PlanEditRefs.Referenced(e).Any(r => !known.Contains(r)));
        ImmutableArray<Issue> extra = dropped > 0
            ? [new Issue(IssueSeverity.Info, IssueCode.StaleEditsDropped, $"{d.Edits.Length - dropped} of {d.Edits.Length} edits still apply",
                         null, [], false)]
            : [];
        return new PlanSession(b, deriver, new State(d.Tuning, [.. d.Edits]), clock, extra);
    }

    public Draft ToDraft()
    {
        State s;
        lock (_lock) s = _state;
        var inv = _base.Scan.Inventory;
        return new Draft(1, inv.Source.DraftKey, inv.InventoryHash, _clock.GetUtcNow().UtcDateTime, s.Tuning, [.. s.Edits]);
    }

    public Task<EditResult> ApplyAsync(PlanEdit edit, CancellationToken ct) => ApplyAllAsync([edit], ct);

    public async Task<EditResult> ApplyAllAsync(IReadOnlyList<PlanEdit> edits, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => ApplyCore(edits, ct), ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private EditResult ApplyCore(IReadOnlyList<PlanEdit> edits, CancellationToken ct)
    {
        State s;
        Plan plan;
        lock (_lock) { s = _state; plan = _current; }
        for (var i = 0; i < edits.Count; i++)
        {
            if (EditValidator.Validate(plan, edits[i]) is { } rejected) return rejected;
            s = s with { Edits = s.Edits.Add(edits[i]) };
            if (i < edits.Count - 1) plan = DeriveNow(s, ct);        // the next edit is validated against this one
        }
        var result = DeriveNow(s, ct);
        lock (_lock)
        {
            _undo.Push(_state);
            _redo.Clear();
            _state = s;
        }
        return new Applied(Commit(result));
    }

    public void Preview(Tuning t) => _ = PreviewAsync(t);

    public Task PreviewAsync(Tuning t)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            _previewCts?.Cancel();
            _previewCts = cts = new CancellationTokenSource();
            _previewTuning = t;
        }
        var token = cts.Token;
        return Task.Run(() => PreviewCore(t, token), CancellationToken.None);
    }

    private void PreviewCore(Tuning t, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            State s;
            lock (_lock) s = _state;
            Plan p;
            try { p = DeriveNow(s with { Tuning = t }, token); }
            catch (OperationCanceledException) { return; }
            lock (_lock)
            {
                if (token.IsCancellationRequested) return;
                if (!ReferenceEquals(s.Edits, _state.Edits)) continue;   // superseded by a completed edit: re-run on the new log
                if (p.Revision <= _published) return;
                _published = p.Revision;
            }
            Changed?.Invoke(p);
            return;
        }
    }

    public async Task CommitTuningAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                Tuning? t;
                State s;
                lock (_lock)
                {
                    t = _previewTuning;
                    _previewTuning = null;
                    _previewCts?.Cancel();
                    _previewCts = null;
                    s = _state;
                }
                if (t is null || t == s.Tuning) return;
                var next = s with { Tuning = t };
                var p = DeriveNow(next, CancellationToken.None);
                lock (_lock)
                {
                    _undo.Push(_state);
                    _redo.Clear();
                    _state = next;
                }
                Commit(p);
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<Plan> UndoAsync() => StepAsync(undo: true);

    public Task<Plan> RedoAsync() => StepAsync(undo: false);

    private async Task<Plan> StepAsync(bool undo)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                State s;
                lock (_lock)
                {
                    var from = undo ? _undo : _redo;
                    var to = undo ? _redo : _undo;
                    if (from.Count == 0) return _current;
                    _previewCts?.Cancel();
                    _previewTuning = null;
                    s = from.Pop();
                    to.Push(_state);
                    _state = s;
                }
                return Commit(DeriveNow(s, CancellationToken.None));
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void AcceptLedgerIssues() => _ = AcceptLedgerIssuesAsync();

    public async Task AcceptLedgerIssuesAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                State s;
                lock (_lock)
                {
                    _flags = new SessionFlags(true);
                    s = _state;
                }
                Commit(DeriveNow(s, CancellationToken.None));
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WhenIdleAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
    }

    private Plan DeriveNow(State s, CancellationToken ct)
    {
        var rev = Interlocked.Increment(ref _revision);
        SessionFlags flags;
        lock (_lock) flags = _flags;
        var p = _deriver.Derive(_base, s.Tuning, s.Edits, flags, rev, ct);
        return _sessionIssues.IsDefaultOrEmpty ? p : p with { Issues = p.Issues.AddRange(_sessionIssues) };
    }

    private Plan Commit(Plan p)
    {
        lock (_lock)
        {
            if (p.Revision <= _published) p = p with { Revision = Interlocked.Increment(ref _revision) };
            _published = p.Revision;
            _current = p;
        }
        Changed?.Invoke(p);
        return p;
    }
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PlanSessionTests"`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Editing/PlanSession.cs tests/UasSort.Testing/GatedPlanDeriver.cs tests/UasSort.Testing/PlanFingerprint.cs tests/UasSort.Core.Tests/Editing/PlanSessionTests.cs
git commit -m "feat: PlanSession with serial edit queue, latest-wins previews, undo/redo and drafts

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.17: Edit invariants (seeded random sequences)

**Files:**
- Create: `tests/UasSort.Testing/Planning/InvariantScenario.cs`
- Test: `tests/UasSort.Core.Tests/Editing/EditInvariantTests.cs`

**Interfaces:**
- Consumes: `PlanSession` (06.16), `Planner` (06.12), `PlanFingerprint` (06.16), `PlanScenario` (06.2).
- Produces (Testing, defined here): `static class InvariantScenario { PlanScenario Build(); }` — a card with walls, a truncated clip, a no-GPS clip, several days and sites, and photos.

Invariants (Ref §13 "Edit invariants"): every video is in exactly one group and no group is empty; undo then redo gives the same plan; a quick fix is one undo entry; replaying the draft on a copy of the scan gives the same plan; item-anchored edits survive every R from 5 to 100 mi; local dates never change with R or G.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Testing/Planning/InvariantScenario.cs
namespace UasSort.Testing.Planning;

public static class InvariantScenario
{
    public static PlanScenario Build()
    {
        var c117 = Clip.Vid("20260725232655", 117, Sites.Council);
        var c118 = Clip.Vid("20260726022937", 118, Sites.Council);
        return new PlanScenario()
            .Library(@"2026\2026-07\2026-07-25 Council Road", c117, c118)
            .Card(c117, c118,
                  Clip.Vid("20260726235645", 1, Sites.Anvil), Clip.Vid("20260727000012", 2, Sites.Anvil),
                  Clip.Vid("20260727002013", 14, Sites.Anvil, moov: false),
                  Clip.Vid("20260523015251", 40, Sites.KodiakTown), Clip.Vid("20260523201928", 52, Sites.KodiakTown),
                  Clip.Vid("20260524190521", 64, new GeoPoint(57.75, -152.50)), Clip.Vid("20260525092718", 85, Sites.KodiakTown),
                  Clip.Vid("20260927140127", 123, Sites.Zachar), Clip.Vid("20260927140144", 124, Sites.Zachar),
                  Clip.Vid("20260927142416", 148, Sites.Zachar), Clip.Vid("20260927160000", 160, Sites.Zachar),
                  Clip.Vid("20260927161000", 161), Clip.Vid("20260927190000", 150, Sites.KodiakTown),
                  Clip.Dng("20260725233000", 116, Sites.Council), Clip.Dng("20260927140000", 122, Sites.Zachar));
    }
}
```

```csharp
// tests/UasSort.Core.Tests/Editing/EditInvariantTests.cs
using Microsoft.Extensions.Time.Testing;
using UasSort.Core.Editing;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Editing;

public sealed class EditInvariantTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlanSession NewSession(PlanBase b) =>
        new(b, PlanScenario.CreatePlanner(), new Tuning(50, 1), new FakeTimeProvider(new DateTimeOffset(PlanScenario.NowUtc)));

    private static void AssertPartition(Plan p, IReadOnlyCollection<ItemId> videos)
    {
        Assert.All(p.Groups, g => Assert.NotEmpty(g.Videos));
        var all = p.Groups.SelectMany(g => g.Videos).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());                 // exactly one group each
        Assert.True(all.ToHashSet().SetEquals(videos));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task RandomEditSequences_KeepInvariants(int seed)
    {
        var rnd = new Random(seed);
        var b = InvariantScenario.Build().Prepare();
        var videos = b.Items.Where(i => i.Raw.Kind == ItemKind.Video).Select(i => i.Raw.Unit.Id).ToList();
        var dates = b.Items.ToDictionary(i => i.Raw.Unit.Id, i => i.Time.LocalDate);
        var s = NewSession(b);

        for (var step = 0; step < 40; step++)
        {
            var p = s.Current;
            VideoGroup G() => p.Groups[rnd.Next(p.Groups.Length)];
            switch (rnd.Next(10))
            {
                case 0 when p.Groups.Length > 1:
                {
                    var i = rnd.Next(p.Groups.Length - 1);
                    await s.ApplyAsync(new Merge(p.Groups[i].Id.Anchor, p.Groups[i + 1].Id.Anchor), Ct);
                    break;
                }
                case 1:
                {
                    var g = G();
                    if (g.Videos.Length > 1) await s.ApplyAsync(new SplitBefore(g.Videos[1 + rnd.Next(g.Videos.Length - 1)]), Ct);
                    break;
                }
                case 2:
                    await s.ApplyAsync(new MoveToNewGroup([videos[rnd.Next(videos.Count)]]), Ct);
                    break;
                case 3:
                    await s.ApplyAsync(new MoveToGroup([videos[rnd.Next(videos.Count)]], G().Id.Anchor), Ct);
                    break;
                case 4:
                {
                    var g = G();
                    await s.ApplyAsync(new Rename(g.Id.Anchor, $"Name {rnd.Next(3)}", g.Videos), Ct);
                    break;
                }
                case 5:
                {
                    var g = G();
                    TargetChoice c = rnd.Next(3) switch { 0 => new NewFolderTarget(), 1 => new SkipTarget(), _ => new AutoTarget() };
                    await s.ApplyAsync(new Retarget(g.Id.Anchor, c, false, g.Videos), Ct);
                    break;
                }
                case 6:
                    await s.ApplyAsync(new SetIncluded([b.Items[rnd.Next(b.Items.Length)].Raw.Unit.Id], rnd.Next(2) == 0), Ct);
                    break;
                case 7:
                    await s.PreviewAsync(new Tuning(5 + rnd.Next(96), rnd.Next(8)));
                    await s.CommitTuningAsync();
                    break;
                case 8:
                {
                    var fixes = p.Issues.SelectMany(x => x.QuickFixes).Where(q => !q.Edits.IsDefaultOrEmpty).ToList();
                    if (fixes.Count == 0) break;
                    var depth = s.UndoDepth;
                    if (await s.ApplyAllAsync(fixes[rnd.Next(fixes.Count)].Edits, Ct) is Applied)
                        Assert.Equal(depth + 1, s.UndoDepth);                     // a quick fix is one undo entry
                    break;
                }
                default:
                    if (rnd.Next(2) == 0) await s.UndoAsync();
                    else await s.RedoAsync();
                    break;
            }

            AssertPartition(s.Current, videos);
            if (s.CanUndo)
            {
                var before = PlanFingerprint.Of(s.Current);
                await s.UndoAsync();
                await s.RedoAsync();
                Assert.Equal(before, PlanFingerprint.Of(s.Current));           // undo then redo is the identity
            }
        }

        // replaying the draft on a copy of the scan gives the same plan
        var resumed = PlanSession.Resume(InvariantScenario.Build().Prepare(), s.ToDraft(), PlanScenario.CreatePlanner(), out var dropped);
        Assert.Equal(0, dropped);
        Assert.Equal(PlanFingerprint.Of(s.Current), PlanFingerprint.Of(resumed.Current));

        // edits survive every R from 5 to 100; local dates never change with R or G
        var planner = PlanScenario.CreatePlanner();
        foreach (var g in new[] { 0, 1, 7 })
            for (var r = 5; r <= 100; r++)
            {
                var p = planner.Derive(b, new Tuning(r, g), s.Edits, new SessionFlags(false), 1, Ct);
                AssertPartition(p, videos);
                Assert.All(p.Groups, grp => Assert.Equal(grp.Videos.Min(v => dates[v]), grp.Start));
                Assert.All(p.Base.Items, i => Assert.Equal(dates[i.Raw.Unit.Id], i.Time.LocalDate));
            }
    }

    [Fact] // §8.9 worked example at session level: SplitBefore at R 50 → 25 → 50 keeps its UserSplit chip and the edit stays in the log
    public async Task SplitBefore_SurvivesTuningChanges()
    {
        var a1 = Clip.Vid("20260726235645", 1, Sites.Anvil);
        var b = new PlanScenario().Card(Clip.Vid("20260725232655", 117, Sites.Council), a1).Prepare();
        var s = NewSession(b);
        await s.ApplyAsync(new SplitBefore(a1.Id()), Ct);
        foreach (var r in new[] { 25, 50 })
        {
            await s.PreviewAsync(new Tuning(r, 1));
            await s.CommitTuningAsync();
            Assert.Equal(BoundaryCause.UserSplit, Assert.Single(s.Current.Boundaries).Cause);
            Assert.Single(s.Edits);
        }
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*EditInvariantTests"`
Expected: build error CS0103 (`InvariantScenario` does not exist).

- [ ] **Step 3: Implement**

Create `tests/UasSort.Testing/Planning/InvariantScenario.cs` as shown in Step 1. Any invariant failure is a bug in `Clusterer` (partition), `Planner` (determinism) or `PlanSession` (undo stack) — fix it there.

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*EditInvariantTests"`
Expected: PASS (5 seeds + 1 fact).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Testing/Planning/InvariantScenario.cs tests/UasSort.Core.Tests/Editing/EditInvariantTests.cs
git commit -m "test: seeded edit invariants over merge, split, move, pins, inclusion, tuning, quick fixes and undo

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.18: Derive benchmark (500 items, median &lt; 50 ms)

**Files:**
- Create: `tests/UasSort.Testing/Planning/BenchmarkScenario.cs`
- Test: `tests/UasSort.Core.Tests/Planning/DeriveBenchmarkTests.cs`

**Interfaces:**
- Consumes: `PlanScenario`, `Planner` (06.2, 06.12).
- Produces (Testing, defined here): `static class BenchmarkScenario { PlanScenario Build(int videos = 400, int groups = 12, int photos = 100); }` — 12 sites at 60° N, 4° of longitude apart (Alaska to Saskatchewan), each on its own day three days apart, clips 2 min apart, photos spread across the same days.

The Ref requires a **Release** benchmark. The test enforces 50 ms when the test assembly is optimised (Release) and 150 ms in Debug (so the default Debug `dotnet test --solution` run still guards against regressions); the completion step runs it in Release.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Testing/Planning/BenchmarkScenario.cs
using System.Globalization;

namespace UasSort.Testing.Planning;

public static class BenchmarkScenario
{
    public static PlanScenario Build(int videos = 400, int groups = 12, int photos = 100)
    {
        var items = new List<RawItem>(videos + photos);
        var start = new DateTime(2026, 6, 1, 18, 0, 0);
        for (var i = 0; i < videos; i++)
        {
            var g = i % groups;
            var site = new GeoPoint(60 + (i % 7) * 0.001, -150 + 4 * g);          // 4° of longitude ≈ 138 mi apart, all on land
            var stamp = start.AddDays(3 * g).AddMinutes(2 * (i / groups));
            items.Add(Clip.Vid(stamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), i + 1, site));
        }
        for (var j = 0; j < photos; j++)
        {
            var g = j % groups;
            var stamp = start.AddDays(3 * g).AddMinutes(1 + 2 * (j / groups));
            items.Add(Clip.Dng(stamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), 5000 + j, new GeoPoint(60, -150 + 4 * g)));
        }
        return new PlanScenario().Card([.. items]);
    }
}
```

```csharp
// tests/UasSort.Core.Tests/Planning/DeriveBenchmarkTests.cs
using System.Diagnostics;
using System.Reflection;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class DeriveBenchmarkTests
{
    private static bool IsOptimized =>
        typeof(UasSort.Core.Planning.Planner).Assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };

    [Fact]
    public void Derive_500Items_MedianUnder50ms()
    {
        var b = BenchmarkScenario.Build().Prepare();
        Assert.Equal(500, b.Items.Length);
        var planner = PlanScenario.CreatePlanner();
        var t = new Tuning(50, 1);
        planner.Derive(b, t, [], new SessionFlags(false), 0, TestContext.Current.CancellationToken);        // warm-up (JIT)
        var times = new List<double>();
        for (var i = 1; i <= 20; i++)
        {
            var sw = Stopwatch.StartNew();
            var p = planner.Derive(b, t, [], new SessionFlags(false), i, TestContext.Current.CancellationToken);
            sw.Stop();
            Assert.Equal(12, p.Groups.Length);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        var median = (times[9] + times[10]) / 2;
        var limit = IsOptimized ? 50 : 150;
        Assert.True(median < limit, $"median derive {median:F1} ms ≥ {limit} ms ({(IsOptimized ? "Release" : "Debug")})");
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Derive_500Items_MedianUnder50ms*"`
Expected: build error CS0103 (`BenchmarkScenario` does not exist).

- [ ] **Step 3: Implement**

Create `tests/UasSort.Testing/Planning/BenchmarkScenario.cs` as shown in Step 1. If the median is over the limit, profile `Planner.Derive`: the known hot spots are `FolderDecider.FolderDays` (cached per folder and zone), `PlanningGeo.MaxPairwise` (O(n²) per group) and `EditValidator` (not on the derive path). Do not raise the limit.

- [ ] **Step 4: Run it and see it pass (Debug, then Release)**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Derive_500Items_MedianUnder50ms*"`
Expected: PASS.
Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -c Release -- --filter-method "*Derive_500Items_MedianUnder50ms*"`
Expected: PASS (median &lt; 50 ms).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Testing/Planning/BenchmarkScenario.cs tests/UasSort.Core.Tests/Planning/DeriveBenchmarkTests.cs
git commit -m "test: derive benchmark over a synthetic 500-item plan

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.19: Golden replay fixture — generator and loader

The fixture `tests/UasSort.Testing/Replay/library-listing.json` is produced **once** (Ref §13) from the library **listing** (names, sizes, mtimes, attributes; no file is opened) plus the GPS already extracted in `docs/research/spikes/djmd/calibration.json` and `docs/research/spikes/grouping/more_gps.json` — the same inputs as `replay50.py`: DJI clips whose `mvhd` was not read (Kodiak, cloud-only) get `mvhdUtc` = filename stamp + 4 h with `mvhdSimulated: true`; Anvil 0014 and 0024 get `hasMoov: false`; Autel `MAX_####.MP4` clips get no GPS and no `mvhdUtc`. It is checked in and never regenerated by tests.

**Files:**
- Create: `tools/fixtures/make-replay-fixture.ps1`
- Create (generated, then checked in): `tests/UasSort.Testing/Replay/library-listing.json`
- Modify: `tests/UasSort.Testing/UasSort.Testing.csproj` (embed the fixture)
- Create: `tests/UasSort.Testing/Replay/ReplayFixture.cs`
- Test: `tests/UasSort.Core.Tests/Planning/ReplayFixtureTests.cs`

**Interfaces:**
- Consumes: `PlanScenario`, `Clip` (06.2); `GpsFix`, `Mp4Info`, `RawItem`, `VideoUnit`, `CardEntry` (Part 02).
- Produces (Testing, `UasSort.Testing.Planning`, defined here):
  - `sealed record ReplayEntry(string RelPath, long Size, DateTime MtimeUtc, uint Attributes)`; `sealed record ReplayClip(double? Lat, double? Lon, DateTime? MvhdUtc, bool HasMoov, bool MvhdSimulated)`
  - `sealed class ReplayFixture { static ReplayFixture Load(); DateTime SnapshotUtc; string PcZone; IReadOnlyList<ReplayEntry> Entries; IReadOnlyDictionary<string, ReplayClip> Clips; IReadOnlyList<ReplayEntry> Mp4s; static string FolderOf(ReplayEntry e); static string NameOf(ReplayEntry e); RawItem CardItem(ReplayEntry e); PlanScenario Scenario(IEnumerable<ReplayEntry> card, Func<ReplayEntry, bool> listed); }`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Planning/ReplayFixtureTests.cs
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class ReplayFixtureTests
{
    [Fact]
    public void Fixture_HasTheUsersEightFoldersAndClipGps()
    {
        var f = ReplayFixture.Load();
        Assert.Equal("America/Anchorage", f.PcZone);
        var folders = f.Mp4s.GroupBy(ReplayFixture.FolderOf).ToDictionary(g => g.Key[(g.Key.LastIndexOf('/') + 1)..], g => g.Count());
        Assert.Equal(8, folders.Count);
        Assert.Equal(4, folders["2026-07-25 Council Road"]);
        Assert.Equal(21, folders["2026-07-26 Anvil Mountain"]);
        Assert.Equal(13, folders["2026-09-27 Zachar Bay"]);
        Assert.All(f.Mp4s, e => Assert.True(f.Clips.ContainsKey(ReplayFixture.NameOf(e))));
        Assert.False(f.Clips["DJI_20260727002013_0014_D.MP4"].HasMoov);
        Assert.False(f.Clips["DJI_20260727005240_0024_D.MP4"].HasMoov);
        Assert.All(f.Clips.Where(c => c.Key.StartsWith("MAX_", StringComparison.Ordinal)), c => Assert.Null(c.Value.MvhdUtc));
        Assert.DoesNotContain(f.Entries, e => e.RelPath.StartsWith(".uas-sort/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CardItem_BuildsDjiAndAutelRawItems()
    {
        var f = ReplayFixture.Load();
        var zachar = f.Mp4s.First(e => ReplayFixture.NameOf(e) == "DJI_20260927142416_0148_D.MP4");
        var r = f.CardItem(zachar);
        Assert.Equal(new DateTime(2026, 9, 27, 14, 24, 16), r.DroneStamp);
        Assert.True(r.Mp4!.First is GpsFix);
        var trunc = f.CardItem(f.Mp4s.First(e => ReplayFixture.NameOf(e) == "DJI_20260727002013_0014_D.MP4"));
        Assert.False(trunc.Mp4!.HasMoov);
        var autel = f.CardItem(f.Mp4s.First(e => ReplayFixture.NameOf(e).StartsWith("MAX_", StringComparison.Ordinal)));
        Assert.Null(autel.Mp4);
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*ReplayFixtureTests"`
Expected: build error CS0103 (`ReplayFixture` does not exist).

- [ ] **Step 3: Implement — the generator**

```powershell
# tools/fixtures/make-replay-fixture.ps1
#Requires -Version 7
<#
.SYNOPSIS
  Writes tests/UasSort.Testing/Replay/library-listing.json, the golden-replay fixture (Ref §13).
  LISTING ONLY: Get-ChildItem reads names, sizes, times and attributes; no library file is ever opened,
  so cloud-only (placeholder) files are never hydrated. Run once; the output is checked in and never regenerated by tests.
#>
param(
    [string]$VideoRoot = (Join-Path ([Environment]::GetFolderPath('MyPictures')) 'UAS Videos'),
    [string]$Out = (Join-Path $PSScriptRoot '..\..\tests\UasSort.Testing\Replay\library-listing.json'),
    [string]$Calibration = (Join-Path $PSScriptRoot '..\..\docs\research\spikes\djmd\calibration.json'),
    [string]$MoreGps = (Join-Path $PSScriptRoot '..\..\docs\research\spikes\grouping\more_gps.json'),
    [string]$PcZone = 'America/Anchorage'
)
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
$root = (Resolve-Path -LiteralPath $VideoRoot).Path.TrimEnd('\')

function ConvertTo-UtcText($value) {
    if ($null -eq $value) { return $null }
    $utc = if ($value -is [DateTime]) { $value.ToUniversalTime() }
           else { [DateTimeOffset]::Parse([string]$value, $inv).UtcDateTime }
    return $utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", $inv)
}

$gps = @{}
foreach ($r in @(Get-Content -Raw -LiteralPath $Calibration | ConvertFrom-Json) + @(Get-Content -Raw -LiteralPath $MoreGps | ConvertFrom-Json)) {
    if ($null -ne $r.lat -and -not $gps.ContainsKey($r.name)) { $gps[$r.name] = $r }
}
$noMoov = @('DJI_20260727002013_0014_D.MP4', 'DJI_20260727005240_0024_D.MP4')

$entries = [System.Collections.Generic.List[object]]::new()
$clips = [ordered]@{}
$files = Get-ChildItem -LiteralPath $root -Recurse -Force -File |
    Where-Object { -not $_.FullName.StartsWith("$root\.uas-sort\", [StringComparison]::OrdinalIgnoreCase) } |
    Sort-Object { $_.FullName.Substring($root.Length + 1) }
foreach ($f in $files) {
    $rel = $f.FullName.Substring($root.Length + 1).Replace('\', '/')
    $entries.Add([ordered]@{
        relPath    = $rel
        size       = $f.Length
        mtimeUtc   = $f.LastWriteTimeUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", $inv)
        attributes = [int]$f.Attributes
    })
    if ($rel -like 'Picture Offload/*' -or $f.Extension -ne '.mp4') { continue }
    $name = $f.Name
    $clip = [ordered]@{ lat = $null; lon = $null; mvhdUtc = $null; hasMoov = $true; mvhdSimulated = $false }
    if ($name -match '^DJI_(\d{14})_') {
        $g = $gps[$name]
        if ($g) { $clip.lat = [double]$g.lat; $clip.lon = [double]$g.lon }
        if ($noMoov -contains $name) { $clip.hasMoov = $false }
        elseif ($g -and $g.mvhd_creation_utc) { $clip.mvhdUtc = ConvertTo-UtcText $g.mvhd_creation_utc }
        else {
            $stamp = [DateTime]::ParseExact($Matches[1], 'yyyyMMddHHmmss', $inv)
            $clip.mvhdUtc = $stamp.AddHours(4).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", $inv)
            $clip.mvhdSimulated = $true
        }
    }
    $clips[$name] = $clip
}
$doc = [ordered]@{
    v           = 1
    snapshotUtc = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", $inv)
    pcZone      = $PcZone
    entries     = $entries
    clips       = $clips
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
$doc | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Out -Encoding utf8NoBOM
Write-Host "Wrote $($entries.Count) entries and $($clips.Count) clips to $Out"
```

**Run it once — user go-ahead required.** The script lists (never opens) the user's library under `C:\Users\damia\OneDrive\Pictures\UAS Videos`, which the Global Constraints otherwise put off-limits. Ask the user: "May I run `tools/fixtures/make-replay-fixture.ps1`? It lists `UAS Videos` (names, sizes, times, attributes only; no file is opened or hydrated) to write the golden-replay fixture." If the user agrees, run from the repo root:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\fixtures\make-replay-fixture.ps1
```

Expected output: `Wrote N entries and M clips to …\library-listing.json` with M = the library's MP4 count (Council 4, Anvil 21, Zachar 13, …). If the user declines, stop and ask them to run the command themselves; do not hand-write the fixture.

- [ ] **Step 4: Implement — embed and load the fixture**

Add to `tests/UasSort.Testing/UasSort.Testing.csproj`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Replay\library-listing.json" LogicalName="UasSort.Testing.Replay.library-listing.json" />
  </ItemGroup>
```

```csharp
// tests/UasSort.Testing/Replay/ReplayFixture.cs
using System.Globalization;
using System.Text.Json;

namespace UasSort.Testing.Planning;

public sealed record ReplayEntry(string RelPath, long Size, DateTime MtimeUtc, uint Attributes);
public sealed record ReplayClip(double? Lat, double? Lon, DateTime? MvhdUtc, bool HasMoov, bool MvhdSimulated);

/// <summary>The checked-in golden-replay fixture (Ref §13). Read from an embedded resource; no file IO.</summary>
public sealed class ReplayFixture
{
    public required DateTime SnapshotUtc { get; init; }
    public required string PcZone { get; init; }
    public required IReadOnlyList<ReplayEntry> Entries { get; init; }
    public required IReadOnlyDictionary<string, ReplayClip> Clips { get; init; }

    public IReadOnlyList<ReplayEntry> Mp4s => Entries
        .Where(e => e.RelPath.EndsWith(".MP4", StringComparison.OrdinalIgnoreCase)
                    && !e.RelPath.StartsWith("Picture Offload/", StringComparison.OrdinalIgnoreCase))
        .ToList();

    public static string FolderOf(ReplayEntry e) => e.RelPath[..e.RelPath.LastIndexOf('/')];
    public static string NameOf(ReplayEntry e) => e.RelPath[(e.RelPath.LastIndexOf('/') + 1)..];

    public static ReplayFixture Load()
    {
        using var s = typeof(ReplayFixture).Assembly.GetManifestResourceStream("UasSort.Testing.Replay.library-listing.json")
                      ?? throw new InvalidOperationException("Replay fixture not embedded; run tools/fixtures/make-replay-fixture.ps1 once.");
        using var doc = JsonDocument.Parse(s);
        var root = doc.RootElement;
        static DateTime Utc(string text) =>
            DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        var entries = root.GetProperty("entries").EnumerateArray().Select(e => new ReplayEntry(
            e.GetProperty("relPath").GetString()!, e.GetProperty("size").GetInt64(),
            Utc(e.GetProperty("mtimeUtc").GetString()!), (uint)e.GetProperty("attributes").GetInt32())).ToList();
        var clips = new Dictionary<string, ReplayClip>(StringComparer.Ordinal);
        foreach (var p in root.GetProperty("clips").EnumerateObject())
        {
            var c = p.Value;
            double? Num(string n) => c.GetProperty(n).ValueKind == JsonValueKind.Number ? c.GetProperty(n).GetDouble() : null;
            DateTime? Time(string n) => c.GetProperty(n).ValueKind == JsonValueKind.String ? Utc(c.GetProperty(n).GetString()!) : null;
            clips[p.Name] = new ReplayClip(Num("lat"), Num("lon"), Time("mvhdUtc"), c.GetProperty("hasMoov").GetBoolean(),
                                           c.GetProperty("mvhdSimulated").GetBoolean());
        }
        return new ReplayFixture
        {
            SnapshotUtc = Utc(root.GetProperty("snapshotUtc").GetString()!),
            PcZone = root.GetProperty("pcZone").GetString()!,
            Entries = entries,
            Clips = clips,
        };
    }

    /// <summary>A card copy of a library MP4: DJI clips carry the fixture's mvhd and GPS; Autel clips are timed from mtime.</summary>
    public RawItem CardItem(ReplayEntry e)
    {
        var name = NameOf(e);
        var stamp = UasSort.Core.Planning.PlanKeys.DjiStamp(name);
        if (stamp is null) return Clip.Autel(name, e.Size, e.MtimeUtc);
        var clip = Clips[name];
        var rel = $"DCIM/DJI_001/{name}";
        var entry = new CardEntry(rel, e.Size, e.MtimeUtc, e.MtimeUtc, e.MtimeUtc, 0x20, EntryClass.Video, null);
        GpsProbe first;
        if (clip.Lat is { } lat && clip.Lon is { } lon)
            first = new GpsFix(new GeoPoint(lat, lon), null, 0, clip.HasMoov ? GpsSource.DjmdModelTable : GpsSource.MdatHeadFallback,
                               clip.HasMoov ? "3-3-4-1" : null);
        else first = new NoFix(NoFixReason.AllProbedSamplesZero);
        var info = new Mp4Info(clip.HasMoov ? clip.MvhdUtc : (DateTime?)null, clip.HasMoov, first, null, "dvtm_Air3s.proto", null, null, null,
                               clip.HasMoov ? TimeSpan.FromSeconds(60) : (TimeSpan?)null);
        return new RawItem(new VideoUnit(new ItemId(rel), entry, false), ItemKind.Video, name, e.Size, e.MtimeUtc, stamp, info, null, null);
    }

    /// <summary>A scenario whose card holds the given MP4s and whose library listing holds every entry passing `listed`.</summary>
    public PlanScenario Scenario(IEnumerable<ReplayEntry> card, Func<ReplayEntry, bool> listed)
    {
        var s = new PlanScenario();
        foreach (var e in Entries.Where(listed)) s.LibraryFile(e.RelPath.Replace('/', '\\'), e.Size, e.MtimeUtc, e.Attributes);
        return s.Card([.. card.Select(CardItem)]);
    }
}
```

- [ ] **Step 5: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*ReplayFixtureTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add tools/fixtures/make-replay-fixture.ps1 tests/UasSort.Testing/Replay tests/UasSort.Testing/UasSort.Testing.csproj tests/UasSort.Core.Tests/Planning/ReplayFixtureTests.cs
git commit -m "test: golden replay fixture generator (listing only) and embedded loader

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.20: Golden replay scenarios A0, A–E

**Files:**
- Create: `tests/UasSort.Testing/Replay/ReplayTruth.cs`
- Test: `tests/UasSort.Core.Tests/Planning/GoldenReplayTests.cs`

**Interfaces:**
- Consumes: `ReplayFixture` (06.19), `Planner` (06.11–06.14), `PlanScenario` (06.2).
- Produces (Testing, defined here): `static class ReplayTruth { string Leaf(string folderRelPath); IReadOnlyDictionary<string, string> FolderByClip(ReplayFixture f); IEnumerable<ReplayEntry> In(ReplayFixture f, string leaf); List<string> Partition(Plan p); List<string> Expected(ReplayFixture f, Func<string, string> mergeLeaf); string Leaves(ReplayFixture f, VideoGroup g); }`

Scenarios (Ref §13 table; `Tuning(50, 1)`, no PlaceIndex, empty ledger, no edits unless stated; "the user's 8 folders" = Newport RI, Kodiak, Nome Roads, Safety Roadhouse, Council Road, Anvil Mountain, Zachar Bay, Makaha Valley):
A0 every library MP4 on the card, empty library → 7 groups (Council + Anvil = one 25-clip group with one emphasised DaySplit), 7 blank NewFolders; `SplitBefore(first Anvil clip)` → the user's 8 folders with a `UserSplit` boundary; R {8, 10, 20, 30, 33} → 8, {40, 50, 60} → 7 · A card = library → 8 AlreadyImported, 7 fold, Anvil unfolded (2 unfinished), watermark 2026-09-27T18:24:16Z · B minus Zachar Bay → NewFolder `2026\2026-09\2026-09-27` (13 clips) · C minus Zachar 0140–0148 → Append High, 4 New · D Council + Anvil clips, library minus Anvil → Append(Council Road) Medium "different day, 34 mi from Council Road" with `SplitBefore(first Anvil clip)`; either click → AlreadyImported + NewFolder `2026\2026-07\2026-07-26` · E Anvil clips only → NewFolder `2026\2026-07\2026-07-26`, Council Road among the append candidates.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Testing/Replay/ReplayTruth.cs
using UasSort.Core.Planning;

namespace UasSort.Testing.Planning;

/// <summary>The user's folder assignment of every library MP4, as partitions comparable with a plan.</summary>
public static class ReplayTruth
{
    public static string Leaf(string folderRelPath) => folderRelPath[(folderRelPath.LastIndexOf('/') + 1)..];

    public static IReadOnlyDictionary<string, string> FolderByClip(ReplayFixture f) =>
        f.Mp4s.ToDictionary(ReplayFixture.NameOf, e => Leaf(ReplayFixture.FolderOf(e)), StringComparer.Ordinal);

    public static IEnumerable<ReplayEntry> In(ReplayFixture f, string leaf) => f.Mp4s.Where(e => Leaf(ReplayFixture.FolderOf(e)) == leaf);

    public static List<string> Partition(Plan p) =>
        p.Groups.Select(g => string.Join("|", g.Videos.Select(v => PlanKeys.FileName(v.CardRelPath)).Order(StringComparer.Ordinal)))
                .Order(StringComparer.Ordinal).ToList();

    public static List<string> Expected(ReplayFixture f, Func<string, string> mergeLeaf) =>
        FolderByClip(f).GroupBy(kv => mergeLeaf(kv.Value))
                       .Select(g => string.Join("|", g.Select(kv => kv.Key).Order(StringComparer.Ordinal)))
                       .Order(StringComparer.Ordinal).ToList();

    public static string Leaves(ReplayFixture f, VideoGroup g)
    {
        var truth = FolderByClip(f);
        return string.Join("+", g.Videos.Select(v => truth[PlanKeys.FileName(v.CardRelPath)]).Distinct().Order(StringComparer.Ordinal));
    }
}
```

```csharp
// tests/UasSort.Core.Tests/Planning/GoldenReplayTests.cs
using UasSort.Core.Planning;
using UasSort.Testing.Planning;
using static UasSort.Testing.Planning.ReplayTruth;

namespace UasSort.Core.Tests.Planning;

public sealed class GoldenReplayTests
{
    private const string Council = "2026-07-25 Council Road";
    private const string Anvil = "2026-07-26 Anvil Mountain";
    private const string Zachar = "2026-09-27 Zachar Bay";
    private static readonly ReplayFixture F = ReplayFixture.Load();

    private static bool InFolder(ReplayEntry e, string leaf) => e.RelPath.Contains($"/{leaf}/", StringComparison.Ordinal);
    private static string Full(string rel) => $@"{PlanScenario.VideoRoot}\{rel}";
    private static VideoGroup GroupWith(Plan p, string leaves) => p.Groups.Single(g => Leaves(F, g) == leaves);

    private static ItemId FirstAnvil(PlanBase b)
    {
        var anvil = In(F, Anvil).Select(ReplayFixture.NameOf).ToHashSet();
        return b.Items.First(i => anvil.Contains(i.Raw.Name)).Raw.Unit.Id;          // Items are in (CaptureUtc, Id) order
    }

    [Fact] // A0: clustering alone
    public void A0_EveryClipEmptyLibrary_SevenGroups_SplitGivesTheUsersEight()
    {
        var b = F.Scenario(F.Mp4s, _ => false).Prepare();
        var p = PlanScenario.Derive(b);

        Assert.Equal(7, p.Groups.Length);
        Assert.All(p.Groups, g => Assert.IsType<NewFolder>(g.Target));
        Assert.Equal(7, p.Issues.Count(i => i.Code == IssueCode.EmptyFolderName && i.Severity == IssueSeverity.Blocking));
        Assert.Equal(Expected(F, l => l == Anvil ? Council : l), Partition(p));

        var ca = GroupWith(p, $"{Council}+{Anvil}");
        Assert.Equal(25, ca.Videos.Length);
        var ds = Assert.Single(ca.DaySplits);
        Assert.True(ds.Emphasised);
        Assert.Equal((new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26)), (ds.From, ds.To));
        var w = Assert.Single(p.Issues, i => i.Code == IssueCode.EmphasisedDaySplit);
        Assert.True(w.RequiresAckAtPreflight);
        Assert.Equal(ds.FirstOfDay, w.Anchor);

        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-27"), GroupWith(p, Zachar).Target);
        Assert.Equal(new NewFolder(@"2026\2026-07\2026-07-25"), ca.Target);
        Assert.Equal(new NewFolder(@"2026\2026-05\2026-05-22"), GroupWith(p, "2026-05-22 Kodiak").Target);
        Assert.Equal(new NewFolder(@"2022\2022-03\2022-03-27"), GroupWith(p, "2022-03-27 Makaha Valley").Target);

        var split = PlanScenario.Derive(b, edits: [new SplitBefore(FirstAnvil(b))]);
        Assert.Equal(8, split.Groups.Length);
        Assert.Equal(Expected(F, l => l), Partition(split));
        var anvilIdx = split.Groups.IndexOf(GroupWith(split, Anvil));
        Assert.Equal(BoundaryCause.UserSplit, split.Boundaries[anvilIdx - 1].Cause);
    }

    [Theory] // A0 R sweep (replay50.py: 8 groups for R 8–33 mi, 7 for R 40–60 mi)
    [InlineData(8, 8)]
    [InlineData(10, 8)]
    [InlineData(20, 8)]
    [InlineData(30, 8)]
    [InlineData(33, 8)]
    [InlineData(40, 7)]
    [InlineData(50, 7)]
    [InlineData(60, 7)]
    public void A0_RadiusSweep(int radiusMiles, int groups)
    {
        var b = F.Scenario(F.Mp4s, _ => false).Prepare();
        Assert.Equal(groups, PlanScenario.Derive(b, new Tuning(radiusMiles, 1)).Groups.Length);
    }

    [Fact] // A: card = library
    public void A_CardEqualsLibrary_EightAlreadyImported_AnvilUnfolded()
    {
        var b = F.Scenario(F.Mp4s, _ => true).Prepare();
        var p = PlanScenario.Derive(b);
        Assert.Equal(8, p.Groups.Length);
        Assert.All(p.Groups, g => Assert.Equal(Leaves(F, g), Leaf(Assert.IsType<AlreadyImported>(g.Target).Folder.FullPath.Replace('\\', '/'))));
        Assert.Equal(7, p.Groups.Count(g => g.Foldable));
        var anvil = GroupWith(p, Anvil);
        Assert.False(anvil.Foldable);
        Assert.Equal(2, anvil.Videos.Count(v => b.Items.Single(i => i.Raw.Unit.Id == v).Flags.HasFlag(ItemFlags.Truncated)));
        Assert.Equal(new DateTime(2026, 9, 27, 18, 24, 16, DateTimeKind.Utc), b.WatermarkUtc);
    }

    [Fact] // B: library minus the Zachar Bay folder
    public void B_WithoutZacharFolder_NewFolderOf13()
    {
        var p = PlanScenario.Derive(F.Scenario(F.Mp4s, e => !InFolder(e, Zachar)).Prepare());
        Assert.Equal(7, p.Groups.Count(g => g.Target is AlreadyImported));
        var z = GroupWith(p, Zachar);
        Assert.Equal((new NewFolder(@"2026\2026-09\2026-09-27"), 13), (z.Target, z.Videos.Length));
        Assert.Contains(p.Issues, i => i.Code == IssueCode.EmptyFolderName && i.Severity == IssueSeverity.Blocking && i.Anchor == z.Id.Anchor);
    }

    [Fact] // C: library minus Zachar 0140–0148
    public void C_WithoutLastZacharClips_AppendHigh()
    {
        static bool Late(ReplayEntry e)
        {
            var name = ReplayFixture.NameOf(e);
            return InFolder(e, Zachar) && name.StartsWith("DJI_", StringComparison.Ordinal) && name.Length > 23
                   && int.Parse(name.AsSpan(19, 4)) is >= 140 and <= 148;
        }
        var b = F.Scenario(F.Mp4s, e => !Late(e)).Prepare();
        var p = PlanScenario.Derive(b);
        Assert.Equal(7, p.Groups.Count(g => g.Target is AlreadyImported));
        var z = GroupWith(p, Zachar);
        var a = Assert.IsType<Append>(z.Target);
        Assert.Equal((Full(@"2026\2026-09\2026-09-27 Zachar Bay"), Confidence.High, "same day as clips already in this folder"),
                     (a.Folder.FullPath, a.Confidence, a.Why));
        Assert.Equal(13, z.Videos.Length);
        Assert.Equal(4, z.Videos.Count(v => b.Items.Single(i => i.Raw.Unit.Id == v).Newness is IsNew));
    }

    [Fact] // D: Council leftovers + Anvil clips, library lacks Anvil Mountain
    public void D_CouncilLeftovers_AppendMediumWithHint_EitherClickSplits()
    {
        var b = F.Scenario(In(F, Council).Concat(In(F, Anvil)), e => !InFolder(e, Anvil)).Prepare();
        var p = PlanScenario.Derive(b);
        var g = Assert.Single(p.Groups);
        Assert.Equal(25, g.Videos.Length);
        var a = Assert.IsType<Append>(g.Target);
        Assert.Equal((Full(@"2026\2026-07\2026-07-25 Council Road"), Confidence.Medium, "different day, 34 mi from Council Road"),
                     (a.Folder.FullPath, a.Confidence, a.Why));
        var firstAnvil = FirstAnvil(b);
        Assert.Equal(new SplitBefore(firstAnvil), Assert.Single(a.Hint!.Fix));
        Assert.True(Assert.Single(p.Issues, i => i.Code == IssueCode.MediumAppend).RequiresAckAtPreflight);
        var split = Assert.Single(p.Issues, i => i.Code == IssueCode.EmphasisedDaySplit);
        Assert.True(split.RequiresAckAtPreflight);

        foreach (var fix in new[] { a.Hint!.Fix, split.QuickFixes[0].Edits })
        {
            var after = PlanScenario.Derive(b, edits: [.. fix]);
            Assert.Equal(2, after.Groups.Length);
            Assert.Equal("Council Road", Assert.IsType<AlreadyImported>(after.Groups[0].Target).Folder.Description);
            Assert.Equal(new NewFolder(@"2026\2026-07\2026-07-26"), after.Groups[1].Target);
            Assert.Contains(after.Issues, i => i.Code == IssueCode.EmptyFolderName && i.Anchor == after.Groups[1].Id.Anchor);
        }
    }

    [Fact] // E: Anvil clips only, library lacks Anvil Mountain
    public void E_AnvilOnly_NewFolder_CouncilOffered()
    {
        var p = PlanScenario.Derive(F.Scenario(In(F, Anvil), e => !InFolder(e, Anvil)).Prepare());
        var g = Assert.Single(p.Groups);
        Assert.Equal(new NewFolder(@"2026\2026-07\2026-07-26"), g.Target);
        Assert.Contains(p.Issues, i => i.Code == IssueCode.EmptyFolderName);
        Assert.Contains(Planner.AppendCandidates(p, g.Id), f => f.Ref.Description == "Council Road");
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*GoldenReplayTests"`
Expected: build error CS0103 (`ReplayTruth` members `Leaves`, `Partition`, `Expected`, `In` not found).

- [ ] **Step 3: Implement**

Create `tests/UasSort.Testing/Replay/ReplayTruth.cs` as shown in Step 1. The expectations are the Ref §13 table and never change; a failure is a bug in `Clusterer`, `FolderDecider` or `Planner` (or Part 04/05 for times, zones and the watermark) — fix it there.

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*GoldenReplayTests"`
Expected: PASS (6 facts + 8 theory rows).

- [ ] **Step 5: Commit**

```bash
git add tests/UasSort.Testing/Replay/ReplayTruth.cs tests/UasSort.Core.Tests/Planning/GoldenReplayTests.cs
git commit -m "test: golden replay scenarios A0 and A-E against the checked-in fixture

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

### Task 06.21: ScanService and the scan half of the fake-FS tripwire

**Files:**
- Create: `src/UasSort.Core/Planning/ScanService.cs`
- Create: `tests/UasSort.Testing/FakeLedgerStore.cs` (namespace `UasSort.Testing`; the one fake `ILedgerStore` of the build — Part 07 Task 07.4 completes its write members in this same file)
- Test: `tests/UasSort.Core.Tests/Planning/ScanServiceTripwireTests.cs`

**Interfaces:**
- Consumes:
  - Ports and model (Part 02, namespace `UasSort.Core`): `IDirectoryLister.Enumerate(string, bool, IReadOnlySet<string>)`, `ICardReaderFactory.Open(CardSource, CardIdentity)`, `ILedgerStore.Check()/Load()`, `IVolumeProvider.GetVolumes()`, `ITimeZoneResolver`; `ScanProgress`, `ScanPhase`, `ScanWarning`, `RootListing`, `LibraryListings`, `LedgerPaths`, `PathRules`, `GuardContext`, `IoOp`.
  - `new CardClassifier(clock).Classify(CardSource, ListingResult)` (Part 02, instance with a `TimeProvider`), `MetadataHarvester.HarvestAsync(CardInventory, ICardReader, IProgress<ScanProgress>, CancellationToken)` (Part 03, **static**), `DroneClock.Learn` (Part 04), `LibraryIndex.Build`, `LedgerSnapshots.Empty`, `LedgerFolderFacts`, `LedgerFolderStatusBuilder.Build`, `LedgerLoader.Load` (Part 05).
  - Test fakes (Part 02 Tasks 02.8–02.9, exact members): `FakeFileSystem(GuardContext context)` (`: IDirectoryLister`); `AddFile(string path, byte[] content, DateTime mtimeUtc, uint attributes = ArchiveAttribute)`; `AddCardVolume(string root, CardIdentity identity, CardSpace space)`; `Metadata(string path) → FsEntry?`; `OpenRead(string path, GuardContext? context = null)`; `GuardLog` (`FakeGuardCall(IoOp Op, string Path, string Decision)` for every guarded call — the opened paths are the `ReadData`/`AppendOwnLedger` entries); `CardDeleteViolations`; `HydrationViolation` is thrown on a placeholder or pre-existing library open outside the ledger exemption (Ref §4.3). `FakeCardReaderFactory(FakeFileSystem fs) : ICardReaderFactory` (the fake FS is not itself a reader factory); `FakeLayout.CardSpace`.
- Produces:
  - `sealed class ScanService { static readonly IReadOnlySet<string> LibraryExcludes /* {".uas-sort"} */; ScanService(Settings settings, IDirectoryLister lister, ILedgerStore ledger, IVolumeProvider volumes, ITimeZoneResolver tz, TimeZoneInfo pcZone, TimeProvider clock); Task<ScanResult> ScanAsync(CardSource source, ICardReaderFactory readers, IProgress<ScanProgress> progress, CancellationToken ct); }` — lists the card (`{}` excludes) and every root (`{".uas-sort"}`) in parallel; `Check()` first and **never** `Load()` when the status is `CloudOnly`, `Missing`, `Empty` or `VideoRootMissing` (`LedgerSnapshots.Empty(status)` carries the status); harvests sequentially; learns the clock; builds the index. The caller runs `Planner.Prepare` and the draft check. Identity: `source.Identity`, else the identity of the volume holding `source.Root`.
  - Testing: `sealed class FakeLedgerStore(FakeFileSystem? fs /* null = in memory */, string videoRoot, string machine, LedgerSnapshot? snapshot = null) : ILedgerStore` with `string Folder` (= `LedgerPaths.For(videoRoot)`), `List<string> Calls` (every call appends its member name), `LedgerFolderStatus? StatusOverride`, `bool CheckThrows`; `Check()` = `StatusOverride ?? LedgerFolderStatusBuilder.Build(videoRoot, machine, facts read from fs metadata and a top-level listing)` (throws `IOException` when `CheckThrows`); `Load()` = `LedgerLoader.Load(status, p => fs.OpenRead(p))`, or the in-memory `snapshot` (else `LedgerSnapshots.Empty(status)`) when `fs` is null; `EnsureFolder`, `OpenOwn`, `SnapshotToBackup`, `KeepOnDevice`, `CopyInto` throw `NotSupportedException` until Part 07 Task 07.4 implements them in place (and adds `FakeLedgerWriter Writer`, `bool EnsureFolderThrows`).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Testing/FakeLedgerStore.cs
namespace UasSort.Testing;

/// <summary>The one fake <see cref="ILedgerStore"/> (registry decision 6). Reads go through Part 05's builder and loader over the
/// fake file system (every open is guarded and logged), or return an in-memory snapshot when no file system is given.
/// Part 07 Task 07.4 implements the write members in this file.</summary>
public sealed class FakeLedgerStore(FakeFileSystem? fs, string videoRoot, string machine, LedgerSnapshot? snapshot = null) : ILedgerStore
{
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

    public void EnsureFolder() => throw NotYet(nameof(EnsureFolder));
    public ILedgerWriter OpenOwn() => throw NotYet(nameof(OpenOwn));
    public void SnapshotToBackup(string runId) => throw NotYet(nameof(SnapshotToBackup));
    public void KeepOnDevice() => throw NotYet(nameof(KeepOnDevice));
    public void CopyInto(string newVideoRoot, LedgerSnapshot current) => throw NotYet(nameof(CopyInto));

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

    private NotSupportedException NotYet(string member)
    {
        Calls.Add(member);
        return new NotSupportedException($"FakeLedgerStore.{member} is completed by Part 07 Task 07.4.");
    }
}
```

```csharp
// tests/UasSort.Core.Tests/Planning/ScanServiceTripwireTests.cs
using UasSort.Core.Geo;
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class ScanServiceTripwireTests
{
    private const string VideoRoot = @"C:\lib\UAS Videos";
    private const string PhotoRoot = @"C:\lib\UAS Videos\Picture Offload";
    private const string Ledger = @"C:\lib\UAS Videos\.uas-sort";
    private const uint CloudOnlyLibrary = 0x401620;          // RecallOnDataAccess | Offline | … (Ref §13)
    private static readonly DateTime T0 = new(2026, 9, 27, 18, 2, 57, DateTimeKind.Utc);
    private static readonly CardIdentity Card = new(0x1A2B3C4D, "DJI", "exFAT", 256_060_514_304);

    private sealed class NoVolumes : IVolumeProvider { public IReadOnlyList<VolumeInfo> GetVolumes() => []; }

    private sealed class ListProgress : IProgress<ScanProgress>
    {
        public List<ScanPhase> Phases { get; } = [];
        public void Report(ScanProgress value) { lock (Phases) Phases.Add(value.Phase); }
    }

    private static Settings SettingsFor() => new(1, VideoRoot, PhotoRoot, [], 50, 1, StoredClockMode.Zone, "America/New_York", true,
        new MapSettings("streets", "", "", "", ImmutableDictionary<string, string>.Empty), new LayoutSettings(420, 0.55), true);

    private static FakeFileSystem Fixture(bool cloudOnlyLedgerB)
    {
        var ctx = new GuardContext(VideoRoot, PhotoRoot, [], @"E:\", @"C:\appdata\uas-sort", "PC1",
                                   new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), @"C:\", false, null);
        var fs = new FakeFileSystem(ctx);
        fs.AddCardVolume(@"E:\", Card, FakeLayout.CardSpace);
        foreach (var n in new[] { "DJI_20260927140127_0123_D.MP4", "DJI_20260927140144_0124_D.MP4" })
        {
            fs.AddFile($@"E:\DCIM\DJI_001\{n}", new byte[4096], T0);                                              // probe errors are fine
            fs.AddFile($@"{VideoRoot}\2026\2026-09\2026-09-27 Zachar Bay\{n}", new byte[4096], T0, CloudOnlyLibrary);  // must never be opened
        }
        fs.AddFile($@"{PhotoRoot}\DJI_20260927141000_0125_D.DNG", new byte[16], T0, CloudOnlyLibrary);
        fs.AddFile($@"{Ledger}\ledger-A.jsonl", [], T0);                                                          // local, readable
        if (cloudOnlyLedgerB) fs.AddFile($@"{Ledger}\ledger-B.jsonl", [], T0, 0x400000);
        fs.AddFile($@"{Ledger}\notes.txt", [1, 2, 3], T0);
        fs.AddFile($@"{Ledger}\sub\ledger-C.jsonl", [], T0);
        fs.AddFile($@"{Ledger}\DJI_20260927150000_0999_D.MP4", new byte[16], T0);                              // stray media: never indexed
        return fs;
    }

    private static ScanService Service(FakeFileSystem fs) =>
        new(SettingsFor(), fs, new FakeLedgerStore(fs, VideoRoot, "PC1"), new NoVolumes(), new GeoTimeZoneResolver(),
            TimeZoneInfo.FindSystemTimeZoneById("America/Anchorage"), new FakeTimeProvider(new DateTimeOffset(T0.AddDays(7))));

    /// <summary>Every path the scan opened (any mode): the guarded ReadData / AppendOwnLedger calls of the fake file system.</summary>
    private static List<string> Opened(FakeFileSystem fs) =>
        [.. fs.GuardLog.Where(c => c.Op is IoOp.ReadData or IoOp.AppendOwnLedger).Select(c => c.Path)];

    [Fact] // fake-FS hydration tripwire, scan + plan half (Ref §13)
    public async Task ScanAndPlan_OpenNoLibraryFile_OnlyTheLocalLedger()
    {
        var fs = Fixture(cloudOnlyLedgerB: false);
        var progress = new ListProgress();
        var scan = await Service(fs).ScanAsync(new CardSource(@"E:\", Card, false, false), new FakeCardReaderFactory(fs), progress,
                                               TestContext.Current.CancellationToken);
        var plan = PlanScenario.Derive(PlanScenario.CreatePlanner().Prepare(scan));
        var opened = Opened(fs);

        Assert.Equal(2, scan.Raw.Length);
        Assert.Contains(@$"{Ledger}\ledger-A.jsonl", opened, StringComparer.OrdinalIgnoreCase);
        Assert.All(opened, path => Assert.True(
            path.StartsWith(@"E:\", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, $@"{Ledger}\ledger-A.jsonl", StringComparison.OrdinalIgnoreCase), $"opened {path}"));
        Assert.DoesNotContain(opened, p => p.EndsWith("notes.txt", StringComparison.OrdinalIgnoreCase) || p.Contains(@"\sub\", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(fs.HydrationViolations);
        Assert.Empty(fs.CardDeleteViolations);
        Assert.Empty(scan.Library.Match(PlanKeys.Key("DJI_20260927150000_0999_D.MP4", 16)));    // .uas-sort is not indexed
        Assert.Single(scan.Library.Match(PlanKeys.Key("DJI_20260927140127_0123_D.MP4", 4096)));
        Assert.All(plan.Groups, g => Assert.IsType<AlreadyImported>(g.Target));
        Assert.Contains(ScanPhase.ListingCard, progress.Phases);
        Assert.Contains(ScanPhase.ReadingLedger, progress.Phases);
    }

    [Fact] // a cloud-only ledger-B opens nothing and gives Blocking CloudOnly
    public async Task CloudOnlyLedger_OpensNoLedgerFile_BlockingIssue()
    {
        var fs = Fixture(cloudOnlyLedgerB: true);
        var scan = await Service(fs).ScanAsync(new CardSource(@"E:\", Card, false, false), new FakeCardReaderFactory(fs), new ListProgress(),
                                               TestContext.Current.CancellationToken);
        Assert.Equal(LedgerFolderState.CloudOnly, scan.Ledger.Status.State);
        Assert.DoesNotContain(Opened(fs), p => p.StartsWith(Ledger, StringComparison.OrdinalIgnoreCase));
        var plan = PlanScenario.Derive(PlanScenario.CreatePlanner().Prepare(scan));
        Assert.Equal(IssueSeverity.Blocking, Assert.Single(plan.Issues, i => i.Code == IssueCode.LedgerCloudOnly).Severity);
        Assert.Empty(fs.CardDeleteViolations);
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*ScanServiceTripwireTests"`
Expected: build error CS0246 (`ScanService` not found). (Write `tests/UasSort.Testing/FakeLedgerStore.cs` first, as shown above, so the only missing type is `ScanService`.)

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Core/Planning/ScanService.cs
using UasSort.Core.Card;
using UasSort.Core.Ledger;
using UasSort.Core.Media;
using UasSort.Core.Time;

namespace UasSort.Core.Planning;

/// <summary>The Plan stage's scan (Ref §4.2, §4.4 step 4): card and roots listed in parallel, ledger after Check(), sequential harvest.</summary>
public sealed class ScanService
{
    public static readonly IReadOnlySet<string> LibraryExcludes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { LedgerPaths.FolderName };
    private static readonly IReadOnlySet<string> NoExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly Settings _settings;
    private readonly IDirectoryLister _lister;
    private readonly ILedgerStore _ledger;
    private readonly IVolumeProvider _volumes;
    private readonly ITimeZoneResolver _tz;
    private readonly TimeZoneInfo _pc;
    private readonly TimeProvider _clock;

    public ScanService(Settings settings, IDirectoryLister lister, ILedgerStore ledger, IVolumeProvider volumes,
                       ITimeZoneResolver tz, TimeZoneInfo pcZone, TimeProvider clock)
    {
        _settings = settings;
        _lister = lister;
        _ledger = ledger;
        _volumes = volumes;
        _tz = tz;
        _pc = pcZone;
        _clock = clock;
    }

    public async Task<ScanResult> ScanAsync(CardSource source, ICardReaderFactory readers, IProgress<ScanProgress> progress, CancellationToken ct)
    {
        var identity = source.Identity ?? IdentityOf(source.Root)
                       ?? throw new InvalidOperationException($"No volume found for {source.Root}");
        var reader = readers.Open(source, identity);

        progress.Report(new ScanProgress(ScanPhase.ListingCard, 0, 1, source.Root));
        var cardTask = Task.Run(() => _lister.Enumerate(source.Root, true, NoExcludes), ct);
        var roots = new List<(string Root, DestRoot Kind, bool Previous)>
        {
            (_settings.VideoRoot, DestRoot.Video, false),
            (_settings.PhotoRoot, DestRoot.Photo, false),
        };
        roots.AddRange(_settings.PreviousPhotoRoots.Select(r => (r, DestRoot.Photo, true)));
        progress.Report(new ScanProgress(ScanPhase.ListingLibrary, 0, roots.Count, null));
        var rootTasks = roots.Select(r => Task.Run(() => ListRoot(r.Root, r.Kind, r.Previous), ct)).ToList();
        var cardListing = await cardTask.ConfigureAwait(false);
        var rootListings = await Task.WhenAll(rootTasks).ConfigureAwait(false);
        var inventory = new CardClassifier(_clock).Classify(source, cardListing);
        ct.ThrowIfCancellationRequested();

        progress.Report(new ScanProgress(ScanPhase.ReadingLedger, 0, 1, null));
        var status = _ledger.Check();                                           // attributes only, before any open
        var ledger = status.State is LedgerFolderState.CloudOnly or LedgerFolderState.Missing
                                  or LedgerFolderState.Empty or LedgerFolderState.VideoRootMissing
            ? LedgerSnapshots.Empty(status)
            : _ledger.Load();
        ct.ThrowIfCancellationRequested();

        var raw = new List<RawItem>(inventory.Units.Length);
        await foreach (var r in MetadataHarvester.HarvestAsync(inventory, reader, progress, ct).ConfigureAwait(false))
            raw.Add(r);

        var clock = DroneClock.Learn(raw, _settings.DroneClockMode, _settings.DroneClockZone, _tz, _pc);
        var listings = new LibraryListings(rootListings[0], rootListings[1], [.. rootListings.Skip(2)]);
        var lib = LibraryIndex.Build(listings, ledger, clock);
        var warnings = inventory.Warnings.AddRange(rootListings
            .Where(l => l.Available)
            .SelectMany(l => l.Listing.Errors.Select(e =>
                new ScanWarning("LibraryListingError", $"Can't list {e.Path} (error {e.Win32Error})", null, false))));
        progress.Report(new ScanProgress(ScanPhase.BuildingPlan, 0, 1, null));
        return new ScanResult(inventory, [.. raw], lib, ledger, clock, warnings, _settings);
    }

    private RootListing ListRoot(string root, DestRoot kind, bool previous)
    {
        var listing = _lister.Enumerate(root, true, LibraryExcludes);
        var missing = listing.Errors.Any(e => SamePath(e.Path, root) && e.Win32Error is 2 or 3);   // file / path not found
        return new RootListing(root, kind, previous, !missing, listing);
    }

    private CardIdentity? IdentityOf(string root)
    {
        var volumeRoot = Path.GetPathRoot(root);
        return volumeRoot is null ? null : _volumes.GetVolumes().FirstOrDefault(v => SamePath(v.Root, volumeRoot))?.Identity;
    }

    private static bool SamePath(string a, string b) => PathRules.Equal(a, b);
}
```

- [ ] **Step 4: Run it and see it pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*ScanServiceTripwireTests"`
Expected: PASS (2 tests; no `HydrationViolation`, no `CardDeleteViolation`).

- [ ] **Step 5: Run the whole suite and the Release benchmark**

Run: `dotnet test --solution uas-sort.slnx`
Expected: PASS (every test of Parts 01–06).
Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -c Release -- --filter-method "*Derive_500Items_MedianUnder50ms*"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/UasSort.Core/Planning/ScanService.cs tests/UasSort.Testing/FakeLedgerStore.cs tests/UasSort.Core.Tests/Planning/ScanServiceTripwireTests.cs
git commit -m "feat: ScanService with parallel listings, attribute-first ledger load and scan tripwire tests

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

## Spec gaps and interpretations (Part 06)

Recorded here, not silently resolved; each is covered by a named test so a different ruling changes one place.

1. **`NothingNew` is not in the closed `IssueCode` catalogue** (Ref §3, §9.10), but Review Focus #1 asks for an explicit state. Task 06.1 adds `IssueCode.NothingNew` (Blocking, raised by `Planner.Derive` when every item is Imported or Decided) — decided (registry decision 14). Review Focus #1 reads "every AlreadyImported group folded; NothingToCopy groups never fold" (main spec §5.6: `NothingToCopy` groups, e.g. ledger-only imports of culled files, are never folded); the Review Focus test pins both halves.
2. **"New" in the FolderDecider** is read as *included* `IsNew` plus *included* `Conflict` (Ref §8.5 says inclusion is resolved first). So a group whose only New clip is an unticked truncated clip is `AlreadyImported`/`NothingToCopy` (summary part "N unticked"), not a blank NewFolder. The fold rule additionally keeps a group with an unticked New clip unfolded.
3. **Two-pass UserSplit exclusion never removes the group's own wall.** Taken literally (§8.9 step 4), when F is B's wall and A's pass-1 target, both A and B would lose F. The Ref test says only "A becomes NewFolder", so B keeps its wall row (test `TwoPass_WallOfNeighbour_ExcludedForOtherSide`).
4. **`GroupDraft.UserSplitNeighbour` is a single id**, yet the rule needs neighbours on both sides. It holds the earlier neighbour; the later one is found as the draft whose `UserSplitNeighbour` is this group.
5. **Rename on a "Folder exists; appending" group** is allowed (the user's own name caused the Append; otherwise the name could never be changed back). All other Append/AlreadyImported renames stay `Rejected(RenameExistingFolder)`.
6. **Ref §4.2 marks few members `static`.** Resolved by registry decision 8: `TimeResolver` and `MetadataHarvester` are static classes (`TimeResolver.Resolve(...)`, `MetadataHarvester.HarvestAsync(...)`), `CardClassifier` is an instance built with a `TimeProvider` (`new CardClassifier(clock)`). This part makes its own pure helpers static (`NewnessRules`, `Clusterer`, `DaySplitFinder`, `FolderNamer`, `SetFolderNamer`, `DescriptionSuggester`, `EditValidator`) with `FolderDecider`, `Planner` (`: IPlanDeriver`, static `AppendCandidates`), `PlanSession`, `ScanService` as instances.
7. **`FolderDecider.AppendCandidates(GroupDraft, int)` has no library parameter** in Ref §4.2, so `FolderDecider` is an instance (lib, drafts, tuning) with the Ref's static `Decide(...)` overload delegating to it; `Planner.AppendCandidates(Plan, GroupId, int)` is added for the Review dropdown (Part 10), since a `Plan` carries no `GroupDraft`s.
8. **`PlanSession` members beyond Ref §4.2** (the ctor, `PreviewAsync`, `AcceptLedgerIssuesAsync`, `WhenIdleAsync`, `UndoDepth`, `CommittedTuning`, `Edits`, a `Resume` overload with `TimeProvider`). `Current` is the last committed-state plan; previews only raise `Changed`. A committed plan whose revision is not above the last published preview is re-stamped with a fresh revision.
9. **The golden-replay fixture needs a listing of the user's real library**, which the Global Constraints otherwise forbid touching. Task 06.19 asks the user before running the listing-only script (or has the user run it).
10. **`ScanService` "validates the source"** (Ref §4.2) but receives an already-validated `CardSource` (validation needs the chosen path and the detected volume, which only the Card stage has); it does not re-validate. The draft offer and `Planner.Prepare` are the caller's (Ref §4.2 signature returns `ScanResult`).
11. **`FakeFileSystem` API** (Part 02) is not spelled out in the Ref — resolved: Task 06.21 uses Part 02's actual members (`GuardLog`, `FakeCardReaderFactory`, `Metadata`, `OpenRead`, `AddCardVolume`) and adds the one fake ledger store, `UasSort.Testing.FakeLedgerStore`, built on Part 05's `LedgerFolderStatusBuilder` and `LedgerLoader` (registry decision 6).
12. **`ClockSummary` has no named builder in the Ref** — resolved: `Prepare` sets `PlanBase.Clock` from Part 04's `DroneClock.Summarize(scan.Clock, resolved)` (registry decision 45); the Planner has no summary code of its own.
13. **CheckDate window text**: the flag does not say which rule fired; the message uses 75 min for `Mvhd`/`DroneClock*` sources and 60 otherwise.

## Part 06 — Produces (summary)

| Namespace · type | Members later parts use |
|---|---|
| `UasSort.Core.Planning.Planner : IPlanDeriver` | `Planner(ITimeZoneResolver, IPlaceIndex?, TimeZoneInfo, TimeProvider)`; `PlanBase Prepare(ScanResult)` (`PlanBase.Clock` from `DroneClock.Summarize`); `Plan Derive(PlanBase, Tuning, IReadOnlyList<PlanEdit>, SessionFlags, int revision, CancellationToken)`; `static IReadOnlyList<LibraryFolder> AppendCandidates(Plan, GroupId, int max = 8)` (the VM retarget flyout calls this, never re-ranks folders); `internal static LibraryFolderRef FolderRefFor(string path, DateOnly fallback, LibraryIndex lib)` |
| `UasSort.Core.Planning.ScanService` | `ScanService(Settings, IDirectoryLister, ILedgerStore, IVolumeProvider, ITimeZoneResolver, TimeZoneInfo, TimeProvider)`; `Task<ScanResult> ScanAsync(CardSource, ICardReaderFactory, IProgress<ScanProgress>, CancellationToken)`; `static LibraryExcludes` |
| `UasSort.Core.Planning.NewnessRules` (static) | `Newness Video(VideoUnit, LibraryIndex, LedgerSnapshot)`; `Newness Photo(MediaUnit, ItemTime, LibraryIndex, LedgerSnapshot, SetPlacement?, IReadOnlySet<DateOnly>, DateTime?)`; `NearWatermarkWindow` |
| `UasSort.Core.Planning.Clusterer` (static) | `Order`; `ClusterResult Cluster(IReadOnlyList<Item>, Tuning, IReadOnlyList<PlanEdit>, out int missingEdits)` |
| `UasSort.Core.Planning.DaySplitFinder` (static) | `ImmutableArray<DaySplit> Find(IReadOnlyList<Item>)` |
| `UasSort.Core.Planning.FolderDecider` | ctor `(LibraryIndex, IReadOnlyList<GroupDraft>, Tuning)`; `static Decide(GroupDraft, IReadOnlyList<GroupDraft>, IReadOnlySet<ItemId>, LibraryIndex, Tuning, IReadOnlyDictionary<GroupId, GroupTarget>?)`; `Decide(GroupDraft, IReadOnlySet<ItemId>, …?)`; `AppendCandidates`; `SplitPoint`; `NewBeforeWallSplitPoint`; `BordersUserSplit`; `static Judged`, `static TargetFolder` |
| `UasSort.Core.Planning.PlanKeys` / `PlanningGeo` / `PlanText` / `PlanEditRefs` (static) | `NormName`, `Key`, `FileName`, `DjiStamp` (= `FileKey.NormalizeName`, `FileKey.Of`, `PathRules.FileName`, `DroneStampParser.FromFileName`) · `Haversine` (= `GeoMath.Haversine`), `Centroid`, `MaxPairwise`, `NearMiles`, `HoursGuard`, `EarthRadiusM` (= `GeoMath.EarthRadiusMeters`) · `Miles`, `ShortDate`, `DateRange`, `Offset`, `ZoneAbbrev`, `LocalTime`, `ZoneName`, `ClockZoneName`, `Zone` (= `ZoneNames.FormatOffset`/`Abbreviation`/`FormatLocal`/`Region`/`ClockName`, `Zones.Find`), `Count` · `Referenced`, `IsStructural`. Later parts call the Core helpers directly. |
| `UasSort.Core.Naming.FolderNamer` (static) | `Clean`, `NewFolderRel`, `TempPath`, `ConflictName`, `TwinName`, `MaxDescription = 80`, `MaxTempPath = 400`, `TempSuffix`, `FolderExistsWhy` |
| `UasSort.Core.Naming.SetFolderNamer` (static) | `SetPlacement Resolve(SetUnit, Item, LibraryIndex, LedgerSnapshot, ISet<string> batchTaken)` |
| `UasSort.Core.Naming.DescriptionSuggester` (static) | `IReadOnlyList<Suggestion> Suggest(GroupDraft, LibraryIndex, IPlaceIndex?)`; `Prefills(DescSource)`; `Max = 6` |
| `UasSort.Core.Editing.EditValidator` (static) | `Rejected? Validate(Plan, PlanEdit)`; `IsUnder(string, string)` |
| `UasSort.Core.Editing.PlanSession` | ctor `(PlanBase, IPlanDeriver, Tuning, TimeProvider)`; `Current`; `ApplyAsync`; `ApplyAllAsync`; `Preview`/`PreviewAsync`; `CommitTuningAsync`; `UndoAsync`/`RedoAsync`; `AcceptLedgerIssues`/`AcceptLedgerIssuesAsync`; `ToDraft`; `static Resume(PlanBase, Draft, IPlanDeriver, out int)` (+ `TimeProvider` overload); `Changed`; `CommittedTuning`, `Edits`, `UndoDepth`, `CanUndo`, `CanRedo`, `WhenIdleAsync` |
| `IssueCode.NothingNew` (added to Part 02's enum) | Blocking; Part 10 shows "Nothing new on this card" and disables Offload (Review Focus #1: every AlreadyImported group folded; NothingToCopy groups never fold) |
| `UasSort.Testing.FakeLedgerStore` (`tests/UasSort.Testing/FakeLedgerStore.cs`) | `FakeLedgerStore(FakeFileSystem? fs, string videoRoot, string machine, LedgerSnapshot? snapshot = null) : ILedgerStore`; `Folder`; `Calls`; `StatusOverride`; `CheckThrows`; `Check()` (via `LedgerFolderStatusBuilder`), `Load()` (via `LedgerLoader` + `fs.OpenRead`); the write members throw until Part 07 Task 07.4 completes them in place |
| `UasSort.Testing.GatedPlanDeriver` (`tests/UasSort.Testing/GatedPlanDeriver.cs`) | `GatedPlanDeriver(IPlanDeriver inner) : IPlanDeriver`; `Arm(match)` → `Gate` (`Entered`, `Release()`); `int CallCount`. Part 10 adds `Hold`, `Calls`, `CallStartedAsync`, `Release(int)`, `ReleaseAll`, `DeriveCall` in the same file |
| `UasSort.Testing.PlanFingerprint` (`tests/UasSort.Testing/PlanFingerprint.cs`) | `static string Of(Plan p)` (revision-independent) |
| `UasSort.Testing.Planning` (scenario builders only) | `Sites` (members `=> FixturePoints.<Name>`), `Clip` (`Vid`, `Dng`, `Set`, `Autel`, `Utc`, `Id`), `PlanScenario` (`Build`, `Prepare`, `CreatePlanner`, `Derive`, fluent library/ledger builders), `ItemFactory`, `PlanAsserts`, `DecisionScenarios`, `InvariantScenario`, `BenchmarkScenario`, `ReplayFixture`, `ReplayEntry`, `ReplayClip`, `ReplayTruth`. Place lookups in tests use Part 04's `UasSort.Testing.FakePlaceIndex`. |
| Fixture and tool | `tests/UasSort.Testing/Replay/library-listing.json` (embedded as `UasSort.Testing.Replay.library-listing.json`); `tools/fixtures/make-replay-fixture.ps1` |

Plan facts later parts rely on: `VideoGroup.Videos` in `(CaptureUtc, Id)` order (index 0 = anchor); a `NewFolder.RelPath` is always the named path (`YYYY\YYYY-MM\YYYY-MM-DD[ desc]`); `Plan.Included` holds item ids (videos, photos and sets); `ColorIndex` is `-1` for AlreadyImported; the Planner's Blocking issues are the ones Preflight re-raises (Part 07).
