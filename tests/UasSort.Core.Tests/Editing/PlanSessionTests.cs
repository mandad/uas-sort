// tests/UasSort.Core.Tests/Editing/PlanSessionTests.cs
using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using UasSort.Core.Editing;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Editing;

public sealed class PlanSessionTests
{
    private static readonly RawItem C117 = Clip.Vid("20260725232655", 117, Sites.Council);
    private static readonly RawItem C118 = Clip.Vid("20260726022937", 118, Sites.Council);
    private static readonly RawItem A1 = Clip.Vid("20260726235645", 1, Sites.Anvil);
    private static readonly RawItem A2 = Clip.Vid("20260727000012", 2, Sites.Anvil);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlanBase Base(PlanScenario? s = null) => (s ?? new PlanScenario()).Card(C117, C118, A1, A2).Prepare();
    private static PlanSession Session(PlanBase b, IPlanDeriver? d = null) =>
        new(b, d ?? PlanScenario.CreatePlanner(), new Tuning(50, 1), new FakeTimeProvider(new DateTimeOffset(PlanScenario.NowUtc)));

    [Fact]
    public async Task Apply_DerivesNewPlan_RejectedChangesNothing()
    {
        var s = Session(Base());
        Assert.Single(s.Current.Groups);
        var r = await s.ApplyAsync(new SplitBefore(A1.Id()), Ct);
        Assert.True(r is Applied);
        Assert.Equal(2, s.Current.Groups.Length);
        Assert.Equal(1, s.UndoDepth);
        var before = PlanFingerprint.Of(s.Current);
        var rej = await s.ApplyAsync(new SplitBefore(A1.Id()), Ct);
        Assert.True(rej is Rejected { Reason: RejectReason.SplitAtGroupStart });
        Assert.Equal(before, PlanFingerprint.Of(s.Current));
        Assert.Equal(1, s.UndoDepth);
    }

    [Fact] // edits are serialised: the second is validated against the plan that includes the first
    public async Task ConcurrentEdits_AreSerialised()
    {
        var s = Session(Base());
        var t1 = s.ApplyAsync(new SplitBefore(A1.Id()), Ct);
        var t2 = s.ApplyAsync(new SplitBefore(A1.Id()), Ct);
        Assert.True(await t1 is Applied);
        Assert.True(await t2 is Rejected { Reason: RejectReason.SplitAtGroupStart });
    }

    [Fact] // a quick fix is one undo entry; undo then redo is the identity
    public async Task QuickFix_IsOneUndoEntry_UndoRedoIdentity()
    {
        var s = Session(Base());
        var original = PlanFingerprint.Of(s.Current);
        await s.ApplyAllAsync([new SplitBefore(A1.Id()), new Rename(A1.Id(), "Anvil Mountain", [A1.Id(), A2.Id()])], Ct);
        Assert.Equal(1, s.UndoDepth);
        Assert.True(s.CanUndo);          // Part 10's ReviewVm mirrors CanUndo/CanRedo (registry decision 39)
        Assert.False(s.CanRedo);
        var after = PlanFingerprint.Of(s.Current);
        Assert.Equal(original, PlanFingerprint.Of(await s.UndoAsync()));
        Assert.True(s.CanRedo);
        Assert.Equal(after, PlanFingerprint.Of(await s.RedoAsync()));
    }

    [Fact]
    public async Task Changed_RaisedOnThreadPool_WithIncreasingRevisions()
    {
        var s = Session(Base());
        var seen = new ConcurrentQueue<(bool Pool, int Rev)>();
        s.Changed += p => seen.Enqueue((Thread.CurrentThread.IsThreadPoolThread, p.Revision));
        await s.ApplyAsync(new SplitBefore(A1.Id()), Ct);
        await s.UndoAsync();
        var list = seen.ToList();
        Assert.Equal(2, list.Count);
        Assert.All(list, x => Assert.True(x.Pool));
        Assert.True(list[1].Rev > list[0].Rev);
    }

    [Fact] // a gated IPlanDeriver proves a slow earlier derive never overwrites a later one
    public async Task SlowEarlierPreview_NeverOverwritesLaterPreview()
    {
        var gated = new GatedPlanDeriver(PlanScenario.CreatePlanner());
        var s = Session(Base(), gated);
        var plans = new ConcurrentQueue<Plan>();
        s.Changed += plans.Enqueue;
        var gate = gated.Arm((t, _) => t.RadiusMiles == 30);
        var slow = s.PreviewAsync(new Tuning(30, 1));
        await gate.Entered;
        await s.PreviewAsync(new Tuning(40, 1));
        gate.Release();
        await slow;
        Assert.Equal(40, plans.Last().Tuning.RadiusMiles);
        Assert.DoesNotContain(plans, p => p.Tuning.RadiusMiles == 30);
        Assert.Equal(50, s.Current.Tuning.RadiusMiles);                 // previews never replace Current
    }

    [Fact] // a preview whose edit log was superseded by a completed edit is re-run on the new log
    public async Task SupersededPreview_IsRerunOnNewLog()
    {
        var gated = new GatedPlanDeriver(PlanScenario.CreatePlanner());
        var s = Session(Base(), gated);
        var plans = new ConcurrentQueue<Plan>();
        s.Changed += plans.Enqueue;
        var gate = gated.Arm((t, e) => t.RadiusMiles == 25 && e.Count == 0);
        var preview = s.PreviewAsync(new Tuning(25, 1));
        await gate.Entered;
        Assert.True(await s.ApplyAsync(new SplitBefore(A1.Id()), Ct) is Applied);
        gate.Release();
        await preview;
        var last = plans.Last();
        Assert.Equal(25, last.Tuning.RadiusMiles);
        Assert.Equal(BoundaryCause.UserSplit, Assert.Single(last.Boundaries).Cause);   // derived with the split in the log
        Assert.True(last.Revision > s.Current.Revision);
    }

    [Fact] // slider commit = one undo entry; nothing to commit = no entry
    public async Task CommitTuning_IsOneEntry()
    {
        var s = Session(Base());
        await s.CommitTuningAsync();
        Assert.Equal(0, s.UndoDepth);
        await s.PreviewAsync(new Tuning(30, 1));
        await s.PreviewAsync(new Tuning(25, 1));
        await s.CommitTuningAsync();
        Assert.Equal(1, s.UndoDepth);
        Assert.Equal(new Tuning(25, 1), s.CommittedTuning);
        Assert.Equal(2, s.Current.Groups.Length);
        await s.UndoAsync();
        Assert.Equal(new Tuning(50, 1), s.CommittedTuning);
    }

    [Fact] // [Accept and continue]: the Blocking parse issue becomes a RequiresAck Warning for this session
    public async Task AcceptLedgerIssues_DowngradesParseIssue()
    {
        var s = Session(Base(new PlanScenario().LedgerParseIssue("ledger-A.jsonl", 3, "bad")));
        Assert.Equal(IssueSeverity.Blocking, s.Current.Issues.Single(i => i.Code == IssueCode.LedgerParseIssue).Severity);
        await s.AcceptLedgerIssuesAsync();
        var i = s.Current.Issues.Single(x => x.Code == IssueCode.LedgerParseIssue);
        Assert.Equal((IssueSeverity.Warning, true), (i.Severity, i.RequiresAckAtPreflight));
        Assert.Equal(0, s.UndoDepth);
    }

    [Fact] // Ref §4.2 latest wins: a preview that snapshotted the flags before [Accept and continue] committed is re-derived with the accepted flags
    public async Task PreviewDuringAccept_PublishesAcceptedFlags()
    {
        var gated = new GatedPlanDeriver(PlanScenario.CreatePlanner());
        var s = Session(Base(new PlanScenario().LedgerParseIssue("ledger-A.jsonl", 3, "bad")), gated);
        var plans = new ConcurrentQueue<Plan>();
        s.Changed += plans.Enqueue;
        var acceptGate = gated.Arm((t, _) => t.RadiusMiles == 50);
        var previewGate = gated.Arm((t, _) => t.RadiusMiles == 30);
        var accept = s.AcceptLedgerIssuesAsync();
        await acceptGate.Entered;
        var preview = s.PreviewAsync(new Tuning(30, 1));                 // reads the flags while the accept derive is in flight
        await previewGate.Entered;
        acceptGate.Release();
        await accept;                                                    // the accept commits first ...
        previewGate.Release();
        await preview;                                                   // ... then the preview publishes
        var last = plans.Last();
        Assert.Equal(30, last.Tuning.RadiusMiles);
        var issue = last.Issues.Single(i => i.Code == IssueCode.LedgerParseIssue);
        Assert.Equal((IssueSeverity.Warning, true), (issue.Severity, issue.RequiresAckAtPreflight));
        Assert.Equal(IssueSeverity.Warning, s.Current.Issues.Single(i => i.Code == IssueCode.LedgerParseIssue).Severity);
    }

    [Fact] // drafts: resume replays the same plan; missing-item edits are counted as dropped
    public async Task Draft_ResumeReplays_AndCountsDropped()
    {
        var b = Base();
        var s = Session(b);
        await s.ApplyAsync(new SplitBefore(A1.Id()), Ct);
        await s.ApplyAsync(new Rename(A1.Id(), "Anvil Mountain", [A1.Id(), A2.Id()]), Ct);
        var draft = s.ToDraft();
        Assert.Equal((1, "vol-1A2B3C4D", "0123456789abcdef", PlanScenario.NowUtc), (draft.V, draft.CardKey, draft.InventoryHash, draft.SavedUtc));

        var resumed = PlanSession.Resume(Base(), draft, PlanScenario.CreatePlanner(), out var dropped);
        Assert.Equal(0, dropped);
        Assert.Equal(PlanFingerprint.Of(s.Current), PlanFingerprint.Of(resumed.Current));

        var withGone = draft with { Edits = [.. draft.Edits, new SplitBefore(new ItemId("DCIM/DJI_001/gone.MP4"))] };
        var r2 = PlanSession.Resume(Base(), withGone, PlanScenario.CreatePlanner(), out var dropped2);
        Assert.Equal(1, dropped2);
        var info = Assert.Single(r2.Current.Issues, i => i.Code == IssueCode.StaleEditsDropped);
        Assert.Equal((IssueSeverity.Info, "2 of 3 edits still apply"), (info.Severity, info.Message));
    }

    [Fact] // Ref §4.2 latest wins: a no-op commit after a newer preview of the committed tuning republishes the committed plan
    public async Task NoOpCommit_AfterOlderPreviewWasPublished_RepublishesCommittedPlan()
    {
        var gated = new GatedPlanDeriver(PlanScenario.CreatePlanner());
        var s = Session(Base(), gated);
        var plans = new ConcurrentQueue<Plan>();
        s.Changed += plans.Enqueue;
        await s.PreviewAsync(new Tuning(30, 1));
        var preview30 = plans.Last();
        Assert.Equal(30, preview30.Tuning.RadiusMiles);
        var gate = gated.Arm((t, _) => t.RadiusMiles == 50);
        var inFlight = s.PreviewAsync(new Tuning(50, 1));
        await gate.Entered;
        await s.CommitTuningAsync();
        gate.Release();
        await inFlight;
        Assert.Equal(new Tuning(50, 1), s.CommittedTuning);
        Assert.Equal(0, s.UndoDepth);                                    // still a no-op for undo
        var last = plans.Last();
        Assert.Equal(50, last.Tuning.RadiusMiles);
        Assert.True(last.Revision > preview30.Revision);
        Assert.Equal(s.Current.Revision, last.Revision);
    }

    [Fact] // a fire-and-forget preview whose derive throws raises Faulted once; Current is unchanged
    public async Task Preview_DeriveFault_RaisesFaultedOnce()
    {
        var boom = new InvalidOperationException("boom");
        var s = Session(Base(), new FaultingDeriver(PlanScenario.CreatePlanner(), (t, _) => t.RadiusMiles == 40 ? boom : null));
        var (faults, first) = Watch(s);
        var before = s.Current;
        s.Preview(new Tuning(40, 1));
        Assert.Same(boom, await first.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        await s.WhenIdleAsync();
        Assert.Same(boom, Assert.Single(faults));
        Assert.Same(before, s.Current);
    }

    [Fact] // [Accept and continue] whose derive throws: Faulted once, Current and the session flags unchanged
    public async Task AcceptLedgerIssues_DeriveFault_RaisesFaulted_FlagsAndCurrentUnchanged()
    {
        var boom = new InvalidOperationException("boom");
        var failing = 1;
        var s = Session(Base(new PlanScenario().LedgerParseIssue("ledger-A.jsonl", 3, "bad")),
                        new FaultingDeriver(PlanScenario.CreatePlanner(), (_, f) => f.LedgerIssuesAccepted && Volatile.Read(ref failing) == 1 ? boom : null));
        var (faults, first) = Watch(s);
        var before = s.Current;
        s.AcceptLedgerIssues();
        Assert.Same(boom, await first.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        await s.WhenIdleAsync();
        Assert.Same(boom, Assert.Single(faults));
        Assert.Same(before, s.Current);
        Volatile.Write(ref failing, 0);
        Assert.True(await s.ApplyAsync(new SplitBefore(A1.Id()), Ct) is Applied);   // the next derive still runs with the old flags
        Assert.Equal(IssueSeverity.Blocking, s.Current.Issues.Single(i => i.Code == IssueCode.LedgerParseIssue).Severity);
    }

    [Fact] // OperationCanceledException from a fire-and-forget derive is not a fault; each leg's observer is awaited before asserting
    public async Task CancelledFireAndForgetDerives_AreNotFaults()
    {
        var boom = new InvalidOperationException("boom");
        var accepted = 0;
        var previewCancelledByToken = 0;
        var previewEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var s = Session(Base(new PlanScenario().LedgerParseIssue("ledger-A.jsonl", 3, "bad")),
            new HookDeriver(PlanScenario.CreatePlanner(), (t, f, ct) =>
            {
                if (t.RadiusMiles == 30)
                {
                    previewEntered.TrySetResult();
                    ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));         // until a newer preview cancels this one's token
                    Volatile.Write(ref previewCancelledByToken, ct.IsCancellationRequested ? 1 : 0);
                    ct.ThrowIfCancellationRequested();
                }
                if (!f.LedgerIssuesAccepted) return;
                if (Interlocked.Increment(ref accepted) == 1) throw new OperationCanceledException();
                throw boom;
            }));
        var (faults, _) = Watch(s);
        var plans = new ConcurrentQueue<Plan>();
        s.Changed += plans.Enqueue;

        // preview leg: the in-flight derive is cancelled through its own token by a newer preview
        var preview30 = s.PreviewAsync(new Tuning(30, 1));
        var preview30Observed = s.ObserveAsync(preview30);
        await previewEntered.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        await s.PreviewAsync(new Tuning(40, 1));
        await preview30Observed.WaitAsync(TimeSpan.FromSeconds(30), Ct);   // barrier: the observer has finished
        Assert.Equal(1, Volatile.Read(ref previewCancelledByToken));
        Assert.True(preview30.IsCompletedSuccessfully);                   // a superseded preview ends quietly
        Assert.DoesNotContain(plans, p => p.Tuning.RadiusMiles == 30);
        Assert.Empty(faults);

        // accept leg: the derive's OperationCanceledException reaches the observer's cancellation branch
        var cancelledAccept = s.AcceptLedgerIssuesAsync();
        await s.ObserveAsync(cancelledAccept).WaitAsync(TimeSpan.FromSeconds(30), Ct);   // barrier: the observer has finished
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledAccept);
        Assert.Empty(faults);

        // control: the same observer does raise a real fault, once
        await s.ObserveAsync(s.AcceptLedgerIssuesAsync()).WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.Same(boom, Assert.Single(faults));
        Assert.Equal(2, Volatile.Read(ref accepted));
    }

    private static (ConcurrentQueue<Exception> Faults, TaskCompletionSource<Exception> First) Watch(PlanSession s)
    {
        var faults = new ConcurrentQueue<Exception>();
        var first = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Faulted += e => { faults.Enqueue(e); first.TrySetResult(e); };
        return (faults, first);
    }

    /// <summary>Throws the exception <paramref name="fault"/> returns for a derive, else delegates.</summary>
    private sealed class FaultingDeriver(IPlanDeriver inner, Func<Tuning, SessionFlags, Exception?> fault) : IPlanDeriver
    {
        public Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct) =>
            fault(t, flags) is { } e ? throw e : inner.Derive(b, t, edits, flags, revision, ct);
    }

    /// <summary>Runs <paramref name="hook"/> (which may block or throw) before each derive, then delegates.</summary>
    private sealed class HookDeriver(IPlanDeriver inner, Action<Tuning, SessionFlags, CancellationToken> hook) : IPlanDeriver
    {
        public Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct)
        {
            hook(t, flags, ct);
            return inner.Derive(b, t, edits, flags, revision, ct);
        }
    }
}
