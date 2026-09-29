namespace UasSort.Core.Planning;

/// <summary>Items an edit references (drop rule of Ref §8.9 step 2.1 and draft resume counting).</summary>
public static class PlanEditRefs
{
    public static IReadOnlyList<ItemId> Referenced(PlanEdit e) => e switch
    {
        Merge m => [m.InA, m.InB],
        SplitBefore s => [s.First],
        MoveToNewGroup mv => [.. mv.Items],
        MoveToGroup mt => [.. mt.Items, mt.InTarget],
        Rename r => [r.InGroup],
        Retarget rt => [rt.InGroup],
        SetIncluded si => [.. si.Items],
        SetDayIncluded => [],
    };

    public static bool IsStructural(PlanEdit e) => e is Merge or SplitBefore or MoveToNewGroup or MoveToGroup;
}
