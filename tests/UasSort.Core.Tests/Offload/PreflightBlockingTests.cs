// tests/UasSort.Core.Tests/Offload/PreflightBlockingTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class PreflightBlockingTests
{
    internal const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";

    internal static PreflightReport Check(OffloadRig rig)
        => Preflight.Check(rig.Batch, rig.Plan, rig.Files, rig.Fs, rig.Reader, rig.Ledger, rig.Lock, rig.Plan.Base.Scan.Settings);

    internal static OffloadRig NewFolderRig(long size = 1_000, Action<OffloadPlanBuilder>? arrange = null, bool cardFiles = true)
    {
        var b = new OffloadPlanBuilder();
        var v = b.Video("DJI_20260927140000_0123_D.MP4", size, T0);
        b.Group(new NewFolder(ZRel), Zachar, v);
        arrange?.Invoke(b);
        return new OffloadRig(b).Build(cardFiles);
    }

    private static Issue Only(PreflightReport r, IssueCode code) => Assert.Single(r.Issues, i => i.Code == code);

    [Fact]
    public void CleanBatch_CanStart_AndWritesNothing()
    {
        var rig = NewFolderRig();
        var before = rig.Fs.GuardLog.Count;

        var r = Check(rig);

        Assert.True(r.CanStart);
        Assert.Equal([rig.B.NewFolderPath(ZRel)], r.FoldersToCreate.ToArray());
        var vol = Assert.Single(r.Volumes);
        Assert.Equal(@"C:\", vol.Volume);
        Assert.Equal(1, vol.Files);
        Assert.Equal(1_000, vol.Bytes);
        Assert.Equal(1_000 + (1L << 30), vol.RequiredFree);
        Assert.Equal(before, rig.Fs.GuardLog.Count);          // no guarded operation at all: nothing opened or created
        Assert.Equal(0, rig.Lock.Holds);
    }

    [Fact]
    public void IdentityChanged_Blocks()
    {
        var rig = NewFolderRig();
        rig.Fs.SetCardIdentity(CardRoot, OffloadPlanBuilder.Card with { VolumeSerial = 0xDEADBEEF });

        var i = Only(Check(rig), IssueCode.CardIdentityChanged);

        Assert.Equal(IssueSeverity.Blocking, i.Severity);
        Assert.Equal("A different card is in E:; rescan", i.Message);
    }

    [Fact]
    public void CardTopLevelUnreadable_Blocks()
    {
        var rig = NewFolderRig();
        rig.Fs.Faults.EnumerationErrors[CardRoot] = 5;

        Assert.Equal("The card's top level can't be read", Only(Check(rig), IssueCode.CardUnreadable).Message);
    }

    [Fact]
    public void LockHeldElsewhere_Blocks()
    {
        var rig = NewFolderRig();
        rig.Lock.HeldElsewhere = true;

        Assert.Equal("Another uas-sort window is offloading", Only(Check(rig), IssueCode.OffloadLockHeld).Message);
    }

    [Fact]
    public void VideoRootMissing_Blocks_ButAnUntargetedPhotoRootDoesNot()
    {
        var rig = NewFolderRig();
        rig.Fs.RemoveUnguarded(rig.B.PhotoRoot);
        Assert.DoesNotContain(Check(rig).Issues, i => i.Code == IssueCode.RootMissing);

        rig.Fs.RemoveUnguarded(rig.B.VideoRoot);
        var i = Only(Check(rig), IssueCode.RootMissing);
        Assert.Equal($"{rig.B.VideoRoot} is not available; its items are unticked", i.Message);
        Assert.False(Check(rig).CanStart);
    }

    [Fact]
    public void RootsUnconfirmed_Blocks()
        => Assert.Equal(IssueSeverity.Blocking,
                        Only(Check(NewFolderRig(arrange: b => b.RootsConfirmed(false))), IssueCode.RootsUnconfirmed).Severity);

    [Theory]
    [InlineData(LedgerFolderState.CloudOnly, IssueCode.LedgerCloudOnly)]
    [InlineData(LedgerFolderState.Unwritable, IssueCode.LedgerUnwritable)]
    [InlineData(LedgerFolderState.VideoRootMissing, IssueCode.RootMissing)]
    public void LedgerFolderProblems_Block(LedgerFolderState state, IssueCode code)
    {
        var rig = NewFolderRig();
        rig.Ledger.StatusOverride = rig.Plan.Base.Scan.Ledger.Status with { State = state };

        var r = Check(rig);

        Assert.Equal(IssueSeverity.Blocking, Only(r, code).Severity);
        Assert.False(r.CanStart);
    }

    [Fact]
    public void MissingLedgerFolder_IsInfoOnly()
    {
        var rig = NewFolderRig();
        rig.Ledger.StatusOverride = rig.Plan.Base.Scan.Ledger.Status with { State = LedgerFolderState.Missing, Exists = false };

        var r = Check(rig);

        var i = Only(r, IssueCode.LedgerNoHistory);
        Assert.Equal(IssueSeverity.Info, i.Severity);
        Assert.Equal("No history yet; this offload starts it", i.Message);
        Assert.True(r.CanStart);
    }

    [Fact]
    public void LedgerFolderUnlistable_Blocks()
    {
        var rig = NewFolderRig();
        rig.Ledger.CheckThrows = true;

        Assert.Equal($@"Can't list {rig.B.VideoRoot}\.uas-sort", Only(Check(rig), IssueCode.LedgerUnlistable).Message);
    }

    [Fact]
    public void LedgerFolderListingError_BlocksAsUnlistable_NotNoHistory()
    {
        var b = new OffloadPlanBuilder();
        b.Group(new NewFolder(ZRel), Zachar, b.Video("DJI_20260927140000_0123_D.MP4", 1_000, T0));
        var rig = new OffloadRig(b).Build(ledgerOnFileSystem: true);
        var folder = rig.B.VideoRoot + @"\.uas-sort";
        rig.Fs.AddDirectory(folder);
        rig.Fs.Faults.EnumerationErrors[folder] = 362;                 // ERROR_CLOUD_FILE_PROVIDER_NOT_RUNNING

        var r = Check(rig);

        Assert.Equal(LedgerFolderState.Unlistable, rig.Ledger.Check().State);
        Assert.Equal((IssueSeverity.Blocking, $"Can't list {folder}"), (Only(r, IssueCode.LedgerUnlistable).Severity, Only(r, IssueCode.LedgerUnlistable).Message));
        Assert.DoesNotContain(r.Issues, i => i.Code == IssueCode.LedgerNoHistory);
        Assert.False(r.CanStart);
    }

    [Fact]
    public void AppendTargetGone_Blocks_AnchoredToTheGroup()
    {
        var b = new OffloadPlanBuilder();
        var folder = new LibraryFolderRef(b.NewFolderPath(ZRel), new DateOnly(2026, 9, 27), "Zachar Bay");
        var v = b.Video("DJI_20260927160000_0160_D.MP4", 1_000, T0.AddHours(2));
        b.Group(new Append(folder, Confidence.High, "same day as clips already in this folder", null), Zachar, v);
        var rig = new OffloadRig(b).Build();

        var i = Only(Check(rig), IssueCode.AppendTargetGone);

        Assert.Equal("'2026-09-27 Zachar Bay' was renamed or moved since the scan; rescan", i.Message);
        Assert.Equal(v, i.Anchor);
        rig.Fs.AddDirectory(folder.FullPath);
        Assert.DoesNotContain(Check(rig).Issues, x => x.Code == IssueCode.AppendTargetGone);
    }

    [Fact]
    public void DuplicateDestination_Blocks()
    {
        var rig = NewFolderRig();
        var copy = new ItemId("DCIM/DJI_001/copy.MP4");
        rig.Rebatch(rig.Batch with { Jobs = rig.Batch.Jobs.Add(rig.Batch.Jobs[0] with { Item = copy }) });

        var i = Only(Check(rig), IssueCode.DuplicateDestination);

        Assert.Equal(copy, i.Anchor);
        Assert.Equal($"Two files would be written to {rig.Batch.Jobs[0].DestPath}", i.Message);
    }

    [Fact]
    public void TempPathOver400Characters_Blocks()
    {
        var b = new OffloadPlanBuilder();
        var v = b.Video("DJI_20260927140000_0123_D.MP4", 1_000, T0);
        b.Group(new NewFolder(@"2026\2026-09\2026-09-27 " + new string('a', 330)), null, v);
        var rig = new OffloadRig(b).Build();

        var i = Only(Check(rig), IssueCode.TempPathTooLong);

        Assert.StartsWith("Path too long for OneDrive (437 > 400 characters): ", i.Message, StringComparison.Ordinal);
        Assert.Equal(v, i.Anchor);
    }

    [Fact]
    public void LowDiskSpace_UsesOneGiBOrTwoPercent()
    {
        var rig = NewFolderRig(size: 31_400_000_000, cardFiles: false);
        rig.Files.FreeBytesOverride = _ => 31_000_000_000;

        var r = Check(rig);

        Assert.Equal("C: needs 32.47 GB free; 31.00 GB available", Only(r, IssueCode.LowDiskSpace).Message);
        Assert.Equal(32_473_741_824, Assert.Single(r.Volumes).RequiredFree);
        rig.Files.FreeBytesOverride = _ => 32_473_741_824;
        Assert.DoesNotContain(Check(rig).Issues, i => i.Code == IssueCode.LowDiskSpace);

        var big = NewFolderRig(size: 100_000_000_000, cardFiles: false);
        Assert.Equal(102_000_000_000, Assert.Single(Check(big).Volumes).RequiredFree);
    }

    [Fact]
    public void PlanBlockingIssues_AreCarriedOnto_TheSheet()
    {
        var rig = NewFolderRig(arrange: b => b.Issue(new Issue(IssueSeverity.Blocking, IssueCode.EmptyFolderName, "Name this folder",
                                                               new ItemId("DCIM/DJI_001/DJI_20260927140000_0123_D.MP4"), [], false)));

        var r = Check(rig);

        Assert.Equal("Name this folder", Only(r, IssueCode.EmptyFolderName).Message);
        Assert.False(r.CanStart);
    }
}
