// tests/UasSort.Review.Tests/Fixtures/ReviewHarness.cs
namespace UasSort.Review.Tests;

internal sealed class ReviewHarness : IDisposable
{
    private ReviewHarness(PlanBase b, Tuning tuning, ImmutableArray<Suggestion> suggestions, DraftOffer? offer, FakeDraftStore? drafts)
    {
        Base = b;
        Deriver = new GatedPlanDeriver(new ScriptedDeriver(suggestions));
        Session = new PlanSession(b, Deriver, tuning, Time);
        Drafts = drafts ?? new FakeDraftStore();
        Services = Fake.Services(Ui, Time, Drafts, Dialogs, Log);
        Decisions = new LedgerDecisionService(Ledger, Time, "PC1");
        Vm = new ReviewVm(Session, Services, Decisions, offer);
    }

    public PlanBase Base { get; }
    public FakeUiDispatcher Ui { get; } = new();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 28, 2, 0, 0, TimeSpan.Zero));
    public FakeDialogService Dialogs { get; } = new();
    public ListLog Log { get; } = new();
    public FakeLedgerStore Ledger { get; } = Fake.Ledger();
    public FakeDraftStore Drafts { get; }
    public GatedPlanDeriver Deriver { get; }
    public PlanSession Session { get; }
    public ReviewServices Services { get; }
    public LedgerDecisionService Decisions { get; }
    public ReviewVm Vm { get; }

    public static ReviewHarness Create(IReadOnlyList<PlanClip> clips, Tuning? tuning = null, ImmutableArray<Suggestion> suggestions = default,
                                       LedgerSnapshot? ledger = null, IReadOnlyList<Item>? extraItems = null,
                                       ImmutableArray<PhotoDay> photoDays = default, DraftOffer? offer = null, FakeDraftStore? drafts = null,
                                       PlanBase? planBase = null)
        => new(planBase ?? TestPlans.Base(clips, ledger: ledger, extraItems: extraItems, photoDays: photoDays),
               tuning ?? new Tuning(), suggestions, offer, drafts);

    /// <summary>Runs posted UI work until the VM shows the session's latest plan.</summary>
    public Task SettleAsync() => Eventually.TrueAsync(() => Vm.Plan.Revision >= Session.Current.Revision && Ui.Pending == 0, Ui);

    public GroupCardVm Card(int index) => Vm.Videos.Timeline.OfType<GroupCardVm>().ElementAt(index);

    public void Dispose() => Vm.Dispose();
}
