// tests/UasSort.Review.Tests/CardScanStageTests.cs
namespace UasSort.Review.Tests;

public class CardScanStageTests
{
    private sealed class Volumes(params VolumeInfo[] v) : IVolumeProvider
    {
        public IReadOnlyList<VolumeInfo> GetVolumes() => v;
    }

    private static VolumeInfo Volume(string root, uint serial, string label, bool readOnly = false) =>
        new(root, new CardIdentity(serial, label, "exFAT", 256_060_514_304), "Removable", true, readOnly, false, true, 12_400_000_000,
            "Sd", true, false);

    private static CardSourceCheck Ok(string path, VolumeInfo? v)
        => new SourceOk(new CardSource(path, v?.Identity, v is null, v?.IsReadOnlyVolume ?? false));

    private static CardStageVm Stage(IVolumeProvider volumes, Func<string, VolumeInfo?, CardSourceCheck>? validate = null)
        => new(volumes, vs => [.. vs.Select(v => new CardCandidate(v, !v.Root.StartsWith('G'), v.Root.StartsWith('G') ? "not a DJI card" : null, 214))],
               validate ?? Ok);

    [Fact]
    public void CardStage_DroneOverUsbWithTwoDjiVolumes_ListsBothAndNeverAutoPicks()
    {
        var sd = Volume(@"E:\", 0x1A2B3C4D, "SD");
        var internalStorage = Volume(@"F:\", 0x5E6F7A8B, "DJI Internal");
        var vm = Stage(new Volumes(sd, internalStorage));
        var chosen = new List<CardSource>();
        vm.CardChosen += chosen.Add;

        Assert.Null(vm.Refresh());

        Assert.Equal(2, vm.Rows.Count);
        Assert.All(vm.Rows, r => Assert.True(r.IsDjiCard));
        Assert.Empty(chosen);
        Assert.Equal("2 DJI cards found. Pick one; each is offloaded on its own.", vm.StatusText);

        vm.Rows[1].UseCommand.Execute(null);
        var source = Assert.Single(chosen);
        Assert.Equal(0x5E6F7A8Bu, source.Identity!.VolumeSerial);
        Assert.Equal("vol-5E6F7A8B", source.DraftKey);
        Assert.NotEqual(new CardSource(@"E:\", sd.Identity, false, false).DraftKey, source.DraftKey);
    }

    [Fact]
    public void CardStage_SingleDjiCard_AutoAdvances()
    {
        var vm = Stage(new Volumes(Volume(@"E:\", 0x1A2B3C4D, "SD"), Volume(@"G:\", 0x11111111, "BACKUP")));
        CardSource? chosen = null;
        vm.CardChosen += s => chosen = s;

        var source = vm.Refresh();

        Assert.NotNull(source);
        Assert.Same(source, chosen);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("not a DJI card", vm.Rows[1].KindText);
    }

    [Fact]
    public void CardStage_WriteProtectedBadge_AndRefusalMessage()
    {
        var vm = Stage(new Volumes(Volume(@"E:\", 0x1A2B3C4D, "SD", readOnly: true), Volume(@"F:\", 2, "X")),
                       (path, v) => v is null ? new SourceRefused("This is part of your library (or a synced folder); uas-sort only offloads from cards.") : Ok(path, v));
        vm.Refresh();
        Assert.True(vm.Rows[0].IsWriteProtected);
        Assert.Equal(@"E:\ · SD · exFAT", vm.Rows[0].Text);
        Assert.Equal("DJI card · 214 media files · write-protected", vm.Rows[0].KindText);

        vm.Browse(@"C:\Lib\UAS Videos");
        Assert.Equal("This is part of your library (or a synced folder); uas-sort only offloads from cards.", vm.Message);
    }

    [Fact]
    public void CardStage_NoCard_ExplainsRescanAndBrowse()
    {
        var vm = Stage(new Volumes());
        Assert.Null(vm.Refresh());
        Assert.Equal("No DJI card found. Insert the card, then Rescan (F5), or browse to a folder.", vm.StatusText);
    }

    [Theory]
    [InlineData(ScanPhase.ListingCard, 0, 0, null, "Listing card")]
    [InlineData(ScanPhase.ListingLibrary, 0, 0, null, "Listing library (C:, D:)")]
    [InlineData(ScanPhase.ReadingLedger, 0, 2, null, "Reading ledger (2 PCs)")]
    [InlineData(ScanPhase.ReadingMetadata, 37, 214, "DJI_20260927140627_0128_D.MP4", "Reading metadata 37/214 · DJI_…0128")]
    [InlineData(ScanPhase.BuildingPlan, 0, 0, null, "Building plan")]
    public void ScanStage_PhaseTexts(ScanPhase phase, int done, int total, string? current, string expected)
    {
        var s = TestPlans.Settings() with { PreviousPhotoRoots = [@"D:\Old Photos"] };
        Assert.Equal(expected, ScanStageVm.PhaseTextFor(new ScanProgress(phase, done, total, current), s));
    }

    [Fact]
    public async Task ScanStage_Run_ReportsThroughDispatcherAndReturnsPlanBase()
    {
        var ui = new FakeUiDispatcher();
        var b = TestPlans.Base(TestPlans.Zachar());
        var vm = new ScanStageVm(async (src, progress, ct) =>
        {
            progress.Report(new ScanProgress(ScanPhase.ReadingMetadata, 1, 3, "DJI_20260927140127_0123_D.MP4"));
            await Task.Yield();
            return b.Scan;
        }, _ => b, TestPlans.Settings(), ui);

        var result = await vm.RunAsync(TestPlans.Source);
        ui.RunAll();

        Assert.Same(b, result);
        Assert.False(vm.IsRunning);
        Assert.Null(vm.ErrorText);
    }

    [Fact]
    public async Task ScanStage_CardRemoved_ShowsReinsertAndKeepsDraft()
    {
        var vm = new ScanStageVm((_, _, _) => Task.FromException<ScanResult>(new IOException("The device is not ready.")),
                                 _ => throw new InvalidOperationException("not reached"), TestPlans.Settings(), new FakeUiDispatcher());
        Assert.Null(await vm.RunAsync(TestPlans.Source));
        Assert.Equal("The card was removed or can't be read. Reinsert it, then Rescan. (The device is not ready.)", vm.ErrorText);
    }

    [Fact]
    public async Task ScanStage_Cancel_ReturnsNull()
    {
        var vm = new ScanStageVm(async (_, _, ct) => { await Task.Delay(Timeout.Infinite, ct); return null!; },
                                 _ => throw new InvalidOperationException("not reached"), TestPlans.Settings(), new FakeUiDispatcher());
        var run = vm.RunAsync(TestPlans.Source);
        vm.CancelCommand.Execute(null);
        Assert.Null(await run);
        Assert.Null(vm.ErrorText);
    }

    [Fact] // F10: access denied on the card (Failures.IsIo treats it as IO) is shown like a read error, never escapes
    public async Task ScanStage_AccessDenied_ShowsTheReadError()
    {
        var vm = new ScanStageVm((_, _, _) => Task.FromException<ScanResult>(new UnauthorizedAccessException("Access to the path 'E:\\DCIM' is denied.")),
                                 _ => throw new InvalidOperationException("not reached"), TestPlans.Settings(), new FakeUiDispatcher());
        Assert.Null(await vm.RunAsync(TestPlans.Source));
        Assert.Equal("The card was removed or can't be read. Reinsert it, then Rescan. (Access to the path 'E:\\DCIM' is denied.)", vm.ErrorText);
        Assert.False(vm.IsRunning);
    }
}