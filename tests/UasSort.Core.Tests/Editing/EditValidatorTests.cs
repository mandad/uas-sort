// tests/UasSort.Core.Tests/Editing/EditValidatorTests.cs
using UasSort.Core.Editing;
using UasSort.Testing.Planning;
using static UasSort.Testing.Planning.DecisionScenarios;

namespace UasSort.Core.Tests.Editing;

public sealed class EditValidatorTests
{
    private static readonly RawItem C117 = Clip.Vid("20260725232655", 117, Sites.Council);
    private static readonly RawItem A1 = Clip.Vid("20260726235645", 1, Sites.Anvil);
    private static readonly RawItem A2 = Clip.Vid("20260727000012", 2, Sites.Anvil);

    private static RejectReason? Reason(Plan p, PlanEdit e) => EditValidator.Validate(p, e)?.Reason;

    [Fact]
    public void ItemsNotFound()
    {
        var p = PlanScenario.Derive(new PlanScenario().Card(Z).Prepare());
        Assert.Equal(RejectReason.ItemsNotFound, Reason(p, new SplitBefore(new ItemId("DCIM/DJI_001/nope.MP4"))));
        Assert.Null(Reason(p, new SetDayIncluded(new DateOnly(2026, 9, 27), false)));
    }

    [Fact]
    public void MergeAcrossWalls_IsRejected()
    {
        var am = Clip.Vid("20260801200000", 1, Sites.Anvil);
        var pm = Clip.Vid("20260802200000", 3, Sites.Anvil);
        var p = PlanScenario.Derive(new PlanScenario().Library(@"2026\2026-08\2026-08-01 Anvil AM", am)
            .Library(@"2026\2026-08\2026-08-02 Anvil PM", pm).Card(am, pm).Prepare());
        Assert.Equal(RejectReason.MergeAcrossLibraryFolders, Reason(p, new Merge(am.Id(), pm.Id())));
    }

    [Fact]
    public void MoveImported_AndSplitAtStart_AreRejected()
    {
        var n = Clip.Vid("20260927160000", 160, Sites.Zachar);
        var p = PlanScenario.Derive(ZLibrary().Card([.. Z, n]).Prepare());
        Assert.Equal(RejectReason.MoveImportedItem, Reason(p, new MoveToNewGroup([Z[1].Id()])));
        Assert.Equal(RejectReason.MoveImportedItem, Reason(p, new MoveToGroup([Z[1].Id()], n.Id())));
        Assert.Null(Reason(p, new MoveToNewGroup([n.Id()])));
        Assert.Equal(RejectReason.SplitAtGroupStart, Reason(p, new SplitBefore(Z[0].Id())));
        Assert.Null(Reason(p, new SplitBefore(n.Id())));
    }

    [Fact]
    public void RenameAppendOrAlreadyImported_IsRejected_NewFolderIsNot()
    {
        var appendPlan = PlanScenario.Derive(ZLibrary().Card([.. Z, Clip.Vid("20260927160000", 160, Sites.Zachar)]).Prepare());
        Assert.Equal(RejectReason.RenameExistingFolder, Reason(appendPlan, new Rename(Z[0].Id(), "x", [])));
        var imported = PlanScenario.Derive(ZLibrary().Card(Z).Prepare());
        Assert.Equal(RejectReason.RenameExistingFolder, Reason(imported, new Rename(Z[0].Id(), "x", [])));
        var fresh = PlanScenario.Derive(new PlanScenario().Card(Z).Prepare());
        Assert.Null(Reason(fresh, new Rename(Z[0].Id(), "Zachar Bay", [])));
    }

    [Theory]
    [InlineData(@"C:\lib\UAS Videos\.uas-sort", RejectReason.RetargetIntoReservedFolder)]
    [InlineData(@"C:\lib\UAS Videos\.uas-sort\x", RejectReason.RetargetIntoReservedFolder)]
    [InlineData(@"C:\lib\UAS Videos\Picture Offload", RejectReason.RetargetIntoReservedFolder)]
    [InlineData(@"C:\lib\UAS Videos\picture offload\sub", RejectReason.RetargetIntoReservedFolder)]
    [InlineData(@"D:\Old Offload", RejectReason.RetargetIntoReservedFolder)]
    [InlineData(@"D:\Elsewhere", RejectReason.RetargetOutsideVideoRoot)]
    [InlineData(@"C:\lib\UAS Videos\2026\2026-09\2026-09-28 Later", RejectReason.RetargetLaterDatedFolderUnconfirmed)]
    [InlineData(@"C:\lib\UAS Videos\2026\2026-09\2026-09-20 Earlier", null)]
    public void Retarget_Rules(string path, RejectReason? expected)
    {
        var b = new PlanScenario().PreviousPhotoRootFile(@"D:\Old Offload", "x.DNG", 1, PlanScenario.NowUtc).Card(Z).Prepare();
        var p = PlanScenario.Derive(b);
        Assert.Equal(expected, Reason(p, new Retarget(Z[0].Id(), new AppendTo(path), false, [])));
    }

    [Fact] // Ref §9: "Pick a folder inside UAS Videos" names the video root as the user typed it
    public void OutsideVideoRoot_MessageKeepsTheRootsCase()
    {
        var p = PlanScenario.Derive(new PlanScenario().Card(Z).Prepare());
        var r = EditValidator.Validate(p, new Retarget(Z[0].Id(), new AppendTo(@"G:\Elsewhere"), false, []));
        Assert.Equal((RejectReason.RetargetOutsideVideoRoot, "Pick a folder inside UAS Videos"), (r?.Reason, r?.Message));
    }

    [Fact] // a drive-root video root has no leaf name: the message names the root itself
    public void OutsideDriveRootVideoRoot_MessageNamesTheDrive()
    {
        var p = PlanScenario.Derive(new PlanScenario { Root = @"F:\" }.Card(Z).Prepare());
        var r = EditValidator.Validate(p, new Retarget(Z[0].Id(), new AppendTo(@"G:\Elsewhere"), false, []));
        Assert.Equal((RejectReason.RetargetOutsideVideoRoot, @"Pick a folder inside F:\"), (r?.Reason, r?.Message));
    }

    [Fact]
    public void LaterDatedFolder_AllowedWhenConfirmed()
    {
        var p = PlanScenario.Derive(new PlanScenario().Card(Z).Prepare());
        Assert.Null(Reason(p, new Retarget(Z[0].Id(), new AppendTo(@"C:\lib\UAS Videos\2026\2026-09\2026-09-28 Later"), true, [])));
    }

    [Fact]
    public void Merge_OfTwoGroupsWithoutWalls_IsValid()
    {
        var p = PlanScenario.Derive(new PlanScenario().Card(C117, A1, A2).Prepare(), new Tuning(25, 1));
        Assert.Null(Reason(p, new Merge(C117.Id(), A1.Id())));
    }
}
