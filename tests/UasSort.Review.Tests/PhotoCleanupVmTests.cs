// tests/UasSort.Review.Tests/PhotoCleanupVmTests.cs
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Review.Tests;

public sealed class PhotoCleanupVmTests
{
    private static readonly DateOnly Jun1 = new(2026, 6, 1), Jun2 = new(2026, 6, 2);
    private const string ReportFile = @"C:\AppData\uas-sort\reports\20261004-200000-photos01-photos.json";

    private sealed class Rig
    {
        public FakeUiDispatcher Ui { get; } = new();
        public FakeDialogService Dialogs { get; } = new();
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 4, 20, 0, 0, TimeSpan.Zero));
        public FakeShellLauncher Shell { get; } = new();
        public PhotoCleanupPreparation Preparation { get; set; } =
            new(new PhotoSurvey(PhotoRoot, [], ["notes.txt"], TestPlans.Ledger()), null, null, @"X:\Lightroom");
        public List<PhotoRow> Rows { get; } = [];
        public LightroomIndexSummary? Lightroom { get; set; }
        public List<PhotoCleanupRequest> Requests { get; } = [];
        public ConfirmedPhotoCleanupPlan? Confirmed { get; private set; }
        public PhotoCleanupReport? Report { get; private set; }
        public int Prepares { get; private set; }

        public PhotoCleanupVm Vm()
        {
            var engine = new PhotoCleanupEngine(
                (progress, ct) =>
                {
                    Prepares++;
                    return Task.FromResult(Preparation);
                },
                (survey, request, progress, ct) =>
                {
                    Requests.Add(request);
                    return Task.FromResult(Plan(request.Mode, request.Cutoff, Rows, notTouched: survey.NotTouched, lightroom: Lightroom));
                },
                (confirmed, progress, ct) =>
                {
                    Confirmed = confirmed;
                    ImmutableArray<PhotoCleanupOutcome> outcomes =
                        [.. confirmed.Items.Select(r => (PhotoCleanupOutcome)new PhotoRecycled(r.Key, r.Item.Members.Length, r.Item.Bytes))];
                    var at = Time.GetUtcNow().UtcDateTime;
                    return Task.FromResult(new PhotoCleanupResult("photos01", confirmed, outcomes, null, [], at, at));
                },
                r =>
                {
                    Report = r;
                    return ReportFile;
                },
                Shell);
            return new PhotoCleanupVm(engine, Dialogs, Ui, Time);
        }
    }

    [Fact]
    public async Task DateMode_EveryRowStartsDelete_GroupedByDay_WithTotals()
    {
        var rig = new Rig();
        rig.Rows.AddRange([Row(Photo("A.DNG", Jun1, twin: "A.JPG")), Row(Set("001_0087", Jun2, PhotoSetKind.Panorama, "PANO_0001.DNG", "PANO_0002.DNG"))]);
        using var vm = rig.Vm();
        await vm.OpenAsync();
        Assert.Equal(PhotoCleanupStep.Choose, vm.Step);
        Assert.False(vm.NextCommand.CanExecute(null));

        vm.PickDate(new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.FromHours(-8)));
        Assert.Equal(new DateOnly(2026, 6, 30), vm.Cutoff);
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(PhotoCleanupStep.Review, vm.Step);
        Assert.Equal(new PhotoCleanupRequest(PhotoCleanupMode.BeforeDate, new DateOnly(2026, 6, 30), null), rig.Requests.Single());
        Assert.All(vm.Rows, r => Assert.Equal(RowDecision.Delete, r.Decision));
        Assert.Equal(["Jun 1 (Mon)", "Jun 2 (Tue)"], vm.Rows.Select(r => r.DayHeader));
        Assert.Equal(["A.DNG + JPG", "Panorama · 001_0087"], vm.Rows.Select(r => r.Title));
        Assert.Equal("1 photo, 1 set, 59 MB to the Recycle Bin", vm.TotalsText);
        Assert.Equal("Not touched: 1 other file or folder in Picture Offload", vm.NotTouchedText);
    }

    [Fact]
    public async Task VerifyMode_UnverifiedRowsStartKeep_AndNeedTheSecondAcknowledgementOnlyWhenSetToDelete()
    {
        var rig = new Rig();
        rig.Rows.AddRange([Row(Photo("A.DNG", Jun1)), Row(Photo("B.DNG", Jun1), verified: false)]);
        using var vm = rig.Vm();
        await vm.OpenAsync();
        Assert.True(vm.VerifyEnabled);
        vm.Verify = true;
        vm.PickDate(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero));
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(new PhotoCleanupRequest(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), @"X:\Lightroom"), rig.Requests.Single());
        Assert.Equal([RowDecision.Delete, RowDecision.Keep], vm.Rows.Select(r => r.Decision));
        Assert.Equal("not found in Lightroom", vm.Rows[1].StatusText);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(PhotoCleanupStep.Confirm, vm.Step);
        Assert.False(vm.ShowUnverifiedAck);

        vm.BackCommand.Execute(null);
        vm.Rows[1].DeleteCommand.Execute(null);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.True(vm.ShowUnverifiedAck);
        Assert.Equal("Move 2 items (50 MB) from Picture Offload to the Recycle Bin", vm.AckMoveText);              // verify mode: no date-mode note
        Assert.Equal("1 of them are not confirmed in Lightroom or your library — the Recycle Bin may hold their only copy", vm.UnverifiedAckText);
        vm.AckMove = true;
        Assert.False(vm.RunCommand.CanExecute(null));
        vm.AckUnverified = true;
        Assert.True(vm.RunCommand.CanExecute(null));
        await vm.RunCommand.ExecuteAsync(null);
        Assert.True(rig.Confirmed!.Ack.UnverifiedIncluded);
        Assert.Equal(2, rig.Confirmed.Items.Length);
    }

    [Fact] // branch-2 ruling: what the Lightroom walk couldn't read is shown on the Review page, never swallowed
    public async Task VerifyMode_APartlyReadLightroomLibrary_IsShownOnTheReviewPage()
    {
        var rig = new Rig
        {
            Lightroom = new LightroomIndexSummary(@"X:\Lightroom", 0, [new LightroomFolderError(@"X:\Lightroom", 5)], []),
        };
        rig.Rows.Add(Row(Photo("A.DNG", Jun1), verified: false));
        using var vm = rig.Vm();
        await vm.OpenAsync();
        Assert.Null(vm.LightroomProblemText);
        vm.Verify = true;
        vm.PickDate(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero));
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(rig.Lightroom.Problem, vm.LightroomProblemText);
        Assert.StartsWith("Part of the Lightroom library couldn't be read: 1 library folder couldn't be listed", vm.LightroomProblemText, StringComparison.Ordinal);
        vm.BackCommand.Execute(null);
        rig.Lightroom = null;
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Null(vm.LightroomProblemText);
    }

    [Fact]
    public async Task Run_ConfirmsExactlyTheDeleteRows_SavesTheReport_AndShowsTheResult()
    {
        var rig = new Rig();
        rig.Rows.AddRange([Row(Photo("A.DNG", Jun1)), Row(Photo("B.DNG", Jun1))]);
        using var vm = rig.Vm();
        await vm.OpenAsync();
        vm.PickDate(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero));
        await vm.NextCommand.ExecuteAsync(null);
        vm.Rows[1].KeepCommand.Execute(null);
        Assert.Equal("1 photo, 0 sets, 25 MB to the Recycle Bin", vm.TotalsText);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal("Move 1 item (25 MB) from Picture Offload to the Recycle Bin — not checked against Lightroom", vm.AckMoveText);   // date mode
        Assert.False(vm.RunCommand.CanExecute(null));
        vm.AckMove = true;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(PhotoCleanupStep.Result, vm.Step);
        Assert.Equal(["A.DNG"], rig.Confirmed!.Items.Select(r => r.Key));
        Assert.Equal(["B.DNG"], rig.Confirmed.Kept.Select(r => r.Key));
        Assert.Equal("photos01", rig.Report!.RunId);
        Assert.Equal("Moved 1 item (1 file, 25 MB) to the Recycle Bin", vm.Result!.HeadlineText);
        Assert.Equal("moved 1 · kept 1 · skipped because changed 0 · failed 0", vm.Result.CountsText);
        vm.Result.OpenReportCommand.Execute(null);
        Assert.Equal(["file:" + ReportFile], rig.Shell.Opened);
        var closed = false;
        vm.Closed += () => closed = true;
        vm.DoneCommand.Execute(null);
        Assert.True(closed);
    }

    [Fact]
    public async Task ABlockedPreparation_ShowsItsReason_AndKeepOnDevicePreparesAgain()
    {
        var rig = new Rig();
        var pinned = 0;
        rig.Preparation = new PhotoCleanupPreparation(null, @"Set UAS Videos\.uas-sort to Always keep on this device", null, null)
        {
            KeepOnDevice = () => pinned++,
        };
        using var vm = rig.Vm();
        await vm.OpenAsync();
        Assert.Equal(@"Set UAS Videos\.uas-sort to Always keep on this device", vm.BlockingText);
        Assert.True(vm.CanKeepOnDevice);
        vm.PickDate(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero));
        Assert.False(vm.NextCommand.CanExecute(null));

        rig.Preparation = new PhotoCleanupPreparation(new PhotoSurvey(PhotoRoot, [], [], TestPlans.Ledger()), null, null, null);
        vm.KeepOnDeviceCommand.Execute(null);
        await Eventually.TrueAsync(() => vm.BlockingText is null && rig.Prepares == 2, rig.Ui);
        Assert.Equal(1, pinned);
    }

    [Fact]
    public async Task WithoutALightroomFolder_VerifyStaysOff_WithItsReason()
    {
        var rig = new Rig
        {
            Preparation = new(new PhotoSurvey(PhotoRoot, [], [], TestPlans.Ledger()), null,
                              "Set a Lightroom library folder in Settings to verify against Lightroom", null),
        };
        using var vm = rig.Vm();
        await vm.OpenAsync();
        Assert.False(vm.VerifyEnabled);
        vm.Verify = true;
        Assert.False(vm.Verify);
        Assert.Equal("Set a Lightroom library folder in Settings to verify against Lightroom", vm.VerifyUnavailableText);
    }

    [Theory]
    [InlineData(true, false, false, true, true, "Wait until the offload finishes")]
    [InlineData(false, false, true, true, true, "Wait until the card cleanup finishes")]
    [InlineData(false, true, false, true, true, "Wait until the scan finishes")]
    [InlineData(false, false, false, false, true, "Set up the photo folder first")]
    [InlineData(false, false, false, true, false, @"The photo folder C:\P is not available")]
    [InlineData(false, false, false, true, true, null)]
    public void Availability_FirstMatchingReason(bool commit, bool scan, bool cardCleanup, bool roots, bool available, string? reason)
        => Assert.Equal(reason, PhotoCleanupAvailability.Reason(new PhotoCleanupContext(commit, scan, cardCleanup, roots, @"C:\P", available)));
}
