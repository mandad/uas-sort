// src/UasSort.Core/Cleanup/CleanupExecutor.Delete.cs
namespace UasSort.Core.Cleanup;

public static partial class CleanupExecutor
{
    private enum FileCheck { Ok, Changed, Swapped, Gone }

    private sealed partial class Run
    {
        /// <summary>Ref §10.6: steps 0–1 for every file and step 2 once; then steps 0, 1, 3, 4 per file in delete order.</summary>
        private CleanupOutcome DeleteUnit(CleanupCandidate c)
        {
            foreach (var f in c.Files)
            {
                var pre = Verify(f);
                if (pre.Result != FileCheck.Ok) return Stopped(c, [], f, pre);
            }

            if (c.Eligibility == CleanupEligibility.NotInLibrary)
            {
                if (!_confirmed.NotInLibraryConfirmed.Contains(c.Unit))           // unreachable through Confirm
                {
                    _stop = CleanupStop.InternalSafetyStop;
                    return new CleanupFailed(c.Unit, CleanupPaths.Rel(c.Files[^1].RelPath), 0, "no confirmation for a unit not in the library");
                }
            }
            else if (c.Eligibility != CleanupEligibility.Evidence)                // unreachable through Confirm
            {
                _stop = CleanupStop.InternalSafetyStop;
                return new CleanupFailed(c.Unit, CleanupPaths.Rel(c.Files[^1].RelPath), 0, "not deletable");
            }
            else if (EvidenceGone(c) is { } gone)
                return new SkippedEvidenceGone(c.Unit, gone.Path, gone.Why);

            var done = new List<string>();
            long bytes = 0;
            for (var j = 0; j < c.Files.Length; j++)
            {
                var f = c.Files[j];
                var rel = CleanupPaths.Rel(f.RelPath);
                if (j > 0 && _ct.IsCancellationRequested)                          // Cancel is checked between files
                {
                    _stop = CleanupStop.Cancelled;
                    return Partial(c, done, "cancelled");
                }
                var check = Verify(f);                                            // steps 0 and 1
                if (check.Result != FileCheck.Ok) return Stopped(c, done, f, check);

                EraseResult result;
                try { result = _eraser!.DeleteFile(f.RelPath); }                  // step 3
                catch (UnsafeIoException ex)
                {
                    _stop = CleanupStop.InternalSafetyStop;
                    return done.Count == 0 ? new CleanupFailed(c.Unit, rel, 0, ex.Message) : Partial(c, done, ex.Message);
                }
                if (result is EraseError err)
                {
                    _stop = err.Win32Error switch
                    {
                        ErrorWriteProtect => CleanupStop.WriteProtected,
                        ErrorNotReady or ErrorDeviceNotConnected => CleanupStop.CardRemoved,
                        _ => IdentityGone() ? CleanupStop.CardRemoved : (CleanupStop?)null,   // access denied, sharing: this unit only
                    };
                    return done.Count == 0 ? new CleanupFailed(c.Unit, rel, err.Win32Error, err.Message) : Partial(c, done, err.Message);
                }

                done.Add(rel);
                bytes += CleanupPaths.Allocated(f.Size, _plan.SpaceBefore.ClusterBytes);
                MarkDeleted(c, f);
                if (!AppendRecord(c, f))                                          // step 4: right after the delete
                    return j == c.Files.Length - 1 ? new Deleted(c.Unit, done.Count, bytes, false)
                                                   : Partial(c, done, "the ledger could not be written");
            }
            return new Deleted(c.Unit, done.Count, bytes, RemoveSetFolder(c));
        }

        private CleanupOutcome Stopped(CleanupCandidate c, List<string> done, CardEntry f,
                                       (FileCheck Result, CardIdentity? Now, long? Size, DateTime? Mtime) check)
        {
            var rel = CleanupPaths.Rel(f.RelPath);
            switch (check.Result)
            {
                case FileCheck.Gone:
                    _stop = CleanupStop.CardRemoved;
                    return done.Count == 0 ? new CleanupFailed(c.Unit, rel, ErrorNotReady, "The card was removed.")
                                           : Partial(c, done, "the card was removed");
                case FileCheck.Swapped:
                    _stop = CleanupStop.CardSwapped;
                    return done.Count == 0 ? new CleanupCardSwapped(c.Unit, check.Now!)
                                           : Partial(c, done, "a different card is in the drive");
                default:
                    return done.Count == 0 ? new SkippedChanged(c.Unit, rel, check.Size, check.Mtime)
                                           : Partial(c, done, $"{CleanupPaths.Name(rel)} changed since the plan");
            }
        }

        /// <summary>Step 0 (identity) and step 1 (the file exists with the planned size and mtime).</summary>
        private (FileCheck Result, CardIdentity? Now, long? Size, DateTime? Mtime) Verify(CardEntry f)
        {
            CardIdentity now;
            try { now = _env.Reader.CurrentIdentity(); }
            catch (IOException) { return (FileCheck.Gone, null, null, null); }
            if (now != _env.Pinned) return (FileCheck.Swapped, now, null, null);
            try
            {
                var e = _env.Reader.Stat(f.RelPath);
                return e.Size == f.Size && e.MtimeUtc == f.MtimeUtc
                    ? (FileCheck.Ok, now, null, null)
                    : (FileCheck.Changed, now, e.Size, e.MtimeUtc);
            }
            catch (FileNotFoundException) { return (FileCheck.Changed, now, null, null); }
            catch (DirectoryNotFoundException) { return (FileCheck.Changed, now, null, null); }
            catch (IOException) { return IdentityGone() ? (FileCheck.Gone, null, null, null) : (FileCheck.Changed, now, null, null); }
        }

        private bool IdentityGone()
        {
            try { _ = _env.Reader.CurrentIdentity(); return false; }
            catch (IOException) { return true; }
        }
    }
}
