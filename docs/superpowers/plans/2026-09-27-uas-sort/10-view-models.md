# Part 10 — Review view models

**Goal:** build every WinUI-free view model of `UasSort.Review` (stages, Review with its tabs, map bridge, Commit, Verdict, Card cleanup, Setup/Settings, `CollectionSync`) test-first against fakes, so Part 11 only binds XAML to them. Ref sections: §4.2 (Review VMs row, PlanSession threading contract), §9 (all UI behaviour that lives in VMs), §9.6 (map message contract and golden messages), §10.2/§10.5 (preflight and verdict), §10.6 (Cleanup VM), §13 (view-model tests, cross-assembly tests), §14 step 10.

**Depends on:** Part 01 (solution skeleton, `UasSort.Review` and `UasSort.Review.Tests` projects, pins), Part 02 (Core model, ports, `IUiDispatcher`, `IDialogService`, `IShellLauncher`, `IThumbnailSource`, `FileKey.OfPath`, `GeoPointJsonConverter`, the fixed `GlobalUsings.Core.cs` in both Review projects, card detection/validation, `FakeFileSystem`/`FakeLayout`), Part 03 (`IThumbnailSource` producers), Part 04 (`ClockModel`, `ClockSummary`, `ZoneNames`, `GeoMath`, `DroneClock.Round15`/`Summarize`), Part 05 (`LibraryIndex`, `LedgerSnapshot`, `ISettingsStore`, `ILedgerStore`, `VideoRootChange`, `SettingsEdits`), Part 06 (`Planner` incl. `Planner.AppendCandidates`, `PlanSession`, `IPlanDeriver`, `ScanService`, `UasSort.Testing.GatedPlanDeriver`, `FakeLedgerStore`), Part 07 (`CommitSession`/`CommitEnvironment`/`CommitResult`, `AckKey`/`PreflightAcks`, `CardAudit`, `VerdictDecisions`, the offload fakes and `OffloadRig`/`OffloadPlanBuilder`), Part 08 (`CleanupPlanner`, `CleanupPlan.Confirm`, `CleanupExecutor`, `CleanupTexts`, `CleanupRowOps`, `CleanupVolumeCheck`, `CleanupPlanFixtures`), Part 09 (Platform ports; bound only in Part 11). Names, namespaces and signatures follow `00-interfaces.md`, which wins over this text.

**Conventions for this part (apply to every task):**
- Every Review source file uses `namespace UasSort.Review;` (folders are only for organisation). Tests use `namespace UasSort.Review.Tests;`; the additions to Part 06's gated deriver stay in its `namespace UasSort.Testing;`.
- Core types are used by their Ref §3/§4 names. Core namespaces come from the fixed `GlobalUsings.Core.cs` that Part 02 Task 02.1 already created in `src/UasSort.Review` and `tests/UasSort.Review.Tests` (never regenerated or edited here), so no task guesses a Core namespace; each project's own `GlobalUsings.cs` has exactly the registry content (Task 10.1).
- Review reuses Core helpers instead of copies: `FileKey.OfPath` (keys), Core `GeoMath.Haversine`, Core `ZoneNames` (`Region`, `Abbreviation`, `OffsetAt`, `IsUs`), `DroneClock.Round15`, `ClockSummary.Headline` for the clock banner, `GeoPointJsonConverter` for map messages, `Planner.AppendCandidates` for the retarget flyout, `VerdictDecisions` for the Verdict page, `CleanupTexts`/`CleanupRowOps` for the cleanup page.
- The Commit runs through Part 07's `CommitSession` (the Preflight/Copy VMs get a `Func<Plan, CommitSession>`), and the cleanup run through Part 08's `CleanupExecutor.RunAsync` (the `CleanupEngine.Run` delegate is bound to exactly that call). Other Core calls that need Platform ports (`CardAudit.Audit` after a re-list, `CardDetector.Detect`, `ICardSourceValidator.Validate`, `ScanService.ScanAsync`, `Planner.Prepare`) reach the VMs as delegates in small records defined here (`CleanupEngine`, `ShellDeps`, `VerdictPorts.Reaudit`, `CardStageVm`/`ScanStageVm` constructor delegates). Part 11's composition root binds them. `CleanupPlanner.Candidates`/`Build` are called directly (Ref §4.2 names them as the only constructor path of `CleanupPlan`, which VM tests must exercise for real).
- All text is formatted with `CultureInfo.InvariantCulture`, every string comparison passes a `StringComparison`, and time comes only from `TimeProvider` (never `DateTime.Now`/`UtcNow`).
- State properties are MVVM Toolkit 8.4.2 partial properties (`[ObservableProperty] public partial T Name { get; private set; }`); commands are created explicitly (`RelayCommand`/`AsyncRelayCommand`) so `CanExecute` wiring is visible.
- If a recommended analyzer rule not anticipated here fires, fix the code the way the rule asks; never add a project-wide `NoWarn`.
- Non-nullable `[ObservableProperty]` partial properties carry an initializer (C# 14+ allows initializers on field-backed partial properties). Constructors assign observable properties only after every command the change handlers touch has been created.
- Test command for one test: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*<Name>*"` (from WSL: `tools/r.sh dotnet test …`, then `dotnet build-server shutdown`).

---

### Task 10.1: Review scaffolding, service seams, test fakes, `GatedPlanDeriver`, plan fixtures

**Files:**
- Modify: `src/UasSort.Review/UasSort.Review.csproj`
- Create: `src/UasSort.Review/GlobalUsings.cs` (`GlobalUsings.Core.cs` already exists from Part 02 Task 02.1; do not touch it)
- Create: `src/UasSort.Review/Services/ReviewServices.cs`
- Modify: `tests/UasSort.Testing/GatedPlanDeriver.cs` (Part 06 Task 06.16 created it; Part 10 adds the hold/list members in the same file)
- Modify: `tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj`
- Create: `tests/UasSort.Review.Tests/GlobalUsings.cs` (`GlobalUsings.Core.cs` already exists from Part 02 Task 02.1)
- Create: `tests/UasSort.Review.Tests/Fakes/Fakes.cs`, `tests/UasSort.Review.Tests/Fakes/Eventually.cs`
- Create: `tests/UasSort.Review.Tests/Fixtures/TestPlans.cs`, `tests/UasSort.Review.Tests/Fixtures/ScriptedDeriver.cs`
- Test: `tests/UasSort.Review.Tests/GatedPlanDeriverTests.cs`

**Interfaces:**
- Consumes: `IPlanDeriver.Derive(PlanBase, Tuning, IReadOnlyList<PlanEdit>, SessionFlags, int, CancellationToken)`, `IUiDispatcher`, `IDialogService`, `DialogRequest`, `DialogResult`, `IShellLauncher`, `IThumbnailSource`, `IDraftStore`, `Draft`, every Core record used to build a `Plan` (Ref §3), `LibraryIndex.Build(LibraryListings, LedgerSnapshot, ClockModel)`, Core `GeoMath.Haversine` (Part 04); Part 06's `UasSort.Testing.GatedPlanDeriver` (`Arm`, `Gate`, `CallCount`); Part 07's `UasSort.Testing.FakeThumbnails` (`Paused` = active pauses).
- Produces (defined here):
  - `public interface IReviewLog { void Warn(string message); }`
  - `public interface IFreeSpace { long? FreeBytes(string anyPathOnVolume); }`
  - `public sealed record ReviewServices(IUiDispatcher Ui, IDialogService Dialogs, IShellLauncher Shell, IThumbnailSource Thumbs, IDraftStore Drafts, IFreeSpace Space, TimeProvider Time, IReviewLog Log)`
  - Added to Part 06's `UasSort.Testing.GatedPlanDeriver(IPlanDeriver inner) : IPlanDeriver` (which keeps `Gate Arm(...)`, `int CallCount`, `sealed class Gate { Task Entered; void Release(); }` working): `bool Hold { get; set; }`, `IReadOnlyList<DeriveCall> Calls`, `Task<DeriveCall> CallStartedAsync(int index)`, `void Release(int index)`, `void ReleaseAll()`
  - `UasSort.Testing.DeriveCall` (same file) — `int Index`, `Tuning Tuning`, `ImmutableArray<PlanEdit> Edits`, `SessionFlags Flags`, `int Revision`, `bool Held`, `bool Completed`, `bool Cancelled`
  - Test-only: `FakeUiDispatcher`, `FakeDialogService`, `FakeShellLauncher`, `FakeDraftStore`, `FakeFreeSpace`, `ListLog`, `Fake.Services`, `Eventually`, `PlanClip`, `TestPlans`, `ScriptedDeriver` (the thumbnail fake is `UasSort.Testing.FakeThumbnails`, Part 07)

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/GatedPlanDeriverTests.cs
namespace UasSort.Review.Tests;

public class GatedPlanDeriverTests
{
    [Fact]
    public async Task GatedPlanDeriver_Part06ArmAndCallCount_StillWork()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var gated = new GatedPlanDeriver(new ScriptedDeriver());
        var ct = TestContext.Current.CancellationToken;
        var gate = gated.Arm((t, _) => t.RadiusMiles == 30);

        var derive = Task.Run(() => gated.Derive(b, new Tuning(30, 1), [], new SessionFlags(false), 1, ct), ct);
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.False(derive.IsCompleted);
        gate.Release();
        await derive;

        gated.Derive(b, new Tuning(), [], new SessionFlags(false), 2, ct);
        Assert.Equal(2, gated.CallCount);
        Assert.Equal(2, gated.Calls.Count);
        Assert.All(gated.Calls, c => Assert.True(c.Completed));
    }

    [Fact]
    public async Task GatedPlanDeriver_HeldCall_BlocksUntilReleased()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var gated = new GatedPlanDeriver(new ScriptedDeriver()) { Hold = true };
        var ct = TestContext.Current.CancellationToken;

        var derive = Task.Run(() => gated.Derive(b, new Tuning(), [], new SessionFlags(false), 1, ct), ct);
        var call = await gated.CallStartedAsync(0);

        Assert.True(call.Held);
        Assert.False(derive.IsCompleted);
        gated.Release(0);
        var plan = await derive;
        Assert.Equal(1, plan.Revision);
        Assert.True(gated.Calls[0].Completed);
    }

    [Fact]
    public async Task GatedPlanDeriver_CancelledWhileHeld_ThrowsAndIsMarked()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var gated = new GatedPlanDeriver(new ScriptedDeriver()) { Hold = true };
        using var cts = new CancellationTokenSource();

        var derive = Task.Run(() => gated.Derive(b, new Tuning(), [], new SessionFlags(false), 1, cts.Token), TestContext.Current.CancellationToken);
        await gated.CallStartedAsync(0);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => derive);
        Assert.True(gated.Calls[0].Cancelled);
    }

    [Fact]
    public void ScriptedDeriver_CouncilAnvilAt50Miles_IsOneGroupWithEmphasisedSplit()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var plan = new ScriptedDeriver().Derive(b, new Tuning(50, 1), [], new SessionFlags(false), 1, TestContext.Current.CancellationToken);

        var g = Assert.Single(plan.Groups);
        Assert.Equal(4, g.Videos.Length);
        var split = Assert.Single(g.DaySplits);
        Assert.True(split.Emphasised);
        Assert.Contains(plan.Issues, i => i.Code == IssueCode.EmphasisedDaySplit && i.RequiresAckAtPreflight);
    }

    [Fact]
    public void ScriptedDeriver_At25Miles_SplitsByDistance()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var plan = new ScriptedDeriver().Derive(b, new Tuning(25, 1), [], new SessionFlags(false), 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, plan.Groups.Length);
        Assert.Equal(BoundaryCause.Distance, Assert.Single(plan.Boundaries).Cause);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*GatedPlanDeriver*"`
Expected: build FAILS with CS0246 (`TestPlans`, `ScriptedDeriver` not found) and CS1061 (`GatedPlanDeriver` has no `Hold`, `Calls`, `CallStartedAsync`, `Release(int)`).

- [ ] **Step 3: Project files and project-specific usings**

```xml
<!-- src/UasSort.Review/UasSort.Review.csproj (replace the whole file) -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <RootNamespace>UasSort.Review</RootNamespace>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\UasSort.Core\UasSort.Core.csproj" />
    <PackageReference Include="CommunityToolkit.Mvvm" />
    <InternalsVisibleTo Include="UasSort.Review.Tests" />
  </ItemGroup>
</Project>
```

```xml
<!-- tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj (replace the whole file) -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <RootNamespace>UasSort.Review.Tests</RootNamespace>
    <NoWarn>$(NoWarn);CA1707</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\UasSort.Review\UasSort.Review.csproj" />
    <ProjectReference Include="..\UasSort.Testing\UasSort.Testing.csproj" />
    <PackageReference Include="xunit.v3.mtp-v2" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

Both projects keep the `GlobalUsings.Core.cs` that Part 02 Task 02.1 created (the fixed list of every Core namespace, `System.Collections.Immutable` included); it is never regenerated or edited here. The project-specific files contain exactly the registry lines (no namespace is imported twice; `Xunit` comes only from the csproj `<Using>` above):

```csharp
// src/UasSort.Review/GlobalUsings.cs
global using System.Collections.ObjectModel;
global using System.Globalization;
global using CommunityToolkit.Mvvm.ComponentModel;
global using CommunityToolkit.Mvvm.Input;
```

```csharp
// tests/UasSort.Review.Tests/GlobalUsings.cs
global using System.Globalization;
global using Microsoft.Extensions.Time.Testing;
global using UasSort.Review;
global using UasSort.Testing;
```

Check that the Core file is there in both projects:

```bash
cd /mnt/c/dev/uas-sort
head -3 src/UasSort.Review/GlobalUsings.Core.cs tests/UasSort.Review.Tests/GlobalUsings.Core.cs
```
Expected: both files start with `// GlobalUsings.Core.cs — fixed content, see docs/superpowers/plans/2026-09-27-uas-sort/00-interfaces.md` followed by `global using System.Collections.Immutable;`. If either is missing, Part 02 Task 02.1 was not completed: stop and finish it (copy the registry content verbatim), do not generate a list.

- [ ] **Step 4: Service seams (Review)**

```csharp
// src/UasSort.Review/Services/ReviewServices.cs
namespace UasSort.Review;

/// <summary>Log sink for things the VMs report but never throw (unknown map messages, ignored plans).</summary>
public interface IReviewLog
{
    void Warn(string message);
}

/// <summary>Free-space readout for the footer, Setup and Settings (Part 11 binds it to IFileOps.FreeBytes).</summary>
public interface IFreeSpace
{
    long? FreeBytes(string anyPathOnVolume);
}

/// <summary>Everything the Review stage needs from the outside (Ref §4.2 "Services").</summary>
public sealed record ReviewServices(IUiDispatcher Ui, IDialogService Dialogs, IShellLauncher Shell, IThumbnailSource Thumbs,
                                    IDraftStore Drafts, IFreeSpace Space, TimeProvider Time, IReviewLog Log);
```

- [ ] **Step 5: Extend Part 06's `GatedPlanDeriver` (Testing)**

Part 06 Task 06.16 created `tests/UasSort.Testing/GatedPlanDeriver.cs` (`namespace UasSort.Testing`) with `Arm`, `Gate` and the int counter `CallCount`. Do not create a second class: replace that file with the version below, which keeps those three members exactly (Part 06's `PlanSessionTests` use them) and adds the hold/list members the VM ordering tests need. An armed gate and `Hold` compose: a call matching an armed gate waits on the gate first, then on its own hold.

```csharp
// tests/UasSort.Testing/GatedPlanDeriver.cs  (replaces Part 06's file; Part 06 members unchanged)
namespace UasSort.Testing;

/// <summary>One call into <see cref="GatedPlanDeriver"/> (added by Part 10).</summary>
public sealed class DeriveCall
{
    internal DeriveCall(int index, Tuning tuning, ImmutableArray<PlanEdit> edits, SessionFlags flags, int revision, bool held)
    {
        Index = index; Tuning = tuning; Edits = edits; Flags = flags; Revision = revision; Held = held;
    }

    public int Index { get; }
    public Tuning Tuning { get; }
    public ImmutableArray<PlanEdit> Edits { get; }
    public SessionFlags Flags { get; }
    public int Revision { get; }
    public bool Held { get; }
    public bool Completed { get; internal set; }
    public bool Cancelled { get; internal set; }
    internal ManualResetEventSlim Gate { get; } = new(false);
}

/// <summary>An IPlanDeriver whose derives block until the test opens their gate (Ref §13 "Ordering"): Part 06's armed gates,
/// plus Part 10's hold-every-call mode with a per-call record.</summary>
public sealed class GatedPlanDeriver(IPlanDeriver inner) : IPlanDeriver
{
    private readonly List<Gate> _armed = [];
    private readonly Lock _lock = new();
    private readonly List<DeriveCall> _calls = [];
    private readonly List<TaskCompletionSource<DeriveCall>> _started = [];
    private int _callCount;

    // ── Part 06 (unchanged)
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

    // ── Part 10
    /// <summary>When true, every derive that starts from now on waits for <see cref="Release(int)"/>.</summary>
    public bool Hold { get; set; }

    public IReadOnlyList<DeriveCall> Calls { get { lock (_lock) { return [.. _calls]; } } }

    public Task<DeriveCall> CallStartedAsync(int index)
    {
        lock (_lock) { return Slot(index).Task.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    public void Release(int index)
    {
        DeriveCall call;
        lock (_lock) { call = _calls[index]; }
        call.Gate.Set();
    }

    public void ReleaseAll()
    {
        Hold = false;
        foreach (var c in Calls) c.Gate.Set();
    }

    public Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct)
    {
        Interlocked.Increment(ref _callCount);
        DeriveCall call;
        Gate? gate;
        lock (_lock)
        {
            gate = _armed.FirstOrDefault(g => g.Match(t, edits));
            if (gate is not null) _armed.Remove(gate);
            call = new DeriveCall(_calls.Count, t, [.. edits], flags, revision, Hold);
            _calls.Add(call);
            Slot(call.Index).TrySetResult(call);
        }
        try
        {
            if (gate is not null)
            {
                gate.EnteredTcs.TrySetResult();
                gate.ReleaseTcs.Task.Wait(CancellationToken.None);
            }
            if (call.Held) call.Gate.Wait(ct);
            ct.ThrowIfCancellationRequested();
            var plan = inner.Derive(b, t, edits, flags, revision, ct);
            call.Completed = true;
            return plan;
        }
        catch (OperationCanceledException)
        {
            call.Cancelled = true;
            throw;
        }
    }

    private TaskCompletionSource<DeriveCall> Slot(int index)
    {
        while (_started.Count <= index) _started.Add(new(TaskCreationOptions.RunContinuationsAsynchronously));
        return _started[index];
    }
}
```

`tests/UasSort.Testing` already has Part 02's `GlobalUsings.Core.cs`, so the file needs no `using` lines.

- [ ] **Step 6: Test fakes** (Review.Tests-only fakes; the thumbnail, ledger, lock, power and file-ops fakes are the shared `UasSort.Testing` ones)

```csharp
// tests/UasSort.Review.Tests/Fakes/Fakes.cs
namespace UasSort.Review.Tests;

/// <summary>
/// Queues posted actions until the test runs them. The fake "UI thread" is whatever thread is inside RunAll, so
/// HasThreadAccess is true only there; PostedWithAccess records, per Post, whether it came from the fake UI thread.
/// </summary>
internal sealed class FakeUiDispatcher : IUiDispatcher
{
    [ThreadStatic] private static bool t_inUi;
    private readonly Lock _lock = new();
    private readonly Queue<Action> _queue = new();

    public bool HasThreadAccess => t_inUi;
    public List<bool> PostedWithAccess { get; } = [];

    public void Post(Action a)
    {
        lock (_lock) { _queue.Enqueue(a); PostedWithAccess.Add(t_inUi); }
    }

    public int Pending { get { lock (_lock) { return _queue.Count; } } }

    /// <summary>Runs every queued action (and anything they queue) on the calling thread, as the UI thread.</summary>
    public void RunAll()
    {
        var outer = t_inUi;
        t_inUi = true;
        try
        {
            while (true)
            {
                Action? next;
                lock (_lock) { next = _queue.Count > 0 ? _queue.Dequeue() : null; }
                if (next is null) return;
                next();
            }
        }
        finally
        {
            t_inUi = outer;
        }
    }
}

internal sealed class FakeDialogService : IDialogService
{
    public List<DialogRequest> Shown { get; } = [];
    public Queue<DialogResult> Answers { get; } = new();
    public Task<DialogResult> ShowAsync(DialogRequest r)
    {
        Shown.Add(r);
        return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : DialogResult.Primary);
    }
}

internal sealed class FakeShellLauncher : IShellLauncher
{
    public List<string> Opened { get; } = [];
    public void OpenFolder(string path) => Opened.Add("folder:" + path);
    public void OpenFile(string path) => Opened.Add("file:" + path);
    public void OpenHttps(Uri uri) => Opened.Add("https:" + uri);
}

internal sealed class FakeDraftStore : IDraftStore
{
    public Dictionary<string, Draft> Drafts { get; } = new(StringComparer.Ordinal);
    public int Saves { get; private set; }
    public List<string> Deleted { get; } = [];
    public Draft? Load(string cardKey) => Drafts.GetValueOrDefault(cardKey);
    public void Save(string cardKey, Draft d) { Drafts[cardKey] = d; Saves++; }
    public void Delete(string cardKey) { Drafts.Remove(cardKey); Deleted.Add(cardKey); }
}

internal sealed class FakeFreeSpace : IFreeSpace
{
    public long? FreeBytes(string anyPathOnVolume) => 317_000_000_000;
}

internal sealed class ListLog : IReviewLog
{
    public List<string> Warnings { get; } = [];
    public void Warn(string message) => Warnings.Add(message);
}

internal static class Fake
{
    public static ReviewServices Services(FakeUiDispatcher ui, TimeProvider? time = null, FakeDraftStore? drafts = null,
                                          FakeDialogService? dialogs = null, ListLog? log = null)
        => new(ui, dialogs ?? new FakeDialogService(), new FakeShellLauncher(), new FakeThumbnails(),
               drafts ?? new FakeDraftStore(), new FakeFreeSpace(), time ?? new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 2, 0, 0, TimeSpan.Zero)),
               log ?? new ListLog());

    /// <summary>The shared in-memory <see cref="FakeLedgerStore"/> (Parts 06/07) for the fixture library; <paramref name="status"/>
    /// pins what <c>Check()</c> reports.</summary>
    public static FakeLedgerStore Ledger(LedgerFolderStatus? status = null, LedgerSnapshot? snapshot = null, string videoRoot = TestPlans.VideoRoot)
        => new(null, videoRoot, "PC1", snapshot ?? TestPlans.Ledger()) { StatusOverride = status ?? (snapshot ?? TestPlans.Ledger()).Status };
}
```

```csharp
// tests/UasSort.Review.Tests/Fakes/Eventually.cs
namespace UasSort.Review.Tests;

/// <summary>Polls a condition (running queued UI work each time) until it holds or 5 s pass.</summary>
internal static class Eventually
{
    public static async Task TrueAsync(Func<bool> condition, FakeUiDispatcher? ui = null)
    {
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 500; i++)
        {
            ui?.RunAll();
            if (condition()) return;
            await Task.Delay(10, ct);
        }
        ui?.RunAll();
        Assert.True(condition(), "condition not met within 5 s");
    }
}
```

- [ ] **Step 7: Plan fixtures**

```csharp
// tests/UasSort.Review.Tests/Fixtures/TestPlans.cs
namespace UasSort.Review.Tests;

/// <summary>One card video in a fixture (named PlanClip so it never clashes with UasSort.Testing.Planning.Clip). Times are true UTC;
/// the local date comes from TzId.</summary>
internal sealed record PlanClip(string Name, DateTime CaptureUtc, string TzId, double? Lat, double? Lon,
                           Newness? Newness = null, ItemFlags Flags = ItemFlags.None, long Bytes = 1_200_000_000,
                           DateTime? DroneStamp = null, TimeSource Source = TimeSource.Mvhd, TimeSpan? Duration = null);

internal static class TestPlans
{
    public const string VideoRoot = @"C:\Lib\UAS Videos";
    public const string PhotoRoot = @"C:\Lib\UAS Videos\Picture Offload";
    public const string Anchorage = "America/Anchorage";
    public static readonly CardIdentity Card = new(0x1A2B3C4D, "DJI_CARD", "exFAT", 256_060_514_304);
    public static readonly CardSource Source = new(@"E:\", Card, IsBrowsedFolder: false, IsWriteProtected: false);

    public static ItemId Id(string name) => new($"DCIM/DJI_001/{name}");
    public static DateTime Utc(int y, int mo, int d, int h, int mi, int s = 0) => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    /// <summary>Council Road (2 clips, Jul 25 AKDT) and Anvil Mountain (2 clips, Jul 26 AKDT), 33.9 mi apart; all New.</summary>
    public static IReadOnlyList<PlanClip> CouncilAnvil() =>
    [
        new("DJI_20260725192655_0117_D.MP4", Utc(2026, 7, 26, 3, 26, 55), Anchorage, 64.6935, -164.2657),
        new("DJI_20260725194000_0118_D.MP4", Utc(2026, 7, 26, 3, 40, 0), Anchorage, 64.6940, -164.2650),
        new("DJI_20260726195645_0001_D.MP4", Utc(2026, 7, 27, 3, 56, 45), Anchorage, 64.5627, -165.3696),
        new("DJI_20260726202000_0002_D.MP4", Utc(2026, 7, 27, 4, 20, 0), Anchorage, 64.5630, -165.3700),
    ];

    /// <summary>Zachar Bay: 3 clips on Sep 27 AKDT shot with an Eastern-set drone clock (ClockMismatch on each).</summary>
    public static IReadOnlyList<PlanClip> Zachar() =>
    [
        new("DJI_20260927140127_0123_D.MP4", Utc(2026, 9, 27, 18, 1, 27), Anchorage, 57.5415, -153.7409,
            Flags: ItemFlags.ClockMismatch, DroneStamp: new DateTime(2026, 9, 27, 14, 1, 27)),
        new("DJI_20260927140627_0128_D.MP4", Utc(2026, 9, 27, 18, 6, 27), Anchorage, 57.550442, -153.738973,
            Flags: ItemFlags.ClockMismatch, DroneStamp: new DateTime(2026, 9, 27, 14, 6, 27)),
        new("DJI_20260927142416_0148_D.MP4", Utc(2026, 9, 27, 18, 24, 16), Anchorage, 57.5420, -153.7400,
            Flags: ItemFlags.ClockMismatch, DroneStamp: new DateTime(2026, 9, 27, 14, 24, 16)),
    ];

    public static Settings Settings(bool rootsConfirmed = true) =>
        new(1, VideoRoot, PhotoRoot, [], 50, 1, StoredClockMode.Zone, "America/New_York", true,
            new MapSettings("streets", "https://tiles.openfreemap.org/styles/liberty", "https://tiles.openfreemap.org/styles/dark",
                            "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                            ImmutableDictionary<string, string>.Empty),
            new LayoutSettings(420, 0.6), rootsConfirmed);

    public static LedgerSnapshot Ledger(LedgerFolderState state = LedgerFolderState.Ok,
                                        ImmutableArray<LedgerParseIssue> parseIssues = default,
                                        ImmutableArray<string> sourceFiles = default) =>
        new(ImmutableDictionary<FileKey, LedgerFile>.Empty,
            ImmutableDictionary<string, ImmutableArray<LedgerSet>>.Empty,
            ImmutableDictionary<FileKey, LedgerDecision>.Empty,
            ImmutableDictionary<FileKey, DateTime>.Empty,
            ImmutableDictionary<string, LedgerFolder>.Empty,
            [], [], parseIssues.IsDefault ? [] : parseIssues,
            sourceFiles.IsDefault ? [@"C:\Lib\UAS Videos\.uas-sort\ledger-PC1.jsonl"] : sourceFiles,
            new LedgerFolderStatus(VideoRoot + @"\.uas-sort", state, state != LedgerFolderState.Missing, true, true,
                                   state != LedgerFolderState.Unwritable, [], [], []));

    public static ClockModel ZoneClock() => new(ClockMode.Zone, "America/New_York", [], TimeSpan.FromHours(-4), StoredClockMode.Zone, "America/New_York");

    public const string Headline = "Drone clock: US Eastern (America/New_York), learned from 13 videos. Folder dates use local time at each site.";

    /// <summary>A ClockSummary as Planner.Prepare builds it through DroneClock.Summarize; the headline text is Core's (Part 04).</summary>
    public static ClockSummary Summary(ClockMode mode = ClockMode.Zone, int mismatchItems = 0, ImmutableArray<string> siteZones = default,
                                       ImmutableArray<ClockChange> changes = default, string headline = Headline) =>
        new(mode, mode == ClockMode.Zone ? "America/New_York" : null, 13, headline, mismatchItems,
            siteZones.IsDefault ? [] : siteZones, changes.IsDefault ? [] : changes);

    public static ListingResult EmptyListing() => new([], []);

    public static LibraryListings Listings(ImmutableArray<FsEntry> videoEntries = default) =>
        new(new RootListing(VideoRoot, DestRoot.Video, false, true, new ListingResult(videoEntries.IsDefault ? [] : videoEntries, [])),
            new RootListing(PhotoRoot, DestRoot.Photo, false, true, EmptyListing()), []);

    public static Item ItemOf(PlanClip c)
    {
        var id = Id(c.Name);
        var entry = new CardEntry(id.CardRelPath, c.Bytes, c.CaptureUtc, c.CaptureUtc, c.CaptureUtc, 0x20, EntryClass.Video, null);
        var mp4 = new Mp4Info(c.CaptureUtc, !c.Flags.HasFlag(ItemFlags.Truncated), new NoFix(NoFixReason.NotDji), null, "dvtm_Air3s.proto",
                              null, "1581F", null, c.Duration ?? TimeSpan.FromSeconds(222));
        var raw = new RawItem(new VideoUnit(id, entry, false), ItemKind.Video, c.Name, c.Bytes, c.CaptureUtc, c.DroneStamp, mp4, null, null);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(c.TzId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(c.CaptureUtc, tz);
        var time = new ItemTime(c.CaptureUtc, c.Source, c.TzId, TzSource.Gps, DateOnly.FromDateTime(local), local);
        GpsFix? gps = c.Lat is { } lat && c.Lon is { } lon ? new GpsFix(new GeoPoint(lat, lon), null, 0, GpsSource.DjmdModelTable, null) : null;
        var flags = c.Flags | (gps is null ? ItemFlags.NoGps : ItemFlags.None);
        return new Item(raw, time, gps, new SessionKey("1581F", c.CaptureUtc), flags, c.Newness ?? new IsNew(NewReason.NoMatch, null));
    }

    public static PlanBase Base(IReadOnlyList<PlanClip> clips, ClockModel? clock = null, ClockSummary? summary = null,
                                LedgerSnapshot? ledger = null, IReadOnlyList<Item>? extraItems = null,
                                ImmutableArray<PhotoDay> photoDays = default, LibraryListings? listings = null,
                                CardSource? source = null, DateTime? watermarkUtc = null, ImmutableArray<CardEntry> extraEntries = default,
                                ImmutableArray<ScanWarning> warnings = default, bool rootsConfirmed = true)
    {
        var items = clips.Select(ItemOf).Concat(extraItems ?? []).ToImmutableArray();
        var clockModel = clock ?? ZoneClock();
        var led = ledger ?? Ledger();
        var entries = items.SelectMany(EntriesOf).Concat(extraEntries.IsDefault ? [] : extraEntries).ToImmutableArray();
        var src = source ?? Source;
        var inventory = new CardInventory(src, Utc(2026, 9, 28, 2, 0), "9f3c0a6d12e4b7a1", entries,
                                          [.. items.Select(i => i.Raw.Unit)], "FC9113", warnings.IsDefault ? [] : warnings);
        var library = LibraryIndex.Build(listings ?? Listings(), led, clockModel);
        var scan = new ScanResult(inventory, [.. items.Select(i => i.Raw)], library, led, clockModel,
                                  warnings.IsDefault ? [] : warnings, Settings(rootsConfirmed));
        return new PlanBase(scan, items, photoDays.IsDefault ? [] : photoDays,
                            ImmutableDictionary<ItemId, SetPlacement>.Empty, summary ?? Summary(), watermarkUtc);
    }

    private static IEnumerable<CardEntry> EntriesOf(Item i) => i.Raw.Unit switch
    {
        VideoUnit v => [v.Mp4],
        PhotoUnit p => p.JpgTwin is { } t ? [p.Primary, t] : [p.Primary],
        SetUnit s => s.Members,
    };
}
```

```csharp
// tests/UasSort.Review.Tests/Fixtures/ScriptedDeriver.cs
namespace UasSort.Review.Tests;

/// <summary>
/// A small, deterministic IPlanDeriver for VM tests: consecutive-clip clustering by G, R and walls, the four structural
/// edits, inclusion, pins, day splits, and the issues the VMs react to. It is not the Planner; it only has to produce
/// realistic Plans so the real PlanSession (Part 06) can validate edits against them.
/// </summary>
internal sealed class ScriptedDeriver(ImmutableArray<Suggestion> suggestions = default) : IPlanDeriver
{
    public Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var videos = b.Items.Where(i => i.Raw.Kind == ItemKind.Video)
                            .OrderBy(i => i.Time.CaptureUtc).ThenBy(i => i.Raw.Unit.Id.CardRelPath, StringComparer.Ordinal).ToList();
        var groups = new List<List<Item>>();
        foreach (var x in videos)
        {
            if (groups.Count == 0) { groups.Add([x]); continue; }
            var cur = groups[^1];
            var prev = cur[^1];
            var dayGap = x.Time.LocalDate.DayNumber - prev.Time.LocalDate.DayNumber > t.GapDays
                         && x.Time.CaptureUtc - prev.Time.CaptureUtc > TimeSpan.FromHours(3);
            var far = x.Gps is { } gx && Centroid(cur) is { } c && GeoMath.Haversine(gx.Point, c).Miles > t.RadiusMiles;
            var wall = Folder(x) is { } fx && cur.Select(Folder).FirstOrDefault(f => f is not null) is { } fc
                       && !string.Equals(fx.FullPath, fc.FullPath, StringComparison.OrdinalIgnoreCase);
            if (dayGap || far || wall) groups.Add([x]); else cur.Add(x);
        }

        var userSplits = new HashSet<ItemId>();
        foreach (var e in edits)
        {
            switch (e)
            {
                case SplitBefore s:
                    var gi = groups.FindIndex(g => g.Exists(i => i.Raw.Unit.Id == s.First));
                    if (gi < 0) break;
                    var at = groups[gi].FindIndex(i => i.Raw.Unit.Id == s.First);
                    if (at > 0) { groups.Insert(gi + 1, groups[gi][at..]); groups[gi] = groups[gi][..at]; }
                    userSplits.Add(s.First);
                    break;
                case Merge m:
                    var a = groups.FindIndex(g => g.Exists(i => i.Raw.Unit.Id == m.InA));
                    var z = groups.FindIndex(g => g.Exists(i => i.Raw.Unit.Id == m.InB));
                    if (a < 0 || z < 0 || a == z) break;
                    var (lo, hi) = a < z ? (a, z) : (z, a);
                    var merged = groups.Skip(lo).Take(hi - lo + 1).SelectMany(g => g).ToList();
                    groups.RemoveRange(lo, hi - lo + 1);
                    groups.Insert(lo, merged);
                    foreach (var i in merged) userSplits.Remove(i.Raw.Unit.Id);
                    break;
                case MoveToNewGroup mv:
                    var moving = groups.SelectMany(g => g).Where(i => mv.Items.Contains(i.Raw.Unit.Id)).ToList();
                    foreach (var g in groups) g.RemoveAll(i => mv.Items.Contains(i.Raw.Unit.Id));
                    groups.Add(moving);
                    if (moving.Count > 0) userSplits.Add(moving.OrderBy(i => i.Time.CaptureUtc).First().Raw.Unit.Id);
                    break;
                case MoveToGroup mt:
                    var target = groups.Find(g => g.Exists(i => i.Raw.Unit.Id == mt.InTarget));
                    if (target is null || mt.Items.Contains(mt.InTarget)) break;
                    var mov = groups.SelectMany(g => g).Where(i => mt.Items.Contains(i.Raw.Unit.Id)).ToList();
                    foreach (var g in groups) g.RemoveAll(i => mt.Items.Contains(i.Raw.Unit.Id));
                    target.AddRange(mov);
                    break;
            }
        }
        groups.RemoveAll(g => g.Count == 0);
        foreach (var g in groups) g.Sort((p, q) => p.Time.CaptureUtc.CompareTo(q.Time.CaptureUtc));
        groups.Sort((p, q) => p[0].Time.CaptureUtc.CompareTo(q[0].Time.CaptureUtc));

        var included = b.Items.Where(i => i.Newness is IsNew && !i.Flags.HasFlag(ItemFlags.Truncated))
                              .Select(i => i.Raw.Unit.Id).ToHashSet();
        foreach (var e in edits)
        {
            if (e is SetIncluded si) foreach (var id in si.Items) { if (si.Included) included.Add(id); else included.Remove(id); }
            if (e is SetDayIncluded sd)
                foreach (var i in b.Items.Where(i => i.Raw.Kind != ItemKind.Video && i.Time.LocalDate == sd.Day))
                    if (sd.Included) included.Add(i.Raw.Unit.Id); else included.Remove(i.Raw.Unit.Id);
        }

        var issues = new List<Issue>();
        var result = new List<VideoGroup>();
        var color = 0;
        foreach (var g in groups)
        {
            var anchor = g[0].Raw.Unit.Id;
            var ids = g.Select(i => i.Raw.Unit.Id).ToImmutableArray();
            var rename = edits.OfType<Rename>().LastOrDefault(r => ids.Contains(r.InGroup));
            var retarget = edits.OfType<Retarget>().LastOrDefault(r => ids.Contains(r.InGroup));
            var pinned = retarget?.PinnedMembers ?? rename?.PinnedMembers;
            if (pinned is { } pm && !pm.ToHashSet().SetEquals(ids))
                issues.Add(new Issue(IssueSeverity.Warning, IssueCode.PinMembershipChanged,
                    string.Create(CultureInfo.InvariantCulture, $"'{rename?.Description ?? "target"}' chosen for {pm.Length} clips; group now has {ids.Length}"),
                    anchor, [], true));
            var wall = g.Select(Folder).FirstOrDefault(f => f is not null);
            var anyIncluded = ids.Any(included.Contains);
            var (desc, src) = rename?.Description is { } d ? (d, DescSource.User)
                            : suggestions.IsDefaultOrEmpty ? ("", DescSource.None) : (suggestions[0].Text, DescSource.Feature);
            var start = g.Min(i => i.Time.LocalDate);
            var end = g.Max(i => i.Time.LocalDate);
            GroupTarget target = retarget?.Choice switch
            {
                SkipTarget => new SkipGroup(),
                AppendTo ap => new Append(new LibraryFolderRef(ap.FolderFullPath, start, Path.GetFileName(ap.FolderFullPath)), Confidence.High, "chosen by you", null),
                _ when anyIncluded => new NewFolder(NewRel(start, desc)),
                _ when wall is not null => new AlreadyImported(wall),
                _ => new NothingToCopy("nothing to copy"),
            };
            var foldable = target is AlreadyImported && !g.Exists(i => i.Newness is Conflict || i.Flags.HasFlag(ItemFlags.Truncated));
            if (target is NewFolder && desc.Length == 0)
                issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.EmptyFolderName, "Name this folder", anchor, [new QuickFix("Name it", [])], false));
            var splits = DaySplits(g);
            foreach (var s in splits.Where(s => s.Emphasised))
                issues.Add(new Issue(IssueSeverity.Warning, IssueCode.EmphasisedDaySplit, "likely separate outing", s.FirstOfDay,
                                     [new QuickFix("Split here", [new SplitBefore(s.FirstOfDay)])], true));
            var c = Centroid(g);
            result.Add(new VideoGroup(new GroupId(anchor), ids, c,
                new Distance(c is { } cc ? g.Where(i => i.Gps is not null).Max(i => GeoMath.Haversine(i.Gps!.Point, cc).Meters) : 0),
                start, end, wall, target, null, target is AlreadyImported or Append ? (target is Append ap2 ? ap2.Folder.Description : wall!.Description) : desc,
                target is AlreadyImported or Append ? DescSource.ExistingFolder : src, target is NewFolder, null,
                suggestions.IsDefault ? [] : suggestions, splits, [], foldable, target is AlreadyImported ? -1 : color++ % 10));
        }

        var boundaries = new List<Boundary>();
        for (var k = 1; k < result.Count; k++)
        {
            var (l, r) = (result[k - 1], result[k]);
            var lastL = groups[k - 1][^1];
            var firstR = groups[k][0];
            var cause = userSplits.Contains(r.Id.Anchor) ? BoundaryCause.UserSplit
                      : l.Wall is not null && r.Wall is not null && !string.Equals(l.Wall.FullPath, r.Wall.FullPath, StringComparison.OrdinalIgnoreCase) ? BoundaryCause.LibraryFolder
                      : firstR.Time.LocalDate.DayNumber - lastL.Time.LocalDate.DayNumber > t.GapDays ? BoundaryCause.DayGap
                      : BoundaryCause.Distance;
            Distance? jump = l.Centroid is { } lc && r.Centroid is { } rc ? GeoMath.Haversine(lc, rc) : null;
            boundaries.Add(new Boundary(l.Id, r.Id, cause, jump, firstR.Time.CaptureUtc - lastL.Time.CaptureUtc,
                                        r.Start.DayNumber - l.End.DayNumber));
        }

        if (b.Items.Any(i => i.Flags.HasFlag(ItemFlags.ClockMismatch)))
            issues.Add(new Issue(IssueSeverity.Info, IssueCode.ClockMismatch, "Drone clock differs from local time", null, [], false));
        foreach (var p in b.Scan.Ledger.ParseIssues)
            issues.Add(flags.LedgerIssuesAccepted
                ? new Issue(IssueSeverity.Warning, IssueCode.LedgerParseIssue, $"Ledger line {p.File}:{p.Line} can't be read: {p.Reason}", null, [], true)
                : new Issue(IssueSeverity.Blocking, IssueCode.LedgerParseIssue, $"Ledger line {p.File}:{p.Line} can't be read: {p.Reason}", null,
                            [new QuickFix("Accept and continue", [])], false));

        return new Plan(revision, b, t, [.. result], [.. boundaries], [.. included], [.. issues]);
    }

    private static string NewRel(DateOnly d, string desc)
        => string.Create(CultureInfo.InvariantCulture, $@"{d:yyyy}\{d:yyyy-MM}\{d:yyyy-MM-dd}") + (desc.Length == 0 ? "" : " " + desc);

    private static LibraryFolderRef? Folder(Item i) => i.Newness is Imported im ? im.Folder : null;

    private static GeoPoint? Centroid(IEnumerable<Item> items)
    {
        var pts = items.Where(i => i.Gps is not null).Select(i => i.Gps!.Point).ToList();
        return pts.Count == 0 ? null : new GeoPoint(pts.Average(p => p.Lat), pts.Average(p => p.Lon));
    }

    private static ImmutableArray<DaySplit> DaySplits(List<Item> g)
    {
        var list = new List<DaySplit>();
        for (var k = 1; k < g.Count; k++)
        {
            if (g[k].Time.LocalDate == g[k - 1].Time.LocalDate) continue;
            var from = g[k - 1].Time.LocalDate;
            var to = g[k].Time.LocalDate;
            var a = Centroid(g.Where(i => i.Time.LocalDate == from));
            var z = Centroid(g.Where(i => i.Time.LocalDate == to));
            Distance? apart = a is { } pa && z is { } pz ? GeoMath.Haversine(pa, pz) : null;
            list.Add(new DaySplit(g[k].Raw.Unit.Id, from, to, apart, g[k].Time.CaptureUtc - g[k - 1].Time.CaptureUtc, apart is { Miles: >= 10 }));
        }
        return [.. list];
    }
}
```

(Distances use Core `GeoMath.Haversine` from Part 04, the same function the Planner uses, so no second Haversine exists in the Review tests.)

- [ ] **Step 8: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Deriver*"`
Expected: PASS, 5 tests. Then `dotnet test --project tests/UasSort.Core.Tests/UasSort.Core.Tests.csproj -- --filter-class "*PlanSessionTests"`: PASS (Part 06's gated tests still compile and pass against the extended class).

- [ ] **Step 9: Commit**

```bash
git add src/UasSort.Review/UasSort.Review.csproj src/UasSort.Review/GlobalUsings.cs src/UasSort.Review/Services tests/UasSort.Testing/GatedPlanDeriver.cs tests/UasSort.Review.Tests
git commit -m "test: review test scaffolding, GatedPlanDeriver and plan fixtures" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.2: Cross-assembly exhaustive switches and edit JSON round-trip

**Files:**
- Test: `tests/UasSort.Review.Tests/CrossAssemblyTests.cs`

**Interfaces:**
- Consumes: Core's closed `CopyOutcome` (`Verified`, `AlreadyThere`, `ConflictAtRename`, `ChangedOnCard`, `CardSwapped`, `Failed`, `Cancelled`, `NotStarted`), closed `CleanupOutcome` (`Deleted`, `SkippedChanged`, `SkippedEvidenceGone`, `PartiallyDeleted`, `CleanupFailed`, `CleanupNotStarted`, `CleanupCardSwapped`), unions `GpsProbe(GpsFix, NoFix)` and `EraseResult(EraseOk, EraseError)`, closed `PlanEdit` / `TargetChoice` with `[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]`.
- Produces: `ReviewTestJsonContext` (test-only source-generated context).

This task has no production code: it proves (Ref §13 "Cross-assembly and JSON") that a `switch` without a default arm over Core's closed/union types compiles in another assembly with `TreatWarningsAsErrors`, and that closed edits round-trip through a source-generated context compiled outside Core. The "failing" state is the missing test file; the test passes as soon as it compiles, and a compile failure here is a stack finding to raise with the user (Ref §15 UNVERIFIED cross-assembly exhaustiveness).

- [ ] **Step 1: Write the test**

```csharp
// tests/UasSort.Review.Tests/CrossAssemblyTests.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UasSort.Review.Tests;

[JsonSerializable(typeof(PlanEdit))]
[JsonSerializable(typeof(TargetChoice))]
[JsonSerializable(typeof(ImmutableArray<PlanEdit>))]
internal sealed partial class ReviewTestJsonContext : JsonSerializerContext;

public class CrossAssemblyTests
{
    private static readonly CopyJob Job = new(new ItemId("DCIM/DJI_001/a.MP4"), "DCIM/DJI_001/a.MP4", 10, DateTime.UnixEpoch,
                                              DateTime.UnixEpoch, @"C:\x\a.MP4", DestRoot.Video, null, false);

    private static string Name(CopyOutcome o) => o switch
    {
        Verified => "verified",
        AlreadyThere => "alreadyThere",
        ConflictAtRename => "conflict",
        ChangedOnCard => "changed",
        CardSwapped => "swapped",
        Failed => "failed",
        Cancelled => "cancelled",
        NotStarted => "notStarted",
    };

    private static string Name(CleanupOutcome o) => o switch
    {
        Deleted => "deleted",
        SkippedChanged => "skippedChanged",
        SkippedEvidenceGone => "evidenceGone",
        PartiallyDeleted => "partial",
        CleanupFailed => "failed",
        CleanupNotStarted => "notStarted",
        CleanupCardSwapped => "swapped",
    };

    private static string Name(GpsProbe p) => p switch
    {
        GpsFix f => "fix " + f.Sample.ToString(CultureInfo.InvariantCulture),
        NoFix n => "none " + n.Reason,
    };

    private static string Name(EraseResult r) => r switch
    {
        EraseOk => "ok",
        EraseError e => "error " + e.Win32Error.ToString(CultureInfo.InvariantCulture),
    };

    [Fact]
    public void CrossAssembly_ExhaustiveSwitches_CompileWithoutDefaultArm()
    {
        Assert.Equal("verified", Name(new Verified(Job, UInt128.One, VerifyMode.Unbuffered)));
        Assert.Equal("notStarted", Name(new NotStarted(Job)));
        Assert.Equal("partial", Name(new PartiallyDeleted(Job.Item, ["a"], ["b"], "stopped")));
        Assert.Equal("none NoGpsTag", Name(new NoFix(NoFixReason.NoGpsTag)));
        Assert.Equal("error 19", Name(new EraseError(19, "write protected")));
    }

    [Fact]
    public void CrossAssembly_PlanEditsRoundTripThroughSourceGeneratedJson()
    {
        ImmutableArray<PlanEdit> edits =
        [
            new Merge(new ItemId("a"), new ItemId("b")),
            new SplitBefore(new ItemId("c")),
            new Rename(new ItemId("a"), "Council Road", [new ItemId("a")]),
            new Retarget(new ItemId("a"), new AppendTo(@"C:\Lib\UAS Videos\2026\2026-07\2026-07-25 Council Road"), true, [new ItemId("a")]),
            new Retarget(new ItemId("a"), new SkipTarget(), false, []),
            new SetDayIncluded(new DateOnly(2026, 7, 25), false),
        ];

        var json = JsonSerializer.Serialize(edits, ReviewTestJsonContext.Default.ImmutableArrayPlanEdit);
        var back = JsonSerializer.Deserialize(json, ReviewTestJsonContext.Default.ImmutableArrayPlanEdit);

        Assert.Contains("\"t\":", json, StringComparison.Ordinal);
        Assert.Equal(edits.Length, back.Length);
        Assert.IsType<Merge>(back[0]);
        Assert.Equal("Council Road", Assert.IsType<Rename>(back[2]).Description);
        Assert.IsType<AppendTo>(Assert.IsType<Retarget>(back[3]).Choice);
        Assert.IsType<SkipTarget>(Assert.IsType<Retarget>(back[4]).Choice);
        Assert.Equal(new DateOnly(2026, 7, 25), Assert.IsType<SetDayIncluded>(back[5]).Day);
    }
}
```

- [ ] **Step 2: Run it**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*CrossAssembly*"`
Expected: PASS, 2 tests. (Before the file exists the filter matches nothing and the run reports zero tests; that is the failing state. If the switches do not compile — CS8509 — stop and raise it with the user; do not add a default arm.)

- [ ] **Step 3: Commit**

```bash
git add tests/UasSort.Review.Tests/CrossAssemblyTests.cs
git commit -m "test: cross-assembly exhaustive switches and PlanEdit JSON round-trip" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 10.3: Units and formatting (`Fmt`)

**Files:**
- Create: `src/UasSort.Review/Services/Fmt.cs`
- Test: `tests/UasSort.Review.Tests/FmtTests.cs`

**Interfaces:**
- Consumes: `Distance`, `GeoPoint`, `CardIdentity`; Core `GeoMath.Haversine` (Part 04, `UasSort.Core.Geo`) in the test. Zone names, abbreviations, offsets and the US check are Core `ZoneNames.Region`/`Abbreviation`/`OffsetAt`/`IsUs` (Part 04, `UasSort.Core.Time`), and the 15-minute rounding is `DroneClock.Round15` (Part 04); Review has no copy of either (Part 04 tests them).
- Produces (defined here):
  - `public static class Fmt` — `string Miles(Distance d)`, `string Size(long bytes)`, `string ClipLength(TimeSpan t)`, `string Day(DateOnly d)` ("Jul 25"), `string DayWithWeekday(DateOnly d)` ("Jul 25 (Sat)"), `string DayYear(DateOnly d)` ("Jul 26, 2026"), `string DateRange(DateOnly a, DateOnly b)`, `string Gap(TimeSpan t)`, `string Offset(TimeSpan o)`, `string Clock(DateTime local)` ("22:29"), `string Count(int n, string one, string many)`, `string Serial(uint serial)` ("1A2B-3C4D"), `string ModelName(string? model)`, `string CardChip(string root, CardIdentity id, string? model, int files, long bytes)`, `string Drive(string path)` ("C:")

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/FmtTests.cs
namespace UasSort.Review.Tests;

public class FmtTests
{
    [Theory]
    [InlineData(0.05, "<0.1 mi")]
    [InlineData(0.3, "0.3 mi")]
    [InlineData(7.8, "7.8 mi")]
    [InlineData(33.7, "34 mi")]
    [InlineData(50, "50 mi")]
    public void Fmt_Miles_FollowsTheUnitsTable(double miles, string expected)
        => Assert.Equal(expected, Fmt.Miles(Distance.FromMiles(miles)));

    [Theory]
    [InlineData(31_400_000_000L, "31.4 GB")]
    [InlineData(317_000_000_000L, "317 GB")]
    [InlineData(256_060_514_304L, "256.1 GB")]
    [InlineData(400_000_000L, "0.4 GB")]
    [InlineData(7_000_000L, "7 MB")]
    [InlineData(12_700L, "13 KB")]
    [InlineData(512L, "512 B")]
    public void Fmt_Size_IsDecimal(long bytes, string expected) => Assert.Equal(expected, Fmt.Size(bytes));

    [Fact]
    public void Fmt_ClipLength_MinutesOrHours()
    {
        Assert.Equal("3:42", Fmt.ClipLength(TimeSpan.FromSeconds(222)));
        Assert.Equal("1:02:05", Fmt.ClipLength(new TimeSpan(1, 2, 5)));
        Assert.Equal("0:07", Fmt.ClipLength(TimeSpan.FromSeconds(7.9)));
    }

    [Fact]
    public void Fmt_DatesAndRanges()
    {
        Assert.Equal("Jul 25", Fmt.DateRange(new(2026, 7, 25), new(2026, 7, 25)));
        Assert.Equal("Jul 25–26", Fmt.DateRange(new(2026, 7, 25), new(2026, 7, 26)));
        Assert.Equal("Jul 31 – Aug 2", Fmt.DateRange(new(2026, 7, 31), new(2026, 8, 2)));
        Assert.Equal("Dec 31, 2025 – Jan 1, 2026", Fmt.DateRange(new(2025, 12, 31), new(2026, 1, 1)));
        Assert.Equal("Jul 25 (Sat)", Fmt.DayWithWeekday(new(2026, 7, 25)));
        Assert.Equal("Jul 26, 2026", Fmt.DayYear(new(2026, 7, 26)));
    }

    [Fact]
    public void Fmt_GapsOffsetsAndCard()
    {
        Assert.Equal("21 h", Fmt.Gap(TimeSpan.FromHours(21.2)));
        Assert.Equal("62 days", Fmt.Gap(TimeSpan.FromHours(1488.5)));
        Assert.Equal("40 min", Fmt.Gap(TimeSpan.FromMinutes(40)));
        Assert.Equal("UTC−4", Fmt.Offset(TimeSpan.FromHours(-4)));
        Assert.Equal("UTC+5:30", Fmt.Offset(new TimeSpan(5, 30, 0)));
        Assert.Equal("UTC+0", Fmt.Offset(TimeSpan.Zero));
        Assert.Equal("1A2B-3C4D", Fmt.Serial(0x1A2B3C4D));
        Assert.Equal("E:\\ · DJI Air 3S · serial 1A2B-3C4D · 214 files · 61.3 GB",
                     Fmt.CardChip(@"E:\", TestPlans.Card, "FC9113", 214, 61_300_000_000));
        Assert.Equal("C:", Fmt.Drive(@"C:\Lib\UAS Videos"));
        Assert.Equal("1 conflict", Fmt.Count(1, "conflict", "conflicts"));
        Assert.Equal("3 conflicts", Fmt.Count(3, "conflict", "conflicts"));
    }

    [Fact]
    public void Fmt_Miles_OfTheCoreHaversine_CouncilToAnvilIs34Miles()
    {
        var d = GeoMath.Haversine(new GeoPoint(64.6935, -164.2657), new GeoPoint(64.5627, -165.3696));
        Assert.InRange(d.Miles, 33.5, 34.2);
        Assert.Equal("34 mi", Fmt.Miles(d));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Fmt*"`
Expected: build FAILS (CS0103 `Fmt`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Services/Fmt.cs
namespace UasSort.Review;

/// <summary>Ref §9.13 units and formatting; every VM text goes through here.</summary>
public static class Fmt
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static string Miles(Distance d)
    {
        var mi = d.Miles;
        if (mi < 0.1) return "<0.1 mi";
        if (mi < 10) return string.Create(C, $"{mi:0.0} mi");
        return string.Create(C, $"{Math.Round(mi, MidpointRounding.AwayFromZero):0} mi");
    }

    public static string Size(long bytes) => bytes switch
    {
        >= 100_000_000 => string.Create(C, $"{bytes / 1e9:0.#} GB"),
        >= 1_000_000 => string.Create(C, $"{Math.Round(bytes / 1e6, MidpointRounding.AwayFromZero):0} MB"),
        >= 1_000 => string.Create(C, $"{Math.Round(bytes / 1e3, MidpointRounding.AwayFromZero):0} KB"),
        _ => string.Create(C, $"{bytes} B"),
    };

    public static string ClipLength(TimeSpan t)
    {
        var total = (long)Math.Floor(t.TotalSeconds);
        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return h > 0 ? string.Create(C, $"{h}:{m:00}:{s:00}") : string.Create(C, $"{m}:{s:00}");
    }

    public static string Day(DateOnly d) => d.ToString("MMM d", C);
    public static string DayWithWeekday(DateOnly d) => d.ToString("MMM d (ddd)", C);
    public static string DayYear(DateOnly d) => d.ToString("MMM d, yyyy", C);

    public static string DateRange(DateOnly a, DateOnly b)
    {
        if (a == b) return Day(a);
        if (a.Year != b.Year) return $"{DayYear(a)} – {DayYear(b)}";
        if (a.Month == b.Month) return string.Create(C, $"{Day(a)}–{b.Day}");
        return $"{Day(a)} – {Day(b)}";
    }

    public static string Gap(TimeSpan t)
    {
        if (t < TimeSpan.FromHours(1)) return string.Create(C, $"{Math.Max(0, Math.Round(t.TotalMinutes)):0} min");
        if (t < TimeSpan.FromHours(48)) return string.Create(C, $"{Math.Round(t.TotalHours):0} h");
        return Count((int)Math.Round(t.TotalDays), "day", "days");
    }

    public static string Offset(TimeSpan o)
    {
        var sign = o < TimeSpan.Zero ? "−" : "+";
        var a = o.Duration();
        return a.Minutes == 0
            ? string.Create(C, $"UTC{sign}{(int)a.TotalHours}")
            : string.Create(C, $"UTC{sign}{(int)a.TotalHours}:{a.Minutes:00}");
    }

    public static string Clock(DateTime local) => local.ToString("HH:mm", C);

    public static string Count(int n, string one, string many) => string.Create(C, $"{n} {(n == 1 ? one : many)}");

    public static string Serial(uint serial)
    {
        var hex = serial.ToString("X8", C);
        return $"{hex[..4]}-{hex[4..]}";
    }

    public static string ModelName(string? model) => model switch
    {
        null => "DJI drone",
        _ when model.StartsWith("FC9113", StringComparison.OrdinalIgnoreCase) => "DJI Air 3S",
        _ => model,
    };

    public static string CardChip(string root, CardIdentity id, string? model, int files, long bytes)
        => $"{root} · {ModelName(model)} · serial {Serial(id.VolumeSerial)} · {Count(files, "file", "files")} · {Size(bytes)}";

    public static string Drive(string path) => (Path.GetPathRoot(path) ?? path).TrimEnd('\\');
}
```

There is no `src/UasSort.Review/Services/ZoneNames.cs` and no `src/UasSort.Review/Services/GeoMath.cs`: every caller in this part uses Core `ZoneNames.Region`, `ZoneNames.Abbreviation`, `ZoneNames.OffsetAt`, `ZoneNames.IsUs` (Part 04) and Core `GeoMath.Haversine` (Part 04), which `GlobalUsings.Core.cs` already imports.

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Fmt*"`
Expected: PASS, 16 tests (the Theories count 12).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Services tests/UasSort.Review.Tests/FmtTests.cs
git commit -m "feat: review units and formatting helpers" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.4: Clock display texts (`ClockText`)

**Files:**
- Create: `src/UasSort.Review/Review/ClockText.cs`
- Test: `tests/UasSort.Review.Tests/ClockTextTests.cs`

**Interfaces:**
- Consumes: `ClockSummary` (`Headline`, built by Part 04's `DroneClock.Summarize` inside `Planner.Prepare`), `ClockModel`, `ClockMode`, `StoredClockMode`, `Item`, `ItemTime`, `TimeSource`, `ItemFlags.ClockMismatch`; Core `ZoneNames.Region`/`Abbreviation`/`OffsetAt` and `DroneClock.Round15` (Part 04).
- Produces (defined here): `public static class ClockText` —
  - `string Banner(ClockSummary s, ClockModel model, IReadOnlyList<Item> items)` = `s.Headline` (Ref §6.1 header text; Core owns the wording per `ClockMode`, clock changes and the mismatch continuation, Review only shows it)
  - `string? MismatchInfoBar(ClockSummary s, ClockModel model, IReadOnlyList<Item> items)` (Ref §9.2 text; null when `MismatchItems == 0`)
  - `string SourceText(TimeSource s)`, `string SourceGlyph(TimeSource s)`, `bool IsEstimated(TimeSource s)`
  - `string TimeTooltip(Item item)` (Ref §9.5 side-by-side tooltip)
  - `string TimeCell(Item item)` ("~22:29 AKDT" / "22:29 AKDT")
  - `string ChipTooltip(Item item)` (Ref §9.4 "clock ≠ local" tooltip)
  - `const string WhyText`

The drone-clock offset of an item is `DroneClock.Round15(DroneStamp − CaptureUtc)`: for a `Mvhd` video that is the learner's sample offset, and for a drone-clock item it is the offset `ClockModel` used, so no second conversion is needed.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/ClockTextTests.cs
namespace UasSort.Review.Tests;

public class ClockTextTests
{
    private const string Fix = "To fix the drone clock: RC 2 → Settings → System → Date & time → turn off the network-provided time zone and set the zone for where you are flying. Network time zones can be wrong on ship or hotel Wi-Fi, and the RC keeps the last one it saw until it reconnects.";

    [Fact]
    public void ClockText_MismatchInfoBar_NamesClockZoneAndSites()
    {
        var items = TestPlans.Zachar().Select(TestPlans.ItemOf).ToList();
        var s = TestPlans.Summary(ClockMode.Zone, 3, ["America/Anchorage"]);

        var text = ClockText.MismatchInfoBar(s, TestPlans.ZoneClock(), items);

        Assert.Equal("Drone clock is set to UTC−4 (America/New_York), but footage on this card was shot in Alaska (UTC−8). Dates here use local time at each site. " + Fix, text);
    }

    [Fact]
    public void ClockText_MismatchInfoBar_JoinsSeveralSitesAndNearestSampleUsesOffsetOnly()
    {
        var hawaii = new PlanClip("DJI_20260228233000_0009_D.MP4", TestPlans.Utc(2026, 3, 1, 9, 30), "Pacific/Honolulu", 21.47, -158.21,
                              Flags: ItemFlags.ClockMismatch, DroneStamp: new DateTime(2026, 3, 1, 4, 30, 0));
        var items = TestPlans.Zachar().Append(hawaii).Select(TestPlans.ItemOf).ToList();
        var s = TestPlans.Summary(ClockMode.NearestSample, 4, ["America/Anchorage", "Pacific/Honolulu"]);
        var model = new ClockModel(ClockMode.NearestSample, null, [], null, StoredClockMode.Zone, "America/New_York");

        var text = ClockText.MismatchInfoBar(s, model, items);

        Assert.StartsWith("Drone clock is set to UTC−4, but footage on this card was shot in Alaska (UTC−8), Hawaii (UTC−10). ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ClockText_NoMismatch_NoInfoBar()
        => Assert.Null(ClockText.MismatchInfoBar(TestPlans.Summary(), TestPlans.ZoneClock(), []));

    [Fact]
    public void ClockText_TimeTooltip_ShowsUtcDroneClockAndSiteSideBySide()
    {
        var item = TestPlans.ItemOf(TestPlans.Zachar()[1]);
        Assert.Equal("18:06:27 UTC · drone clock 14:06:27 (UTC−4) · 10:06:27 AKDT (UTC−8) · from the video (mvhd) · Drone clock ≠ local time",
                     ClockText.TimeTooltip(item));
        Assert.Equal("10:06 AKDT", ClockText.TimeCell(item));
        Assert.Equal("The drone clock (UTC−4) doesn't match local time here (UTC−8). Dates use local time.", ClockText.ChipTooltip(item));
    }

    [Fact]
    public void ClockText_DroneClockTime_IsMarkedEstimated()
    {
        var item = TestPlans.ItemOf(TestPlans.Zachar()[0] with { Source = TimeSource.DroneClockZone, Flags = ItemFlags.None });
        Assert.Equal("~10:01 AKDT", ClockText.TimeCell(item));
    }

    [Fact]
    public void ClockText_Banner_IsTheCoreHeadline()
    {
        var items = TestPlans.Zachar().Select(TestPlans.ItemOf).ToList();
        const string headline = "Drone clock: US Eastern (America/New_York), learned from 13 videos. Folder dates use local time at each site. It doesn't match local time where this card was shot (Alaska).";
        var s = TestPlans.Summary(ClockMode.Zone, 3, ["America/Anchorage"], headline: headline);

        Assert.Equal(headline, ClockText.Banner(s, TestPlans.ZoneClock(), items));
    }

    [Fact]
    public void ClockText_EveryTimeSourceHasTextAndGlyph()
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var src in Enum.GetValues<TimeSource>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ClockText.SourceGlyph(src)), src.ToString());
            Assert.True(texts.Add(ClockText.SourceText(src)), $"duplicate source text for {src}");
        }
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*ClockText*"`
Expected: build FAILS (CS0103 `ClockText`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Review/ClockText.cs
namespace UasSort.Review;

/// <summary>Clock banner, clock-mismatch InfoBar, time cells and tooltips (Ref §6.1, §6.5, §9.2, §9.4, §9.5).</summary>
public static class ClockText
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public const string WhyText =
        "The drone stamps file names with the RC 2's clock, which follows the RC's time-zone setting, not GPS. uas-sort uses the true UTC time in each video and the time zone of where it was shot.";

    private const string FixText =
        "To fix the drone clock: RC 2 → Settings → System → Date & time → turn off the network-provided time zone and set the zone for where you are flying. Network time zones can be wrong on ship or hotel Wi-Fi, and the RC keeps the last one it saw until it reconnects.";

    public static string SourceText(TimeSource s) => s switch
    {
        TimeSource.Mvhd => "from the video (mvhd)",
        TimeSource.ExifWithOffset => "from the photo (EXIF with offset)",
        TimeSource.DroneClockSiteLocal => "estimated from the drone clock (local time at the site)",
        TimeSource.DroneClockZone => "estimated from the drone clock (its time zone)",
        TimeSource.DroneClockSample => "estimated from the drone clock (nearest video)",
        TimeSource.DroneClockSetting => "estimated from the drone clock (last learned setting)",
        TimeSource.Mtime => "from the card file's modified time",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "unknown TimeSource"),
    };

    public static string SourceGlyph(TimeSource s) => s switch
    {
        TimeSource.Mvhd => "",
        TimeSource.ExifWithOffset => "",
        TimeSource.DroneClockSiteLocal or TimeSource.DroneClockZone or TimeSource.DroneClockSample or TimeSource.DroneClockSetting => "",
        TimeSource.Mtime => "",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "unknown TimeSource"),
    };

    public static bool IsEstimated(TimeSource s)
        => s is TimeSource.DroneClockSiteLocal or TimeSource.DroneClockZone or TimeSource.DroneClockSample or TimeSource.DroneClockSetting;

    public static string TimeCell(Item item)
        => (IsEstimated(item.Time.Source) ? "~" : "") + Fmt.Clock(item.Time.LocalTime) + " " + ZoneNames.Abbreviation(item.Time.TzId, item.Time.CaptureUtc);

    /// <summary>The clock banner is Core's text (<see cref="ClockSummary.Headline"/>, from DroneClock.Summarize); Review only shows it.</summary>
    public static string Banner(ClockSummary s, ClockModel model, IReadOnlyList<Item> items)
    {
        ArgumentNullException.ThrowIfNull(s);
        _ = model;   // the registry signature keeps the clock model and the items; Core already summarised both into Headline
        _ = items;
        return s.Headline;
    }

    public static string TimeTooltip(Item item)
    {
        var utc = item.Time.CaptureUtc;
        var parts = new List<string> { utc.ToString("HH:mm:ss", C) + " UTC" };
        if (item.Raw.DroneStamp is { } stamp)
            parts.Add($"drone clock {stamp.ToString("HH:mm:ss", C)} ({Fmt.Offset(DroneOffset(item)!.Value)})");
        parts.Add($"{item.Time.LocalTime.ToString("HH:mm:ss", C)} {ZoneNames.Abbreviation(item.Time.TzId, utc)} ({Fmt.Offset(ZoneNames.OffsetAt(item.Time.TzId, utc))})");
        parts.Add(SourceText(item.Time.Source));
        if (item.Flags.HasFlag(ItemFlags.ClockMismatch)) parts.Add("Drone clock ≠ local time");
        return string.Join(" · ", parts);
    }

    public static string ChipTooltip(Item item)
    {
        var clock = DroneOffset(item) ?? TimeSpan.Zero;
        var site = ZoneNames.OffsetAt(item.Time.TzId, item.Time.CaptureUtc);
        return $"The drone clock ({Fmt.Offset(clock)}) doesn't match local time here ({Fmt.Offset(site)}). Dates use local time.";
    }

    public static string? MismatchInfoBar(ClockSummary s, ClockModel model, IReadOnlyList<Item> items)
    {
        if (s.MismatchItems == 0) return null;
        var flagged = items.Where(i => i.Flags.HasFlag(ItemFlags.ClockMismatch)).ToList();
        var clock = flagged.Select(DroneOffset).FirstOrDefault(o => o is not null);
        var zone = ClockZone(model);
        var clockText = (clock is { } c ? Fmt.Offset(c) : "a different time") + (zone is null ? "" : $" ({zone})");
        return $"Drone clock is set to {clockText}, but footage on this card was shot in {Sites(s, flagged)}. Dates here use local time at each site. {FixText}";
    }

    internal static TimeSpan? DroneOffset(Item item)
        => item.Raw.DroneStamp is { } stamp ? DroneClock.Round15(stamp - item.Time.CaptureUtc) : null;

    private static string? ClockZone(ClockModel m) => m.Mode switch
    {
        ClockMode.Zone => m.ZoneId,
        ClockMode.Setting when m.SettingMode == StoredClockMode.Zone => m.SettingZoneId,
        _ => null,
    };

    private static string Sites(ClockSummary s, IReadOnlyList<Item> flagged)
        => string.Join(", ", s.MismatchSiteZones.Select(z =>
        {
            var at = flagged.FirstOrDefault(i => string.Equals(i.Time.TzId, z, StringComparison.Ordinal))?.Time.CaptureUtc;
            return at is { } u ? $"{ZoneNames.Region(z)} ({Fmt.Offset(ZoneNames.OffsetAt(z, u))})" : ZoneNames.Region(z);
        }));
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*ClockText*"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Review/ClockText.cs tests/UasSort.Review.Tests/ClockTextTests.cs
git commit -m "feat: clock banner, mismatch InfoBar and time tooltip texts" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 10.5: `CollectionSync` keyed diff

**Files:**
- Create: `src/UasSort.Review/Sync/CollectionSync.cs`
- Test: `tests/UasSort.Review.Tests/CollectionSyncTests.cs`

**Interfaces:**
- Produces (defined here):
  - `public interface IKeyed { string Key { get; } }`
  - `public static class CollectionSync` — `void Sync<TVm, TModel>(ObservableCollection<TVm> target, IReadOnlyList<TModel> source, Func<TModel, string> key, Func<TModel, TVm> create, Action<TVm, TModel> update) where TVm : class, IKeyed`

Existing VM instances are kept (updated in place, moved, never replaced), so the ItemsView keeps its selection and scroll position; only vanished keys are removed and new keys inserted (Ref §4.2 "keyed diff that keeps selection and scroll").

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/CollectionSyncTests.cs
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace UasSort.Review.Tests;

public class CollectionSyncTests
{
    private sealed class Row(string key) : IKeyed
    {
        public string Key { get; } = key;
        public int Value { get; set; }
    }

    private static void Sync(ObservableCollection<Row> target, params (string Key, int Value)[] source)
        => CollectionSync.Sync(target, source, s => s.Key, s => new Row(s.Key) { Value = s.Value }, (r, s) => r.Value = s.Value);

    [Fact]
    public void CollectionSync_KeepsInstancesAndSelection()
    {
        var target = new ObservableCollection<Row>();
        Sync(target, ("a", 1), ("b", 2), ("c", 3));
        var selected = target[1];
        var events = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, e) => events.Add(e.Action);

        Sync(target, ("c", 30), ("b", 20), ("d", 4));

        Assert.Equal<string>(["c", "b", "d"], target.Select(r => r.Key));
        Assert.Same(selected, target[1]);
        Assert.Equal(20, selected.Value);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, events);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Replace, events);
    }

    [Fact]
    public void CollectionSync_EmptySourceClears()
    {
        var target = new ObservableCollection<Row>();
        Sync(target, ("a", 1));
        Sync(target);
        Assert.Empty(target);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*CollectionSync*"`
Expected: build FAILS (CS0246 `IKeyed`, CS0103 `CollectionSync`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Sync/CollectionSync.cs
namespace UasSort.Review;

/// <summary>A VM with a stable identity across re-derivations.</summary>
public interface IKeyed
{
    string Key { get; }
}

/// <summary>Keyed diff of an ObservableCollection against a new model list (Ref §4.2 CollectionSync).</summary>
public static class CollectionSync
{
    public static void Sync<TVm, TModel>(ObservableCollection<TVm> target, IReadOnlyList<TModel> source, Func<TModel, string> key,
                                         Func<TModel, TVm> create, Action<TVm, TModel> update)
        where TVm : class, IKeyed
    {
        var wanted = new HashSet<string>(source.Select(key), StringComparer.Ordinal);
        for (var i = target.Count - 1; i >= 0; i--)
            if (!wanted.Contains(target[i].Key)) target.RemoveAt(i);

        for (var i = 0; i < source.Count; i++)
        {
            var k = key(source[i]);
            if (i < target.Count && string.Equals(target[i].Key, k, StringComparison.Ordinal))
            {
                update(target[i], source[i]);
                continue;
            }
            var j = -1;
            for (var s = i + 1; s < target.Count; s++)
                if (string.Equals(target[s].Key, k, StringComparison.Ordinal)) { j = s; break; }
            if (j >= 0)
            {
                target.Move(j, i);
                update(target[i], source[i]);
            }
            else
            {
                var vm = create(source[i]);
                update(vm, source[i]);
                target.Insert(i, vm);
            }
        }
        while (target.Count > source.Count) target.RemoveAt(target.Count - 1);
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*CollectionSync*"`
Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Sync tests/UasSort.Review.Tests/CollectionSyncTests.cs
git commit -m "feat: keyed CollectionSync that keeps selection" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.6: Map message contract (`MapBridge` messages, golden tests)

**Files:**
- Create: `src/UasSort.Review/Map/MapMessages.cs`, `src/UasSort.Review/Map/MapBridge.cs`
- Test: `tests/UasSort.Review.Tests/MapMessageTests.cs`

**Interfaces:**
- Consumes: `GeoPoint`, Core `GeoPointJsonConverter` (Part 02 Task 02.5, `UasSort.Core.Json`, `[lon,lat]`), `IReviewLog`, `TimeProvider`.
- Produces (defined here):
  - `public closed record class HostToMap { int V }` (`[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]`) with cases `MapInit(MapConfig Config, string Base, double RadiusMiles, bool Online, string Theme)` "init", `MapSetData(int Rev, ImmutableArray<MapItem> Items, ImmutableArray<MapGroup> Groups, ImmutableArray<MapJump> Jumps)` "setData", `MapSelect(string GroupId, ImmutableArray<string> ItemIds, bool Fit, ImmutableArray<double> Bbox)` "select", `MapSetRadius(double RadiusMiles)` "setRadius", `MapSetBase(string Base)` "setBase", `MapSetTheme(string Theme)` "setTheme", `MapFit(ImmutableArray<double> Bbox)` "fit", `MapPing(int N)` "ping"
  - `public sealed record MapConfig(string StreetsStyleUrl, string StreetsDarkStyleUrl, string SatelliteUrl)`, `MapItem(string Id, string GroupId, double Lon, double Lat, string Kind)`, `MapGroup(string Id, string Color, GeoPoint Center, string Label)`, `MapJump(GeoPoint From, GeoPoint To, string Label)`
  - `public closed record class MapToHost { int V }` with cases `MapReady(string Maplibre, bool Webgl2)` "ready", `MapPong(int N)` "pong", `MapClick(ImmutableArray<string> ItemIds, string? GroupId, bool Ctrl, bool Shift)` "click", `MapClickEmpty(…same…)` "clickEmpty", `MapContextMenu(ImmutableArray<string> ItemIds, double X, double Y)` "contextMenu", `MapTileError(string? Base, string Message)` "tileError", `MapBaseUnavailable(string? Base, string Message)` "baseUnavailable", `MapError(string? Base, string Message)` "error"
  - `internal sealed partial class MapJsonContext : JsonSerializerContext` (camelCase, `AllowOutOfOrderMetadataProperties = true`, Core's `GeoPointJsonConverter` registered; Review has no converter of its own)
  - `public sealed class MapBridge : IDisposable` — `MapBridge(Action<string> post, IReviewLog log, TimeProvider time)`, `event Action<MapToHost>? Received`, `void Send(HostToMap m)`, `void Dispatch(string json)`, `static string Serialize(HostToMap m)`, `static MapToHost? Parse(string json)`, `static HostToMap? ParseHostMessage(string json)` (tests and selftest only)

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/MapMessageTests.cs
using System.Text.Json.Nodes;

namespace UasSort.Review.Tests;

public class MapMessageTests
{
    // Ref §9.6 golden examples, verbatim.
    private const string Init = """{"v":1,"type":"init","config":{"streetsStyleUrl":"https://tiles.openfreemap.org/styles/liberty","streetsDarkStyleUrl":"https://tiles.openfreemap.org/styles/dark","satelliteUrl":"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"},"base":"streets","radiusMiles":50,"online":true,"theme":"light"}""";
    private const string SetData = """{"v":1,"type":"setData","rev":7,"items":[{"id":"DCIM/DJI_001/DJI_20260927140627_0128_D.MP4","groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","lon":-153.738973,"lat":57.550442,"kind":"video"}],"groups":[{"id":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","color":"#1F77B4","center":[-153.7409,57.5415],"label":"Sep 27 · 13 clips"}],"jumps":[{"from":[-164.2657,64.6935],"to":[-165.3696,64.5627],"label":"34 mi · 21 h"}]}""";
    private const string Select = """{"v":1,"type":"select","groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","itemIds":[],"fit":true,"bbox":[-153.76,57.53,-153.72,57.56]}""";
    private const string SetRadius = """{"v":1,"type":"setRadius","radiusMiles":25}""";
    private const string SetBase = """{"v":1,"type":"setBase","base":"satellite"}""";
    private const string SetTheme = """{"v":1,"type":"setTheme","theme":"dark"}""";
    private const string Fit = """{"v":1,"type":"fit","bbox":[-165.40,64.55,-164.25,64.70]}""";
    private const string Ping = """{"v":1,"type":"ping","n":1}""";
    private const string Ready = """{"v":1,"type":"ready","maplibre":"6.11.2","webgl2":true}""";
    private const string Pong = """{"v":1,"type":"pong","n":1}""";
    private const string Click = """{"v":1,"type":"click","itemIds":["DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"],"groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","ctrl":false,"shift":false}""";
    private const string ClickEmpty = """{"v":1,"type":"clickEmpty","itemIds":[],"groupId":null,"ctrl":false,"shift":false}""";
    private const string ContextMenu = """{"v":1,"type":"contextMenu","itemIds":["DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"],"x":412,"y":288}""";
    private const string TileError = """{"v":1,"type":"tileError","base":"satellite","message":"HTTP 503"}""";
    private const string BaseUnavailable = """{"v":1,"type":"baseUnavailable","base":"satellite","message":"4 tile errors"}""";
    private const string Error = """{"v":1,"type":"error","base":null,"message":"Uncaught TypeError: …"}""";

    private const string Anchor = "DCIM/DJI_001/DJI_20260927140127_0123_D.MP4";
    private const string Clip128 = "DCIM/DJI_001/DJI_20260927140627_0128_D.MP4";

    private static void AssertSameJson(string golden, HostToMap message)
        => Assert.True(JsonNode.DeepEquals(JsonNode.Parse(golden), JsonNode.Parse(MapBridge.Serialize(message))),
                       $"expected {golden}\nactual   {MapBridge.Serialize(message)}");

    [Fact]
    public void MapMessages_HostToMap_SerialiseToTheGoldenObjects()
    {
        AssertSameJson(Init, new MapInit(new MapConfig("https://tiles.openfreemap.org/styles/liberty", "https://tiles.openfreemap.org/styles/dark",
            "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"), "streets", 50, true, "light"));
        AssertSameJson(SetData, new MapSetData(7,
            [new MapItem(Clip128, Anchor, -153.738973, 57.550442, "video")],
            [new MapGroup(Anchor, "#1F77B4", new GeoPoint(57.5415, -153.7409), "Sep 27 · 13 clips")],
            [new MapJump(new GeoPoint(64.6935, -164.2657), new GeoPoint(64.5627, -165.3696), "34 mi · 21 h")]));
        AssertSameJson(Select, new MapSelect(Anchor, [], true, [-153.76, 57.53, -153.72, 57.56]));
        AssertSameJson(SetRadius, new MapSetRadius(25));
        AssertSameJson(SetBase, new MapSetBase("satellite"));
        AssertSameJson(SetTheme, new MapSetTheme("dark"));
        AssertSameJson(Fit, new MapFit([-165.40, 64.55, -164.25, 64.70]));
        AssertSameJson(Ping, new MapPing(1));
    }

    [Fact]
    public void MapMessages_HostToMapGolden_DeserialiseWithVBeforeType()
    {
        var setData = Assert.IsType<MapSetData>(MapBridge.ParseHostMessage(SetData));
        Assert.Equal(new GeoPoint(57.5415, -153.7409), setData.Groups[0].Center);
        Assert.Equal(new GeoPoint(64.5627, -165.3696), setData.Jumps[0].To);
        Assert.Equal(1, Assert.IsType<MapPing>(MapBridge.ParseHostMessage(Ping)).N);
        Assert.IsType<MapInit>(MapBridge.ParseHostMessage(Init));
    }

    [Fact]
    public void MapMessages_MapToHostGolden_DeserialiseWithVBeforeType()
    {
        var ready = Assert.IsType<MapReady>(MapBridge.Parse(Ready));
        Assert.Equal("6.11.2", ready.Maplibre);
        Assert.True(ready.Webgl2);
        Assert.Equal(1, ready.V);
        Assert.Equal(1, Assert.IsType<MapPong>(MapBridge.Parse(Pong)).N);
        var click = Assert.IsType<MapClick>(MapBridge.Parse(Click));
        Assert.Equal(Clip128, Assert.Single(click.ItemIds));
        Assert.Equal(Anchor, click.GroupId);
        var empty = Assert.IsType<MapClickEmpty>(MapBridge.Parse(ClickEmpty));
        Assert.Null(empty.GroupId);
        Assert.Empty(empty.ItemIds);
        var menu = Assert.IsType<MapContextMenu>(MapBridge.Parse(ContextMenu));
        Assert.Equal(412, menu.X);
        Assert.Equal(288, menu.Y);
        Assert.Equal("HTTP 503", Assert.IsType<MapTileError>(MapBridge.Parse(TileError)).Message);
        Assert.Equal("satellite", Assert.IsType<MapBaseUnavailable>(MapBridge.Parse(BaseUnavailable)).Base);
        Assert.Null(Assert.IsType<MapError>(MapBridge.Parse(Error)).Base);
    }

    [Fact]
    public void MapBridge_Dispatch_RaisesReceivedAndLogsUnknownTypes()
    {
        var log = new ListLog();
        var sent = new List<string>();
        using var bridge = new MapBridge(sent.Add, log, new FakeTimeProvider());
        var got = new List<MapToHost>();
        bridge.Received += got.Add;

        bridge.Dispatch(Pong);
        bridge.Dispatch("""{"v":1,"type":"hover","itemIds":[]}""");
        bridge.Dispatch("not json");
        bridge.Send(new MapPing(2));

        Assert.IsType<MapPong>(Assert.Single(got));
        Assert.Equal(2, log.Warnings.Count);
        Assert.Contains("\"n\":2", Assert.Single(sent), StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*MapMessages*"`
Expected: build FAILS (CS0246 `HostToMap`, `MapBridge`, …).

- [ ] **Step 3: Implement the messages**

```csharp
// src/UasSort.Review/Map/MapMessages.cs
using System.Text.Json.Serialization;

namespace UasSort.Review;

#pragma warning disable CA1056 // wire format (Ref §9.6): map.js reads URLs as plain strings

/// <summary>Host → map messages (Ref §9.6). Every message carries v:1 and a "type" discriminator.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MapInit), "init")]
[JsonDerivedType(typeof(MapSetData), "setData")]
[JsonDerivedType(typeof(MapSelect), "select")]
[JsonDerivedType(typeof(MapSetRadius), "setRadius")]
[JsonDerivedType(typeof(MapSetBase), "setBase")]
[JsonDerivedType(typeof(MapSetTheme), "setTheme")]
[JsonDerivedType(typeof(MapFit), "fit")]
[JsonDerivedType(typeof(MapPing), "ping")]
public closed record class HostToMap
{
    [JsonPropertyOrder(-1)] public int V { get; init; } = 1;
}

public sealed record MapConfig(string StreetsStyleUrl, string StreetsDarkStyleUrl, string SatelliteUrl);
public sealed record MapItem(string Id, string GroupId, double Lon, double Lat, string Kind);
public sealed record MapGroup(string Id, string Color, GeoPoint Center, string Label);
public sealed record MapJump(GeoPoint From, GeoPoint To, string Label);

public sealed record class MapInit(MapConfig Config, string Base, double RadiusMiles, bool Online, string Theme) : HostToMap;
public sealed record class MapSetData(int Rev, ImmutableArray<MapItem> Items, ImmutableArray<MapGroup> Groups, ImmutableArray<MapJump> Jumps) : HostToMap;
public sealed record class MapSelect(string GroupId, ImmutableArray<string> ItemIds, bool Fit, ImmutableArray<double> Bbox) : HostToMap;
public sealed record class MapSetRadius(double RadiusMiles) : HostToMap;
public sealed record class MapSetBase(string Base) : HostToMap;
public sealed record class MapSetTheme(string Theme) : HostToMap;
public sealed record class MapFit(ImmutableArray<double> Bbox) : HostToMap;
public sealed record class MapPing(int N) : HostToMap;

/// <summary>Map → host messages (Ref §9.6). map.js sends "v" before "type".</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MapReady), "ready")]
[JsonDerivedType(typeof(MapPong), "pong")]
[JsonDerivedType(typeof(MapClick), "click")]
[JsonDerivedType(typeof(MapClickEmpty), "clickEmpty")]
[JsonDerivedType(typeof(MapContextMenu), "contextMenu")]
[JsonDerivedType(typeof(MapTileError), "tileError")]
[JsonDerivedType(typeof(MapBaseUnavailable), "baseUnavailable")]
[JsonDerivedType(typeof(MapError), "error")]
public closed record class MapToHost
{
    [JsonPropertyOrder(-1)] public int V { get; init; } = 1;
}

public sealed record class MapReady(string Maplibre, bool Webgl2) : MapToHost;
public sealed record class MapPong(int N) : MapToHost;
public sealed record class MapClick(ImmutableArray<string> ItemIds, string? GroupId, bool Ctrl, bool Shift) : MapToHost;
public sealed record class MapClickEmpty(ImmutableArray<string> ItemIds, string? GroupId, bool Ctrl, bool Shift) : MapToHost;
public sealed record class MapContextMenu(ImmutableArray<string> ItemIds, double X, double Y) : MapToHost;
public sealed record class MapTileError(string? Base, string Message) : MapToHost;
public sealed record class MapBaseUnavailable(string? Base, string Message) : MapToHost;
public sealed record class MapError(string? Base, string Message) : MapToHost;

/// <summary>GeoPoint on the wire is [lon,lat] (Ref §3, §9.6) through Core's GeoPointJsonConverter (Part 02).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
                             AllowOutOfOrderMetadataProperties = true,
                             Converters = [typeof(GeoPointJsonConverter)])]
[JsonSerializable(typeof(HostToMap))]
[JsonSerializable(typeof(MapToHost))]
internal sealed partial class MapJsonContext : JsonSerializerContext;
```

- [ ] **Step 4: Implement the bridge (send, dispatch; the plan projection and throttle come in Task 10.7)**

```csharp
// src/UasSort.Review/Map/MapBridge.cs
using System.Text.Json;

namespace UasSort.Review;

/// <summary>
/// The C# side of the map pane (Ref §9.6). Part 11's MapPane gives it PostWebMessageAsString as <c>post</c> and feeds
/// WebMessageReceived strings to <see cref="Dispatch"/>. It never throws on a bad message.
/// </summary>
public sealed partial class MapBridge : IDisposable
{
    private readonly Action<string> _post;
    private readonly IReviewLog _log;
    private readonly TimeProvider _time;

    public MapBridge(Action<string> post, IReviewLog log, TimeProvider time)
    {
        _post = post;
        _log = log;
        _time = time;
    }

    public event Action<MapToHost>? Received;

    public static string Serialize(HostToMap m) => JsonSerializer.Serialize(m, MapJsonContext.Default.HostToMap);
    public static MapToHost? Parse(string json) => JsonSerializer.Deserialize(json, MapJsonContext.Default.MapToHost);
    public static HostToMap? ParseHostMessage(string json) => JsonSerializer.Deserialize(json, MapJsonContext.Default.HostToMap);

    public void Send(HostToMap m) => _post(Serialize(m));

    public void Dispatch(string json)
    {
        MapToHost? message;
        try
        {
            message = Parse(json);
        }
        catch (JsonException ex)
        {
            _log.Warn("Map message ignored: " + ex.Message);
            return;
        }
        catch (NotSupportedException ex)
        {
            _log.Warn("Map message ignored: " + ex.Message);
            return;
        }
        if (message is null || message.GetType() == typeof(MapToHost))
        {
            _log.Warn("Map message ignored: unknown type");
            return;
        }
        Received?.Invoke(message);
    }

    public void Dispose() => DisposeThrottle();
}
```

- [ ] **Step 5: Temporary throttle stub so the partial class compiles**

```csharp
// src/UasSort.Review/Map/MapBridge.Data.cs  (replaced in full by Task 10.7)
namespace UasSort.Review;

public sealed partial class MapBridge
{
    private static void DisposeThrottle() { }
}
```

- [ ] **Step 6: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*MapMessages*"` then `-- --filter-method "*MapBridge_Dispatch*"`
Expected: PASS, 4 tests.

- [ ] **Step 7: Commit**

```bash
git add src/UasSort.Review/Map tests/UasSort.Review.Tests/MapMessageTests.cs
git commit -m "feat: map message contract with golden tests" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 10.7: Map projection of a plan, selection messages, 10/s throttle

**Files:**
- Modify: `src/UasSort.Review/Map/MapBridge.Data.cs` (replace the stub)
- Test: `tests/UasSort.Review.Tests/MapProjectionTests.cs`

**Interfaces:**
- Consumes: `Plan`, `VideoGroup`, `Boundary`, `Item`, `GroupTarget`/`AlreadyImported`, `MapSettings`, `Fmt`.
- Produces (defined here):
  - `public static class MapProjection` — `ImmutableArray<string> Palette`, `const string ImportedGrey = "#9E9E9E"`, `string Color(VideoGroup g)`, `MapSetData SetData(Plan plan, int rev)`, `MapSelect Select(Plan plan, GroupId group, IReadOnlyList<ItemId> itemIds, bool fit)`, `MapInit Init(MapSettings s, double radiusMiles, bool online, bool dark)`, `ImmutableArray<double> Bbox(IReadOnlyList<GeoPoint> points)`
  - `MapBridge.SendData(Plan plan)` (at most one `setData` per 100 ms; the latest plan is sent on the trailing edge), `int Rev { get; }`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/MapProjectionTests.cs
namespace UasSort.Review.Tests;

public class MapProjectionTests
{
    private static Plan Derive(IReadOnlyList<PlanClip> clips, double r = 50)
        => new ScriptedDeriver().Derive(TestPlans.Base(clips), new Tuning(r, 1), [], new SessionFlags(false), 1, TestContext.Current.CancellationToken);

    [Fact]
    public void MapProjection_SetData_ColoursLabelsAndItems()
    {
        var plan = Derive(TestPlans.Zachar());
        var data = MapProjection.SetData(plan, 7);

        Assert.Equal(7, data.Rev);
        Assert.Equal(3, data.Items.Length);
        Assert.All(data.Items, i => Assert.Equal("video", i.Kind));
        var g = Assert.Single(data.Groups);
        Assert.Equal("#1F77B4", g.Color);
        Assert.Equal("Sep 27 · 3 clips", g.Label);
        Assert.Empty(data.Jumps);
    }

    [Fact]
    public void MapProjection_NoGpsClip_UsesGroupCentreAsHollowDot()
    {
        var clips = TestPlans.Zachar().Append(new PlanClip("DJI_20260927143000_0149_D.MP4", TestPlans.Utc(2026, 9, 27, 18, 30), TestPlans.Anchorage, null, null)).ToList();
        var plan = Derive(clips);
        var data = MapProjection.SetData(plan, 1);

        var hollow = Assert.Single(data.Items, i => i.Kind == "videoNoGps");
        Assert.Equal(plan.Groups[0].Centroid!.Value.Lon, hollow.Lon);
    }

    [Fact]
    public void MapProjection_DistanceBoundary_BecomesJumpLineWithMilesLabel()
    {
        var plan = Derive(TestPlans.CouncilAnvil(), r: 25);
        var jump = Assert.Single(MapProjection.SetData(plan, 1).Jumps);
        Assert.Equal("34 mi · 24 h", jump.Label);
        Assert.Equal("#FF7F0E", MapProjection.SetData(plan, 1).Groups[1].Color);
    }

    [Fact]
    public void MapProjection_Select_FitsTheGroupsPoints()
    {
        var plan = Derive(TestPlans.Zachar());
        var sel = MapProjection.Select(plan, plan.Groups[0].Id, [TestPlans.Id(TestPlans.Zachar()[1].Name)], fit: true);

        Assert.Equal(plan.Groups[0].Id.Anchor.CardRelPath, sel.GroupId);
        Assert.Equal(TestPlans.Id(TestPlans.Zachar()[1].Name).CardRelPath, Assert.Single(sel.ItemIds));
        Assert.Equal(4, sel.Bbox.Length);
        Assert.True(sel.Bbox[0] <= -153.738973 && sel.Bbox[2] >= -153.738973);
        Assert.True(sel.Bbox[1] <= 57.5415 && sel.Bbox[3] >= 57.550442);
    }

    [Fact]
    public void MapBridge_SendData_ThrottlesToTenPerSecondWithTrailingLatest()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero));
        var sent = new List<string>();
        using var bridge = new MapBridge(sent.Add, new ListLog(), time);
        var p1 = Derive(TestPlans.CouncilAnvil(), 50);
        var p2 = Derive(TestPlans.CouncilAnvil(), 40);
        var p3 = Derive(TestPlans.CouncilAnvil(), 25);

        bridge.SendData(p1);
        bridge.SendData(p2);
        bridge.SendData(p3);
        Assert.Single(sent);

        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(2, sent.Count);
        var last = Assert.IsType<MapSetData>(MapBridge.ParseHostMessage(sent[1]));
        Assert.Equal(2, last.Groups.Length);
        Assert.Equal(2, last.Rev);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*MapProjection*"`
Expected: build FAILS (CS0103 `MapProjection`; `SendData` missing).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Map/MapBridge.Data.cs
namespace UasSort.Review;

/// <summary>C# computes every distance, label and colour (Ref §9.6); JS only draws.</summary>
public static class MapProjection
{
    public const string ImportedGrey = "#9E9E9E";

    public static ImmutableArray<string> Palette { get; } =
        ["#1F77B4", "#FF7F0E", "#2CA02C", "#D62728", "#9467BD", "#8C564B", "#E377C2", "#7F7F7F", "#BCBD22", "#17BECF"];

    public static string Color(VideoGroup g)
        => g.Target is AlreadyImported || g.ColorIndex < 0 ? ImportedGrey : Palette[g.ColorIndex % Palette.Length];

    public static MapInit Init(MapSettings s, double radiusMiles, bool online, bool dark)
        => new(new MapConfig(s.StreetsStyleUrl, s.StreetsDarkStyleUrl, s.SatelliteUrl), online ? s.Base : "none", radiusMiles, online, dark ? "dark" : "light");

    public static MapSetData SetData(Plan plan, int rev)
    {
        var byId = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var items = ImmutableArray.CreateBuilder<MapItem>();
        var groups = ImmutableArray.CreateBuilder<MapGroup>();
        foreach (var g in plan.Groups)
        {
            var gid = g.Id.Anchor.CardRelPath;
            foreach (var id in g.Videos)
            {
                if (!byId.TryGetValue(id, out var item)) continue;
                if (item.Gps is { } fix) items.Add(new MapItem(id.CardRelPath, gid, fix.Point.Lon, fix.Point.Lat, "video"));
                else if (g.Centroid is { } c) items.Add(new MapItem(id.CardRelPath, gid, c.Lon, c.Lat, "videoNoGps"));
            }
            if (g.Centroid is { } centre)
                groups.Add(new MapGroup(gid, Color(g), centre, $"{Fmt.DateRange(g.Start, g.End)} · {Fmt.Count(g.Videos.Length, "clip", "clips")}"));
        }
        var centres = plan.Groups.ToDictionary(g => g.Id, g => g.Centroid);
        var jumps = ImmutableArray.CreateBuilder<MapJump>();
        foreach (var b in plan.Boundaries)
        {
            if (b.Jump is not { } jump || centres[b.Left] is not { } from || centres[b.Right] is not { } to) continue;
            jumps.Add(new MapJump(from, to, $"{Fmt.Miles(jump)} · {Fmt.Gap(b.Gap)}"));
        }
        return new MapSetData(rev, items.ToImmutable(), groups.ToImmutable(), jumps.ToImmutable());
    }

    public static MapSelect Select(Plan plan, GroupId group, IReadOnlyList<ItemId> itemIds, bool fit)
    {
        var g = plan.Groups.FirstOrDefault(x => x.Id == group);
        HashSet<ItemId> ids = g is null ? [] : g.Videos.ToHashSet();
        var points = plan.Base.Items.Where(i => ids.Contains(i.Raw.Unit.Id) && i.Gps is not null).Select(i => i.Gps!.Point).ToList();
        if (points.Count == 0 && g?.Centroid is { } c) points.Add(c);
        return new MapSelect(group.Anchor.CardRelPath, [.. itemIds.Select(i => i.CardRelPath)], fit, Bbox(points));
    }

    public static ImmutableArray<double> Bbox(IReadOnlyList<GeoPoint> points)
    {
        if (points.Count == 0) return [];
        const double pad = 0.005;
        return [points.Min(p => p.Lon) - pad, points.Min(p => p.Lat) - pad, points.Max(p => p.Lon) + pad, points.Max(p => p.Lat) + pad];
    }
}

public sealed partial class MapBridge
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(100);
    private readonly Lock _throttleLock = new();
    private ITimer? _trailing;
    private Plan? _pending;
    private DateTimeOffset _lastSent = DateTimeOffset.MinValue;

    /// <summary>Revision counter of the setData messages sent so far.</summary>
    public int Rev { get; private set; }

    /// <summary>Sends setData for this plan, at most 10 per second; a burst ends with the latest plan (Ref §9.6).</summary>
    public void SendData(Plan plan)
    {
        lock (_throttleLock)
        {
            var now = _time.GetUtcNow();
            if (_trailing is null && now - _lastSent >= MinInterval)
            {
                SendNow(plan, now);
                return;
            }
            _pending = plan;
            if (_trailing is null)
            {
                var due = MinInterval - (now - _lastSent);
                _trailing = _time.CreateTimer(_ => FlushPending(), null, due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void FlushPending()
    {
        lock (_throttleLock)
        {
            _trailing?.Dispose();
            _trailing = null;
            if (_pending is { } p)
            {
                _pending = null;
                SendNow(p, _time.GetUtcNow());
            }
        }
    }

    private void SendNow(Plan plan, DateTimeOffset now)
    {
        _lastSent = now;
        Rev++;
        Send(MapProjection.SetData(plan, Rev));
    }

    private void DisposeThrottle()
    {
        lock (_throttleLock)
        {
            _trailing?.Dispose();
            _trailing = null;
        }
    }
}
```

Note: `DisposeThrottle` is now an instance method; `MapBridge.Dispose()` from Task 10.6 calls it unchanged.

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Map*"`
Expected: PASS (MapMessages, MapBridge, MapProjection: 9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Map tests/UasSort.Review.Tests/MapProjectionTests.cs
git commit -m "feat: map projection of plans and 10 per second setData throttle" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.8: Group card, boundary chip, folded run, clip row, day-split banner, suggestion VMs

**Files:**
- Create: `src/UasSort.Review/Review/ReviewActions.cs`, `src/UasSort.Review/Review/PlanIndex.cs`, `src/UasSort.Review/Review/SmallVms.cs`, `src/UasSort.Review/Review/ClipRowVm.cs`, `src/UasSort.Review/Review/GroupCardVm.cs`
- Test: `tests/UasSort.Review.Tests/GroupCardVmTests.cs`, `tests/UasSort.Review.Tests/Fakes/RecordingActions.cs`

**Interfaces:**
- Consumes: `Plan`, `VideoGroup`, `GroupTarget` cases, `Boundary`/`BoundaryCause`, `DaySplit`, `Suggestion`/`DescSource`, `Issue`/`QuickFix`, `Item`/`Newness` cases, `LibraryFolder`, `Planner.AppendCandidates(Plan, GroupId, int max = 8)` (Part 06, static; the flyout never ranks folders itself), Core `GeoMath.Haversine`, Core `ZoneNames.Abbreviation`, `Fmt`, `ClockText`, `MapProjection.Color`, `IKeyed`.
- Produces (defined here):
  - `public interface IReviewActions` — `Task SetIncludedAsync(IReadOnlyList<ItemId> items, bool included)`, `Task SplitBeforeAsync(ItemId first)`, `Task MergeAsync(ItemId inA, ItemId inB)`, `Task ApplyQuickFixAsync(QuickFix fix)`, `Task RenameAsync(GroupCardVm card, string text)`, `Task RetargetAsync(GroupCardVm card, RetargetOptionVm option)`
  - `public sealed class PlanIndex` — `PlanIndex(Plan plan)`, `Plan Plan`, `IReadOnlyDictionary<ItemId, Item> Items`, `string VideoRoot`, `Boundary? Before(GroupId g)`, `IReadOnlyList<RetargetOptionVm> Candidates(VideoGroup g)` (the folders `Planner.AppendCandidates(Plan, g.Id)` returns, in its order), `string PhotoCounts(VideoGroup g)`
  - `public enum ChipKind { Unfinished, Conflicts, CheckDate, GpsGuessed, ClockMismatch, EmptyName, CrossDayAppend, EmphasisedSplit, PinChanged, ConflictingPins }`
  - `public sealed class QuickFixVm` (`Label`, `IAsyncRelayCommand Command`, `ToString()` = label), `public sealed class ChipVm` (`Kind`, `Text`, `Tooltip`, `IReadOnlyList<QuickFixVm> Actions`), `public sealed class ThumbVm(ItemId key)` (`Key`), `public sealed class SuggestionVm(Suggestion s)` (`Text`, `Detail`, `ToString()` = `Text`)
  - `public enum RetargetKind { Auto, NewFolder, Append, Browse, Skip }`, `public sealed class RetargetOptionVm(RetargetKind kind, string label, string? detail, string? folderPath, DateOnly? folderDate)`
  - `public sealed class BoundaryChipVm` (`Text`, `ButtonText`, `CanMerge`, `MergeTooltip`, `IAsyncRelayCommand MergeCommand`)
  - `public sealed class DaySplitBannerVm` (`Text`, `IsEmphasised`, `EmphasisText`, `IAsyncRelayCommand SplitCommand`)
  - `public abstract partial class TimelineEntryVm : ObservableObject, IKeyed`
  - `public sealed partial class GroupCardVm : TimelineEntryVm` (members listed in the code below; `internal void Update(VideoGroup g, PlanIndex ix)`)
  - `public sealed partial class FoldedRunVm : TimelineEntryVm` (`Text`, `Groups`, `IsExpanded`, `ClipCount`, `internal void Update(IReadOnlyList<VideoGroup> run)`)
  - `public sealed partial class ClipRowVm : ObservableObject, IKeyed` (members in the code below; `internal void Update(Item item, bool included, VideoGroup group, bool readOnly)`)

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/Fakes/RecordingActions.cs
namespace UasSort.Review.Tests;

internal sealed class RecordingActions : IReviewActions
{
    public List<string> Calls { get; } = [];
    public List<QuickFix> Fixes { get; } = [];
    public Task SetIncludedAsync(IReadOnlyList<ItemId> items, bool included) { Calls.Add($"include {items.Count} {included}"); return Task.CompletedTask; }
    public Task SplitBeforeAsync(ItemId first) { Calls.Add("split " + first.CardRelPath); return Task.CompletedTask; }
    public Task MergeAsync(ItemId inA, ItemId inB) { Calls.Add($"merge {inA.CardRelPath} {inB.CardRelPath}"); return Task.CompletedTask; }
    public Task ApplyQuickFixAsync(QuickFix fix) { Fixes.Add(fix); Calls.Add("fix " + fix.Label); return Task.CompletedTask; }
    public Task RenameAsync(GroupCardVm card, string text) { Calls.Add("rename " + text); return Task.CompletedTask; }
    public Task RetargetAsync(GroupCardVm card, RetargetOptionVm option) { Calls.Add("retarget " + option.Kind); return Task.CompletedTask; }
}
```

```csharp
// tests/UasSort.Review.Tests/GroupCardVmTests.cs
namespace UasSort.Review.Tests;

public class GroupCardVmTests
{
    private static Plan Derive(IReadOnlyList<PlanClip> clips, double r = 50, IReadOnlyList<PlanEdit>? edits = null,
                               ImmutableArray<Suggestion> suggestions = default, LibraryListings? listings = null)
        => new ScriptedDeriver(suggestions).Derive(TestPlans.Base(clips, listings: listings), new Tuning(r, 1), edits ?? [],
                                                   new SessionFlags(false), 1, TestContext.Current.CancellationToken);

    private static GroupCardVm Card(Plan plan, int index, RecordingActions actions)
    {
        var card = new GroupCardVm(plan.Groups[index].Id, actions);
        card.Update(plan.Groups[index], new PlanIndex(plan));
        return card;
    }

    [Fact]
    public async Task GroupCard_DistanceChip_MergesThroughActions()
    {
        var actions = new RecordingActions();
        var plan = Derive(TestPlans.CouncilAnvil(), r: 25);
        var second = Card(plan, 1, actions);

        Assert.NotNull(second.Chip);
        Assert.Equal("── 34 mi jump · 24 h ──", second.Chip!.Text);
        Assert.Equal("Merge", second.Chip.ButtonText);
        Assert.True(second.Chip.CanMerge);
        await second.Chip.MergeCommand.ExecuteAsync(null);
        Assert.Equal($"merge {plan.Groups[0].Id.Anchor.CardRelPath} {plan.Groups[1].Id.Anchor.CardRelPath}", Assert.Single(actions.Calls));
        Assert.Null(Card(plan, 0, actions).Chip);
    }

    [Fact]
    public void GroupCard_UserSplitChip_OffersUndoSplit()
    {
        var anvil = TestPlans.Id(TestPlans.CouncilAnvil()[2].Name);
        var plan = Derive(TestPlans.CouncilAnvil(), edits: [new SplitBefore(anvil)]);
        var card = Card(plan, 1, new RecordingActions());

        Assert.Equal("── split by you ──", card.Chip!.Text);
        Assert.Equal("Undo split", card.Chip.ButtonText);
    }

    [Fact]
    public void GroupCard_LibraryFolderChip_CannotMerge()
    {
        var b = new Boundary(new GroupId(new ItemId("a")), new GroupId(new ItemId("b")), BoundaryCause.LibraryFolder, null, TimeSpan.FromDays(3), 3);
        var chip = new BoundaryChipVm(b, new RecordingActions());
        Assert.Equal("── different library folder ──", chip.Text);
        Assert.False(chip.CanMerge);
        Assert.False(chip.MergeCommand.CanExecute(null));
        Assert.Equal("These clips are already in two different folders", chip.MergeTooltip);
        var dayGap = new BoundaryChipVm(b with { Cause = BoundaryCause.DayGap, DayGap = 62 }, new RecordingActions());
        Assert.Equal("── 62 days ──", dayGap.Text);
    }

    [Fact]
    public async Task GroupCard_EmphasisedDaySplit_ChipSplitsWithOneQuickFix()
    {
        var actions = new RecordingActions();
        var plan = Derive(TestPlans.CouncilAnvil());
        var card = Card(plan, 0, actions);

        var chip = Assert.Single(card.Chips, c => c.Kind == ChipKind.EmphasisedSplit);
        Assert.Equal("2 days · 34 mi apart", chip.Text);
        await Assert.Single(chip.Actions).Command.ExecuteAsync(null);
        var fix = Assert.Single(actions.Fixes);
        Assert.Equal(new SplitBefore(TestPlans.Id(TestPlans.CouncilAnvil()[2].Name)), Assert.Single(fix.Edits));
    }

    [Fact]
    public void GroupCard_ClockChip_OnlyOnGroupsWithAMismatchMember()
    {
        var clips = TestPlans.CouncilAnvil().Concat(TestPlans.Zachar()).ToList();
        var plan = Derive(clips);
        var cards = Enumerable.Range(0, plan.Groups.Length).Select(i => Card(plan, i, new RecordingActions())).ToList();

        var withChip = cards.Where(c => c.Chips.Any(ch => ch.Kind == ChipKind.ClockMismatch)).ToList();
        var zachar = Assert.Single(withChip);
        Assert.Equal(TestPlans.Id(TestPlans.Zachar()[0].Name), zachar.Anchor);
        var chip = zachar.Chips.Single(ch => ch.Kind == ChipKind.ClockMismatch);
        Assert.Equal("clock ≠ local", chip.Text);
        Assert.Equal("The drone clock (UTC−4) doesn't match local time here (UTC−8). Dates use local time.", chip.Tooltip);
        Assert.True(zachar.HasClockMismatch);
    }

    [Fact]
    public void GroupCard_NewFolderFields()
    {
        var plan = Derive(TestPlans.Zachar());
        var card = Card(plan, 0, new RecordingActions());

        Assert.Equal("NEW FOLDER", card.Badge);
        Assert.Equal(@"2026\2026-09\2026-09-27", card.TargetPath);
        Assert.False(card.IsDescriptionReadOnly);
        Assert.Equal("Sep 27 10:01–10:24 AKDT", card.DateRangeText);
        Assert.Equal("Videos 3 new / 3 · 3.6 GB", card.VideoCountsText);
        Assert.Equal("#1F77B4", card.Swatch);
        Assert.Equal(3, card.Thumbs.Count);
        Assert.Null(card.MoreThumbsText);
        Assert.Contains(card.Chips, c => c.Kind == ChipKind.EmptyName && c.Text == "Name this folder");
    }

    [Fact]
    public void GroupCard_SuggestionPrefill_IsItalicAndSuggestionVmToStringIsText()
    {
        var plan = Derive(TestPlans.Zachar(), suggestions: [new Suggestion("Zachar Bay", DescSource.Feature, Distance.FromMiles(0.4), new DateOnly(2026, 9, 27))]);
        var card = Card(plan, 0, new RecordingActions());

        Assert.Equal("Zachar Bay", card.Description);
        Assert.True(card.DescriptionIsSuggestion);
        var s = Assert.Single(card.Suggestions);
        Assert.Equal("Zachar Bay", s.ToString());
        Assert.Equal("feature · 0.4 mi", s.Detail);
        Assert.Equal("near Zachar Bay · spread 0.4 mi", card.LocationText);
    }

    [Fact]
    public void GroupCard_Append_IsReadOnlyWithHint()
    {
        var anchor = TestPlans.Id(TestPlans.Zachar()[0].Name);
        var folder = TestPlans.VideoRoot + @"\2026\2026-09\2026-09-27 Zachar Bay";
        var plan = Derive(TestPlans.Zachar(), edits: [new Retarget(anchor, new AppendTo(folder), false, [anchor])]);
        var card = Card(plan, 0, new RecordingActions());

        Assert.Equal("APPEND", card.Badge);
        Assert.True(card.IsDescriptionReadOnly);
        Assert.Equal("Appending to an existing folder · [New folder instead]", card.ReadOnlyHint);
        Assert.Equal(@"2026\2026-09\2026-09-27 Zachar Bay", card.TargetPath);
    }

    [Fact]
    public void GroupCard_RetargetOptions_AutoNewCandidatesBrowseSkip()
    {
        var folder = TestPlans.VideoRoot + @"\2026\2026-09\2026-09-26 Kodiak";
        var t = TestPlans.Utc(2026, 9, 26, 20, 0);
        ImmutableArray<FsEntry> entries =
        [
            new(TestPlans.VideoRoot + @"\2026", "2026", true, 0, t, t, t, 0x10),
            new(TestPlans.VideoRoot + @"\2026\2026-09", @"2026\2026-09", true, 0, t, t, t, 0x10),
            new(folder, @"2026\2026-09\2026-09-26 Kodiak", true, 0, t, t, t, 0x10),
            new(folder + @"\DJI_20260926120000_0100_D.MP4", @"2026\2026-09\2026-09-26 Kodiak\DJI_20260926120000_0100_D.MP4", false, 5_000_000, t, t, t, 0x20),
        ];
        var plan = Derive(TestPlans.Zachar(), listings: TestPlans.Listings(entries));
        var card = Card(plan, 0, new RecordingActions());

        var options = card.RetargetOptions();
        Assert.Equal<RetargetKind>([RetargetKind.Auto, RetargetKind.NewFolder, RetargetKind.Append, RetargetKind.Browse, RetargetKind.Skip],
                                   options.Select(o => o.Kind));
        var candidate = options[2];
        Assert.Equal("Kodiak", candidate.Label);
        Assert.StartsWith("Sep 26 · ", candidate.Detail, StringComparison.Ordinal);
        Assert.Equal("Browse existing…", options[3].Label);
        Assert.Equal("Skip this group", options[4].ToString());
    }

    [Fact]
    public async Task ClipRow_FieldsBannerAndToggle()
    {
        var actions = new RecordingActions();
        var plan = Derive(TestPlans.CouncilAnvil());
        var ix = new PlanIndex(plan);
        var g = plan.Groups[0];
        var anvil = TestPlans.Id(TestPlans.CouncilAnvil()[2].Name);
        var row = new ClipRowVm(anvil, actions);
        row.Update(ix.Items[anvil], plan.Included.Contains(anvil), g, readOnly: false);

        Assert.Equal("DJI_20260726195645_0001_D.MP4", row.ToString());
        Assert.Equal("19:56 AKDT", row.TimeText);
        Assert.Equal("1.2 GB", row.SizeText);
        Assert.Equal("New", row.StatusText);
        Assert.True(row.IsIncluded);
        Assert.NotNull(row.Banner);
        Assert.Equal("── Jul 25 → Jul 26 · 34 mi apart · 24 h ──", row.Banner!.Text);
        Assert.True(row.Banner.IsEmphasised);
        Assert.Equal("Likely separate outing", row.Banner.EmphasisText);

        await row.ToggleIncludedCommand.ExecuteAsync(null);
        await row.Banner.SplitCommand.ExecuteAsync(null);
        Assert.Equal<string>(["include 1 False", "split " + anvil.CardRelPath], actions.Calls);
    }

    [Fact]
    public void ClipRow_TruncatedClipShowsUnfinishedPill()
    {
        var clip = TestPlans.Zachar()[0] with { Flags = ItemFlags.Truncated };
        var plan = Derive([clip]);
        var row = new ClipRowVm(TestPlans.Id(clip.Name), new RecordingActions());
        row.Update(new PlanIndex(plan).Items[TestPlans.Id(clip.Name)], false, plan.Groups[0], false);

        Assert.Equal("Unfinished", row.StatusText);
        Assert.StartsWith("Unfinished recording. Powering the drone on", row.StatusTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void FoldedRun_TextCountsGroups()
    {
        var plan = Derive(TestPlans.CouncilAnvil(), r: 25);
        var run = new FoldedRunVm("f:x");
        run.Update(plan.Groups);
        Assert.Equal("2 groups already in library", run.Text);
        Assert.Equal(4, run.ClipCount);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*GroupCard*"`
Expected: build FAILS (CS0246 `IReviewActions`, `GroupCardVm`, `PlanIndex`, …).

- [ ] **Step 3: Implement the actions seam and the plan index**

```csharp
// src/UasSort.Review/Review/ReviewActions.cs
namespace UasSort.Review;

/// <summary>What row and card VMs ask the Review stage to do; ReviewVm implements it (Task 10.10).</summary>
public interface IReviewActions
{
    Task SetIncludedAsync(IReadOnlyList<ItemId> items, bool included);
    Task SplitBeforeAsync(ItemId first);
    Task MergeAsync(ItemId inA, ItemId inB);
    Task ApplyQuickFixAsync(QuickFix fix);
    Task RenameAsync(GroupCardVm card, string text);
    Task RetargetAsync(GroupCardVm card, RetargetOptionVm option);
}
```

```csharp
// src/UasSort.Review/Review/PlanIndex.cs
namespace UasSort.Review;

/// <summary>Lookups computed once per derived plan and shared by every card and row.</summary>
public sealed class PlanIndex
{
    private readonly Dictionary<GroupId, Boundary> _before = [];

    public PlanIndex(Plan plan)
    {
        Plan = plan;
        Items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        foreach (var b in plan.Boundaries) _before[b.Right] = b;
    }

    public Plan Plan { get; }
    public IReadOnlyDictionary<ItemId, Item> Items { get; }
    public string VideoRoot => Plan.Base.Scan.Settings.VideoRoot;

    public Boundary? Before(GroupId g) => _before.GetValueOrDefault(g);

    /// <summary>Append candidates for the retarget flyout: exactly the folders Planner.AppendCandidates returns for this group, in its
    /// order (Ref §9.4 item 4; the Planner's own ranking, never a second one here). Only the label and the detail line are Review's.</summary>
    public IReadOnlyList<RetargetOptionVm> Candidates(VideoGroup g)
        => [.. Planner.AppendCandidates(Plan, g.Id)
                .Select(f =>
                {
                    Distance? away = f.Centroid is { } fc && g.Centroid is { } gc ? GeoMath.Haversine(fc, gc) : null;
                    return new RetargetOptionVm(RetargetKind.Append,
                        f.Ref.Description.Length > 0 ? f.Ref.Description : Path.GetFileName(f.Ref.FullPath),
                        $"{Fmt.Day(f.Ref.NameDate)} · {(away is { } d ? Fmt.Miles(d) : "location unknown")}",
                        f.Ref.FullPath, f.Ref.NameDate);
                })];

    public string PhotoCounts(VideoGroup g)
    {
        var days = Plan.Base.PhotoDays.Where(d => d.Date >= g.Start && d.Date <= g.End).SelectMany(d => d.Items).ToList();
        if (days.Count == 0) return "";
        var fresh = days.Count(id => Items.TryGetValue(id, out var i) && i.Newness is IsNew);
        var probably = days.Count(id => Items.TryGetValue(id, out var i) && i.Newness is ProbablyImported);
        return string.Create(CultureInfo.InvariantCulture, $"Photos that day: {fresh} new, {probably} probably imported");
    }
}
```

- [ ] **Step 4: Implement the small VMs**

```csharp
// src/UasSort.Review/Review/SmallVms.cs
namespace UasSort.Review;

public enum ChipKind { Unfinished, Conflicts, CheckDate, GpsGuessed, ClockMismatch, EmptyName, CrossDayAppend, EmphasisedSplit, PinChanged, ConflictingPins }

public enum RetargetKind { Auto, NewFolder, Append, Browse, Skip }

public sealed class QuickFixVm
{
    public QuickFixVm(string label, Func<Task> run)
    {
        Label = label;
        Command = new AsyncRelayCommand(run);
    }

    public QuickFixVm(QuickFix fix, IReviewActions actions) : this(fix.Label, () => actions.ApplyQuickFixAsync(fix)) { }

    public string Label { get; }
    public IAsyncRelayCommand Command { get; }
    public override string ToString() => Label;
}

public sealed class ChipVm(ChipKind kind, string text, string? tooltip, IReadOnlyList<QuickFixVm> actions)
{
    public ChipKind Kind { get; } = kind;
    public string Text { get; } = text;
    public string? Tooltip { get; } = tooltip;
    public IReadOnlyList<QuickFixVm> Actions { get; } = actions;
    public override string ToString() => Text;
}

public sealed class ThumbVm(ItemId key)
{
    public ItemId Key { get; } = key;
}

public sealed class SuggestionVm(Suggestion s)
{
    public Suggestion Model { get; } = s;
    public string Text => Model.Text;
    public string Detail => Model.Source switch
    {
        DescSource.ExistingFolder => "existing folder",
        DescSource.Ledger => "used before" + Away(),
        DescSource.Feature => "feature" + Away(),
        DescSource.Place => "place" + Away(),
        DescSource.Town => "town" + Away(),
        DescSource.User => "your name",
        DescSource.None => "",
        _ => "",
    };
    public override string ToString() => Text;
    private string Away() => Model.Away is { } d ? " · " + Fmt.Miles(d) : "";
}

public sealed class RetargetOptionVm(RetargetKind kind, string label, string? detail, string? folderPath, DateOnly? folderDate)
{
    public RetargetKind Kind { get; } = kind;
    public string Label { get; } = label;
    public string? Detail { get; } = detail;
    public string? FolderPath { get; } = folderPath;
    public DateOnly? FolderDate { get; } = folderDate;
    public override string ToString() => Label;
}

/// <summary>The boundary chip drawn as the header of the next group card (Ref §8.3, §9.3).</summary>
public sealed class BoundaryChipVm
{
    public BoundaryChipVm(Boundary b, IReviewActions actions)
    {
        Model = b;
        var core = b.Cause switch
        {
            BoundaryCause.Distance => $"{(b.Jump is { } j ? Fmt.Miles(j) : "far")} jump · {Fmt.Gap(b.Gap)}",
            BoundaryCause.DayGap => Fmt.Count(b.DayGap, "day", "days"),
            BoundaryCause.LibraryFolder => "different library folder",
            BoundaryCause.UserSplit => "split by you",
            _ => "",
        };
        Text = $"── {core} ──";
        CanMerge = b.Cause != BoundaryCause.LibraryFolder;
        ButtonText = b.Cause == BoundaryCause.UserSplit ? "Undo split" : "Merge";
        MergeTooltip = CanMerge ? null : "These clips are already in two different folders";
        MergeCommand = new AsyncRelayCommand(() => actions.MergeAsync(b.Left.Anchor, b.Right.Anchor), () => CanMerge);
    }

    public Boundary Model { get; }
    public string Text { get; }
    public string ButtonText { get; }
    public bool CanMerge { get; }
    public string? MergeTooltip { get; }
    public IAsyncRelayCommand MergeCommand { get; }
    public override string ToString() => Text;
}

/// <summary>The day-change banner drawn inside the first clip row of the new day (Ref §8.3, §9.5).</summary>
public sealed class DaySplitBannerVm
{
    public DaySplitBannerVm(DaySplit s, IReviewActions actions)
    {
        Model = s;
        var apart = s.Apart is { } d ? $"{Fmt.Miles(d)} apart" : "location unknown";
        Text = $"── {Fmt.Day(s.From)} → {Fmt.Day(s.To)} · {apart} · {Fmt.Gap(s.Gap)} ──";
        SplitCommand = new AsyncRelayCommand(() => actions.SplitBeforeAsync(s.FirstOfDay));
    }

    public DaySplit Model { get; }
    public string Text { get; }
    public bool IsEmphasised => Model.Emphasised;
    public string? EmphasisText => Model.Emphasised ? "Likely separate outing" : null;
    public IAsyncRelayCommand SplitCommand { get; }
    public override string ToString() => Text;
}

public abstract partial class TimelineEntryVm : ObservableObject, IKeyed
{
    public abstract string Key { get; }
}

/// <summary>A run of AlreadyImported groups folded into one grey row (Ref §9.3).</summary>
public sealed partial class FoldedRunVm(string key) : TimelineEntryVm
{
    public override string Key { get; } = key;
    public IReadOnlyList<VideoGroup> Groups { get; private set; } = [];
    [ObservableProperty] public partial string Text { get; private set; } = "";
    [ObservableProperty] public partial int ClipCount { get; private set; }
    [ObservableProperty] public partial bool IsExpanded { get; set; }

    internal void Update(IReadOnlyList<VideoGroup> run)
    {
        Groups = run;
        Text = Fmt.Count(run.Count, "group", "groups") + " already in library";
        ClipCount = run.Sum(g => g.Videos.Length);
    }

    public override string ToString() => Text;
}
```

- [ ] **Step 5: Implement the clip row**

```csharp
// src/UasSort.Review/Review/ClipRowVm.cs
namespace UasSort.Review;

/// <summary>One clip in the clip list (Ref §9.5).</summary>
public sealed partial class ClipRowVm : ObservableObject, IKeyed
{
    private const string UnfinishedTooltip =
        "Unfinished recording. Powering the drone on with this card inserted may repair it (UNVERIFIED on the Air 3S). Rescan afterwards, or tick to copy as-is.";

    private readonly IReviewActions _actions;

    public ClipRowVm(ItemId id, IReviewActions actions)
    {
        Id = id;
        _actions = actions;
        ToggleIncludedCommand = new AsyncRelayCommand(() => _actions.SetIncludedAsync([Id], !IsIncluded), () => !IsReadOnly);
        SplitBeforeCommand = new AsyncRelayCommand(() => _actions.SplitBeforeAsync(Id), () => !IsReadOnly);
    }

    public ItemId Id { get; }
    public ItemId ThumbKey => Id;
    public string Key => Id.CardRelPath;

    [ObservableProperty] public partial string Name { get; private set; } = "";
    [ObservableProperty] public partial string TimeText { get; private set; } = "";
    [ObservableProperty] public partial string TimeGlyph { get; private set; } = "";
    [ObservableProperty] public partial string TimeTooltip { get; private set; } = "";
    [ObservableProperty] public partial string SizeText { get; private set; } = "";
    [ObservableProperty] public partial string StatusText { get; private set; } = "";
    [ObservableProperty] public partial string StatusTooltip { get; private set; } = "";
    [ObservableProperty] public partial string DistanceText { get; private set; } = "";
    [ObservableProperty] public partial bool IsIncluded { get; private set; }
    [ObservableProperty] public partial bool IsReadOnly { get; private set; }
    [ObservableProperty] public partial DaySplitBannerVm? Banner { get; private set; }

    public IAsyncRelayCommand ToggleIncludedCommand { get; }
    public IAsyncRelayCommand SplitBeforeCommand { get; }
    public Item? Item { get; private set; }

    internal void Update(Item item, bool included, VideoGroup group, bool readOnly)
    {
        Item = item;
        Name = item.Raw.Name;
        TimeText = ClockText.TimeCell(item);
        TimeGlyph = ClockText.SourceGlyph(item.Time.Source);
        TimeTooltip = ClockText.TimeTooltip(item);
        SizeText = Fmt.Size(item.Raw.Bytes);
        (StatusText, StatusTooltip) = Status(item);
        DistanceText = item.Gps is { } fix && group.Centroid is { } c ? Fmt.Miles(GeoMath.Haversine(fix.Point, c)) : "";
        IsIncluded = included;
        IsReadOnly = readOnly;
        var split = group.DaySplits.FirstOrDefault(s => s.FirstOfDay == Id);
        if (split is null) Banner = null;
        else if (Banner is null || Banner.Model != split) Banner = new DaySplitBannerVm(split, _actions);
        ToggleIncludedCommand.NotifyCanExecuteChanged();
        SplitBeforeCommand.NotifyCanExecuteChanged();
    }

    internal static (string Text, string Tooltip) Status(Item item)
    {
        if (item.Flags.HasFlag(ItemFlags.Truncated)) return ("Unfinished", UnfinishedTooltip);
        return item.Newness switch
        {
            IsNew n => ("New", n.Why switch
            {
                NewReason.NoMatch => "Not in the library",
                NewReason.SeenNotCopied => "Seen before, not copied",
                NewReason.AfterWatermark => "After the last imported video",
                NewReason.NearWatermark => "Near the last imported video; time estimated",
                NewReason.DayHasNewVideos => "Day has new videos",
                _ => "New",
            }),
            Imported im => ("Imported", im.Why),
            Decided d => d.Kind == DecisionKind.Dismissed
                ? ("Dismissed", $"You marked it not needed on {Fmt.Day(DateOnly.FromDateTime(d.AtUtc))}")
                : ("Confirmed by you", $"You recorded it as imported on {Fmt.Day(DateOnly.FromDateTime(d.AtUtc))}"),
            ProbablyImported p => ("Probably imported", p.Why),
            Conflict c => ("Conflict", $"A different file named {item.Raw.Name} exists: {c.ExistingPath}, {Fmt.Size(c.ExistingSize)}"),
        };
    }

    public override string ToString() => Name;
}
```

- [ ] **Step 6: Implement the group card**

```csharp
// src/UasSort.Review/Review/GroupCardVm.cs
namespace UasSort.Review;

/// <summary>One group on the timeline, with its preceding boundary chip as the header (Ref §9.3, §9.4).</summary>
public sealed partial class GroupCardVm : TimelineEntryVm
{
    private readonly IReviewActions _actions;
    private PlanIndex? _index;

    public GroupCardVm(GroupId id, IReviewActions actions)
    {
        Id = id;
        _actions = actions;
        CommitDescriptionCommand = new AsyncRelayCommand(() => _actions.RenameAsync(this, Description), () => !IsDescriptionReadOnly);
        NewFolderInsteadCommand = new AsyncRelayCommand(NewFolderInsteadAsync);
    }

    public GroupId Id { get; }
    public ItemId Anchor => Id.Anchor;
    public override string Key => "g:" + Id.Anchor.CardRelPath;
    public VideoGroup? Group { get; private set; }

    [ObservableProperty] public partial BoundaryChipVm? Chip { get; private set; }
    [ObservableProperty] public partial string Swatch { get; private set; } = MapProjection.ImportedGrey;
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty] public partial bool DescriptionIsSuggestion { get; private set; }
    [ObservableProperty] public partial bool IsDescriptionReadOnly { get; private set; }
    [ObservableProperty] public partial string? ReadOnlyHint { get; private set; }
    [ObservableProperty] public partial string Badge { get; private set; } = "";
    [ObservableProperty] public partial string? ConfidenceText { get; private set; }
    [ObservableProperty] public partial string TargetPath { get; private set; } = "";
    [ObservableProperty] public partial string DateRangeText { get; private set; } = "";
    [ObservableProperty] public partial IReadOnlyList<string> ZoneBadges { get; private set; } = [];
    [ObservableProperty] public partial string LocationText { get; private set; } = "";
    [ObservableProperty] public partial string VideoCountsText { get; private set; } = "";
    [ObservableProperty] public partial string PhotoCountsText { get; private set; } = "";
    [ObservableProperty] public partial IReadOnlyList<ThumbVm> Thumbs { get; private set; } = [];
    [ObservableProperty] public partial string? MoreThumbsText { get; private set; }
    [ObservableProperty] public partial bool HasClockMismatch { get; private set; }

    public ObservableCollection<SuggestionVm> Suggestions { get; } = [];
    public ObservableCollection<ChipVm> Chips { get; } = [];
    public IAsyncRelayCommand CommitDescriptionCommand { get; }
    public IAsyncRelayCommand NewFolderInsteadCommand { get; }

    /// <summary>Built when the target DropDownButton's flyout opens (Ref §9.4 item 4).</summary>
    public IReadOnlyList<RetargetOptionVm> RetargetOptions()
    {
        var list = new List<RetargetOptionVm>
        {
            new(RetargetKind.Auto, "Auto (proposed)", null, null, null),
            new(RetargetKind.NewFolder, "New folder", null, null, null),
        };
        if (Group is { } g && _index is { } ix) list.AddRange(ix.Candidates(g));
        list.Add(new(RetargetKind.Browse, "Browse existing…", null, null, null));
        list.Add(new(RetargetKind.Skip, "Skip this group", null, null, null));
        return list;
    }

    internal void Update(VideoGroup g, PlanIndex ix)
    {
        Group = g;
        _index = ix;
        var members = g.Videos.Select(v => ix.Items[v]).OrderBy(i => i.Time.CaptureUtc).ToList();

        Chip = ix.Before(g.Id) is { } b ? (Chip?.Model == b ? Chip : new BoundaryChipVm(b, _actions)) : null;
        Swatch = MapProjection.Color(g);
        (Badge, ConfidenceText, TargetPath) = g.Target switch
        {
            NewFolder n => ("NEW FOLDER", (string?)null, n.RelPath),
            Append a => ("APPEND", a.Confidence == Confidence.Medium ? $"Medium · {a.Why}" : "High", Relative(a.Folder.FullPath, ix.VideoRoot)),
            AlreadyImported ai => ("ALREADY IN LIBRARY", null, Relative(ai.Folder.FullPath, ix.VideoRoot)),
            NothingToCopy x => ("NOTHING TO COPY", null, x.Summary),
            SkipGroup => ("SKIP", null, "Not copied"),
        };
        IsDescriptionReadOnly = !g.DescriptionEditable || g.Target is Append or AlreadyImported;
        ReadOnlyHint = g.Target is Append ? "Appending to an existing folder · [New folder instead]" : null;
        if (!string.Equals(Description, g.Description, StringComparison.Ordinal)) Description = g.Description;
        DescriptionIsSuggestion = g.DescSource is DescSource.Ledger or DescSource.Feature or DescSource.Place or DescSource.Town;
        CommitDescriptionCommand.NotifyCanExecuteChanged();

        Suggestions.Clear();
        foreach (var s in g.Suggestions) Suggestions.Add(new SuggestionVm(s));

        var abbrs = members.Select(i => ZoneNames.Abbreviation(i.Time.TzId, i.Time.CaptureUtc)).Distinct(StringComparer.Ordinal).ToList();
        ZoneBadges = abbrs;
        var zone = abbrs.Count == 1 ? " " + abbrs[0] : "";
        DateRangeText = g.Start == g.End && members.Count > 0
            ? $"{Fmt.Day(g.Start)} {Fmt.Clock(members[0].Time.LocalTime)}–{Fmt.Clock(members[^1].Time.LocalTime)}{zone}"
            : Fmt.DateRange(g.Start, g.End) + (zone.Length > 0 ? " ·" + zone : "");

        var place = g.Suggestions.FirstOrDefault(s => s.Source is DescSource.Feature or DescSource.Place or DescSource.Town);
        var placeText = place is null ? "" : (place.Text.StartsWith("near ", StringComparison.Ordinal) ? place.Text : "near " + place.Text) + " · ";
        LocationText = g.Centroid is null ? "no GPS" : $"{placeText}spread {Fmt.Miles(g.Spread)}";

        var included = members.Where(i => ix.Plan.Included.Contains(i.Raw.Unit.Id)).ToList();
        VideoCountsText = string.Create(CultureInfo.InvariantCulture, $"Videos {included.Count} new / {members.Count}")
                          + (included.Count > 0 ? " · " + Fmt.Size(included.Sum(i => i.Raw.Bytes)) : "");
        PhotoCountsText = ix.PhotoCounts(g);
        Thumbs = [.. g.Videos.Take(8).Select(v => new ThumbVm(v))];
        MoreThumbsText = g.Videos.Length > 8 ? string.Create(CultureInfo.InvariantCulture, $"+{g.Videos.Length - 8}") : null;

        HasClockMismatch = members.Exists(i => i.Flags.HasFlag(ItemFlags.ClockMismatch));
        Chips.Clear();
        foreach (var chip in BuildChips(g, members, ix)) Chips.Add(chip);
    }

    private List<ChipVm> BuildChips(VideoGroup g, List<Item> members, PlanIndex ix)
    {
        var chips = new List<ChipVm>();
        var unfinished = members.Count(i => i.Flags.HasFlag(ItemFlags.Truncated));
        if (unfinished > 0) chips.Add(new(ChipKind.Unfinished, Fmt.Count(unfinished, "unfinished recording", "unfinished recordings"), null, []));
        var conflicts = members.Count(i => i.Newness is Conflict);
        if (conflicts > 0) chips.Add(new(ChipKind.Conflicts, Fmt.Count(conflicts, "conflict", "conflicts"), null, []));
        if (members.Exists(i => i.Flags.HasFlag(ItemFlags.CheckDate))) chips.Add(new(ChipKind.CheckDate, "Check date", null, []));
        if (members.Exists(i => i.Flags.HasFlag(ItemFlags.GpsGuessed))) chips.Add(new(ChipKind.GpsGuessed, "GPS guessed", null, []));
        if (members.Find(i => i.Flags.HasFlag(ItemFlags.ClockMismatch)) is { } mm)
            chips.Add(new(ChipKind.ClockMismatch, "clock ≠ local", ClockText.ChipTooltip(mm), []));
        if (g.Target is NewFolder && g.Description.Length == 0) chips.Add(new(ChipKind.EmptyName, "Name this folder", null, []));
        if (g.Target is Append { Confidence: Confidence.Medium, Hint: { } hint })
            chips.Add(new(ChipKind.CrossDayAppend, hint.Text, null, [new QuickFixVm(new QuickFix("New folder instead", hint.Fix), _actions)]));
        if (g.DaySplits.FirstOrDefault(s => s.Emphasised) is { } split)
        {
            var days = g.End.DayNumber - g.Start.DayNumber + 1;
            var apart = split.Apart is { } d ? Fmt.Miles(d) + " apart" : "far apart";
            chips.Add(new(ChipKind.EmphasisedSplit, $"{Fmt.Count(days, "day", "days")} · {apart}", null,
                          [new QuickFixVm(new QuickFix("Split", [new SplitBefore(split.FirstOfDay)]), _actions)]));
        }
        foreach (var issue in ix.Plan.Issues.Where(i => i.Anchor == g.Id.Anchor))
        {
            var kind = issue.Code switch
            {
                IssueCode.PinMembershipChanged => ChipKind.PinChanged,
                IssueCode.ConflictingPins => ChipKind.ConflictingPins,
                _ => (ChipKind?)null,
            };
            if (kind is { } k) chips.Add(new(k, issue.Message, null, [.. issue.QuickFixes.Select(f => new QuickFixVm(f, _actions))]));
        }
        return chips;
    }

    private Task NewFolderInsteadAsync()
        => Group?.Target is Append { Hint: { } hint }
            ? _actions.ApplyQuickFixAsync(new QuickFix("New folder instead", hint.Fix))
            : _actions.RetargetAsync(this, new RetargetOptionVm(RetargetKind.NewFolder, "New folder", null, null, null));

    private static string Relative(string full, string root)
        => full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ? full[(root.TrimEnd('\\').Length + 1)..] : full;

    public override string ToString() => Description.Length > 0 ? Description : TargetPath;
}
```

- [ ] **Step 7: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*GroupCard*"`, then `-- --filter-method "*ClipRow*"` and `-- --filter-method "*FoldedRun*"`
Expected: PASS (13 tests).

- [ ] **Step 8: Commit**

```bash
git add src/UasSort.Review/Review tests/UasSort.Review.Tests/GroupCardVmTests.cs tests/UasSort.Review.Tests/Fakes/RecordingActions.cs
git commit -m "feat: group card, boundary chip, clip row and banner view models" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.9: Videos tab (timeline with folds, clip list, selection keeping, map/grid selection sync)

**Files:**
- Create: `src/UasSort.Review/Review/VideosTabVm.cs`
- Test: `tests/UasSort.Review.Tests/VideosTabVmTests.cs`

**Interfaces:**
- Consumes: `PlanIndex`, `GroupCardVm`, `FoldedRunVm`, `ClipRowVm`, `IReviewActions`, `CollectionSync`, `MapClick`, `MapClickEmpty`.
- Produces (defined here): `public sealed partial class VideosTabVm : ObservableObject` —
  - `VideosTabVm(IReviewActions actions)`
  - `ObservableCollection<TimelineEntryVm> Timeline`, `ObservableCollection<ClipRowVm> Clips`
  - `TimelineEntryVm? SelectedEntry { get; set; }` (observable), `GroupCardVm? SelectedCard`, `IReadOnlyList<ItemId> SelectedClipIds`, `string Header` (observable)
  - `void SetSelectedClips(IReadOnlyList<ItemId> ids)`, `void ToggleFold(FoldedRunVm run)`, `void OnMapClick(MapClick c)`, `void OnMapClickEmpty()`, `GroupCardVm? NextCard(GroupCardVm card)`, `GroupCardVm? CardFor(ItemId anchor)`
  - `event Action<GroupId, IReadOnlyList<ItemId>, bool>? MapSelectRequested` (group, highlighted items, fit), `event Action<IReadOnlyList<ItemId>>? ClipSelectionChanged`
  - `internal void Update(PlanIndex ix)`, `internal void Reveal(ItemId clip)` (used by `ReviewVm.GoTo`, Task 10.13)

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/VideosTabVmTests.cs
namespace UasSort.Review.Tests;

public class VideosTabVmTests
{
    private static readonly LibraryFolderRef Council = new(TestPlans.VideoRoot + @"\2026\2026-07\2026-07-25 Council Road", new(2026, 7, 25), "Council Road");
    private static readonly LibraryFolderRef Anvil = new(TestPlans.VideoRoot + @"\2026\2026-07\2026-07-26 Anvil Mountain", new(2026, 7, 26), "Anvil Mountain");

    private static Plan Derive(IReadOnlyList<PlanClip> clips, double r = 50, IReadOnlyList<PlanEdit>? edits = null, int rev = 1)
        => new ScriptedDeriver().Derive(TestPlans.Base(clips), new Tuning(r, 1), edits ?? [], new SessionFlags(false), rev, TestContext.Current.CancellationToken);

    private static IReadOnlyList<PlanClip> ImportedCouncilAnvilPlusZachar()
    {
        var ca = TestPlans.CouncilAnvil();
        return
        [
            ca[0] with { Newness = new Imported(Evidence.LibraryNameSize, Council, "same name and size") },
            ca[1] with { Newness = new Imported(Evidence.LibraryNameSize, Council, "same name and size") },
            ca[2] with { Newness = new Imported(Evidence.LibraryNameSize, Anvil, "same name and size") },
            ca[3] with { Newness = new Imported(Evidence.LibraryNameSize, Anvil, "same name and size") },
            .. TestPlans.Zachar(),
        ];
    }

    [Fact]
    public void VideosTab_ImportedRunsFoldIntoOneRow_AndExpand()
    {
        var tab = new VideosTabVm(new RecordingActions());
        tab.Update(new PlanIndex(Derive(ImportedCouncilAnvilPlusZachar())));

        Assert.Equal(2, tab.Timeline.Count);
        var fold = Assert.IsType<FoldedRunVm>(tab.Timeline[0]);
        Assert.Equal("2 groups already in library", fold.Text);
        Assert.IsType<GroupCardVm>(tab.Timeline[1]);
        Assert.Equal("Videos · 1 to offload", tab.Header);

        tab.ToggleFold(fold);
        Assert.Equal(3, tab.Timeline.Count);
        Assert.All(tab.Timeline, e => Assert.IsType<GroupCardVm>(e));
    }

    [Fact]
    public void VideosTab_SelectingAFold_ShowsItsClipsReadOnly()
    {
        var tab = new VideosTabVm(new RecordingActions());
        tab.Update(new PlanIndex(Derive(ImportedCouncilAnvilPlusZachar())));
        tab.SelectedEntry = tab.Timeline[0];

        Assert.Equal(4, tab.Clips.Count);
        Assert.All(tab.Clips, c => Assert.True(c.IsReadOnly));
    }

    [Fact]
    public void VideosTab_SelectionSurvivesRederive_SameInstanceOrContainingGroup()
    {
        var tab = new VideosTabVm(new RecordingActions());
        tab.Update(new PlanIndex(Derive(TestPlans.CouncilAnvil(), r: 25, rev: 1)));
        var anvilCard = (GroupCardVm)tab.Timeline[1];
        tab.SelectedEntry = anvilCard;

        tab.Update(new PlanIndex(Derive(TestPlans.CouncilAnvil(), r: 20, rev: 2)));
        Assert.Same(anvilCard, tab.SelectedEntry);

        tab.Update(new PlanIndex(Derive(TestPlans.CouncilAnvil(), r: 50, rev: 3)));
        var merged = Assert.IsType<GroupCardVm>(Assert.Single(tab.Timeline));
        Assert.Same(merged, tab.SelectedEntry);
        Assert.Equal(4, tab.Clips.Count);
    }

    [Fact]
    public void VideosTab_MapClick_SelectsGroupAndClip_CtrlAdds_GridSelectionHighlights()
    {
        var tab = new VideosTabVm(new RecordingActions());
        var plan = Derive(TestPlans.CouncilAnvil(), r: 25);
        tab.Update(new PlanIndex(plan));
        var selects = new List<(GroupId G, IReadOnlyList<ItemId> Ids, bool Fit)>();
        var clipEvents = new List<IReadOnlyList<ItemId>>();
        tab.MapSelectRequested += (g, ids, fit) => selects.Add((g, ids, fit));
        tab.ClipSelectionChanged += clipEvents.Add;
        var a1 = TestPlans.Id(TestPlans.CouncilAnvil()[2].Name);
        var a2 = TestPlans.Id(TestPlans.CouncilAnvil()[3].Name);
        var anvilGroup = plan.Groups[1].Id.Anchor.CardRelPath;

        tab.OnMapClick(new MapClick([a1.CardRelPath], anvilGroup, false, false));
        Assert.Same(tab.Timeline[1], tab.SelectedEntry);
        Assert.Equal<ItemId>([a1], tab.SelectedClipIds);
        Assert.True(selects[0].Fit);

        tab.OnMapClick(new MapClick([a2.CardRelPath], anvilGroup, true, false));
        Assert.Equal<ItemId>([a1, a2], tab.SelectedClipIds);
        Assert.Equal(2, clipEvents[^1].Count);

        tab.SetSelectedClips([a2]);
        var last = selects[^1];
        Assert.False(last.Fit);
        Assert.Equal<ItemId>([a2], last.Ids);

        tab.OnMapClickEmpty();
        Assert.Empty(tab.SelectedClipIds);
    }

    [Fact]
    public void VideosTab_NextCard_SkipsFolds()
    {
        var tab = new VideosTabVm(new RecordingActions());
        tab.Update(new PlanIndex(Derive(TestPlans.CouncilAnvil(), r: 25)));
        var first = (GroupCardVm)tab.Timeline[0];
        Assert.Same(tab.Timeline[1], tab.NextCard(first));
        Assert.Null(tab.NextCard((GroupCardVm)tab.Timeline[1]));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*VideosTab*"`
Expected: build FAILS (CS0246 `VideosTabVm`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Review/VideosTabVm.cs
namespace UasSort.Review;

/// <summary>The Videos tab: timeline of group cards and folded runs, and the clip list of the selection (Ref §9.3, §9.5, §9.6 Sync).</summary>
public sealed partial class VideosTabVm : ObservableObject
{
    private readonly IReviewActions _actions;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private PlanIndex? _index;
    private bool _updating;

    public VideosTabVm(IReviewActions actions) => _actions = actions;

    public ObservableCollection<TimelineEntryVm> Timeline { get; } = [];
    public ObservableCollection<ClipRowVm> Clips { get; } = [];
    public IReadOnlyList<ItemId> SelectedClipIds { get; private set; } = [];

    [ObservableProperty] public partial TimelineEntryVm? SelectedEntry { get; set; }
    [ObservableProperty] public partial string Header { get; private set; } = "Videos";

    public GroupCardVm? SelectedCard => SelectedEntry as GroupCardVm;

    public event Action<GroupId, IReadOnlyList<ItemId>, bool>? MapSelectRequested;
    public event Action<IReadOnlyList<ItemId>>? ClipSelectionChanged;

    private abstract record Entry(string Key);
    private sealed record CardEntry(string Key, VideoGroup Group) : Entry(Key);
    private sealed record FoldEntry(string Key, IReadOnlyList<VideoGroup> Run) : Entry(Key);

    internal void Update(PlanIndex ix)
    {
        _index = ix;
        var plan = ix.Plan;
        var previous = SelectedEntry;
        var previousAnchors = previous switch
        {
            GroupCardVm c => c.Group?.Videos.ToHashSet() ?? [],
            FoldedRunVm f => f.Groups.SelectMany(g => g.Videos).ToHashSet(),
            _ => [],
        };

        var entries = new List<Entry>();
        var run = new List<VideoGroup>();
        void FlushRun()
        {
            if (run.Count == 0) return;
            var key = "f:" + run[0].Id.Anchor.CardRelPath;
            if (_expanded.Contains(key)) entries.AddRange(run.Select(g => new CardEntry("g:" + g.Id.Anchor.CardRelPath, g)));
            else entries.Add(new FoldEntry(key, [.. run]));
            run.Clear();
        }
        foreach (var g in plan.Groups)
        {
            if (g.Foldable) { run.Add(g); continue; }
            FlushRun();
            entries.Add(new CardEntry("g:" + g.Id.Anchor.CardRelPath, g));
        }
        FlushRun();

        _updating = true;
        try
        {
            CollectionSync.Sync<TimelineEntryVm, Entry>(Timeline, entries, e => e.Key,
                e => e switch
                {
                    CardEntry c => new GroupCardVm(c.Group.Id, _actions),
                    FoldEntry f => new FoldedRunVm(f.Key),
                    _ => throw new InvalidOperationException("unknown timeline entry"),
                },
                (vm, e) =>
                {
                    if (vm is GroupCardVm card && e is CardEntry ce) card.Update(ce.Group, ix);
                    else if (vm is FoldedRunVm fold && e is FoldEntry fe) fold.Update(fe.Run);
                });

            if (previous is not null && !Timeline.Contains(previous))
                SelectedEntry = Timeline.FirstOrDefault(e => Contains(e, previousAnchors));
        }
        finally
        {
            _updating = false;
        }

        Header = "Videos · " + string.Create(CultureInfo.InvariantCulture,
            $"{plan.Groups.Count(g => g.Videos.Any(plan.Included.Contains))} to offload");
        RebuildClips();
    }

    public void ToggleFold(FoldedRunVm run)
    {
        if (!_expanded.Add(run.Key)) _expanded.Remove(run.Key);
        run.IsExpanded = _expanded.Contains(run.Key);
        if (_index is { } ix) Update(ix);
    }

    public GroupCardVm? NextCard(GroupCardVm card)
    {
        var i = Timeline.IndexOf(card);
        return i < 0 ? null : Timeline.Skip(i + 1).OfType<GroupCardVm>().FirstOrDefault();
    }

    public GroupCardVm? CardFor(ItemId anchor) => Timeline.OfType<GroupCardVm>().FirstOrDefault(c => c.Anchor == anchor);

    /// <summary>ReviewVm.GoTo: selects the card (or folded run) that holds this clip and selects the clip in the list and on the map.</summary>
    internal void Reveal(ItemId clip)
    {
        var entry = Timeline.FirstOrDefault(e => Contains(e, [clip]));
        if (entry is null) return;
        if (!ReferenceEquals(entry, SelectedEntry)) SelectedEntry = entry;
        SelectedClipIds = [clip];
        ClipSelectionChanged?.Invoke(SelectedClipIds);
        if (SelectedGroupId() is { } g) MapSelectRequested?.Invoke(g, SelectedClipIds, true);
    }

    public void SetSelectedClips(IReadOnlyList<ItemId> ids)
    {
        SelectedClipIds = ids;
        if (SelectedGroupId() is { } g) MapSelectRequested?.Invoke(g, ids, false);
    }

    public void OnMapClick(MapClick c)
    {
        var entry = Timeline.FirstOrDefault(e => e switch
        {
            GroupCardVm card => string.Equals(card.Anchor.CardRelPath, c.GroupId, StringComparison.Ordinal),
            FoldedRunVm fold => fold.Groups.Any(g => string.Equals(g.Id.Anchor.CardRelPath, c.GroupId, StringComparison.Ordinal)),
            _ => false,
        });
        if (entry is not null && !ReferenceEquals(entry, SelectedEntry)) SelectedEntry = entry;
        var clicked = c.ItemIds.Select(s => new ItemId(s)).ToList();
        SelectedClipIds = c.Ctrl ? [.. SelectedClipIds.Concat(clicked).Distinct()] : clicked;
        ClipSelectionChanged?.Invoke(SelectedClipIds);
        if (SelectedGroupId() is { } g) MapSelectRequested?.Invoke(g, SelectedClipIds, false);
    }

    public void OnMapClickEmpty()
    {
        SelectedClipIds = [];
        ClipSelectionChanged?.Invoke(SelectedClipIds);
    }

    partial void OnSelectedEntryChanged(TimelineEntryVm? value)
    {
        RebuildClips();
        if (_updating) return;
        SelectedClipIds = [];
        if (SelectedGroupId() is { } g) MapSelectRequested?.Invoke(g, [], true);
    }

    private GroupId? SelectedGroupId() => SelectedEntry switch
    {
        GroupCardVm c => c.Id,
        FoldedRunVm f when f.Groups.Count > 0 => f.Groups[0].Id,
        _ => null,
    };

    private void RebuildClips()
    {
        if (_index is not { } ix)
        {
            Clips.Clear();
            return;
        }
        IReadOnlyList<VideoGroup> groups = SelectedEntry switch
        {
            GroupCardVm { Group: { } g } => [g],
            FoldedRunVm f => f.Groups,
            _ => [],
        };
        var rows = groups.SelectMany(g => g.Videos.Select(v => (Group: g, Item: ix.Items[v])))
                         .OrderBy(x => x.Item.Time.CaptureUtc).ToList();
        CollectionSync.Sync(Clips, rows, r => r.Item.Raw.Unit.Id.CardRelPath,
            r => new ClipRowVm(r.Item.Raw.Unit.Id, _actions),
            (vm, r) => vm.Update(r.Item, ix.Plan.Included.Contains(r.Item.Raw.Unit.Id), r.Group,
                                 readOnly: SelectedEntry is FoldedRunVm || r.Group.Target is AlreadyImported));
        var present = Clips.Select(c => c.Id).ToHashSet();
        SelectedClipIds = [.. SelectedClipIds.Where(present.Contains)];
    }

    private static bool Contains(TimelineEntryVm e, HashSet<ItemId> anchors) => e switch
    {
        GroupCardVm c => c.Group is { } g && g.Videos.Any(anchors.Contains),
        FoldedRunVm f => f.Groups.Any(g => g.Videos.Any(anchors.Contains)),
        _ => false,
    };
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*VideosTab*"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Review/VideosTabVm.cs tests/UasSort.Review.Tests/VideosTabVmTests.cs
git commit -m "feat: videos tab timeline with folds, selection keeping and map sync" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.10: Issues flyout and footer totals

**Files:**
- Create: `src/UasSort.Review/Review/IssuesVm.cs`, `src/UasSort.Review/Review/Footer.cs`
- Test: `tests/UasSort.Review.Tests/IssuesVmTests.cs`

**Interfaces:**
- Consumes: `Issue`, `IssueSeverity`, `IssueCode`, `QuickFix`, `Plan`, `IFreeSpace`, `QuickFixVm`.
- Produces (defined here):
  - `public sealed class IssueVm` — `IssueSeverity Severity`, `IssueCode Code`, `string Message`, `ItemId? Anchor`, `string Glyph`, `IReadOnlyList<QuickFixVm> QuickFixes`, `ToString()` = message
  - `public sealed partial class IssuesVm : ObservableObject` — `IssuesVm(Func<Issue, QuickFix, Task> runFix)`, `ObservableCollection<IssueVm> Entries`, observable `int BlockingCount`, `int WarningCount`, `int InfoCount`, `string? BlockedTooltip`, `bool HasBlocking`, `internal void Update(ImmutableArray<Issue> issues)`
  - `public static class Footer` — `string Text(Plan plan, IFreeSpace space)`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/IssuesVmTests.cs
namespace UasSort.Review.Tests;

public class IssuesVmTests
{
    [Fact]
    public async Task IssuesVm_CountsOrdersAndRunsQuickFixes()
    {
        var ran = new List<string>();
        var vm = new IssuesVm((issue, fix) => { ran.Add($"{issue.Code}:{fix.Label}"); return Task.CompletedTask; });
        var anchor = new ItemId("DCIM/DJI_001/a.MP4");
        vm.Update(
        [
            new Issue(IssueSeverity.Info, IssueCode.SharedTarget, "Also targeted by Jul 25", anchor, [], false),
            new Issue(IssueSeverity.Blocking, IssueCode.EmptyFolderName, "Name this folder", anchor, [new QuickFix("Name it", [])], false),
            new Issue(IssueSeverity.Warning, IssueCode.MediumAppend, "Appending to 'Council Road': different day, 34 mi from Council Road", anchor,
                      [new QuickFix("New folder instead", [new SplitBefore(anchor)])], true),
        ]);

        Assert.Equal((1, 1, 1), (vm.BlockingCount, vm.WarningCount, vm.InfoCount));
        Assert.True(vm.HasBlocking);
        Assert.Equal<IssueCode>([IssueCode.EmptyFolderName, IssueCode.MediumAppend, IssueCode.SharedTarget], vm.Entries.Select(e => e.Code));
        Assert.Equal("⛔", vm.Entries[0].Glyph);
        Assert.Equal("Fix before offloading:\n• Name this folder", vm.BlockedTooltip);

        await vm.Entries[1].QuickFixes[0].Command.ExecuteAsync(null);
        Assert.Equal("MediumAppend:New folder instead", Assert.Single(ran));
    }

    [Fact]
    public void IssuesVm_NoBlocking_NoTooltip()
    {
        var vm = new IssuesVm((_, _) => Task.CompletedTask);
        vm.Update([]);
        Assert.False(vm.HasBlocking);
        Assert.Null(vm.BlockedTooltip);
    }

    [Fact]
    public void Footer_TotalsPerDriveWithFreeSpace()
    {
        var plan = new ScriptedDeriver().Derive(TestPlans.Base(TestPlans.Zachar()), new Tuning(), [], new SessionFlags(false), 1,
                                                TestContext.Current.CancellationToken);
        Assert.Equal("3 videos · 3.6 GB → C: (317 GB free)", Footer.Text(plan, new FakeFreeSpace()));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*IssuesVm*"`
Expected: build FAILS (CS0246 `IssuesVm`; CS0103 `Footer`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Review/IssuesVm.cs
namespace UasSort.Review;

public sealed class IssueVm
{
    public IssueVm(Issue issue, Func<Issue, QuickFix, Task> runFix)
    {
        Model = issue;
        QuickFixes = [.. issue.QuickFixes.Select(f => new QuickFixVm(f.Label, () => runFix(issue, f)))];
    }

    public Issue Model { get; }
    public IssueSeverity Severity => Model.Severity;
    public IssueCode Code => Model.Code;
    public string Message => Model.Message;
    public ItemId? Anchor => Model.Anchor;
    public string Glyph => Model.Severity switch { IssueSeverity.Blocking => "⛔", IssueSeverity.Warning => "⚠", _ => "ⓘ" };
    public IReadOnlyList<QuickFixVm> QuickFixes { get; }
    public override string ToString() => Message;
}

/// <summary>The ⛔/⚠/ⓘ counters and their flyout (Ref §9.10).</summary>
public sealed partial class IssuesVm(Func<Issue, QuickFix, Task> runFix) : ObservableObject
{
    public ObservableCollection<IssueVm> Entries { get; } = [];

    [ObservableProperty] public partial int BlockingCount { get; private set; }
    [ObservableProperty] public partial int WarningCount { get; private set; }
    [ObservableProperty] public partial int InfoCount { get; private set; }
    [ObservableProperty] public partial string? BlockedTooltip { get; private set; }

    public bool HasBlocking => BlockingCount > 0;

    internal void Update(ImmutableArray<Issue> issues)
    {
        Entries.Clear();
        foreach (var i in issues.OrderBy(i => i.Severity)) Entries.Add(new IssueVm(i, runFix));
        BlockingCount = issues.Count(i => i.Severity == IssueSeverity.Blocking);
        WarningCount = issues.Count(i => i.Severity == IssueSeverity.Warning);
        InfoCount = issues.Count(i => i.Severity == IssueSeverity.Info);
        var blocking = issues.Where(i => i.Severity == IssueSeverity.Blocking).Select(i => "• " + i.Message).ToList();
        BlockedTooltip = blocking.Count == 0 ? null : "Fix before offloading:\n" + string.Join("\n", blocking);
        OnPropertyChanged(nameof(HasBlocking));
    }
}
```

```csharp
// src/UasSort.Review/Review/Footer.cs
namespace UasSort.Review;

/// <summary>Footer totals per destination drive against free space (Ref §9.10).</summary>
public static class Footer
{
    public static string Text(Plan plan, IFreeSpace space)
    {
        var settings = plan.Base.Scan.Settings;
        var included = plan.Base.Items.Where(i => plan.Included.Contains(i.Raw.Unit.Id)).ToList();
        var videos = included.Where(i => i.Raw.Kind == ItemKind.Video).ToList();
        var photos = included.Where(i => i.Raw.Kind != ItemKind.Video).ToList();
        var shownDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();

        string Segment(int n, string one, string many, long bytes, string root)
        {
            var drive = Fmt.Drive(root);
            var free = shownDrives.Add(drive) && space.FreeBytes(root) is { } f ? $" ({Fmt.Size(f)} free)" : "";
            return $"{Fmt.Count(n, one, many)} · {Fmt.Size(bytes)} → {drive}{free}";
        }

        if (videos.Count > 0) parts.Add(Segment(videos.Count, "video", "videos", videos.Sum(i => i.Raw.Bytes), settings.VideoRoot));
        if (photos.Count > 0) parts.Add(Segment(photos.Count, "photo", "photos", photos.Sum(i => i.Raw.Bytes), settings.PhotoRoot));
        return parts.Count == 0 ? "Nothing to copy" : string.Join(" · ", parts);
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*IssuesVm*"` and `-- --filter-method "*Footer*"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Review/IssuesVm.cs src/UasSort.Review/Review/Footer.cs tests/UasSort.Review.Tests/IssuesVmTests.cs
git commit -m "feat: issues flyout counters and footer totals" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 10.11: Tuning sliders (live preview, one undo entry per drag, 400 ms idle commit)

**Files:**
- Create: `src/UasSort.Review/Review/TuningVm.cs`
- Test: `tests/UasSort.Review.Tests/TuningVmTests.cs`

**Interfaces:**
- Consumes: `Tuning`, `IUiDispatcher`, `TimeProvider`, `Fmt`.
- Produces (defined here):
  - `public interface ITuningHost { void Preview(Tuning t); Task CommitTuningAsync(Tuning t); }` (ReviewVm implements it with `PlanSession.Preview` / `PlanSession.CommitTuningAsync`)
  - `public sealed partial class TuningVm : ObservableObject, IDisposable` — `TuningVm(ITuningHost host, TimeProvider time, IUiDispatcher ui)`, observable `double RadiusMiles`, `int GapDays`, `string GroupCountText`, `string RadiusText`, `string GapText`; `bool IsDragging`; `Tuning Current`; `Tuning Committed`; `void BeginDrag()`; `Task EndDragAsync()`; `IAsyncRelayCommand ResetCommand`; `static readonly TimeSpan IdleCommit = 400 ms`; `internal void Sync(Tuning shown, int groupCount)`; `internal void SetCommitted(Tuning t)`

The pointer path (Part 11): `PointerPressed` → `BeginDrag`; `PointerReleased` (handledEventsToo) and `PointerCaptureLost` → `EndDragAsync`. Keyboard and wheel have no release, so a value change outside a drag commits after 400 ms without another change (Ref §9.7).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/TuningVmTests.cs
namespace UasSort.Review.Tests;

public class TuningVmTests
{
    private sealed class Host : ITuningHost
    {
        public List<Tuning> Previews { get; } = [];
        public List<Tuning> Commits { get; } = [];
        public void Preview(Tuning t) => Previews.Add(t);
        public Task CommitTuningAsync(Tuning t) { Commits.Add(t); return Task.CompletedTask; }
    }

    private static (TuningVm Vm, Host Host, FakeTimeProvider Time, FakeUiDispatcher Ui) Make()
    {
        var host = new Host();
        var time = new FakeTimeProvider();
        var ui = new FakeUiDispatcher();
        return (new TuningVm(host, time, ui), host, time, ui);
    }

    [Fact]
    public async Task Tuning_Drag_PreviewsLiveAndCommitsOnceOnRelease()
    {
        var (vm, host, time, ui) = Make();
        vm.BeginDrag();
        vm.RadiusMiles = 40;
        vm.RadiusMiles = 35;
        vm.RadiusMiles = 30;
        time.Advance(TimeSpan.FromSeconds(2));
        ui.RunAll();

        Assert.Equal(3, host.Previews.Count);
        Assert.Empty(host.Commits);

        await vm.EndDragAsync();
        Assert.Equal(new Tuning(30, 1), Assert.Single(host.Commits));
        Assert.Equal(new Tuning(30, 1), vm.Committed);
    }

    [Fact]
    public void Tuning_KeyboardChange_CommitsAfter400MsIdle()
    {
        var (vm, host, time, ui) = Make();
        vm.RadiusMiles = 45;
        time.Advance(TimeSpan.FromMilliseconds(300));
        vm.RadiusMiles = 44;
        time.Advance(TimeSpan.FromMilliseconds(399));
        ui.RunAll();
        Assert.Empty(host.Commits);

        time.Advance(TimeSpan.FromMilliseconds(1));
        ui.RunAll();
        Assert.Equal(new Tuning(44, 1), Assert.Single(host.Commits));
        Assert.Equal(2, host.Previews.Count);
    }

    [Fact]
    public void Tuning_GapSliderUsesTheSameIdleCommit()
    {
        var (vm, host, time, ui) = Make();
        vm.GapDays = 3;
        time.Advance(TuningVm.IdleCommit);
        ui.RunAll();
        Assert.Equal(new Tuning(50, 3), Assert.Single(host.Commits));
        Assert.Equal("3 days", vm.GapText);
    }

    [Fact]
    public async Task Tuning_Reset_CommitsDefaults()
    {
        var (vm, host, time, ui) = Make();
        vm.RadiusMiles = 30;
        time.Advance(TuningVm.IdleCommit);
        ui.RunAll();
        await vm.ResetCommand.ExecuteAsync(null);

        Assert.Equal(new Tuning(50, 1), host.Commits[^1]);
        Assert.Equal("50 mi", vm.RadiusText);
    }

    [Fact]
    public void Tuning_SyncFromPlan_DoesNotPreviewOrCommit()
    {
        var (vm, host, _, _) = Make();
        vm.Sync(new Tuning(25, 2), 8);
        Assert.Empty(host.Previews);
        Assert.Equal(25, vm.RadiusMiles);
        Assert.Equal("8 groups", vm.GroupCountText);
    }

    [Fact]
    public void Tuning_ValuesAreClampedAndStepped()
    {
        var (vm, _, _, _) = Make();
        vm.RadiusMiles = 3.4;
        Assert.Equal(5, vm.RadiusMiles);
        vm.RadiusMiles = 33.6;
        Assert.Equal(34, vm.RadiusMiles);
        vm.GapDays = 9;
        Assert.Equal(7, vm.GapDays);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Tuning_*"`
Expected: build FAILS (CS0246 `ITuningHost`, `TuningVm`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Review/TuningVm.cs
namespace UasSort.Review;

/// <summary>Where TuningVm sends previews and commits (ReviewVm, over PlanSession).</summary>
public interface ITuningHost
{
    void Preview(Tuning t);
    Task CommitTuningAsync(Tuning t);
}

/// <summary>The R and G sliders under the map (Ref §9.7).</summary>
public sealed partial class TuningVm : ObservableObject, IDisposable
{
    public static readonly TimeSpan IdleCommit = TimeSpan.FromMilliseconds(400);
    public const double DefaultRadiusMiles = 50;
    public const int DefaultGapDays = 1;

    private readonly ITuningHost _host;
    private readonly TimeProvider _time;
    private readonly IUiDispatcher _ui;
    private ITimer? _idle;
    private bool _syncing;

    public TuningVm(ITuningHost host, TimeProvider time, IUiDispatcher ui)
    {
        _host = host;
        _time = time;
        _ui = ui;
        _syncing = true;
        RadiusMiles = DefaultRadiusMiles;
        GapDays = DefaultGapDays;
        _syncing = false;
        Committed = Current;
        ResetCommand = new AsyncRelayCommand(ResetAsync);
    }

    [ObservableProperty] public partial double RadiusMiles { get; set; }
    [ObservableProperty] public partial int GapDays { get; set; }
    [ObservableProperty] public partial string GroupCountText { get; private set; } = "";

    public string RadiusText => Fmt.Miles(Distance.FromMiles(RadiusMiles));
    public string GapText => Fmt.Count(GapDays, "day", "days");
    public string ResetText => "Reset to 50 mi · 1 day";
    public bool IsDragging { get; private set; }
    public Tuning Current => new(RadiusMiles, GapDays);
    public Tuning Committed { get; private set; }
    public IAsyncRelayCommand ResetCommand { get; }

    partial void OnRadiusMilesChanged(double value)
    {
        var stepped = Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 5, 100);
        if (stepped != value) { RadiusMiles = stepped; return; }
        OnPropertyChanged(nameof(RadiusText));
        ValueChanged();
    }

    partial void OnGapDaysChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 7);
        if (clamped != value) { GapDays = clamped; return; }
        OnPropertyChanged(nameof(GapText));
        ValueChanged();
    }

    public void BeginDrag()
    {
        IsDragging = true;
        StopIdle();
    }

    public Task EndDragAsync()
    {
        IsDragging = false;
        StopIdle();
        return CommitAsync();
    }

    /// <summary>Shows a plan's tuning without previewing; skipped while the user is moving a slider.</summary>
    internal void Sync(Tuning shown, int groupCount)
    {
        GroupCountText = Fmt.Count(groupCount, "group", "groups");
        if (IsDragging || _idle is not null) return;
        _syncing = true;
        try
        {
            RadiusMiles = shown.RadiusMiles;
            GapDays = shown.GapDays;
        }
        finally
        {
            _syncing = false;
        }
    }

    internal void SetCommitted(Tuning t) => Committed = t;

    public void Dispose() => StopIdle();

    private void ValueChanged()
    {
        if (_syncing) return;
        _host.Preview(Current);
        if (IsDragging) return;
        StopIdle();
        _idle = _time.CreateTimer(_ => _ui.Post(() => _ = OnIdleAsync()), null, IdleCommit, Timeout.InfiniteTimeSpan);
    }

    private Task OnIdleAsync()
    {
        StopIdle();
        return CommitAsync();
    }

    private async Task CommitAsync()
    {
        var t = Current;
        if (t == Committed) return;
        Committed = t;
        await _host.CommitTuningAsync(t).ConfigureAwait(true);
    }

    private async Task ResetAsync()
    {
        StopIdle();
        _syncing = true;
        try
        {
            RadiusMiles = DefaultRadiusMiles;
            GapDays = DefaultGapDays;
        }
        finally
        {
            _syncing = false;
        }
        _host.Preview(Current);
        await CommitAsync().ConfigureAwait(true);
    }

    private void StopIdle()
    {
        _idle?.Dispose();
        _idle = null;
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Tuning_*"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Review/TuningVm.cs tests/UasSort.Review.Tests/TuningVmTests.cs
git commit -m "feat: tuning sliders with live preview and 400 ms idle commit" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.12: Decisions service, Photos tab (per-day tri-state), Other tab

**Files:**
- Create: `src/UasSort.Review/Services/Decisions.cs`, `src/UasSort.Review/Review/PhotosTabVm.cs`, `src/UasSort.Review/Review/OtherTabVm.cs`
- Test: `tests/UasSort.Review.Tests/PhotosOtherTabTests.cs`

**Interfaces:**
- Consumes: `ILedgerStore` (`OpenOwn`, `Load`), `ILedgerWriter.Append`, `DecisionRecord`, `RevokeRecord`, `LedgerSnapshot.Decisions`, `LedgerDecision`, `FileKey.OfPath(string pathOrName, long size)` (Part 02; the Ref §7.1 key of the last path segment), `VerdictDecisions.Revokes(IEnumerable<string>, string machine, TimeProvider)` (Part 07), `DecisionKind`, `PhotoDay`, `SetPlacement`/`SetResolution`, `SetUnit`/`SetKind`, `PhotoUnit`, `CardEntry`/`EntryClass`, `ScanWarning`, `PlanIndex`; tests use the shared `UasSort.Testing.FakeLedgerStore` (Parts 06/07; `Writer.Records` holds what was appended) through `Fake.Ledger(...)`.
- Produces (defined here):
  - `public sealed record DecisionTarget(CardEntry File, DateTime? CaptureUtc, string? Set)`
  - `public static class DecisionTargets` — `ImmutableArray<DecisionTarget> For(Item item)`, `DecisionTarget ForEntry(CardEntry e)` (keys come from `FileKey.OfPath`; there is no Review key helper)
  - `public interface IDecisionService` — `ImmutableArray<string> Record(DecisionKind kind, IReadOnlyList<DecisionTarget> targets, string why)`, `void Revoke(IReadOnlyList<string> decisionIds)`, `ImmutableArray<string> DecisionIdsFor(IReadOnlyList<DecisionTarget> targets, LedgerSnapshot ledger)`
  - `public sealed class LedgerDecisionService(ILedgerStore store, TimeProvider time, string machine) : IDecisionService` (revokes built with `VerdictDecisions.Revokes`)
  - `public sealed partial class PhotoDayVm : ObservableObject, IKeyed` (`Date`, `DayText`, `CountsText`, `StatusText`, `Reason`, `bool? IsIncluded`, `bool IsAllImported`, `IAsyncRelayCommand ToggleCommand`)
  - `public sealed partial class PhotoTileVm : ObservableObject, IKeyed` (`ThumbKey`, `Text`, `StatusText`, `PairText`, `IsIncluded`, `CanUndo`, `ToggleCommand`, `UndoCommand`, `ToString()` = `Text`)
  - `public sealed partial class PhotosTabVm : ObservableObject` — `PhotosTabVm(Func<PlanEdit, Task> apply, Func<IReadOnlyList<Item>, Task> undoConfirmed)`, `ObservableCollection<PhotoDayVm> Days`, `PhotoDayVm? SelectedDay`, `ObservableCollection<PhotoTileVm> Tiles`, `bool ShowImportedDays`, `string Header`, `string ShowImportedText`, `internal void Update(PlanIndex ix)`
  - `public sealed class OtherRowVm` (`Text`, `Detail`, `IAsyncRelayCommand? UndismissCommand`), `public sealed class OtherSectionVm` (`Title`, `Note`, `Rows`, `IsCollapsed`)
  - `public sealed partial class OtherTabVm : ObservableObject` — `OtherTabVm(Func<IReadOnlyList<Item>, Task> undismiss)`, `ObservableCollection<OtherSectionVm> Sections`, `string Header`, `internal void Update(PlanIndex ix)`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/PhotosOtherTabTests.cs
namespace UasSort.Review.Tests;

public class PhotosOtherTabTests
{
    private static readonly DateOnly Jul25 = new(2026, 7, 25);

    internal static Item Photo(string name, DateTime utc, Newness newness, long bytes = 30_000_000)
    {
        var id = new ItemId("DCIM/DJI_001/" + name);
        var entry = new CardEntry(id.CardRelPath, bytes, utc, utc, utc, 0x20, EntryClass.Photo, null);
        var raw = new RawItem(new PhotoUnit(id, entry, null), ItemKind.Photo, name, bytes, utc, null, null, null, null);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(TestPlans.Anchorage));
        return new Item(raw, new ItemTime(utc, TimeSource.DroneClockZone, TestPlans.Anchorage, TzSource.Gps, DateOnly.FromDateTime(local), local),
                        null, null, ItemFlags.NoGps, newness);
    }

    private static (PlanIndex Ix, List<Item> Photos) PhotoPlan(IReadOnlyList<PlanEdit>? edits = null)
    {
        List<Item> photos =
        [
            Photo("DJI_20260725200000_0101_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 0), new IsNew(NewReason.DayHasNewVideos, null)),
            Photo("DJI_20260725200100_0102_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 1), new ProbablyImported("videos from this day are already in the library")),
            Photo("DJI_20260725200200_0103_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 2), new Decided(DecisionKind.AssumedImported, TestPlans.Utc(2026, 10, 4, 18, 0), "PC1")),
        ];
        var day = new PhotoDay(Jul25, TestPlans.Anchorage, [.. photos.Select(p => p.Raw.Unit.Id)], "probably imported: videos from this day already in library");
        var b = TestPlans.Base(TestPlans.CouncilAnvil(), extraItems: photos, photoDays: [day]);
        var plan = new ScriptedDeriver().Derive(b, new Tuning(), edits ?? [], new SessionFlags(false), 1, TestContext.Current.CancellationToken);
        return (new PlanIndex(plan), photos);
    }

    [Fact]
    public async Task PhotosTab_DayRowTriStateAndToggle()
    {
        var applied = new List<PlanEdit>();
        var tab = new PhotosTabVm(e => { applied.Add(e); return Task.CompletedTask; }, _ => Task.CompletedTask);
        tab.Update(PhotoPlan().Ix);

        var day = Assert.Single(tab.Days);
        Assert.Equal("Jul 25 (Sat) · AKDT", day.DayText);
        Assert.Equal("3 photos · 90 MB", day.CountsText);
        Assert.Equal("1 new · 1 probably imported · 1 confirmed by you", day.StatusText);
        Assert.Null(day.IsIncluded);
        Assert.Equal("Photos · 1 day", tab.Header);

        await day.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(new SetDayIncluded(Jul25, true), Assert.Single(applied));
    }

    [Fact]
    public void PhotosTab_AllIncludedDay_IsChecked()
    {
        var tab = new PhotosTabVm(_ => Task.CompletedTask, _ => Task.CompletedTask);
        tab.Update(PhotoPlan([new SetDayIncluded(Jul25, true)]).Ix);
        Assert.True(tab.Days[0].IsIncluded);
    }

    [Fact]
    public async Task PhotosTab_ConfirmedTileOffersUndo()
    {
        var undone = new List<Item>();
        var tab = new PhotosTabVm(_ => Task.CompletedTask, items => { undone.AddRange(items); return Task.CompletedTask; });
        var (ix, photos) = PhotoPlan();
        tab.Update(ix);
        tab.SelectedDay = tab.Days[0];

        Assert.Equal(3, tab.Tiles.Count);
        var confirmed = Assert.Single(tab.Tiles, t => t.CanUndo);
        Assert.Equal("Confirmed by you", confirmed.StatusText);
        await confirmed.UndoCommand.ExecuteAsync(null);
        Assert.Equal(photos[2], Assert.Single(undone));
    }

    [Fact]
    public async Task OtherTab_SectionsAndUndismiss()
    {
        var undismissed = new List<Item>();
        var tab = new OtherTabVm(items => { undismissed.AddRange(items); return Task.CompletedTask; });
        var t = TestPlans.Utc(2026, 7, 26, 3, 0);
        ImmutableArray<CardEntry> extra =
        [
            new("DCIM/DJI_A001/x.MP4", 5_000_000, t, t, t, 0x20, EntryClass.Unknown, null),
            new("DCIM/DJI_001/DJI_20260725192655_0117_D.LRF", 1_000, t, t, t, 0x20, EntryClass.Skip, "LRF"),
            new("DCIM/DJI_001/DJI_20260725194000_0118_D.LRF", 1_000, t, t, t, 0x20, EntryClass.Skip, "LRF"),
        ];
        var clips = TestPlans.CouncilAnvil().ToList();
        clips[3] = clips[3] with { Newness = new Decided(DecisionKind.Dismissed, TestPlans.Utc(2026, 10, 4, 18, 0), "PC1") };
        var b = TestPlans.Base(clips, extraEntries: extra,
                               warnings: [new ScanWarning("EnumerationError", "Can't list DCIM\\DJI_002", "DCIM/DJI_002", true)]);
        var plan = new ScriptedDeriver().Derive(b, new Tuning(), [], new SessionFlags(false), 1, TestContext.Current.CancellationToken);
        tab.Update(new PlanIndex(plan));

        Assert.Equal<string>(["Unknown files", "Skipped by rule", "Scan warnings", "Dismissed by you"], tab.Sections.Select(s => s.Title));
        Assert.Equal("Not copied by uas-sort; copy manually if needed", tab.Sections[0].Note);
        Assert.Equal("LRF · 2 files", Assert.Single(tab.Sections[1].Rows).Text);
        Assert.True(tab.Sections[1].IsCollapsed);
        Assert.Equal("Other · 5", tab.Header);

        await tab.Sections[3].Rows[0].UndismissCommand!.ExecuteAsync(null);
        Assert.Equal(TestPlans.Id(clips[3].Name), Assert.Single(undismissed).Raw.Unit.Id);
    }

    [Fact]
    public void Decisions_RecordAndRevokeWriteLedgerRecords()
    {
        var store = Fake.Ledger();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 18, 0, 0, TimeSpan.Zero));
        var svc = new LedgerDecisionService(store, time, "PC1");
        var photo = Photo("DJI_20260725200000_0101_D (2).DNG", TestPlans.Utc(2026, 7, 26, 4, 0), new ProbablyImported("x"));

        var ids = svc.Record(DecisionKind.AssumedImported, DecisionTargets.For(photo), "recorded on the Verdict page");
        svc.Revoke(ids);

        var records = store.Writer.Records;
        var d = Assert.IsType<DecisionRecord>(records[0]);
        Assert.Equal("assumedImported", d.Kind);
        Assert.Equal("DJI_20260725200000_0101_D (2).DNG", d.Name);
        Assert.Equal("PC1", d.Machine);
        Assert.Equal(time.GetUtcNow().UtcDateTime, d.At);
        var r = Assert.IsType<RevokeRecord>(records[1]);
        Assert.Equal(d.Id, r.Decision);
        Assert.Equal("PC1", r.Machine);
        Assert.Equal(new FileKey("dji_20260725200000_0101_d.dng", 30_000_000), FileKey.OfPath(d.Src, d.Size));

        var ledger = TestPlans.Ledger() with
        {
            Decisions = ImmutableDictionary<FileKey, LedgerDecision>.Empty.Add(FileKey.OfPath(d.Src, d.Size),
                new LedgerDecision(d.Id, FileKey.OfPath(d.Src, d.Size), DecisionKind.AssumedImported, d.At, "PC1", null, d.Why)),
        };
        Assert.Equal<string>([d.Id], svc.DecisionIdsFor(DecisionTargets.For(photo), ledger));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*PhotosTab*"`
Expected: build FAILS (CS0246 `PhotosTabVm`, `OtherTabVm`, `LedgerDecisionService`, …).

- [ ] **Step 3: Implement the decisions service**

```csharp
// src/UasSort.Review/Services/Decisions.cs
namespace UasSort.Review;

/// <summary>One card file a decision is recorded for; a set member carries its set name (Ref §7.3, §10.4).</summary>
public sealed record DecisionTarget(CardEntry File, DateTime? CaptureUtc, string? Set);

/// <summary>The card files a Review-side decision covers. Their ledger key is always Core's <see cref="FileKey.OfPath"/>.</summary>
public static class DecisionTargets
{
    /// <summary>The files a decision covers: a video's MP4, a photo's primary (its twin follows it), every set member.</summary>
    public static ImmutableArray<DecisionTarget> For(Item item) => item.Raw.Unit switch
    {
        VideoUnit v => [new DecisionTarget(v.Mp4, item.Time.CaptureUtc, null)],
        PhotoUnit p => [new DecisionTarget(p.Primary, item.Time.CaptureUtc, null)],
        SetUnit s => [.. s.Members.Select(m => new DecisionTarget(m, item.Time.CaptureUtc, s.SetName))],
    };

    public static DecisionTarget ForEntry(CardEntry e) => new(e, null, null);
}

/// <summary>Writes Verdict-page decisions and their revokes to this PC's own ledger file (Ref §10.4, §10.5).</summary>
public interface IDecisionService
{
    ImmutableArray<string> Record(DecisionKind kind, IReadOnlyList<DecisionTarget> targets, string why);
    void Revoke(IReadOnlyList<string> decisionIds);
    ImmutableArray<string> DecisionIdsFor(IReadOnlyList<DecisionTarget> targets, LedgerSnapshot ledger);
}

public sealed class LedgerDecisionService(ILedgerStore store, TimeProvider time, string machine) : IDecisionService
{
    public ImmutableArray<string> Record(DecisionKind kind, IReadOnlyList<DecisionTarget> targets, string why)
    {
        var at = time.GetUtcNow().UtcDateTime;
        var ids = ImmutableArray.CreateBuilder<string>();
        using var writer = store.OpenOwn();
        foreach (var t in targets)
        {
            var id = Guid.NewGuid().ToString("D");
            var name = t.File.RelPath[(t.File.RelPath.LastIndexOfAny(['/', '\\']) + 1)..];
            writer.Append(new DecisionRecord(1, id, machine, null, at, kind == DecisionKind.AssumedImported ? "assumedImported" : "dismissed",
                                             name, t.File.Size, t.File.RelPath, t.CaptureUtc, why, t.Set));
            ids.Add(id);
        }
        return ids.ToImmutable();
    }

    /// <summary>One revoke per decision, built by Part 07's VerdictDecisions.Revokes (the same records the Verdict page writes).</summary>
    public void Revoke(IReadOnlyList<string> decisionIds)
    {
        if (decisionIds.Count == 0) return;
        using var writer = store.OpenOwn();
        foreach (var r in VerdictDecisions.Revokes(decisionIds, machine, time)) writer.Append(r);
    }

    public ImmutableArray<string> DecisionIdsFor(IReadOnlyList<DecisionTarget> targets, LedgerSnapshot ledger)
    {
        var keys = targets.Select(t => FileKey.OfPath(t.File.RelPath, t.File.Size)).ToHashSet();
        return [.. ledger.Decisions.Values.Where(d => keys.Contains(d.Key)).Select(d => d.Id)];
    }
}
```

- [ ] **Step 4: Implement the Photos tab**

```csharp
// src/UasSort.Review/Review/PhotosTabVm.cs
namespace UasSort.Review;

/// <summary>One day row of the Photos tab with its tri-state include box (Ref §9.8).</summary>
public sealed partial class PhotoDayVm : ObservableObject, IKeyed
{
    private readonly Func<PlanEdit, Task> _apply;

    public PhotoDayVm(DateOnly date, Func<PlanEdit, Task> apply)
    {
        Date = date;
        _apply = apply;
        ToggleCommand = new AsyncRelayCommand(() => _apply(new SetDayIncluded(Date, IsIncluded != true)));
    }

    public DateOnly Date { get; }
    public string Key => Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public IReadOnlyList<Item> Items { get; private set; } = [];

    [ObservableProperty] public partial string DayText { get; private set; } = "";
    [ObservableProperty] public partial string CountsText { get; private set; } = "";
    [ObservableProperty] public partial string StatusText { get; private set; } = "";
    [ObservableProperty] public partial string Reason { get; private set; } = "";
    [ObservableProperty] public partial bool? IsIncluded { get; private set; }
    [ObservableProperty] public partial bool IsAllImported { get; private set; }

    public IAsyncRelayCommand ToggleCommand { get; }

    internal void Update(PhotoDay day, IReadOnlyList<Item> items, IReadOnlySet<ItemId> included)
    {
        Items = items;
        var utc = items.Count > 0 ? items[0].Time.CaptureUtc : day.Date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
        DayText = $"{Fmt.DayWithWeekday(day.Date)} · {ZoneNames.Abbreviation(day.TzId, utc)}";
        var parts = new List<string>();
        var photos = items.Count(i => i.Raw.Unit is PhotoUnit);
        if (photos > 0) parts.Add(Fmt.Count(photos, "photo", "photos"));
        foreach (var kind in new[] { SetKind.Panorama, SetKind.Hyperlapse })
        {
            var n = items.Count(i => i.Raw.Unit is SetUnit s && s.Kind == kind);
            if (n > 0) parts.Add(Fmt.Count(n, kind == SetKind.Panorama ? "pano set" : "hyperlapse set", kind == SetKind.Panorama ? "pano sets" : "hyperlapse sets"));
        }
        parts.Add(Fmt.Size(items.Sum(i => i.Raw.Bytes)));
        CountsText = string.Join(" · ", parts);
        var status = new List<string> { Fmt.Count(items.Count(i => i.Newness is IsNew), "new", "new") };
        var probably = items.Count(i => i.Newness is ProbablyImported);
        if (probably > 0) status.Add(Fmt.Count(probably, "probably imported", "probably imported"));
        var confirmed = items.Count(i => i.Newness is Decided { Kind: DecisionKind.AssumedImported });
        if (confirmed > 0) status.Add(Fmt.Count(confirmed, "confirmed by you", "confirmed by you"));
        StatusText = string.Join(" · ", status);
        Reason = day.Reason;
        var inc = items.Count(i => included.Contains(i.Raw.Unit.Id));
        IsIncluded = inc == 0 ? false : inc == items.Count ? true : null;
        IsAllImported = items.All(i => i.Newness is Imported or Decided);
    }

    public override string ToString() => DayText;
}

/// <summary>One tile of the photo wall; a set is one tile (Ref §9.8).</summary>
public sealed partial class PhotoTileVm : ObservableObject, IKeyed
{
    public PhotoTileVm(Item item, Func<PlanEdit, Task> apply, Func<IReadOnlyList<Item>, Task> undo)
    {
        Item = item;
        ToggleCommand = new AsyncRelayCommand(() => apply(new SetIncluded([Item.Raw.Unit.Id], !IsIncluded)));
        UndoCommand = new AsyncRelayCommand(() => undo([Item]), () => CanUndo);
    }

    public Item Item { get; private set; }
    public ItemId ThumbKey => Item.Raw.Unit.Id;
    public string Key => Item.Raw.Unit.Id.CardRelPath;

    [ObservableProperty] public partial string Text { get; private set; } = "";
    [ObservableProperty] public partial string StatusText { get; private set; } = "";
    [ObservableProperty] public partial string? PairText { get; private set; }
    [ObservableProperty] public partial bool IsIncluded { get; private set; }
    [ObservableProperty] public partial bool CanUndo { get; private set; }

    public IAsyncRelayCommand ToggleCommand { get; }
    public IAsyncRelayCommand UndoCommand { get; }

    internal void Update(Item item, bool included, SetPlacement? placement)
    {
        Item = item;
        Text = item.Raw.Unit is SetUnit s && placement is { } p ? SetText(s, p) : item.Raw.Name;
        StatusText = ClipRowVm.Status(item).Text;
        PairText = item.Raw.Unit is PhotoUnit { JpgTwin: not null } ? "DNG+JPG" : null;
        IsIncluded = included;
        CanUndo = item.Newness is Decided { Kind: DecisionKind.AssumedImported };
        UndoCommand.NotifyCanExecuteChanged();
    }

    internal static string SetText(SetUnit s, SetPlacement p)
    {
        var kind = s.Kind == SetKind.Panorama ? "Panorama" : "Hyperlapse";
        var text = $"{kind} · {Fmt.Count(s.Members.Length, "frame", "frames")} → {p.FolderName}";
        if (p.Resolution == SetResolution.Resume)
            text += $" (resuming: {Fmt.Count(p.MembersToCopy.Length, "frame", "frames")} missing)";
        else if (!string.Equals(p.FolderName, s.SetName, StringComparison.Ordinal) && p.Resolution != SetResolution.Imported)
            text += $" ({s.SetName} holds a different set)";
        return text;
    }

    public override string ToString() => Text;
}

/// <summary>The Photos tab (Ref §9.8).</summary>
public sealed partial class PhotosTabVm : ObservableObject
{
    private readonly Func<PlanEdit, Task> _apply;
    private readonly Func<IReadOnlyList<Item>, Task> _undoConfirmed;
    private PlanIndex? _index;

    public PhotosTabVm(Func<PlanEdit, Task> apply, Func<IReadOnlyList<Item>, Task> undoConfirmed)
    {
        _apply = apply;
        _undoConfirmed = undoConfirmed;
    }

    public ObservableCollection<PhotoDayVm> Days { get; } = [];
    public ObservableCollection<PhotoTileVm> Tiles { get; } = [];

    [ObservableProperty] public partial PhotoDayVm? SelectedDay { get; set; }
    [ObservableProperty] public partial bool ShowImportedDays { get; set; }
    [ObservableProperty] public partial string Header { get; private set; } = "Photos";
    [ObservableProperty] public partial string ShowImportedText { get; private set; } = "";

    internal void Update(PlanIndex ix)
    {
        _index = ix;
        var plan = ix.Plan;
        var all = plan.Base.PhotoDays.OrderBy(d => d.Date)
                      .Select(d => (Day: d, Items: d.Items.Where(ix.Items.ContainsKey).Select(i => ix.Items[i]).ToList()))
                      .ToList();
        var imported = all.Where(x => x.Items.Count > 0 && x.Items.All(i => i.Newness is Imported or Decided)).ToList();
        var visible = ShowImportedDays ? all : all.Except(imported).ToList();
        CollectionSync.Sync(Days, visible, x => x.Day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            x => new PhotoDayVm(x.Day.Date, _apply),
                            (vm, x) => vm.Update(x.Day, x.Items, plan.Included));
        Header = "Photos · " + Fmt.Count(all.Count, "day", "days");
        ShowImportedText = imported.Count == 0 ? "" : $"Show imported days ({imported.Count.ToString(CultureInfo.InvariantCulture)})";
        if (SelectedDay is not null && !Days.Contains(SelectedDay)) SelectedDay = null;
        RebuildTiles();
    }

    partial void OnSelectedDayChanged(PhotoDayVm? value) => RebuildTiles();

    partial void OnShowImportedDaysChanged(bool value)
    {
        if (_index is { } ix) Update(ix);
    }

    private void RebuildTiles()
    {
        if (_index is not { } ix || SelectedDay is null)
        {
            Tiles.Clear();
            return;
        }
        CollectionSync.Sync(Tiles, SelectedDay.Items, i => i.Raw.Unit.Id.CardRelPath,
                            i => new PhotoTileVm(i, _apply, _undoConfirmed),
                            (vm, i) => vm.Update(i, ix.Plan.Included.Contains(i.Raw.Unit.Id), ix.Plan.Base.Sets.GetValueOrDefault(i.Raw.Unit.Id)));
    }
}
```

- [ ] **Step 5: Implement the Other tab**

```csharp
// src/UasSort.Review/Review/OtherTabVm.cs
namespace UasSort.Review;

public sealed class OtherRowVm(string text, string? detail, IAsyncRelayCommand? undismissCommand)
{
    public string Text { get; } = text;
    public string? Detail { get; } = detail;
    public IAsyncRelayCommand? UndismissCommand { get; } = undismissCommand;
    public override string ToString() => Text;
}

public sealed class OtherSectionVm(string title, string? note, IReadOnlyList<OtherRowVm> rows, bool isCollapsed)
{
    public string Title { get; } = title;
    public string? Note { get; } = note;
    public IReadOnlyList<OtherRowVm> Rows { get; } = rows;
    public bool IsCollapsed { get; } = isCollapsed;
    public override string ToString() => Title;
}

/// <summary>The Other tab: unknown files, rule skips, probe errors, scan warnings, dismissed items (Ref §9.9).</summary>
public sealed partial class OtherTabVm(Func<IReadOnlyList<Item>, Task> undismiss) : ObservableObject
{
    public ObservableCollection<OtherSectionVm> Sections { get; } = [];

    [ObservableProperty] public partial string Header { get; private set; } = "Other";

    internal void Update(PlanIndex ix)
    {
        var scan = ix.Plan.Base.Scan;
        var sections = new List<OtherSectionVm>();
        var count = 0;

        var unknown = scan.Inventory.Entries.Where(e => e.Class == EntryClass.Unknown).ToList();
        if (unknown.Count > 0)
            sections.Add(new("Unknown files", "Not copied by uas-sort; copy manually if needed",
                             [.. unknown.Select(e => new OtherRowVm(e.RelPath, Fmt.Size(e.Size), null))], false));
        count += unknown.Count;

        var skipped = scan.Inventory.Entries.Where(e => e.Class == EntryClass.Skip).GroupBy(e => e.Rule ?? "Other", StringComparer.Ordinal).ToList();
        if (skipped.Count > 0)
            sections.Add(new("Skipped by rule", null,
                             [.. skipped.Select(g => new OtherRowVm($"{g.Key} · {Fmt.Count(g.Count(), "file", "files")}", null, null))], true));
        count += skipped.Sum(g => g.Count());

        var probe = ix.Plan.Base.Items.Where(i => i.Raw.ProbeError is not null).ToList();
        if (probe.Count > 0)
            sections.Add(new("Probe errors", null, [.. probe.Select(i => new OtherRowVm(i.Raw.Name, i.Raw.ProbeError, null))], false));
        count += probe.Count;

        if (scan.Warnings.Length > 0)
            sections.Add(new("Scan warnings", "Parts of the card couldn't be read; the verdict will be Don't format yet",
                             [.. scan.Warnings.Select(w => new OtherRowVm(w.Message, w.RelPath, null))], false));
        count += scan.Warnings.Length;

        var dismissed = ix.Plan.Base.Items.Where(i => i.Newness is Decided { Kind: DecisionKind.Dismissed }).ToList();
        if (dismissed.Count > 0)
            sections.Add(new("Dismissed by you", null,
                             [.. dismissed.Select(i => new OtherRowVm(i.Raw.Name, ClipRowVm.Status(i).Tooltip,
                                                                      new AsyncRelayCommand(() => undismiss([i]))))], false));
        count += dismissed.Count;

        Sections.Clear();
        foreach (var s in sections) Sections.Add(s);
        Header = "Other · " + count.ToString(CultureInfo.InvariantCulture);
    }
}
```

- [ ] **Step 6: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*PhotosTab*"`, `-- --filter-method "*OtherTab*"`, `-- --filter-method "*Decisions_*"`
Expected: PASS, 5 tests.

- [ ] **Step 7: Commit**

```bash
git add src/UasSort.Review tests/UasSort.Review.Tests/PhotosOtherTabTests.cs
git commit -m "feat: photos and other tabs with ledger decision service" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.13: `ReviewVm` — plan application, InfoBars, offload gating, edits, undo/redo, rename and retarget **[Review Focus]**

**Files:**
- Create: `src/UasSort.Review/Review/ReviewVm.cs`, `src/UasSort.Review/Review/ReviewVm.Edits.cs`
- Create: `tests/UasSort.Review.Tests/Fixtures/ReviewHarness.cs`
- Test: `tests/UasSort.Review.Tests/ReviewVmTests.cs`

**Interfaces:**
- Consumes: `PlanSession` (Part 06: `new PlanSession(PlanBase b, IPlanDeriver deriver, Tuning tuning, TimeProvider clock)`, `Current`, `Changed`, `ApplyAsync`, `ApplyAllAsync`, `Preview`, `CommitTuningAsync`, `UndoAsync`, `RedoAsync`, `AcceptLedgerIssues`, `ToDraft`, `CanUndo`, `CanRedo`, `static Resume(PlanBase, Draft, IPlanDeriver, out int)`), `EditResult`/`Applied`/`Rejected`, every edit record (`MoveToGroup` included), `TargetChoice` cases, `ReviewServices`, `IDecisionService`, `DecisionTargets`, the tab VMs, `TuningVm`/`ITuningHost`, `IssuesVm`, `MapBridge`/`MapProjection`/`MapSetBase`, `ClockSummary.Headline`, `ClockText`, `Footer`, `LedgerPaths.For`, Core `ZoneNames.Abbreviation`.
- Produces (defined here):
  - `public enum InfoSeverity { Informational, Success, Warning, Error }`
  - `public sealed class InfoBarVm(string key, InfoSeverity severity, string message, bool isClosable, IReadOnlyList<QuickFixVm> actions) : IKeyed`
  - `public sealed record DraftOffer(Draft Draft, PlanSession Session, int Dropped)`
  - `public sealed partial class ReviewVm : ObservableObject, IReviewActions, ITuningHost, IDisposable` —
    - `ReviewVm(PlanSession session, ReviewServices services, IDecisionService decisions, DraftOffer? offer = null)` (a fresh session is `new PlanSession(b, deriver, tuning, time)`; there is no session factory)
    - `PlanSession Session`, `Plan Plan`, `PlanIndex Index`, `VideosTabVm Videos`, `PhotosTabVm Photos`, `OtherTabVm Other`, `TuningVm Tuning`, `IssuesVm Issues`, `ObservableCollection<InfoBarVm> InfoBars`, `string CardChipText`, `MapBridge? Map` (settable; the App assigns it when the map pane is ready)
    - observable: `int SelectedTab`, `string FooterText`, `bool CanOffload`, `string? OffloadDisabledReason`, `bool NothingNew`, `string? EmptyStateText`, `bool CanUndo`, `bool CanRedo` (both mirror `Session.CanUndo`/`Session.CanRedo`, refreshed after every applied plan and every edit, undo, redo and slider commit), `string? LastError`, `bool IsReadOnly`, `string MapBase` (initially `Plan.Base.Scan.Settings.Map.Base`; setting it sends `new MapSetBase(value)` through `Map`)
    - commands: `IAsyncRelayCommand UndoCommand`, `IAsyncRelayCommand RedoCommand`, `IRelayCommand OffloadCommand`, `IRelayCommand ShowVerdictCommand`
    - events: `Action? OffloadRequested`, `Action? VerdictRequested`, `Action? RescanRequested`, `Action<ItemId>? FocusRenameRequested`, `Action<string>? UiActionRequested`, `Action<MapContextMenu>? MapContextMenuRequested`, `Action<MapToHost>? MapStatus`
    - methods: `void CloseInfoBar(string key)`, `Task MergeSelectedWithNextAsync()`, `Task MoveSelectedToNewGroupAsync()`, `Task ToggleSelectedClipsAsync()`, `Task BrowseRetargetAsync(GroupCardVm card, string? pickedFolder)`, `void OpenClip(ItemId id)`, `void GoTo(ItemId anchor)` (Videos tab: selects the card or folded run holding the anchor and the clip; a photo or set anchor selects the Photos tab and its day), `IReadOnlyList<GroupCardVm> MoveTargets()` (every other card whose target is not `AlreadyImported`), `Task MoveSelectedToGroupAsync(GroupCardVm target)` (applies `new MoveToGroup([.. Videos.SelectedClipIds], target.Anchor)`), `internal void OnPlanArrived(Plan p)`
    - partial hooks filled by Task 10.15: `partial void InitDraftOffer(DraftOffer? offer)`, `partial void AddDraftInfoBar(List<InfoBarVm> bars)`, `partial void OnEditCommitted()`, `partial void DisposeDrafts()`
  - Test-only: `ReviewHarness`

**Review Focus #1 (VM side):** a card where every clip and photo is already imported shows the explicit empty state "Nothing new on this card", with every AlreadyImported group folded (NothingToCopy groups follow the spec's fold rule and stay unfolded), and Offload disabled with exactly that reason, while [Show verdict] stays available so the verdict can still be computed (Safe) without an offload.

- [ ] **Step 1: Write the harness and the failing tests** **[Review Focus]** (`ReviewVm_NothingNewOnCard_EmptyStateFoldedAndOffloadDisabled` is Review Focus #1)

```csharp
// tests/UasSort.Review.Tests/Fixtures/ReviewHarness.cs
namespace UasSort.Review.Tests;

internal sealed class ReviewHarness : IDisposable
{
    private ReviewHarness(PlanBase b, Tuning tuning, ImmutableArray<Suggestion> suggestions, DraftOffer? offer, FakeDraftStore? drafts)
    {
        Base = b;
        Deriver = new GatedPlanDeriver(new ScriptedDeriver(suggestions));
        Session = new PlanSession(b, Deriver, tuning, Time);
        Drafts = drafts ?? new FakeDraftStore();
        Services = Fake.Services(Ui, Time, Drafts, Dialogs, Log);
        Decisions = new LedgerDecisionService(Ledger, Time, "PC1");
        Vm = new ReviewVm(Session, Services, Decisions, offer);
    }

    public PlanBase Base { get; }
    public FakeUiDispatcher Ui { get; } = new();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 28, 2, 0, 0, TimeSpan.Zero));
    public FakeDialogService Dialogs { get; } = new();
    public ListLog Log { get; } = new();
    public FakeLedgerStore Ledger { get; } = Fake.Ledger();
    public FakeDraftStore Drafts { get; }
    public GatedPlanDeriver Deriver { get; }
    public PlanSession Session { get; }
    public ReviewServices Services { get; }
    public LedgerDecisionService Decisions { get; }
    public ReviewVm Vm { get; }

    public static ReviewHarness Create(IReadOnlyList<PlanClip> clips, Tuning? tuning = null, ImmutableArray<Suggestion> suggestions = default,
                                       LedgerSnapshot? ledger = null, IReadOnlyList<Item>? extraItems = null,
                                       ImmutableArray<PhotoDay> photoDays = default, DraftOffer? offer = null, FakeDraftStore? drafts = null,
                                       PlanBase? planBase = null)
        => new(planBase ?? TestPlans.Base(clips, ledger: ledger, extraItems: extraItems, photoDays: photoDays),
               tuning ?? new Tuning(), suggestions, offer, drafts);

    /// <summary>Runs posted UI work until the VM shows the session's latest plan.</summary>
    public Task SettleAsync() => Eventually.TrueAsync(() => Vm.Plan.Revision >= Session.Current.Revision && Ui.Pending == 0, Ui);

    public GroupCardVm Card(int index) => Vm.Videos.Timeline.OfType<GroupCardVm>().ElementAt(index);

    public void Dispose() => Vm.Dispose();
}
```

```csharp
// tests/UasSort.Review.Tests/ReviewVmTests.cs
namespace UasSort.Review.Tests;

public class ReviewVmTests
{
    private static readonly LibraryFolderRef CouncilFolder = new(TestPlans.VideoRoot + @"\2026\2026-07\2026-07-25 Council Road", new(2026, 7, 25), "Council Road");
    private static readonly LibraryFolderRef AnvilFolder = new(TestPlans.VideoRoot + @"\2026\2026-07\2026-07-26 Anvil Mountain", new(2026, 7, 26), "Anvil Mountain");
    private static readonly ImmutableArray<Suggestion> ZacharName = [new("Zachar Bay", DescSource.Feature, Distance.FromMiles(0.4), new DateOnly(2026, 9, 27))];

    [Fact]
    public async Task ReviewVm_NothingNewOnCard_EmptyStateFoldedAndOffloadDisabled()
    {
        var ca = TestPlans.CouncilAnvil();
        IReadOnlyList<PlanClip> clips =
        [
            ca[0] with { Newness = new Imported(Evidence.LedgerVerified, CouncilFolder, "in the history") },
            ca[1] with { Newness = new Imported(Evidence.LedgerVerified, CouncilFolder, "in the history") },
            ca[2] with { Newness = new Imported(Evidence.LibraryNameSize, AnvilFolder, "same name and size") },
            ca[3] with { Newness = new Imported(Evidence.LibraryNameSize, AnvilFolder, "same name and size") },
        ];
        var photo = PhotosOtherTabTests.Photo("DJI_20260725200000_0101_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 0),
                                              new Imported(Evidence.LedgerVerified, null, "in the history"));
        using var h = ReviewHarness.Create(clips, extraItems: [photo],
            photoDays: [new PhotoDay(new DateOnly(2026, 7, 25), TestPlans.Anchorage, [photo.Raw.Unit.Id], "already imported")]);
        await h.SettleAsync();

        Assert.True(h.Vm.NothingNew);
        Assert.Equal("Nothing new on this card", h.Vm.EmptyStateText);
        Assert.All(h.Vm.Plan.Groups, g => Assert.IsType<AlreadyImported>(g.Target));
        Assert.All(h.Vm.Videos.Timeline, e => Assert.IsType<FoldedRunVm>(e));   // every AlreadyImported group folded
        Assert.False(h.Vm.CanOffload);
        Assert.False(h.Vm.OffloadCommand.CanExecute(null));
        Assert.Equal("Nothing new on this card", h.Vm.OffloadDisabledReason);
        Assert.True(h.Vm.ShowVerdictCommand.CanExecute(null));
        var asked = false;
        h.Vm.VerdictRequested += () => asked = true;
        h.Vm.ShowVerdictCommand.Execute(null);
        Assert.True(asked);
    }

    [Fact]
    public async Task ReviewVm_BlockingIssue_DisablesOffloadUntilNamed()
    {
        using var h = ReviewHarness.Create(TestPlans.Zachar());
        await h.SettleAsync();
        Assert.False(h.Vm.CanOffload);
        Assert.Equal("Fix before offloading:\n• Name this folder", h.Vm.OffloadDisabledReason);

        await h.Vm.RenameAsync(h.Card(0), "Zachar Bay");
        await h.SettleAsync();
        Assert.True(h.Vm.CanOffload);
        Assert.Null(h.Vm.OffloadDisabledReason);
        Assert.Equal("Zachar Bay", h.Card(0).Description);
        Assert.Equal("3 videos · 3.6 GB → C: (317 GB free)", h.Vm.FooterText);
    }

    [Fact]
    public async Task ReviewVm_ClockInfoBars_MismatchDismissedForSessionOnlyAndChipOnGroup()
    {
        var summary = TestPlans.Summary(ClockMode.Zone, 3, ["America/Anchorage"]);
        var b = TestPlans.Base(TestPlans.Zachar(), summary: summary);
        using (var h = ReviewHarness.Create([], planBase: b))
        {
            await h.SettleAsync();
            Assert.Contains(h.Vm.InfoBars, i => i.Key == "clock");
            var mm = Assert.Single(h.Vm.InfoBars, i => i.Key == "clockMismatch");
            Assert.Equal(InfoSeverity.Warning, mm.Severity);
            Assert.True(mm.IsClosable);
            Assert.StartsWith("Drone clock is set to UTC−4 (America/New_York), but footage on this card was shot in Alaska (UTC−8).", mm.Message, StringComparison.Ordinal);

            h.Vm.CloseInfoBar("clockMismatch");
            Assert.DoesNotContain(h.Vm.InfoBars, i => i.Key == "clockMismatch");
            await h.Vm.RenameAsync(h.Card(0), "Zachar Bay");
            await h.SettleAsync();
            Assert.DoesNotContain(h.Vm.InfoBars, i => i.Key == "clockMismatch");
        }

        using var rescan = ReviewHarness.Create([], planBase: b);
        await rescan.SettleAsync();
        Assert.Contains(rescan.Vm.InfoBars, i => i.Key == "clockMismatch");
    }

    [Fact]
    public async Task ReviewVm_AcceptAndContinue_TurnsLedgerIssueIntoAckWarningForThisSessionOnly()
    {
        var ledger = TestPlans.Ledger(parseIssues: [new LedgerParseIssue("ledger-PC2.jsonl", 17, "unexpected token")]);
        using var h = ReviewHarness.Create(TestPlans.Zachar(), ledger: ledger, suggestions: ZacharName);
        await h.SettleAsync();

        var bar = Assert.Single(h.Vm.InfoBars, i => i.Key.StartsWith("issue:LedgerParseIssue", StringComparison.Ordinal));
        Assert.Equal(InfoSeverity.Error, bar.Severity);
        Assert.False(h.Vm.CanOffload);
        await Assert.Single(bar.Actions, a => a.Label == "Accept and continue").Command.ExecuteAsync(null);
        await Eventually.TrueAsync(() => h.Vm.Plan.Issues.Any(i => i.Code == IssueCode.LedgerParseIssue && i.Severity == IssueSeverity.Warning), h.Ui);

        Assert.True(h.Vm.Plan.Issues.Single(i => i.Code == IssueCode.LedgerParseIssue).RequiresAckAtPreflight);
        Assert.True(h.Vm.CanOffload);
        Assert.Empty(h.Session.ToDraft().Edits);

        using var rescan = ReviewHarness.Create(TestPlans.Zachar(), ledger: ledger, suggestions: ZacharName);
        await rescan.SettleAsync();
        Assert.Equal(IssueSeverity.Blocking, rescan.Vm.Plan.Issues.Single(i => i.Code == IssueCode.LedgerParseIssue).Severity);
    }

    [Fact]
    public async Task ReviewVm_ChipMerge_UndoRedo()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil(), tuning: new Tuning(25, 1));
        await h.SettleAsync();
        Assert.Equal(2, h.Vm.Plan.Groups.Length);
        Assert.False(h.Vm.CanUndo);

        await h.Card(1).Chip!.MergeCommand.ExecuteAsync(null);
        await h.SettleAsync();
        Assert.Single(h.Vm.Plan.Groups);
        Assert.True(h.Vm.CanUndo);
        Assert.Equal((h.Session.CanUndo, h.Session.CanRedo), (h.Vm.CanUndo, h.Vm.CanRedo));

        await h.Vm.UndoCommand.ExecuteAsync(null);
        await h.SettleAsync();
        Assert.Equal(2, h.Vm.Plan.Groups.Length);
        Assert.True(h.Vm.CanRedo);
        Assert.Equal((h.Session.CanUndo, h.Session.CanRedo), (h.Vm.CanUndo, h.Vm.CanRedo));

        await h.Vm.RedoCommand.ExecuteAsync(null);
        await h.SettleAsync();
        Assert.Single(h.Vm.Plan.Groups);
    }

    [Fact]
    public async Task ReviewVm_SplitHereAndMoveToNewGroup()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        h.Vm.Videos.SelectedEntry = h.Card(0);
        var banner = h.Vm.Videos.Clips.Single(c => c.Banner is not null).Banner!;

        await banner.SplitCommand.ExecuteAsync(null);
        await h.SettleAsync();
        Assert.Equal(2, h.Vm.Plan.Groups.Length);
        Assert.Equal("── split by you ──", h.Card(1).Chip!.Text);

        h.Vm.Videos.SelectedEntry = h.Card(0);
        h.Vm.Videos.SetSelectedClips([TestPlans.Id(TestPlans.CouncilAnvil()[1].Name)]);
        await h.Vm.MoveSelectedToNewGroupAsync();
        await h.SettleAsync();
        Assert.Equal(3, h.Vm.Plan.Groups.Length);
    }

    [Fact]
    public async Task ReviewVm_QuickFixWithTwoEdits_IsOneUndoEntry()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        var anvil = TestPlans.Id(TestPlans.CouncilAnvil()[2].Name);
        var anvil2 = TestPlans.Id(TestPlans.CouncilAnvil()[3].Name);

        await h.Vm.ApplyQuickFixAsync(new QuickFix("Split and name", [new SplitBefore(anvil), new Rename(anvil, "Anvil Mountain", [anvil, anvil2])]));
        await h.SettleAsync();
        Assert.Equal(2, h.Vm.Plan.Groups.Length);
        Assert.Equal("Anvil Mountain", h.Vm.Plan.Groups[1].Description);

        await h.Vm.UndoCommand.ExecuteAsync(null);
        await h.SettleAsync();
        Assert.Single(h.Vm.Plan.Groups);
        Assert.False(h.Vm.CanUndo);
    }

    [Fact]
    public async Task ReviewVm_Rename_ValidationUnchangedAndRejectedOnAppend()
    {
        using var h = ReviewHarness.Create(TestPlans.Zachar(), suggestions: ZacharName);
        await h.SettleAsync();

        await h.Vm.RenameAsync(h.Card(0), "???");
        Assert.Equal("Use letters or numbers in the folder name", h.Vm.LastError);
        await h.Vm.RenameAsync(h.Card(0), "Zachar Bay");
        Assert.False(h.Vm.CanUndo);

        var folder = TestPlans.VideoRoot + @"\2026\2026-09\2026-09-27 Zachar Bay";
        await h.Vm.RetargetAsync(h.Card(0), new RetargetOptionVm(RetargetKind.Append, "Zachar Bay", null, folder, new DateOnly(2026, 9, 27)));
        await h.SettleAsync();
        Assert.Equal("APPEND", h.Card(0).Badge);

        await h.Vm.RenameAsync(h.Card(0), "Something else");
        await h.SettleAsync();
        Assert.Contains("Appending to an existing folder", h.Vm.LastError, StringComparison.Ordinal);
        Assert.Equal("APPEND", h.Card(0).Badge);
    }

    [Fact]
    public async Task ReviewVm_RetargetToLaterDatedFolder_NeedsConfirmation()
    {
        using var h = ReviewHarness.Create(TestPlans.Zachar(), suggestions: ZacharName);
        await h.SettleAsync();
        var later = new RetargetOptionVm(RetargetKind.Append, "Kodiak", "Sep 28 · 50 mi", TestPlans.VideoRoot + @"\2026\2026-09\2026-09-28 Kodiak", new DateOnly(2026, 9, 28));

        h.Dialogs.Answers.Enqueue(DialogResult.Close);
        await h.Vm.RetargetAsync(h.Card(0), later);
        Assert.Equal("Folder is dated Sep 28; these clips start Sep 27", Assert.Single(h.Dialogs.Shown).Body);
        Assert.False(h.Vm.CanUndo);

        h.Dialogs.Answers.Enqueue(DialogResult.Primary);
        await h.Vm.RetargetAsync(h.Card(0), later);
        await h.SettleAsync();
        var edit = Assert.IsType<Retarget>(Assert.Single(h.Session.ToDraft().Edits));
        Assert.True(edit.ConfirmedBeforeFolderDate);
        Assert.Equal("APPEND", h.Card(0).Badge);
    }

    [Theory]
    [InlineData(@"D:\Elsewhere\2026-09-27 X", "Pick a folder inside UAS Videos")]
    [InlineData(@"C:\Lib\UAS Videos\.uas-sort", "That folder is reserved for uas-sort history or photos")]
    [InlineData(@"C:\Lib\UAS Videos\.uas-sort\sub", "That folder is reserved for uas-sort history or photos")]
    [InlineData(@"C:\Lib\UAS Videos\Picture Offload", "That folder is reserved for uas-sort history or photos")]
    [InlineData(@"C:\Lib\UAS Videos", "Pick a folder inside UAS Videos")]
    public async Task ReviewVm_BrowseExisting_RefusesOutsideAndReservedFolders(string picked, string message)
    {
        using var h = ReviewHarness.Create(TestPlans.Zachar(), suggestions: ZacharName);
        await h.SettleAsync();
        await h.Vm.BrowseRetargetAsync(h.Card(0), picked);
        Assert.Equal(message, h.Vm.LastError);
        Assert.False(h.Vm.CanUndo);
    }

    [Fact]
    public async Task ReviewVm_BrowseExisting_ValidFolderAppends()
    {
        using var h = ReviewHarness.Create(TestPlans.Zachar(), suggestions: ZacharName);
        await h.SettleAsync();
        await h.Vm.BrowseRetargetAsync(h.Card(0), TestPlans.VideoRoot + @"\2026\2026-09\2026-09-27 Zachar Bay\");
        await h.SettleAsync();
        Assert.Equal("APPEND", h.Card(0).Badge);
    }

    [Fact]
    public async Task ReviewVm_MapClickSelectsGroupAndSendsSelect()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil(), tuning: new Tuning(25, 1));
        await h.SettleAsync();
        var sent = new List<string>();
        using var bridge = new MapBridge(sent.Add, h.Log, h.Time);
        h.Vm.Map = bridge;
        Assert.IsType<MapSetData>(MapBridge.ParseHostMessage(sent[0]));

        var anvil = h.Vm.Plan.Groups[1];
        bridge.Dispatch($$"""{"v":1,"type":"click","itemIds":["{{anvil.Videos[0].CardRelPath}}"],"groupId":"{{anvil.Id.Anchor.CardRelPath}}","ctrl":false,"shift":false}""");

        Assert.Same(h.Card(1), h.Vm.Videos.SelectedEntry);
        var select = sent.Select(MapBridge.ParseHostMessage).OfType<MapSelect>().Last();
        Assert.Equal(anvil.Id.Anchor.CardRelPath, select.GroupId);
    }

    [Fact]
    public async Task ReviewVm_LowerRevisionIsIgnored()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        var current = h.Vm.Plan;
        var stale = current with { Revision = current.Revision - 1, Groups = [] };

        h.Vm.OnPlanArrived(stale);
        Assert.Same(current, h.Vm.Plan);
    }

    [Fact]
    public async Task ReviewVm_MapBase_StartsFromSettingsAndSendsSetBase()
    {
        using var h = ReviewHarness.Create(TestPlans.Zachar());
        await h.SettleAsync();
        var sent = new List<string>();
        using var bridge = new MapBridge(sent.Add, h.Log, h.Time);
        h.Vm.Map = bridge;
        Assert.Equal("streets", h.Vm.MapBase);

        h.Vm.MapBase = "satellite";

        Assert.Equal("satellite", Assert.IsType<MapSetBase>(MapBridge.ParseHostMessage(sent[^1])).Base);
    }

    [Fact]
    public async Task ReviewVm_GoTo_SelectsTheCardAndClip_OrThePhotoDay()
    {
        var photo = PhotosOtherTabTests.Photo("DJI_20260725200000_0101_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 0), new IsNew(NewReason.DayHasNewVideos, null));
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil(), tuning: new Tuning(25, 1), extraItems: [photo],
            photoDays: [new PhotoDay(new DateOnly(2026, 7, 25), TestPlans.Anchorage, [photo.Raw.Unit.Id], "day has new videos")]);
        await h.SettleAsync();
        var anvil2 = TestPlans.Id(TestPlans.CouncilAnvil()[3].Name);
        var picked = new List<IReadOnlyList<ItemId>>();
        h.Vm.Videos.ClipSelectionChanged += picked.Add;

        h.Vm.GoTo(anvil2);
        Assert.Equal(0, h.Vm.SelectedTab);
        Assert.Same(h.Card(1), h.Vm.Videos.SelectedEntry);
        Assert.Equal<ItemId>([anvil2], h.Vm.Videos.SelectedClipIds);
        Assert.Equal<ItemId>([anvil2], picked[^1]);

        h.Vm.GoTo(photo.Raw.Unit.Id);
        Assert.Equal(1, h.Vm.SelectedTab);
        Assert.Equal(new DateOnly(2026, 7, 25), h.Vm.Photos.SelectedDay!.Date);
    }

    [Fact]
    public async Task ReviewVm_MoveTargetsAndMoveSelectedToGroup()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil(), tuning: new Tuning(25, 1));
        await h.SettleAsync();
        var c118 = TestPlans.Id(TestPlans.CouncilAnvil()[1].Name);
        h.Vm.Videos.SelectedEntry = h.Card(0);
        h.Vm.Videos.SetSelectedClips([c118]);

        var target = Assert.Single(h.Vm.MoveTargets());
        Assert.Same(h.Card(1), target);

        await h.Vm.MoveSelectedToGroupAsync(target);
        await h.SettleAsync();

        var move = Assert.IsType<MoveToGroup>(Assert.Single(h.Session.ToDraft().Edits));
        Assert.Equal<ItemId>([c118], move.Items);
        Assert.Equal(target.Anchor, move.InTarget);
        Assert.Contains(h.Vm.Plan.Groups, g => g.Videos.Contains(c118) && g.Videos.Length == 3);
        Assert.True(h.Vm.CanUndo);
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*ReviewVm_*"`
Expected: build FAILS (CS0246 `ReviewVm`, `DraftOffer`, `InfoSeverity`).

- [ ] **Step 3: Implement the core**

```csharp
// src/UasSort.Review/Review/ReviewVm.cs
namespace UasSort.Review;

public enum InfoSeverity { Informational, Success, Warning, Error }

/// <summary>One InfoBar under the title bar (Ref §9.2).</summary>
public sealed class InfoBarVm(string key, InfoSeverity severity, string message, bool isClosable, IReadOnlyList<QuickFixVm> actions) : IKeyed
{
    public string Key { get; } = key;
    public InfoSeverity Severity { get; } = severity;
    public string Message { get; } = message;
    public bool IsClosable { get; } = isClosable;
    public IReadOnlyList<QuickFixVm> Actions { get; } = actions;
    public override string ToString() => Message;
}

/// <summary>A saved draft for this card, already replayed into its own session (Ref §9.11).</summary>
public sealed record DraftOffer(Draft Draft, PlanSession Session, int Dropped);

/// <summary>The Review stage (Ref §9.2–9.13). Plans arrive from PlanSession off the UI thread and are applied through IUiDispatcher.
/// A fresh session is built by the caller as <c>new PlanSession(b, deriver, tuning, time)</c> (Part 06).</summary>
public sealed partial class ReviewVm : ObservableObject, IReviewActions, ITuningHost, IDisposable
{
    private static readonly HashSet<IssueCode> BannerCodes =
        [IssueCode.LedgerCloudOnly, IssueCode.LedgerNotPinned, IssueCode.LedgerParseIssue, IssueCode.LedgerUnwritable,
         IssueCode.RootMissing, IssueCode.RootsUnconfirmed];

    private readonly ReviewServices _s;
    private readonly IDecisionService _decisions;
    private readonly HashSet<string> _closedBars = new(StringComparer.Ordinal);
    private MapBridge? _map;
    private bool _clockMismatchDismissed;

    public ReviewVm(PlanSession session, ReviewServices services, IDecisionService decisions, DraftOffer? offer = null)
    {
        _s = services;
        _decisions = decisions;
        Session = session;
        Videos = new VideosTabVm(this);
        Photos = new PhotosTabVm(e => ApplyEditAsync(e), UndoConfirmedAsync);
        Other = new OtherTabVm(UndoConfirmedAsync);
        Tuning = new TuningVm(this, services.Time, services.Ui);
        Issues = new IssuesVm(RunIssueFixAsync);
        UndoCommand = new AsyncRelayCommand(UndoAsync, () => CanUndo && !IsReadOnly);
        RedoCommand = new AsyncRelayCommand(RedoAsync, () => CanRedo && !IsReadOnly);
        OffloadCommand = new RelayCommand(() => OffloadRequested?.Invoke(), () => CanOffload);
        ShowVerdictCommand = new RelayCommand(() => VerdictRequested?.Invoke(), () => NothingNew && !IsReadOnly);
        Videos.MapSelectRequested += (g, ids, fit) => _map?.Send(MapProjection.Select(Plan, g, ids, fit));

        Plan = session.Current;
        Index = new PlanIndex(Plan);
        var inv = Plan.Base.Scan.Inventory;
        var files = inv.Entries.Length;
        var bytes = inv.Entries.Sum(e => e.Size);
        CardChipText = inv.Source.Identity is { } id
            ? Fmt.CardChip(inv.Source.Root, id, inv.CameraModel, files, bytes)
            : $"{inv.Source.Root} · {Fmt.ModelName(inv.CameraModel)} · {Fmt.Count(files, "file", "files")} · {Fmt.Size(bytes)}";
        Tuning.SetCommitted(Plan.Tuning);
        MapBase = Plan.Base.Scan.Settings.Map.Base;
        InitDraftOffer(offer);
        Session.Changed += OnSessionChanged;
        Apply(Plan);
    }

    public PlanSession Session { get; private set; }
    public Plan Plan { get; private set; }
    public PlanIndex Index { get; private set; }
    public VideosTabVm Videos { get; }
    public PhotosTabVm Photos { get; }
    public OtherTabVm Other { get; }
    public TuningVm Tuning { get; }
    public IssuesVm Issues { get; }
    public ObservableCollection<InfoBarVm> InfoBars { get; } = [];
    public string CardChipText { get; }

    [ObservableProperty] public partial int SelectedTab { get; set; }
    [ObservableProperty] public partial string FooterText { get; private set; } = "";
    [ObservableProperty] public partial bool CanOffload { get; private set; }
    [ObservableProperty] public partial string? OffloadDisabledReason { get; private set; }
    [ObservableProperty] public partial bool NothingNew { get; private set; }
    [ObservableProperty] public partial string? EmptyStateText { get; private set; }
    [ObservableProperty] public partial bool CanUndo { get; private set; }
    [ObservableProperty] public partial bool CanRedo { get; private set; }
    [ObservableProperty] public partial string? LastError { get; private set; }
    [ObservableProperty] public partial bool IsReadOnly { get; set; }

    /// <summary>The map base layer ("streets", "satellite" or "none"); the App's selector binds it (Ref §9.6).</summary>
    [ObservableProperty] public partial string MapBase { get; set; } = "streets";

    public IAsyncRelayCommand UndoCommand { get; }
    public IAsyncRelayCommand RedoCommand { get; }
    public IRelayCommand OffloadCommand { get; }
    public IRelayCommand ShowVerdictCommand { get; }

    public event Action? OffloadRequested;
    public event Action? VerdictRequested;
    public event Action? RescanRequested;
    public event Action<ItemId>? FocusRenameRequested;
    public event Action<string>? UiActionRequested;
    public event Action<MapContextMenu>? MapContextMenuRequested;
    public event Action<MapToHost>? MapStatus;

    public MapBridge? Map
    {
        get => _map;
        set
        {
            if (_map is not null) _map.Received -= OnMapMessage;
            _map = value;
            if (_map is null) return;
            _map.Received += OnMapMessage;
            _map.SendData(Plan);
        }
    }

    /// <summary>Applies a plan on the UI thread; a plan with a lower Revision than the one shown is ignored (Ref §4.2).</summary>
    internal void OnPlanArrived(Plan p)
    {
        if (p.Revision < Plan.Revision) return;
        Apply(p);
    }

    public void CloseInfoBar(string key)
    {
        if (string.Equals(key, "clockMismatch", StringComparison.Ordinal)) _clockMismatchDismissed = true;
        else _closedBars.Add(key);
        RebuildInfoBars();
    }

    public void OpenClip(ItemId id)
        => _s.Shell.OpenFile(PathRules.Join(Plan.Base.Scan.Inventory.Source.Root, id.CardRelPath.Replace('/', '\\')));

    /// <summary>Issues flyout "go to": a video anchor selects the Videos tab, its card (or folded run) and the clip; a photo or set
    /// anchor selects the Photos tab and its day (showing imported days when that day is hidden).</summary>
    public void GoTo(ItemId anchor)
    {
        if (!Index.Items.TryGetValue(anchor, out var item)) return;
        if (item.Raw.Kind == ItemKind.Video)
        {
            SelectedTab = 0;
            Videos.Reveal(anchor);
            return;
        }
        SelectedTab = 1;
        if (!Photos.Days.Any(d => d.Items.Any(i => i.Raw.Unit.Id == anchor))
            && Plan.Base.PhotoDays.Any(d => d.Items.Contains(anchor)))
            Photos.ShowImportedDays = true;
        Photos.SelectedDay = Photos.Days.FirstOrDefault(d => d.Items.Any(i => i.Raw.Unit.Id == anchor)) ?? Photos.SelectedDay;
    }

    /// <summary>The clip menu's "Move to group ▸" targets: every other card that can take clips (not AlreadyImported).</summary>
    public IReadOnlyList<GroupCardVm> MoveTargets()
        => [.. Videos.Timeline.OfType<GroupCardVm>()
                  .Where(c => !ReferenceEquals(c, Videos.SelectedCard) && c.Group is { Target: not AlreadyImported })];

    public void Dispose()
    {
        Session.Changed -= OnSessionChanged;
        Map = null;
        Tuning.Dispose();
        DisposeDrafts();
    }

    partial void InitDraftOffer(DraftOffer? offer);
    partial void AddDraftInfoBar(List<InfoBarVm> bars);
    partial void OnEditCommitted();
    partial void DisposeDrafts();

    partial void OnIsReadOnlyChanged(bool value)
    {
        UpdateOffload();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    partial void OnMapBaseChanged(string value) => _map?.Send(new MapSetBase(value));

    private void OnSessionChanged(Plan p) => _s.Ui.Post(() => OnPlanArrived(p));

    private void Accept(Plan p) => _s.Ui.Post(() => OnPlanArrived(p));

    private void Apply(Plan p)
    {
        Plan = p;
        Index = new PlanIndex(p);
        Videos.Update(Index);
        Photos.Update(Index);
        Other.Update(Index);
        Issues.Update(p.Issues);
        Tuning.Sync(p.Tuning, p.Groups.Length);
        FooterText = Footer.Text(p, _s.Space);
        NothingNew = !p.Base.Items.Any(i => i.Newness is IsNew or Conflict);
        EmptyStateText = NothingNew ? "Nothing new on this card" : null;
        UpdateUndo();
        UpdateOffload();
        RebuildInfoBars();
        _map?.SendData(p);
    }

    private void UpdateOffload()
    {
        OffloadDisabledReason = IsReadOnly ? "The plan is read-only now"
                              : NothingNew ? "Nothing new on this card"
                              : Issues.HasBlocking ? Issues.BlockedTooltip
                              : Plan.Included.Count == 0 ? "Nothing is ticked to copy"
                              : null;
        CanOffload = OffloadDisabledReason is null;
        OffloadCommand.NotifyCanExecuteChanged();
        ShowVerdictCommand.NotifyCanExecuteChanged();
    }

    private void RebuildInfoBars()
    {
        var bars = new List<InfoBarVm>();
        var items = Plan.Base.Items;
        bars.Add(new InfoBarVm("clock", InfoSeverity.Informational, Plan.Base.Clock.Headline, false, []));
        if (!_clockMismatchDismissed && ClockText.MismatchInfoBar(Plan.Base.Clock, Plan.Base.Scan.Clock, items) is { } mismatch)
            bars.Add(new InfoBarVm("clockMismatch", InfoSeverity.Warning, mismatch, true, []));
        AddDraftInfoBar(bars);
        if (FirstRunText() is { } firstRun && !_closedBars.Contains("firstRun"))
            bars.Add(new InfoBarVm("firstRun", InfoSeverity.Informational, firstRun, true, []));
        foreach (var issue in Plan.Issues.Where(i => BannerCodes.Contains(i.Code)))
        {
            var severity = issue.Severity switch
            {
                IssueSeverity.Blocking => InfoSeverity.Error,
                IssueSeverity.Warning => InfoSeverity.Warning,
                _ => InfoSeverity.Informational,
            };
            bars.Add(new InfoBarVm($"issue:{issue.Code}:{issue.Message}", severity, issue.Message, false,
                                   [.. issue.QuickFixes.Select(f => new QuickFixVm(f.Label, () => RunIssueFixAsync(issue, f)))]));
        }
        InfoBars.Clear();
        foreach (var b in bars) InfoBars.Add(b);
    }

    private string? FirstRunText()
    {
        if (Plan.Base.Scan.Ledger.SourceFiles.Length > 0 || Plan.Base.PhotoDays.Length == 0) return null;
        if (Plan.Base.WatermarkUtc is not { } w)
            return "No photo history yet, and no videos were imported outside uas-sort: every photo not already in the library is ticked.";
        var zone = Plan.Base.Items.OrderBy(i => Math.Abs((i.Time.CaptureUtc - w).Ticks)).FirstOrDefault()?.Time.TzId ?? _s.Time.LocalTimeZone.Id;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(w, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(zone));
        return $"No photo history yet. Photos after {Fmt.Day(DateOnly.FromDateTime(local))} {Fmt.Clock(local)} {ZoneNames.Abbreviation(zone, w)}, within 75 min before it, or on days with new videos are ticked; the others are probably already in Lightroom. You can confirm them on the last screen.";
    }

    private void OnMapMessage(MapToHost m)
    {
        switch (m)
        {
            case MapClick c: Videos.OnMapClick(c); break;
            case MapClickEmpty: Videos.OnMapClickEmpty(); break;
            case MapContextMenu cm: MapContextMenuRequested?.Invoke(cm); break;
            default: MapStatus?.Invoke(m); break;
        }
    }

    private Task UndoConfirmedAsync(IReadOnlyList<Item> items)
    {
        var targets = items.SelectMany(DecisionTargets.For).ToList();
        _decisions.Revoke(_decisions.DecisionIdsFor(targets, Plan.Base.Scan.Ledger));
        RescanRequested?.Invoke();
        return Task.CompletedTask;
    }

    private Task RunIssueFixAsync(Issue issue, QuickFix fix)
        => fix.Edits.Length > 0 ? ApplyEditsAsync(fix.Edits) : RunUiFix(fix.Label, issue.Anchor);

    private Task RunUiFix(string label, ItemId? anchor)
    {
        switch (label)
        {
            case "Accept and continue":
                Session.AcceptLedgerIssues();
                break;
            case "Name it" when anchor is { } a:
                FocusRenameRequested?.Invoke(a);
                break;
            default:
                UiActionRequested?.Invoke(label);
                break;
        }
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Implement edits, undo/redo, rename and retarget**

```csharp
// src/UasSort.Review/Review/ReviewVm.Edits.cs
using System.Text.RegularExpressions;

namespace UasSort.Review;

public sealed partial class ReviewVm
{
    [GeneratedRegex(@"^(\d{4})-(\d{2})-(\d{2})(?:\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex DatedFolder();

    public Task SetIncludedAsync(IReadOnlyList<ItemId> items, bool included) => ApplyEditAsync(new SetIncluded([.. items], included));
    public Task SplitBeforeAsync(ItemId first) => ApplyEditAsync(new SplitBefore(first));
    public Task MergeAsync(ItemId inA, ItemId inB) => ApplyEditAsync(new Merge(inA, inB));

    public Task ApplyQuickFixAsync(QuickFix fix) => fix.Edits.Length > 0 ? ApplyEditsAsync(fix.Edits) : RunUiFix(fix.Label, null);

    public Task MergeSelectedWithNextAsync()
        => Videos.SelectedCard is { } card && Videos.NextCard(card) is { } next ? ApplyEditAsync(new Merge(card.Anchor, next.Anchor)) : Task.CompletedTask;

    public Task MoveSelectedToNewGroupAsync()
        => Videos.SelectedClipIds.Count == 0 ? Task.CompletedTask : ApplyEditAsync(new MoveToNewGroup([.. Videos.SelectedClipIds]));

    /// <summary>The clip menu's "Move to group ▸ target" (Ref §9.5): one MoveToGroup edit into the target card's group.</summary>
    public Task MoveSelectedToGroupAsync(GroupCardVm target)
        => Videos.SelectedClipIds.Count == 0 ? Task.CompletedTask : ApplyEditAsync(new MoveToGroup([.. Videos.SelectedClipIds], target.Anchor));

    public Task ToggleSelectedClipsAsync()
    {
        var ids = Videos.SelectedClipIds;
        if (ids.Count == 0) return Task.CompletedTask;
        return SetIncludedAsync(ids, !ids.All(Plan.Included.Contains));
    }

    public async Task RenameAsync(GroupCardVm card, string text)
    {
        if (card.Group is not { } g) return;
        var trimmed = text.Trim();
        if (string.Equals(trimmed, g.Description, StringComparison.Ordinal)) return;
        if (trimmed.Length > 0 && CleanForCheck(trimmed).Length == 0)
        {
            LastError = "Use letters or numbers in the folder name";
            return;
        }
        await ApplyEditAsync(new Rename(g.Id.Anchor, trimmed.Length == 0 ? null : trimmed, g.Videos)).ConfigureAwait(true);
    }

    public async Task RetargetAsync(GroupCardVm card, RetargetOptionVm option)
    {
        if (card.Group is not { } g) return;
        switch (option.Kind)
        {
            case RetargetKind.Auto:
                await ApplyEditAsync(new Retarget(g.Id.Anchor, new AutoTarget(), false, g.Videos)).ConfigureAwait(true);
                break;
            case RetargetKind.NewFolder:
                await ApplyEditAsync(new Retarget(g.Id.Anchor, new NewFolderTarget(), false, g.Videos)).ConfigureAwait(true);
                break;
            case RetargetKind.Skip:
                await ApplyEditAsync(new Retarget(g.Id.Anchor, new SkipTarget(), false, g.Videos)).ConfigureAwait(true);
                break;
            case RetargetKind.Append when option.FolderPath is { } path:
                await AppendToAsync(g, path, option.FolderDate).ConfigureAwait(true);
                break;
            case RetargetKind.Browse:
            default:
                break; // Browse: the App shows the FolderPicker, then calls BrowseRetargetAsync.
        }
    }

    /// <summary>Validates a Browse existing… result (Ref §9.4 item 4, §8.9 RetargetIntoReservedFolder).</summary>
    public async Task BrowseRetargetAsync(GroupCardVm card, string? pickedFolder)
    {
        if (card.Group is not { } g || string.IsNullOrWhiteSpace(pickedFolder)) return;
        var settings = Plan.Base.Scan.Settings;
        var picked = pickedFolder.TrimEnd('\\');
        var videoRoot = settings.VideoRoot.TrimEnd('\\');
        string[] reserved = [LedgerPaths.For(videoRoot), settings.PhotoRoot.TrimEnd('\\'), .. settings.PreviousPhotoRoots.Select(p => p.TrimEnd('\\'))];
        if (reserved.Any(r => IsSameOrUnder(picked, r)))
        {
            LastError = "That folder is reserved for uas-sort history or photos";
            return;
        }
        if (!IsSameOrUnder(picked, videoRoot) || string.Equals(picked, videoRoot, StringComparison.OrdinalIgnoreCase))
        {
            LastError = $"Pick a folder inside {Path.GetFileName(videoRoot)}";
            return;
        }
        var m = DatedFolder().Match(Path.GetFileName(picked));
        DateOnly? date = m.Success
            ? new DateOnly(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                           int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))
            : null;
        await AppendToAsync(g, picked, date).ConfigureAwait(true);
    }

    private async Task AppendToAsync(VideoGroup g, string path, DateOnly? folderDate)
    {
        var confirmed = false;
        if (folderDate is { } d && d > g.Start)
        {
            var answer = await _s.Dialogs.ShowAsync(new DialogRequest("Append to a later folder?",
                $"Folder is dated {Fmt.Day(d)}; these clips start {Fmt.Day(g.Start)}", "Append", null, "Cancel")).ConfigureAwait(true);
            if (answer != DialogResult.Primary) return;
            confirmed = true;
        }
        await ApplyEditAsync(new Retarget(g.Id.Anchor, new AppendTo(path), confirmed, g.Videos)).ConfigureAwait(true);
    }

    private async Task<bool> ApplyEditAsync(PlanEdit edit)
    {
        if (IsReadOnly) return false;
        return Handle(await Session.ApplyAsync(edit, CancellationToken.None).ConfigureAwait(true));
    }

    private async Task ApplyEditsAsync(IReadOnlyList<PlanEdit> edits)
    {
        if (IsReadOnly) return;
        Handle(await Session.ApplyAllAsync(edits, CancellationToken.None).ConfigureAwait(true));
    }

    private bool Handle(EditResult result) => result switch
    {
        Applied a => OnApplied(a.Plan),
        Rejected r => OnRejected(r),
    };

    private bool OnApplied(Plan p)
    {
        LastError = null;
        UpdateUndo();
        Accept(p);
        OnEditCommitted();
        return true;
    }

    private bool OnRejected(Rejected r)
    {
        LastError = r.Message;
        return false;
    }

    private async Task UndoAsync()
    {
        if (!Session.CanUndo || IsReadOnly) return;
        var p = await Session.UndoAsync().ConfigureAwait(true);
        AfterHistoryMove(p);
    }

    private async Task RedoAsync()
    {
        if (!Session.CanRedo || IsReadOnly) return;
        var p = await Session.RedoAsync().ConfigureAwait(true);
        AfterHistoryMove(p);
    }

    private void AfterHistoryMove(Plan p)
    {
        Tuning.SetCommitted(p.Tuning);
        UpdateUndo();
        Accept(p);
        OnEditCommitted();
    }

    /// <summary>CanUndo/CanRedo mirror PlanSession's own history (Part 06); the VM keeps no counters of its own.</summary>
    private void UpdateUndo()
    {
        CanUndo = Session.CanUndo;
        CanRedo = Session.CanRedo;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    void ITuningHost.Preview(Tuning t)
    {
        if (IsReadOnly) return;
        Session.Preview(t);
        _map?.Send(new MapSetRadius(t.RadiusMiles));
    }

    async Task ITuningHost.CommitTuningAsync(Tuning t)
    {
        if (IsReadOnly) return;
        await Session.CommitTuningAsync().ConfigureAwait(true);
        UpdateUndo();
        OnEditCommitted();
    }

    private static string CleanForCheck(string s)
    {
        var chars = s.Select(c => c < 0x20 || "<>:\"/\\|?*".Contains(c, StringComparison.Ordinal) ? ' ' : c).ToArray();
        return new string(chars).Trim().TrimEnd('.', ' ');
    }

    private static bool IsSameOrUnder(string path, string root)
        => string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 5: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*ReviewVm_*"`
Expected: PASS, 20 tests (the Theory counts 5).

- [ ] **Step 6: Commit**

```bash
git add src/UasSort.Review/Review/ReviewVm.cs src/UasSort.Review/Review/ReviewVm.Edits.cs tests/UasSort.Review.Tests/Fixtures/ReviewHarness.cs tests/UasSort.Review.Tests/ReviewVmTests.cs
git commit -m "feat: ReviewVm with plan application, InfoBars, offload gating and edits" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.14: Ordering tests with `GatedPlanDeriver` (latest wins, serial edits, dispatcher, revisions, instant rejection)

**Files:**
- Test: `tests/UasSort.Review.Tests/OrderingTests.cs`

**Interfaces:**
- Consumes: `GatedPlanDeriver` (Task 10.1), `ReviewHarness`, `ReviewVm`, `TuningVm`, `PlanSession` threading contract (Ref §4.2).
- Produces: nothing new; these tests pin the Ref §13 "Ordering" behaviour of Part 06's `PlanSession` as seen through the VMs. If one fails, the defect is in `PlanSession` or `ReviewVm` (never loosen the test).

- [ ] **Step 1: Write the tests**

```csharp
// tests/UasSort.Review.Tests/OrderingTests.cs
namespace UasSort.Review.Tests;

public class OrderingTests
{
    private static readonly ItemId AnvilFirst = TestPlans.Id(TestPlans.CouncilAnvil()[2].Name);

    [Fact]
    public async Task Ordering_SlowEarlierPreview_NeverOverwritesALaterOne()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        var n = h.Deriver.Calls.Count;

        h.Deriver.Hold = true;
        h.Vm.Tuning.BeginDrag();
        h.Vm.Tuning.RadiusMiles = 25;
        await h.Deriver.CallStartedAsync(n);
        h.Deriver.Hold = false;
        h.Vm.Tuning.RadiusMiles = 40;
        await Eventually.TrueAsync(() => h.Vm.Plan.Tuning.RadiusMiles == 40, h.Ui);

        h.Deriver.Release(n);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        h.Ui.RunAll();

        Assert.Equal(40, h.Vm.Plan.Tuning.RadiusMiles);
        Assert.Single(h.Vm.Plan.Groups);
    }

    [Fact]
    public async Task Ordering_EditQueuedBehindSlowDerive_IsValidatedAgainstThePlanWithTheEarlierEdit()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        var n = h.Deriver.Calls.Count;

        h.Deriver.Hold = true;
        var first = h.Vm.SplitBeforeAsync(AnvilFirst);
        await h.Deriver.CallStartedAsync(n);
        var second = h.Vm.SplitBeforeAsync(AnvilFirst);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted);

        h.Deriver.ReleaseAll();
        await first;
        await second;
        await h.SettleAsync();

        Assert.Equal(2, h.Vm.Plan.Groups.Length);
        Assert.NotNull(h.Vm.LastError);
        Assert.Single(h.Session.ToDraft().Edits);
    }

    [Fact]
    public async Task Ordering_ChangedArrivesOffTheUiThread_AndIsAppliedOnlyThroughTheDispatcher()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        var before = h.Vm.Plan;
        var mark = h.Ui.PostedWithAccess.Count;

        await Task.Run(() => h.Vm.SplitBeforeAsync(AnvilFirst), TestContext.Current.CancellationToken);
        await Eventually.TrueAsync(() => h.Ui.Pending > 0);

        Assert.Same(before, h.Vm.Plan);
        Assert.Contains(false, h.Ui.PostedWithAccess.Skip(mark));
        h.Ui.RunAll();
        Assert.Equal(2, h.Vm.Plan.Groups.Length);
    }

    [Fact]
    public async Task Ordering_PlanWithLowerRevisionPostedLate_IsIgnored()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        var older = h.Vm.Plan;
        await h.Vm.SplitBeforeAsync(AnvilFirst);
        await h.SettleAsync();
        var newer = h.Vm.Plan;

        h.Services.Ui.Post(() => h.Vm.OnPlanArrived(older));
        h.Ui.RunAll();
        Assert.Same(newer, h.Vm.Plan);
    }

    [Fact]
    public async Task Ordering_RejectedEdit_ReturnsWithoutWaitingForADerive()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        var n = h.Deriver.Calls.Count;
        var before = h.Vm.Plan;

        h.Deriver.Hold = true;
        var groupStart = h.Vm.Plan.Groups[0].Videos[0];
        await h.Vm.SplitBeforeAsync(groupStart).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(n, h.Deriver.Calls.Count);
        Assert.NotNull(h.Vm.LastError);
        Assert.Same(before, h.Vm.Plan);
        Assert.False(h.Vm.CanUndo);
        h.Deriver.ReleaseAll();
    }
}
```

- [ ] **Step 2: Run them**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Ordering_*"`
Expected: PASS, 5 tests. Before this file exists the filter matches no test (the failing state). A failure here means `PlanSession` (Part 06) or `ReviewVm.OnPlanArrived` breaks the Ref §4.2 contract: fix the code, not the test.

- [ ] **Step 3: Commit**

```bash
git add tests/UasSort.Review.Tests/OrderingTests.cs
git commit -m "test: gated-deriver ordering tests through the review view models" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 10.15: Drafts — 1 s autosave, resume offer with dropped count and pin warnings, discard

**Files:**
- Create: `src/UasSort.Review/Review/ReviewVm.Drafts.cs`
- Test: `tests/UasSort.Review.Tests/DraftTests.cs`

**Interfaces:**
- Consumes: `IDraftStore`, `Draft`, `PlanSession.ToDraft`, `PlanSession.Resume(PlanBase, Draft, IPlanDeriver, out int dropped)`, `CardSource.DraftKey`, `TimeProvider.LocalTimeZone`.
- Produces (defined here):
  - `ReviewVm` members: `static readonly TimeSpan DraftDelay` (1 s), `string DraftKey`, `DraftOffer? Offer`, `Task ResumeDraftAsync()`, `Task DiscardDraftAsync()`, `internal void SaveDraftNow()`; implementations of the partial hooks `InitDraftOffer`, `AddDraftInfoBar`, `OnEditCommitted`, `DisposeDrafts`
  - `public static class DraftOffers` — `DraftOffer? Find(PlanBase b, IDraftStore store, IPlanDeriver deriver)` (offered whenever the `DraftKey` matches, even when the `InventoryHash` differs; Ref §9.11)

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/DraftTests.cs
namespace UasSort.Review.Tests;

public class DraftTests
{
    private static readonly ItemId Council0 = TestPlans.Id(TestPlans.CouncilAnvil()[0].Name);
    private static readonly ItemId Council1 = TestPlans.Id(TestPlans.CouncilAnvil()[1].Name);
    private static readonly ItemId AnvilFirst = TestPlans.Id(TestPlans.CouncilAnvil()[2].Name);

    [Fact]
    public async Task Drafts_AutosaveOneSecondAfterTheLastChange()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();

        await h.Vm.SplitBeforeAsync(AnvilFirst);
        await h.SettleAsync();
        h.Time.Advance(TimeSpan.FromMilliseconds(600));
        await h.Vm.SetIncludedAsync([Council1], false);
        await h.SettleAsync();
        h.Time.Advance(TimeSpan.FromMilliseconds(999));
        h.Ui.RunAll();
        Assert.Equal(0, h.Drafts.Saves);

        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        h.Ui.RunAll();
        Assert.Equal(1, h.Drafts.Saves);
        var draft = h.Drafts.Drafts[TestPlans.Source.DraftKey];
        Assert.Equal(2, draft.Edits.Length);
        Assert.Equal(TestPlans.Source.DraftKey, h.Vm.DraftKey);
    }

    [Fact]
    public async Task Drafts_ChangedInventory_OfferedWithDroppedCountAndPinWarning()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var drafts = new FakeDraftStore();
        drafts.Save(TestPlans.Source.DraftKey, new Draft(1, TestPlans.Source.DraftKey, "0000000000000000",
            TestPlans.Utc(2026, 9, 28, 1, 2), new Tuning(),
            [new Rename(Council0, "Council Road", [Council0, Council1]), new SplitBefore(new ItemId("DCIM/DJI_001/DJI_20260725180000_0099_D.MP4"))]));

        var offer = DraftOffers.Find(b, drafts, new ScriptedDeriver());
        Assert.NotNull(offer);
        Assert.Equal(1, offer!.Dropped);

        using var h = ReviewHarness.Create([], planBase: b, offer: offer, drafts: drafts);
        await h.SettleAsync();
        var bar = Assert.Single(h.Vm.InfoBars, i => i.Key == "draft");
        Assert.Equal("Resume edits from 17:02? 1 of 2 still apply", bar.Message);

        await bar.Actions.Single(a => a.Label == "Resume").Command.ExecuteAsync(null);
        await h.SettleAsync();
        Assert.DoesNotContain(h.Vm.InfoBars, i => i.Key == "draft");
        Assert.Equal("Council Road", h.Vm.Plan.Groups[0].Description);
        Assert.Contains(h.Vm.Plan.Issues, i => i.Code == IssueCode.PinMembershipChanged && i.RequiresAckAtPreflight);
        Assert.Contains(h.Card(0).Chips, c => c.Kind == ChipKind.PinChanged);
    }

    [Fact]
    public async Task Drafts_Discard_DeletesTheDraft()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var drafts = new FakeDraftStore();
        drafts.Save(TestPlans.Source.DraftKey, new Draft(1, TestPlans.Source.DraftKey, b.Scan.Inventory.InventoryHash,
            TestPlans.Utc(2026, 9, 28, 1, 2), new Tuning(), [new SplitBefore(AnvilFirst)]));
        using var h = ReviewHarness.Create([], planBase: b, offer: DraftOffers.Find(b, drafts, new ScriptedDeriver()), drafts: drafts);
        await h.SettleAsync();

        await h.Vm.InfoBars.Single(i => i.Key == "draft").Actions.Single(a => a.Label == "Discard").Command.ExecuteAsync(null);

        Assert.Contains(TestPlans.Source.DraftKey, drafts.Deleted);
        Assert.DoesNotContain(h.Vm.InfoBars, i => i.Key == "draft");
        Assert.Single(h.Vm.Plan.Groups);
    }

    [Fact]
    public void Drafts_NoDraftForThisCard_NoOffer()
        => Assert.Null(DraftOffers.Find(TestPlans.Base(TestPlans.CouncilAnvil()), new FakeDraftStore(), new ScriptedDeriver()));
}
```

The resume text shows the draft's save time in the PC's local zone (`TimeProvider.LocalTimeZone`); the test expects 01:02Z shown as 17:02 in Alaska. Give the harness an Alaska PC zone by adding this line at the start of the `ReviewHarness` constructor (Task 10.13), before anything else runs; no other test reads the local zone:

```csharp
        Time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Anchorage"));
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Drafts_*"`
Expected: build FAILS (CS0103 `DraftOffers`; `DraftKey`, `Offer` missing on `ReviewVm`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Review/ReviewVm.Drafts.cs
namespace UasSort.Review;

public static class DraftOffers
{
    /// <summary>A draft is offered whenever its DraftKey matches this card, even if the InventoryHash differs (Ref §9.11).</summary>
    public static DraftOffer? Find(PlanBase b, IDraftStore store, IPlanDeriver deriver)
    {
        var key = b.Scan.Inventory.Source.DraftKey;
        if (store.Load(key) is not { } draft || draft.Edits.Length == 0) return null;
        var session = PlanSession.Resume(b, draft, deriver, out var dropped);
        return new DraftOffer(draft, session, dropped);
    }
}

public sealed partial class ReviewVm
{
    public static readonly TimeSpan DraftDelay = TimeSpan.FromSeconds(1);

    private ITimer? _draftTimer;

    public DraftOffer? Offer { get; private set; }

    public string DraftKey => Plan.Base.Scan.Inventory.Source.DraftKey;

    public Task ResumeDraftAsync()
    {
        if (Offer is not { } o) return Task.CompletedTask;
        Offer = null;
        Session.Changed -= OnSessionChanged;
        Session = o.Session;
        Session.Changed += OnSessionChanged;
        UpdateUndo();
        Tuning.SetCommitted(Session.Current.Tuning);
        Apply(Session.Current);
        OnEditCommitted();
        return Task.CompletedTask;
    }

    public Task DiscardDraftAsync()
    {
        if (Offer is null) return Task.CompletedTask;
        Offer = null;
        _s.Drafts.Delete(DraftKey);
        RebuildInfoBars();
        return Task.CompletedTask;
    }

    internal void SaveDraftNow()
    {
        _draftTimer?.Dispose();
        _draftTimer = null;
        _s.Drafts.Save(DraftKey, Session.ToDraft());
    }

    partial void InitDraftOffer(DraftOffer? offer) => Offer = offer;

    partial void AddDraftInfoBar(List<InfoBarVm> bars)
    {
        if (Offer is not { } o) return;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(o.Draft.SavedUtc, DateTimeKind.Utc), _s.Time.LocalTimeZone);
        var total = o.Draft.Edits.Length;
        var text = string.Create(CultureInfo.InvariantCulture, $"Resume edits from {Fmt.Clock(local)}? {total - o.Dropped} of {total} still apply");
        bars.Add(new InfoBarVm("draft", InfoSeverity.Informational, text, false,
                               [new QuickFixVm("Resume", ResumeDraftAsync), new QuickFixVm("Discard", DiscardDraftAsync)]));
    }

    /// <summary>Every committed change (edit, quick fix, slider commit, undo, redo, resume) restarts the 1 s draft timer (Ref §9.11).</summary>
    partial void OnEditCommitted()
    {
        _draftTimer?.Dispose();
        _draftTimer = _s.Time.CreateTimer(_ => _s.Ui.Post(SaveDraftNow), null, DraftDelay, Timeout.InfiniteTimeSpan);
    }

    partial void DisposeDrafts()
    {
        _draftTimer?.Dispose();
        _draftTimer = null;
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Drafts_*"`, then the whole Review suite `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj`
Expected: PASS (4 new tests; nothing else regresses).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Review/ReviewVm.Drafts.cs tests/UasSort.Review.Tests/DraftTests.cs tests/UasSort.Review.Tests/Fixtures/ReviewHarness.cs
git commit -m "feat: draft autosave and resume offer in ReviewVm" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---

### Task 10.16: Keyboard routing (page accelerators, list-scoped keys, TextBox undo exception)

**Files:**
- Create: `src/UasSort.Review/Review/ReviewVm.Keys.cs`
- Test: `tests/UasSort.Review.Tests/KeyboardTests.cs`

**Interfaces:**
- Consumes: `ReviewVm` commands and edit methods, `VideosTabVm`.
- Produces (defined here):
  - `public enum ReviewKey { Z, Y, M, N, S, D1, D2, D3, F2, F5, Enter, Space }`
  - `[Flags] public enum KeyMods { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }`
  - `public enum KeyFocus { Other, TextBox, TimelineItem, ClipItem, PhotoItem }`
  - `ReviewVm`: `bool HandleKey(ReviewKey key, KeyMods mods, KeyFocus focus)` (true = handled; Part 11 sets `Handled` from it), `ClipRowVm? FocusedClip { get; set; }`, `PhotoTileVm? FocusedTile { get; set; }`

Part 11 maps `KeyboardAccelerator`s (page keys) and the lists' `PreviewKeyDown` (list keys) to `HandleKey`, passing `KeyFocus.TextBox` when `FocusManager.GetFocusedElement(XamlRoot) is TextBox` (Ref §9.12).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/KeyboardTests.cs
namespace UasSort.Review.Tests;

public class KeyboardTests
{
    private static readonly ItemId AnvilFirst = TestPlans.Id(TestPlans.CouncilAnvil()[2].Name);

    [Fact]
    public async Task Keys_CtrlZ_UndoesExceptInATextBox()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        await h.Vm.SplitBeforeAsync(AnvilFirst);
        await h.SettleAsync();

        Assert.False(h.Vm.HandleKey(ReviewKey.Z, KeyMods.Ctrl, KeyFocus.TextBox));
        await h.SettleAsync();
        Assert.Equal(2, h.Vm.Plan.Groups.Length);

        Assert.True(h.Vm.HandleKey(ReviewKey.Z, KeyMods.Ctrl, KeyFocus.Other));
        await Eventually.TrueAsync(() => h.Vm.Plan.Groups.Length == 1, h.Ui);
        Assert.True(h.Vm.HandleKey(ReviewKey.Z, KeyMods.Ctrl | KeyMods.Shift, KeyFocus.Other));
        await Eventually.TrueAsync(() => h.Vm.Plan.Groups.Length == 2, h.Ui);
        Assert.False(h.Vm.HandleKey(ReviewKey.Y, KeyMods.Ctrl, KeyFocus.TextBox));
    }

    [Fact]
    public async Task Keys_SpaceInRenameBox_LeavesIncludedUnchanged()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        h.Vm.Videos.SelectedEntry = h.Card(0);
        h.Vm.Videos.SetSelectedClips([AnvilFirst]);
        var included = h.Vm.Plan.Included;

        Assert.False(h.Vm.HandleKey(ReviewKey.Space, KeyMods.None, KeyFocus.TextBox));
        await h.SettleAsync();
        Assert.Equal(included, h.Vm.Plan.Included);

        Assert.True(h.Vm.HandleKey(ReviewKey.Space, KeyMods.None, KeyFocus.ClipItem));
        await Eventually.TrueAsync(() => !h.Vm.Plan.Included.Contains(AnvilFirst), h.Ui);
    }

    [Fact]
    public async Task Keys_TabsMergeSplitOffloadRescanRename()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil(), tuning: new Tuning(25, 1),
            suggestions: [new Suggestion("Council Road", DescSource.Feature, Distance.FromMiles(0.2), null)]);
        await h.SettleAsync();
        var offload = 0;
        var rescan = 0;
        ItemId? renameFocus = null;
        h.Vm.OffloadRequested += () => offload++;
        h.Vm.RescanRequested += () => rescan++;
        h.Vm.FocusRenameRequested += a => renameFocus = a;

        Assert.True(h.Vm.HandleKey(ReviewKey.D2, KeyMods.Ctrl, KeyFocus.Other));
        Assert.Equal(1, h.Vm.SelectedTab);

        h.Vm.Videos.SelectedEntry = h.Card(0);
        Assert.True(h.Vm.HandleKey(ReviewKey.F2, KeyMods.None, KeyFocus.TimelineItem));
        Assert.Equal(h.Card(0).Anchor, renameFocus);

        Assert.True(h.Vm.HandleKey(ReviewKey.M, KeyMods.Ctrl, KeyFocus.TimelineItem));
        await Eventually.TrueAsync(() => h.Vm.Plan.Groups.Length == 1, h.Ui);

        h.Vm.Videos.SelectedEntry = h.Card(0);
        h.Vm.FocusedClip = h.Vm.Videos.Clips.Single(c => c.Id == AnvilFirst);
        Assert.True(h.Vm.HandleKey(ReviewKey.S, KeyMods.Ctrl | KeyMods.Shift, KeyFocus.ClipItem));
        await Eventually.TrueAsync(() => h.Vm.Plan.Groups.Length == 2, h.Ui);

        Assert.True(h.Vm.HandleKey(ReviewKey.Enter, KeyMods.Ctrl, KeyFocus.Other));
        Assert.Equal(1, offload);
        Assert.True(h.Vm.HandleKey(ReviewKey.F5, KeyMods.None, KeyFocus.Other));
        Assert.Equal(1, rescan);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Keys_*"`
Expected: build FAILS (CS0246 `ReviewKey`, `KeyMods`, `KeyFocus`; `HandleKey` missing).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Review/ReviewVm.Keys.cs
namespace UasSort.Review;

public enum ReviewKey { Z, Y, M, N, S, D1, D2, D3, F2, F5, Enter, Space }

[Flags]
public enum KeyMods { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

public enum KeyFocus { Other, TextBox, TimelineItem, ClipItem, PhotoItem }

public sealed partial class ReviewVm
{
    public ClipRowVm? FocusedClip { get; set; }
    public PhotoTileVm? FocusedTile { get; set; }

    /// <summary>Ref §9.12. Page keys use modifiers; list keys act only on item containers, never in a TextBox.</summary>
    public bool HandleKey(ReviewKey key, KeyMods mods, KeyFocus focus)
    {
        var ctrl = mods.HasFlag(KeyMods.Ctrl);
        var shift = mods.HasFlag(KeyMods.Shift);
        if (focus == KeyFocus.TextBox && key is ReviewKey.Z or ReviewKey.Y or ReviewKey.Space or ReviewKey.S or ReviewKey.M or ReviewKey.N)
            return false;

        switch (key)
        {
            case ReviewKey.Z when ctrl && !shift:
                _ = UndoCommand.ExecuteAsync(null);
                return true;
            case ReviewKey.Z when ctrl && shift:
            case ReviewKey.Y when ctrl:
                _ = RedoCommand.ExecuteAsync(null);
                return true;
            case ReviewKey.M when ctrl && !shift:
                _ = MergeSelectedWithNextAsync();
                return true;
            case ReviewKey.N when ctrl && shift:
                _ = MoveSelectedToNewGroupAsync();
                return true;
            case ReviewKey.D1 when ctrl:
                SelectedTab = 0;
                return true;
            case ReviewKey.D2 when ctrl:
                SelectedTab = 1;
                return true;
            case ReviewKey.D3 when ctrl:
                SelectedTab = 2;
                return true;
            case ReviewKey.F5 when !IsReadOnly:
                RescanRequested?.Invoke();
                return true;
            case ReviewKey.Enter when ctrl:
                if (OffloadCommand.CanExecute(null)) OffloadCommand.Execute(null);
                return true;
            case ReviewKey.Space when mods == KeyMods.None && focus == KeyFocus.ClipItem:
                _ = ToggleSelectedClipsAsync();
                return true;
            case ReviewKey.Space when mods == KeyMods.None && focus == KeyFocus.PhotoItem && FocusedTile is { } tile:
                _ = tile.ToggleCommand.ExecuteAsync(null);
                return true;
            case ReviewKey.S when ctrl && shift && focus == KeyFocus.ClipItem && FocusedClip is { } clip:
                _ = SplitBeforeAsync(clip.Id);
                return true;
            case ReviewKey.F2 when focus == KeyFocus.TimelineItem && Videos.SelectedCard is { } card:
                FocusRenameRequested?.Invoke(card.Anchor);
                return true;
            default:
                return false;
        }
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Keys_*"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Review/ReviewVm.Keys.cs tests/UasSort.Review.Tests/KeyboardTests.cs
git commit -m "feat: review keyboard routing with the TextBox undo exception" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.17: Setup stage and Settings page VMs (roots, derived ledger status, video-root move prompt)

**Files:**
- Create: `src/UasSort.Review/Settings/LedgerStatusText.cs`, `src/UasSort.Review/Settings/SetupVm.cs`, `src/UasSort.Review/Settings/SettingsPageVm.cs`
- Create: `tests/UasSort.Review.Tests/Fakes/FakeStores.cs`
- Test: `tests/UasSort.Review.Tests/SetupSettingsTests.cs`

**Interfaces:**
- Consumes: `Settings`, `SettingsLoad`, `ISettingsStore`, `ILedgerStore` (`Check`, `KeepOnDevice`, `CopyInto`), `LedgerFolderStatus`/`LedgerFolderState`, `LedgerSnapshot`, `LedgerPaths.For`/`FolderName`, `IDirectoryLister.Enumerate`, `IShellLauncher`, `IDialogService`, `IFreeSpace`, `StoredClockMode`, `MapSettings`; Part 05 `VideoRootChange.NeedsHistoryPrompt(LedgerFolderStatus, LedgerSnapshot)` and `VideoRootChange.AppCopiedVideos(ListingResult, string, LedgerSnapshot)` (`UasSort.Core.Ledger`) and `SettingsEdits.ChangePhotoRoot(Settings, string)` (`UasSort.Core.Config`); tests use `UasSort.Testing.FakeLedgerStore` (via `Fake.Ledger`) and Part 02's `FakeFileSystem` as the lister.
- Produces (defined here):
  - `public static class LedgerStatusText` — `(string Text, InfoSeverity Severity, bool OfferKeepOnDevice) For(LedgerFolderStatus s)`
  - `public sealed partial class SetupVm : ObservableObject` — `SetupVm(SettingsLoad load, ISettingsStore store, Func<string, ILedgerStore> ledgerFor, IFreeSpace space)`; observable `VideoRoot`, `PhotoRoot`, `VideoFreeText`, `PhotoFreeText`, `LedgerStatus`, `LedgerSeverity`, `CanKeepOnDevice`, `CanConfirm`, `RecoveryText`, `PhotoInsideVideoNote`; `void SetVideoRoot(string)`, `void SetPhotoRoot(string)`; `IRelayCommand ConfirmCommand`, `IRelayCommand KeepOnDeviceCommand`; `event Action<Settings>? Confirmed`
  - `public sealed class PreviousRootVm(string path, IRelayCommand forgetCommand)` (`Path`, `ForgetCommand`, `ToString()` = path)
  - `public sealed partial class SettingsPageVm : ObservableObject, IDisposable` — constructor below; `Settings Current`; observable roots, ledger card fields, `NoHistoryPrompt`, grouping defaults, drone clock, `CopyJpgTwin`, map fields; `Task ChangeVideoRootAsync(string newRoot)`, `void ChangePhotoRoot(string newRoot)`; commands `KeepOnDeviceCommand`, `OpenLedgerCommand`, `OpenBackupCommand`, `CopyLedgerCommand`, `StartEmptyCommand`; `static readonly TimeSpan SaveDelay` (500 ms); `string AboutText`
  - Test-only: `FakeSettingsStore` (the lister is Part 02's `FakeFileSystem`, which implements `IDirectoryLister`)

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/Fakes/FakeStores.cs
namespace UasSort.Review.Tests;

internal sealed class FakeSettingsStore(SettingsLoad load) : ISettingsStore
{
    public List<Settings> Saved { get; } = [];
    public List<string> Log { get; } = [];
    public SettingsLoad Load(bool readOnly = false) => load;
    public void Save(Settings s) { Saved.Add(s); Log.Add("Save:" + s.VideoRoot); }
}
```

```csharp
// tests/UasSort.Review.Tests/SetupSettingsTests.cs
namespace UasSort.Review.Tests;

public class SetupSettingsTests
{
    private const string NewRoot = @"D:\UAS Videos";

    private static LedgerFolderStatus Status(LedgerFolderState state, params string[] files)
        => new(TestPlans.VideoRoot + @"\.uas-sort", state, state != LedgerFolderState.Missing, true, state != LedgerFolderState.NotPinned, true,
               [.. files], state == LedgerFolderState.CloudOnly ? [.. files] : [], []);

    [Fact]
    public void LedgerStatusText_CoversEveryState()
    {
        Assert.Equal("History: 2 PCs' ledgers found", LedgerStatusText.For(Status(LedgerFolderState.Ok, "ledger-PC1.jsonl", "ledger-PC2.jsonl")).Text);
        Assert.Equal("History: 1 PC's ledger found", LedgerStatusText.For(Status(LedgerFolderState.Ok, "ledger-PC1.jsonl", "ledger-PC1-DESKTOP.jsonl")).Text);
        Assert.StartsWith("No history yet. It will be created at the first offload.", LedgerStatusText.For(Status(LedgerFolderState.Missing)).Text, StringComparison.Ordinal);
        var cloud = LedgerStatusText.For(Status(LedgerFolderState.CloudOnly, "ledger-PC2.jsonl"));
        Assert.Equal(("Set UAS Videos\\.uas-sort to Always keep on this device", InfoSeverity.Error, true), cloud);
        Assert.Equal(InfoSeverity.Warning, LedgerStatusText.For(Status(LedgerFolderState.NotPinned, "ledger-PC1.jsonl")).Severity);
        foreach (var state in Enum.GetValues<LedgerFolderState>())
            Assert.False(string.IsNullOrWhiteSpace(LedgerStatusText.For(Status(state, "ledger-PC1.jsonl")).Text), state.ToString());
    }

    [Fact]
    public void Setup_ConfirmSavesRootsConfirmed_AndPhotoRootFollowsVideoRoot()
    {
        var store = new FakeSettingsStore(new SettingsLoad(TestPlans.Settings(rootsConfirmed: false), false, null, null));
        var ledger = Fake.Ledger(Status(LedgerFolderState.Ok, "ledger-PC1.jsonl", "ledger-PC2.jsonl"));
        var vm = new SetupVm(store.Load(), store, _ => ledger, new FakeFreeSpace());
        Settings? confirmed = null;
        vm.Confirmed += s => confirmed = s;

        Assert.Equal("History: 2 PCs' ledgers found", vm.LedgerStatus);
        Assert.Equal("317 GB free", vm.VideoFreeText);
        vm.SetVideoRoot(NewRoot);
        Assert.Equal(NewRoot + @"\Picture Offload", vm.PhotoRoot);
        Assert.True(vm.PhotoInsideVideoNote);
        vm.ConfirmCommand.Execute(null);

        Assert.True(confirmed!.RootsConfirmed);
        Assert.Equal(NewRoot, store.Saved.Single().VideoRoot);
    }

    [Fact]
    public void Setup_CloudOnlyLedger_BlocksConfirmAndOffersKeepOnDevice()
    {
        var store = new FakeSettingsStore(new SettingsLoad(TestPlans.Settings(false), true, @"C:\x\settings.json.corrupt-1", new RunRoots(NewRoot, NewRoot + @"\Picture Offload")));
        var ledger = Fake.Ledger(Status(LedgerFolderState.CloudOnly, "ledger-PC2.jsonl"));
        var vm = new SetupVm(store.Load(), store, _ => ledger, new FakeFreeSpace());

        Assert.Equal(NewRoot, vm.VideoRoot);
        Assert.NotNull(vm.RecoveryText);
        Assert.False(vm.CanConfirm);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.True(vm.CanKeepOnDevice);
        vm.KeepOnDeviceCommand.Execute(null);
        Assert.Contains("KeepOnDevice", ledger.Calls);
    }

    /// <summary>Records CopyInto on top of the shared in-memory FakeLedgerStore (the offload fakes never copy a ledger).</summary>
    private sealed class CopyTarget(FakeLedgerStore inner) : ILedgerStore
    {
        public List<string> CopiedInto { get; } = [];
        public LedgerFolderStatus Check() => inner.Check();
        public LedgerSnapshot Load() => inner.Load();
        public void EnsureFolder() => inner.EnsureFolder();
        public ILedgerWriter OpenOwn() => inner.OpenOwn();
        public void SnapshotToBackup(string runId) => inner.SnapshotToBackup(runId);
        public void KeepOnDevice() => inner.KeepOnDevice();
        public void CopyInto(string newVideoRoot, LedgerSnapshot current) => CopiedInto.Add(newVideoRoot);
    }

    private static readonly DateTime T = TestPlans.Utc(2026, 9, 27, 20, 0);
    private static readonly FileKey CopiedKey = new("dji_20260927140627_0128_d.mp4", 1_200_000_000);

    /// <summary>The current ledger: one app-copied video (so VideoRootChange.HasRecords is true).</summary>
    private static LedgerSnapshot CurrentLedger()
        => TestPlans.Ledger() with
        {
            Files = ImmutableDictionary<FileKey, LedgerFile>.Empty.Add(CopiedKey,
                new LedgerFile(CopiedKey, "DCIM/DJI_001/DJI_20260927140627_0128_D.MP4", DestRoot.Video, NewRoot + @"\x.MP4", null,
                               VerifyKind.Unbuffered, T, null, null, null, null, null, null, "PC1", "run1")),
        };

    private static (SettingsPageVm Vm, FakeSettingsStore Store, Dictionary<string, CopyTarget> Ledgers, FakeFileSystem Fs, FakeDialogService Dialogs, FakeTimeProvider Time)
        Page()
    {
        var store = new FakeSettingsStore(new SettingsLoad(TestPlans.Settings(), false, null, null));
        var ledgers = new Dictionary<string, CopyTarget>(StringComparer.OrdinalIgnoreCase)
        {
            [TestPlans.VideoRoot] = new(Fake.Ledger(Status(LedgerFolderState.Ok, "ledger-PC1.jsonl"))),
            [NewRoot] = new(Fake.Ledger(Status(LedgerFolderState.Missing), videoRoot: NewRoot)),
        };
        var fs = FakeLayout.NewFileSystem();
        var dialogs = new FakeDialogService();
        var time = new FakeTimeProvider();
        var vm = new SettingsPageVm(TestPlans.Settings(), store, r => ledgers[r], fs, new FakeShellLauncher(), dialogs, new FakeFreeSpace(),
                                    time, new FakeUiDispatcher(), CurrentLedger, r => @"C:\AppData\uas-sort\ledger-backup\" + r.Length);
        return (vm, store, ledgers, fs, dialogs, time);
    }

    [Fact]
    public async Task Settings_VideoRootChange_SavesFirstThenPromptsAndCopies()
    {
        var (vm, store, ledgers, _, _, _) = Page();
        await vm.ChangeVideoRootAsync(NewRoot);

        Assert.Equal(NewRoot, store.Saved[^1].VideoRoot);
        Assert.Equal(@"No history found in D:\UAS Videos\.uas-sort; copy current ledger there?", vm.NoHistoryPrompt);
        Assert.Equal(NewRoot + @"\.uas-sort", vm.LedgerFolder);

        await vm.CopyLedgerCommand.ExecuteAsync(null);
        Assert.Equal<string>([NewRoot], ledgers[NewRoot].CopiedInto);
        Assert.Null(vm.NoHistoryPrompt);
    }

    [Fact]
    public async Task Settings_StartEmpty_ConfirmsWhenNewRootHoldsAppCopiedVideos()
    {
        var (vm, _, _, fs, dialogs, _) = Page();
        fs.AddFile(NewRoot + @"\2026\2026-09\2026-09-27 Zachar Bay\DJI_20260927140627_0128_D.MP4", 1_200_000_000L, T);
        await vm.ChangeVideoRootAsync(NewRoot);

        dialogs.Answers.Enqueue(DialogResult.Close);
        await vm.StartEmptyCommand.ExecuteAsync(null);
        Assert.StartsWith("1 video here was copied by uas-sort; starting empty treats them as manual imports", dialogs.Shown.Single().Body, StringComparison.Ordinal);
        Assert.NotNull(vm.NoHistoryPrompt);

        dialogs.Answers.Enqueue(DialogResult.Primary);
        await vm.StartEmptyCommand.ExecuteAsync(null);
        Assert.Null(vm.NoHistoryPrompt);
    }

    [Fact]
    public void Settings_PhotoRootChange_AppendsPreviousAndSavesAfter500Ms()
    {
        var (vm, store, _, _, _, time) = Page();
        vm.ChangePhotoRoot(@"D:\Photos");
        Assert.Empty(store.Saved);
        time.Advance(SettingsPageVm.SaveDelay);

        var saved = store.Saved.Single();
        Assert.Equal(@"D:\Photos", saved.PhotoRoot);
        Assert.Equal<string>([TestPlans.PhotoRoot], saved.PreviousPhotoRoots);
        Assert.Equal(TestPlans.PhotoRoot, Assert.Single(vm.PreviousPhotoRoots).Path);

        vm.PreviousPhotoRoots[0].ForgetCommand.Execute(null);
        time.Advance(SettingsPageVm.SaveDelay);
        Assert.Empty(store.Saved[^1].PreviousPhotoRoots);
    }

    [Fact]
    public void Settings_DefaultsClockAndMapFieldsSave()
    {
        var (vm, store, _, _, _, time) = Page();
        vm.RadiusMiles = 30;
        vm.IsSiteLocal = true;
        vm.CopyJpgTwin = false;
        vm.MapBase = "satellite";
        time.Advance(SettingsPageVm.SaveDelay);

        var s = store.Saved.Single();
        Assert.Equal((30.0, StoredClockMode.SiteLocal, false, "satellite"), (s.RadiusMiles, s.DroneClockMode, s.CopyJpgTwin, s.Map.Base));
        Assert.Contains("GeoNames CC-BY 4.0", vm.AboutText, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Settings_*"` and `-- --filter-method "*Setup_*"`
Expected: build FAILS (CS0246 `SetupVm`, `SettingsPageVm`; CS0103 `LedgerStatusText`).

- [ ] **Step 3: Implement the ledger status text**

```csharp
// src/UasSort.Review/Settings/LedgerStatusText.cs
using System.Text.RegularExpressions;

namespace UasSort.Review;

/// <summary>The read-only ledger status line of Setup and Settings (Ref §9.1, §9.14, §11 folder status).</summary>
public static partial class LedgerStatusText
{
    [GeneratedRegex(@"^ledger-(?<m>[^.\-]+)(?:-[^.]*)?\.jsonl$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LedgerName();

    public static (string Text, InfoSeverity Severity, bool OfferKeepOnDevice) For(LedgerFolderStatus s)
    {
        var library = Path.GetFileName(Path.GetDirectoryName(s.Folder.TrimEnd('\\')) ?? s.Folder);
        return s.State switch
        {
            LedgerFolderState.VideoRootMissing => ("The video folder doesn't exist; pick an existing folder", InfoSeverity.Error, false),
            LedgerFolderState.CloudOnly => ($"Set {library}\\.uas-sort to Always keep on this device", InfoSeverity.Error, true),
            LedgerFolderState.Unwritable => ($"Can't write the history file in {s.Folder}", InfoSeverity.Error, false),
            LedgerFolderState.Missing or LedgerFolderState.Empty =>
                ("No history yet. It will be created at the first offload. If you've used uas-sort on another PC, let OneDrive finish syncing first",
                 InfoSeverity.Informational, false),
            LedgerFolderState.NotPinned => ($"{History(s)} · not kept on this device", InfoSeverity.Warning, true),
            _ => (History(s), InfoSeverity.Informational, false),
        };
    }

    private static string History(LedgerFolderStatus s)
    {
        var machines = s.LedgerFiles.Select(f => LedgerName().Match(Path.GetFileName(f)))
                                    .Where(m => m.Success).Select(m => m.Groups["m"].Value.ToUpperInvariant())
                                    .Distinct(StringComparer.Ordinal).Count();
        return machines == 1 ? "History: 1 PC's ledger found" : string.Create(CultureInfo.InvariantCulture, $"History: {machines} PCs' ledgers found");
    }
}
```

- [ ] **Step 4: Implement Setup**

```csharp
// src/UasSort.Review/Settings/SetupVm.cs
namespace UasSort.Review;

/// <summary>First run and settings recovery: confirm the video and photo roots; the ledger is a status only (Ref §9.1 Setup).</summary>
public sealed partial class SetupVm : ObservableObject
{
    private readonly SettingsLoad _load;
    private readonly ISettingsStore _store;
    private readonly Func<string, ILedgerStore> _ledgerFor;
    private readonly IFreeSpace _space;

    public SetupVm(SettingsLoad load, ISettingsStore store, Func<string, ILedgerStore> ledgerFor, IFreeSpace space)
    {
        _load = load;
        _store = store;
        _ledgerFor = ledgerFor;
        _space = space;
        ConfirmCommand = new RelayCommand(Confirm, () => CanConfirm);
        KeepOnDeviceCommand = new RelayCommand(KeepOnDevice, () => CanKeepOnDevice);
        if (load.Recovered && load.RootsFromLastRun is { } last)
        {
            RecoveryText = "Settings couldn't be read, so the folders of the last offload on this PC are shown. Confirm them to continue.";
            VideoRoot = last.Video;
            PhotoRoot = last.Photo;
        }
        else
        {
            RecoveryText = load.Recovered ? "Settings couldn't be read. Confirm the folders to continue." : null;
            VideoRoot = load.Settings.VideoRoot;
            PhotoRoot = load.Settings.PhotoRoot;
        }
        Refresh();
    }

    [ObservableProperty] public partial string VideoRoot { get; private set; } = "";
    [ObservableProperty] public partial string PhotoRoot { get; private set; } = "";
    [ObservableProperty] public partial string VideoFreeText { get; private set; } = "";
    [ObservableProperty] public partial string PhotoFreeText { get; private set; } = "";
    [ObservableProperty] public partial string LedgerStatus { get; private set; } = "";
    [ObservableProperty] public partial InfoSeverity LedgerSeverity { get; private set; }
    [ObservableProperty] public partial bool CanKeepOnDevice { get; private set; }
    [ObservableProperty] public partial bool CanConfirm { get; private set; }
    [ObservableProperty] public partial string? RecoveryText { get; private set; }
    [ObservableProperty] public partial bool PhotoInsideVideoNote { get; private set; }

    public IRelayCommand ConfirmCommand { get; }
    public IRelayCommand KeepOnDeviceCommand { get; }
    public event Action<Settings>? Confirmed;

    public void SetVideoRoot(string path)
    {
        var followed = string.Equals(PhotoRoot, VideoRoot.TrimEnd('\\') + @"\Picture Offload", StringComparison.OrdinalIgnoreCase);
        VideoRoot = path.TrimEnd('\\');
        if (followed) PhotoRoot = VideoRoot + @"\Picture Offload";
        Refresh();
    }

    public void SetPhotoRoot(string path)
    {
        PhotoRoot = path.TrimEnd('\\');
        Refresh();
    }

    private void Refresh()
    {
        VideoFreeText = Free(VideoRoot);
        PhotoFreeText = Free(PhotoRoot);
        PhotoInsideVideoNote = PhotoRoot.StartsWith(VideoRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        var (text, severity, keep) = LedgerStatusText.For(_ledgerFor(VideoRoot).Check());
        LedgerStatus = text;
        LedgerSeverity = severity;
        CanKeepOnDevice = keep;
        CanConfirm = VideoRoot.Length > 0 && PhotoRoot.Length > 0 && severity != InfoSeverity.Error;
        ConfirmCommand.NotifyCanExecuteChanged();
        KeepOnDeviceCommand.NotifyCanExecuteChanged();
    }

    private string Free(string root) => _space.FreeBytes(root) is { } f ? $"{Fmt.Size(f)} free" : "not available";

    private void KeepOnDevice()
    {
        _ledgerFor(VideoRoot).KeepOnDevice();
        Refresh();
    }

    private void Confirm()
    {
        var s = _load.Settings with { VideoRoot = VideoRoot, PhotoRoot = PhotoRoot, RootsConfirmed = true };
        _store.Save(s);
        Confirmed?.Invoke(s);
    }
}
```

- [ ] **Step 5: Implement the Settings page**

```csharp
// src/UasSort.Review/Settings/SettingsPageVm.cs
namespace UasSort.Review;

public sealed class PreviousRootVm(string path, IRelayCommand forgetCommand)
{
    public string Path { get; } = path;
    public IRelayCommand ForgetCommand { get; } = forgetCommand;
    public override string ToString() => Path;
}

/// <summary>The Settings page (Ref §9.14). The ledger folder is derived from the video root and is never a setting.</summary>
public sealed partial class SettingsPageVm : ObservableObject, IDisposable
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly ISettingsStore _store;
    private readonly Func<string, ILedgerStore> _ledgerFor;
    private readonly IDirectoryLister _lister;
    private readonly IShellLauncher _shell;
    private readonly IDialogService _dialogs;
    private readonly IFreeSpace _space;
    private readonly TimeProvider _time;
    private readonly Func<LedgerSnapshot> _currentLedger;
    private readonly Func<string, string> _backupDirFor;
    private ITimer? _saveTimer;
    private bool _loading;

    public SettingsPageVm(Settings settings, ISettingsStore store, Func<string, ILedgerStore> ledgerFor, IDirectoryLister lister,
                          IShellLauncher shell, IDialogService dialogs, IFreeSpace space, TimeProvider time, IUiDispatcher ui,
                          Func<LedgerSnapshot> currentLedger, Func<string, string> backupDirFor)
    {
        _ = ui;
        Current = settings;
        _store = store;
        _ledgerFor = ledgerFor;
        _lister = lister;
        _shell = shell;
        _dialogs = dialogs;
        _space = space;
        _time = time;
        _currentLedger = currentLedger;
        _backupDirFor = backupDirFor;
        KeepOnDeviceCommand = new RelayCommand(() => { _ledgerFor(Current.VideoRoot).KeepOnDevice(); RefreshLedger(); });
        OpenLedgerCommand = new RelayCommand(() => _shell.OpenFolder(LedgerFolder));
        OpenBackupCommand = new RelayCommand(() => _shell.OpenFolder(BackupFolder));
        CopyLedgerCommand = new AsyncRelayCommand(CopyLedgerAsync);
        StartEmptyCommand = new AsyncRelayCommand(StartEmptyAsync);

        _loading = true;
        VideoRoot = settings.VideoRoot;
        PhotoRoot = settings.PhotoRoot;
        RadiusMiles = settings.RadiusMiles;
        GapDays = settings.GapDays;
        IsSiteLocal = settings.DroneClockMode == StoredClockMode.SiteLocal;
        ClockZone = settings.DroneClockZone;
        CopyJpgTwin = settings.CopyJpgTwin;
        MapBase = settings.Map.Base;
        StreetsUrl = settings.Map.StreetsStyleUrl;
        StreetsDarkUrl = settings.Map.StreetsDarkStyleUrl;
        SatelliteUrl = settings.Map.SatelliteUrl;
        _loading = false;
        RebuildPrevious();
        RefreshLedger();
    }

    public Settings Current { get; private set; }
    public ObservableCollection<PreviousRootVm> PreviousPhotoRoots { get; } = [];

    [ObservableProperty] public partial string VideoRoot { get; private set; } = "";
    [ObservableProperty] public partial string PhotoRoot { get; private set; } = "";
    [ObservableProperty] public partial string VideoFreeText { get; private set; } = "";
    [ObservableProperty] public partial string PhotoFreeText { get; private set; } = "";
    [ObservableProperty] public partial string LedgerFolder { get; private set; } = "";
    [ObservableProperty] public partial string BackupFolder { get; private set; } = "";
    [ObservableProperty] public partial string LedgerStatus { get; private set; } = "";
    [ObservableProperty] public partial InfoSeverity LedgerSeverity { get; private set; }
    [ObservableProperty] public partial bool CanKeepOnDevice { get; private set; }
    [ObservableProperty] public partial string? NoHistoryPrompt { get; private set; }
    [ObservableProperty] public partial double RadiusMiles { get; set; }
    [ObservableProperty] public partial int GapDays { get; set; }
    [ObservableProperty] public partial bool IsSiteLocal { get; set; }
    [ObservableProperty] public partial string ClockZone { get; set; } = "";
    [ObservableProperty] public partial bool CopyJpgTwin { get; set; }
    [ObservableProperty] public partial string MapBase { get; set; } = "";
    [ObservableProperty] public partial string StreetsUrl { get; set; } = "";
    [ObservableProperty] public partial string StreetsDarkUrl { get; set; } = "";
    [ObservableProperty] public partial string SatelliteUrl { get; set; } = "";

    public IRelayCommand KeepOnDeviceCommand { get; }
    public IRelayCommand OpenLedgerCommand { get; }
    public IRelayCommand OpenBackupCommand { get; }
    public IAsyncRelayCommand CopyLedgerCommand { get; }
    public IAsyncRelayCommand StartEmptyCommand { get; }

    public IReadOnlyDictionary<string, string> SatellitePresets => Current.Map.SatellitePresets;

    public string ClockLearnedText => Current.DroneClockMode == StoredClockMode.SiteLocal
        ? "Last learned: follows local time at each site"
        : $"Last learned: {Current.DroneClockZone}";

    public string AboutText =>
        "uas-sort · Maps: OpenFreeMap, © OpenStreetMap contributors · Imagery: Esri World Imagery, USGS · Places: GeoNames CC-BY 4.0 · MapLibre GL JS (BSD-3-Clause)";

    /// <summary>Ref §9.14 order: the new root is saved first, so the ledger's derived own-file path is the new one before [Copy] writes.</summary>
    public Task ChangeVideoRootAsync(string newRoot)
    {
        StopSave();
        Current = Current with { VideoRoot = newRoot.TrimEnd('\\'), RootsConfirmed = true };
        _store.Save(Current);
        _loading = true;
        VideoRoot = Current.VideoRoot;
        _loading = false;
        RefreshLedger();
        var status = _ledgerFor(Current.VideoRoot).Check();
        NoHistoryPrompt = VideoRootChange.NeedsHistoryPrompt(status, _currentLedger())
            ? $"No history found in {LedgerPaths.For(Current.VideoRoot)}; copy current ledger there?"
            : null;
        return Task.CompletedTask;
    }

    /// <summary>Ref §7.1: the old photo root joins PreviousPhotoRoots (Part 05 SettingsEdits.ChangePhotoRoot); saved 500 ms later.</summary>
    public void ChangePhotoRoot(string newRoot)
    {
        var next = SettingsEdits.ChangePhotoRoot(Current, newRoot);
        if (ReferenceEquals(next, Current)) return;
        Current = next;
        _loading = true;
        PhotoRoot = next.PhotoRoot;
        _loading = false;
        RebuildPrevious();
        RefreshLedger();
        ScheduleSave();
    }

    public void Dispose() => StopSave();

    partial void OnRadiusMilesChanged(double value) => Edit(s => s with { RadiusMiles = Math.Clamp(Math.Round(value), 5, 100) });
    partial void OnGapDaysChanged(int value) => Edit(s => s with { GapDays = Math.Clamp(value, 0, 7) });
    partial void OnIsSiteLocalChanged(bool value) => Edit(s => s with { DroneClockMode = value ? StoredClockMode.SiteLocal : StoredClockMode.Zone });
    partial void OnClockZoneChanged(string value) => Edit(s => s with { DroneClockZone = value });
    partial void OnCopyJpgTwinChanged(bool value) => Edit(s => s with { CopyJpgTwin = value });
    partial void OnMapBaseChanged(string value) => Edit(s => s with { Map = s.Map with { Base = value } });
    partial void OnStreetsUrlChanged(string value) => Edit(s => s with { Map = s.Map with { StreetsStyleUrl = value } });
    partial void OnStreetsDarkUrlChanged(string value) => Edit(s => s with { Map = s.Map with { StreetsDarkStyleUrl = value } });
    partial void OnSatelliteUrlChanged(string value) => Edit(s => s with { Map = s.Map with { SatelliteUrl = value } });

    private void Edit(Func<Settings, Settings> change)
    {
        if (_loading) return;
        Current = change(Current);
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        StopSave();
        _saveTimer = _time.CreateTimer(_ => SaveNow(), null, SaveDelay, Timeout.InfiniteTimeSpan);
    }

    private void SaveNow()
    {
        StopSave();
        _store.Save(Current);
    }

    private void StopSave()
    {
        _saveTimer?.Dispose();
        _saveTimer = null;
    }

    private void RebuildPrevious()
    {
        PreviousPhotoRoots.Clear();
        foreach (var p in Current.PreviousPhotoRoots)
            PreviousPhotoRoots.Add(new PreviousRootVm(p, new RelayCommand(() => Forget(p))));
    }

    private void Forget(string path)
    {
        Current = Current with { PreviousPhotoRoots = Current.PreviousPhotoRoots.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) };
        RebuildPrevious();
        ScheduleSave();
    }

    private void RefreshLedger()
    {
        LedgerFolder = LedgerPaths.For(Current.VideoRoot);
        BackupFolder = _backupDirFor(Current.VideoRoot);
        var (text, severity, keep) = LedgerStatusText.For(_ledgerFor(Current.VideoRoot).Check());
        LedgerStatus = text;
        LedgerSeverity = severity;
        CanKeepOnDevice = keep;
        VideoFreeText = _space.FreeBytes(Current.VideoRoot) is { } v ? $"{Fmt.Size(v)} free" : "not available";
        PhotoFreeText = _space.FreeBytes(Current.PhotoRoot) is { } p ? $"{Fmt.Size(p)} free" : "not available";
        OnPropertyChanged(nameof(ClockLearnedText));
    }

    private Task CopyLedgerAsync()
    {
        _ledgerFor(Current.VideoRoot).CopyInto(Current.VideoRoot, _currentLedger());
        NoHistoryPrompt = null;
        RefreshLedger();
        return Task.CompletedTask;
    }

    private async Task StartEmptyAsync()
    {
        var listing = _lister.Enumerate(Current.VideoRoot, true, new HashSet<string>([LedgerPaths.FolderName], StringComparer.OrdinalIgnoreCase));
        var copied = VideoRootChange.AppCopiedVideos(listing, Current.VideoRoot, _currentLedger());
        if (copied > 0)
        {
            var lead = copied == 1 ? "1 video here was copied by uas-sort" : string.Create(CultureInfo.InvariantCulture, $"{copied} videos here were copied by uas-sort");
            var answer = await _dialogs.ShowAsync(new DialogRequest("Start without history?",
                lead + "; starting empty treats them as manual imports and may mark un-copied photos as probably imported. [Copy] is recommended.",
                "Start empty", "Copy", "Cancel")).ConfigureAwait(true);
            if (answer == DialogResult.Secondary) { await CopyLedgerAsync().ConfigureAwait(true); return; }
            if (answer != DialogResult.Primary) return;
        }
        NoHistoryPrompt = null;
    }
}
```

- [ ] **Step 6: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Setup*"` and `-- --filter-method "*Settings_*"` and `-- --filter-method "*LedgerStatusText*"`
Expected: PASS, 7 tests.

- [ ] **Step 7: Commit**

```bash
git add src/UasSort.Review/Settings tests/UasSort.Review.Tests/SetupSettingsTests.cs tests/UasSort.Review.Tests/Fakes/FakeStores.cs
git commit -m "feat: setup and settings view models with derived ledger status" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.18: Card and Scan stage VMs **[Review Focus]**

**Files:**
- Create: `src/UasSort.Review/Stages/UiProgress.cs`, `src/UasSort.Review/Stages/CardStageVm.cs`, `src/UasSort.Review/Stages/ScanStageVm.cs`
- Test: `tests/UasSort.Review.Tests/CardScanStageTests.cs`

**Interfaces:**
- Consumes: `IVolumeProvider.GetVolumes()`, `VolumeInfo`, `CardCandidate`, `CardSourceCheck`/`SourceOk`/`SourceRefused`, `CardSource`, `ScanProgress`/`ScanPhase`, `ScanResult`, `PlanBase`, `Settings`, `UnsafeIoException` (Ref §4.3), `IUiDispatcher`.
- Produces (defined here):
  - `public sealed class UiProgress<T>(IUiDispatcher ui, Action<T> onReport) : IProgress<T>`
  - `public sealed class CardRowVm` (`Volume`, `Candidate`, `Text`, `KindText`, `IsDjiCard`, `IsWriteProtected`, `IRelayCommand UseCommand`, `ToString()` = `Text`)
  - `public sealed partial class CardStageVm : ObservableObject` — `CardStageVm(IVolumeProvider volumes, Func<IReadOnlyList<VolumeInfo>, IReadOnlyList<CardCandidate>> detect, Func<string, VolumeInfo?, CardSourceCheck> validate)`, `ObservableCollection<CardRowVm> Rows`, observable `string StatusText`, `string? Message`; `CardSource? Refresh()`; `void Browse(string? path)`; `IRelayCommand RescanCommand`; `event Action<CardSource>? CardChosen`
  - `public sealed partial class ScanStageVm : ObservableObject` — `ScanStageVm(Func<CardSource, IProgress<ScanProgress>, CancellationToken, Task<ScanResult>> scan, Func<ScanResult, PlanBase> prepare, Settings settings, IUiDispatcher ui)`, observable `string PhaseText`, `double Progress`, `bool IsIndeterminate`, `string? ErrorText`, `bool IsRunning`; `IRelayCommand CancelCommand`; `Task<PlanBase?> RunAsync(CardSource source)`; `static string PhaseTextFor(ScanProgress p, Settings s)`

`detect` is bound in Part 11 to `CardDetector.Detect(volumes, lister, settings)` and `validate` to `ICardSourceValidator.Validate(path, detected, settings, lister, pathFacts, appDataDir)`; `scan` to `ScanService.ScanAsync(source, readerFactory, progress, ct)` and `prepare` to `Planner.Prepare`.

**Review Focus #5 (VM side):** the drone over USB exposes two DJI volumes (internal storage and SD card). The Card stage lists both, never picks one by itself, and the chosen volume becomes its own run with its own identity (`CardSource.Identity`, hence its own `DraftKey`).

- [ ] **Step 1: Write the failing test** **[Review Focus]** (`CardStage_DroneOverUsbWithTwoDjiVolumes_ListsBothAndNeverAutoPicks` is Review Focus #5)

```csharp
// tests/UasSort.Review.Tests/CardScanStageTests.cs
namespace UasSort.Review.Tests;

public class CardScanStageTests
{
    private sealed class Volumes(params VolumeInfo[] v) : IVolumeProvider
    {
        public IReadOnlyList<VolumeInfo> GetVolumes() => v;
    }

    private static VolumeInfo Volume(string root, uint serial, string label, bool readOnly = false) =>
        new(root, new CardIdentity(serial, label, "exFAT", 256_060_514_304), "Removable", true, readOnly, false, true, 12_400_000_000,
            "Sd", true, false);

    private static CardSourceCheck Ok(string path, VolumeInfo? v)
        => new SourceOk(new CardSource(path, v?.Identity, v is null, v?.IsReadOnlyVolume ?? false));

    private static CardStageVm Stage(IVolumeProvider volumes, Func<string, VolumeInfo?, CardSourceCheck>? validate = null)
        => new(volumes, vs => [.. vs.Select(v => new CardCandidate(v, !v.Root.StartsWith('G'), v.Root.StartsWith('G') ? "not a DJI card" : null, 214))],
               validate ?? Ok);

    [Fact]
    public void CardStage_DroneOverUsbWithTwoDjiVolumes_ListsBothAndNeverAutoPicks()
    {
        var sd = Volume(@"E:\", 0x1A2B3C4D, "SD");
        var internalStorage = Volume(@"F:\", 0x5E6F7A8B, "DJI Internal");
        var vm = Stage(new Volumes(sd, internalStorage));
        var chosen = new List<CardSource>();
        vm.CardChosen += chosen.Add;

        Assert.Null(vm.Refresh());

        Assert.Equal(2, vm.Rows.Count);
        Assert.All(vm.Rows, r => Assert.True(r.IsDjiCard));
        Assert.Empty(chosen);
        Assert.Equal("2 DJI cards found. Pick one; each is offloaded on its own.", vm.StatusText);

        vm.Rows[1].UseCommand.Execute(null);
        var source = Assert.Single(chosen);
        Assert.Equal(0x5E6F7A8Bu, source.Identity!.VolumeSerial);
        Assert.Equal("vol-5E6F7A8B", source.DraftKey);
        Assert.NotEqual(new CardSource(@"E:\", sd.Identity, false, false).DraftKey, source.DraftKey);
    }

    [Fact]
    public void CardStage_SingleDjiCard_AutoAdvances()
    {
        var vm = Stage(new Volumes(Volume(@"E:\", 0x1A2B3C4D, "SD"), Volume(@"G:\", 0x11111111, "BACKUP")));
        CardSource? chosen = null;
        vm.CardChosen += s => chosen = s;

        var source = vm.Refresh();

        Assert.NotNull(source);
        Assert.Same(source, chosen);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("not a DJI card", vm.Rows[1].KindText);
    }

    [Fact]
    public void CardStage_WriteProtectedBadge_AndRefusalMessage()
    {
        var vm = Stage(new Volumes(Volume(@"E:\", 0x1A2B3C4D, "SD", readOnly: true), Volume(@"F:\", 2, "X")),
                       (path, v) => v is null ? new SourceRefused("This is part of your library (or a synced folder); uas-sort only offloads from cards.") : Ok(path, v));
        vm.Refresh();
        Assert.True(vm.Rows[0].IsWriteProtected);
        Assert.Equal(@"E:\ · SD · exFAT", vm.Rows[0].Text);
        Assert.Equal("DJI card · 214 media files · write-protected", vm.Rows[0].KindText);

        vm.Browse(@"C:\Lib\UAS Videos");
        Assert.Equal("This is part of your library (or a synced folder); uas-sort only offloads from cards.", vm.Message);
    }

    [Fact]
    public void CardStage_NoCard_ExplainsRescanAndBrowse()
    {
        var vm = Stage(new Volumes());
        Assert.Null(vm.Refresh());
        Assert.Equal("No DJI card found. Insert the card, then Rescan (F5), or browse to a folder.", vm.StatusText);
    }

    [Theory]
    [InlineData(ScanPhase.ListingCard, 0, 0, null, "Listing card")]
    [InlineData(ScanPhase.ListingLibrary, 0, 0, null, "Listing library (C:, D:)")]
    [InlineData(ScanPhase.ReadingLedger, 0, 2, null, "Reading ledger (2 PCs)")]
    [InlineData(ScanPhase.ReadingMetadata, 37, 214, "DJI_20260927140627_0128_D.MP4", "Reading metadata 37/214 · DJI_…0128")]
    [InlineData(ScanPhase.BuildingPlan, 0, 0, null, "Building plan")]
    public void ScanStage_PhaseTexts(ScanPhase phase, int done, int total, string? current, string expected)
    {
        var s = TestPlans.Settings() with { PreviousPhotoRoots = [@"D:\Old Photos"] };
        Assert.Equal(expected, ScanStageVm.PhaseTextFor(new ScanProgress(phase, done, total, current), s));
    }

    [Fact]
    public async Task ScanStage_Run_ReportsThroughDispatcherAndReturnsPlanBase()
    {
        var ui = new FakeUiDispatcher();
        var b = TestPlans.Base(TestPlans.Zachar());
        var vm = new ScanStageVm(async (src, progress, ct) =>
        {
            progress.Report(new ScanProgress(ScanPhase.ReadingMetadata, 1, 3, "DJI_20260927140127_0123_D.MP4"));
            await Task.Yield();
            return b.Scan;
        }, _ => b, TestPlans.Settings(), ui);

        var result = await vm.RunAsync(TestPlans.Source);
        ui.RunAll();

        Assert.Same(b, result);
        Assert.False(vm.IsRunning);
        Assert.Null(vm.ErrorText);
    }

    [Fact]
    public async Task ScanStage_CardRemoved_ShowsReinsertAndKeepsDraft()
    {
        var vm = new ScanStageVm((_, _, _) => Task.FromException<ScanResult>(new IOException("The device is not ready.")),
                                 _ => throw new InvalidOperationException("not reached"), TestPlans.Settings(), new FakeUiDispatcher());
        Assert.Null(await vm.RunAsync(TestPlans.Source));
        Assert.Equal("The card was removed or can't be read. Reinsert it, then Rescan. (The device is not ready.)", vm.ErrorText);
    }

    [Fact]
    public async Task ScanStage_Cancel_ReturnsNull()
    {
        var vm = new ScanStageVm(async (_, _, ct) => { await Task.Delay(Timeout.Infinite, ct); return null!; },
                                 _ => throw new InvalidOperationException("not reached"), TestPlans.Settings(), new FakeUiDispatcher());
        var run = vm.RunAsync(TestPlans.Source);
        vm.CancelCommand.Execute(null);
        Assert.Null(await run);
        Assert.Null(vm.ErrorText);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*CardStage*"`
Expected: build FAILS (CS0246 `CardStageVm`, `ScanStageVm`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Stages/UiProgress.cs
namespace UasSort.Review;

/// <summary>IProgress that always lands on the UI thread through IUiDispatcher (never a captured SynchronizationContext).</summary>
public sealed class UiProgress<T>(IUiDispatcher ui, Action<T> onReport) : IProgress<T>
{
    public void Report(T value) => ui.Post(() => onReport(value));
}
```

```csharp
// src/UasSort.Review/Stages/CardStageVm.cs
namespace UasSort.Review;

public sealed class CardRowVm
{
    public CardRowVm(CardCandidate c, Action<CardRowVm> use)
    {
        Candidate = c;
        var v = c.Volume;
        Text = $"{v.Root} · {v.Identity.Label ?? "no label"} · {v.Identity.FileSystem}";
        KindText = (c.IsDjiCard ? $"DJI card · {Fmt.Count(c.MediaCount, "media file", "media files")}" : c.NotCardReason ?? "not a DJI card")
                   + (v.IsReadOnlyVolume ? " · write-protected" : "");
        UseCommand = new RelayCommand(() => use(this), () => c.IsDjiCard);
    }

    public CardCandidate Candidate { get; }
    public VolumeInfo Volume => Candidate.Volume;
    public string Text { get; }
    public string KindText { get; }
    public bool IsDjiCard => Candidate.IsDjiCard;
    public bool IsWriteProtected => Candidate.Volume.IsReadOnlyVolume;
    public IRelayCommand UseCommand { get; }
    public override string ToString() => Text;
}

/// <summary>Shown only when zero or several DJI cards are found; never auto-picks among several (Ref §9.1 Card; Review Focus #5).</summary>
public sealed partial class CardStageVm : ObservableObject
{
    private readonly IVolumeProvider _volumes;
    private readonly Func<IReadOnlyList<VolumeInfo>, IReadOnlyList<CardCandidate>> _detect;
    private readonly Func<string, VolumeInfo?, CardSourceCheck> _validate;

    public CardStageVm(IVolumeProvider volumes, Func<IReadOnlyList<VolumeInfo>, IReadOnlyList<CardCandidate>> detect,
                       Func<string, VolumeInfo?, CardSourceCheck> validate)
    {
        _volumes = volumes;
        _detect = detect;
        _validate = validate;
        RescanCommand = new RelayCommand(() => Refresh());
    }

    public ObservableCollection<CardRowVm> Rows { get; } = [];

    [ObservableProperty] public partial string StatusText { get; private set; } = "";
    [ObservableProperty] public partial string? Message { get; private set; }

    public IRelayCommand RescanCommand { get; }
    public event Action<CardSource>? CardChosen;

    /// <summary>Re-detects; returns (and raises CardChosen for) the source only when exactly one DJI card is present and valid.</summary>
    public CardSource? Refresh()
    {
        Message = null;
        var candidates = _detect(_volumes.GetVolumes());
        Rows.Clear();
        foreach (var c in candidates) Rows.Add(new CardRowVm(c, Use));
        var dji = candidates.Where(c => c.IsDjiCard).ToList();
        StatusText = dji.Count switch
        {
            0 => "No DJI card found. Insert the card, then Rescan (F5), or browse to a folder.",
            1 => "",
            _ => string.Create(CultureInfo.InvariantCulture, $"{dji.Count} DJI cards found. Pick one; each is offloaded on its own."),
        };
        return dji.Count == 1 ? Choose(dji[0].Volume.Root, dji[0].Volume) : null;
    }

    public void Browse(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path)) Choose(path, null);
    }

    private void Use(CardRowVm row) => Choose(row.Volume.Root, row.Volume);

    private CardSource? Choose(string path, VolumeInfo? detected)
    {
        switch (_validate(path, detected))
        {
            case SourceOk ok:
                Message = null;
                CardChosen?.Invoke(ok.Source);
                return ok.Source;
            case SourceRefused refused:
                Message = refused.Reason;
                return null;
        }
        return null;
    }
}
```

```csharp
// src/UasSort.Review/Stages/ScanStageVm.cs
using System.Text.RegularExpressions;

namespace UasSort.Review;

/// <summary>The Scan stage: phases, progress, Cancel (Ref §9.1 Scan, §12 card removed during scan).</summary>
public sealed partial class ScanStageVm : ObservableObject
{
    private readonly Func<CardSource, IProgress<ScanProgress>, CancellationToken, Task<ScanResult>> _scan;
    private readonly Func<ScanResult, PlanBase> _prepare;
    private readonly Settings _settings;
    private readonly IUiDispatcher _ui;
    private CancellationTokenSource? _cts;

    public ScanStageVm(Func<CardSource, IProgress<ScanProgress>, CancellationToken, Task<ScanResult>> scan, Func<ScanResult, PlanBase> prepare,
                       Settings settings, IUiDispatcher ui)
    {
        _scan = scan;
        _prepare = prepare;
        _settings = settings;
        _ui = ui;
        CancelCommand = new RelayCommand(() => _cts?.Cancel());
    }

    [ObservableProperty] public partial string PhaseText { get; private set; } = "Listing card";
    [ObservableProperty] public partial double Progress { get; private set; }
    [ObservableProperty] public partial bool IsIndeterminate { get; private set; } = true;
    [ObservableProperty] public partial string? ErrorText { get; private set; }
    [ObservableProperty] public partial bool IsRunning { get; private set; }

    public IRelayCommand CancelCommand { get; }

    [GeneratedRegex(@"^DJI_\d{14}_(\d{4})", RegexOptions.CultureInvariant)]
    private static partial Regex DjiName();

    public static string PhaseTextFor(ScanProgress p, Settings s) => p.Phase switch
    {
        ScanPhase.ListingCard => "Listing card",
        ScanPhase.ListingLibrary => $"Listing library ({string.Join(", ", new[] { s.VideoRoot, s.PhotoRoot }.Concat(s.PreviousPhotoRoots).Select(Fmt.Drive).Distinct(StringComparer.OrdinalIgnoreCase))})",
        ScanPhase.ReadingLedger => p.Total > 0 ? $"Reading ledger ({Fmt.Count(p.Total, "PC", "PCs")})" : "Reading ledger",
        ScanPhase.ReadingMetadata => string.Create(CultureInfo.InvariantCulture, $"Reading metadata {p.Done}/{p.Total}") + (p.Current is { } c ? " · " + Short(c) : ""),
        ScanPhase.BuildingPlan => "Building plan",
        _ => p.Phase.ToString(),
    };

    public async Task<PlanBase?> RunAsync(CardSource source)
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        ErrorText = null;
        var progress = new UiProgress<ScanProgress>(_ui, p =>
        {
            PhaseText = PhaseTextFor(p, _settings);
            IsIndeterminate = p.Total == 0;
            Progress = p.Total == 0 ? 0 : (double)p.Done / p.Total;
        });
        try
        {
            var result = await _scan(source, progress, _cts.Token).ConfigureAwait(true);
            PhaseText = "Building plan";
            return await Task.Run(() => _prepare(result), _cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException ex)
        {
            ErrorText = $"The card was removed or can't be read. Reinsert it, then Rescan. ({ex.Message})";
            return null;
        }
        catch (UnsafeIoException ex)
        {
            ErrorText = "Internal safety stop: " + ex.Message;
            return null;
        }
        finally
        {
            IsRunning = false;
        }
    }

    private static string Short(string name)
    {
        var m = DjiName().Match(name);
        return m.Success ? "DJI_…" + m.Groups[1].Value : name;
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*CardStage*"` and `-- --filter-method "*ScanStage*"`
Expected: PASS, 12 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Stages tests/UasSort.Review.Tests/CardScanStageTests.cs
git commit -m "feat: card and scan stage view models; two DJI volumes never auto-picked" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.19: Preflight sheet and Copy VMs

**Files:**
- Create: `src/UasSort.Review/Commit/PreflightVm.cs`, `src/UasSort.Review/Commit/CopyVm.cs`
- Test: `tests/UasSort.Review.Tests/PreflightCopyTests.cs`

**Interfaces:**
- Consumes: Part 07's `CommitSession` (`static Begin(Plan, CommitEnvironment, string? runId = null)`, `Plan`, `Batch`, `Preflight`, `RequiredAcks`, `StartAsync(IReadOnlySet<AckKey>, IProgress<OffloadProgress>, CancellationToken)` → `CommitResult`, `Dispose()`), `CommitEnvironment`, `CommitResult` (`Offload`, `Verdict`, `ReportPath`, `FailureFree`), `AckKey` and `PreflightAcks.CanStart(PreflightReport, IReadOnlySet<AckKey>)` (Part 07), `PreflightReport` (`Issues`, `FoldersToCreate`, `FoldersAppended`, `Volumes`, `StaleTemps`), `VolumeNeed`, `OffloadProgress`, `CopyPhase`, `StopReason`, `IDraftStore.Delete`, `IDialogService`; tests use `UasSort.Testing.Offload.OffloadPlanBuilder`/`OffloadRig` and the shared `UasSort.Testing` fakes (`FakeFileOps`, `FakeLedgerStore`, `FakeOffloadLock`, `FakePowerRequest`, `FakeThumbnails`, `MemReportStore`, `FakeVolumeProvider`).
- Produces (defined here):
  - `public sealed record CommitPorts(IDraftStore Drafts, IDialogService Dialogs, IUiDispatcher Ui)`
  - `public sealed partial class AckVm(AckKey key) : ObservableObject` (`Key`, `Text` = `Key.Message`, `IsChecked`)
  - `public sealed partial class PreflightVm : ObservableObject, IDisposable` — `PreflightVm(Plan plan, CardSource source, Func<Plan, CommitSession> begin, CommitPorts ports)`, `void Open()` (= `begin(plan)`), `CommitSession? Session`, `OffloadBatch? Batch` (= `Session.Batch`), `PreflightReport? Report` (= `Session.Preflight`), `ObservableCollection<AckVm> Acks` (one per `Session.RequiredAcks` key), `IReadOnlyList<string> Blocking/Warnings/Infos/FoldersToCreate/FoldersAppended/VolumeLines`, observable `bool CanStart` (= `PreflightAcks.CanStart(Session.Preflight, checked keys)`), `string? LockMessage`, `Plan Plan`, `CardSource Source`, `IRelayCommand BackCommand`, `event Action? BackRequested`, `Task<CommitResult> StartAsync(IProgress<OffloadProgress> progress, CancellationToken ct)` (= `Session.StartAsync(...)`; deletes the draft when `FailureFree`), `void Dispose()` (= `Session.Dispose()`), `internal static string VolumeLine(VolumeNeed v)`
  - `public sealed partial class CopyVm : ObservableObject, IDisposable` — `CopyVm(PreflightVm preflight, IDialogService dialogs, IUiDispatcher ui)`, observable `FilesText`, `BytesText`, `SpeedText`, `EtaText`, `CurrentText`, `PhaseText`, `Fraction`, `IsRunning`, `ErrorText`; `IAsyncRelayCommand CancelCommand`; `Task<CommitResult?> RunAsync()`; `static string PhaseName(CopyPhase p)`; `internal void Apply(OffloadProgress p)`
  - There is no `CommitEngine` record: Part 11 binds `begin` to `plan => CommitSession.Begin(plan, commitEnvironment)`.

Opening the sheet is `CommitSession.Begin` (Part 07): it takes the offload lock and pauses card thumbnails, compiles and runs the preflight, and writes nothing (Ref §10.2). **Start offload** is `CommitSession.StartAsync`, which owns the order (Ref §4.4 step 6): keep-awake → `EnsureFolder()` → `SnapshotToBackup(runId)` → `DeleteOwnTemp` of each stale temp the preflight listed → own ledger writer → copy → `seen`/`cardLeftovers`/`run` records → re-list and `CardAudit.Audit` → report. A folder or writer that cannot be created copies nothing (every job `NotStarted`, stop `LedgerWriteFailed`). **Back** (or showing the verdict) is `CommitSession.Dispose()`: it releases the lock and the thumbnail pause and deletes nothing.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/PreflightCopyTests.cs
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Review.Tests;

public class PreflightCopyTests
{
    private const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private const string AckText = "Appending to 'Zachar Bay': different day, 34 mi from Council Road";

    /// <summary>A real CommitSession over Part 07's offload rig: an Append group with a stale temp from an earlier run and one new
    /// video (a second one with <c>twoVideos</c>), plus one plan issue that needs an acknowledgement.</summary>
    private sealed class Rig
    {
        public Rig(bool twoVideos = false)
        {
            var b = new OffloadPlanBuilder();
            var folder = new LibraryFolderRef(b.NewFolderPath(ZRel), new DateOnly(2026, 9, 27), "Zachar Bay");
            var v = b.Video("DJI_20260927160000_0160_D.MP4", 3_000, T0.AddHours(2));
            ItemId[] videos = twoVideos ? [v, b.Video("DJI_20260927160500_0161_D.MP4", 3_000, T0.AddHours(2).AddMinutes(5))] : [v];
            b.Group(new Append(folder, Confidence.High, "same day as clips already in this folder", null), Zachar, videos);
            b.Issue(new Issue(IssueSeverity.Warning, IssueCode.MediumAppend, AckText, v, [], true));
            Offload = new OffloadRig(b).Build();
            StaleTemp = folder.FullPath + @"\DJI_20260927150000_0150_D.MP4.uas-sort.tmp";
            Offload.Fs.AddFile(StaleTemp, 10, T0, 0x22);
            Env = new CommitEnvironment(Offload.Reader, Offload.Fs, Offload.Ledger, Offload.Lock, Offload.Power, Offload.Thumbnails,
                                        Offload.Reports, new FakeVolumeProvider(Offload.Volumes), dirs => Files = new FakeFileOps(Offload.Fs, dirs),
                                        Time, FakeLayout.Machine, "0.1.0");
        }

        public OffloadRig Offload { get; }
        public string StaleTemp { get; }
        public CommitEnvironment Env { get; }
        public FakeFileOps Files { get; private set; } = null!;
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(T0.AddHours(3)));
        public FakeDraftStore Drafts { get; } = new();
        public FakeDialogService Dialogs { get; } = new();
        public FakeUiDispatcher Ui { get; } = new();
        public CardSource Source => Offload.Plan.Base.Scan.Inventory.Source;

        public PreflightVm Preflight()
            => new(Offload.Plan, Source, plan => CommitSession.Begin(plan, Env, "run-1"), new CommitPorts(Drafts, Dialogs, Ui));
    }

    [Fact]
    public void Preflight_OpenBeginsTheSession_AcknowledgementsGateStart_WritesNothing()
    {
        var rig = new Rig();
        using var vm = rig.Preflight();
        vm.Open();

        Assert.Equal((1, 1), (rig.Offload.Lock.Holds, rig.Offload.Thumbnails.Paused));
        Assert.Same(vm.Session!.Preflight, vm.Report);
        Assert.Same(vm.Session.Batch, vm.Batch);
        var ack = Assert.Single(vm.Acks);
        Assert.Equal(new AckKey(IssueCode.MediumAppend, ack.Key.Anchor, AckText), ack.Key);
        Assert.Equal(AckText, ack.Text);
        Assert.Equal(vm.Report!.Volumes.Length, vm.VolumeLines.Count);
        Assert.False(vm.CanStart);

        ack.IsChecked = true;
        Assert.True(vm.CanStart);
        ack.IsChecked = false;
        Assert.False(vm.CanStart);
        Assert.Equal<string>(["Check"], rig.Offload.Ledger.Calls);
        Assert.True(rig.Offload.Fs.Exists(rig.StaleTemp));
    }

    [Fact]
    public void Preflight_VolumeLine_ShowsFilesSizeAndFreeAfter()
        => Assert.Equal("C: · 3 files · 3.6 GB · 317 GB free → 313.4 GB after",
                        PreflightVm.VolumeLine(new VolumeNeed("C:", 3, 3_600_000_000, 317_000_000_000, 4_673_741_824)));

    [Fact]
    public void Preflight_Back_DisposesTheSession_DeletesNothingAndReleasesTheLockAndPause()
    {
        var rig = new Rig();
        var vm = rig.Preflight();
        vm.Open();
        var back = false;
        vm.BackRequested += () => back = true;

        vm.BackCommand.Execute(null);

        Assert.True(back);
        Assert.Equal((0, 0), (rig.Offload.Lock.Holds, rig.Offload.Thumbnails.Paused));
        Assert.True(rig.Offload.Fs.Exists(rig.StaleTemp));
        Assert.DoesNotContain("EnsureFolder", rig.Offload.Ledger.Calls);
    }

    [Fact]
    public void Preflight_LockHeldElsewhere_BlocksWithMessage()
    {
        var rig = new Rig();
        rig.Offload.Lock.HeldElsewhere = true;
        using var vm = rig.Preflight();
        vm.Open();

        var held = Assert.Single(vm.Report!.Issues, i => i.Code == IssueCode.OffloadLockHeld);
        Assert.Equal(held.Message, vm.LockMessage);
        Assert.Contains(held.Message, vm.Blocking);
        vm.Acks[0].IsChecked = true;
        Assert.False(vm.CanStart);
    }

    [Fact]
    public async Task Copy_Start_RunsTheSession_ReportsProgress_DeletesDraftWhenFailureFree()
    {
        var rig = new Rig();
        using var preflight = rig.Preflight();
        preflight.Open();
        preflight.Acks[0].IsChecked = true;
        rig.Drafts.Save(rig.Source.DraftKey, new Draft(1, rig.Source.DraftKey, "h", DateTime.UnixEpoch, new Tuning(), []));
        using var copy = new CopyVm(preflight, rig.Dialogs, rig.Ui);

        var result = await copy.RunAsync();
        rig.Ui.RunAll();

        Assert.NotNull(result);
        Assert.True(result.FailureFree);
        Assert.Equal<string>(["Check", "EnsureFolder", "SnapshotToBackup run-1", "OpenOwn"], rig.Offload.Ledger.Calls);
        Assert.False(rig.Offload.Fs.Exists(rig.StaleTemp));
        Assert.Equal<string>(["Offloading drone media"], rig.Offload.Power.Reasons);
        Assert.Equal(1, rig.Offload.Thumbnails.Paused);                 // the session keeps the pause until the verdict is shown
        Assert.Same(rig.Offload.Reports.Offload.Single(), result.Report);
        Assert.Contains(rig.Source.DraftKey, rig.Drafts.Deleted);
        Assert.NotEqual("", copy.PhaseText);
        Assert.False(copy.IsRunning);
        Assert.Null(copy.ErrorText);

        preflight.Dispose();
        Assert.Equal((0, 0), (rig.Offload.Lock.Holds, rig.Offload.Thumbnails.Paused));
    }

    [Fact]
    public void Copy_Apply_FormatsTheProgress()
    {
        var rig = new Rig();
        using var preflight = rig.Preflight();
        using var copy = new CopyVm(preflight, rig.Dialogs, rig.Ui);

        copy.Apply(new OffloadProgress(1, 3, 1_200_000_000, 3_600_000_000, 85.4, TimeSpan.FromMinutes(3), "DJI_20260927140627_0128_D.MP4", CopyPhase.Verify, null));

        Assert.Equal("1 / 3 files", copy.FilesText);
        Assert.Equal("1.2 GB of 3.6 GB", copy.BytesText);
        Assert.Equal("85 MB/s", copy.SpeedText);
        Assert.Equal("3 min left", copy.EtaText);
        Assert.Equal("Verifying", copy.PhaseText);
        Assert.Equal("DJI_20260927140627_0128_D.MP4", copy.CurrentText);
        Assert.Equal(1.0 / 3, copy.Fraction, 6);
    }

    [Fact]
    public async Task Copy_LedgerFolderCannotBeCreated_CopiesNothingAndSaysSo()
    {
        var rig = new Rig();
        rig.Offload.Ledger.EnsureFolderThrows = true;
        rig.Drafts.Save(rig.Source.DraftKey, new Draft(1, rig.Source.DraftKey, "h", DateTime.UnixEpoch, new Tuning(), []));
        using var preflight = rig.Preflight();
        preflight.Open();
        preflight.Acks[0].IsChecked = true;
        using var copy = new CopyVm(preflight, rig.Dialogs, rig.Ui);

        var result = await copy.RunAsync();

        Assert.Equal(StopReason.LedgerWriteFailed, result!.Offload.Stop);
        Assert.All(result.Offload.Outcomes, o => Assert.IsType<NotStarted>(o));
        Assert.True(rig.Offload.Fs.Exists(rig.StaleTemp));
        Assert.DoesNotContain(rig.Source.DraftKey, rig.Drafts.Deleted);
        Assert.StartsWith("Couldn't create or open the history file", copy.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copy_Cancel_AsksThenStopsTheRun()
    {
        var rig = new Rig(twoVideos: true);
        using var preflight = rig.Preflight();
        preflight.Open();
        preflight.Acks[0].IsChecked = true;
        var ct = TestContext.Current.CancellationToken;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(false);
        rig.Files.OnTempWrite = (_, _) => { started.TrySetResult(); release.Wait(ct); };
        using var copy = new CopyVm(preflight, rig.Dialogs, rig.Ui);

        var run = Task.Run(copy.RunAsync, ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        rig.Dialogs.Answers.Enqueue(DialogResult.Close);
        await copy.CancelCommand.ExecuteAsync(null);
        Assert.False(run.IsCompleted);
        rig.Dialogs.Answers.Enqueue(DialogResult.Primary);
        await copy.CancelCommand.ExecuteAsync(null);
        release.Set();

        var result = await run;
        Assert.Equal(StopReason.Cancelled, result!.Offload.Stop);
        Assert.False(result.FailureFree);
        Assert.Equal("Stop the offload?", rig.Dialogs.Shown[0].Title);
        Assert.DoesNotContain(rig.Source.DraftKey, rig.Drafts.Deleted);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Preflight_*"`
Expected: build FAILS (CS0246 `PreflightVm`, `CommitPorts`, `CopyVm`, `AckVm`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Commit/PreflightVm.cs
namespace UasSort.Review;

/// <summary>The ports the Commit pages touch themselves; everything else happens inside Part 07's CommitSession.</summary>
public sealed record CommitPorts(IDraftStore Drafts, IDialogService Dialogs, IUiDispatcher Ui);

/// <summary>One acknowledgement checkbox of the sheet (Ref §10.2), keyed like PreflightAcks.</summary>
public sealed partial class AckVm(AckKey key) : ObservableObject
{
    public AckKey Key { get; } = key;
    public string Text => Key.Message;
    [ObservableProperty] public partial bool IsChecked { get; set; }
    public override string ToString() => Text;
}

/// <summary>The preflight sheet (Ref §10.2) over Part 07's CommitSession: Open begins the session (lock, thumbnail pause, preflight;
/// nothing written), Start offload runs it, Back or the verdict disposes it.</summary>
public sealed partial class PreflightVm : ObservableObject, IDisposable
{
    private readonly Func<Plan, CommitSession> _begin;
    private readonly CommitPorts _ports;

    public PreflightVm(Plan plan, CardSource source, Func<Plan, CommitSession> begin, CommitPorts ports)
    {
        Plan = plan;
        Source = source;
        _begin = begin;
        _ports = ports;
        BackCommand = new RelayCommand(Back);
    }

    public Plan Plan { get; }
    public CardSource Source { get; }
    public CommitSession? Session { get; private set; }
    public OffloadBatch? Batch => Session?.Batch;
    public PreflightReport? Report => Session?.Preflight;
    public ObservableCollection<AckVm> Acks { get; } = [];
    public IReadOnlyList<string> Blocking { get; private set; } = [];
    public IReadOnlyList<string> Warnings { get; private set; } = [];
    public IReadOnlyList<string> Infos { get; private set; } = [];
    public IReadOnlyList<string> FoldersToCreate { get; private set; } = [];
    public IReadOnlyList<string> FoldersAppended { get; private set; } = [];
    public IReadOnlyList<string> VolumeLines { get; private set; } = [];

    [ObservableProperty] public partial bool CanStart { get; private set; }
    [ObservableProperty] public partial string? LockMessage { get; private set; }

    public IRelayCommand BackCommand { get; }
    public event Action? BackRequested;

    /// <summary>Begins the Commit (CommitSession.Begin: lock, thumbnail pause, compile, preflight; writes nothing) and fills the sheet.</summary>
    public void Open()
    {
        var session = Session ??= _begin(Plan);
        var report = session.Preflight;
        LockMessage = report.Issues.FirstOrDefault(i => i.Code == IssueCode.OffloadLockHeld)?.Message;

        Acks.Clear();
        foreach (var key in session.RequiredAcks)
        {
            var ack = new AckVm(key);
            ack.PropertyChanged += (_, _) => UpdateCanStart();
            Acks.Add(ack);
        }
        Blocking = [.. report.Issues.Where(i => i.Severity == IssueSeverity.Blocking).Select(i => i.Message)];
        Warnings = [.. report.Issues.Where(i => i.Severity == IssueSeverity.Warning && !i.RequiresAckAtPreflight).Select(i => i.Message)];
        Infos = [.. report.Issues.Where(i => i.Severity == IssueSeverity.Info).Select(i => i.Message)];
        FoldersToCreate = report.FoldersToCreate;
        FoldersAppended = [.. report.FoldersAppended.Select(f => $"{f.Path} · {f.Confidence}")];
        VolumeLines = [.. report.Volumes.Select(VolumeLine)];
        OnPropertyChanged(string.Empty);
        UpdateCanStart();
    }

    /// <summary>Start offload = CommitSession.StartAsync (Part 07 owns the order); the draft is deleted only for a failure-free run.</summary>
    public async Task<CommitResult> StartAsync(IProgress<OffloadProgress> progress, CancellationToken ct)
    {
        if (!CanStart || Session is not { } session)
            throw new InvalidOperationException("Start offload is disabled until every blocking issue is gone and every box is ticked.");
        var result = await session.StartAsync(Checked(), progress, ct).ConfigureAwait(true);
        if (result.FailureFree) _ports.Drafts.Delete(Source.DraftKey);
        return result;
    }

    /// <summary>Back, or the verdict being shown: releases the offload lock and the thumbnail pause; deletes nothing.</summary>
    public void Dispose() => Session?.Dispose();

    internal static string VolumeLine(VolumeNeed v)
        => $"{v.Volume} · {Fmt.Count(v.Files, "file", "files")} · {Fmt.Size(v.Bytes)} · {Fmt.Size(v.FreeBytes)} free → {Fmt.Size(v.FreeBytes - v.Bytes)} after";

    private void Back()
    {
        Dispose();
        BackRequested?.Invoke();
    }

    private HashSet<AckKey> Checked() => [.. Acks.Where(a => a.IsChecked).Select(a => a.Key)];

    private void UpdateCanStart() => CanStart = Session is { } s && PreflightAcks.CanStart(s.Preflight, Checked());
}
```

```csharp
// src/UasSort.Review/Commit/CopyVm.cs
namespace UasSort.Review;

/// <summary>The Copy page: progress at 10 Hz and Cancel (Ref §7.2 step 12, §10.3).</summary>
public sealed partial class CopyVm : ObservableObject, IDisposable
{
    private readonly PreflightVm _preflight;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly CancellationTokenSource _cts = new();

    public CopyVm(PreflightVm preflight, IDialogService dialogs, IUiDispatcher ui)
    {
        _preflight = preflight;
        _dialogs = dialogs;
        _ui = ui;
        CancelCommand = new AsyncRelayCommand(CancelAsync);
    }

    [ObservableProperty] public partial string FilesText { get; private set; } = "";
    [ObservableProperty] public partial string BytesText { get; private set; } = "";
    [ObservableProperty] public partial string SpeedText { get; private set; } = "";
    [ObservableProperty] public partial string EtaText { get; private set; } = "";
    [ObservableProperty] public partial string CurrentText { get; private set; } = "";
    [ObservableProperty] public partial string PhaseText { get; private set; } = "";
    [ObservableProperty] public partial double Fraction { get; private set; }
    [ObservableProperty] public partial bool IsRunning { get; private set; }
    [ObservableProperty] public partial string? ErrorText { get; private set; }

    public IAsyncRelayCommand CancelCommand { get; }

    public static string PhaseName(CopyPhase p) => p switch
    {
        CopyPhase.CardCheck => "Checking the card",
        CopyPhase.Stat => "Checking the file",
        CopyPhase.CreateTemp => "Creating",
        CopyPhase.Copy => "Copying",
        CopyPhase.Flush => "Flushing",
        CopyPhase.Verify => "Verifying",
        CopyPhase.Finalize => "Finishing",
        CopyPhase.Rename => "Renaming",
        CopyPhase.Confirm => "Confirming",
        CopyPhase.Ledger => "Recording",
        _ => p.ToString(),
    };

    /// <summary>Runs the Commit through PreflightVm.StartAsync (CommitSession). A Cancel or a stop comes back as a CommitResult with its
    /// StopReason (the verdict still follows); null only when the run could not return a result at all.</summary>
    public async Task<CommitResult?> RunAsync()
    {
        IsRunning = true;
        ErrorText = null;
        try
        {
            var result = await _preflight.StartAsync(new UiProgress<OffloadProgress>(_ui, Apply), _cts.Token).ConfigureAwait(true);
            if (result.Offload.Stop == StopReason.LedgerWriteFailed && result.Offload.Outcomes.All(o => o is NotStarted))
                ErrorText = "Couldn't create or open the history file, so nothing was copied. The verdict below lists every file as not copied.";
            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnsafeIoException)
        {
            ErrorText = $"The offload stopped before it could finish: {ex.Message}";
            return null;
        }
        finally
        {
            IsRunning = false;
        }
    }

    internal void Apply(OffloadProgress p)
    {
        FilesText = string.Create(CultureInfo.InvariantCulture, $"{p.FilesDone} / {Fmt.Count(p.FilesTotal, "file", "files")}");
        BytesText = $"{Fmt.Size(p.BytesDone)} of {Fmt.Size(p.BytesTotal)}";
        SpeedText = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(p.MBps):0} MB/s");
        EtaText = p.Eta switch
        {
            null => "",
            { TotalMinutes: < 1 } => "under a minute left",
            { TotalHours: >= 1 } e => string.Create(CultureInfo.InvariantCulture, $"{(int)e.TotalHours} h {e.Minutes} min left"),
            { } e => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(e.TotalMinutes):0} min left"),
        };
        CurrentText = p.CurrentFile ?? "";
        PhaseText = PhaseName(p.Phase);
        Fraction = p.BytesTotal == 0 ? 0 : (double)p.BytesDone / p.BytesTotal;
    }

    public void Dispose() => _cts.Dispose();

    private async Task CancelAsync()
    {
        var answer = await _dialogs.ShowAsync(new DialogRequest("Stop the offload?",
            "The file being copied is discarded; everything already copied stays, and the card is not changed.", "Stop", null, "Keep going"))
            .ConfigureAwait(true);
        if (answer == DialogResult.Primary) await _cts.CancelAsync().ConfigureAwait(true);
    }
}
```

Faults inside the run are `CommitSession`'s business: a folder or writer that cannot be created, a card pulled, a full disk or a Cancel all come back as a `CommitResult` whose `Offload.Stop` says why (Part 07); the `catch` above only covers an exception the session itself lets escape.

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Preflight_*"` and `-- --filter-method "*Copy_*"`
Expected: PASS, 8 tests. They run the real `CommitSession` (Part 07) over the offload rig; an expectation that disagrees with Part 07 is a Part 07 defect against Ref §4.4/§10.2 unless the Ref says otherwise.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Commit tests/UasSort.Review.Tests/PreflightCopyTests.cs
git commit -m "feat: preflight sheet with acknowledgements and copy progress view models" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.20: Verdict page VM (wording, nothing preselected, per-day selection for photos and sets only, decisions and undo, eject)

**Files:**
- Create: `src/UasSort.Review/Commit/VerdictVm.cs`
- Test: `tests/UasSort.Review.Tests/VerdictVmTests.cs`

**Interfaces:**
- Consumes: `FormatVerdict`, `VerdictLevel`, `AuditCategory`, `OffloadResult` (`RunId`, `VolumesNeedingSafeRemoval`), `IDeviceEject.Eject` → `EjectResult(Ejected, EjectRefused)`, `ILedgerStore.OpenOwn`, `DecisionKind`, `IDialogService`, `IShellLauncher`, `Plan`; Part 07's `VerdictDecisions` (`NotCopied(FormatVerdict, Plan)`, `Day(IEnumerable<NotCopiedRow>, DateOnly)`, `Check(DecisionKind, IReadOnlyList<NotCopiedRow>)`, `Confirmation(DecisionKind, IReadOnlyList<NotCopiedRow>)`, `Records(DecisionKind, IReadOnlyList<NotCopiedRow>, Plan, string? runId, string machine, TimeProvider)`, `Revokes(IEnumerable<string>, string machine, TimeProvider)`), `NotCopiedRow`, `NotCopiedKind` (`UasSort.Core.Offload`; Review has no enum of its own).
- Produces (defined here):
  - `public sealed record VerdictPorts(ILedgerStore Ledger, string Machine, TimeProvider Time, IDialogService Dialogs, IShellLauncher Shell, IDeviceEject Eject, Func<FormatVerdict> Reaudit)` — `Reaudit` is bound in Part 11 to "reload the ledger, re-list the card, `CardAudit.Audit(...)`"
  - `public sealed partial class NotCopiedRowVm : ObservableObject` — `NotCopiedRowVm(NotCopiedRow row, string text, Action<NotCopiedRowVm> toggle)`: `Row`, `Unit`, `Kind` (Core `NotCopiedKind`), `Text`, `Detail`, `Bytes`, `SizeText`, `LocalDate`, `IsPhotoLike`, observable `IsSelected`, `IRelayCommand ToggleCommand`, `ToString()` = `Text`
  - `public sealed class NotCopiedDayVm(DateOnly date, string text, IRelayCommand selectDayCommand)` (`Date`, `Text`, `SelectDayCommand`)
  - `public sealed class EjectVm(string volume, IDeviceEject eject)` (`Volume`, `Text`, `IRelayCommand EjectCommand`, `string? ResultText`)
  - `public sealed class VerdictGroupRowVm(string text, string path, IRelayCommand openFolderCommand)`
  - `public sealed partial class VerdictVm : ObservableObject` — `VerdictVm(FormatVerdict verdict, Plan plan, OffloadResult? result, string? reportPath, VerdictPorts ports)`; observable `VerdictLevel Level`, `string Headline`, `string LevelText`, `IReadOnlyList<string> CategoryLines`, `IReadOnlyList<string> CardChanges`, `string? SafeRemovalNote`, `string? SelectionText`, `bool CanCleanup`, `string? CleanupTooltip`; `ObservableCollection<NotCopiedRowVm> NotCopied`, `ObservableCollection<NotCopiedDayVm> NotCopiedDays`, `IReadOnlyList<EjectVm> Ejects`, `IReadOnlyList<VerdictGroupRowVm> Groups`; commands `RecordImportedCommand`, `MarkNotNeededCommand`, `UndoCommand`, `OpenPhotoRootCommand`, `OpenReportCommand`, `CleanupCommand`, `DoneCommand`, `ShowPlanCommand`; events `CleanupRequested`, `DoneRequested`, `ShowPlanRequested`; `void SetCleanupAvailability(bool enabled, string? tooltip)`

Selection rules (Ref §10.5): photos and sets may be selected per tile or per day; a video (truncated or not) or an unknown file is selected alone — selecting one clears every other selection, and selecting a photo or set clears a selected video/unknown. [Record selected photos as already imported] needs a selection of photos/sets only. Nothing is ever preselected. The rows, the day selection, what may be decided together, the confirmation text and the `decision`/`revoke` records are Part 07's `VerdictDecisions`; the VM appends the records through `Ledger.OpenOwn()` and then calls `Reaudit()`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/VerdictVmTests.cs
namespace UasSort.Review.Tests;

public class VerdictVmTests
{
    private sealed class FakeEject : IDeviceEject
    {
        public EjectResult Eject(string volumeRoot) => new Ejected(volumeRoot);
    }

    private sealed class Rig
    {
        public FakeLedgerStore Ledger { get; } = Fake.Ledger();
        public FakeDialogService Dialogs { get; } = new();
        public FakeShellLauncher Shell { get; } = new();
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 28, 2, 10, 0, TimeSpan.Zero));
        public int Reaudits { get; private set; }
        public Plan Plan { get; }
        public FormatVerdict Verdict { get; }
        public ItemId Video { get; }
        public ItemId Photo25a { get; }
        public ItemId Photo25b { get; }
        public ItemId Photo26 { get; }
        public ItemId Unknown { get; } = new("DCIM/DJI_A001/x.MP4");

        public Rig()
        {
            var p1 = PhotosOtherTabTests.Photo("DJI_20260725200000_0101_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 0), new ProbablyImported("x"), 200_000_000);
            var p2 = PhotosOtherTabTests.Photo("DJI_20260725200100_0102_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 1), new ProbablyImported("x"), 200_000_000);
            var p3 = PhotosOtherTabTests.Photo("DJI_20260726200000_0103_D.DNG", TestPlans.Utc(2026, 7, 27, 4, 0), new ProbablyImported("x"), 200_000_000);
            var t = TestPlans.Utc(2026, 7, 26, 3, 0);
            var b = TestPlans.Base(TestPlans.CouncilAnvil(), extraItems: [p1, p2, p3],
                                   extraEntries: [new CardEntry(Unknown.CardRelPath, 5_000_000, t, t, t, 0x20, EntryClass.Unknown, null)]);
            Plan = new ScriptedDeriver().Derive(b, new Tuning(), [], new SessionFlags(false), 1, TestContext.Current.CancellationToken);
            Video = TestPlans.Id(TestPlans.CouncilAnvil()[3].Name);
            (Photo25a, Photo25b, Photo26) = (p1.Raw.Unit.Id, p2.Raw.Unit.Id, p3.Raw.Unit.Id);
            UnitAudit U(ItemId id, AuditCategory c, long size, string detail) => new(id, c, [new AuditLine(id.CardRelPath, size, c, detail)]);
            Verdict = new FormatVerdict(VerdictLevel.NotSafe, TestPlans.Card, "E: · DJI Air 3S · serial 1A2B-3C4D: Don't format yet: 1 file failed",
                ImmutableDictionary<AuditCategory, int>.Empty.Add(AuditCategory.VerifiedThisRun, 3).Add(AuditCategory.AssumedByRule, 3).Add(AuditCategory.Unaccounted, 2),
                0, 0,
                [
                    U(TestPlans.Id(TestPlans.CouncilAnvil()[0].Name), AuditCategory.VerifiedThisRun, 1_200_000_000, "copied"),
                    U(Video, AuditCategory.Unaccounted, 1_200_000_000, "failed: verify"),
                    U(Photo25a, AuditCategory.AssumedByRule, 200_000_000, "probably imported"),
                    U(Photo25b, AuditCategory.AssumedByRule, 200_000_000, "probably imported"),
                    U(Photo26, AuditCategory.AssumedByRule, 200_000_000, "probably imported"),
                    U(Unknown, AuditCategory.Unaccounted, 5_000_000, "unknown file"),
                ], [], null);
        }

        public VerdictVm Vm(OffloadResult? result = null)
            => new(Verdict, Plan, result, @"C:\AppData\uas-sort\reports\20260928-020500-run12345.json",
                   new VerdictPorts(Ledger, "PC1", Time, Dialogs, Shell, new FakeEject(), () => { Reaudits++; return Verdict; }));
    }

    [Fact]
    public void Verdict_WordingAndNothingPreselected()
    {
        var vm = new Rig().Vm();
        Assert.Equal("Don't format yet", vm.LevelText);
        Assert.Equal("E: · DJI Air 3S · serial 1A2B-3C4D: Don't format yet: 1 file failed", vm.Headline);
        Assert.Equal<string>(["Verified this run: 3", "Assumed imported: 3", "Not accounted for: 2"], vm.CategoryLines);
        Assert.Equal(5, vm.NotCopied.Count);
        Assert.All(vm.NotCopied, r => Assert.False(r.IsSelected));
        Assert.False(vm.RecordImportedCommand.CanExecute(null));
        Assert.False(vm.MarkNotNeededCommand.CanExecute(null));
        Assert.Equal(2, vm.NotCopiedDays.Count);
    }

    [Fact]
    public void Verdict_PerDaySelectionCoversPhotosOnly_VideosAndUnknownOneAtATime()
    {
        var rig = new Rig();
        var vm = rig.Vm();
        Row(vm, rig.Video).ToggleCommand.Execute(null);

        vm.NotCopiedDays.Single(d => d.Date == new DateOnly(2026, 7, 25)).SelectDayCommand.Execute(null);
        Assert.Equal<ItemId>([rig.Photo25a, rig.Photo25b], vm.NotCopied.Where(r => r.IsSelected).Select(r => r.Unit));
        Assert.True(vm.RecordImportedCommand.CanExecute(null));

        Row(vm, rig.Unknown).ToggleCommand.Execute(null);
        Assert.Equal<ItemId>([rig.Unknown], vm.NotCopied.Where(r => r.IsSelected).Select(r => r.Unit));
        Assert.Equal(NotCopiedKind.Unknown, Row(vm, rig.Unknown).Kind);
        Assert.False(vm.RecordImportedCommand.CanExecute(null));
        Assert.True(vm.MarkNotNeededCommand.CanExecute(null));

        Row(vm, rig.Video).ToggleCommand.Execute(null);
        Assert.Equal<ItemId>([rig.Video], vm.NotCopied.Where(r => r.IsSelected).Select(r => r.Unit));
        Assert.Equal("1 video · 1.2 GB", vm.SelectionText);
    }

    [Fact]
    public async Task Verdict_RecordAsImported_ConfirmsWithCoreText_WritesDecisions_UndoRevokes()
    {
        var rig = new Rig();
        var vm = rig.Vm();
        vm.NotCopiedDays.Single(d => d.Date == new DateOnly(2026, 7, 25)).SelectDayCommand.Execute(null);
        var selected = vm.NotCopied.Where(r => r.IsSelected).Select(r => r.Row).ToList();

        await vm.RecordImportedCommand.ExecuteAsync(null);

        var dialog = Assert.Single(rig.Dialogs.Shown);
        Assert.Equal("Record as already imported?", dialog.Title);
        Assert.StartsWith(VerdictDecisions.Confirmation(DecisionKind.AssumedImported, selected), dialog.Body, StringComparison.Ordinal);
        var made = rig.Ledger.Writer.Records.OfType<DecisionRecord>().ToList();
        Assert.Equal(2, made.Count(d => d.Kind == "assumedImported"));
        Assert.All(made, d => Assert.Equal(("PC1", rig.Time.GetUtcNow().UtcDateTime), (d.Machine, d.At)));
        Assert.Equal(1, rig.Reaudits);
        Assert.True(vm.UndoCommand.CanExecute(null));

        await vm.UndoCommand.ExecuteAsync(null);
        var revokes = rig.Ledger.Writer.Records.OfType<RevokeRecord>().ToList();
        Assert.Equal(made.Select(d => d.Id).Order(StringComparer.Ordinal), revokes.Select(r => r.Decision).Order(StringComparer.Ordinal));
        Assert.Equal(2, rig.Reaudits);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Verdict_MarkOneVideoNotNeeded_WritesDismissed()
    {
        var rig = new Rig();
        var vm = rig.Vm();
        Row(vm, rig.Video).ToggleCommand.Execute(null);
        rig.Dialogs.Answers.Enqueue(DialogResult.Primary);

        await vm.MarkNotNeededCommand.ExecuteAsync(null);

        Assert.Equal("Mark as not needed?", rig.Dialogs.Shown[0].Title);
        var d = Assert.Single(rig.Ledger.Writer.Records.OfType<DecisionRecord>());
        Assert.Equal("dismissed", d.Kind);
        Assert.Equal(rig.Video.CardRelPath, d.Src);
    }

    [Fact]
    public void Verdict_SafeRemovalAndEject_GroupsAndCleanupAvailability()
    {
        var rig = new Rig();
        var result = new OffloadResult("run12345", [], null, TestPlans.Utc(2026, 9, 28, 2, 0), TestPlans.Utc(2026, 9, 28, 2, 5), [@"D:\"]);
        var vm = rig.Vm(result);

        Assert.Equal("Safely remove D: before formatting the card", vm.SafeRemovalNote);
        var eject = Assert.Single(vm.Ejects);
        eject.EjectCommand.Execute(null);
        Assert.Equal("Ejected D:", eject.ResultText);

        vm.OpenReportCommand.Execute(null);
        Assert.Contains(rig.Shell.Opened, o => o.EndsWith("run12345.json", StringComparison.Ordinal));
        Assert.False(vm.CleanupCommand.CanExecute(null));
        vm.SetCleanupAvailability(true, null);
        Assert.True(vm.CleanupCommand.CanExecute(null));
    }

    private static NotCopiedRowVm Row(VerdictVm vm, ItemId id) => vm.NotCopied.Single(r => r.Unit == id);
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Verdict_*"`
Expected: build FAILS (CS0246 `VerdictVm`, `VerdictPorts`, `NotCopiedRowVm`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Commit/VerdictVm.cs
namespace UasSort.Review;

/// <summary>What the Verdict page writes to and reads from; Part 11 binds Reaudit to "reload the ledger, re-list, CardAudit.Audit".</summary>
public sealed record VerdictPorts(ILedgerStore Ledger, string Machine, TimeProvider Time, IDialogService Dialogs, IShellLauncher Shell,
                                  IDeviceEject Eject, Func<FormatVerdict> Reaudit);

/// <summary>One "Not copied" row: Part 07's NotCopiedRow plus the display name.</summary>
public sealed partial class NotCopiedRowVm : ObservableObject
{
    public NotCopiedRowVm(NotCopiedRow row, string text, Action<NotCopiedRowVm> toggle)
    {
        Row = row;
        Text = text;
        ToggleCommand = new RelayCommand(() => toggle(this));
    }

    public NotCopiedRow Row { get; }
    public ItemId Unit => Row.Unit;
    public NotCopiedKind Kind => Row.Kind;
    public string Text { get; }
    public string Detail => Row.Detail;
    public long Bytes => Row.Bytes;
    public string SizeText => Fmt.Size(Bytes);
    public DateOnly? LocalDate => Row.LocalDate;
    public bool IsPhotoLike => Kind is NotCopiedKind.Photo or NotCopiedKind.Set;
    [ObservableProperty] public partial bool IsSelected { get; set; }
    public IRelayCommand ToggleCommand { get; }
    public override string ToString() => Text;
}

public sealed class NotCopiedDayVm(DateOnly date, string text, IRelayCommand selectDayCommand)
{
    public DateOnly Date { get; } = date;
    public string Text { get; } = text;
    public IRelayCommand SelectDayCommand { get; } = selectDayCommand;
    public override string ToString() => Text;
}

public sealed class EjectVm
{
    public EjectVm(string volume, IDeviceEject eject)
    {
        Volume = volume;
        Text = $"Eject {Fmt.Drive(volume)}";
        EjectCommand = new RelayCommand(() => ResultText = eject.Eject(volume) switch
        {
            Ejected e => $"Ejected {Fmt.Drive(e.Volume)}",
            EjectRefused r => $"Couldn't eject {Fmt.Drive(r.Volume)}: {r.Reason}",
        });
    }

    public string Volume { get; }
    public string Text { get; }
    public IRelayCommand EjectCommand { get; }
    public string? ResultText { get; private set; }
    public override string ToString() => Text;
}

public sealed class VerdictGroupRowVm(string text, string path, IRelayCommand openFolderCommand)
{
    public string Text { get; } = text;
    public string Path { get; } = path;
    public IRelayCommand OpenFolderCommand { get; } = openFolderCommand;
    public override string ToString() => Text;
}

/// <summary>The Verdict page (Ref §10.5).</summary>
public sealed partial class VerdictVm : ObservableObject
{
    private readonly Plan _plan;
    private readonly VerdictPorts _ports;
    private readonly string? _reportPath;
    private readonly string? _runId;
    private ImmutableArray<string> _lastDecisionIds = [];
    private bool _cleanupEnabled;

    public VerdictVm(FormatVerdict verdict, Plan plan, OffloadResult? result, string? reportPath, VerdictPorts ports)
    {
        _plan = plan;
        _ports = ports;
        _reportPath = reportPath;
        _runId = result?.RunId;
        RecordImportedCommand = new AsyncRelayCommand(() => DecideAsync(DecisionKind.AssumedImported), () => CanDecide(DecisionKind.AssumedImported));
        MarkNotNeededCommand = new AsyncRelayCommand(() => DecideAsync(DecisionKind.Dismissed), () => CanDecide(DecisionKind.Dismissed));
        UndoCommand = new AsyncRelayCommand(UndoAsync, () => _lastDecisionIds.Length > 0);
        OpenPhotoRootCommand = new RelayCommand(() => _ports.Shell.OpenFolder(plan.Base.Scan.Settings.PhotoRoot));
        OpenReportCommand = new RelayCommand(() => _ports.Shell.OpenFile(_reportPath!), () => _reportPath is not null);
        CleanupCommand = new RelayCommand(() => CleanupRequested?.Invoke(), () => _cleanupEnabled);
        DoneCommand = new RelayCommand(() => DoneRequested?.Invoke());
        ShowPlanCommand = new RelayCommand(() => ShowPlanRequested?.Invoke());
        Ejects = [.. (result?.VolumesNeedingSafeRemoval ?? []).Select(v => new EjectVm(v, ports.Eject))];
        SafeRemovalNote = Ejects.Count == 0 ? null
            : $"Safely remove {string.Join(", ", Ejects.Select(e => Fmt.Drive(e.Volume)))} before formatting the card";
        var root = plan.Base.Scan.Settings.VideoRoot;
        Groups = [.. plan.Groups.Select(g => g.Target switch
            {
                NewFolder n => Path.Join(root, n.RelPath),
                Append a => a.Folder.FullPath,
                AlreadyImported ai => ai.Folder.FullPath,
                _ => null,
            }).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => new VerdictGroupRowVm(System.IO.Path.GetFileName(p), p, new RelayCommand(() => _ports.Shell.OpenFolder(p))))];
        ApplyVerdict(verdict);
    }

    [ObservableProperty] public partial VerdictLevel Level { get; private set; }
    [ObservableProperty] public partial string Headline { get; private set; } = "";
    [ObservableProperty] public partial string LevelText { get; private set; } = "";
    [ObservableProperty] public partial IReadOnlyList<string> CategoryLines { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> CardChanges { get; private set; } = [];
    [ObservableProperty] public partial string? SafeRemovalNote { get; private set; }
    [ObservableProperty] public partial string? SelectionText { get; private set; }
    [ObservableProperty] public partial bool CanCleanup { get; private set; }
    [ObservableProperty] public partial string? CleanupTooltip { get; private set; }

    public ObservableCollection<NotCopiedRowVm> NotCopied { get; } = [];
    public ObservableCollection<NotCopiedDayVm> NotCopiedDays { get; } = [];
    public IReadOnlyList<EjectVm> Ejects { get; }
    public IReadOnlyList<VerdictGroupRowVm> Groups { get; }

    public IAsyncRelayCommand RecordImportedCommand { get; }
    public IAsyncRelayCommand MarkNotNeededCommand { get; }
    public IAsyncRelayCommand UndoCommand { get; }
    public IRelayCommand OpenPhotoRootCommand { get; }
    public IRelayCommand OpenReportCommand { get; }
    public IRelayCommand CleanupCommand { get; }
    public IRelayCommand DoneCommand { get; }
    public IRelayCommand ShowPlanCommand { get; }

    public event Action? CleanupRequested;
    public event Action? DoneRequested;
    public event Action? ShowPlanRequested;

    public static string LevelName(VerdictLevel l) => l switch
    {
        VerdictLevel.Safe => "Safe to format",
        VerdictLevel.SafeWithAssumptions => "Safe, with assumptions",
        VerdictLevel.NotSafe => "Don't format yet",
        _ => l.ToString(),
    };

    public static string CategoryName(AuditCategory c) => c switch
    {
        AuditCategory.VerifiedThisRun => "Verified this run",
        AuditCategory.InLedger => "In the history, verified",
        AuditCategory.ConfirmedByYou => "Confirmed by you",
        AuditCategory.NameSizeMatch => "Matched by name and size",
        AuditCategory.SkippedByRule => "Skipped by rule",
        AuditCategory.AssumedByRule => "Assumed imported",
        AuditCategory.Unaccounted => "Not accounted for",
        _ => c.ToString(),
    };

    public void SetCleanupAvailability(bool enabled, string? tooltip)
    {
        _cleanupEnabled = enabled;
        CanCleanup = enabled;
        CleanupTooltip = tooltip;
        CleanupCommand.NotifyCanExecuteChanged();
    }

    private void ApplyVerdict(FormatVerdict v)
    {
        Level = v.Level;
        Headline = v.Headline;
        LevelText = LevelName(v.Level);
        CategoryLines = [.. Enum.GetValues<AuditCategory>().Where(c => v.Counts.GetValueOrDefault(c) > 0)
                                .Select(c => string.Create(CultureInfo.InvariantCulture, $"{CategoryName(c)}: {v.Counts[c]}"))];
        CardChanges = v.CardChanges;

        var items = _plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        NotCopied.Clear();
        foreach (var row in VerdictDecisions.NotCopied(v, _plan))
            NotCopied.Add(new NotCopiedRowVm(row, items.TryGetValue(row.Unit, out var item) ? item.Raw.Name : row.Unit.CardRelPath, Toggle));
        NotCopiedDays.Clear();
        foreach (var day in NotCopied.Where(r => r.IsPhotoLike && r.LocalDate is not null).GroupBy(r => r.LocalDate!.Value).OrderBy(g => g.Key))
        {
            var d = day.Key;
            NotCopiedDays.Add(new NotCopiedDayVm(d, $"{Fmt.DayWithWeekday(d)} · {Fmt.Count(day.Count(), "item", "items")}",
                                                 new RelayCommand(() => SelectDay(d))));
        }
        SelectionChanged();
    }

    private void Toggle(NotCopiedRowVm row)
    {
        var select = !row.IsSelected;
        if (select)
        {
            foreach (var other in NotCopied.Where(r => r != row && r.IsSelected && (!row.IsPhotoLike || !r.IsPhotoLike)))
                other.IsSelected = false;
        }
        row.IsSelected = select;
        SelectionChanged();
    }

    /// <summary>Per-day selection covers exactly VerdictDecisions.Day (photos and sets of that local day) and clears a selected
    /// video or unknown file.</summary>
    private void SelectDay(DateOnly day)
    {
        var dayUnits = VerdictDecisions.Day(NotCopied.Select(r => r.Row), day).Select(r => r.Unit).ToHashSet();
        foreach (var r in NotCopied)
            r.IsSelected = r.IsPhotoLike && (r.IsSelected || dayUnits.Contains(r.Unit));
        SelectionChanged();
    }

    private List<NotCopiedRowVm> Selected() => [.. NotCopied.Where(r => r.IsSelected)];

    private bool CanDecide(DecisionKind kind) => VerdictDecisions.Check(kind, [.. Selected().Select(r => r.Row)]).Ok;

    private void SelectionChanged()
    {
        var s = Selected();
        SelectionText = s.Count == 0 ? null : $"{Describe(s)} · {Fmt.Size(s.Sum(r => r.Bytes))}";
        RecordImportedCommand.NotifyCanExecuteChanged();
        MarkNotNeededCommand.NotifyCanExecuteChanged();
    }

    private static string Describe(IReadOnlyList<NotCopiedRowVm> rows)
    {
        var parts = new List<string>();
        void Add(NotCopiedKind k, string one, string many)
        {
            var n = rows.Count(r => r.Kind == k);
            if (n > 0) parts.Add(Fmt.Count(n, one, many));
        }
        Add(NotCopiedKind.Video, "video", "videos");
        Add(NotCopiedKind.Photo, "photo", "photos");
        Add(NotCopiedKind.Set, "set", "sets");
        Add(NotCopiedKind.Unknown, "unknown file", "unknown files");
        return string.Join(", ", parts);
    }

    /// <summary>Ref §10.5: confirm with Core's text, write one decision record per file through the own ledger file, re-audit.</summary>
    private async Task DecideAsync(DecisionKind kind)
    {
        List<NotCopiedRow> rows = [.. Selected().Select(r => r.Row)];
        if (!VerdictDecisions.Check(kind, rows).Ok) return;
        var title = kind == DecisionKind.AssumedImported ? "Record as already imported?" : "Mark as not needed?";
        var body = VerdictDecisions.Confirmation(kind, rows) + " uas-sort records this in the history; you can undo it.";
        var answer = await _ports.Dialogs.ShowAsync(new DialogRequest(title, body, kind == DecisionKind.AssumedImported ? "Record" : "Mark", null, "Cancel"))
                                         .ConfigureAwait(true);
        if (answer != DialogResult.Primary) return;
        var records = VerdictDecisions.Records(kind, rows, _plan, _runId, _ports.Machine, _ports.Time);
        using (var writer = _ports.Ledger.OpenOwn())
        {
            foreach (var r in records) writer.Append(r);
        }
        _lastDecisionIds = [.. records.Select(r => r.Id)];
        UndoCommand.NotifyCanExecuteChanged();
        ApplyVerdict(_ports.Reaudit());
    }

    /// <summary>Undo writes one revoke per decision just made (VerdictDecisions.Revokes), then re-audits.</summary>
    private Task UndoAsync()
    {
        using (var writer = _ports.Ledger.OpenOwn())
        {
            foreach (var r in VerdictDecisions.Revokes(_lastDecisionIds, _ports.Machine, _ports.Time)) writer.Append(r);
        }
        _lastDecisionIds = [];
        UndoCommand.NotifyCanExecuteChanged();
        ApplyVerdict(_ports.Reaudit());
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Verdict_*"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Commit/VerdictVm.cs tests/UasSort.Review.Tests/VerdictVmTests.cs
git commit -m "feat: verdict page view model with per-day photo selection and decisions" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.21: Card cleanup — availability, review row and result VMs

**Files:**
- Create: `src/UasSort.Review/Cleanup/CleanupAvailability.cs`, `src/UasSort.Review/Cleanup/CleanupRowVm.cs`, `src/UasSort.Review/Cleanup/CleanupResultVm.cs`
- Test: `tests/UasSort.Review.Tests/CleanupPiecesTests.cs`

**Interfaces:**
- Consumes: `CardSource` (`IsBrowsedFolder`, `IsWriteProtected`), `CleanupCandidate`, `CleanupEligibility`, `NotInLibraryReason`, `ItemKind`, `CleanupResult`, `CleanupOutcome` cases, `CleanupStop`, `CardSpace`, `VerdictLevel`, `IDeviceEject`, `EjectVm`, `VerdictVm.LevelName`, `Fmt`, Core `ZoneNames.Abbreviation`, `CleanupTexts.VolumeName` (Part 08); tests use `UasSort.Testing.CleanupPlanFixtures.Confirmed(...)` (Part 08) for the result's plan.
- Produces (defined here):
  - `public sealed record CleanupContext(bool CommitRunning, bool ScanRunning, CardSource? Source, bool CardPresent, string? VolumeRefusal)`
  - `public static class CleanupAvailability` — `(bool Enabled, string? Tooltip) For(CleanupContext c)` (Ref §10.6 availability table, in its row order)
  - `public enum RowDecision { Undecided, Keep, Delete }`
  - `public sealed partial class CleanupRowVm : ObservableObject, IKeyed` — `CleanupRowVm(CleanupCandidate c, Action<CleanupRowVm, RowDecision> set)`, `Candidate`, `ItemId Unit`, `ItemId ThumbKey`, `DateText`, `LengthText`, `LocationText`, `SizeText`, `ReasonText`, observable `string? Badge`, `RowDecision Decision`, `string Text`; `IRelayCommand KeepCommand`, `IRelayCommand DeleteCommand`; `internal void Update(CleanupCandidate c, RowDecision decision, bool undecidedNewRow)`; `ToString()` = `Text`
  - `public sealed class CleanupResultVm` — `CleanupResultVm(CleanupResult r, VerdictLevel after, string reportPath, string cardRoot, IDeviceEject eject)`, `HeadlineText`, `IReadOnlyList<string> Problems`, `IReadOnlyList<string> StillListed`, `string VerdictText`, `string? StopText`, `string SafeRemovalText`, `EjectVm Eject`, `string ReportPath`; `static (int Files, long Bytes) DeletedTotals(CleanupResult r)`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/CleanupPiecesTests.cs
namespace UasSort.Review.Tests;

public class CleanupPiecesTests
{
    [Theory]
    [InlineData(true, false, false, false, true, null, false, "Wait until the offload finishes")]
    [InlineData(false, true, false, false, true, null, false, "Wait until the scan finishes")]
    [InlineData(false, false, true, false, true, null, false, "The card is write-protected (lock switch)")]
    [InlineData(false, false, false, true, true, null, false, "Cleanup works only on a detected card. A browsed folder could be a backup copy.")]
    [InlineData(false, false, false, false, true, "not removable media", false, "This doesn't look like a drone card (it may be a backup drive)")]
    [InlineData(false, false, false, false, false, null, false, "Rescan first")]
    [InlineData(false, false, false, false, true, null, true, null)]
    public void CleanupAvailability_FollowsTheTable(bool commit, bool scan, bool writeProtected, bool browsed, bool present, string? refusal,
                                                    bool enabled, string? tooltip)
    {
        var source = new CardSource(@"E:\", TestPlans.Card, browsed, writeProtected);
        Assert.Equal((enabled, tooltip), CleanupAvailability.For(new CleanupContext(commit, scan, source, present, refusal)));
    }

    [Fact]
    public void CleanupAvailability_NoCardScanned_RescanFirst()
        => Assert.Equal((false, "Rescan first"), CleanupAvailability.For(new CleanupContext(false, false, null, false, null)));

    [Fact]
    public void CleanupRow_TextFieldsAndToggles()
    {
        var c = CleanupFixture.Candidate("DCIM/DJI_001/DJI_20260726202000_0014_D.MP4", ItemKind.Video, 7_000_000, null,
                                         new DateTime(2026, 7, 26, 20, 20, 0), TestPlans.Utc(2026, 7, 27, 4, 20), NotInLibraryReason.Unfinished,
                                         "unfinished recording; the drone may still repair it", place: "near Anvil Mountain · 0.2 mi", ticked: true);
        var set = new List<RowDecision>();
        var row = new CleanupRowVm(c, (_, d) => set.Add(d));
        row.Update(c, RowDecision.Keep, undecidedNewRow: false);

        Assert.Equal("Jul 26 20:20 AKDT", row.DateText);
        Assert.Equal("unfinished · ~7 MB", row.LengthText);
        Assert.Equal("near Anvil Mountain · 0.2 mi", row.LocationText);
        Assert.Equal("ticked for offload", row.Badge);
        Assert.Equal("Jul 26 20:20 AKDT · unfinished · ~7 MB · near Anvil Mountain · 0.2 mi · 7 MB · unfinished recording; the drone may still repair it · ticked for offload", row.ToString());
        row.DeleteCommand.Execute(null);
        Assert.Equal<RowDecision>([RowDecision.Delete], set);

        var noGps = CleanupFixture.Candidate("DCIM/DJI_001/DJI_20260720190000_0050_D.MP4", ItemKind.Video, 1_200_000_000, TimeSpan.FromSeconds(222),
                                             new DateTime(2026, 7, 20, 19, 0, 0), TestPlans.Utc(2026, 7, 21, 3, 0), NotInLibraryReason.New, "new: not in your library",
                                             location: new GeoPoint(64.5627, -165.3709));
        var r2 = new CleanupRowVm(noGps, (_, _) => { });
        r2.Update(noGps, RowDecision.Undecided, undecidedNewRow: true);
        Assert.Equal("3:42", r2.LengthText);
        Assert.Equal("64.5627, -165.3709", r2.LocationText);
        Assert.Equal("new in range", r2.Badge);
    }

    [Fact]
    public void CleanupResult_TotalsProblemsAndStillListed()
    {
        var u1 = new ItemId("DCIM/DJI_001/a.MP4");
        var u2 = new ItemId("DCIM/DJI_001/b.MP4");
        var u3 = new ItemId("DCIM/PANORAMA/001_0087");
        var result = CleanupFixture.Result(
            [new Deleted(u1, 2, 1_300_000_000, false), new SkippedChanged(u2, u2.CardRelPath, 5, TestPlans.Utc(2026, 9, 28, 1, 0)),
             new PartiallyDeleted(u3, ["DCIM/PANORAMA/001_0087/PANO_0001.JPG"], ["DCIM/PANORAMA/001_0087/PANO_0002.JPG"], "access denied")],
            stop: null, freeAfter: 73_800_000_000, stillListed: ["DCIM/DJI_001/a.LRF"]);
        var vm = new CleanupResultVm(result, VerdictLevel.Safe, @"C:\r.json", @"E:\", new FakeEjectOk());

        Assert.Equal("Deleted 3 files (1.3 GB) · E: now has 73.8 GB free", vm.HeadlineText);
        Assert.Equal(2, vm.Problems.Count);
        Assert.Contains(vm.Problems, p => p.StartsWith("b.MP4: skipped, it changed since the scan", StringComparison.Ordinal));
        Assert.Contains(vm.Problems, p => p.Contains("still on the card: PANO_0002.JPG", StringComparison.Ordinal));
        Assert.Equal("still on the card: DCIM/DJI_001/a.LRF (another program held it open)", Assert.Single(vm.StillListed));
        Assert.Equal("Safe to format", vm.VerdictText);
        Assert.StartsWith("Safely remove the card before putting it back in the drone.", vm.SafeRemovalText, StringComparison.Ordinal);
        Assert.Equal("Eject E:", vm.Eject.Text);
    }
}

internal sealed class FakeEjectOk : IDeviceEject
{
    public List<string> Ejected { get; } = [];
    public EjectResult Eject(string volumeRoot) { Ejected.Add(volumeRoot); return new Ejected(volumeRoot); }
}
```

`CleanupFixture.Candidate` and `CleanupFixture.Result` are created in Task 10.22 Step 1 together with the full fixture; for this task add the file now with just these two helpers:

```csharp
// tests/UasSort.Review.Tests/Fixtures/CleanupFixture.cs  (Task 10.21 part; Task 10.22 appends the inputs builder)
namespace UasSort.Review.Tests;

internal static partial class CleanupFixture
{
    public static CleanupCandidate Candidate(string relPath, ItemKind kind, long size, TimeSpan? duration, DateTime localTime, DateTime captureUtc,
                                             NotInLibraryReason? reason, string reasonText, string? place = null, GeoPoint? location = null,
                                             bool ticked = false)
    {
        var entry = new CardEntry(relPath, size, captureUtc, captureUtc, captureUtc, 0x20, kind == ItemKind.Video ? EntryClass.Video : EntryClass.Photo, null);
        return new CleanupCandidate(new ItemId(relPath), [entry], size, captureUtc, DateOnly.FromDateTime(localTime), TestPlans.Anchorage,
            reason is null ? CleanupEligibility.Evidence : CleanupEligibility.NotInLibrary, AuditCategory.Unaccounted, reasonText, duration,
            location, place, kind, localTime, reason, null, null, [], null, ticked, []);
    }

    /// <summary>A CleanupResult over a real ConfirmedCleanupPlan from Part 08's CleanupPlanFixtures (built through Core's internal
    /// constructors), covering the files the outcomes name.</summary>
    public static CleanupResult Result(ImmutableArray<CleanupOutcome> outcomes, CleanupStop? stop, long freeAfter, ImmutableArray<string> stillListed)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero));
        string[] files = [.. outcomes.Select(o => o.Unit.CardRelPath).Where(p => p.EndsWith(".MP4", StringComparison.OrdinalIgnoreCase))];
        string[] setFolders = [.. outcomes.Select(o => o.Unit.CardRelPath).Where(p => !p.EndsWith(".MP4", StringComparison.OrdinalIgnoreCase))];
        var confirmed = CleanupPlanFixtures.Confirmed(@"E:\", TestPlans.Card, files, setFolders, clock);
        return new("cleanup01", confirmed, outcomes, stop, new CardSpace(freeAfter, 256_060_514_304, 131_072), stillListed,
                   TestPlans.Utc(2026, 9, 28, 3, 0), TestPlans.Utc(2026, 9, 28, 3, 2));
    }
}
```

(`CleanupResultVm` never reads `CleanupResult.Plan`; the fixture still gives it a real `ConfirmedCleanupPlan`, because only Core can build one and `CleanupPlanFixtures.Confirmed` is the shared test path to it.)

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*CleanupAvailability*"`
Expected: build FAILS (CS0103 `CleanupAvailability`; CS0246 `CleanupRowVm`, `CleanupResultVm`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Cleanup/CleanupAvailability.cs
namespace UasSort.Review;

public sealed record CleanupContext(bool CommitRunning, bool ScanRunning, CardSource? Source, bool CardPresent, string? VolumeRefusal);

/// <summary>When [Clean up card…] is enabled, and the tooltip when it isn't (Ref §10.6 table, first matching row).</summary>
public static class CleanupAvailability
{
    public static (bool Enabled, string? Tooltip) For(CleanupContext c)
    {
        if (c.CommitRunning) return (false, "Wait until the offload finishes");
        if (c.ScanRunning) return (false, "Wait until the scan finishes");
        if (c.Source is not { } s || !c.CardPresent) return (false, "Rescan first");
        if (s.IsWriteProtected) return (false, "The card is write-protected (lock switch)");
        if (s.IsBrowsedFolder) return (false, "Cleanup works only on a detected card. A browsed folder could be a backup copy.");
        if (c.VolumeRefusal is not null) return (false, "This doesn't look like a drone card (it may be a backup drive)");
        return (true, null);
    }
}
```

```csharp
// src/UasSort.Review/Cleanup/CleanupRowVm.cs
namespace UasSort.Review;

public enum RowDecision { Undecided, Keep, Delete }

/// <summary>One not-in-library review row (Ref §10.6 "The not-in-library switch and the review list").</summary>
public sealed partial class CleanupRowVm : ObservableObject, IKeyed
{
    public CleanupRowVm(CleanupCandidate c, Action<CleanupRowVm, RowDecision> set)
    {
        Candidate = c;
        KeepCommand = new RelayCommand(() => set(this, RowDecision.Keep));
        DeleteCommand = new RelayCommand(() => set(this, RowDecision.Delete));
    }

    public CleanupCandidate Candidate { get; private set; }
    public ItemId Unit => Candidate.Unit;
    public ItemId ThumbKey => Candidate.Unit;
    public string Key => Candidate.Unit.CardRelPath;

    [ObservableProperty] public partial string DateText { get; private set; } = "";
    [ObservableProperty] public partial string LengthText { get; private set; } = "";
    [ObservableProperty] public partial string LocationText { get; private set; } = "";
    [ObservableProperty] public partial string SizeText { get; private set; } = "";
    [ObservableProperty] public partial string ReasonText { get; private set; } = "";
    [ObservableProperty] public partial string? Badge { get; private set; }
    [ObservableProperty] public partial RowDecision Decision { get; private set; }
    [ObservableProperty] public partial string Text { get; private set; } = "";

    public IRelayCommand KeepCommand { get; }
    public IRelayCommand DeleteCommand { get; }

    internal void Update(CleanupCandidate c, RowDecision decision, bool undecidedNewRow)
    {
        Candidate = c;
        var bytes = c.Files.Sum(f => f.Size);
        DateText = $"{Fmt.Day(c.LocalDate)} {Fmt.Clock(c.LocalTime)} {ZoneNames.Abbreviation(c.TzId, c.CaptureUtc)}";
        LengthText = c.Kind switch
        {
            ItemKind.Video when c.NotInLibrary == NotInLibraryReason.Unfinished || c.Duration is null => $"unfinished · ~{Fmt.Size(bytes)}",
            ItemKind.Video => Fmt.ClipLength(c.Duration!.Value),
            ItemKind.Photo => "photo",
            ItemKind.Set => ((c.SetFolder ?? c.Unit.CardRelPath).Contains("HYPERLAPSE", StringComparison.OrdinalIgnoreCase) ? "hyperlapse" : "panorama")
                            + " · " + Fmt.Count(c.Files.Length, "frame", "frames"),
            _ => "",
        };
        LocationText = c.PlaceLabel
                       ?? (c.Location is { } p ? string.Create(CultureInfo.InvariantCulture, $"{p.Lat:0.0000}, {p.Lon:0.0000}") : "no GPS");
        SizeText = Fmt.Size(bytes);
        ReasonText = c.Reason;
        Badge = c.TickedForOffload ? "ticked for offload" : undecidedNewRow ? "new in range" : null;
        Decision = decision;
        Text = string.Join(" · ", new[] { DateText, LengthText, LocationText, SizeText, ReasonText, Badge }.Where(s => !string.IsNullOrEmpty(s)));
    }

    public override string ToString() => Text;
}
```

```csharp
// src/UasSort.Review/Cleanup/CleanupResultVm.cs
namespace UasSort.Review;

/// <summary>The cleanup result page (Ref §10.6 "After the loop" step 5).</summary>
public sealed class CleanupResultVm
{
    public CleanupResultVm(CleanupResult r, VerdictLevel after, string reportPath, string cardRoot, IDeviceEject eject)
    {
        var (files, bytes) = DeletedTotals(r);
        var drive = CleanupTexts.VolumeName(cardRoot);
        HeadlineText = $"Deleted {Fmt.Count(files, "file", "files")} ({Fmt.Size(bytes)}) · {drive} now has {Fmt.Size(r.SpaceAfter.FreeBytes)} free";
        Problems = [.. r.Outcomes.Select(Problem).OfType<string>()];
        StillListed = [.. r.StillListed.Select(p => $"still on the card: {p} (another program held it open)")];
        VerdictText = VerdictVm.LevelName(after);
        StopText = r.Stop switch
        {
            null => null,
            CleanupStop.OffloadLockHeld => "Another uas-sort window is offloading; nothing was deleted.",
            CleanupStop.LedgerUnavailable => "The history folder can't be written; nothing was deleted.",
            CleanupStop.Cancelled => "Stopped after the current file.",
            CleanupStop.CardSwapped => "A different card is in the drive; stopped.",
            CleanupStop.CardRemoved => "The card was removed; stopped.",
            CleanupStop.WriteProtected => "The card became write-protected (lock switch); stopped.",
            CleanupStop.LedgerWriteFailed => "Recording a delete in the history failed; stopped. The report names the file.",
            CleanupStop.InternalSafetyStop => "Internal safety stop; stopped.",
            _ => r.Stop.ToString(),
        };
        Eject = new EjectVm(cardRoot, eject);
        ReportPath = reportPath;
    }

    public string HeadlineText { get; }
    public IReadOnlyList<string> Problems { get; }
    public IReadOnlyList<string> StillListed { get; }
    public string VerdictText { get; }
    public string? StopText { get; }
    public string ReportPath { get; }
    public EjectVm Eject { get; }

    public string SafeRemovalText =>
        "Safely remove the card before putting it back in the drone. The drone may show old thumbnails until it rebuilds its index; formatting in the drone is the clean option.";

    public static (int Files, long Bytes) DeletedTotals(CleanupResult r)
    {
        var files = 0;
        long bytes = 0;
        foreach (var o in r.Outcomes)
        {
            if (o is Deleted d) { files += d.Files; bytes += d.Bytes; }
            else if (o is PartiallyDeleted p) files += p.DeletedPaths.Length;
        }
        return (files, bytes);
    }

    private static string Name(string relPath) => relPath[(relPath.LastIndexOfAny(['/', '\\']) + 1)..];

    private static string? Problem(CleanupOutcome o) => o switch
    {
        Deleted d when !d.SetFolderRemoved && d.Unit.CardRelPath.Contains("PANORAMA", StringComparison.OrdinalIgnoreCase) => $"{Name(d.Unit.CardRelPath)}: folder kept",
        Deleted => null,
        SkippedChanged s => $"{Name(s.CardRelPath)}: skipped, it changed since the scan",
        SkippedEvidenceGone e => $"{Name(e.CardRelPath)}: skipped, {e.Why}",
        PartiallyDeleted p => $"{Name(p.Unit.CardRelPath)}: partly deleted ({p.Why}); still on the card: {string.Join(", ", p.StillOnCard.Select(Name))}",
        CleanupFailed f => $"{Name(f.CardRelPath)}: not deleted ({f.Error})",
        CleanupNotStarted => null,
        CleanupCardSwapped c => $"{Name(c.Unit.CardRelPath)}: not deleted, a different card is in the drive",
    };
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*CleanupAvailability*"`, `-- --filter-method "*CleanupRow*"`, `-- --filter-method "*CleanupResult*"`
Expected: PASS, 10 tests (the Theory counts 7).

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Cleanup tests/UasSort.Review.Tests/CleanupPiecesTests.cs tests/UasSort.Review.Tests/Fixtures/CleanupFixture.cs
git commit -m "feat: cleanup availability, review row and result view models" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.22: `CleanupVm` — modes, `DateOnly` picker, review list, acknowledgements, delete, rescan, report

**Files:**
- Create: `src/UasSort.Review/Cleanup/CleanupEngine.cs`, `src/UasSort.Review/Cleanup/CleanupReports.cs`, `src/UasSort.Review/Cleanup/CleanupVm.cs`
- Modify: `tests/UasSort.Review.Tests/Fixtures/CleanupFixture.cs` (append the inputs builder)
- Test: `tests/UasSort.Review.Tests/CleanupVmTests.cs`

**Interfaces:**
- Consumes: `CleanupPlanner.Candidates(CleanupInputs)` and `CleanupPlanner.Build(CleanupInputs, ImmutableArray<CleanupCandidate>, CleanupRequest, CleanupRows, IReadOnlySet<ItemId>)` (Ref §4.2; called directly because only Core can construct a `CleanupPlan`), `CleanupPlan` members, `CleanupPlan.Confirm(CleanupAck, TimeProvider)`, `ConfirmedCleanupPlan`, `CleanupRequest`, `FreeSpaceGoal`, `CleanupMode`, `FreeSpaceKind`, `CleanupRows`, `CleanupAck`, `CleanupInputs`, `CleanupCutoff`, `FreeSpaceShortfall`, `CleanupKept`, `CleanupResult`, `CleanupProgress`, `CleanupReport`, `CleanupReportLine`, `CleanupOutcome` cases, `CleanupRowVm`, `CleanupResultVm`, `VerdictLevel`, `IDialogService`, `IDeviceEject`; Part 08's `CleanupTexts` (`CutoffLine`, `NothingToDelete`, `ShortfallLine`, `EvidenceSplit`, `NeverCopiesLine`, `VolumeName`) for every summary text (the wording is tested once, in Part 08) and `CleanupRowOps` (`Set`, `KeepAll`, `DeleteAll`) for every review-row change; Part 08's `CleanupExecutor.RunAsync(ConfirmedCleanupPlan, CleanupEnvironment, IProgress<CleanupProgress>, CancellationToken)` is what `CleanupEngine.Run` is bound to.
- Produces (defined here):
  - `public sealed record CleanupPreparation(CleanupInputs? Inputs, string? BlockingText, bool OfferRescan)` — the result of Ref §10.6 Preparation steps 1–4 (identity, re-list + `CardAudit`, fresh listings + ledger `Check`/`Load`, `Space()`), bound in Part 11
  - `public sealed record CleanupEngine(Func<CleanupPreparation> Prepare, Func<ConfirmedCleanupPlan, IProgress<CleanupProgress>, CancellationToken, Task<CleanupResult>> Run, Func<Task<VerdictLevel>> Rescan, Func<CleanupReport, string> SaveReport, IDeviceEject Eject)` — `Run` is exactly `(confirmed, progress, ct) => CleanupExecutor.RunAsync(confirmed, cleanupEnvironment, progress, ct)` (Part 08 owns the run: lock, keep-awake, thumbnail pause, per-file checks, `cardDelete` records, closing re-list); `Rescan` = ShellVm's rescan of the card, returning the recomputed verdict level; `SaveReport` = `IReportStore.Save(CleanupReport)`
  - `public enum CleanupStep { Choose, Review, Confirm, Deleting, Result }`
  - `public sealed class KeptGroupVm(string reason, IReadOnlyList<string> lines)`
  - `public static class CleanupReports` — `CleanupReport Build(CleanupPlan plan, CleanupResult result, VerdictLevel after)`
  - `public sealed partial class CleanupVm : ObservableObject, IDisposable` — `CleanupVm(CleanupEngine engine, IDialogService dialogs, IUiDispatcher ui, TimeProvider time)`; `void Open()`; `void PickDate(DateTimeOffset? picked)`; observable `CleanupStep Step`, `string? BlockingText`, `bool CanRescan`, `CleanupMode Mode`, `DateTimeOffset? PickedDate`, `DateOnly? Before`, `FreeSpaceKind FreeKind`, `double GbValue`, `bool IncludeNotInLibrary`, `CleanupPlan? Plan`, `string CardSummary`, `string FreeNowText`, `string WillDeleteText`, `string CutoffText`, `string? ShortfallText`, `bool CanIncludeNotInLibraryFix`, `string CountsText`, `string FilesText`, `string RangeText`, `string FreeAfterText`, `string EvidenceText`, `string? NeverCopiedText`, `string NeverTouchedText`, `bool AckCantBeRecovered`, `bool AckNotInLibrary`, `bool ShowNotInLibraryAck`, `string NotInLibraryAckText`, `string DeleteButtonText`, `bool CanDelete`, `bool CanContinue`, `string ProgressText`, `CleanupRowVm? FirstUndecided`, `CleanupResultVm? Result`; `ObservableCollection<CleanupRowVm> Rows`, `ObservableCollection<KeptGroupVm> Kept`; commands `ContinueCommand`, `BackCommand`, `KeepAllCommand`, `DeleteAllCommand`, `IncludeNotInLibraryCommand`, `IAsyncRelayCommand DeleteCommand`, `IAsyncRelayCommand CancelCommand`, `IRelayCommand RescanCommand`, `IRelayCommand DoneCommand`; events `Action? Closed`, `Action? RescanRequested`

- [ ] **Step 1: Write the fixture and the failing tests**

```csharp
// tests/UasSort.Review.Tests/Fixtures/CleanupFixture.cs  (append below the Task 10.21 helpers, inside the same partial class file)
namespace UasSort.Review.Tests;

internal static partial class CleanupFixture
{
    /// <summary>One clip = 9,155 clusters of 128 KiB, so allocated size equals file size.</summary>
    public const long S = 1_199_964_160;
    public static readonly VolumeInfo Volume = new(@"E:\", TestPlans.Card, "Removable", true, false, false, true, 12_400_000_000, "Sd", true, false);
    public static readonly CardSpace Space = new(12_400_000_000, 256_060_514_304, 131_072);

    private static readonly LibraryFolderRef Nome = new(TestPlans.VideoRoot + @"\2026\2026-07\2026-07-03 Nome Roads", new(2026, 7, 3), "Nome Roads");
    private static readonly LibraryFolderRef Teller = new(TestPlans.VideoRoot + @"\2026\2026-07\2026-07-28 Teller", new(2026, 7, 28), "Teller");

    public static readonly string[] Old = ["DJI_20260703190000_0001_D.MP4", "DJI_20260703191000_0002_D.MP4", "DJI_20260703192000_0003_D.MP4", "DJI_20260703193000_0004_D.MP4"];
    public const string NewA = "DJI_20260720190000_0050_D.MP4";   // New, ticked for offload
    public const string NewB = "DJI_20260720191000_0051_D.MP4";   // New, unticked
    public const string NewC = "DJI_20260727190000_0060_D.MP4";   // New, unticked, after Jul 26
    public static readonly string[] Late = ["DJI_20260728190000_0070_D.MP4", "DJI_20260728191000_0071_D.MP4"];

    public static CleanupInputs Inputs()
    {
        PlanClip C(string name, DateTime utc, Newness n) => new(name, utc, TestPlans.Anchorage, 64.5, -165.4, Newness: n, Bytes: S);
        var nome = new Imported(Evidence.LibraryNameSize, Nome, "same name and size");
        var teller = new Imported(Evidence.LibraryNameSize, Teller, "same name and size");
        var isNew = new IsNew(NewReason.NoMatch, null);
        List<PlanClip> clips =
        [
            .. Old.Select((n, i) => C(n, TestPlans.Utc(2026, 7, 4, 3, 10 * i), nome)),
            C(NewA, TestPlans.Utc(2026, 7, 21, 3, 0), isNew),
            C(NewB, TestPlans.Utc(2026, 7, 21, 3, 10), isNew),
            C(NewC, TestPlans.Utc(2026, 7, 28, 3, 0), isNew),
            .. Late.Select((n, i) => C(n, TestPlans.Utc(2026, 7, 29, 3, 10 * i), teller)),
        ];
        var t = TestPlans.Utc(2026, 7, 30, 0, 0);
        ImmutableArray<FsEntry> library =
        [
            .. Old.Select(n => new FsEntry(Nome.FullPath + "\\" + n, @"2026\2026-07\2026-07-03 Nome Roads\" + n, false, S, t, t, t, 0x20)),
            .. Late.Select(n => new FsEntry(Teller.FullPath + "\\" + n, @"2026\2026-07\2026-07-28 Teller\" + n, false, S, t, t, t, 0x20)),
        ];
        var listings = TestPlans.Listings(library);
        var b = TestPlans.Base(clips, listings: listings);
        var plan = new ScriptedDeriver().Derive(b, new Tuning(), [new SetIncluded([TestPlans.Id(NewB), TestPlans.Id(NewC)], false)],
                                                new SessionFlags(false), 1, CancellationToken.None);
        var units = b.Items.Select(i =>
        {
            var cat = i.Newness is Imported ? AuditCategory.NameSizeMatch : AuditCategory.Unaccounted;
            return new UnitAudit(i.Raw.Unit.Id, cat, [new AuditLine(i.Raw.Unit.Id.CardRelPath, S, cat, cat == AuditCategory.NameSizeMatch ? "same name and size" : "new: not copied")]);
        }).ToImmutableArray();
        var audit = new FormatVerdict(VerdictLevel.NotSafe, TestPlans.Card, "E: · DJI Air 3S · serial 1A2B-3C4D: Don't format yet",
            ImmutableDictionary<AuditCategory, int>.Empty.Add(AuditCategory.NameSizeMatch, 6).Add(AuditCategory.Unaccounted, 3), 6, 0, units, [], null);
        return new CleanupInputs(b.Scan.Inventory, plan, audit, null, Space, listings, TestPlans.Ledger(), Volume, null, TestPlans.Settings());
    }
}
```

```csharp
// tests/UasSort.Review.Tests/CleanupVmTests.cs
using System.Reflection;

namespace UasSort.Review.Tests;

public class CleanupVmTests
{
    private sealed class Rig
    {
        public FakeDialogService Dialogs { get; } = new();
        public FakeUiDispatcher Ui { get; } = new();
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero));
        public CleanupPreparation Preparation { get; set; } = new(CleanupFixture.Inputs(), null, false);
        public ConfirmedCleanupPlan? Confirmed { get; private set; }
        public CleanupReport? Report { get; private set; }
        public bool RescanThrows { get; set; }
        public FakeEjectOk Eject { get; } = new();

        public Rig() => Time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Anchorage"));

        public CleanupVm Vm()
        {
            var engine = new CleanupEngine(
                () => Preparation,
                (confirmed, progress, ct) =>
                {
                    Confirmed = confirmed;
                    progress.Report(new CleanupProgress(1, confirmed.Plan.FileCount, CleanupFixture.S, confirmed.Plan.AllocatedBytes, "DJI_x.MP4", null));
                    ImmutableArray<CleanupOutcome> outcomes = [.. confirmed.Plan.Delete.Select(c => (CleanupOutcome)new Deleted(c.Unit, c.Files.Length, c.AllocatedBytes, false))];
                    return Task.FromResult(new CleanupResult("cleanup01", confirmed, outcomes, null,
                        new CardSpace(CleanupFixture.Space.FreeBytes + confirmed.Plan.AllocatedBytes, CleanupFixture.Space.TotalBytes, 131_072), [],
                        TestPlans.Utc(2026, 9, 28, 3, 0), TestPlans.Utc(2026, 9, 28, 3, 1)));
                },
                () => RescanThrows ? Task.FromException<VerdictLevel>(new IOException("card removed")) : Task.FromResult(VerdictLevel.Safe),
                r => { Report = r; return @"C:\AppData\uas-sort\reports\20260928-030100-cleanup0-cleanup.json"; },
                Eject);
            var vm = new CleanupVm(engine, Dialogs, Ui, Time);
            vm.Open();
            return vm;
        }
    }

    private static readonly DateTimeOffset Jul26LateEveningAlaska = new(2026, 7, 26, 23, 30, 0, TimeSpan.FromHours(-8));

    [Fact]
    public void Cleanup_ContinueDisabledUntilADateIsPicked_PickerDayIsTheCutoffDay()
    {
        var vm = new Rig().Vm();
        Assert.Equal(CleanupMode.BeforeDate, vm.Mode);
        Assert.False(vm.ContinueCommand.CanExecute(null));
        Assert.Equal("E: · SD card · exFAT · 256.1 GB · serial 1A2B-3C4D", vm.CardSummary);

        vm.PickDate(Jul26LateEveningAlaska);
        Assert.Equal(new DateOnly(2026, 7, 26), vm.Plan!.Request.Before);
        Assert.True(vm.ContinueCommand.CanExecute(null));

        vm.PickDate(new DateTimeOffset(2026, 7, 26, 0, 30, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 7, 26), vm.Plan!.Request.Before);
        Assert.Equal(4, vm.Plan.Delete.Length);
        Assert.Equal(CleanupTexts.CutoffLine(vm.Plan), vm.CutoffText);          // Part 08 owns (and tests) the wording
        Assert.Equal(CleanupTexts.EvidenceSplit(vm.Plan), vm.EvidenceText);
        Assert.Equal(CleanupTexts.NeverCopiesLine(vm.Plan), vm.NeverCopiedText);
        Assert.Equal("Delete 4 files (4.8 GB)", vm.DeleteButtonText);
        Assert.Equal("4 videos", vm.CountsText);
        Assert.Single(vm.Kept);
    }

    [Fact]
    public void Cleanup_SwitchOn_ReviewListRequired_TickedRowsStartOnKeep_DeleteAllLeavesThem()
    {
        var vm = new Rig().Vm();
        vm.PickDate(Jul26LateEveningAlaska);
        vm.IncludeNotInLibrary = true;
        vm.ContinueCommand.Execute(null);
        Assert.Equal(CleanupStep.Review, vm.Step);

        Assert.Equal(2, vm.Rows.Count);
        var a = vm.Rows.Single(r => r.Unit == TestPlans.Id(CleanupFixture.NewA));
        var b = vm.Rows.Single(r => r.Unit == TestPlans.Id(CleanupFixture.NewB));
        Assert.Equal((RowDecision.Keep, "ticked for offload"), (a.Decision, a.Badge));
        Assert.Equal(RowDecision.Delete, b.Decision);

        vm.DeleteAllCommand.Execute(null);
        Assert.Equal(RowDecision.Keep, vm.Rows.Single(r => r.Unit == a.Unit).Decision);
        vm.ContinueCommand.Execute(null);
        Assert.Equal(CleanupStep.Confirm, vm.Step);

        Assert.True(vm.ShowNotInLibraryAck);
        Assert.Equal("Includes 1 file not proven to be in your library.", vm.NotInLibraryAckText);
        Assert.Equal(5, vm.Plan!.FileCount);
        Assert.Equal(CleanupTexts.CutoffLine(vm.Plan), vm.CutoffText);
        Assert.Equal("Delete 5 files (6 GB)", vm.DeleteButtonText);

        vm.Rows.Single(r => r.Unit == a.Unit).DeleteCommand.Execute(null);
        Assert.Equal("Delete 6 files (7.2 GB)", vm.DeleteButtonText);
        Assert.Contains(vm.Plan!.Delete, c => c.Unit == a.Unit);
        Assert.Equal(CleanupTexts.CutoffLine(vm.Plan), vm.CutoffText);
    }

    [Fact]
    public void Cleanup_RowsThatJoinLater_AreUndecidedAndBlockDelete()
    {
        var vm = new Rig().Vm();
        vm.PickDate(Jul26LateEveningAlaska);
        vm.IncludeNotInLibrary = true;
        vm.ContinueCommand.Execute(null);

        vm.PickDate(new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.FromHours(-8)));
        var c = vm.Rows.Single(r => r.Unit == TestPlans.Id(CleanupFixture.NewC));
        Assert.Equal((RowDecision.Undecided, "new in range"), (c.Decision, c.Badge));
        Assert.Same(c, vm.FirstUndecided);
        Assert.Equal("Decide 1 new row", vm.DeleteButtonText);
        Assert.False(vm.ContinueCommand.CanExecute(null));

        vm.DeleteAllCommand.Execute(null);
        Assert.Equal(RowDecision.Delete, vm.Rows.Single(r => r.Unit == c.Unit).Decision);
        Assert.Equal("Delete 8 files (9.6 GB)", vm.DeleteButtonText);
        Assert.True(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void Cleanup_AcknowledgementsGateDelete_AndClearOnAnyFingerprintChange()
    {
        var vm = new Rig().Vm();
        vm.PickDate(Jul26LateEveningAlaska);
        vm.ContinueCommand.Execute(null);
        Assert.Equal(CleanupStep.Confirm, vm.Step);
        Assert.False(vm.CanDelete);

        vm.AckCantBeRecovered = true;
        Assert.True(vm.CanDelete);

        vm.PickDate(new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.FromHours(-8)));
        Assert.False(vm.AckCantBeRecovered);
        Assert.False(vm.CanDelete);
    }

    [Fact]
    public void Cleanup_FreeSpaceModes_TargetsTextsAndShortfall()
    {
        var vm = new Rig().Vm();
        vm.Mode = CleanupMode.FreeSpace;
        Assert.Equal(FreeSpaceKind.HaveFree, vm.FreeKind);
        Assert.Equal("E: 12.4 GB free of 256.1 GB", vm.FreeNowText);
        Assert.False(vm.ContinueCommand.CanExecute(null));

        vm.GbValue = 15;
        Assert.Equal("will delete ≈ 3.6 GB = free up ≈ 3.6 GB", vm.WillDeleteText);
        Assert.Equal(3, vm.Plan!.FileCount);
        Assert.Equal(CleanupTexts.CutoffLine(vm.Plan), vm.CutoffText);

        vm.FreeKind = FreeSpaceKind.FreeUp;
        vm.GbValue = 2;
        Assert.Equal("will delete ≈ 2.4 GB", vm.WillDeleteText);

        vm.FreeKind = FreeSpaceKind.HaveFree;
        vm.GbValue = 10;
        Assert.Empty(vm.Plan!.Delete);
        Assert.NotNull(CleanupTexts.NothingToDelete(vm.Plan));
        Assert.Equal(CleanupTexts.NothingToDelete(vm.Plan), vm.CutoffText);
        Assert.False(vm.CanDelete);

        vm.GbValue = 20;
        Assert.NotNull(vm.Plan!.Shortfall);
        Assert.Equal(CleanupTexts.ShortfallLine(vm.Plan), vm.ShortfallText);
        Assert.True(vm.CanIncludeNotInLibraryFix);
        vm.IncludeNotInLibraryCommand.Execute(null);
        Assert.True(vm.IncludeNotInLibrary);
    }

    [Fact]
    public void Cleanup_PreparationBlocking_DisablesEverythingAndOffersRescan()
    {
        var rig = new Rig { Preparation = new CleanupPreparation(null, "A different card is in E:; rescan", true) };
        var vm = rig.Vm();
        var rescan = false;
        vm.RescanRequested += () => rescan = true;

        Assert.Equal("A different card is in E:; rescan", vm.BlockingText);
        vm.PickDate(Jul26LateEveningAlaska);
        Assert.False(vm.ContinueCommand.CanExecute(null));
        Assert.False(vm.CanDelete);
        Assert.True(vm.CanRescan);
        vm.RescanCommand.Execute(null);
        Assert.True(rescan);
    }

    [Fact]
    public async Task Cleanup_Delete_ConfirmsRunsRescansAndSavesTheReport()
    {
        var rig = new Rig();
        var vm = rig.Vm();
        vm.PickDate(Jul26LateEveningAlaska);
        vm.ContinueCommand.Execute(null);
        vm.AckCantBeRecovered = true;

        await vm.DeleteCommand.ExecuteAsync(null);
        rig.Ui.RunAll();

        Assert.Equal(4, rig.Confirmed!.FilePaths.Count);
        Assert.Equal(CleanupStep.Result, vm.Step);
        var report = rig.Report!;
        Assert.Equal(4, report.Files.Length);
        Assert.All(report.Files, l => Assert.True(l.LedgerRecorded));
        Assert.All(report.Files, l => Assert.Equal("deleted", l.Outcome));
        Assert.Equal(12_400_000_000, report.FreeBefore);
        Assert.Equal(VerdictLevel.Safe, report.VerdictAfter);
        Assert.Equal("Deleted 4 files (4.8 GB) · E: now has 17.2 GB free", vm.Result!.HeadlineText);
    }

    [Fact]
    public async Task Cleanup_RescanFails_ReportStillWrittenWithNotSafe()
    {
        var rig = new Rig { RescanThrows = true };
        var vm = rig.Vm();
        vm.PickDate(Jul26LateEveningAlaska);
        vm.ContinueCommand.Execute(null);
        vm.AckCantBeRecovered = true;

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(VerdictLevel.NotSafe, rig.Report!.VerdictAfter);
        Assert.Equal("Don't format yet", vm.Result!.VerdictText);
    }

    [Fact]
    public void Cleanup_PlanTypes_HaveNoPublicConstructorAndNoWithClone()
    {
        foreach (var t in new[] { typeof(CleanupPlan), typeof(ConfirmedCleanupPlan) })
        {
            Assert.Empty(t.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
            Assert.Null(t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance));
            Assert.True(t.IsSealed);
        }
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Cleanup_*"`
Expected: build FAILS (CS0246 `CleanupVm`, `CleanupEngine`, `CleanupPreparation`, `CleanupStep`).

- [ ] **Step 3: Implement the engine record and the report builder**

```csharp
// src/UasSort.Review/Cleanup/CleanupEngine.cs
namespace UasSort.Review;

/// <summary>Ref §10.6 Preparation steps 1–4, done by Part 11's composition root when the page opens (nothing is written).</summary>
public sealed record CleanupPreparation(CleanupInputs? Inputs, string? BlockingText, bool OfferRescan);

/// <summary>What the cleanup page calls outside Review. <c>Run</c> is bound (Part 11) to exactly
/// <c>(confirmed, progress, ct) => CleanupExecutor.RunAsync(confirmed, cleanupEnvironment, progress, ct)</c>: Part 08 owns the whole run.</summary>
public sealed record CleanupEngine(
    Func<CleanupPreparation> Prepare,
    Func<ConfirmedCleanupPlan, IProgress<CleanupProgress>, CancellationToken, Task<CleanupResult>> Run,
    Func<Task<VerdictLevel>> Rescan,
    Func<CleanupReport, string> SaveReport,
    IDeviceEject Eject);

public enum CleanupStep { Choose, Review, Confirm, Deleting, Result }

public sealed class KeptGroupVm(string reason, IReadOnlyList<string> lines)
{
    public string Reason { get; } = reason;
    public IReadOnlyList<string> Lines { get; } = lines;
    public override string ToString() => Reason;
}
```

```csharp
// src/UasSort.Review/Cleanup/CleanupReports.cs
namespace UasSort.Review;

/// <summary>Builds reports\&lt;ts&gt;-&lt;run8&gt;-cleanup.json content (Ref §10.6 "After the loop" step 4).</summary>
public static class CleanupReports
{
    public static CleanupReport Build(CleanupPlan plan, CleanupResult result, VerdictLevel after)
    {
        var byUnit = result.Outcomes.ToDictionary(o => o.Unit);
        var deletedInOrder = new List<string>();
        var lines = new List<CleanupReportLine>();
        foreach (var c in plan.Delete)
        {
            var outcome = byUnit.GetValueOrDefault(c.Unit);
            foreach (var f in c.Files)
            {
                var deleted = outcome switch
                {
                    Deleted => true,
                    PartiallyDeleted p => p.DeletedPaths.Contains(f.RelPath, StringComparer.OrdinalIgnoreCase),
                    _ => false,
                };
                if (deleted) deletedInOrder.Add(f.RelPath);
                var text = outcome switch
                {
                    null => "not started",
                    Deleted => "deleted",
                    PartiallyDeleted => deleted ? "deleted" : "kept (unit partly deleted)",
                    SkippedChanged => "skipped: changed since the scan",
                    SkippedEvidenceGone => "skipped: evidence gone",
                    CleanupFailed => "failed",
                    CleanupNotStarted => "not started",
                    CleanupCardSwapped => "not deleted: card swapped",
                };
                var primary = c.Proofs.Any(p => string.Equals(p.CardRelPath, f.RelPath, StringComparison.OrdinalIgnoreCase)) || c.Proofs.Length == 0;
                var evidence = c.Eligibility == CleanupEligibility.NotInLibrary ? "notInLibraryConfirmed"
                             : primary ? (c.Source == EvidenceSource.HistoryOnly ? "historyOnly:" : "") + c.Evidence
                             : "companionOf:" + c.Evidence;
                int? win32 = outcome is CleanupFailed cf && string.Equals(cf.CardRelPath, f.RelPath, StringComparison.OrdinalIgnoreCase) ? cf.Win32Error : null;
                lines.Add(new CleanupReportLine(f.RelPath, f.Size, c.Unit.CardRelPath, text, c.Eligibility.ToString(), evidence, c.Reason, win32, deleted));
            }
        }
        if (result.Stop == CleanupStop.LedgerWriteFailed && deletedInOrder.Count > 0)
        {
            var last = deletedInOrder[^1];
            var i = lines.FindIndex(l => string.Equals(l.CardRelPath, last, StringComparison.OrdinalIgnoreCase));
            lines[i] = lines[i] with { LedgerRecorded = false };
        }
        return new CleanupReport(1, result.RunId, plan.Card, plan.Request, plan.Cutoff, [.. lines], plan.NotDeletable, result.Stop,
                                 plan.SpaceBefore.FreeBytes, result.SpaceAfter.FreeBytes, after);
    }
}
```

- [ ] **Step 4: Implement the page VM**

```csharp
// src/UasSort.Review/Cleanup/CleanupVm.cs
namespace UasSort.Review;

/// <summary>The Card cleanup page (Ref §10.6): Choose → Not-in-library review → Confirm → Deleting → Result.</summary>
public sealed partial class CleanupVm : ObservableObject, IDisposable
{
    private readonly CleanupEngine _engine;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly HashSet<ItemId> _firstShown = [];
    private CleanupRows _rows = new([], []);
    private CleanupInputs? _inputs;
    private ImmutableArray<CleanupCandidate> _candidates = [];
    private string? _ackFingerprint;
    private bool _reviewShown;
    private CancellationTokenSource? _cts;

    public CleanupVm(CleanupEngine engine, IDialogService dialogs, IUiDispatcher ui, TimeProvider time)
    {
        _engine = engine;
        _dialogs = dialogs;
        _ui = ui;
        _time = time;
        ContinueCommand = new RelayCommand(Continue, () => CanContinue);
        BackCommand = new RelayCommand(Back, () => Step is CleanupStep.Choose or CleanupStep.Review or CleanupStep.Confirm);
        KeepAllCommand = new RelayCommand(() => SetAll(keep: true));
        DeleteAllCommand = new RelayCommand(() => SetAll(keep: false));
        IncludeNotInLibraryCommand = new RelayCommand(() => IncludeNotInLibrary = true, () => CanIncludeNotInLibraryFix);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => CanDelete);
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => Step == CleanupStep.Deleting);
        RescanCommand = new RelayCommand(() => RescanRequested?.Invoke(), () => CanRescan);
        DoneCommand = new RelayCommand(() => Closed?.Invoke(), () => Step == CleanupStep.Result);
        GbValue = double.NaN; // after the commands: the change handler rebuilds and notifies them
    }

    public ObservableCollection<CleanupRowVm> Rows { get; } = [];
    public ObservableCollection<KeptGroupVm> Kept { get; } = [];

    [ObservableProperty] public partial CleanupStep Step { get; private set; }
    [ObservableProperty] public partial string? BlockingText { get; private set; }
    [ObservableProperty] public partial bool CanRescan { get; private set; }
    [ObservableProperty] public partial CleanupMode Mode { get; set; }
    [ObservableProperty] public partial DateTimeOffset? PickedDate { get; set; }
    [ObservableProperty] public partial DateOnly? Before { get; private set; }
    [ObservableProperty] public partial FreeSpaceKind FreeKind { get; set; }
    [ObservableProperty] public partial double GbValue { get; set; }
    [ObservableProperty] public partial bool IncludeNotInLibrary { get; set; }
    [ObservableProperty] public partial CleanupPlan? Plan { get; private set; }
    [ObservableProperty] public partial string CardSummary { get; private set; } = "";
    [ObservableProperty] public partial string FreeNowText { get; private set; } = "";
    [ObservableProperty] public partial string WillDeleteText { get; private set; } = "";
    [ObservableProperty] public partial string CutoffText { get; private set; } = "";
    [ObservableProperty] public partial string? ShortfallText { get; private set; }
    [ObservableProperty] public partial bool CanIncludeNotInLibraryFix { get; private set; }
    [ObservableProperty] public partial string CountsText { get; private set; } = "";
    [ObservableProperty] public partial string FilesText { get; private set; } = "";
    [ObservableProperty] public partial string RangeText { get; private set; } = "";
    [ObservableProperty] public partial string FreeAfterText { get; private set; } = "";
    [ObservableProperty] public partial string EvidenceText { get; private set; } = "";
    [ObservableProperty] public partial string? NeverCopiedText { get; private set; }
    [ObservableProperty] public partial string NeverTouchedText { get; private set; } = "";
    [ObservableProperty] public partial bool AckCantBeRecovered { get; set; }
    [ObservableProperty] public partial bool AckNotInLibrary { get; set; }
    [ObservableProperty] public partial bool ShowNotInLibraryAck { get; private set; }
    [ObservableProperty] public partial string NotInLibraryAckText { get; private set; } = "";
    [ObservableProperty] public partial string DeleteButtonText { get; private set; } = "Delete";
    [ObservableProperty] public partial bool CanDelete { get; private set; }
    [ObservableProperty] public partial bool CanContinue { get; private set; }
    [ObservableProperty] public partial string ProgressText { get; private set; } = "";
    [ObservableProperty] public partial CleanupRowVm? FirstUndecided { get; private set; }
    [ObservableProperty] public partial CleanupResultVm? Result { get; private set; }

    public string CantBeRecoveredText => "Files deleted from a memory card can't be recovered.";

    public IRelayCommand ContinueCommand { get; }
    public IRelayCommand BackCommand { get; }
    public IRelayCommand KeepAllCommand { get; }
    public IRelayCommand DeleteAllCommand { get; }
    public IRelayCommand IncludeNotInLibraryCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand CancelCommand { get; }
    public IRelayCommand RescanCommand { get; }
    public IRelayCommand DoneCommand { get; }

    public event Action? Closed;
    public event Action? RescanRequested;

    public void Open()
    {
        var prep = _engine.Prepare();
        BlockingText = prep.BlockingText;
        CanRescan = prep.OfferRescan;
        _inputs = prep.Inputs;
        if (_inputs is { } inputs)
        {
            _candidates = CleanupPlanner.Candidates(inputs);
            var v = inputs.Volume;
            var bus = v.BusType switch { "Sd" or "Mmc" => "SD card", "Usb" => "USB drive", _ => v.BusType };
            var volume = CleanupTexts.VolumeName(v.Root);
            CardSummary = $"{volume} · {bus} · {v.Identity.FileSystem} · {Fmt.Size(v.Identity.TotalBytes)} · serial {Fmt.Serial(v.Identity.VolumeSerial)}";
            FreeNowText = $"{volume} {Fmt.Size(inputs.Space.FreeBytes)} free of {Fmt.Size(inputs.Space.TotalBytes)}";
            var never = inputs.Inventory.Entries.Count(e => e.RelPath.StartsWith("MISC/", StringComparison.OrdinalIgnoreCase)
                                                         || !e.RelPath.StartsWith("DCIM/", StringComparison.OrdinalIgnoreCase));
            NeverTouchedText = $"Never touched: MISC (DJI index), system files · {Fmt.Count(never, "file", "files")}";
        }
        CanRescan = prep.OfferRescan;
        RescanCommand.NotifyCanExecuteChanged();
        Rebuild();
    }

    /// <summary>The cutoff is the picker's own calendar day, never converted through UtcDateTime or ToLocalTime (Ref §10.6 Mode 1).</summary>
    public void PickDate(DateTimeOffset? picked) => PickedDate = picked;

    public void Dispose() => _cts?.Dispose();

    partial void OnPickedDateChanged(DateTimeOffset? value)
    {
        Before = value is { } v ? DateOnly.FromDateTime(v.DateTime) : null;
        Rebuild();
    }

    partial void OnModeChanged(CleanupMode value) => Rebuild();
    partial void OnFreeKindChanged(FreeSpaceKind value) => Rebuild();
    partial void OnGbValueChanged(double value) => Rebuild();

    partial void OnIncludeNotInLibraryChanged(bool value)
    {
        if (value && Step == CleanupStep.Confirm) Step = CleanupStep.Review;
        if (value && Step == CleanupStep.Review) ShowReview();
        Rebuild();
    }

    partial void OnAckCantBeRecoveredChanged(bool value) => UpdateGates();
    partial void OnAckNotInLibraryChanged(bool value) => UpdateGates();

    private CleanupRequest? Request() => Mode switch
    {
        CleanupMode.BeforeDate when Before is { } d => new CleanupRequest(CleanupMode.BeforeDate, d, null, IncludeNotInLibrary),
        CleanupMode.FreeSpace when !double.IsNaN(GbValue) && GbValue >= 0 =>
            new CleanupRequest(CleanupMode.FreeSpace, null, new FreeSpaceGoal(FreeKind, (long)Math.Round(GbValue * 1e9)), IncludeNotInLibrary),
        _ => null,
    };

    private void Rebuild()
    {
        if (_inputs is { } inputs && Request() is { } request)
        {
            Plan = CleanupPlanner.Build(inputs, _candidates, request, _rows, _firstShown);
            if (!string.Equals(Plan.Fingerprint, _ackFingerprint, StringComparison.Ordinal))
            {
                _ackFingerprint = Plan.Fingerprint;
                AckCantBeRecovered = false;
                AckNotInLibrary = false;
            }
        }
        else
        {
            Plan = null;
        }
        SyncRows();
        UpdateTexts();
        UpdateGates();
    }

    /// <summary>First time the review list is shown: every in-scope row gets its start state through CleanupRowOps.Set
    /// ("ticked for offload" rows on Keep, the others on Delete); rows that join later stay undecided (Ref §10.6).</summary>
    private void ShowReview()
    {
        if (_reviewShown || Plan is null) return;
        _reviewShown = true;
        foreach (var c in Plan.NotInLibraryInScope)
        {
            _firstShown.Add(c.Unit);
            if (_rows.Keep.Contains(c.Unit) || _rows.Delete.Contains(c.Unit)) continue;
            _rows = CleanupRowOps.Set(_rows, c.Unit, delete: !c.TickedForOffload);
        }
        Rebuild();
    }

    private void SetRow(CleanupRowVm row, RowDecision decision)
    {
        if (decision == RowDecision.Undecided) return;
        _rows = CleanupRowOps.Set(_rows, row.Unit, delete: decision == RowDecision.Delete);
        Rebuild();
    }

    /// <summary>[Keep all] / [Delete all] are Part 08's CleanupRowOps (Delete all leaves "ticked for offload" rows as they are).</summary>
    private void SetAll(bool keep)
    {
        if (Plan is not { } plan) return;
        _rows = keep ? CleanupRowOps.KeepAll(plan) : CleanupRowOps.DeleteAll(plan);
        Rebuild();
    }

    private void SyncRows()
    {
        IReadOnlyList<CleanupCandidate> inScope = Plan is { } p && IncludeNotInLibrary && _reviewShown ? p.NotInLibraryInScope : [];
        CollectionSync.Sync(Rows, inScope, c => c.Unit.CardRelPath, c => new CleanupRowVm(c, SetRow),
            (vm, c) => vm.Update(c,
                _rows.Keep.Contains(c.Unit) ? RowDecision.Keep : _rows.Delete.Contains(c.Unit) ? RowDecision.Delete : RowDecision.Undecided,
                Plan!.Undecided.Contains(c.Unit)));
        FirstUndecided = Rows.FirstOrDefault(r => r.Decision == RowDecision.Undecided);
    }

    private void UpdateTexts()
    {
        Kept.Clear();
        if (Plan is not { } p || _inputs is null)
        {
            CutoffText = Mode == CleanupMode.BeforeDate ? "Pick a date" : "Enter an amount in GB";
            WillDeleteText = "";
            ShortfallText = null;
            CanIncludeNotInLibraryFix = false;
            DeleteButtonText = "Delete";
            CountsText = "";
            FilesText = "";
            RangeText = "";
            FreeAfterText = "";
            EvidenceText = "";
            NeverCopiedText = null;
            ShowNotInLibraryAck = false;
            return;
        }
        var notInLib = p.Delete.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary).ToList();
        var reviewedFiles = notInLib.Sum(c => c.Files.Length);

        // Part 08's CleanupTexts own the cutoff / nothing-to-delete / shortfall / evidence / never-copied wording (Ref §10.6).
        CutoffText = CleanupTexts.CutoffLine(p);
        WillDeleteText = p.Request.Mode == CleanupMode.FreeSpace
            ? $"will delete ≈ {Fmt.Size(p.AllocatedBytes)}" + (FreeKind == FreeSpaceKind.HaveFree ? $" = free up ≈ {Fmt.Size(p.AllocatedBytes)}" : "")
            : "";
        ShortfallText = CleanupTexts.ShortfallLine(p);
        CanIncludeNotInLibraryFix = p.Shortfall is { HeldByNotInLibrary: > 0 } && !IncludeNotInLibrary;
        IncludeNotInLibraryCommand.NotifyCanExecuteChanged();

        var kinds = new List<string>();
        void Kind(ItemKind k, string one, string many)
        {
            var n = p.Delete.Count(c => c.Kind == k);
            if (n > 0) kinds.Add(Fmt.Count(n, one, many));
        }
        Kind(ItemKind.Video, "video", "videos");
        Kind(ItemKind.Photo, "photo", "photos");
        Kind(ItemKind.Set, "set", "sets");
        CountsText = kinds.Count == 0 ? "nothing" : string.Join(" · ", kinds);
        FilesText = $"{Fmt.Count(p.FileCount, "file", "files")} · {Fmt.Size(p.AllocatedBytes)}";
        RangeText = p.Delete.Length == 0 ? "" : $"captured {Fmt.DateRange(p.Delete.Min(c => c.LocalDate), p.Delete.Max(c => c.LocalDate))}";
        FreeAfterText = $"{Fmt.Size(p.SpaceBefore.FreeBytes)} free now → ≈ {Fmt.Size(p.ExpectedFreeAfter)} after";
        EvidenceText = CleanupTexts.EvidenceSplit(p);
        NeverCopiedText = CleanupTexts.NeverCopiesLine(p);

        ShowNotInLibraryAck = notInLib.Count > 0;
        NotInLibraryAckText = $"Includes {Fmt.Count(reviewedFiles, "file", "files")} not proven to be in your library.";
        DeleteButtonText = p.Undecided.Count > 0
            ? $"Decide {Fmt.Count(p.Undecided.Count, "new row", "new rows")}"
            : $"Delete {Fmt.Count(p.FileCount, "file", "files")} ({Fmt.Size(p.AllocatedBytes)})";
        foreach (var g in p.NotDeletable.GroupBy(k => k.Reason, StringComparer.Ordinal))
            Kept.Add(new KeptGroupVm(g.Key, [.. g.Select(k =>
                $"{Path.GetFileName(k.CardRelPaths.FirstOrDefault() ?? "")}{(k.CardRelPaths.Length > 1 ? $" +{k.CardRelPaths.Length - 1}" : "")} · {Fmt.Size(k.Bytes)}")]));
    }

    private void UpdateGates()
    {
        var ready = Plan is not null && BlockingText is null;
        CanContinue = Step switch
        {
            CleanupStep.Choose => ready,
            CleanupStep.Review => ready && Plan!.Undecided.Count == 0,
            _ => false,
        };
        CanDelete = ready && Step == CleanupStep.Confirm && Plan!.Delete.Length > 0 && Plan.Undecided.Count == 0
                    && AckCantBeRecovered && (!ShowNotInLibraryAck || AckNotInLibrary);
        ContinueCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
    }

    partial void OnStepChanged(CleanupStep value)
    {
        UpdateGates();
        CancelCommand.NotifyCanExecuteChanged();
        DoneCommand.NotifyCanExecuteChanged();
    }

    private void Continue()
    {
        if (Step == CleanupStep.Choose && IncludeNotInLibrary)
        {
            Step = CleanupStep.Review;
            ShowReview();
        }
        else if (Step is CleanupStep.Choose or CleanupStep.Review)
        {
            Step = CleanupStep.Confirm;
        }
    }

    private void Back()
    {
        switch (Step)
        {
            case CleanupStep.Confirm:
                Step = IncludeNotInLibrary ? CleanupStep.Review : CleanupStep.Choose;
                break;
            case CleanupStep.Review:
                Step = CleanupStep.Choose;
                break;
            default:
                Closed?.Invoke();
                break;
        }
    }

    private async Task DeleteAsync()
    {
        if (!CanDelete || Plan is not { } plan) return;
        var notInLibrary = plan.Delete.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary).Select(c => c.Unit).ToImmutableHashSet();
        var confirmed = plan.Confirm(new CleanupAck(plan.Fingerprint, AckCantBeRecovered, notInLibrary.Count > 0 && AckNotInLibrary, notInLibrary), _time);
        _cts = new CancellationTokenSource();
        Step = CleanupStep.Deleting;
        var progress = new UiProgress<CleanupProgress>(_ui, p =>
            ProgressText = string.Create(CultureInfo.InvariantCulture, $"{p.FilesDone} / {Fmt.Count(p.FilesTotal, "file", "files")} · {Fmt.Size(p.BytesDone)} of {Fmt.Size(p.BytesTotal)}"));
        var result = await _engine.Run(confirmed, progress, _cts.Token).ConfigureAwait(true);
        VerdictLevel after;
#pragma warning disable CA1031 // any failure of the rescan (card pulled, IO) means the verdict after is NotSafe (Ref §10.6 step 4)
        try
        {
            after = await _engine.Rescan().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            after = VerdictLevel.NotSafe;
        }
#pragma warning restore CA1031
        var reportPath = _engine.SaveReport(CleanupReports.Build(plan, result, after));
        Result = new CleanupResultVm(result, after, reportPath, plan.CardRoot, _engine.Eject);
        Step = CleanupStep.Result;
    }

    private async Task CancelAsync()
    {
        var answer = await _dialogs.ShowAsync(new DialogRequest("Stop the cleanup?", "Stop the cleanup after the current file?", "Stop", null, "Keep going"))
                                   .ConfigureAwait(true);
        if (answer == DialogResult.Primary && _cts is { } cts) await cts.CancelAsync().ConfigureAwait(true);
    }
}
```

- [ ] **Step 5: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Cleanup_*"`
Expected: PASS, 9 tests. These exercise the real `CleanupPlanner` and `CleanupPlan.Confirm` (Part 08); an expectation that disagrees with Part 08 is a Part 08 defect against Ref §10.6 unless the Ref text says otherwise.

- [ ] **Step 6: Commit**

```bash
git add src/UasSort.Review/Cleanup tests/UasSort.Review.Tests/CleanupVmTests.cs tests/UasSort.Review.Tests/Fixtures/CleanupFixture.cs
git commit -m "feat: card cleanup page view model with review list, acknowledgements and report" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.23: `ShellVm` stage machine (Setup → Card → Scan → Review → Commit, Cleanup, Settings; disabled controls)

**Files:**
- Create: `src/UasSort.Review/Shell/ShellVm.cs`
- Test: `tests/UasSort.Review.Tests/ShellVmTests.cs`

**Interfaces:**
- Consumes: every stage VM of this part, `CleanupAvailability`, `SettingsLoad`, `Settings`/`LayoutSettings`, `CardSource`, `PlanBase`, `CommitResult` (Part 07: `Offload`, `Verdict`, `ReportPath`), `OffloadResult`, `FormatVerdict`; tests: `UasSort.Testing.Offload.OffloadPlanBuilder`/`OffloadRig`, `CommitSession.Begin`, the shared `UasSort.Testing` fakes.
- Produces (defined here):
  - `public enum Stage { Setup, Card, Scan, Review, Preflight, Copy, Verdict, Cleanup, Settings }`
  - `public enum CleanupOrigin { Review, Verdict }`
  - `public sealed record ShellDeps(SettingsLoad Settings, Func<SettingsLoad, SetupVm> CreateSetup, Func<CardStageVm> CreateCard, Func<Settings, ScanStageVm> CreateScan, Func<CardSource, PlanBase, ReviewVm> CreateReview, Func<ReviewVm, PreflightVm> CreatePreflight, Func<PreflightVm, CopyVm> CreateCopy, Func<ReviewVm, CommitResult?, VerdictVm> CreateVerdict, Func<CleanupOrigin, OffloadResult?, CleanupVm> CreateCleanup, Func<Settings, SettingsPageVm> CreateSettings, Func<ReviewVm, FormatVerdict> AuditNow, Func<CardSource, bool> CardPresent, Func<CardSource, string?> VolumeRefusal, Action<Settings> SaveSettings)` (Part 11 binds `CreatePreflight` to `new PreflightVm(r.Plan, source, plan => CommitSession.Begin(plan, commitEnvironment), new CommitPorts(...))` and `SaveSettings` to `ISettingsStore.Save`)
  - `public sealed partial class ShellVm : ObservableObject` — `ShellVm(ShellDeps deps)`; observable `Stage Stage`, `object? Current`, `string? CardChipText`, `bool CanRescan`, `bool CanOpenSettings`, `bool CanBrowse`, `bool CanUndoRedo`, `bool CleanupEnabled`, `string? CleanupTooltip`, `bool IsScanning`; `Settings Settings`; `CardSource? Source`; `ReviewVm? Review`; `PreflightVm? Preflight`; `CopyVm? Copy`; `VerdictVm? Verdict`; `CleanupVm? Cleanup`; `CardStageVm? Card`; methods `Task StartAsync()`, `Task UseCardAsync(CardSource s)`, `Task RescanAsync()`, `Task<VerdictLevel> RescanForCleanupAsync()`, `void BeginOffload()`, `Task StartCopyAsync()`, `void OpenCleanup(CleanupOrigin origin)`, `void OpenSettings()`, `void CloseSettings()`, `void DeviceChanged()`, `void UpdateLayout(double timelineWidth, double mapHeightRatio)` (saves `Settings with { Layout = new(timelineWidth, mapHeightRatio) }` through `SaveSettings` and updates `Settings`); commands `IAsyncRelayCommand RescanCommand`, `IRelayCommand SettingsCommand`, `IRelayCommand CleanupCommand`

Rules (Ref §9.1, §9.2, §10.6): Setup shows when the roots are unconfirmed or settings were recovered; the Card stage auto-advances only for exactly one DJI card; Rescan, Settings and Browse are disabled from Preflight until the verdict is shown, and during Cleanup (plus Undo/Redo and Offload, since Review is not the current stage); leaving Cleanup before Delete deletes nothing; after a cleanup the card is rescanned and Done returns to Review of the rescanned card. The offload lock and the thumbnail pause (held by the `CommitSession` inside `PreflightVm`) are released once the verdict is shown (the shell disposes `Preflight`), so [Clean up card…] on the Verdict page can take the lock; the last `CommitResult` is kept so a cleanup opened from the Verdict page gets its `Offload`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/UasSort.Review.Tests/ShellVmTests.cs
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Review.Tests;

public class ShellVmTests
{
    private sealed class Volumes(IReadOnlyList<VolumeInfo> v) : IVolumeProvider
    {
        public IReadOnlyList<VolumeInfo> GetVolumes() => v;
    }

    private sealed class Rig
    {
        public Rig()
        {
            var b = new OffloadPlanBuilder();
            var v = b.Video("DJI_20260927140000_0123_D.MP4", 5_000, T0);
            b.Group(new NewFolder(@"2026\2026-09\2026-09-27 Zachar Bay"), Zachar, v);
            Offload = new OffloadRig(b).Build();
            Commit = new CommitEnvironment(Offload.Reader, Offload.Fs, Offload.Ledger, Offload.Lock, Offload.Power, Offload.Thumbnails,
                                           Offload.Reports, new FakeVolumeProvider(Offload.Volumes), dirs => new FakeFileOps(Offload.Fs, dirs),
                                           new FakeTimeProvider(new DateTimeOffset(T0.AddHours(1))), FakeLayout.Machine, "0.1.0");
        }

        public FakeUiDispatcher Ui { get; } = new();
        public FakeLedgerStore Ledger { get; } = Fake.Ledger();
        /// <summary>The Commit runs over Part 07's offload rig; the shell only needs a real session to hold and release the lock.</summary>
        public OffloadRig Offload { get; }
        public CommitEnvironment Commit { get; }
        public List<VolumeInfo> Cards { get; } = [CleanupFixture.Volume];
        public bool WriteProtected { get; set; }
        public int Scans { get; private set; }
        public List<Settings> Saved { get; } = [];
        public PlanBase Base { get; } = TestPlans.Base(TestPlans.Zachar());

        public ShellVm Shell(bool rootsConfirmed = true)
        {
            var services = Fake.Services(Ui);
            var load = new SettingsLoad(TestPlans.Settings(rootsConfirmed), false, null, null);
            var store = new FakeSettingsStore(load);
            var verdict = new FormatVerdict(VerdictLevel.Safe, TestPlans.Card, "E: · DJI Air 3S · serial 1A2B-3C4D: Safe to format: 3 verified",
                                            ImmutableDictionary<AuditCategory, int>.Empty, 0, 0, [], [], null);
            ShellVm? shell = null;
            shell = new ShellVm(new ShellDeps(
                load,
                l => new SetupVm(l, store, _ => Ledger, new FakeFreeSpace()),
                () => new CardStageVm(new Volumes(Cards), vs => [.. vs.Select(v => new CardCandidate(v, true, null, 214))],
                                      (path, v) => new SourceOk(new CardSource(path, v?.Identity, v is null, WriteProtected))),
                s => new ScanStageVm((src, p, ct) => { Scans++; return Task.FromResult(Base.Scan); }, _ => Base, s, Ui),
                (src, b) => new ReviewVm(new PlanSession(b, new ScriptedDeriver([new Suggestion("Zachar Bay", DescSource.Feature, null, null)]), new Tuning(), services.Time),
                                         services, new LedgerDecisionService(Ledger, services.Time, "PC1")),
                r => new PreflightVm(r.Plan, shell!.Source!, _ => CommitSession.Begin(Offload.Plan, Commit, "run-1"),
                                     new CommitPorts(new FakeDraftStore(), new FakeDialogService(), Ui)),
                p => new CopyVm(p, new FakeDialogService(), Ui),
                (r, res) => new VerdictVm(res?.Verdict ?? verdict, r.Plan, res?.Offload, res?.ReportPath,
                                          new VerdictPorts(Ledger, "PC1", services.Time, new FakeDialogService(), new FakeShellLauncher(), new FakeEjectOk(),
                                                           () => res?.Verdict ?? verdict)),
                (origin, res) => new CleanupVm(new CleanupEngine(() => new CleanupPreparation(null, "A different card is in E:; rescan", true),
                                                                 (c, p, ct) => throw new InvalidOperationException("not reached"),
                                                                 () => shell!.RescanForCleanupAsync(), r => "r.json", new FakeEjectOk()),
                                               new FakeDialogService(), Ui, services.Time),
                s => new SettingsPageVm(s, store, _ => Ledger, FakeLayout.NewFileSystem(), new FakeShellLauncher(), new FakeDialogService(), new FakeFreeSpace(),
                                        new FakeTimeProvider(), Ui, () => TestPlans.Ledger(), r => @"C:\AppData\backup"),
                r => verdict,
                _ => true,
                _ => null,
                Saved.Add));
            return shell;
        }
    }

    [Fact]
    public async Task Shell_RootsUnconfirmed_SetupThenCardAutoScanToReview()
    {
        var rig = new Rig();
        var shell = rig.Shell(rootsConfirmed: false);
        await shell.StartAsync();
        Assert.Equal(Stage.Setup, shell.Stage);
        Assert.False(shell.CanOpenSettings);

        ((SetupVm)shell.Current!).ConfirmCommand.Execute(null);
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        Assert.True(shell.Settings.RootsConfirmed);
        Assert.Equal(1, rig.Scans);
        Assert.StartsWith(@"E:\ · DJI Air 3S · serial 1A2B-3C4D", shell.CardChipText, StringComparison.Ordinal);
        Assert.True(shell.CanRescan);
        Assert.True(shell.CanUndoRedo);
        Assert.True(shell.CleanupEnabled);
    }

    [Fact]
    public async Task Shell_TwoDjiVolumes_StaysOnCardStage()
    {
        var rig = new Rig();
        rig.Cards.Add(CleanupFixture.Volume with { Root = @"F:\", Identity = new CardIdentity(0x5E6F7A8B, "DJI Internal", "exFAT", 64_000_000_000) });
        var shell = rig.Shell();
        await shell.StartAsync();

        Assert.Equal(Stage.Card, shell.Stage);
        Assert.Equal(2, shell.Card!.Rows.Count);
        Assert.True(shell.CanBrowse);
        Assert.Equal(0, rig.Scans);
    }

    [Fact]
    public async Task Shell_CommitDisablesRescanSettingsBrowseAndCleanup_BackRestores()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        shell.BeginOffload();
        Assert.Equal(Stage.Preflight, shell.Stage);
        Assert.Equal((1, 1), (rig.Offload.Lock.Holds, rig.Offload.Thumbnails.Paused));
        Assert.False(shell.CanRescan);
        Assert.False(shell.CanOpenSettings);
        Assert.False(shell.CanBrowse);
        Assert.False(shell.CanUndoRedo);
        Assert.Equal((false, "Wait until the offload finishes"), (shell.CleanupEnabled, shell.CleanupTooltip));
        await shell.RescanAsync();
        Assert.Equal(1, rig.Scans);

        shell.Preflight!.BackCommand.Execute(null);
        Assert.Equal(Stage.Review, shell.Stage);
        Assert.Equal((0, 0), (rig.Offload.Lock.Holds, rig.Offload.Thumbnails.Paused));
        Assert.True(shell.CanRescan);
    }

    [Fact]
    public async Task Shell_OffloadToVerdict_ReleasesLock_DoneReturnsToCard()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        shell.BeginOffload();
        await shell.StartCopyAsync();

        Assert.Equal(Stage.Verdict, shell.Stage);
        Assert.Equal((0, 0), (rig.Offload.Lock.Holds, rig.Offload.Thumbnails.Paused));   // the shell disposed the session
        Assert.Single(rig.Offload.Reports.Offload);
        Assert.True(shell.Verdict!.CleanupCommand.CanExecute(null));
        rig.Cards.Clear();
        shell.Verdict.DoneCommand.Execute(null);
        Assert.Equal(Stage.Card, shell.Stage);
    }

    [Fact]
    public async Task Shell_WriteProtectedCard_DisablesCleanupWithTooltip()
    {
        var rig = new Rig { WriteProtected = true };
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        Assert.Equal((false, "The card is write-protected (lock switch)"), (shell.CleanupEnabled, shell.CleanupTooltip));
        Assert.False(shell.CleanupCommand.CanExecute(null));
    }

    [Fact]
    public async Task Shell_CleanupRescansAndDoneReturnsToReviewOfTheRescannedCard()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);
        var before = shell.Review;

        shell.OpenCleanup(CleanupOrigin.Review);
        Assert.Equal(Stage.Cleanup, shell.Stage);
        Assert.False(shell.CanRescan);
        Assert.False(shell.CanUndoRedo);

        Assert.Equal(VerdictLevel.Safe, await shell.RescanForCleanupAsync());
        Assert.Equal(Stage.Cleanup, shell.Stage);
        Assert.NotSame(before, shell.Review);
        Assert.Equal(2, rig.Scans);

        shell.Cleanup!.BackCommand.Execute(null);
        Assert.Equal(Stage.Review, shell.Stage);
        Assert.Same(shell.Review, shell.Current);
    }

    [Fact]
    public async Task Shell_SettingsOpensFromReviewAndReturns()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        shell.OpenSettings();
        Assert.Equal(Stage.Settings, shell.Stage);
        shell.CloseSettings();
        Assert.Equal(Stage.Review, shell.Stage);
    }

    [Fact]
    public async Task Shell_UpdateLayout_SavesTheLayoutAndKeepsTheSettings()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();

        shell.UpdateLayout(512, 0.55);

        Assert.Equal(new LayoutSettings(512, 0.55), Assert.Single(rig.Saved).Layout);
        Assert.Equal(new LayoutSettings(512, 0.55), shell.Settings.Layout);
        Assert.Equal(TestPlans.VideoRoot, shell.Settings.VideoRoot);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Shell_*"`
Expected: build FAILS (CS0246 `ShellVm`, `ShellDeps`, `Stage`, `CleanupOrigin`).

- [ ] **Step 3: Implement**

```csharp
// src/UasSort.Review/Shell/ShellVm.cs
namespace UasSort.Review;

public enum Stage { Setup, Card, Scan, Review, Preflight, Copy, Verdict, Cleanup, Settings }

public enum CleanupOrigin { Review, Verdict }

/// <summary>Factories and facts the shell needs; Part 11's composition root builds them from Core and Platform.</summary>
public sealed record ShellDeps(
    SettingsLoad Settings,
    Func<SettingsLoad, SetupVm> CreateSetup,
    Func<CardStageVm> CreateCard,
    Func<Settings, ScanStageVm> CreateScan,
    Func<CardSource, PlanBase, ReviewVm> CreateReview,
    Func<ReviewVm, PreflightVm> CreatePreflight,
    Func<PreflightVm, CopyVm> CreateCopy,
    Func<ReviewVm, CommitResult?, VerdictVm> CreateVerdict,
    Func<CleanupOrigin, OffloadResult?, CleanupVm> CreateCleanup,
    Func<Settings, SettingsPageVm> CreateSettings,
    Func<ReviewVm, FormatVerdict> AuditNow,
    Func<CardSource, bool> CardPresent,
    Func<CardSource, string?> VolumeRefusal,
    Action<Settings> SaveSettings);

/// <summary>The stage machine behind MainWindow's Frame and TitleBar (Ref §9.1, §9.2).</summary>
public sealed partial class ShellVm : ObservableObject
{
    private readonly ShellDeps _deps;
    private (Stage Stage, object? Current)? _beforeSettings;
    private CommitResult? _lastResult;
    private SettingsPageVm? _settingsPage;

    public ShellVm(ShellDeps deps)
    {
        _deps = deps;
        Settings = deps.Settings.Settings;
        RescanCommand = new AsyncRelayCommand(RescanAsync, () => CanRescan);
        SettingsCommand = new RelayCommand(OpenSettings, () => CanOpenSettings);
        CleanupCommand = new RelayCommand(() => OpenCleanup(Stage == Stage.Verdict ? CleanupOrigin.Verdict : CleanupOrigin.Review), () => CleanupEnabled);
    }

    public Settings Settings { get; private set; }
    public CardSource? Source { get; private set; }
    public CardStageVm? Card { get; private set; }
    public ReviewVm? Review { get; private set; }
    public PreflightVm? Preflight { get; private set; }
    public CopyVm? Copy { get; private set; }
    public VerdictVm? Verdict { get; private set; }
    public CleanupVm? Cleanup { get; private set; }

    [ObservableProperty] public partial Stage Stage { get; private set; }
    [ObservableProperty] public partial object? Current { get; private set; }
    [ObservableProperty] public partial string? CardChipText { get; private set; }
    [ObservableProperty] public partial bool CanRescan { get; private set; }
    [ObservableProperty] public partial bool CanOpenSettings { get; private set; }
    [ObservableProperty] public partial bool CanBrowse { get; private set; }
    [ObservableProperty] public partial bool CanUndoRedo { get; private set; }
    [ObservableProperty] public partial bool CleanupEnabled { get; private set; }
    [ObservableProperty] public partial string? CleanupTooltip { get; private set; }
    [ObservableProperty] public partial bool IsScanning { get; private set; }

    public IAsyncRelayCommand RescanCommand { get; }
    public IRelayCommand SettingsCommand { get; }
    public IRelayCommand CleanupCommand { get; }

    public Task StartAsync()
    {
        if (!Settings.RootsConfirmed || _deps.Settings.Recovered) ShowSetup();
        else ShowCard();
        return Task.CompletedTask;
    }

    public async Task UseCardAsync(CardSource s)
    {
        Source = s;
        var scan = _deps.CreateScan(Settings);
        IsScanning = true;
        Go(Stage.Scan, scan);
        var b = await scan.RunAsync(s).ConfigureAwait(true);
        IsScanning = false;
        if (b is null)
        {
            if (scan.ErrorText is null) ShowCard();
            else UpdateFlags();
            return;
        }
        ShowReview(s, b);
    }

    public Task RescanAsync()
    {
        if (Stage is Stage.Preflight or Stage.Copy or Stage.Verdict or Stage.Cleanup or Stage.Setup) return Task.CompletedTask;
        if (Source is { } s) return UseCardAsync(s);
        ShowCard();
        return Task.CompletedTask;
    }

    /// <summary>CleanupVm's rescan after the deletes: the normal scan, a fresh Review, and the verdict level recomputed without an offload.</summary>
    public async Task<VerdictLevel> RescanForCleanupAsync()
    {
        var source = Source ?? throw new InvalidOperationException("No card to rescan");
        var scan = _deps.CreateScan(Settings);
        var b = await scan.RunAsync(source).ConfigureAwait(true)
                ?? throw new IOException(scan.ErrorText ?? "The rescan was cancelled");
        Review?.Dispose();
        Review = Wire(_deps.CreateReview(source, b));
        _lastResult = null;
        return _deps.AuditNow(Review).Level;
    }

    public void BeginOffload()
    {
        if (Review is not { CanOffload: true } review) return;
        var preflight = _deps.CreatePreflight(review);
        preflight.BackRequested += () =>
        {
            Preflight = null;
            Go(Stage.Review, Review);
        };
        Preflight = preflight;
        preflight.Open();
        Go(Stage.Preflight, preflight);
    }

    /// <summary>Start offload: the Copy page runs the CommitSession; its CommitResult (verdict, report, offload) goes to the Verdict page.</summary>
    public async Task StartCopyAsync()
    {
        if (Preflight is not { CanStart: true } preflight || Review is not { } review) return;
        var copy = _deps.CreateCopy(preflight);
        Copy = copy;
        Go(Stage.Copy, copy);
        var result = await copy.RunAsync().ConfigureAwait(true);
        if (result is null && copy.ErrorText is not null) return;
        _lastResult = result;
        ShowVerdict(review, result);
    }

    /// <summary>Remembers the splitter positions (Ref §9.2): Settings.Layout is saved at once through ShellDeps.SaveSettings.</summary>
    public void UpdateLayout(double timelineWidth, double mapHeightRatio)
    {
        var layout = new LayoutSettings(timelineWidth, mapHeightRatio);
        if (Settings.Layout == layout) return;
        Settings = Settings with { Layout = layout };
        _deps.SaveSettings(Settings);
    }

    public void OpenCleanup(CleanupOrigin origin)
    {
        if (!CleanupEnabled) return;
        var returnStage = origin == CleanupOrigin.Verdict ? Stage.Verdict : Stage.Review;
        var cleanup = _deps.CreateCleanup(origin, origin == CleanupOrigin.Verdict ? _lastResult?.Offload : null);
        cleanup.RescanRequested += () => _ = UseCardAsync(Source!);
        cleanup.Closed += () =>
        {
            var finished = cleanup.Step == CleanupStep.Result;
            Cleanup = null;
            if (finished || returnStage == Stage.Review) Go(Stage.Review, Review);
            else Go(Stage.Verdict, Verdict);
        };
        Cleanup = cleanup;
        Go(Stage.Cleanup, cleanup);
        cleanup.Open();
    }

    public void OpenSettings()
    {
        if (!CanOpenSettings) return;
        _beforeSettings = (Stage, Current);
        _settingsPage = _deps.CreateSettings(Settings);
        Go(Stage.Settings, _settingsPage);
    }

    public void CloseSettings()
    {
        if (_settingsPage is { } page)
        {
            Settings = page.Current;
            page.Dispose();
            _settingsPage = null;
        }
        if (_beforeSettings is { } back) Go(back.Stage, back.Current);
        _beforeSettings = null;
    }

    /// <summary>Device arrival or removal (WM_DEVICECHANGE, Part 11); ignored during Commit and Cleanup.</summary>
    public void DeviceChanged()
    {
        if (Stage == Stage.Card) Card?.Refresh();
        else UpdateFlags();
    }

    private void ShowSetup()
    {
        var setup = _deps.CreateSetup(_deps.Settings);
        setup.Confirmed += s =>
        {
            Settings = s;
            ShowCard();
        };
        Go(Stage.Setup, setup);
    }

    private void ShowCard()
    {
        Review?.Dispose();
        Review = null;
        Preflight?.Dispose();
        Preflight = null;
        Verdict = null;
        Source = null;
        CardChipText = null;
        var card = _deps.CreateCard();
        card.CardChosen += s => _ = UseCardAsync(s);
        Card = card;
        Go(Stage.Card, card);
        card.Refresh();
    }

    private void ShowReview(CardSource s, PlanBase b)
    {
        Review?.Dispose();
        Review = Wire(_deps.CreateReview(s, b));
        CardChipText = Review.CardChipText;
        Go(Stage.Review, Review);
    }

    private ReviewVm Wire(ReviewVm review)
    {
        review.OffloadRequested += BeginOffload;
        review.RescanRequested += () => _ = RescanAsync();
        review.VerdictRequested += () => ShowVerdict(review, null);
        review.UiActionRequested += label =>
        {
            if (string.Equals(label, "Open Settings", StringComparison.Ordinal)) OpenSettings();
            else if (string.Equals(label, "Rescan", StringComparison.Ordinal)) _ = RescanAsync();
        };
        return review;
    }

    private void ShowVerdict(ReviewVm review, CommitResult? result)
    {
        var verdict = _deps.CreateVerdict(review, result);
        Preflight?.Dispose();   // disposes the CommitSession: releases the offload lock and the thumbnail pause
        Preflight = null;
        verdict.DoneRequested += ShowCard;
        verdict.CleanupRequested += () => OpenCleanup(CleanupOrigin.Verdict);
        verdict.ShowPlanRequested += () =>
        {
            review.IsReadOnly = true;
            Go(Stage.Review, review);
        };
        Verdict = verdict;
        Go(Stage.Verdict, verdict);
    }

    private void Go(Stage stage, object? current)
    {
        Stage = stage;
        Current = current;
        UpdateFlags();
    }

    private void UpdateFlags()
    {
        var committing = Stage is Stage.Preflight or Stage.Copy;
        CanRescan = Stage is Stage.Card or Stage.Review or Stage.Scan && !IsScanning;
        CanOpenSettings = Stage is Stage.Card or Stage.Review;
        CanBrowse = Stage == Stage.Card;
        CanUndoRedo = Stage == Stage.Review && Review is { IsReadOnly: false };
        var (enabled, tooltip) = CleanupAvailability.For(new CleanupContext(committing, IsScanning, Source,
            Source is { } s && _deps.CardPresent(s), Source is { } s2 ? _deps.VolumeRefusal(s2) : null));
        if (Stage is Stage.Cleanup or Stage.Settings or Stage.Setup) enabled = false;
        CleanupEnabled = enabled;
        CleanupTooltip = tooltip;
        Verdict?.SetCleanupAvailability(enabled, tooltip);
        RescanCommand.NotifyCanExecuteChanged();
        SettingsCommand.NotifyCanExecuteChanged();
        CleanupCommand.NotifyCanExecuteChanged();
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj -- --filter-method "*Shell_*"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/UasSort.Review/Shell tests/UasSort.Review.Tests/ShellVmTests.cs
git commit -m "feat: shell stage machine with disabled controls during commit and cleanup" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```

---
### Task 10.24: Whole-suite run and analyzer sweep for Part 10

**Files:**
- No new files. Fix-ups only in files created by Tasks 10.1–10.23.

**Interfaces:**
- Consumes: everything above.
- Produces: a green solution with `TreatWarningsAsErrors`, and the trim/AOT analyzers quiet for `UasSort.Review` (`IsTrimmable`, `IsAotCompatible`).

- [ ] **Step 1: Build the solution**

Run: `dotnet build uas-sort.slnx -c Debug`
Expected: `Build succeeded` with 0 warnings. If a recommended-level analyzer fires in Review (for example CA1305, CA1307/CA1310, CA1822, CA1852, CA2016), fix it locally the way the rule asks (pass `CultureInfo.InvariantCulture` / a `StringComparison`, make the member static, seal the type, forward the token). If an IL trim/AOT warning fires, it can only come from reflection-based JSON; every map message must go through `MapJsonContext` (never `JsonSerializer.Serialize<T>(value)` without a type info). If the compiler rejects an initializer on an `[ObservableProperty]` partial property, move that value into the constructor.

- [ ] **Step 2: Run the Review tests, then the whole suite**

Run: `dotnet test --project tests/UasSort.Review.Tests/UasSort.Review.Tests.csproj`
Expected: PASS — every test of Tasks 10.1–10.23 (about 150 including theory rows), 0 failed, 0 skipped.

Run: `dotnet test --solution uas-sort.slnx`
Expected: PASS for every test project (Core, Review, Platform, and the rest already present); nothing earlier regresses.

From WSL: `tools/r.sh dotnet test --solution uas-sort.slnx`, then `tools/r.sh dotnet build-server shutdown`.

- [ ] **Step 3: Check the Review assembly stays WinUI-free and file-system-free**

Run: `grep -rnE "Microsoft\.UI|Windows\.UI|System\.IO\.File\b|Directory\.|FileStream|DateTime\.(Now|UtcNow|Today)|DateTimeOffset\.(Now|UtcNow)" src/UasSort.Review || echo "clean"`
Expected: `clean` (the BannedApiAnalyzers would also fail the build on the IO and clock members).

- [ ] **Step 4: Commit any fix-ups**

```bash
git add -A src/UasSort.Review tests/UasSort.Review.Tests tests/UasSort.Testing
git commit -m "chore: part 10 analyzer and suite fix-ups" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015Z1pwYTXofSxv2csCcXa4z"
```
(Skip the commit when there is nothing to commit.)

---

## Part 10 — Produces (summary)

Everything below is in `UasSort.Review` (`namespace UasSort.Review`, `net11.0`, no WinUI types) unless marked Testing/Tests.

| Area | Types and key members |
|---|---|
| Service seams | `IReviewLog.Warn`, `IFreeSpace.FreeBytes`, `ReviewServices(Ui, Dialogs, Shell, Thumbs, Drafts, Space, Time, Log)`, `UiProgress<T>` |
| Formatting | `Fmt` (`Miles`, `Size`, `ClipLength`, `Day`, `DayWithWeekday`, `DayYear`, `DateRange`, `Gap`, `Offset`, `Clock`, `Count`, `Serial`, `ModelName`, `CardChip`, `Drive`); zone names, abbreviations, offsets, distances and 15-minute rounding are Core's `ZoneNames` (`Region`, `Abbreviation`, `OffsetAt`, `IsUs`), `GeoMath.Haversine` and `DroneClock.Round15` (Part 04) — Review defines no `ZoneNames`, `GeoMath` or `Round15` |
| Clock texts | `ClockText` (`Banner` = `ClockSummary.Headline`, `MismatchInfoBar`, `SourceText`, `SourceGlyph`, `IsEstimated`, `TimeCell`, `TimeTooltip`, `ChipTooltip`, `WhyText`) |
| Sync | `IKeyed`, `CollectionSync.Sync` |
| Map | `HostToMap` (+ `MapInit`, `MapSetData`, `MapSelect`, `MapSetRadius`, `MapSetBase`, `MapSetTheme`, `MapFit`, `MapPing`), `MapToHost` (+ `MapReady`, `MapPong`, `MapClick`, `MapClickEmpty`, `MapContextMenu`, `MapTileError`, `MapBaseUnavailable`, `MapError`), `MapConfig`, `MapItem`, `MapGroup`, `MapJump`, `MapJsonContext` (internal; registers Core `GeoPointJsonConverter`), `MapBridge` (`Send`, `Dispatch`, `Received`, `SendData` throttled 10/s, `Rev`, `Serialize`, `Parse`, `ParseHostMessage`), `MapProjection` (`Palette`, `ImportedGrey`, `Color`, `Init`, `SetData`, `Select`, `Bbox`) |
| Review | `IReviewActions`, `PlanIndex` (`Candidates` = `Planner.AppendCandidates`), `ChipKind`, `ChipVm`, `QuickFixVm`, `ThumbVm`, `SuggestionVm` (ToString = text), `RetargetKind`, `RetargetOptionVm`, `BoundaryChipVm`, `DaySplitBannerVm`, `TimelineEntryVm`, `GroupCardVm`, `FoldedRunVm`, `ClipRowVm`, `VideosTabVm`, `IssueVm`, `IssuesVm`, `Footer.Text`, `ITuningHost`, `TuningVm` (`IdleCommit` 400 ms), `PhotoDayVm`, `PhotoTileVm`, `PhotosTabVm`, `OtherRowVm`, `OtherSectionVm`, `OtherTabVm`, `InfoSeverity`, `InfoBarVm`, `DraftOffer`, `DraftOffers.Find`, `ReviewVm` (built on `new PlanSession(b, deriver, tuning, time)`; plan application with revision guard, InfoBars incl. the clock banner `ClockSummary.Headline` and per-session clock-mismatch dismissal, "Nothing new on this card", offload gating, edits, `CanUndo`/`CanRedo` mirroring `PlanSession`, rename/retarget/browse validation, drafts 1 s, `HandleKey`, `MapBase`, `GoTo`, `MoveTargets`, `MoveSelectedToGroupAsync`), `ReviewKey`, `KeyMods`, `KeyFocus` |
| Decisions | `DecisionTarget`, `DecisionTargets` (`For`, `ForEntry`; keys via Core `FileKey.OfPath`), `IDecisionService`, `LedgerDecisionService` (revokes via `VerdictDecisions.Revokes`) |
| Stages | `CardRowVm`, `CardStageVm` (never auto-picks among several DJI volumes), `ScanStageVm` (`PhaseTextFor`), `SetupVm`, `LedgerStatusText.For`, `PreviousRootVm`, `SettingsPageVm` (`SaveDelay` 500 ms, video-root [Copy]/[Start empty] via Part 05 `VideoRootChange`, photo root via `SettingsEdits.ChangePhotoRoot`) |
| Commit | `CommitPorts(Drafts, Dialogs, Ui)`, `AckVm(AckKey)`, `PreflightVm` (over Part 07's `CommitSession`: `Open` = `begin(plan)`, `Session`, `CanStart` = `PreflightAcks.CanStart`, `StartAsync` → `CommitResult`, `Dispose` = `Session.Dispose()`), `CopyVm` (`RunAsync` → `CommitResult?`, `PhaseName`), `VerdictPorts(Ledger, Machine, Time, Dialogs, Shell, Eject, Reaudit)`, `NotCopiedRowVm` (wraps Core `NotCopiedRow`; kind is Core `NotCopiedKind`), `NotCopiedDayVm`, `EjectVm`, `VerdictGroupRowVm`, `VerdictVm` (rows, day selection, checks, confirmation, records and revokes from Part 07's `VerdictDecisions`; `LevelName`, `CategoryName`, `SetCleanupAvailability`). No `CommitEngine`, no Review `NotCopiedKind` |
| Cleanup | `CleanupContext`, `CleanupAvailability.For`, `RowDecision`, `CleanupRowVm` (ToString = text), `CleanupResultVm`, `CleanupPreparation`, `CleanupEngine` (`Run` bound to exactly `CleanupExecutor.RunAsync(confirmed, env, progress, ct)`), `CleanupStep`, `KeptGroupVm`, `CleanupReports.Build`, `CleanupVm` (`PickDate` → `DateOnly.FromDateTime(picked.DateTime)`, HaveFree default, texts from Part 08's `CleanupTexts`, row changes through `CleanupRowOps`, review list with undecided rows, fingerprint-bound acknowledgements, "Delete N files (X GB)") |
| Shell | `Stage`, `CleanupOrigin`, `ShellDeps` (`CreateVerdict: Func<ReviewVm, CommitResult?, VerdictVm>`, `SaveSettings: Action<Settings>`), `ShellVm` (disposes `Preflight` when the verdict is shown, keeps the last `CommitResult` and hands its `Offload` to `CreateCleanup`, `UpdateLayout`) |
| Testing (`tests/UasSort.Testing`) | added to Part 06's `GatedPlanDeriver` (same file, `Arm`/`Gate`/`CallCount` unchanged): `Hold`, `Calls`, `CallStartedAsync`, `Release(int)`, `ReleaseAll`, and `DeriveCall` |
| Tests-only (`tests/UasSort.Review.Tests`) | `FakeUiDispatcher`, `FakeDialogService`, `FakeShellLauncher`, `FakeDraftStore`, `FakeFreeSpace`, `ListLog`, `Fake.Services`/`Fake.Ledger`, `Eventually`, `RecordingActions`, `FakeSettingsStore`, `FakeEjectOk`, `PlanClip`, `TestPlans`, `ScriptedDeriver`, `ReviewHarness`, `CleanupFixture`. Shared fakes come from `UasSort.Testing`: `FakeThumbnails`, `FakeLedgerStore`/`FakeLedgerWriter`, `FakeFileOps`, `FakeOffloadLock`, `FakePowerRequest`, `MemReportStore`, `FakeVolumeProvider`, `FakeFileSystem`/`FakeLayout`, `CleanupPlanFixtures`, and `UasSort.Testing.Offload` (`OffloadPlanBuilder`, `OffloadRig`) |

Part 11 binds: `CommitSession` via `ShellDeps.CreatePreflight`, `CleanupEngine`/`CleanupPreparation`, `ShellDeps`, stage-VM delegates, `VerdictPorts.Reaudit`, `ReviewServices`, `IFreeSpace`, `IReviewLog`, `MapBridge`.
