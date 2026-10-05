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

    /// <summary>One proof of the evidence re-check: a video needs its library listing; a photo listed at plan time needs a listing or a
    /// verified ledger record; a HistoryOnly photo needs the record. An unrevoked photoDelete counts as a verified record for photos
    /// (Picture Offload cleanup, spec 2026-10-04 §6).</summary>
    internal static bool ProofHolds(ItemKind kind, FileProof p, FreshEvidence fresh, LedgerSnapshot ledger)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(fresh);
        ArgumentNullException.ThrowIfNull(ledger);
        var listed = fresh.ListedFolder(p.Key) is not null;
        var verified = (ledger.Files.TryGetValue(p.Key, out var lf) && lf.Verify is VerifyKind.Unbuffered or VerifyKind.Cached)
                       || (kind != ItemKind.Video && ledger.PhotoDeletes.ContainsKey(p.Key));
        return kind == ItemKind.Video ? listed : p.ListedFolder is not null ? listed || verified : verified;
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
            if (status.State is LedgerFolderState.CloudOnly or LedgerFolderState.Unwritable or LedgerFolderState.VideoRootMissing
                             or LedgerFolderState.Unlistable)
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
                if (!ProofHolds(c.Kind, p, _fresh!, _ledger!))
                    return (p.CardRelPath, c.Kind == ItemKind.Video ? "no longer in the library listing"
                                                                    : "no longer in the library listing or the history");
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

        private static PartiallyDeleted Partial(CleanupCandidate c, List<string> done, string why)
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
            string? closingError = null;                                          // first failed closing read; the Stop stays as it is
            try
            {
                var relisted = _env.Reader.Relist();
                if (!relisted.Errors.IsEmpty)
                    closingError = $"the card couldn't be listed (Win32 error {relisted.Errors[0].Win32Error})";
                var listed = relisted.Entries.Where(e => !e.IsDirectory)
                    .Select(e => CleanupPaths.Rel(e.RelPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                stillListed = [.. _deleted.Where(listed.Contains)];
            }
            catch (Exception e) when (Failures.IsIo(e)) { closingError = e.Message; }   // card gone: the rescan shows the truth
            try { after = _env.Reader.Space(); }
            catch (Exception e) when (Failures.IsIo(e)) { closingError ??= e.Message; } // card gone: keep SpaceBefore
            Report(null, null, force: true);
            return new CleanupResult(_runId, _confirmed, [.. _outcomes.Select(o => o!)], _stop, after, stillListed,
                                     _start, _env.Clock.GetUtcNow().UtcDateTime,
                                     closingError is null ? null : $"Couldn't re-read {OffloadPaths.DriveLabel(_plan.CardRoot)} after cleanup: {closingError}");
        }
    }
}
