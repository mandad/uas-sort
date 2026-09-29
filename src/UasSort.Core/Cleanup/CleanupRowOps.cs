// src/UasSort.Core/Cleanup/CleanupRowOps.cs
namespace UasSort.Core.Cleanup;

/// <summary>Row decisions of the not-in-library review list (Ref §10.6; defined here). Build is re-run after each.</summary>
public static class CleanupRowOps
{
    public static CleanupRows Set(CleanupRows rows, ItemId unit, bool delete)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var keep = rows.Keep.Remove(unit);
        var del = rows.Delete.Remove(unit);
        return delete ? new CleanupRows(keep, del.Add(unit)) : new CleanupRows(keep.Add(unit), del);
    }

    /// <summary>[Delete all]: every review row to Delete, except "ticked for offload" rows, which keep their state
    /// (an undecided ticked row becomes Keep).</summary>
    public static CleanupRows DeleteAll(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var keep = plan.Rows.Keep.ToBuilder();
        var del = plan.Rows.Delete.ToBuilder();
        foreach (var c in plan.NotInLibraryInScope)
        {
            if (c.TickedForOffload)
            {
                if (plan.Undecided.Contains(c.Unit)) keep.Add(c.Unit);
                continue;
            }
            keep.Remove(c.Unit);
            del.Add(c.Unit);
        }
        return new CleanupRows(keep.ToImmutable(), del.ToImmutable());
    }

    /// <summary>[Keep all]: every review row to Keep.</summary>
    public static CleanupRows KeepAll(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var keep = plan.Rows.Keep.ToBuilder();
        var del = plan.Rows.Delete.ToBuilder();
        foreach (var c in plan.NotInLibraryInScope)
        {
            del.Remove(c.Unit);
            keep.Add(c.Unit);
        }
        return new CleanupRows(keep.ToImmutable(), del.ToImmutable());
    }
}
