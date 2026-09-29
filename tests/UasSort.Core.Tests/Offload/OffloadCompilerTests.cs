using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class OffloadCompilerTests
{
    private const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private static readonly LibraryFolderRef ZFolder = new(@"C:\Users\u\OneDrive\Pictures\UAS Videos\" + ZRel, new DateOnly(2026, 9, 27), "Zachar Bay");

    [Fact]
    public void NewFolderGroup_OneJobPerIncludedNewVideo_InCaptureOrder()
    {
        var b = new OffloadPlanBuilder();
        var v2 = b.Video("DJI_20260927141000_0124_D.MP4", 2_000, T0.AddMinutes(10));
        var v1 = b.Video("DJI_20260927140000_0123_D.MP4", 1_000, T0);
        var imported = b.Video("DJI_20260927142000_0125_D.MP4", 3_000, T0.AddMinutes(20), new Imported(Evidence.LibraryNameSize, ZFolder, "listed"));
        var unticked = b.Video("DJI_20260927143000_0126_D.MP4", 4_000, T0.AddMinutes(30), included: false);
        b.Group(new NewFolder(ZRel), Zachar, v2, v1, imported, unticked);

        var batch = OffloadCompiler.Compile(b.Build(), "run-1");

        Assert.Equal("run-1", batch.RunId);
        Assert.Equal(OffloadPlanBuilder.Card, batch.Card);
        Assert.Equal([v1, v2], batch.Jobs.Select(j => j.Item).ToArray());
        var first = batch.Jobs[0];
        Assert.Equal(b.NewFolderPath(ZRel) + @"\DJI_20260927140000_0123_D.MP4", first.DestPath);
        Assert.Equal("DCIM/DJI_001/DJI_20260927140000_0123_D.MP4", first.CardRelPath);
        Assert.Equal(1_000, first.Size);
        Assert.Equal(DestRoot.Video, first.Root);
        Assert.Equal(new GroupId(v1), first.Group);
        Assert.True(first.CreatesFolder);
        var folder = Assert.Single(batch.Folders);
        Assert.Equal(b.NewFolderPath(ZRel), folder.FullPath);
        Assert.True(folder.Create);
        Assert.Equal("Zachar Bay", folder.Description);
        Assert.Equal(Zachar, folder.Centroid);
        Assert.Equal(Tz, folder.TzId);
        Assert.Empty(batch.SeenIfNotCopied);
    }

    [Fact]
    public void Append_TargetsTheExistingFolder_WithoutCreating()
    {
        var b = new OffloadPlanBuilder();
        var v = b.Video("DJI_20260927160000_0160_D.MP4", 1_000, T0.AddHours(2));
        b.Group(new Append(ZFolder, Confidence.High, "same day as clips already in this folder", null), Zachar, v);

        var job = Assert.Single(OffloadCompiler.Compile(b.Build(), "r").Jobs);

        Assert.Equal(ZFolder.FullPath + @"\DJI_20260927160000_0160_D.MP4", job.DestPath);
        Assert.False(job.CreatesFolder);
    }

    [Fact]
    public void SkipGroup_NothingToCopy_AndAlreadyImported_CopyNothing()
    {
        var b = new OffloadPlanBuilder();
        var a = b.Video("DJI_20260927140000_0001_D.MP4", 1, T0);
        var c = b.Video("DJI_20260928140000_0002_D.MP4", 1, T0.AddDays(1));
        var d = b.Video("DJI_20260929140000_0003_D.MP4", 1, T0.AddDays(2), new Imported(Evidence.LibraryNameSize, ZFolder, "listed"));
        b.Group(new SkipGroup(), Zachar, a).Group(new NothingToCopy("nothing to copy: 1 conflict"), null, c)
         .Group(new AlreadyImported(ZFolder), null, d);

        var batch = OffloadCompiler.Compile(b.Build(), "r");

        Assert.Empty(batch.Jobs);
        Assert.Empty(batch.Folders);
    }

    [Fact]
    public void AlreadyImported_WithCentroidAndNoLedgerFolder_GetsACardLeftoversFolderPlan()
    {
        var b = new OffloadPlanBuilder();
        var d = b.Video("DJI_20260927140000_0123_D.MP4", 1, T0, new Imported(Evidence.LibraryNameSize, ZFolder, "listed"));
        b.Group(new AlreadyImported(ZFolder), Zachar, d);

        var plan = Assert.Single(OffloadCompiler.Compile(b.Build(), "r").Folders);

        Assert.Equal(ZFolder.FullPath, plan.FullPath);
        Assert.False(plan.Create);
        Assert.Equal(Zachar, plan.Centroid);
        Assert.Empty(OffloadCompiler.Compile(new OffloadPlanBuilder().Also(x =>
        {
            var d2 = x.Video("DJI_20260927140000_0123_D.MP4", 1, T0, new Imported(Evidence.LibraryNameSize, ZFolder, "listed"));
            x.Group(new AlreadyImported(ZFolder), Zachar, d2).LedgerFolder(ZFolder.FullPath);
        }).Build(), "r").Folders);
    }

    [Fact]
    public void TickedConflictVideo_TakesTheNextFreeCopyNumber()
    {
        var b = new OffloadPlanBuilder();
        b.LibraryVideo(ZRel + @"\DJI_20260927140127_0123_D.MP4", 105_764_094)
         .LibraryVideo(ZRel + @"\DJI_20260927140127_0123_D (2).MP4", 50_000);
        var v = b.Video("DJI_20260927140127_0123_D.MP4", 7_340_032, T0,
                        new Conflict(ZFolder.FullPath + @"\DJI_20260927140127_0123_D.MP4", 105_764_094));
        b.Group(new Append(ZFolder, Confidence.High, "same day", null), Zachar, v);

        var job = Assert.Single(OffloadCompiler.Compile(b.Build(), "r").Jobs);

        Assert.Equal(ZFolder.FullPath + @"\DJI_20260927140127_0123_D (3).MP4", job.DestPath);
    }

    [Fact]
    public void Order_GroupsByStart_ThenPhotos_ThenSets()
    {
        var b = new OffloadPlanBuilder();
        var set = b.Set("001_0087", [("PANO_0002.DNG", 20), ("PANO_0001.DNG", 10)], T0.AddHours(-5), SetResolution.Plain);
        var photo = b.Photo("DJI_20260927100000_0001_D.DNG", 30, T0.AddHours(-6));
        var late = b.Video("DJI_20260928100000_0010_D.MP4", 40, T0.AddDays(1));
        var early = b.Video("DJI_20260927100000_0002_D.MP4", 50, T0.AddHours(-4));
        b.Group(new NewFolder(@"2026\2026-09\2026-09-28 Later"), null, late)
         .Group(new NewFolder(@"2026\2026-09\2026-09-27 Earlier"), null, early);

        var batch = OffloadCompiler.Compile(b.Build(), "r");

        Assert.Equal(
            ["DJI_20260927100000_0002_D.MP4", "DJI_20260928100000_0010_D.MP4", "DJI_20260927100000_0001_D.DNG", "PANO_0001.DNG", "PANO_0002.DNG"],
            batch.Jobs.Select(j => OffloadPaths.FileName(j.CardRelPath)).ToArray());
        Assert.Equal([photo, set], batch.SeenIfNotCopied.ToArray());
    }

    [Fact]
    public void PhotoWithTwin_TwoFlatJobsInThePhotoRoot()
    {
        var b = new OffloadPlanBuilder();
        b.Photo("DJI_20260927100000_0001_D.DNG", 30, T0, twinSize: 5);

        var batch = OffloadCompiler.Compile(b.Build(), "r");

        Assert.Equal([@"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload\DJI_20260927100000_0001_D.DNG", @"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload\DJI_20260927100000_0001_D.JPG"],
                     batch.Jobs.Select(j => j.DestPath).ToArray());
        Assert.All(batch.Jobs, j => { Assert.Equal(DestRoot.Photo, j.Root); Assert.Null(j.Group); Assert.False(j.CreatesFolder); });
    }

    [Fact]
    public void CopyJpgTwinOff_CopiesOnlyTheDng()
    {
        var b = new OffloadPlanBuilder().CopyJpgTwin(false);
        b.Photo("DJI_20260927100000_0001_D.DNG", 30, T0, twinSize: 5);

        var job = Assert.Single(OffloadCompiler.Compile(b.Build(), "r").Jobs);

        Assert.EndsWith(".DNG", job.DestPath, StringComparison.Ordinal);
    }

    [Fact]
    public void TickedPhotoConflict_TwinFollowsTheCopyNumber()
    {
        var b = new OffloadPlanBuilder();
        b.LibraryPhoto("DJI_20260927100000_0001_D.DNG", 99);
        b.Photo("DJI_20260927100000_0001_D.DNG", 30, T0, new Conflict(@"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload\DJI_20260927100000_0001_D.DNG", 99), twinSize: 5);

        var batch = OffloadCompiler.Compile(b.Build(), "r");

        Assert.Equal(["DJI_20260927100000_0001_D (2).DNG", "DJI_20260927100000_0001_D (2).JPG"],
                     batch.Jobs.Select(j => OffloadPaths.FileName(j.DestPath)).ToArray());
    }

    [Fact]
    public void ProbablyImported_CopiedOnlyWhenTicked_AndSeenOnlyWhenTicked()
    {
        var b = new OffloadPlanBuilder();
        var ticked = b.Photo("DJI_20260927100000_0001_D.DNG", 30, T0, new ProbablyImported("videos from this day are already in the library"));
        var left = b.Photo("DJI_20260927100500_0002_D.DNG", 30, T0.AddMinutes(5), new ProbablyImported("videos from this day are already in the library"), included: false);
        var newUnticked = b.Photo("DJI_20260927101000_0003_D.DNG", 30, T0.AddMinutes(10), included: false);
        var imported = b.Photo("DJI_20260927101500_0004_D.DNG", 30, T0.AddMinutes(15), new Imported(Evidence.LedgerVerified, null, "ledger"));

        var batch = OffloadCompiler.Compile(b.Build(), "r");

        Assert.Equal([ticked], batch.Jobs.Select(j => j.Item).ToArray());
        Assert.Equal([ticked, newUnticked], batch.SeenIfNotCopied.ToArray());
        Assert.DoesNotContain(left, batch.SeenIfNotCopied);
        Assert.DoesNotContain(imported, batch.SeenIfNotCopied);
    }

    [Fact]
    public void Sets_PlainCreatesItsFolder_ResumeCopiesOnlyMissing_ImportedIsSkipped()
    {
        var b = new OffloadPlanBuilder();
        b.Set("001_0087", [("PANO_0001.DNG", 10), ("PANO_0002.DNG", 20)], T0, SetResolution.DateSuffixed, "001_0087 2026-09-27");
        b.Set("001_0090", [("PANO_0001.DNG", 11), ("PANO_0002.DNG", 21)], T0.AddMinutes(1), SetResolution.Resume, membersToCopy: ["PANO_0002.DNG"]);
        b.Set("001_0091", [("PANO_0001.DNG", 12)], T0.AddMinutes(2), SetResolution.Imported);

        var jobs = OffloadCompiler.Compile(b.Build(), "r").Jobs;

        Assert.Equal(
            [@"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload\001_0087 2026-09-27\PANO_0001.DNG",
             @"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload\001_0087 2026-09-27\PANO_0002.DNG",
             @"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload\001_0090\PANO_0002.DNG"],
            jobs.Select(j => j.DestPath).ToArray());
        Assert.Equal([true, true, false], jobs.Select(j => j.CreatesFolder).ToArray());
    }
}

internal static class BuilderTestExtensions
{
    public static OffloadPlanBuilder Also(this OffloadPlanBuilder b, Action<OffloadPlanBuilder> arrange) { arrange(b); return b; }
}
