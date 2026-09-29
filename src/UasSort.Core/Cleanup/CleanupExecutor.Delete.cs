// src/UasSort.Core/Cleanup/CleanupExecutor.Delete.cs
namespace UasSort.Core.Cleanup;

public static partial class CleanupExecutor
{
    private sealed partial class Run
    {
        /// <summary>Evidence re-check, then deletes in delete order (primary last); Task 08.12 adds the per-file checks and failures.</summary>
        private CleanupOutcome DeleteUnit(CleanupCandidate c)
        {
            if (c.Eligibility == CleanupEligibility.Evidence && EvidenceGone(c) is { } gone)   // step 2
                return new SkippedEvidenceGone(c.Unit, gone.Path, gone.Why);
            var done = new List<string>();
            long bytes = 0;
            for (var j = 0; j < c.Files.Length; j++)
            {
                var f = c.Files[j];
                var rel = CleanupPaths.Rel(f.RelPath);
                if (_eraser!.DeleteFile(f.RelPath) is EraseError err)
                    return done.Count == 0 ? new CleanupFailed(c.Unit, rel, err.Win32Error, err.Message) : Partial(c, done, err.Message);
                done.Add(rel);
                bytes += CleanupPaths.Allocated(f.Size, _plan.SpaceBefore.ClusterBytes);
                MarkDeleted(c, f);
                if (!AppendRecord(c, f))                                          // step 4: right after the delete
                    return j == c.Files.Length - 1 ? new Deleted(c.Unit, done.Count, bytes, false)
                                                   : Partial(c, done, "the ledger could not be written");
            }
            return new Deleted(c.Unit, done.Count, bytes, RemoveSetFolder(c));
        }
    }
}
