// tests/UasSort.Review.Tests/Fakes/RecordingActions.cs
namespace UasSort.Review.Tests;

internal sealed class RecordingActions : IReviewActions
{
    public List<string> Calls { get; } = [];
    public List<QuickFix> Fixes { get; } = [];
    public Task SetIncludedAsync(IReadOnlyList<ItemId> items, bool included) { Calls.Add($"include {items.Count} {included}"); return Task.CompletedTask; }
    public Task SplitBeforeAsync(ItemId first) { Calls.Add("split " + first.CardRelPath); return Task.CompletedTask; }
    public Task MergeAsync(ItemId inA, ItemId inB) { Calls.Add($"merge {inA.CardRelPath} {inB.CardRelPath}"); return Task.CompletedTask; }
    public Task ApplyQuickFixAsync(QuickFix fix) { Fixes.Add(fix); Calls.Add("fix " + fix.Label); return Task.CompletedTask; }
    public Task RenameAsync(GroupCardVm card, string text) { Calls.Add("rename " + text); return Task.CompletedTask; }
    public Task RetargetAsync(GroupCardVm card, RetargetOptionVm option) { Calls.Add("retarget " + option.Kind); return Task.CompletedTask; }
}
