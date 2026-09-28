# Part 04 — Time and geo

Goal: turn raw card items into true capture times, site zones and local dates, and give the planner place names. This part builds the drone-clock learner (`DroneClock`: SiteLocal first, then zone candidates, then NearestSample with clock-change runs, else Setting), the clock conversions behind `ClockModel.ToUtc`/`OffsetAt`, `TimeResolver` (capture-time precedence, site zone with the `Etc/*` and no-GPS fallbacks, local date, every time flag including `CheckDate` windows and `ClockMismatch` at ≥ 15 min), `GpsPlausibility` (the §6.3 step 5b gate), `GeoTimeZoneResolver`, the clock banner text and learned-mode saving, and `PlaceIndex` with its GeoNames extract tool `tools/places/build-places.cs`, which writes `src/UasSort.App/places.bin.gz`. Ref sections: §6.1–6.5, §6.3 step 5b, §8.7 (data), §13 *Time*, §14 step 4; main spec §5.2–5.3, §5.7. Review Focus item 4 (a trip across the 2026-11-01 DST end with a zone-fitted `America/New_York` clock) is tested in Task 04.9.

**Depends on:** Part 01 (solution, `Directory.Build.props`, `Directory.Packages.props` with GeoTimeZone 6.1.0 and TimeProvider.Testing 10.10.0, test projects), Part 02 (the §3 model: `ClockModel`, `ClockSample`, `ClockMode`, `StoredClockMode`, `ClockSummary`, `ClockChange`, `ItemTime`, `TimeSource`, `TzSource`, `ItemFlags`, `RawItem`, `GpsFix`, `NoFix`, `GpsProbe`, `SessionKey`, `TzLookup`, `PlaceClass`, `PlaceHit`, `ResolvedItem`, `Settings`, `MapSettings`, `LayoutSettings`, `GeoPoint`, `Distance`, the units and `CardEntry`; the §4.1 ports `ITimeZoneResolver`, `IPlaceIndex`, `IAppAssets`; `GlobalUsings.Core.cs` in every project and `src/UasSort.Core/Namespaces.cs` from Task 02.1; the `UasSort.Testing` project), Part 03 (`Mp4Info`, `StillInfo` as filled by the probes; nothing from Part 03 is called here).

**Conventions used below.** Part 02's model types and ports are in namespace `UasSort.Core` (see `00-interfaces.md`). Every project that references Core (Core, Testing, Core.Tests, …) has Part 02's fixed `GlobalUsings.Core.cs`, which imports `System.Collections.Immutable`, `UasSort.Core` and every Core folder namespace, including `UasSort.Core.Geo` and `UasSort.Core.Time` (anchored by `src/UasSort.Core/Namespaces.cs` until this part adds real types); `tests/UasSort.Core.Tests/GlobalUsings.cs` also imports `Microsoft.Extensions.Time.Testing` and `UasSort.Testing`. Files in this part therefore add `using` lines only for other namespaces and never import a namespace a global using already covers. New Core code goes in `UasSort.Core.Geo` (`src/UasSort.Core/Geo/`) and `UasSort.Core.Time` (`src/UasSort.Core/Time/`); the one exception is the `ClockModel` partial in `src/UasSort.Core/Time/ClockModel.Conversion.cs`, which is in namespace `UasSort.Core` like Part 02's declaration. Test files rely on the `<Using Include="Xunit" />` item in each test csproj (the only place `Xunit` is imported). Every test method name starts with its task's prefix so that `--filter-method` selects exactly that task's tests. Commands are the Windows `dotnet` commands; from WSL, run them through `tools/r.sh` and then `dotnet build-server shutdown`.

---

### Task 04.1: GeoMath (haversine, median)

**Files:**
- Create: `src/UasSort.Core/Geo/GeoMath.cs`
- Test: `tests/UasSort.Core.Tests/Geo/GeoMathTests.cs`

**Interfaces:**
```csharp
// Consumes (Part 02): GeoPoint(double Lat, double Lon), Distance(double Meters) { Miles; FromMiles(double) }
// Produces (defined here):
namespace UasSort.Core.Geo;
public static class GeoMath {
  public const double EarthRadiusMeters = 6_371_008.8;             // shared with map.js (Ref §3)
  public static double MetersPerDegree { get; }                    // EarthRadiusMeters × π / 180 ≈ 111,195.08
  public static Distance Haversine(GeoPoint a, GeoPoint b);
  public static GeoPoint Median(IReadOnlyList<GeoPoint> points);    // component-wise median; ArgumentException when empty
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Geo/GeoMathTests.cs
namespace UasSort.Core.Tests.Geo;

public sealed class GeoMathTests
{
    static readonly GeoPoint Anvil = new(64.5627, -165.3696);
    static readonly GeoPoint Council = new(64.6935, -164.2657);
    static readonly GeoPoint Zachar = new(57.5368, -153.7484);
    static readonly GeoPoint KodiakTown = new(57.7996, -152.3902);

    [Fact]
    public void GeoMath_Haversine_CouncilToAnvil_Is33_9Miles()
        => Assert.Equal(33.908, GeoMath.Haversine(Council, Anvil).Miles, 0.01);

    [Fact]
    public void GeoMath_Haversine_ZacharToKodiakTown_Is53_4Miles()
        => Assert.Equal(53.372, GeoMath.Haversine(Zachar, KodiakTown).Miles, 0.01);

    [Fact]
    public void GeoMath_Haversine_AcrossAntimeridian_IsShort()
        => Assert.Equal(4.263, GeoMath.Haversine(new GeoPoint(51.9, 179.95), new GeoPoint(51.9, -179.95)).Miles, 0.01);

    [Fact]
    public void GeoMath_Haversine_SamePoint_IsZero()
        => Assert.Equal(0.0, GeoMath.Haversine(Zachar, Zachar).Meters, 1e-9);

    [Fact]
    public void GeoMath_MetersPerDegree_UsesSharedEarthRadius()
        => Assert.Equal(111_195.08, GeoMath.MetersPerDegree, 0.01);

    [Fact]
    public void GeoMath_Median_TakesComponentMedians()
    {
        List<GeoPoint> odd = [new(1, 10), new(3, 30), new(2, 20)];
        Assert.Equal(new GeoPoint(2, 20), GeoMath.Median(odd));
        List<GeoPoint> even = [new(1, 10), new(2, 40), new(4, 20), new(3, 30)];
        Assert.Equal(new GeoPoint(2.5, 25), GeoMath.Median(even));
    }

    [Fact]
    public void GeoMath_Median_EmptyThrows()
        => Assert.Throws<ArgumentException>(() => GeoMath.Median(new List<GeoPoint>()));
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*GeoMath_*"`
Expected: build FAILS with `error CS0103: The name 'GeoMath' does not exist in the current context` (the namespace `UasSort.Core.Geo` already exists through Part 02's `Namespaces.cs`).

- [ ] **Step 3: Write the implementation**

```csharp
// src/UasSort.Core/Geo/GeoMath.cs
namespace UasSort.Core.Geo;

/// <summary>Great-circle distance and simple point statistics on the shared Earth radius (Ref §3 <c>Distance</c>).</summary>
public static class GeoMath
{
    public const double EarthRadiusMeters = 6_371_008.8;
    const double Rad = Math.PI / 180;

    public static double MetersPerDegree => EarthRadiusMeters * Rad;

    public static Distance Haversine(GeoPoint a, GeoPoint b)
    {
        var dLat = (b.Lat - a.Lat) * Rad;
        var dLon = (b.Lon - a.Lon) * Rad;
        var sinLat = Math.Sin(dLat / 2);
        var sinLon = Math.Sin(dLon / 2);
        var h = sinLat * sinLat + Math.Cos(a.Lat * Rad) * Math.Cos(b.Lat * Rad) * sinLon * sinLon;
        return new Distance(2 * EarthRadiusMeters * Math.Asin(Math.Min(1.0, Math.Sqrt(h))));
    }

    public static GeoPoint Median(IReadOnlyList<GeoPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0) throw new ArgumentException("At least one point is required.", nameof(points));
        return new GeoPoint(MedianOf(points.Select(p => p.Lat)), MedianOf(points.Select(p => p.Lon)));
    }

    static double MedianOf(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*GeoMath_*"`
Expected: test run summary Passed, 7 succeeded, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Geo/GeoMath.cs tests/UasSort.Core.Tests/Geo/GeoMathTests.cs
git commit -F - <<'EOF'
feat: add GeoMath haversine and median (Part 04)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 04.2: GeoTimeZoneResolver

**Files:**
- Create: `src/UasSort.Core/Geo/GeoTimeZoneResolver.cs`
- Check (modify only if needed): `src/UasSort.Core/UasSort.Core.csproj`
- Test: `tests/UasSort.Core.Tests/Geo/GeoTimeZoneResolverTests.cs`

**Interfaces:**
```csharp
// Consumes (Part 02): ITimeZoneResolver { TzLookup Resolve(GeoPoint p); }, TzLookup(string IanaId, ImmutableArray<string> Alternatives, bool IsEtc)
// Consumes (package): GeoTimeZone.TimeZoneLookup.GetTimeZone(double lat, double lon) → TimeZoneResult { string Result; List<string> AlternativeResults }
// Produces (Ref §4.2 GeoTimeZoneResolver):
namespace UasSort.Core.Geo;
public sealed class GeoTimeZoneResolver : ITimeZoneResolver {
  public TzLookup Resolve(GeoPoint p);
  public static bool IsEtc(string ianaId);                          // "Etc/…" (ocean), ordinal
}
```

- [ ] **Step 1: Check the GeoTimeZone package reference**

Run: `grep -n "GeoTimeZone" src/UasSort.Core/UasSort.Core.csproj Directory.Packages.props`
Expected: `Directory.Packages.props` has a `PackageVersion` item for GeoTimeZone `6.1.0`, and the Core csproj has a `PackageReference` item for `GeoTimeZone` without a version (Ref §2.4: Core's packages are MetadataExtractor, GeoTimeZone and System.IO.Hashing). If the Core reference is missing, add that `PackageReference` item to the csproj's package `ItemGroup`; if the version pin is missing, stop: Part 01 is incomplete.

- [ ] **Step 2: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Geo/GeoTimeZoneResolverTests.cs
namespace UasSort.Core.Tests.Geo;

public sealed class GeoTimeZoneResolverTests
{
    static readonly GeoTimeZoneResolver Resolver = new();

    [Theory]
    [InlineData(57.5368, -153.7484, "America/Anchorage")]   // Zachar Bay
    [InlineData(64.5627, -165.3696, "America/Nome")]        // Anvil Mountain
    [InlineData(41.5155, -71.2967, "America/New_York")]     // Newport RI
    [InlineData(21.47, -158.21, "Pacific/Honolulu")]        // Makaha
    [InlineData(27.7172, 85.3240, "Asia/Kathmandu")]
    [InlineData(22.5726, 88.3639, "Asia/Kolkata")]
    public void GeoTz_LandPoints_ResolveToIanaZones(double lat, double lon, string expected)
    {
        var lookup = Resolver.Resolve(new GeoPoint(lat, lon));
        Assert.Equal(expected, lookup.IanaId);
        Assert.False(lookup.IsEtc);
        Assert.False(lookup.Alternatives.IsDefault);
        Assert.Equal(expected, TimeZoneInfo.FindSystemTimeZoneById(lookup.IanaId).Id);   // ICU lookup works (Ref §2.1)
    }

    [Fact]
    public void GeoTz_OpenOcean_IsEtc()
    {
        var lookup = Resolver.Resolve(new GeoPoint(56.0, -148.0));   // Gulf of Alaska, far offshore
        Assert.True(lookup.IsEtc);
        Assert.StartsWith("Etc/", lookup.IanaId, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Etc/GMT+10", true)]
    [InlineData("America/Anchorage", false)]
    [InlineData("etc/gmt+10", false)]
    public void GeoTz_IsEtc_IsOrdinalPrefix(string id, bool expected) => Assert.Equal(expected, GeoTimeZoneResolver.IsEtc(id));
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*GeoTz_*"`
Expected: build FAILS with `error CS0246: The type or namespace name 'GeoTimeZoneResolver' could not be found`.

- [ ] **Step 4: Write the implementation**

```csharp
// src/UasSort.Core/Geo/GeoTimeZoneResolver.cs
using GeoTimeZone;

namespace UasSort.Core.Geo;

/// <summary>GeoTimeZone lookup with the <c>Etc/*</c> (open sea) results flagged (Ref §4.2, §6.4).</summary>
public sealed class GeoTimeZoneResolver : ITimeZoneResolver
{
    public TzLookup Resolve(GeoPoint p)
    {
        var result = TimeZoneLookup.GetTimeZone(p.Lat, p.Lon);
        return new TzLookup(result.Result, [.. result.AlternativeResults], IsEtc(result.Result));
    }

    public static bool IsEtc(string ianaId)
    {
        ArgumentNullException.ThrowIfNull(ianaId);
        return ianaId.StartsWith("Etc/", StringComparison.Ordinal);
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*GeoTz_*"`
Expected: test run summary Passed, 10 succeeded, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/UasSort.Core/Geo/GeoTimeZoneResolver.cs tests/UasSort.Core.Tests/Geo/GeoTimeZoneResolverTests.cs src/UasSort.Core/UasSort.Core.csproj
git commit -F - <<'EOF'
feat: add GeoTimeZoneResolver with Etc flag (Part 04)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 04.3: GpsPlausibility (generic-search gate, §6.3 step 5b)

**Files:**
- Create: `src/UasSort.Core/Geo/GpsPlausibility.cs`
- Test: `tests/UasSort.Core.Tests/Geo/GpsPlausibilityTests.cs`

**Interfaces:**
```csharp
// Consumes: GeoMath (04.1); Part 02: GpsFix, NoFix(NoFixReason), NoFixReason.GenericHitImplausible, union GpsProbe(GpsFix, NoFix), TzLookup
// Produces (Ref §4.2 GpsPlausibility):
namespace UasSort.Core.Geo;
public static class GpsPlausibility {
  public static readonly Distance MaxFirstToLast;    // 3 mi
  public static readonly Distance MaxFromCard;       // 500 mi
  public static GpsProbe Check(GpsFix first, GpsFix? last, TzLookup zone, IReadOnlyList<GeoPoint> cardPoints);
  // accepted → `first` (the caller adds ItemFlags.GpsGuessed); else NoFix(GenericHitImplausible).
  // last == null (no LastSameField) → rejected; cardPoints empty → the 500 mi test is skipped.
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Geo/GpsPlausibilityTests.cs
namespace UasSort.Core.Tests.Geo;

public sealed class GpsPlausibilityTests
{
    static readonly GeoPoint Zachar = new(57.5368, -153.7484);
    static readonly TzLookup Anchorage = new("America/Anchorage", [], false);
    static readonly TzLookup Ocean = new("Etc/GMT+10", [], true);
    static readonly List<GeoPoint> KodiakCard = [new(57.7996, -152.3902), new(57.55, -153.74)];
    static readonly List<GeoPoint> NewportCard = [new(41.5155, -71.2967), new(41.4762, -71.3237)];
    static readonly List<GeoPoint> NoCard = [];

    static GpsFix Generic(GeoPoint p, int sample) => new(p, 50, sample, GpsSource.DjmdGenericSearch, "3-9-1");
    static GeoPoint North(GeoPoint p, double miles) => new(p.Lat + miles * 1609.344 / GeoMath.MetersPerDegree, p.Lon);

    static void AssertRejected(GpsProbe probe)
        => Assert.True(probe is NoFix n && n.Reason == NoFixReason.GenericHitImplausible, "expected NoFix(GenericHitImplausible)");

    [Fact]
    public void GpsGate_ConsistentHit_IsAccepted()
    {
        var first = Generic(Zachar, 0);
        var probe = GpsPlausibility.Check(first, Generic(North(Zachar, 0.5), 299), Anchorage, KodiakCard);
        Assert.True(probe is GpsFix f && f == first);
    }

    [Fact]
    public void GpsGate_FirstAndLast2_99MiApart_IsAccepted()
        => Assert.True(GpsPlausibility.Check(Generic(Zachar, 0), Generic(North(Zachar, 2.99), 299), Anchorage, KodiakCard) is GpsFix);

    [Fact]
    public void GpsGate_FirstAndLast3_2MiApart_IsRejected()
        => AssertRejected(GpsPlausibility.Check(Generic(Zachar, 0), Generic(North(Zachar, 3.2), 299), Anchorage, KodiakCard));

    [Fact]
    public void GpsGate_EtcZone_IsRejected()
        => AssertRejected(GpsPlausibility.Check(Generic(Zachar, 0), Generic(Zachar, 299), Ocean, KodiakCard));

    [Fact]
    public void GpsGate_MoreThan500MiFromCardMedian_IsRejected()
        => AssertRejected(GpsPlausibility.Check(Generic(Zachar, 0), Generic(Zachar, 299), Anchorage, NewportCard));

    [Fact]
    public void GpsGate_NoOtherGpsOnCard_SkipsDistanceTest()
        => Assert.True(GpsPlausibility.Check(Generic(Zachar, 0), Generic(Zachar, 299), Anchorage, NoCard) is GpsFix);

    [Fact]
    public void GpsGate_NoLastSample_IsRejected()
        => AssertRejected(GpsPlausibility.Check(Generic(Zachar, 0), null, Anchorage, KodiakCard));
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*GpsGate_*"`
Expected: build FAILS with `error CS0103: The name 'GpsPlausibility' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

```csharp
// src/UasSort.Core/Geo/GpsPlausibility.cs
namespace UasSort.Core.Geo;

/// <summary>Gates generic-search djmd GPS hits (Ref §6.3 step 5b). Model-table, EXIF and mdat-fallback fixes never come here.</summary>
public static class GpsPlausibility
{
    public static readonly Distance MaxFirstToLast = Distance.FromMiles(3);
    public static readonly Distance MaxFromCard = Distance.FromMiles(500);

    public static GpsProbe Check(GpsFix first, GpsFix? last, TzLookup zone, IReadOnlyList<GeoPoint> cardPoints)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(cardPoints);

        // No last sample at the same field path means the hit can't be confirmed: fail safe (no GPS rather than a wrong one).
        if (last is null || GeoMath.Haversine(first.Point, last.Point).Meters > MaxFirstToLast.Meters)
            return new NoFix(NoFixReason.GenericHitImplausible);
        if (zone.IsEtc)
            return new NoFix(NoFixReason.GenericHitImplausible);
        if (cardPoints.Count > 0 && GeoMath.Haversine(first.Point, GeoMath.Median(cardPoints)).Meters > MaxFromCard.Meters)
            return new NoFix(NoFixReason.GenericHitImplausible);
        return first;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*GpsGate_*"`
Expected: test run summary Passed, 7 succeeded, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Geo/GpsPlausibility.cs tests/UasSort.Core.Tests/Geo/GpsPlausibilityTests.cs
git commit -F - <<'EOF'
feat: add GpsPlausibility gate for generic GPS hits (Part 04)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 04.4: Zones and ZoneNames (zone lookup cache, US zone names, abbreviations, offset text)

**Files:**
- Create: `src/UasSort.Core/Time/Zones.cs`
- Create: `src/UasSort.Core/Time/ZoneNames.cs`
- Test: `tests/UasSort.Core.Tests/Time/ZoneNamesTests.cs`

**Interfaces:**
```csharp
// Produces (defined here; used by the rest of this part, and by Parts 06, 08 and 10 for "AKDT"-style display, Ref §9.2, §9.13):
namespace UasSort.Core.Time;
public static class Zones {
  public static bool TryFind(string? id, [NotNullWhen(true)] out TimeZoneInfo? zone);   // cached FindSystemTimeZoneById; false on unknown ids
  public static TimeZoneInfo Find(string id);                                             // throws TimeZoneNotFoundException
  public static string IanaId(TimeZoneInfo zone);                                         // IANA id even for a Windows-id zone ("Alaskan Standard Time")
}
public static class ZoneNames {
  public static string Region(string ianaId);                 // "Eastern", "Central", "Mountain", "Arizona", "Pacific", "Alaska", "Hawaii", else the IANA id
  public static string ClockName(string ianaId);              // "US Eastern" … "US Pacific"; "Arizona", "Alaska", "Hawaii"; else the IANA id
  public static string Abbreviation(string ianaId, DateTime utc);   // "AKDT", "EST", "MST" (Arizona), "HST"; else "UTC+5:45"
  public static string FormatOffset(TimeSpan offset);         // "UTC−4" (U+2212), "UTC+5:30", "UTC"
  public static string FormatLocal(DateTime utc, string? ianaId);   // "Sep 27 10:55 AKDT"; no/unknown zone → "Sep 27 18:55 UTC"
  public static TimeSpan OffsetAt(string ianaId, DateTime utc);     // Zones.Find(ianaId).GetUtcOffset(utc); TimeSpan.Zero (UTC) for unknown ids
  public static bool IsUs(string ianaId);                           // the seven DroneClock.UsZones (incl. America/Phoenix, America/Anchorage, Pacific/Honolulu)
}
```

`OffsetAt` and `IsUs` are the members Review uses instead of a ZoneNames of its own (registry decision 26).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Time/ZoneNamesTests.cs
namespace UasSort.Core.Tests.Time;

public sealed class ZoneNamesTests
{
    static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("America/New_York", "Eastern")]
    [InlineData("America/Chicago", "Central")]
    [InlineData("America/Denver", "Mountain")]
    [InlineData("America/Phoenix", "Arizona")]
    [InlineData("America/Los_Angeles", "Pacific")]
    [InlineData("America/Anchorage", "Alaska")]
    [InlineData("America/Nome", "Alaska")]
    [InlineData("Pacific/Honolulu", "Hawaii")]
    [InlineData("Asia/Kathmandu", "Asia/Kathmandu")]
    public void ZoneNames_Region_UsesShortUsTable(string id, string expected) => Assert.Equal(expected, ZoneNames.Region(id));

    [Theory]
    [InlineData("America/New_York", "US Eastern")]
    [InlineData("America/Los_Angeles", "US Pacific")]
    [InlineData("America/Anchorage", "Alaska")]
    [InlineData("America/Puerto_Rico", "America/Puerto_Rico")]
    public void ZoneNames_ClockName(string id, string expected) => Assert.Equal(expected, ZoneNames.ClockName(id));

    [Fact]
    public void ZoneNames_Abbreviation_FollowsDst()
    {
        Assert.Equal("AKDT", ZoneNames.Abbreviation("America/Anchorage", Utc(2026, 9, 27, 18, 55)));
        Assert.Equal("AKST", ZoneNames.Abbreviation("America/Anchorage", Utc(2026, 12, 1, 12, 0)));
        Assert.Equal("EDT", ZoneNames.Abbreviation("America/New_York", Utc(2026, 11, 1, 5, 20)));
        Assert.Equal("EST", ZoneNames.Abbreviation("America/New_York", Utc(2026, 11, 1, 6, 20)));
        Assert.Equal("MST", ZoneNames.Abbreviation("America/Phoenix", Utc(2026, 7, 1, 12, 0)));
        Assert.Equal("HST", ZoneNames.Abbreviation("Pacific/Honolulu", Utc(2026, 7, 1, 12, 0)));
        Assert.Equal("UTC+5:45", ZoneNames.Abbreviation("Asia/Kathmandu", Utc(2026, 9, 27, 4, 30)));
    }

    [Fact]
    public void ZoneNames_FormatOffset_UsesUnicodeMinusAndMinutesWhenNeeded()
    {
        Assert.Equal("UTC\u22124", ZoneNames.FormatOffset(TimeSpan.FromHours(-4)));
        Assert.Equal("UTC\u22129:30", ZoneNames.FormatOffset(new TimeSpan(-9, -30, 0)));
        Assert.Equal("UTC+5:30", ZoneNames.FormatOffset(new TimeSpan(5, 30, 0)));
        Assert.Equal("UTC", ZoneNames.FormatOffset(TimeSpan.Zero));
    }

    [Fact]
    public void ZoneNames_FormatLocal_ShowsSiteLocalTimeWithAbbreviation()
    {
        Assert.Equal("Sep 27 10:55 AKDT", ZoneNames.FormatLocal(Utc(2026, 9, 27, 18, 55), "America/Anchorage"));
        Assert.Equal("Sep 27 18:55 UTC", ZoneNames.FormatLocal(Utc(2026, 9, 27, 18, 55), null));
        Assert.Equal("Sep 27 18:55 UTC", ZoneNames.FormatLocal(Utc(2026, 9, 27, 18, 55), "Not/AZone"));
    }

    [Fact]
    public void ZoneNames_OffsetAt_FollowsDstAndIsUtcForUnknownIds()
    {
        Assert.Equal(TimeSpan.FromHours(-8), ZoneNames.OffsetAt("America/Anchorage", Utc(2026, 9, 27, 18, 55)));
        Assert.Equal(TimeSpan.FromHours(-9), ZoneNames.OffsetAt("America/Anchorage", Utc(2026, 12, 1, 12, 0)));
        Assert.Equal(TimeSpan.FromHours(-4), ZoneNames.OffsetAt("America/New_York", Utc(2026, 11, 1, 5, 20)));
        Assert.Equal(TimeSpan.FromHours(-5), ZoneNames.OffsetAt("America/New_York", Utc(2026, 11, 1, 6, 20)));
        Assert.Equal(new TimeSpan(5, 45, 0), ZoneNames.OffsetAt("Asia/Kathmandu", Utc(2026, 9, 27, 4, 30)));
        Assert.Equal(TimeSpan.Zero, ZoneNames.OffsetAt("Not/AZone", Utc(2026, 9, 27, 18, 55)));
    }

    [Theory]
    [InlineData("America/New_York", true)]
    [InlineData("America/Chicago", true)]
    [InlineData("America/Denver", true)]
    [InlineData("America/Phoenix", true)]
    [InlineData("America/Los_Angeles", true)]
    [InlineData("America/Anchorage", true)]
    [InlineData("Pacific/Honolulu", true)]
    [InlineData("America/Puerto_Rico", false)]
    [InlineData("Europe/Berlin", false)]
    [InlineData("Asia/Kathmandu", false)]
    public void ZoneNames_IsUs_IsTheSevenUsClockZones(string id, bool expected) => Assert.Equal(expected, ZoneNames.IsUs(id));

    [Fact]
    public void ZoneNames_Zones_TryFind_CachesAndRejectsUnknownIds()
    {
        Assert.True(Zones.TryFind("America/Nome", out var nome));
        Assert.Equal("America/Nome", nome.Id);
        Assert.False(Zones.TryFind("Not/AZone", out _));
        Assert.False(Zones.TryFind(null, out _));
        Assert.Throws<TimeZoneNotFoundException>(() => Zones.Find("Not/AZone"));
    }

    [Fact]
    public void ZoneNames_Zones_IanaId_ConvertsWindowsIds()
    {
        Assert.Equal("America/Anchorage", Zones.IanaId(TimeZoneInfo.FindSystemTimeZoneById("Alaskan Standard Time")));
        Assert.Equal("America/Anchorage", Zones.IanaId(Zones.Find("America/Anchorage")));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*ZoneNames_*"`
Expected: build FAILS with `error CS0103: The name 'ZoneNames' does not exist in the current context` (and `Zones`; the namespace `UasSort.Core.Time` already exists through Part 02's `Namespaces.cs`).

- [ ] **Step 3: Write the implementation**

```csharp
// src/UasSort.Core/Time/Zones.cs
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace UasSort.Core.Time;

/// <summary>Cached IANA zone lookups through the built-in ICU-backed <see cref="TimeZoneInfo"/> (Ref §2.1).</summary>
public static class Zones
{
    static readonly ConcurrentDictionary<string, TimeZoneInfo?> Cache = new(StringComparer.Ordinal);

    public static bool TryFind(string? id, [NotNullWhen(true)] out TimeZoneInfo? zone)
    {
        zone = null;
        if (string.IsNullOrEmpty(id)) return false;
        zone = Cache.GetOrAdd(id, static key =>
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(key); }
            catch (TimeZoneNotFoundException) { return null; }
            catch (InvalidTimeZoneException) { return null; }
        });
        return zone is not null;
    }

    public static TimeZoneInfo Find(string id)
        => TryFind(id, out var zone) ? zone : throw new TimeZoneNotFoundException($"Unknown time zone '{id}'.");

    public static string IanaId(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (zone.HasIanaId) return zone.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;
    }
}
```

```csharp
// src/UasSort.Core/Time/ZoneNames.cs
using System.Collections.Frozen;
using System.Globalization;

namespace UasSort.Core.Time;

/// <summary>Display names for zones: the short US table of Ref §9.2, abbreviations for Ref §9.13, and UTC offsets.</summary>
public static class ZoneNames
{
    static readonly FrozenDictionary<string, string> Regions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["America/New_York"] = "Eastern", ["America/Detroit"] = "Eastern", ["America/Indiana/Indianapolis"] = "Eastern",
        ["America/Kentucky/Louisville"] = "Eastern",
        ["America/Chicago"] = "Central", ["America/Indiana/Knox"] = "Central", ["America/Menominee"] = "Central",
        ["America/North_Dakota/Center"] = "Central",
        ["America/Denver"] = "Mountain", ["America/Boise"] = "Mountain",
        ["America/Phoenix"] = "Arizona",
        ["America/Los_Angeles"] = "Pacific",
        ["America/Anchorage"] = "Alaska", ["America/Juneau"] = "Alaska", ["America/Nome"] = "Alaska", ["America/Sitka"] = "Alaska",
        ["America/Yakutat"] = "Alaska", ["America/Metlakatla"] = "Alaska",
        ["Pacific/Honolulu"] = "Hawaii",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // the seven US clock zones of Ref §6.1 (the same ids as DroneClock.UsZones, Task 04.6; Phoenix, Anchorage and Honolulu are among them)
    static readonly FrozenSet<string> UsIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "America/New_York", "America/Chicago", "America/Denver", "America/Phoenix", "America/Los_Angeles",
        "America/Anchorage", "Pacific/Honolulu",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static string Region(string ianaId)
    {
        ArgumentNullException.ThrowIfNull(ianaId);
        return Regions.TryGetValue(ianaId, out var region) ? region : ianaId;
    }

    public static string ClockName(string ianaId)
    {
        ArgumentNullException.ThrowIfNull(ianaId);
        if (!Regions.TryGetValue(ianaId, out var region)) return ianaId;
        return region is "Eastern" or "Central" or "Mountain" or "Pacific" ? "US " + region : region;
    }

    public static string Abbreviation(string ianaId, DateTime utc)
    {
        ArgumentNullException.ThrowIfNull(ianaId);
        var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        if (!Zones.TryFind(ianaId, out var zone)) return "UTC";
        var dst = zone.IsDaylightSavingTime(u);
        return Region(ianaId) switch
        {
            "Eastern" => dst ? "EDT" : "EST",
            "Central" => dst ? "CDT" : "CST",
            "Mountain" => dst ? "MDT" : "MST",
            "Arizona" => "MST",
            "Pacific" => dst ? "PDT" : "PST",
            "Alaska" => dst ? "AKDT" : "AKST",
            "Hawaii" => "HST",
            _ => FormatOffset(zone.GetUtcOffset(u)),
        };
    }

    public static string FormatOffset(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero) return "UTC";
        var sign = offset < TimeSpan.Zero ? "\u2212" : "+";
        var abs = offset.Duration();
        var hours = (int)abs.TotalHours;
        return abs.Minutes == 0
            ? string.Create(CultureInfo.InvariantCulture, $"UTC{sign}{hours}")
            : string.Create(CultureInfo.InvariantCulture, $"UTC{sign}{hours}:{abs.Minutes:00}");
    }

    public static string FormatLocal(DateTime utc, string? ianaId)
    {
        var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        if (ianaId is not null && Zones.TryFind(ianaId, out var zone))
            return TimeZoneInfo.ConvertTimeFromUtc(u, zone).ToString("MMM d HH:mm", CultureInfo.InvariantCulture)
                   + " " + Abbreviation(ianaId, u);
        return u.ToString("MMM d HH:mm", CultureInfo.InvariantCulture) + " UTC";
    }

    public static TimeSpan OffsetAt(string ianaId, DateTime utc)
    {
        ArgumentNullException.ThrowIfNull(ianaId);
        return Zones.TryFind(ianaId, out var zone)
            ? zone.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc))
            : TimeSpan.Zero;   // unknown id → UTC
    }

    public static bool IsUs(string ianaId)
    {
        ArgumentNullException.ThrowIfNull(ianaId);
        return UsIds.Contains(ianaId);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*ZoneNames_*"`
Expected: test run summary Passed, 29 succeeded, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Time/Zones.cs src/UasSort.Core/Time/ZoneNames.cs tests/UasSort.Core.Tests/Time/ZoneNamesTests.cs
git commit -F - <<'EOF'
feat: add zone lookup cache and zone display names (Part 04)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 04.5: ClockModel conversions (`ToUtc`, `OffsetAt`)

**Files:**
- Create: `src/UasSort.Core/Time/ClockConversion.cs`
- Create: `src/UasSort.Core/Time/ClockModel.Conversion.cs` — the second `partial` declaration of `ClockModel` (namespace `UasSort.Core`), adding `ToUtc` and `OffsetAt`. Part 02's `src/UasSort.Core/Model/Clock.cs` (the primary `public sealed partial record ClockModel(...)`, Ref §3 "clock") is not edited.
- Test: `tests/UasSort.Core.Tests/Time/ClockModelTests.cs`

**Interfaces:**
```csharp
// Consumes (Part 02): partial ClockModel(ClockMode Mode, string? ZoneId, ImmutableArray<ClockSample> Samples, TimeSpan? Modal,
//                                        StoredClockMode SettingMode, string SettingZoneId);
//                     ClockSample(DateTime DroneStamp, DateTime MvhdUtc, TimeSpan Offset, string? SiteZoneId); TimeSource
// Consumes: Zones (04.4)
// Produces (defined here):
namespace UasSort.Core.Time;
public static class ClockConversion {
  public static readonly TimeSpan SampleWindow;                      // 60 days (NearestSample, Ref §6.1)
  public static (DateTime Utc, TimeSource Src)? ToUtc(ClockModel model, DateTime droneStamp, string? siteZoneId);  // null = zone unknown
  public static TimeSpan OffsetAt(ClockModel model, DateTime droneStamp, string? siteZoneId);   // drone clock's UTC offset at that stamp
  public static TimeSource SourceFor(ClockMode mode);
  public static ClockSample? Nearest(ImmutableArray<ClockSample> samples, DateTime droneStamp, TimeSpan[]? allowedOffsets);
  // nearest by drone stamp within 60 days, ties → earlier MvhdUtc; allowedOffsets filters (ambiguous DST hour)
}
// Produces (namespace UasSort.Core, file src/UasSort.Core/Time/ClockModel.Conversion.cs; Ref §3 signatures):
namespace UasSort.Core;
public sealed partial record ClockModel {
  public (DateTime Utc, TimeSource Src)? ToUtc(DateTime droneStamp, string? siteZoneId);
  public TimeSpan OffsetAt(DateTime droneStamp, string? siteZoneId);
}
```

Conversion rules implemented (Ref §6.1): `SiteLocal` converts through `siteZoneId`, else `SettingZoneId`; `Zone` through `ZoneId`; `NearestSample` through the nearest sample's offset within 60 days, else `Modal`; `Setting` through the site zone (stored `SiteLocal`) or `SettingZoneId` (stored `Zone`). A stamp in a zone's repeated DST hour takes the offset of the nearest video sample whose offset is one of the two valid ones (Review Focus 4), else the standard offset; a stamp in a skipped hour takes the standard offset (`TimeZoneInfo.GetUtcOffset` of an invalid time).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Time/ClockModelTests.cs
namespace UasSort.Core.Tests.Time;

public sealed class ClockModelTests
{
    const string NewYork = "America/New_York";
    static DateTime L(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Unspecified);
    static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);
    static ClockSample Sample(DateTime stamp, DateTime mvhd, double offsetHours, string? zone = null)
        => new(stamp, mvhd, TimeSpan.FromHours(offsetHours), zone);

    static void AssertUtc(ClockModel m, DateTime stamp, string? site, DateTime expected, TimeSource src)
    {
        var r = m.ToUtc(stamp, site);
        Assert.NotNull(r);
        Assert.Equal(expected, r.Value.Utc);
        Assert.Equal(DateTimeKind.Utc, r.Value.Utc.Kind);
        Assert.Equal(src, r.Value.Src);
    }

    [Fact]
    public void ClockModel_Zone_ConvertsThroughZoneWithDst()
    {
        var m = new ClockModel(ClockMode.Zone, NewYork, [], TimeSpan.FromHours(-4), StoredClockMode.Zone, NewYork);
        AssertUtc(m, L(2026, 9, 27, 14, 6), "America/Anchorage", Utc(2026, 9, 27, 18, 6), TimeSource.DroneClockZone);   // site zone ignored
        AssertUtc(m, L(2026, 1, 15, 12, 0), null, Utc(2026, 1, 15, 17, 0), TimeSource.DroneClockZone);
        Assert.Equal(TimeSpan.FromHours(-4), m.OffsetAt(L(2026, 9, 27, 14, 6), null));
        Assert.Equal(TimeSpan.FromHours(-5), m.OffsetAt(L(2026, 1, 15, 12, 0), null));
    }

    [Fact]
    public void ClockModel_SiteLocal_UsesSiteZoneElseStoredZone()
    {
        var m = new ClockModel(ClockMode.SiteLocal, null, [], TimeSpan.FromHours(-8), StoredClockMode.Zone, "America/Anchorage");
        AssertUtc(m, L(2026, 7, 26, 19, 55), "America/Nome", Utc(2026, 7, 27, 3, 55), TimeSource.DroneClockSiteLocal);
        AssertUtc(m, L(2026, 5, 10, 10, 41), NewYork, Utc(2026, 5, 10, 14, 41), TimeSource.DroneClockSiteLocal);
        // a library member with no ledger tz converts through the stored zone (Ref §6.1, §7.1)
        AssertUtc(m, L(2026, 9, 27, 10, 0), null, Utc(2026, 9, 27, 18, 0), TimeSource.DroneClockSiteLocal);
    }

    [Fact]
    public void ClockModel_NearestSample_UsesNearestByStampThenModal()
    {
        ImmutableArray<ClockSample> samples =
        [
            Sample(L(2026, 9, 27, 14, 0), Utc(2026, 9, 27, 18, 0), -4),
            Sample(L(2026, 9, 27, 11, 0), Utc(2026, 9, 27, 19, 0), -8),
        ];
        var m = new ClockModel(ClockMode.NearestSample, null, samples, TimeSpan.FromHours(-4), StoredClockMode.Zone, NewYork);
        AssertUtc(m, L(2026, 9, 27, 13, 50), null, Utc(2026, 9, 27, 17, 50), TimeSource.DroneClockSample);
        AssertUtc(m, L(2026, 9, 27, 11, 10), null, Utc(2026, 9, 27, 19, 10), TimeSource.DroneClockSample);
        // equidistant (12:30): tie → the sample with the earlier mvhd (−4 at 18:00Z)
        Assert.Equal(TimeSpan.FromHours(-4), m.OffsetAt(L(2026, 9, 27, 12, 30), null));
        // more than 60 days from every sample → the modal offset
        AssertUtc(m, L(2027, 3, 1, 12, 0), null, Utc(2027, 3, 1, 16, 0), TimeSource.DroneClockSample);
    }

    [Fact]
    public void ClockModel_Setting_FollowsStoredMode()
    {
        var siteLocal = new ClockModel(ClockMode.Setting, null, [], null, StoredClockMode.SiteLocal, NewYork);
        AssertUtc(siteLocal, L(2026, 9, 27, 11, 0), "America/Anchorage", Utc(2026, 9, 27, 19, 0), TimeSource.DroneClockSetting);
        AssertUtc(siteLocal, L(2026, 9, 27, 11, 0), null, Utc(2026, 9, 27, 15, 0), TimeSource.DroneClockSetting);
        var zone = new ClockModel(ClockMode.Setting, NewYork, [], null, StoredClockMode.Zone, NewYork);
        AssertUtc(zone, L(2026, 9, 27, 11, 0), "America/Anchorage", Utc(2026, 9, 27, 15, 0), TimeSource.DroneClockSetting);
    }

    [Fact]
    public void ClockModel_Zone_RepeatedDstHourUsesNearestSampleOffset()
    {
        ImmutableArray<ClockSample> samples =
        [
            Sample(L(2026, 11, 1, 1, 20), Utc(2026, 11, 1, 5, 20), -4),   // first pass through 01:00–02:00 (EDT)
            Sample(L(2026, 11, 1, 1, 30), Utc(2026, 11, 1, 6, 30), -5),   // second pass (EST)
        ];
        var m = new ClockModel(ClockMode.Zone, NewYork, samples, TimeSpan.FromHours(-4), StoredClockMode.Zone, NewYork);
        AssertUtc(m, L(2026, 11, 1, 1, 22), null, Utc(2026, 11, 1, 5, 22), TimeSource.DroneClockZone);
        AssertUtc(m, L(2026, 11, 1, 1, 28), null, Utc(2026, 11, 1, 6, 28), TimeSource.DroneClockZone);
        Assert.Equal(TimeSpan.FromHours(-4), m.OffsetAt(L(2026, 11, 1, 1, 22), null));

        var noSamples = m with { Samples = [] };   // no video to choose by → standard time
        AssertUtc(noSamples, L(2026, 11, 1, 1, 22), null, Utc(2026, 11, 1, 6, 22), TimeSource.DroneClockZone);
    }

    [Fact]
    public void ClockModel_Zone_SkippedSpringHourUsesStandardOffset()
    {
        var m = new ClockModel(ClockMode.Zone, NewYork, [], null, StoredClockMode.Zone, NewYork);
        AssertUtc(m, L(2026, 3, 8, 2, 30), null, Utc(2026, 3, 8, 7, 30), TimeSource.DroneClockZone);
    }

    [Fact]
    public void ClockModel_UnknownZone_GivesNull()
    {
        var m = new ClockModel(ClockMode.Zone, "Not/AZone", [], null, StoredClockMode.Zone, "Not/AZone");
        Assert.Null(m.ToUtc(L(2026, 9, 27, 12, 0), null));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*ClockModel_*"`
Expected: build FAILS with `error CS1061: 'ClockModel' does not contain a definition for 'ToUtc'` (and `OffsetAt`): Part 02 declares `ClockModel` as a `partial` record without these members.

- [ ] **Step 3: Write `ClockConversion`**

```csharp
// src/UasSort.Core/Time/ClockConversion.cs
namespace UasSort.Core.Time;

/// <summary>Drone-clock stamp → UTC through a learned <see cref="ClockModel"/> (Ref §6.1 Modes).</summary>
public static class ClockConversion
{
    public static readonly TimeSpan SampleWindow = TimeSpan.FromDays(60);

    public static (DateTime Utc, TimeSource Src)? ToUtc(ClockModel model, DateTime droneStamp, string? siteZoneId)
    {
        ArgumentNullException.ThrowIfNull(model);
        var stamp = DateTime.SpecifyKind(droneStamp, DateTimeKind.Unspecified);
        if (Offset(model, stamp, siteZoneId) is not { } offset) return null;
        return (DateTime.SpecifyKind(stamp - offset, DateTimeKind.Utc), SourceFor(model.Mode));
    }

    public static TimeSpan OffsetAt(ClockModel model, DateTime droneStamp, string? siteZoneId)
    {
        ArgumentNullException.ThrowIfNull(model);
        var stamp = DateTime.SpecifyKind(droneStamp, DateTimeKind.Unspecified);
        return Offset(model, stamp, siteZoneId) ?? model.Modal ?? TimeSpan.Zero;
    }

    public static TimeSource SourceFor(ClockMode mode) => mode switch
    {
        ClockMode.SiteLocal => TimeSource.DroneClockSiteLocal,
        ClockMode.Zone => TimeSource.DroneClockZone,
        ClockMode.NearestSample => TimeSource.DroneClockSample,
        ClockMode.Setting => TimeSource.DroneClockSetting,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown clock mode."),
    };

    public static ClockSample? Nearest(ImmutableArray<ClockSample> samples, DateTime droneStamp, TimeSpan[]? allowedOffsets)
    {
        ClockSample? best = null;
        var bestGap = TimeSpan.MaxValue;
        foreach (var s in samples)
        {
            if (allowedOffsets is not null && Array.IndexOf(allowedOffsets, s.Offset) < 0) continue;
            var gap = (s.DroneStamp - droneStamp).Duration();
            if (gap > SampleWindow) continue;
            if (best is null || gap < bestGap || (gap == bestGap && s.MvhdUtc < best.MvhdUtc))
            {
                best = s;
                bestGap = gap;
            }
        }
        return best;
    }

    static TimeSpan? Offset(ClockModel m, DateTime stamp, string? siteZoneId) => m.Mode switch
    {
        ClockMode.SiteLocal => ZoneOffset(siteZoneId ?? m.SettingZoneId, stamp, m.Samples),
        ClockMode.Zone => ZoneOffset(m.ZoneId ?? m.SettingZoneId, stamp, m.Samples),
        ClockMode.NearestSample => Nearest(m.Samples, stamp, null)?.Offset ?? m.Modal,
        ClockMode.Setting => ZoneOffset(m.SettingMode == StoredClockMode.SiteLocal ? siteZoneId ?? m.SettingZoneId : m.SettingZoneId,
                                        stamp, m.Samples),
        _ => null,
    };

    static TimeSpan? ZoneOffset(string zoneId, DateTime stamp, ImmutableArray<ClockSample> samples)
    {
        if (!Zones.TryFind(zoneId, out var zone)) return null;
        if (zone.IsAmbiguousTime(stamp))
        {
            // the repeated hour at DST end: the nearest video tells which pass this was (Review Focus 4)
            var valid = zone.GetAmbiguousTimeOffsets(stamp);
            return Nearest(samples, stamp, valid)?.Offset ?? zone.GetUtcOffset(stamp);
        }
        return zone.GetUtcOffset(stamp);   // for a skipped (invalid) time this is the standard offset
    }
}
```

- [ ] **Step 4: Write the `ClockModel` partial**

Create the second part of Part 02's `partial` record (Part 02's `Model/Clock.cs` stays as it is; `ClockConversion` resolves through the global `using UasSort.Core.Time;`):

```csharp
// src/UasSort.Core/Time/ClockModel.Conversion.cs
namespace UasSort.Core;

// Drone-clock conversions on the learned clock; the record's primary declaration (and its doc comment) is Part 02's Model/Clock.cs.
public sealed partial record ClockModel
{
    // siteZoneId: the zone that SiteLocal (or Setting with SettingMode SiteLocal) converts through, chosen by the caller (§6.1);
    // ignored by the other modes
    public (DateTime Utc, TimeSource Src)? ToUtc(DateTime droneStamp, string? siteZoneId)
        => ClockConversion.ToUtc(this, droneStamp, siteZoneId);

    public TimeSpan OffsetAt(DateTime droneStamp, string? siteZoneId)   // the drone clock's UTC offset at that stamp (§6.5)
        => ClockConversion.OffsetAt(this, droneStamp, siteZoneId);
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*ClockModel_*"`
Expected: test run summary Passed, 7 succeeded, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/UasSort.Core/Time/ClockConversion.cs src/UasSort.Core/Time/ClockModel.Conversion.cs tests/UasSort.Core.Tests/Time/ClockModelTests.cs
git commit -F - <<'EOF'
feat: implement ClockModel conversions incl. repeated DST hour (Part 04)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 04.6: DroneClock.Learn and the raw-item test builders

**Files:**
- Create: `src/UasSort.Core/Time/DroneClock.cs`
- Create: `tests/UasSort.Testing/FixturePoints.cs`
- Create: `tests/UasSort.Testing/RawItemBuilder.cs`
- Create: `tests/UasSort.Testing/FakeTimeZoneResolver.cs`
- Test: `tests/UasSort.Core.Tests/Time/DroneClockLearnTests.cs`

**Interfaces:**
```csharp
// Consumes: ClockConversion, Zones (04.4–04.5); GeoTimeZoneResolver (04.2); Part 02: RawItem, VideoUnit, PhotoUnit, CardEntry,
//           ItemId, EntryClass, Mp4Info, StillInfo, GpsFix, NoFix, GpsProbe, GpsSource, SessionKey, ITimeZoneResolver, TzLookup
// Produces (Ref §4.2 DroneClock; Summarize/ApplyLearned follow in 04.9):
namespace UasSort.Core.Time;
public static partial class DroneClock {
  public static readonly ImmutableArray<string> UsZones;            // candidate 3, in order (Ref §6.1); ZoneNames.IsUs is true for exactly these
  public static ClockModel Learn(IEnumerable<RawItem> items, StoredClockMode settingMode, string settingZoneId,
                                 ITimeZoneResolver tz, TimeZoneInfo pc);
  public static ImmutableArray<ClockSample> Samples(IEnumerable<RawItem> items, ITimeZoneResolver tz);   // sorted by MvhdUtc
  public static TimeSpan Round15(TimeSpan offset);
}
// Produces (UasSort.Testing, defined here; reused by Part 06's scenario builder, whose Sites fields are => FixturePoints.<same name>):
namespace UasSort.Testing;
public static class FixturePoints {
  public static readonly GeoPoint Anvil, Council, NomeA, NomeB, Zachar, KodiakTown, NewportAm, NewportPm, Makaha, Kathmandu, Kolkata; }
public static class RawItemBuilder {
  public static readonly TimeSpan Eastern;                          // −4 h: the fixture clock, mvhd = stamp + 4 h (Ref §13)
  public const string Serial = "1581F6Z8C23";                       // drone serial for SessionKey fixtures
  public static DateTime Stamp(string yyyyMMddHHmmss);              // naive drone stamp
  public static DateTime Utc(int y, int mo, int d, int h, int mi, int s = 0);
  public static RawItem Vid(string stamp, int n, GeoPoint? loc, TimeSpan? clockOffset = null, bool moov = true,
                            SessionKey? session = null, GpsSource gpsSource = GpsSource.DjmdModelTable, GeoPoint? last = null,
                            DateTime? mvhdUtc = null);            // DJI_<stamp>_<n:0000>_D.MP4; no moov → mdat-fallback GPS
  public static RawItem Dng(string stamp, int n, GeoPoint? loc, TimeSpan? offsetTime = null);   // EXIF DTO = stamp
  public static RawItem Other(string name, DateTime mtimeUtc, GeoPoint? loc);                   // no drone stamp → Mtime
}
public sealed class FakeTimeZoneResolver(ITimeZoneResolver fallback) : ITimeZoneResolver {
  public FakeTimeZoneResolver With(GeoPoint p, TzLookup lookup);
  public TzLookup Resolve(GeoPoint p);
}
```

Learning rules implemented (Ref §6.1): a sample per DJI video with `moov` and `mvhd` (`offset = round15min(stamp − mvhdUtc)`); `SiteZoneId` only from a `DjmdModelTable` fix resolving to a non-`Etc` zone. Candidates in order: SiteLocal (every sample with a site zone matches it at `mvhdUtc`; at least one such sample), the stored zone, the seven US zones, the PC zone; a zone fits when its offset at every stamp read as zone-local equals the sample's (in a repeated hour, either valid offset). No fit → NearestSample; no samples → Setting. `Modal` = most frequent offset (ties → the offset seen first by `mvhdUtc`).

- [ ] **Step 1: Check the test-project references**

Run: `grep -n "UasSort.Testing\|UasSort.Core.csproj\|TimeProvider.Testing" tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj tests/UasSort.Testing/UasSort.Testing.csproj`
Expected: Core.Tests references `UasSort.Testing` and `Microsoft.Extensions.TimeProvider.Testing`; Testing references `UasSort.Core` (Part 02 built `FakeFileSystem` on Core's `IoGuardPolicy`). Add any missing `ProjectReference`/`PackageReference` line (no version: central pins).

- [ ] **Step 2: Write the test builders**

```csharp
// tests/UasSort.Testing/FixturePoints.cs
namespace UasSort.Testing;

/// <summary>The spike fixture's sites (Ref §13, from docs/research/spikes/grouping/test_grouping.py) plus two for the 15-min case.</summary>
public static class FixturePoints
{
    public static readonly GeoPoint Anvil = new(64.5627, -165.3696);
    public static readonly GeoPoint Council = new(64.6935, -164.2657);
    public static readonly GeoPoint NomeA = new(64.6932, -165.7665);
    public static readonly GeoPoint NomeB = new(64.5925, -165.6731);
    public static readonly GeoPoint Zachar = new(57.5368, -153.7484);
    public static readonly GeoPoint KodiakTown = new(57.7996, -152.3902);
    public static readonly GeoPoint NewportAm = new(41.5155, -71.2967);
    public static readonly GeoPoint NewportPm = new(41.4762, -71.3237);
    public static readonly GeoPoint Makaha = new(21.47, -158.21);
    public static readonly GeoPoint Kathmandu = new(27.7172, 85.3240);
    public static readonly GeoPoint Kolkata = new(22.5726, 88.3639);
}
```

```csharp
// tests/UasSort.Testing/RawItemBuilder.cs
using System.Globalization;

namespace UasSort.Testing;

/// <summary>Builds harvested <see cref="RawItem"/>s directly (no card, no probes) for time, clock and planning tests (Ref §13 fixture).</summary>
public static class RawItemBuilder
{
    public static readonly TimeSpan Eastern = TimeSpan.FromHours(-4);
    public const string Serial = "1581F6Z8C23";

    public static DateTime Stamp(string yyyyMMddHHmmss)
        => DateTime.ParseExact(yyyyMMddHHmmss, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None);

    public static DateTime Utc(int y, int mo, int d, int h, int mi, int s = 0) => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    public static RawItem Vid(string stamp, int n, GeoPoint? loc, TimeSpan? clockOffset = null, bool moov = true,
                              SessionKey? session = null, GpsSource gpsSource = GpsSource.DjmdModelTable, GeoPoint? last = null,
                              DateTime? mvhdUtc = null)
    {
        var s = Stamp(stamp);
        var start = mvhdUtc ?? DateTime.SpecifyKind(s - (clockOffset ?? Eastern), DateTimeKind.Utc);
        var name = string.Create(CultureInfo.InvariantCulture, $"DJI_{stamp}_{n:0000}_D.MP4");
        var rel = "DCIM/DJI_001/" + name;
        var mtime = start.AddSeconds(90);
        var entry = new CardEntry(rel, 100_000_000L + n, mtime, start, mtime, 0x20, EntryClass.Video, null);
        var source = moov ? gpsSource : GpsSource.MdatHeadFallback;
        var path = source == GpsSource.DjmdGenericSearch ? "3-9-1" : null;
        GpsProbe first = loc is { } p
            ? new GpsFix(p, 100, 0, source, path)
            : new NoFix(moov ? NoFixReason.AllProbedSamplesZero : NoFixReason.Unparseable);
        GpsFix? lastFix = last is { } lp ? new GpsFix(lp, 100, 299, source, path) : null;
        var mp4 = new Mp4Info(moov ? start : null, moov, first, lastFix, "dvtm_Air3s.proto",
                              session?.SessionUtc, session?.DroneSerial, null, moov ? TimeSpan.FromSeconds(90) : null);
        return new RawItem(new VideoUnit(new ItemId(rel), entry, false), ItemKind.Video, name, entry.Size, mtime, s, mp4, null, null);
    }

    public static RawItem Dng(string stamp, int n, GeoPoint? loc, TimeSpan? offsetTime = null)
    {
        var s = Stamp(stamp);
        var name = string.Create(CultureInfo.InvariantCulture, $"DJI_{stamp}_{n:0000}_D.DNG");
        var rel = "DCIM/DJI_001/" + name;
        var mtime = DateTime.SpecifyKind(s - Eastern, DateTimeKind.Utc);
        var entry = new CardEntry(rel, 25_000_000L + n, mtime, mtime, mtime, 0x20, EntryClass.Photo, null);
        GpsProbe gps = loc is { } p ? new GpsFix(p, 100, 0, GpsSource.Exif, null) : new NoFix(NoFixReason.NoGpsTag);
        var still = new StillInfo(s, offsetTime, gps, "FC9113", null);
        return new RawItem(new PhotoUnit(new ItemId(rel), entry, null), ItemKind.Photo, name, entry.Size, mtime, s, null, still, null);
    }

    public static RawItem Other(string name, DateTime mtimeUtc, GeoPoint? loc)
    {
        var rel = "DCIM/DJI_001/" + name;
        var mtime = DateTime.SpecifyKind(mtimeUtc, DateTimeKind.Utc);
        var entry = new CardEntry(rel, 3_000_000L, mtime, mtime, mtime, 0x20, EntryClass.Photo, null);
        GpsProbe gps = loc is { } p ? new GpsFix(p, null, 0, GpsSource.Exif, null) : new NoFix(NoFixReason.NoGpsTag);
        var still = new StillInfo(null, null, gps, null, null);
        return new RawItem(new PhotoUnit(new ItemId(rel), entry, null), ItemKind.Photo, name, entry.Size, mtime, null, null, still, null);
    }
}
```

```csharp
// tests/UasSort.Testing/FakeTimeZoneResolver.cs
namespace UasSort.Testing;

/// <summary>Answers chosen points from a table (e.g. an <c>Etc/*</c> ocean point, a border point with alternatives), others from a real resolver.</summary>
public sealed class FakeTimeZoneResolver(ITimeZoneResolver fallback) : ITimeZoneResolver
{
    readonly Dictionary<GeoPoint, TzLookup> _table = [];

    public FakeTimeZoneResolver With(GeoPoint p, TzLookup lookup)
    {
        _table[p] = lookup;
        return this;
    }

    public TzLookup Resolve(GeoPoint p) => _table.TryGetValue(p, out var lookup) ? lookup : fallback.Resolve(p);
}
```

- [ ] **Step 3: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Time/DroneClockLearnTests.cs
using static UasSort.Testing.RawItemBuilder;

namespace UasSort.Core.Tests.Time;

public sealed class DroneClockLearnTests
{
    const string NewYork = "America/New_York";
    static readonly ITimeZoneResolver Tz = new GeoTimeZoneResolver();
    static readonly TimeZoneInfo AlaskaPc = Zones.Find("America/Anchorage");
    static readonly GeoPoint Ocean = new(56.0, -148.0);

    static ClockModel Learn(IEnumerable<RawItem> items, StoredClockMode mode = StoredClockMode.Zone, string zone = NewYork,
                            TimeZoneInfo? pc = null, ITimeZoneResolver? tz = null)
        => DroneClock.Learn(items, mode, zone, tz ?? Tz, pc ?? AlaskaPc);

    static List<RawItem> CardZ() =>
    [
        Vid("20260927140127", 123, FixturePoints.Zachar),
        Vid("20260927140144", 124, FixturePoints.Zachar),
        Vid("20260927142416", 148, FixturePoints.Zachar),
    ];

    [Fact]
    public void Learn_SummerMinus4AtAlaskaSites_FitsNewYork()
    {
        var c = Learn(CardZ());
        Assert.Equal(ClockMode.Zone, c.Mode);
        Assert.Equal(NewYork, c.ZoneId);
        Assert.Equal(3, c.Samples.Length);
        Assert.All(c.Samples, s =>
        {
            Assert.Equal(TimeSpan.FromHours(-4), s.Offset);
            Assert.Equal("America/Anchorage", s.SiteZoneId);
        });
        Assert.Equal(TimeSpan.FromHours(-4), c.Modal);
        Assert.Equal(StoredClockMode.Zone, c.SettingMode);
        Assert.Equal(NewYork, c.SettingZoneId);
    }

    [Fact]
    public void Learn_SameSamplesAtEasternSites_SiteLocalWinsByOrder()
    {
        var c = Learn([Vid("20260510104103", 2, FixturePoints.NewportAm), Vid("20260510193745", 30, FixturePoints.NewportPm)]);
        Assert.Equal(ClockMode.SiteLocal, c.Mode);
        Assert.Null(c.ZoneId);
    }

    [Fact]
    public void Learn_WinterMinus5InAlaska_FitsNewYork()
    {
        var minus5 = TimeSpan.FromHours(-5);
        var c = Learn([Vid("20260115120000", 1, FixturePoints.Zachar, minus5), Vid("20260116090000", 2, FixturePoints.Zachar, minus5)]);
        Assert.Equal(ClockMode.Zone, c.Mode);
        Assert.Equal(NewYork, c.ZoneId);
    }

    [Fact]
    public void Learn_FixedMinus4InWinter_NothingFits_NearestSample()
    {
        var c = Learn([Vid("20260115120000", 1, FixturePoints.Zachar), Vid("20260116090000", 2, FixturePoints.Zachar)]);
        Assert.Equal(ClockMode.NearestSample, c.Mode);
        Assert.Null(c.ZoneId);
        Assert.Equal(TimeSpan.FromHours(-4), c.Modal);
    }

    [Fact]
    public void Learn_PhotoOnlyWinterCard_UsesStoredZone()
    {
        var c = Learn([Dng("20260115120000", 1, FixturePoints.Zachar)]);
        Assert.Equal(ClockMode.Setting, c.Mode);
        Assert.Equal(NewYork, c.ZoneId);
        Assert.True(c.Samples.IsEmpty);
        Assert.Null(c.Modal);
        var r = c.ToUtc(Stamp("20260115120000"), null);
        Assert.NotNull(r);
        Assert.Equal(Utc(2026, 1, 15, 17, 0), r.Value.Utc);   // −5 in winter
        Assert.Equal(TimeSource.DroneClockSetting, r.Value.Src);
    }

    [Fact]
    public void Learn_PhotoOnlyCard_StoredSiteLocal_HasNoZone()
    {
        var c = Learn([Dng("20260927110000", 1, FixturePoints.Zachar)], StoredClockMode.SiteLocal);
        Assert.Equal(ClockMode.Setting, c.Mode);
        Assert.Null(c.ZoneId);
        Assert.Equal(StoredClockMode.SiteLocal, c.SettingMode);
    }

    [Fact]
    public void Learn_StoredZoneIsTriedBeforeTheUsList()
    {
        var c = Learn(CardZ(), zone: "America/Puerto_Rico");   // fixed −4 fits the summer samples too
        Assert.Equal(ClockMode.Zone, c.Mode);
        Assert.Equal("America/Puerto_Rico", c.ZoneId);
    }

    [Fact]
    public void Learn_UsListIsTriedInOrder()
    {
        var c = Learn([Vid("20260115120000", 1, FixturePoints.NewportAm, TimeSpan.FromHours(-9))], pc: Zones.Find("Pacific/Honolulu"));
        Assert.Equal(ClockMode.Zone, c.Mode);
        Assert.Equal("America/Anchorage", c.ZoneId);
    }

    [Fact]
    public void Learn_PcZoneIsTheLastCandidate()
    {
        var c = Learn([Vid("20260715120000", 1, FixturePoints.NewportAm, TimeSpan.FromHours(-3))], pc: Zones.Find("America/Sao_Paulo"));
        Assert.Equal(ClockMode.Zone, c.Mode);
        Assert.Equal("America/Sao_Paulo", c.ZoneId);
    }

    [Fact]
    public void Learn_OffsetsRoundToQuarterHours()
    {
        var c = Learn([Vid("20260927140627", 128, FixturePoints.Zachar, mvhdUtc: Utc(2026, 9, 27, 18, 7, 7))]);   // 40 s drift
        Assert.Equal(TimeSpan.FromHours(-4), Assert.Single(c.Samples).Offset);
        Assert.Equal(TimeSpan.FromHours(-4), DroneClock.Round15(new TimeSpan(-4, -7, -29)));
        Assert.Equal(new TimeSpan(-4, -15, 0), DroneClock.Round15(new TimeSpan(-4, -7, -30)));
        Assert.Equal(new TimeSpan(5, 30, 0), DroneClock.Round15(new TimeSpan(5, 29, 58)));
    }

    [Fact]
    public void Learn_OnlyModelTableLandFixesGiveSiteZones()
    {
        var tz = new FakeTimeZoneResolver(Tz).With(Ocean, new TzLookup("Etc/GMT+10", [], true));
        var c = Learn(
        [
            Vid("20260927140127", 1, FixturePoints.Zachar, gpsSource: GpsSource.DjmdGenericSearch, last: FixturePoints.Zachar),
            Vid("20260927150000", 2, Ocean),
            Vid("20260927160000", 3, null),
        ], tz: tz);
        Assert.All(c.Samples, s => Assert.Null(s.SiteZoneId));
        Assert.Equal(ClockMode.Zone, c.Mode);   // SiteLocal needs at least one sample with a site zone
        Assert.Equal(NewYork, c.ZoneId);
    }

    [Fact]
    public void Learn_TruncatedClipsAndPhotosGiveNoSamples()
    {
        var c = Learn([Vid("20260727002013", 14, FixturePoints.Anvil, moov: false), Dng("20260727003000", 2, FixturePoints.Anvil)]);
        Assert.Equal(ClockMode.Setting, c.Mode);
        Assert.True(c.Samples.IsEmpty);
    }

    [Fact]
    public void Learn_SamplesAreSortedByMvhd()
    {
        var c = Learn([Vid("20260927142416", 148, FixturePoints.Zachar), Vid("20260927140127", 123, FixturePoints.Zachar)]);
        DateTime[] expected = [Utc(2026, 9, 27, 18, 1, 27), Utc(2026, 9, 27, 18, 24, 16)];
        Assert.Equal(expected, c.Samples.Select(s => s.MvhdUtc).ToArray());
    }

    [Fact]
    public void Learn_UsZones_AreExactlyTheIsUsZones()
    {
        Assert.Equal(7, DroneClock.UsZones.Length);
        Assert.All(DroneClock.UsZones, id => Assert.True(ZoneNames.IsUs(id)));
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Learn_*"`
Expected: build FAILS with `error CS0103: The name 'DroneClock' does not exist in the current context`.

- [ ] **Step 5: Write the implementation**

```csharp
// src/UasSort.Core/Time/DroneClock.cs
namespace UasSort.Core.Time;

/// <summary>Learns the drone clock: SiteLocal first, then a zone, else NearestSample; Setting when no video has a moov (Ref §6.1).</summary>
public static partial class DroneClock
{
    public static readonly ImmutableArray<string> UsZones =
    [
        "America/New_York", "America/Chicago", "America/Denver", "America/Phoenix", "America/Los_Angeles",
        "America/Anchorage", "Pacific/Honolulu",
    ];

    static readonly TimeSpan Quarter = TimeSpan.FromMinutes(15);

    public static ClockModel Learn(IEnumerable<RawItem> items, StoredClockMode settingMode, string settingZoneId,
                                   ITimeZoneResolver tz, TimeZoneInfo pc)
    {
        ArgumentNullException.ThrowIfNull(settingZoneId);
        ArgumentNullException.ThrowIfNull(pc);
        var samples = Samples(items, tz);
        if (samples.IsEmpty)
            return new ClockModel(ClockMode.Setting, settingMode == StoredClockMode.Zone ? settingZoneId : null,
                                  samples, null, settingMode, settingZoneId);

        var modal = Modal(samples);
        if (SiteLocalFits(samples))
            return new ClockModel(ClockMode.SiteLocal, null, samples, modal, settingMode, settingZoneId);

        foreach (var id in Candidates(settingZoneId, pc))
            if (Zones.TryFind(id, out var zone) && samples.All(s => ZoneFits(zone, s)))
                return new ClockModel(ClockMode.Zone, id, samples, modal, settingMode, settingZoneId);

        return new ClockModel(ClockMode.NearestSample, null, samples, modal, settingMode, settingZoneId);
    }

    public static ImmutableArray<ClockSample> Samples(IEnumerable<RawItem> items, ITimeZoneResolver tz)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(tz);
        var list = new List<ClockSample>();
        foreach (var r in items)
        {
            if (r.Kind != ItemKind.Video || r.DroneStamp is not { } stamp) continue;
            if (r.Mp4 is not { HasMoov: true, MvhdUtc: { } mvhd } mp4) continue;
            var utc = DateTime.SpecifyKind(mvhd, DateTimeKind.Utc);
            var local = DateTime.SpecifyKind(stamp, DateTimeKind.Unspecified);
            list.Add(new ClockSample(local, utc, Round15(local - utc), SiteZone(mp4, tz)));
        }
        return [.. list.OrderBy(s => s.MvhdUtc).ThenBy(s => s.DroneStamp)];
    }

    public static TimeSpan Round15(TimeSpan offset)
        => TimeSpan.FromTicks((long)Math.Round(offset.Ticks / (double)Quarter.Ticks, MidpointRounding.AwayFromZero) * Quarter.Ticks);

    static string? SiteZone(Mp4Info mp4, ITimeZoneResolver tz)
    {
        if (mp4.First is not GpsFix fix || fix.Source != GpsSource.DjmdModelTable) return null;
        var lookup = tz.Resolve(fix.Point);
        return !lookup.IsEtc && Zones.TryFind(lookup.IanaId, out _) ? lookup.IanaId : null;
    }

    static bool SiteLocalFits(ImmutableArray<ClockSample> samples)
    {
        var any = false;
        foreach (var s in samples)
        {
            if (s.SiteZoneId is null) continue;
            if (!Zones.TryFind(s.SiteZoneId, out var zone) || zone.GetUtcOffset(s.MvhdUtc) != s.Offset) return false;
            any = true;
        }
        return any;
    }

    static bool ZoneFits(TimeZoneInfo zone, ClockSample s)
    {
        var local = DateTime.SpecifyKind(s.DroneStamp, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) return false;
        if (zone.IsAmbiguousTime(local)) return Array.IndexOf(zone.GetAmbiguousTimeOffsets(local), s.Offset) >= 0;
        return zone.GetUtcOffset(local) == s.Offset;
    }

    static IEnumerable<string> Candidates(string settingZoneId, TimeZoneInfo pc)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<string> all = [settingZoneId, .. UsZones, Zones.IanaId(pc)];
        foreach (var id in all)
            if (seen.Add(id)) yield return id;
    }

    static TimeSpan Modal(ImmutableArray<ClockSample> samples)
        => samples.GroupBy(s => s.Offset)
                  .OrderByDescending(g => g.Count())
                  .ThenBy(g => g.Min(s => s.MvhdUtc))
                  .First().Key;
}
```

(`partial` because Task 04.9 adds `Summarize` and `ApplyLearned` in `DroneClock.Summary.cs`.)

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Learn_*"`
Expected: test run summary Passed, 14 succeeded, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add src/UasSort.Core/Time/DroneClock.cs tests/UasSort.Testing/FixturePoints.cs tests/UasSort.Testing/RawItemBuilder.cs tests/UasSort.Testing/FakeTimeZoneResolver.cs tests/UasSort.Core.Tests/Time/DroneClockLearnTests.cs tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj tests/UasSort.Testing/UasSort.Testing.csproj
git commit -F - <<'EOF'
feat: add drone clock learner with SiteLocal and zone candidates (Part 04)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 04.7: TimeResolver — capture time, site zone, local date, GPS gate, sessions

**Files:**
- Create: `src/UasSort.Core/Time/TimeResolver.cs`
- Create: `tests/UasSort.Testing/FakePlaceIndex.cs`
- Test: `tests/UasSort.Core.Tests/Time/TimeResolverTests.cs`

**Interfaces:**
```csharp
// Consumes: DroneClock (04.6), ClockModel.ToUtc (04.5), Zones (04.4), GpsPlausibility (04.3), GeoMath (04.1);
//           Part 02: RawItem, ResolvedItem(RawItem Raw, ItemTime Time, ItemFlags Flags, GpsFix? Gps, SessionKey? Session),
//           ItemTime(DateTime CaptureUtc, TimeSource Source, string TzId, TzSource TzSource, DateOnly LocalDate, DateTime LocalTime),
//           ITimeZoneResolver, IPlaceIndex { IReadOnlyList<PlaceHit> Near(GeoPoint p, Distance r, PlaceClass cls, int max); }
// Produces (Ref §4.2 TimeResolver):
namespace UasSort.Core.Time;
public static class TimeResolver {
  public static readonly TimeSpan NearbyWindow;        // 12 h
  public static readonly Distance GeoNamesTzRadius;    // 60 mi
  public static ImmutableArray<ResolvedItem> Resolve(IReadOnlyList<RawItem> raw, ClockModel clock, ITimeZoneResolver tz,
                                                     IPlaceIndex? places, TimeZoneInfo pc, DateTime nowUtc);   // output order = input order
}
// Produces (UasSort.Testing, defined here; the one FakePlaceIndex, also used by Parts 06 and 08):
namespace UasSort.Testing;
public sealed class FakePlaceIndex(params IReadOnlyList<PlaceHit> places) : IPlaceIndex;   // Away recomputed with GeoMath;
                                                                                            // takes a list/collection expression or hits as arguments
```

Rules implemented here (flags from Ref §6.5 that depend on time windows or the clock come in Task 04.8):
- **GPS.** Model-table, EXIF and mdat-fallback fixes are trusted. A `DjmdGenericSearch` fix goes through `GpsPlausibility.Check(first, Mp4.LastSameField, tz.Resolve(first), trusted points)`; accepted → `GpsGuessed`, rejected → no GPS. No accepted fix → `NoGps`.
- **Capture time (§6.2).** Video with `moov` and `mvhd` → `Mvhd`; EXIF DTO with `OffsetTimeOriginal` → `ExifWithOffset`; a drone stamp → `ClockModel.ToUtc(stamp, clockZone)` where `clockZone` = the item's own land zone, else the land zone of the nearest GPS item within 12 h by drone stamp, else null (the model then uses the stored zone); else card mtime → `Mtime`.
- **Zone (§6.4).** Own GPS with a land zone → `Gps`. Own GPS with an `Etc/*` zone → nearest land-zone GPS item on the whole card within 12 h (`NearestLandGpsOnCard`), else the nearest GeoNames place of either class within 60 mi (`GeoNamesTz`), else the PC zone (`PcZone`); all set `TzFallback`. No GPS → a land-zone GPS item in the same session (`SameSession`, nearest in time), else the nearest land-zone GPS item within 12 h (`NearestGpsWithin12h`), else the PC zone (`PcZone` + `TzFallback`). Local date = `ConvertTimeFromUtc(CaptureUtc, zone).Date`.
- **Other flags here.** `Truncated` (video without `moov`, or `VideoUnit.HasTrinf`), `ProbeFailed` (`ProbeError` set).

- [ ] **Step 1: Write `FakePlaceIndex`**

```csharp
// tests/UasSort.Testing/FakePlaceIndex.cs
namespace UasSort.Testing;

/// <summary>An in-memory <see cref="IPlaceIndex"/> over given hits; <c>Away</c> is recomputed for each query.
/// <c>params</c> collection: <c>new FakePlaceIndex([hit1, hit2])</c>, <c>new FakePlaceIndex(list)</c> and <c>new FakePlaceIndex(hit1, hit2)</c> all work.</summary>
public sealed class FakePlaceIndex(params IReadOnlyList<PlaceHit> places) : IPlaceIndex
{
    public IReadOnlyList<PlaceHit> Near(GeoPoint p, Distance r, PlaceClass cls, int max)
        => places.Where(h => h.Class == cls)
                 .Select(h => h with { Away = GeoMath.Haversine(p, h.Point) })
                 .Where(h => h.Away.Meters <= r.Meters)
                 .OrderBy(h => h.Away.Meters)
                 .Take(max)
                 .ToList();
}
```

- [ ] **Step 2: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Time/TimeResolverTests.cs
using static UasSort.Testing.RawItemBuilder;

namespace UasSort.Core.Tests.Time;

public sealed class TimeResolverTests
{
    const string NewYork = "America/New_York";
    static readonly ITimeZoneResolver Tz = new GeoTimeZoneResolver();
    static readonly TimeZoneInfo AlaskaPc = Zones.Find("America/Anchorage");
    static readonly DateTime Now = new FakeTimeProvider(new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero)).GetUtcNow().UtcDateTime;
    static readonly GeoPoint Ocean = new(56.0, -148.0);
    static readonly FakeTimeZoneResolver OceanTz = new FakeTimeZoneResolver(Tz).With(Ocean, new TzLookup("Etc/GMT+10", [], true));

    static ImmutableArray<ResolvedItem> Resolve(IReadOnlyList<RawItem> raw, ITimeZoneResolver? tz = null, IPlaceIndex? places = null,
                                                StoredClockMode mode = StoredClockMode.Zone, string zone = NewYork)
    {
        var resolver = tz ?? Tz;
        var clock = DroneClock.Learn(raw, mode, zone, resolver, AlaskaPc);
        return TimeResolver.Resolve(raw, clock, resolver, places, AlaskaPc, Now);
    }

    [Fact]
    public void Resolve_Midnight_LocalDateFromSiteZone()
    {
        var r = Resolve([Vid("20260726035000", 1, FixturePoints.Anvil), Vid("20260726041000", 2, FixturePoints.Anvil)]);
        var t = r[0].Time;
        Assert.Equal(Utc(2026, 7, 26, 7, 50), t.CaptureUtc);
        Assert.Equal(TimeSource.Mvhd, t.Source);
        Assert.Equal("America/Nome", t.TzId);
        Assert.Equal(TzSource.Gps, t.TzSource);
        Assert.Equal(new DateOnly(2026, 7, 25), t.LocalDate);                 // never the drone clock's Jul 26
        Assert.Equal(new DateTime(2026, 7, 25, 23, 50, 0), t.LocalTime);
        Assert.False(r[0].Flags.HasFlag(ItemFlags.TzFallback));
        Assert.False(r[0].Flags.HasFlag(ItemFlags.NoGps));
    }

    [Fact]
    public void Resolve_Hawaii_SiteZoneNotPcZone()
    {
        var t = Resolve([Vid("20260301053000", 1, FixturePoints.Makaha)])[0].Time;
        Assert.Equal("Pacific/Honolulu", t.TzId);
        Assert.Equal(new DateOnly(2026, 2, 28), t.LocalDate);
    }

    [Fact]
    public void Resolve_Dng_UsesClockZoneAndSiteZone()
    {
        var t = Resolve([Vid("20260726035000", 1, FixturePoints.Anvil), Dng("20260726035500", 2, FixturePoints.Anvil)])[1].Time;
        Assert.Equal(TimeSource.DroneClockZone, t.Source);
        Assert.Equal(Utc(2026, 7, 26, 7, 55), t.CaptureUtc);
        Assert.Equal(new DateOnly(2026, 7, 25), t.LocalDate);
    }

    [Fact]
    public void Resolve_ZoneClock_HandlesDstChangeOnTheCard()
    {
        var r = Resolve(
        [
            Vid("20261020120000", 1, FixturePoints.Anvil),
            Vid("20261110120000", 2, FixturePoints.Anvil, TimeSpan.FromHours(-5)),
            Dng("20261110121000", 3, FixturePoints.Anvil),
        ]);
        Assert.Equal(TimeSource.DroneClockZone, r[2].Time.Source);
        Assert.Equal(Utc(2026, 11, 10, 17, 10), r[2].Time.CaptureUtc);
    }

    [Fact]
    public void Resolve_TruncatedClip_TimedByClockWithFallbackGps()
    {
        var r = Resolve(
        [
            Vid("20260726235645", 1, FixturePoints.Anvil),
            Vid("20260727002013", 14, FixturePoints.Anvil, moov: false),
            Vid("20260727002118", 15, FixturePoints.Anvil),
        ]);
        var t = r[1];
        Assert.Equal(TimeSource.DroneClockZone, t.Time.Source);
        Assert.Equal(Utc(2026, 7, 27, 4, 20, 13), t.Time.CaptureUtc);
        Assert.True(t.Flags.HasFlag(ItemFlags.Truncated));
        Assert.False(t.Flags.HasFlag(ItemFlags.NoGps));
        Assert.Equal(GpsSource.MdatHeadFallback, t.Gps?.Source);
    }

    [Fact]
    public void Resolve_ExifOffset_BeatsDroneClock()
    {
        var t = Resolve([Dng("20260927120000", 1, FixturePoints.Zachar, TimeSpan.FromHours(-8))])[0].Time;
        Assert.Equal(TimeSource.ExifWithOffset, t.Source);
        Assert.Equal(Utc(2026, 9, 27, 20, 0), t.CaptureUtc);
    }

    [Fact]
    public void Resolve_NoDroneStamp_UsesMtime()
    {
        var t = Resolve([Other("IMG_0001.JPG", Utc(2026, 9, 27, 19, 0), FixturePoints.Zachar)])[0].Time;
        Assert.Equal(TimeSource.Mtime, t.Source);
        Assert.Equal(Utc(2026, 9, 27, 19, 0), t.CaptureUtc);
    }

    [Fact]
    public void Resolve_ProbeFailedClip_TimedFromFilename()
    {
        var broken = Vid("20260927141000", 125, FixturePoints.Zachar) with { Mp4 = null, ProbeError = "moov unreadable" };
        var r = Resolve([Vid("20260927140127", 123, FixturePoints.Zachar), broken]);
        Assert.Equal(TimeSource.DroneClockZone, r[1].Time.Source);
        Assert.Equal(Utc(2026, 9, 27, 18, 10), r[1].Time.CaptureUtc);
        Assert.True(r[1].Flags.HasFlag(ItemFlags.ProbeFailed));
        Assert.True(r[1].Flags.HasFlag(ItemFlags.NoGps));
        Assert.False(r[1].Flags.HasFlag(ItemFlags.Truncated));
    }

    [Fact]
    public void Resolve_EtcZone_UsesNearestLandItemOnCard()
    {
        var r = Resolve([Vid("20260927120000", 1, Ocean), Vid("20260927150000", 2, FixturePoints.Zachar)], OceanTz);
        Assert.Equal("America/Anchorage", r[0].Time.TzId);
        Assert.Equal(TzSource.NearestLandGpsOnCard, r[0].Time.TzSource);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.TzFallback));
        Assert.Equal(TzSource.Gps, r[1].Time.TzSource);
        Assert.False(r[1].Flags.HasFlag(ItemFlags.TzFallback));
    }

    [Fact]
    public void Resolve_EtcZone_NoLandWithin12h_UsesNearestGeoNamesPlace()
    {
        var places = new FakePlaceIndex(
        [
            new PlaceHit("Test Island", new GeoPoint(56.3, -148.2), PlaceClass.Populated, "P", 50, "America/Juneau", new Distance(0)),
        ]);
        var r = Resolve([Vid("20260927120000", 1, Ocean), Vid("20260928010000", 2, FixturePoints.Zachar)], OceanTz, places);   // 13 h apart
        Assert.Equal("America/Juneau", r[0].Time.TzId);
        Assert.Equal(TzSource.GeoNamesTz, r[0].Time.TzSource);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.TzFallback));
    }

    [Fact]
    public void Resolve_EtcZone_NothingNearby_UsesPcZone()
    {
        var places = new FakePlaceIndex(
        [
            new PlaceHit("Kodiak", FixturePoints.KodiakTown, PlaceClass.Populated, "P", 5581, "America/Juneau", new Distance(0)),   // ~200 mi
        ]);
        var r = Resolve([Vid("20260927120000", 1, Ocean), Vid("20260928010000", 2, FixturePoints.Zachar)], OceanTz, places);
        Assert.Equal("America/Anchorage", r[0].Time.TzId);
        Assert.Equal(TzSource.PcZone, r[0].Time.TzSource);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.TzFallback));
    }

    [Fact]
    public void Resolve_NoGps_SameSessionBeatsNearerItem_ThenNearestWithin12h()
    {
        var sessionA = new SessionKey(Serial, Utc(2026, 9, 27, 17, 55, 0));
        var sessionX = new SessionKey(Serial, Utc(2026, 9, 27, 17, 55, 1));   // same power-on (|Δ| ≤ 2 s)
        var r = Resolve(
        [
            Vid("20260927140000", 1, FixturePoints.Zachar, session: sessionA),   // 18:00Z, Alaska
            Vid("20260927200000", 2, FixturePoints.NewportAm),                  // 00:00Z Sep 28, Eastern
            Vid("20260927190000", 3, null, session: sessionX),                  // 23:00Z, no GPS
            Dng("20260927195000", 4, null),                                     // 23:50Z, no GPS, no session
        ]);
        Assert.Equal("America/Anchorage", r[2].Time.TzId);
        Assert.Equal(TzSource.SameSession, r[2].Time.TzSource);
        Assert.Equal(sessionX, r[2].Session);
        Assert.False(r[2].Flags.HasFlag(ItemFlags.TzFallback));
        Assert.True(r[2].Flags.HasFlag(ItemFlags.NoGps));
        Assert.Equal(NewYork, r[3].Time.TzId);
        Assert.Equal(TzSource.NearestGpsWithin12h, r[3].Time.TzSource);
        Assert.False(r[3].Flags.HasFlag(ItemFlags.TzFallback));
    }

    [Fact]
    public void Resolve_NoGps_NothingWithin12h_UsesPcZone()
    {
        var r = Resolve([Vid("20260927140000", 1, FixturePoints.Zachar), Dng("20260928090000", 2, null)]);   // 19 h apart
        Assert.Equal("America/Anchorage", r[1].Time.TzId);
        Assert.Equal(TzSource.PcZone, r[1].Time.TzSource);
        Assert.True(r[1].Flags.HasFlag(ItemFlags.TzFallback));
    }

    [Fact]
    public void Resolve_GenericHit_PlausibleIsGuessed()
    {
        var r = Resolve(
        [
            Vid("20260927140127", 123, FixturePoints.Zachar),
            Vid("20260927141000", 125, new GeoPoint(57.54, -153.74), gpsSource: GpsSource.DjmdGenericSearch,
                last: new GeoPoint(57.545, -153.742)),
        ]);
        Assert.NotNull(r[1].Gps);
        Assert.True(r[1].Flags.HasFlag(ItemFlags.GpsGuessed));
        Assert.False(r[1].Flags.HasFlag(ItemFlags.NoGps));
        Assert.Equal(TzSource.Gps, r[1].Time.TzSource);
    }

    [Fact]
    public void Resolve_GenericHit_ImplausibleIsNoGps()
    {
        var r = Resolve(
        [
            Vid("20260927140127", 123, FixturePoints.Zachar),
            Vid("20260927141000", 125, new GeoPoint(57.54, -153.74), gpsSource: GpsSource.DjmdGenericSearch,
                last: FixturePoints.KodiakTown),   // first↔last ≈ 53 mi
        ]);
        Assert.Null(r[1].Gps);
        Assert.True(r[1].Flags.HasFlag(ItemFlags.NoGps));
        Assert.False(r[1].Flags.HasFlag(ItemFlags.GpsGuessed));
    }

    [Fact]
    public void Resolve_PreservesInputOrder()
    {
        List<RawItem> raw = [Dng("20260927150000", 9, FixturePoints.Zachar), Vid("20260927140127", 123, FixturePoints.Zachar)];
        var r = Resolve(raw);
        Assert.Equal(raw, r.Select(x => x.Raw).ToList());
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Resolve_*"`
Expected: build FAILS with `error CS0103: The name 'TimeResolver' does not exist in the current context`.

- [ ] **Step 4: Write the implementation**

```csharp
// src/UasSort.Core/Time/TimeResolver.cs
namespace UasSort.Core.Time;

/// <summary>Capture time, site zone, local date, session and flags per item (Ref §6.2–6.5). Independent of R and G.</summary>
public static class TimeResolver
{
    public static readonly TimeSpan NearbyWindow = TimeSpan.FromHours(12);
    public static readonly Distance GeoNamesTzRadius = Distance.FromMiles(60);
    static readonly PlaceClass[] PlaceClasses = [PlaceClass.Populated, PlaceClass.Feature];

    public static ImmutableArray<ResolvedItem> Resolve(IReadOnlyList<RawItem> raw, ClockModel clock, ITimeZoneResolver tz,
                                                       IPlaceIndex? places, TimeZoneInfo pc, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(tz);
        ArgumentNullException.ThrowIfNull(pc);
        var n = raw.Count;
        var lookups = new Dictionary<GeoPoint, TzLookup>();
        TzLookup Lookup(GeoPoint p)
        {
            if (!lookups.TryGetValue(p, out var l)) { l = tz.Resolve(p); lookups[p] = l; }
            return l;
        }

        // 1. GPS: trusted fixes, then generic hits through the plausibility gate (§6.3 step 5b)
        var gps = new GpsFix?[n];
        var guessed = new bool[n];
        var trusted = new List<GeoPoint>();
        for (var i = 0; i < n; i++)
            if (FirstFix(raw[i]) is { } f && f.Source != GpsSource.DjmdGenericSearch)
            {
                gps[i] = f;
                trusted.Add(f.Point);
            }
        for (var i = 0; i < n; i++)
            if (FirstFix(raw[i]) is { Source: GpsSource.DjmdGenericSearch } g
                && GpsPlausibility.Check(g, raw[i].Mp4?.LastSameField, Lookup(g.Point), trusted) is GpsFix accepted)
            {
                gps[i] = accepted;
                guessed[i] = true;
            }

        // 2. each GPS item's own land zone (§6.4 step 1)
        var own = new string?[n];
        for (var i = 0; i < n; i++)
            if (gps[i] is { } fix && Lookup(fix.Point) is { IsEtc: false } l && Zones.TryFind(l.IanaId, out _))
                own[i] = l.IanaId;

        // 3. power-on sessions (§6.3 step 6)
        var sessions = new SessionKey?[n];
        for (var i = 0; i < n; i++)
            if (raw[i].Mp4 is { SessionUtc: { } su } m)
                sessions[i] = new SessionKey(m.DroneSerial, DateTime.SpecifyKind(su, DateTimeKind.Utc));

        // 4. capture time (§6.2); SiteLocal converts through the own zone, else the nearest GPS item ≤ 12 h by drone stamp (§6.1)
        var utc = new DateTime[n];
        var src = new TimeSource[n];
        for (var i = 0; i < n; i++)
            (utc[i], src[i]) = Capture(raw[i], clock, own[i] ?? NearestOwnByStamp(raw, own, i));

        // 5. zone, local date and flags (§6.4, §6.5)
        var pcId = Zones.IanaId(pc);
        var result = ImmutableArray.CreateBuilder<ResolvedItem>(n);
        for (var i = 0; i < n; i++)
        {
            var (tzId, tzSource, fallback) = ZoneFor(i, gps, own, sessions, utc, places, pcId);
            if (!Zones.TryFind(tzId, out var zone))
            {
                (tzId, tzSource, fallback, zone) = (pcId, TzSource.PcZone, true, pc);
            }
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc[i], zone);
            var time = new ItemTime(utc[i], src[i], tzId, tzSource, DateOnly.FromDateTime(local), local);
            var flags = BaseFlags(raw[i], gps[i] is null, guessed[i], fallback);
            result.Add(new ResolvedItem(raw[i], time, flags, gps[i], sessions[i]));
        }
        return result.MoveToImmutable();
    }

    static GpsFix? FirstFix(RawItem r)
    {
        if (r.Mp4 is { } m && m.First is GpsFix video) return video;
        if (r.Still is { } s && s.Gps is GpsFix photo) return photo;
        return null;
    }

    static (DateTime Utc, TimeSource Source) Capture(RawItem r, ClockModel clock, string? clockZone)
    {
        if (r.Kind == ItemKind.Video && r.Mp4 is { HasMoov: true, MvhdUtc: { } mvhd })
            return (DateTime.SpecifyKind(mvhd, DateTimeKind.Utc), TimeSource.Mvhd);
        if (r.Still is { DtoNaive: { } dto, OffsetTime: { } offset })
            return (DateTime.SpecifyKind(dto - offset, DateTimeKind.Utc), TimeSource.ExifWithOffset);
        if (r.DroneStamp is { } stamp && clock.ToUtc(stamp, clockZone) is { } converted)
            return (converted.Utc, converted.Src);
        return (DateTime.SpecifyKind(r.CardMtimeUtc, DateTimeKind.Utc), TimeSource.Mtime);
    }

    static string? NearestOwnByStamp(IReadOnlyList<RawItem> raw, string?[] own, int i)
    {
        if (raw[i].DroneStamp is not { } stamp) return null;
        string? best = null;
        var bestGap = TimeSpan.MaxValue;
        for (var j = 0; j < raw.Count; j++)
        {
            if (j == i || own[j] is null || raw[j].DroneStamp is not { } other) continue;
            var gap = (other - stamp).Duration();
            if (gap <= NearbyWindow && gap < bestGap) { best = own[j]; bestGap = gap; }
        }
        return best;
    }

    static (string Id, TzSource Source, bool Fallback) ZoneFor(int i, GpsFix?[] gps, string?[] own, SessionKey?[] sessions,
                                                               DateTime[] utc, IPlaceIndex? places, string pcId)
    {
        if (own[i] is { } mine) return (mine, TzSource.Gps, false);
        if (gps[i] is { } fix)
        {   // GPS over the sea: an Etc/* zone (§6.4 step 2)
            if (NearestOwn(own, utc, i, static _ => true, within12h: true) is { } land) return (land, TzSource.NearestLandGpsOnCard, true);
            if (NearestPlaceZone(places, fix.Point) is { } placeZone) return (placeZone, TzSource.GeoNamesTz, true);
            return (pcId, TzSource.PcZone, true);
        }
        // no GPS (§6.4 step 3)
        if (sessions[i] is { } s
            && NearestOwn(own, utc, i, j => sessions[j] is { } sj && sj.SameSession(s), within12h: false) is { } sessionZone)
            return (sessionZone, TzSource.SameSession, false);
        if (NearestOwn(own, utc, i, static _ => true, within12h: true) is { } nearby) return (nearby, TzSource.NearestGpsWithin12h, false);
        return (pcId, TzSource.PcZone, true);
    }

    static string? NearestOwn(string?[] own, DateTime[] utc, int i, Func<int, bool> eligible, bool within12h)
    {
        string? best = null;
        var bestGap = TimeSpan.MaxValue;
        for (var j = 0; j < own.Length; j++)
        {
            if (j == i || own[j] is null || !eligible(j)) continue;
            var gap = (utc[j] - utc[i]).Duration();
            if (within12h && gap > NearbyWindow) continue;
            if (gap < bestGap) { best = own[j]; bestGap = gap; }
        }
        return best;
    }

    static string? NearestPlaceZone(IPlaceIndex? places, GeoPoint p)
    {
        if (places is null) return null;
        PlaceHit? best = null;
        foreach (var cls in PlaceClasses)
            foreach (var hit in places.Near(p, GeoNamesTzRadius, cls, 1))
                if (Zones.TryFind(hit.TzId, out _) && (best is null || hit.Away.Meters < best.Away.Meters))
                    best = hit;
        return best?.TzId;
    }

    static ItemFlags BaseFlags(RawItem r, bool noGps, bool guessed, bool tzFallback)
    {
        var f = ItemFlags.None;
        if (noGps) f |= ItemFlags.NoGps;
        if (guessed) f |= ItemFlags.GpsGuessed;
        if (tzFallback) f |= ItemFlags.TzFallback;
        if (r.ProbeError is not null) f |= ItemFlags.ProbeFailed;
        if (r.Kind == ItemKind.Video && (r.Mp4 is { HasMoov: false } || r.Unit is VideoUnit { HasTrinf: true }))
            f |= ItemFlags.Truncated;
        return f;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Resolve_*"`
Expected: test run summary Passed, 16 succeeded, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/UasSort.Core/Time/TimeResolver.cs tests/UasSort.Testing/FakePlaceIndex.cs tests/UasSort.Core.Tests/Time/TimeResolverTests.cs
git commit -F - <<'EOF'
feat: add TimeResolver capture time, site zone and fallbacks (Part 04)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 04.8: TimeFlags — CheckDate windows, ClockNotSet, ClockFromSetting, ClockMismatch

**Files:**
- Create: `src/UasSort.Core/Time/TimeFlags.cs`
- Modify: `src/UasSort.Core/Time/TimeResolver.cs` (two edits below)
- Test: `tests/UasSort.Core.Tests/Time/TimeFlagsTests.cs`

**Interfaces:**
```csharp
// Consumes: TimeResolver (04.7), ClockModel.OffsetAt (04.5); Part 02: ItemFlags, ItemTime, TzLookup, TzSource, TimeSource
// Produces (defined here; the predicate is the §13 "15-min boundary" unit):
namespace UasSort.Core.Time;
public static class TimeFlags {
  public static readonly TimeSpan MismatchThreshold;         // 15 min
  public static readonly DateTime EarliestPlausibleUtc;      // 2015-01-01T00:00Z
  public const double AlternativesWindowMinutes = 60;         // CheckDate (a)
  public const double ClockWindowMinutes = 75;                // CheckDate (b)
  public static bool IsClockMismatch(TimeSpan droneOffset, TimeSpan siteOffset);   // |Δ| ≥ 15 min
  public static double MinutesFromMidnight(DateTime local);
  public static bool IsClockSource(TimeSource s);             // Mvhd or any DroneClock*
  public static ItemFlags For(RawItem raw, ItemTime time, TzLookup? ownLookup, ClockModel clock, TimeZoneInfo zone, DateTime nowUtc);
}
```

Rules (Ref §6.5): `ClockNotSet` when `CaptureUtc` &lt; 2015-01-01 or > now + 1 day. `CheckDate` when (a) the item's own GeoTimeZone lookup (zone source `Gps`) has alternatives and local time is ≤ 60 min from midnight, or (b) the source is `Mvhd`/`DroneClock*` and local time is ≤ 75 min from midnight. `ClockFromSetting` when the source is `DroneClockSetting`. `ClockMismatch` when the item has a drone stamp, its zone source is not `PcZone`, its source is not `Mtime`, and `|ClockModel.OffsetAt(stamp, TzId) − zone.GetUtcOffset(CaptureUtc)| ≥ 15 min`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Time/TimeFlagsTests.cs
using static UasSort.Testing.RawItemBuilder;

namespace UasSort.Core.Tests.Time;

public sealed class TimeFlagsTests
{
    const string NewYork = "America/New_York";
    static readonly ITimeZoneResolver Tz = new GeoTimeZoneResolver();
    static readonly TimeZoneInfo AlaskaPc = Zones.Find("America/Anchorage");
    static readonly GeoPoint Stewart = new(55.94, -129.99);   // border point: GeoTimeZone gives an alternative (Ref research §4)

    static ImmutableArray<ResolvedItem> Resolve(IReadOnlyList<RawItem> raw, DateTime nowUtc, ITimeZoneResolver? tz = null,
                                                StoredClockMode mode = StoredClockMode.Zone, string zone = NewYork)
    {
        var resolver = tz ?? Tz;
        var clock = DroneClock.Learn(raw, mode, zone, resolver, AlaskaPc);
        return TimeResolver.Resolve(raw, clock, resolver, null, AlaskaPc, nowUtc);
    }

    static DateTime NowAt(int y, int mo, int d, int h) => new FakeTimeProvider(new DateTimeOffset(y, mo, d, h, 0, 0, TimeSpan.Zero)).GetUtcNow().UtcDateTime;
    static readonly DateTime Later = NowAt(2027, 6, 1, 0);

    [Fact]
    public void Flags_MismatchPredicate_TrueAt15MinFalseAt14m59s()
    {
        var clock = new TimeSpan(5, 30, 0);
        Assert.True(TimeFlags.IsClockMismatch(clock, new TimeSpan(5, 45, 0)));
        Assert.False(TimeFlags.IsClockMismatch(clock, new TimeSpan(5, 44, 59)));
        Assert.True(TimeFlags.IsClockMismatch(new TimeSpan(5, 45, 0), clock));
        Assert.True(TimeFlags.IsClockMismatch(TimeSpan.FromHours(-4), TimeSpan.FromHours(-8)));
        Assert.False(TimeFlags.IsClockMismatch(TimeSpan.FromHours(-4), TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void Flags_MinutesFromMidnight_IsTheNearerMidnight()
    {
        Assert.Equal(50, TimeFlags.MinutesFromMidnight(new DateTime(2026, 9, 27, 23, 10, 0)));
        Assert.Equal(40, TimeFlags.MinutesFromMidnight(new DateTime(2026, 9, 28, 0, 40, 0)));
        Assert.Equal(720, TimeFlags.MinutesFromMidnight(new DateTime(2026, 9, 28, 12, 0, 0)));
    }

    [Fact]
    public void Flags_CheckDate_ZoneAlternativesWindowIs60Min()
    {
        var tz = new FakeTimeZoneResolver(Tz).With(Stewart, new TzLookup("America/Sitka", ["America/Vancouver"], false));
        var r = Resolve(
        [
            Other("IMG_0001.JPG", Utc(2026, 9, 28, 7, 10), Stewart),               // 23:10 AKDT, 50 min
            Other("IMG_0002.JPG", Utc(2026, 9, 28, 6, 55), Stewart),               // 22:55 AKDT, 65 min
            Other("IMG_0003.JPG", Utc(2026, 9, 28, 7, 10), FixturePoints.Zachar),  // 23:10 AKDT, no alternatives, Mtime
        ], Later, tz);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.CheckDate));
        Assert.False(r[1].Flags.HasFlag(ItemFlags.CheckDate));
        Assert.False(r[2].Flags.HasFlag(ItemFlags.CheckDate));
    }

    [Fact]
    public void Flags_CheckDate_ClockSourceWindowIs75Min()
    {
        var r = Resolve(
        [
            Vid("20260928024500", 1, FixturePoints.Zachar),   // 06:45Z = 22:45 AKDT, 75 min
            Vid("20260928024400", 2, FixturePoints.Zachar),   // 06:44Z = 22:44 AKDT, 76 min
            Vid("20260928044000", 3, FixturePoints.Zachar),   // 08:40Z = 00:40 AKDT, 40 min
        ], Later);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.CheckDate));
        Assert.False(r[1].Flags.HasFlag(ItemFlags.CheckDate));
        Assert.True(r[2].Flags.HasFlag(ItemFlags.CheckDate));
    }

    [Fact]
    public void Flags_ClockNotSet_Pre2015AndMoreThanADayAhead()
    {
        var now = NowAt(2026, 9, 27, 20);
        var r = Resolve(
        [
            Dng("20140601120000", 1, null),   // 2014-06-01T16:00Z
            Dng("20260928150000", 2, null),   // 2026-09-28T19:00Z, within now + 1 day
            Dng("20260928170000", 3, null),   // 2026-09-28T21:00Z, beyond now + 1 day
        ], now);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.ClockNotSet));
        Assert.False(r[1].Flags.HasFlag(ItemFlags.ClockNotSet));
        Assert.True(r[2].Flags.HasFlag(ItemFlags.ClockNotSet));
    }

    [Fact]
    public void Flags_ClockFromSetting_OnlyWhenTheCardHasNoClockSample()
    {
        var photoOnly = Resolve([Dng("20260927150000", 1, FixturePoints.Zachar)], Later);
        Assert.True(photoOnly[0].Flags.HasFlag(ItemFlags.ClockFromSetting));
        var withVideo = Resolve([Vid("20260927140127", 123, FixturePoints.Zachar), Dng("20260927150000", 1, FixturePoints.Zachar)], Later);
        Assert.All(withVideo, i => Assert.False(i.Flags.HasFlag(ItemFlags.ClockFromSetting)));
    }

    [Fact]
    public void Flags_ClockMismatch_WorkedExampleZachar0128()
    {
        var r = Resolve([Vid("20260927140627", 128, new GeoPoint(57.55044, -153.73897))], Later);
        var t = r[0].Time;
        Assert.Equal("America/Anchorage", t.TzId);
        Assert.Equal(Utc(2026, 9, 27, 18, 6, 27), t.CaptureUtc);
        Assert.Equal(new DateTime(2026, 9, 27, 10, 6, 27), t.LocalTime);
        Assert.Equal(new DateOnly(2026, 9, 27), t.LocalDate);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.ClockMismatch));
    }

    [Fact]
    public void Flags_ClockMismatch_NotForPcZoneFallback()
    {
        var r = Resolve([Dng("20260927150000", 1, null)], Later);   // Eastern stored clock, no GPS → PC zone (Alaska)
        Assert.Equal(TzSource.PcZone, r[0].Time.TzSource);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.TzFallback));
        Assert.False(r[0].Flags.HasFlag(ItemFlags.ClockMismatch));
    }

    [Fact]
    public void Flags_ClockMismatch_NotForMtimeItems()
    {
        var r = Resolve([Vid("20260927140127", 123, FixturePoints.Zachar), Other("IMG_0001.JPG", Utc(2026, 9, 27, 19, 0), FixturePoints.Zachar)], Later);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.ClockMismatch));
        Assert.Equal(TimeSource.Mtime, r[1].Time.Source);
        Assert.False(r[1].Flags.HasFlag(ItemFlags.ClockMismatch));
    }

    [Fact]
    public void Flags_TruncatedClip_ExactFlagSet()
    {
        var r = Resolve(
        [
            Vid("20260726235645", 1, FixturePoints.Anvil),
            Vid("20260727002013", 14, FixturePoints.Anvil, moov: false),
            Vid("20260727002118", 15, FixturePoints.Anvil),
        ], Later);
        Assert.Equal(ItemFlags.Truncated | ItemFlags.ClockMismatch, r[1].Flags);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Flags_*"`
Expected: build FAILS with `error CS0103: The name 'TimeFlags' does not exist in the current context`.

- [ ] **Step 3: Write `TimeFlags`**

```csharp
// src/UasSort.Core/Time/TimeFlags.cs
namespace UasSort.Core.Time;

/// <summary>The time-window and clock flags of Ref §6.5. Info only: none of them changes a date.</summary>
public static class TimeFlags
{
    public static readonly TimeSpan MismatchThreshold = TimeSpan.FromMinutes(15);
    public static readonly DateTime EarliestPlausibleUtc = new(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public const double AlternativesWindowMinutes = 60;
    public const double ClockWindowMinutes = 75;

    public static bool IsClockMismatch(TimeSpan droneOffset, TimeSpan siteOffset)
        => (droneOffset - siteOffset).Duration() >= MismatchThreshold;

    public static double MinutesFromMidnight(DateTime local)
    {
        var minutes = local.TimeOfDay.TotalMinutes;
        return Math.Min(minutes, 1440 - minutes);
    }

    public static bool IsClockSource(TimeSource s) => s is TimeSource.Mvhd or TimeSource.DroneClockSiteLocal
        or TimeSource.DroneClockZone or TimeSource.DroneClockSample or TimeSource.DroneClockSetting;

    public static ItemFlags For(RawItem raw, ItemTime time, TzLookup? ownLookup, ClockModel clock, TimeZoneInfo zone, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(zone);
        var f = ItemFlags.None;

        if (time.CaptureUtc < EarliestPlausibleUtc || time.CaptureUtc > nowUtc.AddDays(1)) f |= ItemFlags.ClockNotSet;

        var fromMidnight = MinutesFromMidnight(time.LocalTime);
        var hasAlternatives = time.TzSource == TzSource.Gps && ownLookup is { } l && !l.Alternatives.IsDefaultOrEmpty;
        if ((hasAlternatives && fromMidnight <= AlternativesWindowMinutes) || (IsClockSource(time.Source) && fromMidnight <= ClockWindowMinutes))
            f |= ItemFlags.CheckDate;

        if (time.Source == TimeSource.DroneClockSetting) f |= ItemFlags.ClockFromSetting;

        if (raw.DroneStamp is { } stamp && time.TzSource != TzSource.PcZone && time.Source != TimeSource.Mtime
            && IsClockMismatch(clock.OffsetAt(stamp, time.TzId), zone.GetUtcOffset(DateTime.SpecifyKind(time.CaptureUtc, DateTimeKind.Utc))))
            f |= ItemFlags.ClockMismatch;

        return f;
    }
}
```

- [ ] **Step 4: Wire it into `TimeResolver`**

In `src/UasSort.Core/Time/TimeResolver.cs`, replace step 2:

```csharp
        // 2. each GPS item's own land zone (§6.4 step 1)
        var own = new string?[n];
        for (var i = 0; i < n; i++)
            if (gps[i] is { } fix && Lookup(fix.Point) is { IsEtc: false } l && Zones.TryFind(l.IanaId, out _))
                own[i] = l.IanaId;
```

with:

```csharp
        // 2. each GPS item's own land zone (§6.4 step 1), and its lookup for CheckDate (a)
        var own = new string?[n];
        var ownLookup = new TzLookup?[n];
        for (var i = 0; i < n; i++)
            if (gps[i] is { } fix)
            {
                var l = Lookup(fix.Point);
                ownLookup[i] = l;
                if (!l.IsEtc && Zones.TryFind(l.IanaId, out _)) own[i] = l.IanaId;
            }
```

and replace the flags line:

```csharp
            var flags = BaseFlags(raw[i], gps[i] is null, guessed[i], fallback);
```

with:

```csharp
            var flags = BaseFlags(raw[i], gps[i] is null, guessed[i], fallback)
                        | TimeFlags.For(raw[i], time, ownLookup[i], clock, zone, nowUtc);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Flags_*"`
Expected: test run summary Passed, 10 succeeded, 0 failed.
Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Resolve_*"`
Expected: test run summary Passed, 16 succeeded, 0 failed (no regression).

- [ ] **Step 6: Commit**

```bash
git add src/UasSort.Core/Time/TimeFlags.cs src/UasSort.Core/Time/TimeResolver.cs tests/UasSort.Core.Tests/Time/TimeFlagsTests.cs
git commit -F - <<'EOF'
feat: add CheckDate, ClockNotSet and ClockMismatch flags (Part 04)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 04.9: Clock summary, learned-mode saving, and the §13 clock scenarios (incl. Review Focus 4)

**Files:**
- Create: `src/UasSort.Core/Time/DroneClock.Summary.cs`
- Test: `tests/UasSort.Core.Tests/Time/ClockScenarioTests.cs`

**Interfaces:**
```csharp
// Consumes: DroneClock.Learn (04.6), TimeResolver (04.7–04.8), ZoneNames (04.4);
//           Part 02: ClockSummary(ClockMode Mode, string? ZoneId, int SampleCount, string Headline, int MismatchItems,
//                                 ImmutableArray<string> MismatchSiteZones, ImmutableArray<ClockChange> Changes),
//                    ClockChange(DateTime AtUtc, TimeSpan From, TimeSpan To), Settings (DroneClockMode, DroneClockZone), ResolvedItem
// Produces (defined here; Planner.Prepare (Part 06) builds PlanBase.Clock with Summarize; the offload (Part 07) saves with ApplyLearned):
namespace UasSort.Core.Time;
public static partial class DroneClock {
  public static ClockSummary Summarize(ClockModel clock, IReadOnlyList<ResolvedItem> items);
  public static ImmutableArray<ClockChange> Changes(ImmutableArray<ClockSample> samples);   // runs by MvhdUtc; midpoints
  public static Settings ApplyLearned(Settings current, ClockModel clock);   // SiteLocal → mode only; Zone → mode + zone; else unchanged
}
```

Headline texts (Ref §6.1 "Header text"; the mismatch line is appended in any mode):
- Zone: `Drone clock: US Eastern (America/New_York), learned from 13 videos. Folder dates use local time at each site.`
- SiteLocal: `Drone clock: follows local time at each site, learned from 13 videos.`
- NearestSample: `Drone clock: no single time zone fits these videos, so each item uses the offset of the nearest video in time.` then, per clock change, ` Drone clock changed during this card: UTC−4 until Sep 27 10:55 AKDT, then UTC−8.` (the midpoint in the later sample's site zone).
- Setting: `Drone clock: no videos on this card: using the last learned clock, US Eastern (America/New_York).` or `…, local time at each site.`
- Mismatch: ` It doesn't match local time where this card was shot (Alaska).` (regions of `MismatchSiteZones`, deduplicated, comma-joined.)

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Time/ClockScenarioTests.cs
using static UasSort.Testing.RawItemBuilder;

namespace UasSort.Core.Tests.Time;

/// <summary>The clock-learner, SiteLocal and ClockMismatch cases of Ref §13 "Time", end to end through Learn → Resolve → Summarize.</summary>
public sealed class ClockScenarioTests
{
    const string NewYork = "America/New_York";
    static readonly ITimeZoneResolver Tz = new GeoTimeZoneResolver();
    static readonly TimeZoneInfo AlaskaPc = Zones.Find("America/Anchorage");
    static readonly DateTime Now = new FakeTimeProvider(new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero)).GetUtcNow().UtcDateTime;
    static readonly TimeSpan Alaska = TimeSpan.FromHours(-8);

    static (ClockModel Clock, ImmutableArray<ResolvedItem> Items, ClockSummary Summary) Run(
        IReadOnlyList<RawItem> raw, StoredClockMode mode = StoredClockMode.Zone, string zone = NewYork)
    {
        var clock = DroneClock.Learn(raw, mode, zone, Tz, AlaskaPc);
        var items = TimeResolver.Resolve(raw, clock, Tz, null, AlaskaPc, Now);
        return (clock, items, DroneClock.Summarize(clock, items));
    }

    static Settings Stored(StoredClockMode mode, string zone) => new(
        1, @"C:\Videos", @"C:\Videos\Picture Offload", [], 50, 1, mode, zone, true,
        new MapSettings("streets", "https://tiles.openfreemap.org/styles/liberty", "https://tiles.openfreemap.org/styles/dark",
                        "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                        ImmutableDictionary<string, string>.Empty),
        new LayoutSettings(380, 0.45), true);

    static bool Mismatch(ResolvedItem i) => i.Flags.HasFlag(ItemFlags.ClockMismatch);

    [Fact]
    public void Clock_Scenario_SiteLocalFit_AlaskaClock()
    {
        var (clock, items, summary) = Run(
        [
            Vid("20260927100127", 123, FixturePoints.Zachar, Alaska),
            Vid("20260927100144", 124, FixturePoints.Zachar, Alaska),
            Dng("20260927101000", 1, FixturePoints.Zachar),
        ]);
        Assert.Equal(ClockMode.SiteLocal, clock.Mode);
        Assert.Equal(TimeSource.DroneClockSiteLocal, items[2].Time.Source);
        Assert.Equal(Utc(2026, 9, 27, 18, 10), items[2].Time.CaptureUtc);
        Assert.DoesNotContain(items, Mismatch);
        Assert.Equal("Drone clock: follows local time at each site, learned from 2 videos.", summary.Headline);
        Assert.Equal(0, summary.MismatchItems);

        var saved = DroneClock.ApplyLearned(Stored(StoredClockMode.Zone, NewYork), clock);
        Assert.Equal(StoredClockMode.SiteLocal, saved.DroneClockMode);
        Assert.Equal(NewYork, saved.DroneClockZone);   // left as it was
    }

    [Fact]
    public void Clock_Scenario_EasternClockInAlaska_MismatchOnEveryItem()
    {
        var (clock, items, summary) = Run(
        [
            Vid("20260927140127", 123, FixturePoints.Zachar),
            Vid("20260927140144", 124, FixturePoints.Zachar),
            Vid("20260927142416", 148, FixturePoints.Zachar),
            Dng("20260927142000", 1, FixturePoints.Zachar),
        ]);
        Assert.Equal(ClockMode.Zone, clock.Mode);
        Assert.Equal(NewYork, clock.ZoneId);
        Assert.All(items, i => Assert.True(Mismatch(i)));
        Assert.Equal(4, summary.MismatchItems);
        string[] expectedZones = ["America/Anchorage"];
        Assert.Equal(expectedZones, summary.MismatchSiteZones.ToArray());
        Assert.True(summary.Changes.IsEmpty);
        Assert.Equal(3, summary.SampleCount);
        Assert.Equal("Drone clock: US Eastern (America/New_York), learned from 3 videos. Folder dates use local time at each site."
                     + " It doesn't match local time where this card was shot (Alaska).", summary.Headline);

        var saved = DroneClock.ApplyLearned(Stored(StoredClockMode.SiteLocal, "America/Chicago"), clock);
        Assert.Equal(StoredClockMode.Zone, saved.DroneClockMode);
        Assert.Equal(NewYork, saved.DroneClockZone);
    }

    [Fact]
    public void Clock_Scenario_EasternClockInNewport_SiteLocalNoMismatch()
    {
        var (clock, items, summary) = Run([Vid("20260510104103", 2, FixturePoints.NewportAm), Vid("20260510193745", 30, FixturePoints.NewportPm)]);
        Assert.Equal(ClockMode.SiteLocal, clock.Mode);
        Assert.DoesNotContain(items, Mismatch);
        Assert.Equal(0, summary.MismatchItems);
        Assert.DoesNotContain("doesn't match", summary.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void Clock_Scenario_TripAcrossZones_SiteLocalClock()
    {
        var (clock, items, _) = Run(
        [
            Vid("20260510104103", 1, FixturePoints.NewportAm),
            Vid("20260726195000", 2, FixturePoints.Anvil, Alaska),
            Dng("20260726195500", 3, null),
        ]);
        Assert.Equal(ClockMode.SiteLocal, clock.Mode);
        var dng = items[2];
        Assert.Equal(TimeSource.DroneClockSiteLocal, dng.Time.Source);
        Assert.Equal(Utc(2026, 7, 27, 3, 55), dng.Time.CaptureUtc);
        Assert.Equal("America/Nome", dng.Time.TzId);
        Assert.Equal(TzSource.NearestGpsWithin12h, dng.Time.TzSource);
        Assert.DoesNotContain(items, Mismatch);
    }

    [Theory]
    [InlineData(StoredClockMode.SiteLocal, 19, false)]
    [InlineData(StoredClockMode.Zone, 15, true)]
    public void Clock_Scenario_PhotoOnlyCard_UsesStoredMode(StoredClockMode mode, int utcHour, bool mismatch)
    {
        var (clock, items, summary) = Run([Dng("20260927110000", 1, FixturePoints.Zachar)], mode);
        Assert.Equal(ClockMode.Setting, clock.Mode);
        var i = Assert.Single(items);
        Assert.Equal(TimeSource.DroneClockSetting, i.Time.Source);
        Assert.True(i.Flags.HasFlag(ItemFlags.ClockFromSetting));
        Assert.Equal(Utc(2026, 9, 27, utcHour, 0), i.Time.CaptureUtc);
        Assert.Equal("America/Anchorage", i.Time.TzId);
        Assert.Equal(mismatch, Mismatch(i));
        Assert.StartsWith("Drone clock: no videos on this card: using the last learned clock", summary.Headline, StringComparison.Ordinal);
        var stored = Stored(mode, NewYork);
        Assert.Same(stored, DroneClock.ApplyLearned(stored, clock));   // Setting saves nothing
    }

    [Fact]
    public void Clock_Scenario_NoMp4_UsesStoredZone()
    {
        var (clock, items, _) = Run([Dng("20260927150000", 1, FixturePoints.Zachar)]);
        Assert.Equal(ClockMode.Setting, clock.Mode);
        var i = Assert.Single(items);
        Assert.Equal(TimeSource.DroneClockSetting, i.Time.Source);
        Assert.Equal(Utc(2026, 9, 27, 19, 0), i.Time.CaptureUtc);
        Assert.Equal(ItemFlags.ClockFromSetting | ItemFlags.ClockMismatch, i.Flags);
    }

    [Fact]
    public void Clock_Scenario_ClockCorrectedMidCard_NearestSampleWithOneChange()
    {
        List<RawItem> raw =
        [
            Vid("20260927140000", 1, FixturePoints.Zachar), Vid("20260927141000", 2, FixturePoints.Zachar),
            Vid("20260927142000", 3, FixturePoints.Zachar), Vid("20260927143000", 4, FixturePoints.Zachar),
            Vid("20260927144000", 5, FixturePoints.Zachar), Vid("20260927145000", 6, FixturePoints.Zachar),   // Eastern clock, 18:00–18:50Z
            Vid("20260927110000", 7, FixturePoints.Zachar, Alaska), Vid("20260927111000", 8, FixturePoints.Zachar, Alaska),
            Vid("20260927112000", 9, FixturePoints.Zachar, Alaska), Vid("20260927113000", 10, FixturePoints.Zachar, Alaska),
            Vid("20260927114000", 11, FixturePoints.Zachar, Alaska),                                           // Alaska clock, 19:00–19:40Z
            Dng("20260927135500", 90, FixturePoints.Zachar),   // before: nearest #1 (−4) → 17:55Z
            Dng("20260927145300", 91, FixturePoints.Zachar),   // between, old clock: nearest #6 (−4) → 18:53Z
            Dng("20260927105800", 92, FixturePoints.Zachar),   // between, new clock: nearest #7 (−8) → 18:58Z
            Dng("20260927114500", 93, FixturePoints.Zachar),   // after: nearest #11 (−8) → 19:45Z
        ];
        var (clock, items, summary) = Run(raw);
        Assert.Equal(ClockMode.NearestSample, clock.Mode);
        Assert.Equal(TimeSpan.FromHours(-4), clock.Modal);

        var change = Assert.Single(summary.Changes);
        Assert.Equal(Utc(2026, 9, 27, 18, 55), change.AtUtc);
        Assert.Equal(TimeSpan.FromHours(-4), change.From);
        Assert.Equal(Alaska, change.To);

        DateTime[] dngUtc = [Utc(2026, 9, 27, 17, 55), Utc(2026, 9, 27, 18, 53), Utc(2026, 9, 27, 18, 58), Utc(2026, 9, 27, 19, 45)];
        Assert.Equal(dngUtc, items.Skip(11).Select(i => i.Time.CaptureUtc).ToArray());
        Assert.All(items.Skip(11), i => Assert.Equal(TimeSource.DroneClockSample, i.Time.Source));
        Assert.All(items, i => Assert.Equal(i.Time.CaptureUtc < change.AtUtc, Mismatch(i)));   // only items before the change

        Assert.Equal("Drone clock: no single time zone fits these videos, so each item uses the offset of the nearest video in time."
                     + " Drone clock changed during this card: UTC\u22124 until Sep 27 10:55 AKDT, then UTC\u22128."
                     + " It doesn't match local time where this card was shot (Alaska).", summary.Headline);
        var stored = Stored(StoredClockMode.Zone, NewYork);
        Assert.Same(stored, DroneClock.ApplyLearned(stored, clock));   // NearestSample saves nothing
    }

    [Fact]
    public void Clock_Scenario_KolkataClockInKathmandu_Exactly15MinMismatch()
    {
        var kolkataClock = new TimeSpan(5, 30, 0);
        var (clock, items, summary) = Run(
        [
            Vid("20260927100000", 1, FixturePoints.Kathmandu, kolkataClock),
            Vid("20260927110000", 2, FixturePoints.Kathmandu, kolkataClock),
        ]);
        Assert.Equal(ClockMode.NearestSample, clock.Mode);
        Assert.Equal(kolkataClock, clock.Modal);
        Assert.All(items, i =>
        {
            Assert.Equal("Asia/Kathmandu", i.Time.TzId);
            Assert.True(Mismatch(i));
        });
        Assert.EndsWith(" It doesn't match local time where this card was shot (Asia/Kathmandu).", summary.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void Clock_Scenario_KolkataClockInKolkata_NoMismatch()
    {
        var kolkataClock = new TimeSpan(5, 30, 0);
        var (clock, items, _) = Run(
        [
            Vid("20260927100000", 1, FixturePoints.Kolkata, kolkataClock),
            Vid("20260927110000", 2, FixturePoints.Kolkata, kolkataClock),
        ]);
        Assert.Equal(ClockMode.SiteLocal, clock.Mode);
        Assert.DoesNotContain(items, Mismatch);
    }

    [Fact] // [Review Focus] 4: DST end 2026-11-01 with a zone-fitted America/New_York clock
    public void Clock_Scenario_DstEnd_RepeatedHourUsesNearestSample_LocalDatesAndCheckDateWindows()
    {
        var minus5 = TimeSpan.FromHours(-5);
        List<RawItem> raw =
        [
            Vid("20261031140000", 1, FixturePoints.Zachar),           // 18:00Z = Oct 31 10:00 AKDT
            Vid("20261101012000", 2, FixturePoints.Zachar),           // first 01:20 (EDT) = 05:20Z = Oct 31 21:20 AKDT
            Vid("20261101013000", 3, FixturePoints.Zachar, minus5),   // second 01:30 (EST) = 06:30Z = Oct 31 22:30 AKDT
            Vid("20261101040000", 4, FixturePoints.Zachar, minus5),   // 09:00Z = Nov 1 01:00 AKDT (60 min)
            Dng("20261101012200", 11, FixturePoints.Zachar),          // repeated hour, nearest #2 (−4) → 05:22Z
            Dng("20261101012800", 12, FixturePoints.Zachar),          // repeated hour, nearest #3 (−5) → 06:28Z
            Dng("20261101041500", 13, FixturePoints.Zachar),          // 09:15Z = Nov 1 01:15 AKDT (75 min)
            Dng("20261101041600", 14, FixturePoints.Zachar),          // 09:16Z = Nov 1 01:16 AKDT (76 min)
            Dng("20261101043000", 15, FixturePoints.Zachar),          // 09:30Z = Nov 1 01:30 AKDT (90 min)
        ];
        var (clock, items, summary) = Run(raw);

        Assert.Equal(ClockMode.Zone, clock.Mode);
        Assert.Equal(NewYork, clock.ZoneId);

        DateTime[] expectedUtc =
        [
            Utc(2026, 10, 31, 18, 0), Utc(2026, 11, 1, 5, 20), Utc(2026, 11, 1, 6, 30), Utc(2026, 11, 1, 9, 0),
            Utc(2026, 11, 1, 5, 22), Utc(2026, 11, 1, 6, 28), Utc(2026, 11, 1, 9, 15), Utc(2026, 11, 1, 9, 16), Utc(2026, 11, 1, 9, 30),
        ];
        Assert.Equal(expectedUtc, items.Select(i => i.Time.CaptureUtc).ToArray());
        Assert.All(items.Skip(4), i => Assert.Equal(TimeSource.DroneClockZone, i.Time.Source));

        DateOnly oct31 = new(2026, 10, 31), nov1 = new(2026, 11, 1);
        DateOnly[] expectedDates = [oct31, oct31, oct31, nov1, oct31, oct31, nov1, nov1, nov1];
        Assert.Equal(expectedDates, items.Select(i => i.Time.LocalDate).ToArray());   // the drone's Nov 1 01:xx stamps are Oct 31 locally

        bool[] expectedCheckDate = [false, false, false, true, false, false, true, false, false];
        Assert.Equal(expectedCheckDate, items.Select(i => i.Flags.HasFlag(ItemFlags.CheckDate)).ToArray());

        Assert.Equal("Drone clock: US Eastern (America/New_York), learned from 4 videos. Folder dates use local time at each site."
                     + " It doesn't match local time where this card was shot (Alaska).", summary.Headline);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Clock_Scenario_*"`
Expected: build FAILS with `error CS0117: 'DroneClock' does not contain a definition for 'Summarize'` (and `ApplyLearned`).

- [ ] **Step 3: Write the implementation**

```csharp
// src/UasSort.Core/Time/DroneClock.Summary.cs
using System.Globalization;
using System.Text;

namespace UasSort.Core.Time;

public static partial class DroneClock
{
    /// <summary>The clock banner data (Ref §6.1 "Header text", §9.2): headline, clock changes, and the ClockMismatch zones.</summary>
    public static ClockSummary Summarize(ClockModel clock, IReadOnlyList<ResolvedItem> items)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(items);
        var mismatched = items.Where(i => i.Flags.HasFlag(ItemFlags.ClockMismatch)).OrderBy(i => i.Time.CaptureUtc).ToList();
        var zones = mismatched.Select(i => i.Time.TzId).Distinct(StringComparer.Ordinal).ToImmutableArray();
        var runs = clock.Mode == ClockMode.NearestSample ? ChangeRuns(clock.Samples) : [];
        var headline = Headline(clock, runs) + MismatchLine(zones);
        return new ClockSummary(clock.Mode, clock.ZoneId, clock.Samples.Length, headline, mismatched.Count, zones,
                                [.. runs.Select(r => r.Change)]);
    }

    public static ImmutableArray<ClockChange> Changes(ImmutableArray<ClockSample> samples) => [.. ChangeRuns(samples).Select(r => r.Change)];

    /// <summary>After a successful run (Ref §6.1): SiteLocal saves the mode only; a fitted zone saves mode and zone; nothing else saves.</summary>
    public static Settings ApplyLearned(Settings current, ClockModel clock)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(clock);
        return clock.Mode switch
        {
            ClockMode.SiteLocal => current with { DroneClockMode = StoredClockMode.SiteLocal },
            ClockMode.Zone when clock.ZoneId is { } zone => current with { DroneClockMode = StoredClockMode.Zone, DroneClockZone = zone },
            _ => current,
        };
    }

    static List<(ClockChange Change, string? LaterZone)> ChangeRuns(ImmutableArray<ClockSample> samples)
    {
        var sorted = samples.OrderBy(s => s.MvhdUtc).ThenBy(s => s.DroneStamp).ToList();
        var runs = new List<(ClockChange, string?)>();
        for (var k = 1; k < sorted.Count; k++)
        {
            var (prev, next) = (sorted[k - 1], sorted[k]);
            if (prev.Offset == next.Offset) continue;
            var at = prev.MvhdUtc + TimeSpan.FromTicks((next.MvhdUtc - prev.MvhdUtc).Ticks / 2);
            runs.Add((new ClockChange(DateTime.SpecifyKind(at, DateTimeKind.Utc), prev.Offset, next.Offset), next.SiteZoneId));
        }
        return runs;
    }

    static string Headline(ClockModel c, List<(ClockChange Change, string? LaterZone)> runs)
    {
        var n = c.Samples.Length;
        var videos = n == 1 ? "1 video" : string.Create(CultureInfo.InvariantCulture, $"{n} videos");
        return c.Mode switch
        {
            ClockMode.Zone => $"Drone clock: {ZoneNames.ClockName(c.ZoneId ?? c.SettingZoneId)} ({c.ZoneId ?? c.SettingZoneId}), "
                              + $"learned from {videos}. Folder dates use local time at each site.",
            ClockMode.SiteLocal => $"Drone clock: follows local time at each site, learned from {videos}.",
            ClockMode.NearestSample => "Drone clock: no single time zone fits these videos, so each item uses the offset of the nearest video in time."
                                       + ChangeLine(runs),
            ClockMode.Setting => c.SettingMode == StoredClockMode.SiteLocal
                ? "Drone clock: no videos on this card: using the last learned clock, local time at each site."
                : $"Drone clock: no videos on this card: using the last learned clock, {ZoneNames.ClockName(c.SettingZoneId)} ({c.SettingZoneId}).",
            _ => throw new ArgumentOutOfRangeException(nameof(c), c.Mode, "Unknown clock mode."),
        };
    }

    static string ChangeLine(List<(ClockChange Change, string? LaterZone)> runs)
    {
        if (runs.Count == 0) return "";
        var sb = new StringBuilder(" Drone clock changed during this card: ").Append(ZoneNames.FormatOffset(runs[0].Change.From));
        foreach (var (change, laterZone) in runs)
            sb.Append(" until ").Append(ZoneNames.FormatLocal(change.AtUtc, laterZone))
              .Append(", then ").Append(ZoneNames.FormatOffset(change.To));
        return sb.Append('.').ToString();
    }

    static string MismatchLine(ImmutableArray<string> zones)
    {
        if (zones.IsEmpty) return "";
        var regions = zones.Select(ZoneNames.Region).Distinct(StringComparer.Ordinal);
        return $" It doesn't match local time where this card was shot ({string.Join(", ", regions)}).";
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Clock_Scenario_*"`
Expected: test run summary Passed, 11 succeeded, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Core/Time/DroneClock.Summary.cs tests/UasSort.Core.Tests/Time/ClockScenarioTests.cs
git commit -F - <<'EOF'
feat: add clock summary, learned-mode saving and clock scenarios (Part 04)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 04.10: PlacesFormat and PlaceIndex (GeoNames extract, 0.1° grid)

**Files:**
- Create: `src/UasSort.Core/Geo/PlacesFormat.cs`
- Create: `src/UasSort.Core/Geo/PlaceIndex.cs`
- Test: `tests/UasSort.Core.Tests/Geo/PlaceIndexTests.cs`

**Interfaces:**
```csharp
// Consumes: GeoMath (04.1); Part 02: IPlaceIndex, IAppAssets { Stream OpenPlaces(); Stream OpenSelfTest(string name); },
//           PlaceClass : byte { Populated, Feature }, PlaceHit(string Name, GeoPoint Point, PlaceClass Class, string FeatureCode,
//           int Population, string TzId, Distance Away)
// Produces (defined here; the tool of Task 04.11 calls Build and Write):
namespace UasSort.Core.Geo;
public sealed record PlaceRecord(long GeonameId, string Name, double Lat, double Lon, PlaceClass Class, string? FeatureCode,
                                 long Population, string TzId);
public static class PlacesFormat {
  public static ReadOnlySpan<byte> Magic { get; }             // "UPLC"
  public const ushort Version = 1;
  public const byte PopulatedCode = 255;
  public static readonly ImmutableArray<string> FeatureCodes; // MT PK HLL VAL PASS CAPE ISL PEN PT BAY LK GLCR FJD COVE LGN INLT SD STRT HBR FLLS PRK
  public static bool TryParseGeoNamesLine(string line, bool populatedOnly, [NotNullWhen(true)] out PlaceRecord? record);
  public static ImmutableArray<PlaceRecord> Build(IEnumerable<string> usLines, IEnumerable<string> citiesLines);  // dedupe by geonameid
  public static void Write(Stream destination, IReadOnlyList<PlaceRecord> records);   // Ref §8.7 layout, then gzip
}
// Produces (Ref §4.2 PlaceIndex):
public sealed class PlaceIndex : IPlaceIndex {
  public static PlaceIndex Load(Stream gz);                   // InvalidDataException on a bad header, version or truncation
  public static Task<PlaceIndex> LoadAsync(IAppAssets assets, CancellationToken ct = default);   // background thread
  public int Count { get; }
  public IReadOnlyList<PlaceHit> Near(GeoPoint p, Distance r, PlaceClass cls, int max);   // nearest first, ties by name (ordinal)
}
// PlaceHit.FeatureCode is the GeoNames code for features and "P" for populated places.
```

File layout (Ref §8.7, little-endian, then gzip): `"UPLC"`, `u16 version = 1`, `u32 recordCount`, `u16 tzCount`, `tzCount` × (`u8 length` + UTF-8), then per record `u8 nameLength` + UTF-8 name, `i32 lat × 1e6`, `i32 lon × 1e6`, `u8 class`, `u8 featureCodeIndex` (255 for populated), `u32 population`, `u16 tzIndex`. Names longer than 255 UTF-8 bytes are cut at a character boundary.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Core.Tests/Geo/PlaceIndexTests.cs
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace UasSort.Core.Tests.Geo;

public sealed class PlaceIndexTests
{
    static string Line(long id, string name, double lat, double lon, string cls, string code, long pop, string tz)
        => string.Join('\t',
            id.ToString(CultureInfo.InvariantCulture), name, name, "",
            lat.ToString("R", CultureInfo.InvariantCulture), lon.ToString("R", CultureInfo.InvariantCulture),
            cls, code, "US", "", "AK", "180", "", "", pop.ToString(CultureInfo.InvariantCulture), "", "10", tz, "2026-09-27");

    static readonly List<string> UsLines =
    [
        Line(5861187, "Anvil Mountain", 64.5639, -165.3703, "T", "MT", 0, "America/Nome"),
        Line(5870133, "Nome", 64.5011, -165.4064, "P", "PPLA2", 3699, "America/Nome"),
        Line(5877811, "Zachar Bay", 57.5500, -153.7450, "P", "PPL", 0, "America/Anchorage"),
        Line(5877812, "Zachar Bay", 57.5600, -153.7000, "H", "BAY", 0, "America/Anchorage"),
        Line(9000001, "Dateline Cape", 51.9, 179.95, "T", "CAPE", 0, "America/Adak"),
        Line(5000001, "Nome-Council Road", 64.69, -164.27, "R", "RD", 0, "America/Nome"),     // class R: not kept
        Line(5000002, "East Fork", 64.6, -165.0, "H", "STM", 0, "America/Nome"),              // stream: not kept
        Line(5000003, "", 64.6, -165.1, "T", "MT", 0, "America/Nome"),                        // no name: not kept
        "not\ta\tgeonames\tline",
    ];

    static readonly List<string> CityLines =
    [
        Line(5870133, "Nome", 64.5011, -165.4064, "P", "PPLA2", 3699, "America/Nome"),        // duplicate id: skipped
        Line(2179537, "Wellington", -41.28664, 174.77557, "P", "PPLC", 381900, "Pacific/Auckland"),
        Line(9000002, "Cities Mountain", 10.0, 10.0, "T", "MT", 0, "Africa/Lagos"),           // cities list: populated only
    ];

    static byte[] BuildBytes(IReadOnlyList<PlaceRecord> records)
    {
        using var ms = new MemoryStream();
        PlacesFormat.Write(ms, records);
        return ms.ToArray();
    }

    static PlaceIndex Index()
    {
        using var ms = new MemoryStream(BuildBytes(PlacesFormat.Build(UsLines, CityLines)));
        return PlaceIndex.Load(ms);
    }

    static byte[] Gunzip(byte[] gz)
    {
        using var input = new MemoryStream(gz);
        using var z = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    static byte[] Gzip(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var z = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw);
        return output.ToArray();
    }

    [Fact]
    public void Places_Build_KeepsPopulatedAndListedFeatures_DedupesById()
    {
        var records = PlacesFormat.Build(UsLines, CityLines);
        string[] expected = ["Anvil Mountain", "Nome", "Zachar Bay", "Zachar Bay", "Dateline Cape", "Wellington"];
        Assert.Equal(expected, records.Select(r => r.Name).ToArray());
        Assert.Equal("MT", records[0].FeatureCode);
        Assert.Equal(PlaceClass.Populated, records[1].Class);
        Assert.Null(records[1].FeatureCode);
        Assert.Equal(3699, records[1].Population);
    }

    [Fact]
    public void Places_Write_HeaderIsUplcVersion1()
    {
        var raw = Gunzip(BuildBytes(PlacesFormat.Build(UsLines, CityLines)));
        Assert.Equal("UPLC"u8.ToArray(), raw[..4]);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(4)));
        Assert.Equal(6u, BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(6)));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(10)));   // Nome, Anchorage, Adak, Auckland
    }

    [Fact]
    public void Places_RoundTrip_NearestFeatureAndPopulatedPlace()
    {
        var index = Index();
        Assert.Equal(6, index.Count);
        var clip = new GeoPoint(64.56267, -165.36964);
        var feature = Assert.Single(index.Near(clip, Distance.FromMiles(1.5), PlaceClass.Feature, 6));
        Assert.Equal("Anvil Mountain", feature.Name);
        Assert.Equal("MT", feature.FeatureCode);
        Assert.Equal("America/Nome", feature.TzId);
        Assert.Equal(new GeoPoint(64.5639, -165.3703), feature.Point);
        Assert.Equal(GeoMath.Haversine(clip, feature.Point).Meters, feature.Away.Meters, 1e-6);

        var town = Assert.Single(index.Near(clip, Distance.FromMiles(30), PlaceClass.Populated, 6));
        Assert.Equal("Nome", town.Name);
        Assert.Equal("P", town.FeatureCode);
        Assert.Equal(3699, town.Population);
    }

    [Fact]
    public void Places_Near_RespectsRadiusClassAndMaxAndSortsNearestFirst()
    {
        var index = Index();
        var zachar = new GeoPoint(57.5504, -153.7390);
        Assert.Empty(index.Near(zachar, Distance.FromMiles(0.01), PlaceClass.Feature, 6));
        Assert.Empty(index.Near(zachar, Distance.FromMiles(5), PlaceClass.Feature, 0));
        var both = index.Near(zachar, Distance.FromMiles(5), PlaceClass.Populated, 6).Concat(index.Near(zachar, Distance.FromMiles(5), PlaceClass.Feature, 6)).ToList();
        Assert.Equal(2, both.Count);
        Assert.All(both, h => Assert.Equal("Zachar Bay", h.Name));
        var one = index.Near(new GeoPoint(64.55, -165.38), Distance.FromMiles(30), PlaceClass.Feature, 1);
        Assert.Equal("Anvil Mountain", Assert.Single(one).Name);
    }

    [Fact]
    public void Places_Near_CrossesAntimeridian()
    {
        var hit = Assert.Single(Index().Near(new GeoPoint(51.9, -179.95), Distance.FromMiles(5), PlaceClass.Feature, 6));
        Assert.Equal("Dateline Cape", hit.Name);
        Assert.Equal(4.263, hit.Away.Miles, 0.01);
    }

    [Fact]
    public void Places_Near_WorksInTheSouthernHemisphere()
        => Assert.Equal("Wellington", Assert.Single(Index().Near(new GeoPoint(-41.29, 174.78), Distance.FromMiles(3), PlaceClass.Populated, 6)).Name);

    [Fact]
    public void Places_LongName_IsCutAtAUtf8Boundary()
    {
        var name = new string('é', 200);   // 400 UTF-8 bytes
        List<PlaceRecord> records = [new PlaceRecord(1, name, 10, 10, PlaceClass.Populated, null, 1, "UTC")];
        using var ms = new MemoryStream(BuildBytes(records));
        var hit = Assert.Single(PlaceIndex.Load(ms).Near(new GeoPoint(10, 10), Distance.FromMiles(1), PlaceClass.Populated, 1));
        Assert.Equal(new string('é', 127), hit.Name);
    }

    [Fact]
    public void Places_Load_RejectsBadMagic()
    {
        using var ms = new MemoryStream(Gzip(Encoding.ASCII.GetBytes("NOPE\u0001\u0000")));
        Assert.Throws<InvalidDataException>(() => PlaceIndex.Load(ms));
    }

    [Fact]
    public void Places_Load_RejectsTruncatedFile()
    {
        var raw = Gunzip(BuildBytes(PlacesFormat.Build(UsLines, CityLines)));
        using var ms = new MemoryStream(Gzip(raw[..^3]));
        Assert.Throws<InvalidDataException>(() => PlaceIndex.Load(ms));
    }

    sealed class BytesAssets(byte[] places) : IAppAssets
    {
        public Stream OpenPlaces() => new MemoryStream(places, writable: false);
        public Stream OpenSelfTest(string name) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Places_LoadAsync_ReadsThroughAppAssets()
    {
        var index = await PlaceIndex.LoadAsync(new BytesAssets(BuildBytes(PlacesFormat.Build(UsLines, CityLines))), TestContext.Current.CancellationToken);
        Assert.Equal(6, index.Count);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Places_*"`
Expected: build FAILS with `error CS0246: The type or namespace name 'PlaceRecord' could not be found` (and `PlacesFormat`, `PlaceIndex`).

- [ ] **Step 3: Write `PlacesFormat`**

```csharp
// src/UasSort.Core/Geo/PlacesFormat.cs
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace UasSort.Core.Geo;

public sealed record PlaceRecord(long GeonameId, string Name, double Lat, double Lon, PlaceClass Class, string? FeatureCode,
                                 long Population, string TzId);

/// <summary>The places.bin.gz format (Ref §8.7) and the GeoNames dump filter used by tools/places/build-places.cs.</summary>
public static class PlacesFormat
{
    public static ReadOnlySpan<byte> Magic => "UPLC"u8;
    public const ushort Version = 1;
    public const byte PopulatedCode = 255;

    public static readonly ImmutableArray<string> FeatureCodes =
    [
        "MT", "PK", "HLL", "VAL", "PASS", "CAPE", "ISL", "PEN", "PT", "BAY", "LK", "GLCR", "FJD", "COVE", "LGN", "INLT", "SD",
        "STRT", "HBR", "FLLS", "PRK",
    ];

    /// <summary>One line of a GeoNames "geoname" table dump (tab-separated, 19 columns).</summary>
    public static bool TryParseGeoNamesLine(string line, bool populatedOnly, [NotNullWhen(true)] out PlaceRecord? record)
    {
        ArgumentNullException.ThrowIfNull(line);
        record = null;
        var c = line.Split('\t');
        if (c.Length < 18 || c[1].Length == 0 || c[17].Length == 0) return false;
        if (!long.TryParse(c[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return false;
        if (!double.TryParse(c[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(c[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return false;

        PlaceClass cls;
        string? code;
        if (c[6] == "P") { cls = PlaceClass.Populated; code = null; }
        else if (!populatedOnly && FeatureCodes.Contains(c[7])) { cls = PlaceClass.Feature; code = c[7]; }
        else return false;

        _ = long.TryParse(c[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out var population);
        record = new PlaceRecord(id, c[1], lat, lon, cls, code, Math.Max(0, population), c[17]);
        return true;
    }

    /// <summary>US.txt (populated places and the listed features) then cities5000.txt (populated only), deduplicated by geonameid.</summary>
    public static ImmutableArray<PlaceRecord> Build(IEnumerable<string> usLines, IEnumerable<string> citiesLines)
    {
        ArgumentNullException.ThrowIfNull(usLines);
        ArgumentNullException.ThrowIfNull(citiesLines);
        var seen = new HashSet<long>();
        var result = ImmutableArray.CreateBuilder<PlaceRecord>();
        foreach (var line in usLines)
            if (TryParseGeoNamesLine(line, populatedOnly: false, out var r) && seen.Add(r.GeonameId)) result.Add(r);
        foreach (var line in citiesLines)
            if (TryParseGeoNamesLine(line, populatedOnly: true, out var r) && seen.Add(r.GeonameId)) result.Add(r);
        return result.ToImmutable();
    }

    public static void Write(Stream destination, IReadOnlyList<PlaceRecord> records)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(records);
        var zones = new List<string>();
        var zoneIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in records)
            if (zoneIndex.TryAdd(r.TzId, zones.Count)) zones.Add(r.TzId);
        if (zones.Count > ushort.MaxValue) throw new InvalidOperationException("Too many time zones for a u16 index.");

        using var gz = new GZipStream(destination, CompressionLevel.SmallestSize, leaveOpen: true);
        using var w = new BinaryWriter(gz, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write((uint)records.Count);
        w.Write((ushort)zones.Count);
        foreach (var zone in zones) WriteShortString(w, zone);
        foreach (var r in records)
        {
            WriteShortString(w, r.Name);
            w.Write((int)Math.Round(r.Lat * 1e6));
            w.Write((int)Math.Round(r.Lon * 1e6));
            w.Write((byte)r.Class);
            w.Write(r.Class == PlaceClass.Populated ? PopulatedCode : FeatureIndex(r.FeatureCode));
            w.Write((uint)Math.Clamp(r.Population, 0L, uint.MaxValue));
            w.Write((ushort)zoneIndex[r.TzId]);
        }
    }

    static byte FeatureIndex(string? code)
    {
        var i = code is null ? -1 : FeatureCodes.IndexOf(code);
        return i >= 0 ? (byte)i : throw new InvalidOperationException($"Feature code '{code}' is not in the kept list.");
    }

    static void WriteShortString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var length = Math.Min(bytes.Length, 255);
        while (length > 0 && length < bytes.Length && (bytes[length] & 0xC0) == 0x80) length--;   // never split a UTF-8 sequence
        w.Write((byte)length);
        w.Write(bytes, 0, length);
    }
}
```

- [ ] **Step 4: Write `PlaceIndex`**

```csharp
// src/UasSort.Core/Geo/PlaceIndex.cs
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace UasSort.Core.Geo;

/// <summary>The offline GeoNames extract with a 0.1° grid built at load time (Ref §4.2, §8.7).</summary>
public sealed class PlaceIndex : IPlaceIndex
{
    const int LonCells = 3600;
    const int MinRecordBytes = 17;   // u8 + i32 + i32 + u8 + u8 + u32 + u16

    readonly int[] _lat, _lon;
    readonly byte[] _class, _code;
    readonly uint[] _population;
    readonly ushort[] _tz;
    readonly int[] _nameStart;
    readonly byte[] _names;
    readonly string[] _zones;
    readonly int[] _order;
    readonly Dictionary<int, (int Start, int Count)> _cells = [];

    PlaceIndex(int[] lat, int[] lon, byte[] cls, byte[] code, uint[] population, ushort[] tz, int[] nameStart, byte[] names, string[] zones)
    {
        (_lat, _lon, _class, _code, _population, _tz, _nameStart, _names, _zones) = (lat, lon, cls, code, population, tz, nameStart, names, zones);
        var keys = new int[lat.Length];
        _order = new int[lat.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = Key(LatCell(lat[i] / 1e6), LonCell(lon[i] / 1e6));
            _order[i] = i;
        }
        Array.Sort(keys, _order);
        for (var k = 0; k < keys.Length;)
        {
            var start = k;
            while (k < keys.Length && keys[k] == keys[start]) k++;
            _cells[keys[start]] = (start, k - start);
        }
    }

    public int Count => _lat.Length;

    public static PlaceIndex Load(Stream gz)
    {
        ArgumentNullException.ThrowIfNull(gz);
        using var raw = new MemoryStream();
        using (var z = new GZipStream(gz, CompressionMode.Decompress, leaveOpen: true))
            z.CopyTo(raw);   // a corrupt gzip stream throws InvalidDataException itself
        return Parse(raw.GetBuffer().AsSpan(0, checked((int)raw.Length)));
    }

    public static Task<PlaceIndex> LoadAsync(IAppAssets assets, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        return Task.Run(() =>
        {
            using var s = assets.OpenPlaces();
            return Load(s);
        }, ct);
    }

    public IReadOnlyList<PlaceHit> Near(GeoPoint p, Distance r, PlaceClass cls, int max)
    {
        if (max <= 0 || Count == 0) return [];
        var dLat = r.Meters / GeoMath.MetersPerDegree;
        var latLo = Math.Clamp((int)Math.Floor((p.Lat - dLat) * 10), -900, 899);
        var latHi = Math.Clamp((int)Math.Floor((p.Lat + dLat) * 10), -900, 899);
        var edge = Math.Min(89.9, Math.Abs(p.Lat) + dLat);
        var dLon = dLat / Math.Cos(edge * Math.PI / 180);
        int lonLo, lonHi;
        if (dLon >= 180) { lonLo = -1800; lonHi = 1799; }
        else { lonLo = (int)Math.Floor((p.Lon - dLon) * 10); lonHi = (int)Math.Floor((p.Lon + dLon) * 10); }

        var visited = new HashSet<int>();
        var hits = new List<(double Meters, int Index)>();
        for (var la = latLo; la <= latHi; la++)
            for (var lo = lonLo; lo <= lonHi; lo++)
            {
                var key = Key(la, Wrap(lo));
                if (!visited.Add(key) || !_cells.TryGetValue(key, out var cell)) continue;
                for (var k = cell.Start; k < cell.Start + cell.Count; k++)
                {
                    var i = _order[k];
                    if (_class[i] != (byte)cls) continue;
                    var meters = GeoMath.Haversine(p, PointOf(i)).Meters;
                    if (meters <= r.Meters) hits.Add((meters, i));
                }
            }
        hits.Sort((a, b) => a.Meters != b.Meters ? a.Meters.CompareTo(b.Meters) : string.CompareOrdinal(NameOf(a.Index), NameOf(b.Index)));
        return hits.Take(max).Select(h => Hit(h.Index, h.Meters)).ToList();
    }

    PlaceHit Hit(int i, double meters) => new(
        NameOf(i), PointOf(i), (PlaceClass)_class[i],
        _code[i] == PlacesFormat.PopulatedCode ? "P" : PlacesFormat.FeatureCodes[_code[i]],
        (int)Math.Min(_population[i], int.MaxValue), _zones[_tz[i]], new Distance(meters));

    GeoPoint PointOf(int i) => new(_lat[i] / 1e6, _lon[i] / 1e6);
    string NameOf(int i) => Encoding.UTF8.GetString(_names, _nameStart[i], _nameStart[i + 1] - _nameStart[i]);

    static int LatCell(double lat) => Math.Clamp((int)Math.Floor(lat * 10), -900, 899);
    static int LonCell(double lon) => Wrap((int)Math.Floor(lon * 10));
    static int Wrap(int lonCell) => ((lonCell + 1800) % LonCells + LonCells) % LonCells - 1800;
    static int Key(int latCell, int lonCell) => (latCell + 900) * LonCells + (lonCell + 1800);

    static PlaceIndex Parse(ReadOnlySpan<byte> data)
    {
        var c = new Cursor(data);
        if (!c.Take(4).SequenceEqual(PlacesFormat.Magic)) throw new InvalidDataException("places.bin: bad magic.");
        if (c.U16() != PlacesFormat.Version) throw new InvalidDataException("places.bin: unsupported version.");
        var count = c.U32();
        if (count > (uint)(data.Length / MinRecordBytes)) throw new InvalidDataException("places.bin: record count exceeds the data.");
        var n = (int)count;
        var zones = new string[c.U16()];
        for (var z = 0; z < zones.Length; z++) zones[z] = Encoding.UTF8.GetString(c.Take(c.U8()));

        var lat = new int[n];
        var lon = new int[n];
        var cls = new byte[n];
        var code = new byte[n];
        var population = new uint[n];
        var tz = new ushort[n];
        var nameStart = new int[n + 1];
        using var names = new MemoryStream();
        for (var i = 0; i < n; i++)
        {
            nameStart[i] = (int)names.Length;
            names.Write(c.Take(c.U8()));
            lat[i] = c.I32();
            lon[i] = c.I32();
            cls[i] = c.U8();
            code[i] = c.U8();
            population[i] = c.U32();
            tz[i] = c.U16();
            var codeOk = cls[i] == (byte)PlaceClass.Populated ? code[i] == PlacesFormat.PopulatedCode : code[i] < PlacesFormat.FeatureCodes.Length;
            if (cls[i] > (byte)PlaceClass.Feature || !codeOk || tz[i] >= zones.Length)
                throw new InvalidDataException("places.bin: invalid record.");
        }
        nameStart[n] = (int)names.Length;
        if (!c.AtEnd) throw new InvalidDataException("places.bin: trailing bytes.");
        return new PlaceIndex(lat, lon, cls, code, population, tz, nameStart, names.ToArray(), zones);
    }

    ref struct Cursor
    {
        readonly ReadOnlySpan<byte> _data;
        int _pos;

        public Cursor(ReadOnlySpan<byte> data) => _data = data;

        public readonly bool AtEnd => _pos == _data.Length;

        public ReadOnlySpan<byte> Take(int length)
        {
            if (length < 0 || _pos + length > _data.Length) throw new InvalidDataException("places.bin: truncated.");
            var s = _data.Slice(_pos, length);
            _pos += length;
            return s;
        }

        public byte U8() => Take(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*Places_*"`
Expected: test run summary Passed, 10 succeeded, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/UasSort.Core/Geo/PlacesFormat.cs src/UasSort.Core/Geo/PlaceIndex.cs tests/UasSort.Core.Tests/Geo/PlaceIndexTests.cs
git commit -F - <<'EOF'
feat: add places.bin format and PlaceIndex with 0.1 degree grid (Part 04)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

---

### Task 04.11: build-places tool, the real places.bin.gz, and the golden round trip

**Files:**
- Create: `tools/places/build-places.cs`
- Create (generated, checked in): `src/UasSort.App/places.bin.gz`
- Modify: `tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj` (embed `places.bin.gz`)
- Test: `tests/UasSort.Core.Tests/Geo/PlaceIndexGoldenTests.cs`

**Interfaces:**
```csharp
// Consumes: PlacesFormat.Build/Write, PlaceIndex.Load (04.10)
// Produces:
//   tools/places/build-places.cs — .NET file-based app (Ref §2.3, §8.7):
//     dotnet run --file tools/places/build-places.cs -- --us <US.zip> --cities <cities5000.zip> --out <places.bin.gz>
//     exit 0 on success, 2 on bad arguments; writes <out>.tmp then moves it over <out>.
//   src/UasSort.App/places.bin.gz — the extract the App copies to its output (Content, PreserveNewest; wired in Part 11 Task 11.4)
//     and opens through IAppAssets.OpenPlaces(); the CLI (Part 12) copies it beside uas-sort-cli.exe.
```

- [ ] **Step 1: Write the failing golden test and embed the (not yet existing) extract**

Add to `tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj`:

```xml
  <ItemGroup>
    <!-- the checked-in GeoNames extract written by tools/places/build-places.cs (Ref §8.7 round-trip test) -->
    <EmbeddedResource Include="..\..\src\UasSort.App\places.bin.gz" LogicalName="places.bin.gz" />
  </ItemGroup>
```

```csharp
// tests/UasSort.Core.Tests/Geo/PlaceIndexGoldenTests.cs
namespace UasSort.Core.Tests.Geo;

/// <summary>build-places → PlaceIndex.Load on the real extract (Ref §8.7, §14 step 4): the names the Python spike found.</summary>
public sealed class PlaceIndexGoldenTests
{
    static readonly Lazy<PlaceIndex> Index = new(() =>
    {
        using var s = typeof(PlaceIndexGoldenTests).Assembly.GetManifestResourceStream("places.bin.gz")
                      ?? throw new InvalidOperationException("places.bin.gz is not embedded");
        return PlaceIndex.Load(s);
    });

    // first GPS fixes of the real clips (docs/research/spikes/djmd/calibration.json), rounded to 6 decimals
    static readonly ImmutableArray<GeoPoint> AnvilClips =
    [
        new(64.562676, -165.369638), new(64.563839, -165.368771), new(64.564500, -165.370056), new(64.564337, -165.369335),
        new(64.564249, -165.370239), new(64.563745, -165.369407), new(64.562642, -165.373221), new(64.562640, -165.373229),
        new(64.562636, -165.373232), new(64.563534, -165.371245), new(64.562896, -165.374300), new(64.563509, -165.371165),
        new(64.562750, -165.374623), new(64.562744, -165.372460), new(64.562304, -165.373683), new(64.555694, -165.360136),
        new(64.555346, -165.361350), new(64.553791, -165.370900), new(64.554418, -165.375791), new(64.560376, -165.373349),
        new(64.560349, -165.373351),
    ];

    static readonly ImmutableArray<GeoPoint> ZacharClips =
    [
        new(57.536828, -153.748385), new(57.536964, -153.747251), new(57.544347, -153.740997), new(57.546764, -153.739543),
        new(57.550442, -153.738973), new(57.549947, -153.739812), new(57.542624, -153.743735), new(57.542751, -153.743272),
        new(57.544364, -153.744304), new(57.535034, -153.736347), new(57.538145, -153.728609), new(57.536230, -153.732694),
        new(57.534836, -153.748082),
    ];

    /// <summary>Over all clips: the smallest distance at which the nearest place (of the given classes) is the named one.</summary>
    static double NearestNamedMiles(ImmutableArray<GeoPoint> clips, string name, params PlaceClass[] classes)
    {
        var best = double.MaxValue;
        foreach (var p in clips)
        {
            var nearest = classes.SelectMany(c => Index.Value.Near(p, Distance.FromMiles(5), c, 1))
                                 .OrderBy(h => h.Away.Meters)
                                 .FirstOrDefault();
            if (nearest is not null && nearest.Name == name) best = Math.Min(best, nearest.Away.Miles);
        }
        return best;
    }

    [Fact]
    public void PlacesGolden_Extract_HasTheExpectedShape()
    {
        Assert.InRange(Index.Value.Count, 400_000, 900_000);
        var nome = Index.Value.Near(new GeoPoint(64.5011, -165.4064), Distance.FromMiles(3), PlaceClass.Populated, 1);
        Assert.Equal("Nome", Assert.Single(nome).Name);
        Assert.Equal("America/Nome", nome[0].TzId);
    }

    [Fact]
    public void PlacesGolden_AnvilMountain_IsNearestFeature_Within0_2Mi()
        => Assert.InRange(NearestNamedMiles(AnvilClips, "Anvil Mountain", PlaceClass.Feature), 0.0, 0.2);

    [Fact]
    public void PlacesGolden_ZacharBay_IsNearestPlace_Within0_4Mi()
        => Assert.InRange(NearestNamedMiles(ZacharClips, "Zachar Bay", PlaceClass.Feature, PlaceClass.Populated), 0.0, 0.4);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*PlacesGolden_*"`
Expected: build FAILS because `src/UasSort.App/places.bin.gz` doesn't exist yet (`error CS1566: Error reading resource 'places.bin.gz'` or `MSB3552`, depending on the SDK).

- [ ] **Step 3: Write the tool**

```csharp
#:project ../../src/UasSort.Core/UasSort.Core.csproj
#pragma warning disable RS0030 // tool: reads the GeoNames dump and writes places.bin.gz; never shipped, never run by the app

using System.Globalization;
using System.IO.Compression;
using UasSort.Core;       // a file-based app gets no GlobalUsings.Core.cs, so the Core namespaces it uses are imported here
using UasSort.Core.Geo;

// Builds src/UasSort.App/places.bin.gz (Ref §8.7) from GeoNames US.zip (populated places + the listed feature codes)
// and cities5000.zip (populated places worldwide). GeoNames data: CC-BY 4.0 (credited in About).
if (args.Length != 6 || args[0] != "--us" || args[2] != "--cities" || args[4] != "--out")
{
    Console.Error.WriteLine("usage: dotnet run --file tools/places/build-places.cs -- --us <US.zip> --cities <cities5000.zip> --out <places.bin.gz>");
    return 2;
}

var records = PlacesFormat.Build(ReadLines(args[1], "US.txt"), ReadLines(args[3], "cities5000.txt"));
var populated = records.Count(r => r.Class == PlaceClass.Populated);
var zones = records.Select(r => r.TzId).Distinct(StringComparer.Ordinal).Count();
var tmp = args[5] + ".tmp";
using (var output = File.Create(tmp))
    PlacesFormat.Write(output, records);
File.Move(tmp, args[5], overwrite: true);
var size = new FileInfo(args[5]).Length;
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"places: {records.Length:N0} records ({populated:N0} populated, {records.Length - populated:N0} features), {zones} zones -> {args[5]} ({size / 1e6:0.0} MB)"));
return 0;

static IEnumerable<string> ReadLines(string zipPath, string entryName)
{
    using var zip = ZipFile.OpenRead(zipPath);
    var entry = zip.GetEntry(entryName) ?? throw new InvalidDataException($"{zipPath} has no {entryName}");
    using var reader = new StreamReader(entry.Open());
    while (reader.ReadLine() is { } line)
        yield return line;
}
```

- [ ] **Step 4: Download the dump and build the extract**

Run (pwsh, repo root):

```powershell
$g = Join-Path $env:TEMP 'uas-sort-geonames'
New-Item -ItemType Directory -Force $g | Out-Null
curl.exe -fL -o "$g\US.zip" https://download.geonames.org/export/dump/US.zip
curl.exe -fL -o "$g\cities5000.zip" https://download.geonames.org/export/dump/cities5000.zip
dotnet run --file tools/places/build-places.cs -- --us "$g\US.zip" --cities "$g\cities5000.zip" --out src/UasSort.App/places.bin.gz
Remove-Item -Recurse -Force $g
```

Expected: a line like `places: 6xx,xxx records (… populated, … features), 4xx zones -> src/UasSort.App/places.bin.gz (7.x MB)`; exit code 0; the file is about 7–8 MB (Ref §8.7: "about 7.7 MB gzipped"). Record the download date in the commit message (the dump of 2026-09-27 named in the spec can no longer be fetched once GeoNames publishes a newer one).

- [ ] **Step 5: Run the golden test to verify it passes**

Run: `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-method "*PlacesGolden_*"`
Expected: test run summary Passed, 3 succeeded, 0 failed. If `PlacesGolden_ZacharBay_…` or `PlacesGolden_AnvilMountain_…` fails **only** on the distance threshold (the right name found, but farther than 0.4 mi / 0.2 mi), stop and raise it with the user; do not change the threshold (see the gap list in the Produces summary).

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test --solution uas-sort.slnx`
Expected: test run summary Passed for every test project, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add tools/places/build-places.cs src/UasSort.App/places.bin.gz tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj tests/UasSort.Core.Tests/Geo/PlaceIndexGoldenTests.cs
git commit -F - <<'EOF'
feat: add build-places tool and GeoNames places.bin.gz (Part 04)

GeoNames dump downloaded on <yyyy-MM-dd of the download> (US.zip + cities5000.zip, CC-BY 4.0).

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z
EOF
```

(Replace the angle-bracket text with the actual download date before committing.)

---

## Part 04 — Produces (summary)

**Core — `UasSort.Core.Geo`**
- `GeoMath` — `EarthRadiusMeters` (6,371,008.8), `MetersPerDegree`, `Haversine(a, b)` → `Distance`, `Median(points)` → `GeoPoint`. Parts 06 (Clusterer, FolderDecider, DaySplitFinder, DescriptionSuggester), 08 and 10 use `Haversine`.
- `GeoTimeZoneResolver : ITimeZoneResolver` — `Resolve(GeoPoint)`, `static IsEtc(string)`.
- `GpsPlausibility` — `Check(first, last, zone, cardPoints)` → `GpsProbe`; `MaxFirstToLast` 3 mi, `MaxFromCard` 500 mi.
- `PlaceRecord`, `PlacesFormat` (`Magic`, `Version`, `PopulatedCode`, `FeatureCodes`, `TryParseGeoNamesLine`, `Build`, `Write`).
- `PlaceIndex : IPlaceIndex` — `static Load(Stream gz)`, `static LoadAsync(IAppAssets, CancellationToken)`, `Count`, `Near(GeoPoint, Distance, PlaceClass, int max)` (nearest first; `FeatureCode` "P" for populated).

**Core — `UasSort.Core.Time`**
- `Zones` — `TryFind`, `Find`, `IanaId(TimeZoneInfo)`.
- `ZoneNames` — `Region`, `ClockName`, `Abbreviation(ianaId, utc)`, `FormatOffset` ("UTC−4"), `FormatLocal(utc, ianaId)` ("Sep 27 10:55 AKDT"), `TimeSpan OffsetAt(string ianaId, DateTime utc)` (UTC for unknown ids), `bool IsUs(string ianaId)` (the seven `DroneClock.UsZones`). For the §9.2 InfoBar, §9.13 times and the cleanup cutoff text (Parts 06, 08 and 10).
- `ClockConversion` — `ToUtc`, `OffsetAt`, `SourceFor`, `Nearest`, `SampleWindow` (60 days). `ClockModel.ToUtc`/`OffsetAt` are added to Part 02's `partial` record by `src/UasSort.Core/Time/ClockModel.Conversion.cs` (namespace `UasSort.Core`) and delegate here. Library member starts and the watermark (Part 05 `LibraryIndex`) convert with `clock.ToUtc(stamp, ledgerFolder.TzId)`: in SiteLocal mode a null zone falls back to the stored zone.
- `DroneClock` (static partial class: `DroneClock.cs` + `DroneClock.Summary.cs`) — `UsZones`, `Learn(items, settingMode, settingZoneId, tz, pc)`, `Samples`, `Round15` (Review uses it instead of a rounding helper of its own), `Summarize(clock, resolvedItems) → ClockSummary` (for `PlanBase.Clock`, Part 06), `Changes(samples)`, `ApplyLearned(Settings, ClockModel) → Settings` (Part 07 saves it after a successful run).
- `TimeResolver` — `Resolve(raw, clock, tz, places, pc, nowUtc)` → `ResolvedItem`s in input order, `NearbyWindow` 12 h, `GeoNamesTzRadius` 60 mi.
- `TimeFlags` — `IsClockMismatch` (≥ 15 min), `MinutesFromMidnight`, `IsClockSource`, `For(...)`, the window constants.

**Testing — `UasSort.Testing`:** `FixturePoints`, `RawItemBuilder` (`Vid`, `Dng`, `Other`, `Stamp`, `Utc`, `Eastern`, `Serial`), `FakeTimeZoneResolver(ITimeZoneResolver fallback)` (`With(GeoPoint, TzLookup)`), `FakePlaceIndex(params IReadOnlyList<PlaceHit> places) : IPlaceIndex`. Part 06's scenario builder can build its `RawItem`s with these, and its `Sites` fields delegate to `FixturePoints`.

Parts 06 and 10 use `FakePlaceIndex`, `ZoneNames`, `GeoMath` from here; they do not define their own.

**Tools and data:** `tools/places/build-places.cs` (file-based app referencing Core; `dotnet run --file tools/places/build-places.cs -- --us <US.zip> --cities <cities5000.zip> --out <places.bin.gz>`), `src/UasSort.App/places.bin.gz` (checked in; Part 11 Task 11.4 adds it as App `Content`/`PreserveNewest` and the About credit "GeoNames CC-BY 4.0"; the CLI copies it beside `uas-sort-cli.exe`; the composition root starts `PlaceIndex.LoadAsync(assets)` in the background and passes the index once loaded).

**Tests:** GeoMath (7), GeoTz (10), GpsGate (7), Zone names (29), ClockModel (7), Learn (14), Resolve (16), Flags (10), Clock scenarios (11, incl. **[Review Focus] 4** DST end), Places (10), PlacesGolden (3).

**Spec gaps and contradictions found (listed, not silently resolved):**
1. **"Zachar Bay" ≤ 0.4 mi.** Research §5 measured the Zachar Bay village 0.7 km (0.43 mi) and the bay 1.7 km from the folder's *first* GPS fix, which is over 0.4 mi. The golden test takes the closest of all 13 Zachar clip points and accepts either class; if it still misses 0.4 mi, Task 04.11 stops and asks the user rather than loosening the threshold. The Anvil check (0.3 km = 0.19 mi) is consistent.
2. **The dump of 2026-09-27 can't be re-downloaded** after GeoNames publishes a newer one. The executor uses the current dump and records its date in the commit.
3. **The spec's "build-places → PlaceIndex.Load" round trip can't run inside `dotnet test`**, because the tool needs a 68 MB download. It is split into a synthetic writer/reader round trip (04.10) and a golden test over the checked-in extract (04.11).
4. Model and ports are in `UasSort.Core` (Part 02); resolved.
5. **`LastSameField` missing on a generic hit** (§6.3 step 5b says nothing): treated as implausible (fail safe). The "card's other GPS items" for the 500 mi median are the trusted (non-generic) fixes only.
6. **DST edge stamps:** a repeated-hour stamp takes the nearest video sample whose offset is one of the two valid ones (Review Focus 4), else standard time. A skipped-hour stamp takes the standard offset. The spec defines neither in general.
7. **SiteLocal conversion zone vs site zone for no-GPS items.** §6.1 converts by the nearest GPS item by drone stamp; §6.4 assigns the zone by session first. These can differ, which could raise `ClockMismatch` in SiteLocal mode. §6.5 only says "never … for an item converted through its own site zone".
8. **`CheckDate` (a)** is applied only when the zone came from the item's own GPS lookup (`TzSource.Gps`). The spec doesn't say which lookup's alternatives count for fallback zones.
9. **Headline wording** for NearestSample and Setting, and for several clock changes, is not given verbatim in the spec. The texts are in Task 04.9 and pinned by tests.
10. **Capture rule 1 says "DJI video with moov".** It is implemented as any video with `moov` + `mvhd`, since classification only admits `DCIM\DJI_*` videos. Unrecognised locations are Unknown and never harvested.
11. **Unspecified tie-breaks:** `Modal` (most frequent, then earliest by `mvhdUtc`) and `PlaceHit.FeatureCode` for populated records ("P"; the file format drops the code).
12. **Names not in the Ref:** `ClockSummary` construction (`DroneClock.Summarize`), learned-mode saving (`ApplyLearned`), and the helper classes (`GeoMath`, `Zones`, `ZoneNames`, `ClockConversion`, `TimeFlags`, `PlacesFormat`, `PlaceRecord`) are defined here. Later parts should use them rather than re-derive them.
13. **The "default `droneClockZone`" variant of §13 test #6** depends on Settings defaults (Part 05), so it belongs in Part 06's port. At this level the stored zone is just the string passed to `Learn`.
