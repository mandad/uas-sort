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
