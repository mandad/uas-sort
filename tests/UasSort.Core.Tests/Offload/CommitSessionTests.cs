// tests/UasSort.Core.Tests/Offload/CommitSessionTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class CommitSessionTests
{
    internal const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";

    internal sealed class Harness
    {
        public Harness(OffloadRig rig)
        {
            Rig = rig;
            Env = new CommitEnvironment(rig.Reader, rig.Fs, rig.Ledger, rig.Lock, rig.Power, rig.Thumbnails, rig.Reports,
                new FakeVolumeProvider(rig.Volumes), dirs => Files = new FakeFileOps(rig.Fs, dirs), EngineRun.Clock(), FakeLayout.Machine, "0.1.0");
        }

        public OffloadRig Rig { get; }
        public CommitEnvironment Env { get; }
        public FakeFileOps Files { get; private set; } = null!;
        public CommitSession Begin() => CommitSession.Begin(Rig.Plan, Env, "run-1");
    }

    /// <summary>An Append group (existing folder with a stale temp) with one new video, plus an unticked new photo.</summary>
    internal static (Harness H, string Stale, string Folder) AppendWithStaleTemp(bool ledgerOnFileSystem = false, Action<OffloadPlanBuilder>? arrange = null)
    {
        var b = new OffloadPlanBuilder();
        var folder = new LibraryFolderRef(b.NewFolderPath(ZRel), new DateOnly(2026, 9, 27), "Zachar Bay");
        var v = b.Video("DJI_20260927160000_0160_D.MP4", 3_000, T0.AddHours(2));
        b.Group(new Append(folder, Confidence.High, "same day as clips already in this folder", null), Zachar, v);
        b.Photo("DJI_20260927160500_0161_D.DNG", 2_000, T0.AddHours(2).AddMinutes(5), included: false);
        arrange?.Invoke(b);
        var rig = new OffloadRig(b).Build(ledgerOnFileSystem: ledgerOnFileSystem);
        var stale = folder.FullPath + @"\DJI_20260927150000_0150_D.MP4.uas-sort.tmp";
        rig.Fs.AddFile(stale, 10, T0, 0x22);
        return (new Harness(rig), stale, folder.FullPath);
    }

    private static HashSet<AckKey> All(CommitSession s) => s.RequiredAcks.ToHashSet();

    [Fact]
    public void Begin_TakesTheLock_PausesThumbnails_AndChecksWithoutWriting_BackDeletesNothing()
    {
        var (h, stale, _) = AppendWithStaleTemp();
        var guardCalls = h.Rig.Fs.GuardLog.Count;

        var session = h.Begin();

        Assert.Equal((1, 1), (h.Rig.Lock.Holds, h.Rig.Thumbnails.ActivePauses));
        Assert.Equal([stale], session.Preflight.StaleTemps.ToArray());
        Assert.True(session.Preflight.CanStart);
        Assert.Equal(["Check"], h.Rig.Ledger.Calls);
        Assert.Equal(guardCalls, h.Rig.Fs.GuardLog.Count);

        session.Dispose();

        Assert.Equal((0, 0), (h.Rig.Lock.Holds, h.Rig.Thumbnails.ActivePauses));
        Assert.True(h.Rig.Fs.Exists(stale));
        Assert.Equal(guardCalls, h.Rig.Fs.GuardLog.Count);
    }

    [Fact]
    public async Task Start_IsRefused_UntilEveryAcknowledgementIsTicked()
    {
        var medium = new Issue(IssueSeverity.Warning, IssueCode.MediumAppend, "Appending to 'Zachar Bay': different day, 34 mi",
                               new ItemId("DCIM/DJI_001/DJI_20260927160000_0160_D.MP4"), [], true);
        var (h, _, _) = AppendWithStaleTemp(arrange: b => b.Issue(medium));
        using var session = h.Begin();

        Assert.Single(session.RequiredAcks);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.StartAsync(new HashSet<AckKey>(), new ListProgress<OffloadProgress>(), CancellationToken.None));
        var result = await session.StartAsync(All(session), new ListProgress<OffloadProgress>(), CancellationToken.None);
        Assert.IsType<Verified>(result.Offload.Outcomes[0]);
    }

    [Fact]
    public async Task Start_RunsTheSteps_InOrder_AndWritesTheLedgerTail()
    {
        var (h, stale, folder) = AppendWithStaleTemp();
        using var session = h.Begin();

        var result = await session.StartAsync(All(session), new ListProgress<OffloadProgress>(), CancellationToken.None);

        Assert.Equal(["Check", "EnsureFolder", "SnapshotToBackup run-1", "OpenOwn"], h.Rig.Ledger.Calls);
        Assert.False(h.Rig.Fs.Exists(stale));
        var calls = h.Files.Calls;
        Assert.True(calls.IndexOf("DeleteOwnTemp " + stale) < calls.FindIndex(c => c.StartsWith("CreateTemp ", StringComparison.Ordinal)));
        Assert.Equal(["FileRecord", "FolderRecord", "SeenRecord", "RunRecord"], h.Rig.Writer.Records.Select(r => r.GetType().Name).ToArray());
        Assert.Equal("appended", ((FolderRecord)h.Rig.Writer.Records[1]).Source);
        Assert.Equal("unticked", ((SeenRecord)h.Rig.Writer.Records[2]).Why);
        Assert.Equal(result.Verdict.Level.ToString(), ((RunRecord)h.Rig.Writer.Records[3]).Verdict);
        Assert.True(h.Rig.Writer.Disposed);
        Assert.Equal(["Offloading drone media"], h.Rig.Power.Reasons);
        Assert.Equal(0, h.Rig.Power.Active);
        Assert.Equal(VerdictLevel.NotSafe, result.Verdict.Level);           // the unticked new photo stays Unaccounted
        Assert.Same(h.Rig.Reports.Offload.Single(), result.Report);
        Assert.NotNull(result.ReportPath);
        Assert.True(result.LedgerComplete);
        Assert.True(result.FailureFree);
        Assert.True(h.Rig.Fs.Exists(folder + @"\DJI_20260927160000_0160_D.MP4"));
        h.Rig.Fs.AssertNoViolations();
    }

    [Fact]
    public async Task Cancel_StillWritesSeenAndRun()
    {
        var (h, _, _) = AppendWithStaleTemp(arrange: b => b.Photo("DJI_20260927161000_0162_D.DNG", 1_000, T0.AddHours(2).AddMinutes(10)));
        using var session = h.Begin();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await session.StartAsync(All(session), new ListProgress<OffloadProgress>(), cts.Token);

        Assert.Equal(StopReason.Cancelled, result.Offload.Stop);
        Assert.All(result.Offload.Outcomes, o => Assert.IsType<NotStarted>(o));
        var seen = h.Rig.Writer.Records.OfType<SeenRecord>().ToList();
        Assert.Equal(["unticked", "notStarted"], seen.Select(s => s.Why).ToArray());
        Assert.IsType<RunRecord>(h.Rig.Writer.Records[^1]);
        Assert.False(result.FailureFree);
        Assert.Equal(VerdictLevel.NotSafe, result.Verdict.Level);
    }

    [Fact]
    public async Task ALedgerFolderThatCannotBeCreated_CopiesNothing()
    {
        var (h, stale, folder) = AppendWithStaleTemp();
        h.Rig.Ledger.EnsureFolderThrows = true;
        using var session = h.Begin();

        var result = await session.StartAsync(All(session), new ListProgress<OffloadProgress>(), CancellationToken.None);

        Assert.Equal(StopReason.LedgerWriteFailed, result.Offload.Stop);
        Assert.All(result.Offload.Outcomes, o => Assert.IsType<NotStarted>(o));
        Assert.DoesNotContain("OpenOwn", h.Rig.Ledger.Calls);
        Assert.True(h.Rig.Fs.Exists(stale));
        Assert.False(h.Rig.Fs.Exists(folder + @"\DJI_20260927160000_0160_D.MP4"));
        Assert.False(result.LedgerComplete);
        Assert.Single(h.Rig.Reports.Offload);
    }

    [Fact]
    public async Task AnotherWindowHoldingTheLock_BlocksStart()
    {
        var (h, _, _) = AppendWithStaleTemp();
        h.Rig.Lock.HeldElsewhere = true;
        using var session = h.Begin();

        Assert.Contains(session.Preflight.Issues, i => i.Code == IssueCode.OffloadLockHeld && i.Severity == IssueSeverity.Blocking);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.StartAsync(All(session), new ListProgress<OffloadProgress>(), CancellationToken.None));
    }

    [Fact]
    public async Task ACleanOffload_IsSafe_AndFailureFree()
    {
        var b = new OffloadPlanBuilder();
        var v = b.Video("DJI_20260927140000_0123_D.MP4", 5_000, T0);
        b.Group(new NewFolder(ZRel), Zachar, v);
        var h = new Harness(new OffloadRig(b).Build());
        using var session = h.Begin();

        var result = await session.StartAsync(All(session), new ListProgress<OffloadProgress>(), CancellationToken.None);

        Assert.Equal(VerdictLevel.Safe, result.Verdict.Level);
        Assert.Equal("E: · DJI Air 3S · serial 1A2B-3C4D: Safe to format: 1 verified", result.Verdict.Headline);
        Assert.True(result.FailureFree);
        Assert.Equal("created", h.Rig.Writer.Records.OfType<FolderRecord>().Single().Source);
    }
}
