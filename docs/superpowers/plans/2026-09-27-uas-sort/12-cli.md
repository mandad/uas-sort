# Part 12 — CLI

**Goal:** build `uas-sort-cli plan`, the dry-run planner: it validates a card path with the same `CardSourceValidator` as the app, scans it with `ScanService` (ledger read through `Check()` + `Load()` only), runs `Planner.Prepare` + `Derive` with no edits, and prints the plan as text or as the versioned JSON document, optionally comparing it with the user's expected folder list (`--expect`) and printing the edit count. It **writes nothing, ever** (no settings, drafts, ledger, reports, logs or any file; logs and progress go to stderr), has no cleanup command, and exits 0 / 1 / 2. This part also checks in the acceptance template `tests/acceptance/first-card-expected.*` that the user fills in before the first-card acceptance.

**Ref sections:** Ref §4.5 in full (the CLI contract), §4.1 (`ISettingsStore.Load(readOnly)`, `ICardSourceValidator`, `ICardReaderFactory`, `IVolumeProvider`), §4.2 (Cli row, ScanService, Planner), §4.3 (card source validation), §3 (model: `Plan`, `PlanBase`, `ScanResult`, `VideoGroup`, `GroupTarget`, `Newness`, `Issue`, `ClockSummary`, `CardSourceCheck`), §11 (settings read-only load, derived defaults), §13 (CLI test; acceptance step 2), §14 step 12. Main spec §1 success criterion 3, §3.2, §10.

**Depends on:** Parts 01–11 — Part 01 (solution, `UasSort.Cli` in `uas-sort.slnx`, Directory props, BannedSymbols wiring, `PlaceholderMode`), Part 02 (model, ports, `CardSourceValidator`, `IoGuardPolicy`, `GlobalUsings.Core.cs` in every project including `src/UasSort.Cli`), Part 04 (`GeoTimeZoneResolver`, `PlaceIndex.Load`), Part 05 (settings defaults and load policy, `LedgerSnapshot`), Part 06 (`ScanService`, `Planner`), Part 09 (Windows implementations: lister, path facts, volume provider, card reader factory, `LedgerStore`, settings store, `AppAssets`), and `tests/UasSort.Testing` (`RepoPaths`, `TestTempDir` from Part 01; `SyntheticMp4Builder` from Part 03). `FakeCardWriter` is defined here (Task 12.6); this part is its owner in `00-interfaces.md`.

**Registry.** `docs/superpowers/plans/2026-09-27-uas-sort/00-interfaces.md` wins over this part's text on names, namespaces, signatures and ownership. Symbols this part owns there: `CliArgs`, the `PlanDocument` family and `CliJsonContext`, `PlanTextRenderer` (not `PlanText`, which would be ambiguous with `UasSort.Core.Planning.PlanText` in Platform.Tests), the expect types, `ICliHost`, `PlanCommand`, `PlanDocumentMapper`, `StderrProgress`, `WindowsCliHost`, `Program.Main` (after Part 01's stub), `UasSort.Platform.Io.ReadOnlyTextFile` and `UasSort.Testing.FakeCardWriter`.

**Where the tests live.** The CLI targets `net11.0-windows10.0.26100.0`, so its tests live in `tests/UasSort.Platform.Tests/Cli/` (Windows-only tests live in Platform.Tests, index Global Constraints). Commands:

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*CliArgsTests*"
dotnet test --solution uas-sort.slnx
```

**Constructors and namespaces consumed from earlier parts.** Ref §4 fixes the ports and the static/instance members this part calls, but not the constructors of concrete classes. This part constructs Core and Platform services in exactly one class, `WindowsCliHost` (Task 12.6), and builds its test card in exactly one helper, `CliTestCard` (Task 12.6). The signatures below are the ones Parts 01–09 define and the registry lists; if they ever disagree, the registry wins and only `WindowsCliHost`, `CliTestCard` or `Program` changes. Every Platform constructor that takes a `machine` gets `Environment.MachineName`, passed explicitly by the composition root (`WindowsCliHost`, registry decision 32).

| Member used here | Owner (namespace) |
|---|---|
| `static sbyte PlaceholderMode.ExposePlaceholders()` (returns the previous mode; negative = `PHCM_ERROR_*`), called first thing in `Program.Main` | Part 01 (`UasSort.Platform.Win32`) |
| `new CardSourceValidator()` : `ICardSourceValidator`; its messages `CardSourceValidator.NoDcim` = "No DCIM folder here", `PartOfLibrary` = "This is part of your library (or a synced folder); uas-sort only offloads from cards." | Part 02 (`UasSort.Core.Card`) |
| `new GeoTimeZoneResolver()` : `ITimeZoneResolver`; `static PlaceIndex PlaceIndex.Load(Stream gz)` | Part 04 (`UasSort.Core.Geo`) |
| `new ScanService(Settings settings, IDirectoryLister lister, ILedgerStore ledger, IVolumeProvider volumes, ITimeZoneResolver tz, TimeZoneInfo pcZone, TimeProvider clock)`; `Task<ScanResult> ScanAsync(CardSource, ICardReaderFactory, IProgress<ScanProgress>, CancellationToken)` (ledger: `Check()` first, then `Load()` unless the status is `CloudOnly`, `Missing`, `Empty` or `VideoRootMissing`; no other ledger member) | Part 06 (`UasSort.Core.Planning`) |
| `new Planner(ITimeZoneResolver tz, IPlaceIndex? places, TimeZoneInfo pcZone, TimeProvider clock)`; `PlanBase Prepare(ScanResult)`; `Plan Derive(PlanBase, Tuning, IReadOnlyList<PlanEdit>, SessionFlags, int, CancellationToken)` | Part 06 (`UasSort.Core.Planning`) |
| `new WindowsDirectoryLister()`, `new PathFacts()`, `new WindowsVolumeProvider()`; `KnownFolders.Pictures()`, `KnownFolders.AppDataDir()` | Part 09 (`UasSort.Platform.Io`) |
| `new WindowsCardReaderFactory(Settings settings, string appDataDir, string machine, IPathFacts facts, IDirectoryLister lister)` | Part 09 (`UasSort.Platform.Card`) |
| `new LedgerStore(Settings settings, string appDataDir, string machine, IPathFacts facts, IDirectoryLister lister, TimeProvider clock)` : `ILedgerStore` | Part 09 (`UasSort.Platform.Ledger`) |
| `new SettingsStore(string appDataDir, string picturesFolder, string machine, IPathFacts facts, IDirectoryLister lister, TimeProvider clock)` and `SettingsStore.ForFile(string settingsFile, string appDataDir, string picturesFolder, string machine, IPathFacts facts, IDirectoryLister lister, TimeProvider clock)`; `Load(readOnly: true)` renames, creates and writes nothing. Through Part 05's `SettingsLoadPolicy` (registry decision 46): a missing file gives the derived defaults with `Recovered = false` and `RootsConfirmed = false`; an unreadable file gives the derived defaults with `Recovered = true` | Part 09 (`UasSort.Platform.Stores`) |
| `new AppAssets(string appDir, Assembly selfTestAssembly)` : `IAppAssets` (`OpenPlaces()` = `appDir\places.bin.gz`, `FileNotFoundException` when absent) | Part 09 (`UasSort.Platform.Shell`) |
| `UasSort.Testing.RepoPaths.Of(string relativePath)`, `UasSort.Testing.TestTempDir` (`FullPath`, `Combine`, `Dispose`), `SyntheticMp4Builder` (`WithMvhdUtc`, `WithDjmdGps`, `WithDjmdTrack`, `Build()`) | Parts 01, 03 (`UasSort.Testing`) |
| The model and ports of Ref §3/§4.1 (`Settings`, `Plan`, `CardSource`, `SourceOk`, …) in `UasSort.Core`, and the Core component namespaces (`UasSort.Core.Card`, `.Geo`, `.Planning`, …). Every one of them comes from the fixed `GlobalUsings.Core.cs` that Part 02 Task 02.1 puts in `src/UasSort.Cli`, `tests/UasSort.Testing` and `tests/UasSort.Platform.Tests`; `src/UasSort.Cli/GlobalUsings.cs` (Task 12.1) adds only the Platform namespaces. Platform.Tests' own `GlobalUsings.cs` (Part 09) already imports `UasSort.Testing` and the Platform namespaces, so the test files here add only `using UasSort.Cli;` (and BCL namespaces) | Part 02 |

`FakeCardWriter` (Ref §2.3: "materialises scenarios under `%TEMP%\uas-sort-test-*` only") is owned by this part (registry Testing table; its only user is this part): Task 12.6 defines it in `tests/UasSort.Testing`, namespace `UasSort.Testing`.

---

### Task 12.1 — CLI project and argument parsing

**Files:**
- Modify (replace content): `src/UasSort.Cli/UasSort.Cli.csproj` (created by Part 01)
- Create: `src/UasSort.Cli/CliArgs.cs`
- Create: `src/UasSort.Cli/GlobalUsings.cs`
- Modify: `tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj` (reference the CLI)
- Create: `tests/UasSort.Platform.Tests/Cli/CliArgsTests.cs`

**Interfaces:**
- Consumes: nothing beyond the BCL.
- Produces (defined here):
  - `internal sealed record CliArgs(string Card, string? VideoRoot, string? PhotoRoot, double? RadiusMiles, int? GapDays, string? SettingsPath, bool Json, string? ExpectPath)`
  - `public const string CliArgs.Usage`
  - `public static bool CliArgs.TryParse(IReadOnlyList<string> args, [NotNullWhen(true)] out CliArgs? parsed, [NotNullWhen(false)] out string? error)` — accepts exactly the grammar of Ref §4.5: `plan --card <path> [--video-root <path>] [--photo-root <path>] [--radius-mi <5..100>] [--gap-days <0..7>] [--settings <path>] [--json] [--expect <expected.json>]`; numbers parsed with the invariant culture; unknown, repeated or value-less options are errors.

- [ ] **Step 1: Write the failing tests**

Add to `tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj` (Part 09's content), after `<ProjectReference Include="..\UasSort.Testing\UasSort.Testing.csproj" />`:

```xml
    <ProjectReference Include="..\..\src\UasSort.Cli\UasSort.Cli.csproj" />
```

`tests/UasSort.Platform.Tests/Cli/CliArgsTests.cs`:

```csharp
using System.Globalization;
using UasSort.Cli;

namespace UasSort.Platform.Tests.Cli;

public sealed class CliArgsTests
{
    [Fact]
    public void Minimal_PlanWithCard()
    {
        Assert.True(CliArgs.TryParse(["plan", "--card", @"E:\"], out var a, out var error), error);
        Assert.Equal(new CliArgs(@"E:\", null, null, null, null, null, false, null), a);
    }

    [Fact]
    public void AllOptions_AreRead()
    {
        string[] args = ["plan", "--card", @"E:\", "--video-root", @"C:\v", "--photo-root", @"C:\p", "--radius-mi", "25.5",
                         "--gap-days", "3", "--settings", @"C:\s.json", "--json", "--expect", @"C:\e.json"];
        Assert.True(CliArgs.TryParse(args, out var a, out var error), error);
        Assert.Equal(new CliArgs(@"E:\", @"C:\v", @"C:\p", 25.5, 3, @"C:\s.json", true, @"C:\e.json"), a);
    }

    [Fact]
    public void Radius_IsParsedWithTheInvariantCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.True(CliArgs.TryParse(["plan", "--card", "E:", "--radius-mi", "50.5"], out var a, out var error), error);
            Assert.Equal(50.5, a.RadiusMiles);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Theory]
    [InlineData("5", 5.0)]
    [InlineData("100", 100.0)]
    [InlineData("50", 50.0)]
    public void Radius_InRange(string value, double expected)
    {
        Assert.True(CliArgs.TryParse(["plan", "--card", "E:", "--radius-mi", value], out var a, out var error), error);
        Assert.Equal(expected, a.RadiusMiles);
    }

    [Theory]
    [InlineData("4.9")]
    [InlineData("100.1")]
    [InlineData("abc")]
    [InlineData("NaN")]
    public void Radius_OutOfRange_IsAnError(string value)
    {
        Assert.False(CliArgs.TryParse(["plan", "--card", "E:", "--radius-mi", value], out _, out var error));
        Assert.Contains("--radius-mi", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("7", 7)]
    public void GapDays_InRange(string value, int expected)
    {
        Assert.True(CliArgs.TryParse(["plan", "--card", "E:", "--gap-days", value], out var a, out var error), error);
        Assert.Equal(expected, a.GapDays);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("8")]
    [InlineData("1.5")]
    public void GapDays_OutOfRange_IsAnError(string value)
    {
        Assert.False(CliArgs.TryParse(["plan", "--card", "E:", "--gap-days", value], out _, out var error));
        Assert.Contains("--gap-days", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new string[0], "missing command")]
    [InlineData(new[] { "cleanup", "--card", "E:" }, "unknown command")]
    [InlineData(new[] { "plan" }, "--card <path> is required")]
    [InlineData(new[] { "plan", "--card" }, "--card needs a value")]
    [InlineData(new[] { "plan", "--card", "--json" }, "--card needs a value")]
    [InlineData(new[] { "plan", "--card", "E:", "--card", "F:" }, "--card given twice")]
    [InlineData(new[] { "plan", "--card", "E:", "--json", "--json" }, "--json given twice")]
    [InlineData(new[] { "plan", "--card", "E:", "--delete" }, "unknown argument '--delete'")]
    public void BadArguments_AreErrors(string[] args, string expectedError)
    {
        Assert.False(CliArgs.TryParse(args, out var a, out var error));
        Assert.Null(a);
        Assert.Contains(expectedError, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Usage_ListsEveryOption()
    {
        foreach (var option in new[] { "--card", "--video-root", "--photo-root", "--radius-mi", "--gap-days", "--settings", "--json", "--expect" })
            Assert.Contains(option, CliArgs.Usage, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*CliArgsTests*"
```

Expected: the build fails with `error CS0246: The type or namespace name 'CliArgs' could not be found`.

- [ ] **Step 3: Implement the project and the parser**

`src/UasSort.Cli/UasSort.Cli.csproj` (Part 01's file plus the test access and the place index; the BannedSymbols wiring comes from `Directory.Build.props`/`.targets` of Part 01, which already covers `UasSort.Cli`; `GlobalUsings.Core.cs` and `GlobalUsings.cs` are compiled by the SDK's default globbing):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0-windows10.0.26100.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.26100.0</TargetPlatformMinVersion>
    <AssemblyName>uas-sort-cli</AssemblyName>
    <RootNamespace>UasSort.Cli</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\UasSort.Core\UasSort.Core.csproj" />
    <ProjectReference Include="..\UasSort.Platform\UasSort.Platform.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="UasSort.Platform.Tests" />
  </ItemGroup>

  <!-- The offline place index, so dry-run descriptions match the app's (Ref §8.7). Read-only, from the exe folder. -->
  <ItemGroup Condition="Exists('..\UasSort.App\places.bin.gz')">
    <None Include="..\UasSort.App\places.bin.gz" Link="places.bin.gz" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

`src/UasSort.Cli/CliArgs.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace UasSort.Cli;

/// <summary>The command line of <c>uas-sort-cli plan</c> (Ref §4.5). There is no other command, and no cleanup.</summary>
internal sealed record CliArgs(string Card, string? VideoRoot, string? PhotoRoot, double? RadiusMiles, int? GapDays,
                               string? SettingsPath, bool Json, string? ExpectPath)
{
    public const string Usage =
        "usage: uas-sort-cli plan --card <path> [--video-root <path>] [--photo-root <path>] [--radius-mi <5..100>]\n" +
        "                         [--gap-days <0..7>] [--settings <path>] [--json] [--expect <expected.json>]\n" +
        "exit codes: 0 = plan printed, 1 = card source refused, 2 = any other error";

    private static readonly string[] ValueOptions =
        ["--card", "--video-root", "--photo-root", "--radius-mi", "--gap-days", "--settings", "--expect"];

    public static bool TryParse(IReadOnlyList<string> args, [NotNullWhen(true)] out CliArgs? parsed,
                                [NotNullWhen(false)] out string? error)
    {
        parsed = null;
        if (args.Count == 0) { error = "missing command 'plan'"; return false; }
        if (!string.Equals(args[0], "plan", StringComparison.Ordinal))
        {
            error = $"unknown command '{args[0]}' (the only command is 'plan')";
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool json = false;
        for (int i = 1; i < args.Count; i++)
        {
            string a = args[i];
            if (a == "--json")
            {
                if (json) { error = "--json given twice"; return false; }
                json = true;
                continue;
            }
            if (Array.IndexOf(ValueOptions, a) < 0) { error = $"unknown argument '{a}'"; return false; }
            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"{a} needs a value";
                return false;
            }
            if (!values.TryAdd(a, args[++i])) { error = $"{a} given twice"; return false; }
        }

        if (!values.TryGetValue("--card", out var card) || string.IsNullOrWhiteSpace(card))
        {
            error = "--card <path> is required";
            return false;
        }

        double? radius = null;
        if (values.TryGetValue("--radius-mi", out var r))
        {
            if (!double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out var rv) ||
                !double.IsFinite(rv) || rv < 5 || rv > 100)
            {
                error = $"--radius-mi must be a number from 5 to 100 (got '{r}')";
                return false;
            }
            radius = rv;
        }

        int? gap = null;
        if (values.TryGetValue("--gap-days", out var g))
        {
            if (!int.TryParse(g, NumberStyles.None, CultureInfo.InvariantCulture, out var gv) || gv > 7)
            {
                error = $"--gap-days must be a whole number from 0 to 7 (got '{g}')";
                return false;
            }
            gap = gv;
        }

        parsed = new CliArgs(card, values.GetValueOrDefault("--video-root"), values.GetValueOrDefault("--photo-root"),
                             radius, gap, values.GetValueOrDefault("--settings"), json, values.GetValueOrDefault("--expect"));
        error = null;
        return true;
    }
}
```

`src/UasSort.Cli/GlobalUsings.cs` (exactly the registry content; `System.Collections.Immutable` and every Core namespace come from Part 02's `src/UasSort.Cli/GlobalUsings.Core.cs`, which is not touched here, and no namespace is imported twice in the project):

```csharp
global using UasSort.Platform.Card;
global using UasSort.Platform.Io;
global using UasSort.Platform.Ledger;
global using UasSort.Platform.Shell;
global using UasSort.Platform.Stores;
global using UasSort.Platform.Win32;
```

Part 01's `src/UasSort.Cli/Program.cs` (a usage error for every invocation) stays until Task 12.6 replaces it.

- [ ] **Step 4: Run them and watch them pass**

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*CliArgsTests*"
```

Expected: `failed: 0, succeeded: 24` (4 facts + 20 theory rows), exit code 0.

- [ ] **Step 5: Commit**

```powershell
git add src/UasSort.Cli tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj tests/UasSort.Platform.Tests/Cli/CliArgsTests.cs
git commit -m "feat: uas-sort-cli project and plan argument parsing

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 12.2 — The plan JSON document (`v:1`) and the text rendering

**Files:**
- Create: `src/UasSort.Cli/PlanDocument.cs`
- Create: `src/UasSort.Cli/PlanTextRenderer.cs`
- Create: `tests/UasSort.Platform.Tests/Cli/PlanDocumentTests.cs`
- Create: `tests/UasSort.Platform.Tests/Cli/CliSamples.cs`

**Interfaces:**
- Consumes (Ref §3): `ClockMode`, `Confidence`, `BoundaryCause`, `TimeSource`, `SetResolution`, `EntryClass`, `IssueSeverity`, `IssueCode`.
- Produces (defined here; the JSON names are the camelCase of these members, enums as names — exactly the schema of Ref §4.5):

```csharp
internal enum GroupTargetKind { NewFolder, Append, AlreadyImported, NothingToCopy, SkipGroup }   // = the GroupTarget case names
internal enum NewnessStatus { New, Imported, Decided, ProbablyImported, Conflict }              // IsNew → "New"
internal sealed record PlanDocument(int V, CardJson Card, SettingsJson Settings, ClockJson Clock, DateTime? WatermarkUtc,
    IReadOnlyList<GroupJson> Groups, IReadOnlyList<PhotoDayJson> PhotoDays, IReadOnlyList<SetJson> Sets,
    IReadOnlyList<OtherJson> Other, IReadOnlyList<IssueJson> Issues);
internal sealed record CardJson(string Root, IdentityJson? Identity, string? Model, int Files, string InventoryHash);
internal sealed record IdentityJson(string Serial, string? Label, string Fs, long TotalBytes);
internal sealed record SettingsJson(string VideoRoot, string PhotoRoot, double RadiusMiles, int GapDays);
internal sealed record ClockJson(ClockMode Mode, string? Zone, int Samples, MismatchJson Mismatch);
internal sealed record MismatchJson(int Items, IReadOnlyList<string> SiteZones);
internal sealed record GroupJson(string Id, GroupTargetKind Target, string? RelPath, Confidence? Confidence, string? Why,
    DateOnly Start, DateOnly End, BoundaryJson? BoundaryBefore, IReadOnlyList<VideoJson> Videos,
    IReadOnlyList<DaySplitJson> DaySplits, IReadOnlyList<IssueCode> Issues);
internal sealed record BoundaryJson(BoundaryCause Cause, double? JumpMiles, double GapHours, int DayGap);
internal sealed record VideoJson(string Id, NewnessStatus Status, bool Included, DateTime CaptureUtc, DateOnly LocalDate,
    TimeSource TimeSource, IReadOnlyList<string> Flags);
internal sealed record DaySplitJson(string FirstOfDay, DateOnly From, DateOnly To, double? ApartMiles, bool Emphasised);
internal sealed record PhotoDayJson(DateOnly Date, string Tz, int Units, int New, int ProbablyImported, string Reason);
internal sealed record SetJson(string Id, string Folder, SetResolution Resolution, int Members);
internal sealed record OtherJson(string RelPath, EntryClass Class, string? Rule);
internal sealed record IssueJson(IssueSeverity Severity, IssueCode Code, string? Anchor, string Message, bool RequiresAck);
[JsonSerializable(typeof(PlanDocument))] internal sealed partial class CliJsonContext : JsonSerializerContext;   // camelCase, string enums, indented
internal static class PlanTextRenderer { public static void Render(PlanDocument doc, TextWriter w); public static string FormatDays(DateOnly start, DateOnly end); }
```

- [ ] **Step 1: Write the failing tests**

`tests/UasSort.Platform.Tests/Cli/CliSamples.cs` (a hand-built document mirroring the Ref §4.5 example; reused by later tasks):

```csharp
using UasSort.Cli;

namespace UasSort.Platform.Tests.Cli;

internal static class CliSamples
{
    public const string CouncilId = "DCIM/DJI_001/DJI_20260725232655_0117_D.MP4";
    public const string AnvilFirstId = "DCIM/DJI_001/DJI_20260726235645_0001_D.MP4";
    public const string ZacharId = "DCIM/DJI_001/DJI_20260927140127_0123_D.MP4";

    public static VideoJson Video(string id, NewnessStatus status = NewnessStatus.New, bool included = true, params string[] flags) =>
        new(id, status, included, new DateTime(2026, 7, 26, 3, 26, 55, DateTimeKind.Utc), new DateOnly(2026, 7, 25),
            TimeSource.Mvhd, flags);

    public static GroupJson CouncilAppend() =>
        new(CouncilId, GroupTargetKind.Append, @"2026\2026-07\2026-07-25 Council Road", Confidence.Medium,
            "different day, 34 mi from Council Road", new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26),
            new BoundaryJson(BoundaryCause.DayGap, null, 1488.5, 62),
            [Video(CouncilId, NewnessStatus.Imported, false, "ClockMismatch"), Video(AnvilFirstId)],
            [new DaySplitJson(AnvilFirstId, new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26), 33.7, true)],
            [IssueCode.MediumAppend, IssueCode.EmphasisedDaySplit]);

    public static GroupJson ZacharNewFolder() =>
        new(ZacharId, GroupTargetKind.NewFolder, @"2026\2026-09\2026-09-27", null, null,
            new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), null,
            Enumerable.Range(0, 13).Select(i => Video($"DCIM/DJI_001/DJI_20260927140127_{123 + i:0000}_D.MP4")).ToList(),
            [], [IssueCode.EmptyFolderName]);

    public static PlanDocument Document() =>
        new(1,
            new CardJson(@"E:\", new IdentityJson("1A2B3C4D", null, "exFAT", 256060514304), "FC9113", 214, "9f3c0a6d12e4b7a1"),
            new SettingsJson(@"C:\x\UAS Videos", @"C:\x\UAS Videos\Picture Offload", 50, 1),
            new ClockJson(ClockMode.Zone, "America/New_York", 13, new MismatchJson(25, ["America/Anchorage"])),
            new DateTime(2026, 9, 27, 18, 24, 16, DateTimeKind.Utc),
            [CouncilAppend(), ZacharNewFolder()],
            [new PhotoDayJson(new DateOnly(2026, 7, 25), "America/Anchorage", 12, 8, 4, "videos from this day are already in the library")],
            [new SetJson("DCIM/PANORAMA/001_0087", "001_0087 2026-09-27", SetResolution.DateSuffixed, 33)],
            [new OtherJson("DCIM/DJI_A001/x.MP4", EntryClass.Unknown, null)],
            [new IssueJson(IssueSeverity.Blocking, IssueCode.EmptyFolderName, ZacharId, "Name this folder", false)]);
}
```

`tests/UasSort.Platform.Tests/Cli/PlanDocumentTests.cs`:

```csharp
using System.Text.Json;
using UasSort.Cli;

namespace UasSort.Platform.Tests.Cli;

public sealed class PlanDocumentTests
{
    private static JsonElement Serialize() =>
        JsonDocument.Parse(JsonSerializer.Serialize(CliSamples.Document(), CliJsonContext.Default.PlanDocument)).RootElement;

    [Fact]
    public void TopLevel_MatchesTheRefSchema()
    {
        var root = Serialize();
        Assert.Equal(1, root.GetProperty("v").GetInt32());
        string[] names = root.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "v", "card", "settings", "clock", "watermarkUtc", "groups", "photoDays", "sets", "other", "issues" }, names);
        Assert.Equal("2026-09-27T18:24:16Z", root.GetProperty("watermarkUtc").GetString());
    }

    [Fact]
    public void Card_And_Clock_UseCamelCaseAndEnumNames()
    {
        var root = Serialize();
        var identity = root.GetProperty("card").GetProperty("identity");
        Assert.Equal("1A2B3C4D", identity.GetProperty("serial").GetString());
        Assert.Equal(JsonValueKind.Null, identity.GetProperty("label").ValueKind);
        Assert.Equal(256060514304, identity.GetProperty("totalBytes").GetInt64());
        Assert.Equal("FC9113", root.GetProperty("card").GetProperty("model").GetString());
        var clock = root.GetProperty("clock");
        Assert.Equal("Zone", clock.GetProperty("mode").GetString());
        Assert.Equal(25, clock.GetProperty("mismatch").GetProperty("items").GetInt32());
        Assert.Equal("America/Anchorage", clock.GetProperty("mismatch").GetProperty("siteZones")[0].GetString());
        Assert.Equal(50, root.GetProperty("settings").GetProperty("radiusMiles").GetDouble());
    }

    [Fact]
    public void Group_MatchesTheRefExample()
    {
        var g = Serialize().GetProperty("groups")[0];
        Assert.Equal(CliSamples.CouncilId, g.GetProperty("id").GetString());
        Assert.Equal("Append", g.GetProperty("target").GetString());
        Assert.Equal(@"2026\2026-07\2026-07-25 Council Road", g.GetProperty("relPath").GetString());
        Assert.Equal("Medium", g.GetProperty("confidence").GetString());
        Assert.Equal("2026-07-25", g.GetProperty("start").GetString());
        var b = g.GetProperty("boundaryBefore");
        Assert.Equal("DayGap", b.GetProperty("cause").GetString());
        Assert.Equal(JsonValueKind.Null, b.GetProperty("jumpMiles").ValueKind);
        Assert.Equal(62, b.GetProperty("dayGap").GetInt32());
        var v = g.GetProperty("videos")[0];
        Assert.Equal("Imported", v.GetProperty("status").GetString());
        Assert.False(v.GetProperty("included").GetBoolean());
        Assert.Equal("2026-07-26T03:26:55Z", v.GetProperty("captureUtc").GetString());
        Assert.Equal("Mvhd", v.GetProperty("timeSource").GetString());
        Assert.Equal("ClockMismatch", v.GetProperty("flags")[0].GetString());
        Assert.True(g.GetProperty("daySplits")[0].GetProperty("emphasised").GetBoolean());
        Assert.Equal(new[] { "MediumAppend", "EmphasisedDaySplit" }, g.GetProperty("issues").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    [Fact]
    public void PhotoDays_Sets_Other_Issues_UseTheRefNames()
    {
        var root = Serialize();
        var day = root.GetProperty("photoDays")[0];
        Assert.Equal(8, day.GetProperty("new").GetInt32());
        Assert.Equal(4, day.GetProperty("probablyImported").GetInt32());
        Assert.Equal("America/Anchorage", day.GetProperty("tz").GetString());
        Assert.Equal("DateSuffixed", root.GetProperty("sets")[0].GetProperty("resolution").GetString());
        Assert.Equal("Unknown", root.GetProperty("other")[0].GetProperty("class").GetString());
        var issue = root.GetProperty("issues")[0];
        Assert.Equal("Blocking", issue.GetProperty("severity").GetString());
        Assert.Equal("EmptyFolderName", issue.GetProperty("code").GetString());
        Assert.False(issue.GetProperty("requiresAck").GetBoolean());
    }

    [Fact]
    public void Text_NewFolderLine_IsTheRefFormat()
    {
        var w = new StringWriter();
        PlanTextRenderer.Render(CliSamples.Document(), w);
        string text = w.ToString();
        Assert.Contains("NEW FOLDER 2026\\2026-09\\2026-09-27 · 13 clips · Sep 27 · issues: EmptyFolderName", text, StringComparison.Ordinal);
        Assert.Contains("APPEND (Medium) 2026\\2026-07\\2026-07-25 Council Road · 2 clips · Jul 25–26 · issues: MediumAppend, EmphasisedDaySplit",
                        text, StringComparison.Ordinal);
        Assert.Contains("different day, 34 mi from Council Road", text, StringComparison.Ordinal);
        Assert.Contains("PHOTO DAYS", text, StringComparison.Ordinal);
        Assert.Contains("001_0087 2026-09-27", text, StringComparison.Ordinal);
        Assert.Contains("DCIM/DJI_A001/x.MP4", text, StringComparison.Ordinal);
        Assert.Contains("Blocking EmptyFolderName", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2026, 9, 27, 2026, 9, 27, "Sep 27")]
    [InlineData(2026, 7, 25, 2026, 7, 26, "Jul 25–26")]
    [InlineData(2026, 7, 31, 2026, 8, 2, "Jul 31–Aug 2")]
    [InlineData(2025, 12, 31, 2026, 1, 2, "Dec 31, 2025–Jan 2, 2026")]
    public void Text_DayRanges(int y1, int m1, int d1, int y2, int m2, int d2, string expected) =>
        Assert.Equal(expected, PlanTextRenderer.FormatDays(new DateOnly(y1, m1, d1), new DateOnly(y2, m2, d2)));
}
```

- [ ] **Step 2: Run them and watch them fail**

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*PlanDocumentTests*"
```

Expected: build error `CS0246: The type or namespace name 'VideoJson' could not be found` (and the other DTOs).

- [ ] **Step 3: Implement the document and the text rendering**

`src/UasSort.Cli/PlanDocument.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UasSort.Cli;

/// <summary>The GroupTarget case of a group, by name (Ref §3).</summary>
internal enum GroupTargetKind { NewFolder, Append, AlreadyImported, NothingToCopy, SkipGroup }

/// <summary>The Newness case of an item, by name; IsNew is written "New".</summary>
internal enum NewnessStatus { New, Imported, Decided, ProbablyImported, Conflict }

/// <summary>`uas-sort-cli plan --json`, schema v1 (Ref §4.5). Property order and names are the contract.</summary>
internal sealed record PlanDocument(int V, CardJson Card, SettingsJson Settings, ClockJson Clock, DateTime? WatermarkUtc,
    IReadOnlyList<GroupJson> Groups, IReadOnlyList<PhotoDayJson> PhotoDays, IReadOnlyList<SetJson> Sets,
    IReadOnlyList<OtherJson> Other, IReadOnlyList<IssueJson> Issues);

internal sealed record CardJson(string Root, IdentityJson? Identity, string? Model, int Files, string InventoryHash);

internal sealed record IdentityJson(string Serial, string? Label, string Fs, long TotalBytes);

internal sealed record SettingsJson(string VideoRoot, string PhotoRoot, double RadiusMiles, int GapDays);

internal sealed record ClockJson(ClockMode Mode, string? Zone, int Samples, MismatchJson Mismatch);

internal sealed record MismatchJson(int Items, IReadOnlyList<string> SiteZones);

internal sealed record GroupJson(string Id, GroupTargetKind Target, string? RelPath, Confidence? Confidence, string? Why,
    DateOnly Start, DateOnly End, BoundaryJson? BoundaryBefore, IReadOnlyList<VideoJson> Videos,
    IReadOnlyList<DaySplitJson> DaySplits, IReadOnlyList<IssueCode> Issues);

internal sealed record BoundaryJson(BoundaryCause Cause, double? JumpMiles, double GapHours, int DayGap);

internal sealed record VideoJson(string Id, NewnessStatus Status, bool Included, DateTime CaptureUtc, DateOnly LocalDate,
    TimeSource TimeSource, IReadOnlyList<string> Flags);

internal sealed record DaySplitJson(string FirstOfDay, DateOnly From, DateOnly To, double? ApartMiles, bool Emphasised);

internal sealed record PhotoDayJson(DateOnly Date, string Tz, int Units, int New, int ProbablyImported, string Reason);

internal sealed record SetJson(string Id, string Folder, SetResolution Resolution, int Members);

internal sealed record OtherJson(string RelPath, EntryClass Class, string? Rule);

internal sealed record IssueJson(IssueSeverity Severity, IssueCode Code, string? Anchor, string Message, bool RequiresAck);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
                             WriteIndented = true, PropertyNameCaseInsensitive = true,
                             ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(PlanDocument))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
```

`src/UasSort.Cli/PlanTextRenderer.cs`:

```csharp
using System.Globalization;

namespace UasSort.Cli;

/// <summary>The human-readable plan (Ref §4.5): one block per group, then photo days, sets, other files and issues.</summary>
internal static class PlanTextRenderer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Render(PlanDocument doc, TextWriter w)
    {
        w.WriteLine(string.Create(Inv, $"CARD {doc.Card.Root} · {doc.Card.Model ?? "unknown model"} · {doc.Card.Files} files · inventory {doc.Card.InventoryHash}"));
        w.WriteLine(string.Create(Inv, $"ROOTS video {doc.Settings.VideoRoot} · photo {doc.Settings.PhotoRoot} · R {doc.Settings.RadiusMiles} mi · G {doc.Settings.GapDays}"));
        w.WriteLine(string.Create(Inv, $"CLOCK {doc.Clock.Mode} {doc.Clock.Zone ?? "-"} · {doc.Clock.Samples} samples · mismatch {doc.Clock.Mismatch.Items} ({string.Join(", ", doc.Clock.Mismatch.SiteZones)})"));
        w.WriteLine();

        foreach (var g in doc.Groups)
        {
            w.WriteLine(GroupLine(g));
            if (g.BoundaryBefore is { } b)
                w.WriteLine(string.Create(Inv, $"  boundary before: {b.Cause}{(b.JumpMiles is { } j ? " · " + j.ToString("0.0", Inv) + " mi" : "")} · {b.GapHours:0.#} h · {b.DayGap} days"));
            foreach (var v in g.Videos)
            {
                string flags = v.Flags.Count == 0 ? "" : " [" + string.Join(", ", v.Flags) + "]";
                w.WriteLine(string.Create(Inv, $"  {(v.Included ? "x" : " ")} {FileName(v.Id)}  {v.Status}  {v.LocalDate:yyyy-MM-dd}  {v.TimeSource}{flags}"));
            }
            foreach (var s in g.DaySplits)
                w.WriteLine(string.Create(Inv, $"  -- day change {s.From:yyyy-MM-dd} → {s.To:yyyy-MM-dd} before {FileName(s.FirstOfDay)}{(s.ApartMiles is { } a ? " · " + a.ToString("0.0", Inv) + " mi apart" : "")}{(s.Emphasised ? " (emphasised)" : "")}"));
            w.WriteLine();
        }

        w.WriteLine("PHOTO DAYS");
        if (doc.PhotoDays.Count == 0) w.WriteLine("  (none)");
        foreach (var d in doc.PhotoDays)
            w.WriteLine(string.Create(Inv, $"  {d.Date:yyyy-MM-dd} {d.Tz} · {d.Units} units · {d.New} new · {d.ProbablyImported} probably imported · {d.Reason}"));

        w.WriteLine("SETS");
        if (doc.Sets.Count == 0) w.WriteLine("  (none)");
        foreach (var s in doc.Sets)
            w.WriteLine(string.Create(Inv, $"  {s.Id} → {s.Folder} · {s.Resolution} · {s.Members} members"));

        w.WriteLine("OTHER FILES");
        if (doc.Other.Count == 0) w.WriteLine("  (none)");
        foreach (var o in doc.Other)
            w.WriteLine(string.Create(Inv, $"  {o.RelPath} · {o.Class}{(o.Rule is { } r ? $" ({r})" : "")}"));

        w.WriteLine("ISSUES");
        if (doc.Issues.Count == 0) w.WriteLine("  (none)");
        foreach (var i in doc.Issues)
            w.WriteLine(string.Create(Inv, $"  {i.Severity} {i.Code} · {i.Message}{(i.Anchor is { } a ? " · " + a : "")}{(i.RequiresAck ? " · needs acknowledgement at preflight" : "")}"));
    }

    private static string GroupLine(GroupJson g)
    {
        string label = g.Target switch
        {
            GroupTargetKind.NewFolder => "NEW FOLDER",
            GroupTargetKind.Append => string.Create(Inv, $"APPEND ({g.Confidence})"),
            GroupTargetKind.AlreadyImported => "ALREADY IMPORTED",
            GroupTargetKind.NothingToCopy => "NOTHING TO COPY",
            GroupTargetKind.SkipGroup => "SKIP",
            _ => throw new ArgumentOutOfRangeException(nameof(g), g.Target, "unknown target kind"),
        };
        string head = g.RelPath is { } p ? $"{label} {p}" : label;
        string line = string.Create(Inv, $"{head} · {g.Videos.Count} clips · {FormatDays(g.Start, g.End)}");
        if (g.Issues.Count > 0) line += " · issues: " + string.Join(", ", g.Issues);
        if (g.Why is { } why) line += Environment.NewLine + "  " + why;
        return line;
    }

    public static string FormatDays(DateOnly start, DateOnly end)
    {
        if (start == end) return start.ToString("MMM d", Inv);
        if (start.Year != end.Year) return start.ToString("MMM d, yyyy", Inv) + "–" + end.ToString("MMM d, yyyy", Inv);
        if (start.Month != end.Month) return start.ToString("MMM d", Inv) + "–" + end.ToString("MMM d", Inv);
        return start.ToString("MMM d", Inv) + "–" + end.ToString("%d", Inv);
    }

    private static string FileName(string cardRelPath) => cardRelPath[(cardRelPath.LastIndexOf('/') + 1)..];
}
```

- [ ] **Step 4: Run them and watch them pass**

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*PlanDocumentTests*"
```

Expected: `failed: 0, succeeded: 9`, exit code 0.

- [ ] **Step 5: Commit**

```powershell
git add src/UasSort.Cli/PlanDocument.cs src/UasSort.Cli/PlanTextRenderer.cs tests/UasSort.Platform.Tests/Cli/PlanDocumentTests.cs tests/UasSort.Platform.Tests/Cli/CliSamples.cs
git commit -m "feat: uas-sort-cli plan JSON document (v1) and text rendering

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 12.3 — `--expect`: the edit count ("within 2 edits")

The edit count of Ref §4.5, computed on the plan document so it is pure and testable. One edit is one `PlanEdit`:
- for each expected folder whose clips (those on the card) span k > 1 plan groups: k − 1 (merges);
- for each plan group whose clips span k > 1 expected folders: k − 1 (splits or moves);
- plus 1 per expected folder whose matched plan group (the group holding most of its clips; the earliest group on a tie) has a different relative path (a `Rename`) or, when the expected folder names a `target`, a different target kind (a `Retarget`).

Clips on the card that no expected folder lists are not compared. Expected clips that are not on the card are listed as differences but add no edits. Clip names and paths compare case-insensitively, with `/` and `\` treated alike. Acceptance step 2 passes at ≤ 2 edits.

**Files:**
- Create: `src/UasSort.Cli/ExpectDiff.cs`
- Create: `tests/UasSort.Platform.Tests/Cli/ExpectDiffTests.cs`

**Interfaces:**
- Consumes: `GroupJson`, `VideoJson`, `GroupTargetKind` (Task 12.2).
- Produces (defined here):

```csharp
internal sealed record ExpectedFileJson(IReadOnlyList<ExpectedFolderJson> Folders);
internal sealed record ExpectedFolderJson(string RelPath, IReadOnlyList<string> Clips, GroupTargetKind? Target);
internal enum ExpectDifferenceKind { Merge, SplitOrMove, RenameOrRetarget, NotOnCard, ListedTwice }
internal sealed record ExpectDifference(ExpectDifferenceKind Kind, string Subject, string Detail, int Edits);
internal sealed record ExpectReport(int Edits, IReadOnlyList<ExpectDifference> Differences)
{ public const int PassAt = 2; public bool Passes => Edits <= PassAt; }
internal static class ExpectDiff
{ public static ExpectReport Compare(ExpectedFileJson expected, IReadOnlyList<GroupJson> groups);
  public static void Write(ExpectReport report, TextWriter w); }
```

- [ ] **Step 1: Write the failing tests**

`tests/UasSort.Platform.Tests/Cli/ExpectDiffTests.cs`:

```csharp
using UasSort.Cli;

namespace UasSort.Platform.Tests.Cli;

public sealed class ExpectDiffTests
{
    private static string Clip(int n, string day = "20260726") => $"DJI_{day}120000_{n:0000}_D.MP4";

    private static GroupJson Group(string relPath, GroupTargetKind kind, params string[] clipNames) =>
        new($"DCIM/DJI_001/{clipNames[0]}", kind, relPath, kind == GroupTargetKind.Append ? Confidence.High : null, null,
            new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26), null,
            clipNames.Select(c => CliSamples.Video($"DCIM/DJI_001/{c}")).ToList(), [], []);

    private static ExpectedFolderJson Folder(string relPath, params string[] clips) => new(relPath, clips, null);

    private const string Council = @"2026\2026-07\2026-07-25 Council Road";
    private const string Anvil = @"2026\2026-07\2026-07-26 Anvil Mountain";

    [Fact]
    public void ExactMatch_IsZeroEdits()
    {
        var groups = new[] { Group(Council, GroupTargetKind.NewFolder, Clip(1), Clip(2)), Group(Anvil, GroupTargetKind.NewFolder, Clip(3)) };
        var expected = new ExpectedFileJson([Folder(Council, Clip(1), Clip(2)), Folder(Anvil, Clip(3))]);
        var r = ExpectDiff.Compare(expected, groups);
        Assert.Equal(0, r.Edits);
        Assert.Empty(r.Differences);
        Assert.True(r.Passes);
    }

    [Fact]
    public void CouncilAnvilMerged_IsOneSplitPlusOneRename_AndPasses()
    {
        // Scenario D shape: the plan appends Anvil's clips to Council Road; the user expects two folders.
        var groups = new[] { Group(Council, GroupTargetKind.Append, Clip(1), Clip(2), Clip(3), Clip(4)) };
        var expected = new ExpectedFileJson([Folder(Council, Clip(1), Clip(2)), Folder(Anvil, Clip(3), Clip(4))]);
        var r = ExpectDiff.Compare(expected, groups);
        Assert.Equal(2, r.Edits);
        Assert.Contains(r.Differences, d => d.Kind == ExpectDifferenceKind.SplitOrMove && d.Edits == 1);
        Assert.Contains(r.Differences, d => d.Kind == ExpectDifferenceKind.RenameOrRetarget && d.Subject == Anvil);
        Assert.True(r.Passes);
    }

    [Fact]
    public void OneExpectedFolderInTwoGroups_IsOneMerge()
    {
        var groups = new[] { Group(Council, GroupTargetKind.NewFolder, Clip(1)), Group(@"2026\2026-07\2026-07-26", GroupTargetKind.NewFolder, Clip(2)) };
        var r = ExpectDiff.Compare(new ExpectedFileJson([Folder(Council, Clip(1), Clip(2))]), groups);
        Assert.Equal(1, r.Edits);
        Assert.Equal(ExpectDifferenceKind.Merge, Assert.Single(r.Differences).Kind);
    }

    [Fact]
    public void DifferentDescription_IsOneRename()
    {
        var groups = new[] { Group(@"2026\2026-07\2026-07-26", GroupTargetKind.NewFolder, Clip(3)) };
        var r = ExpectDiff.Compare(new ExpectedFileJson([Folder(Anvil, Clip(3))]), groups);
        Assert.Equal(1, r.Edits);
        var d = Assert.Single(r.Differences);
        Assert.Equal(ExpectDifferenceKind.RenameOrRetarget, d.Kind);
        Assert.Contains(@"2026\2026-07\2026-07-26", d.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedTargetKind_IsCompared()
    {
        var groups = new[] { Group(Council, GroupTargetKind.Append, Clip(1)) };
        var r = ExpectDiff.Compare(new ExpectedFileJson([new ExpectedFolderJson(Council, [Clip(1)], GroupTargetKind.NewFolder)]), groups);
        Assert.Equal(1, r.Edits);
        Assert.Equal(0, ExpectDiff.Compare(new ExpectedFileJson([new ExpectedFolderJson(Council, [Clip(1)], GroupTargetKind.Append)]), groups).Edits);
    }

    [Fact]
    public void CaseAndSeparators_DoNotCount()
    {
        var groups = new[] { Group(Council, GroupTargetKind.NewFolder, Clip(1)) };
        var expected = new ExpectedFileJson([Folder("2026/2026-07/2026-07-25 council road/", Clip(1).ToLowerInvariant())]);
        Assert.Equal(0, ExpectDiff.Compare(expected, groups).Edits);
    }

    [Fact]
    public void ClipsNotOnCard_AreListed_WithoutEdits_AndUnlistedCardClipsAreIgnored()
    {
        var groups = new[] { Group(Council, GroupTargetKind.NewFolder, Clip(1), Clip(9)) };
        var r = ExpectDiff.Compare(new ExpectedFileJson([Folder(Council, Clip(1), Clip(7))]), groups);
        Assert.Equal(0, r.Edits);
        var d = Assert.Single(r.Differences);
        Assert.Equal(ExpectDifferenceKind.NotOnCard, d.Kind);
        Assert.Equal(Clip(7), d.Subject);
    }

    [Fact]
    public void AClipListedTwice_IsReported()
    {
        var groups = new[] { Group(Council, GroupTargetKind.NewFolder, Clip(1)) };
        var r = ExpectDiff.Compare(new ExpectedFileJson([Folder(Council, Clip(1)), Folder(Anvil, Clip(1))]), groups);
        Assert.Contains(r.Differences, d => d.Kind == ExpectDifferenceKind.ListedTwice && d.Subject == Clip(1));
    }

    [Fact]
    public void ThreeEdits_Fail()
    {
        var groups = new[] { Group("a", GroupTargetKind.NewFolder, Clip(1), Clip(2), Clip(3)) };
        var r = ExpectDiff.Compare(new ExpectedFileJson([Folder("x", Clip(1)), Folder("y", Clip(2)), Folder("z", Clip(3))]), groups);
        Assert.Equal(5, r.Edits);   // 2 splits + 3 renames
        Assert.False(r.Passes);
    }

    [Fact]
    public void Write_EndsWithTheEditCountAndVerdict()
    {
        var groups = new[] { Group(Council, GroupTargetKind.Append, Clip(1), Clip(2), Clip(3), Clip(4)) };
        var r = ExpectDiff.Compare(new ExpectedFileJson([Folder(Council, Clip(1), Clip(2)), Folder(Anvil, Clip(3), Clip(4))]), groups);
        var w = new StringWriter();
        ExpectDiff.Write(r, w);
        string[] lines = w.ToString().TrimEnd().Split(Environment.NewLine);
        Assert.Equal("EXPECT edits: 2 (passes at <= 2): PASS", lines[^1]);
        Assert.Contains(lines, l => l.StartsWith("EXPECT split/move:", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("EXPECT rename/retarget:", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*ExpectDiffTests*"
```

Expected: build error `CS0246: The type or namespace name 'ExpectedFolderJson' could not be found`.

- [ ] **Step 3: Implement the comparison**

`src/UasSort.Cli/ExpectDiff.cs`:

```csharp
using System.Globalization;

namespace UasSort.Cli;

/// <summary>tests/acceptance/first-card-expected.json (schema: tests/acceptance/first-card-expected.schema.json).</summary>
internal sealed record ExpectedFileJson(IReadOnlyList<ExpectedFolderJson> Folders);

/// <summary>One folder the user expects: its path relative to the video root, its clip file names, optionally the target kind.</summary>
internal sealed record ExpectedFolderJson(string RelPath, IReadOnlyList<string> Clips, GroupTargetKind? Target);

internal enum ExpectDifferenceKind { Merge, SplitOrMove, RenameOrRetarget, NotOnCard, ListedTwice }

internal sealed record ExpectDifference(ExpectDifferenceKind Kind, string Subject, string Detail, int Edits);

internal sealed record ExpectReport(int Edits, IReadOnlyList<ExpectDifference> Differences)
{
    /// <summary>Acceptance step 2 passes at this many edits or fewer (Ref §4.5, §13).</summary>
    public const int PassAt = 2;
    public bool Passes => Edits <= PassAt;
}

/// <summary>The edit count between the dry-run plan and the user's expected folder list (Ref §4.5).</summary>
internal static class ExpectDiff
{
    public static ExpectReport Compare(ExpectedFileJson expected, IReadOnlyList<GroupJson> groups)
    {
        var diffs = new List<ExpectDifference>();
        int edits = 0;

        var groupOfClip = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int gi = 0; gi < groups.Count; gi++)
            foreach (var v in groups[gi].Videos)
                groupOfClip.TryAdd(FileName(v.Id), gi);

        var folderOfClip = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int fi = 0; fi < expected.Folders.Count; fi++)
            foreach (var clip in expected.Folders[fi].Clips)
                if (!folderOfClip.TryAdd(clip, fi))
                    diffs.Add(new(ExpectDifferenceKind.ListedTwice, clip,
                        $"listed in {expected.Folders[folderOfClip[clip]].RelPath} and in {expected.Folders[fi].RelPath}; the first counts", 0));

        for (int fi = 0; fi < expected.Folders.Count; fi++)
        {
            var folder = expected.Folders[fi];
            var present = new List<string>();
            foreach (var clip in folder.Clips.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (groupOfClip.ContainsKey(clip)) present.Add(clip);
                else diffs.Add(new(ExpectDifferenceKind.NotOnCard, clip, $"expected in {folder.RelPath}, not on the card", 0));
            }
            if (present.Count == 0) continue;

            var groupIndexes = present.Select(c => groupOfClip[c]).Distinct().Order().ToList();
            if (groupIndexes.Count > 1)
            {
                int merges = groupIndexes.Count - 1;
                edits += merges;
                diffs.Add(new(ExpectDifferenceKind.Merge, folder.RelPath,
                    $"its clips are in {groupIndexes.Count} plan groups ({string.Join(", ", groupIndexes.Select(i => groups[i].Id))})", merges));
            }

            int matched = groupIndexes.OrderByDescending(gi => present.Count(c => groupOfClip[c] == gi)).ThenBy(gi => gi).First();
            var g = groups[matched];
            bool pathDiffers = !string.Equals(NormalizePath(g.RelPath), NormalizePath(folder.RelPath), StringComparison.OrdinalIgnoreCase);
            bool kindDiffers = folder.Target is { } kind && kind != g.Target;
            if (pathDiffers || kindDiffers)
            {
                edits += 1;
                diffs.Add(new(ExpectDifferenceKind.RenameOrRetarget, folder.RelPath,
                    $"the plan proposes {g.Target} {g.RelPath ?? "(no folder)"}", 1));
            }
        }

        foreach (var g in groups)
        {
            var folders = g.Videos.Select(v => FileName(v.Id)).Where(folderOfClip.ContainsKey)
                                  .Select(c => folderOfClip[c]).Distinct().Order().ToList();
            if (folders.Count > 1)
            {
                int splits = folders.Count - 1;
                edits += splits;
                diffs.Add(new(ExpectDifferenceKind.SplitOrMove, g.Id,
                    $"plan group {g.RelPath ?? g.Id} holds clips of {folders.Count} expected folders ({string.Join(", ", folders.Select(i => expected.Folders[i].RelPath))})", splits));
            }
        }

        return new ExpectReport(edits, diffs);
    }

    public static void Write(ExpectReport report, TextWriter w)
    {
        foreach (var d in report.Differences)
        {
            string label = d.Kind switch
            {
                ExpectDifferenceKind.Merge => "merge",
                ExpectDifferenceKind.SplitOrMove => "split/move",
                ExpectDifferenceKind.RenameOrRetarget => "rename/retarget",
                ExpectDifferenceKind.NotOnCard => "not on card",
                ExpectDifferenceKind.ListedTwice => "listed twice",
                _ => throw new ArgumentOutOfRangeException(nameof(report), d.Kind, "unknown difference"),
            };
            string edits = d.Edits == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $" ({d.Edits} edit{(d.Edits == 1 ? "" : "s")})");
            w.WriteLine($"EXPECT {label}: {d.Subject} — {d.Detail}{edits}");
        }
        w.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"EXPECT edits: {report.Edits} (passes at <= {ExpectReport.PassAt}): {(report.Passes ? "PASS" : "FAIL")}"));
    }

    private static string FileName(string cardRelPath) => cardRelPath[(cardRelPath.LastIndexOf('/') + 1)..];

    private static string? NormalizePath(string? p) => p?.Replace('/', '\\').Trim().TrimEnd('\\');
}
```

- [ ] **Step 4: Run them and watch them pass**

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*ExpectDiffTests*"
```

Expected: `failed: 0, succeeded: 10`, exit code 0.

- [ ] **Step 5: Commit**

```powershell
git add src/UasSort.Cli/ExpectDiff.cs tests/UasSort.Platform.Tests/Cli/ExpectDiffTests.cs
git commit -m "feat: uas-sort-cli --expect edit count (merges, splits, renames; passes at 2)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 12.4 — Reading the expected file; the acceptance template

The CLI may not touch the file system (Ref §2.4 bans file types in Cli), and `IoGuardPolicy` allows reads only under the card, the ledger folder and app data — a user-named JSON file in the repository is none of those. So the expected file is read by a small read-only Platform helper (attributes first; never a placeholder; shared read; size cap), and parsed and validated in the CLI. The template the user fills in before acceptance is checked in beside it.

**Files:**
- Create: `src/UasSort.Platform/Io/ReadOnlyTextFile.cs`
- Create: `src/UasSort.Cli/ExpectedFile.cs`
- Modify: `src/UasSort.Cli/PlanDocument.cs` (register `ExpectedFileJson` in `CliJsonContext`)
- Create: `tests/acceptance/first-card-expected.schema.json`
- Create: `tests/acceptance/first-card-expected.example.json`
- Create: `tests/acceptance/README.md`
- Create: `tests/UasSort.Platform.Tests/Cli/ExpectedFileTests.cs`
- Create: `tests/UasSort.Platform.Tests/Io/ReadOnlyTextFileTests.cs`

**Interfaces:**
- Consumes: `ExpectedFileJson`, `ExpectedFolderJson` (Task 12.3); `CliJsonContext` (Task 12.2).
- Produces (defined here):
  - `public static class UasSort.Platform.Io.ReadOnlyTextFile { public static string Read(string path, int maxBytes); }` — reads the attributes first and throws `IOException` for a cloud-only placeholder (`0x400000`, `0x40000`, `0x1000`), a folder or a file over `maxBytes`; opens with `FileMode.Open`, `FileAccess.Read`, `FileShare.ReadWrite | FileShare.Delete`; UTF-8 with BOM detection. Missing file → `FileNotFoundException`.
  - `internal static class ExpectedFile { public const int MaxBytes = 1_000_000; public static ExpectedFileJson Parse(string json); }` — throws `FormatException` (message names the problem) for invalid JSON, no folders, an empty `relPath`, an empty clip list, or a clip that is not a bare file name.
  - `tests/acceptance/first-card-expected.schema.json` (JSON Schema 2020-12), `first-card-expected.example.json`, `README.md`.

- [ ] **Step 1: Write the failing tests**

`tests/UasSort.Platform.Tests/Io/ReadOnlyTextFileTests.cs`:

```csharp
using System.Text;

namespace UasSort.Platform.Tests.Io;

public sealed class ReadOnlyTextFileTests : IDisposable
{
    private readonly TestTempDir _temp = new();
    private string _dir => _temp.FullPath;

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        _temp.Dispose();
    }

    [Fact]
    public void Reads_Utf8WithBom_WhileAnotherHandleWrites()
    {
        string path = Path.Combine(_dir, "e.json");
        File.WriteAllText(path, "{ \"folders\": [] } · ok", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Equal("{ \"folders\": [] } · ok", ReadOnlyTextFile.Read(path, 1000));
    }

    [Fact]
    public void Refuses_ACloudOnlyPlaceholder_WithoutOpeningIt()
    {
        string path = Path.Combine(_dir, "cloud.json");
        File.WriteAllText(path, "{}");
        File.SetAttributes(path, FileAttributes.Offline);
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);   // any open would fail differently
        var ex = Assert.Throws<IOException>(() => ReadOnlyTextFile.Read(path, 1000));
        Assert.Contains("cloud-only", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_AFileOverTheCap()
    {
        string path = Path.Combine(_dir, "big.json");
        File.WriteAllText(path, new string('x', 2000));
        var ex = Assert.Throws<IOException>(() => ReadOnlyTextFile.Read(path, 1000));
        Assert.Contains("larger than", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_AFolder_And_ReportsAMissingFile()
    {
        Assert.Throws<IOException>(() => ReadOnlyTextFile.Read(_dir, 1000));
        Assert.Throws<FileNotFoundException>(() => ReadOnlyTextFile.Read(Path.Combine(_dir, "missing.json"), 1000));
    }
}
```

`tests/UasSort.Platform.Tests/Cli/ExpectedFileTests.cs`:

```csharp
using System.Text.Json;
using UasSort.Cli;

namespace UasSort.Platform.Tests.Cli;

public sealed class ExpectedFileTests
{
    private static string Acceptance(string name) => RepoPaths.Of($"tests/acceptance/{name}");

    [Fact]
    public void TheCheckedInExample_Parses()
    {
        var e = ExpectedFile.Parse(File.ReadAllText(Acceptance("first-card-expected.example.json")));
        Assert.Equal(2, e.Folders.Count);
        Assert.Equal(@"2026\2026-10\2026-10-04 Nome Roads", e.Folders[0].RelPath);
        Assert.Equal(3, e.Folders[0].Clips.Count);
        Assert.Equal(GroupTargetKind.NewFolder, e.Folders[1].Target);
    }

    [Fact]
    public void TheSchema_DescribesTheSameProperties()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Acceptance("first-card-expected.schema.json")));
        var folder = schema.RootElement.GetProperty("properties").GetProperty("folders").GetProperty("items");
        Assert.Equal(new[] { "relPath", "clips", "target" }, folder.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "relPath", "clips" }, folder.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Equal(Enum.GetNames<GroupTargetKind>(),
                     folder.GetProperty("properties").GetProperty("target").GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    [Fact]
    public void CommentsAndTrailingCommas_AreAccepted()
    {
        var e = ExpectedFile.Parse("""
            // written by hand
            { "folders": [ { "relPath": "2026\\2026-10\\2026-10-04 Nome Roads", "clips": [ "DJI_20261004163012_0151_D.MP4", ], }, ] }
            """);
        Assert.Single(e.Folders);
        Assert.Null(e.Folders[0].Target);
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("{ }", "no folders")]
    [InlineData("{ \"folders\": [] }", "no folders")]
    [InlineData("{ \"folders\": [ { \"relPath\": \" \", \"clips\": [\"a.MP4\"] } ] }", "empty relPath")]
    [InlineData("{ \"folders\": [ { \"relPath\": \"x\", \"clips\": [] } ] }", "no clips")]
    [InlineData("{ \"folders\": [ { \"relPath\": \"x\", \"clips\": [\"DCIM/DJI_001/a.MP4\"] } ] }", "bare file name")]
    [InlineData("{ \"folders\": [ { \"relPath\": \"x\", \"clips\": [\"a.MP4\"], \"target\": \"Merge\" } ] }", "not valid JSON")]
    public void InvalidFiles_AreFormatErrors(string json, string expected)
    {
        var ex = Assert.Throws<FormatException>(() => ExpectedFile.Parse(json));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*ExpectedFileTests*"
```

Expected: build error `CS0103: The name 'ExpectedFile' does not exist in the current context` and `CS0103: The name 'ReadOnlyTextFile' does not exist`.

- [ ] **Step 3: Implement the reader, the parser and the template**

`src/UasSort.Platform/Io/ReadOnlyTextFile.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace UasSort.Platform.Io;

/// <summary>
/// Reads a small user-named text file (the CLI's --expect file) without ever writing, and without hydrating a
/// cloud-only placeholder: the attributes are read before any open (the PlaceholderGuard rule of Ref §4.3).
/// </summary>
public static class ReadOnlyTextFile
{
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;

    public static string Read(string path, int maxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
#pragma warning disable RS0030 // IO layer: read-only open of a user-named JSON file (CLI --expect); attributes checked first
        FileAttributes attributes = File.GetAttributes(full);   // FileNotFoundException when missing
        if ((attributes & (RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline)) != 0)
            throw new IOException($"'{full}' is a cloud-only placeholder; make it available on this device first");
        if ((attributes & FileAttributes.Directory) != 0)
            throw new IOException($"'{full}' is a folder, not a file");
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                                          bufferSize: 4096, FileOptions.SequentialScan);
        if (stream.Length > maxBytes)
            throw new IOException(string.Create(CultureInfo.InvariantCulture, $"'{full}' is larger than {maxBytes} bytes"));
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
#pragma warning restore RS0030
    }
}
```

`src/UasSort.Cli/ExpectedFile.cs`:

```csharp
using System.Text.Json;

namespace UasSort.Cli;

/// <summary>Parses and validates tests/acceptance/first-card-expected.json (Ref §4.5).</summary>
internal static class ExpectedFile
{
    public const int MaxBytes = 1_000_000;

    public static ExpectedFileJson Parse(string json)
    {
        ExpectedFileJson? file;
        try { file = JsonSerializer.Deserialize(json, CliJsonContext.Default.ExpectedFileJson); }
        catch (JsonException ex) { throw new FormatException($"the expected file is not valid JSON: {ex.Message}", ex); }

        if (file is null || file.Folders is not { Count: > 0 } folders)
            throw new FormatException("the expected file has no folders (\"folders\": [ { \"relPath\": …, \"clips\": [ … ] } ])");
        foreach (var f in folders)
        {
            if (string.IsNullOrWhiteSpace(f.RelPath))
                throw new FormatException("an expected folder has an empty relPath");
            if (f.Clips is not { Count: > 0 })
                throw new FormatException($"expected folder {f.RelPath} has no clips");
            foreach (var clip in f.Clips)
                if (string.IsNullOrWhiteSpace(clip) || clip.Contains('/', StringComparison.Ordinal) || clip.Contains('\\', StringComparison.Ordinal))
                    throw new FormatException($"expected folder {f.RelPath}: clip '{clip}' must be a bare file name such as DJI_20261004163012_0151_D.MP4");
        }
        return file;
    }
}
```

In `src/UasSort.Cli/PlanDocument.cs`, add the expected file to the context (the attribute list above `CliJsonContext` becomes):

```csharp
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
                             WriteIndented = true, PropertyNameCaseInsensitive = true,
                             ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(PlanDocument))]
[JsonSerializable(typeof(ExpectedFileJson))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
```

`tests/acceptance/first-card-expected.schema.json`:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://github.com/uas-sort/tests/acceptance/first-card-expected.schema.json",
  "title": "uas-sort first-card expected folder list",
  "description": "The folders the user expects uas-sort-cli plan to propose for the first real card (Ref §4.5, §13). Compared by uas-sort-cli plan --expect; acceptance passes at 2 edits or fewer.",
  "type": "object",
  "required": [ "folders" ],
  "properties": {
    "$schema": { "type": "string" },
    "folders": {
      "type": "array",
      "minItems": 1,
      "items": {
        "type": "object",
        "properties": {
          "relPath": {
            "type": "string",
            "minLength": 1,
            "description": "Folder path relative to the video root, e.g. 2026\\2026-10\\2026-10-04 Nome Roads (an existing folder for an append)."
          },
          "clips": {
            "type": "array",
            "minItems": 1,
            "items": { "type": "string", "pattern": "^[^/\\\\]+$" },
            "description": "Bare file names of the card's MP4 clips that belong in this folder."
          },
          "target": {
            "enum": [ "NewFolder", "Append", "AlreadyImported", "NothingToCopy", "SkipGroup" ],
            "description": "Optional: the target kind you expect. When given, a different kind counts as one edit (a Retarget)."
          }
        },
        "required": [ "relPath", "clips" ],
        "additionalProperties": false
      }
    }
  },
  "additionalProperties": false
}
```

`tests/acceptance/first-card-expected.example.json` (an example only: the clip names are invented; the user writes the real `first-card-expected.json` from the real card before acceptance):

```json
{
  "$schema": "./first-card-expected.schema.json",
  "folders": [
    {
      "relPath": "2026\\2026-10\\2026-10-04 Nome Roads",
      "clips": [ "DJI_20261004163012_0151_D.MP4", "DJI_20261004164530_0152_D.MP4", "DJI_20261004170105_0153_D.MP4" ]
    },
    {
      "relPath": "2026\\2026-10\\2026-10-05 Safety Roadhouse",
      "clips": [ "DJI_20261005121500_0154_D.MP4" ],
      "target": "NewFolder"
    }
  ]
}
```

`tests/acceptance/README.md`:

````markdown
# First-card acceptance: the expected folder list

Before the first-real-card acceptance (design reference §13, step 2), write down where you expect each clip on the card to go, **before** you run the dry run, so the comparison is honest.

1. Copy `first-card-expected.example.json` to `first-card-expected.json` in this folder.
2. For each folder you expect, give its path relative to the video root (`relPath`, e.g. `2026\\2026-10\\2026-10-04 Nome Roads` in JSON; for an append, the existing folder's path) and the file names of the MP4 clips that belong in it (`clips`, bare names such as `DJI_20261004163012_0151_D.MP4`). Optionally add `"target": "NewFolder"` or `"target": "Append"` if you want the target kind compared too. Clips you leave out are not compared. The file may contain `//` comments and trailing commas; `first-card-expected.schema.json` describes it for editors.
3. With the card in the reader (here `E:\`), run:

   ```powershell
   dotnet run --project src/UasSort.Cli -- plan --card E:\ --json --expect tests\acceptance\first-card-expected.json
   ```

   The JSON plan goes to stdout; the comparison goes to stderr (to stdout without `--json`) and ends with a line like `EXPECT edits: 1 (passes at <= 2): PASS`. The dry run writes nothing anywhere.

**How edits are counted** (one edit = one change you would make in Review, a `PlanEdit`):
- an expected folder whose clips are spread over k plan groups: k − 1 merges;
- a plan group holding clips of k expected folders: k − 1 splits or moves;
- 1 for each expected folder whose best-matching plan group has a different folder path (a rename) or, when you gave `target`, a different target kind (a retarget).

Expected clips that are not on the card are listed but not counted. Step 2 passes at **2 edits or fewer**. Also note the scan + plan time printed on stderr (success criterion 4).
````

- [ ] **Step 4: Run them and watch them pass**

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*ExpectedFileTests*"
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*ReadOnlyTextFileTests*"
```

Expected: `failed: 0, succeeded: 10` and `failed: 0, succeeded: 4`, exit code 0 each.

- [ ] **Step 5: Commit**

```powershell
git add src/UasSort.Platform/Io/ReadOnlyTextFile.cs src/UasSort.Cli/ExpectedFile.cs src/UasSort.Cli/PlanDocument.cs tests/acceptance tests/UasSort.Platform.Tests/Cli/ExpectedFileTests.cs tests/UasSort.Platform.Tests/Io/ReadOnlyTextFileTests.cs
git commit -m "feat: expected folder list for --expect (read-only reader, parser, acceptance template)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 12.5 — `PlanCommand`: flow, settings merge, output routing, exit codes

`PlanCommand` runs the contract of Ref §4.5 against an `ICliHost`, the one seam through which the CLI reaches Core and Platform. This task tests every path that ends before a plan exists (exit 1 and 2, the settings merge, the read-only settings note for a missing (decision 46) or unreadable settings file, the expected file read before the scan) with a fake host; Task 12.6 adds the Windows host and the end-to-end run. It also adds the plan-to-document mapper that Task 12.6's run exercises.

**Files:**
- Create: `src/UasSort.Cli/ICliHost.cs`
- Create: `src/UasSort.Cli/PlanCommand.cs`
- Create: `src/UasSort.Cli/PlanDocumentMapper.cs`
- Create: `tests/UasSort.Platform.Tests/Cli/PlanCommandTests.cs`

**Interfaces:**
- Consumes (Ref §3, §4): `Settings`, `SettingsLoad`, `CardSource`, `CardIdentity`, `CardSourceCheck` / `SourceOk` / `SourceRefused`, `ScanResult`, `ScanProgress`, `ScanPhase`, `Plan`, `Tuning`, `VideoGroup`, `GroupTarget` cases (`NewFolder`, `Append`, `AlreadyImported`, `NothingToCopy`, `SkipGroup`), `Newness` cases (`IsNew`, `Imported`, `Decided`, `ProbablyImported`, `Conflict`), `Item`, `ItemFlags`, `Boundary`, `DaySplit`, `PhotoDay`, `SetPlacement`, `SetUnit`, `CardEntry`, `EntryClass`, `Issue`; Tasks 12.1–12.4.
- Produces (defined here):

```csharp
internal interface ICliHost
{
    string AppDataDir { get; }                                              // %LOCALAPPDATA%\uas-sort (never written)
    SettingsLoad LoadSettings(string? settingsPath);                        // ISettingsStore.Load(readOnly: true)
    CardSourceCheck Validate(string cardPath, Settings settings);           // CardSourceValidator, detected = null
    CardIdentity? IdentityFor(string cardRoot);                             // the volume holding the card root, for the JSON
    Task<ScanResult> ScanAsync(CardSource source, Settings settings, IProgress<ScanProgress> progress, CancellationToken ct);
    Plan Plan(ScanResult scan, Tuning tuning, CancellationToken ct);       // Planner.Prepare + Derive with no edits
    string ReadExpectFile(string path);                                     // ReadOnlyTextFile.Read(path, ExpectedFile.MaxBytes)
}
internal static class PlanCommand
{
    public static Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, ICliHost host, CancellationToken ct);
    public static Settings Effective(Settings loaded, CliArgs args);        // command-line values win (Ref §4.5)
}
internal static class PlanDocumentMapper { public static PlanDocument Map(Plan plan, CardIdentity? identity); }
internal sealed class StderrProgress(TextWriter stderr) : IProgress<ScanProgress>;   // one line per phase change, synchronous
```

- [ ] **Step 1: Write the failing tests**

`tests/UasSort.Platform.Tests/Cli/PlanCommandTests.cs`:

```csharp
using UasSort.Cli;

namespace UasSort.Platform.Tests.Cli;

public sealed class PlanCommandTests
{
    private static Settings Defaults() => new(
        Schema: 1, VideoRoot: @"C:\Users\x\Pictures\UAS Videos", PhotoRoot: @"C:\Users\x\Pictures\UAS Videos\Picture Offload",
        PreviousPhotoRoots: ImmutableArray<string>.Empty, RadiusMiles: 50, GapDays: 1, DroneClockMode: StoredClockMode.Zone,
        DroneClockZone: "America/New_York", CopyJpgTwin: true,
        Map: new MapSettings("streets", "s", "sd", "sat", ImmutableDictionary<string, string>.Empty),
        Layout: new LayoutSettings(380, 0.45), RootsConfirmed: false);

    private sealed class FakeHost : ICliHost
    {
        // A missing settings file (registry decision 46): the derived defaults, Recovered = false, RootsConfirmed = false.
        public SettingsLoad Load { get; init; } = new(Defaults(), Recovered: false, CorruptCopyPath: null, RootsFromLastRun: null);
        public CardSourceCheck Check { get; init; } = new SourceRefused("No DCIM folder here");
        public Exception? ScanThrows { get; init; }
        public string ExpectText { get; init; } = "";
        public string? LoadedFrom { get; private set; } = "(not called)";
        public Settings? ValidatedWith { get; private set; }
        public bool Scanned { get; private set; }
        public string AppDataDir => @"C:\Users\x\AppData\Local\uas-sort";

        public SettingsLoad LoadSettings(string? settingsPath) { LoadedFrom = settingsPath; return Load; }
        public CardSourceCheck Validate(string cardPath, Settings settings) { ValidatedWith = settings; return Check; }
        public CardIdentity? IdentityFor(string cardRoot) => null;
        public Task<ScanResult> ScanAsync(CardSource source, Settings settings, IProgress<ScanProgress> progress, CancellationToken ct)
        {
            Scanned = true;
            throw ScanThrows ?? new InvalidOperationException("the fake host cannot scan");
        }
        public Plan Plan(ScanResult scan, Tuning tuning, CancellationToken ct) => throw new InvalidOperationException("not reached");
        public string ReadExpectFile(string path) => ExpectText;
    }

    private static async Task<(int Code, string Out, string Err)> Run(FakeHost host, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = await PlanCommand.RunAsync(args, stdout, stderr, host, TestContext.Current.CancellationToken);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task BadArguments_Exit2_WithUsageOnStderr_AndTouchNothing()
    {
        var host = new FakeHost();
        var (code, stdout, stderr) = await Run(host, "plan", "--radius-mi", "500");
        Assert.Equal(2, code);
        Assert.Empty(stdout);
        Assert.Contains("usage: uas-sort-cli plan", stderr, StringComparison.Ordinal);
        Assert.Equal("(not called)", host.LoadedFrom);
    }

    [Fact]
    public async Task RefusedSource_Exit1_WithTheReasonOnStderr()
    {
        var host = new FakeHost { Check = new SourceRefused("This is part of your library (or a synced folder); uas-sort only offloads from cards.") };
        var (code, stdout, stderr) = await Run(host, "plan", "--card", @"C:\Users\x\Pictures\UAS Videos");
        Assert.Equal(1, code);
        Assert.Empty(stdout);
        Assert.Contains("refused: This is part of your library", stderr, StringComparison.Ordinal);
        Assert.False(host.Scanned);
    }

    [Fact]
    public async Task ScanIoError_Exit2()
    {
        var host = new FakeHost
        {
            Check = new SourceOk(new CardSource(@"E:\", null, IsBrowsedFolder: true, IsWriteProtected: false)),
            ScanThrows = new IOException("The device is not ready."),
        };
        var (code, stdout, stderr) = await Run(host, "plan", "--card", @"E:\");
        Assert.Equal(2, code);
        Assert.Empty(stdout);
        Assert.Contains("error: IOException: The device is not ready.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BadExpectFile_Exit2_BeforeAnyScan()
    {
        var host = new FakeHost
        {
            Check = new SourceOk(new CardSource(@"E:\", null, true, false)),
            ExpectText = "{ \"folders\": [] }",
        };
        var (code, _, stderr) = await Run(host, "plan", "--card", @"E:\", "--expect", @"C:\e.json");
        Assert.Equal(2, code);
        Assert.Contains("no folders", stderr, StringComparison.Ordinal);
        Assert.False(host.Scanned);
    }

    [Fact]
    public async Task SettingsPath_IsPassedThrough_AndDefaultsAreAnnouncedOnStderr()
    {
        var host = new FakeHost();
        var (_, _, stderr) = await Run(host, "plan", "--card", @"E:\", "--settings", @"C:\s\settings.json");
        Assert.Equal(@"C:\s\settings.json", host.LoadedFrom);
        Assert.Contains("derived defaults", stderr, StringComparison.Ordinal);
        Assert.Contains("nothing was written", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreadableSettings_AreAnnounced_ConfirmedSettingsAreNot()
    {
        var unreadable = new FakeHost { Load = new(Defaults(), Recovered: true, CorruptCopyPath: null, RootsFromLastRun: null) };
        var (_, _, stderrUnreadable) = await Run(unreadable, "plan", "--card", @"E:\");
        Assert.Contains("settings file is unreadable; using the derived defaults", stderrUnreadable, StringComparison.Ordinal);

        var confirmed = new FakeHost { Load = new(Defaults() with { RootsConfirmed = true }, Recovered: false, CorruptCopyPath: null, RootsFromLastRun: null) };
        var (_, _, stderrConfirmed) = await Run(confirmed, "plan", "--card", @"E:\");
        Assert.DoesNotContain("settings:", stderrConfirmed, StringComparison.Ordinal);
        Assert.Contains("roots: video", stderrConfirmed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandLineValues_Win_OverSettings()
    {
        var host = new FakeHost();
        await Run(host, "plan", "--card", @"E:\", "--video-root", @"C:\t\video", "--photo-root", @"C:\t\photo",
                  "--radius-mi", "25", "--gap-days", "0");
        var s = host.ValidatedWith!;
        Assert.Equal(@"C:\t\video", s.VideoRoot);
        Assert.Equal(@"C:\t\photo", s.PhotoRoot);
        Assert.Equal(25, s.RadiusMiles);
        Assert.Equal(0, s.GapDays);
        Assert.Equal("America/New_York", s.DroneClockZone);   // everything else comes from settings
    }

    [Fact]
    public void Effective_KeepsSettingsWhenNoOverrides()
    {
        var s = Defaults();
        Assert.Equal(s, PlanCommand.Effective(s, new CliArgs(@"E:\", null, null, null, null, null, false, null)));
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*PlanCommandTests*"
```

Expected: build error `CS0246: The type or namespace name 'ICliHost' could not be found`.

- [ ] **Step 3: Implement the host seam, the command and the mapper**

`src/UasSort.Cli/ICliHost.cs`:

```csharp
namespace UasSort.Cli;

/// <summary>
/// Everything the CLI needs from Core and Platform. WindowsCliHost is the only implementation that constructs services;
/// none of its members writes anything (no settings, drafts, ledger, reports, logs or files; Ref §4.5).
/// </summary>
internal interface ICliHost
{
    string AppDataDir { get; }
    SettingsLoad LoadSettings(string? settingsPath);
    CardSourceCheck Validate(string cardPath, Settings settings);
    CardIdentity? IdentityFor(string cardRoot);
    Task<ScanResult> ScanAsync(CardSource source, Settings settings, IProgress<ScanProgress> progress, CancellationToken ct);
    Plan Plan(ScanResult scan, Tuning tuning, CancellationToken ct);
    string ReadExpectFile(string path);
}
```

`src/UasSort.Cli/PlanCommand.cs`:

```csharp
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace UasSort.Cli;

/// <summary>`uas-sort-cli plan` (Ref §4.5): validate → scan (ledger read only) → Prepare → Derive with no edits → print.</summary>
internal static class PlanCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, ICliHost host,
                                           CancellationToken ct)
    {
        if (!CliArgs.TryParse(args, out var cli, out var argError))
        {
            stderr.WriteLine($"error: {argError}");
            stderr.WriteLine(CliArgs.Usage);
            return 2;
        }

#pragma warning disable CA1031 // CLI boundary: every failure other than a refused source is exit code 2 with its message
        try
        {
            var watch = Stopwatch.StartNew();
            ExpectedFileJson? expected = cli.ExpectPath is { } expectPath
                ? ExpectedFile.Parse(host.ReadExpectFile(expectPath))
                : null;

            var load = host.LoadSettings(cli.SettingsPath);
            // Part 05's meaning of Recovered (registry decision 46): true only for an unreadable file; a missing file gives the
            // derived defaults with Recovered = false and RootsConfirmed = false.
            if (load.Recovered)
                stderr.WriteLine("settings: the settings file is unreadable; using the derived defaults (nothing was written)");
            else if (!load.Settings.RootsConfirmed)
                stderr.WriteLine("settings: roots not confirmed (settings file missing, so the derived defaults are used, or Setup not finished; nothing was written)");
            var settings = Effective(load.Settings, cli);
            stderr.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"roots: video {settings.VideoRoot} · photo {settings.PhotoRoot} · R {settings.RadiusMiles} mi · G {settings.GapDays}"));

            (CardSource? Source, string? Refusal) check = host.Validate(cli.Card, settings) switch
            {
                SourceOk ok => (ok.Source, null),
                SourceRefused refused => (null, refused.Reason),
            };
            if (check.Source is not { } source)
            {
                stderr.WriteLine($"refused: {check.Refusal}");
                return 1;
            }

            var scan = await host.ScanAsync(source, settings, new StderrProgress(stderr), ct).ConfigureAwait(false);
            var plan = host.Plan(scan, new Tuning(settings.RadiusMiles, settings.GapDays), ct);
            var doc = PlanDocumentMapper.Map(plan, host.IdentityFor(source.Root) ?? source.Identity);

            if (cli.Json)
            {
                stdout.WriteLine(JsonSerializer.Serialize(doc, CliJsonContext.Default.PlanDocument));
            }
            else
            {
                PlanTextRenderer.Render(doc, stdout);
            }
            if (expected is not null)
                ExpectDiff.Write(ExpectDiff.Compare(expected, doc.Groups), cli.Json ? stderr : stdout);

            stderr.WriteLine(string.Create(CultureInfo.InvariantCulture, $"scan + plan: {watch.Elapsed.TotalSeconds:0.0} s"));
            return 0;
        }
        catch (OperationCanceledException)
        {
            stderr.WriteLine("error: cancelled");
            return 2;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.GetType().Name}: {ex.Message}");
            return 2;
        }
#pragma warning restore CA1031
    }

    public static Settings Effective(Settings loaded, CliArgs args) => loaded with
    {
        VideoRoot = args.VideoRoot is { } v ? Path.GetFullPath(v) : loaded.VideoRoot,
        PhotoRoot = args.PhotoRoot is { } p ? Path.GetFullPath(p) : loaded.PhotoRoot,
        RadiusMiles = args.RadiusMiles ?? loaded.RadiusMiles,
        GapDays = args.GapDays ?? loaded.GapDays,
    };
}

/// <summary>Scan progress on stderr, one line per phase; synchronous so lines never interleave with the plan.</summary>
internal sealed class StderrProgress(TextWriter stderr) : IProgress<ScanProgress>
{
    private readonly Lock _gate = new();
    private ScanPhase? _last;

    public void Report(ScanProgress value)
    {
        lock (_gate)
        {
            if (_last == value.Phase) return;
            _last = value.Phase;
            stderr.WriteLine(string.Create(CultureInfo.InvariantCulture, $"scan: {value.Phase} ({value.Total} items)"));
        }
    }
}
```

`src/UasSort.Cli/PlanDocumentMapper.cs`:

```csharp
using System.Globalization;

namespace UasSort.Cli;

/// <summary>Plan → the v1 JSON document of Ref §4.5.</summary>
internal static class PlanDocumentMapper
{
    public static PlanDocument Map(Plan plan, CardIdentity? identity)
    {
        var scan = plan.Base.Scan;
        var inventory = scan.Inventory;
        string videoRoot = scan.Settings.VideoRoot;
        var items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);

        var groups = plan.Groups.Select(g => MapGroup(g, plan, items, videoRoot)).ToList();

        var photoDays = plan.Base.PhotoDays.Select(d => new PhotoDayJson(
            d.Date, d.TzId, d.Items.Length,
            d.Items.Count(id => items.TryGetValue(id, out var it) && it.Newness is IsNew),
            d.Items.Count(id => items.TryGetValue(id, out var it) && it.Newness is ProbablyImported),
            d.Reason)).ToList();

        var setUnits = inventory.Units.OfType<SetUnit>().ToDictionary(u => u.Id);
        var sets = plan.Base.Sets.OrderBy(kv => kv.Key.CardRelPath, StringComparer.Ordinal)
            .Select(kv => new SetJson(kv.Key.CardRelPath, kv.Value.FolderName, kv.Value.Resolution,
                                      setUnits.TryGetValue(kv.Key, out var unit) ? unit.Members.Length : kv.Value.MembersToCopy.Length))
            .ToList();

        var other = inventory.Entries.Where(e => e.Class is EntryClass.Unknown or EntryClass.Skip)
            .Select(e => new OtherJson(e.RelPath, e.Class, e.Rule)).ToList();

        var issues = plan.Issues.Select(i => new IssueJson(i.Severity, i.Code, i.Anchor?.CardRelPath, i.Message, i.RequiresAckAtPreflight))
            .ToList();

        var clock = plan.Base.Clock;
        return new PlanDocument(
            1,
            new CardJson(inventory.Source.Root,
                         identity is { } id
                             ? new IdentityJson(id.VolumeSerial.ToString("X8", CultureInfo.InvariantCulture), id.Label, id.FileSystem, id.TotalBytes)
                             : null,
                         inventory.CameraModel, inventory.Entries.Length, inventory.InventoryHash),
            new SettingsJson(scan.Settings.VideoRoot, scan.Settings.PhotoRoot, plan.Tuning.RadiusMiles, plan.Tuning.GapDays),
            new ClockJson(clock.Mode, clock.ZoneId, clock.SampleCount, new MismatchJson(clock.MismatchItems, clock.MismatchSiteZones)),
            plan.Base.WatermarkUtc is { } w ? DateTime.SpecifyKind(w, DateTimeKind.Utc) : null,
            groups, photoDays, sets, other, issues);
    }

    private static GroupJson MapGroup(VideoGroup g, Plan plan, IReadOnlyDictionary<ItemId, Item> items, string videoRoot)
    {
        (GroupTargetKind Kind, string? RelPath, Confidence? Confidence, string? Why) target = g.Target switch
        {
            NewFolder n => (GroupTargetKind.NewFolder, n.RelPath, null, null),
            Append a => (GroupTargetKind.Append, Rel(videoRoot, a.Folder.FullPath), a.Confidence, a.Why),
            AlreadyImported ai => (GroupTargetKind.AlreadyImported, Rel(videoRoot, ai.Folder.FullPath), null, null),
            NothingToCopy nt => (GroupTargetKind.NothingToCopy, null, null, nt.Summary),
            SkipGroup => (GroupTargetKind.SkipGroup, null, null, null),
        };

        var boundary = plan.Boundaries.FirstOrDefault(b => b.Right == g.Id);
        var videos = g.Videos.Select(id =>
        {
            var item = items[id];
            return new VideoJson(id.CardRelPath, Status(item.Newness), plan.Included.Contains(id),
                                 DateTime.SpecifyKind(item.Time.CaptureUtc, DateTimeKind.Utc), item.Time.LocalDate,
                                 item.Time.Source, Flags(item.Flags));
        }).ToList();
        var daySplits = g.DaySplits.Select(s => new DaySplitJson(s.FirstOfDay.CardRelPath, s.From, s.To,
                                                                 s.Apart is { } d ? Math.Round(d.Miles, 1) : null, s.Emphasised)).ToList();
        var groupIssues = plan.Issues.Where(i => i.Anchor is { } a && g.Videos.Contains(a)).Select(i => i.Code).Distinct().ToList();

        return new GroupJson(g.Id.Anchor.CardRelPath, target.Kind, target.RelPath, target.Confidence, target.Why, g.Start, g.End,
            boundary is null ? null : new BoundaryJson(boundary.Cause, boundary.Jump is { } j ? Math.Round(j.Miles, 1) : null,
                                                       Math.Round(boundary.Gap.TotalHours, 1), boundary.DayGap),
            videos, daySplits, groupIssues);
    }

    private static NewnessStatus Status(Newness n) => n switch
    {
        IsNew => NewnessStatus.New,
        Imported => NewnessStatus.Imported,
        Decided => NewnessStatus.Decided,
        ProbablyImported => NewnessStatus.ProbablyImported,
        Conflict => NewnessStatus.Conflict,
    };

    private static List<string> Flags(ItemFlags flags) =>
        Enum.GetValues<ItemFlags>().Where(f => f != ItemFlags.None && flags.HasFlag(f)).Select(f => f.ToString()).ToList();

    private static string Rel(string videoRoot, string fullPath) => Path.GetRelativePath(videoRoot, fullPath);
}
```

(Part 01's `Program.Main` stays until Task 12.6. The switch expressions over `CardSourceCheck`, `GroupTarget` and `Newness` have no default arm: they rely on the C# 15 union / `closed` exhaustiveness proven across assemblies in Part 01. A CS8509 here means a case was added in Core; map it, never add a discard arm.)

- [ ] **Step 4: Run them and watch them pass**

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*PlanCommandTests*"
```

Expected: `failed: 0, succeeded: 8`, exit code 0.

- [ ] **Step 5: Commit**

```powershell
git add src/UasSort.Cli/ICliHost.cs src/UasSort.Cli/PlanCommand.cs src/UasSort.Cli/PlanDocumentMapper.cs tests/UasSort.Platform.Tests/Cli/PlanCommandTests.cs
git commit -m "feat: uas-sort-cli plan command flow, settings merge, exit codes and plan mapping

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 12.6 — The Windows host, `Main`, and the `%TEMP%` run that writes nothing

The spec's CLI test (Ref §13, §14 step 12): `uas-sort-cli plan --json` runs on a card folder under `%TEMP%` written by `FakeCardWriter` and writes nothing anywhere. The test snapshots, before and after, every `%TEMP%` root it uses (the card, the video root with an empty `.uas-sort`, the photo root, the settings folder) and `%LOCALAPPDATA%\uas-sort` (path, size, last-write time, attributes of every entry; or its absence), and requires them identical. The roots and a non-existent `--settings` file are passed explicitly, so the test never reads this PC's real settings or lists the real library (Global Constraints: never touch user data).

**Files:**
- Create: `src/UasSort.Cli/WindowsCliHost.cs`
- Modify (replace content): `src/UasSort.Cli/Program.cs` (Part 01)
- Create: `tests/UasSort.Testing/FakeCardWriter.cs`
- Create: `tests/UasSort.Platform.Tests/Cli/FakeCardWriterTests.cs`
- Create: `tests/UasSort.Platform.Tests/Cli/CliTestCard.cs`
- Create: `tests/UasSort.Platform.Tests/Cli/TreeSnapshot.cs`
- Create: `tests/UasSort.Platform.Tests/Cli/CliPlanRunTests.cs`
- Create: `tests/UasSort.Platform.Tests/Cli/ProgramTests.cs`

**Interfaces:**
- Consumes: the constructors in the table at the top of this part (Parts 01–09), each Platform one given `Environment.MachineName` as `machine`; `PlaceholderMode.ExposePlaceholders()` (Part 01, `UasSort.Platform.Win32`); `TestTempDir`, `RepoPaths.Of` (Part 01, `UasSort.Testing`); `KnownFolders.AppDataDir()` (Part 09); `ICardSourceValidator.Validate(string, VolumeInfo?, Settings, IDirectoryLister, IPathFacts, string)`, `ISettingsStore.Load(bool readOnly)`, `IVolumeProvider.GetVolumes()`, `IPathFacts.Canonical(string)`, `ScanService.ScanAsync(CardSource, ICardReaderFactory, IProgress<ScanProgress>, CancellationToken)`, `Planner.Prepare(ScanResult)`, `Planner.Derive(PlanBase, Tuning, IReadOnlyList<PlanEdit>, SessionFlags, int, CancellationToken)`, `PlaceIndex.Load(Stream)`, `IAppAssets.OpenPlaces()` (Ref §4); `ReadOnlyTextFile.Read` (Task 12.4); `PlanCommand.RunAsync` (Task 12.5).
- Produces (defined here):
  - `internal sealed class WindowsCliHost : ICliHost` (the only place the CLI constructs Core/Platform services).
  - `public static class Program { public static int Main(string[] args); }` (replaces Part 01's usage-only body) — first calls `PlaceholderMode.ExposePlaceholders()` (Ref §4.3; a negative result is a warning on stderr, as in the App), then sets UTF-8 console output; Ctrl+C cancels (exit 2).
  - `public sealed class UasSort.Testing.FakeCardWriter` (defined here, its registry owner; Ref §2.3): `FakeCardWriter(string cardRoot)` (throws `ArgumentException` unless the root is under `%TEMP%\uas-sort-test-`), `string Root`, `IReadOnlyList<string> RelPaths`, `FakeCardWriter AddDjiVideo(string mediaFolder, DateTime droneStamp, int number, GeoPoint? gps)` (a `SyntheticMp4Builder` clip `DCIM/<mediaFolder>/DJI_<stamp>_<nnnn>_D.MP4`, `mvhd` = stamp + 4 h as UTC, mtime = `mvhd` + 90 s: the Ref §13 fixture convention), `FakeCardWriter AddFile(string cardRelPath, byte[] content, DateTime? mtimeUtc = null)`, `void Write()`, `static readonly DateTime DefaultMtimeUtc`.
  - Test helpers `internal static class CliTestCard { public static void Write(string cardRoot); public const int FileCount = 5; public static readonly string[] Clips; }` and `internal static class TreeSnapshot { public static IReadOnlyList<string> Take(string root); }`.

- [ ] **Step 1: Write the failing end-to-end test**

`tests/UasSort.Platform.Tests/Cli/FakeCardWriterTests.cs`:

```csharp
namespace UasSort.Platform.Tests.Cli;

public sealed class FakeCardWriterTests
{
    [Fact]
    public void RefusesARootOutsideTheTestTempFolders()
    {
        Assert.Throws<ArgumentException>(() => new FakeCardWriter(@"E:"));
        Assert.Throws<ArgumentException>(() => new FakeCardWriter(Path.Combine(Path.GetTempPath(), "not-a-test-dir")));
    }

    [Fact]
    public void WritesDjiNamesSizesAndMtimes()
    {
        using var temp = new TestTempDir();
        string root = temp.Combine("card");
        new FakeCardWriter(root)
            .AddDjiVideo("DJI_001", new DateTime(2026, 9, 27, 14, 1, 27), 123, new GeoPoint(57.5368, -153.7484))
            .AddFile("MISC/FC9113.db", "FC9113"u8.ToArray())
            .Write();
        var clip = new FileInfo(Path.Combine(root, "DCIM", "DJI_001", "DJI_20260927140127_0123_D.MP4"));
        Assert.True(clip.Length > 0);
        Assert.Equal(new DateTime(2026, 9, 27, 18, 2, 57, DateTimeKind.Utc), clip.LastWriteTimeUtc);   // mvhd 18:01:27Z + 90 s
        Assert.Equal(FakeCardWriter.DefaultMtimeUtc, File.GetLastWriteTimeUtc(Path.Combine(root, "MISC", "FC9113.db")));
    }
}
```

`tests/UasSort.Platform.Tests/Cli/CliTestCard.cs` (the only place the test builds a card; three Zachar Bay clips on an Eastern-set drone clock, a DNG-free card, the drone's `MISC` index and an LRF proxy):

```csharp
namespace UasSort.Platform.Tests.Cli;

internal static class CliTestCard
{
    public static readonly GeoPoint Zachar = new(57.5368, -153.7484);
    public const int FileCount = 5;

    public static readonly string[] Clips =
        ["DJI_20260927140127_0123_D.MP4", "DJI_20260927140144_0124_D.MP4", "DJI_20260927142416_0148_D.MP4"];

    /// <summary>Materialises the card under cardRoot (which must be under %TEMP%\uas-sort-test-*).</summary>
    public static void Write(string cardRoot)
    {
        var card = new FakeCardWriter(cardRoot);
        card.AddDjiVideo("DJI_001", new DateTime(2026, 9, 27, 14, 1, 27), 123, Zachar);
        card.AddDjiVideo("DJI_001", new DateTime(2026, 9, 27, 14, 1, 44), 124, Zachar);
        card.AddDjiVideo("DJI_001", new DateTime(2026, 9, 27, 14, 24, 16), 148, Zachar);
        card.AddFile("DCIM/DJI_001/DJI_20260927140127_0123_D.LRF", new byte[512]);
        card.AddFile("MISC/FC9113.db", "FC9113"u8.ToArray());
        card.Write();
    }
}
```

`tests/UasSort.Platform.Tests/Cli/TreeSnapshot.cs`:

```csharp
using System.Globalization;

namespace UasSort.Platform.Tests.Cli;

/// <summary>A listing of every entry under a root: relative path, kind, size, last-write ticks, attributes. Opens no file.</summary>
internal static class TreeSnapshot
{
    public static IReadOnlyList<string> Take(string root)
    {
        var dir = new DirectoryInfo(root);
        if (!dir.Exists) return ["(absent)"];
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };
        return dir.EnumerateFileSystemInfos("*", options)
            .Select(e => string.Create(CultureInfo.InvariantCulture,
                $"{Path.GetRelativePath(root, e.FullName)}|{(e is FileInfo f ? f.Length : -1)}|{e.LastWriteTimeUtc.Ticks}|{(int)e.Attributes}"))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
```

`tests/UasSort.Platform.Tests/Cli/CliPlanRunTests.cs`:

```csharp
using System.Text.Json;
using UasSort.Cli;

namespace UasSort.Platform.Tests.Cli;

public sealed class CliPlanRunTests : IDisposable
{
    private readonly TestTempDir _temp = new();
    private readonly string _dir;
    private readonly string _card;
    private readonly string _video;
    private readonly string _photo;
    private readonly string _settingsDir;
    private readonly string _appData = KnownFolders.AppDataDir();   // %LOCALAPPDATA%\uas-sort, the folder WindowsCliHost names

    public CliPlanRunTests()
    {
        _dir = _temp.FullPath;
        _card = Path.Combine(_dir, "card");
        _video = Path.Combine(_dir, "video");
        _photo = Path.Combine(_dir, "photo");
        _settingsDir = Path.Combine(_dir, "settings");
        CliTestCard.Write(_card);
        Directory.CreateDirectory(Path.Combine(_video, ".uas-sort"));   // Check() → Empty: read, never written
        Directory.CreateDirectory(_photo);
        Directory.CreateDirectory(_settingsDir);
    }

    public void Dispose() => _temp.Dispose();

    private string[] Args(params string[] extra) =>
        ["plan", "--card", _card, "--video-root", _video, "--photo-root", _photo,
         "--settings", Path.Combine(_settingsDir, "settings.json"), .. extra];

    private Dictionary<string, IReadOnlyList<string>> Snapshot() => new()
    {
        ["card"] = TreeSnapshot.Take(_card),
        ["video"] = TreeSnapshot.Take(_video),
        ["photo"] = TreeSnapshot.Take(_photo),
        ["settings"] = TreeSnapshot.Take(_settingsDir),
        ["appData"] = TreeSnapshot.Take(_appData),
    };

    private static async Task<(int Code, string Out, string Err)> Run(string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = await PlanCommand.RunAsync(args, stdout, stderr, new WindowsCliHost(), TestContext.Current.CancellationToken);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private void AssertNothingWritten(Dictionary<string, IReadOnlyList<string>> before)
    {
        var after = Snapshot();
        foreach (var (name, listing) in before)
            Assert.True(listing.SequenceEqual(after[name]),
                $"{name} changed:\n- {string.Join("\n- ", listing.Except(after[name]))}\n+ {string.Join("\n+ ", after[name].Except(listing))}");
    }

    [Fact]
    public async Task PlanJson_OnATempCard_PrintsThePlan_AndWritesNothing()
    {
        var before = Snapshot();

        var (code, stdout, stderr) = await Run(Args("--json"));

        Assert.True(code == 0, $"exit {code}; stderr:\n{stderr}");
        AssertNothingWritten(before);
        Assert.Contains("derived defaults", stderr, StringComparison.Ordinal);
        Assert.Contains("scan + plan:", stderr, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(stdout);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("v").GetInt32());
        Assert.Equal(CliTestCard.FileCount, root.GetProperty("card").GetProperty("files").GetInt32());
        Assert.Matches("^[0-9a-fA-F]{16}$", root.GetProperty("card").GetProperty("inventoryHash").GetString());
        Assert.Equal(_video, root.GetProperty("settings").GetProperty("videoRoot").GetString());

        var group = Assert.Single(root.GetProperty("groups").EnumerateArray());
        Assert.Equal("NewFolder", group.GetProperty("target").GetString());
        Assert.StartsWith(@"2026\2026-09\2026-09-27", group.GetProperty("relPath").GetString(), StringComparison.Ordinal);
        var videos = group.GetProperty("videos").EnumerateArray().ToList();
        Assert.Equal(CliTestCard.Clips, videos.Select(v => Path.GetFileName(v.GetProperty("id").GetString()!)).ToArray());
        Assert.All(videos, v =>
        {
            Assert.Equal("New", v.GetProperty("status").GetString());
            Assert.True(v.GetProperty("included").GetBoolean());
            Assert.Equal("2026-09-27", v.GetProperty("localDate").GetString());
            Assert.Equal("Mvhd", v.GetProperty("timeSource").GetString());
        });
        Assert.Contains(root.GetProperty("other").EnumerateArray(),
                        o => o.GetProperty("relPath").GetString() == "MISC/FC9113.db" && o.GetProperty("class").GetString() == "Skip");
    }

    [Fact]
    public async Task PlanText_OnATempCard_PrintsTheGroupBlock_AndWritesNothing()
    {
        var before = Snapshot();
        var (code, stdout, stderr) = await Run(Args());
        Assert.True(code == 0, $"exit {code}; stderr:\n{stderr}");
        AssertNothingWritten(before);
        Assert.Contains(@"NEW FOLDER 2026\2026-09\2026-09-27", stdout, StringComparison.Ordinal);
        Assert.Contains("3 clips · Sep 27", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFolderWithoutDcim_IsRefused_Exit1_AndWritesNothing()
    {
        string notACard = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(notACard);
        var before = Snapshot();
        var (code, stdout, stderr) = await Run(["plan", "--card", notACard, "--video-root", _video, "--photo-root", _photo,
                                                "--settings", Path.Combine(_settingsDir, "settings.json")]);
        Assert.Equal(1, code);
        Assert.Empty(stdout);
        Assert.Contains("refused: No DCIM folder here", stderr, StringComparison.Ordinal);
        AssertNothingWritten(before);
    }

    [Fact]
    public async Task TheVideoRootAsCard_IsRefused_Exit1()
    {
        Directory.CreateDirectory(Path.Combine(_video, "DCIM", "DJI_001"));   // anchors at the video root itself
        var (code, stdout, stderr) = await Run(["plan", "--card", _video, "--video-root", _video, "--photo-root", _photo,
                                                "--settings", Path.Combine(_settingsDir, "settings.json")]);
        Assert.Equal(1, code);
        Assert.Empty(stdout);
        Assert.Contains("refused: This is part of your library", stderr, StringComparison.Ordinal);
    }
}
```

`tests/UasSort.Platform.Tests/Cli/ProgramTests.cs` (a source check: running `Main` inside the test host would change the host's console encoding and install a Ctrl+C handler):

```csharp
namespace UasSort.Platform.Tests.Cli;

/// <summary>Ref §4.3: Program.Main calls PlaceholderMode.ExposePlaceholders() as its first statement, before anything else runs.</summary>
public sealed class ProgramTests
{
    [Fact]
    public void Main_ExposesPlaceholdersFirst_ThenSetsTheEncoding_ThenRunsThePlanCommand()
    {
        string source = File.ReadAllText(RepoPaths.Of("src/UasSort.Cli/Program.cs"));
        int main = source.IndexOf("public static int Main(string[] args)", StringComparison.Ordinal);
        Assert.True(main >= 0, "Program.Main not found");
        int bodyStart = source.IndexOf('{', main) + 1;
        int expose = source.IndexOf("PlaceholderMode.ExposePlaceholders()", bodyStart, StringComparison.Ordinal);
        int encoding = source.IndexOf("Console.OutputEncoding", bodyStart, StringComparison.Ordinal);
        int run = source.IndexOf("PlanCommand.RunAsync(", bodyStart, StringComparison.Ordinal);

        Assert.True(expose > 0, "Program.Main must call PlaceholderMode.ExposePlaceholders()");
        Assert.DoesNotContain(";", source[bodyStart..expose], StringComparison.Ordinal);   // it is the first statement
        Assert.True(encoding > expose, "the console encoding is set after ExposePlaceholders()");
        Assert.True(run > encoding, "PlanCommand.RunAsync runs after ExposePlaceholders() and the encoding");
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*CliPlanRunTests*"
```

Expected: build errors `CS0246: The type or namespace name 'WindowsCliHost' could not be found` and `'FakeCardWriter' could not be found`. (Once they compile, `ProgramTests` still fails against Part 01's stub `Program.cs` with "Program.Main must call PlaceholderMode.ExposePlaceholders()" until Step 3 replaces it.)

- [ ] **Step 3: Implement the Windows host and `Main`**

`tests/UasSort.Testing/FakeCardWriter.cs`:

```csharp
using System.Globalization;

namespace UasSort.Testing;

/// <summary>
/// Materialises a DJI card folder tree on real disk for the Windows tests and the CLI run (Ref §2.3), only under
/// %TEMP%\uas-sort-test-*. Videos are SyntheticMp4Builder clips DJI_{stamp}_{n}_D.MP4 whose mvhd is the drone stamp
/// + 4 h as UTC (an Eastern-set drone clock in summer, the Ref §13 fixture convention) and whose mtime is mvhd + 90 s.
/// </summary>
public sealed class FakeCardWriter
{
    public static readonly DateTime DefaultMtimeUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly List<(string RelPath, byte[] Bytes, DateTime MtimeUtc)> _files = [];

    public FakeCardWriter(string cardRoot)
    {
        string full = Path.GetFullPath(cardRoot);
        string allowed = Path.Join(Path.GetFullPath(Path.GetTempPath()), "uas-sort-test-");
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"FakeCardWriter writes only under %TEMP%\\uas-sort-test-*; refused '{full}'", nameof(cardRoot));
        Root = full;
    }

    public string Root { get; }

    public IReadOnlyList<string> RelPaths => _files.Select(f => f.RelPath).ToList();

    public FakeCardWriter AddDjiVideo(string mediaFolder, DateTime droneStamp, int number, GeoPoint? gps)
    {
        var mvhdUtc = DateTime.SpecifyKind(droneStamp.AddHours(4), DateTimeKind.Utc);
        var builder = gps is { } at
            ? new SyntheticMp4Builder().WithMvhdUtc(mvhdUtc).WithDjmdGps("dvtm_Air3s.proto", at)
            : new SyntheticMp4Builder { WithDjmdTrack = false }.WithMvhdUtc(mvhdUtc);
        string name = string.Create(CultureInfo.InvariantCulture, $"DJI_{droneStamp:yyyyMMddHHmmss}_{number:0000}_D.MP4");
        _files.Add(($"DCIM/{mediaFolder}/{name}", builder.Build(), mvhdUtc.AddSeconds(90)));
        return this;
    }

    public FakeCardWriter AddFile(string cardRelPath, byte[] content, DateTime? mtimeUtc = null)
    {
        _files.Add((cardRelPath, content, mtimeUtc ?? DefaultMtimeUtc));
        return this;
    }

    public void Write()
    {
#pragma warning disable RS0030 // test fixture: materialises a fake card under %TEMP%\uas-sort-test-* only
        Directory.CreateDirectory(Root);
        foreach (var (rel, bytes, mtime) in _files)
        {
            string path = Path.Join(Root, rel.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, mtime);
        }
#pragma warning restore RS0030
    }
}
```

The host builds every service with the registry constructors (the table at the top of this part: `ScanService` and `Planner` from Part 06, the Windows classes from Part 09) and passes `Environment.MachineName` as `machine` to each Platform constructor (registry decision 32).

`src/UasSort.Cli/WindowsCliHost.cs`:

```csharp
namespace UasSort.Cli;

/// <summary>
/// The CLI's composition root: the same validator, lister, path facts, card reader, ledger store (Check + Load only),
/// guard and planner as the app. Nothing here writes: settings are loaded read-only, the ledger is never opened for
/// append, no folder is ensured or pinned, no snapshot is taken, and no log file is used (logs go to stderr).
/// </summary>
internal sealed class WindowsCliHost : ICliHost
{
    private static readonly string Machine = Environment.MachineName;   // passed explicitly as `machine` (registry decision 32)
    private readonly WindowsDirectoryLister _lister = new();
    private readonly PathFacts _facts = new();
    private readonly WindowsVolumeProvider _volumes = new();
    private readonly GeoTimeZoneResolver _tz = new();
    private readonly Lazy<IPlaceIndex?> _places = new(LoadPlaces);

    public string AppDataDir { get; } = KnownFolders.AppDataDir();

    public SettingsLoad LoadSettings(string? settingsPath)
    {
        string pictures = KnownFolders.Pictures();
        var store = settingsPath is { } p
            ? SettingsStore.ForFile(Path.GetFullPath(p), AppDataDir, pictures, Machine, _facts, _lister, TimeProvider.System)
            : new SettingsStore(AppDataDir, pictures, Machine, _facts, _lister, TimeProvider.System);
        return store.Load(readOnly: true);
    }

    public CardSourceCheck Validate(string cardPath, Settings settings) =>
        new CardSourceValidator().Validate(Path.GetFullPath(cardPath), detected: null, settings, _lister, _facts, AppDataDir);

    public CardIdentity? IdentityFor(string cardRoot)
    {
        string canonical = _facts.Canonical(cardRoot);
        return _volumes.GetVolumes()
            .Where(v => canonical.StartsWith(v.Root, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.Root.Length)
            .Select(v => v.Identity)
            .FirstOrDefault();
    }

    public Task<ScanResult> ScanAsync(CardSource source, Settings settings, IProgress<ScanProgress> progress, CancellationToken ct)
    {
        var ledger = new LedgerStore(settings, AppDataDir, Machine, _facts, _lister, TimeProvider.System);   // Check() + Load() only
        var scan = new ScanService(settings, _lister, ledger, _volumes, _tz, TimeZoneInfo.Local, TimeProvider.System);
        var readers = new WindowsCardReaderFactory(settings, AppDataDir, Machine, _facts, _lister);
        return scan.ScanAsync(source, readers, progress, ct);
    }

    public Plan Plan(ScanResult scan, Tuning tuning, CancellationToken ct)
    {
        var planner = new Planner(_tz, _places.Value, TimeZoneInfo.Local, TimeProvider.System);
        PlanBase b = planner.Prepare(scan);
        return planner.Derive(b, tuning, [], new SessionFlags(LedgerIssuesAccepted: false), revision: 1, ct);
    }

    public string ReadExpectFile(string path) => ReadOnlyTextFile.Read(path, ExpectedFile.MaxBytes);

    private static IPlaceIndex? LoadPlaces()
    {
        try
        {
            using Stream gz = new AppAssets(AppContext.BaseDirectory, typeof(WindowsCliHost).Assembly).OpenPlaces();
            return PlaceIndex.Load(gz);
        }
        catch (FileNotFoundException)
        {
            return null;   // no places.bin.gz beside the exe: descriptions come only from the library and the ledger
        }
    }
}
```

`src/UasSort.Cli/Program.cs` (replaces Part 01's usage-only body; `PlaceholderMode` is Part 01's, `UasSort.Platform.Win32`):

```csharp
using System.Globalization;
using System.Text;

namespace UasSort.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        sbyte previousMode = PlaceholderMode.ExposePlaceholders();   // Ref §4.3: first, before any file-system access
        Console.OutputEncoding = Encoding.UTF8;
        if (previousMode < 0)
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"warning: RtlSetProcessPlaceholderCompatibilityMode failed ({previousMode}); cloud placeholders may not show as placeholders"));
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        return PlanCommand.RunAsync(args, Console.Out, Console.Error, new WindowsCliHost(), cts.Token).GetAwaiter().GetResult();
    }
}
```

- [ ] **Step 4: Run it and watch it pass**

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*CliPlanRunTests*"
```

Then the writer's own tests:

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*FakeCardWriterTests*"
```

And the `Main` source check:

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-class "*Cli.ProgramTests*"
```

Expected: `failed: 0, succeeded: 4`, `failed: 0, succeeded: 2` and `failed: 0, succeeded: 1`, exit code 0 each. If `PlanJson_OnATempCard...` fails with a "changed" message, the listed entries are what the CLI wrote — that is a bug to fix at its source (a store or service writing during a read-only run), never by loosening the snapshot. If `TheVideoRootAsCard_IsRefused` fails, `CardSourceValidator` (Part 02) is not refusing the overlap; fix it there.

Then the whole Platform test project, to catch interactions:

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj
```

Expected: `failed: 0`.

- [ ] **Step 5: Commit**

```powershell
git add src/UasSort.Cli/WindowsCliHost.cs src/UasSort.Cli/Program.cs tests/UasSort.Testing/FakeCardWriter.cs tests/UasSort.Platform.Tests/Cli/FakeCardWriterTests.cs tests/UasSort.Platform.Tests/Cli/CliTestCard.cs tests/UasSort.Platform.Tests/Cli/TreeSnapshot.cs tests/UasSort.Platform.Tests/Cli/CliPlanRunTests.cs tests/UasSort.Platform.Tests/Cli/ProgramTests.cs
git commit -m "feat: uas-sort-cli Windows host and main; the %TEMP% card run writes nothing

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 12.7 — `--expect` end to end, and the command-line smoke run

**Files:**
- Modify: `tests/UasSort.Platform.Tests/Cli/CliPlanRunTests.cs` (append two tests)

**Interfaces:**
- Consumes: `CliPlanRunTests` fixture (Task 12.6), `CliTestCard.Clips`, `ExpectDiff.Write` output format (Task 12.3).
- Produces: the `--expect` run verified through `WindowsCliHost` and `ReadOnlyTextFile`, with the report on stderr under `--json` (stdout stays pure JSON) and on stdout otherwise.

- [ ] **Step 1: Write the failing tests** (append inside `CliPlanRunTests`)

```csharp
    private string WriteExpected(string relPath, params string[] clips)
    {
        string path = Path.Combine(_settingsDir, "first-card-expected.json");
        string clipList = string.Join(", ", clips.Select(c => $"\"{c}\""));
        File.WriteAllText(path, $$"""{ "folders": [ { "relPath": "{{relPath.Replace(@"\", @"\\", StringComparison.Ordinal)}}", "clips": [ {{clipList}} ] } ] }""");
        return path;
    }

    [Fact]
    public async Task Expect_WithJson_ReportsOnStderr_KeepsStdoutPureJson_AndWritesNothing()
    {
        // The plan's folder is 2026\2026-09\2026-09-27 plus whatever description the place index suggests; the user expects
        // "Zachar Bay". With no place index that is one rename; with it, zero. Either way the dry run passes (<= 2).
        string expected = WriteExpected(@"2026\2026-09\2026-09-27 Zachar Bay", CliTestCard.Clips);
        var before = Snapshot();

        var (code, stdout, stderr) = await Run(Args("--json", "--expect", expected));

        Assert.True(code == 0, $"exit {code}; stderr:\n{stderr}");
        AssertNothingWritten(before);
        using var _ = JsonDocument.Parse(stdout);   // stdout is still exactly one JSON document
        Assert.Matches(@"EXPECT edits: [01] \(passes at <= 2\): PASS", stderr);
        Assert.DoesNotContain("EXPECT", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expect_WithoutJson_ReportsOnStdout_AndCountsASplit()
    {
        // The user expects the first clip on its own: one split, plus a rename for each folder whose path differs.
        string path = Path.Combine(_settingsDir, "two-folders.json");
        File.WriteAllText(path, $$"""
            { "folders": [
                { "relPath": "2026\\2026-09\\2026-09-27 Zachar Bay", "clips": [ "{{CliTestCard.Clips[0]}}" ] },
                { "relPath": "2026\\2026-09\\2026-09-27 Zachar Bay 2", "clips": [ "{{CliTestCard.Clips[1]}}", "{{CliTestCard.Clips[2]}}" ] } ] }
            """);

        var (code, stdout, _) = await Run(Args("--expect", path));

        Assert.Equal(0, code);
        Assert.Contains("EXPECT split/move:", stdout, StringComparison.Ordinal);
        Assert.Matches(@"EXPECT edits: [23] \(passes at <= 2\): (PASS|FAIL)", stdout);
    }
```

- [ ] **Step 2: Run them and watch them fail — or pass for the right reason**

```powershell
dotnet test --project tests/UasSort.Platform.Tests/UasSort.Platform.Tests.csproj -- --filter-method "*Expect_*"
```

Expected: both tests pass already if Tasks 12.3–12.6 are wired correctly (the `--expect` path was built test-first in 12.3–12.5). To see them fail for the right reason, temporarily change `cli.Json ? stderr : stdout` in `PlanCommand.RunAsync` to `stdout`, re-run, and confirm `Expect_WithJson_...` fails at `JsonDocument.Parse` ('E' is an invalid start of a value / additional text) — then restore the line and re-run: `failed: 0, succeeded: 2`.

- [ ] **Step 3: Smoke-run the real executable from the command line**

```powershell
dotnet build src/UasSort.Cli/UasSort.Cli.csproj -tl:off
dotnet run --project src/UasSort.Cli --no-build -- plan
$LASTEXITCODE
$empty = Join-Path $env:TEMP ("uas-sort-test-" + [guid]::NewGuid().ToString("N")); New-Item -ItemType Directory $empty | Out-Null
dotnet run --project src/UasSort.Cli --no-build -- plan --card $empty --video-root "$empty\v" --photo-root "$empty\p" --settings "$empty\none.json"
$LASTEXITCODE
Get-ChildItem $empty -Force | Measure-Object | Select-Object -ExpandProperty Count
Remove-Item $empty -Recurse -Force
Get-ChildItem src\UasSort.Cli\bin -Recurse -Filter uas-sort-cli.exe | Select-Object -First 1 -ExpandProperty Name
```

Expected, in order: `error: --card <path> is required` and the usage on stderr, then `2`; `settings: roots not confirmed (settings file missing, so the derived defaults are used …`, `roots: …` and `refused: No DCIM folder here` on stderr, then `1`; `0` (the CLI created nothing in the empty folder); `uas-sort-cli.exe` (`AssemblyName=uas-sort-cli`).

- [ ] **Step 4: Run the whole suite**

```powershell
dotnet test --solution uas-sort.slnx
```

Expected: exit code 0, `failed: 0` in every project.

- [ ] **Step 5: Commit**

```powershell
git add tests/UasSort.Platform.Tests/Cli/CliPlanRunTests.cs
git commit -m "test: uas-sort-cli --expect end to end (stderr under --json, stdout otherwise)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

## Part 12 — Produces (summary)

- `src/UasSort.Cli` (namespace `UasSort.Cli`; `AssemblyName=uas-sort-cli`, `net11.0-windows10.0.26100.0`, references Core and Platform only; `InternalsVisibleTo` Platform.Tests; copies `places.bin.gz` beside the exe when present; `GlobalUsings.cs` = the registry's Platform namespaces, Core ones from Part 02's `GlobalUsings.Core.cs`):
  - `CliArgs` (`TryParse`, `Usage`) — the exact grammar of Ref §4.5; invariant-culture numbers; R 5–100, G 0–7.
  - `PlanDocument` and its records, `GroupTargetKind`, `NewnessStatus`, `CliJsonContext` (source-generated, camelCase, enum names, `v:1`) — the JSON schema of Ref §4.5; `PlanTextRenderer` (the text form, `NEW FOLDER 2026\2026-09\2026-09-27 · 13 clips · Sep 27 · issues: EmptyFolderName`).
  - `ExpectedFileJson`, `ExpectedFolderJson` (optional `target`), `ExpectedFile.Parse`, `ExpectDiff.Compare`/`Write`, `ExpectReport` (`PassAt = 2`) — the edit count of Ref §4.5.
  - `ICliHost`, `PlanCommand.RunAsync` / `Effective`, `StderrProgress`, `PlanDocumentMapper.Map`, `WindowsCliHost` (`Environment.MachineName` as `machine`), `Program.Main` (first calls `PlaceholderMode.ExposePlaceholders()`) — exit 0 (plan printed, even with Blocking issues), 1 (source refused, reason on stderr), 2 (anything else); logs, progress, timing and (under `--json`) the expect report on stderr; writes nothing, ever; no cleanup command.
- `src/UasSort.Platform/Io/ReadOnlyTextFile.Read(path, maxBytes)` — attribute-first, placeholder-refusing, read-only text reader (the `--expect` file).
- `tests/acceptance/first-card-expected.schema.json`, `first-card-expected.example.json`, `README.md` — the template and instructions the user fills in before the first-card acceptance.
- Tests in `tests/UasSort.Platform.Tests/Cli/` and `Io/`: `CliArgsTests`, `PlanDocumentTests`, `ExpectDiffTests`, `ExpectedFileTests`, `ReadOnlyTextFileTests`, `PlanCommandTests`, `ProgramTests` (source check of the `Main` order), `CliPlanRunTests` (the spec's `%TEMP%` run: before/after snapshots of the card, video, photo and settings roots and of `%LOCALAPPDATA%\uas-sort` are identical); helpers `CliSamples`, `CliTestCard`, `TreeSnapshot`; `FakeCardWriterTests`.
- `tests/UasSort.Testing/FakeCardWriter.cs` (`UasSort.Testing.FakeCardWriter`; this part is its registry owner and only user) — materialises a DJI card (synthetic MP4s, any extra files, mtimes) under `%TEMP%\uas-sort-test-*` only.
