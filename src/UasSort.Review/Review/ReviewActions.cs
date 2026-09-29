// src/UasSort.Review/Review/ReviewActions.cs
namespace UasSort.Review;

/// <summary>What row and card VMs ask the Review stage to do; ReviewVm implements it (Task 10.10).</summary>
public interface IReviewActions
{
    Task SetIncludedAsync(IReadOnlyList<ItemId> items, bool included);
    Task SplitBeforeAsync(ItemId first);
    Task MergeAsync(ItemId inA, ItemId inB);
    Task ApplyQuickFixAsync(QuickFix fix);
    Task RenameAsync(GroupCardVm card, string text);
#pragma warning disable CA1716 // brief-mandated parameter name (Task 10.8 interface): option
    Task RetargetAsync(GroupCardVm card, RetargetOptionVm option);
#pragma warning restore CA1716
}
