// src/UasSort.Core/Cleanup/CleanupPlanner.Build.cs
namespace UasSort.Core.Cleanup;

public static partial class CleanupPlanner
{
    private enum RowState { Keep, Delete, Undecided }

    public static CleanupPlan Build(CleanupInputs inputs, ImmutableArray<CleanupCandidate> candidates, CleanupRequest request,
                                    CleanupRows rows, IReadOnlySet<ItemId> firstShown)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(firstShown);

        var ordered = candidates.OrderBy(c => c.CaptureUtc).ThenBy(c => c.Unit.CardRelPath, StringComparer.Ordinal).ToImmutableArray();
        var on = request.IncludeNotInLibrary;

        RowState Row(CleanupCandidate c) =>
            rows.Keep.Contains(c.Unit) ? RowState.Keep
            : rows.Delete.Contains(c.Unit) ? RowState.Delete
            : firstShown.Contains(c.Unit) ? (c.TickedForOffload ? RowState.Keep : RowState.Delete)
            : RowState.Undecided;
        static bool IsNil(CleanupCandidate c) => c.Eligibility == CleanupEligibility.NotInLibrary;
        bool Deletable(CleanupCandidate c) => c.Eligibility == CleanupEligibility.Evidence || (on && IsNil(c) && Row(c) == RowState.Delete);

        var space = inputs.Space;
        bool[] inRange;
        var cutoffIndex = -1;
        FreeSpaceShortfall? shortfall = null;
        switch (request.Mode)
        {
            case CleanupMode.BeforeDate:
                var before = request.Before ?? throw new ArgumentException("Before-date mode needs a picked date.", nameof(request));
                inRange = [.. ordered.Select(c => c.LocalDate < before)];     // strictly before; the chosen day is kept
                break;
            case CleanupMode.FreeSpace:
                throw new NotSupportedException("Free-space mode is added in Task 08.6.");
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "Unknown cleanup mode.");
        }

        var delete = ordered.Where((c, i) => inRange[i] && Deletable(c)).ToImmutableArray();
        var scope = ordered.Where((c, i) => inRange[i] && IsNil(c)).ToImmutableArray();
        var undecided = on ? scope.Where(c => Row(c) == RowState.Undecided).Select(c => c.Unit).ToImmutableHashSet()
                           : ImmutableHashSet<ItemId>.Empty;
        var kept = new List<CleanupKept>();
        for (var i = 0; i < ordered.Length; i++)
        {
            var c = ordered[i];
            if (!inRange[i] || Deletable(c)) continue;
            kept.Add(new CleanupKept(c.Unit, [.. c.Files.Select(f => CleanupPaths.Rel(f.RelPath))], c.AllocatedBytes, c.CaptureUtc,
                                     KeptReason(c, on, Row(c))));
        }
        kept.AddRange(LooseFiles(inputs, candidates));

        var cutoff = request.Mode == CleanupMode.BeforeDate
            ? BeforeDateCutoff(request.Before!.Value, ordered, delete)
            : FreeSpaceCutoff(ordered, cutoffIndex, delete);
        var source = inputs.Inventory.Source;
        var fileCount = delete.Sum(c => c.Files.Length);
        var allocatedBytes = delete.Sum(c => c.AllocatedBytes);
        var expectedFreeAfter = Math.Min(space.TotalBytes, space.FreeBytes + allocatedBytes);
        return new CleanupPlan(Guid.NewGuid().ToString("N"), source.Identity ?? inputs.Volume.Identity, source.Root,
            inputs.Inventory.InventoryHash, inputs.Inventory.CameraModel, request, space, delete, scope, rows, undecided,
            [.. kept], cutoff, fileCount, allocatedBytes, expectedFreeAfter, shortfall,
            CleanupFingerprint.Compute(request, space, delete));
    }

    private static string KeptReason(CleanupCandidate c, bool on, RowState row) => c.Eligibility switch
    {
        CleanupEligibility.NotInLibrary when !on => "not in your library: " + c.Reason,
        CleanupEligibility.NotInLibrary when row == RowState.Keep => (c.TickedForOffload ? "ticked for offload: " : "kept by you: ") + c.Reason,
        CleanupEligibility.NotInLibrary => "new in range, not decided yet: " + c.Reason,
        _ => c.Reason,
    };

    private static (int DeletedOnDay, int OnDay) DayCounts(ImmutableArray<CleanupCandidate> ordered,
                                                           ImmutableArray<CleanupCandidate> delete, DateOnly day)
        => (delete.Where(c => c.LocalDate == day).Sum(c => c.Files.Length),
            ordered.Where(c => c.LocalDate == day).Sum(c => c.Files.Length));

    private static CleanupCutoff BeforeDateCutoff(DateOnly before, ImmutableArray<CleanupCandidate> ordered,
                                                  ImmutableArray<CleanupCandidate> delete)
    {
        if (delete.IsEmpty) return new CleanupCutoff(before, null, null, null, 0, 0, null);
        var last = delete[^1];
        var (deletedOnDay, onDay) = DayCounts(ordered, delete, last.LocalDate);
        var deleted = delete.Select(c => c.Unit).ToHashSet();
        DateTime? continues = null;
        foreach (var d in delete)
        {
            if (d.Session is not { } s) continue;
            foreach (var c in ordered)
                if (!deleted.Contains(c.Unit) && c.LocalDate >= before && c.Session is { } t && s.SameSession(t)
                    && (continues is null || c.LocalTime < continues)) continues = c.LocalTime;
        }
        return new CleanupCutoff(before, last.CaptureUtc, last.LocalTime, last.TzId, deletedOnDay, onDay, continues);
    }

    private static CleanupCutoff FreeSpaceCutoff(ImmutableArray<CleanupCandidate> ordered, int cutoffIndex,
                                                 ImmutableArray<CleanupCandidate> delete)
    {
        if (cutoffIndex < 0) return new CleanupCutoff(null, null, null, null, 0, 0, null);
        var last = ordered[cutoffIndex];                                  // S_k (Ref §10.6 Mode 2 Cutoff)
        var (deletedOnDay, onDay) = DayCounts(ordered, delete, last.LocalDate);
        return new CleanupCutoff(null, last.CaptureUtc, last.LocalTime, last.TzId, deletedOnDay, onDay, null);
    }
}
