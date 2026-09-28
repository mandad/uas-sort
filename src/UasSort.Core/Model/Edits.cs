using System.Text.Json.Serialization;

namespace UasSort.Core;

// Item-anchored, so they replay onto any clustering and onto a fresh scan; serialised in drafts (CoreJsonContext, Task 02.5).
[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(Merge), "merge")]
[JsonDerivedType(typeof(SplitBefore), "splitBefore")]
[JsonDerivedType(typeof(MoveToNewGroup), "moveToNewGroup")]
[JsonDerivedType(typeof(MoveToGroup), "moveToGroup")]
[JsonDerivedType(typeof(Rename), "rename")]
[JsonDerivedType(typeof(Retarget), "retarget")]
[JsonDerivedType(typeof(SetIncluded), "setIncluded")]
[JsonDerivedType(typeof(SetDayIncluded), "setDayIncluded")]
public closed record class PlanEdit;
public sealed record class Merge(ItemId InA, ItemId InB) : PlanEdit;
public sealed record class SplitBefore(ItemId First) : PlanEdit;
public sealed record class MoveToNewGroup(ImmutableArray<ItemId> Items) : PlanEdit;
public sealed record class MoveToGroup(ImmutableArray<ItemId> Items, ItemId InTarget) : PlanEdit;
public sealed record class Rename(ItemId InGroup, string? Description /* null = back to suggestion */,
                                  ImmutableArray<ItemId> PinnedMembers) : PlanEdit;
public sealed record class Retarget(ItemId InGroup, TargetChoice Choice, bool ConfirmedBeforeFolderDate,
                                    ImmutableArray<ItemId> PinnedMembers) : PlanEdit;
public sealed record class SetIncluded(ImmutableArray<ItemId> Items, bool Included) : PlanEdit;
public sealed record class SetDayIncluded(DateOnly Day, bool Included) : PlanEdit;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(AutoTarget), "auto")]
[JsonDerivedType(typeof(NewFolderTarget), "newFolder")]
[JsonDerivedType(typeof(AppendTo), "appendTo")]
[JsonDerivedType(typeof(SkipTarget), "skip")]
public closed record class TargetChoice;
public sealed record class AutoTarget() : TargetChoice;
public sealed record class NewFolderTarget() : TargetChoice;
public sealed record class AppendTo(string FolderFullPath) : TargetChoice;
public sealed record class SkipTarget() : TargetChoice;

public enum RejectReason
{
    MergeAcrossLibraryFolders, MoveImportedItem, SplitAtGroupStart, RenameExistingFolder,
    RetargetOutsideVideoRoot, RetargetIntoReservedFolder /* .uas-sort subtree, photo root, previousPhotoRoots */,
    RetargetLaterDatedFolderUnconfirmed, ItemsNotFound,
}
public sealed record Applied(Plan Plan);
public sealed record Rejected(RejectReason Reason, string Message);
public union EditResult(Applied, Rejected);
