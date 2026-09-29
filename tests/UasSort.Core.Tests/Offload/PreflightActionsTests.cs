using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Core.Tests.Offload.PreflightBlockingTests;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class PreflightActionsTests
{
    private static (OffloadRig Rig, ItemId Video, LibraryFolderRef Folder) AppendRig()
    {
        var b = new OffloadPlanBuilder();
        var folder = new LibraryFolderRef(b.NewFolderPath(ZRel), new DateOnly(2026, 9, 27), "Zachar Bay");
        var v = b.Video("DJI_20260927160000_0160_D.MP4", 1_000, T0.AddHours(2));
        b.Group(new Append(folder, Confidence.Medium, "different day, 34 mi from Council Road", null), Zachar, v);
        var rig = new OffloadRig(b).Build();
        rig.Fs.AddDirectory(folder.FullPath);
        return (rig, v, folder);
    }

    [Fact]
    public void StaleTemps_InDestinationDirectories_AreListedButNotDeleted()
    {
        var (rig, _, folder) = AppendRig();
        var stale = folder.FullPath + @"\DJI_20260927150000_0150_D.MP4.uas-sort.tmp";
        var elsewhere = rig.B.VideoRoot + @"\left.MP4.uas-sort.tmp";
        rig.Fs.AddFile(stale, 10, T0, 0x22);
        rig.Fs.AddFile(elsewhere, 10, T0, 0x22);

        var r = Check(rig);

        Assert.Equal([stale], r.StaleTemps.ToArray());
        var info = Assert.Single(r.Issues, i => i.Code == IssueCode.StaleTempFiles);
        Assert.Equal(IssueSeverity.Info, info.Severity);
        Assert.Equal("1 unfinished temp files from an earlier run will be deleted when the offload starts", info.Message);
        Assert.True(rig.Fs.Exists(stale));
        Assert.True(r.CanStart);
    }

    [Fact]
    public void SameSizeDestination_IsAlreadyThere_AndNeedsNoSpace()
    {
        var (rig, v, _) = AppendRig();
        rig.Fs.AddFile(rig.Batch.Jobs[0].DestPath, 1_000, T0);

        var r = Check(rig);

        Assert.Equal([v], r.AlreadyThere.ToArray());
        Assert.Equal(IssueSeverity.Info, Assert.Single(r.Issues, i => i.Code == IssueCode.DestinationAlreadyThere).Severity);
        Assert.Empty(r.Volumes);
        Assert.Equal([(rig.Batch.Folders[0].FullPath, Confidence.Medium)], r.FoldersAppended.ToArray());
    }

    [Fact]
    public void Warnings_CountWhatIsLeftOut_AndTickedAssumptions()
    {
        var b = new OffloadPlanBuilder();
        var ticked = b.Video("DJI_20260927140000_0001_D.MP4", 10, T0, flags: ItemFlags.CheckDate);
        var unticked = b.Video("DJI_20260927140100_0002_D.MP4", 10, T0.AddMinutes(1), included: false);
        var unfinished = b.Video("DJI_20260927140200_0003_D.MP4", 10, T0.AddMinutes(2), flags: ItemFlags.Truncated, included: false);
        var conflict = b.Video("DJI_20260927140300_0004_D.MP4", 10, T0.AddMinutes(3), new Conflict(@"C:\x\DJI_20260927140300_0004_D.MP4", 99), included: false);
        b.Photo("DJI_20260927140400_0005_D.DNG", 10, T0.AddMinutes(4), new ProbablyImported("videos from this day are already in the library"), included: false);
        b.Group(new NewFolder(ZRel), Zachar, ticked, unticked, unfinished, conflict);

        var r = Check(new OffloadRig(b).Build());

        string W(IssueCode c) => Assert.Single(r.Issues, i => i.Code == c && i.Severity == IssueSeverity.Warning).Message;
        Assert.Equal("1 ticked items rely on assumptions", W(IssueCode.AssumptionsTicked));
        Assert.Equal("1 probably-imported photos are left out", W(IssueCode.ProbablyImportedLeftOut));
        Assert.Equal("1 new items are unticked", W(IssueCode.NewItemsUnticked));
        Assert.Equal("1 unfinished recordings", W(IssueCode.UnfinishedRecordings));
        Assert.Equal("1 conflicts are left out", W(IssueCode.ConflictsLeftOut));
        Assert.True(r.CanStart);
    }

    [Fact]
    public void Acknowledgements_ComeFromTheRequiresAckIssues()
    {
        var anchor = new ItemId("DCIM/DJI_001/DJI_20260927140000_0123_D.MP4");
        var medium = new Issue(IssueSeverity.Warning, IssueCode.MediumAppend, "Appending to 'Council Road': different day, 34 mi", anchor, [], true);
        var split = new Issue(IssueSeverity.Warning, IssueCode.EmphasisedDaySplit, "Jul 25 → Jul 26 · 34 mi apart: likely separate outing", anchor, [], true);
        var info = new Issue(IssueSeverity.Info, IssueCode.SharedTarget, "Also targeted by another group", anchor, [], false);
        var rig = NewFolderRig(arrange: b => b.Issue(medium).Issue(split).Issue(info));

        var r = Check(rig);
        var required = PreflightAcks.Required(r);

        Assert.Equal([PreflightAcks.Key(medium), PreflightAcks.Key(split)], required.ToArray());
        Assert.DoesNotContain(r.Issues, i => i.Code == IssueCode.SharedTarget);
        Assert.False(PreflightAcks.CanStart(r, new HashSet<AckKey>()));
        Assert.False(PreflightAcks.CanStart(r, new HashSet<AckKey> { PreflightAcks.Key(medium) }));
        Assert.True(PreflightAcks.CanStart(r, required.ToHashSet()));

        rig.Lock.HeldElsewhere = true;
        Assert.False(PreflightAcks.CanStart(Check(rig), required.ToHashSet()));
    }
}
