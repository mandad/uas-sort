// src/UasSort.Core/Cleanup/Photos/PhotoCleanupExecutor.cs
namespace UasSort.Core.Cleanup;

/// <summary>Runs a confirmed Picture Offload plan (spec 2026-10-04 §5–§6): offload lock, keep-awake, ledger preparation (snapshot before
/// the first append), the recycler (its factory re-checks the photo root), then per row: re-check against the review, move to the
/// Recycle Bin, and one photoDelete record per file right after its move. Cancellable between rows.</summary>
public static class PhotoCleanupExecutor
{
    private static readonly IReadOnlySet<string> NoExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static Task<PhotoCleanupResult> RunAsync(ConfirmedPhotoCleanupPlan confirmed, PhotoCleanupEnvironment env,
                                                    IProgress<PhotoCleanupProgress> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(progress);
        return Task.Run(() => new Run(confirmed, env, progress, ct).Execute(), CancellationToken.None);
    }

    private sealed class Run
    {
        private readonly ConfirmedPhotoCleanupPlan _confirmed;
        private readonly PhotoCleanupEnvironment _env;
        private readonly IProgress<PhotoCleanupProgress> _progress;
        private readonly CancellationToken _ct;
        private readonly ImmutableArray<PhotoRow> _items;
        private readonly PhotoCleanupOutcome?[] _outcomes;
        private readonly List<string> _unrecorded = [];
        private readonly string _runId = Guid.NewGuid().ToString("N");
        private readonly DateTime _start;
        private readonly long _bytesTotal;
        private PhotoCleanupStop? _stop;
        private string? _stopDetail;
        private ILedgerWriter? _writer;
        private IPhotoRootRecycler? _recycler;
        private int _itemsDone;
        private long _bytesDone;
        private DateTime _lastReport = DateTime.MinValue;

        public Run(ConfirmedPhotoCleanupPlan confirmed, PhotoCleanupEnvironment env, IProgress<PhotoCleanupProgress> progress, CancellationToken ct)
        {
            _confirmed = confirmed;
            _env = env;
            _progress = progress;
            _ct = ct;
            _items = confirmed.Items;
            _outcomes = new PhotoCleanupOutcome?[_items.Length];
            _start = env.Clock.GetUtcNow().UtcDateTime;
            _bytesTotal = _items.Sum(r => r.Item.Bytes);
        }

        private PhotoCleanupPlan Plan => _confirmed.Plan;

        public PhotoCleanupResult Execute()
        {
            var held = new Stack<IDisposable>();
            var lk = _env.Lock.TryAcquire();
            if (lk is null)
            {
                _stop = PhotoCleanupStop.OffloadLockHeld;
                return Complete();
            }
            held.Push(lk);
            try
            {
                held.Push(_env.Power.KeepSystemAwake("Cleaning up Picture Offload"));
                if (!PrepareLedger(held)) return Complete();
                if (!OpenRecycler(held)) return Complete();
                if (!RecycleBinHoldsTheRun()) return Complete();
                for (var i = 0; i < _items.Length && _stop is null; i++)
                {
                    if (_ct.IsCancellationRequested)
                    {
                        _stop = PhotoCleanupStop.Cancelled;
                        break;
                    }
                    _outcomes[i] = One(_items[i]);
                    _itemsDone++;
                    Report(null, force: false);
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
            if (status.State is LedgerFolderState.CloudOnly or LedgerFolderState.Unwritable or LedgerFolderState.VideoRootMissing
                             or LedgerFolderState.Unlistable)
            {
                _stop = PhotoCleanupStop.LedgerUnavailable;
                return false;
            }
            try
            {
                _env.Ledger.EnsureFolder();
                _env.Ledger.SnapshotToBackup(_runId);                     // spec §6 Snapshot: before the first append, as for the offload
                _writer = _env.Ledger.OpenOwn();
                held.Push(_writer);
                return true;
            }
            catch (UnsafeIoException) { _stop = PhotoCleanupStop.InternalSafetyStop; return false; }
            catch (IOException) { _stop = PhotoCleanupStop.LedgerUnavailable; return false; }
            catch (UnauthorizedAccessException) { _stop = PhotoCleanupStop.LedgerUnavailable; return false; }
        }

        private bool OpenRecycler(Stack<IDisposable> held)
        {
            try
            {
                _recycler = _env.Recyclers.Open(_confirmed);
                held.Push(_recycler);
                return true;
            }
            catch (UnsafeIoException) { _stop = PhotoCleanupStop.RecyclerRefused; return false; }
            catch (IOException) { _stop = PhotoCleanupStop.RecyclerRefused; return false; }
        }

        /// <summary>Branch-2 ruling: before the first move, the photo root volume's Recycle Bin must hold every byte chosen on top of what
        /// it holds now, and must not remove files at once; a bin that can't be checked refuses too. Never starts moving otherwise.</summary>
        private bool RecycleBinHoldsTheRun()
        {
            try
            {
                _stopDetail = PhotoCleanupRules.RecycleBinRefusal(_recycler!.Capacity(Plan.PhotoRoot), _bytesTotal);
            }
            catch (IOException e)
            {
                _stopDetail = $"The Recycle Bin for {Plan.PhotoRoot} couldn't be checked ({e.Message}). Nothing was moved.";
            }
            if (_stopDetail is null) return true;
            _stop = PhotoCleanupStop.RecycleBinTooSmall;
            return false;
        }

        private PhotoCleanupOutcome One(PhotoRow row)
        {
            var item = row.Item;
            Report(item.RelPath, force: true);
            if (Changed(item) is { } why) return new PhotoSkippedChanged(item.RelPath, why);
            return item.Kind == PhotoItemKind.Set ? RecycleSet(row) : RecyclePhoto(row);
        }

        /// <summary>The JPG twin first, the DNG (the unit's proof) last; a record right after each file's move.</summary>
        private PhotoCleanupOutcome RecyclePhoto(PhotoRow row)
        {
            var item = row.Item;
            var done = new List<string>();
            var notInBin = new List<string>();
            for (var k = item.Members.Length - 1; k >= 0; k--)
            {
                var m = item.Members[k];
                var full = Full(m.RelPath);
                var result = Checked(Recycle(full), full);
                if (result is RecycleError e)
                    return done.Count == 0 ? new PhotoRecycleFailed(item.RelPath, m.RelPath, e.Code, e.Message, e.NotRecyclable)
                                           : Partly(item, done, notInBin, e.Message);
                if (result is RecycleNotInBin) notInBin.Add(m.RelPath);
                done.Add(m.RelPath);
                _bytesDone += m.Size;
                if (!Append(row, m))
                    return done.Count == item.Members.Length ? Recycled(item, notInBin)
                                                             : Partly(item, done, notInBin, "the history couldn't be written; stopped");
            }
            return Recycled(item, notInBin);
        }

        /// <summary>Deferred minor P.11: the shell reported a delete that left nothing in the Recycle Bin. Gone: it stays RecycleNotInBin
        /// (recorded, reported "removed, not in the Recycle Bin"); still there (or unknown): kept, as a RecycleError.</summary>
        private RecycleResult Checked(RecycleResult result, string fullPath)
            => result is RecycleNotInBin && !Gone(fullPath)
                ? new RecycleError(StillThere, "Windows reported it removed, but it is still in Picture Offload; kept", false)
                : result;

        private const int StillThere = unchecked((int)0x80004005);   // E_FAIL

        /// <summary>A set folder moves as one unit; then one record per member. When the shell fails part-way through the folder, the
        /// members already gone are recorded and reported like a partly moved photo (spec §6: never silent).</summary>
        private PhotoCleanupOutcome RecycleSet(PhotoRow row)
        {
            var item = row.Item;
            var folder = Full(item.RelPath);
            var raw = Recycle(folder);
            var result = Checked(raw, folder);
            if (result is RecycleError e)
            {
                var gone = new List<string>();
                var recording = true;
                foreach (var m in item.Members)
                {
                    if (!Gone(Full(m.RelPath))) continue;
                    gone.Add(m.RelPath);
                    _bytesDone += m.Size;
                    if (!recording) _unrecorded.Add(Full(m.RelPath));            // after a failed append: named as unrecorded, never silent
                    else if (!Append(row, m)) recording = false;
                }
                return gone.Count == 0 ? new PhotoRecycleFailed(item.RelPath, item.RelPath, e.Code, e.Message, e.NotRecyclable)
                                       : Partly(item, gone, raw is RecycleNotInBin ? gone : [], e.Message);
            }
            List<string> notInBin = result is RecycleNotInBin ? [.. item.Members.Select(m => m.RelPath)] : [];
            _bytesDone += item.Bytes;
            for (var k = 0; k < item.Members.Length; k++)
            {
                if (Append(row, item.Members[k])) continue;
                for (var rest = k + 1; rest < item.Members.Length; rest++) _unrecorded.Add(Full(item.Members[rest].RelPath));
                break;
            }
            return Recycled(item, notInBin);
        }

        private RecycleResult Recycle(string fullPath)
        {
            try
            {
                return _recycler!.Recycle(fullPath);
            }
            catch (UnsafeIoException e)
            {
                _stop = PhotoCleanupStop.InternalSafetyStop;
                return new RecycleError(-1, "refused by the safety guard: " + e.Message, false);
            }
            catch (IOException e)
            {
                return new RecycleError(e.HResult, e.Message, false);
            }
#pragma warning disable CA1031 // any other recycler failure (a COM wrapper's exception rethrown by the STA worker) must end in a result and a report, never a faulted run
            catch (Exception e)
            {
                return new RecycleError(e.HResult, e.Message, false);
            }
#pragma warning restore CA1031
        }

        /// <summary>Whether a member is no longer at its path (attributes only); unknown (a failed stat) counts as still there.</summary>
        private bool Gone(string fullPath)
        {
            try
            {
                return _recycler!.Stat(fullPath) is null;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private bool Append(PhotoRow row, PhotoMember m)
        {
            var full = Full(m.RelPath);
            try
            {
                _writer!.Append(new PhotoDeleteRecord(LedgerCodec.Version, Guid.NewGuid().ToString("N"), _env.Machine, _runId,
                    _env.Clock.GetUtcNow().UtcDateTime, m.Name, m.Size, full, m.CaptureUtc,
                    row.Item.Kind == PhotoItemKind.Set ? row.Item.SetName : null,
                    PhotoDeleteRecords.Evidence(_confirmed.EvidenceOf(row)), PhotoDeleteRecords.Mode(Plan.Request.Mode), Plan.Request.Cutoff));
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _stop = PhotoCleanupStop.LedgerWriteFailed;
                _unrecorded.Add(full);
                return false;
            }
        }

        /// <summary>Spec §5: the item still exists with the size and mtime seen at review (a set: the same member list, sizes and mtimes, and
        /// no subfolder). Attributes and listings only; nothing is opened.</summary>
        private string? Changed(PhotoItem item)
        {
            try
            {
                if (item.Kind == PhotoItemKind.Photo)
                {
                    foreach (var m in item.Members)
                    {
                        var st = _recycler!.Stat(Full(m.RelPath));
                        if (st is null) return $"{m.Name} is no longer in Picture Offload";
                        if (st.IsDirectory || st.Size != m.Size || st.MtimeUtc != m.MtimeUtc) return $"{m.Name} changed since review";
                    }
                    return null;
                }
                var folder = Full(item.RelPath);
                var dir = _recycler!.Stat(folder);
                if (dir is null) return $"{item.RelPath} is no longer in Picture Offload";
                if (!dir.IsDirectory) return $"{item.RelPath} changed since review";
                var listing = _env.Lister.Enumerate(folder, recurse: true, NoExcludes);
                if (!listing.Errors.IsEmpty) return $"{item.RelPath} couldn't be listed again";
                if (listing.Entries.Any(e => e.IsDirectory)) return $"{item.RelPath} changed since review (it now holds a folder)";
                var now = listing.Entries.ToDictionary(e => PathRules.FileName(e.FullPath), StringComparer.OrdinalIgnoreCase);
                if (now.Count != item.Members.Length)
                    return $"{item.RelPath} changed since review ({now.Count} files now, {item.Members.Length} at review)";
                foreach (var m in item.Members)
                    if (!now.TryGetValue(m.Name, out var e) || e.Size != m.Size || e.MtimeUtc != m.MtimeUtc)
                        return $"{m.Name} in {item.RelPath} changed since review";
                return null;
            }
            catch (IOException e)
            {
                return "it couldn't be checked again: " + e.Message;
            }
        }

        private string Full(string relPath) => PhotoCleanupPaths.Full(Plan.PhotoRoot, relPath);

        private static PhotoRecycled Recycled(PhotoItem item, List<string> notInBin)
            => new(item.RelPath, item.Members.Length, item.Bytes) { NotInRecycleBin = [.. notInBin] };

        private static PhotoPartlyRecycled Partly(PhotoItem item, List<string> done, List<string> notInBin, string why)
            => new(item.RelPath, [.. done],
                   [.. item.Members.Select(m => m.RelPath).Where(r => !done.Contains(r, StringComparer.OrdinalIgnoreCase))], why)
            {
                NotInRecycleBin = [.. notInBin],
            };

        private void Report(string? current, bool force)
        {
            var now = _env.Clock.GetUtcNow().UtcDateTime;
            if (!force && now - _lastReport < TimeSpan.FromMilliseconds(100)) return;
            _lastReport = now;
            _progress.Report(new PhotoCleanupProgress(_itemsDone, _items.Length, _bytesDone, _bytesTotal, current));
        }

        private PhotoCleanupResult Complete()
        {
            for (var i = 0; i < _items.Length; i++) _outcomes[i] ??= new PhotoNotStarted(_items[i].Item.RelPath);
            _progress.Report(new PhotoCleanupProgress(_itemsDone, _items.Length, _bytesDone, _bytesTotal, null));
            return new PhotoCleanupResult(_runId, _confirmed, [.. _outcomes.Select(o => o!)], _stop, [.. _unrecorded], _start,
                                          _env.Clock.GetUtcNow().UtcDateTime)
            {
                StopDetail = _stopDetail,
            };
        }
    }
}
