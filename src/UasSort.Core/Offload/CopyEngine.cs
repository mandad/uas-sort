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

    private static Step CopyOne(OffloadBatch batch, CopyJob job, ICardReader card, IFileOps files, HashSet<string> ensured,
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
                if (!DeleteQuietly(files, temp)) return fault with { Stop = fault.Stop ?? StopReason.InternalSafetyStop };
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
                return new Step(new ConflictAtRename(job), DeleteQuietly(files, temp) ? null : StopReason.InternalSafetyStop);
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
            _ = DeleteQuietly(files, temp);   // the stop is already set
            return new Step(new Failed(job, phase, e.Message), StopReason.InternalSafetyStop);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _ = DeleteQuietly(files, temp);   // the stop is already set
            return new Step(new Cancelled(job), StopReason.Cancelled);
        }
        catch (Exception e) when (Failures.IsIo(e))
        {
            var deleted = DeleteQuietly(files, temp);
            var step = DestinationError(job, files, phase, e);
            return deleted ? step : step with { Stop = step.Stop ?? StopReason.InternalSafetyStop };
        }
    }

    // Completed in Task 07.9 (retry once on a mismatch).
    private static CopyResult CopyVerified(OffloadBatch batch, CopyJob job, ICardReader card, IFileOps files, ProgressMeter meter,
                                    CancellationToken ct, ref CopyPhase phase, ref string? temp)
    {
        var once = CopyOnce(batch, job, card, files, meter, ct, ref phase, ref temp);
        if (!once.Mismatch) return once;
        return new CopyResult(default, default, false, new Step(new Failed(job, CopyPhase.Verify, "The copy didn't match the card"), null));
    }

    /// <summary>Steps 4–7 once: create the temp, copy through the hash, flush, verify.</summary>
    private static CopyResult CopyOnce(OffloadBatch batch, CopyJob job, ICardReader card, IFileOps files, ProgressMeter meter,
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

    private static DataResult CopyData(OffloadBatch batch, CopyJob job, ICardReader card, Stream dest, ProgressMeter meter,
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

    // Completed in Task 07.9 (disk full → DestinationFull; volume gone → DestinationLost).
    private static Step DestinationError(CopyJob job, IFileOps files, CopyPhase phase, Exception error)
        => new(new Failed(job, phase, error.Message), null);

    // Completed in Task 07.10 (non-NTFS / removable destinations).
    private static ImmutableArray<string> FlushDestinations(IFileOps files, List<string> renamed) => [];

    /// <summary>Deletes this run's temp; false only when the guard refused the delete.</summary>
    private static bool DeleteQuietly(IFileOps files, string? temp)
    {
        if (temp is null) return true;
        try
        {
            files.DeleteOwnTemp(temp);
            return true;
        }
        catch (UnsafeIoException)
        {
            // a refused delete of our own temp is an internal safety stop (Ref §10.3)
            return false;
        }
        catch (Exception e) when (Failures.IsIo(e))
        {
            // unreachable destination: the temp stays and the next preflight lists it (Ref §10.3 invariants)
            return true;
        }
    }
}
