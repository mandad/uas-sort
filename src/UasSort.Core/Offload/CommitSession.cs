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
        StopReason setupStop = StopReason.LedgerWriteFailed;
        OffloadResult result;
        try
        {
            env.Ledger.EnsureFolder();
            env.Ledger.SnapshotToBackup(Batch.RunId);
            foreach (var stale in Preflight.StaleTemps) DeleteQuietly(stale);
            writer = env.Ledger.OpenOwn();
        }
        catch (UnsafeIoException)
        {
            writer?.Dispose();
            writer = null;
            ledgerComplete = false;
            setupStop = StopReason.InternalSafetyStop;
        }
        catch (Exception e) when (Failures.IsIo(e))
        {
            writer?.Dispose();
            writer = null;
            ledgerComplete = false;
        }

        if (writer is null)
        {
            var now = Now();
            result = new OffloadResult(Batch.RunId, [.. Batch.Jobs.Select(j => (CopyOutcome)new NotStarted(j))], setupStop,
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
        catch (UnsafeIoException)
        {
            return false;
        }
        catch (Exception e) when (Failures.IsIo(e))
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
